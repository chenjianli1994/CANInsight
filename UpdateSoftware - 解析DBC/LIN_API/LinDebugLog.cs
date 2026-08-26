using System;
using System.Diagnostics;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 调试日志：AppLog（统一运行日志 app_debug.log）的 LIN 专用门面。
    /// 保留原 API（Open/Write/Hex/LogPath），调用方无需改动；历史 lin_debug.log
    /// 由旧版本产生，新版本统一写入 app_debug.log。
    /// </summary>
    internal static class LinDebugLog
    {
        /// <summary>日志文件实际路径（null = 尚未启动）</summary>
        public static string LogPath
        {
            get { return AppLog.LogPath; }
        }

        /// <summary>启动日志线程（幂等；再次调用仅写会话分隔行）</summary>
        public static void Open(string sessionTag)
        {
            AppLog.Open(sessionTag);
        }

        /// <summary>
        /// 周期派发日志（限频，方案阶段 5）：同一通道 tick 日志按 1s 时间窗节流，
        /// 窗口内只写首条、其余计数、翻页时补一条抑制汇总（保留 start/stop/状态变化锚点，
        /// 避免 15ms 周期逐条写撑爆 app_debug.log——历史曾达 183MB）。
        /// </summary>
        private static readonly object _tickLogLock = new object();
        private static long _tickLogWindowStart;   // Stopwatch.GetTimestamp() 基准（无回绕）
        private static int _tickLogSuppressed;
        public static void WriteTick(byte channel, int idx, object slot, bool dispatched)
        {
            long now = Stopwatch.GetTimestamp(); // 单调时钟，无 24.9 天回绕（比 TickCount 掩码安全）
            lock (_tickLogLock)
            {
                if (now - _tickLogWindowStart >= Stopwatch.Frequency) // 1s 窗口（Frequency 为每秒计数）
                {
                    if (_tickLogSuppressed > 0)
                        AppLog.Write("[SCH] tick（限频）... 1s 内抑制 " + _tickLogSuppressed + " 条");
                    _tickLogWindowStart = now;
                    _tickLogSuppressed = 0;
                    AppLog.Write("[SCH] tick ch=" + channel + " idx=" + idx +
                        " slotMs=" + GetSlotMs(slot) + " dispatched=" + dispatched);
                }
                else
                {
                    _tickLogSuppressed++;
                }
            }
        }
        private static string GetSlotMs(object slot)
        {
            try
            {
                var f = slot.GetType().GetField("SlotMs");
                return f == null ? "?" : f.GetValue(slot).ToString();
            }
            catch { return "?"; }
        }

        /// <summary>写一行日志（线程安全，异步落盘）</summary>
        public static void Write(string msg)
        {
            AppLog.Write(msg);
        }

        /// <summary>字节数组转 Hex（len 截断；null 返回 "null"）</summary>
        public static string Hex(byte[] d, int len = -1)
        {
            return AppLog.Hex(d, len);
        }
    }
}
