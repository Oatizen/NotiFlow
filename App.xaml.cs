using System.Windows;
using Wpf.Ui.Appearance;

namespace NotiFlow
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 仅在启动时应用一次默认主题 (浅色)
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

            // 同步启动最高层级的全局透明弹幕引擎宿主窗口（隐藏状态运行）
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
