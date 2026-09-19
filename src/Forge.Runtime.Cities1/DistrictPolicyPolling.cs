using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal void PollObservedHostDistricts()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDistricts == null || authority == null || snapshotSave != null)
                return;
            // WP-1.1: the 262k-cell reconcile no longer runs every tick. Brush/policy patches
            // publish mutations immediately; this cadence poll is the catch-up safety net.
            if (!DistrictVerificationDue()) return;
            Hash256 before = hostDistricts.StateRoot;
            PublishObservedHostDistrict(before);
        }
    }
}
