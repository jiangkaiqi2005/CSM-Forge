using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class EventControlPatchRouter
    {
        private static bool TryResolve(ushort native, out EntityIdentityV2 identity)
        {
            identity = default(EntityIdentityV2);
            if (native == 0) return false;
            EntityIdMapV2 ids = ExtensionIdentityServices.Maps.GetOrAttach(EventStateAdapter.Adapter);
            if (ids.TryGetIdentity(native, out identity)) return true;
            if (RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.HostLive || EventManager.instance == null ||
                native >= EventManager.instance.m_events.m_buffer.Length) return false;
            EventData data = EventManager.instance.m_events.m_buffer[native];
            if (data.m_flags == EventData.Flags.None || data.Info == null) return false;
            identity = ids.Allocate(native);
            return true;
        }

        internal static bool RouteInt(ushort native, EventControlKind kind, int value)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolve(native, out identity))
            {
                RuntimeServices.Lifecycle.Fence("Event control referenced an unknown stable entity");
                return false;
            }
            byte[] payload = EventStateAdapter.EncodeIntIntent(kind, identity, value);
            if (!ForgeExtensionApi.TrySubmitIntent(EventStateAdapter.Adapter, payload))
                RuntimeServices.Lifecycle.Fence("Event control could not be routed through Host authority");
            return false;
        }

        internal static bool RouteColor(ushort native, Color32 value)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading)
                return true;
            if (role != CitiesRuntimeRole.HostLive && role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolve(native, out identity))
            {
                RuntimeServices.Lifecycle.Fence("Event color referenced an unknown stable entity");
                return false;
            }
            byte[] payload = EventStateAdapter.EncodeColorIntent(identity, value);
            if (!ForgeExtensionApi.TrySubmitIntent(EventStateAdapter.Adapter, payload))
                RuntimeServices.Lifecycle.Fence("Event color could not be routed through Host authority");
            return false;
        }

        internal static bool RouteClientActivate(ushort native)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading ||
                role == CitiesRuntimeRole.HostLive) return true;
            if (role != CitiesRuntimeRole.ClientReplicaLive) return false;
            EntityIdentityV2 identity;
            if (!TryResolve(native, out identity))
            {
                RuntimeServices.Lifecycle.Fence("Event activation referenced an unknown stable entity");
                return false;
            }
            if (!ForgeExtensionApi.TrySubmitIntent(EventStateAdapter.Adapter, EventStateAdapter.EncodeActivateIntent(identity)))
                RuntimeServices.Lifecycle.Fence("Event activation could not be routed through Host authority");
            return false;
        }
    }

    [HarmonyPatch(typeof(EventAI), "SetSecurityBudget")]
    internal static class EventSecurityBudgetAuthorityPatch
    {
        public static bool Prefix(ushort eventID, ref EventData data, int newBudget)
        {
            return EventControlPatchRouter.RouteInt(eventID, EventControlKind.SecurityBudget, newBudget);
        }
    }

    [HarmonyPatch(typeof(EventAI), "SetTicketPrice")]
    internal static class EventTicketPriceAuthorityPatch
    {
        public static bool Prefix(ushort eventID, ref EventData data, int newPrice)
        {
            if (eventID == 0)
            {
                CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
                return RuntimeScopeGuard.IsApplying || role == CitiesRuntimeRole.SinglePlayer ||
                    role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading;
            }
            return EventControlPatchRouter.RouteInt(eventID, EventControlKind.TicketPrice, newPrice);
        }
    }

    [HarmonyPatch(typeof(EventAI), "SetColor")]
    internal static class EventColorAuthorityPatch
    {
        public static bool Prefix(ushort eventID, ref EventData data, Color32 newColor)
        {
            return EventControlPatchRouter.RouteColor(eventID, newColor);
        }
    }

    [HarmonyPatch(typeof(EventAI), "Activate")]
    internal static class EventActivateAuthorityPatch
    {
        public static bool Prefix(ushort eventID, ref EventData data)
        {
            return EventControlPatchRouter.RouteClientActivate(eventID);
        }
    }

    [HarmonyPatch(typeof(EventManager), "CreateEvent")]
    internal static class EventCreateSlotBarrierPatch
    {
        public static bool Prefix(ref ushort eventIndex, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            if (role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled || role == CitiesRuntimeRole.Unloading ||
                role == CitiesRuntimeRole.HostLive) return true;
            eventIndex = 0; __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(EventManager), "ReleaseEvent")]
    internal static class EventReleaseSlotBarrierPatch
    {
        public static bool Prefix()
        {
            if (RuntimeScopeGuard.IsApplying) return true;
            CitiesRuntimeRole role = RuntimeServices.Lifecycle.Role;
            return role == CitiesRuntimeRole.SinglePlayer || role == CitiesRuntimeRole.Disabled ||
                role == CitiesRuntimeRole.Unloading || role == CitiesRuntimeRole.HostLive;
        }
    }
}
