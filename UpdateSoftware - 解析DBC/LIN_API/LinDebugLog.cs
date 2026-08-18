using System;

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
