using System.Collections.Generic;
using System.Text;

namespace PCAN_Client.LIN_API
{
    /// <summary>
    /// PLinApi 错误码 → 中文提示 + 官方错误文本兜底
    /// </summary>
    internal static class LinPlErrorCodes
    {
        private static readonly Dictionary<LinPlError, string> Map = new Dictionary<LinPlError, string>
        {
            { LinPlError.errXmtQueueFull, "发送队列已满（总线忙或发送过快）" },
            { LinPlError.errIllegalPeriod, "周期时间非法" },
            { LinPlError.errRcvQueueEmpty, "接收队列为空" },
            { LinPlError.errIllegalChecksumType, "校验和类型非法" },
            { LinPlError.errIllegalHardware, "硬件句柄非法（硬件已拔出或被占用）" },
            { LinPlError.errIllegalClient, "客户端句柄非法" },
            { LinPlError.errWrongParameterType, "参数类型非法" },
            { LinPlError.errWrongParameterValue, "参数值非法" },
            { LinPlError.errIllegalDirection, "帧方向非法" },
            { LinPlError.errIllegalLength, "帧长度超出 1-8 范围" },
            { LinPlError.errIllegalBaudrate, "波特率超出 1000-20000 范围" },
            { LinPlError.errIllegalFrameID, "帧 ID 超出 0-63 范围" },
            { LinPlError.errBufferInsufficient, "缓冲区过小" },
            { LinPlError.errIllegalScheduleNo, "调度表编号超出 0-7 范围" },
            { LinPlError.errIllegalSlotCount, "调度槽数量超出硬件容量（最多 256 槽）" },
            { LinPlError.errIllegalIndex, "数组索引越界" },
            { LinPlError.errIllegalRange, "更新字节范围非法" },
            { LinPlError.errOutOfResource, "LIN 管理器资源不足" },
            { LinPlError.errManagerNotLoaded, "PLIN 管理器未运行（请确认已安装 PEAK LIN 驱动）" },
            { LinPlError.errManagerNotResponding, "与 PLIN 管理器通信中断（硬件可能已拔出）" },
            { LinPlError.errMemoryAccess, "PLinApi 内存访问异常" },
            { LinPlError.errNotImplemented, "PLinApi 未实现该功能" },
            { LinPlError.errUnknown, "PLinApi 未知内部错误" },
        };

        /// <summary>
        /// 错误码 → 中文描述；未知码追加官方原文
        /// </summary>
        public static string ToChinese(LinPlError err)
        {
            string text;
            if (Map.TryGetValue(err, out text))
            {
                string official = GetOfficialText(err);
                return official.Length > 0 ? $"{text}（{official}）" : text;
            }
            string off = GetOfficialText(err);
            return off.Length > 0 ? off : $"PLinApi 错误 0x{unchecked((int)err):X8}";
        }

        private static string GetOfficialText(LinPlError err)
        {
            try
            {
                var sb = new StringBuilder(128);
                if (LinPlApi.GetErrorText(err, 0, sb, sb.Capacity) == LinPlError.errOK)
                    return sb.ToString();
            }
            catch { /* DLL 未加载等场景静默 */ }
            return "";
        }
    }
}
