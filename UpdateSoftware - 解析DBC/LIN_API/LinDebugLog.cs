using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 调试日志（后台线程异步写文件，不阻塞总线收发线程）：
    /// 文件 = 程序目录 lin_debug.log（不可写时回退 %TEMP%\AutoPPT_LinDebug.log）。
    /// 调试期专用：排查连接/发送/错误帧判定问题后移除。
    /// </summary>
    internal static class LinDebugLog
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static Thread _thread;
        private static StreamWriter _writer;
        private static volatile bool _running;
        private static string _logPath;

        /// <summary>日志文件实际路径（null = 尚未启动）</summary>
        public static string LogPath
        {
            get { return _logPath; }
        }

        /// <summary>启动日志线程（幂等；再次调用仅写会话分隔行）</summary>
        public static void Open(string sessionTag)
        {
            if (_thread != null && _thread.IsAlive)
            {
                Write("===== 新会话: " + sessionTag + " =====");
                return;
            }
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "LinDebugLog" };
            _thread.Start();
            _queue.Enqueue("===== 会话开始: " + sessionTag + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " =====");
        }

        /// <summary>写一行日志（线程安全，异步落盘）</summary>
        public static void Write(string msg)
        {
            if (!_running) return;
            _queue.Enqueue("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg);
        }

        /// <summary>字节数组转 Hex（len 截断；null 返回 "null"）</summary>
        public static string Hex(byte[] d, int len = -1)
        {
            if (d == null) return "null";
            if (len < 0 || len > d.Length) len = d.Length;
            var sb = new System.Text.StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(d[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static void Loop()
        {
            try
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lin_debug.log");
                try
                {
                    _writer = new StreamWriter(_logPath, true) { AutoFlush = true };
                }
                catch
                {
                    _logPath = Path.Combine(Path.GetTempPath(), "AutoPPT_LinDebug.log");
                    _writer = new StreamWriter(_logPath, true) { AutoFlush = true };
                }
                _writer.WriteLine("=======================================================");
                while (_running)
                {
                    string s;
                    if (_queue.TryDequeue(out s))
                    {
                        _writer.WriteLine(s);
                        continue;
                    }
                    Thread.Sleep(10);
                }
                while (_queue.TryDequeue(out string s2)) _writer.WriteLine(s2);
                _writer.Flush();
            }
            catch { }
        }
    }
}
