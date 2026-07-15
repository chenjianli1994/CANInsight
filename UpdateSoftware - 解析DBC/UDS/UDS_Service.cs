using PCAN_Client.J_Flash;
using PCAN_Client.UDS;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PCAN_Client.ConversationalMode;
using static PCAN_Client.Decipherment;

namespace PCAN_Client
{
    public partial class UDS_Service : Form
    {
        public static readonly byte UDS_Tx_FillNum = 0xAA;

        public Boolean UnlockFlag = true;
        public Boolean ButtonClickFlag = true;
        Stopwatch ButtonClickTime = new Stopwatch();
        Stopwatch timeSwNew = new Stopwatch();

        private Boolean ReadCfgFlag = false;

        //[DllImport("winmm")]
        //static extern void timeBeginPeriod(int t);
        //[DllImport("winmm")]
        //static extern void timeEndPeriod(int t);

        struct UDS_RxMsgType
        {
            public uint Length;
            public uint ReadCnt;
            public uint FluidicFrameCnt;
            public Boolean FluidicFrameFlag;
            //public uint UDS_Type;
            public uint UDS_DID;
            public byte[] UDS_DATA;
        };
        struct UDS_TxMsgType
        {
            public uint Length;
            public uint TextCnt;
            public uint FluidicFrameCnt;
            public uint UDS_Type;
            public uint UDS_DID;
            public uint DelayTimeMs;
            public bool AllowNextFlag;
            public byte[] UDS_DATA;
        };

        private Boolean ReadFlag = false;
        //private uint ReadLength = 0;
        //private byte[] readDataBuf = null;
        private UDS_RxMsgType uDS_MsgType_Rx;
        private UDS_TxMsgType uDS_MsgType_Tx;
        private TextBox testBox;


        public UDS_Service()
        {
            InitializeComponent();
            Main.udsOpenFlag = true;
        }

