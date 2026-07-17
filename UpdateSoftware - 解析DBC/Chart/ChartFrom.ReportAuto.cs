// ChartFrom 的报告自动化扩展(partial):工具栏UI + 添加页/保存报告handler
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using PCAN_Client.ReportAuto;

namespace PCAN_Client
{
    /// <summary>
    /// ChartFrom 的报告自动化扩展。复用主文件的 _chartControl/_channelGrid/_topToolStrip/_statusLabel。
    /// 不改主文件绘图逻辑,仅追加"分析项目类型下拉+时间范围+添加到报告/保存报告"工具栏组。
    /// </summary>
    public partial class ChartFrom
    {
        private ToolStripComboBox _cmbAnalysisType;
        private ToolStripButton _btnEditAnalysisType;
        private ToolStripButton _btnNewAnalysisType;
        private ToolStripButton _btnDeleteAnalysisType;
        private ToolStripButton _btnSaveAnalysisType;
        private ToolStripTextBox _txtReportStart;
        private ToolStripTextBox _txtReportEnd;
        private ToolStripButton _btnAddReportPage;
        private ToolStripButton _btnSaveReport;
        private string _templatesDir;   // 工况分类JSON目录路径

        /// <summary>报告自动化用:暴露绘图区控件(供ReportAutoService截图)</summary>
        internal ChartControl ChartView { get { return _chartControl; } }

        /// <summary>报告自动化用:暴露信号列表控件(供ReportAutoService截图)</summary>
        internal DataGridView SignalGridView { get { return _channelGrid; } }

        /// <summary>初始化报告自动化工具栏:下拉+时间输入框+两按钮,加载工况分类与模板路径</summary>
        private void InitReportToolbar()
        {
            _cmbAnalysisType = new ToolStripComboBox();
            _cmbAnalysisType.Width = 170;
            _cmbAnalysisType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbAnalysisType.ToolTipText = "选择分析项目类型";

            _txtReportStart = new ToolStripTextBox();
            _txtReportStart.Width = 60;
            _txtReportStart.Text = "0";
            _txtReportStart.ToolTipText = "报告起始时间(秒,输入后按回车生效)";
            _txtReportStart.KeyDown += _txtReportTime_KeyDown;
            _txtReportStart.TextBox.Validated += _txtReportTime_Validated;

            _txtReportEnd = new ToolStripTextBox();
            _txtReportEnd.Width = 60;
            _txtReportEnd.Text = "10";
            _txtReportEnd.ToolTipText = "报告结束时间(秒,输入后按回车生效)";
            _txtReportEnd.KeyDown += _txtReportTime_KeyDown;
            _txtReportEnd.TextBox.Validated += _txtReportTime_Validated;

            _btnEditAnalysisType = new ToolStripButton("编辑");
            _btnEditAnalysisType.ToolTipText = "编辑当前选中的工况分类";
            _btnEditAnalysisType.Click += _btnEditAnalysisType_Click;

            _btnNewAnalysisType = new ToolStripButton("新增");
            _btnNewAnalysisType.ToolTipText = "新增一个工况分类";
            _btnNewAnalysisType.Click += _btnNewAnalysisType_Click;

            _btnDeleteAnalysisType = new ToolStripButton("删除");
            _btnDeleteAnalysisType.ToolTipText = "删除当前选中的工况分类";
            _btnDeleteAnalysisType.Click += _btnDeleteAnalysisType_Click;

            _btnSaveAnalysisType = new ToolStripButton("保存");
            _btnSaveAnalysisType.ToolTipText = "保存当前工况分类（编辑并保存）";
            _btnSaveAnalysisType.Click += _btnSaveAnalysisType_Click;

            _btnAddReportPage = new ToolStripButton("添加到报告");
            _btnAddReportPage.ToolTipText = "按当前时间范围和工况分类,追加一页到报告";
            _btnAddReportPage.Click += _btnAddReportPage_Click;

            _btnSaveReport = new ToolStripButton("保存报告");
            _btnSaveReport.ToolTipText = "保存累积的报告为PPT文件";
            _btnSaveReport.Click += _btnSaveReport_Click;

            // 追加到现有工具栏末尾(不改动原AddRange数组)
            _topToolStrip.Items.Add(new ToolStripSeparator());
            _topToolStrip.Items.Add(new ToolStripLabel("工况:"));
            _topToolStrip.Items.Add(_cmbAnalysisType);
            _topToolStrip.Items.Add(_btnEditAnalysisType);
            _topToolStrip.Items.Add(_btnNewAnalysisType);
            _topToolStrip.Items.Add(_btnDeleteAnalysisType);
            _topToolStrip.Items.Add(_btnSaveAnalysisType);
            _topToolStrip.Items.Add(new ToolStripLabel("时间:"));
            _topToolStrip.Items.Add(_txtReportStart);
            _topToolStrip.Items.Add(new ToolStripLabel("-"));
            _topToolStrip.Items.Add(_txtReportEnd);
            _topToolStrip.Items.Add(_btnAddReportPage);
            _topToolStrip.Items.Add(_btnSaveReport);

            // 加载分析项目类型JSON
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            _templatesDir = Path.Combine(baseDir, "ReportAuto", "templates");
            ReportAutoService.LoadAnalysisTypes(_templatesDir);
            foreach (var t in ReportAutoService.AnalysisTypes)
                _cmbAnalysisType.Items.Add(t);
            if (_cmbAnalysisType.Items.Count > 0)
                _cmbAnalysisType.SelectedIndex = 0;

            // 选中工况分类时自动加载信号列表
            _cmbAnalysisType.SelectedIndexChanged += _cmbAnalysisType_SelectedIndexChanged;

            // 默认模板路径:依次找 exe同级/项目根 的 模板文件.pptx
            string templatePath = ResolveDefaultTemplate(baseDir);
            ReportAutoService.SetTemplatePath(templatePath);
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] exe目录: {baseDir}");
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板原始路径: {templatePath}");
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板实际路径: {ReportAutoService.GetTemplatePath()}");
        }

