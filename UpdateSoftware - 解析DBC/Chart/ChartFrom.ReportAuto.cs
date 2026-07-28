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
        private ToolStripButton _btnAnalysisTypeSelector;   // 工况选择按钮(弹出分组树面板)
        private AnalysisType _currentAnalysisType;          // 当前选中的工况分类
        private ToolStripDropDown _pickerDropDown;          // 工况快捷选择下拉面板
        // 已打开的工况编辑器(单例管理:同一工况不重复开,避免保存互相覆盖/改名产生孤儿文件)
        private readonly List<AnalysisTypeEditor> _openEditors = new List<AnalysisTypeEditor>();
        private ToolStripDropDownButton _toolAnalysisManage;   // 工况管理下拉(编辑/新增/删除/保存)
        private ToolStripTextBox _txtReportStart;
        private ToolStripTextBox _txtReportEnd;
        private ToolStripButton _btnAddReportPage;
        private ToolStripButton _btnSaveReport;
        private ToolStripButton _btnPreviewReport;
        private ToolStripButton _btnUseViewRange;   // "取视图范围"按钮(把图表可视范围填入报告起止时间)
        private string _templatesDir;   // 工况分类JSON目录路径

        /// <summary>报告自动化用:暴露绘图区控件(供ReportAutoService截图)</summary>
        internal ChartControl ChartView { get { return _chartControl; } }

        /// <summary>报告自动化用:暴露信号列表控件(供ReportAutoService截图)</summary>
        internal DataGridView SignalGridView { get { return _channelGrid; } }

        /// <summary>初始化报告自动化工具栏(行2 _analysisToolStrip):工况组+报告时间组+报告操作组</summary>
        private void InitReportToolbar()
        {
            _btnAnalysisTypeSelector = new ToolStripButton("(未选择) ▾");
            _btnAnalysisTypeSelector.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnAnalysisTypeSelector.Image = ToolbarIcons.Get("tag");
            _btnAnalysisTypeSelector.ToolTipText = "选择工况分类(支持分组管理)";
            _btnAnalysisTypeSelector.Click += _btnAnalysisTypeSelector_Click;

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

            _btnUseViewRange = new ToolStripButton("取视图范围");
            _btnUseViewRange.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnUseViewRange.Image = ToolbarIcons.Get("target");
            _btnUseViewRange.ToolTipText = "把图表当前可视时间范围填入报告起止时间(先框选缩放到目标区间再点)";
            _btnUseViewRange.Click += (s, e) => UseViewRangeForReport();

            // 工况管理收敛为一个下拉:编辑/新增/删除/保存
            _toolAnalysisManage = new ToolStripDropDownButton("管理");
            _toolAnalysisManage.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _toolAnalysisManage.Image = ToolbarIcons.Get("wrench");
            _toolAnalysisManage.ToolTipText = "工况分类管理(编辑/新增/删除/保存)";
            var menuEdit = new ToolStripMenuItem("编辑当前工况", null, _btnEditAnalysisType_Click);
            var menuNew = new ToolStripMenuItem("新增工况", null, _btnNewAnalysisType_Click);
            var menuDelete = new ToolStripMenuItem("删除当前工况", null, _btnDeleteAnalysisType_Click);
            var menuSave = new ToolStripMenuItem("保存当前工况", null, _btnSaveAnalysisType_Click);
            _toolAnalysisManage.DropDownItems.AddRange(new ToolStripItem[] { menuEdit, menuNew, menuDelete, menuSave });

            _btnAddReportPage = new ToolStripButton("添加到报告");
            _btnAddReportPage.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnAddReportPage.Image = ToolbarIcons.Get("pageplus");
            _btnAddReportPage.ToolTipText = "按当前时间范围和工况分类,追加一页到报告";
            _btnAddReportPage.Click += _btnAddReportPage_Click;

            _btnPreviewReport = new ToolStripButton("预览报告");
            _btnPreviewReport.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnPreviewReport.Image = ToolbarIcons.Get("eye");
            _btnPreviewReport.ToolTipText = "预览当前报告页,可拖动调整顺序/删除页";
            _btnPreviewReport.Click += _btnPreviewReport_Click;

            _btnSaveReport = new ToolStripButton("保存报告");
            _btnSaveReport.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnSaveReport.Image = ToolbarIcons.Get("save");
            _btnSaveReport.ToolTipText = "保存累积的报告为PPT文件";
            _btnSaveReport.Click += _btnSaveReport_Click;

            // 追加到行2工具栏(信号组之后):工况组+报告组
            _analysisToolStrip.Items.Add(new ToolStripSeparator());
            _analysisToolStrip.Items.Add(new ToolStripLabel("工况:"));
            _analysisToolStrip.Items.Add(_btnAnalysisTypeSelector);
            _analysisToolStrip.Items.Add(_toolAnalysisManage);
            _analysisToolStrip.Items.Add(new ToolStripSeparator());
            _analysisToolStrip.Items.Add(new ToolStripLabel("时间:"));
            _analysisToolStrip.Items.Add(_txtReportStart);
            _analysisToolStrip.Items.Add(new ToolStripLabel("-"));
            _analysisToolStrip.Items.Add(_txtReportEnd);
            _analysisToolStrip.Items.Add(_btnUseViewRange);
            _analysisToolStrip.Items.Add(new ToolStripSeparator());
            _analysisToolStrip.Items.Add(_btnAddReportPage);
            _analysisToolStrip.Items.Add(_btnPreviewReport);
            _analysisToolStrip.Items.Add(_btnSaveReport);

            // 加载分析项目类型JSON(含一级子目录分组)
            // 注意:此时Channels尚未初始化(ChartFrom_Load才创建),只加载列表,初始应用推迟到Load
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            _templatesDir = Path.Combine(baseDir, "ReportAuto", "templates");
            ReloadAnalysisTypes(null, false);

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

        /// <summary>模板目录(供工况管理对话框使用)</summary>
        internal string TemplatesDir => _templatesDir;

        // 首次加载数据后是否已自动重置占位符时间范围(每次启动软件只重置一次)
        private bool _placeholderRangeAutoResetDone = false;

        /// <summary>
        /// 首次跑数据绘图完成后,把当前工况占位符的固定时间范围清空(回到"跟随当前数据范围")。
        /// 避免上次数据(如0,1000)的固定范围与本次数据(如0,800)不匹配导致算不出。
        /// 每次启动只自动重置一次,之后用户手动调整的范围不再动。
        /// </summary>
        private void AutoResetPlaceholderRangesOnce()
        {
            if (_placeholderRangeAutoResetDone) return;
            _placeholderRangeAutoResetDone = true;

            var type = _currentAnalysisType;
            if (type?.Signals == null) return;

            int cleared = 0;
            foreach (var stat in type.Signals)
            {
                if (stat?.TimeRangeMap != null && stat.TimeRangeMap.Count > 0)
                {
                    stat.TimeRangeMap = null;  // 清空=跟随当前数据全范围
                    cleared++;
                }
            }
            if (cleared > 0)
            {
                _statusLabel.Text = $"状态: 播放完成,已将 {cleared} 个信号的占位符时间范围重置为当前数据范围";
                _statusLabel.ForeColor = Color.Blue;
            }
        }

        /// <summary>当前选中的工况分类(供工况管理对话框使用)</summary>
        internal AnalysisType CurrentAnalysisType => _currentAnalysisType;

        /// <summary>点击工况选择按钮:弹出分组树快捷选择面板</summary>
        private void _btnAnalysisTypeSelector_Click(object sender, EventArgs e)
        {
            ReportAutoService.LoadAnalysisTypes(_templatesDir);  // 弹出前同步磁盘最新
            var panel = new AnalysisTypePickerPanel();
            panel.Reload(ReportAutoService.AnalysisTypes, _currentAnalysisType?.Name);
            panel.Picked += (s, t) =>
            {
                _pickerDropDown?.Close();
                SelectAnalysisType(t);
            };
            panel.ManageRequested += (s, e2) =>
            {
                _pickerDropDown?.Close();
                OpenAnalysisTypeManager();
            };
            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _pickerDropDown = new ToolStripDropDown { Padding = Padding.Empty };
            _pickerDropDown.Items.Add(host);
            var b = _btnAnalysisTypeSelector.Bounds;
            _pickerDropDown.Show(_topToolStrip, new Point(b.Left, b.Bottom));
        }

        /// <summary>打开工况管理对话框(分组管理),选好后应用选中工况</summary>
        private void OpenAnalysisTypeManager()
        {
            using (var dlg = new AnalysisTypeManagerDialog(this))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedType != null)
                    SelectAnalysisType(dlg.SelectedType);
            }
        }

        /// <summary>选中并应用工况分类(选择面板/管理对话框/保存后重选共用)</summary>
        internal void SelectAnalysisType(AnalysisType type)
        {
            if (type == null) return;
            _currentAnalysisType = type;
            UpdateAnalysisTypeButtonText();
            ApplyAnalysisType(type);
        }

        /// <summary>重新加载工况列表:保持(或指定)选中项,更新按钮文本;apply=true时同步应用选中工况</summary>
        internal void ReloadAnalysisTypes(string selectName, bool apply)
        {
            string nameToSelect = selectName ?? _currentAnalysisType?.Name;
            ReportAutoService.LoadAnalysisTypes(_templatesDir);

            AnalysisType found = null;
            if (!string.IsNullOrEmpty(nameToSelect))
            {
                foreach (var t in ReportAutoService.AnalysisTypes)
                {
                    if (t.Name == nameToSelect) { found = t; break; }
                }
            }
            if (found == null && ReportAutoService.AnalysisTypes.Count > 0)
                found = ReportAutoService.AnalysisTypes[0];

            _currentAnalysisType = found;
            UpdateAnalysisTypeButtonText();
            if (apply && found != null)
                ApplyAnalysisType(found);
        }

        private void UpdateAnalysisTypeButtonText()
        {
            _btnAnalysisTypeSelector.Text = _currentAnalysisType != null
                ? _currentAnalysisType.Name + " ▾"
                : "(未选择) ▾";
        }

        private void _btnAddReportPage_Click(object sender, EventArgs e)
        {
            try
            {
                var type = _currentAnalysisType;
                if (type == null)
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
                // 确保该工况配置的信号都有数据通道(绘图区外的信号补建隐藏通道并后台补采),
                // 补采完成后再生成页面;期间禁用按钮防止重复点击
                _btnAddReportPage.Enabled = false;
                EnsureReportSignalChannels(type, () =>
                {
                    _btnAddReportPage.Enabled = true;
                    try
                    {
                        int n = ReportAutoService.AppendPage(this, type, t0, t1);
                        _statusLabel.Text = "状态: 已添加第 " + n + " 页";
                        _statusLabel.ForeColor = Color.Green;
                    }
                    catch (Exception ex2)
                    {
                        MessageBox.Show("添加报告页失败:\n" + ex2.Message, "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _statusLabel.Text = "状态: 添加报告页失败";
                        _statusLabel.ForeColor = Color.Red;
                    }
                });
            }
            catch (Exception ex)
            {
                _btnAddReportPage.Enabled = true;  // 异常时恢复按钮可用
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

        /// <summary>预览报告:弹出页面示意图列表,可拖动调整顺序/删除页,操作立即生效</summary>
        private void _btnPreviewReport_Click(object sender, EventArgs e)
        {
            if (ReportAutoService.CurrentPageCount == 0)
            {
                MessageBox.Show("当前无报告内容,请先\"添加到报告\"", "提示");
                return;
            }
            using (var dlg = new ReportPreviewDialog())
                dlg.ShowDialog(this);
            _statusLabel.Text = $"状态: 当前报告共 {ReportAutoService.CurrentPageCount} 页";
            _statusLabel.ForeColor = Color.Green;
        }

        /// <summary>解析工况中保存的DBC路径(便携容错):原路径存在直接用;否则在exe目录/工况模板目录找同名文件;都找不到返回空串</summary>
        private string ResolvePortableDbcPath(string savedPath, string group)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return savedPath;
            if (File.Exists(savedPath)) return savedPath;
            string fileName = Path.GetFileName(savedPath);
            if (string.IsNullOrEmpty(fileName)) return "";
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            var candidates = new List<string> { Path.Combine(exeDir, fileName) };
            if (!string.IsNullOrEmpty(_templatesDir))
            {
                candidates.Add(Path.Combine(_templatesDir, fileName));
                if (!string.IsNullOrEmpty(group))
                    candidates.Add(Path.Combine(_templatesDir, group, fileName));
            }
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return "";
        }

        /// <summary>应用选中的工况分类:恢复CAN通道配置+信号列表(信号列表相同则跳过重载)</summary>
        internal void ApplyAnalysisType(AnalysisType type)
        {
            if (type == null) return;
            // 恢复CAN通道配置
            if (type.BusChannels != null && type.BusChannels.Count > 0)
            {
                _busChannels.Clear();
                foreach (var bcConfig in type.BusChannels)
                {
                    // DBC路径跨机器容错:工况模板随包分发到其他电脑时,保存的绝对路径在本机未必存在。
                    // 原路径不存在则尝试exe目录/工况模板目录下的同名文件;都找不到置空(通道标记未配置,不残留他机路径)
                    string dbcPath = ResolvePortableDbcPath(bcConfig.DbcFilePath, type.Group);
                    var bc = new CanBusChannel(bcConfig.Name, bcConfig.BlfChannelId, dbcPath);
                    bc.HwChannel = bcConfig.HwChannel; // 旧工况JSON无此字段时为0，自动跟随逻辑通道号
                    // 尝试加载DBC文件
                    if (!string.IsNullOrEmpty(dbcPath))
                    {
                        try
                        {
                            bc.DbcHelper = new CAN_Data.DbcHelper();
                            bc.DbcHelper.Parse(dbcPath);
                        }
                        catch (Exception ex)
                        {
                            // DBC加载失败，继续保留通道配置但标记为未配置
                            Debug.WriteLine($"加载DBC失败: {dbcPath} - {ex.Message}");
                        }
                    }
                    _busChannels.Add(bc);
                }
                _btnBusConfig.Text = $"通道配置({_busChannels.Count})";

                // 工况通道即全局唯一DBC源：刷新聚合视图(Main/CanSend/版本校验用)并持久化
                BaseParamter.RefreshGlobalDbcFromChannels();
                BaseParamter.SaveBusChannelsConfig();
                try
                {
                    Main.main?.UpdateDbcTreeview();
                    if (Main.canSendOpenFlag) Main.canSend?.UpdateDbcTreeview();
                }
                catch { /* 窗口未初始化时忽略 */ }
            }

            // 如果当前绘图区的信号列表与工况分类保存的相同，跳过重新加载（避免编辑/保存工况后清空绘图区）
            // (构造函数阶段Channels尚未创建,信号列表恢复推迟到ChartFrom_Load中的应用)
            if (Channels == null) return;
            if (type.SignalList != null && type.SignalList.Count > 0)
            {
                // 比较口径与保存快照一致:排除IsReportOnly报告专用通道(快照不入,由Ensure按需重建),否则存在报告通道时永远误判为"不同"而清空曲线
                var currentSignalNames = Channels?.Where(c => !c.IsReportOnly).Select(c => c.DbcSignalName).OrderBy(n => n).ToList() ?? new List<string>();
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

            // 需要至少一个CAN通道已配置DBC
            if (!_busChannels.Any(bc => bc.IsConfigured))
            {
                _statusLabel.Text = "状态: 请先在通道配置中为CAN通道加载DBC文件再切换工况分类";
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
                // 定位通道DBC报文（旧数据 BusChannelIndex=-1 自动按 CAN ID+信号名 重定位；找不到跳过）
                var msg = ResolveChannelDbcMessage(preset.BusChannelIndex, preset.MessageIndex, (uint)preset.MessageId,
                    preset.SignalName, out int busIdx, out int msgIdx, out int sigIdx);
                if (msg == null) continue;

                preset.BusChannelIndex = busIdx;
                preset.MessageIndex = msgIdx;
                if (sigIdx >= 0) preset.SignalIndex = sigIdx;
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

        /// <summary>
        /// 确保占位符配置中的信号都有对应的数据通道:不在绘图区的信号创建隐藏通道(Visible=false, IsReportOnly),
        /// 之后随文件加载流程自动采集;若数据已加载则立即补采。与绘图区信号同等处理,仅不绘制曲线。
        /// </summary>
        private void EnsureReportSignalChannels(AnalysisType type, Action onComplete = null)
        {
            // 工况未配置信号时无需补采,但仍要回调,否则调用方按钮会永久禁用
            if (type?.Signals == null || type.Signals.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var added = new List<ChannelData>();
            var seen = new HashSet<string>();  // 同一报文+信号只建一次

            foreach (var stat in type.Signals)
            {
                if (stat == null || string.IsNullOrEmpty(stat.SignalName)) continue;

                // 已存在(绘图区或已建的报告通道)则跳过;MessageId=0表示仅按信号名匹配
                bool exists = stat.MessageId == 0
                    ? Channels.Any(c => c.DbcSignalName == stat.SignalName)
                    : Channels.Any(c => c.DbcMessageId == stat.MessageId && c.DbcSignalName == stat.SignalName);
                if (exists) continue;

                if (!seen.Add(stat.MessageId + "|" + stat.SignalName)) continue;

                // 在DBC中定位信号定义(遍历各总线通道DBC)
                CAN_Data.Signal sigDef = null;
                int foundMsgId = stat.MessageId, msgIndex = -1, sigIndex = -1, busIdx = -1;
                double cycleTime = 0.1;

                for (int bi = 0; bi < _busChannels.Count && sigDef == null; bi++)
                {
                    CAN_Data.DbcFile dbc = _busChannels[bi].IsConfigured ? _busChannels[bi].DbcHelper?.dbcFile : null;
                    if (dbc?.messages == null) continue;

                    for (int mi = 0; mi < dbc.messages.Count && sigDef == null; mi++)
                    {
                        if (stat.MessageId != 0 && (int)dbc.messages[mi].messgeId != stat.MessageId) continue;
                        for (int si = 0; si < dbc.messages[mi].signals.Count; si++)
                        {
                            if (dbc.messages[mi].signals[si].signalName != stat.SignalName) continue;
                            sigDef = dbc.messages[mi].signals[si];
                            foundMsgId = (int)dbc.messages[mi].messgeId;
                            msgIndex = mi; sigIndex = si; busIdx = bi;
                            cycleTime = dbc.messages[mi].cycleTime > 0 ? dbc.messages[mi].cycleTime / 1000.0 : 0.1;
                            break;
                        }
                    }
                }

                if (sigDef == null) continue;  // DBC中找不到该信号,无法采集

                var ch = new ChannelData(sigDef.signalName, Color.Gray, DateTime.Now,
                    sigDef.enumDefinitions, sigDef.unitStr, cycleTime,
                    foundMsgId, msgIndex, sigIndex, sigDef.signalName, busIdx);
                ch.Visible = false;      // 不绘制曲线
                ch.IsReportOnly = true;  // 标记为占位符报告专用通道
                Channels.Add(ch);
                added.Add(ch);
            }

            if (added.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            // 刷新通道显示(信号列表中可见但未勾选,不参与绘图)
            _chartControl.SetChannels(Channels);
            _chartControl.Invalidate();
            PopulateChannelGrid();

            // 后台线程补采(状态栏显示进度,可取消),完成后回调;未加载数据时随下次加载自动采集
            _statusLabel.Text = $"状态: 开始补采 {added.Count} 个信号的数据...";
            BackfillReportChannelsAsync(added, onComplete);
        }

        /// <summary>编辑当前选中的工况分类</summary>
        private void _btnEditAnalysisType_Click(object sender, EventArgs e)
        {
            if (_currentAnalysisType == null)
            {
                MessageBox.Show("请先选择要编辑的工况分类", "提示");
                return;
            }
            OpenAnalysisTypeEditor(_currentAnalysisType, null);
        }

        /// <summary>新增工况分类</summary>
        private void _btnNewAnalysisType_Click(object sender, EventArgs e)
        {
            OpenAnalysisTypeEditor(null, null);
        }

        /// <summary>
        /// 打开工况编辑器(统一入口:工具栏编辑/新增、管理对话框共用)。
        /// type=null表示新建;groupForNew=新建时归入的分组(null则用当前选中工况的分组)。
        /// </summary>
        internal void OpenAnalysisTypeEditor(AnalysisType type, string groupForNew)
        {
            // 同一工况(或同为新建未保存)的编辑器已打开:激活置前,不重复打开
            for (int i = _openEditors.Count - 1; i >= 0; i--)
            {
                var ed = _openEditors[i];
                if (ed.IsDisposed) { _openEditors.RemoveAt(i); continue; }
                bool same = type != null
                    ? ed.EditingTypeName == type.Name
                    : ed.EditingTypeName == null;
                if (same)
                {
                    if (ed.WindowState == FormWindowState.Minimized)
                        ed.WindowState = FormWindowState.Normal;
                    ed.Activate();
                    return;
                }
            }

            var editor = new AnalysisTypeEditor(type, Channels, _busChannels);
            _openEditors.Add(editor);
            editor.FormClosed += (s3, e3) => _openEditors.Remove(editor);
            string oldName = type?.Name;          // null=新建
            string oldGroup = type?.Group ?? "";  // 编辑时保留原分组
            // 一键计算前:补建缺失通道并后台补采,完成后回调编辑器继续计算
            editor.EnsureSignalsRequested += (s2, e2) =>
                EnsureReportSignalChannels(editor.BuildAnalysisType(), () =>
                {
                    if (!editor.IsDisposed) editor.CalculateNow();
                });
            editor.Saved += (s, newType) =>
            {
                // 分组:编辑保留原分组;新建归入指定分组(未指定则用当前选中工况的分组)
                newType.Group = type != null ? oldGroup
                    : (groupForNew ?? _currentAnalysisType?.Group ?? "");
                SaveAnalysisTypeJson(newType, oldName, oldGroup);
                oldName = newType.Name;   // 编辑器不再关闭,连续保存时以上次名为旧名,改名不留孤儿文件
                oldGroup = newType.Group;
                EnsureReportSignalChannels(newType);
                ReloadAnalysisTypes(newType.Name, true);
                _statusLabel.Text = $"状态: 工况分类 \"{newType.Name}\" 已保存";
                _statusLabel.ForeColor = Color.Green;
            };
            editor.Show();
        }

        /// <summary>保存当前选中的工况分类(不打开编辑器):快照当前绘图区信号列表并写回JSON</summary>
        private void _btnSaveAnalysisType_Click(object sender, EventArgs e)
        {
            var currentType = _currentAnalysisType;
            if (currentType == null)
            {
                MessageBox.Show("请先选择要保存的工况分类", "提示");
                return;
            }

            // 快照当前绘图区信号列表(占位符报告专用通道不入快照,由Ensure按需重建);
            // 文字模板/占位符配置保持原样,仅在编辑器中修改
            currentType.SignalList = new List<SignalPresetItem>();
            foreach (var ch in Channels)
            {
                if (ch.IsReportOnly) continue;
                currentType.SignalList.Add(new SignalPresetItem
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

            // 记录保存时的数据范围(供编辑器校验数据变化,不含隐藏通道)
            double dMin = double.MaxValue, dMax = double.MinValue;
            foreach (var ch in Channels)
            {
                if (ch.IsReportOnly) continue;
                var xr = ch.GetXRange();
                if (xr.Min < dMin) dMin = xr.Min;
                if (xr.Max > dMax) dMax = xr.Max;
            }
            if (dMin < dMax)
            {
                currentType.DataRangeAtSave =
                    dMin.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + dMax.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }

            SaveAnalysisTypeJson(currentType, null);  // 名称/分组未变,无需删旧文件
            ReloadAnalysisTypes(currentType.Name, true);
            _statusLabel.Text = $"状态: 工况分类 \"{currentType.Name}\" 已保存";
            _statusLabel.ForeColor = Color.Green;
        }

        /// <summary>删除当前选中的工况分类</summary>
        private void _btnDeleteAnalysisType_Click(object sender, EventArgs e)
        {
            var currentType = _currentAnalysisType;
            if (currentType == null)
            {
                MessageBox.Show("请先选择要删除的工况分类", "提示");
                return;
            }
            if (MessageBox.Show($"确定要删除工况分类 \"{currentType.Name}\" 吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // 删除JSON文件(按分组定位),分组目录空则一并删除
            string dir = string.IsNullOrEmpty(currentType.Group)
                ? _templatesDir : Path.Combine(_templatesDir, currentType.Group);
            string jsonPath = Path.Combine(dir, currentType.Name + ".json");
            try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { }
            CleanupEmptyGroupDir(currentType.Group);

            // 刷新列表并应用新的默认选中
            ReloadAnalysisTypes(null, true);
        }

        /// <summary>分组目录空则删除该目录</summary>
        private void CleanupEmptyGroupDir(string group)
        {
            if (string.IsNullOrEmpty(group)) return;
            try
            {
                string dir = Path.Combine(_templatesDir, group);
                if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0
                    && Directory.GetDirectories(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch { }
        }

        /// <summary>将AnalysisType保存为JSON文件(路径含分组:templates/<分组>/<工况名>.json)</summary>
        private void SaveAnalysisTypeJson(AnalysisType type, string oldName, string oldGroup = null)
        {
            string dir = string.IsNullOrEmpty(type.Group)
                ? _templatesDir : Path.Combine(_templatesDir, type.Group);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 改名或改组:删除旧位置文件
            bool renamed = !string.IsNullOrEmpty(oldName) && oldName != type.Name;
            bool regrouped = oldGroup != null && oldGroup != (type.Group ?? "");
            if (renamed || regrouped)
            {
                string oldDir = string.IsNullOrEmpty(oldGroup)
                    ? _templatesDir : Path.Combine(_templatesDir, oldGroup);
                string oldPath = Path.Combine(oldDir, oldName + ".json");
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                if (oldGroup != null) CleanupEmptyGroupDir(oldGroup);
            }

            // 保存当前CAN通道配置
            if (_busChannels != null && _busChannels.Count > 0)
            {
                type.BusChannels = new List<BusChannelConfig>();
                foreach (var bc in _busChannels)
                {
                    type.BusChannels.Add(new BusChannelConfig(bc.Name, bc.BlfChannelId, bc.DbcFilePath) { HwChannel = bc.HwChannel });
                }
            }
            else
            {
                type.BusChannels = null;
            }

            string jsonPath = Path.Combine(dir, type.Name + ".json");
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(type, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>把图表当前可视时间范围填入报告起止时间并生效(先框选缩放到目标区间再点)</summary>
        private void UseViewRangeForReport()
        {
            double t0 = _chartControl.GetCurrentXMin();
            double t1 = _chartControl.GetCurrentXMax();
            _txtReportStart.Text = t0.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            _txtReportEnd.Text = t1.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            ApplyReportTimeRange();
            _statusLabel.Text = $"状态: 已按视图范围设置报告时间 {t0:0.##}s ~ {t1:0.##}s";
            _statusLabel.ForeColor = Color.Green;
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
