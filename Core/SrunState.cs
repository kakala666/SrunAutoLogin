using System;

namespace SrunAutoLogin.Core
{
    /// <summary>守护/登录的整体状态。</summary>
    public enum SrunState
    {
        Unknown,    // 未知/未初始化
        Online,     // 在线
        Offline,    // 已掉线(将尝试重连)
        LoggingIn,  // 正在登录
        Error,      // 出错/已暂停(如连续失败、无法连门户)
        Stopped     // 守护已停止
    }

    /// <summary>一次状态查询的结果快照。</summary>
    public class StatusSnapshot
    {
        public SrunState State { get; set; }
        public string Message { get; set; }
        public string OnlineIp { get; set; }
        public string UserName { get; set; }
        public long UsedBytes { get; set; }
        public int DeviceCount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>登录尝试的结果。</summary>
    public class LoginResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public LoginResult(bool ok, string msg) { Success = ok; Message = msg; }
    }
}
