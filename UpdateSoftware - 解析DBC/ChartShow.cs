using PCAN_Client.util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data;
using System.Windows.Forms.DataVisualization.Charting;

namespace PCAN_Client
{
    public partial class ChartShow : Form
    {
        private int X_AxisMaxValue = 1000;

        private Dictionary<string, UInt32> ChartDataList = new Dictionary<string, UInt32>();
        private Dictionary<string, List<double>> ChartDataBuf = new Dictionary<string, List<double>>();
        private int GetDataTimeDelayTime = 1000;
        private int GetDataTimeDelayCnt = 0;

        private int BindDataTimeDelayTime = 500;
        private int BindDataTimeDelayCnt = 0;

        private List<Chart> charts = new List<Chart>();

        public ChartShow()
        {
            InitializeComponent();
        }

        public void GetChartDataBuf(ref Dictionary<string, List<double>> ChartDataBufref)
        {
            ChartDataBufref = ChartDataBuf;
        }

        public void AddOrRemoveChart(int messageIndex, string signalName)
        {
            int signalIndex = 0;
            bool findFlag = false;

            BaseParamter.dbcHelper.GetSignalIndexBySigName(signalName, messageIndex, ref signalIndex, ref findFlag);
            if (findFlag)
            {
                // 检查信号是否已存在于字典中
                if (ChartDataList.ContainsKey(signalName))
                {
                    // 存在 - 移除信号
                    ChartDataList.Remove(signalName);
                    ChartDataBuf[signalName].Clear();
                    ChartDataBuf[signalName] = null;
                    ChartDataBuf.Remove(signalName);
                    Main.chartShow.SignaleSourceChange("");
                }
                else
                {
                    // 不存在 - 添加信号（这里需要指定一个值，比如0）
                    ChartDataList[signalName] = (uint)(messageIndex << 16 | (ushort)signalIndex);
                    List<double> list = new List<double>();
                    ChartDataBuf[signalName] = list;
                    Main.chartShow.SignaleSourceChange(signalName);
                }
            }
        }

        private void DateNumber_TextChanged(object sender, EventArgs e)
        {
            int X_maxValue = 1000;

            if(int.TryParse(DateNumber.Text, out X_maxValue))
            {
                Main.myChart.SetMYCHART_X_AxisMaxValue(X_maxValue);
                X_AxisMaxValue = X_maxValue;
                Properties.Settings.Default.ChartShowDataNum = X_maxValue;
                Properties.Settings.Default.Save();
                
            }
        }

        private void Y_Auto_CheckedChanged(object sender, EventArgs e)
        {
            Main.myChart.SetMYCHART_YAutoFlag(Y_Auto.Checked);
        }

        private void Y_MinValue_TextChanged(object sender, EventArgs e)
        {
            int Y_minValue = 1000;

            int.TryParse(Y_MinValue.Text, out Y_minValue);
            Main.myChart.SetMYCHART_Y_AxisMinValue(Y_minValue);
        }

        private void Y_MaxValue_TextChanged(object sender, EventArgs e)
        {
            int Y_maxValue = 1000;

            int.TryParse(Y_MaxValue.Text, out Y_maxValue);
            Main.myChart.SetMYCHART_Y_AxisMaxValue(Y_maxValue);
        }

        private void ChartSelectDataCombox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Main.myChart.SetMYCHART_SelectSignalName(ChartSelectDataCombox.Text);
        }

        private void RestoreOrSetDefaultSelection(string lastSelectedItem)
        {
            // 如果之前有选中项且仍然存在，则恢复选中
            if (!string.IsNullOrEmpty(lastSelectedItem) &&
                ChartSelectDataCombox.Items.Contains(lastSelectedItem))
            {
                ChartSelectDataCombox.Text = lastSelectedItem;
            }
            // 否则如果有项存在，选择第一项
            else if (ChartSelectDataCombox.Items.Count > 0)
            {
                ChartSelectDataCombox.SelectedIndex = 0;
            }
            // 如果没有任何项，清空文本
            else
            {
                ChartSelectDataCombox.Text = string.Empty;
            }
        }

        public void SignaleSourceChange(string signalName)
        {
            try
            {
                Dictionary<string, List<double>> ChartDataBuf = null;
                Main.chartShow.GetChartDataBuf(ref ChartDataBuf);

                if (ChartDataBuf == null)
                {
                    MessageBox.Show("获取图表数据失败");
                    return;
                }

                string lastName = ChartSelectDataCombox.Text;

                // 清空当前选项
                ChartSelectDataCombox.Items.Clear();

                // 添加所有键到下拉框
                foreach (string key in ChartDataBuf.Keys)
                {
                    ChartSelectDataCombox.Items.Add(key);
                }

                // 尝试恢复之前的选择或设置默认选择
                if (signalName.Equals(""))
                {
                    RestoreOrSetDefaultSelection(lastName);
                }
                else
                {
                    RestoreOrSetDefaultSelection(signalName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新信号源时出错: {ex.Message}");
            }
        }

        private void ChartShow_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string key in ChartDataList.Keys)
                {
                    sb.Append(key);
                    sb.Append("#");
                    sb.Append(ChartDataList[key].ToString());
                    sb.Append("$");
                }

                // 移除最后一个多余的 $
                if (sb.Length > 0)
                {
                    sb.Length--;
                }

                Properties.Settings.Default.ChartShowSave = sb.ToString();
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存图表设置失败: {ex.Message}");
            }

