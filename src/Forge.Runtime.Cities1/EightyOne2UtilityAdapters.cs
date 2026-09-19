using CsmForge.Core;
using System;
using System.IO;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    /// <summary>
    /// Host-owned absolute result rows for the 81 Tiles 2 1.0.5 expanded electricity grid.
    /// Native node/segment ids never appear in this payload. In particular, the cell's closest-pipe
    /// segment caches stay local because peers may materialize the same stable Net identity in different slots.
    /// </summary>
    internal sealed class EightyOne2ElectricityGridAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31453138u; // 81E1
        private const int Bytes = 8 + (EightyOne2UtilityAuthority.GridResolution * 9);
        internal const string Adapter = "bridge.eightyone2.electricity";
        private ElectricityManager.Cell[] shadow;
        private ElectricityManager.Cell[] shadowTarget;
        internal static EightyOne2ElectricityGridAdapter Current { get; private set; }
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return EightyOne2UtilityAuthority.GridResolution; } }

        public EightyOne2ElectricityGridAdapter() { Current = this; }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            EightyOne2UtilityAuthority.ValidateShard(context, shardIndex);
            ElectricityManager.Cell[] grid = EightyOne2UtilityAuthority.ElectricityGrid;
            if (!context.IsAuthoritative) grid = EnsureShadow(grid);
            int offset = shardIndex * EightyOne2UtilityAuthority.GridResolution;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((ushort)shardIndex);
                writer.Write((ushort)EightyOne2UtilityAuthority.GridResolution);
                for (int i = 0; i < EightyOne2UtilityAuthority.GridResolution; i++)
                {
                    ElectricityManager.Cell cell = grid[offset + i];
                    writer.Write(cell.m_conductivity);
                    writer.Write(cell.m_currentCharge);
                    writer.Write(cell.m_extraCharge);
                    writer.Write(cell.m_pulseGroup);
                    writer.Write(cell.m_electrified);
                    writer.Write(cell.m_tmpElectrified);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            EightyOne2UtilityAuthority.ValidateShard(context, shardIndex);
            if (state == null || state.Length != Bytes) throw new InvalidDataException("Invalid 81 Tiles electricity row.");
            ElectricityManager.Cell[] grid = EightyOne2UtilityAuthority.ElectricityGrid;
            ElectricityManager.Cell[] projection = EnsureShadow(grid);
            int offset = shardIndex * EightyOne2UtilityAuthority.GridResolution;
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != shardIndex ||
                    reader.ReadUInt16() != EightyOne2UtilityAuthority.GridResolution)
                    throw new InvalidDataException("Invalid 81 Tiles electricity row header.");
                for (int i = 0; i < EightyOne2UtilityAuthority.GridResolution; i++)
                {
                    ElectricityManager.Cell cell = grid[offset + i];
                    cell.m_conductivity = reader.ReadByte();
                    cell.m_currentCharge = reader.ReadInt16();
                    cell.m_extraCharge = reader.ReadUInt16();
                    cell.m_pulseGroup = reader.ReadUInt16();
                    cell.m_electrified = reader.ReadBoolean();
                    cell.m_tmpElectrified = reader.ReadBoolean();
                    grid[offset + i] = cell;
                    projection[offset + i] = cell;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing 81 Tiles electricity row bytes.");
            }
        }

        internal void RestoreClientProjection()
        {
            ElectricityManager.Cell[] grid = EightyOne2UtilityAuthority.TryGetElectricityGrid();
            if (grid != null && shadow != null && ReferenceEquals(grid, shadowTarget)) Array.Copy(shadow, grid, shadow.Length);
        }

        private ElectricityManager.Cell[] EnsureShadow(ElectricityManager.Cell[] grid)
        {
            if (shadow == null || !ReferenceEquals(grid, shadowTarget))
            {
                shadowTarget = grid;
                shadow = (ElectricityManager.Cell[])grid.Clone();
            }
            return shadow;
        }
    }

    /// <summary>Host-owned absolute result rows for water, sewage and heating.</summary>
    internal sealed class EightyOne2WaterGridAdapter : IForgeShardedStateAdapterV1
    {
        private const uint Magic = 0x31573138u; // 81W1
        private const int Bytes = 8 + (EightyOne2UtilityAuthority.GridResolution * 21);
        internal const string Adapter = "bridge.eightyone2.water";
        private WaterManager.Cell[] shadow;
        private WaterManager.Cell[] shadowTarget;
        internal static EightyOne2WaterGridAdapter Current { get; private set; }
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return EightyOne2UtilityAuthority.GridResolution; } }

        public EightyOne2WaterGridAdapter() { Current = this; }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            EightyOne2UtilityAuthority.ValidateShard(context, shardIndex);
            WaterManager.Cell[] grid = EightyOne2UtilityAuthority.WaterGrid;
            if (!context.IsAuthoritative) grid = EnsureShadow(grid);
            int offset = shardIndex * EightyOne2UtilityAuthority.GridResolution;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write((ushort)shardIndex);
                writer.Write((ushort)EightyOne2UtilityAuthority.GridResolution);
                for (int i = 0; i < EightyOne2UtilityAuthority.GridResolution; i++)
                {
                    WaterManager.Cell cell = grid[offset + i];
                    writer.Write(cell.m_conductivity);
                    writer.Write(cell.m_conductivity2);
                    writer.Write(cell.m_currentWaterPressure);
                    writer.Write(cell.m_currentSewagePressure);
                    writer.Write(cell.m_currentHeatingPressure);
                    writer.Write(cell.m_waterPulseGroup);
                    writer.Write(cell.m_sewagePulseGroup);
                    writer.Write(cell.m_heatingPulseGroup);
                    writer.Write(cell.m_hasWater);
                    writer.Write(cell.m_hasSewage);
                    writer.Write(cell.m_hasHeating);
                    writer.Write(cell.m_tmpHasWater);
                    writer.Write(cell.m_tmpHasSewage);
                    writer.Write(cell.m_tmpHasHeating);
                    writer.Write(cell.m_pollution);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            EightyOne2UtilityAuthority.ValidateShard(context, shardIndex);
            if (state == null || state.Length != Bytes) throw new InvalidDataException("Invalid 81 Tiles water row.");
            WaterManager.Cell[] grid = EightyOne2UtilityAuthority.WaterGrid;
            WaterManager.Cell[] projection = EnsureShadow(grid);
            int offset = shardIndex * EightyOne2UtilityAuthority.GridResolution;
            using (MemoryStream stream = new MemoryStream(state, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != shardIndex ||
                    reader.ReadUInt16() != EightyOne2UtilityAuthority.GridResolution)
                    throw new InvalidDataException("Invalid 81 Tiles water row header.");
                for (int i = 0; i < EightyOne2UtilityAuthority.GridResolution; i++)
                {
                    WaterManager.Cell cell = grid[offset + i];
                    cell.m_conductivity = reader.ReadByte();
                    cell.m_conductivity2 = reader.ReadByte();
                    cell.m_currentWaterPressure = reader.ReadInt16();
                    cell.m_currentSewagePressure = reader.ReadInt16();
                    cell.m_currentHeatingPressure = reader.ReadInt16();
                    cell.m_waterPulseGroup = reader.ReadUInt16();
                    cell.m_sewagePulseGroup = reader.ReadUInt16();
                    cell.m_heatingPulseGroup = reader.ReadUInt16();
                    cell.m_hasWater = reader.ReadBoolean();
                    cell.m_hasSewage = reader.ReadBoolean();
                    cell.m_hasHeating = reader.ReadBoolean();
                    cell.m_tmpHasWater = reader.ReadBoolean();
                    cell.m_tmpHasSewage = reader.ReadBoolean();
                    cell.m_tmpHasHeating = reader.ReadBoolean();
                    cell.m_pollution = reader.ReadByte();
                    grid[offset + i] = cell;
                    projection[offset + i] = cell;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing 81 Tiles water row bytes.");
            }
        }

        internal void RestoreClientProjection()
        {
            WaterManager.Cell[] grid = EightyOne2UtilityAuthority.TryGetWaterGrid();
            if (grid != null && shadow != null && ReferenceEquals(grid, shadowTarget)) Array.Copy(shadow, grid, shadow.Length);
        }

        private WaterManager.Cell[] EnsureShadow(WaterManager.Cell[] grid)
        {
            if (shadow == null || !ReferenceEquals(grid, shadowTarget))
            {
                shadowTarget = grid;
                shadow = (WaterManager.Cell[])grid.Clone();
            }
            return shadow;
        }
    }

    internal static class EightyOne2UtilityAuthority
    {
        internal const int GridResolution = 462;
        private const int GridCells = GridResolution * GridResolution;
        private static readonly FieldInfo ElectricityGridField = AccessTools.Field(typeof(ElectricityManager), "m_electricityGrid");
        private static readonly FieldInfo WaterGridField = AccessTools.Field(typeof(WaterManager), "m_waterGrid");
        private static bool patched;

        internal static ElectricityManager.Cell[] ElectricityGrid
        {
            get
            {
                ElectricityManager.Cell[] value = ElectricityGridField == null ? null :
                    ElectricityGridField.GetValue(Singleton<ElectricityManager>.instance) as ElectricityManager.Cell[];
                if (value == null || value.Length != GridCells) throw new InvalidOperationException("81 Tiles electricity grid is not expanded to 462x462.");
                return value;
            }
        }

        internal static ElectricityManager.Cell[] TryGetElectricityGrid()
        {
            if (!Singleton<ElectricityManager>.exists || ElectricityGridField == null) return null;
            ElectricityManager.Cell[] value = ElectricityGridField.GetValue(Singleton<ElectricityManager>.instance) as ElectricityManager.Cell[];
            return value != null && value.Length == GridCells ? value : null;
        }

        internal static WaterManager.Cell[] WaterGrid
        {
            get
            {
                WaterManager.Cell[] value = WaterGridField == null ? null :
                    WaterGridField.GetValue(Singleton<WaterManager>.instance) as WaterManager.Cell[];
                if (value == null || value.Length != GridCells) throw new InvalidOperationException("81 Tiles water grid is not expanded to 462x462.");
                return value;
            }
        }

        internal static WaterManager.Cell[] TryGetWaterGrid()
        {
            if (!Singleton<WaterManager>.exists || WaterGridField == null) return null;
            WaterManager.Cell[] value = WaterGridField.GetValue(Singleton<WaterManager>.instance) as WaterManager.Cell[];
            return value != null && value.Length == GridCells ? value : null;
        }

        internal static bool ValidateSurface(Assembly assembly)
        {
            if (assembly == null || ElectricityGridField == null || WaterGridField == null) return false;
            Type electricity = assembly.GetType("EightyOne2.Patches.ExpandedElectricityManager", false);
            Type water = assembly.GetType("EightyOne2.Patches.ExpandedWaterManager", false);
            MethodInfo electricityStep = electricity == null ? null : electricity.GetMethod("SimulationStepImpl", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo waterStep = water == null ? null : water.GetMethod("SimulationStepImpl", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo gameElectricityStep = AccessTools.Method(typeof(ElectricityManager), "SimulationStepImpl", new[] { typeof(int) });
            MethodInfo gameWaterStep = AccessTools.Method(typeof(WaterManager), "SimulationStepImpl", new[] { typeof(int) });
            return MatchesElectricityStep(electricityStep) && MatchesWaterStep(waterStep) &&
                gameElectricityStep != null && gameWaterStep != null;
        }

        private static bool MatchesElectricityStep(MethodInfo method)
        {
            if (method == null || method.ReturnType != typeof(void)) return false;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 9 || parameters[0].ParameterType != typeof(ElectricityManager) ||
                parameters[1].ParameterType != typeof(int) || parameters[2].ParameterType != typeof(ElectricityManager.Cell[]) ||
                parameters[8].ParameterType != typeof(bool).MakeByRefType()) return false;
            for (int i = 3; i < 8; i++) if (parameters[i].ParameterType != typeof(int).MakeByRefType()) return false;
            return true;
        }

        private static bool MatchesWaterStep(MethodInfo method)
        {
            if (method == null || method.ReturnType != typeof(void)) return false;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 18 || parameters[0].ParameterType != typeof(WaterManager) ||
                parameters[1].ParameterType != typeof(int) || parameters[2].ParameterType != typeof(WaterManager.Cell[]) ||
                parameters[14].ParameterType != typeof(bool).MakeByRefType()) return false;
            for (int i = 3; i < 14; i++) if (parameters[i].ParameterType != typeof(int).MakeByRefType()) return false;
            for (int i = 15; i < 18; i++) if (parameters[i].ParameterType != typeof(WaterManager.PulseGroup[])) return false;
            return true;
        }

        internal static void InstallPatches(Harmony harmony)
        {
            if (patched) return;
            MethodInfo prefix = typeof(EightyOne2UtilityAuthority).GetMethod("ClientUtilityStepPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo electricity = AccessTools.Method(typeof(ElectricityManager), "SimulationStepImpl", new[] { typeof(int) });
            MethodInfo water = AccessTools.Method(typeof(WaterManager), "SimulationStepImpl", new[] { typeof(int) });
            if (prefix == null || electricity == null || water == null) throw new MissingMethodException("81 Tiles utility authority patch surface is unavailable.");
            HarmonyMethod guard = new HarmonyMethod(prefix);
            harmony.Patch(electricity, guard);
            harmony.Patch(water, guard);
            patched = true;
        }

        internal static void ResetPatchState() { patched = false; }

        internal static void RestoreClientProjection()
        {
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                role != CitiesRuntimeRole.ClientReplicaLive) return;
            EightyOne2ElectricityGridAdapter electricity = EightyOne2ElectricityGridAdapter.Current;
            EightyOne2WaterGridAdapter water = EightyOne2WaterGridAdapter.Current;
            if (electricity != null) electricity.RestoreClientProjection();
            if (water != null) water.RestoreClientProjection();
        }

        internal static void ValidateShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            Check.NotNull(context, "context");
            if (shardIndex < 0 || shardIndex >= GridResolution) throw new ArgumentOutOfRangeException("shardIndex");
        }

        private static bool ClientUtilityStepPrefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role != CitiesRuntimeRole.ClientLoading && role != CitiesRuntimeRole.ClientRecovering &&
                role != CitiesRuntimeRole.ClientReplicaLive;
        }
    }
}
