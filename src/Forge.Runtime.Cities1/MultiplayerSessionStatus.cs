namespace CsmForge.Runtime.Cities1
{
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

    public sealed class MultiplayerStatusSnapshot
    {
        public MultiplayerSessionMode Mode { get; internal set; }
        public string Detail { get; internal set; }
        public int ConnectedPeers { get; internal set; }
        public ulong Revision { get; internal set; }
        public bool DevelopmentTransport { get; internal set; }
    }
}
