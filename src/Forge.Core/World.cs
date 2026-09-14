using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    public sealed class WorldExecution
    {
        private readonly byte[] delta;
        public bool Applied { get; private set; }
        public Hash256 AfterHash { get; private set; }
        public byte[] Delta { get { return delta == null ? null : (byte[])delta.Clone(); } }

        private WorldExecution(bool applied, byte[] bytes, Hash256 hash)
        {
            Applied = applied;
            delta = bytes;
            AfterHash = hash;
        }

        // Rejected means no state change. Ambiguous/partial application MUST throw instead.
        public static WorldExecution Rejected() { return new WorldExecution(false, null, null); }
        public static WorldExecution Success(byte[] bytes, Hash256 hash)
        {
            if (hash == null) throw new ArgumentNullException("hash");
            return new WorldExecution(true, Check.Copy(bytes, Limits.CommandBytes, false), hash);
        }
    }

    public interface IAuthorityWorld
    {
        Hash256 StateHash { get; }
        WorldExecution Execute(byte[] intent);
        WorldImage Capture();
    }

    public interface IReplicaWorld
    {
        Hash256 StateHash { get; }
        // An adapter must stage changes before publishing, or throw and require full recovery.
        void Apply(byte[] absoluteDelta, Hash256 expectedAfterHash);
        void Install(WorldImage image);
    }

    /// <summary>
    /// Executable reference domain for testing replication. These 16 integer slots are NOT
    /// Cities: Skylines economy fields. The game adapter must implement its own semantics.
    /// </summary>
    public sealed class ParameterWorld : IAuthorityWorld, IReplicaWorld
    {
        private const uint Magic = 0x31575046; // FPW1, little endian
        private SortedDictionary<ushort, int> values = new SortedDictionary<ushort, int>();
        public Hash256 StateHash { get { return Hash256.Compute(Encode(values)); } }

        public int Get(ushort key)
        {
            int value;
            return values.TryGetValue(key, out value) ? value : 0;
        }

        public static byte[] Set(ushort key, int value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(key);
                writer.Write(value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public WorldExecution Execute(byte[] intent)
        {
            ushort key;
            int value;
            if (!DecodeChange(intent, out key, out value)) return WorldExecution.Rejected();
            SortedDictionary<ushort, int> staged = Stage(key, value);
            Hash256 hash = Hash256.Compute(Encode(staged));
            WorldExecution result = WorldExecution.Success(Set(key, value), hash);
            values = staged;
            return result;
        }

        public void Apply(byte[] absoluteDelta, Hash256 expectedAfterHash)
        {
            ushort key;
            int value;
            if (!DecodeChange(absoluteDelta, out key, out value))
                throw new InvalidDataException("Invalid reference-domain change.");
            SortedDictionary<ushort, int> staged = Stage(key, value);
            if (!Hash256.Compute(Encode(staged)).Equals(expectedAfterHash))
                throw new InvalidDataException("Staged state does not match host outcome.");
            values = staged;
        }

        public WorldImage Capture()
        {
            byte[] bytes = Encode(values);
            Hash256 hash = Hash256.Compute(bytes);
            return new WorldImage(bytes, hash, hash);
        }

        public void Install(WorldImage image)
        {
            if (image == null || !image.HasValidContent())
                throw new InvalidDataException("Invalid snapshot content digest.");
            byte[] bytes = image.Bytes;
            if (bytes.Length < 8 || bytes.Length > 8 + 16 * 6)
                throw new InvalidDataException("Invalid reference snapshot length.");
            SortedDictionary<ushort, int> staged = new SortedDictionary<ushort, int>();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unknown snapshot schema.");
                uint count = reader.ReadUInt32();
                if (count > 16 || bytes.Length != 8 + count * 6)
                    throw new InvalidDataException("Invalid snapshot item count.");
                ushort previous = 0;
                for (uint i = 0; i < count; i++)
                {
                    ushort key = reader.ReadUInt16();
                    int value = reader.ReadInt32();
                    if (key <= previous || !Valid(key, value))
                        throw new InvalidDataException("Snapshot keys must be valid, unique and sorted.");
                    staged.Add(key, value);
                    previous = key;
                }
            }
            if (!Hash256.Compute(Encode(staged)).Equals(image.StateHash))
                throw new InvalidDataException("Snapshot state digest does not match.");
            values = staged;
        }

        private SortedDictionary<ushort, int> Stage(ushort key, int value)
        {
            SortedDictionary<ushort, int> staged = new SortedDictionary<ushort, int>(values);
            staged[key] = value;
            return staged;
        }

        private static bool Valid(ushort key, int value)
        {
            return key >= 1 && key <= 16 && value >= -1000000 && value <= 1000000;
        }

        private static bool DecodeChange(byte[] bytes, out ushort key, out int value)
        {
            key = 0;
            value = 0;
            if (bytes == null || bytes.Length != 6) return false;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                key = reader.ReadUInt16();
                value = reader.ReadInt32();
            }
            return Valid(key, value);
        }

        private static byte[] Encode(SortedDictionary<ushort, int> source)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write((uint)source.Count);
                foreach (KeyValuePair<ushort, int> pair in source)
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
