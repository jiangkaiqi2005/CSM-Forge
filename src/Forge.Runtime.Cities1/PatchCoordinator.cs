using System;
using System.Reflection;
using CitiesHarmony.API;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    public sealed class CitiesPatchCoordinator
    {
        private const string HarmonyId = "com.csmforge.v3";
        private readonly object gate = new object();
        private readonly RuntimeEventLog events;
        private bool installed;
        private bool requested;

        public CitiesPatchCoordinator(RuntimeEventLog events)
        {
            if (events == null) throw new ArgumentNullException("events");
            this.events = events;
        }

        public bool Installed { get { lock (gate) return installed; } }

        public void InstallWhenReady()
        {
            lock (gate)
            {
                if (requested || installed) return;
                requested = true;
            }
            HarmonyHelper.DoOnHarmonyReady(delegate
            {
                try
                {
                    Harmony harmony = new Harmony(HarmonyId);
                    harmony.PatchAll(Assembly.GetExecutingAssembly());
                    KnownModBridgeRegistry.InstallOptionalPatches(harmony);
                    lock (gate) installed = true;
                    events.Record(RuntimeEventCode.PatchReady, RuntimeServices.Lifecycle.Current.Generation, null);
                }
                catch (Exception error)
                {
                    try
                    {
                        if (HarmonyHelper.IsHarmonyInstalled) new Harmony(HarmonyId).UnpatchAll(HarmonyId);
                    }
                    catch { }
                    KnownModBridgeRegistry.ResetOptionalPatchState();
                    lock (gate) installed = false;
                    events.Record(RuntimeEventCode.PatchFailed, RuntimeServices.Lifecycle.Current.Generation, error.GetType().Name);
                    RuntimeServices.Lifecycle.Fence("Harmony patch installation failed");
                }
            });
        }

        public void RefreshOptionalBridges()
        {
            bool ready;
            lock (gate) ready = installed;
            if (!ready || !HarmonyHelper.IsHarmonyInstalled) return;
            try
            {
                KnownModBridgeRegistry.InstallOptionalPatches(new Harmony(HarmonyId));
            }
            catch (Exception error)
            {
                events.Record(RuntimeEventCode.PatchFailed, RuntimeServices.Lifecycle.Current.Generation,
                    "known-mod:" + error.GetType().Name);
                RuntimeServices.Lifecycle.Fence("known Mod bridge patch failed");
                throw;
            }
        }

        public void Uninstall()
        {
            bool shouldUnpatch;
            lock (gate)
            {
                shouldUnpatch = installed;
                installed = false;
                requested = false;
            }
            KnownModBridgeRegistry.ResetOptionalPatchState();
            if (!shouldUnpatch || !HarmonyHelper.IsHarmonyInstalled) return;
            try { new Harmony(HarmonyId).UnpatchAll(HarmonyId); }
            catch (Exception error)
            {
                events.Record(RuntimeEventCode.PatchFailed, RuntimeServices.Lifecycle.Current.Generation,
                    "unpatch: " + error.GetType().Name);
            }
        }
    }
}
