
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace PCAN_Client
{
    public class ChartControl : Control
    {
        private List<ChannelData> _channels;
        private bool _isDragging = false;
        private Point _lastMousePos;
        private bool _isZooming = false;
        private Point _zoomStart;
        private Rectangle _zoomRect;
        private Font _axisFont = new Font("Arial", 9f);
        private Font _nameFont = new Font("Arial", 9f);
        private Pen _gridPen = new Pen(Color.LightGray, 0.5f);
        private Pen _axisPen = new Pen(Color.DarkGray, 2f); // 深灰色，加粗至2px确保HighSpeed模式下清晰可见
        private Pen _xAxisBottomPen = new Pen(Color.Gray, 1.5f); // 加粗网格线
        private Pen _selectedAxisPen = new Pen(Color.Blue, 1f);
        private Brush _textBrush = Brushes.Black;
        private Brush _whiteBrush = new SolidBrush(Color.White);
        private Brush _highlightBrush = new SolidBrush(Color.FromArgb(90, 144, 238, 144)); // 信号列表选中联动:面板背景高亮(淡绿)

        // 渲染缩放因子（默认1.0，截图时按比例放大线宽/字体）
        private float _renderScale = 1.0f;
        public void SetRenderScale(float scale) { _renderScale = Math.Max(scale, 0.1f); }
        private struct PenKey : IEquatable<PenKey>
        {
            public int ColorArgb;
            public float Width;
            public PenKey(Color color, float width) { ColorArgb = color.ToArgb(); Width = width; }
            public bool Equals(PenKey other) => ColorArgb == other.ColorArgb && Width == other.Width;
            public override bool Equals(object obj) => obj is PenKey other && Equals(other);
            public override int GetHashCode() => ColorArgb.GetHashCode() ^ Width.GetHashCode();
        }
        private Dictionary<PenKey, Pen> _penCache = new Dictionary<PenKey, Pen>();
        private Dictionary<PenKey, Pen> _dashPenCache = new Dictionary<PenKey, Pen>();
        private Dictionary<PenKey, Bitmap> _dotSpriteCache = new Dictionary<PenKey, Bitmap>();
        /// <summary>单通道每帧描边像素预算：超出则降采样并关闭抗锯齿（GDI+ 软件描边约 0.1~0.3µs/px）</summary>
        private const int StrokePixelBudget = 15000;
        private Pen _zoomPen = new Pen(Color.Blue, 1) { DashStyle = DashStyle.Dash };
        private Brush _zoomBrush = new SolidBrush(Color.FromArgb(50, Color.Blue));
        private readonly object _lockObj = new object();
        private double _globalXMin = 0;
        private double _globalXMax = 10;
        private bool _autoScroll = false;
        private double _latestDataTime = 0;
        private bool _autoScrollManuallyDisabled = false;
        public event Action<bool> OnAutoScrollChanged;
        public event Action<ChannelData> OnChannelColorChanged; // 通道颜色变化事件
        /// <summary>绘图区点击选中通道(Y轴区域点击)时触发,用于联动左侧信号列表选中</summary>
        public event Action<ChannelData> OnChannelClicked;
        /// <summary>右键点击曲线区域，要求显示该时刻前后各5000条报文<br/>参数：X值（秒）</summary>
        public event Action<double> OnShowMessagesAtTime;
        /// <summary>测量线变化事件（添加、拖动、清除时触发）</summary>
        public event Action OnMeasureLinesChanged;
        private bool _isYAxisDragging = false;
        private ChannelData _selectedChannel = null;
        private bool _isXAxisSelected = false;
        private ToolTip _toolTip = new ToolTip();
        private double? _measureLine1X = null;
        private double? _measureLine2X = null;
        public double? MeasureLineX1 => _measureLine1X;
        public double? MeasureLineX2 => _measureLine2X;
        private bool _isDraggingMeasureLine1 = false;
        private bool _isDraggingMeasureLine2 = false;
        private Font _measureLineFont = new Font("Arial", 9f);
        private ContextMenuStrip _measurementMenu;
        private Timer _measurementMenuTimer;
        private double? _contextMenuMeasureX = null;
        private Point _measurementMenuLocation;

        // 帧率控制：避免过度重绘导致CPU占用过高
        private DateTime _lastPaintTime = DateTime.MinValue;
        private bool _needsRepaint = false; // 是否需要重绘标记
        private System.Windows.Forms.Timer _frameTimer; // 帧率控制定时器

        // Y轴区域左右拖拽调整相关
        private int _leftMarginWidth = 115;
        private const int _graySeparatorAbsX = 35; // 灰色分隔线固定绝对位置
        private const int _colorLineAbsX = 10;      // 颜色竖线固定绝对位置（灰线左侧）
        private bool _isDraggingLeftMargin = false;
        private int _dragStartMarginX;
        private int _dragStartMarginWidth;

        public ChartControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _channels = new List<ChannelData>();
            InitializeMeasurementMenu();
            InitializeMeasurementMenuTimer();

            // 初始化帧率控制定时器（67ms ≈ 15fps，精确匹配目标帧率）
            _frameTimer = new System.Windows.Forms.Timer();
            _frameTimer.Interval = 67; // 15fps对应的间隔
            _frameTimer.Tick += (s, e) =>
            {
                if (_needsRepaint)
                {
                    _needsRepaint = false;
                    Invalidate();
                }
            };
            _frameTimer.Start();
        }

        /// <summary>获取缓存的 Pen（避免每帧创建GDI对象）</summary>
        private Pen GetCachedPen(Color color, float width)
        {
            var key = new PenKey(color, width);
            if (!_penCache.TryGetValue(key, out var pen))
            {
                pen = new Pen(color, width);
                _penCache[key] = pen;
            }
            return pen;
        }

        /// <summary>获取缓存的虚线 Pen（避免每帧 new Pen + DashPattern 数组分配）</summary>
        private Pen GetCachedDashPen(Color color, float width)
        {
            var key = new PenKey(color, width);
            if (!_dashPenCache.TryGetValue(key, out var pen))
            {
                pen = new Pen(color, width) { DashStyle = DashStyle.Dash, DashPattern = new float[] { 5f, 3f } };
                _dashPenCache[key] = pen;
            }
            return pen;
        }

        /// <summary>获取缩放后的字体（缓存避免重复创建）</summary>
        private Font GetScaledFont(Font baseFont)
        {
            if (_renderScale <= 1.01f) return baseFont;
            float size = baseFont.Size * _renderScale;
            // 用缓存避免每次渲染都创建新字体
            var key = new PenKey(Color.Black, size); // 复用PenKey做缓存key
            // 简化处理：每次缩放时创建新字体（截图频率低，可接受）
            return new Font(baseFont.FontFamily, size, baseFont.Style);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _frameTimer?.Stop();
                _frameTimer?.Dispose();
                _axisFont?.Dispose();
                _nameFont?.Dispose();
                _gridPen?.Dispose();
                _axisPen?.Dispose();
                _xAxisBottomPen?.Dispose();
                _selectedAxisPen?.Dispose();
                _whiteBrush?.Dispose();
                _zoomPen?.Dispose();
                _zoomBrush?.Dispose();
                _measureLineFont?.Dispose();
                _measurementMenu?.Dispose();
                _measurementMenuTimer?.Dispose();
                foreach (var pen in _penCache.Values) pen.Dispose();
                foreach (var pen in _dashPenCache.Values) pen.Dispose();
                foreach (var sprite in _dotSpriteCache.Values) sprite.Dispose();
                _penCache.Clear();
                _dashPenCache.Clear();
                _dotSpriteCache.Clear();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 智能重绘：通过帧率控制定时器调度，避免过度重绘
        /// 用户交互操作（拖拽、缩放等）直接重绘，数据更新走帧率控制
        /// </summary>
        public void SmartInvalidate(bool immediate = false)
        {
            if (immediate || _isDragging || _isZooming || _isYAxisDragging || _isDraggingMeasureLine1 || _isDraggingMeasureLine2)
            {
                // 交互操作时立即重绘，保证流畅性
                _lastPaintTime = DateTime.Now;
                Invalidate();
            }
            else
            {
                // 数据更新时通过帧率控制，降低CPU占用
                _needsRepaint = true;
            }
        }

        /// <summary>
        /// 暂停帧率定时器（交互操作期间避免与直接Invalidate竞争）
        /// </summary>
        private void PauseFrameTimer()
        {
            if (_frameTimer != null && _frameTimer.Enabled)
            {
                _frameTimer.Stop();
            }
        }

        /// <summary>
        /// 恢复帧率定时器
        /// </summary>
        private void ResumeFrameTimer()
        {
            // 只在没有进行任何交互操作时才恢复
            if (_frameTimer != null && !_frameTimer.Enabled &&
                !_isDragging && !_isZooming && !_isYAxisDragging &&
                !_isDraggingMeasureLine1 && !_isDraggingMeasureLine2)
            {
                _frameTimer.Start();
                _needsRepaint = true; // 恢复后触发一次刷新
            }
        }

        public void SetChannels(List<ChannelData> channels)
        {
            lock (_lockObj)
            {
                _channels = channels;
            }
            OnMeasureLinesChanged?.Invoke();
            SmartInvalidate(true); // 设置通道时立即刷新
        }

        public void SetGlobalXRange(double min, double max)
        {
            _globalXMin = min;
            _globalXMax = max;
            SmartInvalidate(true); // X轴范围变化时立即刷新
        }

        public double GetCurrentXMin()
        {
            return _globalXMin;
        }

        public double GetCurrentXMax()
        {
            return _globalXMax;
        }

        public double GetLatestDataTime()
        {
            return _latestDataTime;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            // 始终开启抗锯齿，确保曲线平滑清晰
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            DrawBackground(g);
            DrawPanels(g);
            DrawMeasureLines(g);
            DrawZoomRectangle(g);
        }

        /// <summary>
        /// 离屏渲染到指定 Graphics（用于报告截图）。
        /// 调用前可临时放大 this.Size 以获得高清输出；复用 OnPaint 的全部绘制逻辑。
        /// </summary>
        public void RenderTo(Graphics g)
        {
            if (g == null) return;
            // 始终开启抗锯齿，确保曲线平滑清晰
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            DrawBackground(g);
            DrawPanels(g);
            DrawMeasureLines(g);
            DrawZoomRectangle(g);
        }

        private void DrawBackground(Graphics g)
        {
            g.FillRectangle(Brushes.White, ClientRectangle);
        }

        private void DrawPanels(Graphics g)
        {
            lock (_lockObj)
            {
                if (_channels.Count == 0) return;

                int visibleChannels = _channels.FindAll(c => c.Visible).Count;
                if (visibleChannels == 0) return;

                int axisLabelHeight = 20;
                int panelContentHeight = (Height - 20 - axisLabelHeight) / visibleChannels;
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                int paddingTop = 5;
                int panelIndex = 0;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    int panelY = paddingTop + panelIndex * panelContentHeight;
                    bool isLastPanel = panelIndex == visibleChannels - 1;
                    int rectHeight = isLastPanel ? panelContentHeight - axisLabelHeight : panelContentHeight;

                    Rectangle panelRect = new Rectangle(
                        paddingLeft, 
                        panelY, 
                        Width - paddingLeft - paddingRight, 
                        rectHeight
                    );

                    DrawPanelBackground(g, panelRect, channel);
                    DrawPanelAxes(g, panelRect, channel, isLastPanel);
                    DrawPanelCurve(g, panelRect, channel);
                    DrawPanelChannelName(g, panelRect, channel);

                    panelIndex++;
                }
            }
        }

        private void DrawPanelBackground(Graphics g, Rectangle rect, ChannelData channel)
        {
            if (channel.IsHighlighted)
                g.FillRectangle(_highlightBrush, rect);
            else
                g.FillRectangle(_whiteBrush, rect);
        }

        private void DrawPanelGrid(Graphics g, Rectangle rect, ChannelData channel)
        {
            RangeResult xRange = channel.GetXRange();
            RangeResult yRange = _autoScroll ? channel.GetYRangeInXRange(_globalXMin, _globalXMax) : channel.GetYRange();
            
            double xMin = _globalXMin;
            double xMax = _globalXMax;
            double yMin = yRange.Min;
            double yMax = yRange.Max;
            
            if (yMin == yMax)
            {
                yMin = -1;
                yMax = 1;
            }

            int xGridCount = 10;
            int yGridCount = 6;

            for (int i = 0; i <= xGridCount; i++)
            {
                double xValue = xMin + (xMax - xMin) * i / xGridCount;
                int x = ValueToScreenX(xValue, rect);
                g.DrawLine(_gridPen, x, rect.Top, x, rect.Bottom);
            }

            for (int i = 0; i <= yGridCount; i++)
            {
                double yValue = yMin + (yMax - yMin) * i / yGridCount;
                int y = ValueToScreenY(yValue, yMin, yMax, rect);
                g.DrawLine(_gridPen, rect.Left, y, rect.Right, y);
            }
        }

        private void DrawPanelAxes(Graphics g, Rectangle rect, ChannelData channel, bool isLastPanel)
        {
            RangeResult xRange = channel.GetXRange();
            RangeResult yRange = _autoScroll ? channel.GetYRangeInXRange(_globalXMin, _globalXMax) : channel.GetYRange();
            
            double xMin = _globalXMin;
            double xMax = _globalXMax;
            double yMin = yRange.Min;
            double yMax = yRange.Max;
            
            if (yMin == yMax)
            {
                yMin = -1;
                yMax = 1;
            }

            Pen xAxisPen = _isXAxisSelected ? _selectedAxisPen : _axisPen;
            // X轴实线：非最后面板画在rect.Bottom-1避免HighSpeed模式下边界裁剪
            int xAxisY = isLastPanel ? rect.Bottom : rect.Bottom - 1;
            g.DrawLine(xAxisPen, rect.Left, xAxisY, rect.Right, xAxisY);
            Pen yAxisPen = channel.IsYAxisSelected ? _selectedAxisPen : _axisPen;
            g.DrawLine(yAxisPen, rect.Left, rect.Top, rect.Left, rect.Bottom);

            if (isLastPanel)
            {
                int xLabelCount = 5;
                double range = xMax - xMin;
                // 智能选择小数位数：范围越小精度越高
                string format = range < 1 ? "F3" : range < 10 ? "F2" : range < 100 ? "F1" : "F0";
                Font scaledAxisFont = GetScaledFont(_axisFont);
                for (int i = 0; i <= xLabelCount; i++)
                {
                    double xValue = xMin + range * i / xLabelCount;
                    int x = ValueToScreenX(xValue, rect);
                    string label = xValue.ToString(format) + "s";
                    SizeF labelSize = g.MeasureString(label, scaledAxisFont);
                    // X轴刻度线
                    g.DrawLine(_axisPen, x, rect.Bottom, x, rect.Bottom + 4);
                    g.DrawString(label, scaledAxisFont, _textBrush, x - labelSize.Width / 2, rect.Bottom + 5);
                }
                if (scaledAxisFont != _axisFont) scaledAxisFont.Dispose();
            }

            // Y轴标签：有枚举定义时显示枚举描述，否则显示数值
            // 限制标签不越过灰色分隔线（固定 _graySeparatorAbsX）
            Region prevClip = g.Clip;
            g.SetClip(new Rectangle(_graySeparatorAbsX, rect.Top - 10, rect.Left - _graySeparatorAbsX + 10, rect.Height + 20));
            Font scaledFont = GetScaledFont(_axisFont);
            float minLabelGap = scaledFont.Height + 6; // 相邻标签最小垂直间距，避免重叠
            if (channel.EnumDefinitions != null && channel.EnumDefinitions.Count > 0)
            {
                // 枚举类型：在每个枚举值位置显示描述
                // 面板过矮时跳过与已绘制标签重叠的枚举值，避免标签堆叠
                float lastEnumY = float.NaN;
                foreach (var kvp in channel.EnumDefinitions)
                {
                    double enumValue = kvp.Key;
                    // 只显示在Y轴范围内的枚举值
                    if (enumValue < yMin || enumValue > yMax) continue;

                    int y = ValueToScreenY(enumValue, yMin, yMax, rect);
                    // 与上一个已绘制标签过近则跳过（枚举值密集时只保留间隔足够的）
                    if (!float.IsNaN(lastEnumY) && Math.Abs(y - lastEnumY) < minLabelGap) continue;

                    string label = kvp.Value;
                    SizeF labelSize = g.MeasureString(label, scaledFont);

                    float labelY = y - labelSize.Height / 2;
                    if (labelY < rect.Top) labelY = rect.Top;
                    if (labelY + labelSize.Height > rect.Bottom - 1) labelY = rect.Bottom - labelSize.Height - 1;

                    g.DrawLine(_axisPen, rect.Left - 4, y, rect.Left, y);
                    g.DrawString(label, scaledFont, _textBrush, rect.Left - labelSize.Width - 6, labelY);
                    lastEnumY = y;
                }
            }
            else
            {
                // 根据面板高度自适应刻度标签数量：面板越矮标签越少，避免信号多时标签重叠
                // 阈值按标签字体高度+间距估算：常规4个 / 较矮3个 / 很矮2个 / 极矮1个
                int yLabelCount;
                if (rect.Height >= 90) yLabelCount = 3;
                else if (rect.Height >= 55) yLabelCount = 2;
                else if (rect.Height >= 30) yLabelCount = 1;
                else yLabelCount = 0;

                for (int i = 0; i <= yLabelCount; i++)
                {
                    // yLabelCount==0 时只画中间一个标签（面板极矮）
                    double yValue = yLabelCount == 0
                        ? (yMin + yMax) / 2
                        : yMin + (yMax - yMin) * i / yLabelCount;
                    int y = ValueToScreenY(yValue, yMin, yMax, rect);
                    double step = yLabelCount > 0 ? (yMax - yMin) / yLabelCount : (yMax - yMin);
                    int decimals = step >= 1 ? 0 : (step >= 0.1 ? 1 : (step >= 0.01 ? 2 : 3));
                    string label = Math.Round(yValue, decimals).ToString("F" + decimals);
                    SizeF labelSize = g.MeasureString(label, scaledFont);

                    // 标签限制在面板顶部和底部直线之间，避免与相邻面板重叠
                    float labelY = y - labelSize.Height / 2;
                    if (labelY < rect.Top) labelY = rect.Top;
                    if (labelY + labelSize.Height > rect.Bottom - 1) labelY = rect.Bottom - labelSize.Height - 1;

                    // Y轴刻度线
                    g.DrawLine(_axisPen, rect.Left - 4, y, rect.Left, y);
                    g.DrawString(label, scaledFont, _textBrush, rect.Left - labelSize.Width - 6, labelY);
                }
            }
            g.Clip = prevClip;
            prevClip?.Dispose();

            // 颜色+曲线名与Y轴数值之间的灰色分隔线（固定位置）
            using (Pen graySepPen = new Pen(Color.FromArgb(200, 200, 200), 1))
            {
                g.DrawLine(graySepPen, _graySeparatorAbsX, rect.Top, _graySeparatorAbsX, rect.Bottom);
            }
        }

        private void DrawPanelCurve(Graphics g, Rectangle rect, ChannelData channel)
        {
            // 使用索引范围避免数据复制（性能优化关键点）
            var indexRange = channel.GetPointIndexRange(_globalXMin, _globalXMax);
            int startIdx = indexRange.Item1;
            int endIdx = indexRange.Item2;
            int totalPoints = indexRange.Item3;

            if (totalPoints < 2) return;

            RangeResult yRange = _autoScroll ? channel.GetYRangeInXRange(_globalXMin, _globalXMax) : channel.GetYRange();
            if (yRange == null) return;
            double yMin = yRange.Min;
            double yMax = yRange.Max;
            
            if (yMin == yMax)
            {
                yMin = -1;
                yMax = 1;
            }

            int plotWidth = rect.Width > 0 ? rect.Width : 1;

            // 抽稀列数：面板越矮可分辨的横向细节越少，无需画出全部采样点
            int maxColumns = Math.Min(plotWidth, Math.Max(64, rect.Height * 8));

            // 一次性批量获取索引范围内的点（已按屏幕列抽稀），避免每帧拷贝全部可见点
            List<ChannelPoint> pointsInRange = channel.GetPointsInRangeDecimated(startIdx, endIdx, maxColumns);
            int pointsCount = pointsInRange.Count;

            // GDI+ 抗锯齿对陡峭/锯齿折线的开销是平滑曲线的数十倍（实测 1000 段：平滑约 3ms，满幅锯齿约 250ms）。
            // 缩小视图后相邻采样点纵向跳变可达整幅面板高度，据此估算描边像素长度：
            // 超预算时进一步降低横向采样率并关闭抗锯齿，保证单帧耗时与数据量、缩放级别无关。
            double strokePx = EstimateStrokePixels(pointsInRange, pointsCount, yMin, yMax, rect.Height);
            bool antiAlias = strokePx <= StrokePixelBudget;
            if (!antiAlias)
            {
                // 超预算时在已抽稀的结果上再降采样（O(点数)，不再扫全量数据）
                int reducedColumns = (int)Math.Max(32, maxColumns * StrokePixelBudget / strokePx);
                pointsInRange = SubSample(pointsInRange, reducedColumns);
                pointsCount = pointsInRange.Count;
            }

            // 线宽跟随通道设置(默认2px),并按渲染缩放等比缩放,保证报告截图/高DPI下一致
            float lineWidth = Math.Max(1, channel.LineWidth) * _renderScale;
            SmoothingMode prevSmoothing = g.SmoothingMode;
            if (!antiAlias) g.SmoothingMode = SmoothingMode.None;
            try
            {
                using (GraphicsPath solidPath = new GraphicsPath())
                using (GraphicsPath dashPath = new GraphicsPath())
                {
                    Pen solidPen = GetCachedPen(channel.Color, lineWidth);
                    Pen dashPen = GetCachedDashPen(channel.Color, lineWidth);

                    bool needStartSolidFigure = true;
                    bool needStartDashFigure = true;

                    for (int i = 0; i < pointsCount - 1; i++)
                    {
                        ChannelPoint currentPoint = pointsInRange[i];
                        ChannelPoint nextPoint = pointsInRange[i + 1];
                        if (currentPoint == null || nextPoint == null) continue;

                        AddLineToPaths(rect, solidPath, dashPath, ref needStartSolidFigure, ref needStartDashFigure,
                                       currentPoint, nextPoint, yMin, yMax);
                    }

                    if (solidPath.PointCount > 1)
                    {
                        g.DrawPath(solidPen, solidPath);
                    }
                    if (dashPath.PointCount > 1)
                    {
                        try
                        {
                            g.DrawPath(dashPen, dashPath);
                        }
                        catch (OutOfMemoryException)
                        {
                            // GDI+ 路径包含过多子图元时跳过虚线绘制，不影响后续渲染
                            System.Diagnostics.Debug.WriteLine("虚线路径太复杂，跳过绘制");
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = prevSmoothing;
            }

            // 数据点绘制：点密度不超过每像素1个时显示（避免糊成一片）
            // 面板过矮时不画点：纵向只有十几像素，点只会糊成一片且是单帧最大开销之一
            bool showDots = (totalPoints <= plotWidth || totalPoints <= 100) && rect.Height >= 24;
            if (showDots)
            {
                // 数据点直径跟随通道设置(默认5px)，并按渲染缩放等比缩放(报告截图/高DPI一致)
                float dotSizePx = Math.Max(1, channel.DotSize) * _renderScale;
                int dotDiameter = Math.Max(1, (int)Math.Round(dotSizePx));
                int dotRadius = dotDiameter / 2;

                // 按屏幕X间距显示圆点:相邻点间距不小于直径 → 不重叠(大小一致)、显示均匀。
                // 间距同时受点数上限约束（≤200 点/通道）：点绘制是 GDI+ 最贵的部分之一。
                int dotSpacing = Math.Max(dotDiameter, plotWidth / 200);

                // 圆点用预渲染贴图一次性 blit（每点 2 次 GDI+ 绘制 → 1 次 DrawImageUnscaled）
                Bitmap dotSprite = GetDotSprite(channel.Color, dotDiameter);

                // 跳过IsLost虚点(Y=旧值,调度脉冲而非真实采样):避免其与真实点(报文周期)混排导致间距忽密忽疏
                // 初始值取负间距,保证首点必然通过(screenX - int.MinValue 会溢出为负导致全部点被跳过)
                int lastDotScreenX = -dotSpacing;
                for (int i = 0; i < pointsCount; i++)
                {
                    ChannelPoint point = pointsInRange[i];
                    if (point == null || point.IsLost) continue;
                    if (point.Y < yMin || point.Y > yMax) continue;
                    int screenX = ValueToScreenX(point.X, rect);
                    if (screenX - lastDotScreenX < dotSpacing) continue;
                    lastDotScreenX = screenX;
                    int screenY = ValueToScreenY(point.Y, yMin, yMax, rect);
                    g.DrawImageUnscaled(dotSprite, screenX - dotRadius - 1, screenY - dotRadius - 1);
                }
            }
        }

        /// <summary>
        /// 把已抽稀的点列表按均匀步长再降采样到目标点数（首尾保留，O(点数)）。
        /// 仅在曲线描边像素超预算（缩小视图、锯齿折线）时使用。
        /// </summary>
        private static List<ChannelPoint> SubSample(List<ChannelPoint> points, int targetCount)
        {
            int count = points.Count;
            if (count <= targetCount || targetCount < 2) return points;

            var result = new List<ChannelPoint>(targetCount);
            double step = (double)(count - 1) / (targetCount - 1);
            int lastIndex = -1;
            for (int i = 0; i < targetCount; i++)
            {
                int index = (int)Math.Round(i * step);
                if (index >= count) index = count - 1;
                if (index == lastIndex) continue;
                lastIndex = index;
                result.Add(points[index]);
            }
            if (!ReferenceEquals(result[result.Count - 1], points[count - 1])) result.Add(points[count - 1]);
            return result;
        }

        /// <summary>
        /// 估算折线在屏幕上的描边像素总长（Σ|Δy| 按 Y 值域映射到面板高度）。
        /// 用于判断曲线是否"陡峭/锯齿"——GDI+ 抗锯齿对这种折线的开销远高于平滑曲线。
        /// </summary>
        private static double EstimateStrokePixels(List<ChannelPoint> points, int count, double yMin, double yMax, int panelHeight)
        {
            double range = yMax - yMin;
            if (range <= 0 || count < 2) return 0;
            double sumDy = 0;
            for (int i = 1; i < count; i++)
            {
                ChannelPoint a = points[i - 1];
                ChannelPoint b = points[i];
                if (a == null || b == null) continue;
                sumDy += Math.Abs(b.Y - a.Y);
            }
            return sumDy / range * panelHeight;
        }

        /// <summary>
        /// 获取预渲染的数据点贴图（圆点 + 半透明深色描边）。
        /// 浅色曲线(黄/白等)在白底上也清晰可辨；按颜色/直径缓存，避免每帧逐点 FillEllipse+DrawEllipse。
        /// </summary>
        private Bitmap GetDotSprite(Color color, int diameter)
        {
            var key = new PenKey(color, diameter);
            if (_dotSpriteCache.TryGetValue(key, out var sprite)) return sprite;

            // 四周各留 1px 给描边抗锯齿
            sprite = new Bitmap(diameter + 2, diameter + 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sprite))
            using (var brush = new SolidBrush(color))
            using (var outline = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(brush, 1, 1, diameter, diameter);
                g.DrawEllipse(outline, 1, 1, diameter, diameter);
            }
            _dotSpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 将线段添加到实线/虚线路径（提取公共逻辑，减少重复代码）
        /// </summary>
        private void AddLineToPaths(Rectangle rect, GraphicsPath solidPath, GraphicsPath dashPath,
                                   ref bool needStartSolidFigure, ref bool needStartDashFigure,
                                   ChannelPoint currentPoint, ChannelPoint nextPoint, double yMin, double yMax)
        {
            int currentX = ValueToScreenX(currentPoint.X, rect);
            double currentY = currentPoint.Y;
            if (currentY < yMin) currentY = yMin;
            if (currentY > yMax) currentY = yMax;
            Point currentScreenPoint = new Point(currentX, ValueToScreenY(currentY, yMin, yMax, rect));

            int nextX = ValueToScreenX(nextPoint.X, rect);
            double nextY = nextPoint.Y;
            if (nextY < yMin) nextY = yMin;
            if (nextY > yMax) nextY = yMax;
            Point nextScreenPoint = new Point(nextX, ValueToScreenY(nextY, yMin, yMax, rect));

            if (currentPoint.IsLost && nextPoint.IsLost)
            {
                if (needStartDashFigure)
                {
                    dashPath.StartFigure();
                    needStartDashFigure = false;
                }
                dashPath.AddLine(currentScreenPoint, nextScreenPoint);
                needStartSolidFigure = true;
            }
            else
            {
                if (needStartSolidFigure)
                {
                    solidPath.StartFigure();
                    needStartSolidFigure = false;
                }
                solidPath.AddLine(currentScreenPoint, nextScreenPoint);
                needStartDashFigure = true;
            }
        }

        private void DrawPanelChannelName(Graphics g, Rectangle rect, ChannelData channel)
        {
            string nameLine = channel.Name;
            if (string.IsNullOrEmpty(nameLine)) return;

            // 颜色线固定绝对位置，不随Y轴调节移动
            int colorLineX = _colorLineAbsX;
            // 绘制颜色标识线
            Pen legendPen = GetCachedPen(channel.Color, 2);
            g.DrawLine(legendPen, colorLineX, rect.Top + 5, colorLineX, rect.Bottom - 5);

            // 逆时针旋转90°绘制曲线名（从下往上读），颜色线右侧
            // 裁剪到面板范围内（上下各留2px间隔），避免与相邻通道曲线名重叠
            Region prevNameClip = g.Clip;
            g.SetClip(new Rectangle(colorLineX - 2, rect.Top + 2, _graySeparatorAbsX - colorLineX + 10, rect.Height - 4));
            int textX = colorLineX + 4;
            g.TranslateTransform(textX, rect.Bottom - 5);
            g.RotateTransform(-90);
            g.DrawString(nameLine, _nameFont, _textBrush, 0, 0);
            g.ResetTransform();
            g.Clip = prevNameClip;
            prevNameClip?.Dispose();
        }

        private void DrawZoomRectangle(Graphics g)
        {
            if (_zoomRect.Width > 0 && _zoomRect.Height > 0)
            {
                g.DrawRectangle(_zoomPen, _zoomRect);
                g.FillRectangle(_zoomBrush, _zoomRect);
            }
        }

        private int ValueToScreenX(double x, Rectangle plotArea)
        {
            double xRange = _globalXMax - _globalXMin;
            if (Math.Abs(xRange) < 0.0001)
            {
                return plotArea.Left;
            }
            return plotArea.Left + (int)((x - _globalXMin) / xRange * plotArea.Width);
        }

        private int ValueToScreenY(double y, double yMin, double yMax, Rectangle plotArea)
        {
            double yRange = yMax - yMin;
            if (Math.Abs(yRange) < 0.0001)
            {
                return plotArea.Bottom;
            }
            return plotArea.Bottom - (int)((y - yMin) / yRange * plotArea.Height);
        }

        private double ScreenToValueX(int screenX, Rectangle plotArea)
        {
            if (plotArea.Width == 0)
            {
                return _globalXMin;
            }
            return _globalXMin + (screenX - plotArea.Left) * (_globalXMax - _globalXMin) / plotArea.Width;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                // 右键点击通道名称/颜色线区域，弹出颜色选择对话框
                ChannelData clickedChannel = GetChannelAtNameArea(e.Location);
                if (clickedChannel != null)
                {
                    using (ColorDialog colorDialog = new ColorDialog())
                    {
                        colorDialog.Color = clickedChannel.Color;
                        colorDialog.AllowFullOpen = true;
                        colorDialog.FullOpen = true;
                        if (colorDialog.ShowDialog(this.FindForm()) == DialogResult.OK)
                        {
                            clickedChannel.Color = colorDialog.Color;
                            OnChannelColorChanged?.Invoke(clickedChannel);
                            Invalidate();
                        }
                    }
                }
                else
                {
                    // 右键在测量交互区域弹出测量菜单
                    Rectangle interactionArea = GetMeasurementInteractionArea();
                    if (interactionArea.Contains(e.Location))
                    {
                        _contextMenuMeasureX = ScreenToValueX(e.X, interactionArea);
                        _measurementMenuLocation = e.Location;
                        _measurementMenuTimer?.Stop();
                        _measurementMenuTimer?.Start();
                    }
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);

                // 检测Y轴区域与曲线区域分界处拖拽（Y轴标签区的右边缘，调节灰线与Y轴标签的间隔）
                if (e.X >= _leftMarginWidth - 15 && e.X <= _leftMarginWidth + 5 && e.Y < Height - 35)
                {
                    _isDraggingLeftMargin = true;
                    _dragStartMarginX = e.X;
                    _dragStartMarginWidth = _leftMarginWidth;
                    return;
                }

                if (_measureLine1X.HasValue)
                {
                    int screenX1 = ValueToScreenX(_measureLine1X.Value, plotArea);
                    if (Math.Abs(e.X - screenX1) < 10)
                    {
                        _isDraggingMeasureLine1 = true;
                        PauseFrameTimer();
                        _isXAxisSelected = false;
                        lock (_lockObj)
                        {
                            foreach (var channel in _channels)
                            {
                                channel.IsYAxisSelected = false;
                            }
                        }
                        _selectedChannel = null;
                        Invalidate();
                        return;
                    }
                }

                if (_measureLine2X.HasValue)
                {
                    int screenX2 = ValueToScreenX(_measureLine2X.Value, plotArea);
                    if (Math.Abs(e.X - screenX2) < 10)
                    {
                        _isDraggingMeasureLine2 = true;
                        PauseFrameTimer();
                        _isXAxisSelected = false;
                        lock (_lockObj)
                        {
                            foreach (var channel in _channels)
                            {
                                channel.IsYAxisSelected = false;
                            }
                        }
                        _selectedChannel = null;
                        Invalidate();
                        return;
                    }
                }

                // Ctrl+左键:优先进入框选缩放(不受X轴选中状态/点击位置影响)
                if (ModifierKeys == Keys.Control)
                {
                    _isZooming = true;
                    PauseFrameTimer();
                    _zoomStart = e.Location;
                    _zoomRect = new Rectangle();
                    return;
                }

                ChannelData clickedChannel = GetChannelAtPosition(e.Location);
                if (clickedChannel != null)
                {
                    lock (_lockObj)
                    {
                        foreach (var channel in _channels)
                        {
                            channel.IsYAxisSelected = false;
                        }
                        _isXAxisSelected = false;
                        clickedChannel.IsYAxisSelected = true;
                        _selectedChannel = clickedChannel;
                        _isYAxisDragging = true;
                        PauseFrameTimer();
                        _lastMousePos = e.Location;
                    }
                    Invalidate();
                    OnChannelClicked?.Invoke(clickedChannel);
                }
                else if (_isXAxisSelected)
                {
                    _isDragging = true;
                    PauseFrameTimer();
                    _lastMousePos = e.Location;
                }
                else
                {
                    _isDragging = true;
                    PauseFrameTimer();
                    _lastMousePos = e.Location;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isDraggingMeasureLine1 && _measureLine1X.HasValue)
            {
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
                _measureLine1X = ScreenToValueX(e.X, plotArea);
                Invalidate();
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingMeasureLine2 && _measureLine2X.HasValue)
            {
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
                _measureLine2X = ScreenToValueX(e.X, plotArea);
                Invalidate();
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingLeftMargin)
            {
                _leftMarginWidth = Math.Max(_graySeparatorAbsX + 40, Math.Min(300, _dragStartMarginWidth + (e.X - _dragStartMarginX)));
                Invalidate();
                return;
            }

            if (_isYAxisDragging && _selectedChannel != null)
            {
                int deltaY = e.Y - _lastMousePos.Y;
                RangeResult yRange = _selectedChannel.GetYRange();
                double range = yRange.Max - yRange.Min;
                double yDelta = deltaY * range / 200;
                _selectedChannel.YMin += yDelta;
                _selectedChannel.YMax += yDelta;
                _lastMousePos = e.Location;
                Invalidate();
            }
            else if (_isDragging)
            {
                int deltaX = e.X - _lastMousePos.X;
                double range = _globalXMax - _globalXMin;
                double xDelta = deltaX * range / (Width - 160);
                _globalXMin -= xDelta;
                _globalXMax -= xDelta;
                _lastMousePos = e.Location;

                // 拖动时关闭自动滑动
                if (_autoScroll)
                {
                    _autoScroll = false;
                    OnAutoScrollChanged?.Invoke(false);
                }
                _autoScrollManuallyDisabled = true;  // 拖动过程中禁止自动恢复

                Invalidate();
            }
            else if (_isZooming)
            {
                _zoomRect = new Rectangle(
                    Math.Min(_zoomStart.X, e.X),
                    Math.Min(_zoomStart.Y, e.Y),
                    Math.Abs(e.X - _zoomStart.X),
                    Math.Abs(e.Y - _zoomStart.Y));
                Invalidate();
            }
            else
            {
                // 检查是否在左边缘拖拽区域，调整鼠标光标
                if (e.X >= _leftMarginWidth - 15 && e.X <= _leftMarginWidth + 5 && e.Y < Height - 35)
                {
                    Cursor = Cursors.VSplit;
                }
                else
                {
                    Cursor = Cursors.Default;
                }
                ShowDataPointTooltip(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isDraggingLeftMargin)
            {
                _isDraggingLeftMargin = false;
                Invalidate();
                return;
            }

            if (_isDraggingMeasureLine1)
            {
                _isDraggingMeasureLine1 = false;
                ResumeFrameTimer();
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingMeasureLine2)
            {
                _isDraggingMeasureLine2 = false;
                ResumeFrameTimer();
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isYAxisDragging)
            {
                _isYAxisDragging = false;
                _selectedChannel = null;
                ResumeFrameTimer();
            }
            else if (_isZooming)
            {
                if (_zoomRect.Width > 10 && _zoomRect.Height > 10)
                {
                    int paddingLeft = _leftMarginWidth;
                    int paddingRight = 20;
                    Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);

                    double newXMin = ScreenToValueX(_zoomRect.Left, plotArea);
                    double newXMax = ScreenToValueX(_zoomRect.Right, plotArea);

                    _globalXMin = Math.Min(newXMin, newXMax);
                    _globalXMax = Math.Max(newXMin, newXMax);
                }
                _isZooming = false;
                _zoomRect = new Rectangle();
                _autoScrollManuallyDisabled = true;  // 缩放后禁止自动恢复
                Invalidate();
                ResumeFrameTimer();
            }

            _isDragging = false;
            _autoScrollManuallyDisabled = true;  // 拖动后禁止自动恢复，防止视图回跳
            ResumeFrameTimer();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);

            MouseEventArgs mouseArgs = e as MouseEventArgs;
            if (mouseArgs == null) return;

            if (mouseArgs.Button == MouseButtons.Left)
            {
                ChannelData clickedChannel = GetChannelAtPosition(mouseArgs.Location);
                if (clickedChannel != null)
                {
                    clickedChannel.ResetYRange();
                    clickedChannel.IsYAxisSelected = false;
                    _selectedChannel = null;
                    Invalidate();
                }
            }
            else if (mouseArgs.Button == MouseButtons.Right)
            {
                _measurementMenuTimer?.Stop();

                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);

                if (_measureLine1X.HasValue)
                {
                    int screenX1 = ValueToScreenX(_measureLine1X.Value, plotArea);
                    if (Math.Abs(mouseArgs.X - screenX1) < 10 && plotArea.Contains(mouseArgs.Location))
                    {
                        _measureLine1X = null;
                        Invalidate();
                        OnMeasureLinesChanged?.Invoke();
                        return;
                    }
                }

                if (_measureLine2X.HasValue)
                {
                    int screenX2 = ValueToScreenX(_measureLine2X.Value, plotArea);
                    if (Math.Abs(mouseArgs.X - screenX2) < 10 && plotArea.Contains(mouseArgs.Location))
                    {
                        _measureLine2X = null;
                        Invalidate();
                        OnMeasureLinesChanged?.Invoke();
                        return;
                    }
                }

                if (plotArea.Contains(mouseArgs.Location))
                {
                    double xValue = ScreenToValueX(mouseArgs.X, plotArea);
                    AddMeasureLine(xValue);
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button == MouseButtons.Left)
            {
                // 排除通道区域点击
                if (GetChannelAtPosition(e.Location) != null) return;
                if (GetChannelAtNameArea(e.Location) != null) return;

                // 判断是否在绘图区域
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
                if (plotArea.Contains(e.Location))
                {
                    double xValue = ScreenToValueX(e.X, plotArea);
                    OnShowMessagesAtTime?.Invoke(xValue);
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            ChannelData selectedChannel = GetSelectedChannel();
            if (selectedChannel != null)
            {
                double zoomFactor = e.Delta > 0 ? 0.9 : 1.1;
                RangeResult yRange = selectedChannel.GetYRange();
                double range = yRange.Max - yRange.Min;
                double newRange = range * zoomFactor;
                double center = (yRange.Max + yRange.Min) / 2;
                
                selectedChannel.YMin = center - newRange / 2;
                selectedChannel.YMax = center + newRange / 2;
                Invalidate();
            }
            else
            {
                double zoomFactor = e.Delta > 0 ? 0.9 : 1.1;
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
                double centerX = ScreenToValueX(e.X, plotArea);

                double range = _globalXMax - _globalXMin;
                double newRange = range * zoomFactor;
                double offset = (centerX - _globalXMin) / range;

                _globalXMin = centerX - offset * newRange;
                _globalXMax = _globalXMin + newRange;

                // 自动滑动开启时:缩放后视图右边缘贴齐最新数据(保留缩放后的范围宽度)。
                // 既保持自动滑动语义不退出,又避免下一帧AutoScrollToLatest因右边缘落后于最新数据而把视图拉回(缩放回弹)
                if (_autoScroll && _latestDataTime > _globalXMax)
                {
                    _globalXMax = _latestDataTime;
                    _globalXMin = _globalXMax - newRange;
                }

                Invalidate();
            }
        }

        public void ResetView()
        {
            _globalXMin = 0;
            _globalXMax = 10;
            _autoScroll = false;
            _isXAxisSelected = false;
            _measureLine1X = null;
            _measureLine2X = null;
            lock (_lockObj)
            {
                foreach (var channel in _channels)
                {
                    channel.ResetYRange();
                    channel.IsYAxisSelected = false;
                }
            }
            OnMeasureLinesChanged?.Invoke();
            Invalidate();
        }

        public void SetAutoScroll(bool enabled)
        {
            _autoScroll = enabled;
            _autoScrollManuallyDisabled = !enabled;
            OnAutoScrollChanged?.Invoke(enabled);
        }

        public bool IsAutoScrollEnabled()
        {
            return _autoScroll;
        }

        public void AutoScrollToLatest(double currentTime)
        {
            _latestDataTime = currentTime;

            if (!_autoScroll)
            {
                // 手动关闭时不自动恢复
                if (_autoScrollManuallyDisabled) return;

                // 如果视图右边界已到达最新数据附近，重新开启自动滑动
                double range = _globalXMax - _globalXMin;
                if (_globalXMax >= currentTime - range * 0.05)
                {
                    _autoScroll = true;
                    OnAutoScrollChanged?.Invoke(true);
                }
                else
                {
                    return;
                }
            }

            // 只有当新数据超出当前视图右边界时才滑动，否则保持视图不变（追加显示）
            // 加入最小滚动阈值：超出可视范围的 3% 才滑动，避免高缩放时轻微波动导致视图抖动
            double scrollRange = _globalXMax - _globalXMin;
            // 当范围为0（如流式模式初始 X 范围 [0,0]）时，使用默认最小范围
            if (scrollRange < 1)
                scrollRange = 10;
            double scrollThreshold = Math.Max(scrollRange * 0.03, 0.01); // 至少 3% 或 0.01s
            if (currentTime > _globalXMax + scrollThreshold)
            {
                _globalXMax = currentTime;
                _globalXMin = currentTime - scrollRange;
            }
            // 不在此处直接 Invalidate()，由调用方通过 SmartInvalidate 统一走帧率控制
            _needsRepaint = true;
        }

        public void AutoFitView()
        {
            lock (_lockObj)
            {
                if (_channels == null || _channels.Count == 0)
                    return;

                double minX = double.MaxValue;
                double maxX = double.MinValue;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    RangeResult xRange = channel.GetXRange();
                    if (xRange.Min < minX) minX = xRange.Min;
                    if (xRange.Max > maxX) maxX = xRange.Max;
                }

                if (minX != double.MaxValue && maxX != double.MinValue)
                {
                    double xRange = maxX - minX;
                    if (xRange < 0.01) xRange = 10;

                    _globalXMin = minX - xRange * 0.1;
                    _globalXMax = maxX + xRange * 0.1;

                    if (_globalXMin < 0) _globalXMin = 0;
                }
                else
                {
                    _globalXMin = 0;
                    _globalXMax = 10;
                }

                foreach (var channel in _channels)
                {
                    channel.ResetYRange();
                    channel.IsYAxisSelected = false;
                }

                _isXAxisSelected = false;
            }

            Invalidate();
        }

        private ChannelData GetSelectedChannel()
        {
            lock (_lockObj)
            {
                foreach (var channel in _channels)
                {
                    if (channel.IsYAxisSelected)
                    {
                        return channel;
                    }
                }
            }
            return null;
        }

        private ChannelData GetChannelAtPosition(Point point)
        {
            lock (_lockObj)
            {
                if (_channels.Count == 0) return null;

                int visibleChannels = _channels.FindAll(c => c.Visible).Count;
                if (visibleChannels == 0) return null;

                int axisLabelHeight = 20;
                int panelContentHeight = (Height - 20 - axisLabelHeight) / visibleChannels;
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                int paddingTop = 5;
                int panelIndex = 0;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    int panelY = paddingTop + panelIndex * panelContentHeight;
                    bool isLastPanel = panelIndex == visibleChannels - 1;
                    int rectHeight = isLastPanel ? panelContentHeight - axisLabelHeight : panelContentHeight;
                    Rectangle yAxisRect = new Rectangle(_graySeparatorAbsX, panelY, paddingLeft - _graySeparatorAbsX, rectHeight);
                    Rectangle xAxisRect = isLastPanel 
                        ? new Rectangle(paddingLeft, panelY + rectHeight, Width - paddingLeft - paddingRight, axisLabelHeight)
                        : Rectangle.Empty;

                    if (yAxisRect.Contains(point))
                    {
                        return channel;
                    }

                    if (isLastPanel && xAxisRect.Contains(point))
                    {
                        _isXAxisSelected = true;
                        foreach (var ch in _channels)
                        {
                            ch.IsYAxisSelected = false;
                        }
                        _selectedChannel = null;
                        Invalidate();
                        return null;
                    }

                    panelIndex++;
                }
            }
            return null;
        }

        /// <summary>
        /// 检测点击位置是否在某个可见通道的名称/颜色线区域（用于右键颜色选择）
        /// 名称区域：颜色线(rect.Left-100) 到 面板左边缘(rect.Left)
        /// </summary>
        private ChannelData GetChannelAtNameArea(Point point)
        {
            lock (_lockObj)
            {
                if (_channels == null || _channels.Count == 0) return null;

                int visibleChannels = _channels.FindAll(c => c.Visible).Count;
                if (visibleChannels == 0) return null;

                int axisLabelHeight = 20;
                int panelContentHeight = (Height - 20 - axisLabelHeight) / visibleChannels;
                int paddingLeft = _leftMarginWidth;
                int paddingTop = 5;
                int panelIndex = 0;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    int panelY = paddingTop + panelIndex * panelContentHeight;
                    bool isLastPanel = panelIndex == visibleChannels - 1;
                    int rectHeight = isLastPanel ? panelContentHeight - axisLabelHeight : panelContentHeight;

                    // 名称区域：颜色线在rect.Left-100，名称文字从rect.Left-95开始
                    // 检测范围：X从paddingLeft-108到paddingLeft，Y覆盖整个面板高度
                    Rectangle nameArea = new Rectangle(
                        paddingLeft - 108,
                        panelY,
                        108,
                        rectHeight
                    );

                    if (nameArea.Contains(point))
                    {
                        return channel;
                    }

                    panelIndex++;
                }
            }
            return null;
        }

        private void DrawMeasureLines(Graphics g)
        {
            if (!_measureLine1X.HasValue && !_measureLine2X.HasValue)
                return;

            int paddingLeft = _leftMarginWidth;
            int paddingRight = 20;
            int axisLabelHeight = 20;

            lock (_lockObj)
            {
                if (_channels == null) return;

                int visibleChannels = _channels.FindAll(c => c.Visible).Count;
                if (visibleChannels == 0) return;

                int panelContentHeight = (Height - 20 - axisLabelHeight * visibleChannels) / visibleChannels;
                int panelHeight = panelContentHeight + axisLabelHeight;
                int panelIndex = 0;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    int panelY = 5 + panelIndex * panelHeight;
                    Rectangle panelRect = new Rectangle(paddingLeft, panelY, Width - paddingLeft - paddingRight, panelContentHeight - 5);
                    RangeResult yRange = _autoScroll ? channel.GetYRangeInXRange(_globalXMin, _globalXMax) : channel.GetYRange();
                    double yMin = yRange.Min - (yRange.Max - yRange.Min) * 0.1;
                    double yMax = yRange.Max + (yRange.Max - yRange.Min) * 0.1;

                    if (_measureLine1X.HasValue)
                    {
                        int screenX1 = ValueToScreenX(_measureLine1X.Value, panelRect);
                        ChannelPoint point1 = FindNearestPoint(channel, _measureLine1X.Value);
                        if (point1 != null)
                        {
                            int screenY1 = ValueToScreenY(point1.Y, yMin, yMax, panelRect);
                            string valueLabel = FormatMeasureLineValue(channel, point1.Y);
                            SizeF valueSize = g.MeasureString(valueLabel, _measureLineFont);
                            using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                            {
                                g.FillRectangle(bgBrush, screenX1 + 5, screenY1 - valueSize.Height / 2 - 2, valueSize.Width + 4, valueSize.Height + 4);
                            }
                            g.DrawString(valueLabel, _measureLineFont, Brushes.Red, screenX1 + 7, screenY1 - valueSize.Height / 2);
                        }
                    }

                    if (_measureLine2X.HasValue)
                    {
                        int screenX2 = ValueToScreenX(_measureLine2X.Value, panelRect);
                        ChannelPoint point2 = FindNearestPoint(channel, _measureLine2X.Value);
                        if (point2 != null)
                        {
                            int screenY2 = ValueToScreenY(point2.Y, yMin, yMax, panelRect);
                            string valueLabel = FormatMeasureLineValue(channel, point2.Y);
                            SizeF valueSize = g.MeasureString(valueLabel, _measureLineFont);
                            using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                            {
                                g.FillRectangle(bgBrush, screenX2 + 5, screenY2 - valueSize.Height / 2 - 2, valueSize.Width + 4, valueSize.Height + 4);
                            }
                            g.DrawString(valueLabel, _measureLineFont, Brushes.Green, screenX2 + 7, screenY2 - valueSize.Height / 2);
                        }
                    }

                    panelIndex++;
                }
            }

            // 绘制连续竖线（贯穿所有通道）
            int totalTop = 5;
            int totalBottom = Height - 35;
            if (_measureLine1X.HasValue)
            {
                int screenX1 = ValueToScreenX(_measureLine1X.Value, new Rectangle(paddingLeft, 5, Width - paddingLeft - paddingRight, Height - 60));
                using (Pen pen = new Pen(Color.Red, 1))
                {
                    pen.DashStyle = DashStyle.Solid;
                    g.DrawLine(pen, screenX1, totalTop, screenX1, totalBottom);
                }
                string label1 = string.Format("{0:F3}s", _measureLine1X.Value);
                SizeF size1 = g.MeasureString(label1, _axisFont);
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                {
                    g.FillRectangle(bgBrush, screenX1 - size1.Width / 2 - 2, Height - 27, size1.Width + 4, size1.Height + 4);
                }
                g.DrawString(label1, _axisFont, Brushes.Red, screenX1 - size1.Width / 2, Height - 25);
            }

            if (_measureLine2X.HasValue)
            {
                int screenX2 = ValueToScreenX(_measureLine2X.Value, new Rectangle(paddingLeft, 5, Width - paddingLeft - paddingRight, Height - 60));
                using (Pen pen = new Pen(Color.Green, 1))
                {
                    pen.DashStyle = DashStyle.Solid;
                    g.DrawLine(pen, screenX2, totalTop, screenX2, totalBottom);
                }
                string label2 = string.Format("{0:F3}s", _measureLine2X.Value);
                SizeF size2 = g.MeasureString(label2, _axisFont);
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                {
                    g.FillRectangle(bgBrush, screenX2 - size2.Width / 2 - 2, Height - 27, size2.Width + 4, size2.Height + 4);
                }
                g.DrawString(label2, _axisFont, Brushes.Green, screenX2 - size2.Width / 2, Height - 25);
            }

            if (_measureLine1X.HasValue && _measureLine2X.HasValue)
            {
                double diff = Math.Abs(_measureLine2X.Value - _measureLine1X.Value);
                int screenX1 = ValueToScreenX(_measureLine1X.Value, new Rectangle(paddingLeft, 5, Width - paddingLeft - paddingRight, Height - 60));
                int screenX2 = ValueToScreenX(_measureLine2X.Value, new Rectangle(paddingLeft, 5, Width - paddingLeft - paddingRight, Height - 60));
                int midX = (screenX1 + screenX2) / 2;
                string diffLabel = string.Format("Δt = {0:F3}s", diff);
                SizeF diffSize = g.MeasureString(diffLabel, _axisFont);
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, Color.Yellow)))
                {
                    g.FillRectangle(bgBrush, midX - diffSize.Width / 2 - 4, 15, diffSize.Width + 8, diffSize.Height + 4);
                }
                g.DrawString(diffLabel, _axisFont, Brushes.Black, midX - diffSize.Width / 2, 15);
            }
        }

        private void ShowDataPointTooltip(Point mousePos)
        {
            int paddingLeft = _leftMarginWidth;
            int paddingRight = 20;
            int axisLabelHeight = 20;

            lock (_lockObj)
            {
                if (_channels == null) return;

                int visibleChannels = _channels.FindAll(c => c.Visible).Count;
                if (visibleChannels == 0) return;

                int panelContentHeight = (Height - 20 - axisLabelHeight * visibleChannels) / visibleChannels;
                int panelHeight = panelContentHeight + axisLabelHeight;
                int panelIndex = 0;

                foreach (var channel in _channels)
                {
                    if (!channel.Visible) continue;

                    int panelY = 5 + panelIndex * panelHeight;
                    Rectangle panelRect = new Rectangle(paddingLeft, panelY, Width - paddingLeft - paddingRight, panelContentHeight - 5);

                    if (panelRect.Contains(mousePos))
                    {
                        List<ChannelPoint> points = channel.GetPointsSnapshotInRange(_globalXMin, _globalXMax);
                        if (points.Count == 0)
                        {
                            panelIndex++;
                            continue;
                        }

                        RangeResult yRange = _autoScroll ? channel.GetYRangeInXRange(_globalXMin, _globalXMax) : channel.GetYRange();
                        double yMin = yRange.Min - (yRange.Max - yRange.Min) * 0.1;
                        double yMax = yRange.Max + (yRange.Max - yRange.Min) * 0.1;

                        double clickedX = ScreenToValueX(mousePos.X, panelRect);
                        double clickedY = ScreenToValueY(mousePos.Y, yMin, yMax, panelRect);

                        ChannelPoint nearestPoint = null;
                        double minDistance = double.MaxValue;

                        foreach (var point in points)
                        {
                            if (point.X < _globalXMin || point.X > _globalXMax)
                                continue;

                            int screenX = ValueToScreenX(point.X, panelRect);
                            int screenY = ValueToScreenY(point.Y, yMin, yMax, panelRect);
                            double distance = Math.Sqrt(Math.Pow(screenX - mousePos.X, 2) + Math.Pow(screenY - mousePos.Y, 2));

                            if (distance < 15 && distance < minDistance)
                            {
                                minDistance = distance;
                                nearestPoint = point;
                            }
                        }

                        if (nearestPoint != null)
                        {
                            string yValueStr = FormatMeasureLineValue(channel, nearestPoint.Y);
                            string tooltip = string.Format("{0}\nX: {1:F2}s\nY: {2}",
                                channel.Name, nearestPoint.X, yValueStr);
                            _toolTip.Show(tooltip, this, mousePos.X + 10, mousePos.Y + 10);
                        }
                        else
                        {
                            _toolTip.Hide(this);
                        }

                        panelIndex++;
                        return;
                    }

                    panelIndex++;
                }
            }

            _toolTip.Hide(this);
        }

        private double ScreenToValueY(int screenY, double yMin, double yMax, Rectangle rect)
        {
            return yMax - (screenY - rect.Top) * (yMax - yMin) / rect.Height;
        }

        public void AddMeasureLine(double xValue)
        {
            if (!_measureLine1X.HasValue)
            {
                _measureLine1X = xValue;
            }
            else if (!_measureLine2X.HasValue)
            {
                _measureLine2X = xValue;
            }
            else
            {
                _measureLine1X = _measureLine2X;
                _measureLine2X = xValue;
            }
            Invalidate();
            OnMeasureLinesChanged?.Invoke();
        }

        public void AddMeasureLine()
        {
            double centerX = (_globalXMin + _globalXMax) / 2;
            AddMeasureLine(centerX);
        }

        public void ClearMeasureLines()
        {
            _measureLine1X = null;
            _measureLine2X = null;
            Invalidate();
            OnMeasureLinesChanged?.Invoke();
        }

        public ChannelPoint FindNearestPoint(ChannelData channel, double xValue)
        {
            ChannelPoint nearestPoint;
            return channel.TryGetNearestPoint(xValue, out nearestPoint) ? nearestPoint : null;
        }

        private string FormatMeasureLineValue(ChannelData channel, double yValue)
        {
            string valueStr;
            if (channel.EnumDefinitions != null && channel.EnumDefinitions.Count > 0)
            {
                string description;
                if (channel.EnumDefinitions.TryGetValue(yValue, out description))
                {
                    valueStr = string.Format("{0} ({1:F1})", description, yValue);
                }
                else
                {
                    valueStr = string.Format("{0:F1}", yValue);
                }
            }
            else
            {
                valueStr = string.Format("{0:F1}", yValue);
            }

            // 带单位显示(过滤空/占位符单位,与通道列表的 [单位] 规则一致)
            string unit = (channel.Unit ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(unit) && unit != "-" && unit != "\"\"")
            {
                valueStr += " " + unit;
            }
            return valueStr;
        }

        /// <summary>
        /// 获取测量线交互区域（OverallPlotRect + XAxisRect 的合并区域）
        /// </summary>
        private Rectangle GetMeasurementInteractionArea()
        {
            int paddingLeft = _leftMarginWidth;
            int paddingRight = 20;
            Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
            return plotArea;
        }

        private void InitializeMeasurementMenu()
        {
            _measurementMenu = new ContextMenuStrip();

            ToolStripMenuItem addCursorItem = new ToolStripMenuItem("添加测量线");
            addCursorItem.Click += (sender, e) =>
            {
                if (_contextMenuMeasureX.HasValue)
                {
                    AddMeasureLine(_contextMenuMeasureX.Value);
                }
            };

            ToolStripMenuItem clearCursorsItem = new ToolStripMenuItem("清除测量线");
            clearCursorsItem.Click += (sender, e) => ClearMeasureLines();

            _measurementMenu.Items.Add(addCursorItem);
            _measurementMenu.Items.Add(clearCursorsItem);
        }

        private void InitializeMeasurementMenuTimer()
        {
            _measurementMenuTimer = new Timer();
            _measurementMenuTimer.Interval = Math.Max(200, SystemInformation.DoubleClickTime);
            _measurementMenuTimer.Tick += (sender, e) =>
            {
                _measurementMenuTimer.Stop();
                if (_contextMenuMeasureX.HasValue)
                {
                    _measurementMenu?.Show(this, _measurementMenuLocation);
                }
            };
        }
    }
}
