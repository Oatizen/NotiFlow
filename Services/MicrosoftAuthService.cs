using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using NotiFlow.Helpers;

namespace NotiFlow.Services
{
    /// <summary>
    /// 微软现代身份验证（OAuth 2.0 / MSAL）管理服务。
    /// 负责唤起微软官方登录窗口、获取访问令牌（Access Token）、静默刷新 Token 及持久化 Token 缓存。
    /// </summary>
    public static class MicrosoftAuthService
    {
        /// <summary>
        /// 微软官方公开的桌面邮件客户端公共 Application (client) ID。
        /// 采用 RFC 7636 (PKCE) 桌面授权模式，不包含任何私钥或秘密，安全合规且免配置。
        /// </summary>
        public const string ClientId = "9e5f94bc-e8a4-4e73-b8be-63364c29d753";

        /// <summary>
        /// IMAP 访问与个人用户信息读取所需权限作用域。
        /// </summary>
        public static readonly string[] Scopes = new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All",
            "offline_access",
            "User.Read"
        };

        private static IPublicClientApplication? _pca;
        private static readonly object _lock = new();

        private static string CacheFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NotiFlow",
            "msal_cache.dat");

        /// <summary>
        /// 获取或初始化公共客户端应用单例（配置 DPAPI 令牌缓存持久化）。
        /// </summary>
        public static IPublicClientApplication GetClient()
        {
            if (_pca != null) return _pca;

            lock (_lock)
            {
                if (_pca != null) return _pca;

                _pca = PublicClientApplicationBuilder.Create(ClientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, "common")
                    .WithRedirectUri("http://localhost")
                    .Build();

                BindTokenCache(_pca.UserTokenCache);
                return _pca;
            }
        }

        private static void BindTokenCache(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccess(args =>
            {
                lock (_lock)
                {
                    try
                    {
                        if (File.Exists(CacheFilePath))
                        {
                            byte[] encrypted = File.ReadAllBytes(CacheFilePath);
                            byte[] decrypted = SecurityHelper.DecryptBytes(encrypted);
                            if (decrypted.Length > 0)
                            {
                                args.TokenCache.DeserializeMsalV3(decrypted);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 读取 Token 缓存失败: {ex.Message}");
                    }
                }
            });

            tokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    lock (_lock)
                    {
                        try
                        {
                            byte[] data = args.TokenCache.SerializeMsalV3();
                            byte[] encrypted = SecurityHelper.EncryptBytes(data);
                            string? dir = Path.GetDirectoryName(CacheFilePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.WriteAllBytes(CacheFilePath, encrypted);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 保存 Token 缓存失败: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 唤起系统浏览器 / 微软官方登录窗口进行交互式 OAuth2 登录授权。
        /// </summary>
        public static async Task<AuthenticationResult?> SignInInteractiveAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var app = GetClient();
                var result = await app.AcquireTokenInteractive(Scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(cancellationToken);

                return result;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[MicrosoftAuth] 登录操作已取消");
                return null;
            }
            catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
            {
                System.Diagnostics.Debug.WriteLine("[MicrosoftAuth] 用户取消了登录授权");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 交互登录失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 静默获取指定账号的有效 Access Token（过期自动使用 Refresh Token 刷新）。
        /// </summary>
        public static async Task<string?> GetAccessTokenAsync(string accountId, string? emailHint = null)
        {
            try
            {
                var app = GetClient();
                IAccount? account = null;

                if (!string.IsNullOrWhiteSpace(accountId))
                {
                    account = await app.GetAccountAsync(accountId);
                }

                if (account == null && !string.IsNullOrWhiteSpace(emailHint))
                {
                    var accounts = await app.GetAccountsAsync();
                    account = accounts.FirstOrDefault(a => string.Equals(a.Username, emailHint, StringComparison.OrdinalIgnoreCase));
                }

                if (account == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 未找到本地账号缓存: {accountId} / {emailHint}");
                    return null;
                }

                var result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync();
                return result.AccessToken;
            }
            catch (MsalUiRequiredException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] Token 过期需重新交互登录: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 静默获取 Token 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 移除指定账号的本地令牌缓存。
        /// </summary>
        public static async Task RemoveAccountAsync(string accountId, string? emailHint = null)
        {
            try
            {
                var app = GetClient();
                IAccount? account = null;

                if (!string.IsNullOrWhiteSpace(accountId))
                {
                    account = await app.GetAccountAsync(accountId);
                }

                if (account == null && !string.IsNullOrWhiteSpace(emailHint))
                {
                    var accounts = await app.GetAccountsAsync();
                    account = accounts.FirstOrDefault(a => string.Equals(a.Username, emailHint, StringComparison.OrdinalIgnoreCase));
                }

                if (account != null)
                {
                    await app.RemoveAsync(account);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] 移除账号缓存异常: {ex.Message}");
            }
        }
    }
}
