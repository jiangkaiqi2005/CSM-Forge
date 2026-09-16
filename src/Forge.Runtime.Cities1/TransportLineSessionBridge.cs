using System;
using System.Collections.Generic;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private readonly HashSet<ushort> pendingLocalTransportLines = new HashSet<ushort>();
        private readonly Dictionary<ulong, ushort> pendingTransportByOperation = new Dictionary<ulong, ushort>();
        private readonly Queue<ushort> committedPendingTransportLines = new Queue<ushort>();

        internal bool RegisterPendingLocalTransportLine(ushort nativeId)
        {
            if (nativeId == 0 || mode != MultiplayerSessionMode.ClientLive || clientTransport == null) return false;
            EntityIdentityV2 existing;
            if (clientTransport.TryResolveEntity(nativeId, out existing)) return false;
            return pendingLocalTransportLines.Add(nativeId);
        }

        internal bool IsPendingLocalTransportLine(ushort nativeId)
        {
            if (nativeId == 0) return false;
            if (pendingLocalTransportLines.Contains(nativeId)) return true;
            foreach (ushort value in pendingTransportByOperation.Values) if (value == nativeId) return true;
            foreach (ushort value in committedPendingTransportLines) if (value == nativeId) return true;
            return false;
        }

        internal bool TrySubmitPendingTransportLine(ushort nativeId)
        {
            if (mode != MultiplayerSessionMode.ClientLive || clientTransport == null || replica == null ||
                snapshotSave != null || clientOperation == ulong.MaxValue || !pendingLocalTransportLines.Contains(nativeId)) return false;
            TransportLineIntentV2 create;
            try { create = TransportLineGameAccess.CaptureCreateIntent(nativeId); }
            catch { return false; }
            if (!create.Complete || create.Stops.Length < 2) return false;
            clientOperation++;
            pendingLocalTransportLines.Remove(nativeId);
            pendingTransportByOperation.Add(clientOperation, nativeId);
            PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                TransportLineAuthorityDomain.Id, clientTransport.StateRoot, TransportLineDomainCodecV2.EncodeIntent(create));
            SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
            lock (gate) detail = "transport-create-" + clientOperation + ":pending";
            return true;
        }

        internal void HandleTransportIntentReceipt(IntentReceiptV2 receipt)
        {
            if (receipt == null) return;
            ushort native;
            if (!pendingTransportByOperation.TryGetValue(receipt.OperationCounter, out native)) return;
            pendingTransportByOperation.Remove(receipt.OperationCounter);
            if (receipt.Decision == AuthoritySubmitDecisionV2.Committed)
            {
                committedPendingTransportLines.Enqueue(native);
                return;
            }
            try { TransportLineGameAccess.Release(load, native); }
            catch { lifecycle.Fence("Rejected pending transport line could not be released"); }
        }

        internal bool TryTakeCommittedPendingTransportLine(out ushort native)
        {
            native = 0;
            if (committedPendingTransportLines.Count == 0) return false;
            native = committedPendingTransportLines.Dequeue();
            return native != 0 && TransportLineGameAccess.Live(native);
        }

        internal void ClearTransportClientPending()
        {
            pendingLocalTransportLines.Clear();
            pendingTransportByOperation.Clear();
            committedPendingTransportLines.Clear();
        }

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

        internal bool TryQueueTransportRoute(ushort nativeId, TransportLineIntentKindV2 kind, int index,
            float x, float y, float z, bool fixedPlatform)
        {
            if (kind != TransportLineIntentKindV2.AddStop && kind != TransportLineIntentKindV2.RemoveStop &&
                kind != TransportLineIntentKindV2.MoveStop) return false;
            EntityIdentityV2 entity;
            if (!TryResolveClientTransportLine(nativeId, out entity)) return false;
            TransportStopV2 stop = kind == TransportLineIntentKindV2.RemoveStop ? null : new TransportStopV2(x, y, z, fixedPlatform);
            return SubmitTransportIntent(new TransportLineIntentV2(kind, entity, index, stop));
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
