using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Composition;
using NotiFlow.Rendering;

namespace NotiFlow.Models
{
    /// <summary>
    /// 代表一个底层视觉图层的轻量级弹幕对象。
    /// 通过 Windows.UI.Composition 的 SpriteVisual + CompositionDrawingSurface 实现
    /// GPU 纹理绘制和合成器驱动的动画。
    /// </summary>
    public class BarrageItem : IDisposable
    {
        private const double UwpIconScaleFactor = 2.5;

        // 物理状态
        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public double SpeedPixelsPerSec { get; set; }
        
        /// <summary>
        /// 弹幕进入屏幕时的初始 X 坐标（屏幕右端），用于推算动画进度中的当前位置。
        /// </summary>
        public double StartX { get; set; }
        
        // 尺寸缓存，供调度器判断何时完全离开屏幕
        public double PhysicalWidth { get; private set; }
        public int TrackIndex { get; set; }

        // 生命周期
        public bool IsAlive { get; set; } = true;
        public bool TrackReleased { get; set; } = false;

        // ===== Composition 资源 =====
        /// <summary>
        /// 弹幕的合成视觉对象，持有 GPU 纹理并参与合成器动画。
        /// 由 BuildVisualForComposition 在后台线程创建。
        /// </summary>
        public SpriteVisual? Visual { get; private set; }

        /// <summary>
        /// 弹幕纹理对应的 CompositionDrawingSurface，
        /// 需在弹幕生命周期结束时释放以回收 GPU 内存。
        /// </summary>
        private CompositionDrawingSurface? _surface;

        /// <summary>
        /// 动画开始时刻（UTC），用于推算当前滚动进度以判断轨道释放。
        /// </summary>
        public DateTime AnimationStartTime { get; set; }

        /// <summary>
        /// 动画结束时刻（UTC），到达此时刻后弹幕将从合成树中移除并回收。
        /// </summary>
        public DateTime AnimationEndTime { get; set; }

        // Win2D 资源缓存
        private CanvasTextLayout? _textLayout;
        private CanvasBitmap? _appIcon;
        private Windows.UI.Color _textColor;
        private Windows.UI.Color _backgroundColor;
        private bool _hasIcon;
        private bool _isUwpIcon;
        private double _iconSize;
        private double _contentWidth;
        private double _contentHeight;
        private double _bgWidth;
        private double _bgHeight;
        private double _padH = 12;
        private double _padV = 6;

        // 构建时缓存的设置值（后台线程安全）
        private bool _showBackground;
        private float _cornerRadius;
        private bool _isUnderlined;

        public void Reset()
        {
            IsAlive = true;
            TrackReleased = false;
            CurrentX = 0;
            CurrentY = 0;
            StartX = 0;
            SpeedPixelsPerSec = 0;
            PhysicalWidth = 0;
            TrackIndex = -1;
            AnimationStartTime = default;
            AnimationEndTime = default;

            _textLayout?.Dispose();
            _textLayout = null;

            _appIcon?.Dispose();
            _appIcon = null;

            // 释放 Composition 资源
            Visual?.Dispose();
            Visual = null;

            _surface?.Dispose();
            _surface = null;
        }

