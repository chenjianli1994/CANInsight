namespace PCAN_Client.LIN_API
{
    /// <summary>LIN 节点模式</summary>
    public enum LinNodeMode
    {
        /// <summary>主节点：通过调度表驱动总线</summary>
        Master = 0,
        /// <summary>从节点：按响应 ID 自动应答</summary>
        Slave = 1,
    }

    /// <summary>LIN 帧方向（相对本工具）</summary>
    public enum LinFrameDir
    {
        /// <summary>本端发送</summary>
        Tx = 0,
        /// <summary>总线接收（含从节点应答）</summary>
        Rx = 1,
    }

    /// <summary>LIN 帧类型</summary>
    public enum LinFrameType
    {
        /// <summary>无条件帧</summary>
        Unconditional = 0,
        /// <summary>事件触发帧</summary>
        EventTriggered = 1,
        /// <summary>偶发帧</summary>
        Sporadic = 2,
        /// <summary>诊断帧（0x3C MasterReq / 0x3D SlaveResp）</summary>
        Diagnostic = 3,
        /// <summary>未定义（无 LDF 或 LDF 未收录）</summary>
        Undefined = 4,
    }

    /// <summary>校验和类型</summary>
    public enum LinChecksumKind
    {
        /// <summary>增强校验（LIN 2.x，含 PID）</summary>
        Enhanced = 0,
        /// <summary>经典校验（LIN 1.x，不含 PID）</summary>
        Classic = 1,
    }

    /// <summary>LIN 帧错误类型</summary>
    public enum LinErrorKind
    {
        /// <summary>无错误</summary>
        None = 0,
        /// <summary>校验和错误</summary>
        Checksum = 1,
        /// <summary>同步间隔（Break/Sync）错误</summary>
        Sync = 2,
        /// <summary>无应答（Header 后无从节点响应）</summary>
        NoResponse = 3,
        /// <summary>硬件错误</summary>
        Hw = 4,
    }

    /// <summary>
    /// LIN 帧记录（独立于 CAN 报文模型，与 CAN 完全解耦）
    /// </summary>
    public struct LinFrameRecord
    {
        /// <summary>时间戳（微秒，会话起点归零）</summary>
        public ulong TimestampUs;
        /// <summary>逻辑 LIN 通道号（1 起）</summary>
        public byte LogicChannel;
        /// <summary>受保护帧 ID（PID，0x00-0x3F）</summary>
        public byte Pid;
        /// <summary>方向</summary>
        public LinFrameDir Direction;
        /// <summary>帧类型</summary>
        public LinFrameType FrameType;
        /// <summary>数据长度（0-8）</summary>
        public byte Dlc;
        /// <summary>数据（长度 0-8）</summary>
        public byte[] Data;
        /// <summary>校验和类型</summary>
        public LinChecksumKind ChecksumType;
        /// <summary>总线上收到的校验和字节（发送帧=计算值；校验和错误帧=总线上的错误值）</summary>
        public byte ChecksumRx;
        /// <summary>校验和验证结果（接收帧有效）</summary>
        public bool ChecksumOk;
        /// <summary>错误类型</summary>
        public LinErrorKind ErrorKind;
        /// <summary>帧名称（LDF 符号名，无 LDF 时为 "0x{pid:X2}"）</summary>
        public string FrameName;

        public string DataHex
        {
            get
            {
                if (Data == null || Data.Length == 0) return "";
                var sb = new System.Text.StringBuilder(Data.Length * 3);
                for (int i = 0; i < Data.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(Data[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        public string StatusText
        {
            get
            {
                switch (ErrorKind)
                {
                    case LinErrorKind.Checksum: return "错误·校验和";
                    case LinErrorKind.Sync: return "错误·同步";
                    case LinErrorKind.NoResponse: return "错误·无应答";
                    case LinErrorKind.Hw: return "错误·硬件";
                    default: return "OK";
                }
            }
        }
    }
}
