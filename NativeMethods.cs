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
    }
}
