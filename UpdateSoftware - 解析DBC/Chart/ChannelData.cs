
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
        public List<ChannelPoint> Points { get; set; }
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
            Points = new List<ChannelPoint>();
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

        /// <summary>淘汰最旧的点（须在 Points 锁内调用）</summary>
        private void TrimOldestLocked(int count)
        {
            if (count <= 0) return;
            if (count > Points.Count) count = Points.Count;
            if (count <= 0) return;
            Points.RemoveRange(0, count);
            Interlocked.Add(ref _globalPointCount, -count);
        }

        public void AddPoint(double x, double y, bool isLost = false)
        {
            lock (Points)
            {
                Points.Add(new ChannelPoint(x, y, isLost));
                Interlocked.Increment(ref _globalPointCount);
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

                int i0 = startIndex;
                while (i0 <= endIndex)
                {
                    int i1 = i0 + bucketSize - 1;
                    if (i1 > endIndex) i1 = endIndex;

                    ChannelPoint minPoint = null, maxPoint = null;
                    int minIdx = -1, maxIdx = -1;
                    ChannelPoint firstLost = null, lastLost = null;

                    for (int i = i0; i <= i1; i++)
                    {
                        ChannelPoint point = Points[i];
                        if (point == null) continue;
                        // 丢帧虚拟点不参与极值竞争（否则抽样段会整段画成虚线），整列全丢帧时保留首尾维持虚线
                        if (point.IsLost)
                        {
                            if (firstLost == null) firstLost = point;
                            lastLost = point;
                            continue;
                        }
                        if (minPoint == null || point.Y < minPoint.Y) { minPoint = point; minIdx = i; }
                        if (maxPoint == null || point.Y > maxPoint.Y) { maxPoint = point; maxIdx = i; }
                    }

                    if (minPoint == null)
                    {
                        AddPointIfNew(result, firstLost);
                        AddPointIfNew(result, lastLost);
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

                // 保证曲线到达右边缘（最新数据）
                AddPointIfNew(result, Points[endIndex]);

                return result;
            }
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
