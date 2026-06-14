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

namespace NotiFlow.Rendering
{
    public class BarrageOverlayWindow : IDisposable
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private NativeMethods.WndProc _wndProcDelegate;
        private bool _disposed;

        // ===== 分层窗口渲染核心 =====
        private byte[]? _frameBuffer;
        private IntPtr _memDC;
        private IntPtr _hBitmap;
        private IntPtr _bitmapBits;

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

            // 使用分层窗口实现透明覆盖
            int exStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | 0x00000008 /* WS_EX_TOPMOST */;
            int style = NativeMethods.WS_POPUP;

            _hwnd = NativeMethods.CreateWindowEx(
                exStyle,
                className,
                "NotiFlow Barrage Overlay",
                style,
                _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            InitializeRendering();

            ApplyCaptureSetting();
            RegisterGlobalHotKey(_hwnd);
        }

        private void InitializeRendering()
        {
            // 1. 预分配帧缓冲区（纯 CPU 内存，不涉及 GPU）
            _frameBuffer = new byte[_width * _height * 4];

            // 2. 创建 GDI DIB 位图和内存 DC
            var bmi = new NativeMethods.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = _width;
            bmi.bmiHeader.biHeight = -_height; // 负数表示自顶向下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            _memDC = NativeMethods.CreateCompatibleDC(screenDC);
            _hBitmap = NativeMethods.CreateDIBSection(screenDC, ref bmi, 0, out _bitmapBits, IntPtr.Zero, 0);
            NativeMethods.SelectObject(_memDC, _hBitmap);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
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
            item.PreRenderSprite(_sharedDevice);

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

                if (!_clearedFrameSubmitted && IsVisible)
                {
                    // 提交一帧全透明画面清空屏幕
                    SubmitFrame(clearOnly: true);
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

            if (IsVisible)
            {
                SubmitFrame(clearOnly: false);
            }
        }

        /// <summary>
        /// 将当前帧通过 UpdateLayeredWindow 提交到屏幕上。
        /// 纯 CPU 操作：清空帧缓冲 → 拷贝精灵图 → 更新分层窗口。不涉及任何 GPU 操作。
        /// </summary>
        private void SubmitFrame(bool clearOnly)
        {
            if (_frameBuffer == null || _bitmapBits == IntPtr.Zero) return;

            // 清空帧缓冲
            Array.Clear(_frameBuffer, 0, _frameBuffer.Length);

            if (!clearOnly)
            {
                // 将每个弹幕的预渲染精灵图拷贝到帧缓冲中
                foreach (var item in _activeItems)
                {
                    BlitSprite(item);
                }
            }

            // 拷贝帧缓冲到 DIB 内存
            Marshal.Copy(_frameBuffer, 0, _bitmapBits, _frameBuffer.Length);

            // 调用 UpdateLayeredWindow 将画面贴到透明窗口上
            var ptSrc = new NativeMethods.POINT { x = 0, y = 0 };
            var ptDst = new NativeMethods.POINT { x = _left, y = _top };
            var size = new NativeMethods.SIZE { cx = _width, cy = _height };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = 0,   // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1 // AC_SRC_ALPHA
            };

            NativeMethods.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref size,
                _memDC, ref ptSrc, 0, ref blend, 0x00000002 /* ULW_ALPHA */);
        }

        /// <summary>
        /// 将单个弹幕的精灵图像素拷贝到帧缓冲的正确位置，带边界裁剪。
        /// </summary>
        private void BlitSprite(BarrageItem item)
        {
            if (item.SpritePixels == null || item.SpriteWidth <= 0 || item.SpriteHeight <= 0) return;

            const int margin = 2; // 与 BarrageItem.SpriteMargin 保持一致
            int startX = (int)item.CurrentX - margin;
            int startY = (int)item.CurrentY - margin;

            for (int row = 0; row < item.SpriteHeight; row++)
            {
                int dstY = startY + row;
                if (dstY < 0 || dstY >= _height) continue;

                int srcX = 0;
                int dstX = startX;
                int copyWidth = item.SpriteWidth;

                // 裁剪左边界
                if (dstX < 0)
                {
                    srcX = -dstX;
                    copyWidth += dstX;
                    dstX = 0;
                }

                // 裁剪右边界
                if (dstX + copyWidth > _width)
                {
                    copyWidth = _width - dstX;
                }

                if (copyWidth <= 0) continue;

                int srcOffset = (row * item.SpriteWidth + srcX) * 4;
                int dstOffset = (dstY * _width + dstX) * 4;

                Buffer.BlockCopy(item.SpritePixels, srcOffset, _frameBuffer!, dstOffset, copyWidth * 4);
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

            _frameBuffer = null;

            if (_hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_hBitmap);
                _hBitmap = IntPtr.Zero;
            }

            if (_memDC != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(_memDC);
                _memDC = IntPtr.Zero;
            }

            _bitmapBits = IntPtr.Zero;

            foreach (var item in _activeItems) item.Dispose();
            _activeItems.Clear();

            while (_pool.Count > 0) _pool.Dequeue().Dispose();
            while (_readyQueue.Count > 0) _readyQueue.Dequeue().Dispose();
        }
    }
}