        /// <summary>按候选位置寻找默认单页模板</summary>
        private static string ResolveDefaultTemplate(string baseDir)
        {
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "模板文件.pptx"),
                Path.Combine(baseDir, "ReportAuto", "模板文件.pptx"),
                // 开发时exe在 Output/,项目根在 ../AutoPPT/
                Path.GetFullPath(Path.Combine(baseDir, "..", "AutoPPT", "模板文件.pptx")),
                // 兼容其他部署场景
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "模板文件.pptx")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "AutoPPT", "模板文件.pptx")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "UpdateSoftware - 解析DBC", "模板文件.pptx")),
            };
            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return Path.Combine(baseDir, "模板文件.pptx"); // 兜底(运行时由按钮handler报错)
        }

        private void _btnAddReportPage_Click(object sender, EventArgs e)
        {
            try
            {
                if (!(_cmbAnalysisType.SelectedItem is AnalysisType type))
                {
                    MessageBox.Show("请先选择分析项目类型", "提示");
                    return;
                }
                if (!double.TryParse(_txtReportStart.Text, out double t0) ||
                    !double.TryParse(_txtReportEnd.Text, out double t1))
                {
                    MessageBox.Show("请输入有效的起始/结束时间(秒,数字)", "提示");
                    return;
                }
                int n = ReportAutoService.AppendPage(this, type, t0, t1);
                _statusLabel.Text = "状态: 已添加第 " + n + " 页";
                _statusLabel.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show("添加报告页失败:\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = "状态: 添加报告页失败";
                _statusLabel.ForeColor = Color.Red;
            }
        }

        private void _btnSaveReport_Click(object sender, EventArgs e)
        {
            try
            {
                if (ReportAutoService.CurrentPageCount == 0)
                {
                    MessageBox.Show("当前无报告内容,请先\"添加到报告\"", "提示");
                    return;
                }
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "PPT文件|*.pptx";
                    sfd.FileName = "报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pptx";
                    if (sfd.ShowDialog() != DialogResult.OK) return;
                    ReportAutoService.SaveAs(sfd.FileName);
                    int pages = ReportAutoService.CurrentPageCount;
                    _statusLabel.Text = "状态: 报告已保存 " + Path.GetFileName(sfd.FileName);
                    _statusLabel.ForeColor = Color.Green;
                    MessageBox.Show("报告已保存:\n" + sfd.FileName + "\n共 " + pages + " 页", "完成");
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + sfd.FileName + "\""); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存报告失败:\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = "状态: 保存报告失败";
                _statusLabel.ForeColor = Color.Red;
            }
        }

        /// <summary>选中工况分类变化时，自动加载该类型保存的信号列表</summary>
        private void _cmbAnalysisType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!(_cmbAnalysisType.SelectedItem is AnalysisType type)) return;

            // 恢复CAN通道配置
            if (type.BusChannels != null && type.BusChannels.Count > 0)
            {
                _busChannels.Clear();
                foreach (var bcConfig in type.BusChannels)
                {
                    var bc = new CanBusChannel(bcConfig.Name, bcConfig.BlfChannelId, bcConfig.DbcFilePath);
                    // 尝试加载DBC文件
                    if (!string.IsNullOrEmpty(bcConfig.DbcFilePath) && File.Exists(bcConfig.DbcFilePath))
                    {
                        try
                        {
                            bc.DbcHelper = new CAN_Data.DbcHelper();
                            bc.DbcHelper.Parse(bcConfig.DbcFilePath);
                        }
                        catch (Exception ex)
                        {
                            // DBC加载失败，继续保留通道配置但标记为未配置
                            Debug.WriteLine($"加载DBC失败: {bcConfig.DbcFilePath} - {ex.Message}");
                        }
                    }
                    _busChannels.Add(bc);
                }
                _btnBusConfig.Text = $"通道配置({_busChannels.Count})";
            }

            // 如果当前绘图区的信号列表与工况分类保存的相同，跳过重新加载（避免编辑/保存工况后清空绘图区）
            if (type.SignalList != null && type.SignalList.Count > 0)
            {
                var currentSignalNames = Channels?.Select(c => c.DbcSignalName).OrderBy(n => n).ToList() ?? new List<string>();
                var savedSignalNames = type.SignalList.Select(s => s.SignalName).OrderBy(n => n).ToList();
                if (currentSignalNames.SequenceEqual(savedSignalNames))
                {
                    // 信号列表相同，只更新状态不重新加载
                    _statusLabel.Text = $"状态: 已切换到工况分类 \"{type.Name}\"";
                    _statusLabel.ForeColor = Color.Green;
                    return;
                }
            }

            if (type.SignalList == null || type.SignalList.Count == 0) return;

            // 需要DBC已加载（兼容模式或至少一个CAN通道已配置）
            bool hasAnyDbc = (BaseParamter.dbcHelper != null && BaseParamter.dbcHelper.dbcFile != null &&
                             BaseParamter.dbcHelper.dbcFile.messages.Count > 0);
            if (!hasAnyDbc && _busChannels.Count == 0)
            {
                _statusLabel.Text = "状态: 请先加载DBC文件或配置CAN通道再切换工况分类";
                _statusLabel.ForeColor = Color.Orange;
                return;
            }

            // 清空当前信号
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                    channel.Clear();
                Channels.Clear();
            }

            // 从SignalList恢复信号
            Color[] palette = new Color[]
            {
                Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple,
                Color.Cyan, Color.Magenta, Color.Lime, Color.Gold, Color.Teal
            };
            int colorIdx = 0;

            foreach (var preset in type.SignalList)
            {
                // 根据BusChannelIndex选择对应的DBC实例
                CAN_Data.DbcFile dbcFile = null;
                if (preset.BusChannelIndex >= 0 && preset.BusChannelIndex < _busChannels.Count)
                {
                    var busCh = _busChannels[preset.BusChannelIndex];
                    if (busCh.IsConfigured)
                        dbcFile = busCh.DbcHelper.dbcFile;
                }
                else if (BaseParamter.dbcHelper?.dbcFile != null)
                {
                    dbcFile = BaseParamter.dbcHelper.dbcFile;
                }

                if (dbcFile == null) continue;
                if (preset.MessageIndex < 0 || preset.MessageIndex >= dbcFile.messages.Count) continue;
                var msg = dbcFile.messages[preset.MessageIndex];
                if (preset.SignalIndex < 0 || preset.SignalIndex >= msg.signals.Count) continue;

                var signal = msg.signals[preset.SignalIndex];
                Color color = !string.IsNullOrEmpty(preset.Color)
                    ? ColorTranslator.FromHtml(preset.Color)
                    : palette[colorIdx % palette.Length];
                colorIdx++;

                string channelName = !string.IsNullOrEmpty(signal.signalName) ? signal.signalName : ("Signal_" + preset.SignalIndex);
                double cycleTime = msg.cycleTime > 0
                    ? (double)(msg.cycleTime / 1000.0)
                    : 0.1;

                var ch = new ChannelData(channelName, color, DateTime.Now,
                    signal.enumDefinitions, signal.unitStr,
                    cycleTime,
                    preset.MessageId,
                    preset.MessageIndex,
                    preset.SignalIndex,
                    preset.SignalName ?? "",
                    preset.BusChannelIndex);
                ch.Visible = preset.Visible;
                Channels.Add(ch);
            }

            _chartControl.SetChannels(Channels);
            _chartControl.Invalidate();
            PopulateChannelGrid();

            _statusLabel.Text = $"状态: 已加载工况分类 \"{type.Name}\" 的信号列表 ({type.SignalList.Count}个)";
            _statusLabel.ForeColor = Color.Green;
        }

        /// <summary>编辑当前选中的工况分类</summary>
        private void _btnEditAnalysisType_Click(object sender, EventArgs e)
        {
            if (!(_cmbAnalysisType.SelectedItem is AnalysisType currentType))
            {
                MessageBox.Show("请先选择要编辑的工况分类", "提示");
                return;
            }

            var editor = new AnalysisTypeEditor(currentType, Channels, _busChannels);
            string oldName = currentType.Name;  // 保存旧名称用于刷新
            editor.Saved += (s, newType) =>
            {
                SaveAnalysisTypeJson(newType, oldName);
                RefreshAnalysisTypeList();
                // 选中编辑后的类型
                for (int i = 0; i < _cmbAnalysisType.Items.Count; i++)
                {
                    if ((_cmbAnalysisType.Items[i] as AnalysisType)?.Name == newType.Name)
                    {
                        _cmbAnalysisType.SelectedIndex = i;
                        break;
                    }
                }
            };
            editor.Show();
        }

        /// <summary>新增工况分类</summary>
        private void _btnNewAnalysisType_Click(object sender, EventArgs e)
        {
            var editor = new AnalysisTypeEditor(null, Channels, _busChannels);
            editor.Saved += (s, newType) =>
            {
                SaveAnalysisTypeJson(newType, null);
                RefreshAnalysisTypeList();
            };
            editor.Show();
        }

        /// <summary>保存当前选中的工况分类（打开编辑器编辑并保存）</summary>
        private void _btnSaveAnalysisType_Click(object sender, EventArgs e)
        {
            if (!(_cmbAnalysisType.SelectedItem is AnalysisType currentType))
            {
                MessageBox.Show("请先选择要保存的工况分类", "提示");
                return;
            }

            var editor = new AnalysisTypeEditor(currentType, Channels, _busChannels);
            string oldName = currentType.Name;
            editor.Saved += (s, newType) =>
            {
                SaveAnalysisTypeJson(newType, oldName);
                RefreshAnalysisTypeList();
                // 选中保存后的类型
                for (int i = 0; i < _cmbAnalysisType.Items.Count; i++)
                {
                    if ((_cmbAnalysisType.Items[i] as AnalysisType)?.Name == newType.Name)
                    {
                        _cmbAnalysisType.SelectedIndex = i;
                        break;
                    }
                }
                _statusLabel.Text = $"状态: 工况分类 \"{newType.Name}\" 已保存";
                _statusLabel.ForeColor = Color.Green;
            };
            editor.Show();
        }

        /// <summary>删除当前选中的工况分类</summary>
        private void _btnDeleteAnalysisType_Click(object sender, EventArgs e)
        {
            if (!(_cmbAnalysisType.SelectedItem is AnalysisType currentType))
            {
                MessageBox.Show("请先选择要删除的工况分类", "提示");
                return;
            }
            if (MessageBox.Show($"确定要删除工况分类 \"{currentType.Name}\" 吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // 删除JSON文件
            string jsonPath = Path.Combine(_templatesDir, currentType.Name + ".json");
            try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { }

            // 刷新下拉
            RefreshAnalysisTypeList();
        }

        /// <summary>将AnalysisType保存为JSON文件</summary>
        private void SaveAnalysisTypeJson(AnalysisType type, string oldName)
        {
            if (!Directory.Exists(_templatesDir))
                Directory.CreateDirectory(_templatesDir);

            // 如果改了名字，删除旧文件
            if (!string.IsNullOrEmpty(oldName) && oldName != type.Name)
            {
                string oldPath = Path.Combine(_templatesDir, oldName + ".json");
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
            }

            // 保存当前CAN通道配置
            if (_busChannels != null && _busChannels.Count > 0)
            {
                type.BusChannels = new List<BusChannelConfig>();
                foreach (var bc in _busChannels)
                {
                    type.BusChannels.Add(new BusChannelConfig(bc.Name, bc.BlfChannelId, bc.DbcFilePath));
                }
            }
            else
            {
                type.BusChannels = null;
            }

            string jsonPath = Path.Combine(_templatesDir, type.Name + ".json");
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(type, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>重新加载工况分类列表并刷新下拉</summary>
        private void RefreshAnalysisTypeList()
        {
            ReportAutoService.LoadAnalysisTypes(_templatesDir);
            _cmbAnalysisType.Items.Clear();
            foreach (var t in ReportAutoService.AnalysisTypes)
                _cmbAnalysisType.Items.Add(t);
            if (_cmbAnalysisType.Items.Count > 0)
                _cmbAnalysisType.SelectedIndex = 0;
        }

        /// <summary>时间输入框按回车时立即缩放到时间范围</summary>
        private void _txtReportTime_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyReportTimeRange();
            }
        }

        /// <summary>时间输入框失去焦点时立即缩放到时间范围</summary>
        private void _txtReportTime_Validated(object sender, EventArgs e)
        {
            ApplyReportTimeRange();
        }

        /// <summary>解析时间输入框并缩放绘图区到指定时间范围</summary>
        private void ApplyReportTimeRange()
        {
            if (!double.TryParse(_txtReportStart.Text, out double t0) ||
                !double.TryParse(_txtReportEnd.Text, out double t1))
                return;
            if (!(t1 > t0)) return;
            try
            {
                _chartControl.SetGlobalXRange(t0, t1);
                _chartControl.Invalidate();
                _statusLabel.Text = $"状态: 已缩放到 {t0:F2}-{t1:F2} 秒";
                _statusLabel.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "状态: 缩放失败 - " + ex.Message;
                _statusLabel.ForeColor = Color.Red;
            }
        }

        /// <summary>设置报告起始时间（由主文件在播放开始时调用）</summary>
        internal void SetReportStartTime(double t)
        {
            _txtReportStart.Text = t.ToString("F2");
        }

        /// <summary>设置报告结束时间并缩放到完整范围（由主文件在播放停止/完成时调用）</summary>
        internal void SetReportEndTime(double t)
        {
            _txtReportEnd.Text = t.ToString("F2");
            ApplyReportTimeRange();
        }
    }
}
