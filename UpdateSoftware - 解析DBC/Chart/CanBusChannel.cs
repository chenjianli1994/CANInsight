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
        /// BLF文件中的通道号（对应 mChannel 字段）
        /// </summary>
        public byte BlfChannelId { get; set; } = 0;

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
