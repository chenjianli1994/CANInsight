using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PCAN_Client
{
    /// <summary>
    /// 信号组（仅本次运行期有效，**不落盘**、不写 Settings、不新增任何 .xml）。
    /// 成员按 <see cref="ChannelData"/> 引用保存（ChannelData 未重写 Equals/GetHashCode，
    /// 故所有查找/去重/成员归属一律是引用相等语义）。只在 UI 线程使用，不加锁。
    /// </summary>
    public sealed class ChannelGroup
    {
        private readonly List<ChannelData> _members = new List<ChannelData>();
        private readonly ReadOnlyCollection<ChannelData> _membersView;

        internal ChannelGroup(string name)
        {
            Name = name;
            _membersView = new ReadOnlyCollection<ChannelData>(_members);
        }

        /// <summary>
        /// 组名：Trim 后非空、组间唯一（<see cref="StringComparison.OrdinalIgnoreCase"/>）、
        /// 长度 ≤ <see cref="ChannelGroupModel.MaxNameLength"/>。只允许 <see cref="ChannelGroupModel"/> 改写
        /// （TryCreateGroup / TryRename 是唯一入口，rename 必须过校验）。
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>
        /// true = 信号列表里只保留组头行（成员行**不生成**，不是 Row.Visible=false）；
        /// 折叠**不改任何 ChannelData.Visible**（折叠≠隐藏曲线），也不影响绘图区。
        /// </summary>
        public bool IsCollapsed { get; internal set; }

        /// <summary>
        /// 成员（插入顺序的只读视图）。渲染顺序由 <see cref="ChannelGroupModel.BuildRowPlan"/>
        /// 按 Channels 顺序重排后决定，不依赖这里的顺序；同一信号最多属于一个组。
        /// 改成员必须走 <see cref="ChannelGroupModel.AddMember"/> / <see cref="ChannelGroupModel.RemoveMember"/>，
        /// 直接往本视图里塞会让模型的"引用→组"索引失真（视图本身会抛 NotSupportedException）。
        /// </summary>
        public IList<ChannelData> Members { get { return _membersView; } }

        /// <summary>成员数</summary>
        public int MemberCount { get { return _members.Count; } }

        /// <summary>可变成员表（仅 ChannelGroupModel 使用）</summary>
        internal List<ChannelData> MutableMembers { get { return _members; } }
    }

    /// <summary>信号列表行种类</summary>
    public enum ChannelGridRowKind
    {
        /// <summary>信号行（Tag = ChannelData）</summary>
        Channel = 0,
        /// <summary>组头行（Tag = ChannelGroup）</summary>
        GroupHeader = 1
    }

    /// <summary>
    /// 一行行计划：Kind 决定其余两个字段的语义。
    /// Channel 行 → Channel 非 null、Group = 该信号所属组（无组时 null）；
    /// GroupHeader 行 → Group 非 null、Channel 为 null。
    /// </summary>
    public sealed class ChannelGridRowSpec
    {
        public ChannelGridRowKind Kind;
        public ChannelData Channel;
        public ChannelGroup Group;
    }

    /// <summary>
    /// 信号组模型：组的创建/改名/解散，成员的单一归属维护，以及"通道列表 → 列表行"的纯函数行计划。
    /// 分组状态只在内存里，**不落盘**；本类不引用 WinForms，可独立探针编译。
    /// </summary>
    public sealed class ChannelGroupModel
    {
        /// <summary>组名最大长度（按 Trim 后的字符数）</summary>
        public const int MaxNameLength = 30;

        private readonly List<ChannelGroup> _groups = new List<ChannelGroup>();
        private readonly ReadOnlyCollection<ChannelGroup> _groupsView;
        /// <summary>"信号引用 → 归属组"。ChannelData 未重写 Equals/GetHashCode ⇒ 默认比较器即引用相等。</summary>
        private readonly Dictionary<ChannelData, ChannelGroup> _groupByChannel = new Dictionary<ChannelData, ChannelGroup>();

        public ChannelGroupModel()
        {
            _groupsView = new ReadOnlyCollection<ChannelGroup>(_groups);
        }

        /// <summary>全部组（创建顺序的只读视图）。增删只能走本类方法。</summary>
        public IList<ChannelGroup> Groups { get { return _groupsView; } }

        /// <summary>
        /// 建组：name 按 Trim 校验（非空、≤30、组间唯一 OrdinalIgnoreCase）；
        /// members 去 null、按引用去重后**至少 1 个**（不允许 0 成员组）；
        /// 每个成员自动从原组移出（原组被移空即删）。任一步校验失败不改动任何状态。
        /// </summary>
        public bool TryCreateGroup(string name, IEnumerable<ChannelData> members, out ChannelGroup created, out string error)
        {
            created = null;
            string trimmed;
            if (!TryNormalizeName(name, out trimmed, out error)) return false;

            List<ChannelData> unique = CollectMembers(members);
            if (unique.Count == 0)
            {
                error = "请至少选择一个信号";
                return false;
            }

            ChannelGroup sameName = FindGroupByName(trimmed, null);
            if (sameName != null)
            {
                error = string.Format("已存在同名信号组：{0}", trimmed);
                return false;
            }

            var group = new ChannelGroup(trimmed);
            _groups.Add(group);
            for (int i = 0; i < unique.Count; i++) Attach(group, unique[i]);
            created = group;
            error = null;
            return true;
        }

        /// <summary>
        /// 改名：group 必须仍属于本模型；newName 走同一套校验，唯一性检查排除自己。
        /// 失败时 Name 保持原值。
        /// </summary>
        public bool TryRename(ChannelGroup group, string newName, out string error)
        {
            if (group == null)
            {
                error = "信号组不存在";
                return false;
            }

            string trimmed;
            if (!TryNormalizeName(newName, out trimmed, out error)) return false;

            if (!_groups.Contains(group))
            {
                error = "信号组已解散";
                return false;
            }

            ChannelGroup sameName = FindGroupByName(trimmed, group);
            if (sameName != null)
            {
                error = string.Format("已存在同名信号组：{0}", trimmed);
                return false;
            }

            group.Name = trimmed;
            error = null;
            return true;
        }

        /// <summary>查信号所属组；不属于任何组（或 channel 为 null）返回 null。</summary>
        public ChannelGroup FindGroup(ChannelData channel)
        {
            if (channel == null) return null;
            ChannelGroup group;
            return _groupByChannel.TryGetValue(channel, out group) ? group : null;
        }

        /// <summary>
        /// 加成员（追加到组尾）：幂等（已在目标组则无动作）；自动从旧组移出；旧组被移空即删组（不保留 0 成员组）；
        /// 已解散/不属于本模型的组不接受新成员。
        /// </summary>
        public void AddMember(ChannelGroup group, ChannelData channel)
        {
            if (group == null || channel == null) return;
            if (!_groups.Contains(group)) return;
            Attach(group, channel);
        }

        /// <summary>
        /// 按信号移出组：命中返回 true；移出的是最后一名成员时该组随即消失（v3 修正 5.9）。
        /// </summary>
        public bool RemoveMember(ChannelData channel)
        {
            if (channel == null) return false;
            ChannelGroup group;
            if (!_groupByChannel.TryGetValue(channel, out group)) return false;
            DetachFrom(group, channel);
            return true;
        }

        /// <summary>
        /// 解散组：只解散分组关系，**不动 Channels、不改任何通道次序、不改任何 ChannelData 字段**。
        /// 解散后该组不再受本模型管理（成员索引一并移除，组对象自身成员表清空）。
        /// </summary>
        public void Dissolve(ChannelGroup group)
        {
            if (group == null) return;
            if (!_groups.Remove(group)) return;
            List<ChannelData> members = group.MutableMembers;
            for (int i = 0; i < members.Count; i++) _groupByChannel.Remove(members[i]);
            members.Clear();
        }

        /// <summary>
        /// 组内成员按 <paramref name="channels"/> 的顺序重排后返回（渲染顺序的唯一依据）；
        /// 已不在 channels 里的成员不返回；同一引用在 channels 里重复出现只按首次计入。
        /// </summary>
        public List<ChannelData> GetMembersInChannelOrder(ChannelGroup group, IList<ChannelData> channels)
        {
            var ordered = new List<ChannelData>();
            if (group == null || channels == null) return ordered;

            var emitted = new HashSet<ChannelData>();
            for (int i = 0; i < channels.Count; i++)
            {
                ChannelData channel = channels[i];
                if (channel == null) continue;
                if (!ReferenceEquals(FindGroup(channel), group)) continue;
                if (!emitted.Add(channel)) continue;
                ordered.Add(channel);
            }
            return ordered;
        }

        /// <summary>清空所有分组（清空信号、删除全部、加载信号清单、应用工况时调用）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _groups.Count; i++) _groups[i].MutableMembers.Clear();
            _groups.Clear();
            _groupByChannel.Clear();
        }

        /// <summary>
        /// 与当前通道表对齐：剔除已不在 <paramref name="liveChannels"/> 里的成员（按引用比对），
        /// 因剔除而变空的组删除。liveChannels 为 null 视作空表（所有组都会消失）。
        /// </summary>
        public void PruneTo(IList<ChannelData> liveChannels)
        {
            var live = new HashSet<ChannelData>();
            if (liveChannels != null)
            {
                for (int i = 0; i < liveChannels.Count; i++)
                {
                    if (liveChannels[i] != null) live.Add(liveChannels[i]);
                }
            }

            for (int gi = _groups.Count - 1; gi >= 0; gi--)
            {
                ChannelGroup group = _groups[gi];
                List<ChannelData> members = group.MutableMembers;
                for (int mi = members.Count - 1; mi >= 0; mi--)
                {
                    ChannelData channel = members[mi];
                    if (live.Contains(channel)) continue;
                    members.RemoveAt(mi);
                    _groupByChannel.Remove(channel);
                }
                if (members.Count == 0) _groups.RemoveAt(gi);
            }
        }

        /// <summary>纯函数：channels → 行计划。**成块聚合语义**（同一组的信号必须挨在一起，这样才看得出归属）：
        /// 1) 每个组渲染成"组头 + 该组全部成员（按 channels 相对顺序）"一块，块位置取该组**首个成员**在
        ///    channels 里的位置；组头紧贴块首；
        /// 2) 未分组信号保持原相对顺序（组的出现不改变它们彼此的先后）；
        /// 3) 同一 ChannelData 重复出现只发首次出现；
        /// 4) 组内无可渲染成员（全 IsReportOnly 或已不在 channels）→ 整块不发（不占位置、不发组头）；
        /// 5) 折叠且 !forReportCapture：只发组头，该组成员行全部跳过；
        /// 6) skipReportOnly：跳过 IsReportOnly；
        /// 7) forReportCapture：不发任何组头，但仍按块聚合（报表配图与屏幕一致）。
        /// 纯函数：不修改 channels、不修改 model（组名/折叠态/成员都不动），同样输入多次调用结果逐项相同。</summary>
        public static List<ChannelGridRowSpec> BuildRowPlan(IList<ChannelData> channels, ChannelGroupModel model,
            bool skipReportOnly, bool forReportCapture)
        {
            var plan = new List<ChannelGridRowSpec>();
            if (channels == null) return plan;

            var emittedGroups = new HashSet<ChannelGroup>();
            var emittedChannels = new HashSet<ChannelData>();

            for (int i = 0; i < channels.Count; i++)
            {
                ChannelData channel = channels[i];
                if (channel == null) continue;
                if (emittedChannels.Contains(channel)) continue;                 // 规则 3

                ChannelGroup group = model != null ? model.FindGroup(channel) : null;

                if (group == null)                                               // 未分组：原位出一行
                {
                    if (skipReportOnly && channel.IsReportOnly) continue;        // 规则 6
                    emittedChannels.Add(channel);
                    plan.Add(new ChannelGridRowSpec
                    {
                        Kind = ChannelGridRowKind.Channel,
                        Channel = channel,
                        Group = null
                    });
                    continue;
                }

                if (!emittedGroups.Add(group)) continue;                           // 该组整块已在前面的位置发过

                // 收集该组全部可渲染成员（按 channels 相对顺序），并在同一趟里标记为已发出
                var members = new List<ChannelData>();
                for (int j = 0; j < channels.Count; j++)
                {
                    ChannelData candidate = channels[j];
                    if (candidate == null) continue;
                    if (!ReferenceEquals(model.FindGroup(candidate), group)) continue;
                    if (skipReportOnly && candidate.IsReportOnly) continue;
                    if (emittedChannels.Contains(candidate)) continue;
                    emittedChannels.Add(candidate);
                    members.Add(candidate);
                }
                if (members.Count == 0) continue;                                // 规则 4：整块不发

                if (!forReportCapture)                                           // 规则 7
                {
                    plan.Add(new ChannelGridRowSpec
                    {
                        Kind = ChannelGridRowKind.GroupHeader,
                        Channel = null,
                        Group = group
                    });
                }

                if (group.IsCollapsed && !forReportCapture) continue;             // 规则 5

                for (int k = 0; k < members.Count; k++)
                {
                    plan.Add(new ChannelGridRowSpec
                    {
                        Kind = ChannelGridRowKind.Channel,
                        Channel = members[k],
                        Group = group
                    });
                }
            }

            return plan;
        }

        /// <summary>把成员挂到组上：已在目标组则无动作；否则先从旧组移出（旧组可能因此消失）。</summary>
        private void Attach(ChannelGroup group, ChannelData channel)
        {
            ChannelGroup previous;
            if (_groupByChannel.TryGetValue(channel, out previous))
            {
                if (ReferenceEquals(previous, group)) return;
                DetachFrom(previous, channel);
            }
            group.MutableMembers.Add(channel);
            _groupByChannel[channel] = group;
        }

        /// <summary>从组里摘掉一个成员，写空即删组（不允许 0 成员组）。</summary>
        private void DetachFrom(ChannelGroup group, ChannelData channel)
        {
            group.MutableMembers.Remove(channel);
            _groupByChannel.Remove(channel);
            if (group.MemberCount == 0) _groups.Remove(group);
        }

        /// <summary>组名规范化：Trim 后非空、≤ MaxNameLength</summary>
        private static bool TryNormalizeName(string name, out string trimmed, out string error)
        {
            trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                error = "组名不能为空";
                return false;
            }
            if (trimmed.Length > MaxNameLength)
            {
                error = string.Format("组名不能超过 {0} 个字符", MaxNameLength);
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>按组名查重（OrdinalIgnoreCase）；except 为改名时的自己，跳过比较。</summary>
        private ChannelGroup FindGroupByName(string trimmedName, ChannelGroup except)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                ChannelGroup group = _groups[i];
                if (ReferenceEquals(group, except)) continue;
                if (string.Equals(group.Name, trimmedName, StringComparison.OrdinalIgnoreCase)) return group;
            }
            return null;
        }

        /// <summary>成员参数去 null、按引用去重，保持传入顺序</summary>
        private static List<ChannelData> CollectMembers(IEnumerable<ChannelData> members)
        {
            var unique = new List<ChannelData>();
            if (members == null) return unique;
            var seen = new HashSet<ChannelData>();
            foreach (ChannelData channel in members)
            {
                if (channel == null) continue;
                if (!seen.Add(channel)) continue;
                unique.Add(channel);
            }
            return unique;
        }
    }
}
