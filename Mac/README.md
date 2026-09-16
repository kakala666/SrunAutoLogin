# 校园网自动登录 · Mac 版

Windows 版(仓库根目录)的 macOS 移植：原生 Swift / SwiftUI **菜单栏应用**，无第三方依赖。
认证协议、掉线守护、加密字典自愈的行为与 Windows 版一致，见根目录 README「技术要点」。

- 常驻菜单栏，无 Dock 图标；点图标可「立即登录 / 暂停守护 / 打开设置 / 退出」
- 密码存 **钥匙串**(对应 Windows 的 DPAPI)，其余配置存 UserDefaults
- 开机自启走系统「登录项」(`SMAppService`)
- 通用二进制(Apple Silicon + Intel)，约 900KB；要求 macOS 14 及以上

## 使用

1. 双击 `SrunAutoLogin.app`，首次运行会自动打开设置窗口
2. 填写账号、密码、运营商，需要的话改门户地址 / ac_id / 检查间隔
3. 「保存并应用」；关闭窗口应用仍留在菜单栏
4. 首次打开未签名的 app 若被拦截：右键 → 打开；或执行
   `xattr -d com.apple.quarantine SrunAutoLogin.app`

## 构建

只需 Xcode Command Line Tools(`xcode-select --install`)，不需要完整 Xcode：

```sh
cd Mac
./build.sh          # 产物：build/SrunAutoLogin.app 与 build/SrunAutoLogin.dmg
```

分发用 `SrunAutoLogin.dmg`：打开后把 app 拖到旁边的 Applications 即可。

`swift run` 可直接以未打包形式运行调试；`SrunAutoLogin --selftest` 跑加密算法自检。

## 目录结构

```
Mac/
├─ Package.swift
├─ Info.plist                      LSUIElement、ATS 放行明文 HTTP
├─ build.sh                        双架构编译 + lipo + 组装 .app + ad-hoc 签名
└─ Sources/SrunAutoLogin/
   ├─ SrunCrypto.swift             HMAC-MD5 / SHA1 / XXTEA / 自定义 base64 + 自检向量
   ├─ SrunClient.swift             get_challenge → 登录 / rad_user_info / 字典自愈
   ├─ AppConfig.swift              UserDefaults + 钥匙串
   ├─ AppModel.swift               守护轮询、状态、日志、设置窗口
   ├─ SrunApp.swift                菜单栏 + 设置界面
   └─ main.swift                   入口(--selftest)
```
