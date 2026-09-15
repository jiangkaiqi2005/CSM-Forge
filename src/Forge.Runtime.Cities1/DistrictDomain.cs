using System;
using System.Collections.Generic;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class DistrictGameAccess
    {
        public const int GridResolution = 512;

        public static bool Live(byte nativeId)
        {
            return nativeId != 0 && DistrictManager.instance != null &&
                (DistrictManager.instance.m_districts.m_buffer[nativeId].m_flags & District.Flags.Created) != District.Flags.None;
        }

        public static DistrictEntityStateV2 CaptureEntity(EntityIdentityV2 identity, byte nativeId)
        {
            if (!identity.IsValid || !Live(nativeId)) throw new InvalidOperationException("District entity is not live.");
            District data = DistrictManager.instance.m_districts.m_buffer[nativeId];
            return new DistrictEntityStateV2(identity, data.m_randomSeed, data.m_Style);
        }

        private static EntityIdentityV2 ResolveCellDistrict(byte nativeId, byte alpha, EntityIdMapV2 ids)
        {
            if (alpha == 0) return default(EntityIdentityV2);
            if (nativeId == 0) throw new InvalidOperationException("Non-empty district cell references district zero.");
            EntityIdentityV2 identity;
            if (!ids.TryGetIdentity(nativeId, out identity))
                throw new InvalidOperationException("District grid references an unmapped native district.");
            return identity;
        }

        public static DistrictCellStateV2 CaptureCell(uint index, EntityIdMapV2 ids)
        {
            DistrictManager manager = DistrictManager.instance;
            if (manager == null || index >= manager.m_districtGrid.Length) throw new ArgumentOutOfRangeException("index");
            DistrictManager.Cell cell = manager.m_districtGrid[index];
            return new DistrictCellStateV2(index,
                ResolveCellDistrict(cell.m_district1, cell.m_alpha1, ids), cell.m_alpha1,
                ResolveCellDistrict(cell.m_district2, cell.m_alpha2, ids), cell.m_alpha2,
                ResolveCellDistrict(cell.m_district3, cell.m_alpha3, ids), cell.m_alpha3,
                ResolveCellDistrict(cell.m_district4, cell.m_alpha4, ids), cell.m_alpha4);
        }

        public static bool CellEquivalent(DistrictCellStateV2 a, DistrictCellStateV2 b)
        {
            if (a == null || b == null || a.Index != b.Index) return false;
            for (int slot = 0; slot < 4; slot++)
                if (a.AlphaAt(slot) != b.AlphaAt(slot) ||
                    (a.AlphaAt(slot) != 0 && !a.IdentityAt(slot).Equals(b.IdentityAt(slot)))) return false;
            return true;
        }

        public static byte CreateDistrict(LoadIdentity load)
        {
            byte nativeId;
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id))
                if (!DistrictManager.instance.CreateDistrict(out nativeId))
                    throw new InvalidOperationException("CS1 could not allocate an authoritative district.");
            return nativeId;
        }

        public static void InstallEntity(LoadIdentity load, byte nativeId, DistrictEntityStateV2 state)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || !Live(nativeId))
                throw new InvalidOperationException("District entity install belongs to a stale or missing district.");
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id))
            {
                DistrictManager.instance.m_districts.m_buffer[nativeId].m_randomSeed = state.RandomSeed;
                DistrictManager.instance.m_districts.m_buffer[nativeId].m_Style = state.Style;
            }
        }

        private static byte Native(EntityIdentityV2 identity, EntityIdMapV2 ids)
        {
            uint native;
            if (!identity.IsValid || !ids.TryGetNative(identity, out native) || native == 0 || native > byte.MaxValue)
                throw new InvalidOperationException("District identity is unavailable on this replica.");
            return (byte)native;
        }

        public static void ApplyCell(LoadIdentity load, DistrictCellStateV2 state, EntityIdMapV2 ids)
        {
            DistrictManager manager = DistrictManager.instance;
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || manager == null || state == null || state.Index >= manager.m_districtGrid.Length)
                throw new InvalidOperationException("District cell apply is invalid.");
            DistrictManager.Cell cell = manager.m_districtGrid[state.Index];
            cell.m_district1 = state.Alpha1 == 0 ? (byte)0 : Native(state.District1, ids); cell.m_alpha1 = state.Alpha1;
            cell.m_district2 = state.Alpha2 == 0 ? (byte)0 : Native(state.District2, ids); cell.m_alpha2 = state.Alpha2;
            cell.m_district3 = state.Alpha3 == 0 ? (byte)0 : Native(state.District3, ids); cell.m_alpha3 = state.Alpha3;
            cell.m_district4 = state.Alpha4 == 0 ? (byte)0 : Native(state.District4, ids); cell.m_alpha4 = state.Alpha4;
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id)) manager.m_districtGrid[state.Index] = cell;
        }

        public static void RefreshArea(DistrictCellStateV2[] cells)
        {
            if (cells == null || cells.Length == 0) return;
            int minX = GridResolution, minZ = GridResolution, maxX = -1, maxZ = -1;
            for (int i = 0; i < cells.Length; i++)
            {
                int x = (int)(cells[i].Index % GridResolution); int z = (int)(cells[i].Index / GridResolution);
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            if (maxX >= minX && maxZ >= minZ)
            {
                DistrictManager.instance.AreaModified(minX, minZ, maxX, maxZ, true);
                DistrictManager.instance.NamesModified();
            }
        }

        public static void ApplyBrush(LoadIdentity load, byte nativeDistrict, DistrictPaintIntentV2 intent)
        {
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id))
            {
                DistrictTool.ApplyBrush(DistrictTool.Layer.Districts, nativeDistrict, intent.BrushRadius,
                    new Vector3(intent.StartX, intent.StartY, intent.StartZ),
                    new Vector3(intent.EndX, intent.EndY, intent.EndZ));
                DistrictManager.instance.NamesModified();
            }
        }

        public static void Release(LoadIdentity load, byte nativeId)
        {
            if (!Live(nativeId)) return;
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id)) DistrictManager.instance.ReleaseDistrict(nativeId);
        }
    }

    public abstract class DistrictDomainBase
    {
        protected const ushort DistrictMapSaveId = 300;
        protected readonly LoadIdentity Load;
        protected readonly EntityIdMapV2 Ids = new EntityIdMapV2();
        protected readonly DistrictStateIndexV2 Committed = new DistrictStateIndexV2();

        protected DistrictDomainBase(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            Load = load; RuntimeServices.EntityMaps.AttachDomain(DistrictMapSaveId, Ids);
            SeedMappings(); SeedCommitted();
        }

        public Hash256 CurrentRoot { get { return Committed.Root; } }
        public bool TryResolve(uint nativeId, out EntityIdentityV2 identity) { return Ids.TryGetIdentity(nativeId, out identity); }

        private void SeedMappings()
        {
            if (Ids.Count == 0)
            {
                for (int i = 1; i < DistrictManager.instance.m_districts.m_buffer.Length && i <= byte.MaxValue; i++)
                    if (DistrictGameAccess.Live((byte)i)) Ids.Allocate((uint)i);
            }
            EntityMapEntryV2[] entries = Ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].NativeId > byte.MaxValue || !DistrictGameAccess.Live((byte)entries[i].NativeId))
                    throw new InvalidOperationException("Restored district identity map does not match the loaded world.");
        }

        private void SeedCommitted()
        {
            EntityMapEntryV2[] entries = Ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++)
                Committed.SeedEntity(DistrictGameAccess.CaptureEntity(entries[i].Identity, (byte)entries[i].NativeId));
            DistrictManager manager = DistrictManager.instance;
            for (uint i = 0; i < manager.m_districtGrid.Length; i++)
            {
                DistrictCellStateV2 cell = DistrictGameAccess.CaptureCell(i, Ids);
                if (!cell.IsEmpty) Committed.SeedCell(cell);
            }
        }

        protected DistrictMutationV2 ReconcileAll()
        {
            List<DistrictEntityStateV2> upsertEntities = new List<DistrictEntityStateV2>();
            List<EntityIdentityV2> deleteEntities = new List<EntityIdentityV2>();
            List<DistrictCellStateV2> cells = new List<DistrictCellStateV2>();

            for (int i = 1; i < DistrictManager.instance.m_districts.m_buffer.Length && i <= byte.MaxValue; i++)
            {
                byte native = (byte)i; if (!DistrictGameAccess.Live(native)) continue;
                EntityIdentityV2 identity;
                if (!Ids.TryGetIdentity(native, out identity)) identity = Ids.Allocate(native);
                DistrictEntityStateV2 actual = DistrictGameAccess.CaptureEntity(identity, native);
                DistrictEntityStateV2 old;
                if (!Committed.TryGetEntity(identity, out old) || old.RandomSeed != actual.RandomSeed || old.Style != actual.Style)
                    upsertEntities.Add(actual);
            }

            EntityMapEntryV2[] mappings = Ids.SnapshotEntries();
            for (int i = 0; i < mappings.Length; i++)
                if (mappings[i].NativeId <= byte.MaxValue && !DistrictGameAccess.Live((byte)mappings[i].NativeId))
                    deleteEntities.Add(mappings[i].Identity);

            DistrictManager manager = DistrictManager.instance;
            for (uint i = 0; i < manager.m_districtGrid.Length; i++)
            {
                DistrictCellStateV2 actual = DistrictGameAccess.CaptureCell(i, Ids); DistrictCellStateV2 old;
                bool known = Committed.TryGetCell(i, out old);
                if ((actual.IsEmpty && known) || (!actual.IsEmpty && (!known || !DistrictGameAccess.CellEquivalent(actual, old))))
                {
                    cells.Add(actual);
                    if (cells.Count > 4096) throw new InvalidOperationException("District brush changed more than 4096 cells; split the operation.");
                }
            }

            if (upsertEntities.Count == 0 && deleteEntities.Count == 0 && cells.Count == 0) return null;
            upsertEntities.Sort(delegate(DistrictEntityStateV2 a, DistrictEntityStateV2 b) { return a.Entity.EntityId.CompareTo(b.Entity.EntityId); });
            deleteEntities.Sort(delegate(EntityIdentityV2 a, EntityIdentityV2 b) { return a.EntityId.CompareTo(b.EntityId); });
            cells.Sort(delegate(DistrictCellStateV2 a, DistrictCellStateV2 b) { return a.Index.CompareTo(b.Index); });
            DistrictMutationV2 mutation = new DistrictMutationV2(upsertEntities.ToArray(), deleteEntities.ToArray(), cells.ToArray());
            Committed.Apply(mutation);
            for (int i = 0; i < deleteEntities.Count; i++) Ids.Retire(deleteEntities[i]);
            return mutation;
        }
    }

    public sealed class DistrictAuthorityDomain : DistrictDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 40;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public DistrictAuthorityDomain(LoadIdentity load) : base(load) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            DistrictPaintIntentV2 intent;
            try { intent = DistrictDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            byte native = 0;
            if (intent.TargetKind == DistrictPaintTargetKindV2.Existing)
            {
                uint value; if (!Ids.TryGetNative(intent.Target, out value) || value == 0 || value > byte.MaxValue) return DomainExecutionV2.Rejected();
                native = (byte)value;
            }
            else if (intent.TargetKind == DistrictPaintTargetKindV2.CreateNew)
                native = DistrictGameAccess.CreateDistrict(Load);
            DistrictGameAccess.ApplyBrush(Load, native, intent);
            DistrictMutationV2 mutation = ReconcileAll();
            if (mutation == null) return DomainExecutionV2.Rejected();
            return DomainExecutionV2.Success(DistrictDomainCodecV2.EncodeMutation(mutation), StateRoot);
        }

        internal DistrictMutationV2 ObserveHostWorld() { return ReconcileAll(); }
    }

    public sealed class DistrictReplicaDomain : DistrictDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return DistrictAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public DistrictReplicaDomain(LoadIdentity load) : base(load) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica district apply is invalid in the current role.");
            DistrictMutationV2 mutation = DistrictDomainCodecV2.DecodeMutation(absoluteDelta);
            for (int i = 0; i < mutation.UpsertEntities.Length; i++)
            {
                DistrictEntityStateV2 entity = mutation.UpsertEntities[i]; uint existing;
                byte native;
                if (Ids.TryGetNative(entity.Entity, out existing)) native = (byte)existing;
                else if (!RuntimeServices.Multiplayer.TryTakeCommittedPendingDistrict(out native)) native = DistrictGameAccess.CreateDistrict(Load);
                if (!Ids.TryGetIdentity(native, out _)) Ids.BindKnown(entity.Entity, native);
                DistrictGameAccess.InstallEntity(Load, native, entity);
            }
            for (int i = 0; i < mutation.Cells.Length; i++) DistrictGameAccess.ApplyCell(Load, mutation.Cells[i], Ids);
            DistrictGameAccess.RefreshArea(mutation.Cells);
            for (int i = 0; i < mutation.DeleteEntities.Length; i++)
            {
                uint native; if (!Ids.TryGetNative(mutation.DeleteEntities[i], out native) || native == 0 || native > byte.MaxValue)
                    throw new InvalidOperationException("Replica cannot release an unknown district.");
                DistrictGameAccess.Release(Load, (byte)native);
                if (!Ids.Retire(mutation.DeleteEntities[i])) throw new InvalidOperationException("Replica district identity retirement failed.");
            }
            Committed.Apply(mutation);
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("District grid projection root mismatch.");
        }
    }
}
