namespace PCAN_Client.DataLog
{
    partial class LogFileToCSV
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LogFileToCSV));
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.button_Start = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.checkBox_ASC = new System.Windows.Forms.CheckBox();
            this.checkBox_CSV = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.comboBoxExcelSaveTime = new System.Windows.Forms.ComboBox();
            this.textBox_path = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.button_load = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(13, 142);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(729, 10);
            this.progressBar1.TabIndex = 20;
            // 
            // button_Start
            // 
            this.button_Start.Location = new System.Drawing.Point(588, 87);
            this.button_Start.Margin = new System.Windows.Forms.Padding(4);
            this.button_Start.Name = "button_Start";
            this.button_Start.Size = new System.Drawing.Size(154, 56);
            this.button_Start.TabIndex = 19;
            this.button_Start.Text = "启动转换";
            this.button_Start.UseVisualStyleBackColor = true;
            this.button_Start.Click += new System.EventHandler(this.button_Start_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(417, 87);
            this.button3.Margin = new System.Windows.Forms.Padding(4);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(163, 58);
            this.button3.TabIndex = 18;
            this.button3.Text = "源文件路径";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(12, 87);
            this.textBox1.Margin = new System.Windows.Forms.Padding(4);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(397, 56);
            this.textBox1.TabIndex = 17;
            // 
            // checkBox_ASC
            // 
            this.checkBox_ASC.AutoSize = true;
            this.checkBox_ASC.Location = new System.Drawing.Point(12, 36);
            this.checkBox_ASC.Name = "checkBox_ASC";
            this.checkBox_ASC.Size = new System.Drawing.Size(83, 19);
            this.checkBox_ASC.TabIndex = 21;
            this.checkBox_ASC.Text = "ASC开关";
            this.checkBox_ASC.UseVisualStyleBackColor = true;
            this.checkBox_ASC.CheckedChanged += new System.EventHandler(this.checkBox_ASC_CheckedChanged);
            // 
            // checkBox_CSV
            // 
            this.checkBox_CSV.AutoSize = true;
            this.checkBox_CSV.Location = new System.Drawing.Point(12, 61);
            this.checkBox_CSV.Name = "checkBox_CSV";
            this.checkBox_CSV.Size = new System.Drawing.Size(83, 19);
            this.checkBox_CSV.TabIndex = 22;
            this.checkBox_CSV.Text = "CSV开关";
            this.checkBox_CSV.UseVisualStyleBackColor = true;
            this.checkBox_CSV.CheckedChanged += new System.EventHandler(this.checkBox_CSV_CheckedChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(101, 61);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(160, 15);
            this.label1.TabIndex = 24;
            this.label1.Text = "CSV数据保存间隔(s)：";
            // 
            // comboBoxExcelSaveTime
            // 
            this.comboBoxExcelSaveTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxExcelSaveTime.FormattingEnabled = true;
            this.comboBoxExcelSaveTime.Location = new System.Drawing.Point(283, 57);
            this.comboBoxExcelSaveTime.Name = "comboBoxExcelSaveTime";
            this.comboBoxExcelSaveTime.Size = new System.Drawing.Size(126, 23);
            this.comboBoxExcelSaveTime.TabIndex = 23;
            this.comboBoxExcelSaveTime.SelectedIndexChanged += new System.EventHandler(this.comboBoxExcelSaveTime_SelectedIndexChanged);
            // 
            // textBox_path
            // 
            this.textBox_path.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_path.Location = new System.Drawing.Point(91, 6);
            this.textBox_path.Name = "textBox_path";
            this.textBox_path.ReadOnly = true;
            this.textBox_path.Size = new System.Drawing.Size(489, 25);
            this.textBox_path.TabIndex = 51;
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(9, 11);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(76, 15);
            this.label2.TabIndex = 50;
            this.label2.Text = "Dbc文件：";
            // 
            // button_load
            // 
            this.button_load.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.button_load.Location = new System.Drawing.Point(589, 6);
            this.button_load.Name = "button_load";
            this.button_load.Size = new System.Drawing.Size(154, 25);
            this.button_load.TabIndex = 49;
            this.button_load.Text = "浏览";
            this.button_load.UseVisualStyleBackColor = true;
            this.button_load.Click += new System.EventHandler(this.button_load_Click);
            // 
            // LogFileToCSV
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(755, 170);
            this.Controls.Add(this.textBox_path);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.button_load);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBoxExcelSaveTime);
            this.Controls.Add(this.checkBox_CSV);
            this.Controls.Add(this.checkBox_ASC);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.button_Start);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.textBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "LogFileToCSV";
            this.Text = "CAN报文转换";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.LogFileToCSV_FormClosed);
            this.Load += new System.EventHandler(this.LogFileToCSV_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.LogFileToCSV_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.LogFileToCSV_DragEnter);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Button button_Start;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.CheckBox checkBox_ASC;
        private System.Windows.Forms.CheckBox checkBox_CSV;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox comboBoxExcelSaveTime;
        private System.Windows.Forms.TextBox textBox_path;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button_load;
    }
}