using System;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class DemandControllerBridgeState
    {
        public bool Enabled;
        public int ResidentialDemand;
        public bool ResidentialEnabled;
        public int CommercialDemand;
        public bool CommercialEnabled;
        public int WorkplaceDemand;
        public bool WorkplaceEnabled;
    }

    /// <summary>
    /// Built-in bridge for thanasip/DemandController (Workshop 2916710759).
    /// The mod's seven static control fields are Host-owned absolute state. Actual ZoneManager
    /// demand values continue to be replicated by Forge's Demand authority domain.
    /// </summary>
    internal sealed class DemandControllerBridgeAdapter : IForgeStateAdapterV1
    {
        private const uint Magic = 0x31434446u; // FDC1
        internal const string Adapter = "bridge.demandcontroller";

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute()
        {
            DemandControllerBridgeState value;
            if (!DemandControllerBridge.TryCapture(out value))
                throw new InvalidOperationException("Demand Controller bridge became unavailable.");
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(value.Enabled);
                writer.Write((byte)value.ResidentialDemand); writer.Write(value.ResidentialEnabled);
                writer.Write((byte)value.CommercialDemand); writer.Write(value.CommercialEnabled);
                writer.Write((byte)value.WorkplaceDemand); writer.Write(value.WorkplaceEnabled);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void ApplyAbsolute(byte[] state)
        {
            if (state == null || state.Length == 0 || state.Length > 64) throw new InvalidDataException("Invalid Demand Controller bridge state.");
            DemandControllerBridgeState value;
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid Demand Controller bridge magic.");
                value = new DemandControllerBridgeState
                {
                    Enabled = reader.ReadBoolean(),
                    ResidentialDemand = reader.ReadByte(), ResidentialEnabled = reader.ReadBoolean(),
                    CommercialDemand = reader.ReadByte(), CommercialEnabled = reader.ReadBoolean(),
                    WorkplaceDemand = reader.ReadByte(), WorkplaceEnabled = reader.ReadBoolean()
                };
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Demand Controller bridge bytes.");
            }
            DemandControllerBridge.Apply(value);
        }
    }

    internal static class DemandControllerBridge
    {
        private const string TypeName = "DemandController.DemandControllerExtension";
        private static readonly string[] Fields =
        {
            "Enabled", "ResidentialDemand", "ResidentialEnabled", "CommercialDemand", "CommercialEnabled",
            "WorkplaceDemand", "WorkplaceEnabled"
        };

        internal static bool IsAvailable { get { return ResolveType() != null; } }

        internal static bool TryCapture(out DemandControllerBridgeState value)
        {
            value = null;
            Type type = ResolveType();
            if (type == null) return false;
            try
            {
                value = new DemandControllerBridgeState
                {
                    Enabled = Read<bool>(type, Fields[0]),
                    ResidentialDemand = ReadDemand(type, Fields[1]),
                    ResidentialEnabled = Read<bool>(type, Fields[2]),
                    CommercialDemand = ReadDemand(type, Fields[3]),
                    CommercialEnabled = Read<bool>(type, Fields[4]),
                    WorkplaceDemand = ReadDemand(type, Fields[5]),
                    WorkplaceEnabled = Read<bool>(type, Fields[6])
                };
                return true;
            }
            catch { value = null; return false; }
        }

        internal static void Apply(DemandControllerBridgeState value)
        {
            if (value == null) throw new ArgumentNullException("value");
            ValidateDemand(value.ResidentialDemand); ValidateDemand(value.CommercialDemand); ValidateDemand(value.WorkplaceDemand);
            Type type = ResolveType();
            if (type == null) throw new InvalidOperationException("Demand Controller is not loaded.");
            Write(type, Fields[0], value.Enabled);
            Write(type, Fields[1], value.ResidentialDemand); Write(type, Fields[2], value.ResidentialEnabled);
            Write(type, Fields[3], value.CommercialDemand); Write(type, Fields[4], value.CommercialEnabled);
            Write(type, Fields[5], value.WorkplaceDemand); Write(type, Fields[6], value.WorkplaceEnabled);

            ZoneManager zone = ZoneManager.instance;
            if (zone != null && value.Enabled)
            {
                if (value.ResidentialEnabled) zone.m_residentialDemand = value.ResidentialDemand;
                if (value.CommercialEnabled) zone.m_commercialDemand = value.CommercialDemand;
                if (value.WorkplaceEnabled) zone.m_workplaceDemand = value.WorkplaceDemand;
            }
        }

        internal static MethodInfo ResolveRefresh()
        {
            Type type = ResolveType();
            return type == null ? null : type.GetMethod("Refresh", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        }

        private static Type ResolveType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(TypeName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static T Read<T>(Type type, string name)
        {
            FieldInfo field = RequiredField(type, name);
            object value = field.GetValue(null);
            if (!(value is T)) throw new InvalidOperationException("Demand Controller field type changed: " + name);
            return (T)value;
        }

        private static int ReadDemand(Type type, string name)
        {
            int value = Read<int>(type, name); ValidateDemand(value); return value;
        }

        private static void Write(Type type, string name, object value)
        {
            RequiredField(type, name).SetValue(null, value);
        }

        private static FieldInfo RequiredField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(TypeName, name);
            return field;
        }

        private static void ValidateDemand(int value)
        {
            if (value < 0 || value > 100) throw new InvalidDataException("Demand Controller value is outside 0..100.");
        }
    }

    internal static class KnownModBridgeRegistry
    {
        private static readonly object Gate = new object();
        private static bool demandControllerPatched;

        internal static void RegisterAvailable()
        {
            if (!DemandControllerBridge.IsAvailable) return;
            string[] adapters = ForgeExtensionApi.RegisteredAdapterIds;
            for (int i = 0; i < adapters.Length; i++) if (adapters[i] == DemandControllerBridgeAdapter.Adapter) return;
            ForgeExtensionApi.Register(new DemandControllerBridgeAdapter());
        }

        internal static void InstallOptionalPatches(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            MethodInfo original = DemandControllerBridge.ResolveRefresh();
            if (original == null) return;
            lock (Gate)
            {
                if (demandControllerPatched) return;
                MethodInfo prefix = typeof(KnownModBridgeRegistry).GetMethod("DemandControllerRefreshPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null) throw new MissingMethodException("Demand Controller bridge prefix is unavailable.");
                harmony.Patch(original, new HarmonyMethod(prefix));
                demandControllerPatched = true;
            }
        }

        internal static void ResetOptionalPatchState()
        {
            lock (Gate) demandControllerPatched = false;
        }

        private static bool DemandControllerRefreshPrefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.ClientLoading || role == CitiesRuntimeRole.ClientRecovering ||
                role == CitiesRuntimeRole.ClientReplicaLive) return false;
            return role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.SinglePlayer ||
                role == CitiesRuntimeRole.HostPreparing || role == CitiesRuntimeRole.HostLive ||
                role == CitiesRuntimeRole.Unloading;
        }
    }
}
