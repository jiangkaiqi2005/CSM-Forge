using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private StableNameAuthorityDomain hostNames;
        private StableNameReplicaDomain clientNames;

        internal uint StableNameNativeUpperBound(StableNameTargetKindV2 kind)
        {
            if (kind == StableNameTargetKindV2.Building)
                return BuildingManager.instance == null ? 0u : (uint)BuildingManager.instance.m_buildings.m_buffer.Length;
            if (kind == StableNameTargetKindV2.NetSegment)
                return NetManager.instance == null ? 0u : (uint)NetManager.instance.m_segments.m_buffer.Length;
            if (kind == StableNameTargetKindV2.District)
            {
                if (DistrictManager.instance == null) return 0u;
                int length = DistrictManager.instance.m_districts.m_buffer.Length;
                if (length > byte.MaxValue + 1) length = byte.MaxValue + 1;
                return (uint)length;
            }
            if (kind == StableNameTargetKindV2.TransportLine)
                return TransportManager.instance == null ? 0u : (uint)TransportManager.instance.m_lines.m_buffer.Length;
            return 0u;
        }

        internal bool TryResolveStableNameIdentity(StableNameTargetKindV2 kind, uint nativeId,
            bool hostSide, out EntityIdentityV2 identity)
        {
            identity = default(EntityIdentityV2);
            if (nativeId == 0) return false;
            if (kind == StableNameTargetKindV2.Building)
            {
                if (nativeId > ushort.MaxValue) return false;
                return hostSide
                    ? hostBuildings != null && hostBuildings.TryResolveEntity(nativeId, out identity)
                    : clientBuildings != null && clientBuildings.TryResolveEntity(nativeId, out identity);
            }
            if (kind == StableNameTargetKindV2.NetSegment)
            {
                if (nativeId > ushort.MaxValue) return false;
                return hostSide
                    ? hostNet != null && hostNet.TryResolveSegment((ushort)nativeId, out identity)
                    : clientNet != null && clientNet.TryResolveSegment((ushort)nativeId, out identity);
            }
            if (kind == StableNameTargetKindV2.District)
            {
                if (nativeId > byte.MaxValue) return false;
                return hostSide
                    ? hostDistricts != null && hostDistricts.TryResolve(nativeId, out identity)
                    : clientDistricts != null && clientDistricts.TryResolve(nativeId, out identity);
            }
            if (kind == StableNameTargetKindV2.TransportLine)
            {
                if (nativeId > ushort.MaxValue) return false;
                return hostSide
                    ? hostTransport != null && hostTransport.TryResolveEntity((ushort)nativeId, out identity)
                    : clientTransport != null && clientTransport.TryResolveEntity((ushort)nativeId, out identity);
            }
            return false;
        }

        internal bool TryResolveStableNameNative(StableNameTargetKindV2 kind, EntityIdentityV2 identity,
            bool hostSide, out uint nativeId)
        {
            nativeId = 0;
            if (!identity.IsValid) return false;
            uint upper = StableNameNativeUpperBound(kind);
            for (uint candidate = 1; candidate < upper; candidate++)
            {
                EntityIdentityV2 current;
                if (TryResolveStableNameIdentity(kind, candidate, hostSide, out current) && current.Equals(identity))
                {
                    nativeId = candidate;
                    return true;
                }
            }
            return false;
        }

        internal bool TryQueueStableName(StableNameTargetKindV2 kind, uint nativeId, string name)
        {
            bool hostSide = mode == MultiplayerSessionMode.Hosting;
            if (!hostSide && mode != MultiplayerSessionMode.ClientLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolveStableNameIdentity(kind, nativeId, hostSide, out identity)) return false;
            StableNameStateV2 value;
            try { value = new StableNameStateV2(new StableNameKeyV2(kind, identity), name ?? string.Empty); }
            catch { return false; }
            if (snapshotSave != null) return false;
            LoadIdentity current = load;
            if (!current.IsValid || !lifecycle.IsCurrent(current)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(current, delegate { SubmitStableName(value); });
        }

        private void SubmitStableName(StableNameStateV2 value)
        {
            if (value == null || snapshotSave != null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostNames == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("name-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    StableNameAuthorityDomain.Id, hostNames.StateRoot, StableNameCodecV2.Encode(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-name-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientNames == null || clientOperation == ulong.MaxValue)
                { FenceSession("name-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    StableNameAuthorityDomain.Id, clientNames.StateRoot, StableNameCodecV2.Encode(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "name-" + clientOperation + ":pending";
            }
        }
    }
}
