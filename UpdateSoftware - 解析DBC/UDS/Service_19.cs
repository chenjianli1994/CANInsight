using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PCAN_Client.ConversationalMode;
using static PCAN_Client.Decipherment;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using TextBox = System.Windows.Forms.TextBox;

namespace PCAN_Client.UDS
{
    public partial class Service_19 : Form
    {
        private Dictionary<int, TextBox> _2426DTCMap = new Dictionary<int, TextBox>();
        private Dictionary<int, TextBox> _27DTCMap = new Dictionary<int, TextBox>();
        private static int timeOutDelayms = 0; 
        private static int AutotimeOutDelayms = 0;

        public Service_19()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
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
        /* 接收长数据 */
        public static byte[] ReceiveBuf = null;
        public static int ReceiveCnt = 0;
        public static int ReceiveIndex = 0;
        public static Boolean ReceiveFlag = false;
        public static Boolean ReceiveFinished = false;
        public static Boolean FluidicFrameFlag = false;
        public void DataReceive(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            if (BaseParamter.UDS_RX_ID != msg.ID)
            {
                return;
            }
            try
            {
                if (msg.DATA.Length > 0 && 0x30 == (msg.DATA[0] & 0xF0)) /* 流控帧 */
                {
                    FluidicFrameFlag = true;
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
                        ReceiveIndex = (ReceiveIndex >= 0x30) ? 0x20 : ReceiveIndex;
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

                    CAN_API.CAN_API.SetService19RunFlag(false);
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke((EventHandler)(delegate
                        {
                            if (ReceiveBuf[0] == (0x19 + 0x40))
                            {
                                // 暂停布局逻辑
                                this.SuspendLayout();
                                UInt32 DTC_Code = 0;
                                int DTC_Status = 0;
                                for (int index = 3; index < ReceiveBuf.Length;)
                                {
                                    DTC_Code = (uint)(ReceiveBuf[index++] << 16);
                                    DTC_Code |= (uint)(ReceiveBuf[index++] << 8);
                                    DTC_Code |= (uint)(ReceiveBuf[index++] & 0xFF);
                                    DTC_Status = ReceiveBuf[index++];
                                    var targetMap = radioButton1.Checked ? _2426DTCMap : _27DTCMap;
                                    if (targetMap.TryGetValue((int)DTC_Code, out TextBox textBox))
                                    {
                                        if (DTC_Status == 0x09)
                                        {
                                            if (textBox.BackColor != Color.Red)
                                            {
                                                textBox.BackColor = Color.Red;
                                            }
                                        }
                                        else if (DTC_Status == 0x08)
                                        {
                                            if (textBox.BackColor != Color.Yellow)
                                            {
                                                textBox.BackColor = Color.Yellow;
                                            }
                                        }
                                        else
                                        {
                                            if (textBox.BackColor != Color.Green)
                                            {
                                                textBox.BackColor = Color.Green;
                                            }
                                        }
                                    }
                                }

                                // 恢复布局逻辑
                                this.ResumeLayout();
                            }
                        }));
                    }
                }
            }
            catch (Exception e)
            {
                // 恢复布局逻辑
                this.ResumeLayout();
                MessageBox.Show(e.ToString());
            }
        }
        
