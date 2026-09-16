using System;
using System.Security.Cryptography;
using System.Text;

namespace SrunAutoLogin.Crypto
{
    /// <summary>
    /// 深澜(SRUN)门户认证所需的加密算法集合。
    /// 全部逻辑均来自门户站点 all.min.js / Portal.js，并已用真实站点输出逐字节验证：
    ///  - 自定义 base64 字母表
    ///  - XXTEA(x_encode)  delta = 0x9E3779B9
    ///  - HMAC-MD5(key=token, msg=password)
    ///  - SHA1
    /// </summary>
    public static class SrunCrypto
    {
        // 门户自定义 base64 字母表（注意：不是标准 base64），已从站点 JS 提取并验证。
        // 作为内置默认值；若配置里存有门户更新后的字典，则优先用配置的。
        public const string DefaultBase64Alphabet =
            "LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA";
        private const char PadChar = '=';

        /// <summary>使用内置字母表做 base64 编码。</summary>
        public static string CustomBase64(byte[] data)
        {
            return CustomBase64(data, DefaultBase64Alphabet);
        }

        /// <summary>使用指定字母表做 base64 编码（字母表非法时回退内置）。</summary>
        public static string CustomBase64(byte[] data, string alphabet)
        {
            if (string.IsNullOrEmpty(alphabet) || alphabet.Length != 64)
                alphabet = DefaultBase64Alphabet;
            if (data == null || data.Length == 0) return string.Empty;
            string Base64Alphabet = alphabet;
            var sb = new StringBuilder();
            int imax = data.Length - data.Length % 3;
            int i = 0;
            for (; i < imax; i += 3)
            {
                int n = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                sb.Append(Base64Alphabet[(n >> 18) & 63]);
                sb.Append(Base64Alphabet[(n >> 12) & 63]);
                sb.Append(Base64Alphabet[(n >> 6) & 63]);
                sb.Append(Base64Alphabet[n & 63]);
            }
            int rem = data.Length - imax;
            if (rem == 1)
            {
                int n = data[i] << 16;
                sb.Append(Base64Alphabet[(n >> 18) & 63]);
                sb.Append(Base64Alphabet[(n >> 12) & 63]);
                sb.Append(PadChar);
                sb.Append(PadChar);
            }
            else if (rem == 2)
            {
                int n = (data[i] << 16) | (data[i + 1] << 8);
                sb.Append(Base64Alphabet[(n >> 18) & 63]);
                sb.Append(Base64Alphabet[(n >> 12) & 63]);
                sb.Append(Base64Alphabet[(n >> 6) & 63]);
                sb.Append(PadChar);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 门户里的 md5(password, token) 实为 HMAC-MD5，key=token，message=password。
        /// 返回小写十六进制。
        /// </summary>
        public static string HmacMd5Hex(string key, string message)
        {
            using (var hmac = new HMACMD5(Encoding.UTF8.GetBytes(key ?? string.Empty)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message ?? string.Empty));
                return ToHex(hash);
            }
        }

        /// <summary>SHA1，返回小写十六进制。</summary>
        public static string Sha1Hex(string input)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
                return ToHex(hash);
            }
        }

        /// <summary>
        /// 门户 info 字段：'{SRBX1}' + CustomBase64(XXTEA(json, token))。
        /// </summary>
        public static string EncodeInfo(string json, string token)
        {
            return EncodeInfo(json, token, DefaultBase64Alphabet);
        }

        /// <summary>用指定 base64 字母表生成 info 字段。</summary>
        public static string EncodeInfo(string json, string token, string alphabet)
        {
            byte[] enc = XxteaEncode(json, token);
            return "{SRBX1}" + CustomBase64(enc, alphabet);
        }

        /// <summary>
        /// XXTEA 加密（对应站点 JS 的 x_encode）。输入按字符码处理（与 JS charCodeAt 一致），
        /// 数据末尾追加长度字（对应 s(str,true)），密钥不追加长度（s(key,false)）。
        /// 返回编码后所有 32 位字的小端字节序（对应 l(v,false)）。
        /// </summary>
        public static byte[] XxteaEncode(string data, string key)
        {
            if (string.IsNullOrEmpty(data)) return new byte[0];

            uint[] v = ToUInt32Array(data, true);
            uint[] k = ToUInt32Array(key, false);
            if (k.Length < 4) Array.Resize(ref k, 4); // 不足 4 字用 0 补齐

            int n = v.Length - 1;
            uint z = v[n];
            uint y;
            const uint delta = 0x9E3779B9;
            uint d = 0;
            int q = (int)Math.Floor(6 + 52.0 / (n + 1));

            unchecked
            {
                while (q-- > 0)
                {
                    d += delta;
                    uint e = (d >> 2) & 3;
                    int p;
                    for (p = 0; p < n; p++)
                    {
                        y = v[p + 1];
                        uint m = (z >> 5) ^ (y << 2);
                        m += (y >> 3) ^ (z << 4) ^ (d ^ y);
                        m += k[(p & 3) ^ (int)e] ^ z;
                        z = v[p] = v[p] + m;
                    }
                    y = v[0];
                    uint m2 = (z >> 5) ^ (y << 2);
                    m2 += (y >> 3) ^ (z << 4) ^ (d ^ y);
                    m2 += k[(p & 3) ^ (int)e] ^ z;
                    z = v[n] = v[n] + m2;
                }
            }

            return WordsToBytes(v);
        }

        // 按字符码把字符串打包成 uint 数组（对应 JS 的 s(a,b)）。
        private static uint[] ToUInt32Array(string s, bool includeLength)
        {
            int c = s.Length;
            int wordCount = c == 0 ? 0 : ((c - 1) >> 2) + 1;
            uint[] v = new uint[includeLength ? wordCount + 1 : wordCount];
            for (int i = 0; i < c; i++)
            {
                v[i >> 2] |= (uint)s[i] << ((i & 3) * 8);
            }
            if (includeLength) v[wordCount] = (uint)c;
            return v;
        }

        // 把 uint 数组按小端展开为字节（对应 JS 的 l(v,false)）。
        private static byte[] WordsToBytes(uint[] v)
        {
            byte[] bytes = new byte[v.Length * 4];
            for (int i = 0; i < v.Length; i++)
            {
                bytes[i * 4] = (byte)(v[i] & 0xff);
                bytes[i * 4 + 1] = (byte)((v[i] >> 8) & 0xff);
                bytes[i * 4 + 2] = (byte)((v[i] >> 16) & 0xff);
                bytes[i * 4 + 3] = (byte)((v[i] >> 24) & 0xff);
            }
            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
