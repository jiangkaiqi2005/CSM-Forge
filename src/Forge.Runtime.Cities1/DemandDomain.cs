using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed class DemandStateV2
    {
        public int Residential { get; private set; }
        public int Commercial { get; private set; }
        public int Workplace { get; private set; }

        public DemandStateV2(int residential, int commercial, int workplace)
        {
            Validate(residential); Validate(commercial); Validate(workplace);
            Residential = residential; Commercial = commercial; Workplace = workplace;
        }

        private static void Validate(int value)
        {
            Check.OutOfRange(value < 0 || value > 100, "demand");
        }

        /// <summary>WP-P2: value equality lets the poll skip computing a root when nothing changed.</summary>
        public bool Equivalent(DemandStateV2 other)
        {
            return other != null && Residential == other.Residential &&
                Commercial == other.Commercial && Workplace == other.Workplace;
        }

        public Hash256 Root
        {
            get { return Hash256.Compute(new byte[] { (byte)Residential, (byte)Commercial, (byte)Workplace }); }
        }
    }

    public static class DemandCodecV2
    {
        public static byte[] Encode(DemandStateV2 value)
        {
            Check.NotNull(value, "value");
            return new byte[] { (byte)value.Residential, (byte)value.Commercial, (byte)value.Workplace };
        }

        public static DemandStateV2 Decode(byte[] bytes)
        {
            Check.Condition(bytes == null || bytes.Length != 3, "bytes", "Invalid demand payload.");
            return new DemandStateV2(bytes[0], bytes[1], bytes[2]);
        }
    }

    internal static class DemandGameAccess
    {
        public static DemandStateV2 Capture()
        {
            ZoneManager zone = ZoneManager.instance;
            if (zone == null) throw new InvalidOperationException("ZoneManager is unavailable.");
            return new DemandStateV2(zone.m_residentialDemand, zone.m_commercialDemand, zone.m_workplaceDemand);
        }

        public static DemandStateV2 ApplyReplica(LoadIdentity load, DemandStateV2 value)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Demand apply belongs to a stale load.");
            ZoneManager zone = ZoneManager.instance;
            if (zone == null) throw new InvalidOperationException("ZoneManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, DemandAuthorityDomain.Id))
            {
                zone.m_residentialDemand = value.Residential;
                zone.m_commercialDemand = value.Commercial;
                zone.m_workplaceDemand = value.Workplace;
                // Replica clients must not independently trigger zone growth from their own simulation demand.
                zone.m_actualResidentialDemand = 0;
                zone.m_actualCommercialDemand = 0;
                zone.m_actualWorkplaceDemand = 0;
            }
            DemandStateV2 actual = Capture();
            if (!actual.Root.Equals(value.Root)) throw new InvalidOperationException("Demand projection failed.");
            return actual;
        }
    }

    public sealed class DemandAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 2;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return DemandGameAccess.Capture().Root; } }
        public DemandAuthorityDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
        }
        public DomainExecutionV2 ExecutePlayer(byte[] payload) { return DomainExecutionV2.Rejected(); }
    }

    public sealed class DemandReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        public ushort DomainId { get { return DemandAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return DemandGameAccess.Capture().Root; } }
        public DemandReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); this.load = load;
        }
        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            DemandStateV2 actual = DemandGameAccess.ApplyReplica(load, DemandCodecV2.Decode(absoluteDelta));
            if (!actual.Root.Equals(expectedAfterRoot)) throw new InvalidOperationException("Demand replica root mismatch.");
        }
    }
}
