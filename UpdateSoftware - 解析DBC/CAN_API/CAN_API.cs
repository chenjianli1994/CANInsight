using PCAN_Client.DataLog;
using PCAN_Client.CAN_Data.blf;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;
using vxlapi_NET;

namespace PCAN_Client.CAN_API
{
    internal class CAN_API
    {
        static int receiveCnt = 0;
        //static StringBuilder Str = new StringBuilder();
        //static StringBuilder ascString = new StringBuilder();
        static Stopwatch sw = new Stopwatch();
        static ulong time_us_last = 0;
        static long startTime;
        static readonly long refersh = (long)(0.5 * 1000 * 1000); /* 100ms */

        // 添加批量处理相关的成员变量
        private static readonly StringBuilder _uiBuffer = new StringBuilder();
        private static readonly StringBuilder _ascBuffer = new StringBuilder();
        private static readonly object _bufferLock = new object();
        private static long _lastUIRefreshTime = 0;
        private static long _lastASCRefreshTime = 0;
        private const long UI_REFRESH_INTERVAL_MS = 100;   // UI刷新间隔50ms
        private const long ASC_REFRESH_INTERVAL_MS = 500; // ASC文件刷新间隔500ms
        private static long CanTransmitLastTicks = 0;
        internal static uint PendingTxId = 0xFFFFFFFF; // 用于标记本端发送的CAN ID
        public static Stopwatch stopwatch = new Stopwatch();
        public static bool logFileConvertToCsvFlag = false; 

        public static object _receiveCanDataLock = new object();

        internal static Boolean CanTransmit(uint ID, ushort len, byte[] data)
        {
            Boolean result = false;
            TPCANMsg tPCANMsg = new TPCANMsg();

            if (Main.pcanOpenFlag)
            {
                tPCANMsg.DATA = data;
                tPCANMsg.ID = ID;
                tPCANMsg.LEN = (byte)len;
                if (TPCANStatus.PCAN_ERROR_OK == Main.main.pCAN_API.PCAN_SendData(tPCANMsg))
                {
                    result = true;
                }
                else
                {
                    result = false;
                }

                lock (_receiveCanDataLock)
                {
                    PendingTxId = ID;
                    CanReceive(ID, len, data, (((ID > 0x7FF) ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD)), 0);
                    PendingTxId = 0xFFFFFFFF;
                }
            }
            else if (Main.canoeOpenFlag)
            {
                if (Main.main.canoe_API.CanoeCanTransmit(ID, len, data))
                {
                    result = true;
                }
                else
                {
                    result = false;
                }
                //lock (_receiveCanDataLock)
                //{
                //    PendingTxId = ID;
                //    //CanReceive(ID, len, data, (((ID > 0x7FF) ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD)), 0);
                //    PendingTxId = 0xFFFFFFFF;
                //}
            }
            else
            {
                result = false;
            }

            return result;
        }

        internal static void CanReceive(uint ID, ushort len, byte[] data, TPCANMessageType MSGTYPE, ulong timestamp2)
        {
            ulong time_us;
            TPCANMsg msg = new TPCANMsg();
            TPCANTimestamp timesamp = new TPCANTimestamp();
            sw.Start();
            string tempStr;
            FileInfo fileInfo = null;
            
            CanTransmitLastTicks = sw.ElapsedTicks;

            timestamp2 = (ulong)(stopwatch.ElapsedTicks / 10);
            if (!logFileConvertToCsvFlag)
            {
                BaseParamter.dbcHelper.CANDataDeal(ID, len, data, timestamp2);
            }
            // 记录实时CAN原始报文（用于ChartFrom保存BLF）
            ChartFrom.RecordRealtimeRawMessage(ID, data);
            if (Logging.SaveFlag && 1 == LoggingSet.SaveFileType_int)
            {
                Log.AddCanMessageToWrite(ID, data, timestamp2);
            }
            if (0 == startTime)
            {
                startTime = sw.ElapsedTicks;
            }
            else
            {
                /* empty */
            }
            try
            {
                msg.DATA = new byte[len];
                msg.ID = ID;
                msg.LEN = (byte)len;
                msg.MSGTYPE = MSGTYPE;
                Array.Copy(data, 0, msg.DATA, 0, len);

                // 更新报文显示记录
                bool isTx = (PendingTxId == ID);
                Main.main.RecordCanMessage(msg, timestamp2, isTx);

                {
                    time_us = timestamp2;

                    // 批量缓冲数据
                    lock (_bufferLock)
                    {
                        FormatAndAppendMessage(_uiBuffer, _ascBuffer, msg, time_us, time_us_last);
                    }

                    time_us_last = time_us;
                    receiveCnt++;

                    var currentTime = sw.ElapsedTicks;

                    // 批量刷新UI（降低频率）
                    if (currentTime - _lastUIRefreshTime >= UI_REFRESH_INTERVAL_MS * 10000) // 转换为ticks
                    {
                        FlushUIBuffer();
                        _lastUIRefreshTime = currentTime;
                    }

                    // 批量刷新ASC文件（降低频率）
                    if (currentTime - _lastASCRefreshTime >= ASC_REFRESH_INTERVAL_MS * 10000)
                    {
                        if (Logging.SaveFlag)
                        {
                            if ((0 == LoggingSet.SaveFileType_int) && _ascBuffer.Length > 0)
                            {
                                FlushASCBuffer();
                            }
                            else if (1 == LoggingSet.SaveFileType_int)
                            {
                                FlushBLFBuffer();
                            }
                        }
                        _lastASCRefreshTime = currentTime;
                    }
                }
            }
            catch { }
        }

