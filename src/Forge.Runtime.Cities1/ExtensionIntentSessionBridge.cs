using System;
using CsmForge.Core;
using CsmForge.Protocol;

namespace CsmForge.Runtime.Cities1
{
    public sealed partial class CitiesMultiplayerSessionV3
    {
        internal void SubmitExtensionIntentOnSimulation(string adapterId, byte[] adapterPayload)
        {
            if (!load.IsValid || !lifecycle.IsCurrent(load) || hostExtensions == null && clientExtensions == null) return;
            ExtensionPlayerIntentV2 request;
            try { request = new ExtensionPlayerIntentV2(adapterId, adapterPayload); }
            catch { return; }
            byte[] payload = ExtensionIntentCodecV2.Encode(request);

            if (mode == MultiplayerSessionMode.Hosting)
            {
                if (authority == null || hostExtensions == null || hostLocalOperation == ulong.MaxValue) return;
                hostLocalOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(authority.Stamp, hostLocalMember, hostLocalOperation, 1,
                    ExtensionStateAuthorityDomain.Id, hostExtensions.StateRoot, payload);
                AuthoritySubmitResultV2 result = authority.Submit(hostLocalBinding, intent);
                if (result.Decision == AuthoritySubmitDecisionV2.Committed && result.Batch != null)
                {
                    BroadcastBatch(result.Batch);
                    lock (gate) detail = "extension-intent-" + adapterId + ":committed";
                }
                else
                {
                    lock (gate) detail = "extension-intent-" + adapterId + ":" + result.Decision;
                    if (result.Decision == AuthoritySubmitDecisionV2.Faulted) FenceSession("host-extension-intent-faulted:" + adapterId);
                }
                return;
            }

            if (mode == MultiplayerSessionMode.ClientLive)
            {
                if (replica == null || clientExtensions == null || clientOperation == ulong.MaxValue) return;
                clientOperation++;
                PlayerIntentV2 intent = new PlayerIntentV2(replica.Stamp, clientMember, clientOperation, clientPermissionVersion,
                    ExtensionStateAuthorityDomain.Id, clientExtensions.StateRoot, payload);
                SendClientFrame(MessageKindV2.Intent, SessionMessagesV2.EncodeIntent(intent));
                lock (gate) detail = "extension-intent-" + adapterId + ":pending";
            }
        }
    }
}
