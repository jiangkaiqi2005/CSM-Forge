using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal enum DistrictParkControlKind : byte
    {
        AcademicStaff = 1,
        CheerleadingBudget = 2,
        TicketPrice = 3,
        VarsityIdentity = 4,
        VarsityColor = 5,
        SetParkPolicy = 6,
        UnsetParkPolicy = 7
    }

    /// <summary>
    /// Interactive control surface shared by Parklife/Industries/Campus/Airports-style DistrictPark
    /// entities. Network identity is always the stable EntityIdentityV2 owned by builtin.districtpark.
    /// </summary>
    internal sealed class DistrictParkControlsAdapter : IForgeInteractiveStateAdapterV1
    {
        private const uint StateMagic = 0x31435046u; // FPC1
        private const uint IntentMagic = 0x31495046u; // FPI1
        internal const string IdentityNamespace = "builtin.districtpark";
        internal const string Adapter = "builtin.districtpark-controls";
        private static readonly MethodInfo SetVarsityIdentityMethod = typeof(DistrictPark).GetMethod(
            "SetVarsityIdentity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo SetVarsityColorMethod = typeof(DistrictPark).GetMethod(
            "SetVarsityColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }

        public byte[] CaptureAbsolute(IForgeAdapterContextV1 context)
        {
            if (context == null) throw new ArgumentNullException("context");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            EntityMapEntryV2[] mappings = ids.SnapshotEntries();
            List<ControlState> values = new List<ControlState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                if (mappings[i].NativeId == 0 || mappings[i].NativeId > byte.MaxValue) continue;
                byte native = (byte)mappings[i].NativeId;
                if (!Live(native)) continue;
                DistrictPark park = manager.m_parks.m_buffer[native];
                Color32 color = park.m_varsityColor;
                values.Add(new ControlState
                {
                    Identity = mappings[i].Identity,
                    ParkType = (int)park.m_parkType,
                    ParkLevel = (int)park.m_parkLevel,
                    AcademicStaff = park.m_academicStaffCount,
                    CheerleadingBudget = park.m_cheerleadingBudget,
                    CoachCount = park.m_coachCount,
                    TicketPrice = park.m_ticketPrice,
                    VarsityIdentity = park.m_varsityIdentityIndex,
                    VarsityColor = color,
                    GrantType = park.m_grantType,
                    ParkPolicies = Convert.ToUInt64(park.m_parkPolicies),
                    EventPolicies = Convert.ToUInt64(park.m_eventPolicies)
                });
            }
            values.Sort(delegate(ControlState a, ControlState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            return EncodeState(values);
        }

        public void ApplyAbsolute(IForgeAdapterContextV1 context, byte[] state)
        {
            if (context == null || state == null) throw new ArgumentNullException("context");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            ControlState[] values = DecodeState(state);
            for (int i = 0; i < values.Length; i++)
            {
                ControlState value = values[i];
                uint nativeValue;
                byte native;
                if (!ids.TryGetNative(value.Identity, out nativeValue))
                {
                    if (context.IsAuthoritative)
                        throw new InvalidOperationException("Host DistrictPark control state lost its stable identity mapping.");
                    if (!manager.CreatePark(out native, (DistrictPark.ParkType)value.ParkType,
                        (DistrictPark.ParkLevel)value.ParkLevel) || native == 0)
                        throw new InvalidOperationException("CS1 could not materialize a DistrictPark control target.");
                    ids.BindKnown(value.Identity, native);
                }
                else
                {
                    if (nativeValue == 0 || nativeValue > byte.MaxValue) throw new InvalidOperationException("DistrictPark control mapping exceeds byte range.");
                    native = (byte)nativeValue;
                }
                if (!Live(native)) throw new InvalidOperationException("DistrictPark control target is not live.");
                Install(native, value);
            }
        }

        public bool ExecuteIntent(IForgeAdapterContextV1 context, byte[] intent)
        {
            if (context == null || !context.IsAuthoritative || intent == null) return false;
            ControlIntent request;
            try { request = DecodeIntent(intent); }
            catch { return false; }
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(IdentityNamespace);
            uint nativeValue;
            if (!ids.TryGetNative(request.Target, out nativeValue) || nativeValue == 0 || nativeValue > byte.MaxValue)
                return false;
            byte native = (byte)nativeValue;
            if (!Live(native)) return false;
            DistrictManager manager = DistrictManager.instance;
            DistrictPark park = manager.m_parks.m_buffer[native];

            switch (request.Kind)
            {
                case DistrictParkControlKind.AcademicStaff:
                    if (request.Value < 0 || request.Value > byte.MaxValue) return false;
                    park.m_academicStaffCount = (byte)request.Value;
                    manager.m_parks.m_buffer[native] = park;
                    return true;
                case DistrictParkControlKind.CheerleadingBudget:
                    if (request.Value < 0) return false;
                    int oldBonus = park.CalculateCheerleadingAttractivenessBonus();
                    park.m_cheerleadingBudget = request.Value;
                    int newBonus = park.CalculateCheerleadingAttractivenessBonus();
                    if (newBonus != oldBonus) park.ModifyStaticAttractiveness(newBonus - oldBonus);
                    manager.m_parks.m_buffer[native] = park;
                    return true;
                case DistrictParkControlKind.TicketPrice:
                    if (request.Value < 0 || request.Value > ushort.MaxValue) return false;
                    park.m_ticketPrice = (ushort)request.Value;
                    manager.m_parks.m_buffer[native] = park;
                    return true;
                case DistrictParkControlKind.VarsityIdentity:
                    if (request.Value < 0 || request.Value > byte.MaxValue) return false;
                    park.m_varsityIdentityIndex = request.Value;
                    park.m_eventPolicies &= ~(DistrictPolicies.Event.Team01Ad | DistrictPolicies.Event.Team02Ad |
                        DistrictPolicies.Event.Team03Ad | DistrictPolicies.Event.Team04Ad | DistrictPolicies.Event.Team05Ad |
                        DistrictPolicies.Event.Team06Ad | DistrictPolicies.Event.Team07Ad);
                    park.m_eventPolicies |= DistrictPark.TeamToEventPolicy((DistrictPark.Team)request.Value);
                    manager.m_parks.m_buffer[native] = park;
                    InvokeStructSideEffect(SetVarsityIdentityMethod, native, request.Value);
                    return true;
                case DistrictParkControlKind.VarsityColor:
                    park.m_varsityColor = request.Color;
                    manager.m_parks.m_buffer[native] = park;
                    InvokeStructSideEffect(SetVarsityColorMethod, native, request.Color);
                    return true;
                case DistrictParkControlKind.SetParkPolicy:
                    manager.SetParkPolicy((DistrictPolicies.Policies)request.Value, native);
                    return true;
                case DistrictParkControlKind.UnsetParkPolicy:
                    manager.UnsetParkPolicy((DistrictPolicies.Policies)request.Value, native);
                    return true;
                default:
                    return false;
            }
        }

        internal static byte[] EncodeIntIntent(DistrictParkControlKind kind, EntityIdentityV2 target, int value)
        {
            return EncodeIntent(new ControlIntent { Kind = kind, Target = target, Value = value, Color = new Color32(0, 0, 0, 0) });
        }

        internal static byte[] EncodeColorIntent(EntityIdentityV2 target, Color32 color)
        {
            return EncodeIntent(new ControlIntent { Kind = DistrictParkControlKind.VarsityColor, Target = target, Value = 0, Color = color });
        }

        private static void Install(byte native, ControlState value)
        {
            DistrictManager manager = DistrictManager.instance;
            DistrictPark park = manager.m_parks.m_buffer[native];
            if ((int)park.m_parkType != value.ParkType || (int)park.m_parkLevel != value.ParkLevel)
                manager.SetParkTypeLevel(native, (DistrictPark.ParkType)value.ParkType, (DistrictPark.ParkLevel)value.ParkLevel);
            park = manager.m_parks.m_buffer[native];
            int oldBonus = park.CalculateCheerleadingAttractivenessBonus();
            park.m_academicStaffCount = value.AcademicStaff;
            park.m_cheerleadingBudget = value.CheerleadingBudget;
            park.m_coachCount = value.CoachCount;
            park.m_ticketPrice = value.TicketPrice;
            park.m_varsityIdentityIndex = value.VarsityIdentity;
            park.m_varsityColor = value.VarsityColor;
            park.m_grantType = value.GrantType;
            park.m_parkPolicies = (DistrictPolicies.Park)value.ParkPolicies;
            park.m_eventPolicies = (DistrictPolicies.Event)value.EventPolicies;
            int newBonus = park.CalculateCheerleadingAttractivenessBonus();
            if (newBonus != oldBonus) park.ModifyStaticAttractiveness(newBonus - oldBonus);
            manager.m_parks.m_buffer[native] = park;
        }

        private static void InvokeStructSideEffect(MethodInfo method, byte native, object value)
        {
            if (method == null) return;
            DistrictPark park = DistrictManager.instance.m_parks.m_buffer[native];
            object boxed = park;
            try
            {
                method.Invoke(boxed, new object[] { native, value });
                DistrictManager.instance.m_parks.m_buffer[native] = (DistrictPark)boxed;
            }
            catch { }
        }

        private static bool Live(byte native)
        {
            return native != 0 && DistrictManager.instance != null &&
                (DistrictManager.instance.m_parks.m_buffer[native].m_flags & DistrictPark.Flags.Created) != DistrictPark.Flags.None;
        }

        private static byte[] EncodeState(List<ControlState> values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(StateMagic);
                writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    ControlState v = values[i];
                    writer.Write(v.Identity.EntityId); writer.Write(v.Identity.Generation);
                    writer.Write(v.ParkType); writer.Write(v.ParkLevel);
                    writer.Write(v.AcademicStaff); writer.Write(v.CheerleadingBudget); writer.Write(v.CoachCount);
                    writer.Write(v.TicketPrice); writer.Write(v.VarsityIdentity);
                    writer.Write(v.VarsityColor.r); writer.Write(v.VarsityColor.g); writer.Write(v.VarsityColor.b); writer.Write(v.VarsityColor.a);
                    writer.Write(v.GrantType); writer.Write(v.ParkPolicies); writer.Write(v.EventPolicies);
                }
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("DistrictPark controls state exceeds one Forge frame.");
                return stream.ToArray();
            }
        }

        private static ControlState[] DecodeState(byte[] bytes)
        {
            if (bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("DistrictPark controls state is too large.");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != StateMagic) throw new InvalidDataException("Invalid DistrictPark controls state magic.");
                int count = reader.ReadUInt16();
                ControlState[] result = new ControlState[count];
                ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 identity = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (identity.EntityId <= previous) throw new InvalidDataException("DistrictPark controls are not canonically ordered.");
                    previous = identity.EntityId;
                    result[i] = new ControlState
                    {
                        Identity = identity,
                        ParkType = reader.ReadInt32(), ParkLevel = reader.ReadInt32(),
                        AcademicStaff = reader.ReadByte(), CheerleadingBudget = reader.ReadInt32(), CoachCount = reader.ReadByte(),
                        TicketPrice = reader.ReadUInt16(), VarsityIdentity = reader.ReadInt32(),
                        VarsityColor = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                        GrantType = reader.ReadByte(), ParkPolicies = reader.ReadUInt64(), EventPolicies = reader.ReadUInt64()
                    };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing DistrictPark controls bytes.");
                return result;
            }
        }

        private static byte[] EncodeIntent(ControlIntent request)
        {
            if (!request.Target.IsValid) throw new ArgumentException("DistrictPark control intent target is missing.");
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(IntentMagic); writer.Write((byte)request.Kind);
                writer.Write(request.Target.EntityId); writer.Write(request.Target.Generation); writer.Write(request.Value);
                writer.Write(request.Color.r); writer.Write(request.Color.g); writer.Write(request.Color.b); writer.Write(request.Color.a);
                writer.Flush(); return stream.ToArray();
            }
        }

        private static ControlIntent DecodeIntent(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != IntentMagic) throw new InvalidDataException("Invalid DistrictPark control intent magic.");
                DistrictParkControlKind kind = (DistrictParkControlKind)reader.ReadByte();
                if (kind < DistrictParkControlKind.AcademicStaff || kind > DistrictParkControlKind.UnsetParkPolicy)
                    throw new InvalidDataException("Invalid DistrictPark control intent kind.");
                ControlIntent result = new ControlIntent
                {
                    Kind = kind,
                    Target = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()),
                    Value = reader.ReadInt32(),
                    Color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte())
                };
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing DistrictPark control intent bytes.");
                return result;
            }
        }

        private sealed class ControlState
        {
            public EntityIdentityV2 Identity;
            public int ParkType;
            public int ParkLevel;
            public byte AcademicStaff;
            public int CheerleadingBudget;
            public byte CoachCount;
            public ushort TicketPrice;
            public int VarsityIdentity;
            public Color32 VarsityColor;
            public byte GrantType;
            public ulong ParkPolicies;
            public ulong EventPolicies;
        }

        private sealed class ControlIntent
        {
            public DistrictParkControlKind Kind;
            public EntityIdentityV2 Target;
            public int Value;
            public Color32 Color;
        }
    }
}
