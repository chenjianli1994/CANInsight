using PCAN_Client.CAN_Data;
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
        private Button _btnLoadDbc;
        private ToolStrip _topToolStrip;
        private ToolStripButton _toolLoadProject, _toolLoadDbc, _toolLoadLog, _toolModeToggle, _toolStart, _toolStop, _toolShowAll, _toolAutoScroll, _toolClear, _toolAddSignal;
        private ToolStripComboBox _toolSpeedComboBox;
        private ToolStripDropDownButton _toolMore;
        private ToolStripTextBox _txtStartTime;
        private ToolStripTextBox _txtEndTime;
        private ToolStripTextBox _txtStreamingThreshold;
        private ToolStripButton _btnShowData;
        private ToolStripButton _btnChannelFilter;
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
        private bool _loadingComplete = false;
        private ToolStripMenuItem menuItemSelectAll;
        private ToolStripMenuItem menuItemDeselectAll;
        private ToolStripMenuItem menuItemDelete;
        private ToolStripMenuItem menuItemAddSignal;
        private ToolStripMenuItem menuItemClearAll;
        private bool _isLoadingFile = false;
        // 文件回放相关字段
        private List<CanRawMessage> _rawMessages;                // 原始报文存储（加载时只存不解析）
        private bool _streamingMode = false;                     // 流式模式（大文件>150MB时不加载到内存）
        private string _streamingFilePath;                       // 流式模式下的文件路径
        private IEnumerator<CanRawMessage> _streamingEnumerator; // 流式播放时的文件枚举器
        private CanRawMessage _streamingPendingMessage;          // 流式模式下等待下一Tick处理的报文
        private long _streamingTotalCount;                       // 流式模式下报文总数（仅用于状态显示）
        private double _fileMaxTime = 0;                         // 文件中的最大时间戳
        private bool _isFileMode = false;                        // 是否为文件模式（已加载文件数据）
        private volatile bool _loadingCancelled;                  // 加载取消标记（内存模式）
        private volatile bool _cancelPlayback;                     // 停止播放标记（流式模式）
        private volatile bool _cacheWhileStreaming;               // 内存模式首次播放：边读边缓存
        private System.Windows.Forms.Timer _playbackTimer;       // 回放定时器
        private double _playbackSpeed = 0;                       // 播放倍速（0=最快）
        private int _playbackRawIndex = 0;                       // _rawMessages 中的当前播放位置
        private double _playbackStartWallTime = 0;               // 开始播放时的 wall clock（秒）
        private Dictionary<uint, List<(CAN_Data.Signal signal, int channelIndex)>> _canIdSignalMap; // 播放用查找表
        // 实时数据BLF保存相关
        private static List<CanRawMessage> _realtimeRawMessages = new List<CanRawMessage>();
        private static readonly object _realtimeRawLock = new object();
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
        /// 记录实时接收的原始CAN报文（用于保存BLF）
        /// </summary>
        internal static void RecordRealtimeRawMessage(uint canId, byte[] data)
        {
            if (Main.chartFromShow == null || Main.chartFromShow._isFileMode || !Main.chartFromShow.RunStatus)
                return;
            double timestamp = (DateTime.Now - Main.chartFromShow.startTime).TotalSeconds;
            byte[] dataCopy = new byte[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            lock (_realtimeRawLock)
            {
                _realtimeRawMessages.Add(new CanRawMessage
                {
                    CanId = canId,
                    Data = dataCopy,
                    TimeStampSeconds = (float)timestamp
                });
            }
        }
        public ChartFrom()
        {
            InitializeComponent();
            // _chartControl 创建和配置（放在这里避免设计器无法序列化自定义控件）
            this._chartControl = new PCAN_Client.ChartControl();
            this._chartControl.BackColor = System.Drawing.Color.White;
            this._chartControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this._chartControl.Name = "_chartControl";
            this._chartControl.TabIndex = 0;
            this.splitContainer.Panel2.Controls.Add(this._chartControl);
            InitializeToolbarBindings();
            InitReportToolbar();
            InitializeChannelGrid();
            // 默认播放速度为"最快"
            if (_speedComboBox.Items.Count > 0)
                _speedComboBox.SelectedIndex = 0;
            this.KeyPreview = true;
            this._loadingRefreshTimer = new System.Windows.Forms.Timer();
            this._loadingRefreshTimer.Interval = 100;
            this._loadingRefreshTimer.Tick += _loadingRefreshTimer_Tick;
            // 初始化回放定时器
            this._playbackTimer = new System.Windows.Forms.Timer();
            this._playbackTimer.Interval = 20;  // 50fps
            this._playbackTimer.Tick += _playbackTimer_Tick;
            // 加载已保存的流式阈值
            int savedThreshold = PCAN_Client.Properties.Settings.Default.StreamingThresholdMB;
            if (savedThreshold >= 50)
                _txtStreamingThreshold.Text = savedThreshold.ToString();
            else
                _txtStreamingThreshold.Text = "150";
            this._chartControl.OnAutoScrollChanged += (enabled) =>
            {
                string text = enabled ? "停止滑动" : "自动滑动";
                if (InvokeRequired)
                    Invoke(new Action(() => { _btnAutoScroll.Text = text; _toolAutoScroll.Text = text; }));
                else
                    { _btnAutoScroll.Text = text; _toolAutoScroll.Text = text; }
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

            // 初始化单选按钮状态
            _toolModeToggle.Text = RealTimeDataSta ? "实时数据" : "报文数据";

            // 加载上次保存的通道筛选设置
            var savedFilter = (string)PCAN_Client.Properties.Settings.Default["ChartChannelFilter"];
            if (!string.IsNullOrEmpty(savedFilter))
            {
                var channels = new HashSet<byte>();
                foreach (var part in savedFilter.Split(','))
                {
                    if (byte.TryParse(part.Trim(), out byte ch))
                        channels.Add(ch);
                }
                if (channels.Count > 0)
                {
                    _selectedChannels = channels;
                    _btnChannelFilter.Text = $"通道({channels.Count})";
                }
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
            this._btnLoadDbc = new System.Windows.Forms.Button();
            this._btnLoadFile = new System.Windows.Forms.Button();
            this._topToolStrip = new System.Windows.Forms.ToolStrip();
            this._toolLoadProject = new System.Windows.Forms.ToolStripButton();
            this._toolLoadDbc = new System.Windows.Forms.ToolStripButton();
            this._toolLoadLog = new System.Windows.Forms.ToolStripButton();
            this._toolModeToggle = new System.Windows.Forms.ToolStripButton();
            this._toolStart = new System.Windows.Forms.ToolStripButton();
            this._toolStop = new System.Windows.Forms.ToolStripButton();
            this._toolSpeedComboBox = new System.Windows.Forms.ToolStripComboBox();
            this._toolShowAll = new System.Windows.Forms.ToolStripButton();
            this._toolAutoScroll = new System.Windows.Forms.ToolStripButton();
            this._toolClear = new System.Windows.Forms.ToolStripButton();
            this._toolAddSignal = new System.Windows.Forms.ToolStripButton();
            this._toolMore = new System.Windows.Forms.ToolStripDropDownButton();
            this._txtStartTime = new System.Windows.Forms.ToolStripTextBox();
            this._txtEndTime = new System.Windows.Forms.ToolStripTextBox();
            this._btnShowData = new System.Windows.Forms.ToolStripButton();
            this._btnChannelFilter = new System.Windows.Forms.ToolStripButton();
            this._txtStreamingThreshold = new System.Windows.Forms.ToolStripTextBox();
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
            // _btnLoadDbc
            // 
            this._btnLoadDbc.Location = new System.Drawing.Point(11, 615);
            this._btnLoadDbc.Name = "_btnLoadDbc";
            this._btnLoadDbc.Size = new System.Drawing.Size(277, 28);
            this._btnLoadDbc.TabIndex = 18;
            this._btnLoadDbc.Text = "加载DBC文件...";
            this._btnLoadDbc.Click += new System.EventHandler(this._btnLoadDbc_Click);
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
            // _topToolStrip
            // 
            this._topToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this._topToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this._topToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._toolLoadProject,
            this._toolLoadDbc,
            this._toolLoadLog,
            this._toolModeToggle,
            this._toolStart,
            this._toolStop,
            this._toolSpeedComboBox,
            this._toolShowAll,
            this._toolAutoScroll,
            this._toolClear,
            this._toolAddSignal,
            this._toolMore,
            this._txtStartTime,
            this._txtEndTime,
            this._btnShowData,
            this._btnChannelFilter,
            this._txtStreamingThreshold});
            this._topToolStrip.Location = new System.Drawing.Point(0, 0);
            this._topToolStrip.Name = "_topToolStrip";
            this._topToolStrip.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this._topToolStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this._topToolStrip.Size = new System.Drawing.Size(1480, 37);
            this._topToolStrip.TabIndex = 1;
            // 
            // _toolLoadProject
            // 
            this._toolLoadProject.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolLoadProject.Name = "_toolLoadProject";
            this._toolLoadProject.Size = new System.Drawing.Size(72, 22);
            this._toolLoadProject.Text = "加载信号组";
            this._toolLoadProject.ToolTipText = "加载已保存的信号组";
            this._toolLoadProject.Click += new System.EventHandler(this._btnLoadPreset_Click);
            // 
            // _toolLoadDbc
            // 
            this._toolLoadDbc.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolLoadDbc.Name = "_toolLoadDbc";
            this._toolLoadDbc.Size = new System.Drawing.Size(61, 22);
            this._toolLoadDbc.Text = "加载DBC";
            this._toolLoadDbc.ToolTipText = "加载DBC文件";
            this._toolLoadDbc.Click += new System.EventHandler(this._btnLoadDbc_Click);
            // 
            // _toolLoadLog
            // 
            this._toolLoadLog.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolLoadLog.Name = "_toolLoadLog";
            this._toolLoadLog.Size = new System.Drawing.Size(60, 22);
            this._toolLoadLog.Text = "加载报文";
            this._toolLoadLog.ToolTipText = "加载BLF/BIN/ASC文件";
            this._toolLoadLog.Click += new System.EventHandler(this._btnLoadFile_Click);
            // 
            // _toolModeToggle
            // 
            this._toolModeToggle.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolModeToggle.Name = "_toolModeToggle";
            this._toolModeToggle.Size = new System.Drawing.Size(60, 22);
            this._toolModeToggle.Text = "实时数据";
            this._toolModeToggle.Click += new System.EventHandler(this._btnModeToggle_Click);
            // 
            // _toolStart
            // 
            this._toolStart.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolStart.Name = "_toolStart";
            this._toolStart.Size = new System.Drawing.Size(36, 22);
            this._toolStart.Text = "开始";
            this._toolStart.ToolTipText = "开始播放或绘制";
            this._toolStart.Click += new System.EventHandler(this._btnStart_Click);
            // 
            // _toolStop
            // 
            this._toolStop.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
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
            // _toolShowAll
            // 
            this._toolShowAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolShowAll.Name = "_toolShowAll";
            this._toolShowAll.Size = new System.Drawing.Size(60, 22);
            this._toolShowAll.Text = "全部显示";
            this._toolShowAll.ToolTipText = "显示全部时间范围";
            this._toolShowAll.Click += new System.EventHandler(this._btnShowAll_Click);
            // 
            // _toolAutoScroll
            // 
            this._toolAutoScroll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolAutoScroll.Name = "_toolAutoScroll";
            this._toolAutoScroll.Size = new System.Drawing.Size(60, 22);
            this._toolAutoScroll.Text = "自动滑动";
            this._toolAutoScroll.ToolTipText = "切换自动滑动";
            this._toolAutoScroll.Click += new System.EventHandler(this._btnAutoScroll_Click);
            // 
            // _toolClear
            // 
            this._toolClear.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolClear.Name = "_toolClear";
            this._toolClear.Size = new System.Drawing.Size(36, 22);
            this._toolClear.Text = "清除";
            this._toolClear.ToolTipText = "清除当前曲线数据";
            this._toolClear.Click += new System.EventHandler(this._btnClear_Click);
            // 
            // _toolAddSignal
            // 
            this._toolAddSignal.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._toolAddSignal.Name = "_toolAddSignal";
            this._toolAddSignal.Size = new System.Drawing.Size(60, 22);
            this._toolAddSignal.Text = "添加信号";
            this._toolAddSignal.ToolTipText = "添加DBC信号";
            this._toolAddSignal.Click += new System.EventHandler(this._btnAddChannel_Click);
            // 
            // _toolMore
            // 
            this._toolMore.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
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
            this._btnShowData.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._btnShowData.Name = "_btnShowData";
            this._btnShowData.Size = new System.Drawing.Size(60, 22);
            this._btnShowData.Text = "数据显示";
            this._btnShowData.ToolTipText = "显示指定时间范围内的数据";
            this._btnShowData.Click += new System.EventHandler(this._btnShowData_Click);
            // 
            // _btnChannelFilter
            // 
            this._btnChannelFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this._btnChannelFilter.Name = "_btnChannelFilter";
            this._btnChannelFilter.Size = new System.Drawing.Size(60, 22);
            this._btnChannelFilter.Text = "通道筛选";
            this._btnChannelFilter.ToolTipText = "选择要显示的CAN通道（默认全部）";
            this._btnChannelFilter.Click += new System.EventHandler(this._btnChannelFilter_Click);
            // 
            // _txtStreamingThreshold
            // 
            this._txtStreamingThreshold.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this._txtStreamingThreshold.Name = "_txtStreamingThreshold";
            this._txtStreamingThreshold.Size = new System.Drawing.Size(50, 25);
            this._txtStreamingThreshold.Text = "150";
            this._txtStreamingThreshold.ToolTipText = "流式阈值（MB，默认150）";
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
            this._channelGrid.MultiSelect = false;
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
            this._filePathTextBox.Location = new System.Drawing.Point(6, 30);
            this._filePathTextBox.Name = "_filePathTextBox";
            this._filePathTextBox.ReadOnly = true;
            this._filePathTextBox.Size = new System.Drawing.Size(288, 21);
            this._filePathTextBox.TabIndex = 19;
            // 
            // _progressLabel
            // 
            this._progressLabel.AutoSize = true;
            this._progressLabel.ForeColor = System.Drawing.Color.Blue;
            this._progressLabel.Location = new System.Drawing.Point(258, 67);
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
            this.Controls.Add(this._topToolStrip);
            this.Name = "ChartFrom";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "曲线绘制工具";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ChartFrom_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.ChartFrom_FormClosed);
            this.Load += new System.EventHandler(this.ChartFrom_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.ChartFrom_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.ChartFrom_DragEnter);
            this._controlGroup.ResumeLayout(false);
            this._controlGroup.PerformLayout();
            this._topToolStrip.ResumeLayout(false);
            this._topToolStrip.PerformLayout();
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

            _toolMore.DropDownItems.Add(new ToolStripMenuItem("保存BLF", null, _btnSaveBlf_Click));
            _toolMore.DropDownItems.Add(new ToolStripMenuItem("保存信号组", null, _btnSavePreset_Click));
            _toolMore.DropDownItems.Add(new ToolStripSeparator());
            _toolMore.DropDownItems.Add(new ToolStripMenuItem("删除全部", null, _btnRemoveAll_Click));

            SyncToolbarStateFromLegacyControls();
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
            _toolLoadDbc.Enabled = _btnLoadDbc.Enabled;
            _toolLoadLog.Enabled = _btnLoadFile.Enabled;
            _toolLoadProject.Enabled = true;
            _toolAddSignal.Enabled = _btnAddChannel.Enabled;
            _toolAutoScroll.Enabled = _btnAutoScroll.Enabled;
            _toolAutoScroll.Text = _btnAutoScroll.Text;
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
            for (int i = 0; i < Channels.Count; i++)
            {
                var channel = Channels[i];
                // 超过30条以后的信号在列表中默认取消勾选，但保留在Channels中
                bool isChecked = channel.Visible && i < 30;
                if (i >= 30 && channel.Visible)
                {
                    channel.Visible = false;
                }
                int rowIndex = _channelGrid.Rows.Add(isChecked, string.Empty, GetChannelDisplayName(channel), string.Empty, string.Empty);
                _channelGrid.Rows[rowIndex].Tag = channel;
                _channelGrid.Rows[rowIndex].Cells["Color"].ToolTipText = "点击修改颜色";
                _channelGrid.Rows[rowIndex].Cells["Signal"].Style.ForeColor = SystemColors.ControlText;
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
            Main.ChartShowOpenFlag = false;
            // 关闭ChartFrom后恢复VersionCheck自动重连
            Main.RestartConnectFlag = true;

            // 主动触发GC回收大块内存（gen2）
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void ChartFrom_FormClosing(object sender, FormClosingEventArgs e)
        {
            _playbackTimer.Stop();
            multiChartFromScheduler.Stop();
            Main.ChartShowOpenFlag = false;

            // 清除Main.cs的导入数据
            Main.main.ClearForPlayback();

            // 清除ChartFrom自身的大数据
            lock (_realtimeRawLock)
            {
                _realtimeRawMessages.Clear();
                _realtimeRawMessages.TrimExcess();
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

        private void _btnModeToggle_Click(object sender, EventArgs e)
        {
            if (RealTimeDataSta)
            {
                SwitchToFileData();
                _toolModeToggle.Text = "报文数据";
            }
            else
            {
                SwitchToRealTimeData();
                _toolModeToggle.Text = "实时数据";
            }
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

                // 断开PCAN/CANoe连接（含按钮文字、下拉框同步更新）
                Main.main.DisconnectPCAN();
                Main.main.DisconnectCANoe();
                // 报文数据模式下禁用VersionCheck自动重连
                Main.RestartConnectFlag = false;

                // 清空Main界面数据并切换到Scroll模式
                Main.main.ClearForPlayback();

                // 清空通道数据
                lock (_lockObj)
                {
                    foreach (var channel in Channels)
                        channel.Clear();
                }
                _currentTime = 0;

                // 预构建 CAN ID → (signal, channelIndex) 查找表
                var canIdSignalMap = new Dictionary<uint, List<(CAN_Data.Signal signal, int channelIndex)>>();
                var msgDict = BaseParamter.dbcHelper.dbcFile.messageDict;
                for (int ci = 0; ci < Channels.Count; ci++)
                {
                    var channel = Channels[ci];
                    if (channel.DbcMessageIndex < 0 || channel.DbcMessageIndex >= BaseParamter.dbcHelper.dbcFile.messages.Count)
                        continue;
                    var msg = BaseParamter.dbcHelper.dbcFile.messages[channel.DbcMessageIndex];
                    if (channel.DbcSignalIndex < 0 || channel.DbcSignalIndex >= msg.signals.Count)
                        continue;
                    var sig = msg.signals[channel.DbcSignalIndex];
                    uint canId = msg.messgeId;

                    if (!canIdSignalMap.TryGetValue(canId, out var list))
                        list = new List<(CAN_Data.Signal, int)>();
                    list.Add((sig, ci));
                    canIdSignalMap[canId] = list;
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
                    }));

                    Task.Run(() =>
                    {
                        try
                        {
                            var parser = new CAN_Data.CanSignalParser();
                            double maxTime = 0;
                            long msgCount = 0;
                            long lastUpdateMsgCount = 0;
                            var updateStopwatch = System.Diagnostics.Stopwatch.StartNew();

                            foreach (var rawMsg in EnumerateRawMessages())
                            {
                                msgCount++;
                                if (canIdSignalMap.TryGetValue(rawMsg.CanId, out var signalList))
                                {
                                    if (msgDict.TryGetValue(rawMsg.CanId, out var dbcMessage))
                                    {
                                        var allValues = parser.ParseSignals(rawMsg.Data, dbcMessage.signals);
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

                                // 流式模式：记录最新的5w帧原始报文
                                if (isStreaming)
                                    RecordStreamingFrame(rawMsg);

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
                                // 缓存模式：切回内存模式
                                FinalizeCachePlayback(totalMsgCount);
                            }));

                            // 流式模式：播放完毕后将记录的5w帧显示到Main.cs界面
                            if (isStreaming)
                            {
                                Invoke(new Action(() =>
                                {
                                    ShowStreamingRecordedFrames();
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
                }

                _chartControl.ResetView();
                multiChartFromScheduler.Start();
                _chartControl.SetAutoScroll(true);
                _btnAutoScroll.Text = "停止滑动";
                RunStatus = true;
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
            var parser = new CAN_Data.CanSignalParser();
            var msgDict = BaseParamter.dbcHelper.dbcFile.messageDict;

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

                // 通道筛选：跳过不在选中通道的报文
                if (!IsChannelMatched(rawMsg))
                {
                    _playbackRawIndex++;
                    messagesThisTick++;
                    continue;
                }

                // 解析此报文中的信号并添加到对应通道
                if (_canIdSignalMap.TryGetValue(rawMsg.CanId, out var signalList))
                {
                    if (msgDict.TryGetValue(rawMsg.CanId, out var dbcMessage))
                    {
                        var allValues = parser.ParseSignals(rawMsg.Data, dbcMessage.signals);
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

                // 同步显示到Main界面
                TPCANMsg tMsg = new TPCANMsg();
                tMsg.ID = rawMsg.CanId;
                tMsg.LEN = (byte)rawMsg.Data.Length;
                tMsg.DATA = rawMsg.Data;
                ulong tUs = (ulong)(rawMsg.TimeStampSeconds * 1000000.0);
                Main.main.RecordCanMessage(tMsg, tUs, false);

                // 流式模式：记录最新的5w帧原始报文
                if (_streamingMode)
                    RecordStreamingFrame(rawMsg);

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

                    SyncToolbarStateFromLegacyControls();
                    // 缓存模式：切回内存模式
                    FinalizeCachePlayback(totalMsgCount);
                }));
            }
        }

        private void _btnChannelFilter_Click(object sender, EventArgs e)
        {
            using (var form = new Form())
            {
                form.Text = "选择CAN通道";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new Size(220, 320);

                var clb = new CheckedListBox
                {
                    CheckOnClick = true,
                    Dock = DockStyle.Top,
                    Height = 240,
                    IntegralHeight = false
                };
                for (int i = 0; i <= 15; i++)
                    clb.Items.Add($"通道 {i}", _selectedChannels == null || _selectedChannels.Contains((byte)i));

                var btnAll = new Button { Text = "全选", Width = 70, Left = 10, Top = 245 };
                btnAll.Click += (ss, ee) => { for (int j = 0; j < clb.Items.Count; j++) clb.SetItemChecked(j, true); };

                var btnNone = new Button { Text = "全不选", Width = 70, Left = 85, Top = 245 };
                btnNone.Click += (ss, ee) => { for (int j = 0; j < clb.Items.Count; j++) clb.SetItemChecked(j, false); };

                var btnOk = new Button { Text = "确定", Width = 80, Left = 70, Top = 275, DialogResult = DialogResult.OK };
                form.Controls.AddRange(new Control[] { clb, btnAll, btnNone, btnOk });

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    var selected = new HashSet<byte>();
                    for (int j = 0; j < clb.Items.Count; j++)
                    {
                        if (clb.GetItemChecked(j))
                            selected.Add((byte)j);
                    }
                    _selectedChannels = selected.Count == 0 ? null : selected;
                    _btnChannelFilter.Text = _selectedChannels == null ? "通道筛选" : $"通道({_selectedChannels.Count})";
                    // 保存到设置
                    PCAN_Client.Properties.Settings.Default["ChartChannelFilter"] = _selectedChannels == null ? "" : string.Join(",", _selectedChannels);
                    PCAN_Client.Properties.Settings.Default.Save();
                }
            }
        }

        private bool IsChannelMatched(CanRawMessage msg)
        {
            return _selectedChannels == null || _selectedChannels.Contains(msg.Channel);
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
            if (BaseParamter.dbcHelper == null || BaseParamter.dbcHelper.dbcFile == null)
            {
                MessageBox.Show("请先加载DBC文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            // 3. 过滤时间范围内的原始报文
            List<CanRawMessage> filteredMessages;
            if (_streamingMode)
            {
                // 流式模式：从文件流读取并过滤
                filteredMessages = new List<CanRawMessage>();
                foreach (var msg in LogFileLoader.EnumerateCanMessages(_streamingFilePath, _selectedChannels))
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
                filteredMessages = _rawMessages
                    .Where(m => m.TimeStampSeconds >= startTime && m.TimeStampSeconds <= endTime && IsChannelMatched(m))
                    .ToList();
            }

            if (filteredMessages.Count == 0)
            {
                MessageBox.Show("所选时间范围内没有数据", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 数据量超过 100000 条时弹窗确认
            if (filteredMessages.Count > 100000)
            {
                var result = MessageBox.Show(
                    $"所选区域数据量 {filteredMessages.Count} 条，大于 20000 条，是否显示？",
                    "数据量确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                    return;
            }

            // 5. 清空 Main.cs 并发送数据到主界面显示
            Main.main.ClearForPlayback();
            _btnShowData.Enabled = false;
            _statusLabel.Text = $"状态: 正在加载 {filteredMessages.Count} 条数据到主界面...";

            Task.Run(() =>
            {
                try
                {
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

        private void _btnStop_Click(object sender, EventArgs e)
        {
            // 请求停止流式模式的"最快"后台Task
            if (_streamingMode)
                _cancelPlayback = true;
            RunStatus = false;
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
        /// 获取流式阈值的有效值（MB），无效返回默认150
        /// </summary>
        private int GetStreamingThresholdMB()
        {
            if (int.TryParse(_txtStreamingThreshold.Text, out int val) && val >= 50)
                return val;
            return 150;
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
                var msgDict = (cache != null && BaseParamter.dbcHelper?.dbcFile?.messageDict != null)
                    ? BaseParamter.dbcHelper.dbcFile.messageDict : null;
                foreach (var msg in LogFileLoader.EnumerateCanMessages(_streamingFilePath, _selectedChannels))
                {
                    var raw = new CanRawMessage
                    {
                        CanId = msg.CanId,
                        Data = msg.Data,
                        TimeStampSeconds = (float)msg.TimeStampSeconds,
                        Channel = msg.Channel
                    };
                    // 缓存模式下，只缓存DBC中有定义的报文（减少内存占用）
                    if (cache != null && (msgDict == null || msgDict.ContainsKey(raw.CanId)))
                        cache.Add(raw);
                    yield return raw;
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
            string text = enabled ? "停止滑动" : "自动滑动";
            _btnAutoScroll.Text = text;
            _toolAutoScroll.Text = text;
        }

        private void _btnAddChannel_Click(object sender, EventArgs e)
        {
            // 创建并显示信号选择器
            using (var selector = new SignalSelector())
            {
                // 把当前已有的通道标记为已选中，以便在信号选择界面自动勾上
                foreach (var channel in Channels)
                {
                    selector.SelectedSignals.Add(new SelectedSignalInfo
                    {
                        SignalName = channel.DbcSignalName,
                        MsgId = (uint)channel.DbcMessageId,
                        MsgIndex = channel.DbcMessageIndex,
                        SignalIndex = channel.DbcSignalIndex,
                        CycleTime = (uint)(channel.CycleTime * 1000),
                        EnumDefinitions = channel.EnumDefinitions,
                        Unit = channel.Unit
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
                        // 跳过已存在的信号，避免重复添加
                        bool exists = Channels.Any(c => c.DbcMessageIndex == signal.MsgIndex && c.DbcSignalIndex == signal.SignalIndex);
                        if (exists) continue;

                        Console.WriteLine($"信号名: {signal.SignalName}");
                        Console.WriteLine($"中文注释: {signal.SignalComment}");
                        Console.WriteLine($"MsgIndex: {signal.MsgIndex}");
                        Console.WriteLine($"SignalIndex: {signal.SignalIndex}");
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
                        //string channelName = "Channel_" + (channelCount + 1).ToString();

                        Channels.Add(new ChannelData(channelName, color, DateTime.Now, signal.EnumDefinitions, signal.Unit, (double)(signal.CycleTime / 1000.0f), (int)signal.MsgId, signal.MsgIndex, signal.SignalIndex, signal.SignalName));
                        _chartControl.SetChannels(Channels);
                        multiChartFromScheduler.AddMessage(signal.MsgIndex, signal.CycleTime);
                        BaseParamter.dbcHelper.dbcFile.messages[signal.MsgIndex].signals[signal.SignalIndex].ChartShowFlag = true;
                    }
                    PopulateChannelGrid();
                }
            }
        }

        private void _btnRemoveChannel_Click(object sender, EventArgs e)
        {
            if (_channelGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择要删除的曲线", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int index = _channelGrid.SelectedRows[0].Index;
            if (index >= 0 && index < _channelGrid.Rows.Count)
            {
                ChannelData channel = GetChannelFromRow(index);
                if (channel != null)
                {
                    lock (_lockObj)
                    {
                        channel.Clear();
                        Channels.Remove(channel);
                    }
                    _channelGrid.Rows.RemoveAt(index);
                    _chartControl.SetChannels(Channels);
                    _statusLabel.Text = "状态: 已停止";
                    _statusLabel.ForeColor = Color.Red;
                }
            }
        }

        private void ChartFrom_Load(object sender, EventArgs e)
        {
            Channels = new List<ChannelData>();
            Main.ChartShowOpenFlag = true;
            RefreshPresetComboBox();
        }

        private void _channelGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            if (_channelGrid.Columns[e.ColumnIndex].Name != "Color")
                return;

            ChangeChannelColorAtRow(e.RowIndex);
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

        private void _channelGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedChannel();
                e.Handled = true;
            }
        }

        private void DeleteSelectedChannel()
        {
            if (_channelGrid.SelectedRows.Count == 0)
                return;

            int index = _channelGrid.SelectedRows[0].Index;
            if (index < 0 || index >= _channelGrid.Rows.Count)
                return;

            ChannelData channel = GetChannelFromRow(index);
            if (channel == null)
                return;

            string channelName = channel.Name;
            var result = MessageBox.Show($"确定要删除通道 \"{channelName}\" 吗？", "确认删除",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (result != DialogResult.OK) return;

            lock (_lockObj)
            {
                channel.Clear();
                Channels.Remove(channel);
                _channelGrid.Rows.RemoveAt(index);
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
                if (safeInsertIndex > sourceIndex) safeInsertIndex--;
                safeInsertIndex = Math.Max(0, Math.Min(safeInsertIndex, Channels.Count));
                Channels.Insert(safeInsertIndex, channel);
                _channelGrid.Rows.RemoveAt(sourceIndex);
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

        private int GetChannelIndex(string signalName)
        {
            for(int index=0; index<Channels.Count; index++)
            {
                if (Channels[index].DbcSignalName.Equals(signalName))
                {
                    return index;
                }
            }
            return -1;
        }
        private int GetChannelIndex(int messageId, int signalIndex)
        {
            for (int index = 0; index < Channels.Count; index++)
            {
                if (Channels[index].DbcSignalIndex == signalIndex && Channels[index].DbcMessageId == messageId)
                {
                    return index;
                }
            }
            return -1;
        }
        public void AddPoint(uint msgId, int signalIndex, double rawValue, uint cycleTime)
        {
            // 文件模式下不接收实时数据
            if (!RunStatus || _isLoadingFile || _isFileMode)
            {
                return;
            }
            int channelIndex = Main.chartFromShow.GetChannelIndex((int)msgId, signalIndex);
            if (-1 != channelIndex)
            {
                TimeSpan elapsed = DateTime.Now - Main.chartFromShow.startTime;
                _currentTime = elapsed.TotalSeconds;

                ChannelData channel = Main.chartFromShow.Channels[channelIndex];
                channel.AddPoint(_currentTime, rawValue, false);
                // 无线测量线时实时更新当前值
                if (!_chartControl.MeasureLineX1.HasValue)
                    UpdateChannelGridValues();
            }
        }
        public class MultiMessageCANScheduler : IDisposable
        {
            private class CANMessageSchedule : IComparable<CANMessageSchedule>
            {
                public CAN_Data.Message Message { get; set; }
                public long IntervalMs { get; set; }
                public long NextTriggerTime { get; set; }
                public uint CanId => Message.messgeId;
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

                        // 如果触发时间相同，使用CanId作为次要排序条件
                        return x.CanId.CompareTo(y.CanId);
                    }
                }

                public void Enqueue(CANMessageSchedule item)
                {
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
            private readonly Dictionary<uint, CANMessageSchedule> _scheduleLookup;
            private readonly List<CAN_Data.Message> _ChartfromBuffer;
            private readonly object _lock = new object();
            private Stopwatch stopwatch = new Stopwatch();
            private bool _isStarted = false;

            private double lastChartRefreshTime;
            public MultiMessageCANScheduler()
            {
                _priorityQueue = new CANMessageScheduleQueue();
                _scheduleLookup = new Dictionary<uint, CANMessageSchedule>();
                _ChartfromBuffer = new List<CAN_Data.Message>();
            }

            public void AddMessage(int msgIndex, uint intervalMs)
            {
                intervalMs = (uint)((intervalMs <= 0) ? 1 : intervalMs*2);
                lock (_lock)
                {
                    var schedule = new CANMessageSchedule
                    {
                        Message = BaseParamter.dbcHelper.dbcFile.messages[msgIndex],
                        IntervalMs = intervalMs,
                        NextTriggerTime = GetCurrentTime()
                    };
                    if (null != schedule.Message)
                    {
                        if (!_scheduleLookup.ContainsKey(schedule.Message.messgeId))
                        {
                            _scheduleLookup[schedule.Message.messgeId] = schedule;
                            _priorityQueue.Enqueue(schedule);
                        }
                        else
                        {
                            UpdateMessageInterval(schedule.Message.messgeId, intervalMs);
                        }
                    }
                }
            }

            public void RemoveMessage(uint canId)
            {
                lock (_lock)
                {
                    if (_scheduleLookup.ContainsKey(canId))
                    {
                        _scheduleLookup.Remove(canId);
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
                        if (!_scheduleLookup.ContainsKey(nextSchedule.CanId))
                        {
                            _priorityQueue.Dequeue(); // 移除已删除的消息
                            continue;
                        }

                        // 如果下一个消息还没到期，就跳出循环
                        if (nextSchedule.NextTriggerTime > currentTime)
                            break;

                        // 出队并处理
                        var schedule = _priorityQueue.Dequeue();
                        _ChartfromBuffer.Add(schedule.Message);

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
                    
                    foreach (var msg in _ChartfromBuffer)
                    {
                        bool isLost = msg.receiveCnt == msg.ChartFromReceiveCnt;
                        if (!Main.chartFromShow.RunStatus)
                        {
                            isLost = false;
                        }
                        foreach (var signal in msg.signals)
                        {
                            if (signal.ChartShowFlag)
                            {
                                int index = Main.chartFromShow.GetChannelIndex(signal.signalName);
                                if (-1 != index)
                                {
                                    ChannelData channel = Main.chartFromShow.Channels[index];
                                    if (isLost)
                                    {
                                        channel.AddPoint(Main.chartFromShow._currentTime, channel.LastValue, isLost);
                                        if (!channel.IsLost)
                                        {
                                            Main.chartFromShow.Channels[index].IsLost = true;
                                            UpdateMessageInterval(msg.messgeId, msg.cycleTime);
                                        }
                                    }
                                    else
                                    {
                                        if (channel.IsLost)
                                        {
                                            Main.chartFromShow.Channels[index].IsLost = false;
                                            UpdateMessageInterval(msg.messgeId, msg.cycleTime * 2);
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

            public void UpdateMessageInterval(uint canId, uint newIntervalMs)
            {
                lock (_lock)
                {
                    if (_scheduleLookup.TryGetValue(canId, out var schedule))
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

            // 更新UI可见性
            _filePathTextBox.Visible = false;
            _speedLabel.Visible = false;
            _speedComboBox.Visible = false;

            // 停止所有运行中的操作
            if (RunStatus)
            {
                RunStatus = false;
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
            // 切回实时数据模式，恢复VersionCheck自动重连
            Main.RestartConnectFlag = true;

            _chartControl.Invalidate();
        }

        /// <summary>集中切换到报文数据模式</summary>
        private void SwitchToFileModeInternal()
        {
            if (!RealTimeDataSta) return; // 已经是报文模式

            RealTimeDataSta = false;

            // 更新UI可见性
            _filePathTextBox.Visible = true;
            _speedLabel.Visible = true;
            _speedComboBox.Visible = true;

            // 停止所有运行中的操作
            if (RunStatus)
            {
                RunStatus = false;
                multiChartFromScheduler.Stop();
            }
            _playbackTimer.Stop();

            // 判断是否有已加载的文件数据
            _isFileMode = _rawMessages != null && _rawMessages.Count > 0;

            // 清空通道显示数据
            lock (_lockObj)
            {
                foreach (var channel in Channels)
                    channel.Clear();
            }
            _currentTime = 0;

            // 更新UI状态
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _statusLabel.Text = _isFileMode
                ? $"状态: 已停止 (报文模式 — 已加载{_rawMessages.Count}条报文，可点击开始播放)"
                : "状态: 已停止 (报文模式 — 请加载文件)";
            _statusLabel.ForeColor = Color.Red;
            _toolModeToggle.Text = "报文数据";
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

        private void _btnLoadDbc_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "请选择DBC文件";
            dialog.Filter = "DBC文件|*.dbc|所有文件|*.*";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                BaseParamter.DBCFilepath = dialog.FileName;
                try
                {
                    BaseParamter.dbcHelper.Parse(BaseParamter.DBCFilepath);
                    _statusLabel.Text = $"状态: DBC已加载 ({BaseParamter.dbcHelper.dbcFile.messages.Count}条报文)";
                    _statusLabel.ForeColor = Color.Green;
                    SyncToolbarStateFromLegacyControls();
                }
                catch (Exception ex)
                {
                    CAN_Data.ExceptionHandler.Handle(ex);
                    _statusLabel.Text = "状态: DBC加载失败";
                    _statusLabel.ForeColor = Color.Red;
                    SyncToolbarStateFromLegacyControls();
                }
            }
        }

        private void _btnLoadFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "请选择日志文件";
            dialog.Filter = "日志文件|*.blf;*.BLF;*.bin;*.asc|BLF文件|*.blf;*.BLF|BIN文件|*.bin|ASC文件|*.asc";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LoadAndPlotFile(dialog.FileName);
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
                lock (_realtimeRawLock)
                {
                    if (_realtimeRawMessages.Count == 0)
                    {
                        MessageBox.Show("没有可保存的实时数据！请先开始接收实时数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    messagesToSave = new List<CanRawMessage>(_realtimeRawMessages);
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

                    messageList.Add(new CAN_Data.blf.CANMessage(0, rawMsg.CanId, data, rawMsg.TimeStampSeconds));
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
            string path = ((Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();
            string ext = Path.GetExtension(path).ToLower();
            if (ext == ".blf" || ext == ".bin" || ext == ".asc")
            {
                if (RealTimeDataSta)
                {
                    SwitchToFileModeInternal();
                }
                LoadAndPlotFile(path);
            }
            else if (ext == ".dbc")
            {
                BaseParamter.DBCFilepath = path;
                try
                {
                    BaseParamter.dbcHelper.Parse(BaseParamter.DBCFilepath);
                    _statusLabel.Text = $"状态: DBC已加载 ({BaseParamter.dbcHelper.dbcFile.messages.Count}条报文)";
                    _statusLabel.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    CAN_Data.ExceptionHandler.Handle(ex);
                }
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
            if (Channels == null || Channels.Count == 0)
            {
                MessageBox.Show("请先通过\"添加通道\"按钮选择要绘制的信号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (BaseParamter.dbcHelper == null || BaseParamter.dbcHelper.dbcFile == null ||
                BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
            {
                MessageBox.Show("DBC文件未加载，请先拖入DBC文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 断开PCAN/CANoe连接（含按钮文字、下拉框同步更新）
            Main.main.DisconnectPCAN();
            Main.main.DisconnectCANoe();
            // 报文数据模式下禁用VersionCheck自动重连
            Main.RestartConnectFlag = false;

            // 切换到报文数据模式
            SwitchToFileModeInternal();
            _filePathTextBox.Text = filePath;

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
            _loadingComplete = false;
            _isLoadingFile = true;
            _rawMessages = null;

            // 根据文件大小决定模式（但不在这里加载数据，推迟到点击开始时）
            long fileSizeMB = new FileInfo(filePath).Length / (1024 * 1024);
            int thresholdMB = GetStreamingThresholdMB();
            _streamingMode = (fileSizeMB > thresholdMB);
            _streamingFilePath = filePath;
            _isFileMode = true;
            _loadingComplete = true;
            _fileMaxTime = 0;

            _isLoadingFile = false;
            _btnLoadFile.Enabled = true;
            _btnLoadFile.Text = "加载文件...";
            _btnStart.Enabled = true;
            _btnClear.Enabled = true;
            _statusLabel.Text = _streamingMode
                ? $"状态: 文件已加载(流式模式,{fileSizeMB}MB) — 点击开始播放"
                : $"状态: 文件已加载(内存模式,{fileSizeMB}MB) — 点击开始播放";
            _statusLabel.ForeColor = Color.Green;
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
                ColorArgb = c.Color.ToArgb()
            }).ToList();
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
                int channelCount = Channels.Count;
                Color color = signal.ColorArgb != 0 ? Color.FromArgb(signal.ColorArgb) : colors[channelCount % colors.Length];
                string channelName = signal.SignalName;
                if (!string.IsNullOrEmpty(signal.SignalComment))
                    channelName = TruncateName($"{signal.SignalName} ({signal.SignalComment})");

                // 从 DBC 信号中获取枚举值定义
                var enumDefs = new Dictionary<double, string>();
                try
                {
                    var dbcSignals = BaseParamter.dbcHelper.dbcFile.messages[signal.MsgIndex].signals;
                    if (signal.SignalIndex >= 0 && signal.SignalIndex < dbcSignals.Count)
                    {
                        var dbcSignal = dbcSignals[signal.SignalIndex];
                        if (dbcSignal.enumDefinitions != null && dbcSignal.enumDefinitions.Count > 0)
                        {
                            enumDefs = dbcSignal.enumDefinitions;
                        }
                    }
                }
                catch { }

                Channels.Add(new ChannelData(channelName, color, DateTime.Now,
                    enumDefs, signal.Unit,
                    signal.CycleTime / 1000.0, (int)signal.MsgId,
                    signal.MsgIndex, signal.SignalIndex, signal.SignalName));
                multiChartFromScheduler.AddMessage(signal.MsgIndex, signal.CycleTime);
                BaseParamter.dbcHelper.dbcFile.messages[signal.MsgIndex].signals[signal.SignalIndex].ChartShowFlag = true;
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
            private string sourceFilePath = "";
            public double[] saveExcelTimeBuf = new double[8] { 0.1, 0.5, 1, 2, 3, 4, 5, 10 };
            public bool saveExcelFlag = false;
            public bool saveAscFlag = false;
            public uint saveExcelTime = 1000; /* ms */
            public uint comboBoxExcelSaveTimeSelectIndex = 0;
            public bool changeFlag = false;


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
    }

}