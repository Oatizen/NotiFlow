using System.Collections.Generic;

namespace NotiFlow.Models
{
    /// <summary>
    /// 邮箱服务商预设信息模型。
    /// </summary>
    public class EmailProviderPreset
    {
        public string ProviderType { get; set; } = "QQ";
        public string DisplayName { get; set; } = "QQ 邮箱";
        public string ImageFileName { get; set; } = "QQ邮箱.png";
        public string DefaultHost { get; set; } = "imap.qq.com";
        public int DefaultPort { get; set; } = 993;
        public bool DefaultUseSsl { get; set; } = true;
        public string HelpGuideUrl { get; set; } = "";
        public string HelpGuideDescription { get; set; } = "";

        public static List<EmailProviderPreset> GetAllPresets()
        {
            return new List<EmailProviderPreset>
            {
                new()
                {
                    ProviderType = "QQ",
                    DisplayName = "QQ 邮箱",
                    ImageFileName = "QQ邮箱.png",
                    DefaultHost = "imap.qq.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请前往 QQ 邮箱网页端 -> 设置 -> 账户 -> 开启 POP3/IMAP 服务，发送短信获取 16 位授权码"
                },
                new()
                {
                    ProviderType = "NetEase163",
                    DisplayName = "网易 163 邮箱",
                    ImageFileName = "163邮箱.png",
                    DefaultHost = "imap.163.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请前往 163 邮箱网页端 -> 设置 -> POP3/SMTP/IMAP -> 开启服务并设置客户端授权密码"
                },
                new()
                {
                    ProviderType = "NetEase126",
                    DisplayName = "网易 126 邮箱",
                    ImageFileName = "126邮箱.png",
                    DefaultHost = "imap.126.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请前往 126 邮箱网页端 -> 设置 -> POP3/SMTP/IMAP -> 开启服务并设置客户端授权密码"
                },
                new()
                {
                    ProviderType = "Gmail",
                    DisplayName = "Gmail",
                    ImageFileName = "Gmail.png",
                    DefaultHost = "imap.gmail.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "需在 Google 账户中开启两步验证并生成应用专用密码（国内网络需配置系统代理）"
                },
                new()
                {
                    ProviderType = "Sina",
                    DisplayName = "新浪邮箱",
                    ImageFileName = "新浪邮箱.png",
                    DefaultHost = "imap.sina.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请前往新浪邮箱网页端 -> 设置 -> 账户 -> 开启 POP/IMAP 服务并生成授权码"
                },
                new()
                {
                    ProviderType = "Mobile139",
                    DisplayName = "139 邮箱",
                    ImageFileName = "139邮箱.png",
                    DefaultHost = "imap.139.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请前往 139 邮箱网页端 -> 设置 -> 账户与安全 -> 开启 IMAP 并生成客户端密码"
                },
                new()
                {
                    ProviderType = "Office365",
                    DisplayName = "Office 365",
                    ImageFileName = "Office365.png",
                    DefaultHost = "outlook.office365.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "请使用组织/学校提供的 Office 365 账号及应用专用凭据登录"
                },
                new()
                {
                    ProviderType = "Outlook",
                    DisplayName = "Outlook",
                    ImageFileName = "Outlook.png",
                    DefaultHost = "outlook.office365.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "Outlook 个人邮箱推荐开启两步验证并使用应用专用密码登录"
                },
                new()
                {
                    ProviderType = "Exchange",
                    DisplayName = "Exchange",
                    ImageFileName = "Exchange.png",
                    DefaultHost = "outlook.office365.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "支持企业自建 Microsoft Exchange 邮件服务器及托管邮箱"
                },
                new()
                {
                    ProviderType = "Custom",
                    DisplayName = "IMAP",
                    ImageFileName = "IMAP.png",
                    DefaultHost = "",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    HelpGuideDescription = "支持校园网 (.edu.cn)、企业邮局等任意标准 IMAP 服务器"
                }
            };
        }

        /// <summary>
        /// 根据服务商类别快速获取预设，若未找到则回退至通用 IMAP 预设。
        /// </summary>
        public static EmailProviderPreset GetPreset(string? providerType)
        {
            var presets = GetAllPresets();
            return presets.Find(p => string.Equals(p.ProviderType, providerType, System.StringComparison.OrdinalIgnoreCase))
                   ?? presets.Find(p => p.ProviderType == "Custom")
                   ?? presets[0];
        }
    }
}
