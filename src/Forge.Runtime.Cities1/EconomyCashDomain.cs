using System;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class EconomyCashGameAccess
    {
        private static readonly FieldInfo CashField = typeof(EconomyManager).GetField("m_cashAmount",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static FieldInfo RequireField()
        {
            if (CashField == null || CashField.FieldType != typeof(long))
                throw new MissingFieldException("EconomyManager", "m_cashAmount");
            return CashField;
        }

        public static EconomyCashStateV2 Capture()
        {
            EconomyManager manager = EconomyManager.instance;
            if (manager == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            return new EconomyCashStateV2((long)RequireField().GetValue(manager));
        }

        public static EconomyCashStateV2 ApplyReplica(LoadIdentity load, EconomyCashStateV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (!RuntimeServices.Lifecycle.IsCurrent(load))
                throw new InvalidOperationException("Economy cash apply belongs to a stale load.");
            EconomyManager manager = EconomyManager.instance;
            if (manager == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, EconomyCashAuthorityDomain.Id))
                RequireField().SetValue(manager, value.RawCash);
            EconomyCashStateV2 actual = Capture();
            if (actual.RawCash != value.RawCash)
                throw new InvalidOperationException("Economy cash projection failed.");
            return actual;
        }
    }

    public sealed class EconomyCashAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 5;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return EconomyCashGameAccess.Capture().Root; } }

        public EconomyCashAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            // Resolve the target-build private field at domain construction so unsupported
            // game builds fail before a multiplayer world can advertise a baseline.
            EconomyCashGameAccess.Capture();
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            return DomainExecutionV2.Rejected();
        }
    }

    public sealed class EconomyCashReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private EconomyCashStateV2 committed;

        public ushort DomainId { get { return EconomyCashAuthorityDomain.Id; } }
        // Replica root represents the last Host-published fact, not transient local simulation
        // changes that may occur before the next absolute cash batch is installed.
        public Hash256 StateRoot { get { return committed.Root; } }

        public EconomyCashReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
            committed = EconomyCashGameAccess.Capture();
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                 role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica cash apply is invalid in the current role.");
            EconomyCashStateV2 requested = EconomyCashCodecV2.Decode(absoluteDelta);
            EconomyCashStateV2 actual = EconomyCashGameAccess.ApplyReplica(load, requested);
            committed = actual;
            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Economy cash replica root mismatch.");
        }
    }
}
