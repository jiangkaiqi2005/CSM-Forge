using ColossalFramework.UI;
using ICities;

namespace CsmForge.Runtime.Cities1
{
    public static class ForgeSettingsPanel
    {
        public static void Build(UIHelperBase helper, ForgeSettings settings)
        {
            if (helper == null || settings == null) return;
            UIHelperBase group = helper.AddGroup("CSM-Forge 设置与诊断");
            UITextField notice = (UITextField)group.AddTextfield("先看这里",
                "联机大厅不在设置页：加入游戏请在主菜单点击“FORGE 联机”；创建/管理房间请进入城市后按 Esc，在暂停菜单点击“FORGE 多人联机”。加入会下载并加载房主快照，请先备份城市。真实双机玩法仍未完成验证。",
                delegate(string text) { }, delegate(string text) { });
            notice.readOnly = true;
            notice.width = 700;

            UITextField scope = (UITextField)group.AddTextfield("当前范围",
                "已支持：道路、建筑、分区、行政区/政策、税率/预算/现金/贷款、区域解锁、暂停/速度、交通线路、命名、需求、天气、Stable-ID Tree/Prop，以及 Host Terrain brush/undo 的 absolute height shard 投影。上述路径仍待真实多机验证；Client 不能直接运行 Terrain 工具。",
                delegate(string text) { }, delegate(string text) { });
            scope.readOnly = true;
            scope.width = 700;

            UITextField name = (UITextField)group.AddTextfield("显示名", settings.DisplayName.value,
                delegate(string text) { }, delegate(string text)
                {
                    if (!string.IsNullOrEmpty(text) && text.Length <= 32) settings.DisplayName.value = text;
                });
            name.width = 360;

            UITextField status = (UITextField)group.AddTextfield("状态", StatusText(),
                delegate(string text) { }, delegate(string text) { });
            status.readOnly = true;
            status.width = 700;

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
