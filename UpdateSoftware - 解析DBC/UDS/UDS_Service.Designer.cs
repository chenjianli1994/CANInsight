namespace PCAN_Client
{
    partial class UDS_Service
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
            this.BootOrApp = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.ConverModeTextBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.Check3EServer = new System.Windows.Forms.CheckBox();
            this.ConverMode = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.ConverModeButton = new System.Windows.Forms.Button();
            this.PM2_5CFG = new System.Windows.Forms.ComboBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.SeatsNumCFG = new System.Windows.Forms.ComboBox();
            this.ADASCFG = new System.Windows.Forms.ComboBox();
            this.button5 = new System.Windows.Forms.Button();
            this.HidetextBox = new System.Windows.Forms.TextBox();
            this.CfgWriteButton = new System.Windows.Forms.Button();
            this.CfgReadButton = new System.Windows.Forms.Button();
            this.HeatPumpTypeCfg = new System.Windows.Forms.ComboBox();
            this.RefrigerateTypeCfg = new System.Windows.Forms.ComboBox();
            this.AreaCfg = new System.Windows.Forms.ComboBox();
            this.WarmAreaCfg = new System.Windows.Forms.ComboBox();
            this.DriveMotorTypeCfg = new System.Windows.Forms.ComboBox();
            this.SteeringRudderTypeCfg = new System.Windows.Forms.ComboBox();
            this.AgsCFG = new System.Windows.Forms.ComboBox();
            this.Valve3DspCFG = new System.Windows.Forms.ComboBox();
            this.FragranceSystemCFG = new System.Windows.Forms.ComboBox();
            this.AnionCFG = new System.Windows.Forms.ComboBox();
            this.AQSCFG = new System.Windows.Forms.ComboBox();
            this.timer2 = new System.Windows.Forms.Timer(this.components);
            this.button2 = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.NRC_textBox = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.button6 = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.SoftWareVersion = new System.Windows.Forms.TextBox();
            this.HardWareVersion = new System.Windows.Forms.TextBox();
            this.button7 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.button9 = new System.Windows.Forms.Button();
            this.button10 = new System.Windows.Forms.Button();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.label13 = new System.Windows.Forms.Label();
            this.SystemNameOrEngineType = new System.Windows.Forms.TextBox();
            this.label12 = new System.Windows.Forms.Label();
            this.BootVersion = new System.Windows.Forms.TextBox();
            this.label11 = new System.Windows.Forms.Label();
            this.TryProgrameNum = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.ProgrameNum = new System.Windows.Forms.TextBox();
            this.button11 = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.ManufacturerSparePartNumber = new System.Windows.Forms.TextBox();
            this.ProgrameDate = new System.Windows.Forms.TextBox();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.button12 = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox5.SuspendLayout();
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
            // BootOrApp
            // 
            this.BootOrApp.Location = new System.Drawing.Point(9, 37);
            this.BootOrApp.Name = "BootOrApp";
            this.BootOrApp.ReadOnly = true;
            this.BootOrApp.Size = new System.Drawing.Size(100, 25);
            this.BootOrApp.TabIndex = 7;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 19);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(103, 15);
            this.label2.TabIndex = 6;
            this.label2.Text = "Boot Or App:";
            // 
            // ConverModeTextBox
            // 
            this.ConverModeTextBox.Location = new System.Drawing.Point(9, 90);
            this.ConverModeTextBox.Name = "ConverModeTextBox";
            this.ConverModeTextBox.ReadOnly = true;
            this.ConverModeTextBox.Size = new System.Drawing.Size(100, 25);
            this.ConverModeTextBox.TabIndex = 9;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(6, 71);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(82, 15);
            this.label3.TabIndex = 8;
            this.label3.Text = "会话模式：";
            // 
            // Check3EServer
            // 
            this.Check3EServer.AutoSize = true;
            this.Check3EServer.Location = new System.Drawing.Point(685, 268);
            this.Check3EServer.Name = "Check3EServer";
            this.Check3EServer.Size = new System.Drawing.Size(75, 19);
            this.Check3EServer.TabIndex = 10;
            this.Check3EServer.Text = "3E服务";
            this.Check3EServer.UseVisualStyleBackColor = true;
            // 
            // ConverMode
            // 
            this.ConverMode.FormattingEnabled = true;
            this.ConverMode.Location = new System.Drawing.Point(127, 340);
            this.ConverMode.Name = "ConverMode";
            this.ConverMode.Size = new System.Drawing.Size(181, 23);
            this.ConverMode.TabIndex = 11;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(24, 343);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(82, 15);
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
            this.ConverModeButton.Location = new System.Drawing.Point(314, 340);
            this.ConverModeButton.Name = "ConverModeButton";
            this.ConverModeButton.Size = new System.Drawing.Size(75, 23);
            this.ConverModeButton.TabIndex = 13;
            this.ConverModeButton.Text = "写入";
            this.ConverModeButton.UseVisualStyleBackColor = true;
            this.ConverModeButton.Click += new System.EventHandler(this.ConverModeButton_Click);
            // 
            // PM2_5CFG
            // 
            this.PM2_5CFG.FormattingEnabled = true;
            this.PM2_5CFG.Location = new System.Drawing.Point(15, 24);
            this.PM2_5CFG.Name = "PM2_5CFG";
            this.PM2_5CFG.Size = new System.Drawing.Size(121, 23);
            this.PM2_5CFG.TabIndex = 14;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.SeatsNumCFG);
            this.groupBox1.Controls.Add(this.ADASCFG);
            this.groupBox1.Controls.Add(this.button5);
            this.groupBox1.Controls.Add(this.HidetextBox);
            this.groupBox1.Controls.Add(this.CfgWriteButton);
            this.groupBox1.Controls.Add(this.CfgReadButton);
            this.groupBox1.Controls.Add(this.HeatPumpTypeCfg);
            this.groupBox1.Controls.Add(this.RefrigerateTypeCfg);
            this.groupBox1.Controls.Add(this.AreaCfg);
            this.groupBox1.Controls.Add(this.WarmAreaCfg);
            this.groupBox1.Controls.Add(this.DriveMotorTypeCfg);
            this.groupBox1.Controls.Add(this.SteeringRudderTypeCfg);
            this.groupBox1.Controls.Add(this.AgsCFG);
            this.groupBox1.Controls.Add(this.Valve3DspCFG);
            this.groupBox1.Controls.Add(this.FragranceSystemCFG);
            this.groupBox1.Controls.Add(this.AnionCFG);
            this.groupBox1.Controls.Add(this.AQSCFG);
            this.groupBox1.Controls.Add(this.PM2_5CFG);
            this.groupBox1.Location = new System.Drawing.Point(27, 589);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(975, 159);
            this.groupBox1.TabIndex = 15;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "配置字";
            // 
            // SeatsNumCFG
            // 
            this.SeatsNumCFG.FormattingEnabled = true;
            this.SeatsNumCFG.Location = new System.Drawing.Point(159, 118);
            this.SeatsNumCFG.Name = "SeatsNumCFG";
            this.SeatsNumCFG.Size = new System.Drawing.Size(121, 23);
            this.SeatsNumCFG.TabIndex = 31;
            // 
            // ADASCFG
            // 
            this.ADASCFG.FormattingEnabled = true;
            this.ADASCFG.Location = new System.Drawing.Point(15, 118);
            this.ADASCFG.Name = "ADASCFG";
            this.ADASCFG.Size = new System.Drawing.Size(121, 23);
            this.ADASCFG.TabIndex = 30;
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(681, 132);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(121, 27);
            this.button5.TabIndex = 29;
            this.button5.Text = "V11国内默认值";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // HidetextBox
            // 
            this.HidetextBox.Location = new System.Drawing.Point(0, 24);
            this.HidetextBox.Name = "HidetextBox";
            this.HidetextBox.ReadOnly = true;
            this.HidetextBox.Size = new System.Drawing.Size(10, 25);
            this.HidetextBox.TabIndex = 28;
            this.HidetextBox.Visible = false;
            // 
            // CfgWriteButton
            // 
            this.CfgWriteButton.Location = new System.Drawing.Point(808, 132);
            this.CfgWriteButton.Name = "CfgWriteButton";
            this.CfgWriteButton.Size = new System.Drawing.Size(121, 27);
            this.CfgWriteButton.TabIndex = 27;
            this.CfgWriteButton.Text = "写入";
            this.CfgWriteButton.UseVisualStyleBackColor = true;
            this.CfgWriteButton.Click += new System.EventHandler(this.CfgWriteButton_Click);
            // 
            // CfgReadButton
            // 
            this.CfgReadButton.Location = new System.Drawing.Point(808, 99);
            this.CfgReadButton.Name = "CfgReadButton";
            this.CfgReadButton.Size = new System.Drawing.Size(121, 27);
            this.CfgReadButton.TabIndex = 26;
            this.CfgReadButton.Text = "读取";
            this.CfgReadButton.UseVisualStyleBackColor = true;
            this.CfgReadButton.Click += new System.EventHandler(this.CfgReadButton_Click);
            // 
            // HeatPumpTypeCfg
            // 
            this.HeatPumpTypeCfg.FormattingEnabled = true;
            this.HeatPumpTypeCfg.Location = new System.Drawing.Point(808, 70);
            this.HeatPumpTypeCfg.Name = "HeatPumpTypeCfg";
            this.HeatPumpTypeCfg.Size = new System.Drawing.Size(121, 23);
            this.HeatPumpTypeCfg.TabIndex = 25;
            // 
            // RefrigerateTypeCfg
            // 
            this.RefrigerateTypeCfg.FormattingEnabled = true;
            this.RefrigerateTypeCfg.Location = new System.Drawing.Point(587, 70);
            this.RefrigerateTypeCfg.Name = "RefrigerateTypeCfg";
            this.RefrigerateTypeCfg.Size = new System.Drawing.Size(171, 23);
            this.RefrigerateTypeCfg.TabIndex = 24;
            // 
            // AreaCfg
            // 
            this.AreaCfg.FormattingEnabled = true;
            this.AreaCfg.Location = new System.Drawing.Point(445, 70);
            this.AreaCfg.Name = "AreaCfg";
            this.AreaCfg.Size = new System.Drawing.Size(121, 23);
            this.AreaCfg.TabIndex = 23;
            // 
            // WarmAreaCfg
            // 
            this.WarmAreaCfg.FormattingEnabled = true;
            this.WarmAreaCfg.Location = new System.Drawing.Point(303, 70);
            this.WarmAreaCfg.Name = "WarmAreaCfg";
            this.WarmAreaCfg.Size = new System.Drawing.Size(121, 23);
            this.WarmAreaCfg.TabIndex = 22;
            // 
            // DriveMotorTypeCfg
            // 
            this.DriveMotorTypeCfg.FormattingEnabled = true;
            this.DriveMotorTypeCfg.Location = new System.Drawing.Point(159, 70);
            this.DriveMotorTypeCfg.Name = "DriveMotorTypeCfg";
            this.DriveMotorTypeCfg.Size = new System.Drawing.Size(121, 23);
            this.DriveMotorTypeCfg.TabIndex = 21;
            // 
            // SteeringRudderTypeCfg
            // 
            this.SteeringRudderTypeCfg.FormattingEnabled = true;
            this.SteeringRudderTypeCfg.Location = new System.Drawing.Point(15, 70);
            this.SteeringRudderTypeCfg.Name = "SteeringRudderTypeCfg";
            this.SteeringRudderTypeCfg.Size = new System.Drawing.Size(121, 23);
            this.SteeringRudderTypeCfg.TabIndex = 20;
            // 
            // AgsCFG
            // 
            this.AgsCFG.FormattingEnabled = true;
            this.AgsCFG.Location = new System.Drawing.Point(808, 24);
            this.AgsCFG.Name = "AgsCFG";
            this.AgsCFG.Size = new System.Drawing.Size(121, 23);
            this.AgsCFG.TabIndex = 19;
            // 
            // Valve3DspCFG
            // 
            this.Valve3DspCFG.FormattingEnabled = true;
            this.Valve3DspCFG.Location = new System.Drawing.Point(587, 24);
            this.Valve3DspCFG.Name = "Valve3DspCFG";
            this.Valve3DspCFG.Size = new System.Drawing.Size(171, 23);
            this.Valve3DspCFG.TabIndex = 18;
            // 
            // FragranceSystemCFG
            // 
            this.FragranceSystemCFG.FormattingEnabled = true;
            this.FragranceSystemCFG.Location = new System.Drawing.Point(445, 24);
            this.FragranceSystemCFG.Name = "FragranceSystemCFG";
            this.FragranceSystemCFG.Size = new System.Drawing.Size(121, 23);
            this.FragranceSystemCFG.TabIndex = 17;
            // 
            // AnionCFG
            // 
            this.AnionCFG.FormattingEnabled = true;
            this.AnionCFG.Location = new System.Drawing.Point(303, 24);
            this.AnionCFG.Name = "AnionCFG";
            this.AnionCFG.Size = new System.Drawing.Size(121, 23);
            this.AnionCFG.TabIndex = 16;
            // 
            // AQSCFG
            // 
            this.AQSCFG.FormattingEnabled = true;
            this.AQSCFG.Location = new System.Drawing.Point(159, 24);
            this.AQSCFG.Name = "AQSCFG";
            this.AQSCFG.Size = new System.Drawing.Size(121, 23);
            this.AQSCFG.TabIndex = 15;
            // 
            // timer2
            // 
            this.timer2.Tick += new System.EventHandler(this.timer2_Tick);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(135, 37);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(75, 23);
            this.button2.TabIndex = 16;
            this.button2.Text = "读取";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.button3);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.button2);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Controls.Add(this.BootOrApp);
            this.groupBox2.Controls.Add(this.ConverModeTextBox);
            this.groupBox2.Location = new System.Drawing.Point(420, 268);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(232, 124);
            this.groupBox2.TabIndex = 17;
            this.groupBox2.TabStop = false;
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(135, 90);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(75, 23);
            this.button3.TabIndex = 17;
            this.button3.Text = "读取";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click_1);
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(685, 360);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(90, 23);
            this.button4.TabIndex = 19;
            this.button4.Text = "获取含义";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // NRC_textBox
            // 
            this.NRC_textBox.Location = new System.Drawing.Point(685, 329);
            this.NRC_textBox.Name = "NRC_textBox";
            this.NRC_textBox.Size = new System.Drawing.Size(100, 25);
            this.NRC_textBox.TabIndex = 18;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(686, 305);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(99, 15);
            this.label5.TabIndex = 20;
            this.label5.Text = "负响应码(0x)";
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.button6);
            this.groupBox3.Controls.Add(this.label6);
            this.groupBox3.Controls.Add(this.label7);
            this.groupBox3.Controls.Add(this.SoftWareVersion);
            this.groupBox3.Controls.Add(this.HardWareVersion);
            this.groupBox3.Location = new System.Drawing.Point(801, 279);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(201, 105);
            this.groupBox3.TabIndex = 21;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "软硬件版本号";
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(57, 74);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(122, 23);
            this.button6.TabIndex = 18;
            this.button6.Text = "读取";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(6, 50);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(45, 15);
            this.label6.TabIndex = 12;
            this.label6.Text = "硬件:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(6, 22);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(45, 15);
            this.label7.TabIndex = 10;
            this.label7.Text = "软件:";
            // 
            // SoftWareVersion
            // 
            this.SoftWareVersion.Location = new System.Drawing.Point(57, 19);
            this.SoftWareVersion.Name = "SoftWareVersion";
            this.SoftWareVersion.ReadOnly = true;
            this.SoftWareVersion.Size = new System.Drawing.Size(122, 25);
            this.SoftWareVersion.TabIndex = 11;
            // 
            // HardWareVersion
            // 
            this.HardWareVersion.Location = new System.Drawing.Point(57, 46);
            this.HardWareVersion.Name = "HardWareVersion";
            this.HardWareVersion.ReadOnly = true;
            this.HardWareVersion.Size = new System.Drawing.Size(122, 25);
            this.HardWareVersion.TabIndex = 13;
            // 
            // button7
            // 
            this.button7.Location = new System.Drawing.Point(169, 371);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(110, 23);
            this.button7.TabIndex = 22;
            this.button7.Text = "复位";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // button8
            // 
            this.button8.Location = new System.Drawing.Point(285, 371);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(104, 23);
            this.button8.TabIndex = 23;
            this.button8.Text = "解密";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // button9
            // 
            this.button9.Location = new System.Drawing.Point(6, 24);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(121, 23);
            this.button9.TabIndex = 24;
            this.button9.Text = "部件控制（2F）";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // button10
            // 
            this.button10.Location = new System.Drawing.Point(31, 371);
            this.button10.Name = "button10";
            this.button10.Size = new System.Drawing.Size(132, 23);
            this.button10.TabIndex = 25;
            this.button10.Text = "解除SWD保护";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.label13);
            this.groupBox4.Controls.Add(this.SystemNameOrEngineType);
            this.groupBox4.Controls.Add(this.label12);
            this.groupBox4.Controls.Add(this.BootVersion);
            this.groupBox4.Controls.Add(this.label11);
            this.groupBox4.Controls.Add(this.TryProgrameNum);
            this.groupBox4.Controls.Add(this.label10);
            this.groupBox4.Controls.Add(this.ProgrameNum);
            this.groupBox4.Controls.Add(this.button11);
            this.groupBox4.Controls.Add(this.label8);
            this.groupBox4.Controls.Add(this.label9);
            this.groupBox4.Controls.Add(this.ManufacturerSparePartNumber);
            this.groupBox4.Controls.Add(this.ProgrameDate);
            this.groupBox4.Location = new System.Drawing.Point(31, 412);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(621, 157);
            this.groupBox4.TabIndex = 26;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "软硬件版本号";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(363, 57);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(75, 15);
            this.label13.TabIndex = 25;
            this.label13.Text = "产品型号:";
            // 
            // SystemNameOrEngineType
            // 
            this.SystemNameOrEngineType.Location = new System.Drawing.Point(481, 50);
            this.SystemNameOrEngineType.Name = "SystemNameOrEngineType";
            this.SystemNameOrEngineType.ReadOnly = true;
            this.SystemNameOrEngineType.Size = new System.Drawing.Size(118, 25);
            this.SystemNameOrEngineType.TabIndex = 26;
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(363, 26);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(92, 15);
            this.label12.TabIndex = 23;
            this.label12.Text = "Boot版本号:";
            // 
            // BootVersion
            // 
            this.BootVersion.Location = new System.Drawing.Point(481, 19);
            this.BootVersion.Name = "BootVersion";
            this.BootVersion.ReadOnly = true;
            this.BootVersion.Size = new System.Drawing.Size(118, 25);
            this.BootVersion.TabIndex = 24;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(6, 112);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(105, 15);
            this.label11.TabIndex = 21;
            this.label11.Text = "尝试编程次数:";
            // 
            // TryProgrameNum
            // 
            this.TryProgrameNum.Location = new System.Drawing.Point(148, 109);
            this.TryProgrameNum.Name = "TryProgrameNum";
            this.TryProgrameNum.ReadOnly = true;
            this.TryProgrameNum.Size = new System.Drawing.Size(210, 25);
            this.TryProgrameNum.TabIndex = 22;
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(6, 81);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(105, 15);
            this.label10.TabIndex = 19;
            this.label10.Text = "实际编程次数:";
            // 
            // ProgrameNum
            // 
            this.ProgrameNum.Location = new System.Drawing.Point(148, 78);
            this.ProgrameNum.Name = "ProgrameNum";
            this.ProgrameNum.ReadOnly = true;
            this.ProgrameNum.Size = new System.Drawing.Size(210, 25);
            this.ProgrameNum.TabIndex = 20;
            // 
            // button11
            // 
            this.button11.Location = new System.Drawing.Point(366, 109);
            this.button11.Name = "button11";
            this.button11.Size = new System.Drawing.Size(122, 23);
            this.button11.TabIndex = 18;
            this.button11.Text = "读取";
            this.button11.UseVisualStyleBackColor = true;
            this.button11.Click += new System.EventHandler(this.button11_Click);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(6, 50);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(90, 15);
            this.label8.TabIndex = 12;
            this.label8.Text = "重编程日期:";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(6, 22);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(60, 15);
            this.label9.TabIndex = 10;
            this.label9.Text = "物料号:";
            // 
            // ManufacturerSparePartNumber
            // 
            this.ManufacturerSparePartNumber.Location = new System.Drawing.Point(148, 19);
            this.ManufacturerSparePartNumber.Name = "ManufacturerSparePartNumber";
            this.ManufacturerSparePartNumber.ReadOnly = true;
            this.ManufacturerSparePartNumber.Size = new System.Drawing.Size(210, 25);
            this.ManufacturerSparePartNumber.TabIndex = 11;
            // 
            // ProgrameDate
            // 
            this.ProgrameDate.Location = new System.Drawing.Point(148, 47);
            this.ProgrameDate.Name = "ProgrameDate";
            this.ProgrameDate.ReadOnly = true;
            this.ProgrameDate.Size = new System.Drawing.Size(210, 25);
            this.ProgrameDate.TabIndex = 13;
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.button12);
            this.groupBox5.Controls.Add(this.button9);
            this.groupBox5.Location = new System.Drawing.Point(658, 415);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(344, 154);
            this.groupBox5.TabIndex = 27;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "诊断服务/测试帧";
            // 
            // button12
            // 
            this.button12.Location = new System.Drawing.Point(6, 54);
            this.button12.Name = "button12";
            this.button12.Size = new System.Drawing.Size(121, 23);
            this.button12.TabIndex = 25;
            this.button12.Text = "故障读取（19）";
            this.button12.UseVisualStyleBackColor = true;
            this.button12.Click += new System.EventHandler(this.button12_Click);
            // 
            // UDS_Service
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1029, 760);
            this.Controls.Add(this.groupBox5);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.button10);
            this.Controls.Add(this.button8);
            this.Controls.Add(this.button7);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.NRC_textBox);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.ConverModeButton);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.ConverMode);
            this.Controls.Add(this.Check3EServer);
            this.Controls.Add(this.WriteVIN);
            this.Controls.Add(this.TxOrRxData);
            this.Controls.Add(this.VIN);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button1);
            this.Name = "UDS_Service";
            this.Text = "UDS";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.UDS_FormClosing);
            this.Load += new System.EventHandler(this.UDS_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.groupBox5.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox VIN;
        private System.Windows.Forms.TextBox TxOrRxData;
        private System.Windows.Forms.Button WriteVIN;
        private System.Windows.Forms.TextBox BootOrApp;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox ConverModeTextBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox Check3EServer;
        private System.Windows.Forms.ComboBox ConverMode;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Button ConverModeButton;
        private System.Windows.Forms.ComboBox PM2_5CFG;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ComboBox AQSCFG;
        private System.Windows.Forms.ComboBox AnionCFG;
        private System.Windows.Forms.ComboBox FragranceSystemCFG;
        private System.Windows.Forms.ComboBox Valve3DspCFG;
        private System.Windows.Forms.ComboBox SteeringRudderTypeCfg;
        private System.Windows.Forms.ComboBox AgsCFG;
        private System.Windows.Forms.ComboBox HeatPumpTypeCfg;
        private System.Windows.Forms.ComboBox RefrigerateTypeCfg;
        private System.Windows.Forms.ComboBox AreaCfg;
        private System.Windows.Forms.ComboBox WarmAreaCfg;
        private System.Windows.Forms.ComboBox DriveMotorTypeCfg;
        private System.Windows.Forms.Button CfgWriteButton;
        private System.Windows.Forms.Button CfgReadButton;
        private System.Windows.Forms.Timer timer2;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.TextBox HidetextBox;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.TextBox NRC_textBox;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox SoftWareVersion;
        private System.Windows.Forms.TextBox HardWareVersion;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Button button8;
        private System.Windows.Forms.Button button9;
        private System.Windows.Forms.ComboBox SeatsNumCFG;
        private System.Windows.Forms.ComboBox ADASCFG;
        private System.Windows.Forms.Button button10;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.Button button11;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox ManufacturerSparePartNumber;
        private System.Windows.Forms.TextBox ProgrameDate;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox ProgrameNum;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.TextBox TryProgrameNum;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.TextBox BootVersion;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.TextBox SystemNameOrEngineType;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.Button button12;
    }
}