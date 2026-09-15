using System;
using System.Net;
using ColossalFramework.UI;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public static class ForgeSettingsPanel
    {
        private static string roomKey = "forge-dev";

        public static void Build(UIHelperBase helper, ForgeSettings settings)
        {
            if (helper == null || settings == null) return;
            UIHelperBase group = helper.AddGroup("CSM-Forge V3 开发直连");
            group.AddLabel("当前网络层为 LiteNetLib + 临时房间口令，仅用于开发/LAN 验证，不代表最终认证传输。Client 目前需先进入任意城市以启动 CS1 Runtime；Join 后会下载、校验并加载 Host 快照，当前本地城市会被替换，请先自行保存。当前正式权威域仅开放 Water 日/夜预算。其他持久预算写入会被阻止，而不是偷偷本地执行。");

            UITextField name = (UITextField)group.AddTextfield("显示名", settings.DisplayName.value,
                delegate(string text) { }, delegate(string text)
                {
                    if (!string.IsNullOrEmpty(text) && text.Length <= 32) settings.DisplayName.value = text;
                });
            name.width = 360;

            UITextField address = (UITextField)group.AddTextfield("主机 IPv4", settings.HostAddress.value,
                delegate(string text) { }, delegate(string text)
                {
                    IPAddress parsed;
                    if (IPAddress.TryParse(text, out parsed)) settings.HostAddress.value = text;
                });
            address.width = 360;

            UITextField port = (UITextField)group.AddTextfield("UDP 端口", settings.Port.value.ToString(),
                delegate(string text) { }, delegate(string text)
                {
                    int value;
                    if (int.TryParse(text, out value) && value > 0 && value <= 65535) settings.Port.value = value;
                });
            port.numericalOnly = true;

            UITextField key = (UITextField)group.AddTextfield("临时房间口令（不持久化）", roomKey,
                delegate(string text) { roomKey = text; }, delegate(string text) { roomKey = text; });
            key.width = 360;

            UILabel status = (UILabel)group.AddLabel(StatusText());
            group.AddButton("Host 当前存档", delegate
            {
                bool ok = RuntimeServices.Multiplayer.RequestHost(settings.Port.value, roomKey, settings.DisplayName.value);
                status.text = (ok ? "已提交 Host 请求。" : "Host 请求失败：请确认已进入城市且当前没有 Forge 会话。") + "\n" + StatusText();
            });
            group.AddButton("Join Host 快照", delegate
            {
                IPAddress ip;
                bool ok = IPAddress.TryParse(settings.HostAddress.value, out ip) &&
                    RuntimeServices.Multiplayer.RequestJoinCurrentWorld(new IPEndPoint(ip, settings.Port.value), roomKey, settings.DisplayName.value);
                status.text = (ok ? "已提交 Join 请求；若兼容检查通过，将自动下载并加载 Host 快照。" :
                    "Join 请求失败：仅支持有效 IPv4，且当前需先进入任意城市启动 Runtime。") + "\n" + StatusText();
            });
            group.AddButton("停止 Forge 会话", delegate
            {
                RuntimeServices.Multiplayer.RequestStop();
                status.text = "已提交停止请求。\n" + StatusText();
            });
            group.AddButton("刷新状态", delegate { status.text = StatusText(); });
        }

        private static string StatusText()
        {
            MultiplayerStatusSnapshot status = RuntimeServices.Multiplayer.Status;
            return "状态: " + status.Mode + " | revision=" + status.Revision +
                   " | peers=" + status.ConnectedPeers + " | " + status.Detail;
        }
    }
}
