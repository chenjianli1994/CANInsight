using CSScriptLibrary;
using PCAN_Client.CAN_Data;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCAN_Client.CAN_Data
{
    public delegate void MsgReceive(Message message);
    public class DbcHelper
    {
        public DbcFile dbcFile = new DbcFile();
        public string dbcFilePath = "";
        public string path = "";
        public int CrcCheckStatus = 0;

        private string fileBuffer = "";

        private ReaderWriterLockSlim _messageDictLock = new ReaderWriterLockSlim();

        /// <summary>
        /// 重建消息字典
        /// </summary>
        public void RebuildMessageDict()
        {
            _messageDictLock.EnterWriteLock();
            try
            {
                dbcFile.messageDict.Clear();
                foreach (var msg in dbcFile.messages)
                {
                    if (dbcFile.messageDict.ContainsKey(msg.messgeId))
                    {
                        Debug.WriteLine($"警告: 发现重复的Message ID 0x{msg.messgeId:X}");
                        continue;
                    }
                    dbcFile.messageDict[msg.messgeId] = msg;

                    /* 判断校验方式 */
                    if (msg.messgeId == 0x3BA)
                    {
                        foreach (var signal in msg.signals)
                        {
                            if (signal.signalName.Contains("GW_CRC_3BA"))
                            {
                                CrcCheckStatus = 1;
                                break;
                            }
                            else if (signal.signalName.Contains("GW_CheckSum"))
                            {
                                CrcCheckStatus = 2;
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                _messageDictLock.ExitWriteLock();
            }
        }
        public static string FormatSignalResult(string content, int totalLength)
        {
            // 计算可用内容长度（减掉固定字符部分）
            int availableContentLength = totalLength - 5; // 减 [ ] 和两个空格

            // 处理超长内容
            if (content.Length > availableContentLength)
            {
                // 保留最后一个位置用于省略号
                content = content.Substring(0, availableContentLength - 1) + "…";
            }
            // 处理过短内容
            else if (content.Length < availableContentLength)
            {
                // 右侧填充空格补齐
                content = content.PadRight(availableContentLength);
            }

            return "[ 0x" + content + "] ";
        }
        public void CANDataDeal(uint ID, ushort len, byte[] data, ulong us)
        {
            int index = 0;
            int index2 = 0;
            string rawValue = "";
            if (BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
            {
                return;
            }
            //_messageDictLock.EnterReadLock();
            if (!dbcFile.messageDict.ContainsKey(ID))
            {
                return;
            }

            try
            {
                // 直接通过字典查找 Message，无需遍历
                if (dbcFile.messageDict.TryGetValue(ID, out Message message) &&
                    len >= message.messageSize)
                {
                    var parser = new CanSignalParser();
                    var result = parser.ParseSignals(data, message.signals);

                    message.receiveTimeMs = ((double)(us - message.receiveTimeUs) / 1000).ToString("F3") + "ms";
                    message.receiveTimeUs = us;

                    // 更新信号结果
                    index2 = 0;
                    foreach (var item in result)
                    {
                        // 根据信号位宽计算十六进制显示的位数
                        int hexDigits = (int)((message.signals[index2].signalSize + 3) / 4);
                        if (hexDigits < 2) hexDigits = 2;
                        rawValue = message.signals[index2].rawValue.ToString("X" + hexDigits);
                        rawValue = FormatSignalResult(rawValue, hexDigits + 6);
                        if (message.signals[index2].result != item.Value || !message.signals[index2].signalDisplayStr.Contains(rawValue) || message.signals[index2].signalDisplayStr.Equals(""))
                        {
                            message.signals[index2].result = item.Value;
                            try
                            {
                                int enumKey = (int)item.Value;
                                var enumDefs = message.signals[index2].enumDefinitions;
                                if (enumDefs.Count > 0 && enumDefs.ContainsKey(enumKey))
                                {
                                    message.signals[index2].signalDisplayStr = rawValue + enumDefs[enumKey];
                                }
                                else
                                {
                                    if (false == (message.signals[index2].unitStr.Equals("\"\"")) &&
                                       (false == (message.signals[index2].unitStr.Equals("-"))))
                                    {
                                        message.signals[index2].signalDisplayStr = rawValue + message.signals[index2].result.ToString() + " " + message.signals[index2].unitStr;
                                    }
                                    else
                                    {
                                        message.signals[index2].signalDisplayStr = rawValue + message.signals[index2].result.ToString();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (false == (message.signals[index2].unitStr.Equals("\"\"")) &&
                                   (false == (message.signals[index2].unitStr.Equals("-"))))
                                {
                                    message.signals[index2].signalDisplayStr = rawValue + message.signals[index2].result.ToString() + " " + message.signals[index2].unitStr;
                                }
                                else
                                {
                                    message.signals[index2].signalDisplayStr = rawValue + message.signals[index2].result.ToString();
                                }
                            }
                        }

                        if (Main.ChartShowOpenFlag && message.signals[index2].ChartShowFlag)
                        {
                            if (null != Main.chartFromShow)
                            {
                                Main.chartFromShow.AddPoint(message.messgeId, index2, message.signals[index2].result, message.cycleTime);
                            }
                        }
                        else if(!Main.ChartShowOpenFlag && message.signals[index2].ChartShowFlag)
                        {
                            message.signals[index2].ChartShowFlag = false;
                        }
                        index2++;
                    }

                    message.receiveCnt++; // 直接操作找到的 message 对象
                    if(null != message.msgReceive) 
                    {
                        message.msgReceive(message);
                    }
                }
            }
            catch
            {

            }
            finally 
            {
                //_messageDictLock.ExitReadLock();
            }
        }

        public int Parse(string _path)
        {
            int err = 0;

            path = _path;
            dbcFilePath = path;
            err =  FileLoader.Load(path, ref fileBuffer);

            err = StrToDbeFile();
            // 新增：按Message ID排序消息列表
            dbcFile.messages.Sort((m1, m2) => m1.messgeId.CompareTo(m2.messgeId));
            RebuildMessageDict(); // 重新构建字典

            try
            {
                if (Main.canSendOpenFlag)
                {
                    Main.canSend.UpdateDbcTreeview();
                }
                Main.main.UpdateDbcTreeview();
                /* 判断校验方式 */
                foreach(var message in dbcFile.messages)
                {
                    if(message.messgeId == 0x3BA)
                    {
                        foreach (var signal in message.signals)
                        {
                            if(signal.signalName.Contains("GW_CRC_3BA"))
                            {
                                CrcCheckStatus = 1;
                                break;
                            }
                            else if(signal.signalName.Contains("GW_CheckSum"))
                            {
                                CrcCheckStatus = 2;
                                break;
                            }
                        }
                        break;
                    }
                }

            }
            catch { }

            return err;
        }

        private int StrToDbeFile()
        {
            int err = 0;
            string[] bufferAry = null;
            dbcFile = new DbcFile();

            if (fileBuffer == null)
            {
                if (fileBuffer == "")
                {
                    ExceptionHandler.Report("Dbc文件为空");
                }
            }

            bufferAry = fileBuffer.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (bufferAry.Length < 3)
            {
                return ExceptionHandler.Report("Dbc文件格式错误");
            }

            int lineNum = bufferAry.Length;
            bool isMessageValid = false;

            for (int i = 0; i < lineNum; i++)
            {
                string[] lineAry = bufferAry[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (lineAry.Length < 1)
                {
                    continue;
                    //return ExceptionHandler.Report("Dbc文件行格式错误");
                }
                string name = lineAry[0];
                switch (name)
                {
                    case "BA_":
                        {
                            // 解析格式：BA_ "GenMsgCycleTime" BO_ [MessageID] [CycleTime];
                            if (lineAry.Length >= 4 && lineAry[1] == "\"GenMsgCycleTime\"")
                            {
                                uint msgId = Convert.ToUInt32(lineAry[3]);
                                uint cycle = Convert.ToUInt32(lineAry[4].TrimEnd(';'));

                                // 查找对应的Message对象
                                var targetMsg = dbcFile.messages.FirstOrDefault(m => m.messgeId == msgId);
                                if (targetMsg != null)
                                {
                                    targetMsg.cycleTime = cycle;
                                }
                            }
                            break;
                        }

                    case "VAL_TABLE_":

                    break;

                    case "VAL_":
                        {
                            if (name.Equals("VAL_TABLE_"))
                            {
                                break;
                            }
                            string valLine = bufferAry[i].ToString();

                            // 1. 移除 "VAL_" 和结尾分号，保留核心内容
                            string trimmedLine = Regex.Replace(valLine, @"^\s*VAL_\s*|\s*;\s*$", "");

                            // 2. 正则匹配：分解 MessageID、SignalName 和键值对
                            var match = Regex.Match(trimmedLine,
                                @"^\s*(\d+)\s+(\w+)\s+(.*)$",
                                RegexOptions.Singleline);

                            if (!match.Success)
                            {
                                break;
                            }
                                //throw new FormatException("VAL_ 行格式错误");

                            uint msgId = Convert.ToUInt32(match.Groups[1].Value);  // 867
                            string signalName = match.Groups[2].Value;            // TMC_WorkModeStsFB
                            string kvPairs = match.Groups[3].Value;                // 5 "AC&Heat ON" 4 "Vent ON" ...

                            // 3. 提取所有键值对
                            var kvMatches = Regex.Matches(kvPairs,
                                @"(\d+)\s+""((?:[^""]|\\"")*)""",  // 匹配格式：数字 + 带引号的字符串
                                RegexOptions.Singleline);

                            // 查找对应的 Message 和 Signal
                            //Console.WriteLine($"Message ID: {msgId}, Signal: {signalName}");
                            var targetMsg = dbcFile.messages.FirstOrDefault(m => m.messgeId == msgId);
                            if (targetMsg != null)
                            {
                                var targetSignal = targetMsg.signals.FirstOrDefault(s =>s.signalName.Trim().Equals(signalName.Trim(), StringComparison.OrdinalIgnoreCase));
                                if (targetSignal != null)
                                {
                                    foreach (Match kvMatch in kvMatches)
                                    {
                                        double key = Convert.ToDouble(kvMatch.Groups[1].Value);
                                        string value = kvMatch.Groups[2].Value.Replace("\\\"", "\""); // 处理转义符
                                        targetSignal.enumDefinitions[key] = value;
                                    }
                                }
                            }

                            //if(0x363 == msgId)
                            //{
                            //    // 输出结果
                            //    Console.WriteLine($"Message ID: {msgId}, Signal: {signalName}");
                            //    foreach (var kv in enumDefinitions)
                            //    {
                            //        Console.WriteLine($"{kv.Key} => {kv.Value}");
                            //    }
                            //}
                            break;
                        }
                    case "CM_":
                        {
                            Comment cmt = new Comment();
                            cmt.comment = bufferAry[i];
                            dbcFile.comments.Add(cmt);
                            // 解析信号注释
                            // 格式: CM_ SG_ [MessageID] [SignalName] "[Comment]";
                            if (lineAry.Length >= 5 && lineAry[1] == "SG_")
                            {
                                uint msgId = Convert.ToUInt32(lineAry[2]);
                                string signalName = lineAry[3];

                                // 合并注释内容（可能包含空格）
                                string comment = string.Join(" ", lineAry, 4, lineAry.Length - 4);
                                comment = comment.Trim('"', ';'); // 移除引号和分号

                                // 查找对应的消息和信号
                                var targetMsg = dbcFile.messages.FirstOrDefault(m => m.messgeId == msgId);
                                if (targetMsg != null)
                                {
                                    var targetSignal = targetMsg.signals.FirstOrDefault(s =>
                                        s.signalName.Trim().Equals(signalName.Trim(), StringComparison.OrdinalIgnoreCase));
                                    if (targetSignal != null)
                                    {
                                        targetSignal.Comment = comment;
                                    }
                                }
                            }
                            // 解析消息注释（可选）
                            // 格式: CM_ BO_ [MessageID] "[Comment]";
                            else if (lineAry.Length >= 4 && lineAry[1] == "BO_")
                            {
                                uint msgId = Convert.ToUInt32(lineAry[2]);
                                string comment = string.Join(" ", lineAry, 3, lineAry.Length - 3);
                                comment = comment.Trim('"', ';');

                                var targetMsg = dbcFile.messages.FirstOrDefault(m => m.messgeId == msgId);
                                if (targetMsg != null)
                                {
                                    targetMsg.messageComment = comment;
                                }
                            }
                            break;
                        }
                    case "BU_:":
                        {
                            for (int j = 1; j < (lineAry.Length); j++)
                            {
                                dbcFile.nodes.Add(lineAry[j]);
                            }
                            break;
                        }
                    case "BO_":
                        {
                            Message message = new Message();
                            uint id = Convert.ToUInt32(lineAry[1]);
                            //_messageDictLock.EnterReadLock();
                            // 检查 ID 是否已存在
                            if (dbcFile.messageDict.ContainsKey(id))
                            {
                                throw new Exception($"DBC 文件错误: ID 0x{id:X} 重复");
                            }
                            //_messageDictLock.ExitReadLock();
                            //跳过默认的消息
                            if (id == 0xC0000000)
                            {
                                isMessageValid = false;
                                break;
                            }
                            else
                            {
                                isMessageValid = true;
                            }
                            //最高位为1的为扩展帧
                            if ((id & 0x80000000) != 0)
                            {
                                id &= 0x7FFFFFFF;
                                message.isExternId = true;
                            }
                            else
                            {
                                message.isExternId = false;
                            }
                            message.messgeId = id;
                            message.messageName = lineAry[2].Substring(0, lineAry[2].Length - 1);
                            if(!message.messageName.Contains(id.ToString("X2")))
                            {
                                message.messageName += "_0x" + id.ToString("X2");
                            }
                            message.messageSize = Convert.ToUInt32(lineAry[3]);
                            message.transmitter = lineAry[4];
                            //_messageDictLock.EnterWriteLock();
                            dbcFile.messageDict[id] = message; // 加入字典
                            //_messageDictLock.ExitWriteLock();
                            dbcFile.messages.Add(message);
                            break;
                        }
                    case "SG_":
                        {
                            if (isMessageValid)
                            {
                                uint byteOffset = 0;
                                Signal signal = new Signal();
          
                                signal.signalName = lineAry[1];
                                if (lineAry[2] == ":")
                                {
                                    signal.multiplexerIndicator = -2;
                                    byteOffset = 0;
                                }
                                else
                                {
                                    byteOffset = 1;
                                    if (lineAry[2][0] == 'M')
                                    {
                                        signal.multiplexerIndicator = -1;
                                    }
                                    else if (lineAry[2][0] == 'm')
                                    {
                                        signal.multiplexerIndicator = Convert.ToInt32(lineAry[2].Substring(1, lineAry[2].Length - 1));
                                    }
                                    else
                                    {
                                        return ExceptionHandler.Report("Dbc信号格式错误");
                                    }
                                }

                                string[] sp = lineAry[3 + byteOffset].Split(new char[] { '|', '@' }, StringSplitOptions.RemoveEmptyEntries);

                                signal.startBit = Convert.ToUInt32(sp[0]);
                                signal.signalSize = Convert.ToUInt32(sp[1]);
                                if (sp[2][0] == '0')
                                {
                                    signal.byteOrder = 1;
                                }
                                else if (sp[2][0] == '1')
                                {
                                    signal.byteOrder = 0;
                                }
                                if (sp[2][1] == '+')
                                {
                                    signal.valueType = 0;
                                }
                                else if (sp[2][1] == '-')
                                {
                                    signal.valueType = 1;
                                }

                                string[] sp1 = lineAry[4 + byteOffset].Split(new char[] { '(', ',', ')' }, StringSplitOptions.RemoveEmptyEntries);
                                signal.factor = Convert.ToDouble(sp1[0]);
                                signal.offset = Convert.ToDouble(sp1[1]);

                                string[] sp2 = lineAry[5 + byteOffset].Split(new char[] { '[', '|', ']' }, StringSplitOptions.RemoveEmptyEntries);
                                signal.minimum = Convert.ToDouble(sp2[0]);
                                signal.maximum = Convert.ToDouble(sp2[1]);

                                signal.unitStr = (lineAry[6 + byteOffset]).Trim('"');
                                signal.receivers = lineAry[7 + byteOffset].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                                dbcFile.messages[dbcFile.messages.Count - 1].signals.Add(signal);
                            }
                            break;
                        }
                }
            }
            return err;
        }

        public Message GetMessageById(uint messageId)
        {
            _messageDictLock.EnterReadLock();
            try
            {
                return dbcFile.messageDict.TryGetValue(messageId, out Message message) ? message : null;
            }
            finally
            {
                _messageDictLock.ExitReadLock();
            }
        }

        public Message GetMessageByMsgName(string messageName)
        {
            var messageDef = dbcFile.messages.FirstOrDefault(m => m.messageName != null && m.messageName.Contains(messageName));
            if (messageDef == null)
            {
                return null;
            }
            else
            {
                return messageDef;
            }
        }
        public void GetMessageIndexAndSignalIndexBySigName(string signalName, ref int messageIndex, ref int signalIndex, ref bool flag)
        {
            for(int i=0; i< dbcFile.messages.Count; i++)
            {
                for(int j=0; j< dbcFile.messages[i].signals.Count; j++)
                {
                    if (dbcFile.messages[i].signals[j].signalName.Equals(signalName))
                    {
                        messageIndex = i;
                        signalIndex = j;
                        flag = true;
                        return;
                    }
                }
                for (int j = 0; j < dbcFile.messages[i].signals.Count; j++)
                {
                    if (dbcFile.messages[i].signals[j].signalName.Contains(signalName))
                    {
                        messageIndex = i;
                        signalIndex = j;
                        flag = true;
                        return;
                    }
                }
            }
        }

        public void GetSignalIndexBySigName(string signalName, int messageIndex, ref int signalIndex, ref bool flag)
        {
            for (int j = 0; j < dbcFile.messages[messageIndex].signals.Count; j++)
            {
                if (dbcFile.messages[messageIndex].signals[j].signalName.Equals(signalName))
                {
                    signalIndex = j;
                    flag = true;
                    return;
                }
            }
            for (int j = 0; j < dbcFile.messages[messageIndex].signals.Count; j++)
            {
                if (dbcFile.messages[messageIndex].signals[j].signalName.Contains(signalName))
                {
                    signalIndex = j;
                    flag = true;
                    return;
                }
            }
        }
        // 发送方法
        public void SendCanMessage(uint messageId)
        {
            var messageDef = dbcFile.messages.FirstOrDefault(m => m.messgeId == messageId);
            if (messageDef == null) return;

            if (messageDef.updateFlag || null == messageDef.sendBuf)
            {
                // 按报文实际长度编码：经典CAN(≤8)保持8字节，CAN FD长报文(如32/64)扩展到messageSize
                messageDef.sendBuf = CanMessageBuilder.EncodeSignals(messageDef.signals, Math.Max(8, (int)messageDef.messageSize));
                // 编码后复位：信号值未变化时后续周期帧复用编码结果（CRC/RollingCounter仍逐帧重算）
                messageDef.updateFlag = false;
            }
            else
            {
                /* empty */
            }
            if(1 == CrcCheckStatus)
            {
                util.FrameCrcCal.messageCrcCal(messageId, (int)messageDef.messageSize, ref messageDef.sendBuf, messageDef.aliveCount);
            }
            else if (2 == CrcCheckStatus)
            {
                if((0x3BA == messageDef.messgeId) ||
                   (0x3BB == messageDef.messgeId) ||
                   (0x3BD == messageDef.messgeId))
                {
                    byte checkSum = 0;
                    for (int i = 0; i < 7; i++)
                    {
                        checkSum = (byte)(checkSum + messageDef.sendBuf[i]);
                    }
                    messageDef.sendBuf[7] = (byte)(checkSum ^ 0xFF);
                }
            }
            CAN_API.CAN_API.CanTransmit(messageId, (ushort)messageDef.sendBuf.Length, messageDef.sendBuf);
            messageDef.sendCnt++;
        }

        public void SendCanMessage(Message msg)
        {
            CAN_API.CAN_API.CanTransmit(msg.messgeId, (ushort)msg.sendBuf.Length, msg.sendBuf);
            msg.sendCnt++;
        }
    }

    public class DbcFile
    {
        public List<string> nodes = new List<string>();
        public List<Message> messages = new List<Message>();
        public List<Comment> comments = new List<Comment>();
        public Dictionary<uint, Message> messageDict = new Dictionary<uint, Message>();
    }


    public class Message
    {
        public uint messgeId = 0;
        public bool isExternId = false;
        public string messageName = "";
        public uint messageSize = 0;
        public string transmitter = "";
        public string messageComment = ""; // 报文描述（DBC CM_ BO_）
        public uint cycleTime = 0; // 新增报文周期字段（单位：ms）
        public List<Signal> signals = new List<Signal>();
        public UInt32 receiveCnt = 0;
        public UInt32 ChartFromReceiveCnt = 0;
        public bool sendFalg  = false;
        public bool enableFlag = false;
        public ulong sendCnt = 0;
        public DataTable dataTable = null;
        public long NextSendTime = 0;
        public int aliveCount = 0;
        public ulong receiveTimeUs = 0;
        public string receiveTimeMs = "";
        public bool updateFlag = true;
        public byte[] sendBuf = null;
        public MsgReceive msgReceive = null;
    }

    public class Signal
    {
        public string signalName = "";
        public int multiplexerIndicator = -2;//-2:普通信号；-1：复用选择信号；0~N：复用信号
        public uint startBit = 0;
        public uint signalSize = 0;
        public uint byteOrder = 0;//0：Intel；1：Motorola
        public uint valueType = 0;//0：unsigned；1：signed
        public double factor = 0;
        public double offset = 0;
        public double minimum = 0;
        public double maximum = 0;
        public long rawValue = 0;
        public string[] receivers;
        public double result = 0;
        public string unitStr = "";
        public string signalDisplayStr = "";
        // 新增：枚举值定义（原始值 -> 描述）
        public Dictionary<double, string> enumDefinitions = new Dictionary<double, string>();

        public double cmdValue { get; set; }
        public string Comment { get; set; } = "";
        public bool ChartShowFlag = false;
    }

    public class Comment
    {
        public string comment ="";
    }

    public class CanSignalParser
    {
        private ulong ExtractIntelValue(byte[] data, int startBit, int size)
        {
            ulong result = 0;

            for (int i = 0; i < size; i++)
            {
                int byteIndex = (startBit + i) / 8;
                int bitIndex = (startBit + i) % 8;

                if (byteIndex < data.Length)
                {
                    int bitValue = (data[byteIndex] >> bitIndex) & 1;
                    result |= (ulong)bitValue << i;
                }
            }

            return result;
        }

        private ulong ExtractMotorolaValue(byte[] data, int startBit, int size)
        {
            ulong result = 0;

            if ((startBit % 8 + 1) >= size)/* 不需要跨字节 */
            {
                int endbit = (startBit - size + 1);
                result = ((ulong)data[startBit / 8] >> (endbit % 8)) & ((1UL << size) - 1);
            }
            else
            {
                int firstNum = startBit % 8; //4
                int firstRow = startBit / 8;

                for (int i=0; i<size; i++)
                {
                    if (0 != (data[firstRow] & (1 << firstNum)))
                    {
                        result |= 1;
                    }
                    else
                    {
                        result |= 0;
                    }
                    result <<= 1;
                    firstNum--;
                    if(firstNum < 0)
                    {
                        firstNum = 7;
                        firstRow++;
                    }
                    else
                    {
                        /* empty */
                    }
                }
                result >>= 1;
            }

            return result;
        }
        private ulong ExtractRawValue(byte[] data, Signal signal)
        {
            int startBit = (int)signal.startBit;
            int size = (int)signal.signalSize;

            if (signal.byteOrder == 0) // Intel格式（小端）
            {
                return ExtractIntelValue(data, startBit, size);
            }
            else // Motorola格式（大端）
            {
                return ExtractMotorolaValue(data, startBit, size);
            }
        }
        
        public Dictionary<string, double> ParseSignals(byte[] canData, List<Signal> signals)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            int multiplexerValue = -1;

            // 先解析复用选择信号（如果有）
            Signal muxSwitch = signals.Find(s => s.multiplexerIndicator == -1);
            if (muxSwitch != null)
            {
                multiplexerValue = (int)ExtractRawValue(canData, muxSwitch);
            }

            foreach (var signal in signals)
            {
                // 跳过复用选择信号（已处理）
                if (signal.multiplexerIndicator == -1) continue;

                // 处理复用逻辑
                if (signal.multiplexerIndicator != -2)
                {
                    if (signal.multiplexerIndicator != multiplexerValue) continue;
                }

                // 提取并转换信号
                try
                {
                    double physicalValue = ConvertToPhysicalValue(canData, signal);
                    result.Add(signal.signalName, physicalValue);
                }
                catch (Exception ex)
                {
                    //Console.WriteLine($"解析信号 {signal.signalName} 失败: {ex.Message}");
                }
            }

            return result;
        }

        private double ConvertToPhysicalValue(byte[] data, Signal signal)
        {
            ulong rawValue = ExtractRawValue(data, signal);

            // 处理有符号数值
            long signedValue = (signal.valueType == 1) ?
                SignExtend(rawValue, (int)signal.signalSize) :
                (long)rawValue;

            signal.rawValue = signedValue;
            // 应用因子和偏移量
            double physicalValue = signedValue * signal.factor + signal.offset;

            // 范围检查
            if (physicalValue < signal.minimum || physicalValue > signal.maximum)
            {
                //Console.WriteLine($"警告: {signal.signalName} 值 {physicalValue} 超出范围 [{signal.minimum}, {signal.maximum}]");
            }

            return physicalValue;
        }

        // 有符号数符号扩展
        private long SignExtend(ulong value, int bitLength)
        {
            // 参数校验（确保 bitLength 有效）
            if (bitLength < 1 || bitLength > 64)
                throw new ArgumentException("bitLength 必须在 1-64 之间");

            // 使用 ulong 类型计算掩码
            ulong mask = (1UL << (bitLength - 1));

            // 符号扩展逻辑（全部使用 ulong 运算）
            ulong extended = (value ^ mask) - mask;

            // 转换为有符号的 long
            return (long)extended;
        }
    }

    public class CanMessageBuilder
    {
        /// <summary>
        /// 将Signal对象的cmdValue编码为CAN数据帧[7,8](@ref)
        /// </summary>
        /// <param name="dataLen">数据帧字节数：经典CAN为8，CAN FD长报文按报文实际长度（如32/64）</param>
        public static byte[] EncodeSignals(List<Signal> signals, int dataLen = 8)
        {
            byte[] data = new byte[dataLen];
            int muxValue = -1;

            // 优先处理复用选择信号
            var muxSignal = signals.FirstOrDefault(s => s.multiplexerIndicator == -1);
            if (muxSignal != null)
            {
                muxValue = (int)ConvertToRawValue(muxSignal.cmdValue, muxSignal);
                EncodeSingleSignal(data, muxSignal, muxValue);
            }

            foreach (var signal in signals)
            {
                if (signal.multiplexerIndicator == -1) continue; // 跳过复用选择信号

                // 复用信号筛选逻辑
                if (signal.multiplexerIndicator != -2 &&
                    signal.multiplexerIndicator != muxValue) continue;

                double rawValue = ConvertToRawValue(signal.cmdValue, signal);
                EncodeSingleSignal(data, signal, (long)rawValue);
            }
            return data;
        }

        /// <summary>
        /// 工程值转原始值（考虑因子和偏移量）[5](@ref)
        /// </summary>
        public static double ConvertToRawValue(double cmdValue, Signal signal)
        {
            return (cmdValue - signal.offset) / signal.factor;
        }

        // 单个信号编码到字节数组
        private static void EncodeSingleSignal(byte[] data, Signal signal, long rawValue)
        {
            int startBit = (int)signal.startBit;
            int size = (int)signal.signalSize;

            if (signal.byteOrder == 0) // Intel格式
            {
                for (int i = 0; i < size; i++)
                {
                    int byteIndex = (startBit + i) / 8;
                    int bitIndex = (startBit + i) % 8;
                    if (byteIndex >= data.Length) break;

                    byte mask = (byte)(1 << bitIndex);
                    data[byteIndex] &= (byte)~mask; // 清空目标位
                    if (((rawValue >> i) & 1) != 0)
                        data[byteIndex] |= mask;     // 设置位值
                }
            }
            else // Motorola格式 - 根据ExtractMotorolaValue的逻辑重写
            {
                // 检查是否不需要跨字节
                if ((startBit % 8 + 1) >= size)
                {
                    // 不跨字节的情况
                    int endbit = (startBit - size + 1);
                    int byteIndex = startBit / 8;

                    // 清除目标位
                    byte mask = (byte)(((1UL << size) - 1) << (endbit % 8));
                    data[byteIndex] &= (byte)~mask;

                    // 设置值
                    data[byteIndex] |= (byte)((rawValue << (endbit % 8)) & mask);
                }
                else
                {
                    // 跨字节的情况
                    int firstNum = startBit % 8; // 起始字节内的位索引
                    int firstRow = startBit / 8; // 起始字节索引

                    // 从高位到低位处理
                    for (int i = 0; i < size; i++)
                    {
                        int bitIndex = size - 1 - i; // 从最高位开始
                        int bitValue = (int)((rawValue >> bitIndex) & 1);

                        if (firstNum < 0)
                        {
                            firstNum = 7;
                            firstRow++;
                        }

                        if (firstRow >= data.Length) break;

                        // 更新目标位
                        if (bitValue == 1)
                        {
                            data[firstRow] |= (byte)(1 << firstNum);
                        }
                        else
                        {
                            data[firstRow] &= (byte)~(1 << firstNum);
                        }

                        firstNum--;
                    }
                }
            }
        }
    }
}
