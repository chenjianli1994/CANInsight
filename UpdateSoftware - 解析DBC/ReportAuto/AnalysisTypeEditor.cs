// 工况分类编辑器:可视化编辑文字模板、占位符计算配置、PPT Shape选择
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PCAN_Client.ReportAuto
{
    /// <summary>
    /// 工况分类可视化编辑器。
    /// 用户可编辑文字模板（含 {{KEY}} 占位符）、为每个占位符配置信号和计算方式、
    /// 选择PPT中接收文字和图片的Shape名称。
    /// </summary>
    internal class AnalysisTypeEditor : Form
    {
        // 当前编辑的工况分类（可为null表示新建）
        private readonly AnalysisType _editingType;
        // 上次保存的工况名(初始=编辑中的工况名,新建=null;改名保存后跟踪新名,用于编辑器单例判定)
        private string _lastSavedName;
        // 当前绘图区已有的信号通道（供下拉选择）
        private readonly List<ChannelData> _channels;
        // CAN总线通道列表(供信号选择器显示多路CAN通道DBC)
        private readonly List<CanBusChannel> _busChannels;
        // 临时抑制TextChange同步,避免程序化插入文本时清空已有绑定
        private bool _suppressTextSync = false;
        // 语法高亮执行中标记(避免SelectionChanged误触发定位)
        private bool _highlighting = false;
        // 暂存从文本中移除的占位符绑定(剪切-粘贴瞬态:同名占位符再次出现时自动恢复)
        private readonly Dictionary<string, DetachedBinding> _detachedBindings = new Dictionary<string, DetachedBinding>();

        /// <summary>被移除占位符行的绑定快照</summary>
        private class DetachedBinding
        {
            public string SignalName;
            public string Calc;
            public string TimeRange;
            public string Unit;
        }

        // UI 控件
        private TextBox _txtName;
        private RichTextBox _rtbText;
        private RichTextBox _txtPreview;    // 实时预览:{{占位符}}→[信号·指标]
        private DataGridView _dgvPlaceholders;
        private Button _btnInsertPlaceholder;
        private Button _btnCalculate;      // 一键计算按钮
        private Button _btnSave;
        private Button _btnCancel;
        private Panel _rangeWarningPanel;    // 数据范围变化提示条(默认隐藏)
        private Label _rangeWarningLabel;

        // 结果
        public AnalysisType Result { get; private set; }

        /// <summary>编辑器当前对应的工况名(新建且未保存过=null;改名保存后为新名)</summary>
        public string EditingTypeName => _lastSavedName;

        /// <summary>编辑器保存工况分类时触发的事件，传递新保存的AnalysisType</summary>
        public event EventHandler<AnalysisType> Saved;

        /// <summary>一键计算前触发:请求宿主确保占位符信号的数据通道已建立(供宿主补建隐藏通道并补采)</summary>
        public event EventHandler EnsureSignalsRequested;

        /// <summary>
        /// 创建编辑器。editingType为null时新建，否则编辑现有类型。
        /// </summary>
        public AnalysisTypeEditor(AnalysisType editingType, List<ChannelData> channels, List<CanBusChannel> busChannels)
        {
            _editingType = editingType;
            _lastSavedName = editingType?.Name;
            _channels = channels ?? new List<ChannelData>();
            _busChannels = busChannels ?? new List<CanBusChannel>();
            InitUI();
            LoadFromType();
            CheckDataRangeMismatch();  // 打开时校验:配置时数据范围与当前是否一致
        }

        private void InitUI()
        {
            Text = "工况分类编辑器";
            Size = new Size(1000, 720);
            MinimumSize = new Size(700, 500);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;  // 允许最小化
            WindowState = FormWindowState.Maximized;  // 默认最大化
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(10);  // 四周留10px边距,不贴边框

            int btnW = 80;

            // === 顶部:名称 ===
            var panelTop = new Panel { Dock = DockStyle.Top, Height = 36, Margin = new Padding(8, 8, 8, 4) };
            panelTop.Controls.Add(new Label { Text = "名称:", Location = new Point(8, 9), AutoSize = true });
            _txtName = new TextBox { Location = new Point(55, 6), Width = 300, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            panelTop.Controls.Add(_txtName);

            // === 主分割(左右,宽度可调):左=文字+预览  右=占位符配置表 ===
            var splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Margin = new Padding(8, 4, 8, 4) };

            // 左栏:上下分割 文字区/预览区(高度可调,默认各占一半)
            var splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            
            // 文字区:标签 + 工具栏(插入占位符按钮) + 文本框
            var panelText = new Panel { Dock = DockStyle.Fill };
            _rtbText = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10F), AcceptsTab = true };
            _rtbText.TextChanged += RtbText_TextChanged;
            _rtbText.KeyDown += RtbText_KeyDown;
            _rtbText.SelectionChanged += RtbText_SelectionChanged;
            panelText.Controls.Add(_rtbText);  // Fill 先添加（后布局）
            // 标题行:标题居左 + 插入按钮居右(合并一行,节省纵向空间)
            var panelTextHeader = new Panel { Dock = DockStyle.Top, Height = 28 };
            panelTextHeader.Controls.Add(new Label
            {
                Text = "文字内容（可输入 {{占位符}}）:",
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            });
            _btnInsertPlaceholder = new Button
            {
                Text = "插入信号占位符...",
                Dock = DockStyle.Right,
                Width = 130,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White
            };
            _btnInsertPlaceholder.FlatAppearance.BorderSize = 0;
            _btnInsertPlaceholder.Click += BtnInsertPlaceholder_Click;
            panelTextHeader.Controls.Add(_btnInsertPlaceholder);
            panelText.Controls.Add(panelTextHeader);  // Top 最上
            splitLeft.Panel1.Controls.Add(panelText);
            
            // 预览区:Fill先添加,Top后添加(Dock反向布局)
            _txtPreview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Microsoft YaHei UI", 9F) };
            splitLeft.Panel2.Controls.Add(_txtPreview);
            splitLeft.Panel2.Controls.Add(new Label { Text = "预览（未计算:[信号·指标]; 一键计算后:直接显示计算结果）:", Dock = DockStyle.Top, Height = 20 });
            splitMain.Panel1.Controls.Add(splitLeft);

            // 右栏:占位符配置表(宽度随分隔条可调)
            // 标题行:标题居左 + 一键计算按钮居右(合并一行,节省纵向空间)
            var panelConfigHeader = new Panel { Dock = DockStyle.Top, Height = 28 };
            panelConfigHeader.Controls.Add(new Label
            {
                Text = "占位符计算配置:",
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            });
            _btnCalculate = new Button
            {
                Text = "一键计算",
                Dock = DockStyle.Right,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White
            };
            _btnCalculate.FlatAppearance.BorderSize = 0;
            _btnCalculate.Click += BtnCalculate_Click;
            panelConfigHeader.Controls.Add(_btnCalculate);
            _dgvPlaceholders = new DataGridView();
            _dgvPlaceholders.Dock = DockStyle.Fill;
            _dgvPlaceholders.AllowUserToAddRows = false;
            _dgvPlaceholders.AllowUserToDeleteRows = false;  // Delete键改由KeyDown统一走删除逻辑(连带清理文字)
            _dgvPlaceholders.ShowCellToolTips = true;        // 截断文本悬停显示全名
            _dgvPlaceholders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _dgvPlaceholders.AllowUserToResizeColumns = true;
            _dgvPlaceholders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvPlaceholders.RowHeadersVisible = false;
            _dgvPlaceholders.ColumnHeadersVisible = true;
            _dgvPlaceholders.BackgroundColor = SystemColors.Window;
            _dgvPlaceholders.GridColor = Color.LightGray;
            _dgvPlaceholders.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _dgvPlaceholders.EnableHeadersVisualStyles = false;
            
            // 显式设置表头样式
            var headerStyle = new DataGridViewCellStyle();
            headerStyle.BackColor = Color.LightSteelBlue;
            headerStyle.ForeColor = Color.Black;
            headerStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            headerStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            headerStyle.WrapMode = DataGridViewTriState.False;
            _dgvPlaceholders.ColumnHeadersDefaultCellStyle = headerStyle;
            
            _dgvPlaceholders.DataError += (s, e) => { e.Cancel = true; };
            // 列定义
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "占位符", FillWeight = 16 });
            _dgvPlaceholders.Columns.Add(new DataGridViewButtonColumn { Name = "SignalBtn", HeaderText = "信号", Text = "选择信号", UseColumnTextForButtonValue = true, FillWeight = 10 });
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "SignalName", HeaderText = "信号名", ReadOnly = true, FillWeight = 18 });
            _dgvPlaceholders.Columns.Add(new DataGridViewComboBoxColumn { Name = "Calc", HeaderText = "计算方式", FillWeight = 14, FlatStyle = FlatStyle.Flat, Items = { "平均值(avg)", "最大值(max)", "最小值(min)", "极差(range)" } });
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeRange", HeaderText = "时间范围", FillWeight = 16, ToolTipText = "格式: 开始时间,结束时间（秒）。留空时使用默认时间范围（灰色显示），输入自定义值可覆盖。" });
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "结果预览", ReadOnly = true, FillWeight = 16 });
            _dgvPlaceholders.Columns.Add(new DataGridViewButtonColumn { Name = "CopyBtn", HeaderText = "复制", Text = "复制", UseColumnTextForButtonValue = true, FillWeight = 7 });
            _dgvPlaceholders.Columns.Add(new DataGridViewButtonColumn { Name = "DeleteBtn", HeaderText = "删除", Text = "删除", UseColumnTextForButtonValue = true, FillWeight = 7 });
            _dgvPlaceholders.CellClick += DgvPlaceholders_CellClick;
            _dgvPlaceholders.CellValueChanged += DgvPlaceholders_CellValueChanged;
            _dgvPlaceholders.CellFormatting += DgvPlaceholders_CellFormatting;
            // Delete键删除选中行:与"删除"按钮同一逻辑(连带移除文字中的 {{KEY}},支持多选)
            _dgvPlaceholders.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    e.Handled = true;
                    var sel = new List<DataGridViewRow>();
                    foreach (DataGridViewRow r in _dgvPlaceholders.SelectedRows)
                        sel.Add(r);
                    DeletePlaceholderRows(sel);
                }
            };
            _dgvPlaceholders.ColumnHeadersHeight = 28;
            
            // === 数据范围变化提示条(默认隐藏):配置时范围与当前数据不一致时显示 ===
            _rangeWarningPanel = new Panel { Dock = DockStyle.Top, Height = 30, Visible = false, BackColor = Color.FromArgb(255, 244, 229) };
            _rangeWarningLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(180, 100, 0),
                Padding = new Padding(6, 0, 0, 0)
            };
            var btnClampAll = new Button
            {
                Text = "全部裁剪", Dock = DockStyle.Right, Width = 68,
                FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            btnClampAll.Click += (s, e) => ClampAllRangesToData();
            var btnKeep = new Button
            {
                Text = "保留原样", Dock = DockStyle.Right, Width = 68,
                FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            btnKeep.Click += (s, e) => _rangeWarningPanel.Visible = false;
            _rangeWarningPanel.Controls.Add(_rangeWarningLabel);  // Fill 先添加
            _rangeWarningPanel.Controls.Add(btnClampAll);          // Right 居中
            _rangeWarningPanel.Controls.Add(btnKeep);              // Right 最右

            // 右栏添加顺序(后添加的先布局,Fill最后填充剩余空间):
            // DGV(Fill) → 范围提示条(Top,标题行之下) → 标题行(Top,最上方)
            splitMain.Panel2.Controls.Add(_dgvPlaceholders);
            splitMain.Panel2.Controls.Add(_rangeWarningPanel);
            splitMain.Panel2.Controls.Add(panelConfigHeader);

            // === 底部:保存/取消按钮 ===
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            _btnSave = new Button { Text = "保存", Size = new Size(btnW, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnSave.Click += BtnSave_Click;
            _btnCancel = new Button { Text = "取消", Size = new Size(btnW, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnCancel.Click += (s, ev) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            panelBottom.Controls.Add(_btnSave);
            panelBottom.Controls.Add(_btnCancel);

            // 添加顺序:Dock=Fill 先,然后 Bottom,最后 Top(后添加的先布局,保证 Fill 填剩余空间)
            Controls.Add(splitMain);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);

            // 分隔位置+按钮位置在Load时设(此时控件已布局,避免Width/Height为0时设值异常)
            Load += (s, e) =>
            {
                if (splitMain.Width > 200) splitMain.SplitterDistance = (int)(splitMain.Width * 0.6);  // 占位符框占40%
                if (splitLeft.Height > 40) splitLeft.SplitterDistance = splitLeft.Height / 2;
                // 按钮位置:基于panelBottom实际宽度,靠右排列
                int pw = panelBottom.ClientSize.Width;
                _btnCancel.Left = pw - btnW - 10;
                _btnCancel.Top = 7;
                _btnSave.Left = pw - btnW * 2 - 20;
                _btnSave.Top = 7;
            };

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;
            KeyPreview = true;  // Ctrl+S 快捷键优先于子控件处理
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.S)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    BtnSave_Click(s, e);
                }
            };
            Activated += (s, e) => CheckDataRangeMismatch();  // 编辑器非模态,数据可能后加载,激活时复验
        }

        /// <summary>不变文化数字格式化(最多3位小数),时间范围写入/显示统一用</summary>
        private static string FNum(double v)
        {
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>校验"配置时数据范围"与当前数据范围是否一致,不一致时显示提示条</summary>
        private void CheckDataRangeMismatch()
        {
            if (_rangeWarningPanel == null) return;
            _rangeWarningPanel.Visible = false;
            if (_editingType == null || string.IsNullOrEmpty(_editingType.DataRangeAtSave)) return;
            if (_dgvPlaceholders.Rows.Count == 0) return;  // 没有占位符行,无可裁剪对象,不报警
            if (!SignalStatsCalculator.TryParseTimeRange(_editingType.DataRangeAtSave, out double s0, out double s1)) return;

            var (g0, g1) = GetGlobalTimeRange();
            if (Math.Abs(s1 - g1) > 1.0 || Math.Abs(s0 - g0) > 1.0)
            {
                _rangeWarningLabel.Text = $"当前数据范围({FNum(g0)},{FNum(g1)})与配置时({FNum(s0)},{FNum(s1)})不一致:";
                _rangeWarningPanel.Visible = true;
            }
        }

        /// <summary>提示条"全部裁剪":把所有行的时间范围钳到当前数据范围;完全错开的行清空(跟随默认范围)</summary>
        private void ClampAllRangesToData()
        {
            var (g0, g1) = GetGlobalTimeRange();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string text = row.Cells["TimeRange"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!SignalStatsCalculator.TryParseTimeRange(text, out double t0, out double t1)) continue;  // 格式错误的留给用户改

                var state = SignalStatsCalculator.ClampTimeRange(t0, t1, g0, g1, out double c0, out double c1);
                if (state == SignalStatsCalculator.TimeRangeClampState.NoIntersection)
                    row.Cells["TimeRange"].Value = "";  // 完全错开→清空,回到跟随默认范围
                else if (state == SignalStatsCalculator.TimeRangeClampState.Clamped)
                    row.Cells["TimeRange"].Value = FNum(c0) + "," + FNum(c1);
            }
            _rangeWarningPanel.Visible = false;
            UpdatePreview();
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        /// <summary>点击信号列/复制列/删除列:SignalBtn弹SignalSelector(单选+多通道),CopyBtn复制占位符名,DeleteBtn删除占位符</summary>
        private void DgvPlaceholders_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string colName = _dgvPlaceholders.Columns[e.ColumnIndex].Name;

            // 复制按钮:把该行占位符名复制到剪贴板(粘贴时自动加括号)
            if (colName == "CopyBtn")
            {
                string key = _dgvPlaceholders.Rows[e.RowIndex].Cells["Key"].Value?.ToString();
                if (!string.IsNullOrEmpty(key))
                    Clipboard.SetText(key);
                return;
            }

            // 删除按钮:与Delete键同一逻辑
            if (colName == "DeleteBtn")
            {
                DeletePlaceholderRows(new List<DataGridViewRow> { _dgvPlaceholders.Rows[e.RowIndex] });
                return;
            }

            if (colName != "SignalBtn") return;

            var row = _dgvPlaceholders.Rows[e.RowIndex];
            using (var selector = new SignalSelector(_busChannels) { SingleSelect = true })
            {
                // 如果当前行已有信号，预选中
                string currentSignalName = row.Cells["SignalName"].Value?.ToString();
                if (!string.IsNullOrEmpty(currentSignalName))
                {
                    foreach (var ch in _channels)
                    {
                        string displayName = !string.IsNullOrEmpty(ch.DbcSignalName) ? ch.DbcSignalName : ch.Name;
                        if (displayName == currentSignalName)
                        {
                            selector.SelectedSignals.Add(new SelectedSignalInfo
                            {
                                SignalName = ch.DbcSignalName,
                                MsgId = (uint)ch.DbcMessageId,
                                MsgIndex = ch.DbcMessageIndex,
                                SignalIndex = ch.DbcSignalIndex,
                                CycleTime = (uint)(ch.CycleTime * 1000),
                                EnumDefinitions = ch.EnumDefinitions,
                                Unit = ch.Unit
                            });
                            break;
                        }
                    }
                }

                if (selector.ShowDialog(this) == DialogResult.OK && selector.SelectedSignals.Count > 0)
                {
                    var sig = selector.SelectedSignals[0]; // 单选模式只取第一个
                    row.Cells["SignalName"].Value = sig.SignalName;
                    // 单位存行Tag(不单独显示列)
                    row.Tag = sig.Unit ?? "";
                    // 自动生成占位符名（如果当前为空）
                    if (string.IsNullOrEmpty(row.Cells["Key"].Value?.ToString()))
                    {
                        string autoKey = GeneratePlaceholderName(sig.SignalName, row.Cells["Calc"].Value?.ToString());
                        row.Cells["Key"].Value = autoKey;
                    }
                    UpdatePreview();
                }
            }
        }

        /// <summary>删除指定占位符行("删除"按钮/Delete键共用,支持多选):确认后连带移除文字中的 {{KEY}},同步链路自动删行</summary>
        private void DeletePlaceholderRows(List<DataGridViewRow> rows)
        {
            if (rows == null || rows.Count == 0) return;

            var keys = new List<string>();
            foreach (var r in rows)
            {
                string k = r.Cells["Key"].Value?.ToString();
                if (!string.IsNullOrEmpty(k)) keys.Add(k);
            }
            if (keys.Count == 0)
            {
                foreach (var r in rows) _dgvPlaceholders.Rows.Remove(r);
                return;
            }

            string msg = keys.Count == 1
                ? $"确定删除占位符 {{{{{keys[0]}}}}} 吗？\n文字内容和配置表中的该占位符都会被移除。"
                : $"确定删除选中的 {keys.Count} 个占位符吗？\n文字内容和配置表中的这些占位符都会被移除。";
            if (MessageBox.Show(msg, "删除占位符", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // 主动删除:清除暂存绑定,避免再次输入同名占位符时旧绑定被恢复
            foreach (var k in keys) _detachedBindings.Remove(k);
            // 从文字模板中移除 {{KEY}},触发 RtbText_TextChanged → SyncPlaceholdersFromText 自动删行+刷新预览
            string text = _rtbText.Text;
            foreach (var k in keys)
                text = Regex.Replace(text, @"\{\{" + Regex.Escape(k) + @"\}\}", "");
            _rtbText.Text = text;
            // 兜底:文字中不存在的残留行,直接删行
            foreach (var r in rows)
                if (r.DataGridView != null) _dgvPlaceholders.Rows.Remove(r);
            UpdatePreview();
        }

        /// <summary>根据信号名和计算方式自动生成占位符名</summary>
        internal static string GeneratePlaceholderName(string signalName, string calcDisplay)
        {
            if (string.IsNullOrEmpty(signalName)) return "PLACEHOLDER";
            string prefix = "";
            if (!string.IsNullOrEmpty(calcDisplay))
            {
                if (calcDisplay.Contains("avg")) prefix = "AVG_";
                else if (calcDisplay.Contains("max")) prefix = "MAX_";
                else if (calcDisplay.Contains("min")) prefix = "MIN_";
                else if (calcDisplay.Contains("range")) prefix = "RANGE_";
            }
            // 清理信号名：去掉非字母数字字符，转大写
            string clean = new string(signalName.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
            if (clean.Length > 20) clean = clean.Substring(0, 20);
            return prefix + clean;
        }

        /// <summary>从编辑中的AnalysisType加载数据到UI</summary>
        private void LoadFromType()
        {
            if (_editingType == null) return;

            _txtName.Text = _editingType.Name ?? "";
            _rtbText.Text = _editingType.TextTemplate ?? "";

            // 占位符配置:只加载TextTemplate中实际存在的占位符
            // 如果TextTemplate不包含任何{{...}}占位符,完全跳过Signals加载(避免DLP旧数据残留)
            bool hasAnyPlaceholder = Regex.IsMatch(_rtbText.Text ?? "", @"\{\{\w+\}\}");
            if (_editingType.Signals != null && hasAnyPlaceholder)
            {
                foreach (var stat in _editingType.Signals)
                {
                    if (stat.PlaceholderMap == null || stat.Metrics == null) continue;
                    foreach (var kv in stat.PlaceholderMap)
                    {
                        string placeholderKey = kv.Value;
                        string metric = kv.Key;
                        // 只加载TextTemplate中实际包含的占位符
                        if (!_rtbText.Text.Contains("{{" + placeholderKey + "}}"))
                            continue;
                        // 找到对应的行，避免重复添加
                        bool exists = false;
                        foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
                        {
                            if (row.Cells["Key"].Value?.ToString() == placeholderKey)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (exists) continue;

                        string signalName = stat.SignalName ?? "";
                        string calcDisplay = MetricToDisplay(metric);
                        // 从TimeRangeMap加载该占位符的独立时间范围
                        string timeRange = "";
                        if (stat.TimeRangeMap != null && stat.TimeRangeMap.TryGetValue(placeholderKey, out var tr))
                            timeRange = tr ?? "";
                        int rowIdx = _dgvPlaceholders.Rows.Add(placeholderKey, "选择信号", signalName, calcDisplay, timeRange, "");
                        _dgvPlaceholders.Rows[rowIdx].Tag = stat.Unit ?? "";  // 单位存行Tag
                    }
                }
            }
            UpdatePreview();
            HighlightPlaceholders();  // 初始加载后刷新占位符高亮
        }

        private static string MetricToDisplay(string metric)
        {
            if (string.IsNullOrEmpty(metric)) return "";
            switch (metric.ToLowerInvariant())
            {
                case "avg": return "平均值(avg)";
                case "max": return "最大值(max)";
                case "min": return "最小值(min)";
                case "range": return "极差(range)";
                default: return metric;
            }
        }

        private static string DisplayToMetric(string display)
        {
            if (string.IsNullOrEmpty(display)) return "";
            if (display.Contains("avg")) return "avg";
            if (display.Contains("max")) return "max";
            if (display.Contains("min")) return "min";
            if (display.Contains("range")) return "range";
            return display;
        }

        /// <summary>文字变化时自动扫描 {{...}} 占位符，同步到DataGridView</summary>
        private void RtbText_TextChanged(object sender, EventArgs e)
        {
            if (_suppressTextSync) return;  // 程序化插入时不同步,避免清空已有绑定
            SyncPlaceholdersFromText();
            UpdatePreview();
            HighlightPlaceholders();
        }

        /// <summary>文字区占位符 {{KEY}} 语法高亮(蓝色加粗,其余恢复默认格式)</summary>
        private void HighlightPlaceholders()
        {
            if (_rtbText == null) return;
            _highlighting = true;
            try
            {
                int selStart = _rtbText.SelectionStart;
                int selLen = _rtbText.SelectionLength;
                // 先全部恢复默认格式,再标占位符
                _rtbText.SelectAll();
                _rtbText.SelectionColor = _rtbText.ForeColor;
                _rtbText.SelectionFont = _rtbText.Font;
                var boldFont = new Font(_rtbText.Font, FontStyle.Bold);
                var placeholderColor = Color.FromArgb(0, 102, 204);
                foreach (Match m in Regex.Matches(_rtbText.Text, @"\{\{\w+\}\}"))
                {
                    _rtbText.Select(m.Index, m.Length);
                    _rtbText.SelectionColor = placeholderColor;
                    _rtbText.SelectionFont = boldFont;
                }
                // 恢复光标,后续输入用默认格式
                _rtbText.SelectionStart = selStart;
                _rtbText.SelectionLength = selLen;
                _rtbText.SelectionColor = _rtbText.ForeColor;
                _rtbText.SelectionFont = _rtbText.Font;
            }
            finally { _highlighting = false; }
        }

        /// <summary>光标进入 {{KEY}} 时,自动选中右侧配置表对应行并滚动到可见</summary>
        private void RtbText_SelectionChanged(object sender, EventArgs e)
        {
            if (_suppressTextSync || _highlighting || _rtbText.SelectionLength != 0) return;
            string text = _rtbText.Text;
            if (string.IsNullOrEmpty(text)) return;
            int pos = _rtbText.SelectionStart;
            foreach (Match m in Regex.Matches(text, @"\{\{(\w+)\}\}"))
            {
                if (pos >= m.Index && pos <= m.Index + m.Length)
                {
                    var row = FindRowByKey(m.Groups[1].Value);
                    if (row != null)
                    {
                        _dgvPlaceholders.ClearSelection();
                        row.Selected = true;
                        try { _dgvPlaceholders.FirstDisplayedScrollingRowIndex = row.Index; } catch { }
                    }
                    break;
                }
            }
        }

        /// <summary>Ctrl+V粘贴:若剪贴板内容是已配置的占位符名,自动包裹{{}};
        /// Delete/Backspace:光标在占位符内(或紧贴边缘)时,一键删除整个 {{KEY}}</summary>
        private void RtbText_KeyDown(object sender, KeyEventArgs e)
        {
            // Delete/Backspace 一键删除整个占位符(无需完全选中)
            if ((e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) && _rtbText.SelectionLength == 0)
            {
                int pos = _rtbText.SelectionStart;
                foreach (Match m in Regex.Matches(_rtbText.Text, @"\{\{\w+\}\}"))
                {
                    // Delete:光标在占位符内或紧贴开头; Backspace:在占位符内或紧贴结尾
                    bool hit = e.KeyCode == Keys.Delete
                        ? (pos >= m.Index && pos < m.Index + m.Length)
                        : (pos > m.Index && pos <= m.Index + m.Length);
                    if (hit)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        // 删除整个占位符,TextChanged同步会自动移除配置表中对应行
                        _rtbText.Text = _rtbText.Text.Remove(m.Index, m.Length);
                        _rtbText.SelectionStart = m.Index;
                        return;
                    }
                }
                return;  // 不在占位符上,走默认逐字删除
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                string clip = "";
                try { clip = Clipboard.GetText(); } catch { }
                clip = clip?.Trim() ?? "";
                if (string.IsNullOrEmpty(clip)) return;
                // 去掉可能带的括号,取纯KEY
                string key = clip.Replace("{{", "").Replace("}}", "").Trim();
                if (FindRowByKey(key) != null)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    int sel = _rtbText.SelectionStart;
                    string ins = "{{" + key + "}}";
                    _rtbText.Text = _rtbText.Text.Insert(sel, ins);
                    _rtbText.SelectionStart = sel + ins.Length;
                    _rtbText.Focus();
                }
            }
        }

        private void SyncPlaceholdersFromText()
        {
            string text = _rtbText.Text;
            var matches = Regex.Matches(text, @"\{\{(\w+)\}\}");
            var keysInText = new HashSet<string>();
            foreach (Match m in matches)
                keysInText.Add(m.Groups[1].Value);

            // 添加文本中有但表格中没有的
            var existingKeys = new HashSet<string>();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string key = row.Cells["Key"].Value?.ToString();
                if (key != null) existingKeys.Add(key);
            }

            foreach (string key in keysInText)
            {
                if (!existingKeys.Contains(key))
                {
                    // 剪切-粘贴场景:暂存的绑定存在则恢复,否则按新占位符添加
                    if (_detachedBindings.TryGetValue(key, out var b))
                    {
                        int idx = _dgvPlaceholders.Rows.Add(key, "选择信号", b.SignalName, b.Calc, b.TimeRange, "");
                        _dgvPlaceholders.Rows[idx].Tag = b.Unit ?? "";
                    }
                    else
                    {
                        int idx = _dgvPlaceholders.Rows.Add(key, "选择信号", "", "平均值(avg)", "", "");
                        _dgvPlaceholders.Rows[idx].Tag = "";  // 单位默认空,选信号后自动填
                    }
                }
            }

            // 删除文本中已没有的:移除前暂存绑定,供剪切-粘贴后恢复
            var rowsToRemove = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string key = row.Cells["Key"].Value?.ToString();
                if (key != null && !keysInText.Contains(key))
                    rowsToRemove.Add(row);
            }
            foreach (var row in rowsToRemove)
            {
                string key = row.Cells["Key"].Value?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    _detachedBindings[key] = new DetachedBinding
                    {
                        SignalName = row.Cells["SignalName"].Value?.ToString() ?? "",
                        Calc = row.Cells["Calc"].Value?.ToString() ?? "",
                        TimeRange = row.Cells["TimeRange"].Value?.ToString() ?? "",
                        Unit = row.Tag?.ToString() ?? ""
                    };
                }
                _dgvPlaceholders.Rows.Remove(row);
            }
        }

        /// <summary>点击"插入信号占位符"按钮:弹对话框选信号+勾指标→插入文字+建立绑定</summary>
        private void BtnInsertPlaceholder_Click(object sender, EventArgs e)
        {
            using (var dlg = new InsertSignalPlaceholderDialog(_busChannels))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null || dlg.Result.Count == 0)
                    return;
                // 抑制TextChanged同步,避免SyncPlaceholdersFromText清空已有绑定
                _suppressTextSync = true;
                try
                {
                    // 1) 把勾选的 {{占位符}} 插入到 RichTextBox 光标处
                    string insertText = string.Join(" ", dlg.Result.Select(r => "{{" + r.Key + "}}"));
                    int selStart = _rtbText.SelectionStart;
                    _rtbText.Text = _rtbText.Text.Insert(selStart, insertText);
                    _rtbText.SelectionStart = selStart + insertText.Length;
                    _rtbText.Focus();
                }
                finally
                {
                    _suppressTextSync = false;
                }
                // 2) 按 Key 把信号名/计算方式/单位填进对应行
                ApplyBindings(dlg.Result);
                UpdatePreview();
            }
        }

        /// <summary>把对话框返回的绑定列表填入占位符配置表(自动处理重复Key)</summary>
        private void ApplyBindings(List<InsertedBinding> bindings)
        {
            foreach (var b in bindings)
            {
                // 自动处理重复Key:如已存在则加后缀
                string key = b.Key;
                int suffix = 2;
                while (FindRowByKey(key) != null)
                {
                    key = b.Key + "_" + suffix;
                    suffix++;
                }

                DataGridViewRow row = FindRowByKey(key);
                if (row == null)
                {
                    int idx = _dgvPlaceholders.Rows.Add(key, "选择信号", b.SignalName, MetricToDisplay(b.Metric), "", "");
                    row = _dgvPlaceholders.Rows[idx];
                }
                else
                {
                    row.Cells["SignalName"].Value = b.SignalName;
                    row.Cells["Calc"].Value = MetricToDisplay(b.Metric);
                }
                row.Tag = b.Unit;  // 单位存行Tag(不单独显示列)
            }
        }

        /// <summary>按占位符Key在配置表中查找行,找不到返回null</summary>
        private DataGridViewRow FindRowByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                if (row.Cells["Key"].Value?.ToString() == key)
                    return row;
            }
            return null;
        }

        /// <summary>实时预览:{{KEY}}→已计算显示结果值,未计算显示[信号名·计算方式],未绑定显示原文</summary>
        private void UpdatePreview()
        {
            if (_txtPreview == null || _rtbText == null) return;
            string text = _rtbText.Text;
            if (string.IsNullOrEmpty(text))
            {
                _txtPreview.Text = "";
                return;
            }
            _txtPreview.Text = Regex.Replace(text, @"\{\{(\w+)\}\}", m =>
            {
                DataGridViewRow row = FindRowByKey(m.Groups[1].Value);
                if (row == null) return m.Value;  // 占位符未入表,保留原文
                // 一键计算后结果列有值,直接填入计算结果(含[未找到信号]/[无数据]等异常提示)
                string result = row.Cells["Result"].Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(result)) return result;
                string sig = row.Cells["SignalName"].Value?.ToString() ?? "";
                string calc = row.Cells["Calc"].Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(sig)) return m.Value;  // 未绑信号,保留原文
                return "[" + sig + "·" + calc + "]";
            });
            _txtPreview.Refresh();  // 强制重绘,避免Dock布局时序导致不渲染
        }

        /// <summary>获取所有通道数据的全局时间范围</summary>
        private (double start, double end) GetGlobalTimeRange()
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (var ch in _channels)
            {
                var range = ch.GetXRange();
                if (range.Min < min) min = range.Min;
                if (range.Max > max) max = range.Max;
            }
            if (min >= max) return (0, 10);
            return (min, max);
        }

        /// <summary>配置变更(SignalName/Calc/TimeRange)时清空该行旧计算结果,避免残留误导;并刷新预览</summary>
        private void DgvPlaceholders_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                string col = _dgvPlaceholders.Columns[e.ColumnIndex].Name;
                if (col == "SignalName" || col == "Calc" || col == "TimeRange")
                    _dgvPlaceholders.Rows[e.RowIndex].Cells["Result"].Value = "";  // 配置变了,旧结果作废
            }
            UpdatePreview();
        }

        /// <summary>时间范围列:留空时灰色显示默认时间范围(仅显示不写入,保留"留空=自动使用默认范围"的语义);格式错误标红提示</summary>
        private void DgvPlaceholders_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_dgvPlaceholders.Columns[e.ColumnIndex].Name != "TimeRange") return;

            string raw = e.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (!SignalStatsCalculator.TryParseTimeRange(raw, out double p0, out double p1))
                {
                    e.CellStyle.ForeColor = Color.Red;  // 格式非法(兼容中文逗号/分号后仍解析失败)
                    return;
                }
                // 超出当前数据范围:橙色警示(计算时按交集裁剪,完全错开则[时间窗无数据])
                var (g0, g1) = GetGlobalTimeRange();
                if (SignalStatsCalculator.ClampTimeRange(p0, p1, g0, g1, out _, out _)
                    != SignalStatsCalculator.TimeRangeClampState.Contained)
                    e.CellStyle.ForeColor = Color.DarkOrange;
                return;
            }

            var (t0, t1) = GetGlobalTimeRange();
            e.Value = t0.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + t1.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            e.CellStyle.ForeColor = Color.Gray;  // 灰色表示这是默认值而非手动输入
            e.FormattingApplied = true;
        }

        /// <summary>点击一键计算:先让宿主确保信号通道(可能后台补采),完成后回调CalculateNow执行计算</summary>
        private void BtnCalculate_Click(object sender, EventArgs e)
        {
            if (EnsureSignalsRequested != null)
            {
                // 宿主补建缺失通道并补采,完成后回调CalculateNow
                EnsureSignalsRequested(this, EventArgs.Empty);
                return;
            }
            CalculateNow();
        }

        /// <summary>对所有占位符行的信号进行计算,结果显示在结果预览列(一键计算或宿主补采完成回调触发)</summary>
        public void CalculateNow()
        {
            var (globalT0, globalT1) = GetGlobalTimeRange();
            int calculatedCount = 0;

            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string signalName = row.Cells["SignalName"].Value?.ToString();
                string calcDisplay = row.Cells["Calc"].Value?.ToString();
                var resultCell = row.Cells["Result"];
                // 每轮重置标记色/提示(上轮裁剪标记可能已过期)
                resultCell.Style.ForeColor = SystemColors.ControlText;
                resultCell.ToolTipText = "";
                if (string.IsNullOrEmpty(signalName))
                {
                    resultCell.Value = "";
                    continue;
                }

                // 查找通道(精确匹配优先,再大小写不敏感兜底,与报告生成服务一致)
                var ch = _channels.FirstOrDefault(c => c.DbcSignalName == signalName)
                      ?? _channels.FirstOrDefault(c => string.Equals(c.DbcSignalName, signalName, StringComparison.OrdinalIgnoreCase));
                if (ch == null)
                {
                    resultCell.Value = "[未找到信号]";
                    continue;
                }

                // 解析时间范围(兼容中文逗号/分号;非法格式静默回退全局范围,单元格已标红提示)
                string timeRange = row.Cells["TimeRange"].Value?.ToString() ?? "";
                double t0 = globalT0, t1 = globalT1;
                if (SignalStatsCalculator.TryParseTimeRange(timeRange, out double parsedT0, out double parsedT1))
                {
                    t0 = parsedT0;
                    t1 = parsedT1;
                    // 钳制到该通道数据范围:部分重叠按交集算并标记,完全错开报[时间窗无数据]
                    var xr = ch.GetXRange();
                    var clampState = SignalStatsCalculator.ClampTimeRange(t0, t1, xr.Min, xr.Max, out double cT0, out double cT1);
                    if (clampState == SignalStatsCalculator.TimeRangeClampState.NoIntersection)
                    {
                        resultCell.Value = "[时间窗无数据]";
                        continue;
                    }
                    if (clampState == SignalStatsCalculator.TimeRangeClampState.Clamped)
                    {
                        t0 = cT0;
                        t1 = cT1;
                        resultCell.Style.ForeColor = Color.DarkOrange;
                        resultCell.ToolTipText = $"已按当前数据裁剪为 {FNum(t0)},{FNum(t1)} 计算";
                    }
                }

                string metric = DisplayToMetric(calcDisplay);
                string key = row.Cells["Key"].Value?.ToString() ?? "";

                var tempStat = new SignalStat
                {
                    MessageId = ch.DbcMessageId,
                    SignalName = signalName,
                    Unit = ch.Unit ?? "",
                    Metrics = new List<string> { metric },
                    PlaceholderMap = new Dictionary<string, string> { { metric, key } }
                };

                var pv = SignalStatsCalculator.Calc(ch, t0, t1, tempStat);
                if (pv != null && pv.Count > 0)
                {
                    resultCell.Value = pv.Values.First();
                    calculatedCount++;
                }
                else
                {
                    resultCell.Value = "[无数据]";
                }
            }

            // 标题栏反馈计算结果,不打断操作(原弹窗需手动点掉)
            Text = $"工况分类编辑器 — 已计算 {calculatedCount} 个占位符 " + DateTime.Now.ToString("HH:mm:ss");
        }

        /// <summary>点击保存：构建AnalysisType并触发保存事件,不关闭编辑器(可继续编辑后再次保存)</summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入工况分类名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            var type = BuildAnalysisType();

            Result = type;
            _lastSavedName = type.Name;  // 跟踪保存名(改名后编辑器身份跟随新名,防止单例判定失效)

            // 触发保存事件，通知主窗口刷新下拉
            Saved?.Invoke(this, type);

            // 不关闭窗口,标题栏显示保存时间作为反馈
            Text = "工况分类编辑器 — 已保存 " + DateTime.Now.ToString("HH:mm:ss");
        }

        /// <summary>按当前UI内容构建AnalysisType(不做名称校验;供确定保存和宿主预建信号通道使用)</summary>
        public AnalysisType BuildAnalysisType()
        {
            var type = new AnalysisType();
            type.Name = _txtName.Text.Trim();
            type.TextTemplate = _rtbText.Text;
            // 记录保存时的数据范围(供下次打开编辑器校验数据是否变化)
            var (g0, g1) = GetGlobalTimeRange();
            type.DataRangeAtSave = FNum(g0) + "," + FNum(g1);
            // 文本框/图片框定位固定为模板内标识符,编辑器不再暴露选择
            type.TextShapeName = TemplateShapeIds.TextDesc;
            type.ImageShapes = new List<ImageShapeItem>
            {
                new ImageShapeItem { ShapeName = TemplateShapeIds.ChartImage, Source = "chart" }
            };

            // 占位符 → Signals
            type.Signals = new List<SignalStat>();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string key = row.Cells["Key"].Value?.ToString();
                string signalName = row.Cells["SignalName"].Value?.ToString();
                string calcDisplay = row.Cells["Calc"].Value?.ToString();
                string unit = row.Tag?.ToString() ?? "";
                string timeRange = row.Cells["TimeRange"].Value?.ToString() ?? "";

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(signalName)) continue;

                string metric = DisplayToMetric(calcDisplay);

                // 每行创建独立的SignalStat（允许相同信号+指标重复添加）
                int msgId = 0;
                var ch = _channels.FirstOrDefault(c => c.DbcSignalName == signalName);
                if (ch != null) msgId = ch.DbcMessageId;

                // 构建TimeRangeMap
                Dictionary<string, string> timeRangeMap = null;
                if (!string.IsNullOrWhiteSpace(timeRange))
                {
                    timeRangeMap = new Dictionary<string, string> { { key, timeRange } };
                }

                type.Signals.Add(new SignalStat
                {
                    MessageId = msgId,
                    SignalName = signalName,
                    Unit = unit,
                    Metrics = new List<string> { metric },
                    PlaceholderMap = new Dictionary<string, string> { { metric, key } },
                    TimeRangeMap = timeRangeMap
                });
            }

            // 保存信号列表（快照当前绘图区通道;占位符报告专用通道不入快照,由主窗口按需重建）
            type.SignalList = new List<SignalPresetItem>();
            foreach (var ch in _channels)
            {
                if (ch.IsReportOnly) continue;
                type.SignalList.Add(new SignalPresetItem
                {
                    SignalName = ch.DbcSignalName ?? "",
                    MessageId = ch.DbcMessageId,
                    MessageIndex = ch.DbcMessageIndex,
                    SignalIndex = ch.DbcSignalIndex,
                    Unit = ch.Unit ?? "",
                    Color = ColorTranslator.ToHtml(ch.Color),
                    Visible = ch.Visible,
                    BusChannelIndex = ch.BusChannelIndex
                });
            }

            return type;
        }
    }
}
