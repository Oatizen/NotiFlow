using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace NotiFlow.Views.Pages
{
    public partial class SettingsPage : Page
    {
        private bool _initialized;

        public SettingsPage()
        {
            InitializeComponent();

            // 根据当前用户保存的主题配置，初始化 ComboBox 选中项
            switch (BarrageSettings.Theme)
            {
                case "Light":
                    ThemeComboBox.SelectedIndex = 1;
                    break;
                case "Dark":
                    ThemeComboBox.SelectedIndex = 2;
                    break;
                default:
                    ThemeComboBox.SelectedIndex = 0; // System
                    break;
            }

            _initialized = true;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止构造阶段 ComboBox 初始化时触发
            if (!_initialized) return;

            switch (ThemeComboBox.SelectedIndex)
            {
                case 0: // 系统默认
                    ApplicationThemeManager.ApplySystemTheme();
                    BarrageSettings.Theme = "System";
                    break;
                case 1: // 浅色
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    BarrageSettings.Theme = "Light";
                    break;
                case 2: // 深色
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    BarrageSettings.Theme = "Dark";
                    break;
            }

            // 保存主题配置到磁盘
            BarrageSettings.ExportConfig();

            // 修复 WPF-UI 切换主题后窗口背景卡顿/失焦颜色的 Bug（重置 Backdrop 触发刷新）
            var currentWindow = Window.GetWindow(this) as Wpf.Ui.Controls.FluentWindow;
            if (currentWindow != null)
            {
                currentWindow.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
                currentWindow.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
            }

            // 主题切换后，WPF-UI 会通过 DWM API 将所有窗口强制设置深色模式属性，
            // 导致全透明的弹幕叠加窗口变为黑色不透明并遮挡整个桌面。
            // 此处立即在 Win32 DWM 层面将 MainWindow 的深色渲染属性重置回去。
            RestoreBarrageWindowTransparency();
        }

        /// <summary>
        /// 遍历当前应用的所有窗口，找到弹幕叠加窗口并在 OS 层面重置其透明状态。
        /// </summary>
        private static void RestoreBarrageWindowTransparency()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow)
                {
                    NativeMethods.ResetWindowTransparency(window);
                }
            }
        }

        private void HotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (DataContext is Models.SettingsViewModel viewModel && viewModel.IsCapturingHotKey)
            {
                e.Handled = true;

                // 按下 ESC 键直接取消捕捉
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    viewModel.IsCapturingHotKey = false;
                    viewModel.HotKeyText = viewModel.GetHotKeyString(BarrageSettings.HotKeyModifier, BarrageSettings.HotKey);
                    return;
                }

                uint modifiers = 0;
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) modifiers |= NativeMethods.MOD_WIN;

                // 真主键提取，包含对 IME 处理过的键的容错
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                if (key == System.Windows.Input.Key.ImeProcessed)
                {
                    // 若使用了中文输入法拦截，退回硬虚拟键码提取 (依赖底层互操作)
                    key = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)NativeMethods.MapVirtualKey((uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.ImeProcessedKey), 0));
                }

                if (key != System.Windows.Input.Key.None &&
                    key != System.Windows.Input.Key.LeftCtrl && key != System.Windows.Input.Key.RightCtrl &&
                    key != System.Windows.Input.Key.LeftShift && key != System.Windows.Input.Key.RightShift &&
                    key != System.Windows.Input.Key.LeftAlt && key != System.Windows.Input.Key.RightAlt &&
                    key != System.Windows.Input.Key.LWin && key != System.Windows.Input.Key.RWin)
                {
                    uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                    // 确保按下了真正的功能主键
                    if (vk != 0 && vk != 255)
                    {
                        viewModel.FinishCaptureHotKey(modifiers, vk);
                    }
                }
            }
        }

        private void HotkeyButton_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is Models.SettingsViewModel viewModel && viewModel.IsCapturingHotKey)
            {
                // 焦点丢失时自动退出捕获状态，还原原有文本
                viewModel.IsCapturingHotKey = false;
                viewModel.HotKeyText = viewModel.GetHotKeyString(BarrageSettings.HotKeyModifier, BarrageSettings.HotKey);
            }
        }
    }
}
