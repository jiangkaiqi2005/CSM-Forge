using System;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class SimulationClockGameAccess
    {
        private static readonly FieldInfo PauseField = typeof(SimulationManager).GetField("m_simulationPaused",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo SpeedField = typeof(SimulationManager).GetField("m_simulationSpeed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static SimulationClockStateV2 Capture()
        {
            SimulationManager manager = SimulationManager.instance;
            if (manager == null) throw new InvalidOperationException("SimulationManager is unavailable.");
            int speed = manager.SelectedSimulationSpeed;
            if (speed < 0 || speed > 3) throw new InvalidOperationException("CS1 published an unsupported simulation speed.");
            return new SimulationClockStateV2(manager.SimulationPaused, speed);
        }

        public static Hash256 Root { get { return SimulationClockDomainCodecV2.Root(Capture()); } }

        public static SimulationClockStateV2 Apply(LoadIdentity load, SimulationClockStateV2 requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Simulation clock apply belongs to a stale load.");
            if (PauseField == null || SpeedField == null) throw new MissingFieldException("SimulationManager pause/speed fields are unavailable.");
            SimulationManager manager = SimulationManager.instance;
            if (manager == null) throw new InvalidOperationException("SimulationManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, SimulationClockAuthorityDomain.Id))
            {
                SpeedField.SetValue(manager, requested.Speed);
                PauseField.SetValue(manager, requested.Paused);
            }
            SimulationClockStateV2 actual = Capture();
            if (actual.Paused != requested.Paused || actual.Speed != requested.Speed)
                throw new InvalidOperationException("CS1 did not publish the requested simulation clock state.");
            return actual;
        }
    }

    public sealed class SimulationClockAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 24;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return SimulationClockGameAccess.Root; } }

        public SimulationClockAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            SimulationClockIntentV2 intent;
            try { intent = SimulationClockDomainCodecV2.DecodeIntent(payload); }
            catch { return DomainExecutionV2.Rejected(); }
            SimulationClockStateV2 actual = SimulationClockGameAccess.Apply(load, intent.Requested);
            return DomainExecutionV2.Success(SimulationClockDomainCodecV2.Encode(actual), StateRoot);
        }
    }

    public sealed class SimulationClockReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        public ushort DomainId { get { return SimulationClockAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return SimulationClockGameAccess.Root; } }

        public SimulationClockReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica simulation clock apply is invalid in the current role.");
            SimulationClockGameAccess.Apply(load, SimulationClockDomainCodecV2.Decode(absoluteDelta));
            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Simulation clock projection root mismatch.");
        }
    }
}
