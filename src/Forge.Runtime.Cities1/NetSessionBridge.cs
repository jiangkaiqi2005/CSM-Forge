using System;
using CsmForge.Core;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private NetAuthorityDomain hostNet;
        private NetReplicaDomain clientNet;

        private IAuthorityDomainV2[] CreateHostDomains(LoadIdentity identity)
        {
            hostWater = new WaterBudgetAuthorityDomain(identity);
            hostBuildings = new BuildingAuthorityDomain(identity);
            hostNet = new NetAuthorityDomain(identity);
            return new IAuthorityDomainV2[] { hostWater, hostBuildings, hostNet };
        }

        private IReplicaDomainV2[] CreateClientDomains(LoadIdentity identity)
        {
            clientWater = new WaterBudgetReplicaDomain(identity);
            clientBuildings = new BuildingReplicaDomain(identity);
            clientNet = new NetReplicaDomain(identity);
            return new IReplicaDomainV2[] { clientWater, clientBuildings, clientNet };
        }

        internal bool IsHostNetAuthorityActive
        {
            get { return mode == MultiplayerSessionMode.Hosting && hostNet != null && authority != null; }
        }

        internal bool IsClientNetReplicaActive
        {
            get { return mode == MultiplayerSessionMode.ClientLive && clientNet != null && replica != null; }
        }

        internal bool TryResolveClientNetNode(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsClientNetReplicaActive && clientNet.TryResolveNode(nativeId, out entity);
        }

        internal bool TryResolveClientNetSegment(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsClientNetReplicaActive && clientNet.TryResolveSegment(nativeId, out entity);
        }

        internal bool TryResolveHostNetNode(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsHostNetAuthorityActive && hostNet.TryResolveNode(nativeId, out entity);
        }

        internal bool TryResolveHostNetSegment(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return IsHostNetAuthorityActive && hostNet.TryResolveSegment(nativeId, out entity);
        }

        internal bool TrySubmitNetIntent(NetIntentV2 value)
        {
            if (value == null || snapshotSave != null) return false;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostNet == null || hostLocalOperation == ulong.MaxValue) return false;
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    NetAuthorityDomain.Id, hostNet.StateRoot, NetDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null) return false;
                BroadcastBatch(result.Batch);
                return true;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientNet == null || clientOperation == ulong.MaxValue) return false;
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    NetAuthorityDomain.Id, clientNet.StateRoot, NetDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "net-intent-" + clientOperation + ":pending";
                return true;
            }
            return false;
        }
    }
}
