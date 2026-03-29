using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Documents;
using NotiFlow.Models;
using NotiFlow.Services;

namespace NotiFlow
{
    public partial class MainWindow : Window
    {
        private readonly Random _random = new();

        // ===== 轨道管理系统 =====
        /// <summary>
        /// 轨道间距（像素），动态计算当前字号 + 上下留白。
        /// </summary>
        private double TrackHeight => BarrageSettings.FontSize + 16;

        /// <summary>
        /// 屏幕顶部留白（像素），避免弹幕紧贴屏幕顶边。
        /// </summary>
        private const int TopMargin = 20;

        /// <summary>
        /// 轨道占用状态表。true = 该轨道正在被某条弹幕使用。
        /// </summary>
        private bool[] _trackOccupied = Array.Empty<bool>();

        /// <summary>
        /// 可用轨道总数，根据屏幕高度动态计算。
        /// </summary>
        private int _trackCount;

        /// <summary>
        /// 待发弹幕队列。当所有轨道都被占满时，新弹幕在此排队。
        /// </summary>
        private readonly Queue<NotificationMessage> _pendingMessages = new();

        /// <summary>
        /// 弹幕可用颜色池，随机分配以增强视觉区分度。
        /// </summary>
        private readonly Brush[] _colors =
        {
            Brushes.White,
            Brushes.Cyan,
            Brushes.Yellow,
            Brushes.LimeGreen,
            Brushes.Orange,
            Brushes.HotPink,
            Brushes.Gold,
            Brushes.LightSkyBlue,
        };

        private readonly NotificationService _notificationService = new();

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动时自动尝试导入曾经落盘的配置文件（带安全回落防注入机制）
            BarrageSettings.ImportConfig();

            InitializeTracks();
            EnableClickThrough();

