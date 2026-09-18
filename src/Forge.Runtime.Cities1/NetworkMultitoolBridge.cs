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
        // Official v1.3.9 tag: 2abaf77e665f8b1c2cae65de188f9109f28f0c0b.
        private static readonly Version SupportedVersion = new Version(1, 3, 9, 0);
        private static bool patched;
        private static Surface surface;

        private sealed class Surface
        {
            internal Type PointType;
            internal Type PointArrayType;
            internal Type SelectionType;
            internal ConstructorInfo PointConstructor;
            internal ConstructorInfo SegmentSelectionConstructor;
            internal FieldInfo PointPosition;
            internal FieldInfo PointForward;
            internal FieldInfo PointBackward;
            internal PropertyInfo NeedMoney;
            internal PropertyInfo NeedMoneyValue;
            internal MethodInfo GetCost;
            internal MethodInfo AddNode;
            internal MethodInfo RemoveNode;
            internal MethodInfo UnionNodes;
            internal MethodInfo SplitNode;
            internal MethodInfo IntersectSegments;
            internal MethodInfo CreateParallel;
            internal MethodInfo CreateConnection;
        }

        internal static bool IsAvailable { get { return ResolveType(ModTypeName) != null; } }

        internal static bool IsSemanticIntent(NetIntentKindV2 kind)
        {
            return kind == NetIntentKindV2.MultitoolAddNode ||
                kind == NetIntentKindV2.MultitoolRemoveNode ||
                kind == NetIntentKindV2.MultitoolUnionNodes ||
                kind == NetIntentKindV2.MultitoolSplitNode ||
                kind == NetIntentKindV2.MultitoolIntersectSegments ||
                kind == NetIntentKindV2.MultitoolCreateParallel ||
                kind == NetIntentKindV2.MultitoolCreateConnection;
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null || patched || !IsAvailable) return;

            // Resolve every required 1.3.9 surface before installing any patch. An installed but
            // incompatible Multitool build must fail closed rather than leave a partially shimmed tool.
            Surface resolved = ResolveSurface();
            MethodInfo[] boolMethods = new MethodInfo[]
            {
                resolved.AddNode, resolved.RemoveNode, resolved.UnionNodes, resolved.SplitNode, resolved.IntersectSegments
            };
            MethodInfo[] voidMethods = new MethodInfo[]
            {
                resolved.CreateParallel, resolved.CreateConnection
            };
            MethodInfo boolPrefix = typeof(NetworkMultitoolBridge).GetMethod("SemanticOperationPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo voidPrefix = typeof(NetworkMultitoolBridge).GetMethod("SemanticVoidOperationPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (boolPrefix == null || voidPrefix == null) throw new MissingMethodException("Network Multitool semantic prefix is unavailable.");
            HarmonyMethod boolHarmony = new HarmonyMethod(boolPrefix);
            HarmonyMethod voidHarmony = new HarmonyMethod(voidPrefix);
            for (int i = 0; i < boolMethods.Length; i++) harmony.Patch(boolMethods[i], boolHarmony);
            for (int i = 0; i < voidMethods.Length; i++) harmony.Patch(voidMethods[i], voidHarmony);
            surface = resolved;
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; surface = null; }

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

        private static bool SemanticVoidOperationPrefix(MethodBase __originalMethod, object[] __args)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;

            NetIntentV2 intent;
            if (!TryBuildIntent(role, __originalMethod, __args, out intent))
            {
                RuntimeServices.Lifecycle.Fence("network-multitool-semantic-capture-failed");
                return false;
            }
            bool queued = RuntimeServices.Multiplayer.TrySubmitNetIntent(intent);
            if (!queued && role == CitiesRuntimeRole.ClientReplicaLive)
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
                if (type == "NetworkMultitool.CreateParallelMode" && method.Name == "Create" && args.Length == 4)
                {
                    NetInfo info = args[2] as NetInfo; NetMultitoolPointV2[] points;
                    if (info == null || string.IsNullOrEmpty(info.name) || !TryExtractPoints(args[0] as Array, out points)) return false;
                    intent = NetIntentV2.MultitoolCreateParallel(info.name, Convert.ToBoolean(args[1]), points);
                    return true;
                }
                if (type == "NetworkMultitool.BaseCreateMode" && method.Name == "Create" && args.Length == 9)
                {
                    EntityIdentityV2 first, second; NetInfo info = args[6] as NetInfo; NetMultitoolPointV2[] points;
                    if (!TryResolveSegment(role, Convert.ToUInt16(args[2]), out first) ||
                        !TryResolveSegment(role, Convert.ToUInt16(args[3]), out second) ||
                        info == null || string.IsNullOrEmpty(info.name) || !TryExtractPoints(args[0] as Array, out points)) return false;
                    intent = NetIntentV2.MultitoolCreateConnection(first, second, Convert.ToBoolean(args[4]),
                        Convert.ToBoolean(args[5]), info.name, Convert.ToBoolean(args[1]), Convert.ToBoolean(args[7]), points);
                    return true;
                }
            }
            catch { return false; }
            return false;
        }

        private static bool TryExtractPoints(Array values, out NetMultitoolPointV2[] points)
        {
            points = null;
            if (values == null || values.Length < 2 || values.Length > 512) return false;
            Surface active = surface ?? ResolveSurface();
            if (values.GetType() != active.PointArrayType) return false;
            NetMultitoolPointV2[] result = new NetMultitoolPointV2[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                object item = values.GetValue(i); if (item == null) return false;
                Vector3 p = (Vector3)active.PointPosition.GetValue(item);
                Vector3 f = (Vector3)active.PointForward.GetValue(item);
                Vector3 b = (Vector3)active.PointBackward.GetValue(item);
                result[i] = new NetMultitoolPointV2(p.x, p.y, p.z, f.x, f.y, f.z, b.x, b.y, b.z);
            }
            points = result; return true;
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
            Surface active = surface ?? ResolveSurface();
            MethodInfo method;
            object[] args;
            uint first, second;
            if (intent.Kind == NetIntentKindV2.MultitoolAddNode)
            {
                if (!segmentIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = active.AddNode;
                args = new object[] { (ushort)first, new Vector3(intent.X, intent.Y, intent.Z) };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolRemoveNode)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = active.RemoveNode;
                args = new object[] { (ushort)first };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolUnionNodes)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !nodeIds.TryGetNative(intent.SecondaryTarget, out second) ||
                    !UShort(first) || !UShort(second)) return false;
                method = active.UnionNodes;
                args = new object[] { (ushort)first, (ushort)second };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolSplitNode)
            {
                if (!nodeIds.TryGetNative(intent.Target, out first) || !UShort(first)) return false;
                method = active.SplitNode;
                object selections = BuildSegmentSelectionArray(active, intent.RelatedTargets, segmentIds);
                if (selections == null) return false;
                args = new object[] { (ushort)first, new Vector3(intent.X, intent.Y, intent.Z), selections };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolIntersectSegments)
            {
                if (!segmentIds.TryGetNative(intent.Target, out first) || !segmentIds.TryGetNative(intent.SecondaryTarget, out second) ||
                    !UShort(first) || !UShort(second)) return false;
                method = active.IntersectSegments;
                args = new object[] { (ushort)first, (ushort)second };
            }
            else if (intent.Kind == NetIntentKindV2.MultitoolCreateParallel)
            {
                method = active.CreateParallel;
                NetInfo info = NetGameAccess.ResolvePrefab(intent.PrefabKey);
                object points = BuildPointArray(active, intent.SemanticPoints);
                if (points == null) return false;
                args = new object[] { points, intent.Invert, info, HostConstructionCost(active, points, info) };
            }
            else
            {
                if (!segmentIds.TryGetNative(intent.Target, out first) || !segmentIds.TryGetNative(intent.SecondaryTarget, out second) ||
                    !UShort(first) || !UShort(second)) return false;
                method = active.CreateConnection;
                NetInfo info = NetGameAccess.ResolvePrefab(intent.PrefabKey);
                object points = BuildPointArray(active, intent.SemanticPoints);
                if (points == null) return false;
                args = new object[] { points, intent.Invert, (ushort)first, (ushort)second, intent.FirstStart,
                    intent.SecondStart, info, intent.FollowTerrain, HostConstructionCost(active, points, info) };
            }

            object result = method.Invoke(null, args);
            return method.ReturnType == typeof(void) || result is bool && (bool)result;
        }

        private static object BuildPointArray(Surface active, NetMultitoolPointV2[] values)
        {
            if (active == null || values == null || values.Length < 2 || values.Length > 512) return null;
            Array result = Array.CreateInstance(active.PointType, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                NetMultitoolPointV2 p = values[i];
                result.SetValue(active.PointConstructor.Invoke(new object[] {
                    new Vector3(p.X, p.Y, p.Z),
                    new Vector3(p.ForwardX, p.ForwardY, p.ForwardZ),
                    new Vector3(p.BackwardX, p.BackwardY, p.BackwardZ)
                }), i);
            }
            return result;
        }

        private static int HostConstructionCost(Surface active, object points, NetInfo info)
        {
            if (active == null || points == null || info == null) throw new ArgumentNullException("Multitool cost input.");
            object saved = active.NeedMoney.GetValue(null, null);
            if (saved == null) throw new MissingMemberException("NetworkMultitool.Settings.NeedMoney");
            if (!(bool)active.NeedMoneyValue.GetValue(saved, null)) return 0;
            return (int)active.GetCost.Invoke(null, new object[] { points, info });
        }

        private static object BuildSegmentSelectionArray(Surface active, EntityIdentityV2[] values, EntityIdMapV2 segmentIds)
        {
            if (active == null || values == null || values.Length == 0) return null;
            Array array = Array.CreateInstance(active.SelectionType, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                uint native;
                if (!segmentIds.TryGetNative(values[i], out native) || !UShort(native)) return null;
                array.SetValue(active.SegmentSelectionConstructor.Invoke(new object[] { (ushort)native }), i);
            }
            return array;
        }

        private static bool UShort(uint value) { return value != 0 && value <= ushort.MaxValue; }

        private static Surface ResolveSurface()
        {
            Type modType = ResolveType(ModTypeName);
            if (modType == null) throw new MissingMemberException(ModTypeName);
            Assembly assembly = modType.Assembly;
            AssemblyName name = assembly.GetName();
            if (!StringComparer.Ordinal.Equals(name.Name, "NetworkMultitool") || name.Version == null || !name.Version.Equals(SupportedVersion))
                throw new NotSupportedException("Network Multitool assembly must be exact Stable 1.3.9.0.");

            Surface result = new Surface();
            result.PointType = ResolveRequiredType(assembly, "NetworkMultitool.BaseNetworkMultitoolMode+Point");
            result.PointArrayType = result.PointType.MakeArrayType();
            result.SelectionType = ResolveRequiredType(assembly, "ModsCommon.Utilities.Selection");
            Type segmentSelection = ResolveRequiredType(assembly, "ModsCommon.Utilities.SegmentSelection");
            if (!result.SelectionType.IsAssignableFrom(segmentSelection))
                throw new MissingMemberException("ModsCommon.Utilities.SegmentSelection : Selection");
            result.PointConstructor = ResolveConstructor(result.PointType, typeof(Vector3), typeof(Vector3), typeof(Vector3));
            result.SegmentSelectionConstructor = ResolveConstructor(segmentSelection, typeof(ushort));
            result.PointPosition = ResolveField(result.PointType, "Position", typeof(Vector3));
            result.PointForward = ResolveField(result.PointType, "ForwardDirection", typeof(Vector3));
            result.PointBackward = ResolveField(result.PointType, "BackwardDirection", typeof(Vector3));

            Type settings = ResolveRequiredType(assembly, "NetworkMultitool.Settings");
            result.NeedMoney = settings.GetProperty("NeedMoney", BindingFlags.Static | BindingFlags.Public);
            if (result.NeedMoney == null) throw new MissingMemberException("NetworkMultitool.Settings.NeedMoney");
            result.NeedMoneyValue = result.NeedMoney.PropertyType.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            if (result.NeedMoneyValue == null || result.NeedMoneyValue.PropertyType != typeof(bool))
                throw new MissingMemberException("NetworkMultitool.Settings.NeedMoney.value");

            Type baseMode = ResolveRequiredType(assembly, "NetworkMultitool.BaseNetworkMultitoolMode");
            result.GetCost = ResolveRequired(baseMode, "GetCost", typeof(int), result.PointArrayType, typeof(NetInfo));
            result.AddNode = ResolveRequired(assembly, "NetworkMultitool.AddNodeMode", "InsertNode", typeof(bool), typeof(ushort), typeof(Vector3));
            result.RemoveNode = ResolveRequired(assembly, "NetworkMultitool.RemoveNodeMode", "RemoveNode", typeof(bool), typeof(ushort));
            result.UnionNodes = ResolveRequired(assembly, "NetworkMultitool.UnionNodeMode", "Union", typeof(bool), typeof(ushort), typeof(ushort));
            Type selections = typeof(IEnumerable<>).MakeGenericType(result.SelectionType);
            result.SplitNode = ResolveRequired(assembly, "NetworkMultitool.SplitNodeMode", "Split", typeof(bool), typeof(ushort), typeof(Vector3), selections);
            result.IntersectSegments = ResolveRequired(assembly, "NetworkMultitool.IntersectSegmentMode", "IntersectSegments", typeof(bool), typeof(ushort), typeof(ushort));
            result.CreateParallel = ResolveRequired(assembly, "NetworkMultitool.CreateParallelMode", "Create", typeof(void),
                result.PointArrayType, typeof(bool), typeof(NetInfo), typeof(int));
            result.CreateConnection = ResolveRequired(assembly, "NetworkMultitool.BaseCreateMode", "Create", typeof(void),
                result.PointArrayType, typeof(bool), typeof(ushort), typeof(ushort), typeof(bool), typeof(bool), typeof(NetInfo), typeof(bool), typeof(int));
            return result;
        }

        private static Type ResolveRequiredType(Assembly assembly, string typeName)
        {
            Type type = assembly == null ? null : assembly.GetType(typeName, false);
            if (type == null) throw new MissingMemberException(typeName);
            return type;
        }

        private static ConstructorInfo ResolveConstructor(Type type, params Type[] parameterTypes)
        {
            ConstructorInfo constructor = type.GetConstructor(parameterTypes);
            if (constructor == null) throw new MissingMethodException(type.FullName, ".ctor");
            return constructor;
        }

        private static FieldInfo ResolveField(Type type, string fieldName, Type fieldType)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, fieldName);
            return field;
        }

        private static MethodInfo ResolveRequired(Assembly assembly, string typeName, string methodName, Type returnType, params Type[] parameterTypes)
        {
            return ResolveRequired(ResolveRequiredType(assembly, typeName), methodName, returnType, parameterTypes);
        }

        private static MethodInfo ResolveRequired(Type type, string methodName, Type returnType, params Type[] parameterTypes)
        {
            MethodInfo found = null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != methodName || candidate.ReturnType != returnType || !ParametersMatch(candidate, parameterTypes)) continue;
                if (found != null) throw new AmbiguousMatchException(type.FullName + "." + methodName);
                found = candidate;
            }
            if (found == null) throw new MissingMethodException(type.FullName, methodName);
            return found;
        }

        private static bool ParametersMatch(MethodInfo method, Type[] expected)
        {
            ParameterInfo[] actual = method.GetParameters();
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i].ParameterType != expected[i]) return false;
            return true;
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
