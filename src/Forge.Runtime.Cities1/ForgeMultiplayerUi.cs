using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
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
        private const string ModalBackdropName = "CSMForgeModalBackdrop";
        private static readonly Type[] ManagedPanelTypes = new Type[]
        {
            typeof(ForgeMainMenuJoinPanel), typeof(ForgeHostGamePanel), typeof(ForgeSessionPanel),
            typeof(ForgePlayersPanel), typeof(ForgeChatPanel), typeof(ForgeJoinProgressPanel),
            typeof(ForgeLeaveConfirmPanel), typeof(ForgeFaultPanel)
        };
        private static readonly Stack<Type> PanelHistory = new Stack<Type>();
        private static Type activePanelType;
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
            button.eventClick += delegate { OpenMainMenuEntry(); };
            EnsurePump();
            if (!string.IsNullOrEmpty(pendingSteamInvite)) OpenMainMenuEntry();
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
                ClosePauseMenu();
                if (mode == MultiplayerSessionMode.Faulted) ShowPanel<ForgeFaultPanel>();
                else if (mode == MultiplayerSessionMode.Offline)
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
            DestroyNamed(view, ModalBackdropName);
            DestroyNamed(view, typeof(ForgeMainMenuJoinPanel).Name);
            DestroyNamed(view, typeof(ForgeHostGamePanel).Name);
            DestroyNamed(view, typeof(ForgeSessionPanel).Name);
            DestroyNamed(view, typeof(ForgePlayersPanel).Name);
            DestroyNamed(view, typeof(ForgeChatPanel).Name);
            DestroyNamed(view, typeof(ForgeJoinProgressPanel).Name);
            DestroyNamed(view, typeof(ForgeLeaveConfirmPanel).Name);
            DestroyNamed(view, typeof(ForgeFaultPanel).Name);
            ForgeMenuPump pump = view.gameObject.GetComponent<ForgeMenuPump>();
            if (pump != null) UnityEngine.Object.Destroy(pump);
            ForgePlayerPresenceUi presence = view.gameObject.GetComponent<ForgePlayerPresenceUi>();
            if (presence != null) UnityEngine.Object.Destroy(presence);
            PanelHistory.Clear(); activePanelType = null;
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

        /// <summary>
        /// S1: pick the address of a real uplink instead of the first non-loopback IPv4 — DNS
        /// ordering previously handed back a gateway-less VPN/TUN adapter (2.0.0.1 on this dev
        /// machine), producing invite codes friends could not reach. Prefer up interfaces that
        /// own an IPv4 default gateway and are not tunnels; fall back to the old DNS probe.
        /// Verified on the dev machine (multi-homed + Hyper-V/WSL virtual switches): returns the
        /// gatewayed Ethernet address, skipping the TUN adapter and virtual switches.
        /// </summary>
        internal static string LocalIpv4()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface candidate = interfaces[i];
                    if (candidate.OperationalStatus != OperationalStatus.Up) continue;
                    if (candidate.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        candidate.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (!HasIpv4Gateway(candidate.GetIPProperties())) continue;
                    string address = FirstIpv4Of(candidate);
                    if (address != null) return address;
                }
            }
            catch { }
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

        private static bool HasIpv4Gateway(IPInterfaceProperties properties)
        {
            if (properties == null || properties.GatewayAddresses == null) return false;
            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                if (gateway != null && gateway.Address != null &&
                    gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return true;
            return false;
        }

        private static bool IsLinkLocalIpv4(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }

        private static string FirstIpv4Of(NetworkInterface candidate)
        {
            IPInterfaceProperties properties = candidate.GetIPProperties();
            if (properties == null || properties.UnicastAddresses == null) return null;
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                IPAddress address = unicast.Address;
                if (address != null && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) && !IsLinkLocalIpv4(address)) return address.ToString();
            }
            return null;
        }

        private static void EnsurePump()
        {
            UIView view = UIView.GetAView();
            if (view != null && view.gameObject.GetComponent<ForgeMenuPump>() == null)
                view.gameObject.AddComponent<ForgeMenuPump>();
            if (view != null && view.gameObject.GetComponent<ForgePlayerPresenceUi>() == null)
                view.gameObject.AddComponent<ForgePlayerPresenceUi>();
        }

        private static void ClosePauseMenu()
        {
            try
            {
                MethodInfo resume = AccessTools.Method(typeof(PauseMenu), "Resume");
                if (resume != null) resume.Invoke(new PauseMenu(), new object[0]);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[CSM-Forge] could not close pause menu before opening multiplayer UI: " + error);
            }
        }

        private static T ShowPanel<T>() where T : UIComponent
        {
            PanelHistory.Clear();
            return (T)DisplayExclusive(typeof(T));
        }

        internal static void ReplacePanel<T>() where T : UIComponent
        {
            PanelHistory.Clear(); DisplayExclusive(typeof(T));
        }

        internal static void OpenChildPanel<T>() where T : UIComponent
        {
            UIView view = UIView.GetAView();
            UIComponent active = view == null || activePanelType == null ? null :
                view.FindUIComponent(activePanelType.Name);
            if (active != null && active.isVisible && activePanelType != typeof(T))
                PanelHistory.Push(activePanelType);
            DisplayExclusive(typeof(T));
        }

        internal static void CloseOrBack(UIComponent panel)
        {
            if (panel != null) panel.isVisible = false;
            if (PanelHistory.Count > 0) DisplayExclusive(PanelHistory.Pop());
            else { activePanelType = null; HideModalBackdrop(); }
        }

        internal static void Dismiss(UIComponent panel)
        {
            if (panel != null) panel.isVisible = false;
            if (panel != null && activePanelType == panel.GetType()) activePanelType = null;
            PanelHistory.Clear(); HideModalBackdrop();
        }

        private static UIComponent DisplayExclusive(Type panelType)
        {
            UIView view = UIView.GetAView(); if (view == null) return null;
            HideManagedPanels(view, panelType);
            UIPanel backdrop = EnsureModalBackdrop(view);
            backdrop.isVisible = true; backdrop.BringToFront();
            UIComponent panel = view.FindUIComponent(panelType.Name);
            if (panel == null) { panel = view.AddUIComponent(panelType); panel.name = panelType.Name; }
            activePanelType = panelType;
            panel.isVisible = true; panel.BringToFront(); panel.Focus(); return panel;
        }

        private static UIPanel EnsureModalBackdrop(UIView view)
        {
            UIPanel backdrop = view.FindUIComponent(ModalBackdropName) as UIPanel;
            if (backdrop == null)
            {
                backdrop = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                backdrop.name = ModalBackdropName; backdrop.backgroundSprite = "GenericPanel";
                backdrop.color = new Color32(0, 0, 0, 150); backdrop.isInteractive = true; backdrop.canFocus = true;
                backdrop.eventClick += delegate(UIComponent component, UIMouseEventParameter parameter)
                { parameter.Use(); };
            }
            backdrop.relativePosition = Vector3.zero;
            backdrop.size = view.GetScreenResolution();
            return backdrop;
        }

        private static void HideModalBackdrop()
        {
            UIView view = UIView.GetAView();
            UIComponent backdrop = view == null ? null : view.FindUIComponent(ModalBackdropName);
            if (backdrop != null) backdrop.isVisible = false;
        }

        private static void HideManagedPanels(UIView view, Type except)
        {
            for (int i = 0; i < ManagedPanelTypes.Length; i++)
            {
                Type type = ManagedPanelTypes[i];
                if (type == except) continue;
                UIComponent panel = view.FindUIComponent(type.Name);
                if (panel != null) panel.isVisible = false;
            }
        }

        internal static void AcceptSteamInvite(string invite)
        {
            IPEndPoint endpoint; string key;
            if (!TryParseInviteCode(invite, out endpoint, out key) || RuntimeServices.Lifecycle.Current.IsValid) return;
            pendingSteamInvite = invite;
            if (RuntimeServices.Multiplayer.Status.Mode == MultiplayerSessionMode.Faulted)
            {
                ShowPanel<ForgeFaultPanel>();
                return;
            }
            OpenMainJoinWithPendingInvite();
        }

        internal static void OpenMainMenuEntry()
        {
            if (RuntimeServices.Multiplayer.Status.Mode == MultiplayerSessionMode.Faulted)
            { ShowPanel<ForgeFaultPanel>(); return; }
            OpenMainJoinWithPendingInvite();
        }

        internal static void OpenMainJoinWithPendingInvite()
        {
            ForgeMainMenuJoinPanel panel = ShowPanel<ForgeMainMenuJoinPanel>();
            if (panel != null && !string.IsNullOrEmpty(pendingSteamInvite))
            {
                string value = pendingSteamInvite; pendingSteamInvite = null; panel.ApplySteamInvite(value);
            }
        }

        internal static void HandleHotkeys()
        {
            MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
            if ((mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.ClientLive) &&
                !UIView.HasModalInput() && !UIView.HasInputFocus() && Input.GetKeyDown(KeyCode.T))
                OpenChildPanel<ForgeChatPanel>();
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
            backgroundSprite = "GenericPanel"; color = new Color32(110, 110, 110, 250);
            width = 360; height = panelHeight;
            UIView view = GetUIView();
            relativePosition = new Vector3(view.GetScreenResolution().x / 2f - width / 2f,
                view.GetScreenResolution().y / 2f - height / 2f);
            AddUIComponent(typeof(UIDragHandle)).size = new Vector2(width, 45);
            UILabel label = AddUIComponent<UILabel>(); label.text = title; label.textScale = 1.25f;
            label.relativePosition = new Vector3(20, 14);
        }

        protected UILabel Label(string text, float y)
        {
            UILabel label = AddUIComponent<UILabel>();
            label.autoSize = false; label.autoHeight = false; label.width = 340; label.height = 32;
            label.textScale = 0.9f; label.wordWrap = true; label.relativePosition = new Vector3(10, y);
            label.text = text; return label;
        }

        protected static void SetText(UILabel label, string value)
        { if (label.text != value) label.text = value; }

        protected static void SetText(UIButton button, string value)
        { if (button.text != value) button.text = value; }

        protected UITextField Field(string text, float y, bool numeric)
        {
            UITextField field = AddUIComponent<UITextField>(); field.text = text; field.width = 340; field.height = 32;
            field.relativePosition = new Vector3(10, y); field.normalBgSprite = "TextFieldPanel";
            field.hoveredBgSprite = "TextFieldPanelHovered"; field.focusedBgSprite = "TextFieldPanelFocused";
            field.padding = new RectOffset(8, 8, 7, 7); field.builtinKeyNavigation = true; field.numericalOnly = numeric;
            return field;
        }

        protected UIButton Button(string text, float y, Action action)
        {
            UIButton button = AddUIComponent<UIButton>(); button.text = text; button.width = 340; button.height = 42;
            button.relativePosition = new Vector3(10, y); button.normalBgSprite = "ButtonMenu";
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
            switch (value.Mode)
            {
                case MultiplayerSessionMode.Offline: return "尚未连接。";
                case MultiplayerSessionMode.StartingHost: return "正在创建房间……";
                case MultiplayerSessionMode.Hosting:
                    return "房间运行中 · " + Math.Max(1, value.Players.Length) + " 人";
                case MultiplayerSessionMode.ConnectingClient: return "正在连接并核对游戏内容……";
                case MultiplayerSessionMode.ClientCatchingUp:
                    return "正在同步房主城市……";
                case MultiplayerSessionMode.ClientLive:
                    return "已加入房间 · " + Math.Max(1, value.Players.Length) + " 人";
                case MultiplayerSessionMode.Faulted:
                    return "联机已停止。诊断代码：" + value.Detail;
                default: return "正在更新联机状态……";
            }
        }

        public override void Update()
        {
            if (isVisible && Input.GetKeyDown(KeyCode.Escape)) ForgeMultiplayerUi.CloseOrBack(this);
            base.Update();
        }
    }

    internal sealed class ForgeMainMenuJoinPanel : ForgePanelBase
    {
        private UITextField invite;
        private UITextField nameField;
        private UIButton joinButton;
        private string deferredSteamInvite;

        public override void Start()
        {
            Configure("CSM-Forge 多人联机", 465);
            Label("加入好友：粘贴房主发来的 LAN 邀请信息", 62);
            invite = Field(string.Empty, 88, false);
            Label("你的显示名", 132);
            string display = ForgeMod.Settings.DisplayName.value;
            if (PlatformService.active && !string.IsNullOrEmpty(PlatformService.personaName)) display = PlatformService.personaName;
            nameField = Field(display, 158, false);
            Status = Label("尚未连接。加入后会自动下载并加载房主存档。", 205);
            joinButton = Button("加入房间", 242, Join);
            UILabel hostHelp = Label("想当房主：返回主菜单，先载入或新建一个城市；进入地图后按 Esc，点击“FORGE 多人联机”创建房间。", 300);
            hostHelp.height = 62; hostHelp.autoHeight = false;
            Button("返回主菜单", 370, delegate
            {
                // WP-1.2: never tear the session down from the UI thread; queue to the simulation
                // thread when a city identity exists (RequestStop falls back to a direct stop at
                // the main menu, where UI and the menu pump share one thread).
                if (!RuntimeServices.Multiplayer.RequestStop()) RuntimeServices.Multiplayer.StopImmediately();
                ForgeMultiplayerUi.Dismiss(this);
            });
            base.Start();
            if (!string.IsNullOrEmpty(deferredSteamInvite)) ApplySteamInvite(deferredSteamInvite);
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
                joinButton.isEnabled = mode == MultiplayerSessionMode.Offline;
                if (mode != MultiplayerSessionMode.Offline) SetText(Status, StatusText());
            }
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
                joinButton.isEnabled = false;
                Status.text = "正在连接房主；收到快照后会自动进入城市……";
                ForgeMultiplayerUi.ReplacePanel<ForgeJoinProgressPanel>();
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
        private UILabel preflightLabel;
        private UIButton createButton;
        private ForgeRoomPreflightReport preflight;
        private string feedback;
        private uint observedPatchStatusRevision;
        private MultiplayerSessionMode observedMode;

        public override void Start()
        {
            Configure("创建 CSM-Forge 房间", 630);
            Label("显示名", 58); nameField = Field(ForgeMod.Settings.DisplayName.value, 82, false);
            Label("UDP 端口", 124); portField = Field(ForgeMod.Settings.Port.value.ToString(), 148, true);
            Label("临时房间口令", 190); keyField = Field(ForgeMultiplayerUi.RoomKey, 214, false);
            Status = Label("设置完成后点击创建房间。", 260);
            preflightLabel = Label(string.Empty, 300); preflightLabel.height = 86; preflightLabel.autoHeight = false;
            players = Label("直连地址：" + ForgeMultiplayerUi.LocalIpv4(), 395);
            createButton = Button("创建房间（当前城市作为房主）", 440, CreateRoom);
            Button("重新检查 DLC / Mod / 资产", 492, RefreshPreflight);
            Button("取消", 544, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
            base.Start();
            RefreshPreflight();
        }

        public override void Update()
        {
            if (isVisible)
            {
                uint patchStatusRevision = RuntimeServices.Patches.StatusRevision;
                if (patchStatusRevision != observedPatchStatusRevision) RefreshPreflight();
                // WP-1.6: the panel never runs the manifest collection inline; it displays the
                // cached report and queues a fresh simulation-thread evaluation when inputs move.
                ForgeRoomPreflight.RequestEvaluation(false, patchStatusRevision);
                preflight = ForgeRoomPreflight.PeekCached();
                bool reportCurrent = preflight != null && ForgeRoomPreflight.CachedPatchRevision() == patchStatusRevision;
                if (preflight != null && reportCurrent) SetText(preflightLabel, preflight.Message);
                else if (ForgeRoomPreflight.EvaluationInFlight()) SetText(preflightLabel, "正在检查 DLC / Mod / 资产……");
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                // WP-1.2: teardown is queued now, so the Offline transition can land after this
                // panel opened; re-run the preflight when it does.
                if (value.Mode != observedMode)
                {
                    observedMode = value.Mode;
                    if (value.Mode == MultiplayerSessionMode.Offline) RefreshPreflight();
                }
                createButton.isEnabled = value.Mode == MultiplayerSessionMode.Offline &&
                    reportCurrent && preflight != null && preflight.CanHost;
                if (value.Mode != MultiplayerSessionMode.Offline || string.IsNullOrEmpty(feedback))
                    SetText(Status, value.Mode == MultiplayerSessionMode.Offline ?
                        "设置完成后点击创建房间。" : StatusText());
                SetText(players, value.Mode == MultiplayerSessionMode.Hosting ?
                    "房间已创建，正在打开会话管理……" : "直连地址：" + ForgeMultiplayerUi.LocalIpv4());
                if (value.Mode == MultiplayerSessionMode.Hosting)
                {
                    ForgeMultiplayerUi.ReplacePanel<ForgeSessionPanel>();
                }
                else if (value.Mode == MultiplayerSessionMode.Faulted)
                    ForgeMultiplayerUi.ReplacePanel<ForgeFaultPanel>();
            }
            base.Update();
        }

        private void CreateRoom()
        {
            UnityEngine.Debug.Log("[CSM-Forge] create room clicked.");
            ForgeRoomPreflightReport report = ForgeRoomPreflight.PeekCached();
            if (report == null || ForgeRoomPreflight.CachedPatchRevision() != RuntimeServices.Patches.StatusRevision)
            { RefreshPreflight(); SetFeedback("正在重新检查 DLC / Mod / 资产清单，请稍候。"); return; }
            UnityEngine.Debug.Log("[CSM-Forge] create room preflight completed; canHost=" + report.CanHost + ".");
            if (!report.CanHost)
            { SetFeedback(report.Message); return; }
            int port; string display = (nameField.text ?? string.Empty).Trim(); string key = keyField.text ?? string.Empty;
            if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
            { SetFeedback("端口必须是 1–65535。"); return; }
            if (display.Length == 0 || display.Length > 32 || Encoding.UTF8.GetByteCount(display) > 64 ||
                key.Length == 0 || key.Length > 64)
            { SetFeedback("显示名需为 1–32 个字符（UTF-8 最多 64 字节），房间口令需为 1–64 个字符。"); return; }
            if (!RuntimeServices.Patches.Installed)
            { SetFeedback("Forge patches 尚未就绪。请确认 CitiesHarmony 已启用，然后重启游戏。"); return; }
            ForgeMod.Settings.DisplayName.value = display; ForgeMod.Settings.Port.value = port;
            ForgeMultiplayerUi.RoomKey = key;
            bool ok = RuntimeServices.Multiplayer.RequestHost(port, key, display);
            UnityEngine.Debug.Log("[CSM-Forge] create room host request returned; queued=" + ok + ".");
            if (ok) createButton.isEnabled = false;
            SetFeedback(ok ? "正在创建房间……" :
                "创建失败：当前城市会话不可用或已有 Forge 会话。请关闭此页后重试。");
        }

        private void RefreshPreflight()
        {
            observedPatchStatusRevision = RuntimeServices.Patches.StatusRevision;
            ForgeRoomPreflight.RequestEvaluation(true, observedPatchStatusRevision);
        }

        private void SetFeedback(string value) { feedback = value; Status.text = value; }
    }

    internal sealed class ForgeSessionPanel : ForgePanelBase
    {
        private UILabel players;
        private UILabel notice;
        private UIButton invite;
        private string lastInvite;

        public override void Start()
        {
            Configure("CSM-Forge 多人联机", 485);
            Status = Label(StatusText(), 62);
            players = Label("玩家：读取中……", 92);
            Button("玩家列表", 132, delegate { ForgeMultiplayerUi.OpenChildPanel<ForgePlayersPanel>(); });
            Button("多人聊天（快捷键 T）", 184, delegate { ForgeMultiplayerUi.OpenChildPanel<ForgeChatPanel>(); });
            invite = Button("复制直连邀请并打开 Steam", 236, InviteFriends);
            Button("停止房间 / 断开", 288,
                delegate { ForgeMultiplayerUi.OpenChildPanel<ForgeLeaveConfirmPanel>(); });
            Button("关闭", 350, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
            notice = Label(string.Empty, 405);
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                SetText(Status, StatusText());
                SetText(players, "玩家：" + value.Players.Length + " 人（" + LiveCount(value.Players) + " 人已就绪）");
                invite.isVisible = value.Mode == MultiplayerSessionMode.Hosting;
                invite.isEnabled = invite.isVisible;
                if (value.Mode == MultiplayerSessionMode.Offline) ForgeMultiplayerUi.Dismiss(this);
                else if (value.Mode == MultiplayerSessionMode.Faulted)
                    ForgeMultiplayerUi.ReplacePanel<ForgeFaultPanel>();
            }
            base.Update();
        }

        private static int LiveCount(MultiplayerPlayerSnapshot[] values)
        { int count = 0; for (int i = 0; i < values.Length; i++) if (values[i].IsLive) count++; return count; }

        internal void SetNotice(string value) { notice.text = value ?? string.Empty; }

        private void InviteFriends()
        {
            if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Hosting)
            { notice.text = "只有房主可以邀请其他玩家。"; return; }
            lastInvite = ForgeMultiplayerUi.BuildInviteCode(ForgeMultiplayerUi.LocalIpv4(), ForgeMod.Settings.Port.value,
                ForgeMultiplayerUi.RoomKey);
            GUIUtility.systemCopyBuffer = lastInvite;
            if (PlatformService.active && PlatformService.IsOverlayEnabled())
            {
                PlatformService.ActivateGameOverlay(GameOverlayDialog.Friends);
                notice.text = "已复制直连邀请码并打开 Steam 好友；请粘贴发送给好友。自动点击加入已为稳定性停用。该方式不提供 NAT 穿透。";
            }
            else notice.text = "直连邀请码已复制到剪贴板；Steam Overlay 当前不可用，请手动粘贴发送给好友。";
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
                names[i].width = 240;
                kick[i] = SmallButton("移出", 260, 55 + i * 34, 90, delegate { Kick(row); });
            }
            Button("返回", 380, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
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
                    SetText(names[i], (player.IsHost ? "[房主] " : "") + player.DisplayName +
                        (player.IsLocal ? "（你）" : "") + (player.IsLive ? "" : " — 正在加入"));
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
            log = Label("按 T 可以随时打开聊天。", 55); log.width = 340; log.height = 240;
            log.wordWrap = true; log.autoHeight = false;
            input = Field(string.Empty, 305, false);
            SmallButton("发送", 10, 350, 205, Send);
            SmallButton("返回", 225, 350, 125, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
            input.eventKeyDown += delegate(UIComponent component, UIKeyEventParameter parameter)
            {
                if (parameter.keycode == KeyCode.Return || parameter.keycode == KeyCode.KeypadEnter)
                { parameter.Use(); Send(); }
                else if (parameter.keycode == KeyCode.Escape)
                { parameter.Use(); ForgeMultiplayerUi.CloseOrBack(this); }
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
                SetText(log, builder.Length == 0 ? "还没有聊天消息。" : builder.ToString());
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
            cancel.eventClick += delegate
            {
                // WP-1.2: serialized with the simulation thread so a cancel during snapshot
                // download cannot dispose the receive file mid-chunk.
                if (!RuntimeServices.Multiplayer.RequestStop()) RuntimeServices.Multiplayer.StopImmediately();
                ForgeMultiplayerUi.Dismiss(this);
            };
            base.Start();
        }

        public override void Update()
        {
            MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
            if (value.Mode == MultiplayerSessionMode.Offline) { ForgeMultiplayerUi.Dismiss(this); return; }
            if (value.Mode == MultiplayerSessionMode.Faulted)
            { ForgeMultiplayerUi.ReplacePanel<ForgeFaultPanel>(); return; }
            if (value.Mode == MultiplayerSessionMode.ClientLive) { ForgeMultiplayerUi.Dismiss(this); return; }
            if (value.SnapshotBytesTotal > 0)
            {
                double percent = value.SnapshotBytesReceived * 100.0 / value.SnapshotBytesTotal;
                string progress = "正在下载房主城市……\n" + percent.ToString("0.0") + "%  " +
                    FormatBytes(value.SnapshotBytesReceived) + " / " + FormatBytes(value.SnapshotBytesTotal);
                if (status.text != progress) status.text = progress;
            }
            else
            {
                string progress = "正在连接并核对游戏内容……\n" + value.Detail;
                if (status.text != progress) status.text = progress;
            }
            base.Update();
        }

        private static string FormatBytes(ulong bytes)
        { return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : (bytes / 1024.0).ToString("0.0") + " KB"; }
    }

    internal sealed class ForgeLeaveConfirmPanel : ForgePanelBase
    {
        private UILabel explanation;
        private UIButton confirm;
        private UILabel feedback;

        public override void Start()
        {
            Configure("退出多人联机", 330);
            explanation = Label(string.Empty, 68);
            confirm = Button("确认断开", 145, Confirm);
            Button("返回", 198, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
            feedback = Label(string.Empty, 252);
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerSessionMode mode = RuntimeServices.Multiplayer.Status.Mode;
                bool host = mode == MultiplayerSessionMode.Hosting || mode == MultiplayerSessionMode.StartingHost;
                SetText(confirm, host ? "确认停止房间" : "确认断开");
                SetText(explanation, host
                    ? "停止后所有加入者都会断开。城市仍保留在本机，之后可以重新创建房间。"
                    : "断开后会退出当前多人会话；再次加入需要重新连接并核对房主城市。");
                if (mode == MultiplayerSessionMode.Offline) ForgeMultiplayerUi.Dismiss(this);
                else if (mode == MultiplayerSessionMode.Faulted)
                    ForgeMultiplayerUi.ReplacePanel<ForgeFaultPanel>();
            }
            base.Update();
        }

        private void Confirm()
        {
            confirm.isEnabled = false;
            if (!RuntimeServices.Multiplayer.RequestStop())
            {
                confirm.isEnabled = true;
                feedback.text = "无法提交停止请求，请稍后重试或写入诊断日志。";
                return;
            }
            UIView view = GetUIView();
            ForgeSessionPanel session = view == null ? null : view.FindUIComponent<ForgeSessionPanel>(typeof(ForgeSessionPanel).Name);
            if (session != null) session.SetNotice("正在安全停止多人会话……");
            ForgeMultiplayerUi.ReplacePanel<ForgeSessionPanel>();
        }
    }

    internal sealed class ForgeFaultPanel : ForgePanelBase
    {
        private UILabel summary;
        private UILabel diagnostic;
        private UIButton retry;
        private UILabel feedback;

        public override void Start()
        {
            Configure("CSM-Forge 联机已停止", 430);
            summary = Label(string.Empty, 62); summary.height = 100; summary.autoHeight = false;
            diagnostic = Label(string.Empty, 168); diagnostic.height = 72; diagnostic.autoHeight = false;
            retry = Button("清理失败状态并返回", 252, ResetAndReturn);
            Button("写入诊断日志", 304, WriteDiagnostics);
            Button("关闭", 356, delegate { ForgeMultiplayerUi.CloseOrBack(this); });
            feedback = Label(string.Empty, 402);
            base.Start();
        }

        public override void Update()
        {
            if (isVisible)
            {
                MultiplayerStatusSnapshot value = RuntimeServices.Multiplayer.Status;
                bool fenced = RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.WorldFenced;
                SetText(summary, fenced
                    ? "为了保护城市状态，Forge 已隔离当前世界。必须返回主菜单并重新加载城市，不能在当前城市里直接重开房间。"
                    : FaultHelp(value.Detail));
                SetText(diagnostic, "诊断代码：" + (value.Detail ?? "unknown") +
                    "\n如果问题重复出现，请写入诊断日志并运行安装目录中的 COLLECT-DIAGNOSTICS.ps1。");
                retry.isVisible = !fenced;
                retry.isEnabled = !fenced;
            }
            base.Update();
        }

        private void ResetAndReturn()
        {
            if (RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.WorldFenced) return;
            bool hasWorld = RuntimeServices.Lifecycle.Current.IsValid;
            // WP-1.2: teardown belongs on the simulation thread when a city identity exists.
            if (!RuntimeServices.Multiplayer.RequestStop()) RuntimeServices.Multiplayer.StopImmediately();
            if (hasWorld) ForgeMultiplayerUi.ReplacePanel<ForgeHostGamePanel>();
            else ForgeMultiplayerUi.OpenMainJoinWithPendingInvite();
        }

        private void WriteDiagnostics()
        {
            RuntimeDiagnostics.DumpToGameLog("multiplayer-fault-panel");
            feedback.text = "诊断已写入游戏日志。请再运行安装目录中的诊断收集脚本。";
        }

        private static string FaultHelp(string detail)
        {
            detail = detail ?? string.Empty;
            if (detail.StartsWith("compatibility-rejected:", StringComparison.Ordinal))
                return ForgeCompatibilityFailureText.Describe(detail.Substring("compatibility-rejected:".Length));
            if (detail.StartsWith("client-disconnected:", StringComparison.Ordinal))
                return "与房主的连接已断开。请确认地址、端口、防火墙和房主房间仍在运行。";
            if (detail.StartsWith("host-start:", StringComparison.Ordinal))
                return "房间创建失败。端口可能被占用，或当前 Mod/运行时检查未通过。可以清理失败状态后修改设置重试。";
            if (detail.StartsWith("snapshot-", StringComparison.Ordinal))
                return "房主城市下载或加载失败。当前城市没有被当作成功加入；可以清理状态后重新连接。";
            return "联机操作没有完成，Forge 没有把失败当作成功。可以清理失败状态后重试；若再次失败，请收集诊断。";
        }
    }
}
