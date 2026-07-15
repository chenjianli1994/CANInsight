namespace PCAN_Client.Update
{
    partial class update
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
            this.S19Addr = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.XmlPath = new System.Windows.Forms.TextBox();
            this.HardVersion = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.SoftVersion = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.button_UpdateStart = new System.Windows.Forms.Button();
            this.ShowText = new System.Windows.Forms.TextBox();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.button1 = new System.Windows.Forms.Button();
            this.comboBox_CanoeChannel = new System.Windows.Forms.ComboBox();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.radioButton_CANOE = new System.Windows.Forms.RadioButton();
            this.radioButton_PCAN = new System.Windows.Forms.RadioButton();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.CmdType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Physical = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Inhibition = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Request = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Response = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Delay = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Enable = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.buttonLoadXML = new System.Windows.Forms.Button();
            this.buttonSaveXML = new System.Windows.Forms.Button();
            this.button_MoveDown = new System.Windows.Forms.Button();
            this.button_MoveUp = new System.Windows.Forms.Button();
            this.button_Delete = new System.Windows.Forms.Button();
            this.button_Add = new System.Windows.Forms.Button();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.VQ_New = new System.Windows.Forms.RadioButton();
            this.radioButtonVQ = new System.Windows.Forms.RadioButton();
            this.radioButtonLP = new System.Windows.Forms.RadioButton();
            this.UDS_TX_ID = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.UDS_RX_ID = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.textBoxDelayus = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.comboBox_JsonSelect = new System.Windows.Forms.ComboBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.radioButtonCAN = new System.Windows.Forms.RadioButton();
            this.radioButtonCANFD = new System.Windows.Forms.RadioButton();
            this.label8 = new System.Windows.Forms.Label();
            this.DLC = new System.Windows.Forms.ComboBox();
            this.BootVersion = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.SuspendLayout();
            // 
            // S19Addr
            // 
            this.S19Addr.Location = new System.Drawing.Point(218, 9);
            this.S19Addr.Multiline = true;
            this.S19Addr.Name = "S19Addr";
            this.S19Addr.Size = new System.Drawing.Size(437, 33);
            this.S19Addr.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 18);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(178, 15);
            this.label1.TabIndex = 1;
            this.label1.Text = "APP S19/Srec文件地址：";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(661, 18);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(138, 15);
            this.label2.TabIndex = 2;
            this.label2.Text = "XML/Tmp文件地址：";
            // 
            // XmlPath
            // 
            this.XmlPath.Location = new System.Drawing.Point(829, 9);
            this.XmlPath.Multiline = true;
            this.XmlPath.Name = "XmlPath";
            this.XmlPath.Size = new System.Drawing.Size(382, 33);
            this.XmlPath.TabIndex = 3;
            // 
            // HardVersion
            // 
            this.HardVersion.Location = new System.Drawing.Point(445, 76);
            this.HardVersion.Name = "HardVersion";
            this.HardVersion.Size = new System.Drawing.Size(80, 25);
            this.HardVersion.TabIndex = 5;
            this.HardVersion.TextChanged += new System.EventHandler(this.HardVersion_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(356, 81);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(82, 15);
            this.label3.TabIndex = 4;
            this.label3.Text = "硬件版本：";
            // 
            // SoftVersion
            // 
            this.SoftVersion.Location = new System.Drawing.Point(445, 46);
            this.SoftVersion.Name = "SoftVersion";
            this.SoftVersion.Size = new System.Drawing.Size(80, 25);
            this.SoftVersion.TabIndex = 7;
            this.SoftVersion.TextChanged += new System.EventHandler(this.SoftVersion_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(356, 51);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(82, 15);
            this.label4.TabIndex = 6;
            this.label4.Text = "软件版本：";
            // 
            // button_UpdateStart
            // 
            this.button_UpdateStart.Location = new System.Drawing.Point(829, 55);
            this.button_UpdateStart.Name = "button_UpdateStart";
            this.button_UpdateStart.Size = new System.Drawing.Size(382, 77);
            this.button_UpdateStart.TabIndex = 8;
            this.button_UpdateStart.Text = "开始刷写";
            this.button_UpdateStart.UseVisualStyleBackColor = true;
            this.button_UpdateStart.Click += new System.EventHandler(this.buttonUpdateStart_Click);
            // 
            // ShowText
            // 
            this.ShowText.Location = new System.Drawing.Point(219, 138);
            this.ShowText.MaxLength = 10485760;
            this.ShowText.Multiline = true;
            this.ShowText.Name = "ShowText";
            this.ShowText.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.ShowText.Size = new System.Drawing.Size(992, 288);
            this.ShowText.TabIndex = 9;
            // 
            // progressBar1
            // 
            this.progressBar1.BackColor = System.Drawing.SystemColors.ControlLight;
            this.progressBar1.Location = new System.Drawing.Point(829, 45);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(382, 10);
            this.progressBar1.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.progressBar1.TabIndex = 10;
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.button1);
            this.groupBox1.Controls.Add(this.comboBox_CanoeChannel);
            this.groupBox1.Controls.Add(this.comboBox1);
            this.groupBox1.Controls.Add(this.radioButton_CANOE);
            this.groupBox1.Controls.Add(this.radioButton_PCAN);
            this.groupBox1.Location = new System.Drawing.Point(12, 45);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(338, 70);
            this.groupBox1.TabIndex = 11;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "CAN通道选择";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(259, 20);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(73, 43);
            this.button1.TabIndex = 47;
            this.button1.Text = "连接";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // comboBox_CanoeChannel
            // 
            this.comboBox_CanoeChannel.FormattingEnabled = true;
            this.comboBox_CanoeChannel.Location = new System.Drawing.Point(6, 43);
            this.comboBox_CanoeChannel.Name = "comboBox_CanoeChannel";
            this.comboBox_CanoeChannel.Size = new System.Drawing.Size(172, 23);
            this.comboBox_CanoeChannel.TabIndex = 2;
            this.comboBox_CanoeChannel.SelectedIndexChanged += new System.EventHandler(this.comboBox_CanoeChannel_SelectedIndexChanged);
            this.comboBox_CanoeChannel.Click += new System.EventHandler(this.comboBox_CanoeChannel_Click);
            // 
            // comboBox1
            // 
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(6, 18);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(172, 23);
            this.comboBox1.TabIndex = 2;
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            this.comboBox1.Click += new System.EventHandler(this.comboBox1_Click);
            // 
            // radioButton_CANOE
            // 
            this.radioButton_CANOE.AutoSize = true;
            this.radioButton_CANOE.Location = new System.Drawing.Point(185, 45);
            this.radioButton_CANOE.Name = "radioButton_CANOE";
            this.radioButton_CANOE.Size = new System.Drawing.Size(68, 19);
            this.radioButton_CANOE.TabIndex = 13;
            this.radioButton_CANOE.TabStop = true;
            this.radioButton_CANOE.Text = "CANOE";
            this.radioButton_CANOE.UseVisualStyleBackColor = true;
            this.radioButton_CANOE.CheckedChanged += new System.EventHandler(this.radioButton_CANOE_CheckedChanged);
            this.radioButton_CANOE.Click += new System.EventHandler(this.radioButton_CANOE_Click);
            // 
            // radioButton_PCAN
            // 
            this.radioButton_PCAN.AutoSize = true;
            this.radioButton_PCAN.Location = new System.Drawing.Point(185, 20);
            this.radioButton_PCAN.Name = "radioButton_PCAN";
            this.radioButton_PCAN.Size = new System.Drawing.Size(68, 19);
            this.radioButton_PCAN.TabIndex = 12;
            this.radioButton_PCAN.TabStop = true;
            this.radioButton_PCAN.Text = "P-CAN";
            this.radioButton_PCAN.UseVisualStyleBackColor = true;
            this.radioButton_PCAN.CheckedChanged += new System.EventHandler(this.radioButton_PCAN_CheckedChanged);
            this.radioButton_PCAN.Click += new System.EventHandler(this.radioButton_PCAN_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.CmdType,
            this.Physical,
            this.Inhibition,
            this.Request,
            this.Response,
            this.Delay,
            this.Enable});
            this.dataGridView1.Location = new System.Drawing.Point(0, 461);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 51;
            this.dataGridView1.RowTemplate.Height = 27;
            this.dataGridView1.Size = new System.Drawing.Size(1211, 401);
            this.dataGridView1.TabIndex = 13;
            this.dataGridView1.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            // 
            // CmdType
            // 
            this.CmdType.HeaderText = "指令类型";
            this.CmdType.MinimumWidth = 6;
            this.CmdType.Name = "CmdType";
            this.CmdType.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.CmdType.Width = 150;
            // 
            // Physical
            // 
            this.Physical.HeaderText = "物理地址";
            this.Physical.MinimumWidth = 6;
            this.Physical.Name = "Physical";
            this.Physical.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Physical.Width = 125;
            // 
            // Inhibition
            // 
            this.Inhibition.HeaderText = "抑制响应";
            this.Inhibition.MinimumWidth = 6;
            this.Inhibition.Name = "Inhibition";
            this.Inhibition.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Inhibition.Width = 125;
            // 
            // Request
            // 
            this.Request.HeaderText = "Request(hex)";
            this.Request.MinimumWidth = 6;
            this.Request.Name = "Request";
            this.Request.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Request.Width = 250;
            // 
            // Response
            // 
            this.Response.HeaderText = "Response(hex)";
            this.Response.MinimumWidth = 6;
            this.Response.Name = "Response";
            this.Response.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Response.Width = 250;
            // 
            // Delay
            // 
            this.Delay.HeaderText = "Delay(ms)";
            this.Delay.MinimumWidth = 6;
            this.Delay.Name = "Delay";
            this.Delay.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Delay.Width = 85;
            // 
            // Enable
            // 
            this.Enable.HeaderText = "Enable";
            this.Enable.MinimumWidth = 6;
            this.Enable.Name = "Enable";
            this.Enable.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.Enable.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Enable.Width = 125;
            // 
            // buttonLoadXML
            // 
            this.buttonLoadXML.Location = new System.Drawing.Point(139, 432);
            this.buttonLoadXML.Name = "buttonLoadXML";
            this.buttonLoadXML.Size = new System.Drawing.Size(118, 23);
            this.buttonLoadXML.TabIndex = 19;
            this.buttonLoadXML.Text = "Load json";
            this.buttonLoadXML.UseVisualStyleBackColor = true;
            this.buttonLoadXML.Click += new System.EventHandler(this.buttonLoadXML_Click);
            // 
            // buttonSaveXML
            // 
            this.buttonSaveXML.Location = new System.Drawing.Point(15, 432);
            this.buttonSaveXML.Name = "buttonSaveXML";
            this.buttonSaveXML.Size = new System.Drawing.Size(118, 23);
            this.buttonSaveXML.TabIndex = 18;
            this.buttonSaveXML.Text = "Save as";
            this.buttonSaveXML.UseVisualStyleBackColor = true;
            this.buttonSaveXML.Click += new System.EventHandler(this.buttonSaveXML_Click);
            // 
            // button_MoveDown
            // 
            this.button_MoveDown.Location = new System.Drawing.Point(1163, 432);
            this.button_MoveDown.Name = "button_MoveDown";
            this.button_MoveDown.Size = new System.Drawing.Size(48, 23);
            this.button_MoveDown.TabIndex = 17;
            this.button_MoveDown.Text = "Down";
            this.button_MoveDown.UseVisualStyleBackColor = true;
            this.button_MoveDown.Click += new System.EventHandler(this.button_MoveDown_Click);
            // 
            // button_MoveUp
            // 
            this.button_MoveUp.Location = new System.Drawing.Point(1109, 432);
            this.button_MoveUp.Name = "button_MoveUp";
            this.button_MoveUp.Size = new System.Drawing.Size(48, 23);
            this.button_MoveUp.TabIndex = 16;
            this.button_MoveUp.Text = "Up";
            this.button_MoveUp.UseVisualStyleBackColor = true;
            this.button_MoveUp.Click += new System.EventHandler(this.button_MoveUp_Click);
            // 
            // button_Delete
            // 
            this.button_Delete.Location = new System.Drawing.Point(1055, 432);
            this.button_Delete.Name = "button_Delete";
            this.button_Delete.Size = new System.Drawing.Size(48, 23);
            this.button_Delete.TabIndex = 15;
            this.button_Delete.Text = "Del";
            this.button_Delete.UseVisualStyleBackColor = true;
            this.button_Delete.Click += new System.EventHandler(this.button_Delete_Click);
            // 
            // button_Add
            // 
            this.button_Add.Location = new System.Drawing.Point(1001, 432);
            this.button_Add.Name = "button_Add";
            this.button_Add.Size = new System.Drawing.Size(48, 23);
            this.button_Add.TabIndex = 14;
            this.button_Add.Text = "Add";
            this.button_Add.UseVisualStyleBackColor = true;
            this.button_Add.Click += new System.EventHandler(this.button_Add_Click);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.VQ_New);
            this.groupBox2.Controls.Add(this.radioButtonVQ);
            this.groupBox2.Controls.Add(this.radioButtonLP);
            this.groupBox2.Location = new System.Drawing.Point(531, 48);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(104, 84);
            this.groupBox2.TabIndex = 25;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "项目选择";
            // 
            // VQ_New
            // 
            this.VQ_New.AutoSize = true;
            this.VQ_New.Location = new System.Drawing.Point(6, 60);
            this.VQ_New.Name = "VQ_New";
            this.VQ_New.Size = new System.Drawing.Size(81, 19);
            this.VQ_New.TabIndex = 2;
            this.VQ_New.TabStop = true;
            this.VQ_New.Text = "客户-新";
            this.VQ_New.UseVisualStyleBackColor = true;
            this.VQ_New.Click += new System.EventHandler(this.VQ_New_Click);
            // 
            // radioButtonVQ
            // 
            this.radioButtonVQ.AutoSize = true;
            this.radioButtonVQ.Location = new System.Drawing.Point(6, 40);
            this.radioButtonVQ.Name = "radioButtonVQ";
            this.radioButtonVQ.Size = new System.Drawing.Size(81, 19);
            this.radioButtonVQ.TabIndex = 1;
            this.radioButtonVQ.TabStop = true;
            this.radioButtonVQ.Text = "客户-旧";
            this.radioButtonVQ.UseVisualStyleBackColor = true;
            this.radioButtonVQ.Click += new System.EventHandler(this.radioButtonVQ_Click);
            // 
            // radioButtonLP
            // 
            this.radioButtonLP.AutoSize = true;
            this.radioButtonLP.Location = new System.Drawing.Point(6, 19);
            this.radioButtonLP.Name = "radioButtonLP";
            this.radioButtonLP.Size = new System.Drawing.Size(96, 19);
            this.radioButtonLP.TabIndex = 0;
            this.radioButtonLP.TabStop = true;
            this.radioButtonLP.Text = "客户B平台";
            this.radioButtonLP.UseVisualStyleBackColor = true;
            this.radioButtonLP.Click += new System.EventHandler(this.radioButtonLP_Click);
            // 
            // UDS_TX_ID
            // 
            this.UDS_TX_ID.Location = new System.Drawing.Point(777, 102);
            this.UDS_TX_ID.Name = "UDS_TX_ID";
            this.UDS_TX_ID.Size = new System.Drawing.Size(42, 25);
            this.UDS_TX_ID.TabIndex = 35;
            this.UDS_TX_ID.TextChanged += new System.EventHandler(this.UDS_TX_ID_TextChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(640, 108);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(131, 15);
            this.label6.TabIndex = 34;
            this.label6.Text = "物理寻址Tx(hex):";
            // 
            // UDS_RX_ID
            // 
            this.UDS_RX_ID.Location = new System.Drawing.Point(777, 55);
            this.UDS_RX_ID.Name = "UDS_RX_ID";
            this.UDS_RX_ID.Size = new System.Drawing.Size(42, 25);
            this.UDS_RX_ID.TabIndex = 33;
            this.UDS_RX_ID.TextChanged += new System.EventHandler(this.UDS_RX_ID_TextChanged);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(641, 64);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(131, 15);
            this.label7.TabIndex = 32;
            this.label7.Text = "物理寻址Rx(hex):";
            // 
            // textBoxDelayus
            // 
            this.textBoxDelayus.Location = new System.Drawing.Point(112, 190);
            this.textBoxDelayus.Name = "textBoxDelayus";
            this.textBoxDelayus.Size = new System.Drawing.Size(95, 25);
            this.textBoxDelayus.TabIndex = 36;
            this.textBoxDelayus.TextChanged += new System.EventHandler(this.textBoxDelayus_TextChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(15, 193);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(99, 15);
            this.label5.TabIndex = 37;
            this.label5.Text = "帧间隔(us)：";
            // 
            // comboBox_JsonSelect
            // 
            this.comboBox_JsonSelect.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_JsonSelect.FormattingEnabled = true;
            this.comboBox_JsonSelect.Location = new System.Drawing.Point(263, 432);
            this.comboBox_JsonSelect.Name = "comboBox_JsonSelect";
            this.comboBox_JsonSelect.Size = new System.Drawing.Size(157, 23);
            this.comboBox_JsonSelect.TabIndex = 38;
            this.comboBox_JsonSelect.SelectedIndexChanged += new System.EventHandler(this.comboBox_JsonSelect_SelectedIndexChanged);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.radioButtonCAN);
            this.groupBox3.Controls.Add(this.radioButtonCANFD);
            this.groupBox3.Location = new System.Drawing.Point(12, 131);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(201, 53);
            this.groupBox3.TabIndex = 46;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "CAN类型";
            // 
            // radioButtonCAN
            // 
            this.radioButtonCAN.AutoSize = true;
            this.radioButtonCAN.Location = new System.Drawing.Point(143, 19);
            this.radioButtonCAN.Name = "radioButtonCAN";
            this.radioButtonCAN.Size = new System.Drawing.Size(52, 19);
            this.radioButtonCAN.TabIndex = 1;
            this.radioButtonCAN.TabStop = true;
            this.radioButtonCAN.Text = "CAN";
            this.radioButtonCAN.UseVisualStyleBackColor = true;
            this.radioButtonCAN.Click += new System.EventHandler(this.radioButtonCAN_Click);
            // 
            // radioButtonCANFD
            // 
            this.radioButtonCANFD.AutoSize = true;
            this.radioButtonCANFD.Location = new System.Drawing.Point(6, 19);
            this.radioButtonCANFD.Name = "radioButtonCANFD";
            this.radioButtonCANFD.Size = new System.Drawing.Size(68, 19);
            this.radioButtonCANFD.TabIndex = 0;
            this.radioButtonCANFD.TabStop = true;
            this.radioButtonCANFD.Text = "CANFD";
            this.radioButtonCANFD.UseVisualStyleBackColor = true;
            this.radioButtonCANFD.Click += new System.EventHandler(this.radioButtonCANFD_Click);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(15, 224);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(94, 15);
            this.label8.TabIndex = 48;
            this.label8.Text = "DLC(Byte)：";
            // 
            // DLC
            // 
            this.DLC.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DLC.FormattingEnabled = true;
            this.DLC.Location = new System.Drawing.Point(112, 220);
            this.DLC.Name = "DLC";
            this.DLC.Size = new System.Drawing.Size(95, 23);
            this.DLC.TabIndex = 49;
            this.DLC.SelectedIndexChanged += new System.EventHandler(this.DLC_SelectedIndexChanged);
            // 
            // BootVersion
            // 
            this.BootVersion.Location = new System.Drawing.Point(445, 107);
            this.BootVersion.Name = "BootVersion";
            this.BootVersion.Size = new System.Drawing.Size(80, 25);
            this.BootVersion.TabIndex = 51;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(356, 112);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(84, 15);
            this.label9.TabIndex = 50;
            this.label9.Text = "Boot版本：";
            // 
            // update
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1223, 874);
            this.Controls.Add(this.BootVersion);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.DLC);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.comboBox_JsonSelect);
            this.Controls.Add(this.textBoxDelayus);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.UDS_TX_ID);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.UDS_RX_ID);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.buttonLoadXML);
            this.Controls.Add(this.buttonSaveXML);
            this.Controls.Add(this.button_MoveDown);
            this.Controls.Add(this.button_MoveUp);
            this.Controls.Add(this.button_Delete);
            this.Controls.Add(this.button_Add);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.ShowText);
            this.Controls.Add(this.button_UpdateStart);
            this.Controls.Add(this.SoftVersion);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.HardVersion);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.XmlPath);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.S19Addr);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "update";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "update";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.update_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.update_FormClosed);
            this.Load += new System.EventHandler(this.update_Load);
            this.SizeChanged += new System.EventHandler(this.update_SizeChanged);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.update_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.update_DragEnter);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox S19Addr;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox XmlPath;
        private System.Windows.Forms.TextBox HardVersion;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox SoftVersion;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button button_UpdateStart;
        private System.Windows.Forms.TextBox ShowText;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton radioButton_PCAN;
        private System.Windows.Forms.RadioButton radioButton_CANOE;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button buttonLoadXML;
        private System.Windows.Forms.Button buttonSaveXML;
        private System.Windows.Forms.Button button_MoveDown;
        private System.Windows.Forms.Button button_MoveUp;
        private System.Windows.Forms.Button button_Delete;
        private System.Windows.Forms.Button button_Add;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.RadioButton radioButtonVQ;
        private System.Windows.Forms.RadioButton radioButtonLP;
        private System.Windows.Forms.TextBox UDS_TX_ID;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox UDS_RX_ID;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox textBoxDelayus;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox comboBox_JsonSelect;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.ComboBox comboBox_CanoeChannel;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.RadioButton radioButtonCAN;
        private System.Windows.Forms.RadioButton radioButtonCANFD;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.DataGridViewTextBoxColumn CmdType;
        private System.Windows.Forms.DataGridViewTextBoxColumn Physical;
        private System.Windows.Forms.DataGridViewTextBoxColumn Inhibition;
        private System.Windows.Forms.DataGridViewTextBoxColumn Request;
        private System.Windows.Forms.DataGridViewTextBoxColumn Response;
        private System.Windows.Forms.DataGridViewTextBoxColumn Delay;
        private System.Windows.Forms.DataGridViewTextBoxColumn Enable;
        private System.Windows.Forms.RadioButton VQ_New;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.ComboBox DLC;
        private System.Windows.Forms.TextBox BootVersion;
        private System.Windows.Forms.Label label9;
    }
}