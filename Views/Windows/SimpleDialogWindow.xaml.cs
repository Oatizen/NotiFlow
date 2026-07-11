using System.Windows;

namespace NotiFlow.Views.Windows
{
    /// <summary>
    /// 通用的简单提示弹窗，仅包含一个"确定"按钮。
    /// 用于替代 System.Windows.MessageBox，保持与应用整体 Fluent 风格一致。
    /// </summary>
    public partial class SimpleDialogWindow
    {
        public bool IsConfirmed { get; private set; }

        /// <summary>
        /// 创建简单提示弹窗。
        /// </summary>
        /// <param name="dialogTitle">弹窗内的主标题文字</param>
        /// <param name="dialogContent">弹窗内的正文内容</param>
        /// <param name="okText">确认按钮的文本，默认为“确定”</param>
        /// <param name="cancelText">取消按钮的文本，留空则不显示取消按钮</param>
        public SimpleDialogWindow(string dialogTitle, string dialogContent, string okText = "确定", string cancelText = "")
        {
            InitializeComponent();

            DialogTitleText.Text = dialogTitle;
            DialogContentText.Text = dialogContent;
            OkButton.Content = okText;

            if (!string.IsNullOrEmpty(cancelText))
            {
                CancelButton.Content = cancelText;
                CancelButton.Visibility = Visibility.Visible;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }
    }
}
