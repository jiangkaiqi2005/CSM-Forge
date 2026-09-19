using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class BudgetGameAccess
    {
        private static bool IsWaterOwned(BudgetKeyV2 key)
        {
            return (ItemClass.Service)key.Service == ItemClass.Service.Water &&
                (ItemClass.SubService)key.SubService == ItemClass.SubService.None;
        }

        public static bool TryRead(BudgetKeyV2 key, out BudgetStateV2 state)
        {
            state = null;
            if (IsWaterOwned(key)) return false;
            ItemClass.Service service = (ItemClass.Service)key.Service;
            ItemClass.SubService subService = (ItemClass.SubService)key.SubService;
            try
            {
                if (!Enum.IsDefined(typeof(ItemClass.Service), service) || !Enum.IsDefined(typeof(ItemClass.SubService), subService) ||
                    ItemClass.GetPublicServiceIndex(service) < 0) return false;
                EconomyManager economy = EconomyManager.instance;
                if (economy == null) return false;
                int budget = economy.GetBudget(service, subService, key.Night);
                if (budget < 0 || budget > 255) return false;
                state = new BudgetStateV2(key, budget); return true;
            }
            catch { return false; }
        }

        public static BudgetStateIndexV2 CaptureIndex()
        {
            BudgetStateIndexV2 index = new BudgetStateIndexV2();
            Array services = Enum.GetValues(typeof(ItemClass.Service));
            Array subServices = Enum.GetValues(typeof(ItemClass.SubService));
            for (int i = 0; i < services.Length; i++)
            {
                ItemClass.Service service = (ItemClass.Service)services.GetValue(i);
                if (ItemClass.GetPublicServiceIndex(service) < 0) continue;
                for (int j = 0; j < subServices.Length; j++)
                {
                    ItemClass.SubService subService = (ItemClass.SubService)subServices.GetValue(j);
                    for (int night = 0; night < 2; night++)
                    {
                        BudgetStateV2 value;
                        if (TryRead(new BudgetKeyV2((int)service, (int)subService, night != 0), out value)) index.Upsert(value);
                    }
                }
            }
            return index;
        }

        public static Hash256 Root { get { return CaptureIndex().Root; } }

        public static BudgetStateV2 Apply(LoadIdentity load, BudgetStateV2 requested)
        {
            if (requested == null || IsWaterOwned(requested.Key)) throw new ArgumentException("Budget target belongs to another domain or is invalid.", "requested");
            BudgetStateV2 current;
            if (!TryRead(requested.Key, out current)) throw new ArgumentException("Unsupported service budget target.", "requested");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Budget apply belongs to a stale load.");
            EconomyManager economy = EconomyManager.instance;
            ItemClass.Service service = (ItemClass.Service)requested.Key.Service;
            ItemClass.SubService subService = (ItemClass.SubService)requested.Key.SubService;
            using (RuntimeScopeGuard.EnterApply(load, BudgetAuthorityDomain.Id))
                economy.SetBudget(service, subService, requested.Budget, requested.Key.Night);
            BudgetStateV2 actual;
            if (!TryRead(requested.Key, out actual) || actual.Budget != requested.Budget)
                throw new InvalidOperationException("CS1 did not publish the requested service budget.");
            return actual;
        }
    }

    public sealed class BudgetAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 4;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return BudgetGameAccess.Root; } }
        public BudgetAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); this.load = load;
        }
        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            BudgetIntentV2 intent;
            try { intent = BudgetDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            BudgetStateV2 current;
            if (!BudgetGameAccess.TryRead(intent.Requested.Key, out current)) return DomainExecutionV2.Rejected();
            BudgetStateV2 actual = BudgetGameAccess.Apply(load, intent.Requested);
            return DomainExecutionV2.Success(BudgetDomainCodecV2.Encode(actual), StateRoot);
        }
    }

    public sealed class BudgetReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        public ushort DomainId { get { return BudgetAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return BudgetGameAccess.Root; } }
        public BudgetReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); this.load = load;
        }
        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica budget apply is invalid in the current role.");
            BudgetStateV2 actual = BudgetGameAccess.Apply(load, BudgetDomainCodecV2.Decode(absoluteDelta));
            if (actual == null || !StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Service budget projection root mismatch.");
        }
    }
}
