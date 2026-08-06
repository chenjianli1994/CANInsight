using PCAN_Client.CAN_Data;
using PCAN_Client.CAN_Data.blf;
using PCAN_Client.DataLog;
using PCAN_Client.util;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml.Linq;
using vxlapi_NET;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Button = System.Windows.Forms.Button;

namespace PCAN_Client
{
    public partial class Main : Form
    {
        internal PCAN_API.PCAN_API pCAN_API = null;
        internal Canoe_API.CanOe_API canoe_API = null;
        internal static CanSend canSend = null;
        internal static ChartFrom chartFromShow = null;
        internal static LogFileToCSV LogFileToCSV = null;
        internal static Boolean canSendOpenFlag = false;
        internal static Boolean ChartShowOpenFlag = false;

        internal static Main main = new Main();
        internal List<ulong> Canoe_Channel;

        internal static Boolean canoeOpenFlag = false;
        internal static Boolean pcanOpenFlag = false;
        internal static XLClass.xl_driver_config driverConfig = null;

        string[] ByteOrder = { "Intel", "Motorola" };
        string[] ValueType = { "Unsigned", "Signed", "Float", "Double" };
        int SelectMessageIndex = 0;

        public MultiMessageCANScheduler multiMessageCANScheduler = null;

        internal static string CANDevice = "";
        internal static bool updeteDbcSuccess = true;

        // ========== 报文显示相关 (VirtualMode高性能) ==========
        public class CanMsgDisplayInfo
        {
            public uint MsgId;
            public uint Count;
            public string TimeGap = "";      // 周期/间隔 (abs|rel)
            public string Description = "";   // DBC报文名称
            public byte Len;
            public string DataBytes = "";     // 十六进制数据
            public string Node = "";          // 发送节点
            public bool IsTx;
            public bool IsLost;
            public ulong LastTimestampUs;
            public int CycleTimeMs;
            public bool IsExtended = false;   // 是否扩展帧（29位ID）
            public byte Channel = 0;          // CAN通道号
            public uint ChangeCnt = 0;        // 数据变化计数
            public double Timestamp = 0;      // 时间戳（秒）
            public long MaxTimeGapUs = 0;     // 最大接收间隔（微秒）
            public long MinTimeGapUs = long.MaxValue; // 最小接收间隔（微秒）
            public byte[] PrevData = null;    // 上一帧数据，用于比较变化
            public int[] ByteChanged = null;  // 标记哪些字节发生了变化（值表示变化后未更新的帧数：0=刚变化，1=1帧前，2=2帧前，-1=不显示）
            public int[] SigChanged = null;   // 信号行高亮计数（与ByteChanged类似逻辑，按信号索引）
            public double[] PrevSignalValues = null; // 各信号上一帧的物理解码值，用于精确判断信号变化
        }

        /// <summary>Scroll模式：每帧的独立记录（精简，只保留原始数据）</summary>
        public class ScrollFrameRecord
        {
            public uint MsgId;
            public ulong TimestampUs;
            public byte Len;
            public byte[] Data;
            public bool IsTx;
            public byte Channel;       // CAN通道号
            /// <summary>与"同通道同ID上一帧"的时间间隔(us)，写入时O(1)预计算——替代colTime单元格每行O(n)前向扫描（5w帧全显时滚动/切换O(n²)卡顿的根因）</summary>
            public ulong PrevSameIdGapUs;
        }

        // === VirtualMode 数据源 ===
        internal List<CanMsgDisplayInfo> _displayList = new List<CanMsgDisplayInfo>();
        // key为复合键 MsgKey(MsgId,Channel)：多通道同ID分行显示
        internal Dictionary<long, int> _msgIndexMap = new Dictionary<long, int>();
        internal DataGridView _dgvMessages;
        internal System.Windows.Forms.TextBox _txtIdFilter;
        private HashSet<uint> _filterIds = new HashSet<uint>();
        /// <summary>通配符筛选模式：mask的1位为必须匹配的十六进制位，value为对应位的值；maxId限制ID位数与模式等长（如3**只匹配0x300~0x3FF，不误中0x1300）</summary>
        private readonly List<(uint mask, uint value, uint maxId)> _filterWildcards = new List<(uint mask, uint value, uint maxId)>();

        /// <summary>ID是否通过筛选（精确匹配或通配符匹配；无筛选条件时全部通过）</summary>
        private bool PassesIdFilter(uint id)
        {
            return IdFilterRule.Match(id, _filterIds, _filterWildcards);
        }
        internal bool _msgDisplayRefreshPending = false;
        internal bool _scrollMode = false;  // false=按ID排序(Fixed); true=按时间顺序(Scroll)
        internal bool _pauseUpdate = false; // 暂停更新
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private const int MIN_REFRESH_MS = 100; // 最大刷新频率 ~10fps
        /// <summary>Scroll帧"同通道同ID上一帧时间戳"缓存（MsgKey→ts）：写入帧时O(1)计算间隔，清空数据时同步清空</summary>
        private readonly Dictionary<long, ulong> _lastFrameTsByKey = new Dictionary<long, ulong>();

        // === 展开/折叠信号 ===
        internal enum FlatRowType { Message, Signal }
        internal class FlatRowInfo
        {
            public FlatRowType Type;
            public int MsgIndex;          // _displayList 索引（Fixed模式）
            public int ScrollFrameIndex = -1; // _scrollFrames 索引（Scroll模式）
            public int SigIndex = -1;     // signals 索引（仅 Signal 类型）
            public int FlatIndex;         // 在 _flatRows 中的索引
        }
        internal List<FlatRowInfo> _flatRows = new List<FlatRowInfo>();
        internal HashSet<long> _expandedIds = new HashSet<long>();   // Fixed模式：按复合键MsgKey(MsgId,Channel)展开
        internal HashSet<int> _expandedScrollFrames = new HashSet<int>(); // Scroll模式：按帧索引展开

        // === Scroll模式：每帧记录列表 ===
        internal List<ScrollFrameRecord> _scrollFrames = new List<ScrollFrameRecord>();
        private const int MAX_SCROLL_FRAMES = 10000000; // 最多保留1000万帧（内存上限）
        private const int SCROLL_TRIM_COUNT = 1000000;  // 每次裁剪量
        // Scroll自动跟随模式（未暂停）：只显示最新SCROLL_LIVE_FRAMES帧；暂停后显示全部帧
        private const int SCROLL_LIVE_FRAMES = 20;
        // 复合键MsgKey(MsgId,Channel) → (Description, Node) 缓存，避免每帧查DBC
        private readonly Dictionary<long, (string desc, string node)> _msgMetaCache = new Dictionary<long, (string, string)>();

        // === 结构变化标志：为true时下次刷新需重建_flatRows ===
        private bool _flatRowsDirty = true;
        // Scroll模式：已加载的帧数量，用于增量追加
        private int _scrollFramesLoaded = 0;
        // Scroll模式：用户是否已滚动离开底部
        private bool _userScrolledAway = false;
        private int _lastScrollRowCount = 0;

        // 会话起始时间戳（微秒），用于将绝对时间戳归零显示
        private long _sessionStartUs = 0;

        // Scroll 模式强制刷新节流（至少每 500ms 刷新一次）
        private DateTime _lastScrollForceRefresh = DateTime.MinValue;
        private static readonly TimeSpan SCROLL_FORCE_REFRESH_INTERVAL = TimeSpan.FromMilliseconds(500);

        // CellPainting 缓存画笔（避免每帧创建对象）
        private static Brush _highlightBrush = new SolidBrush(Color.FromArgb(173, 216, 230));
        private static Brush _defaultTextBrush = new SolidBrush(Color.Black);
        private static Font _dataFont = new Font("Consolas", 9f);
        private static Font _signalFont = new Font("Consolas", 8f);
        private static StringFormat _paintSf = new StringFormat()
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None
        };

        // 工具栏控件
        internal ToolStrip _toolbarPanel;
        internal ToolStrip _connectionStrip;
        internal ToolStripButton _btnRecordStart; /* 录制报文按钮（点击弹出录制配置窗口） */
        internal ToolStripButton _btnScroll;
        internal ToolStripButton _btnPause;
        internal ToolStripButton _btnClear;
        internal ToolStripButton _btnDbcOnly;
        internal bool _dbcOnlyMode = false;

        // === CAN通道列排序状态 ===
        private enum ChannelSortOrder { None, Ascending, Descending }
        private ChannelSortOrder _channelSortOrder = ChannelSortOrder.None;

        private static readonly char[] HexChars = "0123456789ABCDEF".ToCharArray();

