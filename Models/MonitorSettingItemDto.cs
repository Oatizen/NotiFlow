using CommunityToolkit.Mvvm.ComponentModel;

namespace NotiFlow.Models
{
    /// <summary>
    /// 多显示器配置项数据传输对象，支持 UI 列表展示、拖拽重排与状态持久化。
    /// </summary>
    public partial class MonitorSettingItemDto : ObservableObject
    {
        /// <summary>
        /// 系统底层设备标识符（例如 "\\.\DISPLAY1"），用于在屏幕插拔或系统分辨率变更时进行物理匹配。
        /// </summary>
        public string DeviceName { get; set; } = "";

        /// <summary>
        /// 显示器的友好显示名称（例如 "显示器 1 (主显示器)"）。
        /// </summary>
        [ObservableProperty]
        private string _displayName = "";

        /// <summary>
        /// 分辨率描述文本（例如 "1920 × 1080"）。
        /// </summary>
        [ObservableProperty]
        private string _resolutionText = "";

        /// <summary>
        /// 在虚拟桌面中的绝对 X 坐标。
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// 在虚拟桌面中的绝对 Y 坐标。
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 屏幕宽度（像素）。
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 屏幕高度（像素）。
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 是否为主显示屏。
        /// </summary>
        [ObservableProperty]
        private bool _isPrimary;

        /// <summary>
        /// 是否启用此显示器播放弹幕。
        /// </summary>
        [ObservableProperty]
        private bool _isEnabled = true;

        /// <summary>
        /// 用户在列表中的排序序号（1-indexed，用于 UI 编号展示）。
        /// </summary>
        [ObservableProperty]
        private int _displayOrder = 1;
    }
}
