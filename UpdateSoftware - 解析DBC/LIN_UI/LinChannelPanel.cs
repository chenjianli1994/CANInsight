using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PCAN_Client;
using PCAN_Client.LIN_API;
using PCAN_Client.util;

namespace PCAN_Client.LIN_UI
{
    /// <summary>
    /// LIN 通道管理面板（嵌入统一通道管理对话框的 LIN 页签）：
    /// 逻辑 LIN 通道与硬件绑定（独立于 CAN 通道配置），配置持久化 LinChannels.json。
    /// 硬件枚举：PEAK 经 PLinApi（PCAN 硬件内置 LIN 通道，如 "PCAN-USB Pro FD:LIN0"）；
    /// Vector 经 XL 驱动（"ch {index}"）。与 CAN 的物理通道冲突检测：Vector 同 index 即冲突。
    /// </summary>
    internal class LinChannelPanel : UserControl
    {
        private DataGridView _dgv;
        private Label _lblHwStatus; // 顶部硬件识别状态（对齐 CAN 页签：已识别N路 + 已连接N路 + 错误原因）
        // 硬件枚举结果缓存（静态：同一进程内多次打开对话框不重复枚举；60 秒有效期）
        private static List<string> _pcanChannels = new List<string>();
        private static List<string> _xlChannels = new List<string>();
        private static string _pcanError = "";
        private static string _xlError = "";
        private static DateTime _enumStamp = DateTime.MinValue;
        private static readonly object _enumLock = new object();
        private readonly Button _btnAdd, _btnDelete, _btnConnectAll, _btnDisconnectAll, _btnSave;

        /// <summary>绑定硬件下拉项（携带硬件类型+通道标识，选中即固化二元组；HwType=""表示不连接）</summary>
        private class LinHwBindItem
        {
            // 注意：DisplayMember/ValueMember 数据绑定只认属性，必须是属性不能是字段
            public string HwType { get; set; } = "";
            public string HwHandle { get; set; } = "";
            public string Display { get; set; } = "";
            /// <summary>下拉ValueMember唯一键（"类型|通道标识"；HwHandle 可能含冒号故用"|"分隔）。cell.Value存此键字符串</summary>
            public string Key => HwType + "|" + HwHandle;
            public override string ToString() { return Display; }
        }

        private static readonly LinHwBindItem NotConnectItem = new LinHwBindItem { HwType = "", HwHandle = "", Display = "不连接" };

