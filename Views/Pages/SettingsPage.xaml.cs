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
        }
    }
}
