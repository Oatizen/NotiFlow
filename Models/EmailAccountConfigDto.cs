using System;
using System.Text.Json.Serialization;
using NotiFlow.Helpers;

namespace NotiFlow.Models
{
    /// <summary>
    /// 单个邮箱账号的持久化配置与连接参数 DTO。
    /// </summary>
    public class EmailAccountConfigDto
    {
        /// <summary>
        /// 账号唯一标识 GUID。
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 邮箱服务商类别（如 QQ, NetEase163, NetEase126, Sina, Mobile139, Gmail, Outlook, Custom）。
        /// </summary>
        public string ProviderType { get; set; } = "QQ";

        /// <summary>
        /// 用户设置的邮箱自定义备注名称（如 "工作邮箱"、"个人备用"）。
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 邮箱账号地址（如 user@qq.com）。
        /// </summary>
        public string EmailAddress { get; set; } = "";

        /// <summary>
        /// IMAP 邮件服务器主机名（如 imap.qq.com）。
        /// </summary>
        public string ServerHost { get; set; } = "imap.qq.com";

        /// <summary>
        /// IMAP 邮件服务器端口（默认 SSL 993）。
        /// </summary>
        public int ServerPort { get; set; } = 993;

        /// <summary>
        /// 是否使用 SSL/TLS 加密传输。
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// 认证模式（"Basic": 邮箱地址+授权码/密码; "OAuth2": 微软现代身份验证 OAuth 2.0）。
        /// </summary>
        public string AuthType { get; set; } = "Basic";

        /// <summary>
        /// 当使用 OAuth 2.0 时的 MSAL 账户唯一标识符（HomeAccountId）。
        /// </summary>
        public string OAuthAccountId { get; set; } = "";

        /// <summary>
        /// 该邮箱是否启用监听。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 使用 Windows DPAPI 加密后的授权码 Base64 密文（绝文明文落盘）。
        /// </summary>
        public string EncryptedAuthCode { get; set; } = "";

        /// <summary>
        /// 解密获取运行时的明文授权码（仅供网络认证握手使用）。
        /// </summary>
        /// <returns>明文授权码</returns>
        [JsonIgnore]
        public string AuthCode
        {
            get => SecurityHelper.DecryptString(EncryptedAuthCode);
            set => EncryptedAuthCode = SecurityHelper.EncryptString(value);
        }

        /// <summary>
        /// 该邮箱账号独立的弹幕样式覆盖（为 null 时跟随全局通用样式）。
        /// </summary>
        public BarrageConfigDto? StyleOverride { get; set; }
    }
}
