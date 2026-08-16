using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using NotiFlow.Models;

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
        public bool AppNameIsUnderlined { get; set; } = false;
        public bool ContentIsUnderlined { get; set; } = false;
        public bool EllipsisIsUnderlined { get; set; } = false;
        
        public string TextColorHex { get; set; } = "#FFFFFF"; // 默认白色
        public bool ShowTextStroke { get; set; } = false;
        public string TextStrokeColorHex { get; set; } = "#000000"; // 默认黑色
        public double TextStrokeThickness { get; set; } = 1.0;
        public double TextOpacity { get; set; } = 1.0;
        public double LetterSpacing { get; set; } = 0; // 字间距弹幕文字间距
        
        // --- 局部样式配置 (若为空/0 则回退到全局) ---
        public string AppNameTextColorHex { get; set; } = "";
        public double AppNameFontSize { get; set; } = 0;
        public string AppNameFontWeight { get; set; } = "";
        public string AppNameFontStyle { get; set; } = "";
        public string AppNameFontFamilyName { get; set; } = "";
        public double AppNameLetterSpacing { get; set; } = 0;
        public double? AppNameTextOpacity { get; set; } = null;
        public bool? AppNameShowTextStroke { get; set; } = null;
        public bool? ContentShowTextStroke { get; set; } = null;
        
        public string ContentTextColorHex { get; set; } = "";
        public double ContentFontSize { get; set; } = 0;
        public string ContentFontWeight { get; set; } = "";
        public string ContentFontStyle { get; set; } = "";
        public string ContentFontFamilyName { get; set; } = "";
        public double ContentLetterSpacing { get; set; } = 0;
        public double? ContentTextOpacity { get; set; } = null;
        
        public double EllipsisFontSize { get; set; } = 0;
        public string EllipsisColorHex { get; set; } = "";
        public double? EllipsisTextOpacity { get; set; } = null;
        public double AppIconScale { get; set; } = 1.0;
        // ---------------------------------------------
        
        public bool ShowAppIcon { get; set; } = true;
        public bool ShowAppName { get; set; } = true;

        public bool ShowBackground { get; set; } = true;
        public string BackgroundColorHex { get; set; } = "#000000"; // 默认黑色
        public double BackgroundOpacity { get; set; } = 0.4;
        public double BackgroundCornerRadius { get; set; } = 8;
        public bool ShowBackgroundImage { get; set; } = false;
        public string BackgroundImagePath { get; set; } = "";
        public ImageAnchor BackgroundImageAnchor { get; set; } = ImageAnchor.MiddleLeft;
        public double BackgroundImageOffsetX { get; set; } = 0;
        public double BackgroundImageOffsetY { get; set; } = 0;
        public double BackgroundImageScale { get; set; } = 1.0;
        public bool BackgroundImageKeepBaseColor { get; set; } = true;
        public double BackgroundImageOpacity { get; set; } = 1.0;
        
        public int MaxTextLength { get; set; } = 50;
        public bool HighlightEllipsis { get; set; } = true;

        public double ScrollSpeedCharsPerSec { get; set; } = 12.0;
        public string TrackStrategy { get; set; } = "UpperCenter"; // UpperCenter, TopFirst, BottomFirst
        public bool AutoStartWorking { get; set; } = true;

        // 快捷键配置
        public uint HotKeyModifier { get; set; } = 0x0006; // 默认 Ctrl + Shift (MOD_CONTROL | MOD_SHIFT)
        public uint HotKey { get; set; } = 0x44; // 默认 'D' 键
        
        // 更新与版本配置
        public int ConfigVersion { get; set; } = 1;
        public bool AutoCheckUpdate { get; set; } = true;
        public string UpdateSource { get; set; } = "Auto"; // 自动, Gitee, GitHub
        public string SkippedVersion { get; set; } = ""; // 自动检查更新时跳过的版本号
        public string Theme { get; set; } = "System"; // 主题配置：System / Light / Dark
        
        // 行为与系统互操作配置
        public bool AllowCapture { get; set; } = true;         // 允许截图工具截取弹幕
        public bool MinimizeToTray { get; set; } = true;       // 最小化到系统托盘
        public bool CloseToTray { get; set; } = true;          // 关闭主窗口到托盘
        public bool RunOnStartup { get; set; } = false;        // 开机自启动
        
        // 作用域配置
        public string SceneFilterMode { get; set; } = "Disabled";   // Disabled / Whitelist / Blacklist
        public List<Models.ScopeRuleItemDto> SceneBlacklist { get; set; } = new();
        public List<Models.ScopeRuleItemDto> SceneWhitelist { get; set; } = new();
        public string SourceFilterMode { get; set; } = "Disabled";  // Disabled / Whitelist / Blacklist
        public List<Models.ScopeRuleItemDto> SourceBlacklist { get; set; } = new();
        public List<Models.ScopeRuleItemDto> SourceWhitelist { get; set; } = new();
        public List<Models.ScopeRuleItemDto> RecentSourcesCache { get; set; } = new();
        public List<Models.ScopeRuleItemDto> RecentScenesCache { get; set; } = new();
        // 多显示器配置
        public string MultiMonitorMode { get; set; } = "Simultaneous"; // Simultaneous / Sequential
        public List<Models.MonitorSettingItemDto> Monitors { get; set; } = new();

        // 安全模式（防崩溃循环）配置
        public int DeviceCrashCount { get; set; } = 0;
        public int SoftwareCrashCount { get; set; } = 0;
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
        public static bool AppNameIsUnderlined { get; set; } = false;
        public static bool ContentIsUnderlined { get; set; } = false;
        public static bool EllipsisIsUnderlined { get; set; } = false;
        
        public static Brush TextColor { get; set; } = Brushes.White;
        public static bool ShowTextStroke { get; set; } = false;
        public static Brush TextStrokeColor { get; set; } = Brushes.Black;
        public static double TextStrokeThickness { get; set; } = 1.0;
        public static double TextOpacity { get; set; } = 1.0;
        public static double LetterSpacing { get; set; } = 0;
        
        // --- 局部样式配置 (若为空/0 则回退到全局) ---
        public static string AppNameTextColorHex { get; set; } = "";
        public static double AppNameFontSize { get; set; } = 0;
        public static string AppNameFontWeight { get; set; } = "";
        public static string AppNameFontStyle { get; set; } = "";
        public static string AppNameFontFamilyName { get; set; } = "";
        public static double AppNameLetterSpacing { get; set; } = 0;
        public static double? AppNameTextOpacity { get; set; } = null;
        public static bool? AppNameShowTextStroke { get; set; } = null;
        public static bool? ContentShowTextStroke { get; set; } = null;
        
        public static string ContentTextColorHex { get; set; } = "";
        public static double ContentFontSize { get; set; } = 0;
        public static string ContentFontWeight { get; set; } = "";
        public static string ContentFontStyle { get; set; } = "";
        public static string ContentFontFamilyName { get; set; } = "";
        public static double ContentLetterSpacing { get; set; } = 0;
        public static double? ContentTextOpacity { get; set; } = null;
        
        public static double EllipsisFontSize { get; set; } = 0;
        public static string EllipsisColorHex { get; set; } = "";
        public static double? EllipsisTextOpacity { get; set; } = null;
        public static double AppIconScale { get; set; } = 1.0;
        // ---------------------------------------------
        
        public static bool ShowAppIcon { get; set; } = true;
        public static bool ShowAppName { get; set; } = true;

        public static bool ShowBackground { get; set; } = true;
        public static Brush BackgroundColor { get; set; } = Brushes.Black;
        public static double BackgroundOpacity { get; set; } = 0.4;
        public static CornerRadius BackgroundCornerRadius { get; set; } = new CornerRadius(12);
        public static bool ShowBackgroundImage { get; set; } = false;
        public static string BackgroundImagePath { get; set; } = "";
        public static ImageAnchor BackgroundImageAnchor { get; set; } = ImageAnchor.MiddleLeft;
        public static double BackgroundImageOffsetX { get; set; } = 0;
        public static double BackgroundImageOffsetY { get; set; } = 0;
        public static double BackgroundImageScale { get; set; } = 1.0;
        public static bool BackgroundImageKeepBaseColor { get; set; } = true;
        public static double BackgroundImageOpacity { get; set; } = 1.0;

        // ====== 截断与速度设定 ======
        public static int MaxTextLength { get; set; } = 50;
        public static bool HighlightEllipsis { get; set; } = true;
        public static Brush EllipsisColor { get; set; } = Brushes.LimeGreen;
        public static double ScrollSpeedCharsPerSec { get; set; } = 12.0;
        public static string TrackStrategy { get; set; } = "UpperCenter";
        public static bool AutoStartWorking { get; set; } = true;

        // ====== 快捷键配置 ======
        public static uint HotKeyModifier { get; set; } = 0x0006; // MOD_CONTROL | MOD_SHIFT
        public static uint HotKey { get; set; } = 0x44; // 'D'

        // ====== 更新配置 ======
        public static bool AutoCheckUpdate { get; set; } = true;
        public static string UpdateSource { get; set; } = "Auto";
        public static string SkippedVersion { get; set; } = "";
        
        // ====== 界面外观配置 ======
        public static string Theme { get; set; } = "System";

        // ====== 行为与系统互操作 ======
        public static bool AllowCapture { get; set; } = true;
        public static bool MinimizeToTray { get; set; } = true;
        public static bool CloseToTray { get; set; } = true;
        public static bool RunOnStartup { get; set; } = false;

        // ====== 作用域配置 ======
        /// <summary>
        /// 生效场景过滤模式。Disabled = 不过滤；Whitelist = 仅在列表中的应用前台时显示；Blacklist = 列表中的应用前台时隐藏。
        /// </summary>
        public static string SceneFilterMode { get; set; } = "Disabled";
        public static List<Models.ScopeRuleItemDto> SceneBlacklist { get; set; } = new();
        public static List<Models.ScopeRuleItemDto> SceneWhitelist { get; set; } = new();
        
        /// <summary>
        /// 通知来源过滤模式。Disabled = 不过滤；Whitelist = 仅显示列表中应用的通知；Blacklist = 屏蔽列表中应用的通知。
        /// </summary>
        public static string SourceFilterMode { get; set; } = "Disabled";
        public static List<Models.ScopeRuleItemDto> SourceBlacklist { get; set; } = new();
        public static List<Models.ScopeRuleItemDto> SourceWhitelist { get; set; } = new();
        
        /// <summary>
        /// 近期接收过通知的应用缓存（带有历史消息列表）
        /// </summary>
        public static List<Models.ScopeRuleItemDto> RecentSourcesCache { get; set; } = new();
        public static List<Models.ScopeRuleItemDto> RecentScenesCache { get; set; } = new();

        // ====== 多显示器配置 ======
        public static string MultiMonitorMode { get; set; } = "Simultaneous"; // Simultaneous / Sequential
        public static List<Models.MonitorSettingItemDto> Monitors { get; set; } = new();

        // ====== 安全模式（防崩溃循环）配置 ======
        public static int DeviceCrashCount { get; set; } = 0;
        public static int SoftwareCrashCount { get; set; } = 0;
        public static bool IsSafeMode { get; set; } = false;

        // ====== 运行时应用状态 ======
        private static volatile bool _isWorking;
        public static bool IsWorking { get => _isWorking; set => _isWorking = value; }

        private static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "NotiFlow", 
            "BarrageConfig.json");

        public static BarrageConfigDto GetGlobalConfigDto()
        {
            return new BarrageConfigDto
            {
                FontFamilyName = FontFamily.Source,
                FontSize = FontSize,
                FontWeight = FontWeight.ToString(),
                FontStyle = FontStyle.ToString(),
                IsUnderlined = IsUnderlined,
                AppNameIsUnderlined = AppNameIsUnderlined,
                ContentIsUnderlined = ContentIsUnderlined,
                EllipsisIsUnderlined = EllipsisIsUnderlined,
                TextColorHex = (TextColor is SolidColorBrush textBrush) ? textBrush.Color.ToString() : "#FFFFFF",
                ShowTextStroke = ShowTextStroke,
                TextStrokeColorHex = (TextStrokeColor is SolidColorBrush strokeBrush) ? strokeBrush.Color.ToString() : "#000000",
                TextStrokeThickness = TextStrokeThickness,
                TextOpacity = TextOpacity,
                LetterSpacing = LetterSpacing,
                AppNameTextColorHex = AppNameTextColorHex,
                AppNameFontSize = AppNameFontSize,
                AppNameFontWeight = AppNameFontWeight,
                AppNameFontStyle = AppNameFontStyle,
                AppNameFontFamilyName = AppNameFontFamilyName,
                AppNameLetterSpacing = AppNameLetterSpacing,
                AppNameTextOpacity = AppNameTextOpacity,
                AppNameShowTextStroke = AppNameShowTextStroke,
                ContentShowTextStroke = ContentShowTextStroke,
                ContentTextColorHex = ContentTextColorHex,
                ContentFontSize = ContentFontSize,
                ContentFontWeight = ContentFontWeight,
                ContentFontStyle = ContentFontStyle,
                ContentFontFamilyName = ContentFontFamilyName,
                ContentLetterSpacing = ContentLetterSpacing,
                ContentTextOpacity = ContentTextOpacity,
                EllipsisFontSize = EllipsisFontSize,
                EllipsisColorHex = EllipsisColorHex,
                EllipsisTextOpacity = EllipsisTextOpacity,
                AppIconScale = AppIconScale,
                ShowAppIcon = ShowAppIcon,
                ShowAppName = ShowAppName,
                ShowBackground = ShowBackground,
                BackgroundColorHex = (BackgroundColor is SolidColorBrush bgBrush) ? bgBrush.Color.ToString() : "#000000",
                BackgroundOpacity = BackgroundOpacity,
                BackgroundCornerRadius = BackgroundCornerRadius.TopLeft,
                ShowBackgroundImage = ShowBackgroundImage,
                BackgroundImagePath = BackgroundImagePath,
                BackgroundImageAnchor = BackgroundImageAnchor,
                BackgroundImageOffsetX = BackgroundImageOffsetX,
                BackgroundImageOffsetY = BackgroundImageOffsetY,
                BackgroundImageScale = BackgroundImageScale,
                BackgroundImageKeepBaseColor = BackgroundImageKeepBaseColor,
                BackgroundImageOpacity = BackgroundImageOpacity,
                MaxTextLength = MaxTextLength,
                HighlightEllipsis = HighlightEllipsis,

                ScrollSpeedCharsPerSec = ScrollSpeedCharsPerSec,
                TrackStrategy = TrackStrategy,
                AutoStartWorking = AutoStartWorking,
                HotKeyModifier = HotKeyModifier,
                HotKey = HotKey,
                ConfigVersion = 1,
                AutoCheckUpdate = AutoCheckUpdate,
                UpdateSource = UpdateSource,
                Theme = Theme,
                AllowCapture = AllowCapture,
                MinimizeToTray = MinimizeToTray,
                CloseToTray = CloseToTray,
                RunOnStartup = RunOnStartup,
                SceneFilterMode = SceneFilterMode,
                SceneBlacklist = SceneBlacklist,
                SceneWhitelist = SceneWhitelist,
                SourceFilterMode = SourceFilterMode,
                SourceBlacklist = SourceBlacklist,
                SourceWhitelist = SourceWhitelist,
                RecentSourcesCache = RecentSourcesCache,
                RecentScenesCache = RecentScenesCache,
                MultiMonitorMode = MultiMonitorMode,
                Monitors = Monitors,
                DeviceCrashCount = DeviceCrashCount,
                SoftwareCrashCount = SoftwareCrashCount
            };
        }

        public static BarrageConfigDto GetResolvedConfig(string sourceAumid, string foregroundExe)
        {
            if (!string.IsNullOrEmpty(sourceAumid))
            {
                var sourceRule = SourceWhitelist.Concat(SourceBlacklist).Concat(RecentSourcesCache)
                    .FirstOrDefault(x => string.Equals(x.Identifier, sourceAumid, StringComparison.OrdinalIgnoreCase));
                if (sourceRule != null && sourceRule.StyleOverride != null)
                {
                    return sourceRule.StyleOverride;
                }
            }
            
            if (!string.IsNullOrEmpty(foregroundExe))
            {
                var sceneRule = SceneWhitelist.Concat(SceneBlacklist).Concat(RecentScenesCache)
                    .FirstOrDefault(x => string.Equals(x.Identifier, foregroundExe, StringComparison.OrdinalIgnoreCase));
                if (sceneRule != null && sceneRule.StyleOverride != null)
                {
                    return sceneRule.StyleOverride;
                }
            }
            
            return GetGlobalConfigDto();
        }

        public static void ExportConfig(string? filePath = null)
        {
            try
            {
                var dto = GetGlobalConfigDto();
                string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath ?? DefaultConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export config: {ex.Message}");
            }
        }

        public static void ImportConfig(string? filePath = null)
        {
            string targetPath = filePath ?? DefaultConfigPath;
            
            if (!File.Exists(targetPath) && filePath == null)
            {
                string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BarrageConfig.json");
                if (File.Exists(legacyPath))
                {
                    string? dir = Path.GetDirectoryName(targetPath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.Copy(legacyPath, targetPath);
                }
            }

            if (!File.Exists(targetPath)) return;

            try
            {
                string json = File.ReadAllText(targetPath);
                var dto = JsonSerializer.Deserialize<BarrageConfigDto>(json);
                if (dto == null) return;

                bool fontExists = Fonts.SystemFontFamilies.Any(f => f.Source.Equals(dto.FontFamilyName, StringComparison.OrdinalIgnoreCase));
                FontFamily = fontExists ? new FontFamily(dto.FontFamilyName) : new FontFamily("Microsoft YaHei");

                FontSize = Math.Clamp(dto.FontSize, 12, 200);
                TextOpacity = Math.Clamp(dto.TextOpacity, 0.1, 1.0);
                LetterSpacing = Math.Clamp(dto.LetterSpacing, 0, 100);
                
                AppNameTextColorHex = dto.AppNameTextColorHex ?? "";
                AppNameFontSize = dto.AppNameFontSize;
                AppNameFontWeight = dto.AppNameFontWeight ?? "";
                AppNameFontStyle = dto.AppNameFontStyle ?? "";
                AppNameFontFamilyName = dto.AppNameFontFamilyName ?? "";
                AppNameLetterSpacing = dto.AppNameLetterSpacing;
                AppNameTextOpacity = dto.AppNameTextOpacity;
                AppNameShowTextStroke = dto.AppNameShowTextStroke;
                ContentShowTextStroke = dto.ContentShowTextStroke;
                
                ContentTextColorHex = dto.ContentTextColorHex ?? "";
                ContentFontSize = dto.ContentFontSize;
                ContentFontWeight = dto.ContentFontWeight ?? "";
                ContentFontStyle = dto.ContentFontStyle ?? "";
                ContentFontFamilyName = dto.ContentFontFamilyName ?? "";
                ContentLetterSpacing = dto.ContentLetterSpacing;
                ContentTextOpacity = dto.ContentTextOpacity;
                
                EllipsisFontSize = dto.EllipsisFontSize;
                EllipsisColorHex = dto.EllipsisColorHex ?? "";
                EllipsisTextOpacity = dto.EllipsisTextOpacity;
                AppIconScale = dto.AppIconScale > 0 ? dto.AppIconScale : 1.0;
                
                BackgroundOpacity = Math.Clamp(dto.BackgroundOpacity, 0.0, 1.0);
                BackgroundCornerRadius = new CornerRadius(Math.Clamp(dto.BackgroundCornerRadius, 0, 100));
                IsUnderlined = dto.IsUnderlined;
                AppNameIsUnderlined = dto.AppNameIsUnderlined;
                ContentIsUnderlined = dto.ContentIsUnderlined;
                EllipsisIsUnderlined = dto.EllipsisIsUnderlined;
                ShowAppIcon = dto.ShowAppIcon;
                ShowAppName = dto.ShowAppName;
                ShowBackground = dto.ShowBackground;

                try { FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(dto.FontWeight)!; } catch { FontWeight = FontWeights.Normal; }
                try { FontStyle = (FontStyle)new FontStyleConverter().ConvertFromString(dto.FontStyle)!; } catch { FontStyle = FontStyles.Normal; }

                try { TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.TextColorHex)); } catch { TextColor = Brushes.White; }
                
                ShowTextStroke = dto.ShowTextStroke;
                try { TextStrokeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.TextStrokeColorHex)); } catch { TextStrokeColor = Brushes.Black; }
                TextStrokeThickness = Math.Clamp(dto.TextStrokeThickness, 0.0, 10.0);

                if (!string.IsNullOrWhiteSpace(dto.BackgroundColorHex))
                {
                    try { BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dto.BackgroundColorHex)); } catch { }
                }
                
                ShowBackgroundImage = dto.ShowBackgroundImage;
                BackgroundImagePath = dto.BackgroundImagePath;
                BackgroundImageAnchor = dto.BackgroundImageAnchor;
                BackgroundImageOffsetX = dto.BackgroundImageOffsetX;
                BackgroundImageOffsetY = dto.BackgroundImageOffsetY;
                BackgroundImageScale = dto.BackgroundImageScale;
                BackgroundImageKeepBaseColor = dto.BackgroundImageKeepBaseColor;
                BackgroundImageOpacity = Math.Clamp(dto.BackgroundImageOpacity, 0.0, 1.0);

                MaxTextLength = Math.Clamp(dto.MaxTextLength, 10, 500);
                ScrollSpeedCharsPerSec = Math.Clamp(dto.ScrollSpeedCharsPerSec, 5.0, 100.0);
                TrackStrategy = (dto.TrackStrategy == "TopFirst" || dto.TrackStrategy == "BottomFirst") ? dto.TrackStrategy : "UpperCenter";
                HighlightEllipsis = dto.HighlightEllipsis;
                AutoStartWorking = dto.AutoStartWorking;
                IsWorking = AutoStartWorking;

                HotKeyModifier = dto.HotKeyModifier;
                HotKey = dto.HotKey;
                
                AutoCheckUpdate = dto.AutoCheckUpdate;
                UpdateSource = dto.UpdateSource ?? "Auto";
                SkippedVersion = dto.SkippedVersion ?? "";
                Theme = dto.Theme ?? "System";
                AllowCapture = dto.AllowCapture;
                MinimizeToTray = dto.MinimizeToTray;
                CloseToTray = dto.CloseToTray;
                RunOnStartup = dto.RunOnStartup;
                
                SceneFilterMode = (dto.SceneFilterMode == "Whitelist" || dto.SceneFilterMode == "Blacklist") ? dto.SceneFilterMode : "Disabled";
                SceneBlacklist = dto.SceneBlacklist ?? new();
                SceneWhitelist = dto.SceneWhitelist ?? new();
                
                SourceFilterMode = (dto.SourceFilterMode == "Whitelist" || dto.SourceFilterMode == "Blacklist") ? dto.SourceFilterMode : "Disabled";
                SourceBlacklist = dto.SourceBlacklist ?? new();
                SourceWhitelist = dto.SourceWhitelist ?? new();
                RecentSourcesCache = dto.RecentSourcesCache ?? new();
                RecentScenesCache = dto.RecentScenesCache ?? new();

                MultiMonitorMode = (dto.MultiMonitorMode == "Sequential") ? "Sequential" : "Simultaneous";
                Monitors = Services.ScreenService.GetMergedMonitors(dto.Monitors);

                DeviceCrashCount = dto.DeviceCrashCount;
                SoftwareCrashCount = dto.SoftwareCrashCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法导入配置文件: {ex.Message}");
            }
        }

        public static void ResetToDefault()
        {
            FontFamily = new FontFamily("Microsoft YaHei");
            FontSize = 36;
            FontWeight = FontWeights.Bold;
            FontStyle = FontStyles.Normal;
            IsUnderlined = false;
            AppNameIsUnderlined = false;
            ContentIsUnderlined = false;
            EllipsisIsUnderlined = false;
            TextColor = Brushes.White;
            ShowTextStroke = false;
            TextStrokeColor = Brushes.Black;
            TextStrokeThickness = 1.0;
            TextOpacity = 1.0;
            LetterSpacing = 0;
            
            AppNameTextColorHex = "";
            AppNameFontSize = 0;
            AppNameFontWeight = "";
            AppNameFontStyle = "";
            
            ContentTextColorHex = "";
            ContentFontSize = 0;
            ContentFontWeight = "";
            ContentFontStyle = "";
            ContentFontFamilyName = "";
            ContentLetterSpacing = 0;
            ContentTextOpacity = 1.0;
            
            EllipsisFontSize = 0;
            AppIconScale = 1.0;
            
            ShowAppIcon = true;
            ShowAppName = true;
            ShowBackground = true;
            BackgroundColor = Brushes.Black;
            BackgroundOpacity = 0.5;
            BackgroundCornerRadius = new CornerRadius(12);
            ShowBackgroundImage = false;
            BackgroundImagePath = "";
            BackgroundImageAnchor = ImageAnchor.MiddleLeft;
            BackgroundImageOffsetX = 0;
            BackgroundImageOffsetY = 0;
            BackgroundImageScale = 1.0;
            BackgroundImageKeepBaseColor = true;
            BackgroundImageOpacity = 1.0;

            MaxTextLength = 40;
            HighlightEllipsis = true;
            EllipsisColor = Brushes.LimeGreen;
            ScrollSpeedCharsPerSec = 12.0;
            TrackStrategy = "UpperCenter";
            AutoStartWorking = true;
            HotKeyModifier = 0x0006;
            HotKey = 0x44;
            AutoCheckUpdate = true;
            UpdateSource = "Auto";
            SkippedVersion = "";
            Theme = "System";
            AllowCapture = true;
            MinimizeToTray = true;
            CloseToTray = true;
            RunOnStartup = false;
            SceneFilterMode = "Disabled";
            SceneBlacklist = new();
            SceneWhitelist = new();
            SourceFilterMode = "Disabled";
            SourceBlacklist = new();
            SourceWhitelist = new();
            RecentSourcesCache = new();
            RecentScenesCache = new();
            DeviceCrashCount = 0;
            SoftwareCrashCount = 0;
            IsSafeMode = false;
            
            // 重置后立即保存生效
            ExportConfig();
        }
    }
}
