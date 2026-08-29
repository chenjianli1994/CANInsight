using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PcanBackend = PCAN_Client.PCAN_API.PCAN_API;
using CanoeBackend = PCAN_Client.Canoe_API.CanOe_API;

namespace PCAN_Client.CAN_API
{
    /// <summary>
    /// 统一 CAN 收发汇聚点：发送按通道硬件类型路由 PCAN/CANoe，接收汇聚后经事件分发给宿主。
    /// 宿主（GUI/Service）负责订阅事件与注入录制状态，本类不持有任何 UI 引用。
    /// </summary>
    public static class CAN_API
    {
        static int receiveCnt = 0;
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

        // === 后端实例与连接状态（GUI 的 Main 静态字段代理到此处，Service 直接使用） ===
        // 懒初始化：XLDriver 构造会加载 vxlapi 原生库，静态初始化即构造会导致无 Vector 驱动的环境启动崩溃
        private static PcanBackend _pcanApi;
        private static CanoeBackend _canoeApi;
        public static PcanBackend PcanApi => _pcanApi ?? (_pcanApi = new PcanBackend());
        public static CanoeBackend CanoeApi => _canoeApi ?? (_canoeApi = new CanoeBackend());
        public static bool PcanOpenFlag;
        public static bool CanoeOpenFlag;

        // === 宿主事件（GUI/Service 订阅；订阅者必须快速返回，禁止阻塞接收链路） ===

        /// <summary>
        /// 统一接收帧事件(ID,len,data,逻辑通道,isTx本端发送回灌)。
        /// 供脚本引擎(帧统计/触发启动)等订阅;订阅者必须快速返回,禁止阻塞接收链路。
        /// 注:isTx按 PendingTxId==ID 判定,本端发送瞬间总线上同ID的真实RX帧会被误判为回灌(边缘场景,帧统计可能漏计1帧)。
        /// </summary>
        public static event Action<uint, ushort, byte[], byte, bool> RawFrameReceived;

        /// <summary>接收帧上屏事件（GUI 订阅 → Main.RecordCanMessage；Service 订阅 → 帧环形缓冲）</summary>
        public static event Action<TPCANMsg, ulong, bool, byte> FrameReceived;

        /// <summary>实时原始帧记录事件（GUI 订阅 → ChartFrom.RecordRealtimeRawMessage，供手动导出 BLF）</summary>
        public static event Action<uint, byte[], byte> RealtimeRawRecorded;

        /// <summary>BLF 写入请求（GUI 订阅 → Log.AddCanMessageToWrite）</summary>
        public static event Action<uint, byte[], ulong, byte> BlfWriteRequest;

        /// <summary>BLF 队列刷新请求（GUI 订阅 → Log.ContinuousWriteWorker）</summary>
        public static event Action BlfFlushRequest;

        /// <summary>ASC 落盘请求（GUI 订阅 → Log.saveLog）</summary>
        public static event Action<string, string> AscWriteRequest;

        /// <summary>CANoe 发送链路死亡通知（GUI 订阅 → Main.OnCanoeTxLinkDead）</summary>
        public static event Action CanoeTxLinkDead;

        /// <summary>触发 CANoe 发送链路死亡通知（供 CanOe_API 内部调用）</summary>
        public static void RaiseCanoeTxLinkDead()
        {
            try { CanoeTxLinkDead?.Invoke(); } catch { }
        }

        public static Boolean CanTransmit(uint ID, ushort len, byte[] data, byte channel = 1)
        {
            Boolean result = false;
            TPCANMsg tPCANMsg = new TPCANMsg();

            // 混合硬件：按逻辑通道配置的硬件类型路由；未指定类型时按现状兜底（PCAN优先）
            string hwType = BaseParamter.GetEffectiveHwTypeByChannel(channel);
            bool usePcan = hwType == BaseParamter.HwTypePcan || (hwType == "" && PcanOpenFlag);
            bool useCanoe = !usePcan && (hwType == BaseParamter.HwTypeCanoe || hwType == "");

            if (usePcan && PcanOpenFlag)
            {
                tPCANMsg.DATA = data;
                tPCANMsg.ID = ID;
                tPCANMsg.LEN = (byte)len;
                if (TPCANStatus.PCAN_ERROR_OK == PcanApi.PCAN_SendData(tPCANMsg, channel))
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
            else if (useCanoe && CanoeOpenFlag)
            {
                if (CanoeApi.CanoeCanTransmit(ID, len, data, channel))
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

        public static void CanReceive(uint ID, ushort len, byte[] data, TPCANMessageType MSGTYPE, ulong timestamp2, byte channel = 1)
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
            bool recordCh = RecordingState.IsRecordChannel(channel);
            // 记录实时CAN原始报文（用于ChartFrom保存BLF）；
            // 已落盘的帧（BLF录制开启且该通道勾选录制）跳过内存副本；其余帧（未录制/ASC/未勾选通道）必须入内存，否则手动导出永久缺失
            if (!(RecordingState.SaveFlag && 1 == RecordingState.SaveFileTypeInt && recordCh))
            {
                try { RealtimeRawRecorded?.Invoke(ID, data, blfCh); } catch { }
            }
            if (RecordingState.SaveFlag && 1 == RecordingState.SaveFileTypeInt && recordCh)
            {
                try { BlfWriteRequest?.Invoke(ID, data, timestamp2, blfCh); } catch { }
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

                // 更新报文显示记录（宿主上屏事件；未订阅即为空转）
                bool isTx = (PendingTxId == ID);
                try { FrameReceived?.Invoke(msg, timestamp2, isTx, channel); } catch { }

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
                        if (RecordingState.SaveFlag)
                        {
                            if ((0 == RecordingState.SaveFileTypeInt) && _ascBuffer.Length > 0)
                            {
                                FlushASCBuffer();
                            }
                            else if (1 == RecordingState.SaveFileTypeInt)
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
                if (!RecordingState.NowAscFileAddr.Equals("") && _ascBuffer.Length > 0)
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

        public static void FlushBLFBuffer()
        {
            // 异步后台写入，不阻塞CAN接收线程
            Task.Run(() =>
            {
                lock (_bufferLock)
                {
                    try { BlfFlushRequest?.Invoke(); } catch { }
                }
            });
        }

        private static void WriteASCFile(string ascText)
        {
            try
            {
                AscWriteRequest?.Invoke(ascText, RecordingState.NowAscFileAddr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ASC文件写入失败: {ex.Message}");
            }
        }
        /// <summary>ASC记录格式化：仅在需要落盘（recordCh且ASC文件类型）时构建字符串，其余场景零分配。</summary>
        private static void FormatAndAppendMessage(StringBuilder ascString,
            TPCANMsg msg, ulong time_us, byte channel = 1, bool recordCh = true)
        {
            // 条件提前：不记录ASC时直接返回，避免每帧无谓的字符串分配
            if (!(recordCh && RecordingState.SaveFlag && (0 == RecordingState.SaveFileTypeInt)))
            {
                return;
            }

            // ASC通道列写真实逻辑通道号
            ascString.AppendLine($"{time_us / 1000000}.{time_us % 1000000 / 1000:D3}{time_us % 1000:D3} {channel} {msg.ID:X2}             Rx    d {msg.LEN} {string.Join(" ", BitConverter.ToString(msg.DATA, 0, msg.LEN).Split('-'))}");
        }
    }
}
