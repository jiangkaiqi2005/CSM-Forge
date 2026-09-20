using System;
using System.Collections.Generic;
using System.IO;

namespace CsmForge.Core
{
    /// <summary>
    /// WP-1.4: sharded mirror of the district grid. Cells are grouped into shards of 512 slots
    /// (TreePropStateAdapters shard precedent); every shard owns a cached canonical root and the
    /// aggregate root is the hash over all shard roots, so a cell change only re-encodes one
    /// shard instead of the whole city.
    ///
    /// The capacity is NOT a constant: vanilla CS1 allocates 512x512 cells, but grid-expanding
    /// mods replace the array (81 Tiles 2 allocates 900x900 and transpiles DistrictManager's
    /// index arithmetic so the stride becomes 900). The runtime therefore passes the live grid
    /// length; host and client agree because they derive it from the same load.
    ///
    /// Two independent dirty concepts:
    /// - hash-dirty (private): the shard root must be recomputed before the aggregate is read;
    /// - source-dirty (runtime hint): the game grid shard may have drifted from the mirror
    ///   (Harmony hooks set it; a reconcile that re-reads the shard clears it).
    ///
    /// A write that bypasses the source-dirty mark is invisible to the cheap
    /// ReconcileSourceDirty path but is always caught by ReconcileAll — the low-frequency
    /// full verification the WP-1.1 cadence schedules.
    /// </summary>
    public sealed class DistrictShardedCellIndex
    {
        public const int CellsPerShard = 512;
        /// <summary>Vanilla CS1 district grid (512x512). Grid-expanding mods replace it.</summary>
        public const int VanillaCellCount = 512 * 512;
        /// <summary>Bounds guard: 900x900 (81 Tiles 2) is 810,000; a 1024x1024 ceiling is ample.</summary>
        public const int MaximumCellCount = 1024 * 1024;
        private const uint ShardMagic = 0x32444746u; // FGD2, per-shard cell section
        private const uint AggregateMagic = 0x33415344u; // DSA3, shard-root aggregate

        private readonly int totalCells;
        private readonly int shardCount;
        private readonly SortedDictionary<uint, DistrictCellStateV2>[] shards;
        private readonly Hash256[] shardRoots;
        private readonly bool[] sourceDirty;
        private Hash256 aggregate;
        private bool aggregateDirty = true;
        private int cellCount;

        /// <summary>Vanilla capacity; the runtime passes the live grid length instead.</summary>
        public DistrictShardedCellIndex() : this(VanillaCellCount) { }

        public DistrictShardedCellIndex(int totalCells)
        {
            Check.OutOfRange(totalCells < 1 || totalCells > MaximumCellCount, "totalCells");
            this.totalCells = totalCells;
            shardCount = (totalCells + CellsPerShard - 1) / CellsPerShard;
            shards = new SortedDictionary<uint, DistrictCellStateV2>[shardCount];
            shardRoots = new Hash256[shardCount];
            sourceDirty = new bool[shardCount];
            for (int shard = 0; shard < shardCount; shard++)
                shards[shard] = new SortedDictionary<uint, DistrictCellStateV2>();
        }

        public int CellCount { get { return cellCount; } }
        /// <summary>Live grid capacity this index mirrors (vanilla 262,144; 81 Tiles 2 810,000).</summary>
        public int TotalCells { get { return totalCells; } }
        public int ShardCount { get { return shardCount; } }

        public int ShardOf(uint cellIndex)
        {
            Check.OutOfRange(cellIndex >= (uint)totalCells, "cellIndex");
            return (int)(cellIndex / CellsPerShard);
        }

        /// <summary>Number of cells in a shard, clamped to the tail shard's real extent.</summary>
        public int CellsInShard(int shard)
        {
            Check.OutOfRange(shard < 0 || shard >= shardCount, "shard");
            long start = (long)shard * CellsPerShard;
            long remaining = totalCells - start;
            return remaining >= CellsPerShard ? CellsPerShard : (int)remaining;
        }

        public bool IsSourceDirty(int shard) { return sourceDirty[shard]; }

        public bool HasSourceDirtyShards()
        {
            for (int shard = 0; shard < shardCount; shard++)
                if (sourceDirty[shard]) return true;
            return false;
        }

        public void MarkAllSourceDirty()
        {
            for (int shard = 0; shard < shardCount; shard++) sourceDirty[shard] = true;
        }

        public void MarkSourceDirty(int shard)
        {
            Check.OutOfRange(shard < 0 || shard >= shardCount, "shard");
            sourceDirty[shard] = true;
        }

        public void MarkSourceDirtyForCell(uint cellIndex) { sourceDirty[ShardOf(cellIndex)] = true; }

        public bool TryGetCell(uint index, out DistrictCellStateV2 cell) { return shards[ShardOf(index)].TryGetValue(index, out cell); }

        /// <summary>Authoritative upsert/removal (empty cell = removal). Marks the shard hash-dirty.</summary>
        public void ApplyCell(uint index, DistrictCellStateV2 cell)
        {
            int shard = ShardOf(index); // validates the grid range
            Check.Condition(cell.Index != index, "cell", "Cell index mismatch.");
            if (cell.IsEmpty)
            {
                if (shards[shard].Remove(index)) { cellCount--; shardRoots[shard] = null; aggregateDirty = true; }
                return;
            }
            if (!shards[shard].ContainsKey(index)) cellCount++;
            shards[shard][index] = cell;
            shardRoots[shard] = null;
            aggregateDirty = true;
        }

