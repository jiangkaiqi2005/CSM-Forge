using System;
using System.Collections;
using System.Reflection;
using ColossalFramework.Math;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class BuildingToolIntentScope
    {
        [ThreadStatic] private static int depth;
        public static bool Active { get { return depth > 0; } }
        public static void Enter() { depth++; }
        public static void Exit() { if (depth <= 0) throw new InvalidOperationException("Building tool scope underflow."); depth--; }
    }

    internal static class BulldozeBuildingScope
    {
        [ThreadStatic] private static int depth;
        public static bool Active { get { return depth > 0; } }
        public static void Enter() { depth++; }
        public static void Exit() { if (depth <= 0) throw new InvalidOperationException("Bulldoze building scope underflow."); depth--; }
    }

    internal static class IteratorPatchLookup
    {
        public static MethodBase FindMoveNext(Type owner, string iteratorName)
        {
            Type[] nested = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
            {
                if (nested[i].Name.IndexOf(iteratorName, StringComparison.Ordinal) < 0) continue;
                MethodInfo moveNext = nested[i].GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext != null) return moveNext;
            }
            throw new MissingMethodException(owner.FullName, iteratorName + ".MoveNext");
        }

        public static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }
    }

    [HarmonyPatch]
    internal static class BuildingToolCreateMoveNextPatch
    {
        public static MethodBase TargetMethod() { return IteratorPatchLookup.FindMoveNext(typeof(BuildingTool), "<CreateBuilding>"); }
        public static void Prefix(object __instance, out bool __state)
        {
            __state = false; if (RuntimeScopeGuard.IsApplying) return;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return;
            FieldInfo ownerField = IteratorPatchLookup.FindField(__instance.GetType(), "$this") ?? IteratorPatchLookup.FindField(__instance.GetType(), "<>f__this");
            BuildingTool tool = ownerField == null ? null : ownerField.GetValue(__instance) as BuildingTool;
            if (tool == null || tool.m_prefab == null || tool.m_relocate != 0 || tool.m_prefab.m_buildingAI is ExtractingFacilityAI) return;
            FieldInfo pcField = IteratorPatchLookup.FindField(__instance.GetType(), "$PC") ?? IteratorPatchLookup.FindField(__instance.GetType(), "<>1__state");
            if (pcField != null)
            {
                int state = Convert.ToInt32(pcField.GetValue(__instance)); if (state != 0 && state != -1) return;
            }
            FieldInfo placementField = IteratorPatchLookup.FindField(typeof(BuildingTool), "m_placementErrors");
            if (placementField != null && (ToolBase.ToolErrors)placementField.GetValue(tool) != ToolBase.ToolErrors.None) return;
            BuildingToolIntentScope.Enter(); __state = true;
        }
        public static Exception Finalizer(bool __state, Exception __exception)
        { return !__state ? __exception : RuntimeScopeGuard.Finish(__exception, BuildingToolIntentScope.Exit); }
    }

    [HarmonyPatch]
    internal static class BulldozeDeleteBuildingMoveNextPatch
    {
        public static MethodBase TargetMethod() { return IteratorPatchLookup.FindMoveNext(typeof(BulldozeTool), "<DeleteBuilding>"); }
        public static void Prefix(out bool __state)
        {
            __state = false;
            if (RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive) return;
            BulldozeBuildingScope.Enter(); __state = true;
        }
        public static Exception Finalizer(bool __state, Exception __exception)
        { return !__state ? __exception : RuntimeScopeGuard.Finish(__exception, BulldozeBuildingScope.Exit); }
    }

    [HarmonyPatch]
    internal static class ClientBulldozeBuildingIntentPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(BulldozeTool), "DeleteBuilding", new Type[] { typeof(ushort) });
            if (method == null) throw new MissingMethodException("BulldozeTool.DeleteBuilding(ushort) is unavailable.");
            return method;
        }
        private static IEnumerator Empty() { yield break; }
        public static bool Prefix(ushort __0, ref IEnumerator __result)
        {
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                bool queued = RuntimeServices.Multiplayer.TryQueueClientBuildingDelete(__0);
                __result = Empty();
                if (!queued) RuntimeServices.Lifecycle.Fence("Building bulldoze intent could not be queued");
                return false;
            }
            if (role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.WorldFenced)
            { __result = Empty(); return false; }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class BuildingToolConstructionFetchPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(EconomyManager), "FetchResource", new Type[]
            { typeof(EconomyManager.Resource), typeof(int), typeof(ItemClass) });
            if (method == null) throw new MissingMethodException("EconomyManager.FetchResource(Resource,int,ItemClass) is unavailable.");
            return method;
        }
        public static bool Prefix(EconomyManager.Resource __0, int __1, ref int __result)
        {
            if (!RuntimeScopeGuard.IsApplying && RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.ClientReplicaLive &&
                BuildingToolIntentScope.Active && __0 == EconomyManager.Resource.Construction)
            { __result = __1; return false; }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class BuildingManagerCreateWriteBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(BuildingManager), "CreateBuilding", new Type[]
            { typeof(ushort).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(BuildingInfo), typeof(Vector3), typeof(float), typeof(int), typeof(uint) });
            if (method == null) throw new MissingMethodException("BuildingManager.CreateBuilding signature is unavailable."); return method;
        }
        public static bool Prefix(ref ushort __0, BuildingInfo __2, Vector3 __3, float __4, int __5, ref bool __result, out bool __state)
        {
            __state = false; if (RuntimeScopeGuard.IsApplying) return true; CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading) return true;
            if (BuildingToolIntentScope.Active && role == CitiesRuntimeRole.ClientReplicaLive)
            {
                bool valid = __2 != null && !string.IsNullOrEmpty(__2.name) && __5 > 0 && __5 <= byte.MaxValue;
                bool queued = valid && RuntimeServices.Multiplayer.TryQueueBuilding(BuildingIntentV2.Create(__2.name, __3.x, __3.y, __3.z, __4, (byte)__5, 0));
                __0 = 0; __result = false; if (!queued) RuntimeServices.Lifecycle.Fence("Building player intent could not be queued"); return false;
            }
            if (role == CitiesRuntimeRole.HostLive) { __state = true; return true; }
            __0 = 0; __result = false; return false;
        }
        public static void Postfix(ushort __0, BuildingInfo __2, uint __6, bool __result, bool __state)
        {
            if (!__state || !__result || __0 == 0 || RuntimeScopeGuard.IsApplying) return;
            int constructionCost = BuildingToolIntentScope.Active && __2 != null && ToolManager.instance != null &&
                (ToolManager.instance.m_properties.m_mode & ItemClass.Availability.Game) != 0 ? Math.Max(0, __2.GetConstructionCost()) : 0;
            RuntimeServices.Multiplayer.ObserveHostBuildingCreated(__0, __6, constructionCost);
        }
    }

    [HarmonyPatch(typeof(BuildingManager), "ReleaseBuildingImplementation")]
    internal static class BuildingManagerReleaseWriteBarrierPatch
    {
        public static bool Prefix(ushort building, out ObservedBuildingDeleteTicket __state)
        {
            __state = null; if (RuntimeScopeGuard.IsApplying) return true; CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading) return true;
            if (role == CitiesRuntimeRole.HostLive)
            { __state = RuntimeServices.Multiplayer.PrepareHostBuildingDelete(building, BulldozeBuildingScope.Active); return true; }
            return false;
        }
        public static void Postfix(ushort building, ObservedBuildingDeleteTicket __state)
        {
            if (__state == null || RuntimeScopeGuard.IsApplying) return; BuildingManager manager = BuildingManager.instance;
            if (manager == null || manager.m_buildings.m_buffer[building].m_flags != Building.Flags.None)
                throw new InvalidOperationException("Host building deletion did not publish an absent building.");
            RuntimeServices.Multiplayer.ObserveHostBuildingDeleted(__state);
        }
    }

    [HarmonyPatch(typeof(BuildingManager), "SimulationStepImpl")]
    internal static class BuildingManagerClientSimulationBarrierPatch
    {
        public static bool Prefix()
        {
            return RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.ClientReplicaLive;
        }
    }
}
