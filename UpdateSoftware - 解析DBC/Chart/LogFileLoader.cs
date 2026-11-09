using PCAN_Client.CAN_Data;
using PCAN_Client.CAN_Data.blf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace PCAN_Client
{
    /// <summary>
    /// CAN报文数据结构（从文件读取的原始数据）
    /// </summary>
    public class CanRawMessageRead
    {
        public uint CanId { get; set; }
        public byte[] Data { get; set; }
        public double TimeStampSeconds { get; set; } // 绝对时间戳（秒）
        public long FilePosition { get; set; } // 读取时文件中的字节位置（用于进度条）
        public byte Channel { get; set; }       // CAN通道号（BLF文件）
        /// <summary>该帧来自 CAN FD（BLF 的 FD 对象 / ASC 的 CANFD 行）；格式转换据此写 FD 行或 FD 对象</summary>
        public bool IsFd { get; set; }
    }

    public class CanRawMessage
    {
        public uint CanId { get; set; }
        public byte[] Data { get; set; }
        public float TimeStampSeconds { get; set; } // 绝对时间戳（秒）
        public byte Channel { get; set; }       // CAN通道号
    }

    /// <summary>
    /// 日志文件加载器 - 支持BLF/BIN/ASC文件流式读取
    /// </summary>
    public static class LogFileLoader
    {
        /// <summary>
        /// 一次加载所有CAN报文（适用于小文件）
        /// </summary>
        public static List<CanRawMessage> LoadCanMessages(string filePath)
        {
            CanRawMessage message = new CanRawMessage();
            var list = new List<CanRawMessage>();
            foreach (var msg in EnumerateCanMessages(filePath))
            {
                message.CanId = msg.CanId;
                message.Data = msg.Data;
                message.TimeStampSeconds = (float)msg.TimeStampSeconds;
                list.Add(message);
            }
            return list;
        }

        /// <summary>
        /// 流式读取CAN报文，读一条 yield 一条，调用方可立即处理
        /// </summary>
        public static IEnumerable<CanRawMessageRead> EnumerateCanMessages(string filePath, HashSet<byte> channelFilter = null)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".blf")
            {
                foreach (var msg in EnumerateBlfMessages(filePath, channelFilter))
                    yield return msg;
            }
            else if (ext == ".bin")
            {
                foreach (var msg in EnumerateBinMessages(filePath))
                    yield return msg;
            }
            else if (ext == ".asc")
            {
                foreach (var msg in EnumerateAscMessages(filePath, channelFilter))
                    yield return msg;
            }
            else
            {
                throw new NotSupportedException($"不支持的文件格式: {ext}");
            }
        }

        #region BLF文件流式读取

        private static IEnumerable<CanRawMessageRead> EnumerateBlfMessages(string filePath, HashSet<byte> channelFilter = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            IntPtr fileHandle = BLFAPI.BLCreateFileW(filePath, GENERIC.GENERIC_READ);
            if (fileHandle == IntPtr.Zero)
                throw new Exception($"无法打开BLF文件: {filePath}");

            try
            {
                VBLFileStatistics stats;
                stats.mStatisticsSize = 28;
                int retval = BLFAPI.BLGetFileStatistics(fileHandle, out stats);

                double startTimeUs = -1;
                long approximatePosition = 0; // BLF文件无法获取精确位置，用近似值
                VBLObjectHeaderBase headerBase;

                while (retval == 1)
                {
                    retval = BLFAPI.BLPeekObject(fileHandle, out headerBase);
                    if (retval != 1) break;

                    // 更新近似位置（每个对象的大小）
                    approximatePosition += (long)headerBase.mObjectSize;

                    double timestampUs = 0;
                    uint canId = 0;
                    uint dataLen = 0;
                    byte[] datas = null;
                    byte channel = 0;
                    // 该对象是 CAN FD 报文（转换时按 FD 写回，避免 8 字节 FD 帧退化成经典帧）
                    bool isFdObject = headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE
                                   || headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64;

                    if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE)
                    {
                        timestampUs = ReadCANMessage(fileHandle, headerBase, ref canId, ref dataLen, ref datas, ref channel);
                    }
                    else if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE2)
                    {
                        timestampUs = ReadCANMessage2(fileHandle, headerBase, ref canId, ref dataLen, ref datas, ref channel);
                    }
                    else if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE)
                    {
                        timestampUs = ReadCANFDMessage16(fileHandle, headerBase, ref canId, ref dataLen, ref datas, ref channel);
                    }
                    else if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64)
                    {
                        timestampUs = ReadCANFDMessage64(fileHandle, headerBase, ref canId, ref dataLen, ref datas, ref channel);
                    }
                    else
                    {
                        SkipObject(fileHandle, headerBase);
                        continue;
                    }

                    // 通道筛选
                    if (channelFilter != null && !channelFilter.Contains(channel))
                        continue;

                    if (datas != null && dataLen > 0)
                    {
                        if (startTimeUs < 0) startTimeUs = timestampUs;
                        double relativeTime = (timestampUs - startTimeUs) / 1000000.0;

                        yield return new CanRawMessageRead
                        {
                            CanId = canId & 0x1FFFFFFF,
                            Data = datas,
                            TimeStampSeconds = relativeTime,
                            FilePosition = approximatePosition,
                            Channel = channel,
                            IsFd = isFdObject
                        };
                    }
                }
            }
            finally
            {
                BLFAPI.BLCloseHandle(fileHandle);
            }
        }

        #endregion

        #region BIN文件流式读取

        private static IEnumerable<CanRawMessageRead> EnumerateBinMessages(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            const int BUFFER_SIZE = 512 * 1024;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
            using (var reader = new BinaryReader(stream))
            {
                // 跳过8字节的DateTime头部
                if (stream.Position + 8 <= stream.Length)
                    reader.ReadBytes(8);

                ulong nowTimeUs = 0;

                while (stream.Position + 6 <= stream.Length)
                {
                    byte lengthByte = reader.ReadByte();
                    int dataLength = lengthByte - 6;

                    if (dataLength < 0 || dataLength > 64)
                    {
                        stream.Position++;
                        continue;
                    }

                    if (stream.Position + 5 + dataLength > stream.Length) break;

                    uint canId = (uint)(reader.ReadByte() << 8 | reader.ReadByte());

                    ulong us = (ulong)reader.ReadByte() << 16 |
                               (ulong)reader.ReadByte() << 8 |
                               reader.ReadByte();
                    nowTimeUs += us;

                    byte[] datas = reader.ReadBytes(dataLength);

                    byte[] dataCopy = new byte[dataLength];
                    Array.Copy(datas, dataCopy, dataLength);

                    yield return new CanRawMessageRead
                    {
                        CanId = canId,
                        Data = dataCopy,
                        TimeStampSeconds = nowTimeUs / 1000000.0,
                        FilePosition = stream.Position  // BIN文件：使用精确的字节位置
                    };
                }
            }
        }

        #endregion

        #region ASC文件流式读取

        private static IEnumerable<CanRawMessageRead> EnumerateAscMessages(string filePath, HashSet<byte> channelFilter = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            const int BUFFER_SIZE = 512 * 1024;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, BUFFER_SIZE))
            {
                bool headerParsed = false;
                double startTime = -1;

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!headerParsed)
                    {
                        if (line.StartsWith("date") || line.StartsWith("base") ||
                            line.StartsWith("no internal") || line.StartsWith("//") ||
                            string.IsNullOrWhiteSpace(line))
                            continue;
                        headerParsed = true;
                    }

                    if (line.StartsWith("End TriggerBlock"))
                        continue;

                    var msg = ParseAscLine(line, reader.BaseStream.Position);
                    if (msg != null)
                    {
                        // 通道筛选（与BLF文件行为一致）
                        if (channelFilter != null && !channelFilter.Contains(msg.Channel))
                            continue;

                        // ASC 时间戳为绝对时间，归零到首条报文
                        if (startTime < 0) startTime = msg.TimeStampSeconds;
                        msg.TimeStampSeconds -= (float)startTime;
                        yield return msg;
                    }
                }
            }
        }

        /// <summary>解析一行 ASC（经典格式与 CAN FD 扩展格式都支持）；数据转换的 ASC→CSV 也复用这里</summary>
        internal static CanRawMessageRead ParseAscLine(string line, long filePosition)
        {
            try
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7) return null;

                if (!double.TryParse(parts[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double timestampSeconds))
                    return null;

                // CAN FD 行必须单独解析：Vector 的 FD 格式把通道放在 parts[1]、ID 放在 parts[4]，
                // 而且没有经典格式里的 "d"/"r" 标记（按经典规则解析会取错 ID，并因找不到 "d" 把整行丢掉）
                if (parts[1].Equals("CANFD", StringComparison.OrdinalIgnoreCase))
                    return ParseAscFdLine(parts, timestampSeconds, filePosition);

                if (!TryParseAscCanId(parts[2], out uint canId))
                    return null;

                // 解析通道号（parts[1]）：Vector ASC 为 1-based 数字，部分工具输出 "CAN1" 前缀；
                // 无法解析时默认 1（单通道文件）。此前该列被忽略导致报文 Channel 恒为 0，
                // 通道配置（BlfChannelId）匹配不上，绘图区无法解码绘制
                byte channel = 1;
                if (parts.Length >= 3 && parts[1].Length > 0)
                {
                    if (!byte.TryParse(parts[1],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out channel))
                    {
                        channel = 1;
                        if (parts[1].StartsWith("CAN", StringComparison.OrdinalIgnoreCase))
                            byte.TryParse(parts[1].Substring(3),
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out channel);
                    }
                }

                int dataLengthIndex = -1;
                for (int i = 3; i < parts.Length - 1; i++)
                {
                    if ((parts[i] == "d" || parts[i] == "r") &&
                        i + 1 < parts.Length &&
                        int.TryParse(parts[i + 1], out _))
                    {
                        dataLengthIndex = i + 1;
                        break;
                    }
                }

                if (dataLengthIndex < 0 || dataLengthIndex >= parts.Length) return null;
                if (!int.TryParse(parts[dataLengthIndex], out int dataLength) || dataLength < 0 || dataLength > 64)
                    return null;
                if (dataLengthIndex + dataLength >= parts.Length) return null;

                byte[] datas = new byte[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    if (!byte.TryParse(parts[dataLengthIndex + 1 + i],
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out datas[i]))
                        return null;
                }

                return new CanRawMessageRead
                {
                    CanId = canId,
                    Data = datas,
                    TimeStampSeconds = timestampSeconds,
                    FilePosition = filePosition,  // ASC文件：使用当前流位置
                    Channel = channel
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析 Vector ASC 的 CAN FD 行：
        ///   &lt;time&gt; CANFD &lt;channel&gt; &lt;dir&gt; &lt;id&gt; [&lt;符号名&gt;] &lt;brs&gt; &lt;esi&gt; &lt;dlc&gt; &lt;len&gt; &lt;数据...&gt; &lt;标志&gt; &lt;crc&gt; &lt;位定时...&gt;
        /// dlc 是十六进制码、len 是十进制字节数（如 "f 64" = DLC 15 → 64 字节），用两者互相校验来定位数据起点，
        /// 这样即使 ID 后面带符号名占位也能解析。
        /// </summary>
        private static CanRawMessageRead ParseAscFdLine(string[] parts, double timestampSeconds, long filePosition)
        {
            if (parts.Length < 10) return null;

            if (!TryParseAscCanId(parts[4], out uint canId))
                return null;

            // 通道号：parts[2]（Vector 为 1-based 数字；解析不了时按单通道 1 处理，与经典格式保持一致）
            if (!byte.TryParse(parts[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out byte channel))
                channel = 1;

            // ID 之后可能还有符号名：向后找 <brs> <esi> <dlc> <len> 四连，靠 DLC↔长度一致性排除误判
            for (int i = 5; i + 3 < parts.Length; i++)
            {
                if (!IsAscBitToken(parts[i]) || !IsAscBitToken(parts[i + 1])) continue;
                if (!byte.TryParse(parts[i + 2],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out byte dlc) || dlc > 15) continue;

                int expected = BLFAPI.GetFdDataLength(dlc);
                if (!TryParseAscDataLength(parts[i + 3], expected, out int dataLength)) continue;
                if (dataLength < 0 || dataLength > 64) continue;

                int dataStart = i + 4;
                if (dataStart + dataLength > parts.Length) continue;

                byte[] datas = new byte[dataLength];
                bool ok = true;
                for (int k = 0; k < dataLength; k++)
                {
                    if (!byte.TryParse(parts[dataStart + k],
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out datas[k]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;

                return new CanRawMessageRead
                {
                    CanId = canId,
                    Data = datas,
                    TimeStampSeconds = timestampSeconds,
                    FilePosition = filePosition,
                    Channel = channel,
                    IsFd = true
                };
            }
            return null;
        }

        /// <summary>解析 ASC 的 CAN ID（十六进制）；部分工具给扩展帧加 "x"/"X" 后缀，这里一并容忍</summary>
        private static bool TryParseAscCanId(string token, out uint canId)
        {
            if (!string.IsNullOrEmpty(token) &&
                token.EndsWith("x", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(0, token.Length - 1);
            }
            return uint.TryParse(token,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out canId);
        }

        /// <summary>ASC 里 brs/esi 只可能是 0 或 1</summary>
        private static bool IsAscBitToken(string token)
        {
            return token == "0" || token == "1";
        }

        /// <summary>
        /// 取 FD 行的数据字节数：优先选与 DLC 对应长度一致的解释（十进制/十六进制都试），
        /// 都不一致时退回十进制 0~64（兼容个别工具把长度写成十六进制的情况）。
        /// </summary>
        private static bool TryParseAscDataLength(string token, int expected, out int length)
        {
            if (!int.TryParse(token,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out length))
                return false;
            if (length == expected) return true;
            if (int.TryParse(token,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int asHex) && asHex == expected)
            {
                length = asHex;
                return true;
            }
            return length >= 0 && length <= 64;
        }

        #endregion

        #region BLF底层读取方法（优化版 — 缓存大小/复用缓冲区/手动字段提取）

        // ── 缓存结构体大小（避免每调用一次 Marshal.SizeOf 反射一次） ──
        private static readonly int SizeOfCANMsg = Marshal.SizeOf<VBLCANMessage>();
        private static readonly int SizeOfCANMsg2 = Marshal.SizeOf<VBLCANMessage2>();
        private static readonly int SizeOfCANFDMsg = Marshal.SizeOf<VBLCANFDMessage>();
        private static readonly int SizeOfCANFDMsg64 = Marshal.SizeOf<VBLCANFDMessage64>();

        // 所有 BLF 报文结构体的最大尺寸，用于复用缓冲区
        private static readonly int MaxBlfStructSize = Math.Max(
            Math.Max(SizeOfCANMsg, SizeOfCANMsg2),
            Math.Max(SizeOfCANFDMsg, SizeOfCANFDMsg64)
        );

        // ── 缓存关键字段在非托管内存中的偏移量（计算一次，永久使用） ──
        // VBLObjectHeader（四个报文类型都以此开头）
        private static readonly int OffsetOf_mObjectFlags = (int)Marshal.OffsetOf<VBLObjectHeader>("mObjectFlags");
        private static readonly int OffsetOf_mObjectTimeStamp = (int)Marshal.OffsetOf<VBLObjectHeader>("mObjectTimeStamp");
        // VBLCANMessage / VBLCANMessage2（前 48 字节布局相同，mChannel 均为 UInt16 偏移 32）
        private static readonly int OffsetOfCANMsg_mChannel = (int)Marshal.OffsetOf<VBLCANMessage>("mChannel");
        private static readonly int OffsetOfCANMsg_mID = (int)Marshal.OffsetOf<VBLCANMessage>("mID");
        private static readonly int OffsetOfCANMsg_mDLC = (int)Marshal.OffsetOf<VBLCANMessage>("mDLC");
        private static readonly int OffsetOfCANMsg_mData = (int)Marshal.OffsetOf<VBLCANMessage>("mData");
        // VBLCANFDMessage
        private static readonly int OffsetOfCANFDMsg_mID = (int)Marshal.OffsetOf<VBLCANFDMessage>("mID");
        private static readonly int OffsetOfCANFDMsg_mValidDataBytes = (int)Marshal.OffsetOf<VBLCANFDMessage>("mValidDataBytes");
        private static readonly int OffsetOfCANFDMsg_mData = (int)Marshal.OffsetOf<VBLCANFDMessage>("mData");
        // VBLCANFDMessage64
        private static readonly int OffsetOfCANFDMsg64_mID = (int)Marshal.OffsetOf<VBLCANFDMessage64>("mID");
        private static readonly int OffsetOfCANFDMsg64_mValidDataBytes = (int)Marshal.OffsetOf<VBLCANFDMessage64>("mValidDataBytes");
        private static readonly int OffsetOfCANFDMsg64_mData = (int)Marshal.OffsetOf<VBLCANFDMessage64>("mData");

        // ── 每线程重用同一块非托管缓冲区 ──
        [ThreadStatic]
        private static IntPtr t_blfBuffer;

        private static IntPtr GetBlfBuffer()
        {
            if (t_blfBuffer == IntPtr.Zero)
                t_blfBuffer = Marshal.AllocHGlobal(MaxBlfStructSize);
            return t_blfBuffer;
        }

        /// <summary>
        /// 从已填充的非托管缓冲区中解析时间戳（VBLObjectHeader 始终在偏移 0 处）
        /// </summary>
        private static double ReadTimestampFromBuffer(IntPtr pBuf)
        {
            uint flags = (uint)Marshal.ReadInt32(pBuf, OffsetOf_mObjectFlags);
            ulong time = (ulong)Marshal.ReadInt64(pBuf, OffsetOf_mObjectTimeStamp);
            if ((flags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS) != 0)
                return time / 1000.0;
            if ((flags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_TEN_MICS) != 0)
                return time * 10.0;
            return time / 1000.0;
        }

        private static double ReadCANMessage(IntPtr fileHandle, VBLObjectHeaderBase headerBase,
            ref uint canId, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            IntPtr pBuf = GetBlfBuffer();
            // 将 headerBase（16 字节）写入缓冲区头部，BLReadObjectSecure 将填充剩余字节
            Marshal.StructureToPtr(headerBase, pBuf, false);
            BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)SizeOfCANMsg);

            double timestampUs = ReadTimestampFromBuffer(pBuf);
            canId = (uint)Marshal.ReadInt32(pBuf, OffsetOfCANMsg_mID);
            channel = (byte)Marshal.ReadInt16(pBuf, OffsetOfCANMsg_mChannel);
            dataLen = 8; // VBLCANMessage.mData 固定为 byte[8]，与原始行为一致
            // 直接从缓冲区复制数据，避免 PtrToStructure 产生中间 byte[]
            datas = new byte[dataLen];
            Marshal.Copy(pBuf + OffsetOfCANMsg_mData, datas, 0, (int)dataLen);
            return timestampUs;
        }

        private static double ReadCANMessage2(IntPtr fileHandle, VBLObjectHeaderBase headerBase,
            ref uint canId, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            IntPtr pBuf = GetBlfBuffer();
            Marshal.StructureToPtr(headerBase, pBuf, false);
            BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)SizeOfCANMsg2);

            double timestampUs = ReadTimestampFromBuffer(pBuf);
            canId = (uint)Marshal.ReadInt32(pBuf, OffsetOfCANMsg_mID);
            channel = (byte)Marshal.ReadInt16(pBuf, OffsetOfCANMsg_mChannel);
            uint dlc = Marshal.ReadByte(pBuf, OffsetOfCANMsg_mDLC);
            dataLen = Math.Min(8u, dlc);
            datas = new byte[dataLen];
            if (dataLen > 0)
                Marshal.Copy(pBuf + OffsetOfCANMsg_mData, datas, 0, (int)dataLen);
            return timestampUs;
        }

        private static double ReadCANFDMessage16(IntPtr fileHandle, VBLObjectHeaderBase headerBase,
            ref uint canId, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            IntPtr pBuf = GetBlfBuffer();
            Marshal.StructureToPtr(headerBase, pBuf, false);
            BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)SizeOfCANFDMsg);

            double timestampUs = ReadTimestampFromBuffer(pBuf);
            canId = (uint)Marshal.ReadInt32(pBuf, OffsetOfCANFDMsg_mID);
            channel = (byte)Marshal.ReadInt16(pBuf, OffsetOfCANMsg_mChannel);
            uint validBytes = Marshal.ReadByte(pBuf, OffsetOfCANFDMsg_mValidDataBytes);
            dataLen = Math.Min(16u, validBytes);
            datas = new byte[dataLen];
            if (dataLen > 0)
                Marshal.Copy(pBuf + OffsetOfCANFDMsg_mData, datas, 0, (int)dataLen);
            return timestampUs;
        }

        private static double ReadCANFDMessage64(IntPtr fileHandle, VBLObjectHeaderBase headerBase,
            ref uint canId, ref uint dataLen, ref byte[] datas, ref byte channel)
        {
            IntPtr pBuf = GetBlfBuffer();
            Marshal.StructureToPtr(headerBase, pBuf, false);
            BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)SizeOfCANFDMsg64);

            double timestampUs = ReadTimestampFromBuffer(pBuf);
            canId = (uint)Marshal.ReadInt32(pBuf, OffsetOfCANFDMsg64_mID);
            channel = Marshal.ReadByte(pBuf, 32); // VBLCANFDMessage64.mChannel 为 Byte 偏移 32
            uint validBytes = Marshal.ReadByte(pBuf, OffsetOfCANFDMsg64_mValidDataBytes);
            dataLen = Math.Min(64u, validBytes);
            datas = new byte[dataLen];
            if (dataLen > 0)
                Marshal.Copy(pBuf + OffsetOfCANFDMsg64_mData, datas, 0, (int)dataLen);
            return timestampUs;
        }

        private static void SkipObject(IntPtr fileHandle, VBLObjectHeaderBase headerBase)
        {
            int objSize = (int)headerBase.mObjectSize;
            if (objSize <= MaxBlfStructSize)
            {
                IntPtr pBuf = GetBlfBuffer();
                Marshal.StructureToPtr(headerBase, pBuf, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)objSize);
            }
            else
            {
                // 极少出现的超大对象，临时分配
                IntPtr pLarge = Marshal.AllocHGlobal(objSize);
                try
                {
                    Marshal.StructureToPtr(headerBase, pLarge, false);
                    BLFAPI.BLReadObjectSecure(fileHandle, pLarge, (UIntPtr)objSize);
                }
                finally
                {
                    Marshal.FreeHGlobal(pLarge);
                }
            }
        }

        #endregion
    }
}
