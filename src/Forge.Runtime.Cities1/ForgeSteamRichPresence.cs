namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Safety marker for the quarantined automatic Steam join bridge.
    ///
    /// Two real CS1 runs ended in native access violations after Forge entered the
    /// manually declared Steam ABI: first while publishing room state, then while
    /// registering the join callback during level load. Managed exception handling
    /// cannot contain those process-level faults.
    ///
    /// Automatic Steam join therefore stays disabled until a bridge is proven inside
    /// the shipped game runtime. Steam remains discovery/presentation only: the UI
    /// copies the direct invite and opens the official overlay.
    /// Forge MemberIdentity remains the network identity.
    /// </summary>
    internal static class ForgeSteamRichPresence
    {
        internal static bool AutomaticJoinAvailable { get { return false; } }
    }
}
