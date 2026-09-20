using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private WeatherAuthorityDomain hostWeather;
        private WeatherReplicaDomain clientWeather;
        private Hash256 committedWeatherRoot;
        private WeatherStateV2 committedWeatherState;

        internal void PollObservedHostWeather()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostWeather == null || authority == null || snapshotSave != null)
                return;
            // WP-P2: six target floats - compare values instead of hashing every tick.
            WeatherStateV2 actual = WeatherGameAccess.Capture();
            if (committedWeatherState == null)
            {
                committedWeatherState = actual;
                committedWeatherRoot = actual.TargetRoot;
                return;
            }
            if (committedWeatherState.EquivalentTargets(actual)) return;
            Hash256 after = actual.TargetRoot;
            committedWeatherState = actual;
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
