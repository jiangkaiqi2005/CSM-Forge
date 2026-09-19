using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Absolute rows of CS1's editable raw heightmap. FinalHeights is deliberately not serialized:
    /// it is the derived layer rebuilt by TerrainModify together with the legal Net/Building terrain
    /// callbacks. No tool input, cursor state, native entity identity, or undo buffer crosses the wire.
    /// </summary>
    internal sealed class TerrainStateAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31544846u; // FHT1
        private const int RowsPerShard = 8;
        private const int Resolution = TerrainManager.RAW_RESOLUTION + 1;
        private const int Shards = (Resolution + RowsPerShard - 1) / RowsPerShard;
        internal const string Adapter = "builtin.terrain-heights";

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            Check.NotNull(context, "context");
            ValidateShard(shardIndex);
            TerrainManager manager = TerrainManager.instance;
            ushort[] heights = manager == null ? null : manager.RawHeights;
            if (heights == null || heights.Length != Resolution * Resolution)
                throw new InvalidOperationException("CS1 raw terrain heightmap is unavailable or has an unexpected shape.");
            int startRow = shardIndex * RowsPerShard;
            int rowCount = Math.Min(RowsPerShard, Resolution - startRow);
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((ushort)shardIndex);
                writer.Write((ushort)startRow);
                writer.Write((byte)rowCount);
                if (shardIndex == 0) writer.Write(manager.DirtBuffer);
                int start = startRow * Resolution;
                int end = start + rowCount * Resolution;
                for (int i = start; i < end; i++) writer.Write(heights[i]);
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes)
                    throw new InvalidOperationException("Terrain height shard exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            Check.NotNull(context, "context");
            if (state == null || state.Length == 0 || state.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid terrain height shard size.");
            ValidateShard(shardIndex);
            TerrainManager manager = TerrainManager.instance;
            ushort[] heights = manager == null ? null : manager.RawHeights;
            if (heights == null || heights.Length != Resolution * Resolution)
                throw new InvalidOperationException("CS1 raw terrain heightmap is unavailable or has an unexpected shape.");
            int expectedStartRow = shardIndex * RowsPerShard;
            int expectedRows = Math.Min(RowsPerShard, Resolution - expectedStartRow);
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != (ushort)shardIndex ||
                    reader.ReadUInt16() != (ushort)expectedStartRow || reader.ReadByte() != (byte)expectedRows)
                    throw new InvalidDataException("Terrain height shard header mismatch.");
                if (shardIndex == 0) manager.DirtBuffer = reader.ReadInt32();
                int start = expectedStartRow * Resolution;
                int end = start + expectedRows * Resolution;
                for (int i = start; i < end; i++) heights[i] = reader.ReadUInt16();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing terrain height shard bytes.");
            }

            // This is the same source-grounded recomputation entry used by TerrainTool.ApplyBrush.
            // ApplyScope permits collateral Tree/Prop work and the existing Net/Building projections
            // remain authoritative if a callback also changes their persistent result state.
            TerrainModify.UpdateArea(0, expectedStartRow, TerrainManager.RAW_RESOLUTION,
                expectedStartRow + expectedRows - 1, true, false, false);
        }

        private static void ValidateShard(int shardIndex)
        {
            Check.OutOfRange(shardIndex < 0 || shardIndex >= Shards, "shardIndex");
        }
    }
}
