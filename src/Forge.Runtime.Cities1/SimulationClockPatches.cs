using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(SimulationManager), "SimulationPaused", MethodType.Setter)]
    public static class ForgeSimulationPausedPatch
    {
        public static bool Prefix(bool value)
        {
            if (RuntimeScopeGuard.IsApplying && RuntimeScopeGuard.ActiveDomain == SimulationClockAuthorityDomain.Id) return true;
            MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
            if (mode == MultiplayerSessionMode.Offline || mode == MultiplayerSessionMode.StartingHost || mode == MultiplayerSessionMode.ConnectingClient)
                return true;
            if (mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.ClientLive)
            {
                int speed = SimulationManager.instance != null ? SimulationManager.instance.SelectedSimulationSpeed : 1;
                RuntimeServices.Multiplayer.TryQueueSimulationClock(value, speed);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(SimulationManager), "SelectedSimulationSpeed", MethodType.Setter)]
    public static class ForgeSelectedSimulationSpeedPatch
    {
        public static bool Prefix(int value)
        {
            if (RuntimeScopeGuard.IsApplying && RuntimeScopeGuard.ActiveDomain == SimulationClockAuthorityDomain.Id) return true;
            MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
            if (mode == MultiplayerSessionMode.Offline || mode == MultiplayerSessionMode.StartingHost || mode == MultiplayerSessionMode.ConnectingClient)
                return true;
            if (value < 0 || value > 3) return false;
            if (mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.ClientLive)
            {
                bool paused = SimulationManager.instance != null && SimulationManager.instance.SimulationPaused;
                if (paused) paused = false;
                RuntimeServices.Multiplayer.TryQueueSimulationClock(paused, value);
            }
            return false;
        }
    }
}
