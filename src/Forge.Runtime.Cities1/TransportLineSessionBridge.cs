using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal bool TryResolveClientTransportLine(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return mode == MultiplayerSessionMode.ClientLive && clientTransport != null && clientTransport.TryResolveEntity(nativeId, out entity);
        }

        internal bool TryResolveHostTransportLine(ushort nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return mode == MultiplayerSessionMode.Hosting && hostTransport != null && hostTransport.TryResolveEntity(nativeId, out entity);
        }

        internal bool TryQueueTransportProperties(ushort nativeId, byte red, byte green, byte blue, byte alpha,
            ushort budget, ushort ticketPrice, bool day, bool night)
        {
            EntityIdentityV2 entity;
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (!TryResolveClientTransportLine(nativeId, out entity)) return false;
            }
            else if (mode == MultiplayerSessionMode.Hosting)
            {
                if (!TryResolveHostTransportLine(nativeId, out entity)) return false;
            }
            else return false;
            return SubmitTransportIntent(new TransportLineIntentV2(TransportLineIntentKindV2.SetProperties, entity,
                red, green, blue, alpha, budget, ticketPrice, day, night));
        }

        internal bool TryQueueTransportRelease(ushort nativeId)
        {
            EntityIdentityV2 entity;
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (!TryResolveClientTransportLine(nativeId, out entity)) return false;
            }
            else if (mode == MultiplayerSessionMode.Hosting)
            {
                if (!TryResolveHostTransportLine(nativeId, out entity)) return false;
            }
            else return false;
            return SubmitTransportIntent(new TransportLineIntentV2(TransportLineIntentKindV2.Release, entity,
                0, 0, 0, 0, 0, 0, false, false));
        }

        private bool SubmitTransportIntent(TransportLineIntentV2 value)
        {
            if (value == null || snapshotSave != null) return false;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostTransport == null || hostLocalOperation == ulong.MaxValue) return false;
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    TransportLineAuthorityDomain.Id, hostTransport.StateRoot, TransportLineDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null) return false;
                BroadcastBatch(result.Batch); return true;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientTransport == null || clientOperation == ulong.MaxValue) return false;
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    TransportLineAuthorityDomain.Id, clientTransport.StateRoot, TransportLineDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "transport-intent-" + clientOperation + ":pending";
                return true;
            }
            return false;
        }

        internal void PollObservedHostTransportLines()
        {
            if (mode != MultiplayerSessionMode.Hosting || hostTransport == null || authority == null || snapshotSave != null) return;
            Hash256 before = hostTransport.StateRoot;
            TransportLineMutationV2 mutation = hostTransport.ObserveHostWorld();
            if (mutation == null || mutation.Count == 0) return;
            Hash256 after = hostTransport.StateRoot;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, TransportLineAuthorityDomain.Id,
                before, after, TransportLineDomainCodecV2.EncodeMutation(mutation));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-transport-line-change-could-not-commit"); return;
            }
            BroadcastBatch(batch);
        }
    }
}
