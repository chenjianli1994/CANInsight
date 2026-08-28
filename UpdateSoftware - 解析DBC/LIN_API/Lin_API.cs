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
        /// <summary>会话时钟毫秒（测试可注入；生产默认 Stopwatch 会话归零）</summary>
        internal static Func<long> SessionClockMs = () =>
            (long)_sw.ElapsedMilliseconds - (long)(_epochUs / 1000);

        // ==================== 从节点响应活动（调度超时兜底） ====================
        // 只记录真实硬件/Vector Rx，不记录软件 Tx 回显。按通道+PID保存最后一次总线活动，
        // 供主节点调度判断 Header 后是否出现从节点响应或硬件错误事件。
        private static readonly Dictionary<long, long> _lastRxMs = new Dictionary<long, long>();
        private static readonly object _rxActivityLock = new object();

        // ==================== 调度器唯一缓存（方案阶段 1） ====================
        // 同一物理/逻辑 LIN 通道全局只有一个 LinScheduler 实例；窗体关闭/重连/切通道统一释放。
        // 记录实例号、通道、创建/停止原因与活动定时器数量，供“无重复调度”审计。
        // 实例号由 LinScheduler._instanceSeq 提供（阶段 1 审查 F2：删除注册表侧死字段，避免审计误导）
        private static readonly Dictionary<byte, LinScheduler> _schedulers = new Dictionary<byte, LinScheduler>();
        private static readonly object _schedLock = new object();

        /// <summary>当前活动调度器数（诊断/测试：重复开关窗口不得泄漏）</summary>
        internal static int ActiveSchedulerCount
        {
            get { lock (_schedLock) return _schedulers.Count; }
        }

        /// <summary>取通道唯一调度器；不存在则创建（创建即记录实例号与原因）。</summary>
        internal static LinScheduler GetOrCreateScheduler(byte logicChannel, string reason)
        {
            lock (_schedLock)
            {
                LinScheduler sc;
                if (_schedulers.TryGetValue(logicChannel, out sc)) return sc;
                sc = new LinScheduler(logicChannel, false);
                _schedulers[logicChannel] = sc;
                LinDebugLog.Write("[SCH] 创建调度器 ch=" + logicChannel + " instance=" + sc.InstanceId +
                    " reason=" + reason + " active=" + _schedulers.Count);
                return sc;
            }
        }

        /// <summary>释放通道调度器（幂等：未注册/已释放直接返回）。窗体关闭、断开、切通道时调用。</summary>
        internal static void ReleaseScheduler(byte logicChannel, string reason)
        {
            LinScheduler sc;
            lock (_schedLock)
            {
                if (!_schedulers.TryGetValue(logicChannel, out sc)) return;
                _schedulers.Remove(logicChannel);
            }
            LinDebugLog.Write("[SCH] 释放调度器 ch=" + logicChannel + " instance=" + sc.InstanceId +
                " reason=" + reason + " running=" + sc.IsRunning + " timerActive=" + sc.IsTimerActive);
            sc.Dispose();
        }

        /// <summary>类型在线切换后重配运行中的调度器：仅重算运行快照并清除该 PID 在途
        /// pending/冷却（不重启调度循环；若纯 Slave 化导致无活跃槽，由调度器自行回收定时器）。</summary>
        internal static void ReconfigureRunningScheduler(byte logicChannel, string reason)
        {
            LinScheduler sc;
            lock (_schedLock)
            {
                if (!_schedulers.TryGetValue(logicChannel, out sc)) return;
                if (!sc.IsRunning) return;
            }
            LinDebugLog.Write("[SCH] 在线重配调度器 ch=" + logicChannel + " instance=" + sc.InstanceId + " reason=" + reason);
            sc.SyncSnapshots();
        }

        // ==================== 事件 ====================
        /// <summary>新帧（UI 订阅刷新报文表）</summary>
        public static event Action<LinFrameRecord> LinFrameReceived;
        /// <summary>连接状态变化（通道, 已连接, 错误原因）</summary>
        public static event Action<byte, bool, string> ChannelStateChanged;
        /// <summary>链路丢失（通道, 原因）——UI 提示用，自动重连在 OnLinkLost 内进行</summary>
        public static event Action<byte, string> LinkLost;
        /// <summary>链路丢失且自动重连（2 次）全部失败（通道, 最后一次失败原因）——UI 弹窗用</summary>
        public static event Action<byte, string> LinkLostFinal;
        /// <summary>总线事件（通道, "Sleep"/"WakeUp"/"Overrun"）</summary>
        public static event Action<byte, string> BusEvent;

        // ==================== 硬件实例缓存：逻辑通道号 → 适配器（加锁保护，重连线程与 UI 线程并发访问） ====================
        private static readonly Dictionary<byte, PcanLinHardware> _pcan = new Dictionary<byte, PcanLinHardware>();
        private static readonly Dictionary<byte, XlLinHardware> _xl = new Dictionary<byte, XlLinHardware>();
        private static readonly object _hwLock = new object();

        // ==================== 发送状态事件与运行快照（方案 4.2） ====================
        /// <summary>发送状态事件（发送意图/驱动提交/真实总线帧/响应完成/超时/错误/停用；UI 阶段 3 订阅）</summary>
        public static event Action<LinTxEvent> LinTxStateChanged;

        private static readonly Dictionary<long, LinRunSnapshot> _runSnapshots = new Dictionary<long, LinRunSnapshot>();
        private static readonly object _runLock = new object();

        private static long RunKey(byte logicChannel, byte pid)
        {
            return ((long)logicChannel << 8) | pid;
        }

        /// <summary>取/建 (通道, 裸 PID) 运行快照</summary>
        internal static LinRunSnapshot GetOrCreateRunSnapshot(byte logicChannel, byte pid)
        {
            lock (_runLock)
            {
                long k = RunKey(logicChannel, pid);
                LinRunSnapshot s;
                if (!_runSnapshots.TryGetValue(k, out s))
                {
                    s = new LinRunSnapshot { LogicChannel = logicChannel, Pid = pid };
                    _runSnapshots[k] = s;
                }
                return s;
            }
        }
        /// <summary>推进运行快照状态并广播 LinTxStateChanged（事件载荷带裸 PID；UI 据此合并计划行与灯）。
        /// SubmitOk/ResponseWait 清除旧错误；Error 按 errorKind 记录实际错误类型。</summary>
        internal static void NotifyTxState(byte logicChannel, byte pid, LinTransmitType type, LinTxEventKind kind,
            string text = "", LinErrorKind errorKind = LinErrorKind.None)
        {
            var s = GetOrCreateRunSnapshot(logicChannel, pid);
            lock (_runLock)
            {
                s.Type = type;
                switch (kind)
                {
                    case LinTxEventKind.Intent:
                        // 仅新启用（或停用后重新启用）重置状态；已启用项重复同步只刷新 Type，
                        // 不擦除 NoResponse/Error 状态（方案 §3：错误不能被无关操作隐藏）、不虚增 BreakOnly 计数。
                        if (!s.Enabled)
                        {
                            s.Enabled = true;
                            s.Generation++;
                            s.ErrorKind = LinErrorKind.None;
                            s.ErrorText = "";
                            s.LastIntentUs = (long)SessionMs * 1000;
                            // 方案 4.1：Slave 启用 = 已武装等外部 Header；BreakOnly = 明确不支持
                            if (type == LinTransmitType.Slave) s.State = LinRunStateKind.WaitingExternalHeader;
                            else if (type == LinTransmitType.BreakOnly)
                            {
                                s.State = LinRunStateKind.Unsupported;
                                s.ErrorKind = LinErrorKind.Hw;
                                s.ErrorCount++;
                            }
                            else s.State = LinRunStateKind.Armed;
                        }
                        s.Type = type;
                        break;
                    case LinTxEventKind.SubmitOk:
                        // 方案 4.2 错误锁存：普通提交只更新当前周期状态，不清除最近错误
                        // （只有明确成功响应 ResponseComplete / 重新启用 Intent / 用户清除才清除锁存灯）。
                        s.SubmittedCount++;
                        s.LastIntentUs = (long)SessionMs * 1000;
                        s.State = LinRunStateKind.TxSubmitted;
                        break;
                    case LinTxEventKind.SubmitFail:
                        s.State = LinRunStateKind.Error;
                        s.ErrorKind = LinErrorKind.Hw;
                        s.ErrorText = text.Length > 0 ? text : "驱动提交失败";
                        s.ErrorCount++;
                        break;
                    case LinTxEventKind.BusFrame:
                        // 实测周期（方案 4.3）：相同方向相同裸 PID 的相邻真实总线帧间隔；无前帧样本保持 0（UI 显示 --）
                        if (s.LastBusUs != 0)
                            s.MeasuredPeriodMs = ((long)SessionMs * 1000 - s.LastBusUs) / 1000;
                        s.BusFrameCount++;
                        s.LastBusUs = (long)SessionMs * 1000;
                        break;
                    case LinTxEventKind.ResponseWait:
                        // 方案 4.2 错误锁存：等待应答不消除已锁存的 NoResponse/硬件错误
                        s.State = LinRunStateKind.WaitingResponse;
                        break;
                    case LinTxEventKind.ResponseComplete:
                        // 明确成功响应：真实 Rx 完成，允许清除锁存错误（红灯功能性熄灭）
                        s.State = LinRunStateKind.Completed;
                        s.ErrorKind = LinErrorKind.None;
                        s.ErrorText = "";
                        break;
                    case LinTxEventKind.Disabled:
                        // 停用：状态置 Disabled，错误保持锁存（重新启用经 Intent 分支才清除），
                        // 避免停用/取消勾选操作把已发生的 NoResponse/硬件错误静默抹掉。
                        s.Enabled = false;
                        s.State = LinRunStateKind.Disabled;
                        break;
                    case LinTxEventKind.ResponseTimeout:
                        s.State = LinRunStateKind.NoResponse;
                        s.ErrorKind = LinErrorKind.NoResponse;
                        s.ErrorText = text.Length > 0 ? text : "无应答";
                        s.ErrorCount++;
                        break;
                    case LinTxEventKind.Error:
                        if (text.Length == 0) text = "总线错误";
                        s.State = LinRunStateKind.Error;
                        s.ErrorText = text;
                        s.ErrorCount++;
                        s.ErrorKind = errorKind != LinErrorKind.None ? errorKind : LinErrorKind.Hw;
                        break;
                    case LinTxEventKind.Unsupported:
                        s.State = LinRunStateKind.Unsupported;
                        s.ErrorKind = LinErrorKind.Hw;
                        s.ErrorText = text.Length > 0 ? text : "当前适配器不支持该模式";
                        s.ErrorCount++;
                        break;
                }
            }
            var handler = LinTxStateChanged;
            if (handler != null)
            {
                var ev = new LinTxEvent { LogicChannel = logicChannel, Pid = pid, Type = type, Kind = kind, Text = text };
                foreach (Action<LinTxEvent> d in handler.GetInvocationList())
                {
                    try { d(ev); }
                    catch { /* 单订阅者异常隔离 */ }
                }
            }
        }


        /// <summary>
        /// 按当前调度槽同步运行快照：启用项建立/刷新（Intent+Armed），已移除/停用项置 Disabled。
        /// 发送计划状态与真实总线帧状态必须分开：此处只产生计划行与状态，不伪造任何 Rx。
        /// </summary>
        internal static void SyncRunSnapshotsFromPlan(byte logicChannel, List<LinScheduleSlot> slots)
        {
            var active = new HashSet<byte>();
            if (slots != null)
                foreach (var slot in slots)
                    if (slot != null && slot.Enabled && slot.Pid <= 0x3F) active.Add(slot.Pid);

            var nowEnabled = new List<byte>();
            lock (_runLock)
            {
                foreach (var kv in _runSnapshots)
                {
                    if (((kv.Key >> 8) & 0xFF) == logicChannel && kv.Value.Enabled && !active.Contains(kv.Value.Pid))
                        nowEnabled.Add(kv.Value.Pid);
                }
            }
            foreach (byte pid in nowEnabled)
            {
                var type = GetTransmitType(logicChannel, pid, LinTransmitType.Master);
                NotifyTxState(logicChannel, pid, type, LinTxEventKind.Disabled, "已停用");
            }
            foreach (byte pid in active)
            {
                var type = GetTransmitType(logicChannel, pid, LinTransmitType.Master);
                NotifyTxState(logicChannel, pid, type, LinTxEventKind.Intent, "");
            }
        }


        /// <summary>设置运行快照的 HeaderOn 响应截止（微秒；0=当前不等待）</summary>
        internal static void SetExpectedResponseDeadline(byte logicChannel, byte pid, long deadlineUs)
        {
            var s = GetOrCreateRunSnapshot(logicChannel, pid);
            lock (_runLock) { s.ExpectedResponseDeadlineUs = deadlineUs; }
        }

        /// <summary>取某通道全部已启用运行快照（UI 计划行数据源；锁内拷贝，不持有引用）</summary>
        internal static List<LinRunSnapshot> GetEnabledRunSnapshots(byte logicChannel)
        {
            lock (_runLock)
            {
                var list = new List<LinRunSnapshot>();
                foreach (var kv in _runSnapshots)
                {
                    if (((kv.Key >> 8) & 0xFF) == logicChannel && kv.Value.Enabled)
                        list.Add(kv.Value);
                }
                list.Sort((a, b) => a.Pid.CompareTo(b.Pid));
                return list;
            }
        }

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

            // 阶段 4 现场链路诊断：明确打印实际波特率（允许配置其他合法 baud，验收必须可见实际值）；
            // LDF 通道与硬件通道标识不一致时给出警告（连接不阻断，仅定位接线/配置错位）。
            LinDebugLog.Write("[CONN] 波特率现场校验: 配置=" + cfg.Baudrate + "（19200 为验收基准）");
            if (cfg.Baudrate != 19200)
                LinDebugLog.Write("[CONN] 警告: 波特率 " + cfg.Baudrate + " 非 19200 验收基准，现场核对 LIN 节点一致性");
            string hwLabel = cfg.HwType == LinConfig.HwTypePcan ? "PEAK" : cfg.HwType == LinConfig.HwTypeCanoe ? "Vector" : "未绑定";
            if (cfg.LdfPath != null && cfg.LdfPath.Length > 0 && cfg.HwHandle != null && cfg.HwHandle.Length > 0)
            {
                // 通道一致性启发式：比较文件名与 HwHandle 的【末尾数字】。先剥离版本后缀
                // （V2.0/V1.3 等，其尾随数字是版本号不是通道号，如 LP_B13_EP1_LIN2_V2.0），
                // 无版本后缀时取尾随数字比较；无法可靠提取通道号（无数字/仅版本号）则不告警。
                string ldfBase = System.IO.Path.GetFileNameWithoutExtension(cfg.LdfPath);
                var versionSuffix = System.Text.RegularExpressions.Regex.Match(ldfBase, @"[Vv]\d+(\.\d+)*\s*$");
                System.Text.RegularExpressions.Match ldfNum = versionSuffix.Success
                    ? System.Text.RegularExpressions.Match.Empty
                    : System.Text.RegularExpressions.Regex.Match(ldfBase, @"(\d+)\s*$");
                string hwTrim = cfg.HwHandle.Replace("ch ", "").Replace("CH", "").Trim();
                var hwNum = System.Text.RegularExpressions.Regex.Match(hwTrim, @"(\d+)\s*$");
                if (ldfNum.Success && hwNum.Success && ldfNum.Groups[1].Value != hwNum.Groups[1].Value)
                    LinDebugLog.Write("[CONN] 警告: LDF 文件通道号 '" + ldfNum.Groups[1].Value + "'（" + ldfBase +
                        "）与硬件通道 '" + hwNum.Groups[1].Value + "'（" + hwTrim + "，" + hwLabel + "）不一致，核对配置");
            }

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
            // 阶段 1 审查 F1 闭合：调度器释放不依赖 UI 消息队列时序。
            // 摘除硬件后、广播断开事件前同步释放本通道调度器（幂等：未注册/已释放直接返回）；
            // 否则「新硬件已注册 → UI 线程执行释放回调」窗口内旧调度器仍会对新连接发 TX。
            ReleaseScheduler(logicChannel, "连接断开");
            if (logicChannel <= LinConfig.Channels.Count)
            {
                LinConfig.Channels[logicChannel - 1].IsConnected = false;
                // 兼容清理：旧运行期开关在断开时复位；周期发送模型下推导不再依赖它。
                LinConfig.Channels[logicChannel - 1].ForceMasterDriven = false;
                ChannelStateChanged?.Invoke(logicChannel, false, "");
            }
        }

        // ==================== 发送 ====================

        /// <summary>手动发送一帧（完整 Master 帧）；与周期发送共用 NotifyTxState 提交语义。</summary>
        public static bool LinTransmit(byte logicChannel, byte pid, byte[] data, LinChecksumKind ck)
        {
            ck = GetFrameChecksumKind(logicChannel, pid, ck);
            PcanLinHardware pcan;
            XlLinHardware xl;
            bool ok = false;
            lock (_hwLock)
            {
                if (_pcan.TryGetValue(logicChannel, out pcan)) ok = pcan.Transmit(pid, data, ck);
                else if (_xl.TryGetValue(logicChannel, out xl)) ok = xl.Transmit(pid, data, ck);
            }
            NotifyTxState(logicChannel, pid, LinTransmitType.Master,
                ok ? LinTxEventKind.SubmitOk : LinTxEventKind.SubmitFail,
                ok ? "" : "手动发送提交失败（未连接或驱动错误）");
            if (ok) EchoTx(logicChannel, pid, data, ck); // 仅本地提交回显（Tx 方向），不更新真实 Rx 活动
            return ok;
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

        /// <summary>按调度槽周期发送：Master 附本机响应，Slave/HeaderOnly 发 Header 等待总线响应。</summary>
        /// <summary>测试钩子：置非空时 LinSendScheduleSlot 直接用其返回值代替驱动提交（无硬件时验证调度语义）</summary>
        internal static Func<byte, LinScheduleSlot, bool> SendScheduleSlotOverride;

        public static bool LinSendScheduleSlot(byte logicChannel, LinScheduleSlot slot)
        {
            if (slot == null) return false;
            var sendOverride = SendScheduleSlotOverride;
            if (sendOverride != null) return sendOverride(logicChannel, slot);
            LinTransmitType type = slot.TransmitType;
            if (type == LinTransmitType.Master && GetTransmitEntry(logicChannel, slot.Pid) != null)
                type = GetTransmitType(logicChannel, slot.Pid, type);
            if (type == LinTransmitType.Slave)
            {
                // 方案 4.1：Slave 只武装本机响应并等待外部 Master Header，本机绝不主动发 Header。
                // 调度器不应把 Slave 槽放入发送循环；此处兜底拒绝并记录，防止误调度伪装成功。
                LinDebugLog.Write("[SCH] LinSendScheduleSlot ch=" + logicChannel + " pid=0x" + slot.Pid.ToString("X2") + " type=Slave → 拒绝本机发 Header（Slave 等待外部触发）");
                return false;
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

        /// <summary>
        /// 发送类型在线切换门禁：目标类型超出当前适配器能力时返回中文原因（空=允许）。
        /// 连接模式恒为 modMaster（用户决策）后，Master/HeaderOnly/Slave 三类型均在线可切；
        /// 仅 BreakOnly 因 PLIN/XL 驱动均无独立 Break 原语而始终拒绝。
        /// </summary>
        public static string CanSwitchTransmitType(byte logicChannel, byte pid, LinTransmitType newType)
        {
            if (newType == LinTransmitType.BreakOnly)
                return "当前 PCAN/Vector 适配器不支持独立 BreakOnly 原语";
            return "";
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

        /// <summary>会话毫秒（首次连接归零；经 SessionClockMs 可注入，测试与调度器共用同一时钟源）</summary>
        internal static long SessionMs
        {
            get { return SessionClockMs(); }
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

        // ==================== 接收汇聚 ====================

        /// <summary>
        /// 硬件无应答运行事件（方案 §3 P0/§4.2）：PEAK SlaveNOtResponding/Timeout 与 Vector XL_LIN_NOANS
        /// 是真实总线观测。软件调度路径下经注册表通知调度器结算最早 HeaderOnly pending（一次结算、限频），
        /// 避免与软件 deadline 双报；无 pending（Master/Slave/未调度）不报错——Master 完整帧无响应期、
        /// 纯 Slave 等待外部 Header 均不是错误（方案验证 A/B/E 语义）。
        /// </summary>
        private static void HandleHardwareNoResponse(byte logicChannel, byte pid)
        {
            LinScheduler sc;
            lock (_schedLock) { _schedulers.TryGetValue(logicChannel, out sc); }
            if (sc != null && !sc.IsDisposed)
                sc.SettleHardwareNoResponse(pid);
            // 无调度器 / 无 pending：不构造错误（运行快照锁定由调度器结算发布；未启用 PID 无计划行）
        }

        /// <summary>接收汇聚：统一时间戳 → 帧类型/名称映射（LDF）→ 事件派发
        /// 校验和判定信任硬件错误标志（PEAK ErrorFlags / Vector CRCERROR），不做软件复核——避免 cstAuto/经典校验帧误标</summary>
        public static void LinReceive(byte logicChannel, LinFrameRecord frame)
        {
            try
            {
                // 阶段 2 反模式门禁（方案 §3 P0/§4.2）：NoResponse 是【运行事件】不是总线帧。
                // PEAK SlaveNOtResponding/Timeout 与 Vector XL_LIN_NOANS 是真实硬件无应答观测，
                // 必须转为运行事件更新快照锁存（ErrorCount++/State=NoResponse），禁止以 Direction=Rx
                // 伪帧进入 LinFrameReceived/_lastRxMs/统计；软件调度时优先经调度器结算对应 pending，
                // 避免与软件 deadline（下限 500ms）双报。
                if (frame.ErrorKind == LinErrorKind.NoResponse)
                {
                    LinDebugLog.Write("[RX] 硬件无应答运行事件 ch=" + logicChannel + " pid=0x" + frame.Pid.ToString("X2"));
                    HandleHardwareNoResponse(logicChannel, frame.Pid);
                    return;
                }

                // 时间戳：会话时钟（硬件时间戳不一致，统一用 Stopwatch 会话归零）
                frame.TimestampUs = (ulong)_sw.ElapsedMilliseconds * 1000 - _epochUs;

                // 从节点响应超时兜底只看真实 Rx；软件 Tx 回显不能证明总线上出现了响应。
                if (frame.Direction == LinFrameDir.Rx)
                {
                    lock (_rxActivityLock)
                        _lastRxMs[FrameKey(logicChannel, frame.Pid)] = SessionMs;
                    // 运行快照真实总线帧统计（NoResponse 已被上面拦截，不会走到这里）
                    NotifyTxState(logicChannel, frame.Pid,
                        GetTransmitType(logicChannel, frame.Pid, LinTransmitType.Master),
                        LinTxEventKind.BusFrame, "");
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
                // Vector 侧无总线状态查询能力（当前封装无 XL 状态读取）：不把“已连接”谎报成“总线 Active”
                // （阶段 4 反模式门禁 3，审查建议 4）；显示“未初始化”等待现场接入状态模型。
                if (_xl.ContainsKey(logicChannel)) return "未初始化";
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
            Task.Run(async () =>
            {
                string lastErr = "";
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
                    lastErr = err;
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
                LinDebugLog.Write("[LINK] 自动重连全部失败 ch=" + logicChannel + " 最后原因: " + lastErr);
                LinkLostFinal?.Invoke(logicChannel, lastErr);
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
