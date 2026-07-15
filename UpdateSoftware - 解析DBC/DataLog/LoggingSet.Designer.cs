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
            this.FileSizeLimit = new System.Windows.Forms.GroupBox();
            this.Timing_Length_Text = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.fileAdress = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.radioButton2 = new System.Windows.Forms.RadioButton();
            this.radioButton1 = new System.Windows.Forms.RadioButton();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBoxSaveExcel = new System.Windows.Forms.CheckBox();
            this.comboBoxExcelSaveTime = new System.Windows.Forms.ComboBox();
            this.FileSizeLimit.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(411, 192);
            this.button2.Margin = new System.Windows.Forms.Padding(4);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(288, 86);
            this.button2.TabIndex = 11;
            this.button2.Text = "开始保存";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // FileSizeLimit
            // 
            this.FileSizeLimit.Controls.Add(this.Timing_Length_Text);
            this.FileSizeLimit.Location = new System.Drawing.Point(21, 100);
            this.FileSizeLimit.Margin = new System.Windows.Forms.Padding(4);
            this.FileSizeLimit.Name = "FileSizeLimit";
            this.FileSizeLimit.Padding = new System.Windows.Forms.Padding(4);
            this.FileSizeLimit.Size = new System.Drawing.Size(303, 84);
            this.FileSizeLimit.TabIndex = 9;
            this.FileSizeLimit.TabStop = false;
            this.FileSizeLimit.Text = "File Size Limit(MB)";
            // 
            // Timing_Length_Text
            // 
            this.Timing_Length_Text.Location = new System.Drawing.Point(8, 35);
            this.Timing_Length_Text.Margin = new System.Windows.Forms.Padding(4);
            this.Timing_Length_Text.Name = "Timing_Length_Text";
            this.Timing_Length_Text.Size = new System.Drawing.Size(279, 25);
            this.Timing_Length_Text.TabIndex = 0;
            this.Timing_Length_Text.Text = "100";
            this.Timing_Length_Text.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.Timing_Length_Text.TextChanged += new System.EventHandler(this.Timing_Length_Text_TextChanged);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(537, 13);
            this.button1.Margin = new System.Windows.Forms.Padding(4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(163, 58);
            this.button1.TabIndex = 7;
            this.button1.Text = "存储路径";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // fileAdress
            // 
            this.fileAdress.Location = new System.Drawing.Point(21, 13);
            this.fileAdress.Margin = new System.Windows.Forms.Padding(4);
            this.fileAdress.Multiline = true;
            this.fileAdress.Name = "fileAdress";
            this.fileAdress.Size = new System.Drawing.Size(508, 56);
            this.fileAdress.TabIndex = 6;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.radioButton2);
            this.groupBox1.Controls.Add(this.radioButton1);
            this.groupBox1.Location = new System.Drawing.Point(411, 100);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(289, 84);
            this.groupBox1.TabIndex = 12;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "File Type";
            // 
            // radioButton2
            // 
            this.radioButton2.AutoSize = true;
            this.radioButton2.Location = new System.Drawing.Point(205, 35);
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
            this.radioButton1.Location = new System.Drawing.Point(50, 35);
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
            this.groupBox2.Location = new System.Drawing.Point(21, 194);
            this.groupBox2.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox2.Size = new System.Drawing.Size(303, 84);
            this.groupBox2.TabIndex = 17;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Excel";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 58);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(82, 15);
            this.label2.TabIndex = 20;
            this.label2.Text = "是否保存：";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(7, 28);
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
            this.checkBoxSaveExcel.Size = new System.Drawing.Size(99, 19);
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
            this.comboBoxExcelSaveTime.Size = new System.Drawing.Size(162, 23);
            this.comboBoxExcelSaveTime.TabIndex = 0;
            this.comboBoxExcelSaveTime.SelectedIndexChanged += new System.EventHandler(this.comboBoxExcelSaveTime_SelectedIndexChanged);
            // 
            // LoggingSet
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(709, 295);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.FileSizeLimit);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.fileAdress);
            this.Name = "LoggingSet";
            this.Text = "LoggingSet";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.LoggingSet_FormClosing);
            this.Load += new System.EventHandler(this.LoggingSet_Load);
            this.FileSizeLimit.ResumeLayout(false);
            this.FileSizeLimit.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.GroupBox FileSizeLimit;
        private System.Windows.Forms.TextBox Timing_Length_Text;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox fileAdress;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton radioButton2;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.ComboBox comboBoxExcelSaveTime;
        private System.Windows.Forms.CheckBox checkBoxSaveExcel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}