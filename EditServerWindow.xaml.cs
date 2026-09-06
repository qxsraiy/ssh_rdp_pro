using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace 云端管理
{
    public partial class EditServerWindow : Window
    {
        public SshProfile ResultProfile { get; private set; }
        private string _keyContent = "";

        public EditServerWindow(SshProfile profile = null)
        {
            InitializeComponent();
            WindowTitle.Text = profile == null ? "新增服务器" : "编辑服务器";
            if (profile != null)
            {
                NameInput.Text = profile.Name;
                HostInput.Text = profile.Host;
                PortInput.Text = profile.Port;
                UserInput.Text = profile.Username;

                // 回填协议类型
                if (profile.Protocol == "RDP")
                {
                    RadioRDP.IsChecked = true;
                }

                // 回填认证方式与凭据（关键：密码登录必须回填密码框，否则编辑保存后密码丢失）
                if (profile.AuthType == "Key" && profile.Protocol != "RDP")
                {
                    RadioKey.IsChecked = true;
                    _keyContent = profile.SecretData;
                    KeyPathDisplay.Text = "已加载已有密钥";
                }
                else
                {
                    PasswordInput.Password = profile.SecretData;
                }
            }

            // 根据协议初始化界面状态
            UpdateProtocolUI();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Protocol_Changed(object sender, RoutedEventArgs e)
        {
            UpdateProtocolUI();
        }

        private void UpdateProtocolUI()
        {
            if (AuthArea == null || RadioRDP == null) return;

            bool isRdp = RadioRDP.IsChecked == true;

            // RDP 下隐藏认证方式区，SSH 显示
            AuthArea.Visibility = isRdp ? Visibility.Collapsed : Visibility.Visible;

            // 端口默认值切换：SSH=22，RDP=3389（仅当用户还没输入时）
            if (string.IsNullOrWhiteSpace(PortInput.Text) || PortInput.Text == "22" || PortInput.Text == "3389")
            {
                PortInput.Text = isRdp ? "3389" : "22";
            }

            if (isRdp)
            {
                // RDP：只保留密码，隐藏密钥选择区
                SecretLabel.Text = "RDP 密码";
                PasswordArea.Visibility = Visibility.Visible;
                KeyArea.Visibility = Visibility.Collapsed;
            }
            else
            {
                // SSH：按认证方式显示
                bool isKey = RadioKey.IsChecked == true;
                PasswordArea.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
                KeyArea.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
                SecretLabel.Text = isKey ? "私钥文件" : "SSH 密码";
            }
        }

        private void AuthType_Changed(object sender, RoutedEventArgs e)
        {
            if (PasswordArea == null || KeyArea == null || RadioRDP == null) return;
            if (RadioRDP.IsChecked == true) return; // RDP 模式下认证方式不生效

            bool isKey = RadioKey.IsChecked == true;
            PasswordArea.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
            KeyArea.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
            SecretLabel.Text = isKey ? "私钥文件" : "SSH 密码";
        }

        private void SelectKey_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                KeyPathDisplay.Text = Path.GetFileName(openFileDialog.FileName);
                _keyContent = File.ReadAllText(openFileDialog.FileName);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            bool isRdp = RadioRDP.IsChecked == true;

            // 必填校验：主机地址不能为空
            if (string.IsNullOrWhiteSpace(HostInput.Text))
            {
                MessageBox.Show("请填写主机 IP 地址。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                HostInput.Focus();
                return;
            }

            ResultProfile = new SshProfile
            {
                Protocol = isRdp ? "RDP" : "SSH",
                Name = string.IsNullOrWhiteSpace(NameInput.Text) ? HostInput.Text : NameInput.Text,
                Host = HostInput.Text.Trim(),
                Port = string.IsNullOrWhiteSpace(PortInput.Text) ? (isRdp ? "3389" : "22") : PortInput.Text.Trim(),
                Username = UserInput.Text.Trim(),
                AuthType = (RadioKey.IsChecked == true && !isRdp) ? "Key" : "Password",
                SecretData = (RadioKey.IsChecked == true && !isRdp) ? _keyContent : PasswordInput.Password
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
