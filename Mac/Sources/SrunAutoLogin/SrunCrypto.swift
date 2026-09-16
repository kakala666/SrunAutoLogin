import CryptoKit
import Foundation

/// 深澜门户认证所需算法，逐行对应 Windows 版 Crypto/SrunCrypto.cs。
enum SrunCrypto {
    /// 门户自定义 base64 字典(内置默认值；门户更换后由自愈逻辑写入配置)。
    static let defaultAlphabet = "LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA"
    static let stdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

    /// 门户的 md5(password, token) 实为 HMAC-MD5，key=token。小写十六进制。
    static func hmacMd5Hex(key: String, message: String) -> String {
        hex(HMAC<Insecure.MD5>.authenticationCode(for: Data(message.utf8), using: SymmetricKey(data: Data(key.utf8))))
    }

    static func sha1Hex(_ s: String) -> String {
        hex(Insecure.SHA1.hash(data: Data(s.utf8)))
    }

    /// info 字段：'{SRBX1}' + 自定义base64(XXTEA(json, token))
    static func encodeInfo(json: String, token: String, alphabet: String) -> String {
        "{SRBX1}" + customBase64(xxteaEncode(json, key: token), alphabet: alphabet)
    }

    /// 合法字典 = 标准 64 字符的一个排列。
    static func isAlphabet(_ s: String) -> Bool {
        s.count == 64 && Set(s) == Set(stdAlphabet)
    }

    static func customBase64(_ data: [UInt8], alphabet: String) -> String {
        let a = Array(isAlphabet(alphabet) ? alphabet : defaultAlphabet)
        var out = ""
        var i = 0
        while i + 3 <= data.count {
            let n = (Int(data[i]) << 16) | (Int(data[i + 1]) << 8) | Int(data[i + 2])
            out.append(a[(n >> 18) & 63]); out.append(a[(n >> 12) & 63])
            out.append(a[(n >> 6) & 63]); out.append(a[n & 63])
            i += 3
        }
        switch data.count - i {
        case 1:
            let n = Int(data[i]) << 16
            out.append(a[(n >> 18) & 63]); out.append(a[(n >> 12) & 63]); out += "=="
        case 2:
            let n = (Int(data[i]) << 16) | (Int(data[i + 1]) << 8)
            out.append(a[(n >> 18) & 63]); out.append(a[(n >> 12) & 63]); out.append(a[(n >> 6) & 63]); out += "="
        default: break
        }
        return out
    }

    /// XXTEA(对应站点 JS 的 x_encode)：数据末尾追加长度字，密钥不追加；输出各 32 位字的小端字节。
    static func xxteaEncode(_ data: String, key: String) -> [UInt8] {
        if data.isEmpty { return [] }
        var v = words(data, withLength: true)
        var k = words(key, withLength: false)
        while k.count < 4 { k.append(0) }
        let n = v.count - 1
        var z = v[n]
        var y: UInt32
        let delta: UInt32 = 0x9E37_79B9
        var d: UInt32 = 0
        var q = 6 + 52 / (n + 1)
        while q > 0 {
            q -= 1
            d &+= delta
            let e = Int((d >> 2) & 3)
            var p = 0
            while p < n {
                y = v[p + 1]
                var m = (z >> 5) ^ (y << 2)
                m &+= (y >> 3) ^ (z << 4) ^ (d ^ y)
                m &+= k[(p & 3) ^ e] ^ z
                v[p] &+= m
                z = v[p]
                p += 1
            }
            y = v[0]
            var m = (z >> 5) ^ (y << 2)
            m &+= (y >> 3) ^ (z << 4) ^ (d ^ y)
            m &+= k[(p & 3) ^ e] ^ z
            v[n] &+= m
            z = v[n]
        }
        return v.flatMap { w in (0..<4).map { UInt8((w >> (8 * $0)) & 0xff) } }
    }

    /// 按 UTF-16 码元打包成 32 位字(对应 JS charCodeAt / C# char)。
    private static func words(_ s: String, withLength: Bool) -> [UInt32] {
        let u = Array(s.utf16)
        let c = u.count
        let wc = c == 0 ? 0 : ((c - 1) >> 2) + 1
        var v = [UInt32](repeating: 0, count: withLength ? wc + 1 : wc)
        for i in 0..<c { v[i >> 2] |= UInt32(u[i]) << UInt32((i & 3) * 8) }
        if withLength { v[wc] = UInt32(c) }
        return v
    }

    private static func hex<S: Sequence>(_ bytes: S) -> String where S.Element == UInt8 {
        bytes.map { String(format: "%02x", $0) }.joined()
    }

    /// 自检：向量由 C# 版逐行翻译的 Python 参考实现生成。`SrunAutoLogin --selftest` 触发。
    static func selfTest() {
        let token = "8f2c1a7e5b3d9c0f4a6e2b8d1c3f5a7e9b0d2c4f6a8e1b3d5c7f9a0e2b4d6c8f"
        let json = #"{"username":"13800138000@telecom","password":"p@ss","ip":"10.1.2.3","acid":"1","enc_ver":"srun_bx1"}"#
        let info = encodeInfo(json: json, token: token, alphabet: defaultAlphabet)
        precondition(info == "{SRBX1}wHX/AQx164lmDOl7/MU3WBFHEBuA2Vq/6j6UitKlT3CNU3kA04vUOquq+vpN9vM1CuoG7OUC4/djwWiMvORXW0Fc8T7mpXo8BIIoNjmAsiGtSdSih/OhaHjpfR9SRtzYTe8ncimmoE9=", "xxtea/base64")
        let hmd5 = hmacMd5Hex(key: token, message: "p@ss")
        precondition(hmd5 == "1d5d1d0dec76441dfd471c734cd2e875", "hmac-md5")
        let chk = sha1Hex(token + "13800138000@telecom" + token + hmd5 + token + "1" + token + "10.1.2.3" + token + "200" + token + "1" + token + info)
        precondition(chk == "50ffc85547aa9517bae79424b4c33921b5a45907", "sha1")
        precondition(encodeInfo(json: "ab", token: "k", alphabet: defaultAlphabet) == "{SRBX1}4tKo++FtiC+=", "short key/data")
        precondition(customBase64([0, 1, 2], alphabet: defaultAlphabet) == "LLPo", "b64 pad0")
        precondition(customBase64([0, 1], alphabet: defaultAlphabet) == "LLP=", "b64 pad1")
        precondition(customBase64([0], alphabet: defaultAlphabet) == "LL==", "b64 pad2")
        precondition(isAlphabet(defaultAlphabet) && !isAlphabet(stdAlphabet + "A"), "isAlphabet")
        print("selftest OK")
    }
}
