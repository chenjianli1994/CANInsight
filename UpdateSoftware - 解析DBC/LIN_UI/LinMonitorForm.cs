using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PCAN_Client;
using PCAN_Client.LIN_API;
using PCAN_Client.util;

namespace PCAN_Client.LIN_UI
{
    /// <summary>
    /// LIN 监控主窗口：报文流（VirtualMode 表格）+ 调度表/从节点(发布)/信号/发送 四页签 + 状态栏
    /// 与 CAN 模块完全解耦：独立订阅 Lin_API 事件，独立调度引擎实例
    /// </summary>
    internal class LinMonitorForm : Form
    {
        // ==================== 控件 ====================
        private ToolStrip _toolStripLin;
        private ToolStripButton _btnLoadLdf, _btnStart, _btnPause, _btnClear, _btnWakeUp, _btnSleep;
        private ToolStripTextBox _txtFilter;
        private DataGridView _dgvFrames;
        private TabControl _tabLin;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _lblBus, _lblSched, _lblCount, _lblError, _lblBaud;

        // 调度表页签
        private DataGridView _dgvSlots;
        private ToolStrip _slotToolbar;

        // 从节点页签
        private DataGridView _dgvResp;
        /// <summary>从节点页签已展开（信号解析）的帧 PID 集合</summary>
        private readonly HashSet<byte> _expandedResp = new HashSet<byte>();

        // 信号页签
        private DataGridView _dgvSignals;

        // 发送页签（自定义报文定义表）
        private DataGridView _dgvSend;
        private ToolStrip _sendToolbar;

        // ==================== 数据 ====================
        private readonly List<LinFrameRecord> _frames = new List<LinFrameRecord>();
        private readonly List<int> _filtered = new List<int>();   // 过滤后行 → _frames 索引
        private long _errorCount;
        private volatile bool _paused;
        private byte _channel;          // 当前监控通道（逻辑号）
        private readonly Dictionary<byte, LinScheduler> _schedulers = new Dictionary<byte, LinScheduler>();
        private readonly Timer _uiTimer;
        private bool _disposed;

        private const int MaxFrames = 500000;

        public LinMonitorForm()
        {
            Text = "LIN 总线监控";
            Width = 1180;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            UiTheme.StyleForm(this);

            BuildToolStrip();
            BuildFrameGrid();
            BuildTabs();
            BuildStatusStrip();
            LayoutControls();

            _uiTimer = new Timer { Interval = 500 };
            _uiTimer.Tick += (s, e) =>
            {
                RefreshStatusBar();
                RefreshSignalValues(); // 信号页签实时解码刷新（与状态栏同节奏）
            };
            _uiTimer.Start();

            Lin_API.LinFrameReceived += OnFrameReceived;
            Lin_API.BusEvent += OnBusEvent;
            Lin_API.LinkLost += OnLinkLost;
            Lin_API.ChannelStateChanged += OnChannelStateChanged;

            InitChannelView();
            FormClosing += (s, e) =>
            {
                _disposed = true;
                _uiTimer.Stop();
                foreach (var sc in _schedulers.Values) sc.Suspend();
                Lin_API.LinFrameReceived -= OnFrameReceived;
                Lin_API.BusEvent -= OnBusEvent;
                Lin_API.LinkLost -= OnLinkLost;
                Lin_API.ChannelStateChanged -= OnChannelStateChanged;
            };
        }

        // ==================== 布局构建 ====================

        private void BuildToolStrip()
        {
            _toolStripLin = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            var btnChMgr = new ToolStripButton("通道管理", ToolbarIcons.Get("gear"));
            btnChMgr.Click += (s, e) =>
            {
                // 统一通道管理入口：打开 Main 的通道管理对话框并切到 LIN 页签（与 CAN 同窗，冲突可见）
                using (var dlg = new ChannelManagerForm(PCAN_Client.Main.main))
                {
                    dlg.SelectLinTab();
                    dlg.ShowDialog(this);
                }
                InitChannelView();
                RefreshStatusBar();
            };
            _btnLoadLdf = new ToolStripButton("加载 LDF", ToolbarIcons.Get("dbc"));
            _btnLoadLdf.Click += (s, e) => LoadLdf();
            _btnStart = new ToolStripButton("开始", ToolbarIcons.Get("play"));
            _btnStart.Click += (s, e) => { _paused = false; };
            _btnPause = new ToolStripButton("暂停", ToolbarIcons.Get("stop"));
            _btnPause.Click += (s, e) => { _paused = true; };
            _btnClear = new ToolStripButton("清空", ToolbarIcons.Get("clear"));
            _btnClear.Click += (s, e) => { _frames.Clear(); RebuildFilter(); };
            _txtFilter = new ToolStripTextBox { Width = 160, ToolTipText = "帧 ID 过滤（规则与 CAN 接收窗口一致）：精确 11 / 0x11；通配符 3*（匹配 0x30-0x3F）、*（全部）；逗号或空格分隔多个，如 11, 3*" };
            _txtFilter.TextChanged += (s, e) => RebuildFilter();
            _btnWakeUp = new ToolStripButton("唤醒", ToolbarIcons.Get("plus"));
            _btnWakeUp.Click += (s, e) => { if (!Lin_API.WakeUp(_channel)) ShowError("唤醒失败（未连接）"); };
            _btnSleep = new ToolStripButton("休眠", ToolbarIcons.Get("stop"));
            _btnSleep.Click += (s, e) => { if (!Lin_API.Sleep(_channel)) ShowError("休眠失败（未连接）"); };

            _toolStripLin.Items.Add(btnChMgr);
            _toolStripLin.Items.Add(_btnLoadLdf);
            _toolStripLin.Items.Add(new ToolStripSeparator());
            _toolStripLin.Items.Add(_btnStart);
            _toolStripLin.Items.Add(_btnPause);
            _toolStripLin.Items.Add(_btnClear);
            _toolStripLin.Items.Add(new ToolStripSeparator());
            _toolStripLin.Items.Add(new ToolStripLabel("PID 过滤:"));
            _toolStripLin.Items.Add(_txtFilter);
            _toolStripLin.Items.Add(new ToolStripSeparator());
            _toolStripLin.Items.Add(_btnWakeUp);
            _toolStripLin.Items.Add(_btnSleep);
            Controls.Add(_toolStripLin);
        }

