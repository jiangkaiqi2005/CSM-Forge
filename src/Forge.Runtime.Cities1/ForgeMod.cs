using System;
using System.Reflection;
using System.Threading;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public sealed class ForgeMod : IUserMod
    {
        public string Name { get { return "CSM-Forge V3"; } }
        public string Description
        {
            get { return "Host-authoritative Cities: Skylines multiplayer runtime under staged integration."; }
        }

        public void OnEnabled()
        {
            RuntimeServices.Enable();
            UnityEngine.Debug.Log("[CSM-Forge] runtime enabled; multiplayer write access remains gated by session/runtime acceptance.");
        }

        public void OnDisabled()
        {
            RuntimeServices.Disable();
            UnityEngine.Debug.Log("[CSM-Forge] runtime disabled and Forge-owned patches/resources released.");
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
            RuntimeServices.Lifecycle.BeginUnload();
            RuntimeServices.Metadata.Clear();
            base.OnLevelUnloading();
        }

        public override void OnReleased()
        {
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
                    if (method.Name == "FixedUpdate" && method.GetParameters().Length == 0)
                    {
                        fixedUpdate = true;
                        break;
                    }
                }
            }
            UnityEngine.Debug.Log("[CSM-Forge] runtime evidence generation=" + identity.Generation +
                "; SimulationManager=" + (simulation != null) + "; FixedUpdate-surface=" + fixedUpdate +
                "; simulation-isolation=UNPROVEN; authority-projection=UNPROVEN.");
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
            // Network/session inbox draining is connected here in later gates. This callback
            // is deliberately kept as the single simulation-owner entry point.
        }

        public override void OnAfterSimulationTick()
        {
            LoadIdentity identity = RuntimeServices.Lifecycle.Current;
            if (identity.IsValid)
                RuntimeScopeGuard.EndOfSimulationTick(RuntimeServices.Lifecycle, RuntimeServices.Events);
            base.OnAfterSimulationTick();
        }

        public override void OnReleased()
        {
            RuntimeServices.Scheduler.Detach();
            base.OnReleased();
        }
    }
}
