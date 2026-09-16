using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private WeatherAuthorityDomain hostWeather;
        private WeatherReplicaDomain clientWeather;
        private Hash256 committedWeatherRoot;

        internal void PollObservedHostWeather()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostWeather == null || authority == null || snapshotSave != null)
                return;
            WeatherStateV2 actual = WeatherGameAccess.Capture();
            Hash256 after = actual.TargetRoot;
            if (committedWeatherRoot == null)
            {
                committedWeatherRoot = after;
                return;
            }
            if (committedWeatherRoot.Equals(after)) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation,
                WeatherAuthorityDomain.Id, committedWeatherRoot, after, WeatherDomainCodecV2.Encode(actual));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-weather-target-change-could-not-commit");
                return;
            }
            committedWeatherRoot = after;
            BroadcastBatch(batch);
        }

        internal void RestoreClientWeatherTargets()
        {
            if (mode == MultiplayerSessionMode.ClientLive && clientWeather != null && replica != null)
                clientWeather.RestoreCommittedTargets();
        }
    }
}
