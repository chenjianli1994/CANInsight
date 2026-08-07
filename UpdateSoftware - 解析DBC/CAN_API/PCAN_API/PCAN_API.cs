using PCAN_Client.CAN_API;
using PCAN_Client.UTIL;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;


namespace PCAN_Client.PCAN_API
{
    internal class PCAN_API
    {
        private MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();

        ushort PCAN_DeviceChannel = 255;
        /// <summary>多通道连接：逻辑通道号 → PCAN句柄（通道配置驱动的批量连接；为空时走PCAN_DeviceChannel单连接兼容路径）</summary>
        private readonly Dictionary<byte, ushort> _connectedChannels = new Dictionary<byte, ushort>();
        TPCANBaudrate ConnectBaud = TPCANBaudrate.PCAN_BAUD_500K;
        public byte PCAN_ReceiveThreadAlive = 0;
        Task PCAN_ReceiveThread = null;
        Boolean CanFDFlag = false;
        string bitrateFD = "f_clock_mhz=60, nom_brp=12, nom_tseg1=7, nom_tseg2=2, nom_sjw=1, data_brp=3, data_tseg1=7, data_tseg2=2, data_sjw=1"; /* 500k + 2M */

        Action action = null;

        //[DllImport("winmm")]
        //static extern void timeBeginPeriod(int t);
        //[DllImport("winmm")]
        //static extern void timeEndPeriod(int t);

        /// <summary>槽位识别结果缓存条目：Result=""表示空槽（无硬件）；"空闲"/"已占用"为在位状态。
        /// 60 秒内复用结果，避免反复驱动探测。真实设备 Initialize 耗时 0.2~10 秒（USB 枚举），
        /// 驱动 USB 缓存约 30 秒过期后重复变慢；本缓存让反复打开通道管理/刷新识别保持毫秒级。</summary>
        private class SlotCacheEntry
        {
            public DateTime Stamp;
            public string Result; // ""=空槽
        }
        private static readonly Dictionary<ushort, SlotCacheEntry> _slotCache = new Dictionary<ushort, SlotCacheEntry>();
        private static readonly object _slotCacheLock = new object();

        /// <summary>识别串行锁：全部槽位探测与连接操作互斥。启动后台识别与通道管理窗口刷新并发时，
        /// 同句柄并发 Initialize 会互相拖慢至 7~12 秒并产生 INITIALIZE 假错误（已实测）。</summary>
        private static readonly object _detectLock = new object();

        /// <summary>最近探测在位的槽位集合（保温对象）：保温只关心"设备还在不在"，不依赖结果缓存 TTL。</summary>
        private readonly HashSet<ushort> _presentSlots = new HashSet<ushort>();
        private readonly object _presentSlotsLock = new object();

        /// <summary>驱动保温定时器：对已知在位槽位周期做快速 Initialize+Uninitialize，
        /// 保持驱动 USB 缓存不过期（约 30s），使任意时刻的识别/刷新都无需等待 3~12 秒的冷枚举。</summary>
        private readonly System.Threading.Timer _warmTimer;

        public PCAN_API()
        {
            _warmTimer = new System.Threading.Timer(_ => WarmUpDriver(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

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
            lock (_detectLock)
            {
                multiMessageCANScheduler.Stop();
                Main.main.pCAN_API.PCAN_ReceiveThreadAlive = 0;
                foreach (var kv in _connectedChannels)
                {
                    PCANBasic.Uninitialize(kv.Value);
                }
                _connectedChannels.Clear();
                PCANBasic.Uninitialize(PCAN_DeviceChannel);
                PCAN_DeviceChannel = 255;
            }
        }

        /// <summary>
        /// 多通道批量连接：按通道配置（BusChannels）中各通道绑定的硬件通道批量Initialize，
        /// 每路按各自 CAN/CANFD 模式与波特率档位初始化；任一成功即启动接收；返回成功连接的通道数
        /// </summary>
        public int ConnectMulti()
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
                    multiMessageCANScheduler.Start();
                }
                return _connectedChannels.Count;
            }
        }

