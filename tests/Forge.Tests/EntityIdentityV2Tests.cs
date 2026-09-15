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
    }
}
