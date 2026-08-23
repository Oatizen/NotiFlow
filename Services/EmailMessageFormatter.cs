using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MimeKit;
using NotiFlow.Models;

namespace NotiFlow.Services
{
    /// <summary>
    /// 邮件消息格式化工具类。
    /// 将从 IMAP 拉取的邮件数据与用户的展示开关组合，构建出符合弹幕格式的 NotificationMessage。
    /// </summary>
    public static class EmailMessageFormatter
    {
        private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 将 MimeMessage 格式化为 NotificationMessage 弹幕对象。
        /// 弹幕基本格式：[邮箱图标] [收件名称 收件地址] 发件名称 <发件地址> : 邮件主题
        /// </summary>
        /// <param name="account">接收该邮件的本地邮箱账号配置</param>
        /// <param name="email">收到的 MimeMessage 邮件对象</param>
        /// <param name="settings">用户的弹幕字段显示偏好设置</param>
        /// <returns>准备派发至弹幕引擎的 NotificationMessage</returns>
        public static NotificationMessage FormatNotification(
            EmailAccountConfigDto account,
            MimeMessage email,
            EmailDisplaySettingsDto settings)
        {
            var msg = new NotificationMessage
            {
                Aumid = $"NotiFlow.Email.{account.ProviderType}.{account.Id}"
            };

            // 1. 图标分配
            if (settings.ShowEmailIcon)
            {
                msg.AppIcon = GetProviderIcon(account.ProviderType);
            }

            // 2. 组装收件邮箱信息 (AppName)
            var receiverParts = new List<string>();
            if (settings.ShowReceiverName && !string.IsNullOrWhiteSpace(account.DisplayName))
            {
                receiverParts.Add(account.DisplayName.Trim());
            }
            if (settings.ShowReceiverAddress && !string.IsNullOrWhiteSpace(account.EmailAddress))
            {
                receiverParts.Add(account.EmailAddress.Trim());
            }

            if (receiverParts.Count > 0)
            {
                msg.AppName = $"[{string.Join(" ", receiverParts)}]";
            }
            else
            {
                msg.AppName = string.Empty;
            }

            // 3. 组装发件人信息 (Title)
            var sender = email.From.Mailboxes.FirstOrDefault();
            string senderName = sender?.Name?.Trim() ?? string.Empty;
            string senderAddress = sender?.Address?.Trim() ?? string.Empty;

            var senderParts = new List<string>();
            if (settings.ShowSenderName && !string.IsNullOrWhiteSpace(senderName))
            {
                senderParts.Add(senderName);
            }
            if (settings.ShowSenderAddress && !string.IsNullOrWhiteSpace(senderAddress))
            {
                senderParts.Add($"<{senderAddress}>");
            }

            // 智能兜底：如果开启了发件人名称但名称为空，或两个都未开启，至少显示可用的名称或地址
            if (senderParts.Count == 0)
            {
                string fallback = !string.IsNullOrWhiteSpace(senderName) ? senderName : senderAddress;
                senderParts.Add(!string.IsNullOrWhiteSpace(fallback) ? fallback : "未知发件人");
            }

            msg.Title = string.Join(" ", senderParts);

            // 4. 邮件主题 (Body)
            string subject = email.Subject?.Trim() ?? string.Empty;
            msg.Body = string.IsNullOrWhiteSpace(subject) ? "(无主题)" : subject;

            return msg;
        }

        /// <summary>
        /// 获取邮箱服务商对应的高清图标。
        /// </summary>
        /// <param name="providerType">服务商类型标识</param>
        /// <returns>ImageSource 图标对象</returns>
        private static BitmapSource? _unifiedEmailIcon;

        /// <summary>
        /// 获取统一的“邮件”信封图标（128x128 高清位图，橙色主题圆角徽标 + 白色信封矢量轮廓）。
        /// 无论哪个邮箱接收到新邮件，均使用此统一标识。
        /// </summary>
        public static BitmapSource? GetUnifiedEmailIcon()
        {
            if (_unifiedEmailIcon != null) return _unifiedEmailIcon;

            try
            {
                int size = 128;
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 1. 绘制橙色圆角背景（与邮箱连接卡片色彩一致）
                    var bgBrush = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0xA0, 0x00),
                        Color.FromRgb(0xF5, 0x6A, 0x00),
                        new System.Windows.Point(0, 0),
                        new System.Windows.Point(1, 1));
                    dc.DrawRoundedRectangle(bgBrush, null, new Rect(6, 6, size - 12, size - 12), 22, 22);

                    // 2. 绘制白色信封矢量路径（图2 信封图形）
                    var envelopeGeometry = Geometry.Parse(
                        "M 22,34 L 106,34 C 112,34 116,38 116,44 L 116,94 C 116,100 112,104 106,104 L 22,104 C 16,104 12,100 12,94 L 12,44 C 12,38 16,34 22,34 Z " +
                        "M 14,40 L 64,76 L 114,40");

                    var pen = new Pen(Brushes.White, 7.5)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    dc.DrawGeometry(null, pen, envelopeGeometry);
                }

                rtb.Render(dv);
                rtb.Freeze();
                _unifiedEmailIcon = rtb;
                return _unifiedEmailIcon;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EmailMessageFormatter] 绘制统一邮件图标异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取邮件通知图标：统一返回“邮件”信封图标。
        /// </summary>
        /// <param name="providerType">服务商类型（保留以兼容历史调用）</param>
        /// <returns>ImageSource 统一邮件图标</returns>
        public static ImageSource? GetProviderIcon(string? providerType = null)
        {
            return GetUnifiedEmailIcon();
        }
    }
}
