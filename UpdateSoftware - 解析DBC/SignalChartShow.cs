using PCAN_Client.CAN_Data;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static PCAN_Client.SignalChartShow;
using static PCAN_Client.UDS.DataShow;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;
using System.Collections.Concurrent;
using System.Collections;
using PCAN_Client.util;
using System.Windows.Media.Animation;

namespace PCAN_Client
{
    public partial class SignalChartShow : Form
    {
        private DataShowHandle dataShowHandle = new DataShowHandle();
        public class CanSignalData
        {
            public DateTime Timestamp { get; set; }
            public double Value { get; set; }
            public int CanId { get; set; } // 可选
        }

        class ChartStruct 
        {
            public Chart chart;
            public string SignalName;
            public int SinalIndex;
            public int MessageIndex;
            public CAN_Data.MsgReceive msgReceive;
            public ConcurrentQueue<CanSignalData> _dataQueue;
            public CancellationTokenSource _cts;
            public CAN_Data.Message message;
            public List<DateTime> timeList = new List<DateTime>();
            public List<double> valueList = new List<double>();

            public void AddSinalData(CAN_Data.Message message)
            {
                timeList.Add(DateTime.Now);
                valueList.Add(message.signals[this.SinalIndex].result);
                if(timeList.Count >= 2000)
                {
                    timeList.RemoveAt(0);
                    valueList.RemoveAt(0);
                }
                //CanSignalData canSignalData = new CanSignalData();
                //canSignalData.Timestamp = DateTime.Now;
                //canSignalData.Value = message.signals[this.SinalIndex].result;
                //_dataQueue.Enqueue(canSignalData);
                //Console.WriteLine(message.messageName + "   " + canSignalData.Value + "   " + SinalIndex);
            }
        };
        // 支持双缓冲的自定义Chart控件
        public class BufferedChart : Chart
        {
            public BufferedChart()
            {
                // 启用双缓冲
                this.DoubleBuffered = true;
            }
        }

        private List<ChartStruct> charts = new List<ChartStruct>();
        private int chartCount = 0;


        // 用于鼠标拖动的变量
        private bool isMouseDown = false;
        private int lastMouseY = 0;

