using System.Windows.Controls;

namespace NotiFlow.Views.Pages
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
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
                
                // 【核心修正】彻底统一 MessageBox 为均匀灰色背景
                var accentBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SecondaryBackgroundFillColorDefaultBrush"];
                mb.Background = accentBrush;
                mb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;
                
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

                var accentBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SecondaryBackgroundFillColorDefaultBrush"];
                mb.Background = accentBrush;
                mb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;

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

            var accentBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SecondaryBackgroundFillColorDefaultBrush"];
            mb.Background = accentBrush;
            mb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;

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

                successMb.Background = accentBrush;
                successMb.Resources["SolidBackgroundFillColorBaseBrush"] = accentBrush;

                await successMb.ShowDialogAsync();
            }
        }
    }
}
