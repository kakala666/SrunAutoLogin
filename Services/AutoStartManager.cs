using System;
using Microsoft.Win32;

namespace SrunAutoLogin.Services
{
    /// <summary>开机自启：写 HKCU\...\Run。便携程序指向当前 exe 路径。</summary>
    internal static class AutoStartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SrunAutoLogin";

        private static string ExePath
        {
            get
            {
                // Costura 单文件下这是真实的 exe 路径
                return System.Reflection.Assembly.GetEntryAssembly().Location;
            }
        }

        public static void Apply(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue(ValueName, "\"" + ExePath + "\" --minimized");
                    else if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) { Logger.Log("设置开机自启失败：" + ex.Message); }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false))
                {
                    var v = key?.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(v)) return false;
                    return v.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }
    }
}
