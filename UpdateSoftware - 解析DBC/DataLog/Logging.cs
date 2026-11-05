using PCAN_Client.CAN_API;
using PCAN_Client.CAN_Data;
using PCAN_Client.CAN_Data.blf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.WebRequestMethods;

namespace PCAN_Client.DataLog
{
    public partial class Logging : Form
    {
        public static string NowASCFileAddr { get => PCAN_Client.CAN_API.RecordingState.NowAscFileAddr; set => PCAN_Client.CAN_API.RecordingState.NowAscFileAddr = value; }
        public static string NowBLFFileAddr_str { get => PCAN_Client.CAN_API.RecordingState.NowBlfFileAddrStr; set => PCAN_Client.CAN_API.RecordingState.NowBlfFileAddrStr = value; }
        public static IntPtr NowBLFFileAddr = IntPtr.Zero;
        public static IntPtr NowBLFFileHandle = IntPtr.Zero;
        public static Boolean SaveFlag { get => PCAN_Client.CAN_API.RecordingState.SaveFlag; set => PCAN_Client.CAN_API.RecordingState.SaveFlag = value; }
        internal static Logging log = new Logging();

        internal static string SaveCSVPath = "";
        HighSpeedExcelWriter highSpeedExcelWriter = null;

        /// <summary>最近一次开始录制失败的原因（供界面提示；成功时为空）</summary>
        internal static string LastStartError = "";

