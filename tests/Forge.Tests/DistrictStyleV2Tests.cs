using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class DistrictStyleV2Tests
    {
        [Case]
        public static void StyleIntentRoundTripsWithStableDistrictIdentity()
        {
            EntityIdentityV2 district = new EntityIdentityV2(44, 3);
            DistrictStyleIntentV2 decoded = DistrictStyleCodecV2.Decode(
                DistrictStyleCodecV2.Encode(new DistrictStyleIntentV2(district, 17)));
            Assert.Equal(district, decoded.District);
            Assert.Equal((ushort)17, decoded.Style);
        }

        [Case]
        public static void StyleIntentRejectsMalformedPayload()
        {
            byte[] encoded = DistrictStyleCodecV2.Encode(new DistrictStyleIntentV2(new EntityIdentityV2(2, 1), 5));
            encoded[0] = 0;
            Assert.Throws<InvalidDataException>(delegate { DistrictStyleCodecV2.Decode(encoded); });
        }
    }
}
