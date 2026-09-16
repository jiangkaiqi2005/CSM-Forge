using System;
using System.Reflection;
using CsmForge.Core;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal static class TransportLinePatchHelper
    {
        public static bool ClientLive
        {
            get { return RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.ClientReplicaLive; }
        }

        public static bool QueueProperties(ushort lineId, Color32 color, ushort budget, ushort ticket, bool day, bool night)
        {
            return RuntimeServices.Multiplayer.TryQueueTransportProperties(lineId,
                color.r, color.g, color.b, color.a, budget, ticket, day, night);
        }

        public static bool Current(ushort lineId, out TransportLine line, out bool day, out bool night)
        {
            line = default(TransportLine); day = night = false;
            if (!TransportLineGameAccess.Live(lineId)) return false;
            line = TransportManager.instance.m_lines.m_buffer[lineId];
            day = (line.m_flags & TransportLine.Flags.DisabledDay) == TransportLine.Flags.None;
            night = (line.m_flags & TransportLine.Flags.DisabledNight) == TransportLine.Flags.None;
            return true;
        }

        public static ushort GetLineId(object panel)
        {
            if (panel == null) return 0;
            MethodInfo method = panel.GetType().GetMethod("GetLineID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return 0;
            object value = method.Invoke(panel, null); return value is ushort ? (ushort)value : (ushort)0;
        }
    }

    [HarmonyPatch(typeof(TransportManager), "SetLineColor")]
    internal static class ForgeTransportSetColorPatch
    {
        public static bool Prefix(ushort lineID, Color color)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            TransportLine line; bool day, night;
            if (!TransportLinePatchHelper.Current(lineID, out line, out day, out night)) return false;
            Color32 c = color;
            TransportLinePatchHelper.QueueProperties(lineID, c, line.m_budget, line.m_ticketPrice, day, night);
            return false;
        }
    }

    [HarmonyPatch(typeof(PublicTransportWorldInfoPanel), "OnVehicleCountModifierChanged")]
    internal static class ForgeTransportBudgetPatch
    {
        public static bool Prefix(float value, PublicTransportWorldInfoPanel __instance)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            ushort lineId = TransportLinePatchHelper.GetLineId(__instance); TransportLine line; bool day, night;
            if (!TransportLinePatchHelper.Current(lineId, out line, out day, out night)) return false;
            int raw = Mathf.RoundToInt(value); if (raw < 0) raw = 0; if (raw > ushort.MaxValue) raw = ushort.MaxValue;
            TransportLinePatchHelper.QueueProperties(lineId, line.m_color, (ushort)raw, line.m_ticketPrice, day, night);
            return false;
        }
    }

    [HarmonyPatch(typeof(PublicTransportWorldInfoPanel), "OnTicketPriceChanged")]
    internal static class ForgeTransportTicketPatch
    {
        public static bool Prefix(float value, PublicTransportWorldInfoPanel __instance)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            ushort lineId = TransportLinePatchHelper.GetLineId(__instance); TransportLine line; bool day, night;
            if (!TransportLinePatchHelper.Current(lineId, out line, out day, out night)) return false;
            int raw = Mathf.RoundToInt(value); if (raw < 0) raw = 0; if (raw > ushort.MaxValue) raw = ushort.MaxValue;
            TransportLinePatchHelper.QueueProperties(lineId, line.m_color, line.m_budget, (ushort)raw, day, night);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ForgeTransportActiveParentPatch
    {
        internal static ushort LineId;
        public static void Prefix(ushort id) { LineId = id; }
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types = new[] { typeof(PublicTransportLineInfo), typeof(PublicTransportWorldInfoPanel) };
            string[] names = new[] { "SetDayOnly", "SetNightOnly", "SetAllDay" };
            for (int i = 0; i < types.Length; i++)
                for (int j = 0; j < names.Length; j++)
                {
                    MethodInfo method = types[i].GetMethod(names[j], BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(ushort) }, null);
                    if (method != null) yield return method;
                }
        }
    }

    [HarmonyPatch(typeof(TransportLine), "SetActive")]
    internal static class ForgeTransportSetActivePatch
    {
        public static bool Prefix(bool day, bool night)
        {
            ushort lineId = ForgeTransportActiveParentPatch.LineId;
            ForgeTransportActiveParentPatch.LineId = 0;
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive || lineId == 0) return true;
            TransportLine line; bool oldDay, oldNight;
            if (!TransportLinePatchHelper.Current(lineId, out line, out oldDay, out oldNight)) return false;
            TransportLinePatchHelper.QueueProperties(lineId, line.m_color, line.m_budget, line.m_ticketPrice, day, night);
            return false;
        }
    }

    [HarmonyPatch(typeof(TransportManager), "ReleaseLine")]
    internal static class ForgeTransportReleasePatch
    {
        public static bool Prefix(ushort lineID)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            RuntimeServices.Multiplayer.TryQueueTransportRelease(lineID);
            return false;
        }
    }

    [HarmonyPatch(typeof(TransportLine), "AddStop", new Type[] { typeof(ushort), typeof(int), typeof(Vector3), typeof(bool) })]
    internal static class ForgeTransportAddStopPatch
    {
        public static bool Prefix(ushort lineID, int index, Vector3 newPos, bool fixedPlatform, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            __result = false;
            RuntimeServices.Multiplayer.TryQueueTransportRoute(lineID, TransportLineIntentKindV2.AddStop,
                index, newPos.x, newPos.y, newPos.z, fixedPlatform);
            return false;
        }
    }

    [HarmonyPatch(typeof(TransportLine), "RemoveStop", new Type[] { typeof(ushort), typeof(int) })]
    internal static class ForgeTransportRemoveStopPatch
    {
        public static bool Prefix(ushort lineID, int index, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            __result = false;
            RuntimeServices.Multiplayer.TryQueueTransportRoute(lineID, TransportLineIntentKindV2.RemoveStop,
                index, 0f, 0f, 0f, false);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ForgeTransportMoveStopPatch
    {
        public static MethodBase TargetMethod()
        {
            return typeof(TransportLine).GetMethod("MoveStop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(ushort), typeof(int), typeof(Vector3), typeof(bool), typeof(Vector3).MakeByRefType() }, null);
        }

        public static bool Prefix(ushort lineID, int index, Vector3 newPos, bool fixedPlatform, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            __result = false;
            RuntimeServices.Multiplayer.TryQueueTransportRoute(lineID, TransportLineIntentKindV2.MoveStop,
                index, newPos.x, newPos.y, newPos.z, fixedPlatform);
            return false;
        }
    }

    [HarmonyPatch(typeof(TransportManager), "CreateLine")]
    internal static class ForgeTransportCreateLinePatch
    {
        public static bool Prefix(ref ushort line, ref bool __result)
        {
            if (RuntimeScopeGuard.IsApplying || !TransportLinePatchHelper.ClientLive) return true;
            line = 0; __result = false;
            return false;
        }
    }
}