        /// <summary>
        /// 在后台线程构建弹幕的 Composition 视觉：
        /// 1. 使用 Win2D 计算文字布局和尺寸
        /// 2. 通过 CompositionGraphicsDevice 创建 GPU 纹理 (CompositionDrawingSurface)
        /// 3. 使用 CanvasComposition.CreateDrawingSession 在纹理上直接绘制文字和图标
        /// 4. 创建 SpriteVisual 并绑定纹理画刷
        /// </summary>
        public void BuildVisualForComposition(CanvasDevice device, Compositor compositor,
            CompositionGraphicsDevice graphicsDevice,
            string appName, string title, string body,
            System.Windows.Media.Color textColor, double textOpacity,
            double fontSize, string fontFamilyName,
            FontStyle fontStyle, FontWeight fontWeight,
            bool showBackground, System.Windows.Media.Color bgColor, double bgOpacity,
            CornerRadius cornerRadius,
            bool highlightEllipsis, System.Windows.Media.Color ellColor,
            bool showAppName, double maxTextLen, bool isUnderlined,
            bool showAppIcon, byte[]? iconPixels, int iconWidth, int iconHeight, bool isUwpIcon)
        {
            _iconSize = fontSize * 1.25;
            _contentWidth = 0;

            _hasIcon = showAppIcon && iconPixels != null && iconWidth > 0 && iconHeight > 0;
            if (_hasIcon)
            {
                _contentWidth += _iconSize + 10;
                _isUwpIcon = isUwpIcon;
                _appIcon = CanvasBitmap.CreateFromBytes(device, iconPixels!,
                    iconWidth, iconHeight,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    96, CanvasAlphaMode.Premultiplied);
            }

            // 文字内容拼接
            string prefix = "";
            if (showAppName && !string.IsNullOrEmpty(appName))
            {
                prefix += appName;
            }
            if (!string.IsNullOrEmpty(title))
            {
                if (prefix.Length > 0) prefix += " ";
                prefix += title;
            }
            if (prefix.Length > 0)
            {
                prefix += "：";
            }
            prefix = prefix.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            string bodyText = body.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            double currentWeight = 0;
            int truncateIndex = -1;
            for (int i = 0; i < bodyText.Length; i++)
            {
                char c = bodyText[i];
                currentWeight += (c <= 127) ? 0.5 : 1.0;
                if (currentWeight > maxTextLen)
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

            _textColor = Windows.UI.Color.FromArgb(
                (byte)(textColor.A * textOpacity),
                textColor.R, textColor.G, textColor.B);

            _backgroundColor = Windows.UI.Color.FromArgb(
                (byte)(bgColor.A * bgOpacity),
                bgColor.R, bgColor.G, bgColor.B);

            var textFormat = new CanvasTextFormat
            {
                FontFamily = fontFamilyName + ", Segoe UI Emoji",
                FontSize = (float)fontSize,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)fontWeight.ToOpenTypeWeight() },
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            if (fontStyle == FontStyles.Italic)
            {
                textFormat.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }

            _textLayout = new CanvasTextLayout(device, fullText, textFormat, 0.0f, 0.0f);

            if (highlightEllipsis && fullText.EndsWith("......"))
            {
                Windows.UI.Color ellipsisWinColor = Windows.UI.Color.FromArgb(
                    (byte)(ellColor.A * textOpacity),
                    ellColor.R, ellColor.G, ellColor.B);
                var brush = new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(device, ellipsisWinColor);
                _textLayout.SetBrush(fullText.Length - 6, 6, brush);
            }

            double textWidth = _textLayout.LayoutBounds.Width;
            _contentWidth += textWidth;
            _contentHeight = Math.Max(fontSize, _iconSize);

            _bgWidth = _contentWidth + _padH * 2;
            _bgHeight = _contentHeight + _padV * 2;

            this.PhysicalWidth = showBackground ? _bgWidth : _contentWidth;

            // 缓存设置值供 Draw 使用
            _showBackground = showBackground;
            _cornerRadius = (float)cornerRadius.TopLeft;
            _isUnderlined = isUnderlined;

            // ===== 创建 CompositionDrawingSurface 并在其上绘制弹幕内容 =====
            const int spriteMargin = 2;
            double visibleHeight = _showBackground ? _bgHeight : _contentHeight;
            int surfaceWidth = (int)Math.Ceiling(PhysicalWidth) + spriteMargin * 2;
            int surfaceHeight = (int)Math.Ceiling(visibleHeight) + spriteMargin * 2;

            if (surfaceWidth <= 0 || surfaceHeight <= 0) return;

            _surface = graphicsDevice.CreateDrawingSurface(
                new Windows.Foundation.Size(surfaceWidth, surfaceHeight),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied);

            // 在 GPU 纹理上直接绘制弹幕内容（通过 CompositionHelper 替代 CanvasComposition）
            using (var wrapper = CompositionHelper.CreateDrawingSession(_surface, device))
            {
                var session = wrapper.Session;
                session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));

