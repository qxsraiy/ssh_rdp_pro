using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace 云端管理
{
    // 每台服务器的数据模型
    public class SshProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); // 唯一ID

        // 协议类型："SSH" 或 "RDP"（旧数据没有此字段时默认为 SSH）
        public string Protocol { get; set; } = "SSH";

        public string Name { get; set; }
        public string Host { get; set; }
        public string Port { get; set; } = "22";
        public string Username { get; set; }

        // AuthType 可以是 "Password" 或 "Key"
        public string AuthType { get; set; }

        // 如果是密码登录，这里存密码；如果是密钥登录，这里直接存【私钥文件的完整文本内容】
        public string SecretData { get; set; }
    }

    public static class ProfileManager
    {
        // 数据存储目录：用户文档\SshPro\（与 v1.2.0 定位一致）
        public static string GetDataDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "SshPro");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        // 默认数据文件（兼容旧调用：文档\SshPro\profiles.dat）
        private static readonly string DefaultFilePath = Path.Combine(GetDataDir(), "profiles.dat");

        // ---- 带路径的重载（登录窗口按模式传入隔离文件） ----

        // 保存配置（加密并写入指定文件）
        public static void SaveProfiles(List<SshProfile> profiles, string masterPassword, string filePath)
        {
            // 确保目录存在
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 1. 将列表序列化为 JSON 字符串
            string json = JsonConvert.SerializeObject(profiles);

            // 2. 加密 JSON
            string encryptedData = CryptoHelper.Encrypt(json, masterPassword);

            // 3. 写入文件
            File.WriteAllText(filePath, encryptedData);
        }

        // 加载配置（从指定文件读取并解密）
        public static List<SshProfile> LoadProfiles(string masterPassword, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("本地数据文件不存在");

            string encryptedData = File.ReadAllText(filePath);
            string json = CryptoHelper.Decrypt(encryptedData, masterPassword);
            return JsonConvert.DeserializeObject<List<SshProfile>>(json) ?? new List<SshProfile>();
        }

        // ---- 兼容旧调用（默认 profiles.dat，仅用于迁移/临时场景） ----
        public static void SaveProfiles(List<SshProfile> profiles, string masterPassword)
            => SaveProfiles(profiles, masterPassword, DefaultFilePath);

        public static List<SshProfile> LoadProfiles(string masterPassword)
            => LoadProfiles(masterPassword, DefaultFilePath);
    }
}