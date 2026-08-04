using PCAN_Client.CAN_Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCAN_Client.ScriptEngine
{
    /// <summary>引擎运行状态</summary>
    public enum RunnerState { Stopped, Running, Paused }

    public enum LogKind { Info, Send, Condition, Variable, Error }

    public class LogEntry
    {
        public DateTime Time;
        public LogKind Kind;
        public string Text;
    }

    /// <summary>
    /// 脚本发送执行引擎:挂载全局1ms调度器(AddAction "CAN_Script")常驻,
    /// 解释执行步骤树(执行栈+状态机),支持延时/等待条件/循环/IF/变量/触发启动。
    /// Tick 运行在 winmm 定时器线程——禁止直接操作UI;UI 通过轮询(GetXxx)与日志队列取数。
    /// </summary>
    public class ScriptRunner : IDisposable
    {
        // ===================== 帧统计(订阅 CAN_API.RawFrameReceived) =====================
        private class FrameInfo
        {
            public ulong Count;
            public byte[] LastData;
            public ushort LastLen;
        }
        private readonly Dictionary<long, FrameInfo> _frameStats = new Dictionary<long, FrameInfo>();
        private readonly object _statsLock = new object();
        private static long FrameKey(uint id, byte channel) => ((long)channel << 32) | id;

        // ===================== 运行期状态 =====================
        private readonly object _lock = new object();
        private volatile CanScript _script; // OnRawFrame(接收线程)无锁读引用,volatile保证可见性
        private RunnerState _state = RunnerState.Stopped;
        private readonly Dictionary<string, double> _vars = new Dictionary<string, double>();
        private readonly Stopwatch _sw = new Stopwatch();
        private long _pauseStart;
        private bool _singleStep;
        private readonly Random _rand = new Random();

        /// <summary>当前正在执行/等待的步骤(UI高亮轮询用,可能为null)</summary>
        public ScriptStep CurrentStep { get; private set; }
        /// <summary>当前脚本循环第几轮(1-based)</summary>
        public int CurrentRunLoop { get; private set; }
        public RunnerState State => _state;
        public string ScriptName => _script?.Name ?? "";

        // 等待子状态
        private enum WaitKind { None, Delay, Condition, SendRepeat }
        private WaitKind _waitKind = WaitKind.None;
        private long _waitUntil;          // Delay到期时刻
        private long _waitDeadline;       // Condition超时时刻
        private ScriptStep _waitStep;     // Condition/SendRepeat 关联步骤
        private int _sendRemaining;       // 连发剩余帧数
        private long _sendNextTime;       // 连发下一帧时刻

        // ===================== 执行栈 =====================
        private class ExecFrame
        {
            public List<ScriptStep> Steps;
            public int Index;
            public int Iteration;      // 循环块当前轮次(1-based)
            public ScriptStep LoopStep; // 所属循环块步骤(普通列表帧为null)
        }
        private readonly Stack<ExecFrame> _stack = new Stack<ExecFrame>();
        private int _runLoopRemaining;   // 脚本整体剩余轮数;-1=无限

        // 条件信号引用缓存(Load时解析,避免每tick查字典)
        private readonly Dictionary<ScriptOperand, Signal> _signalCache = new Dictionary<ScriptOperand, Signal>();
        private readonly HashSet<string> _nullSignalWarned = new HashSet<string>();

        // ===================== 日志队列(UI 100ms批量拉取) =====================
        private readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private const int MaxLogEntries = 5000;
        private int _logCount;

        /// <summary>OnCondition触发锁存:条件曾成立,需先变 false 才允许再次触发(防止停止后1ms内被自动重启/结束后立即重启)</summary>
        private bool _trigLatch;
        /// <summary>已释放标志:Dispose后Tick首行闸口,防止winmm残留回调在窗口销毁后触发Run/碰任何外部引用</summary>
        private volatile bool _disposed;

        public ScriptRunner()
        {
            CAN_API.CAN_API.RawFrameReceived += OnRawFrame;
            Main.main.multiMessageCANScheduler.AddAction(Tick, "CAN_Script", 1);
        }

        public void Dispose()
        {
            Stop("引擎释放");
            _disposed = true; // 先置闸口:此后残留Tick立即返回
            Main.main.multiMessageCANScheduler.RemoveAction("CAN_Script");
            CAN_API.CAN_API.RawFrameReceived -= OnRawFrame;
        }

        // ===================== 日志 =====================
        private void Log(LogKind kind, string text)
        {
            _logQueue.Enqueue(new LogEntry { Time = DateTime.Now, Kind = kind, Text = text });
            if (System.Threading.Interlocked.Increment(ref _logCount) > MaxLogEntries)
            {
                if (_logQueue.TryDequeue(out _)) System.Threading.Interlocked.Decrement(ref _logCount);
            }
        }

        public bool TryDequeueLog(out LogEntry entry)
        {
            bool ok = _logQueue.TryDequeue(out entry);
            if (ok) System.Threading.Interlocked.Decrement(ref _logCount);
            return ok;
        }

        /// <summary>变量快照(UI变量监视表轮询用)</summary>
        public Dictionary<string, double> GetVarsSnapshot()
        {
            lock (_lock) return new Dictionary<string, double>(_vars);
        }

        /// <summary>等待状态描述(UI状态栏用)</summary>
        public string WaitDescription()
        {
            lock (_lock)
            {
                if (_state == RunnerState.Stopped) return "已停止";
                if (_state == RunnerState.Paused) return "已暂停";
                switch (_waitKind)
                {
                    case WaitKind.Delay: return $"延时中(剩余{Math.Max(0, _waitUntil - _sw.ElapsedMilliseconds)}ms)";
                    case WaitKind.Condition: return $"等待条件(剩余{Math.Max(0, _waitDeadline - _sw.ElapsedMilliseconds)}ms)";
                    case WaitKind.SendRepeat: return $"连发中(剩余{_sendRemaining}帧)";
                    default: return "执行中";
                }
            }
        }

        // ===================== 帧统计回调(接收线程) =====================
        private void OnRawFrame(uint id, ushort len, byte[] data, byte channel, bool isTx)
        {
            if (isTx) return; // 本端发送回灌帧不计入"接收"统计
            lock (_statsLock)
            {
                long key = FrameKey(id, channel);
                if (!_frameStats.TryGetValue(key, out FrameInfo info))
                {
                    info = new FrameInfo();
                    _frameStats[key] = info;
                }
                info.Count++;
                info.LastLen = len;
                if (info.LastData == null || info.LastData.Length < len) info.LastData = new byte[Math.Max(8, (int)len)];
                Array.Copy(data, info.LastData, Math.Min(len, data.Length));
            }
            // 触发启动:收到指定帧(边沿触发,天然防抖)
            if (_state == RunnerState.Stopped && _script != null &&
                _script.Trigger == TriggerMode.OnFrameId &&
                id == _script.TriggerFrameId &&
                (_script.TriggerChannel == 0 || _script.TriggerChannel == channel))
            {
                Log(LogKind.Info, $"触发:收到 0x{id:X3} CH{channel},自动启动脚本");
                Run();
            }
        }

        // ===================== 载入/控制 =====================
        /// <summary>载入脚本(重建运行期报文+条件信号引用缓存);运行/停止后重复Load无副作用</summary>
        public bool Load(CanScript script, out string error)
        {
            error = null;
            lock (_lock)
            {
                if (_state != RunnerState.Stopped)
                {
                    error = "脚本正在运行,请先停止";
                    return false;
                }
                _script = script;
                _signalCache.Clear();
                _nullSignalWarned.Clear();
                ScriptMessageHelper.RelinkAll(script);
                RebuildSignalCache(script);
                // 校验发送步骤报文有效性
                foreach (var s in AllSteps(script.Steps).Where(x => x.Type == ScriptStepType.SendMessage))
                {
                    if (s.Msg?.RuntimeMessage == null)
                    {
                        error = $"发送步骤报文无效({s.Msg?.Name ?? "未配置"}),请检查配置";
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// 编辑期轻量同步:仅更新脚本引用+重建条件信号缓存(微秒级),
        /// 不做报文重链接与校验——供UI每次编辑后武装触发启动,避免Load的RelinkAll全量克隆报文信号造成键入卡顿
        /// </summary>
        public void AttachScript(CanScript script)
        {
            lock (_lock)
            {
                if (_state != RunnerState.Stopped) return;
                _script = script;
                RebuildSignalCache(script);
            }
        }

        /// <summary>重建条件信号引用缓存(遍历全脚本条件,按通道+ID查DBC)</summary>
        private void RebuildSignalCache(CanScript script)
        {
            _signalCache.Clear();
            if (script == null) return;
            var allConds = AllSteps(script.Steps).SelectMany(s => s.Conditions ?? Enumerable.Empty<ScriptCondition>())
                .Concat(script.TriggerConditions ?? Enumerable.Empty<ScriptCondition>());
            foreach (var cond in allConds)
            {
                CacheSignal(cond.Left);
                CacheSignal(cond.Right);
            }
        }

        private void CacheSignal(ScriptOperand op)
        {
            if (op == null || op.Kind != OperandKind.Signal || _signalCache.ContainsKey(op)) return;
            var msg = ScriptMessageHelper.FindDbcMessage(op.Channel, op.MessageId);
            var sig = msg?.signals?.FirstOrDefault(s => s.signalName == op.SignalName);
            _signalCache[op] = sig; // null=未找到(评估时条件为假并告警一次)
        }

        private static IEnumerable<ScriptStep> AllSteps(IEnumerable<ScriptStep> steps)
        {
            foreach (var s in steps)
            {
                yield return s;
                foreach (var c in AllSteps(s.Children)) yield return c;
            }
        }

        /// <summary>开始运行(重置变量/帧统计/循环计数)</summary>
        public void Run()
        {
            lock (_lock)
            {
                if (_disposed || _script == null || _state != RunnerState.Stopped) return;
                if (_script.TotalStepCount() == 0)
                {
                    Log(LogKind.Error, "脚本为空,无法运行");
                    return;
                }
                _vars.Clear();
                lock (_statsLock) _frameStats.Clear();
                _stack.Clear();
                _waitKind = WaitKind.None;
                CurrentStep = null;
                _runLoopRemaining = _script.RunLoopCount <= 0 ? -1 : _script.RunLoopCount;
                CurrentRunLoop = 1;
                _stack.Push(new ExecFrame { Steps = _script.Steps, Index = 0 });
                _sw.Restart();
                _state = RunnerState.Running;
                Log(LogKind.Info, $"▶ 脚本[{_script.Name}]开始运行" +
                    (_runLoopRemaining < 0 ? "(无限循环)" : _runLoopRemaining > 1 ? $"(共{_runLoopRemaining}轮)" : ""));
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (_state != RunnerState.Running) return;
                _state = RunnerState.Paused;
                _pauseStart = _sw.ElapsedMilliseconds;
                Log(LogKind.Info, "⏸ 已暂停");
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (_state != RunnerState.Paused) return;
                long delta = _sw.ElapsedMilliseconds - _pauseStart;
                _waitUntil += delta;
                _waitDeadline += delta;
                _sendNextTime += delta;
                _state = RunnerState.Running;
                Log(LogKind.Info, "▶ 继续运行");
            }
        }

        /// <summary>单步:Stopped时先装载并暂停;Paused时执行一个步骤后再次暂停</summary>
        public void StepOnce()
        {
            lock (_lock)
            {
                if (_disposed || _script == null) return;
                if (_state == RunnerState.Stopped)
                {
                    Run();
                    if (_state == RunnerState.Running) _state = RunnerState.Paused;
                    Log(LogKind.Info, "单步模式:已装载,再点[单步]逐条执行");
                    return;
                }
                if (_state != RunnerState.Paused) return;
                _singleStep = true;
                long delta = _sw.ElapsedMilliseconds - _pauseStart;
                _waitUntil += delta;
                _waitDeadline += delta;
                _sendNextTime += delta;
                _state = RunnerState.Running;
            }
        }

        public void Stop(string reason = null)
        {
            lock (_lock)
            {
                if (_state == RunnerState.Stopped) return;
                _state = RunnerState.Stopped;
                _stack.Clear();
                _waitKind = WaitKind.None;
                _singleStep = false;
                CurrentStep = null;
                _trigLatch = true; // 手动停止后抑制OnCondition触发立即重启(条件解除后自动清除)
                Log(LogKind.Info, "⏹ 脚本停止" + (reason != null ? $":{reason}" : ""));
            }
        }

        // ===================== 1ms Tick(winmm线程) =====================
        private void Tick()
        {
            if (_disposed) return; // Dispose闸口:残留回调立即返回
            try
            {
                lock (_lock)
                {
                    if (_state == RunnerState.Stopped)
                    {
                        EvalConditionTrigger();
                        return;
                    }
                    if (_state != RunnerState.Running) return;

                    long now = _sw.ElapsedMilliseconds;
                    // 处理等待子状态
                    switch (_waitKind)
                    {
                        case WaitKind.Delay:
                            if (now < _waitUntil) return;
                            _waitKind = WaitKind.None;
                            Log(LogKind.Info, $"延时结束");
                            if (_singleStep) { PauseLocked(); return; }
                            break;
                        case WaitKind.Condition:
                            if (EvalConditions(_waitStep.Conditions, _waitStep.Logic))
                            {
                                Log(LogKind.Condition, $"等待条件满足:{ScriptStep.CondSummary(_waitStep.Conditions, _waitStep.Logic)}");
                                _waitKind = WaitKind.None;
                            }
                            else if (now >= _waitDeadline)
                            {
                                if (_waitStep.OnTimeout == TimeoutAction.StopScript)
                                {
                                    Log(LogKind.Error, $"等待超时({_waitStep.TimeoutMs}ms),脚本停止:{ScriptStep.CondSummary(_waitStep.Conditions, _waitStep.Logic)}");
                                    StopLocked("等待超时");
                                    return;
                                }
                                Log(LogKind.Condition, $"等待超时,继续下一步:{ScriptStep.CondSummary(_waitStep.Conditions, _waitStep.Logic)}");
                                _waitKind = WaitKind.None;
                            }
                            else return; // 继续等待
                            if (_singleStep) { PauseLocked(); return; }
                            break;
                        case WaitKind.SendRepeat:
                            while (_sendRemaining > 0 && now >= _sendNextTime)
                            {
                                if (!DoSendFrame(_waitStep)) return; // 发送失败已Stop
                                _sendRemaining--;
                                _sendNextTime += Math.Max(1, _waitStep.RepeatIntervalMs);
                            }
                            if (_sendRemaining > 0) return;
                            _waitKind = WaitKind.None;
                            if (_singleStep) { PauseLocked(); return; }
                            break;
                    }
                    if (_waitKind != WaitKind.None) return;
                    RunExecutionLoop();
                }
            }
            catch (Exception ex)
            {
                Log(LogKind.Error, $"引擎异常:{ex.Message}");
                try { Stop("引擎异常"); } catch { }
            }
        }

        private void PauseLocked()
        {
            _singleStep = false;
            _state = RunnerState.Paused;
            _pauseStart = _sw.ElapsedMilliseconds;
            Log(LogKind.Info, "⏸ 单步暂停");
        }

        private void StopLocked(string reason)
        {
            _state = RunnerState.Stopped;
            _stack.Clear();
            _waitKind = WaitKind.None;
            _singleStep = false;
            CurrentStep = null;
            _trigLatch = true; // 任何停止后都需触发条件先解除再边沿触发(与手动Stop语义一致)
            Log(LogKind.Info, "⏹ 脚本停止" + (reason != null ? $":{reason}" : ""));
        }

        // ===================== 执行流 =====================
        private void RunExecutionLoop()
        {
            int budget = 500; // 单tick步数上限,防止无等待的无限循环卡死定时器线程
            while (budget-- > 0 && _state == RunnerState.Running && _waitKind == WaitKind.None)
            {
                if (_stack.Count == 0)
                {
                    // 一轮脚本执行完:整体循环判断
                    if (_runLoopRemaining < 0 || --_runLoopRemaining > 0)
                    {
                        CurrentRunLoop++;
                        _stack.Push(new ExecFrame { Steps = _script.Steps, Index = 0 });
                        Log(LogKind.Info, $"↻ 开始第 {CurrentRunLoop} 轮");
                        continue;
                    }
                    Log(LogKind.Info, $"✔ 脚本[{_script.Name}]执行完成");
                    StopLocked(null);
                    return;
                }

                var frame = _stack.Peek();
                if (frame.Index >= frame.Steps.Count)
                {
                    // 列表帧结束:循环块回卷判断
                    _stack.Pop();
                    if (frame.LoopStep != null)
                    {
                        var loop = frame.LoopStep;
                        bool again;
                        if (loop.Loop == LoopMode.Infinite) again = true;
                        else if (loop.Loop == LoopMode.Count) again = frame.Iteration < loop.LoopCount;
                        else again = loop.Conditions != null && loop.Conditions.Count > 0 &&
                                     EvalConditions(loop.Conditions, loop.Logic); // 空条件恒假,防死循环
                        if (again && _state == RunnerState.Running)
                        {
                            _stack.Push(new ExecFrame
                            {
                                Steps = loop.Children,
                                Index = 0,
                                Iteration = frame.Iteration + 1,
                                LoopStep = loop
                            });
                        }
                    }
                    continue;
                }

                var step = frame.Steps[frame.Index++];
                CurrentStep = step;
                ExecStep(step);
                if (_singleStep && _waitKind == WaitKind.None && _state == RunnerState.Running)
                {
                    PauseLocked();
                    return;
                }
            }
        }

        private void ExecStep(ScriptStep step)
        {
            switch (step.Type)
            {
                case ScriptStepType.SendMessage:
                    if (!DoSendFrame(step)) return; // 失败已Stop
                    if (step.RepeatCount > 1)
                    {
                        _sendRemaining = step.RepeatCount - 1;
                        _sendNextTime = _sw.ElapsedMilliseconds + Math.Max(1, step.RepeatIntervalMs);
                        _waitKind = WaitKind.SendRepeat;
                        _waitStep = step;
                    }
                    break;

                case ScriptStepType.Delay:
                    _waitUntil = _sw.ElapsedMilliseconds + Math.Max(1, step.DelayMs);
                    _waitKind = WaitKind.Delay;
                    _waitStep = step;
                    break;

                case ScriptStepType.WaitCondition:
                    if (step.Conditions == null || step.Conditions.Count == 0)
                    {
                        Log(LogKind.Error, "等待步骤未配置条件,脚本停止");
                        StopLocked("条件为空");
                        return;
                    }
                    if (!EvalConditions(step.Conditions, step.Logic))
                    {
                        _waitDeadline = _sw.ElapsedMilliseconds + Math.Max(1, step.TimeoutMs);
                        _waitKind = WaitKind.Condition;
                        _waitStep = step;
                        Log(LogKind.Condition, $"等待条件:{ScriptStep.CondSummary(step.Conditions, step.Logic)}");
                    }
                    else
                    {
                        Log(LogKind.Condition, $"条件已满足(无需等待):{ScriptStep.CondSummary(step.Conditions, step.Logic)}");
                    }
                    break;

                case ScriptStepType.IfBlock:
                    if (EvalConditions(step.Conditions, step.Logic))
                    {
                        Log(LogKind.Condition, $"判断成立:{ScriptStep.CondSummary(step.Conditions, step.Logic)}");
                        _stack.Push(new ExecFrame { Steps = step.Children, Index = 0 });
                    }
                    else
                    {
                        Log(LogKind.Condition, $"判断不成立,跳过:{ScriptStep.CondSummary(step.Conditions, step.Logic)}");
                    }
                    break;

                case ScriptStepType.LoopBlock:
                    if (step.Children.Count == 0)
                    {
                        Log(LogKind.Error, "循环块无子步骤,已跳过");
                        break;
                    }
                    if (step.Loop == LoopMode.Condition && (step.Conditions == null || step.Conditions.Count == 0))
                    {
                        // 空条件在EvalConditions中恒真,此处拦截防死循环空转
                        Log(LogKind.Error, "条件循环未配置条件,已跳过(防止死循环)");
                        break;
                    }
                    bool enter;
                    if (step.Loop == LoopMode.Infinite) enter = true;
                    else if (step.Loop == LoopMode.Count) enter = step.LoopCount > 0;
                    else enter = EvalConditions(step.Conditions, step.Logic);
                    if (enter)
                    {
                        _stack.Push(new ExecFrame { Steps = step.Children, Index = 0, Iteration = 1, LoopStep = step });
                        if (step.Loop == LoopMode.Count)
                            Log(LogKind.Info, $"循环开始(共{step.LoopCount}次)");
                    }
                    break;

                case ScriptStepType.SetVariable:
                    ExecSetVariable(step);
                    break;

                case ScriptStepType.LogMessage:
                    Log(LogKind.Info, "📝 " + InterpolateVars(step.Text));
                    break;

                case ScriptStepType.StopScript:
                    Log(LogKind.Info, "脚本执行到[停止脚本]步骤");
                    StopLocked("主动停止");
                    break;
            }
        }

        // ===================== 发送 =====================
        private bool DoSendFrame(ScriptStep step)
        {
            var snap = step.Msg;
            var msg = snap?.RuntimeMessage;
            if (msg == null)
            {
                Log(LogKind.Error, $"发送步骤报文未就绪({snap?.Name ?? "未配置"}),脚本停止");
                StopLocked("报文未就绪");
                return false;
            }

            // 硬件连接检查(与CAN_API.CanTransmit路由判断一致)
            if (!IsChannelConnected(snap.Channel))
            {
                Log(LogKind.Error, $"通道 CH{snap.Channel} 硬件未连接,发送失败,脚本停止");
                StopLocked("硬件未连接");
                return false;
            }

            // 1. 应用$变量表达式
            if (snap.SignalExprs != null)
            {
                foreach (var kv in snap.SignalExprs)
                {
                    if (!kv.Value.StartsWith("$")) continue;
                    var sig = msg.signals.FirstOrDefault(s => s.signalName == kv.Key);
                    if (sig == null) continue;
                    string varName = kv.Value.Substring(1);
                    if (!_vars.TryGetValue(varName, out double v))
                    {
                        Log(LogKind.Error, $"变量 ${varName} 未定义(信号{kv.Key}),按0处理");
                        v = 0;
                    }
                    sig.cmdValue = v;
                    msg.updateFlag = true;
                }
            }

            // 2. 递增填充
            if (snap.Fill == FillMode.SignalIncrement && msg.signals.Count > 0)
            {
                var sig = msg.signals.FirstOrDefault(s => s.signalName == snap.IncrementSignal);
                if (sig != null)
                {
                    sig.cmdValue = snap.IncrementCurrent;
                    msg.updateFlag = true;
                    snap.IncrementCurrent += snap.IncrementStep;
                    bool over = snap.IncrementStep >= 0
                        ? snap.IncrementCurrent > snap.IncrementMax
                        : snap.IncrementCurrent < snap.IncrementMax;
                    if (over) snap.IncrementCurrent = snap.IncrementStart;
                }
            }

            // 3. 随机填充:先手动编码(防SendCanMessage重编码覆盖),再覆盖随机字节
            if (snap.Fill == FillMode.RandomBytes)
            {
                if (msg.signals.Count > 0 && (msg.updateFlag || msg.sendBuf == null))
                {
                    try
                    {
                        msg.sendBuf = CanMessageBuilder.EncodeSignals(msg.signals, Math.Max(8, (int)msg.messageSize));
                    }
                    catch (Exception ex)
                    {
                        Log(LogKind.Error, $"信号编码失败:{ex.Message}");
                    }
                }
                if (msg.sendBuf == null) msg.sendBuf = new byte[Math.Max(8, (int)msg.messageSize)];
                int from = Math.Max(0, snap.RandomFrom);
                int to = Math.Min(snap.RandomTo, msg.sendBuf.Length - 1);
                for (int i = from; i <= to; i++) msg.sendBuf[i] = (byte)_rand.Next(256);
                msg.updateFlag = false; // 随机字节不被信号编码覆盖(CRC仍由SendCanMessage逐帧重算)
            }

            // 4. RollingCounter滚动(与周期调度器一致)
            msg.aliveCount = (msg.aliveCount + 1) & 0x0F;

            // 5. 编码+CRC+发送(复用现有链路);失败(硬件错误/未连接)记错误并停止
            try
            {
                if (!BaseParamter.dbcHelper.SendCanMessage(msg, snap.Channel))
                {
                    Log(LogKind.Error, $"发送失败 0x{msg.messgeId:X3} CH{snap.Channel}(硬件发送错误),脚本停止");
                    StopLocked("发送失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log(LogKind.Error, $"发送异常 0x{msg.messgeId:X3}:{ex.Message},脚本停止");
                StopLocked("发送异常");
                return false;
            }
            Log(LogKind.Send, $"发送 0x{msg.messgeId:X3} [{FormatHex(msg.sendBuf)}] CH{snap.Channel}");
            return true;
        }

        private static bool IsChannelConnected(byte logicChannel)
        {
            string hwType = BaseParamter.GetEffectiveHwType(logicChannel - 1);
            if (hwType == BaseParamter.HwTypePcan) return Main.pcanOpenFlag;
            if (hwType == BaseParamter.HwTypeCanoe) return Main.canoeOpenFlag;
            return Main.pcanOpenFlag || Main.canoeOpenFlag; // 未指定类型:任一硬件连接即可
        }

        private static string FormatHex(byte[] buf)
        {
            if (buf == null) return "";
            return string.Join(" ", buf.Select(b => b.ToString("X2")));
        }

        // ===================== 条件评估 =====================
        /// <summary>条件组评估;任一操作数无法取值(未收到帧/信号无定义)时该条件为false</summary>
        private bool EvalConditions(List<ScriptCondition> conds, CondLogicOp logic)
        {
            if (conds == null || conds.Count == 0) return true; // 无条件视为恒真(IfBlock/循环的兜底)
            if (logic == CondLogicOp.And)
            {
                foreach (var c in conds) if (!EvalCondition(c)) return false;
                return true;
            }
            foreach (var c in conds) if (EvalCondition(c)) return true;
            return false;
        }

        private bool EvalCondition(ScriptCondition c)
        {
            double? l = EvalOperand(c.Left);
            double? r = EvalOperand(c.Right);
            if (l == null || r == null) return false;
            const double eps = 1e-9;
            switch (c.Op)
            {
                case ">": return l > r;
                case ">=": return l >= r || Math.Abs(l.Value - r.Value) < eps;
                case "<": return l < r;
                case "<=": return l <= r || Math.Abs(l.Value - r.Value) < eps;
                case "==": return Math.Abs(l.Value - r.Value) < eps;
                case "!=": return Math.Abs(l.Value - r.Value) >= eps;
                default: return false;
            }
        }

        /// <summary>操作数求值;无法取值返回null(条件按false处理)</summary>
        private double? EvalOperand(ScriptOperand op)
        {
            if (op == null) return null;
            switch (op.Kind)
            {
                case OperandKind.Const:
                    return op.ConstValue;
                case OperandKind.Variable:
                    return _vars.TryGetValue(op.VarName, out double v) ? (double?)v : null;
                case OperandKind.Signal:
                    if (!_signalCache.TryGetValue(op, out Signal sig) || sig == null)
                    {
                        WarnNullSignalOnce(op);
                        return null;
                    }
                    return op.UseRawValue ? sig.rawValue : sig.result;
                case OperandKind.FrameCount:
                    lock (_statsLock)
                    {
                        return _frameStats.TryGetValue(FrameKey(op.MessageId, op.Channel), out FrameInfo fi)
                            ? (double?)fi.Count : null;
                    }
                case OperandKind.FrameByte:
                    lock (_statsLock)
                    {
                        if (!_frameStats.TryGetValue(FrameKey(op.MessageId, op.Channel), out FrameInfo fi2) ||
                            fi2.LastData == null || op.ByteIndex >= fi2.LastLen)
                            return null;
                        return fi2.LastData[op.ByteIndex];
                    }
                default:
                    return null;
            }
        }

        private void WarnNullSignalOnce(ScriptOperand op)
        {
            string key = $"CH{op.Channel}.0x{op.MessageId:X}.{op.SignalName}";
            if (_nullSignalWarned.Add(key))
                Log(LogKind.Error, $"条件信号未找到(DBC未加载或已变更):{key},相关条件恒为不成立");
        }

        /// <summary>触发启动:条件型触发在Stopped状态下每tick评估(边沿触发:条件需先变不成立才允许再次触发)</summary>
        private void EvalConditionTrigger()
        {
            if (_script == null || _script.Trigger != TriggerMode.OnCondition) return;
            if (_script.TriggerConditions == null || _script.TriggerConditions.Count == 0) return;
            if (EvalConditions(_script.TriggerConditions, _script.TriggerLogic))
            {
                if (_trigLatch) return; // 条件持续成立不重复触发(防停止后被自动重启)
                _trigLatch = true;
                Log(LogKind.Info, $"触发条件满足:{ScriptStep.CondSummary(_script.TriggerConditions, _script.TriggerLogic)},自动启动脚本");
                Run();
            }
            else
            {
                _trigLatch = false; // 条件解除后允许下一次边沿触发
            }
        }

        // ===================== 变量 =====================
        private void ExecSetVariable(ScriptStep step)
        {
            if (!ScriptOperand.IsValidVarName(step.VarName))
            {
                Log(LogKind.Error, $"变量名非法:\"{step.VarName}\",脚本停止");
                StopLocked("变量名非法");
                return;
            }
            switch (step.VarSource)
            {
                case VarSourceType.Const:
                    _vars[step.VarName] = step.VarConst;
                    Log(LogKind.Variable, $"${step.VarName} = {step.VarConst:G}");
                    break;
                case VarSourceType.Signal:
                    double? v = EvalOperand(step.VarSignal);
                    if (v == null)
                    {
                        Log(LogKind.Error, $"读取信号失败:{step.VarSignal?.DisplayText()},${step.VarName} 按0处理");
                        v = 0;
                    }
                    _vars[step.VarName] = v.Value;
                    Log(LogKind.Variable, $"${step.VarName} = {v.Value:G} (来自 {step.VarSignal?.DisplayText()})");
                    break;
                case VarSourceType.Increment:
                    _vars.TryGetValue(step.VarName, out double cur);
                    _vars[step.VarName] = cur + step.VarIncrementStep;
                    Log(LogKind.Variable, $"${step.VarName} = {_vars[step.VarName]:G} (自增{step.VarIncrementStep:G})");
                    break;
            }
        }

        private static readonly Regex VarPattern = new Regex(@"\$([A-Za-z_]\w*)", RegexOptions.Compiled);

        /// <summary>日志文本$变量插值</summary>
        private string InterpolateVars(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return VarPattern.Replace(text, m =>
                _vars.TryGetValue(m.Groups[1].Value, out double v) ? v.ToString("G") : m.Value);
        }
    }
}
