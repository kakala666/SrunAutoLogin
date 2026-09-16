using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using SrunAutoLogin.Config;
using SrunAutoLogin.Core;
using SrunAutoLogin.Services;

namespace SrunAutoLogin
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private TaskbarIcon _tray;
        private MainWindow _window;
        private MenuItem _watchdogItem;

        public AppConfig Config { get; private set; }
        public SrunClient Client { get; private set; }
        public Watchdog Watchdog { get; private set; }
        public bool IsExiting { get; private set; }

        public static App Instance { get { return (App)Current; } }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 单实例
            bool created;
            _mutex = new Mutex(true, "SrunAutoLogin_SingleInstance_9F1C", out created);
            if (!created)
            {
                MessageBox.Show("程序已在运行(见右下角托盘)。", "校园网自动登录",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Log("未处理异常：" + ex.Exception.Message);
                ex.Handled = true;
            };

            Config = AppConfig.Load();
            Client = new SrunClient(Config);
            Watchdog = new Watchdog(Client, () => Config);
            Watchdog.StatusChanged += OnStatusChanged;

            BuildTray();

            bool startHidden = Config.StartMinimized || HasArg(e.Args, "--minimized");
            _window = new MainWindow();
            if (startHidden)
            {
                NativeMethods.TrimWorkingSet();
                Logger.Log("已最小化到托盘启动。");
            }
            else
            {
                _window.Show();
            }

            if (Config.WatchdogEnabled)
            {
                if (Config.IsConfigured) Watchdog.Start();
                else Logger.Log("尚未配置账号密码，请打开设置填写。");
            }
        }

        private static bool HasArg(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ——— 托盘 ———
        private void BuildTray()
        {
            _tray = new TaskbarIcon();
            _tray.ToolTipText = "校园网自动登录";
            _tray.Icon = TrayIconFactory.Get(SrunState.Stopped);
            _tray.TrayMouseDoubleClick += (s, e) => ShowMainWindow();

            var menu = new ContextMenu();

            var open = new MenuItem { Header = "打开设置" };
            open.Click += (s, e) => ShowMainWindow();
            menu.Items.Add(open);

            var login = new MenuItem { Header = "立即登录" };
            login.Click += (s, e) => LoginNow();
            menu.Items.Add(login);

            _watchdogItem = new MenuItem { Header = Config.WatchdogEnabled ? "暂停守护" : "启用守护" };
            _watchdogItem.Click += (s, e) => ToggleWatchdog();
            menu.Items.Add(_watchdogItem);

            menu.Items.Add(new Separator());

            var exit = new MenuItem { Header = "退出" };
            exit.Click += (s, e) => ExitApplication();
            menu.Items.Add(exit);

            _tray.ContextMenu = menu;
        }

        private void OnStatusChanged(StatusSnapshot snap)
        {
            Dispatcher.Invoke(() =>
            {
                if (_tray != null)
                {
                    _tray.Icon = TrayIconFactory.Get(snap.State);
                    _tray.ToolTipText = "校园网自动登录 · " + StateText(snap.State) +
                        (string.IsNullOrEmpty(snap.Message) ? "" : "\n" + snap.Message);
                }
                if (_window != null) _window.UpdateStatus(snap);
            });
        }

        public static string StateText(SrunState s)
        {
            switch (s)
            {
                case SrunState.Online: return "在线";
                case SrunState.Offline: return "掉线";
                case SrunState.LoggingIn: return "登录中";
                case SrunState.Error: return "异常";
                default: return "已停止";
            }
        }

        // ——— 供窗口/菜单调用 ———
        public void ShowMainWindow()
        {
            if (_window == null) _window = new MainWindow();
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        public void LoginNow()
        {
            if (!Config.IsConfigured) { ShowMainWindow(); Logger.Log("请先填写手机号和密码。"); return; }
            Watchdog.ForceLoginAsync();
        }

        public void ToggleWatchdog()
        {
            Config.WatchdogEnabled = !Config.WatchdogEnabled;
            Config.Save();
            _watchdogItem.Header = Config.WatchdogEnabled ? "暂停守护" : "启用守护";
            if (Config.WatchdogEnabled) Watchdog.Start(); else Watchdog.Stop();
            if (_window != null) _window.RefreshFromConfig();
        }

        /// <summary>窗口保存配置后调用：应用新配置并重启守护。</summary>
        public void ApplyConfigChange()
        {
            Config.Save();
            Client.SetConfig(Config);
            AutoStartManager.Apply(Config.AutoStart);
            if (_watchdogItem != null)
                _watchdogItem.Header = Config.WatchdogEnabled ? "暂停守护" : "启用守护";
            if (Config.WatchdogEnabled) Watchdog.Start(); else Watchdog.Stop();
            Logger.Log("配置已保存并应用。");
        }

        public void ExitApplication()
        {
            IsExiting = true;
            try { Watchdog.Stop(); } catch { }
            try { if (_tray != null) _tray.Dispose(); } catch { }
            Shutdown();
        }
    }
}