        /// <summary>高效格式化字节数组为十六进制字符串，避免每次分配多个临时字符串</summary>
        private static string FormatHexBytes(byte[] data, int len)
        {
            if (len == 0 || data == null) return "";
            int charCount = len * 3 - 1;
            char[] chars = new char[charCount];
            int pos = 0;
            for (int i = 0; i < len; i++)
            {
                if (i > 0) chars[pos++] = ' ';
                byte b = data[i];
                chars[pos++] = HexChars[b >> 4];
                chars[pos++] = HexChars[b & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// 从通道DBC聚合视图中查找报文定义（dbcHelper 由通道配置统一刷新）
        /// </summary>
        internal static bool TryFindDbcMessage(uint canId, out CAN_Data.Message dbcMsg)
        {
            dbcMsg = null;
            if (BaseParamter.dbcHelper?.dbcFile?.messageDict != null &&
                BaseParamter.dbcHelper.dbcFile.messageDict.TryGetValue(canId, out dbcMsg))
            {
                return true;
            }
            return false;
        }

        /// <summary>报文列表复合键：逻辑通道号(高32位) + CAN ID(低32位)，多通道同ID分行显示</summary>
        internal static long MsgKey(uint msgId, byte channel) => ((long)channel << 32) | msgId;

        /// <summary>
        /// 按逻辑通道查找报文定义：多通道模式严格用该通道自己的DBC（跨通道定义不回退，避免串扰显示）；
        /// 无通道配置（单通道兼容）时用全局聚合视图
        /// </summary>
        internal static bool TryFindDbcMessage(uint canId, byte channel, out CAN_Data.Message dbcMsg)
        {
            dbcMsg = null;
            if (BaseParamter.BusChannels.Count > 0)
            {
                var helper = BaseParamter.GetDbcHelperByChannel(channel);
                if (helper?.dbcFile?.messageDict != null &&
                    helper.dbcFile.messageDict.TryGetValue(canId, out dbcMsg))
                {
                    return true;
                }
                return false;
            }
            if (BaseParamter.dbcHelper?.dbcFile?.messageDict != null &&
                BaseParamter.dbcHelper.dbcFile.messageDict.TryGetValue(canId, out dbcMsg))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查报文ID是否在通道DBC聚合视图中有定义
        /// </summary>
        internal static bool ContainsDbcMessage(uint canId)
        {
            return BaseParamter.dbcHelper?.dbcFile?.messageDict != null &&
                BaseParamter.dbcHelper.dbcFile.messageDict.ContainsKey(canId);
        }

        /// <summary>记录CAN报文（CAN接收线程调用，极轻量）</summary>
        /// <param name="triggerRefresh">是否触发界面刷新；批量导入时传false，最后手动刷新</param>
        /// <param name="channel">逻辑通道号（多通道同ID分行显示，各自独立统计）</param>
        internal void RecordCanMessage(TPCANMsg msg, ulong timestampUs, bool isTx, bool triggerRefresh = true, byte channel = 1)
        {
            int idx;
            CanMsgDisplayInfo info;
            long msgKey = MsgKey(msg.ID, channel);
            lock (_displayList)
            {
                if (!_msgIndexMap.TryGetValue(msgKey, out idx))
                {
                    info = new CanMsgDisplayInfo { MsgId = msg.ID, Channel = channel, LastTimestampUs = timestampUs };
                    if (TryFindDbcMessage(msg.ID, channel, out var dbcMsg))
                    {
                        info.Description = dbcMsg.messageName;
                        info.Node = dbcMsg.transmitter;
                        info.CycleTimeMs = (int)dbcMsg.cycleTime;
                    }
                    // 按(MsgId,Channel)升序插入：同ID多通道相邻
                    int insertIdx = _displayList.Count;
                    for (int i = 0; i < _displayList.Count; i++)
                    {
                        if (_displayList[i].MsgId > msg.ID ||
                            (_displayList[i].MsgId == msg.ID && _displayList[i].Channel > channel))
                        {
                            insertIdx = i;
                            break;
                        }
                    }
                    _displayList.Insert(insertIdx, info);
                    // 更新插入点之后所有元素的索引映射
                    for (int i = insertIdx; i < _displayList.Count; i++)
                        _msgIndexMap[MsgKey(_displayList[i].MsgId, _displayList[i].Channel)] = i;
                    _flatRowsDirty = true;
                }
                else
                {
                    info = _displayList[idx];
                }

                info.Count++;
                info.IsTx = isTx;
                info.IsExtended = (TPCANMessageType.PCAN_MESSAGE_EXTENDED == msg.MSGTYPE) ? true : false; // PCAN_MESSAGE_EXTENDED
                // 以首条报文时间戳为会话起点，后续时间戳归零显示
                if (_sessionStartUs == 0)
                    Interlocked.Exchange(ref _sessionStartUs, (long)timestampUs);
                long relTsUs = (long)timestampUs - _sessionStartUs;
                info.Timestamp = relTsUs / 1000000.0;

                // 基于周期计算渐变帧数：约1秒消除，最少3帧
                int fadeMax = info.CycleTimeMs > 0
                    ? Math.Max(3, (int)Math.Round(1000.0 / info.CycleTimeMs))
                    : 10;

                // --- 优化：重用 PrevData 缓冲区，避免每次 new byte[] ---
                bool dataChanged = false;
                if (info.PrevData == null || info.PrevData.Length != msg.LEN)
                {
                    info.PrevData = new byte[msg.LEN];
                    Array.Copy(msg.DATA, info.PrevData, msg.LEN);
                    info.ByteChanged = new int[msg.LEN];
                    for (int i = 0; i < msg.LEN; i++)
                        info.ByteChanged[i] = 0;
                    dataChanged = true;
                }
                else
                {
                    int len = msg.LEN;
                    if (info.ByteChanged == null || info.ByteChanged.Length != msg.LEN)
                        info.ByteChanged = new int[msg.LEN];
                    for (int i = 0; i < len; i++)
                    {
                        if (info.ByteChanged[i] >= 0)
                            info.ByteChanged[i]++;
                        if (info.PrevData[i] != msg.DATA[i])
                        {
                            info.ByteChanged[i] = 0;
                            dataChanged = true;
                        }
                        if (info.ByteChanged[i] > fadeMax)
                            info.ByteChanged[i] = -1;
                        info.PrevData[i] = msg.DATA[i];
                    }
                }

                // 信号行高亮老化：每帧递增（无论数据是否变化）
                if (info.SigChanged != null)
                {
                    for (int s = 0; s < info.SigChanged.Length; s++)
                    {
                        if (info.SigChanged[s] >= 0)
                        {
                            info.SigChanged[s]++;
                            if (info.SigChanged[s] > fadeMax)
                                info.SigChanged[s] = -1;
                        }
                    }
                }

                // 数据变化计数 + 信号变化精确检测
                if (dataChanged)
                {
                    info.ChangeCnt++;

                    // 首次数据变化时分配 SigChanged/PrevSignalValues
                    if (info.SigChanged == null &&
                        TryFindDbcMessage(info.MsgId, channel, out var initMsg))
                    {
                        int cnt = initMsg.signals.Count;
                        info.SigChanged = new int[cnt];
                        info.PrevSignalValues = new double[cnt];
                    }

                    // 有DBC时：通过比较信号物理解码值精确判断变化
                    if (info.SigChanged != null &&
                        TryFindDbcMessage(info.MsgId, channel, out var sigChangeMsg))
                    {
                        // PrevSignalValues需与SigChanged成对存在：连接中后配DBC/更换DBC后切入本分支时可能尚未分配或长度不符，按需重建
                        if (info.PrevSignalValues == null || info.PrevSignalValues.Length < sigChangeMsg.signals.Count)
                            info.PrevSignalValues = new double[sigChangeMsg.signals.Count];
                        for (int s = 0; s < info.SigChanged.Length && s < sigChangeMsg.signals.Count; s++)
                        {
                            double curVal = sigChangeMsg.signals[s].result;
                            if (Math.Abs(curVal - info.PrevSignalValues[s]) > 1e-12)
                                info.SigChanged[s] = 0;
                            info.PrevSignalValues[s] = curVal;
                        }
                    }
                    else
                    {
                        // 无DBC时回退：按每2个字节映射一个信号槽位
                        int sigCount = Math.Max(msg.LEN / 2 + 1, 16);
                        if (info.SigChanged == null || info.SigChanged.Length < sigCount)
                        {
                            info.SigChanged = new int[sigCount];
                            info.PrevSignalValues = new double[sigCount]; // 与SigChanged成对分配（之后配上DBC切入精确比对分支时要读）
                            // 新初始化的 SigChanged 数组元素全为0，即刚变化
                        }
                        for (int i = 0; i < msg.LEN; i++)
                        {
                            if (info.ByteChanged[i] == 0)
                            {
                                int sigIdx = i / 2;
                                if (sigIdx < info.SigChanged.Length)
                                    info.SigChanged[sigIdx] = 0;
                            }
                        }
                    }
                }

                info.Len = msg.LEN;
                // --- 优化：用逐字节手动格式化替换 LINQ + ToString("X2")，减少临时字符串 ---
                info.DataBytes = FormatHexBytes(msg.DATA, msg.LEN);

                if (info.Count > 1 && info.LastTimestampUs > 0)
                {
                    long gapUs = (long)(timestampUs - info.LastTimestampUs);
                    if (gapUs > 0)
                    {
                        info.TimeGap = gapUs >= 1000 ? $"{gapUs / 1000.0:F1}ms" : $"{gapUs}us";
                        // Count>5 时才开始统计最大/最小间隔
                        if (info.Count >= 6)
                        {
                            if (gapUs > info.MaxTimeGapUs) info.MaxTimeGapUs = gapUs;
                            if (gapUs < info.MinTimeGapUs) info.MinTimeGapUs = gapUs;
                        }
                    }
                    if (info.CycleTimeMs > 0 && gapUs > info.CycleTimeMs * 1500)
                        info.IsLost = true;
                    else if (info.CycleTimeMs > 0)
                        info.IsLost = false;
                }
                info.LastTimestampUs = timestampUs;
            }

            // Scroll模式：每帧均记录到_scrollFrames（实时Fixed模式不记录，节省内存）；
            // 离线回放（无硬件连接）始终记录：Fixed模式回放后切Scroll也要有帧流可看
            if (_scrollMode || (!pcanOpenFlag && !canoeOpenFlag))
            {
                // 预先查DBC缓存Description/Node，供CellValueNeeded绘制用
                if (!_msgMetaCache.TryGetValue(msgKey, out _))
                {
                    string desc, node;
                    if (TryFindDbcMessage(msg.ID, channel, out var dbcMsg2))
                    {
                        desc = dbcMsg2.messageName;
                        node = dbcMsg2.transmitter;
                    }
                    else
                    {
                        desc = "";
                        node = "";
                    }
                    _msgMetaCache[msgKey] = (desc, node);
                }
                byte[] dataCopy = new byte[msg.LEN];
                Array.Copy(msg.DATA, dataCopy, msg.LEN);
                lock (_scrollFrames)
                {
                    // 与同通道同ID上一帧的间隔（O(1)，供colTime直接读取）
                    ulong prevGap = _lastFrameTsByKey.TryGetValue(msgKey, out ulong prevTs) ? timestampUs - prevTs : 0;
                    _lastFrameTsByKey[msgKey] = timestampUs;
                    _scrollFrames.Add(new ScrollFrameRecord
                    {
                        MsgId = msg.ID,
                        TimestampUs = timestampUs,
                        Len = msg.LEN,
                        Data = dataCopy,
                        IsTx = isTx,
                        Channel = channel,
                        PrevSameIdGapUs = prevGap
                    });
                    // 控制内存：增量裁剪
                    if (_scrollFrames.Count > MAX_SCROLL_FRAMES)
                    {
                        _scrollFrames.RemoveRange(0, SCROLL_TRIM_COUNT);
                    }
                }
            }

            if (triggerRefresh)
                _msgDisplayRefreshPending = true;
        }

        /// <summary>只清空数据，不改变scroll/fixed模式（连接前调用）</summary>
        internal void ClearDataOnly()
        {
            lock (_displayList)
            {
                _displayList.Clear();
                _displayList.TrimExcess();
                _msgIndexMap.Clear();
            }
            lock (_scrollFrames)
            {
                _scrollFrames.Clear();
                _scrollFrames.TrimExcess();
            }
            _lastFrameTsByKey.Clear();
            _flatRows.Clear();
            _flatRows.TrimExcess();
            _msgMetaCache.Clear();
            _sessionStartUs = 0;
            _expandedScrollFrames.Clear();
            _flatRowsDirty = true;
            _scrollFramesLoaded = 0;
            _userScrolledAway = false;
            _lastScrollRowCount = 0;
            _dgvMessages.RowCount = 0;
            _msgDisplayRefreshPending = true;
            RefreshMessageDisplay();
        }

        /// <summary>清空数据并切换到Scroll模式（ChartFrom开始播放前调用）</summary>
        internal void ClearForPlayback()
        {
            // 复位暂停状态：前一次回放/导入会置暂停供回看（_pauseUpdate=true），
            // 残留会冻结RefreshMessageDisplay——新回放帧正常写入但列表不刷新（表现为"清空后再回放没报文"）
            SetPauseState(false);
            lock (_displayList)
            {
                _displayList.Clear();
                _displayList.TrimExcess();
                _msgIndexMap.Clear();
            }
            lock (_scrollFrames)
            {
                _scrollFrames.Clear();
                _scrollFrames.TrimExcess(); // 释放内部数组缓冲区，归还内存
            }
            _lastFrameTsByKey.Clear();
            _flatRows.Clear();
            _flatRows.TrimExcess();
            _msgMetaCache.Clear();
            _sessionStartUs = 0;
            _expandedScrollFrames.Clear();
            _flatRowsDirty = true;
            _scrollFramesLoaded = 0;
            _userScrolledAway = false;
            _lastScrollRowCount = 0;
            _dgvMessages.RowCount = 0;

            // 不再强制切Scroll：回放数据双写（Scroll帧记录+Fixed聚合列表），尊重用户当前选择的显示模式
            _msgDisplayRefreshPending = true;
            RefreshMessageDisplay();
        }

        /// <summary>批量导入CAN报文（用于ChartFrom回放，一次加锁避免UI卡死）</summary>
        internal void BatchImportRawMessages(List<CanRawMessage> messages)
        {
            if (messages == null || messages.Count == 0) return;

            lock (_displayList)
            {
                // 确保所有(通道,MsgId)在_displayList中存在
                foreach (var rawMsg in messages)
                {
                    byte ch = rawMsg.Channel > 0 ? BaseParamter.GetLogicChannelByBlfId(rawMsg.Channel) : (byte)1; // rawMsg.Channel为BLF通道号→逻辑通道号
                    long key = MsgKey(rawMsg.CanId, ch);
                    if (!_msgIndexMap.ContainsKey(key))
                    {
                        var info = new CanMsgDisplayInfo { MsgId = rawMsg.CanId, Channel = ch };
                        if (TryFindDbcMessage(rawMsg.CanId, ch, out var dbcMsg))
                        {
                            info.Description = dbcMsg.messageName;
                            info.Node = dbcMsg.transmitter;
                            info.CycleTimeMs = (int)dbcMsg.cycleTime;
                        }
                        // 按(MsgId,Channel)升序插入
                        int insertIdx = _displayList.Count;
                        for (int i = 0; i < _displayList.Count; i++)
                        {
                            if (_displayList[i].MsgId > rawMsg.CanId ||
                                (_displayList[i].MsgId == rawMsg.CanId && _displayList[i].Channel > ch))
                            { insertIdx = i; break; }
                        }
                        _displayList.Insert(insertIdx, info);
                        for (int i = insertIdx; i < _displayList.Count; i++)
                            _msgIndexMap[MsgKey(_displayList[i].MsgId, _displayList[i].Channel)] = i;
                    }
                }
            }

            // 批量添加到_scrollFrames（实时Fixed模式不记录，节省内存）；
            // 离线回放（无硬件连接）始终记录：Fixed模式回放后切Scroll也要有帧流可看
            if (_scrollMode || (!pcanOpenFlag && !canoeOpenFlag))
            {
                lock (_scrollFrames)
                {
                    foreach (var rawMsg in messages)
                    {
                        byte ch = rawMsg.Channel > 0 ? BaseParamter.GetLogicChannelByBlfId(rawMsg.Channel) : (byte)1; // rawMsg.Channel为BLF通道号→逻辑通道号
                        long key = MsgKey(rawMsg.CanId, ch);
                        // 预缓存Description/Node
                        if (!_msgMetaCache.TryGetValue(key, out _))
                        {
                            string desc, node;
                            if (TryFindDbcMessage(rawMsg.CanId, ch, out var dbcMsg2))
                            {
                                desc = dbcMsg2.messageName;
                                node = dbcMsg2.transmitter;
                            }
                            else
                            {
                                desc = "";
                                node = "";
                            }
                            _msgMetaCache[key] = (desc, node);
                        }
                        ulong tsUs = (ulong)(rawMsg.TimeStampSeconds * 1000000.0);
                        byte[] dataCopy = new byte[rawMsg.Data.Length];
                        Array.Copy(rawMsg.Data, dataCopy, rawMsg.Data.Length);
                        // 与同通道同ID上一帧的间隔（O(1)，供colTime直接读取）
                        ulong prevGap = _lastFrameTsByKey.TryGetValue(key, out ulong prevTs) ? tsUs - prevTs : 0;
                        _lastFrameTsByKey[key] = tsUs;
                        _scrollFrames.Add(new ScrollFrameRecord
                        {
                            MsgId = rawMsg.CanId,
                            TimestampUs = tsUs,
                            Len = (byte)rawMsg.Data.Length,
                            Data = dataCopy,
                            IsTx = false,
                            Channel = rawMsg.Channel,
                            PrevSameIdGapUs = prevGap
                        });
                    }
                    // 控制内存：增量裁剪
                    while (_scrollFrames.Count > MAX_SCROLL_FRAMES)
                    {
                        _scrollFrames.RemoveRange(0, SCROLL_TRIM_COUNT);
                    }
                }
            }

            // 计算各 (通道,ID) 的统计信息（用于 Fixed 模式显示）
            lock (_displayList)
            {
                var lastTsPerId = new Dictionary<long, ulong>();
                foreach (var rawMsg in messages)
                {
                    byte ch = rawMsg.Channel > 0 ? BaseParamter.GetLogicChannelByBlfId(rawMsg.Channel) : (byte)1; // rawMsg.Channel为BLF通道号→逻辑通道号
                    long key = MsgKey(rawMsg.CanId, ch);
                    if (!_msgIndexMap.TryGetValue(key, out int idx)) continue;
                    var info = _displayList[idx];
                    info.Count++;
                    ulong tsUs = (ulong)(rawMsg.TimeStampSeconds * 1000000.0);
                    info.Timestamp = rawMsg.TimeStampSeconds;

                    // 数据变化检测
                    byte[] data = rawMsg.Data;
                    if (info.PrevData == null || info.PrevData.Length != data.Length)
                    {
                        info.PrevData = (byte[])data.Clone();
                        info.ChangeCnt++;
                    }
                    else
                    {
                        bool changed = false;
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (info.PrevData[i] != data[i])
                            {
                                info.PrevData[i] = data[i];
                                changed = true;
                            }
                        }
                        if (changed) info.ChangeCnt++;
                    }

                    // 间隔统计
                    if (lastTsPerId.TryGetValue(key, out ulong lastTs) && lastTs > 0)
                    {
                        long gapUs = (long)(tsUs - lastTs);
                        if (gapUs > 0)
                        {
                            // Count>5 时才开始统计最大/最小间隔
                            if (info.Count >= 6)
                            {
                                if (gapUs > info.MaxTimeGapUs) info.MaxTimeGapUs = gapUs;
                                if (gapUs < info.MinTimeGapUs) info.MinTimeGapUs = gapUs;
                            }
                        }
                    }
                    lastTsPerId[key] = tsUs;
                    info.LastTimestampUs = tsUs; // 同步到_displayList，供后续 RecordCanMessage 使用
                }
            }

            _flatRowsDirty = true;
            _msgDisplayRefreshPending = true;
        }

        /// <summary>
        /// 以Scroll模式显示帧列表（供ChartFrom流式读取停止/播放完毕后调用）
        /// </summary>
        internal void DisplayFramesInScrollMode(List<CanRawMessage> frames)
        {
            if (frames == null || frames.Count == 0) return;

            ClearForPlayback();

            // 流式回放完成展示固定走Scroll（数据源为帧记录）；
            // 批量导入双写：Scroll帧记录 + Fixed聚合列表/统计（否则切Fixed模式空白——原实现只写Scroll数据源）
            _scrollMode = true;
            _btnScroll.Checked = true;
            _btnScroll.Text = "Scroll";
            BatchImportRawMessages(frames);

            // 暂停显示所有帧（供回看，同步按钮状态）
            SetPauseState(true);
            _flatRowsDirty = true;
            _msgDisplayRefreshPending = true;
            ForceRefreshDisplay();
        }

        /// <summary>
        /// 强制刷新报文显示（绕过 pause 检查，用于外部导入后立即显示）
        /// </summary>
        internal void ForceRefreshDisplay()
        {
            if (_dgvMessages == null || _dgvMessages.IsDisposed) return;

            if (InvokeRequired)
            {
                Invoke(new Action(ForceRefreshDisplay));
                return;
            }

            _flatRowsDirty = true;
            _msgDisplayRefreshPending = false;

            lock (_displayList)
            {
                RebuildFlatRows();
            }

            _dgvMessages.SuspendLayout();
            _dgvMessages.RowCount = _flatRows.Count;
            _dgvMessages.Invalidate();
            _dgvMessages.ResumeLayout();

            // 自动滚动到底部
            if (_flatRows.Count > 0)
            {
                _userScrolledAway = false;
                try { _dgvMessages.FirstDisplayedScrollingRowIndex = Math.Max(0, _flatRows.Count - 1); }
                catch { }
            }
        }

        /// <summary>设置暂停状态并更新UI</summary>
        internal void SetPauseState(bool paused)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetPauseState(paused)));
                return;
            }
            if (_pauseUpdate == paused) return;
            _pauseUpdate = paused;
            _btnPause.Checked = paused;
            _btnPause.Text = paused ? "Paused" : "Pause";
        }

        /// <summary>重建扁平行列表：消息行 + 展开的信号行</summary>
        private void RebuildFlatRows()
        {
            lock (_displayList)
            {
                if (_scrollMode)
                {
                    lock (_scrollFrames)
                    {
                        // ===== 确定显示帧范围 =====
                        // 未暂停（接收中）：全量重建，只显示最新SCROLL_LIVE_FRAMES条（仅20帧，极快）
                        // 暂停后：增量追加，显示全部匹配报文
                        if (_pauseUpdate)
                        {
                            // ====== 暂停模式：增量追加全部帧 ======
                            if (_flatRowsDirty || _flatRows.Count == 0 || _scrollFramesLoaded > _scrollFrames.Count)
                            {
                                _flatRows.Clear();
                                _scrollFramesLoaded = 0;
                            }

                            for (int i = _scrollFramesLoaded; i < _scrollFrames.Count; i++)
                            {
                                var frame = _scrollFrames[i];
                                if (!PassesIdFilter(frame.MsgId))
                                    continue;
                                if (_dbcOnlyMode && !ContainsDbcMessage(frame.MsgId))
                                    continue;
                                _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Message, ScrollFrameIndex = i, FlatIndex = _flatRows.Count });
                                if (_expandedScrollFrames.Contains(i))
                                {
                                    if (TryFindDbcMessage(frame.MsgId, frame.Channel, out var dbcMsg))
                                    {
                                        for (int sigIdx = 0; sigIdx < dbcMsg.signals.Count; sigIdx++)
                                         _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Signal, ScrollFrameIndex = i, SigIndex = sigIdx, FlatIndex = _flatRows.Count });
                                    }
                                }
                            }
                            _scrollFramesLoaded = _scrollFrames.Count;
                        }
                        else
                        {
                            // ====== 接收模式：全量重建，只显示最新20条匹配帧 ======
                            int matched = 0;
                            int start = Math.Max(0, _scrollFrames.Count - SCROLL_LIVE_FRAMES);
                            for (int i = _scrollFrames.Count - 1; i >= 0 && matched < SCROLL_LIVE_FRAMES; i--)
                            {
                                var f = _scrollFrames[i];
                                if (!PassesIdFilter(f.MsgId) ||
                                    (_dbcOnlyMode && !ContainsDbcMessage(f.MsgId)))
                                    continue;
                                matched++;
                                start = i;
                            }

                            _flatRows.Clear();
                            for (int i = start; i < _scrollFrames.Count; i++)
                            {
                                var frame = _scrollFrames[i];
                                if (!PassesIdFilter(frame.MsgId))
                                    continue;
                                if (_dbcOnlyMode && !ContainsDbcMessage(frame.MsgId))
                                    continue;
                                _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Message, ScrollFrameIndex = i, FlatIndex = _flatRows.Count });
                                if (_expandedScrollFrames.Contains(i))
                                {
                                    if (TryFindDbcMessage(frame.MsgId, frame.Channel, out var dbcMsg))
                                    {
                                        for (int s = 0; s < dbcMsg.signals.Count; s++)
                                            _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Signal, ScrollFrameIndex = i, SigIndex = s, FlatIndex = _flatRows.Count });
                                    }
                                }
                            }
                            // 接收模式不做增量，下次Tick全量重算
                            _scrollFramesLoaded = _scrollFrames.Count;
                        }
                    }
                }
                else
                {
                    if (!_flatRowsDirty && _flatRows.Count > 0)
                        return; // Fixed模式无结构变化时跳过

                    _flatRows.Clear();
                    for (int i = 0; i < _displayList.Count; i++)
                    {
                        var msg = _displayList[i];
                        if (!PassesIdFilter(msg.MsgId))
                            continue;
                        if (_dbcOnlyMode && !ContainsDbcMessage(msg.MsgId))
                            continue;

                        var fi = new FlatRowInfo { Type = FlatRowType.Message, MsgIndex = i, FlatIndex = _flatRows.Count };
                        _flatRows.Add(fi);

                        if (_expandedIds.Contains(MsgKey(msg.MsgId, msg.Channel)))
                        {
                            if (TryFindDbcMessage(msg.MsgId, msg.Channel, out var dbcMsg))
                            {
                                for (int s = 0; s < dbcMsg.signals.Count; s++)
                                    _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Signal, MsgIndex = i, SigIndex = s, FlatIndex = _flatRows.Count });
                            }
                        }
                    }
                }
            }
            _flatRowsDirty = false;
        }

        /// <summary>刷新报文显示（UI线程）- VirtualMode + 节流</summary>
        private void RefreshMessageDisplay()
        {
            if (_dgvMessages == null || _dgvMessages.IsDisposed) return;
            if (!_msgDisplayRefreshPending) return;
            if (_pauseUpdate) return; // 暂停时冻结画面，不再更新

            // 节流：最小刷新间隔 ~10fps
            var now = DateTime.Now;
            if ((now - _lastRefreshTime).TotalMilliseconds < MIN_REFRESH_MS)
                return;

            _msgDisplayRefreshPending = false;
            _lastRefreshTime = now;

            // 重新构建扁平行列表
            RebuildFlatRows();

            _dgvMessages.SuspendLayout();
            int targetCount = _flatRows.Count;

            if (_dgvMessages.RowCount != targetCount)
            {
                // Fixed 模式行数差异大时，先切到0再切目标行（避免DataGridView内部大量重算）
                if (!_scrollMode && targetCount > _displayList.Count * 5)
                    _dgvMessages.RowCount = 0;
                _dgvMessages.RowCount = targetCount;
            }
            else
            {
                // RowCount 不变时仍需强制刷新单元格内容（Scroll 模式最新帧替换旧帧）
                _dgvMessages.Invalidate();
            }
            _dgvMessages.ResumeLayout();

            // Scroll模式：仅在用户已在底部时自动跟随（节流：只对增量超过20行时才滚动）
            if (_scrollMode && targetCount > 0 && !_userScrolledAway)
            {
                // 到达底部时自动跟随最新帧
                if (targetCount > _lastScrollRowCount)
                {
                    try { _dgvMessages.FirstDisplayedScrollingRowIndex = targetCount - 1; }
                    catch { }
                    _lastScrollRowCount = targetCount;
                }
            }

            // 数据刷新后重新调整列宽（scrollbar 可能出现/消失）
            RepositionFilterAndToolbar();
        }

        /// <summary>VirtualMode: 按需提供单元格值（框架按需调用）</summary>
        private void DgvMessages_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _flatRows.Count) return;
            var flat = _flatRows[e.RowIndex];

            if (flat.Type == FlatRowType.Message)
            {
                // Scroll模式：从_scrollFrames取每帧数据
                if (flat.ScrollFrameIndex >= 0)
                {
                    ScrollFrameRecord frame;
                    lock (_scrollFrames)
                    {
                        if (flat.ScrollFrameIndex >= _scrollFrames.Count) return;
                        frame = _scrollFrames[flat.ScrollFrameIndex];
                    }
                    // frame 引用获取后立即释放锁，后续使用缓存字段（frame 不可变，安全）
                    switch (_dgvMessages.Columns[e.ColumnIndex].Name)
                    {
                        case "colFilter":    e.Value = ""; break;
                        case "colCount":     e.Value = (flat.ScrollFrameIndex + 1).ToString(); break;
                        case "colTime":
                            {
                                // 与"同通道同ID上一帧"的间隔：直接读写入时预计算的字段（原为每行O(n)前向扫描，5w帧全显时O(n²)卡顿）
                                ulong gapUs = frame.PrevSameIdGapUs;
                                e.Value = gapUs > 0
                                    ? (gapUs >= 1000 ? $"{gapUs / 1000.0:F1}ms" : $"{gapUs}us")
                                    : "0";
                            }
                            break;
                        case "colTx":        e.Value = frame.IsTx ? "Tx" : "Rx"; break;
                        case "colErr":       e.Value = ""; break;
                        case "colDesc":
                            {
                                // 从缓存查Description
                                if (_msgMetaCache.TryGetValue(MsgKey(frame.MsgId, frame.Channel), out var meta))
                                    e.Value = meta.desc;
                                else
                                    e.Value = "";
                            }
                            break;
                        case "colMsgId":     e.Value = $"0x{frame.MsgId:X3}"; break;
                        case "colLen":       e.Value = frame.Len.ToString(); break;
                        case "colData":      e.Value = FormatHexBytes(frame.Data, frame.Len); break;
                        case "colNetwork":
                            {
                                e.Value = (frame.MsgId > 0x7FF) ? "扩展帧" : "标准帧";
                            }
                            break;
                        case "colNode":
                            {
                                e.Value = frame.Channel > 0 ? $"CH{frame.Channel}" : "-";
                            }
                            break;
                        case "colChangeCnt": e.Value = ""; break;
                        case "colTimestamp":
                            {
                                long relTs = (long)frame.TimestampUs - _sessionStartUs;
                                if (relTs < 0) relTs = 0;
                                e.Value = (relTs / 1000000.0).ToString("F3");
                            }
                            break;
                        case "colMaxGap":    e.Value = ""; break;
                        case "colMinGap":    e.Value = ""; break;
                    }
                    return;
                }

                if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
                CanMsgDisplayInfo info = _displayList[flat.MsgIndex];
                switch (_dgvMessages.Columns[e.ColumnIndex].Name)
                {
                    case "colFilter":    e.Value = ""; break;
                    case "colCount":     e.Value = info.Count.ToString(); break;
                    case "colTime":      e.Value = info.TimeGap; break;
                    case "colTx":        e.Value = info.IsTx ? "Tx" : "Rx"; break;
                    case "colErr":       e.Value = info.IsLost ? "Err" : ""; break;
                    case "colDesc":      e.Value = info.Description; break;
                    case "colMsgId":     e.Value = $"0x{info.MsgId:X3}"; break;
                    case "colLen":       e.Value = info.Len.ToString(); break;
                    case "colData":      e.Value = info.DataBytes; break;
                    case "colNetwork":
                        {
                            e.Value = info.IsExtended ? "扩展帧" : "标准帧";
                        }
                        break;
                    case "colNode":
                        {
                            e.Value = info.Channel > 0 ? $"CH{info.Channel}" : "-";
                        }
                        break;
                    case "colChangeCnt": e.Value = info.ChangeCnt.ToString(); break;
                    case "colTimestamp": e.Value = info.Timestamp.ToString("F3"); break;
                    case "colMaxGap":
                        {
                            if (info.MaxTimeGapUs > 0)
                                e.Value = info.MaxTimeGapUs >= 1000 ? $"{info.MaxTimeGapUs / 1000.0:F1}ms" : $"{info.MaxTimeGapUs}us";
                            else
                                e.Value = "-";
                        }
                        break;
                    case "colMinGap":
                        {
                            if (info.MinTimeGapUs < long.MaxValue)
                                e.Value = info.MinTimeGapUs >= 1000 ? $"{info.MinTimeGapUs / 1000.0:F1}ms" : $"{info.MinTimeGapUs}us";
                            else
                                e.Value = "-";
                        }
                        break;
                }
            }
            else // Signal
             {
                 // 获取信号所属MsgId/通道/Node
                 uint sigMsgId;
                 byte sigChannel;
                 string sigNode;
                 if (flat.ScrollFrameIndex >= 0)
                 {
                     // Scroll模式信号行：从帧记录取MsgId与通道
                     lock (_scrollFrames)
                     {
                         if (flat.ScrollFrameIndex >= _scrollFrames.Count) return;
                         var frame = _scrollFrames[flat.ScrollFrameIndex];
                         sigMsgId = frame.MsgId;
                         sigChannel = frame.Channel;
                     }
                     // Node从缓存查
                     sigNode = _msgMetaCache.TryGetValue(MsgKey(sigMsgId, sigChannel), out var meta) ? meta.node : "";
                 }
                 else
                 {
                     if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
                     sigMsgId = _displayList[flat.MsgIndex].MsgId;
                     sigChannel = _displayList[flat.MsgIndex].Channel;
                     sigNode = _displayList[flat.MsgIndex].Node;
                 }

                 if (TryFindDbcMessage(sigMsgId, sigChannel, out var dbcMsg))
                 {
                     if (flat.SigIndex >= 0 && flat.SigIndex < dbcMsg.signals.Count)
                     {
                         var sig = dbcMsg.signals[flat.SigIndex];
                         switch (_dgvMessages.Columns[e.ColumnIndex].Name)
                         {
                             case "colFilter":    e.Value = ""; break;
                             case "colDesc":
                                 e.Value = "  " + sig.signalName;
                                 break;
                             case "colData":
                                 if (flat.ScrollFrameIndex >= 0)
                                 {
                                     // Scroll模式：从帧数据中解码信号（实时解析，不缓存）
                                     ScrollFrameRecord frame2;
                                     lock (_scrollFrames)
                                     {
                                         if (flat.ScrollFrameIndex >= _scrollFrames.Count) break;
                                         frame2 = _scrollFrames[flat.ScrollFrameIndex];
                                     }
                                     // 确保数据长度足够
                                     byte[] sigData = frame2.Data;
                                     if (sigData != null && sigData.Length > 0)
                                     {
                                         var parsed = CanSignalParser.ParseSignals(sigData, dbcMsg.signals);
                                         // 组装显示字符串
                                         string hexRaw = sig.rawValue.ToString("X2");
                                         hexRaw = DbcHelper.FormatSignalResult(hexRaw, 8);
                                         if (parsed.TryGetValue(sig.signalName, out double physVal))
                                         {
                                             sig.result = physVal; // 更新供Format使用
                                             int enumKey = (int)physVal;
                                             if (sig.enumDefinitions.Count > 0 && sig.enumDefinitions.ContainsKey(enumKey))
                                             {
                                                 e.Value = hexRaw + sig.enumDefinitions[enumKey];
                                             }
                                             else if (sig.unitStr != "\"\"" && sig.unitStr != "-" && !string.IsNullOrEmpty(sig.unitStr))
                                             {
                                                 e.Value = hexRaw + physVal.ToString() + " " + sig.unitStr;
                                             }
                                             else
                                             {
                                                 e.Value = hexRaw + physVal.ToString();
                                             }
                                         }
                                         else
                                         {
                                             e.Value = sig.signalDisplayStr;
                                         }
                                     }
                                     else
                                     {
                                         e.Value = sig.signalDisplayStr;
                                     }
                                 }
                                 else
                                 {
                                     e.Value = sig.signalDisplayStr;
                                 }
                                 break;
                             case "colLen":
                                 e.Value = sig.signalSize.ToString();
                                 break;
                             case "colNode":
                                 e.Value = sigNode; // 与父行Node保持一致
                                 break;
                             default:
                                 e.Value = "";
                                 break;
                         }
                     }
                 }
             }
        }

        /// <summary>CellFormatting: Tx/Err/信号行颜色</summary>
        private void DgvMessages_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _flatRows.Count) return;
            var flat = _flatRows[e.RowIndex];

            if (flat.Type == FlatRowType.Signal)
            {
                // 信号行：浅灰背景 + 小字体
                e.CellStyle.Font = _signalFont;
                e.CellStyle.BackColor = Color.FromArgb(245, 245, 248);
                // 仅 DataBytes 列根据信号字节变化高亮
                string cName = _dgvMessages.Columns[e.ColumnIndex].Name;
                if (cName == "colData" && flat.MsgIndex >= 0 && flat.MsgIndex < _displayList.Count && flat.SigIndex >= 0)
                {
                    var sigInfo = _displayList[flat.MsgIndex];
                    if (sigInfo.SigChanged != null && flat.SigIndex < sigInfo.SigChanged.Length)
                    {
                        int fadeMax = sigInfo.CycleTimeMs > 0
                            ? Math.Max(3, (int)Math.Round(1000.0 / sigInfo.CycleTimeMs))
                            : 10;
                        int sc = sigInfo.SigChanged[flat.SigIndex];
                        if (sc >= 0 && sc <= fadeMax)
                            e.CellStyle.BackColor = Color.FromArgb(200, 230, 200);
                    }
                }
                return;
            }

            // 消息行：Tx/Err颜色（Fixed模式下启用，Scroll模式跳过）
            if (flat.ScrollFrameIndex >= 0) return;
            if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
            var info = _displayList[flat.MsgIndex];
            string colName = _dgvMessages.Columns[e.ColumnIndex].Name;
            if (colName == "colTx" && info.IsTx)
                e.CellStyle.ForeColor = Color.Green;
            else if (colName == "colMaxGap" && info.CycleTimeMs > 0)
            {
                // 仅 MaxGap 超过120%标准周期时标红，MinGap 偏小属于正常情况不标红
                if (info.MaxTimeGapUs > 0)
                {
                    long threshold120 = info.CycleTimeMs * 1200L; // 120% 上限 (微秒)
                    if (info.MaxTimeGapUs > threshold120)
                        e.CellStyle.ForeColor = Color.Red;
                }
            }
        }

        /// <summary>CellPainting: DataBytes高亮 + 展开按钮(+/−)绘制</summary>
        private void DgvMessages_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            try
            {
                // ---------- 防御性空检查 ----------
                if (_flatRows == null || _displayList == null) return;

                // ---------- CAN通道列头排序图标绘制 ----------
                if (e.RowIndex == -1 && e.ColumnIndex >= 0)
                {
                    string colName = _dgvMessages.Columns[e.ColumnIndex].Name;
                    if (colName == "colNode" && _channelSortOrder != ChannelSortOrder.None)
                    {
                        // 绘制默认表头背景
                        e.PaintBackground(e.CellBounds, true);
                        
                        // 绘制表头文字
                        string headerText = "CAN通道";
                        using (Font headerFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
                        using (Brush textBrush = new SolidBrush(Color.Black))
                        {
                            StringFormat sf = new StringFormat
                            {
                                Alignment = StringAlignment.Near,
                                LineAlignment = StringAlignment.Center
                            };
                            Rectangle textRect = new Rectangle(e.CellBounds.Left + 4, e.CellBounds.Top, 
                                e.CellBounds.Width - 20, e.CellBounds.Height);
                            e.Graphics.DrawString(headerText, headerFont, textBrush, textRect, sf);
                            sf.Dispose();
                        }

                        // 绘制排序箭头
                        int arrowX = e.CellBounds.Right - 16;
                        int arrowY = e.CellBounds.Top + e.CellBounds.Height / 2;
                        int arrowSize = 5;
                        
                        Point[] arrowPoints;
                        if (_channelSortOrder == ChannelSortOrder.Ascending)
                        {
                            // 向上箭头 (▲)
                            arrowPoints = new Point[]
                            {
                                new Point(arrowX, arrowY + arrowSize),
                                new Point(arrowX + arrowSize, arrowY + arrowSize),
                                new Point(arrowX + arrowSize / 2, arrowY - arrowSize)
                            };
                        }
                        else // Descending
                        {
                            // 向下箭头 (▼)
                            arrowPoints = new Point[]
                            {
                                new Point(arrowX, arrowY - arrowSize),
                                new Point(arrowX + arrowSize, arrowY - arrowSize),
                                new Point(arrowX + arrowSize / 2, arrowY + arrowSize)
                            };
                        }
                        
                        using (Brush arrowBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                        {
                            e.Graphics.FillPolygon(arrowBrush, arrowPoints);
                        }

                        e.Handled = true;
                        return;
                    }
                }

                if (e.RowIndex < 0 || e.RowIndex >= _flatRows.Count) return;
                var flat = _flatRows[e.RowIndex];
                if (flat == null) return;
                bool isSelected = e.State.HasFlag(DataGridViewElementStates.Selected);

                // ---------- DataBytes 高亮 ----------
                var colData = _dgvMessages.Columns["colData"];
                if (colData != null && e.ColumnIndex == colData.Index && flat.Type == FlatRowType.Message)
                {
                    // Scroll模式下每帧独立，不做字节高亮
                    if (flat.ScrollFrameIndex >= 0) return;

                    if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
                    var info = _displayList[flat.MsgIndex];
                    if (info == null || info.ByteChanged == null || e.Value == null) return;

                    string dataStr = e.Value.ToString();
                    if (string.IsNullOrEmpty(dataStr)) return;

                    string[] bytes = dataStr.Split(' ');
                    if (bytes == null || bytes.Length == 0) return;

                    e.Handled = true;
                    using (Brush bgBrush = new SolidBrush(isSelected
                        ? _dgvMessages.DefaultCellStyle.SelectionBackColor
                        : e.CellStyle.BackColor.IsEmpty ? _dgvMessages.DefaultCellStyle.BackColor : e.CellStyle.BackColor))
                    {
                        e.Graphics.FillRectangle(bgBrush, e.CellBounds);
                    }

                    float x = e.CellBounds.Left + 2;
                    float cellHeight = e.CellBounds.Height;
                    var byteChanged = info.ByteChanged;

                    if (bytes == null || byteChanged == null) return;
                    // 根据周期生成动态渐变颜色
                    int fadeMax = info.CycleTimeMs > 0
                        ? Math.Max(3, (int)Math.Round(1000.0 / info.CycleTimeMs))
                        : 10;
                    Color[] fadeColors = new Color[fadeMax + 1];
                    for (int fi = 0; fi <= fadeMax; fi++)
                    {
                        int alpha = 255 - fi * 255 / fadeMax;
                        fadeColors[fi] = Color.FromArgb(alpha, 173, 216, 230);
                    }
                    
                    for (int i = 0; i < bytes.Length && i < byteChanged.Length; i++)
                    {
                        float width = bytes[i] != null ? bytes[i].Length * 9f + 4 : 4f;
                        RectangleF byteRect = new RectangleF(x, e.CellBounds.Top, width, cellHeight);
                        
                        // 渐变淡出：根据帧数选择不同透明度
                        int fadeLevel = byteChanged[i];
                        if (fadeLevel >= 0 && fadeLevel <= fadeMax)
                        {
                            e.Graphics.FillRectangle(new SolidBrush(fadeColors[fadeLevel]), byteRect);
                        }
                        
                        e.Graphics.DrawString(bytes[i] ?? "", _dataFont, _defaultTextBrush, byteRect, _paintSf);
                        x += width + 4;
                    }
                    e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                    return;
                }

                // ---------- colFilter 展开按钮绘制 ----------
                var colFilter = _dgvMessages.Columns["colFilter"];
                if (colFilter != null && e.ColumnIndex == colFilter.Index && flat.Type == FlatRowType.Message)
                {
                    // 获取 MsgId 与通道
                    uint fMsgId;
                    byte fChannel;
                    if (flat.ScrollFrameIndex >= 0)
                    {
                        lock (_scrollFrames)
                        {
                            if (_scrollFrames == null || flat.ScrollFrameIndex >= _scrollFrames.Count) return;
                            fMsgId = _scrollFrames[flat.ScrollFrameIndex].MsgId;
                            fChannel = _scrollFrames[flat.ScrollFrameIndex].Channel;
                        }
                    }
                    else
                    {
                        if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
                        fMsgId = _displayList[flat.MsgIndex].MsgId;
                        fChannel = _displayList[flat.MsgIndex].Channel;
                    }

                    // 检查是否有 DBC 信号
                    bool hasSignals = false;
                    if (TryFindDbcMessage(fMsgId, fChannel, out var dbcChk))
                        hasSignals = dbcChk.signals.Count > 0;

                    if (!hasSignals) return; // 无信号不绘制

                    e.Handled = true;
                    using (Brush bgBrush = new SolidBrush(isSelected
                        ? _dgvMessages.DefaultCellStyle.SelectionBackColor
                        : e.CellStyle.BackColor.IsEmpty ? _dgvMessages.DefaultCellStyle.BackColor : e.CellStyle.BackColor))
                    {
                        e.Graphics.FillRectangle(bgBrush, e.CellBounds);
                    }

                    bool isExpanded = flat.ScrollFrameIndex >= 0
                        ? _expandedScrollFrames.Contains(flat.ScrollFrameIndex)
                        : _expandedIds.Contains(MsgKey(fMsgId, fChannel));
                    string btnText = isExpanded ? "−" : "+";
                    using (Font btnFont = new Font("Arial", 10f, FontStyle.Bold))
                    using (Brush btnBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    {
                        StringFormat sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        e.Graphics.DrawString(btnText, btnFont, btnBrush, e.CellBounds, sf);
                        sf.Dispose();
                    }
                    e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                    return;
                }

                // ---------- 信号行 DataBytes ----------
                if (colData != null && e.ColumnIndex == colData.Index && flat.Type == FlatRowType.Signal)
                {
                    // 信号行的 DataBytes 列不做特殊高亮，让默认绘制处理
                    return;
                }
            }
            catch
            {
                // 防御：CellPainting 中任何未预期的错误都不应导致程序崩溃
            }
        }

        /// <summary>CellClick: 展开/折叠信号</summary>
        private void DgvMessages_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex != _dgvMessages.Columns["colFilter"].Index) return;

            ToggleMessageExpansion(e.RowIndex);
        }

        private void ToggleMessageExpansion(int rowIndex)
        {
            if (rowIndex >= _flatRows.Count) return;

            var flat = _flatRows[rowIndex];
            if (flat.Type != FlatRowType.Message) return;

            // 获取 MsgId 与通道
            uint msgId;
            byte channel;
            if (flat.ScrollFrameIndex >= 0)
            {
                // Scroll模式：从帧记录中取MsgId
                lock (_scrollFrames)
                {
                    if (flat.ScrollFrameIndex >= _scrollFrames.Count) return;
                    msgId = _scrollFrames[flat.ScrollFrameIndex].MsgId;
                    channel = _scrollFrames[flat.ScrollFrameIndex].Channel;
                }
            }
            else
            {
                if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return;
                msgId = _displayList[flat.MsgIndex].MsgId;
                channel = _displayList[flat.MsgIndex].Channel;
            }

            // 检查是否有 DBC 信号
            bool hasSignals = false;
            if (TryFindDbcMessage(msgId, channel, out var dbcChk))
                hasSignals = dbcChk.signals.Count > 0;
            if (!hasSignals) return;

            // 切换展开状态
            bool wasExpanded;
            if (flat.ScrollFrameIndex >= 0)
            {
                wasExpanded = _expandedScrollFrames.Contains(flat.ScrollFrameIndex);
                if (wasExpanded)
                    _expandedScrollFrames.Remove(flat.ScrollFrameIndex);
                else
                    _expandedScrollFrames.Add(flat.ScrollFrameIndex);
            }
            else
            {
                long key = MsgKey(msgId, channel);
                wasExpanded = _expandedIds.Contains(key);
                if (wasExpanded)
                    _expandedIds.Remove(key);
                else
                    _expandedIds.Add(key);
            }

            if (flat.ScrollFrameIndex >= 0)
            {
                // Scroll模式：直接在_flatRows中插入/删除信号行，避免全量重建
                if (wasExpanded)
                {
                    // 折叠：删除该帧索引的所有信号子行
                    _dgvMessages.SuspendLayout();
                    for (int i = _flatRows.Count - 1; i >= 0; i--)
                    {
                        if (_flatRows[i].Type == FlatRowType.Signal &&
                            _flatRows[i].ScrollFrameIndex == flat.ScrollFrameIndex)
                        {
                            _flatRows.RemoveAt(i);
                        }
                    }
                    // 更新FlatIndex
                    for (int i = 0; i < _flatRows.Count; i++)
                        _flatRows[i].FlatIndex = i;
                    _dgvMessages.RowCount = _flatRows.Count;
                    _dgvMessages.ResumeLayout();
                    _dgvMessages.Invalidate();
                }
                else
                {
                    // 展开：仅在当前点击的帧后插入信号行
                    _dgvMessages.SuspendLayout();
                    int insertPos = rowIndex + 1;
                    // 找到当前帧行在_flatRows中的位置
                    if (rowIndex < _flatRows.Count && _flatRows[rowIndex].ScrollFrameIndex == flat.ScrollFrameIndex)
                    {
                        for (int s = 0; s < dbcChk.signals.Count; s++)
                        {
                            _flatRows.Insert(insertPos + s, new FlatRowInfo
                            {
                                Type = FlatRowType.Signal,
                                ScrollFrameIndex = flat.ScrollFrameIndex,
                                SigIndex = s,
                                FlatIndex = -1
                            });
                        }
                    }
                    // 更新FlatIndex
                    for (int i = 0; i < _flatRows.Count; i++)
                        _flatRows[i].FlatIndex = i;
                    _dgvMessages.RowCount = _flatRows.Count;
                    _dgvMessages.ResumeLayout();
                    _dgvMessages.Invalidate();
                }
            }
            else
            {
                // Fixed模式：重建
                _flatRowsDirty = true;
                var savedPause = _pauseUpdate;
                _pauseUpdate = false;
                _msgDisplayRefreshPending = true;
                RefreshMessageDisplay();
                _pauseUpdate = savedPause;
            }
        }

        private void DgvMessages_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            // 加号列由CellClick处理，避免双击时重复切换；其他列支持双击整行展开/收起。
            if (e.ColumnIndex == _dgvMessages.Columns["colFilter"].Index) return;

            ToggleMessageExpansion(e.RowIndex);
        }

        /// <summary>CAN通道列头点击排序</summary>
        private void DgvMessages_ColumnHeaderMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.RowIndex >= 0) return; // 不是表头点击

            string colName = _dgvMessages.Columns[e.ColumnIndex].Name;
            if (colName != "colNode") return; // 只处理CAN通道列

            // 循环切换排序状态：None → Ascending → Descending → None
            switch (_channelSortOrder)
            {
                case ChannelSortOrder.None:
                    _channelSortOrder = ChannelSortOrder.Ascending;
                    break;
                case ChannelSortOrder.Ascending:
                    _channelSortOrder = ChannelSortOrder.Descending;
                    break;
                case ChannelSortOrder.Descending:
                    _channelSortOrder = ChannelSortOrder.None;
                    break;
            }

            // 执行排序
            if (_channelSortOrder == ChannelSortOrder.None)
            {
                // 恢复默认排序（Fixed模式按MsgId，Scroll模式按时间）
                if (!_scrollMode)
                {
                    // Fixed模式：按(MsgId,Channel)重新排序（同ID多通道相邻）
                    _displayList.Sort((a, b) =>
                    {
                        int c = a.MsgId.CompareTo(b.MsgId);
                        return c != 0 ? c : a.Channel.CompareTo(b.Channel);
                    });
                    RebuildMsgIndexMap();
                }
                else
                {
                    // Scroll模式：按时间戳重新排序
                    lock (_scrollFrames)
                    {
                        _scrollFrames.Sort((a, b) => a.TimestampUs.CompareTo(b.TimestampUs));
                    }
                }
            }
            else
            {
                // 按Channel排序
                int sortMultiplier = (_channelSortOrder == ChannelSortOrder.Ascending) ? 1 : -1;
                if (!_scrollMode)
                {
                    // Fixed模式：按Channel排序
                    _displayList.Sort((a, b) => sortMultiplier * a.Channel.CompareTo(b.Channel));
                    RebuildMsgIndexMap();
                }
                else
                {
                    // Scroll模式：按Channel排序
                    lock (_scrollFrames)
                    {
                        _scrollFrames.Sort((a, b) => sortMultiplier * a.Channel.CompareTo(b.Channel));
                    }
                }
            }

            // 重建显示
            _flatRowsDirty = true;
            _msgDisplayRefreshPending = true;
            RefreshMessageDisplay();
            _dgvMessages.Invalidate(); // 强制重绘以更新排序图标
        }

        /// <summary>重建(通道,MsgId)复合键到索引的映射</summary>
        private void RebuildMsgIndexMap()
        {
            _msgIndexMap.Clear();
            for (int i = 0; i < _displayList.Count; i++)
            {
                _msgIndexMap[MsgKey(_displayList[i].MsgId, _displayList[i].Channel)] = i;
            }
        }

        /// <summary>从_flatRows获取消息显示信息</summary>
        private CanMsgDisplayInfo GetDisplayInfo(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _flatRows.Count) return null;
            var flat = _flatRows[rowIndex];
            if (flat.Type != FlatRowType.Message) return null;
            if (flat.MsgIndex < 0 || flat.MsgIndex >= _displayList.Count) return null;
            return _displayList[flat.MsgIndex];
        }

        public Main()
        {
            InitializeComponent();
            main = this;
        }

        /// <summary>
        /// 创建顶部工具栏：左侧"通道管理"统一入口（硬件识别/连接/通道配置均在通道管理窗口），右侧功能按钮。
        /// 原连接区Designer控件保留在隐藏容器内作状态载体不再显示：
        /// button1/button5文本是连接状态机、comboBox是单通道兼容路径数据源。
        /// </summary>
        private void CreateConnectionStrip()
        {
            // 隐藏旧的顶部区域容器（子控件保留作状态载体，不再显示）
            groupBox1.Visible = false;
            groupBox2.Visible = false;
            button3.Visible = false;
            SendMsg.Visible = false;
            button6.Visible = false;
            button7.Visible = false;

            _connectionStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(20, 20),
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(8, 6, 8, 6),
                Dock = DockStyle.Top,
                AutoSize = true
            };

            // 硬件识别/连接操作/通道配置的统一入口（硬件↔通道↔DBC关系、连接/断开、CAN/CANFD模式均在其中）
            var itemChannelMgr = new ToolStripButton("通道管理", ToolbarIcons.Get("gear"));
            itemChannelMgr.Click += (s, e) => OpenChannelManager();
            _connectionStrip.Items.Add(itemChannelMgr);

            // 右侧功能按钮（右对齐，从右往左依次添加）
            var itemConvert = new ToolStripButton("数据转换", ToolbarIcons.Get("folder"));
            itemConvert.Alignment = ToolStripItemAlignment.Right;
            itemConvert.Click += button7_Click;
            var itemChart = new ToolStripButton("曲线绘制", ToolbarIcons.Get("chart"));
            itemChart.Alignment = ToolStripItemAlignment.Right;
            itemChart.Click += button6_Click;
            var itemSend = new ToolStripButton("发送报文", ToolbarIcons.Get("swap"));
            itemSend.Alignment = ToolStripItemAlignment.Right;
            itemSend.Click += SendMsg_Click;
            _btnRecordStart = new ToolStripButton("录制报文", ToolbarIcons.Get("save"));
            _btnRecordStart.Alignment = ToolStripItemAlignment.Right;
            _btnRecordStart.Click += button3_Click;
            _connectionStrip.Items.AddRange(new ToolStripItem[] { itemConvert, itemChart, itemSend, _btnRecordStart });

            this.Controls.Add(_connectionStrip);
            _connectionStrip.BringToFront();
        }

        /// <summary>创建报文显示工具栏（与绘图窗口一致的ToolStrip无边框风格）</summary>
        private void CreateMessageToolbar()
        {
            _toolbarPanel = new ToolStrip();
            _toolbarPanel.Name = "toolbarPanel";
            _toolbarPanel.GripStyle = ToolStripGripStyle.Hidden;
            _toolbarPanel.ImageScalingSize = new Size(20, 20);
            _toolbarPanel.RenderMode = ToolStripRenderMode.System;
            _toolbarPanel.Padding = new Padding(4, 2, 4, 2);
            _toolbarPanel.Font = UiTheme.UiFont;
            // 不使用Dock，改为手动定位在DataGridView上方
            _toolbarPanel.Dock = DockStyle.None;
            _toolbarPanel.AutoSize = true;

            // Scroll 按钮（CheckOnClick：Checked高亮表示Scroll模式开启）
            _btnScroll = new ToolStripButton("Fixed", ToolbarIcons.Get("scroll"));
            _btnScroll.CheckOnClick = true;
            _btnScroll.Click += (s, e) =>
            {
                _scrollMode = !_scrollMode;
                _btnScroll.Checked = _scrollMode;
                _btnScroll.Text = _scrollMode ? "Scroll" : "Fixed";
                // 切换后刷新模式
                _scrollFramesLoaded = 0; // 重置增量计数
                _userScrolledAway = false;
                _lastScrollRowCount = 0;
                _expandedScrollFrames.Clear();
                _flatRowsDirty = true;
                // 海量帧↔聚合视图切换：先清空行数，避免DataGridView对数万旧行做增量布局（切换卡顿主因之一）
                if (_dgvMessages.RowCount > 0) _dgvMessages.RowCount = 0;
                // 暂停只冻结新数据滚动，不应冻结视图模式切换——强制重建（否则回放完成的暂停态下切Fixed无反应）
                ForceRefreshDisplay();
            };
            _toolbarPanel.Items.Add(_btnScroll);

            // Pause 按钮（CheckOnClick：Checked高亮表示已暂停）
            _btnPause = new ToolStripButton("Pause", ToolbarIcons.Get("stop"));
            _btnPause.CheckOnClick = true;
            _btnPause.Click += (s, e) =>
            {
                bool wasPaused = _pauseUpdate;
                _pauseUpdate = !_pauseUpdate;
                _btnPause.Checked = _pauseUpdate;
                _btnPause.Text = _pauseUpdate ? "Paused" : "Pause";

                if (!_scrollMode)
                {
                    // Fixed模式：只切暂停状态，不清除数据
                    // 暂停时RefreshMessageDisplay跳过刷新，恢复时下次Tick恢复
                    _flatRowsDirty = true;
                    _msgDisplayRefreshPending = true;
                    if (!_pauseUpdate) RefreshMessageDisplay();
                    return;
                }

                _userScrolledAway = false;
                _lastScrollRowCount = 0;

                // 直接从 _scrollFrames 构建 _flatRows，不经过 RebuildFlatRows
                lock (_scrollFrames)
                {
                    _flatRows.Clear();

                    if (_pauseUpdate)
                    {
                        // ====== 暂停：显示全部帧 ======
                        for (int i = 0; i < _scrollFrames.Count; i++)
                        {
                            var frame = _scrollFrames[i];
                            if (!PassesIdFilter(frame.MsgId)) continue;
                            if (_dbcOnlyMode && !ContainsDbcMessage(frame.MsgId)) continue;
                            _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Message, ScrollFrameIndex = i, FlatIndex = _flatRows.Count });
                            if (_expandedScrollFrames.Contains(i))
                            {
                                if (TryFindDbcMessage(frame.MsgId, frame.Channel, out var dbcMsg))
                                {
                                    for (int sigIdx2 = 0; sigIdx2 < dbcMsg.signals.Count; sigIdx2++)
                                        _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Signal, ScrollFrameIndex = i, SigIndex = sigIdx2, FlatIndex = _flatRows.Count });
                                }
                            }
                        }
                        // 同步_scrollFramesLoaded，下次timer Tick只增量追加后续新帧
                        _scrollFramesLoaded = _scrollFrames.Count;
                    }
                    else
                    {
                        // ====== 退出暂停→Live：只显示最新20帧 ======
                        int matched = 0;
                        int start = Math.Max(0, _scrollFrames.Count - SCROLL_LIVE_FRAMES);
                        for (int i = _scrollFrames.Count - 1; i >= 0 && matched < SCROLL_LIVE_FRAMES; i--)
                        {
                            var f = _scrollFrames[i];
                            if (!PassesIdFilter(f.MsgId) ||
                                (_dbcOnlyMode && !ContainsDbcMessage(f.MsgId)))
                                continue;
                            matched++;
                            start = i;
                        }
                        for (int i = start; i < _scrollFrames.Count; i++)
                        {
                            var frame = _scrollFrames[i];
                            if (!PassesIdFilter(frame.MsgId)) continue;
                            if (_dbcOnlyMode && !ContainsDbcMessage(frame.MsgId)) continue;
                            _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Message, ScrollFrameIndex = i, FlatIndex = _flatRows.Count });
                            if (_expandedScrollFrames.Contains(i))
                            {
                                if (TryFindDbcMessage(frame.MsgId, frame.Channel, out var dbcMsg))
                                {
                                    for (int sigIdx3 = 0; sigIdx3 < dbcMsg.signals.Count; sigIdx3++)
                                        _flatRows.Add(new FlatRowInfo { Type = FlatRowType.Signal, ScrollFrameIndex = i, SigIndex = sigIdx3, FlatIndex = _flatRows.Count });
                                }
                            }
                        }
                    }
                }

                // 暂停/恢复都先切0再切目标行，强制DataGridView释放旧结构
                _dgvMessages.SuspendLayout();
                _dgvMessages.RowCount = 0;
                _dgvMessages.RowCount = _flatRows.Count;
                _dgvMessages.ResumeLayout();
                _dgvMessages.Invalidate();

                if (!_pauseUpdate && _flatRows.Count > 0)
                {
                    // 恢复live时滚动到底部
                    try { _dgvMessages.FirstDisplayedScrollingRowIndex = _flatRows.Count - 1; }
                    catch { }
                }
            };
            _toolbarPanel.Items.Add(_btnPause);

            // Clear 按钮
            _btnClear = new ToolStripButton("Clear", ToolbarIcons.Get("clear"));
            _btnClear.Click += (s, e) =>
            {
                // 清空同时复位暂停状态：暂停残留会冻结显示，清空后新数据（实时/回放）写入但不刷新
                SetPauseState(false);
                lock (_displayList)
                {
                    _displayList.Clear();
                    _displayList.TrimExcess();
                    _msgIndexMap.Clear();
                }
                lock (_scrollFrames)
                {
                    _scrollFrames.Clear();
                    _scrollFrames.TrimExcess();
                }
                _lastFrameTsByKey.Clear();
                _flatRows.Clear();
                _flatRows.TrimExcess();
                _expandedScrollFrames.Clear();
                _flatRowsDirty = true;
                _scrollFramesLoaded = 0;
                _userScrolledAway = false;
                _lastScrollRowCount = 0;
                _sessionStartUs = 0;
                _dgvMessages.RowCount = 0;
                _msgDisplayRefreshPending = true;
            };
            _toolbarPanel.Items.Add(_btnClear);

            // DBC Only 按钮（CheckOnClick：Checked高亮表示仅显示DBC报文）
            _btnDbcOnly = new ToolStripButton("全部报文", ToolbarIcons.Get("list"));
            _btnDbcOnly.CheckOnClick = true;
            _btnDbcOnly.Click += (s, e) =>
            {
                _dbcOnlyMode = !_dbcOnlyMode;
                _btnDbcOnly.Checked = _dbcOnlyMode;
                _btnDbcOnly.Text = _dbcOnlyMode ? "仅DBC报文" : "全部报文";
                // 切换后刷新
                _flatRowsDirty = true;
                _scrollFramesLoaded = 0;
                _msgDisplayRefreshPending = true;
                RefreshMessageDisplay();
            };
            _toolbarPanel.Items.Add(_btnDbcOnly);

            this.Controls.Add(_toolbarPanel);
        }

        internal void MngMAIN_OpenLogging()
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    if (null == DataLog.Logging.log)
                    {
                        DataLog.Logging.log = new DataLog.Logging();
                    }
                    else
                    {
                        /* empty */
                    }
                    Logging.SaveCSVPath = "";
                    Logging.log.MngLogging_StartSaveData();//开始保存数据（弹窗已移除，不再Show）
                    UpdateRecordButtonState();//录制状态同步到工具条按钮
                }));
            }
        }

        private void Main_Load(object sender, EventArgs e)
        {
            // 应用图标:读取exe内嵌图标(csproj ApplicationIcon)
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            this.Text = "CANInsight  " + BaseParamter.softVersion;

            // 顶部区域：与绘图页面一致的ToolStrip工具栏（原控件托管迁入，逻辑不变）
            CreateConnectionStrip();
            // 防止窗口过窄挤压工具栏
            this.MinimumSize = new Size(1200, 600);

            // 绘图主页面已由Program入口预建并显示(秒开优化),此处直接接管;
            // 未预建时(兼容路径)现场创建,保证绘图窗口关闭时退出程序
            chartFromShow = Program.PreloadedChart;
            if (chartFromShow == null)
            {
                chartFromShow = new ChartFrom();
                chartFromShow.FormClosed += (s, ev) => System.Windows.Forms.Application.Exit();
                chartFromShow.Show();
            }

            treeView1.Nodes.Add("Nodes");
            treeView1.Nodes.Add("Message");

            // 隐藏旧的 listView1，使用新的 DataGridView 替代
            listView1.Visible = false;

            // ---- 创建 IDs 筛选行（内嵌在DataGridView顶部）----
            _txtIdFilter = main.textBox__txtIdFilter;
            // 将原有的textBox重定位到DataGridView上方，风格化为筛选行
            _txtIdFilter.TextChanged += _txtIdFilter_TextChanged;
            _txtIdFilter.Visible = true;
            _txtIdFilter.BringToFront();
            // 位置和大小在 DataGridView 布局完成后设置（同步到 DataGridView 顶部）

            // ---- 创建工具栏区域 ----
            CreateMessageToolbar();

            // ---- 创建报文显示 DataGridView (VirtualMode) ----
            _dgvMessages = main.dataGridView1;
            _dgvMessages.Name = "dgvMessages";
            _dgvMessages.AllowUserToAddRows = false;
            _dgvMessages.AllowUserToDeleteRows = false;
            _dgvMessages.AllowUserToResizeRows = false;
            _dgvMessages.ReadOnly = true;
            _dgvMessages.RowHeadersVisible = false;
            _dgvMessages.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvMessages.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            // 应用公共主题（表头淡蓝灰/交替行/网格线/选中色/行高24），数据列字体随后单独指定
            UiTheme.StyleGrid(_dgvMessages);
            _dgvMessages.DefaultCellStyle.Font = new Font("Consolas", 9f);
            _dgvMessages.DefaultCellStyle.ForeColor = Color.Black;
            _dgvMessages.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _dgvMessages.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _dgvMessages.ShowCellToolTips = false;

            // === VirtualMode 启用 ===
             _dgvMessages.VirtualMode = true;
             _dgvMessages.CellValueNeeded += DgvMessages_CellValueNeeded;
             _dgvMessages.CellFormatting += DgvMessages_CellFormatting;
             _dgvMessages.CellPainting += DgvMessages_CellPainting;
             _dgvMessages.CellClick += DgvMessages_CellClick;
             _dgvMessages.CellDoubleClick += DgvMessages_CellDoubleClick;
             _dgvMessages.ColumnHeaderMouseDoubleClick += DgvMessages_ColumnHeaderMouseDoubleClick;
             _dgvMessages.Scroll += (ss, ee) =>
             {
                 // 检测用户是否在底部
                 if (_scrollMode && _dgvMessages.RowCount > 0)
                 {
                     bool wasScrolledAway = _userScrolledAway;
                     int visibleRows = _dgvMessages.DisplayedRowCount(false);
                     int firstRow = _dgvMessages.FirstDisplayedScrollingRowIndex;
                     bool atBottom = firstRow >= 0 && firstRow + visibleRows >= _dgvMessages.RowCount - 1;
                     _userScrolledAway = !atBottom;

                     // 用户上拉/回底过渡时，切换显示范围（从最新1000帧切换到全部，或反之）
                     if (wasScrolledAway != _userScrolledAway)
                     {
                         _flatRowsDirty = true;
                         _msgDisplayRefreshPending = true;
                         // 回到底部时重置滚动计数，确保自动跟随到最新帧
                         if (!_userScrolledAway)
                             _lastScrollRowCount = 0;
                         RefreshMessageDisplay();
                     }
                 }
             };

            // 启用双缓冲消除闪烁
            typeof(System.Windows.Forms.DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, _dgvMessages, new object[] { true });

            // 列定义：展开 | 次数 | 时间 | 发送 | 报文名称 | 报文ID | 长度 | 数据 | 帧类型 | CAN通道 | 变化次数 | 时间戳 | 最大/最小间隔
            _dgvMessages.Columns.Add("colFilter", "");
            _dgvMessages.Columns["colFilter"].Width = 20;
            _dgvMessages.Columns["colFilter"].HeaderText = "";

            _dgvMessages.Columns.Add("colCount", "次数");
            _dgvMessages.Columns["colCount"].Width = 70;
            _dgvMessages.Columns["colCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _dgvMessages.Columns.Add("colTime", "时间");
            _dgvMessages.Columns["colTime"].Width = 95;

            _dgvMessages.Columns.Add("colTx", "方向");
            _dgvMessages.Columns["colTx"].Width = 60;
            _dgvMessages.Columns["colTx"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvMessages.Columns["colTx"].DefaultCellStyle.Font = new Font("Segoe UI", 9f);

            _dgvMessages.Columns.Add("colDesc", "报文名称");
            _dgvMessages.Columns["colDesc"].Width = 240;

            _dgvMessages.Columns.Add("colMsgId", "报文ID");
            _dgvMessages.Columns["colMsgId"].Width = 80;

            _dgvMessages.Columns.Add("colLen", "长度");
            _dgvMessages.Columns["colLen"].Width = 60;
            _dgvMessages.Columns["colLen"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _dgvMessages.Columns.Add("colData", "数据");
            _dgvMessages.Columns["colData"].Width = 240;
            _dgvMessages.Columns["colData"].DefaultCellStyle.Font = new Font("Consolas", 9f);

            _dgvMessages.Columns.Add("colNetwork", "帧类型");
            _dgvMessages.Columns["colNetwork"].Width = 80;

            _dgvMessages.Columns.Add("colNode", "CAN通道");
            _dgvMessages.Columns["colNode"].Width = 90;

            _dgvMessages.Columns.Add("colChangeCnt", "变化次数");
            _dgvMessages.Columns["colChangeCnt"].Width = 90;
            _dgvMessages.Columns["colChangeCnt"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _dgvMessages.Columns.Add("colTimestamp", "时间戳");
            _dgvMessages.Columns["colTimestamp"].Width = 90;
            _dgvMessages.Columns["colTimestamp"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            _dgvMessages.Columns.Add("colMaxGap", "最大间隔");
            _dgvMessages.Columns["colMaxGap"].Width = 90;
            _dgvMessages.Columns["colMaxGap"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            _dgvMessages.Columns.Add("colMinGap", "最小间隔");
            _dgvMessages.Columns["colMinGap"].Width = 90;
            _dgvMessages.Columns["colMinGap"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            this.Controls.Add(_dgvMessages);

            // 设置工具栏在最上层显示
            if (_toolbarPanel != null && _dgvMessages != null)
            {
                _toolbarPanel.BringToFront();
            }

            // 初始化筛选输入框样式
            if (_txtIdFilter != null && _dgvMessages != null)
            {
                _txtIdFilter.Height = 22;
                _txtIdFilter.Font = new Font("Consolas", 9f);
                _txtIdFilter.BorderStyle = BorderStyle.FixedSingle;
                _txtIdFilter.BackColor = Color.FromArgb(255, 255, 240);
                _txtIdFilter.ForeColor = Color.Black;
                _txtIdFilter.BringToFront();
                _txtIdFilter.Text = "";
                _txtIdFilter.TabIndex = 0;
            }

            // 第一次布局调整（同步位置和大小）
            RepositionFilterAndToolbar();

            // 窗体大小变化时同步调整
            this.ResizeEnd += (s, args) => RepositionFilterAndToolbar();
            this.Resize += (s, args) => RepositionFilterAndToolbar();

            // 降低定时器频率减少CPU占用（200ms = 5fps，对报文显示足够）
            timer1.Interval = 200;

            timer1.Start();
            try
            {
                Task task = new Task(() =>
                {
                    try { GetPCAN_ComRefresh(); } catch { /* 单个设备枚举失败不影响另一个 */ }
                    try { GetCanoe_ComRefresh(); } catch { }
                });
                task.Start();
            }
            catch { }
            CAN_API.CAN_API.stopwatch.Restart();

            // 创建多消息调度器
            multiMessageCANScheduler = new MultiMessageCANScheduler(messages =>
            {

            });
            // 启动调度器
            multiMessageCANScheduler.Start();

            // 启动恢复通道配置（DBC唯一数据源），并刷新聚合视图供报文列表/发送使用
            BaseParamter.LoadBusChannelsConfig();

            // 恢复上次关闭前的绘图信号列表（含未点"保存工况"的改动）；
            // 必须在LoadBusChannelsConfig之后——BusChannels整体替换会新建DBC对象，提前恢复的标志位会被冲掉
            chartFromShow?.RestoreLastSessionSignals();

            // 用户点X关闭主窗口时,若绘图窗口仍开着则只隐藏不退出(程序经绘图窗口关闭退出)
            this.FormClosing += Main_FormClosingEx;

            // (绘图窗口已在Load开头优先显示,此处不再重复创建)
            // Load事件中直接Hide不生效(显示流程会覆盖),延迟到显示完成后再隐藏
            BeginInvoke(new Action(() => this.Hide()));
        }

        /// <summary>主窗口关闭拦截:绘图窗口仍开着时,点X只隐藏主窗口(需要看报文时可从绘图工具栏再次唤出)</summary>
        private void Main_FormClosingEx(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && ChartShowOpenFlag)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        public void PCAN_Connect(bool ShowFlag)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new EventHandler(delegate
                {
                    if (null == pCAN_API)
                    {
                        pCAN_API = new PCAN_API.PCAN_API();
                    }
                    else
                    {
                        /* empty */
                    }
                    if (button1.Text.Equals("已连接"))
                    {
                        pCAN_API.PCAN_ChannelUninitialize();
                        button1.Text = "连接";
                        pcanOpenFlag = false;
                        RefreshHwConnectedStatusLocal(); // 不做硬件识别，本地重算"已连接"状态
                    }
                    else
                    {
                        // 混合硬件：不再互斥断开CANoe，两类硬件可同时连接、独立断开
                        // 连接前清空数据（不改变scroll/fixed模式）；仅在两类硬件均未连接时清空（连第二类硬件时保留已有数据）
                        if (!pcanOpenFlag && !canoeOpenFlag) ClearDataOnly();

                        // 多通道模式：按通道管理窗口保存的映射直接连接（HwType=PCAN或未指定的通道由PCAN认领）；
                        // 不做硬件识别（识别只在通道管理窗口首开/手动刷新时进行），连接结果由ConnectMulti逐通道尝试得出
                        if (BaseParamter.BusChannels.Count > 0)
                        {
                            int connected = pCAN_API.ConnectMulti();
                            if (connected > 0)
                            {
                                button1.Text = "已连接";
                                pcanOpenFlag = true;
                                // 固化"未指定类型"通道的认领（避免混合连接时CANoe重复认领）
                                ClaimUntypedChannels(BaseParamter.HwTypePcan);
                                RefreshHwConnectedStatusLocal();
                            }
                            else
                            {
                                button1.Text = "连接";
                                pcanOpenFlag = false;
                                if (ShowFlag)
                                {
                                    MessageBox.Show("多通道连接失败：所有映射通道均连接失败（请在通道管理窗口\"刷新识别\"后检查硬件通道绑定与设备状态）");
                                }
                            }
                            return;
                        }

                        // 单通道兼容路径：未配置通道时按下拉框单选连接
                        int channel;
                        string[] pcanChannel = comboBox1.Text.Split('_', '(');
                        if (3 == pcanChannel.Length)
                        {
                            int.TryParse(pcanChannel[1], out channel);

                            pCAN_API.SetPcanChannel(channel - 1);
                            if (true == pCAN_API.Connect(false)) // 单通道兼容路径固定经典CAN（通道级配置见通道管理窗口）
                            {
                                button1.Text = "已连接";
                                pcanOpenFlag = true;
                                comboBox1.Items[comboBox1.SelectedIndex] = "USB_" + (comboBox1.SelectedIndex + 1) + "(已连接)";
                            }
                            else
                            {
                                button1.Text = "连接";
                                pcanOpenFlag = false;
                                if (ShowFlag)
                                {
                                    MessageBox.Show("连接失败");
                                }
                            }
                        }
                        else
                        {
                            // 下拉无有效通道（未识别到硬件，或插入后未点下拉刷新）：明确提示替代静默
                            if (ShowFlag) MessageBox.Show("未识别到PCAN硬件通道");
                        }
                    }
                }));
            }
        }
        private void button1_Click(object sender, EventArgs e)
        {
            CANDevice = "PCAN";
            PCAN_Connect(true);
        }

        internal void VoltRefresh(CAN_Data.CAN_Data.TslCanData tslCanData)
        {
            this.BeginInvoke(new EventHandler(delegate
            {

            }));
        }

        private void button3_Click(object sender, EventArgs e)
        {
            // 录制中报文配置窗口照常打开（对话框内有停止录制按钮）
            DataLog.LoggingSet.loggingSet.Show(); /* 非模态：录制中可用对话框内停止按钮，也可操作主界面 */
        }

        /// <summary>按 Logging.SaveFlag 刷新录制报文按钮状态（录制中红色高亮作状态指示，始终可点开配置窗口）</summary>
        internal void UpdateRecordButtonState()
        {
            if (this.IsDisposed || null == _btnRecordStart)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateRecordButtonState));
                return;
            }
            bool recording = DataLog.Logging.SaveFlag;
            _btnRecordStart.BackColor = recording ? Color.IndianRed : SystemColors.Control;
            // 同步录制设置对话框内的开始/停止按钮
            if (null != DataLog.LoggingSet.loggingSet && !DataLog.LoggingSet.loggingSet.IsDisposed)
            {
                DataLog.LoggingSet.loggingSet.SyncRecordButtons();
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!comboBox1.Text.Contains("未连接"))
            {
                Properties.Settings.Default.PCAN_Channel = comboBox1.Text.Split('(')[0];
                Properties.Settings.Default.Save();
            }
        }

        private string GetPCAN_ComRefresh()
        {
            Boolean flag = false;
            int channel = 0;
            if (null == Main.main.pCAN_API)
            {
                Main.main.pCAN_API = new PCAN_API.PCAN_API();
            }
            else
            {
                /* empty */
            }
            List<string> PCAN_Channel = Main.main.pCAN_API.GetPCAN_ChannelRefresh();
            _lastPcanHwList = ParsePcanHwList(PCAN_Channel); // 结构化识别缓存（通道管理窗口数据源）
            if(comboBox1.Items.Count == PCAN_Channel.Count)
            {
                for(int i=0; i< comboBox1.Items.Count; i++)
                {
                    if(!comboBox1.Items[i].Equals(PCAN_Channel[i]))
                    {
                        break;
                    }
                    if(i == (comboBox1.Items.Count-1))
                    {
                        return "None";
                    }
                }
            }

            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    comboBox1.Items.Clear();
                    for (int i = 0; i < PCAN_Channel.Count; i++)
                    {
                        comboBox1.Items.Add(PCAN_Channel[i]);
                        if (PCAN_Channel[i].Contains(Properties.Settings.Default.PCAN_Channel))
                        {
                            comboBox1.SelectedIndex = i;
                            flag = true;
                        }
                        else
                        {
                            /* empty */
                        }
                    }
                    if (!flag && 0 < comboBox1.Items.Count)
                    {
                        comboBox1.SelectedIndex = 0;
                    }
                    if (0 == comboBox1.Items.Count)
                    {
                        comboBox1.Items.Add("未连接PCAN");
                        comboBox1.SelectedIndex = 0;
                    }
                    // comboBox1已不在界面显示，填充仅作为单通道兼容路径（PCAN_Connect读取Text）的数据源
                }));
            }
            else
            {
                /* empty */
            }
            return "OK";
        }


        private void GetCanoe_ComRefresh()
        {
            if (null == canoe_API)
            {
                canoe_API = new Canoe_API.CanOe_API();
            }
            else
            {
                /* empty */
            }
            driverConfig = canoe_API.FindAllChannel(10, false); // 探测不依赖全局模式（通道级配置驱动）

            // 结构化识别缓存（通道管理窗口数据源）；已打开mask内的通道标记"已连接"
            _lastCanoeHwList = new List<HwChannelInfo>();
            for (int i = 0; i < driverConfig.channelCount; i++)
            {
                // 只统计CAN通道：排除虚拟通道与LIN等非CAN通道（如VN1640A第5路为LIN）
                if (driverConfig.channel[i].name.Contains("Virtual Channel")) continue;
                if ((driverConfig.channel[i].channelBusCapabilities & vxlapi_NET.XLDefine.XL_BusCapabilities.XL_BUS_ACTIVE_CAP_CAN) == 0) continue;
                // 已连接判定用OpenedChannelMask（CANOE_Open实际打开的通道集合）；
                // 不能用appChannelMask——FindAllChannel枚举会把它污染为最后一路CAN通道
                bool opened = canoeOpenFlag && null != canoe_API
                    && (canoe_API.OpenedChannelMask & (1UL << (int)driverConfig.channel[i].channelIndex)) != 0;
                _lastCanoeHwList.Add(new HwChannelInfo
                {
                    Hw = (byte)(driverConfig.channel[i].channelIndex + 1), // XL通道索引0-based→硬件通道号1-based
                    HwType = BaseParamter.HwTypeCanoe,
                    // XL通道名是C定长字符数组，封送后尾部可能带\0/空格（不可见但占宽度，导致单元格显示完整仍出现截断省略号）
                    Name = driverConfig.channel[i].name.TrimEnd('\0', ' ', '\t'),
                    Status = opened ? "已连接" : ""
                });
            }

            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    // 单通道模式才逐通道试开填充下拉框（每路约1秒）；多通道模式下拉框隐藏，跳过试开避免卡UI
                    if (BaseParamter.BusChannels.Count == 0)
                    {
                        comboBox_CanoeChannel.Items.Clear();
                        for (int i = 0; i < driverConfig.channelCount; i++)
                        {
                            if (!driverConfig.channel[i].name.Contains("Virtual Channel"))
                            {
                                if (canoe_API.CANOE_Open((ulong)(1 << ((int)driverConfig.channel[i].channelIndex)), false))
                                {
                                    comboBox_CanoeChannel.Items.Add(driverConfig.channel[i].name);
                                    if (Main.driverConfig.channel[i].name.Contains(Properties.Settings.Default.CANoe_Channel))
                                    {
                                        comboBox_CanoeChannel.SelectedIndex = comboBox_CanoeChannel.Items.Count - 1;
                                    }
                                    canoe_API.CANOE_Close();
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                        }
                        if (comboBox_CanoeChannel.Items.Count > 0)
                        {
                            if (!comboBox_CanoeChannel.Text.Contains(Properties.Settings.Default.CANoe_Channel))
                            {
                                comboBox_CanoeChannel.SelectedIndex = 0;
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                        else
                        {
                            comboBox_CanoeChannel.Items.Add("未连接CANoe");
                            comboBox_CanoeChannel.SelectedIndex = 0;
                        }
                    }
                    // comboBox_CanoeChannel已不在界面显示，填充仅作为单通道兼容路径（CANoeConnect读取Text）的数据源
                }));
            }
            else
            {
                /* empty */
            }
        }

        private void comboBox1_Click(object sender, EventArgs e)
        {
            button1.Text = "连接";
            pcanOpenFlag = false;
            GetPCAN_ComRefresh();
        }

        private void comboBox_CanoeChannel_Click(object sender, EventArgs e)
        {
            button5.Text = "连接";
            canoeOpenFlag = false;
            try
            {
                GetCanoe_ComRefresh();
            }
            catch { }
        }
        public void CANoeConnect(bool ShowFlag)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke((EventHandler)(delegate
                {
                    if (null == canoe_API)
                    {
                        canoe_API = new Canoe_API.CanOe_API();
                    }
                    else
                    {
                        /* empty */
                    }
                    if (button5.Text.Equals("已连接"))
                    {
                        canoe_API.CANOE_Close();
                        button5.Text = "连接";
                        canoeOpenFlag = false;
                        RefreshHwConnectedStatusLocal(); // 不做硬件识别，本地重算"已连接"状态
                    }
                    else
                    {
                        // 混合硬件：不再互斥断开PCAN，两类硬件可同时连接、独立断开
                        // 连接前清空数据（不改变scroll/fixed模式）；仅在两类硬件均未连接时清空（连第二类硬件时保留已有数据）
                        if (!pcanOpenFlag && !canoeOpenFlag) ClearDataOnly();

                        // 多通道模式：按通道管理窗口保存的映射直接连接（HwType=CANoe或未指定的通道由CANoe认领）；
                        // 不做硬件识别（识别只在通道管理窗口首开/手动刷新时进行）
                        if (BaseParamter.BusChannels.Count > 0)
                        {
                            if (_lastCanoeHwList.Count == 0)
                            {
                                if (ShowFlag) MessageBox.Show("未识别到CANoe硬件通道，请在通道管理窗口点击\"刷新识别\"");
                                return;
                            }

                            ulong mask = 0;
                            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
                            {
                                if (BaseParamter.BusChannels[i].HwChannel == BaseParamter.HwNotConnect) continue; // 不连接哨兵
                                string hwType = BaseParamter.GetEffectiveHwType(i);
                                if (hwType != "" && hwType != BaseParamter.HwTypeCanoe) continue; // 混合硬件：只连CANoe类型或未指定的通道
                                byte hw = BaseParamter.GetEffectiveHwChannel(i);
                                if (hw >= 1 && hw <= 64) mask |= (1UL << (hw - 1));
                            }
                            if (mask != 0 && canoe_API.CANOE_Open(mask, false)) // 多通道按行配置逐通道生效，false仅作无行配置兜底
                            {
                                button5.Text = "已连接";
                                canoeOpenFlag = true;
                                // 固化"未指定类型"通道的认领（避免混合连接时PCAN重复认领）
                                ClaimUntypedChannels(BaseParamter.HwTypeCanoe);
                                RefreshHwConnectedStatusLocal();
                            }
                            else
                            {
                                button5.Text = "连接";
                                canoeOpenFlag = false;
                                if (ShowFlag)
                                {
                                    MessageBox.Show("多通道连接失败（请在通道管理窗口\"刷新识别\"后检查硬件通道绑定与设备状态）");
                                }
                            }
                            return;
                        }

                        // 单通道兼容路径：未配置通道时按下拉框单选连接
                        for (int i = 0; i < driverConfig.channelCount; i++)
                        {
                            if (driverConfig.channel[i].name.Contains(comboBox_CanoeChannel.Text))
                            {
                                if (canoe_API.CANOE_Open((ulong)(1 << driverConfig.channel[i].channelIndex), false))
                                {
                                    button5.Text = "已连接";
                                    canoeOpenFlag = true;
                                }
                                else
                                {
                                    button5.Text = "连接";
                                    canoeOpenFlag = false;
                                    if (ShowFlag)
                                    {
                                        MessageBox.Show("连接失败");
                                    }
                                }
                                break;
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                    }
                }));
            }
        }
        // === 硬件识别缓存（通道管理窗口的数据源；连接区UI已移除，识别/连接/配置统一在通道管理窗口）===
        private List<HwChannelInfo> _lastPcanHwList = new List<HwChannelInfo>();
        private List<HwChannelInfo> _lastCanoeHwList = new List<HwChannelInfo>();

        /// <summary>当前识别到的PCAN硬件通道（结构化副本，供通道管理窗口使用）</summary>
        internal List<HwChannelInfo> PcanHwChannels => new List<HwChannelInfo>(_lastPcanHwList);
        /// <summary>当前识别到的CANoe硬件通道（结构化副本，供通道管理窗口使用）</summary>
        internal List<HwChannelInfo> CanoeHwChannels => new List<HwChannelInfo>(_lastCanoeHwList);

        /// <summary>主动刷新两类硬件识别（供通道管理窗口"刷新识别"按钮）</summary>
        internal void RefreshHardwareDetection()
        {
            GetPCAN_ComRefresh();
            GetCanoe_ComRefresh();
        }

        /// <summary>打开统一通道管理窗口（硬件识别/通道配置/DBC/映射/连接一窗统管）</summary>
        internal void OpenChannelManager()
        {
            using (var dlg = new ChannelManagerForm(this))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>把PCAN识别字符串（"USB_1(空闲)"）解析为结构化硬件通道信息</summary>
        internal static List<HwChannelInfo> ParsePcanHwList(List<string> pcanList)
        {
            var hwList = new List<HwChannelInfo>();
            foreach (var text in pcanList)
            {
                // 格式："USB_1(空闲)"/"USB_2(已占用)"/"USB_3(已连接)"
                string name = text.Split('(')[0];
                string status = text.Contains("(") ? text.Split('(', ')')[1] : "";
                if (int.TryParse(name.Replace("USB_", ""), out int hw) && hw >= 1 && hw <= 16)
                {
                    hwList.Add(new HwChannelInfo { Hw = (byte)hw, HwType = BaseParamter.HwTypePcan, Name = name, Status = status });
                }
            }
            return hwList;
        }

        /// <summary>混合硬件认领固化：把HwType未指定且非"不连接"的通道标记为本次连接的硬件类型并持久化（避免另一类硬件重复认领）</summary>
        private void ClaimUntypedChannels(string hwType)
        {
            bool changed = false;
            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
            {
                var ch = BaseParamter.BusChannels[i];
                if (!string.IsNullOrEmpty(ch.HwType)) continue;
                if (ch.HwChannel == BaseParamter.HwNotConnect) continue; // 255=不连接哨兵
                ch.HwType = hwType;
                changed = true;
            }
            if (changed) BaseParamter.SaveBusChannelsConfig();
        }

        /// <summary>本地重算识别缓存中各硬件通道的"已连接"状态（不做硬件访问；空闲/已占用等其余状态保留上次识别结果）</summary>
        internal void RefreshHwConnectedStatusLocal()
        {
            if (null != pCAN_API)
            {
                foreach (var hw in _lastPcanHwList)
                {
                    bool connected = pcanOpenFlag && pCAN_API.IsHwChannelConnected(hw.Hw);
                    if (connected) hw.Status = "已连接";
                    else if (hw.Status == "已连接") hw.Status = "空闲"; // 断开回落（刚被本程序释放，大概率为空闲）
                }
            }
            if (null != canoe_API)
            {
                foreach (var hw in _lastCanoeHwList)
                {
                    bool opened = canoeOpenFlag && hw.Hw >= 1 && hw.Hw <= 64
                        && (canoe_API.OpenedChannelMask & (1UL << (hw.Hw - 1))) != 0;
                    if (opened) hw.Status = "已连接";
                    else if (hw.Status == "已连接") hw.Status = "";
                }
            }
        }

        /// <summary>是否存在绑定到指定硬件类型的通道行（不连接哨兵除外；未指定类型的行两类都候选，由先连者认领）</summary>
        private bool HasBindingFor(string hwType)
        {
            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
            {
                if (BaseParamter.BusChannels[i].HwChannel == BaseParamter.HwNotConnect) continue;
                string t = BaseParamter.GetEffectiveHwType(i);
                if (t == "" || t == hwType) return true;
            }
            return false;
        }

        /// <summary>全部已绑定的通道是否都已连接（一键按钮文本判定）</summary>
        internal bool AllBoundChannelsConnected()
        {
            bool needPcan = HasBindingFor(BaseParamter.HwTypePcan);
            bool needCanoe = HasBindingFor(BaseParamter.HwTypeCanoe);
            if (!needPcan && !needCanoe) return false;
            return (!needPcan || pcanOpenFlag) && (!needCanoe || canoeOpenFlag);
        }

        /// <summary>一键连接所有通道（按各行绑定的硬件类型分别连接；已连的跳过）</summary>
        internal void ConnectAllChannels()
        {
            if (!pcanOpenFlag && HasBindingFor(BaseParamter.HwTypePcan)) PCAN_Connect(true);
            if (!canoeOpenFlag && HasBindingFor(BaseParamter.HwTypeCanoe)) CANoeConnect(true);
        }

        /// <summary>一键断开所有通道</summary>
        internal void DisconnectAllChannels()
        {
            if (pcanOpenFlag) PCAN_Connect(true);   // 已连接状态下调用=断开（切换语义）
            if (canoeOpenFlag) CANoeConnect(true);
        }

        /// <summary>某逻辑通道当前是否已连接（行级连接按钮状态判定）</summary>
        internal bool GetChannelConnected(int logicIndex)
        {
            string t = BaseParamter.GetEffectiveHwType(logicIndex);
            if (t == BaseParamter.HwTypePcan)
                return pcanOpenFlag && pCAN_API != null && pCAN_API.IsLogicChannelConnected(BaseParamter.GetLogicChannel(logicIndex));
            if (t == BaseParamter.HwTypeCanoe)
            {
                byte hw = BaseParamter.GetEffectiveHwChannel(logicIndex);
                return canoeOpenFlag && canoe_API != null && hw >= 1 && hw <= 64
                    && (canoe_API.OpenedChannelMask & (1UL << (hw - 1))) != 0;
            }
            return false;
        }

        /// <summary>连接单个逻辑通道（按其绑定的硬件类型增量连接，不影响其他已连接通道）</summary>
        internal void ConnectSingleChannel(int logicIndex)
        {
            if (logicIndex < 0 || logicIndex >= BaseParamter.BusChannels.Count) return;
            string t = BaseParamter.GetEffectiveHwType(logicIndex);
            if (t == BaseParamter.HwTypePcan && pCAN_API != null)
            {
                if (!pcanOpenFlag && !canoeOpenFlag) ClearDataOnly();
                if (pCAN_API.ConnectOne(logicIndex))
                {
                    pcanOpenFlag = true;
                    button1.Text = "已连接";
                }
                else
                {
                    MessageBox.Show($"通道 [{BaseParamter.BusChannels[logicIndex].Name}] 连接失败（请检查硬件通道绑定与设备状态）");
                }
            }
            else if (t == BaseParamter.HwTypeCanoe && canoe_API != null)
            {
                if (!pcanOpenFlag && !canoeOpenFlag) ClearDataOnly();
                byte hw = BaseParamter.GetEffectiveHwChannel(logicIndex);
                if (canoe_API.ActivateChannel(hw))
                {
                    canoeOpenFlag = true;
                    button5.Text = "已连接";
                }
                else
                {
                    MessageBox.Show($"通道 [{BaseParamter.BusChannels[logicIndex].Name}] 连接失败（请检查硬件通道绑定与设备状态）");
                }
            }
            RefreshHwConnectedStatusLocal();
        }

        /// <summary>按当前配置重连单个已连接通道（保存配置自动重连用）：与 ConnectSingleChannel 的区别是
        /// 不触发 ClearDataOnly（重连不应清掉已收数据）；PCAN走ConnectOne（内部Uninitialize+Initialize原子重开），
        /// CANoe先Deactivate再Activate（已连接通道Activate直接返回true不重开）</summary>
        internal void ReconnectSingleChannel(int logicIndex)
        {
            if (logicIndex < 0 || logicIndex >= BaseParamter.BusChannels.Count) return;
            string t = BaseParamter.GetEffectiveHwType(logicIndex);
            if (t == BaseParamter.HwTypePcan && pCAN_API != null)
            {
                if (pCAN_API.ConnectOne(logicIndex))
                {
                    pcanOpenFlag = true;
                    button1.Text = "已连接";
                }
                else
                {
                    MessageBox.Show($"通道 [{BaseParamter.BusChannels[logicIndex].Name}] 重连失败（请检查硬件通道绑定与设备状态）");
                }
            }
            else if (t == BaseParamter.HwTypeCanoe && canoe_API != null)
            {
                byte hw = BaseParamter.GetEffectiveHwChannel(logicIndex);
                canoe_API.DeactivateChannel(hw);
                if (canoe_API.OpenedChannelMask == 0)
                {
                    canoeOpenFlag = false;
                    button5.Text = "连接";
                }
                if (canoe_API.ActivateChannel(hw))
                {
                    canoeOpenFlag = true;
                    button5.Text = "已连接";
                }
                else
                {
                    MessageBox.Show($"通道 [{BaseParamter.BusChannels[logicIndex].Name}] 重连失败（请检查硬件通道绑定与设备状态）");
                }
            }
            RefreshHwConnectedStatusLocal();
        }

        /// <summary>断开单个逻辑通道（不影响其他已连接通道）</summary>
        internal void DisconnectSingleChannel(int logicIndex)
        {
            if (logicIndex < 0 || logicIndex >= BaseParamter.BusChannels.Count) return;
            string t = BaseParamter.GetEffectiveHwType(logicIndex);
            if (t == BaseParamter.HwTypePcan && pCAN_API != null)
            {
                if (pCAN_API.DisconnectOne(logicIndex) == 0)
                {
                    pcanOpenFlag = false;
                    button1.Text = "连接";
                }
            }
            else if (t == BaseParamter.HwTypeCanoe && canoe_API != null)
            {
                byte hw = BaseParamter.GetEffectiveHwChannel(logicIndex);
                canoe_API.DeactivateChannel(hw);
                if (canoe_API.OpenedChannelMask == 0)
                {
                    canoeOpenFlag = false;
                    button5.Text = "连接";
                }
            }
            RefreshHwConnectedStatusLocal();
        }

        /// <summary>断开PCAN连接并更新UI（供ChartFrom调用）</summary>
        internal void DisconnectPCAN()
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new EventHandler(delegate
                {
                    if (button1.Text.Equals("已连接"))
                    {
                        pCAN_API.PCAN_ChannelUninitialize();
                        button1.Text = "连接";
                        pcanOpenFlag = false;
                        RefreshHwConnectedStatusLocal(); // 不做硬件识别，本地重算"已连接"状态
                    }
                }));
            }
        }

        /// <summary>断开CANoe连接并更新UI（供ChartFrom调用）</summary>
        internal void DisconnectCANoe()
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new EventHandler(delegate
                {
                    if (button5.Text.Equals("已连接"))
                    {
                        canoe_API.CANOE_Close();
                        button5.Text = "连接";
                        canoeOpenFlag = false;
                        RefreshHwConnectedStatusLocal(); // 不做硬件识别，本地重算"已连接"状态
                    }
                }));
            }
        }

        /// <summary>CANoe发送链路死亡（连续发送失败，通常为硬件被拔出）：断开连接、刷新识别状态并提示（供CanOe_API调用）</summary>
        internal void OnCanoeTxLinkDead()
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new EventHandler(delegate
                {
                    if (button5.Text.Equals("已连接"))
                    {
                        canoe_API.CANOE_Close();
                        button5.Text = "连接";
                        canoeOpenFlag = false;
                        RefreshHwConnectedStatusLocal(); // 不做硬件识别，本地重算"已连接"状态
                        MessageBox.Show("CANoe硬件连接已断开，请检查设备连接");
                    }
                }));
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            CANDevice = "CANOE";
            CANoeConnect(true);
        }

        private void Main_FormClosed(object sender, FormClosedEventArgs e)
        {
            if(!(canoe_API is null))
            {
                canoe_API.CANOE_Close();
            }
            multiMessageCANScheduler.Stop();
            multiMessageCANScheduler.Dispose();
        }

        private void comboBox_CanoeChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(!comboBox_CanoeChannel.Text.Contains("未连接"))
            {
                Properties.Settings.Default.CANoe_Channel = comboBox_CanoeChannel.Text;
                Properties.Settings.Default.Save();
            }
        }

        internal void UpdateDbcTreeview()
        {
            updeteDbcSuccess = false;
            for (int i = 0; i < treeView1.Nodes.Count; i++)
            {
                treeView1.Nodes[i].Nodes.Clear();
            }

            for (int i = 0; i < BaseParamter.dbcHelper.dbcFile.nodes.Count; i++)
            {
                treeView1.Nodes[0].Nodes.Add(BaseParamter.dbcHelper.dbcFile.nodes[i]);
            }
            for (int i = 0; i < BaseParamter.dbcHelper.dbcFile.messages.Count; i++)
            {
                treeView1.Nodes[1].Nodes.Add(BaseParamter.dbcHelper.dbcFile.messages[i].messageName);
            }

            treeView1.ExpandAll();
            if(BaseParamter.dbcHelper.dbcFile.messages.Count <= SelectMessageIndex)
            {
                SelectMessageIndex = 0;
            }
            else
            {
                /* empty */
            }
            UpdateDbcListview(SelectMessageIndex);
            updeteDbcSuccess = true;
        }

        private void UpdateDbcListview(int index)
        {
            int i;
            if (BaseParamter.dbcHelper.dbcFile.messages.Count == 0)
            {
                return;
            }
            ListViewItem item;
            listView1.BeginUpdate();
            listView1.Items.Clear();

            try
            {
                for (i = 0; i < BaseParamter.dbcHelper.dbcFile.messages[index].signals.Count; i++)
                {
                    item = new ListViewItem();

                    item.Text = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].signalName;

                    item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].signalDisplayStr);
                    item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].Comment);
                    //if (false == (BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr.Equals("\"\"")) &&
                    //    (false == (BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr.Equals("-"))))
                    //{
                    //    item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString() + " " + BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr);
                    //}
                    //else
                    //{
                    //    item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString());
                    //}
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].startBit.ToString());
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].signalSize.ToString());
                    //item.SubItems.Add(ByteOrder[BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].byteOrder]);
                    //item.SubItems.Add(ValueType[BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].valueType]);
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].factor.ToString());
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].offset.ToString());
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].minimum.ToString());
                    //item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].maximum.ToString());
                    //if (false == (BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr.Equals("\"\"")))
                    //{
                    //    item.SubItems.Add("NULL");
                    //}
                    //else
                    //{
                    //    item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr);
                    //}
                    listView1.Items.Add(item);
                }
                item = new ListViewItem();
                item.Text = "接收数量";
                item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].receiveCnt.ToString());
                listView1.Items.Add(item);
                item = new ListViewItem();
                item.Text = "周期";
                item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[index].receiveTimeMs);
                listView1.Items.Add(item);

                listView1.EndUpdate();
            }catch (Exception ex) { Console.WriteLine(ex.Message); }
        }
        private void UpdateDbcListviewTimer(int index)
        {
            int i;
            if (BaseParamter.dbcHelper.dbcFile.messages.Count == 0 ||
                (listView1.Items.Count == 0)||
                false == updeteDbcSuccess)
            {
                return;
            }
            try
            {
                for (i = 0; i < BaseParamter.dbcHelper.dbcFile.messages[index].signals.Count; i++)
                {
                    if (!listView1.Items[i].SubItems[1].Text.Equals(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].signalDisplayStr))
                    {
                        listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].signalDisplayStr;
                    }
                    else
                    {
                        /* empty */
                    }
                    if (!listView1.Items[i].SubItems[2].Text.Equals(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].Comment))
                    {
                        listView1.Items[i].SubItems[2].Text = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].Comment;
                    }
                    //if (false == (BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr.Equals("\"\"")) &&
                    //   (false == (BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr.Equals("-"))))
                    //{
                    //    if (!(BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString() + " " + BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr).Equals(listView1.Items[i].SubItems[1].Text))
                    //    {
                    //        listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString() + " " + BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].unitStr;
                    //    }
                    //}
                    //else
                    //{
                    //    if (!BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString().Equals(listView1.Items[i].SubItems[1].Text))
                    //    {
                    //        listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[index].signals[i].result.ToString();
                    //    }
                    //}
                }
                if (!BaseParamter.dbcHelper.dbcFile.messages[index].receiveCnt.ToString().Equals(listView1.Items[i].SubItems[1].Text))
                {
                    listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[index].receiveCnt.ToString();
                }
                i++;
                if (!BaseParamter.dbcHelper.dbcFile.messages[index].receiveTimeMs.Equals(listView1.Items[i].SubItems[1].Text))
                {
                    listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[index].receiveTimeMs;
                }
                i++;
            } catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if ((e.Node.Parent != null) && (e.Node.Parent.Text == "Message"))
            {
                try
                {
                    SelectMessageIndex = e.Node.Index;
                    UpdateDbcListview(SelectMessageIndex);
                }
                catch
                {

                }
            }
        }

        private int _skipRefreshCounter = 0; // 跳过刷新计数器

        private void timer1_Tick(object sender, EventArgs e)
        {
            UpdateDbcListviewTimer(SelectMessageIndex);

            // Scroll 模式下至少每 500ms 强制刷新一次（即使无新数据也要更新画面）
            if (_scrollMode && !_pauseUpdate)
            {
                var now = DateTime.Now;
                if (now - _lastScrollForceRefresh >= SCROLL_FORCE_REFRESH_INTERVAL)
                {
                    _lastScrollForceRefresh = now;
                    _msgDisplayRefreshPending = true;
                }
            }

            // 节流：快速报文时每秒刷新太多次会导致CPU飙升
            // 偶数Tick且无待处理数据时跳过（慢速时减半刷新频率）
            _skipRefreshCounter++;
            if ((_skipRefreshCounter & 1) == 0 && !_msgDisplayRefreshPending)
                return;
            if (_msgDisplayRefreshPending)
                _skipRefreshCounter = 0; // 有数据时保持全速刷新

            RefreshMessageDisplay();
        }

        /// <summary>调整筛选行、工具栏、DGV大小以自适应窗口</summary>
        /// <summary>窗口宽度变化时，仅"报文名称"与"数据"两列自动伸缩铺满，其余列宽度固定不联动</summary>
        private int _lastFillClientWidth = -1;
        private void AutoFillDescAndDataColumns()
        {
            if (_dgvMessages == null || _dgvMessages.IsDisposed) return;
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int rowHeaderWidth = _dgvMessages.RowHeadersVisible ? _dgvMessages.RowHeadersWidth : 0;
            int availableWidth = _dgvMessages.ClientSize.Width - scrollBarWidth - rowHeaderWidth - 4;
            if (availableWidth < 50) availableWidth = 50;
            // 可用宽度未变化时不重排，保留用户对这两列的手动调整（仅窗口/滚动条变化时才重新铺满）
            if (availableWidth == _lastFillClientWidth) return;
            _lastFillClientWidth = availableWidth;

            var colDesc = _dgvMessages.Columns["colDesc"];
            var colData = _dgvMessages.Columns["colData"];
            if (colDesc == null || colData == null) return;

            int sumFixed = 0;
            foreach (DataGridViewColumn col in _dgvMessages.Columns)
            {
                if (col != colDesc && col != colData) sumFixed += col.Width;
            }
            int remaining = availableWidth - sumFixed;
            if (remaining < 80) remaining = 80; // 两列各保底40
            int half = remaining / 2;
            colDesc.Width = half;
            colData.Width = remaining - half;
        }

        private void RepositionFilterAndToolbar()
        {
            if (_dgvMessages == null || _dgvMessages.IsDisposed) return;

            // 计算DGV可用的宽度和高度（顶部工具栏高度动态获取，留出报文工具条位置）
            int dgvLeft = 9;
            int dgvTop = (_connectionStrip != null && !_connectionStrip.IsDisposed ? _connectionStrip.Bottom : 128) + 34;
            int marginRight = 6;
            int marginBottom = 8;

            int newWidth = this.ClientSize.Width - dgvLeft - marginRight;
            int newHeight = this.ClientSize.Height - dgvTop - marginBottom;

            if (newWidth < 100) newWidth = 100;
            if (newHeight < 100) newHeight = 100;

            // 保持DGV在表单中的位置不变但缩放尺寸
            _dgvMessages.Left = dgvLeft;
            _dgvMessages.Top = dgvTop;
            _dgvMessages.Width = newWidth;
            _dgvMessages.Height = newHeight;

            // 窗口宽度变化时仅"报文名称"与"数据"两列自动伸缩铺满，其余列固定
            AutoFillDescAndDataColumns();

            // 工具栏在最上方（紧贴DGV顶部，宽度在CreateMessageToolbar中按按钮总宽自适应）
            if (_toolbarPanel != null && !_toolbarPanel.IsDisposed)
            {
                _toolbarPanel.Left = _dgvMessages.Left;
                _toolbarPanel.Top = _dgvMessages.Top - _toolbarPanel.Height - 2;
            }

            // 筛选行在工具栏右边，同一行
            if (_txtIdFilter != null && !_txtIdFilter.IsDisposed)
            {
                _txtIdFilter.Left = (_toolbarPanel?.Right ?? dgvLeft) + 4;
                _txtIdFilter.Top = _dgvMessages.Top - 24;
                _txtIdFilter.Width = newWidth - (_txtIdFilter.Left - dgvLeft) - 2;
            }
        }

        private void _txtIdFilter_TextChanged(object sender, EventArgs e)
        {
            IdFilterRule.Parse(_txtIdFilter.Text, _filterIds, _filterWildcards);
            _flatRowsDirty = true;
            _msgDisplayRefreshPending = true;

            if (_pauseUpdate && _scrollMode)
            {
                // 暂停状态下直接重建，绕过RefreshMessageDisplay的_pauseUpdate检查
                RebuildFlatRows();
                _dgvMessages.SuspendLayout();
                _dgvMessages.RowCount = 0;
                _dgvMessages.RowCount = _flatRows.Count;
                _dgvMessages.ResumeLayout();
                _dgvMessages.Invalidate();
            }
            else
            {
                RefreshMessageDisplay();
            }
        }

        private void SendMsg_Click(object sender, EventArgs e)
        {
            if (false == canSendOpenFlag)
            {
                canSendOpenFlag = true;
                canSend = new CanSend();
                canSend.Show();
            }
            else
            {
                canSend.Dispose();
                canSend = new CanSend();
                canSend.Show();
            }
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            //try
            //{
            //    ListViewItem selectedItem = listView1.SelectedItems[0];
            //    if (null == signalChartShow || false == ChartShowOpenFlag)
            //    {
            //        ChartShowOpenFlag = true;
            //        signalChartShow = new SignalChartShow();
            //        signalChartShow.Show();
            //    }
            //    signalChartShow.AddSignalFun(BaseParamter.dbcHelper.dbcFile.messages[SelectMessageIndex], selectedItem.Text);
            //}
            //catch { }
            try
            {
                // 获取第一个选中项（在单选模式下就是双击的项）
                ListViewItem selectedItem = listView1.SelectedItems[0];
                if (null == chartFromShow || false == ChartShowOpenFlag)
                {
                    //ChartShowOpenFlag = true;
                    chartFromShow = new ChartFrom();
                    chartFromShow.Show();
                }
                //chartShow.AddOrRemoveChart(SelectMessageIndex, selectedItem.Text);
            }
            catch (Exception ex)
            {
                /* empty */
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            //if (null == signalChartShow || false == ChartShowOpenFlag)
            //{
            //    ChartShowOpenFlag = true;
            //    signalChartShow = new SignalChartShow();
            //    signalChartShow.Show();
            //}
            if (null == chartFromShow || false == ChartShowOpenFlag)
            {
                //ChartShowOpenFlag = true;
                chartFromShow = new ChartFrom();
                chartFromShow.Show();
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if(null == LogFileToCSV)
            {
                LogFileToCSV = new LogFileToCSV();
            }
            else
            {
                LogFileToCSV.Close();
                LogFileToCSV = null;
                LogFileToCSV = new LogFileToCSV();
            }
            LogFileToCSV.Show();
        }

        public class MultimediaTimer : IDisposable
        {
            // 定时器回调委托
            private delegate void TimeProc(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

            // Win32 API 导入
            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeSetEvent(
                uint uDelay,
                uint uResolution,
                TimeProc lpTimeProc,
                UIntPtr dwUser,
                uint fuEvent);

            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeKillEvent(uint uTimerID);

            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeBeginPeriod(uint uPeriod);

            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeEndPeriod(uint uPeriod);

            // 常量定义
            private const uint TIME_PERIODIC = 0x0001;
            private const uint TIME_ONESHOT = 0x0000;
            private const uint TIME_KILL_SYNC = 0x0100;

            private uint _timerId;
            private readonly TimeProc _timeProc;
            private readonly Action _callback;
            private bool _disposed = false;
            private bool _isRunning = false;

            public MultimediaTimer(Action callback)
            {
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
                _timeProc = new TimeProc(TimerCallback);
            }

            /// <summary>
            /// 启动定时器
            /// </summary>
            /// <param name="intervalMs">定时间隔（毫秒）</param>
            /// <param name="oneShot">是否只执行一次</param>
            public void Start(uint intervalMs, bool oneShot = false)
            {
                if (_isRunning) return;

                // 设置系统定时器精度（可选，但可以提高精度）
                timeBeginPeriod(1);

                uint mode = oneShot ? TIME_ONESHOT : TIME_PERIODIC;

                _timerId = timeSetEvent(
                    intervalMs,        // 延迟时间（毫秒）
                    0,                // 分辨率（0表示最高精度）
                    _timeProc,        // 回调函数
                    UIntPtr.Zero,     // 用户数据
                    mode);           // 模式：周期性或单次

                if (_timerId == 0)
                {
                    throw new Exception("无法创建多媒体定时器");
                }

                _isRunning = true;
            }

            /// <summary>
            /// 停止定时器
            /// </summary>
            public void Stop()
            {
                if (!_isRunning) return;

                if (_timerId != 0)
                {
                    timeKillEvent(_timerId);
                    _timerId = 0;
                }

                // 恢复系统定时器精度
                timeEndPeriod(1);
                _isRunning = false;
            }

            private void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
            {
                try
                {
                    _callback?.Invoke();
                }
                catch (Exception ex)
                {
                    // 记录异常，避免异常传播到非托管代码
                    System.Diagnostics.Debug.WriteLine($"定时器回调异常: {ex.Message}");
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // 释放托管资源
                    }

                    Stop();
                    _disposed = true;
                }
            }

            ~MultimediaTimer()
            {
                Dispose(false);
            }
        }

        public class MultiMessageCANScheduler : IDisposable
        {
            private readonly uint timeInterval = 1;
            private readonly MultimediaTimer _timer;
            private readonly object _lockObject = new object(); // 添加锁对象
            private List<Tuple<Action, string, uint>> ActionItems = new List<Tuple<Action, string, uint>>();
            private Dictionary<string, uint> keyValuePairs = new Dictionary<string, uint>();

            public MultiMessageCANScheduler(Action<List<CAN_Data.Message>> batchSendAction, uint timerResolutionMs = 1)
            {
                _timer = new MultimediaTimer(SchedulerCallback);
            }

            public void AddAction(Action action, string Name, uint timeMs)
            {
                lock (_lockObject) // 添加锁
                {
                    try
                    {
                        // 查询
                        var item = ActionItems.FirstOrDefault(x => x.Item2 == Name);
                        if (item != null)
                        {
                            ActionItems.RemoveAll(x => x.Item2 == Name);
                            keyValuePairs.Remove(Name);
                        }
                        // 添加元素
                        ActionItems.Add(Tuple.Create(action, Name, timeMs));
                        keyValuePairs.Add(Name, 0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"添加Action失败: {ex.Message}");
                    }
                }
            }

            public void RemoveAction(string Name)
            {
                lock (_lockObject) // 添加锁
                {
                    try
                    {
                        // 查询
                        var item = ActionItems.FirstOrDefault(x => x.Item2 == Name);
                        if (item != null)
                        {
                            ActionItems.RemoveAll(x => x.Item2 == Name);
                            keyValuePairs.Remove(Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"移除Action失败: {ex.Message}");
                    }
                }
            }

            public void ClearAllActions()
            {
                lock (_lockObject) // 添加锁
                {
                    ActionItems.Clear();
                    keyValuePairs.Clear();
                }
            }

            public int GetActionCount()
            {
                lock (_lockObject) // 添加锁
                {
                    return ActionItems.Count;
                }
            }

            public bool ContainsAction(string Name)
            {
                lock (_lockObject) // 添加锁
                {
                    return ActionItems.Any(x => x.Item2 == Name);
                }
            }

            public void Start()
            {
                _timer.Start(timeInterval);
            }

            public void Stop()
            {
                _timer.Stop();
            }

            private void SchedulerCallback()
            {
                List<Tuple<Action, string>> actionsToExecute = new List<Tuple<Action, string>>();

                // 第一步：收集需要执行的Action（在锁内）
                lock (_lockObject)
                {
                    foreach (var item in ActionItems)
                    {
                        try
                        {
                            keyValuePairs[item.Item2] += timeInterval;
                            if (keyValuePairs[item.Item2] >= item.Item3)
                            {
                                actionsToExecute.Add(Tuple.Create(item.Item1, item.Item2));
                                keyValuePairs[item.Item2] = 0; // 重置计时器
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"更新计时器时出错 {item.Item2}: {ex.Message}");
                        }
                    }
                }

                // 第二步：执行Action（在锁外，避免阻塞其他线程）
                foreach (var actionTuple in actionsToExecute)
                {
                    try
                    {
                        actionTuple.Item1?.Invoke();
                        //Console.WriteLine($"成功执行: {actionTuple.Item2}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"执行 {actionTuple.Item2} 时出错: {ex.Message}");
                    }
                }
            }

            // 获取当前所有Action的状态（用于调试）
            public Dictionary<string, uint> GetCurrentTimers()
            {
                lock (_lockObject)
                {
                    return new Dictionary<string, uint>(keyValuePairs);
                }
            }

            // 手动重置某个Action的计时器
            public void ResetTimer(string Name)
            {
                lock (_lockObject)
                {
                    if (keyValuePairs.ContainsKey(Name))
                    {
                        keyValuePairs[Name] = 0;
                    }
                }
            }

            private void FlushSendBuffer()
            {
                // 如果需要清理缓冲区，可以在这里实现
            }

            public void Dispose()
            {
                Stop();
                ClearAllActions();
                _timer?.Dispose();
            }
        }
    }
}
