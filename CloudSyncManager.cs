using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WebDav;

namespace 云端管理
{
    // 同步模式：本地使用 / WebDAV同步 / 官方云同步
    public enum SyncMode
    {
        Local = 0,
        WebDAV = 1,
        Official = 2
    }

    public class CloudConfig
    {
        // ---- 通用 ----
        public SyncMode Mode { get; set; } = SyncMode.Local;

        // ---- 官方云（自建服务器）----
        public string OfficialServerUrl { get; set; } = "";   // 如 https://你的域名/api
        public string OfficialAccount { get; set; } = "";     // 云端账号（token 由主密码现场派生，不落盘）

        // ---- WebDAV（保留原有字段）----
        public string Url { get; set; } = "";
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public bool IsEnabled { get; set; } = false;
    }

    public static class CloudSyncManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloud_config.dat"); // 后缀改为了 dat 隐藏 json 痕迹
        private static CloudConfig? _currentConfig;

        // --- 机器级本地加密逻辑 ---
        private static byte[] GetMachineKey()
        {
            // 绑定当前电脑名和用户名，离开这台电脑文件即失效
            string seed = Environment.MachineName + Environment.UserName + "SshProCloudSecurity";
            return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        }

        private static string Encrypt(string plainText)
        {
            using Aes aes = Aes.Create();
            aes.Key = GetMachineKey();
            aes.GenerateIV();
            using MemoryStream ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length); // 把 IV 写在头部
            using CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using StreamWriter sw = new StreamWriter(cs);
            sw.Write(plainText);
            sw.Close();
            return Convert.ToBase64String(ms.ToArray());
        }

        private static string Decrypt(string cipherText)
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);
            using Aes aes = Aes.Create();
            aes.Key = GetMachineKey();
            byte[] iv = new byte[aes.BlockSize / 8];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;
            using MemoryStream ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using StreamReader sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        // -------------------------

        public static CloudConfig GetConfig()
        {
            if (_currentConfig != null) return _currentConfig;

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string encryptedData = File.ReadAllText(ConfigPath);
                    string json = Decrypt(encryptedData);
                    _currentConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<CloudConfig>(json);
                }
                catch
                {
                    // 如果解密失败（比如换了电脑），当做没有配置处理
                    return new CloudConfig();
                }
            }
            return _currentConfig ??= new CloudConfig();
        }

        public static void SaveConfig(CloudConfig config)
        {
            _currentConfig = config;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
            string encryptedData = Encrypt(json);
            File.WriteAllText(ConfigPath, encryptedData);
        }

        // 新增：彻底销毁云端配置
        public static void ClearConfig()
        {
            _currentConfig = null;
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
            // 如果之前有明文的 json，顺手清理掉
            string oldJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloud_config.json");
            if (File.Exists(oldJsonPath)) File.Delete(oldJsonPath);
        }

        // 当前是否启用了官方云同步
        public static bool IsOfficialEnabled()
        {
            var config = GetConfig();
            return config.Mode == SyncMode.Official
                && !string.IsNullOrEmpty(config.OfficialServerUrl)
                && !string.IsNullOrEmpty(config.OfficialAccount);
        }

        private static WebDavClient CreateClient()
        {
            var config = GetConfig();
            var httpClientHandler = new HttpClientHandler { Credentials = new NetworkCredential(config.User, config.Password) };
            var httpClient = new HttpClient(httpClientHandler) { BaseAddress = new Uri(config.Url) };
            return new WebDavClient(httpClient);
        }

        private static async Task EnsureFolderExistsAsync(WebDavClient client)
        {
            var response = await client.Propfind("sshpro");
            if (!response.IsSuccessful) await client.Mkcol("sshpro");
        }

        public static async Task UploadAndCleanAsync(string localFilePath)
        {
            var config = GetConfig();
            if (!config.IsEnabled || !File.Exists(localFilePath)) return;

            var client = CreateClient();
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string remoteName = $"sshpro/profiles_{timestamp}.dat";

            try
            {
                await EnsureFolderExistsAsync(client);

                using (var fs = File.OpenRead(localFilePath))
                {
                    await client.PutFile(remoteName, fs);
                }

                var result = await client.Propfind("sshpro");
                if (result.IsSuccessful)
                {
                    var files = result.Resources
                        .Where(r => !r.IsCollection && r.DisplayName != null && r.DisplayName.StartsWith("profiles_") && r.DisplayName.EndsWith(".dat"))
                        .OrderByDescending(r => r.DisplayName)
                        .ToList();

                    if (files.Count > 3)
                    {
                        foreach (var oldFile in files.Skip(3))
                        {
                            if (oldFile.Uri != null) await client.Delete(oldFile.Uri);
                        }
                    }
                }
            }
            catch { /* 忽略网络错误 */ }
        }

        public static async Task<bool> DownloadLatestAsync(string localFilePath)
        {
            var config = GetConfig();
            if (!config.IsEnabled) return false;

            try
            {
                var client = CreateClient();
                await EnsureFolderExistsAsync(client);

                var result = await client.Propfind("sshpro");
                if (!result.IsSuccessful) return false;

                var latest = result.Resources
                    .Where(r => !r.IsCollection && r.DisplayName != null && r.DisplayName.StartsWith("profiles_") && r.DisplayName.EndsWith(".dat"))
                    .OrderByDescending(r => r.DisplayName)
                    .FirstOrDefault();

                if (latest != null && latest.Uri != null)
                {
                    var getRes = await client.GetRawFile(latest.Uri);
                    if (getRes.IsSuccessful && getRes.Stream != null)
                    {
                        using (var fs = File.Create(localFilePath))
                        {
                            await getRes.Stream.CopyToAsync(fs);
                        }
                        return true;
                    }
                }
            }
            catch { return false; }

            return false;
        }
    }
}
