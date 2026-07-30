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

        /* ===== 三类通道标识（严格区分，互不混用）=====
         * 逻辑通道号：通道在 BusChannels 中的序号（1-based，CH1=第1个通道）——实时收发链路全程使用
         * 硬件通道号：PCAN USBBUS序号 / CANoe通道号（HwChannel）——仅连接映射使用
         * BLF通道号：BLF文件 mChannel（BlfChannelId）——仅BLF读取匹配/记录写出使用
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
