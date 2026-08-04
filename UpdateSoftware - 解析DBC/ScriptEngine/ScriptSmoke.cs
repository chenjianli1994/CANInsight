// 脚本发送 - 序列化往返冒烟测试(引用 CANInsight.exe 的公共模型)
using PCAN_Client.ScriptEngine;
using System;
using System.Collections.Generic;
using System.Linq;

class ScriptSmoke
{
    static int _fail;

    static void Check(bool cond, string name)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
        if (!cond) _fail++;
    }

    static int Main()
    {
        // 1. 构造全类型嵌套脚本
        var script = new CanScript
        {
            Name = "冒烟脚本",
            RunLoopCount = 3,
            Trigger = TriggerMode.OnCondition,
            TriggerConditions = new List<ScriptCondition>
            {
                new ScriptCondition
                {
                    Left = new ScriptOperand { Kind = OperandKind.Signal, Channel = 2, MessageId = 0x1A0, SignalName = "EngineSpeed" },
                    Op = ">",
                    Right = new ScriptOperand { Kind = OperandKind.Const, ConstValue = 3000 }
                }
            }
        };
        var loop = new ScriptStep
        {
            Type = ScriptStepType.LoopBlock, Loop = LoopMode.Count, LoopCount = 10,
            Comment = "外层循环"
        };
        loop.Children.Add(new ScriptStep
        {
            Type = ScriptStepType.SendMessage,
            Msg = new ScriptMessageSnapshot
            {
                Id = 0x2B1, Name = "GearCmd", Channel = 1, DataLen = 8,
                DataHex = "11 22 33 44 55 66 77 88",
                SignalExprs = new Dictionary<string, string> { { "GearPos", "$gear" }, { "Speed", "25.5" } },
                Fill = FillMode.SignalIncrement, IncrementSignal = "Speed",
                IncrementStart = 0, IncrementStep = 10, IncrementMax = 100
            },
            RepeatCount = 3, RepeatIntervalMs = 100
        });
        loop.Children.Add(new ScriptStep
        {
            Type = ScriptStepType.WaitCondition, TimeoutMs = 5000, OnTimeout = TimeoutAction.ContinueNext,
            Logic = CondLogicOp.Or,
            Conditions = new List<ScriptCondition>
            {
                new ScriptCondition
                {
                    Left = new ScriptOperand { Kind = OperandKind.Variable, VarName = "gear" },
                    Op = "<",
                    Right = new ScriptOperand { Kind = OperandKind.FrameByte, Channel = 1, MessageId = 0x100, ByteIndex = 3 }
                },
                new ScriptCondition
                {
                    Left = new ScriptOperand { Kind = OperandKind.FrameCount, Channel = 1, MessageId = 0x100 },
                    Op = ">=",
                    Right = new ScriptOperand { Kind = OperandKind.Const, ConstValue = 5 }
                }
            }
        });
        loop.Children.Add(new ScriptStep { Type = ScriptStepType.SetVariable, VarName = "gear", VarSource = VarSourceType.Increment, VarIncrementStep = 1 });
        script.Steps.Add(loop);
        script.Steps.Add(new ScriptStep { Type = ScriptStepType.Delay, DelayMs = 500 });
        script.Steps.Add(new ScriptStep
        {
            Type = ScriptStepType.IfBlock,
            Conditions = new List<ScriptCondition>
            {
                new ScriptCondition
                {
                    Left = new ScriptOperand { Kind = OperandKind.Signal, Channel = 1, MessageId = 0x1A0, SignalName = "EngineSpeed", UseRawValue = true },
                    Op = ">=",
                    Right = new ScriptOperand { Kind = OperandKind.Variable, VarName = "gear" }
                }
            }
        });
        script.Steps[2].Children.Add(new ScriptStep { Type = ScriptStepType.LogMessage, Text = "挡位=$gear" });
        script.Steps[2].Children.Add(new ScriptStep { Type = ScriptStepType.StopScript });

        // 2. 序列化往返
        string json = ScriptMessageHelper.SerializeScript(script);
        var back = ScriptMessageHelper.DeserializeScript(json);

        Check(back.Name == "冒烟脚本", "脚本名称往返");
        Check(back.RunLoopCount == 3, "整体循环次数往返");
        Check(back.Trigger == TriggerMode.OnCondition, "触发方式枚举往返(字符串)");
        Check(json.Contains("\"OnCondition\""), "枚举序列化为可读字符串");
        Check(back.TriggerConditions.Count == 1 && back.TriggerConditions[0].Left.SignalName == "EngineSpeed", "触发条件往返");
        Check(back.Steps.Count == 3, "根步骤数往返");
        var loop2 = back.Steps[0];
        Check(loop2.Type == ScriptStepType.LoopBlock && loop2.LoopCount == 10 && loop2.Children.Count == 3, "循环块及子步骤往返");
        var send = loop2.Children[0];
        Check(send.Msg.Id == 0x2B1 && send.Msg.Channel == 1 && send.RepeatCount == 3, "发送步骤快照往返");
        Check(send.Msg.SignalExprs["GearPos"] == "$gear" && send.Msg.SignalExprs["Speed"] == "25.5", "信号表达式($变量)往返");
        Check(send.Msg.Fill == FillMode.SignalIncrement && send.Msg.IncrementStep == 10, "递增填充参数往返");
        var wait = loop2.Children[1];
        Check(wait.Conditions.Count == 2 && wait.Logic == CondLogicOp.Or && wait.OnTimeout == TimeoutAction.ContinueNext, "等待条件组往返");
        Check(wait.Conditions[0].Right.Kind == OperandKind.FrameByte && wait.Conditions[0].Right.ByteIndex == 3, "帧字节操作数往返");
        Check(back.Steps[2].Children[1].Type == ScriptStepType.StopScript, "IF块内停止步骤往返");
        Check(!json.Contains("RuntimeMessage"), "运行态字段不序列化");

        // 3. DeepClone:Id 全树重生成,数据一致
        var clone = loop2.DeepClone();
        Check(clone.Id != loop2.Id, "DeepClone根Id重生成");
        Check(clone.Children[0].Id != loop2.Children[0].Id, "DeepClone子Id重生成");
        Check(clone.Children[0].Msg.SignalExprs["GearPos"] == "$gear" && clone.Children[0].RepeatCount == 3, "DeepClone数据一致");

        // 4. 操作数文本协议解析
        Check(ScriptOperand.TryParse("@CH2.1A0.EngineSpeed", out var o1) && o1.Kind == OperandKind.Signal && o1.Channel == 2 && o1.MessageId == 0x1A0 && o1.SignalName == "EngineSpeed" && !o1.UseRawValue, "协议:@信号");
        Check(ScriptOperand.TryParse("@RAW:CH1.2B1.GearPos", out var o2) && o2.UseRawValue && o2.MessageId == 0x2B1, "协议:@RAW原始值");
        Check(ScriptOperand.TryParse("$speed", out var o3) && o3.Kind == OperandKind.Variable && o3.VarName == "speed", "协议:$变量");
        Check(ScriptOperand.TryParse("#CNT:CH1.100", out var o4) && o4.Kind == OperandKind.FrameCount && o4.MessageId == 0x100, "协议:#CNT帧数");
        Check(ScriptOperand.TryParse("#BYTE:CH1.100[3]", out var o5) && o5.Kind == OperandKind.FrameByte && o5.ByteIndex == 3, "协议:#BYTE字节");
        Check(ScriptOperand.TryParse("25.5", out var o6) && o6.Kind == OperandKind.Const && Math.Abs(o6.ConstValue - 25.5) < 1e-9, "协议:数字常量");
        Check(!ScriptOperand.TryParse("$1abc", out _), "协议:非法变量名拒绝");
        Check(!ScriptOperand.TryParse("xyz", out _), "协议:非法文本拒绝");
        Check(o1.ToText() == "@CH2.1A0.EngineSpeed" && o5.ToText() == "#BYTE:CH1.100[3]", "协议:ToText 回写一致");

        // 5. 摘要生成(属性面板/树节点显示)
        Check(send.Summary().Contains("0x2B1") && send.Summary().Contains("×3"), "发送摘要");
        Check(wait.Summary().Contains("超时5000ms") && wait.Summary().Contains("继续"), "等待摘要");

        Console.WriteLine(_fail == 0 ? "\n=== 全部通过 ===" : $"\n=== {_fail} 项失败 ===");
        return _fail;
    }
}
