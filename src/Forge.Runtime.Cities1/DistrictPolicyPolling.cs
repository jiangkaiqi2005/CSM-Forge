using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        /// <summary>WP-1.4b: Harmony hooks mark the touched grid shard for the cheap reconcile.</summary>
        public void MarkDistrictCellSourceDirty(uint cellIndex)
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDistricts == null) return;
            hostDistricts.MarkCellSourceDirty(cellIndex);
        }

        /// <summary>WP-1.4b: ReleaseDistrict can clear cells across the whole map.</summary>
        public void MarkAllDistrictShardsSourceDirty()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDistricts == null) return;
            hostDistricts.MarkAllCellsSourceDirty();
        }

        internal void PollObservedHostDistricts()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDistricts == null || authority == null || snapshotSave != null)
                return;
            // WP-1.4b cheap path first: only the shards the ModifyCell/ReleaseDistrict hooks
            // marked are re-read from the game; every other shard keeps its cached root.
            if (hostDistricts.HasSourceDirtyShards())
            { PublishObservedHostDistrictSourceDirtyShards(); return; }
            // WP-1.1 cadence: the full verification window reconciles all 512 shards and catches
            // any write that bypassed the hooks.
            if (!DistrictVerificationDue()) return;
            Hash256 before = hostDistricts.StateRoot;
            PublishObservedHostDistrict(before);
        }
    }
}
