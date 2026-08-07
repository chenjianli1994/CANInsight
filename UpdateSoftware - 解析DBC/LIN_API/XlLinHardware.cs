using System;
using System.Collections.Generic;
using System.Threading;
using vxlapi_NET;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// Vector XL/CANoe LIN 硬件适配（vxlapi_NET.dll 的 XL_Lin* API）
    /// 完全独立于 CanOe_API：独立 XLDriver 实例与独立 portHandle，与 CAN 通道互不干扰
    /// </summary>
    internal sealed class XlLinHardware
    {
        /// <summary>独立端口号（CanOe_API 用 2，此处固定 3 避免冲突）</summary>
        private const int LinPortHandle = 3;
        private const string AppName = "CANInsight_LIN";

        private readonly byte _logicChannel;
        private readonly LinChannel _cfg;
        private readonly XLDriver _xlDriver = new XLDriver();
        private int _portHandle = LinPortHandle;
        private ulong _channelMask;
        private volatile bool _running;
        private Thread _recvThread;
        private int _consecutiveErrors;

        public XlLinHardware(byte logicChannel, LinChannel cfg)
        {
            _logicChannel = logicChannel;
            _cfg = cfg;
        }

        public bool IsConnected => _running;

        // ==================== 枚举 ====================

        /// <summary>枚举系统内全部 Vector LIN 通道，返回 "ch {index}" 列表</summary>
        public static List<string> EnumerateChannels()
        {
            var result = new List<string>();
            try
            {
                var driver = new XLDriver();
                if (driver.XL_OpenDriver() != XLDefine.XL_Status.XL_SUCCESS) return result;
                try
                {
                    var cfg = new XLClass.xl_driver_config();
                    if (driver.XL_GetDriverConfig(ref cfg) == XLDefine.XL_Status.XL_SUCCESS)
                    {
                        for (int i = 0; i < cfg.channelCount; i++)
                        {
                            if ((cfg.channel[i].channelBusCapabilities & XLDefine.XL_BusCapabilities.XL_BUS_ACTIVE_CAP_LIN) != 0)
                            {
                                result.Add("ch " + cfg.channel[i].channelIndex);
                            }
                        }
                    }
                }
                finally
                {
                    driver.XL_CloseDriver();
                }
            }
            catch { }
            return result;
        }

        // ==================== 连接/断开 ====================

        /// <summary>连接：打开驱动 → 定位 LIN 通道 → 配置模式/波特率 → 逐 ID 配置 DLC/校验和 → 激活 → 启动接收线程</summary>
        public string Connect()
        {
            bool driverOpened = false;
            bool portOpened = false;
            try
            {
                int channelIndex = -1;
                if (_cfg.HwHandle.StartsWith("ch ", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(_cfg.HwHandle.Substring(3).Trim(), out channelIndex);
                }
                if (channelIndex < 0) return $"LIN 通道标识非法: {_cfg.HwHandle}";

                XLDefine.XL_Status status = _xlDriver.XL_OpenDriver();
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "打开 Vector XL 驱动失败: " + status;
                driverOpened = true;

                // 确认通道存在且为 LIN 类型
                var driverConfig = new XLClass.xl_driver_config();
                status = _xlDriver.XL_GetDriverConfig(ref driverConfig);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "读取 Vector 驱动配置失败: " + status;
                bool found = false;
                for (int i = 0; i < driverConfig.channelCount; i++)
                {
                    if (driverConfig.channel[i].channelIndex == channelIndex)
                    {
                        if ((driverConfig.channel[i].channelBusCapabilities & XLDefine.XL_BusCapabilities.XL_BUS_ACTIVE_CAP_LIN) == 0)
                            return $"通道 ch {channelIndex} 不是 LIN 通道";
                        found = true;
                        break;
                    }
                }
                if (!found) return $"未找到 Vector LIN 通道 ch {channelIndex}（硬件未连接或已被其他软件占用）";

                _channelMask = 1UL << channelIndex;
                ulong permissionMask = _channelMask;
                status = _xlDriver.XL_OpenPort(ref _portHandle, AppName, _channelMask, ref permissionMask, 4096,
                    XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION_V4, XLDefine.XL_BusTypes.XL_BUS_TYPE_LIN);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "打开 Vector LIN 端口失败: " + status;
                portOpened = true;

                // 通道参数：模式（主/从）+ 波特率 + LIN 2.1（增强校验）
                var linStat = new XLClass.xl_linStatPar
                {
                    LINMode = _cfg.Mode == LinNodeMode.Master ? XLDefine.XL_LIN_Mode.XL_LIN_MASTER : XLDefine.XL_LIN_Mode.XL_LIN_SLAVE,
                    baudrate = (int)_cfg.Baudrate,
                    LINVersion = XLDefine.XL_LIN_Version.XL_LIN_VERSION_2_1,
                    reserved = 0,
                };
                status = _xlDriver.XL_LinSetChannelParams(_portHandle, _channelMask, linStat);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN 通道参数失败: " + status;

                // 逐 ID 配置 DLC 与校验和模型（须在激活前，LIN 2.x 节点增强校验：
                // 不配置则硬件按 LIN 1.x 经典校验，增强校验帧会被误标 CRC 错误）
                status = _xlDriver.XL_LinSetDLC(_portHandle, _channelMask, BuildDlcArray());
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN DLC 失败: " + status;
                status = _xlDriver.XL_LinSetChecksum(_portHandle, _channelMask, BuildChecksumArray());
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN 校验和模型失败: " + status;

                status = _xlDriver.XL_ActivateChannel(_portHandle, _channelMask, XLDefine.XL_BusTypes.XL_BUS_TYPE_LIN, XLDefine.XL_AC_Flags.XL_ACTIVATE_RESET_CLOCK);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "激活 LIN 通道失败: " + status;

                // 从节点模式：按 LDF 配置硬件自动应答
                if (_cfg.Mode == LinNodeMode.Slave && _cfg.LdfHelper != null)
                {
                    foreach (byte pid in _cfg.LdfHelper.SlaveRespIds)
                    {
                        var def = _cfg.LdfHelper.Frames[pid];
                        ConfigureSlaveResponse(pid, new byte[def.Dlc], def.Dlc);
                    }
                }

                _running = true;
                _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = $"XLLIN_Rx_CH{_logicChannel}" };
                _recvThread.Start();
                return "";
            }
            catch (Exception ex)
            {
                return "Vector LIN 连接异常: " + ex.Message;
            }
            finally
            {
                // 失败路径集中清理（成功路径由 Disconnect 负责）
                if (!_running)
                {
                    if (portOpened) { try { _xlDriver.XL_ClosePort(_portHandle); } catch { } }
                    if (driverOpened) { try { _xlDriver.XL_CloseDriver(); } catch { } }
                }
            }
        }

        /// <summary>逐 ID DLC 数组（60 项，LDF 命中按定义，缺省 8）</summary>
        private byte[] BuildDlcArray()
        {
            var arr = new byte[60];
            for (int i = 0; i < arr.Length; i++) arr[i] = 8;
            if (_cfg.LdfHelper != null)
            {
                foreach (var kv in _cfg.LdfHelper.Frames)
                {
                    if (kv.Key < arr.Length && kv.Value.Dlc > 0) arr[kv.Key] = kv.Value.Dlc;
                }
            }
            return arr;
        }

        /// <summary>逐 ID 校验和模型数组（60 项全增强=2，LIN 2.x）</summary>
        private byte[] BuildChecksumArray()
        {
            var arr = new byte[60];
            for (int i = 0; i < arr.Length; i++) arr[i] = 2; // XL_LINChecksum: 增强
            return arr;
        }

        public void Disconnect()
        {
            _running = false;
            try { if (_recvThread != null && _recvThread.IsAlive) _recvThread.Join(500); } catch { }
            try
            {
                if (_channelMask != 0)
                    _xlDriver.XL_DeactivateChannel(_portHandle, _channelMask);
                _xlDriver.XL_ClosePort(_portHandle);
                _xlDriver.XL_CloseDriver();
            }
            catch { }
            _channelMask = 0;
        }

        // ==================== 发送 ====================

        /// <summary>
        /// 发送一帧 Header（主节点调度/手动发送用）。
        /// 本机作为发布者的数据由 ConfigureSlaveResponse 预置，硬件在 Header 后自动补响应。
        /// </summary>
        public bool SendRequest(byte pid)
        {
            if (!IsConnected) return false;
            return _xlDriver.XL_LinSendRequest(_portHandle, _channelMask, pid, 0) == XLDefine.XL_Status.XL_SUCCESS;
        }

        /// <summary>配置本机对指定帧 ID 的响应数据（主节点发布数据 / 从节点自动应答共用）</summary>
        public bool ConfigureSlaveResponse(byte pid, byte[] data, byte dlc)
        {
            if (!IsConnected) return false;
            return _xlDriver.XL_LinSetSlave(_portHandle, _channelMask, pid, data, dlc,
                XLDefine.XL_LIN_CalcChecksum.XL_LIN_CALC_CHECKSUM_ENHANCED) == XLDefine.XL_Status.XL_SUCCESS;
        }

        /// <summary>停用响应：硬件不再应答该 ID（XL_LIN_SLAVE_OFF）</summary>
        public bool DisableResponse(byte pid)
        {
            if (!IsConnected) return false;
            return _xlDriver.XL_LinSwitchSlave(_portHandle, _channelMask, pid, XLDefine.XL_LIN_SlaveMode.XL_LIN_SLAVE_OFF) == XLDefine.XL_Status.XL_SUCCESS;
        }

        /// <summary>更新从节点/发布帧数据</summary>
        public bool UpdateSlaveData(byte pid, byte[] data, byte dlc)
        {
            return ConfigureSlaveResponse(pid, data, dlc);
        }

        /// <summary>手动发送完整帧：预置响应数据 + 发 Header（硬件自动补数据与校验和）</summary>
        public bool Transmit(byte pid, byte[] data, LinChecksumKind ck)
        {
            if (!IsConnected) return false;
            byte dlc = (byte)(data == null ? 0 : data.Length);
            if (dlc > 8) return false;
            if (dlc > 0 && !ConfigureSlaveResponse(pid, data, dlc)) return false;
            return SendRequest(pid);
        }

        // ==================== 调度（软件调度原语，LinScheduler 驱动） ====================

        public bool StartSchedule(List<LinScheduleSlot> slots) => true; // Vector 调度由软件 LinScheduler 驱动，无硬件级调度
        public bool SuspendSchedule() => true;
        public bool ResumeSchedule() => true;

        // ==================== 唤醒/休眠 ====================

        public bool WakeUp() => IsConnected && _xlDriver.XL_LinWakeUp(_portHandle, _channelMask) == XLDefine.XL_Status.XL_SUCCESS;

        public bool SleepCommand()
        {
            if (!IsConnected) return false;
            return _xlDriver.XL_LinSetSleepMode(_portHandle, _channelMask, XLDefine.XL_LIN_SleepMode.XL_LIN_SET_SILENT, 0) == XLDefine.XL_Status.XL_SUCCESS;
        }

        // ==================== 接收线程 ====================

        private void ReceiveLoop()
        {
            var evt = new XLClass.xl_event();
            while (_running)
            {
                int processed = 0;
                while (_running && processed < 100) // 单次上限防饿死，照 CanOe_API 思路
                {
                    XLDefine.XL_Status status = _xlDriver.XL_Receive(_portHandle, ref evt);
                    if (status == XLDefine.XL_Status.XL_ERR_QUEUE_IS_EMPTY) break;
                    if (status != XLDefine.XL_Status.XL_SUCCESS)
                    {
                        _consecutiveErrors++;
                        if (_consecutiveErrors >= 10)
                        {
                            _consecutiveErrors = 0;
                            Lin_API.OnLinkLost(_logicChannel, "Vector 接收错误: " + status, this);
                        }
                        break;
                    }
                    _consecutiveErrors = 0;
                    processed++;
                    HandleEvent(evt);
                }
                if (_running) Thread.Sleep(1);
            }
        }

        private void HandleEvent(XLClass.xl_event evt)
        {
            switch (evt.tag)
            {
                case XLDefine.XL_EventTags.XL_LIN_MSG:
                    OnLinMsg(evt.tagData.linMsgApi.linMsg);
                    break;
                case XLDefine.XL_EventTags.XL_LIN_NOANS:
                    // Header 已发，无应答
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(evt.tagData.linMsgApi.linNoAns.id, LinErrorKind.NoResponse));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_CRCINFO:
                    // 校验和错误（id + flags）
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(evt.tagData.linMsgApi.linCRCinfo.id, LinErrorKind.Checksum));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_SYNCERR:
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(0xFF, LinErrorKind.Sync));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_ERRMSG:
                    // 总线错误事件（短路等）——总线状态事件
                    Lin_API.OnBusEvent(_logicChannel, "Error");
                    break;
                case XLDefine.XL_EventTags.XL_LIN_WAKEUP:
                    Lin_API.OnBusEvent(_logicChannel, "WakeUp");
                    break;
                case XLDefine.XL_EventTags.XL_LIN_SLEEP:
                    Lin_API.OnBusEvent(_logicChannel, "Sleep");
                    break;
                default:
                    break; // 其他事件（CAN 等）忽略——本端口只含 LIN 通道
            }
        }

        private void OnLinMsg(XLClass.xl_lin_msg msg)
        {
            var frame = new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = msg.id,
                Direction = (msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_TX) != 0 ? LinFrameDir.Tx : LinFrameDir.Rx,
                Dlc = msg.dlc,
                Data = msg.data != null && msg.data.Length > 0 ? (byte[])msg.data.Clone() : new byte[0],
                ChecksumType = LinChecksumKind.Enhanced,
                ChecksumRx = msg.crc,
                ChecksumOk = (msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_CRCERROR) == 0,
                FrameName = LinLdfHelper.GetFrameName(_cfg.LdfHelper, msg.id),
            };
            if (frame.Data.Length > 8) frame.Data = (byte[])frame.Data.Clone(); // 保险
            if ((msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_CRCERROR) != 0)
                frame.ErrorKind = LinErrorKind.Checksum;
            Lin_API.LinReceive(_logicChannel, frame);
        }

        private LinFrameRecord MakeErrorFrame(byte pid, LinErrorKind kind)
        {
            return new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = pid,
                Direction = LinFrameDir.Rx,
                Dlc = 0,
                Data = new byte[0],
                ChecksumType = LinChecksumKind.Enhanced,
                ChecksumRx = 0,
                ChecksumOk = false,
                ErrorKind = kind,
                FrameName = LinLdfHelper.GetFrameName(_cfg.LdfHelper, pid),
            };
        }
    }
}
