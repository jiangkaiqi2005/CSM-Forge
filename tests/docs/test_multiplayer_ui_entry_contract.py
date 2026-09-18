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
        self.assertIn("复制邀请信息并打开 Steam 好友", source)
        self.assertIn("开发版 LAN 邀请", source)
        self.assertIn("GameOverlayDialog.Friends", source)


if __name__ == "__main__":
    unittest.main()
