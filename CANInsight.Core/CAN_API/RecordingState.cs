using System;

namespace PCAN_Client.CAN_API
{
    /// <summary>
    /// 录制状态（ASC/BLF）唯一真相源。GUI 的 Logging/LoggingSet 通过属性代理读写，
    /// Core 的接收链路直接读取，宿主可直接控制。
    /// </summary>
    public static class RecordingState
    {
        /// <summary>是否正在录制</summary>
        public static bool SaveFlag;

        /// <summary>录制文件类型：0=ASC，1=BLF</summary>
        public static int SaveFileTypeInt;

        /// <summary>当前 ASC 文件路径（空=未创建）</summary>
        public static string NowAscFileAddr = "";

        /// <summary>当前 BLF 文件路径字符串（空=未创建）</summary>
        public static string NowBlfFileAddrStr = "";

        /// <summary>录制通道过滤（GUI 绑定 LoggingSet.IsRecordChannel；null=全通道录制）</summary>
        public static Func<byte, bool> RecordChannelFilter;

        /// <summary>通道是否勾选录制</summary>
        public static bool IsRecordChannel(byte channel)
        {
            return RecordChannelFilter == null || RecordChannelFilter(channel);
        }
    }
}
