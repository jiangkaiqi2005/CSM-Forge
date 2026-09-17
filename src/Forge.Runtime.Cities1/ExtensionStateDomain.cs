using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class CitiesExtensionStateRegistry
    {
        private readonly ForgeStateAdapterRegistration[] adapters;

        public CitiesExtensionStateRegistry(ForgeStateAdapterRegistration[] registrations)
        {
            if (registrations == null) throw new ArgumentNullException("registrations");
            adapters = (ForgeStateAdapterRegistration[])registrations.Clone();
            for (int i = 1; i < adapters.Length; i++)
                if (StringComparer.Ordinal.Compare(adapters[i - 1].AdapterId, adapters[i].AdapterId) >= 0)
                    throw new ArgumentException("Forge adapter registrations must be unique and canonically ordered.", "registrations");
        }

        public ExtensionStateSnapshotV2 Capture(LoadIdentity load)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension capture belongs to a stale load.");
            List<ExtensionStateEntryV2> entries = new List<ExtensionStateEntryV2>(adapters.Length);
            using (RuntimeScopeGuard.EnterCapture(load))
            {
                for (int i = 0; i < adapters.Length; i++)
                {
                    byte[] state = adapters[i].Adapter.CaptureAbsolute();
                    if (state == null) throw new InvalidOperationException("Forge adapter returned a null absolute state: " + adapters[i].AdapterId);
                    entries.Add(new ExtensionStateEntryV2(adapters[i].AdapterId, "state", state));
                }
            }
            return new ExtensionStateSnapshotV2(entries);
        }

        public ExtensionStateSnapshotV2 Apply(LoadIdentity load, ExtensionStateSnapshotV2 snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension apply belongs to a stale load.");
            ExtensionStateEntryV2[] entries = snapshot.Entries;
            if (entries.Length != adapters.Length) throw new InvalidOperationException("Extension adapter set differs from the accepted compatibility manifest.");
            Dictionary<string, ExtensionStateEntryV2> byAdapter = new Dictionary<string, ExtensionStateEntryV2>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Key != "state" || byAdapter.ContainsKey(entries[i].AdapterId))
                    throw new InvalidOperationException("Extension snapshot contains an invalid adapter entry.");
                byAdapter.Add(entries[i].AdapterId, entries[i]);
            }

            using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
            {
                for (int i = 0; i < adapters.Length; i++)
                {
                    ExtensionStateEntryV2 entry;
                    if (!byAdapter.TryGetValue(adapters[i].AdapterId, out entry))
                        throw new InvalidOperationException("Extension snapshot is missing adapter: " + adapters[i].AdapterId);
                    adapters[i].Adapter.ApplyAbsolute(entry.Payload);
                }
            }
            return Capture(load);
        }
    }

    internal sealed class ExtensionStateAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 120;
        private readonly LoadIdentity load;
        private readonly CitiesExtensionStateRegistry registry;
        private ExtensionStateSnapshotV2 committed;

        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return committed.Root; } }

        public ExtensionStateAuthorityDomain(LoadIdentity load, CitiesExtensionStateRegistry registry)
        {
            if (!load.IsValid || registry == null) throw new ArgumentException("Extension authority initialization is incomplete.");
            this.load = load;
            this.registry = registry;
            committed = registry.Capture(load);
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload) { return DomainExecutionV2.Rejected(); }

        public byte[] ObserveHost(out Hash256 beforeRoot, out Hash256 afterRoot)
        {
            beforeRoot = committed.Root;
            ExtensionStateSnapshotV2 actual = registry.Capture(load);
            afterRoot = actual.Root;
            if (beforeRoot.Equals(afterRoot)) return null;
            committed = actual;
            return ExtensionStateCodecV2.Encode(actual);
        }
    }

    internal sealed class ExtensionStateReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private readonly CitiesExtensionStateRegistry registry;
        private ExtensionStateSnapshotV2 committed;

        public ushort DomainId { get { return ExtensionStateAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return committed.Root; } }

        public ExtensionStateReplicaDomain(LoadIdentity load, CitiesExtensionStateRegistry registry)
        {
            if (!load.IsValid || registry == null) throw new ArgumentException("Extension replica initialization is incomplete.");
            this.load = load;
            this.registry = registry;
            committed = registry.Capture(load);
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            if (expectedAfterRoot == null) throw new ArgumentNullException("expectedAfterRoot");
            ExtensionStateSnapshotV2 requested = ExtensionStateCodecV2.Decode(absoluteDelta);
            ExtensionStateSnapshotV2 actual = registry.Apply(load, requested);
            if (!actual.Root.Equals(requested.Root)) throw new InvalidOperationException("Extension adapter absolute projection did not reproduce the requested root.");
            committed = actual;
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Extension replica root mismatch.");
        }

        public Hash256 CaptureActualRoot() { return registry.Capture(load).Root; }
    }

    public sealed partial class CitiesMultiplayerSessionV3
    {
        private ExtensionStateAuthorityDomain hostExtensions;
        private ExtensionStateReplicaDomain clientExtensions;

        internal void PollObservedHostExtensions()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostExtensions == null || authority == null || snapshotSave != null) return;
            Hash256 before;
            Hash256 after;
            byte[] payload = hostExtensions.ObserveHost(out before, out after);
            if (payload == null) return;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, ExtensionStateAuthorityDomain.Id,
                before, after, payload);
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-extension-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }
    }
}
