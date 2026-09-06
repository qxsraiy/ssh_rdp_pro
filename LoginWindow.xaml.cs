using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace 云端管理
{
    public partial class LoginWindow : Window
    {
        private string _dataFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.dat");

        public LoginWindow()
        {
            InitializeComponent();
            this.Loaded += LoginWindow_Loaded;
            ModeLocal.IsChecked = true; // 初始化完成后默认选中本地模式
            LoadSavedConfig();
            UpdateModeUI();
        }

        private void LoadSavedConfig()
        {
            var config = CloudSyncManager.GetConfig();
            if (config == null) return;

            switch (config.Mode)
            {
                case SyncMode.Official:
                    ModeOfficial.IsChecked = true;
                    AccountInput.Text = config.OfficialAccount;
                    break;
                case SyncMode.WebDAV:
                    ModeWebDAV.IsChecked = true;
                    WebDavUrlInput.Text = config.Url;
                    WebDavUserInput.Text = config.User;
                    WebDavPwdInput.Password = config.Password;
                    break;
                default:
                    ModeLocal.IsChecked = true;
                    break;
            }
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeAppAsync();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            // 防止 InitializeComponent 过程中 Checked 事件提前触发导致控件未创建
            if (OfficialPanel == null || WebDAVPanel == null || LocalPanel == null) return;
            OfficialPanel.Visibility = ModeOfficial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            WebDAVPanel.Visibility = ModeWebDAV.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LocalPanel.Visibility = ModeLocal.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (ModeOfficial.IsChecked == true)
            {
                StatusLabel.Text = "请输入云端账号和主密码:";
                if (string.IsNullOrWhiteSpace(AccountInput.Text)) AccountInput.Focus();
                else MasterPwdInputOfficial.Focus();
            }
            else if (ModeWebDAV.IsChecked == true)
            {
                StatusLabel.Text = "WebDAV 同步模式，请输入主密码解锁:";
                MasterPwdInputWebDAV.Focus();
            }
            else
            {
                StatusLabel.Text = "本地模式，请输入主密码解锁:";
                MasterPwdInputLocal.Focus();
            }
        }

        // 当前面板的主密码（官方/WebDAV/本地各自的输入框）
        private string GetMasterPassword()
        {
            if (ModeOfficial.IsChecked == true) return MasterPwdInputOfficial.Password;
            if (ModeWebDAV.IsChecked == true) return MasterPwdInputWebDAV.Password;
            return MasterPwdInputLocal.Password;
        }

        private async Task InitializeAppAsync()
        {
            var config = CloudSyncManager.GetConfig();
            bool localFileExists = File.Exists(_dataFile);

            // WebDAV 模式：本地数据缺失但云同步配置存在 → 询问拉取
            if (config != null && config.Mode == SyncMode.WebDAV && config.IsEnabled && !localFileExists)
            {
                var result = MessageBox.Show(
                    "检测到本地数据缺失，但 WebDAV 云同步已配置。\n是否从云端拉取配置库？",
                    "数据恢复建议",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    StatusLabel.Text = "正在从云端拉取数据...";
                    LoginBtn.IsEnabled = false;

                    bool success = await CloudSyncManager.DownloadLatestAsync(_dataFile);

                    if (success)
                    {
                        StatusLabel.Text = "同步成功，请输入主密码解锁:";
                    }
                    else
                    {
                        MessageBox.Show("云端拉取失败，请检查网络或 WebDAV 配置。", "同步失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusLabel.Text = "请手动设置主密码或检查配置:";
                    }
                    LoginBtn.IsEnabled = true;
                }
                else
                {
                    CloudSyncManager.ClearConfig();
                    StatusLabel.Text = "已重置。请设置新的主密码:";
                }
            }
            else if (!localFileExists && !(config != null && config.Mode == SyncMode.Official))
            {
                StatusLabel.Text = "首次启动，请设置主密码:";
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            await VerifyAndLogin();
        }

        private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await VerifyAndLogin();
        }

        private async Task VerifyAndLogin()
        {
            string masterPwd = GetMasterPassword();
            if (string.IsNullOrWhiteSpace(masterPwd)) return;

            LoginBtn.IsEnabled = false;
            string originalStatus = StatusLabel.Text;

            try
            {
                // ========== 官方同步模式 ==========
                if (ModeOfficial.IsChecked == true)
                {
                    string account = AccountInput.Text.Trim();
                    if (string.IsNullOrWhiteSpace(account))
                    {
                        MessageBox.Show("请输入云端账号。", "提示");
                        LoginBtn.IsEnabled = true;
                        return;
                    }

                    StatusLabel.Text = "正在连接云端 (账号不存在将自动注册)...";
                    string token = OfficialCloudSyncManager.GetToken(account, masterPwd);
                    await OfficialCloudSyncManager.LoginAsync(account, token);

                    var config = CloudSyncManager.GetConfig() ?? new CloudConfig();
                    config.Mode = SyncMode.Official;
                    config.OfficialAccount = account;
                    config.IsEnabled = false;
                    CloudSyncManager.SaveConfig(config);

                    StatusLabel.Text = "连接成功，正在拉取云端数据...";
                    await PullAndLoginAsync(token, masterPwd);
                    return;
                }

                // ========== WebDAV 模式：先保存内嵌配置，再同步 ==========
                if (ModeWebDAV.IsChecked == true)
                {
                    var config = CloudSyncManager.GetConfig() ?? new CloudConfig();
                    config.Mode = SyncMode.WebDAV;
                    config.Url = WebDavUrlInput.Text.Trim().EndsWith("/") ? WebDavUrlInput.Text.Trim() : WebDavUrlInput.Text.Trim() + "/";
                    config.User = WebDavUserInput.Text.Trim();
                    config.Password = WebDavPwdInput.Password;
                    config.IsEnabled = !string.IsNullOrWhiteSpace(WebDavUrlInput.Text)
                                      && !string.IsNullOrWhiteSpace(WebDavUserInput.Text)
                                      && !string.IsNullOrWhiteSpace(WebDavPwdInput.Password);
                    CloudSyncManager.SaveConfig(config);

                    // 登录现有库前先同步一次
                    if (config.IsEnabled && File.Exists(_dataFile))
                    {
                        StatusLabel.Text = "同步中...";
                        await CloudSyncManager.DownloadLatestAsync(_dataFile);
                    }
                }

                // ========== 验证主密码（本地 / WebDAV 共用） ==========
                if (File.Exists(_dataFile))
                {
                    try
                    {
                        ProfileManager.LoadProfiles(masterPwd);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("主密码错误，请重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusLabel.Text = originalStatus;
                        ClearMasterInputs();
                        LoginBtn.IsEnabled = true;
                        return;
                    }
                }

                OpenMainWindow(masterPwd);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"登录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusLabel.Text = originalStatus;
                LoginBtn.IsEnabled = true;
            }
        }

        private void ClearMasterInputs()
        {
            MasterPwdInputOfficial.Clear();
            MasterPwdInputWebDAV.Clear();
            MasterPwdInputLocal.Clear();
        }

        // 官方模式：拉取云端记录（每条 = 一台服务器）→ 逐条用主密码解密 → 存本地 → 进主界面
        private async Task PullAndLoginAsync(string token, string masterPassword)
        {
            try
            {
                var records = await OfficialCloudSyncManager.PullAsync(token);

                var profiles = new List<SshProfile>();
                int decryptFail = 0;
                foreach (var rec in records)
                {
                    // 跳过旧版整包数据（item_key="all"），新版按条存储
                    if (rec.item_key == "all") continue;
                    try
                    {
                        string json = CryptoHelper.Decrypt(rec.payload, masterPassword);
                        var p = JsonConvert.DeserializeObject<SshProfile>(json);
                        if (p != null) profiles.Add(p);
                    }
                    catch
                    {
                        decryptFail++;
                    }
                }

                // 有数据但全部解密失败 → 几乎可以确定是主密码错误
                if (records.Count > 0 && profiles.Count == 0 && decryptFail > 0)
                {
                    MessageBox.Show("主密码无法解密云端数据，请确认输入了正确的主密码。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoginBtn.IsEnabled = true;
                    StatusLabel.Text = "请重新输入主密码:";
                    return;
                }

                // 保存到本地（加密）
                ProfileManager.SaveProfiles(profiles, masterPassword);
                StatusLabel.Text = "同步完成，正在进入...";
                OpenMainWindow(masterPassword);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"拉取云端数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LoginBtn.IsEnabled = true;
                StatusLabel.Text = "拉取失败，请重试:";
            }
        }

        private void OpenMainWindow(string pwd)
        {
            MainWindow mainWindow = new MainWindow(pwd);
            mainWindow.Show();
            this.Close();
        }
    }
}
