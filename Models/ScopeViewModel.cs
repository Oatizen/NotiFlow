using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotiFlow.Models
{
    public partial class ScopeItemViewModel : ObservableObject
    {
        public string DisplayName { get; set; } = "";
        public string Identifier { get; set; } = "";
        public string Detail => Identifier; // 显示在下方的辅助信息

        [ObservableProperty]
        private ImageSource? _icon;

        [ObservableProperty]
        private bool _isExpanded;

        public ObservableCollection<string> RecentMessages { get; } = new();

        [RelayCommand]
        private void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }
    }

    public partial class ScopeViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isSourceTabActive;

        [ObservableProperty]
        private string _currentMode = "Disabled";

        // 上方列表：当前启用的规则
        public ObservableCollection<ScopeItemViewModel> RulesList { get; } = new();

        // 下方列表：可供添加的项目
        public ObservableCollection<ScopeItemViewModel> AvailableList { get; } = new();

        private bool _initialized;

        public ScopeViewModel()
        {
            // 构造函数中不做任何重活，避免在 XAML 解析期间触发 P/Invoke 导致崩溃
            CurrentMode = BarrageSettings.SceneFilterMode;
        }

        /// <summary>
        /// 在页面 Loaded 事件后由 Code-Behind 调用，安全地触发首次数据加载。
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            LoadDataForCurrentTab();
        }

        partial void OnIsSourceTabActiveChanged(bool value)
        {
            if (_initialized)
                LoadDataForCurrentTab();
        }

        private void LoadDataForCurrentTab()
        {
            CurrentMode = IsSourceTabActive ? BarrageSettings.SourceFilterMode : BarrageSettings.SceneFilterMode;
            
            RulesList.Clear();
            AvailableList.Clear();

            // 异步加载所有数据（包括规则列表的图标和下方可用列表）
            _ = LoadAllDataAsync();
        }

        /// <summary>
        /// 统一的数据加载流程：
        /// 1. 后台枚举进程，建立"进程名→exe路径"的缓存字典
        /// 2. 加载规则列表，并利用缓存为规则项提取图标
        /// 3. 加载下方可用列表（排除已在规则中的），并提取图标
        /// </summary>
        private async Task LoadAllDataAsync()
        {
            try
            {
                // 后台枚举进程，同时得到路径缓存
                var processes = await Task.Run(() => Services.ProcessEnumerator.EnumerateWindowProcesses());
                var pathCache = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proc in processes)
                {
                    if (!string.IsNullOrEmpty(proc.ExecutablePath) && !pathCache.ContainsKey(proc.ProcessName))
                        pathCache[proc.ProcessName] = proc.ExecutablePath;
                }

                // 获取当前应该绑定的规则列表
                var currentRules = GetCurrentTargetList();
                
                foreach (var rule in currentRules)
                {
                    var vm = new ScopeItemViewModel
                    {
                        DisplayName = string.IsNullOrEmpty(rule.DisplayName) ? rule.Identifier : rule.DisplayName,
                        Identifier = rule.Identifier
                    };
                    RulesList.Add(vm);

                    // 优先尝试直接从缓存获取，如果没有再利用路径提取
                    pathCache.TryGetValue(rule.Identifier, out string? exePath);
                    _ = ExtractIconAsync(vm, exePath);
                }

                if (!IsSourceTabActive)
                {
                    // 生效场景：显示正在运行的进程（排除已在规则中的）
                    foreach (var proc in processes)
                    {
                        if (RulesList.Any(r => r.Identifier.Equals(proc.ProcessName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var vm = new ScopeItemViewModel
                        {
                            DisplayName = proc.MainWindowTitle,
                            Identifier = proc.ProcessName
                        };
                        AvailableList.Add(vm);
                        _ = ExtractIconAsync(vm, proc.ExecutablePath);
                    }
                }
                else
                {
                    // 通知来源：加载近期通知来源应用
                    var sources = Services.NotificationService.Instance?.RecentSources;
                    if (sources != null)
                    {
                        foreach (var source in sources)
                        {
                            // 过滤掉已经在规则列表中的
                            if (RulesList.Any(r => r.Identifier.Equals(source.Identifier, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var vm = new ScopeItemViewModel
                            {
                                DisplayName = string.IsNullOrEmpty(source.DisplayName) ? source.Identifier : source.DisplayName,
                                Identifier = source.Identifier
                            };
                            
                            if (source.RecentMessages != null)
                            {
                                foreach (var msg in source.RecentMessages)
                                {
                                    vm.RecentMessages.Add(msg);
                                }
                            }
                            
                            AvailableList.Add(vm);
                            
                            // 通知来源应用通常已在收到通知时缓存了图标，这里大概率能直接命中缓存
                            pathCache.TryGetValue(source.Identifier, out string? exePath);
                            _ = ExtractIconAsync(vm, exePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScopeViewModel] 加载数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 优先从本地缓存加载图标。如果缓存未命中且提供了 exePath，则从 exe 提取并存入缓存。
        /// </summary>
        private async Task ExtractIconAsync(ScopeItemViewModel vm, string? exePath)
        {
            try
            {
                // 1. 尝试从本地缓存加载
                var cachedIcon = await Services.IconCacheService.GetIconAsync(vm.Identifier);
                if (cachedIcon != null)
                {
                    vm.Icon = cachedIcon;
                    return;
                }

                // 2. 缓存未命中，且没有运行中的 exe 路径，则只能使用占位符
                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath)) return;

                // 3. 从 exe 提取图标
                var iconHandle = await Task.Run(() =>
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    return icon?.Handle ?? IntPtr.Zero;
                });

                if (iconHandle == IntPtr.Zero) return;

                // 4. 转为 WPF 图片并写入缓存
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            iconHandle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bitmap.Freeze();
                        vm.Icon = bitmap;

                        // 异步将提取的图标写入缓存
                        _ = Services.IconCacheService.CacheIconAsync(vm.Identifier, bitmap);
                    }
                    catch { }
                });
            }
            catch
            {
                // 图标提取失败则保留默认占位图标
            }
        }

        [RelayCommand]
        private void SwitchToScene() 
        {
            IsSourceTabActive = false;
        }

        [RelayCommand]
        private void SwitchToSource() 
        {
            IsSourceTabActive = true;
        }

        [RelayCommand]
        private void SetMode(string mode)
        {
            CurrentMode = mode;
            if (IsSourceTabActive)
                BarrageSettings.SourceFilterMode = mode;
            else
                BarrageSettings.SceneFilterMode = mode;
                
            BarrageSettings.ExportConfig();
            
            // 模式改变意味着需要切换绑定的黑白名单，触发数据刷新
            if (_initialized)
                LoadDataForCurrentTab();
        }

        [RelayCommand]
        private void AddRule(ScopeItemViewModel item)
        {
            if (item == null) return;
            
            // 从可用列表中移除
            AvailableList.Remove(item);
            
            // 添加到规则列表
            RulesList.Add(item);
            SaveRules();
        }

        [RelayCommand]
        private void RemoveRule(ScopeItemViewModel item)
        {
            if (item == null) return;
            
            // 从规则列表移除
            RulesList.Remove(item);
            SaveRules();
            
            // 直接将被移除的项放回下方可用列表（保留图标，无需重新枚举）
            if (!IsSourceTabActive)
                AvailableList.Insert(0, item);
        }
        
        public void AddManualRule(string identifier, string displayName)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return;
            
            if (!RulesList.Any(r => r.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
            {
                RulesList.Add(new ScopeItemViewModel
                {
                    Identifier = identifier,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? identifier : displayName
                });
                SaveRules();
                
                // 从可用列表中移除匹配项（如果有的话）
                var match = AvailableList.FirstOrDefault(a => a.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                if (match != null) AvailableList.Remove(match);
            }
        }

        private void SaveRules()
        {
            var dtos = RulesList.Select(vm => new ScopeRuleItemDto
            {
                Identifier = vm.Identifier,
                DisplayName = vm.DisplayName
            }).ToList();

            if (IsSourceTabActive)
            {
                if (CurrentMode == "Whitelist")
                    BarrageSettings.SourceWhitelist = dtos;
                else
                    BarrageSettings.SourceBlacklist = dtos;
            }
            else
            {
                if (CurrentMode == "Whitelist")
                    BarrageSettings.SceneWhitelist = dtos;
                else
                    BarrageSettings.SceneBlacklist = dtos;
            }
                
            BarrageSettings.ExportConfig();
        }

        private List<ScopeRuleItemDto> GetCurrentTargetList()
        {
            if (IsSourceTabActive)
            {
                return CurrentMode == "Whitelist" ? BarrageSettings.SourceWhitelist : BarrageSettings.SourceBlacklist;
            }
            else
            {
                return CurrentMode == "Whitelist" ? BarrageSettings.SceneWhitelist : BarrageSettings.SceneBlacklist;
            }
        }

        [RelayCommand]
        private void CopyIdentifier(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                Clipboard.SetText(id);
            }
        }
    }
}
