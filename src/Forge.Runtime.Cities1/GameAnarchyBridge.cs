using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Host-owned shared-simulation settings for the audited Game Anarchy 1.3.1 build. UI-only
    /// settings and keybindings are intentionally excluded. Settings whose persistent writes do not
    /// yet have a Forge authority domain are rejected explicitly instead of being allowed to diverge.
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
            GameAnarchyBridge.ValidateSupportedConfiguration(properties, instance);
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
                long[] values = new long[properties.Length];
                for (int i = 0; i < properties.Length; i++) values[i] = reader.ReadInt64();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Game Anarchy bridge bytes.");
                GameAnarchyBridge.ValidateSupportedValues(properties, values);
                GameAnarchyBridge.BeginApply();
                try
                {
                    for (int i = 0; i < properties.Length; i++)
                        properties[i].SetValue(instance, GameAnarchyBridge.FromBits(properties[i].PropertyType, values[i]), null);
                }
                finally { GameAnarchyBridge.EndApply(); }
            }
        }
    }

    internal static class GameAnarchyBridge
    {
        private const string AssemblyName = "GameAnarchy";
        private static readonly Version SupportedVersion = new Version(1, 3, 1, 0);
        private const string SettingsTypeName = "GameAnarchy.ModSettings.ModSetting";
        private static readonly string[] LocalOnly = { "AchievementSystemEnabled", "SkipIntroEnabled", "OptionsPanelCategoriesHorizontalOffset", "OptionsPanelCategoriesUpdated" };
        private static readonly string[] MoneyMutationMethods = { "OnPreSimulationFrame", "ChargeInterest", "AutoAddMoney", "SetStartMoney", "AddMoneyManually", "SubstrateMoneyManually", "ModifyMoney", "AddLoanAmount" };
        private static readonly string[] UnsupportedBooleanSettings =
        {
            "UnlockInfoViews", "UnlockBasicRoads", "UnlockAllRoads", "UnlockTrainTrack", "UnlockMetroTrack",
            "UnlockPolicies", "UnlockPublicTransport", "UnlockUniqueBuildings", "UnlockLandscaping",
            "RemoveNoisePollution", "RemoveGroundPollution", "RemoveWaterPollution", "RemoveDeath",
            "RemoveGarbage", "RemoveCrime", "MaximizeAttractiveness", "MaximizeEntertainment",
            "MaximizeLandValue", "MaximizeEducationCoverage", "MaximizeFireCoverage",
            "RemovePlayerBuildingFire", "RemoveResidentialBuildingFire", "RemoveIndustrialBuildingFire",
            "RemoveCommercialBuildingFire", "RemoveOfficeBuildingFire", "RemoveParkBuildingFire",
            "RemoveMuseumFire", "RemoveCampusBuildingFire", "RemoveAirportBuildingFire"
        };
        [ThreadStatic] private static bool applying;
        private static bool patched;
        private static Assembly compatibleAssembly;

        internal static bool IsAvailable { get { return FindCompatibleAssembly() != null; } }
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

        internal static void ValidateSupportedConfiguration(PropertyInfo[] properties, object instance)
        {
            long[] values = new long[properties.Length];
            for (int i = 0; i < properties.Length; i++) values[i] = ToBits(properties[i].PropertyType, properties[i].GetValue(instance, null));
            ValidateSupportedValues(properties, values);
        }

        internal static void ValidateSupportedValues(PropertyInfo[] properties, long[] values)
        {
            if (properties == null || values == null || properties.Length != values.Length)
                throw new InvalidDataException("Invalid Game Anarchy shared setting values.");
            for (int i = 0; i < UnsupportedBooleanSettings.Length; i++)
                if (RequiredValue(properties, values, UnsupportedBooleanSettings[i]) != 0L)
                    throw new InvalidOperationException("Game Anarchy option is not supported in Forge multiplayer: " + UnsupportedBooleanSettings[i]);
            if (RequiredValue(properties, values, "CurrentUnlockMode") != 0L || RequiredValue(properties, values, "CurrentMilestoneLevel") != 0L)
                throw new InvalidOperationException("Game Anarchy milestone/unlock mutation is not supported in Forge multiplayer.");
            if (RequiredValue(properties, values, "OilDepletionRate") != 100L || RequiredValue(properties, values, "OreDepletionRate") != 100L)
                throw new InvalidOperationException("Game Anarchy oil/ore depletion overrides are not supported; both rates must be 100 for multiplayer.");
            if (RequiredValue(properties, values, "BuildingSpreadFireProbability") != 0L ||
                RequiredValue(properties, values, "TreeSpreadFireProbability") != 0L)
                throw new InvalidOperationException("Game Anarchy fire-spread overrides are not supported in Forge multiplayer.");
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
            Type fire = ResolveType("GameAnarchy.Managers.FireControlManager");
            MethodInfo fireProbability = RequiredMethod(fire, "GetFireProbability", new[] { typeof(uint), typeof(uint).MakeByRefType(), typeof(uint).MakeByRefType() });
            MethodInfo putOut = RequiredMethod(fire, "PutOutBurningBuildings", Type.EmptyTypes);
            harmony.Patch(fireProbability, new HarmonyMethod(typeof(GameAnarchyBridge).GetMethod("FireProbabilityPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(putOut, new HarmonyMethod(typeof(GameAnarchyBridge).GetMethod("UnsupportedManualFirePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; applying = false; compatibleAssembly = null; }

        private static bool SettingWritePrefix() { return applying || !IsClientReplicaRole(); }
        private static bool HostOnlyMutationPrefix() { return !IsClientReplicaRole(); }
        private static bool FireProbabilityPrefix(ref bool __result)
        {
            if (!IsMultiplayerRole()) return true;
            __result = true;
            return false;
        }
        private static bool UnsupportedManualFirePrefix() { return !IsMultiplayerRole(); }
        private static bool IsClientReplicaRole()
        {
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering || role == CitiesRuntimeRole.ClientReplicaLive;
        }
        private static bool IsMultiplayerRole()
        {
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive ||
                role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                role == CitiesRuntimeRole.ClientReplicaLive || role == CitiesRuntimeRole.WorldFenced;
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
            Assembly assembly = FindCompatibleAssembly();
            return assembly == null ? null : assembly.GetType(name, false);
        }

        private static Assembly FindCompatibleAssembly()
        {
            if (compatibleAssembly != null) return compatibleAssembly;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyName name = assemblies[i].GetName();
                if (!StringComparer.Ordinal.Equals(name.Name, AssemblyName) || name.Version != SupportedVersion) continue;
                Type settings = assemblies[i].GetType(SettingsTypeName, false);
                Type economy = assemblies[i].GetType("GameAnarchy.Managers.ModEconomyManager", false);
                Type city = assemblies[i].GetType("GameAnarchy.Managers.CityServicesManager", false);
                Type resources = assemblies[i].GetType("GameAnarchy.Extension.OilAndOreResourceExtension", false);
                Type milestones = assemblies[i].GetType("GameAnarchy.Extension.MilestonesExtension", false);
                Type fire = assemblies[i].GetType("GameAnarchy.Managers.FireControlManager", false);
                if (settings == null || economy == null || city == null || resources == null || milestones == null || fire == null) continue;
                try
                {
                    PropertyInfo[] properties = SharedProperties(settings);
                    for (int p = 0; p < UnsupportedBooleanSettings.Length; p++) RequiredProperty(properties, UnsupportedBooleanSettings[p]);
                    RequiredProperty(properties, "CurrentUnlockMode"); RequiredProperty(properties, "CurrentMilestoneLevel");
                    RequiredProperty(properties, "OilDepletionRate"); RequiredProperty(properties, "OreDepletionRate");
                    RequiredProperty(properties, "BuildingSpreadFireProbability"); RequiredProperty(properties, "TreeSpreadFireProbability");
                    RequiredMethod(city, "OnPostSimulationFrame", Type.EmptyTypes);
                    RequiredMethod(resources, "OnAfterResourcesModified", new[] { typeof(int), typeof(int), RequiredType("ICities.NaturalResource"), typeof(int) });
                    RequiredMethod(milestones, "OnRefreshMilestones", Type.EmptyTypes);
                    RequiredMethod(fire, "GetFireProbability", new[] { typeof(uint), typeof(uint).MakeByRefType(), typeof(uint).MakeByRefType() });
                    RequiredMethod(fire, "PutOutBurningBuildings", Type.EmptyTypes);
                }
                catch { continue; }
                compatibleAssembly = assemblies[i];
                return compatibleAssembly;
            }
            return null;
        }

        private static Type RequiredType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }
            throw new TypeLoadException(fullName);
        }

        private static PropertyInfo RequiredProperty(PropertyInfo[] properties, string name)
        {
            for (int i = 0; i < properties.Length; i++) if (properties[i].Name == name) return properties[i];
            throw new MissingMemberException(SettingsTypeName, name);
        }

        private static long RequiredValue(PropertyInfo[] properties, long[] values, string name)
        {
            for (int i = 0; i < properties.Length; i++) if (properties[i].Name == name) return values[i];
            throw new MissingMemberException(SettingsTypeName, name);
        }

        private static MethodInfo RequiredMethod(Type type, string name, Type[] parameters)
        {
            if (type == null) throw new TypeLoadException(name);
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
