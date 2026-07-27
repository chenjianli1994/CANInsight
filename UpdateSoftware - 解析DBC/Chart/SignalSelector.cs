using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Message = PCAN_Client.CAN_Data.Message;

namespace PCAN_Client
{
    public partial class SignalSelector : Form
    {
        private List<SelectedSignalInfo> selectedSignals = new List<SelectedSignalInfo>();
        private Message currentMessage = null;
        private Timer _searchTimer;
        private string _pendingSearchText = "";
        private bool _suppressUpdateList = false;
        private DbcHelper _customDbcHelper = null;
        private int _busChannelIndex = -1;

        // 多通道模式:各CAN通道的DBC
        private List<CanBusChannel> _busChannels = null;
        // 单选模式:勾选新信号自动取消其他(占位符场景一个占位符只能绑一个信号)
        public bool SingleSelect { get; set; } = false;
        // 当前选中报文的上下文(多通道时记录来自哪个DBC)
        private MsgCtx _currentMsgCtx = null;

        // 报文节点上下文:记录该报文来自哪个DBC/通道
        private class MsgCtx
        {
            public DbcHelper Helper;
            public Message Msg;
            public int MsgIndex;
            public int ChannelIndex;
        }

        // 信号行上下文:记录该信号来自哪个DBC/报文/通道
        private class SigCtx
        {
            public DbcHelper Helper;
            public Message Msg;
            public int MsgIndex;
            public int SignalIndex;
            public int ChannelIndex;
            public string SignalName;
            public string SignalComment;
        }

        public List<SelectedSignalInfo> SelectedSignals
        {
            get { return selectedSignals; }
        }

        private SignalSelector()
        {
            InitializeComponent();
            _searchTimer = new Timer { Interval = 2000 };
            _searchTimer.Tick += SearchTimer_Tick;
        }

        /// <summary>
        /// 使用指定通道DBC实例的信号选择器
        /// </summary>
        /// <param name="customDbcHelper">通道DBC解析实例</param>
        /// <param name="busChannelIndex">所属CAN总线通道索引</param>
        public SignalSelector(DbcHelper customDbcHelper, int busChannelIndex) : this()
        {
            _customDbcHelper = customDbcHelper;
            _busChannelIndex = busChannelIndex;
        }

        /// <summary>
        /// 多通道模式:合并显示各CAN通道DBC的所有报文(占位符选信号覆盖多路CAN)
        /// </summary>
        /// <param name="busChannels">CAN总线通道列表(含各通道DBC)</param>
        public SignalSelector(List<CanBusChannel> busChannels) : this()
        {
            _busChannels = busChannels ?? new List<CanBusChannel>();
        }

        private void SignalSelector_Load(object sender, EventArgs e)
        {
            // 单选模式:隐藏全选框(一个占位符只能绑一个信号)
            if (SingleSelect)
                chkSelectAll.Visible = false;
            LoadMessages();
        }

        private void LoadMessages()
        {
            tvMessages.Nodes.Clear();

            // 收集所有可用DBC:各CAN通道DBC,或调用方明确传入的通道DBC
            var sources = new List<(DbcHelper helper, string label, int channelIndex)>();
            if (_busChannels != null)
            {
                for (int i = 0; i < _busChannels.Count; i++)
                {
                    var bc = _busChannels[i];
                    if (bc != null && bc.IsConfigured && bc.DbcHelper?.dbcFile != null)
                        sources.Add((bc.DbcHelper, bc.Name ?? ("CAN" + i), i));
                }
            }
            if (sources.Count == 0 && _customDbcHelper?.dbcFile != null)
            {
                sources.Add((_customDbcHelper, "DBC", _busChannelIndex));
            }

            if (sources.Count == 0)
            {
                MessageBox.Show("请先在通道配置中为CAN通道加载DBC文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool multi = sources.Count > 1;
            foreach (var src in sources)
            {
                string prefix = multi ? ("[" + src.label + "] ") : "";
                for (int mi = 0; mi < src.helper.dbcFile.messages.Count; mi++)
                {
                    var message = src.helper.dbcFile.messages[mi];
                    var node = new TreeNode
                    {
                        Text = $"{prefix}0x{message.messgeId:X3} - {message.messageName}"
                    };
                    node.Tag = new MsgCtx { Helper = src.helper, Msg = message, MsgIndex = mi, ChannelIndex = src.channelIndex };
                    tvMessages.Nodes.Add(node);
                }
            }

            if (tvMessages.Nodes.Count > 0)
                tvMessages.Nodes[0].Expand();
        }

        private void tvMessages_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is MsgCtx ctx)
            {
                currentMessage = ctx.Msg;
                _currentMsgCtx = ctx;
                LoadSignals(ctx);
            }
        }

