using System;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    public sealed class WaterBudgetIntent
    {
        public bool Night { get; private set; }
        public int Budget { get; private set; }
        public WaterBudgetIntent(bool night, int budget)
        {
            if (budget < 0 || budget > 255) throw new ArgumentOutOfRangeException("budget");
            Night = night; Budget = budget;
        }
    }

    public sealed class WaterBudgetState
    {
        public int Day { get; private set; }
        public int Night { get; private set; }
        public WaterBudgetState(int day, int night)
        {
            if (day < 0 || day > 255 || night < 0 || night > 255) throw new ArgumentOutOfRangeException("day");
            Day = day; Night = night;
        }
        public Hash256 Root { get { return Hash256.Compute(new byte[] { (byte)Day, (byte)Night }); } }
    }

    public static class WaterBudgetCodec
    {
        public static byte[] EncodeIntent(WaterBudgetIntent value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return new byte[] { value.Night ? (byte)1 : (byte)0, (byte)value.Budget };
        }
        public static WaterBudgetIntent DecodeIntent(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 2 || bytes[0] > 1) throw new ArgumentException("Invalid water budget intent.", "bytes");
            return new WaterBudgetIntent(bytes[0] == 1, bytes[1]);
        }
        public static byte[] EncodeState(WaterBudgetState value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return new byte[] { (byte)value.Day, (byte)value.Night };
        }
        public static WaterBudgetState DecodeState(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 2) throw new ArgumentException("Invalid water budget state.", "bytes");
            return new WaterBudgetState(bytes[0], bytes[1]);
        }
    }

    internal static class WaterBudgetGameAccess
    {
        public static WaterBudgetState Capture()
        {
            EconomyManager economy = EconomyManager.instance;
            if (economy == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            return new WaterBudgetState(
                economy.GetBudget(ItemClass.Service.Water, ItemClass.SubService.None, false),
                economy.GetBudget(ItemClass.Service.Water, ItemClass.SubService.None, true));
        }

        public static WaterBudgetState Apply(LoadIdentity load, WaterBudgetState value, ushort domainId)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Water budget apply belongs to a stale load generation.");
            EconomyManager economy = EconomyManager.instance;
            if (economy == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, domainId))
            {
                economy.SetBudget(ItemClass.Service.Water, ItemClass.SubService.None, value.Day, false);
                economy.SetBudget(ItemClass.Service.Water, ItemClass.SubService.None, value.Night, true);
            }
            WaterBudgetState actual = Capture();
            if (actual.Day != value.Day || actual.Night != value.Night)
                throw new InvalidOperationException("CS1 did not publish the requested absolute water budget state.");
            return actual;
        }
    }

    public sealed class WaterBudgetAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 1;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return WaterBudgetGameAccess.Capture().Root; } }
        public WaterBudgetAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
        }
        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            WaterBudgetIntent intent;
            try { intent = WaterBudgetCodec.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            WaterBudgetState before = WaterBudgetGameAccess.Capture();
            WaterBudgetState requested = intent.Night ? new WaterBudgetState(before.Day, intent.Budget) : new WaterBudgetState(intent.Budget, before.Night);
            WaterBudgetState after = WaterBudgetGameAccess.Apply(load, requested, DomainId);
            return DomainExecutionV2.Success(WaterBudgetCodec.EncodeState(after), after.Root);
        }
    }

    public sealed class WaterBudgetReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        public ushort DomainId { get { return WaterBudgetAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return WaterBudgetGameAccess.Capture().Root; } }
        public WaterBudgetReplicaDomain(LoadIdentity load)
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
                throw new InvalidOperationException("Replica water budget apply is not valid in the current runtime role.");
            WaterBudgetState actual = WaterBudgetGameAccess.Apply(load, WaterBudgetCodec.DecodeState(absoluteDelta), DomainId);
            if (!actual.Root.Equals(expectedAfterRoot)) throw new InvalidOperationException("Water budget projection root mismatch.");
        }
    }

    [HarmonyPatch(typeof(EconomyManager))]
    [HarmonyPatch("SetBudget")]
    [HarmonyPatch(new Type[] { typeof(ItemClass.Service), typeof(ItemClass.SubService), typeof(int), typeof(bool) })]
    public static class WaterBudgetSetBudgetPatch
    {
        public static bool Prefix(ItemClass.Service service, ItemClass.SubService subService, int budget, bool night)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            bool multiplayer = role == CitiesRuntimeRole.HostLive || role == CitiesRuntimeRole.HostPreparing ||
                role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientReplicaLive ||
                role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.WorldFenced;
            if (!multiplayer) return true;

            if (role == CitiesRuntimeRole.HostLive || role == CitiesRuntimeRole.ClientReplicaLive)
            {
                if (service == ItemClass.Service.Water && subService == ItemClass.SubService.None)
                    RuntimeServices.Multiplayer.TryQueueWaterBudget(night, budget);
            }
            // All direct multiplayer budget writes are suppressed. Only the supported Water
            // slice is converted to an Intent; other services remain explicitly unsupported.
            return false;
        }
    }
}
