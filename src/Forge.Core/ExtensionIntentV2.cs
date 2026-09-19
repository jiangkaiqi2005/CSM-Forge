using System;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public sealed class ExtensionPlayerIntentV2
    {
        private readonly byte[] payload;
        public string AdapterId { get; private set; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }

        public ExtensionPlayerIntentV2(string adapterId, byte[] bytes)
        {
            ExtensionStateEntryV2.ValidateToken(adapterId, "adapterId", 96);
            if (bytes == null || bytes.Length > ExtensionIntentCodecV2.MaximumIntentPayload)
                throw new ArgumentException("Invalid extension intent payload.", "bytes");
            AdapterId = adapterId;
            payload = (byte[])bytes.Clone();
        }
        internal byte[] UnsafePayload { get { return payload; } }
    }

    public static class ExtensionIntentCodecV2
    {
        private const uint Magic = 0x32495846u; // FXI2
        public const int MaximumIntentPayload = 48 * 1024;

        public static byte[] Encode(ExtensionPlayerIntentV2 intent)
        {
            Check.NotNull(intent, "intent");
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                byte[] id = Encoding.ASCII.GetBytes(intent.AdapterId);
                writer.Write((byte)id.Length);
                writer.Write(id);
                byte[] payload = intent.UnsafePayload;
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                if (stream.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Extension intent exceeds one frame.");
                return stream.ToArray();
            }
        }

        public static ExtensionPlayerIntentV2 Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > Limits.FramePayloadBytes)
                throw new InvalidDataException("Invalid extension intent frame size.");
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid extension intent magic.");
                    int idLength = reader.ReadByte();
                    if (idLength <= 0 || idLength > 96) throw new InvalidDataException("Invalid extension adapter id length.");
                    byte[] idBytes = reader.ReadBytes(idLength);
                    if (idBytes.Length != idLength) throw new EndOfStreamException();
                    string id = Encoding.ASCII.GetString(idBytes);
                    int length = reader.ReadInt32();
                    if (length < 0 || length > MaximumIntentPayload || length > stream.Length - stream.Position)
                        throw new InvalidDataException("Invalid extension intent payload length.");
                    byte[] payload = reader.ReadBytes(length);
                    if (payload.Length != length) throw new EndOfStreamException();
                    if (stream.Position != stream.Length) throw new InvalidDataException("Trailing extension intent bytes.");
                    ExtensionPlayerIntentV2 intent = new ExtensionPlayerIntentV2(id, payload);
                    byte[] canonical = Encode(intent);
                    if (canonical.Length != bytes.Length) throw new InvalidDataException("Non-canonical extension intent.");
                    for (int i = 0; i < bytes.Length; i++) if (canonical[i] != bytes[i]) throw new InvalidDataException("Non-canonical extension intent.");
                    return intent;
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated extension intent.", error); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid extension intent.", error); }
        }
    }
}
