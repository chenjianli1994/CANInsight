using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;

namespace PCAN_Client.UDS
{
    public partial class Service_2F : Form
    {
        public Service_2F()
        {
            InitializeComponent();
        }

        Stopwatch timeSwNew = new Stopwatch();
        private Dictionary<int, string> DIDMap = new Dictionary<int, string>();
        private TextBox textBoxBack = null;
        private ComboBox comboBoxBack = null;
        private ushort DID_2F;
        private byte[] CmdValueBack = new byte[1];
        private byte ControlModeBack;
        private Boolean Switch = false;
        TPCANMsg tPCANMsg;
        private Boolean service2F27UnlockFlag = false;

        private Boolean isInitializing = false;

        private void startReadData()
        {
            BackShow.Text = "";
        }

        public void UDS_SendDataDisplay(uint ID, byte[] cmd)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    BackShow.AppendText("0x");
                    BackShow.AppendText(ID.ToString("X3"));
                    BackShow.AppendText("   ");
                    for (int i = 0; i < cmd.Length; i++)
                    {
                        BackShow.AppendText(cmd[i].ToString("X2"));
                        BackShow.AppendText(" ");
                    }

                    timeSwNew.Stop();
                    BackShow.AppendText("  间隔:" + (timeSwNew.ElapsedTicks / 10000.0f).ToString("F3") + "ms");
                    timeSwNew.Restart();

