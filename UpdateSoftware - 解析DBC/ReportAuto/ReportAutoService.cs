// 报告自动生成编排+会话管理:缩放→截图→算值→追加页
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;

namespace PCAN_Client.ReportAuto
{
    /// <summary>报告中一页的元数据(供预览列表显示/排序/删除)</summary>
    internal class ReportPageInfo
    {
        public string Name;        // 工况名
        public double TimeStart;   // 报告时间范围起点(秒)
        public double TimeEnd;     // 报告时间范围终点(秒)
        public Bitmap Thumbnail;   // 添加页时的图表截图缩略图(可空,预览卡片显示真图用)
    }

    /// <summary>
    /// 报告自动生成服务:维护当前报告会话,编排 缩放→截图→计算→填PPT→追加。
    /// 一次AppendPage追加一页,多次累积,SaveAs输出多页PPT。
    /// </summary>
    internal static class ReportAutoService
    {
        private static PptReportBuilder _builder = null;
        private static bool _started = false;
        private static string _templatePath = null;
        private static string _templatePathRaw = null;   // 未经可读性验证的原始路径(启动时仅记录,首次使用时才验证)
        private static readonly object _templateLock = new object();
        // 已累积页的元数据(与PPT文档内页顺序一致,随AppendPage/DeletePage/MovePage同步)
        private static readonly List<ReportPageInfo> _pages = new List<ReportPageInfo>();

        /// <summary>已加载的分析项目类型列表(供下拉)</summary>
        public static List<AnalysisType> AnalysisTypes { get; private set; } = new List<AnalysisType>();

        /// <summary>当前报告页数</summary>
        public static int CurrentPageCount => _builder?.PageCount ?? 0;

        /// <summary>当前报告页元数据列表(只读,顺序与PPT一致)</summary>
        public static IReadOnlyList<ReportPageInfo> Pages => _pages;

        /// <summary>设置PPT模板路径(仅记录路径;DLP可读性验证延迟到首次使用模板时执行,避免拖慢启动)</summary>
        public static void SetTemplatePath(string path)
        {
            lock (_templateLock)
            {
                _templatePathRaw = path;
                _templatePath = null;
            }
        }

        /// <summary>首次使用模板前调用:执行一次DLP可读性验证(约1~2秒),后续直接返回缓存结果</summary>
        private static void EnsureTemplateReady()
        {
            lock (_templateLock)
            {
                if (_templatePath == null && _templatePathRaw != null)
                {
                    _templatePath = TryEnsureReadableTemplate(_templatePathRaw);
                }
            }
        }

        /// <summary>获取当前PPT模板路径（供编辑器读取Shape列表）</summary>
        public static string GetTemplatePath()
        {
            EnsureTemplateReady();
            return _templatePath;
        }