        private void LoadSignals(MsgCtx ctx)
        {
            dgvSignals.Rows.Clear();

            if (ctx?.Msg?.signals == null)
                return;

            var helper = ctx.Helper;
            var message = ctx.Msg;

            for (int i = 0; i < message.signals.Count; i++)
            {
                var signal = message.signals[i];
                int rowIndex = dgvSignals.Rows.Add();

                dgvSignals.Rows[rowIndex].Cells[colSignalName.Name].Value = signal.signalName;
                dgvSignals.Rows[rowIndex].Cells[colSignalComment.Name].Value = signal.Comment ?? "";
                dgvSignals.Rows[rowIndex].Cells[colMessageId.Name].Value = $"0x{message.messgeId:X3}";
                dgvSignals.Rows[rowIndex].Cells[colMsgIndex.Name].Value = ctx.MsgIndex;
                dgvSignals.Rows[rowIndex].Cells[colSignalIndex.Name].Value = i;
                // 行Tag存SigCtx(多通道时记录来源DBC/通道)
                dgvSignals.Rows[rowIndex].Tag = new SigCtx
                {
                    Helper = helper,
                    Msg = message,
                    MsgIndex = ctx.MsgIndex,
                    SignalIndex = i,
                    ChannelIndex = ctx.ChannelIndex,
                    SignalName = signal.signalName,
                    SignalComment = signal.Comment ?? ""
                };

                bool isSelected = selectedSignals.Any(s =>
                    s.SignalName == signal.signalName &&
                    s.MsgIndex == ctx.MsgIndex &&
                    s.SignalIndex == i &&
                    s.BusChannelIndex == ctx.ChannelIndex);

                dgvSignals.Rows[rowIndex].Cells[colSelect.Name].Value = isSelected;
            }
        }

