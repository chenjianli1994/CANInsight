
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
        public void SetRenderScale(float scale)
        {
            _renderScale = Math.Max(scale, 0.1f);
            _sceneValid = false;   // 线宽/字体/点径都随缩放变化，静态层必须重建
            SmartInvalidate();
        }
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
        /// <summary>数据点绘制：单通道每帧最多绘制的点数（横向间距 = 面板宽度/此值）</summary>
        private const int MaxDotsPerChannel = 200;
        /// <summary>数据点绘制：每帧所有通道合计最多绘制的点数，按可见通道数摊分（下限 24/通道）</summary>
        private const int MaxDotsPerFrame = 1600;
        /// <summary>数据点绘制：可见跨度达到此值(秒)即不再画点（小时级缩略图，单点已无意义）</summary>
        private const double DotMaxSpanSeconds = 3600;
        /// <summary>轴标签文本测量缓存：键=(文本, 字号, DPI)，避免每帧对每个刻度重复 MeasureString</summary>
        private readonly Dictionary<(string Text, float FontSize, float DpiX, float DpiY), SizeF> _textSizeCache =
            new Dictionary<(string, float, float, float), SizeF>();
        private const int TextSizeCacheLimit = 500;   // 超限整体清空，保证长时间运行不无界增长
        /// <summary>缩放字体缓存：报告截图路径按 (字体族, 样式, 字号) 复用，避免每帧 new Font 泄漏 GDI 句柄</summary>
        private readonly Dictionary<(string Family, FontStyle Style, float Size), Font> _scaledFontCache =
            new Dictionary<(string, FontStyle, float), Font>();
        private const int ScaledFontCacheLimit = 4;
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

        // ===== 静态层缓存（H6-A）=====
        /// <summary>静态层位图：背景 + 面板底 + 坐标轴/刻度/标签 + 灰分隔线 + 颜色线/通道名 + 曲线 + 数据点</summary>
        private Bitmap _sceneBitmap;
        /// <summary>位图当前分辨率（必须等于绘制 Graphics 的 DPI，否则文字度量随 DPI 漂移）</summary>
        private float _sceneDpiX = 96f;
        /// <summary>位图当前分辨率（纵向）</summary>
        private float _sceneDpiY = 96f;
        /// <summary>建图时使用的缓存键；与当前帧的键不等即重建</summary>
        private SceneKey _sceneKey;
        /// <summary>位图内容是否对应当前 _sceneKey</summary>
        private bool _sceneValid;
        /// <summary>静态层重建次数（诊断：探针断言命中/重建）</summary>
        private int _sceneBuildCount;
        /// <summary>位图分配失败后的旁路标志：首次失败即置位，此后一律直绘，不再每帧重试分配（契约 §7.1-2）</summary>
        private bool _sceneBypass;
        /// <summary>缓存位图像素上限（4K≈8.3M 仍可缓存）</summary>
        private const long SceneCacheMaxPixels = 12000000L;

        // ===== 帧调度（H6-B）=====
        /// <summary>帧定时器判定节拍（ms）：只判定"是否到了开画时刻"，不对应实际帧率</summary>
        private const int FrameTimerIntervalMs = 10;
        /// <summary>空闲帧最小间隔（ms，与改造前的 67ms 定时器一致，零回归）</summary>
        private const int IdleFrameMinGapMs = 67;
        /// <summary>交互帧最小间隔（ms，拖动/缩放期间）</summary>
        private const int DragFrameMinGapMs = 20;
        /// <summary>墙钟计时（Stopwatch 不受系统时间调整影响），用于节流闸门</summary>
        private readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>上次真正发起重绘的墙钟时刻（ms）；初值取大负数，保证启动后首帧立即开画</summary>
        private long _lastFrameTickMs = -100000;
        /// <summary>是否需要重绘标记（唯一的重绘请求来源，由帧定时器消费）</summary>
        private bool _needsRepaint = false;
        /// <summary>本帧跳过节拍等待（仅 immediate=true 置位；拖动路径不得使用）</summary>
        private bool _forceFrame;
        /// <summary>帧定时器：常驻运行（不再 Pause/Resume），实际帧率由上面的 Stopwatch 闸门决定</summary>
        private System.Windows.Forms.Timer _frameTimer;

        /// <summary>
        /// 静态层缓存键：任一字段变化都必须重建位图。覆盖层状态（测线位置/框选框）不在键内——它们每帧直画。
        /// 观测式键（不给出入口装事件）是唯一不漏的做法，另有 InvalidateScene() 显式兜底。
        /// </summary>
        private struct SceneKey : IEquatable<SceneKey>
        {
            public int Width;
            public int Height;
            public float RenderScale;
            public float DpiX;
            public float DpiY;
            public double XMin;
            public double XMax;
            public bool AutoScroll;
            public int LeftMargin;
            public bool IsXAxisSelected;
            public int ChannelSignature;

            /// <summary>逐字段精确比较（float/double 用 ==：位不一致即变化，宁可多重建一帧也不贴脏像素）</summary>
            public bool Equals(SceneKey o)
            {
                return Width == o.Width && Height == o.Height && RenderScale == o.RenderScale
                    && DpiX == o.DpiX && DpiY == o.DpiY && XMin == o.XMin && XMax == o.XMax
                    && AutoScroll == o.AutoScroll && LeftMargin == o.LeftMargin
                    && IsXAxisSelected == o.IsXAxisSelected && ChannelSignature == o.ChannelSignature;
            }

            public override bool Equals(object obj) { return obj is SceneKey && Equals((SceneKey)obj); }
            public override int GetHashCode() { return Width ^ (Height << 16) ^ ChannelSignature; }
        }

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

            // 初始化帧调度定时器：常驻运行、节拍固定 10ms，真正的帧间隔由 Stopwatch 闸门决定
            // 时序（WM_TIMER 粒度约 15.6ms，故闸门值需取 15.6 的整倍上取）：
            //   交互（拖动/缩放）闸门 20ms ⇒ 实际 ≈31.2ms（≈32fps）
            //   空闲闸门 67ms ⇒ 实际 ≈78ms（与改造前 67ms 定时器一致，零回归）
            //   鼠标按下→首帧 ≈19~22ms，最坏 ≈37ms
            _frameTimer = new System.Windows.Forms.Timer();
            _frameTimer.Interval = FrameTimerIntervalMs;
            _frameTimer.Tick += (s, e) =>
            {
                if (!_needsRepaint) return;
                long minGap = IsInteracting() ? DragFrameMinGapMs : IdleFrameMinGapMs;
                long now = _frameClock.ElapsedMilliseconds;
                if (!_forceFrame && now - _lastFrameTickMs < minGap) return;
                _forceFrame = false;
                _lastFrameTickMs = now;
                _needsRepaint = false;
                Invalidate();
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

        /// <summary>获取缩放后的字体（按字体族/样式/字号缓存复用，统一在 Dispose 释放；调用方不要 Dispose 返回值）</summary>
        private Font GetScaledFont(Font baseFont)
        {
            if (_renderScale <= 1.01f) return baseFont;
            float size = baseFont.Size * _renderScale;
            var key = (baseFont.FontFamily.Name, baseFont.Style, size);
            if (_scaledFontCache.TryGetValue(key, out Font cached)) return cached;

            if (_scaledFontCache.Count >= ScaledFontCacheLimit)
            {
                foreach (var font in _scaledFontCache.Values) font.Dispose();
                _scaledFontCache.Clear();
            }
            Font scaled = new Font(baseFont.FontFamily, size, baseFont.Style);
            _scaledFontCache[key] = scaled;
            return scaled;
        }

        /// <summary>测量文本尺寸（带缓存：键含文本/字号/DPI，避免每帧对每个刻度重复 MeasureString）</summary>
        private SizeF MeasureTextCached(Graphics g, string text, Font font)
        {
            var key = (text, font.Size, g.DpiX, g.DpiY);
            if (_textSizeCache.TryGetValue(key, out SizeF size)) return size;

            size = g.MeasureString(text, font);
            if (_textSizeCache.Count >= TextSizeCacheLimit) _textSizeCache.Clear();
            _textSizeCache[key] = size;
            return size;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_sceneBitmap != null)
                {
                    _sceneBitmap.Dispose();
                    _sceneBitmap = null;
                }
                _sceneValid = false;
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
                foreach (var font in _scaledFontCache.Values) font.Dispose();
                _penCache.Clear();
                _dashPenCache.Clear();
                _dotSpriteCache.Clear();
                _scaledFontCache.Clear();
                _textSizeCache.Clear();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 请求重绘（唯一的失效入口）。所有失效入口（内部 22 处 + 外部 15 处）统一走这里，
        /// 由常驻帧定时器按 Stopwatch 墙钟闸门节拍：交互中最小间隔 20ms、空闲最小间隔 67ms。
        /// immediate=true 只表示"跳过本帧的节拍等待"（≤10ms），不再同步发起重绘；拖动路径禁止使用。
        /// </summary>
        public void SmartInvalidate(bool immediate = false)
        {
            _needsRepaint = true;
            if (immediate) _forceFrame = true;
        }

        /// <summary>是否处于交互中（决定帧间隔取交互闸门还是空闲闸门）</summary>
        private bool IsInteracting()
        {
            return _isDragging || _isZooming || _isYAxisDragging
                || _isDraggingMeasureLine1 || _isDraggingMeasureLine2 || _isDraggingLeftMargin;
        }

        public void SetChannels(List<ChannelData> channels)
        {
            lock (_lockObj)
            {
                _channels = channels;
            }
            InvalidateScene();
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

            // 静态层（背景 + 面板）走复用离屏位图：命中只贴图；覆盖层（测线 + 框选框）每帧直画
            RenderScene(g);
            RenderOverlay(g);
        }

        /// <summary>
        /// 离屏渲染到指定 Graphics（用于报告截图）。
        /// 调用前可临时放大 this.Size 以获得高清输出。
        /// </summary>
        public void RenderTo(Graphics g)
        {
            if (g == null) return;
            // 始终开启抗锯齿，确保曲线平滑清晰
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // 故意不走静态层缓存：离屏渲染只画一帧，无复用价值（3200×1920 会白占约 24MB），
            // 且直绘路径与改造前逐字节一致（报告截图不允许有任何像素差）
            DrawStaticContent(g);
            RenderOverlay(g);
        }

        /// <summary>静态层内容（背景 + 面板）。缓存与直绘共用；禁止读取覆盖层状态。</summary>
        private void DrawStaticContent(Graphics g)
        {
            DrawBackground(g);
            DrawPanels(g);
        }

        /// <summary>覆盖层（测线 + 框选框）。每帧直画，顺序与改造前一致。</summary>
        private void RenderOverlay(Graphics g)
        {
            DrawMeasureLines(g);
            DrawZoomRectangle(g);
        }

        /// <summary>
        /// 画静态层：命中缓存则贴图；键变化则重建整屏后贴图；旁路条件满足则直绘。
        /// 命中时不做任何绘制工作，只做一次 1:1 像素拷贝。
        /// </summary>
        private void RenderScene(Graphics g)
        {
            // 旁路：分配失败过（_sceneBypass，避免每帧重试分配）、报告截图放大（_renderScale>1.01f）、
            // 超出像素上限、控件尚未布局（尺寸为 0 时 Bitmap 构造必然抛异常）
            if (_sceneBypass || _renderScale > 1.01f || (long)Width * (long)Height > SceneCacheMaxPixels
                || Width <= 0 || Height <= 0)
            {
                DrawStaticContent(g);
                return;
            }

            SceneKey key = BuildSceneKey(g);
            if (!_sceneValid || !_sceneKey.Equals(key))
            {
                if (!EnsureSceneBitmap(key))
                {
                    DrawStaticContent(g);
                    return;
                }
                Graphics sg = Graphics.FromImage(_sceneBitmap);
                try
                {
                    // 必须先清成"刚分配"状态：GDI+ 抗锯齿填充会把首行/首列画成半透明像素，
                    // 复用同一张位图重画时这些像素会在上一次的内容上二次混合（实测每重建一次
                    // 首行 1000 + 首列 779 个像素漂移），清底后重建与"新分配+首建"逐字节一致。
                    sg.Clear(Color.Transparent);
                    // 位图 Graphics 的 DPI 取自位图本身（已在 EnsureSceneBitmap 里 SetResolution），
                    // 故这里绘制出的文字度量与屏幕直绘一致
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    sg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    DrawStaticContent(sg);
                }
                finally
                {
                    sg.Dispose();
                }
                _sceneKey = key;
                _sceneValid = true;
                _sceneBuildCount++;
            }

            // 贴图用显式 Pixel 单位 + 显式目标矩形：消除"位图 DPI 必须等于屏幕 DPI"的隐式依赖。
            // 位图尺寸与控件尺寸已由键相等保证，此处是 1:1 像素拷贝（不缩放）。
            System.Diagnostics.Debug.Assert(_sceneBitmap.Width == Width && _sceneBitmap.Height == Height,
                "静态层位图尺寸与控件尺寸不一致，贴图会被缩放");
            // 必须用 SourceCopy（整块替换）：GDI+ 抗锯齿填充会把位图首行/首列边缘画成半透明像素，
            // 若用默认的 SourceOver，重复贴图会把这些像素与上一帧结果二次混合（实测每帧固定 1779 个
            // 像素漂移：首行 1000 + 首列 779），静态层就不再与直绘逐字节一致；替换语义下重复贴图幂等。
            CompositingMode prevCompositing = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            try
            {
                g.DrawImage(_sceneBitmap, new Rectangle(0, 0, Width, Height),
                    0, 0, _sceneBitmap.Width, _sceneBitmap.Height, GraphicsUnit.Pixel);
            }
            finally
            {
                g.CompositingMode = prevCompositing;
            }
        }

        /// <summary>按当前观测状态构造缓存键（覆盖所有影响静态层像素的输入）</summary>
        private SceneKey BuildSceneKey(Graphics g)
        {
            SceneKey k = new SceneKey();
            k.Width = Width;
            k.Height = Height;
            k.RenderScale = _renderScale;
            k.DpiX = g.DpiX;
            k.DpiY = g.DpiY;
            k.XMin = _globalXMin;
            k.XMax = _globalXMax;
            k.AutoScroll = _autoScroll;
            k.LeftMargin = _leftMarginWidth;
            k.IsXAxisSelected = _isXAxisSelected;
            k.ChannelSignature = ComputeChannelSignature();
            return k;
        }

        /// <summary>
        /// 可见通道签名（覆盖 CF/CRA 对 ChannelData 的直写）。
        /// 只读 _channels 不加 _lockObj：调用方与 SetChannels 同在 UI 线程。
        /// </summary>
        private int ComputeChannelSignature()
        {
            if (_channels == null) return 0;
            int h = 17, visible = 0;
            for (int i = 0; i < _channels.Count; i++)
            {
                ChannelData ch = _channels[i];
                if (ch == null || !ch.Visible) continue;
                visible++;
                h = h * 31 + (ch.Name != null ? ch.Name.GetHashCode() : 0);
                h = h * 31 + ch.Color.ToArgb();
                h = h * 31 + ch.LineWidth;
                h = h * 31 + ch.DotSize;
                h = h * 31 + ch.YMin.GetHashCode();
                h = h * 31 + ch.YMax.GetHashCode();
                h = h * 31 + (ch.IsYAxisSelected ? 1 : 0) + (ch.IsHighlighted ? 2 : 0);
                h = h * 31 + ch.DataVersion;
                // Y 轴标签直接由 (枚举值, 描述) 决定，只哈希 Count 会漏掉"改描述/换枚举值"导致的重建
                // （Dictionary 枚举顺序变化会多触发一次重建，只多一帧开销，可接受）
                h = h * 31 + HashEnumDefinitions(ch.EnumDefinitions);
            }
            return h * 31 + visible;
        }

        /// <summary>枚举定义内容哈希（有序 key/value 逐个混合；null/空返回 0）</summary>
        private static int HashEnumDefinitions(Dictionary<double, string> definitions)
        {
            if (definitions == null || definitions.Count == 0) return 0;
            int h = definitions.Count;
            foreach (var kvp in definitions)
            {
                h = h * 31 + kvp.Key.GetHashCode();
                h = h * 31 + (kvp.Value != null ? kvp.Value.GetHashCode() : 0);
            }
            return h;
        }

        /// <summary>确保静态层位图尺寸/分辨率与键匹配；分配失败返回 false（并置旁路标志）。</summary>
        private bool EnsureSceneBitmap(SceneKey key)
        {
            if (_sceneBitmap != null && _sceneBitmap.Width == key.Width && _sceneBitmap.Height == key.Height
                && _sceneDpiX == key.DpiX && _sceneDpiY == key.DpiY) return true;
            if (_sceneBitmap != null)
            {
                _sceneBitmap.Dispose();
                _sceneBitmap = null;
            }
            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(key.Width, key.Height, PixelFormat.Format32bppArgb);
                if (key.DpiX >= 1f && key.DpiY >= 1f) bmp.SetResolution(key.DpiX, key.DpiY);
                _sceneBitmap = bmp;
                _sceneDpiX = key.DpiX;
                _sceneDpiY = key.DpiY;
                return true;
            }
            catch (Exception ex)
            {
                // new Bitmap 成功但 SetResolution 抛异常时 bmp 尚未被字段持有，必须在此释放，
                // 否则每帧泄漏一块整屏位图（评审 B-7）
                bmp?.Dispose();
                // 首次失败后置旁路标志：此后一律直绘，不再每帧"一次异常 + 一次大块分配"（契约 §7.1-2）
                _sceneBypass = true;
                _sceneValid = false;
                System.Diagnostics.Debug.WriteLine("[ChartControl] 静态层位图分配失败，退回直绘: " + ex.Message);
                return false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 尺寸变化会改变面板布局（且位图也要换尺寸），显式失效后按新尺寸开画
            _sceneValid = false;
            SmartInvalidate();
        }

        /// <summary>声明"静态层内容已变"（仅失效，不发起重绘）。</summary>
        public void InvalidateScene()
        {
            _sceneValid = false;
        }

        /// <summary>通道数据/外观被直接改写后调用：静态层失效 + 请求一帧。</summary>
        public void InvalidateChannels()
        {
            _sceneValid = false;
            SmartInvalidate();
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
                // 数据点总预算按可见通道数摊分：通道越多每通道画的点越少，单帧点绘制量有界
                int dotsPerChannel = Math.Max(24, Math.Min(MaxDotsPerChannel, MaxDotsPerFrame / visibleChannels));
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
                    DrawPanelCurve(g, panelRect, channel, dotsPerChannel);
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
                    SizeF labelSize = MeasureTextCached(g, label, scaledAxisFont);
                    // X轴刻度线
                    g.DrawLine(_axisPen, x, rect.Bottom, x, rect.Bottom + 4);
                    g.DrawString(label, scaledAxisFont, _textBrush, x - labelSize.Width / 2, rect.Bottom + 5);
                }
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
                    SizeF labelSize = MeasureTextCached(g, label, scaledFont);

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
                    SizeF labelSize = MeasureTextCached(g, label, scaledFont);

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

        private void DrawPanelCurve(Graphics g, Rectangle rect, ChannelData channel, int maxDots)
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

            // 线宽跟随通道设置(默认1px),并按渲染缩放等比缩放(报告截图/高DPI一致)
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

            // 数据点绘制：按屏幕X间距抽稀显示（每通道不超过 maxDots 点），点数不随缩放级别清零。
            // 仅两种情况不画点：可见跨度大到单点已无意义（DotMaxSpanSeconds，小时级缩略图），或面板过矮
            // （纵向只有十几像素，点只会糊成一片且是单帧最大开销之一）。
            bool showDots = rect.Height >= 24 && (_globalXMax - _globalXMin) < DotMaxSpanSeconds;
            if (showDots)
            {
                // 数据点直径跟随通道设置(默认2px)，并按渲染缩放等比缩放(报告截图/高DPI一致)
                float dotSizePx = Math.Max(1, channel.DotSize) * _renderScale;
                int dotDiameter = Math.Max(1, (int)Math.Round(dotSizePx));
                int dotRadius = dotDiameter / 2;

                // 按屏幕X间距显示圆点:相邻点间距不小于直径 → 不重叠(大小一致)、显示均匀。
                // 间距同时受点数上限约束（≤ maxDots 点/通道）：点绘制是 GDI+ 最贵的部分之一。
                int dotSpacing = Math.Max(dotDiameter, plotWidth / maxDots);

                // 圆点用预渲染贴图一次性 blit（每点 2 次 GDI+ 绘制 → 1 次 DrawImageUnscaled）
                Bitmap dotSprite = GetDotSprite(channel.Color, dotDiameter);

                // 跳过IsLost虚点(Y=旧值,调度脉冲而非真实采样):避免其与真实点(报文周期)混排导致间距忽密忽疏
                // 初始值取负间距,保证首点必然通过(screenX - int.MinValue 会溢出为负导致全部点被跳过)
                int lastDotScreenX = -dotSpacing;
                for (int i = 0; i < pointsCount; i++)
                {
                    ChannelPoint point = pointsInRange[i];
                    if (point == null || point.IsLost) continue;
                    // 与曲线一致：超出可见Y范围的采样按面板边界绘制（AddLineToPaths 同样钳位），
                    // 不跳过点——否则手动Y缩放后会出现"只有曲线、没有数据点"的错位
                    double dotValue = point.Y;
                    if (dotValue < yMin) dotValue = yMin;
                    if (dotValue > yMax) dotValue = yMax;
                    int screenX = ValueToScreenX(point.X, rect);
                    if (screenX - lastDotScreenX < dotSpacing) continue;
                    lastDotScreenX = screenX;
                    int screenY = ValueToScreenY(dotValue, yMin, yMax, rect);
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
                            SmartInvalidate();   // 颜色在通道签名内，键变化自动重建静态层
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
                        _isXAxisSelected = false;
                        lock (_lockObj)
                        {
                            foreach (var channel in _channels)
                            {
                                channel.IsYAxisSelected = false;
                            }
                        }
                        _selectedChannel = null;
                        SmartInvalidate();   // 取消Y轴选中会改变轴笔颜色，键变化自动重建静态层
                        return;
                    }
                }

                if (_measureLine2X.HasValue)
                {
                    int screenX2 = ValueToScreenX(_measureLine2X.Value, plotArea);
                    if (Math.Abs(e.X - screenX2) < 10)
                    {
                        _isDraggingMeasureLine2 = true;
                        _isXAxisSelected = false;
                        lock (_lockObj)
                        {
                            foreach (var channel in _channels)
                            {
                                channel.IsYAxisSelected = false;
                            }
                        }
                        _selectedChannel = null;
                        SmartInvalidate();   // 取消Y轴选中会改变轴笔颜色，键变化自动重建静态层
                        return;
                    }
                }

                // Ctrl+左键:优先进入框选缩放(不受X轴选中状态/点击位置影响)
                if (ModifierKeys == Keys.Control)
                {
                    _isZooming = true;
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
                        _lastMousePos = e.Location;
                    }
                    SmartInvalidate();   // Y轴选中/高亮变化进通道签名，键变化自动重建静态层
                    OnChannelClicked?.Invoke(clickedChannel);
                }
                else if (_isXAxisSelected)
                {
                    _isDragging = true;
                    _lastMousePos = e.Location;
                }
                else
                {
                    _isDragging = true;
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
                SmartInvalidate();   // 测线属覆盖层，不改静态层
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingMeasureLine2 && _measureLine2X.HasValue)
            {
                int paddingLeft = _leftMarginWidth;
                int paddingRight = 20;
                Rectangle plotArea = new Rectangle(paddingLeft, 10, Width - paddingLeft - paddingRight, Height - 50);
                _measureLine2X = ScreenToValueX(e.X, plotArea);
                SmartInvalidate();   // 测线属覆盖层，不改静态层
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingLeftMargin)
            {
                _leftMarginWidth = Math.Max(_graySeparatorAbsX + 40, Math.Min(300, _dragStartMarginWidth + (e.X - _dragStartMarginX)));
                SmartInvalidate();   // 左边距在缓存键内，拖到新宽度时静态层会自动重建
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
                SmartInvalidate();   // YMin/YMax 在通道签名内，静态层按需重建
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

                SmartInvalidate();   // X 范围/AutoScroll 在缓存键内，静态层按需重建
            }
            else if (_isZooming)
            {
                _zoomRect = new Rectangle(
                    Math.Min(_zoomStart.X, e.X),
                    Math.Min(_zoomStart.Y, e.Y),
                    Math.Abs(e.X - _zoomStart.X),
                    Math.Abs(e.Y - _zoomStart.Y));
                SmartInvalidate();   // 框选框属覆盖层，不改静态层
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
                SmartInvalidate();
                return;
            }

            if (_isDraggingMeasureLine1)
            {
                _isDraggingMeasureLine1 = false;
                SmartInvalidate();   // 交互结束补一帧（原恢复帧定时器的调用点）
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isDraggingMeasureLine2)
            {
                _isDraggingMeasureLine2 = false;
                SmartInvalidate();   // 交互结束补一帧（原恢复帧定时器的调用点）；CF 随后会改 SplitterDistance
                OnMeasureLinesChanged?.Invoke();
                return;
            }

            if (_isYAxisDragging)
            {
                _isYAxisDragging = false;
                _selectedChannel = null;
                SmartInvalidate();   // 交互结束补一帧（原恢复帧定时器的调用点）
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
                SmartInvalidate();   // 原「立即重绘 + 恢复帧定时器」合并为一次请求：定时器常驻后只需请求一帧
            }

            _isDragging = false;
            _autoScrollManuallyDisabled = true;  // 拖动后禁止自动恢复，防止视图回跳
            SmartInvalidate();   // 交互结束补一帧（原恢复帧定时器的调用点）
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
                    SmartInvalidate();   // Y 范围/选中状态在通道签名内，键变化自动重建静态层
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
                        SmartInvalidate();   // 测线属覆盖层，不改静态层
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
                        SmartInvalidate();   // 测线属覆盖层，不改静态层
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
                SmartInvalidate();   // Y 范围在通道签名内，键变化自动重建静态层
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

                SmartInvalidate();   // X 范围/AutoScroll 在缓存键内，键变化自动重建静态层
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
            SmartInvalidate();   // 视野/选中/测线全部复位，缓存键自然变化
        }

        public void SetAutoScroll(bool enabled)
        {
            _autoScroll = enabled;
            _autoScrollManuallyDisabled = !enabled;
            OnAutoScrollChanged?.Invoke(enabled);
            // AutoScroll 决定 Y 范围来源（GetYRange vs GetYRangeInXRange），静态层必须重建
            InvalidateScene();
            SmartInvalidate();
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
            // 不在此处直接重绘，由调用方（或本方法末尾）通过 SmartInvalidate 统一走帧调度
            SmartInvalidate();
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

            SmartInvalidate();   // 视野/Y 范围/选中变化，缓存键自然变化
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
                        SmartInvalidate();   // X 轴选中态在缓存键内
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
                            SizeF valueSize = MeasureTextCached(g, valueLabel, _measureLineFont);
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
                            SizeF valueSize = MeasureTextCached(g, valueLabel, _measureLineFont);
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
                SizeF size1 = MeasureTextCached(g, label1, _axisFont);
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
                SizeF size2 = MeasureTextCached(g, label2, _axisFont);
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
                SizeF diffSize = MeasureTextCached(g, diffLabel, _axisFont);
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
            SmartInvalidate();   // 测线属覆盖层，不改静态层
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
            SmartInvalidate();   // 测线属覆盖层，不改静态层
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
