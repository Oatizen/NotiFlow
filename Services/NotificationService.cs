using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications.Management;
using Windows.UI.Notifications;
using NotiFlow.Models;
using System.IO;

namespace NotiFlow.Services
{
    public class NotificationService
    {
        private UserNotificationListener? _listener;
        private readonly HashSet<uint> _knownNotificationIds = new();
        
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
            _ = StartPollingLoopAsync();
            
            return true;
        }

        private async Task StartPollingLoopAsync()
        {
            // 1. 初始化收集目前的历史通知，但不播放（避免启动时出现已读弹幕）
            try
            {
                var initialNotifications = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in initialNotifications)
                {
                    _knownNotificationIds.Add(n.Id);
                }
            }
            catch { }

            // 2. 无限跨线程安全轮询（间隔1.5秒非常轻量，完全没有任何性能负担）
            while (true)
            {
                await Task.Delay(1500);
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
                            try
                            {
                                var msg = await ParseNotificationAsync(n);
                                OnNotificationReceived?.Invoke(msg);
                            }
                            catch { }
                        }
                    }

                    // 取交集清理已经被用户划掉的旧追踪，杜绝内存泄漏
                    _knownNotificationIds.IntersectWith(currentIds);
                }
                catch { }
            }
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
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            fallbackIcon.Handle,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                            
                        bitmap.Freeze();
                        msg.AppIcon = bitmap;
                    });
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
                            var exePath = process.MainModule.FileName;
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                                if (sysIcon != null)
                                {
                                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                        sysIcon.Handle,
                                        System.Windows.Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                    
                                    bitmap.Freeze();
                                    msg.AppIcon = bitmap;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 权限不足（如试图访问高权限进程等）时静默失败回落为无图模式
                    }
                });
            }

            return msg;
        }
    }
}
