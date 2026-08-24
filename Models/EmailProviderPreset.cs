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
        public string AuthType { get; set; } = "Basic";
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
                    AuthType = "Basic",
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
                    AuthType = "Basic",
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
                    AuthType = "Basic",
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
                    AuthType = "Basic",
                    HelpGuideDescription = "请在 Google 账号中开启「两步验证」，并在安全性设置中生成 16 位「应用专用密码」填入下方（国内网络请配置系统代理）"
                },
                new()
                {
                    ProviderType = "Sina",
                    DisplayName = "新浪邮箱",
                    ImageFileName = "新浪邮箱.png",
                    DefaultHost = "imap.sina.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    AuthType = "Basic",
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
                    AuthType = "Basic",
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
                    AuthType = "OAuth2",
                    HelpGuideDescription = "微软现代身份验证：点击下方按钮唤起微软官方登录窗口，完成授权即可自动绑定"
                },
                new()
                {
                    ProviderType = "Outlook",
                    DisplayName = "Outlook",
                    ImageFileName = "Outlook.png",
                    DefaultHost = "outlook.office365.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    AuthType = "OAuth2",
                    HelpGuideDescription = "微软现代身份验证：点击下方按钮唤起微软官方登录窗口，完成授权即可自动绑定"
                },
                new()
                {
                    ProviderType = "Exchange",
                    DisplayName = "Exchange",
                    ImageFileName = "Exchange.png",
                    DefaultHost = "outlook.office365.com",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    AuthType = "OAuth2",
                    HelpGuideDescription = "微软现代身份验证：点击下方按钮唤起微软官方登录窗口，完成授权即可自动绑定"
                },
                new()
                {
                    ProviderType = "Custom",
                    DisplayName = "IMAP",
                    ImageFileName = "IMAP.png",
                    DefaultHost = "",
                    DefaultPort = 993,
                    DefaultUseSsl = true,
                    AuthType = "Basic",
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
