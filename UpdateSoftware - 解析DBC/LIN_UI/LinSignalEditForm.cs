using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PCAN_Client.LIN_API;
using PCAN_Client.util;

namespace PCAN_Client.LIN_UI
{
    /// <summary>
    /// 信号编辑对话框：把 LDF 帧解析成信号列表，直接编辑物理值，自动编码回原始字节。
    /// 替代直接改 Hex 原始数据（用户不可读）。
    /// </summary>
    internal class LinSignalEditForm : Form
    {
        private readonly LinLdfFile _ldf;
        private readonly byte _pid;
        private readonly List<RowDef> _rows = new List<RowDef>();
        private DataGridView _dgv;
        private Label _lblHint;

        /// <summary>编码后的帧数据（点确定后有效）</summary>
        public byte[] Data { get; private set; }

        private class RowDef
        {
            public string SignalName;
            public int Offset;
            public int Width;
            public LinSignalDef Sig;
            public double CurrentPhys;
        }

        public LinSignalEditForm(LinLdfFile ldf, byte pid, byte[] data)
        {
            _ldf = ldf;
            _pid = pid;
            Data = data != null ? (byte[])data.Clone() : new byte[0];
            Text = "编辑信号 — " + LinLdfHelper.GetFrameName(ldf, pid) + " (0x" + pid.ToString("X2") + ")";
            Width = 560;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = UiTheme.UiFont;
            UiTheme.StyleForm(this);

            BuildUi();
            BuildRows();
        }

        private void BuildUi()
        {
            _lblHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(8, 6, 0, 0),
                Text = "按 LDF 定义编辑信号物理值，点确定后自动编码为帧原始数据并下发",
                ForeColor = Color.Gray,
            };
            Controls.Add(_lblHint);

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            UiTheme.StyleGrid(_dgv);
            _dgv.Columns.Add("colName", "信号名");
            _dgv.Columns["colName"].Width = 180;
            _dgv.Columns["colName"].ReadOnly = true;
            _dgv.Columns.Add("colBit", "起始位");
            _dgv.Columns["colBit"].Width = 60;
            _dgv.Columns["colBit"].ReadOnly = true;
            _dgv.Columns.Add("colLen", "长度(bit)");
            _dgv.Columns["colLen"].Width = 70;
            _dgv.Columns["colLen"].ReadOnly = true;
            _dgv.Columns.Add("colCur", "当前值");
            _dgv.Columns["colCur"].Width = 100;
            _dgv.Columns["colCur"].ReadOnly = true;
            _dgv.Columns.Add("colNew", "新值 (物理)");
            _dgv.Columns["colNew"].Width = 110;
            Controls.Add(_dgv);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var btnOk = new Button { Text = "确定并下发", Location = new Point(320, 8), Size = new Size(110, 32) };
            btnOk.Click += (s, e) => ApplyAndClose();
            UiTheme.StyleButton(btnOk);
            var btnCancel = new Button { Text = "取消", Location = new Point(440, 8), Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnOk);
            bottom.Controls.Add(btnCancel);
            Controls.Add(bottom);
            CancelButton = btnCancel;
        }

        private void BuildRows()
        {
            List<LinFrameSignal> signals;
            if (_ldf == null || !_ldf.FrameSignals.TryGetValue(_pid, out signals) || signals.Count == 0)
            {
                _lblHint.Text = "该帧在 LDF 中没有信号定义，无法按信号编辑（请直接编辑原始数据）";
                return;
            }
            foreach (var fs in signals)
            {
                var sig = LinLdfHelper.FindSignal(_ldf, _pid, fs.SignalName);
                if (sig == null) continue;
                ulong raw = LinLdfHelper.ReadSignalBits(Data, fs.Offset, sig.Width);
                double phys = LinLdfHelper.RawToPhys(raw, sig);
                _rows.Add(new RowDef { SignalName = fs.SignalName, Offset = fs.Offset, Width = sig.Width, Sig = sig, CurrentPhys = phys });
            }
            foreach (var r in _rows)
            {
                int idx = _dgv.Rows.Add(r.SignalName, r.Offset, r.Width, r.CurrentPhys.ToString("0.####"), r.CurrentPhys.ToString("0.####"));
                _dgv.Rows[idx].Tag = r;
            }
            if (_rows.Count == 0)
                _lblHint.Text = "该帧在 LDF 中没有可编辑的信号定义";
        }

        private void ApplyAndClose()
        {
            // 逐信号读取输入 → 编码写入数据
            for (int i = 0; i < _dgv.Rows.Count && i < _rows.Count; i++)
            {
                var r = _rows[i];
                string txt = (_dgv.Rows[i].Cells["colNew"].Value ?? "").ToString().Trim();
                double phys;
                if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out phys))
                {
                    MessageBox.Show(this, $"信号 [{r.SignalName}] 的输入不是有效数值: {txt}", "编辑信号", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _dgv.Rows[i].Cells["colNew"].Selected = true;
                    return;
                }
                // 越界提示但不阻断（超出量程硬件按原始位发送）
                if (r.Sig != null && (r.Sig.MinValue != 0 || r.Sig.MaxValue != 0) && (phys < r.Sig.MinValue || phys > r.Sig.MaxValue))
                {
                    MessageBox.Show(this, $"信号 [{r.SignalName}] 值 {txt} 超出 LDF 量程 [{r.Sig.MinValue:0.####}, {r.Sig.MaxValue:0.####}]", "编辑信号", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ulong raw = LinLdfHelper.PhysToRaw(phys, r.Sig);
                LinLdfHelper.WriteSignalBits(Data, r.Offset, Math.Min(r.Width, 64), raw);
            }
            DialogResult = DialogResult.OK;
        }
    }
}
