using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class DistrictPolicyV2Tests
    {
        [Case]
        public static void PolicyIntentRoundTripsCityAndStableDistrictTargets()
        {
            EntityIdentityV2 district = new EntityIdentityV2(42, 3);
            DistrictPolicyIntentV2 local = new DistrictPolicyIntentV2(district, 17, true);
            DistrictPolicyIntentV2 copy = DistrictPolicyCodecV2.Decode(DistrictPolicyCodecV2.Encode(local));
            Assert.Equal(DistrictPolicyTargetKindV2.District, copy.TargetKind);
            Assert.Equal(district, copy.District);
            Assert.Equal(17, copy.PolicyValue);
            Assert.True(copy.Enabled);

            DistrictPolicyIntentV2 city = DistrictPolicyIntentV2.City(23, false);
            DistrictPolicyIntentV2 cityCopy = DistrictPolicyCodecV2.Decode(DistrictPolicyCodecV2.Encode(city));
            Assert.Equal(DistrictPolicyTargetKindV2.City, cityCopy.TargetKind);
            Assert.True(!cityCopy.District.IsValid && !cityCopy.Enabled);
        }

        [Case]
        public static void PolicySnapshotRootIsCanonicalAndRoundTripsAllMasks()
        {
            DistrictPolicyStateV2 a = new DistrictPolicyStateV2(new EntityIdentityV2(10, 1), 1, 2, 3, 4);
            DistrictPolicyStateV2 b = new DistrictPolicyStateV2(new EntityIdentityV2(2, 7), 5, 6, 7, 8);
            DistrictPolicySnapshotV2 first = new DistrictPolicySnapshotV2(11, 12, 13, 14, new[] { a, b });
            DistrictPolicySnapshotV2 second = new DistrictPolicySnapshotV2(11, 12, 13, 14, new[] { b, a });
            Assert.Equal(first.Root, second.Root);

            DistrictPolicySnapshotV2 copy = DistrictPolicyEnvelopeCodecV2.DecodePolicySnapshot(
                DistrictPolicyEnvelopeCodecV2.EncodePolicySnapshot(first));
            Assert.Equal(first.Root, copy.Root);
            Assert.Equal((ulong)11, copy.CityServices);
            DistrictPolicyStateV2 found;
            Assert.True(copy.TryGet(a.District, out found));
            Assert.Equal((ulong)4, found.Special);
        }

        [Case]
        public static void DistrictEnvelopeCarriesChildRootMutationAndPolicies()
        {
            EntityIdentityV2 district = new EntityIdentityV2(9, 1);
            DistrictMutationV2 mutation = new DistrictMutationV2(
                new[] { new DistrictEntityStateV2(district, 77, 2) },
                new EntityIdentityV2[0], new DistrictCellStateV2[0]);
            DistrictPolicySnapshotV2 policies = new DistrictPolicySnapshotV2(1, 2, 3, 4,
                new[] { new DistrictPolicyStateV2(district, 5, 6, 7, 8) });
            Hash256 childRoot = Hash256.Compute(new byte[] { 1, 2, 3 });
            DistrictAuthorityEnvelopeV2 envelope = new DistrictAuthorityEnvelopeV2(mutation, childRoot, policies);
            DistrictAuthorityEnvelopeV2 copy = DistrictPolicyEnvelopeCodecV2.DecodeEnvelope(
                DistrictPolicyEnvelopeCodecV2.EncodeEnvelope(envelope));
            Assert.Equal(childRoot, copy.DistrictAfterRoot);
            Assert.Equal(1, copy.DistrictMutation.UpsertEntities.Length);
            Assert.Equal(policies.Root, copy.Policies.Root);
            Assert.Equal(DistrictCombinedRootV2.Combine(childRoot, policies.Root),
                DistrictCombinedRootV2.Combine(copy.DistrictAfterRoot, copy.Policies.Root));
        }

        [Case]
        public static void PolicyCodecsRejectMalformedPayloads()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                new DistrictPolicyIntentV2(new EntityIdentityV2(1, 1), 0, true);
            });
            Assert.Throws<InvalidDataException>(delegate { DistrictPolicyCodecV2.Decode(new byte[1]); });
            Assert.Throws<InvalidDataException>(delegate { DistrictPolicyEnvelopeCodecV2.DecodeEnvelope(new byte[8]); });
        }
    }
}
