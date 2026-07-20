namespace PCAN_Client.ReportAuto
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;
    using PCAN_Client;  // 访问 ChartControl

    /// <summary>
    /// 自动生成报告 - 截图模块。
    /// 提供 ChartControl 离屏高清渲染与普通控件兜底截图两种方式。
    /// </summary>
    public static class ChartCapturer
    {
        /// <summary>
        /// 离屏渲染ChartControl到高清Bitmap。临时放大控件Size到 width×height，
        /// 用 Graphics.FromImage 调 RenderTo 绘制，然后恢复控件原 Size/Dock。
        /// 不依赖窗体可见，输出任意分辨率。
        /// </summary>
        /// <param name="ctl">要渲染的图表控件</param>
        /// <param name="width">输出位图宽度</param>
        /// <param name="height">输出位图高度</param>
        /// <returns>渲染得到的 Bitmap；入参非法或异常时返回 null</returns>
        public static Bitmap RenderChart(ChartControl ctl, int width, int height)
        {
            // 容错：控件为空或尺寸过小直接返回 null，避免 0 尺寸异常
            if (ctl == null || width < 100 || height < 100)
                return null;

            // 保存控件原布局状态，渲染结束后恢复
            DockStyle oldDock = ctl.Dock;
            Size oldSize = ctl.Size;
            
            // 计算渲染缩放比例（目标分辨率与原始尺寸的比例）
            float scaleX = (float)width / oldSize.Width;
            float scaleY = (float)height / oldSize.Height;
            float renderScale = Math.Min(scaleX, scaleY);
            
            // 设置渲染缩放，使线宽和字体按比例放大
            ctl.SetRenderScale(renderScale);

            Bitmap bmp = null;
            try
            {
                // 临时取消停靠并放大控件尺寸到目标分辨率
                ctl.Dock = DockStyle.None;
                ctl.Size = new Size(width, height);

                // 强制同步布局和绘制（替代 Invalidate + DoEvents，更可靠）
                ctl.PerformLayout();
                ctl.Update();

                // 离屏渲染：基于位图创建 Graphics，复用控件 OnPaint 全部绘制逻辑
                bmp = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    ctl.RenderTo(g);
                }
            }
            catch (Exception ex)
            {
                // 记录异常信息，帮助调试截图问题
                System.Diagnostics.Debug.WriteLine("[ChartCapturer] 离屏渲染异常: " + ex.Message);
                // 任意异常都不向上抛：释放已生成的位图并返回 null
                if (bmp != null)
                {
                    bmp.Dispose();
                    bmp = null;
                }
            }
            finally
            {
                // 恢复控件原 Size/Dock 并刷新显示
                try
                {
                    ctl.Dock = oldDock;
                    ctl.Size = oldSize;
                    ctl.SetRenderScale(1.0f);
                    ctl.Invalidate();
                }
                catch (Exception)
                {
                    // 恢复阶段异常忽略，不影响调用方
                }
            }

            return bmp;
        }

        /// <summary>
        /// 合成截图：将绘图区高清Bitmap与信号列表控件截图左右拼接。
        /// 信号列表在左侧，绘图区在右侧，保持与软件界面相同的宽度比例。
        /// 返回合成后的Bitmap；入参异常时返回null。
        /// chartBmp 的所有权转移给此方法，合成后会释放原始chartBmp。
        /// </summary>
        public static Bitmap ComposeScreenshot(Bitmap chartBmp, DataGridView signalGrid)
        {
            if (chartBmp == null) return null;

            Bitmap gridBmp = null;
            try
            {
                // 信号列表截图（原始分辨率）
                gridBmp = CaptureControl(signalGrid);

                // 获取软件中的实际宽度比例
                int signalGridWidth = signalGrid?.Width ?? 300;
                int chartViewWidth = chartBmp.Width; // 渲染后的宽度，与控件宽度成比例

                // 计算缩放比例：使信号列表的高度匹配绘图区高度（保持宽度比例）
                float gridScale = 1.0f;
                if (gridBmp != null && gridBmp.Height > 0)
                {
                    gridScale = (float)chartBmp.Height / gridBmp.Height;
                }

                // 按相同比例缩放信号列表宽度
                int scaledGridWidth = (int)(signalGridWidth * gridScale);
                int scaledGridHeight = (int)(gridBmp?.Height * gridScale ?? chartBmp.Height);

                int totalWidth = scaledGridWidth + chartBmp.Width;
                int totalHeight = Math.Max(scaledGridHeight, chartBmp.Height);
                if (totalHeight < 1) totalHeight = chartBmp.Height;

                Bitmap composite = new Bitmap(totalWidth, totalHeight);
                using (Graphics g = Graphics.FromImage(composite))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    
                    // 左侧：信号列表（按比例缩放，高度与绘图区一致）
                    if (gridBmp != null)
                    {
                        g.DrawImage(gridBmp, 0, 0, scaledGridWidth, scaledGridHeight);
                    }
                    // 右侧：绘图区（保持原始高分辨率）
                    g.DrawImage(chartBmp, scaledGridWidth, 0);
                }

                return composite;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ChartCapturer] 合成截图异常: " + ex.Message);
                return null;
            }
            finally
            {
                // 确保无论成功或异常，都释放原始位图，避免GDI资源泄漏
                gridBmp?.Dispose();
                chartBmp.Dispose();
            }
        }

        /// <summary>
        /// 兜底：用 DrawToBitmap 截取任意控件当前外观。对标准控件(如DataGridView)可靠。
        /// </summary>
        /// <param name="ctl">要截图的控件</param>
        /// <returns>控件当前外观的 Bitmap；入参非法或异常时返回 null</returns>
        public static Bitmap CaptureControl(Control ctl)
        {
            // 容错：控件为空或尺寸无效返回 null
            if (ctl == null || ctl.Width <= 0 || ctl.Height <= 0)
                return null;

            try
            {
                Bitmap bmp = new Bitmap(ctl.Width, ctl.Height);
                ctl.DrawToBitmap(bmp, new Rectangle(0, 0, ctl.Width, ctl.Height));
                return bmp;
            }
            catch (Exception)
            {
                // 截图失败返回 null，不抛异常
                return null;
            }
        }
    }
}
