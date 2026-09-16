using System;
using System.Collections;
using CsmForge.Core;
using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class EconomyControlPatchHelper
    {
        public static bool MultiplayerControl
        {
            get
            {
                CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
                return role == CitiesRuntimeRole.HostLive || role == CitiesRuntimeRole.ClientReplicaLive;
            }
        }

        public static IEnumerator Empty()
        {
            yield break;
        }

        public static bool Queue(EconomyControlIntentV2 intent)
        {
            if (RuntimeServices.Multiplayer.TryQueueEconomyControl(intent)) return true;
            RuntimeServices.Lifecycle.Fence("Economy control could not be routed through Host authority");
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "TakeNewLoan",
        new Type[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
    internal static class ForgeTakeLoanAuthorityPatch
    {
        public static bool Prefix(int index, int amount, int interest, int length, ref IEnumerator __result)
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyControlPatchHelper.MultiplayerControl) return true;
            __result = EconomyControlPatchHelper.Empty();
            try
            {
                EconomyControlPatchHelper.Queue(new EconomyControlIntentV2(
                    EconomyControlIntentKindV2.TakeLoan, index, amount, interest, length));
            }
            catch
            {
                RuntimeServices.Lifecycle.Fence("Invalid loan request was intercepted");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "PayLoanNow", new Type[] { typeof(int) })]
    internal static class ForgePayLoanAuthorityPatch
    {
        public static bool Prefix(int index, ref IEnumerator __result)
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyControlPatchHelper.MultiplayerControl) return true;
            __result = EconomyControlPatchHelper.Empty();
            EconomyControlPatchHelper.Queue(EconomyControlIntentV2.PayLoan(index));
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "AcceptBailout")]
    internal static class ForgeAcceptBailoutAuthorityPatch
    {
        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyControlPatchHelper.MultiplayerControl) return true;
            EconomyControlPatchHelper.Queue(EconomyControlIntentV2.AcceptBailout());
            return false;
        }
    }

    [HarmonyPatch(typeof(EconomyManager), "RejectBailout")]
    internal static class ForgeRejectBailoutAuthorityPatch
    {
        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying || !EconomyControlPatchHelper.MultiplayerControl) return true;
            EconomyControlPatchHelper.Queue(EconomyControlIntentV2.RejectBailout());
            return false;
        }
    }
}
