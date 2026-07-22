// 工具栏图标工厂:用 GDI+ 绘制 20x20 扁平线性图标,不依赖外部图片资源。
// 图标静态缓存,进程生命周期内复用,随进程退出释放。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PCAN_Client
{
    /// <summary>
    /// 工具栏 20x20 图标工厂。Get(name) 获取缓存图标;未知名称返回空占位图。
    /// 主色灰 #505050,开始绿/停止红等语义色单独定义。
    /// </summary>
    internal static class ToolbarIcons
    {
        private const int S = 20; // 图标边长

        private static readonly Color Ink = Color.FromArgb(80, 80, 80);        // 主色(线条)
        private static readonly Color Green = Color.FromArgb(40, 150, 70);     // 开始/正向
        private static readonly Color Red = Color.FromArgb(200, 60, 55);       // 停止/危险
        private static readonly Color Blue = Color.FromArgb(60, 120, 190);     // 强调

        private static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>获取指定名称的缓存图标(调用方不要 Dispose 返回值)</summary>
        public static Bitmap Get(string name)
        {
            Bitmap bmp;
            if (_cache.TryGetValue(name, out bmp))
                return bmp;
            bmp = Create(name);
            _cache[name] = bmp;
            return bmp;
        }

        private static Bitmap Create(string name)
        {
            switch (name)
            {
                case "list": return Draw(g => { for (int i = 0; i < 3; i++) { int y = 4 + i * 5; g.FillRectangle(B(Ink), 3, y, 3, 3); g.DrawLine(P(Ink), 9, y + 1, 17, y + 1); } });
                case "dbc": return Draw(g => { g.DrawEllipse(P(Ink), 4, 2, 12, 5); g.DrawLine(P(Ink), 4, 4, 4, 15); g.DrawLine(P(Ink), 16, 4, 16, 15); g.DrawArc(P(Ink), 4, 12, 12, 5, 0, 180); g.DrawArc(P(Ink), 4, 8, 12, 5, 0, 180); });
                case "folder": return Draw(g => { using (var path = RoundedFolder()) { g.DrawPath(P(Ink), path); } });
                case "gear": return Draw(g => { for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; int x1 = 10 + (int)(5.5 * Math.Cos(a)), y1 = 10 + (int)(5.5 * Math.Sin(a)), x2 = 10 + (int)(8 * Math.Cos(a)), y2 = 10 + (int)(8 * Math.Sin(a)); g.DrawLine(P(Ink, 2.2f), x1, y1, x2, y2); } g.DrawEllipse(P(Ink), 4, 4, 12, 12); g.DrawEllipse(P(Ink), 8, 8, 4, 4); });
                case "bookmark": return Draw(g => { using (var path = new GraphicsPath()) { path.AddLines(new[] { new Point(6, 3), new Point(14, 3), new Point(14, 17), new Point(10, 13), new Point(6, 17) }); path.CloseFigure(); g.DrawPath(P(Ink), path); } });
                case "swap": return Draw(g => { g.DrawLine(P(Ink), 3, 7, 16, 7); g.DrawLine(P(Ink), 13, 4, 16, 7); g.DrawLine(P(Ink), 13, 10, 16, 7); g.DrawLine(P(Ink), 17, 13, 4, 13); g.DrawLine(P(Ink), 7, 10, 4, 13); g.DrawLine(P(Ink), 7, 16, 4, 13); });
                case "play": return Draw(g => { g.FillPolygon(B(Green), new[] { new Point(6, 4), new Point(16, 10), new Point(6, 16) }); });
                case "stop": return Draw(g => { g.FillRectangle(B(Red), 5, 5, 10, 10); });
                case "clock": return Draw(g => { g.DrawEllipse(P(Ink), 3, 3, 14, 14); g.DrawLine(P(Ink), 10, 10, 10, 5); g.DrawLine(P(Ink), 10, 10, 14, 12); });
                case "fit": return Draw(g => { g.DrawLine(P(Ink), 3, 8, 3, 3); g.DrawLine(P(Ink), 3, 3, 8, 3); g.DrawLine(P(Ink), 12, 3, 17, 3); g.DrawLine(P(Ink), 17, 3, 17, 8); g.DrawLine(P(Ink), 17, 12, 17, 17); g.DrawLine(P(Ink), 17, 17, 12, 17); g.DrawLine(P(Ink), 8, 17, 3, 17); g.DrawLine(P(Ink), 3, 17, 3, 12); });
                case "scroll": return Draw(g => { g.DrawLine(P(Ink), 3, 5, 17, 5); g.DrawLine(P(Ink), 3, 10, 11, 10); g.DrawLine(P(Ink), 3, 15, 11, 15); g.FillPolygon(B(Blue), new[] { new Point(13, 7), new Point(18, 10), new Point(13, 13) }); });
                case "clear": return Draw(g => { g.DrawLine(P(Ink, 2f), 5, 5, 15, 15); g.DrawLine(P(Ink, 2f), 15, 5, 5, 15); });
                case "dots": return Draw(g => { for (int i = 0; i < 3; i++) g.FillEllipse(B(Ink), 3 + i * 6, 9, 3, 3); });
                case "plus": return Draw(g => { g.DrawLine(P(Ink, 2f), 10, 4, 10, 16); g.DrawLine(P(Ink, 2f), 4, 10, 16, 10); });
                case "filter": return Draw(g => { using (var path = new GraphicsPath()) { path.AddLines(new[] { new Point(3, 4), new Point(17, 4), new Point(12, 10), new Point(12, 16), new Point(8, 14), new Point(8, 10) }); path.CloseFigure(); g.DrawPath(P(Ink), path); } });
                case "chart": return Draw(g => { g.DrawLine(P(Ink), 3, 3, 3, 17); g.DrawLine(P(Ink), 3, 17, 17, 17); g.DrawLines(P(Blue, 2f), new[] { new Point(5, 13), new Point(9, 8), new Point(12, 11), new Point(16, 5) }); });
                case "tag": return Draw(g => { using (var path = new GraphicsPath()) { path.AddLines(new[] { new Point(4, 4), new Point(11, 4), new Point(17, 10), new Point(10, 17), new Point(4, 10) }); path.CloseFigure(); g.DrawPath(P(Ink), path); g.FillEllipse(B(Ink), 7, 7, 2, 2); } });
                case "wrench": return Draw(g => { g.DrawArc(P(Ink, 2f), 10, 2, 8, 8, 20, 230); g.DrawLine(P(Ink, 2f), 11, 9, 4, 16); g.DrawArc(P(Ink, 2f), 2, 13, 5, 5, 180, 230); });
                case "target": return Draw(g => { g.DrawEllipse(P(Ink), 5, 5, 10, 10); g.DrawLine(P(Ink), 10, 1, 10, 6); g.DrawLine(P(Ink), 10, 14, 10, 19); g.DrawLine(P(Ink), 1, 10, 6, 10); g.DrawLine(P(Ink), 14, 10, 19, 10); g.FillEllipse(B(Red), 9, 9, 2, 2); });
                case "pageplus": return Draw(g => { g.DrawRectangle(P(Ink), 4, 2, 10, 13); g.DrawLine(P(Ink), 6, 6, 12, 6); g.DrawLine(P(Ink), 6, 9, 12, 9); g.DrawLine(P(Green, 2f), 14, 13, 19, 13); g.DrawLine(P(Green, 2f), 16, 11, 16, 16); });
                case "eye": return Draw(g => { using (var path = new GraphicsPath()) { path.AddBezier(2, 10, 7, 4, 13, 4, 18, 10); path.AddBezier(18, 10, 13, 16, 7, 16, 2, 10); g.DrawPath(P(Ink), path); } g.FillEllipse(B(Ink), 8, 8, 4, 4); });
                case "save": return Draw(g => { using (var path = new GraphicsPath()) { path.AddLines(new[] { new Point(3, 3), new Point(14, 3), new Point(17, 6), new Point(17, 17), new Point(3, 17) }); path.CloseFigure(); g.DrawPath(P(Ink), path); } g.DrawRectangle(P(Ink), 6, 3, 7, 5); g.DrawRectangle(P(Ink), 6, 11, 8, 6); });
                default: return Draw(g => { });
            }
        }

        private static GraphicsPath RoundedFolder()
        {
            var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new Point(2, 5), new Point(7, 5), new Point(9, 3),
                new Point(17, 3), new Point(17, 16), new Point(2, 16)
            });
            path.CloseFigure();
            return path;
        }

        private static Pen P(Color c, float w = 1.6f)
        {
            return new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }

        private static Brush B(Color c)
        {
            return new SolidBrush(c);
        }

        private static Bitmap Draw(Action<Graphics> draw)
        {
            var bmp = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                draw(g);
            }
            return bmp;
        }
    }
}
