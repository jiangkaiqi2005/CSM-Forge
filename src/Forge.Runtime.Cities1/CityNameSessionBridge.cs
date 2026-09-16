using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        private CityNameAuthorityDomain hostCityName;
        private CityNameReplicaDomain clientCityName;

        internal bool TryQueueCityName(string name)
        {
            CityNameStateV2 value;
            try { value = new CityNameStateV2(name ?? string.Empty); } catch { return false; }
            if (snapshotSave != null) return false;
            LoadIdentity current = load;
            if (!current.IsValid || !lifecycle.IsCurrent(current)) return false;
            return RuntimeServices.Scheduler.QueueSimulation(current, delegate { SubmitCityName(value); });
        }

        private void SubmitCityName(CityNameStateV2 value)
        {
            if (value == null || snapshotSave != null) return;
            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostCityName == null || hostLocalOperation == ulong.MaxValue)
                { FenceSession("city-name-host-authority-unavailable"); return; }
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    CityNameAuthorityDomain.Id, hostCityName.StateRoot, CityNameCodecV2.Encode(value));
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision != AuthoritySubmitDecisionV2.Committed || result.Batch == null)
                { FenceSession("host-city-name-rejected:" + result.Decision); return; }
                BroadcastBatch(result.Batch);
                return;
            }
            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientCityName == null || clientOperation == ulong.MaxValue)
                { FenceSession("city-name-client-replica-unavailable"); return; }
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    CityNameAuthorityDomain.Id, clientCityName.StateRoot, CityNameCodecV2.Encode(value));
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "city-name-" + clientOperation + ":pending";
            }
        }
    }
}
