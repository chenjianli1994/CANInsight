using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PCAN_Client
{
    /// <summary>
    /// 日志文件条目（包含路径和启用状态）
    /// </summary>
    public class LogFileEntry
    {
        public string Path { get; set; }
        public bool Enabled { get; set; }
    }
    
    /// <summary>
    /// 报文路径管理对话框 - 支持单个或批量导入BLF/BIN/ASC文件
    /// </summary>
    public class LogFileListDialog : Form
    {
        private DataGridView _dgvFiles;
        private Button _btnAddFiles;
        private Button _btnAddFolder;
        private Button _btnRemoveSelected;
        private Button _btnClearAll;
        private Button _btnOk;
        private Button _btnCancel;
        private Label _lblTitle;
        private Label _lblStatus;

        private const string ColEnabled = "colEnabled";
        private const string ColFilePath = "colFilePath";
        private const string ColFileSize = "colFileSize";

        /// <summary>
        /// 所有文件条目（包含勾选状态）
        /// </summary>
        public List<LogFileEntry> AllEntries { get; private set; } = new List<LogFileEntry>();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="existingEntries">现有文件条目列表（用于编辑已有配置）</param>
        public LogFileListDialog(List<LogFileEntry> existingEntries = null)
        {
            InitializeComponents();
            
            if (existingEntries != null && existingEntries.Count > 0)
            {
                foreach (var entry in existingEntries)
                {
                    AddFileToGrid(entry.Path, entry.Enabled);
                }
            }
            
            UpdateStatus();
        }

        private void InitializeComponents()
        {
            this.Text = "报文路径管理";
            this.Size = new Size(700, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // 标题标签
            _lblTitle = new Label
            {
                Text = "管理报文文件列表（勾选的文件将在播放时读取）",
                Location = new Point(12, 12),
                Size = new Size(660, 23),
                Font = new Font("Microsoft YaHei", 9F)
            };
            this.Controls.Add(_lblTitle);

            // DataGridView
            _dgvFiles = new DataGridView
            {
                Location = new Point(12, 40),
                Size = new Size(660, 400),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false
            };

            // 列：勾选框
            var colEnabled = new DataGridViewCheckBoxColumn
            {
                Name = ColEnabled,
                HeaderText = "启用",
                FillWeight = 10,
                TrueValue = true,
                FalseValue = false
            };
            _dgvFiles.Columns.Add(colEnabled);

            // 列：文件路径
            var colFilePath = new DataGridViewTextBoxColumn
            {
                Name = ColFilePath,
                HeaderText = "文件路径",
                FillWeight = 70,
                ReadOnly = true
            };
            _dgvFiles.Columns.Add(colFilePath);

            // 列：文件大小
            var colFileSize = new DataGridViewTextBoxColumn
            {
                Name = ColFileSize,
                HeaderText = "大小(MB)",
                FillWeight = 20,
                ReadOnly = true
            };
            _dgvFiles.Columns.Add(colFileSize);

            _dgvFiles.CellValueChanged += DgvFiles_CellValueChanged;
            _dgvFiles.CurrentCellDirtyStateChanged += DgvFiles_CurrentCellDirtyStateChanged;
            this.Controls.Add(_dgvFiles);

            // 状态标签
            _lblStatus = new Label
            {
                Location = new Point(12, 445),
                Size = new Size(660, 20),
                Font = new Font("Microsoft YaHei", 9F),
                ForeColor = Color.Blue
            };
            this.Controls.Add(_lblStatus);

            // 按钮面板
            var btnPanel = new Panel
            {
                Location = new Point(12, 470),
                Size = new Size(660, 35)
            };
            this.Controls.Add(btnPanel);

            _btnAddFiles = new Button
            {
                Text = "添加文件",
                Location = new Point(0, 5),
                Size = new Size(90, 28)
            };
            _btnAddFiles.Click += BtnAddFiles_Click;
            btnPanel.Controls.Add(_btnAddFiles);

            _btnAddFolder = new Button
            {
                Text = "添加文件夹",
                Location = new Point(95, 5),
                Size = new Size(90, 28)
            };
            _btnAddFolder.Click += BtnAddFolder_Click;
            btnPanel.Controls.Add(_btnAddFolder);

            _btnRemoveSelected = new Button
            {
                Text = "删除选中",
                Location = new Point(190, 5),
                Size = new Size(90, 28)
            };
            _btnRemoveSelected.Click += BtnRemoveSelected_Click;
            btnPanel.Controls.Add(_btnRemoveSelected);

            _btnClearAll = new Button
            {
                Text = "清空",
                Location = new Point(285, 5),
                Size = new Size(90, 28)
            };
            _btnClearAll.Click += BtnClearAll_Click;
            btnPanel.Controls.Add(_btnClearAll);

            _btnOk = new Button
            {
                Text = "确定",
                Location = new Point(480, 5),
                Size = new Size(80, 28),
                DialogResult = DialogResult.None
            };
            _btnOk.Click += BtnOk_Click;
            btnPanel.Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(565, 5),
                Size = new Size(80, 28),
                DialogResult = DialogResult.Cancel
            };
            btnPanel.Controls.Add(_btnCancel);

            this.CancelButton = _btnCancel;
        }

        private void DgvFiles_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_dgvFiles.IsCurrentCellDirty)
            {
                _dgvFiles.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void DgvFiles_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                UpdateStatus();
            }
        }

        private void AddFileToGrid(string filePath, bool enabled = true)
        {
            if (!File.Exists(filePath)) return;

            // 检查是否已存在
            foreach (DataGridViewRow row in _dgvFiles.Rows)
            {
                if (row.Cells[ColFilePath].Value?.ToString() == filePath)
                    return; // 已存在，不重复添加
            }

            var fileInfo = new FileInfo(filePath);
            double sizeMB = fileInfo.Length / (1024.0 * 1024.0);

            int rowIndex = _dgvFiles.Rows.Add();
            _dgvFiles.Rows[rowIndex].Cells[ColEnabled].Value = enabled;
            _dgvFiles.Rows[rowIndex].Cells[ColFilePath].Value = filePath;
            _dgvFiles.Rows[rowIndex].Cells[ColFileSize].Value = sizeMB.ToString("F2");
        }

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "报文文件|*.blf;*.bin;*.asc|BLF文件|*.blf|BIN文件|*.bin|ASC文件|*.asc|所有文件|*.*";
                ofd.Title = "选择报文文件";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    foreach (var file in ofd.FileNames)
                    {
                        AddFileToGrid(file, true);
                    }
                    UpdateStatus();
                }
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择包含报文文件的文件夹";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    var extensions = new[] { ".blf", ".bin", ".asc" };
                    var files = Directory.GetFiles(fbd.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()));

                    foreach (var file in files)
                    {
                        AddFileToGrid(file, true);
                    }
                    UpdateStatus();
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, EventArgs e)
        {
            var selectedRows = _dgvFiles.SelectedRows.Cast<DataGridViewRow>().ToList();
            foreach (var row in selectedRows)
            {
                _dgvFiles.Rows.Remove(row);
            }
            UpdateStatus();
        }

        private void BtnClearAll_Click(object sender, EventArgs e)
        {
            _dgvFiles.Rows.Clear();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int total = _dgvFiles.Rows.Count;
            int enabled = 0;
            double totalSize = 0;

            foreach (DataGridViewRow row in _dgvFiles.Rows)
            {
                bool isEnabled = (bool)(row.Cells[ColEnabled].Value ?? false);
                if (isEnabled)
                {
                    enabled++;
                    if (double.TryParse(row.Cells[ColFileSize].Value?.ToString(), out double size))
                    {
                        totalSize += size;
                    }
                }
            }

            _lblStatus.Text = $"共 {total} 个文件，已启用 {enabled} 个，总大小 {totalSize:F2} MB";
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            AllEntries.Clear();

            foreach (DataGridViewRow row in _dgvFiles.Rows)
            {
                bool isEnabled = (bool)(row.Cells[ColEnabled].Value ?? false);
                string filePath = row.Cells[ColFilePath].Value?.ToString();
                if (!string.IsNullOrEmpty(filePath))
                {
                    AllEntries.Add(new LogFileEntry { Path = filePath, Enabled = isEnabled });
                }
            }

            // 允许0个勾选/空列表确定:表示清空报文路径,由调用方处理(不加载)
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
