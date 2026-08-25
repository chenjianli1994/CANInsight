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
        /// <summary>该槽对应的发送原语；Slave 槽在非主从一体驱动下只监控外部帧头，不由调度器主动发 Header。</summary>
        public LinTransmitType TransmitType = LinTransmitType.Master;
        public int SlotMs = 10;
        /// <summary>运行计数（调度循环执行次数）</summary>
        public long Counter;
        /// <summary>从机监控状态：本周期未检测到外部 Master 发出对应帧头（UI 标红）。
        /// 仅 Slave 槽在非主从一体驱动下有意义；收到帧头后由调度器自动清除。</summary>
        public volatile bool HeaderMissing;
    }

    /// <summary>
    /// 独立 1ms 精度定时器（winmm MultimediaTimer），供 Vector 软件调度使用。
    /// 不接入 CAN 侧的全局 MultiMessageCANScheduler，保持 LIN 与 CAN 解耦。
    /// </summary>
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
    /// 调度表引擎（双硬件模式）：
    /// - PEAK：调度槽下发硬件，PLIN 固件自主运行（软件只控制起停）
    /// - Vector：软件调度——独立 winmm 定时器逐槽发 Header，累计式计时防漂移
    /// </summary>
    internal sealed class LinScheduler
    {
        private readonly byte _logicChannel;
        private readonly bool _useHardwareSchedule; // 保留硬件路径；当前 PEAK/Vector UI 使用软件调度
        private readonly List<LinScheduleSlot> _slots = new List<LinScheduleSlot>();
        private readonly LinWinmmTimer _timer = new LinWinmmTimer();
        private volatile bool _running;
        private int _cursor;
        private long _nextDueMs;
        private long _tickCount;
        private string _lastError = "";
        private bool _passiveListening;
        private struct PendingResponse
        {
            public long SentMs;
            public long DeadlineMs;
            public LinTransmitType Type;
        }
        private readonly Dictionary<byte, PendingResponse> _pendingResponses = new Dictionary<byte, PendingResponse>();
        /// <summary>从机监控：各 PID 上次帧头检查时刻（Slave 槽非主从一体驱动时使用）</summary>
        private readonly Dictionary<byte, long> _lastFrameCheckMs = new Dictionary<byte, long>();

        // Windows 定时器和 PLIN 接收线程存在调度抖动，给真实响应留出一个完整窗口；
        // 500ms 仍远小于常见调度表周期，且不会把正常响应误判为无应答。
        private const long ResponseTimeoutFloorMs = 500;
        /// <summary>当前槽变化（UI 高亮刷新）</summary>
        public event Action<int> SlotChanged;
        /// <summary>运行状态变化（UI 状态栏）</summary>
        public event Action<bool> RunningChanged;

        /// <summary>64 位毫秒时钟（QPC，无 32 位回绕问题）</summary>
        private static long NowMs()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
        }

        public LinScheduler(byte logicChannel, bool useHardwareSchedule)
        {
            _logicChannel = logicChannel;
            _useHardwareSchedule = useHardwareSchedule;
        }

        public bool IsRunning => _running;

        /// <summary>当前计划只有 Slave 响应项，硬件已配置但软件调度器不发 Header。</summary>
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
                foreach (var s in slots)
                {
                    if (s != null) s.HeaderMissing = false;
                    _slots.Add(s);
                }
            }
            _cursor = 0;
            _pendingResponses.Clear();
            _lastFrameCheckMs.Clear();
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
                // Slave 槽不产生 Header，时隙只用于调度游标，不需要按总线传输时间校验。
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
            bool channelSlave = _logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count &&
                LinConfig.Channels[_logicChannel - 1].GetHardwareMode() == LinNodeMode.Slave;
            // 通道模式可能在通道管理器中切换，而调度器实例按窗体生命周期复用。
            // 切换到从节点后，先清理旧主节点定时器，再进入被动监听状态。
            if (_running)
            {
                if (!channelSlave) return false;
                Suspend();
            }
            else
            {
                // OnTick 在“全部槽被取消勾选”时只置停止状态，不能在 winmm
                // 回调内销毁自身；下一次启动/暂停负责回收遗留定时器。
                _timer.Stop();
            }
            _passiveListening = false;
            if (!Lin_API.IsConnected(_logicChannel))
            {
                _lastError = "LIN 通道未连接，不能启动调度表";
                return false;
            }

            // 纯 Slave 发送计划且调度表为空：没有可监控的帧，仅保持被动监听状态
            // （连接时已把 RESPONSE_ENABLE 配置给硬件，报文由外部 Master 的 Header
            // 触发自动应答）。不启动空定时器，避免制造无意义的高频回调。
            if (channelSlave && _slots.Count == 0)
            {
                _lastError = "";
                _passiveListening = true;
                RunningChanged?.Invoke(false);
                return true;
            }

            if (_slots.Count == 0)
            {
                _lastError = "没有配置调度槽";
                return false;
            }

            bool hasEnabled = false;
            bool hasInitiator = false;
            // 主从一体驱动（ForceMasterDriven）：Slave 槽也由本机发 Header 驱动，视为可驱动槽。
            bool masterDriven = _logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count &&
                LinConfig.Channels[_logicChannel - 1].ForceMasterDriven;
            foreach (var slot in _slots)
            {
                if (slot == null || !slot.Enabled) continue;
                hasEnabled = true;
                if (slot.TransmitType == LinTransmitType.Master || slot.TransmitType == LinTransmitType.HeaderOnly ||
                    (masterDriven && slot.TransmitType == LinTransmitType.Slave))
                    hasInitiator = true;
            }
            if (!hasEnabled)
            {
                _lastError = "没有启用的调度槽";
                return false;
            }
            if (!hasInitiator)
            {
                // 从机监控模式（纯 Slave 计划，非主从一体驱动）：本地定时器只推进游标并
                // 检查外部 Master 是否发出对应帧头（缺失标红），本机不发送任何报文。
                // 走软件定时器路径（modSlave 无法下发硬件调度表；监控只读总线）。
                _lastError = "";
                _pendingResponses.Clear();
                _lastFrameCheckMs.Clear();
                _cursor = 0;
                _nextDueMs = NowMs();
                if (!_timer.Start(1, OnTick)) { _lastError = "定时器启动失败"; return false; }
                _running = true;
                RunningChanged?.Invoke(true);
                return true;
            }
            _lastError = "";
            _pendingResponses.Clear();
            if (_useHardwareSchedule)
            {
                _lastError = Lin_API.StartSchedule(_logicChannel, _slots);
                if (_lastError.Length > 0) return false;
            }
            else
            {
                // 软件调度（PEAK 本环境硬件调度表 errUnknown，Vector 本就软件）：
                // 预置发送页中 Master 项数据；Slave 项在连接时已配置自动响应。
                Lin_API.PrepareMasterFrames(_logicChannel);
                _cursor = 0;
                _nextDueMs = NowMs();
                if (!_timer.Start(1, OnTick)) { _lastError = "定时器启动失败"; return false; }
            }
            _running = true;
            RunningChanged?.Invoke(true);
            return true;
        }

        public void Suspend()
        {
            if (!_running)
            {
                _passiveListening = false;
                _pendingResponses.Clear();
                _timer.Stop();
                return;
            }
            _running = false; // 先置位：已派发的 winmm 回调在 OnTick 开头被拦截，不再多发 Header
            _passiveListening = false;
            _pendingResponses.Clear();
            foreach (var s in _slots) if (s != null) s.HeaderMissing = false; // 停止后清除标红
            _lastFrameCheckMs.Clear();
            if (_useHardwareSchedule) Lin_API.SuspendSchedule(_logicChannel);
            else _timer.Stop();
            RunningChanged?.Invoke(false);
        }

        public void Resume()
        {
            if (_running) return;
            if (_logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count &&
                LinConfig.Channels[_logicChannel - 1].GetHardwareMode() == LinNodeMode.Slave)
            {
                // 纯从机通道：有槽时恢复帧头监控（软件定时器，只读总线），空槽仅保持监听。
                if (_slots.Count == 0) { _passiveListening = true; return; }
                _nextDueMs = NowMs();
                if (!_timer.Start(1, OnTick)) return;
                _running = true;
                RunningChanged?.Invoke(true);
                return;
            }
            if (_useHardwareSchedule)
            {
                if (!Lin_API.ResumeSchedule(_logicChannel)) return;
            }
            else
            {
                _nextDueMs = NowMs();
                if (!_timer.Start(1, OnTick)) return;
            }
            _running = true;
            RunningChanged?.Invoke(true);
        }

        public void Stop()
        {
            Suspend();
            foreach (var s in _slots) s.Counter = 0;
            _cursor = 0;
            _pendingResponses.Clear();
        }

        private void OnTick()
        {
            if (!_running) return; // 已暂停/停止：已派发的回调直接放弃
            long now = NowMs(); // 64 位无回绕
            if (now < _nextDueMs) return;
            CheckPendingResponses(Lin_API.SessionMs);

            // 槽快照 + 锁：与 UI 线程 Add/Remove/MoveSlot 并发安全（快照后 List 修改不影响本次遍历）
            List<LinScheduleSlot> snapshot;
            lock (_slots) { snapshot = new List<LinScheduleSlot>(_slots); }
            if (snapshot.Count == 0) return;

            // 找一个启用槽（跳过禁用；空表则停——不在此回调内 timeKillEvent（文档警告危险模式），
            // 仅置状态，由下次 Start/Suspend 清理定时器）
            int start = _cursor % snapshot.Count;
            int idx = start;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[idx].Enabled) break;
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

            bool dispatched;
            long sendStartMs = Lin_API.SessionMs;
            // 从机监控（Slave 槽，非主从一体驱动）：本机不发送，只检查外部 Master 是否
            // 在该周期内发出对应帧头——有帧头则本周期正常，缺失则槽标红（UI 红灯报错），
            // 收到帧头后由下一周期自动清除。Slave 槽的自动应答仍由连接时配置的
            // RESPONSE_ENABLE 完成，与监控检查互不干扰。
            if (slot.TransmitType == LinTransmitType.Slave && !Lin_API.IsMasterDriven(_logicChannel))
            {
                long since;
                if (!_lastFrameCheckMs.TryGetValue(slot.Pid, out since)) since = sendStartMs;
                slot.HeaderMissing = !Lin_API.HasFrameActivitySince(_logicChannel, slot.Pid, since);
                _lastFrameCheckMs[slot.Pid] = sendStartMs;
                slot.Counter++; // 监控周期计数：调度表里该帧“显示发送”
                dispatched = true;
                LinDebugLog.Write("[SCH] tick ch=" + _logicChannel + " idx=" + idx + " pid=0x" + slot.Pid.ToString("X2") +
                    " slotMs=" + slot.SlotMs + " type=Slave 从机监控 headerMissing=" + slot.HeaderMissing);
                SlotChanged?.Invoke(idx);
                return;
            }
            dispatched = Lin_API.LinSendScheduleSlot(_logicChannel, slot);
            // HeaderOnly 槽和主从一体驱动下的 Slave 槽都发出 Header，需要等待响应；
            // 前者超时按真实无应答处理，后者超时注入仿真响应（软件调度回退）。
            bool waitsResponse = slot.TransmitType == LinTransmitType.HeaderOnly ||
                (slot.TransmitType == LinTransmitType.Slave && Lin_API.IsMasterDriven(_logicChannel));
            if (dispatched && waitsResponse)
            {
                // 同一响应槽可能在超时窗口内重复调度，保留最早一次等待，避免反复重置超时。
                if (!_pendingResponses.ContainsKey(slot.Pid))
                {
                    long timeoutMs = System.Math.Max(ResponseTimeoutFloorMs, (long)slot.SlotMs * 2);
                    _pendingResponses[slot.Pid] = new PendingResponse
                    {
                        SentMs = sendStartMs,
                        DeadlineMs = sendStartMs + timeoutMs,
                        Type = slot.TransmitType,
                    };
                }
            }
            // Slave 槽在主从一体驱动下由本机发 Header 驱动；发送失败也不记为一个已完成周期。
            if (dispatched && slot.TransmitType != LinTransmitType.Slave) slot.Counter++;
            LinDebugLog.Write("[SCH] tick ch=" + _logicChannel + " idx=" + idx + " pid=0x" + slot.Pid.ToString("X2") + " slotMs=" + slot.SlotMs + " type=" + slot.TransmitType + " dispatched=" + dispatched);
            SlotChanged?.Invoke(idx);
        }

        /// <summary>
        /// 响应槽超时兜底：真实 Rx（包括驱动上报的硬件错误帧）优先作为结果；
        /// 只有窗口到期且没有任何 Rx 时，才生成一条记录。主从一体驱动下的 Slave 槽
        /// 注入带数据的仿真响应（软件调度回退）；其余槽按真实无应答记录错误帧。
        /// </summary>
        private void CheckPendingResponses(long nowMs)
        {
            if (_pendingResponses.Count == 0) return;
            var completed = new List<byte>();
            var timedOut = new List<byte>();
            foreach (var kv in _pendingResponses)
            {
                PendingResponse pending = kv.Value;
                if (nowMs < pending.DeadlineMs) continue;
                if (Lin_API.HasRxSince(_logicChannel, kv.Key, pending.SentMs)) completed.Add(kv.Key);
                else timedOut.Add(kv.Key);
            }

            foreach (byte pid in completed) _pendingResponses.Remove(pid);
            foreach (byte pid in timedOut)
            {
                PendingResponse pending = _pendingResponses[pid];
                _pendingResponses.Remove(pid);
                if (pending.Type == LinTransmitType.Slave && Lin_API.IsMasterDriven(_logicChannel))
                    Lin_API.SimulateSlaveResponse(_logicChannel, pid);
                else
                    Lin_API.InjectNoResponse(_logicChannel, pid);
            }
        }
    }
}
