using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace PCAN_Client
{
    /// <summary>
    /// 通用运行日志（应用级）：后台线程异步写文件，不阻塞业务/收发线程。
    /// 文件 = 程序目录 app_debug.log（不可写时回退 %TEMP%\AutoPPT_app.log）。
    /// 记录关键生命周期/连接/配置变更/异常，供问题排查；本类是 LIN 调试日志
    /// （LinDebugLog）与各模块埋点的统一落盘通道。
    /// </summary>
    internal static class AppLog
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static Thread _thread;
        private static StreamWriter _writer;
        private static volatile bool _running;
        private static string _logPath;
        /// <summary>文件大小轮转（阶段 5）：单文件超过阈值即轮转备份，防止现场长时间运行日志无限增长</summary>
        private const long MaxFileBytes = 4L * 1024 * 1024;
        private static string _currentFile;
        private static long _writtenBytes;

        /// <summary>日志文件实际路径（null = 尚未启动）</summary>
        public static string LogPath
        {
            get { return _logPath; }
        }

        /// <summary>启动日志线程（幂等；再次调用仅写会话分隔行）</summary>
        public static void Open(string sessionTag)
        {
            try
            {
                if (_thread != null && _thread.IsAlive)
                {
                    Write("===== 新会话: " + sessionTag + " =====");
                    return;
                }
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "AppLog" };
                _thread.Start();
                _queue.Enqueue("===== 会话开始: " + sessionTag + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " =====");
            }
            catch { /* 日志失败不影响应用 */ }
        }

        /// <summary>关闭日志线程并冲刷剩余消息（应用退出时调用）</summary>
        public static void Close()
        {
            try
            {
                _running = false;
                if (_thread != null && _thread.IsAlive && _thread != Thread.CurrentThread)
                    _thread.Join(1000);
                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }
            }
            catch { }
        }

        /// <summary>常规信息日志</summary>
        public static void Info(string msg) { Write("INFO  ", msg); }

        /// <summary>警告日志（可恢复的异常状态）</summary>
        public static void Warn(string msg) { Write("WARN  ", msg); }

        /// <summary>错误日志（失败/异常）</summary>
        public static void Error(string msg) { Write("ERROR ", msg); }

        /// <summary>写一行日志（线程安全，异步落盘；调用方自行带 [分类] 前缀）</summary>
        public static void Write(string msg)
        {
            if (!_running) return;
            _queue.Enqueue("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg);
        }

        private static void Write(string level, string msg)
        {
            Write(level + msg);
        }

        /// <summary>字节数组转 Hex（len 截断；null 返回 "null"）</summary>
        public static string Hex(byte[] d, int len = -1)
        {
            if (d == null) return "null";
            if (len < 0 || len > d.Length) len = d.Length;
            var sb = new StringBuilder(len * 3);
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
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");
                try
                {
                    _writer = new StreamWriter(_logPath, true) { AutoFlush = true };
                }
                catch
                {
                    _logPath = Path.Combine(Path.GetTempPath(), "AutoPPT_app.log");
                    _writer = new StreamWriter(_logPath, true) { AutoFlush = true };
                }
                _currentFile = _logPath;
                try { _writtenBytes = new FileInfo(_logPath).Length; } catch { _writtenBytes = 0; }
                _writer.WriteLine("=======================================================");
                while (_running)
                {
                    string s;
                    if (_queue.TryDequeue(out s))
                    {
                        // 日志分级/轮转（阶段 5）：按行追加累计字节，超阈值先轮转再写，
                        // 防止现场长时间运行（周期日志限频 + 文件上限）日志无限增长。
                        _writtenBytes += s.Length + 2;
                        if (_writtenBytes >= MaxFileBytes)
                            Rotate();
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

        /// <summary>轮转当前日志：关闭写入器，备份为 app_debug.1.log（覆盖旧备份，保留最近一份），重新打开当前文件。</summary>
        private static void Rotate()
        {
            try
            {
                _writer.WriteLine("===== [轮转] 文件达到 " + MaxFileBytes + " 字节，备份为 " + Path.GetFileName(_currentFile) + ".1 =====");
                _writer.Flush();
                _writer.Dispose();
                string backup = _currentFile + ".1";
                try { File.Copy(_currentFile, backup, true); } catch { }
                try { File.Delete(_currentFile); } catch { }
                _writer = new StreamWriter(_currentFile, true) { AutoFlush = true };
                _writtenBytes = 0;
            }
            catch { }
        }
    }
}
