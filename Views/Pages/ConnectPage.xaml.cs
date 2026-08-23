using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MailKit.Net.Imap;
using MailKit.Security;
using NotiFlow.Models;

namespace NotiFlow.Views.Pages
{
    /// <summary>
    /// “连接”页面交互逻辑。
    /// 管理外部消息源（Windows 系统通知中心、IMAP 邮箱等）的入口导航与启闭状态。
    /// </summary>
    public partial class ConnectPage : Page
    {
        private enum EmailCardState
        {
            Collapsed,
            Selection,
            BindingForm,
            ListView
        }

        private bool _isWindowsCardExpanded;
        private EmailCardState _emailCardState = EmailCardState.Collapsed;

        private readonly List<EmailProviderPreset> _presets;
        private readonly List<CoverflowCardItem> _cardItems = new();
        private readonly Dictionary<string, BitmapImage> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        // 鼠标拖拽 CoverFlow 状态跟踪
        private bool _isDraggingCoverflow;
        private Point _dragStartPoint;
        private double _dragStartPosition;
        private bool _hasDraggedEnough;

        // 当前正在编辑的账号（若为新建则为 null）
        private EmailAccountConfigDto? _currentEditingAccount;

        #region 依赖属性：连续浮点轮播位置（驱动真实 3D CoverFlow 60fps 平滑过渡）

        public static readonly DependencyProperty CarouselPositionProperty =
            DependencyProperty.Register(
                nameof(CarouselPosition),
                typeof(double),
                typeof(ConnectPage),
                new PropertyMetadata(0.0, OnCarouselPositionChanged));

        public double CarouselPosition
        {
            get => (double)GetValue(CarouselPositionProperty);
            set => SetValue(CarouselPositionProperty, value);
        }

