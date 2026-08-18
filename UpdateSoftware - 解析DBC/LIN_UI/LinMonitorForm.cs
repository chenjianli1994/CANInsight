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
        private ToolStripComboBox _cmbChannel;
        private ToolStripButton _btnConnect, _btnDisconnect, _btnLoadLdf, _btnStart, _btnPause, _btnClear, _btnWakeUp, _btnSleep;
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

        // 信号页签
        private DataGridView _dgvSignals;

        // 发送页签
        private TextBox _txtSendPid, _txtSendData, _txtSendCs;
        private ComboBox _cmbSendCs;
        private CheckBox _chkPeriodic;
        private TextBox _txtPeriodMs;
        private Button _btnSend;

        // ==================== 数据 ====================
        private readonly List<LinFrameRecord> _frames = new List<LinFrameRecord>();
        private readonly List<int> _filtered = new List<int>();   // 过滤后行 → _frames 索引
        private long _errorCount;
        private volatile bool _paused;
        private byte _channel;          // 当前监控通道（逻辑号）
        private readonly Dictionary<byte, LinScheduler> _schedulers = new Dictionary<byte, LinScheduler>();
        private readonly Timer _uiTimer;
        private LinWinmmTimer _periodicTimer;
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

            RefreshChannelCombo();
            FormClosing += (s, e) =>
            {
                _disposed = true;
                _uiTimer.Stop();
                _periodicTimer?.Dispose();
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
                RefreshChannelCombo();
                RefreshStatusBar();
            };
            _cmbChannel = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _cmbChannel.SelectedIndexChanged += (s, e) => SelectChannel();
            _btnConnect = new ToolStripButton("连接", ToolbarIcons.Get("play"));
            _btnConnect.Click += (s, e) => TryConnect();
            _btnDisconnect = new ToolStripButton("断开", ToolbarIcons.Get("stop"));
            _btnDisconnect.Click += (s, e) => Lin_API.LinDisconnect(_channel);
            _btnLoadLdf = new ToolStripButton("加载 LDF", ToolbarIcons.Get("dbc"));
            _btnLoadLdf.Click += (s, e) => LoadLdf();
            _btnStart = new ToolStripButton("开始", ToolbarIcons.Get("play"));
            _btnStart.Click += (s, e) => { _paused = false; };
            _btnPause = new ToolStripButton("暂停", ToolbarIcons.Get("stop"));
            _btnPause.Click += (s, e) => { _paused = true; };
            _btnClear = new ToolStripButton("清空", ToolbarIcons.Get("clear"));
            _btnClear.Click += (s, e) => { _frames.Clear(); RebuildFilter(); };
            _txtFilter = new ToolStripTextBox { Width = 160, ToolTipText = "PID 过滤：0x11 / 0x11..0x15 / 0x* / 空=全部" };
            _txtFilter.TextChanged += (s, e) => RebuildFilter();
            _btnWakeUp = new ToolStripButton("唤醒", ToolbarIcons.Get("plus"));
            _btnWakeUp.Click += (s, e) => { if (!Lin_API.WakeUp(_channel)) ShowError("唤醒失败（未连接）"); };
            _btnSleep = new ToolStripButton("休眠", ToolbarIcons.Get("stop"));
            _btnSleep.Click += (s, e) => { if (!Lin_API.Sleep(_channel)) ShowError("休眠失败（未连接）"); };

            _toolStripLin.Items.Add(new ToolStripLabel("通道:"));
            _toolStripLin.Items.Add(_cmbChannel);
            _toolStripLin.Items.Add(_btnConnect);
            _toolStripLin.Items.Add(_btnDisconnect);
            _toolStripLin.Items.Add(new ToolStripSeparator());
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
            _dgvFrames.CellValueNeeded += DgvFrames_CellValueNeeded;
            _dgvFrames.CellFormatting += DgvFrames_CellFormatting;

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
            slotPanel.Controls.Add(_slotToolbar);
            _slotToolbar.Dock = DockStyle.Top;

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
            var colRspEn = new DataGridViewCheckBoxColumn { Name = "colRspEn", HeaderText = "启用", Width = 46, ThreeState = false };
            _dgvResp.Columns.Add(colRspEn);
            _dgvResp.Columns.Add("colRspId", "响应 ID");
            _dgvResp.Columns["colRspId"].Width = 80;
            _dgvResp.Columns.Add("colRspName", "帧名称");
            _dgvResp.Columns["colRspName"].Width = 240;
            _dgvResp.Columns.Add("colRspDlc", "DLC");
            _dgvResp.Columns["colRspDlc"].Width = 50;
            _dgvResp.Columns.Add("colRspData", "数据 (Hex)");
            _dgvResp.Columns["colRspData"].Width = 250;
            _dgvResp.Columns.Add("colRspCs", "校验和");
            _dgvResp.Columns["colRspCs"].Width = 110;
            _dgvResp.CellValueChanged += DgvResp_CellValueChanged;
            _dgvResp.CellFormatting += DgvResp_CellFormatting;
            _dgvResp.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvResp.IsCurrentCellDirty) _dgvResp.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            respPanel.Controls.Add(_dgvResp);
            var respHint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 40,
                Text = "作用：配置本机在总线上应答/发布的数据。\n主节点模式 = 本机作为发送方发布帧数据（调度时发出）；从节点模式 = 本机自动应答收到 Header 的帧。加载 LDF 后已自动填充，勾选=启用，编辑数据即时生效",
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

            // ---- 发送页签 ----
            var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var f = UiTheme.UiFont;
            int y = 20;
            sendPanel.Controls.Add(new Label { Text = "PID:", Location = new Point(12, y + 6), AutoSize = true, Font = f });
            _txtSendPid = new TextBox { Location = new Point(60, y), Width = 80, Font = f, Text = "0x11" };
            sendPanel.Controls.Add(_txtSendPid);
            sendPanel.Controls.Add(new Label { Text = "数据 (Hex, ≤8 字节):", Location = new Point(160, y + 6), AutoSize = true, Font = f });
            _txtSendData = new TextBox { Location = new Point(310, y), Width = 260, Font = f, Text = "" };
            sendPanel.Controls.Add(_txtSendData);
            sendPanel.Controls.Add(new Label { Text = "校验和:", Location = new Point(590, y + 6), AutoSize = true, Font = f });
            _cmbSendCs = new ComboBox { Location = new Point(660, y), Width = 120, Font = f, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSendCs.Items.AddRange(new object[] { "增强 (自动)", "经典 (自动)" });
            _cmbSendCs.SelectedIndex = 0;
            sendPanel.Controls.Add(_cmbSendCs);
            _txtSendCs = new TextBox { Location = new Point(800, y), Width = 60, Font = f, Text = "0x00", Enabled = false };
            sendPanel.Controls.Add(_txtSendCs);
            y += 46;
            _btnSend = new Button { Text = "发送", Location = new Point(12, y), Size = new Size(90, 32), Font = f };
            UiTheme.StyleButton(_btnSend);
            _btnSend.Click += (s, e) => SendFrame();
            sendPanel.Controls.Add(_btnSend);
            _chkPeriodic = new CheckBox { Text = "周期发送", Location = new Point(120, y + 6), AutoSize = true, Font = f };
            _chkPeriodic.CheckedChanged += (s, e) => UpdatePeriodicTimer();
            sendPanel.Controls.Add(_chkPeriodic);
            sendPanel.Controls.Add(new Label { Text = "周期 (ms):", Location = new Point(230, y + 6), AutoSize = true, Font = f });
            _txtPeriodMs = new TextBox { Location = new Point(310, y), Width = 70, Font = f, Text = "100" };
            _txtPeriodMs.TextChanged += (s, e) => UpdatePeriodicTimer();
            sendPanel.Controls.Add(_txtPeriodMs);
            sendPanel.Controls.Add(new Label
            {
                Text = "用法：① PID 填帧 ID（如 0x11）→ ② 数据填 Hex 字节（空格分隔，如 01 02 03）→ ③ 校验和选「增强/经典 自动」→ ④ 点「发送」。\n勾选「周期发送」按设定周期重复发送。仅发 Header（让从节点应答）可配合「从节点/发布」页签预置数据",
                Location = new Point(12, y + 50),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = f,
            });
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
            // 表格与页签上下分栏：上表格 62%，下页签 38%
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 380,
            };
            split.Panel1.Controls.Add(_dgvFrames);
            split.Panel2.Controls.Add(_tabLin);
            Controls.Add(split);
            _dgvFrames.Dock = DockStyle.Fill;
            _statusStrip.Dock = DockStyle.Bottom;
        }

        // ==================== 通道选择/连接 ====================

        private void RefreshChannelCombo()
        {
            _cmbChannel.Items.Clear();
            foreach (var ch in LinConfig.Channels)
            {
                _cmbChannel.Items.Add($"{ch.Name}  [{ch.HwType}|{ch.HwHandle}]{(ch.IsConnected ? " 已连接" : "")}");
            }
            if (_cmbChannel.Items.Count > 0) _cmbChannel.SelectedIndex = 0;
            SelectChannel();
        }

        private void SelectChannel()
        {
            _channel = (byte)(_cmbChannel.SelectedIndex + 1);
            if (_channel < 1 || _channel > LinConfig.Channels.Count) { _channel = 0; return; }
            var ch = LinConfig.Channels[_channel - 1];
            _lblBaud.Text = "波特率 " + ch.Baudrate;
            RefreshSlotGrid();
            RefreshRespGrid();
            RefreshSignalGrid();
        }

        private void TryConnect()
        {
            if (_channel < 1 || _channel > LinConfig.Channels.Count)
            {
                MessageBox.Show(this, "请先在「LIN 通道管理」中添加并配置通道（Main 窗口 → 通道管理 → 添加 LIN 通道）", "LIN 监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string err = Lin_API.LinConnect(_channel);
            if (err.Length > 0) ShowError(err);
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

        /// <summary>PID 过滤：空=全部；0x11；0x11..0x15；0x* 通配</summary>
        private void RebuildFilter()
        {
            if (_disposed) return;
            string text = _txtFilter.Text.Trim();
            lock (_frames)
            {
                _filtered.Clear();
                if (text.Length == 0)
                {
                    for (int i = 0; i < _frames.Count; i++) _filtered.Add(i);
                }
                else
                {
                    var range = ParseFilterRange(text);
                    for (int i = 0; i < _frames.Count; i++)
                    {
                        if (range == null) { _filtered.Add(i); continue; }
                        byte pid = _frames[i].Pid;
                        if (pid >= range.Item1 && pid <= range.Item2) _filtered.Add(i);
                    }
                }
                _dgvFrames.RowCount = _filtered.Count;
                _dgvFrames.Invalidate();
            }
        }

        /// <summary>解析 PID 过滤文本 → (lo, hi)；无法解析返回 null（显示全部）
        /// 支持：0x11；0x11..0x15；0x1*（通配补全为 0x10-0x1F）；*（全部）</summary>
        private static Tuple<byte, byte> ParseFilterRange(string text)
        {
            try
            {
                int dot = text.IndexOf("..", StringComparison.Ordinal);
                if (dot > 0)
                {
                    byte lo = ParsePid(text.Substring(0, dot));
                    byte hi = ParsePid(text.Substring(dot + 2));
                    return Tuple.Create(lo, hi);
                }
                // 通配符：0x1* → 0x10..0x1F；* → 0x00..0x3F
                if (text.TrimEnd().EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = text.Trim().TrimEnd('*').Trim();
                    if (prefix.Length == 0) return Tuple.Create((byte)0x00, (byte)0x3F);
                    string t = prefix;
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                    int v = int.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    int nibbles = t.Length;
                    int shift = nibbles * 4;
                    int hi = (v << (8 - shift)) | ((1 << (8 - shift)) - 1);
                    if (hi > 0x3F) hi = 0x3F;
                    return Tuple.Create((byte)(v << (8 - shift)), (byte)hi);
                }
                byte pid = ParsePid(text);
                return Tuple.Create(pid, pid);
            }
            catch
            {
                return null;
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
                bool useHw = _channel >= 1 && _channel <= LinConfig.Channels.Count &&
                             LinConfig.Channels[_channel - 1].HwType == LinConfig.HwTypePcan;
                sc = new LinScheduler(_channel, useHw);
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
                    if (!sc.Start()) ShowError("调度启动失败（无槽或未连接）");
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
            var ldf = GetLdf();
            if (ldf == null)
            {
                // 无 LDF：空表
                return;
            }
            foreach (byte pid in ldf.SlaveRespIds)
            {
                var def = ldf.Frames[pid];
                int idx = _dgvResp.Rows.Add(true, "0x" + pid.ToString("X2"), def.Name, def.Dlc, new string('0', def.Dlc * 2), "增强·自动");
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
                            if ((byte)r.Tag == kv.Key) { exists = true; break; }
                        if (!exists)
                        {
                            int idx = _dgvResp.Rows.Add(true, "0x" + kv.Key.ToString("X2"), kv.Value.Name, kv.Value.Dlc, new string('0', kv.Value.Dlc * 2), "增强·自动");
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
            if (!(row.Tag is byte)) return;
            byte pid = (byte)row.Tag;
            if (_dgvResp.Columns[e.ColumnIndex].Name == "colRspEn")
            {
                // 勾选/取消勾选 → 启停硬件自动应答
                bool en = (row.Cells["colRspEn"].Value as bool?) ?? false;
                var ldf2 = GetLdf();
                byte respDlc = ldf2 != null && ldf2.Frames.ContainsKey(pid) ? ldf2.Frames[pid].Dlc : (byte)0;
                if (!en)
                {
                    Lin_API.DisableSlaveResponse(_channel, pid, respDlc);
                }
                else
                {
                    // 重新勾选：把行内数据重新下发恢复硬件应答（Vector 恢复 XL_LinSetSlave；
                    // PEAK 由 UpdateSlaveData 重建 RESPONSE_ENABLE 帧条目）
                    var rspData = ParseHexData((row.Cells["colRspData"].Value ?? "").ToString());
                    if (rspData == null) rspData = new byte[respDlc];
                    Lin_API.UpdateSlaveData(_channel, pid, rspData, (byte)rspData.Length);
                }
                return;
            }
            if (_dgvResp.Columns[e.ColumnIndex].Name != "colRspData") return;
            // 数据 Hex 输入（空格分隔）→ 下发硬件
            var data = ParseHexData((row.Cells["colRspData"].Value ?? "").ToString());
            if (data == null) return;
            byte dlc = (byte)data.Length;
            if (dlc > 8) return;
            Lin_API.UpdateSlaveData(_channel, pid, data, dlc);
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

        private void DgvResp_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            // 未启用行灰显
            var row = _dgvResp.Rows[e.RowIndex];
            if (row.Cells["colRspEn"].Value is bool && !(bool)row.Cells["colRspEn"].Value)
                e.CellStyle.ForeColor = Color.Gray;
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

        private void SendFrame()
        {
            try
            {
                byte pid = ParsePid(_txtSendPid.Text);
                if (pid > 0x3F) { ShowError("PID 须 0x00-0x3F"); return; }
                var data = new byte[0];
                string hex = _txtSendData.Text.Replace(" ", "").Replace("0x", "").Trim();
                if (hex.Length > 0)
                {
                    if (hex.Length % 2 != 0) { ShowError("数据须为偶数个十六进制字符"); return; }
                    data = new byte[hex.Length / 2];
                    for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    if (data.Length > 8) { ShowError("数据最多 8 字节"); return; }
                }
                LinChecksumKind ck = _cmbSendCs.SelectedIndex == 1 ? LinChecksumKind.Classic : LinChecksumKind.Enhanced;
                if (!Lin_API.LinTransmit(_channel, pid, data, ck))
                    ShowError("发送失败（未连接或总线忙）");
            }
            catch (Exception ex)
            {
                ShowError("发送参数错误: " + ex.Message);
            }
        }

        private void UpdatePeriodicTimer()
        {
            if (_disposed) return;
            _periodicTimer?.Stop();
            if (!_chkPeriodic.Checked) return;
            int ms;
            if (!int.TryParse(_txtPeriodMs.Text, out ms) || ms < 10) return;
            _periodicTimer = new LinWinmmTimer();
            _periodicTimer.Start(ms, () =>
            {
                try { BeginInvoke(new Action(SendFrame)); } catch { }
            });
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
                if (_chkPeriodic != null && _chkPeriodic.Checked) _lblSched.Text += " +周期发送";
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
                    RefreshChannelCombo();
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
