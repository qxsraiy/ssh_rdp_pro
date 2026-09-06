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
        private static readonly string DataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.dat");

        // 保存配置（加密并写入文件）
        public static void SaveProfiles(List<SshProfile> profiles, string masterPassword)
        {
            // 1. 将列表序列化为 JSON 字符串
            string json = JsonConvert.SerializeObject(profiles);

            // 2. 加密 JSON
            string encryptedData = CryptoHelper.Encrypt(json, masterPassword);

            // 3. 写入文件
            File.WriteAllText(DataFilePath, encryptedData);
        }

        // 加载配置（读取并解密）
        public static List<SshProfile> LoadProfiles(string masterPassword)
        {
            if (!File.Exists(DataFilePath))
                throw new FileNotFoundException("本地数据文件不存在");

            string encryptedData = File.ReadAllText(DataFilePath);
            string json = CryptoHelper.Decrypt(encryptedData, masterPassword);
            return JsonConvert.DeserializeObject<List<SshProfile>>(json) ?? new List<SshProfile>();
        }
    }
}
