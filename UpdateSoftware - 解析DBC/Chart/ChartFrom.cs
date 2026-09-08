using PCAN_Client.CAN_Data;
using PCAN_Client.DataLog;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml.Serialization;
using Microsoft.VisualBasic;

namespace PCAN_Client
{
    public partial class ChartFrom : Form
    {
        public class DataUpdatedEventArgs : EventArgs
        {
            public double CurrentTime { get; private set; }

            public DataUpdatedEventArgs(double currentTime)
            {
                CurrentTime = currentTime;
            }
        }


        public List<ChannelData> Channels { get; private set; }

        /// <summary>
        /// CAN总线通道配置列表（多通道模式）
        /// </summary>
        public List<CanBusChannel> BusChannels { get { return _busChannels; } }

        /// <summary>
        /// 已加载的日志文件路径列表（供报告生成获取文件名）
        /// </summary>
        public List<string> LogFilePaths { get { return _logFilePaths; } }

        public bool RealTimeDataSta = true;
        public bool RunStatus = false;
        public event EventHandler<DataUpdatedEventArgs> DataUpdated;
        public DateTime startTime = DateTime.Now;
        private double _currentTime;
        private MultiMessageCANScheduler multiChartFromScheduler = new MultiMessageCANScheduler();
        private ChartControl _chartControl = (ChartControl)null;
        //private SimulationManager _simulationManager;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnClear;
        private Button _btnAddChannel;
        private Button _btnAutoScroll;
        private Button _btnLoadFile;
        private ToolStrip _topToolStrip;
        private ToolStrip _analysisToolStrip;   // 第二行工具栏(信号与报告)
        private ToolStripButton _btnShowMainForm;   // 唤出报文列表窗口(Main)
        private ToolStripButton _toolLoadLog, _toolStart, _toolStop, _toolShowAll, _toolAutoScroll, _toolClear;
        private ToolStripDropDownButton _toolSignalGroup;   // 信号组(加载/保存)下拉
        private ToolStripDropDownButton _toolModeToggle;    // 实时/报文模式选择下拉
        private ToolStripComboBox _toolSpeedComboBox;
        private ToolStripComboBox _toolLineWidthComboBox;   // 全局线宽选择(对所有信号同时生效)
        private int _globalLineWidth = 2;                   // 当前全局线宽(新通道默认继承)
        private ToolStripComboBox _toolDotSizeComboBox;     // 全局数据点大小选择(对所有信号同时生效)
        private int _globalDotSize = 4;                     // 当前全局数据点直径px(新通道默认继承)
        private ToolStripDropDownButton _toolMore;
        private ToolStripTextBox _txtStartTime;
        private ToolStripTextBox _txtEndTime;
        private int _streamingThresholdMB = 150;            // 流式阈值(MB,超过该值走流式读取)
        private ToolStripButton _btnShowData;
        internal HashSet<byte> _selectedChannels = null; // null=全部通道
        private ProgressBar _progressBar;
        private Label _progressLabel;  // 进度条百分比文字标签
        private ComboBox _speedComboBox;  // 播放倍速选择
        private Label _speedLabel;        // 倍速标签
        private DataGridView _channelGrid;
        private int _channelDragRowIndex = -1;
        private Rectangle _channelDragBoxFromMouseDown = Rectangle.Empty;
        private int _channelDropInsertIndex = -1;
        private GroupBox _controlGroup;
        private Label _statusLabel;
        private Label _modeIndicatorLabel;
        private Panel panel;
        private Panel _leftStatusPanel;
        private TextBox _filePathTextBox;
        private SplitContainer splitContainer;
        private System.ComponentModel.IContainer components;
        private ComboBox _presetComboBox;
        private Button _btnSavePreset;
        private Button _btnLoadPreset;
        private Button _btnDeletePreset;
        private Button _btnRemoveAll;
        private Button _btnShowAll;
        private RadioButton _radioRealTime;
        private RadioButton _radioFileData;
        private Button _btnSaveBlf;
        // 工具栏 实时/报文模式切换按钮
        // 在工具栏初始化中已创建 _toolModeToggle
        private ContextMenuStrip _channelContextMenu;
        private Label _presetLabel;
        private Panel _measurementPanel, _measurementHeaderPanel;
        private DataGridView _measurementGrid;
        private Button _btnToggleMeasurement;
        private Label _measurementTitleLabel;
        private readonly object _lockObj = new object();
        private System.Windows.Forms.Timer _loadingRefreshTimer;
        private double _loadingBatchMaxTime = 0;
        private int _loadingProcessedCount = 0;
        private int _lastPopupShownCount = -1;       // 上次弹窗显示的数量，避免闪烁
        private ToolStripMenuItem menuItemSelectAll;
        private ToolStripMenuItem menuItemDeselectAll;
        private ToolStripMenuItem menuItemDelete;
        private ToolStripMenuItem menuItemAddSignal;
        private ToolStripMenuItem menuItemClearAll;
        private bool _isLoadingFile = false;
        // 文件回放相关字段
        private List<CanRawMessage> _rawMessages;                // 原始报文存储（加载时只存不解析）
        private bool _streamingMode = false;                     // 流式模式（大文件>150MB时不加载到内存）
        private string _streamingFilePath;                       // 流式模式下的文件路径（保留兼容性）
        private List<string> _logFilePaths = new List<string>(); // 报文文件路径列表（勾选的）
        
        /// <summary>
        /// 保存日志文件路径列表到设置（包含勾选状态）
        /// </summary>
        private void SaveLogFilePaths()
        {
            var lines = new List<string>();
            foreach (var entry in _logFileEntries)
            {
                lines.Add(entry.Enabled ? "*" + entry.Path : entry.Path);
            }
            PCAN_Client.Properties.Settings.Default["LogFilePaths"] = string.Join("\n", lines);
            PCAN_Client.Properties.Settings.Default.Save();
        }
        
        private List<LogFileEntry> _logFileEntries = new List<LogFileEntry>(); // 完整的日志文件列表（含勾选状态）
        private IEnumerator<CanRawMessage> _streamingEnumerator; // 流式播放时的文件枚举器
        private CanRawMessage _streamingPendingMessage;          // 流式模式下等待下一Tick处理的报文
        private long _streamingTotalCount;                       // 流式模式下报文总数（仅用于状态显示）
        private double _fileMaxTime = 0;                         // 文件中的最大时间戳
        private bool _isFileMode = false;                        // 是否为文件模式（已加载文件数据）
        private volatile bool _cancelPlayback;                     // 停止播放标记（流式模式）
        private volatile bool _cacheWhileStreaming;               // 内存模式首次播放：边读边缓存
        private string _playbackModeText = "实时数据";            // 播放时确定的模式文本（用于指示器显示）
        private Color _playbackModeColor = Color.Gray;            // 播放时确定的模式颜色
        private System.Windows.Forms.Timer _playbackTimer;       // 回放定时器
        private double _playbackSpeed = 0;                       // 播放倍速（0=最快）
        private int _playbackRawIndex = 0;                       // _rawMessages 中的当前播放位置
        private double _playbackStartWallTime = 0;               // 开始播放时的 wall clock（秒）
        private Dictionary<int, Dictionary<uint, List<(CAN_Data.Signal signal, int channelIndex)>>> _canIdSignalMap; // 播放用查找表（外层key=BusChannelIndex，内层key=CAN ID）
        // CAN总线解析通道（全局唯一数据源，详见 BaseParamter.BusChannels）
        private List<CanBusChannel> _busChannels => BaseParamter.BusChannels;
        private ToolStripButton _btnBusConfig; // 工具栏"通道配置"按钮
        // 实时数据BLF保存相关
        private static List<CanRawMessage> _realtimeRawMessages = new List<CanRawMessage>();
        private static readonly object _realtimeRawLock = new object();
        /// <summary>实时原始报文内存上限（帧）：约 500000×64B≈32MB，超限时批量淘汰最老帧</summary>
        private const int RealtimeRawMaxFrames = 500000;
        /// <summary>超限时一次裁剪到 Max-Batch 的余量，避免每帧 O(n) 头部移除</summary>
        private const int RealtimeRawTrimBatch = 10000;
        /// <summary>曾触发过淘汰（导出时提示数据窗口不完整）</summary>
        private static bool _realtimeRawTrimmed = false;
        private string _currentSignalGroupPath = "";        // 当前信号组文件路径（用于覆盖保存）
        private Form _loadingPopup = null;                  // 加载报文时的弹窗

        // 流式读取时记录的最新的5w帧报文（停止/播放完毕后显示到Main.cs）
        private List<CanRawMessage> _streamingRecordedFrames = new List<CanRawMessage>();
        private const int MAX_RECORDED_FRAMES = 50000;
        private const int RECORDED_TRIM_COUNT = 10000;
        private readonly object _streamingRecordedLock = new object();

        private void RecordStreamingFrame(CanRawMessage msg)
        {
            lock (_streamingRecordedLock)
            {
                _streamingRecordedFrames.Add(msg);
                // 批量裁剪，避免频繁移动
                if (_streamingRecordedFrames.Count >= MAX_RECORDED_FRAMES + RECORDED_TRIM_COUNT)
                    _streamingRecordedFrames.RemoveRange(0, RECORDED_TRIM_COUNT);
            }
        }

        private void ShowStreamingRecordedFrames()
        {
            List<CanRawMessage> frames;
            lock (_streamingRecordedLock)
            {
                if (_streamingRecordedFrames.Count == 0) return;
                frames = new List<CanRawMessage>(_streamingRecordedFrames);
                _streamingRecordedFrames.Clear();
            }
            // 必须在UI线程调用（内部会操作UI控件）
            Main.main.DisplayFramesInScrollMode(frames);
        }

