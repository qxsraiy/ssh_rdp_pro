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
        private string _dataFile;

        // 数据存储目录：用户文档\SshPro\（与 v1.2.0 定位一致，避免程序目录权限/重装丢失问题）
        public static string GetDataDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "SshPro");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        // 免责条款是否已同意（独立标记，不随模式切换重复弹出）
        private static string DisclaimerFlagPath => Path.Combine(GetDataDir(), "disclaimer_agreed.flag");
        public static bool HasAgreedDisclaimer => File.Exists(DisclaimerFlagPath);
        public static void MarkDisclaimerAgreed()
        {
            try { File.WriteAllText(DisclaimerFlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
        }

        public LoginWindow()
        {
            InitializeComponent();
            this.Loaded += LoginWindow_Loaded;
            ModeLocal.IsChecked = true; // 初始化完成后默认选中本地模式
            LoadSavedConfig();
            UpdateModeUI();
            UpdateDataFile();
        }

        // 按当前选择的模式隔离数据文件：本地 / WebDAV / 官方 互不干扰
        private void UpdateDataFile()
        {
            string fileName = "profiles_local.dat";
            if (ModeOfficial.IsChecked == true) fileName = "profiles_official.dat";
            else if (ModeWebDAV.IsChecked == true) fileName = "profiles_webdav.dat";
            _dataFile = Path.Combine(GetDataDir(), fileName);
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
            UpdateDataFile();
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

            // 免责条款：仅首次（未同意过）强制显示，同意后标记文件不再弹；拒绝则退出
            if (!HasAgreedDisclaimer)
            {
                if (new DisclaimerWindow { Owner = this }.ShowDialog() != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
                MarkDisclaimerAgreed();
            }

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

        // 登录页底部“免责条款”超链接：随时可查看（非首次运行，仅查看）
        private void Disclaimer_Click(object sender, RoutedEventArgs e)
        {
            new DisclaimerWindow(isFirstRun: false) { Owner = this }.ShowDialog();
        }

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

            // 主密码至少 8 位（官方 / WebDAV / 本地 通用）
            if (masterPwd.Length < 8)
            {
                MessageBox.Show("主密码长度至少为 8 位，请重新输入。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusLabel.Text = "主密码至少 8 位:";
                ClearMasterInputs();
                LoginBtn.IsEnabled = true;
                return;
            }

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

                // ========== WebDAV 模式：先校验配置完整，再同步 ==========
                if (ModeWebDAV.IsChecked == true)
                {
                    string url = WebDavUrlInput.Text.Trim();
                    string user = WebDavUserInput.Text.Trim();
                    string pwd = WebDavPwdInput.Password;

                    // WebDAV 模式必须填写完整配置，否则禁止登录使用
                    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
                    {
                        MessageBox.Show("WebDAV 同步模式必须填写服务器地址、账号和授权码。\r\n如不需要云同步，请选择「本地使用」。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusLabel.Text = "请填写完整的 WebDAV 配置:";
                        LoginBtn.IsEnabled = true;
                        return;
                    }

                    var config = CloudSyncManager.GetConfig() ?? new CloudConfig();
                    config.Mode = SyncMode.WebDAV;
                    config.Url = url.EndsWith("/") ? url : url + "/";
                    config.User = user;
                    config.Password = pwd;
                    config.IsEnabled = true;
                    CloudSyncManager.SaveConfig(config);

                    // 登录现有库前先同步一次
                    if (config.IsEnabled && File.Exists(_dataFile))
                    {
                        StatusLabel.Text = "同步中...";
                        await CloudSyncManager.DownloadLatestAsync(_dataFile);
                    }
                }

                // ========== 验证主密码（本地 / WebDAV 共用，使用对应模式的数据文件） ==========
                if (File.Exists(_dataFile))
                {
                    try
                    {
                        ProfileManager.LoadProfiles(masterPwd, _dataFile);
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

                // 保存到本地（加密）—— 官方模式隔离文件
                ProfileManager.SaveProfiles(profiles, masterPassword, _dataFile);
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
            // 本次登录选择的模式（决定主窗口状态栏显示与同步行为）
            SyncMode mode = SyncMode.Local;
            if (ModeOfficial.IsChecked == true) mode = SyncMode.Official;
            else if (ModeWebDAV.IsChecked == true) mode = SyncMode.WebDAV;

            MainWindow mainWindow = new MainWindow(pwd, _dataFile, mode);
            mainWindow.Show();
            this.Close();
        }
    }
}