        private void BuildFrameGrid()
        {
            _dgvFrames = new DataGridView
            {
                VirtualMode = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                Dock = DockStyle.Fill,
            };
            UiTheme.StyleGrid(_dgvFrames);
            _dgvFrames.DefaultCellStyle.Font = new Font("Consolas", 9f);
            _dgvFrames.DefaultCellStyle.ForeColor = Color.Black;
            // 表头/单元格边框风格与 CAN 报文接收窗口一致（单线分隔，表头清晰可辨）
            _dgvFrames.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _dgvFrames.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _dgvFrames.CellValueNeeded += DgvFrames_CellValueNeeded;
            _dgvFrames.CellFormatting += DgvFrames_CellFormatting;
            // 空态提示：未收到报文时说明此区域用途（连接后实时显示总线报文）；画在数据区（表头下方）
            _dgvFrames.Paint += (s, e) =>
            {
                if (_dgvFrames.RowCount > 0) return;
                var area = _dgvFrames.DisplayRectangle;
                TextRenderer.DrawText(e.Graphics,
                    "暂无报文 — 连接通道后，此处实时显示总线上的报文（时间/方向/ID/帧名称/数据）",
                    UiTheme.UiFont, area, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            _dgvFrames.Columns.Add("colTime", "时间(ms)");
            _dgvFrames.Columns["colTime"].Width = 90;
            _dgvFrames.Columns.Add("colCh", "通道");
            _dgvFrames.Columns["colCh"].Width = 52;
            _dgvFrames.Columns.Add("colDir", "方向");
            _dgvFrames.Columns["colDir"].Width = 48;
            _dgvFrames.Columns.Add("colId", "ID");
            _dgvFrames.Columns["colId"].Width = 50;
            _dgvFrames.Columns.Add("colName", "帧名称");
            _dgvFrames.Columns["colName"].Width = 220;
            _dgvFrames.Columns.Add("colType", "帧类型");
            _dgvFrames.Columns["colType"].Width = 90;
            _dgvFrames.Columns.Add("colDlc", "DLC");
            _dgvFrames.Columns["colDlc"].Width = 42;
            _dgvFrames.Columns.Add("colData", "数据 (Hex)");
            _dgvFrames.Columns["colData"].Width = 250;
            _dgvFrames.Columns.Add("colCs", "校验和");
            _dgvFrames.Columns["colCs"].Width = 62;
            _dgvFrames.Columns.Add("colStatus", "状态");
            _dgvFrames.Columns["colStatus"].Width = 110;
            Controls.Add(_dgvFrames);
        }

        private void BuildTabs()
        {
            _tabLin = new TabControl { Dock = DockStyle.Fill };
            _tabLin.TabPages.Add("调度表");
            _tabLin.TabPages.Add("从节点 / 发布");
            _tabLin.TabPages.Add("信号 (LDF)");
            _tabLin.TabPages.Add("发送");

            // ---- 调度表页签 ----
            var slotPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _slotToolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            _slotToolbar.Items.Add(new ToolStripButton("开始调度", ToolbarIcons.Get("play")) { Tag = "start", ToolTipText = "按勾选启用的帧循环发送 Header" });
            _slotToolbar.Items.Add(new ToolStripButton("暂停", ToolbarIcons.Get("stop")) { Tag = "suspend", ToolTipText = "暂停调度" });
            _slotToolbar.Items.Add(new ToolStripButton("单步", ToolbarIcons.Get("scroll")) { Tag = "step", ToolTipText = "发送选中帧 Header 一次（Vector 软件模式）" });
            _slotToolbar.Items.Add(new ToolStripSeparator());
            _slotToolbar.Items.Add(new ToolStripButton("从 LDF 导入", ToolbarIcons.Get("dbc")) { Tag = "import", ToolTipText = "按 LDF 调度表自动填充报文（帧ID/名称/时隙），默认全部勾选" });
            _slotToolbar.Items.Add(new ToolStripButton("添加帧槽", ToolbarIcons.Get("plus")) { Tag = "add", ToolTipText = "手动添加一条空帧槽（默认 PID 0x00、时隙 15ms）" });
            _slotToolbar.Items.Add(new ToolStripButton("删除", ToolbarIcons.Get("clear")) { Tag = "del", ToolTipText = "删除选中帧槽" });
            _slotToolbar.Items.Add(new ToolStripButton("上移", ToolbarIcons.Get("scroll")) { Tag = "up", ToolTipText = "选中帧上移（调度顺序提前）" });
            _slotToolbar.Items.Add(new ToolStripButton("下移", ToolbarIcons.Get("scroll")) { Tag = "down", ToolTipText = "选中帧下移（调度顺序延后）" });
            _slotToolbar.ItemClicked += SlotToolbar_ItemClicked;

            _dgvSlots = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            UiTheme.StyleGrid(_dgvSlots);
            var colEn = new DataGridViewCheckBoxColumn { Name = "colEn", HeaderText = "启用", Width = 46, ThreeState = false };
            _dgvSlots.Columns.Add(colEn);
            _dgvSlots.Columns.Add("colSlotId", "帧 ID");
            _dgvSlots.Columns["colSlotId"].Width = 80;
            _dgvSlots.Columns.Add("colSlotName", "帧名称");
            _dgvSlots.Columns["colSlotName"].Width = 240;
            _dgvSlots.Columns.Add("colSlotMs", "时隙 (ms)");
            _dgvSlots.Columns["colSlotMs"].Width = 90;
            _dgvSlots.Columns.Add("colSlotCnt", "周期数");
            _dgvSlots.Columns["colSlotCnt"].Width = 80;
            _dgvSlots.Columns["colSlotCnt"].ReadOnly = true;
            _dgvSlots.CellValueChanged += DgvSlots_CellValueChanged;
            _dgvSlots.CellFormatting += DgvSlots_CellFormatting;
            _dgvSlots.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvSlots.IsCurrentCellDirty) _dgvSlots.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            slotPanel.Controls.Add(_dgvSlots);
            var slotHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 24,
                Text = "勾选 = 该帧参与循环调度（默认全勾）；帧 ID = 报文 PID；时隙 = 两帧发送间隔(ms)；周期数 = 已发送次数(只读)。「从 LDF 导入」按 LDF 调度表自动填充",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            slotPanel.Controls.Add(slotHint);
            // 注意：Add 顺序决定 Dock 布局（逆 z-order 处理）——toolbar 必须最后 Add，
            // 否则 Dock=Top 布局在 Fill 之后处理会被压成 0 高（表头被挤到页面顶部）
            slotPanel.Controls.Add(_slotToolbar);
            _slotToolbar.Dock = DockStyle.Top;
            _tabLin.TabPages[0].Controls.Add(slotPanel);

            // ---- 从节点/发布页签 ----
            var respPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _dgvResp = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            UiTheme.StyleGrid(_dgvResp);
            var colRspExpand = new DataGridViewTextBoxColumn { Name = "colRspExpand", HeaderText = "", Width = 30, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            _dgvResp.Columns.Add(colRspExpand);
            // LDF 导入内容（ID/帧名/DLC）固定，仅「数据」列可编辑
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRspId", HeaderText = "响应 ID", Width = 80, ReadOnly = true });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRspName", HeaderText = "帧名称", Width = 240, ReadOnly = true });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRspDlc", HeaderText = "DLC/位", Width = 50, ReadOnly = true });
            _dgvResp.Columns.Add("colRspData", "数据 (Hex)");
            _dgvResp.Columns["colRspData"].Width = 250;
            _dgvResp.CellContentClick += DgvResp_CellContentClick;
            _dgvResp.CellValueChanged += DgvResp_CellValueChanged;
            _dgvResp.CellDoubleClick += DgvResp_CellDoubleClick;
            _dgvResp.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvResp.IsCurrentCellDirty) _dgvResp.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            // 信号行编辑时编辑框只显示数值（去掉单位后缀），单位不可编辑
            _dgvResp.EditingControlShowing += (s, e) =>
            {
                if (!(e.Control is DataGridViewTextBoxEditingControl ed)) return;
                var cur = _dgvResp.CurrentCell;
                if (cur == null) return;
                var row = _dgvResp.Rows[cur.RowIndex];
                if (!(row.Tag is Tuple<byte, string>)) return;
                var ldf2 = GetLdf();
                if (ldf2 == null) return;
                var sig = FindSignalDef(ldf2, ((Tuple<byte, string>)row.Tag).Item2);
                if (sig == null || sig.Unit.Length == 0) return;
                string txt = (cur.Value ?? "").ToString();
                if (txt.EndsWith(sig.Unit, StringComparison.OrdinalIgnoreCase))
                    txt = txt.Substring(0, txt.Length - sig.Unit.Length).Trim();
                ed.Text = txt;
            };
            respPanel.Controls.Add(_dgvResp);
            var respHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 40,
                Text = "作用：配置本机在总线上发布/应答的数据（LDF 导入后自动填充）。\n主节点模式 = 本机作为发送方发布帧数据（调度时发出）；从节点模式 = 本机按数据应答收到 Header 的帧。点「＋」或双击行展开信号（带单位）改物理值，调度表页签勾选决定哪些帧参与发送",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            respPanel.Controls.Add(respHint);
            _tabLin.TabPages[1].Controls.Add(respPanel);

