using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PCAN_Client.CAN_Data.asc;
using PCAN_Client.CAN_Data.blf;
using static PCAN_Client.DataLog.Logging;

namespace PCAN_Client.DataLog
{
    public partial class LogFileToCSV : Form
    {
        private string sourceFilePath = "";
        public double[] saveExcelTimeBuf = new double[8] { 0.1, 0.5, 1, 2, 3, 4, 5, 10 };
        public bool saveExcelFlag = false;
        public bool saveAscFlag = false;
        /// <summary>输出 BLF（仅 ASC 源可用；BLF 源已是 BLF）</summary>
        public bool saveBlfFlag = false;
        public uint saveExcelTime = 1000; /* ms */
        public uint comboBoxExcelSaveTimeSelectIndex = 0;
        public bool changeFlag = false;

        // 添加CancellationTokenSource用于取消任务
        private CancellationTokenSource _cancellationTokenSource;

        public LogFileToCSV()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private void LogFileToCSV_FormClosed(object sender, FormClosedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CAN_API.CAN_API.logFileConvertToCsvFlag = false;
            Main.LogFileToCSV = null;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "请选择asc、blf或bin文件";
            // 默认选中第一项：asc（ASC 是最常用的来源格式）。blf 的大小写两种写法合并成一条，
            // 避免下拉里出现两个同名"blf文件"
            dialog.Filter = "asc文件|*.asc|blf文件|*.blf;*.BLF|bin文件|*.bin";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string path = dialog.FileName;
                if (path.Contains(".asc") || path.Contains(".bin") ||
                    path.Contains(".blf") || path.Contains(".BLF"))
                {
                    sourceFilePath = path;
                    textBox1.Text = sourceFilePath;
                    ApplyOutputAvailability(sourceFilePath);
                }
                else
                {
                    checkBox_ASC.Enabled = true;
                    MessageBox.Show("当前仅支持bin、asc和blf格式的文件");
                }
            }
        }

        /// <summary>按源文件类型决定可用的输出格式：ASC 源→BLF/CSV，BLF 源→ASC/CSV，BIN 源→ASC/CSV</summary>
        private void ApplyOutputAvailability(string path)
        {
            string ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLower();
            bool isAsc = ext == ".asc";
            bool isBlf = ext == ".blf";
            // 转成同类格式没有意义：ASC 源禁用 ASC 输出，BLF 源禁用 BLF 输出
            checkBox_ASC.Enabled = !isAsc;
            checkBox_BLF.Enabled = isAsc;
            if (isAsc) checkBox_ASC.Checked = false;
            if (isBlf) checkBox_BLF.Checked = false;
        }

