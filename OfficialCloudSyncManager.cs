using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace 云端管理
{
    // 云端单条数据记录
    public class CloudRecord
    {
        public string item_key { get; set; }
        public string payload { get; set; }
        public int version { get; set; }
        public string updated_at { get; set; }
    }

    // 官方云同步管理器（自建 PHP + MySQL 服务器）
    // 安全模型：
    //   token = HMAC-SHA256(主密码, 账号)，客户端现场计算，绝不落盘、绝不上传主密码
    //   服务器只存 SHA256(token)，用于写保护鉴权
    //   云端数据全部是主密码加密的密文，服务器永远看不到明文
    public static class OfficialCloudSyncManager
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // 服务器地址（内置固定，index.php 所在目录）
        public const string ServerUrl = "https://sshpro.xtay.cn/api";

        private static string Api(string action) =>
            ServerUrl.TrimEnd('/') + "/index.php?action=" + action;

        // 派生 token：token = HMAC-SHA256(主密码, 账号)，返回 64 位十六进制
        public static string GetToken(string account, string masterPassword)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(masterPassword));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(account));
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static async Task<JObject> PostAsync(string action, object body)
        {
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await Client.PostAsync(Api(action), content);
            resp.EnsureSuccessStatusCode();
            var text = await resp.Content.ReadAsStringAsync();
            var result = JObject.Parse(text);
            if (result["ok"] == null || (bool)result["ok"] != true)
                throw new Exception(result["msg"]?.ToString() ?? "云端请求失败");
            return result;
        }

        // 登录：账号不存在则自动注册并绑定 token_hash
        // token 由客户端用主密码现场派生，服务器只存 SHA256(token)
        public static async Task LoginAsync(string account, string token)
        {
            await PostAsync("login", new { account, token });
        }

        // 拉取该账号全部数据（payload 为主密码加密后的密文）
        public static async Task<List<CloudRecord>> PullAsync(string token)
        {
            var result = await PostAsync("pull", new { token });
            var list = result["data"]?.ToString();
            if (string.IsNullOrEmpty(list)) return new List<CloudRecord>();
            return JsonConvert.DeserializeObject<List<CloudRecord>>(list) ?? new List<CloudRecord>();
        }

        // 推送单条（新增或覆盖）
        public static async Task PushAsync(string token, string itemKey, string payload)
        {
            await PostAsync("push", new { token, item_key = itemKey, payload });
        }

        // 删除单条（软删除）
        public static async Task DeleteAsync(string token, string itemKey)
        {
            await PostAsync("delete", new { token, item_key = itemKey });
        }
    }
}