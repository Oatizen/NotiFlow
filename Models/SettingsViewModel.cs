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
            TriggerSaveAndPreview();
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

        public BarrageConfigDto GetCurrentConfig()
        {
            if (IsEditingGlobal)
            {
                return BarrageSettings.GetGlobalConfigDto();
            }
            return GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto();
        }

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
            _multiMonitorMode = BarrageSettings.MultiMonitorMode;

            InitializeCharacterPresets();
            LoadMonitors();
            Services.ScreenService.DisplaySettingsChanged += () =>
            {
                Application.Current?.Dispatcher?.Invoke(LoadMonitors);
            };

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
            BackgroundImageEdgeBlur = config.BackgroundImageEdgeBlur;
            BackgroundImageKeepBaseColor = config.BackgroundImageKeepBaseColor;

            ScrollSpeedCharsPerSec = config.ScrollSpeedCharsPerSec;
            TrackStrategy = config.TrackStrategy;
            
            IsFontWeightBold = config.FontWeight == "Bold";
            IsFontStyleItalic = config.FontStyle == "Italic";
            IsUnderline = config.IsUnderlined;

            // --- AppName Local Config (populate with global values if not overridden) ---
            AppNameTextColorHex = config.AppNameTextColorHex;
            AppNameFontFamilyName = !string.IsNullOrEmpty(config.AppNameFontFamilyName) ? config.AppNameFontFamilyName : config.FontFamilyName;
            AppNameFontSize = config.AppNameFontSize > 0 ? config.AppNameFontSize : config.FontSize;
            AppNameLetterSpacing = config.AppNameLetterSpacing > 0 ? config.AppNameLetterSpacing : config.LetterSpacing;
            AppNameFontWeight = !string.IsNullOrEmpty(config.AppNameFontWeight) ? config.AppNameFontWeight : config.FontWeight;
            AppNameFontStyle = !string.IsNullOrEmpty(config.AppNameFontStyle) ? config.AppNameFontStyle : config.FontStyle;
            AppNameTextOpacityPercentage = config.AppNameTextOpacity.HasValue ? config.AppNameTextOpacity.Value * 100.0 : config.TextOpacity * 100.0;
            AppNameIsFontStyleItalic = AppNameFontStyle == "Italic";
            AppNameIsUnderlined = config.AppNameIsUnderlined;
            AppNameShowTextStroke = config.AppNameShowTextStroke ?? config.ShowTextStroke;

            // --- Content Local Config ---
            ContentTextColorHex = config.ContentTextColorHex;
            ContentFontFamilyName = !string.IsNullOrEmpty(config.ContentFontFamilyName) ? config.ContentFontFamilyName : config.FontFamilyName;
            ContentFontSize = config.ContentFontSize > 0 ? config.ContentFontSize : config.FontSize;
            ContentLetterSpacing = config.ContentLetterSpacing > 0 ? config.ContentLetterSpacing : config.LetterSpacing;
            ContentFontWeight = !string.IsNullOrEmpty(config.ContentFontWeight) ? config.ContentFontWeight : config.FontWeight;
            ContentFontStyle = !string.IsNullOrEmpty(config.ContentFontStyle) ? config.ContentFontStyle : config.FontStyle;
            ContentTextOpacityPercentage = config.ContentTextOpacity.HasValue ? config.ContentTextOpacity.Value * 100.0 : config.TextOpacity * 100.0;
            ContentIsFontStyleItalic = ContentFontStyle == "Italic";
            ContentIsUnderlined = config.ContentIsUnderlined;
            ContentShowTextStroke = config.ContentShowTextStroke ?? config.ShowTextStroke;
            
            // --- Ellipsis Local Config ---
            EllipsisColorHex = config.EllipsisColorHex;
            EllipsisFontSize = config.EllipsisFontSize > 0 ? config.EllipsisFontSize : config.FontSize;
            EllipsisTextOpacityPercentage = config.EllipsisTextOpacity.HasValue ? config.EllipsisTextOpacity.Value * 100.0 : config.TextOpacity * 100.0;
            EllipsisIsUnderlined = config.EllipsisIsUnderlined;

            // --- AppIcon Local Config ---
            AppIconScale = config.AppIconScale > 0 ? config.AppIconScale : 1.0;

            // --- 角色伴随挂件配置 ---
            ShowCharacterWidget = config.ShowCharacterWidget;
            CharacterWidgetPresetId = config.CharacterWidgetPresetId ?? (config.ShowCharacterWidget ? "preset_1" : "none");
            CharacterWidgetPath = config.CharacterWidgetPath;
            CharacterWidgetScale = config.CharacterWidgetScale <= 0 ? 1.0 : config.CharacterWidgetScale;
            CharacterWidgetScalePercentage = CharacterWidgetScale * 100.0;
            CharacterWidgetOffsetX = config.CharacterWidgetOffsetX;
            CharacterWidgetOffsetY = config.CharacterWidgetOffsetY;
            CharacterWidgetOpacityPercentage = (config.CharacterWidgetOpacity <= 0 ? 1.0 : config.CharacterWidgetOpacity) * 100.0;
            UpdateCharacterPresetSelection();

            OnPropertyChanged(nameof(FontSizeDisplay));
            OnPropertyChanged(nameof(LetterSpacingDisplay));
            OnPropertyChanged(nameof(MaxTextLengthDisplay));
            OnPropertyChanged(nameof(TextOpacityDisplay));
            OnPropertyChanged(nameof(BackgroundOpacityDisplay));
            OnPropertyChanged(nameof(TextStrokeThicknessDisplay));
            OnPropertyChanged(nameof(TextColorBrush));
            OnPropertyChanged(nameof(AppNameTextColorBrush));
            OnPropertyChanged(nameof(ContentTextColorBrush));
            OnPropertyChanged(nameof(EllipsisColorBrush));
            OnPropertyChanged(nameof(BackgroundColorBrush));
            OnPropertyChanged(nameof(TextStrokeColorBrush));
            OnPropertyChanged(nameof(BackgroundImageOffsetXDisplay));
            OnPropertyChanged(nameof(BackgroundImageOffsetYDisplay));
            OnPropertyChanged(nameof(BackgroundImageScaleDisplay));
            OnPropertyChanged(nameof(BackgroundImageOpacityDisplay));
            OnPropertyChanged(nameof(CharacterWidgetScaleDisplay));
            OnPropertyChanged(nameof(CharacterWidgetOpacityDisplay));
            OnPropertyChanged(nameof(CharacterWidgetOffsetXDisplay));
            OnPropertyChanged(nameof(CharacterWidgetOffsetYDisplay));
            OnPropertyChanged(nameof(SpeedDisplay));

            OnPropertyChanged(nameof(IsTrackUpperCenter));
            OnPropertyChanged(nameof(IsTrackTopFirst));
            OnPropertyChanged(nameof(IsTrackBottomFirst));
            OnPropertyChanged(nameof(HasAppNameCustomSettings));
            OnPropertyChanged(nameof(HasContentCustomSettings));
            OnPropertyChanged(nameof(HasEllipsisCustomSettings));
            OnPropertyChanged(nameof(HasAppIconCustomSettings));

            _isSyncing = false;
        }

        public IEnumerable<FontViewModel> AvailableFonts { get; }
        public ObservableCollection<ColorPaletteItem> PresetColors { get; }
        public Brush TextColorBrush
        {
            get
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(TextColorHex ?? "#FFFFFFFF")); }
                catch { return Brushes.White; }
            }
        }
        
        public Brush AppNameTextColorBrush
        {
            get
            {
                string hex = string.IsNullOrEmpty(AppNameTextColorHex) ? TextColorHex : AppNameTextColorHex;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFFFF")); }
                catch { return Brushes.White; }
            }
        }

        public Brush ContentTextColorBrush
        {
            get
            {
                string hex = string.IsNullOrEmpty(ContentTextColorHex) ? TextColorHex : ContentTextColorHex;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFFFF")); }
                catch { return Brushes.White; }
            }
        }

        public Brush BackgroundColorBrush
        {
            get
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(BackgroundColorHex ?? "#FF000000")); }
                catch { return Brushes.Black; }
            }
        }

                public Brush EllipsisColorBrush
        {
            get
            {
                string hex = string.IsNullOrEmpty(EllipsisColorHex) ? (string.IsNullOrEmpty(ContentTextColorHex) ? TextColorHex : ContentTextColorHex) : EllipsisColorHex;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFFFF")); }
                catch { return Brushes.White; }
            }
        }

        public Brush TextStrokeColorBrush
        {
            get
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(TextStrokeColorHex ?? "#FF000000")); }
                catch { return Brushes.Black; }
            }
        }


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
        [NotifyPropertyChangedFor(nameof(TextColorBrush))]
        [NotifyPropertyChangedFor(nameof(AppNameTextColorBrush))]
        [NotifyPropertyChangedFor(nameof(ContentTextColorBrush))]
        [NotifyPropertyChangedFor(nameof(EllipsisColorBrush))]
        private string _textColorHex;
        partial void OnTextColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.TextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            else GetTargetConfig(true).TextColorHex = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AppNameTextColorBrush))]
        


        private string _appNameTextColorHex;
        partial void OnAppNameTextColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameTextColorHex = value;
            else GetTargetConfig(true).AppNameTextColorHex = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private double _appNameFontSize;
        partial void OnAppNameFontSizeChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameFontSize = value;
            else GetTargetConfig(true).AppNameFontSize = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private string _appNameFontWeight;
        partial void OnAppNameFontWeightChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameFontWeight = value;
            else GetTargetConfig(true).AppNameFontWeight = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private string _appNameFontStyle;
        partial void OnAppNameFontStyleChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameFontStyle = value;
            else GetTargetConfig(true).AppNameFontStyle = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContentTextColorBrush))]
        [NotifyPropertyChangedFor(nameof(EllipsisColorBrush))]
        private string _contentTextColorHex;
        partial void OnContentTextColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentTextColorHex = value;
            else GetTargetConfig(true).ContentTextColorHex = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private double _contentFontSize;
        partial void OnContentFontSizeChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentFontSize = value;
            else GetTargetConfig(true).ContentFontSize = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private string _contentFontWeight;
        partial void OnContentFontWeightChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentFontWeight = value;
            else GetTargetConfig(true).ContentFontWeight = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private string _contentFontStyle;
        partial void OnContentFontStyleChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentFontStyle = value;
            else GetTargetConfig(true).ContentFontStyle = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private double _ellipsisFontSize;
        partial void OnEllipsisFontSizeChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.EllipsisFontSize = value;
            else GetTargetConfig(true).EllipsisFontSize = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        private double _appIconScale;
        partial void OnAppIconScaleChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppIconScale = value;
            else GetTargetConfig(true).AppIconScale = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BackgroundColorBrush))]
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
            OnPropertyChanged(nameof(TextColorBrush));
            OnPropertyChanged(nameof(BackgroundColorBrush));
            OnPropertyChanged(nameof(TextStrokeColorBrush));
        }
        public string TextStrokeThicknessDisplay => $"{TextStrokeThickness:0.0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextStrokeColorBrush))]
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
        [NotifyPropertyChangedFor(nameof(BackgroundImageEdgeBlurDisplay))]
        private double _backgroundImageEdgeBlur;
        partial void OnBackgroundImageEdgeBlurChanged(double value)
        {
            if (_isSyncing) return;
            if (value < 0 || value > 500) 
            { 
                var oldVal = IsEditingGlobal ? BarrageSettings.BackgroundImageEdgeBlur : GetTargetConfig(true).BackgroundImageEdgeBlur;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() => BackgroundImageEdgeBlur = oldVal));
                return; 
            }
            if (IsEditingGlobal) BarrageSettings.BackgroundImageEdgeBlur = value;
            else GetTargetConfig(true).BackgroundImageEdgeBlur = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(BackgroundImageEdgeBlurDisplay));
        }
        public string BackgroundImageEdgeBlurDisplay => $"{BackgroundImageEdgeBlur:0}px";

        [ObservableProperty]
        private bool _backgroundImageKeepBaseColor = true;
        partial void OnBackgroundImageKeepBaseColorChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.BackgroundImageKeepBaseColor = value;
            else GetTargetConfig(true).BackgroundImageKeepBaseColor = value;
            TriggerSaveAndPreview();
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
            OnPropertyChanged(nameof(HasAppNameCustomSettings));
            OnPropertyChanged(nameof(HasContentCustomSettings));
            OnPropertyChanged(nameof(HasEllipsisCustomSettings));
            OnPropertyChanged(nameof(HasAppIconCustomSettings));
        }

        public bool HasAppNameCustomSettings
        {
            get
            {
                var config = IsEditingGlobal ? BarrageSettings.GetGlobalConfigDto() : (GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto());
                return !string.IsNullOrEmpty(config.AppNameTextColorHex)
                    || config.AppNameFontSize > 0
                    || !string.IsNullOrEmpty(config.AppNameFontWeight)
                    || !string.IsNullOrEmpty(config.AppNameFontStyle)
                    || !string.IsNullOrEmpty(config.AppNameFontFamilyName)
                    || config.AppNameLetterSpacing > 0
                    || config.AppNameTextOpacity.HasValue
                    || config.AppNameShowTextStroke.HasValue
                    || config.AppNameIsUnderlined
                    || !config.ShowAppName;
            }
        }

        public bool HasContentCustomSettings
        {
            get
            {
                var config = IsEditingGlobal ? BarrageSettings.GetGlobalConfigDto() : (GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto());
                return !string.IsNullOrEmpty(config.ContentTextColorHex)
                    || config.ContentFontSize > 0
                    || !string.IsNullOrEmpty(config.ContentFontWeight)
                    || !string.IsNullOrEmpty(config.ContentFontStyle)
                    || !string.IsNullOrEmpty(config.ContentFontFamilyName)
                    || config.ContentLetterSpacing > 0
                    || config.ContentTextOpacity.HasValue
                    || config.ContentShowTextStroke.HasValue
                    || config.ContentIsUnderlined;
            }
        }

        public bool HasEllipsisCustomSettings
        {
            get
            {
                var config = IsEditingGlobal ? BarrageSettings.GetGlobalConfigDto() : (GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto());
                return !string.IsNullOrEmpty(config.EllipsisColorHex)
                    || config.EllipsisFontSize > 0
                    || config.EllipsisTextOpacity.HasValue
                    || config.EllipsisIsUnderlined;
            }
        }

        public bool HasAppIconCustomSettings
        {
            get
            {
                var config = IsEditingGlobal ? BarrageSettings.GetGlobalConfigDto() : (GetTargetConfig(false) ?? BarrageSettings.GetGlobalConfigDto());
                return Math.Abs(config.AppIconScale - 1.0) > 0.01 || !config.ShowAppIcon;
            }
        }

        [RelayCommand]
        private void ResetAppNameSettings()
        {
            if (IsEditingGlobal)
            {
                BarrageSettings.AppNameTextColorHex = "";
                BarrageSettings.AppNameFontSize = 0;
                BarrageSettings.AppNameFontWeight = "";
                BarrageSettings.AppNameFontStyle = "";
                BarrageSettings.AppNameFontFamilyName = "";
                BarrageSettings.AppNameLetterSpacing = 0;
                BarrageSettings.AppNameTextOpacity = null;
                BarrageSettings.AppNameIsUnderlined = false;
                BarrageSettings.AppNameShowTextStroke = null;
                BarrageSettings.ShowAppName = true;
            }
            else
            {
                var target = GetTargetConfig(true);
                target.AppNameTextColorHex = "";
                target.AppNameFontSize = 0;
                target.AppNameFontWeight = "";
                target.AppNameFontStyle = "";
                target.AppNameFontFamilyName = "";
                target.AppNameLetterSpacing = 0;
                target.AppNameTextOpacity = null;
                target.AppNameIsUnderlined = false;
                target.AppNameShowTextStroke = null;
                target.ShowAppName = true;
            }

            _isSyncing = true;
            AppNameTextColorHex = "";
            AppNameFontSize = FontSize;
            AppNameLetterSpacing = LetterSpacing;
            AppNameFontWeight = IsFontWeightBold ? "Bold" : "Normal";
            AppNameFontStyle = IsFontStyleItalic ? "Italic" : "Normal";
            AppNameFontFamilyName = SelectedFontItem?.Family.Source ?? "Microsoft YaHei";
            AppNameTextOpacityPercentage = TextOpacityPercentage;
            AppNameIsUnderlined = false;
            AppNameIsFontStyleItalic = IsFontStyleItalic;
            AppNameShowTextStroke = ShowTextStroke;
            ShowAppName = true;
            _isSyncing = false;

            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(AppNameTextColorBrush));
            OnPropertyChanged(nameof(HasAppNameCustomSettings));
        }

        [RelayCommand]
        private void ResetContentSettings()
        {
            if (IsEditingGlobal)
            {
                BarrageSettings.ContentTextColorHex = "";
                BarrageSettings.ContentFontSize = 0;
                BarrageSettings.ContentFontWeight = "";
                BarrageSettings.ContentFontStyle = "";
                BarrageSettings.ContentFontFamilyName = "";
                BarrageSettings.ContentLetterSpacing = 0;
                BarrageSettings.ContentTextOpacity = null;
                BarrageSettings.ContentIsUnderlined = false;
                BarrageSettings.ContentShowTextStroke = null;
            }
            else
            {
                var target = GetTargetConfig(true);
                target.ContentTextColorHex = "";
                target.ContentFontSize = 0;
                target.ContentFontWeight = "";
                target.ContentFontStyle = "";
                target.ContentFontFamilyName = "";
                target.ContentLetterSpacing = 0;
                target.ContentTextOpacity = null;
                target.ContentIsUnderlined = false;
                target.ContentShowTextStroke = null;
            }

            _isSyncing = true;
            ContentTextColorHex = "";
            ContentFontSize = FontSize;
            ContentLetterSpacing = LetterSpacing;
            ContentFontWeight = IsFontWeightBold ? "Bold" : "Normal";
            ContentFontStyle = IsFontStyleItalic ? "Italic" : "Normal";
            ContentFontFamilyName = SelectedFontItem?.Family.Source ?? "Microsoft YaHei";
            ContentTextOpacityPercentage = TextOpacityPercentage;
            ContentIsUnderlined = false;
            ContentIsFontStyleItalic = IsFontStyleItalic;
            ContentShowTextStroke = ShowTextStroke;
            _isSyncing = false;

            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(ContentTextColorBrush));
            OnPropertyChanged(nameof(HasContentCustomSettings));
        }

        [RelayCommand]
        private void ResetEllipsisSettings()
        {
            if (IsEditingGlobal)
            {
                BarrageSettings.EllipsisColorHex = "";
                BarrageSettings.EllipsisFontSize = 0;
                BarrageSettings.EllipsisTextOpacity = null;
                BarrageSettings.EllipsisIsUnderlined = false;
            }
            else
            {
                var target = GetTargetConfig(true);
                target.EllipsisColorHex = "";
                target.EllipsisFontSize = 0;
                target.EllipsisTextOpacity = null;
                target.EllipsisIsUnderlined = false;
            }

            _isSyncing = true;
            EllipsisColorHex = "";
            EllipsisFontSize = FontSize;
            EllipsisTextOpacityPercentage = TextOpacityPercentage;
            EllipsisIsUnderlined = false;
            _isSyncing = false;

            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(EllipsisColorBrush));
            OnPropertyChanged(nameof(HasEllipsisCustomSettings));
        }

        [RelayCommand]
        private void ResetAppIconSettings()
        {
            if (IsEditingGlobal)
            {
                BarrageSettings.AppIconScale = 1.0;
                BarrageSettings.ShowAppIcon = true;
            }
            else
            {
                var target = GetTargetConfig(true);
                target.AppIconScale = 1.0;
                target.ShowAppIcon = true;
            }

            _isSyncing = true;
            AppIconScale = 1.0;
            ShowAppIcon = true;
            _isSyncing = false;

            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(HasAppIconCustomSettings));
        }


        [ObservableProperty]
        private string _appNameFontFamilyName;
        partial void OnAppNameFontFamilyNameChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameFontFamilyName = value;
            else GetTargetConfig(true).AppNameFontFamilyName = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AppNameLetterSpacingDisplay))]
        private double _appNameLetterSpacing;
        partial void OnAppNameLetterSpacingChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameLetterSpacing = value;
            else GetTargetConfig(true).AppNameLetterSpacing = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(AppNameLetterSpacingDisplay));
        }
        public string AppNameLetterSpacingDisplay => $"{AppNameLetterSpacing:0.0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AppNameTextOpacityDisplay))]
        private double _appNameTextOpacityPercentage;
        partial void OnAppNameTextOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameTextOpacity = value / 100.0;
            else GetTargetConfig(true).AppNameTextOpacity = value / 100.0;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(AppNameTextOpacityDisplay));
        }
        
        [ObservableProperty] private bool _isAppNameColorPickerOpen;
        [ObservableProperty] private bool _isContentColorPickerOpen;
        [ObservableProperty] private bool _isEllipsisColorPickerOpen;
        
        [ObservableProperty] private bool _appNameIsUnderlined;
        partial void OnAppNameIsUnderlinedChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameIsUnderlined = value;
            else GetTargetConfig(true).AppNameIsUnderlined = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty] private bool _contentIsUnderlined;
        partial void OnContentIsUnderlinedChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentIsUnderlined = value;
            else GetTargetConfig(true).ContentIsUnderlined = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty] private bool _ellipsisIsUnderlined;
        partial void OnEllipsisIsUnderlinedChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.EllipsisIsUnderlined = value;
            else GetTargetConfig(true).EllipsisIsUnderlined = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private bool _contentShowTextStroke;
        partial void OnContentShowTextStrokeChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentShowTextStroke = value;
            else GetTargetConfig(true).ContentShowTextStroke = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _contentFontFamilyName;
        partial void OnContentFontFamilyNameChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentFontFamilyName = value;
            else GetTargetConfig(true).ContentFontFamilyName = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContentLetterSpacingDisplay))]
        private double _contentLetterSpacing;
        partial void OnContentLetterSpacingChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentLetterSpacing = value;
            else GetTargetConfig(true).ContentLetterSpacing = value;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(ContentLetterSpacingDisplay));
        }
        public string ContentLetterSpacingDisplay => $"{ContentLetterSpacing:0.0}px";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContentTextOpacityDisplay))]
        private double _contentTextOpacityPercentage;
        partial void OnContentTextOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ContentTextOpacity = value / 100.0;
            else GetTargetConfig(true).ContentTextOpacity = value / 100.0;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(ContentTextOpacityDisplay));
        }
        public string ContentTextOpacityDisplay => $"{ContentTextOpacityPercentage:0}%";

        [ObservableProperty]
        private bool _contentIsFontStyleItalic;
        partial void OnContentIsFontStyleItalicChanged(bool value)
        {
            if (_isSyncing) return;
            string style = value ? "Italic" : "Normal";
            ContentFontStyle = style;
        }

        [RelayCommand]
        private void SetContentFontWeight(string weight)
        {
            ContentFontWeight = weight;
            if (IsEditingGlobal) BarrageSettings.ContentFontWeight = weight;
            else GetTargetConfig(true).ContentFontWeight = weight;
            TriggerSaveAndPreview();
        }

        public string AppNameTextOpacityDisplay => $"{AppNameTextOpacityPercentage:0}%";

        [ObservableProperty]
        private bool _appNameShowTextStroke;
        partial void OnAppNameShowTextStrokeChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.AppNameShowTextStroke = value;
            else GetTargetConfig(true).AppNameShowTextStroke = value;
            TriggerSaveAndPreview();
        }
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EllipsisColorBrush))]
        private string _ellipsisColorHex;
        partial void OnEllipsisColorHexChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.EllipsisColorHex = value;
            else GetTargetConfig(true).EllipsisColorHex = value;
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EllipsisTextOpacityDisplay))]
        private double _ellipsisTextOpacityPercentage;
        partial void OnEllipsisTextOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.EllipsisTextOpacity = value / 100.0;
            else GetTargetConfig(true).EllipsisTextOpacity = value / 100.0;
            TriggerSaveAndPreview();
            OnPropertyChanged(nameof(EllipsisTextOpacityDisplay));
        }
        public string EllipsisTextOpacityDisplay => $"{EllipsisTextOpacityPercentage:0}%";

        [ObservableProperty]
        private bool _appNameIsFontStyleItalic;
        partial void OnAppNameIsFontStyleItalicChanged(bool value)
        {
            if (_isSyncing) return;
            string style = value ? "Italic" : "Normal";
            if (IsEditingGlobal) BarrageSettings.AppNameFontStyle = style;
            else GetTargetConfig(true).AppNameFontStyle = style;
            TriggerSaveAndPreview();
        }

        [RelayCommand]
        private void SetAppNameFontWeight(string weight)
        {
            AppNameFontWeight = weight;
            if (IsEditingGlobal) BarrageSettings.AppNameFontWeight = weight;
            else GetTargetConfig(true).AppNameFontWeight = weight;
            TriggerSaveAndPreview();
        }

        // ====== 多显示器设置支持 ======
        public ObservableCollection<MonitorSettingItemDto> MonitorList { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModeSimultaneous))]
        [NotifyPropertyChangedFor(nameof(IsModeSequential))]
        private string _multiMonitorMode = "Simultaneous";

        public bool IsModeSimultaneous => MultiMonitorMode == "Simultaneous";
        public bool IsModeSequential => MultiMonitorMode == "Sequential";

        public void LoadMonitors()
        {
            var list = Services.ScreenService.GetMergedMonitors(BarrageSettings.Monitors);
            MonitorList.Clear();
            foreach (var item in list)
            {
                // 监听每个条目的 IsEnabled 变更以便自动持久化
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MonitorSettingItemDto.IsEnabled))
                    {
                        SaveMonitors();
                    }
                };
                MonitorList.Add(item);
            }
            BarrageSettings.Monitors = list;
        }

        [RelayCommand]
        private void SetMultiMonitorMode(string mode)
        {
            MultiMonitorMode = mode;
            BarrageSettings.MultiMonitorMode = mode;
            TriggerSaveAndPreview();
        }

        [RelayCommand]
        private void ToggleMonitorEnabled(MonitorSettingItemDto? monitor)
        {
            if (monitor == null) return;
            monitor.IsEnabled = !monitor.IsEnabled;
            SaveMonitors();
        }

        [RelayCommand]
        private void MoveMonitorUp(MonitorSettingItemDto? monitor)
        {
            if (monitor == null) return;
            int index = MonitorList.IndexOf(monitor);
            if (index > 0)
            {
                MonitorList.Move(index, index - 1);
                UpdateMonitorOrderNumbers();
                SaveMonitors();
            }
        }

        [RelayCommand]
        private void MoveMonitorDown(MonitorSettingItemDto? monitor)
        {
            if (monitor == null) return;
            int index = MonitorList.IndexOf(monitor);
            if (index >= 0 && index < MonitorList.Count - 1)
            {
                MonitorList.Move(index, index + 1);
                UpdateMonitorOrderNumbers();
                SaveMonitors();
            }
        }

        public void ReorderMonitors(int oldIndex, int newIndex)
        {
            if (oldIndex >= 0 && oldIndex < MonitorList.Count && newIndex >= 0 && newIndex < MonitorList.Count && oldIndex != newIndex)
            {
                MonitorList.Move(oldIndex, newIndex);
                UpdateMonitorOrderNumbers();
                SaveMonitors();
            }
        }

        private void UpdateMonitorOrderNumbers()
        {
            for (int i = 0; i < MonitorList.Count; i++)
            {
                MonitorList[i].DisplayOrder = i + 1;
            }
        }

        private void SaveMonitors()
        {
            BarrageSettings.Monitors = MonitorList.ToList();
            TriggerSaveAndPreview();
        }

        // ====== 角色伴随挂件设置支持 ======
        public ObservableCollection<CharacterPresetItemDto> CharacterPresets { get; } = new();

        public void InitializeCharacterPresets()
        {
            CharacterPresets.Clear();

            // 1. 自定义上传（左边第 1 位）
            CharacterPresets.Add(new CharacterPresetItemDto
            {
                Id = "custom",
                Name = "自定义上传",
                ImagePath = CharacterWidgetPresetId == "custom" ? CharacterWidgetPath : "",
                IsSelected = CharacterWidgetPresetId == "custom"
            });

            // 2. 预设角色图片（右边第 2 位）
            string preset1Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Characters", "Preset1_Pajing.png");
            if (!System.IO.File.Exists(preset1Path) && System.IO.File.Exists(@"E:\PhotoShop成品\组件1.png"))
            {
                preset1Path = @"E:\PhotoShop成品\组件1.png";
            }

            CharacterPresets.Add(new CharacterPresetItemDto
            {
                Id = "preset_1",
                Name = "预设",
                ImagePath = preset1Path,
                IsSelected = CharacterWidgetPresetId == "preset_1" || (string.IsNullOrEmpty(CharacterWidgetPresetId) && !string.IsNullOrEmpty(preset1Path))
            });
        }

        private void UpdateCharacterPresetSelection()
        {
            foreach (var p in CharacterPresets)
            {
                p.IsSelected = (p.Id == CharacterWidgetPresetId);
                if (p.Id == "custom" && !string.IsNullOrEmpty(CharacterWidgetPath))
                {
                    p.ImagePath = CharacterWidgetPath;
                }
            }
        }

        [ObservableProperty]
        private bool _showCharacterWidget;
        partial void OnShowCharacterWidgetChanged(bool value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.ShowCharacterWidget = value;
            else GetTargetConfig(true).ShowCharacterWidget = value;
            UpdateCharacterPresetSelection();
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _characterWidgetPresetId = "none";
        partial void OnCharacterWidgetPresetIdChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetPresetId = value;
            else GetTargetConfig(true).CharacterWidgetPresetId = value;
            UpdateCharacterPresetSelection();
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private string _characterWidgetPath = "";
        partial void OnCharacterWidgetPathChanged(string value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetPath = value;
            else GetTargetConfig(true).CharacterWidgetPath = value;
            UpdateCharacterPresetSelection();
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private double _characterWidgetScale = 1.0;
        partial void OnCharacterWidgetScaleChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetScale = value;
            else GetTargetConfig(true).CharacterWidgetScale = value;
            _characterWidgetScalePercentage = value * 100.0;
            OnPropertyChanged(nameof(CharacterWidgetScalePercentage));
            OnPropertyChanged(nameof(CharacterWidgetScaleDisplay));
            TriggerSaveAndPreview();
        }

        [ObservableProperty]
        private double _characterWidgetScalePercentage = 100.0;
        partial void OnCharacterWidgetScalePercentageChanged(double value)
        {
            if (_isSyncing) return;
            _characterWidgetScale = value / 100.0;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetScale = _characterWidgetScale;
            else GetTargetConfig(true).CharacterWidgetScale = _characterWidgetScale;
            OnPropertyChanged(nameof(CharacterWidgetScale));
            OnPropertyChanged(nameof(CharacterWidgetScaleDisplay));
            TriggerSaveAndPreview();
        }
        public string CharacterWidgetScaleDisplay => $"{CharacterWidgetScalePercentage:0}%";

        [ObservableProperty]
        private double _characterWidgetOffsetX = -15.0;
        partial void OnCharacterWidgetOffsetXChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetOffsetX = value;
            else GetTargetConfig(true).CharacterWidgetOffsetX = value;
            OnPropertyChanged(nameof(CharacterWidgetOffsetXDisplay));
            TriggerSaveAndPreview();
        }
        public string CharacterWidgetOffsetXDisplay => $"{CharacterWidgetOffsetX:0}px";

        [ObservableProperty]
        private double _characterWidgetOffsetY = -20.0;
        partial void OnCharacterWidgetOffsetYChanged(double value)
        {
            if (_isSyncing) return;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetOffsetY = value;
            else GetTargetConfig(true).CharacterWidgetOffsetY = value;
            OnPropertyChanged(nameof(CharacterWidgetOffsetYDisplay));
            TriggerSaveAndPreview();
        }
        public string CharacterWidgetOffsetYDisplay => $"{CharacterWidgetOffsetY:0}px";

        [ObservableProperty]
        private double _characterWidgetOpacityPercentage = 100.0;
        partial void OnCharacterWidgetOpacityPercentageChanged(double value)
        {
            if (_isSyncing) return;
            double opacity = value / 100.0;
            if (IsEditingGlobal) BarrageSettings.CharacterWidgetOpacity = opacity;
            else GetTargetConfig(true).CharacterWidgetOpacity = opacity;
            OnPropertyChanged(nameof(CharacterWidgetOpacityDisplay));
            TriggerSaveAndPreview();
        }
        public string CharacterWidgetOpacityDisplay => $"{CharacterWidgetOpacityPercentage:0}%";

        [RelayCommand]
        private void SelectCharacterPreset(string presetId)
        {
            if (presetId == "custom")
            {
                CharacterWidgetPresetId = "custom";
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var editor = new NotiFlow.Views.Windows.CharacterWidgetEditorWindow();
                    editor.ShowDialog();

                    CharacterWidgetPath = BarrageSettings.CharacterWidgetPath;
                    CharacterWidgetScale = BarrageSettings.CharacterWidgetScale;
                    CharacterWidgetOffsetX = BarrageSettings.CharacterWidgetOffsetX;
                    CharacterWidgetOffsetY = BarrageSettings.CharacterWidgetOffsetY;
                    ShowCharacterWidget = BarrageSettings.ShowCharacterWidget;
                    CharacterWidgetPresetId = "custom";
                    InitializeCharacterPresets();
                    TriggerSaveAndPreview();
                });
            }
            else if (presetId == "preset_1")
            {
                CharacterWidgetPresetId = "preset_1";
                string preset1Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Characters", "Preset1_Pajing.png");
                if (!System.IO.File.Exists(preset1Path) && System.IO.File.Exists(@"E:\PhotoShop成品\组件1.png"))
                {
                    preset1Path = @"E:\PhotoShop成品\组件1.png";
                }
                CharacterWidgetPath = preset1Path;
                CharacterWidgetScale = 1.0;
                CharacterWidgetOffsetX = 0;
                CharacterWidgetOffsetY = 0;
                if (IsEditingGlobal)
                {
                    BarrageSettings.CharacterWidgetPath = CharacterWidgetPath;
                    BarrageSettings.CharacterWidgetPresetId = "preset_1";
                    BarrageSettings.CharacterWidgetScale = 1.0;
                    BarrageSettings.CharacterWidgetOffsetX = 0;
                    BarrageSettings.CharacterWidgetOffsetY = 0;
                }
                InitializeCharacterPresets();
                TriggerSaveAndPreview();
            }
        }

        [RelayCommand]
        private void BrowseCustomCharacterImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择角色挂件图片",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                CharacterWidgetPath = dialog.FileName;
                ShowCharacterWidget = true;
                CharacterWidgetPresetId = "custom";
            }
        }

        [RelayCommand]
        private void ResetCharacterWidgetTransform()
        {
            CharacterWidgetScalePercentage = 100;
            CharacterWidgetOffsetX = -15;
            CharacterWidgetOffsetY = -20;
            CharacterWidgetOpacityPercentage = 100;
        }
    }
}
