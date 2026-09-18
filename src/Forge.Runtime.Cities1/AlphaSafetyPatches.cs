using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Fail-closed boundary for persistent world writes that still do not have a Forge authority
    /// closure. Tree/Prop are owned by DecorationAuthorityPatches; Terrain remains blocked until
    /// its absolute tile/shard authority is implemented.
    /// </summary>
    internal static class AlphaUnsupportedWritePolicy
    {
        public static bool Block
        {
            get
            {
                if (RuntimeScopeGuard.IsApplying) return false;
                CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
                return role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive ||
                    role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientReplicaLive ||
                    role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.WorldFenced;
            }
        }

        public static void Report(string surface)
        {
            LoadIdentity load = RuntimeServices.Lifecycle.Current;
            RuntimeServices.Events.Record(RuntimeEventCode.Error, load.Generation,
                "alpha-unsupported-write-blocked:" + surface);
        }
    }

    [HarmonyPatch(typeof(TerrainTool), "ApplyBrush")]
    internal static class AlphaTerrainBrushBarrierPatch
    {
        public static bool Prefix()
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            AlphaUnsupportedWritePolicy.Report("terrain-brush");
            return false;
        }
    }
}
