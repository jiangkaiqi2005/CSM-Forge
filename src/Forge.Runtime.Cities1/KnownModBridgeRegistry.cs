using CsmForge.Core;
using System;
using System.Reflection;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class KnownModBridgeDescriptor
    {
        private readonly Action registerAdapters;
        private readonly Action<Harmony> installPatches;
        private readonly Action reset;

        public string UserModTypeName { get; private set; }

        public KnownModBridgeDescriptor(string userModTypeName, Action register, Action<Harmony> install, Action resetState)
        {
            Check.Condition(string.IsNullOrEmpty(userModTypeName), "userModTypeName", "Known Mod type is missing.");
            UserModTypeName = userModTypeName;
            registerAdapters = register;
            installPatches = install;
            reset = resetState;
        }

        public void RegisterIfActive(EnabledPluginCatalog catalog)
        {
            if (catalog != null && catalog.ContainsUserMod(UserModTypeName) && registerAdapters != null) registerAdapters();
        }

        public void InstallIfActive(EnabledPluginCatalog catalog, Harmony harmony)
        {
            if (catalog != null && catalog.ContainsUserMod(UserModTypeName) && installPatches != null) installPatches(harmony);
        }

        public void Reset() { if (reset != null) reset(); }
    }

    internal static class KnownModBridgeRegistry
    {
        private static readonly object Gate = new object();
        private static bool demandControllerPatched;
        private static readonly KnownModBridgeDescriptor[] Bridges =
        {
            new KnownModBridgeDescriptor(ModCompatibilityCatalog.Default.DemandControllerUserModType, RegisterDemandController, InstallDemandController, ResetDemandController),
            new KnownModBridgeDescriptor(ModCompatibilityCatalog.Default.GameAnarchyUserModType, RegisterGameAnarchy, GameAnarchyBridge.InstallOptionalPatches, GameAnarchyBridge.ResetPatchState),
            new KnownModBridgeDescriptor(ModCompatibilityCatalog.Default.InfiniteGoodsUserModType, RegisterInfiniteGoods, InfiniteGoodsBridge.InstallOptionalPatches, InfiniteGoodsBridge.ResetPatchState),
            new KnownModBridgeDescriptor(ModCompatibilityCatalog.Default.EightyOne2UserModType, RegisterEightyOne, EightyOne2Bridge.InstallOptionalPatches, EightyOne2Bridge.ResetPatchState),
            new KnownModBridgeDescriptor(ModCompatibilityCatalog.Default.NetworkMultitoolUserModType, null, NetworkMultitoolBridge.InstallOptionalPatches, NetworkMultitoolBridge.ResetPatchState)
        };

        internal static void RegisterAvailable()
        {
            EnabledPluginCatalog enabled = EnabledPluginCatalog.Capture();
            lock (Gate)
            {
                if (!Registered(TreeStateAdapter.Adapter)) ForgeExtensionApi.Register(new TreeStateAdapter());
                if (!Registered(PropStateAdapter.Adapter)) ForgeExtensionApi.Register(new PropStateAdapter());
                for (int i = 0; i < Bridges.Length; i++) Bridges[i].RegisterIfActive(enabled);
                // TM:PE remains a blocked-mod.  Keep its audited bridge code dormant until the
                // UI-write audit and real multi-machine validation promote it to Supported.
                // A blocked mod must not be patched or registered merely because its assembly is
                // present: doing so mutates TM:PE's live simulation path even in single-player.
            }
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            Check.NotNull(harmony, "harmony");
            EnabledPluginCatalog enabled = EnabledPluginCatalog.Capture();
            lock (Gate)
            {
                for (int i = 0; i < Bridges.Length; i++) Bridges[i].InstallIfActive(enabled, harmony);
                // TM:PE is intentionally not patched while its compatibility kind is blocked-mod.
            }
        }

        internal static void ResetOptionalPatchState()
        {
            lock (Gate)
            {
                for (int i = 0; i < Bridges.Length; i++) Bridges[i].Reset();
            }
        }

        private static void RegisterDemandController()
        {
            if (DemandControllerBridge.IsAvailable && !Registered(DemandControllerBridgeAdapter.Adapter))
                ForgeExtensionApi.Register(new DemandControllerBridgeAdapter());
        }

        private static void RegisterGameAnarchy()
        {
            if (GameAnarchyBridge.IsAvailable && !Registered(GameAnarchyBridgeAdapter.Adapter))
                ForgeExtensionApi.Register(new GameAnarchyBridgeAdapter());
        }

        private static void RegisterInfiniteGoods()
        {
            if (!InfiniteGoodsBridge.IsAvailable) return;
            if (!Registered(InfiniteGoodsBridgeAdapter.Adapter)) ForgeExtensionApi.Register(new InfiniteGoodsBridgeAdapter());
            if (!Registered(InfiniteGoodsBuildingBufferAdapter.Adapter)) ForgeExtensionApi.Register(new InfiniteGoodsBuildingBufferAdapter());
        }

        private static void RegisterEightyOne()
        {
            if (!EightyOne2Bridge.IsAvailable) return;
            if (!Registered(EightyOne2BridgeAdapter.Adapter)) ForgeExtensionApi.Register(new EightyOne2BridgeAdapter());
            if (!Registered(EightyOne2ElectricityGridAdapter.Adapter)) ForgeExtensionApi.Register(new EightyOne2ElectricityGridAdapter());
            if (!Registered(EightyOne2WaterGridAdapter.Adapter)) ForgeExtensionApi.Register(new EightyOne2WaterGridAdapter());
        }

        private static void InstallDemandController(Harmony harmony)
        {
            MethodInfo refresh = DemandControllerBridge.ResolveRefresh();
            if (refresh == null || demandControllerPatched) return;
            MethodInfo prefix = typeof(KnownModBridgeRegistry).GetMethod("DemandControllerRefreshPrefix",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null) throw new MissingMethodException("Demand Controller bridge prefix is unavailable.");
            harmony.Patch(refresh, new HarmonyMethod(prefix));
            demandControllerPatched = true;
        }

        private static void ResetDemandController() { demandControllerPatched = false; }

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
