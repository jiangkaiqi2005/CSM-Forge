namespace CsmForge.Core
{
    /// <summary>
    /// Session lifecycle mode. WP/robustness: this is pure session vocabulary with no game
    /// dependency, so it lives in Core where the clock-routing policy (and its tests) can see it.
    /// </summary>
    public enum MultiplayerSessionMode
    {
        Offline,
        StartingHost,
        Hosting,
        ConnectingClient,
        ClientCatchingUp,
        ClientLive,
        Faulted
    }
}
