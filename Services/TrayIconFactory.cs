using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using SrunAutoLogin.Core;

namespace SrunAutoLogin.Services
{
    /// <summary>运行时生成不同状态的托盘图标(彩色圆点)，免去打包 .ico 资源。</summary>
    internal static class TrayIconFactory
    {
        private static readonly Dictionary<SrunState, Icon> _cache = new Dictionary<SrunState, Icon>();

        public static Icon Get(SrunState state)
        {
            lock (_cache)
            {
                Icon icon;
                if (_cache.TryGetValue(state, out icon)) return icon;
                icon = Build(ColorFor(state));
                _cache[state] = icon;
                return icon;
            }
        }

        private static Color ColorFor(SrunState state)
        {
            switch (state)
            {
                case SrunState.Online: return Color.FromArgb(34, 197, 94);    // 绿
                case SrunState.Offline:
                case SrunState.LoggingIn: return Color.FromArgb(245, 158, 11); // 琥珀
                case SrunState.Error: return Color.FromArgb(239, 68, 68);      // 红
                default: return Color.FromArgb(156, 163, 175);                 // 灰
            }
        }

        private static Icon Build(Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, 4, 4, 24, 24);
                    using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f))
                        g.DrawEllipse(pen, 4, 4, 24, 24);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
