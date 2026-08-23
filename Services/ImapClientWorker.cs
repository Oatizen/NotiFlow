using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using NotiFlow.Models;

namespace NotiFlow.Services
{
    /// <summary>
    /// 单个邮箱账号的 IMAP 异步长连接监听工作单元。
    /// 基于 MailKit 实现 IMAP IDLE 实时事件驱动、心跳保活与网络异常自动重连。
    /// </summary>
    public class ImapClientWorker : IDisposable
    {
        public EmailAccountConfigDto Account { get; }

        private ImapClient? _client;
        private CancellationTokenSource? _workerCts;
        private UniqueId _lastKnownUid = UniqueId.MinValue;
        private bool _isInitialSyncDone;
        private bool _disposed;

        /// <summary>
        /// 当接收到新邮件并解析完成时触发的事件。
        /// </summary>
        public event Action<EmailAccountConfigDto, MimeMessage>? OnEmailReceived;

        public ImapClientWorker(EmailAccountConfigDto account)
        {
            Account = account;
        }

        /// <summary>
        /// 启动该邮箱的后台异步监听循环。
        /// </summary>
        public void Start()
        {
            if (_disposed || !Account.IsEnabled) return;

            Stop();
            _workerCts = new CancellationTokenSource();
            _ = RunWorkerLoopAsync(_workerCts.Token);
        }

        /// <summary>
        /// 停止当前邮箱的监听连接并释放资源。
        /// </summary>
        public void Stop()
        {
            try
            {
                _workerCts?.Cancel();
                _workerCts?.Dispose();
                _workerCts = null;

                if (_client != null && _client.IsConnected)
                {
                    _client.Disconnect(true);
                }
                _client?.Dispose();
                _client = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 停止异常: {ex.Message}");
            }
        }

        private async Task RunWorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                try
                {
                    _client = new ImapClient();
                    // 忽略由于国内自建根证书或某些邮件代理引发的非致命 SSL 警告
                    _client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                    var secureSocketOptions = Account.UseSsl
                        ? (Account.ServerPort == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable)
                        : SecureSocketOptions.None;

                    await _client.ConnectAsync(Account.ServerHost, Account.ServerPort, secureSocketOptions, ct);

                    string authCode = Account.AuthCode;
                    if (string.IsNullOrEmpty(authCode))
                    {
                        Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 授权码为空，跳过认证");
                        await Task.Delay(30000, ct);
                        continue;
                    }

                    await _client.AuthenticateAsync(Account.EmailAddress, authCode, ct);

                    var inbox = _client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

                    // 1. 初始化锚定最新 UID（防止启动时将历史邮件作为新通知弹幕发射）
                    if (!_isInitialSyncDone)
                    {
                        if (inbox.Count > 0)
                        {
                            var summaries = await inbox.FetchAsync(inbox.Count - 1, -1, MessageSummaryItems.UniqueId, ct);
                            if (summaries.Count > 0)
                            {
                                _lastKnownUid = summaries.Max(s => s.UniqueId);
                            }
                        }
                        _isInitialSyncDone = true;
                        Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 初始化同步完成，当前最新 UID: {_lastKnownUid}");
                    }

                    // 2. 检查是否支持 IMAP IDLE 即时推送
                    bool supportsIdle = _client.Capabilities.HasFlag(ImapCapabilities.Idle);

                    while (!ct.IsCancellationRequested && _client.IsConnected && _client.IsAuthenticated)
                    {
                        if (supportsIdle)
                        {
                            // 9 分钟心跳超时（防 NAT 超时断线）
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(9));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                            // 订阅新邮件计数变动事件
                            EventHandler<EventArgs>? countChangedHandler = null;
                            countChangedHandler = (s, e) =>
                            {
                                try { timeoutCts.Cancel(); } catch { }
                            };

                            inbox.CountChanged += countChangedHandler;

                            try
                            {
                                await _client.IdleAsync(linkedCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                // 达到 9 分钟心跳或收到新邮件唤醒
                            }
                            finally
                            {
                                inbox.CountChanged -= countChangedHandler;
                            }
                        }
                        else
                        {
                            // 回退方案：不支持 IDLE 时采用轻量 30 秒轮询
                            await Task.Delay(30000, ct);
                            await _client.NoOpAsync(ct);
                        }

                        // 3. 提取增量新邮件
                        await CheckAndDispatchNewEmailsAsync(inbox, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 通信异常，5秒后尝试重连: {ex.Message}");
                    try
                    {
                        if (_client != null && _client.IsConnected)
                        {
                            await _client.DisconnectAsync(true, CancellationToken.None);
                        }
                        _client?.Dispose();
                        _client = null;
                    }
                    catch { }

                    // 断线退避重试
                    try { await Task.Delay(5000, ct); } catch { break; }
                }
            }
        }

        private async Task CheckAndDispatchNewEmailsAsync(IMailFolder inbox, CancellationToken ct)
        {
            if (inbox.Count == 0) return;

            try
            {
                // 获取当前所有 UID 大于 _lastKnownUid 的新邮件摘要
                IList<IMessageSummary> newSummaries;
                if (_lastKnownUid == UniqueId.MinValue)
                {
                    newSummaries = await inbox.FetchAsync(0, -1, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct);
                }
                else
                {
                    var range = new UniqueIdRange(_lastKnownUid, UniqueId.MaxValue);
                    newSummaries = await inbox.FetchAsync(range, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct);
                    // 排除等于 _lastKnownUid 的自身
                    newSummaries = newSummaries.Where(s => s.UniqueId > _lastKnownUid).ToList();
                }

                if (newSummaries.Count > 0)
                {
                    foreach (var summary in newSummaries)
                    {
                        if (summary.UniqueId > _lastKnownUid)
                        {
                            _lastKnownUid = summary.UniqueId;
                        }

                        try
                        {
                            // 仅拉取邮件信头与基础正文结构（不下载几十兆的大附件），毫秒级完成
                            var message = await inbox.GetMessageAsync(summary.UniqueId, ct);
                            if (message != null)
                            {
                                OnEmailReceived?.Invoke(Account, message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 解析单封邮件异常: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImapWorker - {Account.EmailAddress}] 检查新邮件失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
