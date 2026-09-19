using System;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class DistrictBrushAuthorityState
    {
        public Hash256 BeforeRoot;
    }

    [HarmonyPatch]
    internal static class DistrictToolApplyBrushAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(DistrictTool), "ApplyBrush", new Type[]
            {
                typeof(DistrictTool.Layer), typeof(byte), typeof(float), typeof(Vector3), typeof(Vector3), typeof(bool)
            });
            if (method == null) throw new MissingMethodException("DistrictTool.ApplyBrush authority signature is unavailable.");
            return method;
        }

        public static bool Prefix(DistrictTool.Layer layer, byte districtOrPark, float brushRadius,
            Vector3 startPosition, Vector3 endPosition, bool notOverride, out DistrictBrushAuthorityState __state)
        {
            __state = null;
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;

            if ((layer & DistrictTool.Layer.Districts) == 0)
                return false;

            if (role == CitiesRuntimeRole.HostLive)
            {
                Hash256 before = RuntimeServices.Multiplayer.CaptureHostDistrictRoot();
                if (before == null) return false;
                __state = new DistrictBrushAuthorityState { BeforeRoot = before };
                return true;
            }

            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                DistrictPaintTargetKindV2 kind;
                EntityIdentityV2 target = default(EntityIdentityV2);
                byte pendingNative = 0;
                if (districtOrPark == 0) kind = DistrictPaintTargetKindV2.Erase;
                else if (RuntimeServices.Multiplayer.TryResolveClientDistrict(districtOrPark, out target))
                    kind = DistrictPaintTargetKindV2.Existing;
                else if (RuntimeServices.Multiplayer.IsPendingLocalDistrict(districtOrPark))
                {
                    kind = DistrictPaintTargetKindV2.CreateNew;
                    pendingNative = districtOrPark;
                }
                else
                {
                    RuntimeServices.Lifecycle.Fence("District brush referenced an unknown local district");
                    return false;
                }

                DistrictPaintIntentV2 intent;
                try
                {
                    intent = new DistrictPaintIntentV2(kind, target, brushRadius,
                        startPosition.x, startPosition.y, startPosition.z,
                        endPosition.x, endPosition.y, endPosition.z);
                }
                catch
                {
                    RuntimeServices.Lifecycle.Fence("District brush produced an invalid authority intent");
                    return false;
                }
                if (!RuntimeServices.Multiplayer.TrySubmitDistrictPaint(intent, pendingNative))
                    RuntimeServices.Lifecycle.Fence("District brush could not be routed through Host authority");
                return false;
            }

            return false;
        }

        public static void Postfix(DistrictBrushAuthorityState __state)
        {
            if (__state == null || RuntimeScopeGuard.IsApplying) return;
            RuntimeServices.Multiplayer.PublishObservedHostDistrict(__state.BeforeRoot);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "CreateDistrict")]
    internal static class DistrictCreateSlotBarrierPatch
    {
        public static bool Prefix(ref byte district, ref bool __result, out bool __state)
        {
            __state = false;
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading ||
                role == CitiesRuntimeRole.HostLive) return true;
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                ToolController controller = ToolsModifierControl.toolController;
                DistrictTool tool = controller == null ? null : controller.CurrentTool as DistrictTool;
                if (tool != null && (tool.m_layer & DistrictTool.Layer.Districts) != 0)
                {
                    __state = true;
                    return true;
                }
            }
            district = 0; __result = false; return false;
        }

        public static void Postfix(byte district, bool __result, bool __state)
        {
            if (!__state || !__result || district == 0 || RuntimeScopeGuard.IsApplying) return;
            if (!RuntimeServices.Multiplayer.RegisterPendingLocalDistrict(district))
            {
                try { DistrictGameAccess.Release(RuntimeServices.Lifecycle.Current, district); }
                catch { RuntimeServices.Lifecycle.Fence("Untracked speculative district could not be released"); }
            }
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "ReleaseDistrict")]
    internal static class DistrictReleaseSlotBarrierPatch
    {
        public static bool Prefix(byte district)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading ||
                role == CitiesRuntimeRole.HostLive) return true;
            if (role == CitiesRuntimeRole.ClientReplicaLive && RuntimeServices.Multiplayer.TryReleasePendingLocalDistrict(district))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DistrictWorldInfoPanel), "OnStyleChanged")]
    internal static class DistrictStyleAuthorityPatch
    {
        public static bool Prefix(DistrictWorldInfoPanel __instance, int[] ___m_StyleMap, int value)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            if (__instance == null || ___m_StyleMap == null || value < 0 || value >= ___m_StyleMap.Length)
            {
                RuntimeServices.Lifecycle.Fence("District style UI produced an invalid selection");
                return false;
            }
            FieldInfo field = typeof(DistrictWorldInfoPanel).GetField("m_InstanceID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                RuntimeServices.Lifecycle.Fence("District style panel instance identity is unavailable");
                return false;
            }
            InstanceID instance = (InstanceID)field.GetValue(__instance);
            byte native = instance.District;
            EntityIdentityV2 district;
            bool resolved = role == CitiesRuntimeRole.HostLive
                ? RuntimeServices.Multiplayer.TryResolveHostDistrict(native, out district)
                : RuntimeServices.Multiplayer.TryResolveClientDistrict(native, out district);
            if (!resolved || !RuntimeServices.Multiplayer.TryQueueDistrictStyle(district, (ushort)___m_StyleMap[value]))
                RuntimeServices.Lifecycle.Fence("District style could not be routed through Host authority");
            return false;
        }
    }

    internal static class DistrictPolicyPatchHelper
    {
        public static bool District(DistrictPolicies.Policies policy, byte native, bool enabled)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 district;
            bool resolved = role == CitiesRuntimeRole.HostLive
                ? RuntimeServices.Multiplayer.TryResolveHostDistrict(native, out district)
                : RuntimeServices.Multiplayer.TryResolveClientDistrict(native, out district);
            if (!resolved || !RuntimeServices.Multiplayer.TryQueueDistrictPolicy(
                new DistrictPolicyIntentV2(district, (int)policy, enabled)))
                RuntimeServices.Lifecycle.Fence("District policy could not be routed through Host authority");
            return false;
        }

        public static bool City(DistrictPolicies.Policies policy, bool enabled)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            if (!RuntimeServices.Multiplayer.TryQueueDistrictPolicy(DistrictPolicyIntentV2.City((int)policy, enabled)))
                RuntimeServices.Lifecycle.Fence("City policy could not be routed through Host authority");
            return false;
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "SetDistrictPolicy")]
    internal static class DistrictSetPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy, byte district)
        {
            return DistrictPolicyPatchHelper.District(policy, district, true);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "UnsetDistrictPolicy")]
    internal static class DistrictUnsetPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy, byte district)
        {
            return DistrictPolicyPatchHelper.District(policy, district, false);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "SetCityPolicy")]
    internal static class DistrictSetCityPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy)
        {
            return DistrictPolicyPatchHelper.City(policy, true);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "UnsetCityPolicy")]
    internal static class DistrictUnsetCityPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy)
        {
            return DistrictPolicyPatchHelper.City(policy, false);
        }
    }
}
