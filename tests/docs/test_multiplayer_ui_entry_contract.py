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

    def test_steam_join_is_discovery_only_and_keeps_forge_identity(self):
        steam = (RUNTIME / "ForgeSteamRichPresence.cs").read_text(encoding="utf-8")
        ui = (RUNTIME / "ForgeMultiplayerUi.cs").read_text(encoding="utf-8")
        self.assertIn('SetPresence("connect", connect)', steam)
        self.assertIn("JoinRequestedCallback = 337", steam)
        self.assertIn("Forge MemberIdentity remains the network identity", steam)
        self.assertIn("AcceptSteamInvite", ui)


if __name__ == "__main__":
    unittest.main()
