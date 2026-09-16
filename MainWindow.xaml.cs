using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SrunAutoLogin.Config;
using SrunAutoLogin.Core;
using SrunAutoLogin.Services;

namespace SrunAutoLogin
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.EnableDarkTitleBar(hwnd);
                NativeMethods.EnableRoundedCorners(hwnd);
            };

            Loaded += (s, e) =>
            {
                RefreshFromConfig();
                foreach (var line in Logger.Snapshot()) AppendLog(line);
                Logger.LineAdded += OnLogLine;
                // 显示当前状态
                UpdateStatus(new StatusSnapshot
                {
                    State = App.Instance.Watchdog.State,
                    Message = ""
                });
            };

            Closing += OnClosing;
        }

        // ——— 配置 <-> 界面 ———
        public void RefreshFromConfig()
        {
            var cfg = App.Instance.Config;
            TxtPhone.Text = cfg.Phone;
            PwdBox.Password = cfg.Password;
            SelectDomain(cfg.DomainSuffix);
            TxtHost.Text = cfg.Host;
            TxtAcId.Text = cfg.AcId;
            TxtInterval.Text = cfg.PollIntervalSeconds.ToString();
            TglWatchdog.IsChecked = cfg.WatchdogEnabled;
            TglAutoStart.IsChecked = cfg.AutoStart;
            TglStartMin.IsChecked = cfg.StartMinimized;
        }

        private bool SaveFromForm()
        {
            var cfg = App.Instance.Config;
            string phone = (TxtPhone.Text ?? "").Trim();
            if (string.IsNullOrEmpty(phone))
            {
                MessageBox.Show("请填写手机号 / 账号。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            cfg.Phone = phone;
            cfg.Password = PwdBox.Password;
            cfg.DomainSuffix = SelectedDomain();
            cfg.Host = string.IsNullOrWhiteSpace(TxtHost.Text) ? "172.31.255.18" : TxtHost.Text.Trim();
            cfg.AcId = string.IsNullOrWhiteSpace(TxtAcId.Text) ? "1" : TxtAcId.Text.Trim();

            int iv;
            if (!int.TryParse((TxtInterval.Text ?? "").Trim(), out iv) || iv < 1) iv = 20;
            cfg.PollIntervalSeconds = iv;
            TxtInterval.Text = iv.ToString();

            cfg.WatchdogEnabled = TglWatchdog.IsChecked == true;
            cfg.AutoStart = TglAutoStart.IsChecked == true;
            cfg.StartMinimized = TglStartMin.IsChecked == true;
            return true;
        }

        private void SelectDomain(string suffix)
        {
            foreach (var obj in CmbDomain.Items)
            {
                var item = obj as System.Windows.Controls.ComboBoxItem;
                if (item != null && (string)item.Tag == suffix) { CmbDomain.SelectedItem = item; return; }
            }
            CmbDomain.SelectedIndex = 0; // 无匹配时默认电信
        }

        private string SelectedDomain()
        {
            var item = CmbDomain.SelectedItem as System.Windows.Controls.ComboBoxItem;
            return item != null ? (string)item.Tag : "@telecom";
        }

        // ——— 状态显示 ———
        public void UpdateStatus(StatusSnapshot snap)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateStatus(snap)); return; }

            StatusDot.Fill = new SolidColorBrush(DotColor(snap.State));
            TxtStatus.Text = App.StateText(snap.State);

            string detail = "";
            if (snap.State == SrunState.Online)
            {
                detail = "已在线";
                if (!string.IsNullOrEmpty(snap.OnlineIp)) detail += "  IP " + snap.OnlineIp;
                if (snap.UsedBytes > 0) detail += "  已用 " + FormatBytes(snap.UsedBytes);
                if (snap.DeviceCount > 0) detail += "  设备 " + snap.DeviceCount;
            }
            else if (!string.IsNullOrEmpty(snap.Message))
            {
                detail = snap.Message;
            }
            detail += "   (" + snap.Timestamp.ToString("HH:mm:ss") + ")";
            TxtDetail.Text = detail;
        }

        private static Color DotColor(SrunState s)
        {
            switch (s)
            {
                case SrunState.Online: return Color.FromRgb(34, 197, 94);
                case SrunState.Offline:
                case SrunState.LoggingIn: return Color.FromRgb(245, 158, 11);
                case SrunState.Error: return Color.FromRgb(239, 68, 68);
                default: return Color.FromRgb(156, 163, 175);
            }
        }

        private static string FormatBytes(long b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString("0.##") + " " + u[i];
        }

        // ——— 日志 ———
        private void OnLogLine(string line)
        {
            Dispatcher.BeginInvoke(new Action(() => AppendLog(line)));
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        // ——— 按钮 ———
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveFromForm()) return;
            App.Instance.ApplyConfigChange();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveFromForm()) return;
            App.Instance.ApplyConfigChange();
            App.Instance.LoginNow();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            HideToTray();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!App.Instance.IsExiting)
            {
                e.Cancel = true;   // 关闭按钮 = 最小化到托盘
                HideToTray();
            }
            else
            {
                Logger.LineAdded -= OnLogLine;
            }
        }

        private void HideToTray()
        {
            Hide();
            NativeMethods.TrimWorkingSet();
        }
    }
}
