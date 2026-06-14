using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NotiFlow
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int LWA_COLORKEY = 0x00000001;
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        // ===== 前台窗口检测 API（供生效场景过滤使用） =====

        /// <summary>
        /// 获取当前处于前台的窗口句柄。
        /// 配合 GetWindowThreadProcessId 可确定当前用户正在操作的应用进程。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 根据窗口句柄获取其所属的进程 ID。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // ===== 窗口枚举 API（供 ProcessEnumerator 使用） =====

        /// <summary>
        /// 枚举所有顶级窗口。每枚举到一个窗口时回调一次 lpEnumFunc。
        /// 回调返回 true 继续枚举，返回 false 停止。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// 判断窗口是否可见。用于过滤掉隐藏/最小化到托盘的后台窗口。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// 获取窗口标题文本的长度（字符数）。返回 0 表示窗口无标题。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        /// <summary>
        /// 获取窗口标题文本。配合 GetWindowTextLength 预先分配缓冲区。
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        // ===== 进程路径查询 API（弥补 Process.MainModule 权限不足的问题） =====

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// 通过 PID 获取进程的可执行文件完整路径。
        /// 使用 QueryFullProcessImageName + PROCESS_QUERY_LIMITED_INFORMATION，
        /// 相比 Process.MainModule.FileName 具有更高的兼容性：
        /// 对 UWP 应用、提权进程、64 位进程均有效。
        /// </summary>
        public static string GetProcessImagePath(int pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return "";

            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    return sb.ToString();
                return "";
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
        /// <summary>
        /// 设置分层窗口属性（Color Key 色键透明）。
        /// 使用该 API 代替 WPF 的 AllowsTransparency，可以将 DWM 的全屏逐像素 Alpha 合成
        /// 降级为硬件加速的单色键穿透，GPU 开销从 30-40% 直降至接近 0%。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        // ===== DWM (Desktop Window Manager) API =====
        // WPF-UI 的 ApplicationThemeManager 在切换主题时会调用 DwmSetWindowAttribute
        // 对所有窗口设置 DWMWA_USE_IMMERSIVE_DARK_MODE 等属性，
        // 这会在操作系统层面将全透明弹幕窗口渲染为黑色不透明。

        /// <summary>
        /// DWM 边距结构体，用于 DwmExtendFrameIntoClientArea。
        /// 设置为全 -1 时，表示将玻璃效果扩展至整个客户区域，实现全窗口透明。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 将 DWM 合成帧（玻璃效果）扩展到窗口客户区域。
        /// 当所有边距设为 -1 时，整个窗口变为 DWM 合成透明层。
        /// 这是替代 WPF AllowsTransparency 的核心方案：
        /// AllowsTransparency 会创建 WS_EX_LAYERED 分层窗口，导致 SetWindowDisplayAffinity 失效；
        /// 而 DWM 合成透明不创建分层窗口，因此 SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) 可正常工作。
        /// </summary>
        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_MICA_EFFECT = 1029;

        /// <summary>
        /// 将指定窗口的 DWM 深色模式、系统背景类型等属性全部重置，
        /// 并在 WPF 层强制恢复全透明背景。
        /// 用于防止 WPF-UI 主题管理器在 OS 层面污染弹幕叠加窗口。
        /// </summary>
        public static void ResetWindowTransparency(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // 关闭 DWM 深色模式渲染
            int darkMode = 0; // 0 = 禁用
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 将系统背景类型设为 None（0），阻止 Mica/Acrylic 等系统渲染
            int backdropType = 0; // DWMSBT_NONE
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // 在 WPF 层兜底强制重设背景透明
            window.Background = Brushes.Transparent;
        }

        /// <summary>
        /// 销毁图标句柄并释放其关联的内存。
        /// 必须在 Imaging.CreateBitmapSourceFromHIcon 创建 WPF 的 Bitmap 副本后，
        /// 物理销毁传入的 Native HICON 句柄，以彻底防范 GDI 句柄内存泄漏。
        /// </summary>
        /// <param name="hIcon">要销毁的图标句柄</param>
        /// <returns>若成功销毁则返回 true，否则返回 false</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ===== Win2D & UpdateLayeredWindow API =====

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int ULW_ALPHA = 2;
        public const int DIB_RGB_COLORS = 0;
        public const int AC_SRC_OVER = 0x00;
        public const int AC_SRC_ALPHA = 0x01;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        // ===== Windows.UI.Composition (DirectComposition) API =====

        public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        [DllImport("CoreMessaging.dll")]
        public static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

        [StructLayout(LayoutKind.Sequential)]
        public struct DispatcherQueueOptions
        {
            public int dwSize;
            public int threadType;
            public int apartmentType;
        }

        [ComImport]
        [Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDCompositionDevice
        {
            void Commit();
            void WaitForCommitCompletion();
            void GetTargetStatistics(IntPtr statistics);
            void CreateTargetForHwnd(IntPtr hwnd, bool topmost, out IDCompositionTarget target);
            void CreateVisual(out IDCompositionVisual visual);
        }

        [ComImport]
        [Guid("eacdd04c-117e-4e17-88f4-d1b12b0e3d89")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDCompositionTarget
        {
            void SetRoot(IDCompositionVisual visual);
        }

        [ComImport]
        [Guid("4d93059d-097b-4651-9a60-f0f25116e2f3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDCompositionVisual
        {
            void SetOffsetX(float offsetX);
            void SetOffsetX_Anim(IntPtr animation);
            void SetOffsetY(float offsetY);
            void SetOffsetY_Anim(IntPtr animation);
            void SetTransform(IntPtr transform);
            void SetTransform_Matrix(IntPtr matrix);
            void SetTransformParent(IDCompositionVisual visual);
            void SetClip(IntPtr clip);
            void SetClip_Rect(IntPtr rect);
            void SetContent(IntPtr content);
        }

        [ComImport]
        [Guid("5F10688D-EA55-4D55-A3B0-4D29881A5322")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICanvasResourceWrapperNative
        {
            void GetNativeResource(IntPtr device, float dpi, ref Guid iid, out IntPtr resource);
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDirect3DDxgiInterfaceAccess
        {
            void GetInterface([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr p);
        }

        [DllImport("dcomp.dll", PreserveSig = false)]
        public static extern void DCompositionCreateDevice(IntPtr dxgiDevice, [In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IDCompositionDevice dcompDevice);
    }
}
