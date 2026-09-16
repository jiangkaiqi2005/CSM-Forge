using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class EntityIdentityV2Tests
    {
        [Case]
        public static void HostAndReplicaCanMapSameForgeEntityToDifferentNativeIds()
        {
            EntityIdMapV2 host = new EntityIdMapV2();
            EntityIdMapV2 replica = new EntityIdMapV2();
            EntityIdentityV2 identity = host.Allocate(17);
            replica.BindKnown(identity, 4021);
            uint hostNative, replicaNative;
            Assert.True(host.TryGetNative(identity, out hostNative));
            Assert.True(replica.TryGetNative(identity, out replicaNative));
            Assert.Equal((uint)17, hostNative);
            Assert.Equal((uint)4021, replicaNative);
        }

        [Case]
        public static void RetiredIdentityCannotBeSilentlyReboundAfterNativeIdReuse()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            EntityIdentityV2 oldEntity = map.Allocate(9);
            Assert.True(map.Retire(oldEntity));
            EntityIdentityV2 replacement = map.Allocate(9);
            Assert.True(oldEntity.EntityId != replacement.EntityId);
            Assert.True(map.IsRetired(oldEntity.EntityId));
            Assert.Throws<InvalidOperationException>(delegate { map.BindKnown(oldEntity, 10); });
        }

        [Case]
        public static void KnownSnapshotBindingIsIdempotentButConflictsFailClosed()
        {
            EntityIdMapV2 map = new EntityIdMapV2();
            EntityIdentityV2 identity = new EntityIdentityV2(77, 3);
            map.BindKnown(identity, 12);
            map.BindKnown(identity, 12);
            Assert.Throws<InvalidOperationException>(delegate { map.BindKnown(identity, 13); });
            Assert.Throws<InvalidOperationException>(delegate { map.BindKnown(new EntityIdentityV2(78, 1), 12); });
        }

        [Case]
        public static void SnapshotRestorePreservesBindingsAndAllocationWatermark()
        {
            EntityIdMapV2 source = new EntityIdMapV2();
            EntityIdentityV2 first = source.Allocate(17);
            EntityIdentityV2 second = source.Allocate(44);
            EntityMapEntryV2[] snapshot = source.SnapshotEntries();
            ulong watermark = source.HighestIssuedId;

            EntityIdMapV2 restored = new EntityIdMapV2();
            restored.RestoreSnapshot(snapshot, watermark + 10);

            uint native;
            Assert.True(restored.TryGetNative(first, out native));
            Assert.Equal((uint)17, native);
            Assert.True(restored.TryGetNative(second, out native));
            Assert.Equal((uint)44, native);
            EntityIdentityV2 next = restored.Allocate(99);
            Assert.Equal(watermark + 11, next.EntityId);
        }

        [Case]
        public static void SnapshotRestoreRejectsDuplicateNativeAndRegressedWatermark()
        {
            EntityIdMapV2 restored = new EntityIdMapV2();
            EntityMapEntryV2[] duplicateNative = new EntityMapEntryV2[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(5, 1), 20),
                new EntityMapEntryV2(new EntityIdentityV2(6, 1), 20)
            };
            Assert.Throws<InvalidOperationException>(delegate { restored.RestoreSnapshot(duplicateNative, 6); });

            EntityMapEntryV2[] one = new EntityMapEntryV2[]
            {
                new EntityMapEntryV2(new EntityIdentityV2(50, 2), 21)
            };
            Assert.Throws<InvalidOperationException>(delegate { restored.RestoreSnapshot(one, 49); });
        }
    }
}
