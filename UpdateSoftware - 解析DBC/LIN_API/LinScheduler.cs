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
        private readonly bool _useHardwareSchedule; // PEAK=true（硬件调度），Vector=false（软件调度）
        private readonly List<LinScheduleSlot> _slots = new List<LinScheduleSlot>();
        private readonly LinWinmmTimer _timer = new LinWinmmTimer();
        private volatile bool _running;
        private int _cursor;
        private long _nextDueMs;
        private long _tickCount;
        private string _lastError = "";
        /// <summary>响应超时错误注入节流：pid → 上次注入会话毫秒（防每槽周期刷屏，窗口内一条）</summary>
        private readonly Dictionary<byte, long> _respErrStamp = new Dictionary<byte, long>();

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
            _lastError = "";
            if (_useHardwareSchedule)
            {
                _lastError = Lin_API.StartSchedule(_logicChannel, _slots);
                if (_lastError.Length > 0) return false;
            }
            else
            {
                // 软件调度（PEAK 本环境硬件调度表 errUnknown，Vector 本就软件）：主节点模式必须先
                // 预置主节点发布帧数据，否则 LinSendScheduleFrame 无缓存退化为 Header-only——
                // 主节点发布帧缺数据槽违反 LIN 协议，从节点判定错误帧，总线无有效应答（实测日志）。
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
        }

        private void OnTick()
        {
            if (!_running) return; // 已暂停/停止：已派发的回调直接放弃
            long now = NowMs(); // 64 位无回绕
            if (now < _nextDueMs) return;

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

            // 主节点模式：软件调度发 Header/帧（有缓存数据发完整帧，否则发 Header-only 等从节点应答）。
            // 从节点模式：LIN 从节点无权主动发 Header（协议约束），调度仅作响应监控——检测外部主节点
            // 周期内本机/总线应答是否发生（CheckResponseTimeout）；若也从节点模式发帧，会与真实主节点
            // 抢总线 → 数据冲突、全部校验和错误（实测症状：接入外部主节点后所有报文报错误帧）。
            bool master = _logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count &&
                          LinConfig.Channels[_logicChannel - 1].Mode == LinNodeMode.Master;
            bool sent = false;
            if (master)
            {
                if (!Lin_API.LinSendScheduleFrame(_logicChannel, slot.Pid))
                    sent = Lin_API.LinSendHeader(_logicChannel, slot.Pid);
                else
                    sent = true;
            }
            CheckResponseTimeout(slot); // 发送后：窗口内无该帧活动 → 注入无应答错误（主/从节点模式均适用）
            slot.Counter++;
            LinDebugLog.Write("[SCH] tick ch=" + _logicChannel + " idx=" + idx + " pid=0x" + slot.Pid.ToString("X2") + " slotMs=" + slot.SlotMs + " master=" + master + " sent=" + sent);
            SlotChanged?.Invoke(idx);
        }

        /// <summary>
        /// 响应超时检测：期望该帧在窗口内出现总线活动（从节点 = 外部主节点发 Header 触发应答；
        /// 主节点 = 硬件回报应答/错误帧）。窗口内无活动（无帧头/无应答且硬件未报错）→ 注入
        /// NoResponse 错误帧 → 报文列表 err 列亮红灯。节流：同 PID 每超时窗口最多注入一条。
        /// 硬件已报错误帧（有活动）时不再注入，避免重复。
        /// 主节点发布帧（LDF Publisher==MasterName）跳过：主节点模式发完整帧后从节点只接收
        /// 不应答，无活动是正常协议行为，不注入。
        /// </summary>
        private void CheckResponseTimeout(LinScheduleSlot slot)
        {
            // 未连接时停止注入：断开后 winmm 定时器仍在跑，继续注入会在列表刷错误帧，
            // 且无连接时注入无意义（实测：断开后持续注入直到重连）。
            if (!Lin_API.IsConnected(_logicChannel))
            {
                LinDebugLog.Write("[SCH] timeoutCheck ch=" + _logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " → 未连接，跳过注入");
                return;
            }
            // 从节点模式不注入：本机未发送任何 Header（sent=False），无应答不是本机帧的错误——
            // 注入会在地总线空闲（无外部主节点驱动）时对每个 PID 每 500ms 刷一条假错误帧，
            // 用户看到"从节点模式全是错误帧"（实测：无总线流量时 1223 条注入帧、硬件 0 错误帧）。
            // 外部主节点真实驱动时，硬件会回报 SlaveNOtResponding/校验错误帧，UI 仍能看到真实错误。
            if (_logicChannel >= 1 && _logicChannel <= LinConfig.Channels.Count
                && LinConfig.Channels[_logicChannel - 1].Mode == LinNodeMode.Slave)
            {
                long snow = Lin_API.SessionMs;
                long slast;
                if (_respErrStamp.TryGetValue(slot.Pid, out slast) && snow - slast < 5000) return; // 每 PID 每 5s 记一条说明
                _respErrStamp[slot.Pid] = snow;
                LinDebugLog.Write("[SCH] timeoutCheck ch=" + _logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " → 从节点模式，不注入（本机未发 Header）");
                return;
            }
            if (Lin_API.IsMasterPublisherFrame(_logicChannel, slot.Pid))
            {
                LinDebugLog.Write("[SCH] timeoutCheck ch=" + _logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " → 主节点发布帧，跳过（不期待应答）");
                return;
            }
            long timeoutMs = Math.Max(500, (long)slot.SlotMs * 2);
            bool hasActivity = Lin_API.HasPidActivity(_logicChannel, slot.Pid, timeoutMs);
            if (hasActivity) return; // 窗口内有活动（含硬件错误帧）
            long now = Lin_API.SessionMs;
            long last;
            if (_respErrStamp.TryGetValue(slot.Pid, out last) && now - last < timeoutMs)
            {
                LinDebugLog.Write("[SCH] timeoutCheck ch=" + _logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " windowMs=" + timeoutMs + " activity=false → 冷却中，跳过");
                return; // 冷却中
            }
            _respErrStamp[slot.Pid] = now;
            LinDebugLog.Write("[SCH] timeoutCheck ch=" + _logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " windowMs=" + timeoutMs + " activity=false → 注入 NoResponse");
            Lin_API.InjectNoResponse(_logicChannel, slot.Pid);
        }
    }
}
