using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotiFlow.Models
{
    /// <summary>
    /// 代表一个底层视觉图层的轻量级弹幕对象。
    /// 抛弃了厚重的控件树结构，通过 DrawingContext 纯像素绘制。
    /// 可以直接被统一的视觉宿主管理并进行硬件级位移。
    /// </summary>
    public class BarrageItem : DrawingVisual
    {
        /// <summary>
        /// UWP 图标缩放补偿系数。
        /// UWP 应用图标在 256x256 画布中通常包含大量透明垫层，
        /// 直接绘制会导致图标视觉上偏小，需要放大补偿。
        /// </summary>
        private const double UwpIconScaleFactor = 2.5;
        // 物理状态
        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public double SpeedPixelsPerSec { get; set; }
        
        // 尺寸缓存，供调度器判断何时完全离开屏幕
        public double PhysicalWidth { get; private set; }
        public int TrackIndex { get; set; }

        // 生命周期
        public bool IsAlive { get; set; } = true;
        public bool TrackReleased { get; set; } = false;

        /// <summary>
        /// 将弹幕对象重置为初始状态，供对象池复用时调用。
        /// 集中管理所有需要清理的状态字段，防止新增字段时遗漏重置导致脏数据。
        /// </summary>
        public void Reset()
        {
            IsAlive = true;
            TrackReleased = false;
            CurrentX = 0;
            CurrentY = 0;
            SpeedPixelsPerSec = 0;
            PhysicalWidth = 0;
            TrackIndex = -1;
        }

        public void BuildVisual(NotificationMessage message, Brush textBrush, double fontSize, FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight, double pixelsPerDip)
        {
            // ===== 预计算阶段（不绘制，仅测量尺寸） =====
            double iconSize = fontSize * 1.25;
            double contentWidth = 0;

            // 图标占位宽度
            bool hasIcon = BarrageSettings.ShowAppIcon && message.AppIcon != null;
            if (hasIcon)
            {
                contentWidth += iconSize + 10; // 图标宽 + 右间距
            }

            // 文字内容拼接：纯净格式 "应用名称：内容"
            string prefix = "";
            if (BarrageSettings.ShowAppName && !string.IsNullOrEmpty(message.AppName))
            {
                prefix += message.AppName;
            }
            if (!string.IsNullOrEmpty(message.Title))
            {
                if (prefix.Length > 0) prefix += " ";
                prefix += message.Title;
            }
            if (prefix.Length > 0)
            {
                prefix += "：";
            }
            prefix = prefix.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            string bodyText = message.Body ?? "";
            bodyText = bodyText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            
            double currentWeight = 0;
            int truncateIndex = -1;
            for (int i = 0; i < bodyText.Length; i++)
            {
                char c = bodyText[i];
                // ASCII characters usually count as half-width (0.5 weight)
                currentWeight += (c <= 127) ? 0.5 : 1.0;

                if (currentWeight > BarrageSettings.MaxTextLength)
                {
                    truncateIndex = i;
                    break;
                }
            }

            if (truncateIndex != -1)
            {
                bodyText = bodyText.Substring(0, truncateIndex) + "......";
            }

            string fullText = prefix + bodyText;

            // 预烘焙字形以获取精确的像素尺寸
            var typeface = new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
            
            // 真正修复：拷贝原始颜色画刷并注入透明度配置设置
            Brush finalTxtBrush = textBrush.Clone();
            finalTxtBrush.Opacity *= BarrageSettings.TextOpacity;
            if (finalTxtBrush.CanFreeze) finalTxtBrush.Freeze();


            var formattedText = new FormattedText(
                fullText,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                finalTxtBrush,
                pixelsPerDip);

            // NOTE: WPF 的 FormattedText 渲染管线不支持彩色 Emoji (COLR/CPAL)，
            // Emoji 会以当前画刷颜色渲染为单色轮廓，这是 WPF 框架级的已知限制。

            // 修复省略号高亮度：当开启选项并且文本真的以省略号结尾时，对最后6个字符上色
            if (BarrageSettings.HighlightEllipsis && fullText.EndsWith("......"))
            {
                formattedText.SetForegroundBrush(BarrageSettings.EllipsisColor, fullText.Length - 6, 6);
            }

            contentWidth += formattedText.WidthIncludingTrailingWhitespace;
            double contentHeight = Math.Max(fontSize, iconSize);

            // 背景内边距
            double padH = 12; // 水平内边距
            double padV = 6;  // 垂直内边距

            // ===== 绘制阶段 =====
            using (DrawingContext dc = this.RenderOpen())
            {
                double bgWidth = contentWidth + padH * 2;
                double bgHeight = contentHeight + padV * 2;

                // 1. 绘制圆角背景遮罩（深色半透明板）
                if (BarrageSettings.ShowBackground)
                {
                    var bgBrush = BarrageSettings.BackgroundColor.Clone();
                    bgBrush.Opacity = BarrageSettings.BackgroundOpacity;
                    bgBrush.Freeze();

                    double cornerRadius = BarrageSettings.BackgroundCornerRadius.TopLeft;
                    dc.DrawRoundedRectangle(
                        bgBrush,        // 填充色
                        null,           // 无描边
                        new Rect(0, 0, bgWidth, bgHeight),
                        cornerRadius,
                        cornerRadius);
                }

                // 内容区域起始坐标（跳过内边距）
                double drawX = padH;
                double drawY = padV;

                // 2. 绘制图标
                if (hasIcon)
                {
                    double imageX = drawX;
                    double imageY = drawY + (contentHeight - iconSize) / 2.0;

                    if (message.IsUwpIcon)
                    {
                        dc.PushTransform(new ScaleTransform(UwpIconScaleFactor, UwpIconScaleFactor, imageX + iconSize / 2, imageY + iconSize / 2));
                        dc.DrawImage(message.AppIcon, new Rect(imageX, imageY, iconSize, iconSize));
                        dc.Pop();
                    }
                    else
                    {
                        dc.DrawImage(message.AppIcon, new Rect(imageX, imageY, iconSize, iconSize));
                    }

                    drawX += iconSize + 10;
                }

                // 3. 文字垂直居中偏移
                double textY = drawY + (contentHeight - formattedText.Height) / 2.0;

                // 绘制文字阴影（仅在无背景遮罩时生效，避免重复的视觉加深）
                if (!BarrageSettings.ShowBackground)
                {
                    var shadowBrush = Brushes.Black.Clone();
                    shadowBrush.Opacity = 0.9 * BarrageSettings.TextOpacity; // 阴影不透明度也需要受到全局透明度乘数影响
                    shadowBrush.Freeze();

#pragma warning disable CS0618 // 阴影文本不需要新版 API 的额外参数
                    var shadowText = new FormattedText(
                        fullText,
                        System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        shadowBrush,
                        pixelsPerDip);
#pragma warning restore CS0618

                    dc.DrawText(shadowText, new Point(drawX + 1.5, textY + 1.5));
                }

                // 4. 绘制主文字
                dc.DrawText(formattedText, new Point(drawX, textY));

                // 5. 下划线
                if (BarrageSettings.IsUnderlined)
                {
                    var pen = new Pen(finalTxtBrush, 2);
                    pen.Freeze();
                    // 修复下划线遮挡文字：将其置于整个文本框的高度的底侧而非基准线底侧
                    dc.DrawLine(pen,
                        new Point(drawX, textY + formattedText.Height),
                        new Point(drawX + formattedText.WidthIncludingTrailingWhitespace, textY + formattedText.Height));
                }

                // 记录总物理宽度（含背景边距），供物理引擎判断越界
                this.PhysicalWidth = BarrageSettings.ShowBackground ? bgWidth : contentWidth;
            }
        }
    }
}
