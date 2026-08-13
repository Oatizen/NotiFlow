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
    public class FontViewModel
    {
        public FontFamily Family { get; }
        public string LocalizedName { get; }

        public FontViewModel(FontFamily family)
        {
            Family = family;
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

    public class ColorPaletteItem
    {
        public string Name { get; set; }
        public string Hex { get; set; }
        public Brush Brush { get; set; }
    }

    public partial class ScopeSelectionItem : ObservableObject
    {
        public string DisplayName { get; set; }
        public string Identifier { get; set; }
        public string Type { get; set; } // "Global", "Source", "Scene"
        public ScopeRuleItemDto RuleItem { get; set; }

        [ObservableProperty]
        private bool _hasOverride;
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly DispatcherTimer _debounceTimer;
        private bool _isSyncing = false;

        public ObservableCollection<ScopeSelectionItem> ConfigScopes { get; } = new();
        public ObservableCollection<ScopeSelectionItem> FilteredScopes { get; } = new();

        [ObservableProperty]
        private string _scopeCategory = "Global"; // "Global", "Scene", "Source"

        public bool IsCategoryGlobal
        {
            get => ScopeCategory == "Global";
            set { if (value) { ScopeCategory = "Global"; OnPropertyChanged(); OnPropertyChanged(nameof(IsCategoryScene)); OnPropertyChanged(nameof(IsCategorySource)); } }
        }

        public bool IsCategoryScene
        {
            get => ScopeCategory == "Scene";
            set { if (value) { ScopeCategory = "Scene"; OnPropertyChanged(); OnPropertyChanged(nameof(IsCategoryGlobal)); OnPropertyChanged(nameof(IsCategorySource)); } }
        }

        public bool IsCategorySource
        {
            get => ScopeCategory == "Source";
            set { if (value) { ScopeCategory = "Source"; OnPropertyChanged(); OnPropertyChanged(nameof(IsCategoryGlobal)); OnPropertyChanged(nameof(IsCategoryScene)); } }
        }

        public Visibility ComboBoxVisibility => (ScopeCategory == "Global" || FilteredScopes.Count == 0) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EmptyListMessageVisibility => (ScopeCategory != "Global" && FilteredScopes.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ClearButtonVisibility => (SelectedScope != null && SelectedScope.HasOverride && !IsCategoryGlobal) ? Visibility.Visible : Visibility.Collapsed;
        partial void OnScopeCategoryChanged(string value)
        {
            if (value == "Global")
            {
                SelectedScope = ConfigScopes.FirstOrDefault(s => s.Type == "Global");
            }
            else
            {
                FilteredScopes.Clear();
                foreach (var item in ConfigScopes.Where(s => s.Type == value))
                {
                    FilteredScopes.Add(item);
                }
                SelectedScope = FilteredScopes.FirstOrDefault();
            }
            OnPropertyChanged(nameof(ComboBoxVisibility));
            OnPropertyChanged(nameof(EmptyListMessageVisibility));
            OnPropertyChanged(nameof(ClearButtonVisibility));
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditingGlobal))]
        private ScopeSelectionItem _selectedScope;
        partial void OnSelectedScopeChanged(ScopeSelectionItem value)
        {
            OnPropertyChanged(nameof(ClearButtonVisibility));
            if (value == null) return;
            LoadConfigToUI();
        }

        private BarrageConfigDto GetTargetConfig(bool createIfNull)
        {
            if (SelectedScope == null || SelectedScope.Type == "Global") return null;
            if (SelectedScope.RuleItem.StyleOverride == null)
            {
                if (createIfNull)
                {
                    SelectedScope.RuleItem.StyleOverride = BarrageSettings.GetGlobalConfigDto();
                    SelectedScope.HasOverride = true;

                    if (SelectedScope.Type == "Scene")
                    {
                        if (!BarrageSettings.SceneWhitelist.Contains(SelectedScope.RuleItem) &&
                            !BarrageSettings.SceneBlacklist.Contains(SelectedScope.RuleItem) &&
                            !BarrageSettings.RecentScenesCache.Contains(SelectedScope.RuleItem))
                        {
                            BarrageSettings.RecentScenesCache.Add(SelectedScope.RuleItem);
                        }
                    }
                    else if (SelectedScope.Type == "Source")
                    {
                        if (!BarrageSettings.SourceWhitelist.Contains(SelectedScope.RuleItem) &&
                            !BarrageSettings.SourceBlacklist.Contains(SelectedScope.RuleItem) &&
                            !BarrageSettings.RecentSourcesCache.Contains(SelectedScope.RuleItem))
                        {
                            BarrageSettings.RecentSourcesCache.Add(SelectedScope.RuleItem);
                        }
                    }

                    OnPropertyChanged(nameof(ClearButtonVisibility));
                }
            }
            return SelectedScope.RuleItem.StyleOverride;
        }

        [RelayCommand]
        private void SetScopeCategory(string category)
        {
            if (category == "Global") IsCategoryGlobal = true;
            else if (category == "Scene") IsCategoryScene = true;
            else if (category == "Source") IsCategorySource = true;
        }

        [RelayCommand]
        private void ClearScopeOverride()
        {
            if (SelectedScope != null && SelectedScope.Type != "Global" && SelectedScope.RuleItem != null)
            {
                SelectedScope.RuleItem.StyleOverride = null;
                SelectedScope.HasOverride = false;
                OnPropertyChanged(nameof(ClearButtonVisibility));
                LoadConfigToUI();
                TriggerSaveAndPreview();
            }
        }

        public bool IsEditingGlobal => SelectedScope == null || SelectedScope.Type == "Global";

        public SettingsViewModel()
        {
            AvailableFonts = Fonts.SystemFontFamilies
                .Select(f => new FontViewModel(f))
                .OrderBy(f => f.LocalizedName)
                .ToList();

            var hexColors = new[]
            {
                "#EF9A9A", "#FFCC80", "#C5E1A5", "#90CAF9", "#CE93D8", "#E0E0E0",
                "#E57373", "#FFB74D", "#AED581", "#64B5F6", "#BA68C8", "#BDBDBD",
                "#EF5350", "#FFA726", "#9CCC65", "#42A5F5", "#AB47BC", "#9E9E9E",
                "#F44336", "#FF9800", "#8BC34A", "#2196F3", "#9C27B0", "#757575",
                "#E53935", "#F57C00", "#7CB342", "#1E88E5", "#8E24AA", "#616161",
                "#D32F2F", "#EF6C00", "#689F38", "#1976D2", "#7B1FA2", "#424242",
                "#C62828", "#E65100", "#558B2F", "#1565C0", "#6A1B9A", "#212121",
                "#B71C1C", "#BF360C", "#33691E", "#0D47A1", "#4A148C", "#000000"
            };

            PresetColors = new ObservableCollection<ColorPaletteItem>();
            foreach (var hex in hexColors)
            {
                PresetColors.Add(new ColorPaletteItem 
                { 
                    Name = hex, 
                    Hex = hex, 
                    Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) 
                });
            }

            ReloadScopes();

            // Load global non-scoped properties
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
                WeakReferenceMessenger.Default.Send(new BarragePreviewMessage(SelectedScope?.Type == "Global" ? "" : SelectedScope?.Identifier));
            };
        }

        public async void ReloadScopes()
        {
            var newScopes = new System.Collections.Generic.List<ScopeSelectionItem>();
            var globalScope = new ScopeSelectionItem { DisplayName = "全局", Type = "Global" };
            newScopes.Add(globalScope);

            foreach (var item in BarrageSettings.SourceWhitelist.Concat(BarrageSettings.SourceBlacklist).Concat(BarrageSettings.RecentSourcesCache).GroupBy(x => x.Identifier).Select(g => g.First()))
            {
                newScopes.Add(new ScopeSelectionItem { DisplayName = item.DisplayName, Identifier = item.Identifier, Type = "Source", RuleItem = item, HasOverride = item.StyleOverride != null });
            }

            foreach (var item in BarrageSettings.SceneWhitelist.Concat(BarrageSettings.SceneBlacklist).Concat(BarrageSettings.RecentScenesCache).GroupBy(x => x.Identifier).Select(g => g.First()))
            {
                newScopes.Add(new ScopeSelectionItem { DisplayName = item.DisplayName, Identifier = item.Identifier, Type = "Scene", RuleItem = item, HasOverride = item.StyleOverride != null });
            }

            try
            {
                var processes = await System.Threading.Tasks.Task.Run(() => Services.ProcessEnumerator.EnumerateWindowProcesses());
                foreach (var proc in processes)
                {
                    if (!newScopes.Any(s => s.Type == "Scene" && s.Identifier.Equals(proc.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var newRule = new ScopeRuleItemDto { DisplayName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? proc.MainWindowTitle : proc.ProcessName, Identifier = proc.ProcessName };
                        newScopes.Add(new ScopeSelectionItem { DisplayName = newRule.DisplayName, Identifier = newRule.Identifier, Type = "Scene", RuleItem = newRule, HasOverride = false });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to enumerate processes: " + ex.Message);
            }

            ConfigScopes.Clear();
            foreach (var s in newScopes)
            {
                ConfigScopes.Add(s);
            }

            // Make sure we trigger property change for EmptyListMessageVisibility if we are already in Scene
            if (ScopeCategory == "Scene" || ScopeCategory == "Source")
            {
                OnScopeCategoryChanged(ScopeCategory);
            }
            else
            {
                ScopeCategory = "Global";
                SelectedScope = globalScope;
            }
        }

        private void LoadConfigToUI()
        {
            _isSyncing = true;
            var config = IsEditingGlobal ? BarrageSettings.GetGlobalConfigDto() : (GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto());

            var currentSource = config.FontFamilyName;
            SelectedFontItem = AvailableFonts.FirstOrDefault(f => f.Family.Source.Equals(currentSource, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableFonts.FirstOrDefault(f => f.Family.Source.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase))
                               ?? AvailableFonts.FirstOrDefault();

            TextColorHex = config.TextColorHex;
            BackgroundColorHex = config.BackgroundColorHex;
            FontSize = config.FontSize;
            LetterSpacing = config.LetterSpacing;
            MaxTextLength = config.MaxTextLength;
            TextOpacityPercentage = config.TextOpacity * 100;
            BackgroundOpacityPercentage = config.BackgroundOpacity * 100;
            
            ShowTextStroke = config.ShowTextStroke;
            TextStrokeThickness = config.TextStrokeThickness;
            TextStrokeColorHex = config.TextStrokeColorHex;
            
            ShowAppIcon = config.ShowAppIcon;
            ShowAppName = config.ShowAppName;
            HighlightEllipsis = config.HighlightEllipsis;
            ShowBackgroundImage = config.ShowBackgroundImage;
            BackgroundImagePath = config.BackgroundImagePath;
            BackgroundImageAnchor = config.BackgroundImageAnchor;
            BackgroundImageOffsetX = config.BackgroundImageOffsetX;
            BackgroundImageOffsetY = config.BackgroundImageOffsetY;
            BackgroundImageScale = config.BackgroundImageScale;
            BackgroundImageOpacity = config.BackgroundImageOpacity;

            ScrollSpeedCharsPerSec = config.ScrollSpeedCharsPerSec;
            TrackStrategy = config.TrackStrategy;
            
            IsFontWeightBold = config.FontWeight == "Bold";
            IsFontStyleItalic = config.FontStyle == "Italic";
            IsUnderline = config.IsUnderlined;

            OnPropertyChanged(nameof(FontSizeDisplay));
            OnPropertyChanged(nameof(LetterSpacingDisplay));
            OnPropertyChanged(nameof(MaxTextLengthDisplay));
            OnPropertyChanged(nameof(TextOpacityDisplay));
            OnPropertyChanged(nameof(BackgroundOpacityDisplay));
            OnPropertyChanged(nameof(TextStrokeThicknessDisplay));
            OnPropertyChanged(nameof(BackgroundImageOffsetXDisplay));
            OnPropertyChanged(nameof(BackgroundImageOffsetYDisplay));
            OnPropertyChanged(nameof(BackgroundImageScaleDisplay));
            OnPropertyChanged(nameof(BackgroundImageOpacityDisplay));
            OnPropertyChanged(nameof(SpeedDisplay));

            OnPropertyChanged(nameof(IsTrackUpperCenter));
            OnPropertyChanged(nameof(IsTrackTopFirst));
            OnPropertyChanged(nameof(IsTrackBottomFirst));

            _isSyncing = false;
        }

        public IEnumerable<FontViewModel> AvailableFonts { get; }
        public ObservableCollection<ColorPaletteItem> PresetColors { get; }

        [ObservableProperty]
        private FontViewModel? _selectedFontItem;
        partial void OnSelectedFontItemChanged(FontViewModel? value)
        {
            if (_isSyncing || value == null) return;
            if (IsEditingGlobal) BarrageSettings.FontFamily = value.Family;
            else GetTargetConfig(true).FontFamilyName = value.Family.Source;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _textColorHex;
        partial void OnTextColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            else GetTargetConfig(true).TextColorHex = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _backgroundColorHex;
        partial void OnBackgroundColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            else GetTargetConfig(true).BackgroundColorHex = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FontSizeDisplay))]
        private double _fontSize;
        partial void OnFontSizeChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.FontSize = value;
            else GetTargetConfig(true).FontSize = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(FontSizeDisplay));
        }
        public string FontSizeDisplay => $"{FontSize:0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LetterSpacingDisplay))]
        private double _letterSpacing;
        partial void OnLetterSpacingChanged(double value)
        {
            if (_isSyncing) return;
            if (value < 0 || value > 20) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.LetterSpacing : GetTargetConfig(true).LetterSpacing;
                if (oldVal is int && typeof(double) == typeof(double)) oldVal = (double)(int)oldVal;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => LetterSpacing = (double)oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.LetterSpacing = value;
            else GetTargetConfig(true).LetterSpacing = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(LetterSpacingDisplay));
        }
        public string LetterSpacingDisplay => $"{LetterSpacing:0.0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxTextLengthDisplay))]
        private int _maxTextLength;
        partial void OnMaxTextLengthChanged(int value)
        {
            if (_isSyncing) return;
            if (value < 10 || value > 100) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.MaxTextLength : GetTargetConfig(true).MaxTextLength;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => MaxTextLength = (int)oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.MaxTextLength = value;
            else GetTargetConfig(true).MaxTextLength = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(MaxTextLengthDisplay));
        }
        public string MaxTextLengthDisplay => $"{MaxTextLength}";

        [ObservableProperty]
        private double _textOpacityPercentage;
        partial void OnTextOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.TextOpacity = value / 100.0;
            else GetTargetConfig(true).TextOpacity = value / 100.0;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(TextOpacityDisplay));
        }
        public string TextOpacityDisplay => $"{TextOpacityPercentage:0}%";

        [ObservableProperty]
        private double _backgroundOpacityPercentage;
        partial void OnBackgroundOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundOpacity = value / 100.0;
            else GetTargetConfig(true).BackgroundOpacity = value / 100.0;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundOpacityDisplay));
        }
        public string BackgroundOpacityDisplay => $"{BackgroundOpacityPercentage:0}%";

        [ObservableProperty]
        private bool _showTextStroke;
        partial void OnShowTextStrokeChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ShowTextStroke = value;
            else GetTargetConfig(true).ShowTextStroke = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextStrokeThicknessDisplay))]
        private double _textStrokeThickness;
        partial void OnTextStrokeThicknessChanged(double value)
        {
            if (_isSyncing) return;
            if (value < 0.1 || value > 5) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.TextStrokeThickness : GetTargetConfig(true).TextStrokeThickness;
                if (oldVal is int && typeof(double) == typeof(double)) oldVal = (double)(int)oldVal;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => TextStrokeThickness = (double)oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.TextStrokeThickness = value;
            else GetTargetConfig(true).TextStrokeThickness = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(TextStrokeThicknessDisplay));
        }
        public string TextStrokeThicknessDisplay => $"{TextStrokeThickness:0.0}px";

        [ObservableProperty]
        private string _textStrokeColorHex;
        partial void OnTextStrokeColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.TextStrokeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            else GetTargetConfig(true).TextStrokeColorHex = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _showAppIcon;
        partial void OnShowAppIconChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ShowAppIcon = value;
            else GetTargetConfig(true).ShowAppIcon = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _showAppName;
        partial void OnShowAppNameChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ShowAppName = value;
            else GetTargetConfig(true).ShowAppName = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _highlightEllipsis;
        partial void OnHighlightEllipsisChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.HighlightEllipsis = value;
            else GetTargetConfig(true).HighlightEllipsis = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _showBackgroundImage;
        partial void OnShowBackgroundImageChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ShowBackgroundImage = value;
            else GetTargetConfig(true).ShowBackgroundImage = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _backgroundImagePath;
        partial void OnBackgroundImagePathChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImagePath = value;
            else GetTargetConfig(true).BackgroundImagePath = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private ImageAnchor _backgroundImageAnchor;
        partial void OnBackgroundImageAnchorChanged(ImageAnchor value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImageAnchor = value;
            else GetTargetConfig(true).BackgroundImageAnchor = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BackgroundImageOffsetXDisplay))]
        private double _backgroundImageOffsetX;
        partial void OnBackgroundImageOffsetXChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImageOffsetX = value;
            else GetTargetConfig(true).BackgroundImageOffsetX = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundImageOffsetXDisplay));
        }
        public string BackgroundImageOffsetXDisplay => $"{BackgroundImageOffsetX:0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BackgroundImageOffsetYDisplay))]
        private double _backgroundImageOffsetY;
        partial void OnBackgroundImageOffsetYChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImageOffsetY = value;
            else GetTargetConfig(true).BackgroundImageOffsetY = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundImageOffsetYDisplay));
        }
        public string BackgroundImageOffsetYDisplay => $"{BackgroundImageOffsetY:0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BackgroundImageScaleDisplay))]
        private double _backgroundImageScale;
        partial void OnBackgroundImageScaleChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImageScale = value;
            else GetTargetConfig(true).BackgroundImageScale = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundImageScaleDisplay));
        }
        public string BackgroundImageScaleDisplay => $"{BackgroundImageScale:0.00}x";

        [ObservableProperty]
        private double _backgroundImageOpacity;
        partial void OnBackgroundImageOpacityChanged(double value)
        {
            if (_isSyncing) return;
            if (value < 0 || value > 1) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.BackgroundImageOpacity : GetTargetConfig(true).BackgroundImageOpacity;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => BackgroundImageOpacity = oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.BackgroundImageOpacity = value;
            else GetTargetConfig(true).BackgroundImageOpacity = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundImageOpacityDisplay));
            OnPropertyChanged(nameof(BackgroundImageOpacityPercentage));
        }

        public string BackgroundImageOpacityDisplay => $"{BackgroundImageOpacity * 100:0}%";
        public double BackgroundImageOpacityPercentage
        {
            get => BackgroundImageOpacity * 100;
            set => BackgroundImageOpacity = value / 100.0;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SpeedDisplay))]
        private double _scrollSpeedCharsPerSec;
        partial void OnScrollSpeedCharsPerSecChanged(double value)
        {
            if (_isSyncing) return;
            if (value < 5 || value > 30) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.ScrollSpeedCharsPerSec : GetTargetConfig(true).ScrollSpeedCharsPerSec;
                if (oldVal is int && typeof(double) == typeof(double)) oldVal = (double)(int)oldVal;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => ScrollSpeedCharsPerSec = (double)oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.ScrollSpeedCharsPerSec = value;
            else GetTargetConfig(true).ScrollSpeedCharsPerSec = value;
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
        [NotifyPropertyChangedFor(nameof(IsTrackUpperCenter))]
        [NotifyPropertyChangedFor(nameof(IsTrackTopFirst))]
        [NotifyPropertyChangedFor(nameof(IsTrackBottomFirst))]
        private string _trackStrategy;
        partial void OnTrackStrategyChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.TrackStrategy = value;
            else GetTargetConfig(true).TrackStrategy = value;
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
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.FontWeight = value ? FontWeights.Bold : FontWeights.Normal;
            else GetTargetConfig(true).FontWeight = value ? "Bold" : "Normal";
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _isFontStyleItalic;
        partial void OnIsFontStyleItalicChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.FontStyle = value ? FontStyles.Italic : FontStyles.Normal;
            else GetTargetConfig(true).FontStyle = value ? "Italic" : "Normal";
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _isUnderline;
        partial void OnIsUnderlineChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.IsUnderlined = value;
            else GetTargetConfig(true).IsUnderlined = value;
            TriggerSaveAndPreview();
        }

        // Global only properties
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
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (Application.Current is App app) app.ApplyCaptureSetting();
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
            (App.Current as App)?.TrayIconService?.ReRegisterHotKey();
            BarrageSettings.ExportConfig();
        }

        public string GetHotKeyString(uint modifiers, uint key)
        {
            var parts = new List<string>();
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
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

        [RelayCommand]
        private void SetTrackStrategy(string strategy) => TrackStrategy = strategy;

        [RelayCommand]
        private void SetUpdateSource(string source) => UpdateSource = source;

        private void TriggerSaveAndPreview()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }
}
