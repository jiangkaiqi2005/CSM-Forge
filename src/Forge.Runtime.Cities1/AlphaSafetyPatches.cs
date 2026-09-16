using System;
using System.Reflection;
using ColossalFramework.Math;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Minimum-playable Alpha boundary. Unsupported persistent world writes are rejected while a
    /// Forge multiplayer world is active so they cannot silently diverge replicas. Forge-owned
    /// projection scopes are still allowed to call these engine surfaces as side effects of a
    /// supported authoritative operation.
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

    [HarmonyPatch]
    internal static class AlphaTreeCreateBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(TreeManager), "CreateTree", new Type[]
            {
                typeof(uint).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(TreeInfo),
                typeof(Vector3), typeof(bool)
            });
            if (method == null) throw new MissingMethodException("TreeManager.CreateTree signature is unavailable.");
            return method;
        }

        public static bool Prefix(ref uint __0, ref bool __result)
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            __0 = 0; __result = false;
            AlphaUnsupportedWritePolicy.Report("tree-create");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AlphaTreeMoveBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(TreeManager), "MoveTree", new Type[] { typeof(uint), typeof(Vector3) });
            if (method == null) throw new MissingMethodException("TreeManager.MoveTree signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            AlphaUnsupportedWritePolicy.Report("tree-move");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AlphaTreeReleaseBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(TreeManager), "ReleaseTree", new Type[] { typeof(uint) });
            if (method == null) throw new MissingMethodException("TreeManager.ReleaseTree signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            AlphaUnsupportedWritePolicy.Report("tree-release");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AlphaPropCreateBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PropManager), "CreateProp", new Type[]
            {
                typeof(ushort).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(PropInfo),
                typeof(Vector3), typeof(float), typeof(bool)
            });
            if (method == null) throw new MissingMethodException("PropManager.CreateProp signature is unavailable.");
            return method;
        }

        public static bool Prefix(ref ushort __0, ref bool __result)
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            __0 = 0; __result = false;
            AlphaUnsupportedWritePolicy.Report("prop-create");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AlphaPropMoveBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PropManager), "MoveProp", new Type[] { typeof(ushort), typeof(Vector3) });
            if (method == null) throw new MissingMethodException("PropManager.MoveProp signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            AlphaUnsupportedWritePolicy.Report("prop-move");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AlphaPropReleaseBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PropManager), "ReleaseProp", new Type[] { typeof(ushort) });
            if (method == null) throw new MissingMethodException("PropManager.ReleaseProp signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (!AlphaUnsupportedWritePolicy.Block) return true;
            AlphaUnsupportedWritePolicy.Report("prop-release");
            return false;
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
