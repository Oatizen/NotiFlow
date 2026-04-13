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
using CommunityToolkit.Mvvm.Messaging;
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

        // ===== 游戏引擎渲染层 =====
        private readonly List<BarrageItem> _activeItems = new();
        private readonly Queue<BarrageItem> _pool = new(); // 🚀 对象复用池
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private double _screenWidth;

        static MainWindow()
        {
            // 在 WPF 属性底层用强制回调锁死背景为全透明，彻底免疫 WpfUi 内部对所有 Window 背景色的暴力推翻注入
            FrameworkPropertyMetadata metadata = new FrameworkPropertyMetadata(Brushes.Transparent, null, CoerceBackground);
            BackgroundProperty.OverrideMetadata(typeof(MainWindow), metadata);
        }

        private static object CoerceBackground(DependencyObject d, object baseValue)
        {
            return Brushes.Transparent; // 无论外界如何修改，永远只返回 Transparent
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 配置导入已迁移至 App.OnStartup 统一管理

            InitializeTracks();

            // 绑定 Esc 键退出
            KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    Application.Current.Shutdown();
                }
            };

            // 【架构重构：绑定游戏绘图级回调帧】
            _screenWidth = SystemParameters.PrimaryScreenWidth;
            CompositionTarget.Rendering += CompositionTarget_Rendering;

            // （已注销局部预览信使机制，移交至 CustomPage 画布）

            // 订阅通知事件
            _notificationService.OnNotificationReceived += (msg) =>
            {
                if (!BarrageSettings.IsWorking) return;

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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 1像素欺骗法：手动覆盖屏幕铺满不使用最大化，规避第三方任务栏软件的误判
            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight - 1;

            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            
            // 复合样式：
            // WS_EX_TRANSPARENT 允许鼠标穿透
            // WS_EX_TOOLWINDOW 隐藏常规窗口属性避免捕捉
            // WS_EX_NOACTIVATE 避免抢占当前前台焦点
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, 
                extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
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
            // 🚀 从对象池抓取死掉的图层，如果不空闲才 new，杜绝反复开启 GC
            BarrageItem item;
            if (_pool.Count > 0)
            {
                item = _pool.Dequeue();
                // 唤醒它
                item.IsAlive = true;
                item.TrackReleased = false;
                
                // 将重新启用的死魂灵重新挂入宿主（它之前死掉的时候被我们移除了引用）
                EngineHost.AddVisual(item);
            }
            else
            {
                item = new BarrageItem();
                EngineHost.AddVisual(item);
            }
            
            item.TrackIndex = track;
            
            // 将设置的颜色解冻，这是后续使用对象池的核心技术基础，但当前先进行普通的独立绘制
            // 使用设置中的统一色彩，抛弃之前的随机色彩数组
            Brush unFrozenBrush = BarrageSettings.TextColor.Clone();
            unFrozenBrush.Freeze();

            FontFamily fontFamily = BarrageSettings.FontFamily;
            double topPosition = TopMargin + track * TrackHeight;
            
            // 进行像素级的预绘制烘焙
            item.BuildVisual(message, unFrozenBrush, BarrageSettings.FontSize, fontFamily, BarrageSettings.FontStyle, BarrageSettings.FontWeight);

            // 物理位置注册
            item.CurrentX = _screenWidth;
            item.CurrentY = topPosition;
            item.Offset = new Vector(item.CurrentX, item.CurrentY);
            
            double speedPixelsPerSec = BarrageSettings.ScrollSpeedCharsPerSec * BarrageSettings.FontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10;
            item.SpeedPixelsPerSec = speedPixelsPerSec;

            // 送入底层实体执行链
            _activeItems.Add(item);
        }

        // ===== 核心物理引擎（帧率节流至 ~30fps 以减少 DWM 全屏 Alpha 合成次数） =====
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;

            double dt = (_lastRenderTime == TimeSpan.Zero) ? 0 : (renderingArgs.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = renderingArgs.RenderingTime;

            if (dt == 0) return;

            // 如果当前没有任何存活弹幕
            if (_activeItems.Count == 0)
            {
                // 当用户关闭了工作开关且所有弹幕已飞完，自动隐藏透明窗口释放桌面资源
                if (!BarrageSettings.IsWorking && this.IsVisible)
                {
                    this.Hide();
                }
                return;
            }

            // 反向遍历以免由于 List.Remove 引发集合修改报错
            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                var item = _activeItems[i];
                
                // 进行平滑的匀速运动学运算
                item.CurrentX -= item.SpeedPixelsPerSec * dt;
                
                // 让 GPU 指令直接移动此对象而不需要重排布局
                item.Offset = new Vector(item.CurrentX, item.CurrentY);

                // 物理碰撞测算（轨道提早释放给新弹幕）：一旦本弹幕尾部完全脱离屏幕右侧一段安全距离
                if (!item.TrackReleased && (item.CurrentX + item.PhysicalWidth < _screenWidth - _screenWidth / 4.0))
                {
                    item.TrackReleased = true;
                    ReleaseTrack(item.TrackIndex);
                }

                // 屏幕越界销毁，此时它的本体都已消失在左侧
                if (item.CurrentX < -item.PhysicalWidth)
                {
                    item.IsAlive = false;
                    
                    // 从图形宿主剥离以断开内存引用，避免画面产生驻留错位
                    EngineHost.RemoveVisual(item);
                    _activeItems.RemoveAt(i);
                    
                    // 🚀 核心：塞入对象池复用，而不是扔给内存垃圾收集器
                    _pool.Enqueue(item);
                }
            }
        }
    }
}
