using System;
using System.Collections.Generic;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    internal static class DistrictPolicyGameAccess
    {
        public static DistrictPolicySnapshotV2 Capture(Func<uint, EntityIdentityV2?> resolve)
        {
            Check.NotNull(resolve, "resolve");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            District city = manager.m_districts.m_buffer[0];
            List<DistrictPolicyStateV2> districts = new List<DistrictPolicyStateV2>();
            int limit = manager.m_districts.m_buffer.Length;
            if (limit > byte.MaxValue + 1) limit = byte.MaxValue + 1;
            for (int i = 1; i < limit; i++)
            {
                byte native = (byte)i;
                if (!DistrictGameAccess.Live(native)) continue;
                EntityIdentityV2? identity = resolve((uint)i);
                if (!identity.HasValue || !identity.Value.IsValid)
                    throw new InvalidOperationException("Live district has no Forge identity while capturing policies.");
                District data = manager.m_districts.m_buffer[native];
                districts.Add(new DistrictPolicyStateV2(identity.Value,
                    Convert.ToUInt64(data.m_servicePolicies),
                    Convert.ToUInt64(data.m_taxationPolicies),
                    Convert.ToUInt64(data.m_cityPlanningPolicies),
                    Convert.ToUInt64(data.m_specialPolicies)));
            }
            return new DistrictPolicySnapshotV2(
                Convert.ToUInt64(city.m_servicePolicies),
                Convert.ToUInt64(city.m_taxationPolicies),
                Convert.ToUInt64(city.m_cityPlanningPolicies),
                Convert.ToUInt64(city.m_specialPolicies),
                districts.ToArray());
        }

        public static void ApplyIntent(LoadIdentity load, DistrictPolicyIntentV2 intent,
            Func<EntityIdentityV2, byte> resolveNative)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load))
                throw new InvalidOperationException("District policy apply belongs to a stale load.");
            Check.NotNull(intent, "intent");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            DistrictPolicies.Policies policy = (DistrictPolicies.Policies)intent.PolicyValue;
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id))
            {
                if (intent.TargetKind == DistrictPolicyTargetKindV2.City)
                {
                    if (intent.Enabled) manager.SetCityPolicy(policy);
                    else manager.UnsetCityPolicy(policy);
                }
                else
                {
                    byte native = resolveNative(intent.District);
                    if (intent.Enabled) manager.SetDistrictPolicy(policy, native);
                    else manager.UnsetDistrictPolicy(policy, native);
                }
            }
        }

        public static void InstallSnapshot(LoadIdentity load, DistrictPolicySnapshotV2 snapshot,
            Func<EntityIdentityV2, byte> resolveNative)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load))
                throw new InvalidOperationException("District policy projection belongs to a stale load.");
            Check.NotNull(snapshot, "snapshot");
            DistrictManager manager = DistrictManager.instance;
            if (manager == null) throw new InvalidOperationException("DistrictManager is unavailable.");
            using (RuntimeScopeGuard.EnterApply(load, DistrictAuthorityDomain.Id))
            {
                District city = manager.m_districts.m_buffer[0];
                city.m_servicePolicies = (DistrictPolicies.Services)snapshot.CityServices;
                city.m_taxationPolicies = (DistrictPolicies.Taxation)snapshot.CityTaxation;
                city.m_cityPlanningPolicies = (DistrictPolicies.CityPlanning)snapshot.CityPlanning;
                city.m_specialPolicies = (DistrictPolicies.Special)snapshot.CitySpecial;
                manager.m_districts.m_buffer[0] = city;

                DistrictPolicyStateV2[] values = snapshot.Districts;
                for (int i = 0; i < values.Length; i++)
                {
                    byte native = resolveNative(values[i].District);
                    if (!DistrictGameAccess.Live(native))
                        throw new InvalidOperationException("Policy snapshot targets a missing district.");
                    District data = manager.m_districts.m_buffer[native];
                    data.m_servicePolicies = (DistrictPolicies.Services)values[i].Services;
                    data.m_taxationPolicies = (DistrictPolicies.Taxation)values[i].Taxation;
                    data.m_cityPlanningPolicies = (DistrictPolicies.CityPlanning)values[i].CityPlanning;
                    data.m_specialPolicies = (DistrictPolicies.Special)values[i].Special;
                    manager.m_districts.m_buffer[native] = data;
                }
            }
        }
    }

    public sealed class DistrictCompositeAuthorityDomain : IAuthorityDomainV2
    {
        private readonly LoadIdentity load;
        private readonly DistrictAuthorityDomain district;
        private DistrictPolicySnapshotV2 committedPolicies;

        public ushort DomainId { get { return DistrictAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return DistrictCombinedRootV2.Combine(district.StateRoot, committedPolicies.Root); } }

        public DistrictCompositeAuthorityDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
            district = new DistrictAuthorityDomain(load);
            committedPolicies = CapturePolicies();
        }

        public bool TryResolve(uint nativeId, out EntityIdentityV2 identity)
        {
            return district.TryResolve(nativeId, out identity);
        }

        public DomainExecutionV2 ExecutePlayer(byte[] payload)
        {
            if (!RuntimeServices.Lifecycle.IsCurrent(load) || RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive)
                return DomainExecutionV2.Rejected();

            if (DistrictPolicyCodecV2.LooksLikePolicy(payload))
            {
                DistrictPolicyIntentV2 policy;
                try { policy = DistrictPolicyCodecV2.Decode(payload); }
                catch { return DomainExecutionV2.Rejected(); }
                try
                {
                    DistrictPolicyGameAccess.ApplyIntent(load, policy, ResolveNative);
                    DistrictPolicySnapshotV2 actual = CapturePolicies();
                    if (actual.Root.Equals(committedPolicies.Root)) return DomainExecutionV2.Rejected();
                    committedPolicies = actual;
                    DistrictAuthorityEnvelopeV2 envelope = new DistrictAuthorityEnvelopeV2(
                        DistrictPolicyEnvelopeCodecV2.EmptyMutation(), district.StateRoot, committedPolicies);
                    return DomainExecutionV2.Success(DistrictPolicyEnvelopeCodecV2.EncodeEnvelope(envelope), StateRoot);
                }
                catch { return DomainExecutionV2.Rejected(); }
            }

            DomainExecutionV2 child = district.ExecutePlayer(payload);
            if (child == null || !child.Applied) return DomainExecutionV2.Rejected();
            DistrictMutationV2 mutation = DistrictDomainCodecV2.DecodeMutation(child.AbsoluteDelta);
            committedPolicies = CapturePolicies();
            DistrictAuthorityEnvelopeV2 wrapped = new DistrictAuthorityEnvelopeV2(mutation,
                district.StateRoot, committedPolicies);
            return DomainExecutionV2.Success(DistrictPolicyEnvelopeCodecV2.EncodeEnvelope(wrapped), StateRoot);
        }

        internal DistrictAuthorityEnvelopeV2 ObserveHostWorld()
        {
            DistrictMutationV2 mutation = district.ObserveHostWorld();
            DistrictPolicySnapshotV2 actual = CapturePolicies();
            bool policyChanged = !actual.Root.Equals(committedPolicies.Root);
            if (mutation == null && !policyChanged) return null;
            committedPolicies = actual;
            return new DistrictAuthorityEnvelopeV2(mutation ?? DistrictPolicyEnvelopeCodecV2.EmptyMutation(),
                district.StateRoot, committedPolicies);
        }

        internal bool HasSourceDirtyShards() { return district.HasSourceDirtyShards(); }

        /// <summary>WP-1.4b cheap path: reconciles only the shards the game hooks marked.</summary>
        internal DistrictAuthorityEnvelopeV2 ObserveHostSourceDirty()
        {
            DistrictMutationV2 mutation = district.ObserveHostSourceDirty();
            if (mutation == null) return null;
            committedPolicies = CapturePolicies();
            return new DistrictAuthorityEnvelopeV2(mutation, district.StateRoot, committedPolicies);
        }

        internal void MarkCellSourceDirty(uint cellIndex) { district.MarkCellSourceDirty(cellIndex); }

        internal void MarkAllCellsSourceDirty() { district.MarkAllCellsSourceDirty(); }

        private DistrictPolicySnapshotV2 CapturePolicies()
        {
            return DistrictPolicyGameAccess.Capture(delegate(uint native)
            {
                EntityIdentityV2 identity;
                return district.TryResolve(native, out identity) ? (EntityIdentityV2?)identity : null;
            });
        }

        private byte ResolveNative(EntityIdentityV2 identity)
        {
            for (uint i = 1; i <= byte.MaxValue; i++)
            {
                EntityIdentityV2 current;
                if (district.TryResolve(i, out current) && current.Equals(identity)) return (byte)i;
            }
            throw new InvalidOperationException("Stable district identity has no native Host mapping.");
        }
    }

    public sealed class DistrictCompositeReplicaDomain : IReplicaDomainV2
    {
        private readonly LoadIdentity load;
        private readonly DistrictReplicaDomain district;
        private DistrictPolicySnapshotV2 committedPolicies;

        public ushort DomainId { get { return DistrictAuthorityDomain.Id; } }
        public Hash256 StateRoot { get { return DistrictCombinedRootV2.Combine(district.StateRoot, committedPolicies.Root); } }

        public DistrictCompositeReplicaDomain(LoadIdentity load)
        {
            if (!load.IsValid) throw new ArgumentException("Invalid load identity.", "load");
            this.load = load;
            district = new DistrictReplicaDomain(load);
            committedPolicies = CapturePolicies();
        }

        public bool TryResolve(uint nativeId, out EntityIdentityV2 identity)
        {
            return district.TryResolve(nativeId, out identity);
        }

        public void ApplyAbsolute(byte[] absoluteDelta, Hash256 expectedAfterRoot)
        {
            Check.NotNull(expectedAfterRoot, "expectedAfterRoot");
            DistrictAuthorityEnvelopeV2 envelope = DistrictPolicyEnvelopeCodecV2.DecodeEnvelope(absoluteDelta);
            byte[] childBytes = DistrictDomainCodecV2.EncodeMutation(envelope.DistrictMutation);
            district.ApplyAbsolute(childBytes, envelope.DistrictAfterRoot);
            DistrictPolicyGameAccess.InstallSnapshot(load, envelope.Policies, ResolveNative);
            DistrictPolicySnapshotV2 actual = CapturePolicies();
            if (!actual.Root.Equals(envelope.Policies.Root))
                throw new InvalidOperationException("District policy projection root mismatch.");
            committedPolicies = actual;
            if (!StateRoot.Equals(expectedAfterRoot))
                throw new InvalidOperationException("Composite district projection root mismatch.");
        }

        private DistrictPolicySnapshotV2 CapturePolicies()
        {
            return DistrictPolicyGameAccess.Capture(delegate(uint native)
            {
                EntityIdentityV2 identity;
                return district.TryResolve(native, out identity) ? (EntityIdentityV2?)identity : null;
            });
        }

        private byte ResolveNative(EntityIdentityV2 identity)
        {
            for (uint i = 1; i <= byte.MaxValue; i++)
            {
                EntityIdentityV2 current;
                if (district.TryResolve(i, out current) && current.Equals(identity)) return (byte)i;
            }
            throw new InvalidOperationException("Stable district identity has no native replica mapping.");
        }
    }
}
