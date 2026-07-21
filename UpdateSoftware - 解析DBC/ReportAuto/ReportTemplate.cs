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
        public string TextTemplate;               // 用户编辑的文字内容（含 {{KEY}} 占位符）
        public string TextShapeName;              // PPT模板中接收文字内容的Shape名称
        public List<SignalPresetItem> SignalList; // 保存的绘图信号列表（加载时自动恢复）
        public List<BusChannelConfig> BusChannels; // CAN总线通道配置（多通道模式）
        /// <summary>所属分组名(templates下的一级子文件夹名;""=未分组)。由文件位置决定,不入JSON</summary>
        [JsonIgnore]
        public string Group { get; set; } = "";

        public override string ToString() { return Name ?? "(未命名)"; }
    }

    /// <summary>
    /// CAN总线通道配置（用于保存到工况分类）
    /// </summary>
    public class BusChannelConfig
    {
        public string Name;           // 通道名称，如 "CAN1", "CAN3"
        public byte BlfChannelId;     // BLF文件中的通道号
        public string DbcFilePath;    // DBC文件路径

        public BusChannelConfig() { }

        public BusChannelConfig(string name, byte blfChannelId, string dbcFilePath)
        {
            Name = name;
            BlfChannelId = blfChannelId;
            DbcFilePath = dbcFilePath;
        }
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
    /// 工况分类保存的绘图信号列表条目。加载工况分类时自动恢复绘图信号。
    /// </summary>
    public class SignalPresetItem
    {
        public string SignalName;       // DBC信号名
        public int MessageId;           // DBC报文ID
        public int MessageIndex;        // 报文索引
        public int SignalIndex;         // 信号索引
        public string Unit;             // 单位
        public string Color;            // 颜色（如"#FF0000"）
        public bool Visible = true;     // 是否显示
        public int BusChannelIndex = -1; // CAN总线通道索引（-1=兼容模式，使用全局DBC）
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
        /// <summary>每个占位符的独立时间范围(key=占位符名, value="start,end")。为空时使用报告默认时间范围。</summary>
        public Dictionary<string, string> TimeRangeMap;
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
        /// <returns>所有加载成功的工况分类；无任何成功时返回空列表</returns>
        public static List<AnalysisType> LoadAll(string dir)
        {
            List<AnalysisType> result = new List<AnalysisType>();

            // 目录不存在或为空，直接返回空列表
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return result;
            }

            // 枚举根目录及一级子目录(子目录=分组)
            var fileList = new List<string>();
            try
            {
                fileList.AddRange(Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly));
                foreach (string sub in Directory.GetDirectories(dir))
                    fileList.AddRange(Directory.GetFiles(sub, "*.json", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                // 枚举目录失败(如无权限)，记录后返回空列表
                Debug.WriteLine("[ReportAuto] 枚举模板目录失败: " + dir + " -> " + ex.Message);
                return result;
            }

            // 加载成功的类型及其文件修改时间(用于同名去重)
            var loaded = new List<KeyValuePair<AnalysisType, DateTime>>();
            foreach (string file in fileList)
            {
                try
                {
                    string jsonText = File.ReadAllText(file);
                    // Newtonsoft.Json 默认属性名大小写不敏感，JSON 用小驼峰或下划线均可命中 public 字段
                    AnalysisType type = JsonConvert.DeserializeObject<AnalysisType>(jsonText);
                    if (type != null)
                    {
                        // 分组由文件位置决定:根目录=""(未分组),一级子目录=子目录名
                        string fileDir = Path.GetDirectoryName(file);
                        type.Group = string.Equals(fileDir, dir, StringComparison.OrdinalIgnoreCase)
                            ? "" : Path.GetFileName(fileDir);
                        loaded.Add(new KeyValuePair<AnalysisType, DateTime>(type, File.GetLastWriteTime(file)));
                    }
                }
                catch (Exception ex)
                {
                    // 单个文件解析失败跳过，不影响其余文件
                    Debug.WriteLine("[ReportAuto] 解析模板失败: " + file + " -> " + ex.Message);
                }
            }

            // 按工况名去重:同名时保留文件修改时间最新的,避免目录中残留旧文件导致下拉重复显示
            if (loaded.Count > 1)
            {
                var newest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();
                for (int i = 0; i < loaded.Count; i++)
                {
                    string key = loaded[i].Key.Name ?? "";
                    if (!newest.ContainsKey(key)) order.Add(key);
                    if (!newest.TryGetValue(key, out int idx) || loaded[i].Value > loaded[idx].Value)
                        newest[key] = i;
                }
                var deduped = new List<KeyValuePair<AnalysisType, DateTime>>();
                foreach (string key in order)
                    deduped.Add(loaded[newest[key]]);
                loaded = deduped;
            }

            foreach (var item in loaded)
                result.Add(item.Key);

            return result;
        }
    }

    /// <summary>
    /// PPT模板中固定Shape的标识符常量。编辑器不再让用户选择Shape，
    /// 文本框/图片框在模板中用这些固定名称定位，填充逻辑按此匹配（找不到时兜底）。
    /// </summary>
    public static class TemplateShapeIds
    {
        /// <summary>截图右侧的叙述文字框（放文字内容+{{占位符}}）</summary>
        public const string TextDesc = "RPT_TEXT_DESC";

        /// <summary>图表截图图片框（放绘图区截图）</summary>
        public const string ChartImage = "RPT_IMAGE_CHART";
    }
}
