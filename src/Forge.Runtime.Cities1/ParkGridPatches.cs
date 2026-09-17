using System;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class ParkToolApplyBrushAuthorityOverridePatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(DistrictTool), "ApplyBrush", new Type[]
            {
                typeof(DistrictTool.Layer), typeof(byte), typeof(float), typeof(Vector3), typeof(Vector3), typeof(bool)
            });
            if (method == null) throw new MissingMethodException("DistrictTool.ApplyBrush park authority signature is unavailable.");
            return method;
        }

        public static bool Prefix(DistrictTool.Layer layer, byte districtOrPark, float brushRadius,
            Vector3 startPosition, Vector3 endPosition, bool force, out IDisposable __state)
        {
            __state = null;
            if (RuntimeScopeGuard.IsApplying || (layer & DistrictTool.Layer.Parks) == 0) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role == CitiesRuntimeRole.HostLive)
            {
                __state = RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id);
                return true;
            }
            if (role != CitiesRuntimeRole.ClientReplicaLive) return false;

            ParkBrushIntentKind kind;
            EntityIdentityV2 target = default(EntityIdentityV2);
            int parkType = 0, parkLevel = 0;
            EntityIdMapV2 parkIds = ExtensionIdentityServices.Maps.GetOrAttach("builtin.districtpark");
            if (districtOrPark == 0) kind = ParkBrushIntentKind.Erase;
            else if (parkIds.TryGetIdentity(districtOrPark, out target)) kind = ParkBrushIntentKind.Existing;
            else
            {
                DistrictManager manager = DistrictManager.instance;
                if (manager == null ||
                    (manager.m_parks.m_buffer[districtOrPark].m_flags & DistrictPark.Flags.Created) == DistrictPark.Flags.None)
                {
                    RuntimeServices.Lifecycle.Fence("Park brush referenced an unknown local park");
                    return false;
                }
                DistrictPark speculative = manager.m_parks.m_buffer[districtOrPark];
                parkType = (int)speculative.m_parkType;
                parkLevel = (int)speculative.m_parkLevel;
                using (RuntimeScopeGuard.EnterApply(RuntimeServices.Lifecycle.Current, ExtensionStateAuthorityDomain.Id))
                    manager.ReleasePark(districtOrPark);
                kind = ParkBrushIntentKind.CreateNew;
            }

            byte[] intent;
            try
            {
                intent = ParkGridStateAdapter.EncodeBrush(kind, target, parkType, parkLevel,
                    brushRadius, startPosition, endPosition, force);
            }
            catch
            {
                RuntimeServices.Lifecycle.Fence("Park brush produced an invalid extension intent");
                return false;
            }
            if (!ForgeExtensionApi.TrySubmitIntent("builtin.parkgrid", intent))
                RuntimeServices.Lifecycle.Fence("Park brush could not be routed through Host extension authority");
            return false;
        }

        public static void Postfix(IDisposable __state)
        {
            if (__state != null) __state.Dispose();
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "CreatePark")]
    internal static class DistrictParkCreateSlotBarrierPatch
    {
        public static bool Prefix(ref byte park, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading ||
                role == CitiesRuntimeRole.HostLive) return true;
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                ToolController controller = ToolsModifierControl.toolController;
                DistrictTool tool = controller == null ? null : controller.CurrentTool as DistrictTool;
                if (tool != null && (tool.m_layer & DistrictTool.Layer.Parks) != 0) return true;
            }
            park = 0; __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "ReleasePark")]
    internal static class DistrictParkReleaseSlotBarrierPatch
    {
        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled ||
                role == CitiesRuntimeRole.Unloading || role == CitiesRuntimeRole.HostLive;
        }
    }
}
