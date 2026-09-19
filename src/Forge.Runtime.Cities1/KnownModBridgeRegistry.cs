using System;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
using HarmonyLib;
using ICities;

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
                if (!Registered(TreeStateAdapter.Adapter)) ForgeExtensionApi.Register(new TreeStateAdapter());
                if (!Registered(PropStateAdapter.Adapter)) ForgeExtensionApi.Register(new PropStateAdapter());
                if (IsEnabled("DemandController.DemandController") && DemandControllerBridge.IsAvailable &&
                    !Registered(DemandControllerBridgeAdapter.Adapter))
                    ForgeExtensionApi.Register(new DemandControllerBridgeAdapter());
                if (IsEnabled("GameAnarchy.Mod") && GameAnarchyBridge.IsAvailable && !Registered(GameAnarchyBridgeAdapter.Adapter))
                    ForgeExtensionApi.Register(new GameAnarchyBridgeAdapter());
                if (IsEnabled("InfiniteGoodsMod.ModIdentity") && InfiniteGoodsBridge.IsAvailable)
                {
                    if (!Registered(InfiniteGoodsBridgeAdapter.Adapter))
                        ForgeExtensionApi.Register(new InfiniteGoodsBridgeAdapter());
                    if (!Registered(InfiniteGoodsBuildingBufferAdapter.Adapter))
                        ForgeExtensionApi.Register(new InfiniteGoodsBuildingBufferAdapter());
                }
                if (IsEnabled("EightyOne2.Mod") && EightyOne2Bridge.IsAvailable)
                {
                    if (!Registered(EightyOne2BridgeAdapter.Adapter))
                        ForgeExtensionApi.Register(new EightyOne2BridgeAdapter());
                    if (!Registered(EightyOne2ElectricityGridAdapter.Adapter))
                        ForgeExtensionApi.Register(new EightyOne2ElectricityGridAdapter());
                    if (!Registered(EightyOne2WaterGridAdapter.Adapter))
                        ForgeExtensionApi.Register(new EightyOne2WaterGridAdapter());
                }
                // TM:PE remains a blocked-mod.  Keep its audited bridge code dormant until the
                // UI-write audit and real multi-machine validation promote it to Supported.
                // A blocked mod must not be patched or registered merely because its assembly is
                // present: doing so mutates TM:PE's live simulation path even in single-player.
            }
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            lock (Gate)
            {
                MethodInfo refresh = IsEnabled("DemandController.DemandController")
                    ? DemandControllerBridge.ResolveRefresh() : null;
                if (refresh != null && !demandControllerPatched)
                {
                    MethodInfo prefix = typeof(KnownModBridgeRegistry).GetMethod("DemandControllerRefreshPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefix == null) throw new MissingMethodException("Demand Controller bridge prefix is unavailable.");
                    harmony.Patch(refresh, new HarmonyMethod(prefix));
                    demandControllerPatched = true;
                }
                if (IsEnabled("GameAnarchy.Mod")) GameAnarchyBridge.InstallOptionalPatches(harmony);
                if (IsEnabled("InfiniteGoodsMod.ModIdentity")) InfiniteGoodsBridge.InstallOptionalPatches(harmony);
                if (IsEnabled("EightyOne2.Mod")) EightyOne2Bridge.InstallOptionalPatches(harmony);
                if (IsEnabled("NetworkMultitool.Mod")) NetworkMultitoolBridge.InstallOptionalPatches(harmony);
                // TM:PE is intentionally not patched while its compatibility kind is blocked-mod.
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
                NetworkMultitoolBridge.ResetPatchState();
            }
        }

        private static bool Registered(string adapterId)
        {
            string[] values = ForgeExtensionApi.RegisteredAdapterIds;
            for (int i = 0; i < values.Length; i++)
                if (StringComparer.Ordinal.Equals(values[i], adapterId)) return true;
            return false;
        }

        private static bool IsEnabled(string userModTypeName)
        {
            PluginManager manager = Singleton<PluginManager>.instance;
            if (manager == null) return false;
            foreach (PluginManager.PluginInfo plugin in manager.GetPluginsInfo())
            {
                if (plugin == null || !plugin.isEnabled) continue;
                IUserMod userMod = plugin.userModInstance as IUserMod;
                if (userMod != null && StringComparer.Ordinal.Equals(userMod.GetType().FullName, userModTypeName))
                    return true;
            }
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