            Main.ChartShowOpenFlag = false;
        }

        private void ChartShow_Load(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.ChartShowSave) &&
                    !Properties.Settings.Default.ChartShowSave.Equals("NULL"))
                {
                    string saveData = Properties.Settings.Default.ChartShowSave;
                    string[] signalEntries = saveData.Split('$');

                    foreach (string signalEntry in signalEntries)
                    {
                        if (string.IsNullOrEmpty(signalEntry))
                            continue;

                        string[] parts = signalEntry.Split('#');
                        if (parts.Length >= 2)
                        {
                            string signalName = parts[0];
                            if (UInt32.TryParse(parts[1], out UInt32 temp))
                            {
                                ChartDataList[signalName] = temp;
                                ChartDataBuf[signalName] = new List<double>();
                            }
                        }
                    }

                    // 更新下拉框显示
                    UpdateComboBoxFromData();
                }
                Y_Auto.Checked = true;

                X_AxisMaxValue = Properties.Settings.Default.ChartShowDataNum;
                DateNumber.Text = X_AxisMaxValue.ToString();
                GetDataTimeDelayTime = Properties.Settings.Default.ChartShowDataTimeDelayTime;
                textBox1.Text = GetDataTimeDelayTime.ToString();
                
                //Chart chart = new Chart();
                ////chart = chart2;
                //charts.Add(chart);
                //flowLayoutPanel1.Controls.Add(chart);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图表设置失败: {ex.Message}");
            }

            Main.myChart.SetMYCHART_Chart(this.chart2);
            timer1.Start();
            Main.ChartShowOpenFlag = true;
        }

        // 新增方法：从数据更新下拉框
        private void UpdateComboBoxFromData()
        {
            string lastName = ChartSelectDataCombox.Text;
            ChartSelectDataCombox.Items.Clear();

            foreach (string key in ChartDataBuf.Keys)
            {
                ChartSelectDataCombox.Items.Add(key);
            }

            // 如果有数据，选择第一项；否则清空
            if (ChartSelectDataCombox.Items.Count > 0)
            {
                ChartSelectDataCombox.SelectedIndex = 0;
            }
            else
            {
                ChartSelectDataCombox.Text = string.Empty;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (BaseParamter.dbcHelper.dbcFile.messages.Count <= 0)
            {
                return;
            }
            if (!checkBox1.Checked)
            {
                if(BindDataTimeDelayCnt >= BindDataTimeDelayTime)
                {
                    BindDataTimeDelayCnt = 0;
                    Main.myChart.data_bind();
                }
                else
                {
                    BindDataTimeDelayCnt += timer1.Interval;
                }
            }

            if (GetDataTimeDelayCnt >= GetDataTimeDelayTime)
            {
                GetDataTimeDelayCnt = 0;
                foreach (string key in ChartDataBuf.Keys)
                {
                    UInt32 tempp = ChartDataList[key];
                    int msgIndex = (int)(tempp >> 16);
                    int sigIndex = (int)(tempp & 0xFFFF);

                    // 缓存引用以提高性能
                    var messages = BaseParamter.dbcHelper.dbcFile.messages;

                    // 检查 msgIndex 是否有效
                    if (msgIndex >= 0 && msgIndex < messages.Count)
                    {
                        var message = messages[msgIndex];
                        var signals = message.signals;

                        // 检查 sigIndex 是否有效
                        if (sigIndex >= 0 && sigIndex < signals.Count)
                        {
                            var signal = signals[sigIndex];
                            //System.Console.WriteLine(signal.result);
                            ChartDataBuf[key].Add(signal.result);
                        }
                        else
                        {
                        }
                    }
                    else
                    {
                    }
                    if (ChartDataBuf[key].Count >= X_AxisMaxValue)
                    {
                        ChartDataBuf[key].RemoveRange(0, ChartDataBuf[key].Count - X_AxisMaxValue);
                    }
                    else
                    {
                        /* empty */
                    }
                }
            }
            else
            {
                GetDataTimeDelayCnt += timer1.Interval;
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            int DataTimeDelayTime = 1000;

            if(int.TryParse(textBox1.Text, out DataTimeDelayTime))
            {
                GetDataTimeDelayTime = DataTimeDelayTime;
                
                Properties.Settings.Default.ChartShowDataTimeDelayTime = DataTimeDelayTime;
                Properties.Settings.Default.Save();
            }
        }
    }
}
