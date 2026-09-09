using System;
using System.Collections.Generic;

namespace PCAN_Client
{
    /// <summary>
    /// 曲线通道查找表：把原先每次查找 O(通道数) 的线性扫描降为 O(1) 字典查找。
    /// 按 (逻辑通道+CAN ID+信号索引) 与 (逻辑通道+信号名) 两个维度建表，值是通道引用而非索引，
    /// 避免 Channels 增删/重排后索引失效。构建只允许在 UI 线程做，构建完整体替换快照；
    /// 读侧（1ms 调度线程、DBC 解码线程）无锁只读，读到的一定是完整快照。
    /// </summary>
    public sealed class ChannelLookupTable
    {
        /// <summary>通配总线键：BusChannelIndex&lt;0 的通道对所有逻辑通道都匹配（与原线性扫描的 -1 通配语义一致）</summary>
        private const int WildcardBusKey = int.MinValue;
        /// <summary>打包成 long 的总线编码：0xFFFF 表示通配（通道总线索引实际远小于该值）</summary>
        private const int WildcardBusCode = 0xFFFF;

        /// <summary>(信号名, 总线键) 复合键：自定义 struct 而非 ValueTuple——实测 net472 下
        /// ValueTuple 键的哈希/相等走 EqualityComparer 层层派发，约为自定义键的 1.7 倍耗时</summary>
        private struct NameBusKey : IEquatable<NameBusKey>
        {
            public string Name;
            public int BusKey;

            public NameBusKey(string name, int busKey) { Name = name; BusKey = busKey; }

            public bool Equals(NameBusKey other) => BusKey == other.BusKey && string.Equals(Name, other.Name);
            public override bool Equals(object obj) => obj is NameBusKey other && Equals(other);
            public override int GetHashCode() => (Name != null ? Name.GetHashCode() : 0) * 397 ^ BusKey;
        }

        private readonly Dictionary<string, ChannelData> _byNameAny;
        private readonly Dictionary<long, ChannelData> _byIdAny;
        private readonly FirstMatchIndex<NameBusKey> _byNameBus;
        private readonly FirstMatchIndex<long> _byIdBus;

        /// <summary>空表（通道列表尚未创建或已清空时使用，查询恒返回 null）</summary>
        public static readonly ChannelLookupTable Empty = new ChannelLookupTable(0);

        private ChannelLookupTable(int capacity)
        {
            _byNameAny = new Dictionary<string, ChannelData>(capacity);
            _byIdAny = new Dictionary<long, ChannelData>(capacity);
            _byNameBus = new FirstMatchIndex<NameBusKey>(capacity);
            _byIdBus = new FirstMatchIndex<long>(capacity);
        }

        /// <summary>
        /// 按通道列表顺序构建查找表。同键取列表中靠前的通道，与原线性扫描"返回首个匹配"的语义一致。
        /// </summary>
        public static ChannelLookupTable Build(IList<ChannelData> channels)
        {
            var table = new ChannelLookupTable(channels?.Count ?? 0);
            if (channels == null) return table;

            for (int i = 0; i < channels.Count; i++)
            {
                ChannelData channel = channels[i];
                if (channel == null) continue;

                string name = channel.DbcSignalName;
                long idKey = PackIdKey(channel.DbcMessageId, channel.DbcSignalIndex);
                if (name != null && !table._byNameAny.ContainsKey(name)) table._byNameAny[name] = channel;
                if (!table._byIdAny.ContainsKey(idKey)) table._byIdAny[idKey] = channel;

                // 负值（含 -1）视为通配，统一归入通配桶；非负值按总线索引分桶
                int busKey = channel.BusChannelIndex < 0 ? WildcardBusKey : channel.BusChannelIndex;
                if (name != null) table._byNameBus.AddFirst(new NameBusKey(name, busKey), channel, i);
                table._byIdBus.AddFirst(PackIdBusKey(channel.DbcMessageId, channel.DbcSignalIndex, busKey), channel, i);
            }
            return table;
        }

