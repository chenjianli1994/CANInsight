namespace PCAN_Client
{
    /// <summary>
    /// 识别到的一路硬件通道（通道管理窗口、连接映射、识别摘要共用的结构化载体）
    /// </summary>
    public class HwChannelInfo
    {
        public byte Hw;           // 硬件通道号（PCAN USBBUS序号 / CANoe通道号）
        public string HwType;     // 硬件类型（BaseParamter.HwTypePcan / HwTypeCanoe）
        public string Name;       // 显示名，如 "USB_1"、"VN1640A Channel 1"
        public string Status;     // 状态，如 "空闲"、"已占用"、"已连接"

        /// <summary>带类型前缀的显示名，如 "PCAN USB_1(空闲)"</summary>
        public override string ToString()
        {
            string prefix = string.IsNullOrEmpty(HwType) ? "" : HwType + " ";
            string suffix = string.IsNullOrEmpty(Status) ? "" : "(" + Status + ")";
            return prefix + Name + suffix;
        }
    }
}
