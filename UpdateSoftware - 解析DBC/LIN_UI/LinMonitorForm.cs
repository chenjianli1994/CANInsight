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

        // 从节点页签
        private DataGridView _dgvResp;
        /// <summary>从节点页签已展开（信号解析）的帧 PID 集合</summary>
        private readonly HashSet<byte> _expandedResp = new HashSet<byte>();

        // 信号页签
        private DataGridView _dgvSignals;

        // 发送页签（自定义报文定义表）
        private DataGridView _dgvSend;
        private ToolStrip _sendToolbar;
        private bool _suppressSendWriteback;
        private readonly HashSet<LinTransmitEntry> _expandedSend = new HashSet<LinTransmitEntry>();

        private sealed class SendSignalRow
        {
            public LinTransmitEntry Entry;
            public string SignalName;
            public ushort Offset;
        }

        // ==================== 数据 ====================
        // 帧日志（Scroll 数据源）：接收线程 O(1) 追加，UI 线程按 100ms 节流重建（与 CAN 报文窗口同架构）
        private readonly List<LinFrameRecord> _frames = new List<LinFrameRecord>();
        private long _errorCount;
        private volatile bool _paused;
        private byte _channel;          // 当前监控通道（逻辑号）
        /// <summary>本窗体已获取的调度器（值取自 Lin_API 通道唯一注册表；重复打开/多窗体共享同一实例，关闭时统一释放）</summary>
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
            /// <summary>计划行（方案 3）：启用报文的固定行，状态来自运行快照，数据合并真实帧/配置</summary>
            public bool IsPlanRow;
        }
        private readonly List<LinFlatRow> _linFlatRows = new List<LinFlatRow>();
        private readonly HashSet<long> _linExpandedKeys = new HashSet<long>();   // Fixed：按 (ch,pid) 复合键
        /// <summary>当前计划行键（重建时填充；真实帧聚合跳过这些 PID 避免重复行）</summary>
        private readonly HashSet<long> _linPlanKeys = new HashSet<long>();
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
            AppLog.Write("[LIN-UI] LinMonitorForm 打开");
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
            Lin_API.LinkLostFinal += OnLinkLostFinal;
            Lin_API.ChannelStateChanged += OnChannelStateChanged;
            Lin_API.LinTxStateChanged += OnLinTxStateChanged;

            InitChannelView();
            FormClosing += (s, e) =>
            {
                _disposed = true;
                AppLog.Write("[LIN-UI] LinMonitorForm 关闭");
                _uiTimer.Stop();
                // 通道唯一调度器：随窗体关闭统一释放（重复开关窗口不残留定时器/事件订阅）
                // 逐通道释放：注册表幂等（未注册/已释放直接返回），同一通道多窗体共享同一实例也只释放一次
                foreach (byte ch in _schedulers.Keys.ToArray())
                {
                    _schedulers.Remove(ch);
                    Lin_API.ReleaseScheduler(ch, "窗体关闭");
                }
                Lin_API.LinFrameReceived -= OnFrameReceived;
                Lin_API.BusEvent -= OnBusEvent;
                Lin_API.LinkLost -= OnLinkLost;
                Lin_API.LinkLostFinal -= OnLinkLostFinal;
                Lin_API.ChannelStateChanged -= OnChannelStateChanged;
                Lin_API.LinTxStateChanged -= OnLinTxStateChanged;
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
                AppLog.Write("[LIN-UI] LIN 监控打开通道管理对话框");
                using (var dlg = new ChannelManagerForm(PCAN_Client.Main.main))
                {
                    dlg.SelectLinTab();
                    dlg.ShowDialog(this);
                }
                AppLog.Write("[LIN-UI] 通道管理对话框已关闭");
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
            _btnWakeUp.Click += (s, e) =>
            {
                // 硬件模式按通道 Mode：Master 下 PCAN 的 XmtWakeUp 官方契约仅 Slave 模式适用，
                // 硬件层门禁 ShouldUseXmtWakeUp 会拒绝并记 [WAKE] 日志；Vector 的 XL_LinWakeUp 不分模式直接执行。
                if (!Lin_API.WakeUp(_channel)) ShowError("唤醒失败（未连接或当前适配器模式不支持唤醒）");
            };
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
                    EmptyFrameHint(),
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
            _dgvFrames.Columns.Add("colErr", "错误");
            _dgvFrames.Columns["colErr"].Width = 40;
            _dgvFrames.Columns["colErr"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvFrames.Columns["colErr"].SortMode = DataGridViewColumnSortMode.NotSortable;
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
            // 阶段 3（方案 §4.3 统计口径）：计划行专用统计列——不显示会话绝对时间、
            // 不伪造 Rx；提交次数/真实Rx/实测周期/错误次数/最后错误分别成列，不再用 LinFixedInfo.Count 兼任多义。
            _dgvFrames.Columns.Add("colSubmit", "提交次数");
            _dgvFrames.Columns["colSubmit"].Width = 70;
            _dgvFrames.Columns["colSubmit"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvFrames.Columns.Add("colBusRx", "真实Rx");
            _dgvFrames.Columns["colBusRx"].Width = 60;
            _dgvFrames.Columns["colBusRx"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvFrames.Columns.Add("colPeriod", "实测周期(ms)");
            _dgvFrames.Columns["colPeriod"].Width = 80;
            _dgvFrames.Columns["colPeriod"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvFrames.Columns.Add("colErrCnt", "错误次数");
            _dgvFrames.Columns["colErrCnt"].Width = 70;
            _dgvFrames.Columns["colErrCnt"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvFrames.Columns.Add("colLastErr", "最后错误");
            _dgvFrames.Columns["colLastErr"].Width = 140;
            Controls.Add(_dgvFrames);
        }

        private void BuildTabs()
        {
            _tabLin = new TabControl { Dock = DockStyle.Fill };

            _tabLin.TabPages.Add("从节点 / 发布");
            _tabLin.TabPages.Add("信号 (LDF)");
            _tabLin.TabPages.Add("发送");













































































            // ---- 从节点/发布页签：LDF 全量报文目录 ----
            var respPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _dgvResp = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };
            UiTheme.StyleGrid(_dgvResp);
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspExpand", HeaderText = "", Width = 30, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspId", HeaderText = "帧 ID", Width = 70, ReadOnly = true
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspName", HeaderText = "帧名称", Width = 230, ReadOnly = true
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspPublisher", HeaderText = "发布节点", Width = 150, ReadOnly = true
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspType", HeaderText = "帧类型", Width = 110, ReadOnly = true
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspDlc", HeaderText = "DLC", Width = 50, ReadOnly = true
            });
            _dgvResp.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colRspAdded", HeaderText = "发送页", Width = 80, ReadOnly = true
            });
            var colRspAdd = new DataGridViewButtonColumn
            {
                Name = "colRspAdd", HeaderText = "操作", Width = 80, ReadOnly = true,
                FlatStyle = FlatStyle.Flat, UseColumnTextForButtonValue = false
            };
            _dgvResp.Columns.Add(colRspAdd);
            _dgvResp.CellContentClick += DgvResp_CellContentClick;
            _dgvResp.CellDoubleClick += DgvResp_CellDoubleClick;
            respPanel.Controls.Add(_dgvResp);
            var respHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 40,
                Text = "这里显示当前 LDF 的全部报文。点击“添加”把报文加入发送页并自动生成调度槽；“＋”或双击行可展开 LDF 信号定义。Slave 报文只有在外部 Master 发 Header 时才会出现在报文流中。",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            respPanel.Controls.Add(respHint);
            _tabLin.TabPages[0].Controls.Add(respPanel);

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
            _tabLin.TabPages[1].Controls.Add(sigPanel);

            // ---- 发送页签：每条报文一个发送项（类型同时用于硬件配置和调度）----
            var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _sendToolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            _sendToolbar.Items.Add(new ToolStripButton("添加报文", ToolbarIcons.Get("plus")) { Tag = "add", ToolTipText = "新增发送项（默认 Master、PID 0x00、数据全 0）" });
            _sendToolbar.Items.Add(new ToolStripButton("删除报文", ToolbarIcons.Get("clear")) { Tag = "del", ToolTipText = "删除选中发送项" });
            _sendToolbar.Items.Add(new ToolStripSeparator());
            _sendToolbar.Items.Add(new ToolStripButton("从 LDF 导入", ToolbarIcons.Get("dbc")) { Tag = "import", ToolTipText = "按 LDF 调度表自动填充发送项（帧ID/名称/时隙），默认全部勾选启用" });
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
            _dgvSend.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colSendExpand", HeaderText = "", Width = 30, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            var colSendEn = new DataGridViewCheckBoxColumn { Name = "colSendEn", HeaderText = "启用", Width = 46, ThreeState = false };
            _dgvSend.Columns.Add(colSendEn);
            _dgvSend.Columns.Add("colSendPid", "帧 ID");
            _dgvSend.Columns["colSendPid"].Width = 80;
            _dgvSend.Columns.Add("colSendName", "帧名称");
            _dgvSend.Columns["colSendName"].Width = 240;
            _dgvSend.Columns["colSendName"].ReadOnly = true;
            var colSendType = new DataGridViewComboBoxColumn
            {
                Name = "colSendType",
                HeaderText = "发送类型",
                Width = 130,
                FlatStyle = FlatStyle.Flat,
            };
            colSendType.Items.AddRange(new object[]
            {
                TransmitTypeText(LinTransmitType.Master),
                TransmitTypeText(LinTransmitType.Slave),
                TransmitTypeText(LinTransmitType.HeaderOnly),
                TransmitTypeText(LinTransmitType.BreakOnly),
            });
            _dgvSend.Columns.Add(colSendType);
            _dgvSend.Columns.Add("colSendDlc", "DLC");
            _dgvSend.Columns["colSendDlc"].Width = 50;
            _dgvSend.Columns["colSendDlc"].ReadOnly = false;
            _dgvSend.Columns.Add("colSendSlotMs", "时隙 (ms)");
            _dgvSend.Columns["colSendSlotMs"].Width = 85;
            _dgvSend.Columns["colSendSlotMs"].ReadOnly = false;
            _dgvSend.Columns.Add("colSendData", "数据 (Hex)");
            _dgvSend.Columns["colSendData"].Width = 250;
            _dgvSend.CellContentClick += DgvSend_CellContentClick;
            _dgvSend.CellDoubleClick += DgvSend_CellDoubleClick;
            _dgvSend.CellBeginEdit += DgvSend_CellBeginEdit;
            _dgvSend.CellValueChanged += DgvSend_CellValueChanged;
            // 单元格值格式化/转换失败时静默跳过，不弹 DataGridView 默认错误对话框
            // （如 CheckBox 列被旧数据赋了非 bool 值）。
            _dgvSend.DataError += (s, e) => { e.ThrowException = false; };
            _dgvSend.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvSend.IsCurrentCellDirty) _dgvSend.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _dgvSend.EditingControlShowing += DgvSend_EditingControlShowing;
            sendPanel.Controls.Add(_dgvSend);
            var sendHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 90,
                Text = "发送项独立选择 Master / Slave / HeaderOnly / BreakOnly；Slave 只等待外部 Master Header，不会自行产生报文。添加 LDF 报文默认按 Master/HeaderOnly 处理，需明确选择 Slave 才配置本机响应；“＋”或双击可编辑信号。\r\n" +
                    "发送类型在「启用」勾选期间锁定（正在发送不允许改模式）：改类型请先取消勾选，改完再勾选。展开帧后可直接改信号值：枚举信号下拉选择，物理量输入数值（单位自动带出、不可编辑）。\r\n" +
                    "通道硬件模式：Slave = 只监听 + 按 Slave 项应答（不发 Header，Master/HeaderOnly 无法启用）；Master = 本机发 Header/跑调度（只能看到本机请求的帧）。",
                ForeColor = Color.Gray,
                Font = UiTheme.UiFont,
            };
            sendPanel.Controls.Add(sendHint);
            // toolbar 最后 Add（Dock=Top 布局须在 Fill 之后处理）
            sendPanel.Controls.Add(_sendToolbar);
            _sendToolbar.Dock = DockStyle.Top;
            _tabLin.TabPages[2].Controls.Add(sendPanel);
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
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 320,
            };
            split.Panel1.Controls.Add(_dgvFrames);
            split.Panel2.Controls.Add(_tabLin);
            // Dock 布局顺序（实测最小复现）：Fill 容器必须先于 Top/Bottom 条带加入——
            // 条带先加入时 Fill 分栏会占满全客户区、工具栏/状态栏叠画其上，报文表头被工具栏盖住。
            // 其余窗体（AnalysisTypeSelector 等）均为「Fill 先添加、条带后添加」，与之一致。
            Controls.Remove(_toolStripLin);
            Controls.Remove(_statusStrip);
            Controls.Add(split);
            Controls.Add(_toolStripLin);
            Controls.Add(_statusStrip);
            _toolStripLin.Dock = DockStyle.Top;
            _statusStrip.Dock = DockStyle.Bottom;
            // SplitterDistance 须在窗体尺寸确定后设置（构造时 ClientSize 未知会按比例失真）
            this.Shown += (s, e) => { try { split.SplitterDistance = 320; } catch { } };
        }

        // ==================== 通道选择/连接 ====================

        private LinChannel CurrentChannel
        {
            get
            {
                return _channel >= 1 && _channel <= LinConfig.Channels.Count
                    ? LinConfig.Channels[_channel - 1]
                    : null;
            }
        }

        private static string TransmitTypeText(LinTransmitType type)
        {
            switch (type)
            {
                case LinTransmitType.Master: return "Master（发布）";
                case LinTransmitType.Slave: return "Slave（响应）";
                case LinTransmitType.HeaderOnly: return "HeaderOnly（仅头）";
                case LinTransmitType.BreakOnly: return "BreakOnly（不支持）";
                default: return type.ToString();
            }
        }

        private static LinTransmitType ParseTransmitType(object value)
        {
            string text = (value ?? "").ToString();
            if (text.StartsWith("Slave", StringComparison.OrdinalIgnoreCase)) return LinTransmitType.Slave;
            if (text.StartsWith("HeaderOnly", StringComparison.OrdinalIgnoreCase)) return LinTransmitType.HeaderOnly;
            if (text.StartsWith("BreakOnly", StringComparison.OrdinalIgnoreCase)) return LinTransmitType.BreakOnly;
            return LinTransmitType.Master;
        }

        private static byte EntryDlc(LinTransmitEntry entry, LinLdfFile ldf)
        {
            if (entry != null && entry.Dlc <= 8 && entry.Dlc > 0) return entry.Dlc;
            if (entry != null && ldf != null && ldf.Frames.ContainsKey(entry.Pid))
            {
                byte dlc = ldf.Frames[entry.Pid].Dlc;
                if (dlc > 0 && dlc <= 8) return dlc;
            }
            return 8;
        }

        private static byte[] EntryData(LinTransmitEntry entry, byte dlc)
        {
            byte[] data = entry == null || entry.Data == null ? new byte[dlc] : (byte[])entry.Data.Clone();
            if (data.Length != dlc) Array.Resize(ref data, dlc);
            return data;
        }

        /// <summary>当前通道是否为 Slave 硬件模式（不发 Header，只监听+应答外部 Header）</summary>
        private bool IsSlaveHardwareMode()
        {
            var ch = CurrentChannel;
            return ch != null && ch.GetHardwareMode() == LinNodeMode.Slave;
        }

        /// <summary>发送页默认发送类型（纯函数，便于测试）：按通道硬件模式分支</summary>
        internal static LinTransmitType DefaultTransmitTypeFor(LinLdfFile ldf, byte pid, LinNodeMode hardwareMode)
        {
            // Slave 硬件模式的通道本机不发 Header：从节点发布帧默认配 Slave（本机应答），
            // 主节点发布帧本机不参与（由外部主节点自己发，本机配 Slave 会与其发布抢答）。
            if (hardwareMode == LinNodeMode.Slave && !LinLdfHelper.IsMasterPublisherFrame(ldf, pid)) return LinTransmitType.Slave;
            if (LinLdfHelper.IsMasterPublisherFrame(ldf, pid)) return LinTransmitType.Master;
            // LDF 只描述总线上的发布者，不代表该发布者就是本工具。
            // 添加一个从节点发布帧时，默认应由本机 Master 发送 Header 请求响应；
            // 只有用户在发送页明确改成 Slave，才把该帧配置为本机自动响应。
            return LinTransmitType.HeaderOnly;
        }

        private LinTransmitType DefaultTransmitType(LinLdfFile ldf, byte pid)
        {
            var ch = CurrentChannel;
            return DefaultTransmitTypeFor(ldf, pid, ch != null ? ch.GetHardwareMode() : LinNodeMode.Master);
        }

        private LinTransmitEntry CreateTransmitEntry(byte pid)
        {
            var ldf = GetLdf();
            byte dlc = 8;
            LinFrameDef def;
            if (ldf != null && ldf.Frames.TryGetValue(pid, out def) && def.Dlc > 0 && def.Dlc <= 8)
                dlc = def.Dlc;
            LinTransmitType type = DefaultTransmitType(ldf, pid);
            return new LinTransmitEntry
            {
                Pid = pid,
                Type = type,
                Enabled = true,
                Dlc = dlc,
                Data = new byte[dlc],
                SlotMs = 15,
            };
        }

        private LinTransmitEntry FindSendEntry(byte pid)
        {
            var ch = CurrentChannel;
            if (ch == null || ch.TransmitEntries == null) return null;
            return ch.FindTransmitEntry(pid);
        }

        private void SyncSchedulerFromConfig()
        {
            var ch = CurrentChannel;
            if (ch == null) return;
            if (ch.TransmitEntries == null) ch.TransmitEntries = new List<LinTransmitEntry>();
            var sc = GetScheduler();
            while (sc.Slots.Count > ch.TransmitEntries.Count) sc.Slots.RemoveAt(sc.Slots.Count - 1);
            while (sc.Slots.Count < ch.TransmitEntries.Count) sc.Slots.Add(new LinScheduleSlot());
            for (int i = 0; i < ch.TransmitEntries.Count; i++)
            {
                var entry = ch.TransmitEntries[i];
                if (entry == null)
                {
                    entry = new LinTransmitEntry { Pid = 0, Dlc = 8, Data = new byte[8], SlotMs = 15 };
                    ch.TransmitEntries[i] = entry;
                }
                if (entry.SlotMs <= 0) entry.SlotMs = 15;
                var slot = sc.Slots[i];
                slot.Enabled = entry.Enabled;
                slot.Pid = entry.Pid;
                slot.TransmitType = entry.Type;
                slot.SlotMs = entry.SlotMs;
            }
        }

        /// <summary>发送项类型锁定判定：勾选启用（正在发送）期间不允许更改发送模式，
        /// 下拉置灰；取消勾选（停止发送）后才能修改。锁定与连接状态无关（勾选即发模型）。</summary>
        private static bool IsTypeLocked(LinTransmitEntry entry)
        {
            return entry != null && entry.Enabled;
        }

        /// <summary>发送类型单元格置灰样式（勾选启用期间锁定，对齐"正在发送不允许改模式"）</summary>
        private void ApplySendTypeLockStyles()
        {
            for (int i = 0; i < _dgvSend.Rows.Count; i++)
            {
                var entry = _dgvSend.Rows[i].Tag as LinTransmitEntry;
                bool locked = IsTypeLocked(entry);
                var cell = _dgvSend.Rows[i].Cells["colSendType"];
                cell.ReadOnly = locked;
                cell.Style.BackColor = locked ? Color.FromArgb(235, 235, 235) : Color.Empty;
            }
        }

        /// <summary>类型单元格编辑拦截：勾选启用（正在发送）期间禁止进入下拉编辑</summary>
        private void DgvSend_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || _dgvSend.Columns[e.ColumnIndex].Name != "colSendType") return;
            var entry = _dgvSend.Rows[e.RowIndex].Tag as LinTransmitEntry;
            if (!IsTypeLocked(entry)) return;
            e.Cancel = true;
            ShowError("该报文正在发送，取消勾选停止发送后才能更改发送模式");
        }

        /// <summary>初始化页签操作通道（报文表显示全部通道，按「通道」列区分；页签操作第一个已配置通道）</summary>
        private void InitChannelView()
        {
            _channel = LinConfig.Channels.Count > 0 ? (byte)1 : (byte)0;
            _lblBaud.Text = LinConfig.Channels.Count > 0
                ? "LIN 通道 " + LinConfig.Channels.Count + " 路 · 波特率 " + LinConfig.Channels[0].Baudrate
                : "未配置 LIN 通道";
            SyncSchedulerSlots();
            RefreshRespGrid();
            RefreshSignalGrid();
            RefreshSendGrid();
        }

        private void LoadLdf()
        {
            if (_channel < 1 || _channel > LinConfig.Channels.Count) return;
            using (var dlg = new OpenFileDialog { Filter = "LIN 描述文件 (*.ldf)|*.ldf|LIN 描述文件 (*.lin)|*.lin|所有文件 (*.*)|*.*", Title = "选择 LDF 文件" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var ch = LinConfig.Channels[_channel - 1];
                    try
                    {
                        var ldf = LinLdfHelper.Parse(dlg.FileName);
                        ch.LdfHelper = ldf;
                        ch.LdfPath = dlg.FileName;
                        if (Lin_API.IsConnected(_channel))
                        {
                            Lin_API.LinDisconnect(_channel);
                            ch.ConnectError = "LDF 已修改，请重新连接";
                        }
                        LinConfig.SaveLinConfig();
                        SyncSchedulerSlots();
                        RefreshRespGrid();
                        RefreshSignalGrid();
                        RefreshSendGrid();
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
                    // Fixed：计划行（启用项）在前 + 真实帧聚合行（跳过计划 PID 避免重复；行数 ≤ 128，重建极快）
                    if (!_linFlatRowsDirty && _linFlatRows.Count > 0) return;
                    _linFixedList.Sort((a, b) => a.Pid != b.Pid ? a.Pid.CompareTo(b.Pid) : a.Channel.CompareTo(b.Channel));
                    _linFlatRows.Clear();
                    // 计划行（方案 3）：启用项立即出现；无真实帧时计数 0、数据列配置数据或 "--"，不伪造 Rx
                    _linPlanKeys.Clear();
                    var planList = Lin_API.GetEnabledRunSnapshots(_channel);
                    foreach (var snap in planList)
                    {
                        if (!LinPassesFilter(snap.Pid)) continue;
                        _linPlanKeys.Add(LinMsgKey(snap.Pid, snap.LogicChannel));
                        _linFlatRows.Add(new LinFlatRow { Type = LinRowType.Frame, IsPlanRow = true, Pid = snap.Pid, Channel = snap.LogicChannel });
                        // 计划行同样可展开解析信号（数据取最近真实总线帧；无帧时用本机配置数据，不伪造 Rx）
                        if (_linExpandedKeys.Contains(LinMsgKey(snap.Pid, snap.LogicChannel)))
                            AppendLinSignalRows(PlanRowFrame(snap.Pid, snap.LogicChannel), -1);
                    }
                    for (int i = 0; i < _linFixedList.Count; i++)
                    {
                        var info = _linFixedList[i];
                        if (_linPlanKeys.Contains(LinMsgKey(info.Pid, info.Channel))) continue; // 计划行已承载
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

        /// <summary>计划行的信号解析数据源：优先最近真实总线帧，无帧时用本机配置数据（不伪造 Rx）</summary>
        private LinFrameRecord PlanRowFrame(byte pid, byte channel)
        {
            LinFixedInfo info;
            lock (_frames)
            {
                if (_linMsgIndexMap.TryGetValue(LinMsgKey(pid, channel), out info)) return info.Last;
            }
            var entry = GetPlanEntry(pid);
            var ldf = GetLdfForChannel(channel);
            byte dlc = entry != null ? EntryDlc(entry, ldf) : (byte)0;
            return new LinFrameRecord
            {
                LogicChannel = channel,
                Pid = pid,
                Direction = LinFrameDir.Tx,
                Dlc = dlc,
                Data = entry != null && entry.Data != null ? (byte[])entry.Data.Clone() : new byte[0],
                FrameName = LinLdfHelper.GetFrameName(ldf, pid),
            };
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

        /// <summary>取扁平行对应的帧记录（Fixed 取聚合最新帧，Scroll 取帧日志）；失败返回 false（须在 _frames 锁内调用）</summary>
        private bool TryGetLinFlatFrame(LinFlatRow flat, out LinFrameRecord f)
        {
            f = default(LinFrameRecord);
            if (flat.FixedIndex >= 0)
            {
                if (flat.FixedIndex >= _linFixedList.Count) return false;
                f = _linFixedList[flat.FixedIndex].Last;
                return true;
            }
            if (flat.FrameIndex < 0 || flat.FrameIndex >= _frames.Count) return false;
            f = _frames[flat.FrameIndex];
            return true;
        }

        private void DgvFrames_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _linFlatRows.Count) return;
            var flat = _linFlatRows[e.RowIndex];
            string col = _dgvFrames.Columns[e.ColumnIndex].Name;
            if (flat.Type == LinRowType.Frame)
            {
                if (flat.IsPlanRow) { FillPlanRowValue(e, flat); return; } // 计划行：状态/数据来自快照+配置，不伪造 Rx
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
                    case "colErr": e.Value = ""; break; // 指示灯由 CellPainting 绘制
                    case "colId": e.Value = "0x" + f.Pid.ToString("X2"); break;
                    case "colName": e.Value = f.FrameName; break;
                    case "colType": e.Value = FrameTypeText(f.FrameType); break;
                    case "colDlc": e.Value = f.Dlc.ToString(); break;
                    case "colData": e.Value = f.DataHex; break;
                    case "colCs":
                        e.Value = f.Dlc == 0 && f.ErrorKind == LinErrorKind.None
                            ? "—"
                            : f.ErrorKind == LinErrorKind.Checksum ? "0x" + f.ChecksumRx.ToString("X2") + "*" : "0x" + f.ChecksumRx.ToString("X2");
                        break;
                    case "colStatus": e.Value = f.StatusText; break;
                    // 阶段 3 统计列只对计划行有语义；真实帧行/聚合行显式空（VirtualMode 防残留旧值）
                    case "colSubmit": e.Value = ""; break;
                    case "colBusRx": e.Value = ""; break;
                    case "colPeriod": e.Value = ""; break;
                    case "colErrCnt": e.Value = ""; break;
                    case "colLastErr": e.Value = ""; break;
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
                        // 计划行/聚合行：优先最近真实总线帧，无帧时用本机配置数据（不伪造 Rx）
                        f = _linMsgIndexMap.TryGetValue(LinMsgKey(flat.Pid, flat.Channel), out info)
                            ? info.Last
                            : PlanRowFrame(flat.Pid, flat.Channel);
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
                    case "colSubmit": case "colBusRx": case "colPeriod": case "colErrCnt": case "colLastErr":
                        e.Value = ""; break;
                    default: e.Value = ""; break;
                }
            }
        }

        /// <summary>展开列绘制 ＋/－（仅该帧有 LDF 信号定义时显示）；错误列绘制红灯（校验和/同步/无应答/硬件错误），对齐 CAN 报文窗口</summary>
        /// <summary>计划行单元格（方案 3）：状态来自运行快照；数据列优先真实帧，无帧用配置数据或 "--"，不伪造 Rx</summary>
        private void FillPlanRowValue(DataGridViewCellValueEventArgs e, LinFlatRow flat)
        {
            string col = _dgvFrames.Columns[e.ColumnIndex].Name;
            LinRunSnapshot snap = null;
            foreach (var s in Lin_API.GetEnabledRunSnapshots(flat.Channel))
                if (s.Pid == flat.Pid) { snap = s; break; }
            if (snap == null) { e.Value = ""; return; }
            LinFixedInfo info = null;
            lock (_frames) { _linMsgIndexMap.TryGetValue(LinMsgKey(flat.Pid, flat.Channel), out info); }
            switch (col)
            {
                case "colExpand": e.Value = ""; break;
                // 方案 §4.3：计划行“次数/时间”不再兼任多义——次数=总线帧观察数（真实 Tx+Rx 观测），
                // 时间列不显示会话绝对时间（"--"，反模式门禁）；提交次数/真实Rx/实测周期/错误次数独立成列。
                case "colCount": e.Value = (info != null ? info.Count : 0u).ToString(); break;
                case "colTime": e.Value = "--"; break;
                case "colCh": e.Value = "CH" + flat.Channel; break;
                case "colDir":
                    e.Value = info != null ? (info.Last.Direction == LinFrameDir.Tx ? "Tx" : "Rx") : "—";
                    break;
                case "colErr": e.Value = ""; break; // 指示灯由 CellPainting 按快照 IsError 绘制
                case "colId": e.Value = "0x" + flat.Pid.ToString("X2"); break;
                case "colName": e.Value = LinLdfHelper.GetFrameName(GetLdfForChannel(flat.Channel), flat.Pid); break;
                case "colType":
                {
                    var ldf = GetLdfForChannel(flat.Channel);
                    LinFrameDef def = null;
                    if (ldf != null) ldf.Frames.TryGetValue(flat.Pid, out def);
                    e.Value = def != null ? FrameTypeText(def.FrameType) : "—";
                    break;
                }
                case "colDlc":
                    e.Value = info != null ? info.Last.Dlc.ToString() : GetPlanDlc(flat).ToString();
                    break;
                case "colData":
                    // NoResponse 是无应答合成标记（无总线数据）：数据列回退配置数据/--，不伪装真实 Rx
                    e.Value = info != null && info.Last.ErrorKind == LinErrorKind.NoResponse
                        ? (GetPlanDataHex(flat) ?? "--")
                        : info != null ? info.Last.DataHex : (GetPlanDataHex(flat) ?? "--");
                    break;
                case "colCs":
                    e.Value = info != null
                        ? (info.Last.ErrorKind == LinErrorKind.Checksum ? "0x" + info.Last.ChecksumRx.ToString("X2") + "*" : "0x" + info.Last.ChecksumRx.ToString("X2"))
                        : "--";
                    break;
                case "colStatus": e.Value = SnapshotStateText(snap); break;
                // 阶段 3 统计列（方案 §4.3）：
                // colSubmit=提交次数、colBusRx=真实 Rx、colPeriod=实测周期、colErrCnt=错误次数、colLastErr=最后错误
                case "colSubmit": e.Value = snap.SubmittedCount.ToString(); break;
                case "colBusRx": e.Value = snap.BusFrameCount.ToString(); break;
                case "colPeriod": e.Value = snap.MeasuredPeriodMs > 0 ? snap.MeasuredPeriodMs.ToString() : "--"; break;
                case "colErrCnt": e.Value = snap.ErrorCount.ToString(); break;
                case "colLastErr": e.Value = snap.ErrorText.Length > 0 ? snap.ErrorText : "--"; break;
            }
        }

        private LinTransmitEntry GetPlanEntry(byte pid)
        {
            var ch = CurrentChannel;
            return ch == null ? null : ch.FindTransmitEntry(pid);
        }

        private byte GetPlanDlc(LinFlatRow flat)
        {
            var entry = GetPlanEntry(flat.Pid);
            if (entry != null && entry.Dlc > 0 && entry.Dlc <= 8) return entry.Dlc;
            var ldf = GetLdfForChannel(flat.Channel);
            if (ldf != null)
            {
                byte d = LinLdfHelper.GetFrameDlc(ldf, flat.Pid);
                if (d > 0 && d <= 8) return d;
            }
            return 8;
        }

        private string GetPlanDataHex(LinFlatRow flat)
        {
            var entry = GetPlanEntry(flat.Pid);
            if (entry == null || entry.Data == null || entry.Data.Length == 0) return null;
            return BitConverter.ToString(entry.Data).Replace("-", " ");
        }

        private static string SnapshotStateText(LinRunSnapshot snap)
        {
            switch (snap.State)
            {
                case LinRunStateKind.Armed: return "已启用";
                case LinRunStateKind.TxSubmitted: return "已提交";
                case LinRunStateKind.WaitingResponse: return "等待应答";
                case LinRunStateKind.WaitingExternalHeader: return "等待外部Header";
                case LinRunStateKind.Completed: return snap.Type == LinTransmitType.Master ? "已发送" : "应答完成";
                case LinRunStateKind.NoResponse: return "错误·无应答";
                case LinRunStateKind.Error:
                    switch (snap.ErrorKind)
                    {
                        case LinErrorKind.Checksum: return "错误·校验和";
                        case LinErrorKind.Sync: return "错误·同步";
                        case LinErrorKind.NoResponse: return "错误·无应答";
                        case LinErrorKind.Hw: return "错误·硬件";
                        default: return "错误";
                    }
                case LinRunStateKind.Unsupported: return "不支持";
                case LinRunStateKind.Disabled: return "已停用";
                default: return snap.State.ToString();
            }
        }

        private void DgvFrames_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || e.RowIndex >= _linFlatRows.Count) return;
                var flat = _linFlatRows[e.RowIndex];
                string colName = _dgvFrames.Columns[e.ColumnIndex].Name;

                if (colName == "colExpand")
                {
                    if (flat.Type != LinRowType.Frame) return;
                    if (!LinFrameHasSignals(flat.Pid, flat.Channel)) return; // 无信号定义不画按钮
                    e.Handled = true;
                    PaintLinCellSurface(e);
                    // 计划行与聚合行共用聚合键展开；Scroll 逐帧行用帧索引
                    bool isExpanded = flat.IsPlanRow || flat.FixedIndex >= 0
                        ? _linExpandedKeys.Contains(LinMsgKey(flat.Pid, flat.Channel))
                        : _linExpandedFrames.Contains(flat.FrameIndex);
                    using (var btnFont = new Font("Arial", 10f, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.DrawString(isExpanded ? "−" : "+", btnFont, brush, e.CellBounds, sf);
                    }
                    return;
                }

                if (colName == "colErr")
                {
                    if (flat.Type != LinRowType.Frame) return;
                    if (flat.IsPlanRow)
                    {
                        // 计划行红灯只由运行快照驱动（NoResponse/Error/Unsupported）；等待/启用/成功态不点红
                        LinRunSnapshot snap = null;
                        foreach (var s in Lin_API.GetEnabledRunSnapshots(flat.Channel))
                            if (s.Pid == flat.Pid) { snap = s; break; }
                        if (snap == null || !snap.IsError) return;
                        e.Handled = true;
                        PaintLinCellSurface(e);
                        const int d2 = 12;
                        var rc2 = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - d2) / 2,
                            e.CellBounds.Y + (e.CellBounds.Height - d2) / 2, d2, d2);
                        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using (var red = new SolidBrush(Color.FromArgb(226, 68, 54)))
                            e.Graphics.FillEllipse(red, rc2);
                        using (var hl = new SolidBrush(Color.FromArgb(255, 140, 120)))
                            e.Graphics.FillEllipse(hl, rc2.X + rc2.Width / 4, rc2.Y + rc2.Height / 4, rc2.Width / 2, rc2.Height / 2);
                        return;
                    }
                    LinFrameRecord f;
                    lock (_frames)
                    {
                        if (!TryGetLinFlatFrame(flat, out f)) return;
                    }
                    if (f.ErrorKind == LinErrorKind.None) return; // 正常帧：默认绘制
                    // 错误帧：红灯（中心高光，指示灯质感）
                    e.Handled = true;
                    PaintLinCellSurface(e);
                    const int d = 12;
                    var rc = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - d) / 2,
                        e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var red = new SolidBrush(Color.FromArgb(226, 68, 54)))
                        e.Graphics.FillEllipse(red, rc);
                    using (var hl = new SolidBrush(Color.FromArgb(255, 140, 120)))
                        e.Graphics.FillEllipse(hl, rc.X + rc.Width / 4, rc.Y + rc.Height / 4, rc.Width / 2, rc.Height / 2);
                    return;
                }
            }
            catch { /* 防御：绘制异常不中断 */ }
        }

        /// <summary>自绘单元格使用与 DataGridView 相同的网格色，避免自绘格出现黑色边框</summary>
        private void PaintLinCellSurface(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color backColor = selected
                ? (e.CellStyle.SelectionBackColor.IsEmpty ? _dgvFrames.DefaultCellStyle.SelectionBackColor : e.CellStyle.SelectionBackColor)
                : (e.CellStyle.BackColor.IsEmpty ? _dgvFrames.DefaultCellStyle.BackColor : e.CellStyle.BackColor);
            using (var background = new SolidBrush(backColor))
                e.Graphics.FillRectangle(background, e.CellBounds);
            using (var border = new Pen(_dgvFrames.GridColor))
                e.Graphics.DrawRectangle(border, e.CellBounds.X, e.CellBounds.Y,
                    Math.Max(0, e.CellBounds.Width - 1), Math.Max(0, e.CellBounds.Height - 1));
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

            if (flat.IsPlanRow || flat.FixedIndex >= 0)
            {
                // 计划行/聚合行：聚合键展开（重建，行数 ≤ 128 极快；绕过暂停门）
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
            // 窗体侧缓存命中的实例可能已被 LinDisconnect 同步释放（阶段 1 审查 F1 闭合），
            // 已释放实例不可复用：缓存清除后经注册表重建（重连/下一次 sync 会拿到新实例）。
            if (!_schedulers.TryGetValue(_channel, out sc) || sc.IsDisposed)
            {
                if (sc != null) _schedulers.Remove(_channel);
                // PEAK 硬件调度表（SetSchedule/StartSchedule）在当前 PLIN Manager/Pro FD 环境全部
                // errUnknown（官方签名实测），统一走软件调度（定时器 + LIN_Write），Vector 本就软件。
                // 通道唯一注册表（方案阶段 1）：同一通道全局只存在一个调度器，多窗体/重开共享；
                // 窗体关闭时按通道统一释放，避免重复定时器与重复发送。
                sc = Lin_API.GetOrCreateScheduler(_channel, "窗体获取");
                sc.RunningChanged += r => { try { BeginInvoke(new Action(() => RefreshStatusBar())); } catch { } };
                _schedulers[_channel] = sc;
            }
            return sc;
        }

        /// <summary>同步发送项到调度器槽，并按启用勾选自动启停周期发送（勾选即发，取消即停）。</summary>
        private void SyncSchedulerSlots()
        {
            if (_disposed) return;
            SyncSchedulerFromConfig();
            var sc = GetScheduler();
            // 方案 4.2：同步运行快照——启用项 → Intent/Armed；停用/删除项 → Disabled（取消勾选即停，
            // 计划行与勾选一致，历史真实帧仍留在日志）。运行中勾选变化也须同步（Start 早退时不覆盖）。
            sc.SyncSnapshots();
            if (!Lin_API.IsConnected(_channel)) { sc.Suspend(); return; }
            bool hasEnabled = false;
            foreach (var s in sc.Slots)
                if (s != null && s.Enabled) { hasEnabled = true; break; }
            if (hasEnabled)
            {
                if (!sc.IsRunning)
                {
                    string err = sc.ValidateSlots(GetBaudrate());
                    if (err.Length > 0) { ShowError(err); return; }
                    if (!sc.Start()) ShowError(sc.LastError.Length > 0 ? "周期发送启动失败: " + sc.LastError : "周期发送启动失败");
                }
            }
            else if (sc.IsRunning) sc.Suspend();
        }













































































































































































        private string SelectScheduleTable(LinLdfFile ldf)
        {
            if (ldf == null || ldf.ScheduleTables.Count == 0) return null;
            if (ldf.ScheduleTables.Count == 1) return ldf.ScheduleTables.Keys.First();
            using (var dlg = new Form { Text = "选择 LDF 调度表", Width = 360, Height = 130, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.AddRange(ldf.ScheduleTables.Keys.ToArray());
                combo.SelectedIndex = 0;
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 80 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 80 };
                dlg.Controls.Add(combo);
                dlg.Controls.Add(cancel);
                dlg.Controls.Add(ok);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem.ToString() : null;
            }
        }

        /// <summary>
        /// 从 LDF 导入调度表：优先按 LDF 官方调度表（帧名+时隙）填充，无调度表时回退全部帧。
        /// 当前发送模型按 PID 唯一；LDF 调度表中重复出现的同一 PID 合并为一个发送项，采用首次出现的时隙。
        /// </summary>
        private void ImportSlotsFromLdf()
        {
            var ldf = GetLdf();
            if (ldf == null)
            {
                MessageBox.Show(this, "请先加载 LDF 文件（顶部工具栏「加载 LDF」）", "调度表", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var tableName = SelectScheduleTable(ldf);
            if (ldf.ScheduleTables.Count > 0 && string.IsNullOrEmpty(tableName)) return;
            var source = CurrentChannel.TransmitEntries ?? new List<LinTransmitEntry>();
            var prototypes = new Dictionary<byte, LinTransmitEntry>();
            foreach (var old in source)
                if (old != null && !prototypes.ContainsKey(old.Pid)) prototypes[old.Pid] = old;
            var imported = new List<LinTransmitEntry>();
            var importedPids = new HashSet<byte>();
            bool slaveMode = IsSlaveHardwareMode();
            int skippedMaster = 0;
            if (ldf.ScheduleTables.Count > 0)
            {
                var table = ldf.ScheduleTables[tableName];
                foreach (var def in table)
                {
                    var frame = ldf.Frames.Values.FirstOrDefault(f => f.Name == def.FrameName);
                    if (frame == null) continue;
                    if (!importedPids.Add(frame.Pid)) continue;
                    // Slave 硬件模式：主节点发布帧由外部主节点自己发，本机不参与（配 Slave 会抢答）
                    if (slaveMode && LinLdfHelper.IsMasterPublisherFrame(ldf, frame.Pid)) { skippedMaster++; continue; }
                    LinTransmitEntry prototype;
                    prototypes.TryGetValue(frame.Pid, out prototype);
                    var entry = prototype == null
                        ? CreateTransmitEntry(frame.Pid)
                        : prototype.Clone();
                    entry.Pid = frame.Pid;
                    entry.SlotMs = def.SlotMs > 0 ? def.SlotMs : (entry.SlotMs > 0 ? entry.SlotMs : 15);
                    entry.Dlc = EntryDlc(entry, ldf);
                    entry.Data = EntryData(entry, entry.Dlc);
                    if (slaveMode) entry.Enabled = false; // 应答会改变总线上其他节点看到的网络，默认不启用
                    imported.Add(entry);
                }
            }
            else
            {
                foreach (var kv in ldf.Frames)
                {
                    if (slaveMode && LinLdfHelper.IsMasterPublisherFrame(ldf, kv.Key)) { skippedMaster++; continue; }
                    LinTransmitEntry prototype;
                    prototypes.TryGetValue(kv.Key, out prototype);
                    byte dlc = kv.Value.Dlc == 0 ? (byte)8 : kv.Value.Dlc;
                    var entry = prototype == null
                        ? CreateTransmitEntry(kv.Key)
                        : prototype.Clone();
                    entry.Pid = kv.Key;
                    entry.SlotMs = entry.SlotMs > 0 ? entry.SlotMs : 15;
                    entry.Dlc = EntryDlc(entry, ldf);
                    entry.Data = EntryData(entry, entry.Dlc);
                    if (slaveMode) entry.Enabled = false;
                    imported.Add(entry);
                }
            }
            if (imported.Count == 0)
            {
                MessageBox.Show(this, slaveMode
                    ? "LDF 中无本机可参与的从节点发布帧（Slave 硬件模式下主节点发布帧由外部主节点自己发）"
                    : "LDF 中无可用帧（调度表为空）", "调度表", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CurrentChannel.TransmitEntries = imported;
            LinConfig.SaveLinConfig();
            SyncSchedulerSlots();
            RefreshSendGrid();
            RefreshRespGrid();
            MessageBox.Show(this, slaveMode
                ? "已从 LDF 调度表 " + (tableName ?? "全部帧") + " 导入 " + imported.Count + " 个从节点发送项（类型 Slave，默认未勾选）；" +
                  "跳过 " + skippedMaster + " 个主节点发布帧（Slave 硬件模式下本机不参与）。\n" +
                  "勾选启用后，本机将作为对应从节点应答外部主节点的 Header（通道连接后可随时勾选/取消）。"
                : "已从 LDF 调度表 " + (tableName ?? "全部帧") + " 导入 " + imported.Count + " 个发送项；重复 PID 已合并。勾选启用即开始周期发送，发送类型可在发送页调整。",
                "发送列表", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private string EmptyFrameHint()
        {
            var ch = CurrentChannel;
            if (ch != null && ch.TransmitEntries != null && ch.TransmitEntries.Count == 0)
                return "暂无报文 — 在发送页签添加报文并勾选启用后，此处实时显示发送/接收的报文（时间/方向/ID/帧名称/数据）";
            return "暂无报文 — 连接通道并在发送列表勾选启用后，此处实时显示发送/接收的报文（时间/方向/ID/帧名称/数据）";
        }

        private void RefreshSendGrid()
        {
            if (_disposed || _dgvSend == null) return;
            _suppressSendWriteback = true;
            try
            {
                _dgvSend.Rows.Clear();
                _expandedSend.Clear();
                var ch = CurrentChannel;
                if (ch == null) return;
                var entries = ch.TransmitEntries ?? new List<LinTransmitEntry>();
                var ldf = GetLdf();
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    byte dlc = EntryDlc(entry, ldf);
                    byte[] data = EntryData(entry, dlc);
                    entry.Dlc = dlc;
                    entry.Data = data;
                    int idx = _dgvSend.Rows.Add("＋", entry.Enabled, "0x" + entry.Pid.ToString("X2"),
                        LinLdfHelper.GetFrameName(ldf, entry.Pid), TransmitTypeText(entry.Type), dlc, entry.SlotMs,
                        BitConverter.ToString(data).Replace("-", " "));
                    _dgvSend.Rows[idx].Tag = entry;
                }
            }
            finally { _suppressSendWriteback = false; }
            ApplySendTypeLockStyles();
        }

        // ==================== 从节点/发布页签 ====================

        private void RefreshRespGrid()
        {
            if (_disposed) return;
            _dgvResp.Rows.Clear();
            _expandedResp.Clear();
            var ldf = GetLdf();
            var ch = CurrentChannel;
            if (ldf == null) return;
            foreach (var kv in ldf.Frames.OrderBy(x => x.Key))
            {
                byte pid = kv.Key;
                LinFrameDef def = kv.Value;
                byte dlc = def == null || def.Dlc == 0 ? (byte)8 : def.Dlc;
                bool added = ch != null && ch.TransmitEntries != null &&
                    ch.TransmitEntries.Any(x => x != null && x.Pid == pid);
                int idx = _dgvResp.Rows.Add(
                    LinFrameHasSignals(pid, _channel) ? "＋" : "",
                    "0x" + pid.ToString("X2"),
                    def == null ? LinLdfHelper.GetFrameName(ldf, pid) : def.Name,
                    def == null ? "" : def.Publisher,
                    def == null ? "" : FrameTypeText(def.FrameType),
                    dlc,
                    added ? "已添加" : "未添加",
                    added ? "已添加" : "添加");
                _dgvResp.Rows[idx].Tag = pid;
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

        private void AddLdfFrameToSend(byte pid)
        {
            var ch = CurrentChannel;
            if (ch == null || FindSendEntry(pid) != null) return;
            // Slave 硬件模式下本机不发 Header：主节点发布帧（cmd 帧）由外部主节点自己发，
            // 本机配 Slave 会在该帧响应窗口与其发布抢答 → 直接拒绝添加。
            if (IsSlaveHardwareMode() && LinLdfHelper.IsMasterPublisherFrame(GetLdf(), pid))
            {
                MessageBox.Show(this, "该帧由 LDF 主节点发布（" + LinLdfHelper.GetFrameName(GetLdf(), pid) +
                    "），当前通道硬件模式为 Slave：本机不发 Header、也不应答主节点自己发布的帧。\n" +
                    "如需本机发布这些 cmd 帧，请在通道管理把硬件模式改为 Master（总线上不能同时有两个主节点）。",
                    "发送列表", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ch.TransmitEntries.Add(CreateTransmitEntry(pid));
            if (Lin_API.IsConnected(_channel))
            {
                Lin_API.LinDisconnect(_channel);
                ch.ConnectError = "发送项已增加，请重新连接以应用帧方向和 DLC";
            }
            LinConfig.SaveLinConfig();
            SyncSchedulerFromConfig();
            RefreshRespGrid();
            RefreshSendGrid();
            SyncSchedulerSlots();
        }

        private void DgvResp_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvResp.Rows[e.RowIndex];
            if (!(row.Tag is byte)) return;
            byte pid = (byte)row.Tag;
            if (e.ColumnIndex == _dgvResp.Columns["colRspAdd"].Index)
            {
                AddLdfFrameToSend(pid);
                return;
            }
            if (e.ColumnIndex == _dgvResp.Columns["colRspExpand"].Index)
                ToggleRespExpand(pid);
        }

        private void DgvResp_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvResp.Rows[e.RowIndex];
            if (!(row.Tag is byte)) return;
            if (e.ColumnIndex == _dgvResp.Columns["colRspAdd"].Index) return;
            ToggleRespExpand((byte)row.Tag);
        }

        /// <summary>展开/折叠一帧的信号解析行（＋/－ 切换）</summary>
        private void ToggleRespExpand(byte pid)
        {
            var ldf = GetLdf();
            if (ldf == null || !ldf.FrameSignals.ContainsKey(pid) || ldf.FrameSignals[pid].Count == 0) return;
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
            int insertAt = rowIdx + 1;
            int maxNameLen = 0;
            foreach (var fs in ldf.FrameSignals[pid])
                if (fs.SignalName.Length > maxNameLen) maxNameLen = fs.SignalName.Length;
            foreach (var fs in ldf.FrameSignals[pid])
            {
                var sig = FindSignalDef(ldf, fs.SignalName);
                _dgvResp.Rows.Insert(insertAt, 1);
                var srow = _dgvResp.Rows[insertAt];
                srow.Cells["colRspExpand"].Value = "";
                srow.Cells["colRspId"].Value = null;
                srow.Cells["colRspName"].Value = "　├ " + fs.SignalName.PadRight(maxNameLen);
                srow.Cells["colRspPublisher"].Value = sig == null ? "" : sig.Publisher;
                srow.Cells["colRspType"].Value = "信号";
                srow.Cells["colRspDlc"].Value = "bit " + fs.Offset;
                srow.Cells["colRspAdded"].Value = sig == null ? "" : "初值 " + sig.InitValue.ToString("0.###", CultureInfo.InvariantCulture);
                srow.Cells["colRspAdd"].Value = "";
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
            if (op == "add" && CurrentChannel == null)
            {
                // 发送页首次使用时允许在尚未打开通道管理的空配置中建立默认逻辑通道；
                // 连接硬件仍需用户随后在通道管理中绑定。
                LinConfig.Channels.Add(new LinChannel { Name = "LIN1" });
                _channel = 1;
            }
            var sc = GetScheduler();
            switch (op)
            {
                case "add":
                    byte pid = 0;
                    while (FindSendEntry(pid) != null && pid < 0x3F) pid++;
                    if (FindSendEntry(pid) != null)
                    {
                        ShowError("已没有可用的 PID（0x00-0x3F）");
                        return;
                    }
                    var newEntry = CreateTransmitEntry(pid);
                    newEntry.Type = LinTransmitType.Master;
                    CurrentChannel.TransmitEntries.Add(newEntry);
                    LinConfig.SaveLinConfig();
                    RefreshSendGrid();
                    SyncSchedulerSlots();
                    RefreshRespGrid();
                    int idx = _dgvSend.Rows.Count - 1;
                    if (idx < 0) return;
                    _dgvSend.CurrentCell = _dgvSend.Rows[idx].Cells["colSendPid"];
                    _dgvSend.BeginEdit(true);
                    break;
                case "del":
                    if (_dgvSend.SelectedRows.Count > 0)
                    {
                        var sel = _dgvSend.SelectedRows[0];
                        if (sel.Tag is LinTransmitEntry entry && CurrentChannel != null)
                        {
                            int entryIndex = CurrentChannel.TransmitEntries.IndexOf(entry);
                            if (entryIndex >= 0) CurrentChannel.TransmitEntries.RemoveAt(entryIndex);
                            if (Lin_API.IsConnected(_channel)) Lin_API.DisableSlaveResponse(_channel, entry.Pid, entry.Dlc);
                            LinConfig.SaveLinConfig();
                            SyncSchedulerSlots();
                            RefreshRespGrid();
                        }
                        RefreshSendGrid();
                    }
                    break;
                case "import":
                    ImportSlotsFromLdf();
                    break;
            }
        }

        private void DgvSend_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _dgvSend.Columns["colSendExpand"].Index) return;
            var row = _dgvSend.Rows[e.RowIndex];
            var entry = row.Tag as LinTransmitEntry;
            if (entry != null) ToggleSendExpand(entry);
        }

        private void DgvSend_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvSend.Rows[e.RowIndex];
            var entry = row.Tag as LinTransmitEntry;
            if (entry == null) return;
            if (e.ColumnIndex == _dgvSend.Columns["colSendData"].Index ||
                e.ColumnIndex == _dgvSend.Columns["colSendSlotMs"].Index ||
                e.ColumnIndex == _dgvSend.Columns["colSendDlc"].Index ||
                e.ColumnIndex == _dgvSend.Columns["colSendType"].Index)
                return;
            ToggleSendExpand(entry);
        }

        private void ToggleSendExpand(LinTransmitEntry entry)
        {
            var ldf = GetLdf();
            if (entry == null || ldf == null || !ldf.FrameSignals.ContainsKey(entry.Pid) ||
                ldf.FrameSignals[entry.Pid].Count == 0) return;
            if (_expandedSend.Contains(entry)) CollapseSendFrame(entry);
            else ExpandSendFrame(entry, ldf);
        }

        private int FindSendRow(LinTransmitEntry entry)
        {
            for (int i = 0; i < _dgvSend.Rows.Count; i++)
                if (ReferenceEquals(_dgvSend.Rows[i].Tag, entry)) return i;
            return -1;
        }

        private void ExpandSendFrame(LinTransmitEntry entry, LinLdfFile ldf)
        {
            int rowIdx = FindSendRow(entry);
            if (rowIdx < 0) return;
            int insertAt = rowIdx + 1;
            int maxNameLen = ldf.FrameSignals[entry.Pid].Max(x => x.SignalName.Length);
            byte dlc = EntryDlc(entry, ldf);
            byte[] data = EntryData(entry, dlc);
            foreach (var fs in ldf.FrameSignals[entry.Pid])
            {
                var sig = FindSignalDef(ldf, fs.SignalName);
                ulong raw = sig == null ? 0 : LinLdfHelper.ReadSignalBits(data, fs.Offset, sig.Width);
                _dgvSend.Rows.Insert(insertAt, 1);
                var srow = _dgvSend.Rows[insertAt];
                srow.Cells["colSendExpand"].Value = "";
                srow.Cells["colSendEn"].Value = false; // CheckBox 列必须 bool，空字符串会触发格式化异常
                srow.Cells["colSendPid"].Value = "";
                srow.Cells["colSendName"].Value = "　├ " + fs.SignalName.PadRight(maxNameLen);
                // 信号行不适用发送类型（角色由所属帧决定）：ComboBox 列写非法值会被 DataError 吞掉并回退显示 Master
                srow.Cells["colSendType"] = new DataGridViewTextBoxCell { Value = "" };
                srow.Cells["colSendType"].ReadOnly = true; // ReadOnly 只能在单元格加入行后设置
                srow.Cells["colSendDlc"].Value = "bit " + fs.Offset;
                srow.Cells["colSendSlotMs"].Value = "";
                string valueText = sig == null ? "—" : FormatSigValue(sig, raw);
                srow.Cells["colSendData"] = BuildSignalValueCell(sig, valueText);
                srow.Tag = new SendSignalRow { Entry = entry, SignalName = fs.SignalName, Offset = fs.Offset };
                srow.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 248);
                srow.Cells["colSendData"].Style.BackColor = Color.White; // 值可编辑：白底区别于只读列
                srow.Cells["colSendData"].ToolTipText = sig != null && sig.Unit.Length > 0
                    ? "可直接改值：输入数值（单位 " + sig.Unit + " 自动带出，不可编辑）"
                    : "可直接改值：枚举信号下拉选择，其余输入数值";
                insertAt++;
            }
            _expandedSend.Add(entry);
            _dgvSend.Rows[rowIdx].Cells["colSendExpand"].Value = "－";
        }

        /// <summary>
        /// 信号值编辑单元格：枚举信号（LDF logical_value）用下拉选择，物理量信号用文本编辑
        /// （单位由显示格式带出、不参与编辑；提交时按 TryParseSigValue 解析回原始位）。
        /// </summary>
        private DataGridViewCell BuildSignalValueCell(LinSignalDef sig, string valueText)
        {
            if (sig != null && sig.LogicalValues != null && sig.LogicalValues.Count > 0)
            {
                var combo = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                foreach (var kv in sig.LogicalValues.OrderBy(kv => kv.Key))
                    if (!combo.Items.Contains(kv.Value)) combo.Items.Add(kv.Value);
                if (!string.IsNullOrEmpty(valueText) && !combo.Items.Contains(valueText)) combo.Items.Add(valueText);
                combo.Value = valueText;
                return combo;
            }
            return new DataGridViewTextBoxCell { Value = valueText };
        }

        private void CollapseSendFrame(LinTransmitEntry entry)
        {
            for (int i = _dgvSend.Rows.Count - 1; i >= 0; i--)
            {
                var signal = _dgvSend.Rows[i].Tag as SendSignalRow;
                if (signal != null && ReferenceEquals(signal.Entry, entry)) _dgvSend.Rows.RemoveAt(i);
            }
            _expandedSend.Remove(entry);
            int rowIdx = FindSendRow(entry);
            if (rowIdx >= 0) _dgvSend.Rows[rowIdx].Cells["colSendExpand"].Value = "＋";
        }

        private void DgvSend_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            var cur = _dgvSend.CurrentCell;
            if (cur == null || !(e.Control is DataGridViewTextBoxEditingControl ed)) return;
            var signal = _dgvSend.Rows[cur.RowIndex].Tag as SendSignalRow;
            if (signal == null || cur.ColumnIndex != _dgvSend.Columns["colSendData"].Index) return;
            var sig = FindSignalDef(GetLdf(), signal.SignalName);
            if (sig == null || sig.Unit.Length == 0) return;
            string text = (cur.Value ?? "").ToString();
            if (text.EndsWith(sig.Unit, StringComparison.OrdinalIgnoreCase))
                ed.Text = text.Substring(0, text.Length - sig.Unit.Length).Trim();
        }

        private void ApplySignalToSendEntry(SendSignalRow signalRow, string text)
        {
            var ldf = GetLdf();
            var sig = FindSignalDef(ldf, signalRow.SignalName);
            if (sig == null) return;
            ulong raw;
            if (!TryParseSigValue(text, sig, out raw))
            {
                RefreshSendSignalRows(signalRow.Entry);
                return;
            }
            byte dlc = EntryDlc(signalRow.Entry, ldf);
            byte[] data = EntryData(signalRow.Entry, dlc);
            LinLdfHelper.WriteSignalBits(data, signalRow.Offset, sig.Width, raw);
            signalRow.Entry.Dlc = dlc;
            signalRow.Entry.Data = data;
            LinConfig.SaveLinConfig();
            if (Lin_API.IsConnected(_channel) && !Lin_API.UpdateTransmitData(
                _channel, signalRow.Entry.Pid, data, dlc, signalRow.Entry.Type))
                ShowError("发送项信号下发失败");
            RefreshSendFrameData(signalRow.Entry);
            RefreshSendSignalRows(signalRow.Entry);
        }

        private void RefreshSendFrameData(LinTransmitEntry entry)
        {
            int rowIdx = FindSendRow(entry);
            if (rowIdx < 0) return;
            _dgvSend.Rows[rowIdx].Cells["colSendData"].Value =
                BitConverter.ToString(entry.Data ?? new byte[0]).Replace("-", " ");
            _dgvSend.Rows[rowIdx].Cells["colSendDlc"].Value = entry.Dlc;
        }

        private void RefreshSendSignalRows(LinTransmitEntry entry)
        {
            var ldf = GetLdf();
            if (ldf == null || entry == null || !ldf.FrameSignals.ContainsKey(entry.Pid)) return;
            byte[] data = EntryData(entry, EntryDlc(entry, ldf));
            for (int i = 0; i < _dgvSend.Rows.Count; i++)
            {
                var signalRow = _dgvSend.Rows[i].Tag as SendSignalRow;
                if (signalRow == null || !ReferenceEquals(signalRow.Entry, entry)) continue;
                var sig = FindSignalDef(ldf, signalRow.SignalName);
                if (sig != null)
                    _dgvSend.Rows[i].Cells["colSendData"].Value = FormatSigValue(
                        sig, LinLdfHelper.ReadSignalBits(data, signalRow.Offset, sig.Width));
            }
        }

        private void DgvSend_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = _dgvSend.Rows[e.RowIndex];
            if (_suppressSendWriteback) return;
            var signalRow = row.Tag as SendSignalRow;
            if (signalRow != null)
            {
                if (_dgvSend.Columns[e.ColumnIndex].Name == "colSendData")
                    ApplySignalToSendEntry(signalRow, (row.Cells["colSendData"].Value ?? "").ToString());
                return;
            }
            var entry = row.Tag as LinTransmitEntry;
            if (entry == null) return;
            var oldPid = entry.Pid;
            switch (_dgvSend.Columns[e.ColumnIndex].Name)
            {
                case "colSendEn":
                    entry.Enabled = (bool)(row.Cells["colSendEn"].Value ?? false);
                    // Slave 硬件模式不发 Header：启用 Master/HeaderOnly 项只会"看起来在发"，实际发不出去
                    // （LIN_Write 在从节点模式下不产生总线 Header）→ 直接拒绝启用并给出改法。
                    if (entry.Enabled && entry.Type != LinTransmitType.Slave && IsSlaveHardwareMode())
                    {
                        entry.Enabled = false;
                        row.Cells["colSendEn"].Value = false;
                        ShowError("通道硬件模式为 Slave：本机不发 Header，发送类型 " + TransmitTypeText(entry.Type) +
                            " 无法发送。请把该报文改为 Slave（本机应答外部 Header），或在通道管理把硬件模式改成 Master。");
                        break;
                    }
                    if (Lin_API.IsConnected(_channel))
                    {
                        if (!entry.Enabled && entry.Type == LinTransmitType.Slave)
                            Lin_API.DisableSlaveResponse(_channel, entry.Pid, entry.Dlc);
                        else if (entry.Enabled && !Lin_API.UpdateTransmitData(_channel, entry.Pid, entry.Data, entry.Dlc, entry.Type))
                            ShowError("发送项启用失败，请检查硬件能力");
                    }
                    LinConfig.SaveLinConfig();
                    SyncSchedulerSlots(); // 勾选即发、取消即停（连接时自动启停调度器）
                    RefreshRespGrid();
                    ApplySendTypeLockStyles();
                    break;
                case "colSendPid":
                    byte newPid;
                    try { newPid = ParsePid((row.Cells["colSendPid"].Value ?? "").ToString()); }
                    catch { return; }
                    if (newPid > 0x3F) { ShowError("PID 须 0x00-0x3F"); return; }
                    if (newPid != oldPid && FindSendEntry(newPid) != null)
                    {
                        ShowError("该 PID 已存在发送项");
                        RefreshSendGrid();
                        return;
                    }
                    if (_expandedSend.Contains(entry)) CollapseSendFrame(entry);
                    entry.Pid = newPid;
                    row.Cells["colSendName"].Value = LinLdfHelper.GetFrameName(GetLdf(), newPid);
                    int pidEntryIndex = CurrentChannel == null ? -1 : CurrentChannel.TransmitEntries.IndexOf(entry);
                    if (pidEntryIndex >= 0 && pidEntryIndex < GetScheduler().Slots.Count)
                        GetScheduler().Slots[pidEntryIndex].Pid = newPid;
                    if (Lin_API.IsConnected(_channel))
                    {
                        Lin_API.LinDisconnect(_channel);
                        CurrentChannel.ConnectError = "发送项 PID 已修改，请重新连接";
                    }
                    LinConfig.SaveLinConfig();
                    SyncSchedulerSlots();
                    RefreshRespGrid();
                    break;
                case "colSendType":
                    var newType = ParseTransmitType(row.Cells["colSendType"].Value);
                    if (newType != entry.Type)
                    {
                        // 门禁：目标类型超出当前连接能力（纯 Slave 连接切 Master/HeaderOnly、BreakOnly）时拒绝并还原
                        string deny = Lin_API.CanSwitchTransmitType(_channel, entry.Pid, newType);
                        if (deny.Length > 0)
                        {
                            ShowError(deny);
                            row.Cells["colSendType"].Value = TransmitTypeText(entry.Type);
                            break;
                        }
                        entry.Type = newType;
                        int typeEntryIndex = CurrentChannel == null ? -1 : CurrentChannel.TransmitEntries.IndexOf(entry);
                        if (typeEntryIndex >= 0 && typeEntryIndex < GetScheduler().Slots.Count)
                            GetScheduler().Slots[typeEntryIndex].TransmitType = newType;
                        if (Lin_API.IsConnected(_channel) && !Lin_API.UpdateTransmitData(_channel, entry.Pid, entry.Data, entry.Dlc, newType))
                        {
                            ShowError("发送类型下发失败");
                            row.Cells["colSendType"].Value = TransmitTypeText(entry.Type);
                            break;
                        }
                        LinConfig.SaveLinConfig();
                        SyncSchedulerSlots(); // 在线切换：类型变更即时生效（勾选运行中即按新类型发送）
                        Lin_API.ReconfigureRunningScheduler(_channel, "发送类型切换 pid=0x" + entry.Pid.ToString("X2"));
                    }
                    RefreshRespGrid();
                    break;
                case "colSendDlc":
                    byte newDlc;
                    if (!byte.TryParse((row.Cells["colSendDlc"].Value ?? "").ToString(), out newDlc) || newDlc > 8)
                    {
                        ShowError("DLC 须为 0-8");
                        RefreshSendGrid();
                        return;
                    }
                    entry.Dlc = newDlc;
                    entry.Data = EntryData(entry, newDlc);
                    row.Cells["colSendData"].Value = BitConverter.ToString(entry.Data).Replace("-", " ");
                    if (Lin_API.IsConnected(_channel) && !Lin_API.UpdateTransmitData(_channel, entry.Pid, entry.Data, newDlc, entry.Type))
                        ShowError("DLC 下发失败");
                    LinConfig.SaveLinConfig();
                    RefreshRespGrid();
                    break;
                case "colSendSlotMs":
                    int slotMs;
                    if (!int.TryParse((row.Cells["colSendSlotMs"].Value ?? "").ToString(), out slotMs) || slotMs <= 0)
                    {
                        ShowError("时隙必须是大于 0 的整数毫秒");
                        RefreshSendGrid();
                        return;
                    }
                    entry.SlotMs = slotMs;
                    int entryIndex = CurrentChannel == null ? -1 : CurrentChannel.TransmitEntries.IndexOf(entry);
                    if (entryIndex >= 0 && entryIndex < GetScheduler().Slots.Count)
                        GetScheduler().Slots[entryIndex].SlotMs = slotMs;
                    LinConfig.SaveLinConfig();
                    SyncSchedulerSlots();
                    break;
                case "colSendData":
                    var data = ParseHexData((row.Cells["colSendData"].Value ?? "").ToString());
                    if (data == null) return;
                    row.Cells["colSendDlc"].Value = data.Length;
                    entry.Dlc = (byte)data.Length;
                    entry.Data = (byte[])data.Clone();
                    if (Lin_API.IsConnected(_channel) && !Lin_API.UpdateTransmitData(_channel, entry.Pid, data, (byte)data.Length, entry.Type))
                        ShowError("发送项数据下发失败");
                    LinConfig.SaveLinConfig();
                    RefreshRespGrid();
                    RefreshSendSignalRows(entry);
                    break;
            }
        }

        // ==================== 状态栏 ====================

        private void RefreshStatusBar()
        {
            if (_disposed) return;
            if (_channel >= 1 && _channel <= LinConfig.Channels.Count)
            {
                _lblBus.Text = Lin_API.IsConnected(_channel) ? "总线: " + Lin_API.GetBusStateText(_channel) : "总线: 未连接";
                var sc = GetScheduler();
                // 运行状态只看调度器：无启用项（或全为 Slave 项的被动监听）时调度器不运行。
                _lblSched.Text = sc.IsRunning ? "调度: 运行中（周期发送）" : "调度: 停止";
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

        /// <summary>链路丢失且自动重连（2 次）全部失败：弹窗提示（窗口已关时跳过）</summary>
        private void OnLinkLostFinal(byte ch, string reason)
        {
            if (_disposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_disposed) return;
                    var cfg = ch >= 1 && ch <= LinConfig.Channels.Count ? LinConfig.Channels[ch - 1] : null;
                    string name = cfg != null ? cfg.Name : "通道 " + ch;
                    MessageBox.Show(this, name + " 链路已断开，自动重连失败。\n原因: " + reason +
                        "\n\n请检查硬件连接（USB 线/电源）后，在「通道管理」中重新点击「连接」。", "LIN 连接断开",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    if (!connected)
                    {
                        // 断开连接：通道调度器统一停止并释放（重连后 GetScheduler 走注册表重建）
                        if (_schedulers.TryGetValue(ch, out var scheduler))
                        {
                            _schedulers.Remove(ch);
                            scheduler.Stop();
                            Lin_API.ReleaseScheduler(ch, "连接断开");
                        }
                    }
                    InitChannelView();
                    RefreshStatusBar();
                    if (connected) _lblBus.Text = "总线: 已连接";
                }));
            }
            catch { }
        }

        /// <summary>发送状态事件（方案 3）：计划行/状态/红灯数据源变化 → 节流重建（≤100ms 出现）</summary>
        private void OnLinTxStateChanged(LinTxEvent ev)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_disposed) return;
                    _linFlatRowsDirty = true;
                    _linRefreshPending = true;
                    RefreshLinMessageDisplay();
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
