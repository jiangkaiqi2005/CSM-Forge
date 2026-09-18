using System;
using System.Net;
using System.Text;
using ColossalFramework.PlatformServices;
using ColossalFramework.UI;
using HarmonyLib;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    [HarmonyPatch(typeof(MainMenu), "Awake")]
    internal static class ForgeMainMenuAwakePatch
    {
        private static void Postfix() { ForgeMultiplayerUi.EnsureMainMenuEntry(); }
    }

    [HarmonyPatch(typeof(PauseMenu), "Initialize")]
    internal static class ForgePauseMenuInitializePatch
    {
        private static void Postfix() { ForgeMultiplayerUi.EnsurePauseMenuEntry(); }
    }

    internal static class ForgeMultiplayerUi
    {
        private const string MainButtonName = "CSMForgeMainMultiplayer";
        private const string PauseButtonName = "CSMForgePauseMultiplayer";
        internal static string RoomKey = "forge-dev";

        internal static void Initialize()
        {
            EnsurePump();
            EnsureMainMenuEntry();
        }

        internal static void EnsureMainMenuEntry()
        {
            if (RuntimeServices.Lifecycle.Current.IsValid) return;
            UIView view = UIView.GetAView();
            UIPanel menu = view == null ? null : view.FindUIComponent("Menu") as UIPanel;
            if (menu == null || view.FindUIComponent(MainButtonName) != null) return;
            UIButton button = (UIButton)menu.AddUIComponent(typeof(UIButton));
            button.name = MainButtonName;
            button.text = "FORGE 联机";
            button.width = 411; button.height = 56; button.textScale = 1.5f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Center;
            button.textColor = new Color32(254, 254, 254, 255);
            button.hoveredTextColor = new Color32(7, 123, 255, 255);
            button.pressedTextColor = new Color32(30, 30, 44, 255);
            button.useDropShadow = true; button.useGradient = true; button.useGUILayout = true;
            button.eventClick += delegate { ShowPanel<ForgeMainMenuJoinPanel>(); };
            EnsurePump();
        }

        internal static void EnsurePauseMenuEntry()
        {
            if (!RuntimeServices.Lifecycle.Current.IsValid) return;
            UIView view = UIView.GetAView();
            UIPanel menu = view == null ? null : view.FindUIComponent("Menu") as UIPanel;
            if (menu == null || view.FindUIComponent(PauseButtonName) != null) return;
            UIButton button = (UIButton)menu.AddUIComponent(typeof(UIButton));
            button.name = PauseButtonName;
            button.text = "FORGE 多人联机";
            button.width = 310; button.height = 57; button.textScale = 1.25f;
            button.normalBgSprite = "ButtonMenu"; button.disabledBgSprite = "ButtonMenuDisabled";
            button.hoveredBgSprite = "ButtonMenuHovered"; button.focusedBgSprite = "ButtonMenuFocused";
            button.pressedBgSprite = "ButtonMenuPressed"; button.playAudioEvents = true;
            button.eventClick += delegate { ShowPanel<ForgeRoomPanel>(); };
            EnsurePump();
        }

        internal static void Shutdown()
        {
            UIView view = UIView.GetAView();
            if (view == null) return;
            DestroyNamed(view, MainButtonName); DestroyNamed(view, PauseButtonName);
            DestroyNamed(view, typeof(ForgeMainMenuJoinPanel).Name);
            DestroyNamed(view, typeof(ForgeRoomPanel).Name);
            ForgeMenuPump pump = view.gameObject.GetComponent<ForgeMenuPump>();
            if (pump != null) UnityEngine.Object.Destroy(pump);
        }

        internal static string BuildInviteCode(string address, int port, string key)
        {
            return "forge-lan-v1|" + address + "|" + port + "|" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(key ?? string.Empty));
        }

        internal static bool TryParseInviteCode(string text, out IPEndPoint endpoint, out string key)
        {
            endpoint = null; key = null;
            string[] parts = (text ?? string.Empty).Trim().Split('|');
            IPAddress address; int port;
            if (parts.Length != 4 || parts[0] != "forge-lan-v1" || !IPAddress.TryParse(parts[1], out address) ||
                !int.TryParse(parts[2], out port) || port < 1 || port > 65535) return false;
            try { key = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])); }
            catch { return false; }
            if (string.IsNullOrEmpty(key)) return false;
            endpoint = new IPEndPoint(address, port); return true;
        }

        internal static string LocalIpv4()
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
                for (int i = 0; i < addresses.Length; i++)
                    if (addresses[i].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addresses[i])) return addresses[i].ToString();
            }
            catch { }
            return "127.0.0.1";
        }

        private static void EnsurePump()
        {
            UIView view = UIView.GetAView();
            if (view != null && view.gameObject.GetComponent<ForgeMenuPump>() == null)
                view.gameObject.AddComponent<ForgeMenuPump>();
        }

        private static T ShowPanel<T>() where T : UIComponent
        {
            UIView view = UIView.GetAView(); if (view == null) return null;
            T panel = view.FindUIComponent<T>(typeof(T).Name);
            if (panel == null) { panel = (T)view.AddUIComponent(typeof(T)); panel.name = typeof(T).Name; }
            panel.isVisible = true; panel.BringToFront(); panel.Focus(); return panel;
        }

        private static void DestroyNamed(UIView view, string name)
        {
            UIComponent component = view.FindUIComponent(name);
            if (component != null) UnityEngine.Object.Destroy(component.gameObject);
        }
    }

    internal sealed class ForgeMenuPump : MonoBehaviour
    {
        private void Update() { RuntimeServices.Multiplayer.PollMainMenu(); }
    }

    internal abstract class ForgePanelBase : UIPanel
    {
        protected UILabel Status;

        protected void Configure(string title, float panelHeight)
        {
            backgroundSprite = "GenericPanel"; color = new Color32(40, 40, 48, 245);
            width = 520; height = panelHeight;
            UIView view = GetUIView();
            relativePosition = new Vector3(view.GetScreenResolution().x / 2f - width / 2f,
                view.GetScreenResolution().y / 2f - height / 2f);
            AddUIComponent(typeof(UIDragHandle)).size = new Vector2(width, 45);
            UILabel label = AddUIComponent<UILabel>(); label.text = title; label.textScale = 1.25f;
            label.relativePosition = new Vector3(20, 14);
        }

        protected UILabel Label(string text, float y)
        {
            UILabel label = AddUIComponent<UILabel>(); label.text = text; label.textScale = 0.9f;
            label.relativePosition = new Vector3(20, y); return label;
        }

        protected UITextField Field(string text, float y, bool numeric)
        {
            UITextField field = AddUIComponent<UITextField>(); field.text = text; field.width = 480; field.height = 32;
            field.relativePosition = new Vector3(20, y); field.normalBgSprite = "TextFieldPanel";
            field.hoveredBgSprite = "TextFieldPanelHovered"; field.focusedBgSprite = "TextFieldPanelFocused";
            field.padding = new RectOffset(8, 8, 7, 7); field.builtinKeyNavigation = true; field.numericalOnly = numeric;
            return field;
        }

        protected UIButton Button(string text, float y, Action action)
        {
            UIButton button = AddUIComponent<UIButton>(); button.text = text; button.width = 480; button.height = 42;
            button.relativePosition = new Vector3(20, y); button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered"; button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += delegate { action(); }; return button;
        }

        protected static string StatusText()
        {
            MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
            return value.Mode + " | peers=" + value.ConnectedPeers + " | revision=" + value.Revision + " | " + value.Detail;
        }
    }

    internal sealed class ForgeMainMenuJoinPanel : ForgePanelBase
    {
        private UITextField invite;
        private UITextField nameField;

        public override void Start()
        {
            Configure("加入 CSM-Forge 房间", 390);
            Label("粘贴房主发来的开发版 LAN 邀请信息", 62);
            invite = Field(string.Empty, 88, false);
            Label("显示名", 132);
            string display = ForgeMod.Settings.DisplayName.value;
            if (PlatformService.active && !string.IsNullOrEmpty(PlatformService.personaName)) display = PlatformService.personaName;
            nameField = Field(display, 158, false);
            Status = Label("尚未连接。加入后会自动下载并加载房主存档。", 205);
            Button("加入房间", 242, Join);
            Button("取消 / 断开", 300, delegate { RuntimeServices.Multiplayer.StopImmediately(); isVisible = false; });
            base.Start();
        }

        public override void Update()
        {
            if (isVisible && RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline)
                Status.text = StatusText();
            base.Update();
        }

        private void Join()
        {
            IPEndPoint endpoint; string key;
            if (!ForgeMultiplayerUi.TryParseInviteCode(invite.text, out endpoint, out key))
            { Status.text = "邀请信息格式无效，请让房主重新复制。"; return; }
            string display = (nameField.text ?? string.Empty).Trim();
            if (display.Length == 0 || display.Length > 32) { Status.text = "显示名必须为 1–32 个字符。"; return; }
            ForgeMod.Settings.DisplayName.value = display;
            ForgeMod.Settings.HostAddress.value = endpoint.Address.ToString(); ForgeMod.Settings.Port.value = endpoint.Port;
            bool ok = RuntimeServices.Patches.Installed &&
                RuntimeServices.Multiplayer.RequestJoinFromMainMenu(endpoint, key, display);
            Status.text = ok ? "正在连接房主；收到快照后会自动进入城市……" :
                "无法开始连接：请确认 CitiesHarmony/Forge patches 已就绪且当前没有会话。";
        }
    }

    internal sealed class ForgeRoomPanel : ForgePanelBase
    {
        private UITextField portField;
        private UITextField keyField;
        private UITextField nameField;
        private UILabel players;
        private string lastInvite;

        public override void Start()
        {
            Configure("CSM-Forge 多人房间", 560);
            Label("显示名", 58); nameField = Field(ForgeMod.Settings.DisplayName.value, 82, false);
            Label("UDP 端口", 124); portField = Field(ForgeMod.Settings.Port.value.ToString(), 148, true);
            Label("临时房间口令", 190); keyField = Field(ForgeMultiplayerUi.RoomKey, 214, false);
            Status = Label(StatusText(), 260);
            players = Label("玩家：等待创建房间", 285);
            Button("创建房间（当前城市作为房主）", 320, CreateRoom);
            Button("复制邀请信息并打开 Steam 好友", 370, InviteFriends);
            Button("停止房间 / 断开", 420, delegate { RuntimeServices.Multiplayer.RequestStop(); Status.text = "正在停止会话……"; });
            Button("关闭", 470, delegate { isVisible = false; });
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                Status.text = StatusText();
                players.text = value.Mode == MultiplayerSessionMode.Hosting ?
                    "玩家：房主 + " + value.ConnectedPeers + " 个已连接客户端" : "玩家：当前未主持房间";
            }
            base.Update();
        }

        private void CreateRoom()
        {
            int port; string display = (nameField.text ?? string.Empty).Trim(); string key = keyField.text ?? string.Empty;
            if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
            { Status.text = "端口必须是 1–65535。"; return; }
            if (display.Length == 0 || display.Length > 32 || key.Length == 0)
            { Status.text = "显示名需为 1–32 个字符，房间口令不能为空。"; return; }
            ForgeMod.Settings.DisplayName.value = display; ForgeMod.Settings.Port.value = port;
            ForgeMultiplayerUi.RoomKey = key;
            bool ok = RuntimeServices.Patches.Installed && RuntimeServices.Multiplayer.RequestHost(port, key, display);
            Status.text = ok ? "正在创建房间……" : "创建失败：请确认已经进入城市且当前没有 Forge 会话。";
        }

        private void InviteFriends()
        {
            MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
            if (value.Mode != MultiplayerSessionMode.Hosting) { Status.text = "请先成功创建房间。"; return; }
            lastInvite = ForgeMultiplayerUi.BuildInviteCode(ForgeMultiplayerUi.LocalIpv4(), ForgeMod.Settings.Port.value,
                ForgeMultiplayerUi.RoomKey);
            GUIUtility.systemCopyBuffer = lastInvite;
            if (PlatformService.active && PlatformService.IsOverlayEnabled())
            {
                PlatformService.ActivateGameOverlay(GameOverlayDialog.Friends);
                Status.text = "开发版 LAN 邀请已复制，并已打开 Steam 好友；把剪贴板内容发给好友。该方式不提供 NAT 穿透。";
            }
            else Status.text = "开发版 LAN 邀请已复制到剪贴板；Steam Overlay 当前不可用，请手动发给好友。";
        }
    }
}
