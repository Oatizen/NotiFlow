using System.Windows;

namespace NotiFlow.Views.Windows
{
    /// <summary>
    /// 更新检查结果弹窗的用户操作枚举。
    /// 用于替代 WPF-UI MessageBox 无法区分"关闭窗口"和"点击按钮"的缺陷。
    /// </summary>
    public enum UpdateDialogResult
    {
        /// <summary>用户点击了"确定"或关闭了窗口（不执行任何跳转）</summary>
        Close,
        /// <summary>用户点击了"GitHub仓库"按钮</summary>
        GitHub,
        /// <summary>用户点击了"Gitee仓库"按钮</summary>
        Gitee,
        /// <summary>用户点击了"跳过该版本"按钮</summary>
        Skip
    }

    public enum UpdateDialogMode
    {
        /// <summary>普通提示信息，仅显示"确定"，或者"确定"与"仓库"按钮（如检查失败时）。</summary>
        Info,
        /// <summary>用户手动检查且有更新：显示蓝色的"GitHub仓库"和"Gitee仓库"。</summary>
        ManualUpdate,
        /// <summary>后台自动检查且有更新：显示白色的"跳过该版本"以及蓝色的"GitHub仓库"和"Gitee仓库"。</summary>
        AutoUpdate
    }

    /// <summary>
    /// 自定义的更新提示弹窗，替代 WPF-UI 内置 MessageBox。
    /// </summary>
    public partial class UpdateDialogWindow
    {
        public UpdateDialogResult UserResult { get; private set; } = UpdateDialogResult.Close;

        public UpdateDialogWindow(string dialogTitle, string dialogContent, UpdateDialogMode mode = UpdateDialogMode.Info)
        {
            InitializeComponent();

            DialogTitleText.Text = dialogTitle;
            DialogContentText.Text = dialogContent;

            // 根据模式配置按钮
            switch (mode)
            {
                case UpdateDialogMode.ManualUpdate:
                    ActionButton.Visibility = Visibility.Collapsed;
                    GitHubButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                    GiteeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                    break;
                case UpdateDialogMode.AutoUpdate:
                    ActionButton.Visibility = Visibility.Visible;
                    ActionButton.Content = "跳过该版本";
                    ActionButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                    GitHubButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                    GiteeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                    break;
                case UpdateDialogMode.Info:
                default:
                    ActionButton.Visibility = Visibility.Visible;
                    ActionButton.Content = "确定";
                    ActionButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                    GitHubButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                    GiteeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                    break;
            }
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            // 如果按钮文本是"跳过该版本"，则返回 Skip 结果；否则返回 Close 结果。
            if (ActionButton.Content?.ToString() == "跳过该版本")
            {
                UserResult = UpdateDialogResult.Skip;
            }
            else
            {
                UserResult = UpdateDialogResult.Close;
            }
            Close();
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            UserResult = UpdateDialogResult.GitHub;
            Close();
        }

        private void GiteeButton_Click(object sender, RoutedEventArgs e)
        {
            UserResult = UpdateDialogResult.Gitee;
            Close();
        }
    }
}
