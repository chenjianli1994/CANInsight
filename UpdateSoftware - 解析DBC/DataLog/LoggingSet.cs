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
        public static int SaveSize = 100;
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

        public LoggingSet()
        {
            InitializeComponent();
        }

        private void LoggingSet_Load(object sender, EventArgs e)
        {
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
            if(checkBoxSaveExcel.Checked && BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
            {
                MessageBox.Show("若要保存CSV格式数据，请先加载dbc文件!");
                return;
            }
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
                int temp_int;
                int.TryParse(Timing_Length_Text.Text, out temp_int);

                //PublicUtil.data_collection.File_adress = filename;
                FileAddress = fileAdress.Text;

                //存储大小赋值
                if (temp_int < 1 || temp_int > 5000)//大小范围在1MB到5000MB之间
                {
                    temp_int = 100;
                    Timing_Length_Text.Text = temp_int.ToString();
                    SaveSize = temp_int;
                }
                else
                {
                    SaveSize = temp_int;
                }

                //Header_information = Header_information_Text.Text;
                Header_information = "";

                this.Invoke((EventHandler)(delegate
                {
                    this.Hide();
                }));
                Main.main.MngMAIN_OpenLogging();
            }
            catch { }
        }

        private void Timing_Length_Text_TextChanged(object sender, EventArgs e)
        {

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
