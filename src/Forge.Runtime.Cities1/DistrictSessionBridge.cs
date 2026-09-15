using System;
using System.Collections.Generic;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private DistrictAuthorityDomain hostDistricts;
        private DistrictReplicaDomain clientDistricts;
        private readonly HashSet<byte> pendingLocalDistricts = new HashSet<byte>();
        private readonly Dictionary<ulong, byte> pendingDistrictByOperation = new Dictionary<ulong, byte>();
        private readonly Queue<byte> committedPendingDistricts = new Queue<byte>();

        internal void ClearDistrictClientPending()
        {
            pendingLocalDistricts.Clear();
            pendingDistrictByOperation.Clear();
            committedPendingDistricts.Clear();
        }

        internal bool RegisterPendingLocalDistrict(byte nativeId)
        {
            if (nativeId == 0 || mode != MultiplayerSessionMode.ClientLive || clientDistricts == null) return false;
            EntityIdentityV2 existing;
            if (clientDistricts.TryResolve(nativeId, out existing)) return false;
            return pendingLocalDistricts.Add(nativeId);
        }

        internal bool IsPendingLocalDistrict(byte nativeId)
        {
            return nativeId != 0 && pendingLocalDistricts.Contains(nativeId);
        }

        internal bool TryReleasePendingLocalDistrict(byte nativeId)
        {
            if (!pendingLocalDistricts.Remove(nativeId)) return false;
            List<ulong> remove = new List<ulong>();
            foreach (KeyValuePair<ulong, byte> pair in pendingDistrictByOperation)
                if (pair.Value == nativeId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) pendingDistrictByOperation.Remove(remove[i]);
            return true;
        }

        internal bool TryResolveClientDistrict(byte nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return clientDistricts != null && clientDistricts.TryResolve(nativeId, out entity);
        }

        internal bool TryResolveHostDistrict(byte nativeId, out EntityIdentityV2 entity)
        {
            entity = default(EntityIdentityV2);
            return hostDistricts != null && hostDistricts.TryResolve(nativeId, out entity);
        }

        internal bool TrySubmitDistrictPaint(DistrictPaintIntentV2 value, byte pendingNative)
        {
            if (value == null || snapshotSave != null || mode != MultiplayerSessionMode.ClientLive ||
                clientDistricts == null || replica == null || clientOperation == ulong.MaxValue) return false;
            if (value.TargetKind == DistrictPaintTargetKindV2.CreateNew)
            {
                if (pendingNative == 0 || !pendingLocalDistricts.Contains(pendingNative)) return false;
            }
            clientOperation++;
            if (value.TargetKind == DistrictPaintTargetKindV2.CreateNew)
            {
                pendingLocalDistricts.Remove(pendingNative);
                pendingDistrictByOperation.Add(clientOperation, pendingNative);
            }
            PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                DistrictAuthorityDomain.Id, clientDistricts.StateRoot, DistrictDomainCodecV2.EncodeIntent(value));
            SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
            lock (gate) detail = "district-intent-" + clientOperation + ":pending";
            return true;
        }

        internal bool TryQueueDistrictStyle(EntityIdentityV2 district, ushort style)
        {
            if (!district.IsValid || snapshotSave != null) return false;
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            DistrictStyleIntentV2 request = new DistrictStyleIntentV2(district, style);
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate { SubmitDistrictStyle(request); });
        }

        private void SubmitDistrictStyle(DistrictStyleIntentV2 value)
        {
            if (snapshotSave != null || value == null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostDistricts == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("district-style-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    DistrictAuthorityDomain.Id, hostDistricts.StateRoot, DistrictStyleCodecV2.Encode(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-district-style-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientDistricts == null || clientOperation == ulong.MaxValue)
                { FenceSession("district-style-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    DistrictAuthorityDomain.Id, clientDistricts.StateRoot, DistrictStyleCodecV2.Encode(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "district-style-" + clientOperation + ":pending";
            }
        }

        internal void HandleDistrictIntentReceipt(IntentReceiptV2 receipt)
        {
            byte native;
            if (receipt == null || !pendingDistrictByOperation.TryGetValue(receipt.OperationCounter, out native)) return;
            pendingDistrictByOperation.Remove(receipt.OperationCounter);
            if (receipt.Decision == AuthoritySubmitDecisionV2.Committed)
            {
                committedPendingDistricts.Enqueue(native);
                return;
            }
            try { DistrictGameAccess.Release(load, native); }
            catch { lifecycle.Fence("Rejected pending district could not be released"); }
        }

        internal bool TryTakeCommittedPendingDistrict(out byte native)
        {
            native = 0;
            if (committedPendingDistricts.Count == 0) return false;
            native = committedPendingDistricts.Dequeue();
            return native != 0 && DistrictGameAccess.Live(native);
        }

        internal Hash256 CaptureHostDistrictRoot()
        {
            return mode == MultiplayerSessionMode.Hosting && hostDistricts != null ? hostDistricts.StateRoot : null;
        }

        internal void PublishObservedHostDistrict(Hash256 beforeRoot)
        {
            if (beforeRoot == null || mode != MultiplayerSessionMode.Hosting || hostDistricts == null ||
                authority == null || snapshotSave != null) return;
            DistrictMutationV2 mutation = hostDistricts.ObserveHostWorld();
            if (mutation == null || mutation.Count == 0) return;
            Hash256 after = hostDistricts.StateRoot;
            AuthorityBatch batch = authority.PublishObserved(AuthorityOriginKind.Simulation, DistrictAuthorityDomain.Id,
                beforeRoot, after, DistrictDomainCodecV2.EncodeMutation(mutation));
            if (batch == null || authority.IsFenced)
            {
                FenceSession("observed-district-change-could-not-commit");
                return;
            }
            BroadcastBatch(batch);
        }
    }
}
