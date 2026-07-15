using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PCAN_Client.ConversationalMode;
using static PCAN_Client.Decipherment;

namespace PCAN_Client
{
    public partial class UDS : Form
    {
        public readonly uint UDS_TX_ID = 0x7B0;
        public readonly uint UDS_RX_ID = 0x7B8;

        public static readonly byte UDS_Tx_FillNum = 0xAA;

        public Boolean  UnlockFlag = true;
        Stopwatch timeSwNew = new Stopwatch();

        [DllImport("winmm")]
        static extern void timeBeginPeriod(int t);
        [DllImport("winmm")]
        static extern void timeEndPeriod(int t);

        struct UDS_RxMsgType
        {
            public uint Length;
            public uint ReadCnt;
            public uint FluidicFrameCnt;
            public Boolean FluidicFrameFlag;
            public uint UDS_Type;
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
            public byte[] UDS_DATA;
        };

        private Boolean ReadFlag = false;
        private uint ReadLength = 0;
        private byte[] readDataBuf = null;
        private UDS_RxMsgType uDS_MsgType_Rx;
        private UDS_TxMsgType uDS_MsgType_Tx;
        private TextBox testBox;


        public UDS()
        {
            InitializeComponent();
            Main.udsOpenFlag = true;
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
            ReadLength = 0;
            TxOrRxData.Text = "";
        }

        public void SendBuf()
        {
            byte[] sendBuf = new byte[8]{ 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA,};
            TPCANMsg tPCANMsg;
            int index;
            Stopwatch sw = new Stopwatch();

            uDS_MsgType_Tx.TextCnt = 0;
            uDS_MsgType_Tx.FluidicFrameCnt = 0x21;
            tPCANMsg.DATA = sendBuf;
            tPCANMsg.LEN = (byte)sendBuf.Length;
            tPCANMsg.ID = UDS_TX_ID;
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
                if(false == uDS_MsgType_Rx.FluidicFrameFlag)
                {
                    MessageBox.Show("未接收到流控帧信号");
                    return;
                }
                else
                {
                    /* empty */
                }
                timeBeginPeriod(10);
                Thread.Sleep(10);
                timeEndPeriod(10);
            }
            else
            {
                sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
                index = 0;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.Length + 3);
                tPCANMsg.DATA[index++] = (byte)uDS_MsgType_Tx.UDS_Type;
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID >> 8);
                tPCANMsg.DATA[index++] = (byte)(uDS_MsgType_Tx.UDS_DID & 0xFF);
                Array.Copy(uDS_MsgType_Tx.UDS_DATA, 0, sendBuf, index, uDS_MsgType_Tx.Length);
                index += (int)uDS_MsgType_Tx.Length;

