namespace PCAN_Client
{
    partial class SignalSelector
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.chkSelectAll = new System.Windows.Forms.CheckBox();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.lblSearch = new System.Windows.Forms.Label();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.tvMessages = new System.Windows.Forms.TreeView();
            this.dgvSignals = new System.Windows.Forms.DataGridView();
            this.colSignalName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSignalComment = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colMessageId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colMsgIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSignalIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSelect = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.panelSelected = new System.Windows.Forms.Panel();
            this.grpSelected = new System.Windows.Forms.GroupBox();
            this.lstSelected = new System.Windows.Forms.ListBox();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnConfirm = new System.Windows.Forms.Button();
            this.tableLayoutPanel1.SuspendLayout();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvSignals)).BeginInit();
            this.panelSelected.SuspendLayout();
            this.grpSelected.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.panelTop, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.splitContainer1, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.panelSelected, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.panelBottom, 0, 3);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 4;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(900, 595);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.lblTitle);
            this.panelTop.Controls.Add(this.txtSearch);
            this.panelTop.Controls.Add(this.lblSearch);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelTop.Location = new System.Drawing.Point(3, 3);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(894, 54);
            this.panelTop.TabIndex = 0;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(12, 30);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(109, 22);
            this.lblTitle.TabIndex = 2;
            this.lblTitle.Text = "CAN信号选择";
            // 
            // txtSearch
            // 
            this.txtSearch.Location = new System.Drawing.Point(140, 5);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(250, 21);
            this.txtSearch.TabIndex = 1;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            this.txtSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtSearch_KeyDown);
            // 
            // lblSearch
            // 
            this.lblSearch.AutoSize = true;
            this.lblSearch.Location = new System.Drawing.Point(12, 8);
            this.lblSearch.Name = "lblSearch";
            this.lblSearch.TabIndex = 0;
            this.lblSearch.Text = "信号搜索(回车键触发)：";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(3, 63);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.tvMessages);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.dgvSignals);
            this.splitContainer1.Panel2.Controls.Add(this.chkSelectAll);
            // 
            // chkSelectAll
            // 
            this.chkSelectAll.AutoSize = true;
            this.chkSelectAll.Checked = false;
            this.chkSelectAll.Dock = System.Windows.Forms.DockStyle.Top;
            this.chkSelectAll.Location = new System.Drawing.Point(0, 0);
            this.chkSelectAll.Name = "chkSelectAll";
            this.chkSelectAll.Padding = new System.Windows.Forms.Padding(5, 2, 0, 2);
            this.chkSelectAll.Size = new System.Drawing.Size(610, 22);
            this.chkSelectAll.TabIndex = 1;
            this.chkSelectAll.Text = "全选当前报文信号";
            this.chkSelectAll.UseVisualStyleBackColor = true;
            this.chkSelectAll.CheckedChanged += new System.EventHandler(this.chkSelectAll_CheckedChanged);
            this.splitContainer1.Size = new System.Drawing.Size(894, 369);
            this.splitContainer1.SplitterDistance = 280;
            this.splitContainer1.TabIndex = 1;
            // 
            // tvMessages
            // 
            this.tvMessages.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tvMessages.Location = new System.Drawing.Point(0, 0);
            this.tvMessages.Name = "tvMessages";
            this.tvMessages.Size = new System.Drawing.Size(280, 369);
            this.tvMessages.TabIndex = 0;
            this.tvMessages.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.tvMessages_AfterSelect);
            // 
            // dgvSignals
            // 
            this.dgvSignals.AllowUserToAddRows = false;
            this.dgvSignals.AllowUserToDeleteRows = false;
            this.dgvSignals.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvSignals.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvSignals.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colSignalName,
            this.colSignalComment,
            this.colMessageId,
            this.colMsgIndex,
            this.colSignalIndex,
            this.colSelect});
            this.dgvSignals.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvSignals.Location = new System.Drawing.Point(0, 0);
            this.dgvSignals.Name = "dgvSignals";
            this.dgvSignals.ReadOnly = false;
            this.dgvSignals.RowHeadersVisible = false;
            this.dgvSignals.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvSignals.Size = new System.Drawing.Size(610, 369);
            this.dgvSignals.TabIndex = 0;
            this.dgvSignals.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvSignals_CellValueChanged);
            this.dgvSignals.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvSignals_CellContentClick);
            this.dgvSignals.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvSignals_CellDoubleClick);
            this.dgvSignals.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dgvSignals_CellMouseDown);
            this.dgvSignals.CurrentCellDirtyStateChanged += new System.EventHandler(this.dgvSignals_CurrentCellDirtyStateChanged);
            // 
            // colSignalName
            // 
            this.colSignalName.HeaderText = "信号名";
            this.colSignalName.Name = "colSignalName";
            // 
            // colSignalComment
            // 
            this.colSignalComment.HeaderText = "中文注释";
            this.colSignalComment.Name = "colSignalComment";
            // 
            // colMessageId
            // 
            this.colMessageId.HeaderText = "MessageId";
            this.colMessageId.Name = "colMessageId";
            this.colMessageId.ReadOnly = true;
            this.colMessageId.Width = 80;
            // 
            // colMsgIndex
            // 
            this.colMsgIndex.HeaderText = "MsgIndex";
            this.colMsgIndex.Name = "colMsgIndex";
            this.colMsgIndex.Width = 80;
            // 
            // colSignalIndex
            // 
            this.colSignalIndex.HeaderText = "SignalIndex";
            this.colSignalIndex.Name = "colSignalIndex";
            this.colSignalIndex.Width = 80;
            // 
            // colSelect
            // 
            this.colSelect.HeaderText = "选择";
            this.colSelect.Name = "colSelect";
            this.colSelect.Width = 50;
            // 
            // panelSelected
            // 
            this.panelSelected.Controls.Add(this.grpSelected);
            this.panelSelected.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelSelected.Location = new System.Drawing.Point(3, 438);
            this.panelSelected.Name = "panelSelected";
            this.panelSelected.Size = new System.Drawing.Size(894, 74);
            this.panelSelected.TabIndex = 2;
            // 
            // grpSelected
            // 
            this.grpSelected.Controls.Add(this.lstSelected);
            this.grpSelected.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpSelected.Location = new System.Drawing.Point(0, 0);
            this.grpSelected.Name = "grpSelected";
            this.grpSelected.Size = new System.Drawing.Size(894, 74);
            this.grpSelected.TabIndex = 0;
            this.grpSelected.TabStop = false;
            this.grpSelected.Text = "已选择的信号";
            // 
            // lstSelected
            // 
            this.lstSelected.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstSelected.FormattingEnabled = true;
            this.lstSelected.HorizontalScrollbar = true;
            this.lstSelected.Location = new System.Drawing.Point(3, 17);
            this.lstSelected.Name = "lstSelected";
            this.lstSelected.ScrollAlwaysVisible = true;
            this.lstSelected.Size = new System.Drawing.Size(888, 54);
            this.lstSelected.TabIndex = 0;
            // 
            // panelBottom
            // 
            this.panelBottom.Controls.Add(this.btnCancel);
            this.panelBottom.Controls.Add(this.btnConfirm);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelBottom.Location = new System.Drawing.Point(3, 518);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(894, 34);
            this.panelBottom.TabIndex = 3;
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(800, 2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(85, 30);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnConfirm
            // 
            this.btnConfirm.Location = new System.Drawing.Point(700, 2);
            this.btnConfirm.Name = "btnConfirm";
            this.btnConfirm.Size = new System.Drawing.Size(85, 30);
            this.btnConfirm.TabIndex = 0;
            this.btnConfirm.Text = "确认选择";
            this.btnConfirm.UseVisualStyleBackColor = true;
            this.btnConfirm.Click += new System.EventHandler(this.btnConfirm_Click);
            // 
            // SignalSelector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 595);
            this.Controls.Add(this.tableLayoutPanel1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SignalSelector";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "CAN信号选择器";
            this.Load += new System.EventHandler(this.SignalSelector_Load);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvSignals)).EndInit();
            this.panelSelected.ResumeLayout(false);
            this.grpSelected.ResumeLayout(false);
            this.panelBottom.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Label lblSearch;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TreeView tvMessages;
        private System.Windows.Forms.DataGridView dgvSignals;
        private System.Windows.Forms.Panel panelSelected;
        private System.Windows.Forms.GroupBox grpSelected;
        private System.Windows.Forms.ListBox lstSelected;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSignalName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSignalComment;
        private System.Windows.Forms.DataGridViewTextBoxColumn colMessageId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colMsgIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSignalIndex;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colSelect;
        private System.Windows.Forms.CheckBox chkSelectAll;
    }
}