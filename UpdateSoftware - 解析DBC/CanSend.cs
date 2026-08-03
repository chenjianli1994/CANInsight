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

        private static List<CAN_Data.Message> customMessagesList = new List<CAN_Data.Message>();

        int SelectMessageIndex = 0;
        // 在CanSend类中添加成员变量以跟踪当前活动的下拉框
        private ComboBox _currentComboBox;

        /// <summary>左侧栏：CAN通道选择下拉框（按通道配置的BusChannels生成）</summary>
        private ComboBox _cmbChannel;
        /// <summary>左侧栏：报文ID筛选框（规则与接收页面一致，支持通配符*）</summary>
        private TextBox _txtMsgFilter;
        /// <summary>当前选中的通道索引（0-based，对应BusChannels序号）</summary>
        private int _selectedChannelIdx = 0;
        private readonly HashSet<uint> _filterIds = new HashSet<uint>();
        private readonly List<(uint mask, uint value, uint maxId)> _filterWildcards = new List<(uint mask, uint value, uint maxId)>();
        /// <summary>筛选输入项（同时用作报文名子串匹配，不区分大小写）</summary>
        private readonly List<string> _filterNameTokens = new List<string>();

        MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();
        /// <summary>正在手动连发中的发送列表行索引（防止重复点击）</summary>
        private readonly HashSet<int> _manualSendingRows = new HashSet<int>();

        // === 解析区字节编辑模式（自定义报文点击后按字节显示/编辑） ===
        private bool _byteEditorMode = false;
        private CAN_Data.Message _byteEditorMsg = null;
        private DataTable _byteTable; // 字节编辑专用表（与DBC信号dataTable隔离，避免互踩缓存）
        private int _selectedDgv2Row = -1; // 当前选中的发送列表行（RawData编辑时据此判断是否需同步解析区）

        public CanSend()
        {
            InitializeComponent();

            dataGridView2.CellClick += DataGridView2_CellClick;
            dataGridView1.Scroll += DataGridView1_Scroll; // 添加滚动事件监听
            dataTable = new DataTable();
            dataGridView1Column = 3;
            // 信号表列键固定为 SignalName/Value/RawValue（Designer列名CmdType/Physical与数据键不一致，
            // 曾导致写行异常被静默吞掉、表格为空）
            dataTable.Columns.Add("SignalName", typeof(string));
            dataTable.Columns.Add("Value", typeof(string));
            dataTable.Columns.Add("RawValue", typeof(string));

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
            // 追加"数据(hex)"列：发送原始数据的16进制表示，可直接编辑
            messagesTable.Columns.Add("RawData", typeof(string));
            // 追加"发送通道"列：逻辑通道号（0=自动跟随来源DBC通道），可直接编辑
            messagesTable.Columns.Add("TxChannel", typeof(string));
            // 追加"手动次数"列：手动发送的帧数（按报文周期连发），默认1
            messagesTable.Columns.Add("ManualSendCnt", typeof(string));
            // 隐藏列：报文来源DBC通道（多通道同名报文区分用）
            messagesTable.Columns.Add("SrcChannel", typeof(string));
            // 隐藏列：是否自定义报文行（true=自定义，按CustomIdx取customMessagesList）
            messagesTable.Columns.Add("IsCustom", typeof(bool));
            // 隐藏列：自定义报文在customMessagesList中的索引
            messagesTable.Columns.Add("CustomIdx", typeof(int));
            // 绑定DataTable到DataGridView
            dataGridView2.Columns.Clear();
            dataGridView2.DataSource = messagesTable;
            EnsureSingleSendColumn();
            EnsureRemoveColumn();
            dataGridView2.EditMode = DataGridViewEditMode.EditOnEnter; // 单击即可编辑周期/数据列

            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, dataGridView1, new object[] { true });
            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, dataGridView2, new object[] { true });

            // 禁用自动调整
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // 禁用行头
            dataGridView1.RowHeadersVisible = false;
            dataGridView2.RowHeadersVisible = false;

            foreach (DataGridViewColumn column in dataGridView1.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            foreach (DataGridViewColumn column in dataGridView2.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            // 浅色现代风：统一窗体/表格/按钮样式；列名保持英文键不变，仅改显示列头
            UiTheme.StyleForm(this);
            UiTheme.StyleGrid(dataGridView1);
            UiTheme.StyleGrid(dataGridView2);
            UiTheme.StyleButton(saveCfgButton, "save");
            UiTheme.StyleButton(readCfgButton, "folder");

            // 发送列表右键菜单：添加自定义报文
            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add("添加自定义报文", null, (s, args) => AddCustomMessage());
            dataGridView2.ContextMenuStrip = ctxMenu;

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

            // 主体：TableLayoutPanel 分栏（上行=树30%|信号表70%，下行=报文页签跨两列）
            // 不用SplitContainer，彻底避免SplitterDistance越界异常
            this.Controls.Remove(treeView1);
            this.Controls.Remove(dataGridView1);
            treeView1.Dock = DockStyle.Fill;
            dataGridView1.Dock = DockStyle.Fill;

            // 左侧栏：CAN通道选择 + 报文ID筛选 + 报文树（通道下拉最上，筛选框其次，树填充剩余）
            _cmbChannel = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.UiFont
            };
            _cmbChannel.SelectedIndexChanged += _cmbChannel_SelectedIndexChanged;
            _txtMsgFilter = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(255, 255, 240)
            };
            _txtMsgFilter.TextChanged += _txtMsgFilter_TextChanged;
            var leftToolTip = new System.Windows.Forms.ToolTip();
            leftToolTip.SetToolTip(_cmbChannel, "选择CAN通道，下方列出该通道DBC的报文");
            leftToolTip.SetToolTip(_txtMsgFilter, "按报文ID（十六进制，支持通配符如 3**）或报文名称筛选，逗号/空格分隔");
            var leftPanel = new Panel { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(treeView1);
            leftPanel.Controls.Add(_txtMsgFilter);
            leftPanel.Controls.Add(_cmbChannel);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(6, 0, 6, 6)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            layout.Controls.Add(leftPanel, 0, 0);
            layout.Controls.Add(dataGridView1, 1, 0);

            this.Controls.Remove(tabControl1);
            tabControl1.Dock = DockStyle.Fill;
            dataGridView2.Dock = DockStyle.Fill;
            layout.Controls.Add(tabControl1, 0, 1);
            layout.SetColumnSpan(tabControl1, 2);

            // 先加 Fill 再加 Top：Top 先停靠，Fill 占剩余
            this.Controls.Add(layout);
            this.Controls.Add(topPanel);

            // 信号表/发送列表列宽按比例填充（消除右侧空白与列宽矛盾设置）
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; // 列宽独立：拖动任一列不影响其他列
            UiTheme.SetGridHeaders(dataGridView1,
                ("SignalName", "信号名"), ("Value", "物理值"), ("RawValue", "原始值"));
            UiTheme.SetGridHeaders(dataGridView2,
                ("MessageID", "报文ID"), ("MessageName", "报文名称"), ("CycleTime(ms)", "周期(ms)"),
                ("SendCnt", "发送次数"), ("Enable", "使能"), ("TxChannel", "发送通道"), ("ManualSendCnt", "手动次数"));

            // 首次打开时初始化/布局/列生成较慢（JIT+句柄创建），窗体就绪前不显示，避免半成品窗口闪烁
            this.DoubleBuffered = true;
            this.Opacity = 0;
            this.Shown += (s, e) => { this.Opacity = 1; };
            // 窗口尺寸变化时报文名称/数据列自动铺满（其余列固定不联动）
            this.Resize += (s, e) => AutoFillFlexibleColumns();
            this.ResizeEnd += (s, e) => AutoFillFlexibleColumns();
        }

        /// <summary>解析报文实际发送通道（TxChannel=0时自动跟随来源DBC通道）</summary>
        internal static byte ResolveTxChannel(CAN_Data.Message msg)
        {
            return msg.TxChannel > 0 ? msg.TxChannel : BaseParamter.GetChannelOfMessage(msg);
        }

        public void SetCANMessageSendENable(ref CAN_Data.Message message ,bool status, bool customFlag)
        {
            if(false == customFlag)
            {
                if (true == status)
                {
                    message.enableFlag = true;
                    multiMessageCANScheduler.AddMessage(message, message.cycleTime);
                }
                else
                {
                    message.enableFlag = false;
                    multiMessageCANScheduler.RemoveMessage(ResolveTxChannel(message), message.messgeId);
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
                    multiMessageCANScheduler.RemoveMessageCustom(message.TxChannel > 0 ? message.TxChannel : (byte)1, message.messgeId);
                }
            }
        }

        private void DataGridView2_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0) return;
                dataGridView2SelectRowIndex = e.RowIndex;
                dataGridView2SelectColumnIndex = e.ColumnIndex;
                DataRow row = messagesTable.Rows[dataGridView2SelectRowIndex];
                bool isCustom = row.Table.Columns.Contains("IsCustom") && row.Field<bool>("IsCustom");
                var msg = FindMessageByRow(row);
                if (msg == null) return;
                _selectedDgv2Row = e.RowIndex;

                // 点击移除列：将该报文移出发送列表
                if (dataGridView2.Columns["colRemove"] != null &&
                    dataGridView2.Columns["colRemove"].Index == dataGridView2SelectColumnIndex)
                {
                    if (isCustom)
                    {
                        int idx = row.Field<int>("CustomIdx");
                        if (idx >= 0 && idx < customMessagesList.Count)
                        {
                            var cm = customMessagesList[idx];
                            multiMessageCANScheduler.RemoveMessageCustom(cm.TxChannel > 0 ? cm.TxChannel : (byte)1, cm.messgeId);
                            customMessagesList.RemoveAt(idx);
                        }
                    }
                    else
                    {
                        msg.sendFalg = false;
                        msg.enableFlag = false;
                        multiMessageCANScheduler.RemoveMessage(ResolveTxChannel(msg), msg.messgeId);
                    }
                    RebuildMessagesTable();
                    UpdateDbcTreeview();
                    return;
                }

                // 点击任意单元格：上方解析区联动——自定义行显示字节编辑器，DBC行显示信号
                if (isCustom)
                {
                    ShowByteEditor(msg);
                }
                else
                {
                    int aggIdx = BaseParamter.dbcHelper.dbcFile.messages.IndexOf(msg);
                    if (aggIdx >= 0) SwitchSignalTable(aggIdx);
                }

                if (dataGridView2.Columns["colSingleSend"] != null &&
                    dataGridView2.Columns["colSingleSend"].Index == dataGridView2SelectColumnIndex)
                {
                    // 手动发送：按"手动次数"N、以报文周期为间隔连发N帧（默认1）；发送中忽略重复点击
                    if (_manualSendingRows.Contains(dataGridView2SelectRowIndex)) return;
                    int times = 1;
                    if (int.TryParse(messagesTable.Rows[dataGridView2SelectRowIndex]["ManualSendCnt"]?.ToString(), out int n) && n >= 1)
                        times = n;
                    // 第1帧立即发送并刷新显示（SendCnt/RawData后续帧由timer1兜底刷新）；按报文TxChannel路由发送通道
                    BaseParamter.dbcHelper.SendCanMessage(msg, ResolveTxChannel(msg));
                    messagesTable.Rows[dataGridView2SelectRowIndex].SetField("SendCnt", msg.sendCnt.ToString());
                    messagesTable.Rows[dataGridView2SelectRowIndex].SetField("RawData", FormatSendBufHex(msg));
                    if (times > 1)
                    {
                        int rowIndex = dataGridView2SelectRowIndex;
                        var sendMsg = msg;
                        _manualSendingRows.Add(rowIndex);
                        Task.Run(() =>
                        {
                            try
                            {
                                for (int i = 1; i < times; i++)
                                {
                                    Thread.Sleep((int)Math.Max(1, sendMsg.cycleTime)); // 帧间隔=报文周期
                                    BaseParamter.dbcHelper.SendCanMessage(sendMsg, ResolveTxChannel(sendMsg));
                                }
                            }
                            finally { _manualSendingRows.Remove(rowIndex); }
                        });
                    }
                    var flashCell = dataGridView2.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    flashCell.Style.BackColor = Color.LightGreen;
                    Task.Delay(200).ContinueWith(_ =>
                    {
                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                flashCell.Style.BackColor = dataGridView2.DefaultCellStyle.BackColor;
                            }));
                        }
                    });
                }
                else if (dataGridView2.Columns["Enable"].Index == dataGridView2SelectColumnIndex)
                {
                    // Enable为bool复选框列，直接取反；自定义报文走customFlag=true
                    bool enabled = row.Field<bool>("Enable");
                    messagesTable.Rows[dataGridView2SelectRowIndex].SetField("Enable", !enabled);
                    var enableMsg = msg;
                    SetCANMessageSendENable(ref enableMsg, !enabled, isCustom);
                }
            }
            catch { }
        }

        /// <summary>右键"添加自定义报文"：创建一条默认自定义报文加入发送列表（ID自动避让已占用）</summary>
        private void AddCustomMessage()
        {
            uint newId = 0x100;
            var used = new HashSet<uint>(customMessagesList.Select(m => m.messgeId));
            while (used.Contains(newId)) newId++;
            var msg = new CAN_Data.Message
            {
                messgeId = newId,
                cycleTime = 1000,
                sendBuf = new byte[8],
                sendFalg = true,
                enableFlag = false,
                TxChannel = 1
            };
            customMessagesList.Add(msg);
            RebuildMessagesTable();
        }

        /// <summary>解析区切换为字节编辑模式（自定义报文）：dataGridView1按字节显示sendBuf，可编辑</summary>
        private void ShowByteEditor(CAN_Data.Message msg)
        {
            _byteEditorMode = true;
            _byteEditorMsg = msg;
            SelectMessageIndex = -1; // 标记非DBC信号模式，避免SwitchSignalTable回写踩DBC信号缓存
            if (_byteTable == null)
            {
                _byteTable = new DataTable();
                _byteTable.Columns.Add("SignalName", typeof(string));
                _byteTable.Columns.Add("Value", typeof(string));
                _byteTable.Columns.Add("RawValue", typeof(string));
            }
            _byteTable.Rows.Clear();
            int len = msg.sendBuf == null ? 8 : Math.Max(8, msg.sendBuf.Length);
            for (int i = 0; i < len; i++)
            {
                byte b = (msg.sendBuf != null && i < msg.sendBuf.Length) ? msg.sendBuf[i] : (byte)0;
                var r = _byteTable.NewRow();
                r["SignalName"] = $"Byte{i}";
                r["Value"] = $"0x{b:X2}";
                r["RawValue"] = $"0x{b:X2}";
                _byteTable.Rows.Add(r);
            }
            dataGridView1.DataSource = _byteTable;
            UiTheme.SetGridHeaders(dataGridView1, ("SignalName", "字节"), ("Value", "数值"), ("RawValue", "原始值"));
            dataGridView1.Refresh();
        }

        /// <summary>退出字节编辑模式（切到DBC信号表前调用）</summary>
        private void ExitByteEditorMode()
        {
            _byteEditorMode = false;
            _byteEditorMsg = null;
        }

        /// <summary>切换上方信号表到指定报文（每个报文的信号编辑状态独立缓存）</summary>
        private void SwitchSignalTable(int msgIndex)
        {
            ExitByteEditorMode(); // 切到DBC信号表，退出字节编辑模式
            if (msgIndex < 0 || msgIndex >= BaseParamter.dbcHelper.dbcFile.messages.Count) return;
            if (SelectMessageIndex == msgIndex && dataTable.Rows.Count > 0) return; // 已是当前报文，避免覆盖编辑状态

            if (dataTable.Rows.Count > 0 && SelectMessageIndex >= 0 &&
                SelectMessageIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
            {
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = dataTable;
            }
            SelectMessageIndex = msgIndex;
            if (BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable == null)
            {
                var t = new DataTable();
                t.Columns.Add("SignalName", typeof(string));
                t.Columns.Add("Value", typeof(string));
                t.Columns.Add("RawValue", typeof(string));
                BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable = t;
            }
            dataTable = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].dataTable;
            UpdateDbcListview(SelectMessageIndex);
        }

        /// <summary>
        /// 重建发送列表（messagesTable→dataGridView2）：汇总全工程原先4处重复刷表块。
        /// 使能报文注册到周期调度器；Enable为bool复选框列。
        /// </summary>
        private void RebuildMessagesTable()
        {
            // 重建前暂存各报文的手动发送次数，重建后恢复（key=名称+来源通道，多通道同名区分）
            // 注：TxChannel存在Message对象上，Rebuild不清对象无需恢复（切勿用行显示值回写，否则"自动"被固化）
            var manualCntMap = new Dictionary<string, string>();
            foreach (DataRow r in messagesTable.Rows)
            {
                if (r["MessageName"] is string n)
                {
                    manualCntMap[n + "|" + r["SrcChannel"]?.ToString()] = r["ManualSendCnt"]?.ToString();
                }
            }
            messagesTable.Rows.Clear();
            foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
            {
                if (message.sendFalg)
                {
                    byte srcCh = BaseParamter.GetChannelOfMessage(message);
                    string mapKey = message.messageName + "|" + srcCh;
                    var newRow = messagesTable.NewRow();
                    newRow["MessageID"] = "0x" + message.messgeId.ToString("X2");
                    newRow["MessageName"] = message.messageName;
                    newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                    newRow["SendCnt"] = message.sendCnt.ToString();
                    newRow["Enable"] = message.enableFlag;
                    newRow["RawData"] = FormatSendBufHex(message);
                    newRow["SrcChannel"] = srcCh.ToString();
                    // 发送通道列：显示实际生效值（TxChannel=0自动时显示来源通道）
                    newRow["TxChannel"] = ResolveTxChannel(message).ToString();
                    newRow["ManualSendCnt"] = manualCntMap.TryGetValue(mapKey, out string mc) ? mc : "1";
                    messagesTable.Rows.Add(newRow);
                    if (message.enableFlag)
                    {
                        multiMessageCANScheduler.AddMessage(message, message.cycleTime);
                    }
                }
                message.NextSendTime = 0;
            }
            // 自定义报文入表（与DBC报文共用全部列；CustomIdx指向customMessagesList索引）
            for (int ci = 0; ci < customMessagesList.Count; ci++)
            {
                var message = customMessagesList[ci];
                string mapKey = "自定义_0x" + message.messgeId.ToString("X3") + "|" + message.TxChannel;
                var newRow = messagesTable.NewRow();
                newRow["MessageID"] = "0x" + message.messgeId.ToString("X3");
                newRow["MessageName"] = "自定义_0x" + message.messgeId.ToString("X3");
                newRow["CycleTime(ms)"] = message.cycleTime.ToString();
                newRow["SendCnt"] = message.sendCnt.ToString();
                newRow["Enable"] = message.enableFlag;
                newRow["RawData"] = FormatSendBufHex(message);
                newRow["SrcChannel"] = message.TxChannel.ToString();
                newRow["TxChannel"] = ResolveTxChannel(message).ToString();
                newRow["ManualSendCnt"] = manualCntMap.TryGetValue(mapKey, out string mc) ? mc : "1";
                newRow["IsCustom"] = true;
                newRow["CustomIdx"] = ci;
                messagesTable.Rows.Add(newRow);
                if (message.enableFlag)
                {
                    multiMessageCANScheduler.AddMessageCustom(ref message, message.cycleTime);
                }
            }
            dataGridView2.DataSource = messagesTable;
            EnsureSingleSendColumn();
            EnsureRemoveColumn();
            // 列宽按内容分配：周期/使能/发送通道/手动次数/手动发送窄，宽度留给数据列
            SetColumnFill("MessageID", 55, false); // 报文ID可编辑（自定义报文改ID，DBC报文编辑会被拒绝回滚）
            SetColumnFill("MessageName", 130, true);
            SetColumnFill("CycleTime(ms)", 80, false);
            SetColumnFill("SendCnt", 80, true);
            SetColumnFill("Enable", 50, true);
            SetColumnFill("RawData", 190, false);
            SetColumnFill("TxChannel", 80, false);
            SetColumnFill("ManualSendCnt", 80, false);
            SetColumnFill("colSingleSend", 80, true);
            // 隐藏来源通道列（仅用于多通道同名报文区分）
            if (dataGridView2.Columns["SrcChannel"] != null)
                dataGridView2.Columns["SrcChannel"].Visible = false;
            // 隐藏自定义报文路由辅助列（仅内部用，不对用户显示）
            if (dataGridView2.Columns["IsCustom"] != null)
                dataGridView2.Columns["IsCustom"].Visible = false;
            if (dataGridView2.Columns["CustomIdx"] != null)
                dataGridView2.Columns["CustomIdx"].Visible = false;
            // 显示顺序：发送通道/手动次数紧随RawData，手动发送按钮列最后
            if (dataGridView2.Columns["colRemove"] != null)
                dataGridView2.Columns["colRemove"].DisplayIndex = 0;
            if (dataGridView2.Columns["TxChannel"] != null)
                dataGridView2.Columns["TxChannel"].DisplayIndex = 6;
            if (dataGridView2.Columns["ManualSendCnt"] != null)
                dataGridView2.Columns["ManualSendCnt"].DisplayIndex = 7;
            if (dataGridView2.Columns["colSingleSend"] != null)
                dataGridView2.Columns["colSingleSend"].DisplayIndex = 8;
            dataGridView2.Refresh();
            // 列重建后强制重算铺满（可用宽度未变时也需重算：列被重建宽度归位）
            AutoFillFlexibleColumns(true);
        }

        /// <summary>窗口宽度变化时，"报文名称"与"数据"两列自动伸缩铺满（报文名称130~200先长，其余给数据），其余列固定不联动</summary>
        private int _lastFillClientWidth = -1;
        private void AutoFillFlexibleColumns(bool force = false)
        {
            if (dataGridView2 == null || dataGridView2.IsDisposed) return;
            var colName = dataGridView2.Columns["MessageName"];
            var colRaw = dataGridView2.Columns["RawData"];
            if (colName == null || colRaw == null) return;
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int rowHeaderWidth = dataGridView2.RowHeadersVisible ? dataGridView2.RowHeadersWidth : 0;
            int availableWidth = dataGridView2.ClientSize.Width - scrollBarWidth - rowHeaderWidth - 4;
            if (availableWidth < 50) availableWidth = 50;
            // 可用宽度未变化且非强制时跳过，保留用户对这两列的手动调整（仅窗口/滚动条变化时才重新铺满）
            if (!force && availableWidth == _lastFillClientWidth) return;
            _lastFillClientWidth = availableWidth;

            int sumFixed = 0;
            foreach (DataGridViewColumn col in dataGridView2.Columns)
            {
                if (col != colName && col != colRaw && col.Visible) sumFixed += col.Width;
            }
            const int nameBase = 130, nameMax = 200, rawMin = 80;
            int remaining = availableWidth - sumFixed;
            int nameWidth, rawWidth;
            if (remaining <= nameBase + rawMin)
            {
                // 空间不足以同时满足基准：数据列保底80，报文名称取剩余（下限40）
                rawWidth = rawMin;
                nameWidth = remaining - rawMin;
                if (nameWidth < 40) nameWidth = 40;
            }
            else
            {
                // 富余空间先喂给报文名称直到200，再全部给数据列
                int extra = remaining - (nameBase + rawMin);
                int nameAdd = Math.Min(nameMax - nameBase, extra);
                nameWidth = nameBase + nameAdd;
                rawWidth = remaining - nameWidth;
            }
            colName.Width = nameWidth;
            colRaw.Width = rawWidth;
        }

        private void SetColumnFill(string colName, float weight, bool readOnly)
        {
            var col = dataGridView2.Columns[colName];
            if (col == null) return;
            col.Width = (int)weight; // None模式下按实际像素宽度显示，列宽独立不联动
            col.FillWeight = weight;
            col.ReadOnly = readOnly;
        }

        /// <summary>信号值变更后立即编码sendBuf并刷新发送列表RawData显示（不影响发送：发送时updateFlag仍会重编码并逐帧算CRC/RollingCounter）</summary>
        private void RefreshRawDataPreview(int msgIndex)
        {
            var msg = BaseParamter.dbcHelper.dbcFile.messages[msgIndex];
            try { msg.sendBuf = CAN_Data.CanMessageBuilder.EncodeSignals(msg.signals, Math.Max(8, (int)msg.messageSize)); }
            catch { }
            foreach (DataRow row in messagesTable.Rows)
            {
                if (row["MessageName"]?.ToString() == msg.messageName)
                {
                    row.SetField("RawData", FormatSendBufHex(msg));
                    break;
                }
            }
        }

        /// <summary>发送原始数据的16进制表示（字节间空格）；未编码过时按信号值实时编码预览</summary>
        private static string FormatSendBufHex(CAN_Data.Message msg)
        {
            byte[] buf = msg.sendBuf;
            if (buf == null)
            {
                try { buf = CAN_Data.CanMessageBuilder.EncodeSignals(msg.signals, Math.Max(8, (int)msg.messageSize)); }
                catch { return ""; }
            }
            return string.Join(" ", buf.Select(b => b.ToString("X2")));
        }

        private void CanSend_Load(object sender, EventArgs e)
        {
            treeView1.Nodes.Add("Message");
            // 通道下拉框按通道配置填充（设置选中项会触发树刷新并自动选中首条报文）
            RefreshChannelCombo();
            if (BaseParamter.dbcHelper.dbcFile.messages.Count != 0)
            {
                UpdateDbcTreeview();
            }

            RebuildMessagesTable();
            aliveFlag = true;


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
                    RebuildMessagesTable();
                    UpdateDbcListview(SelectMessageIndex);
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

        /// <summary>按通道配置（BusChannels）填充CAN通道下拉框</summary>
        private void RefreshChannelCombo()
        {
            _cmbChannel.Items.Clear();
            if (BaseParamter.BusChannels.Count == 0)
            {
                _cmbChannel.Items.Add("CH1");
            }
            else
            {
                for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
                {
                    string name = BaseParamter.BusChannels[i].Name;
                    _cmbChannel.Items.Add(string.IsNullOrEmpty(name) ? $"CH{i + 1}" : $"CH{i + 1} {name}");
                }
            }
            if (_selectedChannelIdx >= _cmbChannel.Items.Count)
            {
                _selectedChannelIdx = 0;
            }
            _cmbChannel.SelectedIndex = _selectedChannelIdx;
        }

        /// <summary>当前选中通道的DBC报文列表（无通道配置时回退聚合视图；通道未配置DBC时为空）</summary>
        private List<CAN_Data.Message> GetCurrentMessages()
        {
            if (BaseParamter.BusChannels.Count == 0)
            {
                return BaseParamter.dbcHelper.dbcFile.messages;
            }
            var dbc = BaseParamter.GetDbcHelperByChannel((byte)(_selectedChannelIdx + 1));
            if (dbc == null)
            {
                return new List<CAN_Data.Message>();
            }
            return dbc.dbcFile.messages;
        }

        private void _cmbChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            _selectedChannelIdx = _cmbChannel.SelectedIndex;
            SelectMessageIndex = -1; /* 跨通道后索引失效，等待重新选择 */
            dataTable.Rows.Clear();
            dataGridView1.Refresh();
            UpdateDbcTreeview();
            if (treeView1.Nodes.Count > 0 && treeView1.Nodes[0].Nodes.Count > 0)
            {
                treeView1.SelectedNode = treeView1.Nodes[0].Nodes[0];
            }
        }

        private void _txtMsgFilter_TextChanged(object sender, EventArgs e)
        {
            IdFilterRule.Parse(_txtMsgFilter.Text, _filterIds, _filterWildcards);
            // 所有输入项同时作为报文名子串匹配项
            _filterNameTokens.Clear();
            foreach (string part in _txtMsgFilter.Text.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                _filterNameTokens.Add(part.Trim());
            }
            UpdateDbcTreeview();
        }

        /// <summary>报文是否通过筛选：ID（精确/通配符）或报文名子串（不区分大小写），任一命中即通过；无条件时全部通过</summary>
        private bool PassesMsgFilter(CAN_Data.Message m)
        {
            bool hasIdRule = _filterIds.Count > 0 || _filterWildcards.Count > 0;
            if (!hasIdRule && _filterNameTokens.Count == 0)
            {
                return true;
            }
            if (hasIdRule && IdFilterRule.Match(m.messgeId, _filterIds, _filterWildcards))
            {
                return true;
            }
            foreach (var t in _filterNameTokens)
            {
                if (m.messageName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal void UpdateDbcTreeview()
        {
            if (treeView1.Nodes.Count == 0)
            {
                return;
            }
            treeView1.Nodes[0].Nodes.Clear();

            // 树只列当前选中通道的DBC报文，并按ID筛选框过滤；Tag存聚合视图索引（多通道同名报文区分用）
            var aggregate = BaseParamter.dbcHelper.dbcFile.messages;
            foreach (var m in GetCurrentMessages())
            {
                if (!PassesMsgFilter(m))
                {
                    continue;
                }
                int aggIdx = aggregate.IndexOf(m);
                if (aggIdx < 0)
                {
                    continue;
                }
                string text = m.sendFalg ? m.messageName + "(发送)" : m.messageName;
                var node = treeView1.Nodes[0].Nodes.Add(text);
                node.Tag = aggIdx;
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
                    // 原始值按(long)截断取整并以0x前缀16进制显示，与EncodeSignals实际编码值一致（所见即所发）
                    newRow["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(signal.cmdValue, signal)).ToString("X");

                    dataTable.Rows.Add(newRow);
                }

                dataGridView1.DataSource = dataTable;

                // 重新绑定后列被重建，需恢复中文列头（列名保持英文键不变）
                UiTheme.SetGridHeaders(dataGridView1,
                    ("SignalName", "信号名"), ("Value", "物理值"), ("RawValue", "原始值"));

                dataGridView1.Refresh();
            }catch (Exception ex) { }

        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if ((e.Node.Parent != null) && (e.Node.Parent.Text == "Message") && (e.Node.Tag is int))
            {
                SwitchSignalTable((int)e.Node.Tag);
            }
        }

        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            var node = treeView1.SelectedNode;
            if (node == null || node.Parent == null || node.Parent.Text != "Message" || !(node.Tag is int))
            {
                return;
            }
            int aggIdx = (int)node.Tag;
            var messages = BaseParamter.dbcHelper.dbcFile.messages;
            if (aggIdx < 0 || aggIdx >= messages.Count)
            {
                return;
            }
            var msg = messages[aggIdx];
            if (msg.sendFalg)
            {
                msg.sendFalg = false;
                multiMessageCANScheduler.RemoveMessage(ResolveTxChannel(msg), msg.messgeId);
                node.Text = msg.messageName;
            }
            else
            {
                msg.sendFalg = true;
                // 添加到发送列表默认不勾选使能（不注册周期调度），由用户勾选Enable后才启动周期发送
                msg.enableFlag = false;
                msg.NextSendTime = 0;
                node.Text = msg.messageName + "(发送)";
            }
            tabControl1.SelectedIndex = 0;

            RebuildMessagesTable();
        }

        /// <summary>按(名称,来源通道)在聚合视图中定位报文（多通道同名报文区分）</summary>
        private static CAN_Data.Message FindMessageByRow(DataRow row)
        {
            // 自定义报文行：按CustomIdx直接取customMessagesList
            if (row.Table.Columns.Contains("IsCustom") && row.Field<bool>("IsCustom"))
            {
                int idx = row.Field<int>("CustomIdx");
                if (idx >= 0 && idx < customMessagesList.Count) return customMessagesList[idx];
                return null;
            }
            string name = row["MessageName"]?.ToString();
            string srcCh = row["SrcChannel"]?.ToString();
            foreach (var m in BaseParamter.dbcHelper.dbcFile.messages)
            {
                if (m.messageName == name &&
                    (string.IsNullOrEmpty(srcCh) || BaseParamter.GetChannelOfMessage(m).ToString() == srcCh))
                    return m;
            }
            return null;
        }

        private void dataGridView2_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || e.RowIndex >= messagesTable.Rows.Count) return;
                DataRow row = messagesTable.Rows[e.RowIndex];
                string colName = dataGridView2.Columns[e.ColumnIndex].Name;
                string cellValue = dataGridView2.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
                var msg = FindMessageByRow(row);
                if (msg == null) return;
                bool isCustom = row.Table.Columns.Contains("IsCustom") && row.Field<bool>("IsCustom");

                if (colName == "MessageID")
                {
                    // 报文ID编辑：仅自定义报文允许（DBC报文ID来自DBC文件，改了会脱离信号定义/接收解析/调度映射）
                    if (!isCustom)
                    {
                        MessageBox.Show("DBC报文的ID来自DBC文件，不可修改", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("MessageID", "0x" + msg.messgeId.ToString("X3"));
                        return;
                    }
                    string idStr = cellValue.Trim();
                    if (idStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) idStr = idStr.Substring(2);
                    if (!uint.TryParse(idStr, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out uint newId) || newId == 0)
                    {
                        MessageBox.Show($"无效的报文ID: {cellValue}\n格式示例: 0x100（十六进制，非0）", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("MessageID", "0x" + msg.messgeId.ToString("X3"));
                        return;
                    }
                    if (newId != msg.messgeId)
                    {
                        // 旧调度器按旧ID/通道移除，更新ID后使能中的重新注册
                        byte ch = ResolveTxChannel(msg);
                        multiMessageCANScheduler.RemoveMessageCustom(ch, msg.messgeId);
                        msg.messgeId = newId;
                        row.SetField("MessageID", "0x" + newId.ToString("X3"));
                        row.SetField("MessageName", "自定义_0x" + newId.ToString("X3"));
                        if (msg.enableFlag) multiMessageCANScheduler.AddMessageCustom(ref msg, msg.cycleTime);
                    }
                    return;
                }

                if (colName == "CycleTime(ms)")
                {
                    // 周期编辑：合法则更新报文与调度器，非法回滚
                    if (int.TryParse(cellValue, out int cycleTime))
                    {
                        cycleTime = (cycleTime <= 0) ? 1 : cycleTime;
                        row.SetField("CycleTime(ms)", cycleTime);
                        msg.cycleTime = (uint)cycleTime;
                        multiMessageCANScheduler.UpdateMessageInterval(ResolveTxChannel(msg), msg.messgeId, (uint)cycleTime);
                    }
                    else
                    {
                        MessageBox.Show($"无效的周期数值: {cellValue}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("CycleTime(ms)", msg.cycleTime.ToString());
                    }
                }
                else if (colName == "TxChannel")
                {
                    // 发送通道编辑：0=自动跟随来源DBC通道，1~16=固定逻辑通道；非法回滚
                    if (int.TryParse(cellValue, out int txCh) && txCh >= 0 && txCh <= 16)
                    {
                        byte oldCh = ResolveTxChannel(msg);
                        msg.TxChannel = (byte)txCh;
                        // 调度器按新通道重新注册（使能中的报文）
                        if (msg.enableFlag)
                        {
                            multiMessageCANScheduler.RemoveMessage(oldCh, msg.messgeId);
                            multiMessageCANScheduler.AddMessage(msg, msg.cycleTime);
                        }
                        row.SetField("TxChannel", ResolveTxChannel(msg).ToString());
                    }
                    else
                    {
                        MessageBox.Show($"无效的发送通道: {cellValue}\n0=自动（跟随报文所属DBC通道），1~16=固定通道", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("TxChannel", ResolveTxChannel(msg).ToString());
                    }
                }
                else if (colName == "ManualSendCnt")
                {
                    // 手动发送次数：>=1的整数，非法回滚为1
                    if (int.TryParse(cellValue, out int manualCnt) && manualCnt >= 1)
                    {
                        row.SetField("ManualSendCnt", manualCnt.ToString());
                    }
                    else
                    {
                        MessageBox.Show($"无效的发送次数: {cellValue}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("ManualSendCnt", "1");
                    }
                }
                else if (colName == "RawData")
                {
                    // 原始数据hex编辑：空格分隔的1~报文字节数个字节（可带0x前缀），非法回滚
                    if (TryParseHexBytes(cellValue, msg.sendBuf, out byte[] newBuf))
                    {
                        msg.sendBuf = newBuf;
                        // 手动改原始数据后不再用信号编码覆盖（CRC/RollingCounter仍逐帧重算）
                        msg.updateFlag = false;
                        row.SetField("RawData", FormatSendBufHex(msg));
                        // 解析区双向同步：当前行正选中时，把RawData改动体现到解析区
                        if (e.RowIndex == _selectedDgv2Row)
                        {
                            if (_byteEditorMode && _byteEditorMsg == msg)
                            {
                                ShowByteEditor(msg); // 自定义报文：重载字节编辑器
                            }
                            else if (SelectMessageIndex >= 0 && SelectMessageIndex < BaseParamter.dbcHelper.dbcFile.messages.Count &&
                                     BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex] == msg)
                            {
                                SyncSignalsFromRawData(msg); // DBC报文：反解码写回各信号cmdValue
                                UpdateDbcListview(SelectMessageIndex);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"无效的16进制数据: {cellValue}\n格式示例: 11 22 33 44 55 66 77 88（不超过报文字节数）", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        row.SetField("RawData", FormatSendBufHex(msg));
                    }
                }
            }
            catch { }
        }

        /// <summary>解析空格分隔的hex字节串（1~报文字节数个，可带0x前缀）；baseBuf非空时未提供的字节沿用原值</summary>
        private static bool TryParseHexBytes(string text, byte[] baseBuf, out byte[] result)
        {
            // 容量跟随报文实际编码长度（经典CAN为8，CAN FD长报文可达64）
            int capacity = Math.Max(8, baseBuf?.Length ?? 8);
            result = new byte[capacity];
            if (baseBuf != null) Array.Copy(baseBuf, result, Math.Min(baseBuf.Length, capacity));

            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > capacity) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i];
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                if (t.Length == 0 || t.Length > 2) return false;
                if (!byte.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out byte b))
                    return false;
                result[i] = b;
            }
            return true;
        }

        /// <summary>RawData编辑后把sendBuf反解码写回各信号cmdValue（DBC报文解析区同步用）</summary>
        private static void SyncSignalsFromRawData(CAN_Data.Message msg)
        {
            if (msg?.sendBuf == null || msg.signals == null || msg.signals.Count == 0) return;
            try
            {
                var results = new CanSignalParser().ParseSignals(msg.sendBuf, msg.signals);
                foreach (var sig in msg.signals)
                {
                    if (results.TryGetValue(sig.signalName, out double v))
                        sig.cmdValue = v;
                }
            }
            catch { /* 解析失败忽略，保留原cmdValue */ }
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

            // 字节编辑模式：解析0xXX写入sendBuf对应字节，并刷新发送列表RawData单元格
            if (_byteEditorMode && _byteEditorMsg != null)
            {
                var byteStr = dataGridView1.Rows[e.RowIndex].Cells[1].Value?.ToString().Trim() ?? "";
                if (byteStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) byteStr = byteStr.Substring(2);
                if (byte.TryParse(byteStr, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out byte b))
                {
                    // 确保sendBuf容量足够
                    if (_byteEditorMsg.sendBuf == null) _byteEditorMsg.sendBuf = new byte[Math.Max(8, e.RowIndex + 1)];
                    if (_byteEditorMsg.sendBuf.Length <= e.RowIndex)
                    {
                        var grown = new byte[Math.Max(_byteEditorMsg.sendBuf.Length * 2, e.RowIndex + 1)];
                        if (grown.Length < 8) grown = new byte[8];
                        Array.Copy(_byteEditorMsg.sendBuf, grown, _byteEditorMsg.sendBuf.Length);
                        _byteEditorMsg.sendBuf = grown;
                    }
                    _byteEditorMsg.sendBuf[e.RowIndex] = b;
                    _byteEditorMsg.updateFlag = false; // 字节编辑后不再用信号编码覆盖
                    _byteTable.Rows[e.RowIndex]["Value"] = $"0x{b:X2}";
                    _byteTable.Rows[e.RowIndex]["RawValue"] = $"0x{b:X2}";
                    // 同步发送列表该行RawData单元格
                    if (_selectedDgv2Row >= 0 && _selectedDgv2Row < messagesTable.Rows.Count)
                    {
                        messagesTable.Rows[_selectedDgv2Row].SetField("RawData", FormatSendBufHex(_byteEditorMsg));
                    }
                }
                else
                {
                    // 非法输入回滚
                    byte cur = (_byteEditorMsg.sendBuf != null && e.RowIndex < _byteEditorMsg.sendBuf.Length) ? _byteEditorMsg.sendBuf[e.RowIndex] : (byte)0;
                    dataGridView1.Rows[e.RowIndex].Cells[1].Value = $"0x{cur:X2}";
                }
                return;
            }

            var currentValue = dataGridView1.Rows[e.RowIndex].Cells[1].Value?.ToString();
            var signal = BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].signals[e.RowIndex];

            if (signal.enumDefinitions == null || signal.enumDefinitions.Count == 0)
            {
                // 非枚举类型，验证数值格式
                if (double.TryParse(currentValue, out double num))
                {
                    // 范围校验：超出DBC定义的最小/最大值则拒绝并回滚
                    if (num < signal.minimum || num > signal.maximum)
                    {
                        MessageBox.Show($"输入值 {num} 超出信号范围 [{signal.minimum}, {signal.maximum}]",
                            "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        dataGridView1.Rows[e.RowIndex].Cells[1].Value = dataTable.Rows[e.RowIndex]["Value"];
                        return;
                    }
                    // 更新信号值和RawValue
                    signal.cmdValue = num;
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].updateFlag = true;
                    dataTable.Rows[e.RowIndex]["Value"] = num.ToString("F1");
                    // 原始值按(long)截断取整并以0x前缀16进制显示，与EncodeSignals实际编码值一致（所见即所发）
                    dataTable.Rows[e.RowIndex]["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(num, signal)).ToString("X");
                    RefreshRawDataPreview(SelectMessageIndex); // 原始数据列实时跟随信号值
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

                // 字节编辑模式无枚举，直接返回（仍保持可编辑文本输入）
                if (_byteEditorMode) return;

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
                    // 原始值按(long)截断取整并以0x前缀16进制显示，与EncodeSignals实际编码值一致（所见即所发）
                    dataTable.Rows[currentRowIndex]["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(selected.Key, signal)).ToString("X");
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].signals[currentRowIndex].cmdValue = selected.Key;
                    BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex].updateFlag = true;
                    RefreshRawDataPreview(SelectMessageIndex); // 原始数据列实时跟随信号值

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
            public List<CustomMessageCfg> CustomMessages { get; set; } // 自定义报文（持久化，关窗不丢）
        }

        /// <summary>自定义报文的持久化形式</summary>
        [Serializable]
        public class CustomMessageCfg
        {
            public uint Id { get; set; }
            public uint CycleMs { get; set; }
            public byte[] Data { get; set; }
            public bool CycleEnabled { get; set; }
            public byte TxChannel { get; set; } // 逻辑发送通道（0=自动/通道1，兼容旧配置）
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
                DbcData = BaseParamter.dbcHelper.dbcFile, // 直接保存内存中的DBC对象
                CustomMessages = customMessagesList.Select(m => new CustomMessageCfg
                {
                    Id = m.messgeId,
                    CycleMs = m.cycleTime,
                    Data = m.sendBuf?.ToArray(),
                    CycleEnabled = m.enableFlag,
                    TxChannel = m.TxChannel
                }).ToList()
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

                // 按报文ID把配置中的发送设置应用到当前通道DBC聚合视图（多通道同ID全部恢复）
                if (config.DbcData?.messages != null)
                {
                    foreach (var cfgMsg in config.DbcData.messages)
                    {
                        foreach (var curMsg in BaseParamter.dbcHelper.dbcFile.messages.Where(m => m.messgeId == cfgMsg.messgeId))
                        {
                            curMsg.sendFalg = cfgMsg.sendFalg;
                            curMsg.enableFlag = cfgMsg.enableFlag;
                            curMsg.cycleTime = cfgMsg.cycleTime;
                            curMsg.TxChannel = cfgMsg.TxChannel; // 旧配置无此字段=0=自动跟随来源通道
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
                }

                SelectMessageIndex = (config.SelectedMessageIndex >= 0 &&
                    config.SelectedMessageIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
                    ? config.SelectedMessageIndex : 0;

                // 恢复自定义报文（旧格式配置无此字段时跳过；行入表由RebuildMessagesTable统一完成）
                customMessagesList.Clear();
                if (config.CustomMessages != null)
                {
                    foreach (var c in config.CustomMessages)
                    {
                        var buf = new byte[8];
                        if (c.Data != null) Array.Copy(c.Data, buf, Math.Min(c.Data.Length, 8));
                        customMessagesList.Add(new CAN_Data.Message
                        {
                            messgeId = c.Id,
                            cycleTime = c.CycleMs,
                            sendBuf = buf,
                            sendFalg = true,
                            enableFlag = c.CycleEnabled, // RebuildMessagesTable中按此状态注册调度
                            TxChannel = c.TxChannel // 旧配置无此字段=0=通道1
                        });
                    }
                }

                // 刷新UI
                UpdateDbcTreeview();
                RebuildMessagesTable();
                UpdateDbcListview(SelectMessageIndex);
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
                // 更新DBC报文的发送计数与原始数据hex（发送后含最新CRC/RollingCounter）
                int index = 0;
                foreach (DataRow row in messagesTable.Rows)
                {
                    var msg = FindMessageByRow(row);
                    if (msg != null)
                    {
                        // 直接比较数值而不是字符串，避免不必要的更新
                        if (msg.sendCnt.ToString() != row["SendCnt"]?.ToString())
                        {
                            // 必须按列名索引：colSingleSend按钮列占位Index 0（句柄创建前Add导致），
                            // 用枚举硬编码索引会错位写到CycleTime(ms)列
                            dataGridView2.Rows[index].Cells["SendCnt"].Value = msg.sendCnt.ToString();
                        }
                        // RawData独立检测：sendCnt不变时（如仅编辑信号值）也能及时跟随sendBuf刷新
                        string hex = FormatSendBufHex(msg);
                        if (row["RawData"]?.ToString() != hex)
                        {
                            dataGridView2.Rows[index].Cells["RawData"].Value = hex;
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

        public class MultiMessageCANScheduler : IDisposable
        {
            private class CANMessageSchedule : IComparable<CANMessageSchedule>
            {
                public CAN_Data.Message Message { get; set; }
                public long IntervalMs { get; set; }
                public long NextTriggerTime { get; set; }
                public uint CanId => Message.messgeId;
                /// <summary>实际发送通道（注册/重建调度时解析缓存，避免排序时反复遍历通道配置）</summary>
                public byte Channel { get; set; }
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

                        // 触发时间相同，按(通道,CanId)做次要排序（多通道同ID可共存）
                        int chCompare = x.Channel.CompareTo(y.Channel);
                        if (chCompare != 0) return chCompare;
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
            // key为复合键 MsgKey(canId, channel)：多通道同ID报文各自独立调度
            private readonly Dictionary<long, CANMessageSchedule> _scheduleLookup;
            private readonly Dictionary<long, CANMessageSchedule> _scheduleLookupCustom;
            private readonly List<CAN_Data.Message> _sendBuffer;
            private readonly List<CAN_Data.Message> _sendBufferCustom;
            private readonly object _lock = new object();
            private Stopwatch stopwatch = new Stopwatch();

            public MultiMessageCANScheduler()
            {
                _priorityQueue = new CANMessageScheduleQueue();
                _scheduleLookup = new Dictionary<long, CANMessageSchedule>();
                _sendBuffer = new List<CAN_Data.Message>();

                _priorityQueueCustom = new CANMessageScheduleQueue();
                _scheduleLookupCustom = new Dictionary<long, CANMessageSchedule>();
                _sendBufferCustom = new List<CAN_Data.Message>();
            }

            /// <summary>注册DBC报文周期调度（直接传Message引用，避免多通道同ID时GetMessageById歧义）</summary>
            public void AddMessage(CAN_Data.Message msg, uint intervalMs)
            {
                if (msg == null) return;
                intervalMs = (intervalMs <= 0) ? 1 : intervalMs;
                byte ch = msg.TxChannel > 0 ? msg.TxChannel : BaseParamter.GetChannelOfMessage(msg);
                long key = Main.MsgKey(msg.messgeId, ch);
                lock (_lock)
                {
                    var schedule = new CANMessageSchedule
                    {
                        Message = msg,
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime(),
                        Channel = ch
                    };
                    if (!_scheduleLookup.ContainsKey(key))
                    {
                        _scheduleLookup[key] = schedule;
                        _priorityQueue.Enqueue(schedule);
                    }
                }
            }

            public void AddMessageCustom(ref CAN_Data.Message msg, uint intervalMs)
            {
                intervalMs = (intervalMs <= 0) ? 1 : intervalMs;
                byte ch = msg.TxChannel > 0 ? msg.TxChannel : (byte)1;
                long key = Main.MsgKey(msg.messgeId, ch);
                lock (_lock)
                {
                    var schedule = new CANMessageSchedule
                    {
                        Message = msg,
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime(),
                        Channel = ch
                    };
                    if (null != schedule.Message)
                    {
                        if (!_scheduleLookupCustom.ContainsKey(key))
                        {
                            _scheduleLookupCustom[key] = schedule;
                            _priorityQueueCustom.Enqueue(schedule);
                        }
                    }
                }
            }

            public void RemoveMessage(byte channel, uint canId)
            {
                long key = Main.MsgKey(canId, channel);
                lock (_lock)
                {
                    if (_scheduleLookup.ContainsKey(key))
                    {
                        _scheduleLookup.Remove(key);
                    }
                }
            }
            public void RemoveMessageCustom(byte channel, uint canId)
            {
                long key = Main.MsgKey(canId, channel);
                lock (_lock)
                {
                    if (_scheduleLookupCustom.TryGetValue(key, out var schedule))
                    {
                        // 从队列中移除该调度项
                        _priorityQueueCustom.Remove(schedule);
                    }
                    if (_scheduleLookupCustom.ContainsKey(key))
                    {
                        _scheduleLookupCustom.Remove(key);
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
                        if (!_scheduleLookup.ContainsKey(Main.MsgKey(nextSchedule.CanId, nextSchedule.Channel)))
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
                        if (!_scheduleLookupCustom.ContainsKey(Main.MsgKey(nextSchedule.CanId, nextSchedule.Channel)))
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
                    // 批量发送所有到期的CAN报文（按各自TxChannel路由到对应物理通道）
                    foreach (var msg in _sendBuffer)
                    {
                        msg.NextSendTime = Main.canSend.sw.ElapsedMilliseconds + msg.cycleTime;
                        msg.aliveCount++;
                        msg.aliveCount &= 0x0F;
                        byte txCh = msg.TxChannel > 0 ? msg.TxChannel : BaseParamter.GetChannelOfMessage(msg);
                        BaseParamter.dbcHelper.SendCanMessage(msg.messgeId, txCh);
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
                        byte txCh = msg.TxChannel > 0 ? msg.TxChannel : (byte)1;
                        BaseParamter.dbcHelper.SendCanMessage(msg, txCh);
                    }
                    _sendBufferCustom.Clear();
                }
            }

            public void UpdateMessageInterval(byte channel, uint canId, uint newIntervalMs)
            {
                long key = Main.MsgKey(canId, channel);
                lock (_lock)
                {
                    if (_scheduleLookup.TryGetValue(key, out var schedule))
                    {
                        // 更新间隔时间
                        newIntervalMs = (newIntervalMs <= 0) ? 1 : newIntervalMs;
                        schedule.IntervalMs = newIntervalMs;

                        // 注意：由于优先队列的特性，我们无法直接更新队列中的元素
                        // 这个更新会在下次重新入队时生效
                    }
                }
            }

            public void UpdateMessageIntervalCustom(byte channel, uint canId, uint newIntervalMs)
            {
                long key = Main.MsgKey(canId, channel);
                lock (_lock)
                {
                    if (_scheduleLookupCustom.TryGetValue(key, out var schedule))
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

        /// <summary>确保发送列表存在"手动发送"按钮列（重新绑定DataSource后调用）</summary>
        private void EnsureSingleSendColumn()
        {
            if (dataGridView2.Columns["colSingleSend"] != null) return;
            var col = new DataGridViewButtonColumn
            {
                Name = "colSingleSend",
                HeaderText = "手动发送",
                Text = "发送",
                UseColumnTextForButtonValue = true,
                FillWeight = 55
            };
            dataGridView2.Columns.Add(col);
        }

        /// <summary>发送列表最左侧的移除列（×图标，点击将该报文移出发送列表）</summary>
        private void EnsureRemoveColumn()
        {
            if (dataGridView2.Columns["colRemove"] != null) return;
            var col = new DataGridViewImageColumn
            {
                Name = "colRemove",
                HeaderText = "",
                Image = ToolbarIcons.Get("clear"),
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                Width = 24,
                FillWeight = 24,
                ReadOnly = true,
                ToolTipText = "将该报文移出发送列表"
            };
            dataGridView2.Columns.Add(col);
            col.DisplayIndex = 0;
        }

    }
}
