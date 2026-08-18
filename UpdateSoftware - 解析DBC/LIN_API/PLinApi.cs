using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PCAN_Client.LIN_API
{
    // ==================== PEAK PLinApi 枚举（PLinApi.h 官方定义） ====================

    /// <summary>PLinApi 错误码</summary>
    internal enum LinPlError : int
    {
        errOK = 0,
        errXmtQueueFull = 1,
        errIllegalPeriod = 2,
        errRcvQueueEmpty = 3,
        errIllegalChecksumType = 4,
        errIllegalHardware = 5,
        errIllegalClient = 6,
        errWrongParameterType = 7,
        errWrongParameterValue = 8,
        errIllegalDirection = 9,
        errIllegalLength = 10,
        errIllegalBaudrate = 11,
        errIllegalFrameID = 12,
        errBufferInsufficient = 13,
        errIllegalScheduleNo = 14,
        errIllegalSlotCount = 15,
        errIllegalIndex = 16,
        errIllegalRange = 17,
        errOutOfResource = 1001,
        errManagerNotLoaded = 1002,
        errManagerNotResponding = 1003,
        errMemoryAccess = 1004,
        errNotImplemented = 0xFFFE,
        errUnknown = 0xFFFF,
    }

    /// <summary>接收消息类型（含总线状态消息）</summary>
    internal enum LinPlMsgType : byte
    {
        mstStandard = 0,
        mstBusSleep = 1,
        mstBusWakeUp = 2,
        mstAutobaudrateTimeOut = 3,
        mstAutobaudrateReply = 4,
        mstOverrun = 5,
        mstQueueOverrun = 6,
    }

    /// <summary>帧方向</summary>
    internal enum LinPlDirection : byte
    {
        dirDisabled = 0,
        dirPublisher = 1,
        dirSubscriber = 2,
        dirSubscriberAutoLength = 3,
    }

    /// <summary>校验和类型</summary>
    internal enum LinPlChecksumType : byte
    {
        cstCustom = 0,
        cstClassic = 1,
        cstEnhanced = 2,
        cstAuto = 3,
    }

    /// <summary>硬件模式</summary>
    internal enum LinPlHardwareMode : byte
    {
        modNone = 0,
        modSlave = 1,
        modMaster = 2,
    }

    /// <summary>总线状态</summary>
    internal enum LinPlHardwareState : byte
    {
        hwsNotInitialized = 0,
        hwsAutobaudrate = 1,
        hwsActive = 2,
        hwsSleep = 3,
        hwsShortGround = 6,
    }

    /// <summary>调度槽类型</summary>
    internal enum LinPlSlotType : byte
    {
        sltUnconditional = 0,
        sltEvent = 1,
        sltSporadic = 2,
        sltMasterRequest = 3,
        sltSlaveResponse = 4,
    }

    /// <summary>客户端参数</summary>
    internal enum LinPlClientParam : ushort
    {
        clpName = 1,
        clpMessagesOnQueue = 2,
        clpWindowHandle = 3,
        clpConnectedHardware = 4,
        clpTransmittedMessages = 5,
        clpReceivedMessages = 6,
        clpReceiveStatusFrames = 7,
        clpOnReceiveEventHandle = 8,
        clpOnPluginEventHandle = 9,
    }

    /// <summary>硬件参数</summary>
    internal enum LinPlHardwareParam : ushort
    {
        hwpName = 1,
        hwpDeviceNumber = 2,
        hwpChannelNumber = 3,
        hwpConnectedClients = 4,
        hwpMessageFilter = 5,
        hwpBaudrate = 6,
        hwpMode = 7,
        hwpFirmwareVersion = 8,
        hwpBufferOverrunCount = 9,
        hwpBossClient = 10,
        hwpSerialNumber = 11,
        hwpVersion = 12,
        hwpType = 13,
        hwpQueueOverrunCount = 14,
        hwpIdNumber = 15,
        hwpUserData = 16,
    }

    /// <summary>接收帧错误标志（按位）</summary>
    [Flags]
    internal enum LinPlMsgErrors : int
    {
        None = 0,
        InconsistentSynch = 0x1,   // 同步字段错误
        IdParityBit0 = 0x2,        // PID 校验位 0 错误
        IdParityBit1 = 0x4,        // PID 校验位 1 错误
        SlaveNOtResponding = 0x8,  // 从节点无响应
        Timeout = 0x10,            // 超时
        Checksum = 0x20,           // 校验和错误
        GroundShort = 0x40,        // 总线对地短路
        VBatShort = 0x80,          // 总线对电源短路
        SlotDelay = 0x100,         // 调度槽时隙过小
        OtherResponse = 0x200,     // 其他节点响应
    }

    // ==================== PLinApi 结构体 ====================

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinPlMsg
    {
        /// <summary>帧 ID（0-63，不含校验位）</summary>
        public byte FrameId;
        /// <summary>数据长度（1-8）</summary>
        public byte Length;
        public LinPlDirection Direction;
        public LinPlChecksumType ChecksumType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;
        public byte Checksum;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinPlRcvMsg
    {
        public LinPlMsgType Type;
        /// <summary>帧 ID（0-63）</summary>
        public byte FrameId;
        public byte Length;
        public LinPlDirection Direction;
        public LinPlChecksumType ChecksumType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;
        /// <summary>总线上收到的校验和字节</summary>
        public byte Checksum;
        public LinPlMsgErrors ErrorFlags;
        /// <summary>时间戳（微秒，硬件计时）</summary>
        public UInt64 TimeStamp;
        /// <summary>来源硬件句柄</summary>
        public ushort hHw;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinPlFrameEntry
    {
        /// <summary>帧 ID（0-63）</summary>
        public byte FrameId;
        public byte Length;
        public LinPlDirection Direction;
        public LinPlChecksumType ChecksumType;
        /// <summary>FRAME_FLAG_RESPONSE_ENABLE 等</summary>
        public ushort Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] InitialData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinPlScheduleSlot
    {
        public LinPlSlotType Type;
        /// <summary>时隙（毫秒）</summary>
        public ushort Delay;
        /// <summary>帧 ID 数组（偶发帧多个，无条件帧只 1 个有效）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] FrameId;
        /// <summary>偶发帧 ID 计数 / 事件帧解析调度号</summary>
        public byte CountResolve;
        /// <summary>槽句柄（只读）</summary>
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinPlHardwareStatus
    {
        public LinPlHardwareMode Mode;
        public LinPlHardwareState Status;
        public byte FreeOnSendQueue;
        public ushort FreeOnSchedulePool;
        public ushort ReceiveBufferOverrun;
    }

    // ==================== P/Invoke 封装 ====================

    /// <summary>
    /// PEAK PLinApi.dll 原生封装（导出表与签名经 PEAK 官方 PLIN-API 文档核实）
    /// </summary>
    internal static class LinPlApi
    {
        public const byte INVALID_LIN_HANDLE = 0;
        public const byte LIN_MAX_FRAME_ID = 63;
        public const int LIN_MAX_SCHEDULES = 8;
        public const int LIN_MAX_SCHEDULE_SLOTS = 256;
        public const ushort LIN_MIN_BAUDRATE = 1000;
        public const ushort LIN_MAX_BAUDRATE = 20000;
        public const ushort LIN_MAX_NAME_LENGTH = 48;
        public const int FRAME_FLAG_RESPONSE_ENABLE = 1;
        public const int FRAME_FLAG_SINGLE_SHOT = 2;
        public const int FRAME_FLAG_IGNORE_INIT_DATA = 4;

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_RegisterClient")]
        internal static extern LinPlError RegisterClient(string strName, IntPtr hWnd, out byte hClient);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_RemoveClient")]
        internal static extern LinPlError RemoveClient(byte hClient);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_ConnectClient")]
        internal static extern LinPlError ConnectClient(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_DisconnectClient")]
        internal static extern LinPlError DisconnectClient(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_ResetClient")]
        internal static extern LinPlError ResetClient(byte hClient);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SetClientParam")]
        internal static extern LinPlError SetClientParam(byte hClient, LinPlClientParam wParam, int dwValue);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetClientParam")]
        internal static extern LinPlError GetClientParam(byte hClient, LinPlClientParam wParam, out int pBuff, ushort wBuffSize);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SetClientFilter")]
        internal static extern LinPlError SetClientFilter(byte hClient, ushort hHw, UInt64 iRcvMask, ushort wFilterType);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_Read")]
        internal static extern LinPlError Read(byte hClient, out LinPlRcvMsg pMsg);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_ReadMulti")]
        internal static extern LinPlError ReadMulti(byte hClient, [In, Out] LinPlRcvMsg[] pMsgBuff, int iMaxCount, out int pCount);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_Write")]
        internal static extern LinPlError Write(byte hClient, ushort hHw, ref LinPlMsg pMsg);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_InitializeHardware")]
        internal static extern LinPlError InitializeHardware(byte hClient, ushort hHw, LinPlHardwareMode byMode, ushort wBaudrate);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetAvailableHardware")]
        internal static extern LinPlError GetAvailableHardware([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ushort[] pBuff, ushort wBuffSize, out ushort pCount);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SetHardwareParam")]
        internal static extern LinPlError SetHardwareParam(byte hClient, ushort hHw, LinPlHardwareParam wParam, ref UInt64 pBuff, ushort wBuffSize);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetHardwareParam")]
        internal static extern LinPlError GetHardwareParam(ushort hHw, LinPlHardwareParam wParam, out int pBuff, ushort wBuffSize);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetHardwareParam")]
        internal static extern LinPlError GetHardwareParam(ushort hHw, LinPlHardwareParam wParam, [MarshalAs(UnmanagedType.LPStr, SizeParamIndex = 3)] StringBuilder pBuff, ushort wBuffLen);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetHardwareParam")]
        internal static extern LinPlError GetHardwareParam(ushort hHw, LinPlHardwareParam wParam, out UInt64 pBuff, ushort wBuffLen);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_ResetHardware")]
        internal static extern LinPlError ResetHardware(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_IdentifyHardware")]
        internal static extern LinPlError IdentifyHardware(ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_RegisterFrameId")]
        internal static extern LinPlError RegisterFrameId(byte hClient, ushort hHw, byte bFromFrameId, byte bToFrameId);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SetFrameEntry")]
        internal static extern LinPlError SetFrameEntry(byte hClient, ushort hHw, ref LinPlFrameEntry pFrameEntry);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetFrameEntry")]
        internal static extern LinPlError GetFrameEntry(ushort hHw, ref LinPlFrameEntry pFrameEntry);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_UpdateByteArray")]
        internal static extern LinPlError UpdateByteArray(byte hClient, ushort hHw, byte bFrameId, byte bIndex, byte bLen, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] byte[] pData);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SetSchedule")]
        internal static extern LinPlError SetSchedule(byte hClient, ushort hHw, int iScheduleNumber, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] [In, Out] LinPlScheduleSlot[] pSchedule, int iSlotCount);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetSchedule")]
        internal static extern LinPlError GetSchedule(ushort hHw, int iScheduleNumber, [In, Out] LinPlScheduleSlot[] pScheduleBuff, int iMaxSlotCount, out int pSlotCount);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_DeleteSchedule")]
        internal static extern LinPlError DeleteSchedule(byte hClient, ushort hHw, int iScheduleNumber);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_StartSchedule")]
        internal static extern LinPlError StartSchedule(byte hClient, ushort hHw, int iScheduleNumber);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_SuspendSchedule")]
        internal static extern LinPlError SuspendSchedule(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_ResumeSchedule")]
        internal static extern LinPlError ResumeSchedule(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_XmtWakeUp")]
        internal static extern LinPlError XmtWakeUp(byte hClient, ushort hHw);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_StartAutoBaud")]
        internal static extern LinPlError StartAutoBaud(byte hClient, ushort hHw, ushort wTimeOut);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetStatus")]
        internal static extern LinPlError GetStatus(ushort hHw, out LinPlHardwareStatus pStatusBuff);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_CalculateChecksum")]
        internal static extern LinPlError CalculateChecksum(ref LinPlMsg pMsg);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetVersionInfo")]
        internal static extern LinPlError GetVersionInfo([MarshalAs(UnmanagedType.LPStr, SizeParamIndex = 1)] StringBuilder pTextBuff, int wBuffSize);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetErrorText")]
        internal static extern LinPlError GetErrorText(LinPlError dwError, byte bLanguage, [MarshalAs(UnmanagedType.LPStr, SizeParamIndex = 3)] StringBuilder strTextBuff, int wBuffSize);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetPID")]
        internal static extern LinPlError GetPID(ref byte pFrameId);

        [DllImport("PLinApi.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "LIN_GetSystemTime")]
        internal static extern LinPlError GetSystemTime(out UInt64 pTargetTime);
    }
}
