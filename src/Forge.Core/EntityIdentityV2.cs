using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    /// <summary>
    /// Stable cross-replica entity identity. Native CS1 manager indices are strictly local
    /// implementation details and must never be treated as a wire identity.
    /// </summary>
    public struct EntityIdentityV2 : IEquatable<EntityIdentityV2>
    {
        public readonly ulong EntityId;
        public readonly uint Generation;

        public EntityIdentityV2(ulong entityId, uint generation)
        {
            if (entityId == 0 || generation == 0) throw new ArgumentException("Entity identity is incomplete.");
            EntityId = entityId;
            Generation = generation;
        }

        public bool IsValid { get { return EntityId != 0 && Generation != 0; } }
        public bool Equals(EntityIdentityV2 other) { return EntityId == other.EntityId && Generation == other.Generation; }
        public override bool Equals(object obj) { return obj is EntityIdentityV2 && Equals((EntityIdentityV2)obj); }
        public override int GetHashCode() { return EntityId.GetHashCode() ^ Generation.GetHashCode(); }
        public override string ToString() { return EntityId + ":" + Generation; }
    }

    public sealed class EntityMapEntryV2
    {
        public EntityIdentityV2 Identity { get; private set; }
        public uint NativeId { get; private set; }

        public EntityMapEntryV2(EntityIdentityV2 identity, uint nativeId)
        {
            Check.Condition(!identity.IsValid, "identity", "Invalid entity identity.");
            Check.OutOfRange(nativeId == 0, "nativeId");
            Identity = identity;
            NativeId = nativeId;
        }
    }

    /// <summary>
    /// Owner-thread mapping between Forge entity identities and process-local CS1 manager IDs.
    /// Retired Forge IDs are never silently rebound to another native object. Snapshot restore may
    /// explicitly BindKnown identities after validating that the restored entity really matches.
    /// </summary>
    public sealed class EntityIdMapV2
    {
        private sealed class Entry
        {
            public EntityIdentityV2 Identity;
            public uint NativeId;
        }

        private readonly Dictionary<ulong, Entry> byEntity = new Dictionary<ulong, Entry>();
        private readonly Dictionary<uint, Entry> byNative = new Dictionary<uint, Entry>();
        private readonly HashSet<ulong> retired = new HashSet<ulong>();
        private ulong nextEntityId;

        private EntityMapEntryV2[] cachedEntries;

        public int Count { get { return byEntity.Count; } }
        public ulong HighestIssuedId { get { return nextEntityId; } }

        public EntityIdentityV2 Allocate(uint nativeId)
        {
            Check.OutOfRange(nativeId == 0, "nativeId");
            if (byNative.ContainsKey(nativeId)) throw new InvalidOperationException("Native ID is already bound.");
            if (nextEntityId == ulong.MaxValue) throw new InvalidOperationException("Entity identity space exhausted.");
            EntityIdentityV2 identity = new EntityIdentityV2(++nextEntityId, 1);
            Entry entry = new Entry { Identity = identity, NativeId = nativeId };
            byEntity.Add(identity.EntityId, entry);
            byNative.Add(nativeId, entry);
            cachedEntries = null;
            return identity;
        }

        public void BindKnown(EntityIdentityV2 identity, uint nativeId)
        {
            Check.Condition(!identity.IsValid, "identity", "Invalid entity identity.");
            Check.OutOfRange(nativeId == 0, "nativeId");
            if (retired.Contains(identity.EntityId)) throw new InvalidOperationException("Retired entity identity cannot be rebound.");
            Entry current;
            if (byEntity.TryGetValue(identity.EntityId, out current))
            {
                if (!current.Identity.Equals(identity) || current.NativeId != nativeId)
                    throw new InvalidOperationException("Entity identity is already bound differently.");
                return;
            }
            if (byNative.ContainsKey(nativeId)) throw new InvalidOperationException("Native ID is already bound to another entity.");
            Entry entry = new Entry { Identity = identity, NativeId = nativeId };
            byEntity.Add(identity.EntityId, entry);
            byNative.Add(nativeId, entry);
            if (identity.EntityId > nextEntityId) nextEntityId = identity.EntityId;
            cachedEntries = null;
        }

        public bool TryGetNative(EntityIdentityV2 identity, out uint nativeId)
        {
            nativeId = 0;
            if (!identity.IsValid) return false;
            Entry entry;
            if (!byEntity.TryGetValue(identity.EntityId, out entry) || !entry.Identity.Equals(identity)) return false;
            nativeId = entry.NativeId;
            return true;
        }

        public bool TryGetIdentity(uint nativeId, out EntityIdentityV2 identity)
        {
            identity = default(EntityIdentityV2);
            Entry entry;
            if (nativeId == 0 || !byNative.TryGetValue(nativeId, out entry)) return false;
            identity = entry.Identity;
            return true;
        }

        public bool Retire(EntityIdentityV2 identity)
        {
            if (!identity.IsValid) return false;
            Entry entry;
            if (!byEntity.TryGetValue(identity.EntityId, out entry) || !entry.Identity.Equals(identity)) return false;
            byEntity.Remove(identity.EntityId);
            byNative.Remove(entry.NativeId);
            retired.Add(identity.EntityId);
            cachedEntries = null;
            return true;
        }

        public bool IsRetired(ulong entityId) { return entityId != 0 && retired.Contains(entityId); }

        /// <summary>
        /// Canonically ordered (by entity id) mapping snapshot. WP-1.5: the result is cached and
        /// invalidated by every mutation. Polling paths call this every simulation tick over
        /// buffers as large as 49,152 buildings, so rebuilding it per call meant an O(n log n)
        /// sort plus two allocations per tick - the dominant host-side frame cost.
        ///
        /// The returned array is shared: callers must treat it as read-only (no code mutates it;
        /// the cursor-based polls only read entries and length). A mutation nulls the cache and
        /// the next call builds a fresh array, so a caller holding an older reference is never
        /// affected - which is what the reconcile loops rely on.
        /// </summary>
        public EntityMapEntryV2[] SnapshotEntries()
        {
            if (cachedEntries != null) return cachedEntries;
            List<ulong> ids = new List<ulong>(byEntity.Keys);
            ids.Sort();
            EntityMapEntryV2[] result = new EntityMapEntryV2[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                Entry entry = byEntity[ids[i]];
                result[i] = new EntityMapEntryV2(entry.Identity, entry.NativeId);
            }
            cachedEntries = result;
            return result;
        }

        public void RestoreSnapshot(IEnumerable<EntityMapEntryV2> entries, ulong highestIssuedId)
        {
            Check.NotNull(entries, "entries");
            byEntity.Clear();
            byNative.Clear();
            retired.Clear();
            cachedEntries = null;
            nextEntityId = 0;
            foreach (EntityMapEntryV2 value in entries)
            {
                Check.Condition(value == null, "entries", "Snapshot contains a null entity mapping.");
                BindKnown(value.Identity, value.NativeId);
            }
            if (highestIssuedId < nextEntityId)
                throw new InvalidOperationException("Snapshot entity ID watermark precedes a live identity.");
            nextEntityId = highestIssuedId;
        }

        public void ClearForSnapshotRestore()
        {
            byEntity.Clear();
            byNative.Clear();
            retired.Clear();
            cachedEntries = null;
            nextEntityId = 0;
        }
    }
}
