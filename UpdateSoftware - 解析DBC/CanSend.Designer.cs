namespace PCAN_Client
{
    partial class CanSend
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false</param>
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
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.CmdType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Physical = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RawValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.dataGridView2 = new System.Windows.Forms.DataGridView();
            this.MessageID = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dataGridViewTextBoxColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SendCnt = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Enable = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.saveCfgButton = new System.Windows.Forms.Button();
            this.readCfgButton = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).BeginInit();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.SuspendLayout();
            //
            // treeView1
            //
            this.treeView1.Location = new System.Drawing.Point(9, 36);
            this.treeView1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(282, 286);
            this.treeView1.TabIndex = 51;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            this.treeView1.DoubleClick += new System.EventHandler(this.treeView1_DoubleClick);
            //
            // dataGridView1
            //
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.CmdType,
            this.Physical,
            this.RawValue});
            this.dataGridView1.Location = new System.Drawing.Point(295, 36);
            this.dataGridView1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 51;
            this.dataGridView1.RowTemplate.Height = 27;
            this.dataGridView1.Size = new System.Drawing.Size(598, 286);
            this.dataGridView1.TabIndex = 56;
            this.dataGridView1.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellClick_1);
            this.dataGridView1.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellEndEdit);
            this.dataGridView1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.dataGridView1_KeyDown_1);
            //
            // CmdType
            //
            this.CmdType.HeaderText = "SignalName";
            this.CmdType.MinimumWidth = 6;
            this.CmdType.Name = "CmdType";
            this.CmdType.Width = 300;
            //
            // Physical
            //
            this.Physical.HeaderText = "Value";
            this.Physical.MinimumWidth = 6;
            this.Physical.Name = "Physical";
            this.Physical.Width = 350;
            //
            // RawValue
            //
            this.RawValue.HeaderText = "RawValue";
            this.RawValue.MinimumWidth = 6;
            this.RawValue.Name = "RawValue";
            this.RawValue.Width = 75;
            //
            // timer1
            //
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            //
            // dataGridView2
            //
            this.dataGridView2.AllowUserToAddRows = false;
            this.dataGridView2.AllowUserToDeleteRows = false;
            this.dataGridView2.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView2.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.MessageID,
            this.dataGridViewTextBoxColumn1,
            this.dataGridViewTextBoxColumn2,
            this.SendCnt,
            this.Enable});
            this.dataGridView2.Location = new System.Drawing.Point(4, 5);
            this.dataGridView2.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.dataGridView2.Name = "dataGridView2";
            this.dataGridView2.RowHeadersWidth = 51;
            this.dataGridView2.RowTemplate.Height = 27;
            this.dataGridView2.Size = new System.Drawing.Size(872, 322);
            this.dataGridView2.TabIndex = 57;
            this.dataGridView2.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGridView2_CellClick);
            this.dataGridView2.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView2_CellEndEdit);
            //
            // MessageID
            //
            this.MessageID.HeaderText = "MessageID";
            this.MessageID.MinimumWidth = 6;
            this.MessageID.Name = "MessageID";
            this.MessageID.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.MessageID.Width = 125;
            //
            // dataGridViewTextBoxColumn1
            //
            this.dataGridViewTextBoxColumn1.HeaderText = "MessageName";
            this.dataGridViewTextBoxColumn1.MinimumWidth = 6;
            this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            this.dataGridViewTextBoxColumn1.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.dataGridViewTextBoxColumn1.Width = 150;
            //
            // dataGridViewTextBoxColumn2
            //
            this.dataGridViewTextBoxColumn2.HeaderText = "CycleTime(ms)";
            this.dataGridViewTextBoxColumn2.MinimumWidth = 6;
            this.dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
            this.dataGridViewTextBoxColumn2.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.dataGridViewTextBoxColumn2.Width = 150;
            //
            // SendCnt
            //
            this.SendCnt.HeaderText = "SendCnt";
            this.SendCnt.MinimumWidth = 6;
            this.SendCnt.Name = "SendCnt";
            this.SendCnt.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.SendCnt.Width = 125;
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
            // saveCfgButton
            //
            this.saveCfgButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.saveCfgButton.Location = new System.Drawing.Point(741, 10);
            this.saveCfgButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.saveCfgButton.Name = "saveCfgButton";
            this.saveCfgButton.Size = new System.Drawing.Size(74, 22);
            this.saveCfgButton.TabIndex = 58;
            this.saveCfgButton.Text = "保存配置";
            this.saveCfgButton.UseVisualStyleBackColor = true;
            this.saveCfgButton.Click += new System.EventHandler(this.saveCfgButton_Click);
            //
            // readCfgButton
            //
            this.readCfgButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.readCfgButton.Location = new System.Drawing.Point(819, 10);
            this.readCfgButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.readCfgButton.Name = "readCfgButton";
            this.readCfgButton.Size = new System.Drawing.Size(74, 22);
            this.readCfgButton.TabIndex = 59;
            this.readCfgButton.Text = "加载配置";
            this.readCfgButton.UseVisualStyleBackColor = true;
            this.readCfgButton.Click += new System.EventHandler(this.readCfgButton_Click);
            //
            // tabControl1
            //
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Location = new System.Drawing.Point(9, 326);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(886, 354);
            this.tabControl1.TabIndex = 63;
            //
            // tabPage1
            //
            this.tabPage1.Controls.Add(this.dataGridView2);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.tabPage1.Size = new System.Drawing.Size(878, 328);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "发送列表";
            this.tabPage1.UseVisualStyleBackColor = true;
            //
            // CanSend
            //
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(902, 682);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.readCfgButton);
            this.Controls.Add(this.saveCfgButton);
            this.Controls.Add(this.treeView1);
            this.Controls.Add(this.dataGridView1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MaximizeBox = false;
            this.Name = "CanSend";
            this.Text = "报文发送";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.CanSend_FormClosed);
            this.Load += new System.EventHandler(this.CanSend_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.CanSend_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.CanSend_DragEnter);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).EndInit();
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.DataGridView dataGridView2;
        private System.Windows.Forms.DataGridViewTextBoxColumn MessageID;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
        private System.Windows.Forms.DataGridViewTextBoxColumn SendCnt;
        private System.Windows.Forms.DataGridViewCheckBoxColumn Enable;
        private System.Windows.Forms.DataGridViewTextBoxColumn CmdType;
        private System.Windows.Forms.DataGridViewTextBoxColumn Physical;
        private System.Windows.Forms.DataGridViewTextBoxColumn RawValue;
        private System.Windows.Forms.Button saveCfgButton;
        private System.Windows.Forms.Button readCfgButton;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
    }
}
