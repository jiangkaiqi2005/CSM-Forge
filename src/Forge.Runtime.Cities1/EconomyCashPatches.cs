using System;
using System.Reflection;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class EconomyCashPatchPolicy
    {
        public static bool ReplicaLive
        {
            get { return RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.ClientReplicaLive; }
        }

        public static bool SuppressFetch(EconomyManager.Resource resource)
        {
            switch (resource)
            {
                case EconomyManager.Resource.CitizenIncome:
                case EconomyManager.Resource.LoanPayment:
                case EconomyManager.Resource.Maintenance:
                case EconomyManager.Resource.PolicyCost:
                case EconomyManager.Resource.ResourcePrice:
                case EconomyManager.Resource.FeePayment:
                    return true;
                default:
                    return false;
            }
        }

        public static bool SuppressAdd(EconomyManager.Resource resource)
        {
            switch (resource)
            {
                case EconomyManager.Resource.RewardAmount:
                case EconomyManager.Resource.CitizenIncome:
                case EconomyManager.Resource.PublicIncome:
                case EconomyManager.Resource.TourismIncome:
                case EconomyManager.Resource.ResourcePrice:
                    return true;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "FetchResource",
        new Type[] { typeof(EconomyManager.Resource), typeof(int), typeof(ItemClass.Service),
            typeof(ItemClass.SubService), typeof(ItemClass.Level) })]
    internal static class ForgeEconomyFetchSimulationPatch
    {
        public static bool Prefix(EconomyManager.Resource resource, int amount, ref int __result)
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyCashPatchPolicy.ReplicaLive ||
                !EconomyCashPatchPolicy.SuppressFetch(resource)) return true;
            __result = amount;
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "AddResource",
        new Type[] { typeof(EconomyManager.Resource), typeof(int), typeof(ItemClass.Service),
            typeof(ItemClass.SubService), typeof(ItemClass.Level), typeof(DistrictPolicies.Taxation) })]
    internal static class ForgeEconomyAddSimulationPatch
    {
        private static readonly FieldInfo TaxMultiplierField = typeof(EconomyManager).GetField("m_taxMultiplier",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static bool Prefix(EconomyManager.Resource resource, int amount, ItemClass.Service service,
            ItemClass.SubService subService, ItemClass.Level level, DistrictPolicies.Taxation taxationPolicies,
            ref int __result)
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyCashPatchPolicy.ReplicaLive) return true;
            if (EconomyCashPatchPolicy.SuppressAdd(resource))
            {
                __result = amount;
                return false;
            }
            if (resource != EconomyManager.Resource.PrivateIncome) return true;

            EconomyManager manager = EconomyManager.instance;
            if (manager == null || TaxMultiplierField == null || TaxMultiplierField.FieldType != typeof(int))
            {
                RuntimeServices.Lifecycle.Fence("Economy private-income projection surface is unavailable");
                __result = 0;
                return false;
            }
            int taxRate = manager.GetTaxRate(service, subService, level, taxationPolicies);
            taxRate = UniqueFacultyAI.IncreaseByBonus(UniqueFacultyAI.FacultyBonus.Economics, taxRate);
            int multiplier = (int)TaxMultiplierField.GetValue(manager);
            __result = (int)((amount * (long)taxRate * multiplier + 999999L) / 1000000L);
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "AddPrivateIncome",
        new Type[] { typeof(int), typeof(ItemClass.Service), typeof(ItemClass.SubService),
            typeof(ItemClass.Level), typeof(int) })]
    internal static class ForgeEconomyPrivateIncomePatch
    {
        public static bool Prefix(int amount, ref int __result)
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyCashPatchPolicy.ReplicaLive) return true;
            __result = amount;
            return false;
        }
    }
}
