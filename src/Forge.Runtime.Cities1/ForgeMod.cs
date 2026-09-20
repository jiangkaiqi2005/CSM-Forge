using System;
using System.Reflection;
using System.IO;
using System.Threading;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public sealed class ForgeMod : IUserMod
    {
        public static readonly ForgeSettings Settings = new ForgeSettings();
        public string Name { get { return "CSM-Forge 1.0 Candidate"; } }
        public string Description { get { return "主菜单加入房间；进入城市后从暂停菜单创建和管理房间。真实多机玩法仍待验证。"; } }
        public void OnEnabled()
        {
            InitializeCompatibilityCatalog();
            BuiltInDlcAdapters.RegisterAll();
            ForgeExtensionApi.Register(new DistrictParkControlsAdapter());
            ForgeExtensionApi.Register(new DistrictParkDeepScalarAdapter());
            ForgeExtensionApi.Register(new CampusDeepStateAdapter());
            ForgeExtensionApi.Register(new EventStateAdapter());
            ForgeExtensionApi.Register(new DisasterStateAdapter());
            ForgeExtensionApi.Register(new ParkGridStateAdapter());
            ForgeExtensionApi.Register(new BuildingSimulationStateAdapter());
            ForgeExtensionApi.Register(new PathUnitStateAdapter());
            ForgeExtensionApi.Register(new VehiclePresentationStateAdapter());
            ForgeExtensionApi.Register(new CitizenInstancePresentationStateAdapter());
            ForgeExtensionApi.Register(new TerrainStateAdapter());
            KnownModBridgeRegistry.RegisterAvailable();
            RuntimeServices.Enable();
            ForgeMultiplayerUi.Initialize();
            UnityEngine.Debug.Log("[CSM-Forge] runtime enabled; builtInAdapters=" + ForgeExtensionApi.RegisteredAdapterIds.Length + ".");
        }
        public void OnDisabled() { ForgeMultiplayerUi.Shutdown(); RuntimeServices.Disable(); UnityEngine.Debug.Log("[CSM-Forge] runtime disabled."); }

        /// <summary>WP-3.2: external compat documents replace the built-in set wholesale; any
        /// failure leaves the test-pinned built-in set in force (fail closed).</summary>
        private static void InitializeCompatibilityCatalog()
        {
            try
            {
                string location = typeof(ForgeMod).Assembly.Location;
                string compatDirectory = string.IsNullOrEmpty(location)
                    ? null : Path.Combine(Path.GetDirectoryName(location), "compat");
                bool loaded = ModCompatibilityCatalog.TryInitializeFromDirectory(compatDirectory);
                UnityEngine.Debug.Log("[CSM-Forge] compatibility catalog " +
                    (loaded ? "loaded from compat/." : "built-in (no external document loaded)."));
            }
            catch (Exception error)
            {
                UnityEngine.Debug.Log("[CSM-Forge] compatibility catalog init failed: " +
                    error.GetType().Name + "; built-in set in force.");
            }
        }
        public void OnSettingsUI(UIHelperBase helper) { ForgeSettingsPanel.Build(helper, Settings); }
    }

    public sealed class ForgeLoadingExtension : LoadingExtensionBase
    {
        public override void OnCreated(ILoading loading)
        {
            base.OnCreated(loading);
            RuntimeServices.Lifecycle.LoadingCreated();
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            try
            {
                LoadIdentity identity = RuntimeServices.Lifecycle.LevelLoaded(mode,
                    RuntimeServices.Metadata.PendingWorldId, RuntimeServices.Metadata.PendingEpoch);
                RuntimeServices.Metadata.Attach(identity);
                KnownModBridgeRegistry.RegisterAvailable();
                RuntimeServices.Patches.RefreshOptionalBridges();
                ForgeMultiplayerUi.EnsurePauseMenuEntry();
                CompatibilityManifest manifest = CitiesCompatibilityCollector.Collect();
                UnityEngine.Debug.Log("[CSM-Forge] level loaded; world=" + identity.WorldId +
                    "; epoch=" + identity.Epoch + "; generation=" + identity.Generation +
                    "; load=" + mode + "; compatibilityEntries=" + manifest.Entries.Length +
                    "; adapters=" + ForgeExtensionApi.RegisteredAdapterIds.Length +
                    "; gameBuild=" + BuildConfig.applicationVersion +
                    "; managedRuntime=" + Environment.Version +
                    "; unity=" + UnityEngine.Application.unityVersion +
                    "; callbackThread=" + Thread.CurrentThread.ManagedThreadId +
                    "; core=" + typeof(SessionStamp).Assembly.GetName().Version);
                ReportEngineSurface(identity);
                RuntimeServices.WorldLoader.NotifyLevelLoaded(identity);
            }
            catch (Exception error)
            {
                RuntimeServices.Events.Record(RuntimeEventCode.Error, RuntimeServices.Lifecycle.Current.Generation,
                    "level-load:" + error.GetType().Name);
                RuntimeServices.Lifecycle.Fence("level initialization failed");
                UnityEngine.Debug.LogError("[CSM-Forge] level initialization failed: " + error);
            }
        }

        public override void OnLevelUnloading()
        {
            if (!RuntimeServices.Multiplayer.PreserveAcrossLevelLoad)
                RuntimeServices.Multiplayer.StopImmediately();
            RuntimeServices.Lifecycle.BeginUnload();
            RuntimeServices.Metadata.Clear();
            base.OnLevelUnloading();
        }

        public override void OnReleased()
        {
            if (!RuntimeServices.Multiplayer.PreserveAcrossLevelLoad)
                RuntimeServices.Multiplayer.StopImmediately();
            RuntimeServices.Lifecycle.Released();
            base.OnReleased();
        }

        private static void ReportEngineSurface(LoadIdentity identity)
        {
            Type simulation = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                simulation = assembly.GetType("SimulationManager", false);
                if (simulation != null) break;
            }
            bool fixedUpdate = false;
            if (simulation != null)
            {
                foreach (MethodInfo method in simulation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "FixedUpdate" && method.GetParameters().Length == 0) { fixedUpdate = true; break; }
            }
            UnityEngine.Debug.Log("[CSM-Forge] runtime evidence generation=" + identity.Generation +
                "; SimulationManager=" + (simulation != null) + "; FixedUpdate-surface=" + fixedUpdate +
                "; simulation-isolation=PARTIAL; authority-projection=WATER-DEMAND-TAX-BUDGET-CASH-LOAN-AREA-BUILDING-ROAD-ZONE-DISTRICT-POLICY-CLOCK-TRANSPORT-NAME-CITYNAME-WEATHER-EXTENSION-DISTRICTPARK-PARKGRID-DISTRICTPARKCONTROLS-DISTRICTPARKDEEP-CAMPUSDEEP-EVENTS-DISASTERS-KNOWNMODBRIDGES.");
        }
    }

    public sealed class ForgeThreadingExtension : ThreadingExtensionBase
    {
        public override void OnCreated(IThreading threading)
        {
            base.OnCreated(threading);
            RuntimeServices.Scheduler.Attach(threading);
        }
        public override void OnBeforeSimulationTick()
        {
            base.OnBeforeSimulationTick();
            if (RuntimeServices.Lifecycle.Current.IsValid) RuntimeServices.Multiplayer.PollSimulation();
        }
        private PerfWindow perf;
        private long lastTickTicks;

        public override void OnAfterSimulationTick()
        {
            LoadIdentity identity = RuntimeServices.Lifecycle.Current;
            if (identity.IsValid)
            {
                // WP-1.5 instrumentation: the host runs ~11 verification polls per tick, so lag
                // needs per-phase numbers rather than guesses. Cheap: one Stopwatch read per
                // phase, one log line per window.
                long frequency = System.Diagnostics.Stopwatch.Frequency;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (perf == null) perf = new PerfWindow(now, frequency, 10000);
                if (lastTickTicks != 0) perf.RecordFrame(now - lastTickTicks);
                lastTickTicks = now;

                RunPoll("demand", delegate { RuntimeServices.Multiplayer.PollObservedHostDemand(); });
                RunPoll("zones", delegate { RuntimeServices.Multiplayer.PollObservedHostZones(); });
                RunPoll("districts", delegate { RuntimeServices.Multiplayer.PollObservedHostDistricts(); });
                RunPoll("transport", delegate { RuntimeServices.Multiplayer.PollObservedHostTransportLines(); });
                RunPoll("economy", delegate { RuntimeServices.Multiplayer.PollObservedHostEconomyControl(); });
                RunPoll("cash", delegate { RuntimeServices.Multiplayer.PollObservedHostCash(); });
                RunPoll("areas", delegate { RuntimeServices.Multiplayer.PollObservedHostAreas(); });
                RunPoll("weather", delegate { RuntimeServices.Multiplayer.PollObservedHostWeather(); });
                RunPoll("buildings", delegate { RuntimeServices.Multiplayer.PollObservedHostBuildings(); });
                RunPoll("extensions", delegate { RuntimeServices.Multiplayer.PollObservedHostExtensions(); });
                RunPoll("net-full", delegate { RuntimeServices.Multiplayer.PollObservedHostNetFull(); });
                RunPoll("e81-restore", delegate { EightyOne2UtilityAuthority.RestoreClientProjection(); });
                RunPoll("weather-restore", delegate { RuntimeServices.Multiplayer.RestoreClientWeatherTargets(); });
                RunPoll("projection-audit", delegate { RuntimeServices.Multiplayer.AuditClientProjection(); });
                RunPoll("after-tick", delegate { RuntimeServices.Multiplayer.AfterSimulationTick(); });
                RunPoll("tick-guard", delegate { RuntimeScopeGuard.EndOfSimulationTick(RuntimeServices.Lifecycle, RuntimeServices.Events); });

                if (perf.ShouldFlush(now))
                {
                    string report = perf.Flush(now);
                    if (!string.IsNullOrEmpty(report))
                        UnityEngine.Debug.Log("[CSM-Forge][PERF] " + report);
                }
            }
            base.OnAfterSimulationTick();
        }

        private void RunPoll(string phase, Action action)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { action(); }
            finally
            {
                if (perf != null) perf.Record(phase, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }
        }
        public override void OnReleased()
        {
            if (!RuntimeServices.Multiplayer.PreserveAcrossLevelLoad)
                RuntimeServices.Multiplayer.StopImmediately();
            RuntimeServices.Scheduler.Detach();
            base.OnReleased();
        }
    }
}
