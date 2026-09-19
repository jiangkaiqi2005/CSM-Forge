using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public sealed class NamedEntityMapSnapshotV1
    {
        public string NamespaceId { get; private set; }
        public ulong HighestIssuedId { get; private set; }
        public EntityMapEntryV2[] Entries { get; private set; }

        public NamedEntityMapSnapshotV1(string namespaceId, ulong highestIssuedId, EntityMapEntryV2[] entries)
        {
            ValidateNamespace(namespaceId);
            Check.NotNull(entries, "entries");
            NamespaceId = namespaceId;
            HighestIssuedId = highestIssuedId;
            Entries = (EntityMapEntryV2[])entries.Clone();
        }

        internal static void ValidateNamespace(string value)
        {
            Check.Condition(string.IsNullOrEmpty(value) || value.Length > 80, "namespaceId", "Invalid entity-map namespace.");
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_'))
                    throw new ArgumentException("Entity-map namespaces use canonical lowercase ASCII.", "namespaceId");
        }
    }

    /// <summary>
    /// Stable identity persistence for DLC/mod adapter namespaces. Native manager slots stay
    /// process-local; snapshots persist Forge identity/generation plus the slot only as a
    /// locally validated restore hint, exactly like core EntityIdMapV2 domains.
    /// </summary>
    public static class NamedEntityMapSaveCodecV1
    {
        private const uint Magic = 0x314D5846u; // FXM1
        private const ushort Schema = 1;
        public const int MaximumNamespaces = 128;
        public const int MaximumEntriesPerNamespace = 131072;
        public const int MaximumBytes = 4 * 1024 * 1024;

        public static byte[] Encode(IEnumerable<NamedEntityMapSnapshotV1> snapshots)
        {
            Check.NotNull(snapshots, "snapshots");
            List<NamedEntityMapSnapshotV1> values = new List<NamedEntityMapSnapshotV1>(snapshots);
            if (values.Count > MaximumNamespaces) throw new InvalidDataException("Too many named entity-map namespaces.");
            values.Sort(delegate(NamedEntityMapSnapshotV1 a, NamedEntityMapSnapshotV1 b)
            { return StringComparer.Ordinal.Compare(a.NamespaceId, b.NamespaceId); });

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(Schema);
                writer.Write((ushort)values.Count);
                string previousNamespace = null;
                for (int d = 0; d < values.Count; d++)
                {
                    NamedEntityMapSnapshotV1 value = values[d];
                    if (value == null) throw new InvalidDataException("Null named entity-map namespace.");
                    NamedEntityMapSnapshotV1.ValidateNamespace(value.NamespaceId);
                    if (previousNamespace != null && StringComparer.Ordinal.Compare(previousNamespace, value.NamespaceId) >= 0)
                        throw new InvalidDataException("Duplicate named entity-map namespace.");
                    previousNamespace = value.NamespaceId;
                    WriteAscii(writer, value.NamespaceId);
                    writer.Write(value.HighestIssuedId);
                    EntityMapEntryV2[] entries = value.Entries;
                    if (entries.Length > MaximumEntriesPerNamespace) throw new InvalidDataException("Named entity-map namespace is too large.");
                    writer.Write((uint)entries.Length);
                    ulong previousEntity = 0;
                    HashSet<uint> nativeIds = new HashSet<uint>();
                    for (int i = 0; i < entries.Length; i++)
                    {
                        EntityMapEntryV2 entry = entries[i];
                        if (entry == null || !entry.Identity.IsValid || entry.NativeId == 0 || entry.Identity.EntityId <= previousEntity || !nativeIds.Add(entry.NativeId))
                            throw new InvalidDataException("Invalid named entity-map entry.");
                        previousEntity = entry.Identity.EntityId;
                        writer.Write(entry.Identity.EntityId);
                        writer.Write(entry.Identity.Generation);
                        writer.Write(entry.NativeId);
                    }
                    if (value.HighestIssuedId < previousEntity)
                        throw new InvalidDataException("Named entity-map watermark precedes a live identity.");
                }
                writer.Flush();
                if (stream.Length > MaximumBytes) throw new InvalidDataException("Named entity-map payload is too large.");
                return stream.ToArray();
            }
        }

        public static NamedEntityMapSnapshotV1[] Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new NamedEntityMapSnapshotV1[0];
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Named entity-map payload is too large.");
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Schema)
                        throw new InvalidDataException("Unsupported named entity-map payload.");
                    int count = reader.ReadUInt16();
                    if (count > MaximumNamespaces) throw new InvalidDataException("Too many named entity-map namespaces.");
                    NamedEntityMapSnapshotV1[] result = new NamedEntityMapSnapshotV1[count];
                    string previousNamespace = null;
                    for (int d = 0; d < count; d++)
                    {
                        string namespaceId = ReadAscii(reader, 80);
                        NamedEntityMapSnapshotV1.ValidateNamespace(namespaceId);
                        if (previousNamespace != null && StringComparer.Ordinal.Compare(previousNamespace, namespaceId) >= 0)
                            throw new InvalidDataException("Invalid named entity-map namespace ordering.");
                        previousNamespace = namespaceId;
                        ulong watermark = reader.ReadUInt64();
                        uint entryCount = reader.ReadUInt32();
                        if (entryCount > MaximumEntriesPerNamespace) throw new InvalidDataException("Named entity-map namespace is too large.");
                        EntityMapEntryV2[] entries = new EntityMapEntryV2[entryCount];
                        ulong previousEntity = 0;
                        HashSet<uint> nativeIds = new HashSet<uint>();
                        for (uint i = 0; i < entryCount; i++)
                        {
                            ulong entityId = reader.ReadUInt64();
                            uint generation = reader.ReadUInt32();
                            uint nativeId = reader.ReadUInt32();
                            if (entityId <= previousEntity || nativeId == 0 || !nativeIds.Add(nativeId))
                                throw new InvalidDataException("Invalid named entity-map entry ordering.");
                            previousEntity = entityId;
                            entries[i] = new EntityMapEntryV2(new EntityIdentityV2(entityId, generation), nativeId);
                        }
                        if (watermark < previousEntity) throw new InvalidDataException("Named entity-map watermark precedes a live identity.");
                        result[d] = new NamedEntityMapSnapshotV1(namespaceId, watermark, entries);
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing named entity-map bytes.");
                    return result;
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated named entity-map payload.", error); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid named entity-map payload.", error); }
        }

        private static void WriteAscii(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadAscii(BinaryReader reader, int maximum)
        {
            int length = reader.ReadByte();
            if (length <= 0 || length > maximum) throw new InvalidDataException("Invalid named entity-map namespace length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
