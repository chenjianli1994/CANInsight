// 工况选择交互:树构建工具 + 工具栏快捷选择面板 + 工况管理对话框(分组管理)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PCAN_Client.ReportAuto
{
    /// <summary>树分组节点的Tag标记</summary>
    internal class GroupNodeTag
    {
        public string Group;
        public GroupNodeTag(string group) { Group = group; }
    }

    /// <summary>工况分组树的公共构建逻辑(供快捷面板和管理对话框复用)</summary>
    internal static class AnalysisTypeTreeHelper
    {
        public const string UngroupedDisplay = "未分组";
        public const int ImgFolder = 0, ImgDoc = 1;

        public static string GroupDisplay(string group)
        {
            return string.IsNullOrEmpty(group) ? UngroupedDisplay : group;
        }

        /// <summary>构建树用图标列表:0=文件夹,1=工况文档(运行时GDI绘制,不依赖外部资源)</summary>
        public static ImageList BuildImageList()
        {
            var list = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

            var folder = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(folder))
            {
                g.Clear(Color.Transparent);
                g.FillRectangle(new SolidBrush(Color.FromArgb(243, 196, 83)), 1, 3, 6, 3);
                g.FillRectangle(new SolidBrush(Color.FromArgb(250, 215, 120)), 1, 5, 14, 10);
                g.DrawRectangle(new Pen(Color.FromArgb(200, 160, 80)), 1, 5, 13, 9);
            }
            list.Images.Add(folder);

            var doc = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(doc))
            {
                g.Clear(Color.Transparent);
                g.FillRectangle(Brushes.White, 3, 1, 10, 14);
                g.DrawRectangle(Pens.Gray, 3, 1, 9, 13);
                g.DrawLine(new Pen(Color.FromArgb(91, 155, 213), 2), 5, 5, 11, 5);
                g.DrawLine(new Pen(Color.FromArgb(91, 155, 213), 2), 5, 8, 11, 8);
                g.DrawLine(new Pen(Color.FromArgb(91, 155, 213), 2), 5, 11, 9, 11);
            }
            list.Images.Add(doc);
            return list;
        }

        /// <summary>
        /// 把工况按分组填入TreeView。filter为空显示全部;selectedName对应的叶子被选中;
        /// extraGroups=额外存在的空分组(目录)。未分组排最后。
        /// </summary>
        public static void FillTree(TreeView tv, List<AnalysisType> types, string filter,
            string selectedName, IEnumerable<string> extraGroups = null)
        {
            tv.BeginUpdate();
            tv.Nodes.Clear();
            filter = (filter ?? "").Trim();

            // 收集 分组→类型列表(保持分组名不区分大小写去重)
            var groups = new SortedDictionary<string, List<AnalysisType>>(StringComparer.OrdinalIgnoreCase);
            if (types != null)
            {
                foreach (var t in types)
                {
                    if (t == null) continue;
                    // 过滤:工况名或分组名包含关键字
                    if (filter.Length > 0
                        && (t.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                        && GroupDisplay(t.Group).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    string g = t.Group ?? "";
                    if (!groups.TryGetValue(g, out var list))
                    {
                        list = new List<AnalysisType>();
                        groups[g] = list;
                    }
                    list.Add(t);
                }
            }
            // 额外空分组(有目录无文件)也要显示
            if (extraGroups != null)
            {
                foreach (var g in extraGroups)
                {
                    if (string.IsNullOrEmpty(g)) continue;
                    if (filter.Length > 0
                        && GroupDisplay(g).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (!groups.ContainsKey(g))
                        groups[g] = new List<AnalysisType>();
                }
            }

            TreeNode selectedNode = null;
            foreach (var kv in groups)
            {
                // 未分组最后处理
                if (kv.Key == "") continue;
                var gNode = tv.Nodes.Add(GroupDisplay(kv.Key));
                gNode.Tag = new GroupNodeTag(kv.Key);
                gNode.ImageIndex = gNode.SelectedImageIndex = ImgFolder;
                AddLeaves(gNode, kv.Value, selectedName, ref selectedNode);
            }
            if (groups.TryGetValue("", out var ungrouped))
            {
                var gNode = tv.Nodes.Add(UngroupedDisplay);
                gNode.Tag = new GroupNodeTag("");
                gNode.ImageIndex = gNode.SelectedImageIndex = ImgFolder;
                AddLeaves(gNode, ungrouped, selectedName, ref selectedNode);
            }

            tv.EndUpdate();
            if (selectedNode != null)
            {
                tv.SelectedNode = selectedNode;
                selectedNode.EnsureVisible();
            }
        }

        private static void AddLeaves(TreeNode gNode, List<AnalysisType> types,
            string selectedName, ref TreeNode selectedNode)
        {
            types.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var t in types)
            {
                var leaf = gNode.Nodes.Add(t.Name ?? "(未命名)");
                leaf.Tag = t;
                leaf.ImageIndex = leaf.SelectedImageIndex = ImgDoc;
                if (t.Name == selectedName)
                {
                    selectedNode = leaf;
                    gNode.Expand();
                }
            }
        }
    }

    /// <summary>简单文本输入对话框(新建/重命名分组用)</summary>
    internal static class SimpleInputDialog
    {
        public static string Show(IWin32Window owner, string title, string label, string initial = "")
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(320, 110);
                f.MaximizeBox = false; f.MinimizeBox = false;

                var lbl = new Label { Text = label, Location = new Point(12, 15), AutoSize = true };
                var txt = new TextBox { Text = initial, Location = new Point(12, 38), Width = 296 };
                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(148, 74), Width = 76 };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(232, 74), Width = 76 };
                f.Controls.Add(lbl); f.Controls.Add(txt); f.Controls.Add(btnOk); f.Controls.Add(btnCancel);
                f.AcceptButton = btnOk; f.CancelButton = btnCancel;

                return f.ShowDialog(owner) == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }
    }

    /// <summary>工具栏快捷选择面板:搜索+分组树,点叶子立即选中,"管理分组..."打开管理对话框</summary>
    internal class AnalysisTypePickerPanel : Panel
    {
        private readonly TreeView _tree;
        private readonly TextBox _txtSearch;
        private List<AnalysisType> _types = new List<AnalysisType>();
        private string _currentName;

        /// <summary>选中某个工况时触发</summary>
        public event EventHandler<AnalysisType> Picked;
        /// <summary>点击"管理分组..."时触发</summary>
        public event EventHandler ManageRequested;

        public AnalysisTypePickerPanel()
        {
            Size = new Size(300, 360);
            BackColor = Color.White;

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                ImageList = AnalysisTypeTreeHelper.BuildImageList(),
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _tree.NodeMouseClick += (s, e) =>
            {
                if (e.Node.Tag is AnalysisType t) Picked?.Invoke(this, t);
            };

            var lnk = new LinkLabel
            {
                Text = "管理分组...",
                Dock = DockStyle.Bottom,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            lnk.LinkClicked += (s, e) => ManageRequested?.Invoke(this, EventArgs.Empty);

            _txtSearch = new TextBox { Dock = DockStyle.Top };
            _txtSearch.TextChanged += (s, e) => Rebuild();

            // 添加顺序:Fill先,Bottom其次,Top最后
            Controls.Add(_tree);
            Controls.Add(lnk);
            Controls.Add(_txtSearch);
        }

        /// <summary>刷新面板数据(弹出前调用)</summary>
        public void Reload(List<AnalysisType> types, string currentName)
        {
            _types = types ?? new List<AnalysisType>();
            _currentName = currentName;
            Rebuild();
        }

        private void Rebuild()
        {
            AnalysisTypeTreeHelper.FillTree(_tree, _types, _txtSearch.Text, _currentName);
        }
    }

    /// <summary>
    /// 工况管理对话框:分组树+搜索,支持新建/编辑/删除工况,新建/重命名分组,拖拽移动工况到分组。
    /// 双击叶子或点"选择"后,SelectedType 为要选中的工况。
    /// </summary>
    internal class AnalysisTypeManagerDialog : Form
    {
        private readonly ChartFrom _form;
        private TreeView _tree;
        private TextBox _txtSearch;

        public AnalysisType SelectedType { get; private set; }

        public AnalysisTypeManagerDialog(ChartFrom form)
        {
            _form = form;
            InitUI();
            RebuildTree();
        }

        private string GroupDir(string group)
        {
            return string.IsNullOrEmpty(group) ? _form.TemplatesDir : Path.Combine(_form.TemplatesDir, group);
        }

        private void InitUI()
        {
            Text = "工况管理";
            Size = new Size(720, 540);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(10);

            // 底部按钮行
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnChoose = new Button
            {
                Text = "选 择", Dock = DockStyle.Right, Width = 90,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 130, 200), ForeColor = Color.White
            };
            btnChoose.FlatAppearance.BorderSize = 0;
            btnChoose.Click += (s, e) => ChooseSelected();
            var btnCancel = new Button { Text = "取 消", Dock = DockStyle.Right, Width = 80 };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(btnChoose);
            bottom.Controls.Add(btnCancel);

            // 操作按钮行
            var ops = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.LeftToRight };
            var btnNew = MakeOpButton("新建工况", (s, e) => NewAnalysisType());
            var btnEdit = MakeOpButton("编 辑", (s, e) => EditSelected());
            var btnDel = MakeOpButton("删 除", (s, e) => DeleteSelected());
            var btnNewGroup = MakeOpButton("新建分组", (s, e) => NewGroup());
            var btnRenameGroup = MakeOpButton("重命名分组", (s, e) => RenameGroup());
            ops.Controls.Add(btnNew); ops.Controls.Add(btnEdit); ops.Controls.Add(btnDel);
            ops.Controls.Add(btnNewGroup); ops.Controls.Add(btnRenameGroup);
            var tip = new Label
            {
                Text = "双击叶子=直接选中; 拖拽叶子到分组=移动",
                AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(10, 10, 0, 0)
            };
            ops.Controls.Add(tip);

            // 搜索行
            var searchRow = new Panel { Dock = DockStyle.Top, Height = 28 };
            var lblSearch = new Label { Text = "搜索:", Dock = DockStyle.Left, Width = 44, TextAlign = ContentAlignment.MiddleLeft };
            _txtSearch = new TextBox { Dock = DockStyle.Fill };
            _txtSearch.TextChanged += (s, e) => RebuildTree();
            searchRow.Controls.Add(_txtSearch);
            searchRow.Controls.Add(lblSearch);

            // 分组树
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                AllowDrop = true,
                ImageList = AnalysisTypeTreeHelper.BuildImageList(),
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _tree.DoubleClick += (s, e) => { if (SelectedLeaf() != null) ChooseSelected(); };
            _tree.ItemDrag += Tree_ItemDrag;
            _tree.DragEnter += Tree_DragEnterOver;
            _tree.DragOver += Tree_DragEnterOver;
            _tree.DragDrop += Tree_DragDrop;

            // 添加顺序:Fill先,然后Bottom/Top(后添加先布局)
            Controls.Add(_tree);
            Controls.Add(ops);
            Controls.Add(bottom);
            Controls.Add(searchRow);

            AcceptButton = btnChoose;
            CancelButton = btnCancel;
        }

        private Button MakeOpButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = 92, Height = 28, Margin = new Padding(0, 4, 8, 4) };
            b.Click += onClick;
            return b;
        }

        private AnalysisType SelectedLeaf()
        {
            return _tree.SelectedNode?.Tag as AnalysisType;
        }

        private void RebuildTree()
        {
            var extra = new List<string>();
            try
            {
                if (Directory.Exists(_form.TemplatesDir))
                    foreach (var d in Directory.GetDirectories(_form.TemplatesDir))
                        extra.Add(Path.GetFileName(d));
            }
            catch { }
            AnalysisTypeTreeHelper.FillTree(_tree, ReportAutoService.AnalysisTypes,
                _txtSearch.Text, _form.CurrentAnalysisType?.Name, extra);
        }

        /// <summary>双击/选择按钮:把选中叶子作为结果返回</summary>
        private void ChooseSelected()
        {
            var t = SelectedLeaf();
            if (t == null)
            {
                MessageBox.Show("请先在树中选择一个工况", "提示");
                return;
            }
            SelectedType = t;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>新建工况:目标分组取当前选中节点所在分组,关闭本对话框后打开编辑器</summary>
        private void NewAnalysisType()
        {
            string group = "";
            if (_tree.SelectedNode?.Tag is AnalysisType t) group = t.Group ?? "";
            else if (_tree.SelectedNode?.Tag is GroupNodeTag g) group = g.Group;
            _form.OpenAnalysisTypeEditor(null, group);
            Close();
        }

        private void EditSelected()
        {
            var t = SelectedLeaf();
            if (t == null)
            {
                MessageBox.Show("请先选择要编辑的工况", "提示");
                return;
            }
            _form.OpenAnalysisTypeEditor(t, t.Group);
            Close();
        }

        private void DeleteSelected()
        {
            var t = SelectedLeaf();
            if (t == null)
            {
                MessageBox.Show("请先选择要删除的工况", "提示");
                return;
            }
            if (MessageBox.Show($"确定要删除工况分类 \"{t.Name}\" 吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            string path = Path.Combine(GroupDir(t.Group), t.Name + ".json");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            CleanupEmptyGroupDir(t.Group);
            _form.ReloadAnalysisTypes(null, false);
            RebuildTree();
        }

        private void NewGroup()
        {
            string name = SimpleInputDialog.Show(this, "新建分组", "分组名:");
            if (string.IsNullOrEmpty(name)) return;
            if (!IsValidGroupName(name)) return;
            try { Directory.CreateDirectory(Path.Combine(_form.TemplatesDir, name)); }
            catch (Exception ex)
            {
                MessageBox.Show("创建分组失败: " + ex.Message, "错误");
                return;
            }
            _form.ReloadAnalysisTypes(null, false);
            RebuildTree();
        }

        private void RenameGroup()
        {
            if (!(_tree.SelectedNode?.Tag is GroupNodeTag g) || string.IsNullOrEmpty(g.Group))
            {
                MessageBox.Show("请先选择一个要重命名的分组(未分组不可重命名)", "提示");
                return;
            }
            string name = SimpleInputDialog.Show(this, "重命名分组", "新分组名:", g.Group);
            if (string.IsNullOrEmpty(name) || name == g.Group) return;
            if (!IsValidGroupName(name)) return;
            string oldDir = GroupDir(g.Group);
            string newDir = GroupDir(name);
            if (Directory.Exists(newDir))
            {
                MessageBox.Show("已存在同名分组", "提示");
                return;
            }
            try { Directory.Move(oldDir, newDir); }
            catch (Exception ex)
            {
                MessageBox.Show("重命名失败: " + ex.Message, "错误");
                return;
            }
            _form.ReloadAnalysisTypes(null, false);
            RebuildTree();
        }

        private bool IsValidGroupName(string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
            {
                MessageBox.Show("分组名包含非法字符", "提示");
                return false;
            }
            return true;
        }

        /// <summary>分组目录空则删除该目录</summary>
        private void CleanupEmptyGroupDir(string group)
        {
            if (string.IsNullOrEmpty(group)) return;
            try
            {
                string dir = GroupDir(group);
                if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0
                    && Directory.GetDirectories(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch { }
        }

        // ===== 拖拽移动工况到分组 =====

        private void Tree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Item is TreeNode node && node.Tag is AnalysisType)
                _tree.DoDragDrop(node, DragDropEffects.Move);
        }

        private void Tree_DragEnterOver(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(TreeNode)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void Tree_DragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(TreeNode)) is TreeNode dragNode)) return;
            if (!(dragNode.Tag is AnalysisType t)) return;

            // 目标分组:落在分组节点=该分组;落在叶子=叶子所在分组
            Point pt = _tree.PointToClient(new Point(e.X, e.Y));
            var hit = _tree.HitTest(pt).Node;
            string targetGroup = null;
            if (hit?.Tag is GroupNodeTag g) targetGroup = g.Group;
            else if (hit?.Tag is AnalysisType ht) targetGroup = ht.Group;
            if (targetGroup == null || targetGroup == (t.Group ?? "")) return;

            string src = Path.Combine(GroupDir(t.Group), t.Name + ".json");
            string dstDir = GroupDir(targetGroup);
            string dst = Path.Combine(dstDir, t.Name + ".json");
            if (File.Exists(dst))
            {
                MessageBox.Show($"分组 \"{AnalysisTypeTreeHelper.GroupDisplay(targetGroup)}\" 中已存在同名工况", "提示");
                return;
            }
            try
            {
                Directory.CreateDirectory(dstDir);
                File.Move(src, dst);
            }
            catch (Exception ex)
            {
                MessageBox.Show("移动失败: " + ex.Message, "错误");
                return;
            }
            CleanupEmptyGroupDir(t.Group);
            _form.ReloadAnalysisTypes(null, false);
            RebuildTree();
        }
    }
}
