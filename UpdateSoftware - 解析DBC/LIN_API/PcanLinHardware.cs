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
        /// <summary>软件调度帧数据缓存（pid → 数据，UpdateSlaveData 时更新，SendScheduleFrame 读取）</summary>
        private readonly Dictionary<byte, byte[]> _frameData = new Dictionary<byte, byte[]>();

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
            LinDebugLog.Open("ch" + _logicChannel + " " + _cfg.HwHandle);
            LinDebugLog.Write("[CONN] Connect 开始: HwHandle=" + _cfg.HwHandle + " Mode=" + _cfg.Mode + " Baud=" + _cfg.Baudrate);
            try
            {
                LinPlError err = LinPlApi.RegisterClient("CANInsight_LIN", IntPtr.Zero, out _client);
                LinDebugLog.Write("[CONN] RegisterClient → err=" + err + " client=" + _client);
                if (err != LinPlError.errOK) return "注册 PLIN 客户端失败: " + LinPlErrorCodes.ToChinese(err);
                if (_client == LinPlApi.INVALID_LIN_HANDLE) return "注册 PLIN 客户端失败（句柄无效）";

                // 按 HwHandle 定位硬件句柄
                ushort count = 0;
                err = LinPlApi.GetAvailableHardware(null, 0, out count);
                LinDebugLog.Write("[CONN] GetAvailableHardware(count) → err=" + err + " count=" + count);
                if (err != LinPlError.errOK || count == 0) { CleanupClient(); return "未找到 PEAK LIN 硬件（检查硬件连接与驱动、PLINDeviceManager 是否运行）"; }
                var handles = new ushort[count];
                err = LinPlApi.GetAvailableHardware(handles, (ushort)(count * 2), out count);
                LinDebugLog.Write("[CONN] GetAvailableHardware(list) → err=" + err + " count=" + count);
                if (err != LinPlError.errOK) { CleanupClient(); return "枚举 PEAK LIN 硬件失败: " + LinPlErrorCodes.ToChinese(err); }

                bool found = false;
                foreach (ushort hw in handles)
                {
                    string hwHandle = $"{GetHwName(hw)}:LIN{GetHwChannelNumber(hw)}";
                    LinDebugLog.Write("[CONN] 枚举硬件句柄 hw=" + hw + " name=" + hwHandle);
                    if (string.Equals(hwHandle, _cfg.HwHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        _hw = hw;
                        found = true;
                        break;
                    }
                }
                if (!found) { CleanupClient(); return $"未找到配置的 LIN 通道 {_cfg.HwHandle}（硬件未连接或已被其他软件占用）"; }

                err = LinPlApi.ConnectClient(_client, _hw);
                LinDebugLog.Write("[CONN] ConnectClient → err=" + err + " client=" + _client + " hw=" + _hw);
                if (err != LinPlError.errOK) { CleanupClient(); return "连接 LIN 硬件失败: " + LinPlErrorCodes.ToChinese(err); }

                // 初始化：模式 + 波特率（PLIN 硬件波特率直接传值 1000-20000）
                // 无条件初始化（官方序列）：上次会话/其他程序残留的初始化状态（模式/波特率恰好匹配时
                // 旧实现会跳过初始化）会让后续 SetClientFilter 稳定返回 errUnknown——实测仅重插设备可恢复，
                // 重新 InitializeHardware 可复位管理器对该硬件的过滤/客户端注册状态。重复初始化同参数为幂等操作。
                LinPlHardwareMode mode = _cfg.Mode == LinNodeMode.Master ? LinPlHardwareMode.modMaster : LinPlHardwareMode.modSlave;
                err = LinPlApi.InitializeHardware(_client, _hw, mode, (ushort)_cfg.Baudrate);
                LinDebugLog.Write("[CONN] InitializeHardware mode=" + mode + " baud=" + _cfg.Baudrate + " → err=" + err);
                if (err != LinPlError.errOK) { CleanupClient(); return "初始化 LIN 硬件失败（模式/波特率）: " + LinPlErrorCodes.ToChinese(err); }

                // 官方序列：连接后设置客户端过滤器（全 ID 接收，0-63 每位一帧；wFilterType=0 用管理器默认过滤类型）
                err = LinPlApi.SetClientFilter(_client, _hw, 0xFFFFFFFFFFFFFFFF, 0);
                LinDebugLog.Write("[CONN] SetClientFilter mask=FFFFFFFFFFFFFFFF type=0 → err=" + err);
                if (err != LinPlError.errOK && err != LinPlError.errWrongParameterType)
                {
                    CleanupClient();
                    return "设置接收过滤失败: " + LinPlErrorCodes.ToChinese(err) + "\n（PLIN 管理器状态异常时可重新插拔 PCAN 设备或重启「PLIN Device Manager」服务后重试）";
                }

                // 接收全部帧 ID（0-63）
                err = LinPlApi.RegisterFrameId(_client, _hw, 0, LinPlApi.LIN_MAX_FRAME_ID);
                LinDebugLog.Write("[CONN] RegisterFrameId 0..63 → err=" + err);
                if (err != LinPlError.errOK && err != LinPlError.errIllegalFrameID) { CleanupClient(); return "注册帧 ID 失败: " + LinPlErrorCodes.ToChinese(err); }

                ConfigureFrameEntries();

                _running = true;
                _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = $"PLIN_Rx_CH{_logicChannel}" };
                _recvThread.Start();
                LinDebugLog.Write("[CONN] Connect 成功（接收线程已启动），日志文件: " + LinDebugLog.LogPath);
                return "";
            }
            catch (Exception ex)
            {
                LinDebugLog.Write("[CONN] Connect 异常: " + ex);
                CleanupClient();
                return "PEAK LIN 连接异常: " + ex.Message;
            }
        }

        /// <summary>连接失败路径清理：断开客户端连接并移除注册，避免残留导致下次连接状态异常</summary>
        private void CleanupClient()
        {
            LinDebugLog.Write("[CONN] CleanupClient: client=" + _client + " hw=" + _hw);
            try
            {
                if (_client != LinPlApi.INVALID_LIN_HANDLE)
                {
                    if (_hw != LinPlApi.INVALID_LIN_HANDLE)
                    {
                        LinPlError err = LinPlApi.DisconnectClient(_client, _hw);
                        LinDebugLog.Write("[CONN] Cleanup DisconnectClient → err=" + err);
                    }
                    LinPlError err2 = LinPlApi.RemoveClient(_client);
                    LinDebugLog.Write("[CONN] Cleanup RemoveClient → err=" + err2);
                }
            }
            catch (Exception ex) { LinDebugLog.Write("[CONN] CleanupClient 异常: " + ex.Message); }
            _client = LinPlApi.INVALID_LIN_HANDLE;
            _hw = LinPlApi.INVALID_LIN_HANDLE;
        }

        public void Disconnect()
        {
            LinDebugLog.Write("[CONN] Disconnect: client=" + _client + " hw=" + _hw + " running=" + _running);
            _running = false;
            try { if (_recvThread != null && _recvThread.IsAlive) _recvThread.Join(500); } catch { }
            try
            {
                if (_client != LinPlApi.INVALID_LIN_HANDLE && _hw != LinPlApi.INVALID_LIN_HANDLE)
                {
                    LinPlError err = LinPlApi.DisconnectClient(_client, _hw);
                    LinDebugLog.Write("[CONN] Disconnect DisconnectClient → err=" + err);
                }
                if (_client != LinPlApi.INVALID_LIN_HANDLE)
                {
                    LinPlError err2 = LinPlApi.RemoveClient(_client);
                    LinDebugLog.Write("[CONN] Disconnect RemoveClient → err=" + err2);
                }
            }
            catch (Exception ex) { LinDebugLog.Write("[CONN] Disconnect 异常: " + ex.Message); }
            _client = LinPlApi.INVALID_LIN_HANDLE;
            _hw = LinPlApi.INVALID_LIN_HANDLE;
        }

        /// <summary>
        /// 按 LDF 配置硬件帧条目：主节点发布帧 = dirPublisher（硬件发 Header+数据）；
        /// 从节点模式 = dirPublisher + RESPONSE_ENABLE（硬件自动应答）。无 LDF 时跳过（发送时按需建条目）。
        /// </summary>
        private void ConfigureFrameEntries()
        {
            if (_cfg.LdfHelper == null || _cfg.LdfHelper.Frames.Count == 0)
            {
                LinDebugLog.Write("[SLAVE] ConfigureFrameEntries: 无 LDF，跳过帧条目配置");
                return;
            }
            foreach (var kv in _cfg.LdfHelper.Frames)
            {
                byte pid = kv.Key;
                var def = kv.Value;
                bool publisher = def.Publisher == _cfg.LdfHelper.MasterName;
                // 从节点模式：所有从节点发布帧配置为自动应答；主节点模式：主节点发布帧由本机发布
                bool slaveResp = _cfg.Mode == LinNodeMode.Slave && !publisher;
                if (publisher || slaveResp)
                {
                    bool ok = SetFrameEntry(pid, def.Dlc, slaveResp, def.Publisher.Length == 0 ? null : def.Publisher);
                    LinDebugLog.Write("[SLAVE] SetFrameEntry pid=" + pid + " dlc=" + def.Dlc + " publisher=" + def.Publisher + " slaveResp=" + slaveResp + " → " + (ok ? "OK" : "FAIL"));
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
            LinPlError err = LinPlApi.SetFrameEntry(_client, _hw, ref entry);
            LinDebugLog.Write("[SLAVE] SetFrameEntry pid=" + pid + " len=" + len + " respEnable=" + responseEnable + " publisher=" + (publisher == null ? "" : publisher) + " → err=" + err);
            return err == LinPlError.errOK;
        }

        // ==================== 发送 ====================

        /// <summary>发送一帧（Master 模式发 Header+数据；从节点模式发布响应数据）</summary>
        /// <summary>LIN PID 补奇偶校验位（FrameId 字段须带奇偶；裸 ID 会导致 Write 失败）</summary>
        private static byte ToPid(byte pid)
        {
            byte p = pid;
            LinPlApi.GetPID(ref p);
            return p;
        }

        public bool Transmit(byte pid, byte[] data, LinChecksumKind ck)
        {
            if (!IsConnected) { LinDebugLog.Write("[TX] Transmit pid=" + pid + " 未连接，拒绝"); return false; }
            byte len = (byte)(data == null ? 0 : data.Length);
            if (len > 8) return false;
            var msg = new LinPlMsg
            {
                FrameId = ToPid(pid),
                Length = len,
                Direction = LinPlDirection.dirPublisher,
                ChecksumType = ck == LinChecksumKind.Classic ? LinPlChecksumType.cstClassic : LinPlChecksumType.cstEnhanced,
                Data = new byte[8],
            };
            if (data != null) Array.Copy(data, msg.Data, len);
            msg.Checksum = LinChecksum.Calculate(data, pid, ck == LinChecksumKind.Enhanced);
            LinPlError err = LinPlApi.Write(_client, _hw, ref msg);
            LinDebugLog.Write("[TX] Transmit pid=" + pid + " pidParity=" + msg.FrameId + " len=" + len + " data=" + LinDebugLog.Hex(data) + " ckType=" + msg.ChecksumType + " checksum=0x" + msg.Checksum.ToString("X2") + " → err=" + err);
            return err == LinPlError.errOK;
        }

        /// <summary>发 Header（dirSubscriber：硬件发 Header 后等待从节点应答；Length 为期望响应长度）</summary>
        public bool SendHeader(byte pid, byte dlc)
        {
            if (!IsConnected) { LinDebugLog.Write("[TX] SendHeader pid=" + pid + " 未连接，拒绝"); return false; }
            byte len = dlc == 0 ? (byte)8 : dlc;
            var msg = new LinPlMsg
            {
                FrameId = ToPid(pid),
                Length = len,
                Direction = LinPlDirection.dirSubscriber,
                ChecksumType = LinPlChecksumType.cstEnhanced,
                Data = new byte[8],
                Checksum = 0,
            };
            LinPlError err = LinPlApi.Write(_client, _hw, ref msg);
            LinDebugLog.Write("[TX] SendHeader pid=" + pid + " pidParity=" + msg.FrameId + " expectLen=" + len + " → err=" + err);
            return err == LinPlError.errOK;
        }

        /// <summary>软件调度发一帧：有缓存数据发完整帧（dirPublisher），无数据发 Header-only</summary>
        public bool SendScheduleFrame(byte pid, byte dlc)
        {
            byte[] data;
            lock (_frameData)
            {
                if (!_frameData.TryGetValue(pid, out data) || data == null || data.Length == 0)
                {
                    LinDebugLog.Write("[TX] SendScheduleFrame pid=" + pid + " 无缓存数据 → Header-only");
                    return SendHeader(pid, dlc);
                }
            }
            LinDebugLog.Write("[TX] SendScheduleFrame pid=" + pid + " 有缓存数据 " + data.Length + " 字节 → 完整帧");
            return Transmit(pid, data, LinChecksumKind.Enhanced);
        }

        /// <summary>软件调度帧数据（回显用；无缓存返回 null）</summary>
        public byte[] GetFrameData(byte pid)
        {
            lock (_frameData)
            {
                byte[] d;
                return _frameData.TryGetValue(pid, out d) ? (byte[])d.Clone() : null;
            }
        }

        /// <summary>更新从节点发布帧数据（硬件自动应答内容）；帧条目缺失或被禁用时先重建 RESPONSE_ENABLE 条目</summary>
        public bool UpdateSlaveData(byte pid, byte[] data)
        {
            if (!IsConnected || data == null || data.Length > 8) { LinDebugLog.Write("[SLAVE] UpdateSlaveData pid=" + pid + " 未连接或数据非法 len=" + (data == null ? -1 : data.Length)); return false; }
            lock (_frameData) { _frameData[pid] = (byte[])data.Clone(); }
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
            if (err != LinPlError.errOK) { LinDebugLog.Write("[SLAVE] UpdateSlaveData pid=" + pid + " SetFrameEntry → err=" + err); return false; }
            LinPlError err2 = LinPlApi.UpdateByteArray(_client, _hw, pid, 0, (byte)data.Length, data);
            LinDebugLog.Write("[SLAVE] UpdateSlaveData pid=" + pid + " len=" + data.Length + " data=" + LinDebugLog.Hex(data) + " SetFrameEntry=OK UpdateByteArray → err=" + err2);
            return err2 == LinPlError.errOK;
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
        public string StartSchedule(List<LinScheduleSlot> slots)
        {
            if (!IsConnected) return "未连接";
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
            if (err != LinPlError.errOK) return "设置调度表失败: " + LinPlErrorCodes.ToChinese(err);
            err = LinPlApi.StartSchedule(_client, _hw, 0);
            if (err != LinPlError.errOK) return "启动调度失败: " + LinPlErrorCodes.ToChinese(err);
            return "";
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
                    for (int i = 0; i < count; i++)
                    {
                        LinDebugLog.Write("[RX] msg Type=" + buf[i].Type + " ID=" + buf[i].FrameId + " len=" + buf[i].Length +
                            " dir=" + buf[i].Direction + " ckType=" + buf[i].ChecksumType +
                            " data=" + LinDebugLog.Hex(buf[i].Data, buf[i].Length) +
                            " checksum=0x" + buf[i].Checksum.ToString("X2") +
                            " errFlags=0x" + ((int)buf[i].ErrorFlags).ToString("X8") +
                            " ts=" + buf[i].TimeStamp + " hw=" + buf[i].hHw);
                        HandleRcvMsg(buf[i]);
                    }
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
                    if (_consecutiveErrors == 1 || _consecutiveErrors == 10)
                        LinDebugLog.Write("[RX] ReadMulti → err=" + err + " consecutive=" + _consecutiveErrors);
                    if (_consecutiveErrors >= 10)
                    {
                        _consecutiveErrors = 0;
                        Lin_API.OnLinkLost(_logicChannel, LinPlErrorCodes.ToChinese(err), this);
                    }
                    Thread.Sleep(20);
                }
            }
            LinDebugLog.Write("[RX] 接收线程退出");
        }

        private void HandleRcvMsg(LinPlRcvMsg m)
        {
            // 总线状态帧：不产生报文行，仅更新状态
            if (m.Type != LinPlMsgType.mstStandard)
            {
                if (m.Type == LinPlMsgType.mstBusSleep || m.Type == LinPlMsgType.mstBusWakeUp || m.Type == LinPlMsgType.mstOverrun)
                {
                    LinDebugLog.Write("[RX] 总线事件 Type=" + m.Type + " errFlags=0x" + ((int)m.ErrorFlags).ToString("X8"));
                    Lin_API.OnBusEvent(_logicChannel, m.Type == LinPlMsgType.mstBusSleep ? "Sleep" : m.Type == LinPlMsgType.mstBusWakeUp ? "WakeUp" : "Overrun");
                }
                else
                {
                    LinDebugLog.Write("[RX] 非标准消息 Type=" + m.Type + " errFlags=0x" + ((int)m.ErrorFlags).ToString("X8"));
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

            if (frame.ErrorKind != LinErrorKind.None)
                LinDebugLog.Write("[ERR] id=" + m.FrameId + " errFlags=0x" + ((int)m.ErrorFlags).ToString("X8") + " → ErrorKind=" + frame.ErrorKind + " checksumOk=" + frame.ChecksumOk);

            Lin_API.LinReceive(_logicChannel, frame);
        }
    }
}
