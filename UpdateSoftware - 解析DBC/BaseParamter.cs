using PCAN_Client.CAN_Data;
using PCAN_Client.ReportAuto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCAN_Client
{
    /// <summary>ID筛选规则（报文接收/发送页面共用）：支持精确十六进制ID与通配符（*匹配任意一个十六进制位；ID位数与模式等长，如3**匹配0x300~0x3FF，不误中0x1300）</summary>
    internal static class IdFilterRule
    {
        /// <summary>解析筛选文本（逗号/空格分隔），填充精确ID集合与通配符模式列表</summary>
        internal static void Parse(string text, HashSet<uint> exact, List<(uint mask, uint value, uint maxId)> wildcards)
        {
            exact.Clear();
            wildcards.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (string part in text.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                if (token.IndexOf('*') >= 0)
                {
                    // 通配符模式：* 匹配任意一个十六进制位（如 3** 匹配0x300~0x3FF）
                    if (token.Length > 8) continue;
                    uint mask = 0, value = 0;
                    bool valid = true;
                    for (int i = 0; i < token.Length; i++)
                    {
                        char c = token[i];
                        mask <<= 4;
                        value <<= 4;
                        if (c == '*') continue;
                        int d;
                        if (c >= '0' && c <= '9') d = c - '0';
                        else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                        else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                        else { valid = false; break; }
                        mask |= 0xF;
                        value |= (uint)d;
                    }
                    if (!valid) continue;
                    uint maxId = token.Length >= 8 ? uint.MaxValue : (1u << (4 * token.Length)) - 1;
                    wildcards.Add((mask, value, maxId));
                }
                else if (uint.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out uint id))
                {
                    exact.Add(id);
                }
            }
        }

        /// <summary>ID是否通过筛选（精确或通配；无筛选条件时全部通过）</summary>
        internal static bool Match(uint id, HashSet<uint> exact, List<(uint mask, uint value, uint maxId)> wildcards)
        {
            if (exact.Count == 0 && wildcards.Count == 0) return true;
            if (exact.Contains(id)) return true;
            foreach (var w in wildcards)
            {
                if (id <= w.maxId && (id & w.mask) == w.value) return true;
            }
            return false;
        }
    }

    internal class BaseParamter
    {
        public static DbcHelper dbcHelper = new DbcHelper();
        /// <summary>
        /// 全局唯一CAN通道列表（DBC唯一数据源，仅在通道配置/工况应用/启动恢复时修改）
        /// </summary>
        public static List<CanBusChannel> BusChannels = new List<CanBusChannel>();
        public static readonly string softVersion = "V3.01.17 -- 2026-08-05";

        /// <summary>
        /// 通道配置持久化文件（exe目录下）
        /// </summary>
        private static string BusChannelsConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BusChannels.json");

        /// <summary>
        /// 把已配置通道的DBC聚合为全局只读视图（报文/节点为同一对象引用），
        /// 供Main报文树、CanSend发送、LogFileToCSV等单总线功能使用。
        /// 多通道同CAN ID时字典以先加入的通道为准。
        /// </summary>
        public static void RefreshGlobalDbcFromChannels()
        {
            var agg = new DbcFile();
            foreach (var ch in BusChannels)
            {
                if (!ch.IsConfigured) continue;
                agg.messages.AddRange(ch.DbcHelper.dbcFile.messages);
                foreach (var node in ch.DbcHelper.dbcFile.nodes)
                {
                    if (!agg.nodes.Contains(node)) agg.nodes.Add(node);
                }
            }
            agg.messages.Sort((m1, m2) => m1.messgeId.CompareTo(m2.messgeId));
            dbcHelper.dbcFile = agg;
            dbcHelper.RebuildMessageDict();
        }

        /* ===== 三类通道标识（严格区分，互不混用）=====
         * 逻辑通道号：通道在 BusChannels 中的序号（1-based，CH1=第1个通道）——实时收发链路全程使用
         * 硬件通道号：PCAN USBBUS序号 / CANoe通道号（HwChannel）——仅连接映射使用
         * 通道号（BlfChannelId）：记录文件（BLF/ASC）的通道字段，通道管理窗口可自定义编辑——
         *            实时模式：落盘导出时按此写入文件通道字段；报文模式：回放时按此把文件通道号匹配回逻辑通道
         * 硬件类型：HwType（"PCAN"/"CANoe"），混合硬件时收发按类型路由；""=未指定（旧配置兼容）
         */

        /// <summary>硬件类型常量：PCAN（Peak）</summary>
        public const string HwTypePcan = "PCAN";
        /// <summary>硬件类型常量：CANoe（Vector XL）</summary>
        public const string HwTypeCanoe = "CANoe";
        /// <summary>不连接哨兵值（写入 HwChannel 表示该逻辑通道不连接硬件）</summary>
        public const byte HwNotConnect = 255;

        /// <summary>通道的逻辑通道号（列表序号，1-based）</summary>
        public static byte GetLogicChannel(CanBusChannel ch)
        {
            int idx = BusChannels.IndexOf(ch);
            return (byte)(idx >= 0 ? idx + 1 : 1);
        }

        /// <summary>通道实际生效的硬件通道号：HwChannel>0用配置值，否则默认=逻辑通道号（索引+1）</summary>
        public static byte GetEffectiveHwChannel(int index)
        {
            if (index < 0 || index >= BusChannels.Count) return (byte)(index + 1);
            var ch = BusChannels[index];
            return ch.HwChannel > 0 ? ch.HwChannel : (byte)(index + 1);
        }

        /// <summary>通道配置的硬件类型（"PCAN"/"CANoe"/""）；空=未指定，由调用方按当前连接的设备类型解释（兼容旧配置）</summary>
        public static string GetEffectiveHwType(int index)
        {
            if (index < 0 || index >= BusChannels.Count) return "";
            return BusChannels[index].HwType ?? "";
        }

        /// <summary>按逻辑通道号取该通道的DBC解析实例；越界或未配置时返回 null（调用方回退聚合视图）</summary>
        public static DbcHelper GetDbcHelperByChannel(byte logicChannel)
        {
            int idx = logicChannel - 1;
            if (idx >= 0 && idx < BusChannels.Count && BusChannels[idx].IsConfigured)
                return BusChannels[idx].DbcHelper;
            return null;
        }

        /// <summary>按物理硬件通道号反查逻辑通道号（连接映射 HwChannel → 列表序号）；无匹配返回入参本身（兼容未配置场景）</summary>
        public static byte GetLogicChannelByHw(byte hwChannel)
        {
            for (int i = 0; i < BusChannels.Count; i++)
            {
                if (GetEffectiveHwChannel(i) == hwChannel) return (byte)(i + 1);
            }
            return hwChannel;
        }

        /// <summary>按硬件类型+物理硬件通道号反查逻辑通道号（混合硬件时消除PCAN/CANoe同号歧义，只在同类型通道中匹配；HwType未指定的通道视为与同类型匹配）；无匹配返回入参本身</summary>
        public static byte GetLogicChannelByHw(string hwType, byte hwChannel)
        {
            for (int i = 0; i < BusChannels.Count; i++)
            {
                string t = GetEffectiveHwType(i);
                if (t != "" && t != hwType) continue; // 已指定类型且不匹配的跳过；未指定的视为匹配（旧配置兼容）
                if (GetEffectiveHwChannel(i) == hwChannel) return (byte)(i + 1);
            }
            return hwChannel;
        }

        /// <summary>按BLF文件通道号反查逻辑通道号（回放路径：mChannel → 列表序号）；无匹配返回入参本身</summary>
        public static byte GetLogicChannelByBlfId(byte blfChannelId)
        {
            for (int i = 0; i < BusChannels.Count; i++)
            {
                if (BusChannels[i].BlfChannelId == blfChannelId) return (byte)(i + 1);
            }
            return blfChannelId;
        }

        /// <summary>逻辑通道号 → BLF文件通道号（实时记录写BLF/ASC时落盘用）；越界返回入参本身</summary>
        public static byte GetBlfIdByLogicChannel(byte logicChannel)
        {
            int idx = logicChannel - 1;
            if (idx >= 0 && idx < BusChannels.Count) return BusChannels[idx].BlfChannelId;
            return logicChannel;
        }

        /// <summary>反查DBC报文所属的逻辑通道号（聚合视图中报文为各通道同一对象引用）；未匹配返回 1（默认通道）</summary>
        public static byte GetChannelOfMessage(CAN_Data.Message msg)
        {
            if (msg == null) return 1;
            for (int i = 0; i < BusChannels.Count; i++)
            {
                if (!BusChannels[i].IsConfigured) continue;
                if (BusChannels[i].DbcHelper.dbcFile.messages.Contains(msg)) return (byte)(i + 1);
            }
            return 1;
        }

        /// <summary>
        /// 保存通道配置到 BusChannels.json
        /// </summary>
        public static void SaveBusChannelsConfig()
        {
            try
            {
                var list = BusChannels
                    .Select(ch => new BusChannelConfig(ch.Name, ch.BlfChannelId, ch.DbcFilePath ?? "")
                    { HwChannel = ch.HwChannel, HwType = ch.HwType ?? "" })
                    .ToList();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(BusChannelsConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[BusChannels] 保存通道配置失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 启动时从 BusChannels.json 恢复通道配置，加载各通道DBC并刷新聚合视图
        /// </summary>
        public static void LoadBusChannelsConfig()
        {
            try
            {
                if (!File.Exists(BusChannelsConfigPath)) return;
                string json = File.ReadAllText(BusChannelsConfigPath);
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BusChannelConfig>>(json);
                if (list == null) return;

                var channels = new List<CanBusChannel>();
                foreach (var cfg in list)
                {
                    var ch = new CanBusChannel(cfg.Name, cfg.BlfChannelId, cfg.DbcFilePath ?? "");
                    ch.HwChannel = cfg.HwChannel; // 旧配置无此字段时为0，EffectiveHwChannel自动跟随逻辑通道号
                    ch.HwType = cfg.HwType ?? ""; // 旧配置无此字段时为空，连接时按设备类型认领
                    if (!string.IsNullOrWhiteSpace(ch.DbcFilePath) && File.Exists(ch.DbcFilePath))
                    {
                        try
                        {
                            ch.DbcHelper = new DbcHelper();
                            ch.DbcHelper.Parse(ch.DbcFilePath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[BusChannels] 通道[{ch.Name}] DBC加载失败: {ex.Message}");
                        }
                    }
                    channels.Add(ch);
                }
                BusChannels = channels;
                RefreshGlobalDbcFromChannels();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[BusChannels] 恢复通道配置失败: " + ex.Message);
            }
        }
    }
}