        internal void AddSignalFun(CAN_Data.Message message, string SignalName)
        {
            chartCount++;
            BufferedChart chart = new BufferedChart();
            chart.Name = "chart" + chartCount;

            // 调整FlowLayoutPanel的大小以适应窗体大小
            flpCharts.Width = this.ClientSize.Width;
            flpCharts.Height = this.ClientSize.Height;

            // 计算Chart控件的高度，保证可同时显示6个Chart曲线，且高度不小于75
            int chartHeight = 300;
            if (chartCount <= 5)
            {
                // 考虑边距，FlowLayoutPanel高度为当前高度，每个Chart有10像素的上下边距
                chartHeight = (flpCharts.Height - (chartCount * 20)) / chartCount;
                // 确保Chart高度不小于75
                if (chartHeight < 100)
                {
                    chartHeight = 100;
                }
            }
            else
            {
                // 超过6个时，使用固定高度，不小于75
                chartHeight = 100;
            }

            // 创建一个支持双缓冲的自定义Chart控件
            chart.Name = "chart" + chartCount;
            chart.Size = new Size(flpCharts.Width - 20, chartHeight);
            chart.Margin = new Padding(5);
            // 优化绘制模式
            chart.AntiAliasing = AntiAliasingStyles.None;
            chart.TextAntiAliasingQuality = TextAntiAliasingQuality.Normal;

            ChartArea chartArea = new ChartArea();
            chartArea.Name = "ChartArea" + chartCount;
            // 移除网格线
            chartArea.AxisX.MajorGrid.Enabled = false;
            chartArea.AxisY.MajorGrid.Enabled = false;
            // 启用鼠标选择和缩放功能
            chartArea.CursorX.IsUserEnabled = true;
            chartArea.CursorX.IsUserSelectionEnabled = true;
            chartArea.CursorY.IsUserEnabled = true;
            chartArea.CursorY.IsUserSelectionEnabled = true;
            chartArea.AxisX.ScaleView.Zoomable = true;
            chartArea.AxisY.ScaleView.Zoomable = true;
            // 禁用光标视觉效果，解决黑条问题
            chartArea.CursorX.LineWidth = 0;
            chartArea.CursorY.LineWidth = 0;
            // 设置x轴为时间格式（时分秒）
            chartArea.AxisX.LabelStyle.Format = "HH:mm:ss";
            // 设置 x 轴为时间类型，但让系统自动决定间隔
            chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto; // 或者直接省略，默认为 Auto
                                                                      // 不设置 Interval，或者设置为 double.NaN
            chartArea.AxisX.Interval = double.NaN;
            // 自动调整x轴范围
            chartArea.AxisX.Minimum = double.NaN;
            chartArea.AxisX.Maximum = double.NaN;
            // 确保x轴标签可见
            chartArea.AxisX.LabelStyle.Angle = 0; // 不旋转标签，增加水平空间
            chartArea.AxisX.LabelStyle.IsEndLabelVisible = true;
            // 调整内绘图区域位置，减小y轴左侧空白
            chartArea.InnerPlotPosition.Auto = false;
            chartArea.InnerPlotPosition.X = 3;
            chartArea.InnerPlotPosition.Y = 5;
            chartArea.InnerPlotPosition.Width = 92;
            chartArea.InnerPlotPosition.Height = 65;
            chart.ChartAreas.Add(chartArea);

            Series series = new Series();
            series.Name = SignalName;
            series.ChartType = SeriesChartType.Line;
            // 设置x轴数据类型为DateTime
            series.XValueType = ChartValueType.DateTime;
            // 禁用直接显示数据标签，只在鼠标悬停时显示
            series.IsValueShownAsLabel = false;
            // 启用ToolTip显示详细数据
            series.ToolTip = "X: #VALX, Y: #VALY";
            // 设置数据点为圆形标记
            series.MarkerStyle = MarkerStyle.Circle;
            series.MarkerSize = 6;
            chart.Series.Add(series);

            // 移除Legend，避免曲线左边有文字提示
            // 不添加Legend

            chart.Titles.Add(SignalName);

            // 订阅MouseUp事件，实现所有曲线x轴同步
            chart.MouseUp += new MouseEventHandler(Chart_MouseUp);
            // 订阅MouseDoubleClick事件，实现双击重置缩放
            chart.MouseDoubleClick += new MouseEventHandler(Chart_MouseDoubleClick);
            // 订阅MouseWheel事件，实现滚轮缩放
            chart.MouseWheel += new MouseEventHandler(Chart_MouseWheel);

            flpCharts.Controls.Add(chart);

            ChartStruct chartStruct = new ChartStruct();
            chartStruct._dataQueue = new ConcurrentQueue<CanSignalData>();
            chartStruct._cts = new CancellationTokenSource();
            chartStruct.chart = chart;
            chartStruct.SignalName = SignalName;
            chartStruct.msgReceive = new CAN_Data.MsgReceive(chartStruct.AddSinalData);
            chartStruct.message = message;
            if (null == message.msgReceive)
            {
                message.msgReceive = chartStruct.msgReceive;
            }
            else
            {
                message.msgReceive += chartStruct.msgReceive;
            }
            bool flag = false;
            BaseParamter.dbcHelper.GetMessageIndexAndSignalIndexBySigName(SignalName, ref chartStruct.MessageIndex, ref chartStruct.SinalIndex, ref flag);
            charts.Add(chartStruct);

            Console.WriteLine(message.messageName + "   " + chartStruct.MessageIndex + "   " + chartStruct.SinalIndex);

            // 更新所有已添加Chart的高度
            UpdateChartHeights();
        }
        public SignalChartShow()
        {
            InitializeComponent();
            this.Load += new System.EventHandler(this.SignalChartShow_Load);
        }

