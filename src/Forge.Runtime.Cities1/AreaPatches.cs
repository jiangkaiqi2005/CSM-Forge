using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(GameAreaManager), "UnlockArea")]
    internal static class ForgeAreaUnlockPatch
    {
        public static bool Prefix(int index, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive)
            {
                __result = false;
                return false;
            }
            int x = index % 5;
            int z = index / 5;
            bool accepted = index >= 0 && index < 25 && RuntimeServices.Multiplayer.TryQueueAreaUnlock(x, z);
            if (!accepted) RuntimeServices.Lifecycle.Fence("Area unlock could not be routed through Host authority");
            __result = accepted;
            return false;
        }
    }
}