        /// <summary>按逻辑通道号取已连接句柄；未连接该通道时回退当前单连接句柄</summary>
        private ushort GetHandleForChannel(byte logicChannel)
        {
            if (_connectedChannels.TryGetValue(logicChannel, out ushort handle)) return handle;
            return PCAN_DeviceChannel;
        }

        /// <summary>逻辑通道是否已连接（多通道字典）</summary>
        internal bool IsLogicChannelConnected(byte logicChannel) => _connectedChannels.ContainsKey(logicChannel);

        /// <summary>硬件通道号（USBBUS序号1-16）是否已连接（识别缓存"已连接"状态本地判定用，不做硬件访问）</summary>
        internal bool IsHwChannelConnected(byte hw)
        {
            if (hw < 1 || hw > 16) return false;
            return _connectedChannels.ContainsValue(PCAN_DeviceChannelBuf[hw - 1]);
        }

        /// <summary>增量连接单个逻辑通道（多通道模式）：按该行 CAN/CANFD 模式与波特率档位Initialize绑定的硬件句柄并加入已连接字典；首个通道连接时启动接收调度</summary>
        internal bool ConnectOne(int logicIndex)
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
                    multiMessageCANScheduler.Start();
                }
                return true;
            }
        }

        /// <summary>增量断开单个逻辑通道；全部断开后停止接收调度。返回剩余已连接通道数</summary>
        internal int DisconnectOne(int logicIndex)
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
                    multiMessageCANScheduler.Stop();
                }
                return _connectedChannels.Count;
            }
        }

        /// <summary>已连接的（逻辑通道号,句柄）枚举，供接收轮询</summary>
        internal IEnumerable<KeyValuePair<byte, ushort>> ConnectedChannels => _connectedChannels;
        /// <summary>
        /// 枚举全部16个USBBUS槽位硬件。force=true 时绕过60秒结果缓存（供"刷新识别"按钮显式重查）；
        /// 无论 force 与否都先做毫秒级在位查询（PCAN_CHANNEL_CONDITION），只对在位槽位执行 Initialize 判定 空闲/已占用。
        /// 全部探测经 _detectLock 串行化，杜绝并发探测互相拖慢。
        /// </summary>
        public List<string> GetPCAN_ChannelRefresh(bool force = false)
        {
            lock (_detectLock)
            {
                return ProbePcanChannels(force);
            }
        }

        private List<string> ProbePcanChannels(bool force)
        {
            List<string> PCAN_Channel = new List<string>();

            System.Diagnostics.Stopwatch swTotal = System.Diagnostics.Stopwatch.StartNew();
            System.Text.StringBuilder slotLog = new System.Text.StringBuilder();
            // 直接探测全部16个USBBUS槽位：不依赖WMI计数（Description不一定含PCAN、驱动残留节点会虚报），
            // 且USBBUS序号可能不连续（设备按插入顺序占用编号），空槽位跳过继续探测
            for (int i = 0; i < PCAN_DeviceChannelBuf.Length; i++)
            {
                ushort handle = PCAN_DeviceChannelBuf[i];
                if (handle == PCAN_DeviceChannel || _connectedChannels.ContainsValue(handle))
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(已连接)");
                    continue;
                }
                // 结果缓存命中（60 秒内）→ 直接复用，不做任何驱动访问
                if (!force)
                {
                    lock (_slotCacheLock)
                    {
                        if (_slotCache.TryGetValue(handle, out SlotCacheEntry e) && (DateTime.Now - e.Stamp).TotalSeconds < 60)
                        {
                            if (e.Result.Length > 0) PCAN_Channel.Add("USB_" + (i + 1) + "(" + e.Result + ")");
                            continue;
                        }
                    }
                }
                // 快速在位查询（毫秒级，PEAK官方推荐）：无硬件直接跳过，不再逐槽 Initialize
                uint cond = 0;
                TPCANStatus st = PCANBasic.GetValue(handle, TPCANParameter.PCAN_CHANNEL_CONDITION, out cond, sizeof(uint));
                if (st == TPCANStatus.PCAN_ERROR_OK && cond == 0)
                {
                    lock (_slotCacheLock) _slotCache[handle] = new SlotCacheEntry { Stamp = DateTime.Now, Result = "" };
                    lock (_presentSlotsLock) _presentSlots.Remove(handle);
                    continue;
                }
                // 在位（或条件查询失败回退）：Initialize 判定 空闲/已占用
                System.Diagnostics.Stopwatch swSlot = System.Diagnostics.Stopwatch.StartNew();
                TPCANStatus result = PCANBasic.Initialize(handle, ConnectBaud, (TPCANType)0, 0, 0);
                swSlot.Stop();
                if (swSlot.ElapsedMilliseconds > 200)
                    slotLog.Append("slot" + (i + 1) + "=" + swSlot.ElapsedMilliseconds + "ms(" + result + ") ");
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    // 初始化成功：通道存在且空闲（还原释放）
                    lock (_slotCacheLock) _slotCache[handle] = new SlotCacheEntry { Stamp = DateTime.Now, Result = "空闲" };
                    lock (_presentSlotsLock) _presentSlots.Add(handle);
                    PCAN_Channel.Add("USB_" + (i + 1) + "(空闲)");
                    PCANBasic.Uninitialize(handle);
                }
                else if (TPCANStatus.PCAN_ERROR_HWINUSE == result)
                {
                    // 通道存在但被其他程序占用
                    lock (_slotCacheLock) _slotCache[handle] = new SlotCacheEntry { Stamp = DateTime.Now, Result = "已占用" };
                    lock (_presentSlotsLock) _presentSlots.Add(handle);
                    PCAN_Channel.Add("USB_" + (i + 1) + "(已占用)");
                }
                else
                {
                    // ILLHW/INITIALIZE等：该槽位无硬件，记入缓存，跳过继续探测后续序号
                    lock (_slotCacheLock) _slotCache[handle] = new SlotCacheEntry { Stamp = DateTime.Now, Result = "" };
                    lock (_presentSlotsLock) _presentSlots.Remove(handle);
                }
            }
            swTotal.Stop();
            try
            {
                string logLine = DateTime.Now.ToString("HH:mm:ss.fff") + " PCAN16槽=" + swTotal.ElapsedMilliseconds + "ms 结果[" + string.Join(",", PCAN_Channel.ToArray()) + "] 慢槽[" + slotLog.ToString() + "]";
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CANInsight");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "detect_timing.log"), logLine + Environment.NewLine);
            }
            catch { }
            return PCAN_Channel;
        }

        /// <summary>驱动保温：对已知在位且未连接的槽位做快速 Initialize+Uninitialize，
        /// 保持驱动 USB 缓存不因 30 秒空闲而过期（否则下次识别每路在位设备 Initialize 需 3~12 秒）。
        /// 保温只刷新槽位结果、不延长结果缓存 TTL（60 秒后自动重查仍会触发，保证设备插拔最多 60 秒内被发现）；
        /// 设备被拔出时从在位集合与缓存中剔除。</summary>
        private void WarmUpDriver()
        {
            try
            {
                lock (_detectLock)
                {
                    ushort[] present;
                    lock (_presentSlotsLock)
                    {
                        present = new ushort[_presentSlots.Count];
                        _presentSlots.CopyTo(present);
                    }
                    foreach (ushort handle in present)
                    {
                        if (handle == PCAN_DeviceChannel || _connectedChannels.ContainsValue(handle)) continue;
                        TPCANStatus r = PCANBasic.Initialize(handle, ConnectBaud, (TPCANType)0, 0, 0);
                        if (r == TPCANStatus.PCAN_ERROR_OK)
                        {
                            PCANBasic.Uninitialize(handle);
                            lock (_slotCacheLock)
                            {
                                if (_slotCache.TryGetValue(handle, out SlotCacheEntry e) && e.Result.Length > 0)
                                    _slotCache[handle] = new SlotCacheEntry { Stamp = e.Stamp, Result = "空闲" };
                            }
                        }
                        else if (r == TPCANStatus.PCAN_ERROR_HWINUSE)
                        {
                            lock (_slotCacheLock)
                            {
                                if (_slotCache.TryGetValue(handle, out SlotCacheEntry e) && e.Result.Length > 0)
                                    _slotCache[handle] = new SlotCacheEntry { Stamp = e.Stamp, Result = "已占用" };
                            }
                        }
                        else
                        {
                            // 设备已拔出：剔除在位集合与缓存，下次识别重新探测
                            lock (_presentSlotsLock) _presentSlots.Remove(handle);
                            lock (_slotCacheLock) _slotCache.Remove(handle);
                        }
                    }
                }
            }
            catch { /* 保温失败不影响主流程 */ }
        }

        public Boolean Connect(Boolean CanFDFlag)
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
                        multiMessageCANScheduler.Start();
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
                        MessageBox.Show("连接错误");
                        return false;
                    }
                    return false;
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
        internal TPCANStatus PCAN_SendData(TPCANMsg tPCANMsg, byte channel = 1)
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
            else
            {
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
                    result = PCANBasic.ReadFD(PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                }
                else
                {
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

        public class MultiMessageCANScheduler
        {
            /// <summary>单次1ms回调最多处理的接收帧数（多通道/单通道共用；剩余帧留待下个回调，硬件FIFO缓冲）</summary>
            private const int MAX_FRAMES_PER_TICK = 200;
            bool callbackFlag;
            TPCANTimestamp timesamp = new TPCANTimestamp();
            TPCANMsgFD msgFD = new TPCANMsgFD();
            TPCANMsg msg = new TPCANMsg();
            long time_us;
            ulong TimestampBuffer = 0;
            TPCANStatus result;

            public void Start()
            {
                callbackFlag = false;
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "PCAN_Receive", 1);
            }

            public void Stop()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("PCAN_Receive");
            }

            private void SchedulerCallback()
            {
                try
                {
                    if (callbackFlag)
                    {
                        return;
                    }
                    callbackFlag = true;
                    //if (0 == Main.main.pCAN_API.PCAN_ReceiveThreadAlive || true == Main.main.canoe_API.aliveFlag)
                    //{
                    //    if (true == Main.main.canoe_API.aliveFlag)
                    //    {
                    //        //Main.main.PCAN_Connect();
                    //    }
                    //    else
                    //    {
                    //        //Stop();
                    //    }
                    //    Main.main.pCAN_API.PCAN_ReceiveThreadAlive = 0;
                    //}

                    var api = Main.main.pCAN_API;
                    if (api._connectedChannels.Count > 0)
                    {
                        // 多通道模式：轮询所有已连接句柄，按各自逻辑通道号上报。
                        // 限帧：单次1ms回调最多处理MAX_FRAMES_PER_TICK帧，剩余留待下个回调（硬件队列缓冲，总吞吐不变），
                        // 防止高负载时排空超时阻塞同回调线程上的其他动作（周期发送/CANoe接收/图表仿真）
                        int framesThisTick = 0;
                        while (0 != api.PCAN_ReceiveThreadAlive && framesThisTick < MAX_FRAMES_PER_TICK)
                        {
                            bool anyData = false;
                            foreach (var kv in api._connectedChannels)
                            {
                                if (framesThisTick >= MAX_FRAMES_PER_TICK) break;
                                if (BaseParamter.GetChannelCanFdByLogic(kv.Key)) // kv.Key=逻辑通道号，按该通道模式选择读取API
                                {
                                    result = PCANBasic.ReadFD(kv.Value, out msgFD, out TimestampBuffer);
                                }
                                else
                                {
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
                        while (0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive && framesThisTick < MAX_FRAMES_PER_TICK)
                        {
                            if (Main.main.pCAN_API.CanFDFlag)
                            {
                                result = PCANBasic.ReadFD(Main.main.pCAN_API.PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                            }
                            else
                            {
                                result = PCANBasic.Read(Main.main.pCAN_API.PCAN_DeviceChannel, out msg, out timesamp);
                            }
                            if (TPCANStatus.PCAN_ERROR_OK == result)
                            {
                                framesThisTick++;
                                if (Main.main.pCAN_API.CanFDFlag)
                                {
                                    time_us = (long)TimestampBuffer;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE, (ulong)time_us, Main.main.pCAN_API.GetCurrentLogicChannel());
                                    }
                                }
                                else
                                {
                                    time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA, msg.MSGTYPE,(ulong)time_us, Main.main.pCAN_API.GetCurrentLogicChannel());
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
}
