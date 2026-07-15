using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PCAN_Client.CAN_API;
using PCAN_Client.Canoe_API;
using PCAN_Client.PCAN_API;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolBar;

namespace PCAN_Client.Update
{
    public partial class update : Form
    {
        public static string S19FilePath = "";
        public static List<Byte> BinData;
        public static UInt32 startAddr;

        public static List<byte> FlashDriverData;
        public static UInt32 FlashDriverStartAddr;

        public static List<byte> FlashDriverData_V11;
        public static UInt32 FlashDriverStartAddr_V11;

        public static List<byte> FlashDriverData_V50;
        public static UInt32 FlashDriverStartAddr_V50;

        static string XmlFilePath = "";
        public static List<Byte> XmlData;
        public static int XmlDataLength = 0;
        /* 无验签数据时，使用全0x01数据代替 */
        public static List<Byte> XmlFalseData = Enumerable.Repeat((Byte)0x01, 1322).ToList();
        public static int XmlFalseDataLength = 1322;

        /* 自定义指令 */
        internal DataTable dataTable;
        internal int SelectRowIndex = 0;
        internal int SelectColumnIndex = 0;
        internal int dataGridView1Column = 0;

        AutoSizeFormClass autoSizeFormClass = new AutoSizeFormClass();

        internal readonly int[] delay_us_canoe = new int[5] { 250, 400, 450, 900, 3500 };
        internal readonly int[] delay_us_pcan = new int[5] { 250, 400, 450, 900, 3500 };

        StringBuilder stringBuilder = new StringBuilder();
        int setprogressBarStep = -1;
        public update()
        {
            InitializeComponent();
            if(BaseParamter.Pre_b_OnlyUpdateFlag)
            {
                Main.update = this;
                Main.main = new Main();
            }
            else
            {
                /* empty */
            }

            dataGridView1.CellClick += DataGridView1_CellClick;

            dataTable = new DataTable();
            dataGridView1Column = 0;
            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                // Check if the header text is found
                dataTable.Columns.Add(column.HeaderText, typeof(string));
                dataGridView1Column++;
            }
            // 绑定DataTable到DataGridView
            dataGridView1.Columns.Clear();
            dataGridView1.DataSource = dataTable;
        }

