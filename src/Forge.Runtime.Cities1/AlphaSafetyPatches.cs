using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Shared reporting for a write that is not legal in the current staged multiplayer role.
    /// Tree/Prop and Terrain now have dedicated authority owners; this type no longer owns a
    /// persistent-world fail-closed surface.
    /// </summary>
    internal static class AlphaUnsupportedWritePolicy
    {
        public static void Report(string surface)
        {
            LoadIdentity load = RuntimeServices.Lifecycle.Current;
            RuntimeServices.Events.Record(RuntimeEventCode.Error, load.Generation,
                "alpha-unsupported-write-blocked:" + surface);
        }
    }

    internal static class TerrainAuthorityPatchPolicy
    {
        internal static bool AllowToolMutation(string surface)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer ||
                role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive ||
                role == CitiesRuntimeRole.Unloading) return true;
            AlphaUnsupportedWritePolicy.Report(surface);
            return false;
        }
    }

    [HarmonyPatch(typeof(TerrainTool), "ApplyBrush")]
    internal static class TerrainBrushAuthorityPatch
    {
        public static bool Prefix() { return TerrainAuthorityPatchPolicy.AllowToolMutation("terrain-client-brush"); }
    }

    [HarmonyPatch(typeof(TerrainTool), "ApplyUndo")]
    internal static class TerrainUndoAuthorityPatch
    {
        public static bool Prefix() { return TerrainAuthorityPatchPolicy.AllowToolMutation("terrain-client-undo"); }
    }
}
