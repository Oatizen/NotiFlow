using System.Windows;

namespace NotiFlow.Views.Windows
{
    /// <summary>
    /// 通用的简单提示弹窗，仅包含一个"确定"按钮。
    /// 用于替代 System.Windows.MessageBox，保持与应用整体 Fluent 风格一致。
    /// </summary>
    public partial class SimpleDialogWindow
    {
        /// <summary>
        /// 创建简单提示弹窗。
        /// </summary>
        /// <param name="dialogTitle">弹窗内的主标题文字</param>
        /// <param name="dialogContent">弹窗内的正文内容</param>
        public SimpleDialogWindow(string dialogTitle, string dialogContent)
        {
            InitializeComponent();

            DialogTitleText.Text = dialogTitle;
            DialogContentText.Text = dialogContent;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
