using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Math;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    public sealed class NetNativeNodeRecord
    {
        public EntityIdentityV2 Entity;
        public ushort NativeId;
        public uint BuildIndex;
        public NetNodeStateV2 State;
    }

    public sealed class NetNativeSegmentRecord
    {
        public EntityIdentityV2 Entity;
        public ushort NativeId;
        public uint BuildIndex;
        public NetSegmentStateV2 State;
    }

    public sealed class NetWorldSnapshotV2
    {
        public readonly Dictionary<ushort, NetNativeNodeRecord> Nodes = new Dictionary<ushort, NetNativeNodeRecord>();
        public readonly Dictionary<ushort, NetNativeSegmentRecord> Segments = new Dictionary<ushort, NetNativeSegmentRecord>();
        public Hash256 Root;
    }

    internal static class NetGameAccess
    {
        public static bool NodeLive(ushort id)
        {
            return id != 0 && (NetManager.instance.m_nodes.m_buffer[id].m_flags & NetNode.Flags.Created) != NetNode.Flags.None;
        }

        public static bool SegmentLive(ushort id)
        {
            return id != 0 && (NetManager.instance.m_segments.m_buffer[id].m_flags & NetSegment.Flags.Created) != NetSegment.Flags.None;
        }

        public static NetInfo ResolvePrefab(string key)
        {
            NetInfo info = PrefabCollection<NetInfo>.FindLoaded(key);
            if (info == null) throw new InvalidOperationException("Net prefab is not loaded: " + key);
            return info;
        }

        public static NetNodeStateV2 CaptureNode(EntityIdentityV2 entity, ushort nativeId)
        {
            if (!NodeLive(nativeId)) throw new InvalidOperationException("Net node is not live.");
            NetNode node = NetManager.instance.m_nodes.m_buffer[nativeId];
            NetInfo info = node.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Net node prefab is unavailable.");
            return new NetNodeStateV2(entity, info.name, node.m_position.x, node.m_position.y, node.m_position.z, 0);
        }

        public static NetSegmentStateV2 CaptureSegment(EntityIdentityV2 entity, ushort nativeId, EntityIdMapV2 nodeIds)
        {
            if (!SegmentLive(nativeId)) throw new InvalidOperationException("Net segment is not live.");
            NetSegment segment = NetManager.instance.m_segments.m_buffer[nativeId];
            EntityIdentityV2 start, end;
            if (!nodeIds.TryGetIdentity(segment.m_startNode, out start) || !nodeIds.TryGetIdentity(segment.m_endNode, out end))
                throw new InvalidOperationException("Net segment endpoint has no Forge identity.");
            NetInfo info = segment.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Net segment prefab is unavailable.");
            uint flags = (segment.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None ? 1u : 0u;
            NetSegment.Flags2 zoning = segment.m_flags2 & (NetSegment.Flags2.ZoneLeft | NetSegment.Flags2.ZoneRight);
            return new NetSegmentStateV2(entity, info.name, start, end,
                segment.m_startDirection.x, segment.m_startDirection.y, segment.m_startDirection.z,
                segment.m_endDirection.x, segment.m_endDirection.y, segment.m_endDirection.z,
                flags, (uint)zoning);
        }

        public static NetTool.ControlPoint ToNativePoint(NetControlPointV2 point, EntityIdMapV2 nodeIds, EntityIdMapV2 segmentIds)
        {
            ushort node = 0, segment = 0; uint native;
            if (point.ExistingNode.IsValid)
            {
                if (!nodeIds.TryGetNative(point.ExistingNode, out native) || native == 0 || native > ushort.MaxValue)
                    throw new InvalidOperationException("Net control point references an unknown node.");
                node = (ushort)native;
            }
            if (point.ExistingSegment.IsValid)
            {
                if (!segmentIds.TryGetNative(point.ExistingSegment, out native) || native == 0 || native > ushort.MaxValue)
                    throw new InvalidOperationException("Net control point references an unknown segment.");
                segment = (ushort)native;
            }
            return new NetTool.ControlPoint
            {
                m_position = new Vector3(point.X, point.Y, point.Z),
                m_direction = new Vector3(point.DirectionX, point.DirectionY, point.DirectionZ),
                m_elevation = point.Elevation,
                m_node = node,
                m_segment = segment,
                m_outside = point.Outside
            };
        }

        public static ushort CreateReplicaNode(LoadIdentity load, NetNodeStateV2 state)
        {
            NetManager manager = NetManager.instance; SimulationManager simulation = SimulationManager.instance;
            Randomizer randomizer = simulation.m_randomizer; ushort id;
            using (RuntimeScopeGuard.EnterApply(load, NetAuthorityDomain.Id))
            {
                if (!manager.CreateNode(out id, ref randomizer, ResolvePrefab(state.PrefabKey),
                    new Vector3(state.X, state.Y, state.Z), simulation.m_currentBuildIndex))
                    throw new InvalidOperationException("CS1 rejected replica net node projection.");
                simulation.m_currentBuildIndex++;
            }
            return id;
        }

        public static ushort CreateReplicaSegment(LoadIdentity load, NetSegmentStateV2 state, ushort startNode, ushort endNode)
        {
            NetManager manager = NetManager.instance; SimulationManager simulation = SimulationManager.instance;
            Randomizer randomizer = simulation.m_randomizer; ushort id;
            NetSegment.Flags2 previousZoneFlags = NetTool.m_zoneGridFlags;
            try
            {
                NetTool.m_zoneGridFlags = (NetSegment.Flags2)state.Flags2;
                using (RuntimeScopeGuard.EnterApply(load, NetAuthorityDomain.Id))
                {
                    if (!manager.CreateSegment(out id, ref randomizer, ResolvePrefab(state.PrefabKey), startNode, endNode,
                        new Vector3(state.StartDirectionX, state.StartDirectionY, state.StartDirectionZ),
                        new Vector3(state.EndDirectionX, state.EndDirectionY, state.EndDirectionZ),
                        simulation.m_currentBuildIndex, simulation.m_currentBuildIndex, (state.Flags & 1u) != 0))
                        throw new InvalidOperationException("CS1 rejected replica net segment projection.");
                    simulation.m_currentBuildIndex++;
                }
            }
            finally { NetTool.m_zoneGridFlags = previousZoneFlags; }
            return id;
        }

        public static int SegmentRefund(ushort segmentId)
        {
            if (!SegmentLive(segmentId)) return 0;
            BulldozeTool tool = UnityEngine.Object.FindObjectOfType<BulldozeTool>();
            MethodInfo method = AccessTools.Method(typeof(BulldozeTool), "GetSegmentRefundAmount", new Type[] { typeof(ushort) });
            if (tool == null || method == null) return 0;
            object value = method.Invoke(tool, new object[] { segmentId });
            return value == null ? 0 : Math.Max(0, Convert.ToInt32(value));
        }
    }

    public abstract class NetDomainBase
    {
        protected const ushort NodeMapSaveId = 200;
        protected const ushort SegmentMapSaveId = 201;
        protected readonly LoadIdentity Load;
        protected readonly EntityIdMapV2 NodeIds = new EntityIdMapV2();
        protected readonly EntityIdMapV2 SegmentIds = new EntityIdMapV2();
        protected NetWorldSnapshotV2 Committed;

        protected NetDomainBase(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            Load = load;
            RuntimeServices.EntityMaps.AttachDomain(NodeMapSaveId, NodeIds);
            RuntimeServices.EntityMaps.AttachDomain(SegmentMapSaveId, SegmentIds);
            SeedOrValidate();
            Committed = CaptureWorld();
        }

        /// <summary>
        /// WP-1.4c: the committed root is cached - reading it no longer walks the whole graph.
        /// Live captures happen only at explicit diff points (ExecutePlayer, ObserveHostChanges,
        /// Reconcile). A live write that bypasses the NetTool/bulldoze patches stays invisible
        /// here and is caught by the cadence-driven full observe (PollObservedHostNetFull).
        /// </summary>
        public Hash256 CurrentRoot { get { return Committed.Root; } }
        public Hash256 CommittedRoot { get { return Committed.Root; } }

        public bool TryResolveNode(uint nativeId, out EntityIdentityV2 entity) { return NodeIds.TryGetIdentity(nativeId, out entity); }
        public bool TryResolveSegment(uint nativeId, out EntityIdentityV2 entity) { return SegmentIds.TryGetIdentity(nativeId, out entity); }
        public bool TryResolveNodeNative(EntityIdentityV2 entity, out uint nativeId) { return NodeIds.TryGetNative(entity, out nativeId); }
        public bool TryResolveSegmentNative(EntityIdentityV2 entity, out uint nativeId) { return SegmentIds.TryGetNative(entity, out nativeId); }

        private void SeedOrValidate()
        {
            NetManager manager = NetManager.instance;
            int nodeSize = checked((int)manager.m_nodes.m_size);
            if (NodeIds.Count == 0)
                for (int i = 1; i < nodeSize; i++) if (NetGameAccess.NodeLive((ushort)i)) NodeIds.Allocate((uint)i);
            int segmentSize = checked((int)manager.m_segments.m_size);
            if (SegmentIds.Count == 0)
                for (int i = 1; i < segmentSize; i++) if (NetGameAccess.SegmentLive((ushort)i)) SegmentIds.Allocate((uint)i);
            CaptureWorld();
        }

        protected NetWorldSnapshotV2 CaptureWorld()
        {
            NetWorldSnapshotV2 snapshot = new NetWorldSnapshotV2();
            NetStateIndexV2 index = new NetStateIndexV2();
            EntityMapEntryV2[] nodes = NodeIds.SnapshotEntries();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].NativeId > ushort.MaxValue) throw new InvalidOperationException("Net node native id exceeds CS1 range.");
                ushort native = (ushort)nodes[i].NativeId;
                NetNodeStateV2 state = NetGameAccess.CaptureNode(nodes[i].Identity, native);
                NetNode data = NetManager.instance.m_nodes.m_buffer[native];
                snapshot.Nodes.Add(native, new NetNativeNodeRecord { Entity = nodes[i].Identity, NativeId = native, BuildIndex = data.m_buildIndex, State = state });
                index.SeedNode(state);
            }
            EntityMapEntryV2[] segments = SegmentIds.SnapshotEntries();
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].NativeId > ushort.MaxValue) throw new InvalidOperationException("Net segment native id exceeds CS1 range.");
                ushort native = (ushort)segments[i].NativeId;
                NetSegmentStateV2 state = NetGameAccess.CaptureSegment(segments[i].Identity, native, NodeIds);
                NetSegment data = NetManager.instance.m_segments.m_buffer[native];
                snapshot.Segments.Add(native, new NetNativeSegmentRecord { Entity = segments[i].Identity, NativeId = native, BuildIndex = data.m_buildIndex, State = state });
                index.SeedSegment(state);
            }
            snapshot.Root = index.Root;
            return snapshot;
        }

        protected NetMutationV2 Reconcile(NetWorldSnapshotV2 before, int constructionCost, int refund)
        {
            Check.NotNull(before, "before");
            List<NetNodeStateV2> upsertNodes = new List<NetNodeStateV2>();
            List<EntityIdentityV2> deleteNodes = new List<EntityIdentityV2>();
            List<NetSegmentStateV2> upsertSegments = new List<NetSegmentStateV2>();
            List<EntityIdentityV2> deleteSegments = new List<EntityIdentityV2>();
            NetManager manager = NetManager.instance;

            foreach (NetNativeNodeRecord old in before.Nodes.Values)
            {
                if (!NetGameAccess.NodeLive(old.NativeId))
                {
                    if (NodeIds.Retire(old.Entity)) deleteNodes.Add(old.Entity);
                    continue;
                }
                NetNode now = manager.m_nodes.m_buffer[old.NativeId];
                if (now.m_buildIndex != old.BuildIndex)
                {
                    NodeIds.Retire(old.Entity); deleteNodes.Add(old.Entity);
                    EntityIdentityV2 replacement = NodeIds.Allocate(old.NativeId);
                    upsertNodes.Add(NetGameAccess.CaptureNode(replacement, old.NativeId));
                }
            }
            int nodeSize = checked((int)manager.m_nodes.m_size);
            for (int i = 1; i < nodeSize; i++)
            {
                ushort native = (ushort)i; if (!NetGameAccess.NodeLive(native)) continue;
                EntityIdentityV2 entity;
                if (!NodeIds.TryGetIdentity(native, out entity))
                {
                    entity = NodeIds.Allocate(native); upsertNodes.Add(NetGameAccess.CaptureNode(entity, native)); continue;
                }
                NetNativeNodeRecord old;
                if (before.Nodes.TryGetValue(native, out old) && old.Entity.Equals(entity))
                {
                    NetNodeStateV2 state = NetGameAccess.CaptureNode(entity, native);
                    if (!NodeEquivalent(old.State, state)) upsertNodes.Add(state);
                }
            }

            foreach (NetNativeSegmentRecord old in before.Segments.Values)
            {
                if (!NetGameAccess.SegmentLive(old.NativeId))
                {
                    if (SegmentIds.Retire(old.Entity)) deleteSegments.Add(old.Entity);
                    continue;
                }
                NetSegment now = manager.m_segments.m_buffer[old.NativeId];
                if (now.m_buildIndex != old.BuildIndex)
                {
                    SegmentIds.Retire(old.Entity); deleteSegments.Add(old.Entity);
                    EntityIdentityV2 replacement = SegmentIds.Allocate(old.NativeId);
                    upsertSegments.Add(NetGameAccess.CaptureSegment(replacement, old.NativeId, NodeIds));
                }
            }
            int segmentSize = checked((int)manager.m_segments.m_size);
            for (int i = 1; i < segmentSize; i++)
            {
                ushort native = (ushort)i; if (!NetGameAccess.SegmentLive(native)) continue;
                EntityIdentityV2 entity;
                if (!SegmentIds.TryGetIdentity(native, out entity))
                {
                    entity = SegmentIds.Allocate(native); upsertSegments.Add(NetGameAccess.CaptureSegment(entity, native, NodeIds)); continue;
                }
                NetNativeSegmentRecord old;
                if (before.Segments.TryGetValue(native, out old) && old.Entity.Equals(entity))
                {
                    NetSegmentStateV2 state = NetGameAccess.CaptureSegment(entity, native, NodeIds);
                    if (!SegmentEquivalent(old.State, state)) upsertSegments.Add(state);
                }
            }

            SortNodes(upsertNodes); SortIds(deleteNodes); SortSegments(upsertSegments); SortIds(deleteSegments);
            Committed = CaptureWorld();
            return new NetMutationV2(upsertNodes.ToArray(), deleteNodes.ToArray(), upsertSegments.ToArray(), deleteSegments.ToArray(), constructionCost, refund);
        }

        protected static bool NodeEquivalent(NetNodeStateV2 a, NetNodeStateV2 b)
        {
            return a != null && b != null && a.Entity.Equals(b.Entity) && a.PrefabKey == b.PrefabKey &&
                a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Flags == b.Flags;
        }

        protected static bool SegmentEquivalent(NetSegmentStateV2 a, NetSegmentStateV2 b)
        {
            return a != null && b != null && a.Entity.Equals(b.Entity) && a.PrefabKey == b.PrefabKey &&
                a.StartNode.Equals(b.StartNode) && a.EndNode.Equals(b.EndNode) &&
                a.StartDirectionX == b.StartDirectionX && a.StartDirectionY == b.StartDirectionY && a.StartDirectionZ == b.StartDirectionZ &&
                a.EndDirectionX == b.EndDirectionX && a.EndDirectionY == b.EndDirectionY && a.EndDirectionZ == b.EndDirectionZ &&
                a.Flags == b.Flags && a.Flags2 == b.Flags2;
        }

        private static void SortNodes(List<NetNodeStateV2> values) { values.Sort(delegate(NetNodeStateV2 a, NetNodeStateV2 b) { return a.Entity.EntityId.CompareTo(b.Entity.EntityId); }); }
        private static void SortSegments(List<NetSegmentStateV2> values) { values.Sort(delegate(NetSegmentStateV2 a, NetSegmentStateV2 b) { return a.Entity.EntityId.CompareTo(b.Entity.EntityId); }); }
        private static void SortIds(List<EntityIdentityV2> values) { values.Sort(delegate(EntityIdentityV2 a, EntityIdentityV2 b) { return a.EntityId.CompareTo(b.EntityId); }); }
    }

    public sealed class NetAuthorityDomain : NetDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 20;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }

        public NetAuthorityDomain(LoadIdentity load) : base(load) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            NetIntentV2 intent; try { intent = NetDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            NetWorldSnapshotV2 before = CaptureWorld();
            EconomySideEffectScope economy = EconomySideEffectCapture.Begin();
            try
            {
                if (intent.Kind == NetIntentKindV2.Create)
                {
                    ToolBase.ToolErrors errors;
                    NetSegment.Flags2 previous = NetTool.m_zoneGridFlags;
                    try
                    {
                        NetTool.m_zoneGridFlags = (NetSegment.Flags2)intent.ZoneGridFlags;
                        NetTool.ControlPoint start = NetGameAccess.ToNativePoint(intent.Start, NodeIds, SegmentIds);
                        NetTool.ControlPoint middle = NetGameAccess.ToNativePoint(intent.Middle, NodeIds, SegmentIds);
                        NetTool.ControlPoint end = NetGameAccess.ToNativePoint(intent.End, NodeIds, SegmentIds);
                        FastList<NetTool.NodePosition> buffer = new FastList<NetTool.NodePosition>();
                        ushort first, last, segment; int cost, production;
                        using (RuntimeScopeGuard.EnterApply(Load, Id))
                            errors = NetTool.CreateNode(NetGameAccess.ResolvePrefab(intent.PrefabKey), start, middle, end,
                                buffer, intent.MaxSegments, false, intent.TestEnds, false, intent.AutoFix, true,
                                intent.Invert, intent.SwitchDirection, 0, out first, out last, out segment, out cost, out production);
                    }
                    finally { NetTool.m_zoneGridFlags = previous; }
                    if (errors != ToolBase.ToolErrors.None)
                    {
                        NetWorldSnapshotV2 afterFailure = CaptureWorld();
                        if (!afterFailure.Root.Equals(before.Root)) throw new InvalidOperationException("Rejected NetTool operation mutated Host graph.");
                        return DomainExecutionV2.Rejected();
                    }
                }
                else if (intent.Kind == NetIntentKindV2.DeleteSegment)
                {
                    uint native;
                    if (!SegmentIds.TryGetNative(intent.Target, out native) || native == 0 || native > ushort.MaxValue) return DomainExecutionV2.Rejected();
                    ushort segment = (ushort)native; int refund = NetGameAccess.SegmentRefund(segment);
                    if (refund > 0)
                    {
                        NetInfo info = NetManager.instance.m_segments.m_buffer[segment].Info;
                        EconomyManager.instance.AddResource(EconomyManager.Resource.RefundAmount, refund, info.m_class);
                    }
                    using (RuntimeScopeGuard.EnterApply(Load, Id)) NetManager.instance.ReleaseSegment(segment, intent.KeepNodes);
                }
                else if (intent.Kind == NetIntentKindV2.DeleteNode)
                {
                    uint native;
                    if (!NodeIds.TryGetNative(intent.Target, out native) || native == 0 || native > ushort.MaxValue) return DomainExecutionV2.Rejected();
                    ushort node = (ushort)native;
                    if (NetManager.instance.m_nodes.m_buffer[node].CountSegments() != 0) return DomainExecutionV2.Rejected();
                    using (RuntimeScopeGuard.EnterApply(Load, Id)) NetManager.instance.ReleaseNode(node);
                }
                else if (NetworkMultitoolBridge.IsSemanticIntent(intent.Kind))
                {
                    bool executed;
                    using (RuntimeScopeGuard.EnterApply(Load, Id))
                        executed = NetworkMultitoolBridge.TryExecuteHost(intent, NodeIds, SegmentIds);
                    if (!executed) return DomainExecutionV2.Rejected();
                }
                else return DomainExecutionV2.Rejected();

                NetMutationV2 mutation = Reconcile(before, economy.ConstructionFetched, economy.RefundAdded);
                if (mutation.ConstructionCount == 0) return DomainExecutionV2.Rejected();
                return DomainExecutionV2.Success(NetDomainCodecV2.EncodeMutation(mutation), StateRoot);
            }
            finally { economy.Dispose(); }
        }

        internal NetMutationV2 ObserveHostChanges(int constructionCost, int refund)
        {
            NetWorldSnapshotV2 before = Committed;
            NetWorldSnapshotV2 actual = CaptureWorld();
            if (before.Root.Equals(actual.Root)) return null;
            return Reconcile(before, constructionCost, refund);
        }
    }

    public sealed class NetReplicaDomain : NetDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return NetAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public NetReplicaDomain(LoadIdentity load) : base(load) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            NetMutationV2 mutation = NetDomainCodecV2.DecodeMutation(absoluteDelta);
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica net apply is invalid in the current role.");

            using (RuntimeScopeGuard.EnterApply(Load, NetAuthorityDomain.Id))
            {
                if (mutation.ConstructionCost > 0)
                {
                    int fetched = EconomyManager.instance.FetchResource(EconomyManager.Resource.Construction, mutation.ConstructionCost,
                        PrefabCollection<NetInfo>.GetPrefab(0).m_class);
                    if (fetched != mutation.ConstructionCost) throw new InvalidOperationException("Replica could not project Host road construction cost.");
                }
                for (int i = 0; i < mutation.DeleteSegments.Length; i++)
                {
                    uint native;
                    if (!SegmentIds.TryGetNative(mutation.DeleteSegments[i], out native) || native == 0 || native > ushort.MaxValue)
                        throw new InvalidOperationException("Replica cannot delete unknown net segment.");
                    NetManager.instance.ReleaseSegment((ushort)native, true);
                    if (!SegmentIds.Retire(mutation.DeleteSegments[i])) throw new InvalidOperationException("Replica net segment retirement failed.");
                }
                for (int i = 0; i < mutation.DeleteNodes.Length; i++)
                {
                    uint native;
                    if (!NodeIds.TryGetNative(mutation.DeleteNodes[i], out native) || native == 0 || native > ushort.MaxValue)
                        throw new InvalidOperationException("Replica cannot delete unknown net node.");
                    if (NetManager.instance.m_nodes.m_buffer[(ushort)native].CountSegments() != 0)
                        throw new InvalidOperationException("Replica net node still has segments.");
                    NetManager.instance.ReleaseNode((ushort)native);
                    if (!NodeIds.Retire(mutation.DeleteNodes[i])) throw new InvalidOperationException("Replica net node retirement failed.");
                }
                for (int i = 0; i < mutation.UpsertNodes.Length; i++)
                {
                    NetNodeStateV2 state = mutation.UpsertNodes[i]; uint existing;
                    if (NodeIds.TryGetNative(state.Entity, out existing))
                    {
                        NetNodeStateV2 actual = NetGameAccess.CaptureNode(state.Entity, (ushort)existing);
                        if (!NodeEquivalent(state, actual)) throw new InvalidOperationException("Replica does not support in-place net node mutation yet.");
                    }
                    else
                    {
                        ushort native = NetGameAccess.CreateReplicaNode(Load, state); NodeIds.BindKnown(state.Entity, native);
                    }
                }
                for (int i = 0; i < mutation.UpsertSegments.Length; i++)
                {
                    NetSegmentStateV2 state = mutation.UpsertSegments[i]; uint existing;
                    if (SegmentIds.TryGetNative(state.Entity, out existing))
                    {
                        NetSegmentStateV2 actual = NetGameAccess.CaptureSegment(state.Entity, (ushort)existing, NodeIds);
                        if (!SegmentEquivalent(state, actual)) throw new InvalidOperationException("Replica does not support in-place net segment upgrade yet.");
                    }
                    else
                    {
                        uint start, end;
                        if (!NodeIds.TryGetNative(state.StartNode, out start) || !NodeIds.TryGetNative(state.EndNode, out end) ||
                            start == 0 || end == 0 || start > ushort.MaxValue || end > ushort.MaxValue)
                            throw new InvalidOperationException("Replica net segment endpoints are unavailable.");
                        ushort native = NetGameAccess.CreateReplicaSegment(Load, state, (ushort)start, (ushort)end);
                        SegmentIds.BindKnown(state.Entity, native);
                    }
                }
                if (mutation.Refund > 0)
                    EconomyManager.instance.AddResource(EconomyManager.Resource.RefundAmount, mutation.Refund,
                        PrefabCollection<NetInfo>.GetPrefab(0).m_class);
            }
            Committed = CaptureWorld();
            if (!Committed.Root.Equals(expectedAfterRoot)) throw new InvalidOperationException("Net graph projection root mismatch.");
        }
    }
}
