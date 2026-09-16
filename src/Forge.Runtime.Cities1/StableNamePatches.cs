using System;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class StableNamePatchHelper
    {
        public static MethodBase Iterator(Type owner, string nestedName)
        {
            Type iterator = owner.GetNestedType(nestedName, BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (iterator == null) throw new MissingMemberException(owner.FullName, nestedName);
            MethodInfo moveNext = iterator.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (moveNext == null) throw new MissingMethodException(iterator.FullName, "MoveNext");
            return moveNext;
        }

        public static bool Intercept(StableNameTargetKindV2 kind, uint nativeId, string name,
            object iterator, ref bool result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;

            FieldInfo pc = iterator == null ? null : iterator.GetType().GetField("$PC",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pc == null)
            {
                RuntimeServices.Lifecycle.Fence("Name coroutine program counter is unavailable");
                result = false;
                return false;
            }
            int counter = Convert.ToInt32(pc.GetValue(iterator));
            if (counter != 0) return true;

            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive)
            {
                result = false;
                return false;
            }

            string normalized = name ?? string.Empty;
            try
            {
                if (StringComparer.Ordinal.Equals(StableNameGameAccess.Capture(kind, nativeId), normalized))
                {
                    result = false;
                    return false;
                }
            }
            catch
            {
                RuntimeServices.Lifecycle.Fence("Name target could not be inspected");
                result = false;
                return false;
            }

            if (!RuntimeServices.Multiplayer.TryQueueStableName(kind, nativeId, normalized))
                RuntimeServices.Lifecycle.Fence("Name change could not be routed through Host authority");
            result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ForgeBuildingNameAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            return StableNamePatchHelper.Iterator(typeof(BuildingManager), "<SetBuildingName>c__Iterator2");
        }
        public static bool Prefix(object __instance, ushort ___building, string ___name, ref bool __result)
        {
            return StableNamePatchHelper.Intercept(StableNameTargetKindV2.Building, ___building, ___name, __instance, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class ForgeSegmentNameAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            return StableNamePatchHelper.Iterator(typeof(NetManager), "<SetSegmentName>c__Iterator2");
        }
        public static bool Prefix(object __instance, ushort ___segmentID, string ___name, ref bool __result)
        {
            return StableNamePatchHelper.Intercept(StableNameTargetKindV2.NetSegment, ___segmentID, ___name, __instance, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class ForgeDistrictNameAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            return StableNamePatchHelper.Iterator(typeof(DistrictManager), "<SetDistrictName>c__Iterator0");
        }
        public static bool Prefix(object __instance, int ___district, string ___name, ref bool __result)
        {
            if (___district <= 0 || ___district > byte.MaxValue)
            {
                __result = false;
                return false;
            }
            return StableNamePatchHelper.Intercept(StableNameTargetKindV2.District, (uint)___district, ___name, __instance, ref __result);
        }
    }

    [HarmonyPatch]
    internal static class ForgeTransportLineNameAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            return StableNamePatchHelper.Iterator(typeof(TransportManager), "<SetLineName>c__Iterator1");
        }
        public static bool Prefix(object __instance, ushort ___lineID, string ___name, ref bool __result)
        {
            return StableNamePatchHelper.Intercept(StableNameTargetKindV2.TransportLine, ___lineID, ___name, __instance, ref __result);
        }
    }
}
