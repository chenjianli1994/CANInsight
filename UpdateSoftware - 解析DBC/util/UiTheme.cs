using System.Drawing;
using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// 浅色现代风公共主题：统一三个窗口（Main报文列表/CanSend/LogFileToCSV）的
    /// 字体、按钮与 DataGridView 样式。数据列字体（Consolas）由各处自行保留。
    /// </summary>
    internal static class UiTheme
    {
        public static readonly Font UiFont = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        public static readonly Color HeaderBack = Color.FromArgb(232, 238, 247);
        public static readonly Color AlternatingRowBack = Color.FromArgb(247, 249, 252);
        public static readonly Color GridLine = Color.FromArgb(220, 224, 230);
        public static readonly Color SelectionBack = Color.FromArgb(198, 222, 241);
        public static readonly Color Accent = Color.FromArgb(0, 120, 215);

        /// <summary>
        /// 窗体级样式：统一应用 exe 内嵌图标（圆角蓝色信号波形，与主窗口一致）。
        /// applyFont=false（默认）：不设置窗体 Font——这些窗口都是
        /// AutoScaleMode.Font，改窗体字体会触发全部控件按比例重排、布局错位；
        /// 需要微软雅黑的控件（按钮/表头）由 StyleButton/StyleGrid 单独设置。
        /// </summary>
        public static void StyleForm(Form form, bool applyFont = false)
        {
            if (applyFont) form.Font = UiFont;
            ApplyAppIcon(form);
        }

        /// <summary>应用 exe 内嵌图标（csproj ApplicationIcon）</summary>
        public static void ApplyAppIcon(Form form)
        {
            try
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* 设计器/非常规宿主下忽略 */ }
        }

        /// <summary>应用统一的 DataGridView 样式（不改动各列既有字体/颜色语义）</summary>
        public static void StyleGrid(DataGridView dgv)
        {
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack;
            dgv.ColumnHeadersDefaultCellStyle.Font = HeaderFont;
            dgv.ColumnHeadersHeight = 26;
            dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgv.AlternatingRowsDefaultCellStyle.BackColor = AlternatingRowBack;
            dgv.GridColor = GridLine;
            dgv.DefaultCellStyle.SelectionBackColor = SelectionBack;
            dgv.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgv.RowTemplate.Height = 24;
            dgv.BorderStyle = BorderStyle.FixedSingle;
            dgv.BackgroundColor = Color.White;
        }

        /// <summary>
        /// 按钮统一为无边框 ToolStrip 风格（与绘图窗口工具栏一致）：
        /// 无边框、控件底色、悬停淡蓝，可前置 ToolbarIcons 图标（icon 为空则不加图标）
        /// </summary>
        public static void StyleButton(Button btn, string icon = null)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = SelectionBack;
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(160, 200, 235);
            btn.BackColor = SystemColors.Control;
            btn.Font = UiFont;
            if (!string.IsNullOrEmpty(icon))
            {
                btn.Image = ToolbarIcons.Get(icon);
                btn.TextImageRelation = TextImageRelation.ImageBeforeText;
                btn.ImageAlign = ContentAlignment.MiddleLeft;
                btn.TextAlign = ContentAlignment.MiddleCenter;
            }
        }

        /// <summary>
        /// 按列名设置列头显示文字（用于运行时自动生成列的表格，如 CanSend：
        /// 列名保持英文键不变仅改显示文本，不影响数据读写）
        /// </summary>
        public static void SetGridHeaders(DataGridView dgv, params (string name, string text)[] map)
        {
            foreach (var (name, text) in map)
            {
                var col = dgv.Columns[name];
                if (col != null) col.HeaderText = text;
            }
        }
    }
}
