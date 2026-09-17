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
            try
            {
                load = identity;
                localManifest = CitiesCompatibilityCollector.Collect();
                hostPolicy = new CompatibilityPolicy(localManifest.GameBuildHash, localManifest.SchemaHash,
                    localManifest.Entries, new ComponentFingerprint[0]);
                string[] hostErrors = hostPolicy.Evaluate(localManifest);
                if (hostErrors.Length != 0)
                {
                    string hostReason = string.Join(",", hostErrors);
                    if (hostReason.Length > 240) hostReason = hostReason.Substring(0, 240);
                    events.Record(RuntimeEventCode.Error, load.Generation, "host-compatibility-preflight:" + hostReason);
                    throw new InvalidOperationException("Host compatibility preflight rejected the local mod set: " + hostReason);
                }
                CompatibilityCapabilityReport capabilities = CompatibilityCapabilityReport.From(localManifest);
                events.Record(RuntimeEventCode.Enabled, load.Generation, "host-capabilities:" + capabilities.Summary());
                IAuthorityDomainV2[] domains = CreateHostDomains(load);
                authority = new AuthorityCoordinatorV2(new SessionStamp(load.WorldId, load.Epoch), domains);
                joins = new JoinCoordinator(MonotonicMilliseconds);
                hostLocalBinding = Guid.NewGuid();
                hostLocalMember = new MemberIdentity(Guid.NewGuid(), 1);
                if (!authority.RegisterConnection(hostLocalBinding, hostLocalMember, true, 1) || !authority.SetLive(hostLocalBinding, true))
                    throw new InvalidOperationException("Could not register Host local member.");
                server = new LiteNetServerTransport();
                if (!server.Start(port, roomKey)) throw new InvalidOperationException("Could not start LiteNet server.");
                if (!lifecycle.TryTransition(load, CitiesRuntimeRole.HostLive))
                    throw new InvalidOperationException("Could not enter HostLive runtime role.");
                lock (gate) { mode = MultiplayerSessionMode.Hosting; detail = "hosting-development-transport:" + port; }
            }
            catch (Exception error) { AbortStart("host-start:" + error.GetType().Name); }
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
            peer.BootstrapPhase = 3;
        }

        private void HandleHostSessionFrame(HostPeer peer, byte[] bytes)
        {
            SessionFrameV2 frame = SessionFrameCodecV2.Decode(bytes);
            SessionFrameValidatorV2.Validate(frame, authority.Stamp, peer.TransportId, peer.Sequences);
            switch (frame.Kind)
            {
                case MessageKindV2.Intent:
                    HandleHostIntent(peer, SessionMessagesV2.DecodeIntent(frame.Payload));
                    break;
                case MessageKindV2.JoinRequest:
                    HandleJoinRequest(peer, SessionMessagesV2.DecodeJoinRequest(frame.Payload));
                    break;
                case MessageKindV2.SnapshotProgress:
                    HandleSnapshotProgress(peer, SessionMessagesV2.DecodeSnapshotProgress(frame.Payload));
                    break;
                case MessageKindV2.BarrierAck:
                    HandleBarrierAck(peer, SessionMessagesV2.DecodeBarrierAck(frame.Payload));
                    break;
                case MessageKindV2.AppliedAck:
                    authority.RecordApplied(SessionMessagesV2.DecodeAppliedAck(frame.Payload));
                    break;
                default:
                    throw new InvalidOperationException("Client sent an unsupported session message to Host.");
            }
        }

        private void HandleHostIntent(HostPeer peer, PlayerIntentV2 intent)
        {
            AuthoritySubmitResultV2 result = authority.Submit(peer.TransportId, intent);
            if (result.Decision == AuthoritySubmitDecisionV2.Committed && result.Batch != null)
                BroadcastBatch(result.Batch);
            else if (result.Decision == AuthoritySubmitDecisionV2.Faulted)
                throw new InvalidOperationException("Authority domain faulted while applying client intent.");
        }

        private void RemoveHostPeer(HostPeer peer)
        {
            if (peer == null) return;
            if (joins != null && peer.Join.IsValid) joins.Cancel(peer.Join);
            try { if (peer.SnapshotCursor != null) peer.SnapshotCursor.Dispose(); } catch { }
            if (authority != null) authority.RemoveConnection(peer.TransportId);
            hostPeers.Remove(peer.TransportId);
        }
    }
}