        private static void FlushUIBuffer()
        {
            lock (_bufferLock)
            {
                if (_uiBuffer.Length > 0)
                {
                    string uiText = _uiBuffer.ToString();
                    _uiBuffer.Clear();

                    // 使用更高效的UI更新方式
                    Main.main.SafeReceiveBufferRefresh(uiText);
                }
            }
        }

        private static void FlushASCBuffer()
        {
            lock (_bufferLock)
            {
                if (!Logging.NowASCFileAddr.Equals("") && _ascBuffer.Length > 0)
                {
                    string ascText = _ascBuffer.ToString();
                    _ascBuffer.Clear();

                    // 异步写入文件，不阻塞接收线程
                    Task.Run(() => WriteASCFile(ascText));
                }
                else if (_ascBuffer.Length > 0)
                {
                    _ascBuffer.Clear();
                }
            }
        }

        internal static void FlushBLFBuffer()
        {
            // 异步后台写入，不阻塞CAN接收线程
            Task.Run(() =>
            {
                lock (_bufferLock)
                {
                    Log.ContinuousWriteWorker();

                    // 更新文件大小显示
                    try
                    {
                        if (!string.IsNullOrEmpty(Logging.NowBLFFileAddr_str) && File.Exists(Logging.NowBLFFileAddr_str))
                        {
                            FileInfo fileInfo = new FileInfo(Logging.NowBLFFileAddr_str);
                            float fileSizeMB = fileInfo.Length / 1048576.0f;
                            bool isOverflow = fileSizeMB >= LoggingSet.SaveSize;

                            if (null != Logging.log)
                            {
                                Logging.log.SafeFileSizeRefresh(isOverflow, (LoggingSet.SaveSize - fileSizeMB).ToString("F3"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"BLF文件大小检查失败: {ex.Message}");
                    }
                }
            });
        }

        private static void WriteASCFile(string ascText)
        {
            try
            {
                Log.saveLog(ascText, Logging.NowASCFileAddr);
                FileInfo fileInfo = new FileInfo(Logging.NowASCFileAddr);
                float fileSizeMB = fileInfo.Length / 1048576.0f;
                bool isOverflow = fileSizeMB >= LoggingSet.SaveSize;

                // 使用线程安全的UI更新
                Logging.log.SafeFileSizeRefresh(isOverflow, (LoggingSet.SaveSize - fileSizeMB).ToString("F3"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ASC文件写入失败: {ex.Message}");
            }
        }
        private static void FormatAndAppendMessage(StringBuilder str, StringBuilder ascString,
            TPCANMsg msg, ulong time_us, ulong time_us_last)
        {
            ulong timeDiff = time_us - time_us_last;

            // 构建公共部分
            string timestamp = $"{time_us / 1000000}.{time_us % 1000000 / 1000:D3}{time_us % 1000:D3}";
            string dataHex = string.Join(" ", msg.DATA.Take(msg.LEN).Select(b => b.ToString("X2")));
            string period = $"{timeDiff / 1000}.{timeDiff % 1000}ms";

            // 分别格式化输出
            if(Logging.SaveFlag && (0 == LoggingSet.SaveFileType_int))
            {
                ascString.AppendLine($"{timestamp} 1 {msg.ID:X2}             Rx    d {msg.LEN} {dataHex}");
            }
            str.AppendLine($"ID:0x{msg.ID:X2} 数据： {dataHex}  周期： {period}");
        }
    }
}
