using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public enum BootstrapAuthMode : byte
    {
        DevelopmentRoomKeyOnly = 0,
        AuthenticatedTransport = 1
    }

    public sealed class HelloV2
    {
        public Guid ClientInstanceId { get; private set; }
        public string DisplayName { get; private set; }
        public Hash256 GameBuildHash { get; private set; }
        public Hash256 SchemaHash { get; private set; }
        public ushort ManifestPageCount { get; private set; }
        public BootstrapAuthMode AuthMode { get; private set; }

        public HelloV2(Guid clientInstanceId, string displayName, Hash256 gameBuildHash,
            Hash256 schemaHash, ushort manifestPageCount, BootstrapAuthMode authMode)
        {
            if (clientInstanceId == Guid.Empty) throw new ArgumentException("Missing client instance id.", "clientInstanceId");
            if (string.IsNullOrEmpty(displayName) || Encoding.UTF8.GetByteCount(displayName) > 64)
                throw new ArgumentException("Invalid display name.", "displayName");
            if (gameBuildHash == null || schemaHash == null) throw new ArgumentNullException("gameBuildHash");
            if (manifestPageCount == 0 || manifestPageCount > 256) throw new ArgumentOutOfRangeException("manifestPageCount");
            if (!Enum.IsDefined(typeof(BootstrapAuthMode), authMode)) throw new ArgumentOutOfRangeException("authMode");
            ClientInstanceId = clientInstanceId;
            DisplayName = displayName;
            GameBuildHash = gameBuildHash;
            SchemaHash = schemaHash;
            ManifestPageCount = manifestPageCount;
            AuthMode = authMode;
        }
    }

    public sealed class ManifestPageV2
    {
        public ushort Index { get; private set; }
        public ushort Total { get; private set; }
        public ComponentFingerprint[] Entries { get; private set; }

        public ManifestPageV2(ushort index, ushort total, ComponentFingerprint[] entries)
        {
            if (total == 0 || total > 256 || index >= total) throw new ArgumentOutOfRangeException("index");
            if (entries == null || entries.Length > 128) throw new ArgumentException("Invalid manifest page entries.", "entries");
            Index = index;
            Total = total;
            Entries = (ComponentFingerprint[])entries.Clone();
        }
    }

    public sealed class CompatibilityResultV2
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }

        public CompatibilityResultV2(bool accepted, string reason)
        {
            if (reason == null) reason = string.Empty;
            if (Encoding.UTF8.GetByteCount(reason) > 256) throw new ArgumentException("Compatibility reason is too long.", "reason");
            Accepted = accepted;
            Reason = reason;
        }
    }

    public sealed class SessionWelcomeV2
    {
        public SessionStamp Stamp { get; private set; }
        public Guid ConnectionBinding { get; private set; }
        public MemberIdentity Member { get; private set; }
        public ulong PermissionVersion { get; private set; }
        public ulong Revision { get; private set; }
        public Hash256 Root { get; private set; }

        public SessionWelcomeV2(SessionStamp stamp, Guid connectionBinding, MemberIdentity member,
            ulong permissionVersion, ulong revision, Hash256 root)
        {
            if (!stamp.IsValid || connectionBinding == Guid.Empty || !member.IsValid || permissionVersion == 0 || root == null)
                throw new ArgumentException("Session welcome identity is incomplete.");
            Stamp = stamp;
            ConnectionBinding = connectionBinding;
            Member = member;
            PermissionVersion = permissionVersion;
            Revision = revision;
            Root = root;
        }
    }

    public static class BootstrapMessagesV2
    {
        public static byte[] EncodeHello(HelloV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.ClientInstanceId.ToByteArray());
                WriteString(writer, value.DisplayName, 64);
                writer.Write(value.GameBuildHash.ToArray());
                writer.Write(value.SchemaHash.ToArray());
                writer.Write(value.ManifestPageCount);
                writer.Write((byte)value.AuthMode);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static HelloV2 DecodeHello(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 16 + 2 + 64 + 32 + 32 + 2 + 1))
            {
                Guid client = ReadGuid(reader);
                string name = ReadString(reader, 64);
                Hash256 game = ReadHash(reader);
                Hash256 schema = ReadHash(reader);
                ushort pages = reader.ReadUInt16();
                BootstrapAuthMode mode = (BootstrapAuthMode)reader.ReadByte();
                EnsureEnd(reader);
                return new HelloV2(client, name, game, schema, pages, mode);
            }
        }

        public static byte[] EncodeIdentityProof(BootstrapAuthMode mode, Guid clientInstanceId)
        {
            if (!Enum.IsDefined(typeof(BootstrapAuthMode), mode) || clientInstanceId == Guid.Empty)
                throw new ArgumentException("Invalid identity proof.");
            byte[] result = new byte[17];
            result[0] = (byte)mode;
            Buffer.BlockCopy(clientInstanceId.ToByteArray(), 0, result, 1, 16);
            return result;
        }

        public static void DecodeIdentityProof(byte[] bytes, out BootstrapAuthMode mode, out Guid clientInstanceId)
        {
            if (bytes == null || bytes.Length != 17) throw new InvalidDataException("Invalid identity proof length.");
            mode = (BootstrapAuthMode)bytes[0];
            if (!Enum.IsDefined(typeof(BootstrapAuthMode), mode)) throw new InvalidDataException("Unknown identity proof mode.");
            byte[] id = new byte[16];
            Buffer.BlockCopy(bytes, 1, id, 0, 16);
            clientInstanceId = new Guid(id);
            if (clientInstanceId == Guid.Empty) throw new InvalidDataException("Missing identity proof instance id.");
        }

        public static byte[] EncodeManifestPage(ManifestPageV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Index);
                writer.Write(value.Total);
                writer.Write((ushort)value.Entries.Length);
                foreach (ComponentFingerprint entry in value.Entries)
                {
                    if (entry == null) throw new ArgumentException("Null manifest entry.");
                    WriteString(writer, entry.Id, 128);
                    writer.Write(entry.BinaryHash.ToArray());
                    writer.Write(entry.ConfigurationHash.ToArray());
                }
                writer.Flush();
                byte[] result = stream.ToArray();
                if (result.Length > BootstrapCodec.MaxPayloadBytes)
                    throw new InvalidOperationException("Manifest page exceeds bootstrap payload limit.");
                return result;
            }
        }

        public static ManifestPageV2 DecodeManifestPage(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, BootstrapCodec.MaxPayloadBytes))
            {
                ushort index = reader.ReadUInt16();
                ushort total = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                if (count > 128) throw new InvalidDataException("Manifest page entry count is excessive.");
                ComponentFingerprint[] entries = new ComponentFingerprint[count];
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string id = ReadString(reader, 128);
                    if (!ids.Add(id)) throw new InvalidDataException("Duplicate component in one manifest page.");
                    entries[i] = new ComponentFingerprint(id, ReadHash(reader), ReadHash(reader));
                }
                EnsureEnd(reader);
                return new ManifestPageV2(index, total, entries);
            }
        }

        public static ManifestPageV2[] CreateManifestPages(CompatibilityManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            List<ManifestPageV2> pages = new List<ManifestPageV2>();
            List<ComponentFingerprint> current = new List<ComponentFingerprint>();
            int currentBytes = 6;
            foreach (ComponentFingerprint entry in manifest.Entries)
            {
                int entryBytes = 2 + Encoding.UTF8.GetByteCount(entry.Id) + 64;
                if (entryBytes + 6 > BootstrapCodec.MaxPayloadBytes)
                    throw new InvalidOperationException("One manifest entry cannot fit in a bootstrap page.");
                if (current.Count > 0 && (current.Count == 128 || currentBytes + entryBytes > BootstrapCodec.MaxPayloadBytes))
                {
                    pages.Add(new ManifestPageV2((ushort)pages.Count, 1, current.ToArray()));
                    current.Clear();
                    currentBytes = 6;
                }
                current.Add(entry);
                currentBytes += entryBytes;
            }
            if (current.Count > 0 || pages.Count == 0)
                pages.Add(new ManifestPageV2((ushort)pages.Count, 1, current.ToArray()));
            if (pages.Count > 256) throw new InvalidOperationException("Manifest requires too many pages.");
            ManifestPageV2[] result = new ManifestPageV2[pages.Count];
            for (int i = 0; i < pages.Count; i++)
                result[i] = new ManifestPageV2((ushort)i, (ushort)pages.Count, pages[i].Entries);
            return result;
        }

        public static byte[] EncodeCompatibilityResult(CompatibilityResultV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write((byte)(value.Accepted ? 1 : 0));
                WriteString(writer, value.Reason, 256);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static CompatibilityResultV2 DecodeCompatibilityResult(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 259))
            {
                byte accepted = reader.ReadByte();
                if (accepted > 1) throw new InvalidDataException("Invalid compatibility result flag.");
                string reason = ReadString(reader, 256);
                EnsureEnd(reader);
                return new CompatibilityResultV2(accepted == 1, reason);
            }
        }

        public static byte[] EncodeWelcome(SessionWelcomeV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Stamp.WorldId.ToByteArray());
                writer.Write(value.Stamp.Epoch);
                writer.Write(value.ConnectionBinding.ToByteArray());
                writer.Write(value.Member.MemberId.ToByteArray());
                writer.Write(value.Member.Generation);
                writer.Write(value.PermissionVersion);
                writer.Write(value.Revision);
                writer.Write(value.Root.ToArray());
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static SessionWelcomeV2 DecodeWelcome(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 100))
            {
                SessionStamp stamp = new SessionStamp(ReadGuid(reader), reader.ReadUInt64());
                Guid binding = ReadGuid(reader);
                MemberIdentity member = new MemberIdentity(ReadGuid(reader), reader.ReadUInt32());
                ulong permission = reader.ReadUInt64();
                ulong revision = reader.ReadUInt64();
                Hash256 root = ReadHash(reader);
                EnsureEnd(reader);
                return new SessionWelcomeV2(stamp, binding, member, permission, revision, root);
            }
        }

        private static BinaryReader Reader(byte[] bytes, int maximum)
        {
            if (bytes == null || bytes.Length > maximum) throw new InvalidDataException("Payload length is invalid.");
            return new BinaryReader(new MemoryStream(bytes, false));
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            Guid value = new Guid(bytes);
            if (value == Guid.Empty) throw new InvalidDataException("Missing UUID.");
            return value;
        }

        private static Hash256 ReadHash(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Hash256.Size);
            if (bytes.Length != Hash256.Size) throw new EndOfStreamException();
            return new Hash256(bytes);
        }

        private static void WriteString(BinaryWriter writer, string value, int maxBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes) throw new ArgumentException("String is too long.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader, int maxBytes)
        {
            ushort length = reader.ReadUInt16();
            if (length > maxBytes) throw new InvalidDataException("String length is excessive.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void EnsureEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Unexpected trailing payload bytes.");
        }
    }

    public static class SessionMessagesV2
    {
        public static byte[] EncodeIntent(PlayerIntentV2 value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Member.MemberId.ToByteArray());
                writer.Write(value.Member.Generation);
                writer.Write(value.OperationCounter);
                writer.Write(value.PermissionVersion);
                writer.Write(value.DomainId);
                writer.Write(value.ExpectedDomainRoot.ToArray());
                byte[] payload = value.Payload;
                writer.Write((ushort)payload.Length);
                writer.Write(payload);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static PlayerIntentV2 DecodeIntent(SessionStamp stamp, byte[] bytes)
        {
            if (bytes == null || bytes.Length > Limits.CommandBytes + 72) throw new InvalidDataException("Intent payload is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                MemberIdentity member = new MemberIdentity(ReadGuid(reader), reader.ReadUInt32());
                ulong operation = reader.ReadUInt64();
                ulong permission = reader.ReadUInt64();
                ushort domain = reader.ReadUInt16();
                Hash256 expected = ReadHash(reader);
                ushort length = reader.ReadUInt16();
                if (length > Limits.CommandBytes || reader.BaseStream.Length - reader.BaseStream.Position != length)
                    throw new InvalidDataException("Intent body length is invalid.");
                return new PlayerIntentV2(stamp, member, operation, permission, domain, expected, reader.ReadBytes(length));
            }
        }

        public static byte[] EncodeBatch(AuthorityBatch value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Revision);
                writer.Write((byte)value.OriginKind);
                writer.Write(value.MemberId.ToByteArray());
                writer.Write(value.MemberGeneration);
                writer.Write(value.OperationCounter);
                writer.Write(value.DomainId);
                writer.Write(value.BeforeRoot.ToArray());
                writer.Write(value.AfterRoot.ToArray());
                byte[] payload = value.Payload;
                writer.Write((uint)payload.Length);
                writer.Write(payload);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static AuthorityBatch DecodeBatch(SessionStamp stamp, byte[] bytes)
        {
            if (bytes == null || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Authority batch payload is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                ulong revision = reader.ReadUInt64();
                AuthorityOriginKind origin = (AuthorityOriginKind)reader.ReadByte();
                byte[] memberBytes = reader.ReadBytes(16);
                if (memberBytes.Length != 16) throw new EndOfStreamException();
                Guid member = new Guid(memberBytes);
                uint memberGeneration = reader.ReadUInt32();
                ulong operation = reader.ReadUInt64();
                ushort domain = reader.ReadUInt16();
                Hash256 before = ReadHash(reader);
                Hash256 after = ReadHash(reader);
                uint length = reader.ReadUInt32();
                if (length > Limits.FramePayloadBytes || reader.BaseStream.Length - reader.BaseStream.Position != length)
                    throw new InvalidDataException("Authority batch body length is invalid.");
                return new AuthorityBatch(stamp, revision, origin, member, memberGeneration, operation, domain,
                    before, after, reader.ReadBytes((int)length));
            }
        }

        public static byte[] EncodeAppliedAck(AppliedAck value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Revision);
                writer.Write(value.Root.ToArray());
                writer.Write(value.PendingBatches);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static AppliedAck DecodeAppliedAck(SessionStamp stamp, Guid binding, byte[] bytes)
        {
            if (bytes == null || bytes.Length != 44) throw new InvalidDataException("AppliedAck payload length is invalid.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
                return new AppliedAck(stamp, binding, reader.ReadUInt64(), ReadHash(reader), reader.ReadInt32());
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            Guid value = new Guid(bytes);
            if (value == Guid.Empty) throw new InvalidDataException("Missing UUID.");
            return value;
        }

        private static Hash256 ReadHash(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Hash256.Size);
            if (bytes.Length != Hash256.Size) throw new EndOfStreamException();
            return new Hash256(bytes);
        }
    }
}
