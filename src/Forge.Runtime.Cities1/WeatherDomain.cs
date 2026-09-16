using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class WeatherGameAccess
    {
        public static WeatherStateV2 Capture()
        {
            WeatherManager manager = WeatherManager.instance;
            if (manager == null) throw new InvalidOperationException("WeatherManager is unavailable.");
            return new WeatherStateV2(
                manager.m_currentCloud, manager.m_targetCloud,
                manager.m_currentFog, manager.m_targetFog,
                manager.m_currentNorthernLights, manager.m_targetNorthernLights,
                manager.m_currentRain, manager.m_targetRain,
                manager.m_currentRainbow, manager.m_targetRainbow,
                manager.m_currentTemperature, manager.m_targetTemperature);
        }

        public static WeatherStateV2 ApplyFull(LoadIdentity load, WeatherStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (!RuntimeServices.Lifecycle.IsCurrent(load))
                throw new InvalidOperationException("Weather apply belongs to a stale load.");
            WeatherManager manager = WeatherManager.instance;
            if (manager == null) throw new InvalidOperationException("WeatherManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, WeatherAuthorityDomain.Id))
            {
                manager.m_currentCloud = value.CurrentCloud; manager.m_targetCloud = value.TargetCloud;
                manager.m_currentFog = value.CurrentFog; manager.m_targetFog = value.TargetFog;
                manager.m_currentNorthernLights = value.CurrentNorthernLights; manager.m_targetNorthernLights = value.TargetNorthernLights;
                manager.m_currentRain = value.CurrentRain; manager.m_targetRain = value.TargetRain;
                manager.m_currentRainbow = value.CurrentRainbow; manager.m_targetRainbow = value.TargetRainbow;
                manager.m_currentTemperature = value.CurrentTemperature; manager.m_targetTemperature = value.TargetTemperature;
            }
            WeatherStateV2 actual = Capture();
            if (!actual.TargetRoot.Equals(value.TargetRoot))
                throw new InvalidOperationException("Weather target projection failed.");
            return actual;
        }

        public static void RestoreTargets(LoadIdentity load, WeatherStateV2 value)
        {
            if (value == null || !RuntimeServices.Lifecycle.IsCurrent(load)) return;
            WeatherManager manager = WeatherManager.instance;
            if (manager == null) return;
            using (RuntimeScopeGuard.EnterApply(load, WeatherAuthorityDomain.Id))
            {
                manager.m_targetCloud = value.TargetCloud;
                manager.m_targetFog = value.TargetFog;
                manager.m_targetNorthernLights = value.TargetNorthernLights;
                manager.m_targetRain = value.TargetRain;
                manager.m_targetRainbow = value.TargetRainbow;
                manager.m_targetTemperature = value.TargetTemperature;
            }
        }
    }

    internal sealed class WeatherAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 90;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return WeatherGameAccess.Capture().TargetRoot; } }

        public WeatherAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load; WeatherGameAccess.Capture();
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            return DomainExecutionV2.Rejected();
        }
    }

    internal sealed class WeatherReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private WeatherStateV2 committed;
        public ushort DomainId { get { return WeatherAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return committed.TargetRoot; } }

        public WeatherReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load; committed = WeatherGameAccess.Capture();
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            WeatherStateV2 requested = WeatherDomainCodecV2.Decode(absoluteDelta);
            committed = WeatherGameAccess.ApplyFull(load, requested);
            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Weather replica target root mismatch.");
        }

        public void RestoreCommittedTargets()
        {
            WeatherGameAccess.RestoreTargets(load, committed);
        }
    }
}
