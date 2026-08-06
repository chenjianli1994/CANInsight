using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private readonly bool _realTimeMode; // 打开窗口时绘图区所处模式：true=实时数据（通道号用于记录导出编号），false=报文数据（BLF通道号用于回放映射）
        private List<HwChannelInfo> _hwList = new List<HwChannelInfo>(); // 识别到的硬件（PCAN+CANoe合并）
        private bool _detecting;         // 后台识别进行中（重入保护）

        /// <summary>通道号列标题随模式切换：实时="通道号"，报文="BLF通道号"</summary>
        private string BlfColumnTitle => _realTimeMode ? "通道号" : "BLF通道号";

        private Label _lblHwStatus;
        private DataGridView _dgv;
        private TextBox _txtPreview;
        private Button _btnConnectAll;
        private Button _btnSave;
        private Timer _statusTimer;

        private const int ColName = 0;
        private const int ColBlf = 1;
        private const int ColHwBind = 2;
        private const int ColDbc = 3;
        private const int ColBrowse = 4;
        private const int ColDbcStatus = 5;
        private const int ColConn = 6;
        private const int ColMode = 7; // CAN/CANFD 模式下拉
        private const int ColBaud = 8; // 波特率档位下拉

        /// <summary>绑定硬件下拉项（携带硬件类型+通道号，选中即固化二元组；Hw=0表示不连接）</summary>
        private class HwBindItem
        {
            // 注意：DisplayMember/ValueMember 数据绑定只认属性，必须是属性不能是字段
            public string HwType { get; set; } = "";
            public byte Hw { get; set; }
            public string Display { get; set; } = "";
            /// <summary>下拉ValueMember唯一键（"类型:通道号"）；cell.Value存此键字符串，避免对象引用匹配在失焦重绘时回退</summary>
            public string Key => HwType + ":" + Hw;
            public override string ToString() { return Display; }
        }

        private static readonly HwBindItem NotConnectItem = new HwBindItem { HwType = "", Hw = 0, Display = "不连接" };

        public ChannelManagerForm(Main main)
        {
            _main = main;
            _realTimeMode = Main.chartFromShow?.RealTimeDataSta ?? true;
            BuildUi();
            // 先用Main现有识别缓存立即填充（窗口秒开）；硬件识别为耗时操作（PCAN试开16槽位约1-2秒），窗口显示后后台异步刷新
            if (_main != null)
            {
                _hwList = _main.PcanHwChannels.Concat(_main.CanoeHwChannels).ToList();
            }
            UpdateHwStatusLabel();
            LoadChannelRows();   // 从全局通道配置填充表格
            RefreshAllConnButtons();
            RefreshPreview();
            this.Shown += (s, e) => RefreshHardwareAsync();
        }

        private void BuildUi()
        {
            this.Text = "通道管理";
            this.ClientSize = new Size(1030, 564);
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

            var btnRefresh = new Button { Text = "刷新识别", Location = new Point(912, 10), Size = new Size(94, 32) };
            btnRefresh.Click += (s, e) => RefreshHardwareAsync();
            this.Controls.Add(btnRefresh);

            // === 中部：逻辑通道配置表格 ===
            _dgv = new DataGridView
            {
                Location = new Point(12, 52),
                Size = new Size(996, 236),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "通道名称", FillWeight = 11 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "blf", HeaderText = BlfColumnTitle, FillWeight = 8 });
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "hwBind", HeaderText = "绑定硬件通道", DisplayMember = "Display", ValueMember = "Key", FillWeight = 30, FlatStyle = FlatStyle.Flat });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "dbc", HeaderText = "DBC文件路径", ReadOnly = true, FillWeight = 22 });
            _dgv.Columns.Add(new DataGridViewButtonColumn { Name = "browse", HeaderText = "浏览", Text = "...", UseColumnTextForButtonValue = true, FillWeight = 6 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "dbcStatus", HeaderText = "DBC状态", ReadOnly = true, FillWeight = 8 });
            _dgv.Columns.Add(new DataGridViewButtonColumn { Name = "conn", HeaderText = "操作", FillWeight = 8 });
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "mode", HeaderText = "模式", Items = { "CAN", "CANFD" }, FillWeight = 9, FlatStyle = FlatStyle.Flat });
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "baud", HeaderText = "波特率", FillWeight = 16, FlatStyle = FlatStyle.Flat });
            _dgv.CurrentCellDirtyStateChanged += Dgv_CurrentCellDirtyStateChanged;
            _dgv.CellValueChanged += Dgv_CellValueChanged;
            _dgv.CellContentClick += Dgv_CellContentClick;
            _dgv.DataError += (s, e) => { e.ThrowException = false; }; // 重建数据源瞬间旧键值暂不在新列表时静默（随后立即重设）
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
                Size = new Size(996, 150),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = SystemColors.Window
            };
            this.Controls.Add(_txtPreview);

            // === 底部：一键连接/断开所有通道 + 保存/关闭 ===
            _btnConnectAll = new Button { Text = "一键连接所有通道", Location = new Point(12, 516), Size = new Size(170, 36) };
            _btnConnectAll.Click += BtnConnectAll_Click;
            this.Controls.Add(_btnConnectAll);

            _btnSave = new Button { Text = "保存配置", Location = new Point(784, 516), Size = new Size(100, 36) };
            _btnSave.Click += (s, e) => SaveConfig(true, true); // 手动保存：已连接通道配置变化时按新参数自动重连
            this.Controls.Add(_btnSave);
            var btnClose = new Button { Text = "关闭", Location = new Point(892, 516), Size = new Size(106, 36), DialogResult = DialogResult.Cancel };
            this.Controls.Add(btnClose);
            this.CancelButton = btnClose;

            // 连接动作异步执行（Main内BeginInvoke），用一次性Timer延迟刷新窗口状态（本地重算，不做硬件识别）
            _statusTimer = new Timer { Interval = 600 };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                if (_main == null) return;
                _main.RefreshHwConnectedStatusLocal(); // 本地重算各硬件通道"已连接"状态（不访问硬件）
                _hwList = _main.PcanHwChannels.Concat(_main.CanoeHwChannels).ToList();
                // 非编辑状态下重建下拉数据源，同步选项里的"(已连接)"状态后缀（编辑中跳过避免打断）
                if (!_dgv.IsCurrentCellInEditMode)
                    foreach (DataGridViewRow row in _dgv.Rows) RebuildBindCellDataSource(row);
                UpdateHwStatusLabel();
                RefreshAllConnButtons();
                RefreshPreview();
            };
            this.FormClosed += (s, e) => { _statusTimer.Stop(); _statusTimer.Dispose(); };
        }

        /// <summary>后台异步刷新硬件识别（PCAN试开16槽位约1-2秒，不阻塞UI），完成后重建绑定下拉选项（尽量保持各行原选择，不在位回退"不连接"）</summary>
        private void RefreshHardwareAsync()
        {
            if (_main == null || _detecting) return;
            _detecting = true;
            _lblHwStatus.Text = "正在识别硬件...";
            Task.Run(() =>
            {
                try { _main.RefreshHardwareDetection(); } catch { /* 单个设备枚举失败不影响另一个 */ }
                try
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((EventHandler)(delegate
                        {
                            _detecting = false;
                            _hwList = _main.PcanHwChannels.Concat(_main.CanoeHwChannels).ToList();
                            foreach (DataGridViewRow row in _dgv.Rows) RebuildBindCellDataSource(row);
                            UpdateHwStatusLabel();
                            RefreshAllConnButtons();
                            RefreshPreview();
                        }));
                    }
                    else
                    {
                        _detecting = false;
                    }
                }
                catch { _detecting = false; } // 窗口已关闭时BeginInvoke失败，忽略
            });
        }

        private void UpdateHwStatusLabel()
        {
            if (_main == null) return;
            int pcanN = _main.PcanHwChannels.Count;
            int canoeN = _main.CanoeHwChannels.Count;
            _lblHwStatus.Text =
                $"PCAN: {(pcanN > 0 ? $"已识别{pcanN}路" : "未识别到设备")}（{(Main.pcanOpenFlag ? "已连接" : "未连接")}）\r\n" +
                $"CANoe: {(canoeN > 0 ? $"已识别{canoeN}路" : "未识别到设备")}（{(Main.canoeOpenFlag ? "已连接" : "未连接")}）";
        }

        /// <summary>统一刷新连接按钮状态：一键按钮文本（全部已绑定通道都连上→"一键断开"）+ 各行操作按钮（已连接→"断开"，未连接→"连接"，无绑定→禁用灰显）</summary>
        private void RefreshAllConnButtons()
        {
            if (_main == null) return;
            _btnConnectAll.Text = _main.AllBoundChannelsConnected() ? "一键断开所有通道" : "一键连接所有通道";
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var bind = GetRowBindItem(_dgv.Rows[i]);
                var cell = (DataGridViewButtonCell)_dgv.Rows[i].Cells[ColConn];
                if (bind.Hw == 0)
                {
                    cell.Value = "-";
                    cell.Style.ForeColor = SystemColors.GrayText; // 无绑定硬件：禁用灰显（点击处理中同步拦截）
                }
                else
                {
                    cell.Value = _main.GetChannelConnected(i) ? "断开" : "连接";
                    cell.Style.ForeColor = _dgv.DefaultCellStyle.ForeColor;
                }
            }
        }

        /// <summary>构建绑定下拉选项：不连接 + 识别到的全部硬件通道（带类型前缀与状态；冲突不限制选择，由标红提示+连接时拦截）</summary>
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

        /// <summary>读取某行当前绑定项（cell.Value为Key字符串，从该行数据源按键反查；无匹配视为不连接）</summary>
        private HwBindItem GetRowBindItem(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[ColHwBind];
            string key = cell.Value?.ToString();
            var items = cell.DataSource as List<HwBindItem>;
            if (string.IsNullOrEmpty(key) || items == null) return NotConnectItem;
            return items.FirstOrDefault(x => x.Key == key) ?? NotConnectItem;
        }

        /// <summary>重建某行绑定下拉的数据源（识别结果变化后），尽量保持原选择；原绑定硬件不在位时直接回退"不连接"</summary>
        private void RebuildBindCellDataSource(DataGridViewRow row)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[ColHwBind];
            var cur = cell.Value == null ? null : GetRowBindItem(row);
            var items = BuildBindItems();
            cell.DataSource = items;
            if (cur == null) return; // 尚未设置过值（初始化场景，由FindBindKey随后赋值）
            if (cur.Hw == 0)
            {
                cell.Value = NotConnectItem.Key;
                return;
            }
            var keep = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == cur.Hw && (cur.HwType == "" || x.HwType == cur.HwType));
            // 原绑定硬件当前不在位：直接回退"不连接"（重新插入并刷新后需重新选择）
            cell.Value = (keep ?? NotConnectItem).Key;
        }

        /// <summary>按行模式重建该行波特率下拉数据源并回退默认档（模式切换时调用）</summary>
        private void SetupBaudCell(DataGridViewRow row, bool fd)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[ColBaud];
            cell.DataSource = fd
                ? BaudrateConfig.FdPresets.Select(p => p.Name).ToList()
                : BaudrateConfig.ClassicPresets.Select(p => p.Name).ToList();
            if (cell.Value == null || !cell.Items.Contains(cell.Value))
                cell.Value = fd ? BaudrateConfig.DefaultFdName : BaudrateConfig.DefaultClassicName;
        }

        /// <summary>按通道当前绑定找下拉项键值：已保存绑定 > 同号预选 > 不连接（绑定硬件不在位时回退"不连接"）</summary>
        private string FindBindKey(DataGridViewRow row, string hwType, byte hwChannel, int logicIndex)
        {
            var items = (List<HwBindItem>)((DataGridViewComboBoxCell)row.Cells[ColHwBind]).DataSource;
            if (hwChannel == BaseParamter.HwNotConnect) return NotConnectItem.Key;
            if (hwChannel > 0)
            {
                // 已保存的精确绑定（类型+通道号）；绑定硬件当前不在位则回退"不连接"
                var m = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == hwChannel && (hwType == "" || x.HwType == hwType));
                return (m ?? NotConnectItem).Key;
            }
            // HwChannel=0（跟随）：预选同号硬件，找不到则不连接
            byte sameNo = BaseParamter.GetLogicChannel(logicIndex);
            var same = items.FirstOrDefault(x => x.Hw != 0 && x.Hw == sameNo);
            return (same ?? NotConnectItem).Key;
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
                row.Cells[ColHwBind].Value = FindBindKey(row, ch.HwType, ch.HwChannel, i);
                row.Cells[ColDbc].Value = ch.DbcFilePath ?? "";
                row.Cells[ColDbcStatus].Value = DbcStatusText(ch.DbcFilePath, ch.IsConfigured);
                row.Cells[ColMode].Value = ch.CanFd ? "CANFD" : "CAN";
                SetupBaudCell(row, ch.CanFd);
                row.Cells[ColBaud].Value = string.IsNullOrEmpty(ch.Baudrate)
                    ? (ch.CanFd ? BaudrateConfig.DefaultFdName : BaudrateConfig.DefaultClassicName)
                    : ch.Baudrate;
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
            // 绑定列变化后行操作按钮状态可能变化（无绑定↔有绑定）
            if (e.ColumnIndex == ColHwBind) RefreshAllConnButtons();
            if (e.ColumnIndex == ColMode)
            {
                var row = _dgv.Rows[e.RowIndex];
                bool fd = (row.Cells[ColMode].Value?.ToString() == "CANFD");
                SetupBaudCell(row, fd); // 重建波特率下拉并回退默认档
            }
            RefreshPreview();
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            // "操作"列：行级连接/断开（无绑定行显示"-"，点击忽略）
            if (e.ColumnIndex == ColConn)
            {
                var bindItem = GetRowBindItem(_dgv.Rows[e.RowIndex]);
                if (bindItem.Hw == 0) return;
                ConnectRowChannel(e.RowIndex);
                return;
            }
            if (e.ColumnIndex != ColBrowse) return;
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

        private int GetNextChannelNumber()
        {
            var usedNumbers = new HashSet<int>();
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                string name = row.Cells[ColName].Value?.ToString()?.Trim() ?? "";
                if (name.StartsWith("CAN", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name.Substring(3), out int number) && number > 0)
                {
                    usedNumbers.Add(number);
                }
            }

            int next = 1;
            while (usedNumbers.Contains(next)) next++;
            return next;
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            int n = GetNextChannelNumber();
            int r = _dgv.Rows.Add();
            var row = _dgv.Rows[r];
            row.Cells[ColName].Value = "CAN" + n;
            row.Cells[ColBlf].Value = n.ToString();
            RebuildBindCellDataSource(row);
            row.Cells[ColHwBind].Value = FindBindKey(row, "", 0, n - 1); // 同号预选
            row.Cells[ColDbc].Value = "";
            row.Cells[ColDbcStatus].Value = "";
            // 新行默认经典CAN+默认档（模式/波特率按行独立配置）
            row.Cells[ColMode].Value = "CAN";
            SetupBaudCell(row, false);
            row.Cells[ColBaud].Value = BaudrateConfig.DefaultClassicName;
            RefreshAllConnButtons();
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
            RefreshAllConnButtons();
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
                var bind = GetRowBindItem(row);
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
                var bind = GetRowBindItem(row);
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
            UpdateConflictMarks();
        }

        /// <summary>标红绑定冲突：同一硬件通道被多行绑定时，这些行的绑定单元格文字显示为红色（视觉告警，不限制选择）</summary>
        private void UpdateConflictMarks()
        {
            var keyCount = new Dictionary<string, int>();
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                string k = row.Cells[ColHwBind].Value?.ToString();
                if (string.IsNullOrEmpty(k) || k == NotConnectItem.Key) continue;
                keyCount[k] = keyCount.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                string k = row.Cells[ColHwBind].Value?.ToString();
                bool conflict = !string.IsNullOrEmpty(k) && k != NotConnectItem.Key
                    && keyCount.TryGetValue(k, out int cnt) && cnt > 1;
                row.Cells[ColHwBind].Style.ForeColor = conflict ? Color.Red : _dgv.DefaultCellStyle.ForeColor;
            }
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

        /// <summary>校验并保存：写回全局通道列表 → 持久化JSON → 刷新聚合DBC视图；失败弹提示返回false。
        /// autoReconnect=true（手动"保存配置"）时，配置发生变化且已连接的通道按新参数自动重连（其他通道零中断、不清空数据）</summary>
        private bool SaveConfig(bool showSuccess, bool autoReconnect = false)
        {
            _dgv.EndEdit();
            var oldChannels = BaseParamter.BusChannels; // 写回前捕获旧配置，供自动重连对比
            var channels = new List<CanBusChannel>();
            var hwUsed = new Dictionary<string, string>(); // 映射一一对应检测
            var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                string name = (row.Cells[ColName].Value?.ToString() ?? "").Trim();
                string blfStr = row.Cells[ColBlf].Value?.ToString() ?? "";
                var bind = GetRowBindItem(row);
                string dbcPath = row.Cells[ColDbc].Value?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show($"第{i + 1}行：通道名称不能为空", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (!channelNames.Add(name))
                {
                    MessageBox.Show($"第{i + 1}行：通道名称 [{name}] 重复，请修改后再保存", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (!byte.TryParse(blfStr, out byte blfChannelId))
                {
                    MessageBox.Show($"第{i + 1}行：{BlfColumnTitle}格式错误（0-255的数字）", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                channel.CanFd = (row.Cells[ColMode].Value?.ToString() == "CANFD");
                channel.Baudrate = row.Cells[ColBaud].Value?.ToString() ?? "";
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
            // 通道列表整体重建后，绘图区信号的ChartShowFlag/周期调度注册（挂在旧Message对象上）已失效，按当前曲线重建
            Main.chartFromShow?.RestoreChartShowFlags();
            ConfigSaved = true;

            for (int i = 0; i < _dgv.Rows.Count && i < channels.Count; i++)
                _dgv.Rows[i].Cells[ColDbcStatus].Value = DbcStatusText(channels[i].DbcFilePath, channels[i].IsConfigured);
            RefreshPreview();
            if (autoReconnect) ReconnectChangedChannels(oldChannels);
            if (showSuccess)
                MessageBox.Show("通道配置已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        /// <summary>保存后自动重连：对比新旧配置，仅重连"配置（模式/波特率/硬件绑定）发生变化且当前已连接"的通道，
        /// 走 Main.ReconnectSingleChannel（按新配置重开硬件，不清空已收数据，不打断其他通道）</summary>
        private void ReconnectChangedChannels(List<CanBusChannel> oldChannels)
        {
            if (_main == null) return;
            for (int i = 0; i < _dgv.Rows.Count && i < oldChannels.Count && i < BaseParamter.BusChannels.Count; i++)
            {
                var old = oldChannels[i];
                var cur = BaseParamter.BusChannels[i];
                bool changed = old.CanFd != cur.CanFd
                    || (old.Baudrate ?? "") != (cur.Baudrate ?? "")
                    || old.HwChannel != cur.HwChannel
                    || (old.HwType ?? "") != (cur.HwType ?? "");
                if (!changed || !_main.GetChannelConnected(i)) continue;
                _main.ReconnectSingleChannel(i);
            }
        }

        // === 连接操作 ===

        /// <summary>一键连接/断开所有通道：全部已绑定通道都连上时执行全断，否则连接未连的（连接前固化并校验当前编辑）</summary>
        private void BtnConnectAll_Click(object sender, EventArgs e)
        {
            if (_main == null) return;
            if (_main.AllBoundChannelsConnected())
            {
                _main.DisconnectAllChannels();
            }
            else
            {
                // 连接动作前固化当前编辑并检查（连接使用全局配置）：保存校验发现冲突/DBC错误时弹窗告警并中止连接，需重新分配硬件
                if (!SaveConfig(false)) return;
                _main.ConnectAllChannels();
            }
            _statusTimer.Stop();
            _statusTimer.Start();   // 延迟刷新窗口状态（本地重算，不做硬件识别）
        }

        /// <summary>行级连接/断开：点击该行"操作"列按钮（先固化并校验当前编辑，再按该行绑定的硬件类型增量连接/断开）</summary>
        private void ConnectRowChannel(int rowIndex)
        {
            if (_main == null) return;
            if (_main.GetChannelConnected(rowIndex))
            {
                _main.DisconnectSingleChannel(rowIndex);
            }
            else
            {
                if (!SaveConfig(false)) return;
                _main.ConnectSingleChannel(rowIndex);
            }
            _statusTimer.Stop();
            _statusTimer.Start();
        }
    }
}
