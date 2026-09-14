using System;
using System.Reflection;
using System.Threading;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Development probe only. Does not patch simulation, open sockets or alter saves.</summary>
    public sealed class ForgeMod : IUserMod
    {
        public string Name { get { return "CSM-Forge [runtime probe - NOT MULTIPLAYER]"; } }
        public string Description
        {
            get { return "Development-only runtime diagnostics. Multiplayer remains disabled until integration acceptance passes."; }
        }
    }

    public sealed class ForgeLoadingExtension : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            UnityEngine.Debug.Log("[CSM-Forge] probe-only; multiplayer=disabled; load=" + mode +
                "; managedRuntime=" + Environment.Version + "; unity=" + UnityEngine.Application.unityVersion +
                "; callbackThread=" + Thread.CurrentThread.ManagedThreadId +
                "; kernel=" + typeof(HostSession).Assembly.GetName().Version);
            ReportEngineSurface();
        }

        public override void OnLevelUnloading()
        {
            UnityEngine.Debug.Log("[CSM-Forge] probe level unloading; no multiplayer session was started.");
            base.OnLevelUnloading();
        }

        private static void ReportEngineSurface()
        {
            Type simulation = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                simulation = assembly.GetType("SimulationManager", false);
                if (simulation != null) break;
            }
            bool fixedUpdate = false;
            if (simulation != null)
                foreach (MethodInfo method in simulation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "FixedUpdate" && method.GetParameters().Length == 0) fixedUpdate = true;
            UnityEngine.Debug.Log("[CSM-Forge] SimulationManager=" + (simulation != null) +
                "; FixedUpdate-surface=" + fixedUpdate + "; simulation-isolation=UNPROVEN; result-application=UNPROVEN.");
            // Existence of a method is not evidence that patching/skipping it is safe.
        }
    }
}
