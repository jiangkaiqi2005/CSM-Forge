using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Semantic shim for Network Multitool 1.3.9. Native ushort node/segment slots never leave
    /// the local process: Client and Host-local operations are converted to NetIntentV2 Stable IDs,
    /// then the Host invokes the audited high-level Multitool operation and NetAuthorityDomain
    /// captures the resulting absolute graph mutation.
    /// </summary>
    internal static class NetworkMultitoolBridge
    {
        private const string ModTypeName = "NetworkMultitool.Mod";
        private static bool patched;

        internal static bool IsAvailable { get { return ResolveType(ModTypeName) != null; } }

        internal static bool IsSemanticIntent(NetIntentKindV2 kind)
        {
            return kind == NetIntentKindV2.MultitoolAddNode ||
                kind == NetIntentKindV2.MultitoolRemoveNode ||
                kind == NetIntentKindV2.MultitoolUnionNodes ||
                kind == NetIntentKindV2.MultitoolSplitNode ||
                kind == NetIntentKindV2.MultitoolIntersectSegments;
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null || patched || !IsAvailable) return;

            // Resolve every required 1.3.9 surface before installing any patch. An installed but
            // incompatible Multitool build must fail closed rather than leave a partially shimmed tool.
            MethodInfo[] methods = new MethodInfo[]
            {
                ResolveRequired("NetworkMultitool.AddNodeMode", "InsertNode", 2),
                ResolveRequired("NetworkMultitool.RemoveNodeMode", "RemoveNode", 1),
                ResolveRequired("NetworkMultitool.UnionNodeMode", "Union", 2),
                ResolveRequired("NetworkMultitool.SplitNodeMode", "Split", 3),
                ResolveRequired("NetworkMultitool.IntersectSegmentMode", "IntersectSegments", 2)
            };
            MethodInfo prefix = typeof(NetworkMultitoolBridge).GetMethod("SemanticOperationPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null) throw new MissingMethodException("Network Multitool semantic prefix is unavailable.");
            HarmonyMethod harmonyPrefix = new HarmonyMethod(prefix);
            for (int i = 0; i < methods.Length; i++) harmony.Patch(methods[i], harmonyPrefix);
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; }

        private static bool SemanticOperationPrefix(MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive)
            {
                __result = false;
                return false;
            }

            NetIntentV2 intent;
            if (!TryBuildIntent(role, __originalMethod, __args, out intent))
            {
                __result = false;
                RuntimeServices.Lifecycle.Fence("network-multitool-semantic-capture-failed");
                return false;
            }

            __result = RuntimeServices.Multiplayer.TrySubmitNetIntent(intent);
            if (!__result && role == CitiesRuntimeRole.ClientReplicaLive)
                RuntimeServices.Lifecycle.Fence("network-multitool-intent-could-not-be-queued");
            return false;
        }

        private static bool TryBuildIntent(CitiesRuntimeRole role, MethodBase method, object[] args, out NetIntentV2 intent)
        {
            intent = null;
            if (method == null || args == null || method.DeclaringType == null) return false;
            string type = method.DeclaringType.FullName;
            try
            {
                if (type == "NetworkMultitool.AddNodeMode" && method.Name == "InsertNode" && args.Length == 2)
                {
                    EntityIdentityV2 segment; Vector3 position = (Vector3)args[1];
                    if (!TryResolveSegment(role, Convert.ToUInt16(args[0]), out segment)) return false;
                    intent = NetIntentV2.MultitoolAddNode(segment, position.x, position.y, position.z);
                    return true;
                }
                if (type == "NetworkMultitool.RemoveNodeMode" && method.Name == "RemoveNode" && args.Length == 1)
                {
                    EntityIdentityV2 node;
                    if (!TryResolveNode(role, Convert.ToUInt16(args[0]), out node)) return false;
                    intent = NetIntentV2.MultitoolRemoveNode(node);
                    return true;
                }
                if (type == "NetworkMultitool.UnionNodeMode" && method.Name == "Union" && args.Length == 2)
                {
                    EntityIdentityV2 source, target;
                    if (!TryResolveNode(role, Convert.ToUInt16(args[0]), out source) ||
                        !TryResolveNode(role, Convert.ToUInt16(args[1]), out target)) return false;
                    intent = NetIntentV2.MultitoolUnionNodes(source, target);
                    return true;
                }
                if (type == "NetworkMultitool.SplitNodeMode" && method.Name == "Split" && args.Length == 3)
                {
                    EntityIdentityV2 source; Vector3 position = (Vector3)args[1];
                    if (!TryResolveNode(role, Convert.ToUInt16(args[0]), out source)) return false;
                    EntityIdentityV2[] segments;
                    if (!TryResolveSelectionSegments(role, args[2] as IEnumerable, out segments)) return false;
                    intent = NetIntentV2.MultitoolSplitNode(source, position.x, position.y, position.z, segments);
                    return true;
                }
                if (type == "NetworkMultitool.IntersectSegmentMode" && method.Name == "IntersectSegments" && args.Length == 2)
                {
                    EntityIdentityV2 first, second;
                    if (!TryResolveSegment(role, Convert.ToUInt16(args[0]), out first) ||
                        !TryResolveSegment(role, Convert.ToUInt16(args[1]), out second)) return false;
                    intent = NetIntentV2.MultitoolIntersectSegments(first, second);
                    return true;
                }
            }
            catch { return false; }
            return false;
        }

        private static bool TryResolveSelectionSegments(CitiesRuntimeRole role, IEnumerable values, out EntityIdentityV2[] segments)
        {
            segments = null;
            if (values == null) return false;
            List<EntityIdentityV2> result = new List<EntityIdentityV2>();
            foreach (object value in values)
            {
                if (value == null || result.Count >= 7) return false;
                PropertyInfo id = value.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
                if (id == null) return false;
                EntityIdentityV2 segment;
                if (!TryResolveSegment(role, Convert.ToUInt16(id.GetValue(value, null)), out segment)) return false;
                result.Add(segment);
            }
            if (result.Count == 0) return false;
            segments = result.ToArray();
            return true;
        }

        private static bool TryResolveNode(CitiesRuntimeRole role, ushort native, out EntityIdentityV2 identity)
        {
            if (role == CitiesRuntimeRole.HostLive) return RuntimeServices.Multiplayer.TryResolveHostNetNode(native, out identity);
            return RuntimeServices.Multiplayer.TryResolveClientNetNode(native, out identity);
        }

        private static bool TryResolveSegment(CitiesRuntimeRole role, ushort native, out EntityIdentityV2 identity)
        {
            if (role == CitiesRuntimeRole.HostLive) return RuntimeServices.Multiplayer.TryResolveHostNetSegment(native, out identity);
            return RuntimeServices.Multiplayer.TryResolveClientNetSegment(native, out identity);
        }

        internal static bool TryExecuteHost(NetIntentV2 intent, EntityIdMapV2 nodeIds, EntityIdMapV2 segmentIds)
        {
            if (intent == null || nodeIds == null || segmentIds == null || !IsSemanticIntent(intent.Kind) || !IsAvailable)
                return false;
            MethodInfo method;
            object[] args;
            uint first, second;
            if (intent.Kind == NetIntentKindV2.MultitoolAddNode)
            {
                if (!segmentIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = ResolveRequired("NetworkMultitool.AddNodeMode", "InsertNode", 2);
                args = new object[] { (ushort)first, new Vector3(intent.X, intent.Y, intent.Z) };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolRemoveNode)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = ResolveRequired("NetworkMultitool.RemoveNodeMode", "RemoveNode", 1);
                args = new object[] { (ushort)first };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolUnionNodes)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !nodeIds.TryGetNative(intent.SecondaryTarget, out second) ||
                    !UShort(first) || !UShort(second)) return false;
                method = ResolveRequired("NetworkMultitool.UnionNodeMode", "Union", 2);
                args = new object[] { (ushort)first, (ushort)second };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolSplitNode)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = ResolveRequired("NetworkMultitool.SplitNodeMode", "Split", 3);
                object selections = BuildSegmentSelectionArray(method.DeclaringType.Assembly, intent.RelatedTargets, segmentIds);
                if (selections == null) return false;
                args = new object[] { (ushort)first, new Vector3(intent.X, intent.Y, intent.Z), selections };
            }
            else
            {
                if (!segmentIds.TryGetNative(intent.Target, out first) || !segmentIds.TryGetNative(intent.SecondaryTarget, out second) ||
                    !UShort(first) || !UShort(second)) return false;
                method = ResolveRequired("NetworkMultitool.IntersectSegmentMode", "IntersectSegments", 2);
                args = new object[] { (ushort)first, (ushort)second };
            }

            object result = method.Invoke(null, args);
            return result is bool && (bool)result;
        }

        private static object BuildSegmentSelectionArray(Assembly assembly, EntityIdentityV2[] values, EntityIdMapV2 segmentIds)
        {
            if (assembly == null || values == null || values.Length == 0) return null;
            Type baseType = assembly.GetType("ModsCommon.Utilities.Selection", false);
            Type segmentType = assembly.GetType("ModsCommon.Utilities.SegmentSelection", false);
            if (baseType == null || segmentType == null || !baseType.IsAssignableFrom(segmentType)) return null;
            ConstructorInfo constructor = segmentType.GetConstructor(new Type[] { typeof(ushort) });
            if (constructor == null) return null;
            Array array = Array.CreateInstance(baseType, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                uint native;
                if (!segmentIds.TryGetNative(values[i], out native) || !UShort(native)) return null;
                array.SetValue(constructor.Invoke(new object[] { (ushort)native }), i);
            }
            return array;
        }

        private static bool UShort(uint value) { return value != 0 && value <= ushort.MaxValue; }

        private static MethodInfo ResolveRequired(string typeName, string methodName, int parameterCount)
        {
            Type type = ResolveType(typeName);
            if (type == null) throw new MissingMemberException(typeName);
            MethodInfo found = null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != methodName || candidate.ReturnType != typeof(bool) ||
                    candidate.GetParameters().Length != parameterCount) continue;
                if (found != null) throw new AmbiguousMatchException(typeName + "." + methodName);
                found = candidate;
            }
            if (found == null) throw new MissingMethodException(typeName, methodName);
            return found;
        }

        private static Type ResolveType(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
