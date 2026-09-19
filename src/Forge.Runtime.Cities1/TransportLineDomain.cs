using System;
using System.Collections.Generic;
using ColossalFramework.Math;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class TransportLineGameAccess
    {
        public static bool Live(ushort lineId)
        {
            if (lineId == 0 || TransportManager.instance == null) return false;
            TransportLine.Flags flags = TransportManager.instance.m_lines.m_buffer[lineId].m_flags;
            return (flags & TransportLine.Flags.Created) != TransportLine.Flags.None &&
                (flags & TransportLine.Flags.Temporary) == TransportLine.Flags.None;
        }

        public static TransportInfo ResolvePrefab(string key)
        {
            TransportInfo info = PrefabCollection<TransportInfo>.FindLoaded(key);
            if (info == null) throw new InvalidOperationException("Transport prefab is not loaded: " + key);
            return info;
        }

        private static TransportStopV2[] CaptureStops(ushort lineId, out bool complete)
        {
            TransportLine line = TransportManager.instance.m_lines.m_buffer[lineId];
            complete = (line.m_flags & TransportLine.Flags.Complete) != TransportLine.Flags.None;
            int count = line.CountStops(lineId);
            if (count < 0 || count > 512) throw new InvalidOperationException("Transport line stop count exceeds Forge bounds.");
            List<TransportStopV2> stops = new List<TransportStopV2>(count);
            ushort node = line.m_stops;
            for (int i = 0; i < count; i++)
            {
                if (node == 0) throw new InvalidOperationException("Transport line stop chain ended early.");
                Vector3 position = NetManager.instance.m_nodes.m_buffer[node].m_position;
                stops.Add(new TransportStopV2(position.x, position.y, position.z, false));
                node = TransportLine.GetNextStop(node);
            }
            return stops.ToArray();
        }

        public static TransportLineStateV2 Capture(EntityIdentityV2 entity, ushort lineId)
        {
            if (!entity.IsValid || !Live(lineId)) throw new InvalidOperationException("Transport line is not live.");
            TransportLine line = TransportManager.instance.m_lines.m_buffer[lineId];
            TransportInfo info = line.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Transport line prefab is unavailable.");
            Color32 color = line.m_color;
            bool day = (line.m_flags & TransportLine.Flags.DisabledDay) == TransportLine.Flags.None;
            bool night = (line.m_flags & TransportLine.Flags.DisabledNight) == TransportLine.Flags.None;
            bool complete; TransportStopV2[] stops = CaptureStops(lineId, out complete);
            return new TransportLineStateV2(entity, info.name, color.r, color.g, color.b, color.a,
                line.m_budget, line.m_ticketPrice, day, night, complete, stops);
        }

        public static TransportLineIntentV2 CaptureCreateIntent(ushort lineId)
        {
            if (!Live(lineId)) throw new InvalidOperationException("Pending transport line is not live.");
            TransportLine line = TransportManager.instance.m_lines.m_buffer[lineId];
            TransportInfo info = line.Info;
            if (info == null || string.IsNullOrEmpty(info.name)) throw new InvalidOperationException("Pending transport prefab is unavailable.");
            Color32 color = line.m_color;
            bool day = (line.m_flags & TransportLine.Flags.DisabledDay) == TransportLine.Flags.None;
            bool night = (line.m_flags & TransportLine.Flags.DisabledNight) == TransportLine.Flags.None;
            bool complete; TransportStopV2[] stops = CaptureStops(lineId, out complete);
            return TransportLineIntentV2.CreateLine(info.name, color.r, color.g, color.b, color.a,
                line.m_budget, line.m_ticketPrice, day, night, complete, stops);
        }

        public static void ApplyProperties(LoadIdentity load, ushort lineId, TransportLineIntentV2 value)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || !Live(lineId)) throw new InvalidOperationException("Transport line apply is stale or missing.");
            TransportManager manager = TransportManager.instance;
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id))
            {
                TransportLine line = manager.m_lines.m_buffer[lineId];
                line.m_color = new Color32(value.Red, value.Green, value.Blue, value.Alpha);
                line.m_flags |= TransportLine.Flags.CustomColor;
                line.m_budget = value.Budget;
                line.m_ticketPrice = value.TicketPrice;
                line.SetActive(value.Day, value.Night);
                manager.m_lines.m_buffer[lineId] = line;
            }
        }

        public static bool ApplyRouteIntent(LoadIdentity load, ushort lineId, TransportLineIntentV2 value)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || !Live(lineId)) return false;
            TransportManager manager = TransportManager.instance; bool result = false;
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id))
            {
                if (value.Kind == TransportLineIntentKindV2.AddStop)
                {
                    Vector3 position = new Vector3(value.Stop.X, value.Stop.Y, value.Stop.Z);
                    result = manager.m_lines.m_buffer[lineId].AddStop(lineId, value.StopIndex, position, value.Stop.FixedPlatform);
                }
                else if (value.Kind == TransportLineIntentKindV2.RemoveStop)
                    result = manager.m_lines.m_buffer[lineId].RemoveStop(lineId, value.StopIndex);
                else if (value.Kind == TransportLineIntentKindV2.MoveStop)
                {
                    Vector3 oldPosition;
                    Vector3 position = new Vector3(value.Stop.X, value.Stop.Y, value.Stop.Z);
                    result = manager.m_lines.m_buffer[lineId].MoveStop(lineId, value.StopIndex, position, value.Stop.FixedPlatform, out oldPosition);
                }
                if (result) manager.UpdateLine(lineId);
            }
            return result;
        }

        public static ushort CreateAuthority(LoadIdentity load, TransportLineIntentV2 intent)
        {
            if (intent == null || intent.Kind != TransportLineIntentKindV2.Create) throw new ArgumentException("Expected transport create intent.", "intent");
            TransportManager manager = TransportManager.instance; SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("Transport services are unavailable.");
            TransportInfo info = ResolvePrefab(intent.PrefabKey); ushort lineId;
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id))
            {
                if (!manager.CreateLine(out lineId, ref simulation.m_randomizer, info, true)) return 0;
            }
            try
            {
                EntityIdentityV2 temporary = new EntityIdentityV2(ulong.MaxValue, 1);
                TransportLineStateV2 desired = new TransportLineStateV2(temporary, intent.PrefabKey,
                    intent.Red, intent.Green, intent.Blue, intent.Alpha, intent.Budget, intent.TicketPrice,
                    intent.Day, intent.Night, intent.Complete, intent.Stops);
                ApplyRawState(load, lineId, desired);
                return lineId;
            }
            catch
            {
                try { using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id)) manager.ReleaseLine(lineId); } catch { }
                throw;
            }
        }

        public static ushort CreateReplica(LoadIdentity load, TransportLineStateV2 state)
        {
            TransportManager manager = TransportManager.instance; SimulationManager simulation = SimulationManager.instance;
            if (manager == null || simulation == null) throw new InvalidOperationException("Transport services are unavailable.");
            TransportInfo info = ResolvePrefab(state.PrefabKey); Randomizer random = simulation.m_randomizer; ushort lineId;
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id))
            {
                if (!manager.CreateLine(out lineId, ref random, info, true)) throw new InvalidOperationException("CS1 rejected replica line creation.");
            }
            ApplyRawState(load, lineId, state);
            return lineId;
        }

        public static void ApplyState(LoadIdentity load, ushort lineId, TransportLineStateV2 state)
        {
            ApplyRawState(load, lineId, state);
        }

        private static void ApplyRawState(LoadIdentity load, ushort lineId, TransportLineStateV2 state)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || !Live(lineId)) throw new InvalidOperationException("Transport state apply is stale or missing.");
            TransportManager manager = TransportManager.instance;
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id))
            {
                TransportLine line = manager.m_lines.m_buffer[lineId];
                line.m_color = new Color32(state.Red, state.Green, state.Blue, state.Alpha);
                line.m_flags |= TransportLine.Flags.CustomColor;
                line.m_budget = state.Budget; line.m_ticketPrice = state.TicketPrice;
                line.SetActive(state.Day, state.Night); manager.m_lines.m_buffer[lineId] = line;

                int guard = 0;
                while (manager.m_lines.m_buffer[lineId].m_stops != 0 && guard++ < 600)
                {
                    if (!manager.m_lines.m_buffer[lineId].RemoveStop(lineId, 0))
                        throw new InvalidOperationException("Could not clear transport route.");
                }
                if (manager.m_lines.m_buffer[lineId].m_stops != 0)
                    throw new InvalidOperationException("Transport route clear guard exhausted.");
                for (int i = 0; i < state.Stops.Length; i++)
                {
                    TransportStopV2 stop = state.Stops[i]; Vector3 pos = new Vector3(stop.X, stop.Y, stop.Z);
                    if (!manager.m_lines.m_buffer[lineId].AddStop(lineId, i, pos, stop.FixedPlatform))
                        throw new InvalidOperationException("Could not project transport stop " + i + ".");
                }
                if (state.Complete && state.Stops.Length != 0)
                {
                    TransportStopV2 first = state.Stops[0];
                    if (!manager.m_lines.m_buffer[lineId].AddStop(lineId, -1, new Vector3(first.X, first.Y, first.Z), first.FixedPlatform))
                        throw new InvalidOperationException("Could not close transport route.");
                }
                manager.UpdateLine(lineId);
            }
        }

        public static void Release(LoadIdentity load, ushort lineId)
        {
            if (!Live(lineId)) throw new InvalidOperationException("Transport line is already absent.");
            using (RuntimeScopeGuard.EnterApply(load, TransportLineAuthorityDomain.Id)) TransportManager.instance.ReleaseLine(lineId);
            if (Live(lineId)) throw new InvalidOperationException("CS1 did not release the transport line.");
        }

        public static bool Equivalent(TransportLineStateV2 a, TransportLineStateV2 b)
        {
            if (a == null || b == null || !a.Entity.Equals(b.Entity) || a.PrefabKey != b.PrefabKey ||
                a.Red != b.Red || a.Green != b.Green || a.Blue != b.Blue || a.Alpha != b.Alpha ||
                a.Budget != b.Budget || a.TicketPrice != b.TicketPrice || a.Day != b.Day || a.Night != b.Night ||
                a.Complete != b.Complete || a.Stops.Length != b.Stops.Length) return false;
            for (int i = 0; i < a.Stops.Length; i++)
            {
                TransportStopV2 x = a.Stops[i], y = b.Stops[i];
                if (x.X != y.X || x.Y != y.Y || x.Z != y.Z || x.FixedPlatform != y.FixedPlatform) return false;
            }
            return true;
        }
    }

    public abstract class TransportLineDomainBase
    {
        protected readonly LoadIdentity Load;
        protected readonly EntityIdMapV2 Ids = new EntityIdMapV2();
        protected readonly Dictionary<ulong, TransportLineStateV2> Committed = new Dictionary<ulong, TransportLineStateV2>();

        protected TransportLineDomainBase(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            Load = load; RuntimeServices.EntityMaps.AttachDomain(TransportLineAuthorityDomain.Id, Ids);
            if (Ids.Count == 0) SeedExisting(); else ValidateRestored();
            RefreshCommitted();
        }

        public bool TryResolveEntity(ushort nativeId, out EntityIdentityV2 entity) { return Ids.TryGetIdentity(nativeId, out entity); }

        protected Hash256 CaptureRoot()
        {
            TransportLineStateIndexV2 index = new TransportLineStateIndexV2();
            EntityMapEntryV2[] entries = Ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                uint native = entries[i].NativeId;
                if (native == 0 || native > ushort.MaxValue) throw new InvalidOperationException("Transport line native id is invalid.");
                index.Seed(TransportLineGameAccess.Capture(entries[i].Identity, (ushort)native));
            }
            return index.Root;
        }

        protected void RefreshCommitted()
        {
            Committed.Clear(); EntityMapEntryV2[] entries = Ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                TransportLineStateV2 state = TransportLineGameAccess.Capture(entries[i].Identity, (ushort)entries[i].NativeId);
                Committed[state.Entity.EntityId] = state;
            }
        }

        protected TransportLineMutationV2 Reconcile()
        {
            List<TransportLineStateV2> upserts = new List<TransportLineStateV2>();
            List<EntityIdentityV2> deletes = new List<EntityIdentityV2>();
            EntityMapEntryV2[] old = Ids.SnapshotEntries();
            for (int i = 0; i < old.Length; i++)
            {
                ushort native = (ushort)old[i].NativeId;
                if (!TransportLineGameAccess.Live(native))
                {
                    if (Ids.Retire(old[i].Identity)) deletes.Add(old[i].Identity);
                    continue;
                }
                TransportLineStateV2 now = TransportLineGameAccess.Capture(old[i].Identity, native);
                TransportLineStateV2 before;
                if (!Committed.TryGetValue(now.Entity.EntityId, out before) || !TransportLineGameAccess.Equivalent(before, now)) upserts.Add(now);
            }
            int size = checked((int)TransportManager.instance.m_lines.m_size);
            for (int i = 1; i < size; i++)
            {
                ushort native = (ushort)i; if (!TransportLineGameAccess.Live(native)) continue;
                EntityIdentityV2 entity;
                if (!Ids.TryGetIdentity(native, out entity))
                {
                    if (RuntimeServices.Multiplayer.IsPendingLocalTransportLine(native)) continue;
                    entity = Ids.Allocate(native); upserts.Add(TransportLineGameAccess.Capture(entity, native));
                }
            }
            upserts.Sort(delegate(TransportLineStateV2 a, TransportLineStateV2 b) { return a.Entity.EntityId.CompareTo(b.Entity.EntityId); });
            deletes.Sort(delegate(EntityIdentityV2 a, EntityIdentityV2 b) { return a.EntityId.CompareTo(b.EntityId); });
            RefreshCommitted(); return new TransportLineMutationV2(upserts.ToArray(), deletes.ToArray());
        }

        private void SeedExisting()
        {
            TransportManager manager = TransportManager.instance;
            if (manager == null) throw new InvalidOperationException("TransportManager is unavailable.");
            int size = checked((int)manager.m_lines.m_size);
            for (int i = 1; i < size; i++) if (TransportLineGameAccess.Live((ushort)i)) Ids.Allocate((uint)i);
        }

        private void ValidateRestored()
        {
            EntityMapEntryV2[] entries = Ids.SnapshotEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].NativeId == 0 || entries[i].NativeId > ushort.MaxValue || !TransportLineGameAccess.Live((ushort)entries[i].NativeId))
                    throw new InvalidOperationException("Saved transport line identity no longer maps to a live line.");
                TransportLineGameAccess.Capture(entries[i].Identity, (ushort)entries[i].NativeId);
            }
        }
    }

    public sealed class TransportLineAuthorityDomain : TransportLineDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 50;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CaptureRoot(); } }
        public TransportLineAuthorityDomain(LoadIdentity load) : base(load) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return DomainExecutionV2.Rejected();
            TransportLineIntentV2 intent; try { intent = TransportLineDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            if (intent.Kind == TransportLineIntentKindV2.Create)
            {
                ushort created = TransportLineGameAccess.CreateAuthority(Load, intent);
                if (created == 0) return DomainExecutionV2.Rejected();
                EntityIdentityV2 entity = Ids.Allocate(created);
                TransportLineStateV2 state = TransportLineGameAccess.Capture(entity, created);
                RefreshCommitted();
                return DomainExecutionV2.Success(TransportLineDomainCodecV2.EncodeMutation(
                    new TransportLineMutationV2(new[] { state }, new EntityIdentityV2[0])), StateRoot);
            }
            uint native; if (!Ids.TryGetNative(intent.Target, out native) || native == 0 || native > ushort.MaxValue) return DomainExecutionV2.Rejected();
            ushort lineId = (ushort)native;
            if (intent.Kind == TransportLineIntentKindV2.Release)
            {
                TransportLineGameAccess.Release(Load, lineId);
                if (!Ids.Retire(intent.Target)) throw new InvalidOperationException("Transport identity retirement failed.");
                RefreshCommitted();
                return DomainExecutionV2.Success(TransportLineDomainCodecV2.EncodeMutation(
                    new TransportLineMutationV2(new TransportLineStateV2[0], new[] { intent.Target })), StateRoot);
            }
            if (intent.Kind == TransportLineIntentKindV2.SetProperties) TransportLineGameAccess.ApplyProperties(Load, lineId, intent);
            else if (!TransportLineGameAccess.ApplyRouteIntent(Load, lineId, intent)) return DomainExecutionV2.Rejected();
            TransportLineStateV2 actual = TransportLineGameAccess.Capture(intent.Target, lineId);
            RefreshCommitted();
            return DomainExecutionV2.Success(TransportLineDomainCodecV2.EncodeMutation(
                new TransportLineMutationV2(new[] { actual }, new EntityIdentityV2[0])), StateRoot);
        }

        internal TransportLineMutationV2 ObserveHostWorld()
        {
            TransportLineMutationV2 mutation = Reconcile(); return mutation.Count == 0 ? null : mutation;
        }
    }

    public sealed class TransportLineReplicaDomain : TransportLineDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return TransportLineAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CaptureRoot(); } }
        public TransportLineReplicaDomain(LoadIdentity load) : base(load) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica transport apply is invalid in current role.");
            TransportLineMutationV2 mutation = TransportLineDomainCodecV2.DecodeMutation(absoluteDelta);
            for (int i = 0; i < mutation.Deletes.Length; i++)
            {
                uint native; if (!Ids.TryGetNative(mutation.Deletes[i], out native) || native == 0 || native > ushort.MaxValue)
                    throw new InvalidOperationException("Replica cannot delete unknown transport line.");
                TransportLineGameAccess.Release(Load, (ushort)native);
                if (!Ids.Retire(mutation.Deletes[i])) throw new InvalidOperationException("Replica transport identity retirement failed.");
            }
            for (int i = 0; i < mutation.Upserts.Length; i++)
            {
                TransportLineStateV2 state = mutation.Upserts[i]; uint native;
                if (!Ids.TryGetNative(state.Entity, out native))
                {
                    ushort pending;
                    if (RuntimeServices.Multiplayer.TryTakeCommittedPendingTransportLine(out pending))
                    {
                        Ids.BindKnown(state.Entity, pending); native = pending; TransportLineGameAccess.ApplyState(Load, pending, state);
                    }
                    else
                    {
                        ushort created = TransportLineGameAccess.CreateReplica(Load, state); Ids.BindKnown(state.Entity, created); native = created;
                    }
                }
                else TransportLineGameAccess.ApplyState(Load, (ushort)native, state);
                TransportLineStateV2 actual = TransportLineGameAccess.Capture(state.Entity, (ushort)native);
                if (!TransportLineGameAccess.Equivalent(state, actual)) throw new InvalidOperationException("Replica transport projection mismatch.");
            }
            RefreshCommitted();
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Transport line projection root mismatch.");
        }
    }
}
