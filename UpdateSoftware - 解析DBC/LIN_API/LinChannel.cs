using Newtonsoft.Json;
using System.Collections.Generic;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// 逻辑 LIN 通道配置（独立于 CAN 的 CanBusChannel）
    /// </summary>
    public class LinChannel
    {
        /// <summary>通道名（默认 "LIN1"）</summary>
        public string Name = "LIN1";

        /// <summary>硬件类型：LinConfig.HwTypePcan("PCAN") | LinConfig.HwTypeCanoe("CANoe")</summary>
        public string HwType = "";

        /// <summary>
        /// 硬件通道标识：
        /// PEAK: "{硬件名}:{LIN通道号}"（如 "PCAN-USB Pro FD:LIN0"，同一硬件多 LIN 通道各占一条）
        /// CANoe: "ch {物理通道索引}"（如 "ch 3"）
        /// </summary>
        public string HwHandle = "";

        /// <summary>
        /// 通道级节点模式。它决定底层硬件能力；发送页中的 Type 再决定每条报文的具体原语。
        /// </summary>
        public LinNodeMode Mode = LinNodeMode.Master;

        /// <summary>通道级本机从节点选择；空值表示从节点仅监听。</summary>
        public string LocalNodeName = "";

        /// <summary>发送页签配置；每条报文可独立选择 Master/Slave/HeaderOnly/BreakOnly。</summary>
        public List<LinTransmitEntry> TransmitEntries = new List<LinTransmitEntry>();

        /// <summary>波特率（1000/2400/9600/19200，默认 19200）</summary>
        public uint Baudrate = 19200;

        /// <summary>LDF 文件路径（可空；空 = 无符号名，只显示裸 PID）</summary>
        public string LdfPath = "";

        /// <summary>运行期 LDF 解析结果（不序列化）</summary>
        [JsonIgnore]
        public LinLdfFile LdfHelper;

        /// <summary>是否已连接（运行期状态，不序列化）</summary>
        [JsonIgnore]
        public bool IsConnected;

        /// <summary>连接失败原因（运行期状态，不序列化）</summary>
        [JsonIgnore]
        public string ConnectError = "";

        public LinChannel Clone()
        {
            var clone = (LinChannel)MemberwiseClone();
            clone.TransmitEntries = new List<LinTransmitEntry>();
            if (TransmitEntries != null)
                foreach (var entry in TransmitEntries) clone.TransmitEntries.Add(entry == null ? null : entry.Clone());
            return clone;
        }

        public LinTransmitEntry FindTransmitEntry(byte pid)
        {
            if (TransmitEntries == null) TransmitEntries = new List<LinTransmitEntry>();
            foreach (var entry in TransmitEntries)
                if (entry != null && entry.Pid == pid) return entry;
            return null;
        }

        public LinTransmitEntry GetOrCreateTransmitEntry(byte pid, byte dlc, LinTransmitType type)
        {
            var entry = FindTransmitEntry(pid);
            if (entry == null)
            {
                entry = new LinTransmitEntry { Pid = pid, Dlc = dlc, Type = type, SlotMs = 15 };
                entry.Data = new byte[dlc == 0 ? 8 : dlc];
                TransmitEntries.Add(entry);
            }
            else
            {
                entry.Type = type;
                if (entry.Dlc == 0) entry.Dlc = dlc;
                if (entry.Data == null) entry.Data = new byte[entry.Dlc == 0 ? 8 : entry.Dlc];
            }
            return entry;
        }

        /// <summary>
        /// 返回驱动连接所需的基础模式。通道面板的显式从节点选择优先，
        /// 否则 PLIN/XL 驱动仍然需要从发送计划推导底层模式：纯 Slave 计划用 Slave，
        /// 只要计划包含 Master 或 HeaderOnly 就用 Master。
        /// </summary>
        public LinNodeMode GetHardwareMode()
        {
            // 通道面板中的显式“从节点”选择优先于发送项推导；否则旧配置中仍保留
            // Master 发送项时，用户切到从节点后硬件会被错误地以 Master 打开。
            if (Mode == LinNodeMode.Slave) return LinNodeMode.Slave;

            bool hasSlave = false;
            bool hasMasterCapability = false;
            foreach (var entry in TransmitEntries ?? new List<LinTransmitEntry>())
            {
                if (entry == null || !entry.Enabled) continue;
                if (entry.Type == LinTransmitType.Slave) hasSlave = true;
                else if (entry.Type == LinTransmitType.Master || entry.Type == LinTransmitType.HeaderOnly)
                    hasMasterCapability = true;
            }

            return hasSlave && !hasMasterCapability ? LinNodeMode.Slave : LinNodeMode.Master;
        }

        public bool HasEnabledSlaveEntries
        {
            get
            {
                foreach (var entry in TransmitEntries ?? new List<LinTransmitEntry>())
                    if (entry != null && entry.Enabled && entry.Type == LinTransmitType.Slave) return true;
                return false;
            }
        }

        public bool HasEnabledMasterEntries
        {
            get
            {
                foreach (var entry in TransmitEntries ?? new List<LinTransmitEntry>())
                    if (entry != null && entry.Enabled &&
                        (entry.Type == LinTransmitType.Master || entry.Type == LinTransmitType.HeaderOnly)) return true;
                return false;
            }
        }

        /// <summary>
        /// PCAN/PLIN 的硬件模式是通道级能力，不能在同一物理通道同时切换 Master 与 Slave。
        /// 返回空表示发送计划没有混合角色；混合计划仍可保存，但 Slave 项不会在 Master 硬件模式下自动应答。
        /// </summary>
        public string GetHardwareModeNotice()
        {
            return HasEnabledSlaveEntries && HasEnabledMasterEntries
                ? "当前发送计划是混合角色，同时包含 Master/HeaderOnly 与 Slave；PCAN/PLIN 单通道只能使用一种硬件模式，Slave 项在 Master 模式下不会自动应答"
                : "";
        }

        /// <summary>校验发送页配置。</summary>
        public string ValidateTransmitPlan()
        {
            var configuredPids = new HashSet<byte>();
            if (TransmitEntries != null)
                foreach (var entry in TransmitEntries)
                {
                    if (entry == null) continue;
                    if (entry.Pid > 0x3F) return "发送项 PID 必须在 0x00-0x3F 范围内: 0x" + entry.Pid.ToString("X2");
                    if (entry.Dlc > 8) return "发送项 PID 0x" + entry.Pid.ToString("X2") + " 的 DLC 不能超过 8";
                    if (!configuredPids.Add(entry.Pid))
                        return "发送页不能配置重复 PID: 0x" + entry.Pid.ToString("X2") + "；每个 LIN ID 只能有一个本机角色";
                    if (!entry.Enabled) continue;
                    if (entry.Type == LinTransmitType.BreakOnly)
                        return "发送项 PID 0x" + entry.Pid.ToString("X2") + " 使用 BreakOnly，但当前 PCAN/Vector 适配器不提供独立 Break 原语";
                }
            return "";
        }
    }
}
