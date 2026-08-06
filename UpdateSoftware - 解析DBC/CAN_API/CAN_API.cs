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
        private static readonly StringBuilder _ascBuffer = new StringBuilder();
        private static readonly object _bufferLock = new object();
        private static long _lastASCRefreshTime = 0;
        private const long ASC_REFRESH_INTERVAL_MS = 500; // ASC文件刷新间隔500ms
        private static long CanTransmitLastTicks = 0;
        internal static uint PendingTxId = 0xFFFFFFFF; // 用于标记本端发送的CAN ID
        public static Stopwatch stopwatch = new Stopwatch();
        public static bool logFileConvertToCsvFlag = false; 

        public static object _receiveCanDataLock = new object();

        /// <summary>
        /// 统一接收帧事件(ID,len,data,逻辑通道,isTx本端发送回灌)。
        /// 在 CanReceive 汇聚点转发,供脚本引擎(帧统计/触发启动)等订阅;
        /// 订阅者必须快速返回,禁止阻塞接收链路。
        /// 注:isTx按 PendingTxId==ID 判定,本端发送瞬间总线上同ID的真实RX帧会被误判为回灌(边缘场景,帧统计可能漏计1帧)。
        /// </summary>
        internal static event Action<uint, ushort, byte[], byte, bool> RawFrameReceived;

        internal static Boolean CanTransmit(uint ID, ushort len, byte[] data, byte channel = 1)
        {
            Boolean result = false;
            TPCANMsg tPCANMsg = new TPCANMsg();

            // 混合硬件：按逻辑通道配置的硬件类型路由；未指定类型时按现状兜底（PCAN优先）
            string hwType = BaseParamter.GetEffectiveHwTypeByChannel(channel);
            bool usePcan = hwType == BaseParamter.HwTypePcan || (hwType == "" && Main.pcanOpenFlag);
            bool useCanoe = !usePcan && (hwType == BaseParamter.HwTypeCanoe || hwType == "");

            if (usePcan && Main.pcanOpenFlag)
            {
                tPCANMsg.DATA = data;
                tPCANMsg.ID = ID;
                tPCANMsg.LEN = (byte)len;
                if (TPCANStatus.PCAN_ERROR_OK == Main.main.pCAN_API.PCAN_SendData(tPCANMsg, channel))
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
                    CanReceive(ID, len, data, (((ID > 0x7FF) ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD)), 0, channel);
                    PendingTxId = 0xFFFFFFFF;
                }
            }
            else if (useCanoe && Main.canoeOpenFlag)
            {
                if (Main.main.canoe_API.CanoeCanTransmit(ID, len, data, channel))
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

        internal static void CanReceive(uint ID, ushort len, byte[] data, TPCANMessageType MSGTYPE, ulong timestamp2, byte channel = 1)
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
                BaseParamter.dbcHelper.CANDataDeal(ID, len, data, timestamp2, channel);
            }
            // 脚本引擎等订阅者(帧统计/触发启动);本端发送回灌帧带isTx标记
            try { RawFrameReceived?.Invoke(ID, len, data, channel, PendingTxId == ID); }
            catch { /* 订阅者异常不影响接收链路 */ }
            // BLF/ASC落盘统一使用BLF通道号（逻辑通道号→BlfChannelId转换；回放时按BlfChannelId匹配解析）
            byte blfCh = BaseParamter.GetBlfIdByLogicChannel(channel);
            // 录制通道过滤（LoggingSet中勾选；未勾选通道的报文不落盘，UI/解析不受影响）
            bool recordCh = LoggingSet.IsRecordChannel(channel);
            // 记录实时CAN原始报文（用于ChartFrom保存BLF）；
            // 已落盘的帧（BLF录制开启且该通道勾选录制）跳过内存副本；其余帧（未录制/ASC/未勾选通道）必须入内存，否则手动导出永久缺失
            if (!(Logging.SaveFlag && 1 == LoggingSet.SaveFileType_int && recordCh))
            {
                ChartFrom.RecordRealtimeRawMessage(ID, data, blfCh);
            }
            if (Logging.SaveFlag && 1 == LoggingSet.SaveFileType_int && recordCh)
            {
                Log.AddCanMessageToWrite(ID, data, timestamp2, blfCh);
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
                Main.main.RecordCanMessage(msg, timestamp2, isTx, true, channel);

                {
                    time_us = timestamp2;

                    // 批量缓冲数据（ASC通道列写BLF通道号）
                    lock (_bufferLock)
                    {
                        FormatAndAppendMessage(_ascBuffer, msg, time_us, blfCh, recordCh);
                    }

                    time_us_last = time_us;
                    receiveCnt++;

                    var currentTime = sw.ElapsedTicks;

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
                }
            });
        }

        private static void WriteASCFile(string ascText)
        {
            try
            {
                Log.saveLog(ascText, Logging.NowASCFileAddr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ASC文件写入失败: {ex.Message}");
            }
        }
        /// <summary>ASC记录格式化：仅在需要落盘（recordCh且ASC文件类型）时构建字符串，其余场景零分配。
        /// 原UI缓冲管道（_uiBuffer）为死代码已移除</summary>
        private static void FormatAndAppendMessage(StringBuilder ascString,
            TPCANMsg msg, ulong time_us, byte channel = 1, bool recordCh = true)
        {
            // 条件提前：不记录ASC时直接返回，避免每帧无谓的字符串分配
            if (!(recordCh && Logging.SaveFlag && (0 == LoggingSet.SaveFileType_int)))
            {
                return;
            }

            // ASC通道列写真实逻辑通道号
            ascString.AppendLine($"{time_us / 1000000}.{time_us % 1000000 / 1000:D3}{time_us % 1000:D3} {channel} {msg.ID:X2}             Rx    d {msg.LEN} {string.Join(" ", msg.DATA.Take(msg.LEN).Select(b => b.ToString("X2")))}");
        }
    }
}
