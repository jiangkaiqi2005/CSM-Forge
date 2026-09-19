using System;
using System.Text;

namespace CsmForge.Runtime.Cities1
{
    internal static class ForgeCompatibilityFailureText
    {
        public static string Describe(string reason)
        {
            string[] errors = (reason ?? string.Empty).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (errors.Length == 0)
                return "无法加入：房主拒绝了当前游戏内容清单。请让双方重新运行安装校验。";

            StringBuilder value = new StringBuilder("无法加入，双方游戏内容不一致：");
            int count = Math.Min(errors.Length, 3);
            for (int i = 0; i < count; i++) value.Append('\n').Append("• ").Append(DescribeOne(errors[i]));
            if (errors.Length > count) value.Append('\n').Append("• 另有 ").Append(errors.Length - count).Append(" 项不一致");
            value.Append("\n请让房主和加入者核对安装校验、DLC、Mod、资产及共享设置。");
            return value.ToString();
        }

        private static string DescribeOne(string error)
        {
            if (error == "game-build-mismatch") return "Cities: Skylines 游戏版本不同";
            if (error == "schema-mismatch") return "CSM-Forge 版本或协议不同";
            if (Prefix(error, "missing:")) return "加入者缺少 " + Component(error.Substring(8));
            if (Prefix(error, "fingerprint-mismatch:")) return Component(error.Substring(21)) + " 的版本或共享设置不同";
            if (Prefix(error, "unsupported-extra:")) return "加入者多启用了 " + Component(error.Substring(18));
            if (Prefix(error, "client-forbidden:")) return "加入者启用了仅允许房主使用的 " + Component(error.Substring(17));
            if (Prefix(error, "host-unsupported:")) return "房主启用了联机暂不支持的 " + Component(error.Substring(17));
            return "不兼容项 " + Safe(error);
        }

        private static string Component(string id)
        {
            string[] parts = (id ?? string.Empty).Split(':');
            if (parts.Length >= 3 && (parts[0] == "mod" || parts[0] == "sync-mod" ||
                parts[0] == "client-mod" || parts[0] == "dependency-mod" || parts[0] == "blocked-mod"))
                return "Mod “" + Safe(parts[2]) + "”" + Workshop(parts[1]);
            if (parts.Length >= 3 && parts[0] == "asset")
                return "资产 “" + Safe(parts[2]) + "”" + Workshop(parts[1]);
            if (parts.Length >= 3 && parts[0] == "dlc")
                return "DLC（" + Safe(parts[1]) + " #" + Safe(parts[2]) + "）";
            if (parts.Length >= 2 && parts[0] == "adapter") return "Forge 同步组件 “" + Safe(parts[1]) + "”";
            return "组件 “" + Safe(id) + "”";
        }

        private static string Workshop(string id)
        { return string.IsNullOrEmpty(id) || id == "local" ? string.Empty : "（Workshop " + Safe(id) + "）"; }

        private static bool Prefix(string value, string prefix)
        { return value != null && value.StartsWith(prefix, StringComparison.Ordinal); }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < value.Length && result.Length < 72; i++)
            {
                char ch = value[i];
                if (ch >= 32 && ch != '\u007f') result.Append(ch);
            }
            return result.Length == 0 ? "unknown" : result.ToString();
        }
    }
}
