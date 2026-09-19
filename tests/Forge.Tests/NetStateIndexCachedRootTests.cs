using System;
using CsmForge.Core;

namespace CsmForge.Tests
{
    /// <summary>
    /// WP-1.4c regression: NetStateIndexV2 memoizes its canonical root and every mutating path
    /// invalidates it. Without the invalidation the cached root goes stale the first time a
    /// mutation lands - exactly the failure a cached CurrentRoot would hide.
    /// </summary>
    public static class NetStateIndexCachedRootTests
    {
        private static NetNodeStateV2 Node(ulong entityId)
        {
            return new NetNodeStateV2(new EntityIdentityV2((uint)entityId, 1), "road", entityId, 0, entityId, 0);
        }

        private static NetSegmentStateV2 Segment(ulong entityId, NetNodeStateV2 start, NetNodeStateV2 end)
        {
            return new NetSegmentStateV2(new EntityIdentityV2((uint)entityId, 1), "road",
                start.Entity, end.Entity, 0, 0, 0, 1, 0, 0, 0, 0);
        }

        [Case] public static void RootIsStableAcrossRepeatedReads()
        {
            NetStateIndexV2 index = new NetStateIndexV2();
            NetNodeStateV2 node = Node(1);
            index.SeedNode(node);
            index.SeedNode(Node(2));
            index.SeedSegment(Segment(3, node, Node(2)));
            Hash256 root = index.Root;
            Assert.True(root.Equals(index.Root));
            Assert.True(root.Equals(index.Root));
        }

        [Case] public static void ApplyInvalidatesTheMemoizedRoot()
        {
            NetStateIndexV2 index = new NetStateIndexV2();
            NetNodeStateV2 nodeA = Node(1);
            index.SeedNode(nodeA);
            Hash256 before = index.Root;
            index.Apply(new NetMutationV2(new[] { Node(2) }, new EntityIdentityV2[0],
                new NetSegmentStateV2[0], new EntityIdentityV2[0], 0, 0));
            Assert.True(!index.Root.Equals(before)); // stale cached root would fail here
        }

        [Case] public static void DeleteAlsoInvalidatesTheMemoizedRoot()
        {
            NetStateIndexV2 index = new NetStateIndexV2();
            NetNodeStateV2 nodeA = Node(1);
            NetNodeStateV2 nodeB = Node(2);
            index.SeedNode(nodeA);
            index.SeedNode(nodeB);
            index.SeedSegment(Segment(3, nodeA, nodeB));
            Hash256 before = index.Root;
            index.Apply(new NetMutationV2(new NetNodeStateV2[0], new EntityIdentityV2[0],
                new NetSegmentStateV2[0], new[] { new EntityIdentityV2(3, 1) }, 0, 0));
            Assert.True(!index.Root.Equals(before));
        }

        [Case] public static void RootIsCanonicalAcrossInsertionOrder()
        {
            NetStateIndexV2 first = new NetStateIndexV2();
            NetNodeStateV2 n1 = Node(1);
            NetNodeStateV2 n2 = Node(2);
            first.SeedNode(n1);
            first.SeedNode(n2);
            first.SeedSegment(Segment(3, n1, n2));

            NetStateIndexV2 second = new NetStateIndexV2();
            second.SeedNode(n2);
            second.SeedNode(n1);
            second.SeedSegment(Segment(3, n1, n2));
            Assert.True(first.Root.Equals(second.Root));
        }

        [Case] public static void SeedAndApplyProduceTheSameRoot()
        {
            NetStateIndexV2 seeded = new NetStateIndexV2();
            NetNodeStateV2 nodeA = Node(1);
            NetNodeStateV2 nodeB = Node(2);
            seeded.SeedNode(nodeA);
            seeded.SeedNode(nodeB);
            seeded.SeedSegment(Segment(3, nodeA, nodeB));

            NetStateIndexV2 applied = new NetStateIndexV2();
            applied.SeedNode(nodeA);
            applied.Apply(new NetMutationV2(new[] { nodeB }, new EntityIdentityV2[0],
                new[] { Segment(3, nodeA, nodeB) }, new EntityIdentityV2[0], 0, 0));
            Assert.True(seeded.Root.Equals(applied.Root));
        }
    }
}
