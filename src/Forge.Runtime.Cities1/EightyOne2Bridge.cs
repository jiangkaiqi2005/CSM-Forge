using System;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Shared configuration bridge for 81 Tiles 2. These switches change area/build constraints and
    /// the expanded water/electricity simulation, so Client UI cannot mutate them during a session.
    /// </summary>
    internal sealed class EightyOne2BridgeAdapter : IForgeStateAdapterV1
    {
        private const uint Magic = 0x31313846u; // F811
        internal const string Adapter = "bridge.eightyone2";
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute()
        {
            bool[] values = EightyOne2Bridge.Capture();
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((byte)values.Length);
                for (int i = 0; i < values.Length; i++) writer.Write(values[i]);
                writer.Flush(); return stream.ToArray();
            }
        }

        public void ApplyAbsolute(byte[] state)
        {
            if (state == null || state.Length < 6 || state.Length > 64) throw new InvalidDataException("Invalid 81 Tiles 2 bridge state.");
            bool[] values;
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid 81 Tiles 2 bridge magic.");
                int count = reader.ReadByte();
                if (count != EightyOne2Bridge.PropertyNames.Length) throw new InvalidDataException("81 Tiles 2 bridge setting count mismatch.");
                values = new bool[count]; for (int i = 0; i < count; i++) values[i] = reader.ReadBoolean();
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing 81 Tiles 2 bridge bytes.");
            }
            EightyOne2Bridge.Apply(values);
        }
    }

    internal static class EightyOne2Bridge
    {
        private const string SettingsTypeName = "EightyOne2.ModSettings";
        internal static readonly string[] PropertyNames =
        {
            "XMLIgnoreUnlocking", "XMLCrossTheLine", "XMLNoPowerlines", "XMLElectricRoads",
            "XMLNoPipes", "XMLIgnoreOriginalWater", "XMLIgnoreExpanded"
        };
        private static readonly string[,] GuardedProperties =
        {
            { "EightyOne2.Patches.GameAreaManagerPatches", "IgnoreUnlocking" },
            { "EightyOne2.Patches.GameAreaManagerPatches", "CrossTheLine" },
            { "EightyOne2.Patches.NoPowerlinesPatches", "NoPowerlinesEnabled" },
            { "EightyOne2.Patches.ExpandedElectricityManager", "ElectricRoadsEnabled" },
            { "EightyOne2.Patches.NoPipesPatches", "NoPipesEnabled" },
            { "EightyOne2.Patches.WaterFacilityAIPatches", "IgnoreOriginal" },
            { "EightyOne2.ModSettings", "IgnoreExpanded" }
        };
        [ThreadStatic] private static bool applying;
        private static bool patched;

        internal static bool IsAvailable { get { return ResolveType(SettingsTypeName) != null; } }

        internal static bool[] Capture()
        {
            Type type = ResolveType(SettingsTypeName);
            if (type == null) throw new InvalidOperationException("81 Tiles 2 settings are unavailable.");
            object settings = Activator.CreateInstance(type);
            bool[] values = new bool[PropertyNames.Length];
            for (int i = 0; i < PropertyNames.Length; i++)
            {
                PropertyInfo property = type.GetProperty(PropertyNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(bool) || !property.CanRead)
                    throw new MissingMemberException(SettingsTypeName, PropertyNames[i]);
                values[i] = (bool)property.GetValue(settings, null);
            }
            return values;
        }

        internal static void Apply(bool[] values)
        {
            if (values == null || values.Length != PropertyNames.Length) throw new ArgumentException("Invalid 81 Tiles 2 setting vector.", "values");
            Type type = ResolveType(SettingsTypeName);
            if (type == null) throw new InvalidOperationException("81 Tiles 2 settings are unavailable.");
            object settings = Activator.CreateInstance(type);
            applying = true;
            try
            {
                for (int i = 0; i < PropertyNames.Length; i++)
                {
                    PropertyInfo property = type.GetProperty(PropertyNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property == null || property.PropertyType != typeof(bool) || !property.CanWrite)
                        throw new MissingMemberException(SettingsTypeName, PropertyNames[i]);
                    property.SetValue(settings, values[i], null);
                }
            }
            finally { applying = false; }
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null || patched || !IsAvailable) return;
            MethodInfo prefix = typeof(EightyOne2Bridge).GetMethod("SharedSettingWritePrefix", BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null) throw new MissingMethodException("81 Tiles 2 setting guard prefix is unavailable.");
            HarmonyMethod guard = new HarmonyMethod(prefix);
            for (int i = 0; i < GuardedProperties.GetLength(0); i++)
            {
                Type type = ResolveType(GuardedProperties[i, 0]);
                PropertyInfo property = type == null ? null : type.GetProperty(GuardedProperties[i, 1],
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = property == null ? null : property.GetSetMethod(true);
                if (setter == null) throw new MissingMemberException(GuardedProperties[i, 0], GuardedProperties[i, 1]);
                harmony.Patch(setter, guard);
            }
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; applying = false; }

        private static bool SharedSettingWritePrefix()
        {
            if (applying || RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                role != CitiesRuntimeRole.ClientReplicaLive;
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