            // ---- 信号页签 ----
            var sigPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _dgvSignals = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            UiTheme.StyleGrid(_dgvSignals);
            _dgvSignals.Columns.Add("colSigName", "信号名");
            _dgvSignals.Columns["colSigName"].Width = 200;
            _dgvSignals.Columns.Add("colSigFrame", "帧");
            _dgvSignals.Columns["colSigFrame"].Width = 70;
            _dgvSignals.Columns.Add("colSigStart", "起始位");
            _dgvSignals.Columns["colSigStart"].Width = 70;
            _dgvSignals.Columns.Add("colSigLen", "长度");
            _dgvSignals.Columns["colSigLen"].Width = 60;
            _dgvSignals.Columns.Add("colSigRaw", "Raw 值");
            _dgvSignals.Columns["colSigRaw"].Width = 100;
            _dgvSignals.Columns.Add("colSigPhys", "Physical 值");
            _dgvSignals.Columns["colSigPhys"].Width = 100;
            sigPanel.Controls.Add(_dgvSignals);
            var sigHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 24,
                Text = "作用：把总线上收到的帧解码成信号物理值（车速、温度等）。加载 LDF 后自动生效，随监控实时刷新；未加载 LDF 时为空",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            sigPanel.Controls.Add(sigHint);
            _tabLin.TabPages[2].Controls.Add(sigPanel);

