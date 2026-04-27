using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace NotiFlow.Services
{
    public static class UpdateService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/Oatizen/NotiFlow/releases/latest";
        private const string GitHubReleaseUrl = "https://github.com/Oatizen/NotiFlow/releases/latest";

        /// <summary>
        /// 检测更新
        /// </summary>
        /// <param name="isManualCheck">是否为用户手动点击检查更新，若是则无论如何都给弹窗提示</param>
        public static async Task CheckForUpdatesAsync(bool isManualCheck = false)
        {
            try
            {
                using var client = new HttpClient();
                // GitHub API 要求必须设置 User-Agent
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NotiFlow", "1.0"));

                var response = await client.GetAsync(GitHubApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    if (isManualCheck)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            ShowMessage("检查失败", "访问过于频繁，触发了 GitHub API 限制。\n请稍后再试，或直接点击左侧前往 GitHub 仓库查看。");
                        }
                        else
                        {
                            ShowMessage("检查失败", $"无法连接到服务器，状态码: {response.StatusCode}");
                        }
                    }
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                
                string tag_name = root.GetProperty("tag_name").GetString() ?? "";
                string body = root.GetProperty("body").GetString() ?? "无详细说明";
                
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string currentVersionStr = $"v{currentVersion?.Major}.{currentVersion?.Minor}.{currentVersion?.Build}";

                // 简单的版本号比较，如果是以 v 开头，去掉 v
                string latestVersionStr = tag_name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag_name.Substring(1) : tag_name;
                string currentVersionTrimmed = currentVersionStr.Substring(1);

                if (Version.TryParse(latestVersionStr, out Version? latestVersion) && 
                    Version.TryParse(currentVersionTrimmed, out Version? currentVer))
                {
                    if (latestVersion > currentVer)
                    {
                        // 发现新版本
                        ShowUpdateDialog(tag_name, body);
                    }
                    else
                    {
                        if (isManualCheck) ShowMessage("已是最新版", $"当前版本 ({currentVersionStr}) 已经是最新版本。");
                    }
                }
                else
                {
                    // 解析失败时，如果名字不一样，且是手动检测，也可以提示去看看
                    if (tag_name != currentVersionStr && isManualCheck)
                    {
                         ShowUpdateDialog(tag_name, body);
                    }
                    else if (isManualCheck)
                    {
                         ShowMessage("已是最新版", $"当前版本 ({currentVersionStr}) 已经是最新版本。");
                    }
                }
            }
            catch (Exception ex)
            {
                if (isManualCheck) ShowMessage("检查失败", $"网络请求发生异常: {ex.Message}");
            }
        }

        private static void ShowMessage(string title, string content)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                var mb = new Wpf.Ui.Controls.MessageBox
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "确定"
                };
                ForceGreyBackground(mb);
                await mb.ShowDialogAsync();
            });
        }

        private static void ShowUpdateDialog(string newVersion, string releaseNotes)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                var mb = new Wpf.Ui.Controls.MessageBox
                {
                    Title = $"发现新版本 {newVersion} !",
                    Content = $"更新说明：\n{releaseNotes}\n\n是否立即前往 GitHub 下载更新？",
                    PrimaryButtonText = "前往下载",
                    CloseButtonText = "稍后再说"
                };

                ForceGreyBackground(mb);
                
                var result = await mb.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    OpenUrl(GitHubReleaseUrl);
                }
            });
        }

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

        private static void ForceGreyBackground(Wpf.Ui.Controls.MessageBox mb)
        {
            var accentBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SecondaryBackgroundFillColorDefaultBrush"];
            mb.Background = accentBrush;
            mb.Resources["ApplicationBackgroundBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorTertiaryBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorQuarternaryBrush"] = accentBrush;
            mb.Resources["ControlFillColorDefaultBrush"] = accentBrush;

            mb.Loaded += (s, e) =>
            {
                ForceUniformGrey(mb, accentBrush);
            };
        }

        private static void ForceUniformGrey(System.Windows.DependencyObject parent, System.Windows.Media.Brush brush)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.Border border && border.TemplatedParent is Wpf.Ui.Controls.MessageBox)
                {
                    border.Background = System.Windows.Media.Brushes.Transparent;
                }
                else if (child is System.Windows.Controls.Grid grid && grid.TemplatedParent is Wpf.Ui.Controls.MessageBox)
                {
                    grid.Background = System.Windows.Media.Brushes.Transparent;
                }
                ForceUniformGrey(child, brush);
            }
        }
    }
}
