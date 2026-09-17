using System;
using System.Reflection;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class KnownModBridgeRegistry
    {
        private static readonly object Gate = new object();
        private static bool demandControllerPatched;

        internal static void RegisterAvailable()
        {
            lock (Gate)
            {
                if (DemandControllerBridge.IsAvailable && !Registered(DemandControllerBridgeAdapter.Adapter))
                    ForgeExtensionApi.Register(new DemandControllerBridgeAdapter());
                if (GameAnarchyBridge.IsAvailable && !Registered(GameAnarchyBridgeAdapter.Adapter))
                    ForgeExtensionApi.Register(new GameAnarchyBridgeAdapter());
                if (InfiniteGoodsBridge.IsAvailable)
                {
                    if (!Registered(InfiniteGoodsBridgeAdapter.Adapter))
                        ForgeExtensionApi.Register(new InfiniteGoodsBridgeAdapter());
                    if (!Registered(InfiniteGoodsBuildingBufferAdapter.Adapter))
                        ForgeExtensionApi.Register(new InfiniteGoodsBuildingBufferAdapter());
                }
                if (EightyOne2Bridge.IsAvailable && !Registered(EightyOne2BridgeAdapter.Adapter))
                    ForgeExtensionApi.Register(new EightyOne2BridgeAdapter());
            }
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            lock (Gate)
            {
                MethodInfo refresh = DemandControllerBridge.ResolveRefresh();
                if (refresh != null && !demandControllerPatched)
                {
                    MethodInfo prefix = typeof(KnownModBridgeRegistry).GetMethod("DemandControllerRefreshPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefix == null) throw new MissingMethodException("Demand Controller bridge prefix is unavailable.");
                    harmony.Patch(refresh, new HarmonyMethod(prefix));
                    demandControllerPatched = true;
                }
                GameAnarchyBridge.InstallOptionalPatches(harmony);
                InfiniteGoodsBridge.InstallOptionalPatches(harmony);
                EightyOne2Bridge.InstallOptionalPatches(harmony);
            }
        }

        internal static void ResetOptionalPatchState()
        {
            lock (Gate)
            {
                demandControllerPatched = false;
                GameAnarchyBridge.ResetPatchState();
                InfiniteGoodsBridge.ResetPatchState();
                EightyOne2Bridge.ResetPatchState();
            }
        }

        private static bool Registered(string adapterId)
        {
            string[] values = ForgeExtensionApi.RegisteredAdapterIds;
            for (int i = 0; i < values.Length; i++)
                if (StringComparer.Ordinal.Equals(values[i], adapterId)) return true;
            return false;
        }

        private static bool DemandControllerRefreshPrefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                role == CitiesRuntimeRole.ClientReplicaLive) return false;
            return role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer ||
                role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive ||
                role == CitiesRuntimeRole.Unloading;
        }
    }
}
