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
            // Stride comes from the live area grid: 5 vanilla, 9 with 81 Tiles 2. The hardcoded
            // 5 read the wrong cell and rejected indices >= 25, which fenced the whole session.
            int resolution;
            try { resolution = AreaGameAccess.Resolution(); }
            catch { RuntimeServices.Lifecycle.Fence("Area grid shape is unavailable"); __result = false; return false; }
            int x = index % resolution;
            int z = index / resolution;
            bool inRange = index >= 0 && x < resolution && z < resolution;
            bool accepted = inRange && RuntimeServices.Multiplayer.TryQueueAreaUnlock(x, z);
            if (!accepted) RuntimeServices.Lifecycle.Fence("Area unlock could not be routed through Host authority");
            __result = accepted;
            return false;
        }
    }
}
