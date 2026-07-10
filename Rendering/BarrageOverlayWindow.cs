using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NotiFlow.Models;
using NotiFlow.Services;
using Microsoft.Graphics.Canvas;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
// 别名避免与 System.Windows.Media.CompositionTarget 冲突
using WinCompTarget = System.Windows.Media.CompositionTarget;

namespace NotiFlow.Rendering
{
    public class BarrageOverlayWindow : IDisposable
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private NativeMethods.WndProc _wndProcDelegate;
        private bool _disposed;

        // ===== Windows.UI.Composition 渲染核心 =====
        private Compositor _compositor;
        private DesktopWindowTarget _compositionTarget;
        private Windows.UI.Composition.ContainerVisual _rootContainer;
        private CompositionGraphicsDevice _graphicsDevice;
        private CanvasDevice _canvasDevice;

        /// <summary>
        /// 保持 DispatcherQueueController COM 引用存活，防止 GC 回收。
        /// Compositor 依赖当前线程的 DispatcherQueue，若控制器被回收则合成器将失效。
        /// </summary>
        private IntPtr _dispatcherQueueController;

        private int _left;
        private int _top;
        private int _width;
        private int _height;

        private readonly Random _random = new();

        // ===== 轨道管理系统 =====
        private double TrackHeight => BarrageSettings.FontSize + 16;
        private const int TopMargin = 20;
        private bool[] _trackOccupied = Array.Empty<bool>();
        private int _trackCount;

        private readonly Queue<NotificationMessage> _pendingMessages = new();
        private readonly ConcurrentQueue<NotificationMessage> _spawnQueue = new();
        private readonly ConcurrentQueue<BarrageItem> _spriteReadyQueue = new();
        private readonly List<BarrageItem> _activeItems = new();
        private readonly Queue<BarrageItem> _pool = new();
        
        private TimeSpan _lastRenderTime = TimeSpan.Zero;

        public bool IsLoaded => _hwnd != IntPtr.Zero;
        public bool IsVisible { get; private set; }

        public BarrageOverlayWindow()
        {
            _wndProcDelegate = WndProc;
            
            CreateWindow();
            InitializeTracks();
            
            System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;

            NotificationService.Instance!.OnNotificationReceived += (msg) =>
            {
                if (!BarrageSettings.IsWorking) return;
                _spawnQueue.Enqueue(msg);
            };
        }