        static bool ReadMultDataFlag = false;
        public void UDS_DataDeal(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            int receiveLength;
            int receiveResponseCode;
            ushort receiveDID = 0;
            uint saveLength;

            /* 读取响应码 */
            if (0x7F == msg.DATA[1]) /* 负响应码 */
            {
                receiveLength = msg.DATA[0];
                receiveResponseCode = msg.DATA[3];
            }
            else
            {
                /* 读取长度 */
                if (0x01 == (msg.DATA[0] >> 4)) /* 首帧 */
                {
                    receiveLength = ((msg.DATA[0] & 0x0F) << 8) | msg.DATA[1];
                    receiveResponseCode = msg.DATA[2];
                    receiveDID = (ushort)(msg.DATA[3] << 8 | msg.DATA[4]);

                    if (true == ReadFlag)
                    {
                        if (((byte)(receiveResponseCode - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type) &&
                            (receiveDID == uDS_MsgType_Tx.UDS_DID))
                        {
                            ReadFlag = false;
                            ReadMultDataFlag = true;  // 开始多帧数据传输

                            uDS_MsgType_Rx.UDS_DID = receiveDID;
                            uDS_MsgType_Rx.ReadCnt = 0;
                            uDS_MsgType_Rx.FluidicFrameCnt = 0x21;
                            uDS_MsgType_Rx.Length = (uint)(receiveLength - 3);  // 修正：使用总长度计算
                            uDS_MsgType_Rx.UDS_DATA = new byte[uDS_MsgType_Rx.Length];

                            // 复制首帧中的数据
                            saveLength = (uint)(msg.DATA.Length - 5);
                            saveLength = (saveLength > uDS_MsgType_Rx.Length) ? uDS_MsgType_Rx.Length : saveLength;
                            Array.Copy(msg.DATA, 5, uDS_MsgType_Rx.UDS_DATA, (int)uDS_MsgType_Rx.ReadCnt, saveLength);
                            uDS_MsgType_Rx.ReadCnt += saveLength;

                            Console.WriteLine($"First frame received, total length: {uDS_MsgType_Rx.Length}, first data: {saveLength}");

                            // 如果首帧已经包含所有数据，直接显示
                            if (uDS_MsgType_Rx.ReadCnt >= uDS_MsgType_Rx.Length)
                            {
                                ReadMultDataFlag = false;
                                UDS_Display();
                            }
                            else
                            {
                                // 请求更多数据
                                UDS_FluidicFrame();
                            }
                        }
                    }
                }
                else if (0x30 == msg.DATA[0]) /* 流控帧 */
                {
                    uDS_MsgType_Rx.FluidicFrameFlag = true;
                }
                else if (0x20 <= msg.DATA[0] && msg.DATA[0] <= 0x2F) /* 连续帧 */
                {
                    if (ReadMultDataFlag && uDS_MsgType_Rx.FluidicFrameCnt == msg.DATA[0])
                    {
                        uDS_MsgType_Rx.FluidicFrameCnt++;
                        if (uDS_MsgType_Rx.FluidicFrameCnt > 0x2F)
                        {
                            uDS_MsgType_Rx.FluidicFrameCnt = 0x20;
                        }

                        // 计算本次接收的数据长度
                        saveLength = uDS_MsgType_Rx.Length - uDS_MsgType_Rx.ReadCnt;
                        saveLength = (saveLength > 7) ? 7 : saveLength;

                        Array.Copy(msg.DATA, 1, uDS_MsgType_Rx.UDS_DATA, (int)uDS_MsgType_Rx.ReadCnt, saveLength);
                        uDS_MsgType_Rx.ReadCnt += saveLength;

                        Console.WriteLine($"Consecutive frame received, current count: {uDS_MsgType_Rx.ReadCnt}/{uDS_MsgType_Rx.Length}");

                        // 检查是否接收完成
                        if (uDS_MsgType_Rx.ReadCnt >= uDS_MsgType_Rx.Length)
                        {
                            ReadMultDataFlag = false;
                            UDS_Display();
                        }
                    }
                }
                else /* 单帧 */
                {
                    receiveLength = msg.DATA[0];
                    receiveResponseCode = msg.DATA[1];
                    receiveDID = (ushort)(msg.DATA[2] << 8 | msg.DATA[3]);

                    if (true == ReadFlag)
                    {
                        if (((byte)(receiveResponseCode - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type) &&
                            (receiveDID == uDS_MsgType_Tx.UDS_DID))
                        {
                            ReadFlag = false;
                            uDS_MsgType_Rx.UDS_DID = receiveDID;
                            uDS_MsgType_Rx.ReadCnt = 0;
                            uDS_MsgType_Rx.Length = (uint)(receiveLength - 3);
                            uDS_MsgType_Rx.UDS_DATA = new byte[uDS_MsgType_Rx.Length];

                            saveLength = (uint)(uDS_MsgType_Rx.Length);
                            Array.Copy(msg.DATA, 4, uDS_MsgType_Rx.UDS_DATA, 0, saveLength);
                            uDS_MsgType_Rx.ReadCnt = saveLength;

                            UDS_Display();
                        }
                    }
                }
            }
        }

        public static void UDS_Fill0xFF(ref byte[] buf, int index, int length, byte fillNum)
        {
            for (int i = 0; i < length; i++)
            {
                buf[index + i] = fillNum;
            }
        }

        private void startReadData()
        {
            ReadFlag = true;
            if (!uDS_MsgType_Tx.AllowNextFlag)
            {
                TxOrRxData.Text = "";
            }
            else
            {
                TxOrRxData.Text += "\r\n";
            }
        }

        public void SendBuf()
        {
            byte[] sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
            TPCANMsg tPCANMsg;
            int index;
            Stopwatch sw = new Stopwatch();

            uDS_MsgType_Tx.TextCnt = 0;
            uDS_MsgType_Tx.FluidicFrameCnt = 0x21;
            tPCANMsg.DATA = sendBuf;
            tPCANMsg.LEN = (byte)sendBuf.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            if (4 < uDS_MsgType_Tx.Length)
            {
                index = 0;
                tPCANMsg.DATA[index++] = 0x10;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.Length + 3);
                tPCANMsg.DATA[index++] = (byte)uDS_MsgType_Tx.UDS_Type;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID >> 8);
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID & 0xFF);
                Array.Copy(uDS_MsgType_Tx.UDS_DATA, uDS_MsgType_Tx.TextCnt, sendBuf, index, tPCANMsg.DATA.Length - index);
                uDS_MsgType_Tx.TextCnt += (uint)(tPCANMsg.DATA.Length - index);

                //tPCANMsg.DATA = sendBuf;
                sw.Restart();
                uDS_MsgType_Rx.FluidicFrameFlag = false;
                UDS_TxData(tPCANMsg);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (true != uDS_MsgType_Rx.FluidicFrameFlag)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (false == uDS_MsgType_Rx.FluidicFrameFlag)
                {
                    MessageBox.Show("未接收到流控帧信号");
                    return;
                }
                else
                {
                    /* empty */
                }
                //timeBeginPeriod(10);
                Thread.Sleep(10);
                //timeEndPeriod(10);
            }
            else
            {
                sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
                index = 0;
                tPCANMsg.DATA = sendBuf;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.Length + 3);
                tPCANMsg.DATA[index++] = (byte)uDS_MsgType_Tx.UDS_Type;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID >> 8);
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID & 0xFF);
                Array.Copy(uDS_MsgType_Tx.UDS_DATA, 0, tPCANMsg.DATA, index, uDS_MsgType_Tx.Length);
                index += (int)uDS_MsgType_Tx.Length;

                UDS_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                uDS_MsgType_Tx.TextCnt += uDS_MsgType_Tx.Length;

                UDS_TxData(tPCANMsg);
                return;
            }
            while (true)
            {
                if (7 <= (uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt))
                {
                    sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
                    sendBuf[0] = (byte)(uDS_MsgType_Tx.FluidicFrameCnt);

                    Array.Copy(uDS_MsgType_Tx.UDS_DATA, uDS_MsgType_Tx.TextCnt, sendBuf, 1, 7);
                    uDS_MsgType_Tx.TextCnt += 7;

                    uDS_MsgType_Tx.FluidicFrameCnt++;
                    if (0x30 <= uDS_MsgType_Tx.FluidicFrameCnt)
                    {
                        uDS_MsgType_Tx.FluidicFrameCnt = 0x20;
                    }
                    else
                    {
                        /* empty */
                    }
                    tPCANMsg.DATA = sendBuf;

                    UDS_TxData(tPCANMsg);
                    //timeBeginPeriod(10);
                    Thread.Sleep(10);
                    //timeEndPeriod(10);
                }
                else
                {
                    if (0 == uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt)
                    {
                        break;
                    }
                    else
                    {
                        /* empty */
                    }
                    sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
                    sendBuf[0] = (byte)(uDS_MsgType_Tx.FluidicFrameCnt);

                    Array.Copy(uDS_MsgType_Tx.UDS_DATA, uDS_MsgType_Tx.TextCnt, sendBuf, 1, uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt);
                    uDS_MsgType_Tx.TextCnt += uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt;

                    uDS_MsgType_Tx.FluidicFrameCnt++;
                    if (0x30 <= uDS_MsgType_Tx.FluidicFrameCnt)
                    {
                        uDS_MsgType_Tx.FluidicFrameCnt = 0x20;
                    }
                    else
                    {
                        /* empty */
                    }
                    tPCANMsg.DATA = sendBuf;
                    UDS_TxData(tPCANMsg);
                    break;
                }
            }
        }


        public void UDS_FluidicFrame()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            UDS_TxData(tPCANMsg);
        }

        public void UDS_SendDataDisplay(uint ID, byte[] cmd)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    var sb = new StringBuilder();
                    sb.Append($"0x{ID:X3}   ");

                    foreach (byte b in cmd)
                        sb.Append($"{b:X2} ");

                    timeSwNew.Stop();
                    sb.Append($"间隔:{timeSwNew.ElapsedTicks / 10000.0f:F3}ms");
                    timeSwNew.Restart();

                    sb.AppendLine();
                    TxOrRxData.AppendText(sb.ToString());
                }));
            }
        }

        public void UDS_Display()
        {
            if (testBox is null)
            {
                /* empty */
            }
            else
            {
                if (0 < uDS_MsgType_Rx.UDS_DATA.Length)
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke((EventHandler)(delegate
                        {
                            if (0xF1A8 == uDS_MsgType_Rx.UDS_DID)
                            {
                                VehCfg.VehCfgDataDeal(uDS_MsgType_Rx.UDS_DATA);
                                if (ReadCfgFlag)
                                {
                                    ReadCfgFlag = false;
                                    MessageBox.Show("配置字读取成功");
                                }
                            }
                            else if (0xF1EF == uDS_MsgType_Rx.UDS_DID)
                            {
                                if (0x00 == uDS_MsgType_Rx.UDS_DATA[0])
                                {
                                    testBox.Text = "APP";
                                }
                                else
                                {
                                    testBox.Text = "Boot";
                                }
                            }
                            else if (0xF186 == uDS_MsgType_Rx.UDS_DID)
                            {
                                if (0x01 == uDS_MsgType_Rx.UDS_DATA[0])
                                {
                                    testBox.Text = "默认会话";
                                }
                                else if (0x03 == uDS_MsgType_Rx.UDS_DATA[0])
                                {
                                    testBox.Text = "拓展会话";
                                }
                                else
                                {
                                    testBox.Text = "编程会话";
                                }
                            }
                            else if (0xF189 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    uDS_MsgType_Tx.AllowNextFlag = false;
                                    GetHardWartVersion();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if(0xF187 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    GetProgrameDate();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if(0xF199 == uDS_MsgType_Rx.UDS_DID)
                            {
                                if(uDS_MsgType_Rx.UDS_DATA.Length == 4)
                                {
                                    int year, month, day;

                                    // 判断年份格式并解析
                                    if (uDS_MsgType_Rx.UDS_DATA[0] == 0x20)
                                    {
                                        // BCD格式: 0x20 0x25 -> 2025
                                        year = 2000 + ((uDS_MsgType_Rx.UDS_DATA[1] >> 4) * 10 + (uDS_MsgType_Rx.UDS_DATA[1] & 0x0F));
                                        month = (uDS_MsgType_Rx.UDS_DATA[2] >> 4) * 10 + (uDS_MsgType_Rx.UDS_DATA[2] & 0x0F);
                                        day = (uDS_MsgType_Rx.UDS_DATA[3] >> 4) * 10 + (uDS_MsgType_Rx.UDS_DATA[3] & 0x0F);
                                    }
                                    else
                                    {
                                        // 十进制格式: 0x14 0x19 -> 2019
                                        year = uDS_MsgType_Rx.UDS_DATA[0] * 100 + uDS_MsgType_Rx.UDS_DATA[1];
                                        month = uDS_MsgType_Rx.UDS_DATA[2];
                                        day = uDS_MsgType_Rx.UDS_DATA[3];
                                    }

                                    testBox.Text = $"{year}年{month}月{day}日";
                                }

                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    GetProgrameNum();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if(0x0200 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = uDS_MsgType_Rx.UDS_DATA[0].ToString();
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    GetTryProgrameNum();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if (0x0201 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = uDS_MsgType_Rx.UDS_DATA[0].ToString();
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    GetBootVersion();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if (0xF180 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    GetSystemNameOrEngineType();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else if (0xF197 == uDS_MsgType_Rx.UDS_DID)
                            {
                                testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
                                if (uDS_MsgType_Tx.AllowNextFlag)
                                {
                                    uDS_MsgType_Tx.AllowNextFlag = false;
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else
                            {
                                uDS_MsgType_Tx.AllowNextFlag = false;
                                testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
                            }
                        }));
                    }
                }
                else
                {
                    /* empty */
                }
            }
        }

        public void UDS_RxData(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            if (BaseParamter.UDS_RX_ID != msg.ID)
            {
                return;
            }
            else
            {
                /* empty */
            }

            /* 接收报文显示 */
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    ///* 显示接收数据 */
                    timeSwNew.Stop();
                    string dataHex = string.Join(" ", msg.DATA.Select(b => b.ToString("X2")));
                    TxOrRxData.AppendText($"0x{msg.ID:X3}   {dataHex}   时间:{timeSwNew.ElapsedTicks / 10000.0f:F3}ms");
                    timeSwNew.Restart();

                    if (0x7F == msg.DATA[1]) /* 负响应码 */
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine()
                          .AppendLine()
                          .AppendLine($"负响应码：0x{msg.DATA[3]:X2}  {NRC.NRC_Deal(msg.DATA[3])}")
                          .AppendLine();

                        TxOrRxData.AppendText(sb.ToString());
                    }
                    else
                    {
                        /* empty */
                    }
                    TxOrRxData.AppendText(Environment.NewLine);

                    if (0x7F == msg.DATA[1] && 0x78 != msg.DATA[3])
                    {
                        /* empty */
                    }
                    else
                    {
                        if ((false == ButtonClickFlag) && ((byte)(msg.DATA[1] - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type))
                        {
                            ButtonClickFlag = true;
                            MessageBox.Show("设置成功");
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                }));
            }

            /* 接收数据并处理 */
            UDS_DataDeal(msg, timesamp);

            if (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)
            {
                Decipherment.DeciphermentFun(msg);
            }
            else
            {
                /* empty */
            }

            if (converMode_Status != ConverMode_Status.ConverMode_OK)
            {
                ConversationalMode.ConverModeFun(msg);
            }
            else
            {
                /* empty */
            }
        }

        public void UDS_TxData(TPCANMsg tPCANMsg)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    if (testBox is null)
                    {
                        /* empty */
                    }
                    else
                    {
                        testBox.Text = "";
                    }
                }));
            }
            UDS_SendDataDisplay(tPCANMsg.ID, tPCANMsg.DATA);
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
        }

        private void ReadCfg()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0xA8, 0xAA, 0xAA, 0xAA, 0xAA };

            /* 清空所有选项 */
            for (int i = 0; i < VehCfg.tsRTE_H_VehicleCfgType.e_e_comboBoxBuf.Length; i++)
            {
                VehCfg.tsRTE_H_VehicleCfgType.e_e_comboBoxBuf[i].Items.Clear();
                VehCfg.tsRTE_H_VehicleCfgType.e_e_comboBoxBuf[i].Text = "";
            }

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF1A8;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = HidetextBox;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);

            //UDS_SendDataDisplay(UDS_TX_ID, cmd);
            testBox = VIN;
        }

        private void UDS_FormClosing(object sender, FormClosingEventArgs e)
        {
            Main.uDS = null;
            Main.udsOpenFlag = false;
        }

        private void WriteVIN_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            byte[] vinBuf = Encoding.Default.GetBytes(VIN.Text);

            if (17 != vinBuf.Length)
            {
                MessageBox.Show("输入的vin码不为17位！");
                return;
            }
            else
            {
                /* empty */
            }
            uDS_MsgType_Tx.Length = (uint)vinBuf.Length;
            uDS_MsgType_Tx.UDS_Type = 0x2E;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = vinBuf;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            timeSwNew.Restart();
            ButtonClickTime.Restart();
            ButtonClickFlag = false;
            TxOrRxData.Text = "";
            testBox = null;
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        SendBuf();
                    }
                    else
                    {
                        /* empty */
                    }
                }
            });
            task.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (Check3EServer.Checked && !Main.updateingFlag)
            {
                TPCANMsg tPCANMsg;
                byte[] cmd = new byte[8] { 0x02, 0x3E, 0x80, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
                tPCANMsg.DATA = cmd;
                tPCANMsg.LEN = (byte)cmd.Length;
                tPCANMsg.ID = BaseParamter.UDS_TX_ID;
                tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

                CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
            }
        }

        private void UDS_Load(object sender, EventArgs e)
        {
            ComboBox[] comboBoxs = new ComboBox[] { PM2_5CFG, AQSCFG, AnionCFG, FragranceSystemCFG, Valve3DspCFG, AgsCFG, SteeringRudderTypeCfg, DriveMotorTypeCfg, WarmAreaCfg, AreaCfg, RefrigerateTypeCfg, HeatPumpTypeCfg, ADASCFG,SeatsNumCFG };
            ConverMode.Items.Clear();
            ConverMode.Items.Add("默认会话");
            ConverMode.Items.Add("编程会话");
            ConverMode.Items.Add("拓展会话");
            timer1.Start();
            timer2.Start();

            VehCfg.VehCfgInit(comboBoxs);

            ReadCfg();
        }

        private void ConverModeButton_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            timeSwNew.Restart();
            TxOrRxData.Text = "";
            ConversationalMode.ConverMode converMode = (ConversationalMode.ConverMode)(ConverMode.SelectedIndex + 1);
            ButtonClickTime.Restart();
            ButtonClickFlag = true;

            uDS_MsgType_Tx.UDS_Type = 0x10;
            uDS_MsgType_Tx.UDS_DID = 0;
            uDS_MsgType_Tx.DelayTimeMs = 200;

            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(converMode);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    MessageBox.Show("会话模式设置成功");
                }
                else
                {
                    MessageBox.Show("会话模式设置失败");
                }
            });
            task.Start();
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (!ButtonClickFlag)
            {
                if (ButtonClickTime.ElapsedMilliseconds > uDS_MsgType_Tx.DelayTimeMs)
                {
                    ButtonClickFlag = true;

                    if (0x2E == uDS_MsgType_Tx.UDS_Type &&
                       0xF1A8 == uDS_MsgType_Tx.UDS_DID &&
                       true == uDS_MsgType_Tx.AllowNextFlag)
                    {
                        uDS_MsgType_Tx.AllowNextFlag = false;
                        connect();
                        SetV11ChinaCfg();
                    }
                    else
                    {
                        MessageBox.Show("设置失败");
                    }
                }
            }
        }

        private void CfgReadButton_Click(object sender, EventArgs e)
        {
            ReadCfgFlag = true;
            ReadCfg();
        }

        private void CfgWriteButton_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            byte[] cfg = VehCfg.V11GetCfgData();

            uDS_MsgType_Tx.Length = (uint)cfg.Length;
            uDS_MsgType_Tx.UDS_Type = 0x2E;
            uDS_MsgType_Tx.UDS_DID = 0xF1A8;
            uDS_MsgType_Tx.UDS_DATA = cfg;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            timeSwNew.Restart();
            ButtonClickTime.Restart();
            ButtonClickFlag = false;
            TxOrRxData.Text = "";
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        SendBuf();
                    }
                    else
                    {
                        MessageBox.Show("解锁失败");
                    }
                }
            });
            task.Start();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0xEF, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF1EF;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);

            testBox = BootOrApp;
        }


        private void button3_Click_1(object sender, EventArgs e)
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x86, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF186;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);

            testBox = ConverModeTextBox;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                byte nrc = (byte)Convert.ToUInt32(NRC_textBox.Text, 16);
                MessageBox.Show("负响应码：0x" + nrc.ToString("X2") + "  " + NRC.NRC_Deal(nrc) + "\r\n");
            }
            catch
            {
                MessageBox.Show("请输入十六进制数 0x00-0xFF");
            }
        }


        private void connect()
        {
            if (null == Main.main.pCAN_API)
            {
                Main.main.pCAN_API = new PCAN_API.PCAN_API();
                Main.main.pCAN_API.SetPcanChannel(0);

                if (true == Main.main.pCAN_API.Connect(Main.CanFDFlag))
                {
                    /* empty */
                }
                else
                {
                    Main.main.pCAN_API.Connect(Main.CanFDFlag);
                }
            }
            else
            {
                Main.main.pCAN_API.SetPcanChannel(0);
                if (true == Main.main.pCAN_API.Connect(Main.CanFDFlag))
                {
                    /* empty */
                }
                else
                {
                    Main.main.pCAN_API.Connect(Main.CanFDFlag);
                }
            }
        }


        private void SetV11ChinaCfg()
        {
            Stopwatch sw = new Stopwatch();
            byte[] cfg = VehCfg.V11GetCfgData();
            cfg[0] = 0x00;
            cfg[1] = 0x95;
            cfg[2] = 0x00;

            uDS_MsgType_Tx.Length = (uint)cfg.Length;
            uDS_MsgType_Tx.UDS_Type = 0x2E;
            uDS_MsgType_Tx.UDS_DID = 0xF1A8;
            uDS_MsgType_Tx.UDS_DATA = cfg;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            timeSwNew.Restart();
            ButtonClickTime.Restart();
            ButtonClickFlag = false;
            TxOrRxData.Text = "";
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        SendBuf();
                    }
                    else
                    {
                        MessageBox.Show("解锁失败");
                    }
                }
            });
            task.Start();
        }
        private void button5_Click(object sender, EventArgs e)
        {
            uDS_MsgType_Tx.AllowNextFlag = true;
            SetV11ChinaCfg();
        }

        private void GetSoftWartVersion()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x89, 0xAA, 0xAA, 0xAA, 0xAA };
            SoftWareVersion.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF189;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = SoftWareVersion;
        }

        private void GetHardWartVersion()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x50, 0xAA, 0xAA, 0xAA, 0xAA };
            HardWareVersion.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF150;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = HardWareVersion;
        }

        private void GetManufacturerSparePartNumber()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x87, 0xAA, 0xAA, 0xAA, 0xAA };
            ManufacturerSparePartNumber.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF187;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = ManufacturerSparePartNumber;
        }

        private void GetProgrameDate()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x99, 0xAA, 0xAA, 0xAA, 0xAA };
            ProgrameDate.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF199;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = ProgrameDate;
        }
        private void GetProgrameNum()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0x02, 0x00, 0xAA, 0xAA, 0xAA, 0xAA };
            ProgrameNum.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0x0200;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = ProgrameNum;
        }
        private void GetTryProgrameNum()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0x02, 0x01, 0xAA, 0xAA, 0xAA, 0xAA };
            TryProgrameNum.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0x0201;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = TryProgrameNum;
        }
        private void GetBootVersion()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x80, 0xAA, 0xAA, 0xAA, 0xAA };
            BootVersion.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF180;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = BootVersion;
        }
        private void GetSystemNameOrEngineType()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x97, 0xAA, 0xAA, 0xAA, 0xAA };
            SystemNameOrEngineType.Text = "";

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF197;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            timeSwNew.Restart();
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            startReadData();
            UDS_TxData(tPCANMsg);
            testBox = SystemNameOrEngineType;
        }
        
        private void button6_Click(object sender, EventArgs e)
        {
            uDS_MsgType_Tx.AllowNextFlag = true;
            GetSoftWartVersion();
        }

        private void button7_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x02, 0x11, 0x01, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x11;
            uDS_MsgType_Tx.UDS_DID = 0x01;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            uDS_MsgType_Tx.DelayTimeMs = 500;
            if (null != uDS_MsgType_Tx.UDS_DATA)
            {
                uDS_MsgType_Tx.Length += (uint)uDS_MsgType_Tx.UDS_DATA.Length;
            }
            else
            {
                /* empty */
            }

            startReadData();
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        timeSwNew.Restart();
                        tPCANMsg.DATA = cmd;
                        tPCANMsg.LEN = (byte)cmd.Length;
                        tPCANMsg.ID = BaseParamter.UDS_TX_ID;
                        tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                        UDS_TxData(tPCANMsg);
                    }
                    else
                    {
                        /* empty */
                    }
                }
            });
            task.Start();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            service27Unlock();
            //service27BootUnlock();
        }

        private void button9_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = cmd;
            uDS_MsgType_Tx.Length = 1 + 2;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            Check3EServer.Checked = true;
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke((EventHandler)(delegate
                            {
                                Main.service_2F = null;
                                Main.service_2F = new Service_2F();
                                Main.Service2FOpenFlag = true;
                                Main.service_2F.Show();
                                this.Hide();
                            }));
                        }
                        //SendBuf();
                    }
                    else
                    {
                        /* empty */
                    }
                }
            });
            task.Start();
        }

        internal void service27Unlock()
        {
            Stopwatch sw = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = cmd;
            uDS_MsgType_Tx.Length = 1 + 2;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            Check3EServer.Checked = true;
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        if (null != Main.service_2F)
                        {
                            Main.service_2F.Service2F_RepeatInstruction();
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                }
            });
            task.Start();
        }

        internal void service27BootUnlock()
        {
            Stopwatch sw = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = cmd;
            uDS_MsgType_Tx.Length = 1 + 2;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            Check3EServer.Checked = true;
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isProgrammingSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    // 调用示例
                    //var jflash = new util.J_Flash();
                    //jflash.ProgramDevice(
                    //    @"D:\work\work\Projects\Hg\项目\上位机\Output\上位机-全面版\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex",
                    //    @"D:\work\work\Projects\Hg\项目\上位机\Output\上位机-全面版\A11.jflash"
                    //);
                    //var jflash = new util.J_Flash();
                    //jflash.ProgramDevice(
                    //    @"D:\work\work\Projects\Hg\项目\Boot loader\new boot\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey\Debug\Exe\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex",
                    //    @"C:\Users\yuanzhiwen\Desktop\A11.jflash"
                    //);
                    //jflash.ProgramDevice(
                    //    @"D:\work\work\Projects\Hg\项目\Boot loader\new boot\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey\Debug\Exe\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex",
                    //    @"S32K144 (allow security)"
                    //);
                    //bool success = jflash.FlashFirmwareSilently(
                    //            "NXP S32K144 (allow security)",   // 设备名称
                    //            "SWD",         // 接口类型
                    //             4000,          // 速度（kHz）
                    //             @"D:\work\work\Projects\Hg\项目\Boot loader\new boot\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey\Debug\Exe\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex" // 固件路径
                    //        );
                    //Console.WriteLine(success ? "成功！" : "失败！");
                }
            });
            task.Start();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            service27BootUnlock();
            //var jflash = new util.J_Flash();
            //jflash.ProgramDevice(
            //    @"D:\work\work\Projects\Hg\项目\上位机\Output\上位机-全面版\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex",
            //    @"D:\work\work\Projects\Hg\项目\上位机\Output\上位机-全面版\A11.jflash"
            //);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            uDS_MsgType_Tx.AllowNextFlag = true;
            TxOrRxData.Text = "";
            GetManufacturerSparePartNumber();
        }

        private void button12_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = cmd;
            uDS_MsgType_Tx.Length = 1 + 2;
            uDS_MsgType_Tx.DelayTimeMs = 500;

            Check3EServer.Checked = true;
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if (ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
                {
                    UTIL.Delay.delay_ms(150);
                    Decipherment.StartDecipherment();
                    sw.Restart();
                    while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)) /* 等待解锁完毕 */
                    {
                        Thread.Sleep(1);
                    }
                    if (Ser27_LockStatus.Ser27_isUnlock == Decipherment.ser27_LockStatus)
                    {
                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke((EventHandler)(delegate
                            {
                                Main.service_19 = null;
                                Main.service_19 = new Service_19();
                                Main.Service19OpenFlag = true;
                                Main.service_19.Show();
                                this.Hide();
                            }));
                        }
                        //SendBuf();
                    }
                    else
                    {
                        /* empty */
                    }
                }
            });
            task.Start();
        }

        private void button13_Click(object sender, EventArgs e)
        {
            Main.ComTest = null;
            Main.ComTest = new ComTest();
            Main.ComTestOpenFlag = true;
            Main.ComTest.Show();
        }
    }
}
