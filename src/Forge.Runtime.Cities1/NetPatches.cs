using System;
using System.Reflection;
using ColossalFramework;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class NetToolAuthorityState
    {
        public bool Active;
        public Hash256 BeforeRoot;
        public EconomySideEffectScope Economy;

        public void DisposeEconomy()
        {
            if (Economy == null) return;
            Economy.Dispose();
            Economy = null;
        }
    }

    [HarmonyPatch]
    internal static class NetToolCreateNodeAuthorityPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = typeof(NetTool).GetMethod("CreateNode", new Type[]
            {
                typeof(NetInfo), typeof(NetTool.ControlPoint), typeof(NetTool.ControlPoint), typeof(NetTool.ControlPoint),
                typeof(FastList<>).MakeGenericType(typeof(NetTool.NodePosition)), typeof(int),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(ushort),
                typeof(ushort).MakeByRefType(), typeof(ushort).MakeByRefType(), typeof(ushort).MakeByRefType(),
                typeof(int).MakeByRefType(), typeof(int).MakeByRefType()
            });
            if (method == null) throw new MissingMethodException("NetTool.CreateNode authoritative signature is unavailable.");
            return method;
        }

        public static bool Prefix(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint,
            NetTool.ControlPoint endPoint, int maxSegments, bool test, bool testEnds, bool visualize,
            bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID,
            ref ushort firstNode, ref ushort lastNode, ref ushort segmentID, ref int cost, ref int productionRate,
            ref ToolBase.ToolErrors __result, out NetToolAuthorityState __state)
        {
            __state = null;
            if (RuntimeScopeGuard.IsApplying) return true;

            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (test || visualize) return true;

            bool road = info != null && info.m_class != null && info.m_class.m_service == ItemClass.Service.Road;
            if (!road || relocateBuildingID != 0)
            {
                firstNode = lastNode = segmentID = 0; cost = productionRate = 0;
                __result = ToolBase.ToolErrors.ObjectCollision;
                return false;
            }

            if (role == CitiesRuntimeRole.ClientReplicaLive)
            {
                NetControlPointV2 start = null;
                NetControlPointV2 middle = null;
                NetControlPointV2 end = null;
                bool valid = TryEncodePoint(startPoint, out start);
                if (valid) valid = TryEncodePoint(middlePoint, out middle);
                if (valid) valid = TryEncodePoint(endPoint, out end);
                bool queued = valid && RuntimeServices.Multiplayer.TrySubmitNetIntent(NetIntentV2.Create(info.name,
                    start, middle, end, maxSegments, testEnds, autoFix, invert, switchDir, (uint)NetTool.m_zoneGridFlags));
                firstNode = lastNode = segmentID = 0; cost = productionRate = 0;
                __result = queued ? ToolBase.ToolErrors.None : ToolBase.ToolErrors.ObjectCollision;
                if (!queued) RuntimeServices.Lifecycle.Fence("Road player intent could not be queued");
                return false;
            }

            if (role == CitiesRuntimeRole.HostLive)
            {
                Hash256 before = RuntimeServices.Multiplayer.CaptureHostNetRoot();
                if (before == null)
                {
                    __result = ToolBase.ToolErrors.ObjectCollision;
                    return false;
                }
                __state = new NetToolAuthorityState
                {
                    Active = true,
                    BeforeRoot = before,
                    Economy = EconomySideEffectCapture.Begin()
                };
                return true;
            }

            firstNode = lastNode = segmentID = 0; cost = productionRate = 0;
            __result = ToolBase.ToolErrors.ObjectCollision;
            return false;
        }

        public static void Postfix(ToolBase.ToolErrors __result, NetToolAuthorityState __state)
        {
            if (__state == null || !__state.Active) return;
            int construction = __state.Economy == null ? 0 : __state.Economy.ConstructionFetched;
            int refund = __state.Economy == null ? 0 : __state.Economy.RefundAdded;
            __state.DisposeEconomy();
            RuntimeServices.Multiplayer.PublishObservedHostNet(__state.BeforeRoot, construction, refund);
        }

        public static Exception Finalizer(NetToolAuthorityState __state, Exception __exception)
        {
            if (__state != null) __state.DisposeEconomy();
            return __exception;
        }

        private static bool TryEncodePoint(NetTool.ControlPoint source, out NetControlPointV2 point)
        {
            point = null;
            EntityIdentityV2 node = default(EntityIdentityV2), segment = default(EntityIdentityV2);
            if (source.m_node != 0 && !RuntimeServices.Multiplayer.TryResolveClientNetNode(source.m_node, out node)) return false;
            if (source.m_segment != 0 && !RuntimeServices.Multiplayer.TryResolveClientNetSegment(source.m_segment, out segment)) return false;
            point = new NetControlPointV2(source.m_position.x, source.m_position.y, source.m_position.z,
                source.m_direction.x, source.m_direction.y, source.m_direction.z, source.m_elevation,
                node, segment, source.m_outside);
            return true;
        }
    }

    [HarmonyPatch]
    internal static class ClientNetManagerCreateNodeBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = typeof(NetManager).GetMethod("CreateNode", new Type[]
            {
                typeof(ushort).MakeByRefType(), typeof(ColossalFramework.Math.Randomizer).MakeByRefType(),
                typeof(NetInfo), typeof(UnityEngine.Vector3), typeof(uint)
            });
            if (method == null) throw new MissingMethodException("NetManager.CreateNode signature is unavailable.");
            return method;
        }

        public static bool Prefix(ref ushort __0, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                role == CitiesRuntimeRole.ClientReplicaLive)
            {
                __0 = 0; __result = false; return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class ClientNetManagerCreateSegmentBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = typeof(NetManager).GetMethod("CreateSegment", new Type[]
            {
                typeof(ushort).MakeByRefType(), typeof(ColossalFramework.Math.Randomizer).MakeByRefType(), typeof(NetInfo),
                typeof(ushort), typeof(ushort), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector3),
                typeof(uint), typeof(uint), typeof(bool)
            });
            if (method == null) throw new MissingMethodException("NetManager.CreateSegment signature is unavailable.");
            return method;
        }

        public static bool Prefix(ref ushort __0, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                role == CitiesRuntimeRole.ClientReplicaLive)
            {
                __0 = 0; __result = false; return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class ClientNetManagerReleaseSegmentBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = typeof(NetManager).GetMethod("ReleaseSegment", new Type[] { typeof(ushort), typeof(bool) });
            if (method == null) throw new MissingMethodException("NetManager.ReleaseSegment signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                role != CitiesRuntimeRole.ClientReplicaLive;
        }
    }

    [HarmonyPatch]
    internal static class ClientNetManagerReleaseNodeBarrierPatch
    {
        public static MethodBase TargetMethod()
        {
            MethodInfo method = typeof(NetManager).GetMethod("ReleaseNode", new Type[] { typeof(ushort) });
            if (method == null) throw new MissingMethodException("NetManager.ReleaseNode signature is unavailable.");
            return method;
        }

        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                role != CitiesRuntimeRole.ClientReplicaLive;
        }
    }
}
