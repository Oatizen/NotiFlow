using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MimeKit;
using NotiFlow.Models;
using NotiFlow.Rendering;

namespace NotiFlow.Services
{
    /// <summary>
    /// 邮件通知监听中心服务（单例）。
    /// 管理所有配置邮箱的 ImapClientWorker，并在接收到新邮件时组装弹幕派发至渲染管线。
    /// </summary>
    public class EmailNotificationService : IDisposable
    {
        public static EmailNotificationService? Instance { get; private set; }

        private readonly List<ImapClientWorker> _workers = new();
        private readonly object _lock = new();
        private bool _disposed;

        public EmailNotificationService()
        {
            Instance = this;
        }

        /// <summary>
        /// 初始化并启动所有已启用的邮箱监听任务。
        /// </summary>
        public async Task InitializeAsync()
        {
            await Task.Run(() => ReloadAccounts());
        }

        /// <summary>
        /// 重新加载所有邮箱配置（当用户在设置界面增删改邮箱账号后调用）。
        /// </summary>
        public void ReloadAccounts()
        {
            lock (_lock)
            {
                if (_disposed) return;

                // 1. 停止并清理当前全部 worker
                foreach (var worker in _workers)
                {
                    worker.OnEmailReceived -= HandleEmailReceived;
                    worker.Dispose();
                }
                _workers.Clear();

                // 2. 为每一个已启用的邮箱创建新的 worker 并启动
                foreach (var account in BarrageSettings.EmailAccounts.Where(a => a.IsEnabled))
                {
                    if (string.IsNullOrWhiteSpace(account.EmailAddress) || string.IsNullOrWhiteSpace(account.ServerHost))
                        continue;

                    var worker = new ImapClientWorker(account);
                    worker.OnEmailReceived += HandleEmailReceived;
                    worker.Start();
                    _workers.Add(worker);
                }

                System.Diagnostics.Debug.WriteLine($"[EmailNotificationService] 已重新加载 {_workers.Count} 个活跃邮箱监听连接");
            }
        }

        /// <summary>
        /// 处理底层 Worker 推送过来的新邮件事件。
        /// </summary>
        private void HandleEmailReceived(EmailAccountConfigDto account, MimeMessage email)
        {
            if (_disposed || !BarrageSettings.IsWorking) return;

            try
            {
                // 1. 根据用户偏好格式化弹幕对象
                var notificationMsg = EmailMessageFormatter.FormatNotification(
                    account,
                    email,
                    BarrageSettings.EmailDisplaySettings);

                // 2. 回到主 UI 调度器派发给弹幕渲染中心
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    BarrageManager.Instance?.EnqueueNotification(notificationMsg);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EmailNotificationService] 派发邮件弹幕失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                foreach (var worker in _workers)
                {
                    worker.OnEmailReceived -= HandleEmailReceived;
                    worker.Dispose();
                }
                _workers.Clear();
            }
        }
    }
}