        public Logging()
        {
            InitializeComponent();
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

        /// <summary>创建本轮录制文件。成功返回 true；失败写入 LastStartError 并保持未录制状态</summary>
        public bool newFile()
        {
            LastStartError = "";
            if (LoggingSet.SaveFileType.Equals("ASC"))
            {
                NowASCFileAddr = LoggingSet.FileAddress + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asc";
                try
                {
                    if (SaveCSVPath.Equals("") && LoggingSet.saveExcelFlag)
                    {
                        SaveCSVPath = Path.ChangeExtension(NowASCFileAddr, ".csv");
                        highSpeedExcelWriter = new HighSpeedExcelWriter(SaveCSVPath);
                    }

                    /*********************创建ASC文件**********************/
                    FileStream fileStream = new FileStream(NowASCFileAddr, FileMode.Append);
                    StreamWriter sw = new StreamWriter(fileStream);

                    string title = "date ";
                    title += GetWeek() + " " + DateTime.Now.ToString("yyyyMMdd HH:mm:ss ");
                    title += "\r\nbase hex  timestamps absolute \r\nno internal events logged \r\n// version 7.0.0";

                    sw.WriteLine(title);

                    sw.Close();
                    fileStream.Close();//关闭文件
                }
                catch (Exception ex)
                {
                    LastStartError = "创建ASC文件失败：" + NowASCFileAddr + "（" + ex.Message + "）";
                    AppLog.Error("[录制] " + LastStartError);
                    NowASCFileAddr = "";
                    SaveFlag = false;
                    return false;
                }
            }
            else
            {
                if (SaveCSVPath.Equals("") && LoggingSet.saveExcelFlag)
                {
                    SaveCSVPath = Path.ChangeExtension(NowBLFFileAddr_str, ".csv");
                    highSpeedExcelWriter = new HighSpeedExcelWriter(SaveCSVPath);
                }
                // BLF 文件创建/写队列/连续写线程/分割 由 Core Recorder 统一管理
                string path = CAN_Data.Recorder.StartBlf(LoggingSet.FileAddress, LoggingSet.SplitSizeBytes);
                if (string.IsNullOrEmpty(path))
                {
                    LastStartError = string.IsNullOrEmpty(CAN_Data.Recorder.LastStartError)
                        ? "创建BLF文件失败：" + LoggingSet.FileAddress
                        : CAN_Data.Recorder.LastStartError;
                    AppLog.Error("[录制] " + LastStartError);
                    SaveFlag = false;
                    return false;
                }
                NowBLFFileAddr_str = path;
            }

            SaveFlag = true;

            /*********************创建TXT文件**********************/
#if false
            NowFileAddr = filename;

            fileStream = new FileStream(filename, FileMode.Append);
            sw = new StreamWriter(fileStream);

            if (false == LoggingSet.Header_information.Equals(""))
            {
                sw.WriteLine(LoggingSet.Header_information);
            }

            sw.WriteLine(PreDATA_LogtitleValue + "\r\n");

            sw.Close();
            fileStream.Close();//关闭文件
#endif
            return true;
        }

        /// <summary>开始录制。返回 false 表示建文件失败（原因在 LastStartError），调用方须提示用户</summary>
        internal bool MngLogging_StartSaveData()
        {
            if (SaveFlag)
            {
                return true; /* 已在录制中：防止重复开始导致timer叠加、文件被另开 */
            }
            ResistanceSleep.PreventSleep(true); /* 阻止休眠 */
            if (!newFile())
            {
                ResistanceSleep.ResotreSleep();
                return false;
            }
            timer1.Interval = 1000 / DataLog.LoggingSet.Frequency;
            timer1.Start();
            // CSV信号快照保存（原在Logging_Load中启动，弹窗移除后Load不再触发，移至此）
            if (LoggingSet.saveExcelFlag)
            {
                timer2.Interval = (int)LoggingSet.saveExcelTime;
                timer2.Start();
            }
            // BLF模式：启动定时器周期性刷新数据到磁盘
            timer3.Interval = 500;
            timer3.Tick -= timer3_Tick;
            timer3.Tick += timer3_Tick;
            timer3.Start();
            LoggingSet.RecordStartTime = DateTime.Now;   // 状态栏"已录时长"起点
            AppLog.Info("[录制] 开始录制 类型=" + LoggingSet.SaveFileType + " 分段="
                + (LoggingSet.SplitSizeBytes > 0 ? (LoggingSet.SplitSizeBytes / 1048576L) + "MB" : "不分割") + " 文件="
                + (LoggingSet.SaveFileType_int == 1 ? NowBLFFileAddr_str : NowASCFileAddr));
            return true;
        }

        private void Logging_Load(object sender, EventArgs e)
        {
            UiTheme.StyleForm(this);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
#if false
            FileInfo fileInfo = new FileInfo(NowFileAddr);
            CAN_Data.CAN_Data.TslCanData tslCanData;

            if(false == CAN_Data.CAN_Data.ReveiveFlag)
            {
                return;
            }
            else
            {
                /* empty */
            }

            CAN_Data.CAN_Data.GetTslCanData(out tslCanData);

            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    file_size_Text.Text = (DataLog.LoggingSet.SaveSize - (fileInfo.Length / (1048576.0f))).ToString("F3");
                }));
            }

            PreLogging_w_SaveCnt++;
            if (50 < PreLogging_w_SaveCnt)
            {
                PreLogging_w_SaveCnt = 0;
                PreLogging_s_SaveValue += DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss.fff    ") +
                             tslCanData.LeftVolt.ToString("F2") + " mV     " +
                             tslCanData.RightVolt.ToString("F2") + " mV     " +
                             tslCanData.ModeVolt.ToString("F2") + " mV     " +
                             tslCanData.DefrostVolt.ToString("F2") + " mV    "
                             ;

                Log.saveLog(PreLogging_s_SaveValue, NowFileAddr);
                PreLogging_s_SaveValue = "";

                if (fileInfo.Length > LoggingSet.SaveSize * 1024 * 1024)
                {
                    newFile();
                }
            }
            else
            {
                PreLogging_s_SaveValue += DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss.fff    ") +
                             tslCanData.LeftVolt.ToString("F2") + " mV     " +
                             tslCanData.RightVolt.ToString("F2") + " mV     " +
                             tslCanData.ModeVolt.ToString("F2") + " mV     " +
                             tslCanData.DefrostVolt.ToString("F2") + " mV    " + "\r\n"
                             ;
            }
