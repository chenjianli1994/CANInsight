using Newtonsoft.Json;

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

        /// <summary>节点模式：主节点（调度表驱动）/ 从节点（硬件自动应答）</summary>
        public LinNodeMode Mode = LinNodeMode.Master;

        /// <summary>本机仿真的 LDF 从节点名；空表示从节点模式仅监听，不自动应答。</summary>
        public string LocalNodeName = "";

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
            return (LinChannel)MemberwiseClone();
        }
    }
}
