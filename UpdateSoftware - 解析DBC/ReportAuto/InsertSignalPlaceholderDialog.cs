// 插入信号占位符对话框:选信号→勾选指标→生成占位符名→返回绑定列表
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PCAN_Client.ReportAuto
{
    /// <summary>
    /// 插入信号占位符对话框：选择一个DBC信号，勾选需要的统计指标（可多选），
    /// 自动生成占位符名（可编辑），确定后返回"占位符→信号+指标"绑定列表，
    /// 由 AnalysisTypeEditor 插入到文字光标处并写入占位符配置表。
    /// 一次操作完成"选信号+选指标+生成占位符+插入文字+建立绑定"。
    /// </summary>
    internal class InsertSignalPlaceholderDialog : Form
    {
        // 当前选中的信号信息
        private string _signalName = "";
        private int _messageId = 0;
        private string _unit = "";
        // CAN总线通道列表(供信号选择器显示多路CAN通道DBC)
        private readonly List<CanBusChannel> _busChannels;

        // UI 控件
        private Label _lblSignalName;
        private Label _lblUnit;

        // 4个指标项:显示文本 + metric键 + 复选框 + 占位符名输入框
        private class MetricItem
        {
            public string Display;   // 如"平均值(avg)"
            public string Metric;    // 如"avg"
            public CheckBox Cb;
            public TextBox Txt;
        }
        private readonly List<MetricItem> _metrics = new List<MetricItem>();

        /// <summary>确定后返回的绑定列表(占位符名→信号+指标)</summary>
        public List<InsertedBinding> Result { get; private set; } = new List<InsertedBinding>();

        public InsertSignalPlaceholderDialog(List<CanBusChannel> busChannels)
        {
            _busChannels = busChannels ?? new List<CanBusChannel>();
            InitUI();
        }

        private void InitUI()
        {
            Text = "插入信号占位符";
            Size = new Size(490, 350);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 15;

            // 信号行
            Controls.Add(new Label { Text = "信号:", Location = new Point(15, y + 3), AutoSize = true });
            var btnSelect = new Button
            {
                Text = "选择信号...",
                Location = new Point(60, y),
                Size = new Size(110, 28)
            };
            btnSelect.Click += BtnSelect_Click;
            Controls.Add(btnSelect);
            _lblSignalName = new Label
            {
                Location = new Point(180, y + 3),
                AutoSize = true,
                ForeColor = Color.Blue
            };
            Controls.Add(_lblSignalName);
            y += 35;

            // 单位行
            Controls.Add(new Label { Text = "单位:", Location = new Point(15, y + 3), AutoSize = true });
            _lblUnit = new Label { Location = new Point(60, y + 3), AutoSize = true };
            Controls.Add(_lblUnit);
            y += 35;

            // 指标组标题
            Controls.Add(new Label
            {
                Text = "勾选指标(可多选,占位符名可编辑):",
                Location = new Point(15, y + 3),
                AutoSize = true
            });
            y += 28;

            // 4个指标行
            string[][] defs =
            {
                new[] { "平均值(avg)", "avg" },
                new[] { "最小值(min)", "min" },
                new[] { "最大值(max)", "max" },
                new[] { "极差(range)", "range" },
            };
            foreach (var d in defs)
            {
                var item = new MetricItem { Display = d[0], Metric = d[1] };
                item.Cb = new CheckBox
                {
                    Text = d[0],
                    Location = new Point(25, y),
                    AutoSize = true
                };
                // 勾选变化时启用/禁用对应占位符名输入框
                item.Cb.CheckedChanged += (s, e) => item.Txt.Enabled = item.Cb.Checked;
                Controls.Add(item.Cb);

                Controls.Add(new Label { Text = "→", Location = new Point(180, y + 3), AutoSize = true });

                item.Txt = new TextBox
                {
                    Location = new Point(205, y),
                    Width = 250,
                    Enabled = false
                };
                Controls.Add(item.Txt);

                _metrics.Add(item);
                y += 30;
            }
            // 默认勾选平均值
            _metrics[0].Cb.Checked = true;

            y += 8;
            // 确定插入/取消按钮
            var btnOk = new Button
            {
                Text = "确定插入",
                Location = new Point(265, y),
                Size = new Size(95, 30)
            };
            btnOk.Click += BtnOk_Click;
            Controls.Add(btnOk);
            var btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(370, y),
                Size = new Size(95, 30)
            };
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        /// <summary>点击"选择信号...",弹出SignalSelector选择一个信号</summary>
        private void BtnSelect_Click(object sender, EventArgs e)
        {
            using (var selector = new SignalSelector(_busChannels) { SingleSelect = true })
            {
                if (selector.ShowDialog(this) == DialogResult.OK && selector.SelectedSignals.Count > 0)
                {
                    var sig = selector.SelectedSignals[0]; // 只取第一个
                    _signalName = sig.SignalName ?? "";
                    _messageId = (int)sig.MsgId;
                    _unit = sig.Unit ?? "";
                    _lblSignalName.Text = _signalName;
                    _lblUnit.Text = _unit;
                    // 为每个指标预填占位符名(基于信号名+指标)
                    foreach (var m in _metrics)
                    {
                        m.Txt.Text = AnalysisTypeEditor.GeneratePlaceholderName(_signalName, m.Display);
                    }
                }
            }
        }

        /// <summary>点击确定:收集勾选的指标,生成绑定列表</summary>
        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_signalName))
            {
                MessageBox.Show("请先选择信号", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            Result.Clear();
            foreach (var m in _metrics)
            {
                if (!m.Cb.Checked) continue;
                string key = m.Txt.Text.Trim();
                if (string.IsNullOrEmpty(key))
                    key = AnalysisTypeEditor.GeneratePlaceholderName(_signalName, m.Display);
                Result.Add(new InsertedBinding
                {
                    Key = key,
                    SignalName = _signalName,
                    MessageId = _messageId,
                    Unit = _unit,
                    Metric = m.Metric
                });
            }
            if (Result.Count == 0)
            {
                MessageBox.Show("请至少勾选一个指标", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>插入占位符对话框返回的单条绑定:占位符名→信号+指标</summary>
    internal class InsertedBinding
    {
        public string Key;          // 占位符名(不含{{}})
        public string SignalName;   // DBC信号名
        public int MessageId;       // DBC报文ID
        public string Unit;         // 单位
        public string Metric;       // "avg"/"min"/"max"/"range"
    }
}
