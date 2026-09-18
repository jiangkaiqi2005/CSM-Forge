using System;
using System.Collections.Generic;
using CsmForge.Checkpoints;
using CsmForge.Core;
using CsmForge.Protocol;
using CsmForge.Transport.LiteNet;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private void StartHostOnSimulation(LoadIdentity identity, int port, string roomKey, string displayName)
        {
            if (!lifecycle.IsCurrent(identity)) { SetOffline("stale-host-start"); return; }
            UnityEngine.Debug.Log("[CSM-Forge] host start entered; generation=" + identity.Generation + "; port=" + port + ".");
            try
            {
                load = identity;
                localManifest = CitiesCompatibilityCollector.Collect();
                hostPolicy = new CompatibilityPolicy(localManifest.GameBuildHash, localManifest.SchemaHash,
                    localManifest.Entries, new ComponentFingerprint[0]);
                IAuthorityDomainV2[] domains = CreateHostDomains(load);
                authority = new AuthorityCoordinatorV2(new SessionStamp(load.WorldId, load.Epoch), domains);
                joins = new JoinCoordinator(MonotonicMilliseconds);
                hostLocalBinding = Guid.NewGuid();
                hostLocalMember = new MemberIdentity(Guid.NewGuid(), 1);
                hostDisplayName = displayName;
                if (!authority.RegisterConnection(hostLocalBinding, hostLocalMember, true, 1) || !authority.SetLive(hostLocalBinding, true))
                    throw new InvalidOperationException("Could not register Host local member.");
                server = new LiteNetServerTransport();
                if (!server.Start(port, roomKey)) throw new InvalidOperationException("Could not start LiteNet server.");
                if (!lifecycle.TryTransition(load, CitiesRuntimeRole.HostLive))
                    throw new InvalidOperationException("Could not enter HostLive runtime role.");
                RefreshHostRoster();
                lock (gate) { mode = MultiplayerSessionMode.Hosting; detail = "hosting-development-transport:" + port; }
                UnityEngine.Debug.Log("[CSM-Forge] host started; generation=" + identity.Generation + "; port=" + port + ".");
            }
            catch (Exception error)
            {
                AbortStart("host-start:" + error.GetType().Name + ":" + error.Message);
            }
        }

        private void DrainServerEvents()
        {
            TransportEvent item;
            int processed = 0;
            while (processed++ < 128 && server.TryTake(out item))
            {
                if (item.Kind == TransportEventKind.Connected)
                {
                    if (hostPeers.Count >= Limits.Peers * 2) { server.Disconnect(item.ConnectionId); continue; }
                    hostPeers[item.ConnectionId] = new HostPeer { TransportId = item.ConnectionId };
                    continue;
                }
                HostPeer peer;
                if (item.ConnectionId == Guid.Empty || !hostPeers.TryGetValue(item.ConnectionId, out peer))
                {
                    if (item.Kind == TransportEventKind.Error) events.Record(RuntimeEventCode.Error, load.Generation, item.Detail);
                    continue;
                }
                if (item.Kind == TransportEventKind.Disconnected) { RemoveHostPeer(peer); continue; }
                if (item.Kind != TransportEventKind.Data) continue;
                try
                {
                    if (peer.SessionReady) HandleHostSessionFrame(peer, item.Payload);
                    else HandleHostBootstrap(peer, item.Payload);
                }
                catch (Exception error)
                {
                    events.Record(RuntimeEventCode.Error, load.Generation, "peer=" + peer.TransportId + ";" + error.GetType().Name);
                    if (authority != null && authority.IsFenced) { FenceSession("authority-world-fenced"); return; }
                    server.Disconnect(peer.TransportId);
                    RemoveHostPeer(peer);
                }
            }
        }

        private void HandleHostBootstrap(HostPeer peer, byte[] bytes)
        {
            BootstrapFrame frame = BootstrapCodec.Decode(bytes);
            if (peer.BootstrapPhase == 0 && frame.Kind == BootstrapKind.Hello)
            {
                HelloV2 hello = BootstrapMessagesV2.DecodeHello(frame.Payload);
                if (hello.AuthMode != BootstrapAuthMode.DevelopmentRoomKeyOnly)
                    throw new InvalidOperationException("This transport only supports explicitly-marked development authentication.");
                peer.Hello = hello; peer.BootstrapPhase = 1; return;
            }
            if (peer.BootstrapPhase == 1 && frame.Kind == BootstrapKind.IdentityProof)
            {
                BootstrapAuthMode proofMode; Guid instance;
                BootstrapMessagesV2.DecodeIdentityProof(frame.Payload, out proofMode, out instance);
                if (peer.Hello == null || proofMode != peer.Hello.AuthMode || instance != peer.Hello.ClientInstanceId)
                    throw new InvalidOperationException("Bootstrap identity continuity failed.");
                peer.BootstrapPhase = 2; return;
            }
            if (peer.BootstrapPhase == 2 && frame.Kind == BootstrapKind.ManifestPage)
            {
                ManifestPageV2 page = BootstrapMessagesV2.DecodeManifestPage(frame.Payload);
                if (peer.Hello == null || page.Total != peer.Hello.ManifestPageCount || page.Index != peer.NextManifestPage)
                    throw new InvalidOperationException("Manifest page sequence is invalid.");
                peer.ManifestEntries.AddRange(page.Entries); peer.NextManifestPage++;
                if (peer.NextManifestPage == peer.Hello.ManifestPageCount) FinishHostBootstrap(peer);
                return;
            }
            throw new InvalidOperationException("Bootstrap message arrived in an invalid phase.");
        }

        private void FinishHostBootstrap(HostPeer peer)
        {
            CompatibilityManifest remote = new CompatibilityManifest(peer.Hello.GameBuildHash, peer.Hello.SchemaHash,
                peer.ManifestEntries.ToArray());
            string[] errors = hostPolicy.Evaluate(remote);
            if (errors.Length != 0)
            {
                string reason = string.Join(",", errors);
                if (reason.Length > 200) reason = reason.Substring(0, 200);
                SendServerBootstrap(peer, BootstrapKind.CompatibilityResult,
                    BootstrapMessagesV2.EncodeCompatibilityResult(new CompatibilityResultV2(false, reason)));
                peer.BootstrapPhase = -1;
                return;
            }

            uint generation;
            if (!memberGenerations.TryGetValue(peer.Hello.ClientInstanceId, out generation)) generation = 0;
            if (generation == uint.MaxValue) throw new InvalidOperationException("Member generation exhausted.");
            generation++; memberGenerations[peer.Hello.ClientInstanceId] = generation;
            peer.Member = new MemberIdentity(peer.Hello.ClientInstanceId, generation);
            if (!authority.RegisterConnection(peer.TransportId, peer.Member, true, 1))
                throw new InvalidOperationException("Authority connection registration failed.");

            SendServerBootstrap(peer, BootstrapKind.CompatibilityResult,
                BootstrapMessagesV2.EncodeCompatibilityResult(new CompatibilityResultV2(true, string.Empty)));
            SendServerBootstrap(peer, BootstrapKind.SessionWelcome,
                BootstrapMessagesV2.EncodeWelcome(new SessionWelcomeV2(authority.Stamp, peer.TransportId,
                    peer.Member, 1, authority.Revision, authority.CurrentRoot)));
            peer.SessionReady = true;
            RefreshHostRoster();
            BroadcastRoster();
            QueueSnapshotFor(peer);
        }

        private void QueueSnapshotFor(HostPeer peer)
        {
            AuthorityBatch[] retained;
            if (publishedSnapshot != null && authority.TryReadJournal(publishedSnapshot.Revision, out retained))
            {
                StartSnapshotTransfer(peer, publishedSnapshot);
                return;
            }
            peer.WaitingSnapshot = true;
            if (snapshotSave == null)
            {
                SetSnapshotPause();
                snapshotSave = new CitiesSnapshotSaveOperation(load, authority.Revision, authority.CurrentRoot);
                snapshotSave.Start();
                lock (gate) detail = "capturing-snapshot";
            }
        }

        private void PollSnapshotSave()
        {
            if (snapshotSave == null) return;
            if (!snapshotSave.Poll()) return;
            publishedSnapshot = snapshotSave.Descriptor;
            if (!snapshotFiles.Contains(publishedSnapshot.Path)) snapshotFiles.Add(publishedSnapshot.Path);
            snapshotSave.Dispose(); snapshotSave = null;
            RestoreSnapshotPause();
            List<HostPeer> waiting = new List<HostPeer>();
            foreach (HostPeer peer in hostPeers.Values) if (peer.WaitingSnapshot) waiting.Add(peer);
            foreach (HostPeer peer in waiting) StartSnapshotTransfer(peer, publishedSnapshot);
            FlushDeferredIntents();
            lock (gate) detail = "snapshot-published:r" + publishedSnapshot.Revision;
        }

        private void StartSnapshotTransfer(HostPeer peer, SnapshotFileDescriptor snapshot)
        {
            if (peer.Join.IsValid) throw new InvalidOperationException("Peer already owns a JoinContext.");
            peer.Join = joins.StartJoin(peer.Member, peer.TransportId, snapshot.Revision, snapshot.Root);
            if (peer.SnapshotCursor != null) peer.SnapshotCursor.Dispose();
            peer.WaitingSnapshot = false;
            peer.TransferId = Guid.NewGuid();
            peer.SnapshotCursor = new SnapshotReadCursor(snapshot, peer.TransferId);
            peer.LastProgressOffset = 0;
            SendServerFrame(peer, MessageKindV2.SnapshotOffer,
                JoinMessagesV2.EncodeSnapshotOffer(new SnapshotOfferV2(peer.Join.JoinId, peer.Join.Generation,
                    snapshot.Revision, snapshot.Root, true, snapshot.SnapshotId, peer.TransferId,
                    (ulong)snapshot.Length, snapshot.ContentHash)));
        }

        private void PumpSnapshotTransfers()
        {
            if (snapshotSave != null) return;
            foreach (HostPeer peer in hostPeers.Values)
            {
                if (peer.SnapshotCursor == null) continue;
                if (server.ReliableQueuePackets(peer.TransportId) >= 64) continue;
                SnapshotChunkV2 chunk = peer.SnapshotCursor.ReadNext();
                if (chunk == null)
                {
                    peer.SnapshotCursor.Dispose(); peer.SnapshotCursor = null;
                    continue;
                }
                SendServerFrame(peer, MessageKindV2.SnapshotChunk, SnapshotTransferMessagesV2.EncodeChunk(chunk));
                if (peer.SnapshotCursor.Complete)
                {
                    peer.SnapshotCursor.Dispose(); peer.SnapshotCursor = null;
                }
            }
        }

        private void HandleHostSessionFrame(HostPeer peer, byte[] bytes)
        {
            SessionFrameV2 frame = SessionFrameCodecV2.Decode(bytes);
            if (!frame.Stamp.Equals(authority.Stamp) || frame.ConnectionBinding != peer.TransportId ||
                !peer.Sequences.Accept(frame.Lane, frame.Sequence))
                throw new InvalidOperationException("Host rejected session frame identity or sequence.");
            switch (frame.Kind)
            {
                case MessageKindV2.SnapshotProgress: HandleSnapshotProgress(peer, SnapshotTransferMessagesV2.DecodeProgress(frame.Payload)); break;
                case MessageKindV2.WorldInstalled: HandleWorldInstalled(peer, JoinMessagesV2.DecodeWorldInstalled(frame.Payload)); break;
                case MessageKindV2.BarrierAck: HandleBarrierAck(peer, JoinMessagesV2.DecodeBarrierAck(frame.Payload)); break;
                case MessageKindV2.Activated: HandleActivated(peer, JoinMessagesV2.DecodeActivated(frame.Payload)); break;
                case MessageKindV2.Intent: HandleRemoteIntent(peer, SessionMessagesV2.DecodeIntent(frame.Stamp, frame.Payload)); break;
                case MessageKindV2.AppliedAck:
                    if (!authority.RecordApplied(SessionMessagesV2.DecodeAppliedAck(frame.Stamp, peer.TransportId, frame.Payload)))
                        throw new InvalidOperationException("Applied acknowledgement was invalid for retained authority history.");
                    break;
                case MessageKindV2.GapRequest:
                    TrySendJournalOrResync(peer, ControlMessagesV2.DecodeGapRequest(frame.Payload).AfterRevision);
                    break;
                case MessageKindV2.ChatSubmit:
                    if (!peer.Live) throw new InvalidOperationException("Non-live peer submitted chat.");
                    ChatSubmitV2 chat = SocialMessagesV2.DecodeChatSubmit(frame.Payload);
                    PublishChat(new ChatEventV2(peer.Member, peer.Hello.DisplayName, chat.Text));
                    break;
                case MessageKindV2.PlayerPresentation:
                    if (!peer.Live) throw new InvalidOperationException("Non-live peer submitted presentation.");
                    PlayerPresentationV2 presence = SocialMessagesV2.DecodePresentation(frame.Payload);
                    PublishPresentation(new PlayerPresentationV2(peer.Member, peer.Hello.DisplayName, presence.ToolName,
                        presence.WorldX, presence.WorldY, presence.WorldZ, presence.Visible));
                    break;
                default: throw new InvalidOperationException("Client message is not valid in the current Host path.");
            }
        }

        private void HandleSnapshotProgress(HostPeer peer, SnapshotProgressV2 progress)
        {
            if (publishedSnapshot == null || progress.SnapshotId != publishedSnapshot.SnapshotId || progress.TransferId != peer.TransferId ||
                progress.NextOffset < peer.LastProgressOffset || progress.NextOffset > (ulong)publishedSnapshot.Length)
                throw new InvalidOperationException("Snapshot progress does not belong to this transfer.");
            peer.LastProgressOffset = progress.NextOffset;
        }

        private void HandleWorldInstalled(HostPeer peer, WorldInstalledV2 installed)
        {
            if (!peer.Join.IsValid || installed.JoinId != peer.Join.JoinId || installed.JoinGeneration != peer.Join.Generation ||
                !joins.MarkLoading(peer.Join) || !joins.MarkSnapshotInstalled(peer.Join, installed.Revision, installed.Root))
                throw new InvalidOperationException("WorldInstalled baseline did not match the join offer.");
            ulong h = authority.Revision; Hash256 root;
            if (!authority.TryGetRoot(h, out root)) throw new InvalidOperationException("Barrier root is unavailable.");
            if (!TrySendJournalOrResync(peer, installed.Revision)) return;
            ReplayBarrier barrier = joins.IssueBarrier(peer.Join, h, root, 30000);
            if (barrier == null) throw new InvalidOperationException("Could not issue fixed replay barrier.");
            SendServerFrame(peer, MessageKindV2.ReplayBarrier,
                JoinMessagesV2.EncodeReplayBarrier(new ReplayBarrierV2(peer.Join.JoinId, peer.Join.Generation,
                    barrier.BarrierId, barrier.Revision, barrier.Root)));
        }

        private void HandleBarrierAck(HostPeer peer, BarrierAckV2 ack)
        {
            if (ack.JoinId != peer.Join.JoinId || ack.JoinGeneration != peer.Join.Generation ||
                !joins.ReportApplied(peer.Join, ack.Revision, ack.Root) ||
                !joins.AcknowledgeBarrier(peer.Join, ack.BarrierId, ack.Revision, ack.Root))
                throw new InvalidOperationException("Barrier acknowledgement failed.");
            ulong a = authority.Revision; Hash256 root;
            if (!authority.TryGetRoot(a, out root)) throw new InvalidOperationException("Activation root is unavailable.");
            if (!TrySendJournalOrResync(peer, ack.Revision)) return;
            ActivationGrant grant = joins.IssueActivation(peer.Join, a, root, 1, 30000);
            if (grant == null) throw new InvalidOperationException("Could not issue activation grant.");
            peer.StateSubscribed = true;
            SendServerFrame(peer, MessageKindV2.ActivationGrant,
                JoinMessagesV2.EncodeActivationGrant(new ActivationGrantV2(peer.Join.JoinId, peer.Join.Generation,
                    grant.GrantId, grant.Revision, grant.Root, grant.PermissionVersion)));
        }

        private void HandleActivated(HostPeer peer, ActivatedV2 activated)
        {
            JoinSnapshot state = joins.Inspect(peer.Join);
            if (state == null || state.Grant == null || activated.JoinId != peer.Join.JoinId ||
                activated.JoinGeneration != peer.Join.Generation || activated.GrantId != state.Grant.GrantId ||
                !joins.ReportApplied(peer.Join, activated.Revision, state.Grant.Root) ||
                !joins.Activate(peer.Join, activated.GrantId, activated.Revision, state.Grant.Root) ||
                !authority.SetLive(peer.TransportId, true))
                throw new InvalidOperationException("Client activation failed.");
            peer.Live = true;
            RefreshHostRoster();
            BroadcastRoster();
        }

        private void HandleRemoteIntent(HostPeer peer, PlayerIntentV2 intent)
        {
            if (!peer.Live) throw new InvalidOperationException("Non-live peer submitted a player intent.");
            if (snapshotSave != null)
            {
                if (peer.DeferredIntents.Count >= 32) throw new InvalidOperationException("Peer exceeded deferred intent budget during snapshot capture.");
                peer.DeferredIntents.Enqueue(intent);
                return;
            }
            SubmitRemoteIntent(peer, intent);
        }

        private void SubmitRemoteIntent(HostPeer peer, PlayerIntentV2 intent)
        {
            AuthoritySubmitResultV2 result = authority.Submit(peer.TransportId, intent);
            if (result.Decision == AuthoritySubmitDecisionV2.Faulted || authority.IsFenced)
            { FenceSession("authority-domain-fault"); return; }
            SendServerFrame(peer, MessageKindV2.IntentReceipt,
                ControlMessagesV2.EncodeReceipt(new IntentReceiptV2(intent.OperationCounter, result.Decision,
                    result.Batch == null ? 0 : result.Batch.Revision)));
            if (result.Decision == AuthoritySubmitDecisionV2.Committed && result.Batch != null) BroadcastBatch(result.Batch);
        }

        private void FlushDeferredIntents()
        {
            foreach (HostPeer peer in hostPeers.Values)
                while (peer.DeferredIntents.Count > 0 && peer.Live)
                    SubmitRemoteIntent(peer, peer.DeferredIntents.Dequeue());
        }

        private bool TrySendJournalOrResync(HostPeer peer, ulong afterRevision)
        {
            AuthorityBatch[] batches;
            if (!authority.TryReadJournal(afterRevision, out batches))
            {
                BeginPeerResync(peer);
                return false;
            }
            foreach (AuthorityBatch batch in batches)
                SendServerFrame(peer, MessageKindV2.AuthorityBatch, SessionMessagesV2.EncodeBatch(batch));
            return true;
        }

        private void BeginPeerResync(HostPeer peer)
        {
            authority.SetLive(peer.TransportId, false);
            peer.Live = false;
            peer.StateSubscribed = false;
            peer.DeferredIntents.Clear();
            if (peer.SnapshotCursor != null)
            {
                peer.SnapshotCursor.Dispose();
                peer.SnapshotCursor = null;
            }
            if (peer.Join.IsValid) joins.Cancel(peer.Join);
            peer.Join = default(JoinIdentity);
            peer.TransferId = Guid.Empty;
            peer.LastProgressOffset = 0;
            QueueSnapshotFor(peer);
            lock (gate) detail = "peer-resync-snapshot:" + peer.TransportId;
        }

        private void RemoveHostPeer(HostPeer peer)
        {
            hostPeers.Remove(peer.TransportId);
            if (peer.SnapshotCursor != null) { peer.SnapshotCursor.Dispose(); peer.SnapshotCursor = null; }
            if (peer.Join.IsValid) joins.Cancel(peer.Join);
            authority.Disconnect(peer.TransportId);
            lock (gate) if (peer.Member.IsValid) presentationSnapshots.Remove(peer.Member.MemberId);
            RefreshHostRoster();
            BroadcastRoster();
        }

        private void RefreshHostRoster()
        {
            List<MultiplayerPlayerSnapshot> values = new List<MultiplayerPlayerSnapshot>();
            if (hostLocalMember.IsValid) values.Add(new MultiplayerPlayerSnapshot
            { Member = hostLocalMember, DisplayName = hostDisplayName, IsHost = true, IsLive = true, IsLocal = true });
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.Member.IsValid && peer.Hello != null) values.Add(new MultiplayerPlayerSnapshot
                { Member = peer.Member, DisplayName = peer.Hello.DisplayName, IsHost = false, IsLive = peer.Live, IsLocal = false });
            lock (gate) playerSnapshots = values.ToArray();
        }

        private void BroadcastRoster()
        {
            List<SessionPlayerV2> values = new List<SessionPlayerV2>();
            if (hostLocalMember.IsValid) values.Add(new SessionPlayerV2(hostLocalMember, hostDisplayName,
                SessionPlayerRoleV2.Host, SessionPlayerPhaseV2.Live));
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.Member.IsValid && peer.Hello != null) values.Add(new SessionPlayerV2(peer.Member, peer.Hello.DisplayName,
                    SessionPlayerRoleV2.Client, peer.Live ? SessionPlayerPhaseV2.Live : SessionPlayerPhaseV2.Joining));
            if (values.Count == 0) return;
            byte[] payload = SocialMessagesV2.EncodeRoster(new RosterSnapshotV2(values.ToArray()));
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.SessionReady) SendServerFrame(peer, MessageKindV2.RosterSnapshot, payload);
        }

        private void ExpireHostJoins()
        {
            List<Guid> expired = new List<Guid>();
            foreach (KeyValuePair<Guid, HostPeer> pair in hostPeers)
            {
                HostPeer peer = pair.Value;
                if (!peer.Join.IsValid || peer.Live) continue;
                JoinSnapshot state = joins.Inspect(peer.Join);
                if (state != null && state.Phase == JoinPhase.RecoveryPending) expired.Add(pair.Key);
            }
            foreach (Guid id in expired) server.Disconnect(id);
        }
    }
}
