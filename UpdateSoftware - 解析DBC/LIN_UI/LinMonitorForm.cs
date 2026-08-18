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
        // 帧日志（Scroll 数据源）：接收线程 O(1) 追加，UI 线程按 100ms 节流重建（与 CAN 报文窗口同架构）
        private readonly List<LinFrameRecord> _frames = new List<LinFrameRecord>();
        private long _errorCount;
        private volatile bool _paused;
        private byte _channel;          // 当前监控通道（逻辑号）
        private readonly Dictionary<byte, LinScheduler> _schedulers = new Dictionary<byte, LinScheduler>();
        private readonly Timer _uiTimer;
        private bool _disposed;

        private const int MaxFrames = 500000;

        // === 显示模式（对齐 CAN 报文窗口）：Fixed=按 (PID,通道) 聚合；Scroll=按时间逐帧 ===
        private bool _linScrollMode;            // false=Fixed, true=Scroll
        private ToolStripButton _btnLinScroll;  // 工具栏 Fixed/Scroll 切换按钮

        // === Fixed 模式聚合表（每 (PID,通道) 一行，接收时 O(1) 更新） ===
        private class LinFixedInfo
        {
            public byte Pid;
            public byte Channel;
            public uint Count;
            public ulong LastTimestampUs;
            public ulong PrevSameIdGapUs;   // 与同键上一帧间隔（写入时预计算，替代每行 O(n) 前向扫描）
            public LinFrameRecord Last;     // 最新帧快照（Data 引用不可变，可安全共享）
        }
        private readonly List<LinFixedInfo> _linFixedList = new List<LinFixedInfo>();
        private readonly Dictionary<long, LinFixedInfo> _linMsgIndexMap = new Dictionary<long, LinFixedInfo>();
        private static long LinMsgKey(byte pid, byte ch) => ((long)ch << 8) | pid;

        // === 展开/折叠信号（对齐 CAN：＋/－ 列 + 双击整行） ===
        private enum LinRowType { Frame, Signal }
        private class LinFlatRow
        {
            public LinRowType Type;
            public int FrameIndex = -1;      // _frames 索引（Scroll 模式帧行/信号行）
            public int FixedIndex = -1;      // _linFixedList 索引（Fixed 模式帧行）
            public byte Pid;
            public byte Channel;
            public string SignalName = "";   // 仅 Signal 行
            public int SigOffset = -1;       // 仅 Signal 行（LDF 起始位）
        }
        private readonly List<LinFlatRow> _linFlatRows = new List<LinFlatRow>();
        private readonly HashSet<long> _linExpandedKeys = new HashSet<long>();   // Fixed：按 (ch,pid) 复合键
        private readonly HashSet<int> _linExpandedFrames = new HashSet<int>();  // Scroll：按帧索引

        // === 刷新节流（对齐 CAN MIN_REFRESH_MS=100，最大 10fps 重建，消除每帧全量过滤的 O(n²) 卡顿） ===
        private bool _linRefreshPending;
        private bool _linRefreshScheduled;   // 已排队 BeginInvoke（避免每帧排队堆积）
        private DateTime _lastLinRefresh = DateTime.MinValue;
        private const int LIN_MIN_REFRESH_MS = 100;
        private bool _linFlatRowsDirty = true;
        // Scroll：已显示帧数（暂停/离开底部时增量追加）+ 自动跟随状态
        private int _linFramesLoaded;
        private bool _linUserScrolledAway;
        private int _lastLinScrollRowCount;
        private const int LIN_SCROLL_LIVE_FRAMES = 50; // 接收中只显示最新 N 条匹配帧（LIN 帧率低，取 50 保留更多上下文）

        // 帧 ID 过滤规则（文本变化时解析缓存，重建时复用）
        private readonly HashSet<uint> _linFilterIds = new HashSet<uint>();
        private readonly List<(uint mask, uint value, uint maxId)> _linFilterWildcards = new List<(uint, uint, uint)>();

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
                RefreshLinMessageDisplay(); // 节流内兜底刷新（BeginInvoke 被节流丢弃时补上）
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
            _btnStart.Click += (s, e) => { _paused = false; _linRefreshPending = true; RefreshLinMessageDisplay(); };
            _btnPause = new ToolStripButton("暂停", ToolbarIcons.Get("stop"));
            _btnPause.Click += (s, e) =>
            {
                _paused = true;
                // 暂停后 Scroll 模式显示全部帧（供回看），Fixed 模式冻结最新聚合
                _linFramesLoaded = 0;
                _linFlatRowsDirty = true;
                _linRefreshPending = true;
                DoLinRefresh();
            };
            _btnClear = new ToolStripButton("清空", ToolbarIcons.Get("clear"));
            _btnClear.Click += (s, e) => ClearLinFrames();
            _txtFilter = new ToolStripTextBox { Width = 160, ToolTipText = "帧 ID 过滤（规则与 CAN 接收窗口一致）：精确 11 / 0x11；通配符 3*（匹配 0x30-0x3F）、*（全部）；逗号或空格分隔多个，如 11, 3*" };
            _txtFilter.TextChanged += (s, e) => { ParseLinFilter(); RebuildLinFiltered(); };
            // Fixed/Scroll 切换（对齐 CAN 报文窗口：CheckOnClick 高亮表示 Scroll 模式）
            _btnLinScroll = new ToolStripButton("Fixed", ToolbarIcons.Get("scroll"));
            _btnLinScroll.CheckOnClick = true;
            _btnLinScroll.Click += (s, e) =>
            {
                _linScrollMode = !_linScrollMode;
                _btnLinScroll.Checked = _linScrollMode;
                _btnLinScroll.Text = _linScrollMode ? "Scroll" : "Fixed";
                _linFramesLoaded = 0;           // 重置增量计数
                _linUserScrolledAway = false;
                _lastLinScrollRowCount = 0;
                _linExpandedFrames.Clear();
                _linFlatRowsDirty = true;
                // 海量帧↔聚合视图切换：先清空行数，避免 DataGridView 对数万旧行做增量布局（切换卡顿主因）
                if (_dgvFrames.RowCount > 0) _dgvFrames.RowCount = 0;
                _linRefreshPending = true;
                DoLinRefresh();
            };
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
            _toolStripLin.Items.Add(_btnLinScroll);
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
            _dgvFrames.CellPainting += DgvFrames_CellPainting;
            _dgvFrames.CellClick += DgvFrames_CellClick;
            _dgvFrames.CellDoubleClick += DgvFrames_CellDoubleClick;
            _dgvFrames.Scroll += (ss, ee) =>
            {
                // Scroll 模式自动跟随：检测用户是否在底部；离开底部切换为显示全部帧（供回看）
                if (!_linScrollMode || _dgvFrames.RowCount <= 0) return;
                bool wasAway = _linUserScrolledAway;
                int visibleRows = _dgvFrames.DisplayedRowCount(false);
                int firstRow = _dgvFrames.FirstDisplayedScrollingRowIndex;
                bool atBottom = firstRow >= 0 && firstRow + visibleRows >= _dgvFrames.RowCount - 1;
                _linUserScrolledAway = !atBottom;
                if (wasAway != _linUserScrolledAway)
                {
                    _linFlatRowsDirty = true;
                    _linRefreshPending = true;
                    if (!_linUserScrolledAway) _lastLinScrollRowCount = 0;
                    RefreshLinMessageDisplay();
                }
            };
            // 双缓冲消除滚动闪烁（与 CAN 报文窗口一致）
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, _dgvFrames, new object[] { true });
            // 空态提示：未收到报文时说明此区域用途（连接后实时显示总线报文）；
            // 只画在数据区（表头下方），避免遮住列头文字
            _dgvFrames.Paint += (s, e) =>
            {
                if (_dgvFrames.RowCount > 0) return;
                var area = _dgvFrames.DisplayRectangle;
                area.Y += _dgvFrames.ColumnHeadersHeight;
                area.Height -= _dgvFrames.ColumnHeadersHeight;
                if (area.Height <= 0) return;
                TextRenderer.DrawText(e.Graphics,
                    "暂无报文 — 连接通道后，此处实时显示总线上的报文（时间/方向/ID/帧名称/数据）",
                    UiTheme.UiFont, area, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            // 展开列（＋/－，CellPainting 绘制，无信号定义的帧不显示）
            _dgvFrames.Columns.Add("colExpand", "");
            _dgvFrames.Columns["colExpand"].Width = 24;
            _dgvFrames.Columns["colExpand"].SortMode = DataGridViewColumnSortMode.NotSortable;
            _dgvFrames.Columns.Add("colCount", "次数");
            _dgvFrames.Columns["colCount"].Width = 60;
            _dgvFrames.Columns["colCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
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

        /// <summary>
        /// 接收线程回调：O(1) 写入帧日志与 Fixed 聚合，标记待刷新并节流排队 UI 更新。
        /// 旧实现每帧 BeginInvoke + 全量 RebuildFilter（O(n) 扫描 + 全表 Invalidate）是报文繁忙时
        /// UI 卡顿、CPU 飙升的根因；现按 CAN 报文窗口同架构改为 100ms 节流重建。
        /// </summary>
        private void OnFrameReceived(LinFrameRecord frame)
        {
            if (_disposed) return;
            lock (_frames)
            {
                _frames.Add(frame);
                if (frame.ErrorKind != LinErrorKind.None) _errorCount++;
                if (_frames.Count > MaxFrames)
                {
                    int remove = _frames.Count - MaxFrames;
                    _frames.RemoveRange(0, remove);
                }
                UpdateLinFixed(frame); // O(1) 聚合（同锁保护）
            }
            _linRefreshPending = true;
            if (_linRefreshScheduled) return;
            _linRefreshScheduled = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _linRefreshScheduled = false;
                    RefreshLinMessageDisplay();
                }));
            }
            catch { _linRefreshScheduled = false; } /* 窗口关闭竞态 */
        }

        /// <summary>Fixed 聚合 O(1) 更新：命中 (PID,通道) 键更新计数/间隔/最新帧；新键追加聚合表</summary>
        private void UpdateLinFixed(LinFrameRecord frame)
        {
            long key = LinMsgKey(frame.Pid, frame.LogicChannel);
            LinFixedInfo info;
            if (!_linMsgIndexMap.TryGetValue(key, out info))
            {
                info = new LinFixedInfo { Pid = frame.Pid, Channel = frame.LogicChannel };
                _linMsgIndexMap[key] = info;
                _linFixedList.Add(info);
                _linFlatRowsDirty = true;
            }
            info.Count++;
            if (info.LastTimestampUs > 0 && frame.TimestampUs >= info.LastTimestampUs)
                info.PrevSameIdGapUs = frame.TimestampUs - info.LastTimestampUs;
            info.LastTimestampUs = frame.TimestampUs;
            info.Last = frame;
        }

        /// <summary>清空全部报文数据与统计（保留显示模式/过滤设置）</summary>
        private void ClearLinFrames()
        {
            lock (_frames)
            {
                _frames.Clear();
                _linFixedList.Clear();
                _linMsgIndexMap.Clear();
                _errorCount = 0;
            }
            _linExpandedKeys.Clear();
            _linExpandedFrames.Clear();
            _linFlatRows.Clear();
            _linFramesLoaded = 0;
            _linUserScrolledAway = false;
            _lastLinScrollRowCount = 0;
            _linFlatRowsDirty = true;
            _dgvFrames.RowCount = 0;
            _linRefreshPending = true;
            DoLinRefresh();
            RefreshStatusBar();
        }

        /// <summary>过滤文本变化：解析规则（0x 前缀剥除，规则与 CAN 一致）</summary>
        private void ParseLinFilter()
        {
            if (_disposed) return;
            string norm = _txtFilter.Text.Trim().Replace("0x", "").Replace("0X", "");
            _linFilterIds.Clear();
            _linFilterWildcards.Clear();
            IdFilterRule.Parse(norm, _linFilterIds, _linFilterWildcards);
        }

        /// <summary>过滤变化后的重建触发：Scroll 从头增量 / Fixed 全量（绕过暂停门，暂停下也生效）</summary>
        private void RebuildLinFiltered()
        {
            if (_disposed) return;
            _linFramesLoaded = 0;
            _linFlatRowsDirty = true;
            _linRefreshPending = true;
            DoLinRefresh();
        }

        private bool LinPassesFilter(byte pid)
        {
            return IdFilterRule.Match(pid, _linFilterIds, _linFilterWildcards);
        }

        /// <summary>UI 线程：节流重建扁平行列表并同步表格（最大 10fps，对齐 CAN MIN_REFRESH_MS）。
        /// 暂停时冻结画面（帧仍持续记录），仅返回不刷新。</summary>
        private void RefreshLinMessageDisplay()
        {
            if (_disposed) return;
            if (!_linRefreshPending) return;
            if (_paused) return; // 暂停时冻结画面（帧仍持续记录）
            DoLinRefresh();
        }

        /// <summary>实际刷新（绕过暂停门：模式切换/展开/过滤等用户操作在暂停下也须生效）</summary>
        private void DoLinRefresh()
        {
            if (_disposed) return;
            var now = DateTime.Now;
            if ((now - _lastLinRefresh).TotalMilliseconds < LIN_MIN_REFRESH_MS) return;
            _linRefreshPending = false;
            _lastLinRefresh = now;

            RebuildLinFlatRows();

            _dgvFrames.SuspendLayout();
            int targetCount = _linFlatRows.Count;
            if (_dgvFrames.RowCount != targetCount)
            {
                // Fixed 模式行数差异大时先归零再设目标（避免 DataGridView 内部大量增量重算）
                if (!_linScrollMode && targetCount > _linFixedList.Count * 5)
                    _dgvFrames.RowCount = 0;
                _dgvFrames.RowCount = targetCount;
            }
            else
            {
                // 行数不变仍需强制刷新单元格内容（Scroll 最新帧替换旧帧）
                _dgvFrames.Invalidate();
            }
            _dgvFrames.ResumeLayout();

            // Scroll 自动跟随：仅用户未离开底部且行数增长时滚动到最新（节流：增量超阈值才滚）
            if (_linScrollMode && targetCount > 0 && !_linUserScrolledAway && targetCount > _lastLinScrollRowCount)
            {
                try { _dgvFrames.FirstDisplayedScrollingRowIndex = targetCount - 1; }
                catch { }
                _lastLinScrollRowCount = targetCount;
            }
        }

        /// <summary>重建扁平行列表（_frames 锁内）：Fixed=聚合行+展开信号行；Scroll=帧行+展开信号行</summary>
        private void RebuildLinFlatRows()
        {
            lock (_frames)
            {
                if (_linScrollMode)
                {
                    bool showAll = _paused || _linUserScrolledAway;
                    if (showAll)
                    {
                        // 暂停/离开底部：增量追加全部匹配帧
                        if (_linFlatRowsDirty || _linFlatRows.Count == 0 || _linFramesLoaded > _frames.Count)
                        {
                            _linFlatRows.Clear();
                            _linFramesLoaded = 0;
                        }
                        for (int i = _linFramesLoaded; i < _frames.Count; i++)
                        {
                            var f = _frames[i];
                            if (!LinPassesFilter(f.Pid)) continue;
                            AppendLinFrameRow(f, i);
                        }
                        _linFramesLoaded = _frames.Count;
                    }
                    else
                    {
                        // 接收中：只显示最新 LIN_SCROLL_LIVE_FRAMES 条匹配帧（全量重建，仅几十行极快）
                        int matched = 0;
                        int start = Math.Max(0, _frames.Count - LIN_SCROLL_LIVE_FRAMES);
                        for (int i = _frames.Count - 1; i >= 0 && matched < LIN_SCROLL_LIVE_FRAMES; i--)
                        {
                            if (!LinPassesFilter(_frames[i].Pid)) continue;
                            matched++;
                            start = i;
                        }
                        _linFlatRows.Clear();
                        for (int i = start; i < _frames.Count; i++)
                        {
                            var f = _frames[i];
                            if (!LinPassesFilter(f.Pid)) continue;
                            AppendLinFrameRow(f, i);
                        }
                        _linFramesLoaded = _frames.Count;
                    }
                }
                else
                {
                    // Fixed：聚合视图（按 PID,通道 排序；行数 ≤ 128，重建极快）
                    if (!_linFlatRowsDirty && _linFlatRows.Count > 0) return;
                    _linFixedList.Sort((a, b) => a.Pid != b.Pid ? a.Pid.CompareTo(b.Pid) : a.Channel.CompareTo(b.Channel));
                    _linFlatRows.Clear();
                    for (int i = 0; i < _linFixedList.Count; i++)
                    {
                        var info = _linFixedList[i];
                        if (!LinPassesFilter(info.Pid)) continue;
                        _linFlatRows.Add(new LinFlatRow { Type = LinRowType.Frame, FixedIndex = i, Pid = info.Pid, Channel = info.Channel });
                        if (_linExpandedKeys.Contains(LinMsgKey(info.Pid, info.Channel)))
                            AppendLinSignalRows(info.Last, -1);
                    }
                }
            }
            _linFlatRowsDirty = false;
        }

        /// <summary>追加一帧行及展开信号行（须在 _frames 锁内）</summary>
        private void AppendLinFrameRow(LinFrameRecord f, int frameIndex)
        {
            _linFlatRows.Add(new LinFlatRow { Type = LinRowType.Frame, FrameIndex = frameIndex, Pid = f.Pid, Channel = f.LogicChannel });
            if (_linExpandedFrames.Contains(frameIndex))
                AppendLinSignalRows(f, frameIndex);
        }

        /// <summary>按帧追加该帧全部信号行（LDF 按通道匹配；无定义时跳过）</summary>
        private void AppendLinSignalRows(LinFrameRecord f, int frameIndex)
        {
            var ldf = GetLdfForChannel(f.LogicChannel);
            if (ldf == null) return;
            List<LinFrameSignal> sigs;
            if (!ldf.FrameSignals.TryGetValue(f.Pid, out sigs) || sigs.Count == 0) return;
            foreach (var fs in sigs)
                _linFlatRows.Add(new LinFlatRow { Type = LinRowType.Signal, FrameIndex = frameIndex, Pid = f.Pid, Channel = f.LogicChannel, SignalName = fs.SignalName, SigOffset = fs.Offset });
        }

        private static LinLdfFile GetLdfForChannel(byte ch)
        {
            if (ch >= 1 && ch <= LinConfig.Channels.Count)
                return LinConfig.Channels[ch - 1].LdfHelper;
            return null;
        }

        /// <summary>某帧是否有 LDF 信号定义（展开按钮绘制/切换判断）</summary>
        private bool LinFrameHasSignals(byte pid, byte ch)
        {
            var ldf = GetLdfForChannel(ch);
            if (ldf == null) return false;
            List<LinFrameSignal> sigs;
            return ldf.FrameSignals.TryGetValue(pid, out sigs) && sigs.Count > 0;
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
            if (e.RowIndex < 0 || e.RowIndex >= _linFlatRows.Count) return;
            var flat = _linFlatRows[e.RowIndex];
            string col = _dgvFrames.Columns[e.ColumnIndex].Name;
            if (flat.Type == LinRowType.Frame)
            {
                LinFrameRecord f;
                LinFixedInfo info = null;
                lock (_frames)
                {
                    if (flat.FixedIndex >= 0)
                    {
                        if (flat.FixedIndex >= _linFixedList.Count) return;
                        info = _linFixedList[flat.FixedIndex];
                        f = info.Last;
                    }
                    else
                    {
                        if (flat.FrameIndex < 0 || flat.FrameIndex >= _frames.Count) return;
                        f = _frames[flat.FrameIndex];
                    }
                }
                switch (col)
                {
                    case "colExpand": e.Value = ""; break;
                    case "colCount":
                        e.Value = info != null ? info.Count.ToString() : (flat.FrameIndex + 1).ToString();
                        break;
                    case "colTime":
                        e.Value = ((info != null ? info.LastTimestampUs : f.TimestampUs) / 1000.0).ToString("F3");
                        break;
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
            else
            {
                // 信号行：按帧数据 + LDF 定义解码物理值/枚举（Scroll 取帧日志，Fixed 取聚合最新帧）
                LinFrameRecord f;
                lock (_frames)
                {
                    if (flat.FrameIndex >= 0)
                    {
                        if (flat.FrameIndex >= _frames.Count) return;
                        f = _frames[flat.FrameIndex];
                    }
                    else
                    {
                        LinFixedInfo info;
                        if (!_linMsgIndexMap.TryGetValue(LinMsgKey(flat.Pid, flat.Channel), out info)) return;
                        f = info.Last;
                    }
                }
                var ldf = GetLdfForChannel(flat.Channel);
                if (ldf == null) return;
                var sigDef = FindSignalDef(ldf, flat.SignalName);
                if (sigDef == null) return;
                ulong raw = (f.Data != null && f.Data.Length > 0)
                    ? LinLdfHelper.ReadSignalBits(f.Data, flat.SigOffset, sigDef.Width)
                    : 0;
                switch (col)
                {
                    case "colName": e.Value = "　├ " + flat.SignalName; break;
                    case "colDlc": e.Value = "bit " + flat.SigOffset; break;
                    case "colData": e.Value = FormatSigValue(sigDef, raw); break;
                    default: e.Value = ""; break;
                }
            }
        }

        /// <summary>展开列绘制 ＋/－（仅该帧有 LDF 信号定义时显示，对齐 CAN colFilter）</summary>
        private void DgvFrames_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || e.ColumnIndex != _dgvFrames.Columns["colExpand"].Index) return;
                if (e.RowIndex >= _linFlatRows.Count) return;
                var flat = _linFlatRows[e.RowIndex];
                if (flat.Type != LinRowType.Frame) return;
                if (!LinFrameHasSignals(flat.Pid, flat.Channel)) return; // 无信号定义不画按钮
                e.Handled = true;
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                using (var bg = new SolidBrush(selected ? _dgvFrames.DefaultCellStyle.SelectionBackColor
                    : e.CellStyle.BackColor.IsEmpty ? _dgvFrames.DefaultCellStyle.BackColor : e.CellStyle.BackColor))
                {
                    e.Graphics.FillRectangle(bg, e.CellBounds);
                }
                bool isExpanded = flat.FixedIndex >= 0
                    ? _linExpandedKeys.Contains(LinMsgKey(flat.Pid, flat.Channel))
                    : _linExpandedFrames.Contains(flat.FrameIndex);
                using (var btnFont = new Font("Arial", 10f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    e.Graphics.DrawString(isExpanded ? "−" : "+", btnFont, brush, e.CellBounds, sf);
                }
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
            }
            catch { /* 防御：绘制异常不中断 */ }
        }

        /// <summary>展开列单击：切换信号展开（对齐 CAN colFilter 交互）</summary>
        private void DgvFrames_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex != _dgvFrames.Columns["colExpand"].Index) return;
            ToggleLinExpansion(e.RowIndex);
        }

        /// <summary>双击整行：切换信号展开（对齐 CAN：除展开列外任意列双击）</summary>
        private void DgvFrames_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == _dgvFrames.Columns["colExpand"].Index) return; // 由 CellClick 处理
            ToggleLinExpansion(e.RowIndex);
        }

        /// <summary>切换一行帧的信号展开/折叠（无信号定义时静默忽略，与 CAN 一致）</summary>
        private void ToggleLinExpansion(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _linFlatRows.Count) return;
            var flat = _linFlatRows[rowIndex];
            if (flat.Type != LinRowType.Frame) return;
            if (!LinFrameHasSignals(flat.Pid, flat.Channel)) return;

            if (flat.FixedIndex >= 0)
            {
                // Fixed 模式：聚合键展开（重建，行数 ≤ 128 极快；绕过暂停门）
                long key = LinMsgKey(flat.Pid, flat.Channel);
                if (_linExpandedKeys.Contains(key)) _linExpandedKeys.Remove(key);
                else _linExpandedKeys.Add(key);
                _linFlatRowsDirty = true;
                _linRefreshPending = true;
                DoLinRefresh();
            }
            else
            {
                // Scroll 模式：帧索引展开（原地插入/删除信号行，避免全量重建）
                lock (_frames)
                {
                    if (flat.FrameIndex < 0 || flat.FrameIndex >= _frames.Count) return;
                    bool wasExpanded = _linExpandedFrames.Contains(flat.FrameIndex);
                    if (wasExpanded) _linExpandedFrames.Remove(flat.FrameIndex);
                    else _linExpandedFrames.Add(flat.FrameIndex);
                    _dgvFrames.SuspendLayout();
                    if (wasExpanded)
                    {
                        // 折叠：删除该帧全部信号子行
                        for (int i = _linFlatRows.Count - 1; i >= 0; i--)
                            if (_linFlatRows[i].Type == LinRowType.Signal && _linFlatRows[i].FrameIndex == flat.FrameIndex)
                                _linFlatRows.RemoveAt(i);
                    }
                    else
                    {
                        // 展开：仅在点击帧行后插入信号行
                        int insertPos = rowIndex + 1;
                        if (rowIndex < _linFlatRows.Count && _linFlatRows[rowIndex].FrameIndex == flat.FrameIndex)
                        {
                            var f = _frames[flat.FrameIndex];
                            var ldf = GetLdfForChannel(flat.Channel);
                            List<LinFrameSignal> sigs;
                            if (ldf != null && ldf.FrameSignals.TryGetValue(flat.Pid, out sigs) && sigs.Count > 0)
                            {
                                var rows = new List<LinFlatRow>(sigs.Count);
                                foreach (var fs in sigs)
                                    rows.Add(new LinFlatRow { Type = LinRowType.Signal, FrameIndex = flat.FrameIndex, Pid = flat.Pid, Channel = flat.Channel, SignalName = fs.SignalName, SigOffset = fs.Offset });
                                _linFlatRows.InsertRange(insertPos, rows);
                            }
                        }
                    }
                    _dgvFrames.RowCount = _linFlatRows.Count;
                    _dgvFrames.ResumeLayout();
                    _dgvFrames.Invalidate();
                }
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
            if (e.RowIndex < 0 || e.RowIndex >= _linFlatRows.Count) return;
            var flat = _linFlatRows[e.RowIndex];
            if (flat.Type == LinRowType.Signal)
            {
                // 信号行浅灰底（与从节点页签展开行一致）
                e.CellStyle.BackColor = Color.FromArgb(245, 245, 248);
                return;
            }
            LinFrameRecord f;
            lock (_frames)
            {
                if (flat.FixedIndex >= 0)
                {
                    if (flat.FixedIndex >= _linFixedList.Count) return;
                    f = _linFixedList[flat.FixedIndex].Last;
                }
                else
                {
                    if (flat.FrameIndex < 0 || flat.FrameIndex >= _frames.Count) return;
                    f = _frames[flat.FrameIndex];
                }
            }
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