        private void CreateWindow()
        {
            _left = (int)SystemParameters.VirtualScreenLeft;
            _top = (int)SystemParameters.VirtualScreenTop;
            _width = (int)SystemParameters.VirtualScreenWidth;
            _height = (int)SystemParameters.VirtualScreenHeight - 1;

            string className = "NotiFlowBarrageOverlayClass";

            IntPtr hInstance = Marshal.GetHINSTANCE(typeof(BarrageOverlayWindow).Module);

            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX)),
                style = 0,
                lpfnWndProc = _wndProcDelegate,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
                hIconSm = IntPtr.Zero
            };

            NativeMethods.RegisterClassEx(ref wndClass);

            // 使用 WS_EX_NOREDIRECTIONBITMAP 替代 WS_EX_LAYERED，
            // 让 DWM 不为此窗口分配重定向位图，而是由 Composition 引擎直接渲染
            int exStyle = NativeMethods.WS_EX_NOREDIRECTIONBITMAP
                        | NativeMethods.WS_EX_TRANSPARENT
                        | NativeMethods.WS_EX_TOOLWINDOW
                        | NativeMethods.WS_EX_NOACTIVATE
                        | 0x00000008 /* WS_EX_TOPMOST */;
            int style = NativeMethods.WS_POPUP;

            // 创建窗口，故意不传入标题 (string.Empty)，
            // 因为许多截图工具（如微信、QQ、Snipping Tool）在遍历窗口时，
            // 会自动过滤掉没有标题的无边框窗口，从而可能绕过“按窗口截图”的捕捉。
            _hwnd = NativeMethods.CreateWindowEx(
                exStyle,
                className,
                string.Empty,
                style,
                _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            InitializeRendering();

            ApplyCaptureSetting();
            RegisterGlobalHotKey(_hwnd);
        }

        /// <summary>
        /// 初始化 Windows.UI.Composition 渲染管线。
        /// 创建顺序：DispatcherQueue → Compositor → DesktopWindowTarget → 根容器 → Win2D 设备。
        /// DispatcherQueue 必须在 Compositor 之前创建，否则 Compositor 构造函数会抛出异常。
        /// </summary>
        private void InitializeRendering()
        {
            // 1. 创建 DispatcherQueueController（Compositor 需要当前线程具备消息泵）
            var options = new NativeMethods.DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<NativeMethods.DispatcherQueueOptions>(),
                threadType = 2,    // DQTYPE_CURRENT_THREAD
                apartmentType = 2  // DQTAT_COM_STA
            };
            NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);

            // 2. 创建 OS 级 Compositor 并通过 ICompositorDesktopInterop 绑定到 HWND
            _compositor = new Compositor();
            var interop = _compositor.As<NativeMethods.ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(_hwnd, false, out var rawTarget);
            _compositionTarget = MarshalInterface<DesktopWindowTarget>.FromAbi(rawTarget);
            Marshal.Release(rawTarget);

            // 3. 创建根容器视觉并设为合成目标的根
            _rootContainer = _compositor.CreateContainerVisual();
            _rootContainer.Size = new Vector2(_width, _height);
            _compositionTarget.Root = _rootContainer;

            // 4. 追加 WS_EX_LAYERED 以恢复鼠标穿透。
            //    WS_EX_TRANSPARENT 的穿透行为依赖 WS_EX_LAYERED。
            //    WS_EX_NOREDIRECTIONBITMAP + WS_EX_LAYERED 可以共存：
            //    DWM 由 Composition 引擎提供内容，WS_EX_LAYERED 仅影响命中测试语义。
            IntPtr curStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
                (IntPtr)((long)curStyle | NativeMethods.WS_EX_LAYERED));

            // 4. 创建共享同一 D3D11 设备的 CompositionGraphicsDevice 和 CanvasDevice
            //    通过 CompositionHelper 从零创建 D3D11 设备（替代 CanvasComposition）
            (_graphicsDevice, _canvasDevice) = CompositionHelper.CreateSharedDevices(_compositor);
        }

        public void Show()
        {
            if (_disposed || IsVisible) return;
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            IsVisible = true;
        }

        public void Hide()
        {
            if (_disposed || !IsVisible) return;
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            IsVisible = false;
        }

        public void Close()
        {
            Dispose();
        }

        private void InitializeTracks()
        {
            double usableHeight = _height - TopMargin - TrackHeight;
            _trackCount = Math.Max(1, (int)(usableHeight / TrackHeight));
            _trackOccupied = new bool[_trackCount];
            System.IO.File.AppendAllText("barrage_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] InitializeTracks: Height={_height}, TrackHeight={TrackHeight}, Count={_trackCount}\n");
        }

        private int AllocateTrack()
        {
            return BarrageSettings.TrackStrategy switch
            {
                "TopFirst" => AllocateTrackTopFirst(),
                "BottomFirst" => AllocateTrackBottomFirst(),
                _ => AllocateTrackUpperCenter()
            };
        }

        private int AllocateTrackUpperCenter()
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

        private int AllocateTrackTopFirst()
        {
            for (int i = 0; i < _trackCount; i++)
            {
                if (!_trackOccupied[i])
                {
                    _trackOccupied[i] = true;
                    return i;
                }
            }
            return -1;
        }

        private int AllocateTrackBottomFirst()
        {
            for (int i = _trackCount - 1; i >= 0; i--)
            {
                if (!_trackOccupied[i])
                {
                    _trackOccupied[i] = true;
                    return i;
                }
            }
            return -1;
        }

        private void ReleaseTrack(int trackIndex)
        {
            System.IO.File.AppendAllText("barrage_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] ReleaseTrack: {trackIndex}\n");
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
                PrepareBarrage(message, track);
            }
        }

        public void ApplyCaptureSetting()
        {
            if (_hwnd == IntPtr.Zero) return;

            if (BarrageSettings.AllowCapture)
            {
                NativeMethods.SetWindowDisplayAffinity(_hwnd, 0x00000000);
            }
            else
            {
                bool apiSuccess = NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                if (!apiSuccess)
                {
                    NativeMethods.SetWindowDisplayAffinity(_hwnd, 0x00000001);
                }
            }
        }

        private void RegisterGlobalHotKey(IntPtr hwnd)
        {
            NativeMethods.RegisterHotKey(hwnd, 9000, BarrageSettings.HotKeyModifier, BarrageSettings.HotKey);
        }

        private void UnregisterGlobalHotKey(IntPtr hwnd)
        {
            NativeMethods.UnregisterHotKey(hwnd, 9000);
        }

        public void ReRegisterHotKey()
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterGlobalHotKey(_hwnd);
                RegisterGlobalHotKey(_hwnd);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_HOTKEY = 0x0312;
            const uint WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (msg == WM_HOTKEY && (int)wParam == 9000)
            {
                var trayService = (App.Current as App)?.TrayIconService;
                trayService?.RefreshWorkingStateFromHotKey();
                return IntPtr.Zero;
            }

            if (msg == WM_NCHITTEST)
            {
                return (IntPtr)HTTRANSPARENT;
            }

            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void EnqueueBarrage(NotificationMessage msg)
        {
            int track = AllocateTrack();
            System.IO.File.AppendAllText("barrage_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] EnqueueBarrage: track={track}, pending={_pendingMessages.Count}\n");
            if (track >= 0)
            {
                PrepareBarrage(msg, track);
            }
            else
            {
                _pendingMessages.Enqueue(msg);
            }
        }

        /// <summary>
        /// 在 UI 线程上提取所有 WPF 依赖的纯值数据，然后将弹幕纹理构建工作
        /// 通过 Task.Run 移交到后台线程执行。
        /// 后台线程使用 CompositionGraphicsDevice 创建 SpriteVisual + DrawingSurface，
        /// 完成后将弹幕推入就绪队列等待合成。
        /// </summary>
        private void PrepareBarrage(NotificationMessage message, int track)
        {
            BarrageItem item;
            if (_pool.Count > 0)
            {
                item = _pool.Dequeue();
                item.Reset();
            }
            else
            {
                item = new BarrageItem();
            }
            
            item.TrackIndex = track;

            // ===== 在 UI 线程上提取所有 WPF 依赖的纯值数据 =====
            var textBrush = (SolidColorBrush)BarrageSettings.TextColor.Clone();
            textBrush.Freeze();
            var textColor = textBrush.Color;

            string fontFamilyName = BarrageSettings.FontFamily.Source;
            double fontSize = BarrageSettings.FontSize;
            var fontStyle = BarrageSettings.FontStyle;
            var fontWeight = BarrageSettings.FontWeight;
            double topPosition = TopMargin + track * TrackHeight;
            double speedPixelsPerSec = BarrageSettings.ScrollSpeedCharsPerSec * fontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10;

            // 预提取背景设置
            bool showBackground = BarrageSettings.ShowBackground;
            var bgBrush = BarrageSettings.BackgroundColor as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Colors.Black);
            var bgColor = bgBrush.Color;
            double bgOpacity = BarrageSettings.BackgroundOpacity;
            double textOpacity = BarrageSettings.TextOpacity;
            var cornerRadius = BarrageSettings.BackgroundCornerRadius;

            // 预提取省略号设置
            bool highlightEllipsis = BarrageSettings.HighlightEllipsis;
            var ellBrush = BarrageSettings.EllipsisColor as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Colors.White);
            var ellColor = ellBrush.Color;

            // 预提取文本内容
            bool showAppName = BarrageSettings.ShowAppName;
            double maxTextLen = BarrageSettings.MaxTextLength;
            bool isUnderlined = BarrageSettings.IsUnderlined;

            // 预提取图标像素（WPF 对象只能在 UI 线程访问）
            bool showAppIcon = BarrageSettings.ShowAppIcon;
            byte[]? iconPixels = null;
            int iconWidth = 0, iconHeight = 0;
            bool isUwpIcon = message.IsUwpIcon;
            if (showAppIcon && message.AppIcon is System.Windows.Media.Imaging.BitmapSource bmpSrc)
            {
                try
                {
                    var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        bmpSrc, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
                    iconWidth = formatted.PixelWidth;
                    iconHeight = formatted.PixelHeight;
                    if (iconWidth > 0 && iconHeight > 0)
                    {
                        iconPixels = new byte[iconWidth * iconHeight * 4];
                        formatted.CopyPixels(iconPixels, iconWidth * 4, 0);
                    }
                }
                catch { showAppIcon = false; }
            }

            string appName = message.AppName ?? "";
            string title = message.Title ?? "";
            string body = message.Body ?? "";

            // ===== 纹理构建移到后台线程 =====
            var compositor = _compositor;
            var graphicsDevice = _graphicsDevice;
            var canvasDevice = _canvasDevice;
            int screenWidth = _width;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    item.PrepareLayout(canvasDevice,
                        appName, title, body,
                        textColor, textOpacity, fontSize, fontFamilyName, fontStyle, fontWeight,
                        showBackground, bgColor, bgOpacity, cornerRadius,
                        highlightEllipsis, ellColor,
                        showAppName, maxTextLen, isUnderlined,
                        showAppIcon, iconPixels, iconWidth, iconHeight, isUwpIcon);
                    item.CurrentX = screenWidth;
                    item.CurrentY = topPosition;
                    item.SpeedPixelsPerSec = speedPixelsPerSec;
                    item.StartX = screenWidth;
                }
                catch { }
                _spriteReadyQueue.Enqueue(item);
            });
        }

        /// <summary>
        /// 将已完成纹理构建的弹幕添加到合成视觉树，并启动滚动动画。
        /// 动画由 Compositor 在 GPU 端驱动，无需每帧手动更新位置。
        /// </summary>
        private void CommitBarrage(BarrageItem item)
        {
            if (item.Visual == null)
            {
                System.IO.File.AppendAllText("barrage_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] CommitBarrage: Visual is NULL! Track={item.TrackIndex}\n");
                item.TrackReleased = true;
                ReleaseTrack(item.TrackIndex);
                item.IsAlive = false;
                _pool.Enqueue(item);
                return;
            }

            System.IO.File.AppendAllText("barrage_log.txt", $"[{DateTime.Now:HH:mm:ss.fff}] CommitBarrage: Success. Track={item.TrackIndex}, Width={item.PhysicalWidth}, CurrentX={item.CurrentX}\n");

            // 将弹幕的 SpriteVisual 添加到合成树
            _rootContainer.Children.InsertAtTop(item.Visual);

            // 创建从屏幕右端到完全离开左端的匀速滚动动画
            var linear = _compositor.CreateLinearEasingFunction();
            var animation = _compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0f, new Vector3((float)item.CurrentX, (float)item.CurrentY, 0f), linear);
            animation.InsertKeyFrame(1f, new Vector3(-(float)item.PhysicalWidth, (float)item.CurrentY, 0f), linear);

            double totalDistance = item.CurrentX + item.PhysicalWidth;
            double durationSec = totalDistance / item.SpeedPixelsPerSec;
            animation.Duration = TimeSpan.FromSeconds(durationSec);

            item.Visual.StartAnimation("Offset", animation);
            item.AnimationStartTime = DateTime.UtcNow;
            item.AnimationEndTime = DateTime.UtcNow.AddSeconds(durationSec);

            _activeItems.Add(item);
        }

        /// <summary>
        /// WPF CompositionTarget.Rendering 回调，驱动弹幕生命周期管理。
        /// 不再执行任何像素操作——滚动动画由合成器自动驱动。
        /// 此回调仅负责：消息入队、弹幕提交、轨道释放判断、过期弹幕清理。
        /// </summary>
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_disposed) return;

            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;
            _lastRenderTime = renderingArgs.RenderingTime;

            // 每帧处理所有待入队的通知消息（避免 20 条通知只取 1 条的积压问题）
            while (_spawnQueue.TryDequeue(out var spawnMsg))
            {
                if (((App)Application.Current).ForegroundMonitor is { } monitor && !monitor.IsSceneSuppressed)
                {
                    EnqueueBarrage(spawnMsg);
                }
            }

            // 接收后台线程完成的弹幕布局，并在 UI 线程创建 Composition 视觉对象
            // 限制每帧最多创建 2-3 个纹理，防止单帧内向 DWM 提交过多表面分配请求导致 D3D/DXGI 异常（引发弹幕丢失）
            int maxCommitsPerFrame = 3;
            int commitCount = 0;
            while (commitCount < maxCommitsPerFrame && _spriteReadyQueue.TryDequeue(out var readyItem))
            {
                try
                {
                    readyItem.CreateVisualForComposition(_canvasDevice, _compositor, _graphicsDevice);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CreateVisualFailed: {ex.Message}");
                } // 如果抛异常，Visual 会保持为 null，由 CommitBarrage 处理回收

                CommitBarrage(readyItem);
                commitCount++;
            }

            if (_activeItems.Count == 0 && _spawnQueue.IsEmpty && _pendingMessages.Count == 0)
            {
                if (!BarrageSettings.IsWorking && IsVisible)
                {
                    Hide();
                }
                return;
            }

            // 遍历活跃弹幕，处理轨道释放和生命周期结束
            var now = DateTime.UtcNow;
            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                var item = _activeItems[i];
                double elapsed = (now - item.AnimationStartTime).TotalSeconds;
                double totalDuration = (item.AnimationEndTime - item.AnimationStartTime).TotalSeconds;

                // 轨道释放判断：根据动画进度推算当前位置，
                // 当弹幕尾部通过屏幕右侧 3/4 处时释放轨道，允许下一条弹幕进入
                if (!item.TrackReleased && totalDuration > 0)
                {
                    double progress = elapsed / totalDuration;
                    double currentX = item.StartX + ((-item.PhysicalWidth) - item.StartX) * progress;
                    if (currentX + item.PhysicalWidth < _width - _width / 4.0)
                    {
                        item.TrackReleased = true;
                        ReleaseTrack(item.TrackIndex);
                    }
                }

                // 弹幕动画结束，从合成树移除并回收
                if (now >= item.AnimationEndTime)
                {
                    if (item.Visual != null)
                    {
                        _rootContainer.Children.Remove(item.Visual);
                    }
                    item.IsAlive = false;
                    _activeItems.RemoveAt(i);
                    _pool.Enqueue(item);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering;

            if (_hwnd != IntPtr.Zero)
            {
                UnregisterGlobalHotKey(_hwnd);
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            // 清理合成视觉树
            _rootContainer?.Children.RemoveAll();

            foreach (var item in _activeItems) item.Dispose();
            _activeItems.Clear();

            while (_pool.Count > 0) _pool.Dequeue().Dispose();

            // 清空异步队列中残留的项目
            while (_spriteReadyQueue.TryDequeue(out var leftover)) leftover.Dispose();

            // 按逆序释放 Composition 资源
            _compositionTarget?.Dispose();
            _graphicsDevice?.Dispose();
            _canvasDevice?.Dispose();
            _compositor?.Dispose();
        }
    }
}
