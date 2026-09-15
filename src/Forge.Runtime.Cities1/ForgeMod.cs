using System;
using System.Reflection;
using System.Threading;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public sealed class ForgeMod : IUserMod
    {
        public static readonly ForgeSettings Settings = new ForgeSettings();

        public string Name { get { return "CSM-Forge V3"; } }
        public string Description { get { return "Host-authoritative Cities: Skylines multiplayer runtime under staged integration."; } }

        public void OnEnabled()
        {
            RuntimeServices.Enable();
            UnityEngine.Debug.Log("[CSM-Forge] runtime enabled; persistent multiplayer writes stay gated until a Forge session is Live.");
        }

        public void OnDisabled()
        {
            RuntimeServices.Disable();
            UnityEngine.Debug.Log("[CSM-Forge] runtime disabled and Forge-owned transport/patch resources released.");
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            ForgeSettingsPanel.Build(helper, Settings);
        }
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
                CompatibilityManifest manifest = CitiesCompatibilityCollector.Collect();
                UnityEngine.Debug.Log("[CSM-Forge] level loaded; world=" + identity.WorldId +
                    "; epoch=" + identity.Epoch + "; generation=" + identity.Generation +
                    "; load=" + mode + "; compatibilityEntries=" + manifest.Entries.Length +
                    "; gameBuild=" + BuildConfig.applicationVersion +
                    "; managedRuntime=" + Environment.Version +
                    "; unity=" + UnityEngine.Application.unityVersion +
                    "; callbackThread=" + Thread.CurrentThread.ManagedThreadId +
                    "; core=" + typeof(SessionStamp).Assembly.GetName().Version);
                ReportEngineSurface(identity);
            }
            catch (Exception error)
            {
                RuntimeServices.Events.Record(RuntimeEventCode.Error, RuntimeServices.Lifecycle.Current.Generation,
                    "level-load: " + error.GetType().Name);
                RuntimeServices.Lifecycle.Fence("level initialization failed");
                UnityEngine.Debug.LogError("[CSM-Forge] level initialization failed: " + error);
            }
        }

        public override void OnLevelUnloading()
        {
            RuntimeServices.Multiplayer.RequestStop();
            RuntimeServices.Lifecycle.BeginUnload();
            RuntimeServices.Metadata.Clear();
            base.OnLevelUnloading();
        }

        public override void OnReleased()
        {
            RuntimeServices.Multiplayer.RequestStop();
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
                {
                    if (method.Name == "FixedUpdate" && method.GetParameters().Length == 0) { fixedUpdate = true; break; }
                }
            }
            UnityEngine.Debug.Log("[CSM-Forge] runtime evidence generation=" + identity.Generation +
                "; SimulationManager=" + (simulation != null) + "; FixedUpdate-surface=" + fixedUpdate +
                "; simulation-isolation=PARTIAL; authority-projection=WATER-BUDGET-SLICE-ONLY.");
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
            LoadIdentity identity = RuntimeServices.Lifecycle.Current;
            if (!identity.IsValid) return;
            RuntimeServices.Multiplayer.PollSimulation();
        }

        public override void OnAfterSimulationTick()
        {
            LoadIdentity identity = RuntimeServices.Lifecycle.Current;
            if (identity.IsValid)
            {
                RuntimeServices.Multiplayer.AfterSimulationTick();
                RuntimeScopeGuard.EndOfSimulationTick(RuntimeServices.Lifecycle, RuntimeServices.Events);
            }
            base.OnAfterSimulationTick();
        }

        public override void OnReleased()
        {
            RuntimeServices.Multiplayer.RequestStop();
            RuntimeServices.Scheduler.Detach();
            base.OnReleased();
        }
    }
}
