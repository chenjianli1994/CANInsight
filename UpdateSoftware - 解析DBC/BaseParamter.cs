using PCAN_Client.CAN_Data;
using PCAN_Client.ReportAuto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;

namespace PCAN_Client
{
    internal class BaseParamter
    {
        public enum SelectProjectTypeEnum
        {
            LingPao = 0,
            BeiQi_Old = 1,
            BeiQi_New = 2,
        };

        public static Boolean AutoUpdateFlag = true;

        public readonly static byte[] RepairShopCode = new byte[]  /* 外部工具序列号 */
        {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 01
        };

        public static int SelectProjectType = 0; /* 0:客户B平台 1：客户V50 2:客户AS */
        public readonly static UInt16 UDS_FunTX_ID = 0x7DF;
        public static UInt16 UDS_TX_ID = 0x7B0;
        public static UInt16 UDS_RX_ID = 0x7B8;
        public static UInt16 FlashDriverSelectIndex = 0;

        public static Boolean Pre_b_OnlyUpdateFlag = false;
        public static DbcHelper dbcHelper = new DbcHelper();
        /// <summary>
        /// 全局唯一CAN通道列表（DBC唯一数据源，仅在通道配置/工况应用/启动恢复时修改）
        /// </summary>
        public static List<CanBusChannel> BusChannels = new List<CanBusChannel>();
        public static readonly byte[] BeiQiKey = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        public static readonly int KeyLength = 64;
        public static readonly byte KeyFillValue = 0x00;
        public static readonly byte[] BaseKeyBuf_uds = new byte[16] { 0xB7, 0xF9, 0xCE, 0x9C, 0xC9, 0x78, 0x17, 0xB6, 0x21, 0xE6, 0x9C, 0x98, 0x32, 0x8C, 0xAB, 0x3E };
        public static readonly byte[] BaseKeyBuf_program = new byte[16] { 0xD3, 0x97, 0xA1, 0xF0, 0x96, 0xB2, 0x4C, 0xA6, 0x8D, 0x4E, 0x8F, 0x8E, 0xCC, 0x51, 0xCB, 0x79 };
        public static int DataDlc = 8;
        public static readonly string softVersion = "V3.01.13 -- 2026-07-27";

        /// <summary>
        /// 通道配置持久化文件（exe目录下）
        /// </summary>
        private static string BusChannelsConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BusChannels.json");

        /// <summary>
        /// 把已配置通道的DBC聚合为全局只读视图（报文/节点为同一对象引用），
        /// 供Main报文树、CanSend发送、LogFileToCSV、版本校验等单总线功能使用。
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

        /// <summary>
        /// 保存通道配置到 BusChannels.json
        /// </summary>
        public static void SaveBusChannelsConfig()
        {
            try
            {
                var list = BusChannels
                    .Select(ch => new BusChannelConfig(ch.Name, ch.BlfChannelId, ch.DbcFilePath ?? ""))
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

        public static void MngBaseParamterInit()
        {
            SelectProjectType = Properties.Settings.Default.ProjectSelectIndex;
            UDS_TX_ID = (ushort)Properties.Settings.Default.UDS_TX_ID;
            UDS_RX_ID = (ushort)Properties.Settings.Default.UDS_RX_ID;
            FlashDriverSelectIndex = (ushort)Properties.Settings.Default.FlashDriverSelectIndex;
        }

        public static bool IsSelectProjectType(SelectProjectTypeEnum selectProjectType)
        { 
            return SelectProjectType == (int)selectProjectType;
        }

        private static int GetSendDataDlc(int len)
        {
            if (len <= 0)
            {
                return 0;
            }
            else if (len <= 1)
            {
                return 1;
            }
            else if (len <= 2)
            {
                return 2;
            }
            else if (len <= 3)
            {
                return 3;
            }
            else if (len <= 4)
            {
                return 4;
            }
            else if (len <= 5)
            {
                return 5;
            }
            else if (len <= 6)
            {
                return 6;
            }
            else if (len <= 7)
            {
                return 7;
            }
            else if (len <= 8)
            {
                return 8;
            }
            else if (len <= 12)
            {
                return 12;
            }
            else if (len <= 16)
            {
                return 16;
            }
            else if (len <= 20)
            {
                return 20;
            }
            else if (len <= 24)
            {
                return 24;
            }
            else if (len <= 32)
            {
                return 32;
            }
            else if (len <= 48)
            {
                return 48;
            }
            else
            {
                return 64;
            }
        }

        public static int GetCanSetDlc()
        {
            return GetSendDataDlc(DataDlc);
        }
        public static void SetCanSetDlc(int len)
        {
            DataDlc = GetSendDataDlc(len);
        }
    }
}
