using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace NotiFlow.Models
{
    /// <summary>
    /// 代表一个底层视觉图层的轻量级弹幕对象。
    /// 抛弃了厚重的控件树结构，通过 Win2D 纯像素离屏绘制。
    /// </summary>
    public class BarrageItem : IDisposable
    {
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

        public void Reset()
        {
            IsAlive = true;
            TrackReleased = false;
            CurrentX = 0;
            CurrentY = 0;
            SpeedPixelsPerSec = 0;
            PhysicalWidth = 0;
            TrackIndex = -1;

            _textLayout?.Dispose();
            _textLayout = null;

            _appIcon?.Dispose();
            _appIcon = null;
        }

        public void BuildVisual(CanvasDevice device, NotificationMessage message, SolidColorBrush textBrush, double fontSize, System.Windows.Media.FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight)
        {
            _iconSize = fontSize * 1.25;
            _contentWidth = 0;

            _hasIcon = BarrageSettings.ShowAppIcon && message.AppIcon != null;
            if (_hasIcon)
            {
                _contentWidth += _iconSize + 10;
                _isUwpIcon = message.IsUwpIcon;
                _appIcon = ConvertToCanvasBitmap(device, message.AppIcon!);
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

            _textColor = Windows.UI.Color.FromArgb(
                (byte)(textBrush.Color.A * BarrageSettings.TextOpacity), 
                textBrush.Color.R, textBrush.Color.G, textBrush.Color.B);

            var bgBrush = BarrageSettings.BackgroundColor as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Colors.Black);
            _backgroundColor = Windows.UI.Color.FromArgb(
                (byte)(bgBrush.Color.A * BarrageSettings.BackgroundOpacity),
                bgBrush.Color.R, bgBrush.Color.G, bgBrush.Color.B);

            var textFormat = new CanvasTextFormat
            {
                // 推荐注入 Segoe UI Emoji 以完美渲染彩色 Emoji
                FontFamily = fontFamily.Source + ", Segoe UI Emoji",
                FontSize = (float)fontSize,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)fontWeight.ToOpenTypeWeight() },
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            // 如果 WPF 传入的是 Italic 等，可以进一步适配，这里为简略直接赋默认，因为大部分都是 Normal
            if (fontStyle == FontStyles.Italic)
            {
                textFormat.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }

            _textLayout = new CanvasTextLayout(device, fullText, textFormat, 0.0f, 0.0f);

            if (BarrageSettings.HighlightEllipsis && fullText.EndsWith("......"))
            {
                var ellBrush = BarrageSettings.EllipsisColor as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Colors.White);
                Windows.UI.Color ellipsisColor = Windows.UI.Color.FromArgb(
                    (byte)(ellBrush.Color.A * BarrageSettings.TextOpacity),
                    ellBrush.Color.R, ellBrush.Color.G, ellBrush.Color.B);
                
                // 给最后6个字符上高亮颜色 (注意：SetColor 并非原生，需要使用 SetBrush)
                var brush = new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(device, ellipsisColor);
                _textLayout.SetBrush(fullText.Length - 6, 6, brush);
            }

            double textWidth = _textLayout.LayoutBounds.Width;
            double textHeight = _textLayout.LayoutBounds.Height;
            _contentWidth += textWidth;
            _contentHeight = Math.Max(fontSize, _iconSize);

            _bgWidth = _contentWidth + _padH * 2;
            _bgHeight = _contentHeight + _padV * 2;

            this.PhysicalWidth = BarrageSettings.ShowBackground ? _bgWidth : _contentWidth;
        }

        public void Draw(CanvasDrawingSession session)
        {
            if (_textLayout == null) return;

            float drawX = (float)CurrentX;
            float drawY = (float)CurrentY;

            if (BarrageSettings.ShowBackground)
            {
                float cornerRadius = (float)BarrageSettings.BackgroundCornerRadius.TopLeft;
                session.FillRoundedRectangle(drawX, drawY, (float)_bgWidth, (float)_bgHeight, cornerRadius, cornerRadius, _backgroundColor);
            }

            float contentX = drawX + (BarrageSettings.ShowBackground ? (float)_padH : 0);
            float contentY = drawY + (BarrageSettings.ShowBackground ? (float)_padV : 0);

            if (_hasIcon && _appIcon != null)
            {
                float imageX = contentX;
                float imageY = contentY + (float)(_contentHeight - _iconSize) / 2.0f;

                if (_isUwpIcon)
                {
                    float centerX = imageX + (float)_iconSize / 2.0f;
                    float centerY = imageY + (float)_iconSize / 2.0f;
                    var oldTransform = session.Transform;
                    session.Transform = System.Numerics.Matrix3x2.CreateScale((float)UwpIconScaleFactor, (float)UwpIconScaleFactor, new System.Numerics.Vector2(centerX, centerY)) * oldTransform;
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

            if (!BarrageSettings.ShowBackground)
            {
                Windows.UI.Color shadowColor = Windows.UI.Color.FromArgb((byte)(0.9 * _textColor.A), 0, 0, 0);
                session.DrawTextLayout(_textLayout, contentX + 1.5f, textY + 1.5f, shadowColor);
            }

            session.DrawTextLayout(_textLayout, contentX, textY, _textColor);

            if (BarrageSettings.IsUnderlined)
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
                    return CanvasBitmap.CreateFromBytes(device, pixels, width, height, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
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
        }
    }
}
