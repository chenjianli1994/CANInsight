namespace PCAN_Client.ReportAuto
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using PCAN_Client; // ChannelData / ChannelPoint / RangeResult 位于父命名空间 PCAN_Client（嵌套子命名空间可直接访问，此处显式引用以求清晰）

    /// <summary>
    /// 信号区间统计计算器（“自动生成报告”功能模块）。
    /// 对指定信号在 [t0, t1] 时间窗内的采样点计算统计指标，
    /// 并按 <see cref="SignalStat.PlaceholderMap"/> 将结果写回为 PPT 占位符 KEY → 格式化值字符串。
    /// 全程容错：任何 null / 空数据均返回空字典，不抛异常。
    /// </summary>
    public static class SignalStatsCalculator
    {
        /// <summary>时间范围钳制结果状态</summary>
        internal enum TimeRangeClampState
        {
            Contained,       // 配置范围完全在数据范围内
            Clamped,         // 部分重叠,已按交集裁剪
            NoIntersection   // 与数据完全错开
        }

        /// <summary>
        /// 把配置的时间范围钳制到数据范围内(取交集)。
        /// 部分重叠返回裁剪后的交集;完全错开返回NoIntersection。
        /// </summary>
        internal static TimeRangeClampState ClampTimeRange(double t0, double t1, double dMin, double dMax,
            out double c0, out double c1)
        {
            c0 = Math.Max(t0, dMin);
            c1 = Math.Min(t1, dMax);
            if (c0 >= c1) return TimeRangeClampState.NoIntersection;
            if (c0 == t0 && c1 == t1) return TimeRangeClampState.Contained;
            return TimeRangeClampState.Clamped;
        }


        /// <summary>
        /// 解析时间范围文本(如 "0,10")为起止秒。兼容中文逗号(，)/分号(;；)/多余空格,
        /// 数字按不变文化解析。格式非法返回false。
        /// </summary>
        internal static bool TryParseTimeRange(string text, out double t0, out double t1)
        {
            t0 = 0; t1 = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Replace('，', ',').Replace('；', ',').Replace(';', ',').Split(',');
            if (parts.Length != 2) return false;
            return double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out t0)
                && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out t1);
        }

        /// <summary>
        /// 对指定信号在 [t0, t1] 时间窗内计算指标，返回 PPT占位符KEY → 格式化值字符串。
        /// metrics 支持: "avg"(平均值) "min"(实测最小) "max"(实测最大) "range"(最大-最小)。
        /// 值格式化: 数值保留3位小数(F3)，不带单位。
        /// </summary>
        /// <param name="ch">待统计的信号通道数据</param>
        /// <param name="t0">时间窗起点（秒）</param>
        /// <param name="t1">时间窗终点（秒）</param>
        /// <param name="stat">统计配置（指标列表 + 单位 + 占位符映射）</param>
        /// <returns>PPT 占位符 KEY → 格式化值字符串 的字典；无有效数据时为空字典</returns>
        public static Dictionary<string, string> Calc(ChannelData ch, double t0, double t1, SignalStat stat)
        {
            // 结果字典
            Dictionary<string, string> result = new Dictionary<string, string>();

            // ---- 容错：任一关键入参为空，直接返回空字典 ----
            if (ch == null || stat == null)
            {
                return result;
            }
            // 通道本身无任何采样点：无统计意义，返回空
            if (ch.Points == null || ch.Points.Count == 0)
            {
                return result;
            }
            // 统计配置未提供任何指标：返回空
            if (stat.Metrics == null || stat.Metrics.Count == 0)
            {
                return result;
            }

            // ---- 取时间窗内的采样点快照（线程安全,返回副本） ----
            List<ChannelPoint> pts = ch.GetPointsSnapshotInRange(t0, t1);
            // 时间窗内无数据点：所有指标均无意义，返回空字典
            if (pts == null || pts.Count == 0)
            {
                return result;
            }
            // 过滤丢帧插入的虚拟点(IsLost=true),只保留真实采样点用于统计
            // 不使用 GetYRangeInXRange(其带15%padding,非严格实测最值)
            List<double> ys = pts.Where(p => !p.IsLost).Select(p => p.Y).ToList();
            if (ys.Count == 0)
            {
                return result;
            }
            double realMin = ys.Min();
            double realMax = ys.Max();

            // ---- 逐指标计算 ----
            foreach (string metric in stat.Metrics)
            {
                if (string.IsNullOrEmpty(metric))
                {
                    continue;
                }

                double val = 0;
                bool computed = false;

                // 大小写不敏感匹配，提升容错（匹配用小写；占位符查找仍用原始 metric 字符串以保持键一致）
                string m = metric.ToLowerInvariant();

                if (m == "avg")
                {
                    // 平均值（真实采样点）
                    val = ys.Average();
                    computed = true;
                }
                else if (m == "min")
                {
                    // 实测最小值（真实采样点）
                    val = realMin;
                    computed = true;
                }
                else if (m == "max")
                {
                    // 实测最大值（真实采样点）
                    val = realMax;
                    computed = true;
                }
                else if (m == "range")
                {
                    // 极差 = 实测最大 - 实测最小
                    val = realMax - realMin;
                    computed = true;
                }
                // 其余未知指标：忽略

                if (!computed)
                {
                    continue;
                }

                // ---- 数值格式化：保留3位小数，不带单位 ----
                string s = val.ToString("F3");

                // ---- 以占位符 KEY 写回结果 ----
                // 用原始 metric 字符串作为 PlaceholderMap 的查找键，
                // 保证与 stat.Metrics / stat.PlaceholderMap 中的键大小写一致
                if (stat.PlaceholderMap != null && stat.PlaceholderMap.ContainsKey(metric))
                {
                    result[stat.PlaceholderMap[metric]] = s;
                }
            }

            return result;
        }
    }
}
