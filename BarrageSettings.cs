using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace NotiFlow
{
    /// <summary>
    /// 用于序列化的纯数据传输对象 (DTO)
    /// WPF 的 Brush 和 FontFamily 这些原生对象不能直接序列化，所以借助这个中间层。
    /// </summary>
    public class BarrageConfigDto
    {
        public string FontFamilyName { get; set; } = "Microsoft YaHei";
        public double FontSize { get; set; } = 36;
        public string FontWeight { get; set; } = "Bold";
        public string FontStyle { get; set; } = "Normal";
        public bool IsUnderlined { get; set; } = false;
        
        public string TextColorHex { get; set; } = "#FFFFFF"; // 默认白色
        public double TextOpacity { get; set; } = 1.0;
        
        public bool ShowAppIcon { get; set; } = true;
        public bool ShowAppName { get; set; } = true;

        public bool ShowBackground { get; set; } = true;
        public string BackgroundColorHex { get; set; } = "#000000"; // 默认黑色
        public double BackgroundOpacity { get; set; } = 0.4;
        public double BackgroundCornerRadius { get; set; } = 8;
        
        public int MaxTextLength { get; set; } = 50;
        public bool HighlightEllipsis { get; set; } = true;
        public string EllipsisColorHex { get; set; } = "#32CD32"; // 亮绿色 (LimeGreen)
        public double ScrollSpeedCharsPerSec { get; set; } = 12.0;
        public bool AutoStartWorking { get; set; } = true;
    }

    /// <summary>
    /// 全局弹幕外观与行为配置管理器
    /// 支持配置的导入、导出以及应对设备变更时的容灾回落（如：字体缺失）
    /// </summary>
    public static class BarrageSettings
    {
        // 实际供 WPF 绑定的内存实例对象
        public static FontFamily FontFamily { get; set; } = new FontFamily("Microsoft YaHei");
        public static double FontSize { get; set; } = 36;
        public static FontWeight FontWeight { get; set; } = FontWeights.Bold;
        public static FontStyle FontStyle { get; set; } = FontStyles.Normal;
        public static bool IsUnderlined { get; set; } = false;
        
        public static Brush TextColor { get; set; } = Brushes.White;
        public static double TextOpacity { get; set; } = 1.0;
        
        public static bool ShowAppIcon { get; set; } = true;
        public static bool ShowAppName { get; set; } = true;

        public static bool ShowBackground { get; set; } = true;
        public static Brush BackgroundColor { get; set; } = Brushes.Black;
        public static double BackgroundOpacity { get; set; } = 0.4;
        public static CornerRadius BackgroundCornerRadius { get; set; } = new CornerRadius(8);

        // ====== 截断与速度设定 ======
        public static int MaxTextLength { get; set; } = 50;
        public static bool HighlightEllipsis { get; set; } = true;
        public static Brush EllipsisColor { get; set; } = Brushes.LimeGreen;
        public static double ScrollSpeedCharsPerSec { get; set; } = 12.0;
        public static bool AutoStartWorking { get; set; } = true;

        // ====== 运行时应用状态 ======
        /// <summary>
        /// 指示程序当前是否正处于开启（渲染弹幕）状态。
        /// 不进行落盘持久化，每次启动由 AutoStartWorking 赋值。
        /// </summary>
        public static bool IsWorking { get; set; } = false;

        // 默认配置文件保存路径（软件运行目录底下的 JSON 文件）
        private static readonly string DefaultConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BarrageConfig.json");

        /// <summary>
        /// 将当前内存中的配置导出到指定文件路径。如果为空则导出到默认路径。
        /// </summary>
        public static void ExportConfig(string? filePath = null)
        {
            try
            {
                var dto = new BarrageConfigDto
                {
                    FontFamilyName = FontFamily.Source,
                    FontSize = FontSize,
                    FontWeight = FontWeight.ToString(),
                    FontStyle = FontStyle.ToString(),
                    IsUnderlined = IsUnderlined,
                    TextColorHex = (TextColor is SolidColorBrush textBrush) ? textBrush.Color.ToString() : "#FFFFFF",
                    TextOpacity = TextOpacity,
                    ShowAppIcon = ShowAppIcon,
                    ShowAppName = ShowAppName,
                    ShowBackground = ShowBackground,
                    BackgroundColorHex = (BackgroundColor is SolidColorBrush solidBrush) ? solidBrush.Color.ToString() : "#000000",
                    BackgroundOpacity = BackgroundOpacity,
                    BackgroundCornerRadius = BackgroundCornerRadius.TopLeft, // 简化为统一圆角数值进行保存
                    MaxTextLength = MaxTextLength,
                    HighlightEllipsis = HighlightEllipsis,
                    EllipsisColorHex = (EllipsisColor is SolidColorBrush ellBrush) ? ellBrush.Color.ToString() : "#32CD32",
                    ScrollSpeedCharsPerSec = ScrollSpeedCharsPerSec,
                    AutoStartWorking = AutoStartWorking
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(dto, options);
                File.WriteAllText(filePath ?? DefaultConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法导出配置文件: {ex.Message}");
            }
        }

        /// <summary>
        /// 从指定文件路径导入配置。如果发现异常或字体缺失，会自动执行强容错的安全回落机制。
        /// </summary>
        public static void ImportConfig(string? filePath = null)
        {
            string targetPath = filePath ?? DefaultConfigPath;
            
            // 没有配置文件时（初次运行），直接保留初始设置
            if (!File.Exists(targetPath)) return;

            try
            {
                string json = File.ReadAllText(targetPath);
                var dto = JsonSerializer.Deserialize<BarrageConfigDto>(json);
                if (dto == null) return;

                // 1. 安全解析字体 (防灾核心设计点：应对不同 PC 未安装某个特殊字体发生活生生崩溃的 Bug)
                // 原理就是通过系统字库查询比对，如果找得到它，就用它的，找不到就回退为系统最万能的雅黑。
                bool fontExists = Fonts.SystemFontFamilies.Any(f => f.Source.Equals(dto.FontFamilyName, StringComparison.OrdinalIgnoreCase));
                FontFamily = fontExists ? new FontFamily(dto.FontFamilyName) : new FontFamily("Microsoft YaHei");

                // 2. 防御性解析极值数据：对于跨端的极端尺寸和透明度进行物理拦截，防止 UI 直接变不可见或撑爆显存
                FontSize = Math.Clamp(dto.FontSize, 12, 200);
                TextOpacity = Math.Clamp(dto.TextOpacity, 0.1, 1.0);
                BackgroundOpacity = Math.Clamp(dto.BackgroundOpacity, 0.0, 1.0);
                BackgroundCornerRadius = new CornerRadius(Math.Clamp(dto.BackgroundCornerRadius, 0, 100));

                // 3. 布尔值类型（安全），直接覆盖
                IsUnderlined = dto.IsUnderlined;
                ShowAppIcon = dto.ShowAppIcon;
                ShowAppName = dto.ShowAppName;
                ShowBackground = dto.ShowBackground;

                // 4. 解析复杂枚举与结构 (字重/斜体)，一旦手改 json 引入非法字串就原路捕获
                try { FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(dto.FontWeight)!; } catch { FontWeight = FontWeights.Normal; }
                try { FontStyle = (FontStyle)new FontStyleConverter().ConvertFromString(dto.FontStyle)!; } catch { FontStyle = FontStyles.Normal; }

                // 5. 解析系统颜色（HexString 转换到 Brush 画刷模型）
                try { TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.TextColorHex)); } catch { TextColor = Brushes.White; }
                try { BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.BackgroundColorHex)); } catch { BackgroundColor = Brushes.Black; }
                try { EllipsisColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.EllipsisColorHex)); } catch { EllipsisColor = Brushes.LimeGreen; }

                // 6. 新增功能安全回落
                MaxTextLength = Math.Clamp(dto.MaxTextLength, 5, 500);
                ScrollSpeedCharsPerSec = Math.Clamp(dto.ScrollSpeedCharsPerSec, 5.0, 100.0);
                HighlightEllipsis = dto.HighlightEllipsis;
                AutoStartWorking = dto.AutoStartWorking;
                IsWorking = AutoStartWorking; // 启动时自动同步工作状态
            }
            catch
            {
                // 反序列化格式大崩溃时（如把原本内容删了乱写），什么都不做，保留当前启动的默认健壮值
            }
        }
    }
}
