using System;
using System.IO;
using CsmForge.Core;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public sealed class ForgeSaveMetadata
    {
        public const ushort CurrentSchema = 1;
        public Guid WorldId { get; private set; }
        public ulong Epoch { get; private set; }
        public ulong Revision { get; private set; }
        public bool RootKnown { get; private set; }
        public Hash256 StateRoot { get; private set; }

        public ForgeSaveMetadata(Guid worldId, ulong epoch, ulong revision, bool rootKnown, Hash256 stateRoot)
        {
            if (worldId == Guid.Empty || epoch == 0) throw new ArgumentException("Save metadata world identity is incomplete.");
            if (rootKnown && stateRoot == null) throw new ArgumentNullException("stateRoot");
            WorldId = worldId;
            Epoch = epoch;
            Revision = revision;
            RootKnown = rootKnown;
            StateRoot = stateRoot;
        }
    }

    public static class ForgeSaveMetadataCodec
    {
        private const uint Magic = 0x4D465343; // CSFM little endian
        private const int FixedBytes = 4 + 2 + 2 + 16 + 8 + 8 + 1 + Hash256.Size;

        public static byte[] Encode(ForgeSaveMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(ForgeSaveMetadata.CurrentSchema);
                writer.Write((ushort)0);
                writer.Write(metadata.WorldId.ToByteArray());
                writer.Write(metadata.Epoch);
                writer.Write(metadata.Revision);
                writer.Write((byte)(metadata.RootKnown ? 1 : 0));
                writer.Write(metadata.RootKnown ? metadata.StateRoot.ToArray() : new byte[Hash256.Size]);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static ForgeSaveMetadata Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != FixedBytes) throw new InvalidDataException("Invalid Forge save metadata length.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unknown Forge save metadata magic.");
                if (reader.ReadUInt16() != ForgeSaveMetadata.CurrentSchema || reader.ReadUInt16() != 0)
                    throw new InvalidDataException("Unsupported Forge save metadata schema.");
                Guid worldId = new Guid(reader.ReadBytes(16));
                ulong epoch = reader.ReadUInt64();
                ulong revision = reader.ReadUInt64();
                byte rootKnown = reader.ReadByte();
                if (rootKnown > 1) throw new InvalidDataException("Invalid Forge root-known flag.");
                byte[] rootBytes = reader.ReadBytes(Hash256.Size);
                if (rootBytes.Length != Hash256.Size) throw new InvalidDataException("Truncated Forge state root.");
                return new ForgeSaveMetadata(worldId, epoch, revision, rootKnown == 1,
                    rootKnown == 1 ? new Hash256(rootBytes) : null);
            }
        }
    }

    public sealed class ForgeSaveMetadataStore
    {
        private readonly object gate = new object();
        private readonly RuntimeEventLog events;
        private ForgeSaveMetadata pending;
        private ForgeSaveMetadata current;

        public ForgeSaveMetadataStore(RuntimeEventLog events)
        {
            if (events == null) throw new ArgumentNullException("events");
            this.events = events;
        }

        public ForgeSaveMetadata Pending { get { lock (gate) return pending; } }
        public ForgeSaveMetadata Current { get { lock (gate) return current; } }

        public void LoadPending(byte[] bytes)
        {
            ForgeSaveMetadata value = bytes == null || bytes.Length == 0 ? null : ForgeSaveMetadataCodec.Decode(bytes);
            lock (gate) pending = value;
            events.Record(RuntimeEventCode.SaveMetadataLoaded, RuntimeServices.Lifecycle.Current.Generation,
                value == null ? "new-world" : "schema=" + ForgeSaveMetadata.CurrentSchema);
        }

        public Guid? PendingWorldId
        {
            get { lock (gate) return pending == null ? (Guid?)null : pending.WorldId; }
        }

        public ulong? PendingEpoch
        {
            get { lock (gate) return pending == null ? (ulong?)null : pending.Epoch; }
        }

        public void Attach(LoadIdentity identity)
        {
            lock (gate)
            {
                if (pending != null && (pending.WorldId != identity.WorldId || pending.Epoch != identity.Epoch))
                    throw new InvalidDataException("Loaded metadata does not match the active world identity.");
                current = pending ?? new ForgeSaveMetadata(identity.WorldId, identity.Epoch, 0, false, null);
                pending = null;
            }
        }

        public void Update(LoadIdentity identity, ulong revision, Hash256 stateRoot)
        {
            lock (gate)
            {
                if (!identity.IsValid) throw new ArgumentException("Invalid load identity.", "identity");
                current = new ForgeSaveMetadata(identity.WorldId, identity.Epoch, revision, stateRoot != null, stateRoot);
            }
        }

        public byte[] EncodeCurrent(LoadIdentity identity)
        {
            lock (gate)
            {
                ForgeSaveMetadata value = current;
                if (value == null || value.WorldId != identity.WorldId || value.Epoch != identity.Epoch)
                    value = new ForgeSaveMetadata(identity.WorldId, identity.Epoch, 0, false, null);
                return ForgeSaveMetadataCodec.Encode(value);
            }
        }

        public void Clear()
        {
            lock (gate) { pending = null; current = null; }
        }
    }

    public sealed class ForgeSerializableDataExtension : SerializableDataExtensionBase
    {
        private const string DataId = "CSM-Forge.V3.Metadata";
        private ISerializableData serializableData;

        public override void OnCreated(ISerializableData value)
        {
            base.OnCreated(value);
            serializableData = value;
        }

        public override void OnLoadData()
        {
            base.OnLoadData();
            try
            {
                byte[] bytes = serializableData == null ? null : serializableData.LoadData(DataId);
                RuntimeServices.Metadata.LoadPending(bytes);
            }
            catch (Exception error)
            {
                RuntimeServices.Events.Record(RuntimeEventCode.Error, RuntimeServices.Lifecycle.Current.Generation,
                    "metadata-load: " + error.GetType().Name);
                RuntimeServices.Metadata.Clear();
            }
        }

        public override void OnSaveData()
        {
            base.OnSaveData();
            LoadIdentity identity = RuntimeServices.Lifecycle.Current;
            if (serializableData == null || !identity.IsValid) return;
            try
            {
                serializableData.SaveData(DataId, RuntimeServices.Metadata.EncodeCurrent(identity));
                RuntimeServices.Events.Record(RuntimeEventCode.SaveMetadataSaved, identity.Generation, null);
            }
            catch (Exception error)
            {
                RuntimeServices.Events.Record(RuntimeEventCode.Error, identity.Generation,
                    "metadata-save: " + error.GetType().Name);
                RuntimeServices.Lifecycle.Fence("Forge metadata save failed");
            }
        }

        public override void OnReleased()
        {
            serializableData = null;
            base.OnReleased();
        }
    }
}
