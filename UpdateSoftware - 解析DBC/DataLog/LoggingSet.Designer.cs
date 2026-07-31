namespace PCAN_Client.DataLog
{
    partial class LoggingSet
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
            this.button2 = new System.Windows.Forms.Button();
            this.buttonStop = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.fileAdress = new System.Windows.Forms.TextBox();
            this.labelPath = new System.Windows.Forms.Label();
            this.labelStatus = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.radioButton2 = new System.Windows.Forms.RadioButton();
            this.radioButton1 = new System.Windows.Forms.RadioButton();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBoxSaveExcel = new System.Windows.Forms.CheckBox();
            this.comboBoxExcelSaveTime = new System.Windows.Forms.ComboBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.checkedListBoxChannels = new System.Windows.Forms.CheckedListBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.SuspendLayout();
            //
            // button2
            //
            this.button2.Location = new System.Drawing.Point(21, 160);
            this.button2.Margin = new System.Windows.Forms.Padding(4);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(160, 44);
            this.button2.TabIndex = 11;
            this.button2.Text = "开始录制";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            //
            // buttonStop
            //
            this.buttonStop.Enabled = false;
            this.buttonStop.Location = new System.Drawing.Point(200, 160);
            this.buttonStop.Margin = new System.Windows.Forms.Padding(4);
            this.buttonStop.Name = "buttonStop";
            this.buttonStop.Size = new System.Drawing.Size(160, 44);
            this.buttonStop.TabIndex = 19;
            this.buttonStop.Text = "停止录制";
            this.buttonStop.UseVisualStyleBackColor = true;
            this.buttonStop.Click += new System.EventHandler(this.buttonStop_Click);
            //
            // button1
            //
            this.button1.Location = new System.Drawing.Point(552, 16);
            this.button1.Margin = new System.Windows.Forms.Padding(4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(140, 30);
            this.button1.TabIndex = 7;
            this.button1.Text = "存储路径";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            //
            // fileAdress
            //
            this.fileAdress.Location = new System.Drawing.Point(110, 18);
            this.fileAdress.Margin = new System.Windows.Forms.Padding(4);
            this.fileAdress.Name = "fileAdress";
            this.fileAdress.Size = new System.Drawing.Size(432, 25);
            this.fileAdress.TabIndex = 6;
            //
            // labelPath
            //
            this.labelPath.AutoSize = true;
            this.labelPath.Location = new System.Drawing.Point(21, 22);
            this.labelPath.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelPath.Name = "labelPath";
            this.labelPath.Size = new System.Drawing.Size(82, 15);
            this.labelPath.TabIndex = 21;
            this.labelPath.Text = "存储路径：";
            //
            // labelStatus
            //
            this.labelStatus.Location = new System.Drawing.Point(21, 218);
            this.labelStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(671, 60);
            this.labelStatus.TabIndex = 22;
            this.labelStatus.Text = "状态：未录制";
            //
            // groupBox1
            //
            this.groupBox1.Controls.Add(this.radioButton2);
            this.groupBox1.Controls.Add(this.radioButton1);
            this.groupBox1.Location = new System.Drawing.Point(21, 60);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(320, 84);
            this.groupBox1.TabIndex = 12;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "文件类型";
            //
            // radioButton2
            //
            this.radioButton2.AutoSize = true;
            this.radioButton2.Location = new System.Drawing.Point(200, 35);
            this.radioButton2.Name = "radioButton2";
            this.radioButton2.Size = new System.Drawing.Size(52, 19);
            this.radioButton2.TabIndex = 14;
            this.radioButton2.TabStop = true;
            this.radioButton2.Text = "BLF";
            this.radioButton2.UseVisualStyleBackColor = true;
            this.radioButton2.CheckedChanged += new System.EventHandler(this.radioButton2_CheckedChanged);
            //
            // radioButton1
            //
            this.radioButton1.AutoSize = true;
            this.radioButton1.Location = new System.Drawing.Point(80, 35);
            this.radioButton1.Name = "radioButton1";
            this.radioButton1.Size = new System.Drawing.Size(52, 19);
            this.radioButton1.TabIndex = 13;
            this.radioButton1.TabStop = true;
            this.radioButton1.Text = "ASC";
            this.radioButton1.UseVisualStyleBackColor = true;
            this.radioButton1.CheckedChanged += new System.EventHandler(this.radioButton1_CheckedChanged);
            //
            // groupBox2
            //
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.checkBoxSaveExcel);
            this.groupBox2.Controls.Add(this.comboBoxExcelSaveTime);
            this.groupBox2.Location = new System.Drawing.Point(360, 60);
            this.groupBox2.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox2.Size = new System.Drawing.Size(332, 84);
            this.groupBox2.TabIndex = 17;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Excel 保存";
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(10, 58);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(82, 15);
            this.label2.TabIndex = 20;
            this.label2.Text = "是否保存：";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(10, 28);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(106, 15);
            this.label1.TabIndex = 19;
            this.label1.Text = "保存间隔(s)：";
            //
            // checkBoxSaveExcel
            //
            this.checkBoxSaveExcel.AutoSize = true;
            this.checkBoxSaveExcel.Location = new System.Drawing.Point(125, 54);
            this.checkBoxSaveExcel.Name = "checkBoxSaveExcel";
            this.checkBoxSaveExcel.Size = new System.Drawing.Size(91, 19);
            this.checkBoxSaveExcel.TabIndex = 18;
            this.checkBoxSaveExcel.Text = "保存Excel";
            this.checkBoxSaveExcel.UseVisualStyleBackColor = true;
            this.checkBoxSaveExcel.CheckedChanged += new System.EventHandler(this.checkBoxSaveExcel_CheckedChanged);
            //
            // comboBoxExcelSaveTime
            //
            this.comboBoxExcelSaveTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxExcelSaveTime.FormattingEnabled = true;
            this.comboBoxExcelSaveTime.Location = new System.Drawing.Point(125, 25);
            this.comboBoxExcelSaveTime.Name = "comboBoxExcelSaveTime";
            this.comboBoxExcelSaveTime.Size = new System.Drawing.Size(190, 23);
            this.comboBoxExcelSaveTime.TabIndex = 0;
            this.comboBoxExcelSaveTime.SelectedIndexChanged += new System.EventHandler(this.comboBoxExcelSaveTime_SelectedIndexChanged);
            //
            // groupBox3
            //
            this.groupBox3.Controls.Add(this.checkedListBoxChannels);
            this.groupBox3.Location = new System.Drawing.Point(716, 13);
            this.groupBox3.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox3.Size = new System.Drawing.Size(160, 265);
            this.groupBox3.TabIndex = 18;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "录制通道";
            //
            // checkedListBoxChannels
            //
            this.checkedListBoxChannels.CheckOnClick = true;
            this.checkedListBoxChannels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.checkedListBoxChannels.FormattingEnabled = true;
            this.checkedListBoxChannels.Location = new System.Drawing.Point(4, 22);
            this.checkedListBoxChannels.Margin = new System.Windows.Forms.Padding(4);
            this.checkedListBoxChannels.Name = "checkedListBoxChannels";
            this.checkedListBoxChannels.Size = new System.Drawing.Size(152, 239);
            this.checkedListBoxChannels.TabIndex = 0;
            //
            // LoggingSet
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(889, 295);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.labelPath);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.buttonStop);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.fileAdress);
            this.Name = "LoggingSet";
            this.Text = "录制报文配置";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.LoggingSet_FormClosing);
            this.Load += new System.EventHandler(this.LoggingSet_Load);
            this.VisibleChanged += new System.EventHandler(this.LoggingSet_VisibleChanged);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button buttonStop;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox fileAdress;
        private System.Windows.Forms.Label labelPath;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton radioButton2;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.ComboBox comboBoxExcelSaveTime;
        private System.Windows.Forms.CheckBox checkBoxSaveExcel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.CheckedListBox checkedListBoxChannels;
    }
}