        private void SignalChartShow_Load(object sender, EventArgs e)
        {
            dataShowHandle.Start();
            // 设置FlowLayoutPanel的 Dock 属性，使其绑定到窗体的右边、上边、下边
            flpCharts.Dock = DockStyle.Fill;

            // 设置groupBox1的 Dock 属性，使其绑定到窗体的左边、上边、下边
            groupBox1.Dock = DockStyle.Left;
            // 设置groupBox1的宽度
            groupBox1.Width = 200;

            // 确保flpCharts在groupBox1之上
            this.Controls.SetChildIndex(flpCharts, 0);
            this.Controls.SetChildIndex(groupBox1, 1);

            // 订阅FlowLayoutPanel的鼠标事件，实现鼠标拖动功能
            flpCharts.MouseDown += new MouseEventHandler(flpCharts_MouseDown);
            flpCharts.MouseMove += new MouseEventHandler(flpCharts_MouseMove);
            flpCharts.MouseUp += new MouseEventHandler(flpCharts_MouseUp);

            // 订阅窗体大小改变事件，确保FlowLayoutPanel始终适应窗体大小
            this.Resize += new System.EventHandler(this.Form1_Resize);
        }



        private void btnRemoveChart_Click(object sender, EventArgs e)
        {
            //if (charts.Count > 0)
            //{
            //    // 移除最后一个添加的Chart
            //    Chart chartToRemove = charts[charts.Count - 1];
            //    flpCharts.Controls.Remove(chartToRemove);
            //    charts.Remove(chartToRemove);
            //    chartCount--;

            //    // 更新所有Chart的高度
            //    UpdateChartHeights();
            //}
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            // 当窗体大小改变时，更新所有Chart的大小
            UpdateChartHeights();
        }

        // 处理MouseUp事件，实现所有曲线x轴同步
        private void Chart_MouseUp(object sender, MouseEventArgs e)
        {
            // 获取触发事件的Chart
            Chart chart = sender as Chart;
            if (chart == null)
                return;

            // 获取当前x轴视图的位置和大小
            double position = chart.ChartAreas[0].AxisX.ScaleView.Position;
            double size = chart.ChartAreas[0].AxisX.ScaleView.Size;

            // 应用到所有其他Chart的x轴
            foreach (ChartStruct c in charts)
            {
                if (c.chart != chart) // 跳过触发事件的Chart
                {
                    c.chart.ChartAreas[0].AxisX.ScaleView.Position = position;
                    c.chart.ChartAreas[0].AxisX.ScaleView.Size = size;
                }
            }
        }

        // 处理MouseDoubleClick事件，实现双击重置缩放
        private void Chart_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // 获取触发事件的Chart
            Chart chart = sender as Chart;
            if (chart == null)
                return;

            // 重置x轴缩放
            chart.ChartAreas[0].AxisX.ScaleView.ZoomReset();

            // 同步所有其他Chart的x轴缩放
            foreach (ChartStruct c in charts)
            {
                if (c.chart != chart) // 跳过触发事件的Chart
                {
                    c.chart.ChartAreas[0].AxisX.ScaleView.ZoomReset();
                }
            }
        }

        // 处理MouseWheel事件，实现滚轮缩放
        private void Chart_MouseWheel(object sender, MouseEventArgs e)
        {
            // 获取触发事件的Chart
            Chart chart = sender as Chart;
            if (chart == null)
                return;

            // 获取当前x轴视图的位置和大小
            double position = chart.ChartAreas[0].AxisX.ScaleView.Position;
            double size = chart.ChartAreas[0].AxisX.ScaleView.Size;

            // 计算缩放因子
            double scaleFactor = e.Delta > 0 ? 0.9 : 1.1;
            double newSize = size * scaleFactor;

            // 确保缩放后的大小合理
            if (newSize > 0.1 && newSize < chart.ChartAreas[0].AxisX.Maximum - chart.ChartAreas[0].AxisX.Minimum)
            {
                // 计算新的位置，使鼠标位置保持在视图中心
                double mouseX = chart.ChartAreas[0].AxisX.PixelPositionToValue(e.Location.X);
                double newPosition = position + (size - newSize) * ((mouseX - position) / size);

                // 应用新的缩放
                chart.ChartAreas[0].AxisX.ScaleView.Size = newSize;
                chart.ChartAreas[0].AxisX.ScaleView.Position = newPosition;

                // 同步所有其他Chart的x轴缩放
                foreach (ChartStruct c in charts)
                {
                    if (c.chart != chart) // 跳过触发事件的Chart
                    {
                        c.chart.ChartAreas[0].AxisX.ScaleView.Size = newSize;
                        c.chart.ChartAreas[0].AxisX.ScaleView.Position = newPosition;
                    }
                }
            }
        }

