import Foundation
import Security

/// 配置：普通字段存 UserDefaults，密码存钥匙串(对应 Windows 版的 DPAPI)。
struct AppConfig {
    var phone = ""
    var password = ""
    var domainSuffix = "@telecom"   // 电信；可为 @unicom / @cmcc / ""
    var host = "172.31.255.18"
    var acId = "1"
    var pollIntervalSeconds = 20
    var autoStart = false
    var watchdogEnabled = true
    /// 门户自定义 base64 字典。空 = 内置；登录失败时自动抓取门户 JS 更新。无 UI 入口。
    var base64Alphabet = ""

    var userName: String { phone + domainSuffix }
    var isConfigured: Bool { !phone.isEmpty && !password.isEmpty }
    var activeAlphabet: String { SrunCrypto.isAlphabet(base64Alphabet) ? base64Alphabet : SrunCrypto.defaultAlphabet }

    static func load() -> AppConfig {
        let d = UserDefaults.standard
        var c = AppConfig()
        c.phone = d.string(forKey: "Phone") ?? ""
        c.password = Keychain.read()
        c.domainSuffix = d.string(forKey: "DomainSuffix") ?? c.domainSuffix
        c.host = d.string(forKey: "Host") ?? c.host
        c.acId = d.string(forKey: "AcId") ?? c.acId
        c.pollIntervalSeconds = max(1, d.object(forKey: "PollIntervalSeconds") as? Int ?? c.pollIntervalSeconds)
        c.autoStart = d.object(forKey: "AutoStart") as? Bool ?? c.autoStart
        c.watchdogEnabled = d.object(forKey: "WatchdogEnabled") as? Bool ?? c.watchdogEnabled
        c.base64Alphabet = d.string(forKey: "Base64Alphabet") ?? ""
        return c
    }

    /// 返回密码是否成功写入钥匙串。
    @discardableResult
    func save() -> Bool {
        let d = UserDefaults.standard
        d.set(phone, forKey: "Phone")
        d.set(domainSuffix, forKey: "DomainSuffix")
        d.set(host, forKey: "Host")
        d.set(acId, forKey: "AcId")
        d.set(max(1, pollIntervalSeconds), forKey: "PollIntervalSeconds")
        d.set(autoStart, forKey: "AutoStart")
        d.set(watchdogEnabled, forKey: "WatchdogEnabled")
        d.set(base64Alphabet, forKey: "Base64Alphabet")
        return Keychain.write(password)
    }
}

/// 钥匙串里的一条通用密码：service=SrunAutoLogin。
enum Keychain {
    private static let query: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "SrunAutoLogin",
        kSecAttrAccount as String: "portal-password",
    ]

    static func read() -> String {
        var q = query
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return "" }
        return String(decoding: data, as: UTF8.self)
    }

    static func write(_ password: String) -> Bool {
        SecItemDelete(query as CFDictionary)
        if password.isEmpty { return true }
        var q = query
        q[kSecValueData as String] = Data(password.utf8)
        return SecItemAdd(q as CFDictionary, nil) == errSecSuccess
    }
}
