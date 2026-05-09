using System.Windows;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using NotiFlow.Services;

namespace NotiFlow
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private TrayIconService? _trayIconService;
        public TrayIconService? TrayIconService => _trayIconService;
        private MainWindow? _mainWindow;
        private SettingsWindow? _settingsWindow;
        private ForegroundMonitorService? _foregroundMonitorService;
        public ForegroundMonitorService? ForegroundMonitor => _foregroundMonitorService;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例运行检测
            const string appMutexName = "NotiFlow_SingleInstance_Mutex";
            _mutex = new Mutex(true, appMutexName, out bool createdNew);
            if (!createdNew)
            {
                // 如果是静默自启，就不弹窗打扰用户，直接退出
                if (!e.Args.Contains("--startup"))
                {
                    var dialog = new Views.Windows.SimpleDialogWindow(
                        "有另一个 NotiFlow 正在运行！",
                        "请检查系统任务栏托盘。");
                    dialog.ShowDialog();
                }
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // 切换为显式关闭模式：关闭所有窗口不会自动终结应用，由托盘"退出"控制生命周期
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 启动时自动尝试导入曾经落盘的配置文件（带安全回落防注入机制）
            BarrageSettings.ImportConfig();

            // 仅在启动时应用一次默认主题 (根据配置)
            if (BarrageSettings.Theme == "Dark")
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            else if (BarrageSettings.Theme == "System")
                ApplicationThemeManager.ApplySystemTheme();
            else
                ApplicationThemeManager.Apply(ApplicationTheme.Light);

            // 【关键】注册全局主题变更事件，确保任何途径触发的主题切换都不会污染弹幕窗口
            // WPF-UI 内部会通过 DWM API 遍历所有窗口强制设置深色模式属性，
            // 导致全透明 Topmost 弹幕窗口变为黑色不透明遮挡桌面，用户甚至无法操作导致只能重启。
            ApplicationThemeManager.Changed += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    foreach (Window window in Current.Windows)
                    {
                        if (window is MainWindow)
                        {
                            NativeMethods.ResetWindowTransparency(window);
                        }
                    }
                });
            };

            // 初始化系统托盘图标
            _trayIconService = new TrayIconService();

            // 初始化前台窗口监听服务（生效场景过滤）
            _foregroundMonitorService = new ForegroundMonitorService();
            _foregroundMonitorService.Start();

            // 根据自动启动设置决定是否立即显示主弹幕窗口
            if (BarrageSettings.IsWorking)
            {
                EnsureMainWindowVisible();
            }

            // 如果不是开机自启自动运行的，则默认显示设置窗口
            bool isSilentStartup = e.Args.Contains("--startup");
            if (!isSilentStartup)
            {
                ShowOrActivateSettingsWindow();
            }

            // 自动检查更新 (静默进行)
            if (BarrageSettings.AutoCheckUpdate)
            {
                // 不要 await 阻塞启动流程，让它在后台静默执行
                _ = UpdateService.CheckForUpdatesAsync(isManualCheck: false);
            }

            // 同步一次开机自启状态（防脏数据）
            UpdateStartupShortcut(BarrageSettings.RunOnStartup);
        }

        /// <summary>
        /// 更新开机自启动状态（修改系统注册表）
        /// </summary>
        public static void UpdateStartupShortcut(bool enable)
        {
            try
            {
                // AppDomain.CurrentDomain.BaseDirectory 加上执行程序名
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;
                if (key != null)
                {
                    if (enable)
                    {
                        // 添加 --startup 参数，以便区分是用户手动打开还是开机自启
                        key.SetValue("NotiFlow", $"\"{exePath}\" --startup");
                    }
                    else
                    {
                        key.DeleteValue("NotiFlow", false);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"更新开机自启状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 确保弹幕主窗口处于可见状态。如果尚未创建则新建。
        /// </summary>
        public void EnsureMainWindowVisible()
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Show();
            }
            else if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }
        }

        /// <summary>
        /// 隐藏弹幕主窗口（不销毁，保留通知服务订阅）
        /// </summary>
        public void HideMainWindow()
        {
            _mainWindow?.Hide();
        }

        /// <summary>
        /// 根据当前工作状态与弹幕活跃情况同步主窗口的可见性。
        /// 由 TrayIconService 和 CustomPage 的开关按钮调用。
        /// </summary>
        public void SyncMainWindowVisibility()
        {
            if (BarrageSettings.IsWorking)
            {
                EnsureMainWindowVisible();
            }
            // 注意：关闭工作状态时不立即隐藏，要等到所有弹幕飞完
            // MainWindow 内部的渲染循环会检测并自动隐藏
        }

        /// <summary>
        /// 显示或唤醒设置窗口。如果已存在则激活到前台，否则新建。
        /// </summary>
        public void ShowOrActivateSettingsWindow()
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Show();
            }
            else if (_settingsWindow.IsVisible)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
            }
            else
            {
                // 窗口存在但被隐藏，重新显示
                _settingsWindow.Show();
            }

            // 强制置顶并获取焦点（突破 Windows 防焦点窃取限制）
            _settingsWindow.Activate();
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.Topmost = true;
                    _settingsWindow.Topmost = false;
                    _settingsWindow.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 刷新托盘菜单中的工作状态显示
        /// </summary>
        public void RefreshTrayState()
        {
            _trayIconService?.RefreshWorkingState();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _foregroundMonitorService?.Dispose();
            _trayIconService?.Dispose();
            _mainWindow?.Close();
            _settingsWindow?.Close();
            base.OnExit(e);
        }
    }
}