        private static void OnCarouselPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectPage page)
            {
                page.RenderCoverflow((double)e.NewValue);
            }
        }

        #endregion

        public ConnectPage()
        {
            InitializeComponent();
            _presets = EmailProviderPreset.GetAllPresets();

            Loaded += (s, e) =>
            {
                InitializeCoverflow();
                InitializeListView();
                UpdateEmailCollapsedSubtitle();
                UpdateBottomStatus();
            };
        }

        /// <summary>
        /// 为列表网格中的全部邮箱卡片填充高清位图（直接使用 GetPresetBitmap 消除中文 URI 编码异常）。
        /// </summary>
        private void InitializeListView()
        {
            PopulateCardImage(EmailCard_QQ, "QQ邮箱.png");
            PopulateCardImage(EmailCard_NetEase163, "163邮箱.png");
            PopulateCardImage(EmailCard_NetEase126, "126邮箱.png");
            PopulateCardImage(EmailCard_Gmail, "Gmail.png");
            PopulateCardImage(EmailCard_Sina, "新浪邮箱.png");
            PopulateCardImage(EmailCard_Mobile139, "139邮箱.png");
            PopulateCardImage(EmailCard_Office365, "Office365.png");
            PopulateCardImage(EmailCard_Outlook, "Outlook.png");
            PopulateCardImage(EmailCard_Exchange, "Exchange.png");
            PopulateCardImage(EmailCard_Custom, "IMAP.png");
        }

        private void PopulateCardImage(Border cardBorder, string fileName)
        {
            if (cardBorder?.Child is Image img)
            {
                img.Source = GetPresetBitmap(fileName);
            }
        }

        #region 全局点击监听（点击卡片外部区域自动收起）

        /// <summary>
        /// 监听页面全局鼠标点击：当任一卡片处于展开状态且点击发生在卡片外部时，自动收起卡片。
        /// </summary>
        private void Page_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果正在拖拽 CoverFlow，则忽略全局外部收起检测
            if (_isDraggingCoverflow) return;

            // 1. 处理 Windows 卡片外部点击
            if (_isWindowsCardExpanded)
            {
                Point clickPoint = e.GetPosition(WindowsCardBorder);
                var cardBounds = new Rect(0, 0, WindowsCardBorder.ActualWidth, WindowsCardBorder.ActualHeight);

                if (!cardBounds.Contains(clickPoint))
                {
                    CollapseWindowsCard();
                }
            }

            // 2. 处理 邮箱 卡片外部点击（无论处于选择视图还是表单视图均能收起）
            if (_emailCardState != EmailCardState.Collapsed)
            {
                Point clickPoint = e.GetPosition(EmailCardBorder);
                var cardBounds = new Rect(0, 0, EmailCardBorder.ActualWidth, EmailCardBorder.ActualHeight);

                if (!cardBounds.Contains(clickPoint))
                {
                    CollapseEmailCard();
                }
            }
        }

        #endregion

        #region 1. Windows 通知卡片展开/收发交互（防竞态消失保护）

        /// <summary>
        /// 点击折叠卡片：触发平滑变高与内容淡入动画展开详情。
        /// </summary>
        private void WindowsCard_Expand_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isWindowsCardExpanded) return;

            if (_emailCardState != EmailCardState.Collapsed)
            {
                CollapseEmailCard();
            }

            _isWindowsCardExpanded = true;

            WindowsCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            WindowsCollapsedView.BeginAnimation(UIElement.OpacityProperty, null);
            WindowsExpandedView.BeginAnimation(UIElement.OpacityProperty, null);

            WindowsCollapsedView.Visibility = Visibility.Visible;
            WindowsExpandedView.Visibility = Visibility.Visible;
            WindowsCollapsedView.IsHitTestVisible = false;
            WindowsExpandedView.IsHitTestVisible = true;

            double startHeight = WindowsCardBorder.ActualHeight > 0 ? WindowsCardBorder.ActualHeight : 136;
            double startCollapsedOpacity = WindowsCollapsedView.Opacity;
            double startExpandedOpacity = WindowsExpandedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 240,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startCollapsedOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_isWindowsCardExpanded)
                {
                    WindowsCollapsedView.Visibility = Visibility.Collapsed;
                    WindowsExpandedView.Visibility = Visibility.Visible;
                }
            };

            WindowsCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            WindowsCollapsedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            WindowsExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 点击右上角关闭按钮：收起卡片。
        /// </summary>
        private void WindowsCard_Collapse_Click(object sender, RoutedEventArgs e)
        {
            CollapseWindowsCard();
        }

        /// <summary>
        /// 执行收起动画：卡片高度恢复至 136px，淡出展开层并淡入折叠层。
        /// </summary>
        private void CollapseWindowsCard()
        {
            if (!_isWindowsCardExpanded) return;
            _isWindowsCardExpanded = false;

            WindowsCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            WindowsCollapsedView.BeginAnimation(UIElement.OpacityProperty, null);
            WindowsExpandedView.BeginAnimation(UIElement.OpacityProperty, null);

            WindowsCollapsedView.Visibility = Visibility.Visible;
            WindowsExpandedView.Visibility = Visibility.Visible;
            WindowsCollapsedView.IsHitTestVisible = true;
            WindowsExpandedView.IsHitTestVisible = false;

            double startHeight = WindowsCardBorder.ActualHeight > 0 ? WindowsCardBorder.ActualHeight : 240;
            double startCollapsedOpacity = WindowsCollapsedView.Opacity;
            double startExpandedOpacity = WindowsExpandedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 136,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startCollapsedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (!_isWindowsCardExpanded)
                {
                    WindowsExpandedView.Visibility = Visibility.Collapsed;
                    WindowsCollapsedView.Visibility = Visibility.Visible;
                }
            };

            WindowsCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            WindowsExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            WindowsCollapsedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 切换 Windows 通知监听开启/禁用状态。
        /// </summary>
        private void ToggleWindowsNotifications_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.EnableWindowsNotifications = !vm.EnableWindowsNotifications;
            }
        }

        /// <summary>
        /// 点击“前往作用域设置”：一键导航至“作用域”管理页面。
        /// </summary>
        private void GoToScopeSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is SettingsWindow settingsWindow)
            {
                settingsWindow.RootNavigation.Navigate(typeof(ScopePage));
            }
        }

        #endregion

        #region 2. 邮箱卡片三态无缝延展架构（Collapsed 136px <-> Selection 260px <-> BindingForm 475px）

        /// <summary>
        /// 初始化 10 款邮箱服务商的独立 3D 实体卡片并加入舞台 Canvas。
        /// </summary>
        private void InitializeCoverflow()
        {
            if (_cardItems.Count > 0) return;

            CoverflowCanvas.Children.Clear();
            _cardItems.Clear();

            const double cardWidth = 114;
            const double cardHeight = 76;
            double canvasWidth = CoverflowCanvas.Width > 0 ? CoverflowCanvas.Width : 250;
            double canvasHeight = CoverflowCanvas.Height > 0 ? CoverflowCanvas.Height : 84;

            double baseLeft = (canvasWidth - cardWidth) / 2.0;
            double baseTop = (canvasHeight - cardHeight) / 2.0;

            for (int i = 0; i < _presets.Count; i++)
            {
                var preset = _presets[i];

                var scaleTrans = new ScaleTransform(1.0, 1.0);
                var skewTrans = new SkewTransform(0.0, 0.0);
                var translateTrans = new TranslateTransform(0.0, 0.0);

                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(scaleTrans);
                transformGroup.Children.Add(skewTrans);
                transformGroup.Children.Add(translateTrans);

                var shadowEffect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.12,
                    Color = Colors.Black
                };

                var image = new Image
                {
                    Source = GetPresetBitmap(preset.ImageFileName),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(6),
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

                var cardBorder = new Border
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    CornerRadius = new CornerRadius(10),
                    Background = Brushes.White,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    Cursor = Cursors.Hand,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = transformGroup,
                    Effect = shadowEffect,
                    Child = image,
                    Tag = i
                };

                Canvas.SetLeft(cardBorder, baseLeft);
                Canvas.SetTop(cardBorder, baseTop);

                // 点击卡片平滑滑至该卡片
                int capturedIndex = i;
                cardBorder.MouseLeftButtonUp += (s, e) =>
                {
                    if (!_hasDraggedEnough)
                    {
                        AnimateToPresetIndex(capturedIndex);
                    }
                };

                CoverflowCanvas.Children.Add(cardBorder);

                _cardItems.Add(new CoverflowCardItem
                {
                    Index = i,
                    Border = cardBorder,
                    ScaleTrans = scaleTrans,
                    SkewTrans = skewTrans,
                    TranslateTrans = translateTrans,
                    ShadowEffect = shadowEffect,
                    Preset = preset
                });
            }

            RenderCoverflow(CarouselPosition);
        }

        /// <summary>
        /// 基于当前浮点位置值渲染全部卡片的空间透视（位移、缩放、3D倾斜角、透明度、深度与高亮融合）。
        /// 彻底消除固定槽位图片切换时的顿挫与跳帧。
        /// </summary>
        private void RenderCoverflow(double position)
        {
            int count = _presets.Count;
            if (count == 0 || _cardItems.Count == 0) return;

            for (int i = 0; i < _cardItems.Count; i++)
            {
                var item = _cardItems[i];

                double delta = i - position;
                while (delta > count / 2.0) delta -= count;
                while (delta < -count / 2.0) delta += count;

                double absD = Math.Abs(delta);

                if (absD > 2.8)
                {
                    item.Border.Visibility = Visibility.Collapsed;
                    continue;
                }

                item.Border.Visibility = Visibility.Visible;

                // 1. 水平位移 X
                double x;
                if (absD <= 1.0)
                {
                    x = delta * 54.0;
                }
                else if (delta > 1.0)
                {
                    x = 54.0 + (delta - 1.0) * 44.0;
                }
                else
                {
                    x = -54.0 + (delta + 1.0) * 44.0;
                }
                item.TranslateTrans.X = x;

                // 2. 尺寸等比缩放 Scale
                double scale = Math.Max(0.60, 1.0 - absD * 0.18);
                item.ScaleTrans.ScaleX = scale;
                item.ScaleTrans.ScaleY = scale;

                // 3. 3D 空间倾斜角 SkewY
                double skewY = 0;
                if (delta < 0)
                {
                    skewY = Math.Min(8.0, -delta * 5.2);
                }
                else if (delta > 0)
                {
                    skewY = Math.Max(-8.0, -delta * 5.2);
                }
                item.SkewTrans.AngleY = skewY;

                // 4. 渐隐渐现 Opacity
                double opacity;
                if (absD <= 1.0)
                {
                    opacity = 0.65 + (1.0 - absD) * 0.35;
                }
                else
                {
                    opacity = Math.Max(0.0, 0.65 - (absD - 1.0) * 0.43);
                }
                item.Border.Opacity = opacity;

                // 5. 空间深度 Z-Index
                int zIndex = (int)Math.Round((10.0 - absD) * 10);
                Panel.SetZIndex(item.Border, zIndex);

                // 6. 激活高亮边框与阴影渐变
                double activeFactor = Math.Max(0.0, 1.0 - absD);
                item.Border.BorderThickness = new Thickness(1.0 + activeFactor * 1.0);

                var borderColor = LerpColor(Color.FromRgb(0xE0, 0xE0, 0xE0), Color.FromRgb(0x00, 0x78, 0xD4), activeFactor);
                item.Border.BorderBrush = new SolidColorBrush(borderColor);

                item.ShadowEffect.BlurRadius = 8.0 + activeFactor * 6.0;
                item.ShadowEffect.ShadowDepth = 2.0 + activeFactor * 1.5;
                var shadowColor = LerpColor(Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 120, 212), activeFactor);
                item.ShadowEffect.Color = shadowColor;
                item.ShadowEffect.Opacity = 0.10 + activeFactor * 0.08;
            }
        }

        #region 鼠标水平拖拽滑动手势支持

        private void Coverflow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragStartPosition = CarouselPosition;
            _isDraggingCoverflow = true;
            _hasDraggedEnough = false;

            BeginAnimation(CarouselPositionProperty, null);
        }

        private void Coverflow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingCoverflow || e.LeftButton != MouseButtonState.Pressed) return;

            Point currentPoint = e.GetPosition(this);
            double deltaX = currentPoint.X - _dragStartPoint.X;

            if (!_hasDraggedEnough && Math.Abs(deltaX) > 4)
            {
                _hasDraggedEnough = true;
                CoverflowCanvas.CaptureMouse();
            }

            if (_hasDraggedEnough)
            {
                double newPos = _dragStartPosition - (deltaX / 56.0);
                CarouselPosition = newPos;
            }
        }

        private void Coverflow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingCoverflow) return;
            _isDraggingCoverflow = false;

            if (_hasDraggedEnough)
            {
                CoverflowCanvas.ReleaseMouseCapture();
                e.Handled = true;

                double targetPos = Math.Round(CarouselPosition);
                AnimateToPosition(targetPos);
            }
        }

        #endregion

        /// <summary>
        /// 颜色线性插值辅助函数。
        /// </summary>
        private static Color LerpColor(Color c1, Color c2, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            byte a = (byte)(c1.A + (c2.A - c1.A) * t);
            byte r = (byte)(c1.R + (c2.R - c1.R) * t);
            byte g = (byte)(c1.G + (c2.G - c1.G) * t);
            byte b = (byte)(c1.B + (c2.B - c1.B) * t);
            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// 点击折叠邮箱卡片：从 136px 变高展开至 260px 进入 3D 轮播选择视图。
        /// </summary>
        private void EmailCard_Collapsed_Click(object sender, MouseButtonEventArgs e)
        {
            if (_emailCardState != EmailCardState.Collapsed) return;

            if (_isWindowsCardExpanded)
            {
                CollapseWindowsCard();
            }

            _emailCardState = EmailCardState.Selection;
            InitializeCoverflow();
            RenderCoverflow(CarouselPosition);

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailCollapsedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailCollapsedView.Visibility = Visibility.Visible;
            EmailExpandedView.Visibility = Visibility.Visible;
            EmailBindingFormView.Visibility = Visibility.Collapsed;
            EmailListView.Visibility = Visibility.Collapsed;

            EmailCollapsedView.IsHitTestVisible = false;
            EmailExpandedView.IsHitTestVisible = true;
            EmailBindingFormView.IsHitTestVisible = false;
            EmailListView.IsHitTestVisible = false;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 136;
            double startCollapsedOpacity = EmailCollapsedView.Opacity;
            double startExpandedOpacity = EmailExpandedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 260,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startCollapsedOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.Selection)
                {
                    EmailCollapsedView.Visibility = Visibility.Collapsed;
                    EmailExpandedView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            EmailCollapsedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 点击选择视图右上角关闭按钮：完全收起至 136px 折叠状态。
        /// </summary>
        private void EmailCard_Close_Click(object sender, RoutedEventArgs e)
        {
            CollapseEmailCard();
        }

        /// <summary>
        /// 点击选择视图左上角列表按钮：平滑变长（260px -> 475px）展开全部邮箱网格列表（图2）。
        /// </summary>
        private void EmailCard_ShowList_Click(object sender, RoutedEventArgs e)
        {
            if (_emailCardState != EmailCardState.Selection) return;
            _emailCardState = EmailCardState.ListView;

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailExpandedView.Visibility = Visibility.Visible;
            EmailBindingFormView.Visibility = Visibility.Collapsed;
            EmailListView.Visibility = Visibility.Visible;

            EmailExpandedView.IsHitTestVisible = false;
            EmailBindingFormView.IsHitTestVisible = false;
            EmailListView.IsHitTestVisible = true;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 260;
            double startExpandedOpacity = EmailExpandedView.Opacity;
            double startListOpacity = EmailListView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 520,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startListOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.ListView)
                {
                    EmailExpandedView.Visibility = Visibility.Collapsed;
                    EmailListView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 点击列表视图左上角返回按钮：平滑变短（520px -> 260px）返回 3D 轮播选择视图。
        /// </summary>
        private void EmailListView_Back_Click(object sender, RoutedEventArgs e)
        {
            TransitionFromListToSelection();
        }

        /// <summary>
        /// 点击邮箱列表中的任意品牌卡片：选中该服务商并自动平滑过渡回 3D 轮播选择视图（图1）。
        /// </summary>
        private void EmailProviderCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string providerType)
            {
                SelectProviderFromList(providerType);
            }
        }

        private void SelectProviderFromList(string providerType)
        {
            int targetIndex = _presets.FindIndex(p => string.Equals(p.ProviderType, providerType, StringComparison.OrdinalIgnoreCase));
            if (targetIndex >= 0)
            {
                // 1. 设置当前 3D 轮播位置并立即精准渲染聚焦
                CarouselPosition = targetIndex;
                RenderCoverflow(targetIndex);
                UpdateBottomStatus();

                // 2. 平滑渐变过渡回 3D 轮播选择视图
                TransitionFromListToSelection();
            }
        }

        private void TransitionFromListToSelection()
        {
            if (_emailCardState != EmailCardState.ListView) return;
            _emailCardState = EmailCardState.Selection;

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailExpandedView.Visibility = Visibility.Visible;
            EmailBindingFormView.Visibility = Visibility.Collapsed;
            EmailListView.Visibility = Visibility.Visible;

            EmailExpandedView.IsHitTestVisible = true;
            EmailBindingFormView.IsHitTestVisible = false;
            EmailListView.IsHitTestVisible = false;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 520;
            double startListOpacity = EmailListView.Opacity;
            double startExpandedOpacity = EmailExpandedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 260,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startListOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.Selection)
                {
                    EmailListView.Visibility = Visibility.Collapsed;
                    EmailExpandedView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);

            UpdateBottomStatus();
        }

        /// <summary>
        /// 点击“开始绑定”或“管理账号”按钮：原地变长（260px -> 475px）展开绑定配置表单。
        /// </summary>
        private void EmailActionButton_Click(object sender, RoutedEventArgs e)
        {
            int total = _presets.Count;
            if (total == 0) return;

            int activeIndex = (int)(Math.Round(CarouselPosition) % total + total) % total;
            var preset = _presets[activeIndex];

            var boundAccount = BarrageSettings.EmailAccounts
                .FirstOrDefault(a => string.Equals(a.ProviderType, preset.ProviderType, StringComparison.OrdinalIgnoreCase));

            _currentEditingAccount = boundAccount;
            _emailCardState = EmailCardState.BindingForm;

            // 填充表单数据
            PopulateBindingForm(preset, boundAccount);

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailExpandedView.Visibility = Visibility.Visible;
            EmailBindingFormView.Visibility = Visibility.Visible;
            EmailExpandedView.IsHitTestVisible = false;
            EmailBindingFormView.IsHitTestVisible = true;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 260;
            double startExpandedOpacity = EmailExpandedView.Opacity;
            double startFormOpacity = EmailBindingFormView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 475,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startFormOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.BindingForm)
                {
                    EmailExpandedView.Visibility = Visibility.Collapsed;
                    EmailBindingFormView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 填充绑定表单核心字段。
        /// </summary>
        private void PopulateBindingForm(EmailProviderPreset preset, EmailAccountConfigDto? account)
        {
            BindingProviderLogoImage.Source = GetPresetBitmap(preset.ImageFileName);
            BindingProviderNameText.Text = preset.DisplayName;
            BindingHelpGuideText.Text = preset.HelpGuideDescription;
            BindingStatusMessageText.Visibility = Visibility.Collapsed;

            if (preset.ProviderType == "Custom")
            {
                BindingCustomServerPanel.Visibility = Visibility.Visible;
                BindingServerInfoText.Text = "自定义标准 IMAP 协议服务";
                BindingServerHostBox.Text = account?.ServerHost ?? "";
                BindingServerPortBox.Text = (account?.ServerPort ?? 993).ToString();
                BindingUseSslCheckBox.IsChecked = account?.UseSsl ?? true;
            }
            else
            {
                BindingCustomServerPanel.Visibility = Visibility.Collapsed;
                BindingServerInfoText.Text = $"IMAP: {preset.DefaultHost} (SSL: {preset.DefaultPort})";
            }

            if (account != null)
            {
                BindingFormTitleText.Text = $"编辑 {preset.DisplayName}";
                BindingEmailAddressBox.Text = account.EmailAddress;
                BindingAuthCodeBox.Password = account.AuthCode;
                BindingDisplayNameBox.Text = account.DisplayName;
                BindingDeleteAccountButton.Visibility = Visibility.Visible;
            }
            else
            {
                BindingFormTitleText.Text = "绑定邮箱账号";
                BindingEmailAddressBox.Text = "";
                BindingAuthCodeBox.Password = "";
                BindingDisplayNameBox.Text = preset.DisplayName;
                BindingDeleteAccountButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 点击表单右上角关闭按钮或底部“取消”：卡片变短（475px -> 260px）返回 3D 选择视图。
        /// </summary>
        private void BindingForm_Close_Click(object sender, RoutedEventArgs e) => ReturnToSelectionView();
        private void BindingCancel_Click(object sender, RoutedEventArgs e) => ReturnToSelectionView();

        /// <summary>
        /// 执行从表单视图回退到 3D 选择视图的平滑变短动画。
        /// </summary>
        private void ReturnToSelectionView()
        {
            if (_emailCardState != EmailCardState.BindingForm) return;
            _emailCardState = EmailCardState.Selection;

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailExpandedView.Visibility = Visibility.Visible;
            EmailBindingFormView.Visibility = Visibility.Visible;
            EmailExpandedView.IsHitTestVisible = true;
            EmailBindingFormView.IsHitTestVisible = false;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 475;
            double startFormOpacity = EmailBindingFormView.Opacity;
            double startExpandedOpacity = EmailExpandedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 260,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startFormOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startExpandedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.Selection)
                {
                    EmailBindingFormView.Visibility = Visibility.Collapsed;
                    EmailExpandedView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);

            UpdateBottomStatus();
        }

        private void BindingField_TextChanged(object sender, RoutedEventArgs e)
        {
            BindingStatusMessageText.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 点击表单“测试并保存”：异步测试 IMAP 并在验证成功后加密落盘。
        /// </summary>
        private async void BindingSave_Click(object sender, RoutedEventArgs e)
        {
            int total = _presets.Count;
            if (total == 0) return;

            int activeIndex = (int)(Math.Round(CarouselPosition) % total + total) % total;
            var preset = _presets[activeIndex];

            string email = BindingEmailAddressBox.Text.Trim();
            string authCode = BindingAuthCodeBox.Password.Trim();
            string displayName = BindingDisplayNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = preset.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowBindingError("请输入有效的邮箱地址");
                BindingEmailAddressBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(authCode))
            {
                ShowBindingError("请输入邮箱授权码或客户端专用密码");
                BindingAuthCodeBox.Focus();
                return;
            }

            string host = preset.ProviderType == "Custom" ? BindingServerHostBox.Text.Trim() : preset.DefaultHost;
            int port = preset.ProviderType == "Custom" && int.TryParse(BindingServerPortBox.Text.Trim(), out int p) ? p : preset.DefaultPort;
            bool useSsl = preset.ProviderType == "Custom" ? (BindingUseSslCheckBox.IsChecked == true) : preset.DefaultUseSsl;

            if (string.IsNullOrWhiteSpace(host))
            {
                ShowBindingError("请输入 IMAP 服务器主机地址");
                BindingServerHostBox.Focus();
                return;
            }

            BindingSaveButton.IsEnabled = false;
            BindingSaveButton.Content = "正在测试连接...";
            BindingStatusMessageText.Visibility = Visibility.Collapsed;

            try
            {
                bool testSuccess = await Task.Run(async () =>
                {
                    using var client = new ImapClient();
                    client.ServerCertificateValidationCallback = (s, c, h, ex) => true;

                    var secureSocketOptions = useSsl
                        ? (port == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable)
                        : SecureSocketOptions.None;

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(host, port, secureSocketOptions, cts.Token);
                    await client.AuthenticateAsync(email, authCode, cts.Token);
                    await client.DisconnectAsync(true, CancellationToken.None);
                    return true;
                });

                if (testSuccess)
                {
                    var targetAccount = _currentEditingAccount ?? new EmailAccountConfigDto();
                    targetAccount.ProviderType = preset.ProviderType;
                    targetAccount.DisplayName = displayName;
                    targetAccount.EmailAddress = email;
                    targetAccount.ServerHost = host;
                    targetAccount.ServerPort = port;
                    targetAccount.UseSsl = useSsl;
                    targetAccount.IsEnabled = true;
                    targetAccount.AuthCode = authCode;

                    if (_currentEditingAccount == null)
                    {
                        BarrageSettings.EmailAccounts.Add(targetAccount);
                    }

                    BarrageSettings.ExportConfig();
                    ((App)Application.Current).EmailNotificationService?.ReloadAccounts();

                    UpdateEmailCollapsedSubtitle();
                    ReturnToSelectionView();
                }
            }
            catch (Exception ex)
            {
                ShowBindingError($"连接测试失败: {ex.Message}");
            }
            finally
            {
                BindingSaveButton.IsEnabled = true;
                BindingSaveButton.Content = "测试并保存";
            }
        }

        private void ShowBindingError(string msg)
        {
            BindingStatusMessageText.Text = msg;
            BindingStatusMessageText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 点击表单“删除账号”按钮。
        /// </summary>
        private void BindingDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingAccount != null)
            {
                BarrageSettings.EmailAccounts.RemoveAll(a => a.Id == _currentEditingAccount.Id);
                BarrageSettings.ExportConfig();
                ((App)Application.Current).EmailNotificationService?.ReloadAccounts();

                _currentEditingAccount = null;
                UpdateEmailCollapsedSubtitle();
                ReturnToSelectionView();
            }
        }

        /// <summary>
        /// 完全收起邮箱卡片至 136px。
        /// </summary>
        private void CollapseEmailCard()
        {
            if (_emailCardState == EmailCardState.Collapsed) return;

            var oldState = _emailCardState;
            _emailCardState = EmailCardState.Collapsed;

            UpdateEmailCollapsedSubtitle();

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            EmailCollapsedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, null);
            EmailListView.BeginAnimation(UIElement.OpacityProperty, null);

            EmailCollapsedView.Visibility = Visibility.Visible;
            EmailCollapsedView.IsHitTestVisible = true;
            EmailExpandedView.IsHitTestVisible = false;
            EmailBindingFormView.IsHitTestVisible = false;
            EmailListView.IsHitTestVisible = false;

            double startHeight = EmailCardBorder.ActualHeight > 0 ? EmailCardBorder.ActualHeight : 260;
            double startActiveOpacity = oldState == EmailCardState.BindingForm 
                ? EmailBindingFormView.Opacity 
                : (oldState == EmailCardState.ListView ? EmailListView.Opacity : EmailExpandedView.Opacity);
            double startCollapsedOpacity = EmailCollapsedView.Opacity;

            var heightAnim = new DoubleAnimation
            {
                From = startHeight,
                To = 136,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOutAnim = new DoubleAnimation
            {
                From = startActiveOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeInAnim = new DoubleAnimation
            {
                From = startCollapsedOpacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeOutAnim.Completed += (s, args) =>
            {
                if (_emailCardState == EmailCardState.Collapsed)
                {
                    EmailExpandedView.Visibility = Visibility.Collapsed;
                    EmailBindingFormView.Visibility = Visibility.Collapsed;
                    EmailListView.Visibility = Visibility.Collapsed;
                    EmailCollapsedView.Visibility = Visibility.Visible;
                }
            };

            EmailCardBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            if (oldState == EmailCardState.BindingForm)
            {
                EmailBindingFormView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            }
            else if (oldState == EmailCardState.ListView)
            {
                EmailListView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            }
            else
            {
                EmailExpandedView.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
            }
            EmailCollapsedView.BeginAnimation(UIElement.OpacityProperty, fadeInAnim);
        }

        /// <summary>
        /// 更新折叠视图下的副标题文本（显示绑定账号数）。
        /// </summary>
        private void UpdateEmailCollapsedSubtitle()
        {
            int count = BarrageSettings.EmailAccounts.Count(a => a.IsEnabled);
            if (count > 0)
            {
                EmailCollapsedSubtitle.Text = $"已连接 {count} 个邮箱账号";
            }
            else
            {
                EmailCollapsedSubtitle.Text = "绑定你的邮箱以接收邮件";
            }
        }

        private void PrevProvider_Click(object sender, RoutedEventArgs e)
        {
            AnimateToPosition(CarouselPosition - 1.0);
        }

        private void NextProvider_Click(object sender, RoutedEventArgs e)
        {
            AnimateToPosition(CarouselPosition + 1.0);
        }

        /// <summary>
        /// 直接滑动跳转到指定索引的卡片。
        /// </summary>
        private void AnimateToPresetIndex(int targetIndex)
        {
            int count = _presets.Count;
            if (count == 0) return;

            double diff = targetIndex - (CarouselPosition % count);
            while (diff > count / 2.0) diff -= count;
            while (diff < -count / 2.0) diff += count;

            AnimateToPosition(CarouselPosition + diff);
        }

        /// <summary>
        /// 启动 60fps 连续缓动动画平滑滚动到目标位置。
        /// </summary>
        private void AnimateToPosition(double targetPos)
        {
            var anim = new DoubleAnimation
            {
                To = targetPos,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, e) =>
            {
                int total = _presets.Count;
                if (total > 0)
                {
                    double normalized = (CarouselPosition % total + total) % total;
                    CarouselPosition = Math.Round(normalized);
                }
                UpdateBottomStatus();
            };

            BeginAnimation(CarouselPositionProperty, anim);
        }

        /// <summary>
        /// 刷新底部绑定状态与操作按钮。
        /// </summary>
        private void UpdateBottomStatus()
        {
            int total = _presets.Count;
            if (total == 0) return;

            int activeIndex = (int)(Math.Round(CarouselPosition) % total + total) % total;
            var currentPreset = _presets[activeIndex];

            var boundAccount = BarrageSettings.EmailAccounts
                .FirstOrDefault(a => string.Equals(a.ProviderType, currentPreset.ProviderType, StringComparison.OrdinalIgnoreCase));

            if (boundAccount != null)
            {
                EmailStatusText.Text = $"已绑定: {boundAccount.EmailAddress}";
                EmailActionButton.Content = "管理账号";
                GoToEmailBarrageSettingsButton.Visibility = Visibility.Visible;
            }
            else
            {
                EmailStatusText.Text = "暂未绑定";
                EmailActionButton.Content = "开始绑定";
                GoToEmailBarrageSettingsButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 点击“前往弹幕设置”：一键跳转至弹幕设置页面并自动定位到邮箱弹幕设置范围。
        /// </summary>
        private void GoToEmailBarrageSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is SettingsWindow settingsWindow)
            {
                var vm = settingsWindow.DataContext as SettingsViewModel;
                if (vm != null)
                {
                    vm.SetNotificationCategoryCommand.Execute("Email");
                    int total = _presets.Count;
                    if (total > 0)
                    {
                        int activeIndex = (int)(Math.Round(CarouselPosition) % total + total) % total;
                        var currentPreset = _presets[activeIndex];
                        var targetScope = vm.EmailScopes.FirstOrDefault(s => s.Account != null && string.Equals(s.Account.ProviderType, currentPreset.ProviderType, StringComparison.OrdinalIgnoreCase));
                        if (targetScope != null)
                        {
                            vm.SelectedEmailScope = targetScope;
                        }
                    }
                }
                settingsWindow.RootNavigation.Navigate(typeof(CustomPage));
            }
        }

        /// <summary>
        /// 从本地 Images/Email 目录读取位图并进行缓存。
        /// </summary>
        private BitmapImage? GetPresetBitmap(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            if (_iconCache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Email", fileName);
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    _iconCache[fileName] = bmp;
                    return bmp;
                }
            }
            catch { }

            return null;
        }

        #endregion

        /// <summary>
        /// 3D CoverFlow 实体卡片项数据载体。
        /// </summary>
        private sealed class CoverflowCardItem
        {
            public int Index { get; set; }
            public required Border Border { get; set; }
            public required ScaleTransform ScaleTrans { get; set; }
            public required SkewTransform SkewTrans { get; set; }
            public required TranslateTransform TranslateTrans { get; set; }
            public required DropShadowEffect ShadowEffect { get; set; }
            public required EmailProviderPreset Preset { get; set; }
        }
    }
}
