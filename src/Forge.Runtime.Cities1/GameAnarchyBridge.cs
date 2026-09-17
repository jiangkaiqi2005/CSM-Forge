using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Host-owned shared-simulation settings for Game Anarchy 1.3.x. UI-only settings and keybindings
    /// are intentionally excluded. Both peers may execute the mod's deterministic patches, but only
    /// the Host may change the shared configuration or perform manual money mutations.
    /// </summary>
    internal sealed class GameAnarchyBridgeAdapter : IForgeStateAdapterV1
    {
        private const uint Magic = 0x31414746u; // FGA1
        internal const string Adapter = "bridge.gameanarchy";
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute()
        {
            Type type; object instance;
            if (!GameAnarchyBridge.TryGetSettings(out type, out instance))
                throw new InvalidOperationException("Game Anarchy settings are unavailable.");
            PropertyInfo[] properties = GameAnarchyBridge.SharedProperties(type);
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(GameAnarchyBridge.SchemaFingerprint(properties));
                writer.Write((ushort)properties.Length);
                for (int i = 0; i < properties.Length; i++) writer.Write(GameAnarchyBridge.ToBits(properties[i].PropertyType, properties[i].GetValue(instance, null)));
                writer.Flush();
                if (stream.Length > 4096) throw new InvalidOperationException("Game Anarchy bridge state exceeded its fixed budget.");
                return stream.ToArray();
            }
        }

        public void ApplyAbsolute(byte[] state)
        {
            if (state == null || state.Length < 10 || state.Length > 4096) throw new InvalidDataException("Invalid Game Anarchy bridge state.");
            Type type; object instance;
            if (!GameAnarchyBridge.TryGetSettings(out type, out instance)) throw new InvalidOperationException("Game Anarchy settings are unavailable.");
            PropertyInfo[] properties = GameAnarchyBridge.SharedProperties(type);
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != GameAnarchyBridge.SchemaFingerprint(properties))
                    throw new InvalidDataException("Game Anarchy bridge schema mismatch.");
                if (reader.ReadUInt16() != properties.Length) throw new InvalidDataException("Game Anarchy bridge property count mismatch.");
                GameAnarchyBridge.BeginApply();
                try
                {
                    for (int i = 0; i < properties.Length; i++)
                        properties[i].SetValue(instance, GameAnarchyBridge.FromBits(properties[i].PropertyType, reader.ReadInt64()), null);
                }
                finally { GameAnarchyBridge.EndApply(); }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Game Anarchy bridge bytes.");
            }
        }
    }

    internal static class GameAnarchyBridge
    {
        private const string SettingsTypeName = "GameAnarchy.ModSettings.ModSetting";
        private static readonly string[] LocalOnly = { "AchievementSystemEnabled", "SkipIntroEnabled", "OptionsPanelCategoriesHorizontalOffset", "OptionsPanelCategoriesUpdated" };
        private static readonly string[] MoneyMutationMethods = { "OnPreSimulationFrame", "ChargeInterest", "AutoAddMoney", "SetStartMoney", "AddMoneyManually", "SubstrateMoneyManually", "ModifyMoney", "AddLoanAmount" };
        [ThreadStatic] private static bool applying;
        private static bool patched;

        internal static bool IsAvailable { get { return ResolveType(SettingsTypeName) != null; } }
        internal static void BeginApply() { applying = true; }
        internal static void EndApply() { applying = false; }

        internal static bool TryGetSettings(out Type type, out object instance)
        {
            type = ResolveType(SettingsTypeName); instance = null;
            if (type == null) return false;
            string[] holders = { "GameAnarchy.Patches.BuildingAIPatch", "GameAnarchy.Patches.BulldozeToolPatch" };
            for (int i = 0; i < holders.Length; i++)
            {
                Type holder = type.Assembly.GetType(holders[i], false);
                FieldInfo field = holder == null ? null : holder.GetField("_modSetting", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) continue;
                try { instance = field.GetValue(null); } catch { instance = null; }
                if (instance != null && type.IsInstanceOfType(instance)) return true;
            }
            return false;
        }

        internal static PropertyInfo[] SharedProperties(Type type)
        {
            List<PropertyInfo> result = new List<PropertyInfo>();
            PropertyInfo[] values = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < values.Length; i++)
            {
                PropertyInfo property = values[i];
                if (!property.CanRead || !property.CanWrite || !Supported(property.PropertyType) || IsLocalOnly(property.Name)) continue;
                result.Add(property);
            }
            result.Sort(delegate(PropertyInfo a, PropertyInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            if (result.Count == 0 || result.Count > 128) throw new InvalidOperationException("Game Anarchy shared settings surface is invalid.");
            return result.ToArray();
        }

        internal static uint SchemaFingerprint(PropertyInfo[] properties)
        {
            uint value = 2166136261u;
            for (int i = 0; i < properties.Length; i++)
            {
                string text = properties[i].Name + ":" + properties[i].PropertyType.FullName + ";";
                for (int p = 0; p < text.Length; p++) { value ^= text[p]; value *= 16777619u; }
            }
            return value;
        }

        internal static long ToBits(Type type, object value)
        {
            if (type.IsEnum) return Convert.ToInt64(value);
            if (type == typeof(bool)) return (bool)value ? 1L : 0L;
            if (type == typeof(float)) return BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0);
            if (type == typeof(uint)) return (long)(uint)value;
            if (type == typeof(long)) return (long)value;
            return Convert.ToInt64(value);
        }

        internal static object FromBits(Type type, long bits)
        {
            if (type.IsEnum) return Enum.ToObject(type, bits);
            if (type == typeof(bool)) return bits != 0;
            if (type == typeof(float)) return BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)bits)), 0);
            if (type == typeof(uint)) return checked((uint)bits);
            if (type == typeof(long)) return bits;
            if (type == typeof(int)) return checked((int)bits);
            throw new InvalidOperationException("Unsupported Game Anarchy setting type: " + type.FullName);
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null || patched || !IsAvailable) return;
            Type settings = ResolveType(SettingsTypeName);
            PropertyInfo[] properties = SharedProperties(settings);
            HarmonyMethod settingPrefix = new HarmonyMethod(typeof(GameAnarchyBridge).GetMethod("SettingWritePrefix", BindingFlags.Static | BindingFlags.NonPublic));
            for (int i = 0; i < properties.Length; i++)
            {
                MethodInfo setter = properties[i].GetSetMethod(true);
                if (setter != null) harmony.Patch(setter, settingPrefix);
            }
            Type economy = ResolveType("GameAnarchy.Managers.ModEconomyManager");
            if (economy != null)
            {
                HarmonyMethod hostPrefix = new HarmonyMethod(typeof(GameAnarchyBridge).GetMethod("HostOnlyMutationPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                for (int i = 0; i < MoneyMutationMethods.Length; i++)
                {
                    MethodInfo[] methods = economy.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int m = 0; m < methods.Length; m++) if (methods[m].Name == MoneyMutationMethods[i]) harmony.Patch(methods[m], hostPrefix);
                }
            }
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; applying = false; }

        private static bool SettingWritePrefix() { return applying || !IsClientReplicaRole(); }
        private static bool HostOnlyMutationPrefix() { return !IsClientReplicaRole(); }
        private static bool IsClientReplicaRole()
        {
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.ClientReplicaLive;
        }
        private static bool IsLocalOnly(string name)
        {
            for (int i = 0; i < LocalOnly.Length; i++) if (name == LocalOnly[i]) return true;
            return false;
        }
        private static bool Supported(Type type)
        {
            return type.IsEnum || type == typeof(bool) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(float);
        }
        private static Type ResolveType(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(name, false); if (type != null) return type;
            }
            return null;
        }
    }
}
