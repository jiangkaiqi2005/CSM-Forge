using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Keeps stable-identity mappings partitioned so a shard capture does not scan the entire entity map.</summary>
    internal sealed class StableMappingShardIndex
    {
        private readonly int shardCount;
        private EntityMapEntryV2[][] entries;

        public bool IsValid { get { return entries != null; } }

        public StableMappingShardIndex(int shardCount)
        {
            if (shardCount <= 0) throw new ArgumentOutOfRangeException("shardCount");
            this.shardCount = shardCount;
        }

        public void Invalidate() { entries = null; }

        public void Rebuild(IForgeAdapterContextV1 context)
        {
            if (context == null) throw new ArgumentNullException("context");
            List<EntityMapEntryV2>[] building = new List<EntityMapEntryV2>[shardCount];
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if (!mapping.Identity.IsValid) throw new InvalidOperationException("Stable mapping identity is invalid.");
                int shard = (int)((mapping.Identity.EntityId - 1UL) % (ulong)shardCount);
                if (building[shard] == null) building[shard] = new List<EntityMapEntryV2>();
                building[shard].Add(mapping);
            }
            EntityMapEntryV2[][] rebuilt = new EntityMapEntryV2[shardCount][];
            for (int shard = 0; shard < shardCount; shard++)
                rebuilt[shard] = building[shard] == null ? new EntityMapEntryV2[0] : building[shard].ToArray();
            entries = rebuilt;
        }

        public EntityMapEntryV2[] Get(int shardIndex)
        {
            if (shardIndex < 0 || shardIndex >= shardCount) throw new ArgumentOutOfRangeException("shardIndex");
            if (entries == null) throw new InvalidOperationException("Stable mapping shard index has not been built.");
            return entries[shardIndex];
        }
    }
}
