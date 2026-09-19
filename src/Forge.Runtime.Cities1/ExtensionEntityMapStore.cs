using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class ForgeNamedEntityMapStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, NamedEntityMapSnapshotV1> pending =
            new Dictionary<string, NamedEntityMapSnapshotV1>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntityIdMapV2> current =
            new Dictionary<string, EntityIdMapV2>(StringComparer.Ordinal);

        public void LoadPending(byte[] bytes)
        {
            NamedEntityMapSnapshotV1[] values = NamedEntityMapSaveCodecV1.Decode(bytes);
            lock (gate)
            {
                pending.Clear();
                current.Clear();
                for (int i = 0; i < values.Length; i++) pending.Add(values[i].NamespaceId, values[i]);
            }
        }

        public EntityIdMapV2 GetOrAttach(string namespaceId)
        {
            ValidateNamespace(namespaceId);
            lock (gate)
            {
                EntityIdMapV2 map;
                if (current.TryGetValue(namespaceId, out map)) return map;
                map = new EntityIdMapV2();
                NamedEntityMapSnapshotV1 saved;
                if (pending.TryGetValue(namespaceId, out saved))
                {
                    map.RestoreSnapshot(saved.Entries, saved.HighestIssuedId);
                    pending.Remove(namespaceId);
                }
                current.Add(namespaceId, map);
                return map;
            }
        }

        public void SuspendCurrent()
        {
            lock (gate)
            {
                foreach (KeyValuePair<string, EntityIdMapV2> pair in current)
                    pending[pair.Key] = Snapshot(pair.Key, pair.Value);
                current.Clear();
            }
        }

        public byte[] EncodeCurrent()
        {
            lock (gate)
            {
                Dictionary<string, NamedEntityMapSnapshotV1> combined =
                    new Dictionary<string, NamedEntityMapSnapshotV1>(pending, StringComparer.Ordinal);
                foreach (KeyValuePair<string, EntityIdMapV2> pair in current)
                    combined[pair.Key] = Snapshot(pair.Key, pair.Value);
                return NamedEntityMapSaveCodecV1.Encode(combined.Values);
            }
        }

        public string[] ActiveNamespaces()
        {
            lock (gate)
            {
                string[] values = new string[current.Count + pending.Count];
                int index = 0;
                foreach (string key in current.Keys) values[index++] = key;
                foreach (string key in pending.Keys) if (!current.ContainsKey(key)) values[index++] = key;
                if (index != values.Length) Array.Resize(ref values, index);
                Array.Sort(values, StringComparer.Ordinal);
                return values;
            }
        }

        public void Clear()
        {
            lock (gate) { pending.Clear(); current.Clear(); }
        }

        private static NamedEntityMapSnapshotV1 Snapshot(string namespaceId, EntityIdMapV2 map)
        {
            return new NamedEntityMapSnapshotV1(namespaceId, map.HighestIssuedId, map.SnapshotEntries());
        }

        private static void ValidateNamespace(string value)
        {
            Check.Condition(string.IsNullOrEmpty(value) || value.Length > 80, "namespaceId", "Invalid extension identity namespace.");
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_'))
                    throw new ArgumentException("Extension identity namespaces use canonical lowercase ASCII.", "namespaceId");
        }
    }

    internal static class ExtensionIdentityServices
    {
        internal static readonly ForgeNamedEntityMapStore Maps = new ForgeNamedEntityMapStore();
    }
}
