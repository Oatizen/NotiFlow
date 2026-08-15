using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using NotiFlow.Models;

namespace NotiFlow.Views.Pages
{
    public partial class CustomPage : Page
    {
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private readonly SolidColorBrush _whiteBrush;

        public CustomPage()
        {
            InitializeComponent();
            _whiteBrush = new SolidColorBrush(Colors.White);
            _whiteBrush.Freeze();
        }


        private static void OnCurrentScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CustomPage page && page.MainScrollViewer != null)
            {
                page.MainScrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
        

private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWorkButtonState(); // 刚进入页面时先校准一次当前实际状态
            
            WeakReferenceMessenger.Default.Register<BarragePreviewMessage>(this, (recipient, message) =>
            {
                SpawnPreviewBarrage();
            });

            WeakReferenceMessenger.Default.Register<WorkStateChangedMessage>(this, (recipient, message) =>
            {
                // Ensure UI update happens on the main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateWorkButtonState();
                });
            });

            if (PreviewBorder.ActualWidth > 0)
            {
                SpawnPreviewBarrage();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            WeakReferenceMessenger.Default.Unregister<BarragePreviewMessage>(this);
            WeakReferenceMessenger.Default.Unregister<WorkStateChangedMessage>(this);
        }

        private void PausePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (PausePreviewButton.IsChecked == true)
            {
                PausePreviewButton.Content = "恢复滚动";
            }
            else
            {
                PausePreviewButton.Content = "暂停以编辑";
            }
            SpawnPreviewBarrage();
        }

                                private void ApplyConfigToTextBlock(TextBlock tb, BarrageConfigDto config, bool isAppName, bool isEllipsis)
        {
            string globalHex = config.TextColorHex ?? "#FFFFFF";
            string hex = globalHex;
            double fontSize = config.FontSize;
            string fontWeight = config.FontWeight;
            string fontStyle = config.FontStyle;
            string fontFamilyName = config.FontFamilyName;
            bool isUnderlined = config.IsUnderlined;
            double opacity = config.TextOpacity;
            bool showStroke = config.ShowTextStroke;
            
            if (isAppName)
            {
                if (!string.IsNullOrEmpty(config.AppNameTextColorHex)) hex = config.AppNameTextColorHex;
                if (config.AppNameFontSize > 0) fontSize = config.AppNameFontSize;
                if (!string.IsNullOrEmpty(config.AppNameFontWeight)) fontWeight = config.AppNameFontWeight;
                if (!string.IsNullOrEmpty(config.AppNameFontStyle)) fontStyle = config.AppNameFontStyle;
                if (!string.IsNullOrEmpty(config.AppNameFontFamilyName)) fontFamilyName = config.AppNameFontFamilyName;
                isUnderlined = config.AppNameIsUnderlined;
                if (config.AppNameTextOpacity.HasValue) opacity = config.AppNameTextOpacity.Value;
                else opacity = config.TextOpacity;
                if (config.AppNameShowTextStroke.HasValue) showStroke = config.AppNameShowTextStroke.Value;
                else showStroke = config.ShowTextStroke;
            }
            else if (!isEllipsis)
            {
                if (!string.IsNullOrEmpty(config.ContentTextColorHex)) hex = config.ContentTextColorHex;
                if (config.ContentFontSize > 0) fontSize = config.ContentFontSize;
                if (!string.IsNullOrEmpty(config.ContentFontWeight)) fontWeight = config.ContentFontWeight;
                if (!string.IsNullOrEmpty(config.ContentFontStyle)) fontStyle = config.ContentFontStyle;
                if (!string.IsNullOrEmpty(config.ContentFontFamilyName)) fontFamilyName = config.ContentFontFamilyName;
                isUnderlined = config.ContentIsUnderlined;
                if (config.ContentTextOpacity.HasValue) opacity = config.ContentTextOpacity.Value;
                else opacity = config.TextOpacity;
                if (config.ContentShowTextStroke.HasValue) showStroke = config.ContentShowTextStroke.Value;
                else showStroke = config.ShowTextStroke;
            }
            else
            {
                if (config.HighlightEllipsis && !string.IsNullOrEmpty(config.EllipsisColorHex)) hex = config.EllipsisColorHex;
                else if (!string.IsNullOrEmpty(config.ContentTextColorHex)) hex = config.ContentTextColorHex;
                
                if (config.EllipsisFontSize > 0) fontSize = config.EllipsisFontSize;
                else if (config.ContentFontSize > 0) fontSize = config.ContentFontSize;
                
                if (!string.IsNullOrEmpty(config.ContentFontWeight)) fontWeight = config.ContentFontWeight;
                if (!string.IsNullOrEmpty(config.ContentFontStyle)) fontStyle = config.ContentFontStyle;
                if (!string.IsNullOrEmpty(config.ContentFontFamilyName)) fontFamilyName = config.ContentFontFamilyName;
                isUnderlined = config.EllipsisIsUnderlined;
                if (config.EllipsisTextOpacity.HasValue) opacity = config.EllipsisTextOpacity.Value;
                else opacity = config.TextOpacity;
                showStroke = config.ShowTextStroke;
            }

            try { tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFF")); }
            catch { tb.Foreground = Brushes.White; }

            tb.FontSize = fontSize > 0 ? fontSize : 36;
            tb.Opacity = opacity;
            
            bool fontExists = false;
            foreach(var f in Fonts.SystemFontFamilies) { if (f.Source.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase)) { fontExists = true; break; } }
            tb.FontFamily = fontExists ? new FontFamily(fontFamilyName) : new FontFamily("Microsoft YaHei");
            
            try { tb.FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(fontWeight); } catch { tb.FontWeight = FontWeights.Normal; }
            try { tb.FontStyle = (FontStyle)new FontStyleConverter().ConvertFromString(fontStyle); } catch { tb.FontStyle = FontStyles.Normal; }

            if (isUnderlined) tb.TextDecorations = TextDecorations.Underline;
            else tb.TextDecorations = null;

            if (showStroke)
            {
                Color strokeColor = Colors.Black;
                try
                {
                    if (!string.IsNullOrEmpty(config.TextStrokeColorHex))
                        strokeColor = (Color)ColorConverter.ConvertFromString(config.TextStrokeColorHex);
                }
                catch {}

                double thickness = config.TextStrokeThickness > 0 ? config.TextStrokeThickness : 1.0;

                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = strokeColor,
                    BlurRadius = Math.Max(3.0, thickness * 3.0),
                    ShadowDepth = 0,
                    Opacity = 1.0,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
                };
            }
            else
            {
                tb.Effect = null;
            }
        }

