using System;
using System.Collections.Generic;

namespace SrunAutoLogin.Services
{
    /// <summary>极简日志：内存环形缓冲 + 事件通知(供 UI 显示)。</summary>
    public static class Logger
    {
        private const int MaxLines = 300;
        private static readonly object _lock = new object();
        private static readonly LinkedList<string> _lines = new LinkedList<string>();

        /// <summary>有新日志时触发，参数为格式化后的整行文本。</summary>
        public static event Action<string> LineAdded;

        public static void Log(string message)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message);
            lock (_lock)
            {
                _lines.AddLast(line);
                while (_lines.Count > MaxLines) _lines.RemoveFirst();
            }
            var handler = LineAdded;
            if (handler != null) handler(line);
        }

        /// <summary>取当前全部日志(用于窗口首次加载填充)。</summary>
        public static string[] Snapshot()
        {
            lock (_lock)
            {
                var arr = new string[_lines.Count];
                _lines.CopyTo(arr, 0);
                return arr;
            }
        }
    }
}
