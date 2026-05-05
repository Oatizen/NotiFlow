using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NotiFlow.Models
{
    /// <summary>
    /// 用于包装 FontFamily 并提供其本地化名称（如处理中文名，防止符号字体显示乱码）
    /// </summary>
    public class FontViewModel
    {
        public FontFamily Family { get; }
        public string LocalizedName { get; }

        public FontViewModel(FontFamily family)
        {
            Family = family;
            
            // 尝试获取本地化字体名称，优先级：中文 -> 英语 -> 字体原始名称
            var zhLang = System.Windows.Markup.XmlLanguage.GetLanguage("zh-cn");
            var enLang = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");

            if (family.FamilyNames.TryGetValue(zhLang, out string? zhName))
                LocalizedName = zhName;
            else if (family.FamilyNames.TryGetValue(enLang, out string? enName))
                LocalizedName = enName;
            else
                LocalizedName = family.Source;
        }
    }

    /// <summary>
    /// 用于装载预设调色板信息的结构体
    /// </summary>
    public class ColorPaletteItem
    {
        public string Name { get; set; }
        public string Hex { get; set; }
        public Brush Brush { get; set; }
    }

    /// <summary>
    /// 设置界面的总调度 ViewModel，主要作用是将静态层里的 BarrageSettings 与前端进行带缓冲的双向绑定
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly DispatcherTimer _debounceTimer;
        public SettingsViewModel()
        {
            // 从静态内存中拉取初始状态
            
            // 初始化系统字体列表并尝试配对当前使用字体
            AvailableFonts = Fonts.SystemFontFamilies
                .Select(f => new FontViewModel(f))
                .OrderBy(f => f.LocalizedName)
                .ToList();
                
            var currentSource = BarrageSettings.FontFamily.Source;
            _selectedFontItem = AvailableFonts.FirstOrDefault(f => f.Family.Source.Equals(currentSource, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableFonts.FirstOrDefault(f => f.Family.Source.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase))
                               ?? AvailableFonts.FirstOrDefault();

            _textColorHex = (BarrageSettings.TextColor is SolidColorBrush brush) ? brush.Color.ToString() : "#FFFFFF";
            _currentColorBrush = BarrageSettings.TextColor;

            PresetColors = new ObservableCollection<ColorPaletteItem>
            {
                new ColorPaletteItem { Name = "纯净白", Hex = "#FFFFFF", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")) },
                new ColorPaletteItem { Name = "樱花粉", Hex = "#FFA1C5", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA1C5")) },
                new ColorPaletteItem { Name = "明媚黄", Hex = "#FFD700", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")) },
                new ColorPaletteItem { Name = "青草绿", Hex = "#98FB98", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#98FB98")) },
                new ColorPaletteItem { Name = "天际蓝", Hex = "#87CEEB", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#87CEEB")) },
                new ColorPaletteItem { Name = "梦幻紫", Hex = "#DDA0DD", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDA0DD")) },
                new ColorPaletteItem { Name = "活力橙", Hex = "#FFA500", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA500")) },
                new ColorPaletteItem { Name = "暗夜黑", Hex = "#000000", Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000")) },
            };

            _fontSize = BarrageSettings.FontSize;
            _maxTextLength = BarrageSettings.MaxTextLength;
            _textOpacityPercentage = BarrageSettings.TextOpacity * 100;
            _backgroundOpacityPercentage = BarrageSettings.BackgroundOpacity * 100;
            _showAppIcon = BarrageSettings.ShowAppIcon;
            _showAppName = BarrageSettings.ShowAppName;
            _highlightEllipsis = BarrageSettings.HighlightEllipsis;
            _scrollSpeedCharsPerSec = BarrageSettings.ScrollSpeedCharsPerSec;
            _trackStrategy = BarrageSettings.TrackStrategy;
            
            _isFontWeightBold = BarrageSettings.FontWeight == FontWeights.Bold;
            _isFontStyleItalic = BarrageSettings.FontStyle == FontStyles.Italic;
            _isUnderline = BarrageSettings.IsUnderlined;
            _autoStartWorking = BarrageSettings.AutoStartWorking;
            _autoCheckUpdate = BarrageSettings.AutoCheckUpdate;
            _updateSource = BarrageSettings.UpdateSource;
            _allowCapture = BarrageSettings.AllowCapture;
            _minimizeToTray = BarrageSettings.MinimizeToTray;
            _closeToTray = BarrageSettings.CloseToTray;
            _runOnStartup = BarrageSettings.RunOnStartup;
            _hotKeyText = GetHotKeyString(BarrageSettings.HotKeyModifier, BarrageSettings.HotKey);

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                BarrageSettings.ExportConfig();
                WeakReferenceMessenger.Default.Send(new BarragePreviewMessage(""));
            };
        }

        public IEnumerable<FontViewModel> AvailableFonts { get; }

        [ObservableProperty]
        private FontViewModel? _selectedFontItem;
        partial void OnSelectedFontItemChanged(FontViewModel? value)
        {
            if (value != null)
            {
                BarrageSettings.FontFamily = value.Family;
                TriggerSaveAndPreview();
            }
        }

        public ObservableCollection<ColorPaletteItem> PresetColors { get; }

        [ObservableProperty]
        private Brush _currentColorBrush;

        [ObservableProperty]
        private string _textColorHex;
        partial void OnTextColorHexChanged(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var color = (Color)ColorConverter.ConvertFromString(value);
                CurrentColorBrush = new SolidColorBrush(color);
                BarrageSettings.TextColor = CurrentColorBrush;
                TriggerSaveAndPreview();
            }
            catch
            {
                // 如果输入不合法，原封不动，防止崩溃
            }
        }

        [RelayCommand]
        private void SelectColor(string hex)
        {
            TextColorHex = hex;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FontSizeDisplay))]
        private double _fontSize;
        partial void OnFontSizeChanged(double value)
        {
            BarrageSettings.FontSize = value;
            TriggerSaveAndPreview();
        }
        public string FontSizeDisplay => $"{(int)FontSize}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxLengthDisplay))]
        private int _maxTextLength;
        partial void OnMaxTextLengthChanged(int value)
        {
            BarrageSettings.MaxTextLength = value;
            TriggerSaveAndPreview();
        }
        public string MaxLengthDisplay => $"{MaxTextLength} 字";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextOpacityDisplay))]
        private double _textOpacityPercentage;
        partial void OnTextOpacityPercentageChanged(double value)
        {
            BarrageSettings.TextOpacity = value / 100.0;
            TriggerSaveAndPreview();
        }
        public string TextOpacityDisplay => $"{(int)TextOpacityPercentage}%";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BackgroundOpacityDisplay))]
        private double _backgroundOpacityPercentage;
        partial void OnBackgroundOpacityPercentageChanged(double value)
        {
            BarrageSettings.BackgroundOpacity = value / 100.0;
            TriggerSaveAndPreview();
        }
        public string BackgroundOpacityDisplay => $"{(int)BackgroundOpacityPercentage}%";

        [ObservableProperty]
        private bool _showAppIcon;
        partial void OnShowAppIconChanged(bool value)
        {
            BarrageSettings.ShowAppIcon = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _showAppName;
        partial void OnShowAppNameChanged(bool value)
        {
            BarrageSettings.ShowAppName = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _highlightEllipsis;
        partial void OnHighlightEllipsisChanged(bool value)
        {
            BarrageSettings.HighlightEllipsis = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SpeedDisplay))]
        private double _scrollSpeedCharsPerSec;
        partial void OnScrollSpeedCharsPerSecChanged(double value)
        {
            BarrageSettings.ScrollSpeedCharsPerSec = value;
            TriggerSaveAndPreview();
        }
        
        public string SpeedDisplay
        {
            get
            {
                string label = "中";
                if (ScrollSpeedCharsPerSec < 10) label = "慢";
                else if (ScrollSpeedCharsPerSec >= 20) label = "快";
                return $"{label} ({(int)ScrollSpeedCharsPerSec} 字/秒)";
            }
        }

        [ObservableProperty]
        private string _trackStrategy;
        partial void OnTrackStrategyChanged(string value)
        {
            BarrageSettings.TrackStrategy = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(IsTrackUpperCenter));
            OnPropertyChanged(nameof(IsTrackTopFirst));
            OnPropertyChanged(nameof(IsTrackBottomFirst));
        }

        public bool IsTrackUpperCenter
        {
            get => TrackStrategy == "UpperCenter";
            set { if (value) TrackStrategy = "UpperCenter"; }
        }

        public bool IsTrackTopFirst
        {
            get => TrackStrategy == "TopFirst";
            set { if (value) TrackStrategy = "TopFirst"; }
        }

        public bool IsTrackBottomFirst
        {
            get => TrackStrategy == "BottomFirst";
            set { if (value) TrackStrategy = "BottomFirst"; }
        }

        [ObservableProperty]
        private bool _isFontWeightBold;
        partial void OnIsFontWeightBoldChanged(bool value)
        {
            BarrageSettings.FontWeight = value ? FontWeights.Bold : FontWeights.Normal;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _isFontStyleItalic;
        partial void OnIsFontStyleItalicChanged(bool value)
        {
            BarrageSettings.FontStyle = value ? FontStyles.Italic : FontStyles.Normal;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _isUnderline;
        partial void OnIsUnderlineChanged(bool value)
        {
            BarrageSettings.IsUnderlined = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _autoStartWorking;
        partial void OnAutoStartWorkingChanged(bool value)
        {
            BarrageSettings.AutoStartWorking = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _autoCheckUpdate;
        partial void OnAutoCheckUpdateChanged(bool value)
        {
            BarrageSettings.AutoCheckUpdate = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _updateSource;
        partial void OnUpdateSourceChanged(string value)
        {
            BarrageSettings.UpdateSource = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(IsUpdateSourceAuto));
            OnPropertyChanged(nameof(IsUpdateSourceGitee));
            OnPropertyChanged(nameof(IsUpdateSourceGitHub));
        }

        public bool IsUpdateSourceAuto
        {
            get => UpdateSource == "Auto";
            set { if (value) UpdateSource = "Auto"; }
        }

        public bool IsUpdateSourceGitee
        {
            get => UpdateSource == "Gitee";
            set { if (value) UpdateSource = "Gitee"; }
        }

        public bool IsUpdateSourceGitHub
        {
            get => UpdateSource == "GitHub";
            set { if (value) UpdateSource = "GitHub"; }
        }

        [ObservableProperty]
        private bool _allowCapture;
        partial void OnAllowCaptureChanged(bool value)
        {
            BarrageSettings.AllowCapture = value;
            TriggerSaveAndPreview();
            
            // 立即生效防截屏设置
            Application.Current.Dispatcher.Invoke(() => 
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWin)
                    {
                        mainWin.ApplyCaptureSetting();
                    }
                }
            });
        }

        [ObservableProperty]
        private bool _minimizeToTray;
        partial void OnMinimizeToTrayChanged(bool value)
        {
            BarrageSettings.MinimizeToTray = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _closeToTray;
        partial void OnCloseToTrayChanged(bool value)
        {
            BarrageSettings.CloseToTray = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _runOnStartup;
        partial void OnRunOnStartupChanged(bool value)
        {
            BarrageSettings.RunOnStartup = value;
            TriggerSaveAndPreview();
            
            // 处理开机自启动快捷方式
            App.UpdateStartupShortcut(value);
        }

        [ObservableProperty]
        private string _hotKeyText;

        [ObservableProperty]
        private bool _isCapturingHotKey;

        [RelayCommand]
        private void StartCaptureHotKey()
        {
            IsCapturingHotKey = true;
            HotKeyText = "输入快捷键以绑定";
        }

        public void FinishCaptureHotKey(uint modifiers, uint key)
        {
            BarrageSettings.HotKeyModifier = modifiers;
            BarrageSettings.HotKey = key;
            HotKeyText = GetHotKeyString(modifiers, key);
            IsCapturingHotKey = false;
            
            // 立即生效：通知托盘服务重新注册热键
            (App.Current as App)?.TrayIconService?.ReRegisterHotKey();
            
            // 保存配置
            BarrageSettings.ExportConfig();
        }

        public string GetHotKeyString(uint modifiers, uint key)
        {
            var parts = new List<string>();
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
            
            // 将 KeyCode 转为字符串 (简单映射，满足常用场景)
            string keyName = ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)key)).ToString();
            parts.Add(keyName);
            
            return string.Join(" + ", parts);
        }

        [RelayCommand]
        private void SetFontWeight(string isBoldStr) => IsFontWeightBold = bool.Parse(isBoldStr);

        [RelayCommand]
        private void ToggleFontStyle() => IsFontStyleItalic = !IsFontStyleItalic;

        [RelayCommand]
        private void ToggleUnderline() => IsUnderline = !IsUnderline;

        /// <summary>
        /// 当 UI 的值成功反写回底部的静态内存对象后，触发落地 IO 和一次通知中心预览
        /// </summary>
        private void TriggerSaveAndPreview()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }
}
