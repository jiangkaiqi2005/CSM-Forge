using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsmForge.Core;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal enum DecorationIntentKind : byte { Create = 1, Move = 2, Delete = 3 }

    internal sealed class TreeStateAdapter : IForgeInteractiveShardedStateAdapterV1
    {
        private const uint StateMagic = 0x31525446u; // FTR1
        private const uint IntentMagic = 0x31495446u; // FTI1
        private const int Shards = 512;
        internal const string Adapter = "builtin.trees";
        private static int mutationVersion;
        private int seenMutation = int.MinValue;
        private List<TreeState>[] cache;

        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }
        internal static void MarkDirty() { unchecked { mutationVersion++; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex)
        {
            ValidateShard(shardIndex);
            EnsureCache(context);
            return EncodeState(shardIndex, cache[shardIndex]);
        }

        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            ValidateShard(shardIndex);
            TreeState[] desired = DecodeState(shardIndex, state);
            TreeManager manager = TreeManager.instance;
            if (manager == null) throw new InvalidOperationException("TreeManager is unavailable.");
            Dictionary<EntityIdentityV2, TreeState> wanted = new Dictionary<EntityIdentityV2, TreeState>();
            for (int i = 0; i < desired.Length; i++) wanted.Add(desired[i].Identity, desired[i]);

            for (int i = 0; i < desired.Length; i++)
            {
                TreeState value = desired[i];
                uint native;
                if (!context.TryGetNative(value.Identity, out native))
                {
                    TreeInfo info = PrefabCollection<TreeInfo>.FindLoaded(value.PrefabKey);
                    if (info == null) throw new InvalidOperationException("Tree prefab is not loaded: " + value.PrefabKey);
                    ColossalFramework.Math.Randomizer randomizer = SimulationManager.instance.m_randomizer;
                    if (!manager.CreateTree(out native, ref randomizer, info, value.Position, value.Single) || native == 0)
                        throw new InvalidOperationException("CS1 could not materialize replica Tree state.");
                    context.BindKnownIdentity(value.Identity, native);
                }
                if (native == 0 || native >= manager.m_trees.m_buffer.Length)
                    throw new InvalidOperationException("Tree stable identity maps outside the local array.");
                TreeInstance data = manager.m_trees.m_buffer[native];
                if (data.m_flags == 0 || data.Info == null || data.Info.name != value.PrefabKey)
                    throw new InvalidOperationException("Tree stable identity prefab validation failed.");
                manager.MoveTree(native, value.Position);
                data = manager.m_trees.m_buffer[native];
                data.Single = value.Single;
                data.FixedHeight = value.FixedHeight;
                data.Hidden = value.Hidden;
                manager.m_trees.m_buffer[native] = data;
                manager.UpdateTree(native);
            }

            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i];
                if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || wanted.ContainsKey(mapping.Identity)) continue;
                uint native = mapping.NativeId;
                if (native > 0 && native < manager.m_trees.m_buffer.Length && manager.m_trees.m_buffer[native].m_flags != 0)
                    manager.ReleaseTree(native);
                if (!context.RetireIdentity(mapping.Identity)) throw new InvalidOperationException("Replica Tree identity retirement failed.");
            }
            seenMutation = int.MinValue;
        }

        public bool ExecuteIntent(IForgeAdapterContextV1 context, byte[] intent)
        {
            if (context == null || !context.IsAuthoritative) return false;
            DecorationIntent request;
            try { request = DecodeIntent(intent, IntentMagic); }
            catch { return false; }
            TreeManager manager = TreeManager.instance;
            if (manager == null) return false;
            if (request.Kind == DecorationIntentKind.Create)
            {
                TreeInfo info = PrefabCollection<TreeInfo>.FindLoaded(request.PrefabKey);
                if (info == null) return false;
                ColossalFramework.Math.Randomizer randomizer = SimulationManager.instance.m_randomizer;
                uint native;
                if (!manager.CreateTree(out native, ref randomizer, info, request.Position, request.Single) || native == 0) return false;
                context.GetOrAllocateIdentity(native); MarkDirty(); return true;
            }
            uint target;
            if (!context.TryGetNative(request.Target, out target) || target == 0 || target >= manager.m_trees.m_buffer.Length) return false;
            if (request.Kind == DecorationIntentKind.Move)
            {
                manager.MoveTree(target, request.Position); MarkDirty(); return true;
            }
            if (request.Kind == DecorationIntentKind.Delete)
            {
                manager.ReleaseTree(target);
                if (!context.RetireIdentity(request.Target)) return false;
                MarkDirty(); return true;
            }
            return false;
        }

        internal static byte[] CreateIntent(TreeInfo info, Vector3 position, bool single)
        {
            if (info == null || string.IsNullOrEmpty(info.name)) throw new ArgumentException("Tree prefab is missing.");
            return EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Create, PrefabKey = info.name, Position = position, Single = single });
        }
        internal static byte[] MoveIntent(EntityIdentityV2 target, Vector3 position)
        { return EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Move, Target = target, Position = position }); }
        internal static byte[] DeleteIntent(EntityIdentityV2 target)
        { return EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Delete, Target = target }); }
        internal static bool TryResolveLocal(uint native, out EntityIdentityV2 identity)
        { return ExtensionIdentityServices.Maps.GetOrAttach(Adapter).TryGetIdentity(native, out identity); }

        private void EnsureCache(IForgeAdapterContextV1 context)
        {
            if (cache != null && seenMutation == mutationVersion) return;
            TreeManager manager = TreeManager.instance;
            if (manager == null) throw new InvalidOperationException("TreeManager is unavailable.");
            if (context.IsAuthoritative)
            {
                int limit = manager.m_trees.m_buffer.Length;
                for (uint native = 1; native < limit; native++)
                {
                    TreeInstance data = manager.m_trees.m_buffer[native];
                    if (data.m_flags == 0 || data.Info == null) continue;
                    EntityIdentityV2 identity;
                    if (!context.TryGetIdentity(native, out identity)) context.GetOrAllocateIdentity(native);
                }
            }
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            if (context.IsAuthoritative)
            {
                for (int i = 0; i < mappings.Length; i++)
                {
                    uint native = mappings[i].NativeId;
                    if (native == 0 || native >= manager.m_trees.m_buffer.Length || manager.m_trees.m_buffer[native].m_flags == 0)
                        context.RetireIdentity(mappings[i].Identity);
                }
                mappings = context.SnapshotMappings();
            }
            cache = new List<TreeState>[Shards];
            for (int i = 0; i < Shards; i++) cache[i] = new List<TreeState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                uint native = mappings[i].NativeId;
                if (native == 0 || native >= manager.m_trees.m_buffer.Length) continue;
                TreeInstance data = manager.m_trees.m_buffer[native];
                if (data.m_flags == 0 || data.Info == null || string.IsNullOrEmpty(data.Info.name)) continue;
                int shard = (int)((mappings[i].Identity.EntityId - 1UL) % Shards);
                cache[shard].Add(new TreeState
                {
                    Identity = mappings[i].Identity, PrefabKey = data.Info.name, Position = data.Position,
                    Single = data.Single, FixedHeight = data.FixedHeight, Hidden = data.Hidden
                });
            }
            for (int i = 0; i < Shards; i++) cache[i].Sort(delegate(TreeState a, TreeState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); });
            seenMutation = mutationVersion;
        }

        private static byte[] EncodeState(int shard, List<TreeState> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(StateMagic); writer.Write((ushort)shard); writer.Write((ushort)values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    TreeState v = values[i]; writer.Write(v.Identity.EntityId); writer.Write(v.Identity.Generation); WriteString(writer, v.PrefabKey);
                    writer.Write(v.Position.x); writer.Write(v.Position.y); writer.Write(v.Position.z);
                    byte flags = 0; if (v.Single) flags |= 1; if (v.FixedHeight) flags |= 2; if (v.Hidden) flags |= 4; writer.Write(flags);
                }
                writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Tree shard exceeds one Forge frame."); return stream.ToArray();
            }
        }
        private static TreeState[] DecodeState(int shard, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid Tree shard size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != StateMagic || reader.ReadUInt16() != (ushort)shard) throw new InvalidDataException("Invalid Tree shard header.");
                int count = reader.ReadUInt16(); TreeState[] result = new TreeState[count]; ulong previous = 0;
                for (int i = 0; i < count; i++)
                {
                    EntityIdentityV2 id = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
                    if (!id.IsValid || id.EntityId <= previous || (int)((id.EntityId - 1UL) % Shards) != shard) throw new InvalidDataException("Invalid Tree stable ordering.");
                    previous = id.EntityId; byte flags;
                    result[i] = new TreeState { Identity = id, PrefabKey = ReadString(reader), Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) };
                    flags = reader.ReadByte(); result[i].Single = (flags & 1) != 0; result[i].FixedHeight = (flags & 2) != 0; result[i].Hidden = (flags & 4) != 0;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Tree shard bytes."); return result;
            }
        }
        private static void ValidateShard(int shard) { if (shard < 0 || shard >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }
        private sealed class TreeState { public EntityIdentityV2 Identity; public string PrefabKey; public Vector3 Position; public bool Single; public bool FixedHeight; public bool Hidden; }

        internal static byte[] EncodeIntent(uint magic, DecorationIntent value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(magic); writer.Write((byte)value.Kind);
                writer.Write(value.Target.EntityId); writer.Write(value.Target.Generation); WriteString(writer, value.PrefabKey ?? "-");
                writer.Write(value.Position.x); writer.Write(value.Position.y); writer.Write(value.Position.z); writer.Write(value.Angle); writer.Write(value.Single);
                writer.Flush(); return stream.ToArray();
            }
        }
        internal static DecorationIntent DecodeIntent(byte[] bytes, uint magic)
        {
            if (bytes == null || bytes.Length < 32 || bytes.Length > 1024) throw new InvalidDataException("Invalid decoration intent size.");
            using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != magic) throw new InvalidDataException("Invalid decoration intent magic.");
                DecorationIntentKind kind = (DecorationIntentKind)reader.ReadByte(); if (kind < DecorationIntentKind.Create || kind > DecorationIntentKind.Delete) throw new InvalidDataException("Invalid decoration intent kind.");
                DecorationIntent result = new DecorationIntent { Kind = kind, Target = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()), PrefabKey = ReadString(reader),
                    Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), Angle = reader.ReadSingle(), Single = reader.ReadBoolean() };
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing decoration intent bytes.");
                if (kind == DecorationIntentKind.Create && (string.IsNullOrEmpty(result.PrefabKey) || result.PrefabKey == "-")) throw new InvalidDataException("Decoration create prefab is missing.");
                if (kind != DecorationIntentKind.Create && !result.Target.IsValid) throw new InvalidDataException("Decoration target identity is missing.");
                return result;
            }
        }
        internal static void WriteString(BinaryWriter writer, string value)
        { byte[] data = Encoding.UTF8.GetBytes(value ?? string.Empty); if (data.Length == 0 || data.Length > 255) throw new InvalidDataException("Decoration prefab key is invalid."); writer.Write((byte)data.Length); writer.Write(data); }
        internal static string ReadString(BinaryReader reader)
        { int length = reader.ReadByte(); if (length == 0) throw new InvalidDataException("Decoration prefab key is empty."); byte[] data = reader.ReadBytes(length); if (data.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(data); }
    }

    internal sealed class PropStateAdapter : IForgeInteractiveShardedStateAdapterV1
    {
        private const uint StateMagic = 0x31525046u; // FPR1
        private const uint IntentMagic = 0x31495046u; // FPI1
        private const int Shards = 256;
        internal const string Adapter = "builtin.props";
        private static int mutationVersion;
        private int seenMutation = int.MinValue;
        private List<PropState>[] cache;
        public string AdapterId { get { return Adapter; } }
        public uint SchemaVersion { get { return 1; } }
        public int ShardCount { get { return Shards; } }
        internal static void MarkDirty() { unchecked { mutationVersion++; } }

        public byte[] CaptureShard(IForgeAdapterContextV1 context, int shardIndex) { ValidateShard(shardIndex); EnsureCache(context); return EncodeState(shardIndex, cache[shardIndex]); }
        public void ApplyShard(IForgeAdapterContextV1 context, int shardIndex, byte[] state)
        {
            ValidateShard(shardIndex); PropState[] desired = DecodeState(shardIndex, state); PropManager manager = PropManager.instance;
            if (manager == null) throw new InvalidOperationException("PropManager is unavailable.");
            Dictionary<EntityIdentityV2, PropState> wanted = new Dictionary<EntityIdentityV2, PropState>(); for (int i = 0; i < desired.Length; i++) wanted.Add(desired[i].Identity, desired[i]);
            for (int i = 0; i < desired.Length; i++)
            {
                PropState v = desired[i]; uint nativeValue; ushort native;
                if (!context.TryGetNative(v.Identity, out nativeValue))
                {
                    PropInfo info = PrefabCollection<PropInfo>.FindLoaded(v.PrefabKey); if (info == null) throw new InvalidOperationException("Prop prefab is not loaded: " + v.PrefabKey);
                    ColossalFramework.Math.Randomizer randomizer = SimulationManager.instance.m_randomizer;
                    if (!manager.CreateProp(out native, ref randomizer, info, v.Position, v.Angle, v.Single) || native == 0) throw new InvalidOperationException("CS1 could not materialize replica Prop state.");
                    context.BindKnownIdentity(v.Identity, native); nativeValue = native;
                }
                if (nativeValue == 0 || nativeValue > ushort.MaxValue) throw new InvalidOperationException("Prop stable identity maps outside ushort range."); native = (ushort)nativeValue;
                PropInstance data = manager.m_props.m_buffer[native]; if (data.m_flags == 0 || data.Info == null || data.Info.name != v.PrefabKey) throw new InvalidOperationException("Prop stable identity prefab validation failed.");
                manager.MoveProp(native, v.Position); data = manager.m_props.m_buffer[native]; data.Angle = v.Angle; data.Single = v.Single; data.FixedHeight = v.FixedHeight; data.Hidden = v.Hidden; data.Blocked = v.Blocked;
                manager.m_props.m_buffer[native] = data; manager.UpdateProp(native);
            }
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                EntityMapEntryV2 mapping = mappings[i]; if ((int)((mapping.Identity.EntityId - 1UL) % Shards) != shardIndex || wanted.ContainsKey(mapping.Identity)) continue;
                if (mapping.NativeId > 0 && mapping.NativeId <= ushort.MaxValue)
                { ushort native = (ushort)mapping.NativeId; if (native < manager.m_props.m_buffer.Length && manager.m_props.m_buffer[native].m_flags != 0) manager.ReleaseProp(native); }
                if (!context.RetireIdentity(mapping.Identity)) throw new InvalidOperationException("Replica Prop identity retirement failed.");
            }
            seenMutation = int.MinValue;
        }
        public bool ExecuteIntent(IForgeAdapterContextV1 context, byte[] intent)
        {
            if (context == null || !context.IsAuthoritative) return false; DecorationIntent r; try { r = TreeStateAdapter.DecodeIntent(intent, IntentMagic); } catch { return false; }
            PropManager manager = PropManager.instance; if (manager == null) return false;
            if (r.Kind == DecorationIntentKind.Create)
            {
                PropInfo info = PrefabCollection<PropInfo>.FindLoaded(r.PrefabKey); if (info == null) return false; ColossalFramework.Math.Randomizer randomizer = SimulationManager.instance.m_randomizer; ushort native;
                if (!manager.CreateProp(out native, ref randomizer, info, r.Position, r.Angle, r.Single) || native == 0) return false; context.GetOrAllocateIdentity(native); MarkDirty(); return true;
            }
            uint nativeValue; if (!context.TryGetNative(r.Target, out nativeValue) || nativeValue == 0 || nativeValue > ushort.MaxValue) return false; ushort target = (ushort)nativeValue;
            if (r.Kind == DecorationIntentKind.Move) { manager.MoveProp(target, r.Position); MarkDirty(); return true; }
            if (r.Kind == DecorationIntentKind.Delete) { manager.ReleaseProp(target); if (!context.RetireIdentity(r.Target)) return false; MarkDirty(); return true; }
            return false;
        }
        internal static byte[] CreateIntent(PropInfo info, Vector3 pos, float angle, bool single)
        { if (info == null || string.IsNullOrEmpty(info.name)) throw new ArgumentException("Prop prefab is missing."); return TreeStateAdapter.EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Create, PrefabKey = info.name, Position = pos, Angle = angle, Single = single }); }
        internal static byte[] MoveIntent(EntityIdentityV2 target, Vector3 pos) { return TreeStateAdapter.EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Move, Target = target, Position = pos }); }
        internal static byte[] DeleteIntent(EntityIdentityV2 target) { return TreeStateAdapter.EncodeIntent(IntentMagic, new DecorationIntent { Kind = DecorationIntentKind.Delete, Target = target }); }
        internal static bool TryResolveLocal(ushort native, out EntityIdentityV2 identity) { return ExtensionIdentityServices.Maps.GetOrAttach(Adapter).TryGetIdentity(native, out identity); }

        private void EnsureCache(IForgeAdapterContextV1 context)
        {
            if (cache != null && seenMutation == mutationVersion) return; PropManager manager = PropManager.instance; if (manager == null) throw new InvalidOperationException("PropManager is unavailable.");
            if (context.IsAuthoritative)
            {
                int limit = manager.m_props.m_buffer.Length; for (int i = 1; i < limit; i++) { ushort native = (ushort)i; PropInstance data = manager.m_props.m_buffer[native]; if (data.m_flags == 0 || data.Info == null) continue; EntityIdentityV2 id; if (!context.TryGetIdentity(native, out id)) context.GetOrAllocateIdentity(native); }
            }
            EntityMapEntryV2[] mappings = context.SnapshotMappings();
            if (context.IsAuthoritative)
            {
                for (int i = 0; i < mappings.Length; i++) { uint n = mappings[i].NativeId; if (n == 0 || n > ushort.MaxValue || n >= manager.m_props.m_buffer.Length || manager.m_props.m_buffer[(ushort)n].m_flags == 0) context.RetireIdentity(mappings[i].Identity); }
                mappings = context.SnapshotMappings();
            }
            cache = new List<PropState>[Shards]; for (int i = 0; i < Shards; i++) cache[i] = new List<PropState>();
            for (int i = 0; i < mappings.Length; i++)
            {
                uint n = mappings[i].NativeId; if (n == 0 || n > ushort.MaxValue) continue; ushort native = (ushort)n; PropInstance data = manager.m_props.m_buffer[native]; if (data.m_flags == 0 || data.Info == null || string.IsNullOrEmpty(data.Info.name)) continue;
                int shard = (int)((mappings[i].Identity.EntityId - 1UL) % Shards); cache[shard].Add(new PropState { Identity = mappings[i].Identity, PrefabKey = data.Info.name, Position = data.Position, Angle = data.Angle, Single = data.Single, FixedHeight = data.FixedHeight, Hidden = data.Hidden, Blocked = data.Blocked });
            }
            for (int i = 0; i < Shards; i++) cache[i].Sort(delegate(PropState a, PropState b) { return a.Identity.EntityId.CompareTo(b.Identity.EntityId); }); seenMutation = mutationVersion;
        }
        private static byte[] EncodeState(int shard, List<PropState> values)
        {
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(StateMagic); writer.Write((ushort)shard); writer.Write((ushort)values.Count); for (int i = 0; i < values.Count; i++) { PropState v = values[i]; writer.Write(v.Identity.EntityId); writer.Write(v.Identity.Generation); TreeStateAdapter.WriteString(writer, v.PrefabKey); writer.Write(v.Position.x); writer.Write(v.Position.y); writer.Write(v.Position.z); writer.Write(v.Angle); byte flags = 0; if (v.Single) flags |= 1; if (v.FixedHeight) flags |= 2; if (v.Hidden) flags |= 4; if (v.Blocked) flags |= 8; writer.Write(flags); } writer.Flush(); if (stream.Length > Limits.FramePayloadBytes) throw new InvalidOperationException("Prop shard exceeds one Forge frame."); return stream.ToArray();
            }
        }
        private static PropState[] DecodeState(int shard, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > Limits.FramePayloadBytes) throw new InvalidDataException("Invalid Prop shard size."); using (MemoryStream stream = new MemoryStream(bytes, false)) using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadUInt32() != StateMagic || reader.ReadUInt16() != (ushort)shard) throw new InvalidDataException("Invalid Prop shard header."); int count = reader.ReadUInt16(); PropState[] result = new PropState[count]; ulong previous = 0;
                for (int i = 0; i < count; i++) { EntityIdentityV2 id = new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32()); if (!id.IsValid || id.EntityId <= previous || (int)((id.EntityId - 1UL) % Shards) != shard) throw new InvalidDataException("Invalid Prop stable ordering."); previous = id.EntityId; PropState v = new PropState { Identity = id, PrefabKey = TreeStateAdapter.ReadString(reader), Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), Angle = reader.ReadSingle() }; byte flags = reader.ReadByte(); v.Single = (flags & 1) != 0; v.FixedHeight = (flags & 2) != 0; v.Hidden = (flags & 4) != 0; v.Blocked = (flags & 8) != 0; result[i] = v; }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing Prop shard bytes."); return result;
            }
        }
        private static void ValidateShard(int shard) { if (shard < 0 || shard >= Shards) throw new ArgumentOutOfRangeException("shardIndex"); }
        private sealed class PropState { public EntityIdentityV2 Identity; public string PrefabKey; public Vector3 Position; public float Angle; public bool Single; public bool FixedHeight; public bool Hidden; public bool Blocked; }
    }

    internal sealed class DecorationIntent
    {
        public DecorationIntentKind Kind; public EntityIdentityV2 Target; public string PrefabKey; public Vector3 Position; public float Angle; public bool Single;
    }
}
