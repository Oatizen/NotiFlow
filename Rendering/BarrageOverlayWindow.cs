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
// 鍒悕閬垮厤锟?System.Windows.Media.CompositionTarget 鍐茬獊
using WinCompTarget = System.Windows.Media.CompositionTarget;

namespace NotiFlow.Rendering
{
    public class BarrageOverlayWindow : IDisposable
    {
        private IntPtr _hwnd = IntPtr.Zero;
        private NativeMethods.WndProc _wndProcDelegate;
        private bool _disposed;

        // ===== Windows.UI.Composition 娓叉煋鏍稿績 =====
        private Compositor _compositor;
        private DesktopWindowTarget _compositionTarget;
        private Windows.UI.Composition.ContainerVisual _rootContainer;
        private CompositionGraphicsDevice _graphicsDevice;
        private CanvasDevice _canvasDevice;

        /// <summary>
        /// 淇濇寔 DispatcherQueueController COM 寮曠敤瀛樻椿锛岄槻锟?GC 鍥炴敹锟?
        /// Compositor 渚濊禆褰撳墠绾跨▼锟?DispatcherQueue锛岃嫢鎺у埗鍣ㄨ鍥炴敹鍒欏悎鎴愬櫒灏嗗け鏁堬拷?
        /// </summary>
        private IntPtr _dispatcherQueueController;

        private int _left;
        private int _top;
        private int _width;
        private int _height;

        private readonly Random _random = new();

        // ===== 杞ㄩ亾绠＄悊绯荤粺 =====
        // private double TrackHeight => BarrageSettings.FontSize + 16;
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

            // 浣跨敤 WS_EX_NOREDIRECTIONBITMAP 鏇夸唬 WS_EX_LAYERED锟?
            // 锟?DWM 涓嶄负姝ょ獥鍙ｅ垎閰嶉噸瀹氬悜浣嶅浘锛岃€屾槸锟?Composition 寮曟搸鐩存帴娓叉煋
            int exStyle = NativeMethods.WS_EX_NOREDIRECTIONBITMAP
                        | NativeMethods.WS_EX_TRANSPARENT
                        | NativeMethods.WS_EX_TOOLWINDOW
                        | NativeMethods.WS_EX_NOACTIVATE
                        | 0x00000008 /* WS_EX_TOPMOST */;
            int style = NativeMethods.WS_POPUP;

            // 鍒涘缓绐楀彛锛屾晠鎰忎笉浼犲叆鏍囬 (string.Empty)锟?
            // 鍥犱负璁稿鎴浘宸ュ叿锛堝寰俊銆丵Q銆丼nipping Tool锛夊湪閬嶅巻绐楀彛鏃讹紝
            // 浼氳嚜鍔ㄨ繃婊ゆ帀娌℃湁鏍囬鐨勬棤杈规绐楀彛锛屼粠鑰屽彲鑳界粫杩団€滄寜绐楀彛鎴浘鈥濈殑鎹曟崏锟?
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
        /// 鍒濆锟?Windows.UI.Composition 娓叉煋绠＄嚎锟?
        /// 鍒涘缓椤哄簭锛欴ispatcherQueue 锟?Compositor 锟?DesktopWindowTarget 锟?鏍瑰锟?锟?Win2D 璁惧锟?
        /// DispatcherQueue 蹇呴』锟?Compositor 涔嬪墠鍒涘缓锛屽惁锟?Compositor 鏋勯€犲嚱鏁颁細鎶涘嚭寮傚父锟?
        /// </summary>
        private void InitializeRendering()
        {
            // 1. 鍒涘缓 DispatcherQueueController锛圕ompositor 闇€瑕佸綋鍓嶇嚎绋嬪叿澶囨秷鎭车锟?
            var options = new NativeMethods.DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<NativeMethods.DispatcherQueueOptions>(),
                threadType = 2,    // DQTYPE_CURRENT_THREAD
                apartmentType = 2  // DQTAT_COM_STA
            };
            NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);