                // 临时设置绘制坐标为精灵图原点
                double savedX = CurrentX, savedY = CurrentY;
                CurrentX = spriteMargin;
                CurrentY = spriteMargin;

                Draw(session);

                CurrentX = savedX;
                CurrentY = savedY;
            }

            // 创建 SpriteVisual 并绑定纹理画刷
            var surfaceBrush = compositor.CreateSurfaceBrush(_surface);
            surfaceBrush.Stretch = CompositionStretch.None;
            Visual = compositor.CreateSpriteVisual();
            Visual.Size = new Vector2(surfaceWidth, surfaceHeight);
            Visual.Brush = surfaceBrush;
        }

        public void Draw(CanvasDrawingSession session)
        {
            if (_textLayout == null) return;

            float drawX = (float)CurrentX;
            float drawY = (float)CurrentY;

            if (_showBackground)
            {
                session.FillRoundedRectangle(drawX, drawY, (float)_bgWidth, (float)_bgHeight, _cornerRadius, _cornerRadius, _backgroundColor);
            }

            float contentX = drawX + (_showBackground ? (float)_padH : 0);
            float contentY = drawY + (_showBackground ? (float)_padV : 0);

            if (_hasIcon && _appIcon != null)
            {
                float imageX = contentX;
                float imageY = contentY + (float)(_contentHeight - _iconSize) / 2.0f;

                if (_isUwpIcon)
                {
                    float centerX = imageX + (float)_iconSize / 2.0f;
                    float centerY = imageY + (float)_iconSize / 2.0f;
                    var oldTransform = session.Transform;
                    session.Transform = Matrix3x2.CreateScale((float)UwpIconScaleFactor, (float)UwpIconScaleFactor, new Vector2(centerX, centerY)) * oldTransform;
                    session.DrawImage(_appIcon, new Windows.Foundation.Rect(imageX, imageY, _iconSize, _iconSize));
                    session.Transform = oldTransform;
                }
                else
                {
                    session.DrawImage(_appIcon, new Windows.Foundation.Rect(imageX, imageY, _iconSize, _iconSize));
                }

                contentX += (float)_iconSize + 10f;
            }

            float textY = contentY + (float)(_contentHeight - _textLayout.LayoutBounds.Height) / 2.0f;

            if (!_showBackground)
            {
                Windows.UI.Color shadowColor = Windows.UI.Color.FromArgb((byte)(0.9 * _textColor.A), 0, 0, 0);
                session.DrawTextLayout(_textLayout, contentX + 1.5f, textY + 1.5f, shadowColor);
            }

            session.DrawTextLayout(_textLayout, contentX, textY, _textColor);

            if (_isUnderlined)
            {
                float lineY = textY + (float)_textLayout.LayoutBounds.Height;
                session.DrawLine(contentX, lineY, contentX + (float)_textLayout.LayoutBounds.Width, lineY, _textColor, 2.0f);
            }
        }

        private CanvasBitmap? ConvertToCanvasBitmap(CanvasDevice device, ImageSource source)
        {
            if (source is BitmapSource bitmapSource)
            {
                try
                {
                    var formatted = new FormatConvertedBitmap(bitmapSource, PixelFormats.Pbgra32, null, 0);
                    int width = formatted.PixelWidth;
                    int height = formatted.PixelHeight;
                    if (width == 0 || height == 0) return null;

                    byte[] pixels = new byte[width * height * 4];
                    formatted.CopyPixels(pixels, width * 4, 0);
                    return CanvasBitmap.CreateFromBytes(device, pixels, width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
                }
                catch { return null; }
            }
            return null;
        }

        public void Dispose()
        {
            _textLayout?.Dispose();
            _textLayout = null;

            _appIcon?.Dispose();
            _appIcon = null;

            // 释放 Composition 资源
            Visual?.Dispose();
            Visual = null;

            _surface?.Dispose();
            _surface = null;
        }
    }
}
