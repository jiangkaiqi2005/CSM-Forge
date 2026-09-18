using System;
using System.Collections.Generic;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class DynamicSimulationPatchPolicy
    {
        internal static bool AllowClientManagerMutation()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer ||
                role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive || role == CitiesRuntimeRole.Unloading;
        }
    }

    [HarmonyPatch]
    internal static class PathManagerCreateBarrierPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo[] methods = typeof(PathManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++) if (methods[i].Name == "CreatePath") yield return methods[i];
        }
        public static bool Prefix(ref uint __0, ref bool __result)
        { if (DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return true; __0 = 0; __result = false; return false; }
        public static void Postfix(uint __0, bool __result)
        {
            if (__result && __0 != 0 && !RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive)
                PathUnitStateAdapter.ObserveHostCreated(__0);
        }
    }

    [HarmonyPatch(typeof(PathManager), "ReleasePath")]
    internal static class PathManagerReleaseBarrierPatch
    {
        public static bool Prefix(uint __0, out EntityIdentityV2[] __state)
        {
            __state = null; if (!DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return false;
            if (!RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive)
                __state = PathUnitStateAdapter.PrepareHostRelease(__0, true);
            return true;
        }
        public static void Postfix(EntityIdentityV2[] __state) { PathUnitStateAdapter.CompleteHostRelease(__state); }
    }

    [HarmonyPatch(typeof(PathManager), "ReleaseFirstUnit")]
    internal static class PathManagerReleaseFirstBarrierPatch
    {
        public static bool Prefix(ref uint __0, out EntityIdentityV2[] __state)
        {
            __state = null; if (!DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return false;
            if (!RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive)
                __state = PathUnitStateAdapter.PrepareHostRelease(__0, false);
            return true;
        }
        public static void Postfix(EntityIdentityV2[] __state) { PathUnitStateAdapter.CompleteHostRelease(__state); }
    }

    [HarmonyPatch(typeof(PathManager), "SimulationStepImpl")]
    internal static class PathManagerSimulationBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
    }

    [HarmonyPatch(typeof(VehicleManager), "CreateVehicle")]
    internal static class VehicleManagerCreateBarrierPatch
    {
        public static bool Prefix(ref ushort __0, ref bool __result)
        { if (DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return true; __0 = 0; __result = false; return false; }
        public static void Postfix(ushort __0, bool __result)
        { if (__result && !RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive) VehiclePresentationStateAdapter.ObserveHostCreated(__0); }
    }

    [HarmonyPatch(typeof(VehicleManager), "ReleaseVehicle")]
    internal static class VehicleManagerReleaseBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
        public static void Postfix() { if (!RuntimeScopeGuard.IsApplying) VehiclePresentationStateAdapter.ReconcileReleasedHostVehicles(); }
    }

    [HarmonyPatch(typeof(VehicleManager), "SimulationStepImpl")]
    internal static class VehicleManagerSimulationBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
    }

    [HarmonyPatch(typeof(CitizenManager), "CreateCitizenInstance")]
    internal static class CitizenManagerCreateInstanceBarrierPatch
    {
        public static bool Prefix(ref ushort __0, ref bool __result)
        { if (DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return true; __0 = 0; __result = false; return false; }
        public static void Postfix(ushort __0, bool __result)
        { if (__result && !RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.HostLive) CitizenInstancePresentationStateAdapter.ObserveHostCreated(__0); }
    }

    [HarmonyPatch(typeof(CitizenManager), "ReleaseCitizenInstance")]
    internal static class CitizenManagerReleaseInstanceBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
        public static void Postfix() { if (!RuntimeScopeGuard.IsApplying) CitizenInstancePresentationStateAdapter.ReconcileReleasedHostInstances(); }
    }

    [HarmonyPatch(typeof(CitizenManager), "SimulationStepImpl")]
    internal static class CitizenManagerSimulationBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
    }

    [HarmonyPatch]
    internal static class CitizenManagerCreateCitizenBarrierPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo[] methods = typeof(CitizenManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++) if (methods[i].Name == "CreateCitizen") yield return methods[i];
        }
        public static bool Prefix(ref uint __0, ref bool __result)
        { if (DynamicSimulationPatchPolicy.AllowClientManagerMutation()) return true; __0 = 0; __result = false; return false; }
    }

    [HarmonyPatch(typeof(CitizenManager), "ReleaseCitizen")]
    internal static class CitizenManagerReleaseCitizenBarrierPatch
    {
        public static bool Prefix() { return DynamicSimulationPatchPolicy.AllowClientManagerMutation(); }
    }
}