        private void dgvSignals_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == colSelect.Index)
            {
                bool isSelected = Convert.ToBoolean(dgvSignals.Rows[e.RowIndex].Cells[colSelect.Name].Value);
                var ctx = dgvSignals.Rows[e.RowIndex].Tag as SigCtx;
                if (ctx == null) return;

                uint msgId = 0;
                var helper = ctx.Helper;
                if (ctx.MsgIndex >= 0 && ctx.MsgIndex < helper.dbcFile.messages.Count)
                    msgId = helper.dbcFile.messages[ctx.MsgIndex].messgeId;
                uint cycleTime = ctx.Msg?.cycleTime ?? 0;

                if (isSelected)
                {
                    // 单选模式:勾选新信号前先清空已选(一个占位符只能绑一个信号)
                    if (SingleSelect && selectedSignals.Count > 0)
                    {
                        selectedSignals.Clear();
                        _suppressUpdateList = true;
                        foreach (DataGridViewRow r in dgvSignals.Rows)
                        {
                            if (r.Index != e.RowIndex)
                                r.Cells[colSelect.Name].Value = false;
                        }
                        _suppressUpdateList = false;
                    }

                    // 获取枚举值定义和单位
                    Dictionary<double, string> enumDefs = new Dictionary<double, string>();
                    string unit = "";
                    if (ctx.SignalIndex >= 0 && ctx.SignalIndex < ctx.Msg.signals.Count)
                    {
                        var sig = ctx.Msg.signals[ctx.SignalIndex];
                        if (sig.enumDefinitions != null)
                        {
                            foreach (var kv in sig.enumDefinitions)
                                enumDefs[kv.Key] = kv.Value;
                        }
                        string rawUnit = sig.unitStr ?? "";
                        if (rawUnit != "-" && rawUnit != "\"\"" && !string.IsNullOrWhiteSpace(rawUnit))
                            unit = rawUnit;
                    }

                    var signalInfo = new SelectedSignalInfo
                    {
                        SignalName = ctx.SignalName,
                        SignalComment = ctx.SignalComment,
                        MsgId = msgId,
                        MsgIndex = ctx.MsgIndex,
                        SignalIndex = ctx.SignalIndex,
                        CycleTime = cycleTime,
                        EnumDefinitions = enumDefs,
                        Unit = unit,
                        BusChannelIndex = ctx.ChannelIndex
                    };
                    selectedSignals.Add(signalInfo);
                }
                else
                {
                    selectedSignals.RemoveAll(s =>
                        s.SignalName == ctx.SignalName &&
                        s.MsgIndex == ctx.MsgIndex &&
                        s.SignalIndex == ctx.SignalIndex &&
                        s.BusChannelIndex == ctx.ChannelIndex);
                }

                if (!_suppressUpdateList)
                    UpdateSelectedList();
            }
        }

        private void dgvSignals_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvSignals.IsCurrentCellDirty)
            {
                dgvSignals.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void UpdateSelectedList()
        {
            lstSelected.Items.Clear();
            foreach (var signal in selectedSignals)
            {
                lstSelected.Items.Add($"{signal.SignalName} ({signal.SignalComment})");
            }
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            _pendingSearchText = txtSearch.Text;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                _searchTimer.Stop();
                PerformSearch(txtSearch.Text);
            }
        }

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            _searchTimer.Stop();
            PerformSearch(_pendingSearchText);
        }

        private void PerformSearch(string searchText)
        {
            searchText = searchText.ToLower().Trim();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                if (_currentMsgCtx != null)
                    LoadSignals(_currentMsgCtx);
                return;
            }

            dgvSignals.Rows.Clear();

            // 收集所有DBC源(与LoadMessages一致:各CAN通道DBC,或调用方传入的通道DBC)
            var sources = new List<(DbcHelper helper, int channelIndex, string label)>();
            if (_busChannels != null)
            {
                for (int i = 0; i < _busChannels.Count; i++)
                {
                    var bc = _busChannels[i];
                    if (bc != null && bc.IsConfigured && bc.DbcHelper?.dbcFile != null)
                        sources.Add((bc.DbcHelper, i, bc.Name ?? ("CAN" + i)));
                }
            }
            if (sources.Count == 0 && _customDbcHelper?.dbcFile != null)
            {
                sources.Add((_customDbcHelper, _busChannelIndex, "DBC"));
            }

            bool multi = sources.Count > 1;

            foreach (var src in sources)
            {
                var helper = src.helper;
                for (int mi = 0; mi < helper.dbcFile.messages.Count; mi++)
                {
                    var message = helper.dbcFile.messages[mi];
                    for (int i = 0; i < message.signals.Count; i++)
                    {
                        var signal = message.signals[i];
                        string signalNameLower = signal.signalName?.ToLower() ?? "";
                        string signalCommentLower = signal.Comment?.ToLower() ?? "";
                        string messageNameLower = message.messageName?.ToLower() ?? "";

                        bool isMatch = signalNameLower.Contains(searchText) ||
                                       signalCommentLower.Contains(searchText) ||
                                       messageNameLower.Contains(searchText);

                        if (isMatch)
                        {
                            string prefix = multi ? ("[" + src.label + "] ") : "";
                            int rowIndex = dgvSignals.Rows.Add();
                            dgvSignals.Rows[rowIndex].Cells[colSignalName.Name].Value = prefix + signal.signalName;
                            dgvSignals.Rows[rowIndex].Cells[colSignalComment.Name].Value = signal.Comment ?? "";
                            dgvSignals.Rows[rowIndex].Cells[colMessageId.Name].Value = $"0x{message.messgeId:X3}";
                            dgvSignals.Rows[rowIndex].Cells[colMsgIndex.Name].Value = mi;
                            dgvSignals.Rows[rowIndex].Cells[colSignalIndex.Name].Value = i;
                            dgvSignals.Rows[rowIndex].Tag = new SigCtx
                            {
                                Helper = helper,
                                Msg = message,
                                MsgIndex = mi,
                                SignalIndex = i,
                                ChannelIndex = src.channelIndex,
                                SignalName = signal.signalName,
                                SignalComment = signal.Comment ?? ""
                            };

                            bool isSelected = selectedSignals.Any(s =>
                                s.SignalName == signal.signalName &&
                                s.MsgIndex == mi &&
                                s.SignalIndex == i &&
                                s.BusChannelIndex == src.channelIndex);

                            dgvSignals.Rows[rowIndex].Cells[colSelect.Name].Value = isSelected;
                        }
                    }
                }
            }
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            if (selectedSignals.Count == 0)
            {
                MessageBox.Show("请至少选择一个信号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void chkSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            if (currentMessage == null || currentMessage.signals == null)
                return;

            bool checkAll = chkSelectAll.Checked;
            _suppressUpdateList = true;

            for (int i = 0; i < dgvSignals.Rows.Count; i++)
            {
                var row = dgvSignals.Rows[i];
                var ctx = row.Tag as SigCtx;
                if (ctx == null) continue;

                bool currentlyChecked = Convert.ToBoolean(row.Cells[colSelect.Name].Value ?? false);
                if (checkAll != currentlyChecked)
                {
                    // 设值触发CellValueChanged(基于row.Tag的SigCtx处理selectedSignals增减)
                    row.Cells[colSelect.Name].Value = checkAll;
                }
            }

            _suppressUpdateList = false;
            UpdateSelectedList();
        }

        // 在CellMouseDown中保存多选行索引（此时DataGridView尚未改变选择状态）
        private List<int> _batchSelectedRowIndices = new List<int>();
        // 标记本次点击是否为双击，BeginInvoke回调中跳过批量处理
        private bool _skipBatch = false;

        private void dgvSignals_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == colSelect.Index)
            {
                _batchSelectedRowIndices.Clear();
                if (dgvSignals.SelectedRows.Count > 1)
                {
                    foreach (DataGridViewRow row in dgvSignals.SelectedRows)
                        _batchSelectedRowIndices.Add(row.Index);
                }
            }
        }

        private void dgvSignals_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colSelect.Index)
                return;

            // 多选行批量勾选/取消勾选（不需要按Ctrl）
            if (_batchSelectedRowIndices.Count > 1)
            {
                _skipBatch = false;

                // 使用BeginInvoke延迟执行，等DataGridView完成当前点击行的自动切换后再同步其他行
                this.BeginInvoke(new Action(() =>
                {
                    if (_skipBatch)
                    {
                        // 双击时取消批量处理，只保留双击的切换结果
                        _skipBatch = false;
                        return;
                    }

                    // 此时DataGridView已自动切换了当前点击行的勾选状态
                    // 读取当前点击行的值，将其他选中行同步为该值
                    DataGridViewRow clickedRow = dgvSignals.Rows[e.RowIndex];
                    bool targetValue = Convert.ToBoolean(clickedRow.Cells[colSelect.Name].Value ?? false);

                    _suppressUpdateList = true;

                    foreach (int rowIndex in _batchSelectedRowIndices)
                    {
                        if (rowIndex == e.RowIndex)
                            continue; // 跳过点击行（已由DataGridView处理）

                        DataGridViewRow row = dgvSignals.Rows[rowIndex];
                        string signalName = row.Cells[colSignalName.Name].Value?.ToString();
                        if (string.IsNullOrEmpty(signalName)) continue;

                        bool currentlyChecked = Convert.ToBoolean(row.Cells[colSelect.Name].Value ?? false);
                        if (currentlyChecked != targetValue)
                        {
                            row.Cells[colSelect.Name].Value = targetValue;
                            // CellValueChanged事件会自动处理selectedSignals的增减
                        }
                    }

                    _suppressUpdateList = false;
                    UpdateSelectedList();
                    _batchSelectedRowIndices.Clear();
                }));
            }
        }

        private void dgvSignals_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            _skipBatch = true;

            // 双击行切换勾选状态
            bool isSelected = Convert.ToBoolean(dgvSignals.Rows[e.RowIndex].Cells[colSelect.Name].Value ?? false);
            dgvSignals.Rows[e.RowIndex].Cells[colSelect.Name].Value = !isSelected;
        }
    }

    public class SelectedSignalInfo
    {
        public string SignalName { get; set; }
        public string SignalComment { get; set; }
        public uint MsgId { get; set; }
        public int MsgIndex { get; set; }
        public int SignalIndex { get; set; }
        public uint CycleTime { get; set; }
        // 新增：枚举值定义（原始值 -> 描述）
        public Dictionary<double, string> EnumDefinitions { get; set; } = new Dictionary<double, string>();
        // 新增：信号单位
        public string Unit { get; set; } = "";
        /// <summary>
        /// 所属CAN总线通道索引（-1 = 未指定，添加时按 CAN ID+信号名 重定位到通道DBC）
        /// </summary>
        public int BusChannelIndex { get; set; } = -1;
    }
}