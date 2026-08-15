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
using Microsoft.Graphics.Canvas.Effects;

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
        public Windows.UI.Composition.ContainerVisual? Visual { get; private set; }

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

        // 背景图缓存 (静态复用，避免每条弹幕重复加载)
        private static string _cachedBgImagePath = "";
        private static CanvasBitmap? _cachedBgImage = null;
        private static readonly object _bgImageLock = new object();

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

        private bool _showBackground;
        private float _cornerRadius;
        private bool _isUnderlined;
        
        // 渲染时使用的背景图设置
        private bool _showBgImage;
        private ImageAnchor _bgAnchor;
        private double _bgOffsetX;
        private double _bgOffsetY;
        private double _bgScale;
        private bool _bgKeepBaseColor;
        private double _bgImageOpacity;
        private bool _showTextStroke;
        private Windows.UI.Color _textStrokeColor;
        private double _textStrokeThickness;

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
        public void PrepareLayout(CanvasDevice device,
            string appName, string title, string body,
            BarrageConfigDto config,
            byte[]? iconPixels, int iconWidth, int iconHeight, bool isUwpIcon)
        {
            double globalFontSize = config.FontSize > 0 ? config.FontSize : 36;
            _iconSize = globalFontSize * 1.25 * (config.AppIconScale > 0 ? config.AppIconScale : 1.0);
            _contentWidth = 0;

            _hasIcon = config.ShowAppIcon && iconPixels != null && iconWidth > 0 && iconHeight > 0;
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
            int appNameStart = -1, appNameLen = 0;
            if (config.ShowAppName && !string.IsNullOrEmpty(appName))
            {
                appNameStart = 0;
                appName = appName.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                appNameLen = appName.Length;
                prefix += appName;
            }
            if (!string.IsNullOrEmpty(title))
            {
                title = title.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                if (prefix.Length > 0) prefix += " ";
                prefix += title;
            }
            if (prefix.Length > 0)
            {
                prefix += "：";
            }

            string bodyText = body.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            double currentWeight = 0;
            int truncateIndex = -1;
            for (int i = 0; i < bodyText.Length; i++)
            {
                char c = bodyText[i];
                currentWeight += (c <= 127) ? 0.5 : 1.0;
                if (currentWeight > config.MaxTextLength)
                {
                    truncateIndex = i;
                    break;
                }
            }
            
            bool hasEllipsis = false;
            if (truncateIndex != -1)
            {
                bodyText = bodyText.Substring(0, truncateIndex) + "......";
                hasEllipsis = true;
            }

            string fullText = prefix + bodyText;
            
            Windows.UI.Color ParseColor(string hex, Windows.UI.Color defaultColor)
            {
                if (string.IsNullOrEmpty(hex)) return defaultColor;
                try {
                    var mcolor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    return Windows.UI.Color.FromArgb((byte)(mcolor.A * config.TextOpacity), mcolor.R, mcolor.G, mcolor.B);
                } catch { return defaultColor; }
            }
            
            Windows.UI.Color ParseColorExact(string hex, Windows.UI.Color defaultColor, double opacity)
            {
                if (string.IsNullOrEmpty(hex)) return defaultColor;
                try {
                    var mcolor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    return Windows.UI.Color.FromArgb((byte)(mcolor.A * opacity), mcolor.R, mcolor.G, mcolor.B);
                } catch { return defaultColor; }
            }

            _textColor = ParseColor(config.TextColorHex, Windows.UI.Color.FromArgb((byte)(255 * config.TextOpacity), 255, 255, 255));
            _backgroundColor = ParseColorExact(config.BackgroundColorHex, Windows.UI.Color.FromArgb((byte)(255 * config.BackgroundOpacity), 0, 0, 0), config.BackgroundOpacity);

            var textFormat = new CanvasTextFormat
            {
                FontFamily = config.FontFamilyName + ", Segoe UI Emoji",
                FontSize = (float)globalFontSize,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(config.FontWeight == "Bold" ? System.Windows.FontWeights.Bold.ToOpenTypeWeight() : System.Windows.FontWeights.Normal.ToOpenTypeWeight()) },
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            if (config.FontStyle == "Italic")
            {
                textFormat.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }

            _textLayout = new CanvasTextLayout(device, fullText, textFormat, 0.0f, 0.0f);
            
            // --- Apply Per-Part Styling ---
            
            // 1. AppName
            if (appNameStart >= 0 && appNameLen > 0)
            {
                double appOpacity = config.AppNameTextOpacity ?? config.TextOpacity;
                var appColor = ParseColorExact(string.IsNullOrEmpty(config.AppNameTextColorHex) ? config.TextColorHex : config.AppNameTextColorHex, _textColor, appOpacity);
                _textLayout.SetBrush(appNameStart, appNameLen, new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(device, appColor));
                
                if (config.AppNameFontSize > 0) _textLayout.SetFontSize(appNameStart, appNameLen, (float)config.AppNameFontSize);
                if (!string.IsNullOrEmpty(config.AppNameFontWeight)) _textLayout.SetFontWeight(appNameStart, appNameLen, new Windows.UI.Text.FontWeight { Weight = (ushort)(config.AppNameFontWeight == "Bold" ? System.Windows.FontWeights.Bold.ToOpenTypeWeight() : System.Windows.FontWeights.Normal.ToOpenTypeWeight()) });
                if (!string.IsNullOrEmpty(config.AppNameFontStyle)) _textLayout.SetFontStyle(appNameStart, appNameLen, config.AppNameFontStyle == "Italic" ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal);
                if (!string.IsNullOrEmpty(config.AppNameFontFamilyName)) _textLayout.SetFontFamily(appNameStart, appNameLen, config.AppNameFontFamilyName + ", Segoe UI Emoji");
                if (config.AppNameIsUnderlined) _textLayout.SetUnderline(appNameStart, appNameLen, true);
                  if (config.AppNameLetterSpacing > 0)
                {
                    _textLayout.SetCharacterSpacing(appNameStart, appNameLen, 0, (float)config.AppNameLetterSpacing, 0);
                }
            }
            
            // 2. Content
            int contentStart = appNameLen > 0 ? appNameLen : 0;
            int contentLen = fullText.Length - contentStart - (hasEllipsis ? 6 : 0);
            if (contentLen > 0)
            {
                if (!string.IsNullOrEmpty(config.ContentTextColorHex)) {
                    double contentOpacity = config.ContentTextOpacity ?? config.TextOpacity;
                    var color = ParseColorExact(string.IsNullOrEmpty(config.ContentTextColorHex) ? config.TextColorHex : config.ContentTextColorHex, _textColor, contentOpacity);
                    _textLayout.SetBrush(contentStart, contentLen, new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(device, color));
                }
                if (config.ContentFontSize > 0) _textLayout.SetFontSize(contentStart, contentLen, (float)config.ContentFontSize);
                if (!string.IsNullOrEmpty(config.ContentFontFamilyName)) _textLayout.SetFontFamily(contentStart, contentLen, config.ContentFontFamilyName + ", Segoe UI Emoji");
                if (config.ContentLetterSpacing > 0) _textLayout.SetCharacterSpacing(contentStart, contentLen, 0, (float)config.ContentLetterSpacing, 0);
                if (!string.IsNullOrEmpty(config.ContentFontWeight)) _textLayout.SetFontWeight(contentStart, contentLen, new Windows.UI.Text.FontWeight { Weight = (ushort)(config.ContentFontWeight == "Bold" ? System.Windows.FontWeights.Bold.ToOpenTypeWeight() : System.Windows.FontWeights.Normal.ToOpenTypeWeight()) });
                if (!string.IsNullOrEmpty(config.ContentFontStyle)) _textLayout.SetFontStyle(contentStart, contentLen, config.ContentFontStyle == "Italic" ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal);
                  if (config.ContentIsUnderlined) _textLayout.SetUnderline(contentStart, contentLen, true);
            }
            
            // 3. Ellipsis
            if (hasEllipsis)
            {
                int ellStart = fullText.Length - 6;
                int ellLen = 6;
                
                Windows.UI.Color ellColor = _textColor;
                double ellOpacity = config.EllipsisTextOpacity ?? config.TextOpacity;
                if (config.HighlightEllipsis && !string.IsNullOrEmpty(config.EllipsisColorHex)) 
                    ellColor = ParseColorExact(config.EllipsisColorHex, _textColor, ellOpacity);
                else if (!string.IsNullOrEmpty(config.ContentTextColorHex))
                    ellColor = ParseColorExact(config.ContentTextColorHex, _textColor, ellOpacity);
                    
                _textLayout.SetBrush(ellStart, ellLen, new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(device, ellColor));
                
                if (config.EllipsisFontSize > 0) _textLayout.SetFontSize(ellStart, ellLen, (float)config.EllipsisFontSize);
                else if (config.ContentFontSize > 0) _textLayout.SetFontSize(ellStart, ellLen, (float)config.ContentFontSize);
                
                if (!string.IsNullOrEmpty(config.ContentFontWeight)) _textLayout.SetFontWeight(ellStart, ellLen, new Windows.UI.Text.FontWeight { Weight = (ushort)(config.ContentFontWeight == "Bold" ? System.Windows.FontWeights.Bold.ToOpenTypeWeight() : System.Windows.FontWeights.Normal.ToOpenTypeWeight()) });
                if (!string.IsNullOrEmpty(config.ContentFontStyle)) _textLayout.SetFontStyle(ellStart, ellLen, config.ContentFontStyle == "Italic" ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal);
                  if (config.EllipsisIsUnderlined) _textLayout.SetUnderline(ellStart, ellLen, true);
            }

            if (config.LetterSpacing > 0 && fullText.Length > 0)
            {
                _textLayout.SetCharacterSpacing(0, fullText.Length, 0, (float)config.LetterSpacing, 0);
            }

            double textWidth = _textLayout.LayoutBounds.Width;
            _contentWidth += textWidth;
            
            double maxFontSize = Math.Max(globalFontSize, Math.Max(config.AppNameFontSize, Math.Max(config.ContentFontSize, config.EllipsisFontSize)));
            _contentHeight = Math.Max(maxFontSize, _iconSize);

            _bgWidth = _contentWidth + _padH * 2;
            _bgHeight = _contentHeight + _padV * 2;

            this.PhysicalWidth = config.ShowBackground ? _bgWidth : _contentWidth;

            _showBackground = config.ShowBackground;
            _cornerRadius = (float)config.BackgroundCornerRadius;
            _isUnderlined = config.IsUnderlined;
            
            _showBgImage = config.ShowBackgroundImage;
            _bgAnchor = config.BackgroundImageAnchor;
            _bgOffsetX = config.BackgroundImageOffsetX;
            _bgOffsetY = config.BackgroundImageOffsetY;
            _bgScale = config.BackgroundImageScale;
            _bgKeepBaseColor = config.BackgroundImageKeepBaseColor;
            _bgImageOpacity = config.BackgroundImageOpacity;
            _showTextStroke = config.ShowTextStroke;
            _textStrokeColor = ParseColorExact(config.TextStrokeColorHex, Windows.UI.Color.FromArgb((byte)(255 * config.TextOpacity), 0, 0, 0), config.TextOpacity);
            _textStrokeThickness = config.TextStrokeThickness;

            if (_showBgImage && !string.IsNullOrEmpty(config.BackgroundImagePath))
            {
                lock (_bgImageLock)
                {
                    if (_cachedBgImagePath != config.BackgroundImagePath)
                    {
                        _cachedBgImage?.Dispose();
                        _cachedBgImage = null;
                        try
                        {
                            _cachedBgImage = CanvasBitmap.LoadAsync(device, config.BackgroundImagePath).GetAwaiter().GetResult();
                            _cachedBgImagePath = config.BackgroundImagePath;
                        }
                        catch { }
                    }
                }
            }
        }

        public void CreateVisualForComposition(CanvasDevice device, Compositor compositor,
            CompositionGraphicsDevice graphicsDevice)
        {
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
                
                // 【核心修复】CompositionDrawingSurface 可能是大图集的一部分，必须应用 BeginDraw 返回的 offset！
                var offset = wrapper.Offset;
                session.Transform = System.Numerics.Matrix3x2.CreateTranslation(offset.x, offset.y);

                // 使用 Copy 混合模式擦除我们分配的区域（绝对不能用 session.Clear()，那会清空整个图集！）
                var previousBlend = session.Blend;
                session.Blend = Microsoft.Graphics.Canvas.CanvasBlend.Copy;
                session.FillRectangle(0, 0, surfaceWidth, surfaceHeight, Windows.UI.Color.FromArgb(0, 0, 0, 0));
                session.Blend = previousBlend;

                // 临时设置绘制坐标为精灵图原点
                double savedX = CurrentX, savedY = CurrentY;
                CurrentX = spriteMargin;
                CurrentY = spriteMargin;

                Draw(session);

                CurrentX = savedX;
                CurrentY = savedY;
            }

            // 将 SpriteVisual 挂载到 Composition
            var surfaceBrush = compositor.CreateSurfaceBrush(_surface);
            surfaceBrush.Stretch = CompositionStretch.None;
            var contentVisual = compositor.CreateSpriteVisual();
            contentVisual.Size = new Vector2(surfaceWidth, surfaceHeight);
            contentVisual.Brush = surfaceBrush;

            var root = compositor.CreateContainerVisual();
            root.Size = contentVisual.Size;



            root.Children.InsertAtTop(contentVisual);
            Visual = root;
        }

        public void Draw(CanvasDrawingSession session)
        {
            if (_textLayout == null) return;

            float drawX = (float)CurrentX;
            float drawY = (float)CurrentY;

            if (_showBackground)
            {
                if (!_showBgImage || _bgKeepBaseColor)
                {
                    session.FillRoundedRectangle(drawX, drawY, (float)_bgWidth, (float)_bgHeight, _cornerRadius, _cornerRadius, _backgroundColor);
                }
                
                if (_showBgImage && _cachedBgImage != null)
                {
                    double imgW = _cachedBgImage.Size.Width * _bgScale;
                    double imgH = _cachedBgImage.Size.Height * _bgScale;
                    double destX = drawX;
                    double destY = drawY;
                    
                    switch (_bgAnchor)
                    {
                        case ImageAnchor.TopLeft:
                        case ImageAnchor.MiddleLeft:
                        case ImageAnchor.BottomLeft:
                            destX += _bgOffsetX;
                            break;
                        case ImageAnchor.TopCenter:
                        case ImageAnchor.MiddleCenter:
                        case ImageAnchor.BottomCenter:
                            destX += (_bgWidth - imgW) / 2.0 + _bgOffsetX;
                            break;
                        case ImageAnchor.TopRight:
                        case ImageAnchor.MiddleRight:
                        case ImageAnchor.BottomRight:
                            destX += _bgWidth - imgW - _bgOffsetX;
                            break;
                    }
                    
                    switch (_bgAnchor)
                    {
                        case ImageAnchor.TopLeft:
                        case ImageAnchor.TopCenter:
                        case ImageAnchor.TopRight:
                            destY += _bgOffsetY;
                            break;
                        case ImageAnchor.MiddleLeft:
                        case ImageAnchor.MiddleCenter:
                        case ImageAnchor.MiddleRight:
                            destY += (_bgHeight - imgH) / 2.0 + _bgOffsetY;
                            break;
                        case ImageAnchor.BottomLeft:
                        case ImageAnchor.BottomCenter:
                        case ImageAnchor.BottomRight:
                            destY += _bgHeight - imgH - _bgOffsetY;
                            break;
                    }

                    // 创建一个圆角矩形的 Geometry 用来裁剪背景图，防止图片超出背景区域
                    using (var geometry = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateRoundedRectangle(session.Device, drawX, drawY, (float)_bgWidth, (float)_bgHeight, _cornerRadius, _cornerRadius))
                    using (session.CreateLayer(1.0f, geometry))
                    {
                        session.DrawImage(_cachedBgImage, new Windows.Foundation.Rect(destX, destY, imgW, imgH), _cachedBgImage.Bounds, (float)_bgImageOpacity, Microsoft.Graphics.Canvas.CanvasImageInterpolation.Linear);
                    }
                }
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

            if (_showTextStroke && _textStrokeThickness > 0)
            {
                using (var geom = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreateText(_textLayout))
                {
                    session.DrawGeometry(geom, contentX, textY, _textStrokeColor, (float)_textStrokeThickness);
                }
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