            // ---- 发送页签：自定义报文定义表（一条一行，是否发送由调度表勾选）----
            var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _sendToolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            _sendToolbar.Items.Add(new ToolStripButton("添加报文", ToolbarIcons.Get("plus")) { Tag = "add", ToolTipText = "新增一条报文定义（默认 PID 0x00、数据全 0），自动加入「调度表」页签并默认勾选" });
            _sendToolbar.Items.Add(new ToolStripButton("删除报文", ToolbarIcons.Get("clear")) { Tag = "del", ToolTipText = "删除选中报文（同步从「调度表」移除）" });
            _sendToolbar.ItemClicked += SendToolbar_ItemClicked;
            _dgvSend = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            UiTheme.StyleGrid(_dgvSend);
            _dgvSend.Columns.Add("colSendPid", "帧 ID");
            _dgvSend.Columns["colSendPid"].Width = 80;
            _dgvSend.Columns.Add("colSendName", "帧名称");
            _dgvSend.Columns["colSendName"].Width = 240;
            _dgvSend.Columns["colSendName"].ReadOnly = true;
            _dgvSend.Columns.Add("colSendDlc", "DLC");
            _dgvSend.Columns["colSendDlc"].Width = 50;
            _dgvSend.Columns["colSendDlc"].ReadOnly = true;
            _dgvSend.Columns.Add("colSendData", "数据 (Hex)");
            _dgvSend.Columns["colSendData"].Width = 250;
            _dgvSend.CellValueChanged += DgvSend_CellValueChanged;
            _dgvSend.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvSend.IsCurrentCellDirty) _dgvSend.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            sendPanel.Controls.Add(_dgvSend);
            var sendHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 40,
                Text = "作用：定义本机要发送的报文（一条一行，可多条）。「添加报文」自动把该报文加入「调度表」页签并默认勾选——调度表勾选决定是否参与循环发送。\n帧 ID/数据可直接编辑（帧名称按 LDF 自动显示，DLC 随数据长度），编辑后自动同步硬件；发送动作统一在「调度表」页签进行",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            sendPanel.Controls.Add(sendHint);
            // toolbar 最后 Add（Dock=Top 布局须在 Fill 之后处理）
            sendPanel.Controls.Add(_sendToolbar);
            _sendToolbar.Dock = DockStyle.Top;
            _tabLin.TabPages[3].Controls.Add(sendPanel);
            Controls.Add(_tabLin);
        }

        private void BuildStatusStrip()
        {
            _statusStrip = new StatusStrip();
            _lblBus = new ToolStripStatusLabel("总线: 未连接");
            _lblSched = new ToolStripStatusLabel("调度: 停止");
            _lblCount = new ToolStripStatusLabel("帧数: 0");
            _lblError = new ToolStripStatusLabel("错误: 0");
            _lblBaud = new ToolStripStatusLabel("");
            _statusStrip.Items.AddRange(new ToolStripItem[] { _lblBus, new ToolStripStatusLabel("  |  "), _lblSched, new ToolStripStatusLabel("  |  "), _lblCount, new ToolStripStatusLabel("  |  "), _lblError, new ToolStripStatusLabel("  |  "), _lblBaud });
            Controls.Add(_statusStrip);
        }

        private void LayoutControls()
        {
            _toolStripLin.Dock = DockStyle.Top;
            // 表格与页签上下分栏：上表格（报文显示）约 46%，下页签约 54%
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 320,
            };
            split.Panel1.Controls.Add(_dgvFrames);
            split.Panel2.Controls.Add(_tabLin);
            Controls.Add(split);
            _dgvFrames.Dock = DockStyle.Fill;
            _statusStrip.Dock = DockStyle.Bottom;
            // SplitterDistance 须在窗体尺寸确定后设置（构造时 ClientSize 未知会按比例失真）
            this.Shown += (s, e) => { try { split.SplitterDistance = 320; } catch { } };
        }

        // ==================== 通道选择/连接 ====================

        /// <summary>初始化页签操作通道（报文表显示全部通道，按「通道」列区分；页签操作第一个已配置通道）</summary>
        private void InitChannelView()
        {
            _channel = LinConfig.Channels.Count > 0 ? (byte)1 : (byte)0;
            _lblBaud.Text = LinConfig.Channels.Count > 0
                ? "LIN 通道 " + LinConfig.Channels.Count + " 路 · 波特率 " + LinConfig.Channels[0].Baudrate
                : "未配置 LIN 通道";
            RefreshSlotGrid();
            RefreshRespGrid();
            RefreshSignalGrid();
        }

        private void LoadLdf()
        {
            if (_channel < 1 || _channel > LinConfig.Channels.Count) return;
            using (var dlg = new OpenFileDialog { Filter = "LIN 描述文件 (*.lin)|*.lin|所有文件 (*.*)|*.*", Title = "选择 LDF 文件" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var ch = LinConfig.Channels[_channel - 1];
                    try
                    {
                        ch.LdfHelper = LinLdfHelper.Parse(dlg.FileName);
                        ch.LdfPath = dlg.FileName;
                        LinConfig.SaveLinConfig();
                        RefreshSlotGrid();
                        RefreshRespGrid();
                        RefreshSignalGrid();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "LDF 加载失败: " + ex.Message, "LIN 监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        // ==================== 帧接收 ====================

        private void OnFrameReceived(LinFrameRecord frame)
        {
            if (_disposed) return;
            if (_paused) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    lock (_frames)
                    {
                        _frames.Add(frame);
                        if (frame.ErrorKind != LinErrorKind.None) _errorCount++;
                        if (_frames.Count > MaxFrames)
                        {
                            int remove = _frames.Count - MaxFrames;
                            _frames.RemoveRange(0, remove);
                        }
                    }
                    RebuildFilter();
                }));
            }
            catch { /* 窗口关闭竞态 */ }
        }

        /// <summary>帧 ID 过滤（规则与 CAN 接收窗口一致）：空=全部；精确 11 / 0x11；通配符 3*（匹配 0x30-0x3F）；逗号/空格分隔多个（如 11, 3*）</summary>
        private void RebuildFilter()
        {
            if (_disposed) return;
            string text = _txtFilter.Text.Trim();
            // IdFilterRule 不支持 0x 前缀：先剥除
            string norm = text.Replace("0x", "").Replace("0X", "");
            var exact = new HashSet<uint>();
            var wildcards = new List<(uint mask, uint value, uint maxId)>();
            IdFilterRule.Parse(norm, exact, wildcards);
            bool hasRule = exact.Count > 0 || wildcards.Count > 0;
            lock (_frames)
            {
                _filtered.Clear();
                for (int i = 0; i < _frames.Count; i++)
                {
                    if (!hasRule) { _filtered.Add(i); continue; }
                    if (IdFilterRule.Match(_frames[i].Pid, exact, wildcards)) _filtered.Add(i);
                }
                _dgvFrames.RowCount = _filtered.Count;
                _dgvFrames.Invalidate();
            }
        }


        private static byte ParsePid(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (s.EndsWith("*", StringComparison.Ordinal) || s == "*") return 0xFF; // 通配由上层处理
            return byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private void DgvFrames_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _filtered.Count) return;
            LinFrameRecord f;
            lock (_frames) { f = _frames[_filtered[e.RowIndex]]; }
            switch (_dgvFrames.Columns[e.ColumnIndex].Name)
            {
                case "colTime": e.Value = (f.TimestampUs / 1000.0).ToString("F3"); break;
                case "colCh": e.Value = "CH" + f.LogicChannel; break;
                case "colDir": e.Value = f.Direction == LinFrameDir.Tx ? "Tx" : "Rx"; break;
                case "colId": e.Value = "0x" + f.Pid.ToString("X2"); break;
                case "colName": e.Value = f.FrameName; break;
                case "colType": e.Value = FrameTypeText(f.FrameType); break;
                case "colDlc": e.Value = f.Dlc.ToString(); break;
                case "colData": e.Value = f.DataHex; break;
                case "colCs":
                    e.Value = f.ErrorKind == LinErrorKind.Checksum ? "0x" + f.ChecksumRx.ToString("X2") + "*" : "0x" + f.ChecksumRx.ToString("X2");
                    break;
                case "colStatus": e.Value = f.StatusText; break;
            }
        }

        private static string FrameTypeText(LinFrameType t)
        {
            switch (t)
            {
                case LinFrameType.Unconditional: return "无条件帧";
                case LinFrameType.EventTriggered: return "事件触发帧";
                case LinFrameType.Sporadic: return "偶发帧";
                case LinFrameType.Diagnostic: return "诊断帧";
                default: return "未定义";
            }
        }

        private void DgvFrames_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _filtered.Count) return;
            LinFrameRecord f;
            lock (_frames) { f = _frames[_filtered[e.RowIndex]]; }
            if (e.ColumnIndex == _dgvFrames.Columns["colDir"].Index)
            {
                e.CellStyle.ForeColor = f.Direction == LinFrameDir.Tx ? Color.FromArgb(0, 90, 200) : Color.FromArgb(0, 128, 0);
            }
            else if (e.ColumnIndex == _dgvFrames.Columns["colStatus"].Index && f.ErrorKind != LinErrorKind.None)
            {
                e.CellStyle.ForeColor = Color.FromArgb(196, 43, 28);
            }
        }

        // ==================== 调度表页签 ====================

        private LinScheduler GetScheduler()
        {
            LinScheduler sc;
            if (!_schedulers.TryGetValue(_channel, out sc))
            {
                // PEAK 硬件调度表（SetSchedule/StartSchedule）在当前 PLIN Manager/Pro FD 环境全部
                // errUnknown（官方签名实测），统一走软件调度（定时器 + LIN_Write），Vector 本就软件
                sc = new LinScheduler(_channel, false);
                sc.SlotChanged += i => { try { BeginInvoke(new Action(() => UpdateSlotRow(i))); } catch { } };
                sc.RunningChanged += r => { try { BeginInvoke(new Action(() => RefreshStatusBar())); } catch { } };
                _schedulers[_channel] = sc;
            }
            return sc;
        }

        private void RefreshSlotGrid()
        {
            if (_disposed) return;
            var sc = GetScheduler();
            _dgvSlots.Rows.Clear();
            var ldf = GetLdf();
            foreach (var slot in sc.Slots)
            {
                int idx = _dgvSlots.Rows.Add(slot.Enabled, "0x" + slot.Pid.ToString("X2"),
                    LinLdfHelper.GetFrameName(ldf, slot.Pid), slot.SlotMs, slot.Counter);
                _dgvSlots.Rows[idx].Tag = slot;
            }
        }

        /// <summary>槽跳变轻量刷新：仅更新对应行周期数与高亮（避免 10ms 槽位下整表重建闪烁/打断编辑）</summary>
        private void UpdateSlotRow(int slotIndex)
        {
            if (_disposed) return;
            if (slotIndex >= 0 && slotIndex < _dgvSlots.Rows.Count)
            {
                var row = _dgvSlots.Rows[slotIndex];
                if (row.Tag is LinScheduleSlot slot)
                    row.Cells["colSlotCnt"].Value = slot.Counter;
                _dgvSlots.InvalidateRow(slotIndex);
            }
        }

        private void DgvSlots_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvSlots.Rows[e.RowIndex];
            var slot = (LinScheduleSlot)row.Tag;
            if (slot == null) return;
            switch (_dgvSlots.Columns[e.ColumnIndex].Name)
            {
                case "colEn": slot.Enabled = (bool)(row.Cells["colEn"].Value ?? false); break;
                case "colSlotId":
                    try { slot.Pid = ParsePid((row.Cells["colSlotId"].Value ?? "").ToString()); }
                    catch { }
                    row.Cells["colSlotName"].Value = LinLdfHelper.GetFrameName(GetLdf(), slot.Pid);
                    break;
                case "colSlotMs":
                    int ms;
                    if (int.TryParse((row.Cells["colSlotMs"].Value ?? "").ToString(), out ms) && ms > 0) slot.SlotMs = ms;
                    break;
            }
        }

        private void DgvSlots_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvSlots.Rows[e.RowIndex];
            if (row.Tag is LinScheduleSlot && GetScheduler().CurrentSlotIndex == e.RowIndex && GetScheduler().IsRunning)
            {
                e.CellStyle.BackColor = UiTheme.SelectionBack; // 当前槽高亮
            }
        }

        private void SlotToolbar_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (!(e.ClickedItem.Tag is string op)) return;
            var sc = GetScheduler();
            switch (op)
            {
                case "start":
                    string err = sc.ValidateSlots(GetBaudrate());
                    if (err.Length > 0)
                    {
                        MessageBox.Show(this, err, "调度表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (!sc.Start()) ShowError(sc.LastError.Length > 0 ? "调度启动失败: " + sc.LastError : "调度启动失败（无槽或未连接）");
                    break;
                case "suspend":
                    sc.Suspend();
                    break;
                case "step":
                    // 单步：Vector 软件模式发当前选中槽 Header 一次；PEAK 硬件调度不支持单步
                    bool isPcan = _channel >= 1 && _channel <= LinConfig.Channels.Count &&
                                  LinConfig.Channels[_channel - 1].HwType == LinConfig.HwTypePcan;
                    if (isPcan)
                    {
                        ShowError("PEAK 硬件调度不支持单步");
                    }
                    else if (_dgvSlots.SelectedRows.Count > 0)
                    {
                        var slot = (LinScheduleSlot)_dgvSlots.SelectedRows[0].Tag;
                        if (slot != null && !Lin_API.LinSendHeader(_channel, slot.Pid))
                            ShowError("单步发送失败（未连接）");
                    }
                    break;
                case "add":
                    sc.Slots.Add(new LinScheduleSlot { Pid = 0x00, SlotMs = 15 });
                    RefreshSlotGrid();
                    break;
                case "import":
                    ImportSlotsFromLdf();
                    break;
                case "del":
                    if (_dgvSlots.SelectedRows.Count > 0)
                    {
                        sc.Slots.Remove((LinScheduleSlot)_dgvSlots.SelectedRows[0].Tag);
                        RefreshSlotGrid();
                    }
                    break;
                case "up":
                    MoveSlot(sc, -1);
                    break;
                case "down":
                    MoveSlot(sc, 1);
                    break;
            }
            RefreshStatusBar();
        }

        private void MoveSlot(LinScheduler sc, int dir)
        {
            if (_dgvSlots.SelectedRows.Count == 0) return;
            int i = _dgvSlots.SelectedRows[0].Index;
            int j = i + dir;
            if (j < 0 || j >= sc.Slots.Count) return;
            var t = sc.Slots[i];
            sc.Slots[i] = sc.Slots[j];
            sc.Slots[j] = t;
            RefreshSlotGrid();
            if (j >= 0 && j < _dgvSlots.Rows.Count) _dgvSlots.Rows[j].Selected = true;
        }

        /// <summary>
        /// 从 LDF 导入调度表：优先按 LDF 官方调度表（帧名+时隙）填充，无调度表时回退全部帧（时隙默认 15ms）。
        /// 追加到现有调度表并按 PID 去重（不破坏已手动配置的帧槽）；导入的帧默认勾选（Enabled=true）。
        /// </summary>
        private void ImportSlotsFromLdf()
        {
            var ldf = GetLdf();
            if (ldf == null)
            {
                MessageBox.Show(this, "请先加载 LDF 文件（顶部工具栏「加载 LDF」）", "调度表", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var sc = GetScheduler();
            var toAdd = new List<LinScheduleSlot>();
            if (ldf.ScheduleTables.Count > 0)
            {
                // 取第一个调度表（通常为 NormalTable，含每帧官方时隙）
                var table = ldf.ScheduleTables.First().Value;
                foreach (var def in table)
                {
                    var frame = ldf.Frames.Values.FirstOrDefault(f => f.Name == def.FrameName);
                    if (frame == null) continue;
                    toAdd.Add(new LinScheduleSlot { Enabled = true, Pid = frame.Pid, SlotMs = def.SlotMs > 0 ? def.SlotMs : 15 });
                }
            }
            else
            {
                foreach (var kv in ldf.Frames)
                    toAdd.Add(new LinScheduleSlot { Enabled = true, Pid = kv.Key, SlotMs = 15 });
            }
            if (toAdd.Count == 0)
            {
                MessageBox.Show(this, "LDF 中无可用帧（调度表为空）", "调度表", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int added = 0;
            var existing = new HashSet<byte>(sc.Slots.Select(s => s.Pid));
            foreach (var s in toAdd)
            {
                if (existing.Add(s.Pid)) { sc.Slots.Add(s); added++; }
            }
            RefreshSlotGrid();
            MessageBox.Show(this, $"已从 LDF 导入 {added} 条报文到调度表（勾选=参与调度，默认全勾）\n时隙按 LDF 调度表自动填充，可自行调整", "调度表", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private LinLdfFile GetLdf()
        {
            if (_channel >= 1 && _channel <= LinConfig.Channels.Count)
                return LinConfig.Channels[_channel - 1].LdfHelper;
            return null;
        }

        private uint GetBaudrate()
        {
            if (_channel >= 1 && _channel <= LinConfig.Channels.Count)
                return LinConfig.Channels[_channel - 1].Baudrate;
            return 19200;
        }

        // ==================== 从节点/发布页签 ====================

        private void RefreshRespGrid()
        {
            if (_disposed) return;
            _dgvResp.Rows.Clear();
            _expandedResp.Clear();
            var ldf = GetLdf();
            if (ldf == null)
            {
                // 无 LDF：空表
                return;
            }
            foreach (byte pid in ldf.SlaveRespIds)
            {
                var def = ldf.Frames[pid];
                int idx = _dgvResp.Rows.Add("＋", "0x" + pid.ToString("X2"), def.Name, def.Dlc, new string('0', def.Dlc * 2));
                _dgvResp.Rows[idx].Tag = pid;
            }
            if (IsMasterMode())
            {
                // 主节点模式：额外列出主节点发布帧（本机发布数据可编辑）
                foreach (var kv in ldf.Frames)
                {
                    if (kv.Value.Publisher == ldf.MasterName)
                    {
                        bool exists = false;
                        foreach (DataGridViewRow r in _dgvResp.Rows)
                            if (r.Tag is byte && (byte)r.Tag == kv.Key) { exists = true; break; }
                        if (!exists)
                        {
                            int idx = _dgvResp.Rows.Add("＋", "0x" + kv.Key.ToString("X2"), kv.Value.Name, kv.Value.Dlc, new string('0', kv.Value.Dlc * 2));
                            _dgvResp.Rows[idx].Tag = kv.Key;
                        }
                    }
                }
            }
        }

        private bool IsMasterMode()
        {
            return _channel >= 1 && _channel <= LinConfig.Channels.Count &&
                   LinConfig.Channels[_channel - 1].Mode == LinNodeMode.Master;
        }

        private void DgvResp_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvResp.Rows[e.RowIndex];
            // 信号解析行：编辑物理值/枚举 → 编码写回帧数据列并同步硬件
            if (row.Tag is Tuple<byte, string>)
            {
                if (_dgvResp.Columns[e.ColumnIndex].Name != "colRspData") return;
                var t = (Tuple<byte, string>)row.Tag;
                var ldf = GetLdf();
                if (ldf == null) return;
                var sig = FindSignalDef(ldf, t.Item2);
                if (sig == null) return;
                ulong raw;
                if (!TryParseSigValue((row.Cells["colRspData"].Value ?? "").ToString(), sig, out raw))
                {
                    // 非法输入：还原为该信号当前值
                    if (GetRespFrameData(t.Item1) != null) RefreshSignalRows(t.Item1);
                    return;
                }
                ApplySignalToFrame(t.Item1, sig, raw);
                return;
            }
            if (!(row.Tag is byte)) return;
            byte pid = (byte)row.Tag;
            if (_dgvResp.Columns[e.ColumnIndex].Name != "colRspData") return;
            // 数据 Hex 输入（空格分隔）→ 下发硬件
            var data = ParseHexData((row.Cells["colRspData"].Value ?? "").ToString());
            if (data == null) return;
            byte dlc = (byte)data.Length;
            if (dlc > 8) return;
            Lin_API.UpdateSlaveData(_channel, pid, data, dlc);
        }

        /// <summary>取帧行的当前数据（Hex 解析失败返回 null）</summary>
        private byte[] GetRespFrameData(byte pid)
        {
            for (int i = 0; i < _dgvResp.Rows.Count; i++)
            {
                var r = _dgvResp.Rows[i];
                if (r.Tag is byte && (byte)r.Tag == pid)
                    return ParseHexData((r.Cells["colRspData"].Value ?? "").ToString());
            }
            return null;
        }

        /// <summary>把信号新值编码进帧数据，写回帧行并同步硬件，刷新该帧全部信号行显示</summary>
        private void ApplySignalToFrame(byte pid, LinSignalDef sig, ulong raw)
        {
            var ldf = GetLdf();
            byte[] data = GetRespFrameData(pid);
            if (data == null || data.Length == 0)
            {
                byte dlc = ldf != null && ldf.Frames.ContainsKey(pid) ? ldf.Frames[pid].Dlc : (byte)8;
                data = new byte[dlc];
            }
            LinFrameSignal fs = null;
            if (ldf != null && ldf.FrameSignals.ContainsKey(pid))
                foreach (var x in ldf.FrameSignals[pid]) if (x.SignalName == sig.Name) { fs = x; break; }
            if (fs == null) return;
            LinLdfHelper.WriteSignalBits(data, fs.Offset, sig.Width, raw);
            string hex = BitConverter.ToString(data).Replace("-", " ");
            for (int i = 0; i < _dgvResp.Rows.Count; i++)
            {
                var r = _dgvResp.Rows[i];
                if (r.Tag is byte && (byte)r.Tag == pid) { r.Cells["colRspData"].Value = hex; break; }
            }
            RefreshSignalRows(pid);
            Lin_API.UpdateSlaveData(_channel, pid, data, (byte)data.Length);
        }

        /// <summary>按帧行当前数据刷新该帧全部信号行的显示值</summary>
        private void RefreshSignalRows(byte pid)
        {
            var ldf = GetLdf();
            byte[] data = GetRespFrameData(pid);
            if (ldf == null || data == null) return;
            for (int i = 0; i < _dgvResp.Rows.Count; i++)
            {
                var r = _dgvResp.Rows[i];
                if (!(r.Tag is Tuple<byte, string>)) continue;
                var t = (Tuple<byte, string>)r.Tag;
                if (t.Item1 != pid) continue;
                var s2 = FindSignalDef(ldf, t.Item2);
                LinFrameSignal fs2 = null;
                if (s2 != null && ldf.FrameSignals.ContainsKey(pid))
                    foreach (var x in ldf.FrameSignals[pid]) if (x.SignalName == s2.Name) { fs2 = x; break; }
                if (s2 != null && fs2 != null)
                    r.Cells["colRspData"].Value = FormatSigValue(s2, LinLdfHelper.ReadSignalBits(data, fs2.Offset, s2.Width));
            }
        }

        /// <summary>解析 Hex 文本（空格/0x 分隔）→ 字节数组；非法或空返回 null</summary>
        private static byte[] ParseHexData(string text)
        {
            string hex = (text ?? "").Replace(" ", "").Replace("0x", "").Trim();
            if (hex.Length == 0 || hex.Length % 2 != 0) return null;
            var data = new byte[hex.Length / 2];
            try
            {
                for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            catch { return null; }
            if (data.Length > 8) return null;
            return data;
        }

        private void DgvResp_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != _dgvResp.Columns["colRspExpand"].Index) return;
            var row = _dgvResp.Rows[e.RowIndex];
            if (!(row.Tag is byte)) return;
            ToggleRespExpand((byte)row.Tag);
        }

        private void DgvResp_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            // 数据列双击进入编辑，不参与展开/折叠
            if (e.ColumnIndex == _dgvResp.Columns["colRspData"].Index) return;
            var row = _dgvResp.Rows[e.RowIndex];
            if (!(row.Tag is byte)) return;
            ToggleRespExpand((byte)row.Tag);
        }

        /// <summary>展开/折叠一帧的信号解析行（＋/－ 切换）</summary>
        private void ToggleRespExpand(byte pid)
        {
            var ldf = GetLdf();
            if (ldf == null || !ldf.FrameSignals.ContainsKey(pid) || ldf.FrameSignals[pid].Count == 0)
            {
                MessageBox.Show(this, "该帧在 LDF 中没有信号定义，无法展开解析\n可直接编辑「数据 (Hex)」列，或加载包含该帧信号的 LDF", "展开信号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_expandedResp.Contains(pid)) CollapseRespFrame(pid);
            else ExpandRespFrame(pid, ldf);
        }

        private void ExpandRespFrame(byte pid, LinLdfFile ldf)
        {
            // 定位帧行（忽略可能已存在的信号行）
            int rowIdx = -1;
            for (int i = 0; i < _dgvResp.Rows.Count; i++)
                if (_dgvResp.Rows[i].Tag is byte && (byte)_dgvResp.Rows[i].Tag == pid) { rowIdx = i; break; }
            if (rowIdx < 0) return;

            var frameRow = _dgvResp.Rows[rowIdx];
            var data = ParseHexData((frameRow.Cells["colRspData"].Value ?? "").ToString());
            if (data == null || data.Length == 0)
            {
                byte dlc = ldf.Frames.ContainsKey(pid) ? ldf.Frames[pid].Dlc : (byte)8;
                data = new byte[dlc];
            }
            int insertAt = rowIdx + 1;
            int maxNameLen = 0;
            foreach (var fs in ldf.FrameSignals[pid])
                if (fs.SignalName.Length > maxNameLen) maxNameLen = fs.SignalName.Length;
            foreach (var fs in ldf.FrameSignals[pid])
            {
                var sig = FindSignalDef(ldf, fs.SignalName);
                ulong raw = sig != null ? LinLdfHelper.ReadSignalBits(data, fs.Offset, sig.Width) : 0;
                string valText = sig != null ? FormatSigValue(sig, raw) : "—";
                _dgvResp.Rows.Insert(insertAt, 1);
                var srow = _dgvResp.Rows[insertAt];
                srow.Cells["colRspExpand"].Value = "";
                srow.Cells["colRspId"].Value = null;
                srow.Cells["colRspName"].Value = "　├ " + fs.SignalName.PadRight(maxNameLen);
                srow.Cells["colRspDlc"].Value = "bit " + fs.Offset;
                srow.Cells["colRspData"].Value = valText;
                srow.Tag = new Tuple<byte, string>(pid, fs.SignalName);
                srow.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 248);
                insertAt++;
            }
            _expandedResp.Add(pid);
            frameRow.Cells["colRspExpand"].Value = "－";
        }

        private void CollapseRespFrame(byte pid)
        {
            // 删除该帧的信号行
            for (int i = _dgvResp.Rows.Count - 1; i >= 0; i--)
            {
                var r = _dgvResp.Rows[i];
                if (r.Tag is Tuple<byte, string> && ((Tuple<byte, string>)r.Tag).Item1 == pid)
                    _dgvResp.Rows.RemoveAt(i);
            }
            _expandedResp.Remove(pid);
            for (int i = 0; i < _dgvResp.Rows.Count; i++)
            {
                var r = _dgvResp.Rows[i];
                if (r.Tag is byte && (byte)r.Tag == pid) { r.Cells["colRspExpand"].Value = "＋"; break; }
            }
        }

        private static LinSignalDef FindSignalDef(LinLdfFile ldf, string name)
        {
            foreach (var s in ldf.Signals) if (s.Name == name) return s;
            return null;
        }

        /// <summary>信号原始值 → 显示文本（枚举 → 枚举文本；物理值 → 数值 + 单位）</summary>
        private static string FormatSigValue(LinSignalDef sig, ulong raw)
        {
            if (sig.LogicalValues != null && sig.LogicalValues.Count > 0)
            {
                string text;
                if (sig.LogicalValues.TryGetValue((byte)raw, out text)) return text;
            }
            double phys = LinLdfHelper.RawToPhys(raw, sig);
            string num = phys == Math.Floor(phys) ? ((long)phys).ToString() : phys.ToString("0.###");
            return sig.Unit.Length > 0 ? num + " " + sig.Unit : num;
        }

        /// <summary>信号值编辑文本 → 原始值（枚举文本 / 数值（可带单位后缀））</summary>
        private static bool TryParseSigValue(string text, LinSignalDef sig, out ulong raw)
        {
            text = (text ?? "").Trim();
            if (sig.LogicalValues != null && sig.LogicalValues.Count > 0)
            {
                foreach (var kv in sig.LogicalValues)
                    if (string.Equals(kv.Value, text, StringComparison.OrdinalIgnoreCase)) { raw = kv.Key; return true; }
            }
            if (sig.Unit.Length > 0 && text.EndsWith(sig.Unit, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - sig.Unit.Length).Trim();
            double phys;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out phys)) { raw = 0; return false; }
            raw = LinLdfHelper.PhysToRaw(phys, sig);
            return true;
        }

        // ==================== 信号页签 ====================

        private void RefreshSignalGrid()
        {
            if (_disposed) return;
            _dgvSignals.Rows.Clear();
            var ldf = GetLdf();
            if (ldf == null) return;
            foreach (var kv in ldf.FrameSignals)
            {
                byte pid = kv.Key;
                foreach (var fs in kv.Value)
                {
                    var sig = ldf.Signals.FirstOrDefault(s => s.Name == fs.SignalName);
                    int idx = _dgvSignals.Rows.Add(fs.SignalName, "0x" + pid.ToString("X2"), fs.Offset,
                        sig != null ? sig.Width.ToString() : "?", "—", "—");
                    _dgvSignals.Rows[idx].Tag = new Tuple<byte, LinFrameSignal>(pid, fs);
                }
            }
            RefreshSignalValues();
        }

        private void RefreshSignalValues()
        {
            if (_disposed) return;
            var ldf = GetLdf();
            if (ldf == null) return;
            // 取每帧最近一条 Rx 记录解码（限制扫描窗口最近 2000 帧，防长期无响应帧触发全量扫描）
            var latest = new Dictionary<byte, LinFrameRecord>();
            lock (_frames)
            {
                int scanLimit = Math.Max(0, _frames.Count - 2000);
                for (int i = _frames.Count - 1; i >= scanLimit && latest.Count < ldf.Frames.Count; i--)
                {
                    var f = _frames[i];
                    if (f.Direction == LinFrameDir.Rx && !latest.ContainsKey(f.Pid)) latest[f.Pid] = f;
                }
            }
            foreach (DataGridViewRow row in _dgvSignals.Rows)
            {
                if (!(row.Tag is Tuple<byte, LinFrameSignal> t)) continue;
                var pid = t.Item1;
                var fs = t.Item2;
                // 信号长度从 LDF 信号定义取
                var sigDef = ldf.Signals.FirstOrDefault(s => s.Name == fs.SignalName);
                LinFrameRecord f;
                if (latest.TryGetValue(pid, out f) && f.Data != null && sigDef != null)
                {
                    long raw = ExtractBits(f.Data, fs.Offset, sigDef.Width);
                    row.Cells["colSigRaw"].Value = raw.ToString();
                    row.Cells["colSigPhys"].Value = raw.ToString(); // v1 无编码表，物理=raw
                }
                else
                {
                    row.Cells["colSigRaw"].Value = "—";
                    row.Cells["colSigPhys"].Value = "—";
                }
            }
        }

        /// <summary>从帧数据提取 LSB-first 位段（LIN 信号默认 LSB 优先）</summary>
        private static long ExtractBits(byte[] data, int startBit, int length)
        {
            long v = 0;
            for (int i = 0; i < length && startBit + i < data.Length * 8; i++)
            {
                int bit = startBit + i;
                if (((data[bit / 8] >> (bit % 8)) & 1) != 0) v |= 1L << i;
            }
            return v;
        }

        // ==================== 发送页签 ====================

        private void SendToolbar_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (!(e.ClickedItem.Tag is string op)) return;
            var sc = GetScheduler();
            switch (op)
            {
                case "add":
                    // 新增报文定义：加入调度表（默认勾选）并显示一行
                    var slot = new LinScheduleSlot { Pid = 0x00, SlotMs = 15, Enabled = true };
                    sc.Slots.Add(slot);
                    RefreshSlotGrid();
                    int idx = _dgvSend.Rows.Add("0x00", LinLdfHelper.GetFrameName(GetLdf(), 0x00), 0, "");
                    _dgvSend.Rows[idx].Tag = slot;
                    _dgvSend.CurrentCell = _dgvSend.Rows[idx].Cells["colSendPid"];
                    _dgvSend.BeginEdit(true);
                    break;
                case "del":
                    if (_dgvSend.SelectedRows.Count > 0)
                    {
                        var sel = _dgvSend.SelectedRows[0];
                        if (sel.Tag is LinScheduleSlot s)
                        {
                            sc.Slots.Remove(s);
                            RefreshSlotGrid();
                        }
                        _dgvSend.Rows.Remove(sel);
                    }
                    break;
            }
        }

        private void DgvSend_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvSend.Rows[e.RowIndex];
            if (!(row.Tag is LinScheduleSlot slot)) return;
            switch (_dgvSend.Columns[e.ColumnIndex].Name)
            {
                case "colSendPid":
                    byte newPid;
                    try { newPid = ParsePid((row.Cells["colSendPid"].Value ?? "").ToString()); }
                    catch { return; }
                    if (newPid > 0x3F) { ShowError("PID 须 0x00-0x3F"); return; }
                    slot.Pid = newPid;
                    row.Cells["colSendName"].Value = LinLdfHelper.GetFrameName(GetLdf(), newPid);
                    RefreshSlotGrid();
                    break;
                case "colSendData":
                    var data = ParseHexData((row.Cells["colSendData"].Value ?? "").ToString());
                    if (data == null) return;
                    row.Cells["colSendDlc"].Value = data.Length;
                    Lin_API.UpdateSlaveData(_channel, slot.Pid, data, (byte)data.Length);
                    break;
            }
        }

        // ==================== 状态栏 ====================

        private void RefreshStatusBar()
        {
            if (_disposed) return;
            if (_channel >= 1 && _channel <= LinConfig.Channels.Count)
            {
                var ch = LinConfig.Channels[_channel - 1];
                _lblBus.Text = ch.IsConnected ? "总线: " + Lin_API.GetBusStateText(_channel) : "总线: 未连接";
                var sc = GetScheduler();
                _lblSched.Text = sc.IsRunning ? "调度: 运行中" : "调度: 停止";
            }
            else
            {
                _lblBus.Text = "总线: 未连接";
                _lblSched.Text = "调度: 停止";
            }
            lock (_frames)
            {
                _lblCount.Text = "帧数: " + _frames.Count;
            }
            _lblError.Text = "错误: " + _errorCount;
        }

        private void OnBusEvent(byte ch, string kind)
        {
            try { BeginInvoke(new Action(() => { if (ch == _channel) RefreshStatusBar(); })); } catch { }
        }

        private void OnLinkLost(byte ch, string reason)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (ch == _channel) ShowError("链路丢失: " + reason + "（自动重连中…）");
                }));
            }
            catch { }
        }

        private void OnChannelStateChanged(byte ch, bool connected, string error)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    InitChannelView();
                    RefreshStatusBar();
                    if (connected) _lblBus.Text = "总线: 已连接";
                }));
            }
            catch { }
        }

        private void ShowError(string msg)
        {
            if (_disposed) return;
            try
            {
                _lblError.Text = "错误: " + msg;
                _lblError.ForeColor = Color.FromArgb(196, 43, 28);
            }
            catch { }
        }
    }
}
