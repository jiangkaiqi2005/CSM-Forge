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
        private static string pendingSteamInvite;

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
            if (!string.IsNullOrEmpty(pendingSteamInvite))
            {
                ForgeMainMenuJoinPanel panel = ShowPanel<ForgeMainMenuJoinPanel>();
                if (panel != null)
                {
                    string value = pendingSteamInvite; pendingSteamInvite = null;
                    panel.ApplySteamInvite(value);
                }
            }
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
            button.eventClick += delegate
            {
                MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
                if (mode == MultiplayerSessionMode.Offline || mode == MultiplayerSessionMode.Faulted)
                    ShowPanel<ForgeHostGamePanel>();
                else ShowPanel<ForgeSessionPanel>();
            };
            EnsurePump();
            MultiplayerSessionMode currentMode = RuntimeServices.Multiplayer.Status.Mode;
            if (currentMode == MultiplayerSessionMode.ConnectingClient || currentMode == MultiplayerSessionMode.ClientCatchingUp)
                ShowPanel<ForgeJoinProgressPanel>();
        }

        internal static void Shutdown()
        {
            UIView view = UIView.GetAView();
            if (view == null) return;
            DestroyNamed(view, MainButtonName); DestroyNamed(view, PauseButtonName);
            DestroyNamed(view, typeof(ForgeMainMenuJoinPanel).Name);
            DestroyNamed(view, typeof(ForgeHostGamePanel).Name);
            DestroyNamed(view, typeof(ForgeSessionPanel).Name);
            DestroyNamed(view, typeof(ForgePlayersPanel).Name);
            DestroyNamed(view, typeof(ForgeChatPanel).Name);
            DestroyNamed(view, typeof(ForgeJoinProgressPanel).Name);
            ForgeMenuPump pump = view.gameObject.GetComponent<ForgeMenuPump>();
            if (pump != null) UnityEngine.Object.Destroy(pump);
            ForgePlayerPresenceUi presence = view.gameObject.GetComponent<ForgePlayerPresenceUi>();
            if (presence != null) UnityEngine.Object.Destroy(presence);
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
            if (view != null && view.gameObject.GetComponent<ForgePlayerPresenceUi>() == null)
                view.gameObject.AddComponent<ForgePlayerPresenceUi>();
        }

        private static T ShowPanel<T>() where T : UIComponent
        {
            UIView view = UIView.GetAView(); if (view == null) return null;
            T panel = view.FindUIComponent<T>(typeof(T).Name);
            if (panel == null) { panel = (T)view.AddUIComponent(typeof(T)); panel.name = typeof(T).Name; }
            panel.isVisible = true; panel.BringToFront(); panel.Focus(); return panel;
        }

        internal static void OpenPanel<T>() where T : UIComponent { ShowPanel<T>(); }

        internal static void AcceptSteamInvite(string invite)
        {
            IPEndPoint endpoint; string key;
            if (!TryParseInviteCode(invite, out endpoint, out key) || RuntimeServices.Lifecycle.Current.IsValid) return;
            pendingSteamInvite = invite;
            ForgeMainMenuJoinPanel panel = ShowPanel<ForgeMainMenuJoinPanel>();
            if (panel != null) { pendingSteamInvite = null; panel.ApplySteamInvite(invite); }
        }

        internal static void HandleHotkeys()
        {
            MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
            if ((mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.ClientLive) &&
                !UIView.HasModalInput() && !UIView.HasInputFocus() && Input.GetKeyDown(KeyCode.T))
                ShowPanel<ForgeChatPanel>();
        }

        private static void DestroyNamed(UIView view, string name)
        {
            UIComponent component = view.FindUIComponent(name);
            if (component != null) UnityEngine.Object.Destroy(component.gameObject);
        }
    }

    internal sealed class ForgeMenuPump : MonoBehaviour
    {
        private void Update()
        {
            RuntimeServices.Multiplayer.PollMainMenu();
            ForgeMultiplayerUi.HandleHotkeys();
        }
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

        protected UIButton SmallButton(string text, float x, float y, float buttonWidth, Action action)
        {
            UIButton button = AddUIComponent<UIButton>(); button.text = text; button.width = buttonWidth; button.height = 32;
            button.relativePosition = new Vector3(x, y); button.normalBgSprite = "ButtonMenu";
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
        private string deferredSteamInvite;

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
            if (!string.IsNullOrEmpty(deferredSteamInvite)) ApplySteamInvite(deferredSteamInvite);
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
            if (display.Length == 0 || display.Length > 32 || Encoding.UTF8.GetByteCount(display) > 64)
            { Status.text = "显示名需为 1–32 个字符（UTF-8 最多 64 字节）。"; return; }
            ForgeMod.Settings.DisplayName.value = display;
            ForgeMod.Settings.HostAddress.value = endpoint.Address.ToString(); ForgeMod.Settings.Port.value = endpoint.Port;
            bool ok = RuntimeServices.Patches.Installed &&
                RuntimeServices.Multiplayer.RequestJoinFromMainMenu(endpoint, key, display);
            if (ok)
            {
                Status.text = "正在连接房主；收到快照后会自动进入城市……";
                isVisible = false;
                ForgeMultiplayerUi.OpenPanel<ForgeJoinProgressPanel>();
            }
            else Status.text = "无法开始连接：请确认 CitiesHarmony/Forge patches 已就绪且当前没有会话。";
        }

        internal void ApplySteamInvite(string value)
        {
            if (invite == null) { deferredSteamInvite = value; return; }
            invite.text = value; deferredSteamInvite = null;
            Status.text = "已收到 Steam 好友邀请，正在连接……";
            Join();
        }
    }

    internal sealed class ForgeHostGamePanel : ForgePanelBase
    {
        private UITextField portField;
        private UITextField keyField;
        private UITextField nameField;
        private UILabel players;

        public override void Start()
        {
            Configure("创建 CSM-Forge 房间", 560);
            Label("显示名", 58); nameField = Field(ForgeMod.Settings.DisplayName.value, 82, false);
            Label("UDP 端口", 124); portField = Field(ForgeMod.Settings.Port.value.ToString(), 148, true);
            Label("临时房间口令", 190); keyField = Field(ForgeMultiplayerUi.RoomKey, 214, false);
            Status = Label(StatusText(), 260);
            players = Label("房间尚未创建", 285);
            Button("创建房间（当前城市作为房主）", 320, CreateRoom);
            Button("关闭", 390, delegate { isVisible = false; });
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                Status.text = StatusText();
                players.text = value.Mode == MultiplayerSessionMode.Hosting ?
                    "房间已创建，正在打开会话管理……" : "房间尚未创建";
                if (value.Mode == MultiplayerSessionMode.Hosting)
                {
                    isVisible = false;
                    ForgeMultiplayerUi.OpenPanel<ForgeSessionPanel>();
                }
            }
            base.Update();
        }

        private void CreateRoom()
        {
            int port; string display = (nameField.text ?? string.Empty).Trim(); string key = keyField.text ?? string.Empty;
            if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
            { Status.text = "端口必须是 1–65535。"; return; }
            if (display.Length == 0 || display.Length > 32 || Encoding.UTF8.GetByteCount(display) > 64 ||
                key.Length == 0 || key.Length > 64)
            { Status.text = "显示名需为 1–32 个字符（UTF-8 最多 64 字节），房间口令需为 1–64 个字符。"; return; }
            ForgeMod.Settings.DisplayName.value = display; ForgeMod.Settings.Port.value = port;
            ForgeMultiplayerUi.RoomKey = key;
            bool ok = RuntimeServices.Patches.Installed && RuntimeServices.Multiplayer.RequestHost(port, key, display);
            Status.text = ok ? "正在创建房间……" : "创建失败：请确认已经进入城市且当前没有 Forge 会话。";
        }
    }

    internal sealed class ForgeSessionPanel : ForgePanelBase
    {
        private UILabel players;
        private UIButton invite;
        private string lastInvite;

        public override void Start()
        {
            Configure("CSM-Forge 多人联机", 485);
            Status = Label(StatusText(), 62);
            players = Label("玩家：读取中……", 92);
            Button("玩家列表", 132, delegate { ForgeMultiplayerUi.OpenPanel<ForgePlayersPanel>(); });
            Button("多人聊天（快捷键 T）", 184, delegate { ForgeMultiplayerUi.OpenPanel<ForgeChatPanel>(); });
            invite = Button("邀请 Steam 好友", 236, InviteFriends);
            Button("停止房间 / 断开", 288, delegate
            {
                RuntimeServices.Multiplayer.RequestStop(); Status.text = "正在停止会话……";
            });
            Button("关闭", 350, delegate { isVisible = false; });
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                Status.text = StatusText();
                players.text = "玩家：" + value.Players.Length + " 人（" + LiveCount(value.Players) + " 人已就绪）";
                invite.isVisible = value.Mode == MultiplayerSessionMode.Hosting;
                invite.isEnabled = invite.isVisible;
                if (value.Mode == MultiplayerSessionMode.Offline) isVisible = false;
            }
            base.Update();
        }

        private static int LiveCount(MultiplayerPlayerSnapshot[] values)
        { int count = 0; for (int i = 0; i < values.Length; i++) if (values[i].IsLive) count++; return count; }

        private void InviteFriends()
        {
            if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Hosting)
            { Status.text = "只有房主可以邀请其他玩家。"; return; }
            lastInvite = ForgeMultiplayerUi.BuildInviteCode(ForgeMultiplayerUi.LocalIpv4(), ForgeMod.Settings.Port.value,
                ForgeMultiplayerUi.RoomKey);
            bool steamJoinReady = ForgeSteamRichPresence.PublishInvite(lastInvite,
                Math.Max(1, RuntimeServices.Multiplayer.Status.Players.Length));
            GUIUtility.systemCopyBuffer = lastInvite;
            if (PlatformService.active && PlatformService.IsOverlayEnabled())
            {
                PlatformService.ActivateGameOverlay(GameOverlayDialog.Friends);
                Status.text = steamJoinReady
                    ? "已发布 Steam 加入状态并打开好友列表；也已复制直连邀请码。该方式不提供 NAT 穿透。"
                    : "已打开 Steam 好友并复制直连邀请码；Steam 点击加入当前不可用。该方式不提供 NAT 穿透。";
            }
            else Status.text = "开发版 LAN 邀请已复制到剪贴板；Steam Overlay 当前不可用，请手动发给好友。";
        }
    }

    internal sealed class ForgePlayersPanel : ForgePanelBase
    {
        private readonly UILabel[] names = new UILabel[9];
        private readonly UIButton[] kick = new UIButton[9];

        public override void Start()
        {
            Configure("已连接玩家", 470);
            for (int i = 0; i < names.Length; i++)
            {
                int row = i;
                names[i] = Label(string.Empty, 60 + i * 34);
                kick[i] = SmallButton("移出", 390, 55 + i * 34, 90, delegate { Kick(row); });
            }
            Button("关闭", 380, delegate { isVisible = false; });
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                for (int i = 0; i < names.Length; i++)
                {
                    bool present = i < value.Players.Length;
                    names[i].isVisible = present; kick[i].isVisible = present;
                    if (!present) continue;
                    MultiplayerPlayerSnapshot player = value.Players[i];
                    names[i].text = (player.IsHost ? "[房主] " : "") + player.DisplayName +
                        (player.IsLocal ? "（你）" : "") + (player.IsLive ? "" : " — 正在加入");
                    kick[i].isVisible = value.Mode == MultiplayerSessionMode.Hosting && !player.IsHost && !player.IsLocal;
                    kick[i].isEnabled = kick[i].isVisible;
                }
            }
            base.Update();
        }

        private void Kick(int index)
        {
            MultiplayerPlayerSnapshot[] values = RuntimeServices.Multiplayer.Status.Players;
            if (index >= 0 && index < values.Length) RuntimeServices.Multiplayer.RequestKick(values[index].Member);
        }
    }

    internal sealed class ForgeChatPanel : ForgePanelBase
    {
        private UILabel log;
        private UITextField input;

        public override void Start()
        {
            Configure("多人聊天", 430);
            log = Label("按 T 可以随时打开聊天。", 55); log.width = 480; log.height = 240;
            log.wordWrap = true; log.autoHeight = false;
            input = Field(string.Empty, 305, false);
            SmallButton("发送", 20, 350, 300, Send);
            SmallButton("关闭", 330, 350, 170, delegate { isVisible = false; });
            input.eventKeyDown += delegate(UIComponent component, UIKeyEventParameter parameter)
            {
                if (parameter.keycode == KeyCode.Return || parameter.keycode == KeyCode.KeypadEnter)
                { parameter.Use(); Send(); }
                else if (parameter.keycode == KeyCode.Escape) { parameter.Use(); isVisible = false; }
            };
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerChatSnapshot[] values = RuntimeServices.Multiplayer.Status.Chat;
                StringBuilder builder = new StringBuilder();
                int start = Math.Max(0, values.Length - 10);
                for (int i = start; i < values.Length; i++) builder.Append('<').Append(values[i].DisplayName)
                    .Append("> ").Append(values[i].Text).Append('\n');
                log.text = builder.Length == 0 ? "还没有聊天消息。" : builder.ToString();
            }
            base.Update();
        }

        private void Send()
        {
            string text = (input.text ?? string.Empty).Trim();
            if (RuntimeServices.Multiplayer.TrySendChat(text)) input.text = string.Empty;
        }
    }

    internal sealed class ForgeJoinProgressPanel : UIPanel
    {
        private UILabel status;
        private UIButton cancel;

        public override void Start()
        {
            UIView view = GetUIView(); width = view.GetScreenResolution().x; height = view.GetScreenResolution().y;
            relativePosition = Vector3.zero; backgroundSprite = "GenericPanel"; color = new Color32(18, 18, 24, 225);
            status = AddUIComponent<UILabel>(); status.textScale = 1.15f; status.textAlignment = UIHorizontalAlignment.Center;
            status.width = 640; status.height = 90;
            status.relativePosition = new Vector3(width / 2f - 320, height / 2f - 80);
            cancel = AddUIComponent<UIButton>(); cancel.text = "取消加入"; cancel.width = 340; cancel.height = 44;
            cancel.normalBgSprite = "ButtonMenu"; cancel.hoveredBgSprite = "ButtonMenuHovered";
            cancel.pressedBgSprite = "ButtonMenuPressed"; cancel.relativePosition = new Vector3(width / 2f - 170, height / 2f + 35);
            cancel.eventClick += delegate { RuntimeServices.Multiplayer.StopImmediately(); isVisible = false; };
            base.Start();
        }

        public override void Update()
        {
            MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
            if (value.Mode == MultiplayerSessionMode.Offline) { isVisible = false; return; }
            if (value.Mode == MultiplayerSessionMode.Faulted)
            { status.text = "加入失败\n" + value.Detail; cancel.text = "关闭"; base.Update(); return; }
            if (value.Mode == MultiplayerSessionMode.ClientLive) { isVisible = false; return; }
            if (value.SnapshotBytesTotal > 0)
            {
                double percent = value.SnapshotBytesReceived * 100.0 / value.SnapshotBytesTotal;
                status.text = "正在下载房主城市……\n" + percent.ToString("0.0") + "%  " +
                    FormatBytes(value.SnapshotBytesReceived) + " / " + FormatBytes(value.SnapshotBytesTotal);
            }
            else status.text = "正在连接并核对游戏内容……\n" + value.Detail;
            base.Update();
        }

        private static string FormatBytes(ulong bytes)
        { return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : (bytes / 1024.0).ToString("0.0") + " KB"; }
    }
}
