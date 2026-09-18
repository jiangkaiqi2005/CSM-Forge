using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Tests
{
    public static class NetDomainV2Tests
    {
        private static NetNodeStateV2 Node(ulong id, string prefab, float x)
        {
            return new NetNodeStateV2(new EntityIdentityV2(id, 1), prefab, x, 0, 0, 1);
        }

        private static NetSegmentStateV2 Segment(ulong id, string prefab, NetNodeStateV2 start, NetNodeStateV2 end)
        {
            return new NetSegmentStateV2(new EntityIdentityV2(id, 1), prefab, start.Entity, end.Entity,
                1, 0, 0, -1, 0, 0, 3, 4);
        }

        [Case]
        public static void NetRootIsCanonicalAcrossInsertionOrder()
        {
            NetNodeStateV2 a = Node(10, "road:basic", 1);
            NetNodeStateV2 b = Node(3, "road:basic", 2);
            NetSegmentStateV2 segment = Segment(20, "road:basic", a, b);

            NetStateIndexV2 first = new NetStateIndexV2();
            first.SeedNode(a); first.SeedNode(b); first.SeedSegment(segment);
            NetStateIndexV2 second = new NetStateIndexV2();
            second.SeedNode(b); second.SeedNode(a); second.SeedSegment(segment);
            Assert.Equal(first.Root, second.Root);
        }

        [Case]
        public static void AtomicMutationCreatesAndDeletesGraphFactsByForgeIdentity()
        {
            NetNodeStateV2 a = Node(1, "road:basic", 0);
            NetNodeStateV2 b = Node(2, "road:basic", 10);
            NetSegmentStateV2 segment = Segment(3, "road:basic", a, b);
            NetStateIndexV2 index = new NetStateIndexV2();
            index.Apply(new NetMutationV2(new[] { a, b }, new EntityIdentityV2[0], new[] { segment },
                new EntityIdentityV2[0], 1200, 0));
            Assert.Equal(2, index.NodeCount);
            Assert.Equal(1, index.SegmentCount);

            index.Apply(new NetMutationV2(new NetNodeStateV2[0], new[] { a.Entity, b.Entity },
                new NetSegmentStateV2[0], new[] { segment.Entity }, 0, 800));
            Assert.Equal(0, index.NodeCount);
            Assert.Equal(0, index.SegmentCount);
        }

        [Case]
        public static void CreateIntentCarriesStableEndpointReferencesNotNativeIds()
        {
            EntityIdentityV2 existingNode = new EntityIdentityV2(99, 4);
            NetControlPointV2 start = new NetControlPointV2(1, 2, 3, 1, 0, 0, 0, existingNode,
                default(EntityIdentityV2), false);
            NetControlPointV2 middle = new NetControlPointV2(4, 2, 3, 1, 0, 0, 0,
                default(EntityIdentityV2), default(EntityIdentityV2), false);
            NetControlPointV2 end = new NetControlPointV2(8, 2, 3, 1, 0, 0, 0,
                default(EntityIdentityV2), default(EntityIdentityV2), false);
            NetIntentV2 intent = NetIntentV2.Create("road:basic", start, middle, end, 32, true, true, false, false, 3);
            NetIntentV2 decoded = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(intent));
            Assert.Equal(NetIntentKindV2.Create, decoded.Kind);
            Assert.Equal(existingNode, decoded.Start.ExistingNode);
            Assert.Equal("road:basic", decoded.PrefabKey);
            Assert.Equal((uint)3, decoded.ZoneGridFlags);
        }

        [Case]
        public static void NetworkMultitoolIntentCodecCarriesStableSemanticTargets()
        {
            EntityIdentityV2 node = new EntityIdentityV2(100, 2);
            EntityIdentityV2 otherNode = new EntityIdentityV2(101, 3);
            EntityIdentityV2 segmentA = new EntityIdentityV2(200, 4);
            EntityIdentityV2 segmentB = new EntityIdentityV2(201, 5);

            NetIntentV2 add = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolAddNode(segmentA, 12.5f, 3f, -9f)));
            Assert.Equal(NetIntentKindV2.MultitoolAddNode, add.Kind);
            Assert.Equal(segmentA, add.Target);
            Assert.Equal(12.5f, add.X);

            NetIntentV2 union = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolUnionNodes(node, otherNode)));
            Assert.Equal(node, union.Target);
            Assert.Equal(otherNode, union.SecondaryTarget);

            NetIntentV2 split = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolSplitNode(node, 1f, 2f, 3f, new[] { segmentB, segmentA })));
            Assert.Equal(2, split.RelatedTargets.Length);
            Assert.Equal(segmentA, split.RelatedTargets[0]);
            Assert.Equal(segmentB, split.RelatedTargets[1]);

            NetIntentV2 intersect = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolIntersectSegments(segmentA, segmentB)));
            Assert.Equal(NetIntentKindV2.MultitoolIntersectSegments, intersect.Kind);
            Assert.Equal(segmentB, intersect.SecondaryTarget);

            NetMultitoolPointV2[] points = new[]
            {
                new NetMultitoolPointV2(0, 1, 2, 1, 0, 0, -1, 0, 0),
                new NetMultitoolPointV2(10, 1, 2, 1, 0, 0, -1, 0, 0)
            };
            NetIntentV2 parallel = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolCreateParallel("road:parallel", true, points)));
            Assert.Equal(NetIntentKindV2.MultitoolCreateParallel, parallel.Kind);
            Assert.Equal(2, parallel.SemanticPoints.Length);
            Assert.Equal("road:parallel", parallel.PrefabKey);
            Assert.True(parallel.Invert);

            NetIntentV2 connection = NetDomainCodecV2.DecodeIntent(NetDomainCodecV2.EncodeIntent(
                NetIntentV2.MultitoolCreateConnection(segmentA, segmentB, true, false,
                    "road:connection", false, true, points)));
            Assert.Equal(NetIntentKindV2.MultitoolCreateConnection, connection.Kind);
            Assert.Equal(segmentA, connection.Target);
            Assert.Equal(segmentB, connection.SecondaryTarget);
            Assert.True(connection.FirstStart);
            Assert.True(!connection.SecondStart);
            Assert.True(connection.FollowTerrain);
        }

        [Case]
        public static void NetworkMultitoolSplitRejectsDuplicateOrOversizedStableSets()
        {
            EntityIdentityV2 node = new EntityIdentityV2(300, 1);
            EntityIdentityV2 segment = new EntityIdentityV2(301, 1);
            Assert.Throws<ArgumentException>(delegate
            {
                NetIntentV2.MultitoolSplitNode(node, 0, 0, 0, new[] { segment, segment });
            });
            EntityIdentityV2[] tooMany = new EntityIdentityV2[8];
            for (int i = 0; i < tooMany.Length; i++) tooMany[i] = new EntityIdentityV2((ulong)(400 + i), 1);
            Assert.Throws<ArgumentException>(delegate
            {
                NetIntentV2.MultitoolSplitNode(node, 0, 0, 0, tooMany);
            });
        }

        [Case]
        public static void NetMutationCodecPreservesGraphAndEconomyMetadata()
        {
            NetNodeStateV2 a = Node(11, "road:a", 0);
            NetNodeStateV2 b = Node(12, "road:a", 20);
            NetSegmentStateV2 segment = Segment(13, "road:a", a, b);
            NetMutationV2 mutation = new NetMutationV2(new[] { a, b }, new EntityIdentityV2[0],
                new[] { segment }, new EntityIdentityV2[0], 2500, 0);
            NetMutationV2 decoded = NetDomainCodecV2.DecodeMutation(NetDomainCodecV2.EncodeMutation(mutation));
            Assert.Equal(2, decoded.UpsertNodes.Length);
            Assert.Equal(1, decoded.UpsertSegments.Length);
            Assert.Equal(a.Entity, decoded.UpsertSegments[0].StartNode);
            Assert.Equal(b.Entity, decoded.UpsertSegments[0].EndNode);
            Assert.Equal(2500, decoded.ConstructionCost);
        }

        [Case]
        public static void StaleEndpointDeleteAndTrailingPayloadFailClosed()
        {
            NetNodeStateV2 a = Node(21, "road:test", 0);
            NetNodeStateV2 b = Node(22, "road:test", 10);
            NetSegmentStateV2 segment = Segment(23, "road:test", a, b);
            NetStateIndexV2 index = new NetStateIndexV2();
            index.SeedNode(a); index.SeedNode(b); index.SeedSegment(segment);
            Assert.Throws<InvalidOperationException>(delegate
            {
                index.Apply(new NetMutationV2(new NetNodeStateV2[0], new[] { a.Entity },
                    new NetSegmentStateV2[0], new EntityIdentityV2[0], 0, 0));
            });

            byte[] encoded = NetDomainCodecV2.EncodeIntent(NetIntentV2.DeleteSegment(segment.Entity, true));
            byte[] tainted = new byte[encoded.Length + 1]; Buffer.BlockCopy(encoded, 0, tainted, 0, encoded.Length);
            Assert.Throws<InvalidDataException>(delegate { NetDomainCodecV2.DecodeIntent(tainted); });
        }
    }
}
