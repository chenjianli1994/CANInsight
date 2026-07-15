using PCAN_Client.UTIL;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CSScriptLibrary;
using System.Linq;
using Newtonsoft.Json.Linq;
using vxlapi_NET;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Reflection;

namespace PCAN_Client.Update
{
    internal class Download
    {
        //[DllImport("winmm")]
        //static extern void timeBeginPeriod(int t);
        //[DllImport("winmm")]
        //static extern void timeEndPeriod(int t);

        public readonly static byte[] RepairShopCode = new byte[]  /* 外部工具序列号 */
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 01
        };
        public static int PackingLength = 1022;
        public static int delayus = 0;

        private struct FlowControlStatus
        {
            public byte FS;      // 流控状态 (0=溢出, 1=等待, 2=继续发送)
            public byte BS;      // 块大小 (0=无限制)
            public byte STmin;   // 最小帧间隔时间
        }

        public struct CmdMessageType
        {
            public Boolean Physical;           /* 是否使用物理地址发送 */
            public Boolean Inhibition;         /* 是否抑制响应 */
            public byte[] CmdData;            /* 发送指令 */
            public byte[] RspData;            /* 回复指令 */
            public UInt16 DelayMs;             /* 等待延时 */
            public UInt16 CmdAfterDelayMs;     /* 接收到响应后延时一定时间 */
            public Func<string, string> Function;      /* 回调函数 */
        };
        static Stopwatch timeSwNew = new Stopwatch();
        static TPCANMsg tPCANMsg;
        public static List<CmdMessageType> CmdMessages;
        public static CmdMessageType CmdMessage;
        public static Boolean RspResult = false;
        public static UInt32 Serv27Key = 0;

        public static Boolean SoftVersionChecked = false;
        public static Boolean HardVersionChecked = false;
        public static Boolean BootVersionChecked = false;
        public static Boolean SoftVersionInputNull = false;
        public static Boolean HardVersionInputNull = false;
        public static Boolean BootVersionInputNull = false;

