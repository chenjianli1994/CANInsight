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

        // ==================== 响应超时监控（调度运行期） ====================
        // 通道+PID → 最近一次总线活动（收到该帧任何记录）的会话毫秒。
        // 调度器按槽位检测：窗口内无活动 = 期待响应未发生（外部无帧头/无应答），注入 NoResponse 错误帧。
        private static readonly Dictionary<long, long> _pidActivityMs = new Dictionary<long, long>();
        private static readonly object _pidLock = new object();

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
                ChannelStateChanged?.Invoke(logicChannel, false, "");
            }
        }

        // ==================== 发送 ====================

        /// <summary>手动发送一帧；失败返回 false（原因经状态事件/返回值）</summary>
        public static bool LinTransmit(byte logicChannel, byte pid, byte[] data, LinChecksumKind ck)
        {
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

        /// <summary>调度表发 Header（软件调度；Vector 用 XL_SendRequest，PEAK 用 Write dirSubscriber）</summary>
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
            if (ok) EchoTx(logicChannel, pid, null, LinChecksumKind.Enhanced);
            return ok;
        }

        /// <summary>调度表发一帧（软件调度）：PEAK 有数据发完整帧，无数据发 Header；Vector 返回 false 回退 Header</summary>
        public static bool LinSendScheduleFrame(byte logicChannel, byte pid)
        {
            PcanLinHardware pcan;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan))
                {
                    bool ok = pcan.SendScheduleFrame(pid, GetFrameDlc(logicChannel, pid));
                    LinDebugLog.Write("[SCH] LinSendScheduleFrame ch=" + logicChannel + " pid=0x" + pid.ToString("X2") + " → " + ok);
                    // 回显：硬件不回传本端发送帧，软件调度须自行显示（数据为缓存帧数据，无缓存为 Header 记录）
                    if (ok) EchoTx(logicChannel, pid, pcan.GetFrameData(pid), LinChecksumKind.Enhanced);
                    return ok;
                }
            }
            LinDebugLog.Write("[SCH] LinSendScheduleFrame ch=" + logicChannel + " pid=0x" + pid.ToString("X2") + " → false（非 PEAK，回退 LinSendHeader）");
            return false;
        }

        private static byte GetFrameDlc(byte logicChannel, byte pid)
        {
            var ldf = GetLdf(logicChannel);
            if (ldf != null && ldf.Frames.ContainsKey(pid)) return ldf.Frames[pid].Dlc;
            return 0;
        }

        /// <summary>更新发布/从节点响应数据</summary>
        public static bool UpdateSlaveData(byte logicChannel, byte pid, byte[] data, byte dlc)
        {
            PcanLinHardware pcan;
            XlLinHardware xl;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) return pcan.UpdateSlaveData(pid, data);
                if (_xl.TryGetValue(logicChannel, out xl)) return xl.UpdateSlaveData(pid, data, dlc);
            }
            return false;
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
                ChecksumRx = LinChecksum.Calculate(data, pid, ck == LinChecksumKind.Enhanced),
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

        // ==================== 接收汇聚 ====================

        /// <summary>接收汇聚：统一时间戳 → 帧类型/名称映射（LDF）→ 事件派发
        /// 校验和判定信任硬件错误标志（PEAK ErrorFlags / Vector CRCERROR），不做软件复核——避免 cstAuto/经典校验帧误标</summary>
        public static void LinReceive(byte logicChannel, LinFrameRecord frame)
        {
            try
            {
                // 时间戳：会话时钟（硬件时间戳不一致，统一用 Stopwatch 会话归零）
                frame.TimestampUs = (ulong)_sw.ElapsedMilliseconds * 1000 - _epochUs;

                // 总线活动记录（响应超时判定）：仅 Rx 记录算活动——Tx 是本机发送回显（EchoTx），
                // 若计入会把"本机刚发送"误当"从节点应答"，主节点模式超时检测将永不触发。
                // 含硬件错误帧（errFlags 非 0）——说明 Header 已到总线，也算活动。
                if (frame.Direction == LinFrameDir.Rx)
                {
                    lock (_pidLock) { _pidActivityMs[((long)logicChannel << 8) | frame.Pid] = SessionMs; }
                }

                // 帧类型/名称映射：LDF 命中则用其定义；无 LDF 时按诊断帧 ID 兜底
                var ldf = GetLdf(logicChannel);
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

        /// <summary>
        /// 预置 LDF 中主节点发布帧的初始数据（全零，后续由发布数据页签修改）到硬件帧条目/软件缓存。
        /// 仅主节点模式：从节点模式下预置会让本机对主节点发布帧配置 RESPONSE_ENABLE 自动应答，
        /// 与真实主节点/真实从节点抢答 → 总线数据冲突、全部校验和错误（实测症状：接入外部主节点后
        /// 所有报文报错误帧）。从节点模式只应答自己发布的帧（由 ConfigureFrameEntries/从节点页签配置）。
        /// 软件调度（Vector/PEAK 均为 LinScheduler 驱动）与硬件调度启动前都必须调用——
        /// 否则主节点发布帧无缓存数据，发送时退化为 Header-only（协议违规：主节点发布帧必须有数据槽），
        /// 从节点把该帧判为错误帧（实测日志：master 模式全部 SendScheduleFrame 无缓存数据 → Header-only）。
        /// </summary>
        public static void PrepareMasterFrames(byte logicChannel)
        {
            bool master = logicChannel >= 1 && logicChannel <= LinConfig.Channels.Count &&
                          LinConfig.Channels[logicChannel - 1].Mode == LinNodeMode.Master;
            if (!master) return;
            var ldf = GetLdf(logicChannel);
            if (ldf == null) return;
            int n = 0;
            foreach (var kv in ldf.Frames)
            {
                if (kv.Value.Publisher != ldf.MasterName) continue;
                // 已有用户配置数据（发送页/发布数据页签先于调度写入）时不覆盖，仅补未配置帧的全零初始数据
                if (HasFrameData(logicChannel, kv.Key)) continue;
                UpdateSlaveData(logicChannel, kv.Key, new byte[kv.Value.Dlc == 0 ? 8 : kv.Value.Dlc], kv.Value.Dlc);
                n++;
            }
            LinDebugLog.Write("[SCH] PrepareMasterFrames ch=" + logicChannel + " 补预置主节点发布帧 " + n + " 条（全零初始数据）");
        }

        /// <summary>该帧是否已有缓存数据（PEAK 软件调度缓存；Vector 无独立缓存，返回 false 即始终走全零预置）</summary>
        private static bool HasFrameData(byte logicChannel, byte pid)
        {
            PcanLinHardware pcan;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan))
                    return pcan.GetFrameData(pid) != null;
            }
            return false;
        }

        /// <summary>
        /// 启动调度：先预置主节点发布帧的初始数据（Vector 需 XL_LinSetSlave 配置响应，否则发 Header 后无应答；
        /// PEAK 需帧条目数据，否则发全零帧），再启动硬件调度（PEAK）或软件调度（Vector 由 LinScheduler 驱动）。
        /// </summary>
        public static string StartSchedule(byte logicChannel, List<LinScheduleSlot> slots)
        {
            bool master = logicChannel >= 1 && logicChannel <= LinConfig.Channels.Count &&
                          LinConfig.Channels[logicChannel - 1].Mode == LinNodeMode.Master;
            var sb = new System.Text.StringBuilder();
            foreach (var s in slots) { if (s.Enabled) { if (sb.Length > 0) sb.Append(','); sb.Append("0x").Append(s.Pid.ToString("X2")).Append('@').Append(s.SlotMs).Append("ms"); } }
            LinDebugLog.Write("[SCH] StartSchedule ch=" + logicChannel + " master=" + master + " slots=" + sb);
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

        // ==================== 响应超时监控 API ====================

        /// <summary>会话毫秒（首次连接归零）</summary>
        internal static long SessionMs => (long)_sw.ElapsedMilliseconds - (long)(_epochUs / 1000);

        /// <summary>某通道某帧在 timeoutMs 窗口内是否有总线活动（无记录 = 从未活动 = 超时）</summary>
        internal static bool HasPidActivity(byte logicChannel, byte pid, long timeoutMs)
        {
            lock (_pidLock)
            {
                long t;
                if (!_pidActivityMs.TryGetValue(((long)logicChannel << 8) | pid, out t)) return false;
                return SessionMs - t < timeoutMs;
            }
        }

        /// <summary>
        /// 注入无应答错误帧（调度期待响应但窗口内无总线活动：外部无帧头/无应答）。
        /// 软件生成原因：硬件只在"收到 Header 但应答失败"时上报错误；根本没收到帧头时
        /// 不产生任何记录，从节点模式无外部主节点驱动时列表将无任何提示。
        /// 注入帧走 LinReceive 会刷新活动时间，配合调度器侧冷却实现稳定节流（每超时窗口一条）。
        /// </summary>
        internal static void InjectNoResponse(byte logicChannel, byte pid)
        {
            LinDebugLog.Write("[SCH] InjectNoResponse ch=" + logicChannel + " pid=0x" + pid.ToString("X2") + "（窗口内无 Rx 活动）");
            var ldf = GetLdf(logicChannel);
            byte dlc = ldf != null && ldf.Frames.ContainsKey(pid) ? ldf.Frames[pid].Dlc : (byte)8;
            var frame = new LinFrameRecord
            {
                LogicChannel = logicChannel,
                Pid = pid,
                Direction = LinFrameDir.Rx,
                Dlc = dlc,
                Data = new byte[0],
                ChecksumType = LinChecksumKind.Enhanced,
                ErrorKind = LinErrorKind.NoResponse,
                FrameName = LinLdfHelper.GetFrameName(ldf, pid),
            };
            LinReceive(logicChannel, frame);
        }

        /// <summary>该帧是否为 LDF 中主节点发布帧（主节点模式发完整帧后无需应答，超时检测应跳过）；无 LDF 返回 false</summary>
        internal static bool IsMasterPublisherFrame(byte logicChannel, byte pid)
        {
            var ldf = GetLdf(logicChannel);
            if (ldf == null) return false;
            LinFrameDef def;
            if (!ldf.Frames.TryGetValue(pid, out def)) return false;
            return def.Publisher == ldf.MasterName;
        }
    }
}
