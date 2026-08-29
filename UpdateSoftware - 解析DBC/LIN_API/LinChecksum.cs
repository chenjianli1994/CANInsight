using System;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 校验和计算（LIN 1.x 经典 / LIN 2.x 增强）
    /// 增强 = 数据字节 + 受保护 PID（含奇偶校验位）累加并回卷进位后取反；经典 = 仅数据字节累加并回卷进位后取反
    /// </summary>
    public static class LinChecksum
    {
        /// <summary>
        /// 计算 LIN 校验和
        /// </summary>
        /// <param name="data">帧数据字节（0-8 字节）</param>
        /// <param name="pid">裸帧 ID（0x00-0x3F）；增强校验内部转换为受保护 PID</param>
        /// <param name="enhanced">true=增强校验（含 PID，LIN 2.x 默认）；false=经典校验（LIN 1.x）</param>
        public static byte Calculate(byte[] data, byte pid, bool enhanced)
        {
            int sum = 0;
            if (data != null)
            {
                foreach (byte b in data) sum += b;
            }
            if (enhanced) sum += GetProtectedId(pid);
            // LIN 使用带回卷进位的 8 位一补和，而不是直接截断高位。
            while (sum > 0xFF) sum = (sum & 0xFF) + (sum >> 8);
            return (byte)(~sum & 0xFF);
        }

        /// <summary>由裸帧 ID 计算 LIN 受保护 ID（ID6/ID7 为奇偶校验位）。委托 LinPidCodec，保证单一实现。</summary>
        public static byte GetProtectedId(byte pid)
        {
            return LinPidCodec.ToProtectedPid(pid);
        }

        /// <summary>
        /// 校验接收帧的校验和字节是否正确
        /// </summary>
        public static bool Verify(byte[] data, byte pid, byte receivedChecksum, bool enhanced)
        {
            return Calculate(data, pid, enhanced) == receivedChecksum;
        }

        /// <summary>
        /// LIN 帧最小传输时间（毫秒）：break(13) + sync(10) + pid(10) + data(10*DLC) + checksum(10) + 间隔(5) = 48 + 10*DLC bits
        /// </summary>
        public static double MinFrameTimeMs(int dlc, uint baudrate)
        {
            if (baudrate == 0) return 0;
            return (48 + 10 * dlc) * 1000.0 / baudrate;
        }
    }
}