        public static ManualResetEvent responseEvent = new ManualResetEvent(false);
        private static ManualResetEvent sendControlEvent = new ManualResetEvent(false);
        /* 流控帧 */
        private static ManualResetEvent flowControlEvent = new ManualResetEvent(false);
        private static FlowControlStatus currentFlowControl;
        private static readonly object flowControlLock = new object();
        private static bool flowControlReceived = false;
        private static int blockCounter = 0; // 当前块内已发送帧数
        private static ManualResetEvent MultiMessageCANSchedulerEvent = new ManualResetEvent(false);
        private static byte[] byteReverse(byte[] data)
        {
            byte[] bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                bytes[i] = data[data.Length - i - 1];
            }
            return bytes;
        }
        public static void CmdMessageAdd(ref List<CmdMessageType> CmdMessages, Boolean Physical, Boolean Inhibition, byte[] CmdData, byte[] RspData, UInt16 delayMs)
        {
            CmdMessageType cmdMessageTemp;
            cmdMessageTemp = new CmdMessageType();

            cmdMessageTemp.Physical = Physical;
            cmdMessageTemp.Inhibition = Inhibition;
            cmdMessageTemp.CmdData = CmdData;
            cmdMessageTemp.RspData = RspData;
            cmdMessageTemp.DelayMs = delayMs;
            cmdMessageTemp.Function = null;
            CmdMessages.Add(cmdMessageTemp);
        }
        public static void CmdMessageAdd(ref List<CmdMessageType> CmdMessages, Boolean Physical, Boolean Inhibition, byte[] CmdData, byte[] RspData, UInt16 delayMs, UInt16 CmdAfterDelayMs)
        {
            CmdMessageType cmdMessageTemp;
            cmdMessageTemp = new CmdMessageType();

            cmdMessageTemp.Physical = Physical;
            cmdMessageTemp.Inhibition = Inhibition;
            cmdMessageTemp.CmdData = CmdData;
            cmdMessageTemp.RspData = RspData;
            cmdMessageTemp.DelayMs = delayMs;
            cmdMessageTemp.Function = null;
            cmdMessageTemp.CmdAfterDelayMs = CmdAfterDelayMs;
            CmdMessages.Add(cmdMessageTemp);
        }

        public static void CmdMessageAdd(ref List<CmdMessageType> CmdMessages, Boolean Physical, Boolean Inhibition, byte[] CmdData, byte[] RspData, UInt16 delayMs, Func<string, string> functionToPass)
        {
            CmdMessageType cmdMessageTemp;
            cmdMessageTemp = new CmdMessageType();

            cmdMessageTemp.Physical = Physical;
            cmdMessageTemp.Inhibition = Inhibition;
            cmdMessageTemp.CmdData = CmdData;
            cmdMessageTemp.RspData = RspData;
            cmdMessageTemp.DelayMs = delayMs;
            cmdMessageTemp.Function = functionToPass;
            CmdMessages.Add(cmdMessageTemp);
        }

        public static UInt32 GetUDSServer27Key(ref byte[] buf)
        {
            UInt32 result = 0;
            // 创建新的应用程序域
            AppDomain domain = AppDomain.CreateDomain("DynamicCodeDomain");
            // 配置为使用可回收程序集
            CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;

            try
            {
                // 动态编译并执行代码
                string code = @"
                using System;
                public class Script
                {
                    public void GetUDSServ27Key(ref byte[] buf, out UInt32 result)
                    {
                        UInt32 result;
                        UInt32 seed;
                        UInt32 y;
                        UInt32 z;
                        UInt32 sum;
                        uint n;
                        UInt32 key1;
                        UInt32[] key2 = new UInt32[4] { 0x4fe87269, 0x6bc361d8, 0x9b127d51, 0x5ba41903 }; /* 128 bits */

                        if (0x01 == buf[2]) /* 拓展功能解锁 */
                        {
                            seed = (UInt32)(buf[6] + (buf[5] << 8) + (buf[4] << 16) + (buf[3] << 24));
                            if (0 == seed) /* 已解锁状态 */
                            {
                                result = 0;
                                return result;
                            }
                            else
                            {
                                /* empty */
                            }
                            key1 = ((((seed >> 4) ^ seed) << 3) ^ seed);
                            result = key1;
                            return result;
                        }
                        else if (0x11 == buf[2]) /* 编程解锁 */
                        {
                            seed = BitConverter.ToUInt32(buf, 3);
                            if (0 == seed) /* 已解锁状态 */
                            {
                                result = 0;
                                return result;
                            }
                            else
                            {
                                /* empty */
                            }
                            z = 0;
                            sum = 0; /* y = LOW_PART, z = HIGH_PART */
                            n = 64; /* number of iterations */
                            y = seed;
                            while (n > 0)
                            { /* encrypt */
                                y += (((z << 4) ^ (z >> 5)) + z) ^ (sum + key2[sum & 3]);
                                sum += (UInt32)0x8f750a1dU;
                                z += (((y << 4) ^ (y >> 5)) + y) ^ (sum + key2[(sum >> 11) & 3]);
                                n--;
                            }
                            result = z;
                            return result;
                        }
                        result = 0;
                        return result;
                    }
                }";
                dynamic script = CSScript.Evaluator.LoadCode(code);
                script.GetUDSServ27Key(ref buf, out result);
            }
            finally
            {
                // 卸载应用程序域
                AppDomain.Unload(domain);
            }
            return result;
        }

        public static UInt32 GetUDSServ27Key(ref byte[] buf)
        {
            UInt32 result;
            UInt32 seed;
            UInt32 y;
            UInt32 z;
            UInt32 sum;
            uint n;
            UInt32 key1;
            UInt32[] key2 = new UInt32[4] { 0x4fe87269, 0x6bc361d8, 0x9b127d51, 0x5ba41903 }; /* 128 bits */

            if (0x01 == buf[1]) /* 拓展功能解锁 */
            {
                seed = (UInt32)(buf[5] + (buf[4] << 8) + (buf[3] << 16) + (buf[2] << 24));
                if (0 == seed) /* 已解锁状态 */
                {
                    result = 0;
                    return result;
                }
                else
                {
                    /* empty */
                }
                key1 = ((((seed >> 4) ^ seed) << 3) ^ seed);
                result = key1;
                return result;
            }
            else if (0x11 == buf[1]) /* 编程解锁 */
            {
                seed = BitConverter.ToUInt32(buf, 2);
                if (0 == seed) /* 已解锁状态 */
                {
                    result = 0;
                    return result;
                }
                else
                {
                    /* empty */
                }
                z = 0;
                sum = 0; /* y = LOW_PART, z = HIGH_PART */
                n = 64; /* number of iterations */
                y = seed;
                while (n > 0)
                { /* encrypt */
                    y += (((z << 4) ^ (z >> 5)) + z) ^ (sum + key2[sum & 3]);
                    sum += (UInt32)0x8f750a1dU;
                    z += (((y << 4) ^ (y >> 5)) + y) ^ (sum + key2[(sum >> 11) & 3]);
                    n--;
                }
                result = z;
                return result;
            }
            result = 0;
            return GetUDSServer27Key(ref buf);
            //return result;
        }

        public static UInt32 GetUDSServ27Key_VQ(ref byte[] buf)
        {
            UInt32 result;
            UInt32 seed;
            UInt32 y;
            UInt32 z;
            UInt32 sum;
            uint n;
            UInt32 key1;
            UInt32[] key2 = new UInt32[4] { 0x4fe87269, 0x6bc361d8, 0x9b127d51, 0x5ba41903 }; /* 128 bits */

            if (0x01 == buf[1]) /* 拓展功能解锁 */
            {
                seed = (UInt32)(buf[5] + (buf[4] << 8) + (buf[3] << 16) + (buf[2] << 24));
                if (0 == seed) /* 已解锁状态 */
                {
                    result = 0;
                    return result;
                }
                else
                {
                    /* empty */
                }
                key1 = ((((seed >> 4) ^ seed) << 3) ^ seed);
                result = key1;
                return result;
            }
            else if (0x11 == buf[1]) /* 编程解锁 */
            {
                seed = BitConverter.ToUInt32(buf, 2);
                //seed = 0xAD249f67;
                byte[] Seed = new byte[4];
                byte[] Const = new byte[4];
                byte[] Seed2 = new byte[4];
                byte[] Key1 = new byte[4];
                byte[] Key2 = new byte[4];
                byte[] Key = new byte[4];
                int i;
                int j = 3;
                UInt32 mask = 0x000025C1;
                Seed[0] = (byte)((seed & 0xff000000) >> 24);
                Seed[1] = (byte)((seed & 0x00ff0000) >> 16);
                Seed[2] = (byte)((seed & 0x0000ff00) >> 8);
                Seed[3] = (byte)(seed & 0x000000ff);
                Const[3] = (byte)((mask & 0xff000000) >> 24);
                Const[2] = (byte)((mask & 0x00ff0000) >> 16);
                Const[1] = (byte)((mask & 0x0000ff00) >> 8);
                Const[0] = (byte)(mask & 0x000000ff);

                for (i = 0; i < 4; i++)
                {
                    Key1[i] = (byte)(Seed[i] ^ Const[i]);
                    Seed2[i] = Seed[j];
                    Key2[i] = (byte)(Seed2[i] ^ Const[i]);
                    Key[i] = (byte)(Key1[i] + Key2[i] + 0x05);
                    j--;
                }

                result = ((((UInt32)Key[0]) & 0x000000ff) + ((((UInt32)Key[1]) << 8) & 0x0000ff00) + ((((UInt32)Key[2]) << 16) & 0x00ff0000) + ((((UInt32)Key[3]) << 24) & 0xff000000));
                return result;
            }
            result = 0;
            return result;
        }

        public static byte[] GetUDSServ27Key_VQ_New(ref byte[] buf)
        {
            byte[] DF = Signer.GenerateSecureRandomBytes(16);
            byte[] sk = null;
            byte[] result = null;

            try
            {
                if (0x01 == buf[1])
                {
                    sk = Signer.ComputeCMAC(DF, BaseParamter.BaseKeyBuf_uds);
                }
                else if (0x11 == buf[1])
                {
                    sk = Signer.ComputeCMAC(DF, BaseParamter.BaseKeyBuf_program);
                }
                byte[] seed = new byte[16];
                Array.Copy(buf, 2, seed, 0, 16);
                var token = Signer.ComputeCMAC(seed, sk);
                result = new byte[32];
                Array.Copy(DF, 0, result, 0, 16);
                Array.Copy(token, 0, result, 16, 16);

                Console.WriteLine(BitConverter.ToString(DF).Replace("-", ""));
                Console.WriteLine(BitConverter.ToString(token).Replace("-", ""));
            }
            catch { }

            return result;
        }
        public static void UDS_FluidicFrame()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
        }

        public static Boolean BufCompare(ref byte[] sourceBuf, ref byte[] destBuf, int len)
        {
            Boolean result = true;
            try
            {
                for (int i = 0; i < len; i++)
                {
                    if (sourceBuf[i] != destBuf[i])
                    {
                        result = false;
                        break;
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        /* 接收长数据 */
        public static byte[] ReceiveBuf = null;
        public static int ReceiveCnt = 0;
        public static int ReceiveIndex = 0;
        public static Boolean ReceiveFlag = false;
        public static Boolean ReceiveFinished = false;
        public static Boolean FluidicFrameFlag = false;
        public static void Download_RxData(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            if (BaseParamter.UDS_RX_ID == msg.ID)
            {
                DataReceive(msg, timesamp);
            }
        }

        public static string GetDownloadShowText(ref CmdMessageType cmdMessage, ref byte[] buf, int index, int ms)
        {
            //string str = "";
            //if (cmdMessage.Physical)
            //{
            //    str = "Tx   ID: 0x" + BaseParamter.UDS_TX_ID.ToString("X2") + " index:" + index.ToString("D4") + "  " + ms.ToString("D4") + "ms  DATA:  ";
            //}
            //else
            //{
            //    str = "Tx   ID: 0x" + BaseParamter.UDS_FunTX_ID.ToString("X2") + " index:" + index.ToString("D4") + "  " + ms.ToString("D4") + "ms   DATA:  ";
            //}

            //str += buf[0].ToString("X2");
            //for (int i = 1; i < buf.Length; i++)
            //{
            //    str += "," + buf[i].ToString("X2");
            //    if (i >= 20)
            //    {
            //        str += "…";
            //        break;
            //    }
            //}
            //str += "\r\n";
            //return str;
            var id = cmdMessage.Physical ? BaseParamter.UDS_TX_ID : BaseParamter.UDS_FunTX_ID;
            var hex = BitConverter.ToString(buf, 0, Math.Min(buf.Length, 20))       // 自带分隔符“-”
                                  .Replace("-", ",") + (buf.Length > 20 ? "…" : "");
            return $"Tx   ID: 0x{id:X2}  index:{index:D4}  {ms:D4}ms   DATA:  {hex}\r\n";
        }

        private static void Delay_ms(int ms)
        {
            Thread.Sleep(ms);
        }
        private static MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();
        public static void MngDownloadHandle(DataTable dataTable, int delayus)
        {
            Download.delayus = delayus;
            try
            {
                Boolean Result = true;
                var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                var ts2 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                var ts3 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                var ts4 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                SoftVersionChecked = false;
                HardVersionChecked = false;
                BootVersionChecked = false;
                var Cmd_8eStopWatch = new Stopwatch();
                int Cmd_8eCount = 2000;
                Cmd_8eStopWatch.Start();
                //timeBeginPeriod(1);
                currentFlowControl.BS = 0;
                currentFlowControl.FS = 0;
                currentFlowControl.STmin = 0;
                Task task = new Task(() =>
                {
                    long end;
                    Download.MngCmdMessageLoad(dataTable);
                    for (int i = 0; i < CmdMessages.Count; i++)
                    {
                        if (!Main.updateingFlag)
                        {
                            Main.update.SetprogressBarMaxValue(0);
                            //timeEndPeriod(1);
                            MessageBox.Show("已停止更新");
                            return;
                        }
                        else
                        {
                            /* empty */
                        }
                        CmdMessage = CmdMessages[i];
                        Main.update.SetprogressBarStep(i);
                        if (CmdMessages[i].Inhibition)
                        {
                            CmdMessages[i].CmdData[1] |= 0x80;
                        }
                        else
                        {
                            /* empty */
                        }
                        RspResult = false;
                        responseEvent.Reset();

                        if (CmdMessages[i].Physical)
                        {
                            // 启动调度器
                            multiMessageCANScheduler.Start(BaseParamter.UDS_TX_ID, CmdMessages[i].CmdData, delayus);
                            //sendCmd(BaseParamter.UDS_TX_ID, CmdMessages[i].CmdData);
                        }
                        else
                        {
                            multiMessageCANScheduler.Start(BaseParamter.UDS_FunTX_ID, CmdMessages[i].CmdData, delayus);
                            //sendCmd(BaseParamter.UDS_FunTX_ID, CmdMessages[i].CmdData);
                        }
                        MultiMessageCANSchedulerEvent.Reset();
                        if (!MultiMessageCANSchedulerEvent.WaitOne(3 * 1000))/* 等1秒 */
                        {
                            Console.WriteLine("发送超时");
                        }

                        ts4 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                        Main.update.update_ShowTextRefresh(GetDownloadShowText(ref CmdMessage, ref CmdMessage.CmdData, i, (int)(ts4.TotalMilliseconds - ts3.TotalMilliseconds)));
                        ts3 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                        /* 等待反馈 */
                        timeSwNew = new Stopwatch();
                        timeSwNew.Restart();
                        timeSwNew.Start();
                        end = timeSwNew.ElapsedTicks + CmdMessage.DelayMs * 10000;
                        if (!CmdMessage.Inhibition)
                        {
                            bool signaled = responseEvent.WaitOne(CmdMessage.DelayMs);

                            if (!Main.updateingFlag)
                            {
                                Main.update.SetprogressBarMaxValue(0);
                                MessageBox.Show("已停止更新");
                                return;
                            }
                            else
                            {
                                /* empty */
                            }
                            if ((!RspResult) && !((0 == CmdMessage.RspData[0]) && (1 == CmdMessage.RspData.Length))) /* 未接收到正确响应、且不是非0，则表示失败 */
                            {
                                if (i < CmdMessages.Count - 1)
                                {
                                    Result = false;
                                }
                                break;
                            }
                            else
                            {
                                if (0 != CmdMessage.CmdAfterDelayMs)
                                {
                                    Delay_ms(CmdMessage.CmdAfterDelayMs);
                                }
                            }
                        }
                        else
                        {
                            if (0 != CmdMessage.CmdAfterDelayMs)
                            {
                                Delay_ms(CmdMessage.CmdAfterDelayMs);
                            }
                        }

                        /* 发送保持会话的指令 */
                        if (Cmd_8eStopWatch.ElapsedMilliseconds >= Cmd_8eCount)
                        {
                            Cmd_8eCount += 2000;
                            MngSendAliveCmd();
                            Delay_ms(10);
                        }
                        else
                        {
                            /* empty */
                        }
                    }

                    if (Result)
                    {
                        ts2 = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                        Main.update.update_ShowTextRefresh("\r\n总共用时：" + ((ts2.TotalMilliseconds - ts.TotalMilliseconds) / 1000).ToString("F3") + "s");

                        int delayCnt = 0;
                        while (true)
                        {
                            Delay_ms(1);
                            if (HardVersionChecked && SoftVersionChecked)
                            {
                                MessageBox.Show("刷写成功");
                                break;
                            }

                            if (++delayCnt >= 300)
                            {
                                if (HardVersionChecked && SoftVersionChecked)
                                {
                                    MessageBox.Show("刷写成功");
                                }
                                else if (HardVersionInputNull && SoftVersionInputNull)
                                {
                                    MessageBox.Show("刷写成功\r\n(软硬件版本未比对)");
                                }
                                else if (!HardVersionChecked && !SoftVersionChecked)
                                {
                                    MessageBox.Show("刷写失败：软件版本号与硬件版本号比对失败");
                                }
                                else if (!HardVersionChecked)
                                {
                                    MessageBox.Show("刷写失败：硬件版本号比对失败");
                                }
                                else if (!SoftVersionChecked)
                                {
                                    MessageBox.Show("刷写失败：软件版本号比对失败");
                                }
                                else
                                {
                                    MessageBox.Show("刷写失败");
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("刷写失败");
                    }
                    Main.updateingFlag = false;
                    Main.update.SetprogressBarMaxValue(0);
                });
                task.Start();
            }
            catch
            {
                Main.updateingFlag = false;
                Main.update.SetprogressBarMaxValue(0);
            }
            //timeEndPeriod(1);
        }
        public static void Download_Fill0xFF(ref byte[] buf, int index, int length, byte fillNum)
        {
            for (int i = 0; i < length; i++)
            {
                buf[index + i] = fillNum;
            }
        }
        private static int GetSendDataDlc(int len)
        {
            if (len <= 0)
            {
                return 0;
            }
            else if (len <= 1)
            {
                return 1;
            }
            else if (len <= 2)
            {
                return 2;
            }
            else if (len <= 3)
            {
                return 3;
            }
            else if (len <= 4)
            {
                return 4;
            }
            else if (len <= 5)
            {
                return 5;
            }
            else if (len <= 6)
            {
                return 6;
            }
            else if (len <= 7)
            {
                return 7;
            }
            else if (len <= 8)
            {
                return 8;
            }
            else if (len <= 12)
            {
                return 12;
            }
            else if (len <= 16)
            {
                return 16;
            }
            else if (len <= 20)
            {
                return 20;
            }
            else if (len <= 24)
            {
                return 24;
            }
            else if (len <= 32)
            {
                return 32;
            }
            else if (len <= 48)
            {
                return 48;
            }
            else
            {
                return 64;
            }
        }

        private static int GetSendDlcFormCmdLength(int cmdDataLen)
        {
            int dlc;
            if (GetSendDataDlc(cmdDataLen + 2) >= BaseParamter.GetCanSetDlc())
            {
                dlc = BaseParamter.GetCanSetDlc();
            }
            else
            {
                dlc = GetSendDataDlc(cmdDataLen + 2);
            }
            dlc = (8 > dlc) ? 8 : dlc;

            return dlc;
        }

        private static void SendIndexAdd(ref byte SendIndex)
        {
            SendIndex++;
            SendIndex = (0x30 == SendIndex) ? (byte)0x20 : SendIndex;
        }

        private static Boolean sendCmd(UInt16 cmdID, byte[] cmd)
        {
            byte[] sendCmd = null;
            byte SendIndex = 0x21;
            Boolean result = true;
            int timeOut = 0;
            int dlc = 0;
            bool firstFreamFlag = false;

            dlc = GetSendDlcFormCmdLength(cmd.Length);

            sendCmd = new byte[dlc];
            int temp = (8 < dlc) ? 2 : 1;
            if (cmd.Length <= (dlc - temp))
            {
                if (8 < dlc)
                {
                    sendCmd[0] = 0x00;
                    sendCmd[1] = (byte)cmd.Length;
                }
                else
                {
                    sendCmd[0] = (byte)cmd.Length;
                }
                Array.Copy(cmd, 0, sendCmd, temp, cmd.Length);
                Download_Fill0xFF(ref sendCmd, cmd.Length + temp, sendCmd.Length - cmd.Length - temp, 0x55);
                tPCANMsg.DATA = sendCmd;
                tPCANMsg.LEN = (byte)sendCmd.Length;
                tPCANMsg.ID = cmdID;
                tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
            }
            else
            {
                int index = 0;
                for (; ; )
                {
                    if (!Main.updateingFlag)
                    {
                        Main.update.SetprogressBarMaxValue(0);
                        return false;
                    }
                    else
                    {
                        /* empty */
                    }

                    if (8 < BaseParamter.GetCanSetDlc())
                    {
                        if (GetSendDataDlc(cmd.Length - index + 2) >= BaseParamter.GetCanSetDlc())
                        {
                            dlc = BaseParamter.GetCanSetDlc();
                        }
                        else
                        {
                            dlc = GetSendDataDlc(cmd.Length - index + 2);
                        }
                        dlc = BaseParamter.GetCanSetDlc();
                        dlc = (8 > dlc) ? 8 : dlc;
                        temp = (8 < dlc) ? 2 : 1;
                    }
                    else
                    {
                        dlc = 8;
                        temp = 1;
                    }

                    sendCmd = new byte[dlc];

                    if (0 == index) /* 第一帧 */
                    {
                        sendCmd[0] = (byte)(((cmd.Length & 0x0F00) >> 8) | 0x10);
                        sendCmd[1] = (byte)(cmd.Length & 0x00FF);
                        Array.Copy(cmd, 0, sendCmd, 2, dlc - 2);
                        index += dlc - 2;
                        SendIndex = 0x21;
                        firstFreamFlag = true;
                        FluidicFrameFlag = false;
                        flowControlEvent.Reset();
                        // 初始化流控
                        lock (flowControlLock)
                        {
                            flowControlReceived = false;
                            blockCounter = 1;
                        }
                    }
                    else if ((cmd.Length - index) >= (dlc - 1))
                    {
                        sendCmd[0] = SendIndex;
                        SendIndexAdd(ref SendIndex);
                        Array.Copy(cmd, index, sendCmd, 1, dlc - 1);
                        index += dlc - 1;
                    }
                    else
                    {
                        sendCmd[0] = SendIndex;
                        SendIndexAdd(ref SendIndex);
                        Array.Copy(cmd, index, sendCmd, 1, cmd.Length - index);
                        Download_Fill0xFF(ref sendCmd, cmd.Length - index + 1, dlc - (cmd.Length - index + 1), 0x55);
                        index += cmd.Length - index;
                    }
                    tPCANMsg.DATA = sendCmd;
                    tPCANMsg.LEN = (byte)sendCmd.Length;
                    tPCANMsg.ID = cmdID;
                    tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                    //if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_Old)) /* 客户 */
                    //{
                    //    if (SendIndex == 0x22 || SendIndex == 0x2A)
                    //    {
                    //        FluidicFrameFlag = false;
                    //        firstFreamFlag = true;
                    //        flowControlEvent.Reset();
                    //    }
                    //}

                    if (firstFreamFlag || FluidicFrameFlag || flowControlEvent.WaitOne(1000))
                    {
                        //Delay.delay_us(delayus);
                        CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
                        if (firstFreamFlag)
                        {
                            // 等待首帧流控响应
                            flowControlEvent.Reset();
                            if (!flowControlEvent.WaitOne(1000)) // 1秒超时
                            {
                                return false;
                            }

                            // 检查流控状态
                            if (currentFlowControl.FS == 2) // 2 = CTS
                            {
                                return false;
                            }
                        }
                        else
                        {
                            // 检查块大小限制
                            if (currentFlowControl.BS > 0 && blockCounter >= currentFlowControl.BS)
                            {
                                // 等待新的流控帧                   
                                FluidicFrameFlag = false;
                                firstFreamFlag = true;
                                flowControlEvent.Reset();
                                if (!flowControlEvent.WaitOne(1000))
                                {
                                    //return false;
                                    flowControlEvent.Set();
                                }

                                // 重置块计数器
                                blockCounter = 1;
                            }
                            else
                            {
                                blockCounter++;
                            }
                        }
                        // 应用流控延迟
                        if (delayus >= 1000)
                        {
                            Delay.delay_us(delayus / 2);
                            sendControlEvent.Reset();
                            TimeSpan timeSpan = TimeSpan.FromTicks(delayus * 10);
                            sendControlEvent.WaitOne(timeSpan);
                        }
                        else
                        {
                            Delay.delay_us(delayus);
                        }
                        //Delay.delay_us(delayus);
                        //ApplyFlowControlDelay();
                        firstFreamFlag = false;
                    }
                    else
                    {
                        result = false;
                        break;
                    }
                    if (index == cmd.Length || false == result)
                    {
                        break;
                    }
                    else
                    {
                        /* empty */
                    }
                }
            }

            return result;
        }

        private static void DataPacket(ref List<byte> bytes, ref List<byte[]> list_packResult)
        {
            int index = 0x3601;
            int len;
            byte[] bytes1 = new byte[PackingLength + 2];
            int j;

            if (0 == (bytes.Count - ((bytes.Count / PackingLength) * PackingLength)))
            {
                len = bytes.Count / PackingLength * 2 + bytes.Count;
            }
            else
            {
                len = bytes.Count / PackingLength * 2 + bytes.Count + 2;
            }
            byte[] data = new byte[len];

            for (int i = 0; i < bytes.Count / PackingLength + 1; i++)
            {
                data[i * (PackingLength + 2) + 0] = (byte)((index >> 8) & 0x00FF);
                data[i * (PackingLength + 2) + 1] = (byte)((index & 0x00FF));
                for (j = 0; j < PackingLength; j++)
                {
                    if ((i * PackingLength + j) <= bytes.Count - 1)
                    {
                        data[2 + i * (PackingLength + 2) + j] = bytes[i * PackingLength + j];
                    }
                    else
                    {
                        for (j = 0; j < data.Length;)
                        {
                            if ((data.Length - j) >= (PackingLength + 2))
                            {
                                bytes1 = new byte[(PackingLength + 2)];
                                Array.Copy(data, j, bytes1, 0, (PackingLength + 2));
                                list_packResult.Add(bytes1);
                                j += (PackingLength + 2);
                            }
                            else
                            {
                                byte[] bytes2 = new byte[data.Length - j];
                                Array.Copy(data, j, bytes2, 0, data.Length - j);
                                list_packResult.Add(bytes2);
                                j += data.Length - j;
                                break;
                            }
                        }
                        return;
                    }
                }
                index++;
                if (0x36FF < index)
                {
                    index = 0x3600;
                }
            }

            for (j = 0; j < data.Length;)
            {
                if ((data.Length - j) >= (PackingLength + 2))
                {
                    Array.Copy(data, j, bytes1, 0, (PackingLength + 2));
                    list_packResult.Add(bytes1);
                    j += (PackingLength + 2);
                }
                else
                {
                    byte[] bytes2 = new byte[data.Length - j];
                    Array.Copy(data, j, bytes2, 0, data.Length - j);
                    list_packResult.Add(bytes2);
                    j += data.Length - j;
                    break;
                }
            }
            return;
        }

        public static void DataReceive(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            try
            {
                if (msg.DATA.Length > 0 && 0x30 == (msg.DATA[0] & 0xF0)) /* 流控帧 */
                {
                    FluidicFrameFlag = true;
                    flowControlEvent.Set();
                    /* 标准格式：0x30, FS, BS, STmin */
                    if (msg.DATA.Length >= 3)
                    {
                        lock (flowControlLock)
                        {
                            currentFlowControl.FS = (byte)(msg.DATA[0] & 0x0F);
                            currentFlowControl.BS = msg.DATA.Length > 1 ? msg.DATA[1] : (byte)0;
                            currentFlowControl.STmin = msg.DATA.Length > 2 ? msg.DATA[2] : (byte)0;
                            currentFlowControl.STmin = (byte)((currentFlowControl.STmin > 100) ? 100 : currentFlowControl.STmin);/* 最长延时100ms */
                            flowControlReceived = true;
                        }

                        // 仅设置事件，不直接控制发送
                        flowControlEvent.Set();
                    }
                }
                else if (0x01 == (msg.DATA[0] >> 4)) /* 接收大于1帧的数据 */
                {
                    ReceiveFinished = false;
                    int receiveLength = ((msg.DATA[0] & 0x0F) << 8) | msg.DATA[1];
                    ReceiveBuf = new byte[receiveLength];
                    Array.Copy(msg.DATA, 2, ReceiveBuf, 0, msg.DATA.Length - 2);
                    ReceiveCnt = msg.DATA.Length - 2;
                    ReceiveFlag = true;
                    ReceiveIndex = 0x21;
                    UDS_FluidicFrame();
                }
                else if (ReceiveFlag)
                {
                    if (ReceiveIndex == msg.DATA[0])
                    {
                        //Main.update.update_ShowTextRefresh(ReceiveIndex.ToString("X2") + " " + msg.DATA.Length.ToString("X2"));
                        ReceiveIndex++;
                        if ((ReceiveBuf.Length - ReceiveCnt) > 7)
                        {
                            Array.Copy(msg.DATA, 1, ReceiveBuf, ReceiveCnt, msg.DATA.Length - 1);
                            ReceiveCnt += msg.DATA.Length - 1;
                            if (ReceiveCnt == ReceiveBuf.Length)/* 接收完毕 */
                            {
                                ReceiveFlag = false;
                                ReceiveFinished = true;
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                        else /* 所有数据接收完毕 */
                        {
                            Array.Copy(msg.DATA, 1, ReceiveBuf, ReceiveCnt, ReceiveBuf.Length - ReceiveCnt);
                            ReceiveCnt += ReceiveBuf.Length - ReceiveCnt;
                            ReceiveFlag = false;
                            ReceiveFinished = true;
                        }
                    }
                }
                else if (msg.DATA[1] <= (msg.DATA.Length - 2))/* 接收单帧数据 */
                {
                    int receiveLength = msg.DATA[1];
                    ReceiveBuf = new byte[receiveLength];
                    Array.Copy(msg.DATA, 2, ReceiveBuf, 0, receiveLength);
                    ReceiveFinished = true;
                }
                else /* 接收单帧数据 */
                {
                    int receiveLength = msg.DATA[0];
                    ReceiveBuf = new byte[receiveLength];
                    Array.Copy(msg.DATA, 1, ReceiveBuf, 0, receiveLength);
                    ReceiveFinished = true;
                }
            }
            catch
            {
                ReceiveFlag = false;
                ReceiveFinished = false;
            }

            try
            {
                if (ReceiveFinished) /* 接收完毕 */
                {
                    ReceiveFinished = false;

                    if (6 == ReceiveBuf.Length && 0x67 == ReceiveBuf[0])
                    {
                        if (BaseParamter.SelectProjectType == (int)BaseParamter.SelectProjectTypeEnum.BeiQi_Old)
                        {
                            Serv27Key = GetUDSServ27Key_VQ(ref ReceiveBuf);
                        }
                        else
                        {
                            Serv27Key = GetUDSServ27Key(ref ReceiveBuf);
                        }
                        for (int i = 0; i < CmdMessages.Count; i++)
                        {
                            if (0x27 == CmdMessages[i].CmdData[0] && ((0x12 == CmdMessages[i].CmdData[1]) || (0x02 == CmdMessages[i].CmdData[1])))
                            {
                                CmdMessages[i].CmdData[2] = (byte)(Serv27Key & 0x000000FF);
                                CmdMessages[i].CmdData[3] = (byte)((Serv27Key & 0x0000FF00) >> 8);
                                CmdMessages[i].CmdData[4] = (byte)((Serv27Key & 0x00FF0000) >> 16);
                                CmdMessages[i].CmdData[5] = (byte)((Serv27Key & 0xFF000000) >> 24);
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                    else if (18 == ReceiveBuf.Length && 0x67 == ReceiveBuf[0])
                    {
                        if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                        {
                            var result = GetUDSServ27Key_VQ_New(ref ReceiveBuf);
                            if (null != result)
                            {
                                for (int i = 0; i < CmdMessages.Count; i++)
                                {
                                    if (0x27 == CmdMessages[i].CmdData[0] && ((0x12 == CmdMessages[i].CmdData[1]) || (0x02 == CmdMessages[i].CmdData[1])))
                                    {
                                        Array.Copy(result, 0, CmdMessages[i].CmdData, 2, result.Length);
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                            }
                        }
                    }

                    if (CmdMessage.Function is null)
                    {
                        /* empty */
                    }
                    else
                    {
                        if (0x62 == ReceiveBuf[0] || 0x6E == ReceiveBuf[0])
                        {
                            byte[] bytes = new byte[ReceiveBuf.Length - 3];
                            Array.Copy(ReceiveBuf, 3, bytes, 0, bytes.Length);
                            CmdMessage.Function(Encoding.Default.GetString(bytes));
                        }
                    }

                    if (CmdMessage.RspData.Length <= ReceiveBuf.Length)
                    {
                        if (BufCompare(ref CmdMessage.RspData, ref ReceiveBuf, CmdMessage.RspData.Length))
                        {
                            RspResult = true;
                            responseEvent.Set();
                        }
                    }
                    /* 显示接收的数据 */
                    var str = $"Rx   ID: 0x{BaseParamter.UDS_RX_ID:X2}  {string.Join(" ", ReceiveBuf.Select(b => b.ToString("X2")))}\r\n";
                    //string str = "";
                    //str = "Rx   ID: 0x" + BaseParamter.UDS_RX_ID.ToString("X2") + "  ";
                    //for (int i = 0; i < ReceiveBuf.Length; i++)
                    //{
                    //    str += (ReceiveBuf[i].ToString("X2") + " ");
                    //}
                    //str += "\r\n";
                    Main.update.update_ShowTextRefresh(str);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
        }

        internal static void MngCmdMessageLoad(DataTable dataTable)
        {
            string str = "";
            DataRow row;
            int index;
            Boolean Physical = false;
            Boolean Inhibition = false;
            Boolean Enable = false;
            byte[] CmdData = new byte[2];
            byte[] RspData = new byte[2];
            int delayms = 0;
            CmdMessages = new List<CmdMessageType>();
            int requestIndex = 0;

            for (int rowindex = 0; rowindex < dataTable.Rows.Count; rowindex++)
            {
                row = dataTable.Rows[rowindex];
                int i = 0;
                foreach (DataColumn column in dataTable.Columns)
                {
                    if (column.ColumnName.Contains("物理地址"))
                    {
                        if (row[i].ToString().Equals("True"))
                        {
                            Physical = true;
                        }
                        else
                        {
                            Physical = false;
                        }
                    }
                    else if (column.ColumnName.Contains("抑制响应"))
                    {
                        if (row[i].ToString().Equals("True"))
                        {
                            Inhibition = true;
                        }
                        else
                        {
                            Inhibition = false;
                        }
                    }
                    else if (column.ColumnName.Contains("Request(hex)"))
                    {
                        string[] strings = row[i].ToString().Split(',');
                        CmdData = new byte[strings.Length];
                        for (int j = 0; j < strings.Length; j++)
                        {
                            CmdData[j] = Convert.ToByte(strings[j], 16);
                        }
                    }
                    else if (column.ColumnName.Contains("Response(hex)"))
                    {
                        string[] strings = row[i].ToString().Split(',');
                        RspData = new byte[strings.Length];
                        for (int j = 0; j < strings.Length; j++)
                        {
                            RspData[j] = Convert.ToByte(strings[j], 16);
                        }
                    }
                    else if (column.ColumnName.Contains("Delay(ms)"))
                    {
                        int.TryParse(row[i].ToString(), out delayms);
                    }
                    else if (column.ColumnName.Contains("Enable"))
                    {
                        if (row[i].ToString().Equals("True"))
                        {
                            Enable = true;
                        }
                        else
                        {
                            Enable = false;
                        }
                    }
                    i++;
                }
                if (Enable)
                {
                    if (0x27 == CmdData[0] && 0x02 == CmdData[1])
                    {
                        /* 发送密钥 */
                        if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                        {
                            CmdData = new byte[34];
                            CmdData[0] = 0x27;
                            CmdData[1] = 0x02;
                            Array.Copy(Enumerable.Repeat((byte)0x00, 32).ToArray(), 0, CmdData, 2, CmdData.Length - 2);
                        }
                        else
                        {
                            CmdData = new byte[6];
                            Array.Copy(new byte[] { 0x27, 0x02 }, 0, CmdData, 0, 2);
                            Array.Copy(Enumerable.Repeat((byte)0x00, 4).ToArray(), 0, CmdData, 2, CmdData.Length - 2);
                        }
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 1000, (ushort)delayms);
                    }
                    else if (0x27 == CmdData[0] && 0x12 == CmdData[1])
                    {
                        /* 发送密钥 */
                        if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                        {
                            CmdData = new byte[34];
                            Array.Copy(new byte[] { 0x27, 0x12 }, 0, CmdData, 0, 2);
                            Array.Copy(Enumerable.Repeat((byte)0x00, 32).ToArray(), 0, CmdData, 2, CmdData.Length - 2);
                        }
                        else
                        {
                            CmdData = new byte[6];
                            CmdData[0] = 0x27;
                            CmdData[1] = 0x12;
                            Array.Copy(Enumerable.Repeat((byte)0x00, 4).ToArray(), 0, CmdData, 2, CmdData.Length - 2);
                        }
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 1000, (ushort)delayms);
                    }
                    else if (0x31 == CmdData[0] && 0x01 == CmdData[1] && 0x60 == CmdData[2] && 0x00 == CmdData[3]) /* 发送验签数据 */
                    {
                        if (0 == update.XmlDataLength)
                        {
                            CmdData = new byte[4 + update.XmlFalseDataLength];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x60, 0x00 }, 0, CmdData, 0, 4);
                            Array.Copy(update.XmlFalseData.ToArray(), 0, CmdData, 4, update.XmlFalseData.Count);
                        }
                        else
                        {
                            CmdData = new byte[4 + update.XmlDataLength];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x60, 0x00 }, 0, CmdData, 0, 4);
                            Array.Copy(update.XmlData.ToArray(), 0, CmdData, 4, update.XmlData.Count);
                        }
                        CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                    }
                    else if (0x34 == CmdData[0] && 0x00 == CmdData[1] && 0x44 == CmdData[2] && 0 == requestIndex) /* 写flashDriver */
                    {
                        CmdData = new byte[11];
                        Array.Copy(new byte[] { 0x34, 0x00, 0x44 }, 0, CmdData, 0, 3);
                        Array.Copy(byteReverse(BitConverter.GetBytes(update.FlashDriverStartAddr)), 0, CmdData, 3, 4);
                        Array.Copy(byteReverse(BitConverter.GetBytes(update.FlashDriverData.Count)), 0, CmdData, 7, 4);
                        CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);/* 写flashDriver 地址和长度 */
                        /* 发送flashDriver数据 */
                        List<byte[]> list_packResult = new List<byte[]>();
                        DataPacket(ref update.FlashDriverData, ref list_packResult);

                        for (index = 0; index < list_packResult.Count; index++)
                        {
                            CmdData = list_packResult[index];
                            RspData = new byte[2] { 0x76, 0x00 };
                            RspData[1] = CmdData[1];
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                        }
                    }
                    else if (0x31 == CmdData[0] && 0x01 == CmdData[1] && 0x02 == CmdData[2] && 0x02 == CmdData[3] && 0 == requestIndex)
                    {
                        /* FlashDriver CRC32校验 */
                        if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                        {
                            var result = Signer.SignData(update.FlashDriverData.ToArray(), BaseParamter.BeiQiKey);
                            CmdData = new byte[68];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x02, 0x02 }, 0, CmdData, 0, 4);
                            Array.Copy(result, 0, CmdData, 4, result.Length);
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                            requestIndex++;
                        }
                        else
                        {
                            CmdData = new byte[8];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x02, 0x02 }, 0, CmdData, 0, 4);
                            Array.Copy(util.CRC32.ComputeUpdate(ref update.FlashDriverData), 0, CmdData, 4, 4);
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                            requestIndex++;
                        }
                    }
                    else if (0x34 == CmdData[0] && 0x00 == CmdData[1] && 0x44 == CmdData[2] && 1 == requestIndex) /* 写APP 地址和长度 */
                    {
                        CmdData = new byte[11];
                        Array.Copy(new byte[] { 0x34, 0x00, 0x44 }, 0, CmdData, 0, 3);
                        Array.Copy(byteReverse(BitConverter.GetBytes(update.startAddr)), 0, CmdData, 3, 4);
                        Array.Copy(byteReverse(BitConverter.GetBytes(update.BinData.Count)), 0, CmdData, 7, 4);
                        CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);

                        /* 发送App数据 */
                        List<byte[]> list_packResult = new List<byte[]>();
                        DataPacket(ref update.BinData, ref list_packResult);
                        for (index = 0; index < list_packResult.Count; index++)
                        {
                            CmdData = list_packResult[index];
                            RspData = new byte[2] { 0x76, 0x00 };
                            RspData[1] = CmdData[1];
                            CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 1000);
                        }
                        requestIndex++;
                    }
                    else if (0x31 == CmdData[0] && 0x01 == CmdData[1] && 0x02 == CmdData[2] && 0x02 == CmdData[3] && 2 == requestIndex)
                    {
                        /* APP CRC32校验 */
                        if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                        {
                            var result = Signer.SignData(update.BinData.ToArray(), BaseParamter.BeiQiKey);
                            CmdData = new byte[68];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x02, 0x02 }, 0, CmdData, 0, 4);
                            Array.Copy(result, 0, CmdData, 4, result.Length);
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 3000, (ushort)delayms);
                            requestIndex++;
                        }
                        else
                        {
                            CmdData = new byte[8];
                            Array.Copy(new byte[] { 0x31, 0x01, 0x02, 0x02 }, 0, CmdData, 0, 4);
                            Array.Copy(util.CRC32.ComputeUpdate(ref update.BinData), 0, CmdData, 4, 4);
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                            requestIndex++;
                        }
                    }
                    else if (0x22 == CmdData[0] && 0xF1 == CmdData[1] && 0x89 == CmdData[2] && BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao)) /* 读取客户软件版本  */
                    {
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 500, (Func<string, string>)Main.update.Update_SoftVersion);
                    }
                    else if (0x22 == CmdData[0] && 0xF1 == CmdData[1] && 0x50 == CmdData[2] && BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))/* 读取客户硬件版本  */
                    {
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 500, (Func<string, string>)Main.update.Update_HardVersion);
                    }
                    else if (0x22 == CmdData[0] && 0xF1 == CmdData[1] && 0x95 == CmdData[2] && !BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))/* 读取客户软件版本  */
                    {
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 500, (Func<string, string>)Main.update.Update_SoftVersion);
                    }
                    else if (0x22 == CmdData[0] && 0xF1 == CmdData[1] && 0x91 == CmdData[2] && !BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))/* 读取客户硬件版本  */
                    {
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 500, (Func<string, string>)Main.update.Update_HardVersion);
                    }
                    else if (0x22 == CmdData[0] && 0xF1 == CmdData[1] && 0x83 == CmdData[2] && !BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))/* 读取客户Boot版本  */
                    {
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 500, (Func<string, string>)Main.update.Update_BootVersion);
                    }
                    else if (0x2E == CmdData[0] && 0xF1 == CmdData[1] && 0x99 == CmdData[2])
                    {
                        if (7 == CmdData.Length) /* 已经输入时间了，就按输入的时间来 */
                        {
                            /* empty */
                        }
                        else
                        {
                            /* 写刷写时间 */
                            CmdData = new byte[7];
                            Array.Copy(new byte[] { 0x2E, 0xF1, 0x99 }, 0, CmdData, 0, 3);
                            CmdData[3] = (byte)(DateTime.Now.Year / 100);
                            CmdData[4] = (byte)(DateTime.Now.Year % 100);
                            CmdData[5] = (byte)(DateTime.Now.Month);
                            CmdData[6] = (byte)(DateTime.Now.Day);
                        }
                        CmdMessageAdd(ref CmdMessages, true, false, CmdData, RspData, 1000);
                    }
                    else if (0x31 == CmdData[0] && 0x01 == CmdData[1] && 0xFF == CmdData[2] && 0x00 == CmdData[3] && 0x44 == CmdData[4] && BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
                    {
                        if (CmdData.Length < 13)
                        {
                            CmdData = new byte[13];
                            Array.Copy(new byte[] { 0x31, 0x01, 0xFF, 0x00, 0x44 }, 0, CmdData, 0, 5);
                            Array.Copy(byteReverse(BitConverter.GetBytes(update.startAddr)), 0, CmdData, 5, 4);
                            Array.Copy(byteReverse(BitConverter.GetBytes(update.BinData.Count)), 0, CmdData, 9, 4);
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 1000, (ushort)delayms);
                            Console.WriteLine(BitConverter.ToString(CmdData).Replace("-", ""));
                        }
                        else
                        {
                            if (false == Inhibition)
                            {
                                CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 2000, (ushort)delayms);
                            }
                            else
                            {
                                CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 10, (ushort)delayms);
                            }
                        }
                    }
                    else
                    {
                        if (false == Inhibition)
                        {
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 2000, (ushort)delayms);
                        }
                        else
                        {
                            CmdMessageAdd(ref CmdMessages, Physical, Inhibition, CmdData, RspData, 10, (ushort)delayms);
                        }
                    }

                }
            }
            Main.update.SetprogressBarMaxValue(CmdMessages.Count);
        }

        internal static void MngSendAliveCmd()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x02, 0x3E, 0x80, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_FunTX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
        }

        private static void ApplyFlowControlDelay()
        {
            if (currentFlowControl.STmin > 0)
            {
                // 精确延迟处理
                int delayMs = currentFlowControl.STmin;

                // 处理特殊值 (0xF1-0xF9 表示微秒单位)
                if (delayMs >= 0xF1 && delayMs <= 0xF9)
                {
                    int us = delayMs - 0xF0;
                    if (us > 0)
                    {
                        TimeSpan timeSpan = TimeSpan.FromTicks(us * 10);
                        sendControlEvent.WaitOne(timeSpan);
                        //Thread.Sleep(1); // 最小1ms延迟
                        // 高精度延迟需要平台调用
                        //(1); // 需要时启用高精度定时器
                        //Thread.SpinWait(us * 100); // 近似微秒延迟
                        //timeEndPeriod(1);
                    }
                }
                else
                {
                    Delay.delay_ms(delayMs);
                    //TimeSpan timeSpan = TimeSpan.FromTicks(delayMs * 1000 * 10);
                    //sendControlEvent.WaitOne(timeSpan);
                }
            }
        }

        //public class MultimediaTimer : IDisposable
        //{
        //    // 定时器回调委托
        //    private delegate void TimeProc(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

        //    // Win32 API 导入
        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeSetEvent(
        //        uint uDelay,
        //        uint uResolution,
        //        TimeProc lpTimeProc,
        //        UIntPtr dwUser,
        //        uint fuEvent);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeKillEvent(uint uTimerID);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeBeginPeriod(uint uPeriod);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeEndPeriod(uint uPeriod);

        //    // 常量定义
        //    private const uint TIME_PERIODIC = 0x0001;
        //    private const uint TIME_ONESHOT = 0x0000;
        //    private const uint TIME_KILL_SYNC = 0x0100;

        //    private uint _timerId;
        //    private readonly TimeProc _timeProc;
        //    private readonly Action _callback;
        //    private bool _disposed = false;
        //    private bool _isRunning = false;

        //    public MultimediaTimer(Action callback)
        //    {
        //        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        //        _timeProc = new TimeProc(TimerCallback);
        //    }

        //    /// <summary>
        //    /// 启动定时器
        //    /// </summary>
        //    /// <param name="intervalMs">定时间隔（毫秒）</param>
        //    /// <param name="oneShot">是否只执行一次</param>
        //    public void Start(uint intervalMs, bool oneShot = false)
        //    {
        //        if (_isRunning) return;

        //        // 设置系统定时器精度（可选，但可以提高精度）
        //        timeBeginPeriod(1);

        //        uint mode = oneShot ? TIME_ONESHOT : TIME_PERIODIC;

        //        _timerId = timeSetEvent(
        //            intervalMs,        // 延迟时间（毫秒）
        //            0,                // 分辨率（0表示最高精度）
        //            _timeProc,        // 回调函数
        //            UIntPtr.Zero,     // 用户数据
        //            mode);           // 模式：周期性或单次

        //        if (_timerId == 0)
        //        {
        //            throw new Exception("无法创建多媒体定时器");
        //        }

        //        _isRunning = true;
        //    }

        //    /// <summary>
        //    /// 停止定时器
        //    /// </summary>
        //    public void Stop()
        //    {
        //        if (!_isRunning) return;

        //        if (_timerId != 0)
        //        {
        //            timeKillEvent(_timerId);
        //            _timerId = 0;
        //        }

        //        // 恢复系统定时器精度
        //        timeEndPeriod(1);
        //        _isRunning = false;
        //    }

        //    private void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        //    {
        //        try
        //        {
        //            _callback?.Invoke();
        //        }
        //        catch (Exception ex)
        //        {
        //            // 记录异常，避免异常传播到非托管代码
        //            System.Diagnostics.Debug.WriteLine($"定时器回调异常: {ex.Message}");
        //        }
        //    }

        //    public void Dispose()
        //    {
        //        Dispose(true);
        //        GC.SuppressFinalize(this);
        //    }

        //    protected virtual void Dispose(bool disposing)
        //    {
        //        if (!_disposed)
        //        {
        //            if (disposing)
        //            {
        //                // 释放托管资源
        //            }

        //            Stop();
        //            _disposed = true;
        //        }
        //    }

        //    ~MultimediaTimer()
        //    {
        //        Dispose(false);
        //    }
        //}
        public class MultiMessageCANScheduler : IDisposable
        {
            private long _nextTime;
            private UInt16 msgID;
            private byte[] cmdData;
            private int intervalUs;
            private Stopwatch stopwatch = new Stopwatch();
            private long FreamDelayTime;

            byte[] sendCmd;
            byte SendIndex;
            int dlc;
            int status;
            int sendCmdDataIndex;
            bool callbackFlag;
            bool sendFlag = false;

            public void Start(UInt16 canMsgID, byte[] cmdBytes, int delayUs)
            {
                byte[] sendCmd;
                msgID = canMsgID;
                cmdData = cmdBytes;
                intervalUs = delayUs;
                stopwatch.Restart();
                _nextTime = GetCurrentTime();
                /* 初始化 */
                SendIndex = 0x21;
                dlc = 0;
                status = 0;
                sendCmdDataIndex = 0;

                callbackFlag = false;
                sendFlag = true;
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "Download", 1);
            }

            public void Stop(string str)
            {
                sendFlag = false;
                Main.main.multiMessageCANScheduler.RemoveAction("Download");
                MultiMessageCANSchedulerEvent.Set();
                //Console.WriteLine(str);
            }

            private void SchedulerCallback()
            {
                if (callbackFlag)
                {
                    Console.WriteLine("退出");
                    return;
                }
                callbackFlag = true;
                var currentTime = GetCurrentTime();
                if (currentTime > _nextTime)
                {
                    //sendIntervalUs = (((intervalUs * 10) > currentFlowControl.STmin * 10000) ? (intervalUs * 10) : currentFlowControl.STmin * 10000);
                    //_nextTime = currentTime + (((intervalUs * 10) > currentFlowControl.STmin*10000) ? (intervalUs * 10) : currentFlowControl.STmin * 10000);
                    //SendData();


                    if (intervalUs <= 250)
                    {
                        _nextTime = currentTime + (((intervalUs * 10) > currentFlowControl.STmin * 10000) ? (intervalUs * 10) : currentFlowControl.STmin * 10000);
                        SendData();
                        Delay.delay_us(intervalUs);
                        SendData();
                        Delay.delay_us(intervalUs);
                        SendData();
                        Delay.delay_us(intervalUs);
                        SendData();
                    }
                    else if (intervalUs <= 500)
                    {
                        _nextTime = currentTime + (((intervalUs * 10) > currentFlowControl.STmin * 10000) ? (intervalUs * 10) : currentFlowControl.STmin * 10000);
                        SendData();
                        Delay.delay_us(intervalUs);
                        SendData();
                    }
                    else if (intervalUs <= 1000)
                    {
                        _nextTime = currentTime + (((intervalUs * 10) > currentFlowControl.STmin * 10000) ? (intervalUs * 10) : currentFlowControl.STmin * 10000);
                        SendData();
                    }
                    else
                    {
                        _nextTime = currentTime + (intervalUs - 1000) * 10;
                        Delay.delay_us(intervalUs % 1000);
                        Console.WriteLine($"_nextTime:{_nextTime} delayUs:{intervalUs % 1000}");
                        SendData();
                    }
                }
                else
                {
                    /* empty */
                }
                callbackFlag = false;
            }

            private void SendData()
            {
                if(!sendFlag)
                {
                    return;
                }
                switch (status)
                {
                    case 0:
                        dlc = GetSendDlcFormCmdLength(cmdData.Length);
                        sendCmd = new byte[dlc];
                        int temp = (8 < dlc) ? 2 : 1;

                        if (cmdData.Length <= (dlc - temp))
                        {
                            if (8 < dlc)
                            {
                                sendCmd[0] = 0x00;
                                sendCmd[1] = (byte)cmdData.Length;
                            }
                            else
                            {
                                sendCmd[0] = (byte)cmdData.Length;
                            }
                            Array.Copy(cmdData, 0, sendCmd, temp, cmdData.Length);
                            Download_Fill0xFF(ref sendCmd, cmdData.Length + temp, sendCmd.Length - cmdData.Length - temp, 0x55);
                            CAN_API.CAN_API.CanTransmit(msgID, (byte)sendCmd.Length, sendCmd);
                            Stop("stop1:单帧发送完毕");
                        }
                        else
                        {
                            status = 1;
                            if (8 < BaseParamter.GetCanSetDlc())
                            {
                                if (GetSendDataDlc(cmdData.Length - sendCmdDataIndex + 2) >= BaseParamter.GetCanSetDlc())
                                {
                                    dlc = BaseParamter.GetCanSetDlc();
                                }
                                else
                                {
                                    dlc = GetSendDataDlc(cmdData.Length - sendCmdDataIndex + 2);
                                }
                                dlc = BaseParamter.GetCanSetDlc();
                                dlc = (8 > dlc) ? 8 : dlc;
                                temp = (8 < dlc) ? 2 : 1;
                            }
                            else
                            {
                                dlc = 8;
                                temp = 1;
                            }
                            sendCmd = new byte[dlc];

                            sendCmd[0] = (byte)(((cmdData.Length & 0x0F00) >> 8) | 0x10);
                            sendCmd[1] = (byte)(cmdData.Length & 0x00FF);
                            Array.Copy(cmdData, 0, sendCmd, 2, dlc - 2);
                            sendCmdDataIndex += dlc - 2;
                            SendIndex = 0x21;
                            FluidicFrameFlag = false;
                            flowControlEvent.Reset();
                            // 初始化流控
                            lock (flowControlLock)
                            {
                                flowControlReceived = false;
                                blockCounter = 1;
                            }

                            CAN_API.CAN_API.CanTransmit(msgID, (ushort)sendCmd.Length, sendCmd);

                            FreamDelayTime = GetCurrentTime() + 3000 * 1000 * 10; /* 流控帧等待时间最长为3秒 */
                        }
                        break;

                    case 1:
                        // 等待首帧流控响应
                        if (FreamDelayTime < GetCurrentTime())
                        {
                            Stop("stop2:未检测到流控帧");
                        }

                        // 检查流控状态
                        if (currentFlowControl.FS == 2) // 2 = CTS
                        {
                            Stop("stop3:FS=2");
                        }

                        if (FluidicFrameFlag || flowControlReceived)
                        {
                            status = 2;
                        }
                        break;
                    case 2:
                        if (8 < BaseParamter.GetCanSetDlc())
                        {
                            if (GetSendDataDlc(cmdData.Length - sendCmdDataIndex + 2) >= BaseParamter.GetCanSetDlc())
                            {
                                dlc = BaseParamter.GetCanSetDlc();
                            }
                            else
                            {
                                dlc = GetSendDataDlc(cmdData.Length - sendCmdDataIndex + 2);
                            }
                            dlc = BaseParamter.GetCanSetDlc();
                            dlc = (8 > dlc) ? 8 : dlc;
                            temp = (8 < dlc) ? 2 : 1;
                        }
                        else
                        {
                            dlc = 8;
                            temp = 1;
                        }
                        sendCmd = new byte[dlc];

                        if ((cmdData.Length - sendCmdDataIndex) >= (dlc - 1))
                        {
                            sendCmd[0] = SendIndex;
                            SendIndexAdd(ref SendIndex);
                            Array.Copy(cmdData, sendCmdDataIndex, sendCmd, 1, dlc - 1);
                            sendCmdDataIndex += dlc - 1;
                        }
                        else
                        {
                            sendCmd[0] = SendIndex;
                            SendIndexAdd(ref SendIndex);
                            Array.Copy(cmdData, sendCmdDataIndex, sendCmd, 1, cmdData.Length - sendCmdDataIndex);
                            Download_Fill0xFF(ref sendCmd, cmdData.Length - sendCmdDataIndex + 1, dlc - (cmdData.Length - sendCmdDataIndex + 1), 0x55);
                            sendCmdDataIndex += cmdData.Length - sendCmdDataIndex;
                        }
                        CAN_API.CAN_API.CanTransmit(msgID, (ushort)sendCmd.Length, sendCmd);

                        // 检查块大小限制
                        if (currentFlowControl.BS > 0 && blockCounter >= currentFlowControl.BS)
                        {
                            // 等待新的流控帧                   
                            FluidicFrameFlag = false;
                            // 初始化流控
                            lock (flowControlLock)
                            {
                                flowControlReceived = false;
                                blockCounter = 1;
                            }
                            FreamDelayTime = GetCurrentTime() + 3000 * 1000 * 10; /* 流控帧等待时间最长为3秒 */
                            status = 1;
                        }
                        else
                        {
                            //Console.WriteLine($"blockCounter：{blockCounter}");
                            blockCounter++;
                        }
                        break;
                }

                if (sendCmdDataIndex == cmdData.Length)
                {
                    Stop("stop4:当前发送完毕");
                }
                else
                {
                    /* empty */
                }

                if (!Main.updateingFlag)
                {
                    Main.update.SetprogressBarMaxValue(0);
                    Stop("stop5:停止更新");
                }
                else
                {
                    /* empty */
                }
            }

            private long GetCurrentTime()
            {
                return stopwatch.ElapsedTicks;
            }

            public void Dispose()
            {
            }
        }

    }
}
