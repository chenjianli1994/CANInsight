using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// 连接阶段通道映射对话框：列出识别到的硬件通道，
    /// 用户把每个硬件通道指定给"通道配置"中的逻辑CAN通道（或不连接）。
    /// 确认后映射写回 CanBusChannel.HwChannel（255=不连接）并持久化。
    /// </summary>
    public class ChannelMappingDialog : Form
    {
        /// <summary>不连接哨兵值（写入 HwChannel 表示该逻辑通道本次不连接）</summary>
        public const byte NotConnect = 255;

        public class HwChannelInfo
        {
            public byte Hw;           // 硬件通道号（PCAN USBBUS序号 / CANoe通道号）
            public string Name;       // 显示名，如 "USB_1"、"CAN 1"
            public string Status;     // 状态，如 "空闲"、"已占用"
        }

        private readonly DataGridView _dgv;
        private readonly List<HwChannelInfo> _hwChannels;
        private readonly List<CanBusChannel> _logicChannels;
        private readonly List<string> _logicNames; // ["不连接", "CAN1", "CAN2", ...]

        public ChannelMappingDialog(string deviceTitle, List<HwChannelInfo> hwChannels, List<CanBusChannel> logicChannels)
        {
            _hwChannels = hwChannels;
            _logicChannels = logicChannels;
            // 下拉只显示逻辑通道名（CAN1/CAN2...），逻辑序号是内部实现细节不暴露
            _logicNames = new List<string> { "不连接" };
            _logicNames.AddRange(logicChannels.Select(c => c.Name));

            this.Text = $"{deviceTitle} 通道映射";
            this.Size = new Size(560, 120 + Math.Max(3, hwChannels.Count) * 32 + 70);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var lbl = new Label
            {
                Text = "识别到以下硬件通道，请为每个硬件通道选择要连接的逻辑CAN通道（在绘图区\"通道配置\"中维护）：",
                Location = new Point(12, 10),
                Size = new Size(520, 34),
                Font = new Font("Microsoft YaHei", 9F)
            };
            this.Controls.Add(lbl);

            _dgv = new DataGridView
            {
                Location = new Point(12, 48),
                Size = new Size(520, Math.Max(3, hwChannels.Count) * 32 + 30),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "hw", HeaderText = "硬件通道", ReadOnly = true, FillWeight = 30 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "状态", ReadOnly = true, FillWeight = 25 });
            _dgv.Columns.Add(new DataGridViewComboBoxColumn { Name = "map", HeaderText = "映射到逻辑通道", FillWeight = 45 });
            this.Controls.Add(_dgv);

            foreach (var hw in hwChannels)
            {
                int rowIdx = _dgv.Rows.Add(hw.Name, hw.Status, null);
                var cell = (DataGridViewComboBoxCell)_dgv.Rows[rowIdx].Cells[2];
                cell.DataSource = new List<string>(_logicNames);
                cell.Value = _logicNames[PreselectLogic(hw.Hw)];
            }

            var btnOk = new Button
            {
                Text = "连接",
                Location = new Point(340, _dgv.Bottom + 12),
                Size = new Size(90, 30),
                DialogResult = DialogResult.None
            };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(442, _dgv.Bottom + 12),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            this.CancelButton = btnCancel;
        }

        /// <summary>按当前配置预选：该硬件通道已绑定到某逻辑通道则预选之，否则按同号默认映射（逻辑序号=索引+1）</summary>
        private int PreselectLogic(byte hw)
        {
            // 精确绑定（HwChannel>0 且 ≠255）
            for (int i = 0; i < _logicChannels.Count; i++)
            {
                if (_logicChannels[i].HwChannel == hw) return i + 1;
            }
            // "不连接"哨兵
            for (int i = 0; i < _logicChannels.Count; i++)
            {
                if (_logicChannels[i].HwChannel == NotConnect && (i + 1) == hw) return 0;
            }
            // 默认同号映射：硬件通道n → 逻辑通道CHn（HwChannel=0 表示跟随逻辑序号）
            for (int i = 0; i < _logicChannels.Count; i++)
            {
                if (_logicChannels[i].HwChannel == 0 && (i + 1) == hw) return i + 1;
            }
            return 0; // 不连接
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            _dgv.EndEdit();

            // 收集映射：逻辑通道 → 硬件通道号（0=不连接，用哨兵255表示）
            var hwOfLogic = new byte[_logicChannels.Count];
            for (int i = 0; i < hwOfLogic.Length; i++) hwOfLogic[i] = NotConnect;

            for (int r = 0; r < _hwChannels.Count; r++)
            {
                string sel = _dgv.Rows[r].Cells[2].Value?.ToString() ?? _logicNames[0];
                int logicIdx = _logicNames.IndexOf(sel) - 1; // -1=不连接
                if (logicIdx < 0) continue;
                if (hwOfLogic[logicIdx] != NotConnect)
                {
                    MessageBox.Show($"逻辑通道 [{_logicChannels[logicIdx].Name}] 被多个硬件通道选中，请调整为一一对应。",
                        "映射冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                hwOfLogic[logicIdx] = _hwChannels[r].Hw;
            }

            // 写回配置：未连接的逻辑通道用哨兵（区别于0=默认跟随）
            for (int i = 0; i < _logicChannels.Count; i++)
            {
                _logicChannels[i].HwChannel = hwOfLogic[i];
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
