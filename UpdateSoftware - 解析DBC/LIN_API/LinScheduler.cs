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
        public int SlotMs = 10;
        /// <summary>运行计数（调度循环执行次数）</summary>
        public long Counter;
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
        private struct PendingResponse
        {
            public long SentMs;
            public long DeadlineMs;
        }
        private readonly Dictionary<byte, PendingResponse> _pendingResponses = new Dictionary<byte, PendingResponse>();

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
            _pendingResponses.Clear();
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
            if (_running || _slots.Count == 0) return false;
            if (_logicChannel < 1 || _logicChannel > LinConfig.Channels.Count ||
                LinConfig.Channels[_logicChannel - 1].Mode != LinNodeMode.Master)
            {
                _lastError = "从节点不启动调度表；LIN 从节点只能等待外部主节点 Header 并自动响应";
                return false;
            }
            if (!Lin_API.IsConnected(_logicChannel))
            {
                _lastError = "LIN 通道未连接，不能启动调度表";
                return false;
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
                // 软件调度（PEAK 本环境硬件调度表 errUnknown，Vector 本就软件）：主节点模式先
                // 预置主节点发布帧数据，确保硬件响应槽和界面回显都使用同一份数据。
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
            if (!_running) return;
            _running = false; // 先置位：已派发的 winmm 回调在 OnTick 开头被拦截，不再多发 Header
            _pendingResponses.Clear();
            if (_useHardwareSchedule) Lin_API.SuspendSchedule(_logicChannel);
            else _timer.Stop();
            RunningChanged?.Invoke(false);
        }

        public void Resume()
        {
            if (_running) return;
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

            // 主节点模式：逐槽发送完整发布帧或 Header（等待从节点响应）。
            // 从节点不进入此调度器；它只在外部主节点 Header 到达时由 PLIN 自动响应。
            bool master = _logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count &&
                          LinConfig.Channels[_logicChannel - 1].Mode == LinNodeMode.Master;
            if (!master)
            {
                _running = false;
                _timer.Stop();
                _lastError = "从节点不运行调度表；等待外部主节点 Header";
                RunningChanged?.Invoke(false);
                return;
            }
            bool sent = false;
            // 完整帧发送失败时不能退化为 Header-only：对主节点发布帧会违反 LIN 角色语义。
            long sendStartMs = Lin_API.SessionMs;
            sent = Lin_API.LinSendScheduleFrame(_logicChannel, slot.Pid);
            if (sent && !Lin_API.IsMasterPublisherFrame(_logicChannel, slot.Pid))
            {
                // 同一响应槽可能在超时窗口内重复调度，保留最早一次等待，避免反复重置超时。
                if (!_pendingResponses.ContainsKey(slot.Pid))
                {
                    long timeoutMs = System.Math.Max(ResponseTimeoutFloorMs, (long)slot.SlotMs * 2);
                    _pendingResponses[slot.Pid] = new PendingResponse
                    {
                        SentMs = sendStartMs,
                        DeadlineMs = sendStartMs + timeoutMs,
                    };
                }
            }
            slot.Counter++;
            LinDebugLog.Write("[SCH] tick ch=" + _logicChannel + " idx=" + idx + " pid=0x" + slot.Pid.ToString("X2") + " slotMs=" + slot.SlotMs + " master=" + master + " sent=" + sent);
            SlotChanged?.Invoke(idx);
        }

        /// <summary>
        /// 响应槽超时兜底：真实 Rx（包括驱动上报的硬件错误帧）优先作为结果；
        /// 只有窗口到期且没有任何 Rx 时，才生成一条 Rx/错误·无应答记录。
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
                _pendingResponses.Remove(pid);
                Lin_API.InjectNoResponse(_logicChannel, pid);
            }
        }
    }
}
