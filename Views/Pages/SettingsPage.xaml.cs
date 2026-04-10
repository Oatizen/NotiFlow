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

            // 根据当前应用主题，初始化 ComboBox 选中项
            var currentTheme = ApplicationThemeManager.GetAppTheme();
            switch (currentTheme)
            {
                case ApplicationTheme.Light:
                    ThemeComboBox.SelectedIndex = 1;
                    break;
                case ApplicationTheme.Dark:
                    ThemeComboBox.SelectedIndex = 2;
                    break;
                default:
                    ThemeComboBox.SelectedIndex = 0; // 系统默认
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
                    break;
                case 1: // 浅色
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case 2: // 深色
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
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
    }
}
