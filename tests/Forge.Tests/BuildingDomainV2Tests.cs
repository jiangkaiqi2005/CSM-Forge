using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class BuildingDomainV2Tests
    {
        [Case]
        public static void CreateAndDeleteRoundTripUseForgeEntityIdentity()
        {
            BuildingIntentV2 create = BuildingIntentV2.Create("building:vanilla:clinic", 12.5f, 7f, -2f, 1.25f, 4);
            BuildingIntentV2 decodedIntent = BuildingDomainCodecV2.DecodeIntent(BuildingDomainCodecV2.EncodeIntent(create));
            Assert.Equal(BuildingIntentKindV2.Create, decodedIntent.Kind);
            Assert.Equal("building:vanilla:clinic", decodedIntent.PrefabKey);

            EntityIdentityV2 entity = new EntityIdentityV2(44, 2);
            BuildingStateV2 state = new BuildingStateV2(entity, create.PrefabKey, create.X, create.Y, create.Z, create.Angle, create.Length);
            BuildingResultV2 created = BuildingResultV2.Created(state);
            BuildingResultV2 decodedResult = BuildingDomainCodecV2.DecodeResult(BuildingDomainCodecV2.EncodeResult(created));
            Assert.Equal(entity, decodedResult.Entity);
            Assert.Equal(state.PrefabKey, decodedResult.State.PrefabKey);

            BuildingIntentV2 delete = BuildingIntentV2.Delete(entity);
            BuildingIntentV2 decodedDelete = BuildingDomainCodecV2.DecodeIntent(BuildingDomainCodecV2.EncodeIntent(delete));
            Assert.Equal(entity, decodedDelete.Entity);
        }

        [Case]
        public static void BuildingRootIsCanonicalAcrossInsertionOrder()
        {
            BuildingStateV2 a = new BuildingStateV2(new EntityIdentityV2(10, 1), "a", 1, 2, 3, 0.5f, 2);
            BuildingStateV2 b = new BuildingStateV2(new EntityIdentityV2(3, 1), "b", 4, 5, 6, 1f, 3);
            BuildingStateIndexV2 first = new BuildingStateIndexV2(); first.Seed(a); first.Seed(b);
            BuildingStateIndexV2 second = new BuildingStateIndexV2(); second.Seed(b); second.Seed(a);
            Assert.Equal(first.Root, second.Root);
        }

        [Case]
        public static void DeleteStaleGenerationAndTrailingPayloadFailClosed()
        {
            BuildingStateIndexV2 index = new BuildingStateIndexV2();
            EntityIdentityV2 live = new EntityIdentityV2(7, 2);
            index.Seed(new BuildingStateV2(live, "building:test", 0, 0, 0, 0, 1));
            Assert.Throws<InvalidOperationException>(delegate { index.Apply(BuildingResultV2.Deleted(new EntityIdentityV2(7, 1))); });

            byte[] encoded = BuildingDomainCodecV2.EncodeIntent(BuildingIntentV2.Delete(live));
            byte[] tainted = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, tainted, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { BuildingDomainCodecV2.DecodeIntent(tainted); });
        }
    }
}
