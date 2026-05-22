using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications.Management;
using Windows.UI.Notifications;
using NotiFlow.Models;
using System.IO;

namespace NotiFlow.Services
{
    public class NotificationService : IDisposable
    {
        public static NotificationService? Instance { get; private set; }

        public NotificationService()
        {
            Instance = this;
        }
        private UserNotificationListener? _listener;
        private readonly HashSet<uint> _knownNotificationIds = new();
        private CancellationTokenSource? _cts;

        /// <summary>
        /// 启动以来及历史缓存中曾发送过通知的应用列表（去重，最多 20 个，最新的在前）。
        /// 后续 ScopePage 可直接绑定此列表供用户快速添加来源规则。
        /// </summary>
        public ObservableCollection<ScopeRuleItemDto> RecentSources { get; } = new(BarrageSettings.RecentSourcesCache);
        private const int MaxRecentSources = 20;
        private const int MaxMessagesPerSource = 5;

        // 抛回给 WPF UI 调度器的主动事件钩子
        public event Action<NotificationMessage>? OnNotificationReceived;

        public async Task<bool> InitializeAsync()
        {
            _listener = UserNotificationListener.Current;
            UserNotificationListenerAccessStatus accessStatus = await _listener.RequestAccessAsync();

            if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
            {
                return false;
            }

            // 解决传统的 WinRT 事件订阅 (_listener.NotificationChanged += ...) 在未打包成商店应用的传统 WPF 环境下抛出 0x80070490 的系统级底层缺陷。
            // 我们直接完全废弃这条由于 COM 桥接不可靠带来的报错捷径，转而使用高性能且极其稳健的后台异步身份ID轮询比对法。
            _cts = new CancellationTokenSource();
            _ = StartPollingLoopAsync(_cts.Token);
            
            return true;
        }

        private async Task StartPollingLoopAsync(CancellationToken cancellationToken)
        {
            // 1. 初始化收集目前的历史通知，但不播放（避免启动时出现已读弹幕）
            try
            {
                var initialNotifications = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in initialNotifications)
                {
                    _knownNotificationIds.Add(n.Id);
                    
                    // 记录历史通知的来源及纯文本内容
                    string aumid = n.AppInfo?.AppUserModelId ?? "";
                    string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    if (!string.IsNullOrEmpty(aumid) || !string.IsNullOrEmpty(appName))
                    {
                        string messageText = ParseNotificationTextOnly(n);
                        TrackRecentSource(aumid, appName, messageText);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationService] 初始化历史通知失败: {ex.Message}");
            }

            // 2. 无限跨线程安全轮询（间隔1.5秒非常轻量，完全没有任何性能负担）
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1500, cancellationToken);
                try
                {
                    var currentNotifications = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
                    var currentIds = new HashSet<uint>();

                    foreach (var n in currentNotifications)
                    {
                        currentIds.Add(n.Id);
                        
                        // 发现增量的新通知
                        if (!_knownNotificationIds.Contains(n.Id))
                        {
                            _knownNotificationIds.Add(n.Id);

                            // 作用域预过滤：在昂贵的 ParseNotificationAsync 之前快速跳过被屏蔽的来源
                            string aumid = n.AppInfo?.AppUserModelId ?? "";
                            string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                            if (!ScopeFilter.ShouldAcceptSource(aumid, appName)) continue;

                            try
                            {
                                var msg = await ParseNotificationAsync(n);
                                OnNotificationReceived?.Invoke(msg);

                                // 追踪近期通知来源并缓存具体内容（去重，供 UI 快速选择及预览）
                                string messageText = ParseNotificationTextOnly(n);
                                TrackRecentSource(aumid, appName, messageText);
                            }
                            catch { }
                        }
                    }

                    // 取交集清理已经被用户划掉的旧追踪，杜绝内存泄漏
                    _knownNotificationIds.IntersectWith(currentIds);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NotificationService] 轮询异常: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private void TrackRecentSource(string aumid, string appName, string messageText)
        {
            if (string.IsNullOrEmpty(aumid) && string.IsNullOrEmpty(appName)) return;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existingItem = RecentSources.FirstOrDefault(r =>
                    (!string.IsNullOrEmpty(r.Identifier) && r.Identifier.Equals(aumid, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(r.DisplayName) && r.DisplayName.Equals(appName, StringComparison.OrdinalIgnoreCase)));

                if (existingItem != null)
                {
                    // 移到最前
                    RecentSources.Remove(existingItem);
                    RecentSources.Insert(0, existingItem);
                }
                else
                {
                    existingItem = new ScopeRuleItemDto
                    {
                        DisplayName = appName,
                        Identifier = aumid
                    };
                    RecentSources.Insert(0, existingItem);
                }

                // 写入缓存文本并去重（避免因为重复推送相同内容导致刷屏）
                if (!string.IsNullOrWhiteSpace(messageText) && !existingItem.RecentMessages.Contains(messageText))
                {
                    existingItem.RecentMessages.Insert(0, messageText);
                    while (existingItem.RecentMessages.Count > MaxMessagesPerSource)
                        existingItem.RecentMessages.RemoveAt(existingItem.RecentMessages.Count - 1);
                }

                while (RecentSources.Count > MaxRecentSources)
                    RecentSources.RemoveAt(RecentSources.Count - 1);

                // 更新配置缓存
                BarrageSettings.RecentSourcesCache = RecentSources.ToList();
                BarrageSettings.ExportConfig();
            });
        }

