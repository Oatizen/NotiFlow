using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
        

        private static readonly string[] FlyoutKeys = new[]
        {
            "AppIconFlyout",
            "AppNameFlyout",
            "ContentFlyout",
            "EllipsisFlyout",
            "CharacterWidgetFlyout"
        };

        private readonly System.Collections.Generic.HashSet<System.Windows.Controls.Primitives.Popup> _closingFlyouts = new();
        private Window? _parentWindow;

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWorkButtonState(); // 刚进入页面时先校准一次当前实际状态
            RegisterWindowEvents();
            
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
            UnregisterWindowEvents();
            CloseAllFlyoutsWithAnimation();
            WeakReferenceMessenger.Default.Unregister<BarragePreviewMessage>(this);
            WeakReferenceMessenger.Default.Unregister<WorkStateChangedMessage>(this);
        }

        private void RegisterWindowEvents()
        {
            if (_parentWindow != null) return;
            _parentWindow = Window.GetWindow(this);
            if (_parentWindow != null)
            {
                _parentWindow.PreviewMouseDown += OnGlobalPreviewMouseDown;
                _parentWindow.PreviewKeyDown += OnGlobalPreviewKeyDown;
                _parentWindow.Deactivated += OnGlobalDeactivated;
            }
        }

        private void UnregisterWindowEvents()
        {
            if (_parentWindow != null)
            {
                _parentWindow.PreviewMouseDown -= OnGlobalPreviewMouseDown;
                _parentWindow.PreviewKeyDown -= OnGlobalPreviewKeyDown;
                _parentWindow.Deactivated -= OnGlobalDeactivated;
                _parentWindow = null;
            }
        }

        private void OnGlobalPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var openFlyout = System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.Select(FlyoutKeys, k => this.Resources[k] as System.Windows.Controls.Primitives.Popup),
                f => f != null && f.IsOpen && !_closingFlyouts.Contains(f));

            if (openFlyout != null)
            {
                if (IsClickInsideFlyoutOrSubPopups(openFlyout, e))
                {
                    return;
                }

                // 点击在菜单外部（包括弹幕预览区域、主页面等任意区域），立即执行渐隐关闭
                CloseFlyoutWithAnimation(openFlyout);
            }
        }

        private static bool IsClickInsideFlyoutOrSubPopups(System.Windows.Controls.Primitives.Popup flyout, MouseButtonEventArgs e)
        {
            if (flyout.Child is FrameworkElement child)
            {
                var pos = e.GetPosition(child);
                if (pos.X >= 0 && pos.X <= child.ActualWidth && pos.Y >= 0 && pos.Y <= child.ActualHeight)
                {
                    return true;
                }

                // 兼容弹窗内部嵌套打开的颜色拾色器等子 Popup
                foreach (var subPopup in FindVisualChildren<System.Windows.Controls.Primitives.Popup>(child))
                {
                    if (subPopup.IsOpen && subPopup.Child is FrameworkElement subChild)
                    {
                        var subPos = e.GetPosition(subChild);
                        if (subPos.X >= 0 && subPos.X <= subChild.ActualWidth && subPos.Y >= 0 && subPos.Y <= subChild.ActualHeight)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var openFlyout = System.Linq.Enumerable.FirstOrDefault(
                    System.Linq.Enumerable.Select(FlyoutKeys, k => this.Resources[k] as System.Windows.Controls.Primitives.Popup),
                    f => f != null && f.IsOpen && !_closingFlyouts.Contains(f));

                if (openFlyout != null)
                {
                    CloseFlyoutWithAnimation(openFlyout);
                    e.Handled = true;
                }
            }
        }

        private void OnGlobalDeactivated(object? sender, EventArgs e)
        {
            CloseAllFlyoutsWithAnimation();
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
                CloseAllFlyoutsWithAnimation();
            }
            SpawnPreviewBarrage();
        }

        private void ApplyConfigToTextBlock(NotiFlow.Views.Controls.OutlinedTextBlock tb, BarrageConfigDto config, bool isAppName, bool isEllipsis)
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
                showStroke = false;
            }

            try { tb.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFF")); }
            catch { tb.Fill = Brushes.White; }

            tb.FontSize = fontSize > 0 ? fontSize : 36;
            tb.Opacity = opacity;
            
            bool fontExists = false;
            foreach(var f in Fonts.SystemFontFamilies) { if (f.Source.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase)) { fontExists = true; break; } }
            tb.FontFamily = fontExists ? new FontFamily(fontFamilyName) : new FontFamily("Microsoft YaHei");
            
            try { tb.FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(fontWeight); } catch { tb.FontWeight = FontWeights.Normal; }
            try { tb.FontStyle = (FontStyle)new FontStyleConverter().ConvertFromString(fontStyle); } catch { tb.FontStyle = FontStyles.Normal; }

            tb.IsUnderlined = isUnderlined;

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
                tb.Stroke = new SolidColorBrush(strokeColor);
                tb.StrokeThickness = thickness;
            }
            else
            {
                tb.Stroke = null;
                tb.StrokeThickness = 0;
            }
        }

        private void SpawnPreviewBarrage()
        {
            PreviewCanvas.Children.Clear();
            var vm = DataContext as SettingsViewModel;
            var config = vm?.GetCurrentConfig() ?? BarrageSettings.GetGlobalConfigDto();
            bool isPaused = PausePreviewButton.IsChecked == true;
            bool isEmail = vm?.IsEmailNotification == true;
            
            var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            
            if (isEmail)
            {
                // ====== 邮件弹幕预览格式：[邮件图标] [收件邮箱名称] [收件邮箱地址] [发件邮箱名称] [发件邮箱地址] [邮件主题] ======
                if (vm?.ShowEmailIcon == true)
                {
                    double iconSize = config.FontSize * 1.25 * (config.AppIconScale > 0 ? config.AppIconScale : 1.0);
                    var iconBorder = new System.Windows.Controls.Border
                    {
                        Width = iconSize,
                        Height = iconSize,
                        Background = Brushes.Transparent,
                        Margin = new Thickness(0, 0, 10, 0),
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true
                    };

                    var iconSource = NotiFlow.Services.EmailMessageFormatter.GetUnifiedEmailIcon();
                    if (iconSource != null)
                    {
                        var img = new Image { Source = iconSource, Stretch = Stretch.Uniform };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        iconBorder.Child = img;
                    }

                    if (isPaused)
                    {
                        iconBorder.Cursor = Cursors.Hand;
                        iconBorder.MouseLeftButtonUp += (s, e) =>
                        {
                            OpenFlyout("AppIconFlyout", iconBorder);
                            e.Handled = true;
                        };
                    }

                    stack.Children.Add(iconBorder);
                }

                string receiverName = vm?.SelectedEmailScope?.DisplayName ?? "QQ 邮箱";
                string receiverAddress = vm?.SelectedEmailScope?.EmailAddress ?? "2251493718@qq.com";
                if (receiverName == "全局") receiverName = "QQ 邮箱";
                if (receiverAddress == "所有邮箱通用样式") receiverAddress = "2251493718@qq.com";

                var enabledPrefixes = new System.Collections.Generic.List<(NotiFlow.Views.Controls.OutlinedTextBlock Block, string BaseText, string FlyoutTitle, string FlyoutType)>();

                if (vm?.ShowReceiverName == true)
                {
                    var tb = new NotiFlow.Views.Controls.OutlinedTextBlock { VerticalAlignment = VerticalAlignment.Center };
                    enabledPrefixes.Add((tb, "收件邮箱名称", "收件邮箱名称设置", "ReceiverName"));
                }

                if (vm?.ShowReceiverAddress == true)
                {
                    var tb = new NotiFlow.Views.Controls.OutlinedTextBlock { VerticalAlignment = VerticalAlignment.Center };
                    enabledPrefixes.Add((tb, "收件邮箱地址", "收件邮箱地址设置", "ReceiverAddress"));
                }

                if (vm?.ShowSenderName == true)
                {
                    var tb = new NotiFlow.Views.Controls.OutlinedTextBlock { VerticalAlignment = VerticalAlignment.Center };
                    enabledPrefixes.Add((tb, "发件邮箱名称", "发件邮箱名称设置", "SenderName"));
                }

                if (vm?.ShowSenderAddress == true)
                {
                    var tb = new NotiFlow.Views.Controls.OutlinedTextBlock { VerticalAlignment = VerticalAlignment.Center };
                    enabledPrefixes.Add((tb, "发件邮箱地址", "发件邮箱地址设置", "SenderAddress"));
                }

                for (int i = 0; i < enabledPrefixes.Count; i++)
                {
                    var item = enabledPrefixes[i];
                    bool isLast = (i == enabledPrefixes.Count - 1);
                    item.Block.Text = isLast ? (item.BaseText + "：") : (item.BaseText + " ");

                    ApplyConfigToTextBlock(item.Block, config, true, false);
                    if (isPaused)
                    {
                        item.Block.Cursor = Cursors.Hand;
                        string ft = item.FlyoutTitle;
                        string ftype = item.FlyoutType;
                        item.Block.MouseLeftButtonUp += (s, e) =>
                        {
                            if (vm != null)
                            {
                                vm.AppNameFlyoutTitle = ft;
                                vm.ActiveFlyoutType = ftype;
                            }
                            OpenFlyout("AppNameFlyout", item.Block);
                            e.Handled = true;
                        };
                    }
                    stack.Children.Add(item.Block);
                }

                var tbEmailSubject = new NotiFlow.Views.Controls.OutlinedTextBlock { Text = "邮件主题", VerticalAlignment = VerticalAlignment.Center };
                ApplyConfigToTextBlock(tbEmailSubject, config, false, false);
                if (isPaused)
                {
                    tbEmailSubject.Cursor = Cursors.Hand;
                    tbEmailSubject.MouseLeftButtonUp += (s, e) =>
                    {
                        if (vm != null)
                        {
                            vm.ContentFlyoutTitle = "邮件主题设置";
                        }
                        OpenFlyout("ContentFlyout", tbEmailSubject);
                        e.Handled = true;
                    };
                }
                stack.Children.Add(tbEmailSubject);
            }
            else
            {
                // ====== 原生 Windows 通知弹幕预览 ======
                if (config.ShowAppIcon)
                {
                    double iconSize = config.FontSize * 1.25 * (config.AppIconScale > 0 ? config.AppIconScale : 1.0);
                    var iconBorder = new System.Windows.Controls.Border 
                    { 
                        Width = iconSize, 
                        Height = iconSize, 
                        Background = Brushes.Transparent, 
                        Margin = new System.Windows.Thickness(0, 0, 10, 0), 
                        CornerRadius = new System.Windows.CornerRadius(4),
                        ClipToBounds = true
                    };

                    var appIconSource = GetNotiFlowAppIcon();
                    if (appIconSource != null)
                    {
                        var img = new Image { Source = appIconSource, Stretch = Stretch.Uniform };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        iconBorder.Child = img;
                    }
                    else
                    {
                        iconBorder.Background = Brushes.Gray;
                    }

                    if (isPaused)
                    {
                        iconBorder.Cursor = System.Windows.Input.Cursors.Hand;
                        iconBorder.MouseLeftButtonUp += (s, e) =>
                        {
                            OpenFlyout("AppIconFlyout", iconBorder);
                            e.Handled = true;
                        };
                    }
                    stack.Children.Add(iconBorder);
                }

                if (config.ShowAppName)
                {
                    var tbApp = new NotiFlow.Views.Controls.OutlinedTextBlock { Text = "应用名称：", VerticalAlignment = VerticalAlignment.Center };
                    ApplyConfigToTextBlock(tbApp, config, true, false);
                    if (isPaused)
                    {
                        tbApp.Cursor = Cursors.Hand;
                        tbApp.MouseLeftButtonUp += (s, e) =>
                        {
                            if (vm != null)
                            {
                                vm.AppNameFlyoutTitle = "应用名称设置";
                                vm.ActiveFlyoutType = "AppName";
                            }
                            OpenFlyout("AppNameFlyout", tbApp);
                            e.Handled = true;
                        };
                    }
                    stack.Children.Add(tbApp);
                }

                var tbContent = new NotiFlow.Views.Controls.OutlinedTextBlock { Text = "这是一条测试弹幕", VerticalAlignment = VerticalAlignment.Center };
                ApplyConfigToTextBlock(tbContent, config, false, false);
                if (isPaused)
                {
                    tbContent.Cursor = Cursors.Hand;
                    tbContent.MouseLeftButtonUp += (s, e) =>
                    {
                        if (vm != null)
                        {
                            vm.ContentFlyoutTitle = "内容设置";
                        }
                        OpenFlyout("ContentFlyout", tbContent);
                        e.Handled = true;
                    };
                }
                stack.Children.Add(tbContent);
            }
            
            var tbEllipsis = new NotiFlow.Views.Controls.OutlinedTextBlock { Text = "......", VerticalAlignment = VerticalAlignment.Center };
            ApplyConfigToTextBlock(tbEllipsis, config, false, true);
            if (isPaused) {
                tbEllipsis.Cursor = Cursors.Hand;
                tbEllipsis.MouseLeftButtonUp += (s, e) => {
                    OpenFlyout("EllipsisFlyout", tbEllipsis);
                    e.Handled = true;
                };
            }
            stack.Children.Add(tbEllipsis);
            
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

                    if (config.BackgroundImageEdgeBlur > 0 && img.Width > 0 && img.Height > 0)
                    {
                        double vBlur = Math.Min(6.0, img.Height * 0.15);
                        double ry = vBlur > 0 ? Math.Min(0.2, vBlur / img.Height) : 0.0;

                        var vStops = new System.Windows.Media.GradientStopCollection
                        {
                            new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0),
                            new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), ry),
                            new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - ry),
                            new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0)
                        };

                        var hStops = new System.Windows.Media.GradientStopCollection();

                        bool isRightAnchor = config.BackgroundImageAnchor == ImageAnchor.TopRight || config.BackgroundImageAnchor == ImageAnchor.MiddleRight || config.BackgroundImageAnchor == ImageAnchor.BottomRight;
                        bool isLeftAnchor = config.BackgroundImageAnchor == ImageAnchor.TopLeft || config.BackgroundImageAnchor == ImageAnchor.MiddleLeft || config.BackgroundImageAnchor == ImageAnchor.BottomLeft;

                        if (isRightAnchor)
                        {
                            double rx = Math.Clamp(config.BackgroundImageEdgeBlur / img.Width, 0.01, 1.0);
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), rx * 0.25));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), rx * 0.50));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), rx * 0.75));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), rx));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0));
                        }
                        else if (isLeftAnchor)
                        {
                            double rx = Math.Clamp(config.BackgroundImageEdgeBlur / img.Width, 0.01, 1.0);
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 0.0));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - rx));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), 1.0 - rx * 0.75));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), 1.0 - rx * 0.50));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), 1.0 - rx * 0.25));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0));
                        }
                        else
                        {
                            double rx = Math.Clamp(config.BackgroundImageEdgeBlur / img.Width, 0.01, 0.5);
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), rx * 0.25));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), rx * 0.50));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), rx * 0.75));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), rx));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - rx));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), 1.0 - rx * 0.75));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), 1.0 - rx * 0.50));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), 1.0 - rx * 0.25));
                            hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0));
                        }

                        canvas.OpacityMask = new System.Windows.Media.LinearGradientBrush(hStops, new Point(0, 0), new Point(1, 0));
                        img.OpacityMask = new System.Windows.Media.LinearGradientBrush(vStops, new Point(0, 0), new Point(0, 1));
                    }

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

            var wrapperCanvas = new Canvas();
            wrapperCanvas.Children.Add(border);

            if (config.ShowCharacterWidget)
            {
                string charPath = config.CharacterWidgetPath;
                if (string.IsNullOrEmpty(charPath) || config.CharacterWidgetPresetId == "preset_1")
                {
                    string presetInApp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Characters", "Preset1_Pajing.png");
                    if (System.IO.File.Exists(presetInApp)) charPath = presetInApp;
                    else if (System.IO.File.Exists(@"E:\PhotoShop成品\组件1.png")) charPath = @"E:\PhotoShop成品\组件1.png";
                }

                if (!string.IsNullOrEmpty(charPath) && System.IO.File.Exists(charPath))
                {
                    try
                    {
                        var charBmp = new System.Windows.Media.Imaging.BitmapImage();
                        charBmp.BeginInit();
                        charBmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        using (var stream = new System.IO.FileStream(charPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                        {
                            charBmp.StreamSource = stream;
                            charBmp.EndInit();
                        }
                        charBmp.Freeze();

                        double charScale = config.CharacterWidgetScale <= 0 ? 1.0 : config.CharacterWidgetScale;
                        double charH = config.FontSize * 2.8 * charScale;
                        double charW = charH * (charBmp.PixelWidth / (double)charBmp.PixelHeight);

                        var charImg = new Image
                        {
                            Source = charBmp,
                            Width = charW,
                            Height = charH,
                            Stretch = Stretch.Uniform,
                            Opacity = config.CharacterWidgetOpacity <= 0 ? 1.0 : config.CharacterWidgetOpacity,
                            SnapsToDevicePixels = true
                        };
                        RenderOptions.SetBitmapScalingMode(charImg, BitmapScalingMode.HighQuality);

                        double relX = itemWidth - charW * 0.95 + config.CharacterWidgetOffsetX;
                        double relY = -charH + config.CharacterWidgetOffsetY;

                        Canvas.SetLeft(charImg, relX);
                        Canvas.SetTop(charImg, relY);

                        if (isPaused)
                        {
                            charImg.Cursor = Cursors.Hand;
                            charImg.MouseLeftButtonUp += (s, e) =>
                            {
                                OpenFlyout("CharacterWidgetFlyout", charImg);
                                e.Handled = true;
                            };
                        }

                        // 先插入底层挂件，弹幕胶囊自然在上方遮挡角色下半部
                        wrapperCanvas.Children.Insert(0, charImg);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Character preview failed: " + ex.Message);
                    }
                }
            }

            if (isPaused)
            {
                Canvas.SetLeft(wrapperCanvas, Math.Max(24.0, (PreviewBorder.ActualWidth - itemWidth) / 2.0));
                Canvas.SetTop(wrapperCanvas, Math.Max(0, (PreviewBorder.ActualHeight - border.DesiredSize.Height) / 2.0));
                PreviewCanvas.Children.Add(wrapperCanvas);
                wrapperCanvas.UpdateLayout();

                // 重新附着弹窗（此时所有视觉子元素均已完成布局测量，TranslatePoint 坐标绝对精确）
                if (((System.Windows.Controls.Primitives.Popup)this.Resources["AppIconFlyout"])?.IsOpen == true && config.ShowAppIcon) 
                { 
                    OpenFlyout("AppIconFlyout", (UIElement)stack.Children[0], true); 
                }
                
                int appNameIndex = config.ShowAppIcon ? 1 : 0;
                if (((System.Windows.Controls.Primitives.Popup)this.Resources["AppNameFlyout"])?.IsOpen == true && config.ShowAppName) 
                { 
                    OpenFlyout("AppNameFlyout", (UIElement)stack.Children[appNameIndex], true); 
                }
                
                int contentIndex = appNameIndex + (config.ShowAppName ? 1 : 0);
                if (((System.Windows.Controls.Primitives.Popup)this.Resources["ContentFlyout"])?.IsOpen == true) 
                { 
                    OpenFlyout("ContentFlyout", (UIElement)stack.Children[contentIndex], true); 
                }
                
                int ellipsisIndex = contentIndex + 1;
                if (((System.Windows.Controls.Primitives.Popup)this.Resources["EllipsisFlyout"])?.IsOpen == true) 
                { 
                    OpenFlyout("EllipsisFlyout", (UIElement)stack.Children[ellipsisIndex], true); 
                }

                if (((System.Windows.Controls.Primitives.Popup)this.Resources["CharacterWidgetFlyout"])?.IsOpen == true && config.ShowCharacterWidget)
                {
                    Image? charElement = null;
                    foreach (UIElement child in wrapperCanvas.Children)
                    {
                        if (child is Image img)
                        {
                            charElement = img;
                            break;
                        }
                    }

                    if (charElement != null)
                    {
                        OpenFlyout("CharacterWidgetFlyout", charElement, true);
                    }
                }
            }
            else
            {
                Canvas.SetLeft(wrapperCanvas, PreviewBorder.ActualWidth);
                Canvas.SetTop(wrapperCanvas, Math.Max(0, (PreviewBorder.ActualHeight - border.DesiredSize.Height) / 2.0));
                PreviewCanvas.Children.Add(wrapperCanvas);

                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = PreviewBorder.ActualWidth,
                    To = -itemWidth - 100,
                    Duration = TimeSpan.FromSeconds(Math.Max(3, (PreviewBorder.ActualWidth + itemWidth) / (config.ScrollSpeedCharsPerSec * config.FontSize))),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                wrapperCanvas.BeginAnimation(Canvas.LeftProperty, animation);
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

        private void OpenCharacterWidgetEditor_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var editor = new Windows.CharacterWidgetEditorWindow();
            editor.ShowDialog();

            // 刷新 ViewModel 和预览图以应用角色挂件排版设置
            if (DataContext is SettingsViewModel vm)
            {
                vm.CharacterWidgetPath = BarrageSettings.CharacterWidgetPath;
                vm.CharacterWidgetScale = BarrageSettings.CharacterWidgetScale;
                vm.CharacterWidgetOffsetX = BarrageSettings.CharacterWidgetOffsetX;
                vm.CharacterWidgetOffsetY = BarrageSettings.CharacterWidgetOffsetY;
                vm.ShowCharacterWidget = BarrageSettings.ShowCharacterWidget;
                vm.InitializeCharacterPresets();
            }

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
    
        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) > 1.0 || Math.Abs(e.HorizontalChange) > 1.0)
            {
                // 页面滚动时立即收起所有打开的菜单，符合现代 Fluent/WinUI 标准交互并避免离屏重绘卡顿
                CloseAllFlyoutsInstant();
            }
        }

        private void CloseAllFlyoutsInstant()
        {
            foreach (var key in FlyoutKeys)
            {
                if (this.Resources[key] is System.Windows.Controls.Primitives.Popup flyout && flyout.IsOpen)
                {
                    if (flyout.Child is FrameworkElement child)
                    {
                        child.BeginAnimation(UIElement.OpacityProperty, null);
                        child.Opacity = 0.0;
                    }
                    flyout.IsOpen = false;
                    _closingFlyouts.Remove(flyout);
                }
            }
        }

        /// <summary>
        /// 播放弹窗唤出动画：从下方偏移 16px 向上滑出并渐显。
        /// </summary>
        private void AnimateFlyoutOpen(System.Windows.Controls.Primitives.Popup flyout)
        {
            if (flyout.Child is not FrameworkElement child) return;

            if (child.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform();
                child.RenderTransform = tt;
            }

            // 停止任何可能正在运行的动画并重置状态
            child.BeginAnimation(UIElement.OpacityProperty, null);
            tt.BeginAnimation(TranslateTransform.YProperty, null);

            child.Opacity = 0.0;
            tt.Y = 16.0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            var slideAnimation = new DoubleAnimation
            {
                From = 16.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = easing
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = easing
            };

            tt.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
            child.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        /// <summary>
        /// 播放弹窗关闭动画：原位直接渐隐消失后关闭 Popup。
        /// </summary>
        private void CloseFlyoutWithAnimation(System.Windows.Controls.Primitives.Popup flyout, Action? onCompleted = null)
        {
            if (flyout == null || !flyout.IsOpen)
            {
                onCompleted?.Invoke();
                return;
            }

            if (_closingFlyouts.Contains(flyout)) return;
            _closingFlyouts.Add(flyout);

            if (flyout.Child is not FrameworkElement child)
            {
                flyout.IsOpen = false;
                _closingFlyouts.Remove(flyout);
                onCompleted?.Invoke();
                return;
            }

            // 设置本地值为 0，这样即便动画结束解除绑定，也不会瞬间弹回 1.0 产生闪烁
            child.Opacity = 0.0;

            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOutAnimation.Completed += (s, e) =>
            {
                flyout.IsOpen = false;
                child.BeginAnimation(UIElement.OpacityProperty, null);
                child.Opacity = 0.0;
                _closingFlyouts.Remove(flyout);
                onCompleted?.Invoke();
            };

            child.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
        }

        /// <summary>
        /// 渐隐关闭所有当前打开的弹窗。
        /// </summary>
        private void CloseAllFlyoutsWithAnimation(Action? onCompleted = null)
        {
            var openFlyouts = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Where(
                    System.Linq.Enumerable.Select(FlyoutKeys, key => this.Resources[key] as System.Windows.Controls.Primitives.Popup),
                    f => f != null && f.IsOpen));

            if (openFlyouts.Count == 0)
            {
                onCompleted?.Invoke();
                return;
            }

            int remaining = openFlyouts.Count;
            foreach (var flyout in openFlyouts)
            {
                CloseFlyoutWithAnimation(flyout!, () =>
                {
                    remaining--;
                    if (remaining <= 0)
                    {
                        onCompleted?.Invoke();
                    }
                });
            }
        }

        private void OpenFlyout(string key, UIElement target, bool isReattaching = false)
        {
            var flyout = this.Resources[key] as System.Windows.Controls.Primitives.Popup;
            if (flyout == null) return;

            flyout.DataContext = this.DataContext;

            // 如果当前有其它弹窗处于打开状态，先平滑渐隐关闭其它弹窗
            foreach (var otherKey in FlyoutKeys)
            {
                if (otherKey != key && this.Resources[otherKey] is System.Windows.Controls.Primitives.Popup otherFlyout && otherFlyout.IsOpen)
                {
                    CloseFlyoutWithAnimation(otherFlyout);
                }
            }

            if (!isReattaching) 
            {
                try 
                {
                    var p = target.TranslatePoint(new System.Windows.Point(0, target.RenderSize.Height), PreviewCanvas);
                    flyout.PlacementTarget = PreviewCanvas;
                    flyout.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                    flyout.HorizontalOffset = p.X;
                    flyout.VerticalOffset = p.Y + 4;
                } 
                catch {}

                // 在打开前预置透明度为 0，防止原生窗口创建第一帧出现满透明度闪烁
                if (flyout.Child is FrameworkElement child)
                {
                    if (child.RenderTransform is not TranslateTransform tt)
                    {
                        tt = new TranslateTransform();
                        child.RenderTransform = tt;
                    }
                    child.BeginAnimation(UIElement.OpacityProperty, null);
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    child.Opacity = 0.0;
                    tt.Y = 16.0;
                }

                flyout.IsOpen = true;
                AnimateFlyoutOpen(flyout);
            }
            else
            {
                // 拖动滑块时重新附着：仅平滑跟随位置，不重新触发进入动画
                try 
                {
                    var p = target.TranslatePoint(new System.Windows.Point(0, target.RenderSize.Height), PreviewCanvas);
                    if (!double.IsNaN(p.X) && !double.IsNaN(p.Y) && (p.X > 0 || p.Y > 0))
                    {
                        flyout.PlacementTarget = PreviewCanvas;
                        flyout.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                        flyout.HorizontalOffset = p.X;
                        flyout.VerticalOffset = p.Y + 4;
                    }
                } 
                catch {}

                if (!flyout.IsOpen)
                {
                    flyout.IsOpen = true;
                    AnimateFlyoutOpen(flyout);
                }
            }
        }

        // ====== 多显示器卡片拖拽重排交互 ======
        private Point _monitorDragStartPoint;

        private void MonitorCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _monitorDragStartPoint = e.GetPosition(null);
        }

        private void MonitorCard_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element && element.Tag is MonitorSettingItemDto item)
            {
                Point currentPoint = e.GetPosition(null);
                Vector diff = _monitorDragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var data = new DataObject("MonitorItemDto", item);
                    DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
                }
            }
        }

        private void MonitorCard_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("MonitorItemDto"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void MonitorCard_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("MonitorItemDto") &&
                e.Data.GetData("MonitorItemDto") is MonitorSettingItemDto sourceItem &&
                sender is FrameworkElement targetElement &&
                targetElement.Tag is MonitorSettingItemDto targetItem &&
                DataContext is SettingsViewModel vm)
            {
                int oldIndex = vm.MonitorList.IndexOf(sourceItem);
                int newIndex = vm.MonitorList.IndexOf(targetItem);
                if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                {
                    vm.ReorderMonitors(oldIndex, newIndex);
                }
            }
        }

        private static BitmapSource? _notiFlowAppIcon;

        /// <summary>
        /// 获取 NotiFlow 应用原生高清图标。
        /// </summary>
        private static BitmapSource? GetNotiFlowAppIcon()
        {
            if (_notiFlowAppIcon != null) return _notiFlowAppIcon;

            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NotiFlow Icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _notiFlowAppIcon = bmp;
                    return _notiFlowAppIcon;
                }

                var uri = new Uri("pack://application:,,,/NotiFlow;component/NotiFlow Icon.png", UriKind.Absolute);
                var bmpResource = new BitmapImage(uri);
                bmpResource.Freeze();
                _notiFlowAppIcon = bmpResource;
                return _notiFlowAppIcon;
            }
            catch
            {
                return null;
            }
        }
    }
}