using System;
using System.Collections.Generic;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed class SavedEntityDomainV2
    {
        public ushort DomainId { get; private set; }
        public ulong HighestIssuedId { get; private set; }
        public EntityMapEntryV2[] Entries { get; private set; }

        public SavedEntityDomainV2(ushort domainId, ulong highestIssuedId, EntityMapEntryV2[] entries)
        {
            if (domainId == 0) throw new ArgumentOutOfRangeException("domainId");
            if (entries == null) throw new ArgumentNullException("entries");
            DomainId = domainId;
            HighestIssuedId = highestIssuedId;
            Entries = (EntityMapEntryV2[])entries.Clone();
        }
    }

    public static class EntityMapSaveCodecV2
    {
        private const uint Magic = 0x324D4546u; // FEM2
        private const ushort Schema = 1;
        private const int MaxDomains = 64;
        private const int MaxEntries = 131072;
        private const int MaxBytes = 4 * 1024 * 1024;

        public static byte[] Encode(IEnumerable<SavedEntityDomainV2> domains)
        {
            if (domains == null) throw new ArgumentNullException("domains");
            List<SavedEntityDomainV2> values = new List<SavedEntityDomainV2>(domains);
            if (values.Count > MaxDomains) throw new InvalidDataException("Too many entity-map domains.");
            values.Sort(delegate(SavedEntityDomainV2 a, SavedEntityDomainV2 b) { return a.DomainId.CompareTo(b.DomainId); });
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Schema);
                writer.Write((ushort)values.Count);
                ushort previous = 0;
                foreach (SavedEntityDomainV2 domain in values)
                {
                    if (domain == null || domain.DomainId <= previous) throw new InvalidDataException("Duplicate or unordered entity-map domain.");
                    previous = domain.DomainId;
                    EntityMapEntryV2[] entries = domain.Entries;
                    if (entries.Length > MaxEntries) throw new InvalidDataException("Entity-map domain is too large.");
                    writer.Write(domain.DomainId);
                    writer.Write(domain.HighestIssuedId);
                    writer.Write((uint)entries.Length);
                    ulong last = 0;
                    foreach (EntityMapEntryV2 entry in entries)
                    {
                        if (entry == null || !entry.Identity.IsValid || entry.NativeId == 0 || entry.Identity.EntityId <= last)
                            throw new InvalidDataException("Invalid entity-map entry ordering.");
                        last = entry.Identity.EntityId;
                        writer.Write(entry.Identity.EntityId);
                        writer.Write(entry.Identity.Generation);
                        writer.Write(entry.NativeId);
                    }
                }
                writer.Flush();
                if (stream.Length > MaxBytes) throw new InvalidDataException("Entity-map save payload is too large.");
                return stream.ToArray();
            }
        }

        public static SavedEntityDomainV2[] Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new SavedEntityDomainV2[0];
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Entity-map save payload is too large.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Schema)
                    throw new InvalidDataException("Unsupported entity-map save payload.");
                ushort domainCount = reader.ReadUInt16();
                if (domainCount > MaxDomains) throw new InvalidDataException("Too many entity-map domains.");
                SavedEntityDomainV2[] result = new SavedEntityDomainV2[domainCount];
                ushort previousDomain = 0;
                for (int d = 0; d < domainCount; d++)
                {
                    ushort domainId = reader.ReadUInt16();
                    if (domainId == 0 || domainId <= previousDomain) throw new InvalidDataException("Invalid entity-map domain ordering.");
                    previousDomain = domainId;
                    ulong watermark = reader.ReadUInt64();
                    uint count = reader.ReadUInt32();
                    if (count > MaxEntries) throw new InvalidDataException("Entity-map domain is too large.");
                    EntityMapEntryV2[] entries = new EntityMapEntryV2[count];
                    ulong previousEntity = 0;
                    for (uint i = 0; i < count; i++)
                    {
                        ulong entityId = reader.ReadUInt64();
                        uint generation = reader.ReadUInt32();
                        uint nativeId = reader.ReadUInt32();
                        if (entityId <= previousEntity) throw new InvalidDataException("Invalid entity-map entity ordering.");
                        previousEntity = entityId;
                        entries[i] = new EntityMapEntryV2(new EntityIdentityV2(entityId, generation), nativeId);
                    }
                    if (watermark < previousEntity) throw new InvalidDataException("Entity-map watermark precedes a live identity.");
                    result[d] = new SavedEntityDomainV2(domainId, watermark, entries);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing entity-map bytes.");
                return result;
            }
        }
    }

    public sealed class ForgeEntityMapStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<ushort, SavedEntityDomainV2> pending = new Dictionary<ushort, SavedEntityDomainV2>();
        private readonly Dictionary<ushort, EntityIdMapV2> current = new Dictionary<ushort, EntityIdMapV2>();

        public void LoadPending(byte[] bytes)
        {
            SavedEntityDomainV2[] values = EntityMapSaveCodecV2.Decode(bytes);
            lock (gate)
            {
                pending.Clear();
                current.Clear();
                foreach (SavedEntityDomainV2 value in values) pending.Add(value.DomainId, value);
            }
        }

        public void AttachDomain(ushort domainId, EntityIdMapV2 map)
        {
            if (domainId == 0 || map == null) throw new ArgumentException("Invalid entity-map domain attachment.");
            lock (gate)
            {
                if (current.ContainsKey(domainId)) throw new InvalidOperationException("Entity-map domain is already attached.");
                SavedEntityDomainV2 saved;
                if (pending.TryGetValue(domainId, out saved))
                {
                    map.RestoreSnapshot(saved.Entries, saved.HighestIssuedId);
                    pending.Remove(domainId);
                }
                current.Add(domainId, map);
            }
        }

        public void SuspendCurrent()
        {
            lock (gate)
            {
                foreach (KeyValuePair<ushort, EntityIdMapV2> pair in current)
                    pending[pair.Key] = new SavedEntityDomainV2(pair.Key, pair.Value.HighestIssuedId, pair.Value.SnapshotEntries());
                current.Clear();
            }
        }

        public byte[] EncodeCurrent()
        {
            lock (gate)
            {
                Dictionary<ushort, SavedEntityDomainV2> combined = new Dictionary<ushort, SavedEntityDomainV2>(pending);
                foreach (KeyValuePair<ushort, EntityIdMapV2> pair in current)
                    combined[pair.Key] = new SavedEntityDomainV2(pair.Key, pair.Value.HighestIssuedId, pair.Value.SnapshotEntries());
                return EntityMapSaveCodecV2.Encode(combined.Values);
            }
        }

        public void Clear()
        {
            lock (gate) { pending.Clear(); current.Clear(); }
        }
    }
}