        public LinChannelPanel()
        {
            Dock = DockStyle.Fill;
            Size = new Size(1000, 470);

            // 后台异步枚举硬件（不阻塞 UI；完成回调填充下拉并显示错误原因）
            EnsureEnumerated();

            // 顶部硬件识别状态（对齐 CAN 页签交互：先刷新识别看到硬件，再从已识别硬件中选择绑定连接）
            _lblHwStatus = new Label
            {
                Text = "PLinApi: -  XL: -",
                Location = new Point(12, 12),
                Size = new Size(560, 34),
                Font = UiTheme.UiFont,
            };
            Controls.Add(_lblHwStatus);
            RefreshHwStatus();

            var btnRefresh = new Button { Text = "刷新识别", Location = new Point(912, 10), Size = new Size(94, 32) };
            btnRefresh.Click += (s, e) => EnsureEnumerated(force: true); // 手动刷新绕过60秒缓存显式重查（对齐CAN页签"刷新识别"）
            Controls.Add(btnRefresh);

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
            // 绑定硬件通道：不连接 + 已识别硬件单下拉（带类型前缀），选中即固化 HwType+HwHandle（对齐 CAN 页签）
            var colHwBind = new DataGridViewComboBoxColumn
            {
                Name = "colHwBind",
                HeaderText = "绑定硬件通道",
                Width = 240,
                DisplayMember = "Display",
                ValueMember = "Key",
                FlatStyle = FlatStyle.Flat
            };
            _dgv.Columns.Add(colHwBind);
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
            var colOp = new DataGridViewButtonColumn { Name = "colOp", HeaderText = "操作", Width = 80, ReadOnly = true, Text = "连接" };
            // 注意：不能用 UseColumnTextForButtonValue=true（会忽略单元格 Value，导致"连接/断开/-"切换不显示）
            _dgv.Columns.Add(colOp);

            _dgv.CellFormatting += Dgv_CellFormatting;
            // 防 DataGridView 默认错误弹窗：异步枚举完成前行绑定 Key 可能不在下拉 DataSource 中，
            // DataError 静默处理（RebuildHwBindCellDataSource 已保证保存值会被回退/重建）
            _dgv.DataError += (s, e) => { e.ThrowException = false; };
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
            _btnSave = MakeButton("保存", new Point(900, 375));
            _btnSave.Click += (s, e) => SaveConfig();
            Controls.Add(_btnAdd); Controls.Add(_btnDelete); Controls.Add(_btnConnectAll);
            Controls.Add(_btnDisconnectAll); Controls.Add(_btnSave);

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
        /// 确保硬件枚举结果就绪：缓存未过期且非 force 时直接用；否则后台线程枚举，完成回调 UI 填充。
        /// force=true 供"刷新识别"按钮显式重查（绕过 60 秒缓存）。
        /// 枚举失败时在状态标签与对应硬件通道下拉显示具体原因（如 PLIN 管理器未运行）。
        /// </summary>
        private void EnsureEnumerated(bool force = false)
        {
            lock (_enumLock)
            {
                bool fresh = (DateTime.Now - _enumStamp).TotalSeconds < 60
                    && (_pcanChannels.Count > 0 || _xlChannels.Count > 0 || _pcanError.Length > 0 || _xlError.Length > 0);
                if (!force && fresh)
                    return;
                _enumStamp = DateTime.Now; // 占位：防并发重复枚举（失败也缓存 60s，可点刷新重试）
                _pcanChannels = new List<string>();
                _xlChannels = new List<string>();
                _pcanError = "";
                _xlError = "";
            }
            SetHwStatusText("正在识别硬件...");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                Tuple<List<string>, string> pcan = null, xl = null;
                try { pcan = PcanLinHardware.EnumerateChannels(); } catch (Exception ex) { pcan = Tuple.Create(new List<string>(), "PLinApi 枚举异常: " + ex.Message); }
                try { xl = XlLinHardware.EnumerateChannels(); } catch (Exception ex) { xl = Tuple.Create(new List<string>(), "XL 枚举异常: " + ex.Message); }
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
                        if (IsDisposed) return;
                        foreach (DataGridViewRow row in _dgv.Rows)
                        {
                            RebuildHwBindCellDataSource(row); // 尽量保持原选择，硬件不在位回退"不连接"
                            UpdateRowStatus(row);
                            UpdateConflict(row);
                        }
                        RefreshHwStatus();
                    }));
                }
                catch { }
            });
        }

        /// <summary>顶部硬件识别状态标签：两行（PLinApi / XL）显示已识别路数与已连接路数；枚举失败显示原因</summary>
        private void RefreshHwStatus()
        {
            if (_lblHwStatus == null || _lblHwStatus.IsDisposed) return;
            string pcan = _pcanError.Length > 0 ? "枚举失败: " + _pcanError
                : _pcanChannels.Count > 0 ? $"已识别{_pcanChannels.Count}路" : "未识别到设备";
            string xl = _xlError.Length > 0 ? "枚举失败: " + _xlError
                : _xlChannels.Count > 0 ? $"已识别{_xlChannels.Count}路" : "未识别到设备";
            int pcanConn = LinConfig.Channels.Count(c => c.HwType == LinConfig.HwTypePcan && c.IsConnected);
            int xlConn = LinConfig.Channels.Count(c => c.HwType == LinConfig.HwTypeCanoe && c.IsConnected);
            _lblHwStatus.Text =
                $"PLinApi: {pcan}（已连接{pcanConn}路）\r\n" +
                $"XL: {xl}（已连接{xlConn}路）";
        }

        /// <summary>识别中占位文案（不叠加刷新完成后的计数）</summary>
        private void SetHwStatusText(string text)
        {
            if (_lblHwStatus == null || _lblHwStatus.IsDisposed) return;
            _lblHwStatus.Text = text;
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
                "", // 绑定硬件通道：由 FindHwBindKey 赋值
                ch.Mode == LinNodeMode.Master ? "主节点" : "从节点",
                ch.Baudrate.ToString(),
                ch.LdfPath,
                StatusText(ch),
                "连接");
            _dgv.Rows.Add(row);
            RebuildHwBindCellDataSource(row);
            row.Cells["colHwBind"].Value = FindHwBindKey(row, ch);
            UpdateRowStatus(row);
            UpdateConflict(row);
        }

        private LinChannel RowChannel(DataGridViewRow row) => (LinChannel)row.Tag;

        private static string StatusText(LinChannel ch)
        {
            if (ch.IsConnected) return "已连接";
            return ch.ConnectError.Length > 0 ? "错误: " + ch.ConnectError : "未连接";
        }

        /// <summary>构建绑定下拉选项：不连接 + 已识别到的全部硬件通道（带类型前缀；冲突不限制选择，由标红提示+连接时拦截）</summary>
        private List<LinHwBindItem> BuildHwBindItems()
        {
            var items = new List<LinHwBindItem> { NotConnectItem };
            foreach (var c in _pcanChannels)
                items.Add(new LinHwBindItem { HwType = LinConfig.HwTypePcan, HwHandle = c, Display = "PCAN " + c });
            foreach (var c in _xlChannels)
                items.Add(new LinHwBindItem { HwType = LinConfig.HwTypeCanoe, HwHandle = c, Display = "XL " + c });
            return items;
        }

        /// <summary>读取某行当前绑定项（cell.Value为Key字符串，从该行数据源按键反查；无匹配视为不连接）</summary>
        private LinHwBindItem GetRowHwBind(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells["colHwBind"];
            string key = cell.Value?.ToString();
            var items = cell.DataSource as List<LinHwBindItem>;
            if (string.IsNullOrEmpty(key) || items == null) return NotConnectItem;
            return items.FirstOrDefault(x => x.Key == key) ?? NotConnectItem;
        }

        /// <summary>重建某行绑定下拉的数据源（枚举结果变化后），尽量保持原选择；原绑定硬件不在位时回退"不连接"</summary>
        private void RebuildHwBindCellDataSource(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells["colHwBind"];
            var cur = GetRowHwBind(row);
            var items = BuildHwBindItems();
            cell.DataSource = items;
            if (cur.HwType.Length == 0) { cell.Value = NotConnectItem.Key; return; }
            // 原绑定硬件当前不在位：直接回退"不连接"（重新插入并刷新后需重新选择）
            var keep = items.FirstOrDefault(x => x.HwType == cur.HwType && x.HwHandle == cur.HwHandle);
            cell.Value = (keep ?? NotConnectItem).Key;
        }

        /// <summary>按通道已保存绑定反查下拉键；硬件不在位时回退"不连接"</summary>
        private string FindHwBindKey(DataGridViewRow row, LinChannel ch)
        {
            if (ch.HwType.Length == 0 || ch.HwHandle.Length == 0) return NotConnectItem.Key;
            var items = (List<LinHwBindItem>)((DataGridViewComboBoxCell)row.Cells["colHwBind"]).DataSource;
            var m = items.FirstOrDefault(x => x.HwType == ch.HwType && x.HwHandle == ch.HwHandle);
            return (m ?? NotConnectItem).Key;
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
                var ch = RowChannel(_dgv.Rows[i]);
                if (ch.HwType.Length == 0 || ch.HwHandle.Length == 0) continue; // 未绑定硬件行跳过（对齐 CAN 一键连接）
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
                ch.ConnectError = "请先选择绑定的硬件通道";
                UpdateRowStatus(row);
                return;
            }
            // Vector 侧物理通道冲突拦截（同一 channelIndex 已被 CAN 占用时不可同时激活）
            string conflict = GetConflictText(ch);
            if (conflict != null)
            {
                MessageBox.Show(this, conflict + "\n请更换 LIN 通道或先断开对应 CAN 通道", "LIN 通道管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string err = Lin_API.LinConnect(logicChannel);
            UpdateRowStatus(row);
        }

        /// <summary>
        /// 与 CAN 配置的物理通道冲突检测：
        /// Vector（XL 驱动）同一 channelIndex 被 CAN 与 LIN 同时激活即冲突（硬件单通道单总线）；
        /// PEAK（PLinApi）LIN 通道与 CAN 通道物理独立（如 Pro FD 的 LIN0/1 与 CAN1/2），无冲突返回 null。
        /// 返回 null = 无冲突；返回字符串 = 冲突描述（红色显示/连接拦截）。
        /// </summary>
        private static string GetConflictText(LinChannel ch)
        {
            if (ch == null || ch.HwType != LinConfig.HwTypeCanoe) return null;
            int chIdx = -1;
            if (ch.HwHandle.StartsWith("ch ", StringComparison.OrdinalIgnoreCase))
                int.TryParse(ch.HwHandle.Substring(3).Trim(), out chIdx);
            if (chIdx < 0) return null;
            foreach (var can in BaseParamter.BusChannels)
            {
                if (can.HwType == BaseParamter.HwTypeCanoe && can.HwChannel == chIdx + 1 && can.HwChannel != 255)
                {
                    return $"⚠ 与 CAN 通道[{can.Name}] 冲突：同一物理通道 ch {chIdx} 已被 CAN 绑定";
                }
            }
            return null;
        }

        /// <summary>刷新行的冲突提示：绑定硬件通道单元格红字+ToolTip（对齐 CAN 页签冲突标红方式；连接时仍拦截）</summary>
        private void UpdateConflict(DataGridViewRow row)
        {
            var ch = RowChannel(row);
            string conflict = GetConflictText(ch);
            var cell = row.Cells["colHwBind"];
            if (conflict != null)
            {
                cell.Style.ForeColor = Color.FromArgb(196, 43, 28);
                cell.ToolTipText = conflict;
            }
            else
            {
                cell.Style.ForeColor = row.DefaultCellStyle.ForeColor;
                cell.ToolTipText = "";
            }
        }

        private void UpdateRowStatus(DataGridViewRow row)
        {
            var ch = RowChannel(row);
            row.Cells["colStatus"].Value = StatusText(ch);
            row.Cells["colStatus"].Style.ForeColor = ch.IsConnected ? Color.FromArgb(0, 128, 0) :
                ch.ConnectError.Length > 0 ? Color.FromArgb(196, 43, 28) : Color.Gray;
            // 操作列：未绑定硬件时显示"-"（对齐 CAN 页签），点击忽略
            row.Cells["colOp"].Value = (ch.HwType.Length == 0 || ch.HwHandle.Length == 0) ? "-" :
                (ch.IsConnected ? "断开" : "连接");
            RefreshHwStatus(); // 顶部状态标签的"已连接N路"随连接/断开变化
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
                if (ch.HwType.Length == 0 || ch.HwHandle.Length == 0) return; // 未绑定硬件，操作列"-"，忽略
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
                case "colHwBind":
                    // 绑定硬件通道单下拉（对齐 CAN）：选中即固化 HwType+HwHandle；"不连接"清空
                    var bind = GetRowHwBind(row);
                    ch.HwType = bind.HwType;
                    ch.HwHandle = bind.HwHandle;
                    UpdateRowStatus(row); // 操作列 "-/连接/断开" 联动
                    UpdateConflict(row);   // 冲突标红即时刷新
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
