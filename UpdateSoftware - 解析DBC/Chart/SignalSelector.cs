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

        public List<SelectedSignalInfo> SelectedSignals
        {
            get { return selectedSignals; }
        }

        public SignalSelector()
        {
            InitializeComponent();
            _searchTimer = new Timer { Interval = 2000 };
            _searchTimer.Tick += SearchTimer_Tick;
        }

        private void SignalSelector_Load(object sender, EventArgs e)
        {
            LoadMessages();
        }

        private void LoadMessages()
        {
            tvMessages.Nodes.Clear();

            if (BaseParamter.dbcHelper == null || BaseParamter.dbcHelper.dbcFile == null)
            {
                MessageBox.Show("DBC文件未加载，请先加载DBC文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
            {
                TreeNode node = new TreeNode();
                node.Text = $"0x{message.messgeId:X3} - {message.messageName}";
                node.Tag = message;
                tvMessages.Nodes.Add(node);
            }

            if (tvMessages.Nodes.Count > 0)
            {
                tvMessages.Nodes[0].Expand();
            }
        }

        private void tvMessages_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is Message message)
            {
                currentMessage = message;
                LoadSignals(message);
            }
        }

        private void LoadSignals(Message message)
        {
            dgvSignals.Rows.Clear();

            if (message == null || message.signals == null)
            {
                return;
            }

            int msgIndex = BaseParamter.dbcHelper.dbcFile.messages.IndexOf(message);
            uint cycleTime = message.cycleTime;

            for (int i = 0; i < message.signals.Count; i++)
            {
                var signal = message.signals[i];
                int rowIndex = dgvSignals.Rows.Add();

                dgvSignals.Rows[rowIndex].Cells[colSignalName.Name].Value = signal.signalName;
                dgvSignals.Rows[rowIndex].Cells[colSignalComment.Name].Value = signal.Comment ?? "";
                dgvSignals.Rows[rowIndex].Cells[colMessageId.Name].Value = $"0x{message.messgeId:X3}";
                dgvSignals.Rows[rowIndex].Cells[colMsgIndex.Name].Value = msgIndex;
                dgvSignals.Rows[rowIndex].Cells[colSignalIndex.Name].Value = i;
                dgvSignals.Rows[rowIndex].Tag = cycleTime;

                bool isSelected = selectedSignals.Any(s => 
                    s.SignalName == signal.signalName && 
                    s.MsgIndex == msgIndex && 
                    s.SignalIndex == i);
                
                dgvSignals.Rows[rowIndex].Cells[colSelect.Name].Value = isSelected;
            }
        }

        private void dgvSignals_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == colSelect.Index)
            {
                bool isSelected = Convert.ToBoolean(dgvSignals.Rows[e.RowIndex].Cells[colSelect.Name].Value);
                string signalName = dgvSignals.Rows[e.RowIndex].Cells[colSignalName.Name].Value?.ToString();
                string signalComment = dgvSignals.Rows[e.RowIndex].Cells[colSignalComment.Name].Value?.ToString();
                int msgIndex = Convert.ToInt32(dgvSignals.Rows[e.RowIndex].Cells[colMsgIndex.Name].Value);
                int signalIndex = Convert.ToInt32(dgvSignals.Rows[e.RowIndex].Cells[colSignalIndex.Name].Value);
                uint cycleTime = dgvSignals.Rows[e.RowIndex].Tag is uint ? (uint)dgvSignals.Rows[e.RowIndex].Tag : 0;

                uint msgId = 0;
                if (msgIndex >= 0 && msgIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
                {
                    msgId = BaseParamter.dbcHelper.dbcFile.messages[msgIndex].messgeId;
                }

                if (isSelected)
                {
                    // 获取枚举值定义和单位
                    Dictionary<double, string> enumDefs = new Dictionary<double, string>();
                    string unit = "";
                    if (msgIndex >= 0 && msgIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
                    {
                        var msg = BaseParamter.dbcHelper.dbcFile.messages[msgIndex];
                        if (signalIndex >= 0 && signalIndex < msg.signals.Count)
                        {
                            var sig = msg.signals[signalIndex];
                            if (sig.enumDefinitions != null)
                            {
                                foreach (var kv in sig.enumDefinitions)
                                {
                                    enumDefs[kv.Key] = kv.Value;
                                }
                            }
                            // 获取单位，过滤掉 "-" 和空字符串
                            string rawUnit = sig.unitStr ?? "";
                            if (rawUnit != "-" && rawUnit != "\"\"" && !string.IsNullOrWhiteSpace(rawUnit))
                            {
                                unit = rawUnit;
                            }
                        }
                    }

                    var signalInfo = new SelectedSignalInfo
                    {
                        SignalName = signalName,
                        SignalComment = signalComment,
                        MsgId = msgId,
                        MsgIndex = msgIndex,
                        SignalIndex = signalIndex,
                        CycleTime = cycleTime,
                        EnumDefinitions = enumDefs,
                        Unit = unit
                    };
                    selectedSignals.Add(signalInfo);
                }
                else
                {
                    selectedSignals.RemoveAll(s => 
                        s.SignalName == signalName && 
                        s.MsgIndex == msgIndex && 
                        s.SignalIndex == signalIndex);
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
                if (currentMessage != null)
                {
                    LoadSignals(currentMessage);
                }
                return;
            }

            dgvSignals.Rows.Clear();
            
            foreach (var message in BaseParamter.dbcHelper.dbcFile.messages)
            {
                int msgIndex = BaseParamter.dbcHelper.dbcFile.messages.IndexOf(message);
                uint cycleTime = message.cycleTime;
                
                for (int i = 0; i < message.signals.Count; i++)
                {
                    var signal = message.signals[i];
                    string signalName = signal.signalName?.ToLower() ?? "";
                    string signalComment = signal.Comment?.ToLower() ?? "";
                    string messageName = message.messageName?.ToLower() ?? "";

                    bool isMatch = signalName.Contains(searchText) || 
                                   signalComment.Contains(searchText) ||
                                   messageName.Contains(searchText);

                    if (isMatch)
                    {
                        int rowIndex = dgvSignals.Rows.Add();
                        dgvSignals.Rows[rowIndex].Cells[colSignalName.Name].Value = signal.signalName;
                        dgvSignals.Rows[rowIndex].Cells[colSignalComment.Name].Value = signal.Comment ?? "";
                        dgvSignals.Rows[rowIndex].Cells[colMessageId.Name].Value = $"0x{message.messgeId:X3}";
                        dgvSignals.Rows[rowIndex].Cells[colMsgIndex.Name].Value = msgIndex;
                        dgvSignals.Rows[rowIndex].Cells[colSignalIndex.Name].Value = i;
                        dgvSignals.Rows[rowIndex].Tag = cycleTime;

                        bool isSelected = selectedSignals.Any(s => 
                            s.SignalName == signal.signalName && 
                            s.MsgIndex == msgIndex && 
                            s.SignalIndex == i);
                        
                        dgvSignals.Rows[rowIndex].Cells[colSelect.Name].Value = isSelected;
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
                string signalName = row.Cells[colSignalName.Name].Value?.ToString();
                if (string.IsNullOrEmpty(signalName))
                    continue;

                int msgIndex = Convert.ToInt32(row.Cells[colMsgIndex.Name].Value);
                int signalIndex = Convert.ToInt32(row.Cells[colSignalIndex.Name].Value);
                bool currentlyChecked = Convert.ToBoolean(row.Cells[colSelect.Name].Value ?? false);

                if (checkAll != currentlyChecked)
                {
                    row.Cells[colSelect.Name].Value = checkAll;

                    if (checkAll)
                    {
                        // 添加
                        if (!selectedSignals.Any(s => s.SignalName == signalName && s.MsgIndex == msgIndex && s.SignalIndex == signalIndex))
                        {
                            uint cycleTime = row.Tag is uint ? (uint)row.Tag : 0;
                            uint msgId = 0;
                            if (msgIndex >= 0 && msgIndex < BaseParamter.dbcHelper.dbcFile.messages.Count)
                                msgId = BaseParamter.dbcHelper.dbcFile.messages[msgIndex].messgeId;

                            var sig = BaseParamter.dbcHelper.dbcFile.messages[msgIndex].signals[signalIndex];
                            Dictionary<double, string> enumDefs = new Dictionary<double, string>();
                            if (sig.enumDefinitions != null)
                            {
                                foreach (var kv in sig.enumDefinitions)
                                    enumDefs[kv.Key] = kv.Value;
                            }
                            string rawUnit = sig.unitStr ?? "";
                            string unit = (rawUnit != "-" && rawUnit != "\"\"" && !string.IsNullOrWhiteSpace(rawUnit)) ? rawUnit : "";

                            selectedSignals.Add(new SelectedSignalInfo
                            {
                                SignalName = signalName,
                                SignalComment = row.Cells[colSignalComment.Name].Value?.ToString(),
                                MsgId = msgId,
                                MsgIndex = msgIndex,
                                SignalIndex = signalIndex,
                                CycleTime = cycleTime,
                                EnumDefinitions = enumDefs,
                                Unit = unit
                            });
                        }
                    }
                    else
                    {
                        selectedSignals.RemoveAll(s => s.SignalName == signalName && s.MsgIndex == msgIndex && s.SignalIndex == signalIndex);
                    }
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
    }
}