                UDS_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length-index, UDS_Tx_FillNum);
                uDS_MsgType_Tx.TextCnt += uDS_MsgType_Tx.Length;

                tPCANMsg.DATA = sendBuf;
                UDS_TxData(tPCANMsg);
                return;
            }
            while(true)
            {
                if(7 <= (uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt))
                {
                    sendBuf = new byte[8] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, };
                    sendBuf[0] = (byte)(uDS_MsgType_Tx.FluidicFrameCnt);

                    Array.Copy(uDS_MsgType_Tx.UDS_DATA, uDS_MsgType_Tx.TextCnt, sendBuf, 1, 7);
                    uDS_MsgType_Tx.TextCnt += 7;

                    uDS_MsgType_Tx.FluidicFrameCnt++;
                    if(0x30 <= uDS_MsgType_Tx.FluidicFrameCnt)
                    {
                        uDS_MsgType_Tx.FluidicFrameCnt = 0x20;
                    }
                    else
                    {
                        /* empty */
                    }
                    tPCANMsg.DATA = sendBuf;

                    UDS_TxData(tPCANMsg);
                    timeBeginPeriod(10);
                    Thread.Sleep(10);
                    timeEndPeriod(10);
                }
                else
                {
                    if(0 == uDS_MsgType_Tx.Length - uDS_MsgType_Tx.TextCnt)
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
            tPCANMsg.ID = UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            UDS_TxData(tPCANMsg);
        }

        public void UDS_SendDataDisplay(uint ID, byte[] cmd)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    TxOrRxData.AppendText("0x");
                    TxOrRxData.AppendText(ID.ToString("X3"));
                    TxOrRxData.AppendText("   ");
                    for (int i = 0; i < cmd.Length; i++)
                    {
                        //TxOrRxData.AppendText("0x");
                        TxOrRxData.AppendText(cmd[i].ToString("X2"));
                        TxOrRxData.AppendText(" ");
                    }
                    //timeSwNew = new Stopwatch();
                    //TxOrRxData.AppendText("  时间:" + ((timeSwNew.ElapsedTicks - timeSwOld.ElapsedTicks)/10000).ToString(" ms"));
                    //timeSwOld = new Stopwatch();

                    timeSwNew.Stop();
                    TxOrRxData.AppendText("  间隔:" + (timeSwNew.ElapsedTicks / 10000.0f).ToString("F3")  + "ms");
                    timeSwNew.Restart();

                    TxOrRxData.AppendText(Environment.NewLine);
                }));
            }
        }

        public void UDS_Display()
        {
            if(testBox is null)
            {
                /* empty */
            }
            else
            {
                if(0 < uDS_MsgType_Rx.UDS_DATA.Length)
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke((EventHandler)(delegate
                        {
                            testBox.Text = Encoding.Default.GetString(uDS_MsgType_Rx.UDS_DATA);
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
            uint saveLength;

            if (UDS_RX_ID != msg.ID)
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
                    /* 显示接收数据 */
                    TxOrRxData.AppendText("0x");
                    TxOrRxData.AppendText(msg.ID.ToString("X3"));
                    TxOrRxData.AppendText("   ");
                    for (int i = 0; i < msg.DATA.Length; i++)
                    {
                        //TxOrRxData.AppendText("0x");
                        TxOrRxData.AppendText(msg.DATA[i].ToString("X2"));
                        TxOrRxData.AppendText(" ");
                    }

                    /* 显示时间间隔 */
                    timeSwNew.Stop();
                    TxOrRxData.AppendText("  时间:" + (timeSwNew.ElapsedTicks / 10000.0f).ToString("F3") + "ms");
                    timeSwNew.Restart();

                    TxOrRxData.AppendText(Environment.NewLine);

                    if (0x7F == msg.DATA[1] && 0x78 != msg.DATA[3])
                    {
                        MessageBox.Show("负响应，响应码为：0x" + msg.DATA[3].ToString("X2"));
                    }
                    else
                    {
                        if ((byte)(msg.DATA[1] - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type)
                        {
                            MessageBox.Show("设置成功");
                        }
                    }
                }));
            }

            /* 接收数据并处理 */
            if (true == ReadFlag)
            {
                if (0x10 == msg.DATA[0]) /* 流控帧 */
                {
                    if ((byte)(msg.DATA[2] - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type)
                    {
                        if ((uint)((msg.DATA[3] << 8) | msg.DATA[4]) == uDS_MsgType_Tx.UDS_DID)
                        {
                            ReadFlag = false;
                            uDS_MsgType_Rx.ReadCnt = 0;
                            uDS_MsgType_Rx.FluidicFrameCnt = 0x21;
                            uDS_MsgType_Rx.Length = (uint)(msg.DATA[1] - 3);
                            uDS_MsgType_Rx.UDS_DATA = new byte[uDS_MsgType_Rx.Length];
                            saveLength = (uint)(msg.DATA.Length - 5);
                            Array.Copy(msg.DATA, 5, uDS_MsgType_Rx.UDS_DATA, (int)uDS_MsgType_Rx.ReadCnt, saveLength);
                            uDS_MsgType_Rx.ReadCnt += saveLength;
                            UDS_FluidicFrame();
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                }
                else
                {
                    if ((byte)(msg.DATA[1] - 0x40) == (byte)uDS_MsgType_Tx.UDS_Type)
                    {
                        if ((uint)((msg.DATA[2] << 8) | msg.DATA[3]) == uDS_MsgType_Tx.UDS_DID)
                        {
                            ReadFlag = false;
                            uDS_MsgType_Rx.ReadCnt = 0;
                            uDS_MsgType_Rx.FluidicFrameCnt = 0x21;
                            uDS_MsgType_Rx.Length = (uint)(msg.DATA[0] - 3);
                            uDS_MsgType_Rx.UDS_DATA = new byte[uDS_MsgType_Rx.Length];
                            saveLength = (uint)(msg.DATA.Length - 4);
                            Array.Copy(msg.DATA, 4, uDS_MsgType_Rx.UDS_DATA, (int)uDS_MsgType_Rx.ReadCnt, saveLength);
                            uDS_MsgType_Rx.ReadCnt += saveLength;
                            UDS_Display();
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                }
            }
            else
            {
                if (uDS_MsgType_Rx.FluidicFrameCnt == msg.DATA[0])
                {
                    uDS_MsgType_Rx.FluidicFrameCnt++;
                    if (0x30 <= uDS_MsgType_Rx.FluidicFrameCnt)
                    {
                        uDS_MsgType_Rx.FluidicFrameCnt = 0x20;
                    }
                    else
                    {
                        /* empty */
                    }
                    saveLength = uDS_MsgType_Rx.Length - uDS_MsgType_Rx.ReadCnt;
                    saveLength = (7 < saveLength) ? 7 : saveLength;
                    Array.Copy(msg.DATA, 1, uDS_MsgType_Rx.UDS_DATA, (int)uDS_MsgType_Rx.ReadCnt, saveLength);
                    uDS_MsgType_Rx.ReadCnt += saveLength;
                    UDS_Display();
                }
                else
                {
                    /* empty */
                }
            }

            if (0x30 == msg.DATA[0])
            {
                uDS_MsgType_Rx.FluidicFrameFlag = true;
            }
            else
            {
                /* empty */
            }

            if(Ser27_LockStatus.Ser27_isUnlock != Decipherment.ser27_LockStatus)
            {
                Decipherment.DeciphermentFun(msg);
            }
            else
            {
                /* empty */
            }

            if (ConversationalMode.converMode_Status != ConverMode_Status.ConverMode_OK)
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
            UDS_SendDataDisplay(tPCANMsg.ID, tPCANMsg.DATA);
            PCAN_API.PCAN_API.PCAN_SendData(tPCANMsg);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x90, 0xAA, 0xAA, 0xAA, 0xAA };

            uDS_MsgType_Tx.UDS_Type = 0x22;
            uDS_MsgType_Tx.UDS_DID  = 0xF190;
            uDS_MsgType_Tx.UDS_DATA = null;
            uDS_MsgType_Tx.Length = 1 + 2;
            if(null != uDS_MsgType_Tx.UDS_DATA)
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
            tPCANMsg.ID = UDS_TX_ID;
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

            if(17 != vinBuf.Length)
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
            uDS_MsgType_Tx.DelayTimeMs = 200;

            timeSwNew.Restart();
            TxOrRxData.Text = "";
            Task task = new Task(() =>
            {
                sw.Restart();
                StartConverMode(ConversationalMode.ConverMode.ConverMode_isExtendedSession);
                while ((sw.ElapsedMilliseconds < uDS_MsgType_Tx.DelayTimeMs) && (ConverMode_Status.ConverMode_OK != ConversationalMode.converMode_Status)) /* 等待跳转到拓展会话 */
                {
                    Thread.Sleep(1);
                }
                if(ConverMode_Status.ConverMode_OK == ConversationalMode.converMode_Status)
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

        private void timer1_Tick(object sender, EventArgs e)
        {
            if(Check3EServer.Checked)
            {
                TPCANMsg tPCANMsg;
                byte[] cmd = new byte[8] { 0x02, 0x3E, 0x80, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
                tPCANMsg.DATA = cmd;
                tPCANMsg.LEN = (byte)cmd.Length;
                tPCANMsg.ID = UDS_TX_ID;
                tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

                PCAN_API.PCAN_API.PCAN_SendData(tPCANMsg);
            }
        }

        private void UDS_Load(object sender, EventArgs e)
        {
            ConverMode.Items.Clear();
            ConverMode.Items.Add("默认会话");
            ConverMode.Items.Add("编程会话");
            ConverMode.Items.Add("拓展会话");
            timer1.Start();
        }

        private void ConverModeButton_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            timeSwNew.Restart();
            TxOrRxData.Text = "";
            ConversationalMode.ConverMode converMode = (ConversationalMode.ConverMode)(ConverMode.SelectedIndex + 1);
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

                }
            });
            task.Start();
            //int index;
            //byte[] cmd = new byte[8] { 0x02, 0x10, 0x00, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
            //TPCANMsg tPCANMsg;

            //uDS_MsgType_Tx.Length = (uint)cmd.Length;
            //uDS_MsgType_Tx.UDS_Type = 0x10;
            //uDS_MsgType_Tx.UDS_DID = 0;
            //uDS_MsgType_Tx.UDS_DATA = cmd;
            //uDS_MsgType_Tx.DelayTimeMs = 100;

            //tPCANMsg.DATA = cmd;
            //tPCANMsg.LEN = (byte)cmd.Length;
            //tPCANMsg.ID = UDS_TX_ID;
            //tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            //index = 2;
            //tPCANMsg.DATA[index++] = (byte)(ConverMode.SelectedIndex + 1);
            //Decipherment.Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, Decipherment.UDS_Tx_FillNum);
            //UDS_TxData(tPCANMsg);
            //ser27_LockStatus = Ser27_LockStatus.Ser27_isExtendedConversation;
        }
    }
}
