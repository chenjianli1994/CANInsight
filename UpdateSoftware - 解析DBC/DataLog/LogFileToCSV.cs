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
            dialog.Title = "请选择bin、ASC或BLF文件";
            dialog.Filter = "bin文件|*.bin|asc文件|*.asc|blf文件|*.blf|blf文件|*.BLF";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string path = dialog.FileName;
                if (path.Contains(".asc"))
                {
                    checkBox_ASC.Checked = false;
                    checkBox_ASC.Enabled = false;
                    sourceFilePath = path;
                    textBox1.Text = sourceFilePath;
                }
                else if (path.Contains(".bin"))
                {
                    checkBox_ASC.Enabled = true;
                    sourceFilePath = path;
                    textBox1.Text = sourceFilePath;
                }
                else if (path.Contains(".blf") || path.Contains(".BLF"))
                {
                    checkBox_ASC.Enabled = true;
                    sourceFilePath = path;
                    textBox1.Text = sourceFilePath;
                }
                else
                {
                    checkBox_ASC.Enabled = true;
                    MessageBox.Show("当前仅支持bin、asc和blf格式的文件");
                }
            }
        }

        private void LogFileToCSV_DragDrop(object sender, DragEventArgs e)
        {
            string path = ((Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            if(path.Contains(".asc"))
            {
                checkBox_ASC.Checked = false;
                checkBox_ASC.Enabled = false;
                sourceFilePath = path;
                textBox1.Text = sourceFilePath;
            }
            else if(path.Contains(".bin"))
            {
                checkBox_ASC.Enabled = true;
                sourceFilePath = path;
                textBox1.Text = sourceFilePath;
            }
            else if (path.Contains(".blf") || path.Contains(".BLF"))
            {
                checkBox_ASC.Enabled = true;
                sourceFilePath = path;
                textBox1.Text = sourceFilePath;
            }
            else if(path.Contains(".dbc") || path.Contains(".DBC"))
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
            if (!saveExcelFlag && !saveAscFlag)
            {
                MessageBox.Show("ASC和CSV必须选择一项");
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
                        // ASC文件转换逻辑
                        if (saveExcelFlag)
                        {
                            string csvFileAddr = Path.ChangeExtension(sourceFilePath, ".csv");
                            UpdateProgressSafe(1);
                            ConvertAscToCsvHighPerformance(sourceFilePath, csvFileAddr, _cancellationTokenSource.Token);
                        }
                        else
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                MessageBox.Show("ASC文件已存在，无需转换ASC格式");
                            }));
                            return;
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

        // 添加时间戳转换方法
        private double ConvertTimestampToMicroseconds(VBLObjectHeader header)
        {
            double timestampMicroseconds;

            // 根据mObjectFlags确定时间戳单位
            if ((header.mObjectFlags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS) != 0)
            {
                // 纳秒单位，转换为微秒
                timestampMicroseconds = header.mObjectTimeStamp / 1000.0;
            }
            else if ((header.mObjectFlags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_TEN_MICS) != 0)
            {
                // 10微秒单位
                timestampMicroseconds = header.mObjectTimeStamp * 10.0;
            }
            else
            {
                // 默认使用纳秒（更常见）
                timestampMicroseconds = header.mObjectTimeStamp / 1000.0;
            }

            return timestampMicroseconds;
        }

        // 转换微秒为时间字符串（用于ASC输出）
        private string ConvertMicrosecondsToTimestampString(double microseconds)
        {
            ulong totalMicroseconds = (ulong)microseconds;
            ulong seconds = totalMicroseconds / 1000000;
            ulong remainingMicroseconds = totalMicroseconds % 1000000;

            // 格式化为"秒.微秒"的字符串，微秒部分固定6位
            return $"{seconds}.{remainingMicroseconds:D6}";
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
        private void ConvertBlfToAscHighPerformance(string sourceFilePath, string ascFilePath, string csvFilePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"源文件不存在: {sourceFilePath}");

            IntPtr fileHandle = BLFAPI.BLCreateFileW(sourceFilePath, GENERIC.GENERIC_READ);

            StreamWriter ascWriter = null;
            HighSpeedExcelWriter csvWriter = null;

            int lastProgress = 0;
            int messageCount = 0;
            double startTimeMicroseconds = -1;  // 第一个时间戳（微秒）
            double currentTimeMicroseconds = 0; // 当前时间戳（微秒）
            double lastTimeMicroseconds = 0;    // 上一个时间戳（微秒）
            ulong SaveCSVFileIntervalUs = 1000000;
            ulong LastSaveCSVFileTimeUs = 0;
            DateTime dateTime = DateTime.Now;
            uint canid = 0;
            uint dataLen = 0;
            byte[] datas = new byte[64];
            long currentPosition = 0;
            const int BUFFER_SIZE = 512 * 1024;
            const int BATCH_SIZE = 2000;

            // 读取文件信息
            VBLFileStatistics myFileStatistics;
            myFileStatistics.mStatisticsSize = 28;
            int retval = BLFAPI.BLGetFileStatistics(fileHandle, out myFileStatistics);
            long fileSize = (long)myFileStatistics.mUncompressedFileSize;

            TryExtractTimeFromFileName(sourceFilePath, ref dateTime);
            try
            {
                if (saveAscFlag)
                {
                    ascWriter = new StreamWriter(ascFilePath, false, Encoding.UTF8, BUFFER_SIZE);

                    // 写入文件头
                    string title = $"date {GetWeek()} {dateTime:yyyyMMdd HH:mm:ss}\r\n" +
                                  "base hex  timestamps absolute\r\n" +
                                  "no internal events logged\r\n" +
                                  "// version 7.0.0";
                    ascWriter.WriteLine(title);
                }

                if (saveExcelFlag)
                {
                    csvWriter = new HighSpeedExcelWriter(csvFilePath);
                    SaveCSVFileIntervalUs = (uint)(saveExcelTimeBuf[comboBoxExcelSaveTimeSelectIndex] * 1000000.0f);
                }

                VBLObjectHeaderBase headerBase;
                StringBuilder batchBuilder = new StringBuilder(BATCH_SIZE * 80);
                long progressUpdateThreshold = Math.Max(fileSize / 1000, 16 * 1024);
                long nextProgressUpdate = progressUpdateThreshold;

                while (retval == 1)
                {
                    retval = BLFAPI.BLPeekObject(fileHandle, out headerBase);
                    if (retval == 1)
                    {
                        double timestampMicroseconds = 0;
                        byte msgChannel = 1; // BLF mChannel（1-based）

                        // 处理不同的对象类型
                        switch (headerBase.mObjectType)
                        {
                            case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE:
                                timestampMicroseconds = ProcessCANMessage(fileHandle, headerBase, ref canid, ref dataLen, ref datas, ref msgChannel);
                                break;

                            case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE2:
                                timestampMicroseconds = ProcessCANMessage2(fileHandle, headerBase, ref canid, ref dataLen, ref datas, ref msgChannel);
                                break;

                            case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE:
                                timestampMicroseconds = ProcessCANFDMessage(fileHandle, headerBase, ref canid, ref dataLen, ref datas, ref msgChannel);
                                break;

                            case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64:
                                timestampMicroseconds = ProcessCANFDMessage64(fileHandle, headerBase, ref canid, ref dataLen, ref datas, ref msgChannel);
                                break;

                            case (uint)BLFObjectType.BL_OBJ_TYPE_APP_TEXT:
                                SkipAppText(fileHandle, headerBase);
                                break;

                            default:
                                SkipObject(fileHandle, headerBase);
                                break;
                        }

                        currentPosition += headerBase.mObjectSize;

                        // 更新进度
                        if (currentPosition >= nextProgressUpdate)
                        {
                            int progress = (int)((currentPosition * 100L) / fileSize);
                            if (progress != lastProgress && progress < 100)
                            {
                                lastProgress = progress;
                                UpdateProgressSafe(progress);
                            }
                            nextProgressUpdate = currentPosition + progressUpdateThreshold;
                        }

                        // 处理CAN消息数据
                        if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE ||
                            headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE2 ||
                            headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE ||
                            headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64)
                        {
                            // 设置起始时间（第一个有效消息的时间）
                            if (startTimeMicroseconds < 0)
                            {
                                startTimeMicroseconds = timestampMicroseconds;
                            }

                            // 计算相对时间（从第一个消息开始）
                            currentTimeMicroseconds = timestampMicroseconds - startTimeMicroseconds;

                            // 确保时间戳非负
                            if (currentTimeMicroseconds < 0) currentTimeMicroseconds = 0;

                            // 计算与上一个消息的时间差（用于DBC处理）
                            double deltaTimeMicroseconds = currentTimeMicroseconds - lastTimeMicroseconds;
                            if (lastTimeMicroseconds == 0) deltaTimeMicroseconds = 0;

                            lastTimeMicroseconds = currentTimeMicroseconds;

                            if (msgChannel == 0) msgChannel = 1; // 容错：BLF通道号应1-based
                            byte logicCh = BaseParamter.GetLogicChannelByBlfId(msgChannel); // BLF通道号→逻辑通道号（解码路由用）

                            if (saveExcelFlag)
                            {
                                // 使用时间差（微秒）处理DBC数据（按通道选择对应DBC解析）
                                BaseParamter.dbcHelper.CANDataDeal(canid, (ushort)dataLen, datas, (ulong)deltaTimeMicroseconds, logicCh);
                            }

                            // 生成ASC格式输出（通道列写BLF实际通道号）
                            if (saveAscFlag && ascWriter != null)
                            {
                                string timestamp = ConvertMicrosecondsToTimestampString(currentTimeMicroseconds);

                                StringBuilder dataHexBuilder = new StringBuilder((int)(dataLen * 3));
                                for (int i = 0; i < dataLen; i++)
                                {
                                    if (i > 0) dataHexBuilder.Append(' ');
                                    dataHexBuilder.Append(datas[i].ToString("X2"));
                                }

                                batchBuilder.AppendLine($"{timestamp} {msgChannel} {canid:X2}             Rx    d {dataLen} {dataHexBuilder}");

                                if (messageCount % BATCH_SIZE == 0)
                                {
                                    ascWriter.Write(batchBuilder.ToString());
                                    ascWriter.Flush();
                                    batchBuilder.Clear();
                                }
                            }

                            messageCount++;

                            // CSV保存逻辑 - 使用绝对时间
                            if (saveExcelFlag)
                            {
                                // 计算从开始到现在的时间（秒）
                                double elapsedSeconds = currentTimeMicroseconds / 1000000.0;
                                DateTime currentTime = dateTime.AddSeconds(elapsedSeconds);

                                // 检查是否需要保存
                                ulong currentTimeUs = (ulong)currentTimeMicroseconds;
                                if ((currentTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
                                {
                                    LastSaveCSVFileTimeUs = currentTimeUs;
                                    csvWriter?.WriteSignalRow(currentTime);
                                }
                            }
                        }

                        // GC优化
                        if (messageCount % 50000 == 0 && GC.GetTotalMemory(false) > 200 * 1024 * 1024)
                        {
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                        }
                    }
                }

                // 写入剩余数据
                if (saveAscFlag && ascWriter != null && batchBuilder.Length > 0)
                {
                    ascWriter.Write(batchBuilder.ToString());
                    ascWriter.WriteLine("End TriggerBlock");
                    ascWriter.Flush();
                    ascWriter.Close();
                }

                BLFAPI.BLCloseHandle(fileHandle);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"BLF转换失败: {ex.Message}");
            }
            finally
            {
                ascWriter?.Dispose();
                csvWriter?.Dispose();
            }
        }

        // 处理不同类型的方法（channel输出BLF mChannel通道号）
        private double ProcessCANMessage(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            int structSize = Marshal.SizeOf<VBLCANMessage>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANMessage msg = new VBLCANMessage();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                int retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANMessage retMsg = Marshal.PtrToStructure<VBLCANMessage>(pMsg);

                // 获取时间戳（微秒）
                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                channel = (byte)retMsg.mChannel;
                dataLen = Math.Min((uint)retMsg.mData.Length, 8);
                Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private double ProcessCANMessage2(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            int structSize = Marshal.SizeOf<VBLCANMessage2>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANMessage2 msg = new VBLCANMessage2();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                int retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANMessage2 retMsg = Marshal.PtrToStructure<VBLCANMessage2>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                channel = (byte)retMsg.mChannel;
                dataLen = Math.Min((uint)retMsg.mDLC, 8);
                if (dataLen > 8) dataLen = 8;
                Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private double ProcessCANFDMessage(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            int structSize = Marshal.SizeOf<VBLCANFDMessage>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANFDMessage msg = new VBLCANFDMessage();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                int retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANFDMessage retMsg = Marshal.PtrToStructure<VBLCANFDMessage>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                channel = (byte)retMsg.mChannel;
                dataLen = retMsg.mValidDataBytes;
                if (dataLen > 16) dataLen = 16;
                Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private double ProcessCANFDMessage64(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            int structSize = Marshal.SizeOf<VBLCANFDMessage64>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANFDMessage64 msg = new VBLCANFDMessage64();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                int retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANFDMessage64 retMsg = Marshal.PtrToStructure<VBLCANFDMessage64>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                channel = (byte)retMsg.mChannel;
                dataLen = retMsg.mValidDataBytes;
                if (dataLen > 64) dataLen = 64;
                Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }
        private void SkipAppText(IntPtr fileHandle, VBLObjectHeaderBase headerBase)
        {
            // 分配内存读取整个AppText对象
            IntPtr pObj = Marshal.AllocHGlobal((int)headerBase.mObjectSize);
            try
            {
                Marshal.StructureToPtr(headerBase, pObj, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pObj, (UIntPtr)headerBase.mObjectSize);

                // 如果需要，可以在这里提取数据库信息
                 VBLAppText appText = Marshal.PtrToStructure<VBLAppText>(pObj);
                 string text = Marshal.PtrToStringAnsi(pObj + Marshal.SizeOf<VBLAppText>());
                Console.WriteLine(text);
            }
            finally
            {
                Marshal.FreeHGlobal(pObj);
            }
        }

        private void SkipObject(IntPtr fileHandle, VBLObjectHeaderBase headerBase)
        {
            // 跳过不支持的对象
            IntPtr pBuffer = Marshal.AllocHGlobal((int)headerBase.mObjectSize);
            try
            {
                Marshal.StructureToPtr(headerBase, pBuffer, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pBuffer, (UIntPtr)headerBase.mObjectSize);
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
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
                bool isCanFdFormat = false;

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
                                // 尝试从日期行解析基准时间
                                if (line.StartsWith("date"))
                                {
                                    // 格式1: "date 周五 20251106 13:47:04"
                                    // 格式2: "date Mon Jan  1 00:00:00 2007"
                                    // 格式3: "date Sat Nov 8 11:32:06 AM 2025"

                                    // 提取日期时间部分：从第一个空格后的所有内容
                                    int firstSpace = line.IndexOf(' ');
                                    if (firstSpace >= 0)
                                    {
                                        string dateTimePart = line.Substring(firstSpace + 1).Trim();

                                        // 检查是否是格式1 (包含中文星期)
                                        if (dateTimePart.Contains("周") || dateTimePart.Contains("星期"))
                                        {
                                            // 格式1: "周五 20251106 13:47:04"
                                            string[] parts = dateTimePart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                            if (parts.Length >= 3)
                                            {
                                                string datePart = parts[1];
                                                string timePart = parts[2];
                                                if (DateTime.TryParseExact(datePart + " " + timePart, "yyyyMMdd HH:mm:ss",
                                                    System.Globalization.CultureInfo.InvariantCulture,
                                                    System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                                                {
                                                    baseDateTime = parsedDate;
                                                    System.Diagnostics.Debug.WriteLine($"解析到基准时间(格式1): {baseDateTime}");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // 格式2和格式3: 尝试多种可能的日期格式
                                            string[] possibleFormats = new string[]
                                            {
                                        "MMM d HH:mm:ss yyyy",           // "Jan 1 00:00:00 2007"
                                        "MMM dd HH:mm:ss yyyy",          // "Jan 01 00:00:00 2007"
                                        "MMM  d HH:mm:ss yyyy",          // "Jan  1 00:00:00 2007" (有额外空格)
                                        "MMM  dd HH:mm:ss yyyy",         // "Jan  01 00:00:00 2007" (有额外空格)
                                        "MMM d h:mm:ss tt yyyy",         // "Nov 8 11:32:06 AM 2025"
                                        "MMM dd h:mm:ss tt yyyy",        // "Nov 08 11:32:06 AM 2025"
                                        "MMM  d h:mm:ss tt yyyy",        // "Nov  8 11:32:06 AM 2025" (有额外空格)
                                        "MMM  dd h:mm:ss tt yyyy"        // "Nov  08 11:32:06 AM 2025" (有额外空格)
                                            };

                                            bool dateParsed = false;
                                            foreach (string format in possibleFormats)
                                            {
                                                if (DateTime.TryParseExact(dateTimePart, format,
                                                    System.Globalization.CultureInfo.InvariantCulture,
                                                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDate))
                                                {
                                                    baseDateTime = parsedDate;
                                                    System.Diagnostics.Debug.WriteLine($"解析到基准时间(格式2/3): {baseDateTime}, 格式: {format}");
                                                    dateParsed = true;
                                                    break;
                                                }
                                            }

                                            // 如果标准格式解析失败，尝试系统的默认解析
                                            if (!dateParsed && DateTime.TryParse(dateTimePart, out DateTime defaultParsedDate))
                                            {
                                                baseDateTime = defaultParsedDate;
                                                System.Diagnostics.Debug.WriteLine($"使用默认解析到基准时间: {baseDateTime}");
                                            }
                                        }
                                    }
                                }
                                continue;
                            }
                            headerParsed = true;

                            // 检查第一行数据以确定格式
                            if (line.Contains("CANFD"))
                            {
                                isCanFdFormat = true;
                                System.Diagnostics.Debug.WriteLine("检测到CANFD格式");
                            }
                        }

                        // 跳过结束标记
                        if (line.StartsWith("End TriggerBlock"))
                            continue;

                        // 根据格式解析数据行
                        if (isCanFdFormat)
                        {
                            ParseCanFdLine(line, ref NowTimeUs, ref messageCount, baseDateTime, ref LastSaveCSVFileTimeUs, SaveCSVFileIntervalUs, highSpeedExcelWriter);
                        }
                        else
                        {
                            ParseStandardCanLine(line, ref NowTimeUs, ref messageCount, baseDateTime, ref LastSaveCSVFileTimeUs, SaveCSVFileIntervalUs, highSpeedExcelWriter);
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

        // 解析标准CAN格式行
        private void ParseStandardCanLine(string line, ref ulong NowTimeUs, ref int messageCount, DateTime baseDateTime,
            ref ulong LastSaveCSVFileTimeUs, ulong SaveCSVFileIntervalUs, HighSpeedExcelWriter highSpeedExcelWriter)
        {
            // 解析ASC数据行格式: "0.000000 1 363             Rx    d 8 23 00 00 88 00 7D 80 05"
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7)
                return;

            // 解析时间戳 (格式: seconds.millisecondsmicroseconds)
            string timestampStr = parts[0];
            if (!double.TryParse(timestampStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double timestampSeconds))
                return;

            NowTimeUs = (ulong)(timestampSeconds * 1000000.0);

            // 解析通道号（第2列，ASC标准格式 "时间 通道 ID Rx d ..."）
            byte msgChannel = 1;
            if (!byte.TryParse(parts[1], out msgChannel) || msgChannel == 0) msgChannel = 1;

            // 解析CAN ID
            if (!uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint canId))
                return;

            // 查找数据长度位置 - 改进的查找逻辑
            int dataLengthIndex = -1;
            for (int i = 3; i < parts.Length - 1; i++)
            {
                // 查找 "d" 或 "r" 标识符，后面跟着数字表示数据长度
                if ((parts[i] == "d" || parts[i] == "r") &&
                    i + 1 < parts.Length &&
                    int.TryParse(parts[i + 1], out _))
                {
                    dataLengthIndex = i + 1;
                    break;
                }
            }

            if (dataLengthIndex < 0 || dataLengthIndex >= parts.Length)
                return;

            // 解析数据长度
            if (!int.TryParse(parts[dataLengthIndex], out int dataLength) || dataLength < 0 || dataLength > 64)
                return;

            // 解析数据字节
            if (dataLengthIndex + dataLength >= parts.Length)
                return;

            byte[] datas = new byte[dataLength];
            bool dataValid = true;
            for (int i = 0; i < dataLength; i++)
            {
                if (!byte.TryParse(parts[dataLengthIndex + 1 + i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out datas[i]))
                {
                    dataValid = false;
                    break;
                }
            }

            if (!dataValid)
                return;

            // 处理CAN数据（ASC通道列为BLF通道号语义，转逻辑通道号路由对应DBC解析）
            BaseParamter.dbcHelper.CANDataDeal(canId, (ushort)dataLength, datas, NowTimeUs, BaseParamter.GetLogicChannelByBlfId(msgChannel));

            messageCount++;

            // CSV保存逻辑
            DateTime currentTime = baseDateTime.AddSeconds(timestampSeconds);
            if ((NowTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
            {
                LastSaveCSVFileTimeUs = NowTimeUs;
                highSpeedExcelWriter?.WriteSignalRow(currentTime);
            }
        }

        // 解析CANFD格式行
        private void ParseCanFdLine(string line, ref ulong NowTimeUs, ref int messageCount, DateTime baseDateTime,
            ref ulong LastSaveCSVFileTimeUs, ulong SaveCSVFileIntervalUs, HighSpeedExcelWriter highSpeedExcelWriter)
        {
            // 解析CANFD数据行格式: "00000000.110000 CANFD 12 Rx 6ca   1 0 8 08 00 00 00 00 00 00 00 00  111000 141 203040 80000000 46500250 4b280150"
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) // 至少需要有时间戳、CANFD、通道、方向、CANID、标志位、数据长度和数据
                return;

            // 检查是否是CANFD格式
            if (parts[1] != "CANFD" && parts[1] != "CAND")
                return;

            // 解析时间戳 (格式: integer.fractional)
            string timestampStr = parts[0];
            if (!double.TryParse(timestampStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double timestampSeconds))
                return;

            NowTimeUs = (ulong)(timestampSeconds * 1000000.0);

            // 解析通道号（CANFD格式第3列 "时间 CANFD 通道 Rx ..."）
            byte msgChannel = 1;
            if (!byte.TryParse(parts[2], out msgChannel) || msgChannel == 0) msgChannel = 1;

            // 解析CAN ID (第5个字段)
            if (!uint.TryParse(parts[4], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint canId))
                return;

            // 数据长度在第9个字段（索引8）
            int dataLengthIndex = 8;
            if (dataLengthIndex >= parts.Length)
                return;

            // 解析数据长度
            if (!int.TryParse(parts[dataLengthIndex], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int dataLength) || dataLength < 0 || dataLength > 64)
                return;

            // 解析数据字节（从索引8开始，共dataLength个字节）
            int dataStartIndex = dataLengthIndex + 1;
            if (dataStartIndex + dataLength > parts.Length)
                return;

            byte[] datas = new byte[dataLength];
            bool dataValid = true;
            for (int i = 0; i < dataLength; i++)
            {
                if (!byte.TryParse(parts[dataStartIndex + i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out datas[i]))
                {
                    dataValid = false;
                    break;
                }
            }

            if (!dataValid)
                return;

            // 处理CAN数据（ASC通道列为BLF通道号语义，转逻辑通道号路由对应DBC解析）
            BaseParamter.dbcHelper.CANDataDeal(canId, (ushort)dataLength, datas, NowTimeUs, BaseParamter.GetLogicChannelByBlfId(msgChannel));
            if(canId == 0x5a0)
            {
                Console.WriteLine($"ID:{canId:X2} len:{dataLength} { string.Join(" ", datas.Select(b => b.ToString("X2")))}" );
            }
            messageCount++;

            // CSV保存逻辑
            DateTime currentTime = baseDateTime.AddSeconds(timestampSeconds);
            if ((NowTimeUs - LastSaveCSVFileTimeUs) > SaveCSVFileIntervalUs)
            {
                LastSaveCSVFileTimeUs = NowTimeUs;
                highSpeedExcelWriter?.WriteSignalRow(currentTime);
            }
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
        }

        private void checkBox_ASC_CheckedChanged(object sender, EventArgs e)
        {
            saveAscFlag = checkBox_ASC.Checked;

            Properties.Settings.Default.checkBoxSaveAscChecked = saveAscFlag;
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
