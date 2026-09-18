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
            UIHelperBase group = helper.AddGroup("CSM-Forge V3 最小可玩 Alpha");
            UITextField notice = (UITextField)group.AddTextfield("说明",
                "开发/LAN 1.0 候选。Join 会下载并加载 Host 快照，请先备份城市。已支持：道路、建筑、分区、行政区/政策、税率/预算/现金/贷款、区域解锁、暂停/速度、交通线路、命名、需求、天气、Stable-ID Tree/Prop，以及 Host Terrain brush/undo 的 absolute height shard 投影。上述路径仍待真实多机验证；Client 不能直接运行 Terrain 工具。",
                delegate(string text) { }, delegate(string text) { });
            notice.readOnly = true;
            notice.width = 700;

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

            UITextField status = (UITextField)group.AddTextfield("状态", StatusText(),
                delegate(string text) { }, delegate(string text) { });
            status.readOnly = true;
            status.width = 700;

            group.AddButton("Host 当前存档", delegate
            {
                bool ok = RuntimeReadyForStart() &&
                    RuntimeServices.Multiplayer.RequestHost(settings.Port.value, roomKey, settings.DisplayName.value);
                status.text = (ok ? "已提交 Host 请求。" :
                    "Host 请求失败：请确认已进入城市、CitiesHarmony/Forge patches 已就绪且当前没有 Forge 会话。") + " " + StatusText();
            });
            group.AddButton("Join Host 快照", delegate
            {
                IPAddress ip;
                bool ok = RuntimeReadyForStart() && IPAddress.TryParse(settings.HostAddress.value, out ip) &&
                    RuntimeServices.Multiplayer.RequestJoinCurrentWorld(new IPEndPoint(ip, settings.Port.value), roomKey, settings.DisplayName.value);
                status.text = (ok ? "已提交 Join 请求；兼容检查通过后会自动下载并加载 Host 快照。" :
                    "Join 请求失败：需已进入城市、patches 已就绪、IPv4 有效且当前没有 Forge 会话。") + " " + StatusText();
            });
            group.AddButton("停止 Forge 会话", delegate
            {
                RuntimeServices.Multiplayer.RequestStop();
                status.text = "已提交停止请求。 " + StatusText();
            });
            group.AddButton("刷新状态", delegate { status.text = StatusText(); });
            group.AddButton("写入诊断日志", delegate
            {
                RuntimeDiagnostics.DumpToGameLog("settings-button");
                status.text = "诊断快照已写入游戏日志；若测试失败，请随后运行包内 COLLECT-ALPHA-DIAGNOSTICS.ps1。 " + StatusText();
            });
        }

        private static bool RuntimeReadyForStart()
        {
            return RuntimeServices.Patches.Installed && RuntimeServices.Lifecycle.Current.IsValid &&
                RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.SinglePlayer;
        }

        private static string StatusText()
        {
            MultiplayerStatusSnapshot status = RuntimeServices.Multiplayer.Status;
            return "状态: " + status.Mode + " | revision=" + status.Revision +
                   " | peers=" + status.ConnectedPeers +
                   " | role=" + RuntimeServices.Lifecycle.Role +
                   " | patches=" + (RuntimeServices.Patches.Installed ? "ready" : "not-ready") +
                   " | " + status.Detail;
        }
    }
}
