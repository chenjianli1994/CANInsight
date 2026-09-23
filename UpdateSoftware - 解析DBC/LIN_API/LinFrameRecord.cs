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

    /// <summary>
    /// 发送页签中的 LIN 发送原语。它描述本机对某一个报文的行为，
    /// 不再把主/从角色绑定到整个通道。
    /// </summary>
    public enum LinTransmitType
    {
        /// <summary>本机发送 Header 和数据（主节点发布帧）。</summary>
        Master = 0,
        /// <summary>等待总线 Header，再由本机发布数据。</summary>
        Slave = 1,
        /// <summary>本机只发送 Header，不提供数据。</summary>
        HeaderOnly = 2,
        /// <summary>只发送 LIN Break；当前 PCAN/Vector 适配器若无原语则明确报告不支持。</summary>
        BreakOnly = 3,
    }

    /// <summary>发送页签的一条报文配置，同时作为调度表的来源。</summary>
    public class LinTransmitEntry
    {
        public byte Pid;
        public LinTransmitType Type = LinTransmitType.Master;
        public bool Enabled = true;
        public int SlotMs = 15;
        public byte Dlc;
        public byte[] Data = new byte[0];

        public LinTransmitEntry Clone()
        {
            return new LinTransmitEntry
            {
                Pid = Pid,
                Type = Type,
                Enabled = Enabled,
                SlotMs = SlotMs,
                Dlc = Dlc,
                Data = Data == null ? new byte[0] : (byte[])Data.Clone(),
            };
        }
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
        /// <summary>裸帧 ID（0x00-0x3F，不含奇偶校验位；奇偶位只在硬件边界生成/剥离，见 LinPidCodec）</summary>
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
        /// <summary>是否由硬件适配器上报的真实总线帧（软件提交回显为 false）。
        /// 用于区分"本机帧确实上了总线"与"仅驱动队列提交回显"：Slave 发送项的硬件
        /// dirPublisher 帧即"外部 Header 已到达并触发本机应答"的真实证据。</summary>
        public bool HwFrame;

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
