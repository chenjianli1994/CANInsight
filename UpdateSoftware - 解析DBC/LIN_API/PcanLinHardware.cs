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
            lock (PeakHardwareAccess.SyncRoot)
            {
                return EnumerateChannelsCore();
            }
        }

        private static Tuple<List<string>, string> EnumerateChannelsCore()
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

        /// <summary>连接：注册客户端 → 定位硬件句柄 → 修复异常硬件配置 → 初始化模式/波特率 →
        /// 配置帧条目 → 启动接收线程；管理器返回未知内部错误时完整重建客户端重试一次。</summary>
        public string Connect()
        {
            lock (PeakHardwareAccess.SyncRoot)
            {
                LinDebugLog.Open("ch" + _logicChannel + " " + _cfg.HwHandle);
                string err = TryConnect();
                if (err.Length == 0) return "";
                if (!IsTransientManagerError(err))
                {
                    LinDebugLog.Write("[CONN] 连接失败: " + err);
                    return err;
                }

                // PLIN Manager 在设备刚完成枚举或刚释放上一客户端时可能短暂返回 errUnknown；
                // TryConnect 已清理客户端，延迟后从 RegisterClient 起重建，避免用户必须拔插设备。
                LinDebugLog.Write("[CONN] 首次连接失败: " + err + " → 延迟 500ms 完整重建客户端重试");
                Thread.Sleep(500);
                string retryErr = TryConnect();
                if (retryErr.Length > 0) LinDebugLog.Write("[CONN] 重试仍失败: " + retryErr);
                return retryErr;
            }
        }

        private static bool IsTransientManagerError(string error)
        {
            return error.IndexOf("设置接收过滤失败", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("未知内部错误", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("通信中断", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("恢复 LIN 硬件默认配置失败", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string TryConnect()
        {
            LinDebugLog.Write("[CONN] Connect 开始: HwHandle=" + _cfg.HwHandle + " TransmitEntries=" + (_cfg.TransmitEntries == null ? 0 : _cfg.TransmitEntries.Count) + " Baud=" + _cfg.Baudrate);
            string planError = _cfg.ValidateTransmitPlan();
            if (planError.Length > 0)
            {
                LinDebugLog.Write("[CONN] 发送计划拒绝: " + planError);
                return planError;
            }
            try
            {
                LinPlError err = LinPlApi.RegisterClient("CANInsight_LIN", IntPtr.Zero, out _client);
                LinDebugLog.Write("[CONN] RegisterClient → err=" + err + " client=" + _client);
                if (err != LinPlError.errOK) return "注册 PLIN 客户端失败: " + LinPlErrorCodes.ToChinese(err);
                if (_client == LinPlApi.INVALID_LIN_HANDLE) return "注册 PLIN 客户端失败（句柄无效）";

                // ResetClient 只清空客户端接收队列和计数，保证新客户端从空队列开始接收。
                LinPlError rerr = LinPlApi.ResetClient(_client);
                LinDebugLog.Write("[CONN] ResetClient → err=" + rerr);

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

                // 设备刚枚举但尚未完成配置时，PLIN 可能返回 mode=0、baudrate=0；
                // 仅 InitializeHardware 无法清掉这份异常配置，随后 SetClientFilter 会返回 errUnknown。
                // 先读回硬件配置，只有检测到非法波特率时才恢复默认配置，避免影响正常会话/其他客户端。
                int currentBaudrate = 0;
                int currentMode = 0;
                LinPlError baudErr = LinPlApi.GetHardwareParam(_hw, LinPlHardwareParam.hwpBaudrate, out currentBaudrate, 4);
                LinPlError modeErr = LinPlApi.GetHardwareParam(_hw, LinPlHardwareParam.hwpMode, out currentMode, 4);
                LinDebugLog.Write("[CONN] 当前硬件参数 mode=" + currentMode + "(err=" + modeErr + ") baud=" + currentBaudrate + "(err=" + baudErr + ")");
                if (baudErr == LinPlError.errOK &&
                    (currentBaudrate < LinPlApi.LIN_MIN_BAUDRATE || currentBaudrate > LinPlApi.LIN_MAX_BAUDRATE))
                {
                    LinPlError resetConfigErr = LinPlApi.ResetHardwareConfig(_client, _hw);
                    LinDebugLog.Write("[CONN] 检测到非法硬件波特率，ResetHardwareConfig → err=" + resetConfigErr);
                    if (resetConfigErr != LinPlError.errOK)
                    {
                        CleanupClient();
                        return "恢复 LIN 硬件默认配置失败: " + LinPlErrorCodes.ToChinese(resetConfigErr);
                    }
                }

                // 界面不再单独保存通道主从，但 PLIN 驱动仍要求连接时选择硬件模式。
                // 纯 Slave 发送计划必须以 modSlave 打开，否则 RESPONSE_ENABLE 不会参与总线应答。
                LinNodeMode hardwareMode = _cfg.GetHardwareMode();
                LinPlHardwareMode mode = hardwareMode == LinNodeMode.Slave
                    ? LinPlHardwareMode.modSlave
                    : LinPlHardwareMode.modMaster;
                string modeNotice = _cfg.GetHardwareModeNotice();
                if (modeNotice.Length > 0) LinDebugLog.Write("[CONN] 警告: " + modeNotice);
                err = LinPlApi.InitializeHardware(_client, _hw, mode, (ushort)_cfg.Baudrate);
                LinDebugLog.Write("[CONN] InitializeHardware mode=" + mode + " derived=" + hardwareMode + " baud=" + _cfg.Baudrate + " → err=" + err);
                if (err != LinPlError.errOK) { CleanupClient(); return "初始化 LIN 硬件失败（模式/波特率）: " + LinPlErrorCodes.ToChinese(err); }

                // 官方序列：连接后设置客户端过滤器（全 ID 接收，0-63 每位一帧；官方签名为 3 参无 filterType）
                err = LinPlApi.SetClientFilter(_client, _hw, 0xFFFFFFFFFFFFFFFF);
                LinDebugLog.Write("[CONN] SetClientFilter mask=FFFFFFFFFFFFFFFF → err=" + err);
                if (err != LinPlError.errOK && err != LinPlError.errWrongParameterType)
                {
                    if (err == LinPlError.errUnknown || err == LinPlError.errManagerNotResponding)
                    {
                        LinDebugLog.Write("[CONN] SetClientFilter 返回管理器错误，尝试 ResetHardwareConfig 后重新初始化");
                        LinPlError resetConfigErr = LinPlApi.ResetHardwareConfig(_client, _hw);
                        LinDebugLog.Write("[CONN] SetClientFilter 恢复 ResetHardwareConfig → err=" + resetConfigErr);
                        if (resetConfigErr != LinPlError.errOK)
                        {
                            CleanupClient();
                            return "恢复 LIN 硬件默认配置失败: " + LinPlErrorCodes.ToChinese(resetConfigErr);
                        }
                        LinPlError reinitErr = LinPlApi.InitializeHardware(_client, _hw, mode, (ushort)_cfg.Baudrate);
                        LinDebugLog.Write("[CONN] SetClientFilter 恢复 InitializeHardware → err=" + reinitErr);
                        if (reinitErr != LinPlError.errOK)
                        {
                            CleanupClient();
                            return "恢复 LIN 硬件配置后初始化失败: " + LinPlErrorCodes.ToChinese(reinitErr);
                        }
                        err = LinPlApi.SetClientFilter(_client, _hw, 0xFFFFFFFFFFFFFFFF);
                        LinDebugLog.Write("[CONN] SetClientFilter(恢复重试) → err=" + err);
                    }
                }
                if (err != LinPlError.errOK && err != LinPlError.errWrongParameterType)
                {
                    CleanupClient();
                    return "设置接收过滤失败: " + LinPlErrorCodes.ToChinese(err) + "\n（PLIN 管理器状态异常时可重新插拔 PCAN 设备或重启「PLIN Device Manager」服务后重试）";
                }

                // 使能总线状态帧（Sleep/WakeUp）上报：诊断"硬件是否看到总线"的关键信号
                LinPlError prm = LinPlApi.SetClientParam(_client, LinPlClientParam.clpReceiveStatusFrames, 1);
                LinDebugLog.Write("[CONN] SetClientParam clpReceiveStatusFrames=1 → err=" + prm);

                // 接收全部帧 ID（0-63）
                err = LinPlApi.RegisterFrameId(_client, _hw, 0, LinPlApi.LIN_MAX_FRAME_ID);
                LinDebugLog.Write("[CONN] RegisterFrameId 0..63 → err=" + err);
                if (err != LinPlError.errOK && err != LinPlError.errIllegalFrameID) { CleanupClient(); return "注册帧 ID 失败: " + LinPlErrorCodes.ToChinese(err); }

                string frameConfigError = ConfigureFrameEntries();
                if (frameConfigError.Length > 0)
                {
                    CleanupClient();
                    return frameConfigError;
                }

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
                    LinPlError rerr = LinPlApi.ResetClient(_client);
                    LinDebugLog.Write("[CONN] Cleanup ResetClient → err=" + rerr);
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
            lock (PeakHardwareAccess.SyncRoot)
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
                        // 复位客户端设置（过滤/帧/调度残留），保证下次连接 SetClientFilter 干净
                        LinPlError rerr = LinPlApi.ResetClient(_client);
                        LinDebugLog.Write("[CONN] Disconnect ResetClient → err=" + rerr);
                        LinPlError err2 = LinPlApi.RemoveClient(_client);
                        LinDebugLog.Write("[CONN] Disconnect RemoveClient → err=" + err2);
                    }
                }
                catch (Exception ex) { LinDebugLog.Write("[CONN] Disconnect 异常: " + ex.Message); }
                _client = LinPlApi.INVALID_LIN_HANDLE;
                _hw = LinPlApi.INVALID_LIN_HANDLE;
            }
        }

        /// <summary>
        /// 按发送页签配置硬件帧条目。未加入发送页的报文一律 Subscriber，
        /// 这样监控不会因为 LDF Publisher 字段而抢答；Slave 项才启用 RESPONSE_ENABLE。
        /// </summary>
        private string ConfigureFrameEntries()
        {
            var frameIds = new HashSet<byte>();
            if (_cfg.LdfHelper != null)
                foreach (var kv in _cfg.LdfHelper.Frames) frameIds.Add(kv.Key);
            foreach (var configuredEntry in _cfg.TransmitEntries ?? new List<LinTransmitEntry>())
                if (configuredEntry != null) frameIds.Add(configuredEntry.Pid);
            if (frameIds.Count == 0)
            {
                LinDebugLog.Write("[LIN] ConfigureFrameEntries: 无 LDF/发送项，保持默认接收配置");
                return "";
            }
            foreach (byte pid in frameIds)
            {
                LinFrameDef def = null;
                if (_cfg.LdfHelper != null) _cfg.LdfHelper.Frames.TryGetValue(pid, out def);
                var configured = _cfg.FindTransmitEntry(pid);
                LinTransmitType transmitType = configured == null ? LinTransmitType.HeaderOnly : configured.Type;
                if (pid > 0x3F) return "发送项 PID 超出 LIN 范围: 0x" + pid.ToString("X2");
                if (transmitType == LinTransmitType.BreakOnly)
                    return "发送项 PID 0x" + pid.ToString("X2") + " 使用 BreakOnly，但当前 PLIN 适配器不支持独立 Break 原语";
                // PLIN 的 Master 通道同样支持通过 RESPONSE_ENABLE 配置本机 Slave 响应。
                // 因此响应归属必须按发送项判断，不能再由通道初始化模式屏蔽混合计划。
                bool masterPublisher = configured != null && configured.Enabled && transmitType == LinTransmitType.Master;
                // 从机响应完全由发送页 Slave 项驱动：只有显式配置的 Slave 项才打开
                // RESPONSE_ENABLE（硬件收到 Header 后自动应答本机响应帧）。
                bool slaveResp = transmitType == LinTransmitType.Slave &&
                    configured != null && configured.Enabled;
                // PLIN 的帧表同时决定发送角色和接收过滤：本机发布帧为 Publisher，
                // 其余帧必须显式设为 Subscriber，主节点才能看到从节点响应，
                // 从节点也才能接收主节点 Header。未选本机节点的从节点帧不会启用响应。
                byte dlc = configured != null && configured.Dlc > 0
                    ? configured.Dlc
                    : (def == null || def.Dlc == 0 ? (byte)8 : def.Dlc);
                if (dlc > 8) return "发送项 PID 0x" + pid.ToString("X2") + " 的 DLC 超出 8";
                LinPlDirection direction = (masterPublisher || slaveResp)
                    ? LinPlDirection.dirPublisher
                    : LinPlDirection.dirSubscriber;
                byte[] initial = configured == null || configured.Data == null ? new byte[0] : configured.Data;
                string publisher = def == null ? null : def.Publisher;
                bool ok = SetFrameEntry(pid, dlc, direction, slaveResp, publisher, initial);
                LinDebugLog.Write("[LIN] SetFrameEntry pid=" + pid + " dlc=" + dlc + " publisher=" + (publisher ?? "") + " direction=" + direction + " transmitType=" + transmitType + " → " + (ok ? "OK" : "FAIL"));
                if (!ok) return "配置 LIN 帧条目失败: PID 0x" + pid.ToString("X2") + "（方向=" + direction + ", 类型=" + transmitType + "）";
            }
            return "";
        }

        private bool SetFrameEntry(byte pid, byte len, LinPlDirection direction, bool responseEnable, string publisher, byte[] initialData)
        {
            var entry = new LinPlFrameEntry
            {
                FrameId = pid,
                Length = len,
                Direction = direction,
                ChecksumType = ToPlChecksum(LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid)),
                Flags = (ushort)(responseEnable ? LinPlApi.FRAME_FLAG_RESPONSE_ENABLE : 0),
                InitialData = new byte[8],
            };
            if (initialData != null) Array.Copy(initialData, entry.InitialData, Math.Min(initialData.Length, entry.InitialData.Length));
            LinPlError err = LinPlApi.SetFrameEntry(_client, _hw, ref entry);
            LinDebugLog.Write("[LIN] SetFrameEntry pid=" + pid + " len=" + len + " respEnable=" + responseEnable + " publisher=" + (publisher == null ? "" : publisher) + " checksum=" + entry.ChecksumType + " → err=" + err);
            if (err != LinPlError.errOK) return false;
            if (direction != LinPlDirection.dirPublisher) return true;

            // PLIN 的 InitialData 在不同固件版本上并不总是立即刷新发布缓存；
            // 用官方 UpdateByteArray 再写一次，确保从节点收到 Header 后有可发送的数据。
            byte updateLength = len == 0 ? (byte)8 : len;
            byte[] payload = new byte[updateLength];
            if (initialData != null) Array.Copy(initialData, payload, Math.Min(initialData.Length, payload.Length));
            LinPlError updateErr = LinPlApi.UpdateByteArray(_client, _hw, pid, 0, updateLength, payload);
            LinDebugLog.Write("[LIN] UpdateByteArray pid=" + pid + " len=" + updateLength + " → err=" + updateErr);
            if (updateErr != LinPlError.errOK) return false;
            lock (_frameData) _frameData[pid] = payload;
            return true;
        }

        private static LinPlChecksumType ToPlChecksum(LinChecksumKind checksum)
        {
            return checksum == LinChecksumKind.Classic ? LinPlChecksumType.cstClassic : LinPlChecksumType.cstEnhanced;
        }

        private LinTransmitEntry GetTransmitEntry(byte pid) => _cfg.FindTransmitEntry(pid);

        private bool IsConfiguredPublisher(byte pid)
        {
            var entry = GetTransmitEntry(pid);
            return entry != null && entry.Enabled &&
                (entry.Type == LinTransmitType.Master || entry.Type == LinTransmitType.Slave);
        }

        // ==================== 发送 ====================

        /// <summary>发送一帧完整的 Master 报文（Header + Data + 校验和）。</summary>
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
                ChecksumType = ToPlChecksum(LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid)),
                Data = new byte[8],
                Checksum = 0,
            };
            LinPlError err = LinPlApi.Write(_client, _hw, ref msg);
            LinDebugLog.Write("[TX] SendHeader pid=" + pid + " pidParity=" + msg.FrameId + " expectLen=" + len + " → err=" + err);
            return err == LinPlError.errOK;
        }

        /// <summary>当前 PLIN API 没有只发 Break 的合法原语。</summary>
        public bool SendBreakOnly(byte pid)
        {
            LinDebugLog.Write("[TX] BreakOnly pid=" + pid + " → 当前 PLIN API 不支持独立 Break 原语");
            return false;
        }

        /// <summary>按发送项类型执行一次调度原语。</summary>
        public bool SendScheduleFrame(byte pid, byte dlc, LinTransmitType transmitType)
        {
            if (transmitType == LinTransmitType.Slave)
            {
                // Slave 项的响应由 PLIN 在外部 Header 到达时自动完成，调度器不应主动发 Header。
                LinDebugLog.Write("[TX] SendScheduleFrame pid=" + pid + " type=Slave → 等待外部 Header");
                return true;
            }
            if (transmitType == LinTransmitType.HeaderOnly) return SendHeader(pid, dlc);
            if (transmitType == LinTransmitType.BreakOnly) return SendBreakOnly(pid);

            byte[] data;
            lock (_frameData) _frameData.TryGetValue(pid, out data);
            byte len = dlc == 0 ? (byte)8 : dlc;
            if (data == null || data.Length != len) data = new byte[len];
            LinDebugLog.Write("[TX] SendScheduleFrame pid=" + pid + " type=Master len=" + len + " → 完整帧");
            return Transmit(pid, data, LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid));
        }

        /// <summary>旧调用兼容：发送项存在时使用其类型，否则按 Master 处理。</summary>
        public bool SendScheduleFrame(byte pid, byte dlc)
        {
            var entry = GetTransmitEntry(pid);
            return SendScheduleFrame(pid, dlc, entry == null ? LinTransmitType.Master : entry.Type);
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

        /// <summary>按发送类型配置数据槽；Slave 才启用 RESPONSE_ENABLE。</summary>
        public bool ConfigureTransmitEntry(byte pid, LinTransmitType transmitType, byte[] data, byte dlc)
        {
            if (!IsConnected || dlc > 8 || (data != null && data.Length > 8)) return false;
            if (transmitType == LinTransmitType.BreakOnly) return SendBreakOnly(pid);
            byte len = dlc == 0 ? (byte)(data == null ? 8 : data.Length) : dlc;
            if (len == 0) len = 8;
            bool publisher = transmitType == LinTransmitType.Master || transmitType == LinTransmitType.Slave;
            bool responseEnable = transmitType == LinTransmitType.Slave;
            var entry = new LinPlFrameEntry
            {
                FrameId = pid,
                Length = len,
                Direction = publisher ? LinPlDirection.dirPublisher : LinPlDirection.dirSubscriber,
                ChecksumType = ToPlChecksum(LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid)),
                Flags = (ushort)(responseEnable ? LinPlApi.FRAME_FLAG_RESPONSE_ENABLE : 0),
                InitialData = new byte[8],
            };
            if (data != null) Array.Copy(data, entry.InitialData, Math.Min(data.Length, len));
            LinPlError err = LinPlApi.SetFrameEntry(_client, _hw, ref entry);
            if (err != LinPlError.errOK)
            {
                LinDebugLog.Write("[TX] ConfigureTransmitEntry pid=" + pid + " type=" + transmitType + " SetFrameEntry → " + err);
                return false;
            }
            if (!publisher)
            {
                lock (_frameData) _frameData.Remove(pid);
                return true;
            }
            byte[] payload = data == null ? new byte[len] : (byte[])data.Clone();
            if (payload.Length != len) Array.Resize(ref payload, len);
            LinPlError updateErr = LinPlApi.UpdateByteArray(_client, _hw, pid, 0, len, payload);
            LinDebugLog.Write("[TX] ConfigureTransmitEntry pid=" + pid + " type=" + transmitType + " len=" + len + " response=" + responseEnable + " → " + updateErr);
            if (updateErr != LinPlError.errOK) return false;
            lock (_frameData) _frameData[pid] = payload;
            return true;
        }

        /// <summary>旧调用兼容：已配置项按其类型更新，否则按 LDF 主节点发布者推断。</summary>
        public bool UpdateSlaveData(byte pid, byte[] data)
        {
            var configured = GetTransmitEntry(pid);
            LinTransmitType type = configured == null
                ? (LinLdfHelper.IsMasterPublisherFrame(_cfg.LdfHelper, pid) ? LinTransmitType.Master : LinTransmitType.Slave)
                : configured.Type;
            return ConfigureTransmitEntry(pid, type, data, (byte)(data == null ? 0 : data.Length));
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
                ChecksumType = ToPlChecksum(LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, pid)),
                Flags = 0,
                InitialData = new byte[8],
            };
            bool ok = LinPlApi.SetFrameEntry(_client, _hw, ref entry) == LinPlError.errOK;
            if (ok) lock (_frameData) _frameData.Remove(pid);
            return ok;
        }

        // ==================== 调度表（硬件自主运行） ====================

        /// <summary>
        /// 下发调度槽到硬件并启动（slot 0）。
        /// 主从一体（modMaster + 硬件调度表）：Slave 槽同样纳入调度表，硬件发 Header 后由
        /// 帧表 RESPONSE_ENABLE 自动应答本机响应帧（官方手册：modMaster 下帧表自动应答在
        /// 硬件调度表激活时生效）。HeaderOnly/Master 槽均为无条件帧（sltUnconditional 总是发
        /// Header；sltMasterRequest 只用于诊断请求 0x3C 且“新数据才发”，普通帧会被饿死）。
        /// </summary>
        public string StartSchedule(List<LinScheduleSlot> slots)
        {
            if (!IsConnected) return "未连接";
            var active = new List<LinScheduleSlot>();
            foreach (var slot in slots)
                if (slot.Enabled && slot.TransmitType != LinTransmitType.BreakOnly)
                    active.Add(slot);
            if (active.Count == 0)
                return "没有启用的调度槽";
            var arr = new LinPlScheduleSlot[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                // 无条件槽：每个周期固定发 Header；Slave 槽的响应由帧表自动应答。
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
            return Transmit(0x3C, new byte[] { 0x00 }, LinLdfHelper.GetFrameChecksumType(_cfg.LdfHelper, 0x3C));
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
            long lastDiagMs = Environment.TickCount;
            while (_running)
            {
                // 每 2s 输出管理器侧计数诊断：clpMessagesOnQueue=本客户端接收队列未读消息数（ReadMulti 应能读到）；
                // clpReceivedMessages=管理器累计收到的消息数；clpTransmittedMessages=管理器累计发送数。
                // 用于定位"总线上有报文但软件收不到"：管理器计数为 0 → 硬件/管理器接收链路问题；
                // 计数增长但 ReadMulti 读空 → 本软件读取路径问题。
                long nowMs = Environment.TickCount;
                if (nowMs - lastDiagMs >= 2000)
                {
                    lastDiagMs = nowMs;
                    int onQueue = 0, rxTotal = 0, txTotal = 0;
                    LinPlApi.GetClientParam(_client, LinPlClientParam.clpMessagesOnQueue, out onQueue, 4);
                    LinPlApi.GetClientParam(_client, LinPlClientParam.clpReceivedMessages, out rxTotal, 4);
                    LinPlApi.GetClientParam(_client, LinPlClientParam.clpTransmittedMessages, out txTotal, 4);
                    // 硬件侧总线状态：hwsAutobaudrate=未锁定总线（可能接线/电平问题或该通道无流量）、hwsActive=已同步可收发
                    string busState = "未知";
                    try
                    {
                        LinPlHardwareStatus st;
                        if (LinPlApi.GetStatus(_hw, out st) == LinPlError.errOK)
                            busState = st.Status.ToString() + "(" + (int)st.Status + ")";
                    }
                    catch { }
                    LinDebugLog.Write("[RX] 诊断(2s) queue=" + onQueue + " rxTotal=" + rxTotal + " txTotal=" + txTotal + " hwBus=" + busState);
                }
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

            // PLIN 返回的是 LDF 角色方向（Publisher/Subscriber），而不是本机 Tx/Rx。
            // 只有发送页中明确配置为本机 Publisher 的 Master/Slave 项才标为 Tx；
            // 未配置报文和 HeaderOnly 报文一律把总线数据视为 Rx。
            bool otherResponse = (m.ErrorFlags & LinPlMsgErrors.OtherResponse) != 0;
            var configured = GetTransmitEntry(m.FrameId);
            bool localPublisher = configured != null && configured.Enabled &&
                (configured.Type == LinTransmitType.Master || configured.Type == LinTransmitType.Slave);
            LinFrameDir frameDirection = localPublisher && !otherResponse && m.Direction == LinPlDirection.dirPublisher
                ? LinFrameDir.Tx
                : LinFrameDir.Rx;
            var frame = new LinFrameRecord
            {
                LogicChannel = _logicChannel,
                Pid = m.FrameId,
                Direction = frameDirection,
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
