using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed class MultiplayerStatusSnapshot
    {
        public MultiplayerSessionMode Mode { get; internal set; }
        public string Detail { get; internal set; }
        public int ConnectedPeers { get; internal set; }
        public ulong Revision { get; internal set; }
        public bool DevelopmentTransport { get; internal set; }
        public ulong SnapshotBytesReceived { get; internal set; }
        public ulong SnapshotBytesTotal { get; internal set; }
        public MultiplayerPlayerSnapshot[] Players { get; internal set; }
        public MultiplayerChatSnapshot[] Chat { get; internal set; }
        public MultiplayerPresentationSnapshot[] Presentations { get; internal set; }
    }

    public sealed class MultiplayerPlayerSnapshot
    {
        public MemberIdentity Member { get; internal set; }
        public string DisplayName { get; internal set; }
        public bool IsHost { get; internal set; }
        public bool IsLive { get; internal set; }
        public bool IsLocal { get; internal set; }
    }

    public sealed class MultiplayerChatSnapshot
    {
        public MemberIdentity Member { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class MultiplayerPresentationSnapshot
    {
        public MemberIdentity Member { get; internal set; }
        public string DisplayName { get; internal set; }
        public string ToolName { get; internal set; }
        public float WorldX { get; internal set; }
        public float WorldY { get; internal set; }
        public float WorldZ { get; internal set; }
        public bool Visible { get; internal set; }
        public bool IsLocal { get; internal set; }
    }
}