        /// <summary>确保模板可被OpenXml打开:先直接试打开并验证内容,失败则用cmd /c type走DLP授权路径提取明文副本</summary>
        private static string TryEnsureReadableTemplate(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return path;
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 尝试验证模板: {path}");
            
            // 1) 先尝试直接用OpenXml打开并验证模板内容
            try
            {
                using (var doc = PresentationDocument.Open(path, false))
                {
                    var presPart = doc.PresentationPart;
                    var slidePart = presPart?.SlideParts?.FirstOrDefault();
                    if (slidePart?.Slide != null)
                    {
                        // 验证模板是否包含预期的Shape标识符
                        // 如果DLP透明加密,OpenXml可能读到损坏的数据,找不到这些Shape
                        bool hasTextDesc = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Shape>()
                            .Any(sp => sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value == TemplateShapeIds.TextDesc);
                        bool hasChartImage = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Picture>()
                            .Any(pic => pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value == TemplateShapeIds.ChartImage);
                        
                        if (hasTextDesc && hasChartImage)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板可直接打开且验证通过");
                            return path;
                        }
                        System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板打开但验证失败: TextDesc={hasTextDesc}, ChartImage={hasChartImage}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板打开但无有效幻灯片");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReportAuto] 模板打开失败: {ex.Message}");
            }
            
            // 2) 直接打开失败或验证不通过,用cmd /c type提取明文(DLP授权进程,本机已验证可用)
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 需要DLP解密");
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "ReportAutoTpl_" + Guid.NewGuid().ToString("N") + ".pptx");
                System.Diagnostics.Debug.WriteLine($"[ReportAuto] 尝试DLP解密到: {tmp}");
                var psi = new ProcessStartInfo("cmd.exe", "/c type \"" + path + "\" > \"" + tmp + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(30000);
                if (File.Exists(tmp) && new FileInfo(tmp).Length > 0)
                {
                    // 验证解密后的副本
                    try
                    {
                        using (var doc = PresentationDocument.Open(tmp, false))
                        {
                            var presPart = doc.PresentationPart;
                            var slidePart = presPart?.SlideParts?.FirstOrDefault();
                            if (slidePart?.Slide != null)
                            {
                                bool hasTextDesc = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Shape>()
                                    .Any(sp => sp.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value == TemplateShapeIds.TextDesc);
                                bool hasChartImage = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Picture>()
                                    .Any(pic => pic.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value == TemplateShapeIds.ChartImage);
                                
                                if (hasTextDesc && hasChartImage)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ReportAuto] DLP解密成功且验证通过: {tmp}");
                                    return tmp;
                                }
                                System.Diagnostics.Debug.WriteLine($"[ReportAuto] DLP解密后验证失败: TextDesc={hasTextDesc}, ChartImage={hasChartImage}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ReportAuto] DLP解密副本打开失败: {ex.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[ReportAuto] DLP解密失败或文件为空");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReportAuto] DLP解密异常: {ex.Message}");
            }
            System.Diagnostics.Debug.WriteLine($"[ReportAuto] 解密失败, 返回原路径: {path}");
            return path; // 解密失败,返回原路径(后续StartReport会报错提示)
        }

        /// <summary>从指定目录加载所有分析项目类型JSON</summary>
        public static void LoadAnalysisTypes(string templatesDir)
        {
            AnalysisTypes = AnalysisTypeLoader.LoadAll(templatesDir) ?? new List<AnalysisType>();
        }

        /// <summary>新建/重置报告会话(清空已累积页)</summary>
        public static void NewReport()
        {
            if (_builder != null) { _builder.Dispose(); _builder = null; }
            _started = false;
            foreach (var p in _pages) p.Thumbnail?.Dispose();
            _pages.Clear();
        }

        /// <summary>把图表截图等比缩小为缩略图(letterbox居中,白底)</summary>
        private static Bitmap MakeThumbnail(Bitmap src, int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float scale = Math.Min((float)w / src.Width, (float)h / src.Height);
                int dw = Math.Max(1, (int)(src.Width * scale));
                int dh = Math.Max(1, (int)(src.Height * scale));
                g.DrawImage(src, (w - dw) / 2, (h - dh) / 2, dw, dh);
            }
            return bmp;
        }

