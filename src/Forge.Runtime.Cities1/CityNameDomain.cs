using System;
using System.Collections;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class CityNameGameAccess
    {
        public static CityNameStateV2 Capture()
        {
            SimulationManager simulation = SimulationManager.instance;
            if (simulation == null || simulation.m_metaData == null)
                throw new InvalidOperationException("Simulation metadata is unavailable.");
            return new CityNameStateV2(simulation.m_metaData.m_CityName ?? string.Empty);
        }

        public static CityNameStateV2 Apply(LoadIdentity load, CityNameStateV2 requested)
        {
            Check.NotNull(requested, "requested");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("City-name apply belongs to a stale load.");
            CityInfoPanel panel = CityInfoPanel.instance;
            if (panel == null) throw new InvalidOperationException("CityInfoPanel is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, CityNameAuthorityDomain.Id))
            {
                IEnumerator action = panel.SetCityName(requested.Name);
                if (action != null) action.MoveNext();
            }
            CityNameStateV2 actual = Capture();
            if (!StringComparer.Ordinal.Equals(actual.Name, requested.Name))
                throw new InvalidOperationException("CS1 did not publish the requested city name.");
            return actual;
        }
    }

    public sealed class CityNameAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 9;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CityNameGameAccess.Capture().Root; } }

        public CityNameAuthorityDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load; CityNameGameAccess.Capture();
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            CityNameStateV2 requested;
            try { requested = CityNameCodecV2.Decode(payload); } catch { return DomainExecutionV2.Rejected(); }
            CityNameStateV2 before = CityNameGameAccess.Capture();
            if (StringComparer.Ordinal.Equals(before.Name, requested.Name)) return DomainExecutionV2.Rejected();
            CityNameStateV2 actual;
            try { actual = CityNameGameAccess.Apply(load, requested); } catch { return DomainExecutionV2.Rejected(); }
            return DomainExecutionV2.Success(CityNameCodecV2.Encode(actual), actual.Root);
        }
    }

    public sealed class CityNameReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private CityNameStateV2 committed;
        public ushort DomainId { get { return CityNameAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return committed.Root; } }

        public CityNameReplicaDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load; committed = CityNameGameAccess.Capture();
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            CityNameStateV2 requested = CityNameCodecV2.Decode(absoluteDelta);
            committed = CityNameGameAccess.Apply(load, requested);
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("City-name replica root mismatch.");
        }
    }
}
