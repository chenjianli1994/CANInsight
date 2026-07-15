namespace PCAN_Client
{
    partial class ChartShow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.Legend legend1 = new System.Windows.Forms.DataVisualization.Charting.Legend();
            System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
            this.chart2 = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.ChartSelectDataCombox = new System.Windows.Forms.ComboBox();
            this.label10 = new System.Windows.Forms.Label();
            this.Y_Auto = new System.Windows.Forms.CheckBox();
            this.Y_MaxValue = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Y_MinValue = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.DateNumber = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)(this.chart2)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // chart2
            // 
            chartArea1.AxisY.LabelStyle.Format = "N4";
            chartArea1.Name = "ChartArea1";
            this.chart2.ChartAreas.Add(chartArea1);
            legend1.Name = "Legend1";
            this.chart2.Legends.Add(legend1);
            this.chart2.Location = new System.Drawing.Point(4, 4);
            this.chart2.Margin = new System.Windows.Forms.Padding(4);
            this.chart2.Name = "chart2";
            this.chart2.Palette = System.Windows.Forms.DataVisualization.Charting.ChartColorPalette.None;
            series1.ChartArea = "ChartArea1";
            series1.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
            series1.Font = new System.Drawing.Font("华文楷体", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            series1.LabelAngle = 90;
            series1.LabelBorderDashStyle = System.Windows.Forms.DataVisualization.Charting.ChartDashStyle.Dash;
            series1.Legend = "Legend1";
            series1.LegendText = "数据";
            series1.MarkerColor = System.Drawing.Color.Red;
            series1.MarkerSize = 6;
            series1.MarkerStyle = System.Windows.Forms.DataVisualization.Charting.MarkerStyle.Circle;
            series1.Name = "Series1";
            series1.SmartLabelStyle.CalloutLineWidth = 2;
            this.chart2.Series.Add(series1);
            this.chart2.Size = new System.Drawing.Size(964, 425);
            this.chart2.TabIndex = 2;
            this.chart2.Text = "chart2";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.textBox1);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.checkBox1);
            this.groupBox1.Controls.Add(this.ChartSelectDataCombox);
            this.groupBox1.Controls.Add(this.label10);
            this.groupBox1.Controls.Add(this.Y_Auto);
            this.groupBox1.Controls.Add(this.Y_MaxValue);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.Y_MinValue);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.DateNumber);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(13, 13);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(313, 451);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "曲线参数设置";
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(144, 131);
            this.textBox1.Margin = new System.Windows.Forms.Padding(4);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(161, 25);
            this.textBox1.TabIndex = 19;
            this.textBox1.Text = "1000";
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(8, 135);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(114, 15);
            this.label6.TabIndex = 18;
            this.label6.Text = "数据间隔(ms)：";
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(215, 195);
            this.checkBox1.Margin = new System.Windows.Forms.Padding(4);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(89, 19);
            this.checkBox1.TabIndex = 17;
            this.checkBox1.Text = "暂停显示";
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // ChartSelectDataCombox
            // 
            this.ChartSelectDataCombox.FormattingEnabled = true;
            this.ChartSelectDataCombox.Location = new System.Drawing.Point(144, 164);
            this.ChartSelectDataCombox.Margin = new System.Windows.Forms.Padding(4);
            this.ChartSelectDataCombox.Name = "ChartSelectDataCombox";
            this.ChartSelectDataCombox.Size = new System.Drawing.Size(161, 23);
            this.ChartSelectDataCombox.TabIndex = 14;
            this.ChartSelectDataCombox.SelectedIndexChanged += new System.EventHandler(this.ChartSelectDataCombox_SelectedIndexChanged);
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(9, 168);
            this.label10.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(67, 15);
            this.label10.TabIndex = 13;
            this.label10.Text = "数据源：";
            // 
            // Y_Auto
            // 
            this.Y_Auto.AutoSize = true;
            this.Y_Auto.Location = new System.Drawing.Point(95, 72);
            this.Y_Auto.Margin = new System.Windows.Forms.Padding(4);
            this.Y_Auto.Name = "Y_Auto";
            this.Y_Auto.Size = new System.Drawing.Size(97, 19);
            this.Y_Auto.TabIndex = 8;
            this.Y_Auto.Text = "Y轴自适应";
            this.Y_Auto.UseVisualStyleBackColor = true;
            this.Y_Auto.CheckedChanged += new System.EventHandler(this.Y_Auto_CheckedChanged);
            // 
            // Y_MaxValue
            // 
            this.Y_MaxValue.Location = new System.Drawing.Point(177, 96);
            this.Y_MaxValue.Margin = new System.Windows.Forms.Padding(4);
            this.Y_MaxValue.Name = "Y_MaxValue";
            this.Y_MaxValue.Size = new System.Drawing.Size(127, 25);
            this.Y_MaxValue.TabIndex = 7;
            this.Y_MaxValue.Text = "50";
            this.Y_MaxValue.TextChanged += new System.EventHandler(this.Y_MaxValue_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(155, 101);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(15, 15);
            this.label3.TabIndex = 6;
            this.label3.Text = "-";
            // 
            // Y_MinValue
            // 
            this.Y_MinValue.Location = new System.Drawing.Point(11, 96);
            this.Y_MinValue.Margin = new System.Windows.Forms.Padding(4);
            this.Y_MinValue.Name = "Y_MinValue";
            this.Y_MinValue.Size = new System.Drawing.Size(135, 25);
            this.Y_MinValue.TabIndex = 5;
            this.Y_MinValue.Text = "0";
            this.Y_MinValue.TextChanged += new System.EventHandler(this.Y_MinValue_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 78);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(75, 15);
            this.label2.TabIndex = 4;
            this.label2.Text = "Y轴范围：";
            // 
            // DateNumber
            // 
            this.DateNumber.Location = new System.Drawing.Point(144, 28);
            this.DateNumber.Margin = new System.Windows.Forms.Padding(4);
            this.DateNumber.Name = "DateNumber";
            this.DateNumber.Size = new System.Drawing.Size(160, 25);
            this.DateNumber.TabIndex = 3;
            this.DateNumber.Text = "500";
            this.DateNumber.TextChanged += new System.EventHandler(this.DateNumber_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 32);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(82, 15);
            this.label1.TabIndex = 2;
            this.label1.Text = "数据个数：";
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.chart2);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(333, 21);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(979, 444);
            this.flowLayoutPanel1.TabIndex = 5;
            // 
            // ChartShow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1314, 477);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.groupBox1);
            this.Name = "ChartShow";
            this.Text = "ChartShow";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ChartShow_FormClosing);
            this.Load += new System.EventHandler(this.ChartShow_Load);
            ((System.ComponentModel.ISupportInitialize)(this.chart2)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataVisualization.Charting.Chart chart2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ComboBox ChartSelectDataCombox;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.CheckBox Y_Auto;
        private System.Windows.Forms.TextBox Y_MaxValue;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox Y_MinValue;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox DateNumber;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
    }
}