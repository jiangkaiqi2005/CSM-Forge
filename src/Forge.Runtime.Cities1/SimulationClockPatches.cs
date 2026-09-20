using HarmonyLib;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(SimulationManager), "SimulationPaused", MethodType.Setter)]
    public static class ForgeSimulationPausedPatch
    {
        public static bool Prefix(bool value)
        {
            if (RuntimeScopeGuard.IsApplying && RuntimeScopeGuard.ActiveDomain == SimulationClockAuthorityDomain.Id) return true;
            if (!SimulationClockRouting.RoutesToHost(RuntimeServices.Multiplayer.Status.Mode)) return true;
            int speed = SimulationManager.instance != null ? SimulationManager.instance.SelectedSimulationSpeed : 1;
            // If the request cannot be queued, fall through to the vanilla setter rather than
            // dropping the player's input on the floor.
            if (!RuntimeServices.Multiplayer.TryQueueSimulationClock(value, speed)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(SimulationManager), "SelectedSimulationSpeed", MethodType.Setter)]
    public static class ForgeSelectedSimulationSpeedPatch
    {
        public static bool Prefix(int value)
        {
            if (RuntimeScopeGuard.IsApplying && RuntimeScopeGuard.ActiveDomain == SimulationClockAuthorityDomain.Id) return true;
            if (value < 0 || value > 3) return false;
            if (!SimulationClockRouting.RoutesToHost(RuntimeServices.Multiplayer.Status.Mode)) return true;
            bool paused = SimulationManager.instance != null && SimulationManager.instance.SimulationPaused;
            if (!RuntimeServices.Multiplayer.TryQueueSimulationClock(paused, value)) return true;
            return false;
        }
    }
}
