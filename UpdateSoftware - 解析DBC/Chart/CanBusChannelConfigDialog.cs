using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// CAN总线解析通道配置对话框
    /// 支持添加/删除/编辑多个CAN解析通道，每个通道可独立配置DBC文件和BLF通道映射
    /// </summary>
    public class CanBusChannelConfigDialog : Form
    {
        private DataGridView _dgvChannels;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnOk;
        private Button _btnCancel;
        private Label _lblTitle;

        private List<CanBusChannel> _channels = new List<CanBusChannel>();

        // DataGridView列定义
        private const string ColName = "colName";
        private const string ColBlfChannel = "colBlfChannel";
        private const string ColHwChannel = "colHwChannel";
        private const string ColDbcPath = "colDbcPath";
        private const string ColBrowse = "colBrowse";

        /// <summary>
        /// 配置结果：CAN总线通道列表
        /// </summary>
        public List<CanBusChannel> Channels
        {
            get { return _channels; }
        }

        public CanBusChannelConfigDialog(List<CanBusChannel> existingChannels = null)
        {
            InitializeComponents();
            if (existingChannels != null)
            {
                _channels = new List<CanBusChannel>(existingChannels);
                LoadChannelsToGrid();
            }
        }

        private void InitializeComponents()
        {
            this.Text = "CAN通道配置";
            this.Size = new Size(790, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 标题标签
            _lblTitle = new Label
            {
                Text = "配置CAN解析通道（每路CAN总线对应一个通道，可配置独立的DBC文件；硬件通道=PCAN USBBUS序号/CANoe通道号，与BLF通道号一致时留0）",
                Location = new Point(12, 12),
                Size = new Size(750, 23),
                Font = new Font("Microsoft YaHei", 9F)
            };
            this.Controls.Add(_lblTitle);

            // DataGridView
            _dgvChannels = new DataGridView
            {
                Location = new Point(12, 40),
                Size = new Size(750, 360),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false
            };

            // 列：通道名称
            var colName = new DataGridViewTextBoxColumn
            {
                Name = ColName,
                HeaderText = "通道名称",
                FillWeight = 20,
                MaxInputLength = 20
            };
            _dgvChannels.Columns.Add(colName);

            // 列：BLF通道号
            var colBlfChannel = new DataGridViewTextBoxColumn
            {
                Name = ColBlfChannel,
                HeaderText = "BLF通道号",
                FillWeight = 12,
                MaxInputLength = 3
            };
            _dgvChannels.Columns.Add(colBlfChannel);

            // 列：硬件通道号（实时收发映射：PCAN USBBUS序号/CANoe通道号，0=跟随BLF通道号）
            var colHwChannel = new DataGridViewTextBoxColumn
            {
                Name = ColHwChannel,
                HeaderText = "硬件通道",
                FillWeight = 12,
                MaxInputLength = 3
            };
            _dgvChannels.Columns.Add(colHwChannel);

            // 列：DBC文件路径
            var colDbcPath = new DataGridViewTextBoxColumn
            {
                Name = ColDbcPath,
                HeaderText = "DBC文件路径",
                FillWeight = 46
            };
            _dgvChannels.Columns.Add(colDbcPath);

            // 列：浏览按钮
            var colBrowse = new DataGridViewButtonColumn
            {
                Name = ColBrowse,
                HeaderText = "浏览",
                FillWeight = 10,
                Text = "浏览...",
                UseColumnTextForButtonValue = true
            };
            _dgvChannels.Columns.Add(colBrowse);

            _dgvChannels.CellClick += DgvChannels_CellClick;
            _dgvChannels.CellValidating += DgvChannels_CellValidating;
            this.Controls.Add(_dgvChannels);

            // 按钮面板
            var btnPanel = new Panel
            {
                Location = new Point(12, 410),
                Size = new Size(750, 40)
            };
            this.Controls.Add(btnPanel);

            _btnAdd = new Button
            {
                Text = "添加通道",
                Location = new Point(0, 5),
                Size = new Size(90, 30)
            };
            _btnAdd.Click += BtnAdd_Click;
            btnPanel.Controls.Add(_btnAdd);

            _btnRemove = new Button
            {
                Text = "删除通道",
                Location = new Point(100, 5),
                Size = new Size(90, 30)
            };
            _btnRemove.Click += BtnRemove_Click;
            btnPanel.Controls.Add(_btnRemove);

            _btnOk = new Button
            {
                Text = "确定",
                Location = new Point(560, 5),
                Size = new Size(80, 30),
                DialogResult = DialogResult.None
            };
            _btnOk.Click += BtnOk_Click;
            btnPanel.Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(650, 5),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };
            btnPanel.Controls.Add(_btnCancel);

            this.CancelButton = _btnCancel;
        }

        private void LoadChannelsToGrid()
        {
            _dgvChannels.Rows.Clear();
            foreach (var ch in _channels)
            {
                int rowIndex = _dgvChannels.Rows.Add();
                _dgvChannels.Rows[rowIndex].Cells[ColName].Value = ch.Name;
                _dgvChannels.Rows[rowIndex].Cells[ColBlfChannel].Value = ch.BlfChannelId.ToString();
                _dgvChannels.Rows[rowIndex].Cells[ColHwChannel].Value = ch.HwChannel.ToString();
                _dgvChannels.Rows[rowIndex].Cells[ColDbcPath].Value = ch.DbcFilePath;
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            int rowIndex = _dgvChannels.Rows.Add();
            _dgvChannels.Rows[rowIndex].Cells[ColName].Value = $"CAN{_dgvChannels.Rows.Count}";
            _dgvChannels.Rows[rowIndex].Cells[ColBlfChannel].Value = "0";
            _dgvChannels.Rows[rowIndex].Cells[ColHwChannel].Value = "0"; // 0=跟随BLF通道号
            _dgvChannels.Rows[rowIndex].Cells[ColDbcPath].Value = "";
            _dgvChannels.Rows[rowIndex].Cells[ColBrowse].Value = "浏览...";
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (_dgvChannels.SelectedRows.Count > 0)
            {
                int rowIndex = _dgvChannels.SelectedRows[0].Index;
                _dgvChannels.Rows.RemoveAt(rowIndex);
            }
            else
            {
                MessageBox.Show("请先选择要删除的通道", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void DgvChannels_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            // 点击"浏览"按钮列
            if (e.ColumnIndex == _dgvChannels.Columns[ColBrowse].Index)
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Filter = "DBC文件 (*.dbc)|*.dbc|所有文件 (*.*)|*.*";
                    ofd.Title = "选择DBC文件";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        _dgvChannels.Rows[e.RowIndex].Cells[ColDbcPath].Value = ofd.FileName;
                    }
                }
            }
        }

        private void DgvChannels_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            // 验证BLF通道号/硬件通道号必须是数字
            if (e.ColumnIndex == _dgvChannels.Columns[ColBlfChannel].Index)
            {
                string value = e.FormattedValue?.ToString() ?? "";
                if (!byte.TryParse(value, out byte result))
                {
                    MessageBox.Show("BLF通道号必须是0-255之间的数字", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            }
            else if (e.ColumnIndex == _dgvChannels.Columns[ColHwChannel].Index)
            {
                string value = e.FormattedValue?.ToString() ?? "";
                if (!byte.TryParse(value, out byte result))
                {
                    MessageBox.Show("硬件通道必须是0-255之间的数字（0=跟随BLF通道号）", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            // 验证并收集数据
            _channels.Clear();

            for (int i = 0; i < _dgvChannels.Rows.Count; i++)
            {
                var row = _dgvChannels.Rows[i];
                string name = row.Cells[ColName].Value?.ToString() ?? "";
                string blfChannelStr = row.Cells[ColBlfChannel].Value?.ToString() ?? "";
                string hwChannelStr = row.Cells[ColHwChannel].Value?.ToString() ?? "";
                string dbcPath = row.Cells[ColDbcPath].Value?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show($"第{i + 1}行：通道名称不能为空", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!byte.TryParse(blfChannelStr, out byte blfChannelId))
                {
                    MessageBox.Show($"第{i + 1}行：BLF通道号格式错误", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!byte.TryParse(hwChannelStr, out byte hwChannel))
                {
                    MessageBox.Show($"第{i + 1}行：硬件通道格式错误（0=跟随BLF通道号）", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var channel = new CanBusChannel(name, blfChannelId, dbcPath);
                channel.HwChannel = hwChannel;

                // 如果配置了DBC路径，尝试加载
                if (!string.IsNullOrWhiteSpace(dbcPath))
                {
                    try
                    {
                        channel.DbcHelper = new CAN_Data.DbcHelper();
                        channel.DbcHelper.Parse(dbcPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"第{i + 1}行：DBC文件加载失败\n{ex.Message}", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                _channels.Add(channel);
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
