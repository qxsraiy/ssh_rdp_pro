using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace 云端管理
{
    public partial class MainWindow : Window
    {
        private List<SshProfile> _profiles = new List<SshProfile>();
        private string _masterPassword;
        private string _dataFile;
        private SyncMode _syncMode; // 本次登录选择的模式，决定状态栏显示与同步行为

        public MainWindow(string password, string dataFile, SyncMode syncMode)
        {
            InitializeComponent();
            _masterPassword = password;
            _dataFile = dataFile;
            _syncMode = syncMode;
            RefreshList();
            UpdateSyncStatus(true);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void RefreshList()
        {
            try
            {
                _profiles = ProfileManager.LoadProfiles(_masterPassword, _dataFile);
                ServerListBox.ItemsSource = null;
                ServerListBox.ItemsSource = _profiles;
            }
            catch (Exception ex)
            {
                if (!(ex is FileNotFoundException)) MessageBox.Show(ex.Message);
            }
        }

        // 底部状态栏显示当前同步状态
        private void SetSyncStatus(string text, bool isError = false)
        {
            if (SyncStatus == null) return;
            SyncStatus.Text = text;
            SyncStatus.Foreground = isError
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.Gray;
        }

        // 每次保存后调用：本地保存 + 官方云/WebDAV 同步（同步结果实时反馈）
        private async void SaveAndRefresh()
        {
            // 1. 保存到本地
            try
            {
                ProfileManager.SaveProfiles(_profiles, _masterPassword, _dataFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"本地保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            RefreshList();

            // 2. 按本次登录选择的模式同步（保存按钮点击后立即执行）
            var config = CloudSyncManager.GetConfig();
            if (_syncMode == SyncMode.Official && config != null && !string.IsNullOrEmpty(config.OfficialAccount))
            {
                SetSyncStatus("正在同步到官方云端...");
                try
                {
                    string token = OfficialCloudSyncManager.GetToken(config.OfficialAccount, _masterPassword);

                    // 1. 拉取云端现有记录，拿到 item_key 集合（用于删除本地已移除的条目）
                    var cloudRecords = await OfficialCloudSyncManager.PullAsync(token);
                    var cloudKeys = new HashSet<string>();
                    foreach (var r in cloudRecords) cloudKeys.Add(r.item_key);

                    // 2. 逐条推送：一台服务器 = 一条数据库记录（item_key = 服务器ID）
                    var localKeys = new HashSet<string>();
                    foreach (var p in _profiles)
                    {
                        string singleJson = Newtonsoft.Json.JsonConvert.SerializeObject(p);
                        string payload = CryptoHelper.Encrypt(singleJson, _masterPassword);
                        await OfficialCloudSyncManager.PushAsync(token, p.Id, payload);
                        localKeys.Add(p.Id);
                    }

                    // 3. 删除云端存在但本地已移除的条目（含旧版整包 "all"）
                    foreach (var key in cloudKeys)
                    {
                        if (!localKeys.Contains(key))
                        {
                            await OfficialCloudSyncManager.DeleteAsync(token, key);
                        }
                    }

                    SetSyncStatus($"已同步到官方云端 {_profiles.Count} 台设备  {DateTime.Now:HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    SetSyncStatus("官方云端同步失败", true);
                    MessageBox.Show($"同步到官方云端失败（本地已保存，稍后会自动重试）:\n{ex.Message}", "同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (_syncMode == SyncMode.WebDAV && config != null && config.IsEnabled)
            {
                SetSyncStatus("正在同步 WebDAV...");
                try
                {
                    await CloudSyncManager.UploadAndCleanAsync(_dataFile);
                    SetSyncStatus($"已同步到 WebDAV  {DateTime.Now:HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    SetSyncStatus("WebDAV 同步失败", true);
                    MessageBox.Show($"同步到 WebDAV 失败（本地已保存）:\n{ex.Message}", "同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (_syncMode == SyncMode.Official)
            {
                SetSyncStatus("官方云端：未配置账号");
            }
            else
            {
                SetSyncStatus("本地模式（未启用云同步）");
            }
        }

        private void UpdateSyncStatus(bool initial)
        {
            if (_syncMode == SyncMode.Official)
            {
                var config = CloudSyncManager.GetConfig();
                if (config != null && !string.IsNullOrEmpty(config.OfficialAccount))
                    SetSyncStatus($"官方云端账号: {config.OfficialAccount}");
                else
                    SetSyncStatus("官方云端：未配置账号");
            }
            else if (_syncMode == SyncMode.WebDAV)
            {
                var config = CloudSyncManager.GetConfig();
                SetSyncStatus(config != null && config.IsEnabled ? "WebDAV 同步已启用" : "WebDAV 同步未配置");
            }
            else
            {
                SetSyncStatus("本地模式（未启用云同步）");
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var win = new EditServerWindow();
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                _profiles.Add(win.ResultProfile);
                SaveAndRefresh();
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (ServerListBox.SelectedItem is SshProfile selected)
            {
                var win = new EditServerWindow(selected);
                win.Owner = this;
                if (win.ShowDialog() == true)
                {
                    var index = _profiles.IndexOf(selected);
                    _profiles[index] = win.ResultProfile;
                    SaveAndRefresh();
                }
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ServerListBox.SelectedItem is SshProfile selected)
            {
                if (MessageBox.Show($"确定删除 {selected.Name} 吗？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _profiles.Remove(selected);
                    SaveAndRefresh();
                }
            }
        }

        private void ServerListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ServerListBox.SelectedItem != null) Connect_Click(null, null);
        }

        private void Connect_Click(object sender, RoutedEventArgs? e)
        {
            if (ServerListBox.SelectedItem is SshProfile server)
            {
                // ===== RDP 协议：走 mstsc（凭据用 cmdkey 临时写入，5秒后自动删除） =====
                if (server.Protocol == "RDP")
                {
                    string target = (string.IsNullOrWhiteSpace(server.Port) || server.Port == "3389")
                        ? server.Host
                        : $"{server.Host}:{server.Port}";

                    // 1. 写入 RDP 凭据到 Windows 凭据管理器
                    if (!string.IsNullOrEmpty(server.SecretData))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmdkey.exe",
                            Arguments = $"/generic:TERMSRV/{target} /user:\"{server.Username}\" /pass:\"{server.SecretData}\"",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        })?.WaitForExit();
                    }

                    // 2. 启动远程桌面连接
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "mstsc.exe",
                        Arguments = "/v:" + target,
                        UseShellExecute = true
                    });

                    // 3. 5秒后自动删除凭据，防止密码残留
                    if (!string.IsNullOrEmpty(server.SecretData))
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(5000);
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "cmdkey.exe",
                                Arguments = "/delete:TERMSRV/" + target,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            });
                        });
                    }
                    return;
                }

                // ===== SSH 协议：原逻辑 =====
                string sshArgs = $"-p {server.Port} ";
                string tempKeyPath = "";
                string titleMsg = $"【{server.Name}】";

                if (server.AuthType == "Key")
                {
                    tempKeyPath = Path.Combine(Path.GetTempPath(), $"ssh_temp_{Guid.NewGuid()}");
                    File.WriteAllText(tempKeyPath, server.SecretData);
                    FixKeyPermissions(tempKeyPath);
                    sshArgs += $"-i \"{tempKeyPath}\" {server.Username}@{server.Host}";
                }
                else
                {
                    sshArgs += $"{server.Username}@{server.Host}";

                    // 核心修复：自动将密码复制到剪贴板
                    if (!string.IsNullOrEmpty(server.SecretData))
                    {
                        try
                        {
                            Clipboard.SetText(server.SecretData);
                            titleMsg += " (密码已复制，右键即可粘贴)";
                        }
                        catch { /* 忽略极低概率的剪贴板占用冲突 */ }
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k title {titleMsg} && echo 正在连接 {server.Host}... && ssh {sshArgs}",
                    UseShellExecute = true
                });
            }
        }

        private void FixKeyPermissions(string keyPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "icacls.exe", Arguments = $"\"{keyPath}\" /inheritance:r", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit();
                Process.Start(new ProcessStartInfo { FileName = "icacls.exe", Arguments = $"\"{keyPath}\" /grant \"{Environment.UserName}:F\"", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit();
            }
            catch { }
        }
    }
}