#endif
        }

        /// <summary>停止录制：直接清理（弹窗已移除，不再依赖关窗触发）</summary>
        internal static void StopRecording()
        {
            Logging instance = log;
            if (instance != null)
            {
                try
                {
                    instance.CleanupRecording();
                }
                catch (Exception ex)
                {
                    /* 清理异常不得让界面卡在"录制中"（否则开始录制按钮永久置灰） */
                    AppLog.Error("[录制] 停止录制清理异常: " + ex);
                }
                finally
                {
                    SaveFlag = false;
                    try { instance.Dispose(); } catch { }
                    log = null;
                }
            }
            else
            {
                SaveFlag = false; /* 防御：实例已不在但标志仍置位 */
            }
            // 录制状态变化，同步主界面及设置对话框按钮
            if (Main.main != null && !Main.main.IsDisposed)
            {
                Main.main.UpdateRecordButtonState();
            }
        }

        /// <summary>录制清理：停timer、复位标志、刷BLF队列关句柄、恢复休眠（FormClosing与主动停止共用）</summary>
        internal void CleanupRecording()
        {
            timer1.Stop();
            timer2.Stop();
            timer3.Stop();
            NowASCFileAddr = "";
            // 先停接收侧入队（SaveFlag=false），再刷盘关句柄：否则停止瞬间仍在到达的帧会写进已关闭的队列
            SaveFlag = false;
            try
            {
                // BLF 文件关闭/队列刷新由 Core Recorder 统一管理（内部串行化，停止与写线程不会并发碰句柄）
                CAN_Data.Recorder.StopBlf();
            }
            finally
            {
                ResistanceSleep.ResotreSleep();
                LoggingSet.RecordStartTime = DateTime.MinValue;
                AppLog.Info("[录制] 停止录制");
            }
        }

        private void Logging_FormClosing(object sender, FormClosingEventArgs e)
        {
            CleanupRecording();
            Logging.log = null;
            // 录制状态变化，同步主界面按钮（关闭软件过程中Main可能已销毁）
            if (Main.main != null && !Main.main.IsDisposed)
            {
                Main.main.UpdateRecordButtonState();
            }
        }

        private void timer3_Tick(object sender, EventArgs e)
        {
            if (LoggingSet.SaveFileType_int == 1 && SaveFlag)
            {
                CAN_API.CAN_API.FlushBLFBuffer();
            }
            // 状态栏：已录时长 + 当前文件大小（对话框未打开/未录制时内部直接返回）
            if (SaveFlag && LoggingSet.loggingSet != null && !LoggingSet.loggingSet.IsDisposed)
            {
                LoggingSet.loggingSet.UpdateRecordingStatus();
            }
        }

        public class HighSpeedExcelWriter
        {
            private string filePathTemp;
            // 用于写入CSV文件的StreamWriter对象
            private StreamWriter _csvWriter;

            public HighSpeedExcelWriter(string filePath)
            {
                filePathTemp = filePath;
                // 设置表头
                SetHeaders();

                Console.WriteLine($"Excel初始化完成，准备写入数据...");
            }

            /// <summary>已配置通道数>1时表头加通道前缀（多通道同ID/同名列可区分）</summary>
            private static bool MultiChannelMode
            {
                get
                {
                    int configured = 0;
                    foreach (var ch in BaseParamter.BusChannels) if (ch.IsConfigured) configured++;
                    return configured > 1;
                }
            }

            private void SetHeaders()
            {
                try
                {
                    // 创建新的文件流，模式为追加（FileMode.Append），如果文件存在则追加，不存在则创建
                    // 使用UTF-8编码，并写入BOM以支持Excel正确显示中文
                    _csvWriter = new StreamWriter(new FileStream(filePathTemp, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                    _csvWriter.Write("采集时间,");
                    bool multiCh = MultiChannelMode;
                    foreach (var msg in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        string prefix = multiCh ? $"CH{BaseParamter.GetChannelOfMessage(msg)} " : "";
                        foreach (var signal in msg.signals)
                        {
                            // 在写入表头之前，先写入枚举关系作为注释
                            _csvWriter.Write($"{prefix}{msg.messageName} -> {signal.signalName}({signal.Comment}),");
                        }
                    }
                    _csvWriter.WriteLine();
                    _csvWriter.Flush(); // 立即将缓冲区的数据写入文件
                    _csvWriter.Close();
                }
                catch (Exception ex)
                {
                    // 构造失败时 _csvWriter 仍为 null，这里再抛会顶掉"开始录制"（原实现即如此）
                    try { if (_csvWriter != null) _csvWriter.Close(); } catch { }
                    MessageBox.Show($"初始化CSV文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // 批量写入单行数据（高性能版本）
            public void WriteSignalRow(DateTime dateTime)
            {
                try
                {
                    // 创建新的文件流，模式为追加（FileMode.Append），如果文件存在则追加，不存在则创建
                    // 使用UTF-8编码，并写入BOM以支持Excel正确显示中文
                    _csvWriter = new StreamWriter(new FileStream(filePathTemp, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                    _csvWriter.Write($"{dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff")},");
                    foreach (var msg in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        foreach (var signal in msg.signals)
                        {
                            _csvWriter.Write($"{signal.result.ToString("F2")},");
                        }
                    }
                    _csvWriter.WriteLine();
                    _csvWriter.Flush(); // 立即将缓冲区的数据写入文件
                    _csvWriter.Close();
                }
                catch (Exception ex)
                {
                    try { if (_csvWriter != null) _csvWriter.Close(); } catch { }
                    MessageBox.Show($"初始化CSV文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            internal void Dispose()
            {
                
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            Task task = new Task(() =>
            {
                if (LoggingSet.saveExcelFlag)
                {
                    highSpeedExcelWriter.WriteSignalRow(DateTime.Now);
                }
            });
            task.Start();
        }
    }
}
