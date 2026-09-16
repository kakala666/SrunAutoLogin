# 校园网自动登录 (SrunAutoLogin)

一个针对**深澜(SRUN)门户**校园网的、Windows 便携版一键登录 + 掉线自动重连小工具。

- 纯原生 WPF(.NET Framework 4.8)，**无浏览器内核**
- 打包为**单个便携 exe**(~140KB)，双击即用，朋友机器**零安装**(Win10/11 自带 4.8 运行时)
- 常驻右下角托盘；最小化到托盘时内存约 **7MB**
- 密码用 **Windows DPAPI(当前用户)** 加密存储，不落明文
- 掉线守护：定时查 `rad_user_info`，掉线自动重登
- **加密字典自愈**：门户若更换自定义 base64 字典，登录失败时自动抓取门户 JS 识别新字典、写入配置并重试，无需手动、无需更新软件

## 使用

1. 双击 `SrunAutoLogin.exe`
2. 填写：手机号/账号、密码、运营商后缀(电信=`@telecom`、联通=`@unicom`、移动=`@cmcc`、无则留空)
3. 高级设置：门户服务器地址(默认 `172.31.255.18`)、`ac_id`(默认 `1`)、检查间隔(默认 20 秒)
4. 行为：可开启「掉线守护」「开机自启」「启动即最小化到托盘」
5. 点「保存并应用」；点「立即登录」可手动触发一次登录
6. 关闭窗口 = 最小化到托盘;托盘右键可「立即登录 / 暂停守护 / 退出」

> 配置保存在 exe 同目录 `config.json`(便携);若该目录不可写则退回 `%AppData%\SrunAutoLogin\`。
> 密码经 DPAPI 加密，仅当前 Windows 账户可解密(换机器/换账户需重新输入)。

## 构建

需要 .NET SDK(已验证 9.0.x)。**不需要**安装 Visual Studio 或 .NET Framework Developer Pack
(net48 引用程序集由 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies` 提供)。

```powershell
dotnet build -c Release
# 产物: bin\Release\net48\SrunAutoLogin.exe  (单文件，可直接拷走)
```

## 技术要点(认证流程)

均来自门户站点 JS(`all.min.js` / `Portal.js`)并已逐字节验证：

1. `GET /cgi-bin/get_challenge` → 取 `token`(挑战值，60 秒有效) 与本机 `online_ip`
2. 计算登录字段：
   - `password` = `{MD5}` + **HMAC-MD5**(key=token, msg=明文密码)
   - `info` = `{SRBX1}` + 自定义base64(**XXTEA**(json, token))，json 键序固定为 `username,password,ip,acid,enc_ver`(`enc_ver=srun_bx1`)
   - `chksum` = **SHA1**(token+username+token+hmd5+token+ac_id+token+ip+token+n+token+type+token+info)
   - 常量 `n=200`、`type=1`
3. `GET /cgi-bin/srun_portal?action=login&...` 提交，`error=="ok"` 即成功
4. `GET /cgi-bin/rad_user_info` → 查在线状态(`not_online_error` = 掉线)

自定义 base64 字母表：`LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA`

## 许可证

[MIT](LICENSE)

## 目录结构

```
SrunAutoLogin/
├─ Crypto/SrunCrypto.cs      HMAC-MD5 / SHA1 / XXTEA / 自定义 base64(已验证)
├─ Core/SrunClient.cs        get_challenge → 登录 / rad_user_info 查询
├─ Core/Watchdog.cs          定时轮询 + 掉线自动重登(连续失败自动暂停)
├─ Core/SrunState.cs         状态与结果类型
├─ Config/AppConfig.cs       配置持久化 + DPAPI 密码加密(零依赖 JSON)
├─ Services/                 日志 / 托盘图标 / 开机自启 / DWM 深色标题栏与工作集回收
├─ Themes/Dark.xaml          现代深色样式
├─ App.xaml(.cs)             托盘、守护、生命周期
└─ MainWindow.xaml(.cs)      配置界面
```
