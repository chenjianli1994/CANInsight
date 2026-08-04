using PCAN_Client.CAN_Data;
using PCAN_Client.ScriptEngine;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Message = PCAN_Client.CAN_Data.Message;

namespace PCAN_Client
{
    /// <summary>
    /// CanSend 脚本发送页签(partial):可视化步骤编排 + 属性面板 + 条件构造器 +
    /// 信号/RawData数据编辑(复用DBC解析编码链路) + 运行控制/日志/变量监视。
    /// </summary>
    public partial class CanSend
    {
        // ===================== 成员 =====================
        private CanScript _script = new CanScript();
        private ScriptRunner _runner;
        private TabPage _tabPageScript;
        private TreeView _treeSteps;
        private Panel _propPanel;
        private int _propY; // 属性面板流式布局当前Y
        private Label _scriptStatusLabel;
        private DataGridView _dgvVars;
        private RichTextBox _rtbScriptLog;
        private System.Windows.Forms.Timer _scriptUiTimer;
        private bool _uiLocked; // 运行中锁定编辑
        private bool _suppressRawSync; // 程序化刷新RawData框时抑制TextChanged回写(防$变量绑定被解码值覆盖)
        private string _clipboardStepJson;
        private TreeNode _lastHighlightNode;
        private List<ScriptSelectionItem> _operandItemsCache;
        private int _operandItemsCacheVersion;
        private Task<List<ScriptSelectionItem>> _operandItemsLoadTask;
        private readonly Dictionary<ScriptStepType, string> _stepTypeNames = new Dictionary<ScriptStepType, string>
        {
            { ScriptStepType.SendMessage, "发送报文" },
            { ScriptStepType.Delay, "延时" },
            { ScriptStepType.WaitCondition, "等待条件" },
            { ScriptStepType.IfBlock, "条件分支(如果)" },
            { ScriptStepType.LoopBlock, "循环块" },
            { ScriptStepType.SetVariable, "变量赋值" },
            { ScriptStepType.LogMessage, "日志输出" },
            { ScriptStepType.StopScript, "停止脚本" },
        };

        // ===================== 初始化 =====================
        /// <summary>构造函数尾部调用:创建"脚本发送"页签及全部UI</summary>
        private void InitScriptTab()
        {
            _runner = new ScriptRunner();
            // 注意:引擎不推送任何事件到UI(winmm线程碰窗体句柄存在AV风险),
            // 一切状态由下方 _scriptUiTimer 100ms 在UI线程轮询(窗体销毁Timer即停,本质安全)

            _tabPageScript = new TabPage("脚本发送");
            tabControl1.Controls.Add(_tabPageScript);

            // --- 工具栏 ---
            var tool = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Font = UiTheme.UiFont };
            _scriptToolStrip = tool; // 缓存引用,轮询刷新时不再遍历控件树
            var btnRun = new ToolStripButton("▶ 运行", null, (s, e) => ScriptRunClick()) { Name = "btnScriptRun" };
            var btnPause = new ToolStripButton("⏸ 暂停", null, (s, e) => ScriptPauseClick()) { Name = "btnScriptPause" };
            var btnStop = new ToolStripButton("⏹ 停止", null, (s, e) => ScriptStopClick()) { Name = "btnScriptStop" };
            var btnStep = new ToolStripButton("单步", null, (s, e) => ScriptStepOnceClick()) { Name = "btnScriptStep" };
            var dropAdd = new ToolStripDropDownButton("＋ 添加步骤") { Name = "btnScriptAdd" };
            foreach (var kv in _stepTypeNames)
            {
                var t = kv.Key;
                dropAdd.DropDownItems.Add(kv.Value, null, (s, e) => AddStep(t));
            }
            var btnDel = new ToolStripButton("删除", null, (s, e) => DeleteSelectedStep()) { Name = "btnScriptDel" };
            var btnUp = new ToolStripButton("上移", null, (s, e) => MoveSelectedStep(-1)) { Name = "btnScriptUp" };
            var btnDown = new ToolStripButton("下移", null, (s, e) => MoveSelectedStep(1)) { Name = "btnScriptDown" };
            var btnCopy = new ToolStripButton("复制", null, (s, e) => CopySelectedStep()) { Name = "btnScriptCopy" };
            var btnPaste = new ToolStripButton("粘贴", null, (s, e) => PasteStep()) { Name = "btnScriptPaste" };
            var btnSaveScript = new ToolStripButton("保存脚本", null, (s, e) => SaveScriptFile()) { Name = "btnScriptSave" };
            var btnOpenScript = new ToolStripButton("打开脚本", null, (s, e) => OpenScriptFile()) { Name = "btnScriptOpen" };
            var btnScriptProps = new ToolStripButton("脚本设置", null, (s, e) => { _treeSteps.SelectedNode = null; ShowScriptProps(); }) { Name = "btnScriptProps" };
            tool.Items.AddRange(new ToolStripItem[]
            {
                btnRun, btnPause, btnStop, btnStep, new ToolStripSeparator(),
                dropAdd, btnDel, btnUp, btnDown, btnCopy, btnPaste, new ToolStripSeparator(),
                btnSaveScript, btnOpenScript, btnScriptProps
            });

            // --- 底部:状态 + 变量表 + 日志 ---
            var bottom = new Panel { Dock = DockStyle.Fill };
            _scriptStatusLabel = new Label
            {
                Dock = DockStyle.Left,
                Width = 290,
                Font = UiTheme.UiFont,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(4),
                Text = "状态:已停止"
            };
            _dgvVars = new DataGridView
            {
                Dock = DockStyle.Left,
                Width = 210,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvVars.Columns.Add("VarName", "变量");
            _dgvVars.Columns.Add("VarValue", "值");
            UiTheme.StyleGrid(_dgvVars);
            _rtbScriptLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                ReadOnly = true,
                BackColor = Color.White,
                DetectUrls = false
            };
            var logMenu = new ContextMenuStrip();
            logMenu.Items.Add("清空日志", null, (s, e) => _rtbScriptLog.Clear());
            logMenu.Items.Add("导出日志...", null, (s, e) => ExportScriptLog());
            _rtbScriptLog.ContextMenuStrip = logMenu;
            bottom.Controls.Add(_rtbScriptLog);
            bottom.Controls.Add(_dgvVars);
            bottom.Controls.Add(_scriptStatusLabel);

