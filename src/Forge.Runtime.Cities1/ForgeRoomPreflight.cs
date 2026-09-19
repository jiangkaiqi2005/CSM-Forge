using System;
using System.Text;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class ForgeRoomPreflightReport
    {
        public readonly bool CanHost;
        public readonly string Message;

        public ForgeRoomPreflightReport(bool canHost, string message)
        {
            CanHost = canHost;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Player-facing advisory preflight. RequestHost remains the authoritative check and recollects
    /// the manifest on the simulation thread; this module only explains known blockers before submit.
    /// </summary>
    internal static class ForgeRoomPreflight
    {
        public static ForgeRoomPreflightReport EvaluateHost()
        {
            if (!RuntimeServices.Patches.Installed)
            {
                string failure = RuntimeServices.Patches.FailureDetail;
                return Blocked(string.IsNullOrEmpty(failure)
                    ? "CitiesHarmony 已加载；Forge 补丁仍在等待初始化。"
                    : "Forge 补丁安装失败：" + failure + "。详细异常已写入游戏日志。");
            }
            if (!RuntimeServices.Lifecycle.Current.IsValid)
                return Blocked("当前没有已载入的城市。请先进入要作为房主的城市。");
            if (RuntimeServices.Lifecycle.Role == CitiesRuntimeRole.WorldFenced)
                return Blocked("当前城市已被安全隔离，必须返回主菜单并重新加载城市。");
            if (RuntimeServices.Lifecycle.Role != CitiesRuntimeRole.SinglePlayer)
                return Blocked("当前城市不处于可创建房间的单人状态。");
            if (RuntimeServices.Multiplayer.Status.Mode != MultiplayerSessionMode.Offline)
                return Blocked("已有 Forge 会话正在启动或运行。");

            try
            {
                CompatibilityCapabilityReport report =
                    CompatibilityCapabilityReport.From(CitiesCompatibilityCollector.Collect());
                if (report.BlockedIds.Length != 0)
                    return Blocked("已启用不支持联机的 Mod：" + FriendlyIds(report.BlockedIds) +
                        "。请退出游戏、禁用后重新启动。");

                return new ForgeRoomPreflightReport(true,
                    "开房检查通过：" + report.SynchronizedMods + " 个 Forge 同步 Mod，" +
                    report.ExactMods + " 个来自已启用插件、需客户端完全一致的 Mod 组件，" + report.Assets +
                    " 个需客户端具备的资产。创建后仍会逐个核对加入者清单。");
            }
            catch (Exception error)
            {
                // S2: surface the full cause chain (e.g. the aggregated Game Anarchy option list)
                // instead of only the exception type name — the player must be able to act on it.
                return Blocked("无法读取当前 DLC/Mod/资产清单：" + DescribeCause(error) +
                    "。请按原因处理后重试；详细上下文见游戏日志。");
            }
        }

        private static ForgeRoomPreflightReport Blocked(string message)
        { return new ForgeRoomPreflightReport(false, "无法创建房间：" + message); }

        /// <summary>Flattens the exception chain into one bounded, player-readable line.</summary>
        private static string DescribeCause(Exception error)
        {
            StringBuilder value = new StringBuilder();
            for (Exception current = error; current != null && value.Length < 600; current = current.InnerException)
            {
                if (value.Length != 0) value.Append(" < ");
                value.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return value.ToString();
        }

        private static string FriendlyIds(string[] ids)
        {
            StringBuilder value = new StringBuilder();
            int count = Math.Min(ids.Length, 3);
            for (int i = 0; i < count; i++)
            {
                if (i != 0) value.Append("、");
                string id = ids[i] ?? string.Empty;
                const string prefix = "blocked-mod:";
                value.Append(id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id);
            }
            if (ids.Length > count) value.Append(" 等 ").Append(ids.Length).Append(" 项");
            return value.ToString();
        }
    }
}
