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
        private volatile bool _active;
        private volatile bool _running;
        private Thread _recvThread;
        private int _consecutiveErrors;
        private readonly Dictionary<byte, byte[]> _frameData = new Dictionary<byte, byte[]>();

        public XlLinHardware(byte logicChannel, LinChannel cfg)
        {
            _logicChannel = logicChannel;
            _cfg = cfg;
        }

        public bool IsConnected => _active;

        // ==================== 枚举 ====================

        /// <summary>枚举系统内全部 Vector LIN 通道，返回 "ch {index}" 列表与错误说明（空=成功）</summary>
        public static Tuple<List<string>, string> EnumerateChannels()
        {
            var result = new List<string>();
            try
            {
                var driver = new XLDriver();
                if (driver.XL_OpenDriver() != XLDefine.XL_Status.XL_SUCCESS)
                    return Tuple.Create(result, "打开 Vector XL 驱动失败（驱动未安装或无 Vector 硬件）");
                try
                {
                    var cfg = new XLClass.xl_driver_config();
                    if (driver.XL_GetDriverConfig(ref cfg) == XLDefine.XL_Status.XL_SUCCESS)
                    {
                        for (int i = 0; i < cfg.channelCount; i++)
                        {
                            if ((cfg.channel[i].channelBusCapabilities & XLDefine.XL_BusCapabilities.XL_BUS_ACTIVE_CAP_LIN) != 0)
                            {
                                // 显示 1-based 通道号（对齐 CAN 侧 Hw=channelIndex+1 约定，与设备丝印一致）；
                                // 连接时解析回 channelIndex = N-1
                                result.Add("ch " + (cfg.channel[i].channelIndex + 1));
                            }
                        }
                    }
                }
                finally
                {
                    driver.XL_CloseDriver();
                }
            }
            catch (Exception ex)
            {
                return Tuple.Create(result, "XL 枚举异常: " + ex.Message);
            }
            if (result.Count == 0)
                return Tuple.Create(result, "未找到 Vector LIN 通道（无 Vector 硬件或通道未配置为 LIN）");
            return Tuple.Create(result, "");
        }

        // ==================== 连接/断开 ====================

        /// <summary>连接：打开驱动 → 定位 LIN 通道 → 配置模式/波特率 → 逐 ID 配置 DLC/校验和 → 激活 → 启动接收线程</summary>
        public string Connect()
        {
            bool driverOpened = false;
            bool portOpened = false;
            try
            {
                string planError = _cfg.ValidateTransmitPlan();
                if (planError.Length > 0) return planError;
                LinDebugLog.Write("[CONN] Vector Connect 开始: 逻辑通道=" + _logicChannel + " HwHandle=" + _cfg.HwHandle +
                    " Baud=" + _cfg.Baudrate + " TransmitEntries=" + (_cfg.TransmitEntries == null ? 0 : _cfg.TransmitEntries.Count));
                int channelIndex = -1;
                if (_cfg.HwHandle.StartsWith("ch ", StringComparison.OrdinalIgnoreCase))
                {
                    // HwHandle 为 1-based 显示号（枚举输出 "ch {channelIndex+1}"），转回 0-based channelIndex
                    int parsed;
                    if (int.TryParse(_cfg.HwHandle.Substring(3).Trim(), out parsed))
                        channelIndex = parsed - 1;
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
                // VN1600 系列（VN1640A 等）LIN 通道仅支持 V3 接口：V4 下 XL_BUS_TYPE_LIN 返回
                // XL_ERR_NOT_IMPLEMENTED（已实测：V3+LIN 打开/激活成功，V4+LIN/CAN 均失败）
                status = _xlDriver.XL_OpenPort(ref _portHandle, AppName, _channelMask, ref permissionMask, 4096,
                    XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION_V3, XLDefine.XL_BusTypes.XL_BUS_TYPE_LIN);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "打开 Vector LIN 端口失败: " + status;
                portOpened = true;

                // 连接模式一律 XL_LIN_MASTER（用户决策，与 PCAN 侧一致）：全发送类型可在线切换；
                // 纯 Slave 仿真的防误发由软件层负责（调度器跳过 Slave 槽 + SendScheduleFrame 拒绝发 Header）。
                var linVersion = GetLinVersion();
                var linStat = new XLClass.xl_linStatPar
                {
                    LINMode = XLDefine.XL_LIN_Mode.XL_LIN_MASTER,
                    baudrate = (int)_cfg.Baudrate,
                    LINVersion = linVersion,
                    reserved = 0,
                };
                string modeNotice = _cfg.GetHardwareModeNotice();
                if (modeNotice.Length > 0) LinDebugLog.Write("[CONN] 警告: " + modeNotice);
                status = _xlDriver.XL_LinSetChannelParams(_portHandle, _channelMask, linStat);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN 通道参数失败: " + status;

                // 逐 ID 配置 DLC 与校验和模型（须在激活前）。XL_LinSetChecksum
                // 仅适用于 LIN 2.x；LIN 1.3 固定使用经典校验且不调用该 API。
                status = _xlDriver.XL_LinSetDLC(_portHandle, _channelMask, BuildDlcArray());
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN DLC 失败: " + status;
                if (linVersion != XLDefine.XL_LIN_Version.XL_LIN_VERSION_1_3)
                {
                    status = _xlDriver.XL_LinSetChecksum(_portHandle, _channelMask, BuildChecksumArray());
                    if (status != XLDefine.XL_Status.XL_SUCCESS) return "配置 LIN 校验和模型失败: " + status;
                }

                // Vector 示例要求在激活通道前调用 XL_LinSetSlave。发送页明确选择
                // 为 Slave 的报文直接配置；旧的通道级从节点配置也在这里迁移，
                // 从机响应完全由发送页 Slave 项驱动（Vector 示例要求在激活通道前调用 XL_LinSetSlave）。
                if (_cfg.TransmitEntries != null)
                {
                    foreach (var transmit in _cfg.TransmitEntries)
                    {
                        if (transmit == null || !transmit.Enabled) continue;
                        if (transmit.Type != LinTransmitType.Slave) continue;
                        byte dlc = transmit.Dlc;
                        if (dlc == 0 && _cfg.LdfHelper != null) dlc = LinLdfHelper.GetFrameDlc(_cfg.LdfHelper, transmit.Pid);
                        if (dlc == 0) dlc = 8;
                        byte[] data = transmit.Data == null ? new byte[dlc] : (byte[])transmit.Data.Clone();
                        if (data.Length != dlc) Array.Resize(ref data, dlc);
                        if (!ConfigureSlaveResponse(transmit.Pid, data, dlc))
                            return "配置从节点响应失败: PID 0x" + transmit.Pid.ToString("X2");
                    }
                }

                status = _xlDriver.XL_ActivateChannel(_portHandle, _channelMask, XLDefine.XL_BusTypes.XL_BUS_TYPE_LIN, XLDefine.XL_AC_Flags.XL_ACTIVATE_NONE);
                if (status != XLDefine.XL_Status.XL_SUCCESS) return "激活 LIN 通道失败: " + status;
                _active = true;
                LinDebugLog.Write("[CONN] Vector 连接成功: 逻辑通道=" + _logicChannel + " chIdx=" + channelIndex +
                    " mask=0x" + _channelMask.ToString("X") + " mode=" + linStat.LINMode + " baud=" + _cfg.Baudrate +
                    " linVersion=" + linVersion + " Slave响应项=按发送项（XL_LinSetSlave 在线武装）");

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
                    _active = false;
                    if (portOpened) { try { _xlDriver.XL_ClosePort(_portHandle); } catch { } }
                    if (driverOpened) { try { _xlDriver.XL_CloseDriver(); } catch { } }
                    _channelMask = 0;
                }
            }
        }

        /// <summary>逐 ID DLC 数组（64 项，LDF 命中按定义，缺省 8）</summary>
        private byte[] BuildDlcArray()
        {
            var arr = new byte[64];
            for (int i = 0; i < arr.Length; i++) arr[i] = 8;
            if (_cfg.LdfHelper != null)
            {
                foreach (var kv in _cfg.LdfHelper.Frames)
                {
                    if (kv.Key < arr.Length && kv.Value.Dlc > 0) arr[kv.Key] = kv.Value.Dlc;
                }
            }
            foreach (var entry in _cfg.TransmitEntries ?? new List<LinTransmitEntry>())
            {
                if (entry != null && entry.Pid < arr.Length && entry.Dlc > 0 && entry.Dlc <= 8)
                    arr[entry.Pid] = entry.Dlc;
            }
            return arr;
        }

        /// <summary>逐 ID 校验和模型数组（64 项；Vector API：经典=1，增强=2）</summary>
        private byte[] BuildChecksumArray()
        {
            var arr = new byte[64];
            for (int i = 0; i < arr.Length; i++) arr[i] = 2;
            if (_cfg.LdfHelper != null)
            {
                foreach (var kv in _cfg.LdfHelper.Frames)
                {
                    if (kv.Key < arr.Length && LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, kv.Key) == LinChecksumKind.Classic)
                        arr[kv.Key] = 1;
                }
            }
            return arr;
        }

        private XLDefine.XL_LIN_Version GetLinVersion()
        {
            double version;
            if (_cfg.LdfHelper == null || !double.TryParse(_cfg.LdfHelper.ProtocolVersion,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out version))
                return XLDefine.XL_LIN_Version.XL_LIN_VERSION_2_1;
            if (version < 2.0) return XLDefine.XL_LIN_Version.XL_LIN_VERSION_1_3;
            return version < 2.1
                ? XLDefine.XL_LIN_Version.XL_LIN_VERSION_2_0
                : XLDefine.XL_LIN_Version.XL_LIN_VERSION_2_1;
        }

        public void Disconnect()
        {
            _running = false;
            _active = false;
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

        /// <summary>Vector API 没有公开独立 Break 发送原语。</summary>
        public bool SendBreakOnly(byte pid)
        {
            LinDebugLog.Write("[TX] BreakOnly pid=" + pid + " → Vector API 不支持独立 Break 原语");
            return false;
        }

        /// <summary>配置本机对指定帧 ID 的响应数据（主节点发布数据 / 从节点自动应答共用）</summary>
        public bool ConfigureSlaveResponse(byte pid, byte[] data, byte dlc)
        {
            // XL_LinSetSlave 按官方示例须在 XL_ActivateChannel 前调用；此时 _active
            // 尚未置位，但端口和通道掩码已经有效。
            if (_channelMask == 0 || data == null || dlc > 8 || data.Length < dlc) return false;
            var checksum = LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid) == LinChecksumKind.Classic
                ? XLDefine.XL_LIN_CalcChecksum.XL_LIN_CALC_CHECKSUM
                : XLDefine.XL_LIN_CalcChecksum.XL_LIN_CALC_CHECKSUM_ENHANCED;
            bool ok = _xlDriver.XL_LinSetSlave(_portHandle, _channelMask, pid, data, dlc,
                checksum) == XLDefine.XL_Status.XL_SUCCESS;
            if (ok)
            {
                lock (_frameData) { _frameData[pid] = (byte[])data.Clone(); }
            }
            return ok;
        }

        /// <summary>停用响应：硬件不再应答该 ID（XL_LIN_SLAVE_OFF）</summary>
        public bool DisableResponse(byte pid)
        {
            if (!IsConnected) return false;
            bool ok = _xlDriver.XL_LinSwitchSlave(_portHandle, _channelMask, pid, XLDefine.XL_LIN_SlaveMode.XL_LIN_SLAVE_OFF) == XLDefine.XL_Status.XL_SUCCESS;
            if (ok) lock (_frameData) { _frameData.Remove(pid); }
            return ok;
        }

        /// <summary>按发送项类型配置数据；Vector 的 Master 数据通过一次性 SetSlave + SendRequest 完成。</summary>
        public bool ConfigureTransmitEntry(byte pid, LinTransmitType type, byte[] data, byte dlc)
        {
            if (type == LinTransmitType.BreakOnly) return SendBreakOnly(pid);
            if (type == LinTransmitType.HeaderOnly)
            {
                // 防止此前的 Slave 配置继续响应该 ID。
                DisableResponse(pid);
                return true;
            }
            if (data == null) data = new byte[dlc == 0 ? 8 : dlc];
            if (dlc == 0) dlc = (byte)data.Length;
            if (type == LinTransmitType.Master)
            {
                // Master 数据缓存不应在连接层长期注册成 Slave 响应；发送时临时装载。
                lock (_frameData) _frameData[pid] = (byte[])data.Clone();
                return true;
            }
            return ConfigureSlaveResponse(pid, data, dlc);
        }

        /// <summary>旧调用兼容：已配置项按其类型更新，否则按 Slave 处理。</summary>
        public bool UpdateSlaveData(byte pid, byte[] data, byte dlc)
        {
            var configured = _cfg.FindTransmitEntry(pid);
            return ConfigureTransmitEntry(pid, configured == null ? LinTransmitType.Slave : configured.Type, data, dlc);
        }

        /// <summary>软件调度回显用的本地帧数据缓存。</summary>
        public byte[] GetFrameData(byte pid)
        {
            lock (_frameData)
            {
                byte[] data;
                return _frameData.TryGetValue(pid, out data) ? (byte[])data.Clone() : null;
            }
        }

        /// <summary>手动发送完整帧：临时装载数据响应、发 Header，再撤销临时响应。</summary>
        public bool Transmit(byte pid, byte[] data, LinChecksumKind ck)
        {
            if (!IsConnected) return false;
            byte dlc = (byte)(data == null ? 0 : data.Length);
            if (dlc > 8) return false;
            if (dlc == 0) dlc = 8;
            if (!ConfigureSlaveResponse(pid, data ?? new byte[dlc], dlc)) return false;
            bool ok = SendRequest(pid);
            // XL_LinSendRequest 已将当前请求放入驱动队列；撤销长期 Slave 响应，
            // 防止外部主节点随后用同一 PID 触发本机抢答。
            DisableResponse(pid);
            lock (_frameData) _frameData[pid] = data == null ? new byte[dlc] : (byte[])data.Clone();
            return ok;
        }

        /// <summary>软件调度按发送项类型执行一次操作。</summary>
        public bool SendScheduleFrame(byte pid, byte dlc, LinTransmitType type)
        {
            if (type == LinTransmitType.Slave)
            {
                LinDebugLog.Write("[TX] SendScheduleFrame pid=" + pid + " type=Slave → 等待外部 Header");
                return true;
            }
            if (type == LinTransmitType.HeaderOnly) return SendRequest(pid);
            if (type == LinTransmitType.BreakOnly) return SendBreakOnly(pid);
            byte[] data = GetFrameData(pid);
            byte len = dlc == 0 ? (byte)8 : dlc;
            if (data == null || data.Length != len) data = new byte[len];
            return Transmit(pid, data, LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid));
        }

        public bool SendScheduleFrame(byte pid, byte dlc)
        {
            var configured = _cfg.FindTransmitEntry(pid);
            return SendScheduleFrame(pid, dlc, configured == null ? LinTransmitType.Master : configured.Type);
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
                    // Header 已发，无应答（id 归一为裸 ID 后仅进日志/错误帧）
                    LinDebugLog.Write("[RX] Vector NOANS id=0x" + evt.tagData.linMsgApi.linNoAns.id.ToString("X2") +
                        " normPid=0x" + LinPidCodec.ToRawId(evt.tagData.linMsgApi.linNoAns.id).ToString("X2") + " → 无应答错误帧");
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(evt.tagData.linMsgApi.linNoAns.id, LinErrorKind.NoResponse));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_CRCINFO:
                    // 校验和错误（id + flags）
                    LinDebugLog.Write("[RX] Vector CRCINFO id=0x" + evt.tagData.linMsgApi.linCRCinfo.id.ToString("X2") +
                        " normPid=0x" + LinPidCodec.ToRawId(evt.tagData.linMsgApi.linCRCinfo.id).ToString("X2") +
                        " flags=0x" + ((int)evt.tagData.linMsgApi.linCRCinfo.flags).ToString("X8") + " → 校验和错误帧");
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(evt.tagData.linMsgApi.linCRCinfo.id, LinErrorKind.Checksum));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_SYNCERR:
                    LinDebugLog.Write("[RX] Vector SYNCERR → 同步错误帧");
                    Lin_API.LinReceive(_logicChannel, MakeErrorFrame(0xFF, LinErrorKind.Sync));
                    break;
                case XLDefine.XL_EventTags.XL_LIN_ERRMSG:
                    // 总线错误事件（短路等）——总线状态事件
                    LinDebugLog.Write("[RX] Vector ERRMSG 总线错误事件");
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
            // 硬件边界 ID 归一化（方案 4.3）：Vector xl_lin_msg.id 按受保护 PID 兼容处理——
            // 统一归一化为裸 ID 后再进入 LDF/UI/响应关联；原始 ID 只进诊断日志。
            // 若驱动实际返回裸 ID，掩码 &0x3F 为恒等，无副作用。
            byte rawWireId = msg.id;
            byte pid = LinPidCodec.ToRawId(rawWireId);
            // 长度校验：dlc>8 拒绝复制并上报硬件错误帧，防止越界克隆。
            if (msg.dlc > 8)
            {
                LinDebugLog.Write("[ERR] Vector 非法接收长度 pid=0x" + pid.ToString("X2") + " rawId=0x" + rawWireId.ToString("X2") + " dlc=" + msg.dlc + " → 按硬件错误帧上报");
                Lin_API.LinReceive(_logicChannel, MakeErrorFrame(pid, LinErrorKind.Hw));
                return;
            }
            var frame = new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = pid,
                Direction = (msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_TX) != 0 ? LinFrameDir.Tx : LinFrameDir.Rx,
                Dlc = msg.dlc,
                Data = msg.data != null && msg.data.Length > 0 ? (byte[])msg.data.Clone() : new byte[0],
                ChecksumType = LinChecksumKind.Enhanced,
                ChecksumRx = msg.crc,
                ChecksumOk = (msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_CRCERROR) == 0,
                FrameName = LinLdfHelper.GetFrameName(_cfg.LdfHelper, pid),
            };
            if (frame.Data.Length > 8) frame.Data = new byte[0]; // 保险：拒绝越界数据
            if ((msg.flags & XLDefine.XL_MessageFlags.XL_LIN_MSGFLAG_CRCERROR) != 0)
                frame.ErrorKind = LinErrorKind.Checksum;
            LinDebugLog.Write("[RX] Vector id=0x" + rawWireId.ToString("X2") + " normPid=0x" + pid.ToString("X2") +
                " len=" + msg.dlc + " dir=" + frame.Direction + " crc=0x" + msg.crc.ToString("X2") +
                " flags=0x" + ((int)msg.flags).ToString("X8"));
            Lin_API.LinReceive(_logicChannel, frame);
        }

        private LinFrameRecord MakeErrorFrame(byte pid, LinErrorKind kind)
        {
            // NOANS/CRCINFO 事件的 id 归一为裸 ID；SYNCERR 无帧 ID（0xFF 哨兵）保持原样显示。
            byte norm = pid == 0xFF ? pid : LinPidCodec.ToRawId(pid);
            return new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = norm,
                Direction = LinFrameDir.Rx,
                Dlc = 0,
                Data = new byte[0],
                ChecksumType = LinChecksumKind.Enhanced,
                ChecksumRx = 0,
                ChecksumOk = false,
                ErrorKind = kind,
                FrameName = LinLdfHelper.GetFrameName(_cfg.LdfHelper, norm),
            };
        }
    }
}
