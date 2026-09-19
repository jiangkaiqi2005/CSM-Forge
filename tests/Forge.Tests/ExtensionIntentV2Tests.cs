using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class ExtensionIntentV2Tests
    {
        [Case]
        public static void IntentRoundTripsCanonically()
        {
            ExtensionPlayerIntentV2 value = new ExtensionPlayerIntentV2("tmpe", new byte[] { 1, 2, 3, 4 });
            byte[] encoded = ExtensionIntentCodecV2.Encode(value);
            ExtensionPlayerIntentV2 copy = ExtensionIntentCodecV2.Decode(encoded);
            Assert.Equal("tmpe", copy.AdapterId);
            Assert.Equal(4, copy.Payload.Length);
            Assert.Equal((byte)3, copy.Payload[2]);
        }

        [Case]
        public static void OversizeAndTrailingIntentFailsClosed()
        {
            Assert.Throws<ArgumentException>(delegate
            {
                new ExtensionPlayerIntentV2("x", new byte[ExtensionIntentCodecV2.MaximumIntentPayload + 1]);
            });
            byte[] good = ExtensionIntentCodecV2.Encode(new ExtensionPlayerIntentV2("x", new byte[0]));
            byte[] bad = new byte[good.Length + 1];
            Buffer.BlockCopy(good, 0, bad, 0, good.Length);
            Assert.Throws<InvalidDataException>(delegate { ExtensionIntentCodecV2.Decode(bad); });
        }
    }
}
