using System;
using System.Collections.Generic;
using System.Reflection;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// WP-3.2b: state adapter for manifest-declared synchronized mods. Resolves the mod's
    /// settings instance through its declared holders, captures/applies it with the shared
    /// SettingsSurfaceCodec, and is registered automatically from ModCompatibilityCatalog
    /// entries - a new mod needs only a manifest document, no hand-written bridge.
    /// </summary>
    internal sealed class GenericSettingsStateAdapter : IForgeStateAdapterV1
    {
        private readonly string adapterId;
        private readonly string userModType;
        private readonly string settingsTypeName;
        private readonly string[] holderTypeNames;
        private readonly string holderFieldName;
        private readonly string[] localOnlySettings;
        private readonly string[] blockedBooleanSettings;
        private readonly FixedValueRule[] fixedValueRules;
        private readonly int maximumStateBytes;
        private SettingsSurfaceCodec codec;
        private Type settingsType;
        private object settingsInstance;
        private bool resolved;

        private static readonly Dictionary<string, GenericSettingsStateAdapter> Resolved =
            new Dictionary<string, GenericSettingsStateAdapter>(StringComparer.Ordinal);

        internal GenericSettingsStateAdapter(ModEntryData entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            userModType = entry.UserModType;
            adapterId = "bridge.generic." + Canonicalize(userModType);
            settingsTypeName = entry.SettingsTypeName ?? string.Empty;
            holderTypeNames = entry.HolderTypeNames ?? new string[0];
            holderFieldName = string.IsNullOrEmpty(entry.HolderFieldName) ? "_modSetting" : entry.HolderFieldName;
            localOnlySettings = entry.LocalOnlySettings ?? new string[0];
            blockedBooleanSettings = entry.BlockedBooleanSettings ?? new string[0];
            maximumStateBytes = 4096;

            int ruleCount = entry.FixedValuePropertyNames == null ? 0 : entry.FixedValuePropertyNames.Length;
            var rules = new FixedValueRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = new FixedValueRule(entry.FixedValuePropertyNames[i], entry.FixedValueRequired[i], entry.FixedValueLabels[i]);
            fixedValueRules = rules;
        }

        public string AdapterId { get { return adapterId; } }
        public uint SchemaVersion { get { return 1; } }

        internal string UserModType { get { return userModType; } }

        /// <summary>Resolution succeeded against the loaded game (settings instance available).</summary>
        internal bool IsResolved { get { return resolved; } }

        /// <summary>Attempts the one-time settings resolution; false keeps the mod blocked.</summary>
        internal bool TryResolve() { return EnsureResolved(); }

        public byte[] CaptureAbsolute()
        {
            if (!EnsureResolved()) throw new InvalidOperationException("Settings surface is unavailable: " + userModType);
            return codec.Capture(settingsInstance);
        }

        public void ApplyAbsolute(byte[] state)
        {
            if (!EnsureResolved()) throw new InvalidOperationException("Settings surface is unavailable: " + userModType);
            long[] values = codec.ValidateState(state);
            codec.WriteValues(settingsInstance, values);
        }

        /// <summary>
        /// One-time scan for the declared settings type and its live instance, following the
        /// holder convention (static field on the declared holder types). Resolution failure
        /// keeps the adapter unresolved: the collector then classifies the mod as blocked
        /// instead of silently running it unsynchronized.
        /// </summary>
        private bool EnsureResolved()
        {
            if (resolved) return true;
            if (settingsTypeName.Length == 0) return false;
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = assembly.GetType(settingsTypeName, false);
                if (candidate != null) { type = candidate; break; }
            }
            if (type == null) return false;
            object instance = null;
            foreach (string holderName in holderTypeNames)
            {
                Type holder = type.Assembly.GetType(holderName, false);
                if (holder == null) continue;
                FieldInfo field = holder.GetField(holderFieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) continue;
                try { instance = field.GetValue(null); } catch { instance = null; }
                if (instance != null && type.IsInstanceOfType(instance)) break;
            }
            if (instance == null) return false;
            codec = new SettingsSurfaceCodec(
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                localOnlySettings, blockedBooleanSettings, fixedValueRules,
                0x46475347u, maximumStateBytes, userModType);
            settingsType = type; settingsInstance = instance; resolved = true;
            lock (Resolved) Resolved[userModType] = this;
            return true;
        }

        internal static bool IsResolvedFor(string userModType)
        {
            lock (Resolved)
            {
                GenericSettingsStateAdapter adapter;
                return Resolved.TryGetValue(userModType, out adapter) && adapter.IsResolved;
            }
        }

        internal static string Canonicalize(string value)
        {
            var result = new System.Text.StringBuilder();
            foreach (char raw in value.ToLowerInvariant())
            {
                char ch = raw;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '_')
                    result.Append(ch);
                else result.Append('-');
                if (result.Length >= 60) break;
            }
            return result.Length == 0 ? "unknown" : result.ToString();
        }
    }
}