        /// <summary>
        /// 追加一页到当前报告:缩放→截图→算值→填占位符/图/表→追加。
        /// 返回追加后的总页数。
        /// </summary>
        public static int AppendPage(ChartFrom form, AnalysisType type, double t0, double t1)
        {
            if (form == null) throw new InvalidOperationException("绘图窗体未就绪");
            if (type == null) throw new InvalidOperationException("未选择分析项目类型");
            EnsureTemplateReady(); /* 首次使用模板时执行DLP可读性验证 */
            if (string.IsNullOrEmpty(_templatePath) || !File.Exists(_templatePath))
                throw new FileNotFoundException("未设置PPT模板路径或模板不存在: " + (_templatePath ?? "(空)"));
            if (form.Channels == null || form.Channels.Count == 0)
                throw new InvalidOperationException("未加载信号数据,请先加载DBC/日志并添加信号");
            if (!(t1 > t0))
                throw new InvalidOperationException("结束时间必须大于起始时间");

            // 确保会话开始(首次自动Start)
            if (_builder == null || !_started)
            {
                _builder = new PptReportBuilder();
                _builder.StartReport(_templatePath);
                _started = true;
            }

            // 1) 缩放到目标时间范围并等待重绘
            form.ChartView.SetGlobalXRange(t0, t1);
            form.ChartView.PerformLayout();   // 强制同步布局
            form.ChartView.Update();       // 强制同步绘制
            Application.DoEvents();
            Thread.Sleep(150);             // 从80ms增至150ms，确保复杂图表渲染完成

            // 2) 截图:绘图区离屏高清 + 信号列表，合成为一张左右拼接图
            var chartBmp = ChartCapturer.RenderChart(form.ChartView, 3200, 1920);
            var compositeBmp = ChartCapturer.ComposeScreenshot(chartBmp, form.SignalGridView);
            var images = new Dictionary<string, Bitmap>();
            if (compositeBmp != null) images["chart"] = compositeBmp;

            // 3) 计算各信号区间统计值(avg/min/max/range)→占位符KEY映射
            var values = new Dictionary<string, string>();
            if (type.Signals != null)
            {
                foreach (var stat in type.Signals)
                {
                    ChannelData ch;
                    if (stat.MessageId == 0)
                    {
                        // MessageId=0 表示不指定报文ID，仅按信号名匹配（精确优先，再大小写不敏感）
                        ch = form.Channels.FirstOrDefault(c => c.DbcSignalName == stat.SignalName)
                          ?? form.Channels.FirstOrDefault(c =>
                             string.Equals(c.DbcSignalName, stat.SignalName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        ch = form.Channels.FirstOrDefault(c =>
                            c.DbcMessageId == stat.MessageId && c.DbcSignalName == stat.SignalName);
                    }
                    if (ch == null)
                    {
                        // 匹配不到信号(MessageId/SignalName未与DBC配对),占位符填"-"避免花括号残留
                        if (stat.PlaceholderMap != null)
                            foreach (var pm in stat.PlaceholderMap)
                                values[pm.Value] = "-";
                        continue;
                    }
                    
                    // 按时间范围分组计算：相同时间范围的指标一起计算，提高效率
                    var metricsByTimeRange = new Dictionary<string, List<string>>(); // "t0,t1" -> [metrics]
                    foreach (var metric in stat.Metrics)
                    {
                        if (stat.PlaceholderMap == null || !stat.PlaceholderMap.ContainsKey(metric))
                            continue;
                        string placeholderKey = stat.PlaceholderMap[metric];
                        
                        // 检查该占位符是否有独立时间范围(兼容中文逗号/分号)
                        double calcT0 = t0, calcT1 = t1;
                        bool customRange = false;
                        double parsedT0 = 0, parsedT1 = 0;
                        if (stat.TimeRangeMap != null && stat.TimeRangeMap.TryGetValue(placeholderKey, out var tr))
                            customRange = SignalStatsCalculator.TryParseTimeRange(tr, out parsedT0, out parsedT1);
                        if (customRange)
                        {
                            calcT0 = parsedT0;
                            calcT1 = parsedT1;
                            // 钳制到该通道数据范围:部分重叠按交集算,完全错开填"-"
                            var xr = ch.GetXRange();
                            var clampState = SignalStatsCalculator.ClampTimeRange(calcT0, calcT1, xr.Min, xr.Max, out calcT0, out calcT1);
                            if (clampState == SignalStatsCalculator.TimeRangeClampState.NoIntersection)
                            {
                                values[placeholderKey] = "-";
                                continue;
                            }
                        }
                        
                        string timeKey = $"{calcT0},{calcT1}";
                        if (!metricsByTimeRange.ContainsKey(timeKey))
                            metricsByTimeRange[timeKey] = new List<string>();
                        metricsByTimeRange[timeKey].Add(metric);
                    }
                    
                    // 对每个时间范围分别计算
                    foreach (var kv in metricsByTimeRange)
                    {
                        var parts = kv.Key.Split(',');
                        double calcT0 = double.Parse(parts[0]);
                        double calcT1 = double.Parse(parts[1]);
                        
                        // 创建临时stat，只包含该时间范围的metrics
                        var tempStat = new SignalStat
                        {
                            MessageId = stat.MessageId,
                            SignalName = stat.SignalName,
                            Unit = stat.Unit,
                            Metrics = kv.Value,
                            PlaceholderMap = kv.Value.ToDictionary(m => m, m => stat.PlaceholderMap[m])
                        };
                        
                        var pv = SignalStatsCalculator.Calc(ch, calcT0, calcT1, tempStat);
                        if (pv == null || pv.Count == 0)
                        {
                            // 算不出值(时间窗内无数据),同样填"-"
                            foreach (var metric in kv.Value)
                            {
                                if (stat.PlaceholderMap.ContainsKey(metric))
                                    values[stat.PlaceholderMap[metric]] = "-";
                            }
                            continue;
                        }
                        foreach (var pvKv in pv) values[pvKv.Key] = pvKv.Value;
                    }
                }
            }

            // 4) 自动填充固定占位符:工况名→WORK_MODE, 日志文件名→DATA_FILE
            values["WORK_MODE"] = type.Name ?? "";
            var fileNames = new List<string>();
            if (form.LogFilePaths != null)
            {
                foreach (var path in form.LogFilePaths)
                    fileNames.Add(Path.GetFileName(path));
            }
            values["DATA_FILE"] = string.Join("\n", fileNames);

            // 5) 文字模板占位符替换（如果有TextTemplate）
            if (!string.IsNullOrEmpty(type.TextTemplate))
            {
                string filledText = type.TextTemplate;
                foreach (var kv in values)
                    filledText = filledText.Replace("{{" + kv.Key + "}}", kv.Value);
                values["__TEXT_TEMPLATE__"] = filledText;
            }

            // 6) 追加并填充该页
            _builder.AppendPage(type, images, values);
            Bitmap thumb = null;
            if (images != null && images.TryGetValue("chart", out var chartImg) && chartImg != null)
            {
                try { thumb = MakeThumbnail(chartImg, 480, 270); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[ReportAuto] 生成缩略图失败: " + ex.Message); }
            }
            _pages.Add(new ReportPageInfo { Name = type.Name ?? "", TimeStart = t0, TimeEnd = t1, Thumbnail = thumb });
            return _builder.PageCount;
        }

        /// <summary>删除指定页(索引基于Pages顺序),同步PPT文档与页列表</summary>
        public static void DeletePage(int index)
        {
            if (!_started || _builder == null)
                throw new InvalidOperationException("当前无报告内容");
            if (index < 0 || index >= _pages.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _builder.DeletePage(index);
            _pages[index].Thumbnail?.Dispose();
            _pages.RemoveAt(index);
        }

        /// <summary>把第from页移动到第to位(索引基于Pages顺序),同步PPT文档与页列表</summary>
        public static void MovePage(int from, int to)
        {
            if (!_started || _builder == null)
                throw new InvalidOperationException("当前无报告内容");
            if (from < 0 || from >= _pages.Count || to < 0 || to >= _pages.Count)
                throw new ArgumentOutOfRangeException();
            if (from == to) return;
            _builder.MovePage(from, to);
            var item = _pages[from];
            _pages.RemoveAt(from);
            _pages.Insert(to, item);
        }

        /// <summary>保存当前累积报告到指定路径</summary>
        public static void SaveAs(string path)
        {
            if (!_started || _builder == null)
                throw new InvalidOperationException("当前无报告内容,请先添加页");
            _builder.SaveAs(path);
        }
    }
}
