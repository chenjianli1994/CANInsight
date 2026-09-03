using PCAN_Client.CAN_API;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace PCAN_Client.PCAN_API
{
    public class PCAN_API
    {
        private MultiMessageCANScheduler _recvScheduler = new MultiMessageCANScheduler(null);
        private const string RecvActionName = "PCAN_Receive";

        ushort PCAN_DeviceChannel = 255;
        /// <summary>多通道连接：逻辑通道号 → PCAN句柄（通道配置驱动的批量连接；为空时走PCAN_DeviceChannel单连接兼容路径）</summary>
        private readonly Dictionary<byte, ushort> _connectedChannels = new Dictionary<byte, ushort>();
        TPCANBaudrate ConnectBaud = TPCANBaudrate.PCAN_BAUD_500K;
        public byte PCAN_ReceiveThreadAlive = 0;
        Task PCAN_ReceiveThread = null;
        Boolean CanFDFlag = false;
        string bitrateFD = "f_clock_mhz=60, nom_brp=12, nom_tseg1=7, nom_tseg2=2, nom_sjw=1, data_brp=3, data_tseg1=7, data_tseg2=2, data_sjw=1"; /* 500k + 2M */

        //[DllImport("winmm")]
        //static extern void timeBeginPeriod(int t);
        //[DllImport("winmm")]
        //static extern void timeEndPeriod(int t);

        /// <summary>识别串行锁：全部槽位探测与连接操作互斥。启动后台识别与通道管理窗口刷新并发时，
        /// 同句柄并发 Initialize 会互相拖慢至 7~12 秒并产生 INITIALIZE 假错误（已实测）。</summary>
        private static readonly object _detectLock = new object();

        ushort[] PCAN_DeviceChannelBuf = new ushort[16]
        {
            PCANBasic.PCAN_USBBUS1,
            PCANBasic.PCAN_USBBUS2,
            PCANBasic.PCAN_USBBUS3,
            PCANBasic.PCAN_USBBUS4,
            PCANBasic.PCAN_USBBUS5,
            PCANBasic.PCAN_USBBUS6,
            PCANBasic.PCAN_USBBUS7,
            PCANBasic.PCAN_USBBUS8,
            PCANBasic.PCAN_USBBUS9,
            PCANBasic.PCAN_USBBUS10,
            PCANBasic.PCAN_USBBUS11,
            PCANBasic.PCAN_USBBUS12,
            PCANBasic.PCAN_USBBUS13,
            PCANBasic.PCAN_USBBUS14,
            PCANBasic.PCAN_USBBUS15,
            PCANBasic.PCAN_USBBUS16,
        };

        public void SetPcanChannel(int channel)
        {
            PCAN_DeviceChannel = PCAN_DeviceChannelBuf[channel];
        }

        /// <summary>当前连接的物理通道（PCAN_USBBUSn）对应的逻辑通道号（经通道配置的硬件通道映射；混合硬件时限定PCAN类型反查消除同号歧义）</summary>
        private byte GetCurrentLogicChannel()
        {
            int idx = Array.IndexOf(PCAN_DeviceChannelBuf, PCAN_DeviceChannel);
            byte hw = (byte)(idx >= 0 ? idx + 1 : 1);
            return BaseParamter.GetLogicChannelByHw(BaseParamter.HwTypePcan, hw);
        }

        public void PCAN_ChannelUninitialize()
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    StopReceiveScheduler();
                    PCAN_ReceiveThreadAlive = 0;
                    foreach (var kv in _connectedChannels)
                    {
                        PCANBasic.Uninitialize(kv.Value);
                    }
                    _connectedChannels.Clear();
                    PCANBasic.Uninitialize(PCAN_DeviceChannel);
                    PCAN_DeviceChannel = 255;
                }
            }
        }

        /// <summary>
        /// 多通道批量连接：按通道配置（BusChannels）中各通道绑定的硬件通道批量Initialize，
        /// 每路按各自 CAN/CANFD 模式与波特率档位初始化；任一成功即启动接收；返回成功连接的通道数
        /// </summary>
        public int ConnectMulti()
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    _connectedChannels.Clear();
                    PCAN_ReceiveThreadAlive = 0;

                    for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
                    {
                        var ch = BaseParamter.BusChannels[i];
                        string hwType = BaseParamter.GetEffectiveHwType(i);
                        if (hwType != "" && hwType != BaseParamter.HwTypePcan) continue; // 混合硬件：只连PCAN类型或未指定的通道
                        byte hw = BaseParamter.GetEffectiveHwChannel(i);
                        if (hw < 1 || hw > 16) continue; // 含255=不连接哨兵
                        ushort handle = PCAN_DeviceChannelBuf[hw - 1];

                        PCANBasic.Uninitialize(handle);
                        TPCANStatus result = BaseParamter.GetChannelCanFd(i)
                            ? PCANBasic.InitializeFD(handle, BaudrateConfig.BuildPcanFdBitrateString(
                                BaseParamter.GetChannelFdPreset(i).ArbBaud, BaseParamter.GetChannelFdPreset(i).DataBaud))
                            : PCANBasic.Initialize(handle, BaseParamter.GetChannelClassicPreset(i).PcanBaud, (TPCANType)0, 0, 0);
                        if (TPCANStatus.PCAN_ERROR_OK == result)
                        {
                            _connectedChannels[BaseParamter.GetLogicChannel(i)] = handle; // key=配置通道号（接收轮询按此上报）
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[PCAN] 通道{ch.Name}(USB_{hw})连接失败: {result}");
                        }
                    }

                    if (_connectedChannels.Count > 0)
                    {
                        PCAN_ReceiveThreadAlive = 1;
                        StartReceiveScheduler();
                    }
                    return _connectedChannels.Count;
                }
            }
        }

        /// <summary>按逻辑通道号取已连接句柄；未连接该通道时回退当前单连接句柄</summary>
        private ushort GetHandleForChannel(byte logicChannel)
        {
            if (_connectedChannels.TryGetValue(logicChannel, out ushort handle)) return handle;
            return PCAN_DeviceChannel;
        }

        /// <summary>逻辑通道是否已连接（多通道字典）</summary>
        /// <summary>逻辑通道是否已连接（多通道字典）</summary>
        public bool IsLogicChannelConnected(byte logicChannel) => _connectedChannels.ContainsKey(logicChannel);

        /// <summary>硬件通道号（USBBUS序号1-16）是否已连接（识别缓存"已连接"状态本地判定用，不做硬件访问）</summary>
        public bool IsHwChannelConnected(byte hw)
        {
            if (hw < 1 || hw > 16) return false;
            return _connectedChannels.ContainsValue(PCAN_DeviceChannelBuf[hw - 1]);
        }

        /// <summary>增量连接单个逻辑通道（多通道模式）：按该行 CAN/CANFD 模式与波特率档位Initialize绑定的硬件句柄并加入已连接字典；首个通道连接时启动接收调度</summary>
        public bool ConnectOne(int logicIndex)
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    if (logicIndex < 0 || logicIndex >= BaseParamter.BusChannels.Count) return false;
                    byte hw = BaseParamter.GetEffectiveHwChannel(logicIndex);
                    if (hw < 1 || hw > 16) return false;
                    ushort handle = PCAN_DeviceChannelBuf[hw - 1];

                    PCANBasic.Uninitialize(handle);
                    TPCANStatus result = BaseParamter.GetChannelCanFd(logicIndex)
                        ? PCANBasic.InitializeFD(handle, BaudrateConfig.BuildPcanFdBitrateString(
                            BaseParamter.GetChannelFdPreset(logicIndex).ArbBaud, BaseParamter.GetChannelFdPreset(logicIndex).DataBaud))
                        : PCANBasic.Initialize(handle, BaseParamter.GetChannelClassicPreset(logicIndex).PcanBaud, (TPCANType)0, 0, 0);
                    if (TPCANStatus.PCAN_ERROR_OK != result)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PCAN] 通道{BaseParamter.BusChannels[logicIndex].Name}(USB_{hw})连接失败: {result}");
                        return false;
                    }
                    _connectedChannels[BaseParamter.GetLogicChannel(logicIndex)] = handle; // key=配置通道号（接收轮询按此上报）
                    if (PCAN_ReceiveThreadAlive == 0)
                    {
                        PCAN_ReceiveThreadAlive = 1;
                        StartReceiveScheduler();
                    }
                    return true;
                }
            }
        }

        /// <summary>增量断开单个逻辑通道；全部断开后停止接收调度。返回剩余已连接通道数</summary>
        public int DisconnectOne(int logicIndex)
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    byte key = BaseParamter.GetLogicChannel(logicIndex);
                    if (_connectedChannels.TryGetValue(key, out ushort handle))
                    {
                        _connectedChannels.Remove(key);
                        PCANBasic.Uninitialize(handle);
                    }
                    if (_connectedChannels.Count == 0)
                    {
                        PCAN_ReceiveThreadAlive = 0;
                        StopReceiveScheduler();
                    }
                    return _connectedChannels.Count;
                }
            }
        }

        /// <summary>已连接的（逻辑通道号,句柄）枚举，供接收轮询</summary>
        public IEnumerable<KeyValuePair<byte, ushort>> ConnectedChannels => _connectedChannels;
        /// <summary>
        /// 枚举 PCAN-Basic 报告的已连接通道。识别阶段只读 PCAN_ATTACHED_CHANNELS，
        /// 不调用 Initialize/Uninitialize，避免重置 PCAN-USB Pro FD 的 CAN 状态并干扰 PLIN Manager。
        /// force 参数保留用于兼容现有 UI，附着通道查询本身已是毫秒级，无需结果缓存。
        /// </summary>
        public List<string> GetPCAN_ChannelRefresh(bool force = false)
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    return ProbePcanChannels();
                }
            }
        }

        private List<string> ProbePcanChannels()
        {
            List<string> PCAN_Channel = new List<string>();

            System.Diagnostics.Stopwatch swTotal = System.Diagnostics.Stopwatch.StartNew();
            string mode = "附着通道";
            uint attachedCount = 0;
            TPCANStatus attachedStatus = PCANBasic.GetValue(
                PCANBasic.PCAN_NONEBUS,
                TPCANParameter.PCAN_ATTACHED_CHANNELS_COUNT,
                out attachedCount,
                sizeof(uint));

            bool attachedApiOk = attachedStatus == TPCANStatus.PCAN_ERROR_OK;
            if (attachedStatus == TPCANStatus.PCAN_ERROR_OK)
            {
                mode = "附着通道";
                int count = attachedCount > 256 ? 256 : (int)attachedCount;
                if (count > 0)
                {
                    var channels = new TPCANChannelInformation[count];
                    attachedStatus = PCANBasic.GetValue(
                        PCANBasic.PCAN_NONEBUS,
                        TPCANParameter.PCAN_ATTACHED_CHANNELS,
                        channels);
                    if (attachedStatus == TPCANStatus.PCAN_ERROR_OK)
                    {
                        AddAttachedPcanChannels(PCAN_Channel, channels);
                    }
                    else
                    {
                        attachedApiOk = false;
                    }
                }
            }

            if (!attachedApiOk)
            {
                // 旧版 PCAN-Basic 不支持附着通道列表时，退回逐句柄只读条件查询。
                // 该路径仍绝不 Initialize/Uninitialize；未知条件按“已发现”保留，避免漏报硬件。
                mode = "通道条件回退";
                ProbePcanChannelsByCondition(PCAN_Channel);
            }
            swTotal.Stop();
            try
            {
                string logLine = DateTime.Now.ToString("HH:mm:ss.fff")
                    + " PCAN识别=" + swTotal.ElapsedMilliseconds + "ms"
                    + " 模式=" + mode
                    + " 附着数=" + attachedCount
                    + " 结果[" + string.Join(",", PCAN_Channel.ToArray()) + "]"
                    + " API=" + attachedStatus;
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CANInsight");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "detect_timing.log"), logLine + Environment.NewLine);
            }
            catch { }
            return PCAN_Channel;
        }

        private void AddAttachedPcanChannels(List<string> result, TPCANChannelInformation[] channels)
        {
            // PCAN_ATTACHED_CHANNELS 的返回顺序不是 UI 约定的 USB_1、USB_2 顺序，
            // 先收集到槽位映射，再按固定句柄表输出，避免重插后下拉顺序跳变。
            var bySlot = new Dictionary<int, string>();
            foreach (TPCANChannelInformation channel in channels)
            {
                int slot = Array.IndexOf(PCAN_DeviceChannelBuf, channel.channel_handle);
                if (slot < 0) continue; // 忽略 PCI/LAN/Virtual 等非 USB 槽位

                string status;
                if (channel.channel_handle == PCAN_DeviceChannel || _connectedChannels.ContainsValue(channel.channel_handle))
                {
                    status = "已连接";
                }
                else if (channel.channel_condition == PCANBasic.PCAN_CHANNEL_AVAILABLE)
                {
                    status = "空闲";
                }
                else if (channel.channel_condition == PCANBasic.PCAN_CHANNEL_OCCUPIED
                    || channel.channel_condition == PCANBasic.PCAN_CHANNEL_PCANVIEW)
                {
                    status = "已占用";
                }
                else if (channel.channel_condition == PCANBasic.PCAN_CHANNEL_UNAVAILABLE)
                {
                    continue;
                }
                else
                {
                    status = "已发现";
                }
                bySlot[slot] = "USB_" + (slot + 1) + "(" + status + ")";
            }

            for (int slot = 0; slot < PCAN_DeviceChannelBuf.Length; slot++)
            {
                string item;
                if (bySlot.TryGetValue(slot, out item)) result.Add(item);
            }
        }

        private void ProbePcanChannelsByCondition(List<string> result)
        {
            for (int slot = 0; slot < PCAN_DeviceChannelBuf.Length; slot++)
            {
                ushort handle = PCAN_DeviceChannelBuf[slot];
                if (handle == PCAN_DeviceChannel || _connectedChannels.ContainsValue(handle))
                {
                    result.Add("USB_" + (slot + 1) + "(已连接)");
                    continue;
                }

                uint condition = PCANBasic.PCAN_CHANNEL_UNAVAILABLE;
                TPCANStatus status = PCANBasic.GetValue(
                    handle,
                    TPCANParameter.PCAN_CHANNEL_CONDITION,
                    out condition,
                    sizeof(uint));
                if (status != TPCANStatus.PCAN_ERROR_OK
                    || condition == PCANBasic.PCAN_CHANNEL_UNAVAILABLE)
                {
                    continue;
                }
                if (condition == PCANBasic.PCAN_CHANNEL_AVAILABLE)
                {
                    result.Add("USB_" + (slot + 1) + "(空闲)");
                }
                else if (condition == PCANBasic.PCAN_CHANNEL_OCCUPIED
                    || condition == PCANBasic.PCAN_CHANNEL_PCANVIEW)
                {
                    result.Add("USB_" + (slot + 1) + "(已占用)");
                }
                else
                {
                    result.Add("USB_" + (slot + 1) + "(已发现)");
                }
            }
        }

        public Boolean Connect(Boolean CanFDFlag)
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                lock (_detectLock)
                {
                    TPCANStatus result;

                    this.CanFDFlag = CanFDFlag;

                    PCANBasic.Uninitialize(PCAN_DeviceChannel);
                    PCAN_ReceiveThreadAlive = 0;
                    Thread.Sleep(50);
                    result = PCANBasic.GetStatus(PCAN_DeviceChannel);

                    if (TPCANStatus.PCAN_ERROR_OK != result)
                    {
                        if (CanFDFlag)
                        {
                            result = PCANBasic.InitializeFD(PCAN_DeviceChannel, bitrateFD);
                        }
                        else
                        {
                            result = PCANBasic.Initialize(PCAN_DeviceChannel, ConnectBaud, (TPCANType)0, 0, 0);
                        }
                        if (TPCANStatus.PCAN_ERROR_OK == result)
                        {
                            PCAN_ReceiveThreadAlive = 1;
                            // 启动调度器
                            StartReceiveScheduler();
#if false
                            if (null == action)
                            {
                                action = PCAN_ReadData;
                            }
                            else
                            {
                                /* empty */
                            }
                            action.BeginInvoke(null, null); //打开接收线程
#endif
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        PCAN_ReceiveThreadAlive = 0;
                        Thread.Sleep(50);
                        PCAN_ReceiveThread?.Dispose();
                        PCAN_ReceiveThread = null;

                        PCANBasic.Uninitialize(PCAN_DeviceChannel);
                        if (TPCANStatus.PCAN_ERROR_OK != result)
                        {
                            Debug.WriteLine("[PCAN] 连接错误");
                            return false;
                        }
                        return false;
                    }
                }
            }
        }

        private static int GetReceiveDataDlc(int len)
        {
            int actualDataLength;
            switch (len)
            {
                case 9:
                    actualDataLength = 12;
                    break;
                case 10:
                    actualDataLength = 16;
                    break;
                case 11:
                    actualDataLength = 20;
                    break;
                case 12:
                    actualDataLength = 24;
                    break;
                case 13:
                    actualDataLength = 32;
                    break;
                case 14:
                    actualDataLength = 48;
                    break;
                case 15:
                    actualDataLength = 64;
                    break;
                default:
                    actualDataLength = len;
                    break;
            }
            return actualDataLength;
        }
        // 实现数据长度到DLC的转换（兼容低版本C#）
        private byte GetSendDataDlc(byte dataLength)
        {
            // 确保数据长度在有效范围内
            if (dataLength <= 8) return dataLength;
            if (dataLength <= 12) return 9;
            if (dataLength <= 16) return 10;
            if (dataLength <= 20) return 11;
            if (dataLength <= 24) return 12;
            if (dataLength <= 32) return 13;
            if (dataLength <= 48) return 14;
            if (dataLength <= 64) return 15;

            // 数据长度超过64字节，默认使用最大DLC值
            return 15;
        }
        /// <summary>发送数据。channel为逻辑通道号：多通道连接时路由到对应句柄，未连接该通道时回退当前单连接句柄；
        /// FD 格式按该通道配置判定（单通道兼容场景回退全局默认模式）</summary>
        public TPCANStatus PCAN_SendData(TPCANMsg tPCANMsg, byte channel = 1)
        {
            lock (CoreHardwareAccess.SyncRoot)
            {
                ushort handle = GetHandleForChannel(channel);
                if (BaseParamter.GetChannelCanFdByLogic(channel))
                {
                    TPCANMsgFD tPCANMsgFD = new TPCANMsgFD();
                    tPCANMsgFD.DATA = new byte[64];
                    Array.Copy(tPCANMsg.DATA, 0, tPCANMsgFD.DATA, 0, tPCANMsg.LEN);
                    // 自动降级：≤8字节按经典CAN格式发送（不带FD标志，兼容纯经典CAN节点）；>8字节必须FD格式（保留扩展帧位）
                    tPCANMsgFD.MSGTYPE = tPCANMsg.LEN <= 8
                        ? tPCANMsg.MSGTYPE
                        : (TPCANMessageType.PCAN_MESSAGE_FD | (tPCANMsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_EXTENDED));
                    tPCANMsgFD.DLC = GetSendDataDlc(tPCANMsg.LEN);
                    tPCANMsgFD.ID = tPCANMsg.ID;
                    return PCANBasic.WriteFD(handle, ref tPCANMsgFD);
                }
                return PCANBasic.Write(handle, ref tPCANMsg);
            }
        }

        private void PCAN_ReadData()
        {
            TPCANTimestamp timesamp = new TPCANTimestamp();
            TPCANMsgFD msgFD = new TPCANMsgFD();
            TPCANMsg msg = new TPCANMsg();
            long time_us;
            ulong TimestampBuffer = 0;
            TPCANStatus result;

            while (0 != PCAN_ReceiveThreadAlive)
            {
                if (CanFDFlag)
                {
                    lock (CoreHardwareAccess.SyncRoot)
                        result = PCANBasic.ReadFD(PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                }
                else
                {
                    lock (CoreHardwareAccess.SyncRoot)
                        result = PCANBasic.Read(PCAN_DeviceChannel, out msg, out timesamp);
                }
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    if (CanFDFlag)
                    {
                        time_us = (long)TimestampBuffer;
                        lock (CAN_API.CAN_API._receiveCanDataLock)
                        {
                            CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE,(ulong)time_us, GetCurrentLogicChannel());
                        }
                    }
                    else
                    {
                        time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                        lock (CAN_API.CAN_API._receiveCanDataLock)
                        {
                            CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA,msg.MSGTYPE, (ulong)time_us, GetCurrentLogicChannel());
                        }
                    }
                }
                else
                {
                    Thread.Sleep(20);
                }
            }
        }

        /// <summary>启动接收轮询（自建 1ms 调度器，注册本实例回调）</summary>
        private void StartReceiveScheduler()
        {
            _recvScheduler.AddAction(SchedulerCallback, RecvActionName, 1);
            _recvScheduler.Start();
        }

        /// <summary>停止接收轮询</summary>
        private void StopReceiveScheduler()
        {
            _recvScheduler.RemoveAction(RecvActionName);
            _recvScheduler.Stop();
        }

        /// <summary>单次1ms回调最多处理的接收帧数（多通道/单通道共用；剩余帧留待下个回调，硬件FIFO缓冲）</summary>
        private const int MAX_FRAMES_PER_TICK = 200;
        private bool callbackFlag;
        private TPCANTimestamp timesamp = new TPCANTimestamp();
        private TPCANMsgFD msgFD = new TPCANMsgFD();
        private TPCANMsg msg = new TPCANMsg();
        private long time_us;
        private ulong TimestampBuffer = 0;
        private TPCANStatus result;

        private void SchedulerCallback()
        {
            try
            {
                if (callbackFlag)
                {
                    return;
                }
                callbackFlag = true;

                if (_connectedChannels.Count > 0)
                {
                    // 多通道模式：轮询所有已连接句柄，按各自逻辑通道号上报。
                    // 限帧：单次1ms回调最多处理MAX_FRAMES_PER_TICK帧，剩余留待下个回调（硬件队列缓冲，总吞吐不变）
                    int framesThisTick = 0;
                    while (0 != PCAN_ReceiveThreadAlive && framesThisTick < MAX_FRAMES_PER_TICK)
                    {
                        bool anyData = false;
                        foreach (var kv in _connectedChannels)
                        {
                            if (framesThisTick >= MAX_FRAMES_PER_TICK) break;
                            if (BaseParamter.GetChannelCanFdByLogic(kv.Key)) // kv.Key=逻辑通道号，按该通道模式选择读取API
                            {
                                lock (CoreHardwareAccess.SyncRoot)
                                    result = PCANBasic.ReadFD(kv.Value, out msgFD, out TimestampBuffer);
                            }
                            else
                            {
                                lock (CoreHardwareAccess.SyncRoot)
                                    result = PCANBasic.Read(kv.Value, out msg, out timesamp);
                            }
                            if (TPCANStatus.PCAN_ERROR_OK != result) continue;
                            anyData = true;
                            framesThisTick++;
                            if (BaseParamter.GetChannelCanFdByLogic(kv.Key))
                            {
                                time_us = (long)TimestampBuffer;
                                lock (CAN_API.CAN_API._receiveCanDataLock)
                                {
                                    CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE, (ulong)time_us, kv.Key);
                                }
                            }
                            else
                            {
                                time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                                lock (CAN_API.CAN_API._receiveCanDataLock)
                                {
                                    CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA, msg.MSGTYPE, (ulong)time_us, kv.Key);
                                }
                            }
                        }
                        if (!anyData) break;
                    }
                }
                else
                {
                    // 单通道兼容路径（同样限帧）
                    int framesThisTick = 0;
                    while (0 != PCAN_ReceiveThreadAlive && framesThisTick < MAX_FRAMES_PER_TICK)
                    {
                        if (CanFDFlag)
                        {
                            lock (CoreHardwareAccess.SyncRoot)
                                result = PCANBasic.ReadFD(PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                        }
                        else
                        {
                            lock (CoreHardwareAccess.SyncRoot)
                                result = PCANBasic.Read(PCAN_DeviceChannel, out msg, out timesamp);
                        }
                        if (TPCANStatus.PCAN_ERROR_OK == result)
                        {
                            framesThisTick++;
                            if (CanFDFlag)
                            {
                                time_us = (long)TimestampBuffer;
                                lock (CAN_API.CAN_API._receiveCanDataLock)
                                {
                                    CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE, (ulong)time_us, GetCurrentLogicChannel());
                                }
                            }
                            else
                            {
                                time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                                lock (CAN_API.CAN_API._receiveCanDataLock)
                                {
                                    CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA, msg.MSGTYPE, (ulong)time_us, GetCurrentLogicChannel());
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                callbackFlag = false;
            }
            catch
            {
                callbackFlag = false;
            }
        }
    }
}
