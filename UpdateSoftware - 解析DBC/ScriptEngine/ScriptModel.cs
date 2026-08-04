using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PCAN_Client.ScriptEngine
{
    /* ========================================================================
     * 脚本发送 - 数据模型
     * CanScript(脚本) -> ScriptStep(步骤,树形嵌套) -> ScriptCondition(条件组)
     * 报文数据以 ScriptMessageSnapshot 快照持有,运行前经 ScriptMessageHelper
     * 深拷贝/重链接为独立 Message 副本,与发送列表编辑状态完全隔离。
     * ======================================================================== */

    /// <summary>步骤类型</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ScriptStepType
    {
        SendMessage,    // 发送报文
        Delay,          // 延时
        WaitCondition,  // 等待条件(带超时)
        IfBlock,        // 条件分支(满足才执行子步骤)
        LoopBlock,      // 循环块(次数/无限/条件)
        SetVariable,    // 变量赋值
        LogMessage,     // 日志输出
        StopScript      // 停止脚本
    }

    /// <summary>多条件组合逻辑</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CondLogicOp { And, Or }

    /// <summary>等待条件超时后的动作</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TimeoutAction { StopScript, ContinueNext }

    /// <summary>循环块模式</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LoopMode { Count, Infinite, Condition }

    /// <summary>变量赋值来源</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum VarSourceType { Const, Signal, Increment }

    /// <summary>条件操作数类型</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum OperandKind { Const, Variable, Signal, FrameCount, FrameByte }

    /// <summary>脚本触发方式</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerMode { Manual, OnFrameId, OnCondition }

    /// <summary>发送数据填充模式</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FillMode { None, RandomBytes, SignalIncrement }

    /// <summary>
    /// 条件操作数:常量 / 脚本变量($name) / 接收信号(通道+报文ID+信号名) /
    /// 帧统计(某ID接收帧数) / 帧字节(某ID最新帧指定字节值)
    /// </summary>
    public class ScriptOperand
    {
        public OperandKind Kind { get; set; } = OperandKind.Const;
        public double ConstValue { get; set; }
        public string VarName { get; set; } = "";
        // 信号/帧引用
        public byte Channel { get; set; } = 1;   // 逻辑通道号(1-based)
        public uint MessageId { get; set; }
        public string SignalName { get; set; } = "";
        public bool UseRawValue { get; set; }    // true=信号原始值 false=物理值
        public int ByteIndex { get; set; }       // FrameByte 用

        /// <summary>显示文本(条件构造器/摘要共用)</summary>
        public string DisplayText()
        {
            switch (Kind)
            {
                case OperandKind.Const: return ConstValue.ToString("G");
                case OperandKind.Variable: return "$" + VarName;
                case OperandKind.Signal:
                    string msgName = ScriptMessageHelper.FindMessageName(Channel, MessageId);
                    return $"CH{Channel} {(msgName ?? "0x" + MessageId.ToString("X"))}.{SignalName}" +
                           (UseRawValue ? "(原始值)" : "");
                case OperandKind.FrameCount: return $"帧数 CH{Channel} 0x{MessageId:X}";
                case OperandKind.FrameByte: return $"字节 CH{Channel} 0x{MessageId:X}[{ByteIndex}]";
                default: return "?";
            }
        }

        /// <summary>紧凑文本协议(序列化/手输解析用):数字 | $var | @CH1.1A0.Sig | @RAW:CH1.1A0.Sig | #CNT:CH1.100 | #BYTE:CH1.100[3]</summary>
        public string ToText()
        {
            switch (Kind)
            {
                case OperandKind.Const: return ConstValue.ToString("G", CultureInfo.InvariantCulture);
                case OperandKind.Variable: return "$" + VarName;
                case OperandKind.Signal:
                    return (UseRawValue ? "@RAW:" : "@") + $"CH{Channel}.{MessageId:X}.{SignalName}";
                case OperandKind.FrameCount: return $"#CNT:CH{Channel}.{MessageId:X}";
                case OperandKind.FrameByte: return $"#BYTE:CH{Channel}.{MessageId:X}[{ByteIndex}]";
                default: return "0";
            }
        }

        /// <summary>解析紧凑文本协议;失败返回false</summary>
        public static bool TryParse(string text, out ScriptOperand operand)
        {
            operand = new ScriptOperand();
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) ||
                double.TryParse(text, out num))
            {
                operand.Kind = OperandKind.Const;
                operand.ConstValue = num;
                return true;
            }
            if (text.StartsWith("$"))
            {
                string name = text.Substring(1).Trim();
                if (!IsValidVarName(name)) return false;
                operand.Kind = OperandKind.Variable;
                operand.VarName = name;
                return true;
            }
            if (text.StartsWith("@RAW:", StringComparison.OrdinalIgnoreCase))
            {
                operand.UseRawValue = true;
                return ParseSignalRef(text.Substring(5), operand);
            }
            if (text.StartsWith("@"))
            {
                return ParseSignalRef(text.Substring(1), operand);
            }
            if (text.StartsWith("#CNT:", StringComparison.OrdinalIgnoreCase))
            {
                operand.Kind = OperandKind.FrameCount;
                return ParseFrameRef(text.Substring(5), operand, false);
            }
            if (text.StartsWith("#BYTE:", StringComparison.OrdinalIgnoreCase))
            {
                operand.Kind = OperandKind.FrameByte;
                return ParseFrameRef(text.Substring(6), operand, true);
            }
            return false;
        }

        private static bool ParseSignalRef(string body, ScriptOperand operand)
        {
            // CH{ch}.{idHex}.{signalName}
            operand.Kind = OperandKind.Signal;
            if (!ParseFrameRef(body, operand, false)) return false;
            int dot2 = body.IndexOf('.', body.IndexOf('.') + 1);
            if (dot2 < 0 || dot2 == body.Length - 1) return false;
            operand.SignalName = body.Substring(dot2 + 1);
            return true;
        }

        private static bool ParseFrameRef(string body, ScriptOperand operand, bool withByteIdx)
        {
            // CH{ch}.{idHex} 或 CH{ch}.{idHex}[{idx}]
            if (!body.StartsWith("CH", StringComparison.OrdinalIgnoreCase)) return false;
            int dot1 = body.IndexOf('.');
            if (dot1 <= 2) return false;
            if (!byte.TryParse(body.Substring(2, dot1 - 2), out byte ch)) return false;
            string rest = body.Substring(dot1 + 1);
            int bIdx = 0;
            if (withByteIdx)
            {
                int lb = rest.IndexOf('['), rb = rest.IndexOf(']');
                if (lb < 0 || rb < lb + 1) return false;
                if (!int.TryParse(rest.Substring(lb + 1, rb - lb - 1), out bIdx) || bIdx < 0 || bIdx > 63) return false;
                rest = rest.Substring(0, lb);
            }
            else
            {
                int sigDot = rest.IndexOf('.');
                if (sigDot >= 0) rest = rest.Substring(0, sigDot); // 信号引用时去掉信号名部分
            }
            if (!uint.TryParse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)) return false;
            operand.Channel = ch;
            operand.MessageId = id;
            operand.ByteIndex = bIdx;
            return true;
        }

        public static bool IsValidVarName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        public ScriptOperand Clone() => (ScriptOperand)MemberwiseClone();
    }

    /// <summary>单条条件:左操作数 比较符 右操作数</summary>
    public class ScriptCondition
    {
        public ScriptOperand Left { get; set; } = new ScriptOperand();
        public string Op { get; set; } = ">";
        public ScriptOperand Right { get; set; } = new ScriptOperand();

        public static readonly string[] ValidOps = { ">", ">=", "<", "<=", "==", "!=" };

        public string DisplayText() => $"{Left.DisplayText()} {Op} {Right.DisplayText()}";

        public ScriptCondition Clone() => new ScriptCondition { Left = Left.Clone(), Op = Op, Right = Right.Clone() };
    }

    /// <summary>
    /// 发送报文快照:脚本步骤独立持有的报文数据(与发送列表隔离)。
    /// 信号值以"按名表达式字典"存储(数字或$变量),加载时重链接DBC信号定义。
    /// </summary>
    public class ScriptMessageSnapshot
    {
        public uint Id { get; set; }
        public bool IsExtendedId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>true=自定义报文(纯字节编辑,不与DBC重链接——即使ID巧合匹配DBC报文也保持纯字节)</summary>
        public bool IsCustom { get; set; }
        public byte Channel { get; set; } = 1;      // 逻辑发送通道
        public int DataLen { get; set; } = 8;
        public string DataHex { get; set; } = "";   // 空格分隔hex(无DBC报文的全部数据/DBC报文的基准数据)
        /// <summary>信号名 → 值表达式("25.5" 或 "$var");仅含用户编辑过的信号</summary>
        public Dictionary<string, string> SignalExprs { get; set; } = new Dictionary<string, string>();
        // 动态填充
        public FillMode Fill { get; set; } = FillMode.None;
        public int RandomFrom { get; set; } = 0;    // 随机填充起始字节(含)
        public int RandomTo { get; set; } = 7;      // 随机填充结束字节(含)
        public string IncrementSignal { get; set; } = "";  // 递增填充的信号名
        public double IncrementStart { get; set; }
        public double IncrementStep { get; set; } = 1;
        public double IncrementMax { get; set; } = 100;    // 达到/超过后回绕到Start

        /// <summary>运行期重建的 Message 深拷贝(不序列化,由 ScriptMessageHelper.RelinkFromDbc 生成)</summary>
        [JsonIgnore]
        public Message RuntimeMessage { get; set; }

        /// <summary>递增填充运行期当前值(不序列化)</summary>
        [JsonIgnore]
        public double IncrementCurrent { get; set; }
    }

    /// <summary>脚本步骤(树形:循环/IF块含子步骤)</summary>
    public class ScriptStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ScriptStepType Type { get; set; } = ScriptStepType.SendMessage;
        public string Comment { get; set; } = "";
        public List<ScriptStep> Children { get; set; } = new List<ScriptStep>();

        // --- 发送报文 ---
        public ScriptMessageSnapshot Msg { get; set; }
        public int RepeatCount { get; set; } = 1;
        public int RepeatIntervalMs { get; set; } = 100;

        // --- 延时 ---
        public int DelayMs { get; set; } = 100;

        // --- 条件(等待/IF/条件循环) ---
        public List<ScriptCondition> Conditions { get; set; } = new List<ScriptCondition>();
        public CondLogicOp Logic { get; set; } = CondLogicOp.And;
        public int TimeoutMs { get; set; } = 5000;
        public TimeoutAction OnTimeout { get; set; } = TimeoutAction.StopScript;

        // --- 循环 ---
        public LoopMode Loop { get; set; } = LoopMode.Count;
        public int LoopCount { get; set; } = 1;

        // --- 变量赋值 ---
        public string VarName { get; set; } = "";
        public VarSourceType VarSource { get; set; } = VarSourceType.Const;
        public double VarConst { get; set; }
        public double VarIncrementStep { get; set; } = 1;
        public ScriptOperand VarSignal { get; set; } = new ScriptOperand(); // VarSource=Signal 时取该操作数的值

        // --- 日志输出 ---
        public string Text { get; set; } = "";

        /// <summary>树节点摘要文本</summary>
        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            switch (Type)
            {
                case ScriptStepType.SendMessage:
                    sb.Append("发送 ").Append(Msg?.Name ?? "未配置");
                    if (Msg != null) sb.Append($" 0x{Msg.Id:X3} CH{Msg.Channel}");
                    if (RepeatCount > 1) sb.Append($" ×{RepeatCount}@{RepeatIntervalMs}ms");
                    if (Msg?.Fill == FillMode.RandomBytes) sb.Append(" [随机]");
                    if (Msg?.Fill == FillMode.SignalIncrement) sb.Append(" [递增]");
                    break;
                case ScriptStepType.Delay:
                    sb.Append($"延时 {DelayMs}ms");
                    break;
                case ScriptStepType.WaitCondition:
                    sb.Append($"等待 {CondSummary(Conditions, Logic)} (超时{TimeoutMs}ms→{(OnTimeout == TimeoutAction.StopScript ? "停止" : "继续")})");
                    break;
                case ScriptStepType.IfBlock:
                    sb.Append($"如果 {CondSummary(Conditions, Logic)}");
                    break;
                case ScriptStepType.LoopBlock:
                    if (Loop == LoopMode.Count) sb.Append($"循环 {LoopCount} 次");
                    else if (Loop == LoopMode.Infinite) sb.Append("循环 ∞");
                    else sb.Append($"循环 当 {CondSummary(Conditions, Logic)}");
                    break;
                case ScriptStepType.SetVariable:
                    sb.Append($"变量 ${VarName} = ");
                    if (VarSource == VarSourceType.Const) sb.Append(VarConst.ToString("G"));
                    else if (VarSource == VarSourceType.Signal) sb.Append(VarSignal?.DisplayText() ?? "?");
                    else sb.Append($"${VarName}+{(VarIncrementStep >= 0 ? "+" : "")}{VarIncrementStep:G}".Replace("++", "+"));
                    break;
                case ScriptStepType.LogMessage:
                    sb.Append($"日志 \"{Text}\"");
                    break;
                case ScriptStepType.StopScript:
                    sb.Append("停止脚本");
                    break;
            }
            if (!string.IsNullOrWhiteSpace(Comment)) sb.Append($"   // {Comment}");
            return sb.ToString();
        }

        public static string CondSummary(List<ScriptCondition> conds, CondLogicOp logic)
        {
            if (conds == null || conds.Count == 0) return "(无条件)";
            string sep = logic == CondLogicOp.And ? " 且 " : " 或 ";
            return string.Join(sep, conds.Select(c => c.DisplayText()));
        }

        /// <summary>深拷贝(含全部子步骤,重新生成Id)</summary>
        public ScriptStep DeepClone()
        {
            string json = JsonConvert.SerializeObject(this);
            var copy = JsonConvert.DeserializeObject<ScriptStep>(json);
            // 重新分配Id,避免粘贴副本与原步骤同Id
            var queue = new Queue<ScriptStep>();
            queue.Enqueue(copy);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                s.Id = Guid.NewGuid();
                foreach (var c in s.Children) queue.Enqueue(c);
            }
            return copy;
        }
    }

    /// <summary>脚本(根)</summary>
    public class CanScript
    {
        public string Name { get; set; } = "未命名脚本";
        public List<ScriptStep> Steps { get; set; } = new List<ScriptStep>();
        /// <summary>脚本整体循环次数;0=无限循环</summary>
        public int RunLoopCount { get; set; } = 1;
        public TriggerMode Trigger { get; set; } = TriggerMode.Manual;
        public uint TriggerFrameId { get; set; }
        public byte TriggerChannel { get; set; } = 0; // 0=任意通道
        public List<ScriptCondition> TriggerConditions { get; set; } = new List<ScriptCondition>();
        public CondLogicOp TriggerLogic { get; set; } = CondLogicOp.And;

        /// <summary>全部步骤数(含嵌套,用于空脚本校验/统计)</summary>
        public int TotalStepCount()
        {
            int n = 0;
            var queue = new Queue<ScriptStep>(Steps);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                n++;
                foreach (var c in s.Children) queue.Enqueue(c);
            }
            return n;
        }
    }

    /// <summary>报文快照与运行期 Message 的转换工具(深拷贝/DBC重链接/序列化)</summary>
    public static class ScriptMessageHelper
    {
        /// <summary>信号逐字段深拷贝(enumDefinitions共享只读定义字典)</summary>
        public static Signal CloneSignal(Signal s)
        {
            return new Signal
            {
                signalName = s.signalName,
                multiplexerIndicator = s.multiplexerIndicator,
                startBit = s.startBit,
                signalSize = s.signalSize,
                byteOrder = s.byteOrder,
                valueType = s.valueType,
                factor = s.factor,
                offset = s.offset,
                minimum = s.minimum,
                maximum = s.maximum,
                rawValue = s.rawValue,
                receivers = s.receivers,
                result = s.result,
                unitStr = s.unitStr,
                signalDisplayStr = s.signalDisplayStr,
                enumDefinitions = s.enumDefinitions, // 只读定义,共享
                cmdValue = s.cmdValue,
                Comment = s.Comment,
                ChartShowFlag = false
            };
        }

        /// <summary>报文深拷贝(含全部信号;不含接收运行态)</summary>
        public static Message CloneMessage(Message src)
        {
            return new Message
            {
                messgeId = src.messgeId,
                isExternId = src.isExternId,
                messageName = src.messageName,
                messageSize = src.messageSize,
                transmitter = src.transmitter,
                messageComment = src.messageComment,
                cycleTime = src.cycleTime,
                signals = src.signals?.Select(CloneSignal).ToList() ?? new List<Signal>(),
                sendFalg = false,
                enableFlag = false,
                sendCnt = 0,
                aliveCount = 0,
                updateFlag = true,
                sendBuf = src.sendBuf?.ToArray(),
                TxChannel = src.TxChannel
            };
        }

        /// <summary>从现有 Message(发送列表/DBC/自定义)生成快照</summary>
        public static ScriptMessageSnapshot SnapshotFromMessage(Message msg, byte channel)
        {
            var snap = new ScriptMessageSnapshot
            {
                Id = msg.messgeId,
                IsExtendedId = msg.isExternId,
                Name = msg.messageName,
                // 无信号定义的报文(自定义报文)按纯字节处理,不做DBC重链接
                IsCustom = msg.signals == null || msg.signals.Count == 0,
                Channel = channel,
                DataLen = Math.Max(8, (int)msg.messageSize),
                DataHex = msg.sendBuf != null
                    ? string.Join(" ", msg.sendBuf.Select(b => b.ToString("X2")))
                    : "",
                SignalExprs = new Dictionary<string, string>()
            };
            if (snap.DataHex == "" && msg.signals != null && msg.signals.Count > 0)
            {
                try
                {
                    var buf = CanMessageBuilder.EncodeSignals(msg.signals, snap.DataLen);
                    snap.DataHex = string.Join(" ", buf.Select(b => b.ToString("X2")));
                }
                catch { }
            }
            // 已编辑过的信号值按名记录(cmdValue与默认值可能不同,全部记录以保证还原)
            if (msg.signals != null)
            {
                foreach (var sig in msg.signals)
                {
                    snap.SignalExprs[sig.signalName] = sig.cmdValue.ToString("G", CultureInfo.InvariantCulture);
                }
            }
            return snap;
        }

        /// <summary>
        /// 加载/运行前重建:按 通道+报文ID 匹配当前通道DBC克隆信号定义并恢复快照值;
        /// 无匹配DBC时退化为纯字节报文(signals为空,sendBuf=DataHex)。
        /// 返回null表示快照无效(ID为0等)。
        /// </summary>
        public static Message RelinkFromDbc(ScriptMessageSnapshot snap)
        {
            if (snap == null || snap.Id == 0) return null;
            // 自定义报文不查DBC,直接按纯字节构造(即使ID巧合匹配DBC报文)
            Message dbcMsg = snap.IsCustom ? null : FindDbcMessage(snap.Channel, snap.Id);
            Message rt;
            if (dbcMsg != null)
            {
                rt = CloneMessage(dbcMsg);
                // 恢复快照的信号值表达式中的纯数字部分($变量在发送时替换)
                if (snap.SignalExprs != null)
                {
                    foreach (var kv in snap.SignalExprs)
                    {
                        var sig = rt.signals.FirstOrDefault(s => s.signalName == kv.Key);
                        if (sig == null) continue;
                        if (!kv.Value.StartsWith("$") &&
                            double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        {
                            sig.cmdValue = v;
                        }
                    }
                    rt.updateFlag = true;
                }
                // 基准数据:无信号表达式覆盖时沿用DataHex
                if (!string.IsNullOrWhiteSpace(snap.DataHex))
                {
                    var buf = ParseHex(snap.DataHex, Math.Max(8, (int)rt.messageSize));
                    if (buf != null && (snap.SignalExprs == null || snap.SignalExprs.Count == 0))
                    {
                        rt.sendBuf = buf;
                        rt.updateFlag = false;
                    }
                }
            }
            else
            {
                rt = new Message
                {
                    messgeId = snap.Id,
                    isExternId = snap.IsExtendedId,
                    messageName = string.IsNullOrWhiteSpace(snap.Name) ? $"自定义_0x{snap.Id:X3}" : snap.Name,
                    messageSize = (uint)Math.Max(8, snap.DataLen),
                    signals = new List<Signal>(),
                    sendBuf = ParseHex(snap.DataHex, Math.Max(8, snap.DataLen)) ?? new byte[Math.Max(8, snap.DataLen)],
                    updateFlag = false,
                    TxChannel = snap.Channel
                };
            }
            rt.messgeId = snap.Id; // 允许快照ID与DBC不同(用户改ID场景)
            rt.TxChannel = snap.Channel;
            snap.RuntimeMessage = rt;
            snap.IncrementCurrent = snap.IncrementStart;
            return rt;
        }

        /// <summary>按 通道+ID 查通道DBC报文;指定通道未命中时遍历所有已配置通道兜底(通道配置可能变化)</summary>
        public static Message FindDbcMessage(byte channel, uint id)
        {
            var helper = BaseParamter.GetDbcHelperByChannel(channel);
            var msg = helper?.dbcFile?.messageDict != null &&
                      helper.dbcFile.messageDict.TryGetValue(id, out Message m) ? m : null;
            if (msg != null) return msg;
            foreach (var ch in BaseParamter.BusChannels)
            {
                if (!ch.IsConfigured) continue;
                if (ch.DbcHelper.dbcFile.messageDict.TryGetValue(id, out m)) return m;
            }
            return null;
        }

        /// <summary>查报文名(显示用);未找到返回null</summary>
        public static string FindMessageName(byte channel, uint id)
        {
            return FindDbcMessage(channel, id)?.messageName;
        }

        /// <summary>解析空格分隔hex为字节数组(失败返回null)</summary>
        public static byte[] ParseHex(string hex, int capacity)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var parts = hex.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > capacity) return null;
            var buf = new byte[capacity];
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i];
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                if (!byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    return null;
                buf[i] = b;
            }
            return buf;
        }

        /// <summary>脚本JSON序列化(独立*.canscript文件,枚举为字符串提高可读性)</summary>
        public static string SerializeScript(CanScript script)
        {
            return JsonConvert.SerializeObject(script, Formatting.Indented);
        }

        public static CanScript DeserializeScript(string json)
        {
            return JsonConvert.DeserializeObject<CanScript>(json);
        }

        /// <summary>运行前重建整脚本的运行期报文(所有发送步骤)</summary>
        public static void RelinkAll(CanScript script)
        {
            var queue = new Queue<ScriptStep>(script.Steps);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                if (s.Type == ScriptStepType.SendMessage && s.Msg != null)
                    RelinkFromDbc(s.Msg);
                foreach (var c in s.Children) queue.Enqueue(c);
            }
        }
    }
}
