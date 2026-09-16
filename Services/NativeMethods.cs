using System;
using System.Runtime.InteropServices;

namespace SrunAutoLogin.Services
{
    /// <summary>Win32/DWM 互操作：深色标题栏、圆角、回收工作集。</summary>
    internal static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        // DWMWA_USE_IMMERSIVE_DARK_MODE：让系统标题栏渲染为深色。
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Win10 1809/1903 旧值
        // DWMWA_WINDOW_CORNER_PREFERENCE：Win11 圆角。
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public static void EnableDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
            catch { /* 旧系统忽略 */ }
        }

        public static void EnableRoundedCorners(IntPtr hwnd)
        {
            try
            {
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { /* Win10 忽略 */ }
        }

        /// <summary>把工作集交还系统(最小化到托盘时调用，任务管理器内存显示会大幅下降)。</summary>
        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }
    }
}
