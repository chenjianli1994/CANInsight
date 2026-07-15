namespace PCAN_Client
{
    partial class Main
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            this.button1 = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.button3 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.comboBox_CanoeChannel = new System.Windows.Forms.ComboBox();
            this.button5 = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.radioButtonCAN = new System.Windows.Forms.RadioButton();
            this.radioButtonCANFD = new System.Windows.Forms.RadioButton();
            this.textBox_path = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.button_load = new System.Windows.Forms.Button();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.listView1 = new System.Windows.Forms.ListView();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.SendMsg = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.button7 = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.textBox_InputSwVer = new System.Windows.Forms.TextBox();
            this.textBox_InputHwVer = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.textBox_ReadSwVer = new System.Windows.Forms.TextBox();
            this.textBox_ReadHwVer = new System.Windows.Forms.TextBox();
            this.radioButtonLP = new System.Windows.Forms.RadioButton();
            this.radioButtonVQ = new System.Windows.Forms.RadioButton();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.radioButtonVQNew = new System.Windows.Forms.RadioButton();
            this.PartResult = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.textBox_ReadPartVer = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.textBox_InputPartVer = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.HwResult = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.SwResult = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.radioButton1 = new System.Windows.Forms.RadioButton();
            this.radioButton2 = new System.Windows.Forms.RadioButton();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.textBox__txtIdFilter = new System.Windows.Forms.TextBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(135, 18);
            this.button1.Margin = new System.Windows.Forms.Padding(2);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(56, 18);
            this.button1.TabIndex = 0;
            this.button1.Text = "连接";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(1225, 232);
            this.textBox1.Margin = new System.Windows.Forms.Padding(2);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBox1.Size = new System.Drawing.Size(46, 76);
            this.textBox1.TabIndex = 1;
            this.textBox1.Visible = false;
            // 
            // comboBox1
            // 
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(4, 19);
            this.comboBox1.Margin = new System.Windows.Forms.Padding(2);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(127, 20);
            this.comboBox1.TabIndex = 2;
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            this.comboBox1.Click += new System.EventHandler(this.comboBox1_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(482, 5);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(74, 28);
            this.button3.TabIndex = 40;
            this.button3.Text = "存储数据";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(396, 5);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(74, 28);
            this.button2.TabIndex = 41;
            this.button2.Text = "UDS";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(316, 5);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(74, 28);
            this.button4.TabIndex = 42;
            this.button4.Text = "程序刷写";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.comboBox1);
            this.groupBox1.Controls.Add(this.button1);
            this.groupBox1.Location = new System.Drawing.Point(9, 10);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox1.Size = new System.Drawing.Size(208, 51);
            this.groupBox1.TabIndex = 43;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "PCAN";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.comboBox_CanoeChannel);
            this.groupBox2.Controls.Add(this.button5);
            this.groupBox2.Location = new System.Drawing.Point(9, 66);
            this.groupBox2.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox2.Size = new System.Drawing.Size(208, 49);
            this.groupBox2.TabIndex = 44;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "CANOE";
            // 
            // comboBox_CanoeChannel
            // 
            this.comboBox_CanoeChannel.FormattingEnabled = true;
            this.comboBox_CanoeChannel.Location = new System.Drawing.Point(4, 19);
            this.comboBox_CanoeChannel.Margin = new System.Windows.Forms.Padding(2);
            this.comboBox_CanoeChannel.Name = "comboBox_CanoeChannel";
            this.comboBox_CanoeChannel.Size = new System.Drawing.Size(127, 20);
            this.comboBox_CanoeChannel.TabIndex = 2;
            this.comboBox_CanoeChannel.SelectedIndexChanged += new System.EventHandler(this.comboBox_CanoeChannel_SelectedIndexChanged);
            this.comboBox_CanoeChannel.Click += new System.EventHandler(this.comboBox_CanoeChannel_Click);
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(135, 19);
            this.button5.Margin = new System.Windows.Forms.Padding(2);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(56, 18);
            this.button5.TabIndex = 0;
            this.button5.Text = "连接";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.radioButtonCAN);
            this.groupBox3.Controls.Add(this.radioButtonCANFD);
            this.groupBox3.Location = new System.Drawing.Point(222, 10);
            this.groupBox3.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox3.Size = new System.Drawing.Size(62, 105);
            this.groupBox3.TabIndex = 45;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "CAN类型";
            // 
            // radioButtonCAN
            // 
            this.radioButtonCAN.AutoSize = true;
            this.radioButtonCAN.Location = new System.Drawing.Point(4, 76);
            this.radioButtonCAN.Margin = new System.Windows.Forms.Padding(2);
            this.radioButtonCAN.Name = "radioButtonCAN";
            this.radioButtonCAN.Size = new System.Drawing.Size(41, 16);
            this.radioButtonCAN.TabIndex = 1;
            this.radioButtonCAN.TabStop = true;
            this.radioButtonCAN.Text = "CAN";
            this.radioButtonCAN.UseVisualStyleBackColor = true;
            this.radioButtonCAN.Click += new System.EventHandler(this.radioButtonCAN_Click);
            // 
            // radioButtonCANFD
            // 
            this.radioButtonCANFD.AutoSize = true;
            this.radioButtonCANFD.Location = new System.Drawing.Point(4, 15);
            this.radioButtonCANFD.Margin = new System.Windows.Forms.Padding(2);
            this.radioButtonCANFD.Name = "radioButtonCANFD";
            this.radioButtonCANFD.Size = new System.Drawing.Size(53, 16);
            this.radioButtonCANFD.TabIndex = 0;
            this.radioButtonCANFD.TabStop = true;
            this.radioButtonCANFD.Text = "CANFD";
            this.radioButtonCANFD.UseVisualStyleBackColor = true;
            this.radioButtonCANFD.Click += new System.EventHandler(this.radioButtonCANFD_Click);
            // 
            // textBox_path
            // 
            this.textBox_path.Location = new System.Drawing.Point(877, 13);
            this.textBox_path.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_path.Name = "textBox_path";
            this.textBox_path.ReadOnly = true;
            this.textBox_path.Size = new System.Drawing.Size(206, 21);
            this.textBox_path.TabIndex = 48;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(814, 18);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 12);
            this.label1.TabIndex = 47;
            this.label1.Text = "Dbc文件：";
            // 
            // button_load
            // 
            this.button_load.Location = new System.Drawing.Point(1087, 13);
            this.button_load.Margin = new System.Windows.Forms.Padding(2);
            this.button_load.Name = "button_load";
            this.button_load.Size = new System.Drawing.Size(74, 22);
            this.button_load.TabIndex = 46;
            this.button_load.Text = "浏览";
            this.button_load.UseVisualStyleBackColor = true;
            this.button_load.Click += new System.EventHandler(this.button_load_Click);
            // 
            // treeView1
            // 
            this.treeView1.Location = new System.Drawing.Point(9, 634);
            this.treeView1.Margin = new System.Windows.Forms.Padding(2);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(69, 12);
            this.treeView1.TabIndex = 49;
            this.treeView1.Visible = false;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            // 
            // listView1
            // 
            this.listView1.HideSelection = false;
            this.listView1.Location = new System.Drawing.Point(323, 330);
            this.listView1.Margin = new System.Windows.Forms.Padding(2);
            this.listView1.Name = "listView1";
            this.listView1.Size = new System.Drawing.Size(33, 30);
            this.listView1.TabIndex = 50;
            this.listView1.UseCompatibleStateImageBehavior = false;
            this.listView1.Visible = false;
            this.listView1.DoubleClick += new System.EventHandler(this.listView1_DoubleClick);
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // SendMsg
            // 
            this.SendMsg.Location = new System.Drawing.Point(316, 53);
            this.SendMsg.Name = "SendMsg";
            this.SendMsg.Size = new System.Drawing.Size(74, 28);
            this.SendMsg.TabIndex = 51;
            this.SendMsg.Text = "发送报文";
            this.SendMsg.UseVisualStyleBackColor = true;
            this.SendMsg.Click += new System.EventHandler(this.SendMsg_Click);
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(396, 53);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(74, 28);
            this.button6.TabIndex = 52;
            this.button6.Text = "曲线绘制";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button7
            // 
            this.button7.Location = new System.Drawing.Point(482, 53);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(74, 28);
            this.button7.TabIndex = 53;
            this.button7.Text = "数据转换";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(4, 20);
            this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(65, 12);
            this.label2.TabIndex = 55;
            this.label2.Text = "软件版本：";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(4, 38);
            this.label3.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(65, 12);
            this.label3.TabIndex = 56;
            this.label3.Text = "硬件版本：";
            // 
            // textBox_InputSwVer
            // 
            this.textBox_InputSwVer.Location = new System.Drawing.Point(65, 12);
            this.textBox_InputSwVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_InputSwVer.Name = "textBox_InputSwVer";
            this.textBox_InputSwVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_InputSwVer.TabIndex = 57;
            this.textBox_InputSwVer.TextChanged += new System.EventHandler(this.textBox_InputSwVer_TextChanged);
            // 
            // textBox_InputHwVer
            // 
            this.textBox_InputHwVer.Location = new System.Drawing.Point(65, 34);
            this.textBox_InputHwVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_InputHwVer.Name = "textBox_InputHwVer";
            this.textBox_InputHwVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_InputHwVer.TabIndex = 58;
            this.textBox_InputHwVer.TextChanged += new System.EventHandler(this.textBox_InputHwVer_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(224, 17);
            this.label4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(89, 12);
            this.label4.TabIndex = 59;
            this.label4.Text = "总线软件版本：";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(224, 38);
            this.label5.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(89, 12);
            this.label5.TabIndex = 60;
            this.label5.Text = "总线硬件版本：";
            // 
            // textBox_ReadSwVer
            // 
            this.textBox_ReadSwVer.Location = new System.Drawing.Point(310, 12);
            this.textBox_ReadSwVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_ReadSwVer.Name = "textBox_ReadSwVer";
            this.textBox_ReadSwVer.ReadOnly = true;
            this.textBox_ReadSwVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_ReadSwVer.TabIndex = 61;
            // 
            // textBox_ReadHwVer
            // 
            this.textBox_ReadHwVer.Location = new System.Drawing.Point(310, 34);
            this.textBox_ReadHwVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_ReadHwVer.Name = "textBox_ReadHwVer";
            this.textBox_ReadHwVer.ReadOnly = true;
            this.textBox_ReadHwVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_ReadHwVer.TabIndex = 62;
            // 
            // radioButtonLP
            // 
            this.radioButtonLP.AutoSize = true;
            this.radioButtonLP.Location = new System.Drawing.Point(145, 15);
            this.radioButtonLP.Margin = new System.Windows.Forms.Padding(2);
            this.radioButtonLP.Name = "radioButtonLP";
            this.radioButtonLP.Size = new System.Drawing.Size(77, 16);
            this.radioButtonLP.TabIndex = 63;
            this.radioButtonLP.TabStop = true;
            this.radioButtonLP.Text = "客户B平台";
            this.radioButtonLP.UseVisualStyleBackColor = true;
            this.radioButtonLP.Click += new System.EventHandler(this.radioButtonLP_Click);
            // 
            // radioButtonVQ
            // 
            this.radioButtonVQ.AutoSize = true;
            this.radioButtonVQ.Location = new System.Drawing.Point(145, 35);
            this.radioButtonVQ.Margin = new System.Windows.Forms.Padding(2);
            this.radioButtonVQ.Name = "radioButtonVQ";
            this.radioButtonVQ.Size = new System.Drawing.Size(71, 16);
            this.radioButtonVQ.TabIndex = 64;
            this.radioButtonVQ.TabStop = true;
            this.radioButtonVQ.Text = "客户-Old";
            this.radioButtonVQ.UseVisualStyleBackColor = true;
            this.radioButtonVQ.Click += new System.EventHandler(this.radioButtonVQ_Click);
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.radioButtonVQNew);
            this.groupBox4.Controls.Add(this.PartResult);
            this.groupBox4.Controls.Add(this.label8);
            this.groupBox4.Controls.Add(this.textBox_ReadPartVer);
            this.groupBox4.Controls.Add(this.label9);
            this.groupBox4.Controls.Add(this.textBox_InputPartVer);
            this.groupBox4.Controls.Add(this.label10);
            this.groupBox4.Controls.Add(this.HwResult);
            this.groupBox4.Controls.Add(this.label7);
            this.groupBox4.Controls.Add(this.SwResult);
            this.groupBox4.Controls.Add(this.label6);
            this.groupBox4.Controls.Add(this.radioButtonVQ);
            this.groupBox4.Controls.Add(this.radioButtonLP);
            this.groupBox4.Controls.Add(this.textBox_ReadHwVer);
            this.groupBox4.Controls.Add(this.textBox_ReadSwVer);
            this.groupBox4.Controls.Add(this.label5);
            this.groupBox4.Controls.Add(this.label4);
            this.groupBox4.Controls.Add(this.textBox_InputHwVer);
            this.groupBox4.Controls.Add(this.textBox_InputSwVer);
            this.groupBox4.Controls.Add(this.label3);
            this.groupBox4.Controls.Add(this.label2);
            this.groupBox4.Location = new System.Drawing.Point(650, 42);
            this.groupBox4.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox4.Size = new System.Drawing.Size(530, 86);
            this.groupBox4.TabIndex = 54;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "版本校验";
            // 
            // radioButtonVQNew
            // 
            this.radioButtonVQNew.AutoSize = true;
            this.radioButtonVQNew.Location = new System.Drawing.Point(145, 55);
            this.radioButtonVQNew.Margin = new System.Windows.Forms.Padding(2);
            this.radioButtonVQNew.Name = "radioButtonVQNew";
            this.radioButtonVQNew.Size = new System.Drawing.Size(71, 16);
            this.radioButtonVQNew.TabIndex = 75;
            this.radioButtonVQNew.TabStop = true;
            this.radioButtonVQNew.Text = "客户-New";
            this.radioButtonVQNew.UseVisualStyleBackColor = true;
            this.radioButtonVQNew.Click += new System.EventHandler(this.radioButtonVQNew_Click);
            // 
            // PartResult
            // 
            this.PartResult.Location = new System.Drawing.Point(478, 60);
            this.PartResult.Margin = new System.Windows.Forms.Padding(2);
            this.PartResult.Multiline = true;
            this.PartResult.Name = "PartResult";
            this.PartResult.ReadOnly = true;
            this.PartResult.Size = new System.Drawing.Size(48, 20);
            this.PartResult.TabIndex = 74;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(392, 65);
            this.label8.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(89, 12);
            this.label8.TabIndex = 73;
            this.label8.Text = "零件校验结果：";
            // 
            // textBox_ReadPartVer
            // 
            this.textBox_ReadPartVer.Location = new System.Drawing.Point(310, 58);
            this.textBox_ReadPartVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_ReadPartVer.Name = "textBox_ReadPartVer";
            this.textBox_ReadPartVer.ReadOnly = true;
            this.textBox_ReadPartVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_ReadPartVer.TabIndex = 72;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(224, 62);
            this.label9.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(89, 12);
            this.label9.TabIndex = 71;
            this.label9.Text = "总线零件总成：";
            // 
            // textBox_InputPartVer
            // 
            this.textBox_InputPartVer.Location = new System.Drawing.Point(65, 58);
            this.textBox_InputPartVer.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_InputPartVer.Name = "textBox_InputPartVer";
            this.textBox_InputPartVer.Size = new System.Drawing.Size(76, 21);
            this.textBox_InputPartVer.TabIndex = 70;
            this.textBox_InputPartVer.TextChanged += new System.EventHandler(this.textBox_InputPartVer_TextChanged);
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(4, 62);
            this.label10.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(65, 12);
            this.label10.TabIndex = 69;
            this.label10.Text = "零件总成：";
            // 
            // HwResult
            // 
            this.HwResult.Location = new System.Drawing.Point(478, 37);
            this.HwResult.Margin = new System.Windows.Forms.Padding(2);
            this.HwResult.Multiline = true;
            this.HwResult.Name = "HwResult";
            this.HwResult.ReadOnly = true;
            this.HwResult.Size = new System.Drawing.Size(48, 20);
            this.HwResult.TabIndex = 68;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(392, 42);
            this.label7.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(89, 12);
            this.label7.TabIndex = 67;
            this.label7.Text = "硬件校验结果：";
            // 
            // SwResult
            // 
            this.SwResult.Location = new System.Drawing.Point(478, 14);
            this.SwResult.Margin = new System.Windows.Forms.Padding(2);
            this.SwResult.Multiline = true;
            this.SwResult.Name = "SwResult";
            this.SwResult.ReadOnly = true;
            this.SwResult.Size = new System.Drawing.Size(48, 20);
            this.SwResult.TabIndex = 66;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(392, 19);
            this.label6.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(89, 12);
            this.label6.TabIndex = 65;
            this.label6.Text = "软件校验结果：";
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.radioButton1);
            this.groupBox5.Controls.Add(this.radioButton2);
            this.groupBox5.Location = new System.Drawing.Point(650, 4);
            this.groupBox5.Margin = new System.Windows.Forms.Padding(2);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Padding = new System.Windows.Forms.Padding(2);
            this.groupBox5.Size = new System.Drawing.Size(160, 34);
            this.groupBox5.TabIndex = 55;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "年款选择";
            // 
            // radioButton1
            // 
            this.radioButton1.AutoSize = true;
            this.radioButton1.Location = new System.Drawing.Point(86, 14);
            this.radioButton1.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton1.Name = "radioButton1";
            this.radioButton1.Size = new System.Drawing.Size(65, 16);
            this.radioButton1.TabIndex = 1;
            this.radioButton1.TabStop = true;
            this.radioButton1.Text = "26/27款";
            this.radioButton1.UseVisualStyleBackColor = true;
            this.radioButton1.Click += new System.EventHandler(this.radioButton1_Click);
            // 
            // radioButton2
            // 
            this.radioButton2.AutoSize = true;
            this.radioButton2.Location = new System.Drawing.Point(4, 15);
            this.radioButton2.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton2.Name = "radioButton2";
            this.radioButton2.Size = new System.Drawing.Size(47, 16);
            this.radioButton2.TabIndex = 0;
            this.radioButton2.TabStop = true;
            this.radioButton2.Text = "24款";
            this.radioButton2.UseVisualStyleBackColor = true;
            this.radioButton2.Click += new System.EventHandler(this.radioButton2_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(9, 133);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowTemplate.Height = 23;
            this.dataGridView1.Size = new System.Drawing.Size(1174, 496);
            this.dataGridView1.TabIndex = 56;
            // 
            // textBox__txtIdFilter
            // 
            this.textBox__txtIdFilter.Location = new System.Drawing.Point(316, 90);
            this.textBox__txtIdFilter.Margin = new System.Windows.Forms.Padding(2);
            this.textBox__txtIdFilter.Name = "textBox__txtIdFilter";
            this.textBox__txtIdFilter.Size = new System.Drawing.Size(141, 21);
            this.textBox__txtIdFilter.TabIndex = 71;
            // 
            // Main
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1193, 641);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.textBox__txtIdFilter);
            this.Controls.Add(this.groupBox5);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.button7);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.SendMsg);
            this.Controls.Add(this.textBox_path);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button_load);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.listView1);
            this.Controls.Add(this.treeView1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "Main";
            this.Text = "Main V1.00.01";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.Main_FormClosed);
            this.Load += new System.EventHandler(this.Main_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.ComboBox comboBox_CanoeChannel;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.RadioButton radioButtonCAN;
        private System.Windows.Forms.RadioButton radioButtonCANFD;
        private System.Windows.Forms.TextBox textBox_path;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button button_load;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.ListView listView1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Button SendMsg;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox textBox_InputSwVer;
        private System.Windows.Forms.TextBox textBox_InputHwVer;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox textBox_ReadSwVer;
        private System.Windows.Forms.TextBox textBox_ReadHwVer;
        private System.Windows.Forms.RadioButton radioButtonLP;
        private System.Windows.Forms.RadioButton radioButtonVQ;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.TextBox HwResult;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox SwResult;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox PartResult;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.TextBox textBox_ReadPartVer;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox textBox_InputPartVer;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.RadioButton radioButtonVQNew;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.RadioButton radioButton2;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.TextBox textBox__txtIdFilter;
    }
}

