using System;

namespace NotiFlow.Models
{
    /// <summary>
    /// 邮件弹幕内容展示偏好设置 DTO。
    /// 控制邮件弹幕中各原子模块的显隐（除邮件主题始终显示外，其余均可自由配置）。
    /// </summary>
    public class EmailDisplaySettingsDto
    {
        /// <summary>
        /// 是否在弹幕前缀展示邮箱品牌/协议图标。
        /// </summary>
        public bool ShowEmailIcon { get; set; } = true;

        /// <summary>
        /// 是否展示收件邮箱的自定义名称备注（如 [工作邮箱]）。
        /// </summary>
        public bool ShowReceiverName { get; set; } = true;

        /// <summary>
        /// 是否展示收件邮箱的完整地址（如 [user@163.com]）。
        /// </summary>
        public bool ShowReceiverAddress { get; set; } = false;

        /// <summary>
        /// 是否展示发件人的名称（如 Microsoft / 张三）。
        /// </summary>
        public bool ShowSenderName { get; set; } = true;

        /// <summary>
        /// 是否展示发件人的原生邮箱地址（如 &lt;msa@communication.microsoft.com&gt;）。
        /// </summary>
        public bool ShowSenderAddress { get; set; } = false;
    }
}
