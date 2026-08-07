using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PCAN_Client;
using PCAN_Client.LIN_API;
using PCAN_Client.util;

namespace PCAN_Client.LIN_UI
{
    /// <summary>
    /// LIN 通道管理对话框：逻辑 LIN 通道与硬件绑定（独立于 CAN 通道配置）
    /// 硬件枚举：PEAK 经 PLinApi（PCAN 硬件内置 LIN 通道，如 "PCAN-USB Pro FD:LIN0"）；
    /// Vector 经 XL 驱动（"ch {index}"）。配置持久化 LinChannels.json。
    /// </summary>
    internal class LinChannelManagerForm : Form
    {
        private DataGridView _dgv;
        // 硬件枚举结果缓存（静态：同一进程内多次打开对话框不重复枚举；60 秒有效期）
        private static List<string> _pcanChannels = new List<string>();
        private static List<string> _xlChannels = new List<string>();
        private static string _pcanError = "";
        private static string _xlError = "";
        private static DateTime _enumStamp = DateTime.MinValue;
        private static readonly object _enumLock = new object();
        private readonly Button _btnAdd, _btnDelete, _btnConnectAll, _btnDisconnectAll, _btnSave, _btnClose, _btnRefresh;

        public LinChannelManagerForm()
        {
            Text = "LIN 通道管理";
            Width = 1080;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            UiTheme.StyleForm(this);

            // 后台异步枚举硬件（不阻塞 UI；完成回调填充下拉并显示错误原因）
            EnsureEnumerated();

            // 说明行
            var lbl = new Label
            {
                Text = "逻辑 LIN 通道与硬件绑定（独立于 CAN 通道配置，保存到 LinChannels.json）",
                AutoSize = true,
                Location = new Point(12, 12),
                Font = UiTheme.UiFont,
            };
            Controls.Add(lbl);

            // 通道表格
            _dgv = new DataGridView
            {
                Location = new Point(12, 40),
                Size = new Size(1040, 320),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ReadOnly = false,
            };
            UiTheme.StyleGrid(_dgv);

            // 列定义
            _dgv.Columns.Add("colName", "通道名");
            _dgv.Columns["colName"].Width = 90;
            var colHwType = new DataGridViewComboBoxColumn { Name = "colHwType", HeaderText = "硬件类型", Width = 140 };
            colHwType.Items.AddRange(new object[] { "PCAN (PLinApi)", "Vector XL (vxlapi)" });
            _dgv.Columns.Add(colHwType);
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "colHw", HeaderText = "硬件通道", Width = 200 });
            var colMode = new DataGridViewComboBoxColumn { Name = "colMode", HeaderText = "节点模式", Width = 90 };
            colMode.Items.AddRange(new object[] { "主节点", "从节点" });
            _dgv.Columns.Add(colMode);
            var colBaud = new DataGridViewComboBoxColumn { Name = "colBaud", HeaderText = "波特率", Width = 80 };
            colBaud.Items.AddRange(new object[] { "1000", "2400", "9600", "19200" });
            _dgv.Columns.Add(colBaud);
            _dgv.Columns.Add("colLdf", "LDF 文件");
            _dgv.Columns["colLdf"].Width = 300;
            _dgv.Columns["colLdf"].ReadOnly = true; // 路径只经浏览按钮选择（点击单元格触发）
            _dgv.Columns.Add("colStatus", "状态");
            _dgv.Columns["colStatus"].Width = 110;
            _dgv.Columns["colStatus"].ReadOnly = true;
            var colOp = new DataGridViewButtonColumn { Name = "colOp", HeaderText = "操作", Width = 80, ReadOnly = true, Text = "连接", UseColumnTextForButtonValue = true };
            _dgv.Columns.Add(colOp);