        private void LogFileToCSV_DragDrop(object sender, DragEventArgs e)
        {
            string path = ((Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            if (path.Contains(".asc") || path.Contains(".bin") ||
                path.Contains(".blf") || path.Contains(".BLF"))
            {
                sourceFilePath = path;
                textBox1.Text = sourceFilePath;
                ApplyOutputAvailability(sourceFilePath);
            }
            else if (path.Contains(".dbc") || path.Contains(".DBC"))
            {
                MessageBox.Show("DBC文件请通过绘图窗口的\"通道配置\"加载");
            }
            else
            {
                checkBox_ASC.Enabled = true;
                MessageBox.Show("当前仅支持bin、asc和blf格式的文件");
            }
        }

        private void LogFileToCSV_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Link;
                this.Cursor = System.Windows.Forms.Cursors.Arrow;  //指定鼠标形状（更好看）  
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void button_Start_Click(object sender, EventArgs e)
        {
            if (!saveExcelFlag && !saveAscFlag && !saveBlfFlag)
            {
                MessageBox.Show("ASC、BLF、CSV 至少要选择一项输出");
                return;
            }
            if (changeFlag)
            {
                MessageBox.Show("正在转换，请勿重复点击");
                return;
            }

            if (saveExcelFlag && 0 == BaseParamter.dbcHelper.dbcFile.messages.Count)
            {
                MessageBox.Show("检测到已经勾选CSV数据转换，请先在通道配置中为CAN通道加载DBC文件");
                return;
            }

            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                MessageBox.Show("请先选择有效的BIN文件、ASC文件或BLF文件");
                return;
            }
            CAN_API.CAN_API.logFileConvertToCsvFlag = true;
            changeFlag = true;
            button_Start.Text = "正在转换";

            // 使用Task.Run在后台线程执行同步方法，避免阻塞UI
            Task.Run(() =>
            {
                try
                {
                    string extension = Path.GetExtension(sourceFilePath).ToLower();

                    if (extension == ".bin")
                    {
                        // BIN文件转换逻辑
                        string ascFileAddr = sourceFilePath;
                        if (saveAscFlag)
                        {
                            ascFileAddr = Path.ChangeExtension(sourceFilePath, ".asc");
                        }
                        string csvFileAddr = sourceFilePath;
                        if (saveExcelFlag)
                        {
                            csvFileAddr = Path.ChangeExtension(sourceFilePath, ".csv");
                        }

                        // 重置进度条
                        UpdateProgressSafe(1);
                        ConvertBinToAscHighPerformance(sourceFilePath, ascFileAddr, csvFileAddr, _cancellationTokenSource.Token);
                    }
                    else if (extension == ".asc")
                    {
                        // ASC 源：可转 BLF / CSV（ASC→ASC 无意义，UI 已禁用该勾选）
                        if (saveBlfFlag)
                        {
                            string blfFileAddr = Path.ChangeExtension(sourceFilePath, ".blf");
                            UpdateProgressSafe(1);
                            ConvertAscToBlf(sourceFilePath, blfFileAddr, _cancellationTokenSource.Token);
                        }
                        if (saveExcelFlag)
                        {
                            string csvFileAddr = Path.ChangeExtension(sourceFilePath, ".csv");
                            UpdateProgressSafe(1);
                            ConvertAscToCsvHighPerformance(sourceFilePath, csvFileAddr, _cancellationTokenSource.Token);
                        }
                    }
                    else if (extension == ".blf" || extension == ".BLF")
                    {
                        // BLF文件转换逻辑
                        string ascFileAddr = Path.ChangeExtension(sourceFilePath, ".asc");
                        string csvFileAddr = Path.ChangeExtension(sourceFilePath, ".csv");

                        UpdateProgressSafe(1);
                        ConvertBlfToAscHighPerformance(sourceFilePath, ascFileAddr, csvFileAddr, _cancellationTokenSource.Token);
                    }

                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("转换成功！");
                        }));
                    }
                }
                catch (OperationCanceledException)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show("转换已取消");
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show($"转换失败: {ex.Message}");
                    }));
                }
                finally
                {
                    changeFlag = false;
                    CAN_API.CAN_API.logFileConvertToCsvFlag = false;
                    this.BeginInvoke(new Action(() =>
                    {
                        UpdateProgressSafe(0);
                        button_Start.Text = "开始转换";
                    }));

                    // 只在必要时进行GC
                    if (GC.GetTotalMemory(false) > 200 * 1024 * 1024)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
            });
        }


        private string GetWeek()
        {
            string week;

            switch (DateTime.Now.DayOfWeek.ToString())
            {
                case "Monday":
                    week = "周一";
                    break;
                case "Tuesday":
                    week = "周二";
                    break;
                case "Wednesday":
                    week = "周三";
                    break;
                case "Thursday":
                    week = "周四";
                    break;
                case "Friday":
                    week = "周五";
                    break;
                case "Saturday":
                    week = "周六";
                    break;
                case "Sunday":
                    week = "周日";
                    break;
                default:
                    week = "周日";
                    break;
            }

            return week;
        }
        private void comboBoxExcelSaveTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboBoxExcelSaveTimeSelectIndex = (uint)comboBoxExcelSaveTime.SelectedIndex;

            Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex = (int)comboBoxExcelSaveTimeSelectIndex;
            Properties.Settings.Default.Save();
        }

        // 从文件名中提取时间戳的函数
        private void TryExtractTimeFromFileName(string filePath, ref DateTime dateTime)
        {
            try
            {
                // 获取文件名（不带路径和扩展名）
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                if (string.IsNullOrEmpty(fileName))
                    return;

                Console.WriteLine($"尝试从文件名提取时间: {fileName}");

                // 尝试解析多种常见的时间格式
                string[] dateTimePatterns = new string[]
                {
            // 格式1: CANFDDTU_CAN-MERGE_2025-12-08_13-29-56 (您的文件格式)
            @"(\d{4})[-_](\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2})",
            
            // 格式2: 2025-12-08_13-29-56
            @"(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})",
            
            // 格式3: 20251208_132956
            @"(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})",
            
            // 格式4: 2025-12-08 13-29-56
            @"(\d{4})-(\d{2})-(\d{2})\s+(\d{2})-(\d{2})-(\d{2})",
            
            // 格式5: 2025_12_08_13_29_56
            @"(\d{4})_(\d{2})_(\d{2})_(\d{2})_(\d{2})_(\d{2})",
            
            // 格式6: 08-12-2025_13-29-56 (日-月-年)
            @"(\d{2})-(\d{2})-(\d{4})_(\d{2})-(\d{2})-(\d{2})",
            
            // 格式7: 132956_2025-12-08
            @"(\d{2})(\d{2})(\d{2})_(\d{4})-(\d{2})-(\d{2})",
            
            // 格式8: 文件名中包含时间，但不一定在末尾
            @".*?(\d{4})[-_](\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2}).*?",
            
            // 格式9: 更灵活的模式，允许各种分隔符
            @".*?(\d{4})[^\d](\d{2})[^\d](\d{2})[^\d](\d{2})[^\d](\d{2})[^\d](\d{2}).*?"
                };

                // 尝试每种模式
                for (int patternIndex = 0; patternIndex < dateTimePatterns.Length; patternIndex++)
                {
                    try
                    {
                        var match = Regex.Match(fileName, dateTimePatterns[patternIndex], RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            Console.WriteLine($"模式{patternIndex}匹配成功: {match.Value}");

                            int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;

                            switch (patternIndex)
                            {
                                case 0: // 格式1: yyyy-MM-dd_HH-mm-ss 或 yyyy_MM_dd_HH_mm_ss
                                case 1: // 格式2: yyyy-MM-dd_HH-mm-ss
                                case 4: // 格式5: yyyy_MM_dd_HH_mm_ss
                                case 8: // 格式8: 灵活匹配
                                case 9: // 格式9: 更灵活
                                    year = int.Parse(match.Groups[1].Value);
                                    month = int.Parse(match.Groups[2].Value);
                                    day = int.Parse(match.Groups[3].Value);
                                    hour = int.Parse(match.Groups[4].Value);
                                    minute = int.Parse(match.Groups[5].Value);
                                    second = int.Parse(match.Groups[6].Value);
                                    break;

                                case 2: // 格式3: yyyyMMdd_HHmmss
                                    year = int.Parse(match.Groups[1].Value);
                                    month = int.Parse(match.Groups[2].Value);
                                    day = int.Parse(match.Groups[3].Value);
                                    hour = int.Parse(match.Groups[4].Value);
                                    minute = int.Parse(match.Groups[5].Value);
                                    second = int.Parse(match.Groups[6].Value);
                                    break;

                                case 3: // 格式4: yyyy-MM-dd HH-mm-ss
                                    year = int.Parse(match.Groups[1].Value);
                                    month = int.Parse(match.Groups[2].Value);
                                    day = int.Parse(match.Groups[3].Value);
                                    hour = int.Parse(match.Groups[4].Value);
                                    minute = int.Parse(match.Groups[5].Value);
                                    second = int.Parse(match.Groups[6].Value);
                                    break;

                                case 5: // 格式6: dd-MM-yyyy_HH-mm-ss
                                    day = int.Parse(match.Groups[1].Value);
                                    month = int.Parse(match.Groups[2].Value);
                                    year = int.Parse(match.Groups[3].Value);
                                    hour = int.Parse(match.Groups[4].Value);
                                    minute = int.Parse(match.Groups[5].Value);
                                    second = int.Parse(match.Groups[6].Value);
                                    break;

                                case 6: // 格式7: HHmmss_yyyy-MM-dd
                                    hour = int.Parse(match.Groups[1].Value);
                                    minute = int.Parse(match.Groups[2].Value);
                                    second = int.Parse(match.Groups[3].Value);
                                    year = int.Parse(match.Groups[4].Value);
                                    month = int.Parse(match.Groups[5].Value);
                                    day = int.Parse(match.Groups[6].Value);
                                    break;
                            }

                            // 验证日期时间是否有效
                            if (year >= 2000 && year <= 2100 &&
                                month >= 1 && month <= 12 &&
                                day >= 1 && day <= 31 &&
                                hour >= 0 && hour <= 23 &&
                                minute >= 0 && minute <= 59 &&
                                second >= 0 && second <= 59)
                            {
                                DateTime extractedTime = new DateTime(year, month, day, hour, minute, second);
                                Console.WriteLine($"成功提取时间: {extractedTime:yyyy-MM-dd HH:mm:ss}");
                                dateTime = extractedTime;
                                return;
                            }
                            else
                            {
                                Console.WriteLine($"提取的时间无效: {year}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"模式{patternIndex}匹配失败: {ex.Message}");
                        // 继续尝试下一个模式
                        continue;
                    }
                }

                Console.WriteLine($"所有模式都匹配失败，文件名: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从文件名提取时间失败: {ex.Message}");
            }

            return;
        }
        /// <summary>BLF → ASC / CSV：统一走 LogFileLoader 解析（经典与 CAN FD 帧都取到正确长度），按勾选写出</summary>
        private void ConvertBlfToAscHighPerformance(string sourceFilePath, string ascFilePath, string csvFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"源文件不存在: {sourceFilePath}");

            DateTime dateTime = DateTime.Now;
            TryExtractTimeFromFileName(sourceFilePath, ref dateTime);

            AscStreamWriter ascWriter = null;
            HighSpeedExcelWriter csvWriter = null;
            try
            {
                if (saveAscFlag)
                {
                    ascWriter = new AscStreamWriter(ascFilePath, dateTime);
                    if (ascWriter.LastError.Length > 0) throw new Exception(ascWriter.LastError);
                }
                if (saveExcelFlag)
                {
                    csvWriter = new HighSpeedExcelWriter(csvFilePath);
                }

                ulong saveCsvIntervalUs = (ulong)(saveExcelTimeBuf[comboBoxExcelSaveTimeSelectIndex] * 1000000.0f);
                ulong lastSaveCsvTimeUs = 0;
                double lastSeconds = 0;
                long fileSize = new FileInfo(sourceFilePath).Length;
                long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024);
                long nextProgressUpdate = progressUpdateThreshold;

                foreach (CanRawMessageRead msg in LogFileLoader.EnumerateCanMessages(sourceFilePath))
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                    // 进度：BLF 的 FilePosition 是按对象长度累计的近似值
                    if (msg.FilePosition >= nextProgressUpdate)
                    {
                        int progress = fileSize > 0 ? (int)(msg.FilePosition * 100L / fileSize) : 0;
                        if (progress > 0 && progress < 100) UpdateProgressSafe(progress);
                        nextProgressUpdate = msg.FilePosition + progressUpdateThreshold;
                    }

                    if (ascWriter != null)
                    {
                        ascWriter.Write(msg.CanId, msg.Data, msg.TimeStampSeconds, msg.Channel, msg.IsFd);
                    }

                    if (csvWriter != null)
                    {
                        // 与旧实现一致：用帧间时间差驱动 DBC 解析（CANDataDeal 期望时间增量）
                        double deltaUs = msg.TimeStampSeconds - lastSeconds;
                        lastSeconds = msg.TimeStampSeconds;
                        if (msg.TimeStampSeconds <= 0) deltaUs = 0;
                        BaseParamter.dbcHelper.CANDataDeal(msg.CanId, (ushort)msg.Data.Length, msg.Data,
                            (ulong)Math.Max(0.0, deltaUs * 1000000.0), BaseParamter.GetLogicChannelByBlfId(msg.Channel));

                        ulong nowUs = (ulong)(msg.TimeStampSeconds * 1000000.0);
                        if (nowUs - lastSaveCsvTimeUs > saveCsvIntervalUs)
                        {
                            lastSaveCsvTimeUs = nowUs;
                            csvWriter.WriteSignalRow(dateTime.AddSeconds(msg.TimeStampSeconds));
                        }
                    }
                }

                UpdateProgressSafe(100);
            }
            finally
            {
                ascWriter?.Dispose();
                csvWriter?.Dispose();
            }
        }

        /// <summary>ASC → BLF：逐帧流式落盘（不把整份文件读进内存），CAN FD 帧写成 FD 对象</summary>
        private void ConvertAscToBlf(string sourceFilePath, string blfFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"源文件不存在: {sourceFilePath}");

            // 测量起始时间优先取 ASC 文件头的 date 行（解析不了退回文件名/当前时间）
            DateTime startTime = DateTime.Now;
            TryExtractTimeFromFileName(sourceFilePath, ref startTime);
            TryReadAscDateHeader(sourceFilePath, ref startTime);

            long fileSize = new FileInfo(sourceFilePath).Length;
            long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024);
            long nextProgressUpdate = progressUpdateThreshold;

            using (var blfWriter = new BlfStreamWriter(blfFilePath, startTime))
            {
                if (blfWriter.LastError.Length > 0) throw new Exception(blfWriter.LastError);

                foreach (CanRawMessageRead msg in LogFileLoader.EnumerateCanMessages(sourceFilePath))
                {
                    if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                    if (msg.FilePosition >= nextProgressUpdate)
                    {
                        int progress = fileSize > 0 ? (int)(msg.FilePosition * 100L / fileSize) : 0;
                        if (progress > 0 && progress < 100) UpdateProgressSafe(progress);
                        nextProgressUpdate = msg.FilePosition + progressUpdateThreshold;
                    }

                    blfWriter.Write(msg.CanId, msg.Data, msg.TimeStampSeconds, msg.Channel, msg.IsFd);
                }
            }

            UpdateProgressSafe(100);
        }

        /// <summary>读 ASC 文件头 date 行作为测量起始时间（解析不了就保持入参）</summary>
        private static void TryReadAscDateHeader(string ascFilePath, ref DateTime dateTime)
        {
            try
            {
                using (var stream = new FileStream(ascFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        if (!line.StartsWith("date")) continue;
                        DateTime parsed;
                        if (TryParseAscDateText(line, out parsed))
                        {
                            dateTime = parsed;
                            return;
                        }
                    }
                }
            }
            catch
            {
                /* 头信息只是显示用，读不到不算错 */
            }
        }

        /// <summary>解析 ASC 的 date 行（支持中文星期、英文星期、带/不带 AM·PM 等常见写法）</summary>
        private static bool TryParseAscDateText(string line, out DateTime dateTime)
        {
            dateTime = DateTime.Now;
            int firstSpace = line.IndexOf(' ');
            if (firstSpace < 0) return false;
            string text = line.Substring(firstSpace + 1).Trim();

            // 中文星期 + yyyyMMdd HH:mm:ss
            if (text.Contains("周") || text.Contains("星期"))
            {
                string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    DateTime.TryParseExact(parts[1] + " " + parts[2], "yyyyMMdd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out dateTime))
                    return true;
                return false;
            }

            // 英文写法：Jan 1 00:00:00 2007 / Nov 8 11:32:06 AM 2025 / Thu Nov 5 10:13:57 2026
            string[] formats =
            {
                "MMM d HH:mm:ss yyyy", "MMM dd HH:mm:ss yyyy",
                "MMM d h:mm:ss tt yyyy", "MMM dd h:mm:ss tt yyyy",
                "ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy",
                "ddd MMM d h:mm:ss tt yyyy", "ddd MMM dd h:mm:ss tt yyyy"
            };
            foreach (string format in formats)
            {
                if (DateTime.TryParseExact(text, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out dateTime))
                    return true;
            }
            return DateTime.TryParse(text, out dateTime);
        }


        // 高性能ASC到CSV转换器
        private void ConvertAscToCsvHighPerformance(string sourceFilePath, string csvFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"源文件不存在: {sourceFilePath}");

            long fileSize = new FileInfo(sourceFilePath).Length;
            int messageCount = 0;
            int lastProgress = 0;
            ulong NowTimeUs = 0;
            ulong LastSaveCSVFileTimeUs = 0;
            DateTime baseDateTime = DateTime.Now;
            ulong SaveCSVFileIntervalUs = 1000000;
            HighSpeedExcelWriter highSpeedExcelWriter = null;

            // 使用更大的缓冲区提高读取性能
            const int BUFFER_SIZE = 512 * 1024; // 512KB缓冲区

            using (FileStream ascFileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
            using (StreamReader reader = new StreamReader(ascFileStream, Encoding.UTF8, true, BUFFER_SIZE))
            {
                highSpeedExcelWriter = new HighSpeedExcelWriter(csvFilePath);
                SaveCSVFileIntervalUs = (uint)(saveExcelTimeBuf[comboBoxExcelSaveTimeSelectIndex] * 1000000.0f);

                // 预计算进度更新阈值，减少计算次数
                long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024); // 每0.1%或0.01MB更新一次
                long nextProgressUpdate = progressUpdateThreshold;

                string line;
                bool headerParsed = false;

                while ((line = reader.ReadLine()) != null && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        long currentPosition = ascFileStream.Position;

                        // 检查是否需要更新进度
                        if (currentPosition >= nextProgressUpdate)
                        {
                            int progress = (int)((currentPosition * 100L) / fileSize);
                            if (progress != lastProgress && progress < 100) // 确保不超过99%
                            {
                                lastProgress = progress;
                                UpdateProgressSafe(progress);
                            }
                            nextProgressUpdate = currentPosition + progressUpdateThreshold;
                        }

                        // 跳过文件头
                        if (!headerParsed)
                        {
                            // 检查所有可能的文件头行
                            if (line.StartsWith("date") || line.StartsWith("base") ||
                                line.StartsWith("no internal") || line.StartsWith("//") || string.IsNullOrWhiteSpace(line))
                            {
                                // 从 date 行解析基准时间（与 ASC→BLF 共用同一套写法识别）
                                DateTime headerTime;
                                if (line.StartsWith("date") && TryParseAscDateText(line, out headerTime))
                                {
                                    baseDateTime = headerTime;
                                }
                                continue;
                            }
                            headerParsed = true;
                        }

                        // 跳过结束标记
                        if (line.StartsWith("End TriggerBlock"))
                            continue;

                        // 统一走 LogFileLoader 的 ASC 解析器：经典/ CAN FD 同一套规则，
                        // FD 的数据长度按十进制并与 DLC 互校（旧实现按十六进制读，"f 64" 会被判成 100 字节而丢帧）
                        CanRawMessageRead ascMsg = LogFileLoader.ParseAscLine(line, currentPosition);
                        if (ascMsg == null) continue;

                        double ascSeconds = ascMsg.TimeStampSeconds;
                        NowTimeUs = (ulong)(ascSeconds * 1000000.0);
                        BaseParamter.dbcHelper.CANDataDeal(ascMsg.CanId, (ushort)ascMsg.Data.Length, ascMsg.Data,
                            NowTimeUs, BaseParamter.GetLogicChannelByBlfId(ascMsg.Channel));
                        messageCount++;

                        if ((NowTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
                        {
                            LastSaveCSVFileTimeUs = NowTimeUs;
                            highSpeedExcelWriter?.WriteSignalRow(baseDateTime.AddSeconds(ascSeconds));
                        }

                        // 每处理1000条消息输出一次调试信息
                        if (messageCount % 1000 == 0)
                        {
                            //System.Diagnostics.Debug.WriteLine($"已处理 {messageCount} 条消息");
                        }

                        // 优化GC策略 - 只在真正需要时收集
                        if (messageCount % 50000 == 0 && GC.GetTotalMemory(false) > 200 * 1024 * 1024)
                        {
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"解析ASC行时出错: {ex.Message}, 行内容: {line}");
                        // 继续处理下一行
                    }
                }
            }

            // 最后写入一次以确保所有数据都被保存
            highSpeedExcelWriter?.WriteSignalRow(baseDateTime.AddSeconds((double)NowTimeUs / 1000000.0));

            System.Diagnostics.Debug.WriteLine($"ASC转换完成，共处理 {messageCount} 条消息");
            UpdateProgressSafe(100);
        }

        private void LogFileToCSV_Load(object sender, EventArgs e)
        {
            // 浅色现代风：统一字体/按钮样式，顶部补一行DBC来源提示（原DBC加载行已移除）
            UiTheme.StyleForm(this);
            UiTheme.StyleButton(button3, "folder");
            UiTheme.StyleButton(button_Start, "play");
            button_Start.BackColor = UiTheme.Accent;
            button_Start.ForeColor = Color.White;
            button_Start.FlatAppearance.BorderColor = UiTheme.Accent;
            var lblDbcHint = new Label
            {
                Text = "DBC 请在绘图窗口的「通道配置」中加载，本窗口共用该DBC解析",
                Location = new Point(12, 8),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblDbcHint);

            comboBoxExcelSaveTime.Items.Clear();
            foreach (var time in saveExcelTimeBuf)
            {
                comboBoxExcelSaveTime.Items.Add(time.ToString());
            }
            comboBoxExcelSaveTimeSelectIndex = (uint)Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex;
            comboBoxExcelSaveTime.SelectedIndex = Properties.Settings.Default.comboBoxExcelSaveTimeSelectIndex;

            if (Properties.Settings.Default.checkBoxSaveExcelChecked)
            {
                checkBox_CSV.Checked = true;
            }
            else
            {
                checkBox_CSV.Checked = false;
            }

            if (Properties.Settings.Default.checkBoxSaveAscChecked)
            {
                checkBox_ASC.Checked = true;
            }
            else
            {
                checkBox_ASC.Checked = false;
            }

            if (Properties.Settings.Default.checkBoxSaveBlfChecked)
            {
                checkBox_BLF.Checked = true;
            }
            else
            {
                checkBox_BLF.Checked = false;
            }
            // 未选源文件时 BLF 输出不可用（只有 ASC 源能转 BLF）
            ApplyOutputAvailability(sourceFilePath);
        }

        private void checkBox_ASC_CheckedChanged(object sender, EventArgs e)
        {
            saveAscFlag = checkBox_ASC.Checked;

            Properties.Settings.Default.checkBoxSaveAscChecked = saveAscFlag;
            Properties.Settings.Default.Save();
        }

        private void checkBox_BLF_CheckedChanged(object sender, EventArgs e)
        {
            saveBlfFlag = checkBox_BLF.Checked;

            Properties.Settings.Default.checkBoxSaveBlfChecked = saveBlfFlag;
            Properties.Settings.Default.Save();
        }

        private void checkBox_CSV_CheckedChanged(object sender, EventArgs e)
        {
            saveExcelFlag = checkBox_CSV.Checked;

            Properties.Settings.Default.checkBoxSaveExcelChecked = saveExcelFlag;
            Properties.Settings.Default.Save();
        }

        // 高性能BIN到ASC转换器 - 平衡内存和速度
        private void ConvertBinToAscHighPerformance(string sourceFilePath, string ascFilePath, string csvFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"源文件不存在: {sourceFilePath}");

            long fileSize = new FileInfo(sourceFilePath).Length;
            int messageCount = 0;
            int lastProgress = 0;
            ulong NowTimeUs = 0;
            ulong LastSaveCSVFileTimeUs = 0;
            DateTime dateTime = DateTime.Now;
            ulong SaveCSVFileIntervalUs = 1000000;
            string SaveCSVPath = csvFilePath;
            HighSpeedExcelWriter highSpeedExcelWriter = null;

            // 使用更大的缓冲区提高读取性能
            const int BUFFER_SIZE = 512 * 1024; // 512KB缓冲区
            const int BATCH_SIZE = 2000; // 批量处理2000条消息

            if(saveAscFlag)
            {
                // 使用FileStream和BinaryReader进行流式读取
                using (FileStream binFileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
                using (BinaryReader reader = new BinaryReader(binFileStream))
                using (FileStream ascFileStream = new FileStream(ascFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE))
                using (StreamWriter sw = new StreamWriter(ascFileStream, Encoding.UTF8, BUFFER_SIZE))
                {
                    if (binFileStream.Position + 8 <= binFileStream.Length)
                    {
                        byte[] bytes = reader.ReadBytes(8);
                        long TimeNow = BitConverter.ToInt64(bytes, 0);
                        dateTime = DateTime.FromBinary(TimeNow);

                        // 写入文件头
                        string title = $"date {GetWeek()} {dateTime:yyyyMMdd HH:mm:ss}\r\n" +
                                      "base hex  timestamps absolute\r\n" +
                                      "no internal events logged\r\n" +
                                      "// version 7.0.0";
                        sw.WriteLine(title);
                        sw.Flush();

                        // 立即更新初始进度
                        UpdateProgressSafe(1);
                    }

                    if (saveExcelFlag)
                    {
                        highSpeedExcelWriter = new HighSpeedExcelWriter(SaveCSVPath);
                        SaveCSVFileIntervalUs = (uint)(saveExcelTimeBuf[comboBoxExcelSaveTimeSelectIndex] * 1000000.0f);
                    }

                    // 使用StringBuilder批量构建输出，减少IO操作
                    StringBuilder batchBuilder = new StringBuilder(BATCH_SIZE * 80); // 预估每条消息80字符
                                                                                     // 预计算进度更新阈值，减少计算次数
                    long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024); // 每0.1%或0.01MB更新一次
                    long nextProgressUpdate = progressUpdateThreshold;

                    while (binFileStream.Position < binFileStream.Length && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            long currentPosition = binFileStream.Position;

                            // 检查是否需要更新进度
                            if (currentPosition >= nextProgressUpdate)
                            {
                                int progress = (int)((currentPosition * 100L) / fileSize);
                                if (progress != lastProgress && progress < 100) // 确保不超过99%
                                {
                                    lastProgress = progress;
                                    UpdateProgressSafe(progress);
                                }
                                nextProgressUpdate = currentPosition + progressUpdateThreshold;
                            }

                            if (binFileStream.Position + 6 > binFileStream.Length) break;

                            // 读取消息头
                            byte lengthByte = reader.ReadByte();
                            int dataLength = lengthByte - 6;

                            // 验证数据长度合理性
                            if (dataLength < 0 || dataLength > 64)
                            {
                                // 尝试寻找下一个有效消息头
                                continue;
                            }

                            if (binFileStream.Position + 5 + dataLength > binFileStream.Length) break;

                            // 读取CAN ID（高字节高5位为通道号，旧文件读出0→归一化为1）
                            byte idHigh = reader.ReadByte();
                            byte msgChannel = (byte)(idHigh >> 3);
                            if (msgChannel == 0) msgChannel = 1;
                            uint canId = (uint)(((idHigh & 0x07) << 8) | reader.ReadByte());

                            // 读取时间戳 - 优化计算
                            ulong us = (ulong)reader.ReadByte() << 16 |
                                      (ulong)reader.ReadByte() << 8 |
                                      reader.ReadByte();
                            NowTimeUs += us;
                            dateTime = dateTime.AddTicks((long)(us * 10));

                            // 读取数据
                            byte[] datas = reader.ReadBytes(dataLength);
                            if (saveExcelFlag)
                            {
                                // 裸格式通道号为BLF通道号语义，转逻辑通道号路由解码
                                BaseParamter.dbcHelper.CANDataDeal(canId, (ushort)dataLength, datas, NowTimeUs, BaseParamter.GetLogicChannelByBlfId(msgChannel));
                            }

                            messageCount++;
                            // 格式化时间戳
                            ulong seconds = NowTimeUs / 1000000;
                            ulong milliseconds = (NowTimeUs % 1000000) / 1000;
                            ulong microseconds = NowTimeUs % 1000;
                            string timestamp = $"{seconds}.{milliseconds:D3}{microseconds:D3}";

                            // 构建数据十六进制字符串 - 使用StringBuilder提高性能
                            StringBuilder dataHexBuilder = new StringBuilder(dataLength * 3);
                            for (int i = 0; i < dataLength; i++)
                            {
                                if (i > 0) dataHexBuilder.Append(' ');
                                dataHexBuilder.Append(datas[i].ToString("X2"));
                            }

                            // 添加到批量构建器（通道列写实际通道号）
                            batchBuilder.AppendLine($"{timestamp} {msgChannel} {canId:X2}             Rx    d {dataLength} {dataHexBuilder}");

                            // 批量写入，减少IO操作
                            if (messageCount % BATCH_SIZE == 0)
                            {
                                sw.Write(batchBuilder.ToString());
                                sw.Flush();
                                batchBuilder.Clear();
                            }

                            // CSV保存逻辑
                            if (saveExcelFlag && (NowTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
                            {
                                LastSaveCSVFileTimeUs = NowTimeUs;
                                highSpeedExcelWriter?.WriteSignalRow(dateTime);
                            }

                            // 优化GC策略 - 只在真正需要时收集
                            if (messageCount % 50000 == 0 && GC.GetTotalMemory(false) > 200 * 1024 * 1024)
                            {
                                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"解析消息时出错 (位置{binFileStream.Position}): {ex.Message}");
                            // 尝试恢复，跳过当前字节
                            if (binFileStream.Position < binFileStream.Length)
                                binFileStream.Position++;
                        }
                    }                        
                    
                    // 写入剩余的消息
                    if (batchBuilder.Length > 0)
                    {
                        sw.Write(batchBuilder.ToString());
                    }

                    // 写入结束标记
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        sw.WriteLine("End TriggerBlock");
                        sw.Flush();
                    }
                }
            }
            else if(saveExcelFlag)
            {
                // 使用FileStream和BinaryReader进行流式读取
                using (FileStream binFileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
                using (BinaryReader reader = new BinaryReader(binFileStream))
                {
                    if (binFileStream.Position + 8 <= binFileStream.Length)
                    {
                        byte[] bytes = reader.ReadBytes(8);
                        long TimeNow = BitConverter.ToInt64(bytes, 0);
                        dateTime = DateTime.FromBinary(TimeNow);

                        // 立即更新初始进度
                        UpdateProgressSafe(1);
                    }

                    highSpeedExcelWriter = new HighSpeedExcelWriter(SaveCSVPath);
                    SaveCSVFileIntervalUs = (uint)(saveExcelTimeBuf[comboBoxExcelSaveTimeSelectIndex] * 1000000.0f);

                    // 使用StringBuilder批量构建输出，减少IO操作
                    StringBuilder batchBuilder = new StringBuilder(BATCH_SIZE * 80); // 预估每条消息80字符
                                                                                     // 预计算进度更新阈值，减少计算次数
                    long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024); // 每0.1%或0.01MB更新一次
                    long nextProgressUpdate = progressUpdateThreshold;

                    while (binFileStream.Position < binFileStream.Length && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            long currentPosition = binFileStream.Position;

                            // 检查是否需要更新进度
                            if (currentPosition >= nextProgressUpdate)
                            {
                                int progress = (int)((currentPosition * 100L) / fileSize);
                                if (progress != lastProgress && progress < 100) // 确保不超过99%
                                {
                                    lastProgress = progress;
                                    UpdateProgressSafe(progress);
                                }
                                nextProgressUpdate = currentPosition + progressUpdateThreshold;
                            }

                            if (binFileStream.Position + 6 > binFileStream.Length) break;

                            // 读取消息头
                            byte lengthByte = reader.ReadByte();
                            int dataLength = lengthByte - 6;

                            // 验证数据长度合理性
                            if (dataLength < 0 || dataLength > 64)
                            {
                                // 尝试寻找下一个有效消息头
                                continue;
                            }

                            if (binFileStream.Position + 5 + dataLength > binFileStream.Length) break;

                            // 读取CAN ID（高字节高5位为通道号，旧文件读出0→归一化为1）
                            byte idHigh = reader.ReadByte();
                            byte msgChannel = (byte)(idHigh >> 3);
                            if (msgChannel == 0) msgChannel = 1;
                            uint canId = (uint)(((idHigh & 0x07) << 8) | reader.ReadByte());

                            // 读取时间戳 - 优化计算
                            ulong us = (ulong)reader.ReadByte() << 16 |
                                      (ulong)reader.ReadByte() << 8 |
                                      reader.ReadByte();
                            NowTimeUs += us;
                            dateTime = dateTime.AddTicks((long)(us * 10));

                            // 读取数据
                            byte[] datas = reader.ReadBytes(dataLength);
                            if (saveExcelFlag)
                            {
                                // 裸格式通道号为BLF通道号语义，转逻辑通道号路由解码
                                BaseParamter.dbcHelper.CANDataDeal(canId, (ushort)dataLength, datas, NowTimeUs, BaseParamter.GetLogicChannelByBlfId(msgChannel));
                            }

                            messageCount++;

                            // CSV保存逻辑
                            if ((NowTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
                            {
                                LastSaveCSVFileTimeUs = NowTimeUs;
                                highSpeedExcelWriter?.WriteSignalRow(dateTime);
                            }

                            // 优化GC策略 - 只在真正需要时收集
                            if (messageCount % 50000 == 0 && GC.GetTotalMemory(false) > 200 * 1024 * 1024)
                            {
                                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"解析消息时出错 (位置{binFileStream.Position}): {ex.Message}");
                            // 尝试恢复，跳过当前字节
                            if (binFileStream.Position < binFileStream.Length)
                                binFileStream.Position++;
                        }
                    }
                }
            }
            UpdateProgressSafe(100);
        }


        // 安全的进度更新方法
        private void UpdateProgressSafe(int progress)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.BeginInvoke(new Action<int>(UpdateProgressSafe), progress);
            }
            else
            {
                progressBar1.Value = Math.Min(Math.Max(progress, 0), 100);
                button_Start.Text = $"正在转换:{Math.Min(Math.Max(progress, 0), 100)}%";
            }
        }

    }
}