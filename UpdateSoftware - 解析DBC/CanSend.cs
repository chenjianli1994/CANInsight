using BrightIdeasSoftware;
using CSScriptLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PCAN_Client.CAN_Data;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PCAN_Client.CanSend;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;

namespace PCAN_Client
{        
    public partial class CanSend : Form
    {
        bool aliveFlag = true;
        internal enum dataGridView2ColumnEnum
        {
            MessageID = 0,
            MessageName,
            CycleTime,
            SendCnt,
            Enable
        };

        Stopwatch sw = new Stopwatch();

        /* 表设置 */
        internal DataTable dataTable;
        internal int dataGridView1SelectRowIndex = 0;
        internal int dataGridView1SelectColumnIndex = 0;
        internal int dataGridView1Column = 0;

        internal DataTable messagesTable;
        internal int dataGridView2SelectRowIndex = 0;
        internal int dataGridView2SelectColumnIndex = 0;
        internal int dataGridView2Column = 0;


        internal DataTable addMessagesTable;
        internal int dataGridView3SelectRowIndex = 0;
        internal int dataGridView3SelectColumnIndex = 0;
        internal int dataGridView3Column = 0;
        private static List<CAN_Data.Message> customMessagesList = new List<CAN_Data.Message>();

        int SelectMessageIndex = 0;
        // 在CanSend类中添加成员变量以跟踪当前活动的下拉框
        private ComboBox _currentComboBox;
        // 在CanSend类中添加成员变量
        private ToolStripDropDown _comboDropDown;
        private ToolStripControlHost _comboHost;

        private ManualResetEvent sendControlEvent = new ManualResetEvent(false);

        private List<CAN_Data.Message> messages = new List<CAN_Data.Message>();

        MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();
        public CanSend()
        {
            InitializeComponent();

            dataGridView2.CellClick += DataGridView2_CellClick;
            dataGridView1.Scroll += DataGridView1_Scroll; // 添加滚动事件监听
            dataTable = new DataTable();
            dataGridView1Column = 0;
            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                // Check if the header text is found
                dataTable.Columns.Add(column.HeaderText, typeof(string));
                dataGridView1Column++;
            }

            dataGridView1.EditMode = DataGridViewEditMode.EditOnEnter; // 确保单击即可进入编辑
            // 绑定DataTable到DataGridView
            dataGridView1.Columns.Clear();
            dataGridView1.DataSource = dataTable;

            messagesTable = new DataTable();
            dataGridView2Column = 0;
            foreach (DataGridViewColumn column in dataGridView2.Columns)
            {
                // Check if the header text is found
                if(column.GetType().ToString().Contains("DataGridViewTextBoxColumn"))
                {
                    messagesTable.Columns.Add(column.HeaderText, typeof(string));
                }
                else if(column.GetType().ToString().Contains("DataGridViewCheckBoxColumn"))
                {
                    messagesTable.Columns.Add(column.HeaderText, typeof(bool));
                }
                else
                {
                    messagesTable.Columns.Add(column.HeaderText, column.GetType());
                }
                dataGridView2Column++;
            }
            // 绑定DataTable到DataGridView
            dataGridView2.Columns.Clear();
            dataGridView2.DataSource = messagesTable;

