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

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

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