        private void UpdateChartHeights()
        {
            // 由于flpCharts使用了DockStyle.Fill，不需要手动调整大小

            int totalHeight = flpCharts.ClientSize.Height;
            int marginPerChart = 10; // 每个Chart的上下边距总和

            if (chartCount <= 6 && chartCount != 0)
            {
                int chartHeight = (totalHeight - (chartCount * marginPerChart)) / chartCount;
                // 确保Chart高度不小于140，以保证时间标签能够完全显示
                if (chartHeight < 140)
                {
                    chartHeight = 140;
                }
                foreach (ChartStruct chartStruct in charts)
                {
                    Chart chart = chartStruct.chart;
                    chart.Size = new System.Drawing.Size(flpCharts.Width - 20, chartHeight);
                    chart.Margin = new Padding(5);
                    // 重新设置ChartArea属性，确保缩放功能有效
                    ChartArea chartArea = chart.ChartAreas[0];
                    // 启用鼠标选择和缩放功能
                    chartArea.CursorX.IsUserEnabled = true;
                    chartArea.CursorX.IsUserSelectionEnabled = true;
                    chartArea.CursorY.IsUserEnabled = true;
                    chartArea.CursorY.IsUserSelectionEnabled = true;
                    chartArea.AxisX.ScaleView.Zoomable = true;
                    chartArea.AxisY.ScaleView.Zoomable = true;
                    // 禁用光标视觉效果，解决黑条问题
                    chartArea.CursorX.LineWidth = 0;
                    chartArea.CursorY.LineWidth = 0;
                    // 设置x轴为时间格式（时分秒）
                    chartArea.AxisX.LabelStyle.Format = "HH:mm:ss";
                    chartArea.AxisX.IntervalType = DateTimeIntervalType.Seconds;
                    chartArea.AxisX.Interval = 5; // 每5秒显示一个标签
                    // 自动调整x轴范围
                    chartArea.AxisX.Minimum = double.NaN;
                    chartArea.AxisX.Maximum = double.NaN;
                    // 确保x轴标签可见
                    chartArea.AxisX.LabelStyle.Angle = 0; // 不旋转标签，增加水平空间
                    chartArea.AxisX.LabelStyle.IsEndLabelVisible = true;
                    // 调整内绘图区域位置，减小y轴左侧空白
                    chartArea.InnerPlotPosition.Auto = false;
                    chartArea.InnerPlotPosition.X = 3;
                    chartArea.InnerPlotPosition.Y = 5;
                    chartArea.InnerPlotPosition.Width = 92;
                    chartArea.InnerPlotPosition.Height = 65;
                }
            }
            else if (chartCount != 0)
            {
                // 超过6个时，使用固定高度，不小于140
                foreach (ChartStruct chartStruct in charts)
                {
                    Chart chart = chartStruct.chart;
                    chart.Size = new System.Drawing.Size(flpCharts.Width - 20, 140);
                    chart.Margin = new Padding(5);
                    // 重新设置ChartArea属性，确保缩放功能有效
                    ChartArea chartArea = chart.ChartAreas[0];
                    // 启用鼠标选择和缩放功能
                    chartArea.CursorX.IsUserEnabled = true;
                    chartArea.CursorX.IsUserSelectionEnabled = true;
                    chartArea.CursorY.IsUserEnabled = true;
                    chartArea.CursorY.IsUserSelectionEnabled = true;
                    chartArea.AxisX.ScaleView.Zoomable = true;
                    chartArea.AxisY.ScaleView.Zoomable = true;
                    // 禁用光标视觉效果，解决黑条问题
                    chartArea.CursorX.LineWidth = 0;
                    chartArea.CursorY.LineWidth = 0;
                    // 设置x轴为时间格式（时分秒）
                    chartArea.AxisX.LabelStyle.Format = "HH:mm:ss";
                    // 设置 x 轴为时间类型，但让系统自动决定间隔
                    chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto; // 或者直接省略，默认为 Auto
                                                                              // 不设置 Interval，或者设置为 double.NaN
                    chartArea.AxisX.Interval = double.NaN;
                    // 自动调整x轴范围
                    chartArea.AxisX.Minimum = double.NaN;
                    chartArea.AxisX.Maximum = double.NaN;
                    // 确保x轴标签可见
                    chartArea.AxisX.LabelStyle.Angle = 0; // 不旋转标签，增加水平空间
                    chartArea.AxisX.LabelStyle.IsEndLabelVisible = true;
                    // 调整内绘图区域位置，减小y轴左侧空白
                    chartArea.InnerPlotPosition.Auto = false;
                    chartArea.InnerPlotPosition.X = 3;
                    chartArea.InnerPlotPosition.Y = 5;
                    chartArea.InnerPlotPosition.Width = 92;
                    chartArea.InnerPlotPosition.Height = 65;
                }
            }
        }

