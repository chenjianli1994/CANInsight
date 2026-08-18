using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PCAN_Client.LIN_API
{
    /// <summary>LDF 帧定义</summary>
    public class LinFrameDef
    {
        public byte Pid;
        public string Name = "";
        public byte Dlc;
        public LinFrameType FrameType = LinFrameType.Unconditional;
        /// <summary>发布节点名（LDF 原样）</summary>
        public string Publisher = "";
    }

    /// <summary>LDF 信号定义（width/初始值/发布者；起始位来自帧内映射 offset）</summary>
    public class LinSignalDef
    {
        public string Name = "";
        public ushort Width;
        public double InitValue;
        public string Publisher = "";
        /// <summary>物理量程与换算（LDF Signals 块：min, max, scale, offset）；物理值 = raw*scale + offset</summary>
        public double MinValue;
        public double MaxValue;
        public double Scale = 1;
        public double Offset;
    }

    /// <summary>帧内信号映射（SignalName 在帧数据中的起始位 offset）</summary>
    public class LinFrameSignal
    {
        public string SignalName = "";
        public ushort Offset;
    }

    /// <summary>LDF 调度表槽定义</summary>
    public class LinScheduleSlotDef
    {
        public string FrameName = "";
        public int SlotMs;
    }

    /// <summary>LDF 解析结果</summary>
    public class LinLdfFile
    {
        /// <summary>帧定义（按 PID 索引）</summary>
        public Dictionary<byte, LinFrameDef> Frames = new Dictionary<byte, LinFrameDef>();
        /// <summary>信号定义</summary>
        public List<LinSignalDef> Signals = new List<LinSignalDef>();
        /// <summary>帧内信号映射（PID → 信号+offset）</summary>
        public Dictionary<byte, List<LinFrameSignal>> FrameSignals = new Dictionary<byte, List<LinFrameSignal>>();
        /// <summary>调度表定义（表名 → 槽列表）</summary>
        public Dictionary<string, List<LinScheduleSlotDef>> ScheduleTables = new Dictionary<string, List<LinScheduleSlotDef>>();
        /// <summary>从节点发布的帧 ID 集合（响应 ID，从节点仿真用）</summary>
        public List<byte> SlaveRespIds = new List<byte>();
        /// <summary>主节点名（LDF 原样）</summary>
        public string MasterName = "";
    }

    /// <summary>LDF 解析异常（携带行号）</summary>
    public class LinLdfException : Exception
    {
        public int LineNumber { get; private set; }
        public LinLdfException(int line, string message) : base($"LDF 解析错误(第 {line} 行): {message}")
        {
            LineNumber = line;
        }
    }

    /// <summary>
    /// LDF 解析器（LIN 1.3/2.x/ISO 17987 描述文件，行式状态机）
    /// 支持块：Nodes / Frames（帧定义嵌套信号列表）/ Diagnostic_frames / Event_triggered_frames /
    /// Sporadic_frames / Signals / Schedule_tables（帧名 delay N ms 槽）/ 其余块容错跳过
    /// 语法经 ldfparser 测试套件 12 个真实样本（LIN 1.3/2.0/2.1/2.2/ISO 17987/诊断/偶发帧）回归验证
    /// </summary>
    public static class LinLdfHelper
    {
        /// <summary>解析 LDF 文件（UTF-8 编码）</summary>
        public static LinLdfFile Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new LinLdfException(0, $"文件不存在: {path}");
            return ParseLines(File.ReadAllLines(path, Encoding.UTF8));
        }

        /// <summary>解析 LDF 文本（测试用）</summary>
        public static LinLdfFile ParseText(string text)
        {
            return ParseLines(text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        }

        /// <summary>解析帧 ID：0x/h 前缀按十六进制；纯数字按十进制；含字母按十六进制（LDF 三种写法并存）</summary>
        private static bool TryParseFrameId(string s, out byte pid)
        {
            pid = 0;
            string t = (s ?? "").Trim();
            if (t.Length == 0) return false;
            bool hex = false;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(2);
                hex = true;
            }
            else if (t.EndsWith("h", StringComparison.OrdinalIgnoreCase) && t.Length > 1)
            {
                t = t.Substring(0, t.Length - 1);
                hex = true;
            }
            else
            {
                // 纯数字 → 十进制（lin13 的 "32"）；含字母 → 十六进制（"3c"）
                hex = false;
                foreach (char c in t)
                {
                    if (c < '0' || c > '9') { hex = true; break; }
                }
            }
            bool ok = hex
                ? byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid)
                : byte.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
            return ok && pid <= 0x7F; // 标准 LIN ID 0x00-0x3F；0x40-0x7F 保留区宽容（测试文件出现）
        }

        /// <summary>按逗号切分但跳过 { } 括号组（诊断槽参数/事件帧列表）</summary>
        private static string[] SplitTopLevel(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(s.Substring(start));
            return parts.ToArray();
        }

        /// <summary>块关键字（行首大小写不敏感匹配）</summary>
        private static readonly string[] BlockKeywords =
        {
            "NODES", "FRAMES", "SIGNALS", "SCHEDULE_TABLES", "EVENT_TRIGGERED_FRAMES", "SPORADIC_FRAMES",
            "DIAGNOSTIC_FRAMES", "DIAGNOSTIC_SIGNALS", "NODE_ATTRIBUTES", "SIGNAL_ENCODING_TYPES",
            "SIGNAL_GROUPS", "SIGNAL_REPRESENTATION", "NODE_COMPOSITIONS", "DIAGNOSTIC_ADDRESSES",
        };

        /// <summary>内容可整体跳过的块（解析器不需要其内部数据）</summary>
        private static bool IsSkipBlock(string block)
        {
            switch (block)
            {
                case "Node_attributes": case "Signal_encoding_types": case "Signal_groups":
                case "Signal_representation": case "Node_compositions":
                case "Diagnostic_addresses":
                    return true;
                default:
                    return false;
            }
        }

        private static LinLdfFile ParseLines(string[] lines)
        {
            var file = new LinLdfFile();
            var stack = new List<string>();     // 块上下文栈
            string curTable = null;             // 当前调度表名
            var slaveNames = new HashSet<string>();
            var sporadicFrames = new HashSet<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw == null) continue;
                int ci = raw.IndexOf("//", StringComparison.Ordinal);
                if (ci >= 0) raw = raw.Substring(0, ci);
                string line = raw.Trim();
                if (line.Length == 0) continue;
                // 跳过 /* */ 块注释行（头部注释在块外本就忽略；块内出现时防误解析）
                if (line.StartsWith("/*") || line.StartsWith("*")) continue;

                int lineNo = i + 1;
                string upper = line.ToUpperInvariant();
                string top = stack.Count > 0 ? stack[stack.Count - 1] : "";

                // ===== 块结束 =====
                if (upper == "}")
                {
                    if (stack.Count > 0)
                    {
                        string popped = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        if (popped.StartsWith("Table:")) curTable = null;
                    }
                    continue;
                }

                // ===== 块开始（行以 { 结尾）=====
                if (line.EndsWith("{"))
                {
                    string head = line.Substring(0, line.Length - 1).Trim();
                    // 1) 已知块关键字
                    string kw = null;
                    foreach (string k in BlockKeywords)
                    {
                        if (upper.StartsWith(k)) { kw = k; break; }
                    }
                    if (kw != null)
                    {
                        stack.Add(kw == "NODES" ? "Nodes" : kw == "FRAMES" ? "Frames" : kw == "SIGNALS" ? "Signals"
                            : kw == "SCHEDULE_TABLES" ? "Schedule_tables" : kw == "EVENT_TRIGGERED_FRAMES" ? "Event_triggered_frames"
                            : kw == "SPORADIC_FRAMES" ? "Sporadic_frames" : kw == "DIAGNOSTIC_FRAMES" ? "Diagnostic_frames"
                            : kw == "DIAGNOSTIC_SIGNALS" ? "Diagnostic_signals" : kw == "NODE_ATTRIBUTES" ? "Node_attributes"
                            : kw == "SIGNAL_ENCODING_TYPES" ? "Signal_encoding_types" : kw == "SIGNAL_GROUPS" ? "Signal_groups"
                            : kw == "SIGNAL_REPRESENTATION" ? "Signal_representation" : kw == "NODE_COMPOSITIONS" ? "Node_compositions"
                            : "Diagnostic_addresses");
                        if (kw == "SCHEDULE_TABLES") curTable = null;
                        continue;
                    }
                    // 2) Schedule_tables 内的表名行
                    if (top == "Schedule_tables")
                    {
                        if (head.Length == 0) throw new LinLdfException(lineNo, $"调度表名缺失: {line}");
                        stack.Add("Table:" + head);
                        curTable = head;
                        continue;
                    }
                    // 3) Frames/Diagnostic_frames 内的帧定义行（嵌套信号列表）
                    if (top == "Frames" || top == "Diagnostic_frames")
                    {
                        string frameName = ParseFrameDef(file, line, lineNo, top == "Diagnostic_frames");
                        stack.Add("FrameBody:" + frameName);
                        continue;
                    }
                    // 4) 跳过块内的任意子块（Node_attributes 的 LSM {、编码表名等）
                    if (IsSkipBlock(top) || top == "SkipBody")
                    {
                        stack.Add("SkipBody");
                        continue;
                    }
                    throw new LinLdfException(lineNo, $"无法识别的块开始: {line}");
                }

                // ===== 块外行（头信息等）忽略 =====
                if (stack.Count == 0) continue;

                string ctx = top;

                // ===== 帧体内信号映射行：SIGNAL, OFFSET; =====
                if (ctx.StartsWith("FrameBody:"))
                {
                    ParseFrameSignal(file, ctx.Substring(10), line, lineNo);
                    continue;
                }

                switch (ctx)
                {
                    case "Nodes":
                        ParseNodes(line, file, slaveNames, lineNo);
                        break;
                    case "Signals":
                    case "Diagnostic_signals":
                        ParseSignal(file, line, lineNo);
                        break;
                    case "Schedule_tables":
                        // 表名行应带 {（已在上方处理）；裸表名行按表名记录（容错）
                        if (line.EndsWith(";")) line = line.Substring(0, line.Length - 1).Trim();
                        if (line.Length > 0 && curTable == null) { curTable = line; stack.Add("Table:" + line); }
                        break;
                    case "Event_triggered_frames":
                        ParseEventFrame(file, line, lineNo);
                        break;
                    case "Sporadic_frames":
                        ParseSporadicFrame(line, sporadicFrames);
                        break;
                    case "Diagnostic_frames":
                        // 分号结尾的裸诊断帧定义（无嵌套信号列表的变体）
                        ParseFrameDef(file, line, lineNo, true);
                        break;
                    default:
                        break; // 跳过块内内容
                }

                // Table:xxx 上下文（表槽行）
                if (ctx.StartsWith("Table:"))
                {
                    ParseScheduleSlot(file, ctx.Substring(6), line, lineNo);
                }
            }

            // ===== 收尾：帧类型归类 + 从节点响应 ID 集合 =====
            foreach (var kv in file.Frames)
            {
                var def = kv.Value;
                if (def.Pid == 0x3C || def.Pid == 0x3D) def.FrameType = LinFrameType.Diagnostic;
                else if (sporadicFrames.Contains(def.Name)) def.FrameType = LinFrameType.Sporadic;
                if (def.FrameType != LinFrameType.EventTriggered &&
                    def.Publisher.Length > 0 && def.Publisher != file.MasterName)
                    file.SlaveRespIds.Add(def.Pid);
                // LIN 1.3 旧格式帧定义无 DLC 字段（如 "Frm:0x30,CEM {"）：按信号位布局推导
                if (def.Dlc == 0 && file.FrameSignals.ContainsKey(kv.Key))
                {
                    int maxBit = 0;
                    foreach (var fs in file.FrameSignals[kv.Key])
                    {
                        LinSignalDef sig = null;
                        foreach (var s in file.Signals) { if (s.Name == fs.SignalName) { sig = s; break; } }
                        if (sig == null) continue;
                        int end = fs.Offset + sig.Width;
                        if (end > maxBit) maxBit = end;
                    }
                    if (maxBit > 0) def.Dlc = (byte)((maxBit + 7) / 8);
                }
            }
            file.SlaveRespIds.Sort();
            return file;
        }

        private static void ParseNodes(string line, LinLdfFile file, HashSet<string> slaveNames, int lineNo)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) return;
            string kind = line.Substring(0, colon).Trim();
            string rest = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
            string[] parts = SplitTopLevel(rest);
            if (kind.Equals("Master", StringComparison.OrdinalIgnoreCase) && parts.Length >= 1)
            {
                file.MasterName = parts[0].Trim();
            }
            else if (kind.Equals("Slaves", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string p in parts)
                {
                    string n = p.Trim();
                    if (n.Length > 0) slaveNames.Add(n);
                }
            }
        }

        /// <summary>
        /// 解析帧定义行并注册：
        /// 嵌套格式 "NAME: ID, PUBLISHER, DLC {"（Frames/Diagnostic_frames 块，行尾 {）
        /// 或裸格式 "NAME: ID, PUBLISHER, DLC;"（兼容）
        /// 返回帧名（供 FrameBody 上下文使用）
        /// </summary>
        private static string ParseFrameDef(LinLdfFile file, string line, int lineNo, bool isDiagnostic)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) throw new LinLdfException(lineNo, $"帧定义缺少冒号: {line}");
            string frameName = line.Substring(0, colon).Trim();
            string rest = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
            if (rest.EndsWith("{")) rest = rest.Substring(0, rest.Length - 1).Trim(); // 嵌套格式去掉行尾 {
            string[] parts = SplitTopLevel(rest);
            if (parts.Length < 1) throw new LinLdfException(lineNo, $"帧定义字段不足: {line}");
            byte pid;
            if (!TryParseFrameId(parts[0].Trim(), out pid))
                throw new LinLdfException(lineNo, $"帧 ID 非法(须 0-0x3F): {parts[0].Trim()}");
            // 诊断帧定义 "MasterReq: 0x3c {" 无发布者/DLC 字段
            string publisher = parts.Length >= 2 ? parts[1].Trim() : "";
            byte dlc = 0;
            if (parts.Length >= 3)
            {
                if (!byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dlc) || dlc > 8)
                    throw new LinLdfException(lineNo, $"帧 DLC 非法(须 0-8): {parts[2].Trim()}");
            }
            // 诊断帧固定 8 字节（LIN 规范）；LDF 定义里无 DLC 字段，解析为 0 会让信号编辑/应答数据长度错误
            if (isDiagnostic && dlc == 0) dlc = 8;
            file.Frames[pid] = new LinFrameDef
            {
                Pid = pid,
                Name = frameName,
                Dlc = dlc,
                Publisher = publisher,
                FrameType = isDiagnostic ? LinFrameType.Diagnostic : LinFrameType.Unconditional,
            };
            return frameName;
        }

        private static void ParseFrameSignal(LinLdfFile file, string frameName, string line, int lineNo)
        {
            // 格式: SIGNAL_NAME, OFFSET;
            string body = line.Trim().TrimEnd(';').Trim();
            string[] parts = SplitTopLevel(body);
            if (parts.Length < 2) return;
            string sigName = parts[0].Trim();
            ushort offset;
            if (!ushort.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out offset)) return;
            // 帧名 → PID
            byte pid = 0xFF;
            foreach (var kv in file.Frames)
            {
                if (kv.Value.Name == frameName) { pid = kv.Key; break; }
            }
            if (pid == 0xFF) return;
            List<LinFrameSignal> list;
            if (!file.FrameSignals.TryGetValue(pid, out list))
            {
                list = new List<LinFrameSignal>();
                file.FrameSignals[pid] = list;
            }
            list.Add(new LinFrameSignal { SignalName = sigName, Offset = offset });
        }

        private static void ParseSignal(LinLdfFile file, string line, int lineNo)
        {
            // 格式: SIG_NAME: WIDTH, INITIAL_VALUE, PUBLISHER, SUBSCRIBER...;
            // 诊断信号（MasterReqB0 等）只有 2 字段: WIDTH, INIT; 无发布者
            int colon = line.IndexOf(':');
            if (colon <= 0) return;
            string sigName = line.Substring(0, colon).Trim();
            string rest = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
            string[] parts = SplitTopLevel(rest);
            if (parts.Length < 2) throw new LinLdfException(lineNo, $"信号定义字段不足: {line}");
            // WIDTH 可为列表 {5,4,3,2,1}（ISO 17987 可变长度信号）→ 取最大值
            ushort width;
            string wTok = parts[0].Trim();
            if (wTok.StartsWith("{"))
            {
                width = 0;
                foreach (string p in SplitTopLevel(wTok.Trim('{', '}')))
                {
                    ushort v;
                    if (ushort.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > width) width = v;
                }
            }
            else if (!ushort.TryParse(wTok, NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
            {
                throw new LinLdfException(lineNo, $"信号宽度非法: {wTok}");
            }
            double init;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out init)) init = 0;
            // LIN 1.3/2.x 完整格式: name, size, init, min, max, scale, offset, publisher, ...
            // 诊断信号仅 WIDTH, INIT（无其余字段）；容错解析缺失字段用默认值
            double min = 0, max = 0, scale = 1, offset = 0;
            if (parts.Length >= 4) double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out min);
            if (parts.Length >= 5) double.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            if (parts.Length >= 6) { if (!double.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) scale = 1; }
            if (parts.Length >= 7) double.TryParse(parts[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
            file.Signals.Add(new LinSignalDef
            {
                Name = sigName,
                Width = width,
                InitValue = init,
                Publisher = parts.Length >= 3 ? parts[2].Trim() : "",
                MinValue = min,
                MaxValue = max,
                Scale = scale,
                Offset = offset,
            });
        }

        /// <summary>事件触发帧：NAME : COLLISION_TABLE, ID, F1, F2...;（无 DLC，取包含帧的 DLC）</summary>
        private static void ParseEventFrame(LinLdfFile file, string line, int lineNo)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) return;
            string name = line.Substring(0, colon).Trim();
            string[] parts = SplitTopLevel(line.Substring(colon + 1).Trim().TrimEnd(';').Trim());
            if (parts.Length < 3) throw new LinLdfException(lineNo, $"事件帧定义字段不足: {line}");
            byte pid;
            if (!TryParseFrameId(parts[1].Trim(), out pid))
                throw new LinLdfException(lineNo, $"事件帧 ID 非法: {parts[1].Trim()}");
            byte dlc = 0;
            for (int i = 2; i < parts.Length; i++)
            {
                LinFrameDef def;
                if (file.Frames.TryGetValue(FindFramePidByName(file, parts[i].Trim()), out def))
                {
                    dlc = def.Dlc;
                    break;
                }
            }
            file.Frames[pid] = new LinFrameDef { Pid = pid, Name = name, Dlc = dlc, FrameType = LinFrameType.EventTriggered };
        }

        private static void ParseSporadicFrame(string line, HashSet<string> sporadicFrames)
        {
            // 格式: SF_NAME: FRAME1, FRAME2...;
            int colon = line.IndexOf(':');
            if (colon <= 0) return;
            foreach (string p in SplitTopLevel(line.Substring(colon + 1).Trim().TrimEnd(';').Trim()))
            {
                string n = p.Trim();
                if (n.Length > 0) sporadicFrames.Add(n);
            }
        }

        /// <summary>调度表槽：FRAME delay N ms;（含诊断槽 "Cmd {params} delay N ms;"）或兼容 "SLOT: FRAME, N;"</summary>
        private static void ParseScheduleSlot(LinLdfFile file, string tableName, string line, int lineNo)
        {
            if (tableName.Length == 0) return;
            string body = line.Trim().TrimEnd(';').Trim();
            string frameName = "";
            int slotMs = 0;

            int di = body.IndexOf("delay", StringComparison.OrdinalIgnoreCase);
            if (di >= 0)
            {
                frameName = body.Substring(0, di).Trim();
                // 去掉诊断槽参数 {LSM} 等
                int bo = frameName.IndexOf('{');
                if (bo >= 0) frameName = frameName.Substring(0, bo).Trim();
                string msTok = body.Substring(di + 5).Trim().ToLowerInvariant().Replace("ms", "").Trim().TrimEnd(';', ',').Trim();
                // 时隙可为小数（Mentor 生成文件 "10.000 ms"）
                double ms;
                if (double.TryParse(msTok, NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
                    slotMs = (int)Math.Round(ms);
            }
            else
            {
                // 兼容旧格式 SLOT_NAME: FRAME, N;
                int colon = body.IndexOf(':');
                if (colon > 0) body = body.Substring(colon + 1).Trim();
                string[] parts = SplitTopLevel(body);
                if (parts.Length >= 2)
                {
                    frameName = parts[0].Trim();
                    double ms;
                    if (double.TryParse(parts[1].Trim().ToLowerInvariant().Replace("ms", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
                        slotMs = (int)Math.Round(ms);
                }
            }

            if (frameName.Length == 0 || slotMs <= 0)
                throw new LinLdfException(lineNo, $"调度槽时隙非法: {line}");

            // 只记录已注册的帧（诊断命令槽 AssignNAD 等无对应帧定义，跳过）
            if (FindFramePidByName(file, frameName) == 0xFF) return;

            List<LinScheduleSlotDef> list;
            if (!file.ScheduleTables.TryGetValue(tableName, out list))
            {
                list = new List<LinScheduleSlotDef>();
                file.ScheduleTables[tableName] = list;
            }
            list.Add(new LinScheduleSlotDef { FrameName = frameName, SlotMs = slotMs });
        }

        private static byte FindFramePidByName(LinLdfFile file, string name)
        {
            foreach (var kv in file.Frames)
            {
                if (kv.Value.Name == name) return kv.Key;
            }
            return 0xFF;
        }

        /// <summary>从 LDF 帧表取帧名，未定义返回 "0x{pid:X2}"</summary>
        public static string GetFrameName(LinLdfFile ldf, byte pid)
        {
            LinFrameDef def;
            if (ldf != null && ldf.Frames.TryGetValue(pid, out def)) return def.Name;
            return "0x" + pid.ToString("X2");
        }

        // ==================== 信号编解码（物理值 ↔ 原始位） ====================

        /// <summary>LDF 信号定义查找（按帧 PID + 信号名；无定义返回 null）</summary>
        public static LinSignalDef FindSignal(LinLdfFile ldf, byte pid, string signalName)
        {
            if (ldf == null) return null;
            foreach (var s in ldf.Signals)
                if (s.Name == signalName) return s;
            return null;
        }

        /// <summary>物理值 → raw 整数（raw = (phys - offset) / scale，四舍五入）</summary>
        public static ulong PhysToRaw(double phys, LinSignalDef sig)
        {
            double scale = sig != null && sig.Scale != 0 ? sig.Scale : 1;
            double offset = sig != null ? sig.Offset : 0;
            double raw = (phys - offset) / scale;
            if (raw < 0) return 0;
            return (ulong)Math.Round(raw);
        }

        /// <summary>raw 整数 → 物理值（phys = raw*scale + offset）</summary>
        public static double RawToPhys(ulong raw, LinSignalDef sig)
        {
            double scale = sig != null && sig.Scale != 0 ? sig.Scale : 1;
            double offset = sig != null ? sig.Offset : 0;
            return raw * scale + offset;
        }

        /// <summary>读帧数据中指定起始位/长度的 raw 值（LDF 小端序位填充：低位在前）</summary>
        public static ulong ReadSignalBits(byte[] data, int bitOffset, int width)
        {
            ulong v = 0;
            for (int b = 0; b < width; b++)
            {
                int byteIdx = (bitOffset + b) / 8;
                if (byteIdx < 0 || byteIdx >= (data == null ? 0 : data.Length)) break;
                if (((data[byteIdx] >> ((bitOffset + b) % 8)) & 1) != 0) v |= 1UL << b;
            }
            return v;
        }

        /// <summary>把 raw 值按起始位/长度写入帧数据（低位在前）</summary>
        public static void WriteSignalBits(byte[] data, int bitOffset, int width, ulong raw)
        {
            for (int b = 0; b < width; b++)
            {
                int bit = bitOffset + b;
                int byteIdx = bit / 8;
                if (byteIdx < 0 || byteIdx >= data.Length) break;
                bool one = ((raw >> b) & 1) != 0;
                if (one) data[byteIdx] |= (byte)(1 << (bit % 8));
                else data[byteIdx] &= (byte)~(1 << (bit % 8));
            }
        }

        /// <summary>从 LDF 帧表取 DLC，未定义返回 0（调用方按 8 保守处理）</summary>
        public static byte GetFrameDlc(LinLdfFile ldf, byte pid)
        {
            LinFrameDef def;
            if (ldf != null && ldf.Frames.TryGetValue(pid, out def)) return def.Dlc;
            return 0;
        }
    }
}
