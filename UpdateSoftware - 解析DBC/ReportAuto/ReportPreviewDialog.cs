// 报告预览对话框:16:9页面示意图卡片列表,支持拖动调整顺序/删除单页
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PCAN_Client.ReportAuto
{
    /// <summary>
    /// 报告预览对话框。以16:9示意图卡片展示当前报告页(标题=工况名),
    /// 支持拖动卡片调整页顺序、点击右上角×删除页,操作立即生效到当前报告。
    /// </summary>
    internal class ReportPreviewDialog : Form
    {
        private const int CardW = 240;   // 卡片宽(16:9)
        private const int CardH = 135;   // 卡片高
        private const int TitleH = 26;   // 标题栏高

        private FlowLayoutPanel _flow;
        private Label _lblCount;
        private int _insertIndex = -1;   // 拖动悬停时的插入位(-1=无)

        public ReportPreviewDialog()
        {
            InitUI();
            BuildCards();
        }

        private void InitUI()
        {
            Text = "报告预览";
            Size = new Size(860, 600);
            MinimumSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(8);

            var lblTip = new Label
            {
                Text = "拖动卡片调整页面顺序，点击卡片右上角 × 删除页面。操作立即生效到当前报告。",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(80, 80, 80),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                AllowDrop = true,
                BackColor = Color.FromArgb(240, 244, 250),
                Padding = new Padding(6)
            };
            _flow.DragEnter += Flow_DragEnterOver;
            _flow.DragOver += Flow_DragEnterOver;
            _flow.DragLeave += (s, e) => { _insertIndex = -1; _flow.Invalidate(); };
            _flow.DragDrop += Flow_DragDrop;
            _flow.Paint += Flow_Paint;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
            _lblCount = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            var btnClose = new Button
            {
                Text = "关闭",
                Dock = DockStyle.Right,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(_lblCount);
            bottom.Controls.Add(btnClose);

            // 添加顺序:Fill先,Bottom其次,Top最后(后添加的先布局)
            Controls.Add(_flow);
            Controls.Add(bottom);
            Controls.Add(lblTip);
        }

        /// <summary>按当前报告页列表重建全部卡片</summary>
        private void BuildCards()
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            var pages = ReportAutoService.Pages;
            for (int i = 0; i < pages.Count; i++)
                _flow.Controls.Add(MakeCard(i, pages[i]));
            _flow.ResumeLayout();
            _lblCount.Text = $"共 {pages.Count} 页";
        }

        /// <summary>创建单页示意图卡片:蓝色标题栏(工况名)+正文线框示意+时间范围+删除按钮</summary>
        private Control MakeCard(int index, ReportPageInfo info)
        {
            var card = new Panel
            {
                Size = new Size(CardW, CardH),
                Margin = new Padding(10),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = index
            };

            // 页面示意图主体(Fill,先添加后布局)
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Tag = index };
            body.Paint += PaintPageMock;

            // 底部时间范围
            var timeLbl = new Label
            {
                Text = $"{info.TimeStart:0.##}s ~ {info.TimeEnd:0.##}s",
                Dock = DockStyle.Bottom,
                Height = 16,
                Font = new Font("Microsoft YaHei UI", 7.5F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = index
            };

            // 标题栏:蓝色底+页码+工况名
            var title = new Panel { Dock = DockStyle.Top, Height = TitleH, BackColor = Color.FromArgb(70, 130, 200), Tag = index };
            var titleLbl = new Label
            {
                Text = $"  {index + 1}. {info.Name}",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = index
            };
            var btnX = new Label
            {
                Text = "×",
                Dock = DockStyle.Right,
                Width = 24,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Tag = index
            };
            btnX.Click += (s, e) => DeleteCard((int)((Label)s).Tag);
            title.Controls.Add(titleLbl);  // Fill 先添加
            title.Controls.Add(btnX);      // Right 后添加先布局

            card.Controls.Add(body);
            card.Controls.Add(timeLbl);
            card.Controls.Add(title);

            // 卡片及其子控件(除×按钮)按下左键均可发起拖动
            MouseEventHandler startDrag = (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    card.DoDragDrop(new DataObject("ReportPageCard", (int)((Control)s).Tag), DragDropEffects.Move);
            };
            card.MouseDown += startDrag;
            title.MouseDown += startDrag;
            titleLbl.MouseDown += startDrag;
            body.MouseDown += startDrag;
            timeLbl.MouseDown += startDrag;

            return card;
        }

        /// <summary>绘制页面内容示意:左侧文本线+右侧图表框折线(固定种子,形状稳定)</summary>
        private void PaintPageMock(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = (Panel)sender;
            int w = p.Width, h = p.Height;

            // 左侧文本示意线
            using (var brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            {
                int lw = (int)(w * 0.40);
                for (int i = 0; i < 4; i++)
                {
                    int y = 10 + i * 12;
                    if (y + 5 > h - 4) break;
                    g.FillRectangle(brush, 8, y, lw - (i == 3 ? 20 : 0), 5);
                }
            }

            // 右侧图表示意框+折线
            int cx = (int)(w * 0.50), cw = w - cx - 10, ch = h - 16;
            if (cw > 10 && ch > 10)
            {
                using (var pen = new Pen(Color.LightSteelBlue))
                    g.DrawRectangle(pen, cx, 8, cw, ch);
                using (var pen = new Pen(Color.FromArgb(91, 155, 213), 2))
                {
                    var rnd = new Random(42);
                    var prev = new Point(cx + 4, 8 + ch / 2);
                    for (int i = 1; i <= 8; i++)
                    {
                        var next = new Point(cx + 4 + (cw - 8) * i / 8, 8 + 4 + rnd.Next(ch - 8));
                        g.DrawLine(pen, prev, next);
                        prev = next;
                    }
                }
            }
        }

        /// <summary>删除指定页(确认后生效)</summary>
        private void DeleteCard(int index)
        {
            var pages = ReportAutoService.Pages;
            if (index < 0 || index >= pages.Count) return;
            if (MessageBox.Show($"确定删除第 {index + 1} 页（{pages[index].Name}）吗？",
                "删除页面", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            ReportAutoService.DeletePage(index);
            BuildCards();
        }

        /// <summary>拖入/悬停:显示插入位置指示线</summary>
        private void Flow_DragEnterOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ReportPageCard"))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            Point pt = _flow.PointToClient(new Point(e.X, e.Y));
            int idx = GetInsertIndex(pt);
            if (idx != _insertIndex)
            {
                _insertIndex = idx;
                _flow.Invalidate();
            }
        }

        /// <summary>放下:把拖动页移动到插入位并刷新</summary>
        private void Flow_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ReportPageCard") && _insertIndex >= 0)
            {
                int from = (int)e.Data.GetData("ReportPageCard");
                int to = _insertIndex;
                if (to > from) to--;  // 先移除拖动项,插入位前移
                int maxTo = ReportAutoService.Pages.Count - 1;
                if (to > maxTo) to = maxTo;
                if (to >= 0 && from != to)
                    ReportAutoService.MovePage(from, to);
            }
            _insertIndex = -1;
            BuildCards();
        }

        /// <summary>绘制插入位置指示线(蓝色竖线)</summary>
        private void Flow_Paint(object sender, PaintEventArgs e)
        {
            if (_insertIndex < 0 || _flow.Controls.Count == 0) return;
            int x, top, bottom;
            if (_insertIndex < _flow.Controls.Count)
            {
                var c = _flow.Controls[_insertIndex];
                x = c.Left - 6; top = c.Top; bottom = c.Bottom;
            }
            else
            {
                var c = _flow.Controls[_flow.Controls.Count - 1];
                x = c.Right + 3; top = c.Top; bottom = c.Bottom;
            }
            using (var pen = new Pen(Color.FromArgb(70, 130, 200), 3))
                e.Graphics.DrawLine(pen, x, top, x, bottom);
        }

        /// <summary>按光标位置计算插入位:第一个"光标位于其上半行或左半部分"的卡片之前</summary>
        private int GetInsertIndex(Point pt)
        {
            for (int i = 0; i < _flow.Controls.Count; i++)
            {
                var c = _flow.Controls[i];
                bool aboveRow = pt.Y < c.Top;
                bool sameRow = pt.Y >= c.Top && pt.Y < c.Bottom;
                if (aboveRow || (sameRow && pt.X < c.Left + c.Width / 2))
                    return i;
            }
            return _flow.Controls.Count;
        }
    }
}
