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
            // 1. 初始化收集并播放目前的历史通知
            try
            {
                var initialNotifications = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in initialNotifications)
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
            msg.AppName = notification.AppInfo.DisplayInfo.DisplayName ?? "未知系统通知";
            msg.Aumid = notification.AppInfo.AppUserModelId ?? "";

            // 解析标题和正文
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

            // 安全获取系统管线中的图片流转换为 WPF 的内存位图
            try
            {
                var displayInfo = notification.AppInfo.DisplayInfo;
                if (displayInfo != null)
                {
                    var streamRef = displayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
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
                            bitmap.Freeze(); // 冻结内存以允许 WPF 的跨线程高效传递
                            msg.AppIcon = bitmap;
                        });
                    }
                }
            }
            catch
            {
                // Win32 程序图层回落暂无，等待最终设置页面建立后做提取系统快捷方式的逻辑
            }

            return msg;
        }
    }
}
