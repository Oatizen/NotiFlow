using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace NotiFlow.Services
{
    /// <summary>
    /// 系统托盘图标管理服务。
    /// 负责创建和维护任务栏通知区域图标，提供左键唤出设置界面和右键 Fluent Design 快捷菜单的能力。
    /// 生命周期与 Application 绑定，在应用退出时自动释放资源。
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenu _contextMenu;
        private readonly MenuItem _toggleWorkItem;
        private Window? _helperWindow;

        public TrayIconService()
        {
            // 使用应用程序自身的可执行文件图标作为托盘图标
            Icon appIcon;
            try
            {
                string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                appIcon = Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
            }
            catch
            {
                appIcon = SystemIcons.Application;
            }

            // 构建 WPF 风格的 Fluent Design 右键上下文菜单
            _toggleWorkItem = new MenuItem { Header = GetWorkingText() };
            _toggleWorkItem.Click += (_, _) => ToggleWorking();

            var openSettingsItem = new MenuItem { Header = "打开设置" };
            openSettingsItem.Click += (_, _) => ShowSettingsWindow();

            var exitItem = new MenuItem { Header = "退出 NotiFlow" };
            exitItem.Click += (_, _) => ExitApplication();

            _contextMenu = new ContextMenu
            {
                Items =
                {
                    _toggleWorkItem,
                    new Separator(),
                    openSettingsItem,
                    new Separator(),
                    exitItem
                }
            };

            _notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = "NotiFlow - 弹幕通知",
                Visible = true
            };

            // 左键单击唤出设置界面，右键弹出 WPF Fluent 菜单
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowSettingsWindow();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    ShowContextMenu();
                }
            };
        }

        /// <summary>
        /// 在托盘图标位置弹出 WPF 风格的上下文菜单。
        /// 核心技巧：必须先将一个窗口设为前台窗口，否则 WPF ContextMenu 无法感知失焦事件，
        /// 导致点击菜单外部区域时菜单不会自动关闭。
        /// </summary>
        private void ShowContextMenu()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 刷新菜单项状态
                _toggleWorkItem.Header = GetWorkingText();

                // 确保辅助窗口存在（一个像素级别的隐藏窗口，仅用于承载前台焦点）
                if (_helperWindow == null)
                {
                    _helperWindow = new Window
                    {
                        Width = 0, Height = 0,
                        WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        Topmost = true
                    };
                }

                // 将辅助窗口移到鼠标当前位置附近并显示，以便夺取前台焦点
                var cursorPos = System.Windows.Forms.Cursor.Position;
                _helperWindow.Left = cursorPos.X;
                _helperWindow.Top = cursorPos.Y;
                _helperWindow.Show();

                // 【关键 Win32 调用】将辅助窗口设为前台，系统才会在点击外部时发送失焦消息
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_helperWindow).Handle;
                NativeMethods.SetForegroundWindow(hwnd);

                // 将 ContextMenu 绑定到辅助窗口上弹出
                _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _contextMenu.PlacementTarget = _helperWindow;
                _contextMenu.IsOpen = true;

                // 菜单关闭后自动隐藏辅助窗口
                _contextMenu.Closed -= OnContextMenuClosed;
                _contextMenu.Closed += OnContextMenuClosed;
            });
        }

        private void OnContextMenuClosed(object sender, RoutedEventArgs e)
        {
            _helperWindow?.Hide();
        }

        /// <summary>
        /// 根据当前工作状态返回菜单显示文本
        /// </summary>
        private static string GetWorkingText()
        {
            return BarrageSettings.IsWorking ? "⏸ 暂停工作" : "▶ 开始工作";
        }

        /// <summary>
        /// 切换弹幕渲染的工作状态，并同步更新菜单文本与主窗口可见性
        /// </summary>
        private void ToggleWorking()
        {
            BarrageSettings.IsWorking = !BarrageSettings.IsWorking;
            RefreshWorkingState();
        }

        /// <summary>
        /// 刷新所有与工作状态相关的 UI 组件（菜单文本、托盘提示、主窗口可见性）
        /// </summary>
        public void RefreshWorkingState()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _toggleWorkItem.Header = GetWorkingText();
                _notifyIcon.Text = BarrageSettings.IsWorking
                    ? "NotiFlow - 工作中"
                    : "NotiFlow - 已暂停";

                // 通知主窗口根据工作状态决定显示或隐藏
                if (Application.Current is App app)
                {
                    app.SyncMainWindowVisibility();
                }
            });
        }

        /// <summary>
        /// 唤出或创建设置窗口
        /// </summary>
        private static void ShowSettingsWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current is App app)
                {
                    app.ShowOrActivateSettingsWindow();
                }
            });
        }

        /// <summary>
        /// 安全退出整个应用程序
        /// </summary>
        private static void ExitApplication()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Application.Current.Shutdown();
            });
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _helperWindow?.Close();
        }
    }
}
