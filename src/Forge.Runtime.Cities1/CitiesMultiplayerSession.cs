using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using CsmForge.Core;
using CsmForge.Protocol;
using CsmForge.Transport.LiteNet;

namespace CsmForge.Runtime.Cities1
{
    public enum MultiplayerSessionMode
    {
        Offline,
        StartingHost,
        Hosting,
        ConnectingClient,
        ClientCatchingUp,
        ClientLive,
        Faulted
    }

    public sealed class MultiplayerStatusSnapshot
    {
        public MultiplayerSessionMode Mode { get; internal set; }
        public string Detail { get; internal set; }
        public int ConnectedPeers { get; internal set; }
        public ulong Revision { get; internal set; }
        public bool DevelopmentTransport { get; internal set; }
    }

    public sealed class CitiesMultiplayerSession
    {
        private sealed class HostPeer
        {
            public Guid TransportId;
            public int BootstrapPhase;
            public HelloV2 Hello;
            public readonly List<ComponentFingerprint> ManifestEntries = new List<ComponentFingerprint>();
            public ushort NextManifestPage;
            public MemberIdentity Member;
            public JoinIdentity Join;
            public LaneSequenceTracker Sequences = new LaneSequenceTracker();
            public bool SessionReady;
            public bool StateSubscribed;
            public bool Live;
        }

        private readonly object gate = new object();
        private readonly CitiesLifecycleCoordinator lifecycle;
        private readonly RuntimeEventLog events;
        private readonly Queue<WaterBudgetIntent> pendingBudget = new Queue<WaterBudgetIntent>();
        private readonly Dictionary<Guid, HostPeer> hostPeers = new Dictionary<Guid, HostPeer>();
        private readonly Dictionary<Guid, uint> memberGenerations = new Dictionary<Guid, uint>();

        private MultiplayerSessionMode mode = MultiplayerSessionMode.Offline;
        private string detail = "offline";
        private LoadIdentity load;
        private CompatibilityManifest localManifest;
        private CompatibilityPolicy hostPolicy;
        private LiteNetServerTransport server;
        private LiteNetClientTransport client;
        private AuthorityCoordinatorV2 authority;
        private ReplicaCoordinatorV2 replica;
        private JoinCoordinator joins;
        private WaterBudgetAuthorityDomain hostWater;
        private WaterBudgetReplicaDomain clientWater;
        private Guid hostLocalBinding;
        private MemberIdentity hostLocalMember;
        private ulong hostLocalOperation;
        private readonly Guid clientInstanceId = Guid.NewGuid();
        private string clientName;
        private ManifestPageV2[] clientManifestPages;
        private bool clientCompatibilityAccepted;
        private bool clientSessionReady;
        private SessionStamp clientStamp;
        private Guid clientBinding;
        private MemberIdentity clientMember;
        private ulong clientPermissionVersion;
        private ulong clientOperation;
        private LaneSequenceTracker clientSequences;
        private SnapshotOfferV2 clientOffer;

        public CitiesMultiplayerSession(CitiesLifecycleCoordinator lifecycle, RuntimeEventLog events)
        {
            if (lifecycle == null || events == null) throw new ArgumentNullException("lifecycle");
            this.lifecycle = lifecycle;
            this.events = events;
        }

        public MultiplayerStatusSnapshot Status
        {
            get
            {
                lock (gate)
                    return new MultiplayerStatusSnapshot
                    {
                        Mode = mode,
                        Detail = detail,
                        ConnectedPeers = hostPeers.Count,
                        Revision = authority != null ? authority.Revision : replica != null ? replica.Revision : 0,
                        DevelopmentTransport = mode != MultiplayerSessionMode.Offline
                    };
            }
        }

