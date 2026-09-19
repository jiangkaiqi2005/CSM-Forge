using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>WP-1.3 regression: DistrictCellStateV2 is a value type with field-wise equality,
    /// so the 262k-cell reconcile allocates no heap per cell and dictionary semantics match the
    /// previous class version.</summary>
    public static class DistrictCellValueTests
    {
        private static DistrictCellStateV2 Cell(uint index, byte alpha, EntityIdentityV2 district)
        {
            return new DistrictCellStateV2(index, district, alpha,
                default(EntityIdentityV2), 0, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
        }

        [Case] public static void CellEqualityIsFieldWise()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            DistrictCellStateV2 a = Cell(12, 255, id);
            DistrictCellStateV2 b = Cell(12, 255, id);
            DistrictCellStateV2 c = Cell(12, 200, id);
            Assert.True(a.Equals(b));
            Assert.True(!a.Equals(c));
            Assert.True(!a.Equals(Cell(13, 255, id)));
            Assert.True(a.Equals(b)); // value copy: repeated comparison stays stable
        }

        [Case] public static void DefaultCellIsEmptyAndPassesSeedValidation()
        {
            DistrictCellStateV2 cell = default(DistrictCellStateV2);
            Assert.True(cell.IsEmpty);
            DistrictStateIndexV2 index = new DistrictStateIndexV2();
            index.SeedCell(cell); // must not throw; empty cells are not stored
            Assert.Equal(0, index.CellCount);
        }

        [Case] public static void DictionarySemanticsMatchTheClassVersion()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            var cells = new SortedDictionary<uint, DistrictCellStateV2>();
            cells.Add(20, Cell(20, 255, id));
            DistrictCellStateV2 stored;
            Assert.True(cells.TryGetValue(20, out stored));
            Assert.True(stored.Equals(Cell(20, 255, id)));
            cells[20] = Cell(20, 200, id);
            Assert.True(!cells[20].Equals(Cell(20, 255, id)));
            Assert.True(cells[20].Alpha1 == 200);
        }

        [Case] public static void CellConstructorRejectsIdentityWithoutAlpha()
        {
            EntityIdentityV2 id = new EntityIdentityV2(7, 1);
            Assert.Throws<ArgumentException>(delegate
            {
                new DistrictCellStateV2(1, id, 0, default(EntityIdentityV2), 0,
                    default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
            });
        }
    }
}