            // --- 中部:左步骤树 / 右属性面板 ---
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.None
            };
            var contentSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.None
            };
            // 首次显示时右侧详情面板占脚本页可用宽度的40%;用户后续拖动分隔条不被重置
            this.Shown += (s, e) =>
            {
                int totalWidth = split.ClientSize.Width;
                int availableWidth = totalWidth - split.SplitterWidth;
                if (availableWidth < 50) return; // 保留Panel1/Panel2默认最小尺寸各25像素

                int panel1MinSize = Math.Min(260, Math.Max(25, availableWidth / 2));
                int panel2MinSize = Math.Min(320, Math.Max(25, availableWidth - panel1MinSize));
                int rightWidth = (int)Math.Round(totalWidth * 0.4);
                int distance = availableWidth - rightWidth;
                distance = Math.Max(panel1MinSize,
                    Math.Min(distance, availableWidth - panel2MinSize));

                // 先设置分隔位置，再设置最小尺寸，避免设置最小尺寸时校验当前距离失败。
                split.SplitterDistance = distance;
                split.Panel1MinSize = panel1MinSize;
                split.Panel2MinSize = panel2MinSize;
            };
            // 底部状态/变量/日志区域默认较矮,通过水平分隔条可随时拖动调整高度
            this.Shown += (s, e) =>
            {
                int totalHeight = contentSplit.ClientSize.Height;
                int availableHeight = totalHeight - contentSplit.SplitterWidth;
                if (availableHeight < 50) return;

                int panel1MinSize = Math.Min(180, Math.Max(25, availableHeight / 2));
                int panel2MinSize = Math.Min(68, Math.Max(25, availableHeight - panel1MinSize));
                int bottomHeight = Math.Max(panel2MinSize,
                    Math.Min(84, availableHeight - panel1MinSize));
                int distance = availableHeight - bottomHeight;
                distance = Math.Max(panel1MinSize,
                    Math.Min(distance, availableHeight - panel2MinSize));

                // 先设置分隔位置,再设置最小尺寸,避免布局尚未完成时触发范围校验。
                contentSplit.SplitterDistance = distance;
                contentSplit.Panel1MinSize = panel1MinSize;
                contentSplit.Panel2MinSize = panel2MinSize;
            };
            _treeSteps = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.UiFont,
                HideSelection = false,
                AllowDrop = true,
                ShowNodeToolTips = true,
                ItemHeight = 20
            };
            _treeSteps.AfterSelect += (s, e) => { if (!_suppressProps) ShowStepProps(e?.Node?.Tag as ScriptStep); };
            _treeSteps.ItemDrag += TreeSteps_ItemDrag;
            _treeSteps.DragEnter += TreeSteps_DragEnter;
            _treeSteps.DragDrop += TreeSteps_DragDrop;
            _treeSteps.MouseDown += TreeSteps_MouseDown;
            _treeSteps.KeyDown += TreeSteps_KeyDown;
            var treeMenu = new ContextMenuStrip();
            // 添加位置二选一:块内(子步骤) / 同级之后(块外);可用性随选中节点动态更新
            var mChild = new ToolStripMenuItem("添加子步骤(到选中块内)");
            var mSibling = new ToolStripMenuItem("添加同级步骤(到选中步骤之后)");
            foreach (var kv in _stepTypeNames)
            {
                var t = kv.Key;
                mChild.DropDownItems.Add(kv.Value, null, (s, e) => AddStep(t, true));
                mSibling.DropDownItems.Add(kv.Value, null, (s, e) => AddStep(t, false));
            }
            treeMenu.Items.Add(mChild);
            treeMenu.Items.Add(mSibling);
            treeMenu.Items.Add(new ToolStripSeparator());
            treeMenu.Items.Add("上移", null, (s, e) => MoveSelectedStep(-1));
            treeMenu.Items.Add("下移", null, (s, e) => MoveSelectedStep(1));
            treeMenu.Items.Add("删除", null, (s, e) => DeleteSelectedStep());
            treeMenu.Items.Add("复制", null, (s, e) => CopySelectedStep());
            treeMenu.Items.Add("粘贴", null, (s, e) => PasteStep());
            treeMenu.Opening += (s, e) =>
            {
                var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
                bool selIsBlock = sel != null && (sel.Type == ScriptStepType.LoopBlock || sel.Type == ScriptStepType.IfBlock);
                mChild.Enabled = selIsBlock && !_uiLocked;   // 只有循环/IF块能收子步骤
                mSibling.Enabled = !_uiLocked;               // 同级添加总是可用(无选中=根末尾)
            };
            _treeSteps.ContextMenuStrip = treeMenu;
            split.Panel1.Controls.Add(_treeSteps);

            _propPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
            // 双缓冲:属性面板每次选中都全量重建控件,无双缓冲会逐控件闪烁渲染
            typeof(Panel).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, _propPanel, new object[] { true });
            split.Panel2.Controls.Add(_propPanel);

            contentSplit.Panel1.Controls.Add(split);
            contentSplit.Panel2.Controls.Add(bottom);

            // Dock顺序:Fill最先,Top最后(后Add先停靠)
            _tabPageScript.Controls.Add(contentSplit);
            _tabPageScript.Controls.Add(tool);

            // --- UI刷新定时器(100ms):状态/变量/日志/高亮 ---
            _scriptUiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _scriptUiTimer.Tick += ScriptUiTimer_Tick;
            _scriptUiTimer.Start();

            // 报文树节点可拖拽到步骤树,直接创建发送步骤
            treeView1.ItemDrag += (s, e) =>
            {
                if (e.Item is TreeNode n && n.Tag is int && n.Parent?.Text == "Message")
                    DoDragDrop(n, DragDropEffects.Copy);
            };

            RebuildStepTree();
            SyncRunnerScript(); // 初始即装载,武装触发启动
            ShowScriptProps();
        }

        /// <summary>窗体关闭时调用(CanSend_FormClosed)</summary>
        private void DisposeScriptRunner()
        {
            try { _scriptUiTimer?.Stop(); _scriptUiTimer?.Dispose(); } catch { }
            try { _runner?.Dispose(); } catch { }
        }

        /// <summary>脚本加载/结构编辑后轻量同步到引擎(更新引用+重建条件信号缓存),武装触发启动;报文重链接在运行前完整Load时做</summary>
        private void SyncRunnerScript()
        {
            if (_runner.State == RunnerState.Stopped)
                _runner.AttachScript(_script);
        }

        // ===================== 步骤树 =====================
        private bool _suppressProps; // RebuildStepTree恢复选中时不触发AfterSelect属性面板重建(调用方显式刷新,避免双重构建)
        private void RebuildStepTree(Guid? selectId = null)
        {
            InvalidateOperandItemsCache(); // 操作数下拉缓存随脚本结构失效(变量/信号清单重建)
            // 记录展开状态
            var expanded = new HashSet<Guid>();
            foreach (TreeNode n in _treeSteps.Nodes) CollectExpanded(n, expanded);
            if (selectId == null && _treeSteps.SelectedNode?.Tag is ScriptStep sel) selectId = sel.Id;

            _suppressProps = true;
            try
            {
                _treeSteps.BeginUpdate();
                _treeSteps.Nodes.Clear();
                BuildStepNodes(_script.Steps, _treeSteps.Nodes, "");
                // 恢复展开与选中
                TreeNode toSelect = null;
                foreach (TreeNode n in _treeSteps.Nodes) RestoreExpanded(n, expanded, selectId, ref toSelect);
                if (toSelect != null) _treeSteps.SelectedNode = toSelect;
            }
            finally
            {
                _treeSteps.EndUpdate();
                _suppressProps = false;
            }
        }

        private void CollectExpanded(TreeNode node, HashSet<Guid> set)
        {
            if (node.IsExpanded && node.Tag is ScriptStep s) set.Add(s.Id);
            foreach (TreeNode c in node.Nodes) CollectExpanded(c, set);
        }

        private void RestoreExpanded(TreeNode node, HashSet<Guid> set, Guid? selectId, ref TreeNode toSelect)
        {
            if (node.Tag is ScriptStep s)
            {
                if (set.Contains(s.Id)) node.Expand();
                if (selectId.HasValue && s.Id == selectId.Value) toSelect = node;
            }
            foreach (TreeNode c in node.Nodes) RestoreExpanded(c, set, selectId, ref toSelect);
        }

        private void BuildStepNodes(List<ScriptStep> steps, TreeNodeCollection nodes, string prefix)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                string no = string.IsNullOrEmpty(prefix) ? (i + 1).ToString() : $"{prefix}.{i + 1}";
                var node = new TreeNode($"{no}  {step.Summary()}") { Tag = step, Name = step.Id.ToString() };
                node.ToolTipText = step.Summary();
                node.ForeColor = StepTypeColor(step.Type);
                nodes.Add(node);
                if (step.Children.Count > 0 || step.Type == ScriptStepType.LoopBlock || step.Type == ScriptStepType.IfBlock)
                    BuildStepNodes(step.Children, node.Nodes, no);
            }
        }

        private static Color StepTypeColor(ScriptStepType t)
        {
            switch (t)
            {
                case ScriptStepType.SendMessage: return Color.FromArgb(0, 90, 180);
                case ScriptStepType.LoopBlock: return Color.FromArgb(150, 80, 0);
                case ScriptStepType.IfBlock: return Color.FromArgb(120, 60, 160);
                case ScriptStepType.WaitCondition: return Color.FromArgb(0, 130, 130);
                case ScriptStepType.SetVariable: return Color.FromArgb(160, 60, 100);
                case ScriptStepType.StopScript: return Color.FromArgb(190, 30, 30);
                default: return Color.Black;
            }
        }

        /// <summary>属性变更后刷新该节点文本(序号不变,摘要更新)</summary>
        private void RefreshNodeText(TreeNode node)
        {
            if (node?.Tag is ScriptStep step)
            {
                string old = node.Text;
                int sep = old.IndexOf("  ");
                string no = sep > 0 ? old.Substring(0, sep) : "";
                node.Text = $"{no}  {step.Summary()}";
                node.ToolTipText = step.Summary();
            }
        }

        private TreeNode FindNodeByStep(TreeNodeCollection nodes, ScriptStep step)
        {
            foreach (TreeNode n in nodes)
            {
                if (ReferenceEquals(n.Tag, step)) return n;
                var f = FindNodeByStep(n.Nodes, step);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>查找步骤所在列表与索引</summary>
        private bool FindStepLocation(ScriptStep step, out List<ScriptStep> list, out int index)
        {
            list = null; index = -1;
            var queue = new Queue<(List<ScriptStep> l, ScriptStep parent)>();
            queue.Enqueue((_script.Steps, null));
            while (queue.Count > 0)
            {
                var (l, _) = queue.Dequeue();
                int i = l.IndexOf(step);
                if (i >= 0) { list = l; index = i; return true; }
                foreach (var s in l) if (s.Children.Count > 0) queue.Enqueue((s.Children, s));
            }
            return false;
        }

        // ===================== 步骤增删改 =====================
        private void AddStep(ScriptStepType type) => AddStep(type, null);

        /// <summary>
        /// 添加步骤。position=null=智能(选中循环/IF块→加入块内末尾;选中普通步骤→插到同级之后;无选中→根末尾);
        /// true=强制加入选中块内(块内子步骤);false=强制插到选中步骤同级之后(块外)
        /// </summary>
        private void AddStep(ScriptStepType type, bool? forceChild)
        {
            if (_uiLocked) return;
            var step = CreateStep(type);

            var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
            bool selIsBlock = sel != null && (sel.Type == ScriptStepType.LoopBlock || sel.Type == ScriptStepType.IfBlock);
            if (sel != null && (forceChild == true || (forceChild == null && selIsBlock)))
            {
                if (selIsBlock)
                    sel.Children.Add(step); // 块内末尾
                else if (FindStepLocation(sel, out List<ScriptStep> l2, out int i2))
                    l2.Insert(i2 + 1, step); // 兜底:强制子级但选中非块→同级之后
            }
            else if (sel != null && FindStepLocation(sel, out List<ScriptStep> list, out int idx))
            {
                list.Insert(idx + 1, step); // 同级之后(块外)
            }
            else
            {
                _script.Steps.Add(step); // 根末尾
            }
            RebuildStepTree(step.Id);
            SyncRunnerScript();
            ShowStepProps(step);
        }

        /// <summary>按类型创建步骤(带合理默认配置)</summary>
        private ScriptStep CreateStep(ScriptStepType type)
        {
            var step = new ScriptStep { Type = type };
            if (type == ScriptStepType.SendMessage)
            {
                // 默认自定义报文(纯字节,可自由改ID与数据);ID自动避让脚本内已用
                step.Msg = NewCustomSnapshot();
            }
            if (type == ScriptStepType.WaitCondition || type == ScriptStepType.IfBlock)
                step.Conditions.Add(new ScriptCondition());
            return step;
        }

        /// <summary>新建自定义报文快照:ID从0x100起避让脚本内所有发送步骤已占用的ID</summary>
        private ScriptMessageSnapshot NewCustomSnapshot()
        {
            var used = new HashSet<uint>();
            var queue = new Queue<ScriptStep>(_script.Steps);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                if (s.Msg != null) used.Add(s.Msg.Id);
                foreach (var c in s.Children) queue.Enqueue(c);
            }
            uint id = 0x100;
            while (used.Contains(id)) id++;
            return new ScriptMessageSnapshot
            {
                Id = id,
                IsCustom = true,
                Name = $"自定义_0x{id:X3}",
                Channel = 1,
                DataLen = 8,
                DataHex = "00 00 00 00 00 00 00 00"
            };
        }

        private void DeleteSelectedStep()
        {
            if (_uiLocked) return;
            var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
            if (sel == null) return;
            if (FindStepLocation(sel, out List<ScriptStep> list, out int idx))
            {
                list.RemoveAt(idx);
                RebuildStepTree();
                SyncRunnerScript();
                ShowScriptProps();
            }
        }

        private void MoveSelectedStep(int dir)
        {
            if (_uiLocked) return;
            var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
            if (sel == null) return;
            if (FindStepLocation(sel, out List<ScriptStep> list, out int idx))
            {
                int ni = idx + dir;
                if (ni < 0 || ni >= list.Count) return;
                list.RemoveAt(idx);
                list.Insert(ni, sel);
                RebuildStepTree(sel.Id);
                SyncRunnerScript();
            }
        }

        private void CopySelectedStep()
        {
            var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
            if (sel == null) return;
            _clipboardStepJson = Newtonsoft.Json.JsonConvert.SerializeObject(sel);
            SetScriptStatus($"已复制:{sel.Summary()}", false);
        }

        private void PasteStep()
        {
            if (_uiLocked || _clipboardStepJson == null) return;
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<ScriptStep>(_clipboardStepJson);
            // 重新分配Id
            var queue = new Queue<ScriptStep>();
            queue.Enqueue(copy);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                s.Id = Guid.NewGuid();
                foreach (var c in s.Children) queue.Enqueue(c);
            }
            var sel = _treeSteps.SelectedNode?.Tag as ScriptStep;
            if (sel != null && FindStepLocation(sel, out List<ScriptStep> list, out int idx))
                list.Insert(idx + 1, copy);
            else
                _script.Steps.Add(copy);
            RebuildStepTree(copy.Id);
            SyncRunnerScript();
            ShowStepProps(copy); // AfterSelect已挂起,显式刷新面板
        }

        // ===================== 树拖拽 =====================
        private void TreeSteps_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var node = _treeSteps.GetNodeAt(e.X, e.Y);
                if (node != null) _treeSteps.SelectedNode = node;
            }
        }

        private void TreeSteps_KeyDown(object sender, KeyEventArgs e)
        {
            if (_uiLocked) return;
            if (e.KeyCode == Keys.Delete) DeleteSelectedStep();
            else if (e.Control && e.KeyCode == Keys.C) CopySelectedStep();
            else if (e.Control && e.KeyCode == Keys.V) PasteStep();
        }

        private void TreeSteps_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (_uiLocked) return;
            if (e.Item is TreeNode node) DoDragDrop(node, DragDropEffects.Move);
        }

        private void TreeSteps_DragEnter(object sender, DragEventArgs e)
        {
            if (_uiLocked || !e.Data.GetDataPresent(typeof(TreeNode))) { e.Effect = DragDropEffects.None; return; }
            // 步骤节点=移动;报文树节点(Tag为int聚合索引)=复制创建发送步骤
            if (e.Data.GetData(typeof(TreeNode)) is TreeNode n)
            {
                if (n.Tag is ScriptStep) e.Effect = DragDropEffects.Move;
                else if (n.Tag is int) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            }
            else e.Effect = DragDropEffects.None;
        }

        private void TreeSteps_DragDrop(object sender, DragEventArgs e)
        {
            if (_uiLocked) return;
            if (!(e.Data.GetData(typeof(TreeNode)) is TreeNode src)) return;
            Point pt = _treeSteps.PointToClient(new Point(e.X, e.Y));
            TreeNode target = _treeSteps.GetNodeAt(pt.X, pt.Y);

            // 报文树节点拖入:创建发送步骤
            if (src.Tag is int aggIdx)
            {
                var messages = BaseParamter.dbcHelper.dbcFile.messages;
                if (aggIdx < 0 || aggIdx >= messages.Count) return;
                var msg = messages[aggIdx];
                var newStep = new ScriptStep
                {
                    Type = ScriptStepType.SendMessage,
                    Msg = ScriptMessageHelper.SnapshotFromMessage(msg, ResolveTxChannel(msg)),
                    RepeatIntervalMs = GetScriptRepeatIntervalMs(msg)
                };
                InsertStepAtTarget(newStep, target);
                RebuildStepTree(newStep.Id);
                SyncRunnerScript();
                ShowStepProps(newStep);
                return;
            }

            // 步骤节点移动
            if (!(src.Tag is ScriptStep srcStep)) return;
            if (target == src) return;
            // 禁止拖入自己的子孙
            for (TreeNode p = target; p != null; p = p.Parent)
                if (p == src) return;

            if (!FindStepLocation(srcStep, out List<ScriptStep> srcList, out int srcIdx)) return;
            srcList.RemoveAt(srcIdx);
            InsertStepAtTarget(srcStep, target);
            RebuildStepTree(srcStep.Id);
            SyncRunnerScript();
        }

        /// <summary>把步骤插入到目标节点位置(块节点=入子列表;普通节点=插到其后;空白=根末尾)</summary>
        private void InsertStepAtTarget(ScriptStep step, TreeNode target)
        {
            if (target?.Tag is ScriptStep targetStep)
            {
                if (targetStep.Type == ScriptStepType.LoopBlock || targetStep.Type == ScriptStepType.IfBlock)
                {
                    targetStep.Children.Add(step);
                    target.Expand();
                }
                else if (FindStepLocation(targetStep, out List<ScriptStep> tList, out int tIdx))
                {
                    tList.Insert(tIdx + 1, step);
                }
            }
            else
            {
                _script.Steps.Add(step);
            }
        }

        // ===================== 运行控制 =====================
        private void ScriptRunClick()
        {
            if (_runner.State == RunnerState.Running) return;
            if (!_runner.Load(_script, out string err))
            {
                MessageBox.Show(err, "脚本发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _runner.Run();
        }

        private void ScriptPauseClick()
        {
            if (_runner.State == RunnerState.Running) _runner.Pause();
            else if (_runner.State == RunnerState.Paused) _runner.Resume();
        }

        private void ScriptStopClick() => _runner.Stop();

        private void ScriptStepOnceClick()
        {
            if (_runner.State == RunnerState.Stopped)
            {
                if (!_runner.Load(_script, out string err))
                {
                    MessageBox.Show(err, "脚本发送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            _runner.StepOnce();
        }

        private ToolStrip _scriptToolStrip;
        private RunnerState _lastRunnerState = (RunnerState)(-1); // 强制首次刷新

        /// <summary>按引擎状态刷新工具栏使能与编辑锁(由_scriptUiTimer 100ms轮询调用,UI线程;状态未变直接返回)</summary>
        private void UpdateRunControlStates()
        {
            var tool = _scriptToolStrip;
            if (tool == null) return;
            var st = _runner.State;
            if (st == _lastRunnerState) return; // 状态未变不碰ToolStrip(避免每100ms设置9个按钮属性)
            _lastRunnerState = st;
            _uiLocked = st != RunnerState.Stopped;
            SetToolItem(tool, "btnScriptRun", st == RunnerState.Stopped); // Paused时应走[继续],避免Load报错
            var btnPause = tool.Items["btnScriptPause"];
            if (btnPause != null)
            {
                btnPause.Enabled = st != RunnerState.Stopped;
                btnPause.Text = st == RunnerState.Paused ? "▶ 继续" : "⏸ 暂停";
            }
            SetToolItem(tool, "btnScriptStop", st != RunnerState.Stopped);
            SetToolItem(tool, "btnScriptStep", true);
            foreach (string n in new[] { "btnScriptAdd", "btnScriptDel", "btnScriptUp", "btnScriptDown", "btnScriptCopy", "btnScriptPaste", "btnScriptSave", "btnScriptOpen", "btnScriptProps" })
                SetToolItem(tool, n, !_uiLocked);
        }

        private static void SetToolItem(ToolStrip tool, string name, bool enabled)
        {
            var item = tool.Items[name];
            if (item != null) item.Enabled = enabled;
        }

        private void SetScriptStatus(string text, bool isError)
        {
            // 状态栏文本每100ms被刷新覆盖,操作反馈改写日志区
            AppendLog(new LogEntry { Time = DateTime.Now, Kind = isError ? LogKind.Error : LogKind.Info, Text = text });
        }

        private Dictionary<string, double> _lastVarsSnapshot = new Dictionary<string, double>();

        private void ScriptUiTimer_Tick(object sender, EventArgs e)
        {
            if (_runner == null || IsDisposed) return;
            // 工具栏使能/编辑锁跟随引擎状态(UI线程轮询,替代原引擎事件推送)
            UpdateRunControlStates();
            // 状态栏(文本未变不赋值,避免每100ms无效重绘)
            string stateText;
            switch (_runner.State)
            {
                case RunnerState.Running: stateText = "运行中"; break;
                case RunnerState.Paused: stateText = "已暂停"; break;
                default: stateText = "已停止"; break;
            }
            string cur = _runner.CurrentStep?.Summary() ?? "-";
            string newStatus =
                $"状态:{stateText}\n{_runner.WaitDescription()}\n当前步骤:{cur}\n脚本循环:第{_runner.CurrentRunLoop}轮";
            if (_scriptStatusLabel.Text != newStatus) _scriptStatusLabel.Text = newStatus;

            // 变量表(仅变量集变化时重建,避免每100ms清空重绘闪动)
            var vars = _runner.GetVarsSnapshot();
            if (!VarsEqual(_lastVarsSnapshot, vars))
            {
                _lastVarsSnapshot = vars;
                _dgvVars.Rows.Clear();
                foreach (var kv in vars.OrderBy(x => x.Key))
                    _dgvVars.Rows.Add("$" + kv.Key, kv.Value.ToString("G"));
            }

            // 日志批量
            int n = 0;
            while (n++ < 200 && _runner.TryDequeueLog(out LogEntry entry))
            {
                AppendLog(entry);
            }

            // 当前步骤高亮
            UpdateStepHighlight();
        }

        private static bool VarsEqual(Dictionary<string, double> a, Dictionary<string, double> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out double v) || Math.Abs(v - kv.Value) > 1e-12) return false;
            }
            return true;
        }

        private void AppendLog(LogEntry entry)
        {
            if (_rtbScriptLog.TextLength > 300000)
            {
                _rtbScriptLog.Clear();
                _rtbScriptLog.AppendText("...(日志已满,已清空)...\n");
            }
            Color color;
            switch (entry.Kind)
            {
                case LogKind.Send: color = Color.FromArgb(0, 90, 180); break;
                case LogKind.Condition: color = Color.FromArgb(0, 130, 130); break;
                case LogKind.Variable: color = Color.FromArgb(160, 60, 100); break;
                case LogKind.Error: color = Color.Red; break;
                default: color = Color.Black; break;
            }
            _rtbScriptLog.SelectionStart = _rtbScriptLog.TextLength;
            _rtbScriptLog.SelectionColor = color;
            _rtbScriptLog.AppendText($"[{entry.Time:HH:mm:ss.fff}] {entry.Text}\n");
            _rtbScriptLog.SelectionColor = _rtbScriptLog.ForeColor;
            _rtbScriptLog.ScrollToCaret();
        }

        private void UpdateStepHighlight()
        {
            var cur = _runner.CurrentStep;
            TreeNode node = cur != null ? FindNodeByStep(_treeSteps.Nodes, cur) : null;
            if (node == _lastHighlightNode) return;
            if (_lastHighlightNode != null) _lastHighlightNode.BackColor = Color.Empty;
            if (node != null)
            {
                node.BackColor = Color.FromArgb(190, 240, 190);
                node.EnsureVisible();
            }
            _lastHighlightNode = node;
        }

        private void ExportScriptLog()
        {
            using (var dlg = new SaveFileDialog { Filter = "日志文件|*.txt", FileName = $"脚本日志_{DateTime.Now:yyyyMMdd_HHmmss}.txt" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try { File.WriteAllText(dlg.FileName, _rtbScriptLog.Text); }
                    catch (Exception ex) { MessageBox.Show($"导出失败:{ex.Message}"); }
                }
            }
        }

        // ===================== 脚本文件 =====================
        private void SaveScriptFile()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "CAN脚本|*.canscript",
                FileName = _script.Name + ".canscript",
                Title = "保存脚本"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, ScriptMessageHelper.SerializeScript(_script));
                    SetScriptStatus("脚本已保存", false);
                }
                catch (Exception ex) { MessageBox.Show($"保存失败:{ex.Message}"); }
            }
        }

        private void OpenScriptFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "CAN脚本|*.canscript", Title = "打开脚本" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                LoadScriptFile(dlg.FileName);
            }
        }

        /// <summary>加载脚本文件(也供窗体拖拽调用)</summary>
        internal void LoadScriptFile(string path)
        {
            try
            {
                var script = ScriptMessageHelper.DeserializeScript(File.ReadAllText(path));
                if (script == null) throw new Exception("脚本文件格式无效");
                _script = script;
                ScriptMessageHelper.RelinkAll(_script); // 预重建,供属性面板数据编辑
                InvalidateOperandItemsCache();
                RebuildStepTree();
                SyncRunnerScript(); // 武装触发启动
                ShowScriptProps();
                SetScriptStatus($"已加载脚本:{_script.Name}", false);
            }
            catch (Exception ex) { MessageBox.Show($"加载脚本失败:{ex.Message}"); }
        }

        /// <summary>供.dbccfg保存:取当前脚本</summary>
        internal CanScript GetScriptForSave() => _script;

        /// <summary>供.dbccfg加载:恢复脚本</summary>
        internal void LoadScriptFromConfig(CanScript script)
        {
            if (script == null) return;
            _script = script;
            ScriptMessageHelper.RelinkAll(_script);
            InvalidateOperandItemsCache();
            RebuildStepTree();
            SyncRunnerScript(); // 武装触发启动
            ShowScriptProps(); // 属性面板切到脚本设置,避免残留旧脚本步骤绑定
        }

        /// <summary>供发送列表右键"添加到脚本发送"</summary>
        internal void AddStepFromMessage(Message msg, byte channel)
        {
            if (msg == null) return;
            if (_uiLocked)
            {
                SetScriptStatus("脚本运行中,请先停止再添加步骤", true);
                return;
            }
            var step = new ScriptStep
            {
                Type = ScriptStepType.SendMessage,
                Msg = ScriptMessageHelper.SnapshotFromMessage(msg, channel),
                RepeatIntervalMs = GetScriptRepeatIntervalMs(msg)
            };
            _script.Steps.Add(step);
            RebuildStepTree(step.Id);
            SyncRunnerScript();
            tabControl1.SelectedTab = _tabPageScript;
            ShowStepProps(step);
        }

        // ===================== 属性面板 =====================
        private void ClearProps(string title)
        {
            // 旧控件显式释放(Controls.Clear仅移除不释放,高频重建防句柄堆积);清Tag防悬挂引用
            foreach (Control c in _propPanel.Controls) c.Dispose();
            _propPanel.Controls.Clear();
            _propPanel.Tag = null;
            _propY = 4;
            var lbl = new Label
            {
                Text = title,
                Font = UiTheme.HeaderFont,
                AutoSize = true,
                Location = new Point(4, _propY),
                ForeColor = UiTheme.Accent
            };
            _propPanel.Controls.Add(lbl);
            _propY += 24;
        }

        private Label AddPropLabel(string text)
        {
            var lbl = new Label
            {
                Text = text,
                Font = UiTheme.UiFont,
                AutoSize = true,
                Location = new Point(4, _propY + 4)
            };
            _propPanel.Controls.Add(lbl);
            return lbl;
        }

        private T AddPropControl<T>(T ctrl, int x, int width, int height = 24) where T : Control
        {
            ctrl.Location = new Point(x, _propY);
            ctrl.Size = new Size(width, height);
            ctrl.Font = UiTheme.UiFont;
            if (ctrl is ComboBox combo)
                EnableComboBoxClickDropDown(combo);
            _propPanel.Controls.Add(ctrl);
            return ctrl;
        }

        private void NextPropRow(int height = 28) => _propY += height;

        private int PropWidth => Math.Max(200, _propPanel.ClientSize.Width - 130);

        private const int DefaultScriptRepeatIntervalMs = 100;
        private const int MaxScriptRepeatIntervalMs = 3600000;

        private static int GetScriptRepeatIntervalMs(Message msg)
        {
            if (msg == null || msg.cycleTime == 0) return DefaultScriptRepeatIntervalMs;
            return (int)Math.Min((long)msg.cycleTime, MaxScriptRepeatIntervalMs);
        }

        private static void EnableComboBoxClickDropDown(ComboBox combo)
        {
            if (combo == null) return;
            combo.Click += (s, e) =>
            {
                if (combo.Enabled && combo.Items.Count > 0)
                    combo.DroppedDown = true;
            };
        }

        /// <summary>数值钳制到[min,max],防手改JSON后属性面板NumericUpDown越界异常</summary>
        private static decimal ClampDec(long value, long min, long max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>下拉索引钳制(手改JSON的枚举值可能越界)</summary>
        private static int ClampIdx(int value, int maxIdx)
        {
            if (value < 0) return 0;
            if (value > maxIdx) return maxIdx;
            return value;
        }

        private void ShowStepProps(ScriptStep step)
        {
            if (step == null) { ShowScriptProps(); return; }
            _propPanel.SuspendLayout(); // 重建期间暂停布局,配合双缓冲避免逐控件渲染
            try
            {
                switch (step.Type)
                {
                    case ScriptStepType.SendMessage: BuildSendProps(step); break;
                    case ScriptStepType.Delay: BuildDelayProps(step); break;
                    case ScriptStepType.WaitCondition: BuildWaitProps(step); break;
                    case ScriptStepType.IfBlock: BuildIfProps(step); break;
                    case ScriptStepType.LoopBlock: BuildLoopProps(step); break;
                    case ScriptStepType.SetVariable: BuildVarProps(step); break;
                    case ScriptStepType.LogMessage: BuildLogProps(step); break;
                    case ScriptStepType.StopScript: BuildStopProps(step); break;
                }
            }
            finally { _propPanel.ResumeLayout(true); }
        }

        private TreeNode CurrentNode() => _treeSteps.SelectedNode;

        // ---------- 脚本级属性 ----------
        private void ShowScriptProps()
        {
            ClearProps("脚本设置");
            AddPropLabel("脚本名称");
            var txtName = AddPropControl(new TextBox(), 110, PropWidth);
            txtName.Text = _script.Name;
            txtName.TextChanged += (s, e) => { if (!_uiLocked) _script.Name = txtName.Text; };
            NextPropRow();

            AddPropLabel("整体循环次数");
            var numLoop = AddPropControl(new NumericUpDown { Minimum = 0, Maximum = 100000, Value = ClampDec(_script.RunLoopCount, 0, 100000) }, 110, 90);
            AddPropLabelAt("(0=无限循环)", 210);
            numLoop.ValueChanged += (s, e) => { if (!_uiLocked) _script.RunLoopCount = (int)numLoop.Value; };
            NextPropRow();

            AddPropLabel("触发方式");
            var cmbTrig = AddPropControl(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 110, 140);
            cmbTrig.Items.AddRange(new object[] { "手动运行", "收到指定帧时启动", "条件满足时启动" });
            cmbTrig.SelectedIndex = ClampIdx((int)_script.Trigger, 2);
            cmbTrig.SelectedIndexChanged += (s, e) =>
            {
                if (_uiLocked) return;
                _script.Trigger = (TriggerMode)cmbTrig.SelectedIndex;
                SyncRunnerScript(); // 触发方式变更即时武装
                ShowScriptProps(); // 重建以显示对应参数
            };
            NextPropRow();

            if (_script.Trigger == TriggerMode.OnFrameId)
            {
                AddPropLabel("触发帧ID(hex)");
                var txtId = AddPropControl(new TextBox(), 110, 80);
                txtId.Text = _script.TriggerFrameId.ToString("X");
                txtId.TextChanged += (s, e) =>
                {
                    if (_uiLocked) return;
                    if (uint.TryParse(txtId.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id))
                    { _script.TriggerFrameId = id; txtId.BackColor = Color.White; SyncRunnerScript(); }
                    else txtId.BackColor = Color.MistyRose;
                };
                AddPropLabelAt("通道(0=任意)", 200);
                var numCh = AddPropControl(new NumericUpDown { Minimum = 0, Maximum = 16, Value = ClampDec(_script.TriggerChannel, 0, 16) }, 310, 55);
                numCh.ValueChanged += (s, e) => { if (!_uiLocked) { _script.TriggerChannel = (byte)numCh.Value; SyncRunnerScript(); } };
                NextPropRow();
            }

            AddPropLabel("");
            NextPropRow(12);
            var tip = new Label
            {
                Text = "提示:\r\n· 脚本随顶部[保存配置]一并存入.dbccfg,也可单独保存为.canscript文件(支持拖拽加载)\r\n" +
                       "· 添加步骤:工具栏[＋添加步骤]或步骤树右键(可选加入块内/同级之后),支持拖拽排序\r\n" +
                       "· 条件操作数:输入数字常量、$变量,或下拉选择接收信号/帧数/字节\r\n" +
                       "· 选中不同步骤时,本面板切换为对应步骤的属性编辑(发送步骤含信号表/RawData编辑)",
                Font = UiTheme.UiFont,
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(4, _propY)
            };
            _propPanel.Controls.Add(tip);
            NextPropRow(92);

            // 触发条件组放最后:增量添加条件时面板向下延伸,不遮挡上方控件
            if (_script.Trigger == TriggerMode.OnCondition)
            {
                AddPropLabel("触发条件");
                NextPropRow(24);
                BuildCondGroup(_script.TriggerConditions, () => _script.TriggerLogic, l => _script.TriggerLogic = l, null);
            }
        }

        private void AddPropLabelAt(string text, int x)
        {
            var lbl = new Label
            {
                Text = text,
                Font = UiTheme.UiFont,
                AutoSize = true,
                Location = new Point(x, _propY + 4),
                ForeColor = Color.Gray
            };
            _propPanel.Controls.Add(lbl);
        }

        // ---------- 发送报文 ----------
        private class MsgSourceItem
        {
            public string Display;
            public Message Msg;
            public byte Channel;
            public override string ToString() => Display;
        }

        private sealed class ScriptSelectionItem
        {
            public string Display;
            public string Text;
            public uint? MessageId;
            public string SignalName;
            public string MessageName;
            public object Value;

            public override string ToString() => Display;
        }

        /// <summary>属性面板中的只读选择框，筛选动作在弹出的选择窗口内完成。</summary>
        private sealed class ScriptPickerControl : Panel
        {
            public readonly TextBox ValueBox;

            public ScriptPickerControl(string text, Action<TextBox> choose)
            {
                Height = 24;
                Padding = new Padding(0);
                BackColor = SystemColors.Window;

                var btn = new Button
                {
                    Text = "选",
                    Width = 30,
                    Dock = DockStyle.Right,
                    TabStop = false,
                    Margin = new Padding(0)
                };
                UiTheme.StyleButton(btn);
                var tip = new ToolTip();
                tip.SetToolTip(btn, "打开筛选选择信号/报文");

                ValueBox = new TextBox
                {
                    Text = text ?? "",
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0),
                    Font = UiTheme.UiFont,
                    Cursor = Cursors.Hand
                };
                btn.Click += (s, e) => choose?.Invoke(ValueBox);
                ValueBox.Click += (s, e) => choose?.Invoke(ValueBox);

                Controls.Add(ValueBox);
                Controls.Add(btn);
            }
        }

        /// <summary>脚本报文/信号选择窗口：筛选框只在用户展开选择时显示。</summary>
        private sealed class ScriptSelectionDialog : Form
        {
            private List<ScriptSelectionItem> _items;
            private List<ScriptSelectionItem> _matches = new List<ScriptSelectionItem>();
            private readonly TextBox _filterBox;
            private readonly DataGridView _list;
            private readonly Label _countLabel;
            private readonly string _initialText;

            public ScriptSelectionItem SelectedItem { get; private set; }

            public ScriptSelectionDialog(string title, IEnumerable<ScriptSelectionItem> items, string initialText = null, bool loading = false)
            {
                _items = items as List<ScriptSelectionItem> ?? (items ?? Enumerable.Empty<ScriptSelectionItem>()).ToList();
                _initialText = initialText ?? "";

                Text = title;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                ShowInTaskbar = false;
                MinimizeBox = false;
                MaximizeBox = false;
                MinimumSize = new Size(560, 360);
                Size = new Size(760, 540);
                Font = UiTheme.UiFont;
                UiTheme.StyleForm(this);

                var filterPanel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 5, 6, 4) };
                var filterLabel = new Label
                {
                    Text = "筛选",
                    Dock = DockStyle.Left,
                    Width = 42,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _countLabel = new Label
                {
                    Dock = DockStyle.Right,
                    Width = 74,
                    ForeColor = Color.Gray,
                    TextAlign = ContentAlignment.MiddleRight
                };
                _filterBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
                var filterTip = new ToolTip();
                filterTip.SetToolTip(_filterBox, "支持十六进制ID、信号名/报文名；ID支持*通配符，如 3**，多个条件用空格或逗号分隔");
                filterPanel.Controls.Add(_filterBox);
                filterPanel.Controls.Add(_countLabel);
                filterPanel.Controls.Add(filterLabel);

                _list = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    AllowUserToResizeRows = false,
                    AutoGenerateColumns = false,
                    ColumnHeadersVisible = false,
                    MultiSelect = false,
                    ReadOnly = true,
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    VirtualMode = true,
                    Font = UiTheme.UiFont
                };
                _list.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Display",
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
                _list.CellValueNeeded += (s, e) =>
                {
                    if (e.ColumnIndex == 0 && e.RowIndex >= 0 && e.RowIndex < _matches.Count)
                        e.Value = _matches[e.RowIndex].Display;
                };

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 38,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(6, 5, 6, 5),
                    WrapContents = false
                };
                var cancel = new Button { Text = "取消", Width = 78, Height = 26, DialogResult = DialogResult.Cancel };
                var confirm = new Button { Text = "确定", Width = 78, Height = 26 };
                UiTheme.StyleButton(cancel);
                UiTheme.StyleButton(confirm);
                confirm.Click += (s, e) => ConfirmSelection();
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(confirm);

                Controls.Add(_list);
                Controls.Add(buttons);
                Controls.Add(filterPanel);
                AcceptButton = confirm;
                CancelButton = cancel;

                _filterBox.TextChanged += (s, e) => RefreshItems();
                _filterBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        ConfirmSelection();
                    }
                };
                _list.CellDoubleClick += (s, e) => ConfirmSelection();
                _list.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        ConfirmSelection();
                    }
                };
                Shown += (s, e) => _filterBox.Focus();

                if (loading)
                {
                    _filterBox.Enabled = false;
                    _list.Enabled = false;
                    _countLabel.Text = "加载中...";
                }
                else
                {
                    RefreshItems();
                }
            }

            public void SetItems(IEnumerable<ScriptSelectionItem> items)
            {
                _items = items as List<ScriptSelectionItem> ?? (items ?? Enumerable.Empty<ScriptSelectionItem>()).ToList();
                _filterBox.Enabled = true;
                _list.Enabled = true;
                RefreshItems();
            }

            private void RefreshItems()
            {
                string currentText = null;
                int currentIndex = _list.CurrentCell?.RowIndex ?? -1;
                if (currentIndex >= 0 && currentIndex < _matches.Count)
                    currentText = _matches[currentIndex].Text;

                string[] tokens = SplitSelectionFilter(_filterBox.Text);
                var exactIds = new HashSet<uint>();
                var wildcardIds = new List<(uint mask, uint value, uint maxId)>();
                string normalizedIds = string.Join(" ", tokens.Select(token =>
                    token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token.Substring(2) : token));
                IdFilterRule.Parse(normalizedIds, exactIds, wildcardIds);
                bool hasIdRule = exactIds.Count > 0 || wildcardIds.Count > 0;
                var matches = _items.Where(item => PassesScriptSelectionFilter(item, tokens, exactIds, wildcardIds, hasIdRule)).ToList();

                _list.CurrentCell = null;
                _matches = matches;
                _list.RowCount = matches.Count;
                _list.ClearSelection();
                _list.Invalidate();
                _countLabel.Text = $"{matches.Count}/{_items.Count}";

                if (matches.Count == 0) return;
                int index = matches.FindIndex(item =>
                    (!string.IsNullOrEmpty(currentText) && item.Text == currentText) ||
                    (string.IsNullOrEmpty(currentText) && !string.IsNullOrEmpty(_initialText) && item.Text == _initialText));
                index = index >= 0 ? index : 0;
                _list.CurrentCell = _list.Rows[index].Cells[0];
                _list.Rows[index].Selected = true;
            }

            private void ConfirmSelection()
            {
                int index = _list.CurrentCell?.RowIndex ?? -1;
                if (index >= 0 && index < _matches.Count)
                {
                    SelectedItem = _matches[index];
                    DialogResult = DialogResult.OK;
                }
            }
        }

        private static string[] SplitSelectionFilter(string text)
        {
            return (text ?? "").Split(new[] { ',', '，', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool PassesScriptSelectionFilter(ScriptSelectionItem item, string[] tokens,
            HashSet<uint> exactIds, List<(uint mask, uint value, uint maxId)> wildcardIds, bool hasIdRule)
        {
            if (item.Value is NewCustomMsgItem) return true;
            if (tokens.Length == 0) return true;

            if (item.MessageId.HasValue && hasIdRule && IdFilterRule.Match(item.MessageId.Value, exactIds, wildcardIds))
                return true;

            foreach (string token in tokens)
            {
                if (ContainsIgnoreCase(item.SignalName, token) ||
                    ContainsIgnoreCase(item.MessageName, token) ||
                    ContainsIgnoreCase(item.Display, token))
                    return true;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private ScriptSelectionItem ChooseScriptItem(string title, IEnumerable<ScriptSelectionItem> items, string initialText = null)
        {
            using (var dlg = new ScriptSelectionDialog(title, items, initialText))
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
        }

        private ScriptSelectionItem ChooseScriptItemAsync(string title, Func<Task<List<ScriptSelectionItem>>> loadItems,
            Action<List<ScriptSelectionItem>> onLoaded, string initialText = null)
        {
            using (var dlg = new ScriptSelectionDialog(title, null, initialText, true))
            {
                dlg.Shown += (s, e) =>
                {
                    loadItems().ContinueWith(task =>
                    {
                        if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                        try
                        {
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (dlg.IsDisposed) return;
                                var items = task.Status == TaskStatus.RanToCompletion
                                    ? task.Result
                                    : new List<ScriptSelectionItem>();
                                onLoaded?.Invoke(items);
                                dlg.SetItems(items);
                            }));
                        }
                        catch (InvalidOperationException)
                        {
                            // 选择窗口已关闭时忽略后台结果
                        }
                    }, TaskScheduler.Default);
                };
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
            }
        }

        /// <summary>报文源下拉的特殊项:新建自定义报文</summary>
        private class NewCustomMsgItem
        {
            public override string ToString() => "＋ 新建自定义报文";
        }

        private List<MsgSourceItem> BuildMsgSourceItems()
        {
            var items = new List<MsgSourceItem>();
            foreach (var m in BaseParamter.dbcHelper.dbcFile.messages.Where(m => m.sendFalg))
                items.Add(new MsgSourceItem { Display = $"[发送列表] CH{ResolveTxChannel(m)} {m.messageName} 0x{m.messgeId:X3}", Msg = m, Channel = ResolveTxChannel(m) });
            for (int ci = 0; ci < customMessagesList.Count; ci++)
            {
                var m = customMessagesList[ci];
                items.Add(new MsgSourceItem { Display = $"[自定义] 0x{m.messgeId:X3} CH{m.TxChannel}", Msg = m, Channel = m.TxChannel });
            }
            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
            {
                var ch = BaseParamter.BusChannels[i];
                if (!ch.IsConfigured) continue;
                foreach (var m in ch.DbcHelper.dbcFile.messages)
                {
                    if (m.sendFalg) continue; // 已在发送列表分组
                    items.Add(new MsgSourceItem { Display = $"[CH{i + 1} DBC] {m.messageName} 0x{m.messgeId:X3}", Msg = m, Channel = (byte)(i + 1) });
                }
            }
            return items;
        }

        private List<ScriptSelectionItem> BuildMsgSourcePickerItems()
        {
            var items = new List<ScriptSelectionItem>
            {
                new ScriptSelectionItem
                {
                    Display = "＋ 新建自定义报文",
                    Text = "__new_custom_message__",
                    Value = new NewCustomMsgItem()
                }
            };
            items.AddRange(BuildMsgSourceItems().Select(item => new ScriptSelectionItem
            {
                Display = item.Display,
                Text = item.Display,
                MessageId = item.Msg?.messgeId,
                MessageName = item.Msg?.messageName,
                Value = item
            }));
            return items;
        }

        private static List<ScriptSelectionItem> BuildSignalPickerItems(Message message)
        {
            var items = new List<ScriptSelectionItem>();
            if (message?.signals == null) return items;

            foreach (var signal in message.signals)
            {
                items.Add(new ScriptSelectionItem
                {
                    Display = $"信号 {signal.signalName} 0x{message.messgeId:X3} {message.messageName}",
                    Text = signal.signalName,
                    MessageId = message.messgeId,
                    SignalName = signal.signalName,
                    MessageName = message.messageName,
                    Value = signal.signalName
                });
            }
            return items;
        }

        private void BuildSendProps(ScriptStep step)
        {
            ClearProps("发送报文步骤");
            var node = CurrentNode();
            var snap = step.Msg ?? (step.Msg = new ScriptMessageSnapshot { Id = 0x100, Name = "自定义_0x100", DataHex = "00 00 00 00 00 00 00 00" });
            if (snap.RuntimeMessage == null) ScriptMessageHelper.RelinkFromDbc(snap);

            // 报文源(筛选框在选择窗口内显示;支持ID/信号或报文名/*通配)
            AddPropLabel("报文源");
            string sourceText = snap.IsCustom
                ? $"[自定义] 0x{snap.Id:X3} CH{snap.Channel}"
                : $"当前: {snap.Name} 0x{snap.Id:X3} CH{snap.Channel}";
            var srcPicker = AddPropControl(new ScriptPickerControl(sourceText, valueBox =>
            {
                if (_uiLocked) return;
                var selected = ChooseScriptItem("选择报文", BuildMsgSourcePickerItems());
                if (selected == null) return;
                if (selected.Value is NewCustomMsgItem)
                {
                    step.Msg = NewCustomSnapshot();
                    step.RepeatIntervalMs = DefaultScriptRepeatIntervalMs;
                    ScriptMessageHelper.RelinkFromDbc(step.Msg);
                    BuildSendProps(step);
                    RefreshNodeText(node);
                    return;
                }
                if (selected.Value is MsgSourceItem item)
                {
                    step.Msg = ScriptMessageHelper.SnapshotFromMessage(item.Msg, item.Channel);
                    step.RepeatIntervalMs = GetScriptRepeatIntervalMs(item.Msg);
                    ScriptMessageHelper.RelinkFromDbc(step.Msg);
                    BuildSendProps(step);
                    RefreshNodeText(node);
                }
            }), 110, Math.Max(260, PropWidth - 60));
            srcPicker.ValueBox.ReadOnly = true;
            NextPropRow();

            // ID / 通道
            AddPropLabel("报文ID(hex)");
            var txtId = AddPropControl(new TextBox(), 110, 80);
            txtId.Text = snap.Id.ToString("X");
            txtId.TextChanged += (s, e) =>
            {
                if (_uiLocked) return;
                if (uint.TryParse(txtId.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id) && id <= 0x1FFFFFFF)
                {
                    snap.Id = id; txtId.BackColor = Color.White;
                    if (snap.RuntimeMessage != null) snap.RuntimeMessage.messgeId = id;
                    // 自定义报文改ID时同步默认名称
                    if (snap.IsCustom && (string.IsNullOrEmpty(snap.Name) || snap.Name.StartsWith("自定义_")))
                    {
                        snap.Name = $"自定义_0x{id:X3}";
                        if (snap.RuntimeMessage != null) snap.RuntimeMessage.messageName = snap.Name;
                    }
                    RefreshNodeText(node);
                }
                else txtId.BackColor = Color.MistyRose;
            };
            AddPropLabelAt("通道", 200);
            var numCh = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 16, Value = ClampDec(snap.Channel, 1, 16) }, 245, 60);
            numCh.ValueChanged += (s, e) =>
            {
                if (_uiLocked) return;
                snap.Channel = (byte)numCh.Value;
                if (snap.RuntimeMessage != null) snap.RuntimeMessage.TxChannel = snap.Channel;
                RefreshNodeText(node);
            };
            AddPropLabelAt("扩展帧", 315);
            var chkExt = AddPropControl(new CheckBox { Checked = snap.IsExtendedId, AutoSize = true }, 375, 24);
            chkExt.CheckedChanged += (s, e) =>
            {
                if (_uiLocked) return;
                snap.IsExtendedId = chkExt.Checked;
                if (snap.RuntimeMessage != null) snap.RuntimeMessage.isExternId = chkExt.Checked;
            };
            NextPropRow();

            // 次数×间隔
            AddPropLabel("次数×间隔(ms)");
            var numCnt = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 100000, Value = ClampDec(step.RepeatCount, 1, 100000) }, 110, 70);
            var numGap = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 3600000, Value = ClampDec(step.RepeatIntervalMs, 1, 3600000) }, 190, 90);
            numCnt.ValueChanged += (s, e) => { if (!_uiLocked) { step.RepeatCount = (int)numCnt.Value; RefreshNodeText(node); } };
            numGap.ValueChanged += (s, e) => { if (!_uiLocked) { step.RepeatIntervalMs = (int)numGap.Value; RefreshNodeText(node); } };
            NextPropRow();

            // 动态填充
            AddPropLabel("动态数据");
            var cmbFill = AddPropControl(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 110, 160);
            cmbFill.Items.AddRange(new object[] { "无(固定数据)", "随机填充字节", "信号递增" });
            cmbFill.SelectedIndex = ClampIdx((int)snap.Fill, 2);
            cmbFill.SelectedIndexChanged += (s, e) =>
            {
                if (_uiLocked) return;
                snap.Fill = (FillMode)cmbFill.SelectedIndex;
                BuildSendProps(step);
                RefreshNodeText(node);
            };
            NextPropRow();
            if (snap.Fill == FillMode.RandomBytes)
            {
                AddPropLabel("随机字节范围");
                var numFrom = AddPropControl(new NumericUpDown { Minimum = 0, Maximum = 63, Value = Math.Min(63, Math.Max(0, snap.RandomFrom)) }, 110, 55);
                AddPropLabelAt("到", 170);
                var numTo = AddPropControl(new NumericUpDown { Minimum = 0, Maximum = 63, Value = Math.Min(63, Math.Max(0, snap.RandomTo)) }, 195, 55);
                numFrom.ValueChanged += (s, e) => { if (!_uiLocked) snap.RandomFrom = (int)numFrom.Value; };
                numTo.ValueChanged += (s, e) => { if (!_uiLocked) snap.RandomTo = (int)numTo.Value; };
                NextPropRow();
            }
            else if (snap.Fill == FillMode.SignalIncrement)
            {
                AddPropLabel("递增信号");
                var sigPicker = AddPropControl(new ScriptPickerControl(snap.IncrementSignal, valueBox =>
                {
                    if (_uiLocked) return;
                    var selected = ChooseScriptItem("选择递增信号", BuildSignalPickerItems(snap.RuntimeMessage), valueBox.Text);
                    if (selected == null) return;
                    snap.IncrementSignal = selected.Text;
                    valueBox.Text = selected.Display;
                }), 110, 200);
                sigPicker.ValueBox.ReadOnly = true;
                NextPropRow();
                AddPropLabel("初值/步长/上限");
                var txtStart = AddPropControl(new TextBox { Text = snap.IncrementStart.ToString("G") }, 110, 55);
                var txtStep = AddPropControl(new TextBox { Text = snap.IncrementStep.ToString("G") }, 170, 55);
                var txtMax = AddPropControl(new TextBox { Text = snap.IncrementMax.ToString("G") }, 230, 55);
                Action syncInc = () =>
                {
                    if (_uiLocked) return;
                    bool ok1 = double.TryParse(txtStart.Text, out double v1);
                    bool ok2 = double.TryParse(txtStep.Text, out double v2);
                    bool ok3 = double.TryParse(txtMax.Text, out double v3);
                    if (ok1) snap.IncrementStart = v1;
                    if (ok2) snap.IncrementStep = v2;
                    if (ok3) snap.IncrementMax = v3;
                    txtStart.BackColor = ok1 ? Color.White : Color.MistyRose;
                    txtStep.BackColor = ok2 ? Color.White : Color.MistyRose;
                    txtMax.BackColor = ok3 ? Color.White : Color.MistyRose;
                };
                txtStart.TextChanged += (s, e) => syncInc();
                txtStep.TextChanged += (s, e) => syncInc();
                txtMax.TextChanged += (s, e) => syncInc();
                NextPropRow();
            }

            // 数据编辑区
            NextPropRow(6);
            if (snap.RuntimeMessage != null && snap.RuntimeMessage.signals.Count > 0)
                BuildScriptSignalTable(step, snap);
            else
                BuildScriptRawEditor(step, snap, 120);
        }

        private sealed class SignalEnumOption
        {
            public string ValueText { get; set; }
            public string Display { get; set; }
        }

        private static string GetSignalUnit(Signal signal)
        {
            string unit = (signal?.unitStr ?? "").Trim();
            if (unit == "-" || unit == "\"\"" || string.IsNullOrWhiteSpace(unit)) return "";
            return unit.Trim('"');
        }

        private static string GetSignalDisplayName(Signal signal)
        {
            string unit = GetSignalUnit(signal);
            return string.IsNullOrEmpty(unit) ? signal.signalName : $"{signal.signalName} [{unit}]";
        }

        private static List<SignalEnumOption> BuildSignalEnumOptions(Signal signal, string currentText)
        {
            var options = new List<SignalEnumOption>();
            if (signal?.enumDefinitions != null)
            {
                foreach (var item in signal.enumDefinitions.OrderBy(kv => kv.Key))
                {
                    string valueText = item.Key.ToString("G", CultureInfo.InvariantCulture);
                    options.Add(new SignalEnumOption
                    {
                        ValueText = valueText,
                        Display = $"{valueText} - {item.Value}"
                    });
                }
            }

            // 保留当前$变量或未出现在枚举表中的当前值，避免加载脚本后下拉单元格变成非法值。
            if (!string.IsNullOrWhiteSpace(currentText) && !options.Any(item => item.ValueText == currentText))
            {
                options.Insert(0, new SignalEnumOption
                {
                    ValueText = currentText,
                    Display = currentText.StartsWith("$", StringComparison.Ordinal) ? currentText : $"{currentText} - 当前值"
                });
            }
            return options;
        }

        /// <summary>脚本发送步骤的信号编辑表(复用编码/解码链路,支持$变量引用)</summary>
        private void BuildScriptSignalTable(ScriptStep step, ScriptMessageSnapshot snap)
        {
            var msg = snap.RuntimeMessage;
            AddPropLabel("信号数据（单位按DBC显示；枚举信号可下拉选择）");
            NextPropRow(22);

            var table = new DataTable();
            table.Columns.Add("SignalName", typeof(string));
            table.Columns.Add("Value", typeof(string));
            table.Columns.Add("RawValue", typeof(string));
            foreach (var sig in msg.signals)
            {
                var r = table.NewRow();
                r["SignalName"] = GetSignalDisplayName(sig);
                if (snap.SignalExprs.TryGetValue(sig.signalName, out string expr) && expr.StartsWith("$"))
                {
                    r["Value"] = expr;
                    r["RawValue"] = "$";
                }
                else
                {
                    r["Value"] = sig.cmdValue.ToString("G", CultureInfo.InvariantCulture);
                    try { r["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(sig.cmdValue, sig)).ToString("X"); }
                    catch { r["RawValue"] = "-"; }
                }
                table.Rows.Add(r);
            }
            var dgv = new DataGridView
            {
                // 显式BindingContext:未父化时默认null会延迟绑定(Columns为空),
                // 导致下方Controls["SignalName"]访问NRE;显式赋值后绑定立即生效
                BindingContext = new BindingContext(),
                DataSource = table,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                EditMode = DataGridViewEditMode.EditOnEnter,
                Location = new Point(4, _propY),
                Size = new Size(Math.Max(260, _propPanel.ClientSize.Width - 14), 180),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                ScrollBars = ScrollBars.Vertical,
                Font = UiTheme.UiFont
            };
            UiTheme.StyleGrid(dgv);
            dgv.Columns["SignalName"].ReadOnly = true;
            dgv.Columns["RawValue"].ReadOnly = true;
            dgv.Columns["SignalName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dgv.Columns["SignalName"].Width = 180;
            dgv.Columns["SignalName"].MinimumWidth = 120;
            dgv.Columns["Value"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgv.Columns["Value"].MinimumWidth = 140;
            dgv.Columns["RawValue"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dgv.Columns["RawValue"].Width = 84;
            dgv.Columns["RawValue"].MinimumWidth = 70;
            UiTheme.SetGridHeaders(dgv, ("SignalName", "信号名/单位"), ("Value", "物理值（DBC枚举可选）"), ("RawValue", "原始值"));

            for (int i = 0; i < msg.signals.Count; i++)
            {
                var sig = msg.signals[i];
                string currentText = table.Rows[i]["Value"]?.ToString() ?? "";
                string unit = GetSignalUnit(sig);
                if (!string.IsNullOrEmpty(unit))
                {
                    dgv.Rows[i].Cells["SignalName"].ToolTipText = "DBC单位: " + unit;
                    dgv.Rows[i].Cells["Value"].ToolTipText = "单位: " + unit;
                }
                if (sig.enumDefinitions != null && sig.enumDefinitions.Count > 0)
                {
                    var combo = new DataGridViewComboBoxCell
                    {
                        DataSource = BuildSignalEnumOptions(sig, currentText),
                        DisplayMember = "Display",
                        ValueMember = "ValueText",
                        FlatStyle = FlatStyle.Flat,
                        DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                        ValueType = typeof(string)
                    };
                    dgv.Rows[i].Cells["Value"] = combo;
                    combo.Value = currentText;
                }
            }
            dgv.DataError += (s, e) => { e.ThrowException = false; };
            dgv.CellParsing += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 1 || e.Value == null) return;
                string text = e.Value.ToString().Trim();
                if (text.StartsWith("$", StringComparison.Ordinal) ||
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                    double.TryParse(text, out parsed))
                {
                    e.Value = text;
                    e.ParsingApplied = true;
                }
            };
            dgv.EditingControlShowing += (s, e) =>
            {
                if (dgv.CurrentCell is DataGridViewComboBoxCell && e.Control is ComboBox combo)
                    combo.DropDownStyle = ComboBoxStyle.DropDown;
            };
            dgv.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 1 ||
                    !(dgv.Rows[e.RowIndex].Cells[e.ColumnIndex] is DataGridViewComboBoxCell)) return;
                if (!dgv.BeginEdit(true)) return;
                try
                {
                    dgv.BeginInvoke(new Action(() =>
                    {
                        if (dgv.IsDisposed || !(dgv.CurrentCell is DataGridViewComboBoxCell) ||
                            !(dgv.EditingControl is ComboBox combo)) return;
                        combo.DroppedDown = true;
                    }));
                }
                catch (InvalidOperationException)
                {
                    // 表格重建/窗体关闭时忽略延迟展开
                }
            };
            dgv.CellEndEdit += (s, e) =>
            {
                if (_uiLocked || e.RowIndex < 0 || e.ColumnIndex != 1) return;
                var sig = msg.signals[e.RowIndex];
                string text = dgv.Rows[e.RowIndex].Cells[1].Value?.ToString().Trim() ?? "";
                if (text.StartsWith("$"))
                {
                    string varName = text.Substring(1);
                    if (!ScriptOperand.IsValidVarName(varName))
                    {
                        MessageBox.Show("变量名非法(字母/下划线开头,字母数字下划线组成)", "提示");
                        dgv.Rows[e.RowIndex].Cells[1].Value = table.Rows[e.RowIndex]["Value"];
                        return;
                    }
                    snap.SignalExprs[sig.signalName] = text;
                    table.Rows[e.RowIndex]["Value"] = text;
                    table.Rows[e.RowIndex]["RawValue"] = "$";
                }
                else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) || double.TryParse(text, out num))
                {
                    if (num < sig.minimum || num > sig.maximum)
                    {
                        MessageBox.Show($"输入值 {num} 超出信号范围 [{sig.minimum}, {sig.maximum}]", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        dgv.Rows[e.RowIndex].Cells[1].Value = table.Rows[e.RowIndex]["Value"];
                        return;
                    }
                    sig.cmdValue = num;
                    msg.updateFlag = true;
                    snap.SignalExprs[sig.signalName] = num.ToString("G", CultureInfo.InvariantCulture);
                    table.Rows[e.RowIndex]["Value"] = num.ToString("G", CultureInfo.InvariantCulture);
                    table.Rows[e.RowIndex]["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(num, sig)).ToString("X");
                }
                else
                {
                    dgv.Rows[e.RowIndex].Cells[1].Value = table.Rows[e.RowIndex]["Value"];
                    return;
                }
                RefreshScriptRawFromSignals(msg, snap);
            };
            _propPanel.Controls.Add(dgv);
            NextPropRow(186);

            BuildScriptRawEditor(step, snap, 54, dgv, table);
        }

        /// <summary>信号编码后刷新RawData框(发送步骤编辑用);程序化赋值须抑制TextChanged回写,防$变量绑定被覆盖</summary>
        private void RefreshScriptRawFromSignals(Message msg, ScriptMessageSnapshot snap)
        {
            try
            {
                msg.sendBuf = CanMessageBuilder.EncodeSignals(msg.signals, Math.Max(8, (int)msg.messageSize));
                msg.updateFlag = false;
                snap.DataHex = string.Join(" ", msg.sendBuf.Select(b => b.ToString("X2")));
                snap.DataLen = msg.sendBuf.Length;
            }
            catch { }
            if (_propPanel.Tag is TextBox rawBox && !rawBox.IsDisposed)
            {
                _suppressRawSync = true;
                try { rawBox.Text = snap.DataHex; }
                finally { _suppressRawSync = false; } // 异常也不能卡住抑制标志
            }
        }

        /// <summary>RawData hex编辑框(纯字节报文主编辑器;信号报文的同步副编辑器)</summary>
        private void BuildScriptRawEditor(ScriptStep step, ScriptMessageSnapshot snap, int height,
            DataGridView dgv = null, DataTable table = null)
        {
            AddPropLabel("RawData(hex)");
            NextPropRow(22);
            var msg = snap.RuntimeMessage;
            if (msg != null && string.IsNullOrWhiteSpace(snap.DataHex) && msg.sendBuf != null)
                snap.DataHex = string.Join(" ", msg.sendBuf.Select(b => b.ToString("X2")));
            var txt = new TextBox
            {
                Multiline = true,
                Text = snap.DataHex,
                Font = new Font("Consolas", 9f),
                Location = new Point(4, _propY),
                Size = new Size(Math.Max(260, _propPanel.ClientSize.Width - 14), height),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                ScrollBars = ScrollBars.Vertical
            };
            _propPanel.Tag = txt; // RefreshScriptRawFromSignals 同步用
            txt.TextChanged += (s, e) =>
            {
                if (_uiLocked || _suppressRawSync || msg == null) return;
                // 与发送列表RawData编辑一致的解析规则(未提供的字节沿用原值)
                if (TryParseHexBytes(txt.Text, msg.sendBuf, out byte[] buf))
                {
                    msg.sendBuf = buf;
                    msg.updateFlag = false;
                    snap.DataHex = txt.Text.Trim();
                    snap.DataLen = buf.Length;
                    txt.BackColor = Color.White;
                    // 反解码回信号(信号报文时保持信号表同步;$变量绑定行跳过,保留绑定)
                    if (msg.signals.Count > 0 && table != null)
                    {
                        SyncSignalsFromRawData(msg);
                        for (int i = 0; i < msg.signals.Count && i < table.Rows.Count; i++)
                        {
                            var sig = msg.signals[i];
                            if (snap.SignalExprs.TryGetValue(sig.signalName, out string bound) && bound.StartsWith("$"))
                                continue; // $变量绑定不被解码值覆盖
                            table.Rows[i]["Value"] = sig.cmdValue.ToString("G", CultureInfo.InvariantCulture);
                            snap.SignalExprs[sig.signalName] = sig.cmdValue.ToString("G", CultureInfo.InvariantCulture);
                            try { table.Rows[i]["RawValue"] = "0x" + ((long)CanMessageBuilder.ConvertToRawValue(sig.cmdValue, sig)).ToString("X"); }
                            catch { }
                        }
                        dgv?.Refresh();
                    }
                }
                else txt.BackColor = Color.MistyRose;
            };
            _propPanel.Controls.Add(txt);
            NextPropRow(height + 6);
        }

        // ---------- 延时 ----------
        private void BuildDelayProps(ScriptStep step)
        {
            ClearProps("延时步骤");
            var node = CurrentNode();
            AddPropLabel("延时(ms)");
            var num = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 3600000, Value = ClampDec(step.DelayMs, 1, 3600000) }, 110, 90);
            num.ValueChanged += (s, e) => { if (!_uiLocked) { step.DelayMs = (int)num.Value; RefreshNodeText(node); } };
            NextPropRow();
        }

        // ---------- 等待条件 ----------
        private void BuildWaitProps(ScriptStep step)
        {
            ClearProps("等待条件步骤");
            var node = CurrentNode();
            AddPropLabel("超时(ms)");
            var num = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 3600000, Value = ClampDec(step.TimeoutMs, 1, 3600000) }, 110, 90);
            num.ValueChanged += (s, e) => { if (!_uiLocked) { step.TimeoutMs = (int)num.Value; RefreshNodeText(node); } };
            AddPropLabelAt("超时后", 210);
            var cmbTimeout = AddPropControl(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 260, 100);
            cmbTimeout.Items.AddRange(new object[] { "停止脚本", "继续下一步" });
            cmbTimeout.SelectedIndex = ClampIdx((int)step.OnTimeout, 1);
            cmbTimeout.SelectedIndexChanged += (s, e) => { if (!_uiLocked) { step.OnTimeout = (TimeoutAction)cmbTimeout.SelectedIndex; RefreshNodeText(node); } };
            NextPropRow();
            NextPropRow(6);
            AddPropLabel("等待条件(满足即继续)");
            NextPropRow(22);
            BuildCondGroup(step.Conditions, () => step.Logic, l => { step.Logic = l; }, node);
        }

        // ---------- 条件分支 ----------
        private void BuildIfProps(ScriptStep step)
        {
            ClearProps("条件分支(如果) — 满足时执行子步骤");
            var node = CurrentNode();
            NextPropRow(6);
            BuildCondGroup(step.Conditions, () => step.Logic, l => { step.Logic = l; }, node);
        }

        // ---------- 循环块 ----------
        private void BuildLoopProps(ScriptStep step)
        {
            ClearProps("循环块 — 子步骤重复执行");
            var node = CurrentNode();
            AddPropLabel("循环方式");
            var cmb = AddPropControl(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 110, 140);
            cmb.Items.AddRange(new object[] { "固定次数", "无限循环", "条件循环" });
            cmb.SelectedIndex = ClampIdx((int)step.Loop, 2);
            cmb.SelectedIndexChanged += (s, e) =>
            {
                if (_uiLocked) return;
                step.Loop = (LoopMode)cmb.SelectedIndex;
                if (step.Loop == LoopMode.Condition && step.Conditions.Count == 0)
                    step.Conditions.Add(new ScriptCondition());
                BuildLoopProps(step);
                RefreshNodeText(node);
                SyncRunnerScript();
            };
            NextPropRow();
            if (step.Loop == LoopMode.Count)
            {
                AddPropLabel("循环次数");
                var num = AddPropControl(new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = ClampDec(step.LoopCount, 1, 1000000) }, 110, 90);
                num.ValueChanged += (s, e) => { if (!_uiLocked) { step.LoopCount = (int)num.Value; RefreshNodeText(node); } };
                NextPropRow();
            }
            else if (step.Loop == LoopMode.Condition)
            {
                AddPropLabel("循环条件(满足期间反复执行)");
                NextPropRow(22);
                BuildCondGroup(step.Conditions, () => step.Logic, l => { step.Logic = l; }, node);
            }
            if (step.Loop == LoopMode.Infinite)
            {
                var warn = new Label
                {
                    Text = "⚠ 无限循环内务必包含延时或等待条件,否则将以最快速度空转发送",
                    Font = UiTheme.UiFont,
                    ForeColor = Color.DarkOrange,
                    AutoSize = true,
                    Location = new Point(4, _propY)
                };
                _propPanel.Controls.Add(warn);
                NextPropRow();
            }
        }

        // ---------- 变量赋值 ----------
        private void BuildVarProps(ScriptStep step)
        {
            ClearProps("变量赋值步骤");
            var node = CurrentNode();
            AddPropLabel("变量名");
            var txtName = AddPropControl(new TextBox(), 110, 120);
            txtName.Text = step.VarName;
            txtName.TextChanged += (s, e) =>
            {
                if (_uiLocked) return;
                if (ScriptOperand.IsValidVarName(txtName.Text.Trim()) || txtName.Text == "")
                {
                    step.VarName = txtName.Text.Trim(); txtName.BackColor = Color.White;
                    InvalidateOperandItemsCache(); // 变量重命名后条件下拉清单需重建
                    RefreshNodeText(node);
                    SyncRunnerScript();
                }
                else txtName.BackColor = Color.MistyRose;
            };
            AddPropLabelAt("(字母/下划线开头)", 240);
            NextPropRow();

            AddPropLabel("赋值来源");
            var cmb = AddPropControl(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 110, 160);
            cmb.Items.AddRange(new object[] { "常量", "读取信号/操作数", "自增" });
            cmb.SelectedIndex = ClampIdx((int)step.VarSource, 2);
            cmb.SelectedIndexChanged += (s, e) =>
            {
                if (_uiLocked) return;
                step.VarSource = (VarSourceType)cmb.SelectedIndex;
                BuildVarProps(step);
                RefreshNodeText(node);
            };
            NextPropRow();

            if (step.VarSource == VarSourceType.Const)
            {
                AddPropLabel("常量值");
                var txt = AddPropControl(new TextBox { Text = step.VarConst.ToString("G") }, 110, 90);
                txt.TextChanged += (s, e) =>
                {
                    if (_uiLocked) return;
                    if (double.TryParse(txt.Text, out double v)) { step.VarConst = v; txt.BackColor = Color.White; RefreshNodeText(node); }
                    else txt.BackColor = Color.MistyRose;
                };
                NextPropRow();
            }
            else if (step.VarSource == VarSourceType.Signal)
            {
                AddPropLabel("来源操作数");
                var cmbOp = BuildOperandEditor(step.VarSignal, () => { RefreshNodeText(node); SyncRunnerScript(); });
                cmbOp.Location = new Point(110, _propY);
                cmbOp.Size = new Size(PropWidth, 24);
                _propPanel.Controls.Add(cmbOp);
                NextPropRow();
            }
            else
            {
                AddPropLabel("自增步长");
                var txt = AddPropControl(new TextBox { Text = step.VarIncrementStep.ToString("G") }, 110, 90);
                txt.TextChanged += (s, e) =>
                {
                    if (_uiLocked) return;
                    if (double.TryParse(txt.Text, out double v)) { step.VarIncrementStep = v; txt.BackColor = Color.White; RefreshNodeText(node); }
                    else txt.BackColor = Color.MistyRose;
                };
                NextPropRow();
            }
        }

        // ---------- 日志输出 ----------
        private void BuildLogProps(ScriptStep step)
        {
            ClearProps("日志输出步骤");
            var node = CurrentNode();
            AddPropLabel("日志文本");
            var txt = AddPropControl(new TextBox(), 110, PropWidth);
            txt.Text = step.Text;
            txt.TextChanged += (s, e) => { if (!_uiLocked) { step.Text = txt.Text; RefreshNodeText(node); } };
            NextPropRow();
            AddPropLabelAt("支持 $变量名 插值,如:当前车速=$speed", 4);
            NextPropRow();
        }

        // ---------- 停止脚本 ----------
        private void BuildStopProps(ScriptStep step)
        {
            ClearProps("停止脚本步骤");
            var lbl = new Label
            {
                Text = "执行到本步骤时立即终止整个脚本(常用于错误分支主动停止)。",
                Font = UiTheme.UiFont,
                AutoSize = true,
                Location = new Point(4, _propY)
            };
            _propPanel.Controls.Add(lbl);
            NextPropRow();
        }

        // ===================== 条件构造器 =====================
        private void InvalidateOperandItemsCache()
        {
            _operandItemsCache = null;
            _operandItemsCacheVersion++;
            _operandItemsLoadTask = null;
        }

        private Task<List<ScriptSelectionItem>> GetOperandItemsTask()
        {
            if (_operandItemsCache != null)
                return Task.FromResult(_operandItemsCache);
            if (_operandItemsLoadTask == null)
            {
                int cacheVersion = _operandItemsCacheVersion;
                _operandItemsLoadTask = Task.Run(BuildOperandItems);
                _operandItemsLoadTask.ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion || IsDisposed || !IsHandleCreated) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && cacheVersion == _operandItemsCacheVersion)
                                _operandItemsCache = task.Result;
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // 窗体关闭时忽略后台缓存结果
                    }
                }, TaskScheduler.Default);
            }
            return _operandItemsLoadTask;
        }

        /// <summary>操作数下拉项:变量/信号/帧统计(常量直接输入数字即可)</summary>
        private List<ScriptSelectionItem> BuildOperandItems()
        {
            var items = new List<ScriptSelectionItem>();
            // 脚本变量
            var varNames = new HashSet<string>();
            var queue = new Queue<ScriptStep>(_script.Steps);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                if (s.Type == ScriptStepType.SetVariable && ScriptOperand.IsValidVarName(s.VarName)) varNames.Add(s.VarName);
                foreach (var c in s.Children) queue.Enqueue(c);
            }
            foreach (var name in varNames.OrderBy(x => x))
                items.Add(new ScriptSelectionItem { Display = $"变量 ${name}", Text = "$" + name });
            // 各通道DBC信号
            for (int i = 0; i < BaseParamter.BusChannels.Count; i++)
            {
                var ch = BaseParamter.BusChannels[i];
                if (!ch.IsConfigured) continue;
                byte logicCh = (byte)(i + 1);
                foreach (var m in ch.DbcHelper.dbcFile.messages)
                {
                    items.Add(new ScriptSelectionItem
                    {
                        Display = $"帧数 CH{logicCh} 0x{m.messgeId:X3} {m.messageName}",
                        Text = $"#CNT:CH{logicCh}.{m.messgeId:X}",
                        MessageId = m.messgeId,
                        MessageName = m.messageName
                    });
                    items.Add(new ScriptSelectionItem
                    {
                        Display = $"字节 CH{logicCh} 0x{m.messgeId:X3}[0] {m.messageName}",
                        Text = $"#BYTE:CH{logicCh}.{m.messgeId:X}[0]",
                        MessageId = m.messgeId,
                        MessageName = m.messageName
                    });
                    foreach (var sig in m.signals)
                    {
                        items.Add(new ScriptSelectionItem
                        {
                            Display = $"信号 CH{logicCh} {m.messageName}.{sig.signalName}",
                            Text = $"@CH{logicCh}.{m.messgeId:X}.{sig.signalName}",
                            MessageId = m.messgeId,
                            SignalName = sig.signalName,
                            MessageName = m.messageName
                        });
                    }
                }
            }
            return items;
        }

        /// <summary>操作数编辑器:可手输数字/$变量，点击按钮后在带筛选的列表中选择信号/报文。</summary>
        private ScriptPickerControl BuildOperandEditor(ScriptOperand bindTo, Action onChanged)
        {
            Action commit = null;
            GetOperandItemsTask();
            var picker = new ScriptPickerControl(bindTo.ToText(), valueBox =>
            {
                if (_uiLocked) return;
                ScriptSelectionItem selected;
                var items = _operandItemsCache;
                if (items != null)
                {
                    selected = ChooseScriptItem("选择信号/报文", items, valueBox.Text);
                }
                else
                {
                    int cacheVersion = _operandItemsCacheVersion;
                    selected = ChooseScriptItemAsync("选择信号/报文", GetOperandItemsTask, loaded =>
                    {
                        if (cacheVersion == _operandItemsCacheVersion)
                            _operandItemsCache = loaded;
                    }, valueBox.Text);
                }
                if (selected == null) return;
                valueBox.Text = selected.Text;
                commit();
            });
            var textBox = picker.ValueBox;
            var operandTip = new ToolTip();
            operandTip.SetToolTip(textBox, "可输入数字或$变量；点击右侧“选”按钮可筛选选择信号/报文");
            commit = () =>
            {
                if (_uiLocked) return;
                if (ScriptOperand.TryParse(textBox.Text, out ScriptOperand op))
                {
                    CopyOperand(op, bindTo);
                    textBox.BackColor = Color.White;
                    onChanged?.Invoke();
                }
                else textBox.BackColor = Color.MistyRose;
            };
            textBox.Leave += (s, e) => commit();
            textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    commit();
                }
            };
            return picker;
        }

        private static void CopyOperand(ScriptOperand src, ScriptOperand dst)
        {
            dst.Kind = src.Kind;
            dst.ConstValue = src.ConstValue;
            dst.VarName = src.VarName;
            dst.Channel = src.Channel;
            dst.MessageId = src.MessageId;
            dst.SignalName = src.SignalName;
            dst.UseRawValue = src.UseRawValue;
            dst.ByteIndex = src.ByteIndex;
        }

        /// <summary>
        /// 条件组编辑区(增量更新设计):列头+每行一个TableLayoutPanel(条件/且或46px | 左操作数50% | 操作符60px | 右操作数50% | 删除28px),
        /// 增删条件只插入/移除对应行,不做全量重建——消除"整页从上到下重刷"的闪烁;删除按钮固定列宽,面板再窄也不被裁剪
        /// </summary>
        private void BuildCondGroup(List<ScriptCondition> conds, Func<CondLogicOp> getLogic, Action<CondLogicOp> setLogic, TreeNode node)
        {
            var condPanel = new Panel
            {
                Location = new Point(4, _propY),
                Size = new Size(PropWidth + 106, GetCondPanelHeight(conds.Count)),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            _propPanel.Controls.Add(condPanel);
            NextPropRow(condPanel.Height + 6);
            AddCondHeader(condPanel);
            for (int i = 0; i < conds.Count; i++)
                AddCondRow(condPanel, conds, i, getLogic, setLogic, node);
            RefreshCondActionRow(condPanel, conds, getLogic, setLogic, node);
        }

        private const int CondHeaderHeight = 26;
        private const int CondRowHeight = 32;

        private static int GetCondPanelHeight(int conditionCount)
        {
            return Math.Max(CondHeaderHeight + 34, CondHeaderHeight + conditionCount * CondRowHeight + 34);
        }

        private static void ConfigureCondColumns(TableLayoutPanel table)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46)); // 条件编号/且或
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));  // 左操作数
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); // 操作符
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));  // 右操作数
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28)); // 删除
        }

        private void AddCondHeader(Panel condPanel)
        {
            var header = new TableLayoutPanel
            {
                Location = new Point(0, 0),
                Size = new Size(condPanel.Width - 4, CondHeaderHeight),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Name = "condHeader",
                BackColor = UiTheme.HeaderBack
            };
            ConfigureCondColumns(header);
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            foreach (string text in new[] { "条件", "左值", "比较", "右值", "" })
            {
                var label = new Label
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Font = UiTheme.HeaderFont,
                    ForeColor = Color.FromArgb(70, 70, 70),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 0, 1, 0)
                };
                header.Controls.Add(label);
            }
            condPanel.Controls.Add(header);
        }

        /// <summary>追加一行条件(index=conds中的序号);行Tag持有条件对象引用,后续按引用定位</summary>
        private void AddCondRow(Panel condPanel, List<ScriptCondition> conds, int index,
            Func<CondLogicOp> getLogic, Action<CondLogicOp> setLogic, TreeNode node)
        {
            var cond = conds[index];
            Action condChanged = () => { RefreshNodeText(node); SyncRunnerScript(); };
            var row = new TableLayoutPanel
            {
                Location = new Point(0, CondHeaderHeight + index * CondRowHeight),
                Size = new Size(condPanel.Width - 4, CondRowHeight - 4),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Tag = cond,
                Name = "condRow"
            };
            ConfigureCondColumns(row);
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblLogic = new Label
            {
                Text = index == 0 ? "条件1" : (getLogic() == CondLogicOp.And ? "且" : "或"),
                Font = UiTheme.HeaderFont,
                ForeColor = index == 0 ? UiTheme.Accent : Color.Gray,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            row.Controls.Add(lblLogic, 0, 0);

            var cmbL = BuildOperandEditor(cond.Left, condChanged);
            cmbL.Dock = DockStyle.Fill;
            cmbL.Margin = new Padding(0, 1, 2, 0);
            row.Controls.Add(cmbL, 1, 0);

            var cmbOp = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.UiFont,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 2, 0)
            };
            EnableComboBoxClickDropDown(cmbOp);
            cmbOp.Items.AddRange(ScriptCondition.ValidOps);
            cmbOp.SelectedItem = ScriptCondition.ValidOps.Contains(cond.Op) ? cond.Op : ">";
            cmbOp.SelectedIndexChanged += (s, e) => { if (!_uiLocked) { cond.Op = cmbOp.SelectedItem.ToString(); condChanged(); } };
            row.Controls.Add(cmbOp, 2, 0);

            var cmbR = BuildOperandEditor(cond.Right, condChanged);
            cmbR.Dock = DockStyle.Fill;
            cmbR.Margin = new Padding(0, 1, 2, 0);
            row.Controls.Add(cmbR, 3, 0);

            var btnDel = new Button { Text = "×", ForeColor = Color.Red, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 0) };
            UiTheme.StyleButton(btnDel);
            var deleteTip = new ToolTip();
            deleteTip.SetToolTip(btnDel, "删除此条件");
            btnDel.Click += (s, e) =>
            {
                if (_uiLocked) return;
                RemoveCondRow(condPanel, conds, cond, getLogic, setLogic, node);
            };
            row.Controls.Add(btnDel, 4, 0);

            condPanel.Controls.Add(row);
        }

        /// <summary>增量删除一行:仅移除该行控件,其余行原位重排,不重建</summary>
        private void RemoveCondRow(Panel condPanel, List<ScriptCondition> conds, ScriptCondition cond,
            Func<CondLogicOp> getLogic, Action<CondLogicOp> setLogic, TreeNode node)
        {
            conds.Remove(cond);
            var row = FindCondRow(condPanel, cond);
            if (row != null)
            {
                condPanel.Controls.Remove(row);
                row.Dispose();
            }
            RelayoutCondRows(condPanel, conds, getLogic);
            RefreshCondActionRow(condPanel, conds, getLogic, setLogic, node);
            condPanel.Height = GetCondPanelHeight(conds.Count);
            RefreshNodeText(node);
            SyncRunnerScript();
        }

        /// <summary>按conds顺序重排行位置与首列且/或标签(只改属性,不销毁控件)</summary>
        private void RelayoutCondRows(Panel condPanel, List<ScriptCondition> conds, Func<CondLogicOp> getLogic)
        {
            for (int i = 0; i < conds.Count; i++)
            {
                var row = FindCondRow(condPanel, conds[i]);
                if (row == null) continue;
                row.Location = new Point(0, CondHeaderHeight + i * CondRowHeight);
                if (row.GetControlFromPosition(0, 0) is Label lbl)
                {
                    lbl.Text = i == 0 ? "条件1" : (getLogic() == CondLogicOp.And ? "且" : "或");
                    lbl.ForeColor = i == 0 ? UiTheme.Accent : Color.Gray;
                }
            }
        }

        private static TableLayoutPanel FindCondRow(Panel condPanel, ScriptCondition cond)
        {
            foreach (Control c in condPanel.Controls)
                if (c is TableLayoutPanel r && r.Name == "condRow" && ReferenceEquals(r.Tag, cond)) return r;
            return null;
        }

        /// <summary>底部操作行(添加条件 + AND/OR切换):轻量重建(仅1~2个控件),位置随条件行数移动</summary>
        private void RefreshCondActionRow(Panel condPanel, List<ScriptCondition> conds,
            Func<CondLogicOp> getLogic, Action<CondLogicOp> setLogic, TreeNode node)
        {
            var old = condPanel.Controls.OfType<Panel>().FirstOrDefault(p => p.Name == "condActionRow");
            if (old != null)
            {
                foreach (Control c in old.Controls) c.Dispose();
                condPanel.Controls.Remove(old);
                old.Dispose();
            }
            var action = new Panel
            {
                Name = "condActionRow",
                Location = new Point(0, CondHeaderHeight + conds.Count * CondRowHeight + 4),
                Size = new Size(condPanel.Width - 4, 26),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            var btnAdd = new Button { Text = "＋ 添加条件", Location = new Point(4, 0), Size = new Size(90, 24) };
            UiTheme.StyleButton(btnAdd);
            btnAdd.Click += (s, e) =>
            {
                if (_uiLocked) return;
                conds.Add(new ScriptCondition());
                AddCondRow(condPanel, conds, conds.Count - 1, getLogic, setLogic, node); // 增量追加,不重建
                RelayoutCondRows(condPanel, conds, getLogic); // 第2行起显示且/或标签
                RefreshCondActionRow(condPanel, conds, getLogic, setLogic, node); // 操作行下移
                condPanel.Height = GetCondPanelHeight(conds.Count);
                RefreshNodeText(node);
                SyncRunnerScript();
            };
            action.Controls.Add(btnAdd);
            if (conds.Count > 1)
            {
                var cmbLogic = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(104, 0),
                    Size = new Size(130, 24),
                    Font = UiTheme.UiFont
                };
                cmbLogic.Items.AddRange(new object[] { "全部满足(AND)", "任一满足(OR)" });
                cmbLogic.SelectedIndex = ClampIdx((int)getLogic(), 1);
                cmbLogic.SelectedIndexChanged += (s, e) =>
                {
                    if (_uiLocked) return;
                    setLogic((CondLogicOp)cmbLogic.SelectedIndex);
                    RelayoutCondRows(condPanel, conds, getLogic); // 仅更新且/或标签文本,不重建
                    RefreshNodeText(node);
                    SyncRunnerScript();
                };
                EnableComboBoxClickDropDown(cmbLogic);
                action.Controls.Add(cmbLogic);
            }
            condPanel.Controls.Add(action);
        }
    }
}