        /// <summary>
        /// 记录实时接收的原始CAN报文（用于ChartFrom"保存BLF"导出），channel为逻辑通道号（写入BLF mChannel）。
        /// 调用方已保证：BLF录制开启且该通道勾选录制的帧不调用本方法（数据已落盘）；本方法仅累积需要内存副本的帧。
        /// 内存累积有上限保护（超限淘汰最老帧，导出时提示窗口）
        /// </summary>
        internal static void RecordRealtimeRawMessage(uint canId, byte[] data, byte channel = 1)
        {
            if (Main.chartFromShow == null || Main.chartFromShow._isFileMode || !Main.chartFromShow.RunStatus)
                return;
            double timestamp = (DateTime.Now - Main.chartFromShow.startTime).TotalSeconds;
            byte[] dataCopy = new byte[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            lock (_realtimeRawLock)
            {
                // 上限保护：超限时批量裁剪最老帧（裁剪到 Max-Batch 留余量，均摊O(1)），并标记供导出提示
                if (_realtimeRawMessages.Count >= RealtimeRawMaxFrames)
                {
                    int removeCount = _realtimeRawMessages.Count - (RealtimeRawMaxFrames - RealtimeRawTrimBatch);
                    if (removeCount > 0) _realtimeRawMessages.RemoveRange(0, removeCount);
                    _realtimeRawTrimmed = true;
                }
                _realtimeRawMessages.Add(new CanRawMessage
                {
                    CanId = canId,
                    Data = dataCopy,
                    TimeStampSeconds = (float)timestamp,
                    Channel = channel
                });
            }
        }
        public ChartFrom()
        {
            InitializeComponent();
            // 应用图标:读取exe内嵌图标(csproj ApplicationIcon)
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            // _chartControl 创建和配置（放在这里避免设计器无法序列化自定义控件）
            this._chartControl = new PCAN_Client.ChartControl();
            this._chartControl.BackColor = System.Drawing.Color.White;
            this._chartControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this._chartControl.Name = "_chartControl";
            this._chartControl.TabIndex = 0;
            this.splitContainer.Panel2.Controls.Add(this._chartControl);
            // 拖拽加载补全:图表区/分隔面板也接受文件拖入(窗体级AllowDrop被子控件遮挡,拖上去不触发)
            _chartControl.AllowDrop = true;
            _chartControl.DragEnter += ChartFrom_DragEnter;
            _chartControl.DragDrop += ChartFrom_DragDrop;
            splitContainer.Panel1.AllowDrop = true;
            splitContainer.Panel1.DragEnter += ChartFrom_DragEnter;
            splitContainer.Panel1.DragDrop += ChartFrom_DragDrop;
            splitContainer.Panel2.AllowDrop = true;
            splitContainer.Panel2.DragEnter += ChartFrom_DragEnter;
            splitContainer.Panel2.DragDrop += ChartFrom_DragDrop;
            InitializeToolbarBindings();
            InitReportToolbar();
            InitializeChannelGrid();
            // "报文列表"按钮:唤出被隐藏的Main报文窗口(插入工具栏最前)
            _btnShowMainForm = new ToolStripButton("报文列表");
            _btnShowMainForm.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            _btnShowMainForm.Image = ToolbarIcons.Get("list");
            _btnShowMainForm.ToolTipText = "显示报文列表窗口";
            _btnShowMainForm.Click += _btnShowMainForm_Click;
            _topToolStrip.Items.Insert(0, _btnShowMainForm);
            _topToolStrip.Items.Insert(1, new ToolStripSeparator());
            // "取消补采"按钮:补采信号数据时显示在状态栏右上(默认隐藏)
            _btnCancelBackfill = new Button
            {
                Text = "取消补采",
                Size = new Size(72, 20),
                Location = new Point(210, 5),
                Visible = false
            };
            _btnCancelBackfill.Click += (s, e) => { _backfillCancel = true; _btnCancelBackfill.Enabled = false; };
            _leftStatusPanel.Controls.Add(_btnCancelBackfill);
            // 默认播放速度为"最快"
            if (_speedComboBox.Items.Count > 0)
                _speedComboBox.SelectedIndex = 0;
            this.KeyPreview = true;
            this.KeyDown += ChartFrom_KeyDown;
            this._loadingRefreshTimer = new System.Windows.Forms.Timer();
            this._loadingRefreshTimer.Interval = 100;
            this._loadingRefreshTimer.Tick += _loadingRefreshTimer_Tick;
            // 初始化回放定时器
            this._playbackTimer = new System.Windows.Forms.Timer();
            this._playbackTimer.Interval = 20;  // 50fps
            this._playbackTimer.Tick += _playbackTimer_Tick;
            // 加载已保存的流式阈值
            int savedThreshold = PCAN_Client.Properties.Settings.Default.StreamingThresholdMB;
            _streamingThresholdMB = (savedThreshold >= 50) ? savedThreshold : 150;
            this._chartControl.OnAutoScrollChanged += (enabled) =>
            {
                string text = enabled ? "停止滑动" : "自动滑动";
                if (InvokeRequired)
                    Invoke(new Action(() => { _btnAutoScroll.Text = text; _toolAutoScroll.Checked = enabled; }));
                else
                    { _btnAutoScroll.Text = text; _toolAutoScroll.Checked = enabled; }
            };
            // 通道颜色变化时，更新通道列表中的颜色显示
            this._chartControl.OnChannelColorChanged += (channel) =>
            {
                if (InvokeRequired)
                    Invoke(new Action(() => UpdateChannelGridColor(channel)));
                else
                    UpdateChannelGridColor(channel);
            };
            // 右键点击曲线 → 显示该时刻前后各5000条报文
            this._chartControl.OnShowMessagesAtTime += (xValue) =>
            {
                ShowMessagesAtTime(xValue);
            };
            // 测量线变化时更新通道网格的当前值和差值
            this._chartControl.OnMeasureLinesChanged += () =>
            {
                if (InvokeRequired)
                    Invoke(new Action(() =>
                    {
                        UpdateChannelGridValues();
                        // 有两条竖线时，自动填充时间输入框
                        var x1 = _chartControl.MeasureLineX1;
                        var x2 = _chartControl.MeasureLineX2;
                        if (x1.HasValue && x2.HasValue)
                        {
                            double t1 = Math.Min(x1.Value, x2.Value);
                            double t2 = Math.Max(x1.Value, x2.Value);
                            _txtStartTime.Text = t1.ToString("F1");
                            _txtEndTime.Text = t2.ToString("F1");
                        }
                    }));
                else
                {
                    UpdateChannelGridValues();
                    var x1 = _chartControl.MeasureLineX1;
                    var x2 = _chartControl.MeasureLineX2;
                    if (x1.HasValue && x2.HasValue)
                    {
                        double t1 = Math.Min(x1.Value, x2.Value);
                        double t2 = Math.Max(x1.Value, x2.Value);
                        _txtStartTime.Text = t1.ToString("F1");
                        _txtEndTime.Text = t2.ToString("F1");
                    }
                }
            };
            // 绘图区选中通道(Y轴区域点击) → 左侧信号列表同步选中该行
            this._chartControl.OnChannelClicked += (channel) =>
            {
                if (IsDisposed) return; // 窗体销毁中丢弃回调,避免访问已释放控件
                if (InvokeRequired)
                    Invoke(new Action(() => SelectChannelGridRow(channel)));
                else
                    SelectChannelGridRow(channel);
            };
            // 信号列表选中变化(Ctrl/Shift多选) → 绘图区对应曲线面板背景高亮
            this._channelGrid.SelectionChanged += _channelGrid_SelectionChanged;

            // 加载上次保存的日志文件路径列表（包含勾选状态）
            var savedLogPaths = (string)PCAN_Client.Properties.Settings.Default["LogFilePaths"];
            if (!string.IsNullOrEmpty(savedLogPaths))
            {
                _logFileEntries = new List<LogFileEntry>();
                _logFilePaths = new List<string>();
                foreach (var line in savedLogPaths.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("*"))
                    {
                        var path = line.Substring(1);
                        _logFileEntries.Add(new LogFileEntry { Path = path, Enabled = true });
                        _logFilePaths.Add(path);
                    }
                    else
                    {
                        _logFileEntries.Add(new LogFileEntry { Path = line, Enabled = false });
                    }
                }
                
                // 如果有启用的文件路径，检查文件大小决定是否使用流式模式
                if (_logFilePaths.Count > 0)
                {
                    long totalSizeMB = 0;
                    foreach (var path in _logFilePaths)
                    {
                        if (File.Exists(path))
                        {
                            totalSizeMB += new FileInfo(path).Length / (1024 * 1024);
                        }
                    }
                    
                    int thresholdMB = GetStreamingThresholdMB();
                    _streamingMode = (totalSizeMB > thresholdMB);
                    _isFileMode = true;
                    if (_logFilePaths.Count > 0)
                        _streamingFilePath = _logFilePaths[0]; // 保留兼容性
                }
            }

            // 加载上次保存的图表模式（实时/报文）:报文模式走完整切换,保证状态栏/模式指示器/内部标志一致
            // 注意必须放在日志路径恢复之后,否则 _isFileMode 判断不到已勾选的报文文件
            var savedMode = (string)PCAN_Client.Properties.Settings.Default["ChartMode"];
            if (savedMode == "FileData")
            {
                SwitchToFileModeInternal();
            }
            else
            {
                RealTimeDataSta = true;
                // 实时模式：清除上方日志路径恢复时置位的文件模式标志——否则界面显示实时、点开始却按报文模式跑并断开硬件
                _isFileMode = false;
            }

            if (RealTimeDataSta)
            {
                _radioRealTime.Checked = true;
                _radioFileData.Checked = false;
            }
            else
            {
                _radioRealTime.Checked = false;
                _radioFileData.Checked = true;
            }
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._controlGroup = new System.Windows.Forms.GroupBox();
            this._btnStart = new System.Windows.Forms.Button();
            this._btnStop = new System.Windows.Forms.Button();
            this._btnClear = new System.Windows.Forms.Button();
            this._btnShowAll = new System.Windows.Forms.Button();
            this._btnAddChannel = new System.Windows.Forms.Button();
            this._btnAutoScroll = new System.Windows.Forms.Button();
            this._presetLabel = new System.Windows.Forms.Label();
            this._presetComboBox = new System.Windows.Forms.ComboBox();
            this._btnSavePreset = new System.Windows.Forms.Button();
            this._btnLoadPreset = new System.Windows.Forms.Button();
            this._btnRemoveAll = new System.Windows.Forms.Button();
            this._btnDeletePreset = new System.Windows.Forms.Button();
            this._radioRealTime = new System.Windows.Forms.RadioButton();
            this._radioFileData = new System.Windows.Forms.RadioButton();
            this._btnLoadFile = new System.Windows.Forms.Button();
            this._topToolStrip = new System.Windows.Forms.ToolStrip();
            this._analysisToolStrip = new System.Windows.Forms.ToolStrip();
            this._toolSignalGroup = new System.Windows.Forms.ToolStripDropDownButton();
            this._toolLoadLog = new System.Windows.Forms.ToolStripButton();
            this._toolModeToggle = new System.Windows.Forms.ToolStripDropDownButton();
            this._toolStart = new System.Windows.Forms.ToolStripButton();
            this._toolStop = new System.Windows.Forms.ToolStripButton();
            this._toolSpeedComboBox = new System.Windows.Forms.ToolStripComboBox();
            this._toolLineWidthComboBox = new System.Windows.Forms.ToolStripComboBox();
            this._toolDotSizeComboBox = new System.Windows.Forms.ToolStripComboBox();
            this._toolShowAll = new System.Windows.Forms.ToolStripButton();
            this._toolAutoScroll = new System.Windows.Forms.ToolStripButton();
            this._toolClear = new System.Windows.Forms.ToolStripButton();
            this._toolMore = new System.Windows.Forms.ToolStripDropDownButton();
            this._txtStartTime = new System.Windows.Forms.ToolStripTextBox();
            this._txtEndTime = new System.Windows.Forms.ToolStripTextBox();
            this._btnShowData = new System.Windows.Forms.ToolStripButton();
            this._btnBusConfig = new System.Windows.Forms.ToolStripButton();
            this._btnSaveBlf = new System.Windows.Forms.Button();
            this._progressBar = new System.Windows.Forms.ProgressBar();
            this._statusLabel = new System.Windows.Forms.Label();
            this._channelGrid = new System.Windows.Forms.DataGridView();
            this._channelContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuItemAddSignal = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemSelectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemDeselectAll = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemClearAll = new System.Windows.Forms.ToolStripMenuItem();
            this.panel = new System.Windows.Forms.Panel();
            this._leftStatusPanel = new System.Windows.Forms.Panel();
            this._filePathTextBox = new System.Windows.Forms.TextBox();
            this._progressLabel = new System.Windows.Forms.Label();
            this._speedLabel = new System.Windows.Forms.Label();
            this._speedComboBox = new System.Windows.Forms.ComboBox();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this._measurementPanel = new System.Windows.Forms.Panel();
            this._measurementGrid = new System.Windows.Forms.DataGridView();
            this._btnToggleMeasurement = new System.Windows.Forms.Button();
            this._measurementHeaderPanel = new System.Windows.Forms.Panel();
            this._measurementTitleLabel = new System.Windows.Forms.Label();
            this._controlGroup.SuspendLayout();
            this._topToolStrip.SuspendLayout();
            this._analysisToolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._channelGrid)).BeginInit();
            this._channelContextMenu.SuspendLayout();
            this.panel.SuspendLayout();
            this._leftStatusPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._measurementGrid)).BeginInit();
            this.SuspendLayout();
            // 
            // _controlGroup
            // 
            this._controlGroup.Controls.Add(this._btnStart);
            this._controlGroup.Controls.Add(this._btnStop);
            this._controlGroup.Controls.Add(this._btnClear);
            this._controlGroup.Controls.Add(this._btnShowAll);
            this._controlGroup.Controls.Add(this._btnAddChannel);
            this._controlGroup.Controls.Add(this._btnAutoScroll);
            this._controlGroup.Controls.Add(this._presetLabel);
            this._controlGroup.Controls.Add(this._presetComboBox);
            this._controlGroup.Controls.Add(this._btnSavePreset);
            this._controlGroup.Controls.Add(this._btnLoadPreset);
            this._controlGroup.Controls.Add(this._btnRemoveAll);
            this._controlGroup.Controls.Add(this._btnDeletePreset);
            this._controlGroup.Controls.Add(this._radioRealTime);
            this._controlGroup.Controls.Add(this._radioFileData);
            this._controlGroup.Location = new System.Drawing.Point(10, 10);
            this._controlGroup.Name = "_controlGroup";
            this._controlGroup.Size = new System.Drawing.Size(287, 320);
            this._controlGroup.TabIndex = 0;
            this._controlGroup.TabStop = false;
            this._controlGroup.Text = "控制面板";
            // 
            // _btnStart
            // 
            this._btnStart.Location = new System.Drawing.Point(5, 30);
            this._btnStart.Name = "_btnStart";
            this._btnStart.Size = new System.Drawing.Size(105, 30);
            this._btnStart.TabIndex = 0;
            this._btnStart.Text = "开始";
            this._btnStart.Click += new System.EventHandler(this._btnStart_Click);
            // 
            // _btnStop
            // 
            this._btnStop.Enabled = false;
            this._btnStop.Location = new System.Drawing.Point(116, 30);
            this._btnStop.Name = "_btnStop";
            this._btnStop.Size = new System.Drawing.Size(105, 30);
            this._btnStop.TabIndex = 1;
            this._btnStop.Text = "停止";
            this._btnStop.Click += new System.EventHandler(this._btnStop_Click);
            // 
            // _btnClear
            // 
            this._btnClear.Location = new System.Drawing.Point(5, 70);
            this._btnClear.Name = "_btnClear";
            this._btnClear.Size = new System.Drawing.Size(105, 30);
            this._btnClear.TabIndex = 2;
            this._btnClear.Text = "清除";
            this._btnClear.Click += new System.EventHandler(this._btnClear_Click);
            // 
            // _btnShowAll
            // 
            this._btnShowAll.Location = new System.Drawing.Point(116, 70);
            this._btnShowAll.Name = "_btnShowAll";
            this._btnShowAll.Size = new System.Drawing.Size(105, 30);
            this._btnShowAll.TabIndex = 3;
            this._btnShowAll.Text = "全部显示";
            this._btnShowAll.Click += new System.EventHandler(this._btnShowAll_Click);
            // 
            // _btnAddChannel
            // 
            this._btnAddChannel.Location = new System.Drawing.Point(2, 260);
            this._btnAddChannel.Name = "_btnAddChannel";
            this._btnAddChannel.Size = new System.Drawing.Size(276, 30);
            this._btnAddChannel.TabIndex = 4;
            this._btnAddChannel.Text = "添加通道";
            this._btnAddChannel.Click += new System.EventHandler(this._btnAddChannel_Click);
            // 
            // _btnAutoScroll
            // 
            this._btnAutoScroll.Location = new System.Drawing.Point(6, 106);
            this._btnAutoScroll.Name = "_btnAutoScroll";
            this._btnAutoScroll.Size = new System.Drawing.Size(275, 30);
            this._btnAutoScroll.TabIndex = 6;
            this._btnAutoScroll.Text = "自动滑动";
            this._btnAutoScroll.Click += new System.EventHandler(this._btnAutoScroll_Click);
            // 
            // _presetLabel
            // 
            this._presetLabel.AutoSize = true;
            this._presetLabel.Location = new System.Drawing.Point(2, 170);
            this._presetLabel.Name = "_presetLabel";
            this._presetLabel.Size = new System.Drawing.Size(59, 12);
            this._presetLabel.TabIndex = 7;
            this._presetLabel.Text = "预设列表:";
            // 
            // _presetComboBox
            // 
            this._presetComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._presetComboBox.FormattingEnabled = true;
            this._presetComboBox.Location = new System.Drawing.Point(2, 185);
            this._presetComboBox.Name = "_presetComboBox";
            this._presetComboBox.Size = new System.Drawing.Size(190, 20);
            this._presetComboBox.TabIndex = 8;
            this._presetComboBox.SelectedIndexChanged += new System.EventHandler(this._presetComboBox_SelectedIndexChanged);
            // 
            // _btnSavePreset
            // 
            this._btnSavePreset.Location = new System.Drawing.Point(197, 183);
            this._btnSavePreset.Name = "_btnSavePreset";
            this._btnSavePreset.Size = new System.Drawing.Size(80, 23);
            this._btnSavePreset.TabIndex = 9;
            this._btnSavePreset.Text = "保存";
            this._btnSavePreset.Click += new System.EventHandler(this._btnSavePreset_Click);
            // 
            // _btnLoadPreset
            // 
            this._btnLoadPreset.Location = new System.Drawing.Point(197, 210);
            this._btnLoadPreset.Name = "_btnLoadPreset";
            this._btnLoadPreset.Size = new System.Drawing.Size(80, 23);
            this._btnLoadPreset.TabIndex = 10;
            this._btnLoadPreset.Text = "加载";
            this._btnLoadPreset.Click += new System.EventHandler(this._btnLoadPreset_Click);
            // 
            // _btnRemoveAll
            // 
            this._btnRemoveAll.Location = new System.Drawing.Point(2, 208);
            this._btnRemoveAll.Name = "_btnRemoveAll";
            this._btnRemoveAll.Size = new System.Drawing.Size(190, 23);
            this._btnRemoveAll.TabIndex = 11;
            this._btnRemoveAll.Text = "删除全部";
            this._btnRemoveAll.Click += new System.EventHandler(this._btnRemoveAll_Click);
            // 
            // _btnDeletePreset
            // 
            this._btnDeletePreset.Location = new System.Drawing.Point(2, 235);
            this._btnDeletePreset.Name = "_btnDeletePreset";
            this._btnDeletePreset.Size = new System.Drawing.Size(190, 23);
            this._btnDeletePreset.TabIndex = 14;
            this._btnDeletePreset.Text = "删除信号组";
            this._btnDeletePreset.Click += new System.EventHandler(this._btnDeletePreset_Click);
            // 
            // _radioRealTime
            // 
            this._radioRealTime.AutoSize = true;
            this._radioRealTime.Location = new System.Drawing.Point(2, 140);
            this._radioRealTime.Name = "_radioRealTime";
            this._radioRealTime.Size = new System.Drawing.Size(71, 16);
            this._radioRealTime.TabIndex = 7;
            this._radioRealTime.TabStop = true;
            this._radioRealTime.Text = "实时数据";
            this._radioRealTime.UseVisualStyleBackColor = true;
            // 
            // _radioFileData
            // 
            this._radioFileData.AutoSize = true;
            this._radioFileData.Location = new System.Drawing.Point(100, 140);
            this._radioFileData.Name = "_radioFileData";
            this._radioFileData.Size = new System.Drawing.Size(71, 16);
            this._radioFileData.TabIndex = 8;
            this._radioFileData.TabStop = true;
            this._radioFileData.Text = "报文数据";
            this._radioFileData.UseVisualStyleBackColor = true;
            //
            // _btnLoadFile
            // 
            this._btnLoadFile.Location = new System.Drawing.Point(11, 645);
            this._btnLoadFile.Name = "_btnLoadFile";
            this._btnLoadFile.Size = new System.Drawing.Size(277, 28);
            this._btnLoadFile.TabIndex = 12;
            this._btnLoadFile.Text = "加载文件...";
            this._btnLoadFile.Click += new System.EventHandler(this._btnLoadFile_Click);
            // 
            // _topToolStrip (行1: 数据与采集)
            //
            this._topToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this._topToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._topToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._toolLoadLog,
            this._btnBusConfig,
            this._toolSignalGroup,
            new System.Windows.Forms.ToolStripSeparator(),
            this._toolModeToggle,
            this._toolStart,
            this._toolStop,
            this._toolSpeedComboBox,
            new System.Windows.Forms.ToolStripSeparator(),
            this._toolShowAll,
            this._toolAutoScroll,
            this._toolClear,
            new System.Windows.Forms.ToolStripSeparator(),
            this._toolMore});
            this._topToolStrip.Location = new System.Drawing.Point(0, 0);
            this._topToolStrip.Name = "_topToolStrip";
            this._topToolStrip.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this._topToolStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this._topToolStrip.Size = new System.Drawing.Size(1480, 37);
            this._topToolStrip.TabIndex = 1;
            //
            // _analysisToolStrip (行2: 信号与报告; 工况/报告组由 InitReportToolbar 追加)
            //
            this._analysisToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this._analysisToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._analysisToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            new System.Windows.Forms.ToolStripLabel("线宽:"),
            this._toolLineWidthComboBox,
            new System.Windows.Forms.ToolStripLabel("点:"),
            this._toolDotSizeComboBox,
            new System.Windows.Forms.ToolStripSeparator(),
            new System.Windows.Forms.ToolStripLabel("区间:"),
            this._txtStartTime,
            new System.Windows.Forms.ToolStripLabel("~"),
            this._txtEndTime,
            this._btnShowData});
            this._analysisToolStrip.Location = new System.Drawing.Point(0, 37);
            this._analysisToolStrip.Name = "_analysisToolStrip";
            this._analysisToolStrip.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this._analysisToolStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this._analysisToolStrip.Size = new System.Drawing.Size(1480, 35);
            this._analysisToolStrip.TabIndex = 2;
            //
            // _toolSignalGroup
            //
            this._toolSignalGroup.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolSignalGroup.Image = ToolbarIcons.Get("bookmark");
            this._toolSignalGroup.Name = "_toolSignalGroup";
            this._toolSignalGroup.Size = new System.Drawing.Size(72, 22);
            this._toolSignalGroup.Text = "信号组";
            this._toolSignalGroup.ToolTipText = "加载/保存信号组";
            //
            // _btnBusConfig
            //
            this._btnBusConfig.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._btnBusConfig.Image = ToolbarIcons.Get("gear");
            this._btnBusConfig.Name = "_btnBusConfig";
            this._btnBusConfig.Size = new System.Drawing.Size(72, 22);
            this._btnBusConfig.Text = "通道管理";
            this._btnBusConfig.ToolTipText = "配置CAN解析通道（多路CAN总线）";
            this._btnBusConfig.Click += new System.EventHandler(this._btnBusConfig_Click);
            //
            // _toolLoadLog
            //
            this._toolLoadLog.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolLoadLog.Image = ToolbarIcons.Get("folder");
            this._toolLoadLog.Name = "_toolLoadLog";
            this._toolLoadLog.Size = new System.Drawing.Size(60, 22);
            this._toolLoadLog.Text = "加载报文";
            this._toolLoadLog.ToolTipText = "加载BLF/BIN/ASC文件";
            this._toolLoadLog.Click += new System.EventHandler(this._btnLoadFile_Click);
            //
            // _toolModeToggle
            //
            this._toolModeToggle.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolModeToggle.Image = ToolbarIcons.Get("swap");
            this._toolModeToggle.Name = "_toolModeToggle";
            this._toolModeToggle.Size = new System.Drawing.Size(60, 22);
            this._toolModeToggle.Text = "实时数据";
            this._toolModeToggle.ToolTipText = "选择数据来源(实时采集/报文回放)";
            //
            // _toolStart
            //
            this._toolStart.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolStart.Image = ToolbarIcons.Get("play");
            this._toolStart.Name = "_toolStart";
            this._toolStart.Size = new System.Drawing.Size(36, 22);
            this._toolStart.Text = "开始";
            this._toolStart.ToolTipText = "开始播放或绘制";
            this._toolStart.Click += new System.EventHandler(this._btnStart_Click);
            //
            // _toolStop
            //
            this._toolStop.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolStop.Image = ToolbarIcons.Get("stop");
            this._toolStop.Name = "_toolStop";
            this._toolStop.Size = new System.Drawing.Size(36, 22);
            this._toolStop.Text = "停止";
            this._toolStop.ToolTipText = "停止播放或绘制";
            this._toolStop.Click += new System.EventHandler(this._btnStop_Click);
            //
            // _toolSpeedComboBox
            //
            this._toolSpeedComboBox.AutoSize = false;
            this._toolSpeedComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._toolSpeedComboBox.Name = "_toolSpeedComboBox";
            this._toolSpeedComboBox.Size = new System.Drawing.Size(80, 25);
            this._toolSpeedComboBox.ToolTipText = "播放倍速";
            this._toolSpeedComboBox.SelectedIndexChanged += new System.EventHandler(this._toolSpeedComboBox_SelectedIndexChanged);
            //
            // _toolLineWidthComboBox
            //
            this._toolLineWidthComboBox.AutoSize = false;
            this._toolLineWidthComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._toolLineWidthComboBox.Name = "_toolLineWidthComboBox";
            this._toolLineWidthComboBox.Size = new System.Drawing.Size(64, 25);
            this._toolLineWidthComboBox.ToolTipText = "曲线线宽(对所有信号同时生效)";
            this._toolLineWidthComboBox.SelectedIndexChanged += new System.EventHandler(this._toolLineWidthComboBox_SelectedIndexChanged);
            //
            // _toolDotSizeComboBox
            //
            this._toolDotSizeComboBox.AutoSize = false;
            this._toolDotSizeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._toolDotSizeComboBox.Name = "_toolDotSizeComboBox";
            this._toolDotSizeComboBox.Size = new System.Drawing.Size(56, 25);
            this._toolDotSizeComboBox.ToolTipText = "数据点大小(对所有信号同时生效)";
            this._toolDotSizeComboBox.SelectedIndexChanged += new System.EventHandler(this._toolDotSizeComboBox_SelectedIndexChanged);
            //
            // _toolShowAll
            //
            this._toolShowAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolShowAll.Image = ToolbarIcons.Get("fit");
            this._toolShowAll.Name = "_toolShowAll";
            this._toolShowAll.Size = new System.Drawing.Size(60, 22);
            this._toolShowAll.Text = "全部显示";
            this._toolShowAll.ToolTipText = "显示全部时间范围";
            this._toolShowAll.Click += new System.EventHandler(this._btnShowAll_Click);
            //
            // _toolAutoScroll
            //
            this._toolAutoScroll.CheckOnClick = true;
            this._toolAutoScroll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolAutoScroll.Image = ToolbarIcons.Get("scroll");
            this._toolAutoScroll.Name = "_toolAutoScroll";
            this._toolAutoScroll.Size = new System.Drawing.Size(60, 22);
            this._toolAutoScroll.Text = "自动滑动";
            this._toolAutoScroll.ToolTipText = "切换自动滑动(按下为开启)";
            this._toolAutoScroll.Click += new System.EventHandler(this._btnAutoScroll_Click);
            //
            // _toolClear
            //
            this._toolClear.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolClear.Image = ToolbarIcons.Get("clear");
            this._toolClear.Name = "_toolClear";
            this._toolClear.Size = new System.Drawing.Size(36, 22);
            this._toolClear.Text = "清除";
            this._toolClear.ToolTipText = "清除当前曲线数据";
            this._toolClear.Click += new System.EventHandler(this._btnClear_Click);
            //
            // _toolMore
            //
            this._toolMore.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._toolMore.Image = ToolbarIcons.Get("dots");
            this._toolMore.Name = "_toolMore";
            this._toolMore.Size = new System.Drawing.Size(45, 22);
            this._toolMore.Text = "更多";
            this._toolMore.ToolTipText = "更多功能入口";
            //
            // _txtStartTime
            //
            this._txtStartTime.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this._txtStartTime.Name = "_txtStartTime";
            this._txtStartTime.Size = new System.Drawing.Size(60, 25);
            this._txtStartTime.Text = "0";
            this._txtStartTime.ToolTipText = "起始时间（秒）";
            //
            // _txtEndTime
            //
            this._txtEndTime.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this._txtEndTime.Name = "_txtEndTime";
            this._txtEndTime.Size = new System.Drawing.Size(60, 25);
            this._txtEndTime.Text = "0";
            this._txtEndTime.ToolTipText = "结束时间（秒）";
            //
            // _btnShowData
            //
            this._btnShowData.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText;
            this._btnShowData.Image = ToolbarIcons.Get("chart");
            this._btnShowData.Name = "_btnShowData";
            this._btnShowData.Size = new System.Drawing.Size(60, 22);
            this._btnShowData.Text = "数据显示";
            this._btnShowData.ToolTipText = "显示指定时间范围内的数据";
            this._btnShowData.Click += new System.EventHandler(this._btnShowData_Click);
            // 
            // _btnSaveBlf
            // 
            this._btnSaveBlf.Location = new System.Drawing.Point(207, 673);
            this._btnSaveBlf.Name = "_btnSaveBlf";
            this._btnSaveBlf.Size = new System.Drawing.Size(81, 28);
            this._btnSaveBlf.TabIndex = 17;
            this._btnSaveBlf.Text = "保存BLF";
            this._btnSaveBlf.Click += new System.EventHandler(this._btnSaveBlf_Click);
            // 
            // _progressBar
            // 
            this._progressBar.Location = new System.Drawing.Point(6, 58);
            this._progressBar.Name = "_progressBar";
            this._progressBar.Size = new System.Drawing.Size(288, 18);
            this._progressBar.TabIndex = 13;
            this._progressBar.Visible = false;
            // 
            // _statusLabel
            // 
            this._statusLabel.AutoSize = true;
            this._statusLabel.ForeColor = System.Drawing.Color.Red;
            this._statusLabel.Location = new System.Drawing.Point(6, 8);
            this._statusLabel.Name = "_statusLabel";
            this._statusLabel.Size = new System.Drawing.Size(77, 12);
            this._statusLabel.TabIndex = 8;
            this._statusLabel.Text = "状态: 已停止";
            // 
            // _modeIndicatorLabel
            // 
            this._modeIndicatorLabel = new System.Windows.Forms.Label();
            this._modeIndicatorLabel.AutoSize = true;
            this._modeIndicatorLabel.ForeColor = System.Drawing.Color.Gray;
            this._modeIndicatorLabel.Location = new System.Drawing.Point(6, 26);
            this._modeIndicatorLabel.Name = "_modeIndicatorLabel";
            this._modeIndicatorLabel.Size = new System.Drawing.Size(65, 12);
            this._modeIndicatorLabel.TabIndex = 20;
            this._modeIndicatorLabel.Text = "模式: 实时数据";
            // 
            // _channelGrid
            // 
            this._channelGrid.AllowDrop = true;
            this._channelGrid.AllowUserToAddRows = false;
            this._channelGrid.AllowUserToDeleteRows = false;
            this._channelGrid.AllowUserToResizeRows = false;
            this._channelGrid.BackgroundColor = System.Drawing.Color.White;
            this._channelGrid.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this._channelGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this._channelGrid.ContextMenuStrip = this._channelContextMenu;
            this._channelGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this._channelGrid.Location = new System.Drawing.Point(0, 0);
            this._channelGrid.MultiSelect = true;
            this._channelGrid.Name = "_channelGrid";
            this._channelGrid.RowHeadersVisible = false;
            this._channelGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this._channelGrid.Size = new System.Drawing.Size(290, 662);
            this._channelGrid.TabIndex = 0;
            this._channelGrid.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this._channelGrid_CellClick);
            this._channelGrid.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this._channelGrid_CellContentClick);
            this._channelGrid.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this._channelGrid_CellDoubleClick);
            this._channelGrid.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this._channelGrid_CellMouseDown);
            this._channelGrid.CellPainting += new System.Windows.Forms.DataGridViewCellPaintingEventHandler(this._channelGrid_CellPainting);
            this._channelGrid.DragDrop += new System.Windows.Forms.DragEventHandler(this._channelGrid_DragDrop);
            this._channelGrid.DragOver += new System.Windows.Forms.DragEventHandler(this._channelGrid_DragOver);
            this._channelGrid.DragLeave += new System.EventHandler(this._channelGrid_DragLeave);
            this._channelGrid.Paint += new System.Windows.Forms.PaintEventHandler(this._channelGrid_Paint);
            this._channelGrid.KeyDown += new System.Windows.Forms.KeyEventHandler(this._channelGrid_KeyDown);
            this._channelGrid.MouseDown += new System.Windows.Forms.MouseEventHandler(this._channelGrid_MouseDown);
            this._channelGrid.MouseMove += new System.Windows.Forms.MouseEventHandler(this._channelGrid_MouseMove);
            // 
            // _channelContextMenu
            // 
            this._channelContextMenu.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._channelContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAddSignal,
            this.menuItemDelete,
            this.menuItemSelectAll,
            this.menuItemDeselectAll,
            this.menuItemClearAll});
            this._channelContextMenu.Name = "_channelContextMenu";
            this._channelContextMenu.Size = new System.Drawing.Size(125, 114);
            // 
            // menuItemAddSignal
            // 
            this.menuItemAddSignal.Name = "menuItemAddSignal";
            this.menuItemAddSignal.Size = new System.Drawing.Size(124, 22);
            this.menuItemAddSignal.Text = "添加信号";
            this.menuItemAddSignal.Click += new System.EventHandler(this._btnAddChannel_Click);
            // 
            // menuItemDelete
            // 
            this.menuItemDelete.Name = "menuItemDelete";
            this.menuItemDelete.Size = new System.Drawing.Size(124, 22);
            this.menuItemDelete.Text = "删除信号";
            this.menuItemDelete.Click += new System.EventHandler(this._channelContextMenu_Delete_Click);
            // 
            // menuItemSelectAll
            // 
            this.menuItemSelectAll.Name = "menuItemSelectAll";
            this.menuItemSelectAll.Size = new System.Drawing.Size(124, 22);
            this.menuItemSelectAll.Text = "全选显示";
            this.menuItemSelectAll.Click += new System.EventHandler(this._channelContextMenu_SelectAll_Click);
            // 
            // menuItemDeselectAll
            // 
            this.menuItemDeselectAll.Name = "menuItemDeselectAll";
            this.menuItemDeselectAll.Size = new System.Drawing.Size(124, 22);
            this.menuItemDeselectAll.Text = "全部隐藏";
            this.menuItemDeselectAll.Click += new System.EventHandler(this._channelContextMenu_DeselectAll_Click);
            // 
            // menuItemClearAll
            // 
            this.menuItemClearAll.Name = "menuItemClearAll";
            this.menuItemClearAll.Size = new System.Drawing.Size(124, 22);
            this.menuItemClearAll.Text = "清空信号";
            this.menuItemClearAll.Click += new System.EventHandler(this._btnRemoveAll_Click);
            // 
            // panel
            // 
            this.panel.BackColor = System.Drawing.SystemColors.Control;
            this.panel.Controls.Add(this._channelGrid);
            this.panel.Controls.Add(this._leftStatusPanel);
            this.panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel.Location = new System.Drawing.Point(0, 0);
            this.panel.Name = "panel";
            this.panel.Size = new System.Drawing.Size(290, 758);
            this.panel.TabIndex = 0;
            // 
            // _leftStatusPanel
            // 
            this._leftStatusPanel.BackColor = System.Drawing.SystemColors.ControlLight;
            this._leftStatusPanel.Controls.Add(this._statusLabel);
            this._leftStatusPanel.Controls.Add(this._modeIndicatorLabel);
            this._leftStatusPanel.Controls.Add(this._filePathTextBox);
            this._leftStatusPanel.Controls.Add(this._progressBar);
            this._leftStatusPanel.Controls.Add(this._progressLabel);
            this._leftStatusPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this._leftStatusPanel.Location = new System.Drawing.Point(0, 662);
            this._leftStatusPanel.Name = "_leftStatusPanel";
            this._leftStatusPanel.Padding = new System.Windows.Forms.Padding(6);
            this._leftStatusPanel.Size = new System.Drawing.Size(290, 96);
            this._leftStatusPanel.TabIndex = 1;
            // 
            // _filePathTextBox
            // 
            this._filePathTextBox.Location = new System.Drawing.Point(6, 46);
            this._filePathTextBox.Name = "_filePathTextBox";
            this._filePathTextBox.ReadOnly = true;
            this._filePathTextBox.Size = new System.Drawing.Size(288, 21);
            this._filePathTextBox.TabIndex = 19;
            // 
            // _progressLabel
            // 
            this._progressLabel.AutoSize = true;
            this._progressLabel.ForeColor = System.Drawing.Color.Blue;
            this._progressLabel.Location = new System.Drawing.Point(258, 73);
            this._progressLabel.Name = "_progressLabel";
            this._progressLabel.Size = new System.Drawing.Size(17, 12);
            this._progressLabel.TabIndex = 14;
            this._progressLabel.Text = "0%";
            this._progressLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this._progressLabel.Visible = false;
            // 
            // _speedLabel
            // 
            this._speedLabel.AutoSize = true;
            this._speedLabel.Location = new System.Drawing.Point(12, 681);
            this._speedLabel.Name = "_speedLabel";
            this._speedLabel.Size = new System.Drawing.Size(53, 12);
            this._speedLabel.TabIndex = 15;
            this._speedLabel.Text = "播放倍速";
            // 
            // _speedComboBox
            // 
            this._speedComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._speedComboBox.Items.AddRange(new object[] {
            "最快",
            "1x",
            "2x",
            "5x",
            "10x",
            "20x",
            "50x",
            "100x"});
            this._speedComboBox.Location = new System.Drawing.Point(65, 678);
            this._speedComboBox.Name = "_speedComboBox";
            this._speedComboBox.Size = new System.Drawing.Size(51, 20);
            this._speedComboBox.TabIndex = 16;
            this._speedComboBox.SelectedIndexChanged += new System.EventHandler(this._speedComboBox_SelectedIndexChanged);
            // 
            // splitContainer
            // 
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Location = new System.Drawing.Point(0, 37);
            this.splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            this.splitContainer.Panel1.Controls.Add(this.panel);
            this.splitContainer.Size = new System.Drawing.Size(1480, 758);
            this.splitContainer.SplitterDistance = 290;
            this.splitContainer.SplitterWidth = 5;
            this.splitContainer.TabIndex = 0;
            // 
            // _measurementPanel
            // 
            this._measurementPanel.Location = new System.Drawing.Point(0, 0);
            this._measurementPanel.Name = "_measurementPanel";
            this._measurementPanel.Size = new System.Drawing.Size(200, 100);
            this._measurementPanel.TabIndex = 0;
            // 
            // _measurementGrid
            // 
            this._measurementGrid.Location = new System.Drawing.Point(0, 0);
            this._measurementGrid.Name = "_measurementGrid";
            this._measurementGrid.Size = new System.Drawing.Size(240, 150);
            this._measurementGrid.TabIndex = 0;
            // 
            // _btnToggleMeasurement
            // 
            this._btnToggleMeasurement.Location = new System.Drawing.Point(0, 0);
            this._btnToggleMeasurement.Name = "_btnToggleMeasurement";
            this._btnToggleMeasurement.Size = new System.Drawing.Size(75, 23);
            this._btnToggleMeasurement.TabIndex = 0;
            // 
            // _measurementHeaderPanel
            // 
            this._measurementHeaderPanel.Location = new System.Drawing.Point(0, 0);
            this._measurementHeaderPanel.Name = "_measurementHeaderPanel";
            this._measurementHeaderPanel.Size = new System.Drawing.Size(200, 100);
            this._measurementHeaderPanel.TabIndex = 0;
            // 
            // _measurementTitleLabel
            // 
            this._measurementTitleLabel.Location = new System.Drawing.Point(0, 0);
            this._measurementTitleLabel.Name = "_measurementTitleLabel";
            this._measurementTitleLabel.Size = new System.Drawing.Size(100, 23);
            this._measurementTitleLabel.TabIndex = 0;
            // 
            // ChartFrom
            // 
            this.AllowDrop = true;
            this.ClientSize = new System.Drawing.Size(1480, 795);
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this._analysisToolStrip);
            this.Controls.Add(this._topToolStrip);
            this.Name = "ChartFrom";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CANInsight · 曲线绘制工具";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ChartFrom_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.ChartFrom_FormClosed);
            this.Load += new System.EventHandler(this.ChartFrom_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.ChartFrom_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.ChartFrom_DragEnter);
            this._controlGroup.ResumeLayout(false);
            this._controlGroup.PerformLayout();
            this._topToolStrip.ResumeLayout(false);
            this._topToolStrip.PerformLayout();
            this._analysisToolStrip.ResumeLayout(false);
            this._analysisToolStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._channelGrid)).EndInit();
            this._channelContextMenu.ResumeLayout(false);
            this.panel.ResumeLayout(false);
            this._leftStatusPanel.ResumeLayout(false);
            this._leftStatusPanel.PerformLayout();
            this.splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._measurementGrid)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void InitializeToolbarBindings()
        {
            foreach (var item in _speedComboBox.Items)
            {
                _toolSpeedComboBox.Items.Add(item);
            }

            // 全局线宽:1~5px,默认2px,选中即应用到所有通道
            foreach (int w in new int[] { 1, 2, 3, 4, 5 })
            {
                _toolLineWidthComboBox.Items.Add(w + " px");
            }
            _toolLineWidthComboBox.SelectedIndex = _globalLineWidth - 1;

            // 全局数据点大小:1~5px,默认4px,选中即应用到所有通道
            foreach (int d in new int[] { 1, 2, 3, 4, 5 })
            {
                _toolDotSizeComboBox.Items.Add(d + " px");
            }
            _toolDotSizeComboBox.SelectedIndex = _globalDotSize - 1;

            // 信号组下拉:加载/保存成对
            _toolSignalGroup.DropDownItems.Add(new ToolStripMenuItem("加载信号组", null, _btnLoadPreset_Click));
            _toolSignalGroup.DropDownItems.Add(new ToolStripMenuItem("保存信号组", null, _btnSavePreset_Click));

            // 模式下拉:实时/报文单选,当前项打勾
            var menuRealtime = new ToolStripMenuItem("实时数据", null, (s, e) => SwitchToRealTimeData());
            var menuFileData = new ToolStripMenuItem("报文数据", null, (s, e) => SwitchToFileData());
            menuRealtime.Name = "menuRealtime";
            menuFileData.Name = "menuFileData";
            _toolModeToggle.DropDownItems.Add(menuRealtime);
            _toolModeToggle.DropDownItems.Add(menuFileData);

            _toolMore.DropDownItems.Add(new ToolStripMenuItem("保存BLF", null, _btnSaveBlf_Click));
            _toolMore.DropDownItems.Add(new ToolStripMenuItem("流式阈值设置…", null, _menuStreamingThreshold_Click));
            _toolMore.DropDownItems.Add(new ToolStripSeparator());
            _toolMore.DropDownItems.Add(new ToolStripMenuItem("删除全部", null, _btnRemoveAll_Click));
            _toolMore.DropDownItems.Add(new ToolStripSeparator());
            _toolMore.DropDownItems.Add(new ToolStripMenuItem("版本信息", null, _menuVersionInfo_Click));

            SyncToolbarStateFromLegacyControls();
        }

        /// <summary>同步模式下拉的显示文本与勾选状态</summary>
        private void UpdateModeToggleDisplay()
        {
            _toolModeToggle.Text = RealTimeDataSta ? "实时数据" : "报文数据";
            foreach (ToolStripItem item in _toolModeToggle.DropDownItems)
            {
                var menu = item as ToolStripMenuItem;
                if (menu == null) continue;
                menu.Checked = RealTimeDataSta ? menu.Name == "menuRealtime" : menu.Name == "menuFileData";
            }
        }

        /// <summary>更多→版本信息:显示当前版本号、发布日期，并提供主动检查更新入口</summary>
        private void _menuVersionInfo_Click(object sender, EventArgs e)
        {
            var parts = BaseParamter.softVersion.Split(new[] { "--" }, StringSplitOptions.None);
            string ver = parts[0].Trim();
            string date = parts.Length > 1 ? parts[1].Trim() : "未知";

            using (var dlg = new Form())
            {
                dlg.Text = "版本信息";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ClientSize = new Size(330, 145);

                var label = new Label
                {
                    Text = "当前版本：" + ver + "\r\n发布日期：" + date,
                    AutoSize = true,
                    Location = new Point(20, 20)
                };
                var buttonCheck = new Button
                {
                    Text = "检查更新",
                    Size = new Size(100, 30),
                    Location = new Point(100, 92)
                };
                var buttonClose = new Button
                {
                    Text = "关闭",
                    DialogResult = DialogResult.Cancel,
                    Size = new Size(80, 30),
                    Location = new Point(215, 92)
                };
                buttonCheck.Click += (s, args) =>
                {
                    dlg.Close();
                    PCAN_Client.util.SelfUpdater.CheckNow(this);
                };

                dlg.Controls.Add(label);
                dlg.Controls.Add(buttonCheck);
                dlg.Controls.Add(buttonClose);
                dlg.CancelButton = buttonClose;
                dlg.ShowDialog(this);
            }
        }


        /// <summary>更多→流式阈值设置:弹小对话框修改并持久化(下次加载生效)</summary>
        private void _menuStreamingThreshold_Click(object sender, EventArgs e)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "流式阈值设置";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ClientSize = new Size(280, 100);
                var lbl = new Label { Text = "报文文件超过该大小时使用流式读取(MB, ≥50):", Left = 12, Top = 14, AutoSize = true };
                var txt = new TextBox { Text = _streamingThresholdMB.ToString(), Left = 12, Top = 36, Width = 100 };
                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 118, Top = 64, Width = 70 };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 198, Top = 64, Width = 70 };
                dlg.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                int val;
                if (int.TryParse(txt.Text, out val) && val >= 50)
                {
                    _streamingThresholdMB = val;
                    PCAN_Client.Properties.Settings.Default.StreamingThresholdMB = val;
                    PCAN_Client.Properties.Settings.Default.Save();
                    _statusLabel.Text = $"状态: 流式阈值已设置为 {val} MB (下次加载生效)";
                    _statusLabel.ForeColor = Color.Green;
                }
                else
                {
                    MessageBox.Show("请输入 ≥50 的整数", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void SyncToolbarStateFromLegacyControls()
        {
            if (_toolStart == null)
            {
                return;
            }

            _toolStart.Enabled = _btnStart.Enabled;
            _toolStop.Enabled = _btnStop.Enabled;
            _toolClear.Enabled = _btnClear.Enabled;
            _toolShowAll.Enabled = _btnShowAll.Enabled;
            _toolLoadLog.Enabled = _btnLoadFile.Enabled;
            _toolSignalGroup.Enabled = true;
            _toolAutoScroll.Enabled = _btnAutoScroll.Enabled;
            // 自动滑动:Checked 反映实际开关状态(按钮文本固定为"自动滑动")
            _toolAutoScroll.Checked = _chartControl != null && _chartControl.IsAutoScrollEnabled();
            // 开始/停止的 Enabled 互斥即运行状态反馈(图标绿/红语义色辅助)
            UpdateModeToggleDisplay();
            _speedComboBox.Enabled = _btnStart.Enabled && !_isLoadingFile;
            _toolSpeedComboBox.Enabled = _btnStart.Enabled && !_isLoadingFile;

            if (_speedComboBox.SelectedIndex >= 0 &&
                _speedComboBox.SelectedIndex < _toolSpeedComboBox.Items.Count &&
                _toolSpeedComboBox.SelectedIndex != _speedComboBox.SelectedIndex)
            {
                _toolSpeedComboBox.SelectedIndex = _speedComboBox.SelectedIndex;
            }
        }

        private void InitializeChannelGrid()
        {
            _channelGrid.Columns.Clear();
            _channelGrid.AutoGenerateColumns = false;
            _channelGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _channelGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
            _channelGrid.EnableHeadersVisualStyles = false;
            _channelGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.Gainsboro;
            _channelGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _channelGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            _channelGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
            _channelGrid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);
            _channelGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

            DataGridViewCheckBoxColumn visibleColumn = new DataGridViewCheckBoxColumn();
            visibleColumn.Name = "Visible";
            visibleColumn.HeaderText = "";
            visibleColumn.Width = 28;
            visibleColumn.MinimumWidth = 28;
            visibleColumn.Resizable = DataGridViewTriState.False;
            visibleColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

            DataGridViewTextBoxColumn colorColumn = new DataGridViewTextBoxColumn();
            colorColumn.Name = "Color";
            colorColumn.HeaderText = "";
            colorColumn.Width = 28;
            colorColumn.MinimumWidth = 28;
            colorColumn.Resizable = DataGridViewTriState.False;
            colorColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colorColumn.ReadOnly = true;
            colorColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn signalColumn = new DataGridViewTextBoxColumn();
            signalColumn.Name = "Signal";
            signalColumn.HeaderText = "信号";
            signalColumn.MinimumWidth = 140;
            signalColumn.FillWeight = 54F;
            signalColumn.ReadOnly = true;
            signalColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn valueColumn = new DataGridViewTextBoxColumn();
            valueColumn.Name = "Value";
            valueColumn.HeaderText = "当前值";
            valueColumn.MinimumWidth = 90;
            valueColumn.FillWeight = 27F;
            valueColumn.ReadOnly = true;
            valueColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            valueColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            DataGridViewTextBoxColumn deltaColumn = new DataGridViewTextBoxColumn();
            deltaColumn.Name = "Delta";
            deltaColumn.HeaderText = "差值";
            deltaColumn.MinimumWidth = 72;
            deltaColumn.FillWeight = 19F;
            deltaColumn.ReadOnly = true;
            deltaColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            deltaColumn.Visible = false;
            deltaColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            _channelGrid.Columns.Add(visibleColumn);
            _channelGrid.Columns.Add(colorColumn);
            _channelGrid.Columns.Add(signalColumn);
            _channelGrid.Columns.Add(valueColumn);
            _channelGrid.Columns.Add(deltaColumn);
        }

        private void PopulateChannelGrid()
        {
            _channelGrid.Rows.Clear();
            int displayIndex = 0;  // 仅统计显示在列表中的通道(占位符报告专用通道不进列表)
            for (int i = 0; i < Channels.Count; i++)
            {
                var channel = Channels[i];
                if (channel.IsReportOnly) continue;  // 占位符报告专用通道:参与采集/计算,但不显示在信号列表
                // 超过30条以后的信号在列表中默认取消勾选，但保留在Channels中
                bool isChecked = channel.Visible && displayIndex < 30;
                if (displayIndex >= 30 && channel.Visible)
                {
                    channel.Visible = false;
                }
                int rowIndex = _channelGrid.Rows.Add(isChecked, string.Empty, GetChannelDisplayName(channel), string.Empty, string.Empty);
                _channelGrid.Rows[rowIndex].Tag = channel;
                _channelGrid.Rows[rowIndex].Cells["Color"].ToolTipText = "点击修改颜色";
                _channelGrid.Rows[rowIndex].Cells["Signal"].Style.ForeColor = SystemColors.ControlText;
                displayIndex++;
            }
            // 同步图表可见性（前30条显示，其余隐藏）
            _chartControl.SetChannels(Channels);
            // 初始化当前值/差值显示
            UpdateChannelGridValues();
        }

        private string GetChannelDisplayName(ChannelData channel)
        {
            if (channel == null)
                return string.Empty;
            string unit = (channel.Unit ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(unit) || unit == "-" || unit == "\"\"")
                return channel.Name ?? string.Empty;
            return string.Format("{0} [{1}]", channel.Name ?? string.Empty, unit);
        }

        private ChannelData GetChannelFromRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _channelGrid.Rows.Count)
                return null;
            return _channelGrid.Rows[rowIndex].Tag as ChannelData;
        }

        private void SetChannelRowChecked(int rowIndex, bool isChecked)
        {
            _channelGrid.Rows[rowIndex].Cells["Visible"].Value = isChecked;
            ChannelData channel = GetChannelFromRow(rowIndex);
            if (channel != null)
            {
                channel.Visible = isChecked;
                _chartControl.SmartInvalidate();
            }
        }

        private bool GetChannelRowChecked(int rowIndex)
        {
            object value = _channelGrid.Rows[rowIndex].Cells["Visible"].Value;
            return value is bool b && b;
        }

        private void SelectChannelGridRow(ChannelData targetChannel)
        {
            if (targetChannel == null || _channelGrid.Rows.Count == 0)
                return;
            foreach (DataGridViewRow row in _channelGrid.Rows)
            {
                if (!ReferenceEquals(row.Tag, targetChannel))
                    continue;
                _channelGrid.ClearSelection();
                row.Selected = true;
                if (row.Cells["Signal"] != null)
                    _channelGrid.CurrentCell = row.Cells["Signal"];
                if (row.Index >= 0 && row.Index < _channelGrid.RowCount)
                    _channelGrid.FirstDisplayedScrollingRowIndex = row.Index;
                return;
            }
        }

        private void SimulationManager_DataUpdated(object sender, DataUpdatedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateChart(e.CurrentTime)));
            }
            else
            {
                UpdateChart(e.CurrentTime);
            }
        }

        private void UpdateChart(double currentTime)
        {
            _chartControl.AutoScrollToLatest(currentTime);
            _chartControl.SmartInvalidate(); // 使用智能重绘，通过帧率控制降低CPU
        }

        private void UpdateChannelGridValues()
        {
            double? redLineX = _chartControl.MeasureLineX1;
            double? greenLineX = _chartControl.MeasureLineX2;
            bool hasTwoLines = redLineX.HasValue && greenLineX.HasValue && greenLineX.Value != redLineX.Value;

            // 有两条竖线时显示差值列并加宽通道网格，否则隐藏差值列并缩窄
            if (_channelGrid.Columns.Contains("Delta"))
            {
                if (_channelGrid.Columns["Delta"].Visible != hasTwoLines)
                {
                    _channelGrid.Columns["Delta"].Visible = hasTwoLines;
                    splitContainer.SplitterDistance = hasTwoLines ? 380 : 290;
                }
            }
            else if (hasTwoLines)
            {
                splitContainer.SplitterDistance = 380;
            }

            for (int i = 0; i < _channelGrid.Rows.Count; i++)
            {
                var row = _channelGrid.Rows[i];
                var channel = row.Tag as ChannelData;
                if (channel == null) continue;

                double currentValue;
                if (redLineX.HasValue)
                {
                    var nearest = _chartControl.FindNearestPoint(channel, redLineX.Value);
                    currentValue = nearest != null ? nearest.Y : channel.LastValue;
                }
                else
                    currentValue = channel.LastValue;

                row.Cells["Value"].Value = FormatGridValueWithEnum(channel, currentValue);

                if (hasTwoLines)
                {
                    var redNearest = _chartControl.FindNearestPoint(channel, redLineX.Value);
                    var greenNearest = _chartControl.FindNearestPoint(channel, greenLineX.Value);
                    if (redNearest != null && greenNearest != null)
                    {
                        double delta = greenNearest.Y - redNearest.Y;
                        string deltaNum = TrimTrailingZeros(delta);
                        string unit = (channel.Unit ?? string.Empty).Trim();
                        row.Cells["Delta"].Value = !string.IsNullOrEmpty(unit)
                            ? string.Format("{0} {1}", deltaNum, unit)
                            : deltaNum;
                    }
                    else
                    {
                        row.Cells["Delta"].Value = "";
                    }
                }
                else
                {
                    row.Cells["Delta"].Value = "";
                }
            }
        }

        private string FormatGridValueWithEnum(ChannelData channel, double value)
        {
            string numStr = TrimTrailingZeros(value);
            if (channel.EnumDefinitions != null && channel.EnumDefinitions.Count > 0)
            {
                string description;
                if (channel.EnumDefinitions.TryGetValue(value, out description))
                {
                    return string.Format("[{0}] {1}", numStr, description);
                }
            }
            string unit = (channel.Unit ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(unit))
                return string.Format("{0} {1}", numStr, unit);
            return numStr;
        }

        private static string TrimTrailingZeros(double value)
        {
            string s = value.ToString("F6");
            if (s.Contains('.'))
            {
                s = s.TrimEnd('0');
                if (s.EndsWith("."))
                    s = s.TrimEnd('.');
            }
            return s;
        }

        private void ChartFrom_FormClosed(object sender, FormClosedEventArgs e)
        {
            CAN_Data.DbcHelper.ChartShowOpenFlag = false;

            // 主动触发GC回收大块内存（gen2）
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void ChartFrom_FormClosing(object sender, FormClosingEventArgs e)
        {
            _playbackTimer.Stop();
            multiChartFromScheduler.Stop();
            CAN_Data.DbcHelper.ChartShowOpenFlag = false;

            // 快照当前绘图区信号列表+选中工况名（下次启动恢复关闭前状态，含未点"保存工况"的改动）
            SaveLastSessionSnapshot();

            // 清除Main.cs的导入数据
            Main.main.ClearForPlayback();

            // 清除ChartFrom自身的大数据
            lock (_realtimeRawLock)
            {
                _realtimeRawMessages.Clear();
                _realtimeRawMessages.TrimExcess();
                _realtimeRawTrimmed = false;
            }
            _rawMessages = null;
            _streamingMode = false;
            _streamingFilePath = null;
            if (_streamingEnumerator != null)
            {
                _streamingEnumerator.Dispose();
                _streamingEnumerator = null;
            }
            _streamingTotalCount = 0;
            _streamingPendingMessage = null;
            _canIdSignalMap = null;
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                    channel.Clear(); // 内含 TrimExcess
            }

            // 保存流式阈值
            PCAN_Client.Properties.Settings.Default.StreamingThresholdMB = GetStreamingThresholdMB();
            PCAN_Client.Properties.Settings.Default.Save();
        }

        /// <summary>
        /// 右键点击曲线某时刻 → 在Main.cs中显示该时刻前后各5000条报文
        /// </summary>
        private void ShowMessagesAtTime(double xValue)
        {
            // 流式模式不支持按时间索引（文件太大无法快速随机访问）
            if (_streamingMode)
            {
                MessageBox.Show("流式加载模式下不支持右键查看报文，请使用\"数据显示\"功能。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_rawMessages == null || _rawMessages.Count == 0) return;

            // 二分查找最接近该时刻的报文索引
            int closestIdx = 0;
            double minDiff = double.MaxValue;
            for (int i = 0; i < _rawMessages.Count; i++)
            {
                double diff = Math.Abs(_rawMessages[i].TimeStampSeconds - (float)xValue);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIdx = i;
                }
            }

            // 取后15000条
            int AFTER_COUNT = 15000;
            int startIdx = Math.Max(0, closestIdx);
            int endIdx = Math.Min(_rawMessages.Count, closestIdx + AFTER_COUNT + 1);
            int count = endIdx - startIdx;

            var range = _rawMessages.GetRange(startIdx, count);

            // 切换到主界面暂停状态
            Main.main.SetPauseState(true);
            // 导入到Main界面
            Main.main.ClearForPlayback();
            Main.main.BatchImportRawMessages(range);
            Main.main.ForceRefreshDisplay(); // 强制刷新（绕过pause状态）
        }

        private void _radioRealTime_CheckedChanged(object sender, EventArgs e)
        {
            if (_radioRealTime.Checked && !RealTimeDataSta)
                SwitchToRealTimeData();
        }

        private void _radioFileData_CheckedChanged(object sender, EventArgs e)
        {
            if (_radioFileData.Checked && RealTimeDataSta)
                SwitchToFileData();
        }

        private void SwitchToRealTimeData()
        {
            SwitchToRealtimeModeInternal();
        }

        private void SwitchToFileData()
        {
            SwitchToFileModeInternal();
        }

        private void _btnStart_Click(object sender, EventArgs e)
        {
            // 重置停止标记
            _cancelPlayback = false;
            if (_isLoadingFile) return;

            // 实时模式且未连接硬件(PCAN/CANoe)时:弹警告并中断,避免空跑(放在任何状态变更之前)
            if (!_isFileMode && !Main.pcanOpenFlag && !Main.canoeOpenFlag)
            {
                MessageBox.Show("实时模式需要连接CAN硬件:请在「报文列表」窗口连接 PCAN 或 CANoe 后再开始", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = "状态: 已停止 (实时模式 — 未连接硬件)";
                _statusLabel.ForeColor = Color.Red;
                return;
            }

            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _speedComboBox.Enabled = false;
            _toolSpeedComboBox.Enabled = false;
            SyncToolbarStateFromLegacyControls();
            _statusLabel.Text = "状态: 运行中";
            _statusLabel.ForeColor = Color.Green;

            // 点击开始按钮，默认开启自动滑动
            _chartControl.SetAutoScroll(true);
            _btnAutoScroll.Text = "停止滑动";

            if (_isFileMode)
            {
                // ========== 文件回放模式 ==========
                // 内存模式且数据未加载时：临时切到流式模式 + 开启缓存
                // 播放时边读文件边绘图，读完自动缓存到 _rawMessages，后续播放直接用内存
                if (!_streamingMode && _rawMessages == null)
                {
                    _streamingMode = true;
                    _cacheWhileStreaming = true;
                    _statusLabel.Text = "状态: 边读边画(首次加载)...";
                    _statusLabel.ForeColor = Color.Green;
                }

                // 设置模式指示器（在播放开始时确定，之后保持不变）
                if (_streamingMode)
                {
                    _playbackModeText = "流式";
                    _playbackModeColor = Color.DarkOrange;
                }
                else
                {
                    _playbackModeText = "内存";
                    _playbackModeColor = Color.DarkGreen;
                }
                UpdateModeIndicator();

                // 断开PCAN/CANoe连接（含按钮文字、下拉框同步更新）
                Main.main.DisconnectPCAN();
                Main.main.DisconnectCANoe();

                // 清空Main界面数据并切换到Scroll模式
                Main.main.ClearForPlayback();

                // 清空通道数据
                lock (_lockObj)
                {
                    foreach (var channel in Channels)
                        channel.Clear();
                }
                _currentTime = 0;

                // 预构建 BusChannelIndex → (CAN ID → (signal, channelIndex)) 查找表
                // 信号统一从各通道独立的DBC中按 CAN ID+信号名 定位（含旧数据 BusChannelIndex=-1 的重定位）
                var canIdSignalMap = new Dictionary<int, Dictionary<uint, List<(CAN_Data.Signal signal, int channelIndex)>>>();

                for (int ci = 0; ci < Channels.Count; ci++)
                {
                    var channel = Channels[ci];

                    uint canId = (uint)channel.DbcMessageId;
                    string signalName = channel.DbcSignalName;

                    if (string.IsNullOrEmpty(signalName))
                    {
                        continue;
                    }

                    // 遍历所有通道，查找包含该 CAN ID 和信号名的通道 DBC
                    for (int bi = 0; bi < _busChannels.Count; bi++)
                    {
                        var busCh = _busChannels[bi];
                        if (!busCh.IsConfigured || busCh.DbcHelper?.dbcFile?.messageDict == null)
                            continue;

                        if (busCh.DbcHelper.dbcFile.messageDict.TryGetValue(canId, out var chMsg))
                        {
                            // 在通道 DBC 中按信号名查找
                            CAN_Data.Signal chSig = chMsg.signals.FirstOrDefault(s => s.signalName == signalName);
                            if (chSig == null) continue;

                            if (!canIdSignalMap.TryGetValue(bi, out var innerMap))
                                innerMap = new Dictionary<uint, List<(CAN_Data.Signal, int)>>();
                            if (!innerMap.TryGetValue(canId, out var list))
                                list = new List<(CAN_Data.Signal, int)>();
                            list.Add((chSig, ci));
                            innerMap[canId] = list;
                            canIdSignalMap[bi] = innerMap;
                            break; // 找到第一个匹配的通道即可
                        }
                    }
                    // 信号未在任何通道DBC中找到则跳过
                }

                if (_playbackSpeed == 0)
                {
                    // ====== 最快速度：一次性解析并绘制所有数据 ======
                    bool isStreaming = _streamingMode;

                    // 在开始前设置图表通道和时间范围，确保曲线能实时绘制
                    Invoke(new Action(() =>
                    {
                        _chartControl.SetChannels(Channels);
                        _chartControl.SetGlobalXRange(0, Math.Max(_fileMaxTime, 1));
                        _chartControl.Invalidate();
                        SetReportStartTime(0);
                    }));

                    Task.Run(() =>
                    {
                        try
                        {
                            double maxTime = 0;
                            long msgCount = 0;
                            long lastUpdateMsgCount = 0;
                            var updateStopwatch = System.Diagnostics.Stopwatch.StartNew();

                            foreach (var rawMsg in EnumerateRawMessages())
                            {
                                msgCount++;
                                // 流式模式：记录最新的5w帧原始报文(原始帧记录不依赖DBC解码,放在通道匹配之前,未配置通道的报文也要保留到报文列表)
                                if (isStreaming)
                                    RecordStreamingFrame(rawMsg);

                                // 根据BLF通道号查找对应的BusChannelIndex(仅用于信号解码;无匹配通道的报文不解码但已记录)
                                int busIdx = GetBusChannelIndex(rawMsg.Channel);
                                if (busIdx == -2)
                                {
                                    continue;
                                }

                                // 从信号映射表中查找该通道的信号
                                if (canIdSignalMap.TryGetValue(busIdx, out var innerMap) &&
                                    innerMap.TryGetValue(rawMsg.CanId, out var signalList))
                                {
                                    // 获取对应通道的DBC实例
                                    Dictionary<uint, CAN_Data.Message> msgDict = null;
                                    if (busIdx >= 0 && busIdx < _busChannels.Count && _busChannels[busIdx].DbcHelper != null)
                                        msgDict = _busChannels[busIdx].DbcHelper.dbcFile.messageDict;

                                    if (msgDict != null && msgDict.TryGetValue(rawMsg.CanId, out var dbcMessage))
                                    {
                                        var allValues = CAN_Data.CanSignalParser.ParseSignals(rawMsg.Data, dbcMessage.signals);
                                        foreach (var entry in signalList)
                                        {
                                            if (allValues.TryGetValue(entry.signal.signalName, out double val))
                                            {
                                                var ch = Channels[entry.channelIndex];
                                                double currentTime = rawMsg.TimeStampSeconds;

                                                // 检测数据间隙，间隙超过 CycleTime 的 1.5 倍则插入丢失点（虚线绘制）
                                                if (ch.LastReceiveTime > 0 && ch.CycleTime > 0)
                                                {
                                                    double gap = currentTime - ch.LastReceiveTime;
                                                    if (gap > ch.CycleTime * 1.5)
                                                    {
                                                        double lostStart = ch.LastReceiveTime + ch.CycleTime;
                                                        ch.AddPoint(lostStart, ch.LastValue, true);
                                                        double lostEnd = currentTime - 0.0001;
                                                        if (lostEnd > lostStart)
                                                            ch.AddPoint(lostEnd, ch.LastValue, true);
                                                    }
                                                }

                                                ch.AddPoint(currentTime, val, false);
                                            }
                                        }
                                    }
                                }

                                if (rawMsg.TimeStampSeconds > maxTime)
                                    maxTime = rawMsg.TimeStampSeconds;

                                // 定期更新图表（每5000条或每100ms），实现实时绘制
                                if (msgCount - lastUpdateMsgCount >= 5000 && updateStopwatch.ElapsedMilliseconds >= 100)
                                {
                                    lastUpdateMsgCount = msgCount;
                                    updateStopwatch.Restart();
                                    Invoke(new Action(() =>
                                    {
                                        // 用户未关闭自动滑动时才扩展X轴
                                        if (_chartControl.IsAutoScrollEnabled() && maxTime > 0)
                                            _chartControl.SetGlobalXRange(0, maxTime + 1);
                                        _chartControl.Invalidate();
                                        UpdateChannelGridValues();
                                    }));
                                }

                                // 检查停止请求（流式模式 >150MB）
                                if (isStreaming && _cancelPlayback)
                                {
                                    _cancelPlayback = false;
                                    Invoke(new Action(() =>
                                    {
                                        _chartControl.Invalidate();
                                        UpdateChannelGridValues();
                                        _statusLabel.Text = "状态: 已停止播放";
                                        _statusLabel.ForeColor = Color.Red;
                                        _btnStart.Enabled = true;
                                        _btnStop.Enabled = false;
                                        _speedComboBox.Enabled = true;
                                        _toolSpeedComboBox.Enabled = true;
                                        // 流式模式：停止后将记录的5w帧显示到Main.cs界面（需在UI线程调用）
                                        ShowStreamingRecordedFrames();
                                        SyncToolbarStateFromLegacyControls();
                                    }));
                                    return;
                                }
                            }

                            long totalMsgCount = isStreaming
                                ? (_cacheWhileStreaming ? (_rawMessages?.Count ?? 0) : _streamingTotalCount)
                                : (_rawMessages?.Count ?? 0);

                            Invoke(new Action(() =>
                            {
                                _chartControl.SetChannels(Channels);
                                // 用户未关闭自动滑动时才调整视图
                                if (_chartControl.IsAutoScrollEnabled())
                                {
                                    if (maxTime > 0)
                                        _chartControl.SetGlobalXRange(0, maxTime);
                                    _chartControl.SetAutoScroll(false);
                                    _btnAutoScroll.Text = "自动滑动";
                                    _chartControl.AutoFitView();
                                }
                                else
                                {
                                    _chartControl.SetAutoScroll(false);
                                    _btnAutoScroll.Text = "自动滑动";
                                }
                                _chartControl.Invalidate();

                                UpdateChannelGridValues();

                                _statusLabel.Text = $"状态: 播放完成 ({totalMsgCount}条报文)";
                                _statusLabel.ForeColor = Color.Blue;
                                _btnStart.Enabled = true;
                                _btnStop.Enabled = false;
                                SyncToolbarStateFromLegacyControls();
                                SetReportEndTime(GetMaxChannelTime());
                                // 缓存模式：切回内存模式
                                FinalizeCachePlayback(totalMsgCount);
                                // 首次跑数据绘图完成:重置占位符固定时间范围为跟随当前数据(每次启动仅一次)
                                AutoResetPlaceholderRangesOnce();
                            }));

                            // 流式模式：播放完毕后将记录的5w帧显示到Main.cs界面
                            if (isStreaming)
                            {
                                Invoke(new Action(() =>
                                {
                                    ShowStreamingRecordedFrames();
                                }));
                            }
                            // 内存/缓存模式（含首次流式转缓存后的再次回放）：最快路径回放过程不写报文列表，
                            // 完成后把内存数据批量显示到Main（否则只有首次流式回放有报文，再次回放列表空白）
                            else if (_rawMessages != null && _rawMessages.Count > 0)
                            {
                                Invoke(new Action(() =>
                                {
                                    Main.main.BatchImportRawMessages(_rawMessages);
                                    Main.main.SetPauseState(true); // 暂停显示全部帧供回看（与流式完成行为一致）
                                    Main.main.ForceRefreshDisplay();
                                }));
                            }
                        }
                        catch (Exception ex)
                        {
                            Invoke(new Action(() =>
                            {
                                MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                _btnStart.Enabled = true;
                                _btnStop.Enabled = false;
                                SyncToolbarStateFromLegacyControls();
                            }));
                        }
                    });
                }
                else
                {
                    // ====== 倍速播放：定时器逐步推进 ======
                    _canIdSignalMap = canIdSignalMap;
                    _playbackRawIndex = 0;
                    // 流式模式：创建文件枚举器
                    if (_streamingMode)
                        _streamingEnumerator = EnumerateRawMessages().GetEnumerator();
                    _playbackStartWallTime = Environment.TickCount / 1000.0;
                    _chartControl.SetChannels(Channels);
                    _chartControl.SetGlobalXRange(0, Math.Max(_fileMaxTime, 1));
                    _chartControl.SetAutoScroll(true);
                    _btnAutoScroll.Text = "停止滑动";

                    // 根据当前倍速设定Timer间隔
                    if (_playbackSpeed <= 1)
                        _playbackTimer.Interval = 20;
                    else if (_playbackSpeed <= 5)
                        _playbackTimer.Interval = 40;
                    else if (_playbackSpeed <= 20)
                        _playbackTimer.Interval = 60;
                    else
                        _playbackTimer.Interval = 100;

                    _playbackTimer.Start();
                    RunStatus = true;
                    UpdateModeIndicator();
                }
            }
            else
            {
                // ========== 实时模式 ==========
                startTime = DateTime.Now;
                _currentTime = 0;
                lock (_lockObj)
                {
                    foreach (var channel in Channels)
                        channel.Clear();
                }
                lock (_realtimeRawLock)
                {
                    _realtimeRawMessages.Clear();
                    _realtimeRawTrimmed = false;
                }

                // 设置模式指示器
                _playbackModeText = "实时数据";
                _playbackModeColor = Color.Gray;
                UpdateModeIndicator();

                _chartControl.ResetView();
                multiChartFromScheduler.Start();
                _chartControl.SetAutoScroll(true);
                _btnAutoScroll.Text = "停止滑动";
                RunStatus = true;
                UpdateModeIndicator();
            }

            SyncToolbarStateFromLegacyControls();
        }

        /// <summary>
        /// 倍速选择变更
        /// </summary>
        private void _speedComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selected = _speedComboBox.SelectedItem as string;
            if (selected == "最快")
            {
                _playbackSpeed = 0; // 最快速度
            }
            else if (selected != null && selected.EndsWith("x"))
            {
                _playbackSpeed = double.Parse(selected.TrimEnd('x'));
            }

            // 高速时降低UI刷新频率，让每个Tick处理更多数据
            if (_playbackTimer != null)
            {
                if (_playbackSpeed <= 1)
                    _playbackTimer.Interval = 20;
                else if (_playbackSpeed <= 5)
                    _playbackTimer.Interval = 40;
                else if (_playbackSpeed <= 20)
                    _playbackTimer.Interval = 60;
                else
                    _playbackTimer.Interval = 100;
            }

            if (_toolSpeedComboBox.SelectedIndex != _speedComboBox.SelectedIndex)
            {
                _toolSpeedComboBox.SelectedIndex = _speedComboBox.SelectedIndex;
            }
        }

        private void _toolSpeedComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_toolSpeedComboBox.SelectedIndex >= 0 && _toolSpeedComboBox.SelectedIndex != _speedComboBox.SelectedIndex)
            {
                _speedComboBox.SelectedIndex = _toolSpeedComboBox.SelectedIndex;
            }

            SyncToolbarStateFromLegacyControls();
        }

        /// <summary>全局数据点大小改变:应用到所有通道并刷新绘图(新通道创建时继承_globalDotSize)</summary>
        private void _toolDotSizeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_toolDotSizeComboBox.SelectedIndex < 0) return;
            _globalDotSize = _toolDotSizeComboBox.SelectedIndex + 1;

            if (Channels == null || _chartControl == null) return;
            foreach (var ch in Channels)
            {
                if (ch != null) ch.DotSize = _globalDotSize;
            }
            _chartControl.Invalidate();
        }

        /// <summary>全局线宽改变:应用到所有通道并刷新绘图(新通道创建时继承_globalLineWidth)</summary>
        private void _toolLineWidthComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_toolLineWidthComboBox.SelectedIndex < 0) return;
            _globalLineWidth = _toolLineWidthComboBox.SelectedIndex + 1;

            if (Channels == null || _chartControl == null) return;
            foreach (var ch in Channels)
            {
                if (ch != null) ch.LineWidth = _globalLineWidth;
            }
            _chartControl.Invalidate();
        }

        /// <summary>
        /// 文件回放定时器Tick：按倍速逐步推进虚拟时间，解析并绘制数据
        /// </summary>
        private void _playbackTimer_Tick(object sender, EventArgs e)
        {
            if (_canIdSignalMap == null)
            {
                _playbackTimer.Stop();
                return;
            }
            // 流式模式使用 _streamingEnumerator，内存模式使用 _rawMessages
            if (!_streamingMode && (_rawMessages == null || _rawMessages.Count == 0))
            {
                _playbackTimer.Stop();
                return;
            }

            // 计算虚拟时间 = (真实经过时间) × 倍速
            double wallElapsed = Environment.TickCount / 1000.0 - _playbackStartWallTime;
            double virtualTime = wallElapsed * _playbackSpeed;
            _currentTime = virtualTime;

            // 一次性处理所有时间戳 <= virtualTime 的报文

            bool allDone = true;
            int messagesThisTick = 0;       // 本Tick处理的消息数限制
            // 高速时按比例增加每Tick处理量，同时降低UI刷新频率（避免界面卡顿）
            const int BASE_MSG_PER_TICK = 500;
            int maxPerTick = (int)(BASE_MSG_PER_TICK * Math.Max(1.0, _playbackSpeed));
            if (maxPerTick > 10000) maxPerTick = 10000;

            bool hasMoreMessages;
            double lastProcessedTime = 0;
            bool streamingExhausted = false;
            while (true)
            {
                // 检查是否还有更多消息
                if (_streamingMode)
                {
                    if (_streamingPendingMessage != null)
                    {
                        hasMoreMessages = true;
                    }
                    else
                    {
                        hasMoreMessages = _streamingEnumerator != null && _streamingEnumerator.MoveNext();
                        if (!hasMoreMessages) streamingExhausted = true;
                    }
                }
                else
                {
                    hasMoreMessages = _playbackRawIndex < _rawMessages.Count;
                }
                if (!hasMoreMessages) break;

                // 如果用户点击了停止按钮，立即退出
                if (!RunStatus)
                {
                    break;
                }

                // 每Tick最多处理maxPerTick条，防止UI线程阻塞太久
                if (messagesThisTick >= maxPerTick)
                {
                    allDone = false;
                    break;
                }

                CanRawMessage rawMsg;
                if (_streamingMode)
                {
                    if (_streamingPendingMessage != null)
                    {
                        rawMsg = _streamingPendingMessage;
                        _streamingPendingMessage = null;
                    }
                    else
                    {
                        rawMsg = _streamingEnumerator.Current;
                    }
                }
                else
                {
                    rawMsg = _rawMessages[_playbackRawIndex];
                }

                if (rawMsg.TimeStampSeconds > virtualTime)
                {
                    // 剩下的报文时间都大于当前虚拟时间，等下次Tick
                    // 流式模式：保存当前报文到下一Tick，避免丢失
                    if (_streamingMode)
                        _streamingPendingMessage = rawMsg;
                    allDone = false;
                    break;
                }

                lastProcessedTime = rawMsg.TimeStampSeconds;

                // 通道筛选：跳过不在选中通道的报文(用户显式设置的过滤器,对记录和解码都生效)
                if (!IsChannelMatched(rawMsg))
                {
                    _playbackRawIndex++;
                    messagesThisTick++;
                    continue;
                }

                // 同步显示到Main界面 + 流式记录(原始帧不依赖DBC解码,放在通道配置匹配之前,未配置通道的报文也要保留到报文列表)
                TPCANMsg tMsg = new TPCANMsg();
                tMsg.ID = rawMsg.CanId;
                tMsg.LEN = (byte)rawMsg.Data.Length;
                tMsg.DATA = rawMsg.Data;
                ulong tUs = (ulong)(rawMsg.TimeStampSeconds * 1000000.0);
                // 回放帧rawMsg.Channel为BLF通道号，转换为逻辑通道号（列表分行/各自通道DBC解析）
                byte logicCh = rawMsg.Channel > 0 ? BaseParamter.GetLogicChannelByBlfId(rawMsg.Channel) : (byte)1;
                Main.main.RecordCanMessage(tMsg, tUs, false, true, logicCh);

                // 流式模式：记录最新的5w帧原始报文
                if (_streamingMode)
                    RecordStreamingFrame(rawMsg);

                // 根据BLF通道号查找对应的BusChannelIndex(仅用于信号解码;无匹配通道的报文不解码但已记录)
                int busIdx = GetBusChannelIndex(rawMsg.Channel);
                if (busIdx == -2)
                {
                    _playbackRawIndex++;
                    messagesThisTick++;
                    continue;
                }

                // 解析此报文中的信号并添加到对应通道
                if (_canIdSignalMap.TryGetValue(busIdx, out var innerMap) &&
                    innerMap.TryGetValue(rawMsg.CanId, out var signalList))
                {
                    // 获取对应通道的DBC实例
                    Dictionary<uint, CAN_Data.Message> msgDict = null;
                    if (busIdx >= 0 && busIdx < _busChannels.Count && _busChannels[busIdx].DbcHelper != null)
                        msgDict = _busChannels[busIdx].DbcHelper.dbcFile.messageDict;

                    if (msgDict != null && msgDict.TryGetValue(rawMsg.CanId, out var dbcMessage))
                    {
                        var allValues = CAN_Data.CanSignalParser.ParseSignals(rawMsg.Data, dbcMessage.signals);
                        foreach (var entry in signalList)
                        {
                            if (allValues.TryGetValue(entry.signal.signalName, out double val))
                            {
                                var ch = Channels[entry.channelIndex];
                                double currentTime = rawMsg.TimeStampSeconds;

                                // 检测数据间隙，间隙超过 CycleTime 的 1.5 倍则插入丢失点（虚线绘制）
                                if (ch.LastReceiveTime > 0 && ch.CycleTime > 0)
                                {
                                    double gap = currentTime - ch.LastReceiveTime;
                                    if (gap > ch.CycleTime * 1.5)
                                    {
                                        double lostStart = ch.LastReceiveTime + ch.CycleTime;
                                        ch.AddPoint(lostStart, ch.LastValue, true);
                                        double lostEnd = currentTime - 0.0001;
                                        if (lostEnd > lostStart)
                                            ch.AddPoint(lostEnd, ch.LastValue, true);
                                    }
                                }

                                ch.AddPoint(currentTime, val, false);
                            }
                        }
                    }
                }

                _playbackRawIndex++;
                messagesThisTick++;
            }

            // 更新图表显示
            // 如果本Tick未能处理完所有消息（allDone=false），传入实际已处理的最新时间而非虚拟时间
            double scrollToTime = virtualTime;
            if (!allDone && _playbackRawIndex > 0 && lastProcessedTime > 0)
            {
                if (lastProcessedTime < scrollToTime)
                    scrollToTime = lastProcessedTime;
            }

            // 判断消息是否已耗尽
            bool exhausted;
            if (_streamingMode)
            {
                exhausted = streamingExhausted;
            }
            else
            {
                exhausted = _playbackRawIndex >= _rawMessages.Count;
            }

            if (!exhausted && RunStatus)
            {
                // 仍有消息但被 virtualTime 挡住了，等下次 Tick
                // 仅当未耗尽时才滚动
                _chartControl.AutoScrollToLatest(scrollToTime);
                _chartControl.Invalidate();
                UpdateChannelGridValues();
            }

            // 所有报文播放完毕
            if (RunStatus && exhausted)
            {
                if (_streamingMode && _streamingEnumerator != null)
                {
                    _streamingEnumerator.Dispose();
                    _streamingEnumerator = null;
                }
                _playbackTimer.Stop();
                RunStatus = false;
                UpdateModeIndicator();
                _chartControl.AutoFitView();
                _chartControl.SetAutoScroll(false);
                long totalMsgCount = _streamingMode
                    ? (_cacheWhileStreaming ? (_rawMessages?.Count ?? 0) : _streamingTotalCount)
                    : (_rawMessages?.Count ?? 0);

                // 流式模式：播放完毕后将记录的5w帧显示到Main.cs界面
                if (_streamingMode)
                    ShowStreamingRecordedFrames();

                Invoke(new Action(() =>
                {
                    _btnStart.Enabled = true;
                    _btnStop.Enabled = false;
                    _statusLabel.Text = $"状态: 播放完成 ({totalMsgCount}条报文)";
                    _statusLabel.ForeColor = Color.Blue;
                    _btnAutoScroll.Text = "自动滑动";

                    UpdateChannelGridValues();
                    SetReportEndTime(GetMaxChannelTime());

                    SyncToolbarStateFromLegacyControls();
                    // 缓存模式：切回内存模式
                    FinalizeCachePlayback(totalMsgCount);
                }));
            }
        }

        private bool IsChannelMatched(CanRawMessage msg)
        {
            return _selectedChannels == null || _selectedChannels.Contains(msg.Channel);
        }

        private void _btnBusConfig_Click(object sender, EventArgs e)
        {
            // 统一通道管理窗口（硬件识别/通道配置/DBC/映射/连接一窗统管）：
            // 窗口内保存时已写回全局通道列表、持久化并刷新聚合DBC视图，此处仅执行后置刷新
            using (var dlg = new ChannelManagerForm(Main.main))
            {
                dlg.ShowDialog(this);
                if (!dlg.ConfigSaved) return;
            }
            {
                {
                    try
                    {
                        Main.main?.UpdateDbcTreeview();
                        if (Main.canSendOpenFlag) Main.canSend?.UpdateDbcTreeview();
                    }
                    catch { /* 窗口未初始化时忽略 */ }
                    _btnBusConfig.Text = _busChannels.Count > 0 ? $"通道管理({_busChannels.Count})" : "通道管理";

                    // 通道配置变更立即回写当前工况JSON:否则下次应用工况时会被工况里保存的旧路径覆盖(跨机器使用时表现为"路径每次被重置")
                    if (_currentAnalysisType != null)
                        SaveAnalysisTypeJson(_currentAnalysisType, _currentAnalysisType.Name);

                    // 多通道模式下：重映射现有信号的 BusChannelIndex
                    if (_busChannels.Count > 0 && Channels.Count > 0)
                    {
                        int remappedCount = 0;
                        foreach (var ch in Channels)
                        {
                            // 只重映射 BusIdx=-1 的信号（来自全局 DBC）
                            if (ch.BusChannelIndex != -1) continue;

                            uint canId = (uint)ch.DbcMessageId;
                            for (int bi = 0; bi < _busChannels.Count; bi++)
                            {
                                var busCh = _busChannels[bi];
                                if (busCh.IsConfigured && busCh.DbcHelper?.dbcFile?.messageDict != null)
                                {
                                    if (busCh.DbcHelper.dbcFile.messageDict.TryGetValue(canId, out var chDbcMsg))
                                    {
                                        ch.BusChannelIndex = bi;
                                        // 更新 MsgIdx 为通道 DBC 中的索引
                                        ch.DbcMessageIndex = busCh.DbcHelper.dbcFile.messages.IndexOf(chDbcMsg);
                                        remappedCount++;
                                        break;
                                    }
                                }
                            }
                        }
                        if (remappedCount > 0)
                        {
                            _statusLabel.Text = $"状态: 已将 {remappedCount} 个信号映射到配置的通道";
                            _statusLabel.ForeColor = Color.Blue;
                        }
                        PopulateChannelGrid();
                    }
                }
            }
        }

        /// <summary>
        /// 根据BLF通道号查找对应的BusChannelIndex
        /// </summary>
        private int GetBusChannelIndex(byte blfChannel)
        {
            if (_busChannels == null || _busChannels.Count == 0)
                return -1; // 未配置通道（不解码）
            for (int i = 0; i < _busChannels.Count; i++)
            {
                if (_busChannels[i].BlfChannelId == blfChannel)
                    return i;
            }
            return -2; // 未找到匹配的通道
        }

        // ===== 占位符报告通道数据补采(后台线程,不阻塞界面) =====
        private bool _backfillRunning = false;             // 防重入:是否正在补采
        private volatile bool _backfillCancel = false;     // 取消标志
        private readonly List<ChannelData> _backfillPending = new List<ChannelData>();  // 排队通道
        private Action _backfillPendingCallback;           // 排队任务的合并完成回调
        private Button _btnCancelBackfill;                 // 状态栏"取消补采"按钮(补采时显示)

        /// <summary>
        /// 异步为新增的报告通道补采数据:后台线程扫描内存帧(内存模式)或重新遍历文件(流式模式),
        /// 状态栏显示进度,可取消;onComplete在全部完成(或取消)后于UI线程回调。
        /// 补采中再来新通道自动排队,当前批完成后继续。
        /// </summary>
        private void BackfillReportChannelsAsync(List<ChannelData> newChannels, Action onComplete)
        {
            if (newChannels == null || newChannels.Count == 0) { onComplete?.Invoke(); return; }
            lock (_backfillPending)
            {
                if (_backfillRunning)
                {
                    _backfillPending.AddRange(newChannels);
                    if (onComplete != null) _backfillPendingCallback += onComplete;
                    return;
                }
            }
            StartBackfillBatch(newChannels, onComplete);
        }

        /// <summary>后台线程安全地调度到UI线程:窗口已关闭则直接丢弃(补采结果无意义)</summary>
        private void SafeBeginInvoke(Action action)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
                // 窗口已关闭,丢弃
            }
            catch (InvalidOperationException)
            {
                // 句柄未创建/已销毁,丢弃
            }
        }

        private void StartBackfillBatch(List<ChannelData> batch, Action onComplete)
        {
            // 数据源:内存模式扫缓存帧;流式模式后台重新遍历文件;实时模式无历史数据直接回调
            bool fromMemory = _rawMessages != null && _rawMessages.Count > 0;
            if (!fromMemory && !_isFileMode)
            {
                _statusLabel.Text = "状态: 实时模式无法补采历史数据,加载文件后可用";
                _statusLabel.ForeColor = Color.Orange;
                onComplete?.Invoke();
                return;
            }

            _backfillRunning = true;
            _backfillCancel = false;
            SetBackfillUiState(true);

            // 快照后台线程需要的引用(避免扫描期间被界面操作修改)
            var busChannelsSnapshot = _busChannels.ToArray();
            var memSource = fromMemory ? _rawMessages : null;

            Task.Run(() =>
            {
                long frames = 0;
                try
                {
                    IEnumerable<CanRawMessage> source = memSource ?? EnumerateRawMessages();
                    foreach (var rawMsg in source)
                    {
                        if (_backfillCancel) break;
                        ProcessBackfillFrame(rawMsg, batch, busChannelsSnapshot);
                        frames++;
                        if (frames % 50000 == 0)
                        {
                            long f = frames;
                            SafeBeginInvoke(new Action(() =>
                                _statusLabel.Text = $"状态: 补采信号数据中... 已处理 {f / 10000} 万帧"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Backfill] 补采异常: " + ex.Message);
                }
                SafeBeginInvoke(new Action(() => FinishBackfillBatch(onComplete)));
            });
        }

        /// <summary>一批补采完成(回UI线程):有排队通道则继续下一批,否则收尾并回调</summary>
        private void FinishBackfillBatch(Action onComplete)
        {
            if (IsDisposed) return; // 窗口已关闭,丢弃结果
            List<ChannelData> next = null;
            Action nextCb = null;
            lock (_backfillPending)
            {
                _backfillRunning = false;
                if (_backfillPending.Count > 0 && !_backfillCancel)
                {
                    next = new List<ChannelData>(_backfillPending);
                    nextCb = _backfillPendingCallback;
                }
                _backfillPending.Clear();
                _backfillPendingCallback = null;
            }

            if (next != null)
            {
                StartBackfillBatch(next, () => { nextCb?.Invoke(); onComplete?.Invoke(); });
                return;
            }

            SetBackfillUiState(false);
            if (_backfillCancel)
            {
                _statusLabel.Text = "状态: 补采已取消(部分数据可能不完整,可重新计算)";
                _statusLabel.ForeColor = Color.Orange;
            }
            else
            {
                _statusLabel.Text = "状态: 数据补采完成";
                _statusLabel.ForeColor = Color.Green;
            }
            onComplete?.Invoke();
        }

        /// <summary>补采进行/结束的界面状态切换(进度条+取消按钮)</summary>
        private void SetBackfillUiState(bool running)
        {
            if (IsDisposed) return; // 窗口已关闭,不再操作界面
            if (running)
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.Visible = true;
                _btnCancelBackfill.Enabled = true;
                _btnCancelBackfill.Visible = true;
            }
            else
            {
                _progressBar.Visible = false;
                _btnCancelBackfill.Visible = false;
            }
        }

        /// <summary>单帧补采处理(后台线程):按CAN ID+总线匹配目标通道,解码存点</summary>
        private static void ProcessBackfillFrame(CanRawMessage rawMsg, List<ChannelData> batch,
            CanBusChannel[] busChannels)
        {
            // 总线索引(基于快照):无匹配通道直接跳过
            if (busChannels.Length == 0) return;
            int busIdx = -1;
            for (int i = 0; i < busChannels.Length; i++)
            {
                if (busChannels[i].BlfChannelId == rawMsg.Channel) { busIdx = i; break; }
            }
            if (busIdx < 0) return;

            // 本帧可能命中的报告通道(按 CAN ID + 总线索引匹配;BusChannelIndex=-1的旧数据接受任意总线)
            List<ChannelData> hits = null;
            foreach (var ch in batch)
            {
                if (ch.DbcMessageId != (int)rawMsg.CanId) continue;
                if (ch.BusChannelIndex >= 0 && ch.BusChannelIndex != busIdx) continue;
                if (hits == null) hits = new List<ChannelData>();
                hits.Add(ch);
            }
            if (hits == null) return;

            // 获取对应总线的DBC报文定义并解析全部信号
            Dictionary<uint, CAN_Data.Message> msgDict = busChannels[busIdx].DbcHelper?.dbcFile?.messageDict;
            if (msgDict == null || !msgDict.TryGetValue(rawMsg.CanId, out var dbcMessage)) return;

            var allValues = CAN_Data.CanSignalParser.ParseSignals(rawMsg.Data, dbcMessage.signals);
            foreach (var ch in hits)
            {
                if (allValues.TryGetValue(ch.DbcSignalName, out double val))
                    ch.AddPoint(rawMsg.TimeStampSeconds, val, false);
            }
        }

        private bool IsChannelMatched(CanRawMessageRead msg)
        {
            return _selectedChannels == null || _selectedChannels.Contains(msg.Channel);
        }

        private void _btnShowData_Click(object sender, EventArgs e)
        {
            bool hasMessages = _rawMessages != null && _rawMessages.Count > 0;
            if (!hasMessages && !_streamingMode)
            {
                MessageBox.Show("请先加载报文文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Channels == null || Channels.Count == 0)
            {
                MessageBox.Show("请先添加需要显示的信号通道", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // 检查DBC是否可用：至少一个通道配置了DBC
            if (!_busChannels.Any(bc => bc.IsConfigured))
            {
                MessageBox.Show("请先在通道配置中为至少一个CAN通道加载DBC文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 1. 解析时间输入，做有效性校验
            double startTime, endTime;
            if (!double.TryParse(_txtStartTime.Text, out startTime))
                startTime = 0;
            if (!double.TryParse(_txtEndTime.Text, out endTime))
                endTime = _fileMaxTime;

            // 2. 修正到有效范围
            if (startTime < 0) startTime = 0;
            if (endTime < 0) endTime = 0;
            if (startTime > _fileMaxTime) startTime = _fileMaxTime;
            if (endTime > _fileMaxTime) endTime = _fileMaxTime;
            if (startTime > endTime)
            {
                double tmp = startTime;
                startTime = endTime;
                endTime = tmp;
            }

            // 回显修正后的值
            _txtStartTime.Text = startTime.ToString("F1");
            _txtEndTime.Text = endTime.ToString("F1");

            // 3. 过滤+数据量确认+导入整体后台执行：流式模式下过滤需遍历整个文件，
            // 在UI线程同步执行会导致大文件界面冻结（导入步骤原已在Task内）。
            // Task前快照过滤所需字段（UI线程读，防止过滤期间用户清除/加载新文件/关闭窗口造成竞态）
            bool snapshotStreamingMode = _streamingMode;
            string snapshotStreamingFilePath = _streamingFilePath;
            List<CanRawMessage> snapshotRawMessages = _rawMessages;
            var snapshotSelectedChannels = _selectedChannels;

            _btnShowData.Enabled = false;
            _statusLabel.Text = "状态: 正在过滤时间范围数据...";
            Task.Run(() =>
            {
                try
                {
                    // 过滤时间范围内的原始报文
                    List<CanRawMessage> filteredMessages;
                    if (snapshotStreamingMode)
                    {
                        // 流式模式：从文件流读取并过滤（快照路径，不随UI变化）
                        filteredMessages = new List<CanRawMessage>();
                        foreach (var msg in LogFileLoader.EnumerateCanMessages(snapshotStreamingFilePath, snapshotSelectedChannels))
                        {
                            if (msg.TimeStampSeconds >= startTime && msg.TimeStampSeconds <= endTime)
                            {
                                filteredMessages.Add(new CanRawMessage
                                {
                                    CanId = msg.CanId,
                                    Data = msg.Data,
                                    TimeStampSeconds = (float)msg.TimeStampSeconds,
                                    Channel = msg.Channel
                                });
                            }
                        }
                    }
                    else
                    {
                        // 快照列表引用：_rawMessages只整体替换从不原地修改，引用安全
                        var src = snapshotRawMessages;
                        filteredMessages = src == null
                            ? new List<CanRawMessage>()
                            : src.Where(m => m.TimeStampSeconds >= startTime && m.TimeStampSeconds <= endTime && IsChannelMatched(m))
                                 .ToList();
                    }

                    if (filteredMessages.Count == 0)
                    {
                        Invoke(new Action(() => MessageBox.Show("所选时间范围内没有数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        return;
                    }

                    // 数据量超过 100000 条时弹窗确认（Invoke回UI线程等待用户决策）
                    if (filteredMessages.Count > 100000)
                    {
                        bool confirmed = false;
                        Invoke(new Action(() =>
                        {
                            var r = MessageBox.Show(
                                $"所选区域数据量 {filteredMessages.Count} 条，大于 20000 条，是否显示？",
                                "数据量确认",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            confirmed = r == DialogResult.Yes;
                        }));
                        if (!confirmed) return;
                    }

                    // 5. 清空 Main.cs 并发送数据到主界面显示
                    Invoke(new Action(() =>
                    {
                        Main.main.ClearForPlayback();
                        _statusLabel.Text = $"状态: 正在加载 {filteredMessages.Count} 条数据到主界面...";
                    }));

                    Main.main.BatchImportRawMessages(filteredMessages);
                    // 设置为暂停模式，确保显示所有帧（而非仅最后20帧）
                    Main.main._pauseUpdate = true;
                    Main.main.ForceRefreshDisplay();

                    Invoke(new Action(() =>
                    {
                        _statusLabel.Text = $"状态: 已发送 {filteredMessages.Count} 条数据到主界面 ({startTime:F1}s ~ {endTime:F1}s)";
                        _statusLabel.ForeColor = Color.Black;
                    }));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show($"数据显示失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                finally
                {
                    Invoke(new Action(() => { _btnShowData.Enabled = true; }));
                }
            });
        }

        private void UpdateModeIndicator()
        {
            _modeIndicatorLabel.Text = $"模式: {_playbackModeText}";
            _modeIndicatorLabel.ForeColor = _playbackModeColor;
        }

        private void _btnStop_Click(object sender, EventArgs e)
        {
            // 请求停止流式模式的"最快"后台Task
            if (_streamingMode)
                _cancelPlayback = true;
            RunStatus = false;
            UpdateModeIndicator();
            _playbackTimer.Stop();
            multiChartFromScheduler.Stop();
            // 清理流式枚举器
            if (_streamingEnumerator != null)
            {
                _streamingEnumerator.Dispose();
                _streamingEnumerator = null;
            }
            _streamingPendingMessage = null;
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _statusLabel.Text = "状态: 已停止" + (_isFileMode ? " (可重新播放)" : "");
            _statusLabel.ForeColor = Color.Red;
            _chartControl.AutoFitView();

            // 设置报告结束时间为当前数据最大时间
            SetReportEndTime(GetMaxChannelTime());

            _chartControl.SetAutoScroll(false);
            _btnAutoScroll.Text = "自动滑动";

            // 流式模式：停止后将记录的5w帧显示到Main.cs界面
            if (_streamingMode)
                ShowStreamingRecordedFrames();

            // 缓存模式：切回内存模式（即使播放未完成也保存已缓存的数据）
            if (_cacheWhileStreaming && _rawMessages != null && _rawMessages.Count > 0)
            {
                _cacheWhileStreaming = false;
                _streamingMode = false;
                _txtEndTime.Text = _fileMaxTime.ToString("F1");
                _statusLabel.Text = "状态: 已停止（报文数据已缓存到内存，下次可直接播放）";
                _statusLabel.ForeColor = Color.Red;
            }

            SyncToolbarStateFromLegacyControls();
        }

        private void _btnClear_Click(object sender, EventArgs e)
        {
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                {
                    channel.Clear();
                }
                _currentTime = 0;
            }
            lock (_realtimeRawLock)
            {
                _realtimeRawMessages.Clear();
                _realtimeRawTrimmed = false;
            }
            startTime = DateTime.Now;
            _chartControl.SetChannels(Channels);
            _chartControl.ResetView();
            _chartControl.SetAutoScroll(true);
            _btnAutoScroll.Text = "停止滑动";
        }

        private void _btnShowAll_Click(object sender, EventArgs e)
        {
            // 使用 AutoFitView 从所有通道数据中计算完整时间范围，而不是用 GetLatestDataTime
            //（后者仅返回当前播放位置时间，在播放中途使用时会导致范围偏小）
            _chartControl.AutoFitView();
            _chartControl.SetAutoScroll(false);
            _btnAutoScroll.Text = "自动滑动";
        }

        /// <summary>
        /// 获取流式阈值的有效值（MB）
        /// </summary>
        private int GetStreamingThresholdMB()
        {
            return _streamingThresholdMB;
        }

        /// <summary>
        /// 返回当前模式下的原始报文枚举器：流式模式从文件读取，内存模式从 _rawMessages 返回
        /// </summary>
        /// <summary>
        /// 缓存模式播放结束后切回内存模式（下次播放直接用 _rawMessages，不再读文件）
        /// </summary>
        private void FinalizeCachePlayback(long totalMsgCount)
        {
            if (_cacheWhileStreaming && _rawMessages != null && _rawMessages.Count > 0)
            {
                _cacheWhileStreaming = false;
                _streamingMode = false;
                _txtEndTime.Text = _fileMaxTime.ToString("F1");
                _statusLabel.Text = "状态: 播放完成（报文数据已缓存到内存，下次可直接播放）";
                _statusLabel.ForeColor = Color.Blue;
            }
        }

        private IEnumerable<CanRawMessage> EnumerateRawMessages()
        {
            if (_streamingMode)
            {
                List<CanRawMessage> cache = _cacheWhileStreaming ? new List<CanRawMessage>() : null;
                // 收集所有通道DBC中定义了的CAN ID（缓存时只保留有定义的报文，减少内存占用）
                HashSet<uint> multiChannelCanIds = null;
                if (cache != null)
                {
                    multiChannelCanIds = new HashSet<uint>();
                    foreach (var busCh in _busChannels)
                    {
                        if (busCh.IsConfigured && busCh.DbcHelper.dbcFile.messageDict != null)
                        {
                            foreach (var canId in busCh.DbcHelper.dbcFile.messageDict.Keys)
                                multiChannelCanIds.Add(canId);
                        }
                    }
                }

                // 确定要读取的文件列表：优先使用 _logFilePaths，否则回退到 _streamingFilePath
                var filesToRead = (_logFilePaths != null && _logFilePaths.Count > 0) 
                    ? _logFilePaths 
                    : new List<string> { _streamingFilePath };

                foreach (var filePath in filesToRead)
                {
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                        continue;

                    foreach (var msg in LogFileLoader.EnumerateCanMessages(filePath, _selectedChannels))
                    {
                        var raw = new CanRawMessage
                        {
                            CanId = msg.CanId,
                            Data = msg.Data,
                            TimeStampSeconds = (float)msg.TimeStampSeconds,
                            Channel = msg.Channel
                        };
                        // 缓存模式下，只缓存DBC中有定义的报文（减少内存占用）
                        if (cache != null)
                        {
                            if (multiChannelCanIds != null && multiChannelCanIds.Contains(raw.CanId))
                                cache.Add(raw);
                        }
                        yield return raw;
                    }
                }
                // 读取完毕后，把缓存存回 _rawMessages（内存模式后续播放直接用内存）
                if (cache != null && cache.Count > 0)
                {
                    _rawMessages = cache;
                    _loadingBatchMaxTime = cache[cache.Count - 1].TimeStampSeconds;
                    _fileMaxTime = _loadingBatchMaxTime;
                }
            }
            else
            {
                if (_rawMessages == null) yield break;
                foreach (var msg in _rawMessages)
                {
                    yield return msg;
                }
            }
        }

        private void _btnAutoScroll_Click(object sender, EventArgs e)
        {
            bool enabled = !_chartControl.IsAutoScrollEnabled();
            _chartControl.SetAutoScroll(enabled);
            // SetAutoScroll 触发 OnAutoScrollChanged,统一在其中同步 Checked
            _btnAutoScroll.Text = enabled ? "停止滑动" : "自动滑动";
        }

        private void _btnAddChannel_Click(object sender, EventArgs e)
        {
            // 必须先配置CAN通道（DBC唯一入口）
            if (_busChannels.Count == 0)
            {
                MessageBox.Show("请先通过\"通道配置\"添加CAN通道并加载DBC文件", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 选择CAN总线通道
            int selectedBusChannelIndex = -1;
            {
                using (var channelSelectorForm = new Form())
                {
                    channelSelectorForm.Text = "选择CAN通道";
                    channelSelectorForm.StartPosition = FormStartPosition.CenterParent;
                    channelSelectorForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    channelSelectorForm.MaximizeBox = false;
                    channelSelectorForm.MinimizeBox = false;
                    channelSelectorForm.ClientSize = new Size(300, 120);

                    var lblPrompt = new Label { Text = "请选择要添加信号的CAN通道：", Left = 20, Top = 15, AutoSize = true };
                    var cmbChannel = new ComboBox { Left = 20, Top = 45, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
                    var btnOk = new Button { Text = "确定", Left = 100, Top = 80, Width = 80, DialogResult = DialogResult.OK };

                    // 填充通道列表
                    for (int i = 0; i < _busChannels.Count; i++)
                    {
                        var ch = _busChannels[i];
                        string displayName = $"{ch.Name} (通道={ch.BlfChannelId})";
                        if (ch.IsConfigured)
                            displayName += $" [{Path.GetFileName(ch.DbcFilePath)}]";
                        else
                            displayName += " [未配置DBC]";
                        cmbChannel.Items.Add(displayName);
                    }
                    if (cmbChannel.Items.Count > 0) cmbChannel.SelectedIndex = 0;

                    channelSelectorForm.Controls.AddRange(new Control[] { lblPrompt, cmbChannel, btnOk });
                    channelSelectorForm.AcceptButton = btnOk;

                    if (channelSelectorForm.ShowDialog(this) != DialogResult.OK)
                        return;

                    selectedBusChannelIndex = cmbChannel.SelectedIndex;

                    // 检查DBC是否已配置
                    if (!_busChannels[selectedBusChannelIndex].IsConfigured)
                    {
                        MessageBox.Show($"通道 {_busChannels[selectedBusChannelIndex].Name} 未配置DBC文件，请先在通道配置中加载DBC", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            // 创建并显示信号选择器（使用选定通道的DBC实例）
            SignalSelector selector = new SignalSelector(_busChannels[selectedBusChannelIndex].DbcHelper, selectedBusChannelIndex);

            using (selector)
            {
                // 把当前已有的通道标记为已选中，以便在信号选择界面自动勾上
                // 只预填充同一 BusChannelIndex 的信号（避免不同 DBC 的 MsgIndex 含义不同导致误判）
                foreach (var channel in Channels)
                {
                    if (channel.BusChannelIndex != selectedBusChannelIndex)
                        continue;

                    selector.SelectedSignals.Add(new SelectedSignalInfo
                    {
                        SignalName = channel.DbcSignalName,
                        MsgId = (uint)channel.DbcMessageId,
                        MsgIndex = channel.DbcMessageIndex,
                        SignalIndex = channel.DbcSignalIndex,
                        CycleTime = (uint)(channel.CycleTime * 1000),
                        EnumDefinitions = channel.EnumDefinitions,
                        Unit = channel.Unit,
                        BusChannelIndex = channel.BusChannelIndex
                    });
                }

                if (selector.ShowDialog() == DialogResult.OK)
                {
                    Color[] colors = new Color[]
                    {
                        // 优化后的高对比度颜色（白色背景上清晰可见）
                        Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple,
                        Color.DeepSkyBlue, Color.Crimson, Color.Brown, Color.DarkBlue, Color.DarkGreen,
                        Color.DarkRed, Color.DarkOrange, Color.DarkCyan, Color.DarkMagenta, Color.Indigo,
                        Color.MediumSeaGreen, Color.Maroon, Color.Navy, Color.OliveDrab, Color.SteelBlue
                    };
                    // 获取选择的信号信息
                    foreach (var signal in selector.SelectedSignals)
                    {
                        // 跳过已存在的信号，避免重复添加（多通道模式下需同时匹配BusChannelIndex）
                        bool exists = Channels.Any(c =>
                            c.DbcMessageIndex == signal.MsgIndex &&
                            c.DbcSignalIndex == signal.SignalIndex &&
                            c.BusChannelIndex == signal.BusChannelIndex);
                        if (exists) continue;

                        Console.WriteLine($"信号名: {signal.SignalName}");
                        Console.WriteLine($"中文注释: {signal.SignalComment}");
                        Console.WriteLine($"MsgIndex: {signal.MsgIndex}");
                        Console.WriteLine($"SignalIndex: {signal.SignalIndex}");
                        Console.WriteLine($"BusChannelIndex: {signal.BusChannelIndex}");
                        Console.WriteLine($"周期时间: {signal.CycleTime} ms");
                        Console.WriteLine($"单位: {(string.IsNullOrEmpty(signal.Unit) ? "无" : signal.Unit)}");
                        // 输出枚举值定义
                        if (signal.EnumDefinitions != null && signal.EnumDefinitions.Count > 0)
                        {
                            Console.WriteLine($"枚举值定义:");
                            foreach (var kv in signal.EnumDefinitions)
                            {
                                Console.WriteLine($"  {kv.Key} => {kv.Value}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"枚举值定义: 无");
                        }
                        Console.WriteLine("-------------------");

                        int channelCount = Channels.Count;
                        Color color = colors[channelCount % colors.Length];
                        string channelName = TruncateName($"{signal.SignalName} ({signal.SignalComment})");

                        ChannelData newCh = new ChannelData(channelName, color, DateTime.Now, signal.EnumDefinitions, signal.Unit,
                            (double)(signal.CycleTime / 1000.0f), (int)signal.MsgId, signal.MsgIndex, signal.SignalIndex,
                            signal.SignalName, signal.BusChannelIndex);
                        newCh.LineWidth = _globalLineWidth;   // 继承当前全局线宽
                        newCh.DotSize = _globalDotSize;       // 继承当前全局数据点大小
                        Channels.Add(newCh);
                        _chartControl.SetChannels(Channels);

                        // 从通道DBC取报文对象，注册调度并设置ChartShowFlag
                        CAN_Data.Message dbcMsg = ResolveChannelDbcMessage(
                            signal.BusChannelIndex, signal.MsgIndex, signal.MsgId, signal.SignalName,
                            out _, out _, out _);
                        if (dbcMsg != null)
                        {
                            multiChartFromScheduler.AddMessage(dbcMsg, signal.CycleTime, BaseParamter.GetLogicChannel(signal.BusChannelIndex));
                            if (signal.SignalIndex >= 0 && signal.SignalIndex < dbcMsg.signals.Count)
                                dbcMsg.signals[signal.SignalIndex].ChartShowFlag = true;
                        }
                    }
                    PopulateChannelGrid();
                }
            }
        }

        private void _btnRemoveChannel_Click(object sender, EventArgs e)
        {
            // 与Delete键/右键菜单同一路径,支持多选批量移除
            DeleteSelectedChannel();
        }

        private void ChartFrom_Load(object sender, EventArgs e)
        {
            Channels = new List<ChannelData>();
            CAN_Data.DbcHelper.ChartShowOpenFlag = true;
            // 启动时已从BusChannels.json恢复全局通道配置,同步按钮文字
            if (_busChannels.Count > 0)
                _btnBusConfig.Text = $"通道配置({_busChannels.Count})";
            // 初始选中工况在构造函数阶段仅加载未应用,此时Channels已就绪,补应用(恢复信号列表)
            // 存在上次会话快照时跳过：信号列表由Main_Load恢复全局通道配置后按快照恢复（保持关闭前状态，含未保存工况的改动）
            if (_currentAnalysisType != null && LoadLastSession() == null)
                ApplyAnalysisType(_currentAnalysisType);
            RefreshPresetComboBox();
        }

        /// <summary>唤出报文列表窗口(Main启动后被隐藏,需要查看报文时显示)</summary>
        private void _btnShowMainForm_Click(object sender, EventArgs e)
        {
            if (Main.main == null) return;
            // 独立窗口化：启动时为防闪窗设了ShowInTaskbar=false（Program.cs），唤出时恢复任务栏/Alt-Tab独立按钮，
            // 之后可独立最小化(任务栏找回)/最大化/关闭(隐藏)；仅在隐藏状态下设置，避免可见时修改引发句柄重建闪烁
            if (!Main.main.Visible) Main.main.ShowInTaskbar = true;
            // Main以最小化方式启动从未真正绘制过，首次恢复Normal时整个窗体走首次布局+绘制，
            // 半成品窗口会闪现；先隐藏起来同步完成完整绘制，再一次性呈现完整窗口
            bool firstShow = (Main.main.WindowState == FormWindowState.Minimized);
            if (firstShow) Main.main.Opacity = 0;
            Main.main.Show();
            if (Main.main.WindowState == FormWindowState.Minimized)
                Main.main.WindowState = FormWindowState.Normal;
            Main.main.BringToFront();
            Main.main.Activate();
            if (firstShow)
            {
                Main.main.Refresh();   // 同步完成首次完整绘制（此时不可见）
                Main.main.Opacity = 1; // 绘制完成后直接呈现完整窗口
            }
        }

        private void _channelGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            string columnName = _channelGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "Color")
            {
                ChangeChannelColorAtRow(e.RowIndex);
            }
        }

        /// <summary>信号列表选中变化(Ctrl/Shift多选):高亮绘图区对应曲线面板背景</summary>
        private void _channelGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (IsDisposed || _chartControl == null || _chartControl.IsDisposed)
                return; // 窗体/绘图控件销毁中丢弃回调,避免访问已释放对象
            if (Channels == null)
                return;
            foreach (var channel in Channels)
                channel.IsHighlighted = false;
            foreach (DataGridViewRow row in _channelGrid.SelectedRows)
            {
                if (row.Tag is ChannelData channel)
                    channel.IsHighlighted = true;
            }
            _chartControl.SmartInvalidate();
        }

        private void _channelGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            if (_channelGrid.Columns[e.ColumnIndex].Name != "Visible")
                return;

            bool isChecked = GetChannelRowChecked(e.RowIndex);
            SetChannelRowChecked(e.RowIndex, !isChecked);
        }

        private void _channelGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            bool isChecked = GetChannelRowChecked(e.RowIndex);
            SetChannelRowChecked(e.RowIndex, !isChecked);
        }

        private void _channelGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0)
                return;

            // 信号列与当前值列之间的灰色分隔线
            if (_channelGrid.Columns[e.ColumnIndex].Name == "Signal")
            {
                e.Handled = true;
                if (e.RowIndex >= 0)
                {
                    e.PaintBackground(e.CellBounds, e.State.HasFlag(DataGridViewElementStates.Selected));
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.Border);
                }
                else
                {
                    e.PaintBackground(e.CellBounds, true);
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground);
                }
                using (Pen grayLinePen = new Pen(Color.FromArgb(180, 180, 180), 1))
                {
                    int lineX = e.CellBounds.Right - 1;
                    e.Graphics.DrawLine(grayLinePen, lineX, e.CellBounds.Top, lineX, e.CellBounds.Bottom);
                }
                return;
            }

            // 当前值与差值之间的灰色分隔线
            if (_channelGrid.Columns[e.ColumnIndex].Name == "Value")
            {
                e.Handled = true;
                if (e.RowIndex >= 0)
                {
                    e.PaintBackground(e.CellBounds, e.State.HasFlag(DataGridViewElementStates.Selected));
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.Border);
                }
                else
                {
                    e.PaintBackground(e.CellBounds, true);
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground);
                }
                using (Pen grayLinePen = new Pen(Color.FromArgb(180, 180, 180), 1))
                {
                    int lineX = e.CellBounds.Right - 1;
                    e.Graphics.DrawLine(grayLinePen, lineX, e.CellBounds.Top, lineX, e.CellBounds.Bottom);
                }
                return;
            }

            if (_channelGrid.Columns[e.ColumnIndex].Name != "Color")
                return;

            if (e.RowIndex < 0)
                return;

            e.Handled = true;
            e.PaintBackground(e.CellBounds, e.State.HasFlag(DataGridViewElementStates.Selected));

            ChannelData channel = GetChannelFromRow(e.RowIndex);
            if (channel == null)
                return;

            Rectangle swatchRect = new Rectangle(
                e.CellBounds.Left + 7,
                e.CellBounds.Top + 5,
                Math.Max(10, e.CellBounds.Width - 14),
                Math.Max(10, e.CellBounds.Height - 10));

            using (SolidBrush brush = new SolidBrush(channel.Color))
            using (Pen borderPen = new Pen(Color.Black))
            {
                e.Graphics.FillRectangle(brush, swatchRect);
                e.Graphics.DrawRectangle(borderPen, swatchRect);
            }

            e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        }

        private void ChartFrom_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+W: 自适应调整所有坐标轴（类似CANoe）
            if (e.Control && e.KeyCode == Keys.W)
            {
                _chartControl.AutoFitView();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void _channelGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedChannel();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                // Ctrl+A 全选列表行
                _channelGrid.SelectAll();
                e.Handled = true;
            }
        }

        private void DeleteSelectedChannel()
        {
            if (_channelGrid.SelectedRows.Count == 0)
                return;

            // 收集所有选中行的通道和行引用(支持Shift多选/Ctrl+A全选后批量删除)
            var toDelete = new List<ChannelData>();
            var rowsToRemove = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _channelGrid.SelectedRows)
            {
                var channel = row.Tag as ChannelData;
                if (channel == null) continue;
                toDelete.Add(channel);
                rowsToRemove.Add(row);
            }
            if (toDelete.Count == 0)
                return;

            lock (_lockObj)
            {
                foreach (var channel in toDelete)
                {
                    channel.Clear();
                    Channels.Remove(channel);
                }
                // 按行引用删除,避免索引位移
                foreach (var row in rowsToRemove)
                    _channelGrid.Rows.Remove(row);
            }

            _chartControl.SetChannels(Channels);
            _statusLabel.Text = "状态: 已停止";
            _statusLabel.ForeColor = Color.Red;
        }

        private void ChangeChannelColorAtRow(int rowIndex)
        {
            ChannelData channel = GetChannelFromRow(rowIndex);
            if (channel == null) return;

            using (ColorDialog colorDialog = new ColorDialog())
            {
                colorDialog.Color = channel.Color;
                colorDialog.AllowFullOpen = true;
                colorDialog.FullOpen = true;
                if (colorDialog.ShowDialog(this) != DialogResult.OK) return;

                channel.Color = colorDialog.Color;
                UpdateChannelGridColor(channel);
                _chartControl.Invalidate();
            }
        }

        /// <summary>
        /// 更新通道列表中指定通道的颜色显示
        /// </summary>
        private void UpdateChannelGridColor(ChannelData changedChannel)
        {
            foreach (DataGridViewRow row in _channelGrid.Rows)
            {
                if (ReferenceEquals(row.Tag, changedChannel))
                {
                    _channelGrid.InvalidateCell(row.Cells["Color"]);
                    break;
                }
            }
        }

        #region 通道网格拖拽排序
        private void _channelGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            _channelDragRowIndex = -1;
            _channelDragBoxFromMouseDown = Rectangle.Empty;

            if (e.RowIndex < 0) return;

            if (e.Button == MouseButtons.Left && CanReorderChannels())
            {
                _channelDragRowIndex = e.RowIndex;
                Size dragSize = SystemInformation.DragSize;
                _channelDragBoxFromMouseDown = new Rectangle(
                    new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                    dragSize);
            }
        }

        private void _channelGrid_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                _channelDragRowIndex = -1;
                _channelDragBoxFromMouseDown = Rectangle.Empty;
            }

            if (e.Button != MouseButtons.Right) return;

            var hit = _channelGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0)
            {
                _channelGrid.ClearSelection();
            }
        }

        private void _channelGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (_channelDragRowIndex < 0 || _channelDragBoxFromMouseDown == Rectangle.Empty) return;

            if (_channelDragBoxFromMouseDown.Contains(e.Location)) return;

            int dragRowIndex = _channelDragRowIndex;
            _channelDragRowIndex = -1;
            _channelDragBoxFromMouseDown = Rectangle.Empty;
            _channelGrid.DoDragDrop(dragRowIndex, DragDropEffects.Move);
        }

        private void _channelGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!CanReorderChannels() || !e.Data.GetDataPresent(typeof(int)))
            {
                ClearChannelDropInsertIndicator();
                e.Effect = DragDropEffects.None;
                return;
            }

            Point clientPoint = _channelGrid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = _channelGrid.HitTest(clientPoint.X, clientPoint.Y);
            UpdateChannelDropInsertIndicator(GetDropInsertIndex(hit, clientPoint));
            e.Effect = DragDropEffects.Move;
        }

        private void _channelGrid_DragLeave(object sender, EventArgs e)
        {
            ClearChannelDropInsertIndicator();
        }

        private void _channelGrid_DragDrop(object sender, DragEventArgs e)
        {
            if (!CanReorderChannels() || !e.Data.GetDataPresent(typeof(int)))
            {
                ClearChannelDropInsertIndicator();
                return;
            }

            int sourceIndex = (int)e.Data.GetData(typeof(int));
            if (sourceIndex < 0 || sourceIndex >= _channelGrid.Rows.Count)
            {
                ClearChannelDropInsertIndicator();
                return;
            }

            Point clientPoint = _channelGrid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = _channelGrid.HitTest(clientPoint.X, clientPoint.Y);
            int insertIndex = GetDropInsertIndex(hit, clientPoint);
            ClearChannelDropInsertIndicator();
            MoveChannelRow(sourceIndex, insertIndex);
        }

        private void _channelGrid_Paint(object sender, PaintEventArgs e)
        {
            if (_channelDropInsertIndex < 0 || _channelGrid.Rows.Count == 0) return;

            int lineY;
            if (_channelDropInsertIndex >= _channelGrid.Rows.Count)
            {
                Rectangle lastRowRect = _channelGrid.GetRowDisplayRectangle(_channelGrid.Rows.Count - 1, true);
                if (lastRowRect == Rectangle.Empty) return;
                lineY = lastRowRect.Bottom - 1;
            }
            else
            {
                Rectangle rowRect = _channelGrid.GetRowDisplayRectangle(_channelDropInsertIndex, true);
                if (rowRect == Rectangle.Empty) return;
                lineY = rowRect.Top;
            }

            int lineLeft = _channelGrid.RowHeadersVisible ? _channelGrid.RowHeadersWidth : 0;
            int lineRight = _channelGrid.ClientSize.Width - 1;
            using (Pen insertPen = new Pen(Color.Black, 2))
            {
                e.Graphics.DrawLine(insertPen, lineLeft, lineY, lineRight, lineY);
            }
        }

        private bool CanReorderChannels()
        {
            return !_isLoadingFile &&
                !RunStatus &&
                _channelGrid.Rows.Count > 1 &&
                Channels != null &&
                Channels.Count > 1;
        }

        private int GetDropInsertIndex(DataGridView.HitTestInfo hit, Point clientPoint)
        {
            if (_channelGrid.Rows.Count == 0) return 0;

            if (hit.RowIndex < 0)
            {
                Rectangle firstRowRect = _channelGrid.GetRowDisplayRectangle(0, false);
                if (firstRowRect != Rectangle.Empty && clientPoint.Y <= firstRowRect.Top)
                    return 0;

                Rectangle lastRowRect = _channelGrid.GetRowDisplayRectangle(_channelGrid.Rows.Count - 1, false);
                return clientPoint.Y > lastRowRect.Bottom ? _channelGrid.Rows.Count : _channelGrid.Rows.Count - 1;
            }

            Rectangle rowRect = _channelGrid.GetRowDisplayRectangle(hit.RowIndex, false);
            return clientPoint.Y > rowRect.Top + rowRect.Height / 2 ? hit.RowIndex + 1 : hit.RowIndex;
        }

        private void UpdateChannelDropInsertIndicator(int insertIndex)
        {
            int safeInsertIndex = Math.Max(0, Math.Min(insertIndex, _channelGrid.Rows.Count));
            if (_channelDropInsertIndex == safeInsertIndex) return;

            _channelDropInsertIndex = safeInsertIndex;
            _channelGrid.Invalidate();
        }

        private void ClearChannelDropInsertIndicator()
        {
            if (_channelDropInsertIndex < 0) return;
            _channelDropInsertIndex = -1;
            _channelGrid.Invalidate();
        }

        private void MoveChannelRow(int sourceIndex, int insertIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= _channelGrid.Rows.Count) return;

            int maxInsertIndex = _channelGrid.Rows.Count;
            int safeInsertIndex = Math.Max(0, Math.Min(insertIndex, maxInsertIndex));
            if (safeInsertIndex == sourceIndex || safeInsertIndex == sourceIndex + 1) return;

            ChannelData channel = GetChannelFromRow(sourceIndex);
            if (channel == null) return;

            bool isChecked = GetChannelRowChecked(sourceIndex);

            lock (_lockObj)
            {
                Channels.Remove(channel);
                _channelGrid.Rows.RemoveAt(sourceIndex);
                if (safeInsertIndex > sourceIndex) safeInsertIndex--;
                // 网格插入位映射到Channels索引:隐藏的IsReportOnly通道不在网格中,按锚点通道在Channels中的实际位置插入
                int channelsIndex = Channels.Count;
                if (safeInsertIndex < _channelGrid.Rows.Count)
                {
                    var anchor = GetChannelFromRow(safeInsertIndex);
                    if (anchor != null)
                        channelsIndex = Channels.IndexOf(anchor);
                }
                Channels.Insert(channelsIndex, channel);
                InsertChannelGridRow(safeInsertIndex, channel, isChecked);
            }

            SelectChannelGridRow(channel);
            _chartControl.SetChannels(Channels);
            _chartControl.Invalidate();
        }

        private void InsertChannelGridRow(int rowIndex, ChannelData channel, bool isChecked)
        {
            int safeRowIndex = Math.Max(0, Math.Min(rowIndex, _channelGrid.Rows.Count));
            _channelGrid.Rows.Insert(safeRowIndex, isChecked, string.Empty, GetChannelDisplayName(channel), string.Empty, string.Empty);
            _channelGrid.Rows[safeRowIndex].Tag = channel;
            _channelGrid.Rows[safeRowIndex].Cells["Color"].ToolTipText = "点击修改颜色";
            _channelGrid.Rows[safeRowIndex].Cells["Signal"].Style.ForeColor = SystemColors.ControlText;
            UpdateChannelGridValues();
        }
        #endregion

        private void _channelContextMenu_SelectAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < _channelGrid.Rows.Count; i++)
            {
                SetChannelRowChecked(i, true);
            }
        }

        private void _channelContextMenu_DeselectAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < _channelGrid.Rows.Count; i++)
            {
                SetChannelRowChecked(i, false);
            }
        }

        private void _channelContextMenu_Delete_Click(object sender, EventArgs e)
        {
            DeleteSelectedChannel();
        }

        /// <summary>按信号名找曲线通道；多通道模式（同一份DBC配多路，信号名相同）额外匹配逻辑通道（-1=兼容未分配通道，通配）</summary>
        private int GetChannelIndex(string signalName, byte logicChannel = 0)
        {
            int targetBusIndex = logicChannel > 0 ? BaseParamter.GetChannelIndex(logicChannel) : -1;
            if (targetBusIndex < 0 && logicChannel > 0) targetBusIndex = logicChannel - 1;
            for (int index = 0; index < Channels.Count; index++)
            {
                if (!Channels[index].DbcSignalName.Equals(signalName)) continue;
                if (BaseParamter.BusChannels.Count > 0 && logicChannel > 0
                    && Channels[index].BusChannelIndex >= 0 && Channels[index].BusChannelIndex != targetBusIndex)
                    continue;
                return index;
            }
            return -1;
        }
        /// <summary>按 CAN ID+信号索引找曲线通道；多通道模式下额外匹配信号所属逻辑通道（BusChannelIndex），同ID信号各归各的曲线（-1=兼容未分配通道，通配）</summary>
        private int GetChannelIndex(int messageId, int signalIndex, byte logicChannel = 0)
        {
            int targetBusIndex = logicChannel > 0 ? BaseParamter.GetChannelIndex(logicChannel) : -1;
            if (targetBusIndex < 0 && logicChannel > 0) targetBusIndex = logicChannel - 1;
            for (int index = 0; index < Channels.Count; index++)
            {
                if (Channels[index].DbcSignalIndex != signalIndex || Channels[index].DbcMessageId != messageId)
                    continue;
                if (BaseParamter.BusChannels.Count > 0 && logicChannel > 0
                    && Channels[index].BusChannelIndex >= 0 && Channels[index].BusChannelIndex != targetBusIndex)
                    continue; // 多通道同ID：只喂给信号所属通道的曲线
                return index;
            }
            return -1;
        }
        private int _lastGridValueUpdateTick = 0;  // 上次信号列表数值刷新时间(TickCount),用于实时刷新节流
        public void AddPoint(uint msgId, int signalIndex, double rawValue, uint cycleTime, byte logicChannel = 0)
        {
            // 文件模式下不接收实时数据
            if (!RunStatus || _isLoadingFile || _isFileMode)
            {
                return;
            }
            int channelIndex = Main.chartFromShow.GetChannelIndex((int)msgId, signalIndex, logicChannel);
            if (-1 != channelIndex)
            {
                TimeSpan elapsed = DateTime.Now - Main.chartFromShow.startTime;
                _currentTime = elapsed.TotalSeconds;

                ChannelData channel = Main.chartFromShow.Channels[channelIndex];
                channel.AddPoint(_currentTime, rawValue, false);
                // 无线测量线时实时更新当前值(节流:最多每100ms刷新一次,避免高帧率下CPU空耗)
                if (!_chartControl.MeasureLineX1.HasValue)
                {
                    int tick = Environment.TickCount;
                    if (tick - _lastGridValueUpdateTick >= 100)
                    {
                        _lastGridValueUpdateTick = tick;
                        UpdateChannelGridValues();
                    }
                }
            }
        }
        public class MultiMessageCANScheduler : IDisposable
        {
            private class CANMessageSchedule : IComparable<CANMessageSchedule>
            {
                public CAN_Data.Message Message { get; set; }
                public long IntervalMs { get; set; }
                public long NextTriggerTime { get; set; }
                /// <summary>逻辑通道号（1-based，0=未指定/单通道兼容）；多通道同一份DBC给多路时按 通道+ID 区分注册</summary>
                public byte LogicChannel { get; set; }
                public uint CanId => Message.messgeId;
                /// <summary>调度注册复合键：逻辑通道号(高32位)+CAN ID(低32位)；LogicChannel=0时退化为纯ID（兼容单通道）</summary>
                public long ScheduleKey => ((long)LogicChannel << 32) | CanId;
                /// <summary>入队序号（相同触发时间+相同键时保证SortedSet不丢元素）</summary>
                public long Seq { get; set; }
                public int CompareTo(CANMessageSchedule other)
                {
                    return NextTriggerTime.CompareTo(other.NextTriggerTime);
                }
            }
            // 简化优先队列实现，使用 SortedSet（需要处理重复元素）
            private class CANMessageScheduleQueue
            {
                private readonly SortedSet<CANMessageSchedule> _sortedSet;
                private long _sequenceNumber = 0; // 用于处理相同触发时间的元素

                public int Count => _sortedSet.Count;

                public CANMessageScheduleQueue()
                {
                    _sortedSet = new SortedSet<CANMessageSchedule>(new CANMessageScheduleComparer());
                }

                private class CANMessageScheduleComparer : IComparer<CANMessageSchedule>
                {
                    public int Compare(CANMessageSchedule x, CANMessageSchedule y)
                    {
                        if (x == null && y == null) return 0;
                        if (x == null) return -1;
                        if (y == null) return 1;

                        int timeCompare = x.NextTriggerTime.CompareTo(y.NextTriggerTime);
                        if (timeCompare != 0) return timeCompare;

                        // 触发时间相同：先按复合键（通道+ID），再按入队序号——多通道同ID同周期时也不会被SortedSet判重丢弃
                        int keyCompare = x.ScheduleKey.CompareTo(y.ScheduleKey);
                        if (keyCompare != 0) return keyCompare;
                        return x.Seq.CompareTo(y.Seq);
                    }
                }

                public void Enqueue(CANMessageSchedule item)
                {
                    item.Seq = _sequenceNumber++;
                    _sortedSet.Add(item);
                }

                public CANMessageSchedule Dequeue()
                {
                    if (_sortedSet.Count == 0)
                        throw new InvalidOperationException("Queue is empty");

                    var first = _sortedSet.Min;
                    _sortedSet.Remove(first);
                    return first;
                }

                public CANMessageSchedule Peek()
                {
                    if (_sortedSet.Count == 0)
                        throw new InvalidOperationException("Queue is empty");
                    return _sortedSet.Min;
                }

                public bool Remove(CANMessageSchedule item)
                {
                    return _sortedSet.Remove(item);
                }
            }

            private readonly CANMessageScheduleQueue _priorityQueue;
            private readonly Dictionary<long, CANMessageSchedule> _scheduleLookup; // key=ScheduleKey（逻辑通道<<32|CAN ID）
            private readonly List<CANMessageSchedule> _ChartfromBuffer;
            private readonly object _lock = new object();
            private Stopwatch stopwatch = new Stopwatch();
            private bool _isStarted = false;

            private double lastChartRefreshTime;
            public MultiMessageCANScheduler()
            {
                _priorityQueue = new CANMessageScheduleQueue();
                _scheduleLookup = new Dictionary<long, CANMessageSchedule>();
                _ChartfromBuffer = new List<CANMessageSchedule>();
            }

            public void AddMessage(CAN_Data.Message message, uint intervalMs, byte logicChannel = 0)
            {
                intervalMs = (uint)((intervalMs <= 0) ? 1 : intervalMs*2);
                lock (_lock)
                {
                    if (message == null) return;
                    var schedule = new CANMessageSchedule
                    {
                        Message = message,
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime(),
                        LogicChannel = logicChannel
                    };
                    if (null != schedule.Message)
                    {
                        if (!_scheduleLookup.ContainsKey(schedule.ScheduleKey))
                        {
                            _scheduleLookup[schedule.ScheduleKey] = schedule;
                            _priorityQueue.Enqueue(schedule);
                        }
                        else
                        {
                            UpdateMessageInterval(message.messgeId, intervalMs, logicChannel);
                        }
                    }
                }
            }

            public void RemoveMessage(uint canId, byte logicChannel = 0)
            {
                lock (_lock)
                {
                    long key = ((long)logicChannel << 32) | canId;
                    if (_scheduleLookup.ContainsKey(key))
                    {
                        _scheduleLookup.Remove(key);
                    }
                }
            }

            public void Start()
            {
                if (_isStarted) return;
                _isStarted = true;
                stopwatch.Start();
                lastChartRefreshTime = stopwatch.ElapsedMilliseconds;
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "ChartFrom", 1);

                Main.chartFromShow.DataUpdated += Main.chartFromShow.SimulationManager_DataUpdated;
            }

            public void Stop()
            {
                if (!_isStarted) return;
                _isStarted = false;
                Main.main.multiMessageCANScheduler.RemoveAction("ChartFrom");
                Main.chartFromShow.DataUpdated -= Main.chartFromShow.SimulationManager_DataUpdated;
            }

            private void SchedulerCallback()
            {
                var currentTime = stopwatch.ElapsedMilliseconds;
                //Console.WriteLine(DateTime.Now);
                
                // 处理所有到期的消息
                lock (_lock)
                {
                    while (_priorityQueue.Count > 0)
                    {
                        var nextSchedule = _priorityQueue.Peek();

                        // 检查该消息是否已被移除
                        if (!_scheduleLookup.ContainsKey(nextSchedule.ScheduleKey))
                        {
                            _priorityQueue.Dequeue(); // 移除已删除的消息
                            continue;
                        }

                        // 如果下一个消息还没到期，就跳出循环
                        if (nextSchedule.NextTriggerTime > currentTime)
                            break;

                        // 出队并处理（缓存schedule：后续超时判定/曲线匹配需要LogicChannel区分多通道同ID）
                        var schedule = _priorityQueue.Dequeue();
                        _ChartfromBuffer.Add(schedule);

                        // 更新下次触发时间并重新入队
                        schedule.NextTriggerTime = currentTime + schedule.IntervalMs;
                        _priorityQueue.Enqueue(schedule);
                    }
                }


                // 批量处理
                if (_ChartfromBuffer.Count > 0)
                {
                    TimeSpan elapsed = DateTime.Now - Main.chartFromShow.startTime;
                    Main.chartFromShow._currentTime = elapsed.TotalSeconds;

                    foreach (var sc in _ChartfromBuffer)
                    {
                        var msg = sc.Message;
                        bool isLost = msg.receiveCnt == msg.ChartFromReceiveCnt;
                        if (!Main.chartFromShow.RunStatus)
                        {
                            isLost = false;
                        }
                        foreach (var signal in msg.signals)
                        {
                            if (signal.ChartShowFlag)
                            {
                                // 多通道同名信号（同一份DBC配多路）：按逻辑通道匹配，超时丢失点插到各自通道的曲线
                                int index = Main.chartFromShow.GetChannelIndex(signal.signalName, sc.LogicChannel);
                                if (-1 != index)
                                {
                                    ChannelData channel = Main.chartFromShow.Channels[index];
                                    if (isLost)
                                    {
                                        channel.AddPoint(Main.chartFromShow._currentTime, channel.LastValue, isLost);
                                        if (!channel.IsLost)
                                        {
                                            Main.chartFromShow.Channels[index].IsLost = true;
                                            UpdateMessageInterval(msg.messgeId, msg.cycleTime, sc.LogicChannel);
                                        }
                                    }
                                    else
                                    {
                                        if (channel.IsLost)
                                        {
                                            Main.chartFromShow.Channels[index].IsLost = false;
                                            UpdateMessageInterval(msg.messgeId, msg.cycleTime * 2, sc.LogicChannel);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }
                        msg.ChartFromReceiveCnt = msg.receiveCnt;
                        //BaseParamter.dbcHelper.SendCanMessage(msg.messgeId);
                    }
                    _ChartfromBuffer.Clear();
                }

                /* 刷新：根据X轴时间范围动态调整刷新间隔 */
                if (Main.chartFromShow.DataUpdated != null)
                {
                    // 计算当前X轴范围（秒）
                    double xMin = Main.chartFromShow._chartControl.GetCurrentXMin();
                    double xMax = Main.chartFromShow._chartControl.GetCurrentXMax();
                    double xRange = xMax - xMin;

                    // 根据X轴范围确定刷新间隔（ms）
                    long refreshIntervalMs;
                    if (xRange <= 300)
                        refreshIntervalMs = 150;
                    else if (xRange < 500)
                        refreshIntervalMs = 500;
                    else if (xRange < 2000)
                        refreshIntervalMs = 1000;
                    else
                        refreshIntervalMs = 3000;

                    if (currentTime - lastChartRefreshTime >= refreshIntervalMs)
                    {
                        lastChartRefreshTime = currentTime;
                        Main.chartFromShow.DataUpdated.BeginInvoke(this, new DataUpdatedEventArgs(Main.chartFromShow._currentTime), null, null);
                    }
                }
            }

            public void UpdateMessageInterval(uint canId, uint newIntervalMs, byte logicChannel = 0)
            {
                lock (_lock)
                {
                    long key = ((long)logicChannel << 32) | canId;
                    if (_scheduleLookup.TryGetValue(key, out var schedule))
                    {
                        // 更新间隔时间
                        newIntervalMs = (newIntervalMs <= 0) ? 1 : newIntervalMs;
                        schedule.IntervalMs = newIntervalMs;

                        // 注意：由于优先队列的特性，我们无法直接更新队列中的元素
                        // 这个更新会在下次重新入队时生效
                    }
                }
            }

            private long GetCurrentTime()
            {
                return stopwatch.ElapsedMilliseconds;
            }

            public void Dispose()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("CAN_Send");
            }
        }

        /// <summary>集中切换到实时数据模式</summary>
        private void SwitchToRealtimeModeInternal()
        {
            if (RealTimeDataSta) return; // 已经是实时模式
            if (_isLoadingFile) return;  // 加载中禁止切换

            RealTimeDataSta = true;
            
            // 保存模式设置
            PCAN_Client.Properties.Settings.Default["ChartMode"] = "RealTime";
            PCAN_Client.Properties.Settings.Default.Save();

            // 更新UI可见性
            _filePathTextBox.Visible = false;
            _speedLabel.Visible = false;
            _speedComboBox.Visible = false;

            // 停止所有运行中的操作
            if (RunStatus)
            {
                RunStatus = false;
                UpdateModeIndicator();
                multiChartFromScheduler.Stop();
            }
            _playbackTimer.Stop();

            // 切换到实时模式
            _isFileMode = false;

            // 清空通道显示数据（保留 _rawMessages 在内存中，可随时切回播放）
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                    channel.Clear();
            }
            _currentTime = 0;

            // 更新UI状态
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _statusLabel.Text = _rawMessages != null
                ? "状态: 已停止 (实时模式 — 文件数据已保留)"
                : "状态: 已停止 (实时模式)";
            _statusLabel.ForeColor = Color.Red;
            _toolModeToggle.Text = "实时数据";
            UpdateModeToggleDisplay();
            // 左下角模式指示器与当前模式保持一致
            _playbackModeText = "实时数据";
            _playbackModeColor = Color.Gray;
            UpdateModeIndicator();

            _chartControl.Invalidate();
        }

        /// <summary>集中切换到报文数据模式</summary>
        private void SwitchToFileModeInternal()
        {
            if (!RealTimeDataSta) return; // 已经是报文模式

            RealTimeDataSta = false;
            
            // 保存模式设置
            PCAN_Client.Properties.Settings.Default["ChartMode"] = "FileData";
            PCAN_Client.Properties.Settings.Default.Save();

            // 更新UI可见性
            _filePathTextBox.Visible = true;
            _speedLabel.Visible = true;
            _speedComboBox.Visible = true;

            // 停止所有运行中的操作
            if (RunStatus)
            {
                RunStatus = false;
                UpdateModeIndicator();
                multiChartFromScheduler.Stop();
            }
            _playbackTimer.Stop();

            // 切换到报文模式即断开实时硬件连接（报文回放/分析不再需要硬件收发；原先延迟到点开始回放才断开，切换后硬件仍在收发）
            Main.main?.DisconnectPCAN();
            Main.main?.DisconnectCANoe();

            // 判断是否有可用的文件数据:内存已加载,或已勾选报文路径(播放时走流式/边读边缓存)
            int loadedCount = _rawMessages != null ? _rawMessages.Count : 0;
            bool hasLogFiles = _logFilePaths != null && _logFilePaths.Count > 0;
            _isFileMode = loadedCount > 0 || hasLogFiles;

            // 清空通道显示数据(构造函数恢复模式时 Channels 尚未创建,需判空)
            lock (_lockObj)
            {
                if (Channels != null)
                    foreach (var channel in Channels)
                        channel.Clear();
            }
            _currentTime = 0;

            // 更新UI状态
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _statusLabel.Text = !_isFileMode
                ? "状态: 已停止 (报文模式 — 请加载文件)"
                : (loadedCount > 0
                    ? $"状态: 已停止 (报文模式 — 已加载{loadedCount}条报文，可点击开始播放)"
                    : $"状态: 已停止 (报文模式 — 已勾选{_logFilePaths.Count}个报文文件，可点击开始播放)");
            _statusLabel.ForeColor = Color.Red;
            _toolModeToggle.Text = "报文数据";
            UpdateModeToggleDisplay();
            // 左下角模式指示器与当前模式保持一致(播放时会覆盖为流式/内存)
            _playbackModeText = "报文数据";
            _playbackModeColor = Color.Gray;
            UpdateModeIndicator();
            _chartControl.Invalidate();
        }

        /// <summary>
        /// 加载时的定时器刷新图表（UI线程，不阻塞后台加载）
        /// </summary>
        private void _loadingRefreshTimer_Tick(object sender, EventArgs e)
        {
            _btnLoadFile.Text = $"加载中...";
            // 弹窗显示进度（每0.1秒刷新，仅数字变化时更新，避免闪烁）
            if (_loadingPopup != null && _loadingProcessedCount != _lastPopupShownCount)
            {
                _lastPopupShownCount = _loadingProcessedCount;
                var lbl = _loadingPopup.Controls.OfType<Label>().FirstOrDefault();
                if (lbl != null)
                {
                    lbl.Text = $"已加载: {_loadingProcessedCount} 条报文";
                }
            }

            _chartControl.SetChannels(Channels);
            if (_loadingBatchMaxTime > 0)
            {
                _chartControl.SetGlobalXRange(0, _loadingBatchMaxTime + 1);
            }
            _chartControl.Invalidate();
        }

        private void _btnLoadFile_Click(object sender, EventArgs e)
        {
            using (var dialog = new LogFileListDialog(_logFileEntries))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _logFileEntries = dialog.AllEntries;
                    _logFilePaths = _logFileEntries.Where(entry => entry.Enabled).Select(entry => entry.Path).ToList();
                    SaveLogFilePaths();

                    if (_logFilePaths.Count > 0)
                    {
                        LoadAndPlotFiles(_logFilePaths);
                    }
                    else
                    {
                        // 0勾选/空列表确定:已清空报文路径,仅提示不加载
                        _statusLabel.Text = "状态: 已清空报文路径(未加载文件)";
                        _statusLabel.ForeColor = Color.Gray;
                    }
                }
            }
        }

        private void _btnSaveBlf_Click(object sender, EventArgs e)
        {
            List<CanRawMessage> messagesToSave = null;

            if (_isFileMode && _rawMessages != null && _rawMessages.Count > 0)
            {
                // 文件模式：保存加载的文件数据
                messagesToSave = _rawMessages;
            }
            else
            {
                // 实时模式：保存实时捕获的数据
                bool trimmed = false;
                lock (_realtimeRawLock)
                {
                    if (_realtimeRawMessages.Count == 0)
                    {
                        // 区分"未接收数据"与"数据已实时录制到文件"（录制开启时内存缓存为空属正常）
                        if (Logging.SaveFlag && 1 == LoggingSet.SaveFileType_int)
                        {
                            MessageBox.Show($"实时数据已录制至文件，无需内存缓存导出。\n录制文件：{Logging.NowBLFFileAddr_str}",
                                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("没有可保存的实时数据！请先开始接收实时数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        return;
                    }
                    messagesToSave = new List<CanRawMessage>(_realtimeRawMessages);
                    trimmed = _realtimeRawTrimmed;
                }
                if (trimmed)
                {
                    MessageBox.Show($"实时缓存曾超过上限，仅保留最近 {RealtimeRawMaxFrames} 帧（更早的数据未在内存中，如需要完整数据请开启BLF录制）",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存BLF文件";
            dialog.Filter = "BLF文件|*.blf";
            dialog.FileName = "export.blf";
            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                var messageList = new List<CAN_Data.blf.MessageBase>();
                foreach (var rawMsg in messagesToSave)
                {
                    byte[] data = new byte[8];
                    int copyLen = Math.Min(rawMsg.Data.Length, 8);
                    Array.Copy(rawMsg.Data, data, copyLen);

                    // rawMsg.Channel为1-based（实时逻辑通道/BLF mChannel），writeBLF内部+1写出，故传入-1保持原通道号
                    uint saveChannel = rawMsg.Channel > 0 ? (uint)(rawMsg.Channel - 1) : 0;
                    messageList.Add(new CAN_Data.blf.CANMessage(saveChannel, rawMsg.CanId, data, rawMsg.TimeStampSeconds));
                }

                CAN_Data.blf.BinlogReadWrite.writeBLF(dialog.FileName, messageList);
                MessageBox.Show($"BLF文件保存成功！\n共 {messagesToSave.Count} 条报文", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存BLF失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChartFrom_DragDrop(object sender, DragEventArgs e)
        {
            var files = ((Array)e.Data.GetData(DataFormats.FileDrop)).Cast<string>().ToList();
            var logFiles = new List<string>();

            // 分类文件（DBC请通过"通道配置"加载，拖拽不再支持）
            foreach (var path in files)
            {
                string ext = Path.GetExtension(path).ToLower();
                if (ext == ".blf" || ext == ".bin" || ext == ".asc")
                {
                    logFiles.Add(path);
                }
            }

            // 处理日志文件（支持多文件）
            if (logFiles.Count > 0)
            {
                if (RealTimeDataSta)
                {
                    SwitchToFileModeInternal();
                }
                LoadAndPlotFiles(logFiles);
            }
        }

        private void ChartFrom_DragEnter(object sender, DragEventArgs e)
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

        /// <summary>
        /// 截断曲线名称，最多保留20个字符
        /// </summary>
        private static string TruncateName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Length <= 20 ? name : name.Substring(0, 20) + "…";
        }


        /// <summary>
        /// 从BLF/BIN/ASC文件加载数据（只存储原始报文，不解析，点击开始后才解析绘制）
        /// </summary>
        private void LoadAndPlotFile(string filePath)
        {
            // 保留单文件兼容性，调用多文件版本
            LoadAndPlotFiles(new List<string> { filePath });
        }

        private void LoadAndPlotFiles(List<string> filePaths)
        {
            if (Channels == null || Channels.Count == 0)
            {
                MessageBox.Show("请先通过\"添加通道\"按钮选择要绘制的信号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查DBC是否可用：至少一个通道配置了DBC
            if (!_busChannels.Any(bc => bc.IsConfigured))
            {
                MessageBox.Show("请先在通道配置中为至少一个CAN通道加载DBC文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 断开PCAN/CANoe连接（含按钮文字、下拉框同步更新）
            Main.main.DisconnectPCAN();
            Main.main.DisconnectCANoe();

            // 切换到报文数据模式
            SwitchToFileModeInternal();
            
            // 更新文件路径列表
            _logFilePaths = new List<string>(filePaths);
            _streamingFilePath = filePaths.Count > 0 ? filePaths[0] : ""; // 保留兼容性
            _filePathTextBox.Text = $"已加载 {filePaths.Count} 个文件";

            // 停止实时模式
            if (RunStatus)
            {
                _btnStop_Click(null, null);
            }
            _isFileMode = false;

            // 清空现有数据
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                    channel.Clear();
            }
            _currentTime = 0;
            _isLoadingFile = true;
            _rawMessages = null;

            // 根据所有文件总大小决定模式（但不在这里加载数据，推迟到点击开始时）
            long totalSizeMB = 0;
            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    totalSizeMB += new FileInfo(filePath).Length / (1024 * 1024);
                }
            }
            int thresholdMB = GetStreamingThresholdMB();
            _streamingMode = (totalSizeMB > thresholdMB);
            _isFileMode = true;
            _fileMaxTime = 0;

            _isLoadingFile = false;
            _btnLoadFile.Enabled = true;
            _btnLoadFile.Text = "加载文件...";
            _btnStart.Enabled = true;
            _btnClear.Enabled = true;
            _statusLabel.Text = _streamingMode
                ? $"状态: 文件已加载(流式模式,{totalSizeMB}MB) — 点击开始播放"
                : $"状态: 文件已加载(内存模式,{totalSizeMB}MB) — 点击开始播放";
            _statusLabel.ForeColor = Color.Green;
            // 同步文件列表对话框数据:本次加载的文件勾选,其余不勾选(已在列表中的去重)
            foreach (var entry in _logFileEntries)
                entry.Enabled = false;
            foreach (var p in filePaths)
            {
                var existing = _logFileEntries.FirstOrDefault(
                    en => string.Equals(en.Path, p, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Enabled = true;
                else _logFileEntries.Add(new LogFileEntry { Path = p, Enabled = true });
            }
            SaveLogFilePaths();
            _txtEndTime.Text = "";
            SyncToolbarStateFromLegacyControls();
            // 清理图表
            foreach (var ch in Channels) ch.Clear();
            _chartControl.SetChannels(Channels);
            _chartControl.ResetView();
            _chartControl.Invalidate();
            UpdateChannelGridValues();
        }

        /// <summary>
        /// 预估文件中的CAN报文总数（用于进度条百分比显示）
        /// </summary>
        private long EstimateTotalMessages(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".blf")
                {
                    // BLF: 通过文件大小估算（每条约60字节）
                    FileInfo fi = new FileInfo(filePath);
                    return fi.Length / 60;
                }
                else if (ext == ".bin")
                {
                    // BIN: 每条约15字节
                    FileInfo fi = new FileInfo(filePath);
                    return fi.Length / 15;
                }
                else if (ext == ".asc")
                {
                    // ASC: 统计非空行数
                    long lineCount = 0;
                    using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8, true, 65536))
                        while (reader.ReadLine() != null) lineCount++;
                    return lineCount;
                }
            }
            catch { }
            return 0; // 无法预估时返回0，进度条不显示百分比
        }

        #region Signal Group Save/Load

        /// <summary>可序列化的信号组集合包装</summary>
        [XmlRoot("SignalGroups")]
        public class SignalGroupCollection
        {
            [XmlElement("Group")]
            public List<SignalGroupItem> Items { get; set; } = new List<SignalGroupItem>();
        }

        /// <summary>单个信号组条目</summary>
        public class SignalGroupItem
        {
            [XmlAttribute("name")]
            public string Name { get; set; }
            [XmlArray("Signals")]
            [XmlArrayItem("Signal")]
            public List<SignalPresetData> Signals { get; set; }
        }

        private string GetPresetsFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChartPresets.xml");
        }

        private Dictionary<string, List<SignalPresetData>> LoadAllPresets()
        {
            string filePath = GetPresetsFilePath();
            if (!File.Exists(filePath))
                return new Dictionary<string, List<SignalPresetData>>();
            try
            {
                string xml = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(xml))
                {
                    File.Delete(filePath);
                    return new Dictionary<string, List<SignalPresetData>>();
                }
                using (StringReader sr = new StringReader(xml))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(SignalGroupCollection));
                    var collection = (SignalGroupCollection)serializer.Deserialize(sr);
                    var dict = new Dictionary<string, List<SignalPresetData>>();
                    if (collection?.Items != null)
                    {
                        foreach (var item in collection.Items)
                        {
                            if (!string.IsNullOrEmpty(item.Name))
                                dict[item.Name] = item.Signals ?? new List<SignalPresetData>();
                        }
                    }
                    return dict;
                }
            }
            catch (Exception ex)
            {
                // 文件损坏，删除并重新开始
                try { File.Delete(filePath); } catch { }
                System.Diagnostics.Debug.WriteLine($"删除损坏的ChartPresets.xml: {ex.Message}");
                return new Dictionary<string, List<SignalPresetData>>();
            }
        }

        private void SaveAllPresets(Dictionary<string, List<SignalPresetData>> presets)
        {
            string filePath = GetPresetsFilePath();
            var collection = new SignalGroupCollection();
            collection.Items = new List<SignalGroupItem>();
            foreach (var kvp in presets)
            {
                collection.Items.Add(new SignalGroupItem
                {
                    Name = kvp.Key,
                    Signals = kvp.Value
                });
            }
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SignalGroupCollection));
                serializer.Serialize(fs, collection);
            }
        }

        private void RefreshPresetComboBox()
        {
            _presetComboBox.Items.Clear();
            _presetComboBox.Items.Add("请选择信号组...");
            var presets = LoadAllPresets();
            foreach (var key in presets.Keys.OrderBy(k => k))
            {
                _presetComboBox.Items.Add(key);
            }
            _presetComboBox.SelectedIndex = 0;
        }

        private bool _isLoadingPreset = false;

        private void _presetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isLoadingPreset) return;
            // 选中有效项（非"请选择信号组..."）时自动加载
            if (_presetComboBox.SelectedIndex > 0)
            {
                string name = _presetComboBox.SelectedItem.ToString();
                var allPresets = LoadAllPresets();
                if (allPresets.ContainsKey(name))
                {
                    _isLoadingPreset = true;
                    try
                    {
                        ApplySignalData(allPresets[name]);
                        _currentSignalGroupPath = GetPresetsFilePath();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"加载信号组失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        _isLoadingPreset = false;
                    }
                }
            }
        }

        private void _btnRemoveAll_Click(object sender, EventArgs e)
        {
            if (Channels == null || Channels.Count == 0)
            {
                MessageBox.Show("当前没有曲线可删除", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            lock (_lockObj)
            {
                foreach (var channel in Channels)
                {
                    channel.Clear();
                }
                Channels.Clear();
                _channelGrid.Rows.Clear();
            }

            _chartControl.SetChannels(Channels);
            _chartControl.ResetView();
            _statusLabel.Text = "状态: 已停止";
            _statusLabel.ForeColor = Color.Red;
        }

        private List<SignalPresetData> CollectCurrentChannels()
        {
            return Channels.Select(c => new SignalPresetData
            {
                SignalName = c.DbcSignalName,
                SignalComment = "",
                MsgId = (uint)c.DbcMessageId,
                MsgIndex = c.DbcMessageIndex,
                SignalIndex = c.DbcSignalIndex,
                CycleTime = (uint)(c.CycleTime * 1000),
                Unit = c.Unit ?? "",
                ColorArgb = c.Color.ToArgb(),
                BusChannelIndex = c.BusChannelIndex
            }).ToList();
        }

        /// <summary>
        /// 定位信号所属的通道DBC报文：优先按 BusChannelIndex+MsgIndex 直接取；
        /// 无效(旧数据-1或越界)时按 CAN ID+信号名 跨通道搜索并重定位。
        /// 找不到返回 null。
        /// </summary>
        private CAN_Data.Message ResolveChannelDbcMessage(int busChannelIndex, int msgIndex, uint msgId, string signalName,
            out int resolvedBusIdx, out int resolvedMsgIdx, out int resolvedSigIdx)
        {
            resolvedBusIdx = -1;
            resolvedMsgIdx = -1;
            resolvedSigIdx = -1;

            // 1) 直接按 BusChannelIndex+MsgIndex 取
            if (busChannelIndex >= 0 && busChannelIndex < _busChannels.Count)
            {
                var bc = _busChannels[busChannelIndex];
                if (bc.IsConfigured && msgIndex >= 0 && msgIndex < bc.DbcHelper.dbcFile.messages.Count)
                {
                    var msg = bc.DbcHelper.dbcFile.messages[msgIndex];
                    resolvedBusIdx = busChannelIndex;
                    resolvedMsgIdx = msgIndex;
                    resolvedSigIdx = string.IsNullOrEmpty(signalName) ? -1 :
                        msg.signals.FindIndex(s => s.signalName == signalName);
                    return msg;
                }
            }

            // 2) 旧数据重定位：按 CAN ID+信号名 跨通道搜索
            for (int bi = 0; bi < _busChannels.Count; bi++)
            {
                var bc = _busChannels[bi];
                if (!bc.IsConfigured || bc.DbcHelper?.dbcFile?.messageDict == null) continue;
                if (!bc.DbcHelper.dbcFile.messageDict.TryGetValue(msgId, out var chMsg)) continue;
                int sigIdx = string.IsNullOrEmpty(signalName) ? 0 :
                    chMsg.signals.FindIndex(s => s.signalName == signalName);
                if (sigIdx < 0) continue;

                resolvedBusIdx = bi;
                resolvedMsgIdx = bc.DbcHelper.dbcFile.messages.IndexOf(chMsg);
                resolvedSigIdx = sigIdx;
                return chMsg;
            }
            return null;
        }

        /// <summary>通道配置(BusChannels)整体替换后，按当前曲线信号重新在各自通道DBC报文对象上置位ChartShowFlag并重建周期调度注册。
        /// 替换会新建DbcHelper/Message对象（通道管理窗口每次保存/连接前保存都会重建），原置位丢失导致实时喂点中断——
        /// 表现为报文列表解析正常但曲线"没收到报文"。</summary>
        internal void RestoreChartShowFlags()
        {
            if (Channels == null) return;
            foreach (var ch in Channels)
            {
                var msg = ResolveChannelDbcMessage(ch.BusChannelIndex, ch.DbcMessageIndex, (uint)ch.DbcMessageId, ch.DbcSignalName,
                    out _, out _, out int sigIdx);
                if (msg == null) continue;
                int si = sigIdx >= 0 ? sigIdx : ch.DbcSignalIndex;
                if (si >= 0 && si < msg.signals.Count)
                    msg.signals[si].ChartShowFlag = true;
                // 周期调度注册的旧Message对象已作废：Remove再Add换新对象（复合键按通道区分，同ID多通道互不干扰）
                byte logicCh = BaseParamter.GetLogicChannel(ch.BusChannelIndex);
                multiChartFromScheduler.RemoveMessage(msg.messgeId, logicCh);
                multiChartFromScheduler.AddMessage(msg, ch.CycleTime > 0 ? (uint)(ch.CycleTime * 1000) : 0, logicCh);
            }
        }

        private void ApplySignalData(List<SignalPresetData> signals)
        {
            // 清空当前曲线
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                {
                    channel.Clear();
                }
                Channels.Clear();
                _channelGrid.Rows.Clear();
            }

            Color[] colors = new Color[]
            {
                Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple,
                Color.DeepSkyBlue, Color.Crimson, Color.Brown, Color.DarkBlue, Color.DarkGreen,
                Color.DarkRed, Color.DarkOrange, Color.DarkCyan, Color.DarkMagenta, Color.Indigo,
                Color.MediumSeaGreen, Color.Maroon, Color.Navy, Color.OliveDrab, Color.SteelBlue
            };

            foreach (var signal in signals)
            {
                // 定位信号所属的通道DBC报文（旧数据 BusChannelIndex=-1 自动重定位；找不到则跳过）
                CAN_Data.Message msg = ResolveChannelDbcMessage(
                    signal.BusChannelIndex, signal.MsgIndex, signal.MsgId, signal.SignalName,
                    out int busIdx, out int msgIdx, out int sigIdx);
                if (msg == null) continue;

                // 重定位到通道DBC索引
                signal.BusChannelIndex = busIdx;
                signal.MsgIndex = msgIdx;
                if (sigIdx >= 0) signal.SignalIndex = sigIdx;

                int channelCount = Channels.Count;
                Color color = signal.ColorArgb != 0 ? Color.FromArgb(signal.ColorArgb) : colors[channelCount % colors.Length];
                string channelName = signal.SignalName;
                if (!string.IsNullOrEmpty(signal.SignalComment))
                    channelName = TruncateName($"{signal.SignalName} ({signal.SignalComment})");

                // 从 DBC 信号中获取枚举值定义
                var enumDefs = new Dictionary<double, string>();
                try
                {
                    if (signal.SignalIndex >= 0 && signal.SignalIndex < msg.signals.Count)
                    {
                        var dbcSignal = msg.signals[signal.SignalIndex];
                        if (dbcSignal.enumDefinitions != null && dbcSignal.enumDefinitions.Count > 0)
                        {
                            enumDefs = dbcSignal.enumDefinitions;
                        }
                    }
                }
                catch { }

                ChannelData presetCh = new ChannelData(channelName, color, DateTime.Now,
                    enumDefs, signal.Unit,
                    signal.CycleTime / 1000.0, (int)signal.MsgId,
                    signal.MsgIndex, signal.SignalIndex, signal.SignalName,
                    signal.BusChannelIndex);
                presetCh.LineWidth = _globalLineWidth;   // 继承当前全局线宽
                presetCh.DotSize = _globalDotSize;       // 继承当前全局数据点大小
                Channels.Add(presetCh);
                multiChartFromScheduler.AddMessage(msg, signal.CycleTime, (byte)(busIdx + 1));

                // 设置ChartShowFlag到通道DBC实例
                try
                {
                    if (signal.SignalIndex >= 0 && signal.SignalIndex < msg.signals.Count)
                        msg.signals[signal.SignalIndex].ChartShowFlag = true;
                }
                catch { }
            }

            PopulateChannelGrid();
            _chartControl.SetChannels(Channels);
            _chartControl.ResetView();
            _chartControl.SetAutoScroll(true);
            _btnAutoScroll.Text = "停止滑动";
            _statusLabel.Text = "状态: 已停止";
            _statusLabel.ForeColor = Color.Red;
        }

        private void _btnSavePreset_Click(object sender, EventArgs e)
        {
            if (Channels == null || Channels.Count == 0)
            {
                MessageBox.Show("当前没有曲线数据可保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedName = "";
            if (_presetComboBox.SelectedIndex > 0)
                selectedName = _presetComboBox.SelectedItem.ToString();

            // 弹出输入框让用户输入信号组名称
            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入信号组名称:",
                "保存信号组",
                selectedName,
                -1, -1);

            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                var signals = CollectCurrentChannels();
                var presets = LoadAllPresets();

                // 如果已存在，询问是否覆盖
                if (presets.ContainsKey(name))
                {
                    var result = MessageBox.Show($"信号组 \"{name}\" 已存在，是否覆盖？", "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result != DialogResult.Yes) return;
                }

                presets[name] = signals;
                SaveAllPresets(presets);
                _currentSignalGroupPath = GetPresetsFilePath();
                RefreshPresetComboBox();
                _presetComboBox.SelectedItem = name;
                MessageBox.Show($"信号组 \"{name}\" 保存成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存信号组失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void _btnLoadPreset_Click(object sender, EventArgs e)
        {
            if (_isLoadingPreset) return;
            _isLoadingPreset = true;
            try
            {
                // 始终弹出选择对话框让用户选择信号组
                string selectedName = ShowPresetSelectionDialog();
                if (selectedName == null)
                    return;

                var allPresets = LoadAllPresets();
                if (!allPresets.ContainsKey(selectedName))
                {
                    MessageBox.Show($"信号组 \"{selectedName}\" 不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RefreshPresetComboBox();
                    return;
                }

                ApplySignalData(allPresets[selectedName]);
                _currentSignalGroupPath = GetPresetsFilePath();
                RefreshPresetComboBox();
                _presetComboBox.SelectedItem = selectedName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载信号组失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoadingPreset = false;
            }
        }

        /// <summary>弹出选择信号组对话框</summary>
        private string ShowPresetSelectionDialog()
        {
            var presets = LoadAllPresets();
            if (presets.Count == 0)
            {
                MessageBox.Show("没有可加载的信号组，请先保存一个信号组", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            string[] names = presets.Keys.OrderBy(k => k).ToArray();

            using (var form = new Form())
            {
                form.Text = "加载信号组";
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new System.Drawing.Size(360, 420);
                form.MinimumSize = new System.Drawing.Size(300, 350);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ShowInTaskbar = false;
                form.BackColor = Color.White;

                const int margin = 14;
                int cw = form.ClientSize.Width;
                int ch = form.ClientSize.Height;

                // 顶部提示标签
                var lblHint = new Label();
                lblHint.Text = "请选择一个信号组：";
                lblHint.Location = new Point(margin, margin);
                lblHint.Size = new Size(cw - margin * 2, 20);
                lblHint.Font = new Font("Microsoft YaHei UI", 9F);

                // 信号组列表
                int btnAreaHeight = 28 + margin + 8; // 按钮+间距+留白
                var listBox = new ListBox();
                listBox.Location = new Point(margin, lblHint.Bottom + 6);
                listBox.Size = new Size(cw - margin * 2, ch - listBox.Top - btnAreaHeight);
                listBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                listBox.Items.AddRange(names);
                listBox.SelectedIndex = 0;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                listBox.Font = new Font("Microsoft YaHei UI", 9F);
                listBox.IntegralHeight = false;

                // 确定+取消按钮（底部居中）
                int btnW = 80;
                int btnH = 28;
                int gap = 10;
                int totalW = btnW * 2 + gap;
                int btnY = ch - margin - btnH;
                int btnStartX = (cw - totalW) / 2;

                var okBtn = new Button();
                okBtn.Text = "确  定";
                okBtn.DialogResult = DialogResult.OK;
                okBtn.Location = new Point(btnStartX, btnY);
                okBtn.Size = new System.Drawing.Size(btnW, btnH);
                okBtn.Anchor = AnchorStyles.Bottom;
                okBtn.Font = new Font("Microsoft YaHei UI", 9F);
                okBtn.FlatStyle = FlatStyle.Flat;

                var cancelBtn = new Button();
                cancelBtn.Text = "取  消";
                cancelBtn.DialogResult = DialogResult.Cancel;
                cancelBtn.Location = new Point(btnStartX + btnW + gap, btnY);
                cancelBtn.Size = new System.Drawing.Size(btnW, btnH);
                cancelBtn.Anchor = AnchorStyles.Bottom;
                cancelBtn.Font = new Font("Microsoft YaHei UI", 9F);
                cancelBtn.FlatStyle = FlatStyle.Flat;

                // 双击列表项直接确认
                listBox.DoubleClick += (s, args) =>
                {
                    if (listBox.SelectedItem != null)
                    {
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                };

                form.Controls.Add(lblHint);
                form.Controls.Add(listBox);
                form.Controls.Add(okBtn);
                form.Controls.Add(cancelBtn);

                if (form.ShowDialog(this) != DialogResult.OK || listBox.SelectedItem == null)
                    return null;

                return listBox.SelectedItem.ToString();
            }
        }

        private void _btnDeletePreset_Click(object sender, EventArgs e)
        {
            if (_presetComboBox.SelectedIndex <= 0)
            {
                MessageBox.Show("请先在预设列表中选择一个要删除的信号组", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedName = _presetComboBox.SelectedItem.ToString();
            var result = MessageBox.Show($"确定要删除信号组 \"{selectedName}\" 吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            try
            {
                var presets = LoadAllPresets();
                if (presets.Remove(selectedName))
                {
                    SaveAllPresets(presets);
                    if (_currentSignalGroupPath == GetPresetsFilePath())
                        _currentSignalGroupPath = "";
                    RefreshPresetComboBox();
                    MessageBox.Show($"信号组 \"{selectedName}\" 已删除", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"信号组 \"{selectedName}\" 不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除信号组失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion


        public class dateread
        {
            public double[] saveExcelTimeBuf = new double[8] { 0.1, 0.5, 1, 2, 3, 4, 5, 10 };
            public bool saveExcelFlag = false;
            public bool saveAscFlag = false;
            public uint saveExcelTime = 1000; /* ms */
            public uint comboBoxExcelSaveTimeSelectIndex = 0;
            public bool changeFlag = false;


        }

        /// <summary>获取所有通道中的最大时间点</summary>
        private double GetMaxChannelTime()
        {
            if (Channels == null || Channels.Count == 0)
                return 10.0;

            double maxTime = 0;
            foreach (var channel in Channels)
            {
                if (channel.Points != null && channel.Points.Count > 0)
                {
                    var lastPoint = channel.Points[channel.Points.Count - 1];
                    if (lastPoint.X > maxTime)
                        maxTime = lastPoint.X;
                }
            }

            return maxTime > 0 ? maxTime : 10.0;
        }
    }

    /// <summary>单个信号预设数据</summary>
    [Serializable]
    public class SignalPresetData
    {
        public string SignalName { get; set; }
        public string SignalComment { get; set; }
        public uint MsgId { get; set; }
        public int MsgIndex { get; set; }
        public int SignalIndex { get; set; }
        public uint CycleTime { get; set; }
        public string Unit { get; set; }
        public int ColorArgb { get; set; } // 保存曲线颜色（ARGB整数值）
        public int BusChannelIndex { get; set; } = -1; // CAN总线通道索引（-1=兼容模式，使用全局DBC）
    }

}
