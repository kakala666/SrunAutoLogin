import AppKit
import ServiceManagement
import SwiftUI

/// 应用状态中枢：配置、掉线守护(对应 Windows 版 Core/Watchdog.cs)、日志、设置窗口。
/// 连续失败达到阈值后暂停自动登录(防止用错密码反复请求)，需手动「立即登录」或保存配置恢复。
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published var config = AppConfig.load()
    @Published var status = StatusSnapshot(state: .stopped, message: "守护已停止")
    @Published var log: [String] = []

    private let maxConsecutiveFailures = 3
    private var loop: Task<Void, Never>?
    private var busy = false
    private var consecutiveFail = 0
    private var autoPaused = false
    private var window: NSWindow?

    private init() {}

    func launch() {
        if !config.isConfigured {
            addLog("尚未配置账号密码，请填写。")
            showSettings()
        } else if config.watchdogEnabled {
            startWatchdog()
        }
    }

    // MARK: - 日志

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f
    }()

    func addLog(_ msg: String) {
        log.append("[\(Self.time.string(from: Date()))] \(msg)")
        if log.count > 300 { log.removeFirst(log.count - 300) }
    }

    // MARK: - 配置

    /// 窗口「保存并应用」：保存、开机自启、重启守护。
    func apply(_ c: AppConfig) {
        config = c
        if !config.save() { addLog("密码写入钥匙串失败，请重试。") }
        setAutoStart(c.autoStart)
        if c.watchdogEnabled { startWatchdog() } else { stopWatchdog() }
        addLog("配置已保存并应用。")
    }

    func toggleWatchdog() {
        var c = config
        c.watchdogEnabled.toggle()
        apply(c)
    }

    private func setAutoStart(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() }
            else if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
        } catch {
            addLog("设置开机自启失败：\(error.localizedDescription)")
        }
    }

    // MARK: - 守护

    func startWatchdog() {
        stopWatchdog()
        autoPaused = false
        consecutiveFail = 0
        addLog("守护已启动，每 \(config.pollIntervalSeconds) 秒检查一次。")
        loop = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(500))
            while !Task.isCancelled, let self {
                await self.tick()
                try? await Task.sleep(for: .seconds(self.config.pollIntervalSeconds))
            }
        }
    }

    func stopWatchdog() {
        loop?.cancel()
        loop = nil
        status = StatusSnapshot(state: .stopped, message: "守护已停止")
    }

    /// 手动立即登录(同时解除暂停)。
    func loginNow() {
        guard config.isConfigured else {
            addLog("请先填写手机号和密码。")
            showSettings()
            return
        }
        autoPaused = false
        consecutiveFail = 0
        Task { await doLogin() }
    }

    private func tick() async {
        guard !busy, config.watchdogEnabled else { return }
        busy = true
        defer { busy = false }
        let snap = await SrunClient(cfg: config).status()
        switch snap.state {
        case .online:
            consecutiveFail = 0
            autoPaused = false
            status = snap
        case .offline:
            if autoPaused {
                status = StatusSnapshot(state: .error, message: "已暂停自动登录(连续失败)。请检查账号密码，或点“立即登录”。")
            } else {
                await doLogin()
            }
        default:
            status = StatusSnapshot(state: .error, message: snap.message)
        }
    }

    private func doLogin() async {
        guard config.isConfigured else {
            status = StatusSnapshot(state: .error, message: "尚未配置手机号/密码")
            return
        }
        status = StatusSnapshot(state: .loggingIn, message: "正在登录…")
        var client = SrunClient(cfg: config)
        var r = await client.login()
        if !r.success, let a = await client.fetchNewAlphabet() {
            config.base64Alphabet = a
            config.save()
            addLog("检测到门户加密字典变更，已自动更新，重试登录…")
            client = SrunClient(cfg: config)
            r = await client.login()
        }
        if r.success {
            consecutiveFail = 0
            autoPaused = false
            addLog("登录成功：" + r.message)
            let s = await client.status()
            status = s.state == .online ? s : StatusSnapshot(state: .online, message: "登录成功")
        } else {
            consecutiveFail += 1
            addLog("登录失败：" + r.message)
            if consecutiveFail >= maxConsecutiveFailures {
                autoPaused = true
                status = StatusSnapshot(state: .error, message: "连续失败已暂停：" + r.message)
            } else {
                status = StatusSnapshot(state: .offline, message: "登录失败，将重试：" + r.message)
            }
        }
    }

    // MARK: - 设置窗口

    func showSettings() {
        if window == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView().environmentObject(self)))
            w.title = "校园网自动登录"
            w.styleMask = [.titled, .closable, .miniaturizable]
            w.isReleasedWhenClosed = false   // 关闭 = 隐藏，应用留在菜单栏
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}

extension SrunState {
    var text: String {
        switch self {
        case .online: "在线"
        case .offline: "掉线"
        case .loggingIn: "登录中"
        case .error: "异常"
        case .stopped: "已停止"
        }
    }

    /// 菜单栏图标(SF Symbols，模板色)。
    var icon: String {
        switch self {
        case .online: "wifi"
        case .offline: "wifi.slash"
        case .loggingIn: "arrow.triangle.2.circlepath"
        case .error: "wifi.exclamationmark"
        case .stopped: "pause.circle"
        }
    }
}
