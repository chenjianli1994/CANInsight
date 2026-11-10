
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace PCAN_Client
{
    public class RangeResult
    {
        public double Min { get; set; }
        public double Max { get; set; }

        public RangeResult(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    public class ChannelPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsLost { get; set; }

        public ChannelPoint(double x, double y, bool isLost = false)
        {
            X = x;
            Y = y;
            IsLost = isLost;
        }
    }

    public class ChannelData
    {
        public string Name { get; set; }
        public Color Color { get; set; }
        /// <summary>
        /// 点集（按 X 递增）。整体替换会让分块索引与静态层缓存失效；
        /// null 归一化为空列表，保证 lock(Points) 的锁对象稳定非 null。
        /// 注意：本 setter 不加锁（与改造前一致的既有约束，仓内没有赋值点）；
        /// 真的要在采集线程并发改点集时，必须自行加锁，否则分块索引的 witness 比较可能漏检。
        /// </summary>
        public List<ChannelPoint> Points
        {
            get { return _points; }
            set
            {
                _points = value != null ? value : new List<ChannelPoint>();
                _blockIndexDirty = true;
                BumpDataVersion();
            }
        }
        private List<ChannelPoint> _points;
        /// <summary>
        /// 数据版本：点集发生任何变化（追加/裁剪/清空/替换 Points 实例）时递增，
        /// 供静态层缓存判断失效；int 原子读。
        /// </summary>
        public int DataVersion { get { return Volatile.Read(ref _dataVersion); } }
        private int _dataVersion;
        /// <summary>递增数据版本（线程安全）。写入点：AddPoint / TrimOldestLocked / Clear / Points setter。</summary>
        private void BumpDataVersion() { Interlocked.Increment(ref _dataVersion); }
        public bool Visible { get; set; }
        public int LineWidth { get; set; }
        /// <summary>数据点直径(px,1~5),全局设置,所有通道一致</summary>
        public int DotSize { get; set; }
        public System.Drawing.Drawing2D.DashStyle DashStyle { get; set; }
        public double CycleTime { get; set; }
        public double LastReceiveTime { get; set; }
        public double LastValue { get; set; }
        public bool IsLost { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }
        public bool IsYAxisSelected { get; set; }
        /// <summary>
        /// 信号列表选中联动:绘图区面板背景高亮(支持多选)
        /// </summary>
        public bool IsHighlighted { get; set; }
        private double _cachedYMin = double.MaxValue;
        private double _cachedYMax = double.MinValue;
        private bool _yRangeCacheValid = false;

        public int DbcMessageId = -1;
        public int DbcMessageIndex = -1;
        public int DbcSignalIndex = -1;
        public string DbcSignalName = "";
        public Dictionary<double, string> EnumDefinitions { get; set; } = new Dictionary<double, string>();
        public string Unit { get; set; } = "";
        /// <summary>
        /// 所属CAN总线通道索引（-1 = 兼容模式，使用全局DBC）
        /// </summary>
        public int BusChannelIndex = -1;
        /// <summary>
        /// 占位符报告专用通道（true=不在绘图区显示，仅为报告统计采集数据）
        /// </summary>
        public bool IsReportOnly { get; set; } = false;

        // Y轴范围缓存（用于滑动时避免重复计算）
        private double _cachedXMin = double.NaN;
        private double _cachedXMax = double.NaN;
        private RangeResult _cachedYRangeInX = null;
        private const int Y_RANGE_CACHE_TTL_MS = 100; // 缓存有效期100ms

        public ChannelData(string name, Color color, DateTime dateTimeNow, Dictionary<double, string> enumDefinitions, string unit, double cycleTime = 0.1, int dbcMessageId = -1, int dbcMessageIndex = -1, int dbcSignalIndex = -1, string signalName = "", int busChannelIndex = -1)
        {
            Name = name;
            Color = color;
            _points = new List<ChannelPoint>();   // 直接初始化字段：构造期 DataVersion=0、索引未脏
            ResetBlockIndexLocked();              // 空索引绑定到该列表，后续 AddPoint 走增量追加
            Visible = true;
            LineWidth = 1;
            DotSize = 2;   // 默认2px直径
            DashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
            CycleTime = cycleTime;
            LastReceiveTime = 0;
            LastValue = 0;
            IsLost = false;
            YMin = 0;
            YMax = 0;
            IsYAxisSelected = false;
            IsHighlighted = false;
            DbcMessageIndex = dbcMessageIndex;
            DbcSignalIndex = dbcSignalIndex;
            DateTime dateTime = dateTimeNow;
            DbcSignalName = signalName;
            DbcMessageId = dbcMessageId;
            EnumDefinitions = enumDefinitions;
            Unit = unit ?? "";
            BusChannelIndex = busChannelIndex;
        }

        // 全局数据点预算（所有通道合计）：超出后淘汰最旧的点，限制长时间运行的常驻内存。
        // 800 万点 × 约 48 字节/点 ≈ 380MB，是"长时间实时采集吃光内存"的上限护栏。
        // 完整历史仍由 BLF 录制落盘；绘图区只保留最近的数据。
        private const long GlobalPointBudget = 8000000;
        // 超预算时单次淘汰的点数：分批淘汰，单次 RemoveRange 的搬移量有界（均摊 O(1)/点）
        private const int GlobalTrimBatch = 50000;
        // 所有通道的点数合计（跨线程用 Interlocked 维护）
        private static long _globalPointCount;

        // ===== H6-C：16 点分块索引（抽稀用）=====
        // 目的：把每帧抽稀的扫描量从 O(可见点数) 降到 O(列数 × (桶内残段 + 块数))，
        // 做法是维护"每 16 点一块"的块内极值（偏移 + 极值 Y），抽稀时"两端残段逐点 + 整块合并"，
        // 输出与改造前的逐点实现逐引用等价（H6 设计 §4 有等价性论证）。
        // 纪律：索引的一切读写都在 lock(Points) 内（与 Points 的写入方同一把锁）；
        //       块内偏移一律用 sbyte 且"无值"哨兵是 -1（0 是合法块内偏移，绝不能用 0 当哨兵）。
        // 体积：每块 4 个 sbyte + 2 个 double = 20 字节 ⇒ 约 1.25B/点（800 万点约 10MB，Points 本身约 384MB）。
        //       极值 Y 冗余一份是为了让热路径不必解引用点对象（DRAM 随机访问是 500k 点场景的头号瓶颈）。
        /// <summary>块大小：必须整除 GlobalTrimBatch(50000/16=3125)，整块裁剪才能只做数组左移</summary>
        private const int BlockSize = 16;
        /// <summary>块内最小 Y 的点偏移；-1 = 该块无有效点</summary>
        private sbyte[] _blockMinOffset;
        /// <summary>块内最大 Y 的点偏移；-1 = 该块无有效点</summary>
        private sbyte[] _blockMaxOffset;
        /// <summary>块内首个丢帧点偏移；-1 = 该块无丢帧点</summary>
        private sbyte[] _blockFirstLostOffset;
        /// <summary>块内末个丢帧点偏移；-1 = 该块无丢帧点</summary>
        private sbyte[] _blockLastLostOffset;
        /// <summary>
        /// 块内最小 Y 的值（与 _blockMinOffset 一一对应）。抽稀热路径直接比较它，避免为了比较去解引用点对象：
        /// 50 万点时点对象合计约 16MB，超出 LLC，每块 2 次点对象加载就是 2 次 DRAM 随机访问（约 70~100ns），
        /// 这是 500k 场景提速上不去的真正瓶颈。内存代价 = 每块 16 字节 ≈ 1B/点（800 万点约 8MB）。
        /// 只在偏移 >= 0 时才被读，故空块填什么值都不影响结果。
        /// </summary>
        private double[] _blockMinY;
        /// <summary>块内最大 Y 的值（与 _blockMaxOffset 一一对应），用途同 _blockMinY</summary>
        private double[] _blockMaxY;
        /// <summary>已建块的块数（恒等于 Points.Count/BlockSize，除非 _blockIndexDirty）</summary>
        private int _blockCount;
        /// <summary>六个索引数组的容量</summary>
        private int _blockCapacity;
        /// <summary>索引当前绑定的 Points 实例（witness）：换了列表就必须重建</summary>
        private List<ChannelPoint> _blockForList;
        /// <summary>索引是否已失效：Append/Trim 时只置脏不清脏，查询时整块重建后清脏</summary>
        private bool _blockIndexDirty;
        /// <summary>整块聚合命中次数（锁内自增，诊断用）</summary>
        private int _aggregationHits;

        /// <summary>整块聚合命中次数（诊断：构建探针据此断言聚合路径确实被跑到）</summary>
        internal int AggregationHits { get { return Volatile.Read(ref _aggregationHits); } }
        /// <summary>分块索引是否处于"待重建"状态（诊断）</summary>
        internal bool BlockIndexDirty { get { return Volatile.Read(ref _blockIndexDirty); } }

        /// <summary>重置索引（空态；Points 未被替换时索引仍与它绑定）</summary>
        private void ResetBlockIndexLocked()
        {
            _blockCount = 0;
            _blockForList = _points;
            _blockIndexDirty = false;
        }

        /// <summary>
        /// 确保索引与当前 Points 一致；需要时整块重建。重建在局部数组里完成，最后一次发布
        /// （字段 + 容量 + count + witness + 脏标志），读者不会看到半成品索引。
        /// </summary>
        private void EnsureBlockIndexLocked()
        {
            List<ChannelPoint> points = _points;
            if (!_blockIndexDirty && ReferenceEquals(points, _blockForList)) return;

            int blocks = points.Count / BlockSize;
            int capacity = _blockCapacity;
            if (capacity < blocks)
            {
                capacity = blocks < 4 ? 4 : blocks;
                if (capacity < _blockCapacity * 2) capacity = _blockCapacity * 2;
            }
            sbyte[] minOff = NewOffsetArray(capacity);
            sbyte[] maxOff = NewOffsetArray(capacity);
            sbyte[] firstLost = NewOffsetArray(capacity);
            sbyte[] lastLost = NewOffsetArray(capacity);
            double[] minY = new double[capacity];
            double[] maxY = new double[capacity];
            for (int b = 0; b < blocks; b++) FillBlockLocked(points, b, minOff, maxOff, firstLost, lastLost, minY, maxY);

            // 一次性发布
            _blockMinOffset = minOff;
            _blockMaxOffset = maxOff;
            _blockFirstLostOffset = firstLost;
            _blockLastLostOffset = lastLost;
            _blockMinY = minY;
            _blockMaxY = maxY;
            _blockCapacity = capacity;
            _blockCount = blocks;
            _blockForList = points;
            _blockIndexDirty = false;
        }

        /// <summary>新建偏移数组，全部填 -1（"无值"）</summary>
        private static sbyte[] NewOffsetArray(int capacity)
        {
            sbyte[] array = new sbyte[capacity];
            for (int i = 0; i < capacity; i++) array[i] = -1;
            return array;
        }

        /// <summary>
        /// 填充单块（必须是完整块）的四项块内偏移与块极值 Y。极值一律严格 &lt; / &gt; ⇒ 取块内首次出现，
        /// 与改造前逐点扫描的口径完全相同；丢帧虚拟点不参与极值，只记块内首/末丢帧偏移。
        /// </summary>
        private static void FillBlockLocked(List<ChannelPoint> points, int blockIndex, sbyte[] minOff, sbyte[] maxOff, sbyte[] firstLost, sbyte[] lastLost, double[] minY, double[] maxY)
        {
            int b = blockIndex * BlockSize;
            int end = b + BlockSize;
            sbyte mo = -1, xo = -1, fo = -1, lo = -1;
            ChannelPoint minPoint = null, maxPoint = null;
            for (int i = b; i < end; i++)
            {
                ChannelPoint point = points[i];
                if (point == null) continue;
                sbyte offset = (sbyte)(i - b);
                if (point.IsLost)
                {
                    if (fo < 0) fo = offset;
                    lo = offset;
                    continue;
                }
                if (minPoint == null || point.Y < minPoint.Y) { minPoint = point; mo = offset; }
                if (maxPoint == null || point.Y > maxPoint.Y) { maxPoint = point; xo = offset; }
            }
            minOff[blockIndex] = mo;
            maxOff[blockIndex] = xo;
            firstLost[blockIndex] = fo;
            lastLost[blockIndex] = lo;
            minY[blockIndex] = minPoint != null ? minPoint.Y : double.MaxValue;
            maxY[blockIndex] = maxPoint != null ? maxPoint.Y : double.MinValue;
        }

        /// <summary>重建单块索引（追加整块时调用；须保证索引已与该列表绑定）</summary>
        private void BuildBlockLocked(int blockIndex)
        {
            FillBlockLocked(_points, blockIndex, _blockMinOffset, _blockMaxOffset, _blockFirstLostOffset, _blockLastLostOffset, _blockMinY, _blockMaxY);
        }

        /// <summary>
        /// AddPoint 之后调用：正好凑满一整块时增量补一块（摊还 O(1)/点）。
        /// 索引已脏或绑定的不是当前列表时不补，交给查询时的整体重建。
        /// </summary>
        private void AppendBlockIfCompleteLocked()
        {
            if (_blockIndexDirty) return;
            List<ChannelPoint> points = _points;
            if (!ReferenceEquals(points, _blockForList)) { _blockIndexDirty = true; return; }
            int complete = points.Count / BlockSize;
            if (complete != _blockCount + 1) return;   // 只在"正好新增一整块"时追加
            if (_blockCount >= _blockCapacity) GrowBlockCapacityLocked(_blockCount + 1);
            BuildBlockLocked(_blockCount);
            _blockCount++;
        }

        /// <summary>扩容四个偏移数组并保留已建块的内容（整块追加时用）</summary>
        private void GrowBlockCapacityLocked(int required)
        {
            if (_blockCapacity >= required) return;
            int capacity = _blockCapacity * 2;
            if (capacity < required) capacity = required;
            if (capacity < 4) capacity = 4;
            sbyte[] minOff = NewOffsetArray(capacity);
            sbyte[] maxOff = NewOffsetArray(capacity);
            sbyte[] firstLost = NewOffsetArray(capacity);
            sbyte[] lastLost = NewOffsetArray(capacity);
            double[] minY = new double[capacity];
            double[] maxY = new double[capacity];
            int keep = _blockCount < capacity ? _blockCount : capacity;
            if (_blockMinOffset != null)
            {
                Array.Copy(_blockMinOffset, minOff, keep);
                Array.Copy(_blockMaxOffset, maxOff, keep);
                Array.Copy(_blockFirstLostOffset, firstLost, keep);
                Array.Copy(_blockLastLostOffset, lastLost, keep);
                Array.Copy(_blockMinY, minY, keep);
                Array.Copy(_blockMaxY, maxY, keep);
            }
            _blockMinOffset = minOff;
            _blockMaxOffset = maxOff;
            _blockFirstLostOffset = firstLost;
            _blockLastLostOffset = lastLost;
            _blockMinY = minY;
            _blockMaxY = maxY;
            _blockCapacity = capacity;
        }

        /// <summary>
        /// TrimOldestLocked 之后调用（点已从表头移除）：整块裁剪只需四个数组左移（块基准仍是 0）；
        /// 非整块裁剪置脏，等查询时整体重建；清空则重置。
        /// </summary>
        private void OnPointsTrimmedLocked(int removedCount)
        {
            if (removedCount <= 0) return;
            List<ChannelPoint> points = _points;
            if (_blockIndexDirty || !ReferenceEquals(points, _blockForList)) { _blockIndexDirty = true; return; }
            if (points.Count == 0) { ResetBlockIndexLocked(); return; }
            if (removedCount % BlockSize != 0) { _blockIndexDirty = true; return; }
            int drop = removedCount / BlockSize;
            int remain = _blockCount - drop;
            // 一致性自检：左移后块数必须等于 Count/BlockSize，否则宁可整块重建
            if (remain < 0 || remain != points.Count / BlockSize) { _blockIndexDirty = true; return; }
            if (remain > 0)
            {
                Array.Copy(_blockMinOffset, drop, _blockMinOffset, 0, remain);
                Array.Copy(_blockMaxOffset, drop, _blockMaxOffset, 0, remain);
                Array.Copy(_blockFirstLostOffset, drop, _blockFirstLostOffset, 0, remain);
                Array.Copy(_blockLastLostOffset, drop, _blockLastLostOffset, 0, remain);
                Array.Copy(_blockMinY, drop, _blockMinY, 0, remain);
                Array.Copy(_blockMaxY, drop, _blockMaxY, 0, remain);
            }
            _blockCount = remain;
        }

        /// <summary>淘汰最旧的点（须在 Points 锁内调用）</summary>
        private void TrimOldestLocked(int count)
        {
            if (count <= 0) return;
            if (count > Points.Count) count = Points.Count;
            if (count <= 0) return;
            Points.RemoveRange(0, count);
            Interlocked.Add(ref _globalPointCount, -count);
            BumpDataVersion();
            OnPointsTrimmedLocked(count);
        }

        public void AddPoint(double x, double y, bool isLost = false)
        {
            lock (Points)
            {
                Points.Add(new ChannelPoint(x, y, isLost));
                Interlocked.Increment(ref _globalPointCount);
                // 分块索引增量维护：必须先于下面的淘汰，保证"未脏时 _blockCount == Points.Count/BlockSize"
                BumpDataVersion();
                AppendBlockIfCompleteLocked();
                // 超过全局预算时淘汰本通道最旧的点，保持常驻内存有界
                if (Interlocked.Read(ref _globalPointCount) > GlobalPointBudget)
                {
                    TrimOldestLocked(GlobalTrimBatch);
                }
            }
            if (!isLost)
            {
                LastValue = y;
                LastReceiveTime = x;
                _yRangeCacheValid = false;
                if (y < _cachedYMin) _cachedYMin = y;
                if (y > _cachedYMax) _cachedYMax = y;

                // 清除Y轴范围缓存（数据更新后需要重新计算）
                _cachedYRangeInX = null;
                _cachedXMin = double.NaN;
                _cachedXMax = double.NaN;
            }
        }

        public void Clear()
        {
            lock (Points)
            {
                Interlocked.Add(ref _globalPointCount, -Points.Count);
                Points.Clear();
                Points.TrimExcess();
                ResetBlockIndexLocked();
                BumpDataVersion();
            }
            LastReceiveTime = 0;
            LastValue = 0;
            IsLost = false;
            _yRangeCacheValid = false;
            _cachedYMin = double.MaxValue;
            _cachedYMax = double.MinValue;

            // 清除所有缓存
            _cachedYRangeInX = null;
            _cachedXMin = double.NaN;
            _cachedXMax = double.NaN;
        }

        public RangeResult GetXRange()
        {
            if (Points == null) return new RangeResult(0, 100);
            lock (Points)
            {
                if (Points.Count == 0) return new RangeResult(0, 100);
                // Points 按 X 递增，直接取首尾元素，避免复制整个列表
                return new RangeResult(Points[0].X, Points[Points.Count - 1].X);
            }
        }

        public RangeResult GetYRange()
        {
            if (YMin != YMax)
            {
                return new RangeResult(YMin, YMax);
            }

            if (Points == null) return new RangeResult(-1, 1);
            if (Points.Count == 0) return new RangeResult(-1, 1);

            if (_yRangeCacheValid)
            {
                if (_cachedYMin == _cachedYMax)
                {
                    double value = _cachedYMin;
                    double range1 = Math.Abs(value) * 0.2;
                    if (range1 < 1) range1 = 1;
                    return new RangeResult(value - range1, value + range1);
                }

                double padding = (_cachedYMax - _cachedYMin) * 0.15;
                return new RangeResult(_cachedYMin - padding, _cachedYMax + padding);
            }

            lock (Points)
            {
                _cachedYMin = double.MaxValue;
                _cachedYMax = double.MinValue;
                foreach (var point in Points)
                {
                    if (point.Y < _cachedYMin) _cachedYMin = point.Y;
                    if (point.Y > _cachedYMax) _cachedYMax = point.Y;
                }
            }
            _yRangeCacheValid = true;

            double range;
            if (_cachedYMin == _cachedYMax)
            {
                // 当所有值相同时，根据数值大小动态调整范围
                double value = _cachedYMin;
                range = Math.Abs(value) * 0.2; // 使用20%的相对范围
                if (range < 1) range = 1; // 最小范围为1
                return new RangeResult(value - range, value + range);
            }

            double pad = (_cachedYMax - _cachedYMin) * 0.15;
            range = _cachedYMax - _cachedYMin;

            // 当范围太小时，根据数值大小动态调整
            if (range < 0.1)
            {
                double center = (_cachedYMax + _cachedYMin) / 2;
                double minRange = Math.Abs(center) * 0.2;
                if (minRange < 1) minRange = 1;
                return new RangeResult(center - minRange / 2, center + minRange / 2);
            }

            return new RangeResult(_cachedYMin - pad, _cachedYMax + pad);
        }

        /// <summary>
        /// 获取指定X范围内的Y轴范围（用于滑动时Y轴自适应，带缓存）
        /// </summary>
        public RangeResult GetYRangeInXRange(double xMin, double xMax)
        {
            if (Points == null || Points.Count == 0) return new RangeResult(-1, 1);

            // 检查缓存是否有效（相同的X范围直接返回缓存结果）
            if (_cachedYRangeInX != null &&
                Math.Abs(_cachedXMin - xMin) < 0.001 &&  // X范围几乎相同
                Math.Abs(_cachedXMax - xMax) < 0.001)
            {
                return _cachedYRangeInX;
            }

            double yMin = double.MaxValue;
            double yMax = double.MinValue;
            bool hasVisiblePoint = false;

            lock (Points)
            {
                // 二分查找快速定位起始位置（Points按X递增）
                int startIdx = BinarySearchLeftBound(Points, xMin);
                for (int i = startIdx; i < Points.Count; i++)
                {
                    ChannelPoint point = Points[i];
                    if (point.X > xMax) break;
                    hasVisiblePoint = true;
                    if (point.Y < yMin) yMin = point.Y;
                    if (point.Y > yMax) yMax = point.Y;
                }
            }

            if (!hasVisiblePoint) return new RangeResult(-1, 1);

            RangeResult result;
            if (yMin == yMax)
            {
                double value = yMin;
                double range = Math.Abs(value) * 0.2;
                if (range < 1) range = 1;
                result = new RangeResult(value - range, value + range);
            }
            else
            {
                double pad = (yMax - yMin) * 0.15;
                double rangeTotal = yMax - yMin;

                if (rangeTotal < 0.1)
                {
                    double center = (yMax + yMin) / 2;
                    double minRange = Math.Abs(center) * 0.2;
                    if (minRange < 1) minRange = 1;
                    result = new RangeResult(center - minRange / 2, center + minRange / 2);
                }
                else
                {
                    result = new RangeResult(yMin - pad, yMax + pad);
                }
            }

            // 更新缓存
            _cachedXMin = xMin;
            _cachedXMax = xMax;
            _cachedYRangeInX = result;

            return result;
        }

        public void ResetYRange()
        {
            YMin = 0;
            YMax = 0;
        }

        public List<ChannelPoint> GetPointsSnapshot()
        {
            if (Points == null) return new List<ChannelPoint>();
            lock (Points)
            {
                return new List<ChannelPoint>(Points);
            }
        }

        /// <summary>
        /// 获取指定X范围内的数据点快照（避免复制全部数据）
        /// </summary>
        public List<ChannelPoint> GetPointsSnapshotInRange(double xMin, double xMax)
        {
            if (Points == null || Points.Count == 0) return new List<ChannelPoint>();
            lock (Points)
            {
                int startIdx = BinarySearchLeftBound(Points, xMin);
                int endIdx = BinarySearchRightBound(Points, xMax);
                if (startIdx < 0) startIdx = 0;
                if (endIdx >= Points.Count) endIdx = Points.Count - 1;
                if (startIdx > endIdx) return new List<ChannelPoint>();

                int count = endIdx - startIdx + 1;
                List<ChannelPoint> result = new List<ChannelPoint>(count);
                for (int i = startIdx; i <= endIdx; i++)
                {
                    result.Add(Points[i]);
                }
                return result;
            }
        }

        /// <summary>
        /// 获取指定X范围的索引范围（避免数据复制，用于高性能绘制）
        /// 返回值：Item1=起始索引, Item2=结束索引, Item3=实际数据量
        /// </summary>
        public Tuple<int, int, int> GetPointIndexRange(double xMin, double xMax)
        {
            if (Points == null || Points.Count == 0) return Tuple.Create(0, -1, 0);

            lock (Points)
            {
                int startIdx = BinarySearchLeftBound(Points, xMin);
                int endIdx = BinarySearchRightBound(Points, xMax);
                if (startIdx < 0) startIdx = 0;
                if (endIdx >= Points.Count) endIdx = Points.Count - 1;
                if (startIdx > endIdx) return Tuple.Create(0, -1, 0);

                return Tuple.Create(startIdx, endIdx, endIdx - startIdx + 1);
            }
        }

        /// <summary>
        /// 根据索引获取数据点（线程安全）
        /// </summary>
        public ChannelPoint GetPointAtIndex(int index)
        {
            lock (Points)
            {
                if (index >= 0 && index < Points.Count)
                    return Points[index];
                return null;
            }
        }

        /// <summary>
        /// 二分查找距离指定X值最近的数据点（高效，用于测量线值查找）
        /// </summary>
        public bool TryGetNearestPoint(double xValue, out ChannelPoint point)
        {
            lock (Points)
            {
                if (Points == null || Points.Count == 0)
                {
                    point = null;
                    return false;
                }

                int rightIndex = BinarySearchLeftBound(Points, xValue);
                if (rightIndex <= 0)
                {
                    point = Points[0];
                    return true;
                }

                if (rightIndex >= Points.Count)
                {
                    point = Points[Points.Count - 1];
                    return true;
                }

                ChannelPoint leftPoint = Points[rightIndex - 1];
                ChannelPoint rightPoint = Points[rightIndex];
                double leftDistance = Math.Abs(leftPoint.X - xValue);
                double rightDistance = Math.Abs(rightPoint.X - xValue);
                point = leftDistance <= rightDistance ? leftPoint : rightPoint;
                return true;
            }
        }

        /// <summary>
        /// 获取索引范围内按屏幕列抽稀后的点：每列保留一个代表点（列内靠后的极值），
        /// 返回点数约 ≤ maxColumns，首尾点必定保留。点数未超过 2*maxColumns 时原样返回，
        /// 稀疏数据的绘制结果不变。用于把每帧的拷贝量与 GDI+ 绘制量限制在屏幕分辨率内，
        /// 避免长时间运行/缩小视图后每帧 O(全部点) 导致界面卡死。
        /// H6-C：抽稀改「两端残段逐点 + 整块合并」（块内极值内联比较，见下方桶循环），
        /// 输出与改造前的逐点扫描逐引用等价，签名不变。
        /// 注意：startIndex/endIndex 可能来自另一次取锁前（调用方先算范围再调用），
        /// 所以锁内必须重新钳位，不能假定它们仍然有效。
        /// </summary>
        public List<ChannelPoint> GetPointsInRangeDecimated(int startIndex, int endIndex, int maxColumns)
        {
            if (Points == null || Points.Count == 0) return new List<ChannelPoint>();
            if (maxColumns < 1) maxColumns = 1;

            lock (Points)
            {
                if (startIndex < 0) startIndex = 0;
                if (endIndex >= Points.Count) endIndex = Points.Count - 1;
                if (startIndex > endIndex) return new List<ChannelPoint>();

                int count = endIndex - startIndex + 1;
                int maxPoints = maxColumns * 2;
                if (count <= maxPoints)
                {
                    var exact = new List<ChannelPoint>(count);
                    for (int i = startIndex; i <= endIndex; i++) exact.Add(Points[i]);
                    return exact;
                }

                var result = new List<ChannelPoint>(maxPoints + 2);
                int bucketSize = (count + maxColumns - 1) / maxColumns;
                AddPointIfNew(result, Points[startIndex]);   // 保证左端起点

                List<ChannelPoint> points = _points;
                // 索引一致性：每次调用只做一次 O(1) 检查（脏 / 换了列表 / 块数与 Count 不符 ⇒ 整体重建）。
                // 有了这条，热路径上的每个桶不必再逐块校验偏移合法性——上轮那层"每桶 4×块数 次校验"
                // 与聚合本身同量级，是 16:1 收益被吃掉的第二个原因。
                if (_blockIndexDirty || !ReferenceEquals(_blockForList, points) || _blockCount != points.Count / BlockSize)
                {
                    _blockIndexDirty = true;          // 强制整体重建：块数不符说明索引已失真，不能走增量
                    EnsureBlockIndexLocked();
                }
                sbyte[] blockMin = _blockMinOffset;
                sbyte[] blockMax = _blockMaxOffset;
                sbyte[] blockFirstLost = _blockFirstLostOffset;
                sbyte[] blockLastLost = _blockLastLostOffset;
                double[] blockMinY = _blockMinY;
                double[] blockMaxY = _blockMaxY;
                int blockCount = _blockCount;
                // 块数≥1 ⇒ 六个数组必然已分配
                bool canAggregate = blockCount > 0 && blockMin != null && blockMinY != null;
                int aggregationHits = 0;

                int i0 = startIndex;
                while (i0 <= endIndex)
                {
                    int i1 = i0 + bucketSize - 1;
                    if (i1 > endIndex) i1 = endIndex;

                    // 桶内被完整覆盖的整块区间 [blocksFirst, blocksLast]；没有整块时整桶逐点兜底（与改造前逐字一致）。
                    // 顺序必须是"残段1（升序）→ 块（升序）→ 残段2（升序）"：并列取首次出现靠的就是这个扫描序。
                    int blocksFirst = (i0 + BlockSize - 1) / BlockSize;
                    int blocksLast = (i1 + 1) / BlockSize - 1;
                    if (blocksLast >= blockCount) blocksLast = blockCount - 1;

                    // 桶内累加用局部变量：minY/maxY 是"运行极值"的 Y 值（与 MinPoint.Y 同步），
                    // 这样比较时不需再解引用上一个代表点；丢帧只记索引，桶末才取点对象。
                    ChannelPoint minPoint = null, maxPoint = null;
                    int minIdx = 0, maxIdx = 0;
                    double minY = double.MaxValue, maxY = double.MinValue;
                    int firstLostIdx = -1, lastLostIdx = -1;

                    if (canAggregate && blocksFirst <= blocksLast)
                    {
                        // 残段1：[i0, blocksFirst*BlockSize)
                        int segEnd = blocksFirst * BlockSize;
                        for (int i = i0; i < segEnd; i++)
                        {
                            TakePoint(points, i, ref minPoint, ref minIdx, ref minY, ref maxPoint, ref maxIdx, ref maxY, ref firstLostIdx, ref lastLostIdx);
                        }

                        // 整块合并：每块不再有 4 次累加调用，也不为了比较去解引用点对象——
                        // 先与索引里的块极值 Y 比较（紧凑数组，与点对象无关），只有"赢"的时候才取一次点对象。
                        // 块内取的是该块极值的首次出现，按块升序施加同一严格比较 ⇒ 全局极值与索引不变；
                        // 丢帧按块升序只记首/末索引 ⇒ 全局首个/最后一个丢帧不变。
                        for (int b = blocksFirst; b <= blocksLast; b++)
                        {
                            int b0 = b * BlockSize;
                            int fo = blockFirstLost[b];
                            if (fo >= 0)
                            {
                                if (firstLostIdx < 0) firstLostIdx = b0 + fo;
                                lastLostIdx = b0 + fo;
                                int lo = blockLastLost[b];
                                if (lo != fo) lastLostIdx = b0 + lo;
                            }
                            int mo = blockMin[b];
                            // "minPoint == null ||" 保证与旧实现逐字同口径（含 Y=MaxValue / NaN 这类退化数据）
                            if (mo >= 0 && (minPoint == null || blockMinY[b] < minY))
                            {
                                int abs = b0 + mo;
                                ChannelPoint p = points[abs];
                                minPoint = p;
                                minIdx = abs;
                                minY = p.Y;
                            }
                            int xo = blockMax[b];
                            if (xo >= 0 && (maxPoint == null || blockMaxY[b] > maxY))
                            {
                                int abs = b0 + xo;
                                ChannelPoint p = points[abs];
                                maxPoint = p;
                                maxIdx = abs;
                                maxY = p.Y;
                            }
                        }

                        // 残段2：[(blocksLast+1)*BlockSize, i1]
                        int segStart = (blocksLast + 1) * BlockSize;
                        for (int i = segStart; i <= i1; i++)
                        {
                            TakePoint(points, i, ref minPoint, ref minIdx, ref minY, ref maxPoint, ref maxIdx, ref maxY, ref firstLostIdx, ref lastLostIdx);
                        }
                        aggregationHits++;
                    }
                    else
                    {
                        for (int i = i0; i <= i1; i++)
                        {
                            TakePoint(points, i, ref minPoint, ref minIdx, ref minY, ref maxPoint, ref maxIdx, ref maxY, ref firstLostIdx, ref lastLostIdx);
                        }
                    }

                    if (minPoint == null)
                    {
                        AddPointIfNew(result, firstLostIdx >= 0 ? points[firstLostIdx] : null);
                        AddPointIfNew(result, lastLostIdx >= 0 ? points[lastLostIdx] : null);
                    }
                    else if (maxIdx > minIdx)
                    {
                        // 每列只保留一个代表点（列内靠后的极值）：抽稀后相邻点 X 间距均匀，
                        // 且避免"峰谷交替"的锯齿折线——GDI+ 抗锯齿对陡峭折线的开销是平滑曲线的数十倍
                        AddPointIfNew(result, maxPoint);
                    }
                    else
                    {
                        AddPointIfNew(result, minPoint);
                    }

                    i0 = i1 + 1;
                }

                // 命中计数按调用累计、一次发布（每桶一次 Interlocked 是纯粹的内存栅栏开销）
                if (aggregationHits != 0) Interlocked.Add(ref _aggregationHits, aggregationHits);

                // 保证曲线到达右边缘（最新数据）
                AddPointIfNew(result, Points[endIndex]);

                return result;
            }
        }

        /// <summary>
        /// 桶内逐点累加（只用于两端残段与整桶兜底；整块路径已内联，不再经过这里）。
        /// 规则与改造前的逐点实现逐字相同：严格 &lt; / &gt; ⇒ 并列取首次出现；
        /// 丢帧虚拟点不参与极值竞争（否则抽样段会整段画成虚线），只记首/末丢帧索引，桶末才取点对象。
        /// </summary>
        private static void TakePoint(List<ChannelPoint> points, int index,
            ref ChannelPoint minPoint, ref int minIdx, ref double minY,
            ref ChannelPoint maxPoint, ref int maxIdx, ref double maxY,
            ref int firstLostIdx, ref int lastLostIdx)
        {
            ChannelPoint point = points[index];
            if (point == null) return;
            if (point.IsLost)
            {
                if (firstLostIdx < 0) firstLostIdx = index;
                lastLostIdx = index;
                return;
            }
            double y = point.Y;
            if (minPoint == null || y < minY) { minPoint = point; minIdx = index; minY = y; }
            if (maxPoint == null || y > maxY) { maxPoint = point; maxIdx = index; maxY = y; }
        }

        /// <summary>追加点，跳过与上一个引用相同的点（抽稀时首尾点会被相邻列重复选中）</summary>
        private static void AddPointIfNew(List<ChannelPoint> list, ChannelPoint point)
        {
            if (point == null) return;
            if (list.Count > 0 && ReferenceEquals(list[list.Count - 1], point)) return;
            list.Add(point);
        }

        /// <summary>
        /// 二分查找第一个X >= xMin的索引
        /// </summary>
        private int BinarySearchLeftBound(List<ChannelPoint> points, double xMin)
        {
            int left = 0, right = points.Count;
            while (left < right)
            {
                int mid = left + (right - left) / 2;
                if (points[mid].X < xMin) left = mid + 1;
                else right = mid;
            }
            return left;
        }

        /// <summary>
        /// 二分查找最后一个X <= xMax的索引
        /// </summary>
        private int BinarySearchRightBound(List<ChannelPoint> points, double xMax)
        {
            int left = 0, right = points.Count;
            while (left < right)
            {
                int mid = left + (right - left) / 2;
                if (points[mid].X <= xMax) left = mid + 1;
                else right = mid;
            }
            return left - 1;
        }
    }
}
