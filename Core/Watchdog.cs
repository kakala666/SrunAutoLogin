using System;
using System.Threading;
using System.Threading.Tasks;
using SrunAutoLogin.Config;
using SrunAutoLogin.Services;

namespace SrunAutoLogin.Core
{
    /// <summary>
    /// 掉线守护：按间隔轮询 rad_user_info，掉线则自动重登。
    /// 连续失败达到阈值后暂停自动登录(防止用错密码反复请求)，需手动"立即登录"或保存配置恢复。
    /// </summary>
    public class Watchdog
    {
        private const int MaxConsecutiveFailures = 3;

        private readonly SrunClient _client;
        private readonly Func<AppConfig> _config;
        private Timer _timer;
        private int _running;          // 防重入
        private int _consecutiveFail;
        private bool _autoPaused;

        public SrunState State { get; private set; } = SrunState.Stopped;

        /// <summary>状态变化时触发(在线程池线程上，UI 侧需自行 marshal)。</summary>
        public event Action<StatusSnapshot> StatusChanged;

        public Watchdog(SrunClient client, Func<AppConfig> config)
        {
            _client = client;
            _config = config;
        }

        public void Start()
        {
            Stop();
            _autoPaused = false;
            _consecutiveFail = 0;
            int intervalMs = Math.Max(1, _config().PollIntervalSeconds) * 1000;
            _timer = new Timer(Tick, null, 500, intervalMs);
            Logger.Log("守护已启动，每 " + (intervalMs / 1000) + " 秒检查一次。");
        }

        public void Stop()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
            Raise(SrunState.Stopped, "守护已停止");
        }

        /// <summary>手动立即登录(同时解除暂停)。</summary>
        public void ForceLoginAsync()
        {
            _autoPaused = false;
            _consecutiveFail = 0;
            Task.Run(() => DoLogin(true));
        }

        private void Tick(object _)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                var cfg = _config();
                if (!cfg.WatchdogEnabled) return;

                var snap = _client.GetStatus();
                switch (snap.State)
                {
                    case SrunState.Online:
                        _consecutiveFail = 0;
                        _autoPaused = false;
                        Raise(snap);
                        break;

                    case SrunState.Offline:
                        if (_autoPaused)
                        {
                            Raise(SrunState.Error, "已暂停自动登录(连续失败)。请检查账号密码，或点“立即登录”。");
                            break;
                        }
                        DoLogin(false);
                        break;

                    default: // Unknown：网络/门户不可达
                        Raise(SrunState.Error, snap.Message ?? "无法连接门户");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("守护异常：" + ex.Message);
                Raise(SrunState.Error, ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private void DoLogin(bool manual)
        {
            var cfg = _config();
            if (!cfg.IsConfigured)
            {
                Raise(SrunState.Error, "尚未配置手机号/密码");
                return;
            }

            Raise(SrunState.LoggingIn, "正在登录…");
            var r = _client.Login();
            if (!r.Success && _client.TryRefreshAlphabet())
            {
                Logger.Log("已更新加密字典，重试登录…");
                r = _client.Login();
            }
            if (r.Success)
            {
                _consecutiveFail = 0;
                _autoPaused = false;
                Logger.Log("登录成功：" + r.Message);
                var s = _client.GetStatus();
                if (s.State == SrunState.Online) Raise(s);
                else Raise(SrunState.Online, "登录成功");
            }
            else
            {
                _consecutiveFail++;
                Logger.Log("登录失败：" + r.Message);
                if (_consecutiveFail >= MaxConsecutiveFailures)
                {
                    _autoPaused = true;
                    Raise(SrunState.Error, "连续失败已暂停：" + r.Message);
                }
                else
                {
                    Raise(SrunState.Offline, "登录失败，将重试：" + r.Message);
                }
            }
        }

        private void Raise(SrunState state, string message)
        {
            Raise(new StatusSnapshot { State = state, Message = message });
        }

        private void Raise(StatusSnapshot snap)
        {
            State = snap.State;
            var h = StatusChanged;
            if (h != null) h(snap);
        }
    }
}
