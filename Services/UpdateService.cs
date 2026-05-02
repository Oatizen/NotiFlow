using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using NotiFlow.Views.Windows;

namespace NotiFlow.Services
{
    /// <summary>
    /// 多源更新检查服务。
    /// 支持 GitHub、Gitee 以及自动回落（Auto）模式。
    /// </summary>
    public static class UpdateService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/Oatizen/NotiFlow/releases/latest";
        private const string GitHubReleaseUrl = "https://github.com/Oatizen/NotiFlow/releases/latest";
        private const string GiteeApiUrl = "https://gitee.com/api/v5/repos/Oatizen/NotiFlow/releases/latest";
        private const string GiteeReleaseUrl = "https://gitee.com/Oatizen/NotiFlow/releases";

        // HttpClient 复用单例（避免 socket 耗尽，遵循 .NET 最佳实践）
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NotiFlow", "1.0"));
            return client;
        }

        /// <summary>
        /// 执行更新检查。根据用户选择的更新源发起请求，自动模式下先尝试 GitHub 再回落 Gitee。
        /// </summary>
        /// <param name="isManualCheck">是否为用户手动点击检查更新，若是则无论如何都给弹窗提示</param>
        public static async Task CheckForUpdatesAsync(bool isManualCheck = false)
        {
            string source = BarrageSettings.UpdateSource; // "Auto", "GitHub", "Gitee"
            (bool Success, bool HasUpdate, string Version, string Notes, string Error) result = (false, false, "", "", "");

            if (source == "GitHub" || source == "Auto")
            {
                result = await TryCheckSource(GitHubApiUrl, "GitHub");
            }

            if ((!result.Success) && (source == "Gitee" || source == "Auto"))
            {
                var giteeResult = await TryCheckSource(GiteeApiUrl, "Gitee");
                if (source == "Auto")
                {
                    if (giteeResult.Success)
                    {
                        result = giteeResult;
                    }
                    else
                    {
                        result.Error = result.Error + "\n" + giteeResult.Error;
                    }
                }
                else
                {
                    result = giteeResult;
                }
            }

            if (result.Success && result.HasUpdate)
            {
                ShowUpdateDialog($"发现新版本 {result.Version} !", $"更新说明：\n{result.Notes}\n\n是否立即前往仓库下载更新？");
            }
            else if (isManualCheck)
            {
                if (result.Success && !result.HasUpdate)
                {
                    ShowUpdateDialog("已是最新版", $"当前版本 ({result.Version}) 已经是最新版本。");
                }
                else if (!result.Success)
                {
                    ShowUpdateDialog("检查失败", $"{result.Error}\n请稍后再试，或直接点击下方按钮前往仓库查看。");
                }
            }
        }

        /// <summary>
        /// 尝试从指定的 API 源获取最新版本信息。
        /// </summary>
        private static async Task<(bool Success, bool HasUpdate, string Version, string Notes, string Error)> TryCheckSource(string apiUrl, string sourceName)
        {
            try
            {
                var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return (false, false, "", "", $"访问过于频繁，触发了 {sourceName} API 限制。");
                    }
                    return (false, false, "", "", $"无法连接到 {sourceName} 服务器，状态码: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                
                string tag_name = root.GetProperty("tag_name").GetString() ?? "";
                string body = root.GetProperty("body").GetString() ?? "无详细说明";
                
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string currentVersionStr = $"v{currentVersion?.Major}.{currentVersion?.Minor}.{currentVersion?.Build}";

                string latestVersionStr = tag_name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag_name.Substring(1) : tag_name;
                string currentVersionTrimmed = currentVersionStr.Substring(1);

                if (Version.TryParse(latestVersionStr, out Version? latestVersion) && 
                    Version.TryParse(currentVersionTrimmed, out Version? currentVer))
                {
                    if (latestVersion > currentVer) return (true, true, tag_name, body, "");
                    else return (true, false, currentVersionStr, "", "");
                }
                else
                {
                    if (tag_name != currentVersionStr) return (true, true, tag_name, body, "");
                    else return (true, false, currentVersionStr, "", "");
                }
            }
            catch (Exception ex)
            {
                return (false, false, "", "", $"{sourceName} 请求发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 弹出自定义的更新提示窗口，并根据用户点击的按钮执行相应的跳转。
        /// 使用自定义的 UpdateDialogWindow 替代 WPF-UI 内置 MessageBox，
        /// 解决了上下异色、按钮文字截断、以及关闭按钮逻辑冲突等问题。
        /// </summary>
        private static void ShowUpdateDialog(string title, string content)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new UpdateDialogWindow(title, content)
                {
                    Owner = Application.Current.MainWindow
                };

                dialog.ShowDialog();

                switch (dialog.UserResult)
                {
                    case UpdateDialogResult.GitHub:
                        OpenUrl(GitHubReleaseUrl);
                        break;
                    case UpdateDialogResult.Gitee:
                        OpenUrl(GiteeReleaseUrl);
                        break;
                    case UpdateDialogResult.Close:
                    default:
                        // 用户点击了"确定"或右上角的 X，不执行任何跳转
                        break;
                }
            });
        }

        /// <summary>
        /// 使用系统默认浏览器打开指定 URL。
        /// </summary>
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法打开链接 {url}: {ex.Message}");
            }
        }
    }
}
