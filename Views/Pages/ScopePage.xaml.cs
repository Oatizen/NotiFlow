using System.Windows;
using System.Windows.Controls;
using NotiFlow.Models;
using NotiFlow.Views.Windows;

namespace NotiFlow.Views.Pages
{
    public partial class ScopePage : Page
    {
        public ScopePage()
        {
            InitializeComponent();
            Loaded += ScopePage_Loaded;
        }

        private void ScopePage_Loaded(object sender, RoutedEventArgs e)
        {
            // 在页面完全加载后才触发数据初始化，避免在 XAML 解析阶段执行 P/Invoke 导致崩溃
            if (DataContext is ScopeViewModel vm)
            {
                vm.Initialize();
            }
        }

        private void Fab_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialogWindow(Window.GetWindow(this));
            dialog.ShowDialog();

            if (dialog.IsConfirmed && DataContext is ScopeViewModel vm)
            {
                vm.AddManualRule(dialog.Identifier, dialog.DisplayName);
            }
        }
        private void TopHelpButton_Click(object sender, RoutedEventArgs e)
        {
            TopHelpFlyout.IsOpen = true;
        }

        private void BottomHelpButton_Click(object sender, RoutedEventArgs e)
        {
            BottomHelpFlyout.IsOpen = true;
        }
    }
}
