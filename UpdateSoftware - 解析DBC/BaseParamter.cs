using PCAN_Client.CAN_Data;
using PCAN_Client.ReportAuto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCAN_Client
{
    internal class BaseParamter
    {
        public static DbcHelper dbcHelper = new DbcHelper();
        /// <summary>
        /// 全局唯一CAN通道列表（DBC唯一数据源，仅在通道配置/工况应用/启动恢复时修改）
        /// </summary>
        public static List<CanBusChannel> BusChannels = new List<CanBusChannel>();
        public static readonly string softVersion = "V3.01.12 -- 2026-07-27";

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

        /// <summary>按逻辑通道号（BlfChannelId）查找通道索引；未找到返回 -1</summary>
        public static int GetChannelIndexByBlfId(byte blfChannelId)
        {
            for (int i = 0; i < BusChannels.Count; i++)
            {
                if (BusChannels[i].BlfChannelId == blfChannelId) return i;
            }
            return -1;
        }

        /// <summary>按逻辑通道号取该通道的DBC解析实例；无匹配或该通道未配置时返回 null（调用方回退聚合视图）</summary>
        public static DbcHelper GetDbcHelperByChannel(byte blfChannelId)
        {
            int idx = GetChannelIndexByBlfId(blfChannelId);
            if (idx >= 0 && BusChannels[idx].IsConfigured) return BusChannels[idx].DbcHelper;
            return null;
        }

        /// <summary>按物理硬件通道号反查逻辑通道号；无匹配返回入参本身（兼容未配置场景）</summary>
        public static byte GetBlfChannelByHw(byte hwChannel)
        {
            foreach (var ch in BusChannels)
            {
                if (ch.EffectiveHwChannel == hwChannel) return ch.BlfChannelId;
            }
            return hwChannel;
        }

        /// <summary>反查DBC报文所属的逻辑通道号（聚合视图中报文为各通道同一对象引用）；未匹配返回 1（默认通道）</summary>
        public static byte GetChannelOfMessage(CAN_Data.Message msg)
        {
            if (msg == null) return 1;
            foreach (var ch in BusChannels)
            {
                if (!ch.IsConfigured) continue;
                if (ch.DbcHelper.dbcFile.messages.Contains(msg)) return ch.BlfChannelId;
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
                    { HwChannel = ch.HwChannel })
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
