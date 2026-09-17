using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class DisasterAuthorityPatchHelper
    {
        internal static bool AllowHostOnlyPersistentMutation()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled ||
                role == CitiesRuntimeRole.Unloading || role == CitiesRuntimeRole.HostLive;
        }
    }

    [HarmonyPatch(typeof(DisasterManager), "CreateDisaster")]
    internal static class DisasterCreateSlotBarrierPatch
    {
        public static bool Prefix(ref ushort disasterIndex, ref bool __result)
        {
            if (DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation()) return true;
            disasterIndex = 0; __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(DisasterManager), "ReleaseDisaster")]
    internal static class DisasterReleaseSlotBarrierPatch
    {
        public static bool Prefix()
        {
            return DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation();
        }
    }

    [HarmonyPatch(typeof(DisasterManager), "StartRandomDisaster")]
    internal static class DisasterRandomStartAuthorityPatch
    {
        public static bool Prefix()
        {
            return DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation();
        }
    }

    [HarmonyPatch(typeof(DisasterAI), "StartNow")]
    internal static class DisasterStartNowAuthorityPatch
    {
        public static bool Prefix()
        {
            return DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation();
        }
    }

    [HarmonyPatch(typeof(DisasterAI), "ActivateNow")]
    internal static class DisasterActivateNowAuthorityPatch
    {
        public static bool Prefix()
        {
            return DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation();
        }
    }

    [HarmonyPatch(typeof(DisasterAI), "DeactivateNow")]
    internal static class DisasterDeactivateNowAuthorityPatch
    {
        public static bool Prefix()
        {
            return DisasterAuthorityPatchHelper.AllowHostOnlyPersistentMutation();
        }
    }
}
