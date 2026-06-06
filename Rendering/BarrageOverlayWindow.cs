using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NotiFlow.Models;
using NotiFlow.Services;
using Microsoft.Graphics.Canvas;
using Windows.System;
using Windows.UI.Composition;
using Microsoft.Graphics.Canvas.UI.Composition;
using WinRT;

namespace NotiFlow.Rendering
{
    public class BarrageOverlayWindow : IDisposable
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private NativeMethods.WndProc _wndProcDelegate;
        private bool _disposed;

        // ===== Composition 核心 =====
        private IntPtr _dispatcherQueueController;
        private Compositor? _compositor;
        private Windows.UI.Composition.Desktop.DesktopWindowTarget? _target;
        private CompositionGraphicsDevice? _compositionGraphicsDevice;
        private CompositionDrawingSurface? _drawingSurface;

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
        private readonly Queue<BarrageItem> _readyQueue = new();
        private readonly List<BarrageItem> _activeItems = new();
        private readonly Queue<BarrageItem> _pool = new();
        
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private CanvasDevice _sharedDevice;
        private bool _clearedFrameSubmitted = false;

        public bool IsLoaded => _hwnd != IntPtr.Zero;
        public bool IsVisible { get; private set; }

        public BarrageOverlayWindow()
        {
            _wndProcDelegate = WndProc;
            _sharedDevice = CanvasDevice.GetSharedDevice();
            
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

            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX)),
                style = 0,
                lpfnWndProc = _wndProcDelegate,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = NativeMethods.GetDC(IntPtr.Zero),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
                hIconSm = IntPtr.Zero
            };

            NativeMethods.RegisterClassEx(ref wndClass);

            // 加入 WS_EX_NOREDIRECTIONBITMAP 以配合 DirectComposition
            int exStyle = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | 0x00000008 /* WS_EX_TOPMOST */ | NativeMethods.WS_EX_NOREDIRECTIONBITMAP;
            int style = NativeMethods.WS_POPUP;

            _hwnd = NativeMethods.CreateWindowEx(
                exStyle,
                className,
                "NotiFlow Barrage Overlay",
                style,
                _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            InitializeComposition();

            ApplyCaptureSetting();
            RegisterGlobalHotKey(_hwnd);
        }

        private void InitializeComposition()
        {
            // 1. 初始化当前线程的 DispatcherQueue (Compositor 所需)
            var options = new NativeMethods.DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf(typeof(NativeMethods.DispatcherQueueOptions)),
                threadType = 2, // DQTYPE_THREAD_CURRENT
                apartmentType = 2 // DQA_NONE
            };
            NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);

            // 2. 创建 Compositor
            _compositor = new Compositor();

            // 3. 将 Compositor 桥接到我们的 Win32 窗口
            var interop = _compositor.As<NativeMethods.ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(_hwnd, true, out IntPtr targetPtr);
            _target = WinRT.MarshalInterface<Windows.UI.Composition.Desktop.DesktopWindowTarget>.FromAbi(targetPtr);

            // 4. 创建 Composition 图形设备与 DrawingSurface
            _compositionGraphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _sharedDevice);
            
            _drawingSurface = _compositionGraphicsDevice.CreateDrawingSurface(
                new Windows.Foundation.Size(_width, _height),
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                Windows.Graphics.DirectX.DirectXAlphaMode.Premultiplied);

            // 5. 将 DrawingSurface 挂载到视觉树根节点
            var visual = _compositor.CreateSpriteVisual();
            visual.Size = new System.Numerics.Vector2(_width, _height);
            visual.Brush = _compositor.CreateSurfaceBrush(_drawingSurface);
            
            _target.Root = visual;
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
            if (track >= 0)
            {
                PrepareBarrage(msg, track);
            }
            else
            {
                _pendingMessages.Enqueue(msg);
            }
        }

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
            
            SolidColorBrush unFrozenBrush = (SolidColorBrush)BarrageSettings.TextColor.Clone();
            unFrozenBrush.Freeze();

            System.Windows.Media.FontFamily fontFamily = BarrageSettings.FontFamily;
            double topPosition = TopMargin + track * TrackHeight;
            
            item.BuildVisual(_sharedDevice, message, unFrozenBrush, BarrageSettings.FontSize, fontFamily, BarrageSettings.FontStyle, BarrageSettings.FontWeight);

            item.CurrentX = _width;
            item.CurrentY = topPosition;
            
            double speedPixelsPerSec = BarrageSettings.ScrollSpeedCharsPerSec * BarrageSettings.FontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10;
            item.SpeedPixelsPerSec = speedPixelsPerSec;

            _readyQueue.Enqueue(item);
        }

        private void CommitBarrage(BarrageItem item)
        {
            _activeItems.Add(item);
            _clearedFrameSubmitted = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_disposed) return;

            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;

            double dt = (_lastRenderTime == TimeSpan.Zero) ? 0 : (renderingArgs.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = renderingArgs.RenderingTime;

            if (dt == 0) return;

            if (_spawnQueue.TryDequeue(out var spawnMsg))
            {
                if (((App)Application.Current).ForegroundMonitor is { } monitor && !monitor.IsSceneSuppressed)
                {
                    EnqueueBarrage(spawnMsg);
                }
            }

            if (_readyQueue.Count > 0)
            {
                CommitBarrage(_readyQueue.Dequeue());
            }

            if (_activeItems.Count == 0 && _spawnQueue.IsEmpty && _readyQueue.Count == 0 && _pendingMessages.Count == 0)
            {
                if (!BarrageSettings.IsWorking && IsVisible)
                {
                    Hide();
                }

                if (!_clearedFrameSubmitted && _drawingSurface != null && IsVisible)
                {
                    using (var session = CanvasComposition.CreateDrawingSession(_drawingSurface)) 
                    {
                        session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    }
                    _clearedFrameSubmitted = true;
                }
                return;
            }

            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                var item = _activeItems[i];
                
                item.CurrentX -= item.SpeedPixelsPerSec * dt;
                
                if (!item.TrackReleased && (item.CurrentX + item.PhysicalWidth < _width - _width / 4.0))
                {
                    item.TrackReleased = true;
                    ReleaseTrack(item.TrackIndex);
                }

                if (item.CurrentX < -item.PhysicalWidth)
                {
                    item.IsAlive = false;
                    _activeItems.RemoveAt(i);
                    _pool.Enqueue(item);
                }
            }

            if (_drawingSurface != null && IsVisible)
            {
                using (var session = CanvasComposition.CreateDrawingSession(_drawingSurface))
                {
                    session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    foreach (var item in _activeItems)
                    {
                        item.Draw(session);
                    }
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

            _drawingSurface?.Dispose();
            _drawingSurface = null;

            _compositionGraphicsDevice?.Dispose();
            _compositionGraphicsDevice = null;

            _target?.Dispose();
            _target = null;

            _compositor?.Dispose();
            _compositor = null;

            foreach (var item in _activeItems) item.Dispose();
            _activeItems.Clear();

            while (_pool.Count > 0) _pool.Dequeue().Dispose();
            while (_readyQueue.Count > 0) _readyQueue.Dequeue().Dispose();
        }
    }
}
