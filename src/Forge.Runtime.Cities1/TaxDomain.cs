using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class TaxTargetV2
    {
        public readonly ItemClass.Service Service;
        public readonly ItemClass.SubService SubService;
        public TaxTargetV2(ItemClass.Service service, ItemClass.SubService subService)
        {
            Service = service; SubService = subService;
        }
    }

    internal static class TaxGameAccess
    {
        private static readonly ItemClass.Level[] Levels = new ItemClass.Level[]
        {
            ItemClass.Level.Level1, ItemClass.Level.Level2, ItemClass.Level.Level3,
            ItemClass.Level.Level4, ItemClass.Level.Level5
        };

        private static readonly TaxTargetV2[] Targets = new TaxTargetV2[]
        {
            new TaxTargetV2(ItemClass.Service.Residential, ItemClass.SubService.ResidentialLow),
            new TaxTargetV2(ItemClass.Service.Residential, ItemClass.SubService.ResidentialHigh),
            new TaxTargetV2(ItemClass.Service.Residential, ItemClass.SubService.ResidentialLowEco),
            new TaxTargetV2(ItemClass.Service.Residential, ItemClass.SubService.ResidentialHighEco),
            new TaxTargetV2(ItemClass.Service.Residential, ItemClass.SubService.ResidentialWallToWall),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialLow),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialHigh),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialLeisure),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialTourist),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialEco),
            new TaxTargetV2(ItemClass.Service.Commercial, ItemClass.SubService.CommercialWallToWall),
            new TaxTargetV2(ItemClass.Service.Industrial, ItemClass.SubService.IndustrialGeneric),
            new TaxTargetV2(ItemClass.Service.Industrial, ItemClass.SubService.IndustrialForestry),
            new TaxTargetV2(ItemClass.Service.Industrial, ItemClass.SubService.IndustrialFarming),
            new TaxTargetV2(ItemClass.Service.Industrial, ItemClass.SubService.IndustrialOil),
            new TaxTargetV2(ItemClass.Service.Industrial, ItemClass.SubService.IndustrialOre),
            new TaxTargetV2(ItemClass.Service.Office, ItemClass.SubService.OfficeGeneric),
            new TaxTargetV2(ItemClass.Service.Office, ItemClass.SubService.OfficeHightech),
            new TaxTargetV2(ItemClass.Service.Office, ItemClass.SubService.OfficeWallToWall),
            new TaxTargetV2(ItemClass.Service.Office, ItemClass.SubService.OfficeFinancial)
        };

        public static bool Supported(TaxKeyV2 key)
        {
            ItemClass.Level level = (ItemClass.Level)key.Level;
            bool levelSupported = false;
            for (int i = 0; i < Levels.Length; i++) if (Levels[i] == level) { levelSupported = true; break; }
            if (!levelSupported) return false;
            ItemClass.Service service = (ItemClass.Service)key.Service;
            ItemClass.SubService subService = (ItemClass.SubService)key.SubService;
            for (int i = 0; i < Targets.Length; i++)
                if (Targets[i].Service == service && Targets[i].SubService == subService) return true;
            return false;
        }

        public static TaxStateIndexV2 CaptureIndex()
        {
            EconomyManager economy = EconomyManager.instance;
            if (economy == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            TaxStateIndexV2 index = new TaxStateIndexV2();
            for (int i = 0; i < Targets.Length; i++)
                for (int j = 0; j < Levels.Length; j++)
                {
                    TaxKeyV2 key = new TaxKeyV2((int)Targets[i].Service, (int)Targets[i].SubService, (int)Levels[j]);
                    int rate = economy.GetTaxRate(Targets[i].Service, Targets[i].SubService, Levels[j]);
                    index.Upsert(new TaxStateV2(key, rate));
                }
            return index;
        }

        public static Hash256 Root { get { return CaptureIndex().Root; } }

        public static TaxStateV2 Apply(LoadIdentity load, TaxStateV2 requested)
        {
            Check.Condition(requested == null || !Supported(requested.Key), "requested", "Unsupported tax target.");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Tax apply belongs to a stale load.");
            EconomyManager economy = EconomyManager.instance;
            if (economy == null) throw new InvalidOperationException("EconomyManager is unavailable.");
            ItemClass.Service service = (ItemClass.Service)requested.Key.Service;
            ItemClass.SubService subService = (ItemClass.SubService)requested.Key.SubService;
            ItemClass.Level level = (ItemClass.Level)requested.Key.Level;
            using (RuntimeScopeGuard.EnterApply(load, TaxAuthorityDomain.Id))
                economy.SetTaxRate(service, subService, level, requested.Rate);
            int actual = economy.GetTaxRate(service, subService, level);
            if (actual < 0 || actual > 29) throw new InvalidOperationException("CS1 published an unsupported tax rate.");
            return new TaxStateV2(requested.Key, actual);
        }
    }

    public sealed class TaxAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 3;
        private readonly LoadIdentity load;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return TaxGameAccess.Root; } }
        public TaxAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); this.load = load;
        }
        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            TaxIntentV2 intent;
            try { intent = TaxDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            if (!TaxGameAccess.Supported(intent.Requested.Key)) return DomainExecutionV2.Rejected();
            TaxStateV2 actual = TaxGameAccess.Apply(load, intent.Requested);
            return DomainExecutionV2.Success(TaxDomainCodecV2.Encode(actual), StateRoot);
        }
    }

    public sealed class TaxReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        public ushort DomainId { get { return TaxAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return TaxGameAccess.Root; } }
        public TaxReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load"); this.load = load;
        }
        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (!RuntimeServices.Lifecycle.IsCurrent(load) ||
                (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering && role != CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Replica tax apply is invalid in the current role.");
            TaxStateV2 actual = TaxGameAccess.Apply(load, TaxDomainCodecV2.Decode(absoluteDelta));
            if (actual.Rate < 0 || !StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Tax projection root mismatch.");
        }
    }
}
