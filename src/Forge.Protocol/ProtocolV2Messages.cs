using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;

namespace CsmForge.Protocol
{
    public enum BootstrapAuthMode : byte
    {
        DevelopmentRoomKeyOnly = 1,
        TrustedTransportIdentity = 2
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
            // S3: the character bound and the UTF-8 byte bound (the wire limit used by
            // EncodeHello's WriteString) must both hold here, or a CJK name that passes the
            // character check fails only at encode time, after the join has started.
            if (clientInstanceId == Guid.Empty || string.IsNullOrEmpty(displayName) || displayName.Length > 32 ||
                Encoding.UTF8.GetByteCount(displayName) > 64 ||
                gameBuildHash == null || schemaHash == null || manifestPageCount == 0 || manifestPageCount > 256 ||
                !Enum.IsDefined(typeof(BootstrapAuthMode), authMode)) throw new ArgumentException("Invalid bootstrap hello.");
            ClientInstanceId = clientInstanceId; DisplayName = displayName;
            GameBuildHash = gameBuildHash; SchemaHash = schemaHash; ManifestPageCount = manifestPageCount; AuthMode = authMode;
        }
    }

    public sealed class ManifestPageV2
    {
        public ushort Index { get; private set; }
        public ushort Total { get; private set; }
        public ComponentFingerprint[] Entries { get; private set; }
        public ManifestPageV2(ushort index, ushort total, ComponentFingerprint[] entries)
        {
            if (total == 0 || total > 256 || index >= total || entries == null || entries.Length > 64)
                throw new ArgumentException("Invalid manifest page.");
            Index = index; Total = total; Entries = (ComponentFingerprint[])entries.Clone();
        }
    }

    public sealed class CompatibilityResultV2
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }
        public CompatibilityResultV2(bool accepted, string reason)
        {
            // S3: same character-vs-byte consistency as HelloV2 (wire bound: 256 UTF-8 bytes).
            if (reason == null || reason.Length > 256 || Encoding.UTF8.GetByteCount(reason) > 256)
                throw new ArgumentException("Invalid compatibility reason.");
            Accepted = accepted; Reason = reason;
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
                throw new ArgumentException("Invalid session welcome.");
            Stamp = stamp; ConnectionBinding = connectionBinding; Member = member;
            PermissionVersion = permissionVersion; Revision = revision; Root = root;
        }
    }

    public static class BootstrapMessagesV2
    {
        private const int ManifestEntriesPerPage = 64;
        private const int WelcomeBytes = 108;

        public static ManifestPageV2[] CreateManifestPages(CompatibilityManifest manifest)
        {
            Check.NotNull(manifest, "manifest");
            ComponentFingerprint[] entries = manifest.Entries;
            int count = Math.Max(1, (entries.Length + ManifestEntriesPerPage - 1) / ManifestEntriesPerPage);
            Check.Condition(count > 256, "manifest", "Manifest needs too many pages.");
            ManifestPageV2[] result = new ManifestPageV2[count];
            for (int page = 0; page < count; page++)
            {
                int start = page * ManifestEntriesPerPage;
                int length = Math.Min(ManifestEntriesPerPage, entries.Length - start);
                ComponentFingerprint[] slice = new ComponentFingerprint[Math.Max(0, length)];
                if (length > 0) Array.Copy(entries, start, slice, 0, length);
                result[page] = new ManifestPageV2((ushort)page, (ushort)count, slice);
            }
            return result;
        }

        public static byte[] EncodeHello(HelloV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.ClientInstanceId.ToByteArray());
                WriteString(writer, value.DisplayName, 64);
                writer.Write(value.GameBuildHash.ToArray());
                writer.Write(value.SchemaHash.ToArray());
                writer.Write(value.ManifestPageCount);
                writer.Write((byte)value.AuthMode);
                writer.Flush(); return stream.ToArray();
            }
        }

        public static HelloV2 DecodeHello(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 160))
            {
                Guid id = ReadGuid(reader);
                string name = ReadString(reader, 64);
                Hash256 game = ReadHash(reader);
                Hash256 schema = ReadHash(reader);
                ushort pages = reader.ReadUInt16();
                BootstrapAuthMode auth = (BootstrapAuthMode)reader.ReadByte();
                EnsureEnd(reader);
                return new HelloV2(id, name, game, schema, pages, auth);
            }
        }

        public static byte[] EncodeIdentityProof(BootstrapAuthMode mode, Guid instanceId)
        {
            if (!Enum.IsDefined(typeof(BootstrapAuthMode), mode) || instanceId == Guid.Empty)
                throw new ArgumentException("Invalid identity proof.");
            byte[] result = new byte[17];
            result[0] = (byte)mode;
            Buffer.BlockCopy(instanceId.ToByteArray(), 0, result, 1, 16);
            return result;
        }

        public static void DecodeIdentityProof(byte[] bytes, out BootstrapAuthMode mode, out Guid instanceId)
        {
            if (bytes == null || bytes.Length != 17) throw new InvalidDataException("Invalid identity proof length.");
            mode = (BootstrapAuthMode)bytes[0];
            if (!Enum.IsDefined(typeof(BootstrapAuthMode), mode)) throw new InvalidDataException("Unknown identity proof mode.");
            byte[] guid = new byte[16]; Buffer.BlockCopy(bytes, 1, guid, 0, 16); instanceId = new Guid(guid);
            if (instanceId == Guid.Empty) throw new InvalidDataException("Missing identity proof instance.");
        }

        public static byte[] EncodeManifestPage(ManifestPageV2 page)
        {
            Check.NotNull(page, "page");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(page.Index); writer.Write(page.Total); writer.Write((ushort)page.Entries.Length);
                foreach (ComponentFingerprint entry in page.Entries)
                {
                    WriteString(writer, entry.Id, 128);
                    writer.Write(entry.BinaryHash.ToArray());
                    writer.Write(entry.ConfigurationHash.ToArray());
                }
                writer.Flush(); return stream.ToArray();
            }
        }

        public static ManifestPageV2 DecodeManifestPage(byte[] bytes)
        {
            using (BinaryReader reader = Reader(bytes, 64 * 256))
            {
                ushort index = reader.ReadUInt16(); ushort total = reader.ReadUInt16(); ushort count = reader.ReadUInt16();
                if (count > ManifestEntriesPerPage) throw new InvalidDataException("Manifest page has too many entries.");
                List<ComponentFingerprint> entries = new List<ComponentFingerprint>();
                for (int i = 0; i < count; i++)
                    entries.Add(new ComponentFingerprint(ReadString(reader, 128), ReadHash(reader), ReadHash(reader)));
                EnsureEnd(reader);
                return new ManifestPageV2(index, total, entries.ToArray());
            }
        }

        public static byte[] EncodeCompatibilityResult(CompatibilityResultV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.Accepted ? (byte)1 : (byte)0);
                WriteString(writer, value.Reason, 256);
                writer.Flush(); return stream.ToArray();
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
            Check.NotNull(value, "value");
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
                writer.Flush(); return stream.ToArray();
            }
        }

        public static SessionWelcomeV2 DecodeWelcome(byte[] bytes)
        {
            if (bytes == null || bytes.Length != WelcomeBytes)
                throw new InvalidDataException("Session welcome payload length is invalid.");
            using (BinaryReader reader = Reader(bytes, WelcomeBytes))
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
            Check.NotNull(value, "value");
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > maxBytes) throw new ArgumentException("UTF-8 string is too long.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader, int maxBytes)
        {
            ushort length = reader.ReadUInt16();
            if (length > maxBytes) throw new InvalidDataException("UTF-8 string exceeds its bound.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void EnsureEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing payload bytes.");
        }
    }
}
