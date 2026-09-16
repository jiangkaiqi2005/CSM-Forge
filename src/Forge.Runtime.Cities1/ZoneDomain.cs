using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class ZoneGameAccess
    {
        private const double PositionScale = 16.0;
        private const double AngleScale = 4096.0;

        private static int Quantize(float value, double scale)
        {
            double scaled = Math.Round(value * scale, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue || scaled > int.MaxValue) throw new InvalidOperationException("Zone geometry exceeds canonical key range.");
            return (int)scaled;
        }

        public static bool TryKey(ushort blockId, NetDomainBase net, out ZoneBlockKeyV2 key)
        {
            key = default(ZoneBlockKeyV2);
            if (net == null || blockId == 0 || ZoneManager.instance == null) return false;
            ZoneBlock[] blocks = ZoneManager.instance.m_blocks.m_buffer;
            if (blockId >= blocks.Length) return false;
            ZoneBlock block = blocks[blockId];
            if ((block.m_flags & ZoneBlock.FLAG_CREATED) == 0 || block.m_segment == 0) return false;
            EntityIdentityV2 segment;
            if (!net.TryResolveSegment(block.m_segment, out segment)) return false;
            key = new ZoneBlockKeyV2(segment, Quantize(block.m_position.x, PositionScale),
                Quantize(block.m_position.z, PositionScale), Quantize(block.m_angle, AngleScale));
            return true;
        }

        public static ZoneStateV2 Capture(ushort blockId, NetDomainBase net)
        {
            ZoneBlockKeyV2 key;
            if (!TryKey(blockId, net, out key)) throw new InvalidOperationException("Zone block has no stable Forge key.");
            ZoneBlock block = ZoneManager.instance.m_blocks.m_buffer[blockId];
            return new ZoneStateV2(key, block.m_zone1, block.m_zone2);
        }

        public static bool TryFindBlock(ZoneBlockKeyV2 key, NetDomainBase net, out ushort blockId)
        {
            blockId = 0;
            if (!key.IsValid || net == null || ZoneManager.instance == null) return false;
            ZoneBlock[] blocks = ZoneManager.instance.m_blocks.m_buffer;
            for (int i = 1; i < blocks.Length && i <= ushort.MaxValue; i++)
            {
                ZoneBlockKeyV2 candidate;
                if (!TryKey((ushort)i, net, out candidate) || !candidate.Equals(key)) continue;
                if (blockId != 0) throw new InvalidOperationException("Zone geometry key is ambiguous on this replica.");
                blockId = (ushort)i;
            }
            return blockId != 0;
        }

        public static Dictionary<ZoneBlockKeyV2, ZoneStateV2> CaptureSparse(NetDomainBase net)
        {
            Dictionary<ZoneBlockKeyV2, ZoneStateV2> result = new Dictionary<ZoneBlockKeyV2, ZoneStateV2>();
            if (ZoneManager.instance == null) throw new InvalidOperationException("ZoneManager is unavailable.");
            ZoneBlock[] blocks = ZoneManager.instance.m_blocks.m_buffer;
            for (int i = 1; i < blocks.Length && i <= ushort.MaxValue; i++)
            {
                ZoneStateV2 state;
                try { state = Capture((ushort)i, net); }
                catch (InvalidOperationException) { continue; }
                if (state.IsEmpty) continue;
                if (result.ContainsKey(state.Key)) throw new InvalidOperationException("Duplicate non-empty zoning key detected.");
                result.Add(state.Key, state);
            }
            return result;
        }

        public static void Apply(LoadIdentity load, NetDomainBase net, ZoneStateV2 value, bool allowMissingEmpty)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Zone apply belongs to a stale load.");
            ushort blockId;
            if (!TryFindBlock(value.Key, net, out blockId))
            {
                if (allowMissingEmpty && value.IsEmpty) return;
                throw new InvalidOperationException("Target zoning block is unavailable on this replica.");
            }
            using (RuntimeScopeGuard.EnterApply(load, ZoneAuthorityDomain.Id))
            {
                ZoneManager.instance.m_blocks.m_buffer[blockId].m_zone1 = value.Zone1;
                ZoneManager.instance.m_blocks.m_buffer[blockId].m_zone2 = value.Zone2;
                ZoneManager.instance.m_blocks.m_buffer[blockId].RefreshZoning(blockId);
            }
            ZoneBlock actual = ZoneManager.instance.m_blocks.m_buffer[blockId];
            if (actual.m_zone1 != value.Zone1 || actual.m_zone2 != value.Zone2)
                throw new InvalidOperationException("CS1 did not install the requested zoning overlay.");
        }
    }

    public abstract class ZoneDomainBase
    {
        protected readonly LoadIdentity Load;
        protected readonly NetDomainBase Net;
        protected readonly ZoneStateIndexV2 Committed = new ZoneStateIndexV2();

        protected ZoneDomainBase(LoadIdentity load, NetDomainBase net)
        {
            if (!load.IsValid || net == null) throw new ArgumentException("Invalid zoning domain dependencies.");
            Load = load; Net = net;
            Dictionary<ZoneBlockKeyV2, ZoneStateV2> actual = ZoneGameAccess.CaptureSparse(Net);
            List<ZoneStateV2> values = new List<ZoneStateV2>(actual.Values);
            values.Sort(delegate(ZoneStateV2 a, ZoneStateV2 b) { return a.Key.CompareTo(b.Key); });
            for (int i = 0; i < values.Count; i++) Committed.Seed(values[i]);
        }

        public Hash256 CurrentRoot { get { return Committed.Root; } }

        public bool TryGetCommittedForBlock(ushort blockId, out ZoneBlockKeyV2 key, out ulong zone1, out ulong zone2)
        {
            zone1 = zone2 = 0;
            if (!ZoneGameAccess.TryKey(blockId, Net, out key)) return false;
            ZoneStateV2 state;
            if (Committed.TryGet(key, out state)) { zone1 = state.Zone1; zone2 = state.Zone2; }
            return true;
        }

        protected ZoneMutationV2 MutationFor(ZoneStateV2 state)
        {
            ZoneStateV2 current;
            bool known = Committed.TryGet(state.Key, out current);
            if (state.IsEmpty)
            {
                if (!known) return null;
                ZoneMutationV2 deletion = new ZoneMutationV2(new ZoneStateV2[0], new ZoneBlockKeyV2[] { state.Key });
                Committed.Apply(deletion); return deletion;
            }
            if (known && current.Zone1 == state.Zone1 && current.Zone2 == state.Zone2) return null;
            ZoneMutationV2 upsert = new ZoneMutationV2(new ZoneStateV2[] { state }, new ZoneBlockKeyV2[0]);
            Committed.Apply(upsert); return upsert;
        }

        public ZoneMutationV2 ReconcileWorld()
        {
            Dictionary<ZoneBlockKeyV2, ZoneStateV2> actual = ZoneGameAccess.CaptureSparse(Net);
            ZoneStateV2[] committed = Committed.Snapshot();
            List<ZoneStateV2> upserts = new List<ZoneStateV2>();
            List<ZoneBlockKeyV2> deletes = new List<ZoneBlockKeyV2>();
            for (int i = 0; i < committed.Length; i++)
            {
                ZoneStateV2 now;
                if (!actual.TryGetValue(committed[i].Key, out now)) deletes.Add(committed[i].Key);
                else if (now.Zone1 != committed[i].Zone1 || now.Zone2 != committed[i].Zone2) upserts.Add(now);
            }
            foreach (KeyValuePair<ZoneBlockKeyV2, ZoneStateV2> pair in actual)
            {
                ZoneStateV2 old;
                if (!Committed.TryGet(pair.Key, out old)) upserts.Add(pair.Value);
            }
            if (upserts.Count == 0 && deletes.Count == 0) return null;
            upserts.Sort(delegate(ZoneStateV2 a, ZoneStateV2 b) { return a.Key.CompareTo(b.Key); });
            deletes.Sort(delegate(ZoneBlockKeyV2 a, ZoneBlockKeyV2 b) { return a.CompareTo(b); });
            ZoneMutationV2 mutation = new ZoneMutationV2(upserts.ToArray(), deletes.ToArray());
            Committed.Apply(mutation); return mutation;
        }
    }

    public sealed class ZoneAuthorityDomain : ZoneDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 30;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }

        public ZoneAuthorityDomain(LoadIdentity load, NetAuthorityDomain net) : base(load, net) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            ZoneIntentV2 intent;
            try { intent = ZoneDomainCodecV2.DecodeIntent(payload); }
            catch { return DomainExecutionV2.Rejected(); }
            ushort blockId;
            if (!ZoneGameAccess.TryFindBlock(intent.Requested.Key, Net, out blockId)) return DomainExecutionV2.Rejected();
            ZoneGameAccess.Apply(Load, Net, intent.Requested, false);
            ZoneMutationV2 mutation = MutationFor(ZoneGameAccess.Capture(blockId, Net));
            if (mutation == null) return DomainExecutionV2.Rejected();
            return DomainExecutionV2.Success(ZoneDomainCodecV2.EncodeMutation(mutation), StateRoot);
        }

        internal ZoneMutationV2 ObserveBlock(ushort blockId)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return null;
            ZoneStateV2 state;
            try { state = ZoneGameAccess.Capture(blockId, Net); }
            catch { return null; }
            return MutationFor(state);
        }
    }

    public sealed class ZoneReplicaDomain : ZoneDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return ZoneAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public ZoneReplicaDomain(LoadIdentity load, NetReplicaDomain net) : base(load, net) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica zoning apply is invalid in the current role.");
            ZoneMutationV2 mutation = ZoneDomainCodecV2.DecodeMutation(absoluteDelta);
            for (int i = 0; i < mutation.Deletes.Length; i++)
                ZoneGameAccess.Apply(Load, Net, new ZoneStateV2(mutation.Deletes[i], 0, 0), true);
            for (int i = 0; i < mutation.Upserts.Length; i++)
                ZoneGameAccess.Apply(Load, Net, mutation.Upserts[i], false);
            Committed.Apply(mutation);
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Zoning overlay root mismatch.");
        }
    }
}
