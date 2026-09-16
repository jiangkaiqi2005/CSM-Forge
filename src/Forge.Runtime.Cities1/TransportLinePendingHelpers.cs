namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal bool CancelPendingLocalTransportLine(ushort nativeId)
        {
            if (nativeId == 0) return false;
            return pendingLocalTransportLines.Remove(nativeId);
        }
    }
}
