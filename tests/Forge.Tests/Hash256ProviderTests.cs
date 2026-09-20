using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-P5 regression: Hash256 reuses a per-thread SHA-256 provider instead of allocating one
    /// per call. A reused provider must not carry state between digests, and the adopted-digest
    /// path must still produce correct, stable, distinct hashes.
    /// </summary>
    public static class Hash256ProviderTests
    {
        private static readonly byte[] Known = { 1, 2, 3 };

        [Case] public static void RepeatedHashesAreStableAcrossProviderReuse()
        {
            Hash256 first = Hash256.Compute(Known);
            for (int i = 0; i < 1000; i++)
                Assert.True(first.Equals(Hash256.Compute(Known)));
        }

        [Case] public static void DifferentInputsProduceDifferentHashes()
        {
            Assert.True(!Hash256.Compute(new byte[] { 1, 2, 3 }).Equals(Hash256.Compute(new byte[] { 1, 2, 4 })));
            Assert.True(!Hash256.Compute(new byte[] { 1, 2, 3 }).Equals(Hash256.Compute(new byte[] { 1, 2, 3, 0 })));
        }

        [Case] public static void InterleavedInputsDoNotLeakProviderState()
        {
            Hash256 a = Hash256.Compute(new byte[] { 9 });
            Hash256 b = Hash256.Compute(new byte[] { 8 });
            Hash256 a2 = Hash256.Compute(new byte[] { 9 });
            Hash256 b2 = Hash256.Compute(new byte[] { 8 });
            Assert.True(a.Equals(a2) && b.Equals(b2) && !a.Equals(b));
        }

        [Case] public static void MatchesAnIndependentSha256()
        {
            byte[] payload = new byte[4096];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7 + 3);
            byte[] expected;
            using (System.Security.Cryptography.SHA256 reference = System.Security.Cryptography.SHA256.Create())
                expected = reference.ComputeHash(payload);
            Hash256 actual = Hash256.Compute(payload);
            for (int i = 0; i < Hash256.Size; i++) Assert.True(actual.ToArray()[i] == expected[i]);
        }

        [Case] public static void StreamAndArrayPathsAgree()
        {
            byte[] payload = { 5, 4, 3, 2, 1 };
            Hash256 fromArray = Hash256.Compute(payload);
            Hash256 fromStream;
            using (MemoryStream stream = new MemoryStream(payload, false))
                fromStream = Hash256.Compute(stream);
            Assert.True(fromArray.Equals(fromStream));
        }

        [Case] public static void AdoptedDigestIsNotAliasedToCallerInput()
        {
            // the constructed digest must own its bytes; mutating the source array cannot leak in
            byte[] source = new byte[Hash256.Size];
            Hash256 value = new Hash256(source);
            source[0] = 255;
            Assert.True(value.ToArray()[0] == 0);
        }
    }
}
