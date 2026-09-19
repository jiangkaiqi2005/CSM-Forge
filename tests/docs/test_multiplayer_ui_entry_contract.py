from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "src" / "Forge.Runtime.Cities1"


class MultiplayerUiEntryContractTests(unittest.TestCase):
    def test_main_and_pause_menus_own_multiplayer_entry(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn('HarmonyPatch(typeof(MainMenu), "Awake")', source)
        self.assertIn('HarmonyPatch(typeof(PauseMenu), "Initialize")', source)
        self.assertIn('text = "FORGE 联机"', source)
        self.assertIn('text = "FORGE 多人联机"', source)

    def test_pause_menu_is_closed_before_the_host_panel_takes_input(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        pause_method = source.index("internal static void EnsurePauseMenuEntry()")
        click = source[source.index("button.eventClick += delegate", pause_method):]
        self.assertIn("ClosePauseMenu();", click)
        self.assertLess(click.index("ClosePauseMenu();"), click.index("ShowPanel<ForgeHostGamePanel>()"))

    def test_host_creation_feedback_is_not_overwritten_while_offline(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        host = source[source.index("internal sealed class ForgeHostGamePanel"):source.index("internal sealed class ForgeSessionPanel")]
        self.assertIn("private string feedback", host)
        self.assertIn("string.IsNullOrEmpty(feedback)", host)
        self.assertIn("Forge patches 尚未就绪", host)
        self.assertIn("create room clicked", host)
        self.assertIn("create room preflight completed", host)
        self.assertIn("create room host request returned", host)

    def test_live_labels_have_stable_geometry_and_skip_identical_text_assignments(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        label_factory = source[source.index("protected UILabel Label"):source.index("protected UITextField Field")]
        self.assertIn("label.autoSize = false", label_factory)
        self.assertIn("label.autoHeight = false", label_factory)
        self.assertIn("label.width = 340", label_factory)
        self.assertIn("label.height = 32", label_factory)
        self.assertIn("if (label.text != value)", label_factory)
        host = source[source.index("internal sealed class ForgeHostGamePanel"):source.index("internal sealed class ForgeSessionPanel")]
        self.assertIn("SetText(players", host)
        self.assertNotIn("players.text = value.Mode", host)

    def test_host_preflight_rechecks_when_harmony_readiness_changes(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        host = source[source.index("internal sealed class ForgeHostGamePanel"):source.index("internal sealed class ForgeSessionPanel")]
        self.assertIn("private uint observedPatchStatusRevision", host)
        self.assertIn("uint patchStatusRevision = RuntimeServices.Patches.StatusRevision", host)
        self.assertIn("if (patchStatusRevision != observedPatchStatusRevision) RefreshPreflight();", host)
        refresh = host[host.index("private void RefreshPreflight()") :]
        self.assertIn("observedPatchStatusRevision = RuntimeServices.Patches.StatusRevision", refresh)

    def test_patch_failure_names_the_exact_patch_in_game_log_and_preflight(self):
        coordinator = (RUNTIME / "PatchCoordinator.cs").read_text(encoding="utf-8")
        preflight = (RUNTIME / "ForgeRoomPreflight.cs").read_text(encoding="utf-8")
        self.assertIn("currentPatch = types[i].FullName", coordinator)
        self.assertIn("Harmony patch installation failed at", coordinator)
        self.assertIn("public string FailureDetail", coordinator)
        self.assertIn("Forge 补丁安装失败：", preflight)

    def test_district_brush_prefix_uses_the_real_cs1_parameter_name(self):
        source = (RUNTIME / "DistrictPatches.cs").read_text(encoding="utf-8")
        patch = source[source.index("internal static class DistrictToolApplyBrushAuthorityPatch"):
                       source.index("internal static class DistrictCreateSlotBarrierPatch")]
        self.assertIn("bool notOverride, out DistrictBrushAuthorityState __state", patch)
        self.assertNotIn("bool force, out DistrictBrushAuthorityState __state", patch)
        park = (RUNTIME / "ParkGridPatches.cs").read_text(encoding="utf-8")
        self.assertIn("bool notOverride, out IDisposable __state", park)
        self.assertNotIn("bool force, out IDisposable __state", park)

    def test_client_segment_bulldoze_matches_cs1_dual_segment_coroutine(self):
        source = (RUNTIME / "NetBulldozePatches.cs").read_text(encoding="utf-8")
        patch = source[source.index("internal static class ClientBulldozeSegmentIntentPatch"):
                       source.index("internal static class ClientBulldozeNodeIntentPatch")]
        self.assertIn("parameters.Length == 2", patch)
        self.assertIn("parameters[1].ParameterType == typeof(ushort)", patch)
        self.assertIn("Prefix(ushort segment, ushort segment2, ref IEnumerator __result)", patch)
        self.assertIn("TryResolveClientNetSegment(segment2, out entity2)", patch)
        self.assertIn("NetIntentV2.DeleteSegment(entity2, false)", patch)

    def test_harmony_prefix_names_match_real_cs1_metadata(self):
        transport = (RUNTIME / "TransportLinePatches.cs").read_text(encoding="utf-8")
        net = (RUNTIME / "NetPatches.cs").read_text(encoding="utf-8")
        add_stop = transport[transport.index("internal static class ForgeTransportAddStopPatch"):
                             transport.index("internal static class ForgeTransportRemoveStopPatch")]
        create_line = transport[transport.index("internal static class ForgeTransportCreateLinePatch"):]
        create_node = net[net.index("internal static class NetToolCreateNodeAuthorityPatch"):
                          net.index("internal static class ClientNetManagerCreateNodeBarrierPatch")]
        self.assertIn("Vector3 position, bool fixedPlatform, ref bool __result", add_stop)
        self.assertNotIn("Vector3 newPos, bool fixedPlatform, ref bool __result", add_stop)
        self.assertIn("Prefix(ref ushort lineID, ref bool __result)", create_line)
        self.assertIn("Postfix(ref ushort lineID, bool __result)", create_line)
        self.assertIn("ref ushort firstNode, ref ushort lastNode, ref ushort segment, ref int cost", create_node)
        self.assertNotIn("ref ushort segmentID, ref int cost", create_node)

    def test_forge_pages_have_one_navigation_owner_and_do_not_stack_click_targets(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("private static readonly Type[] ManagedPanelTypes", source)
        self.assertIn("private static UIComponent DisplayExclusive(Type panelType)", source)
        self.assertIn("HideManagedPanels(view, panelType);", source)
        self.assertIn("internal static void OpenChildPanel<T>()", source)
        self.assertIn("internal static void CloseOrBack(UIComponent panel)", source)
        session = source[source.index("internal sealed class ForgeSessionPanel"):source.index("internal sealed class ForgePlayersPanel")]
        self.assertIn("OpenChildPanel<ForgePlayersPanel>()", session)
        self.assertIn("OpenChildPanel<ForgeChatPanel>()", session)

    def test_forge_pages_block_clicks_from_reaching_the_underlying_game_menu(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn('ModalBackdropName = "CSMForgeModalBackdrop"', source)
        self.assertIn("backdrop.isInteractive = true", source)
        self.assertIn("parameter.Use();", source)
        self.assertLess(source.index("backdrop.BringToFront();"), source.index("panel.BringToFront();"))
        self.assertIn("HideModalBackdrop();", source)

    def test_main_menu_explains_both_join_and_host_paths(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        main = source[source.index("internal sealed class ForgeMainMenuJoinPanel"):source.index("internal sealed class ForgeHostGamePanel")]
        self.assertIn("加入好友", main)
        self.assertIn("想当房主", main)
        self.assertIn("先载入或新建一个城市", main)
        self.assertIn("FORGE 多人联机", main)

    def test_compatibility_rejection_names_actionable_content_mismatches(self):
        formatter = (RUNTIME / "ForgeCompatibilityFailureText.cs").read_text(encoding="utf-8")
        ui = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        for marker in [
            "game-build-mismatch", "schema-mismatch", "missing:", "fingerprint-mismatch:",
            "unsupported-extra:", "client-forbidden:", "host-unsupported:", "Workshop",
        ]:
            self.assertIn(marker, formatter)
        self.assertIn("ForgeCompatibilityFailureText.Describe", ui)

    def test_primary_ui_translates_runtime_modes_into_player_facing_stages(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        for marker in [
            "正在创建房间", "房间运行中", "正在连接并核对游戏内容",
            "正在同步房主城市", "已加入房间", "联机已停止",
        ]:
            self.assertIn(marker, source)
        self.assertNotIn('return value.Mode + " | peers="', source)

    def test_session_actions_keep_feedback_separate_from_live_status(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        session = source[source.index("internal sealed class ForgeSessionPanel"):source.index("internal sealed class ForgePlayersPanel")]
        self.assertIn("private UILabel notice", session)
        self.assertIn("internal void SetNotice", session)
        self.assertIn("自动点击加入已为稳定性停用", session)

    def test_faulted_world_is_not_silently_unfenced_or_presented_as_retryable(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        pause = source[source.index("internal static void EnsurePauseMenuEntry()"):source.index("internal static void Shutdown()")]
        self.assertNotIn("RuntimeServices.Multiplayer.StopImmediately();", pause)
        self.assertIn("ShowPanel<ForgeFaultPanel>()", pause)
        fault = source[source.index("internal sealed class ForgeFaultPanel"):]
        self.assertIn("CitiesRuntimeRole.WorldFenced", fault)
        self.assertIn("必须返回主菜单并重新加载城市", fault)
        self.assertIn('RuntimeDiagnostics.DumpToGameLog("multiplayer-fault-panel")', fault)

    def test_session_stop_requires_an_explicit_confirmation(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        session = source[source.index("internal sealed class ForgeSessionPanel"):source.index("internal sealed class ForgePlayersPanel")]
        self.assertIn("OpenChildPanel<ForgeLeaveConfirmPanel>()", session)
        confirm = source[source.index("internal sealed class ForgeLeaveConfirmPanel"):source.index("internal sealed class ForgeFaultPanel")]
        self.assertIn("确认停止房间", confirm)
        self.assertIn("确认断开", confirm)
        self.assertIn("RuntimeServices.Multiplayer.RequestStop()", confirm)

    def test_start_actions_are_single_submit_and_faults_have_recovery_routes(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("private UIButton createButton", source)
        self.assertIn("createButton.isEnabled = value.Mode == MultiplayerSessionMode.Offline", source)
        self.assertIn("private UIButton joinButton", source)
        self.assertIn("joinButton.isEnabled = false", source)
        self.assertIn("joinButton.isEnabled = mode == MultiplayerSessionMode.Offline", source)
        self.assertIn("internal static void OpenMainMenuEntry()", source)
        self.assertIn("OpenMainJoinWithPendingInvite", source)
        self.assertIn("ResetAndReturn", source)

    def test_host_preflight_reuses_the_real_compatibility_manifest_and_explains_blockers(self):
        source = (RUNTIME / "ForgeRoomPreflight.cs").read_text(encoding="utf-8")
        ui = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("CitiesCompatibilityCollector.Collect()", source)
        self.assertIn("CompatibilityCapabilityReport.From", source)
        self.assertIn("report.BlockedIds", source)
        self.assertIn("当前城市已被安全隔离", source)
        self.assertIn("创建后仍会逐个核对加入者清单", source)
        self.assertIn("ForgeRoomPreflight.EvaluateHost()", ui)
        self.assertIn("重新检查 DLC / Mod / 资产", ui)

    def test_room_lifecycle_never_enters_the_unverified_manual_steam_abi(self):
        source = (RUNTIME / "ForgeSteamRichPresence.cs").read_text(encoding="utf-8")
        host = (RUNTIME / "CitiesMultiplayerSessionV3.Host.cs").read_text(encoding="utf-8")
        ui = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        for marker in ["DllImport", "SteamAPI_", "RegisterCallback", "SetRichPresence", "Marshal."]:
            self.assertNotIn(marker, source)
        self.assertNotIn("ForgeSteamRichPresence", host)
        self.assertNotIn("ForgeSteamRichPresence.Pump();", ui)

    def test_main_menu_join_does_not_require_a_loaded_world(self):
        source = (RUNTIME / "CitiesMultiplayerSessionV3.cs").read_text(encoding="utf-8")
        client = (RUNTIME / "CitiesMultiplayerSessionV3.Client.cs").read_text(encoding="utf-8")
        self.assertIn("RequestJoinFromMainMenu", source)
        self.assertIn("PollMainMenu", source)
        self.assertIn("StartClientBootstrap", client)

    def test_settings_is_diagnostics_not_the_room_lobby(self):
        source = (RUNTIME / "ForgeSettingsPanel.cs").read_text(encoding="utf-8")
        self.assertNotIn("RuntimeServices.Multiplayer.RequestHost", source)
        self.assertNotIn("RuntimeServices.Multiplayer.RequestJoinCurrentWorld", source)
        self.assertIn("主菜单", source)
        self.assertIn("暂停菜单", source)

    def test_visible_invitation_action_is_honest_about_lan_transport(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("邀请 Steam 好友", source)
        self.assertIn("直连邀请码", source)
        self.assertIn("不提供 NAT 穿透", source)
        self.assertIn("GameOverlayDialog.Friends", source)
        self.assertNotIn("开发版 LAN 邀请", source)

    def test_session_ui_has_join_progress_roster_chat_and_role_management(self):
        source = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("ForgeJoinProgressPanel", source)
        self.assertIn("ForgePlayersPanel", source)
        self.assertIn("ForgeChatPanel", source)
        self.assertIn("RequestKick(values[index].Member)", source)
        self.assertIn("多人聊天（快捷键 T）", source)

    def test_player_activity_uses_presentation_lane_and_stable_member_identity(self):
        protocol = (ROOT / "src/Forge.Protocol/FrameCodecV2.cs").read_text(encoding="utf-8")
        messages = (ROOT / "src/Forge.Protocol/SocialMessagesV2.cs").read_text(encoding="utf-8")
        runtime = (RUNTIME / "ForgePlayerPresenceUi.cs").read_text(encoding="utf-8")
        self.assertIn("PlayerPresentation", protocol)
        self.assertIn("SessionLane.Presentation", protocol)
        self.assertIn("MemberIdentity Member", messages)
        self.assertIn("ToolName", messages)
        self.assertIn("TryPublishPresentation", runtime)

    def test_steam_invitation_keeps_forge_identity_and_manual_fallback(self):
        steam = (RUNTIME / "ForgeSteamRichPresence.cs").read_text(encoding="utf-8")
        ui = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn("Forge MemberIdentity remains the network identity", steam)
        self.assertIn("AutomaticJoinAvailable", steam)
        self.assertIn("GUIUtility.systemCopyBuffer = lastInvite", ui)
        self.assertIn("PlatformService.ActivateGameOverlay(GameOverlayDialog.Friends)", ui)
        self.assertIn("自动点击加入已为稳定性停用", ui)
        self.assertIn("不提供 NAT 穿透", ui)


if __name__ == "__main__":
    unittest.main()
