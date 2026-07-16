// 工况分类编辑器:可视化编辑文字模板、占位符计算配置、PPT Shape选择
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;

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
        // PPT模板路径（用于列出Shape名称）
        private readonly string _templatePath;
        // PPT中所有Shape名称列表
        private List<string> _pptShapeNames = new List<string>();

        // UI 控件
        private TextBox _txtName;
        private ComboBox _cmbTextShape;
        private ComboBox _cmbImageShape;
        private RichTextBox _rtbText;
        private DataGridView _dgvPlaceholders;
        private Button _btnInsertPlaceholder;
        private Button _btnOk;
        private Button _btnCancel;

        // 结果
        public AnalysisType Result { get; private set; }

        /// <summary>
        /// 创建编辑器。editingType为null时新建，否则编辑现有类型。
        /// </summary>
        public AnalysisTypeEditor(AnalysisType editingType, List<ChannelData> channels, string templatePath)
        {
            _editingType = editingType;
            _channels = channels ?? new List<ChannelData>();
            _templatePath = templatePath;
            InitUI();
            LoadPptShapeNames();
            LoadFromType();
        }

        private void InitUI()
        {
            Text = "工况分类编辑器";
            Size = new Size(700, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 15;
            int labelW = 110;
            int inputW = 400;
            int btnW = 80;

            // 名称
            AddLabel("名称:", 15, y + 3);
            _txtName = new TextBox { Location = new Point(130, y), Width = inputW };
            Controls.Add(_txtName);
            y += 35;

            // PPT文本框Shape
            AddLabel("PPT文本框Shape:", 15, y + 3);
            _cmbTextShape = new ComboBox
            {
                Location = new Point(130, y),
                Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            Controls.Add(_cmbTextShape);
            y += 35;

            // PPT图片Shape
            AddLabel("PPT图片Shape:", 15, y + 3);
            _cmbImageShape = new ComboBox
            {
                Location = new Point(130, y),
                Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            Controls.Add(_cmbImageShape);
            y += 35;

            // 文字编辑区标签
            AddLabel("文字内容（可输入 {{占位符}}）:", 15, y + 3);
            y += 22;

            _rtbText = new RichTextBox
            {
                Location = new Point(15, y),
                Size = new Size(655, 150),
                Font = new Font("Microsoft YaHei UI", 10F),
                AcceptsTab = true
            };
            _rtbText.TextChanged += RtbText_TextChanged;
            Controls.Add(_rtbText);
            y += 155;

            // 插入占位符按钮
            _btnInsertPlaceholder = new Button
            {
                Text = "插入占位符...",
                Location = new Point(15, y),
                Size = new Size(120, 28)
            };
            _btnInsertPlaceholder.Click += BtnInsertPlaceholder_Click;
            Controls.Add(_btnInsertPlaceholder);
            y += 38;

            // 占位符配置标签
            AddLabel("占位符计算配置:", 15, y + 3);
            y += 22;

            // 占位符 DataGridView
            _dgvPlaceholders = new DataGridView
            {
                Location = new Point(15, y),
                Size = new Size(655, 160),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            _dgvPlaceholders.DataError += (s, e) => { e.Cancel = true; }; // 忽略ComboBox值无效错误

            // 占位符列（可编辑）
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Key",
                HeaderText = "占位符",
                FillWeight = 22
            });

            // 信号列（按钮，点击弹出SignalSelector）
            var signalBtnCol = new DataGridViewButtonColumn
            {
                Name = "SignalBtn",
                HeaderText = "信号",
                Text = "选择信号...",
                UseColumnTextForButtonValue = true,
                FillWeight = 30
            };
            _dgvPlaceholders.Columns.Add(signalBtnCol);

            // 信号名显示列（只读，显示选中的信号名）
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SignalName",
                HeaderText = "信号名",
                ReadOnly = true,
                FillWeight = 20
            });

            // 计算方式下拉列
            var calcCol = new DataGridViewComboBoxColumn
            {
                Name = "Calc",
                HeaderText = "计算方式",
                FillWeight = 18,
                FlatStyle = FlatStyle.Flat,
                Items = { "平均值(avg)", "最大值(max)", "最小值(min)", "极差(range)" }
            };
            _dgvPlaceholders.Columns.Add(calcCol);

            // 单位列
            _dgvPlaceholders.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Unit",
                HeaderText = "单位",
                FillWeight = 10
            });

            // 按钮点击事件：弹出SignalSelector
            _dgvPlaceholders.CellClick += DgvPlaceholders_CellClick;

            Controls.Add(_dgvPlaceholders);
            y += 170;

            // 确定/取消按钮
            _btnOk = new Button
            {
                Text = "确定",
                Location = new Point(460, y),
                Size = new Size(btnW, 30)
            };
            _btnOk.Click += BtnOk_Click;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(560, y),
                Size = new Size(btnW, 30)
            };
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        /// <summary>点击信号列按钮，弹出SignalSelector选择信号</summary>
        private void DgvPlaceholders_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_dgvPlaceholders.Columns[e.ColumnIndex].Name != "SignalBtn") return;

            var row = _dgvPlaceholders.Rows[e.RowIndex];
            using (var selector = new SignalSelector())
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
                    var sig = selector.SelectedSignals[0]; // 只取第一个
                    row.Cells["SignalName"].Value = sig.SignalName;
                    // 自动填充单位
                    if (string.IsNullOrEmpty(row.Cells["Unit"].Value?.ToString()))
                        row.Cells["Unit"].Value = sig.Unit ?? "";
                    // 自动生成占位符名（如果当前为空）
                    if (string.IsNullOrEmpty(row.Cells["Key"].Value?.ToString()))
                    {
                        string autoKey = GeneratePlaceholderName(sig.SignalName, row.Cells["Calc"].Value?.ToString());
                        row.Cells["Key"].Value = autoKey;
                    }
                }
            }
        }

        /// <summary>根据信号名和计算方式自动生成占位符名</summary>
        private static string GeneratePlaceholderName(string signalName, string calcDisplay)
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

        /// <summary>从PPT模板中读取所有Shape名称</summary>
        private void LoadPptShapeNames()
        {
            if (string.IsNullOrEmpty(_templatePath) || !File.Exists(_templatePath)) return;
            try
            {
                string readablePath = _templatePath;
                // DLP兼容：尝试直接打开
                try
                {
                    using (var doc = PresentationDocument.Open(_templatePath, false)) { }
                }
                catch
                {
                    // 提取明文副本
                    string tmp = Path.Combine(Path.GetTempPath(), "EditorTpl_" + Guid.NewGuid().ToString("N") + ".pptx");
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c type \"" + _templatePath + "\" > \"" + tmp + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null) p.WaitForExit(15000);
                    if (File.Exists(tmp) && new FileInfo(tmp).Length > 0)
                        readablePath = tmp;
                    else
                        return;
                }

                using (var doc = PresentationDocument.Open(readablePath, false))
                {
                    var presPart = doc.PresentationPart;
                    var slidePart = presPart?.SlideParts?.FirstOrDefault();
                    if (slidePart?.Slide == null) return;

                    // 文本Shape
                    foreach (var sp in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Shape>())
                    {
                        string name = sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (!string.IsNullOrEmpty(name) && !_pptShapeNames.Contains(name))
                            _pptShapeNames.Add(name);
                    }
                    // 图片Shape
                    foreach (var pic in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Picture>())
                    {
                        string name = pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (!string.IsNullOrEmpty(name) && !_pptShapeNames.Contains(name))
                            _pptShapeNames.Add(name);
                    }
                    // 表格Shape
                    foreach (var gf in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.GraphicFrame>())
                    {
                        string name = gf.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (!string.IsNullOrEmpty(name) && !_pptShapeNames.Contains(name))
                            _pptShapeNames.Add(name);
                    }
                }

                // 填充下拉
                _cmbTextShape.Items.AddRange(_pptShapeNames.ToArray());
                _cmbImageShape.Items.AddRange(_pptShapeNames.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnalysisTypeEditor] 加载PPT Shape失败: " + ex.Message);
            }
        }

        /// <summary>从编辑中的AnalysisType加载数据到UI</summary>
        private void LoadFromType()
        {
            if (_editingType == null) return;

            _txtName.Text = _editingType.Name ?? "";
            _cmbTextShape.Text = _editingType.TextShapeName ?? "";
            _rtbText.Text = _editingType.TextTemplate ?? "";

            // 图片Shape（取第一个Source="chart"的）
            if (_editingType.ImageShapes != null)
            {
                var chartImg = _editingType.ImageShapes.FirstOrDefault(i => i.Source == "chart");
                if (chartImg != null)
                    _cmbImageShape.Text = chartImg.ShapeName ?? "";
            }

            // 占位符配置
            if (_editingType.Signals != null)
            {
                foreach (var stat in _editingType.Signals)
                {
                    if (stat.PlaceholderMap == null || stat.Metrics == null) continue;
                    foreach (var kv in stat.PlaceholderMap)
                    {
                        string placeholderKey = kv.Value;
                        string metric = kv.Key;
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
                        int rowIdx = _dgvPlaceholders.Rows.Add(placeholderKey, "选择信号...", signalName, calcDisplay, stat.Unit ?? "");
                    }
                }
            }
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
                    _dgvPlaceholders.Rows.Add(key, "选择信号...", "", "平均值(avg)", "");
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

        /// <summary>点击"插入占位符"按钮</summary>
        private void BtnInsertPlaceholder_Click(object sender, EventArgs e)
        {
            using (var inputForm = new Form
            {
                Text = "输入占位符名称",
                Size = new Size(350, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent
            })
            {
                var label = new Label { Text = "占位符名称（英文/数字）:", Location = new Point(15, 15), AutoSize = true };
                var txtInput = new TextBox { Location = new Point(15, 40), Width = 300 };
                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(130, 75), Width = 80 };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(220, 75), Width = 80 };
                inputForm.Controls.AddRange(new Control[] { label, txtInput, btnOk, btnCancel });
                inputForm.AcceptButton = btnOk;
                inputForm.CancelButton = btnCancel;

                if (inputForm.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txtInput.Text))
                {
                    string key = txtInput.Text.Trim().Replace(" ", "").Replace("{", "").Replace("}", "");
                    string placeholder = "{{" + key + "}}";
                    int selStart = _rtbText.SelectionStart;
                    _rtbText.Text = _rtbText.Text.Insert(selStart, placeholder);
                    _rtbText.SelectionStart = selStart + placeholder.Length;
                    _rtbText.Focus();
                }
            }
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
            type.TextShapeName = _cmbTextShape.Text.Trim();

            // 图片Shape
            string imageShapeName = _cmbImageShape.Text.Trim();
            if (!string.IsNullOrEmpty(imageShapeName))
            {
                type.ImageShapes = new List<ImageShapeItem>
                {
                    new ImageShapeItem { ShapeName = imageShapeName, Source = "chart" }
                };
            }

            // 占位符 → Signals
            type.Signals = new List<SignalStat>();
            foreach (DataGridViewRow row in _dgvPlaceholders.Rows)
            {
                string key = row.Cells["Key"].Value?.ToString();
                string signalName = row.Cells["SignalName"].Value?.ToString();
                string calcDisplay = row.Cells["Calc"].Value?.ToString();
                string unit = row.Cells["Unit"].Value?.ToString() ?? "";

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
