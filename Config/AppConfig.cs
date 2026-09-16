using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SrunAutoLogin.Services;

namespace SrunAutoLogin.Config
{
    /// <summary>
    /// 应用配置。持久化为 config.json(便携：优先放 exe 同目录，不可写则退回 %AppData%)。
    /// 密码用 Windows DPAPI(当前用户)加密后存储，不落明文。
    /// 采用零依赖的手写 JSON 读写(字段扁平)。
    /// </summary>
    public class AppConfig
    {
        // ——— 持久化字段 ———
        public string Phone { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public string DomainSuffix { get; set; } = "@telecom"; // 电信；可为 @unicom/@cmcc/""
        public string Host { get; set; } = "172.31.255.18";
        public string AcId { get; set; } = "1";
        public int PollIntervalSeconds { get; set; } = 20;
        public bool AutoStart { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool WatchdogEnabled { get; set; } = true;
        // 门户自定义 base64 字典。空 = 用内置；登录失败时会自动抓取门户 JS 更新此值。无 UI 入口。
        public string Base64Alphabet { get; set; } = "";

        /// <summary>明文密码(不序列化)。读写自动做 DPAPI 加解密。</summary>
        public string Password
        {
            get { return Decrypt(EncryptedPassword); }
            set { EncryptedPassword = Encrypt(value ?? ""); }
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(Phone) && !string.IsNullOrEmpty(Password); }
        }

        // ——— 加解密 ———
        private static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] enc = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch (Exception ex) { Logger.Log("密码加密失败：" + ex.Message); return ""; }
        }

        private static string Decrypt(string cipherBase64)
        {
            if (string.IsNullOrEmpty(cipherBase64)) return "";
            try
            {
                byte[] dec = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipherBase64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch
            {
                Logger.Log("已保存的密码无法解密(可能更换了系统账户)，请重新输入密码。");
                return "";
            }
        }

        // ——— 持久化 ———
        private static string _resolvedPath;

        private static string ResolvePath()
        {
            if (_resolvedPath != null) return _resolvedPath;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(exeDir, "config.json");
            try
            {
                string probe = Path.Combine(exeDir, ".wtest.tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                _resolvedPath = exePath;
            }
            catch
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrunAutoLogin");
                Directory.CreateDirectory(dir);
                _resolvedPath = Path.Combine(dir, "config.json");
            }
            return _resolvedPath;
        }

        public static AppConfig Load()
        {
            try
            {
                string path = ResolvePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = new AppConfig
                    {
                        Phone = GetStr(json, "Phone", ""),
                        EncryptedPassword = GetStr(json, "EncryptedPassword", ""),
                        DomainSuffix = GetStr(json, "DomainSuffix", "@telecom"),
                        Host = GetStr(json, "Host", "172.31.255.18"),
                        AcId = GetStr(json, "AcId", "1"),
                        PollIntervalSeconds = GetInt(json, "PollIntervalSeconds", 20),
                        AutoStart = GetBool(json, "AutoStart", false),
                        StartMinimized = GetBool(json, "StartMinimized", false),
                        WatchdogEnabled = GetBool(json, "WatchdogEnabled", true),
                        Base64Alphabet = GetStr(json, "Base64Alphabet", "")
                    };
                    if (cfg.PollIntervalSeconds < 1) cfg.PollIntervalSeconds = 1;
                    return cfg;
                }
            }
            catch (Exception ex) { Logger.Log("读取配置失败，使用默认值：" + ex.Message); }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                if (PollIntervalSeconds < 1) PollIntervalSeconds = 1;
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"Phone\": \"").Append(Esc(Phone)).Append("\",\n");
                sb.Append("  \"EncryptedPassword\": \"").Append(Esc(EncryptedPassword)).Append("\",\n");
                sb.Append("  \"DomainSuffix\": \"").Append(Esc(DomainSuffix)).Append("\",\n");
                sb.Append("  \"Host\": \"").Append(Esc(Host)).Append("\",\n");
                sb.Append("  \"AcId\": \"").Append(Esc(AcId)).Append("\",\n");
                sb.Append("  \"PollIntervalSeconds\": ").Append(PollIntervalSeconds).Append(",\n");
                sb.Append("  \"AutoStart\": ").Append(AutoStart ? "true" : "false").Append(",\n");
                sb.Append("  \"StartMinimized\": ").Append(StartMinimized ? "true" : "false").Append(",\n");
                sb.Append("  \"WatchdogEnabled\": ").Append(WatchdogEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"Base64Alphabet\": \"").Append(Esc(Base64Alphabet)).Append("\"\n");
                sb.Append("}\n");
                File.WriteAllText(ResolvePath(), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Logger.Log("保存配置失败：" + ex.Message); }
        }

        // ——— 极简 JSON 辅助 ———
        private static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (++i >= s.Length) break;
                char n = s[i];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code); i += 4;
                            }
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        private static string GetStr(string json, string name, string def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? Unesc(m.Groups[1].Value) : def;
        }

        private static int GetInt(string json, string name, int def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)");
            int v;
            return (m.Success && int.TryParse(m.Groups[1].Value, out v)) ? v : def;
        }

        private static bool GetBool(string json, string name, bool def)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(true|false)");
            return m.Success ? (m.Groups[1].Value == "true") : def;
        }
    }
}
