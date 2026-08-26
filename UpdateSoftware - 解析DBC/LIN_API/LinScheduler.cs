using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PCAN_Client.LIN_API
{
    /// <summary>调度表槽（UI 与调度引擎共用）</summary>
    public class LinScheduleSlot
    {
        public bool Enabled = true;
        public byte Pid;
        /// <summary>该槽对应的发送原语（方案 4.1）：Master=本机周期发完整帧（Header+Data+Checksum）；
        /// HeaderOnly=本机周期发 Header 等待远端响应；Slave=只武装本机响应等待外部 Master Header
        /// （调度器不主动发 Header）；BreakOnly=当前适配器不支持。</summary>
        public LinTransmitType TransmitType = LinTransmitType.Master;
        public int SlotMs = 10;
        /// <summary>运行计数（调度循环执行次数）</summary>
        public long Counter;
    }

    internal sealed class LinWinmmTimer : IDisposable
    {
        private delegate void TimeProc(uint uID, uint uMsg, IntPtr dwUser, IntPtr dw1, IntPtr dw2);
        [DllImport("winmm.dll")]
        private static extern uint timeSetEvent(uint uDelay, uint uResolution, TimeProc lpTimeProc, IntPtr dwUser, uint fuEvent);
        [DllImport("winmm.dll")]
        private static extern uint timeKillEvent(uint uTimerID);

        private const uint TIME_PERIODIC = 1;
        private TimeProc _proc;
        private uint _timerId;

        /// <summary>是否存在活动定时器（生命周期诊断用）</summary>
        public bool IsActive => _timerId != 0;

        /// <summary>启动周期回调（periodMs 毫秒）</summary>
        public bool Start(int periodMs, Action callback)
        {
            Stop();
            _proc = (id, msg, user, d1, d2) => { try { callback(); } catch { } };
            _timerId = timeSetEvent((uint)periodMs, 1, _proc, IntPtr.Zero, TIME_PERIODIC);
            return _timerId != 0;
        }

        public void Stop()
        {
            if (_timerId != 0)
            {
                timeKillEvent(_timerId);
                _timerId = 0;
            }
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// 调度表引擎（方案 4.1/4.2）：
    /// - Master/HeaderOnly 槽进入周期调度循环（本机发 Header/完整帧）
    /// - 纯 Slave 计划进入被动监听：已武装响应，等待外部 Master Header，不启动定时器
    /// - HeaderOnly 的响应关联使用带序列号的有序 pending 队列 + 真实 Rx 到达队列；
    ///   同 PID 重复周期请求不互相覆盖，真实 Rx 只完成最早未完成请求，超时每个 pending 只结算一次
    /// </summary>
    internal sealed class LinScheduler : IDisposable
    {
        private readonly byte _logicChannel;
        private readonly bool _useHardwareSchedule; // 保留硬件路径；当前 PEAK/Vector UI 使用软件调度
        private readonly List<LinScheduleSlot> _slots = new List<LinScheduleSlot>();
        private readonly LinWinmmTimer _timer = new LinWinmmTimer();
        private volatile bool _running;
        private bool _passiveListening;
        private int _cursor;
        private long _nextDueMs;
        private string _lastError = "";
        private bool _disposed;
        /// <summary>防重入：OnTick 在派发回调尚未返回时再次进入即放弃本次</summary>
        private int _inTick;

        /// <summary>全局递增实例号（通道唯一缓存审计：区分新旧调度器）</summary>
        public int InstanceId { get; private set; }
        private static int _instanceSeq;

        /// <summary>活动定时器状态（生命周期诊断）</summary>
        public bool IsTimerActive => _timer.IsActive;

        // ==================== 响应关联（方案 4.2） ====================
        private sealed class PendingEntry
        {
            public long Seq;          // 单调递增序列号：与 (通道, 裸 PID) 一起唯一标识一次请求
            public byte Pid;
            public LinTransmitType Type;
            public long SentMs;       // SessionMs 基准
            public long DeadlineMs;
        }
        private sealed class RcvActivity
        {
            public long Ms;           // SessionMs 基准（frame.TimestampUs/1000）
            public LinErrorKind Kind;
        }
        private readonly List<PendingEntry> _pending = new List<PendingEntry>();
        private readonly Dictionary<byte, Queue<RcvActivity>> _rxArrivals = new Dictionary<byte, Queue<RcvActivity>>();
        private readonly object _activityLock = new object();
        private long _pendingSeq;
        /// <summary>无应答限频：同一 PID 冷却期内只报一次，避免红灯刷屏（矩阵 D：不重复刷屏）</summary>
        private readonly Dictionary<byte, long> _noResponseCooldownUntil = new Dictionary<byte, long>();
        private const long NoResponseCooldownMs = 2000;
        /// <summary>响应窗口下限（定时器/接收线程抖动余量；远小于常见调度周期）</summary>
        private const long ResponseTimeoutFloorMs = 500;

        /// <summary>当前槽变化（UI 高亮刷新）</summary>
        public event Action<int> SlotChanged;
        /// <summary>运行状态变化（UI 状态栏）</summary>
        public event Action<bool> RunningChanged;

        /// <summary>可注入调度时钟（测试用）；生产默认 QPC 毫秒（64 位无回绕）</summary>
        internal static Func<long> ClockMs = () =>
            System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>测试钩子：置 true 时 Start/Resume 不启动真实 winmm 定时器（由测试直接驱动 OnTick）</summary>
        internal bool IsTimerSuppressedForTest;

        public LinScheduler(byte logicChannel, bool useHardwareSchedule)
        {
            _logicChannel = logicChannel;
            _useHardwareSchedule = useHardwareSchedule;
            InstanceId = ++_instanceSeq;
            LinDebugLog.Write("[SCH] 构造 ch=" + logicChannel + " instance=" + InstanceId);
            // 真实总线帧（仅 Rx 方向；Tx 提交回显不算）进入到达队列，供 pending 完成/超时判定
            Lin_API.LinFrameReceived += OnPlannedFrameReceived;
        }

        /// <summary>释放：退订静态事件（防窗体关闭/通道断开后泄漏）、停表、清理活动队列。幂等：重复调用安全。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LinDebugLog.Write("[SCH] Dispose ch=" + _logicChannel + " instance=" + InstanceId +
                " running=" + _running + " timerActive=" + _timer.IsActive);
            Lin_API.LinFrameReceived -= OnPlannedFrameReceived;
            _timer.Stop();
            _running = false;
            _passiveListening = false;
            ClearActivity();
        }

        public bool IsRunning => _running;

        /// <summary>已释放（窗体侧缓存判断：LinDisconnect 同步释放后不可再复用）</summary>
        public bool IsDisposed => _disposed;

        /// <summary>当前计划只有 Slave 响应项：已武装，等待外部 Master Header，软件调度器不发 Header。</summary>
        public bool IsPassiveListening => _passiveListening;

        /// <summary>槽列表（UI 直接编辑）</summary>
        public List<LinScheduleSlot> Slots => _slots;

        /// <summary>当前槽索引（UI 高亮）</summary>
        public int CurrentSlotIndex => _cursor;

        public void SetSlots(List<LinScheduleSlot> slots)
        {
            lock (_slots)
            {
                _slots.Clear();
                foreach (var s in slots) _slots.Add(s);
            }
            _cursor = 0;
            ClearActivity();
            SyncSnapshots();
        }

        /// <summary>把当前槽同步为运行快照（启用项 → Intent/Armed；移除/停用 → Disabled），
        /// 并撤销停用项的 in-flight pending 与冷却项（Disabled 为吸收态：不会被超时翻成 NoResponse）。</summary>
        public void SyncSnapshots()
        {
            List<LinScheduleSlot> snapshot;
            lock (_slots) { snapshot = new List<LinScheduleSlot>(_slots); }
            Lin_API.SyncRunSnapshotsFromPlan(_logicChannel, snapshot);
            PurgeDisabledPending(snapshot);
        }

        /// <summary>停用/删除项：撤销在途 pending 与无应答冷却项，防止 Disabled 后 deadline 到期把快照覆盖为 NoResponse 并注入合成错误帧（矩阵 G）</summary>
        private void PurgeDisabledPending(List<LinScheduleSlot> slots)
        {
            var enabled = new HashSet<byte>();
            foreach (var slot in slots)
                if (slot != null && slot.Enabled && slot.Pid <= 0x3F) enabled.Add(slot.Pid);
            lock (_pending)
            {
                for (int i = _pending.Count - 1; i >= 0; i--)
                    if (!enabled.Contains(_pending[i].Pid)) _pending.RemoveAt(i);
            }
            lock (_activityLock)
            {
                var doomed = new List<byte>();
                foreach (var kv in _noResponseCooldownUntil)
                    if (!enabled.Contains(kv.Key)) doomed.Add(kv.Key);
                foreach (byte pid in doomed) _noResponseCooldownUntil.Remove(pid);
            }
        }

        private bool IsPidEnabled(byte pid)
        {
            lock (_slots)
            {
                foreach (var slot in _slots)
                    if (slot != null && slot.Pid == pid && slot.Enabled) return true;
            }
            return false;
        }

        private void ClearActivity()
        {
            lock (_pending) _pending.Clear();
            lock (_activityLock) _rxArrivals.Clear();
            lock (_activityLock) _noResponseCooldownUntil.Clear();
        }

        /// <summary>真实 Rx 到达（Tx 回显与自注入的 NoResponse 帧不进入队列）</summary>
        /// <summary>到达队列每 PID 上限（防无界增长：被动监听等无 pending 消费场景下丢弃最旧）</summary>
        private const int MaxArrivalsPerPid = 128;

        private void OnPlannedFrameReceived(LinFrameRecord frame)
        {
            if (frame.LogicChannel != _logicChannel || frame.Direction != LinFrameDir.Rx) return;
            if (frame.ErrorKind == LinErrorKind.NoResponse) return; // 自注入/驱动无应答帧：结果即超时，不重复完成
            lock (_activityLock)
            {
                Queue<RcvActivity> q;
                if (!_rxArrivals.TryGetValue(frame.Pid, out q))
                {
                    q = new Queue<RcvActivity>();
                    _rxArrivals[frame.Pid] = q;
                }
                // 与 pending 的 SentMs/DeadlineMs 同用 SessionMs 时钟基准（可注入，测试与生产一致）
                q.Enqueue(new RcvActivity { Ms = Lin_API.SessionMs, Kind = frame.ErrorKind });
                while (q.Count > MaxArrivalsPerPid) q.Dequeue();
            }
            // 方案 4.2：真实 Rx 覆盖请求窗口 → 立即完成最早未完成请求（状态即时翻转为 Completed，
            // 不必等 deadline；deadline 仅作为无到达时的超时裁决，CheckPendingResponses 兜底）。
            TrySettleEarlyPending(frame.Pid);
        }

        /// <summary>真实 Rx 到达即结算：窗口覆盖该到达的最早未完成 pending 立即完成。</summary>
        private void TrySettleEarlyPending(byte pid)
        {
            PendingEntry p;
            lock (_pending)
            {
                p = null;
                foreach (var e in _pending)
                    if (e.Pid == pid) { p = e; break; }
                if (p == null) return;          // 无待响应请求（例：被动监听收到的帧）
                if (Lin_API.SessionMs > p.DeadlineMs) return; // 窗口已过：留给 CheckPendingResponses 裁决
                _pending.Remove(p);
            }
            var matched = ConsumeArrival(pid, p.SentMs, p.DeadlineMs);
            if (matched == null)
            {
                // 极端竞争：到达被并发结算消费 → 按 Seq 恢复原队列位置，交回正常超时裁决
                // （不得尾插：List 按 Seq 有序，尾插会破坏"真实 Rx 只完成最早 pending"的次序）
                lock (_pending)
                {
                    int ins = 0;
                    while (ins < _pending.Count && _pending[ins].Seq < p.Seq) ins++;
                    _pending.Insert(ins, p);
                }
                return;
            }
            if (matched.Kind == LinErrorKind.None)
                Lin_API.NotifyTxState(_logicChannel, pid, p.Type, LinTxEventKind.ResponseComplete, "真实响应完成");
            else
                Lin_API.NotifyTxState(_logicChannel, pid, p.Type, LinTxEventKind.Error,
                    "总线错误帧完成响应: " + matched.Kind, matched.Kind);
        }

        /// <summary>校验所有启用槽的时隙是否满足帧最小传输时间；返回不合法槽描述（空=全部合法）</summary>
        public string ValidateSlots(uint baudrate)
        {
            LinLdfFile ldf = null;
            if (_logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count)
                ldf = LinConfig.Channels[_logicChannel - 1].LdfHelper;
            foreach (var s in _slots)
            {
                if (!s.Enabled) continue;
                if (s.TransmitType == LinTransmitType.BreakOnly)
                    return $"帧 0x{s.Pid:X2} 使用 BreakOnly，但当前 PCAN/Vector 适配器不提供独立 Break 原语";
                if (s.SlotMs <= 0)
                    return $"帧 0x{s.Pid:X2} 时隙必须大于 0ms";
                // Slave 槽不产生 Header，时隙只用于计划占位，不需要按总线传输时间校验。
                if (s.TransmitType == LinTransmitType.Slave) continue;
                byte dlc = LinLdfHelper.GetFrameDlc(ldf, s.Pid);
                if (dlc == 0) dlc = 8; // 无 LDF 时按最大帧保守校验
                double minMs = LinChecksum.MinFrameTimeMs(dlc, baudrate);
                if (s.SlotMs < minMs)
                    return $"帧 0x{s.Pid:X2} 时隙 {s.SlotMs}ms 小于最小传输时间 {minMs:F1}ms（{baudrate} 波特率，{dlc} 字节）";
            }
            return "";
        }

        /// <summary>最近一次启动失败的详细错误（空=无）</summary>
        public string LastError => _lastError;

        public bool Start()
        {
            LinDebugLog.Write("[SCH] Start ch=" + _logicChannel + " instance=" + InstanceId +
                " running=" + _running + " timerActive=" + _timer.IsActive + " reason=周期启停");
            if (_running) Suspend();
            else _timer.Stop();
            _passiveListening = false;
            if (!Lin_API.IsConnected(_logicChannel))
            {
                _lastError = "LIN 通道未连接，不能启动调度表";
                return false;
            }

            bool hasEnabled = false;
            bool hasActive = false; // Master/HeaderOnly 需要本机周期发 Header
            foreach (var slot in _slots)
            {
                if (slot == null || !slot.Enabled) continue;
                hasEnabled = true;
                if (slot.TransmitType != LinTransmitType.Slave) hasActive = true;
            }
            if (!hasEnabled)
            {
                _lastError = "没有启用的调度槽（请在发送列表勾选周期发送）";
                return false;
            }
            _lastError = "";
            ClearActivity();
            SyncSnapshots();

            if (_useHardwareSchedule)
            {
                if (!hasActive)
                {
                    // 纯 Slave 计划：被动监听（RESPONSE_ENABLE 已武装），不进硬件调度表
                    _running = true;
                    _passiveListening = true;
                    RunningChanged?.Invoke(true);
                    return true;
                }
                _lastError = Lin_API.StartSchedule(_logicChannel, _slots);
                if (_lastError.Length > 0) return false;
            }
            else
            {
                if (!hasActive)
                {
                    // 纯 Slave 计划：已武装，等待外部 Master Header；不启动定时器、不主动发 Header
                    _running = true;
                    _passiveListening = true;
                    RunningChanged?.Invoke(true);
                    return true;
                }
                // 软件调度：预置 Master 项数据；Master/HeaderOnly 槽按周期发 Header
                Lin_API.PrepareMasterFrames(_logicChannel);
                _cursor = 0;
                _nextDueMs = ClockMs();
                if (!IsTimerSuppressedForTest && !_timer.Start(1, OnTick))
                {
                    _lastError = "定时器启动失败";
                    return false;
                }
            }
            _running = true;
            RunningChanged?.Invoke(true);
            return true;
        }

        public void Suspend()
        {
            LinDebugLog.Write("[SCH] Suspend ch=" + _logicChannel + " instance=" + InstanceId +
                " running=" + _running + " timerActive=" + _timer.IsActive);
            if (!_running)
            {
                _passiveListening = false;
                ClearActivity();
                _timer.Stop();
                return;
            }
            _running = false; // 先置位：已派发的 winmm 回调在 OnTick 开头被拦截，不再多发 Header
            _passiveListening = false;
            ClearActivity();
            if (_useHardwareSchedule) Lin_API.SuspendSchedule(_logicChannel);
            else _timer.Stop();
            RunningChanged?.Invoke(false);
        }

        public void Resume()
        {
            LinDebugLog.Write("[SCH] Resume ch=" + _logicChannel + " instance=" + InstanceId +
                " running=" + _running + " timerActive=" + _timer.IsActive);
            if (_running) return;
            bool hasActive = false;
            foreach (var slot in _slots)
                if (slot != null && slot.Enabled && slot.TransmitType != LinTransmitType.Slave) { hasActive = true; break; }
            if (_useHardwareSchedule)
            {
                if (hasActive && !Lin_API.ResumeSchedule(_logicChannel)) return;
            }
            else if (hasActive)
            {
                _nextDueMs = ClockMs();
                if (!IsTimerSuppressedForTest && !_timer.Start(1, OnTick)) return;
            }
            _running = true;
            RunningChanged?.Invoke(true);
        }

        public void Stop()
        {
            LinDebugLog.Write("[SCH] Stop ch=" + _logicChannel + " instance=" + InstanceId +
                " running=" + _running + " timerActive=" + _timer.IsActive);
            Suspend();
            foreach (var s in _slots) s.Counter = 0;
            _cursor = 0;
            ClearActivity();
        }

        private void OnTick()
        {
            if (!_running) return; // 已暂停/停止：已派发的回调直接放弃
            if (System.Threading.Interlocked.CompareExchange(ref _inTick, 1, 0) != 0) return; // 防重入：派发未返回时放弃本次
            try
            {
                OnTickCore();
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _inTick, 0);
            }
        }

        /// <summary>OnTick 实际派发主体（防重入守卫包住，保证异常后标志复位）</summary>
        private void OnTickCore()
        {
            if (!_running) return; // 已暂停/停止：已派发的回调直接放弃
            long now = ClockMs(); // 64 位无回绕
            if (now < _nextDueMs) return;
            CheckPendingResponses(Lin_API.SessionMs);

            // 槽快照 + 锁：与 UI 线程并发安全（快照后 List 修改不影响本次遍历）
            List<LinScheduleSlot> snapshot;
            lock (_slots) { snapshot = new List<LinScheduleSlot>(_slots); }
            if (snapshot.Count == 0) return;

            // 找下一个可调度启用槽（Master/HeaderOnly 由本机周期发 Header；Slave 槽跳过——
            // 全表无活跃槽则停，由下次 Start/Suspend 回收定时器）
            int start = _cursor % snapshot.Count;
            int idx = start;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var s = snapshot[idx];
                if (s != null && s.Enabled && s.TransmitType != LinTransmitType.Slave) break;
                idx = (idx + 1) % snapshot.Count;
                if (idx == start)
                {
                    if (_running)
                    {
                        _running = false;
                        RunningChanged?.Invoke(false);
                    }
                    return;
                }
            }

            var slot = snapshot[idx];
            _cursor = (idx + 1) % snapshot.Count;
            _nextDueMs = now + slot.SlotMs; // 累计式：基于实际时刻，防漂移

            bool dispatched = DispatchSlot(slot, Lin_API.SessionMs);
            if (dispatched) slot.Counter++;
            LinDebugLog.Write("[SCH] tick ch=" + _logicChannel + " idx=" + idx + " pid=0x" + slot.Pid.ToString("X2") + " slotMs=" + slot.SlotMs + " type=" + slot.TransmitType + " dispatched=" + dispatched);
            SlotChanged?.Invoke(idx);
        }

        /// <summary>
        /// 统一调度原语（方案 4.1/4.2）：
        /// Master=完整帧（无 pending）；HeaderOnly=发 Header 并登记带序列号的 pending；
        /// Slave=不产生总线动作；BreakOnly=明确不支持。提交结果与等待语义在此统一。
        /// </summary>
        private bool DispatchSlot(LinScheduleSlot slot, long sessionNowMs)
        {
            // 连接门控（方案阶段 1 审查 F1）：调度器可能仍在旧连接上运行（1ms winmm 定时器），
            // 而连接已被断开/重连、新硬件已注册。此时必须拒绝派发，避免旧实例对新连接发 TX；
            // 释放的最终责任仍在注册表，但门控使释放不依赖 UI 消息队列时序。
            if (!Lin_API.IsConnected(_logicChannel)) return false;
            switch (slot.TransmitType)
            {
                case LinTransmitType.Slave:
                    // 响应已由连接时 RESPONSE_ENABLE/SetSlave 武装；调度器不主动发 Header
                    return false;
                case LinTransmitType.BreakOnly:
                    Lin_API.NotifyTxState(_logicChannel, slot.Pid, slot.TransmitType,
                        LinTxEventKind.Unsupported, "当前适配器不支持独立 Break 原语");
                    return false;
                case LinTransmitType.Master:
                {
                    bool ok = Lin_API.LinSendScheduleSlot(_logicChannel, slot);
                    Lin_API.NotifyTxState(_logicChannel, slot.Pid, slot.TransmitType,
                        ok ? LinTxEventKind.SubmitOk : LinTxEventKind.SubmitFail,
                        ok ? "" : "周期发送提交失败");
                    return ok;
                }
                case LinTransmitType.HeaderOnly:
                {
                    bool ok = Lin_API.LinSendScheduleSlot(_logicChannel, slot);
                    Lin_API.NotifyTxState(_logicChannel, slot.Pid, slot.TransmitType,
                        ok ? LinTxEventKind.SubmitOk : LinTxEventKind.SubmitFail,
                        ok ? "" : "Header 提交失败");
                    if (ok)
                    {
                        long deadline = sessionNowMs + ResponseTimeoutMs(slot);
                        lock (_pending)
                        {
                            _pending.Add(new PendingEntry
                            {
                                Seq = ++_pendingSeq,
                                Pid = slot.Pid,
                                Type = slot.TransmitType,
                                SentMs = sessionNowMs,
                                DeadlineMs = deadline,
                            });
                        }
                        Lin_API.NotifyTxState(_logicChannel, slot.Pid, slot.TransmitType,
                            LinTxEventKind.ResponseWait, "等待应答");
                        // 快照记录响应截止（微秒），供 UI 显示等待窗口
                        Lin_API.SetExpectedResponseDeadline(_logicChannel, slot.Pid, deadline * 1000);
                    }
                    return ok;
                }
            }
            return false;
        }

        /// <summary>响应窗口 = 时隙 + 帧最小传输时间 + 30ms 余量（不再用无上下文固定值），下限 500ms</summary>
        private long ResponseTimeoutMs(LinScheduleSlot slot)
        {
            uint baud = 19200;
            if (_logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count)
                baud = LinConfig.Channels[_logicChannel - 1].Baudrate;
            double frameMs = LinChecksum.MinFrameTimeMs(SlotDlc(slot), baud);
            long t = slot.SlotMs + (long)Math.Ceiling(frameMs) + 30;
            return Math.Max(t, ResponseTimeoutFloorMs);
        }

        private byte SlotDlc(LinScheduleSlot slot)
        {
            if (_logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count)
            {
                var cfg = LinConfig.Channels[_logicChannel - 1];
                var entry = cfg.FindTransmitEntry(slot.Pid);
                if (entry != null && entry.Dlc > 0 && entry.Dlc <= 8) return entry.Dlc;
                if (cfg.LdfHelper != null)
                {
                    byte dlc = LinLdfHelper.GetFrameDlc(cfg.LdfHelper, slot.Pid);
                    if (dlc > 0 && dlc <= 8) return dlc;
                }
            }
            return 8;
        }

        /// <summary>
        /// 响应超时兜底：真实 Rx（含驱动错误帧）优先作为结果，只完成对应最早未完成请求；
        /// 窗口到期且无匹配 Rx 才生成一条 NoResponse（每个 pending 只结算一次；同 PID 冷却期内限频）。
        /// </summary>
        private void CheckPendingResponses(long nowMs)
        {
            PendingEntry[] pending;
            lock (_pending) { pending = _pending.ToArray(); }
            foreach (var p in pending)
            {
                // 边界互补：提前结算用 <=Deadline（TrySettleEarlyPending），超时裁决只用 >Deadline，
                // 避免 == 毫秒上接收线程与定时器线程对同一 pending 并发结算。
                if (nowMs <= p.DeadlineMs) continue;
                lock (_pending) { if (!_pending.Remove(p)) continue; } // 每个 pending 只结算一次；已被提前结算移除则跳过
                if (!IsPidEnabled(p.Pid)) continue; // 已停用：不结算（Disabled 为吸收态，不注入错误帧）

                RcvActivity matched = ConsumeArrival(p.Pid, p.SentMs, p.DeadlineMs);
                if (matched != null)
                {
                    if (matched.Kind == LinErrorKind.None)
                        Lin_API.NotifyTxState(_logicChannel, p.Pid, p.Type, LinTxEventKind.ResponseComplete, "真实响应完成");
                    else
                        Lin_API.NotifyTxState(_logicChannel, p.Pid, p.Type, LinTxEventKind.Error,
                            "总线错误帧完成响应: " + matched.Kind, matched.Kind);
                    continue;
                }

                // 无真实 Rx → 无应答（限频：同一 PID 冷却期内只报一次，避免红灯刷屏）
                bool doEmit;
                lock (_activityLock)
                {
                    long until;
                    doEmit = !_noResponseCooldownUntil.TryGetValue(p.Pid, out until) || nowMs >= until;
                    if (doEmit) _noResponseCooldownUntil[p.Pid] = nowMs + NoResponseCooldownMs;
                }
                if (doEmit)
                {
                    // 阶段 2：超时只发布运行错误事件（ResponseTimeout 进错误锁存），
                    // 不再构造 Direction=Rx 的 NoResponse 伪帧进 LinReceive/LinFrameReceived。
                    Lin_API.NotifyTxState(_logicChannel, p.Pid, p.Type, LinTxEventKind.ResponseTimeout, "无应答");
                }
            }
        }

        /// <summary>消费该 PID 在 [sentMs, deadlineMs] 内的最早真实 Rx（排掉过期项）；null=无匹配</summary>
        private RcvActivity ConsumeArrival(byte pid, long sentMs, long deadlineMs)
        {
            lock (_activityLock)
            {
                Queue<RcvActivity> q;
                if (!_rxArrivals.TryGetValue(pid, out q)) return null;
                while (q.Count > 0 && q.Peek().Ms < sentMs) q.Dequeue();
                if (q.Count > 0 && q.Peek().Ms <= deadlineMs) return q.Dequeue();
                return null;
            }
        }
    }
}