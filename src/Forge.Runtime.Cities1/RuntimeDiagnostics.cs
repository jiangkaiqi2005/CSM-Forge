using System;

namespace CsmForge.Runtime.Cities1
{
    public static class RuntimeDiagnostics
    {
        public static void DumpToGameLog(string reason)
        {
            try
            {
                MultiplayerStatusSnapshot status = RuntimeServices.Multiplayer.Status;
                LoadIdentity identity = RuntimeServices.Lifecycle.Current;
                RuntimeEvent[] entries = RuntimeServices.Events.Read();
                string source = string.IsNullOrEmpty(reason) ? "manual" : reason;

                UnityEngine.Debug.Log("[CSM-Forge] diagnostic dump begin; source=" + source +
                    "; mode=" + status.Mode + "; detail=" + status.Detail +
                    "; revision=" + status.Revision + "; peers=" + status.ConnectedPeers +
                    "; role=" + RuntimeServices.Lifecycle.Role +
                    "; patches=" + RuntimeServices.Patches.Installed +
                    "; world=" + (identity.IsValid ? identity.WorldId.ToString() : "none") +
                    "; epoch=" + (identity.IsValid ? identity.Epoch.ToString() : "0") +
                    "; generation=" + (identity.IsValid ? identity.Generation.ToString() : "0") +
                    "; events=" + entries.Length + ".");

                for (int i = 0; i < entries.Length; i++)
                {
                    RuntimeEvent entry = entries[i];
                    if (entry == null) continue;
                    UnityEngine.Debug.Log("[CSM-Forge] diagnostic event; utc=" + entry.Utc.ToString("o") +
                        "; code=" + entry.Code + "; generation=" + entry.Generation +
                        "; thread=" + entry.ThreadId +
                        "; detail=" + (entry.Detail ?? string.Empty));
                }

                UnityEngine.Debug.Log("[CSM-Forge] diagnostic dump end; source=" + source + ".");
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[CSM-Forge] diagnostic dump failed: " + error);
            }
        }
    }
}
