using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public sealed class ExtensionStateEntryV2
    {
        private readonly byte[] payload;
        public string AdapterId { get; private set; }
        public string Key { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public ExtensionStateEntryV2(string adapterId, string key, byte[] bytes)
        {
            ValidateToken(adapterId, "adapterId", 96);
            ValidateToken(key, "key", 128);
            if (bytes == null || bytes.Length > Limits.FramePayloadBytes) throw new ArgumentException("Invalid extension payload.", "bytes");
            AdapterId = adapterId;
            Key = key;
            payload = (byte[])bytes.Clone();
        }

        internal byte[] UnsafePayload { get { return payload; } }

        internal static void ValidateToken(string value, string name, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) throw new ArgumentException("Invalid extension state token.", name);
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == ':' || c == '-' || c == '_'))
                    throw new ArgumentException("Extension state tokens use canonical lowercase ASCII.", name);
        }
    }

    public sealed class ExtensionStateSnapshotV2
    {
        private readonly ExtensionStateEntryV2[] entries;
        public ExtensionStateEntryV2[] Entries { get { return (ExtensionStateEntryV2[])entries.Clone(); } }
        public Hash256 Root { get; private set; }

        public ExtensionStateSnapshotV2(IEnumerable<ExtensionStateEntryV2> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            List<ExtensionStateEntryV2> collected = new List<ExtensionStateEntryV2>();
            foreach (ExtensionStateEntryV2 value in values)
            {
                if (value == null || collected.Count >= ExtensionStateCodecV2.MaximumEntries)
                    throw new ArgumentException("Invalid or excessive extension state entries.", "values");
                collected.Add(new ExtensionStateEntryV2(value.AdapterId, value.Key, value.Payload));
            }
            collected.Sort(delegate(ExtensionStateEntryV2 a, ExtensionStateEntryV2 b)
            {
                int adapter = StringComparer.Ordinal.Compare(a.AdapterId, b.AdapterId);
                return adapter != 0 ? adapter : StringComparer.Ordinal.Compare(a.Key, b.Key);
            });
            for (int i = 1; i < collected.Count; i++)
                if (collected[i - 1].AdapterId == collected[i].AdapterId && collected[i - 1].Key == collected[i].Key)
                    throw new ArgumentException("Duplicate extension state identity.", "values");
            entries = collected.ToArray();
            Root = Hash256.Compute(ExtensionStateCodecV2.EncodeEntries(entries));
        }
    }

    public static class ExtensionStateCodecV2
    {
        public const int MaximumEntries = 256;
        private const uint Magic = 0x32584546; // FEX2

        public static byte[] Encode(ExtensionStateSnapshotV2 snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            return EncodeEntries(snapshot.Entries);
        }

        internal static byte[] EncodeEntries(ExtensionStateEntryV2[] entries)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write((ushort)entries.Length);
                for (int i = 0; i < entries.Length; i++)
                {
                    WriteToken(writer, entries[i].AdapterId);
                    WriteToken(writer, entries[i].Key);
                    byte[] payload = entries[i].UnsafePayload;
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new ArgumentException("Extension state snapshot exceeds one authority frame.", "entries");
                return stream.ToArray();
            }
        }

        public static ExtensionStateSnapshotV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid extension snapshot size.");
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid extension snapshot magic.");
                    int count = reader.ReadUInt16();
                    if (count > MaximumEntries) throw new InvalidDataException("Too many extension snapshot entries.");
                    List<ExtensionStateEntryV2> entries = new List<ExtensionStateEntryV2>(count);
                    for (int i = 0; i < count; i++)
                    {
                        string adapter = ReadToken(reader, 96);
                        string key = ReadToken(reader, 128);
                        int length = reader.ReadInt32();
                        if (length < 0 || length > Limits.FramePayloadBytes || length > stream.Length - stream.Position)
                            throw new InvalidDataException("Invalid extension payload length.");
                        byte[] payload = reader.ReadBytes(length);
                        if (payload.Length != length) throw new EndOfStreamException();
                        entries.Add(new ExtensionStateEntryV2(adapter, key, payload));
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("Trailing extension snapshot bytes.");
                    ExtensionStateSnapshotV2 snapshot = new ExtensionStateSnapshotV2(entries);
                    byte[] canonical = Encode(snapshot);
                    if (canonical.Length != bytes.Length) throw new InvalidDataException("Non-canonical extension snapshot.");
                    for (int i = 0; i < bytes.Length; i++)
                        if (canonical[i] != bytes[i]) throw new InvalidDataException("Non-canonical extension snapshot.");
                    return snapshot;
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated extension snapshot.", error); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid extension snapshot.", error); }
        }

        private static void WriteToken(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadToken(BinaryReader reader, int maximum)
        {
            int length = reader.ReadByte();
            if (length == 0 || length > maximum) throw new InvalidDataException("Invalid extension token length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string value = Encoding.ASCII.GetString(bytes);
            ExtensionStateEntryV2.ValidateToken(value, "value", maximum);
            return value;
        }
    }
}
