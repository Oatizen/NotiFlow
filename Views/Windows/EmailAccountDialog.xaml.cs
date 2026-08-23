using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using MailKit.Net.Imap;
using MailKit.Security;
using NotiFlow.Models;
using Wpf.Ui.Controls;

namespace NotiFlow.Views.Windows
{
    /// <summary>
    /// 邮箱账号添加与编辑对话框交互逻辑。
    /// </summary>
    public partial class EmailAccountDialog : FluentWindow
    {
        private readonly EmailProviderPreset _preset;
        private readonly EmailAccountConfigDto? _editingAccount;

        public EmailAccountDialog(EmailProviderPreset preset, EmailAccountConfigDto? editingAccount = null)
        {
            InitializeComponent();

            _preset = preset;
            _editingAccount = editingAccount;

            InitializeForm();
        }

        private void InitializeForm()
        {
            ProviderNameText.Text = _preset.DisplayName;
            HelpGuideText.Text = _preset.HelpGuideDescription;

            // 加载品牌 Logo 图片
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Email", _preset.ImageFileName);
                if (File.Exists(iconPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    ProviderLogoImage.Source = bmp;
                }
            }
            catch { }

            // 配置服务器信息显示
            if (_preset.ProviderType == "Custom")
            {
                CustomServerPanel.Visibility = Visibility.Visible;
                ServerInfoText.Text = "自定义标准 IMAP 协议服务";
                ServerHostBox.Text = _editingAccount?.ServerHost ?? "";
                ServerPortBox.Text = (_editingAccount?.ServerPort ?? 993).ToString();
                UseSslCheckBox.IsChecked = _editingAccount?.UseSsl ?? true;
            }
            else
            {
                CustomServerPanel.Visibility = Visibility.Collapsed;
                ServerInfoText.Text = $"IMAP: {_preset.DefaultHost} (SSL: {_preset.DefaultPort})";
            }

            // 加载编辑数据
            if (_editingAccount != null)
            {
                EmailAddressBox.Text = _editingAccount.EmailAddress;
                DisplayNameBox.Text = _editingAccount.DisplayName;
                AuthCodeBox.Password = _editingAccount.AuthCode;
                DeleteAccountButton.Visibility = Visibility.Visible;
                Title = $"编辑 {_preset.DisplayName}";
            }
            else
            {
                DisplayNameBox.Text = _preset.DisplayName;
                Title = $"绑定 {_preset.DisplayName}";
            }
        }

        private void Field_TextChanged(object sender, RoutedEventArgs e)
        {
            StatusMessageText.Visibility = Visibility.Collapsed;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailAddressBox.Text.Trim();
            string authCode = AuthCodeBox.Password.Trim();
            string displayName = DisplayNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = _preset.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("请输入有效的邮箱地址");
                EmailAddressBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(authCode))
            {
                ShowError("请输入邮箱授权码或客户端专用密码");
                AuthCodeBox.Focus();
                return;
            }

            string host = _preset.ProviderType == "Custom" ? ServerHostBox.Text.Trim() : _preset.DefaultHost;
            int port = _preset.ProviderType == "Custom" && int.TryParse(ServerPortBox.Text.Trim(), out int p) ? p : _preset.DefaultPort;
            bool useSsl = _preset.ProviderType == "Custom" ? (UseSslCheckBox.IsChecked == true) : _preset.DefaultUseSsl;

            if (string.IsNullOrWhiteSpace(host))
            {
                ShowError("请输入 IMAP 服务器主机地址");
                ServerHostBox.Focus();
                return;
            }

            // 锁定界面并开始测试连接
            SaveButton.IsEnabled = false;
            SaveButton.Content = "正在测试连接...";
            StatusMessageText.Visibility = Visibility.Collapsed;

            try
            {
                // 进行 IMAP SSL 真实连接与身份认证测试
                bool testSuccess = await Task.Run(async () =>
                {
                    using var client = new ImapClient();
                    client.ServerCertificateValidationCallback = (s, c, h, ex) => true;

                    var secureSocketOptions = useSsl
                        ? (port == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable)
                        : SecureSocketOptions.None;

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(host, port, secureSocketOptions, cts.Token);
                    await client.AuthenticateAsync(email, authCode, cts.Token);
                    await client.DisconnectAsync(true, CancellationToken.None);
                    return true;
                });

                if (testSuccess)
                {
                    // 保存/更新账号配置
                    var targetAccount = _editingAccount ?? new EmailAccountConfigDto();
                    targetAccount.ProviderType = _preset.ProviderType;
                    targetAccount.DisplayName = displayName;
                    targetAccount.EmailAddress = email;
                    targetAccount.ServerHost = host;
                    targetAccount.ServerPort = port;
                    targetAccount.UseSsl = useSsl;
                    targetAccount.IsEnabled = true;
                    targetAccount.AuthCode = authCode; // 内部自动触发 DPAPI 加密

                    if (_editingAccount == null)
                    {
                        BarrageSettings.EmailAccounts.Add(targetAccount);
                    }

                    // 导出配置并触发服务热加载
                    BarrageSettings.ExportConfig();
                    ((App)Application.Current).EmailNotificationService?.ReloadAccounts();

                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                ShowError($"连接测试失败: {ex.Message}");
            }
            finally
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = "测试并保存";
            }
        }

        private void ShowError(string msg)
        {
            StatusMessageText.Text = msg;
            StatusMessageText.Visibility = Visibility.Visible;
        }

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_editingAccount != null)
            {
                BarrageSettings.EmailAccounts.RemoveAll(a => a.Id == _editingAccount.Id);
                BarrageSettings.ExportConfig();
                ((App)Application.Current).EmailNotificationService?.ReloadAccounts();

                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
