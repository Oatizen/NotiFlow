using System.Windows.Controls;

namespace NotiFlow.Views.Pages
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            
            // 动态设置版本号
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBase.Text = $"V{version?.Major}.{version?.Minor}.{version?.Build}";
        }

        private void ApplyUniformBackground(Wpf.Ui.Controls.MessageBox mb)
        {
            var accentBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SecondaryBackgroundFillColorDefaultBrush"];
            mb.Background = accentBrush;
            
            // 强制洗掉所有的内部分层背景画刷
            mb.Resources["ApplicationBackgroundBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorTertiaryBrush"] = accentBrush;
            mb.Resources["SolidBackgroundFillColorQuarternaryBrush"] = accentBrush;
            mb.Resources["ControlFillColorDefaultBrush"] = accentBrush;

            // 通过 Loaded 事件进一步剥离内部可能的硬编码模板边框背景
            mb.Loaded += (s, e) =>
            {
                ForceUniformGrey(mb, accentBrush);
            };
        }

        private void ForceUniformGrey(System.Windows.DependencyObject parent, System.Windows.Media.Brush brush)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.Border border && border.TemplatedParent is Wpf.Ui.Controls.MessageBox)
                {
                    border.Background = System.Windows.Media.Brushes.Transparent;
                }
                else if (child is System.Windows.Controls.Grid grid && grid.TemplatedParent is Wpf.Ui.Controls.MessageBox)
                {
                    grid.Background = System.Windows.Media.Brushes.Transparent;
                }
                
                ForceUniformGrey(child, brush);
            }
        }

        private void Import_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                Title = "导入配置"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                BarrageSettings.ImportConfig(openFileDialog.FileName);
                
                // 导入后需要同步重注册热键
                (System.Windows.Application.Current as App)?.TrayIconService?.ReRegisterHotKey();
                
                Wpf.Ui.Controls.MessageBox mb = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "成功",
                    Content = "配置已成功从文件导入。",
                    CloseButtonText = "确定"
                };
                
                ApplyUniformBackground(mb);
                mb.ShowDialogAsync();
            }
        }

        private void Export_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 配置文件 (*.json)|*.json",
                Title = "导出配置",
                FileName = "BarrageConfig.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                BarrageSettings.ExportConfig(saveFileDialog.FileName);
                
                Wpf.Ui.Controls.MessageBox mb = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "成功",
                    Content = "当前配置已成功导出到位。",
                    CloseButtonText = "确定"
                };

                ApplyUniformBackground(mb);
                mb.ShowDialogAsync();
            }
        }

        private async void Reset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var mb = new Wpf.Ui.Controls.MessageBox
            {
                Title = "确认重置",
                Content = "您确定要将所有设置重置为系统默认值吗？此操作不可撤销。",
                PrimaryButtonText = "确认重置",
                CloseButtonText = "取消"
            };

            ApplyUniformBackground(mb);

            var result = await mb.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                BarrageSettings.ResetToDefault();
                
                // 重置后同步重注册热键
                (System.Windows.Application.Current as App)?.TrayIconService?.ReRegisterHotKey();

                Wpf.Ui.Controls.MessageBox successMb = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "重置成功",
                    Content = "设置已恢复至出厂状态。部分视觉效果可能需要重启设置页面以刷新显示。",
                    CloseButtonText = "确定"
                };

                ApplyUniformBackground(successMb);
                await successMb.ShowDialogAsync();
            }
        }

        private void Feedback_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Services.UpdateService.OpenUrl("https://github.com/Oatizen/NotiFlow/issues");
        }

        private void GitHubGo_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Services.UpdateService.OpenUrl("https://github.com/Oatizen/NotiFlow");
        }

        private async void CheckUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await Services.UpdateService.CheckForUpdatesAsync(isManualCheck: true);
        }
    }
}
