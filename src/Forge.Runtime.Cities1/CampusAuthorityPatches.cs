using HarmonyLib;

namespace CsmForge.Runtime.Cities1
{
    internal static class CampusHostOnlyAuthority
    {
        internal static bool Allow()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled ||
                role == CitiesRuntimeRole.Unloading || role == CitiesRuntimeRole.HostLive;
        }
    }

    /// <summary>
    /// The vanilla callback generates coach-hire timestamps. Until the callback semantics are a
    /// first-class intent, it executes only on Host; builtin.districtpark-campus projects the
    /// resulting count/timestamps to every replica.
    /// </summary>
    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnCoachesCountChanged")]
    internal static class CampusCoachCountHostAuthorityPatch
    {
        public static bool Prefix()
        {
            return CampusHostOnlyAuthority.Allow();
        }
    }

    /// <summary>
    /// Research-grant purchase includes local UI/economy side effects. Host executes the vanilla
    /// purchase and the final grant type is replicated; Client never performs the persistent write.
    /// </summary>
    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnBuyResearchGrant")]
    internal static class CampusResearchGrantHostAuthorityPatch
    {
        public static bool Prefix()
        {
            return CampusHostOnlyAuthority.Allow();
        }
    }

    /// <summary>
    /// Academic-year outcome is intentionally Host-only. Client-side random/result calculation is
    /// suppressed; park level and gameplay-affecting Campus state are projected by Forge adapters.
    /// </summary>
    [HarmonyPatch(typeof(DistrictPark), "OnAcademicYearEnded")]
    internal static class CampusAcademicYearHostAuthorityPatch
    {
        public static bool Prefix()
        {
            return CampusHostOnlyAuthority.Allow();
        }
    }
}
