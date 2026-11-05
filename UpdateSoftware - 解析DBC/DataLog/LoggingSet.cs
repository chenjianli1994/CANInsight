using Peak.Can.Basic;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PCAN_Client.DataLog.Logging;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace PCAN_Client.DataLog
{
    public partial class LoggingSet : Form
    {
        public static string FileAddress = "";
        public static string Header_information = "";
        public static int Frequency = 10;
        public static LoggingSet loggingSet = new LoggingSet();
        public static string SaveFileType = "ASC";
        public static int SaveFileType_int { get => PCAN_Client.CAN_API.RecordingState.SaveFileTypeInt; set => PCAN_Client.CAN_API.RecordingState.SaveFileTypeInt = value; }
        public static string BINFilepath = "";
        public static bool changeFlag = false;


        public static double[] saveExcelTimeBuf = new double[8] {0.1, 0.5, 1, 2, 3, 4, 5, 10};
        public static bool saveExcelFlag = false;
        public static uint saveExcelTime = 1000; /* ms */
        public static uint comboBoxExcelSaveTimeSelectIndex = 0;

        /// <summary>勾选录制的逻辑通道号集合；空集合视为全选（兼容旧行为）</summary>
        public static HashSet<byte> RecordLogicChannels = new HashSet<byte>();

        /// <summary>该逻辑通道是否被勾选录制</summary>
        internal static bool IsRecordChannel(byte logicChannel)
        {
            return RecordLogicChannels.Count == 0 || RecordLogicChannels.Contains(logicChannel);
        }

        /// <summary>只录制DBC中定义的报文（唯一真源在 Core 的 RecordingState，供接收链路直接读取）</summary>
        public static bool DbcOnlyRecord
        {
            get { return PCAN_Client.CAN_API.RecordingState.DbcOnlyRecord; }
            set { PCAN_Client.CAN_API.RecordingState.DbcOnlyRecord = value; }
        }

        /// <summary>
        /// 报文是否在对应通道的DBC中定义（录制过滤用）。
        /// 路由规则与显示/解析一致：多通道模式严格用该通道自己的DBC（未挂DBC的通道不录制，
        /// 避免跨通道同ID串扰）；无通道配置时用全局聚合视图。
        /// </summary>
        internal static bool IsRecordMessage(uint canId, byte logicChannel)
        {
            if (BaseParamter.BusChannels.Count > 0)
            {
                var helper = BaseParamter.GetDbcHelperByChannel(logicChannel);
                return helper?.dbcFile?.messageDict != null && helper.dbcFile.messageDict.ContainsKey(canId);
            }
            return BaseParamter.dbcHelper?.dbcFile?.messageDict != null
                   && BaseParamter.dbcHelper.dbcFile.messageDict.ContainsKey(canId);
        }

        /// <summary>当前是否至少有一份可用DBC（全局聚合或任一行通道），用于"只录制DBC报文"的前置校验</summary>
        private static bool HasAnyDbc()
        {
            if (BaseParamter.dbcHelper?.dbcFile?.messages != null && BaseParamter.dbcHelper.dbcFile.messages.Count > 0)
                return true;
            foreach (var ch in BaseParamter.BusChannels)
            {
                var helper = ch?.DbcHelper;
                if (helper?.dbcFile?.messages != null && helper.dbcFile.messages.Count > 0) return true;
            }
            return false;
        }

        /// <summary>分段大小下拉项对应的 MB 值（0 = 不分割），索引与下拉框一一对应</summary>
        public static readonly int[] SplitSizeMbOptions = new int[] { 0, 64, 128, 256, 512 };

        /// <summary>当前分段阈值（字节）：-1 = 不分割（Recorder 约定），由下拉框索引映射，默认 128 MB</summary>
        public static long SplitSizeBytes
        {
            get
            {
                int idx = Properties.Settings.Default.comboSplitSizeIndex;
                if (idx < 0 || idx >= SplitSizeMbOptions.Length) idx = 2;
                int mb = SplitSizeMbOptions[idx];
                return mb <= 0 ? -1 : mb * 1024L * 1024L;
            }
        }

        /// <summary>本轮录制开始时间（状态栏显示已录时长；未录制时为 MinValue）</summary>
        public static DateTime RecordStartTime = DateTime.MinValue;

        private static int _lastStatusTick;

        /// <summary>录制中周期刷新状态栏（已录时长 + 当前文件大小 / 分段阈值），由录制定时器调用</summary>
        internal void UpdateRecordingStatus()
        {
            if (this.IsDisposed || !this.Visible || !Logging.SaveFlag) return;
            int now = Environment.TickCount;
            if (now - _lastStatusTick < 1000) return;   // 1 秒一次足够
            _lastStatusTick = now;
            RefreshStatusLabel();
        }

        /// <summary>按当前录制状态刷新状态栏（开始/停止/周期刷新共用）</summary>
        private void RefreshStatusLabel()
        {
            if (this.IsDisposed) return;
            if (!Logging.SaveFlag)
            {
                labelStatus.Text = "状态：未录制";
                labelStatus.ForeColor = System.Drawing.Color.Gray;
                return;
            }
            string path = SaveFileType_int == 1 ? Logging.NowBLFFileAddr_str : Logging.NowASCFileAddr;
            string sizeText = "";
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    sizeText = (new FileInfo(path).Length / 1048576.0).ToString("F1") + " MB";
            }
            catch { }
            string elapsed = "";
            if (RecordStartTime != DateTime.MinValue)
            {
                TimeSpan ts = DateTime.Now - RecordStartTime;
                elapsed = string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }
            long limit = SplitSizeBytes;
            string limitText = limit > 0 ? (limit / 1048576L) + " MB" : "不分割";
            string scope = DbcOnlyRecord ? "（仅DBC报文）" : "";
            labelStatus.Text = string.Format("状态：录制中{0}  已录 {1}  {2} / {3}\r\n{4}",
                scope, elapsed, sizeText, limitText, path);
            labelStatus.ForeColor = System.Drawing.Color.IndianRed;
        }

        /// <summary>通道列表项（Tag 存逻辑通道号）</summary>
        private class ChannelItem
        {
            internal readonly byte LogicChannel;
            private readonly string _text;
            internal ChannelItem(byte logicChannel, string text) { LogicChannel = logicChannel; _text = text; }
            public override string ToString() { return _text; }
        }

        public LoggingSet()
        {
            InitializeComponent();
        }

        private void LoggingSet_Load(object sender, EventArgs e)
        {
            UiTheme.StyleForm(this);
            // 按钮统一为工具整体的无边框图标风格
            UiTheme.StyleButton(button1, "folder");
            UiTheme.StyleButton(button2, "play");
            UiTheme.StyleButton(buttonStop, "stop");
            labelStatus.Font = UiTheme.UiFont;
            labelStatus.ForeColor = System.Drawing.Color.Gray;
            if (Properties.Settings.Default.SaveFileType.Equals("ASC"))
            {
                radioButton1.Checked = true;
                SaveFileType = "ASC";
                SaveFileType_int = 0;
            }
            else
            {
                radioButton2.Checked = true;
                SaveFileType = "BLF";
                SaveFileType_int = 1;
            }

            if(Properties.Settings.Default.checkBoxSaveExcelChecked)
            {
                checkBoxSaveExcel.Checked = true;
            }
            else
            {
                checkBoxSaveExcel.Checked = false;
            }

            checkBoxDbcOnly.Checked = Properties.Settings.Default.checkBoxDbcOnlyChecked;
            DbcOnlyRecord = checkBoxDbcOnly.Checked;


            fileAdress.Text = AppDomain.CurrentDomain.BaseDirectory + "data Log\\";

            try
            {
                if (!Directory.Exists(fileAdress.Text))//如果路径不存在
                {
                    Directory.CreateDirectory(fileAdress.Text);//创建一个路径的文件夹
                }
            }
            catch { }
            FileAddress = fileAdress.Text;

            comboBoxExcelSaveTime.Items.Clear();
            foreach (var time in saveExcelTimeBuf)
            {
                comboBoxExcelSaveTime.Items.Add(time.ToString());
            }
            comboBoxExcelSaveTimeSelectIndex = (uint)Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex;
            comboBoxExcelSaveTime.SelectedIndex = Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex;

            // 分段大小：索引与 SplitSizeMbOptions 一一对应（0=不分割，其余为 MB）
            comboSplitSize.Items.Clear();
            comboSplitSize.Items.Add("不分割");
            foreach (int mb in SplitSizeMbOptions)
            {
                if (mb > 0) comboSplitSize.Items.Add(mb + " MB");
            }
            int splitIdx = Properties.Settings.Default.comboSplitSizeIndex;
            if (splitIdx < 0 || splitIdx >= comboSplitSize.Items.Count) splitIdx = 2; // 默认 128 MB
            comboSplitSize.SelectedIndex = splitIdx;
        }

        /// <summary>按当前 BusChannels 刷新录制通道勾选列表，保留用户已勾选项</summary>
        private void RefreshChannelList()
        {
            // 先记住当前勾选状态，重建后恢复
            var curChecked = new HashSet<byte>();
            for (int i = 0; i < checkedListBoxChannels.Items.Count; i++)
            {
                if (checkedListBoxChannels.GetItemChecked(i))
                {
                    curChecked.Add(((ChannelItem)checkedListBoxChannels.Items[i]).LogicChannel);
                }
            }
            if (curChecked.Count > 0 || checkedListBoxChannels.Items.Count > 0)
            {
                RecordLogicChannels = curChecked;
            }

            checkedListBoxChannels.Items.Clear();
            int chCount = BaseParamter.BusChannels.Count;
            if (chCount == 0)
            {
                checkedListBoxChannels.Items.Add(new ChannelItem(1, "CH1"), true);
                return;
            }
            for (int i = 0; i < chCount; i++)
            {
                byte logicCh = BaseParamter.GetLogicChannel(i);
                string name = BaseParamter.BusChannels[i].Name;
                string text = string.IsNullOrEmpty(name) ? $"CH{logicCh}" : $"CH{logicCh} {name}";
                bool isChecked = RecordLogicChannels.Count == 0 || RecordLogicChannels.Contains(logicCh);
                checkedListBoxChannels.Items.Add(new ChannelItem(logicCh, text), isChecked);
            }
        }

        private void LoggingSet_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {
                RefreshChannelList();
                SyncRecordButtons();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string foldpath = dialog.SelectedPath;
                fileAdress.Text = foldpath + "\\data Log\\";

                try
                {
                    if (!Directory.Exists(fileAdress.Text))//如果路径不存在
                    {
                        Directory.CreateDirectory(fileAdress.Text);//创建一个路径的文件夹
                    }
                }
                catch { }
                FileAddress = fileAdress.Text;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // 状态自愈：标志为"录制中"但录制实例已不存在（上次停止/异常遗留）时，
            // 开始录制按钮会永久置灰并一直提示"正在录制中"，这里复位后继续
            if (Logging.SaveFlag && Logging.log == null)
            {
                AppLog.Warn("[录制] 检测到残留录制状态（无录制实例），已复位");
                Logging.SaveFlag = false;
                SyncRecordButtons();
            }
            if (Logging.SaveFlag)
            {
                MessageBox.Show("正在录制中，请先停止当前录制!");
                return;
            }
            if(checkBoxSaveExcel.Checked && BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
            {
                MessageBox.Show("若要保存CSV格式数据，请先加载dbc文件!");
                return;
            }
            // 只录制DBC报文的前提校验：没有任何DBC时一个报文都不会落盘，必须先拦住
            if (checkBoxDbcOnly.Checked && !HasAnyDbc())
            {
                MessageBox.Show("只录制DBC中定义的报文，需要先加载DBC文件（或给录制通道配置DBC）。");
                return;
            }
            // 录制通道勾选校验：至少选择1个通道
            var checkedChannels = new HashSet<byte>();
            for (int i = 0; i < checkedListBoxChannels.Items.Count; i++)
            {
                if (checkedListBoxChannels.GetItemChecked(i))
                {
                    checkedChannels.Add(((ChannelItem)checkedListBoxChannels.Items[i]).LogicChannel);
                }
            }
            if (checkedChannels.Count == 0)
            {
                MessageBox.Show("请至少勾选一个录制通道!");
                return;
            }
            RecordLogicChannels = checkedChannels;
            try
            {
                if(checkBoxSaveExcel.Checked)
                {
                    saveExcelFlag = true;
                    saveExcelTime = (uint)(saveExcelTimeBuf[comboBoxExcelSaveTime.SelectedIndex]*1000.0f);
                }
                else
                {
                    saveExcelFlag = false;
                }
                if(radioButton1.Checked)
                {
                    SaveFileType_int = 0;
                    SaveFileType = "ASC";
                    Properties.Settings.Default.SaveFileType = SaveFileType;
                }
                else
                {
                    SaveFileType_int = 1;
                    SaveFileType = "BLF";
                    Properties.Settings.Default.SaveFileType = SaveFileType;
                }
                SaveSettingsQuietly();
                //string data = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                //string filename = fileAdress.Text + "FN" + data + ".txt";

                //PublicUtil.data_collection.File_adress = filename;
                FileAddress = fileAdress.Text;

                //Header_information = Header_information_Text.Text;
                Header_information = "";

                if (Main.main == null || Main.main.IsDisposed)
                {
                    MessageBox.Show("开始录制失败：主窗口未就绪，请重新打开软件。", "录制报文",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // 勾选"只录制DBC报文"时口径不可逆：没写进去的报文事后补不回来，开始前必须确认一次
                if (!ConfirmDbcOnlyStart()) return;
                Main.main.MngMAIN_OpenLogging();
            }
            catch (Exception ex)
            {
                // 原实现 catch { } 把失败全部吞掉，现场表现为"点开始录制没反应"，无法定位
                AppLog.Error("[录制] 开始录制请求异常: " + ex);
                MessageBox.Show("开始录制失败：" + ex.Message, "录制报文",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>保存用户设置：失败只记日志，不阻断录制（user.config 在受限账户/漫游目录下可能不可写）</summary>
        private static void SaveSettingsQuietly()
        {
            try { Properties.Settings.Default.Save(); }
            catch (Exception ex) { AppLog.Warn("[设置] 保存失败: " + ex.Message); }
        }

        /// <summary>
        /// 勾选"只录制DBC中定义的报文"时的开始前确认；返回 false 表示用户取消，不得开始录制。
        /// 未勾选时直接放行，不打扰用户。
        /// </summary>
        private bool ConfirmDbcOnlyStart()
        {
            if (!checkBoxDbcOnly.Checked) return true;
            DialogResult confirm = MessageBox.Show(
                "本次录制只保存DBC中定义的报文：\r\n" +
                "· 未在DBC中定义的报文不会写入文件，事后无法补录；\r\n" +
                "· 已配置通道未挂DBC时，该通道不会录到任何报文。\r\n\r\n" +
                "确定按此方式开始录制吗？",
                "只录制DBC中定义的报文",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            return confirm == DialogResult.OK;
        }

        private void buttonStop_Click(object sender, EventArgs e)
        {
            Logging.StopRecording();
        }

        /// <summary>按录制状态同步对话框内"开始录制/停止录制"按钮与状态栏（由主界面状态刷新统一调用）</summary>
        internal void SyncRecordButtons()
        {
            if (this.IsDisposed)
            {
                return;
            }
            bool recording = Logging.SaveFlag;
            button2.Enabled = !recording;
            buttonStop.Enabled = recording;
            buttonStop.BackColor = recording ? System.Drawing.Color.IndianRed : System.Drawing.SystemColors.Control;
            // 录制途中不允许改过滤口径/分段大小：只对下一次录制生效，避免同一份文件里前后不一致
            checkBoxDbcOnly.Enabled = !recording;
            comboSplitSize.Enabled = !recording;
            RefreshStatusLabel();
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            SaveFileType = "ASC";
            SaveFileType_int = 0;

            Properties.Settings.Default.SaveFileType = SaveFileType;
            SaveSettingsQuietly();
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            SaveFileType = "BLF";
            SaveFileType_int = 1;
            Properties.Settings.Default.SaveFileType = SaveFileType;
            SaveSettingsQuietly();
        }

        /// <summary>"只录制DBC中定义的报文"切换：写唯一真源并持久化</summary>
        private void checkBoxDbcOnly_CheckedChanged(object sender, EventArgs e)
        {
            DbcOnlyRecord = checkBoxDbcOnly.Checked;
            Properties.Settings.Default.checkBoxDbcOnlyChecked = DbcOnlyRecord;
            SaveSettingsQuietly();
        }

        /// <summary>分段大小切换：持久化索引（下一次录制生效）</summary>
        private void comboSplitSize_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.comboSplitSizeIndex = comboSplitSize.SelectedIndex;
            SaveSettingsQuietly();
        }

        private void LoggingSet_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 非模态单例：关闭只隐藏不销毁，避免下次打开时实例已Dispose
            if (!this.Modal)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void checkBoxSaveExcel_CheckedChanged(object sender, EventArgs e)
        {
            saveExcelFlag = checkBoxSaveExcel.Checked;

            Properties.Settings.Default.checkBoxSaveExcelChecked = saveExcelFlag;
            SaveSettingsQuietly();
        }

        private void comboBoxExcelSaveTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboBoxExcelSaveTimeSelectIndex = (uint)comboBoxExcelSaveTime.SelectedIndex;

            Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex = (int)comboBoxExcelSaveTimeSelectIndex;
            SaveSettingsQuietly();

        }
    }
}
