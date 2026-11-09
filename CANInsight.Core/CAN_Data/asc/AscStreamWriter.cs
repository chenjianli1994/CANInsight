using System;
using System.Globalization;
using System.IO;
using System.Text;
using PCAN_Client.CAN_Data.blf;

namespace PCAN_Client.CAN_Data.asc
{
    /// <summary>
    /// 流式写 ASC（Vector 文本格式）：经典帧写 <c>通道 ID Rx d 长度 数据</c>，
    /// CAN FD 帧写 <c>CANFD 通道 Rx ID brs esi dlc 长度 数据 标志/CRC/位定时</c>，
    /// 与 CANoe / ES582 记录的形态保持一致，写出的文件能被本工具与 CANoe 双向读取。
    /// </summary>
    public sealed class AscStreamWriter : IDisposable
    {
        /// <summary>CAN FD 行尾的帧标志/CRC/位定时列（ES582 与 CANoe 记录的形态，读端不解析这些列）</summary>
        private const string FdTail = "  111000 141 203040 80000000 46500250 4b280150";

        private readonly StreamWriter _writer;
        private readonly StringBuilder _batch = new StringBuilder(2000 * 80);
        private readonly int _batchLines;
        private bool _closed;

        /// <summary>已写入的帧数</summary>
        public long WrittenCount { get; private set; }

        /// <summary>创建失败原因（成功时为空串）</summary>
        public string LastError { get; private set; } = "";

        /// <param name="path">目标 ASC 路径</param>
        /// <param name="measurementStartTime">测量起始时间（写入 date 行）</param>
        /// <param name="batchLines">攒多少行落一次盘（默认 2000 行）</param>
        public AscStreamWriter(string path, DateTime measurementStartTime, int batchLines = 2000)
        {
            _batchLines = Math.Max(1, batchLines);
            try
            {
                _writer = new StreamWriter(path, false, new UTF8Encoding(false), 512 * 1024);
                _writer.NewLine = "\r\n";
                _writer.WriteLine("date " + measurementStartTime.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture));
                _writer.WriteLine("base hex  timestamps absolute");
                _writer.WriteLine("no internal events logged");
                _writer.WriteLine("// version 7.0.0");
            }
            catch (Exception ex)
            {
                LastError = "无法创建 ASC 文件：" + ex.Message;
            }
        }

        /// <summary>写一帧；timestampSeconds 为相对文件起始的秒数</summary>
        public bool Write(uint canId, byte[] data, double timestampSeconds, byte channel, bool isFd)
        {
            if (_writer == null) return false;
            try
            {
                byte[] bytes = data ?? new byte[0];
                // ASC 的 ID 列：扩展帧加 x 后缀（CANoe 约定），经典帧最多 8 字节、FD 帧最多 64 字节
                string idText = canId.ToString("X3", CultureInfo.InvariantCulture)
                              + (canId > 0x7FF ? "x" : "");
                string dataText = ToHex(bytes);

                if (isFd || bytes.Length > 8)
                {
                    int length = Math.Min(bytes.Length, 64);
                    int dlc = BLFAPI.GetFdDlcCode(length);
                    _batch.Append(FormatTimestamp(timestampSeconds)).Append(" CANFD ")
                          .Append(channel.ToString(CultureInfo.InvariantCulture)).Append(" Rx ")
                          .Append(idText).Append("   1 0 ")
                          .Append(dlc.ToString("x", CultureInfo.InvariantCulture)).Append(' ')
                          .Append(length.ToString(CultureInfo.InvariantCulture)).Append(' ')
                          .Append(dataText).Append(FdTail)
                          .Append("\r\n");
                }
                else
                {
                    _batch.Append(FormatTimestamp(timestampSeconds)).Append(' ')
                          .Append(channel.ToString(CultureInfo.InvariantCulture)).Append(' ')
                          .Append(idText).Append("             Rx    d ")
                          .Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(' ')
                          .Append(dataText)
                          .Append("\r\n");
                }

                WrittenCount++;
                if (WrittenCount % _batchLines == 0) FlushBatch();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            if (_writer == null) return;
            try
            {
                FlushBatch();
                _writer.WriteLine("End TriggerBlock");
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
                /* 关文件失败不再抛出：数据已尽量落盘 */
            }
        }

        private void FlushBatch()
        {
            if (_batch.Length == 0) return;
            _writer.Write(_batch.ToString());
            _writer.Flush();
            _batch.Clear();
        }

        /// <summary>ASC 时间戳：8 位整数秒 + 6 位小数（与 CANoe 记录一致）</summary>
        private static string FormatTimestamp(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long whole = (long)seconds;
            int micro = (int)Math.Round((seconds - whole) * 1000000.0);
            if (micro >= 1000000)
            {
                whole++;
                micro = 0;
            }
            return whole.ToString("D8", CultureInfo.InvariantCulture) + "."
                 + micro.ToString("D6", CultureInfo.InvariantCulture);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
