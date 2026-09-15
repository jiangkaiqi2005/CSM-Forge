using System;
using HarmonyLib;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(EconomyManager))]
    [HarmonyPatch("SetTaxRate")]
    [HarmonyPatch(new Type[] { typeof(ItemClass.Service), typeof(ItemClass.SubService), typeof(ItemClass.Level), typeof(int) })]
    internal static class TaxSetRateAuthorityPatch
    {
        public static bool Prefix(ItemClass.Service service, ItemClass.SubService subService,
            ItemClass.Level level, int rate)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;

            if (role == CitiesRuntimeRole.HostLive || role == CitiesRuntimeRole.ClientReplicaLive)
            {
                TaxStateV2 requested;
                try { requested = new TaxStateV2(new TaxKeyV2((int)service, (int)subService, (int)level), rate); }
                catch
                {
                    RuntimeServices.Lifecycle.Fence("Invalid multiplayer tax write");
                    return false;
                }
                if (!TaxGameAccess.Supported(requested.Key) ||
                    !RuntimeServices.Multiplayer.TryQueueTax(new TaxIntentV2(requested)))
                    RuntimeServices.Lifecycle.Fence("Tax write could not be routed through Host authority");
                return false;
            }

            // HostPreparing, ClientLoading/Recovering and fenced worlds cannot author tax state.
            return false;
        }
    }
}