        public bool RequestHost(int port, string roomKey, string displayName)
        {
            LoadIdentity identity = lifecycle.Current;
            if (!identity.IsValid || string.IsNullOrEmpty(roomKey) || string.IsNullOrEmpty(displayName)) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Offline) return false;
                mode = MultiplayerSessionMode.StartingHost;
                detail = "host-start-queued";
            }
            if (RuntimeServices.Scheduler.QueueSimulation(identity,
                delegate { StartHostOnSimulation(identity, port, roomKey, displayName); })) return true;
            SetOffline("host-start-queue-failed");
            return false;
        }

        public bool RequestJoinCurrentWorld(IPEndPoint endpoint, string roomKey, string displayName)
        {
            LoadIdentity identity = lifecycle.Current;
            if (!identity.IsValid || endpoint == null || string.IsNullOrEmpty(roomKey) || string.IsNullOrEmpty(displayName)) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Offline) return false;
                mode = MultiplayerSessionMode.ConnectingClient;
                detail = "client-start-queued";
            }
            if (RuntimeServices.Scheduler.QueueSimulation(identity,
                delegate { StartClientOnSimulation(identity, endpoint, roomKey, displayName); })) return true;
            SetOffline("client-start-queue-failed");
            return false;
        }

        public bool RequestStop()
        {
            LoadIdentity identity = lifecycle.Current;
            if (!identity.IsValid) { StopNow(); return true; }
            return RuntimeServices.Scheduler.QueueSimulation(identity, StopNow);
        }

        public bool TryQueueWaterBudget(bool night, int budget)
        {
            if (budget < 0 || budget > 255) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Hosting && mode != MultiplayerSessionMode.ClientLive) return false;
                if (pendingBudget.Count >= 64) return false;
                pendingBudget.Enqueue(new WaterBudgetIntent(night, budget));
                return true;
            }
        }

        public void PollSimulation()
        {
            if (!load.IsValid || !lifecycle.IsCurrent(load)) return;
            try
            {
                if (server != null) DrainServerEvents();
                if (client != null) DrainClientEvents();
                if (joins != null) { joins.Expire(); ExpireHostJoins(); }
                DrainBudgetIntents();
            }
            catch (Exception error) { FenceSession("session-poll:" + error.GetType().Name); }
        }

        public void AfterSimulationTick()
        {
            if (!load.IsValid || !lifecycle.IsCurrent(load)) return;
            try
            {
                if (authority != null) RuntimeServices.Metadata.Update(load, authority.Revision, authority.CurrentRoot);
                else if (replica != null) RuntimeServices.Metadata.Update(load, replica.Revision, replica.CurrentRoot);
            }
            catch (Exception error) { FenceSession("session-after-tick:" + error.GetType().Name); }
        }

        private void StartHostOnSimulation(LoadIdentity identity, int port, string roomKey, string displayName)
        {
            if (!lifecycle.IsCurrent(identity)) { SetOffline("stale-host-start"); return; }
            try
            {
                load = identity;
                localManifest = CitiesCompatibilityCollector.Collect();
                hostPolicy = new CompatibilityPolicy(localManifest.GameBuildHash, localManifest.SchemaHash,
                    localManifest.Entries, new ComponentFingerprint[0]);
                hostWater = new WaterBudgetAuthorityDomain(load);
                authority = new AuthorityCoordinatorV2(new SessionStamp(load.WorldId, load.Epoch),
                    new IAuthorityDomainV2[] { hostWater });
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
                UnityEngine.Debug.Log("[CSM-Forge] Host started on UDP " + port +
                    " using DEVELOPMENT room-key transport; this is not production authentication.");
            }
            catch (Exception error) { AbortStart("host-start:" + error.GetType().Name); }
        }

        private void StartClientOnSimulation(LoadIdentity identity, IPEndPoint endpoint, string roomKey, string displayName)
        {
            if (!lifecycle.IsCurrent(identity)) { SetOffline("stale-client-start"); return; }
            try
            {
                load = identity;
                clientName = displayName;
                localManifest = CitiesCompatibilityCollector.Collect();
                clientManifestPages = BootstrapMessagesV2.CreateManifestPages(localManifest);
                client = new LiteNetClientTransport();
                if (!client.Start(endpoint, roomKey)) throw new InvalidOperationException("Could not start LiteNet client.");
                if (!lifecycle.TryTransition(load, CitiesRuntimeRole.ClientLoading))
                    throw new InvalidOperationException("Could not enter ClientLoading runtime role.");
                lock (gate) { mode = MultiplayerSessionMode.ConnectingClient; detail = "connecting-development-transport"; }
            }
            catch (Exception error) { AbortStart("client-start:" + error.GetType().Name); }
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
                    events.Record(RuntimeEventCode.Error, load.Generation,
                        "peer=" + peer.TransportId + "; " + error.GetType().Name);
                    if (authority != null && authority.IsFenced)
                    {
                        FenceSession("authority-world-fenced");
                        return;
                    }
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
            peer.Join = joins.StartJoin(peer.Member, peer.TransportId, authority.Revision, authority.CurrentRoot);

            SendServerBootstrap(peer, BootstrapKind.CompatibilityResult,
                BootstrapMessagesV2.EncodeCompatibilityResult(new CompatibilityResultV2(true, string.Empty)));
            SendServerBootstrap(peer, BootstrapKind.SessionWelcome,
                BootstrapMessagesV2.EncodeWelcome(new SessionWelcomeV2(authority.Stamp, peer.TransportId,
                    peer.Member, 1, authority.Revision, authority.CurrentRoot)));
            peer.SessionReady = true;
            SendServerFrame(peer, MessageKindV2.SnapshotOffer,
                JoinMessagesV2.EncodeSnapshotOffer(new SnapshotOfferV2(peer.Join.JoinId, peer.Join.Generation,
                    authority.Revision, authority.CurrentRoot, false, Guid.Empty, Guid.Empty, 0, null)));
        }

        private void HandleHostSessionFrame(HostPeer peer, byte[] bytes)
        {
            SessionFrameV2 frame = SessionFrameCodecV2.Decode(bytes);
            if (!frame.Stamp.Equals(authority.Stamp) || frame.ConnectionBinding != peer.TransportId ||
                !peer.Sequences.Accept(frame.Lane, frame.Sequence))
                throw new InvalidOperationException("Host rejected session frame identity or sequence.");
            switch (frame.Kind)
            {
                case MessageKindV2.WorldInstalled: HandleWorldInstalled(peer, JoinMessagesV2.DecodeWorldInstalled(frame.Payload)); break;
                case MessageKindV2.BarrierAck: HandleBarrierAck(peer, JoinMessagesV2.DecodeBarrierAck(frame.Payload)); break;
                case MessageKindV2.Activated: HandleActivated(peer, JoinMessagesV2.DecodeActivated(frame.Payload)); break;
                case MessageKindV2.Intent: HandleRemoteIntent(peer, SessionMessagesV2.DecodeIntent(frame.Stamp, frame.Payload)); break;
                case MessageKindV2.AppliedAck:
                    if (!authority.RecordApplied(SessionMessagesV2.DecodeAppliedAck(frame.Stamp, peer.TransportId, frame.Payload)))
                        throw new InvalidOperationException("Applied acknowledgement was not valid for retained authority history.");
                    break;
                case MessageKindV2.GapRequest: SendJournal(peer, ControlMessagesV2.DecodeGapRequest(frame.Payload).AfterRevision); break;
                default: throw new InvalidOperationException("Client message is not valid in the current Host path.");
            }
        }

        private void HandleWorldInstalled(HostPeer peer, WorldInstalledV2 installed)
        {
            if (installed.JoinId != peer.Join.JoinId || installed.JoinGeneration != peer.Join.Generation ||
                !joins.MarkLoading(peer.Join) || !joins.MarkSnapshotInstalled(peer.Join, installed.Revision, installed.Root))
                throw new InvalidOperationException("WorldInstalled baseline did not match the join offer.");
            ulong h = authority.Revision; Hash256 root;
            if (!authority.TryGetRoot(h, out root)) throw new InvalidOperationException("Barrier root is unavailable.");
            SendJournal(peer, installed.Revision);
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
            SendJournal(peer, ack.Revision);
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
        }

        private void HandleRemoteIntent(HostPeer peer, PlayerIntentV2 intent)
        {
            if (!peer.Live) throw new InvalidOperationException("Non-live peer submitted a player intent.");
            AuthoritySubmitResultV2 result = authority.Submit(peer.TransportId, intent);
            if (result.Decision == AuthoritySubmitDecisionV2.Faulted || authority.IsFenced)
            {
                FenceSession("authority-domain-fault");
                return;
            }
            SendServerFrame(peer, MessageKindV2.IntentReceipt,
                ControlMessagesV2.EncodeReceipt(new IntentReceiptV2(intent.OperationCounter, result.Decision,
                    result.Batch == null ? 0 : result.Batch.Revision)));
            if (result.Decision == AuthoritySubmitDecisionV2.Committed && result.Batch != null) BroadcastBatch(result.Batch);
        }

        private void SendJournal(HostPeer peer, ulong afterRevision)
        {
            AuthorityBatch[] batches;
            if (!authority.TryReadJournal(afterRevision, out batches))
                throw new InvalidOperationException("Required authority journal range is unavailable.");
            foreach (AuthorityBatch batch in batches)
                SendServerFrame(peer, MessageKindV2.AuthorityBatch, SessionMessagesV2.EncodeBatch(batch));
        }

        private void BroadcastBatch(AuthorityBatch batch)
        {
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.Live || peer.StateSubscribed)
                    SendServerFrame(peer, MessageKindV2.AuthorityBatch, SessionMessagesV2.EncodeBatch(batch));
        }

        private void DrainClientEvents()
        {
            TransportEvent item; int processed = 0;
            while (processed++ < 128 && client.TryTake(out item))
            {
                if (item.Kind == TransportEventKind.Connected) { SendClientBootstrap(); continue; }
                if (item.Kind == TransportEventKind.Disconnected)
                {
                    if (replica == null) AbortStart("client-disconnected:" + item.Detail);
                    else FenceSession("client-disconnected:" + item.Detail);
                    continue;
                }
                if (item.Kind == TransportEventKind.Error) { events.Record(RuntimeEventCode.Error, load.Generation, item.Detail); continue; }
                if (item.Kind == TransportEventKind.Data)
                {
                    if (clientSessionReady) HandleClientSessionFrame(item.Payload);
                    else HandleClientBootstrap(item.Payload);
                }
            }
        }

        private void SendClientBootstrap()
        {
            HelloV2 hello = new HelloV2(clientInstanceId, clientName, localManifest.GameBuildHash,
                localManifest.SchemaHash, (ushort)clientManifestPages.Length, BootstrapAuthMode.DevelopmentRoomKeyOnly);
            SendClientBootstrapFrame(BootstrapKind.Hello, BootstrapMessagesV2.EncodeHello(hello));
            SendClientBootstrapFrame(BootstrapKind.IdentityProof,
                BootstrapMessagesV2.EncodeIdentityProof(BootstrapAuthMode.DevelopmentRoomKeyOnly, clientInstanceId));
            foreach (ManifestPageV2 page in clientManifestPages)
                SendClientBootstrapFrame(BootstrapKind.ManifestPage, BootstrapMessagesV2.EncodeManifestPage(page));
        }

        private void HandleClientBootstrap(byte[] bytes)
        {
            BootstrapFrame frame = BootstrapCodec.Decode(bytes);
            if (frame.Kind == BootstrapKind.CompatibilityResult)
            {
                CompatibilityResultV2 result = BootstrapMessagesV2.DecodeCompatibilityResult(frame.Payload);
                if (!result.Accepted) { AbortStart("compatibility-rejected:" + result.Reason); return; }
                clientCompatibilityAccepted = true; return;
            }
            if (frame.Kind == BootstrapKind.SessionWelcome)
            {
                if (!clientCompatibilityAccepted) throw new InvalidOperationException("SessionWelcome arrived before compatibility acceptance.");
                SessionWelcomeV2 welcome = BootstrapMessagesV2.DecodeWelcome(frame.Payload);
                clientStamp = welcome.Stamp; clientBinding = welcome.ConnectionBinding; clientMember = welcome.Member;
                clientPermissionVersion = welcome.PermissionVersion; clientSequences = new LaneSequenceTracker(); clientSessionReady = true;
                return;
            }
            throw new InvalidOperationException("Unexpected Host bootstrap message.");
        }

        private void HandleClientSessionFrame(byte[] bytes)
        {
            SessionFrameV2 frame = SessionFrameCodecV2.Decode(bytes);
            if (!frame.Stamp.Equals(clientStamp) || frame.ConnectionBinding != clientBinding || !clientSequences.Accept(frame.Lane, frame.Sequence))
                throw new InvalidOperationException("Client rejected session frame identity or sequence.");
            switch (frame.Kind)
            {
                case MessageKindV2.SnapshotOffer: HandleSnapshotOffer(frame, JoinMessagesV2.DecodeSnapshotOffer(frame.Payload)); break;
                case MessageKindV2.AuthorityBatch: HandleClientBatch(frame, SessionMessagesV2.DecodeBatch(frame.Stamp, frame.Payload)); break;
                case MessageKindV2.ReplayBarrier: HandleReplayBarrier(frame, JoinMessagesV2.DecodeReplayBarrier(frame.Payload)); break;
                case MessageKindV2.ActivationGrant: HandleActivationGrant(frame, JoinMessagesV2.DecodeActivationGrant(frame.Payload)); break;
                case MessageKindV2.IntentReceipt:
                    IntentReceiptV2 receipt = ControlMessagesV2.DecodeReceipt(frame.Payload);
                    lock (gate) detail = "intent-" + receipt.OperationCounter + ":" + receipt.Decision;
                    break;
                default: throw new InvalidOperationException("Host message is not valid in the current Client path.");
            }
        }

        private void HandleSnapshotOffer(SessionFrameV2 frame, SnapshotOfferV2 offer)
        {
            if (offer.RequiresTransfer) { AbortStart("snapshot-transfer-required"); return; }
            if (frame.Stamp.WorldId != load.WorldId || frame.Stamp.Epoch != load.Epoch)
            { AbortStart("snapshot-required-world-identity-mismatch"); return; }
            clientWater = new WaterBudgetReplicaDomain(load);
            replica = new ReplicaCoordinatorV2(frame.Stamp, new IReplicaDomainV2[] { clientWater }, offer.BaselineRevision);
            if (!replica.CurrentRoot.Equals(offer.BaselineRoot))
            {
                replica = null; clientWater = null;
                AbortStart("snapshot-required-state-root-mismatch"); return;
            }
            clientOffer = offer;
            lock (gate) { mode = MultiplayerSessionMode.ClientCatchingUp; detail = "baseline-installed"; }
            SendClientFrame(MessageKindV2.WorldInstalled,
                JoinMessagesV2.EncodeWorldInstalled(new WorldInstalledV2(offer.JoinId, offer.JoinGeneration,
                    offer.BaselineRevision, offer.BaselineRoot)));
        }

        private void HandleClientBatch(SessionFrameV2 frame, AuthorityBatch batch)
        {
            if (replica == null || !frame.Stamp.Equals(replica.Stamp)) throw new InvalidOperationException("Authority batch arrived before replica baseline.");
            ReplicaDecisionV2 decision = replica.Receive(batch);
            if (decision == ReplicaDecisionV2.Gap)
            {
                SendClientFrame(MessageKindV2.GapRequest, ControlMessagesV2.EncodeGapRequest(new GapRequestV2(replica.Revision))); return;
            }
            if (decision != ReplicaDecisionV2.Applied && decision != ReplicaDecisionV2.Duplicate)
                throw new InvalidOperationException("Replica rejected authority batch: " + decision);
            AppliedAck ack = replica.CreateAppliedAck(clientBinding, 0);
            if (ack != null) SendClientFrame(MessageKindV2.AppliedAck, SessionMessagesV2.EncodeAppliedAck(ack));
        }

        private void HandleReplayBarrier(SessionFrameV2 frame, ReplayBarrierV2 barrier)
        {
            if (replica == null || clientOffer == null || barrier.JoinId != clientOffer.JoinId || barrier.JoinGeneration != clientOffer.JoinGeneration ||
                replica.Revision < barrier.Revision || !replica.CurrentRoot.Equals(barrier.Root))
            {
                SendClientFrame(MessageKindV2.GapRequest,
                    ControlMessagesV2.EncodeGapRequest(new GapRequestV2(replica == null ? 0 : replica.Revision))); return;
            }
            SendClientFrame(MessageKindV2.BarrierAck,
                JoinMessagesV2.EncodeBarrierAck(new BarrierAckV2(barrier.JoinId, barrier.JoinGeneration,
                    barrier.BarrierId, barrier.Revision, barrier.Root)));
        }

        private void HandleActivationGrant(SessionFrameV2 frame, ActivationGrantV2 grant)
        {
            if (replica == null || clientOffer == null || grant.JoinId != clientOffer.JoinId || grant.JoinGeneration != clientOffer.JoinGeneration ||
                replica.Revision != grant.Revision || !replica.CurrentRoot.Equals(grant.Root) || !replica.MarkLive(grant.Revision, grant.Root) ||
                !lifecycle.TryTransition(load, CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Activation grant cannot be applied to the current replica state.");
            clientPermissionVersion = grant.PermissionVersion;
            lock (gate) { mode = MultiplayerSessionMode.ClientLive; detail = "live"; }
            SendClientFrame(MessageKindV2.Activated,
                JoinMessagesV2.EncodeActivated(new ActivatedV2(grant.JoinId, grant.JoinGeneration, grant.GrantId, grant.Revision)));
        }

        private void DrainBudgetIntents()
        {
            int count = 0;
            while (count++ < 32)
            {
                WaterBudgetIntent intent;
                lock (gate) { if (pendingBudget.Count == 0) break; intent = pendingBudget.Dequeue(); }
                if (mode == MultiplayerSessionMode.Hosting) SubmitHostBudget(intent);
                else if (mode == MultiplayerSessionMode.ClientLive) SubmitClientBudget(intent);
            }
        }

        private void SubmitHostBudget(WaterBudgetIntent value)
        {
            if (hostLocalOperation == ulong.MaxValue) throw new InvalidOperationException("Host operation counter exhausted.");
            hostLocalOperation++;
            PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                WaterBudgetAuthorityDomain.Id, hostWater.StateRoot, WaterBudgetCodec.EncodeIntent(value));
            AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
            if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                throw new InvalidOperationException("Host-local water budget operation was rejected: " + result.Decision);
            BroadcastBatch(result.Batch);
        }

        private void SubmitClientBudget(WaterBudgetIntent value)
        {
            if (clientOperation == ulong.MaxValue) throw new InvalidOperationException("Client operation counter exhausted.");
            clientOperation++;
            PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                WaterBudgetAuthorityDomain.Id, clientWater.StateRoot, WaterBudgetCodec.EncodeIntent(value));
            SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
            lock (gate) detail = "intent-" + clientOperation + ":pending";
        }

        private void SendServerBootstrap(HostPeer peer, BootstrapKind kind, byte[] payload)
        {
            if (!server.TrySend(peer.TransportId, BootstrapCodec.Encode(new BootstrapFrame(kind, payload))))
                throw new InvalidOperationException("Could not send bootstrap frame to client.");
        }

        private void SendClientBootstrapFrame(BootstrapKind kind, byte[] payload)
        {
            if (!client.TrySend(BootstrapCodec.Encode(new BootstrapFrame(kind, payload))))
                throw new InvalidOperationException("Could not send bootstrap frame to Host.");
        }

        private void SendServerFrame(HostPeer peer, MessageKindV2 kind, byte[] payload)
        {
            SessionLane lane = SessionFrameV2.ExpectedLane(kind);
            SessionFrameV2 frame = new SessionFrameV2(lane, kind, authority.Stamp, peer.TransportId,
                peer.Sequences.Next(lane), Guid.Empty, 1, payload);
            if (!server.TrySend(peer.TransportId, SessionFrameCodecV2.Encode(frame)))
                throw new InvalidOperationException("Could not send session frame to client.");
        }

        private void SendClientFrame(MessageKindV2 kind, byte[] payload)
        {
            SessionLane lane = SessionFrameV2.ExpectedLane(kind);
            SessionFrameV2 frame = new SessionFrameV2(lane, kind, clientStamp, clientBinding,
                clientSequences.Next(lane), Guid.Empty, 1, payload);
            if (!client.TrySend(SessionFrameCodecV2.Encode(frame)))
                throw new InvalidOperationException("Could not send session frame to Host.");
        }

        private void RemoveHostPeer(HostPeer peer)
        {
            hostPeers.Remove(peer.TransportId);
            if (peer.Join.IsValid) joins.Cancel(peer.Join);
            authority.Disconnect(peer.TransportId);
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

        private void AbortStart(string reason)
        {
            events.Record(RuntimeEventCode.Error, lifecycle.Current.Generation, reason);
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            server = null; client = null; authority = null; replica = null; joins = null;
            hostWater = null; clientWater = null; hostPeers.Clear();
            lock (gate) { pendingBudget.Clear(); mode = MultiplayerSessionMode.Faulted; detail = reason; }
            if (load.IsValid && lifecycle.IsCurrent(load)) lifecycle.TryTransition(load, CitiesRuntimeRole.SinglePlayer);
        }

        private void FenceSession(string reason)
        {
            events.Record(RuntimeEventCode.Error, lifecycle.Current.Generation, reason);
            lock (gate) { mode = MultiplayerSessionMode.Faulted; detail = reason; pendingBudget.Clear(); }
            lifecycle.Fence(reason);
            try { if (server != null) server.Stop(); } catch { }
            try { if (client != null) client.Stop(); } catch { }
        }

        private void StopNow()
        {
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            server = null; client = null; authority = null; replica = null; joins = null;
            hostWater = null; clientWater = null; hostPolicy = null; localManifest = null;
            hostPeers.Clear(); memberGenerations.Clear(); clientManifestPages = null; clientOffer = null;
            clientCompatibilityAccepted = false; clientSessionReady = false; clientSequences = null;
            clientStamp = default(SessionStamp); hostLocalOperation = 0; clientOperation = 0; clientBinding = Guid.Empty;
            lock (gate) { pendingBudget.Clear(); mode = MultiplayerSessionMode.Offline; detail = "offline"; }
            if (load.IsValid && lifecycle.IsCurrent(load)) lifecycle.TryTransition(load, CitiesRuntimeRole.SinglePlayer);
            load = default(LoadIdentity);
        }

        private void SetOffline(string reason)
        {
            lock (gate) { mode = MultiplayerSessionMode.Offline; detail = reason; }
        }

        private static long MonotonicMilliseconds()
        {
            long ticks = Stopwatch.GetTimestamp();
            return ticks / Stopwatch.Frequency * 1000 + ticks % Stopwatch.Frequency * 1000 / Stopwatch.Frequency;
        }
    }
}
