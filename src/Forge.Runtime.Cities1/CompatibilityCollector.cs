using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework.Packaging;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>Collects local facts only. CompatibilityPolicy remains the authority.</summary>
    public static class CitiesCompatibilityCollector
    {
        private const int EntryLimit = 4096;
        private static readonly object OwnedDlcGate = new object();
        private static bool ownedDlcCached;
        private static ulong ownedExpansionMask;
        private static ulong ownedModderPackMask;
        public static CompatibilityManifest Collect()
        {
            List<ComponentFingerprint> entries = new List<ComponentFingerprint>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            EnabledPluginCatalog.Entry[] enabledPlugins = EnabledPluginCatalog.Capture().Entries;
            for (int pluginIndex = 0; pluginIndex < enabledPlugins.Length; pluginIndex++)
            {
                EnabledPluginCatalog.Entry plugin = enabledPlugins[pluginIndex];
                string typeName = plugin.UserModTypeName;
                Assembly[] assemblies = plugin.Assemblies;
                for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
                {
                    Assembly assembly = assemblies[assemblyIndex];
                    if (assembly == null) continue;
                    string category = ModCategory(typeName, assembly);
                    string id = category + ":" + Workshop(plugin.PublishedFileId) + ":" + Canonical(assembly.GetName().Name);
                    Add(entries, seen, id, BinaryHash(assembly), ModConfigurationHash(typeName, assembly));
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

            ulong expansionMask;
            ulong modderPackMask;
            GetOwnedDlcMasks(out expansionMask, out modderPackMask);
            AddOwnedDlcBits(entries, seen, "dlc:expansion:", expansionMask);
            AddOwnedDlcBits(entries, seen, "dlc:modderpack:", modderPackMask);

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

        private static void GetOwnedDlcMasks(out ulong expansionMask, out ulong modderPackMask)
        {
            lock (OwnedDlcGate)
            {
                if (!ownedDlcCached)
                {
                    ownedExpansionMask = Convert.ToUInt64(SteamHelper.GetOwnedExpansionMask(), CultureInfo.InvariantCulture);
                    ownedModderPackMask = Convert.ToUInt64(SteamHelper.GetOwnedModderPackMask(), CultureInfo.InvariantCulture);
                    ownedDlcCached = true;
                }
                expansionMask = ownedExpansionMask;
                modderPackMask = ownedModderPackMask;
            }
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
                    case ForgeModCompatibilityKind.ForgeSynchronized:
                        if (!ForgeExtensionApi.HasRegistrationFromAssembly(assembly))
                            throw new InvalidOperationException("ForgeSynchronized mod declaration has no state adapter: " + assembly.GetName().Name);
                        return "sync-mod";
                    case ForgeModCompatibilityKind.Blocked: return "blocked-mod";
                    default: return "mod";
                }
            }
            if (typeName == ModCompatibilityCatalog.DependencyModType) return "dependency-mod";
            if (typeName == ModCompatibilityCatalog.BlockedModType) return "blocked-mod";
            if (typeName == "GameAnarchy.Mod" && !GameAnarchyBridge.IsAvailable) return "blocked-mod";
            if (typeName == "InfiniteGoodsMod.ModIdentity" && !InfiniteGoodsBridge.IsAvailable) return "blocked-mod";
            if (IsAuditedCslModernMap(typeName, assembly)) return "client-mod";
            for (int i = 0; i < ModCompatibilityCatalog.ClientOnlyModTypes.Length; i++)
                if (typeName == ModCompatibilityCatalog.ClientOnlyModTypes[i]) return "client-mod";
            // WP-3.1: heuristic auto-classification. A mod whose visible type surface never
            // names a simulation manager/tool is treated as client-only; explicit declarations
            // and the audited lists above always win, and ambiguous assemblies fail closed to
            // exact-match ("mod").
            if (assembly != null && !TouchesSimulationSurface(assembly)) return "client-mod";
            return "mod";
        }

        /// <summary>
        /// WP-3.1: collect every visible simple type name of the mod assembly (own types, base
        /// chains, interfaces, member signatures) and feed the conservative matcher. Unloadable
        /// types or assemblies fail closed to simulation-touching (exact-match classification).
        /// Known v1 limitation: generic type arguments and Harmony attribute targets are not
        /// scanned, so a UI mod using List&lt;BuildingManager&gt; in a signature is kept exact-match.
        /// </summary>
        private static bool TouchesSimulationSurface(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error)
            {
                types = error.Types;
                if (types == null) return true;
            }
            catch { return true; }
            List<string> names = new List<string>(types.Length * 2);
            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (type == null) return true; // unloadable type: cannot prove presentation-only
                if (type.BaseType != null) names.Add(type.BaseType.Name);
                try
                {
                    foreach (Type interfaceType in type.GetInterfaces()) names.Add(interfaceType.Name);
                    const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (FieldInfo field in type.GetFields(all | BindingFlags.DeclaredOnly)) names.Add(field.FieldType.Name);
                    foreach (PropertyInfo property in type.GetProperties(all | BindingFlags.DeclaredOnly)) names.Add(property.PropertyType.Name);
                    foreach (MethodInfo method in type.GetMethods(all | BindingFlags.DeclaredOnly))
                    {
                        names.Add(method.ReturnType.Name);
                        ParameterInfo[] parameters = method.GetParameters();
                        for (int p = 0; p < parameters.Length; p++) names.Add(parameters[p].ParameterType.Name);
                    }
                }
                catch { return true; } // reflection failure: fail closed
            }
            return SimulationSurfaceMatcher.TouchesSimulationSurface(names);
        }

        private static bool IsAuditedCslModernMap(string typeName, Assembly assembly)
        {
            if (typeName != "CSLModernMap.CSLModernMap" || assembly == null) return false;
            AssemblyName name = assembly.GetName();
            if (!StringComparer.Ordinal.Equals(name.Name, "CSLModernMap") || name.Version != new Version(6, 6, 2, 0)) return false;
            return StringComparer.OrdinalIgnoreCase.Equals(BinaryHash(assembly).ToString(),
                "9fc331505b43484dc55d38762d5198e7b68aa04dca19af5ff8f59d37e76c300a");
        }

        private static Hash256 ModConfigurationHash(string typeName, Assembly assembly)
        {
            string baseline = assembly.GetName().Version + "|enabled=1";
            try
            {
                if (typeName == "GameAnarchy.Mod" && GameAnarchyBridge.IsAvailable)
                    return ConfigHash(baseline + "|forge-shared=" + Hash256.Compute(new GameAnarchyBridgeAdapter().CaptureAbsolute()).ToString());
                if (typeName == "EightyOne2.Mod" && EightyOne2Bridge.IsAvailable)
                    return ConfigHash(baseline + "|forge-shared=" + Hash256.Compute(new EightyOne2BridgeAdapter().CaptureAbsolute()).ToString());
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Could not fingerprint shared settings for known Mod " + (typeName ?? assembly.GetName().Name) + ".", error);
            }
            return ConfigHash(baseline);
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
