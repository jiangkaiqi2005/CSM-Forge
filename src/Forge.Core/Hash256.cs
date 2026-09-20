using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CsmForge.Core
{
    /// <summary>Immutable integrity digest. This is NOT authentication or a MAC.</summary>
    public sealed class Hash256 : IEquatable<Hash256>
    {
        public const int Size = 32;
        private readonly byte[] bytes;

        public Hash256(byte[] value)
        {
            if (value == null || value.Length != Size)
                throw new ArgumentException("A SHA-256 digest must contain 32 bytes.", "value");
            bytes = (byte[])value.Clone();
        }

        /// <summary>
        /// Adopts a digest this class just computed: the array is freshly allocated by the hash
        /// provider and never shared with the caller, so the defensive clone is redundant.
        /// </summary>
        private Hash256(byte[] ownedDigest, bool adopt)
        {
            bytes = ownedDigest;
        }

        /// <summary>
        /// WP-P5: per-thread hash provider. Hash256.Compute sits under every domain root read and
        /// every payload hash, and SHA256.Create() allocated a provider (plus its internal state
        /// buffers) on each call. The Core contract is owner-thread, and each thread gets its own
        /// instance, so reusing it is safe. HashAlgorithm.ComputeHash re-initializes itself, so a
        /// reused provider carries no state between digests.
        /// </summary>
        [ThreadStatic] private static SHA256 provider;

        private static SHA256 Provider()
        {
            SHA256 value = provider;
            if (value == null) { value = SHA256.Create(); provider = value; }
            return value;
        }

        public static Hash256 Compute(byte[] value)
        {
            Check.NotNull(value, "value");
            return new Hash256(Provider().ComputeHash(value), true);
        }

        public static Hash256 Compute(Stream stream)
        {
            Check.NotNull(stream, "stream");
            return new Hash256(Provider().ComputeHash(stream), true);
        }

        public byte[] ToArray() { return (byte[])bytes.Clone(); }

        public bool Equals(Hash256 other)
        {
            if (ReferenceEquals(other, null)) return false;
            int difference = 0;
            for (int i = 0; i < Size; i++) difference |= bytes[i] ^ other.bytes[i];
            return difference == 0;
        }

        public override bool Equals(object obj) { return Equals(obj as Hash256); }
        // Only for in-process containers; never used as a wire/state checksum.
        public override int GetHashCode()
        {
            return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
        }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder(Size * 2);
            foreach (byte value in bytes) result.Append(value.ToString("x2"));
            return result.ToString();
        }
    }
}
