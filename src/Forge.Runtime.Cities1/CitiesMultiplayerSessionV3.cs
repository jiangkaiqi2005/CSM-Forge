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
        private sealed class PendingPresentation
        {
            public string ToolName;
            public float X;
            public float Y;
            public float Z;
            public bool Visible;
        }

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
        private readonly Queue<string> pendingChat = new Queue<string>();
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
        private string hostDisplayName;
        private MultiplayerPlayerSnapshot[] playerSnapshots = new MultiplayerPlayerSnapshot[0];
        private readonly List<MultiplayerChatSnapshot> chatSnapshots = new List<MultiplayerChatSnapshot>();
        private readonly Dictionary<Guid, MultiplayerPresentationSnapshot> presentationSnapshots =
            new Dictionary<Guid, MultiplayerPresentationSnapshot>();
        private PendingPresentation pendingPresentation;
        private ulong snapshotBytesReceived;
        private ulong snapshotBytesTotal;

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
                        ConnectedPeers = Math.Max(0, playerSnapshots.Length - 1),
                        Revision = authority != null ? authority.Revision : replica != null ? replica.Revision : 0,
                        DevelopmentTransport = mode != MultiplayerSessionMode.Offline,
                        SnapshotBytesReceived = snapshotBytesReceived,
                        SnapshotBytesTotal = snapshotBytesTotal,
                        Players = ClonePlayers(playerSnapshots),
                        Chat = CloneChat(chatSnapshots),
                        Presentations = ClonePresentations(presentationSnapshots)
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

        public bool RequestJoinFromMainMenu(IPEndPoint endpoint, string roomKey, string displayName)
        {
            if (lifecycle.Current.IsValid || endpoint == null || string.IsNullOrEmpty(roomKey) ||
                string.IsNullOrEmpty(displayName)) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Offline) return false;
                mode = MultiplayerSessionMode.ConnectingClient;
                detail = "main-menu-client-start";
            }
            StartClientBootstrap(default(LoadIdentity), endpoint, roomKey, displayName);
            return Status.Mode != MultiplayerSessionMode.Faulted;
        }

        public void PollMainMenu()
        {
            if (lifecycle.Current.IsValid || client == null) return;
            try { DrainClientEvents(); }
            catch (Exception error) { AbortStart("main-menu-poll:" + error.GetType().Name); }
        }

        public bool RequestStop()
        {
            LoadIdentity identity = lifecycle.Current;
            if (!identity.IsValid) { StopImmediately(); return true; }
            return RuntimeServices.Scheduler.QueueSimulation(identity, StopImmediately);
        }

        public void StopImmediately()
        {
            ForgeSteamRichPresence.Clear();
            RestoreSnapshotPause();
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            try { if (clientSnapshot != null) clientSnapshot.Dispose(); } catch { }
            RuntimeServices.WorldLoader.CancelPending();
            foreach (HostPeer peer in hostPeers.Values)
                try { if (peer.SnapshotCursor != null) peer.SnapshotCursor.Dispose(); } catch { }
            try { RuntimeServices.EntityMaps.SuspendCurrent(); } catch { }
            server = null; client = null; clientSnapshot = null; snapshotSave = null;
            authority = null; replica = null; joins = null;
            ClearAllDomainReferences();
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
                pendingChat.Clear();
                playerSnapshots = new MultiplayerPlayerSnapshot[0];
                chatSnapshots.Clear();
                presentationSnapshots.Clear();
                pendingPresentation = null;
                snapshotBytesReceived = 0;
                snapshotBytesTotal = 0;
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

        public bool TrySendChat(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0 || text.Length > 256) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Hosting && mode != MultiplayerSessionMode.ClientLive) return false;
                if (pendingChat.Count >= 32) return false;
                pendingChat.Enqueue(text);
                return true;
            }
        }

        public bool RequestKick(MemberIdentity member)
        {
            LoadIdentity identity = lifecycle.Current;
            if (!identity.IsValid || !member.IsValid) return false;
            lock (gate) if (mode != MultiplayerSessionMode.Hosting) return false;
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate
            {
                HostPeer target = null;
                foreach (HostPeer peer in hostPeers.Values)
                    if (peer.Member.Equals(member)) { target = peer; break; }
                if (target != null && server != null) server.Disconnect(target.TransportId);
            });
        }

        public bool TryPublishPresentation(string toolName, float x, float y, float z, bool visible)
        {
            if (toolName == null || toolName.Length > 96 || float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z)) return false;
            lock (gate)
            {
                if (mode != MultiplayerSessionMode.Hosting && mode != MultiplayerSessionMode.ClientLive) return false;
                pendingPresentation = new PendingPresentation
                { ToolName = toolName, X = x, Y = y, Z = z, Visible = visible };
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
                DrainChatMessages();
                DrainPresentation();
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

        private void DrainChatMessages()
        {
            int count = 0;
            while (count++ < 8)
            {
                string text;
                lock (gate) { if (pendingChat.Count == 0) break; text = pendingChat.Dequeue(); }
                if (mode == MultiplayerSessionMode.Hosting)
                    PublishChat(new ChatEventV2(hostLocalMember, hostDisplayName, text));
                else if (mode == MultiplayerSessionMode.ClientLive)
                    SendClientFrame(MessageKindV2.ChatSubmit, SocialMessagesV2.EncodeChatSubmit(new ChatSubmitV2(text)));
            }
        }

        private void DrainPresentation()
        {
            PendingPresentation pending;
            lock (gate) { pending = pendingPresentation; pendingPresentation = null; }
            if (pending == null) return;
            if (mode == MultiplayerSessionMode.Hosting)
                PublishPresentation(new PlayerPresentationV2(hostLocalMember, hostDisplayName, pending.ToolName,
                    pending.X, pending.Y, pending.Z, pending.Visible));
            else if (mode == MultiplayerSessionMode.ClientLive)
                SendClientFrame(MessageKindV2.PlayerPresentation, SocialMessagesV2.EncodePresentation(
                    new PlayerPresentationV2(clientMember, clientName, pending.ToolName,
                        pending.X, pending.Y, pending.Z, pending.Visible)));
        }

        private void PublishPresentation(PlayerPresentationV2 value)
        {
            RememberPresentation(value);
            byte[] payload = SocialMessagesV2.EncodePresentation(value);
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.Live) SendServerFrame(peer, MessageKindV2.PlayerPresentation, payload);
        }

        private void RememberPresentation(PlayerPresentationV2 value)
        {
            bool local = value.Member.Equals(hostLocalMember) || value.Member.Equals(clientMember);
            lock (gate) presentationSnapshots[value.Member.MemberId] = new MultiplayerPresentationSnapshot
            {
                Member = value.Member, DisplayName = value.DisplayName, ToolName = value.ToolName,
                WorldX = value.WorldX, WorldY = value.WorldY, WorldZ = value.WorldZ,
                Visible = value.Visible, IsLocal = local
            };
        }

        private void PublishChat(ChatEventV2 value)
        {
            RememberChat(value);
            foreach (HostPeer peer in hostPeers.Values)
                if (peer.SessionReady) SendServerFrame(peer, MessageKindV2.ChatEvent, SocialMessagesV2.EncodeChatEvent(value));
        }

        private void RememberChat(ChatEventV2 value)
        {
            lock (gate)
            {
                chatSnapshots.Add(new MultiplayerChatSnapshot
                { Member = value.Member, DisplayName = value.DisplayName, Text = value.Text });
                if (chatSnapshots.Count > 64) chatSnapshots.RemoveAt(0);
            }
        }

        private static MultiplayerPlayerSnapshot[] ClonePlayers(MultiplayerPlayerSnapshot[] values)
        {
            MultiplayerPlayerSnapshot[] result = new MultiplayerPlayerSnapshot[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = new MultiplayerPlayerSnapshot
            {
                Member = values[i].Member, DisplayName = values[i].DisplayName, IsHost = values[i].IsHost,
                IsLive = values[i].IsLive, IsLocal = values[i].IsLocal
            };
            return result;
        }

        private static MultiplayerChatSnapshot[] CloneChat(List<MultiplayerChatSnapshot> values)
        {
            MultiplayerChatSnapshot[] result = new MultiplayerChatSnapshot[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = new MultiplayerChatSnapshot
            { Member = values[i].Member, DisplayName = values[i].DisplayName, Text = values[i].Text };
            return result;
        }

        private static MultiplayerPresentationSnapshot[] ClonePresentations(
            Dictionary<Guid, MultiplayerPresentationSnapshot> values)
        {
            MultiplayerPresentationSnapshot[] result = new MultiplayerPresentationSnapshot[values.Count];
            int index = 0;
            foreach (MultiplayerPresentationSnapshot value in values.Values) result[index++] =
                new MultiplayerPresentationSnapshot
                {
                    Member = value.Member, DisplayName = value.DisplayName, ToolName = value.ToolName,
                    WorldX = value.WorldX, WorldY = value.WorldY, WorldZ = value.WorldZ,
                    Visible = value.Visible, IsLocal = value.IsLocal
                };
            return result;
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
            ForgeSteamRichPresence.Clear();
            events.Record(RuntimeEventCode.Error, lifecycle.Current.Generation, reason);
            try { if (server != null) server.Dispose(); } catch { }
            try { if (client != null) client.Dispose(); } catch { }
            try { RuntimeServices.EntityMaps.SuspendCurrent(); } catch { }
            server = null; client = null; authority = null; replica = null; joins = null;
            ClearAllDomainReferences();
            hostPeers.Clear();
            lock (gate)
            {
                preserveAcrossLevelLoad = false; pendingBudget.Clear(); pendingBuildings.Clear(); pendingChat.Clear();
                playerSnapshots = new MultiplayerPlayerSnapshot[0]; presentationSnapshots.Clear(); pendingPresentation = null;
                snapshotBytesReceived = 0; snapshotBytesTotal = 0; mode = MultiplayerSessionMode.Faulted; detail = reason;
            }
            if (load.IsValid && lifecycle.IsCurrent(load)) lifecycle.TryTransition(load, CitiesRuntimeRole.SinglePlayer);
        }

        private void FenceSession(string reason)
        {
            ForgeSteamRichPresence.Clear();
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
