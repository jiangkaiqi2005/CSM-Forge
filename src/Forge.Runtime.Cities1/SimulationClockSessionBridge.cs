using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private SimulationClockAuthorityDomain hostClock;
        private SimulationClockReplicaDomain clientClock;

        internal bool TryQueueSimulationClock(bool paused, int speed)
        {
            if (speed < 0 || speed > 3 || snapshotSave != null) return false;
            LoadIdentity identity = load;
            if (!identity.IsValid || !lifecycle.IsCurrent(identity)) return false;
            SimulationClockIntentV2 request = new SimulationClockIntentV2(new SimulationClockStateV2(paused, speed));
            return RuntimeServices.Scheduler.QueueSimulation(identity, delegate { SubmitSimulationClock(request); });
        }

        private void SubmitSimulationClock(SimulationClockIntentV2 value)
        {
            if (snapshotSave != null || value == null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostClock == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("clock-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    SimulationClockAuthorityDomain.Id, hostClock.StateRoot, SimulationClockDomainCodecV2.EncodeIntent(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-clock-intent-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientClock == null || clientOperation == ulong.MaxValue)
                { FenceSession("clock-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    SimulationClockAuthorityDomain.Id, clientClock.StateRoot, SimulationClockDomainCodecV2.EncodeIntent(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "clock-intent-" + clientOperation + ":pending";
            }
        }
    }
}
