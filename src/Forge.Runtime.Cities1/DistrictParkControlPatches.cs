using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class DistrictParkControlPatchRouter
    {
        private static bool TryResolve(byte native, out EntityIdentityV2 identity)
        {
            identity = default(EntityIdentityV2);
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(DistrictParkControlsAdapter.IdentityNamespace);
            if (ids.TryGetIdentity(native, out identity)) return true;
            if (RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive || native == 0 || DistrictManager.instance == null)
                return false;
            DistrictPark park = DistrictManager.instance.m_parks.m_buffer[native];
            if ((park.m_flags & DistrictPark.Flags.Created) == DistrictPark.Flags.None) return false;
            identity = ids.Allocate(native);
            return true;
        }

        internal static bool RouteInt(byte native, DistrictParkControlKind kind, int value)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolve(native, out identity))
            {
                RuntimeServices.Lifecycle.Fence("DistrictPark control referenced an unknown stable entity");
                return false;
            }
            byte[] payload;
            try { payload = DistrictParkControlsAdapter.EncodeIntIntent(kind, identity, value); }
            catch
            {
                RuntimeServices.Lifecycle.Fence("DistrictPark control produced an invalid authority intent");
                return false;
            }
            if (!ForgeExtensionApi.TrySubmitIntent(DistrictParkControlsAdapter.Adapter, payload))
                RuntimeServices.Lifecycle.Fence("DistrictPark control could not be routed through Host authority");
            return false;
        }

        internal static bool RouteColor(byte native, Color value)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolve(native, out identity))
            {
                RuntimeServices.Lifecycle.Fence("Varsity color referenced an unknown stable DistrictPark entity");
                return false;
            }
            byte[] payload = DistrictParkControlsAdapter.EncodeColorIntent(identity, (Color32)value);
            if (!ForgeExtensionApi.TrySubmitIntent(DistrictParkControlsAdapter.Adapter, payload))
                RuntimeServices.Lifecycle.Fence("Varsity color could not be routed through Host authority");
            return false;
        }
    }

    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnAcademicStaffValueChanged")]
    internal static class CampusAcademicStaffAuthorityPatch
    {
        public static bool Prefix(InstanceID ___m_InstanceID, float value)
        {
            return DistrictParkControlPatchRouter.RouteInt(___m_InstanceID.Park,
                DistrictParkControlKind.AcademicStaff, (int)value);
        }
    }

    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnCheerleadingBudgetChanged")]
    internal static class CampusCheerleadingAuthorityPatch
    {
        public static bool Prefix(InstanceID ___m_InstanceID, float value)
        {
            return DistrictParkControlPatchRouter.RouteInt(___m_InstanceID.Park,
                DistrictParkControlKind.CheerleadingBudget, (int)value);
        }
    }

    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnTicketPriceChanged")]
    internal static class CampusTicketPriceAuthorityPatch
    {
        public static bool Prefix(InstanceID ___m_InstanceID, float value)
        {
            int price = (int)(value * 100f);
            return DistrictParkControlPatchRouter.RouteInt(___m_InstanceID.Park,
                DistrictParkControlKind.TicketPrice, price);
        }
    }

    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnVarsityIdentityChanged")]
    internal static class CampusVarsityIdentityAuthorityPatch
    {
        public static bool Prefix(InstanceID ___m_InstanceID, int value)
        {
            return DistrictParkControlPatchRouter.RouteInt(___m_InstanceID.Park,
                DistrictParkControlKind.VarsityIdentity, value);
        }
    }

    [HarmonyPatch(typeof(CampusWorldInfoPanel), "OnVarsityColorChanged")]
    internal static class CampusVarsityColorAuthorityPatch
    {
        public static bool Prefix(InstanceID ___m_InstanceID, Color value)
        {
            return DistrictParkControlPatchRouter.RouteColor(___m_InstanceID.Park, value);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "SetParkPolicy")]
    internal static class DistrictParkSetPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy, byte park)
        {
            return DistrictParkControlPatchRouter.RouteInt(park, DistrictParkControlKind.SetParkPolicy, (int)policy);
        }
    }

    [HarmonyPatch(typeof(DistrictManager), "UnsetParkPolicy")]
    internal static class DistrictParkUnsetPolicyAuthorityPatch
    {
        public static bool Prefix(DistrictPolicies.Policies policy, byte park)
        {
            return DistrictParkControlPatchRouter.RouteInt(park, DistrictParkControlKind.UnsetParkPolicy, (int)policy);
        }
    }
}
