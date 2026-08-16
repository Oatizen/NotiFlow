using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NotiFlow.Models;
using Microsoft.Graphics.Canvas;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace NotiFlow.Rendering
{
    /// <summary>
    /// 单个显示器专属的底层透明穿透弹幕渲染窗口。
    /// 严格贴合目标显示器的物理坐标与分辨率，杜绝跨屏坐标错位与渲染溢出。
    /// </summary>
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
        private IntPtr _dispatcherQueueController;

        public MonitorSettingItemDto Monitor { get; private set; }

        private int _left;
        private int _top;
        private int _width;
        private int _height;

        private readonly Random _random = new();

        // ===== 轨道管理 =====
        private const int TopMargin = 20;
        private bool[] _trackOccupied = Array.Empty<bool>();
        private int _trackCount;

        private readonly Queue<(NotificationMessage Message, int SequenceIndex, List<MonitorSettingItemDto>? Sequence)> _pendingMessages = new();
        private readonly ConcurrentQueue<(NotificationMessage Message, int SequenceIndex, List<MonitorSettingItemDto>? Sequence)> _spawnQueue = new();
        private readonly ConcurrentQueue<BarrageItem> _spriteReadyQueue = new();
        private readonly List<BarrageItem> _activeItems = new();
        private readonly Queue<BarrageItem> _pool = new();
        
        private TimeSpan _lastRenderTime = TimeSpan.Zero;

        public bool IsLoaded => _hwnd != IntPtr.Zero;
        public bool IsVisible { get; private set; }

        /// <summary>
        /// 当顺序流转模式下的弹幕完全滑出当前屏幕左边缘时触发的事件。
        /// </summary>
        public event Action<NotificationMessage, int, List<MonitorSettingItemDto>>? OnBarrageSequenceExit;

        public BarrageOverlayWindow(MonitorSettingItemDto monitor)
        {
            Monitor = monitor;
            _wndProcDelegate = WndProc;
            
            CreateWindow();
            InitializeTracks();
            
            System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void CreateWindow()
        {
            _left = Monitor.X;
            _top = Monitor.Y;
            _width = Math.Max(100, Monitor.Width);
            _height = Math.Max(100, Monitor.Height);

            string className = $"NotiFlowBarrageOverlayClass_{Math.Abs(Monitor.DeviceName.GetHashCode())}";

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

            int exStyle = NativeMethods.WS_EX_NOREDIRECTIONBITMAP
                        | NativeMethods.WS_EX_LAYERED
                        | NativeMethods.WS_EX_TRANSPARENT
                        | NativeMethods.WS_EX_TOOLWINDOW
                        | NativeMethods.WS_EX_NOACTIVATE
                        | 0x00000008 /* WS_EX_TOPMOST */;
            int style = NativeMethods.WS_POPUP;

            _hwnd = NativeMethods.CreateWindowEx(
                exStyle,
                className,
                string.Empty,
                style,
                _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            // 显式强化设置扩展样式，确保穿透与置顶无焦点属性绝对生效
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

            InitializeRendering();

            ApplyCaptureSetting();

            if (Monitor.IsPrimary)
            {
                RegisterGlobalHotKey(_hwnd);
            }
        }

        public void UpdateBounds(MonitorSettingItemDto monitor)
        {
            Monitor = monitor;
            if (_left != monitor.X || _top != monitor.Y || _width != monitor.Width || _height != monitor.Height)
            {
                _left = monitor.X;
                _top = monitor.Y;
                _width = Math.Max(100, monitor.Width);
                _height = Math.Max(100, monitor.Height);

                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowBounds(_hwnd, _left, _top, _width, _height);
                }

                InitializeTracks();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static void SetWindowBounds(IntPtr hwnd, int x, int y, int width, int height)
        {
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_NOZORDER = 0x0004;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOACTIVATE | SWP_NOZORDER);
        }

        private void InitializeRendering()
        {
            var options = new NativeMethods.DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf(typeof(NativeMethods.DispatcherQueueOptions)),
                threadType = 2, // DQTYPE_THREAD_CURRENT
                apartmentType = 0 // DQTAT_COM_NONE
            };
            NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);

            _compositor = new Compositor();

            var interop = _compositor.As<NativeMethods.ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(_hwnd, true, out var targetPtr);
            _compositionTarget = WinRT.MarshalInspectable<DesktopWindowTarget>.FromAbi(targetPtr);

            _rootContainer = _compositor.CreateContainerVisual();
            _rootContainer.RelativeSizeAdjustment = Vector2.One;
            _compositionTarget.Root = _rootContainer;

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
            double usableHeight = _height - TopMargin - (BarrageSettings.FontSize + 16);
            _trackCount = Math.Max(1, (int)(usableHeight / (BarrageSettings.FontSize + 16)));
            _trackOccupied = new bool[_trackCount];
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
            if (trackIndex >= 0 && trackIndex < _trackCount)
            {
                _trackOccupied[trackIndex] = false;
            }
            TryFlushQueue();
        }

        private void TryFlushQueue()
        {
            int count = _pendingMessages.Count;
            while (count-- > 0 && _pendingMessages.Count > 0)
            {
                var item = _pendingMessages.Dequeue();
                int track = AllocateTrack();
                if (track >= 0)
                {
                    PrepareBarrage(item.Message, track, item.SequenceIndex, item.Sequence);
                }
                else
                {
                    _pendingMessages.Enqueue(item);
                }
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
            if (_hwnd != IntPtr.Zero && Monitor.IsPrimary)
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

        public void EnqueueBarrage(NotificationMessage msg, int sequenceIndex, List<MonitorSettingItemDto>? sequence)
        {
            _spawnQueue.Enqueue((msg, sequenceIndex, sequence));
        }

        private void PrepareBarrage(NotificationMessage message, int track, int sequenceIndex, List<MonitorSettingItemDto>? sequence)
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
            item.TargetMonitorDeviceName = Monitor.DeviceName;
            item.MonitorSequenceIndex = sequenceIndex;
            item.TargetMonitorsSequence = sequence;
            item.SourceNotification = message;

            string currentForegroundExe = ((App)Application.Current).ForegroundMonitor?.CurrentForegroundProcess ?? "";
            var config = BarrageSettings.GetResolvedConfig(message.Aumid, currentForegroundExe);

            double fontSize = config.FontSize > 0 ? config.FontSize : 36;
            double topPosition = TopMargin + track * (BarrageSettings.FontSize + 16);
            double speedPixelsPerSec = config.ScrollSpeedCharsPerSec * fontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10;

            bool showAppIcon = config.ShowAppIcon;
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

            var canvasDevice = _canvasDevice;
            int screenWidth = _width;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    item.PrepareLayout(canvasDevice,
                        appName, title, body,
                        config,
                        iconPixels, iconWidth, iconHeight, isUwpIcon);
                    item.CurrentX = screenWidth;
                    item.CurrentY = topPosition;
                    item.SpeedPixelsPerSec = speedPixelsPerSec;
                    item.StartX = screenWidth;
                }
                catch { }
                _spriteReadyQueue.Enqueue(item);
            });
        }

        private void CommitBarrage(BarrageItem item)
        {
            if (item.Visual == null)
            {
                item.TrackReleased = true;
                ReleaseTrack(item.TrackIndex);
                item.IsAlive = false;
                _pool.Enqueue(item);
                return;
            }

            // 确保窗口可见
            if (!IsVisible) Show();

            _rootContainer.Children.InsertAtTop(item.Visual);

            float destinationX = -(float)item.PhysicalWidth - 50f;

            var linear = _compositor.CreateLinearEasingFunction();
            var animation = _compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0f, new Vector3((float)item.CurrentX, (float)item.CurrentY, 0f), linear);
            animation.InsertKeyFrame(1f, new Vector3(destinationX, (float)item.CurrentY, 0f), linear);

            double totalDistance = item.CurrentX - destinationX;
            double durationSec = totalDistance / item.SpeedPixelsPerSec;
            animation.Duration = TimeSpan.FromSeconds(durationSec);

            item.Visual.StartAnimation("Offset", animation);
            item.AnimationStartTime = DateTime.UtcNow;
            item.AnimationEndTime = DateTime.UtcNow.AddSeconds(durationSec);

            _activeItems.Add(item);
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_disposed) return;

            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;
            _lastRenderTime = renderingArgs.RenderingTime;

            while (_spawnQueue.TryDequeue(out var spawnItem))
            {
                if (((App)Application.Current).ForegroundMonitor is { } monitor && !monitor.IsSceneSuppressed)
                {
                    int track = AllocateTrack();
                    if (track >= 0)
                    {
                        PrepareBarrage(spawnItem.Message, track, spawnItem.SequenceIndex, spawnItem.Sequence);
                    }
                    else
                    {
                        _pendingMessages.Enqueue(spawnItem);
                    }
                }
            }

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
                }

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

            var now = DateTime.UtcNow;
            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                var item = _activeItems[i];
                double elapsed = (now - item.AnimationStartTime).TotalSeconds;
                double totalDuration = (item.AnimationEndTime - item.AnimationStartTime).TotalSeconds;
                float destinationX = -(float)item.PhysicalWidth - 50f;

                // 轨道释放判断：当弹幕尾部通过该屏幕右侧 3/4 处时释放轨道
                if (!item.TrackReleased && totalDuration > 0)
                {
                    double progress = elapsed / totalDuration;
                    double currentX = item.StartX + (destinationX - item.StartX) * progress;
                    if (currentX + item.PhysicalWidth < _width - _width / 4.0)
                    {
                        item.TrackReleased = true;
                        ReleaseTrack(item.TrackIndex);
                    }
                }

                // 动画结束判定
                if (now >= item.AnimationEndTime)
                {
                    if (item.Visual != null)
                    {
                        _rootContainer.Children.Remove(item.Visual);
                    }

                    // 顺序流转：触发回调给下一个显示器
                    if (item.TargetMonitorsSequence != null &&
                        item.MonitorSequenceIndex >= 0 &&
                        item.MonitorSequenceIndex + 1 < item.TargetMonitorsSequence.Count &&
                        item.SourceNotification != null)
                    {
                        OnBarrageSequenceExit?.Invoke(item.SourceNotification, item.MonitorSequenceIndex + 1, item.TargetMonitorsSequence);
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
                if (Monitor.IsPrimary)
                {
                    UnregisterGlobalHotKey(_hwnd);
                }
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            _rootContainer?.Children.RemoveAll();

            foreach (var item in _activeItems) item.Dispose();
            _activeItems.Clear();

            while (_pool.Count > 0) _pool.Dequeue().Dispose();
            while (_spriteReadyQueue.TryDequeue(out var leftover)) leftover.Dispose();

            _compositionTarget?.Dispose();
            _graphicsDevice?.Dispose();
            _canvasDevice?.Dispose();
            _compositor?.Dispose();
        }
    }
}
