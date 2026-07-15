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
        public static string NowASCFileAddr = "";
        public static string NowBLFFileAddr_str = "";
        public static IntPtr NowBLFFileAddr = IntPtr.Zero;
        public static IntPtr NowBLFFileHandle = IntPtr.Zero;
        public static Boolean SaveFlag = false;
        internal static Logging log = new Logging();

        internal static string SaveCSVPath = "";
        HighSpeedExcelWriter highSpeedExcelWriter = null;

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

        internal void SafeFileSizeRefresh(bool isOverflow, string size)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool, string>(SafeFileSizeRefresh), isOverflow, size);
            }
            else
            {
                try
                {
                    FileSizeRefresh(isOverflow, size);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"文件大小更新失败: {ex.Message}");
                }
            }
        }
        internal void FileSizeRefresh(bool newfile, string str)
        {
            if (newfile)
            {
                newFile();
            }
            else
            {
                /* empty */
            }
            file_size_Text.Text = str;
            //this.BeginInvoke(new EventHandler(delegate
            //{
            //    file_size_Text.Text = str;
            //}));
        }

        public void newFile()
        {
            if (LoggingSet.SaveFileType.Equals("ASC"))
            {
                NowASCFileAddr = LoggingSet.FileAddress + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asc";
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
            else
            {
                // 关闭旧的BLF句柄（文件溢出时创建新文件）
                if (NowBLFFileHandle != IntPtr.Zero)
                {
                    // 只刷队列数据，不检查文件大小（防止递归触发newFile）
                    Log.ContinuousWriteWorker();
                    BLFAPI.BLCloseHandle(NowBLFFileHandle);
                    NowBLFFileHandle = IntPtr.Zero;
                }
                Log.SetTimeUs(0);
                NowBLFFileAddr_str = LoggingSet.FileAddress + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".blf";
                if (SaveCSVPath.Equals("") && LoggingSet.saveExcelFlag)
                {
                    SaveCSVPath = Path.ChangeExtension(NowBLFFileAddr_str, ".csv");
                    highSpeedExcelWriter = new HighSpeedExcelWriter(SaveCSVPath);
                }
                /*********************创建BLF文件**********************/
                IntPtr blfHandle = BLFAPI.BLCreateFileW(NowBLFFileAddr_str, GENERIC.GENERIC_WRITE);
                if (blfHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"创建BLF文件失败: {NowBLFFileAddr_str}");
                    SaveFlag = false;
                    return;
                }
                BLFAPI.BLSetApplication(blfHandle, BLAppID.BL_APPID_CANALYZER, 3, 0, 1);
                int timeSize = Marshal.SizeOf<SYSTEMTIME>();
                IntPtr timePtr = Marshal.AllocHGlobal(timeSize);
                SYSTEMTIME systemTime = new SYSTEMTIME();
                DateTime now = DateTime.Now;
                systemTime.wYear = (UInt16)now.Year;
                systemTime.wMonth = (UInt16)now.Month;
                systemTime.wDay = (UInt16)now.Day;
                systemTime.wHour = (UInt16)now.Hour;
                systemTime.wMinute = (UInt16)now.Minute;
                systemTime.wSecond = (UInt16)now.Second;
                Marshal.StructureToPtr(systemTime, timePtr, false);
                BLFAPI.BLSetMeasurementStartTime(blfHandle, timePtr);
                Marshal.FreeHGlobal(timePtr);
                BLFAPI.BLSetWriteOptions(blfHandle, 0, 0); // 减少内部缓存，确保及时写入
                NowBLFFileHandle = blfHandle;
                System.Diagnostics.Debug.WriteLine($"BLF文件创建成功: {NowBLFFileAddr_str}, handle={NowBLFFileHandle}");
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
        }

        internal void MngLogging_StartSaveData()
        {
            ResistanceSleep.PreventSleep(true); /* 阻止休眠 */
            newFile();
            timer1.Interval = 1000 / DataLog.LoggingSet.Frequency;
            timer1.Start();
            // BLF模式：启动定时器周期性刷新数据到磁盘
            timer3.Interval = 500;
            timer3.Tick += timer3_Tick;
            timer3.Start();
        }

        private void Logging_Load(object sender, EventArgs e)
        {
            if (LoggingSet.saveExcelFlag)
            {
                timer2.Interval = (int)LoggingSet.saveExcelTime;
                timer2.Start();
            }
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

        private void Logging_FormClosing(object sender, FormClosingEventArgs e)
        {
            timer1.Stop();
            timer2.Stop();
            timer3.Stop();
            NowASCFileAddr = "";
            // 关闭BLF文件句柄并刷新剩余数据
            if (NowBLFFileHandle != IntPtr.Zero)
            {
                Log.ContinuousWriteWorker();
                BLFAPI.BLCloseHandle(NowBLFFileHandle);
                NowBLFFileHandle = IntPtr.Zero;
            }
            Logging.log = null;
            SaveFlag = false;
            ResistanceSleep.ResotreSleep();
        }

        private void timer3_Tick(object sender, EventArgs e)
        {
            if (LoggingSet.SaveFileType_int == 1 && SaveFlag)
            {
                CAN_API.CAN_API.FlushBLFBuffer();
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

            private void SetHeaders()
            {
                try
                {
                    // 创建新的文件流，模式为追加（FileMode.Append），如果文件存在则追加，不存在则创建
                    // 使用UTF-8编码，并写入BOM以支持Excel正确显示中文
                    _csvWriter = new StreamWriter(new FileStream(filePathTemp, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
                    _csvWriter.Write("采集时间,");
                    foreach (var msg in BaseParamter.dbcHelper.dbcFile.messages)
                    {
                        foreach (var signal in msg.signals)
                        {
                            // 在写入表头之前，先写入枚举关系作为注释
                            _csvWriter.Write($"{msg.messageName} -> {signal.signalName}({signal.Comment}),");
                        }
                    }
                    _csvWriter.WriteLine();
                    _csvWriter.Flush(); // 立即将缓冲区的数据写入文件
                    _csvWriter.Close();
                }
                catch (Exception ex)
                {
                    _csvWriter.Close();
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
                    _csvWriter.Close();
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
