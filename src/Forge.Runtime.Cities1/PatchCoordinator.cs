using CsmForge.Core;
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
        private uint statusRevision;
        private string failureDetail;

        public CitiesPatchCoordinator(RuntimeEventLog events)
        {
            Check.NotNull(events, "events");
            this.events = events;
        }

        public bool Installed { get { lock (gate) return installed; } }
        public uint StatusRevision { get { lock (gate) return statusRevision; } }
        public string FailureDetail { get { lock (gate) return failureDetail; } }

        public void InstallWhenReady()
        {
            lock (gate)
            {
                if (requested || installed) return;
                requested = true;
            }
            HarmonyHelper.DoOnHarmonyReady(delegate
            {
                string currentPatch = "initializing";
                try
                {
                    Harmony harmony = new Harmony(HarmonyId);
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    Type[] types = assembly.GetTypes();
                    for (int i = 0; i < types.Length; i++)
                    {
                        currentPatch = types[i].FullName;
                        harmony.CreateClassProcessor(types[i]).Patch();
                    }
                    currentPatch = "known-mod-bridges";
                    KnownModBridgeRegistry.InstallOptionalPatches(harmony);
                    lock (gate)
                    {
                        installed = true;
                        failureDetail = null;
                        statusRevision++;
                    }
                    events.Record(RuntimeEventCode.PatchReady, RuntimeServices.Lifecycle.Current.Generation, null);
                    UnityEngine.Debug.Log("[CSM-Forge] Harmony patches ready.");
                }
                catch (Exception error)
                {
                    try
                    {
                        if (HarmonyHelper.IsHarmonyInstalled) new Harmony(HarmonyId).UnpatchAll(HarmonyId);
                    }
                    catch { }
                    KnownModBridgeRegistry.ResetOptionalPatchState();
                    string detail = currentPatch + ":" + RootCause(error).GetType().Name;
                    lock (gate)
                    {
                        installed = false;
                        failureDetail = detail;
                        statusRevision++;
                    }
                    events.Record(RuntimeEventCode.PatchFailed, RuntimeServices.Lifecycle.Current.Generation, detail);
                    UnityEngine.Debug.LogError("[CSM-Forge] Harmony patch installation failed at " + currentPatch + ": " + error);
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
                failureDetail = null;
                statusRevision++;
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

        private static Exception RootCause(Exception error)
        {
            Exception value = error;
            while (value.InnerException != null) value = value.InnerException;
            return value;
        }
    }
}
