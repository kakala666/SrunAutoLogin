import Foundation

enum SrunState { case stopped, online, offline, loggingIn, error }

struct StatusSnapshot {
    var state: SrunState
    var message = ""
    var onlineIp = ""
    var usedBytes: Int64 = 0
    var deviceCount = 0
    var timestamp = Date()
}

struct LoginResult {
    let success: Bool
    let message: String
}

struct SrunError: LocalizedError {
    let msg: String
    init(_ m: String) { msg = m }
    var errorDescription: String? { msg }
}

/// 深澜门户认证客户端，对应 Windows 版 Core/SrunClient.cs。
///   1) get_challenge 取 token 与本机 IP
///   2) srun_portal 提交登录(密码 HMAC-MD5、info XXTEA、chksum SHA1)
///   3) rad_user_info 查询在线状态
struct SrunClient {
    let cfg: AppConfig

    private static let n = "200", loginType = "1", encVer = "srun_bx1"

    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 8
        c.timeoutIntervalForResource = 30
        c.connectionProxyDictionary = [:]   // 绕过系统代理，直连内网门户
        c.httpAdditionalHeaders = ["User-Agent": "SrunAutoLogin/1.0", "X-Requested-With": "XMLHttpRequest"]
        return URLSession(configuration: c)
    }()

    private func get(_ url: String) async throws -> String {
        guard let u = URL(string: url) else { throw URLError(.badURL) }
        let (data, _) = try await Self.session.data(from: u)
        return String(decoding: data, as: UTF8.self)
    }

    /// RFC 3986 严格转义，对应 C# Uri.EscapeDataString。
    private static let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
    private func esc(_ s: String) -> String { s.addingPercentEncoding(withAllowedCharacters: Self.unreserved) ?? s }

    /// 解析 JSONP 响应 jsonp({...}) 里的对象；解析失败返回空字典。
    private func json(_ body: String) -> [String: Any] {
        guard let a = body.firstIndex(of: "{"), let b = body.lastIndex(of: "}"), a <= b,
              let obj = try? JSONSerialization.jsonObject(with: Data(body[a...b].utf8)) as? [String: Any]
        else { return [:] }
        return obj
    }

    private func str(_ o: [String: Any], _ key: String) -> String {
        if let s = o[key] as? String { return s }
        if let n = o[key] as? NSNumber { return n.stringValue }
        return ""
    }

    private func jsonEsc(_ s: String) -> String {
        var out = ""
        for c in s.unicodeScalars {
            switch c {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            case "\u{8}": out += "\\b"
            case "\u{C}": out += "\\f"
            default:
                if c.value < 0x20 { out += String(format: "\\u%04x", c.value) } else { out.unicodeScalars.append(c) }
            }
        }
        return out
    }

    private func ms() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }

    // MARK: - 三个接口

    private func challenge() async throws -> (token: String, ip: String) {
        let body = try await get("http://\(cfg.host)/cgi-bin/get_challenge?callback=jsonp&username=\(esc(cfg.userName))&_=\(ms())")
        let o = json(body)
        let token = str(o, "challenge")
        var ip = str(o, "online_ip")
        if ip.isEmpty { ip = str(o, "client_ip") }
        guard !token.isEmpty else {
            let e = str(o, "error")
            throw SrunError("获取挑战值失败：" + (e.isEmpty ? "未知响应" : e))
        }
        return (token, ip)
    }

    func login() async -> LoginResult {
        do {
            let (token, ip) = try await challenge()
            let user = cfg.userName, pwd = cfg.password
            let acId = cfg.acId.isEmpty ? "1" : cfg.acId

            // 密码：{MD5} + HMAC-MD5(key=token, msg=password)
            let hmd5 = SrunCrypto.hmacMd5Hex(key: token, message: pwd)

            // info：{SRBX1} + base64(xxtea(json, token))，键序固定
            let j = "{\"username\":\"\(jsonEsc(user))\",\"password\":\"\(jsonEsc(pwd))\",\"ip\":\"\(jsonEsc(ip))\","
                + "\"acid\":\"\(jsonEsc(acId))\",\"enc_ver\":\"\(Self.encVer)\"}"
            let info = SrunCrypto.encodeInfo(json: j, token: token, alphabet: cfg.activeAlphabet)

            // chksum：SHA1(token+u + token+hmd5 + token+acid + token+ip + token+n + token+type + token+info)
            let chksum = SrunCrypto.sha1Hex(token + user + token + hmd5 + token + acId + token + ip
                + token + Self.n + token + Self.loginType + token + info)

            let url = "http://\(cfg.host)/cgi-bin/srun_portal?callback=jsonp&action=login"
                + "&username=\(esc(user))&password=\(esc("{MD5}" + hmd5))&os=\(esc("Mac OS"))&name=Macintosh&nas_ip=&double_stack=0"
                + "&chksum=\(esc(chksum))&info=\(esc(info))&ac_id=\(esc(acId))&ip=\(esc(ip))&n=\(Self.n)&type=\(Self.loginType)"
                + "&captchaVal=&ap_id=&ap_ip=&mac=&_=\(ms())"

            let o = json(try await get(url))
            let error = str(o, "error")
            if error == "ok" {
                let m = str(o, "suc_msg")
                return LoginResult(success: true, message: m.isEmpty ? "登录成功" : m)
            }
            if error.lowercased().contains("online") { return LoginResult(success: true, message: "已在线") }  // 已在线也视为成功
            let em = str(o, "error_msg")
            let msg = em.isEmpty ? error : em
            return LoginResult(success: false, message: msg.isEmpty ? "登录被拒绝" : msg)
        } catch {
            return LoginResult(success: false, message: "网络/请求错误：" + error.localizedDescription)
        }
    }

    func status() async -> StatusSnapshot {
        do {
            let body = try await get("http://\(cfg.host)/cgi-bin/rad_user_info?callback=jsonp&_=\(ms())")
            if body.lowercased().contains("not_online_error") { return StatusSnapshot(state: .offline, message: "未在线") }
            let o = json(body)
            let error = str(o, "error")
            guard error == "ok" else { return StatusSnapshot(state: .offline, message: error.isEmpty ? "未在线" : error) }
            var s = StatusSnapshot(state: .online, message: "在线")
            s.onlineIp = str(o, "online_ip")
            s.usedBytes = Int64(str(o, "sum_bytes")) ?? Int64(str(o, "used_bytes")) ?? 0
            s.deviceCount = Int(str(o, "online_device_total")) ?? 0
            return s
        } catch {
            return StatusSnapshot(state: .error, message: "无法连接门户：" + error.localizedDescription)
        }
    }

    // MARK: - 自定义 base64 字典的自愈

    /// 抓门户页面与其外链 JS，找出门户当前使用的自定义字典；与当前相同或没找到返回 nil。
    func fetchNewAlphabet() async -> String? {
        let acId = cfg.acId.isEmpty ? "1" : cfg.acId
        var scanned = Set<String>()
        for page in ["http://\(cfg.host)/srun_portal_pc?ac_id=\(acId)&theme=pro", "http://\(cfg.host)/"] {
            guard let html = try? await get(page) else { continue }
            if let a = Self.scanAlphabet(html) { return a == cfg.activeAlphabet ? nil : a }
            for url in Self.scriptUrls(html, host: cfg.host) {
                if !scanned.insert(url).inserted { continue }
                if scanned.count > 15 { break }
                guard let js = try? await get(url), let a = Self.scanAlphabet(js) else { continue }
                return a == cfg.activeAlphabet ? nil : a
            }
        }
        return nil
    }

    /// 在文本中找"非标准顺序"的 64 位 base64 排列。
    static func scanAlphabet(_ text: String) -> String? {
        for m in text.matches(of: #/["']([A-Za-z0-9+/]{64})["']/#) {
            let s = String(m.1)
            if s != SrunCrypto.stdAlphabet && SrunCrypto.isAlphabet(s) { return s }
        }
        return nil
    }

    static func scriptUrls(_ html: String, host: String) -> [String] {
        var list: [String] = []
        for m in html.matches(of: #/<script[^>]*\ssrc\s*=\s*["']([^"']+)["']/#.ignoresCase()) {
            let src = String(m.1)
            let url = src.hasPrefix("http://") || src.hasPrefix("https://") ? src
                : src.hasPrefix("//") ? "http:" + src
                : src.hasPrefix("/") ? "http://\(host)" + src
                : "http://\(host)/" + src
            if !list.contains(url) { list.append(url) }
        }
        // 优先扫描更可能含字典的脚本
        func score(_ u: String) -> Int {
            let u = u.lowercased()
            return (u.contains("base64") ? 5 : 0) + (u.contains("encode") ? 4 : 0) + (u.contains("all") ? 3 : 0)
                + (u.contains("min") ? 2 : 0) + (u.contains("lib") || u.contains("portal") ? 1 : 0)
        }
        return list.sorted { score($0) > score($1) }
    }
}
