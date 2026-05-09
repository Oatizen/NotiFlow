namespace NotiFlow.Models
{
    /// <summary>
    /// 作用域规则条目，用于序列化持久化和 UI 展示。
    /// 同时服务于「通知来源」和「生效场景」两个维度的过滤列表。
    /// </summary>
    public class ScopeRuleItemDto
    {
        /// <summary>
        /// 友好显示名称（如 "微信"、"PowerPoint"）。
        /// 主要供 UI 列表展示使用，不参与匹配判定。
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 唯一标识符，用于实际的过滤匹配。
        /// 生效场景维度：进程可执行文件名（如 "powerpnt.exe"）
        /// 通知来源维度：应用的 AUMID 或 AppName（如 "Microsoft.Windows.Defender_xxx"）
        /// </summary>
        public string Identifier { get; set; } = "";
    }
}
