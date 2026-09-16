using System;
using System.IO;
using CsmForge.Core;

namespace CsmForge.Protocol
{
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
            if (bytes == null || bytes.Length > Limits.CommandBytes + 72)
                throw new InvalidDataException("Intent payload is invalid.");
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
                return new PlayerIntentV2(stamp, member, operation, permission, domain, expected,
                    reader.ReadBytes(length));
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
            if (bytes == null || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Authority batch payload is invalid.");
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
                return new AuthorityBatch(stamp, revision, origin, member, memberGeneration, operation,
                    domain, before, after, reader.ReadBytes((int)length));
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
            if (bytes == null || bytes.Length != 44)
                throw new InvalidDataException("AppliedAck payload length is invalid.");
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
