using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NotiFlow.Views.Controls
{
    /// <summary>
    /// 支持高质量矢量描边（Outer Stroke）的文字渲染控件。
    /// 通过 FormattedText 提取文字几何轮廓，底层绘制加粗圆润边框，顶层填充文字字芯，实现绝对清晰锐利的描边效果。
    /// </summary>
    public class OutlinedTextBlock : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(new FontFamily("Microsoft YaHei"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FontStyleProperty =
            DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsUnderlinedProperty =
            DependencyProperty.Register(nameof(IsUnderlined), typeof(bool), typeof(OutlinedTextBlock),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush? Stroke
        {
            get => (Brush?)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public FontWeight FontWeight
        {
            get => (FontWeight)GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public FontStyle FontStyle
        {
            get => (FontStyle)GetValue(FontStyleProperty);
            set => SetValue(FontStyleProperty, value);
        }

        public bool IsUnderlined
        {
            get => (bool)GetValue(IsUnderlinedProperty);
            set => SetValue(IsUnderlinedProperty, value);
        }

        private FormattedText CreateFormattedText()
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            return new FormattedText(
                Text ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
                FontSize > 0 ? FontSize : 36.0,
                Brushes.Black,
                pixelsPerDip);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (string.IsNullOrEmpty(Text)) return new Size(0, 0);

            var formatted = CreateFormattedText();
            double extra = Stroke != null && StrokeThickness > 0 ? StrokeThickness * 2.0 : 0.0;
            return new Size(formatted.WidthIncludingTrailingWhitespace + extra, formatted.Height + extra);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(Text)) return;

            var formatted = CreateFormattedText();
            double offset = Stroke != null && StrokeThickness > 0 ? StrokeThickness : 0.0;
            var origin = new Point(offset, offset);

            var geometry = formatted.BuildGeometry(origin);
            if (geometry != null)
            {
                // 1. 底层绘制加粗圆润边框（两倍粗细，内侧被字芯覆盖，外侧形成绝对精确的外描边）
                if (Stroke != null && StrokeThickness > 0)
                {
                    var pen = new Pen(Stroke, StrokeThickness * 2.0)
                    {
                        LineJoin = PenLineJoin.Round,
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    pen.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                }

                // 2. 顶层绘制饱满字芯填充
                dc.DrawGeometry(Fill ?? Brushes.White, null, geometry);

                // 3. 下划线
                if (IsUnderlined)
                {
                    double lineY = origin.Y + formatted.Height + 2.0;
                    var underlinePen = new Pen(Fill ?? Brushes.White, 2.0);
                    underlinePen.Freeze();
                    dc.DrawLine(underlinePen, new Point(origin.X, lineY), new Point(origin.X + formatted.WidthIncludingTrailingWhitespace, lineY));
                }
            }
        }
    }
}
