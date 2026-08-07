using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// PEAK LIN 硬件适配（PLinApi.dll）：枚举/连接/收发/帧定义/硬件调度表/唤醒休眠/状态轮询
    /// 支持 PCAN 硬件内置 LIN 通道（如 PCAN-USB Pro FD 的 LIN0/LIN1）与独立 PLIN-USB
    /// </summary>
    internal sealed class PcanLinHardware
    {
        private readonly byte _logicChannel;
        private readonly LinChannel _cfg;
        private byte _client = LinPlApi.INVALID_LIN_HANDLE;
        private ushort _hw = LinPlApi.INVALID_LIN_HANDLE;
        private volatile bool _running;
        private Thread _recvThread;
        private int _consecutiveErrors;

        public PcanLinHardware(byte logicChannel, LinChannel cfg)
        {
            _logicChannel = logicChannel;
            _cfg = cfg;
        }

        public bool IsConnected => _client != LinPlApi.INVALID_LIN_HANDLE && _hw != LinPlApi.INVALID_LIN_HANDLE;

        // ==================== 枚举 ====================

        /// <summary>
        /// 枚举系统内全部 PEAK LIN 通道，返回 "{硬件名}:LIN{通道号}" 列表与错误说明（空=成功）
        /// 错误码 1002（errManagerNotLoaded）说明 PEAK 驱动未安装 PLIN Manager 组件
        /// </summary>
        public static Tuple<List<string>, string> EnumerateChannels()
        {
            var result = new List<string>();
            try
            {
                ushort count = 0;
                LinPlError err = LinPlApi.GetAvailableHardware(null, 0, out count);
                if (err != LinPlError.errOK)
                    return Tuple.Create(result, LinPlErrorCodes.ToChinese(err));
                if (count == 0)
                    return Tuple.Create(result, "未找到 PEAK LIN 硬件（确认硬件为带 LIN 通道的型号且已连接）");
                var handles = new ushort[count];
                err = LinPlApi.GetAvailableHardware(handles, (ushort)(count * 2), out count);
                if (err != LinPlError.errOK)
                    return Tuple.Create(result, LinPlErrorCodes.ToChinese(err));
                for (int i = 0; i < count; i++)
                {
                    string name = GetHwName(handles[i]);
                    int ch = GetHwChannelNumber(handles[i]);
                    result.Add($"{name}:LIN{ch}");
                }
            }
            catch (Exception ex)
            {
                return Tuple.Create(result, "PLinApi 枚举异常: " + ex.Message);
            }
            return Tuple.Create(result, "");
        }

        private static string GetHwName(ushort hw)
        {
            try
            {
                var sb = new System.Text.StringBuilder(LinPlApi.LIN_MAX_NAME_LENGTH);
                if (LinPlApi.GetHardwareParam(hw, LinPlHardwareParam.hwpName, sb, (ushort)sb.Capacity) == LinPlError.errOK)
                    return sb.ToString();
            }
            catch { }
            return "PEAK-LIN";
        }

        private static int GetHwChannelNumber(ushort hw)
        {
            try
            {
                int ch = 0;
                if (LinPlApi.GetHardwareParam(hw, LinPlHardwareParam.hwpChannelNumber, out ch, 4) == LinPlError.errOK)
                    return ch;
            }
            catch { }
            return 0;
        }

        // ==================== 连接/断开 ====================

        /// <summary>连接：注册客户端 → 定位硬件句柄 → 初始化模式/波特率 → 配置帧条目 → 启动接收线程</summary>
        public string Connect()
        {
            try
            {
                LinPlError err = LinPlApi.RegisterClient("CANInsight_LIN", IntPtr.Zero, out _client);
                if (err != LinPlError.errOK) return LinPlErrorCodes.ToChinese(err);
                if (_client == LinPlApi.INVALID_LIN_HANDLE) return "注册 PLIN 客户端失败（句柄无效）";

                // 按 HwHandle 定位硬件句柄
                ushort count = 0;
                err = LinPlApi.GetAvailableHardware(null, 0, out count);
                if (err != LinPlError.errOK || count == 0) { CleanupClient(); return "未找到 PEAK LIN 硬件（检查硬件连接与驱动）"; }
                var handles = new ushort[count];
                err = LinPlApi.GetAvailableHardware(handles, (ushort)(count * 2), out count);
                if (err != LinPlError.errOK) { CleanupClient(); return LinPlErrorCodes.ToChinese(err); }

                bool found = false;
                foreach (ushort hw in handles)
                {
                    string hwHandle = $"{GetHwName(hw)}:LIN{GetHwChannelNumber(hw)}";
                    if (string.Equals(hwHandle, _cfg.HwHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        _hw = hw;
                        found = true;
                        break;
                    }
                }
                if (!found) { CleanupClient(); return $"未找到配置的 LIN 通道 {_cfg.HwHandle}（硬件未连接或已被其他软件占用）"; }

                err = LinPlApi.ConnectClient(_client, _hw);
                if (err != LinPlError.errOK) { CleanupClient(); return LinPlErrorCodes.ToChinese(err); }

                // 初始化：模式 + 波特率（PLIN 硬件波特率直接传值 1000-20000）
                // 按官方示例：仅当硬件未初始化（modNone）或波特率不同时才调用，
                // 避免重初始化其他客户端正在使用的连接
                LinPlHardwareMode mode = _cfg.Mode == LinNodeMode.Master ? LinPlHardwareMode.modMaster : LinPlHardwareMode.modSlave;
                int hwMode = -1, hwBaud = -1;
                LinPlApi.GetHardwareParam(_hw, LinPlHardwareParam.hwpMode, out hwMode, 4);
                LinPlApi.GetHardwareParam(_hw, LinPlHardwareParam.hwpBaudrate, out hwBaud, 4);
                if ((LinPlHardwareMode)hwMode == LinPlHardwareMode.modNone || hwBaud != (int)_cfg.Baudrate)
                {
                    err = LinPlApi.InitializeHardware(_client, _hw, mode, (ushort)_cfg.Baudrate);
                    if (err != LinPlError.errOK) { CleanupClient(); return LinPlErrorCodes.ToChinese(err); }
                }

                // 官方序列：连接后设置客户端过滤器（全 ID 接收，0-63 每位一帧）
                err = LinPlApi.SetClientFilter(_client, _hw, 0xFFFFFFFFFFFFFFFF);
                if (err != LinPlError.errOK && err != LinPlError.errWrongParameterType) { CleanupClient(); return LinPlErrorCodes.ToChinese(err); }

                // 接收全部帧 ID（0-63）
                err = LinPlApi.RegisterFrameId(_client, _hw, 0, LinPlApi.LIN_MAX_FRAME_ID);
                if (err != LinPlError.errOK && err != LinPlError.errIllegalFrameID) { CleanupClient(); return LinPlErrorCodes.ToChinese(err); }

                ConfigureFrameEntries();

                _running = true;
                _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = $"PLIN_Rx_CH{_logicChannel}" };
                _recvThread.Start();
                return "";
            }
            catch (Exception ex)
            {
                CleanupClient();
                return "PEAK LIN 连接异常: " + ex.Message;
            }
        }

        /// <summary>连接失败路径清理：移除已注册的客户端，避免泄漏</summary>
        private void CleanupClient()
        {
            try
            {
                if (_client != LinPlApi.INVALID_LIN_HANDLE)
                {
                    LinPlApi.RemoveClient(_client);
                }
            }
            catch { }
            _client = LinPlApi.INVALID_LIN_HANDLE;
            _hw = LinPlApi.INVALID_LIN_HANDLE;
        }

        public void Disconnect()
        {
            _running = false;
            try { if (_recvThread != null && _recvThread.IsAlive) _recvThread.Join(500); } catch { }
            try
            {
                if (_client != LinPlApi.INVALID_LIN_HANDLE && _hw != LinPlApi.INVALID_LIN_HANDLE)
                {
                    LinPlApi.DisconnectClient(_client, _hw);
                }
                if (_client != LinPlApi.INVALID_LIN_HANDLE)
                {
                    LinPlApi.RemoveClient(_client);
                }
            }
            catch { }
            _client = LinPlApi.INVALID_LIN_HANDLE;
            _hw = LinPlApi.INVALID_LIN_HANDLE;
        }

        /// <summary>
        /// 按 LDF 配置硬件帧条目：主节点发布帧 = dirPublisher（硬件发 Header+数据）；
        /// 从节点模式 = dirPublisher + RESPONSE_ENABLE（硬件自动应答）。无 LDF 时跳过（发送时按需建条目）。
        /// </summary>
        private void ConfigureFrameEntries()
        {
            if (_cfg.LdfHelper == null || _cfg.LdfHelper.Frames.Count == 0) return;
            foreach (var kv in _cfg.LdfHelper.Frames)
            {
                byte pid = kv.Key;
                var def = kv.Value;
                bool publisher = def.Publisher == _cfg.LdfHelper.MasterName;
                // 从节点模式：所有从节点发布帧配置为自动应答；主节点模式：主节点发布帧由本机发布
                bool slaveResp = _cfg.Mode == LinNodeMode.Slave && !publisher;
                if (publisher || slaveResp)
                {
                    SetFrameEntry(pid, def.Dlc, slaveResp, def.Publisher.Length == 0 ? null : def.Publisher);
                }
            }
        }

        private bool SetFrameEntry(byte pid, byte len, bool responseEnable, string publisher)
        {
            var entry = new LinPlFrameEntry
            {
                FrameId = pid,
                Length = len,
                Direction = LinPlDirection.dirPublisher,
                ChecksumType = LinPlChecksumType.cstEnhanced,
                Flags = (ushort)(responseEnable ? LinPlApi.FRAME_FLAG_RESPONSE_ENABLE : 0),
                InitialData = new byte[8],
            };
            return LinPlApi.SetFrameEntry(_client, _hw, ref entry) == LinPlError.errOK;
        }

        // ==================== 发送 ====================

        /// <summary>发送一帧（Master 模式发 Header+数据；从节点模式发布响应数据）</summary>
        public bool Transmit(byte pid, byte[] data, LinChecksumKind ck)
        {
            if (!IsConnected) return false;
            byte len = (byte)(data == null ? 0 : data.Length);
            if (len > 8) return false;
            var msg = new LinPlMsg
            {
                FrameId = pid,
                Length = len,
                Direction = LinPlDirection.dirPublisher,
                ChecksumType = ck == LinChecksumKind.Classic ? LinPlChecksumType.cstClassic : LinPlChecksumType.cstEnhanced,
                Data = new byte[8],
            };
            if (data != null) Array.Copy(data, msg.Data, len);
            msg.Checksum = LinChecksum.Calculate(data, pid, ck == LinChecksumKind.Enhanced);
            return LinPlApi.Write(_client, _hw, ref msg) == LinPlError.errOK;
        }

        /// <summary>更新从节点发布帧数据（硬件自动应答内容）；帧条目缺失或被禁用时先重建 RESPONSE_ENABLE 条目</summary>
        public bool UpdateSlaveData(byte pid, byte[] data)
        {
            if (!IsConnected || data == null || data.Length > 8) return false;
            // 确保帧条目为 Publisher + RESPONSE_ENABLE（被 DisableResponse 置 dirDisabled 后需恢复）
            var entry = new LinPlFrameEntry
            {
                FrameId = pid,
                Length = (byte)data.Length,
                Direction = LinPlDirection.dirPublisher,
                ChecksumType = LinPlChecksumType.cstEnhanced,
                Flags = LinPlApi.FRAME_FLAG_RESPONSE_ENABLE,
                InitialData = new byte[8],
            };
            Array.Copy(data, entry.InitialData, data.Length);
            LinPlError err = LinPlApi.SetFrameEntry(_client, _hw, ref entry);
            if (err != LinPlError.errOK) return false;
            return LinPlApi.UpdateByteArray(_client, _hw, pid, 0, (byte)data.Length, data) == LinPlError.errOK;
        }

        /// <summary>停用响应：帧条目方向置禁用（硬件不再自动应答该 ID）</summary>
        public bool DisableResponse(byte pid)
        {
            if (!IsConnected) return false;
            var entry = new LinPlFrameEntry
            {
                FrameId = pid,
                Length = 8,
                Direction = LinPlDirection.dirDisabled,
                ChecksumType = LinPlChecksumType.cstEnhanced,
                Flags = 0,
                InitialData = new byte[8],
            };
            return LinPlApi.SetFrameEntry(_client, _hw, ref entry) == LinPlError.errOK;
        }

        // ==================== 调度表（硬件自主运行） ====================

        /// <summary>下发调度槽到硬件并启动（slot 0；无条件帧）</summary>
        public bool StartSchedule(List<LinScheduleSlot> slots)
        {
            if (!IsConnected) return false;
            var active = new List<LinScheduleSlot>(slots);
            var arr = new LinPlScheduleSlot[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                arr[i] = new LinPlScheduleSlot
                {
                    Type = LinPlSlotType.sltUnconditional,
                    Delay = (ushort)active[i].SlotMs,
                    FrameId = new byte[8],
                    CountResolve = 1,
                };
                arr[i].FrameId[0] = active[i].Pid;
            }
            LinPlError err = LinPlApi.SetSchedule(_client, _hw, 0, arr, arr.Length);
            if (err != LinPlError.errOK) return false;
            return LinPlApi.StartSchedule(_client, _hw, 0) == LinPlError.errOK;
        }

        public bool SuspendSchedule() => IsConnected && LinPlApi.SuspendSchedule(_client, _hw) == LinPlError.errOK;
        public bool ResumeSchedule() => IsConnected && LinPlApi.ResumeSchedule(_client, _hw) == LinPlError.errOK;
        public bool StopSchedule() => IsConnected && LinPlApi.SuspendSchedule(_client, _hw) == LinPlError.errOK;

        // ==================== 唤醒/休眠/状态 ====================

        public bool WakeUp() => IsConnected && LinPlApi.XmtWakeUp(_client, _hw) == LinPlError.errOK;

        /// <summary>发送休眠命令（诊断帧 0x3C 数据 0x00）</summary>
        public bool SleepCommand()
        {
            if (!IsConnected) return false;
            return Transmit(0x3C, new byte[] { 0x00 }, LinChecksumKind.Enhanced);
        }

        /// <summary>轮询总线状态（Active/Sleep/短路等）</summary>
        public LinPlHardwareState GetBusState()
        {
            if (!IsConnected) return LinPlHardwareState.hwsNotInitialized;
            try
            {
                LinPlHardwareStatus st;
                if (LinPlApi.GetStatus(_hw, out st) == LinPlError.errOK) return st.Status;
            }
            catch { }
            return LinPlHardwareState.hwsNotInitialized;
        }

        // ==================== 接收线程 ====================

        private void ReceiveLoop()
        {
            var buf = new LinPlRcvMsg[32];
            while (_running)
            {
                int count = 0;
                LinPlError err = LinPlApi.ReadMulti(_client, buf, buf.Length, out count);
                if (err == LinPlError.errOK && count > 0)
                {
                    _consecutiveErrors = 0;
                    for (int i = 0; i < count; i++) HandleRcvMsg(buf[i]);
                    continue; // 立即再读，尽量排空硬件队列
                }
                if (err == LinPlError.errRcvQueueEmpty)
                {
                    // 队列空：5ms 轮询（PLIN 管理器接收事件句柄是 8 字节 HANDLE，
                    // 经 out int 获取有 x64 越界写风险且需先 CreateEvent+SetClientParam，第一版用轮询）
                    Thread.Sleep(5);
                }
                else
                {
                    _consecutiveErrors++;
                    if (_consecutiveErrors >= 10)
                    {
                        _consecutiveErrors = 0;
                        Lin_API.OnLinkLost(_logicChannel, LinPlErrorCodes.ToChinese(err), this);
                    }
                    Thread.Sleep(20);
                }
            }
        }

        private void HandleRcvMsg(LinPlRcvMsg m)
        {
            // 总线状态帧：不产生报文行，仅更新状态
            if (m.Type != LinPlMsgType.mstStandard)
            {
                if (m.Type == LinPlMsgType.mstBusSleep || m.Type == LinPlMsgType.mstBusWakeUp || m.Type == LinPlMsgType.mstOverrun)
                {
                    Lin_API.OnBusEvent(_logicChannel, m.Type == LinPlMsgType.mstBusSleep ? "Sleep" : m.Type == LinPlMsgType.mstBusWakeUp ? "WakeUp" : "Overrun");
                }
                return;
            }

            var frame = new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = m.FrameId,
                Direction = LinFrameDir.Rx,
                Dlc = m.Length,
                Data = new byte[m.Length],
                ChecksumType = m.ChecksumType == LinPlChecksumType.cstClassic ? LinChecksumKind.Classic : LinChecksumKind.Enhanced,
                ChecksumRx = m.Checksum,
                ChecksumOk = (m.ErrorFlags & LinPlMsgErrors.Checksum) == 0,
                FrameName = LinLdfHelper.GetFrameName(_cfg.LdfHelper, m.FrameId),
            };
            if (m.Length > 0) Array.Copy(m.Data, frame.Data, m.Length);

            // 错误标志 → 错误类型（TLINMsgErrors 位）
            if ((m.ErrorFlags & LinPlMsgErrors.Checksum) != 0) frame.ErrorKind = LinErrorKind.Checksum;
            else if ((m.ErrorFlags & LinPlMsgErrors.InconsistentSynch) != 0 || (m.ErrorFlags & LinPlMsgErrors.IdParityBit0) != 0 || (m.ErrorFlags & LinPlMsgErrors.IdParityBit1) != 0)
                frame.ErrorKind = LinErrorKind.Sync;
            else if ((m.ErrorFlags & LinPlMsgErrors.SlaveNOtResponding) != 0 || (m.ErrorFlags & LinPlMsgErrors.Timeout) != 0)
                frame.ErrorKind = LinErrorKind.NoResponse;
            else if ((m.ErrorFlags & (LinPlMsgErrors.GroundShort | LinPlMsgErrors.VBatShort | LinPlMsgErrors.SlotDelay)) != 0)
                frame.ErrorKind = LinErrorKind.Hw;
            // OtherResponse（其他节点响应）：正常帧语义，不标错

            Lin_API.LinReceive(_logicChannel, frame);
        }
    }
}
