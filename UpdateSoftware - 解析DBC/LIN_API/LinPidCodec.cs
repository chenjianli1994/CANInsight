using System;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// LIN 裸 ID（0x00-0x3F）与受保护 PID（裸 ID + 2 位奇偶校验位）的双向转换。
    /// 系统内部（LDF、发送计划、UI、超时键、LinFrameRecord.Pid）一律使用裸 6-bit ID；
    /// 仅在 PEAK/Vector 硬件边界调用本编解码：发送前 <see cref="ToProtectedPid"/>，
    /// 接收后 <see cref="ToRawId"/>/<see cref="TryDecode"/>。禁止让奇偶校验位进入内部模型。
    /// 奇偶公式（LIN 2.x）：P0 = ID0⊕ID1⊕ID2⊕ID4；P1 = ¬(ID1⊕ID3⊕ID4⊕ID5)。
    /// </summary>
    public static class LinPidCodec
    {
        /// <summary>裸 6-bit ID → 受保护 PID（bit6=P0，bit7=P1）。</summary>
        public static byte ToProtectedPid(byte rawId)
        {
            byte id = (byte)(rawId & 0x3F);
            byte p0 = (byte)(((id >> 0) ^ (id >> 1) ^ (id >> 2) ^ (id >> 4)) & 1);
            byte p1 = (byte)((((id >> 1) ^ (id >> 3) ^ (id >> 4) ^ (id >> 5) ^ 1)) & 1);
            return (byte)(id | (p0 << 6) | (p1 << 7));
        }

        /// <summary>受保护 PID → 裸 6-bit ID（低 6 位，丢弃奇偶位）。</summary>
        public static byte ToRawId(byte protectedPid)
        {
            return (byte)(protectedPid & 0x3F);
        }

        /// <summary>
        /// 解码受保护 PID 并校验奇偶位。parityOk=false 表示总线上的 PID 奇偶位与裸 ID 不符
        /// （对应 PLIN ErrorFlags IdParityBit0/IdParityBit1）；此时 rawId 仍取低 6 位，
        /// 供错误帧关联显示对应裸 ID，原始 PID 保留在诊断日志中。
        /// </summary>
        public static bool TryDecode(byte protectedPid, out byte rawId, out bool parityOk)
        {
            rawId = ToRawId(protectedPid);
            parityOk = ToProtectedPid(rawId) == protectedPid;
            // 任何受保护 PID 都能提取出裸 ID；奇偶正确性用 parityOk 表达，不以返回值阻断。
            return true;
        }
    }
}