        /// <summary>
        /// 极速提取通知的纯文本，跳过高昂的图片流读取，专用于界面历史折叠预览
        /// </summary>
        private string ParseNotificationTextOnly(UserNotification notification)
        {
            string title = "";
            string body = "";
            var binding = notification.Notification.Visual.Bindings.FirstOrDefault();
            if (binding != null)
            {
                var textElements = binding.GetTextElements();
                if (textElements.Count > 0) title = textElements[0].Text;
                if (textElements.Count > 1) body = textElements[1].Text;
                for (int i = 2; i < textElements.Count; i++)
                {
                    body += " | " + textElements[i].Text;
                }
            }
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return "";
            if (string.IsNullOrWhiteSpace(body)) return title;
            return $"{title}: {body}";
        }

        private async Task<NotificationMessage> ParseNotificationAsync(UserNotification notification)
        {
            var msg = new NotificationMessage();
            msg.AppName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "System";
            msg.Aumid = notification.AppInfo?.AppUserModelId ?? string.Empty;

            var binding = notification.Notification.Visual.Bindings.FirstOrDefault();
            if (binding != null)
            {
                var textElements = binding.GetTextElements();
                if (textElements.Count > 0) msg.Title = textElements[0].Text;
                if (textElements.Count > 1) msg.Body = textElements[1].Text;
                // 拼合剩余超长正文
                for (int i = 2; i < textElements.Count; i++)
                {
                    msg.Body += " | " + textElements[i].Text;
                }
            }

            // 1. 如果是原生 UWP 或注册的现代应用，则通过注册系统管线索要高质量原图
            if (msg.AppIcon == null)
            {
                try
                {
                    var displayInfo = notification.AppInfo.DisplayInfo;
                    if (displayInfo != null)
                    {
                        // [破局修复]：将 32x32 索取改为极高精度的 256x256
                        // UWP 的图标库内部由于微软官方 UI 规范通常包含了“极其骇人”的透明垫层。如果请求 32x32，实体肉眼通常只有 8 像素。
                        var streamRef = displayInfo.GetLogo(new Windows.Foundation.Size(256, 256));
                        if (streamRef != null)
                        {
                            using var ras = await streamRef.OpenReadAsync();
                            using var stream = ras.AsStream();
                            
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = stream;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                msg.AppIcon = bitmap;
                                msg.IsUwpIcon = true;
                            });
                        }
                    }
                }
                catch { }
            }

            // 2. 特殊系统级 AUMID 硬编码回落（微软未提供防御中心等原生组件的图标流）
            if (msg.AppIcon == null && !string.IsNullOrEmpty(msg.Aumid))
            {
                System.Drawing.Icon fallbackIcon = null;
                
                if (msg.Aumid.Contains("Defender") || msg.Aumid.Contains("Security"))
                {
                    // [审美优化] 放弃使用丑陋古老的 SystemIcons.Shield，直接去 System32 扒防御中心的系统托盘原生 EXE 高清图标！
                    try
                    {
                        string secHealthPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SecurityHealthSystray.exe");
                        if (File.Exists(secHealthPath))
                        {
                            fallbackIcon = System.Drawing.Icon.ExtractAssociatedIcon(secHealthPath);
                        }
                    }
                    catch { }

                    if (fallbackIcon == null)
                    {
                        fallbackIcon = System.Drawing.SystemIcons.Shield;
                    }
                }
                else if (msg.Aumid.StartsWith("Windows.SystemToast"))
                {
                    fallbackIcon = System.Drawing.SystemIcons.Information;
                }

                if (fallbackIcon != null)
                {
                    IntPtr hIcon = fallbackIcon.Handle;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            hIcon,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                            
                        bitmap.Freeze();
                        msg.AppIcon = bitmap;
                    });

                    // 物理释放我们自己通过 ExtractAssociatedIcon 获取的原生 HICON 句柄，并调用 Dispose 释放托管 Icon，
                    // 彻底阻断 GDI 图标句柄内存泄漏。对于系统内置单例（如 SystemIcons.Shield），因为并非由我们创建，此处不能销毁它们，以防止系统范围崩溃。
                    if (fallbackIcon != System.Drawing.SystemIcons.Shield && fallbackIcon != System.Drawing.SystemIcons.Information)
                    {
                        NativeMethods.DestroyIcon(hIcon);
                        fallbackIcon.Dispose();
                    }
                }
            }

            // 3. Win32 程序的进程暴力图标回落机制（针对如 QQ、Edge 等未进入 UWP 管线的老牌软件）
            if (msg.AppIcon == null && !string.IsNullOrEmpty(msg.AppName))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // 如果在当前系统中找到了同名进程，直接抓取其中进程入口的主模块 EXE 自带的图标！
                        var process = System.Diagnostics.Process.GetProcessesByName(msg.AppName).FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle));
                        if (process == null) 
                        {
                            process = System.Diagnostics.Process.GetProcessesByName(msg.AppName).FirstOrDefault();
                        }

                        if (process != null && process.MainModule != null)
                        {
                            var exePath = NativeMethods.GetProcessImagePath(process.Id);
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                                if (sysIcon != null)
                                {
                                    IntPtr hIcon = sysIcon.Handle;
                                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                        hIcon,
                                        System.Windows.Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                    
                                    bitmap.Freeze();
                                    msg.AppIcon = bitmap;

                                    // 物理释放提取出来的原生 HICON 句柄，根治高频通知提取 Win32 应用图标时的 GDI 内存泄漏
                                    NativeMethods.DestroyIcon(hIcon);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 权限不足或提取失败静默处理
                    }
                });
            }

            // 4. [新增] 成功提取到图标后，将其缓存到本地磁盘，供后续直接快速读取（尤其当该应用关闭时）
            if (msg.AppIcon is BitmapSource bmp)
            {
                // 优先使用 Aumid，如果是空的（Win32 进程），则使用 AppName (即 identifier)
                string identifier = string.IsNullOrEmpty(msg.Aumid) ? msg.AppName : msg.Aumid;
                _ = IconCacheService.CacheIconAsync(identifier, bmp);
            }

            return msg;
        }
    }
}
