namespace PCAN_Client
{
    partial class UDS
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
            this.button1 = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.VIN = new System.Windows.Forms.TextBox();
            this.TxOrRxData = new System.Windows.Forms.TextBox();
            this.WriteVIN = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Check3EServer = new System.Windows.Forms.CheckBox();
            this.ConverMode = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.ConverModeButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(314, 279);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 0;
            this.button1.Text = "读取";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 299);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(97, 15);
            this.label1.TabIndex = 1;
            this.label1.Text = "车辆识别码：";
            // 
            // VIN
            // 
            this.VIN.Location = new System.Drawing.Point(127, 277);
            this.VIN.Multiline = true;
            this.VIN.Name = "VIN";
            this.VIN.Size = new System.Drawing.Size(181, 51);
            this.VIN.TabIndex = 2;
            // 
            // TxOrRxData
            // 
            this.TxOrRxData.Location = new System.Drawing.Point(27, 26);
            this.TxOrRxData.Multiline = true;
            this.TxOrRxData.Name = "TxOrRxData";
            this.TxOrRxData.ReadOnly = true;
            this.TxOrRxData.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.TxOrRxData.Size = new System.Drawing.Size(975, 230);
            this.TxOrRxData.TabIndex = 4;
            // 
            // WriteVIN
            // 
            this.WriteVIN.Location = new System.Drawing.Point(314, 308);
            this.WriteVIN.Name = "WriteVIN";
            this.WriteVIN.Size = new System.Drawing.Size(75, 23);
            this.WriteVIN.TabIndex = 5;
            this.WriteVIN.Text = "写入";
            this.WriteVIN.UseVisualStyleBackColor = true;
            this.WriteVIN.Click += new System.EventHandler(this.WriteVIN_Click);
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(507, 298);
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            this.textBox1.Size = new System.Drawing.Size(100, 25);
            this.textBox1.TabIndex = 7;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(504, 280);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(103, 15);
            this.label2.TabIndex = 6;
            this.label2.Text = "Boot Or App:";
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(685, 299);
            this.textBox2.Name = "textBox2";
            this.textBox2.ReadOnly = true;
            this.textBox2.Size = new System.Drawing.Size(100, 25);
            this.textBox2.TabIndex = 9;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(682, 280);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(103, 19);
            this.label3.TabIndex = 8;
            this.label3.Text = "会话模式：";
            // 
            // Check3EServer
            // 
            this.Check3EServer.AutoSize = true;
            this.Check3EServer.Location = new System.Drawing.Point(908, 276);
            this.Check3EServer.Name = "Check3EServer";
            this.Check3EServer.Size = new System.Drawing.Size(75, 19);
            this.Check3EServer.TabIndex = 10;
            this.Check3EServer.Text = "3E服务";
            this.Check3EServer.UseVisualStyleBackColor = true;
            // 
            // ConverMode
            // 
            this.ConverMode.FormattingEnabled = true;
            this.ConverMode.Location = new System.Drawing.Point(127, 353);
            this.ConverMode.Name = "ConverMode";
            this.ConverMode.Size = new System.Drawing.Size(181, 23);
            this.ConverMode.TabIndex = 11;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(24, 356);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(103, 19);
            this.label4.TabIndex = 12;
            this.label4.Text = "会话模式：";
            // 
            // timer1
            // 
            this.timer1.Interval = 2000;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // ConverModeButton
            // 
            this.ConverModeButton.Location = new System.Drawing.Point(314, 353);
            this.ConverModeButton.Name = "ConverModeButton";
            this.ConverModeButton.Size = new System.Drawing.Size(75, 23);
            this.ConverModeButton.TabIndex = 13;
            this.ConverModeButton.Text = "写入";
            this.ConverModeButton.UseVisualStyleBackColor = true;
            this.ConverModeButton.Click += new System.EventHandler(this.ConverModeButton_Click);
            // 
            // UDS
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1029, 586);
            this.Controls.Add(this.ConverModeButton);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.ConverMode);
            this.Controls.Add(this.Check3EServer);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.WriteVIN);
            this.Controls.Add(this.TxOrRxData);
            this.Controls.Add(this.VIN);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button1);
            this.Name = "UDS";
            this.Text = "UDS";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.UDS_FormClosing);
            this.Load += new System.EventHandler(this.UDS_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox VIN;
        private System.Windows.Forms.TextBox TxOrRxData;
        private System.Windows.Forms.Button WriteVIN;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox Check3EServer;
        private System.Windows.Forms.ComboBox ConverMode;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Button ConverModeButton;
    }
}