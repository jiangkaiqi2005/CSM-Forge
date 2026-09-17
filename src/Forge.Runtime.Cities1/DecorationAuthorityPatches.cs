using System;
using System.Reflection;
using ColossalFramework.Math;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class DecorationAuthorityRouter
    {
        [ThreadStatic] private static bool bypassTree;
        [ThreadStatic] private static bool bypassProp;

        internal static bool TreeCreate(TreeManager manager, ref uint tree, ref Randomizer randomizer, TreeInfo info,
            Vector3 position, bool single, ref bool result)
        {
            if (bypassTree) return true;
            if (RuntimeScopeGuard.IsApplying) { TreeStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                LoadIdentity load = RuntimeServices.Lifecycle.Current;
                using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
                {
                    bypassTree = true;
                    try { result = manager.CreateTree(out tree, ref randomizer, info, position, single); }
                    finally { bypassTree = false; }
                }
                TreeStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                bool queued = info != null && ForgeExtensionApi.TrySubmitIntent(TreeStateAdapter.Adapter, TreeStateAdapter.CreateIntent(info, position, single));
                tree = 0; result = false; if (!queued) Fail("tree-create-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("tree-create-not-live"); tree = 0; result = false; return false;
        }

        internal static bool TreeMove(TreeManager manager, uint tree, Vector3 position)
        {
            if (bypassTree) return true;
            if (RuntimeScopeGuard.IsApplying) { TreeStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                { bypassTree = true; try { manager.MoveTree(tree, position); } finally { bypassTree = false; } }
                TreeStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                EntityIdentityV2 identity; bool queued = TreeStateAdapter.TryResolveLocal(tree, out identity) &&
                    ForgeExtensionApi.TrySubmitIntent(TreeStateAdapter.Adapter, TreeStateAdapter.MoveIntent(identity, position));
                if (!queued) Fail("tree-move-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("tree-move-not-live"); return false;
        }

        internal static bool TreeRelease(TreeManager manager, uint tree)
        {
            if (bypassTree) return true;
            if (RuntimeScopeGuard.IsApplying) { TreeStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                { bypassTree = true; try { manager.ReleaseTree(tree); } finally { bypassTree = false; } }
                TreeStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                EntityIdentityV2 identity; bool queued = TreeStateAdapter.TryResolveLocal(tree, out identity) &&
                    ForgeExtensionApi.TrySubmitIntent(TreeStateAdapter.Adapter, TreeStateAdapter.DeleteIntent(identity));
                if (!queued) Fail("tree-delete-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("tree-delete-not-live"); return false;
        }

        internal static bool PropCreate(PropManager manager, ref ushort prop, ref Randomizer randomizer, PropInfo info,
            Vector3 position, float angle, bool single, ref bool result)
        {
            if (bypassProp) return true;
            if (RuntimeScopeGuard.IsApplying) { PropStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                { bypassProp = true; try { result = manager.CreateProp(out prop, ref randomizer, info, position, angle, single); } finally { bypassProp = false; } }
                PropStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                bool queued = info != null && ForgeExtensionApi.TrySubmitIntent(PropStateAdapter.Adapter, PropStateAdapter.CreateIntent(info, position, angle, single));
                prop = 0; result = false; if (!queued) Fail("prop-create-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("prop-create-not-live"); prop = 0; result = false; return false;
        }

        internal static bool PropMove(PropManager manager, ushort prop, Vector3 position)
        {
            if (bypassProp) return true;
            if (RuntimeScopeGuard.IsApplying) { PropStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                { bypassProp = true; try { manager.MoveProp(prop, position); } finally { bypassProp = false; } }
                PropStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                EntityIdentityV2 identity; bool queued = PropStateAdapter.TryResolveLocal(prop, out identity) &&
                    ForgeExtensionApi.TrySubmitIntent(PropStateAdapter.Adapter, PropStateAdapter.MoveIntent(identity, position));
                if (!queued) Fail("prop-move-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("prop-move-not-live"); return false;
        }

        internal static bool PropRelease(PropManager manager, ushort prop)
        {
            if (bypassProp) return true;
            if (RuntimeScopeGuard.IsApplying) { PropStateAdapter.MarkDirty(); return true; }
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (Offline(role)) return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                { bypassProp = true; try { manager.ReleaseProp(prop); } finally { bypassProp = false; } }
                PropStateAdapter.MarkDirty(); return false;
            }
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                EntityIdentityV2 identity; bool queued = PropStateAdapter.TryResolveLocal(prop, out identity) &&
                    ForgeExtensionApi.TrySubmitIntent(PropStateAdapter.Adapter, PropStateAdapter.DeleteIntent(identity));
                if (!queued) Fail("prop-delete-intent"); return false;
            }
            AlphaUnsupportedWritePolicy.Report("prop-delete-not-live"); return false;
        }

        private static bool Offline(CitiesRuntimeRole role)
        { return role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Unloading; }
        private static void Fail(string reason)
        {
            RuntimeServices.Events.Record(RuntimeEventCode.Error, RuntimeServices.Lifecycle.Current.Generation, reason);
            RuntimeServices.Lifecycle.Fence(reason);
        }
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class TreeCreateAuthorityPatch
    {
        static MethodBase TargetMethod() { return AccessTools.Method(typeof(TreeManager), "CreateTree", new Type[] { typeof(uint).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(TreeInfo), typeof(Vector3), typeof(bool) }); }
        static bool Prefix(TreeManager __instance, ref uint __0, ref Randomizer __1, TreeInfo __2, Vector3 __3, bool __4, ref bool __result)
        { return DecorationAuthorityRouter.TreeCreate(__instance, ref __0, ref __1, __2, __3, __4, ref __result); }
    }
    [HarmonyPatch(typeof(TreeManager), "MoveTree", new Type[] { typeof(uint), typeof(Vector3) })]
    [HarmonyPriority(Priority.First)]
    internal static class TreeMoveAuthorityPatch { static bool Prefix(TreeManager __instance, uint __0, Vector3 __1) { return DecorationAuthorityRouter.TreeMove(__instance, __0, __1); } }
    [HarmonyPatch(typeof(TreeManager), "ReleaseTree", new Type[] { typeof(uint) })]
    [HarmonyPriority(Priority.First)]
    internal static class TreeReleaseAuthorityPatch { static bool Prefix(TreeManager __instance, uint __0) { return DecorationAuthorityRouter.TreeRelease(__instance, __0); } }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class PropCreateAuthorityPatch
    {
        static MethodBase TargetMethod() { return AccessTools.Method(typeof(PropManager), "CreateProp", new Type[] { typeof(ushort).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(PropInfo), typeof(Vector3), typeof(float), typeof(bool) }); }
        static bool Prefix(PropManager __instance, ref ushort __0, ref Randomizer __1, PropInfo __2, Vector3 __3, float __4, bool __5, ref bool __result)
        { return DecorationAuthorityRouter.PropCreate(__instance, ref __0, ref __1, __2, __3, __4, __5, ref __result); }
    }
    [HarmonyPatch(typeof(PropManager), "MoveProp", new Type[] { typeof(ushort), typeof(Vector3) })]
    [HarmonyPriority(Priority.First)]
    internal static class PropMoveAuthorityPatch { static bool Prefix(PropManager __instance, ushort __0, Vector3 __1) { return DecorationAuthorityRouter.PropMove(__instance, __0, __1); } }
    [HarmonyPatch(typeof(PropManager), "ReleaseProp", new Type[] { typeof(ushort) })]
    [HarmonyPriority(Priority.First)]
    internal static class PropReleaseAuthorityPatch { static bool Prefix(PropManager __instance, ushort __0) { return DecorationAuthorityRouter.PropRelease(__instance, __0); } }
}