                    BackShow.AppendText(Environment.NewLine);
                }));
            }
        }

        private int? FindKeyByValue(string value)
        {
            foreach (var pair in DIDMap)
            {
                if (pair.Value == value)
                {
                    return pair.Key;
                }
            }
            return null; // 或者根据需要返回-1或其他默认值
        }

        private void DIDMapAdd()
        {
            DIDMap.Clear();
            if (radioButton1.Checked)
            {
                DIDMap.Add(0xF42A, "左温度风门");
                DIDMap.Add(0xF42B, "右温度风门");
                DIDMap.Add(0xF42C, "模式风门");
                DIDMap.Add(0xF42D, "内外循环");
                DIDMap.Add(0xF428, "五通水阀");
                DIDMap.Add(0xF429, "前鼓风机档位");
                DIDMap.Add(0xF427, "采暖三通水阀");
                DIDMap.Add(0xF431, "Dsp三通水阀");
                DIDMap.Add(0xF423, "电子风扇");
                DIDMap.Add(0xF424, "EXV_Bat");
                DIDMap.Add(0xF425, "EXV_H");
                DIDMap.Add(0xF426, "EXV_Ac");
                DIDMap.Add(0xF42E, "SOV1");
                DIDMap.Add(0xF42F, "SOV2");
                DIDMap.Add(0xF430, "SOV3");
                DIDMap.Add(0xF420, "前电机水泵");
                DIDMap.Add(0xF421, "后电机水泵");
                DIDMap.Add(0xF422, "采暖水泵");
                DIDMap.Add(0x7021, "电池水泵");
                DIDMap.Add(0xF436, "后SOTxv");
                DIDMap.Add(0xF434, "后模式风门");
                DIDMap.Add(0xF435, "后温度风门");
                DIDMap.Add(0xF432, "后除霜继电器");
                DIDMap.Add(0xF433, "后鼓风机档位");
                DIDMap.Add(0xF316, "空调开关指令");
                DIDMap.Add(0xF317, "空调通风开关请求");
                DIDMap.Add(0xF318, "空调吹风模式请求");
                DIDMap.Add(0xF319, "空调制冷开关请求");
                DIDMap.Add(0xF324, "空调制热开关请求");
            }
            else
            {
                DIDMap.Add(0xF42A, "左温度风门");
                DIDMap.Add(0xF42B, "右温度风门");
                DIDMap.Add(0xF42C, "模式风门");
                DIDMap.Add(0xF42D, "内外循环");
                DIDMap.Add(0xF428, "五通水阀");
                DIDMap.Add(0xF429, "前鼓风机档位");
                DIDMap.Add(0xF535, "采暖三通水阀");
                DIDMap.Add(0xF431, "Dsp三通水阀");
                DIDMap.Add(0xF423, "电子风扇");
                DIDMap.Add(0xF424, "EXV_Bat");
                DIDMap.Add(0xF425, "EXV_H");
                DIDMap.Add(0xF426, "EXV_Ac");
                DIDMap.Add(0xF42F, "SOV1");
                DIDMap.Add(0xF42E, "SOV2");
                DIDMap.Add(0xF436, "SOV3");
                DIDMap.Add(0xF438, "前电机水泵");
                DIDMap.Add(0xF420, "后电机水泵");
                DIDMap.Add(0xF510, "采暖水泵");
                DIDMap.Add(0xF316, "电池水泵");
                DIDMap.Add(0xF43A, "后SOTxv");
                DIDMap.Add(0xF527, "后模式风门");
                DIDMap.Add(0xF439, "后温度风门");
                DIDMap.Add(0xF432, "后除霜继电器");
                DIDMap.Add(0xF513, "后鼓风机档位");
                DIDMap.Add(0xF312, "空调开关指令");
                DIDMap.Add(0xF313, "空调通风开关请求");
                DIDMap.Add(0xF314, "空调吹风模式请求");
                DIDMap.Add(0xF315, "空调制冷开关请求");
                DIDMap.Add(0xF317, "空调制热开关请求");
            }
        }

        internal void Service2F_RepeatInstruction()
        {
            if (service2F27UnlockFlag)
            {
                /* 接收报文显示 */
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke((EventHandler)(delegate
                    {
                        if (textBoxBack is null)
                        {
                            if (comboBoxBack is null)
                            {
                                /* empty */
                            }
                            else
                            {
                                ComponentControl(DID_2F, comboBoxBack);
                            }
                        }
                        else
                        {
                            ComponentControl(DID_2F, textBoxBack);
                        }
                    }));
                }
                else
                {
                    /* empty */
                }

                service2F27UnlockFlag = false;
            }
            else
            {
                /* empty */
            }
        }

        public void Service2F_RxData(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            if (BaseParamter.UDS_RX_ID != msg.ID)
            {
                return;
            }
            else
            {
                /* empty */
            }
            if (0x6F == msg.DATA[1] && msg.LEN >= 8)
            {
                ComponentResp(ref msg);
            }
            else if (0x03 == msg.DATA[0] && 0x7F == msg.DATA[1] && 0x2F == msg.DATA[2] && 0x7F == msg.DATA[3])
            {
                service2F27UnlockFlag = true;
                Main.uDS.service27Unlock();
            }
            else if (0x03 == msg.DATA[0] && 0x7F == msg.DATA[1] && 0x2F == msg.DATA[2] && 0x22 == msg.DATA[3])
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke((EventHandler)(delegate
                    {
                        string ComponentName = "未知部件";
                        if (!DIDMap.TryGetValue(DID_2F, out ComponentName))
                        {
                            ComponentName = "未知部件";
                        }
                        else
                        {
                            /* empty */
                        }
                        BackShow.Text = "\"" + ComponentName + "\" 控制条件不满足，可能存在故障";
                        BackShow.BackColor = Color.Yellow;
                    }));
                }
            }
            else
            {
                /* empty */
            }
        }

        private void ComponentResp(ref TPCANMsg msg)
        {
            UInt16 DID;
            byte Cmd;
            byte ControlMode;
            string ComponentName = "未知部件";
            DID = (ushort)(msg.DATA[2] << 8 | msg.DATA[3]);
            ControlMode = msg.DATA[4];
            Cmd = msg.DATA[5];

            UDS_SendDataDisplay(msg.ID, msg.DATA);
            /* 接收报文显示 */
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    if (0 == ControlMode)
                    {
                        if (comboBoxBack is null)
                        {
                            /* empty */
                        }
                        else
                        {
                            comboBoxBack.BackColor = Color.White;
                            comboBoxBack = null;
                        }
                        if (textBoxBack is null)
                        {
                            /* empty */
                        }
                        else
                        {
                            textBoxBack.BackColor = Color.White;
                            textBoxBack = null;
                        }
                        if (!DIDMap.TryGetValue(DID, out ComponentName))
                        {
                            ComponentName = "未知部件";
                        }
                        else
                        {
                            /* empty */
                        }
                        BackShow.BackColor = Color.White;
                        BackShow.AppendText("\r\n\"" + ComponentName + "\" 取消控制");
                    }
                    else if (3 == ControlMode)
                    {
                        if (!DIDMap.TryGetValue(DID, out ComponentName))
                        {
                            ComponentName = "未知部件";
                        }
                        else
                        {
                            /* empty */
                        }
                        if (Cmd == CmdValueBack[0] && DID == DID_2F)
                        {
                            if (comboBoxBack is null)
                            {
                                /* empty */
                            }
                            else
                            {
                                comboBoxBack.BackColor = Color.White;
                                comboBoxBack = null;
                            }
                            if (textBoxBack is null)
                            {
                                /* empty */
                            }
                            else
                            {
                                textBoxBack.BackColor = Color.White;
                                textBoxBack = null;
                            }
                        }
                        else
                        {
                            /* empty */
                        }
                        BackShow.BackColor = Color.White;
                        BackShow.AppendText("\r\n\"" + ComponentName + "\" 短暂控制 控制值：" + CmdValueBack[0].ToString());
                    }
                }));
            }
            else
            {
                /* empty */
            }
        }

        private void ComponentControl(UInt16 DID, TextBox textBox)
        {
            int TextValue = -1;
            byte[] cmd = new byte[8] { 0x05, 0x2F, 0xF4, 0x28, 0x03, 0x03, 0xAA, 0xAA };

            if (!Switch)
            {
                return;
            }
            else
            {
                DID_2F = DID;
                if (!int.TryParse(textBox.Text, out TextValue))
                {
                    return;
                }
                textBoxBack = textBox;
                ControlModeBack = (byte)((-1 == TextValue) ? 0 : 3);
                CmdValueBack[0] = (byte)(TextValue);
            }
            startReadData();

            if (0 != ControlModeBack) /* 取消控制时，移除指令复制代码 */
            {
                cmd[0] = (byte)(4 + CmdValueBack.Length);
                cmd[2] = (byte)((DID >> 8) & 0x00FF);
                cmd[3] = (byte)((DID >> 0) & 0x00FF);
                cmd[4] = ControlModeBack;
                Array.Copy(CmdValueBack, 0, cmd, 5, CmdValueBack.Length);
                for (int i = 5 + CmdValueBack.Length; i < cmd.Length; i++)
                {
                    cmd[i] = 0xAA;
                }
            }
            else
            {
                cmd[0] = 4;
                cmd[2] = (byte)((DID >> 8) & 0x00FF);
                cmd[3] = (byte)((DID >> 0) & 0x00FF);
                cmd[4] = ControlModeBack;
                for (int i = 5; i < cmd.Length; i++)
                {
                    cmd[i] = 0xAA;
                }
            }

            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);

            if (comboBoxBack is null)
            {
                /* empty */
            }
            else
            {
                comboBoxBack.BackColor = Color.Yellow;
            }
            if (textBox is null)
            {
                /* empty */
            }
            else
            {
                textBox.BackColor = Color.Yellow;
            }

            UDS_SendDataDisplay(tPCANMsg.ID, tPCANMsg.DATA);
        }
        private void ComponentControl(UInt16 DID, ComboBox comboBox)
        {
            int TextValue = -1;
            byte[] cmd = new byte[8] { 0x05, 0x2F, 0xF4, 0x28, 0x03, 0x03, 0xAA, 0xAA };

            if (!Switch)
            {
                return;
            }
            else
            {
                DID_2F = DID;
                comboBoxBack = comboBox;
                if (radioButton1.Checked)
                {
                    if (0xF434 == DID ||
                        0xF432 == DID ||
                        0xF42E == DID ||
                        0xF42F == DID ||
                        0xF430 == DID ||
                        0xF316 == DID ||
                        0xF317 == DID ||
                        0xF318 == DID ||
                        0xF319 == DID ||
                        0xF324 == DID)
                    {
                        TextValue = (byte)(comboBox.SelectedIndex - 1);
                    }
                    else
                    {
                        TextValue = (byte)(comboBox.SelectedIndex);
                    }
                }
                else
                {
                    if (0xF527 == DID ||
                        0xF432 == DID ||
                        0xF42F == DID ||
                        0xF42E == DID ||
                        0xF436 == DID ||
                        0xF312 == DID ||
                        0xF313 == DID ||
                        0xF314 == DID ||
                        0xF315 == DID ||
                        0xF317 == DID)
                    {
                        TextValue = (byte)(comboBox.SelectedIndex - 1);
                    }
                    else
                    {
                        TextValue = (byte)(comboBox.SelectedIndex);
                    }
                }

                ControlModeBack = (byte)((0 == comboBox.SelectedIndex) ? 0 : 3);

                CmdValueBack[0] = (byte)(TextValue);
            }

            startReadData();
            if (0 != ControlModeBack) /* 取消控制时，移除指令复制代码 */
            {
                cmd[0] = (byte)(4 + CmdValueBack.Length);
                cmd[2] = (byte)((DID >> 8) & 0x00FF);
                cmd[3] = (byte)((DID >> 0) & 0x00FF);
                cmd[4] = ControlModeBack;
                Array.Copy(CmdValueBack, 0, cmd, 5, CmdValueBack.Length);
                for (int i = 5 + CmdValueBack.Length; i < cmd.Length; i++)
                {
                    cmd[i] = 0xAA;
                }
            }
            else
            {
                cmd[0] = 4;
                cmd[2] = (byte)((DID >> 8) & 0x00FF);
                cmd[3] = (byte)((DID >> 0) & 0x00FF);
                cmd[4] = ControlModeBack;
                for (int i = 5; i < cmd.Length; i++)
                {
                    cmd[i] = 0xAA;
                }
            }

            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            CAN_API.CAN_API.CanTransmit(tPCANMsg.ID, tPCANMsg.LEN, tPCANMsg.DATA);

            if (comboBox is null)
            {
                /* empty */
            }
            else
            {
                comboBox.BackColor = Color.Yellow;
            }
            if (textBoxBack is null)
            {
                /* empty */
            }
            else
            {
                textBoxBack.BackColor = Color.Yellow;
            }
            UDS_SendDataDisplay(tPCANMsg.ID, tPCANMsg.DATA);
        }


        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComponentControl(0xF428, C5WV_Ctrl);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            Switch = true;
        }

        private void Service_2F_Load(object sender, EventArgs e)
        {
            isInitializing = true;
            if (Properties.Settings.Default.Service2FProjectSelect.Equals("27款"))
            {
                radioButton2.Checked = true;
            }
            else
            {
                radioButton1.Checked = true;
            }
            DIDMapAdd();
            C5WV_Ctrl.Items.Add("取消控制");
            C5WV_Ctrl.Items.Add("正常模式");
            C5WV_Ctrl.Items.Add("低温散热");
            C5WV_Ctrl.Items.Add("电机保温");
            C5WV_Ctrl.Items.Add("余热利用");
            C5WV_Ctrl.SelectedIndex = 0;

            ModeMotor.Items.Add("取消控制");
            ModeMotor.Items.Add("吹面");
            ModeMotor.Items.Add("吹面吹脚");
            ModeMotor.Items.Add("吹脚");
            ModeMotor.Items.Add("吹脚吹窗");
            ModeMotor.Items.Add("吹窗");
            ModeMotor.SelectedIndex = 0;

            SOV1.Items.Add("取消控制");
            SOV1.Items.Add("Disable");
            SOV1.Items.Add("Enable");
            SOV1.SelectedIndex = 0;

            SOV2.Items.Add("取消控制");
            SOV2.Items.Add("Disable");
            SOV2.Items.Add("Enable");
            SOV2.SelectedIndex = 0;

            SOV3.Items.Add("取消控制");
            SOV3.Items.Add("Disable");
            SOV3.Items.Add("Enable");
            SOV3.SelectedIndex = 0;

            RearDefrost.Items.Add("取消控制");
            RearDefrost.Items.Add("Disable");
            RearDefrost.Items.Add("Enable");
            RearDefrost.SelectedIndex = 0;

            SOTXV.Items.Add("取消控制");
            SOTXV.Items.Add("Disable");
            SOTXV.Items.Add("Enable");
            SOTXV.SelectedIndex = 0;

            RearModeMotor.Items.Add("取消控制");
            RearModeMotor.Items.Add("吹面");
            RearModeMotor.Items.Add("吹面吹脚");
            RearModeMotor.Items.Add("吹脚");
            RearModeMotor.SelectedIndex = 0;

            FBlowerLevel.Items.Add("取消控制");
            FBlowerLevel.Items.Add("1档");
            FBlowerLevel.Items.Add("2档");
            FBlowerLevel.Items.Add("3档");
            FBlowerLevel.Items.Add("4档");
            FBlowerLevel.Items.Add("5档");
            FBlowerLevel.Items.Add("6档");
            FBlowerLevel.Items.Add("7档");
            FBlowerLevel.SelectedIndex = 0;

            RBlowerLevel.Items.Add("取消控制");
            RBlowerLevel.Items.Add("1档");
            RBlowerLevel.Items.Add("2档");
            RBlowerLevel.Items.Add("3档");
            RBlowerLevel.Items.Add("4档");
            RBlowerLevel.Items.Add("5档");
            RBlowerLevel.Items.Add("6档");
            RBlowerLevel.Items.Add("7档");
            RBlowerLevel.SelectedIndex = 0;

            HvacSwitch.Items.Add("取消控制");
            HvacSwitch.Items.Add("No Control");
            HvacSwitch.Items.Add("OFF");
            HvacSwitch.Items.Add("ON");
            HvacSwitch.Items.Add("RemotON");
            HvacSwitch.SelectedIndex = 0;

            VentSwitch.Items.Add("取消控制");
            VentSwitch.Items.Add("No Control");
            VentSwitch.Items.Add("OFF");
            VentSwitch.Items.Add("ON");
            VentSwitch.SelectedIndex = 0;

            HvacMode.Items.Add("取消控制");
            HvacMode.Items.Add("No control");
            HvacMode.Items.Add("Face");
            HvacMode.Items.Add("FaceFoot");
            HvacMode.Items.Add("Foot");
            HvacMode.Items.Add("FootDefrost");
            HvacMode.Items.Add("Windscreen");
            HvacMode.Items.Add("FaceDefrost");
            HvacMode.Items.Add("FaceFootDefrost");
            HvacMode.SelectedIndex = 0;

            AcSwitch.Items.Add("取消控制");
            AcSwitch.Items.Add("No Control");
            AcSwitch.Items.Add("OFF");
            AcSwitch.Items.Add("ON");
            AcSwitch.SelectedIndex = 0;

            HeatSwitch.Items.Add("取消控制");
            HeatSwitch.Items.Add("No Control");
            HeatSwitch.Items.Add("OFF");
            HeatSwitch.Items.Add("ON");
            HeatSwitch.SelectedIndex = 0;

            HVACCycleMode.Text = "-1";
            EXV_Ac.Text = "-1";
            EXV_H.Text = "-1";
            EXV_Bat.Text = "-1";
            RightMotor.Text = "-1";
            LeftMixMotor.Text = "-1";
            CFCV.Text = "-1";
            DspValve3.Text = "-1";
            EWP_Bat.Text = "-1";
            EWP_FMotor.Text = "-1";
            EWP_RMOtor.Text = "-1";
            EWP_H.Text = "-1";
            RearMixMotor.Text = "-1";
            CondFan.Text = "-1";

            timer1.Interval = 100;
            timer1.Start();
            isInitializing = false;
        }

        private void Service_2F_FormClosed(object sender, FormClosedEventArgs e)
        {
            Main.Service2FOpenFlag = false; 
            if (Main.udsOpenFlag)
            {
                Main.uDS.Show();
            }
        }

        private void HVACCycleMode_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "内外循环";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void HVACCycleMode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "内外循环";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EXV_Ac_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_Ac";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EXV_Ac_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_Ac";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void ModeMotor_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "模式风门";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, ModeMotor);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void LeftMixMotor_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "左温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void LeftMixMotor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "左温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void RightMotor_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "右温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void RightMotor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "右温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void CFCV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "采暖三通水阀";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void CFCV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "采暖三通水阀";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void DspValve3_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "Dsp三通水阀";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void DspValve3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "Dsp三通水阀";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EXV_H_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_H";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EXV_H_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_H";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EXV_Bat_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_Bat";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EXV_Bat_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "EXV_Bat";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EWP_Bat_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "电池水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EWP_Bat_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "电池水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EWP_FMotor_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "前电机水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EWP_FMotor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "前电机水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EWP_RMOtor_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "后电机水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EWP_RMOtor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "后电机水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void EWP_H_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "采暖水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void EWP_H_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "采暖水泵";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void SOV1_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "SOV1";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, SOV1);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void SOV2_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "SOV2";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, SOV2);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void SOV3_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "SOV3";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, SOV3);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void RearDefrost_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "后除霜继电器";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, RearDefrost);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void SOTXV_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "后SOTxv";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, SOTXV);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void RearMixMotor_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "后温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void RearMixMotor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "后温度风门";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void RearModeMotor_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "后模式风门";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, RearModeMotor);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void FBlowerLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "前鼓风机档位";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, FBlowerLevel);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void RBlowerLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "后鼓风机档位";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, RBlowerLevel);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void CondFan_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "电子风扇";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void CondFan_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    string componentName = "电子风扇";
                    int? did = FindKeyByValue(componentName);
                    if (did.HasValue)
                    {
                        ComponentControl((ushort)did.Value, textBox);
                    }
                    else
                    {
                        // 处理未找到的情况
                        MessageBox.Show($"未找到组件'{componentName}'对应的DID");
                    }
                    textBox.BackColor = Color.White;
                }
            }
        }

        private void HvacSwitch_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "空调开关指令";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, HvacSwitch);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void VentSwitch_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "空调通风开关请求";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, VentSwitch);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void HvacMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "空调吹风模式请求";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, HvacMode);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void AcSwitch_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "空调制冷开关请求";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, AcSwitch);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void HeatSwitch_SelectedIndexChanged(object sender, EventArgs e)
        {
            string componentName = "空调制热开关请求";
            int? did = FindKeyByValue(componentName);
            if (did.HasValue)
            {
                ComponentControl((ushort)did.Value, HeatSwitch);
            }
            else
            {
                // 处理未找到的情况
                MessageBox.Show($"未找到组件'{componentName}'对应的DID");
            }
        }

        private void radioButton2_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Service2FProjectSelect = "27款";
            Properties.Settings.Default.Save();
            DIDMapAdd();
        }

        private void radioButton1_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Service2FProjectSelect = "24/26款";
            Properties.Settings.Default.Save(); 
            DIDMapAdd();
        }
    }
}
