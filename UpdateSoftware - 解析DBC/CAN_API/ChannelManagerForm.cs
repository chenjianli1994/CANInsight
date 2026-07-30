using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// 统一通道管理窗口：硬件识别 / 逻辑通道配置 / DBC加载 / 硬件↔逻辑绑定 / 连接操作一窗统管。
    /// 同屏展示"硬件通道 ↔ 逻辑CAN通道 ↔ DBC文件"完整对应关系；
    /// 支持混合硬件（不同逻辑通道分别走PCAN/CANoe，绑定下拉选中即固化 HwType+HwChannel 二元组）。
    /// 替代原"通道配置"(CanBusChannelConfigDialog)与"连接映射"(ChannelMappingDialog)两个对话框。
    /// </summary>
    public class ChannelManagerForm : Form
    {
        /// <summary>配置是否已保存（供调用方决定是否执行后置刷新）</summary>
        public bool ConfigSaved { get; private set; }

        private readonly Main _main;
        private List<HwChannelInfo> _hwList = new List<HwChannelInfo>(); // 识别到的硬件（PCAN+CANoe合并）
        private bool _suppressModeEvent; // 初始化模式单选时抑制事件

        private Label _lblHwStatus;
        private RadioButton _rbCan;
        private RadioButton _rbCanFd;
        private DataGridView _dgv;
        private TextBox _txtPreview;
        private Button _btnConnectPcan;
        private Button _btnConnectCanoe;
        private Button _btnSave;
        private Timer _statusTimer;

        private const int ColName = 0;
        private const int ColBlf = 1;
        private const int ColHwBind = 2;
        private const int ColDbc = 3;
        private const int ColBrowse = 4;
        private const int ColDbcStatus = 5;

        /// <summary>绑定硬件下拉项（携带硬件类型+通道号，选中即固化二元组；Hw=0表示不连接）</summary>
        private class HwBindItem
        {
            public string HwType = "";
            public byte Hw;
            public string Display = "";
            public override string ToString() { return Display; }
        }

        private static readonly HwBindItem NotConnectItem = new HwBindItem { HwType = "", Hw = 0, Display = "不连接" };

        public ChannelManagerForm(Main main)
        {
            _main = main;
            BuildUi();
            RefreshHardware();   // 打开时主动识别一次
            LoadChannelRows();   // 从全局通道配置填充表格
            RefreshConnButtons();
            RefreshPreview();
        }

        private void BuildUi()
        {
            this.Text = "通道管理";
            this.ClientSize = new Size(878, 564);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Microsoft YaHei", 9F);

            // === 顶部：硬件识别状态 + 模式 + 刷新 ===
            _lblHwStatus = new Label
            {
                Location = new Point(12, 12),
                Size = new Size(500, 34),
                Text = "PCAN: -   CANoe: -"
            };
            this.Controls.Add(_lblHwStatus);

            var lblMode = new Label { Text = "模式:", Location = new Point(524, 20), Size = new Size(40, 20) };
            this.Controls.Add(lblMode);
            _rbCan = new RadioButton { Text = "CAN", Location = new Point(566, 18), Size = new Size(52, 22) };
            _rbCanFd = new RadioButton { Text = "CAN FD", Location = new Point(622, 18), Size = new Size(72, 22) };
            _suppressModeEvent = true;
            _rbCanFd.Checked = Main.CanFDFlag;
            _rbCan.Checked = !Main.CanFDFlag;
            _suppressModeEvent = false;
            _rbCan.CheckedChanged += ModeRadio_CheckedChanged;
            _rbCanFd.CheckedChanged += ModeRadio_CheckedChanged;
            this.Controls.Add(_rbCan);
            this.Controls.Add(_rbCanFd);

            var btnRefresh = new Button { Text = "刷新识别", Location = new Point(772, 10), Size = new Size(94, 32) };
            btnRefresh.Click += (s, e) => RefreshHardware();
            this.Controls.Add(btnRefresh);

            // === 中部：逻辑通道配置表格 ===
            _dgv = new DataGridView
            {
                Location = new Point(12, 52),
                Size = new Size(854, 236),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "通道名称", FillWeight = 13 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "blf", HeaderText = "BLF通道号", FillWeight = 10 });
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "hwBind", HeaderText = "绑定硬件通道", DisplayMember = "Display", FillWeight = 30, FlatStyle = FlatStyle.Flat });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "dbc", HeaderText = "DBC文件路径", ReadOnly = true, FillWeight = 29 });
            _dgv.Columns.Add(new DataGridViewButtonColumn { Name = "browse", HeaderText = "浏览", Text = "...", UseColumnTextForButtonValue = true, FillWeight = 8 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "dbcStatus", HeaderText = "DBC状态", ReadOnly = true, FillWeight = 10 });
            _dgv.CurrentCellDirtyStateChanged += Dgv_CurrentCellDirtyStateChanged;
            _dgv.CellValueChanged += Dgv_CellValueChanged;
            _dgv.CellContentClick += Dgv_CellContentClick;
            _dgv.DataError += (s, e) => { e.ThrowException = false; }; // 绑定项不在数据源时静默（不在位硬件占位项）
            this.Controls.Add(_dgv);

            var btnAdd = new Button { Text = "添加通道", Location = new Point(12, 296), Size = new Size(96, 28) };
            btnAdd.Click += BtnAdd_Click;
            this.Controls.Add(btnAdd);
            var btnRemove = new Button { Text = "删除通道", Location = new Point(116, 296), Size = new Size(96, 28) };
            btnRemove.Click += BtnRemove_Click;
            this.Controls.Add(btnRemove);

            // === 映射预览（硬件视角，只读） ===
            var lblPreview = new Label
            {
                Text = "映射预览（硬件视角，随编辑实时刷新）：",
                Location = new Point(12, 332),
                Size = new Size(400, 18)
            };
            this.Controls.Add(lblPreview);
            _txtPreview = new TextBox
            {
                Location = new Point(12, 352),
                Size = new Size(854, 150),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = SystemColors.Window
            };
            this.Controls.Add(_txtPreview);

            // === 底部：连接操作 + 保存/关闭 ===
            _btnConnectPcan = new Button { Text = "连接PCAN", Location = new Point(12, 516), Size = new Size(120, 36) };
            _btnConnectPcan.Click += BtnConnectPcan_Click;
            this.Controls.Add(_btnConnectPcan);
            _btnConnectCanoe = new Button { Text = "连接CANoe", Location = new Point(140, 516), Size = new Size(120, 36) };
            _btnConnectCanoe.Click += BtnConnectCanoe_Click;
            this.Controls.Add(_btnConnectCanoe);

            _btnSave = new Button { Text = "保存配置", Location = new Point(652, 516), Size = new Size(100, 36) };
            _btnSave.Click += (s, e) => SaveConfig(true);
            this.Controls.Add(_btnSave);
            var btnClose = new Button { Text = "关闭", Location = new Point(760, 516), Size = new Size(106, 36), DialogResult = DialogResult.Cancel };
            this.Controls.Add(btnClose);
            this.CancelButton = btnClose;

            // 连接动作异步执行（Main内BeginInvoke），用一次性Timer延迟刷新窗口状态
            _statusTimer = new Timer { Interval = 600 };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                RefreshHardware();
                RefreshConnButtons();
            };
            this.FormClosed += (s, e) => { _statusTimer.Stop(); _statusTimer.Dispose(); };
        }

        private void ModeRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressModeEvent || _main == null) return;
            _main.SetCanFdMode(_rbCanFd.Checked);
        }

        /// <summary>主动刷新硬件识别并重建绑定下拉选项（尽量保持各行原选择）</summary>
        private void RefreshHardware()
        {
            if (_main == null) return;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                _main.RefreshHardwareDetection();
                _hwList = _main.PcanHwChannels.Concat(_main.CanoeHwChannels).ToList();
                foreach (DataGridViewRow row in _dgv.Rows) RebuildBindCellDataSource(row);
                UpdateHwStatusLabel();
                RefreshPreview();
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        private void UpdateHwStatusLabel()
        {
            int pcanN = _main.PcanHwChannels.Count;
            int canoeN = _main.CanoeHwChannels.Count;
            _lblHwStatus.Text =
                $"PCAN: {(pcanN > 0 ? $"已识别{pcanN}路" : "未识别到设备")}（{(Main.pcanOpenFlag ? "已连接" : "未连接")}）\r\n" +
                $"CANoe: {(canoeN > 0 ? $"已识别{canoeN}路" : "未识别到设备")}（{(Main.canoeOpenFlag ? "已连接" : "未连接")}）";
        }

        private void RefreshConnButtons()
        {
            _btnConnectPcan.Text = Main.pcanOpenFlag ? "断开PCAN" : "连接PCAN";
            _btnConnectCanoe.Text = Main.canoeOpenFlag ? "断开CANoe" : "连接CANoe";
        }

        /// <summary>构建绑定下拉选项：不连接 + 识别到的全部硬件通道（带类型前缀与状态）</summary>
        private List<HwBindItem> BuildBindItems()
        {
            var items = new List<HwBindItem> { NotConnectItem };
            foreach (var hw in _hwList)
            {
                string status = string.IsNullOrEmpty(hw.Status) ? "" : $"({hw.Status})";
                items.Add(new HwBindItem { HwType = hw.HwType, Hw = hw.Hw, Display = $"{hw.HwType} {hw.Name}{status}" });
            }
            return items;
        }

        /// <summary>重建某行绑定下拉的数据源（识别结果变化后），尽量保持原选择；原绑定硬件不在位时保留占位项</summary>
        private void RebuildBindCellDataSource(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[ColHwBind];
            var cur = cell.Value as HwBindItem;
            var items = BuildBindItems();
            cell.DataSource = items;
            if (cur == null || cur.Hw == 0)
            {
                cell.Value = NotConnectItem;
                return;
            }
            var keep = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == cur.Hw && (cur.HwType == "" || x.HwType == cur.HwType));
            if (keep != null)
            {
                cell.Value = keep;
            }
            else
            {
                // 原绑定硬件当前不在位：保留占位项（不丢配置，预览中提示）
                items.Add(cur);
                cell.Value = cur;
            }
        }

        /// <summary>按通道当前绑定找下拉项：已保存绑定 > 不在位占位 > 同号预选 > 不连接</summary>
        private HwBindItem FindBindItem(DataGridViewRow row, string hwType, byte hwChannel, int logicIndex)
        {
            var items = (List<HwBindItem>)((DataGridViewComboBoxCell)row.Cells[ColHwBind]).DataSource;
            if (hwChannel == BaseParamter.HwNotConnect) return NotConnectItem;
            if (hwChannel > 0)
            {
                var m = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == hwChannel && (hwType == "" || x.HwType == hwType));
                if (m != null) return m;
                // 绑定硬件当前不在位：插入占位项保留原绑定（类型未知时无前缀）
                var absent = new HwBindItem
                {
                    HwType = hwType ?? "",
                    Hw = hwChannel,
                    Display = string.IsNullOrEmpty(hwType) ? $"通道{hwChannel}(不在位)" : $"{hwType} 通道{hwChannel}(不在位)"
                };
                items.Add(absent);
                return absent;
            }
            // HwChannel=0（跟随）：预选同号硬件（逻辑序号=索引+1），找不到则不连接
            byte sameNo = (byte)(logicIndex + 1);
            var same = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == sameNo);
            return same ?? NotConnectItem;
        }

        /// <summary>从全局通道配置填充表格</summary>
        private void LoadChannelRows()
        {
            _dgv.Rows.Clear();
            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
            {
                var ch = BaseParamter.BusChannels[i];
                int r = _dgv.Rows.Add();
                var row = _dgv.Rows[r];
                row.Cells[ColName].Value = ch.Name;
                row.Cells[ColBlf].Value = ch.BlfChannelId.ToString();
                RebuildBindCellDataSource(row);
                row.Cells[ColHwBind].Value = FindBindItem(row, ch.HwType, ch.HwChannel, i);
                row.Cells[ColDbc].Value = ch.DbcFilePath ?? "";
                row.Cells[ColDbcStatus].Value = DbcStatusText(ch.DbcFilePath, ch.IsConfigured);
            }
        }

        private static string DbcStatusText(string dbcPath, bool isConfigured)
        {
            if (string.IsNullOrWhiteSpace(dbcPath)) return "";
            return isConfigured ? "√ 已加载" : "× 未加载";
        }

        // === 表格事件 ===

        private void Dgv_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // 下拉选择立即提交，触发CellValueChanged刷新预览
            if (_dgv.IsCurrentCellDirty && _dgv.CurrentCell is DataGridViewComboBoxCell)
                _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Dgv_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            RefreshPreview();
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColBrowse) return;
            var row = _dgv.Rows[e.RowIndex];
            using (var ofd = new OpenFileDialog { Filter = "DBC文件 (*.dbc)|*.dbc|所有文件 (*.*)|*.*" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                row.Cells[ColDbc].Value = ofd.FileName;
                // 即时试解析反馈状态列（保存时会再次正式加载）
                try
                {
                    var helper = new DbcHelper();
                    helper.Parse(ofd.FileName);
                    row.Cells[ColDbcStatus].Value = "√ 已加载";
                }
                catch
                {
                    row.Cells[ColDbcStatus].Value = "× 加载失败";
                }
                RefreshPreview();
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            int n = _dgv.Rows.Count + 1;
            int r = _dgv.Rows.Add();
            var row = _dgv.Rows[r];
            row.Cells[ColName].Value = "CAN" + n;
            row.Cells[ColBlf].Value = n.ToString();
            RebuildBindCellDataSource(row);
            row.Cells[ColHwBind].Value = FindBindItem(row, "", 0, n - 1); // 同号预选
            row.Cells[ColDbc].Value = "";
            row.Cells[ColDbcStatus].Value = "";
            RefreshPreview();
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (_dgv.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选中要删除的通道行", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (DataGridViewRow row in _dgv.SelectedRows)
            {
                if (!row.IsNewRow) _dgv.Rows.Remove(row);
            }
            RefreshPreview();
        }

        // === 映射预览 ===

        /// <summary>硬件视角预览：每路识别到的硬件 → 逻辑通道 [DBC]；附冲突/不在位/未关联警告</summary>
        private void RefreshPreview()
        {
            if (_txtPreview == null) return;
            var sb = new StringBuilder();
            var hwBound = new Dictionary<string, string>(); // "TYPE:hw" → 通道名
            var conflicts = new List<string>();

            // 收集各行绑定
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                string name = (row.Cells[ColName].Value?.ToString() ?? "").Trim();
                var bind = row.Cells[ColHwBind].Value as HwBindItem ?? NotConnectItem;
                if (bind.Hw == 0) continue;
                string key = bind.HwType + ":" + bind.Hw;
                if (hwBound.ContainsKey(key))
                    conflicts.Add($"!! 冲突：{hwBound[key]} 与 {name} 绑定同一硬件通道（{bind.Display}）");
                else
                    hwBound[key] = name;
            }

            // 硬件视角明细
            if (_hwList.Count == 0)
            {
                sb.AppendLine("（未识别到硬件，请点击右上角\"刷新识别\"）");
            }
            foreach (var hw in _hwList)
            {
                string status = string.IsNullOrEmpty(hw.Status) ? "" : $"({hw.Status})";
                string key = hw.HwType + ":" + hw.Hw;
                if (hwBound.TryGetValue(key, out string chName))
                {
                    string dbc = GetRowDbcFileName(chName);
                    sb.AppendLine($"{hw.HwType} {hw.Name}{status}  ->  {chName} [{dbc}]");
                }
                else
                {
                    sb.AppendLine($"{hw.HwType} {hw.Name}{status}  ->  （不连接）");
                }
            }

            // 警告区
            foreach (var c in conflicts) sb.AppendLine(c);
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                string name = (row.Cells[ColName].Value?.ToString() ?? "").Trim();
                var bind = row.Cells[ColHwBind].Value as HwBindItem ?? NotConnectItem;
                if (bind.Hw == 0)
                {
                    sb.AppendLine($"[!] {name} 未关联硬件通道（不连接）");
                }
                else if (!_hwList.Any(h => h.Hw == bind.Hw && (bind.HwType == "" || h.HwType == bind.HwType)))
                {
                    sb.AppendLine($"[!] {name} 绑定的 {bind.Display} 当前未识别到");
                }
            }

            _txtPreview.Text = sb.ToString();
        }

        private string GetRowDbcFileName(string channelName)
        {
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                string name = (_dgv.Rows[i].Cells[ColName].Value?.ToString() ?? "").Trim();
                if (name == channelName)
                {
                    string p = _dgv.Rows[i].Cells[ColDbc].Value?.ToString() ?? "";
                    return string.IsNullOrEmpty(p) ? "未配置DBC" : Path.GetFileName(p);
                }
            }
            return "未配置DBC";
        }

        // === 保存 ===

        /// <summary>校验并保存：写回全局通道列表 → 持久化JSON → 刷新聚合DBC视图；失败弹提示返回false</summary>
        private bool SaveConfig(bool showSuccess)
        {
            _dgv.EndEdit();
            var channels = new List<CanBusChannel>();
            var hwUsed = new Dictionary<string, string>(); // 映射一一对应检测

            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                string name = (row.Cells[ColName].Value?.ToString() ?? "").Trim();
                string blfStr = row.Cells[ColBlf].Value?.ToString() ?? "";
                var bind = row.Cells[ColHwBind].Value as HwBindItem ?? NotConnectItem;
                string dbcPath = row.Cells[ColDbc].Value?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show($"第{i + 1}行：通道名称不能为空", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (!byte.TryParse(blfStr, out byte blfChannelId))
                {
                    MessageBox.Show($"第{i + 1}行：BLF通道号格式错误", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (bind.Hw != 0)
                {
                    string key = bind.HwType + ":" + bind.Hw;
                    if (hwUsed.TryGetValue(key, out string other))
                    {
                        MessageBox.Show($"第{i + 1}行：硬件通道 [{bind.Display}] 已被 [{other}] 绑定，请调整为一一对应。",
                            "映射冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    hwUsed[key] = name;
                }

                var channel = new CanBusChannel(name, blfChannelId, dbcPath);
                if (bind.Hw == 0)
                {
                    channel.HwChannel = BaseParamter.HwNotConnect; // 不连接哨兵
                    channel.HwType = "";
                }
                else
                {
                    channel.HwChannel = bind.Hw; // 选中即固化 类型+通道号 二元组
                    channel.HwType = bind.HwType;
                }

                // 配置了DBC路径则试加载（失败阻止保存，与原通道配置对话框一致）
                if (!string.IsNullOrWhiteSpace(dbcPath))
                {
                    try
                    {
                        channel.DbcHelper = new DbcHelper();
                        channel.DbcHelper.Parse(dbcPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"第{i + 1}行：DBC文件加载失败\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }
                channels.Add(channel);
            }

            // 写回全局唯一数据源并持久化（与原通道配置对话框"确定"后的动作一致）
            BaseParamter.BusChannels = channels;
            BaseParamter.SaveBusChannelsConfig();
            BaseParamter.RefreshGlobalDbcFromChannels();
            ConfigSaved = true;

            for (int i = 0; i < _dgv.Rows.Count && i < channels.Count; i++)
                _dgv.Rows[i].Cells[ColDbcStatus].Value = DbcStatusText(channels[i].DbcFilePath, channels[i].IsConfigured);
            _main?.RefreshChannelComboState(); // 通道配置变化：刷新连接区显示（单/多通道模式切换）
            RefreshPreview();
            if (showSuccess)
                MessageBox.Show("通道配置已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        // === 连接操作 ===

        private void BtnConnectPcan_Click(object sender, EventArgs e)
        {
            if (_main == null) return;
            // 连接动作前固化当前编辑（连接使用全局配置）；断开动作无需保存
            if (!Main.pcanOpenFlag && !SaveConfig(false))
            {
                MessageBox.Show("请先修正配置后再连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _main.PCAN_Connect(true); // 内部BeginInvoke异步执行 连接/断开 切换
            _statusTimer.Stop();
            _statusTimer.Start();   // 延迟刷新窗口状态（识别状态/按钮文本/预览）
        }

        private void BtnConnectCanoe_Click(object sender, EventArgs e)
        {
            if (_main == null) return;
            if (!Main.canoeOpenFlag && !SaveConfig(false))
            {
                MessageBox.Show("请先修正配置后再连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _main.CANoeConnect(true);
            _statusTimer.Stop();
            _statusTimer.Start();
        }
    }
}
