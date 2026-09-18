namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Safety marker for the quarantined automatic Steam join bridge.
    ///
    /// The previous implementation called an unverified Steam native ABI from the CS1
    /// simulation thread. A real CS1 room-creation run ended in a native access violation
    /// on that path, without a managed exception.
    /// Automatic Steam join therefore stays disabled until a bridge is proven inside the
    /// shipped game runtime. Steam remains discovery/presentation only: the UI copies the
    /// LAN invite and opens the official overlay. Forge MemberIdentity remains the network identity.
    /// </summary>
    internal static class ForgeSteamRichPresence
    {
        internal static bool AutomaticJoinAvailable { get { return false; } }
    }
}
