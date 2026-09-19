using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsmForge.Core
{
    public enum NetIntentKindV2 : byte
    {
        Create = 1,
        DeleteSegment = 2,
        DeleteNode = 3,
        UpgradeSegment = 4,
        MultitoolAddNode = 16,
        MultitoolRemoveNode = 17,
        MultitoolUnionNodes = 18,
        MultitoolSplitNode = 19,
        MultitoolIntersectSegments = 20,
        MultitoolCreateParallel = 21,
        MultitoolCreateConnection = 22
    }

    public sealed class NetControlPointV2
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float DirectionX { get; private set; }
        public float DirectionY { get; private set; }
        public float DirectionZ { get; private set; }
        public float Elevation { get; private set; }
        public EntityIdentityV2 ExistingNode { get; private set; }
        public EntityIdentityV2 ExistingSegment { get; private set; }
        public bool Outside { get; private set; }

        public NetControlPointV2(float x, float y, float z, float directionX, float directionY, float directionZ,
            float elevation, EntityIdentityV2 existingNode, EntityIdentityV2 existingSegment, bool outside)
        {
            CheckFinite(x); CheckFinite(y); CheckFinite(z); CheckFinite(directionX); CheckFinite(directionY);
            CheckFinite(directionZ); CheckFinite(elevation);
            X = x; Y = y; Z = z; DirectionX = directionX; DirectionY = directionY; DirectionZ = directionZ;
            Elevation = elevation; ExistingNode = existingNode; ExistingSegment = existingSegment; Outside = outside;
        }

        internal static void CheckFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Net geometry must be finite.");
        }
    }

    public sealed class NetMultitoolPointV2
    {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float ForwardX { get; private set; }
        public float ForwardY { get; private set; }
        public float ForwardZ { get; private set; }
        public float BackwardX { get; private set; }
        public float BackwardY { get; private set; }
        public float BackwardZ { get; private set; }

        public NetMultitoolPointV2(float x, float y, float z, float forwardX, float forwardY, float forwardZ,
            float backwardX, float backwardY, float backwardZ)
        {
            NetControlPointV2.CheckFinite(x); NetControlPointV2.CheckFinite(y); NetControlPointV2.CheckFinite(z);
            NetControlPointV2.CheckFinite(forwardX); NetControlPointV2.CheckFinite(forwardY); NetControlPointV2.CheckFinite(forwardZ);
            NetControlPointV2.CheckFinite(backwardX); NetControlPointV2.CheckFinite(backwardY); NetControlPointV2.CheckFinite(backwardZ);
            X = x; Y = y; Z = z; ForwardX = forwardX; ForwardY = forwardY; ForwardZ = forwardZ;
            BackwardX = backwardX; BackwardY = backwardY; BackwardZ = backwardZ;
        }
    }

    public sealed class NetIntentV2
    {
        public NetIntentKindV2 Kind { get; private set; }
        public string PrefabKey { get; private set; }
        public NetControlPointV2 Start { get; private set; }
        public NetControlPointV2 Middle { get; private set; }
        public NetControlPointV2 End { get; private set; }
        public int MaxSegments { get; private set; }
        public bool TestEnds { get; private set; }
        public bool AutoFix { get; private set; }
        public bool Invert { get; private set; }
        public bool SwitchDirection { get; private set; }
        public uint ZoneGridFlags { get; private set; }
        public EntityIdentityV2 Target { get; private set; }
        public bool KeepNodes { get; private set; }
        public byte UpgradeMode { get; private set; }
        public bool UpgradeSide { get; private set; }
        public EntityIdentityV2 SecondaryTarget { get; private set; }
        public EntityIdentityV2[] RelatedTargets { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public NetMultitoolPointV2[] SemanticPoints { get; private set; }
        public bool FirstStart { get; private set; }
        public bool SecondStart { get; private set; }
        public bool FollowTerrain { get; private set; }

        private NetIntentV2() { }

        public static NetIntentV2 Create(string prefabKey, NetControlPointV2 start, NetControlPointV2 middle,
            NetControlPointV2 end, int maxSegments, bool testEnds, bool autoFix, bool invert,
            bool switchDirection, uint zoneGridFlags)
        {
            ValidatePrefab(prefabKey);
            if (start == null || middle == null || end == null) throw new ArgumentNullException("controlPoint");
            if (maxSegments <= 0 || maxSegments > 1024) throw new ArgumentOutOfRangeException("maxSegments");
            return new NetIntentV2
            {
                Kind = NetIntentKindV2.Create, PrefabKey = prefabKey, Start = start, Middle = middle, End = end,
                MaxSegments = maxSegments, TestEnds = testEnds, AutoFix = autoFix, Invert = invert,
                SwitchDirection = switchDirection, ZoneGridFlags = zoneGridFlags
            };
        }

        public static NetIntentV2 DeleteSegment(EntityIdentityV2 segment, bool keepNodes)
        {
            if (!segment.IsValid) throw new ArgumentException("Invalid segment identity.", "segment");
            return new NetIntentV2 { Kind = NetIntentKindV2.DeleteSegment, Target = segment, KeepNodes = keepNodes };
        }

        public static NetIntentV2 DeleteNode(EntityIdentityV2 node)
        {
            if (!node.IsValid) throw new ArgumentException("Invalid node identity.", "node");
            return new NetIntentV2 { Kind = NetIntentKindV2.DeleteNode, Target = node };
        }

        public static NetIntentV2 UpgradeSegment(EntityIdentityV2 segment, string prefabKey, byte mode, bool side)
        {
            if (!segment.IsValid) throw new ArgumentException("Invalid segment identity.", "segment");
            ValidatePrefab(prefabKey);
            return new NetIntentV2 { Kind = NetIntentKindV2.UpgradeSegment, Target = segment,
                PrefabKey = prefabKey, UpgradeMode = mode, UpgradeSide = side };
        }

        public static NetIntentV2 MultitoolAddNode(EntityIdentityV2 segment, float x, float y, float z)
        {
            ValidateIdentity(segment, "segment"); ValidatePosition(x, y, z);
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolAddNode, Target = segment, X = x, Y = y, Z = z };
        }

        public static NetIntentV2 MultitoolRemoveNode(EntityIdentityV2 node)
        {
            ValidateIdentity(node, "node");
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolRemoveNode, Target = node };
        }

        public static NetIntentV2 MultitoolUnionNodes(EntityIdentityV2 source, EntityIdentityV2 target)
        {
            ValidateIdentity(source, "source"); ValidateIdentity(target, "target");
            if (source.Equals(target)) throw new ArgumentException("Multitool union endpoints must be distinct.");
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolUnionNodes, Target = source, SecondaryTarget = target };
        }

        public static NetIntentV2 MultitoolSplitNode(EntityIdentityV2 source, float x, float y, float z,
            EntityIdentityV2[] segments)
        {
            ValidateIdentity(source, "source"); ValidatePosition(x, y, z);
            EntityIdentityV2[] normalized = NormalizeTargets(segments, 1, 7, "segments");
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolSplitNode, Target = source,
                X = x, Y = y, Z = z, RelatedTargets = normalized };
        }

        public static NetIntentV2 MultitoolIntersectSegments(EntityIdentityV2 first, EntityIdentityV2 second)
        {
            ValidateIdentity(first, "first"); ValidateIdentity(second, "second");
            if (first.Equals(second)) throw new ArgumentException("Multitool intersect segments must be distinct.");
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolIntersectSegments, Target = first, SecondaryTarget = second };
        }

        public static NetIntentV2 MultitoolCreateParallel(string prefabKey, bool invert, NetMultitoolPointV2[] points)
        {
            ValidatePrefab(prefabKey);
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolCreateParallel, PrefabKey = prefabKey,
                Invert = invert, SemanticPoints = CopyPoints(points) };
        }

        public static NetIntentV2 MultitoolCreateConnection(EntityIdentityV2 firstSegment, EntityIdentityV2 secondSegment,
            bool firstStart, bool secondStart, string prefabKey, bool invert, bool followTerrain, NetMultitoolPointV2[] points)
        {
            ValidateIdentity(firstSegment, "firstSegment"); ValidateIdentity(secondSegment, "secondSegment");
            if (firstSegment.Equals(secondSegment)) throw new ArgumentException("Multitool connection segments must be distinct.");
            ValidatePrefab(prefabKey);
            return new NetIntentV2 { Kind = NetIntentKindV2.MultitoolCreateConnection, Target = firstSegment,
                SecondaryTarget = secondSegment, FirstStart = firstStart, SecondStart = secondStart,
                PrefabKey = prefabKey, Invert = invert, FollowTerrain = followTerrain, SemanticPoints = CopyPoints(points) };
        }

        private static NetMultitoolPointV2[] CopyPoints(NetMultitoolPointV2[] points)
        {
            if (points == null || points.Length < 2 || points.Length > 512)
                throw new ArgumentException("Invalid Multitool semantic point count.", "points");
            NetMultitoolPointV2[] result = (NetMultitoolPointV2[])points.Clone();
            for (int i = 0; i < result.Length; i++)
                if (result[i] == null) throw new ArgumentException("Null Multitool semantic point.", "points");
            return result;
        }

        private static void ValidateIdentity(EntityIdentityV2 value, string name)
        {
            if (!value.IsValid) throw new ArgumentException("Invalid net entity identity.", name);
        }

        private static void ValidatePosition(float x, float y, float z)
        {
            NetControlPointV2.CheckFinite(x); NetControlPointV2.CheckFinite(y); NetControlPointV2.CheckFinite(z);
        }

        private static EntityIdentityV2[] NormalizeTargets(EntityIdentityV2[] values, int min, int max, string name)
        {
            if (values == null || values.Length < min || values.Length > max) throw new ArgumentException("Invalid net entity set.", name);
            EntityIdentityV2[] result = (EntityIdentityV2[])values.Clone();
            for (int i = 0; i < result.Length; i++) ValidateIdentity(result[i], name);
            Array.Sort(result, delegate(EntityIdentityV2 a, EntityIdentityV2 b)
            {
                int id = a.EntityId.CompareTo(b.EntityId); return id != 0 ? id : a.Generation.CompareTo(b.Generation);
            });
            for (int i = 1; i < result.Length; i++)
                if (result[i - 1].Equals(result[i])) throw new ArgumentException("Duplicate net entity identity.", name);
            return result;
        }

        internal static void ValidatePrefab(string value)
        {
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 192)
                throw new ArgumentException("Invalid net prefab identity.", "value");
        }
    }

    public sealed class NetNodeStateV2
    {
        public EntityIdentityV2 Entity { get; private set; }
        public string PrefabKey { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public uint Flags { get; private set; }

        public NetNodeStateV2(EntityIdentityV2 entity, string prefabKey, float x, float y, float z, uint flags)
        {
            if (!entity.IsValid) throw new ArgumentException("Invalid node identity.", "entity");
            NetIntentV2.ValidatePrefab(prefabKey);
            NetControlPointV2.CheckFinite(x); NetControlPointV2.CheckFinite(y); NetControlPointV2.CheckFinite(z);
            Entity = entity; PrefabKey = prefabKey; X = x; Y = y; Z = z; Flags = flags;
        }
    }

    public sealed class NetSegmentStateV2
    {
        public EntityIdentityV2 Entity { get; private set; }
        public string PrefabKey { get; private set; }
        public EntityIdentityV2 StartNode { get; private set; }
        public EntityIdentityV2 EndNode { get; private set; }
        public float StartDirectionX { get; private set; }
        public float StartDirectionY { get; private set; }
        public float StartDirectionZ { get; private set; }
        public float EndDirectionX { get; private set; }
        public float EndDirectionY { get; private set; }
        public float EndDirectionZ { get; private set; }
        public uint Flags { get; private set; }
        public uint Flags2 { get; private set; }

        public NetSegmentStateV2(EntityIdentityV2 entity, string prefabKey, EntityIdentityV2 startNode,
            EntityIdentityV2 endNode, float startDirectionX, float startDirectionY, float startDirectionZ,
            float endDirectionX, float endDirectionY, float endDirectionZ, uint flags, uint flags2)
        {
            if (!entity.IsValid || !startNode.IsValid || !endNode.IsValid)
                throw new ArgumentException("Invalid segment identity or endpoint.");
            if (startNode.Equals(endNode)) throw new ArgumentException("Segment endpoints must be distinct.");
            NetIntentV2.ValidatePrefab(prefabKey);
            NetControlPointV2.CheckFinite(startDirectionX); NetControlPointV2.CheckFinite(startDirectionY);
            NetControlPointV2.CheckFinite(startDirectionZ); NetControlPointV2.CheckFinite(endDirectionX);
            NetControlPointV2.CheckFinite(endDirectionY); NetControlPointV2.CheckFinite(endDirectionZ);
            Entity = entity; PrefabKey = prefabKey; StartNode = startNode; EndNode = endNode;
            StartDirectionX = startDirectionX; StartDirectionY = startDirectionY; StartDirectionZ = startDirectionZ;
            EndDirectionX = endDirectionX; EndDirectionY = endDirectionY; EndDirectionZ = endDirectionZ;
            Flags = flags; Flags2 = flags2;
        }
    }

    public sealed class NetMutationV2
    {
        public NetNodeStateV2[] UpsertNodes { get; private set; }
        public EntityIdentityV2[] DeleteNodes { get; private set; }
        public NetSegmentStateV2[] UpsertSegments { get; private set; }
        public EntityIdentityV2[] DeleteSegments { get; private set; }
        public int ConstructionCost { get; private set; }
        public int Refund { get; private set; }

        public NetMutationV2(NetNodeStateV2[] upsertNodes, EntityIdentityV2[] deleteNodes,
            NetSegmentStateV2[] upsertSegments, EntityIdentityV2[] deleteSegments, int constructionCost, int refund)
        {
            UpsertNodes = Copy(upsertNodes, "upsertNodes");
            DeleteNodes = Copy(deleteNodes, "deleteNodes");
            UpsertSegments = Copy(upsertSegments, "upsertSegments");
            DeleteSegments = Copy(deleteSegments, "deleteSegments");
            if (ConstructionCount > 4096) throw new ArgumentException("Net mutation is too large.");
            if (constructionCost < 0 || refund < 0) throw new ArgumentOutOfRangeException("cost");
            ConstructionCost = constructionCost; Refund = refund;
        }

        public int ConstructionCount { get { return UpsertNodes.Length + DeleteNodes.Length + UpsertSegments.Length + DeleteSegments.Length; } }

        private static NetNodeStateV2[] Copy(NetNodeStateV2[] values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            NetNodeStateV2[] result = (NetNodeStateV2[])values.Clone();
            for (int i = 0; i < result.Length; i++) if (result[i] == null) throw new ArgumentException("Null net node state.", name);
            return result;
        }
        private static NetSegmentStateV2[] Copy(NetSegmentStateV2[] values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            NetSegmentStateV2[] result = (NetSegmentStateV2[])values.Clone();
            for (int i = 0; i < result.Length; i++) if (result[i] == null) throw new ArgumentException("Null net segment state.", name);
            return result;
        }
        private static EntityIdentityV2[] Copy(EntityIdentityV2[] values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            EntityIdentityV2[] result = (EntityIdentityV2[])values.Clone();
            for (int i = 0; i < result.Length; i++) if (!result[i].IsValid) throw new ArgumentException("Invalid net entity identity.", name);
            return result;
        }
    }

    public sealed class NetStateIndexV2
    {
        private readonly SortedDictionary<ulong, NetNodeStateV2> nodes = new SortedDictionary<ulong, NetNodeStateV2>();
        private readonly SortedDictionary<ulong, NetSegmentStateV2> segments = new SortedDictionary<ulong, NetSegmentStateV2>();
        private Hash256 cachedRoot;
        public int NodeCount { get { return nodes.Count; } }
        public int SegmentCount { get { return segments.Count; } }

        /// <summary>
        /// WP-1.4c: the canonical encoding is memoized and every mutating path clears it —
        /// repeated Root reads on a surviving index stay cheap, and a missed invalidation is a
        /// stale-root bug caught by NetStateIndexCachedRootTests.
        /// </summary>
        public Hash256 Root
        {
            get
            {
                if (cachedRoot == null) cachedRoot = Hash256.Compute(EncodeCanonical());
                return cachedRoot;
            }
        }

        public void SeedNode(NetNodeStateV2 value) { UpsertNode(value, false); }
        public void SeedSegment(NetSegmentStateV2 value) { UpsertSegment(value, false); }

        public void Apply(NetMutationV2 mutation)
        {
            Check.NotNull(mutation, "mutation");
            cachedRoot = null; // WP-1.4c: mutation invalidates the memoized root even if it rejects later
            HashSet<ulong> deletedSegments = new HashSet<ulong>();
            for (int i = 0; i < mutation.DeleteSegments.Length; i++)
            {
                EntityIdentityV2 id = mutation.DeleteSegments[i];
                NetSegmentStateV2 current;
                if (!segments.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id))
                    throw new InvalidOperationException("Cannot delete unknown or stale net segment.");
                segments.Remove(id.EntityId); deletedSegments.Add(id.EntityId);
            }
            for (int i = 0; i < mutation.DeleteNodes.Length; i++)
            {
                EntityIdentityV2 id = mutation.DeleteNodes[i];
                NetNodeStateV2 current;
                if (!nodes.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id))
                    throw new InvalidOperationException("Cannot delete unknown or stale net node.");
                foreach (NetSegmentStateV2 segment in segments.Values)
                    if (segment.StartNode.Equals(id) || segment.EndNode.Equals(id))
                        throw new InvalidOperationException("Cannot delete a net node still referenced by a segment.");
                nodes.Remove(id.EntityId);
            }
            for (int i = 0; i < mutation.UpsertNodes.Length; i++) UpsertNode(mutation.UpsertNodes[i], true);
            for (int i = 0; i < mutation.UpsertSegments.Length; i++) UpsertSegment(mutation.UpsertSegments[i], true);
        }

        public bool TryGetNode(EntityIdentityV2 id, out NetNodeStateV2 value)
        {
            value = null; NetNodeStateV2 current;
            if (!id.IsValid || !nodes.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id)) return false;
            value = current; return true;
        }

        public bool TryGetSegment(EntityIdentityV2 id, out NetSegmentStateV2 value)
        {
            value = null; NetSegmentStateV2 current;
            if (!id.IsValid || !segments.TryGetValue(id.EntityId, out current) || !current.Entity.Equals(id)) return false;
            value = current; return true;
        }

        private void UpsertNode(NetNodeStateV2 value, bool allowReplace)
        {
            Check.NotNull(value, "value");
            NetNodeStateV2 current;
            if (nodes.TryGetValue(value.Entity.EntityId, out current))
            {
                if (!current.Entity.Equals(value.Entity)) throw new InvalidOperationException("Net node generation conflict.");
                if (!allowReplace) throw new InvalidOperationException("Net node already exists.");
                nodes[value.Entity.EntityId] = value; cachedRoot = null; return;
            }
            nodes.Add(value.Entity.EntityId, value); cachedRoot = null;
        }

        private void UpsertSegment(NetSegmentStateV2 value, bool allowReplace)
        {
            Check.NotNull(value, "value");
            NetNodeStateV2 start, end;
            if (!nodes.TryGetValue(value.StartNode.EntityId, out start) || !start.Entity.Equals(value.StartNode) ||
                !nodes.TryGetValue(value.EndNode.EntityId, out end) || !end.Entity.Equals(value.EndNode))
                throw new InvalidOperationException("Net segment references an unknown endpoint.");
            NetSegmentStateV2 current;
            if (segments.TryGetValue(value.Entity.EntityId, out current))
            {
                if (!current.Entity.Equals(value.Entity)) throw new InvalidOperationException("Net segment generation conflict.");
                if (!allowReplace) throw new InvalidOperationException("Net segment already exists.");
                segments[value.Entity.EntityId] = value; cachedRoot = null; return;
            }
            segments.Add(value.Entity.EntityId, value); cachedRoot = null;
        }

        private byte[] EncodeCanonical()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0x324E4746u); // FGN2
                writer.Write((uint)nodes.Count);
                foreach (NetNodeStateV2 node in nodes.Values) WriteNode(writer, node);
                writer.Write((uint)segments.Count);
                foreach (NetSegmentStateV2 segment in segments.Values) WriteSegment(writer, segment);
                writer.Flush(); return stream.ToArray();
            }
        }

        internal static void WriteNode(BinaryWriter writer, NetNodeStateV2 value)
        {
            WriteIdentity(writer, value.Entity); WriteString(writer, value.PrefabKey);
            writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); writer.Write(value.Flags);
        }

        internal static void WriteSegment(BinaryWriter writer, NetSegmentStateV2 value)
        {
            WriteIdentity(writer, value.Entity); WriteString(writer, value.PrefabKey);
            WriteIdentity(writer, value.StartNode); WriteIdentity(writer, value.EndNode);
            writer.Write(value.StartDirectionX); writer.Write(value.StartDirectionY); writer.Write(value.StartDirectionZ);
            writer.Write(value.EndDirectionX); writer.Write(value.EndDirectionY); writer.Write(value.EndDirectionZ);
            writer.Write(value.Flags); writer.Write(value.Flags2);
        }

        internal static void WriteIdentity(BinaryWriter writer, EntityIdentityV2 value)
        {
            writer.Write(value.EntityId); writer.Write(value.Generation);
        }

        internal static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length == 0 || bytes.Length > 192) throw new InvalidDataException("Invalid net prefab length.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
    }

    public static class NetDomainCodecV2
    {
        private const int MaxPayload = 60 * 1024;

        public static byte[] EncodeIntent(NetIntentV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream); writer.Write((byte)value.Kind);
                if (value.Kind == NetIntentKindV2.Create)
                {
                    NetStateIndexV2.WriteString(writer, value.PrefabKey);
                    WritePoint(writer, value.Start); WritePoint(writer, value.Middle); WritePoint(writer, value.End);
                    writer.Write(value.MaxSegments); writer.Write(value.TestEnds); writer.Write(value.AutoFix);
                    writer.Write(value.Invert); writer.Write(value.SwitchDirection); writer.Write(value.ZoneGridFlags);
                }
                else if (value.Kind == NetIntentKindV2.MultitoolCreateParallel)
                {
                    NetStateIndexV2.WriteString(writer, value.PrefabKey); writer.Write(value.Invert);
                    WriteSemanticPoints(writer, value.SemanticPoints);
                }
                else
                {
                    NetStateIndexV2.WriteIdentity(writer, value.Target);
                    if (value.Kind == NetIntentKindV2.DeleteSegment) writer.Write(value.KeepNodes);
                    else if (value.Kind == NetIntentKindV2.UpgradeSegment)
                    {
                        NetStateIndexV2.WriteString(writer, value.PrefabKey); writer.Write(value.UpgradeMode); writer.Write(value.UpgradeSide);
                    }
                    else if (value.Kind == NetIntentKindV2.MultitoolAddNode)
                        WritePosition(writer, value.X, value.Y, value.Z);
                    else if (value.Kind == NetIntentKindV2.MultitoolUnionNodes ||
                        value.Kind == NetIntentKindV2.MultitoolIntersectSegments)
                        NetStateIndexV2.WriteIdentity(writer, value.SecondaryTarget);
                    else if (value.Kind == NetIntentKindV2.MultitoolSplitNode)
                    {
                        WritePosition(writer, value.X, value.Y, value.Z);
                        writer.Write((byte)value.RelatedTargets.Length);
                        for (int i = 0; i < value.RelatedTargets.Length; i++) NetStateIndexV2.WriteIdentity(writer, value.RelatedTargets[i]);
                    }
                    else if (value.Kind == NetIntentKindV2.MultitoolCreateConnection)
                    {
                        NetStateIndexV2.WriteIdentity(writer, value.SecondaryTarget);
                        writer.Write(value.FirstStart); writer.Write(value.SecondStart);
                        NetStateIndexV2.WriteString(writer, value.PrefabKey);
                        writer.Write(value.Invert); writer.Write(value.FollowTerrain);
                        WriteSemanticPoints(writer, value.SemanticPoints);
                    }
                    else if (value.Kind != NetIntentKindV2.DeleteNode && value.Kind != NetIntentKindV2.MultitoolRemoveNode)
                        throw new InvalidDataException("Unknown net intent kind.");
                }
                writer.Flush(); return Checked(stream);
            }
        }

        public static NetIntentV2 DecodeIntent(byte[] bytes)
        {
            Validate(bytes);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                NetIntentKindV2 kind = (NetIntentKindV2)reader.ReadByte(); NetIntentV2 result;
                if (kind == NetIntentKindV2.Create)
                    result = NetIntentV2.Create(ReadString(reader), ReadPoint(reader), ReadPoint(reader), ReadPoint(reader),
                        reader.ReadInt32(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadUInt32());
                else if (kind == NetIntentKindV2.DeleteSegment)
                    result = NetIntentV2.DeleteSegment(ReadIdentity(reader), reader.ReadBoolean());
                else if (kind == NetIntentKindV2.DeleteNode)
                    result = NetIntentV2.DeleteNode(ReadIdentity(reader));
                else if (kind == NetIntentKindV2.UpgradeSegment)
                    result = NetIntentV2.UpgradeSegment(ReadIdentity(reader), ReadString(reader), reader.ReadByte(), reader.ReadBoolean());
                else if (kind == NetIntentKindV2.MultitoolAddNode)
                {
                    EntityIdentityV2 segment = ReadIdentity(reader); float x, y, z; ReadPosition(reader, out x, out y, out z);
                    result = NetIntentV2.MultitoolAddNode(segment, x, y, z);
                }
                else if (kind == NetIntentKindV2.MultitoolRemoveNode)
                    result = NetIntentV2.MultitoolRemoveNode(ReadIdentity(reader));
                else if (kind == NetIntentKindV2.MultitoolUnionNodes)
                    result = NetIntentV2.MultitoolUnionNodes(ReadIdentity(reader), ReadIdentity(reader));
                else if (kind == NetIntentKindV2.MultitoolSplitNode)
                {
                    EntityIdentityV2 source = ReadIdentity(reader); float x, y, z; ReadPosition(reader, out x, out y, out z);
                    int count = reader.ReadByte(); if (count < 1 || count > 7) throw new InvalidDataException("Invalid Multitool split segment count.");
                    EntityIdentityV2[] segments = new EntityIdentityV2[count];
                    for (int i = 0; i < count; i++) segments[i] = ReadIdentity(reader);
                    result = NetIntentV2.MultitoolSplitNode(source, x, y, z, segments);
                }
                else if (kind == NetIntentKindV2.MultitoolIntersectSegments)
                    result = NetIntentV2.MultitoolIntersectSegments(ReadIdentity(reader), ReadIdentity(reader));
                else if (kind == NetIntentKindV2.MultitoolCreateParallel)
                    result = NetIntentV2.MultitoolCreateParallel(ReadString(reader), reader.ReadBoolean(), ReadSemanticPoints(reader));
                else if (kind == NetIntentKindV2.MultitoolCreateConnection)
                {
                    EntityIdentityV2 first = ReadIdentity(reader), second = ReadIdentity(reader);
                    bool firstStart = reader.ReadBoolean(), secondStart = reader.ReadBoolean();
                    string prefab = ReadString(reader); bool invert = reader.ReadBoolean(), follow = reader.ReadBoolean();
                    result = NetIntentV2.MultitoolCreateConnection(first, second, firstStart, secondStart,
                        prefab, invert, follow, ReadSemanticPoints(reader));
                }
                else throw new InvalidDataException("Unknown net intent kind.");
                EnsureEnd(reader); return result;
            }
        }

        public static byte[] EncodeMutation(NetMutationV2 value)
        {
            Check.NotNull(value, "value");
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(value.ConstructionCost); writer.Write(value.Refund);
                WriteCount(writer, value.UpsertNodes.Length); for (int i = 0; i < value.UpsertNodes.Length; i++) NetStateIndexV2.WriteNode(writer, value.UpsertNodes[i]);
                WriteCount(writer, value.DeleteNodes.Length); for (int i = 0; i < value.DeleteNodes.Length; i++) NetStateIndexV2.WriteIdentity(writer, value.DeleteNodes[i]);
                WriteCount(writer, value.UpsertSegments.Length); for (int i = 0; i < value.UpsertSegments.Length; i++) NetStateIndexV2.WriteSegment(writer, value.UpsertSegments[i]);
                WriteCount(writer, value.DeleteSegments.Length); for (int i = 0; i < value.DeleteSegments.Length; i++) NetStateIndexV2.WriteIdentity(writer, value.DeleteSegments[i]);
                writer.Flush(); return Checked(stream);
            }
        }

        public static NetMutationV2 DecodeMutation(byte[] bytes)
        {
            Validate(bytes);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                int cost = reader.ReadInt32(); int refund = reader.ReadInt32();
                NetNodeStateV2[] upsertNodes = new NetNodeStateV2[ReadCount(reader)];
                for (int i = 0; i < upsertNodes.Length; i++) upsertNodes[i] = ReadNode(reader);
                EntityIdentityV2[] deleteNodes = new EntityIdentityV2[ReadCount(reader)];
                for (int i = 0; i < deleteNodes.Length; i++) deleteNodes[i] = ReadIdentity(reader);
                NetSegmentStateV2[] upsertSegments = new NetSegmentStateV2[ReadCount(reader)];
                for (int i = 0; i < upsertSegments.Length; i++) upsertSegments[i] = ReadSegment(reader);
                EntityIdentityV2[] deleteSegments = new EntityIdentityV2[ReadCount(reader)];
                for (int i = 0; i < deleteSegments.Length; i++) deleteSegments[i] = ReadIdentity(reader);
                EnsureEnd(reader);
                return new NetMutationV2(upsertNodes, deleteNodes, upsertSegments, deleteSegments, cost, refund);
            }
        }

        private static void WriteSemanticPoints(BinaryWriter writer, NetMultitoolPointV2[] points)
        {
            if (points == null || points.Length < 2 || points.Length > 512) throw new InvalidDataException("Invalid Multitool point count.");
            writer.Write((ushort)points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                NetMultitoolPointV2 p = points[i];
                writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z);
                writer.Write(p.ForwardX); writer.Write(p.ForwardY); writer.Write(p.ForwardZ);
                writer.Write(p.BackwardX); writer.Write(p.BackwardY); writer.Write(p.BackwardZ);
            }
        }

        private static NetMultitoolPointV2[] ReadSemanticPoints(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count < 2 || count > 512) throw new InvalidDataException("Invalid Multitool point count.");
            NetMultitoolPointV2[] result = new NetMultitoolPointV2[count];
            for (int i = 0; i < count; i++)
                result[i] = new NetMultitoolPointV2(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return result;
        }

        private static void WritePosition(BinaryWriter writer, float x, float y, float z)
        {
            writer.Write(x); writer.Write(y); writer.Write(z);
        }

        private static void ReadPosition(BinaryReader reader, out float x, out float y, out float z)
        {
            x = reader.ReadSingle(); y = reader.ReadSingle(); z = reader.ReadSingle();
        }

        private static void WritePoint(BinaryWriter writer, NetControlPointV2 value)
        {
            writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z);
            writer.Write(value.DirectionX); writer.Write(value.DirectionY); writer.Write(value.DirectionZ); writer.Write(value.Elevation);
            WriteOptionalIdentity(writer, value.ExistingNode); WriteOptionalIdentity(writer, value.ExistingSegment); writer.Write(value.Outside);
        }

        private static NetControlPointV2 ReadPoint(BinaryReader reader)
        {
            float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
            float dx = reader.ReadSingle(), dy = reader.ReadSingle(), dz = reader.ReadSingle(), elevation = reader.ReadSingle();
            EntityIdentityV2 node = ReadOptionalIdentity(reader), segment = ReadOptionalIdentity(reader); bool outside = reader.ReadBoolean();
            return new NetControlPointV2(x, y, z, dx, dy, dz, elevation, node, segment, outside);
        }

        private static NetNodeStateV2 ReadNode(BinaryReader reader)
        {
            EntityIdentityV2 entity = ReadIdentity(reader); string prefab = ReadString(reader);
            return new NetNodeStateV2(entity, prefab, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt32());
        }

        private static NetSegmentStateV2 ReadSegment(BinaryReader reader)
        {
            EntityIdentityV2 entity = ReadIdentity(reader); string prefab = ReadString(reader);
            EntityIdentityV2 start = ReadIdentity(reader), end = ReadIdentity(reader);
            return new NetSegmentStateV2(entity, prefab, start, end,
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadUInt32(), reader.ReadUInt32());
        }

        private static void WriteOptionalIdentity(BinaryWriter writer, EntityIdentityV2 value)
        {
            writer.Write(value.IsValid); if (value.IsValid) NetStateIndexV2.WriteIdentity(writer, value);
        }

        private static EntityIdentityV2 ReadOptionalIdentity(BinaryReader reader)
        {
            bool present = reader.ReadBoolean(); return present ? ReadIdentity(reader) : default(EntityIdentityV2);
        }

        private static EntityIdentityV2 ReadIdentity(BinaryReader reader)
        {
            return new EntityIdentityV2(reader.ReadUInt64(), reader.ReadUInt32());
        }

        private static string ReadString(BinaryReader reader)
        {
            ushort length = reader.ReadUInt16(); if (length == 0 || length > 192) throw new InvalidDataException("Invalid net prefab length.");
            byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteCount(BinaryWriter writer, int count)
        {
            if (count < 0 || count > 4096) throw new InvalidDataException("Invalid net mutation count."); writer.Write((ushort)count);
        }

        private static int ReadCount(BinaryReader reader)
        {
            ushort count = reader.ReadUInt16(); if (count > 4096) throw new InvalidDataException("Invalid net mutation count."); return count;
        }

        private static byte[] Checked(MemoryStream stream)
        {
            if (stream.Length <= 0 || stream.Length > MaxPayload) throw new InvalidDataException("Net payload exceeds frame budget.");
            return stream.ToArray();
        }

        private static void Validate(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayload) throw new InvalidDataException("Invalid net payload length.");
        }

        private static void EnsureEnd(BinaryReader reader)
        {
            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing net bytes.");
        }
    }
}
