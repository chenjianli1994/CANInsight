using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 通道配置全局中枢（独立于 CAN 的 BaseParamter.BusChannels，避免污染 CAN 配置）
    /// 持久化到 exe 目录 LinChannels.json
    /// </summary>
    internal static class LinConfig
    {
        public const string HwTypePcan = "PCAN";
        public const string HwTypeCanoe = "CANoe";

        public static List<LinChannel> Channels = new List<LinChannel>();

        private static string LinChannelsConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LinChannels.json");

        private class LinChannelConfig
        {
            public string Name;
            public string HwType;
            public string HwHandle;
            // 通道级硬件模式；发送页中的具体发送角色保存在 TransmitEntries。
            public LinNodeMode? Mode;
            public string LocalNodeName;
            public uint Baudrate;
            public string LdfPath;
            public List<LinTransmitEntry> TransmitEntries;

            public LinChannelConfig() { }
            public LinChannelConfig(LinChannel ch)
            {
                Name = ch.Name; HwType = ch.HwType ?? ""; HwHandle = ch.HwHandle ?? "";
                Mode = ch.Mode; LocalNodeName = ch.LocalNodeName ?? "";
                Baudrate = ch.Baudrate; LdfPath = ch.LdfPath ?? "";
                TransmitEntries = ch.TransmitEntries == null
                    ? new List<LinTransmitEntry>()
                    : ch.TransmitEntries.ConvertAll(x => x == null ? null : x.Clone());
            }
        }

        /// <summary>保存通道配置到 LinChannels.json</summary>
        public static void SaveLinConfig()
        {
            try
            {
                var list = Channels.Select(ch => new LinChannelConfig(ch)).ToList();
                string json = JsonConvert.SerializeObject(list, Formatting.Indented);
                File.WriteAllText(LinChannelsConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LinConfig] 保存通道配置失败: " + ex.Message);
            }
        }

        /// <summary>启动时从 LinChannels.json 恢复通道配置并加载各通道 LDF</summary>
        public static void LoadLinConfig()
        {
            try
            {
                if (!File.Exists(LinChannelsConfigPath)) return;
                string json = File.ReadAllText(LinChannelsConfigPath);
                var list = JsonConvert.DeserializeObject<List<LinChannelConfig>>(json);
                if (list == null) return;

                var channels = new List<LinChannel>();
                foreach (var cfg in list)
                {
                    if (cfg == null) continue;
                    var ch = new LinChannel();
                    if (!string.IsNullOrWhiteSpace(cfg.Name)) ch.Name = cfg.Name;
                    ch.HwType = cfg.HwType ?? "";
                    ch.HwHandle = cfg.HwHandle ?? "";
                    ch.Mode = cfg.Mode.GetValueOrDefault(LinNodeMode.Master);
                    ch.LocalNodeName = cfg.LocalNodeName ?? "";
                    if (cfg.Baudrate > 0) ch.Baudrate = cfg.Baudrate;
                    ch.LdfPath = cfg.LdfPath ?? "";
                    ch.TransmitEntries = cfg.TransmitEntries == null
                        ? new List<LinTransmitEntry>()
                        : cfg.TransmitEntries.ConvertAll(x => x == null ? null : x.Clone());
                    if (!string.IsNullOrWhiteSpace(ch.LdfPath) && File.Exists(ch.LdfPath))
                    {
                        try { ch.LdfHelper = LinLdfHelper.Parse(ch.LdfPath); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LinConfig] 通道[{ch.Name}] LDF加载失败: {ex.Message}"); }
                    }
                    // 旧版本把节点角色保存在 Mode/LocalNodeName。只有旧配置没有有效发送项时
                    // 才迁移，避免覆盖新版本用户已经编辑好的发送页。
                    bool needsLegacyMigration = ch.TransmitEntries.Count == 0 &&
                        (cfg.Mode.HasValue || !string.IsNullOrWhiteSpace(cfg.LocalNodeName));
                    if (needsLegacyMigration && ch.LdfHelper != null)
                    {
                        string local = LinLdfHelper.NormalizeLocalSlaveName(ch.LdfHelper, cfg.LocalNodeName);
                        foreach (var frame in ch.LdfHelper.Frames)
                        {
                            bool isMaster = cfg.Mode.GetValueOrDefault(LinNodeMode.Master) == LinNodeMode.Master
                                && LinLdfHelper.IsMasterPublisherFrame(ch.LdfHelper, frame.Key);
                            bool isSlave = cfg.Mode.GetValueOrDefault(LinNodeMode.Master) == LinNodeMode.Slave
                                && LinLdfHelper.IsLocalSlaveResponseFrame(ch.LdfHelper, frame.Key, local);
                            if (!isMaster && !isSlave) continue;
                            byte dlc = frame.Value.Dlc == 0 ? (byte)8 : frame.Value.Dlc;
                            ch.TransmitEntries.Add(new LinTransmitEntry
                            {
                                Pid = frame.Key,
                                Type = isSlave ? LinTransmitType.Slave : LinTransmitType.Master,
                                Dlc = dlc,
                                SlotMs = 15,
                                Data = new byte[dlc],
                            });
                        }
                    }
                    foreach (var entry in ch.TransmitEntries)
                    {
                        if (entry == null) continue;
                        if (entry.Dlc > 8) entry.Dlc = 8;
                        if (entry.Data == null) entry.Data = new byte[entry.Dlc == 0 ? 8 : entry.Dlc];
                        if (entry.Dlc > 0 && entry.Data.Length != entry.Dlc) Array.Resize(ref entry.Data, entry.Dlc);
                        if (entry.SlotMs <= 0) entry.SlotMs = 15;
                    }
                    channels.Add(ch);
                }
                Channels = channels;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LinConfig] 恢复通道配置失败: " + ex.Message);
            }
        }
    }
}
