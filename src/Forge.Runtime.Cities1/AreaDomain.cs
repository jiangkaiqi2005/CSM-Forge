using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class AreaGameAccess
    {
        public static AreaStateV2 Capture()
        {
            GameAreaManager manager = GameAreaManager.instance;
            if (manager == null) throw new InvalidOperationException("GameAreaManager is unavailable.");
            uint mask = 0;
            for (int z = 0; z < 5; z++)
                for (int x = 0; x < 5; x++)
                    if (manager.IsUnlocked(x, z)) mask |= 1u << (z * 5 + x);
            return new AreaStateV2(mask);
        }

        public static AreaStateV2 Unlock(LoadIdentity load, AreaUnlockIntentV2 intent)
        {
            Check.NotNull(intent, "intent");
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Area unlock belongs to a stale load.");
            GameAreaManager manager = GameAreaManager.instance;
            if (manager == null) throw new InvalidOperationException("GameAreaManager is unavailable.");
            if (manager.IsUnlocked(intent.X, intent.Z)) return Capture();
            int index = intent.Z * 5 + intent.X;
            bool result;
            using (RuntimeScopeGuard.EnterApply(load, AreaAuthorityDomain.Id)) result = manager.UnlockArea(index);
            if (!result || !manager.IsUnlocked(intent.X, intent.Z))
                throw new InvalidOperationException("CS1 rejected authoritative area unlock.");
            return Capture();
        }

        public static AreaStateV2 Install(LoadIdentity load, AreaStateV2 target)
        {
            Check.NotNull(target, "target");
            AreaStateV2 current = Capture();
            if ((current.UnlockedMask & ~target.UnlockedMask) != 0)
                throw new InvalidOperationException("Replica has an extra unlocked area and requires a snapshot rebaseline.");
            for (int z = 0; z < 5; z++)
                for (int x = 0; x < 5; x++)
                    if (target.IsUnlocked(x, z) && !current.IsUnlocked(x, z))
                        Unlock(load, new AreaUnlockIntentV2(x, z));
            AreaStateV2 actual = Capture();
            if (actual.UnlockedMask != target.UnlockedMask)
                throw new InvalidOperationException("Area projection mismatch.");
            return actual;
        }
    }

    public sealed class AreaAuthorityDomain : IAuthorityDomainV2
    {
        public const ushort Id = 7;
        private readonly LoadIdentity load;
        private Hash256 committedRoot;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return AreaGameAccess.Capture().Root; } }
        internal Hash256 CommittedRoot { get { return committedRoot; } }

        public AreaAuthorityDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load; committedRoot = StateRoot;
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            AreaUnlockIntentV2 intent;
            try { intent = AreaDomainCodecV2.DecodeIntent(payload); } catch { return DomainExecutionV2.Rejected(); }
            Hash256 before = StateRoot;
            if (AreaGameAccess.Capture().IsUnlocked(intent.X, intent.Z)) return DomainExecutionV2.Rejected();
            AreaStateV2 actual;
            try { actual = AreaGameAccess.Unlock(load, intent); } catch { return DomainExecutionV2.Rejected(); }
            if (actual.Root.Equals(before)) return DomainExecutionV2.Rejected();
            committedRoot = actual.Root;
            return DomainExecutionV2.Success(AreaDomainCodecV2.EncodeState(actual), actual.Root);
        }

        internal void MarkObservedCommitted(Hash256 root) { committedRoot = root; }
    }

    public sealed class AreaReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private AreaStateV2 committed;
        public ushort DomainId { get { return AreaAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return committed.Root; } }

        public AreaReplicaDomain(LoadIdentity load)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            this.load = load; committed = AreaGameAccess.Capture();
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            AreaStateV2 requested = AreaDomainCodecV2.DecodeState(absoluteDelta);
            committed = AreaGameAccess.Install(load, requested);
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Area replica root mismatch.");
        }
    }
}
