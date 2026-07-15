using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCAN_Client.util
{
    internal class MyChart
    {
        private System.Windows.Forms.DataVisualization.Charting.Chart PreMYCHART_chart = null;

        private int X_AxisMaxValue = 1000;
        private Boolean Y_AxisAutoFlag = true;
        private float Y_AxisMinValue = 0;
        private float Y_AxisMaxValue = 20;
        public static string SelectSignalName = "";

        internal void SetMYCHART_SelectSignalName(string SignalName)
        {
            SelectSignalName = SignalName;
        }
        internal void SetMYCHART_YAutoFlag(Boolean sta)
        {
            Y_AxisAutoFlag = sta;
        }

        internal void SetMYCHART_X_AxisMaxValue(int value)
        {
            X_AxisMaxValue = value;
        }
        internal void SetMYCHART_Y_AxisMinValue(float value)
        {
            if (value < Y_AxisMaxValue)
            {
                Y_AxisMinValue = value;
            }
            else
            {
                /* empty */
            }
        }

        internal void SetMYCHART_Y_AxisMaxValue(float value)
        {
            if (value > Y_AxisMinValue)
            {
                Y_AxisMaxValue = value;
            }
            else
            {
                /* empty */
            }
        }

        internal void SetMYCHART_Chart(System.Windows.Forms.DataVisualization.Charting.Chart chart)
        {
            PreMYCHART_chart = chart;
        }

        internal void data_bind()//数据绑定
        {
            Dictionary<string, List<double>> ChartDataBuf = null;
            Main.chartShow.GetChartDataBuf(ref ChartDataBuf);

            if(null != ChartDataBuf && null != PreMYCHART_chart && ChartDataBuf.Count > 0)
            {
                Main.chartShow.BeginInvoke((EventHandler)delegate
                {
                    try
                    {
                        if (Y_AxisAutoFlag)//自适应
                        {
                            float min;
                            float max;

                            if (ChartDataBuf[SelectSignalName].Count > 0)
                            {
                                min = (float)ChartDataBuf[SelectSignalName].Min();
                                max = (float)ChartDataBuf[SelectSignalName].Max();
                            }
                            else
                            {
                                min = 0;
                                max = 100;
                            }
                            float difference = (max - min) * 0.01f;
                            difference = (difference < 0) ? -difference : difference;

                            PreMYCHART_chart.ChartAreas[0].AxisY.Minimum = min - difference - 0.01f;
                            PreMYCHART_chart.ChartAreas[0].AxisY.Maximum = max + difference + 0.01f;
                        }
                        else
                        {
                            PreMYCHART_chart.ChartAreas[0].AxisY.Minimum = Y_AxisMinValue;
                            PreMYCHART_chart.ChartAreas[0].AxisY.Maximum = Y_AxisMaxValue;
                        }

                        PreMYCHART_chart.Series[0].Points.DataBindY(ChartDataBuf[SelectSignalName]);
                        PreMYCHART_chart.Series[0].Name = SelectSignalName;
                        PreMYCHART_chart.ChartAreas[0].Axes[1].Title = SelectSignalName;
                    }
                    catch
                    {
                        //MessageBox.Show(ex.ToString());
                    }
                });
            }
        }
    }
}