            addMessagesTable = new DataTable();
            dataGridView3Column = 0;
            foreach (DataGridViewColumn column in dataGridView3.Columns)
            {
                // Check if the header text is found
                if (column.GetType().ToString().Contains("DataGridViewTextBoxColumn"))
                {
                    addMessagesTable.Columns.Add(column.HeaderText, typeof(string));
                }
                else if (column.GetType().ToString().Contains("DataGridViewCheckBoxColumn"))
                {
                    addMessagesTable.Columns.Add(column.HeaderText, typeof(bool));
                }
                else
                {
                    addMessagesTable.Columns.Add(column.HeaderText, column.GetType());
                }
                dataGridView2Column++;
            }
            // 绑定DataTable到DataGridView
            dataGridView3.Columns.Clear();
            dataGridView3.DataSource = addMessagesTable;

            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, dataGridView1, new object[] { true });
            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, dataGridView2, new object[] { true });

            // 禁用自动调整
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView3.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // 禁用行头
            dataGridView1.RowHeadersVisible = false;
            dataGridView2.RowHeadersVisible = false;
            dataGridView3.RowHeadersVisible = false;

            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            foreach (DataGridViewColumn column in dataGridView2.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            foreach (DataGridViewColumn column in dataGridView3.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            // 浅色现代风：统一窗体/表格/按钮样式；列名保持英文键不变，仅改显示列头
            UiTheme.StyleForm(this);
            UiTheme.StyleGrid(dataGridView1);
            UiTheme.StyleGrid(dataGridView2);
            UiTheme.StyleGrid(dataGridView3);
            UiTheme.StyleButton(saveCfgButton, "save");
            UiTheme.StyleButton(readCfgButton, "folder");

            // ===== 布局重做：窗口可缩放 + Dock 自适应 =====
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimumSize = new Size(902, 682);

            // 顶部按钮行（配置按钮右对齐）
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 36 };
            this.Controls.Remove(saveCfgButton);
            this.Controls.Remove(readCfgButton);
            readCfgButton.Dock = DockStyle.Right;
            readCfgButton.Size = new Size(104, 28);
            saveCfgButton.Dock = DockStyle.Right;
            saveCfgButton.Size = new Size(104, 28);
            topPanel.Controls.Add(readCfgButton); // 先加入者靠最右
            topPanel.Controls.Add(saveCfgButton);

            // 主体：上行=树|信号表（SplitContainer 30%/70%），下行=报文页签
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 4,
                Panel1MinSize = 150,
                Panel2MinSize = 300
            };
            this.Controls.Remove(treeView1);
            this.Controls.Remove(dataGridView1);
            treeView1.Dock = DockStyle.Fill;
            dataGridView1.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(treeView1);
            split.Panel2.Controls.Add(dataGridView1);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(6, 0, 6, 6)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            layout.Controls.Add(split, 0, 0);

            this.Controls.Remove(tabControl1);
            tabControl1.Dock = DockStyle.Fill;
            dataGridView2.Dock = DockStyle.Fill;
            dataGridView3.Dock = DockStyle.Fill;
            layout.Controls.Add(tabControl1, 0, 1);

            // 先加 Fill 再加 Top：Top 先停靠，Fill 占剩余
            this.Controls.Add(layout);
            this.Controls.Add(topPanel);
            split.SplitterDistance = 280;

            // 信号表/发送列表列宽按比例填充（消除右侧空白与列宽矛盾设置）
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            UiTheme.SetGridHeaders(dataGridView1,
                ("SignalName", "信号名"), ("Value", "物理值"), ("RawValue", "原始值"));
            UiTheme.SetGridHeaders(dataGridView2,
                ("MessageID", "报文ID"), ("MessageName", "报文名称"), ("CycleTime(ms)", "周期(ms)"),
                ("SendCnt", "发送次数"), ("Enable", "使能"));
            UiTheme.SetGridHeaders(dataGridView3,
                ("MessageID", "报文ID"), ("CycleTime(ms)", "周期(ms)"), ("SendCnt", "发送次数"),
                ("Cycle Send", "周期发送"), ("SigleSend", "单次发送"));
        }

        /* 循环发送报文 */
        private void SendDataCycleDeal()
        {
            sendControlEvent.Reset();
            Task task = new Task(() =>
            {
                sw.Start();
                int runCnt = 0;
                while (aliveFlag)
                {
                    try
                    {
                        foreach (var message in messages)
                        {
                            //if (0x314 == message.messgeId && message.sendFalg && message.enableFlag)
                            //{
                            //    Console.WriteLine("NextSendTime:" + message.NextSendTime + " ElapsedMilliseconds:" + sw.ElapsedMilliseconds);
                            //}
                            if (message.sendFalg && message.enableFlag &&
                              (0 == message.NextSendTime || sw.ElapsedMilliseconds >= message.NextSendTime))
                            {
                                message.NextSendTime = sw.ElapsedMilliseconds + message.cycleTime;
                                message.aliveCount++;
                                message.aliveCount &= 0x0F;
                                BaseParamter.dbcHelper.SendCanMessage(message.messgeId);
                            }
                        }
                        if(++runCnt >= 60)
                        {
                            runCnt = 0;
                            if (this.IsHandleCreated)
                            {
                                this.BeginInvoke((EventHandler)(delegate
                                {
                                    try
                                    {
                                        int index = 0;
                                        for (index = 0; index< messages.Count; index++)
                                        {
                                            if (null != messages[index] && messages[index].sendFalg && messages[index].enableFlag)
                                            {
                                                dataGridView2.Rows[index].Cells[(int)dataGridView2ColumnEnum.SendCnt].Value = messages[index].sendCnt.ToString();
                                            }
                                        }
                                        //foreach (DataRow row in messagesTable.Rows)
                                        //{
                                        //    if(null != messages[index])
                                        //    {
                                        //        if (messages[index].sendFalg && messages[index].enableFlag)
                                        //        {
                                        //            dataGridView2.Rows[index].Cells[(int)dataGridView2ColumnEnum.SendCnt].Value = messages[index].sendCnt.ToString();
                                        //        }
                                        //    }
                                        //    index++;
                                        //}

                                        //foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                                        //{
                                        //    if (message.sendFalg && message.enableFlag)
                                        //    {
                                        //        int index = 0;
                                        //        foreach (DataRow row in messagesTable.Rows)
                                        //        {
                                        //            if (row["MessageName"].Equals(message.messageName))
                                        //            {
                                        //                dataGridView2.Rows[index].Cells[(int)dataGridView2ColumnEnum.SendCnt].Value = message.sendCnt.ToString();
                                        //                break;
                                        //            }
                                        //            else
                                        //            {
                                        //                index++;
                                        //            }
                                        //        }
                                        //    }
                                        //}
                                    }
                                    catch { }
                                }));
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                    }
                    catch { }
                    //timeBeginPeriod(2);
                    Thread.Sleep(0);
                    //timeEndPeriod(2);
                    //sendControlEvent.WaitOne(10);
                }
            });
            task.Start();
        }

        public void SetCANMessageSendENable(ref CAN_Data.Message message ,bool status, bool customFlag)
        {
            if(false == customFlag)
            {
                if (true == status)
                {
                    message.enableFlag = true;
                    multiMessageCANScheduler.AddMessage(message.messgeId, message.cycleTime);
                }
                else
                {
                    message.enableFlag = false;
                    multiMessageCANScheduler.RemoveMessage(message.messgeId);
                }
            }
            else
            {
                if (true == status)
                {
                    message.enableFlag = true;
                    multiMessageCANScheduler.AddMessageCustom(ref message, message.cycleTime);
                }
                else
                {
                    message.enableFlag = false;
                    multiMessageCANScheduler.RemoveMessageCustom(message.messgeId);
                }
            }
        }

        private void DataGridView2_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (dataGridView2SelectRowIndex == -1 || e.RowIndex < 0)
                {
                    return;
                }
                dataGridView2SelectRowIndex = e.RowIndex; /* 获取点击的行索引 */
                dataGridView2SelectColumnIndex = e.ColumnIndex;
                DataRow row = messagesTable.Rows[dataGridView2SelectRowIndex];
                var Nowmessage = BaseParamter.dbcHelper.dbcFile.messages[0];
                int NowSelectMessageIndex = 0;

                /* 找到对应的报文 */
                foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                {
                    if (message.messageName.Equals(row[(int)dataGridView2ColumnEnum.MessageName]))
                    {
                        Nowmessage = message;
                        break;
                    }
                    NowSelectMessageIndex++;
                }

                if (dataGridView2.Columns["Enable"].Index == dataGridView2SelectColumnIndex)
                {
                    if (row[dataGridView2SelectColumnIndex].Equals("True"))
                    {
                        messagesTable.Rows[dataGridView2SelectRowIndex].SetField("Enable", "False");
                        SetCANMessageSendENable(ref Nowmessage, false, false);
                    }
                    else
                    {
                        messagesTable.Rows[dataGridView2SelectRowIndex].SetField("Enable", "True");
                        SetCANMessageSendENable(ref Nowmessage, true, false);
                    }
                }
                else if (dataGridView2.Columns["MessageID"].Index == dataGridView2SelectColumnIndex ||
                         dataGridView2.Columns["MessageName"].Index == dataGridView2SelectColumnIndex)
                {
                    if (dataTable.Rows.Count > 0)
                    {
                        BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = dataTable;
                    }
                    SelectMessageIndex = NowSelectMessageIndex;
                    if (BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable == null)
                    {
                        BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = new DataTable();
                        foreach (DataGridViewColumn column in dataGridView1.Columns)
                        {
                            // Check if the header text is found
                            BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable.Columns.Add(column.HeaderText, typeof(string));
                            dataGridView1Column++;
                        }
                    }
                    dataTable = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable;
                    UpdateDbcListview(SelectMessageIndex);
                }
            }
            catch { }
        }

        private void CanSend_Load(object sender, EventArgs e)
        {
            treeView1.Nodes.Add("Nodes");
            treeView1.Nodes.Add("Message");
            if (BaseParamter.dbcHelper.dbcFile.messages.Count != 0)
            {
                UpdateDbcTreeview();
            }

            messagesTable.Rows.Clear();
            foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
            {
                DataRow newRow;
                int i;
                if (message.sendFalg)
                {
                    message.NextSendTime = 0;
                    newRow = messagesTable.NewRow();
                    newRow["MessageID"] = "0x" + message.messgeId.ToString("X2");
                    newRow["MessageName"] = message.messageName;
                    newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                    newRow["SendCnt"] = message.sendCnt.ToString();
                    newRow["Enable"] = message.enableFlag.ToString();
                    messagesTable.Rows.Add(newRow);
                }
            }
            dataGridView2.DataSource = messagesTable;
            dataGridView2.Columns[0].Width = 100;
            dataGridView2.Columns[1].Width = 150;
            dataGridView2.Columns[2].Width = 150;
            dataGridView2.Columns[3].Width = 125;
            dataGridView2.Columns[4].Width = 75;
            dataGridView2.Columns[0].ReadOnly = true;
            dataGridView2.Columns[1].ReadOnly = true;
            dataGridView2.Columns[3].ReadOnly = true;
            dataGridView2.Columns[4].ReadOnly = true;
            dataGridView2.Refresh();
            aliveFlag = true;

            dataGridView3.DataSource = addMessagesTable;
            dataGridView3.Columns[0].Width = 80;
            dataGridView3.Columns[1].Width = 120;
            for(int i=2; i<10; i++)
            {
                dataGridView3.Columns[i].Width = 45;
            }
            dataGridView3.Columns[10].Width = 80;
            dataGridView3.Columns[11].Width = 100;
            dataGridView3.Columns[12].Width = 80;
            dataGridView3.Columns[10].ReadOnly = true;
            dataGridView3.Columns[11].ReadOnly = true;
            dataGridView3.Columns[12].ReadOnly = true;
            dataGridView3.Refresh();
            addMessagesTableInit();


            //// 创建多消息调度器
            //var scheduler = new MultiMessageCANScheduler(messages =>
            //{
            //    // 批量发送所有到期的CAN报文
            //    foreach (var msg in messages)
            //    {
            //        msg.NextSendTime = sw.ElapsedMilliseconds + msg.cycleTime;
            //        msg.aliveCount++;
            //        msg.aliveCount &= 0x0F;
            //        BaseParamter.dbcHelper.SendCanMessage(msg.messgeId);
            //    }
            //});
            //multiChartFromScheduler = scheduler;
            // 启动调度器
            multiMessageCANScheduler.Start();

            // DBC统一来自"通道配置"的聚合视图（启动时已由Main恢复），直接刷新界面
            {
                try
                {
                    UpdateDbcTreeview();
                    messagesTable.Rows.Clear();
                    foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        DataRow newRow;
                        int i;
                        if (message.sendFalg)
                        {
                            newRow = messagesTable.NewRow();
                            newRow["MessageID"] = "0x" + message.messgeId.ToString("X2");
                            newRow["MessageName"] = message.messageName;
                            newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                            newRow["SendCnt"] = message.sendCnt.ToString();
                            newRow["Enable"] = message.enableFlag.ToString();
                            messagesTable.Rows.Add(newRow);
                            if (message.enableFlag)
                            {
                                multiMessageCANScheduler.AddMessage(message.messgeId, message.cycleTime);
                            }
                        }
                        message.NextSendTime = 0;
                    }
                    //dataGridView2.DataSource = messagesTable;
                    //dataGridView2.Columns[0].Width = 100;
                    //dataGridView2.Columns[0].Width = 150;
                    //dataGridView2.Columns[1].Width = 150;
                    //dataGridView2.Columns[2].Width = 125;
                    //dataGridView2.Columns[3].Width = 75;
                    //dataGridView2.Refresh();

                    UpdateDbcListview(SelectMessageIndex);
                    foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        message.NextSendTime = 0;
                    }
                    sw = new Stopwatch();
                    sw.Start();

                    if (Main.canSendOpenFlag)
                    {
                        Main.canSend.UpdateDbcTreeview();
                    }
                    Main.main.UpdateDbcTreeview();
                }
                catch
                {
                    /* empty */
                }
            }

            timer1.Interval = 200; // 降低UI定时器频率
            timer1.Start();
        }

        internal void UpdateDbcTreeview()
        {
            for (int i = 0; i < treeView1.Nodes.Count; i++)
            {
                treeView1.Nodes[i].Nodes.Clear();
            }

            for (int i = 0; i < BaseParamter.dbcHelper.dbcFile.nodes.Count; i++)
            {
                treeView1.Nodes[0].Nodes.Add(BaseParamter.dbcHelper.dbcFile.nodes[i]);
            }
            for (int i = 0; i < BaseParamter.dbcHelper.dbcFile.messages.Count; i++)
            {
                if (BaseParamter.dbcHelper.dbcFile.messages[i].sendFalg)
                {
                    treeView1.Nodes[1].Nodes.Add(BaseParamter.dbcHelper.dbcFile.messages[i].messageName +"(发送)");
                }
                else
                {
                    treeView1.Nodes[1].Nodes.Add(BaseParamter.dbcHelper.dbcFile.messages[i].messageName);
                }
            }

            treeView1.ExpandAll();
        }

        private void UpdateDbcListview(int index)
        {
            try
            {
                int i;
                if (BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
                {
                    return;
                }
                dataTable.Rows.Clear();
                DataRow newRow;
                for (i = 0; i < BaseParamter.dbcHelper.dbcFile.messages[index].signals.Count; i++)
                {
                    newRow = dataTable.NewRow();
                    var signal = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i];
                    newRow["SignalName"] = signal.signalName;
                    newRow["Value"] = signal.enumDefinitions?.ContainsKey(signal.cmdValue) == true ?
                            signal.enumDefinitions[signal.cmdValue] :
                            signal.cmdValue.ToString("F1");
                    newRow["RawValue"] = CanMessageBuilder.ConvertToRawValue(signal.cmdValue, signal);

                    dataTable.Rows.Add(newRow);
                }

                dataGridView1.DataSource = dataTable;

                dataGridView1.Columns[0].Width = 200;
                dataGridView1.Columns[1].Width = 150;
                dataGridView1.Columns[2].Width = 75;

                dataGridView1.Refresh();
            }catch (Exception ex) { }

        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if ((e.Node.Parent != null) && (e.Node.Parent.Text == "Message"))
            {
                if(dataTable.Rows.Count > 0)
                {
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = dataTable;
                }
                SelectMessageIndex = e.Node.Index;
                if(BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable == null)
                {
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = new DataTable();
                    foreach (DataGridViewColumn column in dataGridView1.Columns)
                    {
                        // Check if the header text is found
                        BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable.Columns.Add(column.HeaderText, typeof(string));
                        dataGridView1Column++;
                    }
                }
                dataTable = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable;
                UpdateDbcListview(SelectMessageIndex);
            }
        }

        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            if(BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].sendFalg)
            {
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].sendFalg = false;
                multiMessageCANScheduler.RemoveMessage(BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].messgeId);
                treeView1.Nodes[1].Nodes[SelectMessageIndex].Text = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].messageName;
            }
            else
            {
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].sendFalg = true;
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].enableFlag = true;
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].NextSendTime = 0;
                multiMessageCANScheduler.AddMessage(BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].messgeId, BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].cycleTime);
                treeView1.Nodes[1].Nodes[SelectMessageIndex].Text = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].messageName + "(发送)";
            }
            tabControl1.SelectedIndex = 0;

            messagesTable.Rows.Clear();
            foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
            {
                DataRow newRow;
                int i;
                if(message.sendFalg)
                {
                    newRow = messagesTable.NewRow();
                    newRow["MessageID"] = "0x" + message.messgeId.ToString("X2");
                    newRow["MessageName"] = message.messageName;
                    newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                    newRow["SendCnt"] = message.sendCnt.ToString();
                    newRow["Enable"] = message.enableFlag.ToString();
                    messagesTable.Rows.Add(newRow);
                }
            }
            dataGridView2.DataSource = messagesTable;
            dataGridView2.Columns[0].Width = 100;
            dataGridView2.Columns[0].Width = 150;
            dataGridView2.Columns[1].Width = 150;
            dataGridView2.Columns[2].Width = 125;
            dataGridView2.Columns[3].Width = 75;
            dataGridView2.Refresh();
        }

        private void dataGridView2_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.ColumnIndex == (int)dataGridView2ColumnEnum.CycleTime)
                {
                    var Nowmessage = BaseParamter.dbcHelper.dbcFile.messages[0];
                    DataRow row = messagesTable.Rows[e.RowIndex];
                    /* 找到对应的报文 */
                    foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        Console.WriteLine(message.messageName.ToString() + row[(int)dataGridView2ColumnEnum.MessageName]);
                        if (message.messageName.Equals(row[(int)dataGridView2ColumnEnum.MessageName]))
                        {
                            Nowmessage = message;
                            break;
                        }
                    }
                    object cellValue = dataGridView2.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                    // 空值校验
                    if (cellValue != null && cellValue != DBNull.Value)
                    {
                        // 类型转换校验
                        if (int.TryParse(cellValue.ToString(), out int cycleTime))
                        {
                            // 更新 DataTable 和 Nowmessage
                            cycleTime = (cycleTime <= 0) ? 1 : cycleTime;
                            messagesTable.Rows[dataGridView2SelectRowIndex].SetField("CycleTime(ms)", cycleTime);
                            Nowmessage.cycleTime = (uint)cycleTime;// 更新调度器中的间隔时间
                            multiMessageCANScheduler.UpdateMessageInterval(Nowmessage.messgeId, (uint)cycleTime);
                        }
                        else
                        {
                            // 处理非数值类型（如日志、默认值）
                            //MessageBox.Show($"无效的数值格式: {cellValue}");
                        }
                    }
                }
            }
            catch { }
        }

        private void CanSend_FormClosed(object sender, FormClosedEventArgs e)
        {
            aliveFlag = false;
            multiMessageCANScheduler.Dispose();
            Main.canSend = null;
            Main.canSendOpenFlag = false;
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 1 || e.RowIndex < 0) return;

            var currentValue = dataGridView1.Rows[e.RowIndex].Cells[1].Value?.ToString();
            var signal = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].signals[e.RowIndex];

            if (signal.enumDefinitions == null || signal.enumDefinitions.Count == 0)
            {
                // 非枚举类型，验证数值格式
                if (double.TryParse(currentValue, out double num))
                {
                    // 更新信号值和RawValue
                    signal.cmdValue = num;
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].updateFlag = true;
                    dataTable.Rows[e.RowIndex]["Value"] = num.ToString("F1");
                    dataTable.Rows[e.RowIndex]["RawValue"] = CanMessageBuilder.ConvertToRawValue(num, signal).ToString("F1");
                }
                else
                {
                    // 输入无效，恢复旧值
                    dataGridView1.Rows[e.RowIndex].Cells[1].Value = dataTable.Rows[e.RowIndex]["Value"];
                }
            }
        }

        private void comBoxShow()
        {
            try
            {
                // 清除所有现存的下拉框
                foreach (Control ctrl in dataGridView1.Controls.OfType<ComboBox>().ToList())
                {
                    dataGridView1.Controls.Remove(ctrl);
                    ctrl.Dispose();
                }
                _currentComboBox = null;

                if (dataGridView1.CurrentCell?.ColumnIndex != 1 || dataGridView1.CurrentCell.RowIndex < 0) return;

                var currentRowIndex = dataGridView1.CurrentCell.RowIndex;
                var signal = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].signals[currentRowIndex];

                if (signal?.enumDefinitions == null || signal.enumDefinitions.Count == 0) return;

                var cellRect = dataGridView1.GetCellDisplayRectangle(1, currentRowIndex, true);
                _currentComboBox = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = cellRect.Width,
                    Location = new Point(cellRect.X, cellRect.Y),
                    Tag = currentRowIndex,
                    DataSource = new BindingSource(signal.enumDefinitions, null),
                    DisplayMember = "Value",
                    ValueMember = "Key"
                };

                // 设置默认选中项
                var currentValue = dataTable.Rows[currentRowIndex]["Value"]?.ToString();
                var selectedItem = signal.enumDefinitions.FirstOrDefault(kv => kv.Value == currentValue);
                _currentComboBox.SelectedItem = selectedItem;

                // 事件处理
                _currentComboBox.SelectionChangeCommitted += (s, args) =>
                {
                    var selected = (KeyValuePair<double, string>)_currentComboBox.SelectedItem;
                    dataTable.Rows[currentRowIndex]["Value"] = selected.Value;
                    dataTable.Rows[currentRowIndex]["RawValue"] = CanMessageBuilder.ConvertToRawValue(selected.Key, signal).ToString("F1");
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].signals[currentRowIndex].cmdValue = selected.Key;

                    dataGridView1.Controls.Remove(_currentComboBox);
                    _currentComboBox.Dispose();
                    _currentComboBox = null;

                    dataGridView1.EndEdit();
                    dataGridView1.CurrentCell = dataGridView1.Rows[currentRowIndex].Cells[1]; // 重新聚焦
                    dataGridView1.BeginEdit(true); // 重新进入编辑模式
                };

                dataGridView1.Controls.Add(_currentComboBox);
                _currentComboBox.Show();
                _currentComboBox.DroppedDown = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"错误：{ex.Message}");
            }
        }
        private void dataGridView1_CellClick_1(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 1 && e.RowIndex >= 0)
            {
                dataGridView1.BeginEdit(true); // 强制进入编辑模式

                comBoxShow();/* 显示下拉框按钮 */
            }
        }

        private void DataGridView1_Scroll(object sender, ScrollEventArgs e)
        {
            if (_currentComboBox != null && dataGridView1.CurrentCell != null)
            {
                var cellRect = dataGridView1.GetCellDisplayRectangle(
                    dataGridView1.CurrentCell.ColumnIndex,
                    dataGridView1.CurrentCell.RowIndex,
                    true
                );
                _currentComboBox.Location = new Point(cellRect.X, cellRect.Y);
            }
        }


        // 修改AppConfig类，包含完整的DBC数据
        [Serializable]
        public class AppConfig
        {
            public int SelectedMessageIndex { get; set; }
            public DbcFile DbcData { get; set; } // 直接保存DBC文件对象
        }

        private void saveCfgButton_Click(object sender, EventArgs e)
        {
            if (0 == BaseParamter.dbcHelper.dbcFile.messages.Count)
            {
                MessageBox.Show("请先加载DBC文件");
                return;
            }

            var config = new AppConfig
            {
                SelectedMessageIndex = SelectMessageIndex,
                DbcData = BaseParamter.dbcHelper.dbcFile // 直接保存内存中的DBC对象
            };


            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "配置文件|*.dbccfg",
                Title = "保存配置"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(config,
                        Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(saveDialog.FileName, json);
                    MessageBox.Show("配置保存成功");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败: {ex.Message}");
                }
            }
        }
        private void loadCfgFile(string FileName)
        {
            try
            {
                // DBC本体统一来自"通道配置"，.dbccfg只恢复发送配置（发送标记/周期/命令值）
                if (0 == BaseParamter.dbcHelper.dbcFile.messages.Count)
                {
                    MessageBox.Show("请先在通道配置中为CAN通道加载DBC文件，再导入发送配置");
                    return;
                }

                Console.WriteLine(FileName);
                string json = File.ReadAllText(FileName);
                AppConfig config = JsonConvert.DeserializeObject<AppConfig>(json,
                    new JsonSerializerSettings
                    {
                        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                        TypeNameHandling = TypeNameHandling.Auto
                    });

                // 按报文ID把配置中的发送设置应用到当前通道DBC聚合视图
                if (config.DbcData?.messages != null)
                {
                    foreach (var cfgMsg in config.DbcData.messages)
                    {
                        if (!BaseParamter.dbcHelper.dbcFile.messageDict.TryGetValue(cfgMsg.messgeId, out var curMsg))
                            continue;

                        curMsg.sendFalg = cfgMsg.sendFalg;
                        curMsg.enableFlag = cfgMsg.enableFlag;
                        curMsg.cycleTime = cfgMsg.cycleTime;
                        curMsg.NextSendTime = 0;

                        // 按信号名恢复命令值
                        if (cfgMsg.signals != null)
                        {
                            foreach (var cfgSig in cfgMsg.signals)
                            {
                                var curSig = curMsg.signals.FirstOrDefault(s => s.signalName == cfgSig.signalName);
                                if (curSig != null) curSig.cmdValue = cfgSig.cmdValue;
                            }
                        }
                    }
                }

                SelectMessageIndex = (config.SelectedMessageIndex >= 0 &&
                    config.SelectedMessageIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
                    ? config.SelectedMessageIndex : 0;

                // 刷新UI
                UpdateDbcTreeview();
                messagesTable.Rows.Clear();
                foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
                {
                    DataRow newRow;
                    int i;
                    if (message.sendFalg)
                    {
                        newRow = messagesTable.NewRow();
                        newRow["MessageID"] = "0x" + message.messgeId.ToString("X2");
                        newRow["MessageName"] = message.messageName;
                        newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                        newRow["SendCnt"] = message.sendCnt.ToString();
                        newRow["Enable"] = message.enableFlag.ToString();
                        messagesTable.Rows.Add(newRow);
                        if (message.enableFlag)
                        {
                            multiMessageCANScheduler.AddMessage(message.messgeId, message.cycleTime);
                        }
                    }
                    message.NextSendTime = 0;
                }
                dataGridView2.DataSource = messagesTable;
                dataGridView2.Columns[0].Width = 100;
                dataGridView2.Columns[0].Width = 150;
                dataGridView2.Columns[1].Width = 150;
                dataGridView2.Columns[2].Width = 125;
                dataGridView2.Columns[3].Width = 75;
                dataGridView2.Refresh();

                UpdateDbcListview(SelectMessageIndex);
                foreach(var message in BaseParamter.dbcHelper.dbcFile.messages)
                {
                    message.NextSendTime = 0;
                }
                sw = new Stopwatch();
                sw.Start();

                if (Main.canSendOpenFlag)
                {
                    Main.canSend.UpdateDbcTreeview();
                }
                Main.main.UpdateDbcTreeview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败: {ex.Message}");
            }
        }
        private void readCfgButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "配置文件|*.dbccfg",
                Title = "加载配置"
            };

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                loadCfgFile(openDialog.FileName);
            }
        }

        private void LoadFilePath(string filePath)
        {
            // 仅支持拖入.dbccfg发送配置；.dbc统一在"通道配置"中加载
            if (filePath.Contains(".dbccfg"))
            {
                loadCfgFile(filePath);
            }
        }
        private void CanSend_DragDrop(object sender, DragEventArgs e)
        {
            LoadFilePath(((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString());
        }

        private void CanSend_DragEnter(object sender, DragEventArgs e)
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

        private void dataGridView1_KeyDown_1(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && dataGridView1.CurrentCell.ColumnIndex == 1)
            {
                // 强制结束编辑
                dataGridView1.EndEdit();

                // 模拟触发CellEndEdit
                var cell = dataGridView1.CurrentCell;
                var eventArgs = new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex);
                dataGridView1_CellEndEdit(sender, eventArgs);

                e.Handled = true;
            }
        }

        int timeout = 0;
        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                timeout++;
                //if (timeout == 100)
                //{
                //    CAN_Data.Message message = new CAN_Data.Message();
                //    message.messgeId = 0x33A;
                //    message.cycleTime = 1000;
                //    message.sendBuf = new byte[8] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                //    message.sendFalg = true;
                //    message.enableFlag = false;
                //    CAN_API.CAN_API.CanTransmit(message.messgeId, (ushort)message.sendBuf.Length, message.sendBuf); 
                //}
                //else if(timeout == 200)
                //{
                //    timeout = 0;
                //    CAN_Data.Message message = new CAN_Data.Message();
                //    message.messgeId = 0x33A;
                //    message.cycleTime = 1000;
                //    message.sendBuf = new byte[8] { 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                //    message.sendFalg = false; ;
                //    message.enableFlag = false;
                //    CAN_API.CAN_API.CanTransmit(message.messgeId, (ushort)message.sendBuf.Length, message.sendBuf);
                //}
                // 更新DBC报文的发送计数
                int index = 0;
                foreach (DataRow row in messagesTable.Rows)
                {
                    var msg = BaseParamter.dbcHelper.GetMessageByMsgName((string)row["MessageName"]);
                    if (msg != null)
                    {
                        // 直接比较数值而不是字符串，避免不必要的更新
                        if (msg.sendCnt.ToString() != row["SendCnt"]?.ToString())
                        {
                            dataGridView2.Rows[index].Cells[(int)dataGridView2ColumnEnum.SendCnt].Value = msg.sendCnt.ToString();
                        }
                    }
                    index++;
                }

                // 更新自定义报文的发送计数
                index = 0;
                foreach (DataRow row in addMessagesTable.Rows)
                {
                    // 跳过最后一行（"双击添加"行）
                    if (index < customMessagesList.Count)
                    {
                        var msg = customMessagesList[index];
                        if (msg != null)
                        {
                            // 直接比较数值而不是字符串
                            if (msg.sendCnt.ToString() != row["SendCnt"]?.ToString())
                            {
                                dataGridView3.Rows[index].Cells["SendCnt"].Value = msg.sendCnt.ToString();
                            }
                        }
                    }
                    index++;
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不影响程序运行
                System.Diagnostics.Debug.WriteLine($"定时器更新错误: {ex.Message}");
            }
        }

        //public class MultimediaTimer : IDisposable
        //{
        //    // 定时器回调委托
        //    private delegate void TimeProc(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

        //    // Win32 API 导入
        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeSetEvent(
        //        uint uDelay,
        //        uint uResolution,
        //        TimeProc lpTimeProc,
        //        UIntPtr dwUser,
        //        uint fuEvent);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeKillEvent(uint uTimerID);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeBeginPeriod(uint uPeriod);

        //    [DllImport("winmm.dll", SetLastError = true)]
        //    private static extern uint timeEndPeriod(uint uPeriod);

        //    // 常量定义
        //    private const uint TIME_PERIODIC = 0x0001;
        //    private const uint TIME_ONESHOT = 0x0000;
        //    private const uint TIME_KILL_SYNC = 0x0100;

        //    private uint _timerId;
        //    private readonly TimeProc _timeProc;
        //    private readonly Action _callback;
        //    private bool _disposed = false;
        //    private bool _isRunning = false;

        //    public MultimediaTimer(Action callback)
        //    {
        //        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        //        _timeProc = new TimeProc(TimerCallback);
        //    }

        //    /// <summary>
        //    /// 启动定时器
        //    /// </summary>
        //    /// <param name="intervalMs">定时间隔（毫秒）</param>
        //    /// <param name="oneShot">是否只执行一次</param>
        //    public void Start(uint intervalMs, bool oneShot = false)
        //    {
        //        if (_isRunning) return;

        //        // 设置系统定时器精度（可选，但可以提高精度）
        //        timeBeginPeriod(1);

        //        uint mode = oneShot ? TIME_ONESHOT : TIME_PERIODIC;

        //        _timerId = timeSetEvent(
        //            intervalMs,        // 延迟时间（毫秒）
        //            0,                // 分辨率（0表示最高精度）
        //            _timeProc,        // 回调函数
        //            UIntPtr.Zero,     // 用户数据
        //            mode);           // 模式：周期性或单次

        //        if (_timerId == 0)
        //        {
        //            throw new Exception("无法创建多媒体定时器");
        //        }

        //        _isRunning = true;
        //    }

        //    /// <summary>
        //    /// 停止定时器
        //    /// </summary>
        //    public void Stop()
        //    {
        //        if (!_isRunning) return;

        //        if (_timerId != 0)
        //        {
        //            timeKillEvent(_timerId);
        //            _timerId = 0;
        //        }

        //        // 恢复系统定时器精度
        //        timeEndPeriod(1);
        //        _isRunning = false;
        //    }

        //    private void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        //    {
        //        try
        //        {
        //            _callback?.Invoke();
        //        }
        //        catch (Exception ex)
        //        {
        //            // 记录异常，避免异常传播到非托管代码
        //            System.Diagnostics.Debug.WriteLine($"定时器回调异常: {ex.Message}");
        //        }
        //    }

        //    public void Dispose()
        //    {
        //        Dispose(true);
        //        GC.SuppressFinalize(this);
        //    }

        //    protected virtual void Dispose(bool disposing)
        //    {
        //        if (!_disposed)
        //        {
        //            if (disposing)
        //            {
        //                // 释放托管资源
        //            }

        //            Stop();
        //            _disposed = true;
        //        }
        //    }

        //    ~MultimediaTimer()
        //    {
        //        Dispose(false);
        //    }
        //}
        public class MultiMessageCANScheduler : IDisposable
        {
            private class CANMessageSchedule : IComparable<CANMessageSchedule>
            {
                public CAN_Data.Message Message { get; set; }
                public long IntervalMs { get; set; }
                public long NextTriggerTime { get; set; }
                public uint CanId => Message.messgeId;
                public int CompareTo(CANMessageSchedule other)
                {
                    return NextTriggerTime.CompareTo(other.NextTriggerTime);
                }
            }
            // 简化优先队列实现，使用 SortedSet（需要处理重复元素）
            private class CANMessageScheduleQueue
            {
                private readonly SortedSet<CANMessageSchedule> _sortedSet;
                private long _sequenceNumber = 0; // 用于处理相同触发时间的元素

                public int Count => _sortedSet.Count;

                public CANMessageScheduleQueue()
                {
                    _sortedSet = new SortedSet<CANMessageSchedule>(new CANMessageScheduleComparer());
                }

                private class CANMessageScheduleComparer : IComparer<CANMessageSchedule>
                {
                    public int Compare(CANMessageSchedule x, CANMessageSchedule y)
                    {
                        if (x == null && y == null) return 0;
                        if (x == null) return -1;
                        if (y == null) return 1;

                        int timeCompare = x.NextTriggerTime.CompareTo(y.NextTriggerTime);
                        if (timeCompare != 0) return timeCompare;

                        // 如果触发时间相同，使用CanId作为次要排序条件
                        return x.CanId.CompareTo(y.CanId);
                    }
                }

                public void Enqueue(CANMessageSchedule item)
                {
                    _sortedSet.Add(item);
                }

                public CANMessageSchedule Dequeue()
                {
                    if (_sortedSet.Count == 0)
                        throw new InvalidOperationException("Queue is empty");

                    var first = _sortedSet.Min;
                    _sortedSet.Remove(first);
                    return first;
                }

                public CANMessageSchedule Peek()
                {
                    if (_sortedSet.Count == 0)
                        throw new InvalidOperationException("Queue is empty");
                    return _sortedSet.Min;
                }

                public bool Remove(CANMessageSchedule item)
                {
                    return _sortedSet.Remove(item);
                }
            }

            private readonly CANMessageScheduleQueue _priorityQueue;
            private readonly CANMessageScheduleQueue _priorityQueueCustom;
            private readonly Dictionary<uint, CANMessageSchedule> _scheduleLookup;
            private readonly Dictionary<uint, CANMessageSchedule> _scheduleLookupCustom;
            private readonly List<CAN_Data.Message> _sendBuffer;
            private readonly List<CAN_Data.Message> _sendBufferCustom;
            private readonly object _lock = new object();
            private Stopwatch stopwatch = new Stopwatch();

            public MultiMessageCANScheduler()
            {
                _priorityQueue = new CANMessageScheduleQueue();
                _scheduleLookup = new Dictionary<uint, CANMessageSchedule>();
                _sendBuffer = new List<CAN_Data.Message>();

                _priorityQueueCustom = new CANMessageScheduleQueue();
                _scheduleLookupCustom = new Dictionary<uint, CANMessageSchedule>();
                _sendBufferCustom = new List<CAN_Data.Message>();
            }

            public void AddMessage(uint canId, uint intervalMs)
            {
                intervalMs = (intervalMs <= 0) ? 1 : intervalMs;
                lock (_lock)
                {
                    var schedule = new CANMessageSchedule
                    {
                        Message = BaseParamter.dbcHelper.GetMessageById(canId),
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime()
                    };
                    if(null != schedule.Message)
                    {
                        if (!_scheduleLookup.ContainsKey(canId))
                        {
                            _scheduleLookup[canId] = schedule;
                            _priorityQueue.Enqueue(schedule);
                        }
                    }
                }
            }

            public void AddMessageCustom(ref CAN_Data.Message msg, uint intervalMs)
            {
                intervalMs = (intervalMs <= 0) ? 1 : intervalMs;
                lock (_lock)
                {
                    var schedule = new CANMessageSchedule
                    {
                        Message = msg,
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime()
                    };
                    if (null != schedule.Message)
                    {
                        if (!_scheduleLookupCustom.ContainsKey(msg.messgeId))
                        {
                            _scheduleLookupCustom[msg.messgeId] = schedule;
                            _priorityQueueCustom.Enqueue(schedule);
                        }
                    }
                }
            }

            public void RemoveMessage(uint canId)
            {
                lock (_lock)
                {
                    if (_scheduleLookup.ContainsKey(canId))
                    {
                        _scheduleLookup.Remove(canId);
                    }
                }
            }
            public void RemoveMessageCustom(uint canId)
            {
                lock (_lock)
                {
                    if (_scheduleLookupCustom.TryGetValue(canId, out var schedule))
                    {
                        // 从队列中移除该调度项
                        _priorityQueueCustom.Remove(schedule);
                    }
                    if (_scheduleLookupCustom.ContainsKey(canId))
                    {
                        _scheduleLookupCustom.Remove(canId);
                    }
                }
            }
            public void Start()
            {
                stopwatch.Start();
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "CAN_Send", 1);
            }

            public void Stop()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("CAN_Send");
            }

            private void SchedulerCallback()
            {
                var currentTime = stopwatch.ElapsedMilliseconds;

                // 处理所有到期的消息
                lock (_lock)
                {
                    while (_priorityQueue.Count > 0)
                    {
                        var nextSchedule = _priorityQueue.Peek();

                        // 检查该消息是否已被移除
                        if (!_scheduleLookup.ContainsKey(nextSchedule.CanId))
                        {
                            _priorityQueue.Dequeue(); // 移除已删除的消息
                            continue;
                        }

                        // 如果下一个消息还没到期，就跳出循环
                        if (nextSchedule.NextTriggerTime > currentTime)
                            break;

                        // 出队并处理
                        var schedule = _priorityQueue.Dequeue();
                        _sendBuffer.Add(schedule.Message);

                        // 更新下次触发时间并重新入队
                        schedule.NextTriggerTime = currentTime + schedule.IntervalMs;
                        _priorityQueue.Enqueue(schedule);
                    }
                }

                lock (_lock)
                {
                    while (_priorityQueueCustom.Count > 0)
                    {
                        var nextSchedule = _priorityQueueCustom.Peek();

                        // 检查该消息是否已被移除
                        if (!_scheduleLookupCustom.ContainsKey(nextSchedule.CanId))
                        {
                            _priorityQueueCustom.Dequeue(); // 移除已删除的消息
                            continue;
                        }

                        // 如果下一个消息还没到期，就跳出循环
                        if (nextSchedule.NextTriggerTime > currentTime)
                            break;

                        // 出队并处理
                        var schedule = _priorityQueueCustom.Dequeue();
                        _sendBufferCustom.Add(schedule.Message);

                        // 更新下次触发时间并重新入队
                        schedule.NextTriggerTime = currentTime + schedule.IntervalMs;
                        _priorityQueueCustom.Enqueue(schedule);
                    }
                }

                // 批量发送
                if (_sendBuffer.Count > 0)
                {
                    // 批量发送所有到期的CAN报文
                    foreach (var msg in _sendBuffer)
                    {
                        msg.NextSendTime = Main.canSend.sw.ElapsedMilliseconds + msg.cycleTime;
                        msg.aliveCount++;
                        msg.aliveCount &= 0x0F;
                        BaseParamter.dbcHelper.SendCanMessage(msg.messgeId);
                    }
                    _sendBuffer.Clear();
                }

                // 批量发送
                if (_sendBufferCustom.Count > 0)
                {
                    // 批量发送所有到期的CAN报文
                    foreach (var msg in _sendBufferCustom)
                    {
                        msg.NextSendTime = Main.canSend.sw.ElapsedMilliseconds + msg.cycleTime;
                        msg.aliveCount++;
                        msg.aliveCount &= 0x0F;
                        BaseParamter.dbcHelper.SendCanMessage(msg);
                    }
                    _sendBufferCustom.Clear();
                }
            }

            public void UpdateMessageInterval(uint canId, uint newIntervalMs)
            {
                lock (_lock)
                {
                    if (_scheduleLookup.TryGetValue(canId, out var schedule))
                    {
                        // 更新间隔时间
                        newIntervalMs = (newIntervalMs <= 0) ? 1 : newIntervalMs;
                        schedule.IntervalMs = newIntervalMs;

                        // 注意：由于优先队列的特性，我们无法直接更新队列中的元素
                        // 这个更新会在下次重新入队时生效
                    }
                }
            }

            public void UpdateMessageIntervalCustom(uint canId, uint newIntervalMs)
            {
                lock (_lock)
                {
                    if (_scheduleLookupCustom.TryGetValue(canId, out var schedule))
                    {
                        // 更新间隔时间
                        newIntervalMs = (newIntervalMs <= 0) ? 1 : newIntervalMs;
                        schedule.IntervalMs = newIntervalMs;

                        // 从队列中移除该调度项
                        _priorityQueueCustom.Remove(schedule);

                        // 重新计算下一次触发时间：当前时间加上新的间隔
                        schedule.NextTriggerTime = GetCurrentTime() + newIntervalMs;

                        // 重新加入队列
                        _priorityQueueCustom.Enqueue(schedule);
                    }
                }
            }

            private long GetCurrentTime()
            {
                return stopwatch.ElapsedMilliseconds;
            }

            public void Dispose()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("CAN_Send");
            }
        }

        private void addMessagesTableInit()
        {
            DataRow newRow3;
            for (int i = 0; i < customMessagesList.Count; i++)
            {
                var msg = customMessagesList[i];
                newRow3 = addMessagesTable.NewRow();
                newRow3["MessageID"] = $"0x{msg.messgeId.ToString("X3")}";
                newRow3["CycleTime(ms)"] = msg.cycleTime.ToString();
                for (int j = 0; j < msg.sendBuf.Length; j++)
                {
                    newRow3[$"Data{j}"] = $"0x{msg.sendBuf[j].ToString("X2")}";
                }
                for (int j = msg.sendBuf.Length; j < 8; j++)
                {
                    newRow3[$"Data{j}"] = "*";
                }
                newRow3["SendCnt"] = msg.sendCnt;
                newRow3["Cycle Send"] = (msg.enableFlag) ? "true" : "false";
                newRow3["SigleSend"] = "单击发送";
                addMessagesTable.Rows.Add(newRow3);

                if (msg.enableFlag)
                {
                    // 修复：不再使用ref参数，而是直接传递对象
                    SetCANMessageSendENable(ref msg, true, true);
                }
            }

            newRow3 = addMessagesTable.NewRow();
            newRow3["MessageID"] = "双击添加";
            newRow3["CycleTime(ms)"] = "*";
            newRow3["Data0"] = "*";
            newRow3["Data1"] = "*";
            newRow3["Data2"] = "*";
            newRow3["Data3"] = "*";
            newRow3["Data4"] = "*";
            newRow3["Data5"] = "*";
            newRow3["Data6"] = "*";
            newRow3["Data7"] = "*";
            newRow3["SendCnt"] = 0;
            newRow3["Cycle Send"] = "*";
            newRow3["SigleSend"] = "*";
            addMessagesTable.Rows.Add(newRow3);
        }
        private void dataGridView3_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // 检查行索引和列索引是否有效（避免点击表头触发事件）
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            // 获取列名
            string columnName = dataGridView3.Columns[e.ColumnIndex].HeaderText; // 或使用 .Name 获取字段名
                                                                                 // 获取当前单元格的值
            string cellValue = dataGridView3.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

            if(columnName.Equals("MessageID")&& cellValue.Equals("双击添加"))
            {
                addMessagesTable.Rows[e.RowIndex].SetField("MessageID", "0x000");
                addMessagesTable.Rows[e.RowIndex].SetField("CycleTime(ms)", "1000");
                addMessagesTable.Rows[e.RowIndex].SetField("Data0", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data1", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data2", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data3", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data4", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data5", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data6", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Data7", "0x00");
                addMessagesTable.Rows[e.RowIndex].SetField("Cycle Send", "false");
                addMessagesTable.Rows[e.RowIndex].SetField("SigleSend", "单击发送");
                CAN_Data.Message message = new CAN_Data.Message();
                message.messgeId = 0x000;
                message.cycleTime = 1000;
                message.sendBuf = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
                message.sendFalg = true;
                message.enableFlag = false;
                customMessagesList.Add(message);
                //multiChartFromScheduler.AddMessageCustom(ref message, message.cycleTime);
                dataGridView3.Refresh();
                DataRow newRow3 = addMessagesTable.NewRow();
                newRow3["MessageID"] = "双击添加";
                newRow3["CycleTime(ms)"] = "*";
                newRow3["Data0"] = "*";
                newRow3["Data1"] = "*";
                newRow3["Data2"] = "*";
                newRow3["Data3"] = "*";
                newRow3["Data4"] = "*";
                newRow3["Data5"] = "*";
                newRow3["Data6"] = "*";
                newRow3["Data7"] = "*";
                newRow3["SendCnt"] = 0;
                newRow3["Cycle Send"] = "*";
                newRow3["SigleSend"] = "*";
                addMessagesTable.Rows.Add(newRow3);
            }
            // 示例：显示信息
            //MessageBox.Show($"列名：{columnName}\n单元格值：{cellValue}");
        }

        private void dataGridView3_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                // 检查行索引和列索引是否有效（避免点击表头触发事件）
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                // 获取列名
                string columnName = dataGridView3.Columns[e.ColumnIndex].HeaderText; // 或使用 .Name 获取字段名
                                                                                     // 获取当前单元格的值
                string cellValue = dataGridView3.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

                dataGridView3SelectRowIndex = e.RowIndex; /* 获取点击的行索引 */
                if (dataGridView3SelectRowIndex < 0 || cellValue.Equals("双击添加"))
                {
                    return;
                }
                dataGridView3SelectColumnIndex = e.ColumnIndex;
                DataRow row = addMessagesTable.Rows[dataGridView3SelectRowIndex];
                int NowSelectMessageIndex = 0;
                var msg = customMessagesList[dataGridView3SelectRowIndex];

                // 处理"单击发送"列的点击
                if (columnName.Equals("SigleSend") && cellValue.Equals("单击发送"))
                {
                    // 检查是否是有效的数据行（不是最后一行"双击添加"）
                    if (dataGridView3SelectRowIndex < customMessagesList.Count)
                    {
                        if (msg != null)
                        {
                            // 发送单条报文
                            BaseParamter.dbcHelper.SendCanMessage(msg);

                            // 更新界面显示
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField("SendCnt", msg.sendCnt.ToString());
                            dataGridView3.Refresh();

                            // 可选：添加发送成功的视觉反馈
                            dataGridView3.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor = Color.LightGreen;

                            // 延迟后恢复颜色（可选）
                            Task.Delay(200).ContinueWith(_ =>
                            {
                                if (this.IsHandleCreated)
                                {
                                    this.BeginInvoke(new Action(() =>
                                    {
                                        dataGridView3.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor = dataGridView3.DefaultCellStyle.BackColor;
                                    }));
                                }
                            });
                        }
                    }
                    return;
                }
                else if (columnName.Equals("Cycle Send"))
                {
                    if (cellValue.Equals("True"))
                    {
                        addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField("Cycle Send", "False");
                        if (null != msg)
                        {
                            SetCANMessageSendENable(ref msg, false, true);
                        }
                    }
                    else
                    {
                        addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField("Cycle Send", "True");
                        if (null != msg)
                        {
                            SetCANMessageSendENable(ref msg, true, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 添加错误处理
                MessageBox.Show($"发送报文时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView3_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                // 检查行索引和列索引是否有效（避免点击表头触发事件）
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                // 获取列名
                string columnName = dataGridView3.Columns[e.ColumnIndex].HeaderText; // 或使用 .Name 获取字段名
                                                                                     // 获取当前单元格的值
                object cellValue = dataGridView3.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;

                int value = 0;
                bool valueValid = false;

                // 空值校验
                if (cellValue != null && cellValue != DBNull.Value)
                {
                    string strValue = cellValue.ToString().Trim(); // 移除首尾空格

                    // 类型转换校验
                    try
                    {
                        // 检查字符串是否以"0x"或"0X"开头（不区分大小写）
                        if (strValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            // 使用Convert.ToInt32进行十六进制转换[7,8](@ref)
                            value = Convert.ToInt32(strValue, 16);
                            valueValid = true;
                        }
                        else
                        {
                            // 使用int.TryParse进行十进制转换
                            if (!int.TryParse(strValue, out value))
                            {
                                // 处理十进制转换失败的情况
                                // MessageBox.Show($"无效的十进制数值格式: {strValue}");
                                // 可以选择记录日志或设置默认值
                                value = 0; // 确保value有一个已知状态
                                valueValid = false;
                            }
                            else
                            {
                                valueValid = true;
                            }
                        }
                        // 如果走到这里且没有进入else的转换失败分支，说明十六进制转换成功或十进制tryparse成功
                        if (!(strValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !valueValid))
                        {
                            // 对于十六进制情况，只要没抛异常就是成功
                            // 对于十进制，tryparse成功会设置valueValid为true
                            valueValid = true;
                        }
                    }
                    catch (FormatException)
                    {
                        // 捕获转换格式错误（例如字符串包含非法字符）
                        // MessageBox.Show($"无效的数值格式: {strValue}");
                    }
                    catch (OverflowException)
                    {
                        // 捕获数值溢出错误（例如转换后的整数超出int范围）
                        // MessageBox.Show($"数值太大或太小，超出Int32范围: {strValue}");
                    }
                    catch (Exception ex)
                    {
                        // 捕获其他未预期的异常
                        // MessageBox.Show($"转换过程中发生错误: {ex.Message}");
                    }
                }

                var msg = customMessagesList[e.RowIndex];
                if (valueValid && null != msg)
                {
                    switch (columnName)
                    {
                        case "MessageID":
                            if(msg.messgeId != value)
                            {
                                multiMessageCANScheduler.RemoveMessageCustom(msg.messgeId);
                                msg.messgeId = (uint)value;
                                // 设置显示为十六进制
                                addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X3}");
                                if (msg.enableFlag)
                                {
                                    SetCANMessageSendENable(ref msg, true, true);
                                }
                                else
                                {
                                    SetCANMessageSendENable(ref msg, false, true);
                                }
                            }
                            break;
                        case "CycleTime(ms)":
                            value = (value <= 0) ? 1 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, value);
                            msg.cycleTime = (uint)value;

                            // 重要：更新调度器中的间隔时间并重新计算触发时间
                            multiMessageCANScheduler.UpdateMessageIntervalCustom(msg.messgeId, (uint)value);
                            break;
                        case "Data0":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[0] = (byte)value;
                            break;
                        case "Data1":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[1] = (byte)value;
                            break;
                        case "Data2":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[2] = (byte)value;
                            break;
                        case "Data3":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[3] = (byte)value;
                            break;
                        case "Data4":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[4] = (byte)value;
                            break;
                        case "Data5":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[5] = (byte)value;
                            break;
                        case "Data6":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[6] = (byte)value;
                            break;
                        case "Data7":
                            value = (value < 0) ? 0 : value;
                            value = (value > 255) ? 255 : value;
                            addMessagesTable.Rows[dataGridView3SelectRowIndex].SetField(columnName, $"0x{value:X2}");
                            msg.sendBuf[7] = (byte)value;
                            break;
                    }
                }
            }
            catch { }
        }
    }
}
