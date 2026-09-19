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
        internal const string Adapter = "bridge.gameanarchy";
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute()
        {
            Type type; object instance;
            if (!GameAnarchyBridge.TryGetSettings(out type, out instance))
                throw new InvalidOperationException("Game Anarchy settings are unavailable.");
            return GameAnarchyBridge.GetSettingsCodec(type).Capture(instance);
        }

        public void ApplyAbsolute(byte[] state)
        {
            Type type; object instance;
            if (!GameAnarchyBridge.TryGetSettings(out type, out instance))
                throw new InvalidOperationException("Game Anarchy settings are unavailable.");
            SettingsSurfaceCodec codec = GameAnarchyBridge.GetSettingsCodec(type);
            long[] values = codec.ValidateState(state);
            GameAnarchyBridge.BeginApply();
            try { codec.WriteValues(instance, values); }
            finally { GameAnarchyBridge.EndApply(); }
        }
    }

    internal static class GameAnarchyBridge
    {
        private static readonly string AssemblyName = ModCompatibilityCatalog.Default.GameAnarchy.AssemblyName;
        private static readonly Version SupportedVersion = new Version(ModCompatibilityCatalog.Default.GameAnarchy.SupportedVersion);
        private static readonly string SettingsTypeName = ModCompatibilityCatalog.Default.GameAnarchy.SettingsTypeName;
        private static readonly string[] MoneyMutationMethods = { "OnPreSimulationFrame", "ChargeInterest", "AutoAddMoney", "SetStartMoney", "AddMoneyManually", "SubstrateMoneyManually", "ModifyMoney", "AddLoanAmount" };
        [ThreadStatic] private static bool applying;
        private static bool patched;
        private static Assembly compatibleAssembly;
        private static SettingsSurfaceCodec settingsCodec;

        internal static bool IsAvailable { get { return FindCompatibleAssembly() != null; } }
        internal static void BeginApply() { applying = true; }
        internal static void EndApply() { applying = false; }

        internal static bool TryGetSettings(out Type type, out object instance)
        {
            type = ResolveType(SettingsTypeName); instance = null;
            if (type == null) return false;
            string[] holders = ModCompatibilityCatalog.Default.GameAnarchy.HolderTypeNames;
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

        internal static SettingsSurfaceCodec GetSettingsCodec(Type settingsType)
        {
            // WP-3.2: the surface selection, wire capture and aggregated validation now live in
            // the shared codec; only the catalog data and the fixed-value labels are GA-specific.
            if (settingsCodec == null)
            {
                string bothRates = "OilDepletionRate/OreDepletionRate (both rates must be 100)";
                string fireSpread = "BuildingSpreadFireProbability/TreeSpreadFireProbability (fire-spread overrides)";
                string unlock = "CurrentUnlockMode/CurrentMilestoneLevel (milestone/unlock overrides)";
                settingsCodec = new SettingsSurfaceCodec(
                    settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                    ModCompatibilityCatalog.Default.GameAnarchy.LocalOnlySettings,
                    ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings,
                    new[]
                    {
                        new FixedValueRule("OilDepletionRate", ModCompatibilityCatalog.Default.GameAnarchy.FixedOilDepletionRate, bothRates),
                        new FixedValueRule("OreDepletionRate", ModCompatibilityCatalog.Default.GameAnarchy.FixedOilDepletionRate, bothRates),
                        new FixedValueRule("BuildingSpreadFireProbability", ModCompatibilityCatalog.Default.GameAnarchy.FixedSpreadFireProbability, fireSpread),
                        new FixedValueRule("TreeSpreadFireProbability", ModCompatibilityCatalog.Default.GameAnarchy.FixedSpreadFireProbability, fireSpread),
                        new FixedValueRule("CurrentUnlockMode", 0L, unlock),
                        new FixedValueRule("CurrentMilestoneLevel", 0L, unlock)
                    },
                    0x31414746u, 4096, "Game Anarchy");
            }
            return settingsCodec;
        }

        internal static PropertyInfo[] SharedProperties(Type type)
        {
            return GetSettingsCodec(type).SharedProperties;
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
                if (setter != null) harmony.Patch(DeclaredMethod(setter), settingPrefix);
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

        private static MethodInfo DeclaredMethod(MethodInfo method)
        {
            if (method == null || method.DeclaringType == null) return method;
            ParameterInfo[] parameters = method.GetParameters();
            Type[] types = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) types[i] = parameters[i].ParameterType;
            return method.DeclaringType.GetMethod(method.Name, BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, types, null) ?? method;
        }

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
            string[] localOnly = ModCompatibilityCatalog.Default.GameAnarchy.LocalOnlySettings;
            for (int i = 0; i < localOnly.Length; i++) if (name == localOnly[i]) return true;
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
                // The audited-type names above are pinned in ModCompatibilityCatalog (D-gate).
                for (int r = 0; r < ModCompatibilityCatalog.Default.GameAnarchy.RequiredTypeNames.Length; r++)
                    if (assemblies[i].GetType(ModCompatibilityCatalog.Default.GameAnarchy.RequiredTypeNames[r], false) == null) { economy = null; break; }
                if (settings == null || economy == null || city == null || resources == null || milestones == null || fire == null) continue;
                try
                {
                    PropertyInfo[] properties = SharedProperties(settings);
                    string[] blocked = ModCompatibilityCatalog.Default.GameAnarchy.UnsupportedBooleanSettings;
                    for (int p = 0; p < blocked.Length; p++) RequiredProperty(properties, blocked[p]);
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
