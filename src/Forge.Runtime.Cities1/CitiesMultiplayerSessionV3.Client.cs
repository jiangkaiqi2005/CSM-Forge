using System;
using System.IO;
using System.Net;
using CsmForge.Checkpoints;
using CsmForge.Core;
using CsmForge.Protocol;
using CsmForge.Transport.LiteNet;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private void StartClientOnSimulation(LoadIdentity identity, IPEndPoint endpoint, string roomKey, string displayName)
        {
            if (!lifecycle.IsCurrent(identity)) { SetOffline("stale-client-start"); return; }
            try
            {
                load = identity;
                clientName = displayName;
                localManifest = CitiesCompatibilityCollector.Collect();
                clientManifestPages = BootstrapMessagesV2.CreateManifestPages(localManifest);
                client = new LiteNetClientTransport();
                if (!client.Start(endpoint, roomKey)) throw new InvalidOperationException("Could not start LiteNet client.");
                if (!lifecycle.TryTransition(load, CitiesRuntimeRole.ClientLoading))
                    throw new InvalidOperationException("Could not enter ClientLoading runtime role.");
                lock (gate) { mode = MultiplayerSessionMode.ConnectingClient; detail = "connecting-development-transport"; }
            }
            catch (Exception error) { AbortStart("client-start:" + error.GetType().Name); }
        }

        private void DrainClientEvents()
        {
            TransportEvent item;
            int processed = 0;
            while (processed++ < 128 && client.TryTake(out item))
            {
                if (item.Kind == TransportEventKind.Connected) { SendClientBootstrap(); continue; }
                if (item.Kind == TransportEventKind.Disconnected)
                {
                    if (replica == null && !preserveAcrossLevelLoad) AbortStart("client-disconnected:" + item.Detail);
                    else FenceSession("client-disconnected:" + item.Detail);
                    continue;
                }
                if (item.Kind == TransportEventKind.Error) { events.Record(RuntimeEventCode.Error, load.Generation, item.Detail); continue; }
                if (item.Kind != TransportEventKind.Data) continue;
                if (clientSessionReady) HandleClientSessionFrame(item.Payload);
                else HandleClientBootstrap(item.Payload);
            }
        }

        private void SendClientBootstrap()
        {
            HelloV2 hello = new HelloV2(clientInstanceId, clientName, localManifest.GameBuildHash,
                localManifest.SchemaHash, (ushort)clientManifestPages.Length, BootstrapAuthMode.DevelopmentRoomKeyOnly);
            SendClientBootstrapFrame(BootstrapKind.Hello, BootstrapMessagesV2.EncodeHello(hello));
            SendClientBootstrapFrame(BootstrapKind.IdentityProof,
                BootstrapMessagesV2.EncodeIdentityProof(BootstrapAuthMode.DevelopmentRoomKeyOnly, clientInstanceId));
            foreach (ManifestPageV2 page in clientManifestPages)
                SendClientBootstrapFrame(BootstrapKind.ManifestPage, BootstrapMessagesV2.EncodeManifestPage(page));
        }

        private void HandleClientBootstrap(byte[] bytes)
        {
            BootstrapFrame frame = BootstrapCodec.Decode(bytes);
            if (frame.Kind == BootstrapKind.CompatibilityResult)
            {
                CompatibilityResultV2 result = BootstrapMessagesV2.DecodeCompatibilityResult(frame.Payload);
                if (!result.Accepted) { AbortStart("compatibility-rejected:" + result.Reason); return; }
                clientCompatibilityAccepted = true;
                return;
            }
            if (frame.Kind == BootstrapKind.SessionWelcome)
            {
                if (!clientCompatibilityAccepted) throw new InvalidOperationException("SessionWelcome arrived before compatibility acceptance.");
                SessionWelcomeV2 welcome = BootstrapMessagesV2.DecodeWelcome(frame.Payload);
                clientStamp = welcome.Stamp; clientBinding = welcome.ConnectionBinding; clientMember = welcome.Member;
                clientPermissionVersion = welcome.PermissionVersion; clientSequences = new LaneSequenceTracker(); clientSessionReady = true;
                return;
            }
            throw new InvalidOperationException("Unexpected Host bootstrap message.");
        }

        private void HandleClientSessionFrame(byte[] bytes)
        {
            SessionFrameV2 frame = SessionFrameCodecV2.Decode(bytes);
            if (!frame.Stamp.Equals(clientStamp) || frame.ConnectionBinding != clientBinding || !clientSequences.Accept(frame.Lane, frame.Sequence))
                throw new InvalidOperationException("Client rejected session frame identity or sequence.");
            switch (frame.Kind)
            {
                case MessageKindV2.SnapshotOffer: HandleSnapshotOffer(JoinMessagesV2.DecodeSnapshotOffer(frame.Payload)); break;
                case MessageKindV2.SnapshotChunk: HandleSnapshotChunk(SnapshotTransferMessagesV2.DecodeChunk(frame.Payload)); break;
                case MessageKindV2.AuthorityBatch: HandleClientBatch(SessionMessagesV2.DecodeBatch(frame.Stamp, frame.Payload)); break;
                case MessageKindV2.ReplayBarrier: HandleReplayBarrier(JoinMessagesV2.DecodeReplayBarrier(frame.Payload)); break;
                case MessageKindV2.ActivationGrant: HandleActivationGrant(JoinMessagesV2.DecodeActivationGrant(frame.Payload)); break;
                case MessageKindV2.IntentReceipt:
                    IntentReceiptV2 receipt = ControlMessagesV2.DecodeReceipt(frame.Payload);
                    HandleDistrictIntentReceipt(receipt);
                    lock (gate) detail = "intent-" + receipt.OperationCounter + ":" + receipt.Decision;
                    break;
                default: throw new InvalidOperationException("Host message is not valid in the current Client path.");
            }
        }

        private void HandleSnapshotOffer(SnapshotOfferV2 offer)
        {
            if (!offer.RequiresTransfer) throw new InvalidOperationException("V3 requires a transfer-backed host snapshot.");
            if (replica != null)
            {
                lifecycle.TryTransition(load, CitiesRuntimeRole.ClientRecovering);
                RuntimeServices.EntityMaps.SuspendCurrent();
                ClearDistrictClientPending();
                replica = null;
                clientWater = null;
                clientDemand = null;
                clientTaxes = null;
                clientBuildings = null;
                clientNet = null;
                clientZones = null;
                clientDistricts = null;
            }
            if (clientSnapshot != null) clientSnapshot.Dispose();
            string path = Path.Combine(Path.GetTempPath(), "csm-forge-snapshot-" + offer.TransferId.ToString("N") + ".crp");
            clientOffer = offer;
            clientSnapshot = new SnapshotReceiveFile(offer, path);
            lock (gate) { mode = MultiplayerSessionMode.ClientCatchingUp; detail = "downloading-snapshot"; }
        }

        private void HandleSnapshotChunk(SnapshotChunkV2 chunk)
        {
            if (clientSnapshot == null || clientOffer == null)
                throw new InvalidOperationException("Snapshot chunk arrived without an active offer.");
            SnapshotProgressV2 progress = clientSnapshot.Accept(chunk);
            SendClientFrame(MessageKindV2.SnapshotProgress, SnapshotTransferMessagesV2.EncodeProgress(progress));
            if (!clientSnapshot.Complete) return;

            byte[] world = clientSnapshot.ReadAllVerifiedBytes();
            lock (gate) { preserveAcrossLevelLoad = true; detail = "loading-host-snapshot"; }
            lifecycle.TryTransition(load, CitiesRuntimeRole.ClientRecovering);
            RuntimeServices.WorldLoader.Start(world, CompleteSnapshotLoad, delegate(Exception error)
            {
                lock (gate) preserveAcrossLevelLoad = false;
                FenceSession("snapshot-load:" + error.GetType().Name);
            });
            clientSnapshot.Dispose();
            clientSnapshot = null;
        }

        private void CompleteSnapshotLoad(LoadIdentity identity)
        {
            try
            {
                if (!identity.IsValid || identity.WorldId != clientStamp.WorldId || identity.Epoch != clientStamp.Epoch)
                    throw new InvalidOperationException("Loaded snapshot has the wrong Forge world identity.");
                ForgeSaveMetadata metadata = RuntimeServices.Metadata.Current;
                if (metadata == null || metadata.Revision != clientOffer.BaselineRevision || !metadata.RootKnown ||
                    metadata.StateRoot == null || !metadata.StateRoot.Equals(clientOffer.BaselineRoot))
                    throw new InvalidOperationException("Loaded snapshot metadata does not match the offered baseline.");
                load = identity;
                if (!lifecycle.TryTransition(load, CitiesRuntimeRole.ClientRecovering))
                    throw new InvalidOperationException("Could not enter ClientRecovering after snapshot load.");
                IReplicaDomainV2[] domains = CreateClientDomains(load);
                replica = new ReplicaCoordinatorV2(clientStamp, domains, clientOffer.BaselineRevision);
                if (!replica.CurrentRoot.Equals(clientOffer.BaselineRoot))
                    throw new InvalidOperationException("Loaded game projection root does not match the offered baseline.");
                lock (gate) { preserveAcrossLevelLoad = false; mode = MultiplayerSessionMode.ClientCatchingUp; detail = "baseline-installed"; }
                SendClientFrame(MessageKindV2.WorldInstalled,
                    JoinMessagesV2.EncodeWorldInstalled(new WorldInstalledV2(clientOffer.JoinId, clientOffer.JoinGeneration,
                        clientOffer.BaselineRevision, clientOffer.BaselineRoot)));
            }
            catch (Exception error)
            {
                lock (gate) preserveAcrossLevelLoad = false;
                FenceSession("snapshot-baseline:" + error.GetType().Name);
            }
        }

        private void HandleClientBatch(AuthorityBatch batch)
        {
            if (replica == null) throw new InvalidOperationException("Authority batch arrived before replica baseline.");
            ReplicaDecisionV2 decision = replica.Receive(batch);
            if (decision == ReplicaDecisionV2.Gap)
            {
                SendClientFrame(MessageKindV2.GapRequest, ControlMessagesV2.EncodeGapRequest(new GapRequestV2(replica.Revision)));
                return;
            }
            if (decision != ReplicaDecisionV2.Applied && decision != ReplicaDecisionV2.Duplicate)
                throw new InvalidOperationException("Replica rejected authority batch: " + decision);
            AppliedAck ack = replica.CreateAppliedAck(clientBinding, 0);
            if (ack != null) SendClientFrame(MessageKindV2.AppliedAck, SessionMessagesV2.EncodeAppliedAck(ack));
        }

        private void HandleReplayBarrier(ReplayBarrierV2 barrier)
        {
            if (replica == null || clientOffer == null || barrier.JoinId != clientOffer.JoinId || barrier.JoinGeneration != clientOffer.JoinGeneration ||
                replica.Revision < barrier.Revision || !replica.CurrentRoot.Equals(barrier.Root))
            {
                SendClientFrame(MessageKindV2.GapRequest,
                    ControlMessagesV2.EncodeGapRequest(new GapRequestV2(replica == null ? 0 : replica.Revision)));
                return;
            }
            SendClientFrame(MessageKindV2.BarrierAck,
                JoinMessagesV2.EncodeBarrierAck(new BarrierAckV2(barrier.JoinId, barrier.JoinGeneration,
                    barrier.BarrierId, barrier.Revision, barrier.Root)));
        }

        private void HandleActivationGrant(ActivationGrantV2 grant)
        {
            if (replica == null || clientOffer == null || grant.JoinId != clientOffer.JoinId || grant.JoinGeneration != clientOffer.JoinGeneration ||
                replica.Revision != grant.Revision || !replica.CurrentRoot.Equals(grant.Root) || !replica.MarkLive(grant.Revision, grant.Root) ||
                !lifecycle.TryTransition(load, CitiesRuntimeRole.ClientReplicaLive))
                throw new InvalidOperationException("Activation grant cannot be applied to the current replica state.");
            clientPermissionVersion = grant.PermissionVersion;
            lock (gate) { mode = MultiplayerSessionMode.ClientLive; detail = "live"; }
            SendClientFrame(MessageKindV2.Activated,
                JoinMessagesV2.EncodeActivated(new ActivatedV2(grant.JoinId, grant.JoinGeneration, grant.GrantId, grant.Revision)));
        }
    }
}