        // 鼠标按下事件
        private void flpCharts_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isMouseDown = true;
                lastMouseY = e.Y;
            }
        }

        // 鼠标移动事件
        private void flpCharts_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                // 计算鼠标移动的距离
                int deltaY = e.Y - lastMouseY;
                // 调整FlowLayoutPanel的滚动位置
                flpCharts.AutoScrollPosition = new Point(0, flpCharts.AutoScrollPosition.Y - deltaY);
                lastMouseY = e.Y;
            }
        }

        // 鼠标释放事件
        private void flpCharts_MouseUp(object sender, MouseEventArgs e)
        {
            isMouseDown = false;
        }


        public class DataShowHandle
        {
            bool callbackFlag;
            TPCANTimestamp timesamp = new TPCANTimestamp();
            TPCANMsgFD msgFD = new TPCANMsgFD();
            TPCANMsg msg = new TPCANMsg();
            long time_us;
            ulong TimestampBuffer = 0;
            TPCANStatus result;
            

            public void Start()
            {
                callbackFlag = false;
                Main.main.multiMessageCANScheduler.AddAction(chartDraw, "DataShow", 300);
            }

            public void Stop()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("PCAN_Receive");
            }

            private void chartDraw()
            {
                if (callbackFlag)
                {
                    return;
                }
                callbackFlag = true;
                foreach (var chart in Main.signalChartShow.charts)
                {
                    Main.signalChartShow.Invoke(new Action(() =>
                    {
                        var series = chart.chart.Series[chart.SignalName];
                        series.Points.DataBindXY(chart.timeList, chart.valueList);
                    }));
                    //// 临时存储本次要更新的所有点
                    //List<CanSignalData> batch = new List<CanSignalData>();
                    //// 限制单次处理的数据量，避免长时间占用线程
                    //int count = 0;
                    //const int maxBatchSize = 100;
                    //while (chart._dataQueue.TryDequeue(out CanSignalData data) && count < maxBatchSize)
                    //{
                    //    batch.Add(data);
                    //    count++;
                    //    //Main.signalChartShow.Invoke(new Action(() => UpdateChart(chart.chart, ref data, chart.SignalName)));
                    //}
                    //if (batch.Count > 0)
                    //{
                    //    // 一次性将整批数据传递给 UI 线程
                    //    Main.signalChartShow.Invoke(new Action(() =>
                    //    {
                    //        var series = chart.chart.Series[chart.SignalName];
                    //        // 批量添加点（SuspendUpdates 可以减少重绘）
                    //        chart.chart.SuspendLayout();
                    //        foreach (var point in batch)
                    //        {
                    //            series.Points.AddXY(point.Timestamp, point.Value);
                    //            series.Points.DataBindXY();
                    //        }
                    //        // 限制显示点数，保持性能
                    //        const int maxPoints = 1000;
                    //        while (series.Points.Count > maxPoints)
                    //            series.Points.RemoveAt(0);
                    //        chart.chart.ResumeLayout();
                    //    }));
                    //}
                }
                callbackFlag = false;
            }
        }

        private void SignalChartShow_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var chart in Main.signalChartShow.charts)
            {
                if(null != chart.msgReceive)
                {
                    chart.message.msgReceive -= chart.msgReceive;
                    chart.msgReceive = null;
                }
            }
            dataShowHandle.Stop();
            dataShowHandle = null;
            Main.DataShowOpenFlag = false;
        }

        private void SignalChartShow_Load_1(object sender, EventArgs e)
        {
            Main.ChartShowOpenFlag = true;
        }
    }
}
