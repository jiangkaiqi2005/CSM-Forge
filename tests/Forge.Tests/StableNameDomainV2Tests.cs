using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class StableNameDomainV2Tests
    {
        [Case]
        public static void StableNameRoundTripsUnicodeAndIdentity()
        {
            StableNameKeyV2 key = new StableNameKeyV2(StableNameTargetKindV2.Building,
                new EntityIdentityV2(123, 4));
            StableNameStateV2 value = new StableNameStateV2(key, "青萍中央车站");
            StableNameStateV2 copy = StableNameCodecV2.Decode(StableNameCodecV2.Encode(value));
            Assert.Equal(key, copy.Key);
            Assert.Equal("青萍中央车站", copy.Name);
        }

        [Case]
        public static void SparseNameRootIsCanonicalAndEmptyNameRemovesEntry()
        {
            StableNameStateV2 a = new StableNameStateV2(
                new StableNameKeyV2(StableNameTargetKindV2.NetSegment, new EntityIdentityV2(5, 1)), "北山路");
            StableNameStateV2 b = new StableNameStateV2(
                new StableNameKeyV2(StableNameTargetKindV2.TransportLine, new EntityIdentityV2(2, 3)), "二号线");
            StableNameStateIndexV2 first = new StableNameStateIndexV2(); first.Set(a); first.Set(b);
            StableNameStateIndexV2 second = new StableNameStateIndexV2(); second.Set(b); second.Set(a);
            Assert.Equal(first.Root, second.Root);
            Assert.Equal(2, first.Count);
            first.Set(new StableNameStateV2(a.Key, string.Empty));
            Assert.Equal(1, first.Count);
            string ignored;
            Assert.True(!first.TryGet(a.Key, out ignored));
        }

        [Case]
        public static void DifferentKindsWithSameEntityRemainDistinct()
        {
            EntityIdentityV2 id = new EntityIdentityV2(8, 1);
            StableNameStateIndexV2 index = new StableNameStateIndexV2();
            index.Set(new StableNameStateV2(new StableNameKeyV2(StableNameTargetKindV2.Building, id), "建筑"));
            index.Set(new StableNameStateV2(new StableNameKeyV2(StableNameTargetKindV2.District, id), "行政区"));
            Assert.Equal(2, index.Count);
        }

        [Case]
        public static void StableNameRejectsMalformedAndOversizedPayloads()
        {
            Assert.Throws<InvalidDataException>(delegate { StableNameCodecV2.Decode(new byte[1]); });
            Assert.Throws<ArgumentException>(delegate
            {
                new StableNameStateV2(new StableNameKeyV2(StableNameTargetKindV2.Building,
                    new EntityIdentityV2(1, 1)), new string('x', 513));
            });
        }
    }
}
