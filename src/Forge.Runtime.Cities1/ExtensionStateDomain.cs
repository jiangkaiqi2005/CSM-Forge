using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class CitiesExtensionStateRegistry
    {
        private readonly ForgeStateAdapterRegistration[] adapters;
        private readonly ForgeAdapterContextV1[] contexts;
        private readonly Dictionary<string, int> indexByAdapter = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count { get { return adapters.Length; } }

        public CitiesExtensionStateRegistry(ForgeStateAdapterRegistration[] registrations, bool authoritative)
        {
            if (registrations == null) throw new ArgumentNullException("registrations");
            adapters = (ForgeStateAdapterRegistration[])registrations.Clone();
            contexts = new ForgeAdapterContextV1[adapters.Length];
            for (int i = 0; i < adapters.Length; i++)
            {
                if (i > 0 && StringComparer.Ordinal.Compare(adapters[i - 1].AdapterId, adapters[i].AdapterId) >= 0)
                    throw new ArgumentException("Forge adapter registrations must be unique and canonically ordered.", "registrations");
                contexts[i] = new ForgeAdapterContextV1(adapters[i].AdapterId, authoritative);
                indexByAdapter.Add(adapters[i].AdapterId, i);
            }
        }

        public ExtensionStateEntryV2[] CaptureAll(LoadIdentity load)
        {
            ExtensionStateEntryV2[] entries = new ExtensionStateEntryV2[adapters.Length];
            for (int i = 0; i < entries.Length; i++) entries[i] = CaptureOne(load, i);
            return entries;
        }

        public ExtensionStateEntryV2 CaptureOne(LoadIdentity load, int index)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension capture belongs to a stale load.");
            if (index < 0 || index >= adapters.Length) throw new ArgumentOutOfRangeException("index");
            byte[] state;
            using (RuntimeScopeGuard.EnterCapture(load))
            {
                state = adapters[index].AdapterV2 != null
                    ? adapters[index].AdapterV2.CaptureAbsolute(contexts[index])
                    : adapters[index].AdapterV1.CaptureAbsolute();
            }
            if (state == null) throw new InvalidOperationException("Forge adapter returned a null absolute state: " + adapters[index].AdapterId);
            return new ExtensionStateEntryV2(adapters[index].AdapterId, "state", state);
        }

        public ExtensionStateEntryV2 ApplyOne(LoadIdentity load, ExtensionStateEntryV2 requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            if (requested.Key != "state") throw new InvalidOperationException("Extension delta uses an unsupported state key.");
            int index = FindIndex(requested.AdapterId);
            if (index < 0) throw new InvalidOperationException("Extension delta references an unaccepted adapter: " + requested.AdapterId);
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Extension apply belongs to a stale load.");
            using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
            {
                if (adapters[index].AdapterV2 != null) adapters[index].AdapterV2.ApplyAbsolute(contexts[index], requested.Payload);
                else adapters[index].AdapterV1.ApplyAbsolute(requested.Payload);
            }
            return CaptureOne(load, index);
        }

        public bool ExecutePlayer(LoadIdentity load, ExtensionPlayerIntentV2 request,
            out int index, out ExtensionStateEntryV2 actual)
        {
            index = FindIndex(request == null ? null : request.AdapterId);
            actual = null;
            if (index < 0 || request == null || !RuntimeServices.Lifecycle.IsCurrent(load)) return false;
            IForgeInteractiveStateAdapterV1 interactive = adapters[index].AdapterV2 as IForgeInteractiveStateAdapterV1;
            if (interactive == null || !contexts[index].IsAuthoritative) return false;
            bool applied;
            using (RuntimeScopeGuard.EnterApply(load, ExtensionStateAuthorityDomain.Id))
                applied = interactive.ExecuteIntent(contexts[index], request.Payload);
            if (!applied) return false;
            actual = CaptureOne(load, index);
            return true;
        }

        public int FindIndex(string adapterId)
        {
            int index;
            return adapterId != null && indexByAdapter.TryGetValue(adapterId, out index) ? index : -1;
        }
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
            int index;
            ExtensionStateEntryV2 actual;
            if (!registry.ExecutePlayer(load, request, out index, out actual) || index < 0 || index >= committed.Length || actual == null)
                return DomainExecutionV2.Rejected();
            if (committed[index].PayloadRoot.Equals(actual.PayloadRoot)) return DomainExecutionV2.Rejected();
            committed[index] = actual;
            return DomainExecutionV2.Success(ExtensionStateCodecV2.EncodeDelta(actual), StateRoot);
        }

        public ExtensionObservedChange ObserveNextHostChange()
        {
            if (committed.Length == 0) return null;
            for (int scanned = 0; scanned < committed.Length; scanned++)
            {
                int index = observationCursor++ % committed.Length;
                ExtensionStateEntryV2 actual = registry.CaptureOne(load, index);
                if (committed[index].PayloadRoot.Equals(actual.PayloadRoot)) continue;
                Hash256 before = StateRoot;
                committed[index] = actual;
                Hash256 after = StateRoot;
                return new ExtensionObservedChange { BeforeRoot = before, AfterRoot = after, Delta = ExtensionStateCodecV2.EncodeDelta(actual) };
            }
            return null;
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
            int index = registry.FindIndex(requested.AdapterId);
            if (index < 0 || index >= committed.Length) throw new InvalidOperationException("Extension delta references an unknown adapter.");
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
            for (int i = 0; i < 8; i++)
            {
                ExtensionObservedChange change = hostExtensions.ObserveNextHostChange();
                if (change == null) return;
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
}