            // 绑定 Esc 键退出
            KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    Application.Current.Shutdown();
                }
            };

            // 订阅通知事件
            _notificationService.OnNotificationReceived += (msg) =>
            {
                // 必须回到 UI 线程执行
                Application.Current.Dispatcher.Invoke(() =>
                {
                    EnqueueBarrage(msg);
                });
            };

            bool success = await _notificationService.InitializeAsync();
            if (!success)
            {
                MessageBox.Show("获取通知系统读取权限失败！\n\n请前往 Windows 设置 -> 隐私和安全性 -> 通知，允许桌面应用获取通知。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
            }
        }

        private void InitializeTracks()
        {
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double usableHeight = screenHeight - TopMargin - TrackHeight;
            _trackCount = Math.Max(1, (int)(usableHeight / TrackHeight));
            _trackOccupied = new bool[_trackCount];
        }

        private int AllocateTrack()
        {
            int goldenTrackIndex = _trackCount / 3;
            var candidates = new List<(int TrackIndex, double Score)>();
            for (int i = 0; i < _trackCount; i++)
            {
                if (!_trackOccupied[i])
                {
                    int distance = Math.Abs(i - goldenTrackIndex);
                    double score = i <= goldenTrackIndex ? distance : distance * 1.8;
                    candidates.Add((i, score));
                }
            }

            if (candidates.Count == 0) return -1;

            double bestScore = candidates.Min(c => c.Score);
            var bestCandidates = candidates.Where(c => c.Score == bestScore).ToList();
            int chosen = bestCandidates[_random.Next(bestCandidates.Count)].TrackIndex;

            _trackOccupied[chosen] = true;
            return chosen;
        }

        private void ReleaseTrack(int trackIndex)
        {
            if (trackIndex >= 0 && trackIndex < _trackCount)
            {
                _trackOccupied[trackIndex] = false;
            }
            TryFlushQueue();
        }

        private void TryFlushQueue()
        {
            while (_pendingMessages.Count > 0)
            {
                int track = AllocateTrack();
                if (track < 0) break;
                var message = _pendingMessages.Dequeue();
                LaunchBarrage(message, track);
            }
        }

        private void EnableClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, extendedStyle | NativeMethods.WS_EX_TRANSPARENT);
        }

        private void EnqueueBarrage(NotificationMessage msg)
        {
            int track = AllocateTrack();
            if (track >= 0)
            {
                LaunchBarrage(msg, track);
            }
            else
            {
                _pendingMessages.Enqueue(msg);
            }
        }

        private void LaunchBarrage(NotificationMessage message, int track)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            var color = _colors[_random.Next(_colors.Length)];

            // 1. 构建最外层容器 (可能带有背景)
            var border = new Border
            {
                CornerRadius = BarrageSettings.BackgroundCornerRadius,
                Padding = new Thickness(12, 6, 12, 6)
            };

            if (BarrageSettings.ShowBackground)
            {
                var bgBrush = BarrageSettings.BackgroundColor.Clone();
                bgBrush.Opacity = BarrageSettings.BackgroundOpacity;
                border.Background = bgBrush;
            }

            // 2. 构建水平栈组合控件
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 3. 构建应用图标
            if (BarrageSettings.ShowAppIcon && message.AppIcon != null)
            {
                var image = new Image
                {
                    Source = message.AppIcon,
                    Width = BarrageSettings.FontSize, // 图标物理尺寸跟随字号大小以保持协调
                    Height = BarrageSettings.FontSize,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                stackPanel.Children.Add(image);
            }

            // 4. 构建文字内容
            var textBlock = new TextBlock
            {
                FontFamily = BarrageSettings.FontFamily,
                FontSize = BarrageSettings.FontSize,
                FontWeight = BarrageSettings.FontWeight,
                FontStyle = BarrageSettings.FontStyle,
                Opacity = BarrageSettings.TextOpacity,
                Foreground = color,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 性能终极优化：WPF 的高强度模糊发光阴影是造成 4060Ti 跑满 50% 的“罪魁祸首”！
            // 如果用户开启了底层黑色遮罩板，就不需要文字阴影；如果没有开启底板，则使用显卡消耗极低的“硬笔划描边”阴影代替
            if (!BarrageSettings.ShowBackground)
            {
                textBlock.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 1, // 降低模糊换取指数级性能提升
                    ShadowDepth = 1.5,
                    Opacity = 0.9,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                };
            }

            if (BarrageSettings.IsUnderlined)
            {
                textBlock.TextDecorations = TextDecorations.Underline;
            }

            // 拼接可读文本文字
            string prefix = "";
            if (BarrageSettings.ShowAppName && !string.IsNullOrEmpty(message.AppName))
            {
                prefix += $"[{message.AppName}] ";
            }
            if (!string.IsNullOrEmpty(message.Title))
            {
                prefix += $"【{message.Title}】";
            }

            string bodyText = message.Body ?? "";
            bool isTruncated = false;
            if (bodyText.Length > BarrageSettings.MaxTextLength)
            {
                bodyText = bodyText.Substring(0, BarrageSettings.MaxTextLength);
                isTruncated = true;
            }

            // 使用 Inlines 实现同一行文字不同颜色
            textBlock.Inlines.Add(new Run(prefix + bodyText));
            if (isTruncated)
            {
                var ellipsisRun = new Run("......");
                if (BarrageSettings.HighlightEllipsis)
                {
                    ellipsisRun.Foreground = BarrageSettings.EllipsisColor;
                }
                textBlock.Inlines.Add(ellipsisRun);
            }

            stackPanel.Children.Add(textBlock);
            border.Child = stackPanel;

            // 开启位图缓存技术，将原本复杂的文字与图片矢量重绘计算，降维降级成显卡最擅长的常规 2D 贴图平移，可大幅降低 GPU 和 CPU 负载
            border.CacheMode = new BitmapCache();

            // 5. 加入 WPF 画布舞台并计算物理宽度
            double topPosition = TopMargin + track * TrackHeight;
            Canvas.SetLeft(border, screenWidth);
            Canvas.SetTop(border, topPosition);
            BarrageCanvas.Children.Add(border);

            // 修复由于动画执行时 UI 还在排队渲染导致计算元素宽度为 0 引起的重叠 Bug。
            // 封杀滞后的 UpdateLayout 方法，改用最激进直接预处理的 Measure 强行要求 WPF 交出实际将要占用的像素总宽
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double elementWidth = border.DesiredSize.Width > 0 ? border.DesiredSize.Width : 800;
            double targetX = -elementWidth;

            // 物理移动速度：字数/秒 转换为 像素/秒 (Speed = CharsPerSec * FontSize)
            // 使得短弹幕和长弹幕移动“横向速度”完全一致，防碰撞逻辑也因此更加完美死锁，永远不会发生追尾
            double speedPixelsPerSec = BarrageSettings.ScrollSpeedCharsPerSec * BarrageSettings.FontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10; // 保底防除零或卡死

            double totalDistance = screenWidth + elementWidth;
            double duration = totalDistance / speedPixelsPerSec;

            // ===== 计算提前释放轨道的时间 =====
            double releaseDistance = elementWidth + screenWidth / 4.0;
            double releaseDelay = duration * (releaseDistance / totalDistance);
            releaseDelay = Math.Min(releaseDelay, duration);

            var releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(releaseDelay) };
            releaseTimer.Tick += (s, e) =>
            {
                releaseTimer.Stop();
                ReleaseTrack(track);
            };
            releaseTimer.Start();

            var animation = new DoubleAnimation
            {
                From = screenWidth,
                To = targetX,
                Duration = TimeSpan.FromSeconds(duration),
            };

            animation.Completed += (s, e) =>
            {
                BarrageCanvas.Children.Remove(border);
            };

            border.BeginAnimation(Canvas.LeftProperty, animation);
        }
    }
}
