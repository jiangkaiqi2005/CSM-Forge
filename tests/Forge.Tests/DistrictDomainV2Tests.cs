using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class DistrictDomainV2Tests
    {
        [Case]
        public static void DistrictPaintIntentRoundTripSupportsEraseExistingAndNew()
        {
            EntityIdentityV2 district = new EntityIdentityV2(7, 1);
            DistrictPaintIntentV2 existing = new DistrictPaintIntentV2(DistrictPaintTargetKindV2.Existing,
                district, 25f, 1, 2, 3, 4, 5, 6);
            DistrictPaintIntentV2 copy = DistrictDomainCodecV2.DecodeIntent(DistrictDomainCodecV2.EncodeIntent(existing));
            Assert.Equal(DistrictPaintTargetKindV2.Existing, copy.TargetKind);
            Assert.Equal(district, copy.Target);

            DistrictPaintIntentV2 create = new DistrictPaintIntentV2(DistrictPaintTargetKindV2.CreateNew,
                default(EntityIdentityV2), 10f, 0, 0, 0, 1, 0, 1);
            Assert.Equal(DistrictPaintTargetKindV2.CreateNew,
                DistrictDomainCodecV2.DecodeIntent(DistrictDomainCodecV2.EncodeIntent(create)).TargetKind);
        }

        [Case]
        public static void DistrictMutationUsesCompactIdentityDictionaryAndRoundTrips()
        {
            EntityIdentityV2 a = new EntityIdentityV2(11, 1);
            EntityIdentityV2 b = new EntityIdentityV2(19, 2);
            DistrictMutationV2 mutation = new DistrictMutationV2(
                new DistrictEntityStateV2[] { new DistrictEntityStateV2(a, 123, 4), new DistrictEntityStateV2(b, 456, 5) },
                new EntityIdentityV2[0],
                new DistrictCellStateV2[]
                {
                    new DistrictCellStateV2(12, a, 255, b, 100, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0),
                    new DistrictCellStateV2(13, b, 255, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0)
                });
            byte[] encoded = DistrictDomainCodecV2.EncodeMutation(mutation);
            Assert.True(encoded.Length < 128);
            DistrictMutationV2 copy = DistrictDomainCodecV2.DecodeMutation(encoded);
            Assert.Equal(2, copy.UpsertEntities.Length);
            Assert.Equal(2, copy.Cells.Length);
            Assert.Equal(a, copy.Cells[0].District1);
            Assert.Equal(b, copy.Cells[0].District2);
        }

        [Case]
        public static void DistrictMutationRoundTripsUnassignedBackgroundWeight()
        {
            DistrictCellStateV2 background = new DistrictCellStateV2(27,
                default(EntityIdentityV2), 255, default(EntityIdentityV2), 0,
                default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
            DistrictMutationV2 mutation = new DistrictMutationV2(new DistrictEntityStateV2[0],
                new EntityIdentityV2[0], new DistrictCellStateV2[] { background });

            DistrictMutationV2 copy = DistrictDomainCodecV2.DecodeMutation(DistrictDomainCodecV2.EncodeMutation(mutation));
            Assert.Equal(1, copy.Cells.Length);
            Assert.Equal((byte)255, copy.Cells[0].Alpha1);
            Assert.Equal(default(EntityIdentityV2), copy.Cells[0].District1);

            DistrictStateIndexV2 state = new DistrictStateIndexV2();
            state.Apply(copy);
            Assert.Equal(1, state.CellCount);
        }

        [Case]
        public static void DistrictRootIsCanonicalAndDeletionRequiresNoReferences()
        {
            EntityIdentityV2 a = new EntityIdentityV2(2, 1);
            EntityIdentityV2 b = new EntityIdentityV2(1, 1);
            DistrictEntityStateV2 ea = new DistrictEntityStateV2(a, 1, 0);
            DistrictEntityStateV2 eb = new DistrictEntityStateV2(b, 2, 0);
            DistrictCellStateV2 ca = new DistrictCellStateV2(20, a, 255, default(EntityIdentityV2), 0,
                default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
            DistrictCellStateV2 cb = new DistrictCellStateV2(10, b, 200, default(EntityIdentityV2), 0,
                default(EntityIdentityV2), 0, default(EntityIdentityV2), 0);
            DistrictStateIndexV2 first = new DistrictStateIndexV2(); first.SeedEntity(ea); first.SeedEntity(eb); first.SeedCell(ca); first.SeedCell(cb);
            DistrictStateIndexV2 second = new DistrictStateIndexV2(); second.SeedEntity(eb); second.SeedEntity(ea); second.SeedCell(cb); second.SeedCell(ca);
            Assert.Equal(first.Root, second.Root);
            Assert.Throws<InvalidOperationException>(delegate
            {
                first.Apply(new DistrictMutationV2(new DistrictEntityStateV2[0], new EntityIdentityV2[] { a }, new DistrictCellStateV2[0]));
            });
            first.Apply(new DistrictMutationV2(new DistrictEntityStateV2[0], new EntityIdentityV2[] { a },
                new DistrictCellStateV2[] { new DistrictCellStateV2(20, default(EntityIdentityV2), 0, default(EntityIdentityV2), 0,
                    default(EntityIdentityV2), 0, default(EntityIdentityV2), 0) }));
        }

        [Case]
        public static void DistrictMalformedPayloadFailsClosed()
        {
            EntityIdentityV2 a = new EntityIdentityV2(3, 1);
            DistrictMutationV2 mutation = new DistrictMutationV2(new DistrictEntityStateV2[] { new DistrictEntityStateV2(a, 9, 0) },
                new EntityIdentityV2[0], new DistrictCellStateV2[0]);
            byte[] encoded = DistrictDomainCodecV2.EncodeMutation(mutation);
            byte[] tainted = new byte[encoded.Length + 1]; Buffer.BlockCopy(encoded, 0, tainted, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { DistrictDomainCodecV2.DecodeMutation(tainted); });
        }
    }
}
