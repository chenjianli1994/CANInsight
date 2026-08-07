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
            public LinNodeMode Mode;
            public uint Baudrate;
            public string LdfPath;

            public LinChannelConfig() { }
            public LinChannelConfig(LinChannel ch)
            {
                Name = ch.Name; HwType = ch.HwType ?? ""; HwHandle = ch.HwHandle ?? "";
                Mode = ch.Mode; Baudrate = ch.Baudrate; LdfPath = ch.LdfPath ?? "";
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
                    var ch = new LinChannel();
                    if (!string.IsNullOrWhiteSpace(cfg.Name)) ch.Name = cfg.Name;
                    ch.HwType = cfg.HwType ?? "";
                    ch.HwHandle = cfg.HwHandle ?? "";
                    ch.Mode = cfg.Mode;
                    if (cfg.Baudrate > 0) ch.Baudrate = cfg.Baudrate;
                    ch.LdfPath = cfg.LdfPath ?? "";
                    if (!string.IsNullOrWhiteSpace(ch.LdfPath) && File.Exists(ch.LdfPath))
                    {
                        try
                        {
                            ch.LdfHelper = LinLdfHelper.Parse(ch.LdfPath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LinConfig] 通道[{ch.Name}] LDF加载失败: {ex.Message}");
                        }
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
