namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private void ClearClientDomainReferences()
        {
            ClearClientProjectionAudit();
            ClearDistrictClientPending();
            ClearTransportClientPending();
            clientWater = null;
            clientDemand = null;
            clientTaxes = null;
            clientBudgets = null;
            clientCash = null;
            clientEconomyControl = null;
            clientAreas = null;
            clientBuildings = null;
            clientNet = null;
            clientZones = null;
            clientDistricts = null;
            clientClock = null;
            clientTransport = null;
            clientNames = null;
            clientCityName = null;
            clientWeather = null;
            clientExtensions = null;
        }

        private void ClearAllDomainReferences()
        {
            ClearClientDomainReferences();
            hostWater = null;
            hostDemand = null;
            hostTaxes = null;
            hostBudgets = null;
            hostCash = null;
            hostEconomyControl = null;
            hostAreas = null;
            hostBuildings = null;
            hostNet = null;
            hostZones = null;
            hostDistricts = null;
            hostClock = null;
            hostTransport = null;
            hostNames = null;
            hostCityName = null;
            hostWeather = null;
            hostExtensions = null;
            committedDemandRoot = null;
            committedCashRoot = null;
            committedEconomyControlRoot = null;
            committedWeatherRoot = null;
        }
    }
}