        private void MapCreate()
        {
            _2426DTCMap.Clear();
            _2426DTCMap.Add(0x900014, textBox1);
            _2426DTCMap.Add(0x900114, textBox2);
            _2426DTCMap.Add(0x900214, textBox3);
            _2426DTCMap.Add(0x900314, textBox4);
            _2426DTCMap.Add(0x900414, textBox5);
            _2426DTCMap.Add(0x900514, textBox6);
            _2426DTCMap.Add(0x900714, textBox7);
            _2426DTCMap.Add(0x900814, textBox8);
            _2426DTCMap.Add(0x900914, textBox9);
            _2426DTCMap.Add(0x900A14, textBox10);
            _2426DTCMap.Add(0x900B14, textBox11);
            _2426DTCMap.Add(0x900C14, textBox12);
            _2426DTCMap.Add(0x900D14, textBox13);
            _2426DTCMap.Add(0x900E14, textBox14);
            _2426DTCMap.Add(0x900F14, textBox15);
            _2426DTCMap.Add(0x901014, textBox16);
            _2426DTCMap.Add(0x901277, textBox17);
            _2426DTCMap.Add(0x901377, textBox18);
            _2426DTCMap.Add(0x901477, textBox19);
            _2426DTCMap.Add(0x901577, textBox20);
            _2426DTCMap.Add(0xD4D087, textBox21);
            _2426DTCMap.Add(0x901671, textBox22);
            _2426DTCMap.Add(0x901612, textBox23);
            _2426DTCMap.Add(0x901613, textBox24);
            _2426DTCMap.Add(0x901698, textBox25);
            _2426DTCMap.Add(0x90161C, textBox26);
            _2426DTCMap.Add(0xD4D187, textBox27);
            _2426DTCMap.Add(0x901771, textBox28);
            _2426DTCMap.Add(0x901712, textBox29);
            _2426DTCMap.Add(0x901713, textBox30);
            _2426DTCMap.Add(0x901798, textBox31);
            _2426DTCMap.Add(0x90171C, textBox32);
            _2426DTCMap.Add(0xD4D287, textBox33);
            _2426DTCMap.Add(0x901871, textBox34);
            _2426DTCMap.Add(0x901812, textBox35);
            _2426DTCMap.Add(0x901813, textBox36);
            _2426DTCMap.Add(0x901898, textBox37);
            _2426DTCMap.Add(0x90181C, textBox38);
            _2426DTCMap.Add(0xD4D387, textBox39);
            _2426DTCMap.Add(0x901998, textBox40);
            _2426DTCMap.Add(0x901919, textBox41);
            _2426DTCMap.Add(0x901972, textBox42);
            _2426DTCMap.Add(0x90191C, textBox43);
            _2426DTCMap.Add(0x901971, textBox44);
            _2426DTCMap.Add(0xD4D487, textBox45);
            _2426DTCMap.Add(0x901A98, textBox46);
            _2426DTCMap.Add(0x901A19, textBox47);
            _2426DTCMap.Add(0x901A72, textBox48);
            _2426DTCMap.Add(0x901A1C, textBox49);
            _2426DTCMap.Add(0x901A71, textBox50);
            _2426DTCMap.Add(0x901A96, textBox51);
            _2426DTCMap.Add(0xD4D587, textBox52);
            _2426DTCMap.Add(0x901B98, textBox53);
            _2426DTCMap.Add(0x901B19, textBox54);
            _2426DTCMap.Add(0x901B72, textBox55);
            _2426DTCMap.Add(0x901B1C, textBox56);
            _2426DTCMap.Add(0x901B71, textBox57);
            _2426DTCMap.Add(0x901B96, textBox58);
            _2426DTCMap.Add(0xD4D687, textBox59);
            _2426DTCMap.Add(0x901C98, textBox60);
            _2426DTCMap.Add(0x901C19, textBox61);
            _2426DTCMap.Add(0x901C72, textBox62);
            _2426DTCMap.Add(0x901C1C, textBox63);
            _2426DTCMap.Add(0x901C71, textBox64);
            _2426DTCMap.Add(0xD4D787, textBox65);
            _2426DTCMap.Add(0x901D13, textBox66);
            _2426DTCMap.Add(0x901D71, textBox67);
            _2426DTCMap.Add(0x901D98, textBox68);
            _2426DTCMap.Add(0x901D12, textBox69);
            _2426DTCMap.Add(0x901D1C, textBox70);
            _2426DTCMap.Add(0xD4D887, textBox71);
            _2426DTCMap.Add(0x901E96, textBox72);
            _2426DTCMap.Add(0x901E00, textBox73);
            _2426DTCMap.Add(0x901E71, textBox74);
            _2426DTCMap.Add(0x901E98, textBox75);
            _2426DTCMap.Add(0x901E1C, textBox76);
            _2426DTCMap.Add(0x901E19, textBox77);
            _2426DTCMap.Add(0xD4DD87, textBox78);
            _2426DTCMap.Add(0x902614, textBox79);
            _2426DTCMap.Add(0xD4D987, textBox80);
            _2426DTCMap.Add(0x901F12, textBox81);
            _2426DTCMap.Add(0x901F71, textBox82);
            _2426DTCMap.Add(0x901F49, textBox83);
            _2426DTCMap.Add(0x901F1C, textBox84);
            _2426DTCMap.Add(0x901F98, textBox85);
            _2426DTCMap.Add(0xD4DA87, textBox86);
            _2426DTCMap.Add(0x902016, textBox87);
            _2426DTCMap.Add(0x902098, textBox88);
            _2426DTCMap.Add(0x902017, textBox89);
            _2426DTCMap.Add(0x902001, textBox90);
            _2426DTCMap.Add(0x902071, textBox91);
            _2426DTCMap.Add(0x902091, textBox92);
            _2426DTCMap.Add(0xD4DB87, textBox93);
            _2426DTCMap.Add(0x902196, textBox94);
            _2426DTCMap.Add(0x902114, textBox95);
            _2426DTCMap.Add(0x902714, textBox96);
            _2426DTCMap.Add(0x902814, textBox97);
            _2426DTCMap.Add(0xD31787, textBox98);
            _2426DTCMap.Add(0x902296, textBox99);
            _2426DTCMap.Add(0xD4DC87, textBox100);
            _2426DTCMap.Add(0xC07388, textBox101);
            _2426DTCMap.Add(0x910016, textBox102);
            _2426DTCMap.Add(0x910117, textBox103);
            _2426DTCMap.Add(0x910119, textBox104);
            _2426DTCMap.Add(0x910116, textBox105);
            _2426DTCMap.Add(0x910197, textBox106);
            _2426DTCMap.Add(0x910101, textBox107);
            _2426DTCMap.Add(0x910198, textBox108);
            _2426DTCMap.Add(0xD4DE87, textBox109);
            _2426DTCMap.Add(0x910204, textBox110);
            _2426DTCMap.Add(0x910316, textBox111);
            _2426DTCMap.Add(0x910317, textBox112);
            _2426DTCMap.Add(0xD4DF87, textBox113);
            _2426DTCMap.Add(0x902996, textBox114);
            _2426DTCMap.Add(0x902A14, textBox115);
            _2426DTCMap.Add(0x902B14, textBox116);
            _2426DTCMap.Add(0x902C14, textBox117);
            _2426DTCMap.Add(0x902D14, textBox118);
            _2426DTCMap.Add(0x902E14, textBox119);
            _2426DTCMap.Add(0x902F14, textBox120);
            _2426DTCMap.Add(0x903014, textBox121);
            _2426DTCMap.Add(0x903114, textBox122);
            _2426DTCMap.Add(0xD4E087, textBox123);
            _2426DTCMap.Add(0xD4E187, textBox124);
            _2426DTCMap.Add(0xD4E183, textBox125);
            _2426DTCMap.Add(0xD4E287, textBox126);
            _2426DTCMap.Add(0xD4E283, textBox127);
            _2426DTCMap.Add(0xD4E383, textBox128);
            _2426DTCMap.Add(0xD4E483, textBox129);
            _2426DTCMap.Add(0xD4E583, textBox130);
            _2426DTCMap.Add(0xD4E683, textBox131);
            _2426DTCMap.Add(0xD4E783, textBox132);
            _2426DTCMap.Add(0xD4E883, textBox133);
            _2426DTCMap.Add(0xD4E983, textBox134);
            _2426DTCMap.Add(0xD4EA83, textBox135);
            _2426DTCMap.Add(0xD4EB83, textBox136);
            _2426DTCMap.Add(0xD4EC87, textBox137);
            _2426DTCMap.Add(0xD4ED83, textBox138);
            _2426DTCMap.Add(0xD4EE87, textBox139);
            _2426DTCMap.Add(0xD4EF87, textBox140);

            _27DTCMap.Add(0x900014, textBox1);
            _27DTCMap.Add(0x900114, textBox2);
            _27DTCMap.Add(0x900214, textBox3);
            _27DTCMap.Add(0x900314, textBox4);
            _27DTCMap.Add(0x900414, textBox5);
            _27DTCMap.Add(0x900514, textBox6);
            _27DTCMap.Add(0x901114, textBox7);
            _27DTCMap.Add(0x903014, textBox8);
            _27DTCMap.Add(0x900714, textBox9);
            _27DTCMap.Add(0x904014, textBox10);
            _27DTCMap.Add(0x900614, textBox11);
            _27DTCMap.Add(0x900D14, textBox12);
            _27DTCMap.Add(0x903E14, textBox13);
            _27DTCMap.Add(0x900E14, textBox14);
            _27DTCMap.Add(0x900F14, textBox15);
            _27DTCMap.Add(0x901014, textBox16);
            _27DTCMap.Add(0x901271, textBox17);
            _27DTCMap.Add(0x901471, textBox18);
            _27DTCMap.Add(0x901C71, textBox19);
            _27DTCMap.Add(0x903271, textBox20);
            _27DTCMap.Add(0xD4D887, textBox21);
            _27DTCMap.Add(0x901871, textBox22);
            _27DTCMap.Add(0x901812, textBox23);
            _27DTCMap.Add(0x901813, textBox24);
            _27DTCMap.Add(0x901898, textBox25);
            _27DTCMap.Add(0x90181C, textBox26);
            _27DTCMap.Add(0x901987, textBox27);
            _27DTCMap.Add(0x901971, textBox28);
            _27DTCMap.Add(0x901912, textBox29);
            _27DTCMap.Add(0x901913, textBox30);
            _27DTCMap.Add(0x901998, textBox31);
            _27DTCMap.Add(0x90191C, textBox32);
            _27DTCMap.Add(0xD4D787, textBox33);
            _27DTCMap.Add(0x901771, textBox34);
            _27DTCMap.Add(0x901712, textBox35);
            _27DTCMap.Add(0x901713, textBox36);
            _27DTCMap.Add(0x901798, textBox37);
            _27DTCMap.Add(0x90171C, textBox38);
            _27DTCMap.Add(0xD4C487, textBox39);
            _27DTCMap.Add(0x904698, textBox40);
            _27DTCMap.Add(0x904619, textBox41);
            _27DTCMap.Add(0x904672, textBox42);
            _27DTCMap.Add(0x90461C, textBox43);
            _27DTCMap.Add(0x904671, textBox44);
            _27DTCMap.Add(0xD4D487, textBox45);
            _27DTCMap.Add(0x901A98, textBox46);
            _27DTCMap.Add(0x901A19, textBox47);
            _27DTCMap.Add(0x901A72, textBox48);
            _27DTCMap.Add(0x901A71, textBox49);
            _27DTCMap.Add(0x901A1C, textBox50);
            _27DTCMap.Add(0x901A49, textBox51);
            _27DTCMap.Add(0xD4D587, textBox52);
            _27DTCMap.Add(0x901B98, textBox53);
            _27DTCMap.Add(0x901B19, textBox54);
            _27DTCMap.Add(0x901B72, textBox55);
            _27DTCMap.Add(0x901B1C, textBox56);
            _27DTCMap.Add(0x901B71, textBox57);
            _27DTCMap.Add(0x901B49, textBox58);
            _27DTCMap.Add(0xD4EF87, textBox59);
            _27DTCMap.Add(0x902598, textBox60);
            _27DTCMap.Add(0x902519, textBox61);
            _27DTCMap.Add(0x902572, textBox62);
            _27DTCMap.Add(0x90251C, textBox63);
            _27DTCMap.Add(0x902571, textBox64);
            _27DTCMap.Add(0xD4C387, textBox65);
            _27DTCMap.Add(0x902A13, textBox66);
            _27DTCMap.Add(0x902A71, textBox67);
            _27DTCMap.Add(0x902A98, textBox68);
            _27DTCMap.Add(0x902A12, textBox69);
            _27DTCMap.Add(0x902A1C, textBox70);
            _27DTCMap.Add(0xD4C287, textBox71);
            _27DTCMap.Add(0x901E49, textBox72);
            _27DTCMap.Add(0x901E48, textBox73);
            _27DTCMap.Add(0x901E71, textBox74);
            _27DTCMap.Add(0x901E98, textBox75);
            _27DTCMap.Add(0x901E17, textBox76);
            _27DTCMap.Add(0x901E19, textBox77);
            _27DTCMap.Add(0xD4F587, textBox78);
            _27DTCMap.Add(0x98B977, textBox79);
            _27DTCMap.Add(0xD4F287, textBox80);
            _27DTCMap.Add(0x98B612, textBox81);
            _27DTCMap.Add(0x98B671, textBox82);
            _27DTCMap.Add(0x98B607, textBox83);
            _27DTCMap.Add(0x98B61C, textBox84);
            _27DTCMap.Add(0x98B698, textBox85);
            _27DTCMap.Add(0x98BE87, textBox86);
            _27DTCMap.Add(0x98BE16, textBox87);
            _27DTCMap.Add(0x98BE98, textBox88);
            _27DTCMap.Add(0x98BE17, textBox89);
            _27DTCMap.Add(0x98BE01, textBox90);
            _27DTCMap.Add(0x98BE71, textBox91);
            _27DTCMap.Add(0x98BE07, textBox92);
            _27DTCMap.Add(0xD4F187, textBox93);
            _27DTCMap.Add(0x98B577, textBox94);
            _27DTCMap.Add(0x902177, textBox95);
            _27DTCMap.Add(0x98B507, textBox96);
            _27DTCMap.Add(0x98B598, textBox97);
            _27DTCMap.Add(0xD4DD87, textBox98);
            _27DTCMap.Add(0x902E77, textBox99);
            _27DTCMap.Add(0xD4DC87, textBox100);
            _27DTCMap.Add(0xC07388, textBox101);
            _27DTCMap.Add(0x910016, textBox102);
            _27DTCMap.Add(0x910117, textBox103);
            _27DTCMap.Add(0x910119, textBox104);
            _27DTCMap.Add(0x910116, textBox105);
            _27DTCMap.Add(0x910197, textBox106);
            _27DTCMap.Add(0x910101, textBox107);
            _27DTCMap.Add(0x910198, textBox108);
            _27DTCMap.Add(0xD4C187, textBox109);
            _27DTCMap.Add(0x901D49, textBox110);
            _27DTCMap.Add(0x910316, textBox111);
            _27DTCMap.Add(0x910317, textBox112);
            _27DTCMap.Add(0xD4F487, textBox113);
            _27DTCMap.Add(0x98B877, textBox114);
            _27DTCMap.Add(0x900C14, textBox115);
            _27DTCMap.Add(0x903F07, textBox116);
            _27DTCMap.Add(0x903449, textBox117);
            _27DTCMap.Add(0x98B349, textBox118);
            _27DTCMap.Add(0x900A14, textBox119);
            _27DTCMap.Add(0x900B14, textBox120);
            _27DTCMap.Add(0x900914, textBox121);
            _27DTCMap.Add(0x902F14, textBox122);
            _27DTCMap.Add(0xD50087, textBox123);
            _27DTCMap.Add(0xD4E787, textBox124);
            _27DTCMap.Add(0xD4EA83, textBox125);
            _27DTCMap.Add(0xD4E587, textBox126);
            _27DTCMap.Add(0xD4F883, textBox127);
            _27DTCMap.Add(0xD4F983, textBox128);
            _27DTCMap.Add(0xD4FA83, textBox129);
            _27DTCMap.Add(0xD4FB83, textBox130);
            _27DTCMap.Add(0xD4FC83, textBox131);
            _27DTCMap.Add(0xD4DE83, textBox132);
            _27DTCMap.Add(0xD4FE83, textBox133);
            _27DTCMap.Add(0xD4FD83, textBox134);
            _27DTCMap.Add(0xD4FF83, textBox135);
            _27DTCMap.Add(0xD4DB83, textBox136);
            _27DTCMap.Add(0xD4DA87, textBox137);
            _27DTCMap.Add(0xD4EB83, textBox138);
            _27DTCMap.Add(0xD4EE87, textBox139);
            _27DTCMap.Add(0xD4ED87, textBox140);

        }
        private void Read19Data()
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x02, 0x19, 0x0A, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            try
            {
                var targetMap = radioButton1.Checked ? _2426DTCMap : _27DTCMap;
                // 暂停布局逻辑
                //this.SuspendLayout();
                //foreach (var item in targetMap)
                //{
                //    if (item.Value.BackColor != Color.White)
                //    {
                //        item.Value.BackColor = Color.White;
                //    }
                //}
                ////恢复布局逻辑
                //this.ResumeLayout();
                // 暂停布局逻辑
                //this.SuspendLayout();
                //foreach (var item in targetMap)
                //{
                //    if (item.Value.BackColor != Color.Green)
                //    {
                //        item.Value.BackColor = Color.Green;
                //    }
                //}
            }
            finally
            {
                // 恢复布局逻辑
                this.ResumeLayout();
            }
            CAN_API.CAN_API.SetService19RunFlag(true);
            timeOutDelayms = 2000 / 100;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
        }
        private void Service_19_Load(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.Service2FProjectSelect.Equals("27款"))
            {
                radioButton2.Checked = true;
            }
            else
            {
                radioButton1.Checked = true;
            }
            MapCreate();
            timer1.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Read19Data();
        }

        private void radioButton1_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Service2FProjectSelect = "24/26款";
            Properties.Settings.Default.Save();
        }

        private void radioButton2_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Service2FProjectSelect = "27款";
            Properties.Settings.Default.Save();
        }

        private void Service_19_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void Service_19_FormClosed(object sender, FormClosedEventArgs e)
        {
            Main.Service19OpenFlag = false;
            if (Main.udsOpenFlag)
            {
                Main.uDS.Show();
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if(timeOutDelayms > 0)
            {
                timeOutDelayms--;
                if (0 == timeOutDelayms)
                {
                    CAN_API.CAN_API.SetService19RunFlag(false);
                }
            }
            if (checkBox1.Checked)
            {
                if(AutotimeOutDelayms++ >= 20) /* 2000ms */
                {
                    AutotimeOutDelayms = 0;
                    Read19Data();
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x04, 0x14, 0xFF, 0xFF, 0xFF, 0xAA, 0xAA, 0xAA };
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);
        }
    }
}