            _dgv.CellFormatting += Dgv_CellFormatting;
            _dgv.CellClick += Dgv_CellClick;
            _dgv.CellValueChanged += Dgv_CellValueChanged;
            _dgv.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgv.IsCurrentCellDirty) _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            Controls.Add(_dgv);

            // 底部按钮
            _btnAdd = MakeButton("添加通道", new Point(12, 375));
            _btnAdd.Click += (s, e) => AddChannelRow();
            _btnDelete = MakeButton("删除通道", new Point(120, 375));
            _btnDelete.Click += (s, e) => DeleteSelectedRow();
            _btnConnectAll = MakeButton("全部连接", new Point(700, 375), true);
            _btnConnectAll.Click += (s, e) => ConnectAll();
            _btnDisconnectAll = MakeButton("全部断开", new Point(800, 375));
            _btnDisconnectAll.Click += (s, e) => DisconnectAll();
            _btnRefresh = MakeButton("刷新硬件", new Point(596, 375));
            _btnRefresh.Click += (s, e) => { lock (_enumLock) _enumStamp = DateTime.MinValue; EnsureEnumerated(); };
            _btnSave = MakeButton("保存", new Point(900, 375));
            _btnSave.Click += (s, e) => SaveConfig();
            _btnClose = MakeButton("关闭", new Point(972, 375));
            _btnClose.Click += (s, e) => { SaveConfig(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_btnAdd); Controls.Add(_btnDelete); Controls.Add(_btnConnectAll);
            Controls.Add(_btnDisconnectAll); Controls.Add(_btnRefresh);
            Controls.Add(_btnSave); Controls.Add(_btnClose);

            LoadRows();
        }

        private Button MakeButton(string text, Point loc, bool primary = false)
        {
            var btn = new Button { Text = text, Location = loc, Size = new Size(92, 30), Font = UiTheme.UiFont };
            UiTheme.StyleButton(btn);
            if (primary) btn.BackColor = UiTheme.Accent;
            return btn;
        }

        // ==================== 行数据 ====================

        /// <summary>
        /// 确保硬件枚举结果就绪：缓存未过期直接用；否则后台线程枚举，完成回调 UI 填充。
        /// 枚举失败时在对应硬件通道下拉显示具体原因（如 PLIN 管理器未运行）。
        /// </summary>
        private void EnsureEnumerated()
        {
            lock (_enumLock)
            {
                if ((DateTime.Now - _enumStamp).TotalSeconds < 60 && (_pcanChannels.Count > 0 || _xlChannels.Count > 0 || _pcanError.Length > 0 || _xlError.Length > 0))
                    return;
                _enumStamp = DateTime.Now; // 占位：防并发重复枚举（失败也缓存 60s，可点刷新重试）
                _pcanChannels = new List<string>();
                _xlChannels = new List<string>();
                _pcanError = "";
                _xlError = "";
            }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var pcan = PcanLinHardware.EnumerateChannels();
                var xl = XlLinHardware.EnumerateChannels();
                lock (_enumLock)
                {
                    _pcanChannels = pcan.Item1;
                    _pcanError = pcan.Item2;
                    _xlChannels = xl.Item1;
                    _xlError = xl.Item2;
                }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        foreach (DataGridViewRow row in _dgv.Rows) RefreshHwCombo(row);
                    }));
                }
                catch { }
            });
        }

        private void LoadRows()
        {
            _dgv.Rows.Clear();
            foreach (var ch in LinConfig.Channels)
            {
                AddChannelRow(ch);
            }
            if (_dgv.Rows.Count == 0) AddChannelRow(new LinChannel());
        }

        private void AddChannelRow(LinChannel ch)
        {
            var row = new DataGridViewRow { Tag = ch };
            row.CreateCells(_dgv,
                ch.Name,
                HwTypeDisplay(ch.HwType),
                ch.HwHandle,
                ch.Mode == LinNodeMode.Master ? "主节点" : "从节点",
                ch.Baudrate.ToString(),
                ch.LdfPath,
                StatusText(ch),
                "连接");
            _dgv.Rows.Add(row);
            RefreshHwCombo(row);
            UpdateRowStatus(row);
        }

        private LinChannel RowChannel(DataGridViewRow row) => (LinChannel)row.Tag;

        private static string HwTypeDisplay(string hwType)
        {
            return hwType == LinConfig.HwTypePcan ? "PCAN (PLinApi)" :
                   hwType == LinConfig.HwTypeCanoe ? "Vector XL (vxlapi)" : "";
        }

        private static string StatusText(LinChannel ch)
        {
            if (ch.IsConnected) return "已连接";
            return ch.ConnectError.Length > 0 ? "错误: " + ch.ConnectError : "未连接";
        }

        /// <summary>按行硬件类型刷新硬件通道下拉数据源</summary>
        private void RefreshHwCombo(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells["colHw"];
            cell.Items.Clear();
            bool pcan = RowChannel(row).HwType == LinConfig.HwTypePcan;
            var list = pcan ? _pcanChannels : _xlChannels;
            var err = pcan ? _pcanError : _xlError;
            foreach (var c in list) cell.Items.Add(c);
            if (cell.Items.Count == 0)
            {
                // 无通道：显示具体原因（PLIN 管理器未运行等），便于用户诊断
                cell.Items.Add(err.Length > 0 ? "(枚举失败: " + err + ")" : "(未检测到硬件)");
                cell.ToolTipText = err;
            }
        }

        private void AddChannelRow()
        {
            // 添加按钮：以当前行数为序号新建默认行
            int n = _dgv.Rows.Count + 1;
            while (LinConfig.Channels.Exists(c => c.Name == "LIN" + n)) n++;
            AddChannelRow(new LinChannel { Name = "LIN" + n });
        }

        private void DeleteSelectedRow()
        {
            if (_dgv.SelectedRows.Count == 0) return;
            var row = _dgv.SelectedRows[0];
            int idx = _dgv.Rows.IndexOf(row);
            var ch = RowChannel(row);
            if (ch.IsConnected) Lin_API.LinDisconnect((byte)(idx + 1));
            _dgv.Rows.Remove(row);
            // 逻辑通道号 = 行号+1：删除中间行会导致后续行号整体前移。
            // 后续已连接行按旧逻辑号挂在 Lin_API 实例字典中，重排后旧实例成为僵尸连接（硬件被占用）。
            // 处理：断开所有后续已连接行并提示重连。
            bool warned = false;
            for (int i = idx; i < _dgv.Rows.Count; i++)
            {
                var r = _dgv.Rows[i];
                var c = RowChannel(r);
                if (c.IsConnected)
                {
                    // 被删行之后的行：旧逻辑号 = 新逻辑号 + 1（被删行占了一个号）
                    Lin_API.LinDisconnect((byte)(i + 2));
                    UpdateRowStatus(r);
                    warned = true;
                }
            }
            if (warned)
                MessageBox.Show(this, "通道行号已重排，后续通道已断开，请重新连接", "LIN 通道管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==================== 连接/断开 ====================

        private void ConnectAll()
        {
            SaveConfig(); // 行集合写回 LinConfig，保证逻辑号与行号一致（未保存即连接时防错连/越界）
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                ConnectRow(_dgv.Rows[i], (byte)(i + 1));
            }
        }

        private void DisconnectAll()
        {
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                var ch = RowChannel(row);
                if (ch.IsConnected)
                {
                    Lin_API.LinDisconnect((byte)(i + 1));
                    UpdateRowStatus(row);
                }
            }
        }

        private void ConnectRow(DataGridViewRow row, byte logicChannel)
        {
            SaveConfig(); // 同 ConnectAll：保证逻辑号与行号一致
            var ch = RowChannel(row);
            if (ch.HwType.Length == 0 || ch.HwHandle.Length == 0)
            {
                ch.ConnectError = "请先选择硬件类型与硬件通道";
                UpdateRowStatus(row);
                return;
            }
            string err = Lin_API.LinConnect(logicChannel);
            UpdateRowStatus(row);
        }

        private void UpdateRowStatus(DataGridViewRow row)
        {
            var ch = RowChannel(row);
            row.Cells["colStatus"].Value = StatusText(ch);
            row.Cells["colStatus"].Style.ForeColor = ch.IsConnected ? Color.FromArgb(0, 128, 0) :
                ch.ConnectError.Length > 0 ? Color.FromArgb(196, 43, 28) : Color.Gray;
            row.Cells["colOp"].Value = ch.IsConnected ? "断开" : "连接";
        }

        // ==================== 事件 ====================

        private void Dgv_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _dgv.Rows.Count) return;
            var row = _dgv.Rows[e.RowIndex];
            if (e.ColumnIndex == _dgv.Columns["colStatus"].Index)
            {
                var ch = RowChannel(row);
                if (ch.IsConnected) e.CellStyle.ForeColor = Color.FromArgb(0, 128, 0);
                else if (ch.ConnectError.Length > 0) e.CellStyle.ForeColor = Color.FromArgb(196, 43, 28);
                else e.CellStyle.ForeColor = Color.Gray;
            }
        }

        private void Dgv_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == _dgv.Columns["colOp"].Index)
            {
                var row = _dgv.Rows[e.RowIndex];
                var ch = RowChannel(row);
                byte logic = (byte)(e.RowIndex + 1);
                if (ch.IsConnected) { Lin_API.LinDisconnect(logic); UpdateRowStatus(row); }
                else ConnectRow(row, logic);
            }
            else if (e.ColumnIndex == _dgv.Columns["colLdf"].Index)
            {
                var row = _dgv.Rows[e.RowIndex];
                using (var dlg = new OpenFileDialog { Filter = "LIN 描述文件 (*.lin)|*.lin|所有文件 (*.*)|*.*", Title = "选择 LDF 文件" })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        var ch = RowChannel(row);
                        ch.LdfPath = dlg.FileName;
                        row.Cells["colLdf"].Value = dlg.FileName;
                        try { ch.LdfHelper = LinLdfHelper.Parse(dlg.FileName); }
                        catch (Exception ex) { MessageBox.Show(this, "LDF 加载失败: " + ex.Message, "LIN 通道管理", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
                }
            }
        }

        private void Dgv_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var row = _dgv.Rows[e.RowIndex];
            var ch = RowChannel(row);
            string col = _dgv.Columns[e.ColumnIndex].Name;
            object v = row.Cells[e.ColumnIndex].Value;
            switch (col)
            {
                case "colName":
                    ch.Name = (v ?? "").ToString().Trim();
                    if (ch.Name.Length == 0) ch.Name = "LIN" + (e.RowIndex + 1);
                    break;
                case "colHwType":
                    string t = (v ?? "").ToString();
                    ch.HwType = t.Contains("PCAN") ? LinConfig.HwTypePcan : LinConfig.HwTypeCanoe;
                    ch.HwHandle = "";
                    row.Cells["colHw"].Value = "";
                    RefreshHwCombo(row);
                    break;
                case "colHw":
                    ch.HwHandle = (v ?? "").ToString();
                    break;
                case "colMode":
                    ch.Mode = (v ?? "").ToString() == "从节点" ? LinNodeMode.Slave : LinNodeMode.Master;
                    break;
                case "colBaud":
                    uint b;
                    if (uint.TryParse((v ?? "").ToString(), out b) && b >= 1000 && b <= 20000) ch.Baudrate = b;
                    break;
            }
        }

        // ==================== 保存 ====================

        private void SaveConfig()
        {
            var channels = new List<LinChannel>();
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                if (row.Tag == null) continue;
                channels.Add(RowChannel(row));
            }
            LinConfig.Channels = channels;
            LinConfig.SaveLinConfig();
        }
    }
}
