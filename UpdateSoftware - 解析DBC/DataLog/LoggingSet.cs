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
        public static int SaveFileType_int = 0;
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
                    Properties.Settings.Default.Save();
                }
                else
                {
                    SaveFileType_int = 1;
                    SaveFileType = "BLF";
                    Properties.Settings.Default.SaveFileType = SaveFileType;
                    Properties.Settings.Default.Save();
                }
                //string data = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                //string filename = fileAdress.Text + "FN" + data + ".txt";

                //PublicUtil.data_collection.File_adress = filename;
                FileAddress = fileAdress.Text;

                //Header_information = Header_information_Text.Text;
                Header_information = "";

                Main.main.MngMAIN_OpenLogging();
            }
            catch { }
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
            if (recording)
            {
                string file = SaveFileType_int == 1 ? Logging.NowBLFFileAddr_str : Logging.NowASCFileAddr;
                labelStatus.Text = $"状态：录制中 → {file}";
                labelStatus.ForeColor = System.Drawing.Color.IndianRed;
            }
            else
            {
                labelStatus.Text = "状态：未录制";
                labelStatus.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            SaveFileType = "ASC";
            SaveFileType_int = 0;

            Properties.Settings.Default.SaveFileType = SaveFileType;
            Properties.Settings.Default.Save();
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            SaveFileType = "BLF";
            SaveFileType_int = 1;
            Properties.Settings.Default.SaveFileType = SaveFileType;
            Properties.Settings.Default.Save();
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
            Properties.Settings.Default.Save();
        }

        private void comboBoxExcelSaveTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboBoxExcelSaveTimeSelectIndex = (uint)comboBoxExcelSaveTime.SelectedIndex;

            Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex = (int)comboBoxExcelSaveTimeSelectIndex;
            Properties.Settings.Default.Save();

        }
    }
}
