import SwiftUI

/// 菜单栏应用(对应 Windows 版托盘)。无 Dock 图标，设置窗口按需打开。
struct SrunApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @ObservedObject private var model = AppModel.shared

    var body: some Scene {
        MenuBarExtra {
            Text(model.status.state.text + (model.status.message.isEmpty ? "" : "  " + model.status.message))
            Divider()
            Button("打开设置") { model.showSettings() }
            Button("立即登录") { model.loginNow() }
            Button(model.config.watchdogEnabled ? "暂停守护" : "启用守护") { model.toggleWatchdog() }
            Divider()
            Button("退出") { NSApp.terminate(nil) }
        } label: {
            Image(systemName: model.status.state.icon)
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)   // 未打包成 .app 直接运行时也不显示 Dock 图标
        AppModel.shared.launch()
    }
}

struct SettingsView: View {
    @EnvironmentObject private var model: AppModel
    @State private var draft = AppConfig()
    @State private var showPhoneAlert = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Circle().fill(dotColor).frame(width: 10, height: 10)
                Text(model.status.state.text).bold()
                Text(detail).font(.callout).foregroundStyle(.secondary).lineLimit(1)
            }

            GroupBox("账号设置") {
                Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 12, verticalSpacing: 8) {
                    GridRow { Text("手机号 / 账号"); TextField("", text: $draft.phone) }
                    GridRow { Text("密码"); SecureField("", text: $draft.password) }
                    GridRow {
                        Text("运营商")
                        Picker("", selection: $draft.domainSuffix) {
                            Text("中国电信 (@telecom)").tag("@telecom")
                            Text("中国联通 (@unicom)").tag("@unicom")
                            Text("中国移动 (@cmcc)").tag("@cmcc")
                            Text("无后缀").tag("")
                        }.labelsHidden()
                    }
                }.padding(6)
            }

            GroupBox("高级设置") {
                Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 12, verticalSpacing: 8) {
                    GridRow { Text("门户服务器地址"); TextField("", text: $draft.host) }
                    GridRow { Text("ac_id"); TextField("", text: $draft.acId) }
                    GridRow { Text("检查间隔(秒)"); TextField("", value: $draft.pollIntervalSeconds, format: .number) }
                }.padding(6)
            }

            GroupBox("行为") {
                VStack(alignment: .leading, spacing: 8) {
                    Toggle("启用掉线守护(自动重登)", isOn: $draft.watchdogEnabled)
                    Toggle("开机自启", isOn: $draft.autoStart)
                }.frame(maxWidth: .infinity, alignment: .leading).padding(6)
            }

            GroupBox("运行日志") {
                ScrollView {
                    Text(model.log.joined(separator: "\n"))
                        .font(.system(.caption, design: .monospaced))
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .defaultScrollAnchor(.bottom)
                .frame(height: 130)
            }

            HStack {
                Button("保存并应用") { save() }.keyboardShortcut(.defaultAction)
                Button("立即登录") { if save() { model.loginNow() } }
                Spacer()
            }
        }
        .textFieldStyle(.roundedBorder)
        .padding(16)
        .frame(width: 440)
        .onAppear { draft = model.config }
        .onChange(of: model.config.watchdogEnabled) { draft.watchdogEnabled = model.config.watchdogEnabled }
        .alert("请填写手机号 / 账号。", isPresented: $showPhoneAlert) {}
    }

    private var dotColor: Color {
        switch model.status.state {
        case .online: .green
        case .offline, .loggingIn: .orange
        case .error: .red
        case .stopped: .gray
        }
    }

    private var detail: String {
        let s = model.status
        var d = s.message
        if s.state == .online {
            d = "已在线"
            if !s.onlineIp.isEmpty { d += "  IP " + s.onlineIp }
            if s.usedBytes > 0 { d += "  已用 " + ByteCountFormatter.string(fromByteCount: s.usedBytes, countStyle: .binary) }
            if s.deviceCount > 0 { d += "  设备 \(s.deviceCount)" }
        }
        return d + "  (" + s.timestamp.formatted(date: .omitted, time: .standard) + ")"
    }

    @discardableResult
    private func save() -> Bool {
        draft.phone = draft.phone.trimmingCharacters(in: .whitespaces)
        guard !draft.phone.isEmpty else { showPhoneAlert = true; return false }
        draft.host = draft.host.trimmingCharacters(in: .whitespaces)
        if draft.host.isEmpty { draft.host = "172.31.255.18" }
        draft.acId = draft.acId.trimmingCharacters(in: .whitespaces)
        if draft.acId.isEmpty { draft.acId = "1" }
        draft.pollIntervalSeconds = max(1, draft.pollIntervalSeconds)
        draft.base64Alphabet = model.config.base64Alphabet   // 字典由自愈逻辑维护，不随表单覆盖
        model.apply(draft)
        return true
    }
}