private void SpawnPreviewBarrage()
        {
            PreviewCanvas.Children.Clear();
            var vm = DataContext as SettingsViewModel;
            var config = vm?.GetCurrentConfig() ?? BarrageSettings.GetGlobalConfigDto();
            bool isPaused = PausePreviewButton.IsChecked == true;
            
            var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            
            if (config.ShowAppIcon)
            {
                double iconSize = config.FontSize * 1.25 * (config.AppIconScale > 0 ? config.AppIconScale : 1.0);
                var iconBorder = new System.Windows.Controls.Border { Width = iconSize, Height = iconSize, Background = System.Windows.Media.Brushes.Gray, Margin = new System.Windows.Thickness(0,0,10,0), CornerRadius = new System.Windows.CornerRadius(4) };
                if (isPaused) {
                    iconBorder.Cursor = System.Windows.Input.Cursors.Hand;
                    iconBorder.MouseLeftButtonUp += (s, e) => {
                        OpenFlyout("AppIconFlyout", iconBorder);
                        e.Handled = true;
                    };
                }
                stack.Children.Add(iconBorder);
            }

            if (config.ShowAppName)
            {
                var tbApp = new TextBlock { Text = "应用名称：", VerticalAlignment = VerticalAlignment.Center };
                ApplyConfigToTextBlock(tbApp, config, true, false);
                if (isPaused) {
                    tbApp.Cursor = Cursors.Hand;
                    tbApp.MouseLeftButtonUp += (s, e) => {
                        OpenFlyout("AppNameFlyout", tbApp);
                        e.Handled = true;
                    };
                }
                stack.Children.Add(tbApp);
            }
            
            var tbContent = new TextBlock { Text = "这是一条测试弹幕", VerticalAlignment = VerticalAlignment.Center };
            ApplyConfigToTextBlock(tbContent, config, false, false);
            if (isPaused) {
                tbContent.Cursor = Cursors.Hand;
                tbContent.MouseLeftButtonUp += (s, e) => {
                    OpenFlyout("ContentFlyout", tbContent);
                    e.Handled = true;
                };
            }
            stack.Children.Add(tbContent);
            
            var tbEllipsis = new TextBlock { Text = "......", VerticalAlignment = VerticalAlignment.Center };
            ApplyConfigToTextBlock(tbEllipsis, config, false, true);
            if (isPaused) {
                tbEllipsis.Cursor = Cursors.Hand;
                tbEllipsis.MouseLeftButtonUp += (s, e) => {
                    OpenFlyout("EllipsisFlyout", tbEllipsis);
                    e.Handled = true;
                };
            }
            stack.Children.Add(tbEllipsis);
            
            
            // Re-attach popups if they are open
            if (((System.Windows.Controls.Primitives.Popup)this.Resources["AppIconFlyout"])?.IsOpen == true && config.ShowAppIcon) { OpenFlyout("AppIconFlyout", (UIElement)stack.Children[0], true); }
            
            int appNameIndex = config.ShowAppIcon ? 1 : 0;
            if (((System.Windows.Controls.Primitives.Popup)this.Resources["AppNameFlyout"])?.IsOpen == true && config.ShowAppName) { OpenFlyout("AppNameFlyout", (UIElement)stack.Children[appNameIndex], true); }
            
            int contentIndex = appNameIndex + (config.ShowAppName ? 1 : 0);
            if (((System.Windows.Controls.Primitives.Popup)this.Resources["ContentFlyout"])?.IsOpen == true) { OpenFlyout("ContentFlyout", (UIElement)stack.Children[contentIndex], true); }
            
            int ellipsisIndex = contentIndex + 1;
            if (((System.Windows.Controls.Primitives.Popup)this.Resources["EllipsisFlyout"])?.IsOpen == true) { OpenFlyout("EllipsisFlyout", (UIElement)stack.Children[ellipsisIndex], true); }
            
            UIElement textElement = stack;


            Color bgBrushColor = Colors.Black;
            try { bgBrushColor = (Color)ColorConverter.ConvertFromString(config.BackgroundColorHex ?? "#000000"); } catch {}
            Brush baseBgBrush = config.ShowBackground ? new SolidColorBrush(Color.FromArgb(
                (byte)(255 * config.BackgroundOpacity),
                bgBrushColor.R, bgBrushColor.G, bgBrushColor.B)) : Brushes.Transparent;

            var containerGrid = new Grid();

            if (config.ShowBackground)
            {
                if (!config.ShowBackgroundImage || config.BackgroundImageKeepBaseColor)
                {
                    containerGrid.Children.Add(new Border
                    {
                        Background = baseBgBrush,
                        CornerRadius = new CornerRadius(config.BackgroundCornerRadius)
                    });
                }
            }

            if (config.ShowBackgroundImage && System.IO.File.Exists(config.BackgroundImagePath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    using (var stream = new System.IO.FileStream(config.BackgroundImagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    {
                        bmp.StreamSource = stream;
                        bmp.EndInit();
                    }
                    bmp.Freeze();

                    var canvas = new System.Windows.Controls.Canvas { ClipToBounds = true };
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = System.Windows.Media.Stretch.Fill,
                        Width = bmp.PixelWidth * (config.BackgroundImageScale > 0 ? config.BackgroundImageScale : 1.0),
                        Height = bmp.PixelHeight * (config.BackgroundImageScale > 0 ? config.BackgroundImageScale : 1.0)
                    };
                    canvas.Children.Add(img);

                    containerGrid.SizeChanged += (s, ev) =>
                    {
                        double w = ev.NewSize.Width;
                        double h = ev.NewSize.Height;
                        double scaledW = img.Width;
                        double scaledH = img.Height;
                        double x = 0, y = 0;

                        containerGrid.Clip = new System.Windows.Media.RectangleGeometry(
                            new System.Windows.Rect(0, 0, w, h), 
                            config.BackgroundCornerRadius, 
                            config.BackgroundCornerRadius);

                        switch (config.BackgroundImageAnchor)
                        {
                            case ImageAnchor.TopLeft:
                            case ImageAnchor.MiddleLeft:
                            case ImageAnchor.BottomLeft:
                                x += config.BackgroundImageOffsetX;
                                break;
                            case ImageAnchor.TopCenter:
                            case ImageAnchor.MiddleCenter:
                            case ImageAnchor.BottomCenter:
                                x += (w - scaledW) / 2 + config.BackgroundImageOffsetX;
                                break;
                            case ImageAnchor.TopRight:
                            case ImageAnchor.MiddleRight:
                            case ImageAnchor.BottomRight:
                                x += w - scaledW - config.BackgroundImageOffsetX;
                                break;
                        }

                        switch (config.BackgroundImageAnchor)
                        {
                            case ImageAnchor.TopLeft:
                            case ImageAnchor.TopCenter:
                            case ImageAnchor.TopRight:
                                y += config.BackgroundImageOffsetY;
                                break;
                            case ImageAnchor.MiddleLeft:
                            case ImageAnchor.MiddleCenter:
                            case ImageAnchor.MiddleRight:
                                y += (h - scaledH) / 2 + config.BackgroundImageOffsetY;
                                break;
                            case ImageAnchor.BottomLeft:
                            case ImageAnchor.BottomCenter:
                            case ImageAnchor.BottomRight:
                                y += h - scaledH - config.BackgroundImageOffsetY;
                                break;
                        }

                        System.Windows.Controls.Canvas.SetLeft(img, x);
                        System.Windows.Controls.Canvas.SetTop(img, y);
                    };

                    containerGrid.Children.Add(canvas);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Preview image load failed: " + ex.Message);
                }
            }

            containerGrid.Children.Add(new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                Child = textElement
            });

            var border = new Border
            {
                Child = containerGrid
            };

            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double itemWidth = border.DesiredSize.Width > 0 ? border.DesiredSize.Width : 400;

            if (isPaused)
            {
                Canvas.SetLeft(border, (PreviewBorder.ActualWidth - itemWidth) / 2.0);
                Canvas.SetTop(border, Math.Max(0, (PreviewBorder.ActualHeight - border.DesiredSize.Height) / 2.0));
                PreviewCanvas.Children.Add(border);
            }
            else
            {
                Canvas.SetLeft(border, PreviewBorder.ActualWidth);
                Canvas.SetTop(border, Math.Max(0, (PreviewBorder.ActualHeight - border.DesiredSize.Height) / 2.0));
                PreviewCanvas.Children.Add(border);

                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = PreviewBorder.ActualWidth,
                    To = -itemWidth,
                    Duration = TimeSpan.FromSeconds(Math.Max(3, (PreviewBorder.ActualWidth + itemWidth) / (config.ScrollSpeedCharsPerSec * config.FontSize))),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                border.BeginAnimation(Canvas.LeftProperty, animation);
            }
        }

        private void ToggleWorkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 在内存中取反实际渲染开关，并更新所有与之相连的状态组件
            BarrageSettings.IsWorking = !BarrageSettings.IsWorking;
            UpdateWorkButtonState();

            // 同步刷新托盘图标菜单状态与主窗口可见性
            if (Application.Current is App app)
            {
                app.RefreshTrayState();
            }
        }

        private void UpdateWorkButtonState()
        {
            if (BarrageSettings.IsWorking)
            {
                ToggleWorkButton.Content = "工作中";
                ToggleWorkButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            else
            {
                ToggleWorkButton.Content = "开启";
                ToggleWorkButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
        }

        private void HelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            HelpFlyout.Show();
        }

        private void ColorPickerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ColorPaletteFlyout.Show();
        }

        private void TextStrokeColorPickerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            TextStrokeColorPaletteFlyout.Show();
        }

        private void BackgroundColorPickerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BackgroundColorPaletteFlyout.Show();
        }

        private void OpenBackgroundImageEditor_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var editor = new Windows.BackgroundImageEditorWindow();
            editor.ShowDialog();
            
            // 刷新预览图以应用背景图设置
            if (PreviewBorder.ActualWidth > 0)
            {
                SpawnPreviewBarrage();
            }
        }

        /// <summary>
        /// 保留对齐相关算法，该逻辑纯属 View 视图视觉调整，不应进入 ViewModel
        /// </summary>
        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (SettingsGrid == null || LeftSettingsStack == null || RightSettingsCard == null) return;

            if (e.NewSize.Width < 700)
            {
                // Single column layout
                SettingsGrid.ColumnDefinitions.Clear();
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                SettingsGrid.RowDefinitions.Clear();
                SettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                SettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                System.Windows.Controls.Grid.SetColumn(LeftSettingsStack, 0);
                System.Windows.Controls.Grid.SetRow(LeftSettingsStack, 0);

                System.Windows.Controls.Grid.SetColumn(RightSettingsCard, 0);
                System.Windows.Controls.Grid.SetRow(RightSettingsCard, 1);
                RightSettingsCard.Margin = new System.Windows.Thickness(0, 24, 0, 0);
            }
            else
            {
                // Two columns layout
                SettingsGrid.ColumnDefinitions.Clear();
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(16) });
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                SettingsGrid.RowDefinitions.Clear();

                System.Windows.Controls.Grid.SetColumn(LeftSettingsStack, 0);
                System.Windows.Controls.Grid.SetRow(LeftSettingsStack, 0);

                System.Windows.Controls.Grid.SetColumn(RightSettingsCard, 2);
                System.Windows.Controls.Grid.SetRow(RightSettingsCard, 0);
                RightSettingsCard.Margin = new System.Windows.Thickness(0);
            }
        }
    
        private void OpenFlyout(string key, UIElement target, bool isReattaching = false)
        {
            var flyout = this.Resources[key] as System.Windows.Controls.Primitives.Popup;
            if (flyout != null)
            {
                flyout.DataContext = this.DataContext;
                if (!isReattaching) 
                {
                    try {
                        var p = target.TranslatePoint(new System.Windows.Point(0, target.RenderSize.Height), PreviewCanvas);
                        flyout.PlacementTarget = PreviewCanvas;
                        flyout.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                        flyout.HorizontalOffset = p.X;
                        flyout.VerticalOffset = p.Y + 4;
                    } catch {}
                }
                flyout.IsOpen = true;
            }
        }
    }
}