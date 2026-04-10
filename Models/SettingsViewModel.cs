using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Windows;
using System.Windows.Threading;

namespace NotiFlow.Models
{
    /// <summary>
    /// 设置界面的总调度 ViewModel，主要作用是将静态层里的 BarrageSettings 与前端进行带缓冲的双向绑定
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly DispatcherTimer _debounceTimer;
        public SettingsViewModel()
        {
            // 从静态内存中拉取初始状态
            _fontSize = BarrageSettings.FontSize;
            _maxTextLength = BarrageSettings.MaxTextLength;
            _textOpacityPercentage = BarrageSettings.TextOpacity * 100;
            _backgroundOpacityPercentage = BarrageSettings.BackgroundOpacity * 100;
            _showAppIcon = BarrageSettings.ShowAppIcon;
            _showAppName = BarrageSettings.ShowAppName;
            _highlightEllipsis = BarrageSettings.HighlightEllipsis;
            _scrollSpeedCharsPerSec = BarrageSettings.ScrollSpeedCharsPerSec;
            
            _isFontWeightBold = BarrageSettings.FontWeight == FontWeights.Bold;
            _isFontStyleItalic = BarrageSettings.FontStyle == FontStyles.Italic;
            _isUnderline = BarrageSettings.IsUnderlined;

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                BarrageSettings.ExportConfig();
                WeakReferenceMessenger.Default.Send(new BarragePreviewMessage(""));
            };
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