        /// <summary>Lazy aggregate: only hash-dirty shards are re-encoded; clean roots are reused.</summary>
        public Hash256 AggregateRoot
        {
            get
            {
                if (aggregateDirty)
                {
                    Hash256[] roots = new Hash256[shardCount];
                    for (int shard = 0; shard < shardCount; shard++) roots[shard] = ShardRoot(shard);
                    using (MemoryStream stream = new MemoryStream())
                    {
                        BinaryWriter writer = new BinaryWriter(stream);
                        writer.Write(AggregateMagic);
                        for (int shard = 0; shard < shardCount; shard++) writer.Write(roots[shard].ToArray());
                        writer.Flush(); aggregate = Hash256.Compute(stream.ToArray());
                    }
                    aggregateDirty = false;
                }
                return aggregate;
            }
        }

        /// <summary>Recomputes every shard root from stored cells; must equal AggregateRoot.</summary>
        public Hash256 RecomputeFullAggregateRoot()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(AggregateMagic);
                for (int shard = 0; shard < shardCount; shard++) writer.Write(ComputeShardRoot(shard).ToArray());
                writer.Flush(); return Hash256.Compute(stream.ToArray());
            }
        }

        /// <summary>
        /// Reconciles one shard against the game-side source cells; returns the changed cells
        /// (removals carry an empty cell with the same index, matching mutation semantics) and
        /// clears the shard's source-dirty mark regardless of drift.
        /// </summary>
        public DistrictCellStateV2[] ReconcileShard(int shard, IDictionary<uint, DistrictCellStateV2> sourceCells)
        {
            Check.OutOfRange(shard < 0 || shard >= shardCount, "shard");
            if (sourceCells == null) throw new ArgumentNullException("sourceCells");
            List<DistrictCellStateV2> changed = new List<DistrictCellStateV2>();
            List<uint> removals = new List<uint>();
            foreach (KeyValuePair<uint, DistrictCellStateV2> pair in shards[shard])
                if (!sourceCells.ContainsKey(pair.Key)) removals.Add(pair.Key);
            for (int i = 0; i < removals.Count; i++)
            {
                DistrictCellStateV2 empty = new DistrictCellStateV2(removals[i], default(EntityIdentityV2), 0,
                    default(EntityIdentityV2), 0, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
                ApplyCell(removals[i], empty); changed.Add(empty);
            }
            foreach (KeyValuePair<uint, DistrictCellStateV2> pair in sourceCells)
            {
                DistrictCellStateV2 current;
                if (shards[shard].TryGetValue(pair.Key, out current) && current.Equals(pair.Value)) continue;
                ApplyCell(pair.Key, pair.Value); changed.Add(pair.Value);
            }
            sourceDirty[shard] = false;
            return changed.ToArray();
        }

        /// <summary>Cheap path: reconciles only shards flagged source-dirty. Returns the changed cells.</summary>
        public List<DistrictCellStateV2> ReconcileSourceDirty(Func<int, IDictionary<uint, DistrictCellStateV2>> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            List<DistrictCellStateV2> changed = new List<DistrictCellStateV2>();
            for (int shard = 0; shard < shardCount; shard++)
            {
                if (!sourceDirty[shard]) continue;
                changed.AddRange(ReconcileShard(shard, source(shard)));
            }
            return changed;
        }

        /// <summary>Full verification path: reconciles every shard, catching bypassed writes.</summary>
        public List<DistrictCellStateV2> ReconcileAll(Func<int, IDictionary<uint, DistrictCellStateV2>> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            List<DistrictCellStateV2> changed = new List<DistrictCellStateV2>();
            for (int shard = 0; shard < shardCount; shard++) changed.AddRange(ReconcileShard(shard, source(shard)));
            return changed;
        }

        /// <summary>All stored cells in shard-major, key order (entity-deletion reference scans).</summary>
        public IEnumerable<DistrictCellStateV2> Cells
        {
            get
            {
                for (int shard = 0; shard < shardCount; shard++)
                    foreach (KeyValuePair<uint, DistrictCellStateV2> pair in shards[shard]) yield return pair.Value;
            }
        }

        public Hash256 ShardRoot(int shard)
        {
            Check.OutOfRange(shard < 0 || shard >= shardCount, "shard");
            if (shardRoots[shard] == null) shardRoots[shard] = ComputeShardRoot(shard);
            return shardRoots[shard];
        }

        private Hash256 ComputeShardRoot(int shard)
        {
            SortedDictionary<uint, DistrictCellStateV2> shardCells = shards[shard];
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(ShardMagic);
                writer.Write((uint)shard); writer.Write((uint)shardCells.Count);
                foreach (KeyValuePair<uint, DistrictCellStateV2> pair in shardCells)
                {
                    writer.Write(pair.Key);
                    for (int slot = 0; slot < 4; slot++)
                    {
                        writer.Write(pair.Value.AlphaAt(slot));
                        DistrictStateIndexV2.WriteIdentity(writer, pair.Value.IdentityAt(slot));
                    }
                }
                writer.Flush(); return Hash256.Compute(stream.ToArray());
            }
        }
    }
}
