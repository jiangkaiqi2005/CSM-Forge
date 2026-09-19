using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Tests
{
    /// <summary>
    /// S3 regression: model constructors must enforce the same UTF-8 byte bounds as the wire
    /// codecs, so a display name (or rejection reason) that passes the character check cannot
    /// blow up at encode time — the failure that made 22+ character CJK names unjoinable.
    /// </summary>
    public static class ProtocolTextBoundsTests
    {
        private static readonly Hash256 Build = Hash256.Compute(new byte[] { 1 });
        private static readonly Hash256 Schema = Hash256.Compute(new byte[] { 2 });

        private static HelloV2 Hello(string displayName)
        {
            return new HelloV2(Guid.NewGuid(), displayName, Build, Schema, 1, BootstrapAuthMode.DevelopmentRoomKeyOnly);
        }

        [Case] public static void CjkDisplayNameInsideByteBoundRoundTrips()
        {
            string name = new string('汉', 21); // 63 UTF-8 bytes, 21 characters
            HelloV2 decoded = BootstrapMessagesV2.DecodeHello(BootstrapMessagesV2.EncodeHello(Hello(name)));
            Assert.Equal(name, decoded.DisplayName);
        }

        [Case] public static void CjkDisplayNameBeyondByteBoundFailsAtConstruction()
        {
            string name = new string('汉', 22); // 66 UTF-8 bytes: inside the 32-char rule, over the 64-byte wire bound
            Assert.Throws<ArgumentException>(delegate { Hello(name); });
        }

        [Case] public static void AsciiDisplayNameCharacterBoundStillHolds()
        {
            Assert.Throws<ArgumentException>(delegate { Hello(new string('a', 33)); });
        }

        [Case] public static void CompatibilityReasonByteAndCharacterBoundsHold()
        {
            string reason = new string('原', 85); // 255 UTF-8 bytes
            CompatibilityResultV2 result = new CompatibilityResultV2(false, reason);
            Assert.Equal(reason, result.Reason);
            Assert.Throws<ArgumentException>(delegate { new CompatibilityResultV2(false, new string('原', 86)); });  // 258 bytes
            Assert.Throws<ArgumentException>(delegate { new CompatibilityResultV2(false, new string('a', 257)); }); // 257 chars
        }

        [Case] public static void CompatibilityResultRoundTrips()
        {
            CompatibilityResultV2 decoded = BootstrapMessagesV2.DecodeCompatibilityResult(
                BootstrapMessagesV2.EncodeCompatibilityResult(new CompatibilityResultV2(false, "fingerprint-mismatch:mod:x")));
            Assert.True(!decoded.Accepted);
            Assert.Equal("fingerprint-mismatch:mod:x", decoded.Reason);
        }
    }
}
