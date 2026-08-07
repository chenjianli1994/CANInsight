using System;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 校验和计算（LIN 1.x 经典 / LIN 2.x 增强）
    /// 增强 = 数据字节 + PID 累加后取反；经典 = 仅数据字节累加后取反
    /// </summary>
    public static class LinChecksum
    {
        /// <summary>
        /// 计算 LIN 校验和
        /// </summary>
        /// <param name="data">帧数据字节（0-8 字节）</param>
        /// <param name="pid">受保护帧 ID（0x00-0x3F）</param>
        /// <param name="enhanced">true=增强校验（含 PID，LIN 2.x 默认）；false=经典校验（LIN 1.x）</param>
        public static byte Calculate(byte[] data, byte pid, bool enhanced)
        {
            int sum = 0;
            if (data != null)
            {
                foreach (byte b in data) sum += b;
            }
            if (enhanced) sum += pid;
            return (byte)(~sum & 0xFF);
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
