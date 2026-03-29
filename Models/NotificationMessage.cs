using System.Windows.Media;

namespace NotiFlow.Models
{
    public class NotificationMessage
    {
        // 如果成功由 WinRT 抽取流并转换为内存位图，就会塞进这里
        public ImageSource? AppIcon { get; set; }
        
        public string AppName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        
        // 用于后续应对传统 Win32 程序图标降级抽取的标识
        public string Aumid { get; set; } = string.Empty;
    }
}
