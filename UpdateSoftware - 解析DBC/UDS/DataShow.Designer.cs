namespace PCAN_Client.UDS
{
    partial class DataShow
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
            this.listView1 = new System.Windows.Forms.ListView();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.radioButton_R134a = new System.Windows.Forms.RadioButton();
            this.radioButton_1234yf = new System.Windows.Forms.RadioButton();
            this.SuspendLayout();
            // 
            // listView1
            // 
            this.listView1.HideSelection = false;
            this.listView1.Location = new System.Drawing.Point(425, 41);
            this.listView1.Name = "listView1";
            this.listView1.Size = new System.Drawing.Size(1056, 772);
            this.listView1.TabIndex = 52;
            this.listView1.UseCompatibleStateImageBehavior = false;
            // 
            // treeView1
            // 
            this.treeView1.Location = new System.Drawing.Point(12, 41);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(407, 772);
            this.treeView1.TabIndex = 51;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            // 
            // timer1
            // 
            this.timer1.Interval = 500;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // radioButton_R134a
            // 
            this.radioButton_R134a.AutoSize = true;
            this.radioButton_R134a.Location = new System.Drawing.Point(425, 16);
            this.radioButton_R134a.Name = "radioButton_R134a";
            this.radioButton_R134a.Size = new System.Drawing.Size(68, 19);
            this.radioButton_R134a.TabIndex = 53;
            this.radioButton_R134a.TabStop = true;
            this.radioButton_R134a.Text = "R134a";
            this.radioButton_R134a.UseVisualStyleBackColor = true;
            // 
            // radioButton_1234yf
            // 
            this.radioButton_1234yf.AutoSize = true;
            this.radioButton_1234yf.Location = new System.Drawing.Point(521, 16);
            this.radioButton_1234yf.Name = "radioButton_1234yf";
            this.radioButton_1234yf.Size = new System.Drawing.Size(76, 19);
            this.radioButton_1234yf.TabIndex = 54;
            this.radioButton_1234yf.TabStop = true;
            this.radioButton_1234yf.Text = "1234yf";
            this.radioButton_1234yf.UseVisualStyleBackColor = true;
            // 
            // DataShow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1496, 824);
            this.Controls.Add(this.radioButton_1234yf);
            this.Controls.Add(this.radioButton_R134a);
            this.Controls.Add(this.listView1);
            this.Controls.Add(this.treeView1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Name = "DataShow";
            this.Text = "DataShow";
            this.Load += new System.EventHandler(this.DataShow_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListView listView1;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.RadioButton radioButton_R134a;
        private System.Windows.Forms.RadioButton radioButton_1234yf;
    }
}