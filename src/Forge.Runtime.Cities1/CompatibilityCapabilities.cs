using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class CompatibilityCapabilityReport
    {
        public int ExpansionDlc;
        public int ModderPackDlc;
        public int Assets;
        public int DependencyMods;
        public int ClientOnlyMods;
        public int SynchronizedMods;
        public int ExactMods;
        public int BlockedMods;
        public int StateAdapters;
        public string[] BlockedIds;
        public string[] AdapterIds;

        public static CompatibilityCapabilityReport From(CompatibilityManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            CompatibilityCapabilityReport report = new CompatibilityCapabilityReport();
            List<string> blocked = new List<string>();
            ComponentFingerprint[] entries = manifest.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                string id = entries[i].Id;
                if (Prefix(id, "dlc:expansion:")) report.ExpansionDlc++;
                else if (Prefix(id, "dlc:modderpack:")) report.ModderPackDlc++;
                else if (Prefix(id, "asset:")) report.Assets++;
                else if (Prefix(id, "dependency-mod:")) report.DependencyMods++;
                else if (Prefix(id, "client-mod:")) report.ClientOnlyMods++;
                else if (Prefix(id, "sync-mod:")) report.SynchronizedMods++;
                else if (Prefix(id, "blocked-mod:")) { report.BlockedMods++; blocked.Add(id); }
                else if (Prefix(id, "mod:")) report.ExactMods++;
                else if (Prefix(id, "adapter:")) report.StateAdapters++;
            }
            blocked.Sort(StringComparer.Ordinal);
            report.BlockedIds = blocked.ToArray();
            report.AdapterIds = ForgeExtensionApi.RegisteredAdapterIds;
            return report;
        }

        public string Summary()
        {
            return "dlc-expansion=" + ExpansionDlc +
                "; dlc-content=" + ModderPackDlc +
                "; assets=" + Assets +
                "; dependency-mod-components=" + DependencyMods +
                "; client-only-mods=" + ClientOnlyMods +
                "; synchronized-mods=" + SynchronizedMods +
                "; exact-mod-components=" + ExactMods +
                "; blocked-mods=" + BlockedMods +
                "; state-adapters=" + StateAdapters +
                "; extension-identity-namespaces=" + ExtensionIdentityServices.Maps.ActiveNamespaces().Length;
        }

        private static bool Prefix(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
