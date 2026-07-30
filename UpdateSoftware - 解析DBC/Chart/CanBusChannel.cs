using PCAN_Client.CAN_Data;

namespace PCAN_Client
{
    /// <summary>
    /// CAN总线解析通道配置
    /// 每个通道对应BLF文件中一路CAN总线，拥有独立的DBC解析实例
    /// </summary>
    public class CanBusChannel
    {
        /// <summary>
        /// 显示名称，如 "CAN1", "CAN3"
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// BLF文件中的通道号（对应 mChannel 字段），同时作为全应用统一的逻辑通道号（1..N）
        /// </summary>
        public byte BlfChannelId { get; set; } = 0;

        /// <summary>
        /// 绑定的物理硬件通道号（PCAN USBBUS序号 / CANoe(Vector)通道号），实时收发时与逻辑通道号映射；
        /// 0=未单独配置（生效值=逻辑通道号，见 BaseParamter.GetEffectiveHwChannel）
        /// </summary>
        public byte HwChannel { get; set; } = 0;

        /// <summary>
        /// 绑定的硬件类型（BaseParamter.HwTypePcan / HwTypeCanoe），在通道管理窗口选择绑定硬件时固化；
        /// ""=未指定（兼容旧配置：连接哪类硬件时被哪类认领，见 BaseParamter.GetEffectiveHwType）
        /// </summary>
        public string HwType { get; set; } = "";

        /// <summary>
        /// 该通道使用的DBC文件路径
        /// </summary>
        public string DbcFilePath { get; set; } = "";

        /// <summary>
        /// 该通道独立的DBC解析实例
        /// </summary>
        public DbcHelper DbcHelper { get; set; }

        /// <summary>
        /// DBC是否已成功加载
        /// </summary>
        public bool IsConfigured => DbcHelper?.dbcFile != null && DbcHelper.dbcFile.messages.Count > 0;

        public CanBusChannel() { }

        public CanBusChannel(string name, byte blfChannelId, string dbcFilePath = "")
        {
            Name = name;
            BlfChannelId = blfChannelId;
            DbcFilePath = dbcFilePath;
        }
    }
}