        private void S19InputTextShowOrHide(Boolean isShow)
        {
            if (isShow)
            {
                // Show elements and adjust layout
                label1.Show();
                S19Addr.Show();
                label2.Location = new Point(S19Addr.Location.X + S19Addr.Width + 20, 18);
                XmlPath.Location = new Point(button_UpdateStart.Location.X, 9);
                XmlPath.Width = button_UpdateStart.Location.X + button_UpdateStart.Width - XmlPath.Location.X;
            }
            else
            {
                // Hide elements and expand XML path
                label1.Hide();
                S19Addr.Hide();
                label2.Location = new Point(12, 18);
                XmlPath.Location = new Point(label2.Location.X + label2.Width + 20, 9);
                XmlPath.Width = button_UpdateStart.Location.X + button_UpdateStart.Width - XmlPath.Location.X;
            }
        }
        private void UpdateStart()
        {
            Boolean result = false;
            if (!Main.updateingFlag)
            {
                if (radioButton_PCAN.Checked == true)
                {
                    result = CAN_Connect();
                }
                else if (radioButton_CANOE.Checked == true)
                {
                    Main.pcanOpenFlag = false;
                    result = CANOE_Connect();
                }
                else
                {
                    result = CAN_Connect();
                }
                if (!result)
                {
                    if (radioButton_PCAN.Checked == true)
                    {
                        result = CAN_Connect();
                    }
                    else if (radioButton_CANOE.Checked == true)
                    {
                        Main.pcanOpenFlag = false;
                        result = CANOE_Connect();
                    }
                    else
                    {
                        result = CAN_Connect();
                    }
                }
                if (result)
                {
                    button1.Text = "已连接";
                    Main.updateingFlag = true;
                    if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))
                    {
                        BaseParamter.SetCanSetDlc(8);
                        Download.PackingLength = 1022;
                        FlashDriverData = FlashDriverData_V11;
                        FlashDriverStartAddr = FlashDriverStartAddr_V11;
                    }
                    else
                    {
                        if(BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_Old))
                        {
                            BaseParamter.SetCanSetDlc(8);
                            Download.PackingLength = 1022;
                        }
                        else
                        {
                            //BaseParamter.SetCanSetDlc(20);
                                    Download.PackingLength = 4093;
                        }
                        FlashDriverData = FlashDriverData_V50;
                        FlashDriverStartAddr = FlashDriverStartAddr_V50;
                    }
                    int delayus;
                    if (int.TryParse(textBoxDelayus.Text, out delayus))
                    {
                        /* empty */
                    }
                    else
                    {
                        delayus = 0;
                    }
                    if(0 == XmlDataLength && BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))
                    {
                        var dialogResult = MessageBox.Show("当前无验签数据，是否继续？继续则使用全0x01数据填充(仅适用于屏蔽校验的boot)", "确认", MessageBoxButtons.YesNo);
                        if (dialogResult != DialogResult.Yes)
                        {
                            Main.updateingFlag = false;
                            return; // 用户取消，直接退出方法
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                    stringBuilder = new StringBuilder();
                    Download.MngDownloadHandle(dataTable, delayus);
                    ShowText.Text = "";
                    SoftVersion.BackColor = Color.White;
                    HardVersion.BackColor = Color.White;
                }
                else
                {
                    /* empty */
                }
            }
            else
            {
                Main.updateingFlag = false;
                Download.responseEvent.Set();
            }

        }

        private void FileRead(string path, bool drop)
        {
            if (path.Equals("0"))
            {
                return;
            }
            else
            {
                /* empty */
            }
            string FileName = Path.GetFileName(path);
            if (FileName.Contains(".xml"))
            {
                ReadXmlFile(path);
            }
            else if (FileName.Contains(".tmp"))
            {
                ReadTmpFile(path, false);
            }
            else if (FileName.Contains(".s19") || FileName.Contains(".srec") || FileName.Contains(".hex"))
            {
                ReadS19File(path);
            }
            else if (FileName.Contains(".json"))
            {
                MemoryReadJson(File.ReadAllText(path));
            }
            else
            {
                MessageBox.Show(FileName + "不符合 .s19 .srec .hex .xml .tmp json 中的任意类文件");
            }
        }
        private void ReadS19File(string path)
        {
            S19FilePath = path;
            Properties.Settings.Default.S19Path = S19FilePath; /* 记忆路径 */
            Properties.Settings.Default.Save();
            S19Addr.Text = Path.GetFileName(S19FilePath);
            if (S19Addr.Text.Contains(".srec") || S19Addr.Text.Contains(".s19"))
            {
                S19InputTextShowOrHide(true);
                BinData = new List<Byte>();
                try
                {
                    StreamReader HexReader = new StreamReader(S19FilePath);
                    if (!util.S19DataRead.MngS19SrecToBin(HexReader, ref startAddr, ref BinData))
                    {
                        S19Addr.BackColor = Color.Red;
                        S19Addr.Text = "S19 Or SREC地址无效";
                    }
                    else
                    {
                        S19Addr.BackColor = Color.White;
                    }
                }
                catch 
                {
                    S19Addr.BackColor = Color.Red;
                    S19Addr.Text = "S19 Or SREC地址无效";
                }
            }
            else if(S19Addr.Text.Contains(".hex"))
            {
                S19InputTextShowOrHide(true);
                BinData = new List<Byte>();
                try
                {
                    StreamReader HexReader = new StreamReader(S19FilePath);
                    if (!util.S19DataRead.MngHexToBin(HexReader, ref startAddr, ref BinData))
                    {
                        S19Addr.BackColor = Color.Red;
                        S19Addr.Text = "hex 地址无效";
                    }
                    else
                    {
                        S19Addr.BackColor = Color.White;
                    }
                }
                catch
                {
                    S19Addr.BackColor = Color.Red;
                    S19Addr.Text = "hex 地址无效";
                }
            }
            else
            {
                MessageBox.Show("请拖入.Srec文件或者.S19文件或者.hex文件");
                S19Addr.Text = "";
            }
        }

        private void ReadXmlFile(string path)
        {
            XmlFilePath = path;
            Properties.Settings.Default.XmlOrTempPath = XmlFilePath; /* 记忆路径 */
            Properties.Settings.Default.Save();
            XmlPath.Text = Path.GetFileName(XmlFilePath);
            if (XmlPath.Text.Contains(".xml"))
            {
                S19InputTextShowOrHide(true);
                S19FilePath = Properties.Settings.Default.S19Path; /* 恢复路径 */
                S19Addr.Text = Path.GetFileName(S19FilePath);
                FileRead(S19FilePath, false);

                XmlData = new List<byte>();
                if (!util.XmlFileRead.XmlToBytes(XmlFilePath, ref XmlDataLength, ref XmlData))
                {
                    XmlPath.BackColor = Color.Red;
                    XmlPath.Text = "XML文件地址无效";
                }
                else
                {
                    XmlPath.BackColor = Color.White;
                }
            }
            else
            {
                MessageBox.Show("请拖入.xml文件或者.tmp文件");
                XmlPath.Text = "";
            }
        }

        private void ReadTmpFile(string path, bool drop)
        {
            int result = 0;
            XmlFilePath = path;
            Properties.Settings.Default.XmlOrTempPath = XmlFilePath; /* 记忆路径 */
            if (drop)
            {
                Properties.Settings.Default.S19Path = "NULL"; /* 记忆路径 */
            }
            Properties.Settings.Default.Save();

            XmlPath.Text = Path.GetFileName(XmlFilePath);

            if (XmlPath.Text.Contains(".tmp"))
            {
                XmlData = new List<byte>();
                XmlDataLength = 1322;
                result = util.tmpFileRead.tmpFileTobytes(XmlFilePath, XmlDataLength, ref XmlData, ref startAddr, ref BinData, ref FlashDriverStartAddr_V11, ref FlashDriverData_V11);

                if (-1 == result)
                {
                    XmlPath.BackColor = Color.Red;
                    XmlPath.Text = "TMP文件地址无效";
                }
                else if(1 == result)
                {
                    S19InputTextShowOrHide(false);
                    XmlPath.BackColor = Color.White;
                }
                else
                {
                    S19InputTextShowOrHide(true);
                    S19FilePath = Properties.Settings.Default.S19Path; /* 恢复路径 */
                    S19Addr.Text = Path.GetFileName(S19FilePath);
                    FileRead(S19FilePath, false);
                    XmlPath.BackColor = Color.White;
                }
            }
            else
            {
                MessageBox.Show("请拖入.xml文件或者.tmp文件");
                XmlPath.Text = "";
            }
        }
        private void update_Load(object sender, EventArgs e)
        {
            this.Text = "Update  " + BaseParamter.softVersion;
            button_UpdateStart.Enabled = false;
            Main.updateOpenFlag = true;

            Task task = new Task(() =>
            {
                if (null == Main.main.pCAN_API)
                {
                    Main.main.pCAN_API = new PCAN_API.PCAN_API();
                }
                else
                {
                    /* empty */
                }

                string resourceName = "";
                if (Properties.Settings.Default.JsonSelect.Equals("自动加载"))
                {
                    StreamReader Reader;
                    if ((BaseParamter.SelectProjectTypeEnum)BaseParamter.SelectProjectType == BaseParamter.SelectProjectTypeEnum.LingPao)
                    {
                        resourceName = "PCAN_Client.Resources.V11download.json";
                        Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                    }
                    else if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_Old))
                    {
                        resourceName = "PCAN_Client.Resources.V50download.json";
                        Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                    }
                    else
                    {
                        resourceName = "PCAN_Client.Resources.VQ_ASdownload.json";
                        Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                    }
                    if (!(Reader is null))
                    {
                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke((EventHandler)(delegate
                            {
                                MemoryReadJson(Reader.ReadToEnd());
                            }));
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                }

                GetPCAN_ComRefresh();
                GetCanoe_ComRefresh();
                BaseParamter.MngBaseParamterInit();
                resourceName = "PCAN_Client.Resources.Flashdriver_NEW.s19";
                FlashDriverData_V11 = new List<byte>();
                util.S19DataRead.MngS19SrecToBin(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)), ref FlashDriverStartAddr_V11, ref FlashDriverData_V11);/* 读取flashDriver数据 */

                resourceName = "PCAN_Client.Resources.V50_FLASHDriver.s19";
                FlashDriverData_V50 = new List<byte>();
                util.S19DataRead.MngS19SrecToBin(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)), ref FlashDriverStartAddr_V50, ref FlashDriverData_V50);/* 读取flashDriver数据 */

                if (BaseParamter.Pre_b_OnlyUpdateFlag)
                {
                    util.USBEventWatch.StartWMIWatcher(GetPCAN_ComRefresh);
                }
            });
            task.Start();
 
            if (Properties.Settings.Default.CANType.Equals("CAN"))
            {
                radioButtonCAN.Checked = true;
                Main.CanFDFlag = false;
            }
            else if (Properties.Settings.Default.CANType.Equals("CANFD"))
            {
                radioButtonCANFD.Checked = true;
                Main.CanFDFlag = true;
            }
            else
            {
                radioButtonCAN.Checked = true;
                Main.CanFDFlag = false;
            }

            textBoxDelayus.Text = Properties.Settings.Default.Delayus;
            //autoSizeFormClass.controllInitializeSize(this);


            /* 测试秘钥生成 */
            //var privateKey = Enumerable.Repeat((byte)0xFF, 16).ToArray();
            //var result = Signer.SignData(FlashDriverData_V50.ToArray(), privateKey);
            //Console.WriteLine(BitConverter.ToString(result).Replace("-", ""));

            if (!Properties.Settings.Default.XmlOrTempPath.Equals("NULL"))
            {
                XmlFilePath = Properties.Settings.Default.XmlOrTempPath;/* 恢复路径 */
                XmlPath.Text = Path.GetFileName(XmlFilePath);
                FileRead(XmlFilePath, false);
            }
            if (!Properties.Settings.Default.S19Path.Equals("NULL"))
            {
                S19FilePath = Properties.Settings.Default.S19Path; /* 恢复路径 */
                S19Addr.Text = Path.GetFileName(S19FilePath);
                FileRead(S19FilePath, false);
            }

            SoftVersion.Text = Properties.Settings.Default.SoftwareVersion;
            HardVersion.Text = Properties.Settings.Default.HardwareVersion;

            if (Properties.Settings.Default.selectDevice.Equals("PCAN"))
            {
                radioButton_PCAN.Checked = true;
            }
            else if (Properties.Settings.Default.selectDevice.Equals("CANOE"))
            {
                radioButton_CANOE.Checked = true;
            }
            else
            {
                radioButton_PCAN.Checked = true;
            }
            MemoryReadValue();

            UDS_TX_ID.Text = BaseParamter.UDS_TX_ID.ToString("x2");
            UDS_RX_ID.Text = BaseParamter.UDS_RX_ID.ToString("x2");

            if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.LingPao))
            {
                radioButtonLP.Checked = true;
                XmlPath.Enabled = true;
                BaseParamter.FlashDriverSelectIndex = 0;
            }
            else if (BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_Old))
            {
                radioButtonVQ.Checked = true;
                XmlPath.Enabled = false;
                BaseParamter.FlashDriverSelectIndex = 1;
            }
            else
            {
                VQ_New.Checked = true;
                XmlPath.Enabled = false;
                BaseParamter.FlashDriverSelectIndex = 2;
            }

            timer1.Interval = 100;
            timer1.Start();
            comboBox_JsonSelect.Items.Clear();
            comboBox_JsonSelect.Items.Add("自动加载");
            comboBox_JsonSelect.Items.Add("自定义");
            if (Properties.Settings.Default.JsonSelect.Equals("自动加载"))
            {
                comboBox_JsonSelect.SelectedIndex = 0;
            }
            else
            {
                comboBox_JsonSelect.SelectedIndex = 1;
            }

            DLC.Items.Clear();
            DLC.Items.Add("8");
            DLC.Items.Add("16");
            DLC.Items.Add("24");
            DLC.Items.Add("32");
            DLC.Items.Add("64");
            if(BaseParamter.IsSelectProjectType(BaseParamter.SelectProjectTypeEnum.BeiQi_New))
            {
                DLC.SelectedIndex = Properties.Settings.Default.SetDLCSelectIndex;
            }
            else
            {
                DLC.SelectedIndex = 0;
                Properties.Settings.Default.SetDLCSelectIndex = DLC.SelectedIndex;
                Properties.Settings.Default.Save();
            }

            if (radioButtonLP.Checked)
            {
                BootVersion.Enabled = false;
            }
            else
            {
                BootVersion.Enabled = true;
            }
        }

        private void update_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
                this.Cursor = System.Windows.Forms.Cursors.Arrow;  //指定鼠标形状（更好看）  
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void update_DragDrop(object sender, DragEventArgs e)
        {
            FileRead(((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString(), true);
        }

        internal void SetprogressBarMaxValue(int value)
        {
            if(value == 0)
            {
                setprogressBarStep = -1;
            }
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    progressBar1.Value = 0;
                    progressBar1.Minimum = 0;
                    progressBar1.Maximum = value;
                    progressBar1.Step = 1;
                }));
            }
            else
            {
                /* empty */
            }
        }
        internal void SetprogressBarStep(int value)
        {
            setprogressBarStep = value;
            //if (this.IsHandleCreated)
            //{
            //    this.BeginInvoke((EventHandler)(delegate
            //    {
            //        progressBar1.Value = value;
            //    }));
            //}
            //else
            //{
            //    /* empty */
            //}
        }

        internal Boolean CAN_Connect()
        {
            Boolean result = false;
            Main.main.pCAN_API.PCAN_ChannelUninitialize();
            button1.Text = "连接";
            if (comboBox1.Items.Count > 0)
            {
                if (null == Main.main.pCAN_API)
                {
                    Main.main.pCAN_API = new PCAN_API.PCAN_API();
                }
                else
                {
                    /* empty */
                }
                int channel;
                string[] pcanChannel = comboBox1.Text.Split('_', '(');
                if (3 == pcanChannel.Length)
                {
                    int.TryParse(pcanChannel[1], out channel);

                    Main.main.pCAN_API.SetPcanChannel(channel - 1);
                    if (true == Main.main.pCAN_API.Connect(Main.CanFDFlag))
                    {
                        Main.pcanOpenFlag = true;
                        comboBox1.Items[comboBox1.SelectedIndex] = "USB_" + (comboBox1.SelectedIndex + 1) + "(已连接)";
                        result = true;
                    }
                    else
                    {
                        if (true == Main.main.pCAN_API.Connect(Main.CanFDFlag))
                        {
                            Main.pcanOpenFlag = true;
                            comboBox1.Items[comboBox1.SelectedIndex] = "USB_" + (comboBox1.SelectedIndex + 1) + "(已连接)";
                            result = true;
                        }
                    }
                }
                else
                {
                    /* empty */
                }
            }
            else
            {
                MessageBox.Show("未连接PCAN设备，请重新选择设备");
                result = false;
            }
            return result;
        }
        internal Boolean CANOE_Connect()
        {
            Boolean result = false;
            if (comboBox_CanoeChannel.Items.Count == 0)
            {
                MessageBox.Show("未连接CANOE设备，请重新选择设备");
                result = false;
            }
            else
            {
                Main.main.canoe_API.CANOE_Close();
                Thread.Sleep(100);
                for (int i = 0; i < Main.driverConfig.channelCount; i++)
                {
                    if (Main.driverConfig.channel[i].name.Contains(comboBox_CanoeChannel.Text))
                    {
                        if (Main.main.canoe_API.CANOE_Open((ulong)(1 << Main.driverConfig.channel[i].channelIndex), Main.CanFDFlag))
                        {
                            Main.canoeOpenFlag = true;
                            Thread.Sleep(100);
                            result = true;
                        }
                        else
                        {
                            Main.canoeOpenFlag = false;
                            MessageBox.Show("连接失败");
                            result = false;
                        }
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
        private void buttonUpdateStart_Click(object sender, EventArgs e)
        {
            UpdateStart();
            //var bytes = new byte[1]{ 0x00};
            //Download.GetUDSServ27Key_VQ_New(ref bytes);
        }

        public void update_ShowTextRefresh(string str)
        {
            lock (stringBuilder)
            {
                stringBuilder.Append(str);
            }
            //if (this.IsHandleCreated)
            //{
            //    this.BeginInvoke((EventHandler)(delegate
            //    {
            //        ShowText.AppendText(str);
            //        ShowText.ScrollToCaret();
            //    }));
            //}
            //else
            //{
            //    /* empty */
            //}
        }

        private void update_FormClosing(object sender, FormClosingEventArgs e)
        {
            Main.updateOpenFlag = false;
        }
        public void Update_RxData(TPCANMsg msg, TPCANTimestamp timesamp)
        {
            Download.Download_RxData(msg, timesamp);
        }

        internal string Update_SoftVersion(string ver)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    if (SoftVersion.Text.Equals(""))
                    {
                        Download.SoftVersionInputNull = true;
                    }
                    else
                    {
                        Download.SoftVersionInputNull = false;
                    }
                    if ((BaseParamter.SelectProjectTypeEnum)BaseParamter.SelectProjectType == BaseParamter.SelectProjectTypeEnum.BeiQi_Old)
                    {
                        if (SoftVersion.Text.Equals(ver.Replace(" ", "")))
                        {
                            Download.SoftVersionChecked = true;
                            SoftVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.SoftVersionChecked = false;
                            SoftVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("软件版本：" + ver.Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                    else
                    {
                        if (SoftVersion.Text.Equals(ver.Replace("\0", "").Replace(" ", "")))
                        {
                            Download.SoftVersionChecked = true;
                            SoftVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.SoftVersionChecked = false;
                            SoftVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("软件版本：" + ver.Replace("\0", "").Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                }));
            }
            else
            {
                /* empty */
            }
            return ver;
        }
        internal string Update_HardVersion(string ver)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    string inputStr = HardVersion.Text;
                    if(inputStr.Equals(""))
                    {
                        Download.HardVersionInputNull = true;
                    }
                    else
                    {
                        Download.HardVersionInputNull = false;
                    }
                    if ((BaseParamter.SelectProjectTypeEnum)BaseParamter.SelectProjectType == BaseParamter.SelectProjectTypeEnum.BeiQi_Old)
                    {
                        if (inputStr.Equals(ver.Replace(" ", "")))
                        {
                            Download.HardVersionChecked = true;
                            HardVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.HardVersionChecked = false;
                            HardVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("硬件版本：" + ver.Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                    else
                    {
                        if (inputStr.Equals(ver.Replace("\0", "").Replace(" ", "")))
                        {
                            Download.HardVersionChecked = true;
                            HardVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.HardVersionChecked = false;
                            HardVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("硬件版本：" + ver.Replace("\0", "").Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                }));
            }
            else
            {
                /* empty */
            }
            return ver;
        }

        internal string Update_BootVersion(string ver)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    string inputStr = BootVersion.Text;
                    if (inputStr.Equals(""))
                    {
                        Download.BootVersionInputNull = true;
                    }
                    else
                    {
                        Download.BootVersionInputNull = false;
                    }
                    if ((BaseParamter.SelectProjectTypeEnum)BaseParamter.SelectProjectType == BaseParamter.SelectProjectTypeEnum.BeiQi_Old)
                    {
                        if (inputStr.Equals(ver.Replace(" ", "")))
                        {
                            Download.BootVersionChecked = true;
                            BootVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.BootVersionChecked = false;
                            BootVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("Boot版本：" + ver.Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                    else
                    {
                        if (inputStr.Equals(ver.Replace("\0", "").Replace(" ", "")))
                        {
                            Download.BootVersionChecked = true;
                            BootVersion.BackColor = Color.ForestGreen;
                        }
                        else
                        {
                            Download.BootVersionChecked = false;
                            BootVersion.BackColor = Color.Yellow;
                        }
                        ShowText.AppendText("Boot版本：" + ver.Replace("\0", "").Replace(" ", "") + "\r\n");
                        ShowText.ScrollToCaret();
                    }
                }));
            }
            else
            {
                /* empty */
            }
            return ver;
        }
        private void SoftVersion_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.SoftwareVersion = SoftVersion.Text; /* 记忆版本号 */
            Properties.Settings.Default.Save();
        }

        private void HardVersion_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.HardwareVersion = HardVersion.Text; /* 记忆版本号 */
            Properties.Settings.Default.Save();
        }

        private void update_FormClosed(object sender, FormClosedEventArgs e)
        {
            if(!BaseParamter.Pre_b_OnlyUpdateFlag)
            {
                Main.main.Show();
            }
            else
            {
                /* empty */
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            string str = "";
            lock (stringBuilder)
            {
                str = stringBuilder.ToString();
                stringBuilder = new StringBuilder();
            }
            ShowText.AppendText(str);
            ShowText.ScrollToCaret();
            if(-1 != setprogressBarStep)
            {
                progressBar1.Value = setprogressBarStep;
            }
            //if (this.IsHandleCreated)
            //{
            //    this.BeginInvoke((EventHandler)(delegate
            //    {
            //        ShowText.AppendText(str);
            //        ShowText.ScrollToCaret();
            //    }));
            //}
            //else
            //{
            //    /* empty */
            //}
        }

        private void radioButton_PCAN_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.selectDevice = "PCAN";
            Properties.Settings.Default.Save();
        }

        private void radioButton_CANOE_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.selectDevice = "CANOE";
            Properties.Settings.Default.Save();
        }

        /* 自定义指令 */
        private void DataGridView1_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (SelectRowIndex == -1)
            {
                return;
            }
            DataRow row = dataTable.Rows[SelectRowIndex];
            SelectRowIndex = e.RowIndex; /* 获取点击的行索引 */
            SelectColumnIndex = e.ColumnIndex;
            int index = 0;
            foreach (DataColumn column in dataTable.Columns) /* 单击更改使能/失能 */
            {
                if (column.ColumnName.Contains("Enable"))
                {
                    if (SelectColumnIndex == index)
                    {
                        if (row[SelectColumnIndex].Equals("True"))
                        {
                            dataTable.Rows[SelectRowIndex].SetField(column.ColumnName, "False");
                            return;
                        }
                        else
                        {
                            dataTable.Rows[SelectRowIndex].SetField(column.ColumnName, "True");
                            return;
                        }
                    }
                }
                else
                {
                    index++;
                }
            }
        }
        internal int CustomChangeResult(string str)
        {
            DataRow newRow = dataTable.NewRow();
            string[] strtemps = Regex.Split(str, "###", RegexOptions.IgnoreCase);
            int index = 0;

            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                if (index < strtemps.Length)
                {
                    newRow[column.HeaderText] = strtemps[index++];
                }
                else
                {
                    /* empty */
                }
            }
            dataTable.Rows.RemoveAt(SelectRowIndex);
            dataTable.Rows.InsertAt(newRow, SelectRowIndex);
            dataGridView1.Refresh();
            MemoryWriteValue();
            return 0;
        }
        internal int CustomAddResult(string str)
        {
            DataRow newRow = dataTable.NewRow();
            string[] strtemps = Regex.Split(str, "###", RegexOptions.IgnoreCase);
            int index = 0;

            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                newRow[column.HeaderText] = strtemps[index++];
            }
            if (SelectRowIndex == dataTable.Rows.Count - 1)
            {
                dataTable.Rows.Add(newRow);
            }
            else
            {
                // 在指定行后面插入新行
                dataTable.Rows.InsertAt(newRow, SelectRowIndex);
            }
            SelectRowIndex++;
            dataGridView1.Refresh();
            MemoryWriteValue();
            return 0;
        }
        private void MemoryWriteValue()
        {
            string str = "";
            DataRow row;

            for (int index = 0; index < dataGridView1.Rows.Count; index++)
            {
                row = dataTable.Rows[index];
                for (int i = 0; i < dataGridView1Column; i++)
                {
                    str += row[i].ToString() + "###";
                }
            }
            Properties.Settings.Default.customInstruction = str; /* 记忆 */
            Properties.Settings.Default.Save();
        }

        private void MemoryReadValue()
        {
            string str = Properties.Settings.Default.customInstruction;
            string[] strs = Regex.Split(Properties.Settings.Default.customInstruction, "###", RegexOptions.IgnoreCase);
            if (strs.Length < dataGridView1Column)
            {
                return;
            }
            dataTable.Rows.Clear();
            for (int index = 0; index < ((strs.Length - 1) / dataGridView1Column); index++)
            {
                DataRow newRow = dataTable.NewRow();
                int i = 0;
                foreach (DataGridViewColumn column in dataGridView1.Columns)
                {
                    newRow[column.HeaderText] = strs[index * dataGridView1Column + i].ToString();
                    i++;
                }
                dataTable.Rows.Add(newRow);
            }
            dataGridView1.Refresh();
        }
        private void MoveRow(ref int SelectRowIndex, int direction)
        {
            try
            {
                int newRowIdx = SelectRowIndex + direction;

                if (newRowIdx >= 0 && newRowIdx < dataTable.Rows.Count)
                {
                    // 暂存当前行数据
                    DataRow row = dataTable.Rows[SelectRowIndex];
                    List<string> strings = new List<string>();
                    int index = 0;
                    DataRow newRow = dataTable.NewRow();
                    foreach (DataGridViewColumn column in dataGridView1.Columns)
                    {
                        newRow[column.HeaderText] = row[index++].ToString();
                    }
                    dataTable.Rows.RemoveAt(SelectRowIndex);
                    // 插入到新位置
                    dataTable.Rows.InsertAt(newRow, newRowIdx);

                    // 重新选中新位置的行
                    SelectRowIndex = newRowIdx;
                    dataGridView1.Rows[SelectRowIndex].Selected = true;
                }
                dataGridView1.Refresh();
                MemoryWriteValue();
            }
            catch { }
        }
        private void MemoryWriteJson(string path)
        {
            string str = "";
            DataRow row;
            string jsonString = "{\"name\":\"Kimi\",\"age\":25}";

            File.WriteAllText(path, "");
            for (int index = 0; index < dataTable.Rows.Count; index++)
            {
                row = dataTable.Rows[index];
                jsonString = "{";
                int i = 0;
                foreach (DataGridViewColumn column in dataGridView1.Columns)
                {
                    jsonString += "\"";
                    jsonString += column.HeaderText;
                    jsonString += "\"";
                    jsonString += ":";
                    jsonString += "\"";
                    jsonString += row[i].ToString();
                    jsonString += "\"";
                    if (i != dataGridView1Column - 1)
                    {
                        jsonString += ",";
                    }
                    i++;
                }
                jsonString += "}\r\n";
                File.AppendAllText(path, jsonString);
            }
        }

        private void MemoryReadJson(string str)
        {
            try
            {
                string[] strs = Regex.Split(str, "\r\n", RegexOptions.IgnoreCase);

                if (!strs[0].Contains("True") && !strs[0].Contains("False"))
                {
                    MessageBox.Show("导入的json文件有误！");
                    return;
                }
                if (!strs[0].Equals(""))
                {
                    dataTable.Columns.Clear();
                    dataTable.Rows.Clear();
                    dataTable = new DataTable();
                    JObject joson = (JObject)JsonConvert.DeserializeObject(strs[0]);
                    foreach (JProperty property in joson.Properties())
                    {
                        dataTable.Columns.Add(property.Name);
                    }
                }
                for (int index = 0; index < strs.Length; index++)
                {
                    if (!strs[index].Equals(""))
                    {
                        JObject joson = (JObject)JsonConvert.DeserializeObject(strs[index]);
                        DataRow newRow = dataTable.NewRow();
                        foreach (JProperty property in joson.Properties())
                        {
                            newRow[property.Name] = property.Value;
                        }
                        dataTable.Rows.Add(newRow);
                    }
                    else
                    {
                        /* empty */
                    }
                }

                dataGridView1.DataSource = dataTable;
                dataGridView1.Refresh();
                MemoryWriteValue();
            }
            catch (Exception ex) { MessageBox.Show(ex.ToString()); }
        }

        private void button_Add_Click(object sender, EventArgs e)
        {
            Custom_Add custom_Add = new Custom_Add();
            custom_Add.GetAddValue(CustomAddResult);
        }

        private void button_Delete_Click(object sender, EventArgs e)
        {
            if (0 < dataTable.Rows.Count)
            {
                dataTable.Rows.RemoveAt(SelectRowIndex);
            }
        }

        private void button_MoveUp_Click(object sender, EventArgs e)
        {
            MoveRow(ref SelectRowIndex, -1);
        }

        private void button_MoveDown_Click(object sender, EventArgs e)
        {
            MoveRow(ref SelectRowIndex, 1);
        }

        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (-1 == SelectRowIndex)
            {
                return;
            }
            DataRow row = dataTable.Rows[SelectRowIndex];
            int index = 0;
            List<string> strings = new List<string>();

            foreach (DataColumn column in dataTable.Columns) /* 单击更改使能/失能 */
            {
                if (column.ColumnName.Contains("Enable"))
                {
                    if (SelectColumnIndex == index)
                    {
                        if (row[SelectColumnIndex].Equals("True"))
                        {
                            dataTable.Rows[SelectRowIndex].SetField(column.ColumnName, "False");
                            return;
                        }
                        else
                        {
                            dataTable.Rows[SelectRowIndex].SetField(column.ColumnName, "True");
                            return;
                        }
                    }
                }
                else
                {
                    index++;
                }
            }
            index = 0;
            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                strings.Add(row[index++].ToString());
            }
            Custom_Add custom_Add = new Custom_Add();
            custom_Add.ChangeValue(CustomChangeResult, strings);
        }

        private void buttonSaveXML_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                // 设置文件类型过滤器
                saveFileDialog.Filter = "Text files (*.json)|*.json|All files (*.*)|*.*";

                // 显示对话框
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // 获取用户选择的文件路径
                    string filePath = saveFileDialog.FileName;

                    // 在这里执行文件保存操作
                    MemoryWriteJson(filePath);
                }
            }
        }

        private void buttonLoadXML_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog saveFileDialog = new OpenFileDialog())
            {
                // 设置文件类型过滤器
                saveFileDialog.Filter = "Text files (*.json)|*.json|All files (*.*)|*.*";

                // 显示对话框
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // 获取用户选择的文件路径
                    string filePath = saveFileDialog.FileName;

                    MemoryReadJson(File.ReadAllText(filePath));
                }
            }
        }

        private void radioButtonLP_Click(object sender, EventArgs e)
        {
            string resourceName;
            BootVersion.Enabled = false;

            DLC.SelectedIndex = 0;
            Properties.Settings.Default.SetDLCSelectIndex = DLC.SelectedIndex;
            Properties.Settings.Default.Save();

            BaseParamter.SelectProjectType = (int)BaseParamter.SelectProjectTypeEnum.LingPao;
            Properties.Settings.Default.ProjectSelectIndex = BaseParamter.SelectProjectType;
            Properties.Settings.Default.Save();
            BaseParamter.FlashDriverSelectIndex = (ushort)BaseParamter.SelectProjectType;
            UDS_TX_ID.Text = "0x7B0";
            Properties.Settings.Default.UDS_TX_ID = 0x7B0;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_TX_ID = (ushort)Properties.Settings.Default.UDS_TX_ID;

            UDS_RX_ID.Text = "0x7B8";
            Properties.Settings.Default.UDS_RX_ID = 0x7B8;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_RX_ID = (ushort)Properties.Settings.Default.UDS_RX_ID;
            XmlPath.Enabled = true;
            if (1 != comboBox_JsonSelect.SelectedIndex)
            {
                resourceName = "PCAN_Client.Resources.V11download.json";
                StreamReader Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                MemoryReadJson(Reader.ReadToEnd());
            }
            else
            {
                /* empty */
            }
            resourceName = "PCAN_Client.Resources.Flashdriver_NEW.s19";
            FlashDriverData_V11 = new List<byte>();
            util.S19DataRead.MngS19SrecToBin(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)), ref FlashDriverStartAddr_V11, ref FlashDriverData_V11);/* 读取flashDriver数据 */
        }

        private void radioButtonVQ_Click(object sender, EventArgs e)
        {
            string resourceName;

            BootVersion.Enabled = true;
            DLC.SelectedIndex = 0;
            Properties.Settings.Default.SetDLCSelectIndex = DLC.SelectedIndex;
            Properties.Settings.Default.Save();

            BaseParamter.SelectProjectType = (int)BaseParamter.SelectProjectTypeEnum.BeiQi_Old;
            Properties.Settings.Default.ProjectSelectIndex = BaseParamter.SelectProjectType;
            Properties.Settings.Default.Save();
            BaseParamter.FlashDriverSelectIndex = (ushort)BaseParamter.SelectProjectType;

            UDS_TX_ID.Text = "0x7F0";
            Properties.Settings.Default.UDS_TX_ID = 0x7F0;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_TX_ID = (ushort)Properties.Settings.Default.UDS_TX_ID;

            UDS_RX_ID.Text = "0x7F8";
            Properties.Settings.Default.UDS_RX_ID = 0x7F8;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_RX_ID = (ushort)Properties.Settings.Default.UDS_RX_ID;
            XmlPath.Enabled = false;
            if (1 != comboBox_JsonSelect.SelectedIndex)
            {
                resourceName = "PCAN_Client.Resources.V50download.json";
                StreamReader Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                MemoryReadJson(Reader.ReadToEnd());
            }
            else
            {
                /* empty */
            }
            resourceName = "PCAN_Client.Resources.V50_FLASHDriver.s19";
            FlashDriverData_V50 = new List<byte>();
            util.S19DataRead.MngS19SrecToBin(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)), ref FlashDriverStartAddr_V50, ref FlashDriverData_V50);/* 读取flashDriver数据 */
        }

        private void UDS_RX_ID_TextChanged(object sender, EventArgs e)
        {
            int temp;

            try
            {
                temp = Int32.Parse(UDS_RX_ID.Text, System.Globalization.NumberStyles.HexNumber);
                Properties.Settings.Default.UDS_RX_ID = temp;
                Properties.Settings.Default.Save();
                BaseParamter.UDS_RX_ID = (ushort)temp;
            }
            catch { }
        }

        private void UDS_TX_ID_TextChanged(object sender, EventArgs e)
        {
            int temp;
            try
            {
                temp = Int32.Parse(UDS_TX_ID.Text, System.Globalization.NumberStyles.HexNumber);
                Properties.Settings.Default.UDS_TX_ID = temp;
                Properties.Settings.Default.Save();
                BaseParamter.UDS_TX_ID = (ushort)temp;
            }
            catch { }
        }

        private void update_SizeChanged(object sender, EventArgs e)
        {
            //autoSizeFormClass.controlAutoSize(this);
        }

        private void textBoxDelayus_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Delayus = textBoxDelayus.Text;
            Properties.Settings.Default.Save();
        }

        private void comboBox_JsonSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.JsonSelect = comboBox_JsonSelect.Text;
            Properties.Settings.Default.Save();
        }

        private string GetPCAN_ComRefresh()
        {
            Boolean flag = false;
            if (null == Main.main.pCAN_API)
            {
                Main.main.pCAN_API = new PCAN_API.PCAN_API();
            }
            else
            {
                /* empty */
            }

            List<string> PCAN_Channel = Main.main.pCAN_API.GetPCAN_ChannelRefresh();

            if(comboBox1.Items.Count == PCAN_Channel.Count)
            {
                for(int i=0; i< comboBox1.Items.Count; i++)
                {
                    if(!comboBox1.Items[i].Equals(PCAN_Channel[i]))
                    {
                        break;
                    }
                    if(i == (comboBox1.Items.Count-1))
                    {
                        return "None";
                    }
                }
            }
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    comboBox1.Items.Clear();
                    for (int i = 0; i < PCAN_Channel.Count; i++)
                    {
                        comboBox1.Items.Add(PCAN_Channel[i]);
                        if (PCAN_Channel[i].Contains(Properties.Settings.Default.PCAN_Channel))
                        {
                            comboBox1.SelectedIndex = i;
                            flag = true;
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                    if (!flag && 0 < comboBox1.Items.Count)
                    {
                        comboBox1.SelectedIndex = 0;
                    }

                    if(0 == comboBox1.Items.Count)
                    {
                        comboBox1.Items.Add("未连接PCAN");
                        comboBox1.SelectedIndex = 0;
                    }
                }));
            }
            else
            {
                /* empty */
            }
            return "OK";
        }

        private void GetCanoe_ComRefresh()
        {
            if (null == Main.main.canoe_API)
            {
                Main.main.canoe_API = new Canoe_API.CanOe_API();
            }
            else
            {
                /* empty */
            }
            Main.driverConfig = Main.main.canoe_API.FindAllChannel(10, Main.CanFDFlag);

            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    comboBox_CanoeChannel.Items.Clear();
                    for (int i = 0; i < Main.driverConfig.channelCount; i++)
                    {
                        if (!Main.driverConfig.channel[i].name.Contains("Virtual Channel"))
                        {
                            if (Main.main.canoe_API.CANOE_Open((ulong)(1 << ((int)Main.driverConfig.channel[i].channelIndex)), Main.CanFDFlag))
                            {
                                comboBox_CanoeChannel.Items.Add(Main.driverConfig.channel[i].name);
                                if (Main.driverConfig.channel[i].name.Contains(Properties.Settings.Default.CANoe_Channel))
                                {
                                    comboBox_CanoeChannel.SelectedIndex = comboBox_CanoeChannel.Items.Count - 1;
                                }
                                Main.main.canoe_API.CANOE_Close();
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                    }
                    if (comboBox_CanoeChannel.Items.Count > 0)
                    {
                        if (!comboBox_CanoeChannel.Text.Contains(Properties.Settings.Default.CANoe_Channel))
                        {
                            comboBox_CanoeChannel.SelectedIndex = 0;
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                    else
                    {
                        comboBox_CanoeChannel.Items.Add("未连接CANoe");
                        comboBox_CanoeChannel.SelectedIndex = 0;
                    }

                    button_UpdateStart.Enabled = true;
                }));
            }
            else
            {
                /* empty */
            }
        }
        private void comboBox1_Click(object sender, EventArgs e)
        {
            Main.pcanOpenFlag = false;
            if (button1.Text.Equals("已连接"))
            {
                button1.Text = "连接";
                Main.main.pCAN_API.PCAN_ChannelUninitialize();
            }
            Task task = new Task(() =>
            {
                GetPCAN_ComRefresh();
            });
            task.Start();
        }

        private void comboBox_CanoeChannel_Click(object sender, EventArgs e)
        {
            Main.canoeOpenFlag = false;
            try
            {
                GetCanoe_ComRefresh();
            }
            catch { }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!comboBox1.Text.Contains("未连接"))
            {
                Properties.Settings.Default.PCAN_Channel = comboBox1.Text.Split('(')[0];
                Properties.Settings.Default.Save();
            }
            if(button1.Text.Equals("已连接"))
            {
                Main.main.pCAN_API.PCAN_ChannelUninitialize();
                button1.Text = "连接";
                GetPCAN_ComRefresh();
            }
        }

        private void comboBox_CanoeChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!comboBox_CanoeChannel.Text.Contains("未连接"))
            {
                Properties.Settings.Default.CANoe_Channel = comboBox_CanoeChannel.Text;
                Properties.Settings.Default.Save();
            }
        }

        private void radioButtonCANFD_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.CANType = "CANFD";
            Properties.Settings.Default.Save();
            Main.CanFDFlag = true;
        }

        private void radioButtonCAN_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.CANType = "CAN";
            Properties.Settings.Default.Save();
            Main.CanFDFlag = false;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(radioButton_PCAN.Checked)
            {
                if (null == Main.main.pCAN_API)
                {
                    Main.main.pCAN_API = new PCAN_API.PCAN_API();
                }
                else
                {
                    /* empty */
                }
                if (button1.Text.Equals("已连接"))
                {
                    Main.main.pCAN_API.PCAN_ChannelUninitialize();
                    button1.Text = "连接";
                    Main.pcanOpenFlag = false;
                    GetPCAN_ComRefresh();
                }
                else if (comboBox1.Text.Contains("已占用"))
                {
                    MessageBox.Show("当前通道已被占用，请选择其他通道！");
                }
                else
                {
                    if (Main.main.canoe_API.aliveFlag)
                    {
                        Main.main.CANoeConnect(false);
                    }
                    int channel;
                    string[] pcanChannel = comboBox1.Text.Split('_', '(');
                    if (3 == pcanChannel.Length)
                    {
                        int.TryParse(pcanChannel[1], out channel);

                        Main.main.pCAN_API.SetPcanChannel(channel - 1);
                        if (true == Main.main.pCAN_API.Connect(Main.CanFDFlag))
                        {
                            comboBox1.Items[comboBox1.SelectedIndex] = "USB_" + (comboBox1.SelectedIndex + 1) + "(已连接)";
                            button1.Text = "已连接";
                            Main.pcanOpenFlag = true;
                        }
                        else
                        {
                            button1.Text = "连接";
                            Main.pcanOpenFlag = false;
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                }
            }
            else if(radioButton_CANOE.Checked)
            {
                if (null == Main.main.canoe_API)
                {
                    Main.main.canoe_API = new Canoe_API.CanOe_API();
                }
                else
                {
                    /* empty */
                }
                if (button1.Text.Equals("已连接"))
                {
                    Main.main.canoe_API.CANOE_Close();
                    button1.Text = "连接";
                    Main.canoeOpenFlag = false;
                }
                else
                {
                    if (0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                    {
                        Main.main.PCAN_Connect(false);
                    }
                    for (int i = 0; i < Main.driverConfig.channelCount; i++)
                    {
                        if (Main.driverConfig.channel[i].name.Contains(comboBox_CanoeChannel.Text))
                        {
                            if (Main.main.canoe_API.CANOE_Open((ulong)(1 << Main.driverConfig.channel[i].channelIndex), Main.CanFDFlag))
                            {
                                button1.Text = "已连接";
                                Main.canoeOpenFlag = true;
                            }
                            else
                            {
                                button1.Text = "连接";
                                Main.canoeOpenFlag = false;
                                MessageBox.Show("连接失败");
                            }
                            break;
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                }
            }
        }

        private void VQ_New_Click(object sender, EventArgs e)
        {
            string resourceName;

            BootVersion.Enabled = true;

            BaseParamter.SelectProjectType = (int)BaseParamter.SelectProjectTypeEnum.BeiQi_New;
            Properties.Settings.Default.ProjectSelectIndex = BaseParamter.SelectProjectType;
            Properties.Settings.Default.Save();
            BaseParamter.FlashDriverSelectIndex = (ushort)BaseParamter.SelectProjectType;

            UDS_TX_ID.Text = "0x7F0";
            Properties.Settings.Default.UDS_TX_ID = 0x7F0;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_TX_ID = (ushort)Properties.Settings.Default.UDS_TX_ID;

            UDS_RX_ID.Text = "0x7F8";
            Properties.Settings.Default.UDS_RX_ID = 0x7F8;
            Properties.Settings.Default.Save();
            BaseParamter.UDS_RX_ID = (ushort)Properties.Settings.Default.UDS_RX_ID;
            XmlPath.Enabled = false;
            if (1 != comboBox_JsonSelect.SelectedIndex)
            {
                resourceName = "PCAN_Client.Resources.VQ_ASdownload.json";
                StreamReader Reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName));
                MemoryReadJson(Reader.ReadToEnd());
            }
            else
            {
                /* empty */
            }
            resourceName = "PCAN_Client.Resources.V50_FLASHDriver.s19";
            FlashDriverData_V50 = new List<byte>();
            util.S19DataRead.MngS19SrecToBin(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)), ref FlashDriverStartAddr_V50, ref FlashDriverData_V50);/* 读取flashDriver数据 */
        }

        private void DLC_SelectedIndexChanged(object sender, EventArgs e)
        {
            int[] ints = new int[5] {8,16,24,32,64 };
            Properties.Settings.Default.SetDLCSelectIndex = DLC.SelectedIndex;
            Properties.Settings.Default.Save();
            BaseParamter.SetCanSetDlc(ints[DLC.SelectedIndex]);
            if (radioButton_PCAN.Checked)
            {
                textBoxDelayus.Text = delay_us_pcan[DLC.SelectedIndex].ToString();
            }
            else
            {
                textBoxDelayus.Text = delay_us_canoe[DLC.SelectedIndex].ToString();
            }
        }

        private void radioButton_PCAN_Click(object sender, EventArgs e)
        {
            /* 重新设置延时 */
            int[] ints = new int[5] { 8, 16, 24, 32, 64 };
            textBoxDelayus.Text = delay_us_pcan[DLC.SelectedIndex].ToString();
            BaseParamter.SetCanSetDlc(ints[DLC.SelectedIndex]);
        }

        private void radioButton_CANOE_Click(object sender, EventArgs e)
        {
            /* 重新设置延时 */
            int[] ints = new int[5] { 8, 16, 24, 32, 64 };
            textBoxDelayus.Text = delay_us_canoe[DLC.SelectedIndex].ToString();
            BaseParamter.SetCanSetDlc(ints[DLC.SelectedIndex]);
        }
    }
}
