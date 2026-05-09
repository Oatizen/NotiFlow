using System;
using System.Linq;

namespace NotiFlow.Services
{
    /// <summary>
    /// 作用域过滤判定器。
    /// 集中管理「通知来源」和「生效场景」两个维度的过滤逻辑，
    /// 供 NotificationService 和 ForegroundMonitorService 调用。
    /// </summary>
    public static class ScopeFilter
    {
        /// <summary>
        /// 判断指定来源的通知是否应该被接受（显示为弹幕）。
        /// 在 NotificationService 的轮询循环中，于 ParseNotificationAsync 之前调用，
        /// 以避免为被过滤的通知执行昂贵的图标解析。
        /// </summary>
        /// <param name="aumid">通知来源应用的 AUMID（Application User Model ID）</param>
        /// <param name="appName">通知来源应用的显示名称</param>
        /// <returns>true = 通过过滤，应显示；false = 被过滤，应丢弃</returns>
        public static bool ShouldAcceptSource(string aumid, string appName)
        {
            var mode = BarrageSettings.SourceFilterMode;
            if (mode == "Disabled") return true;

            var list = BarrageSettings.SourceFilterList;
            if (list == null || list.Count == 0)
            {
                // 列表为空时：白名单模式 = 全部拒绝；黑名单模式 = 全部接受
                return mode != "Whitelist";
            }

            // 匹配规则：Identifier 或 DisplayName 任一命中即视为匹配
            bool inList = list.Any(rule =>
                (!string.IsNullOrEmpty(rule.Identifier) &&
                 rule.Identifier.Equals(aumid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(rule.DisplayName) &&
                 rule.DisplayName.Equals(appName, StringComparison.OrdinalIgnoreCase)));

            return mode == "Whitelist" ? inList : !inList;
        }

        /// <summary>
        /// 判断指定进程名是否应该允许弹幕显示。
        /// 由 ForegroundMonitorService 在每次检测到前台窗口切换时调用。
        /// </summary>
        /// <param name="processName">前台窗口所属进程的可执行文件名（如 "powerpnt.exe"）</param>
        /// <returns>true = 允许显示弹幕；false = 应抑制弹幕</returns>
        public static bool ShouldAcceptScene(string processName)
        {
            var mode = BarrageSettings.SceneFilterMode;
            if (mode == "Disabled") return true;

            var list = BarrageSettings.SceneFilterList;
            if (list == null || list.Count == 0)
            {
                return mode != "Whitelist";
            }

            bool inList = list.Any(rule =>
                !string.IsNullOrEmpty(rule.Identifier) &&
                rule.Identifier.Equals(processName, StringComparison.OrdinalIgnoreCase));

            return mode == "Whitelist" ? inList : !inList;
        }
    }
}
