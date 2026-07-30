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
        /// 通道号（记录文件 BLF/ASC 的通道字段），通道管理窗口可自定义编辑；
        /// 实时模式：落盘导出时按此写入文件通道字段；报文模式：回放时按此把文件通道号匹配回逻辑通道
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
