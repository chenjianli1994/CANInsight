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
        // 当前绘图区已有的信号通道（供下拉选择）
        private readonly List<ChannelData> _channels;
        // CAN总线通道列表(供信号选择器显示多路CAN通道DBC)
        private readonly List<CanBusChannel> _busChannels;

        // UI 控件
        private TextBox _txtName;
        private RichTextBox _rtbText;
        private TextBox _txtPreview;        // 实时预览:{{占位符}}→[信号·指标]
        private DataGridView _dgvPlaceholders;
        private Button _btnInsertPlaceholder;
        private Button _btnOk;
        private Button _btnCancel;

        // 结果
        public AnalysisType Result { get; private set; }

        /// <summary>
        /// 创建编辑器。editingType为null时新建，否则编辑现有类型。
        /// </summary>
        public AnalysisTypeEditor(AnalysisType editingType, List<ChannelData> channels, List<CanBusChannel> busChannels)
        {
            _editingType = editingType;
            _channels = channels ?? new List<ChannelData>();
            _busChannels = busChannels ?? new List<CanBusChannel>();
            InitUI();
            LoadFromType();
        }

        private void InitUI()
        {
            Text = "工况分类编辑器";
            Size = new Size(1000, 720);
            MinimumSize = new Size(700, 500);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
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
            panelText.Controls.Add(_rtbText);  // Fill 先添加（后布局）
            _btnInsertPlaceholder = new Button { Text = "插入信号占位符...", Dock = DockStyle.Top, Height = 26 };
            _btnInsertPlaceholder.Click += BtnInsertPlaceholder_Click;
            panelText.Controls.Add(_btnInsertPlaceholder);  // Top 中间
            panelText.Controls.Add(new Label { Text = "文字内容（可输入 {{占位符}}）:", Dock = DockStyle.Top, Height = 20 });  // Top 最上
            splitLeft.Panel1.Controls.Add(panelText);
            
            // 预览区
            splitLeft.Panel2.Controls.Add(new Label { Text = "预览（{{占位符}}→[信号·指标]）:", Dock = DockStyle.Top, Height = 20 });
            _txtPreview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = SystemColors.Control, Font = new Font("Microsoft YaHei UI", 9F) };
            splitLeft.Panel2.Controls.Add(_txtPreview);
            splitMain.Panel1.Controls.Add(splitLeft);

            // 右栏:占位符配置表(宽度随分隔条可调)
            splitMain.Panel2.Controls.Add(new Label { Text = "占位符计算配置:", Dock = DockStyle.Top, Height = 20 });
            _dgvPlaceholders = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            _dgvPlaceholders.DataError += (s, e) => { e.Cancel = true; };
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "占位符", FillWeight = 28 });
            _dgvPlaceholders.Columns.Add(new DataGridViewButtonColumn { Name = "SignalBtn", HeaderText = "信号", Text = "选择信号", UseColumnTextForButtonValue = true, FillWeight = 12 });
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn { Name = "SignalName", HeaderText = "信号名", ReadOnly = true, FillWeight = 42 });
            _dgvPlaceholders.Columns.Add(new DataGridViewComboBoxColumn { Name = "Calc", HeaderText = "计算方式", FillWeight = 15, FlatStyle = FlatStyle.Flat, Items = { "平均值(avg)", "最大值(max)", "最小值(min)", "极差(range)" } });
            _dgvPlaceholders.Columns.Add(new DataGridViewButtonColumn { Name = "CopyBtn", HeaderText = "复制", Text = "复制", UseColumnTextForButtonValue = true, FillWeight = 10 });
            _dgvPlaceholders.CellClick += DgvPlaceholders_CellClick;
            _dgvPlaceholders.CellValueChanged += (s, e) => UpdatePreview();
            splitMain.Panel2.Controls.Add(_dgvPlaceholders);

            // === 底部:确定/取消按钮 ===
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            _btnOk = new Button { Text = "确定", Size = new Size(btnW, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnOk.Click += BtnOk_Click;
            _btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(btnW, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            panelBottom.Controls.Add(_btnOk);
            panelBottom.Controls.Add(_btnCancel);

            // 添加顺序:Dock=Fill 先,然后 Bottom,最后 Top(后添加的先布局,保证 Fill 填剩余空间)
            Controls.Add(splitMain);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);

            // 分隔位置+按钮位置在Load时设(此时控件已布局,避免Width/Height为0时设值异常)
            Load += (s, e) =>
            {
                if (splitMain.Width > 200) splitMain.SplitterDistance = (int)(splitMain.Width * 0.7);  // 占位符框占30%
                if (splitLeft.Height > 40) splitLeft.SplitterDistance = splitLeft.Height / 2;
                // 按钮位置:基于panelBottom实际宽度,靠右排列
                int pw = panelBottom.ClientSize.Width;
                _btnCancel.Left = pw - btnW - 10;
                _btnCancel.Top = 7;
                _btnOk.Left = pw - btnW * 2 - 20;
                _btnOk.Top = 7;
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        /// <summary>点击信号列/复制列:SignalBtn弹SignalSelector(单选+多通道),CopyBtn复制占位符名</summary>
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
                        int rowIdx = _dgvPlaceholders.Rows.Add(placeholderKey, "选择信号", signalName, calcDisplay);
                        _dgvPlaceholders.Rows[rowIdx].Tag = stat.Unit ?? "";  // 单位存行Tag
                    }
                }
            }
            UpdatePreview();
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
            SyncPlaceholdersFromText();
            UpdatePreview();
        }

        /// <summary>Ctrl+V粘贴:若剪贴板内容是已配置的占位符名,自动包裹{{}}</summary>
        private void RtbText_KeyDown(object sender, KeyEventArgs e)
        {
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
                    int idx = _dgvPlaceholders.Rows.Add(key, "选择信号", "", "平均值(avg)");
                    _dgvPlaceholders.Rows[idx].Tag = "";  // 单位默认空,选信号后自动填
                }
            }

            // 删除文本中已没有的（标记灰色行或直接删除）
            var rowsToRemove = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string key = row.Cells["Key"].Value?.ToString();
                if (key != null && !keysInText.Contains(key))
                    rowsToRemove.Add(row);
            }
            foreach (var row in rowsToRemove)
                _dgvPlaceholders.Rows.Remove(row);
        }

        /// <summary>点击"插入信号占位符"按钮:弹对话框选信号+勾指标→插入文字+建立绑定</summary>
        private void BtnInsertPlaceholder_Click(object sender, EventArgs e)
        {
            using (var dlg = new InsertSignalPlaceholderDialog(_busChannels))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null || dlg.Result.Count == 0)
                    return;
                // 1) 把勾选的 {{占位符}} 插入到 RichTextBox 光标处
                string insertText = string.Join(" ", dlg.Result.Select(r => "{{" + r.Key + "}}"));
                int selStart = _rtbText.SelectionStart;
                _rtbText.Text = _rtbText.Text.Insert(selStart, insertText);
                _rtbText.SelectionStart = selStart + insertText.Length;
                _rtbText.Focus();
                // 2) TextChanged→SyncPlaceholdersFromText 已自动加空行,
                //    这里按 Key 把信号名/计算方式/单位填进对应行
                ApplyBindings(dlg.Result);
                UpdatePreview();
            }
        }

        /// <summary>把对话框返回的绑定列表填入占位符配置表(按Key找行,找不到则加)</summary>
        private void ApplyBindings(List<InsertedBinding> bindings)
        {
            foreach (var b in bindings)
            {
                DataGridViewRow row = FindRowByKey(b.Key);
                if (row == null)
                {
                    int idx = _dgvPlaceholders.Rows.Add(b.Key, "选择信号", b.SignalName, MetricToDisplay(b.Metric));
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

        /// <summary>实时预览:把文字里的 {{KEY}} 替换成 [信号名·计算方式],未绑定显示 [未绑定]</summary>
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
                string sig = row.Cells["SignalName"].Value?.ToString() ?? "";
                string calc = row.Cells["Calc"].Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(sig)) return m.Value;  // 未绑信号,保留原文
                return "[" + sig + "·" + calc + "]";
            });
            _txtPreview.Refresh();  // 强制重绘,避免Dock布局时序导致不渲染
        }

        /// <summary>点击确定：构建AnalysisType结果</summary>
        private void BtnOk_Click(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入工况分类名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            var type = new AnalysisType();
            type.Name = name;
            type.TextTemplate = _rtbText.Text;
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

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(signalName)) continue;

                string metric = DisplayToMetric(calcDisplay);

                // 查找已有同信号的SignalStat，合并PlaceholderMap
                var existing = type.Signals.FirstOrDefault(s => s.SignalName == signalName);
                if (existing != null)
                {
                    if (!existing.Metrics.Contains(metric))
                        existing.Metrics.Add(metric);
                    existing.PlaceholderMap[metric] = key;
                }
                else
                {
                    // 从channels中查找MessageId
                    int msgId = 0;
                    var ch = _channels.FirstOrDefault(c => c.DbcSignalName == signalName);
                    if (ch != null) msgId = ch.DbcMessageId;

                    type.Signals.Add(new SignalStat
                    {
                        MessageId = msgId,
                        SignalName = signalName,
                        Unit = unit,
                        Metrics = new List<string> { metric },
                        PlaceholderMap = new Dictionary<string, string> { { metric, key } }
                    });
                }
            }

            // 保存信号列表（快照当前绘图区通道）
            type.SignalList = new List<SignalPresetItem>();
            foreach (var ch in _channels)
            {
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

            Result = type;
            this.DialogResult = DialogResult.OK;
        }
    }
}
