using System;
using System.Reflection;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch]
    internal static class ForgeCityNameAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            return StableNamePatchHelper.Iterator(typeof(CityInfoPanel), "<SetCityName>c__Iterator1");
        }

        public static bool Prefix(object __instance, string ___name, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;

            FieldInfo pc = __instance == null ? null : __instance.GetType().GetField("$PC",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pc == null)
            {
                RuntimeServices.Lifecycle.Fence("City-name coroutine program counter is unavailable");
                __result = false;
                return false;
            }
            if (Convert.ToInt32(pc.GetValue(__instance)) != 0) return true;

            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive)
            {
                __result = false;
                return false;
            }

            string requested = ___name ?? string.Empty;
            try
            {
                if (StringComparer.Ordinal.Equals(CityNameGameAccess.Capture().Name, requested))
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                RuntimeServices.Lifecycle.Fence("City name could not be inspected");
                __result = false;
                return false;
            }

            if (!RuntimeServices.Multiplayer.TryQueueCityName(requested))
                RuntimeServices.Lifecycle.Fence("City name could not be routed through Host authority");
            __result = false;
            return false;
        }
    }
}