        /// <summary>
        /// 按信号名查找曲线通道。filterByBusChannel=false 时不区分总线通道（原逻辑：未配多通道或逻辑通道为 0）；
        /// =true 时按目标总线索引过滤，BusChannelIndex&lt;0 的通道通配，命中多个时取列表中靠前的。
        /// </summary>
        public ChannelData FindBySignalName(string signalName, bool filterByBusChannel, int targetBusIndex)
        {
            if (signalName == null) return null;
            if (!filterByBusChannel)
                return _byNameAny.TryGetValue(signalName, out ChannelData any) ? any : null;

            ChannelData wildcard = _byNameBus.Find(new NameBusKey(signalName, WildcardBusKey), out int wildcardIndex);
            ChannelData specific = _byNameBus.Find(new NameBusKey(signalName, targetBusIndex), out int specificIndex);
            return PickEarlier(wildcard, wildcardIndex, specific, specificIndex);
        }

        /// <summary>
        /// 按 CAN ID+信号索引查找曲线通道。总线通道过滤语义同 <see cref="FindBySignalName"/>。
        /// </summary>
        public ChannelData FindByMessageSignal(int messageId, int signalIndex, bool filterByBusChannel, int targetBusIndex)
        {
            if (!filterByBusChannel)
                return _byIdAny.TryGetValue(PackIdKey(messageId, signalIndex), out ChannelData any) ? any : null;

            ChannelData wildcard = _byIdBus.Find(
                PackIdBusKey(messageId, signalIndex, WildcardBusKey), out int wildcardIndex);
            ChannelData specific = _byIdBus.Find(
                PackIdBusKey(messageId, signalIndex, targetBusIndex), out int specificIndex);
            return PickEarlier(wildcard, wildcardIndex, specific, specificIndex);
        }

        /// <summary>打包 (CAN ID, 信号索引)：高位 CAN ID，低位信号索引（信号索引 &lt; 65536）</summary>
        private static long PackIdKey(int messageId, int signalIndex)
        {
            return ((long)(uint)messageId << 32) | (uint)signalIndex;
        }

        /// <summary>打包 (CAN ID, 信号索引, 总线键)：低 16 位为总线编码（通配=0xFFFF）</summary>
        private static long PackIdBusKey(int messageId, int signalIndex, int busKey)
        {
            int busCode = busKey < 0 ? WildcardBusCode : busKey;
            return ((long)(uint)messageId << 32) | ((long)(ushort)signalIndex << 16) | (uint)busCode;
        }

        /// <summary>取列表中位置靠前的候选（列表顺序即原线性扫描顺序）</summary>
        private static ChannelData PickEarlier(ChannelData a, int aIndex, ChannelData b, int bIndex)
        {
            if (a == null) return b;
            if (b == null) return a;
            return aIndex <= bIndex ? a : b;
        }

        /// <summary>
        /// 同键多候选表：记录列表中首个匹配通道及其位置。位置用于比较"通配通道"与
        /// "指定总线通道"谁在列表中更靠前，从而与线性扫描的返回结果逐字段一致。
        /// </summary>
        private sealed class FirstMatchIndex<TKey>
        {
            private readonly Dictionary<TKey, (ChannelData Channel, int Index)> _map;

            public FirstMatchIndex(int capacity)
            {
                _map = new Dictionary<TKey, (ChannelData, int)>(capacity);
            }

            /// <summary>仅首次写入（同键保留列表中靠前的通道）</summary>
            public void AddFirst(TKey key, ChannelData channel, int index)
            {
                if (!_map.ContainsKey(key)) _map[key] = (channel, index);
            }

            /// <summary>查键；未命中返回 null，index 为 int.MaxValue</summary>
            public ChannelData Find(TKey key, out int index)
            {
                if (_map.TryGetValue(key, out var slot))
                {
                    index = slot.Index;
                    return slot.Channel;
                }
                index = int.MaxValue;
                return null;
            }
        }
    }
}
