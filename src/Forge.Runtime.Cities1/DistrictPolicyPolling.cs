using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal void PollObservedHostDistricts()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostDistricts == null || authority == null || snapshotSave != null)
                return;
            Hash256 before = hostDistricts.StateRoot;
            PublishObservedHostDistrict(before);
        }
    }
}
