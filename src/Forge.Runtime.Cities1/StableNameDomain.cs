using System;
using System.Collections;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class StableNameGameAccess
    {
        public static InstanceID Instance(StableNameTargetKindV2 kind, uint nativeId)
        {
            InstanceID id = default(InstanceID);
            if (kind == StableNameTargetKindV2.Building)
            {
                if (nativeId == 0 || nativeId > ushort.MaxValue) throw new ArgumentOutOfRangeException("nativeId");
                id.Building = (ushort)nativeId;
            }
            else if (kind == StableNameTargetKindV2.NetSegment)
            {
                if (nativeId == 0 || nativeId > ushort.MaxValue) throw new ArgumentOutOfRangeException("nativeId");
                id.NetSegment = (ushort)nativeId;
            }
            else if (kind == StableNameTargetKindV2.District)
            {
                if (nativeId == 0 || nativeId > byte.MaxValue) throw new ArgumentOutOfRangeException("nativeId");
                id.District = (byte)nativeId;
            }
            else if (kind == StableNameTargetKindV2.TransportLine)
            {
                if (nativeId == 0 || nativeId > ushort.MaxValue) throw new ArgumentOutOfRangeException("nativeId");
                id.TransportLine = (ushort)nativeId;
            }
            else throw new ArgumentOutOfRangeException("kind");
            return id;
        }

        public static string Capture(StableNameTargetKindV2 kind, uint nativeId)
        {
            string value = InstanceManager.instance.GetName(Instance(kind, nativeId));
            return value ?? string.Empty;
        }

        public static string Apply(LoadIdentity load, StableNameStateV2 requested, uint nativeId)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load)) throw new InvalidOperationException("Name apply belongs to a stale load.");
            Check.NotNull(requested, "requested");
            IEnumerator action;
            using (RuntimeScopeGuard.EnterApply(load, StableNameAuthorityDomain.Id))
            {
                if (requested.Key.Kind == StableNameTargetKindV2.Building)
                    action = BuildingManager.instance.SetBuildingName((ushort)nativeId, requested.Name);
                else if (requested.Key.Kind == StableNameTargetKindV2.NetSegment)
                    action = NetManager.instance.SetSegmentName((ushort)nativeId, requested.Name);
                else if (requested.Key.Kind == StableNameTargetKindV2.District)
                    action = DistrictManager.instance.SetDistrictName((int)nativeId, requested.Name);
                else if (requested.Key.Kind == StableNameTargetKindV2.TransportLine)
                    action = TransportManager.instance.SetLineName((ushort)nativeId, requested.Name);
                else throw new InvalidOperationException("Unsupported stable-name target.");
                if (action != null) action.MoveNext();
            }
            return Capture(requested.Key.Kind, nativeId);
        }
    }

    internal abstract class StableNameDomainBase
    {
        protected readonly LoadIdentity Load;
        protected readonly bool HostSide;
        protected readonly StableNameStateIndexV2 Index = new StableNameStateIndexV2();
        private readonly Dictionary<StableNameKeyV2, uint> nativeHints = new Dictionary<StableNameKeyV2, uint>();

        protected StableNameDomainBase(LoadIdentity load, bool hostSide)
        {
            Check.Condition(!load.IsValid, "load", "Invalid load identity.");
            Load = load; HostSide = hostSide; Seed();
        }

        public Hash256 CurrentRoot
        {
            get { PruneDead(); return Index.Root; }
        }

        protected uint ResolveNative(StableNameKeyV2 key)
        {
            uint hinted;
            EntityIdentityV2 identity;
            if (nativeHints.TryGetValue(key, out hinted) &&
                RuntimeServices.Multiplayer.TryResolveStableNameIdentity(key.Kind, hinted, HostSide, out identity) &&
                identity.Equals(key.Entity)) return hinted;
            uint native;
            if (!RuntimeServices.Multiplayer.TryResolveStableNameNative(key.Kind, key.Entity, HostSide, out native))
                throw new InvalidOperationException("Stable-name entity has no native mapping.");
            nativeHints[key] = native;
            return native;
        }

        protected void ApplyCommitted(StableNameStateV2 state, uint native)
        {
            if (string.IsNullOrEmpty(state.Name))
            {
                Index.Remove(state.Key); nativeHints.Remove(state.Key);
            }
            else
            {
                Index.Set(state); nativeHints[state.Key] = native;
            }
        }

        private void Seed()
        {
            StableNameTargetKindV2[] kinds = new[]
            {
                StableNameTargetKindV2.Building, StableNameTargetKindV2.NetSegment,
                StableNameTargetKindV2.District, StableNameTargetKindV2.TransportLine
            };
            for (int k = 0; k < kinds.Length; k++)
            {
                uint upper = RuntimeServices.Multiplayer.StableNameNativeUpperBound(kinds[k]);
                for (uint native = 1; native < upper; native++)
                {
                    EntityIdentityV2 identity;
                    if (!RuntimeServices.Multiplayer.TryResolveStableNameIdentity(kinds[k], native, HostSide, out identity)) continue;
                    string name = StableNameGameAccess.Capture(kinds[k], native);
                    if (string.IsNullOrEmpty(name)) continue;
                    StableNameStateV2 state = new StableNameStateV2(new StableNameKeyV2(kinds[k], identity), name);
                    Index.Set(state); nativeHints[state.Key] = native;
                }
            }
        }

        private void PruneDead()
        {
            StableNameKeyV2[] keys = Index.Keys();
            for (int i = 0; i < keys.Length; i++)
            {
                uint native;
                try { native = ResolveNative(keys[i]); }
                catch
                {
                    Index.Remove(keys[i]); nativeHints.Remove(keys[i]); continue;
                }
                EntityIdentityV2 identity;
                if (!RuntimeServices.Multiplayer.TryResolveStableNameIdentity(keys[i].Kind, native, HostSide, out identity) ||
                    !identity.Equals(keys[i].Entity))
                {
                    Index.Remove(keys[i]); nativeHints.Remove(keys[i]);
                }
            }
        }
    }

    internal sealed class StableNameAuthorityDomain : StableNameDomainBase, IAuthorityDomainV2
    {
        public const ushort Id = 8;
        public ushort DomainId { get { return Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public StableNameAuthorityDomain(LoadIdentity load) : base(load, true) { }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(Load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();
            StableNameStateV2 requested;
            try { requested = StableNameCodecV2.Decode(payload); } catch { return DomainExecutionV2.Rejected(); }
            uint native;
            try { native = ResolveNative(requested.Key); } catch { return DomainExecutionV2.Rejected(); }
            string actual;
            try { actual = StableNameGameAccess.Apply(Load, requested, native); } catch { return DomainExecutionV2.Rejected(); }
            StableNameStateV2 result = new StableNameStateV2(requested.Key, actual);
            ApplyCommitted(result, native);
            return DomainExecutionV2.Success(StableNameCodecV2.Encode(result), StateRoot);
        }
    }

    internal sealed class StableNameReplicaDomain : StableNameDomainBase, IReplicaDomainV2
    {
        public ushort DomainId { get { return StableNameAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return CurrentRoot; } }
        public StableNameReplicaDomain(LoadIdentity load) : base(load, false) { }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            StableNameStateV2 requested = StableNameCodecV2.Decode(absoluteDelta);
            uint native = ResolveNative(requested.Key);
            string actual = StableNameGameAccess.Apply(Load, requested, native);
            StableNameStateV2 installed = new StableNameStateV2(requested.Key, actual);
            if (!StringComparer.Ordinal.Equals(installed.Name, requested.Name))
                throw new InvalidOperationException("Stable name projection mismatch.");
            ApplyCommitted(installed, native);
            if (!StateRoot.Equals(expectedAfterRoot)) throw new InvalidOperationException("Stable name root mismatch.");
        }
    }
}
