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
        Gitee
    }

    /// <summary>
    /// 自定义的更新提示弹窗，替代 WPF-UI 内置 MessageBox。
    /// 解决了原生 MessageBox 的上下异色、按钮文字截断、以及关闭按钮逻辑冲突等问题。
    /// </summary>
    public partial class UpdateDialogWindow
    {
        /// <summary>
        /// 用户在弹窗中做出的操作结果，默认为 Close（关闭窗口不跳转）。
        /// </summary>
        public UpdateDialogResult UserResult { get; private set; } = UpdateDialogResult.Close;

        /// <summary>
        /// 创建更新检查弹窗。
        /// </summary>
        /// <param name="dialogTitle">弹窗内的主标题文字（如"发现新版本"或"检查失败"）</param>
        /// <param name="dialogContent">弹窗内的正文内容（如更新日志或错误信息）</param>
        public UpdateDialogWindow(string dialogTitle, string dialogContent)
        {
            InitializeComponent();

            DialogTitleText.Text = dialogTitle;
            DialogContentText.Text = dialogContent;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            UserResult = UpdateDialogResult.Close;
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
