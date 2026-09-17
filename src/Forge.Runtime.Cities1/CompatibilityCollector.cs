using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.Plugins;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Collects local facts only. CompatibilityPolicy remains the authority.</summary>
    public static class CitiesCompatibilityCollector
    {
        private const int EntryLimit = 4096;
        private static readonly string[] ClientOnlyModTypes =
        {
            "LoadingScreenMod.Mod", "MyFirstMod.DestroyChirperMod", "RemoveChirper.RemoveChirper",
            "ChirpRemover.ChirpRemover", "MoreAspectRatios.MoreAspectRatios", "FPSCamera.Mod", "AchieveIt.ModInfo"
        };

        public static CompatibilityManifest Collect()
        {
            List<ComponentFingerprint> entries = new List<ComponentFingerprint>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (PluginManager.PluginInfo plugin in Singleton<PluginManager>.instance.GetPluginsInfo())
            {
                if (plugin == null || !plugin.isEnabled) continue;
                IUserMod userMod = plugin.userModInstance as IUserMod;
                string typeName = userMod == null ? null : userMod.GetType().FullName;
                List<Assembly> assemblies = plugin.GetAssemblies();
                foreach (Assembly assembly in assemblies)
                {
                    if (assembly == null) continue;
                    string category = ModCategory(typeName, assembly);
                    string id = category + ":" + Workshop(plugin.publishedFileID.AsUInt64) + ":" + Canonical(assembly.GetName().Name);
                    Add(entries, seen, id, BinaryHash(assembly), ConfigHash(assembly.GetName().Version + "|enabled=1"));
                }

                if (assemblies.Count == 0 && userMod != null)
                {
                    Assembly assembly = userMod.GetType().Assembly;
                    string category = ModCategory(typeName, assembly);
                    string id = category + ":" + Workshop(plugin.publishedFileID.AsUInt64) + ":" + Canonical(assembly.GetName().Name);
                    Add(entries, seen, id, BinaryHash(assembly), ConfigHash(assembly.GetName().Version + "|enabled=1"));
                }
            }

            foreach (Package.Asset asset in PackageManager.FilterAssets(UserAssetType.CustomAssetMetaData))
            {
                if (asset == null || !asset.isEnabled) continue;
                ulong published = asset.package == null ? 0UL : asset.package.GetPublishedFileID().AsUInt64;
                string id = "asset:" + Workshop(published) + ":" + Canonical(asset.name);
                string checksum = asset.checksum == null ? "" : asset.checksum.ToString();
                Add(entries, seen, id, ConfigHash(checksum), ConfigHash(asset.fullName + "|enabled=1"));
            }

            AddOwnedDlcBits(entries, seen, "dlc:expansion:", Convert.ToUInt64(SteamHelper.GetOwnedExpansionMask(), CultureInfo.InvariantCulture));
            AddOwnedDlcBits(entries, seen, "dlc:modderpack:", Convert.ToUInt64(SteamHelper.GetOwnedModderPackMask(), CultureInfo.InvariantCulture));

            ForgeStateAdapterRegistration[] adapters = ForgeExtensionApi.SnapshotRegistrations();
            for (int i = 0; i < adapters.Length; i++)
            {
                ForgeStateAdapterRegistration adapter = adapters[i];
                string id = "adapter:" + adapter.AdapterId + ":v" + adapter.SchemaVersion.ToString(CultureInfo.InvariantCulture);
                Add(entries, seen, id, BinaryHash(adapter.Assembly), ConfigHash("schema=" + adapter.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
            }

            Hash256 build = GameBuildHash();
            Hash256 schema = ConfigHash("csm-forge-v3-schema:2|core=" + typeof(CompatibilityManifest).Assembly.GetName().Version);
            return new CompatibilityManifest(build, schema, entries);
        }

        private static void AddOwnedDlcBits(List<ComponentFingerprint> entries, HashSet<string> seen, string prefix, ulong mask)
        {
            for (int bit = 0; bit < 64; bit++)
            {
                ulong flag = 1UL << bit;
                if ((mask & flag) == 0) continue;
                string id = prefix + bit.ToString(CultureInfo.InvariantCulture);
                Add(entries, seen, id, ConfigHash(id), ConfigHash("owned=1"));
            }
        }

        private static string ModCategory(string typeName, Assembly assembly)
        {
            ForgeModCompatibilityKind declared;
            if (ForgeCompatibilityApi.TryGet(assembly, out declared))
            {
                switch (declared)
                {
                    case ForgeModCompatibilityKind.ClientOnly: return "client-mod";
                    case ForgeModCompatibilityKind.ForgeSynchronized: return "sync-mod";
                    case ForgeModCompatibilityKind.Blocked: return "blocked-mod";
                    default: return "mod";
                }
            }
            if (typeName == "CitiesHarmony.Mod") return "dependency-mod";
            if (typeName == "TrafficManager.Lifecycle.TrafficManagerMod") return "blocked-mod";
            for (int i = 0; i < ClientOnlyModTypes.Length; i++)
                if (typeName == ClientOnlyModTypes[i]) return "client-mod";
            return "mod";
        }

        private static void Add(List<ComponentFingerprint> entries, HashSet<string> seen, string id,
            Hash256 binary, Hash256 configuration)
        {
            if (entries.Count >= EntryLimit) throw new InvalidOperationException("Compatibility manifest entry limit exceeded.");
            if (!seen.Add(id)) return;
            entries.Add(new ComponentFingerprint(id, binary, configuration));
        }

        private static Hash256 GameBuildHash()
        {
            StringBuilder value = new StringBuilder();
            value.Append(BuildConfig.applicationVersion ?? "unknown");
            Assembly game = typeof(BuildConfig).Assembly;
            value.Append('|').Append(game.GetName().Version);
            Hash256 file = TryFileHash(game);
            value.Append('|').Append(file.ToString());
            return ConfigHash(value.ToString());
        }

        private static Hash256 BinaryHash(Assembly assembly)
        {
            Hash256 value = TryFileHash(assembly);
            if (value != null) return value;
            AssemblyName name = assembly.GetName();
            return ConfigHash(name.Name + "|" + name.Version + "|" + assembly.FullName);
        }

        private static Hash256 TryFileHash(Assembly assembly)
        {
            try
            {
                string location = assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                    using (FileStream stream = File.OpenRead(location)) return Hash256.Compute(stream);
            }
            catch { }
            return ConfigHash(assembly.FullName ?? "unknown-assembly");
        }

        private static Hash256 ConfigHash(string text)
        {
            return Hash256.Compute(Encoding.UTF8.GetBytes(text ?? ""));
        }

        private static string Workshop(ulong id)
        {
            return id == 0 || id == ulong.MaxValue ? "local" : id.ToString(CultureInfo.InvariantCulture);
        }

        private static string Canonical(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            StringBuilder result = new StringBuilder();
            foreach (char raw in value.ToLowerInvariant())
            {
                char ch = raw;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '_')
                    result.Append(ch);
                else result.Append('-');
                if (result.Length >= 72) break;
            }
            return result.Length == 0 ? "unknown" : result.ToString();
        }
    }
}
