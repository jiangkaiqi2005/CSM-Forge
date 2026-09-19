using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Infinite Goods is authoritative on the Host because its original TransferMonitor indexes the
    /// process-local Building array directly. Clients keep a shadow of the Host configuration for
    /// projection/root verification but never execute the original material-buffer mutation loop.
    /// </summary>
    internal sealed class InfiniteGoodsBridgeAdapter : IForgeStateAdapterV1
    {
        private const uint Magic = 0x31474946u; // FIG1
        internal const string Adapter = "bridge.infinitegoods";
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute()
        {
            byte[] shadow = InfiniteGoodsBridge.ClientShadow;
            if (InfiniteGoodsBridge.IsClientReplicaRole && shadow != null) return (byte[])shadow.Clone();
            return InfiniteGoodsBridge.Capture();
        }

        public void ApplyAbsolute(byte[] state)
        {
            byte[] normalized = InfiniteGoodsBridge.ValidateAndNormalize(state);
            if (InfiniteGoodsBridge.IsClientReplicaRole)
            {
                InfiniteGoodsBridge.ClientShadow = normalized;
                return;
            }
            InfiniteGoodsBridge.ApplyToMod(normalized);
        }
    }

    internal static class InfiniteGoodsBridge
    {
        private const string AssemblyName = "InfiniteGoodsMod";
        private const string SupportedProductVersion = "6.1";
        private const string SettingsTypeName = "InfiniteGoodsMod.Settings.Settings";
        private const string SettingIdTypeName = "InfiniteGoodsMod.Settings.SettingId";
        private const string TransferMonitorTypeName = "InfiniteGoodsMod.Transfer.TransferMonitor";
        private static readonly string[] ExpectedSettingNames =
        {
            "CommercialGoods", "CommercialLuxuryProducts", "SpecializedIndustryOil", "SpecializedIndustryOre",
            "SpecializedIndustryGrain", "SpecializedIndustryLogs", "GenericIndustryPetrol", "GenericIndustryCoal",
            "GenericIndustryFood", "GenericIndustryLumber", "ShelterGoods", "WarehouseOil", "WarehouseOre",
            "WarehouseGrain", "WarehouseLogs", "PloppedIndustryOil", "PloppedIndustryOre", "PloppedIndustryGrain",
            "PloppedIndustryLogs", "UniqueIndustryAnimalProducts", "UniqueIndustryFlours", "UniqueIndustryPaper",
            "UniqueIndustryPlanedTimber", "UniqueIndustryPetroleum", "UniqueIndustryPlastics", "UniqueIndustryGlass",
            "UniqueIndustryMetals", "UniqueIndustryGrain", "FishingHarbor", "FishingFarm", "FishingMarket",
            "FishingProcessing", "PowerPlantCoal", "PowerPlantOil", "PedestrianServicePointGoods",
            "PedestrianServicePointLuxuryProducts", "CargoServicePointSpecializedIndustryOil",
            "CargoServicePointSpecializedIndustryOre", "CargoServicePointSpecializedIndustryGrain",
            "CargoServicePointSpecializedIndustryLogs", "CargoServicePointGenericIndustryPetrol",
            "CargoServicePointGenericIndustryCoal", "CargoServicePointGenericIndustryFood",
            "CargoServicePointGenericIndustryLumber", "Debug"
        };
        private static readonly string[] UnsupportedServicePointSettings = ModCompatibilityCatalog.Default.InfiniteGoods.UnsupportedServicePointSettings;
        private static byte[] clientShadow;
        private static bool patched;
        private static Assembly compatibleAssembly;

        internal static byte[] ClientShadow
        {
            get { return clientShadow == null ? null : (byte[])clientShadow.Clone(); }
            set { clientShadow = value == null ? null : (byte[])value.Clone(); }
        }

        internal static bool IsAvailable
        {
            get { return FindCompatibleAssembly() != null; }
        }

        internal static bool IsClientReplicaRole
        {
            get
            {
                CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
                return role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                    role == CitiesRuntimeRole.ClientReplicaLive;
            }
        }

        internal static byte[] Capture()
        {
            Type settingsType = ResolveType(SettingsTypeName);
            Type settingIdType = ResolveType(SettingIdTypeName);
            if (settingsType == null || settingIdType == null || !settingIdType.IsEnum)
                throw new InvalidOperationException("Infinite Goods settings surface is unavailable.");
            object settings = GetSettings(settingsType);
            PropertyInfo indexer = RequiredIndexer(settingsType, settingIdType);
            object[] ids = SortedIds(settingIdType);
            ValidateSupportedConfiguration(settingIdType, settings, indexer);
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(0x31474946u);
                writer.Write(SchemaFingerprint(settingIdType, ids));
                writer.Write((ushort)ids.Length);
                for (int i = 0; i < ids.Length; i++) writer.Write((bool)indexer.GetValue(settings, new[] { ids[i] }));
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static byte[] ValidateAndNormalize(byte[] state)
        {
            if (state == null || state.Length < 10 || state.Length > 4096)
                throw new InvalidDataException("Invalid Infinite Goods bridge state.");
            Type settingIdType = ResolveType(SettingIdTypeName);
            if (settingIdType == null || !settingIdType.IsEnum) throw new InvalidOperationException("Infinite Goods SettingId is unavailable.");
            object[] ids = SortedIds(settingIdType);
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x31474946u || reader.ReadUInt32() != SchemaFingerprint(settingIdType, ids))
                    throw new InvalidDataException("Infinite Goods bridge schema mismatch.");
                if (reader.ReadUInt16() != ids.Length) throw new InvalidDataException("Infinite Goods setting count mismatch.");
                bool[] values = new bool[ids.Length];
                for (int i = 0; i < values.Length; i++) values[i] = reader.ReadBoolean();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Infinite Goods bridge bytes.");
                for (int i = 0; i < ids.Length; i++)
                    if (values[i] && IsUnsupportedServicePoint(Enum.GetName(settingIdType, ids[i])))
                        throw new InvalidDataException("Infinite Goods state enables an unsupported service-point transfer.");
                using (MemoryStream output = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(output))
                {
                    writer.Write(0x31474946u); writer.Write(SchemaFingerprint(settingIdType, ids)); writer.Write((ushort)ids.Length);
                    for (int i = 0; i < values.Length; i++) writer.Write(values[i]);
                    writer.Flush(); return output.ToArray();
                }
            }
        }

        internal static void ApplyToMod(byte[] state)
        {
            byte[] normalized = ValidateAndNormalize(state);
            Type settingsType = ResolveType(SettingsTypeName);
            Type settingIdType = ResolveType(SettingIdTypeName);
            object settings = GetSettings(settingsType);
            PropertyInfo indexer = RequiredIndexer(settingsType, settingIdType);
            object[] ids = SortedIds(settingIdType);
            using (MemoryStream stream = new MemoryStream(normalized, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16();
                for (int i = 0; i < ids.Length; i++) indexer.SetValue(settings, reader.ReadBoolean(), new[] { ids[i] });
            }
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null || patched || !IsAvailable) return;
            Type monitor = ResolveType(TransferMonitorTypeName);
            MethodInfo tick = monitor == null ? null : monitor.GetMethod("OnAfterSimulationTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (tick == null) throw new MissingMethodException(TransferMonitorTypeName, "OnAfterSimulationTick");
            MethodInfo prefix = typeof(InfiniteGoodsBridge).GetMethod("HostOnlyTickPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(tick, new HarmonyMethod(prefix));
            patched = true;
        }

        internal static void ResetPatchState()
        {
            patched = false;
            clientShadow = null;
            compatibleAssembly = null;
        }

        private static bool HostOnlyTickPrefix()
        {
            return !IsClientReplicaRole;
        }

        private static object GetSettings(Type settingsType)
        {
            if (settingsType == null) throw new InvalidOperationException("Infinite Goods Settings type is unavailable.");
            MethodInfo get = settingsType.GetMethod("GetInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (get == null) throw new MissingMethodException(SettingsTypeName, "GetInstance");
            object value = get.Invoke(null, null);
            if (value == null) throw new InvalidOperationException("Infinite Goods Settings instance is unavailable.");
            return value;
        }

        private static PropertyInfo RequiredIndexer(Type settingsType, Type settingIdType)
        {
            PropertyInfo property = settingsType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, typeof(bool), new[] { settingIdType }, null);
            if (property == null || !property.CanRead || !property.CanWrite)
                throw new MissingMemberException(SettingsTypeName, "Item[SettingId]");
            return property;
        }

        private static object[] SortedIds(Type enumType)
        {
            Array raw = Enum.GetValues(enumType);
            if (raw.Length != ExpectedSettingNames.Length) throw new InvalidOperationException("Infinite Goods SettingId surface drifted.");
            List<object> values = new List<object>();
            for (int i = 0; i < raw.Length; i++)
            {
                object value = raw.GetValue(i);
                if (Convert.ToInt64(value) != i || !StringComparer.Ordinal.Equals(Enum.GetName(enumType, value), ExpectedSettingNames[i]))
                    throw new InvalidOperationException("Infinite Goods SettingId surface drifted.");
                if (StringComparer.Ordinal.Equals(Enum.GetName(enumType, value), "Debug")) continue;
                values.Add(value);
            }
            values.Sort(delegate(object a, object b) { return Convert.ToInt64(a).CompareTo(Convert.ToInt64(b)); });
            return values.ToArray();
        }

        private static uint SchemaFingerprint(Type enumType, object[] ids)
        {
            uint value = 2166136261u;
            for (int i = 0; i < ids.Length; i++)
            {
                string text = Enum.GetName(enumType, ids[i]) + "=" + Convert.ToInt64(ids[i]) + ";";
                for (int p = 0; p < text.Length; p++) { value ^= text[p]; value *= 16777619u; }
            }
            return value;
        }

        private static void ValidateSupportedConfiguration(Type enumType, object settings, PropertyInfo indexer)
        {
            // S2: aggregate like GameAnarchyBridge — report every unsupported service point at once.
            List<string> violations = new List<string>();
            for (int i = 0; i < UnsupportedServicePointSettings.Length; i++)
            {
                object value = Enum.Parse(enumType, UnsupportedServicePointSettings[i], false);
                if ((bool)indexer.GetValue(settings, new[] { value }))
                    violations.Add(UnsupportedServicePointSettings[i]);
            }
            if (violations.Count != 0)
                throw new InvalidOperationException("Infinite Goods options are not supported in Forge multiplayer: " +
                    string.Join(", ", violations.ToArray()));
        }

        private static bool IsUnsupportedServicePoint(string name)
        {
            for (int i = 0; i < UnsupportedServicePointSettings.Length; i++)
                if (StringComparer.Ordinal.Equals(name, UnsupportedServicePointSettings[i])) return true;
            return false;
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
                if (!StringComparer.Ordinal.Equals(name.Name, AssemblyName) || name.Version == null ||
                    name.Version.Major != 6 || name.Version.Minor != 0) continue;
                Type identity = assemblies[i].GetType("InfiniteGoodsMod.ModIdentity", false);
                Type settings = assemblies[i].GetType(SettingsTypeName, false);
                Type settingId = assemblies[i].GetType(SettingIdTypeName, false);
                Type monitor = assemblies[i].GetType(TransferMonitorTypeName, false);
                Type definition = assemblies[i].GetType("InfiniteGoodsMod.Transfer.TransferDefinition`1", false);
                if (identity == null || settings == null || settingId == null || monitor == null || definition == null || !settingId.IsEnum) continue;
                try
                {
                    FieldInfo version = identity.GetField("Version", BindingFlags.Static | BindingFlags.Public);
                    if (version == null || !version.IsLiteral || !StringComparer.Ordinal.Equals((string)version.GetRawConstantValue(), SupportedProductVersion)) continue;
                    RequiredIndexer(settings, settingId);
                    SortedIds(settingId);
                    RequiredMethod(monitor, "OnAfterSimulationTick", Type.EmptyTypes);
                    Type closedDefinition = definition.MakeGenericType(typeof(BuildingAI));
                    RequiredMethod(closedDefinition, "TransferIfMatch", new[] { typeof(ushort), typeof(bool) });
                }
                catch { continue; }
                compatibleAssembly = assemblies[i];
                return compatibleAssembly;
            }
            return null;
        }

        private static MethodInfo RequiredMethod(Type type, string name, Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