            // 2. 鍒涘缓 OS 锟?Compositor 骞堕€氳繃 ICompositorDesktopInterop 缁戝畾锟?HWND
            _compositor = new Compositor();
            var interop = _compositor.As<NativeMethods.ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(_hwnd, false, out var rawTarget);
            _compositionTarget = MarshalInterface<DesktopWindowTarget>.FromAbi(rawTarget);
            Marshal.Release(rawTarget);

            // 3. 鍒涘缓鏍瑰鍣ㄨ瑙夊苟璁句负鍚堟垚鐩爣鐨勬牴
            _rootContainer = _compositor.CreateContainerVisual();
            _rootContainer.Size = new Vector2(_width, _height);
            _compositionTarget.Root = _rootContainer;

            // 4. 杩藉姞 WS_EX_LAYERED 浠ユ仮澶嶉紶鏍囩┛閫忥拷?
            //    WS_EX_TRANSPARENT 鐨勭┛閫忚涓轰緷锟?WS_EX_LAYERED锟?
            //    WS_EX_NOREDIRECTIONBITMAP + WS_EX_LAYERED 鍙互鍏卞瓨锟?
            //    DWM 锟?Composition 寮曟搸鎻愪緵鍐呭锛學S_EX_LAYERED 浠呭奖鍝嶅懡涓祴璇曡涔夛拷?
            IntPtr curStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
                (IntPtr)((long)curStyle | NativeMethods.WS_EX_LAYERED));

            // 4. 鍒涘缓鍏变韩鍚屼竴 D3D11 璁惧锟?CompositionGraphicsDevice 锟?CanvasDevice
            //    閫氳繃 CompositionHelper 浠庨浂鍒涘缓 D3D11 璁惧锛堟浛锟?CanvasComposition锟?
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

        /// <summary>
        /// 锟?UI 绾跨▼涓婃彁鍙栨墍锟?WPF 渚濊禆鐨勭函鍊兼暟鎹紝鐒跺悗灏嗗脊骞曠汗鐞嗘瀯寤哄伐锟?
        /// 閫氳繃 Task.Run 绉讳氦鍒板悗鍙扮嚎绋嬫墽琛岋拷?
        /// 鍚庡彴绾跨▼浣跨敤 CompositionGraphicsDevice 鍒涘缓 SpriteVisual + DrawingSurface锟?
        /// 瀹屾垚鍚庡皢寮瑰箷鎺ㄥ叆灏辩华闃熷垪绛夊緟鍚堟垚锟?
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

            // ===== 锟?UI 绾跨▼涓婃彁鍙栨墍锟?WPF 渚濊禆鐨勭函鍊兼暟锟?=====
            string currentForegroundExe = ((App)Application.Current).ForegroundMonitor?.CurrentForegroundProcess ?? "";
            var config = BarrageSettings.GetResolvedConfig(message.Aumid, currentForegroundExe);

            var textColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(config.TextColorHex);
            var textStrokeColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(config.TextStrokeColorHex);

            string fontFamilyName = config.FontFamilyName;
            double fontSize = config.FontSize;
            var fontStyle = config.FontStyle == "Italic" ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
            var fontWeight = config.FontWeight == "Bold" ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            double topPosition = TopMargin + track * (BarrageSettings.FontSize + 16);
            double speedPixelsPerSec = config.ScrollSpeedCharsPerSec * fontSize;
            if (speedPixelsPerSec < 10) speedPixelsPerSec = 10;

            bool showBackground = config.ShowBackground;
            var bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(config.BackgroundColorHex);
            double bgOpacity = config.BackgroundOpacity;
            double textOpacity = config.TextOpacity;
            var cornerRadius = new System.Windows.CornerRadius(config.BackgroundCornerRadius);

            bool highlightEllipsis = config.HighlightEllipsis;
            var ellColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(config.EllipsisColorHex);

            bool showAppName = config.ShowAppName;
            double maxTextLen = config.MaxTextLength;
            bool isUnderlined = config.IsUnderlined;
            double letterSpacing = config.LetterSpacing;
            
            bool showBgImage = config.ShowBackgroundImage;
            string bgImagePath = config.BackgroundImagePath;
            NotiFlow.Models.ImageAnchor bgAnchor = config.BackgroundImageAnchor;
            double bgOffsetX = config.BackgroundImageOffsetX;
            double bgOffsetY = config.BackgroundImageOffsetY;
            double bgScale = config.BackgroundImageScale;
            bool bgKeepBaseColor = config.BackgroundImageKeepBaseColor;
            
            double textStrokeThickness = config.TextStrokeThickness;
            bool showTextStroke = config.ShowTextStroke;
            double bgImageOpacity = config.BackgroundImageOpacity;

            // 棰勬彁鍙栧浘鏍囧儚绱狅紙WPF 瀵硅薄鍙兘?UI 绾跨▼璁块棶?
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

            // ===== 绾圭悊鏋勫缓绉诲埌鍚庡彴绾跨▼ =====
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
                        textColor, textOpacity, fontSize, letterSpacing, fontFamilyName, fontStyle, fontWeight,
                        showBackground, bgColor, bgOpacity, cornerRadius,
                        showBgImage, bgImagePath, bgAnchor, bgOffsetX, bgOffsetY, bgScale, bgKeepBaseColor, BarrageSettings.BackgroundImageOpacity,
                        BarrageSettings.ShowTextStroke, textStrokeColor, BarrageSettings.TextStrokeThickness,
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
        /// 灏嗗凡瀹屾垚绾圭悊鏋勫缓鐨勫脊骞曟坊鍔犲埌鍚堟垚瑙嗚鏍戯紝骞跺惎鍔ㄦ粴鍔ㄥ姩鐢伙拷?
        /// 鍔ㄧ敾锟?Compositor 锟?GPU 绔┍鍔紝鏃犻渶姣忓抚鎵嬪姩鏇存柊浣嶇疆锟?
        /// </summary>
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

            // 灏嗗脊骞曠殑 SpriteVisual 娣诲姞鍒板悎鎴愭爲
            _rootContainer.Children.InsertAtTop(item.Visual);

            // 鍒涘缓浠庡睆骞曞彸绔埌瀹屽叏绂诲紑宸︾鐨勫寑閫熸粴鍔ㄥ姩锟?
            // 棰濆澧炲姞 50 鍍忕礌鐨勫畨鍏ㄨ竟璺濓紝闃叉鍊炬枩瀛椾綋鎴栭暱闃村奖鐢变簬娴嬮噺璇樊瀵艰嚧杈圭紭绮樿繛
            var linear = _compositor.CreateLinearEasingFunction();
            var animation = _compositor.CreateVector3KeyFrameAnimation();
            float destinationX = -(float)item.PhysicalWidth - 50f;
            animation.InsertKeyFrame(0f, new Vector3((float)item.CurrentX, (float)item.CurrentY, 0f), linear);
            animation.InsertKeyFrame(1f, new Vector3(destinationX, (float)item.CurrentY, 0f), linear);

            double totalDistance = item.CurrentX + item.PhysicalWidth + 50f;
            double durationSec = totalDistance / item.SpeedPixelsPerSec;
            animation.Duration = TimeSpan.FromSeconds(durationSec);

            item.Visual.StartAnimation("Offset", animation);
            item.AnimationStartTime = DateTime.UtcNow;
            item.AnimationEndTime = DateTime.UtcNow.AddSeconds(durationSec);

            _activeItems.Add(item);
        }

        /// <summary>
        /// WPF CompositionTarget.Rendering 鍥炶皟锛岄┍鍔ㄥ脊骞曠敓鍛藉懆鏈熺鐞嗭拷?
        /// 涓嶅啀鎵ц浠讳綍鍍忕礌鎿嶄綔鈥斺€旀粴鍔ㄥ姩鐢荤敱鍚堟垚鍣ㄨ嚜鍔ㄩ┍鍔拷?
        /// 姝ゅ洖璋冧粎璐熻矗锛氭秷鎭叆闃熴€佸脊骞曟彁浜ゃ€佽建閬撻噴鏀惧垽鏂€佽繃鏈熷脊骞曟竻鐞嗭拷?
        /// </summary>
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_disposed) return;

            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;
            _lastRenderTime = renderingArgs.RenderingTime;

            // 姣忓抚澶勭悊鎵€鏈夊緟鍏ラ槦鐨勯€氱煡娑堟伅锛堥伩锟?20 鏉￠€氱煡鍙彇 1 鏉＄殑绉帇闂锟?
            while (_spawnQueue.TryDequeue(out var spawnMsg))
            {
                if (((App)Application.Current).ForegroundMonitor is { } monitor && !monitor.IsSceneSuppressed)
                {
                    EnqueueBarrage(spawnMsg);
                }
            }

            // 鎺ユ敹鍚庡彴绾跨▼瀹屾垚鐨勫脊骞曞竷灞€锛屽苟锟?UI 绾跨▼鍒涘缓 Composition 瑙嗚瀵硅薄
            // 闄愬埗姣忓抚鏈€澶氬垱锟?2-3 涓汗鐞嗭紝闃叉鍗曞抚鍐呭悜 DWM 鎻愪氦杩囧琛ㄩ潰鍒嗛厤璇锋眰瀵艰嚧 D3D/DXGI 寮傚父锛堝紩鍙戝脊骞曚涪澶憋級
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
                } // 濡傛灉鎶涘紓甯革紝Visual 浼氫繚鎸佷负 null锛岀敱 CommitBarrage 澶勭悊鍥炴敹

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

            // 閬嶅巻娲昏穬寮瑰箷锛屽鐞嗚建閬撻噴鏀惧拰鐢熷懡鍛ㄦ湡缁撴潫
            var now = DateTime.UtcNow;
            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                var item = _activeItems[i];
                double elapsed = (now - item.AnimationStartTime).TotalSeconds;
                double totalDuration = (item.AnimationEndTime - item.AnimationStartTime).TotalSeconds;

                // 杞ㄩ亾閲婃斁鍒ゆ柇锛氭牴鎹姩鐢昏繘搴︽帹绠楀綋鍓嶄綅缃紝
                // 褰撳脊骞曞熬閮ㄩ€氳繃灞忓箷鍙充晶 3/4 澶勬椂閲婃斁杞ㄩ亾锛屽厑璁镐笅涓€鏉″脊骞曡繘锟?
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

                // 寮瑰箷鍔ㄧ敾缁撴潫锛屼粠鍚堟垚鏍戠Щ闄ゅ苟鍥炴敹
                // 澧炲姞 1.5 绉掔殑缂撳啿鏃堕棿锛屽讥琛ュ悗鍙扮嚎绋嬬敓鎴愪笌 GPU 娓叉煋鐨勫紓姝ュ欢杩燂紝纭繚寮瑰箷褰诲簳椋炲嚭灞忓箷鍚庡啀绉婚櫎
                if (now >= item.AnimationEndTime.AddSeconds(1.5))
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

            // 娓呯悊鍚堟垚瑙嗚锟?
            _rootContainer?.Children.RemoveAll();

            foreach (var item in _activeItems) item.Dispose();
            _activeItems.Clear();

            while (_pool.Count > 0) _pool.Dequeue().Dispose();

            // 娓呯┖寮傛闃熷垪涓畫鐣欑殑椤圭洰
            while (_spriteReadyQueue.TryDequeue(out var leftover)) leftover.Dispose();

            // 鎸夐€嗗簭閲婃斁 Composition 璧勬簮
            _compositionTarget?.Dispose();
            _graphicsDevice?.Dispose();
            _canvasDevice?.Dispose();
            _compositor?.Dispose();
        }
    }
}
