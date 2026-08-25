using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 统一汇聚层（照 CAN_API.CAN_API 的静态模式）：
    /// 连接/断开/发送/调度按逻辑通道路由到 PcanLinHardware 或 XlLinHardware
    /// </summary>
    internal static class Lin_API
    {
        // ==================== 会话时钟（微秒，首次连接归零） ====================
        private static readonly Stopwatch _sw = Stopwatch.StartNew();
        private static ulong _epochUs;

        // ==================== 从节点响应活动（调度超时兜底） ====================
        // 只记录真实硬件/Vector Rx，不记录软件 Tx 回显。按通道+PID保存最后一次总线活动，
        // 供主节点调度判断 Header 后是否出现从节点响应或硬件错误事件。
        private static readonly Dictionary<long, long> _lastRxMs = new Dictionary<long, long>();
        // 该 PID 任意方向帧活动（含本机 Tx 回显与硬件错误帧）：从机监控模式用它判断
        // 外部 Master 是否发出了对应帧头——modSlave 下本机自动应答帧按 dirPublisher 上报，
        // 方向可能是 Tx，不能只依赖 _lastRxMs。
        private static readonly Dictionary<long, long> _lastFrameMs = new Dictionary<long, long>();
        private static readonly object _rxActivityLock = new object();

        // ==================== 硬件实例缓存：逻辑通道号 → 适配器（加锁保护，重连线程与 UI 线程并发访问） ====================
        private static readonly Dictionary<byte, PcanLinHardware> _pcan = new Dictionary<byte, PcanLinHardware>();
        private static readonly Dictionary<byte, XlLinHardware> _xl = new Dictionary<byte, XlLinHardware>();
        private static readonly object _hwLock = new object();

        // ==================== 事件 ====================
        /// <summary>新帧（UI 订阅刷新报文表）</summary>
        public static event Action<LinFrameRecord> LinFrameReceived;
        /// <summary>连接状态变化（通道, 已连接, 错误原因）</summary>
        public static event Action<byte, bool, string> ChannelStateChanged;
        /// <summary>链路丢失（通道, 原因）——UI 提示用，自动重连在 OnLinkLost 内进行</summary>
        public static event Action<byte, string> LinkLost;
        /// <summary>总线事件（通道, "Sleep"/"WakeUp"/"Overrun"）</summary>
        public static event Action<byte, string> BusEvent;

        /// <summary>当前是否已连接</summary>
        public static bool IsConnected(byte logicChannel)
        {
            lock (_hwLock)
            {
                return _pcan.ContainsKey(logicChannel) || _xl.ContainsKey(logicChannel);
            }
        }

        /// <summary>是否存在已连接的 PEAK LIN 通道；PCAN-CAN 探测期间不得再触碰同一设备。</summary>
        internal static bool HasPcanConnection
        {
            get
            {
                lock (_hwLock) return _pcan.Count > 0;
            }
        }

        // ==================== 连接/断开 ====================

        /// <summary>按通道配置连接硬件；成功返回 ""，失败返回中文原因</summary>
        public static string LinConnect(byte logicChannel)
        {
            LinDebugLog.Open("ch" + logicChannel);
            LinDebugLog.Write("[CONN] LinConnect 入口 ch=" + logicChannel);
            if (logicChannel < 1 || logicChannel > LinConfig.Channels.Count) return "逻辑通道号越界";
            var cfg = LinConfig.Channels[logicChannel - 1];

            // 清理旧实例（重连场景）
            LinDisconnect(logicChannel);

            string err;
            if (cfg.HwType == LinConfig.HwTypePcan)
            {
                var hw = new PcanLinHardware(logicChannel, cfg);
                err = hw.Connect();
                lock (_hwLock) { if (err.Length == 0) _pcan[logicChannel] = hw; }
                LinDebugLog.Write("[CONN] LinConnect PEAK ch=" + logicChannel + " → err='" + err + "'");
            }
            else if (cfg.HwType == LinConfig.HwTypeCanoe)
            {
                var hw = new XlLinHardware(logicChannel, cfg);
                err = hw.Connect();
                lock (_hwLock) { if (err.Length == 0) _xl[logicChannel] = hw; }
                LinDebugLog.Write("[CONN] LinConnect Vector ch=" + logicChannel + " → err='" + err + "'");
            }
            else
            {
                err = "通道未绑定硬件类型";
            }

            cfg.IsConnected = err.Length == 0;
            cfg.ConnectError = err;
            if (err.Length == 0 && _epochUs == 0) _epochUs = (ulong)_sw.ElapsedMilliseconds * 1000;
            ChannelStateChanged?.Invoke(logicChannel, err.Length == 0, err);
            return err;
        }

        public static void LinDisconnect(byte logicChannel)
        {
            LinDebugLog.Write("[CONN] LinDisconnect 入口 ch=" + logicChannel);
            PcanLinHardware pcan = null;
            XlLinHardware xl = null;
            // 从注册表移除与释放底层句柄必须是一个原子区间；否则枚举线程可能在
            // _pcan 已移除、PLIN 客户端尚未 Disconnect 的窗口内启动 PCAN 探测。
            lock (PeakHardwareAccess.SyncRoot)
            {
                lock (_hwLock)
                {
                    if (_pcan.TryGetValue(logicChannel, out pcan))
                    {
                        _pcan.Remove(logicChannel);
                    }
                    else if (_xl.TryGetValue(logicChannel, out xl))
                    {
                        _xl.Remove(logicChannel);
                    }
                }
                if (pcan != null) pcan.Disconnect();
                else if (xl != null) xl.Disconnect();
            }
            if (logicChannel <= LinConfig.Channels.Count)
            {
                LinConfig.Channels[logicChannel - 1].IsConnected = false;
                // 断开连接 = 本次“主从一体驱动”会话结束；下次连接回到按发送计划推导。
                LinConfig.Channels[logicChannel - 1].ForceMasterDriven = false;
                ChannelStateChanged?.Invoke(logicChannel, false, "");
            }
        }

        // ==================== 发送 ====================

        /// <summary>手动发送一帧；失败返回 false（原因经状态事件/返回值）</summary>
        public static bool LinTransmit(byte logicChannel, byte pid, byte[] data, LinChecksumKind ck)
        {
            ck = GetFrameChecksumKind(logicChannel, pid, ck);
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan))
                {
                    bool ok = pcan.Transmit(pid, data, ck);
                    if (ok) EchoTx(logicChannel, pid, data, ck);
                    return ok;
                }
                if (_xl.TryGetValue(logicChannel, out xl))
                {
                    bool ok = xl.Transmit(pid, data, ck);
                    if (ok) EchoTx(logicChannel, pid, data, ck);
                    return ok;
                }
            }
            return false;
        }

        /// <summary>调度表发 Header（软件调度；真实响应/无应答由硬件接收事件上报）</summary>
        public static bool LinSendHeader(byte logicChannel, byte pid)
        {
            PcanLinHardware pcan;
            XlLinHardware xl;
            bool ok = false;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) ok = pcan.SendHeader(pid, GetFrameDlc(logicChannel, pid));
                else if (_xl.TryGetValue(logicChannel, out xl)) ok = xl.SendRequest(pid);
            }
            LinDebugLog.Write("[SCH] LinSendHeader ch=" + logicChannel + " pid=0x" + pid.ToString("X2") + " → " + ok);
            return ok;
        }

        /// <summary>调度表兼容入口：使用发送页中该 PID 的发送类型。</summary>
        public static bool LinSendScheduleFrame(byte logicChannel, byte pid)
        {
            var slot = new LinScheduleSlot
            {
                Pid = pid,
                SlotMs = 15,
                TransmitType = GetTransmitType(logicChannel, pid, LinTransmitType.Master),
            };
            return LinSendScheduleSlot(logicChannel, slot);
        }

        /// <summary>按调度槽发送 Master/HeaderOnly/BreakOnly；Slave 槽默认只等待外部 Header，
        /// 主从一体驱动（ForceMasterDriven）下改为本机发 Header 驱动响应帧。</summary>
        public static bool LinSendScheduleSlot(byte logicChannel, LinScheduleSlot slot)
        {
            if (slot == null) return false;
            LinTransmitType type = slot.TransmitType;
            if (type == LinTransmitType.Master && GetTransmitEntry(logicChannel, slot.Pid) != null)
                type = GetTransmitType(logicChannel, slot.Pid, type);
            if (type == LinTransmitType.Slave)
            {
                // 主从一体驱动（单设备自测）：本机主动发 Header，响应由硬件自动应答
                // （硬件调度表路径）或软件仿真注入（软件调度回退路径）完成。
                if (IsMasterDriven(logicChannel))
                {
                    LinDebugLog.Write("[SCH] LinSendScheduleSlot(主从一体) ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " → 发 Header 驱动响应帧");
                    return LinSendHeader(logicChannel, slot.Pid);
                }
                LinDebugLog.Write("[SCH] LinSendScheduleSlot ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " type=Slave → 被动响应，不产生调度报文");
                return true;
            }
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan))
                {
                    bool ok = pcan.SendScheduleFrame(slot.Pid, GetFrameDlc(logicChannel, slot.Pid), type);
                    LinDebugLog.Write("[SCH] LinSendScheduleSlot ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " type=" + type + " → " + ok);
                    return ok;
                }
                if (_xl.TryGetValue(logicChannel, out xl))
                {
                    byte dlc = GetFrameDlc(logicChannel, slot.Pid);
                    bool ok = xl.SendScheduleFrame(slot.Pid, dlc, type);
                    LinDebugLog.Write("[SCH] LinSendScheduleSlot Vector ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " type=" + type + " → " + ok);
                    return ok;
                }
            }
            LinDebugLog.Write("[SCH] LinSendScheduleSlot ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " → false（未连接）");
            return false;
        }

        private static byte GetFrameDlc(byte logicChannel, byte pid)
        {
            var configured = GetTransmitEntry(logicChannel, pid);
            if (configured != null && configured.Dlc > 0) return configured.Dlc;
            var ldf = GetLdf(logicChannel);
            if (ldf != null && ldf.Frames.ContainsKey(pid)) return ldf.Frames[pid].Dlc;
            return 0;
        }

        /// <summary>按通道 LDF 覆盖调用方校验和；未加载 LDF 时保留调用方默认值。</summary>
        internal static LinChecksumKind GetFrameChecksumKind(byte logicChannel, byte pid,
            LinChecksumKind fallback = LinChecksumKind.Enhanced)
        {
            var ldf = GetLdf(logicChannel);
            return ldf == null ? fallback : LinLdfHelper.GetFrameChecksumType(ldf, pid);
        }

        /// <summary>更新发送项数据和硬件帧条目。</summary>
        public static bool UpdateTransmitData(byte logicChannel, byte pid, byte[] data, byte dlc, LinTransmitType type)
        {
            if (logicChannel < 1 || logicChannel > LinConfig.Channels.Count || dlc > 8) return false;
            var cfg = LinConfig.Channels[logicChannel - 1];
            var entry = cfg.GetOrCreateTransmitEntry(pid, dlc, type);
            entry.Type = type;
            entry.Dlc = dlc == 0 ? (byte)(data == null ? 0 : data.Length) : dlc;
            entry.Data = data == null ? new byte[entry.Dlc == 0 ? 8 : entry.Dlc] : (byte[])data.Clone();
            if (entry.Data.Length != (entry.Dlc == 0 ? entry.Data.Length : entry.Dlc))
                Array.Resize(ref entry.Data, entry.Dlc);
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.ConfigureTransmitEntry(pid, type, entry.Data, entry.Dlc);
                if (_xl.TryGetValue(logicChannel, out xl)) return xl.ConfigureTransmitEntry(pid, type, entry.Data, entry.Dlc);
            }
            return false;
        }

        /// <summary>旧调用兼容：使用已配置项类型；无配置时按 Slave 处理。</summary>
        public static bool UpdateSlaveData(byte logicChannel, byte pid, byte[] data, byte dlc)
        {
            return UpdateTransmitData(logicChannel, pid, data, dlc,
                GetTransmitType(logicChannel, pid, LinTransmitType.Slave));
        }

        /// <summary>停用从节点/发布响应（取消勾选时撤销硬件自动应答）</summary>
        public static bool DisableSlaveResponse(byte logicChannel, byte pid, byte dlc)
        {
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.DisableResponse(pid);
                if (_xl.TryGetValue(logicChannel, out xl)) return xl.DisableResponse(pid);
            }
            return false;
        }

        private static void EchoTx(byte logicChannel, byte pid, byte[] data, LinChecksumKind ck)
        {
            var frame = new LinFrameRecord
            {
                LogicChannel = logicChannel,
                Pid = pid,
                Direction = LinFrameDir.Tx,
                Dlc = (byte)(data == null ? 0 : data.Length),
                Data = data != null ? (byte[])data.Clone() : new byte[0],
                ChecksumType = ck,
                ChecksumRx = data == null ? (byte)0 : LinChecksum.Calculate(data, pid, ck == LinChecksumKind.Enhanced),
                ChecksumOk = true,
                FrameName = LinLdfHelper.GetFrameName(GetLdf(logicChannel), pid),
            };
            LinReceive(logicChannel, frame);
        }

        private static LinLdfFile GetLdf(byte logicChannel)
        {
            if (logicChannel >= 1 && logicChannel <= LinConfig.Channels.Count)
                return LinConfig.Channels[logicChannel - 1].LdfHelper;
            return null;
        }

        private static long FrameKey(byte logicChannel, byte pid)
        {
            return ((long)logicChannel << 8) | pid;
        }

        /// <summary>会话毫秒（首次连接归零）</summary>
        internal static long SessionMs
        {
            get { return (long)_sw.ElapsedMilliseconds - (long)(_epochUs / 1000); }
        }

        /// <summary>判断 Header 发送后是否收到该 PID 的真实 Rx（含硬件错误帧）</summary>
        internal static bool HasRxSince(byte logicChannel, byte pid, long sinceMs)
        {
            lock (_rxActivityLock)
            {
                long last;
                return _lastRxMs.TryGetValue(FrameKey(logicChannel, pid), out last) && last >= sinceMs;
            }
        }

        /// <summary>该 PID 自 sinceMs 以来是否有任意方向帧活动（Tx 回显/Rx/硬件错误帧）。
        /// 从机监控模式：本机从机应答帧只可能由外部 Master 的 Header 触发，
        /// 因此任意方向活动都证明帧头已出现在总线上。</summary>
        internal static bool HasFrameActivitySince(byte logicChannel, byte pid, long sinceMs)
        {
            lock (_rxActivityLock)
            {
                long last;
                return _lastFrameMs.TryGetValue(FrameKey(logicChannel, pid), out last) && last >= sinceMs;
            }
        }

        /// <summary>
        /// 注入从节点响应超时错误。仅由主节点调度器对非本机发布帧调用；
        /// 这表示 Header 已按调度发出但驱动没有返回可显示的 Rx/NoResponse 事件。
        /// </summary>
        internal static void InjectNoResponse(byte logicChannel, byte pid)
        {
            var ldf = GetLdf(logicChannel);
            byte dlc = ldf != null && ldf.Frames.ContainsKey(pid) ? ldf.Frames[pid].Dlc : (byte)8;
            var frame = new LinFrameRecord
            {
                LogicChannel = logicChannel,
                Pid = pid,
                Direction = LinFrameDir.Rx,
                Dlc = dlc,
                Data = new byte[0],
                ChecksumType = GetFrameChecksumKind(logicChannel, pid),
                ErrorKind = LinErrorKind.NoResponse,
                FrameName = LinLdfHelper.GetFrameName(ldf, pid),
            };
            LinDebugLog.Write("[SCH] InjectNoResponse ch=" + logicChannel + " pid=0x" + pid.ToString("X2"));
            LinReceive(logicChannel, frame);
        }
        /// <summary>通道是否处于单设备主从一体驱动（ForceMasterDriven 运行期标志）。</summary>
        internal static bool IsMasterDriven(byte logicChannel)
        {
            return logicChannel >= 1 && logicChannel <= LinConfig.Channels.Count &&
                LinConfig.Channels[logicChannel - 1].ForceMasterDriven;
        }

        /// <summary>
        /// 主从一体软件调度回退：Header 已发出但硬件没有返回真实响应（无外部从机、硬件
        /// 自动应答在软件调度下不生效）时，注入一条带发送页数据的仿真响应帧。
        /// 仅主从一体驱动模式使用；真实网络模式仍注入无数据错误帧（InjectNoResponse）。
        /// </summary>
        internal static void SimulateSlaveResponse(byte logicChannel, byte pid)
        {
            var ldf = GetLdf(logicChannel);
            byte dlc = ldf != null && ldf.Frames.ContainsKey(pid) ? ldf.Frames[pid].Dlc : (byte)8;
            var configured = GetTransmitEntry(logicChannel, pid);
            byte[] data = new byte[dlc];
            if (configured != null && configured.Data != null && configured.Data.Length > 0)
            {
                data = new byte[configured.Data.Length];
                Array.Copy(configured.Data, data, configured.Data.Length);
            }
            var frame = new LinFrameRecord
            {
                LogicChannel = logicChannel,
                Pid = pid,
                Direction = LinFrameDir.Rx,
                Dlc = (byte)data.Length,
                Data = data,
                ChecksumType = GetFrameChecksumKind(logicChannel, pid),
                ChecksumRx = LinChecksum.Calculate(data, pid, GetFrameChecksumKind(logicChannel, pid) == LinChecksumKind.Enhanced),
                ChecksumOk = true,
                FrameName = LinLdfHelper.GetFrameName(ldf, pid),
            };
            LinDebugLog.Write("[SCH] SimulateSlaveResponse ch=" + logicChannel + " pid=0x" + pid.ToString("X2") + "（主从一体软件仿真响应）");
            LinReceive(logicChannel, frame);
        }

        // ==================== 接收汇聚 ====================

        /// <summary>接收汇聚：统一时间戳 → 帧类型/名称映射（LDF）→ 事件派发
        /// 校验和判定信任硬件错误标志（PEAK ErrorFlags / Vector CRCERROR），不做软件复核——避免 cstAuto/经典校验帧误标</summary>
        public static void LinReceive(byte logicChannel, LinFrameRecord frame)
        {
            try
            {
                // 时间戳：会话时钟（硬件时间戳不一致，统一用 Stopwatch 会话归零）
                frame.TimestampUs = (ulong)_sw.ElapsedMilliseconds * 1000 - _epochUs;

                lock (_rxActivityLock)
                    _lastFrameMs[FrameKey(logicChannel, frame.Pid)] = SessionMs;

                // 从节点响应超时兜底只看真实 Rx；软件 Tx 回显不能证明总线上出现了响应。
                if (frame.Direction == LinFrameDir.Rx)
                {
                    lock (_rxActivityLock)
                        _lastRxMs[FrameKey(logicChannel, frame.Pid)] = SessionMs;
                }

                // 帧类型/名称映射：LDF 命中则用其定义；无 LDF 时按诊断帧 ID 兜底
                var ldf = GetLdf(logicChannel);
                frame.ChecksumType = GetFrameChecksumKind(logicChannel, frame.Pid, frame.ChecksumType);
                LinFrameDef def = null;
                if (ldf != null) ldf.Frames.TryGetValue(frame.Pid, out def);
                if (def != null)
                {
                    if (string.IsNullOrEmpty(frame.FrameName)) frame.FrameName = def.Name;
                    if (frame.FrameType == LinFrameType.Unconditional) frame.FrameType = def.FrameType;
                }
                else
                {
                    if (string.IsNullOrEmpty(frame.FrameName))
                        frame.FrameName = "0x" + frame.Pid.ToString("X2");
                    if (frame.Pid == 0x3C || frame.Pid == 0x3D)
                        frame.FrameType = LinFrameType.Diagnostic;
                }

                var handler = LinFrameReceived;
                if (handler != null)
                {
                    foreach (Action<LinFrameRecord> d in handler.GetInvocationList())
                    {
                        try { d(frame); }
                        catch { /* 单个订阅者异常隔离 */ }
                    }
                }
            }
            catch { /* 汇聚异常不影响接收线程 */ }
        }

        // ==================== 调度表 ====================

        /// <summary>预置发送页中 Master 项的初始数据，不再按 LDF Publisher 全量抢答。</summary>
        public static void PrepareMasterFrames(byte logicChannel)
        {
            if (logicChannel < 1 || logicChannel > LinConfig.Channels.Count) return;
            var cfg = LinConfig.Channels[logicChannel - 1];
            int n = 0;
            foreach (var entry in cfg.TransmitEntries ?? new List<LinTransmitEntry>())
            {
                if (entry == null || !entry.Enabled || entry.Type != LinTransmitType.Master) continue;
                byte dlc = entry.Dlc == 0 ? GetFrameDlc(logicChannel, entry.Pid) : entry.Dlc;
                if (dlc == 0) dlc = 8;
                if (entry.Data == null || entry.Data.Length != dlc) entry.Data = new byte[dlc];
                if (HasFrameData(logicChannel, entry.Pid)) continue;
                UpdateTransmitData(logicChannel, entry.Pid, entry.Data, dlc, LinTransmitType.Master);
                n++;
            }
            LinDebugLog.Write("[SCH] PrepareMasterFrames ch=" + logicChannel + " 补预置发送页 Master 帧 " + n + " 条（全零初始数据）");
        }

        /// <summary>该帧是否已有缓存数据（PEAK/Vector 软件调度缓存）</summary>
        private static bool HasFrameData(byte logicChannel, byte pid)
        {
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan))
                    return pcan.GetFrameData(pid) != null;
                if (_xl.TryGetValue(logicChannel, out xl))
                    return xl.GetFrameData(pid) != null;
            }
            return false;
        }

        /// <summary>启动硬件调度；每槽发送类型已经编码在 LinScheduleSlot 中。</summary>
        public static string StartSchedule(byte logicChannel, List<LinScheduleSlot> slots)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in slots) { if (s.Enabled) { if (sb.Length > 0) sb.Append(','); sb.Append("0x").Append(s.Pid.ToString("X2")).Append('@').Append(s.SlotMs).Append("ms/").Append(s.TransmitType); } }
            LinDebugLog.Write("[SCH] StartSchedule ch=" + logicChannel + " slots=" + sb);
            if (!IsConnected(logicChannel))
                return "LIN 通道未连接，不能启动调度表";
            PrepareMasterFrames(logicChannel);
            PcanLinHardware pcan;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.StartSchedule(slots);
            }
            // Vector 模式：软件调度由 LinScheduler 驱动，无需硬件级操作
            return "";
        }

        public static bool SuspendSchedule(byte logicChannel)
        {
            PcanLinHardware pcan;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.SuspendSchedule();
            }
            return true;
        }

        public static bool ResumeSchedule(byte logicChannel)
        {
            PcanLinHardware pcan;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.ResumeSchedule();
            }
            return true;
        }

        // ==================== 唤醒/休眠/状态 ====================

        public static bool WakeUp(byte logicChannel)
        {
            lock (_hwLock)
            {
                PcanLinHardware pcan;
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.WakeUp();
                XlLinHardware xl;
                if (_xl.TryGetValue(logicChannel, out xl)) return xl.WakeUp();
            }
            return false;
        }

        public static bool Sleep(byte logicChannel)
        {
            lock (_hwLock)
            {
                PcanLinHardware pcan;
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.SleepCommand();
                XlLinHardware xl;
                if (_xl.TryGetValue(logicChannel, out xl)) return xl.SleepCommand();
            }
            return false;
        }

        /// <summary>总线状态文本：Active / Sleep / 未连接</summary>
        public static string GetBusStateText(byte logicChannel)
        {
            lock (_hwLock)
            {
                PcanLinHardware pcan;
                if (_pcan.TryGetValue(logicChannel, out pcan))
                {
                    switch (pcan.GetBusState())
                    {
                        case LinPlHardwareState.hwsActive: return "Active";
                        case LinPlHardwareState.hwsSleep: return "Sleep";
                        case LinPlHardwareState.hwsShortGround: return "短路";
                    }
                    return "未初始化";
                }
                if (_xl.ContainsKey(logicChannel)) return "Active";
            }
            return "未连接";
        }

        // ==================== 硬件回调入口（硬件层调用） ====================

        /// <summary>
        /// 链路丢失：通知 UI + 自动重连（最多 2 次，间隔 1s）。
        /// sender 为来源适配器实例：重连前校验它仍是字典中的当前实例（防止旧接收线程的僵尸回调重连掉新实例）
        /// </summary>
        public static void OnLinkLost(byte logicChannel, string reason, object sender)
        {
            LinDebugLog.Write("[LINK] OnLinkLost ch=" + logicChannel + " reason=" + reason + " sender=" + (sender == null ? "null" : sender.GetType().Name));
            LinkLost?.Invoke(logicChannel, reason);
            Task.Run(async () =>
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    await Task.Delay(1000);
                    object hw = null;
                    lock (_hwLock)
                    {
                        // 实例亲和性校验：sender 已不是当前实例 → 放弃重连（新实例接管）
                        if (sender is PcanLinHardware)
                        {
                            PcanLinHardware cur;
                            if (!_pcan.TryGetValue(logicChannel, out cur) || !ReferenceEquals(cur, sender)) return;
                            hw = cur;
                        }
                        else if (sender is XlLinHardware)
                        {
                            XlLinHardware cur;
                            if (!_xl.TryGetValue(logicChannel, out cur) || !ReferenceEquals(cur, sender)) return;
                            hw = cur;
                        }
                    }
                    LinDebugLog.Write("[LINK] 自动重连 attempt=" + attempt);
                    LinDisconnect(logicChannel);
                    string err = LinConnect(logicChannel);
                    LinDebugLog.Write("[LINK] 自动重连 attempt=" + attempt + " → " + (err.Length == 0 ? "成功" : "失败: " + err));
                    if (err.Length == 0) return; // 重连成功（ChannelStateChanged 已通知）
                    // 重连失败：实例未写回字典，恢复亲和占位使下一次尝试可继续
                    if (hw != null)
                    {
                        lock (_hwLock)
                        {
                            if (hw is PcanLinHardware) _pcan[logicChannel] = (PcanLinHardware)hw;
                            else _xl[logicChannel] = (XlLinHardware)hw;
                        }
                    }
                }
            });
        }

        /// <summary>总线事件（Sleep/WakeUp/Overrun）</summary>
        public static void OnBusEvent(byte logicChannel, string kind)
        {
            BusEvent?.Invoke(logicChannel, kind);
        }

        private static LinTransmitEntry GetTransmitEntry(byte logicChannel, byte pid)
        {
            if (logicChannel < 1 || logicChannel > LinConfig.Channels.Count) return null;
            return LinConfig.Channels[logicChannel - 1].FindTransmitEntry(pid);
        }

        internal static LinTransmitType GetTransmitType(byte logicChannel, byte pid, LinTransmitType fallback)
        {
            var entry = GetTransmitEntry(logicChannel, pid);
            return entry == null ? fallback : entry.Type;
        }

        /// <summary>该帧是否是发送页中明确配置的 Master 项。</summary>
        internal static bool IsMasterPublisherFrame(byte logicChannel, byte pid)
        {
            var entry = GetTransmitEntry(logicChannel, pid);
            return entry != null && entry.Enabled && entry.Type == LinTransmitType.Master;
        }
    }
}
