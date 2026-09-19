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

        public static Hash256 Compute(byte[] value)
        {
            Check.NotNull(value, "value");
            using (SHA256 hash = SHA256.Create())
                return new Hash256(hash.ComputeHash(value));
        }

        public static Hash256 Compute(Stream stream)
        {
            Check.NotNull(stream, "stream");
            using (SHA256 hash = SHA256.Create())
                return new Hash256(hash.ComputeHash(stream));
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
