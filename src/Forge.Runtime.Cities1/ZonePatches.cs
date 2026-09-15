using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(ZoneBlock), "RefreshZoning")]
    internal static class ZoneRefreshAuthorityPatch
    {
        public static bool Prefix(ushort blockID, ref ulong ___m_zone1, ref ulong ___m_zone2, out bool __state)
        {
            __state = false;
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;

            if (role == CitiesRuntimeRole.ClientReplicaLive || role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.ClientLoading)
            {
                ulong requested1 = ___m_zone1, requested2 = ___m_zone2;
                bool playerTool = false;
                ToolController controller = ToolsModifierControl.toolController;
                if (controller != null) playerTool = controller.CurrentTool is ZoneTool;
                ulong restore1, restore2;
                if (!RuntimeServices.Multiplayer.TryInterceptClientZoneRefresh(blockID, requested1, requested2,
                    playerTool && role == CitiesRuntimeRole.ClientReplicaLive, out restore1, out restore2))
                {
                    RuntimeServices.Lifecycle.Fence("Client zoning write could not be resolved to a Forge block key");
                    ___m_zone1 = 0; ___m_zone2 = 0;
                    return true;
                }
                ___m_zone1 = restore1; ___m_zone2 = restore2;
                return true;
            }

            if (role == CitiesRuntimeRole.HostLive) __state = true;
            return true;
        }

        public static void Postfix(ushort blockID, bool __state)
        {
            if (!__state || RuntimeScopeGuard.IsApplying) return;
            RuntimeServices.Multiplayer.ObserveHostZoneBlock(blockID);
        }
    }
}
