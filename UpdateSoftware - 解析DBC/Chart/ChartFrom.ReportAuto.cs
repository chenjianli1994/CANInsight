// ChartFrom 的报告自动化扩展(partial):工具栏UI + 添加页/保存报告handler
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
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
        private ToolStripTextBox _txtReportStart;
        private ToolStripTextBox _txtReportEnd;
        private ToolStripButton _btnAddReportPage;
        private ToolStripButton _btnSaveReport;

        /// <summary>报告自动化用:暴露绘图区控件(供ReportAutoService截图)</summary>
        internal ChartControl ChartView { get { return _chartControl; } }

        /// <summary>报告自动化用:暴露信号列表控件(供ReportAutoService截图)</summary>
        internal DataGridView SignalGridView { get { return _channelGrid; } }

        /// <summary>初始化报告自动化工具栏:下拉+时间输入框+两按钮,加载分析类型与模板路径</summary>
        private void InitReportToolbar()
        {
            _cmbAnalysisType = new ToolStripComboBox();
            _cmbAnalysisType.Width = 170;
            _cmbAnalysisType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbAnalysisType.ToolTipText = "选择分析项目类型";

            _txtReportStart = new ToolStripTextBox();
            _txtReportStart.Width = 60;
            _txtReportStart.Text = "0";
            _txtReportStart.ToolTipText = "报告起始时间(秒)";

            _txtReportEnd = new ToolStripTextBox();
            _txtReportEnd.Width = 60;
            _txtReportEnd.Text = "10";
            _txtReportEnd.ToolTipText = "报告结束时间(秒)";

            _btnAddReportPage = new ToolStripButton("添加到报告");
            _btnAddReportPage.ToolTipText = "按当前时间范围和分析类型,追加一页到报告";
            _btnAddReportPage.Click += _btnAddReportPage_Click;

            _btnSaveReport = new ToolStripButton("保存报告");
            _btnSaveReport.ToolTipText = "保存累积的报告为PPT文件";
            _btnSaveReport.Click += _btnSaveReport_Click;

            // 追加到现有工具栏末尾(不改动原AddRange数组)
            _topToolStrip.Items.Add(new ToolStripSeparator());
            _topToolStrip.Items.Add(new ToolStripLabel("分析:"));
            _topToolStrip.Items.Add(_cmbAnalysisType);
            _topToolStrip.Items.Add(new ToolStripLabel("时间:"));
            _topToolStrip.Items.Add(_txtReportStart);
            _topToolStrip.Items.Add(new ToolStripLabel("-"));
            _topToolStrip.Items.Add(_txtReportEnd);
            _topToolStrip.Items.Add(_btnAddReportPage);
            _topToolStrip.Items.Add(_btnSaveReport);

            // 加载分析项目类型JSON
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            string templatesDir = Path.Combine(baseDir, "ReportAuto", "templates");
            ReportAutoService.LoadAnalysisTypes(templatesDir);
            foreach (var t in ReportAutoService.AnalysisTypes)
                _cmbAnalysisType.Items.Add(t);
            if (_cmbAnalysisType.Items.Count > 0)
                _cmbAnalysisType.SelectedIndex = 0;

            // 默认模板路径:依次找 exe同级/项目根 的 模板文件.pptx
            ReportAutoService.SetTemplatePath(ResolveDefaultTemplate(baseDir));
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
    }
}
