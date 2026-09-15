using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using CsmForge.Checkpoints;
using CsmForge.Core;
using CsmForge.Protocol;
using CsmForge.Transport.LiteNet;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
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
            public readonly LaneSequenceTracker Sequences = new LaneSequenceTracker();
            public bool SessionReady;
            public bool StateSubscribed;
            public bool Live;
            public bool WaitingSnapshot;
            public Guid TransferId;
            public SnapshotReadCursor SnapshotCursor;
            public ulong LastProgressOffset;
            public readonly Queue<PlayerIntentV2> DeferredIntents = new Queue<PlayerIntentV2>();
        }

        private readonly object gate = new object();
        private readonly CitiesLifecycleCoordinator lifecycle;
        private readonly RuntimeEventLog events;
        private readonly Queue<WaterBudgetIntent> pendingBudget = new Queue<WaterBudgetIntent>();
        private readonly Queue<BuildingIntentV2> pendingBuildings = new Queue<BuildingIntentV2>();
        private readonly Dictionary<Guid, HostPeer> hostPeers = new Dictionary<Guid, HostPeer>();
        private readonly Dictionary<Guid, uint> memberGenerations = new Dictionary<Guid, uint>();
        private readonly List<string> snapshotFiles = new List<string>();

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
        private BuildingAuthorityDomain hostBuildings;
        private BuildingReplicaDomain clientBuildings;
        private Guid hostLocalBinding;
        private MemberIdentity hostLocalMember;
        private ulong hostLocalOperation;

        private CitiesSnapshotSaveOperation snapshotSave;
        private SnapshotFileDescriptor publishedSnapshot;
        private bool snapshotPriorPaused;
        private bool snapshotPaused;

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
        private SnapshotReceiveFile clientSnapshot;
        private bool preserveAcrossLevelLoad;

        public CitiesMultiplayerSessionV3(CitiesLifecycleCoordinator lifecycle, RuntimeEventLog events)
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

        public bool PreserveAcrossLevelLoad { get { lock (gate) return preserveAcrossLevelLoad; } }

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
            if (!identity.IsValid) { StopImmediately(); return true; }
            return RuntimeServices.Scheduler.QueueSimulation(identity, StopImmediately);
        }

        public void StopImmediately()
        {
            RestoreSnapshotPause();
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            try { if (clientSnapshot != null) clientSnapshot.Dispose(); } catch { }
            RuntimeServices.WorldLoader.CancelPending();
            foreach (HostPeer peer in hostPeers.Values)
                try { if (peer.SnapshotCursor != null) peer.SnapshotCursor.Dispose(); } catch { }
            try { RuntimeServices.EntityMaps.SuspendCurrent(); } catch { }
            server = null; client = null; clientSnapshot = null; snapshotSave = null;
            authority = null; replica = null; joins = null; hostWater = null; clientWater = null;
            hostBuildings = null; clientBuildings = null;
            hostPolicy = null; localManifest = null; publishedSnapshot = null;
            hostPeers.Clear(); memberGenerations.Clear(); clientManifestPages = null; clientOffer = null;
            clientCompatibilityAccepted = false; clientSessionReady = false; clientSequences = null;
            clientStamp = default(SessionStamp); hostLocalOperation = 0; clientOperation = 0; clientBinding = Guid.Empty;
            foreach (string path in snapshotFiles)
                try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
            snapshotFiles.Clear();
            lock (gate)
            {
                preserveAcrossLevelLoad = false;
                pendingBudget.Clear();
                pendingBuildings.Clear();
                mode = MultiplayerSessionMode.Offline;
                detail = "offline";
            }
            if (load.IsValid && lifecycle.IsCurrent(load)) lifecycle.TryTransition(load, CitiesRuntimeRole.SinglePlayer);
            load = default(LoadIdentity);
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

        public bool TryQueueBuilding(BuildingIntentV2 intent)
        {
            if (intent == null) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Hosting && mode != MultiplayerSessionMode.ClientLive) return false;
                if (pendingBuildings.Count >= 64) return false;
                pendingBuildings.Enqueue(intent);
                return true;
            }
        }

        public void PollSimulation()
        {
            if (!load.IsValid || !lifecycle.IsCurrent(load)) return;
            try
            {
                if (server != null)
                {
                    DrainServerEvents();
                    PollSnapshotSave();
                    PumpSnapshotTransfers();
                    if (joins != null) { joins.Expire(); ExpireHostJoins(); }
                }
                if (client != null) DrainClientEvents();
                DrainBudgetIntents();
                DrainBuildingIntents();
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

        private void DrainBudgetIntents()
        {
            if (snapshotSave != null) return;
            int count = 0;
            while (count++ < 32)
            {
                WaterBudgetIntent intent;
                lock (gate) { if (pendingBudget.Count == 0) break; intent = pendingBudget.Dequeue(); }
                if (mode == MultiplayerSessionMode.Hosting) SubmitHostBudget(intent);
                else if (mode == MultiplayerSessionMode.ClientLive) SubmitClientBudget(intent);
            }
        }

        private void DrainBuildingIntents()
        {
            if (snapshotSave != null) return;
            int count = 0;
            while (count++ < 16)
            {
                BuildingIntentV2 intent;
                lock (gate) { if (pendingBuildings.Count == 0) break; intent = pendingBuildings.Dequeue(); }
                if (mode == MultiplayerSessionMode.Hosting) SubmitHostBuilding(intent);
                else if (mode == MultiplayerSessionMode.ClientLive) SubmitClientBuilding(intent);
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

        private void SubmitHostBuilding(BuildingIntentV2 value)
        {
            if (hostLocalOperation == ulong.MaxValue) throw new InvalidOperationException("Host operation counter exhausted.");
            hostLocalOperation++;
            PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                BuildingAuthorityDomain.Id, hostBuildings.StateRoot, BuildingDomainCodecV2.EncodeIntent(value));
            AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
            if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                throw new InvalidOperationException("Host-local building operation was rejected: " + result.Decision);
            BroadcastBatch(result.Batch);
        }

        private void SubmitClientBuilding(BuildingIntentV2 value)
        {
            if (clientOperation == ulong.MaxValue) throw new InvalidOperationException("Client operation counter exhausted.");
            clientOperation++;
            PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                BuildingAuthorityDomain.Id, clientBuildings.StateRoot, BuildingDomainCodecV2.EncodeIntent(value));
            SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
            lock (gate) detail = "building-intent-" + clientOperation + ":pending";
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

        private void BroadcastBatch(AuthorityBatch batch)
        {
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.Live || peer.StateSubscribed)
                    SendServerFrame(peer, MessageKindV2.AuthorityBatch, SessionMessagesV2.EncodeBatch(batch));
        }

        private void SetSnapshotPause()
        {
            if (snapshotPaused) return;
            FieldInfo field = typeof(SimulationManager).GetField("m_simulationPaused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException("SimulationManager", "m_simulationPaused");
            snapshotPriorPaused = (bool)field.GetValue(SimulationManager.instance);
            field.SetValue(SimulationManager.instance, true);
            snapshotPaused = true;
        }

        private void RestoreSnapshotPause()
        {
            if (!snapshotPaused) return;
            try
            {
                FieldInfo field = typeof(SimulationManager).GetField("m_simulationPaused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && SimulationManager.instance != null) field.SetValue(SimulationManager.instance, snapshotPriorPaused);
            }
            finally { snapshotPaused = false; }
        }

        private void AbortStart(string reason)
        {
            events.Record(RuntimeEventCode.Error, lifecycle.Current.Generation, reason);
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            try { RuntimeServices.EntityMaps.SuspendCurrent(); } catch { }
            server = null; client = null; authority = null; replica = null; joins = null;
            hostWater = null; clientWater = null; hostBuildings = null; clientBuildings = null; hostPeers.Clear();
            lock (gate) { preserveAcrossLevelLoad = false; pendingBudget.Clear(); pendingBuildings.Clear(); mode = MultiplayerSessionMode.Faulted; detail = reason; }
            if (load.IsValid && lifecycle.IsCurrent(load)) lifecycle.TryTransition(load, CitiesRuntimeRole.SinglePlayer);
        }

        private void FenceSession(string reason)
        {
            events.Record(RuntimeEventCode.Error, lifecycle.Current.Generation, reason);
            lock (gate) { preserveAcrossLevelLoad = false; mode = MultiplayerSessionMode.Faulted; detail = reason; pendingBudget.Clear(); pendingBuildings.Clear(); }
            lifecycle.Fence(reason);
            RestoreSnapshotPause();
            try { if (server != null) server.Stop(); } catch { }
            try { if (client != null) client.Stop(); } catch { }
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
