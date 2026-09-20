using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-1.5 regression: EntityIdMapV2 caches its canonically ordered snapshot. Polling paths
    /// call SnapshotEntries every simulation tick over buffers as large as 49,152 buildings, so
    /// the cache must be both fast AND correct: a mutation must invalidate it, and a reference
    /// held across a mutation must not be rewritten in place (the reconcile loops iterate an
    /// older snapshot while allocating/retiring).
    /// </summary>
    public static class EntityMapSnapshotCacheTests
    {
        [Case] public static void RepeatedSnapshotsReturnTheCachedInstance()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            map.Allocate(10);
            map.Allocate(20);
            EntityMapEntryV2[] first = map.SnapshotEntries();
            Assert.True(ReferenceEquals(first, map.SnapshotEntries()));
        }

        [Case] public static void AllocateInvalidatesTheCache()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            EntityIdentityV2 a = map.Allocate(10);
            EntityMapEntryV2[] before = map.SnapshotEntries();
            Assert.Equal(1, before.Length);

            EntityIdentityV2 b = map.Allocate(20);
            EntityMapEntryV2[] after = map.SnapshotEntries();
            Assert.True(!ReferenceEquals(before, after)); // rebuilt, not mutated in place
            Assert.Equal(2, after.Length);
            Assert.Equal(1, before.Length);               // the old reference is untouched
        }

        [Case] public static void RetireInvalidatesTheCache()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            EntityIdentityV2 a = map.Allocate(10);
            map.Allocate(20);
            Assert.Equal(2, map.SnapshotEntries().Length);
            Assert.True(map.Retire(a));
            EntityMapEntryV2[] after = map.SnapshotEntries();
            Assert.Equal(1, after.Length);
            Assert.True(a.EntityId != after[0].Identity.EntityId);
        }

        [Case] public static void SnapshotStaysOrderedByEntityId()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            EntityIdentityV2 first = map.Allocate(700);
            EntityIdentityV2 second = map.Allocate(3);   // allocated later, lower native id
            EntityMapEntryV2[] entries = map.SnapshotEntries();
            Assert.Equal(2, entries.Length);
            Assert.True(entries[0].Identity.EntityId < entries[1].Identity.EntityId);
            Assert.Equal(first.EntityId, entries[0].Identity.EntityId);
            Assert.Equal(second.EntityId, entries[1].Identity.EntityId);
        }

        [Case] public static void BindKnownInvalidatesAndClearResetsTheCache()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            map.Allocate(10);
            EntityMapEntryV2[] before = map.SnapshotEntries();
            map.BindKnown(new EntityIdentityV2(500, 1), 77);
            Assert.True(!ReferenceEquals(before, map.SnapshotEntries()));
            Assert.Equal(2, map.SnapshotEntries().Length);

            map.ClearForSnapshotRestore();
            Assert.Equal(0, map.SnapshotEntries().Length);
        }
    }
}
