using System;
using System.Collections.Generic;

namespace CsmForge.Core
{
    /// <summary>Fingerprints are gathered locally; a peer cannot declare itself harmless.</summary>
    public sealed class ComponentFingerprint
    {
        public string Id { get; private set; }
        public Hash256 BinaryHash { get; private set; }
        public Hash256 ConfigurationHash { get; private set; }
        public ComponentFingerprint(string id, Hash256 binaryHash, Hash256 configurationHash)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) throw new ArgumentException("Invalid component identity.", "id");
            foreach (char value in id)
                if (!((value >= 'a' && value <= 'z') || (value >= '0' && value <= '9') || value == '.' || value == ':' || value == '-' || value == '_'))
                    throw new ArgumentException("Component IDs use canonical lowercase ASCII.", "id");
            if (binaryHash == null || configurationHash == null) throw new ArgumentNullException("binaryHash");
            Id = id; BinaryHash = binaryHash; ConfigurationHash = configurationHash;
        }
        public bool Matches(ComponentFingerprint other)
        {
            return other != null && Id == other.Id && BinaryHash.Equals(other.BinaryHash) && ConfigurationHash.Equals(other.ConfigurationHash);
        }
    }

    public sealed class CompatibilityManifest
    {
        private readonly Dictionary<string, ComponentFingerprint> components;
        public Hash256 GameBuildHash { get; private set; }
        public Hash256 SchemaHash { get; private set; }
        public CompatibilityManifest(Hash256 gameBuildHash, Hash256 schemaHash, IEnumerable<ComponentFingerprint> entries)
        {
            if (gameBuildHash == null || schemaHash == null || entries == null) throw new ArgumentNullException("gameBuildHash");
            GameBuildHash = gameBuildHash; SchemaHash = schemaHash;
            components = Collect(entries);
        }
        internal static Dictionary<string, ComponentFingerprint> Collect(IEnumerable<ComponentFingerprint> entries)
        {
            if (entries == null) throw new ArgumentNullException("entries");
            Dictionary<string, ComponentFingerprint> result = new Dictionary<string, ComponentFingerprint>(StringComparer.Ordinal);
            foreach (ComponentFingerprint entry in entries)
            {
                if (entry == null || result.Count == 4096 || result.ContainsKey(entry.Id))
                    throw new ArgumentException("Invalid, duplicate or excessive manifest entries.", "entries");
                result.Add(entry.Id, entry);
            }
            return result;
        }
        public ComponentFingerprint[] Entries
        {
            get
            {
                List<ComponentFingerprint> values = new List<ComponentFingerprint>(components.Values);
                values.Sort(delegate(ComponentFingerprint a, ComponentFingerprint b) { return StringComparer.Ordinal.Compare(a.Id, b.Id); });
                return values.ToArray();
            }
        }
    }

    /// <summary>
    /// Approved required components and optional local-only components are a trusted room
    /// policy built from audited adapter support. Unknown mods fail closed. The v3 ultimate
    /// manifest also uses audited component categories: extra client DLC/assets are harmless,
    /// known client-only mods may differ, and blocked mods fence room creation.
    /// </summary>
    public sealed class CompatibilityPolicy
    {
        private readonly Hash256 gameBuild;
        private readonly Hash256 schema;
        private readonly Dictionary<string, ComponentFingerprint> required;
        private readonly Dictionary<string, ComponentFingerprint> optionalLocal;

        public CompatibilityPolicy(Hash256 gameBuild, Hash256 schema,
            IEnumerable<ComponentFingerprint> required, IEnumerable<ComponentFingerprint> approvedLocalOnly)
        {
            if (gameBuild == null || schema == null) throw new ArgumentNullException("gameBuild");
            this.gameBuild = gameBuild; this.schema = schema;
            this.required = CompatibilityManifest.Collect(required);
            optionalLocal = CompatibilityManifest.Collect(approvedLocalOnly);
            foreach (string id in this.required.Keys)
                if (optionalLocal.ContainsKey(id)) throw new ArgumentException("Required components cannot be downgraded to optional.");
        }

        // Validate host AND every client before registering a transport connection in HostSession.
        public string[] Evaluate(CompatibilityManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            List<string> errors = new List<string>();
            if (!gameBuild.Equals(manifest.GameBuildHash)) errors.Add("game-build-mismatch");
            if (!schema.Equals(manifest.SchemaHash)) errors.Add("schema-mismatch");
            Dictionary<string, ComponentFingerprint> actual = CompatibilityManifest.Collect(manifest.Entries);
            foreach (KeyValuePair<string, ComponentFingerprint> pair in required)
            {
                if (IsPrefix(pair.Key, "blocked-mod:"))
                {
                    errors.Add("host-unsupported:" + pair.Key);
                    continue;
                }
                if (IsPrefix(pair.Key, "client-mod:")) continue;

                ComponentFingerprint value;
                if (!actual.TryGetValue(pair.Key, out value)) errors.Add("missing:" + pair.Key);
                else if (!pair.Value.Matches(value)) errors.Add("fingerprint-mismatch:" + pair.Key);
            }
            foreach (KeyValuePair<string, ComponentFingerprint> pair in actual)
            {
                ComponentFingerprint expected;
                if (required.TryGetValue(pair.Key, out expected))
                {
                    if (IsPrefix(pair.Key, "client-mod:")) continue;
                    continue;
                }
                if (AllowsClientExtra(pair.Key)) continue;

                ComponentFingerprint approved;
                if (!optionalLocal.TryGetValue(pair.Key, out approved)) errors.Add("unsupported:" + pair.Key);
                else if (!approved.Matches(pair.Value)) errors.Add("local-fingerprint-mismatch:" + pair.Key);
            }
            errors.Sort(StringComparer.Ordinal);
            return errors.ToArray();
        }

        private static bool AllowsClientExtra(string id)
        {
            return IsPrefix(id, "dlc:") || IsPrefix(id, "asset:") || IsPrefix(id, "client-mod:");
        }

        private static bool IsPrefix(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
