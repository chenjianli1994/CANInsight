using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PCAN_Client.ReportAuto
{
    /// <summary>
    /// 一个"分析项目类型"，对应工具栏下拉的一个选项。
    /// 每个 templates/*.json 文件反序列化成一个 AnalysisType。
    /// </summary>
    public class AnalysisType
    {
        public string Name;                       // 显示名
        public List<ImageShapeItem> ImageShapes;   // 要填的图片Shape
        public TableConfig Table;                  // 要填的条件表(可空)
        public List<SignalStat> Signals;           // 要统计的信号
    }

    /// <summary>
    /// PPT 中图片 Shape 的填充配置。
    /// Source="chart"      表示把绘图区(Chart)截图填入该 Shape；
    /// Source="signallist" 表示把信号列表区域截图填入该 Shape。
    /// </summary>
    public class ImageShapeItem
    {
        public string ShapeName;   // PPT里图片Shape的名字
        public string Source;      // "chart"(绘图区) 或 "signallist"(信号列表)
    }

    /// <summary>
    /// PPT 中表格 Shape 的填充配置：固定写指定的某一行，单元格内容可含 {{KEY}} 占位符。
    /// </summary>
    public class TableConfig
    {
        public string ShapeName;   // PPT里表格Shape的名字
        public int Row;            // 要写的那一行(0基)
        public List<string> Cells; // 每个单元格的内容，可含 {{KEY}} 占位符
    }

    /// <summary>
    /// 单个信号的统计配置：根据 MessageId+SignalName 定位数据，
    /// 按 Metrics 计算指标，再用 PlaceholderMap 把指标值替换进 PPT 占位符。
    /// </summary>
    public class SignalStat
    {
        public int MessageId;    // DBC报文ID(用于定位ChannelData,匹配ChannelData.DbcMessageId的int类型)
        public string SignalName;  // 信号名
        public string Unit;        // 单位(如 ℃ km/h)，可空
        public List<string> Metrics;             // 要算的指标: "avg"/"min"/"max"/"range"
        public Dictionary<string, string> PlaceholderMap;  // metric→PPT占位符KEY，如 {"avg":"AVG_HEADTEMP"}
    }

    /// <summary>
    /// 模板加载器：扫描指定目录下所有 *.json，每个 JSON 文件描述一个 AnalysisType。
    /// 单个文件解析失败不影响其它文件，失败信息通过 Debug.WriteLine 输出。
    /// </summary>
    public static class AnalysisTypeLoader
    {
        /// <summary>
        /// 加载 dir 目录下所有 *.json 模板。
        /// 目录不存在或为空时返回空列表，不抛异常；
        /// 单个文件解析失败时跳过该文件并记录到 Debug，继续加载其余文件。
        /// </summary>
        /// <param name="dir">templates 目录路径</param>
        /// <returns>所有加载成功的分析类型；无任何成功时返回空列表</returns>
        public static List<AnalysisType> LoadAll(string dir)
        {
            List<AnalysisType> result = new List<AnalysisType>();

            // 目录不存在或为空，直接返回空列表
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return result;
            }

            string[] jsonFiles;
            try
            {
                jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                // 枚举目录失败(如无权限)，记录后返回空列表
                Debug.WriteLine("[ReportAuto] 枚举模板目录失败: " + dir + " -> " + ex.Message);
                return result;
            }

            foreach (string file in jsonFiles)
            {
                try
                {
                    string jsonText = File.ReadAllText(file);
                    // Newtonsoft.Json 默认属性名大小写不敏感，JSON 用小驼峰或下划线均可命中 public 字段
                    AnalysisType type = JsonConvert.DeserializeObject<AnalysisType>(jsonText);
                    if (type != null)
                    {
                        result.Add(type);
                    }
                }
                catch (Exception ex)
                {
                    // 单个文件解析失败跳过，不影响其余文件
                    Debug.WriteLine("[ReportAuto] 解析模板失败: " + file + " -> " + ex.Message);
                }
            }

            return result;
        }
    }
}
