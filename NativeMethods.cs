using System;
using System.Runtime.InteropServices;

namespace NotiFlow
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int LWA_COLORKEY = 0x00000001;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        /// <summary>
        /// 设置分层窗口属性（Color Key 色键透明）。
        /// 使用该 API 代替 WPF 的 AllowsTransparency，可以将 DWM 的全屏逐像素 Alpha 合成
        /// 降级为硬件加速的单色键穿透，GPU 开销从 30-40% 直降至接近 0%。
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }
}
