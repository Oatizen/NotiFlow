using System.Windows;
using Wpf.Ui.Controls;

namespace NotiFlow.Views.Windows
{
    public partial class InputDialogWindow : FluentWindow
    {
        public string Identifier { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        
        public bool IsConfirmed { get; private set; }

        public InputDialogWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            IdentifierTextBox.Focus();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(IdentifierTextBox.Text))
            {
                // 可以弹个提示或直接返回
                return;
            }
            
            Identifier = IdentifierTextBox.Text.Trim();
            DisplayName = DisplayNameTextBox.Text.Trim();
            IsConfirmed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
