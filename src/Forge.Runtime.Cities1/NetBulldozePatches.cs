using System;
using System.Collections;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class EmptySimulationAction
    {
        public static IEnumerator Create() { return new object[0].GetEnumerator(); }
    }

    [HarmonyPatch]
    internal static class ClientBulldozeSegmentIntentPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo[] methods = typeof(BulldozeTool).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "DeleteSegment" && parameters.Length == 1 && parameters[0].ParameterType == typeof(ushort) &&
                    typeof(IEnumerator).IsAssignableFrom(method.ReturnType)) return method;
            }
            throw new MissingMethodException("BulldozeTool.DeleteSegment(ushort) coroutine is unavailable.");
        }

        public static bool Prefix(ushort segment, ref IEnumerator __result)
        {
            if (RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.ClientReplicaLive) return true;
            EntityIdentityV2 entity;
            bool queued = RuntimeServices.Multiplayer.TryResolveClientNetSegment(segment, out entity) &&
                RuntimeServices.Multiplayer.TrySubmitNetIntent(NetIntentV2.DeleteSegment(entity, false));
            __result = EmptySimulationAction.Create();
            if (!queued) RuntimeServices.Lifecycle.Fence("Road segment bulldoze intent could not be queued");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ClientBulldozeNodeIntentPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo[] methods = typeof(BulldozeTool).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "DeleteNode" && parameters.Length == 1 && parameters[0].ParameterType == typeof(ushort) &&
                    typeof(IEnumerator).IsAssignableFrom(method.ReturnType)) return method;
            }
            throw new MissingMethodException("BulldozeTool.DeleteNode(ushort) coroutine is unavailable.");
        }

        public static bool Prefix(ushort node, ref IEnumerator __result)
        {
            if (RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.ClientReplicaLive) return true;
            EntityIdentityV2 entity;
            bool queued = RuntimeServices.Multiplayer.TryResolveClientNetNode(node, out entity) &&
                RuntimeServices.Multiplayer.TrySubmitNetIntent(NetIntentV2.DeleteNode(entity));
            __result = EmptySimulationAction.Create();
            if (!queued) RuntimeServices.Lifecycle.Fence("Road node bulldoze intent could not be queued");
            return false;
        }
    }

    internal sealed class HostNetBulldozeMoveState
    {
        public Hash256 BeforeRoot;
        public EconomySideEffectScope Economy;
        public void DisposeEconomy()
        {
            if (Economy == null) return;
            Economy.Dispose(); Economy = null;
        }
    }

    internal static class BulldozeIteratorFinder
    {
        public static MethodBase Find(string marker)
        {
            Type[] nested = typeof(BulldozeTool).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
            {
                Type type = nested[i];
                if (type.Name.IndexOf(marker, StringComparison.Ordinal) < 0) continue;
                MethodInfo move = type.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (move != null) return move;
            }
            throw new MissingMethodException("Could not find BulldozeTool iterator: " + marker);
        }
    }

    [HarmonyPatch]
    internal static class HostBulldozeSegmentMoveNextPatch
    {
        public static MethodBase TargetMethod() { return BulldozeIteratorFinder.Find("<DeleteSegment>"); }

        public static void Prefix(out HostNetBulldozeMoveState __state)
        {
            __state = null;
            if (RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive ||
                !RuntimeServices.Multiplayer.IsHostNetAuthorityActive) return;
            Hash256 before = RuntimeServices.Multiplayer.CaptureHostNetRoot();
            if (before == null) return;
            __state = new HostNetBulldozeMoveState { BeforeRoot = before, Economy = EconomySideEffectCapture.Begin() };
        }

        public static void Postfix(HostNetBulldozeMoveState __state)
        {
            if (__state == null) return;
            int construction = __state.Economy == null ? 0 : __state.Economy.ConstructionFetched;
            int refund = __state.Economy == null ? 0 : __state.Economy.RefundAdded;
            __state.DisposeEconomy();
            RuntimeServices.Multiplayer.PublishObservedHostNet(__state.BeforeRoot, construction, refund);
        }

        public static Exception Finalizer(HostNetBulldozeMoveState __state, Exception __exception)
        {
            if (__state != null) __state.DisposeEconomy();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class HostBulldozeNodeMoveNextPatch
    {
        public static MethodBase TargetMethod() { return BulldozeIteratorFinder.Find("<DeleteNode>"); }

        public static void Prefix(out HostNetBulldozeMoveState __state)
        {
            __state = null;
            if (RuntimeScopeGuard.IsApplying || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive ||
                !RuntimeServices.Multiplayer.IsHostNetAuthorityActive) return;
            Hash256 before = RuntimeServices.Multiplayer.CaptureHostNetRoot();
            if (before == null) return;
            __state = new HostNetBulldozeMoveState { BeforeRoot = before, Economy = EconomySideEffectCapture.Begin() };
        }

        public static void Postfix(HostNetBulldozeMoveState __state)
        {
            if (__state == null) return;
            int construction = __state.Economy == null ? 0 : __state.Economy.ConstructionFetched;
            int refund = __state.Economy == null ? 0 : __state.Economy.RefundAdded;
            __state.DisposeEconomy();
            RuntimeServices.Multiplayer.PublishObservedHostNet(__state.BeforeRoot, construction, refund);
        }

        public static Exception Finalizer(HostNetBulldozeMoveState __state, Exception __exception)
        {
            if (__state != null) __state.DisposeEconomy();
            return __exception;
        }
    }
}
