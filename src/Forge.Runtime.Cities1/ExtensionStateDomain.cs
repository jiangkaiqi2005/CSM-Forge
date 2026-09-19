using System;
using System.Collections.Generic;
using System.Globalization;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class CitiesExtensionStateRegistry
    {
        private sealed class EntryDescriptor
        {
            public int AdapterIndex;
            public int ShardIndex;
            public string Key;
        }

        private readonly ForgeStateAdapterRegistration[] adapters;
        private readonly ForgeAdapterContextV1[] contexts;
        private readonly EntryDescriptor[] entries;
        private readonly Dictionary<string, int> adapterIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> entryIndexByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count { get { return entries.Length; } }

        public CitiesExtensionStateRegistry(ForgeStateAdapterRegistration[] registrations, bool authoritative)
        {
            if (registrations == null) throw new ArgumentNullException("registrations");
            adapters = (ForgeStateAdapterRegistration[])registrations.Clone();
            contexts = new ForgeAdapterContextV1[adapters.Length];
            List<EntryDescriptor> descriptors = new List<EntryDescriptor>();
            for (int i = 0; i < adapters.Length; i++)
            {
                if (i > 0 && StringComparer.Ordinal.Compare(adapters[i - 1].AdapterId, adapters[i].AdapterId) >= 0)
                    throw new ArgumentException("Forge adapter registrations must be unique and canonically ordered.", "registrations");
                contexts[i] = new ForgeAdapterContextV1(adapters[i].AdapterId, authoritative);
                adapterIndexById.Add(adapters[i].AdapterId, i);
                if (adapters[i].Sharded == null)
                {
                    descriptors.Add(new EntryDescriptor { AdapterIndex = i, ShardIndex = -1, Key = "state" });
                }
                else
                {
                    int count = adapters[i].Sharded.ShardCount;
                    if (count <= 0 || count > 1024) throw new InvalidOperationException("Invalid sharded adapter size: " + adapters[i].AdapterId);
                    for (int shard = 0; shard < count; shard++)
                        descriptors.Add(new EntryDescriptor
                        {
                            AdapterIndex = i,
                            ShardIndex = shard,
                            Key = "shard:" + shard.ToString("D4", CultureInfo.InvariantCulture)
                        });
                }
            }
            if (descriptors.Count > ExtensionStateCodecV2.MaximumEntries)
                throw new InvalidOperationException("Extension state entry limit exceeded by registered adapters.");
            entries = descriptors.ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                string adapterId = adapters[entries[i].AdapterIndex].AdapterId;
                entryIndexByIdentity.Add(EntryIdentity(adapterId, entries[i].Key), i);
            }
        }

        public ExtensionStateEntryV2[] CaptureAll(LoadIdentity load)
        {
            ExtensionStateEntryV2[] result = new ExtensionStateEntryV2[entries.Length];
            for (int i = 0; i < result.Length; i++) result[i] = CaptureOne(load, i);
            return result;
        }

        public ExtensionStateEntryV2 CaptureOne(LoadIdentity load, int entryIndex)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension capture belongs to a stale load.");
            if (entryIndex < 0 || entryIndex >= entries.Length) throw new ArgumentOutOfRangeException("entryIndex");
            EntryDescriptor descriptor = entries[entryIndex];
            ForgeStateAdapterRegistration registration = adapters[descriptor.AdapterIndex];
            byte[] state;
            using (RuntimeScopeGuard.EnterCapture(load))
            {
                if (registration.Sharded != null)
                    state = registration.Sharded.CaptureShard(contexts[descriptor.AdapterIndex], descriptor.ShardIndex);
                else if (registration.AdapterV2 != null)
                    state = registration.AdapterV2.CaptureAbsolute(contexts[descriptor.AdapterIndex]);
                else state = registration.AdapterV1.CaptureAbsolute();
            }
            if (state == null) throw new InvalidOperationException("Forge adapter returned a null absolute state: " + registration.AdapterId);
            return new ExtensionStateEntryV2(registration.AdapterId, descriptor.Key, state);
        }

        public ExtensionStateEntryV2 ApplyOne(LoadIdentity load, ExtensionStateEntryV2 requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            int entryIndex = FindEntryIndex(requested.AdapterId, requested.Key);
            if (entryIndex < 0) throw new InvalidOperationException("Extension delta references an unaccepted adapter state entry.");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension apply belongs to a stale load.");
            EntryDescriptor descriptor = entries[entryIndex];
            ForgeStateAdapterRegistration registration = adapters[descriptor.AdapterIndex];
            using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
            {
                if (registration.Sharded != null)
                    registration.Sharded.ApplyShard(contexts[descriptor.AdapterIndex], descriptor.ShardIndex, requested.Payload);
                else if (registration.AdapterV2 != null)
                    registration.AdapterV2.ApplyAbsolute(contexts[descriptor.AdapterIndex], requested.Payload);
                else registration.AdapterV1.ApplyAbsolute(requested.Payload);
            }
            return CaptureOne(load, entryIndex);
        }

        public bool ExecutePlayer(LoadIdentity load, ExtensionPlayerIntentV2 request, out int adapterIndex)
        {
            adapterIndex = FindAdapterIndex(request == null ? null : request.AdapterId);
            if (adapterIndex < 0 || request == null || !RuntimeServices.Lifecycle.IsCurrent(load)) return false;
            ForgeStateAdapterRegistration registration = adapters[adapterIndex];
            if (!contexts[adapterIndex].IsAuthoritative) return false;
            bool applied;
            using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
            {
                IForgeInteractiveStateAdapterV1 normal = registration.AdapterV2 as IForgeInteractiveStateAdapterV1;
                IForgeInteractiveShardedStateAdapterV1 sharded = registration.Sharded as IForgeInteractiveShardedStateAdapterV1;
                if (normal != null) applied = normal.ExecuteIntent(contexts[adapterIndex], request.Payload);
                else if (sharded != null) applied = sharded.ExecuteIntent(contexts[adapterIndex], request.Payload);
                else return false;
            }
            return applied;
        }

        public int FindAdapterIndex(string adapterId)
        {
            int index;
            return adapterId != null && adapterIndexById.TryGetValue(adapterId, out index) ? index : -1;
        }

        public int FindEntryIndex(string adapterId, string key)
        {
            int index;
            return adapterId != null && key != null && entryIndexByIdentity.TryGetValue(EntryIdentity(adapterId, key), out index) ? index : -1;
        }

        public int EntryAdapterIndex(int entryIndex)
        {
            if (entryIndex < 0 || entryIndex >= entries.Length) throw new ArgumentOutOfRangeException("entryIndex");
            return entries[entryIndex].AdapterIndex;
        }

        private static string EntryIdentity(string adapterId, string key) { return adapterId + "\n" + key; }
    }

    internal sealed class ExtensionObservedChange
    {
        public Hash256 BeforeRoot;
        public Hash256 AfterRoot;
        public byte[] Delta;
    }

    internal sealed class ExtensionStateAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 120;
        private readonly LoadIdentity load;
        private readonly CitiesExtensionStateRegistry registry;
        private readonly ExtensionStateEntryV2[] committed;
        private int observationCursor;

        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return ExtensionStateSnapshotV2.ComputeAggregateRoot(committed); } }

        public ExtensionStateAuthorityDomain(LoadIdentity load, CitiesExtensionStateRegistry registry)
        {
            if (!load.IsValid || registry == null) throw new ArgumentException("Extension authority initialization is incomplete.");
            this.load = load;
            this.registry = registry;
            committed = registry.CaptureAll(load);
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            ExtensionPlayerIntentV2 request;
            try { request = ExtensionIntentCodecV2.Decode(payload); }
            catch { return DomainExecutionV2.Rejected(); }
            int adapterIndex;
            if (!registry.ExecutePlayer(load, request, out adapterIndex)) return DomainExecutionV2.Rejected();
            for (int i = 0; i < committed.Length; i++)
            {
                if (registry.EntryAdapterIndex(i) != adapterIndex) continue;
                ExtensionStateEntryV2 actual = registry.CaptureOne(load, i);
                if (committed[i].PayloadRoot.Equals(actual.PayloadRoot)) continue;
                committed[i] = actual;
                return DomainExecutionV2.Success(ExtensionStateCodecV2.EncodeDelta(actual), StateRoot);
            }
            return DomainExecutionV2.Rejected();
        }

        /// <summary>Poll exactly one bounded entry; caller controls per-tick work.</summary>
        public bool PollNextHostEntry(out ExtensionObservedChange change)
        {
            change = null;
            if (committed.Length == 0) return false;
            int index = observationCursor++ % committed.Length;
            ExtensionStateEntryV2 actual = registry.CaptureOne(load, index);
            if (committed[index].PayloadRoot.Equals(actual.PayloadRoot)) return true;
            Hash256 before = StateRoot;
            committed[index] = actual;
            Hash256 after = StateRoot;
            change = new ExtensionObservedChange
            {
                BeforeRoot = before,
                AfterRoot = after,
                Delta = ExtensionStateCodecV2.EncodeDelta(actual)
            };
            return true;
        }
    }

    internal sealed class ExtensionStateReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private readonly CitiesExtensionStateRegistry registry;
        private readonly ExtensionStateEntryV2[] committed;

        public ushort DomainId { get { return ExtensionStateAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return ExtensionStateSnapshotV2.ComputeAggregateRoot(committed); } }

        public ExtensionStateReplicaDomain(LoadIdentity load, CitiesExtensionStateRegistry registry)
        {
            if (!load.IsValid || registry == null) throw new ArgumentException("Extension replica initialization is incomplete.");
            this.load = load;
            this.registry = registry;
            committed = registry.CaptureAll(load);
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            ExtensionStateEntryV2 requested = ExtensionStateCodecV2.DecodeDelta(absoluteDelta);
            int index = registry.FindEntryIndex(requested.AdapterId, requested.Key);
            if (index < 0 || index >= committed.Length) throw new InvalidOperationException("Extension delta references an unknown adapter state entry.");
            ExtensionStateEntryV2 actual = registry.ApplyOne(load, requested);
            if (!actual.PayloadRoot.Equals(requested.PayloadRoot))
                throw new InvalidOperationException("Extension adapter absolute projection did not reproduce the requested payload root.");
            committed[index] = actual;
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Extension replica root mismatch.");
        }

        public Hash256 CaptureActualRoot() { return ExtensionStateSnapshotV2.ComputeAggregateRoot(registry.CaptureAll(load)); }
    }

    public sealed partial class CitiesMultiplayerSessionV3
    {
        private ExtensionStateAuthorityDomain hostExtensions;
        private ExtensionStateReplicaDomain clientExtensions;

        internal void PollObservedHostExtensions()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostExtensions == null || authority == null || snapshotSave != null) return;
            ExtensionObservedChange change;
            if (!hostExtensions.PollNextHostEntry(out change) || change == null) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, ExtensionStateAuthorityDomain.Id,
                change.BeforeRoot, change.AfterRoot, change.Delta);
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-extension-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }
    }
}
