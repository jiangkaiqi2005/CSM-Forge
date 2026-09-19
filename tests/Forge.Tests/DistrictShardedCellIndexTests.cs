using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-1.4 deterministic regressions for the sharded district-grid mirror:
    /// a write that bypasses the source-dirty mark is invisible to the cheap reconcile and is
    /// always caught by the full verification path (the one the WP-1.1 cadence schedules);
    /// shard roots are incremental, canonical across insertion order, and cache-coherent.
    /// </summary>
    public static class DistrictShardedCellIndexTests
    {
        private static DistrictCellStateV2 Cell(uint index, byte alpha, EntityIdentityV2 district)
        {
            return new DistrictCellStateV2(index, district, alpha,
                default(EntityIdentityV2), 0, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
        }


        private static IDictionary<uint, DistrictCellStateV2> Slice(Dictionary<uint, DistrictCellStateV2> all, int shard)
        {
            // Runtime contract: a shard's source contains only cells of its own 512-slot range.
            var slice = new Dictionary<uint, DistrictCellStateV2>();
            foreach (var pair in all)
                if (pair.Key / DistrictShardedCellIndex.CellsPerShard == shard) slice[pair.Key] = pair.Value;
            return slice;
        }

        private static Func<int, IDictionary<uint, DistrictCellStateV2>> SourceOf(Dictionary<uint, DistrictCellStateV2> all)
        {
            return shard => Slice(all, shard);
        }

        [Case] public static void BypassedWriteIsInvisibleToCheapReconcileAndCaughtByFullVerify()
        {
            EntityIdentityV2 a = new EntityIdentityV2(7, 1);
            EntityIdentityV2 b = new EntityIdentityV2(8, 1);
            DistrictShardedCellIndex index = new DistrictShardedCellIndex();
            var source = new Dictionary<uint, DistrictCellStateV2>();
            source[100] = Cell(100, 255, a);
            Assert.Equal(1, index.ReconcileAll(SourceOf(source)).Count); // initial sync
            Hash256 inSync = index.AggregateRoot;

            // Bypassed write: the game grid changed but no source-dirty mark was set.
            source[100] = Cell(100, 255, b);
            Assert.True(!index.IsSourceDirty(0));
            Assert.Equal(0, index.ReconcileSourceDirty(SourceOf(source)).Count); // cheap path sees nothing
            Assert.Equal(inSync, index.AggregateRoot);           // stale by design

            // Full verification (WP-1.1 cadence) catches the bypassed write.
            Assert.Equal(1, index.ReconcileAll(SourceOf(source)).Count);
            Assert.True(!index.AggregateRoot.Equals(inSync));

            // And it converges to the same state as an honest direct application.
            DistrictShardedCellIndex direct = new DistrictShardedCellIndex();
            direct.ApplyCell(100, Cell(100, 255, b));
            Assert.True(index.AggregateRoot.Equals(direct.AggregateRoot));
        }

        [Case] public static void SourceDirtyMarkDrivesTheCheapReconcile()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            DistrictShardedCellIndex index = new DistrictShardedCellIndex();
            var source = new Dictionary<uint, DistrictCellStateV2>();
            source[511] = Cell(511, 255, id); // shard 0
            source[512] = Cell(512, 255, id); // shard 1
            Assert.Equal(2, index.ReconcileAll(SourceOf(source)).Count);
            Hash256 inSync = index.AggregateRoot;

            source[513] = Cell(513, 255, id);
            index.MarkSourceDirtyForCell(513); // Harmony hook marks exactly the touched shard
            Assert.True(index.IsSourceDirty(1));
            Assert.Equal(1, index.ReconcileSourceDirty(SourceOf(source)).Count);
            Assert.True(!index.IsSourceDirty(1));
            Assert.True(!index.AggregateRoot.Equals(inSync));
        }

        [Case] public static void RootIsStableAcrossReadsAndIndependentOfInsertionOrder()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            DistrictShardedCellIndex first = new DistrictShardedCellIndex();
            first.ApplyCell(10, Cell(10, 255, id));
            first.ApplyCell(600, Cell(600, 100, id));
            first.ApplyCell(262143, Cell(262143, 5, id));
            Hash256 firstRoot = first.AggregateRoot;
            Assert.Equal(firstRoot, first.AggregateRoot);
            Assert.True(first.AggregateRoot.Equals(first.RecomputeFullAggregateRoot()));

            DistrictShardedCellIndex second = new DistrictShardedCellIndex();
            second.ApplyCell(262143, Cell(262143, 5, id));
            second.ApplyCell(600, Cell(600, 100, id));
            second.ApplyCell(10, Cell(10, 255, id));
            Assert.True(firstRoot.Equals(second.AggregateRoot));
        }

        [Case] public static void EmptyCellRemovesAndBoundariesHold()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            DistrictShardedCellIndex index = new DistrictShardedCellIndex();
            index.ApplyCell(100, Cell(100, 255, id));
            Assert.Equal(1, index.CellCount);
            index.ApplyCell(100, Cell(100, 0, default(EntityIdentityV2)));
            Assert.Equal(0, index.CellCount);
            DistrictCellStateV2 removed;
            Assert.True(!index.TryGetCell(100, out removed));

            Assert.Equal(0, index.ShardOf(0));
            Assert.Equal(0, index.ShardOf(511));
            Assert.Equal(1, index.ShardOf(512));
            Assert.Equal(511, index.ShardOf(262143));
            Assert.Throws<ArgumentOutOfRangeException>(delegate { index.ShardOf(262144); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { index.ApplyCell(262144, Cell(0, 0, default(EntityIdentityV2))); });
        }

        [Case] public static void HookSequenceIsCaughtByCheapPathAndConfirmedByFullWindow()
        {
            // Runtime contract model (WP-1.4b): the ModifyCell/ReleaseDistrict postfixes mark the
            // touched shard, the cheap reconcile catches the change, and the cadence full window
            // finds zero drift afterwards.
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            DistrictShardedCellIndex index = new DistrictShardedCellIndex();
            var source = new Dictionary<uint, DistrictCellStateV2>();
            source[300] = Cell(300, 255, id);
            source[900] = Cell(900, 255, id);
            Assert.Equal(2, index.ReconcileAll(SourceOf(source)).Count);
            Hash256 inSync = index.AggregateRoot;

            source[300] = Cell(300, 200, id);   // player repaints cell 300
            index.MarkSourceDirtyForCell(300);  // ModifyCell postfix
            Assert.Equal(1, index.ReconcileSourceDirty(SourceOf(source)).Count);
            Hash256 afterCheap = index.AggregateRoot;
            Assert.True(!afterCheap.Equals(inSync));

            // cadence full window: zero changes - the cheap path already converged
            Assert.Equal(0, index.ReconcileAll(SourceOf(source)).Count);
            Assert.True(index.AggregateRoot.Equals(afterCheap));
            Assert.True(index.AggregateRoot.Equals(index.RecomputeFullAggregateRoot()));

            // an untouched shard stays exactly as synced
            DistrictCellStateV2 cell900;
            Assert.True(index.TryGetCell(900, out cell900));
            Assert.True(cell900.Equals(Cell(900, 255, id)));
        }

        [Case] public static void StateIndexCheapPathCarriesCellsAndKeepsRootCoherent()
        {
            DistrictStateIndexV2 index = new DistrictStateIndexV2();
            EntityIdentityV2 id = new EntityIdentityV2(9, 1);
            index.Apply(new DistrictMutationV2(new[] { new DistrictEntityStateV2(id, 5, 1) },
                new EntityIdentityV2[0], new DistrictCellStateV2[0]));

            index.MarkCellSourceDirty(600); // hook marked shard 1
            var sourceCells = new Dictionary<uint, DistrictCellStateV2>();
            sourceCells[600] = Cell(600, 255, id);
            var empty = new Dictionary<uint, DistrictCellStateV2>();
            List<DistrictCellStateV2> changed = index.ReconcileCellsSourceDirty(shard => shard == 1 ? sourceCells : empty);
            Assert.Equal(1, changed.Count);

            Hash256 root = index.Root;
            Assert.True(index.CellAggregateRoot.Equals(index.RecomputedCellAggregateRoot));
            Assert.True(root.Equals(index.Root));
            Assert.Equal(1, index.CellCount);
        }
    }
}
