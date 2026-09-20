namespace CsmForge.Core
{
    /// <summary>
    /// Which session modes make the host authoritative for pause and game speed.
    ///
    /// Bug fix: the pause/speed Harmony prefixes previously named the local-apply modes by
    /// omission - they allow-listed Offline/StartingHost/ConnectingClient and intercepted only
    /// Hosting/ClientLive, so Faulted (after a fence) and ClientCatchingUp fell through to a
    /// branch that suppressed the vanilla setter without queueing anything. The player could then
    /// never pause the game again. Routing is now decided here, positively, for the two active
    /// modes only; every other mode applies locally.
    /// </summary>
    public static class SimulationClockRouting
    {
        /// <summary>True when a pause/speed change must be queued to the host instead of applied.</summary>
        public static bool RoutesToHost(MultiplayerSessionMode mode)
        {
            return mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.ClientLive;
        }
    }
}
