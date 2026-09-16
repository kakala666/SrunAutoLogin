using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SrunAutoLogin.Config;
using SrunAutoLogin.Crypto;
using SrunAutoLogin.Services;

namespace SrunAutoLogin.Core
{
    /// <summary>
    /// 深澜门户认证客户端。实现三步：
    ///   1) get_challenge 取 token(挑战值) 与本机 IP
    ///   2) srun_portal 提交登录(密码 HMAC-MD5、info XXTEA、chksum SHA1)
    ///   3) rad_user_info 查询在线状态
    /// 全部走原生 HttpWebRequest(明文 HTTP + JSONP)。
    /// </summary>
    public class SrunClient
    {
        private const string N = "200";
        private const string Type = "1";
        private const string EncVer = "srun_bx1";
        private const string Os = "Windows 10";
        private const string DeviceName = "Windows";

        private volatile AppConfig _cfg;

        public SrunClient(AppConfig cfg) { _cfg = cfg; }
        public void SetConfig(AppConfig cfg) { _cfg = cfg; }

        private string UserName
        {
            get { return (_cfg.Phone ?? "") + (_cfg.DomainSuffix ?? ""); }
        }

        // ——— HTTP ———
        private static string HttpGet(string url, int timeoutMs = 8000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "SrunAutoLogin/1.0";
            req.Accept = "*/*";
            req.Headers["X-Requested-With"] = "XMLHttpRequest";
            req.KeepAlive = false;
            req.AllowAutoRedirect = false;
            req.Proxy = null; // 绕过系统代理，直连内网门户
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // ——— 解析辅助 ———
        private static string Field(string body, string name)
        {
            var m = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static long NumField(string body, string name)
        {
            var m = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(\\d+)");
            long v;
            return (m.Success && long.TryParse(m.Groups[1].Value, out v)) ? v : 0;
        }

        private static string JsonEsc(string s)
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

        private static long Ts() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        // ——— 三个接口 ———

        /// <summary>取挑战值。返回 token；out ip 为门户探测到的本机 IP。</summary>
        private string GetChallenge(out string ip)
        {
            string url = string.Format(
                "http://{0}/cgi-bin/get_challenge?callback=jsonp&username={1}&_={2}",
                _cfg.Host, Uri.EscapeDataString(UserName), Ts());
            string body = HttpGet(url);
            string token = Field(body, "challenge");
            ip = Field(body, "online_ip");
            if (string.IsNullOrEmpty(ip)) ip = Field(body, "client_ip");
            if (string.IsNullOrEmpty(token))
                throw new Exception("获取挑战值失败：" + (Field(body, "error") ?? "未知响应"));
            return token;
        }

        /// <summary>执行登录。</summary>
        public LoginResult Login()
        {
            try
            {
                string ip;
                string token = GetChallenge(out ip);
                string user = UserName;
                string pwd = _cfg.Password ?? "";
                string acId = string.IsNullOrEmpty(_cfg.AcId) ? "1" : _cfg.AcId;

                // 密码：{MD5} + HMAC-MD5(key=token, msg=password)
                string hmd5 = SrunCrypto.HmacMd5Hex(token, pwd);

                // info：{SRBX1} + base64(xxtea(json, token))，键顺序固定
                string json = "{\"username\":\"" + JsonEsc(user) +
                              "\",\"password\":\"" + JsonEsc(pwd) +
                              "\",\"ip\":\"" + JsonEsc(ip ?? "") +
                              "\",\"acid\":\"" + JsonEsc(acId) +
                              "\",\"enc_ver\":\"" + EncVer + "\"}";
                string info = SrunCrypto.EncodeInfo(json, token, ActiveAlphabet());

                // chksum：SHA1(token+u + token+hmd5 + token+acid + token+ip + token+n + token+type + token+info)
                string chk = token + user + token + hmd5 + token + acId + token + (ip ?? "") +
                             token + N + token + Type + token + info;
                string chksum = SrunCrypto.Sha1Hex(chk);

                string url = string.Format(
                    "http://{0}/cgi-bin/srun_portal?callback=jsonp&action=login" +
                    "&username={1}&password={2}&os={3}&name={4}&nas_ip=&double_stack=0" +
                    "&chksum={5}&info={6}&ac_id={7}&ip={8}&n={9}&type={10}" +
                    "&captchaVal=&ap_id=&ap_ip=&mac=&_={11}",
                    _cfg.Host,
                    Uri.EscapeDataString(user),
                    Uri.EscapeDataString("{MD5}" + hmd5),
                    Uri.EscapeDataString(Os),
                    Uri.EscapeDataString(DeviceName),
                    Uri.EscapeDataString(chksum),
                    Uri.EscapeDataString(info),
                    Uri.EscapeDataString(acId),
                    Uri.EscapeDataString(ip ?? ""),
                    N, Type, Ts());

                string body = HttpGet(url);
                string error = Field(body, "error") ?? "";
                string sucMsg = Field(body, "suc_msg");
                string errMsg = Field(body, "error_msg");

                if (error == "ok")
                    return new LoginResult(true, string.IsNullOrEmpty(sucMsg) ? "登录成功" : sucMsg);

                // 已经在线也视为成功
                if (error.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new LoginResult(true, "已在线");

                string msg = !string.IsNullOrEmpty(errMsg) ? errMsg : error;
                return new LoginResult(false, string.IsNullOrEmpty(msg) ? "登录被拒绝" : msg);
            }
            catch (Exception ex)
            {
                return new LoginResult(false, "网络/请求错误：" + ex.Message);
            }
        }

        // ——— 自定义 base64 字典的自愈 ———

        private const string StdB64 =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        /// <summary>当前生效字典：配置里合法则用配置，否则用内置。</summary>
        private string ActiveAlphabet()
        {
            string a = _cfg.Base64Alphabet;
            return IsBase64Permutation(a) ? a : SrunCrypto.DefaultBase64Alphabet;
        }

        // 合法的 base64 字母表 = 64 位、且正好是标准 64 字符的一个排列。
        private static bool IsBase64Permutation(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 64) return false;
            var set = new HashSet<char>();
            foreach (char c in s)
            {
                if (StdB64.IndexOf(c) < 0) return false;
                set.Add(c);
            }
            return set.Count == 64;
        }

        /// <summary>
        /// 抓取门户 JS，提取门户当前使用的自定义 base64 字典；
        /// 若与当前不同则写入配置并返回 true(供失败后重试一次)。
        /// </summary>
        public bool TryRefreshAlphabet()
        {
            try
            {
                string found = FetchAlphabetFromPortal();
                if (string.IsNullOrEmpty(found)) return false;
                if (found == ActiveAlphabet()) return false; // 没有变化
                _cfg.Base64Alphabet = found;
                _cfg.Save();
                Logger.Log("检测到门户加密字典变更，已自动更新。");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("自动更新字典失败：" + ex.Message);
                return false;
            }
        }

        private string FetchAlphabetFromPortal()
        {
            string acid = string.IsNullOrEmpty(_cfg.AcId) ? "1" : _cfg.AcId;
            string[] pages =
            {
                string.Format("http://{0}/srun_portal_pc?ac_id={1}&theme=pro", _cfg.Host, acid),
                string.Format("http://{0}/", _cfg.Host)
            };

            var scanned = new HashSet<string>();
            foreach (var page in pages)
            {
                string html;
                try { html = HttpGetFollow(page); }
                catch { continue; }

                string a = ScanForCustomAlphabet(html); // 内联脚本
                if (a != null) return a;

                foreach (var url in ExtractScriptUrls(html, _cfg.Host))
                {
                    if (!scanned.Add(url)) continue;
                    if (scanned.Count > 15) break;
                    string js;
                    try { js = HttpGetFollow(url); }
                    catch { continue; }
                    a = ScanForCustomAlphabet(js);
                    if (a != null) return a;
                }
            }
            return null;
        }

        // 在文本中找"非标准顺序"的 64 位 base64 排列，即门户自定义字典。
        private static string ScanForCustomAlphabet(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (Match m in Regex.Matches(text, "[\"']([A-Za-z0-9+/]{64})[\"']"))
            {
                string s = m.Groups[1].Value;
                if (s != StdB64 && IsBase64Permutation(s)) return s;
            }
            return null;
        }

        private static List<string> ExtractScriptUrls(string html, string host)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(html,
                "<script[^>]*\\ssrc\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                string url = ResolveUrl(m.Groups[1].Value, host);
                if (url != null && !list.Contains(url)) list.Add(url);
            }
            // 优先扫描更可能含字典的脚本
            list.Sort((x, y) => Score(y) - Score(x));
            return list;
        }

        private static int Score(string url)
        {
            string u = url.ToLowerInvariant();
            int s = 0;
            if (u.Contains("base64")) s += 5;
            if (u.Contains("encode")) s += 4;
            if (u.Contains("all")) s += 3;
            if (u.Contains("min")) s += 2;
            if (u.Contains("lib") || u.Contains("portal")) s += 1;
            return s;
        }

        private static string ResolveUrl(string src, string host)
        {
            if (string.IsNullOrEmpty(src)) return null;
            if (src.StartsWith("http://") || src.StartsWith("https://")) return src;
            if (src.StartsWith("//")) return "http:" + src;
            if (src.StartsWith("/")) return "http://" + host + src;
            return "http://" + host + "/" + src;
        }

        private static string HttpGetFollow(string url, int timeoutMs = 8000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "SrunAutoLogin/1.0";
            req.Accept = "*/*";
            req.AllowAutoRedirect = true;
            req.KeepAlive = false;
            req.Proxy = null;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        /// <summary>查询当前在线状态。</summary>
        public StatusSnapshot GetStatus()
        {
            var snap = new StatusSnapshot();
            try
            {
                string url = string.Format(
                    "http://{0}/cgi-bin/rad_user_info?callback=jsonp&_={1}", _cfg.Host, Ts());
                string body = HttpGet(url);

                if (body.IndexOf("not_online_error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    snap.State = SrunState.Offline;
                    snap.Message = "未在线";
                    return snap;
                }

                string error = Field(body, "error");
                if (error == "ok")
                {
                    snap.State = SrunState.Online;
                    snap.OnlineIp = Field(body, "online_ip");
                    snap.UserName = Field(body, "user_name");
                    snap.UsedBytes = NumField(body, "sum_bytes");
                    if (snap.UsedBytes == 0) snap.UsedBytes = NumField(body, "used_bytes");
                    string devs = Field(body, "online_device_total");
                    int d; if (int.TryParse(devs, out d)) snap.DeviceCount = d;
                    snap.Message = "在线";
                    return snap;
                }

                snap.State = SrunState.Offline;
                snap.Message = string.IsNullOrEmpty(error) ? "未在线" : error;
                return snap;
            }
            catch (Exception ex)
            {
                snap.State = SrunState.Unknown;
                snap.Message = "无法连接门户：" + ex.Message;
                return snap;
            }
        }
    }
}
