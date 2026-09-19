using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public enum SessionPlayerRoleV2 : byte { Host = 1, Client = 2 }
    public enum SessionPlayerPhaseV2 : byte { Joining = 1, Live = 2 }

    public sealed class SessionPlayerV2
    {
        public MemberIdentity Member { get; private set; }
        public string DisplayName { get; private set; }
        public SessionPlayerRoleV2 Role { get; private set; }
        public SessionPlayerPhaseV2 Phase { get; private set; }

        public SessionPlayerV2(MemberIdentity member, string displayName, SessionPlayerRoleV2 role,
            SessionPlayerPhaseV2 phase)
        {
            if (!member.IsValid || !SocialMessagesV2.IsBoundedText(displayName, 32, 64, false) ||
                !Enum.IsDefined(typeof(SessionPlayerRoleV2), role) ||
                !Enum.IsDefined(typeof(SessionPlayerPhaseV2), phase))
                throw new ArgumentException("Invalid session player.");
            Member = member; DisplayName = displayName; Role = role; Phase = phase;
        }
    }

    public sealed class RosterSnapshotV2
    {
        private readonly SessionPlayerV2[] players;
        public SessionPlayerV2[] Players { get { return (SessionPlayerV2[])players.Clone(); } }
        public RosterSnapshotV2(SessionPlayerV2[] values)
        {
            if (values == null || values.Length == 0 || values.Length > Limits.Peers + 1)
                throw new ArgumentException("Invalid roster size.", "values");
            players = (SessionPlayerV2[])values.Clone();
        }
    }

    public sealed class ChatSubmitV2
    {
        public string Text { get; private set; }
        public ChatSubmitV2(string text)
        {
            if (!SocialMessagesV2.IsBoundedText(text, 256, 512, false))
                throw new ArgumentException("Invalid chat text.", "text");
            Text = text;
        }
    }

    public sealed class ChatEventV2
    {
        public MemberIdentity Member { get; private set; }
        public string DisplayName { get; private set; }
        public string Text { get; private set; }
        public ChatEventV2(MemberIdentity member, string displayName, string text)
        {
            if (!member.IsValid || !SocialMessagesV2.IsBoundedText(displayName, 32, 64, false) ||
                !SocialMessagesV2.IsBoundedText(text, 256, 512, false)) throw new ArgumentException("Invalid chat event.");
            Member = member; DisplayName = displayName; Text = text;
        }
    }

    public sealed class PlayerPresentationV2
    {
        public MemberIdentity Member { get; private set; }
        public string DisplayName { get; private set; }
        public string ToolName { get; private set; }
        public float WorldX { get; private set; }
        public float WorldY { get; private set; }
        public float WorldZ { get; private set; }
        public bool Visible { get; private set; }

        public PlayerPresentationV2(MemberIdentity member, string displayName, string toolName,
            float worldX, float worldY, float worldZ, bool visible)
        {
            if (!member.IsValid || !SocialMessagesV2.IsBoundedText(displayName, 32, 64, false) ||
                !SocialMessagesV2.IsBoundedText(toolName, 96, 192, true) || float.IsNaN(worldX) || float.IsInfinity(worldX) ||
                float.IsNaN(worldY) || float.IsInfinity(worldY) || float.IsNaN(worldZ) || float.IsInfinity(worldZ))
                throw new ArgumentException("Invalid player presentation.");
            Member = member; DisplayName = displayName; ToolName = toolName;
            WorldX = worldX; WorldY = worldY; WorldZ = worldZ; Visible = visible;
        }
    }

    public static class SocialMessagesV2
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static bool IsBoundedText(string value, int maximumCharacters, int maximumBytes, bool allowEmpty)
        {
            if (value == null || (!allowEmpty && value.Length == 0) || value.Length > maximumCharacters) return false;
            try { return StrictUtf8.GetByteCount(value) <= maximumBytes; }
            catch (EncoderFallbackException) { return false; }
        }

        public static byte[] EncodeRoster(RosterSnapshotV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                SessionPlayerV2[] players = value.Players;
                writer.Write((byte)players.Length);
                for (int i = 0; i < players.Length; i++)
                {
                    WriteMember(writer, players[i].Member); WriteString(writer, players[i].DisplayName, 64);
                    writer.Write((byte)players[i].Role); writer.Write((byte)players[i].Phase);
                }
                writer.Flush(); return stream.ToArray();
            }
        }

        public static RosterSnapshotV2 DecodeRoster(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 1 + (Limits.Peers + 1) * 88))
            {
                int count = reader.ReadByte();
                if (count == 0 || count > Limits.Peers + 1) throw new InvalidDataException("Invalid roster size.");
                List<SessionPlayerV2> players = new List<SessionPlayerV2>();
                for (int i = 0; i < count; i++)
                    players.Add(new SessionPlayerV2(ReadMember(reader), ReadString(reader, 64),
                        (SessionPlayerRoleV2)reader.ReadByte(), (SessionPlayerPhaseV2)reader.ReadByte()));
                EnsureEnd(reader); return new RosterSnapshotV2(players.ToArray());
            }
        }

        public static byte[] EncodeChatSubmit(ChatSubmitV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            { BinaryWriter writer = new BinaryWriter(stream); WriteString(writer, value.Text, 512); writer.Flush(); return stream.ToArray(); }
        }

        public static ChatSubmitV2 DecodeChatSubmit(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 514))
            { ChatSubmitV2 value = new ChatSubmitV2(ReadString(reader, 512)); EnsureEnd(reader); return value; }
        }

        public static byte[] EncodeChatEvent(ChatEventV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); WriteMember(writer, value.Member);
                WriteString(writer, value.DisplayName, 64); WriteString(writer, value.Text, 512);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static ChatEventV2 DecodeChatEvent(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 602))
            {
                ChatEventV2 value = new ChatEventV2(ReadMember(reader), ReadString(reader, 64), ReadString(reader, 512));
                EnsureEnd(reader); return value;
            }
        }

        public static byte[] EncodePresentation(PlayerPresentationV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); WriteMember(writer, value.Member);
                WriteString(writer, value.DisplayName, 64); WriteString(writer, value.ToolName, 192);
                writer.Write(value.WorldX); writer.Write(value.WorldY); writer.Write(value.WorldZ);
                writer.Write(value.Visible ? (byte)1 : (byte)0); writer.Flush(); return stream.ToArray();
            }
        }

        public static PlayerPresentationV2 DecodePresentation(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 296))
            {
                MemberIdentity member = ReadMember(reader); string name = ReadString(reader, 64);
                string tool = ReadString(reader, 192); float x = reader.ReadSingle(); float y = reader.ReadSingle(); float z = reader.ReadSingle();
                byte visible = reader.ReadByte(); if (visible > 1) throw new InvalidDataException("Invalid presentation visibility.");
                EnsureEnd(reader); return new PlayerPresentationV2(member, name, tool, x, y, z, visible == 1);
            }
        }

        private static void WriteMember(BinaryWriter writer, MemberIdentity member)
        { if (!member.IsValid) throw new ArgumentException("Invalid member."); writer.Write(member.MemberId.ToByteArray()); writer.Write(member.Generation); }
        private static MemberIdentity ReadMember(BinaryReader reader)
        { byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new MemberIdentity(new Guid(bytes), reader.ReadUInt32()); }
        private static void WriteString(BinaryWriter writer, string value, int maximumBytes)
        { byte[] bytes = StrictUtf8.GetBytes(value); if (bytes.Length > maximumBytes) throw new ArgumentException("UTF-8 string is too long."); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadString(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadUInt16();
            if (length > maximumBytes) throw new InvalidDataException("UTF-8 string is too long.");
            byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            try { return StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Social text is not valid UTF-8.", error); }
        }
        private static BinaryReader Reader(byte[] bytes, int maximum)
        { if (bytes == null || bytes.Length > maximum) throw new InvalidDataException("Social payload length is invalid."); return new BinaryReader(new MemoryStream(bytes, false)); }
        private static void EnsureEnd(BinaryReader reader)
        { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing social payload bytes."); }
    }
}
