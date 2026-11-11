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

        // ===== ASC 行解析快路径（H7：回放提速）=====
        // 实测：475MB/322 万帧的 ASC 纯解析要 21.9s，占"最快"回放总耗时的约 85%（BLF 同数据仅 2s）。
        // 原实现对每行 string.Split（每行分配 string[] + N 个子串，总计约 4000 万个小字符串），
        // 且每个数据字节都走 byte.TryParse(HexNumber, InvariantCulture)（带样式/文化开销）。
        // 这里改为：按索引切 token（零分配）+ 手写数字/十六进制解析；
        // 遇到非常规写法（指数、超长数字、奇怪前缀等）**立即回退到框架解析**，保证与改造前逐字段一致。
        // 对拍工具：.build-check/team/probes/AscEqProbe（旧实现副本 vs 生产实现，逐帧逐字段比对）。
        [ThreadStatic] private static int[] t_ascTokStart;
        [ThreadStatic] private static int[] t_ascTokLen;
        private const int AscInitTokenCapacity = 128;

        private static readonly double[] AscPow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22
        };

        /// <summary>按单个空格切 token（与 Split(' ', RemoveEmptyEntries) 等价），结果写线程级缓冲，返回 token 数</summary>
        private static int TokenizeAscLine(string line)
        {
            int[] starts = t_ascTokStart;
            int[] lens = t_ascTokLen;
            if (starts == null || starts.Length < AscInitTokenCapacity)
            {
                starts = new int[AscInitTokenCapacity];
                lens = new int[AscInitTokenCapacity];
                t_ascTokStart = starts;
                t_ascTokLen = lens;
            }

            int count = 0, i = 0, n = line.Length;
            while (i < n)
            {
                while (i < n && line[i] == ' ') i++;
                if (i >= n) break;
                int s = i;
                while (i < n && line[i] != ' ') i++;
                if (count == starts.Length)
                {
                    Array.Resize(ref starts, count * 2);
                    Array.Resize(ref lens, count * 2);
                    t_ascTokStart = starts;
                    t_ascTokLen = lens;
                }
                starts[count] = s;
                lens[count] = i - s;
                count++;
            }
            return count;
        }

        /// <summary>token 与字面量序数比较（区分大小写，等价 string ==）</summary>
        private static bool TokEq(string line, int idx, string lit)
        {
            int len = t_ascTokLen[idx];
            if (len != lit.Length) return false;
            int s = t_ascTokStart[idx];
            for (int k = 0; k < len; k++)
                if (line[s + k] != lit[k]) return false;
            return true;
        }

        /// <summary>token 与字面量序数比较（忽略大小写，等价 string.Equals(OrdinalIgnoreCase)）</summary>
        private static bool TokEqIgnoreCase(string line, int idx, string lit)
        {
            int len = t_ascTokLen[idx];
            if (len != lit.Length) return false;
            int s = t_ascTokStart[idx];
            for (int k = 0; k < len; k++)
            {
                char a = line[s + k];
                char b = lit[k];
                if (a == b) continue;
                if (char.ToUpperInvariant(a) != char.ToUpperInvariant(b)) return false;
            }
            return true;
        }

        /// <summary>token 是否以给定前缀开头（忽略大小写，等价 StartsWith(OrdinalIgnoreCase)）</summary>
        private static bool TokStartsWithIgnoreCase(string line, int idx, string prefix)
        {
            if (t_ascTokLen[idx] < prefix.Length) return false;
            int s = t_ascTokStart[idx];
            for (int k = 0; k < prefix.Length; k++)
                if (char.ToUpperInvariant(line[s + k]) != char.ToUpperInvariant(prefix[k])) return false;
            return true;
        }

        /// <summary>ASC 的 brs/esi 只可能是 "0" 或 "1"</summary>
        private static bool AscTokIsBit(string line, int idx)
        {
            if (t_ascTokLen[idx] != 1) return false;
            char c = line[t_ascTokStart[idx]];
            return c == '0' || c == '1';
        }

        /// <summary>十进制整数（带符号），语义等价 int.TryParse(s, NumberStyles.Integer, InvariantCulture)</summary>
        private static bool AscParseInt(string line, int idx, out int value)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            int i = s, end = s + len;
            bool neg = false;
            if (i < end && (line[i] == '-' || line[i] == '+')) { neg = line[i] == '-'; i++; }
            long acc = 0;
            int digits = 0;
            bool overflow = false;
            while (i < end && line[i] >= '0' && line[i] <= '9')
            {
                acc = acc * 10 + (line[i] - '0');
                if (acc > 2000000000L) { overflow = true; break; }
                i++;
                digits++;
            }
            if (!overflow && digits > 0 && i == end)
            {
                value = (int)(neg ? -acc : acc);
                return true;
            }
            return int.TryParse(line.Substring(s, len),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>十进制整数（无符号位、默认文化），语义等价 int.TryParse(s, out v)（用于 d/r 后的长度探测）</summary>
        private static bool AscParseIntDefaultCulture(string line, int idx, out int value)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            int i = s, end = s + len;
            long acc = 0;
            int digits = 0;
            bool overflow = false;
            while (i < end && line[i] >= '0' && line[i] <= '9')
            {
                acc = acc * 10 + (line[i] - '0');
                if (acc > 2000000000L) { overflow = true; break; }
                i++;
                digits++;
            }
            if (!overflow && digits > 0 && i == end)
            {
                value = (int)acc;
                return true;
            }
            return int.TryParse(line.Substring(s, len), out value);
        }

        /// <summary>十进制字节（0~255，Invariant），语义等价 byte.TryParse(s, Integer, InvariantCulture)</summary>
        private static bool AscParseByte(string line, int idx, out byte value)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            int i = s, end = s + len;
            int acc = 0;
            int digits = 0;
            while (i < end && line[i] >= '0' && line[i] <= '9')
            {
                acc = acc * 10 + (line[i] - '0');
                if (acc > 255) break;
                i++;
                digits++;
            }
            if (digits > 0 && i == end && acc <= 255)
            {
                value = (byte)acc;
                return true;
            }
            return byte.TryParse(line.Substring(s, len),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>从 token 的第 skip 个字符起按十进制解析成字节（用于 "CAN7" 这类写法）</summary>
        private static bool AscParseByteFrom(string line, int idx, int skip, out byte value)
        {
            int s = t_ascTokStart[idx] + skip, len = t_ascTokLen[idx] - skip;
            value = 0;
            if (len <= 0) return false;
            int end = s + len;
            int i = s, acc = 0, digits = 0;
            while (i < end && line[i] >= '0' && line[i] <= '9')
            {
                acc = acc * 10 + (line[i] - '0');
                if (acc > 255) break;
                i++;
                digits++;
            }
            if (digits > 0 && i == end && acc <= 255) { value = (byte)acc; return true; }
            return byte.TryParse(line.Substring(s, len),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>十六进制无符号整数（1~8 位，无前缀/无符号位）</summary>
        private static bool AscParseHexUIntAt(string line, int start, int hexLen, out uint value)
        {
            value = 0;
            if (hexLen <= 0 || hexLen > 8) return false;
            ulong acc = 0;
            for (int k = 0; k < hexLen; k++)
            {
                char c = line[start + k];
                int d;
                if (c >= '0' && c <= '9') d = c - '0';
                else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                else return false;
                acc = (acc << 4) | (uint)d;
            }
            if (acc > uint.MaxValue) return false;
            value = (uint)acc;
            return true;
        }

        /// <summary>十六进制字节（1~2 位），语义等价 byte.TryParse(s, HexNumber, InvariantCulture)</summary>
        private static bool AscParseHexByte(string line, int idx, out byte value)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            value = 0;
            if (len <= 0 || len > 2)
            {
                // 多于 2 位（例如带前导零的 "00F"）交给框架判定，保持与改造前一致
                return byte.TryParse(line.Substring(s, len),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value);
            }
            int acc = 0;
            for (int k = 0; k < len; k++)
            {
                char c = line[s + k];
                int d;
                if (c >= '0' && c <= '9') d = c - '0';
                else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                else
                {
                    return byte.TryParse(line.Substring(s, len),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value);
                }
                acc = (acc << 4) | d;
            }
            value = (byte)acc;
            return true;
        }

        /// <summary>十进制定点数（ASC 时间戳形如 123.456789）。
        /// 用整数尾数 + 精确 10 的幂做一次除法，得到与 double.TryParse 相同的正确舍入结果；
        /// 指数写法或超长有效数字回退框架解析</summary>
        private static bool AscParseDouble(string line, int idx, out double value)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            int i = s, end = s + len;
            bool neg = false;
            if (i < end && (line[i] == '-' || line[i] == '+')) { neg = line[i] == '-'; i++; }

            long mant = 0;
            int digits = 0, fracDigits = 0;
            bool dot = false;
            while (i < end)
            {
                char c = line[i];
                if (c >= '0' && c <= '9')
                {
                    // 有效数字超过 15 位就不再走快路径，避免尾数溢出导舍入差异
                    if (mant > 900000000000000L) return AscParseDoubleFallback(line, s, len, out value);
                    mant = mant * 10 + (c - '0');
                    digits++;
                    if (dot) fracDigits++;
                    i++;
                }
                else if (c == '.' && !dot)
                {
                    dot = true;
                    i++;
                }
                else
                {
                    return AscParseDoubleFallback(line, s, len, out value);   // 指数/异常字符
                }
            }

            if (digits == 0 || fracDigits > 22) return AscParseDoubleFallback(line, s, len, out value);
            double v = mant;
            if (fracDigits > 0) v = v / AscPow10[fracDigits];
            value = neg ? -v : v;
            return true;
        }

        private static bool AscParseDoubleFallback(string line, int start, int len, out double value)
        {
            return double.TryParse(line.Substring(start, len),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>ASC 的 CAN ID（十六进制，允许带 "x"/"X" 扩展帧后缀）</summary>
        private static bool AscParseCanId(string line, int idx, out uint canId)
        {
            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            canId = 0;
            if (len <= 0) return false;
            char last = line[s + len - 1];
            int hexLen = (last == 'x' || last == 'X') ? len - 1 : len;
            if (AscParseHexUIntAt(line, s, hexLen, out canId)) return true;
            // 回退：与原实现完全一致（先去 x 后缀再按 HexNumber 解析）
            return TryParseAscCanId(line.Substring(s, len), out canId);
        }

        /// <summary>取 FD 行的数据字节数：优先选与 DLC 对应长度一致的解释（十进制/十六进制都试），
        /// 都不一致时退回十进制 0~64（兼容个别工具把长度写成十六进制）</summary>
        private static bool AscParseDataLength(string line, int idx, int expected, out int length)
        {
            if (!AscParseInt(line, idx, out length)) return false;
            if (length == expected) return true;

            int s = t_ascTokStart[idx], len = t_ascTokLen[idx];
            if (len > 0 && len <= 8 && AscParseHexUIntAt(line, s, len, out uint hexVal) && hexVal <= int.MaxValue)
            {
                int asHex = (int)hexVal;
                if (asHex == expected)
                {
                    length = asHex;
                    return true;
                }
            }
            return length >= 0 && length <= 64;
        }

        /// <summary>解析一行 ASC（经典格式与 CAN FD 扩展格式都支持）；数据转换的 ASC→CSV 也复用这里</summary>
        internal static CanRawMessageRead ParseAscLine(string line, long filePosition)
        {
            try
            {
                int count = TokenizeAscLine(line);
                if (count < 7) return null;

                if (!AscParseDouble(line, 0, out double timestampSeconds)) return null;

                // CAN FD 行必须单独解析：Vector 的 FD 格式把通道放在 token1、ID 放在 token4，
                // 而且没有经典格式里的 "d"/"r" 标记（按经典规则解析会取错 ID，并因找不到 "d" 把整行丢掉）
                if (TokEqIgnoreCase(line, 1, "CANFD"))
                    return ParseAscFdLineTokens(line, count, timestampSeconds, filePosition);

                if (!AscParseCanId(line, 2, out uint canId))
                    return null;

                // 解析通道号（token1）：Vector ASC 为 1-based 数字，部分工具输出 "CAN1" 前缀；
                // 无法解析时默认 1（单通道文件）。此前该列被忽略导致报文 Channel 恒为 0，
                // 通道配置（BlfChannelId）匹配不上，绘图区无法解码绘制
                byte channel = 1;
                if (count >= 3 && t_ascTokLen[1] > 0)
                {
                    if (!AscParseByte(line, 1, out channel))
                    {
                        channel = 1;
                        if (TokStartsWithIgnoreCase(line, 1, "CAN"))
                        {
                            // 与原实现一致：byte.TryParse 失败时 out 会被置 0
                            if (!AscParseByteFrom(line, 1, 3, out channel)) channel = 0;
                        }
                    }
                }

                int dataLengthIndex = -1;
                for (int i = 3; i < count - 1; i++)
                {
                    if ((TokEq(line, i, "d") || TokEq(line, i, "r")) &&
                        i + 1 < count &&
                        AscParseIntDefaultCulture(line, i + 1, out _))
                    {
                        dataLengthIndex = i + 1;
                        break;
                    }
                }

                if (dataLengthIndex < 0 || dataLengthIndex >= count) return null;
                if (!AscParseInt(line, dataLengthIndex, out int dataLength) || dataLength < 0 || dataLength > 64)
                    return null;
                if (dataLengthIndex + dataLength >= count) return null;

                byte[] datas = new byte[dataLength];
                for (int i = 0; i < dataLength; i++)
                {
                    if (!AscParseHexByte(line, dataLengthIndex + 1 + i, out datas[i]))
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
        private static CanRawMessageRead ParseAscFdLineTokens(string line, int count, double timestampSeconds, long filePosition)
        {
            if (count < 10) return null;

            if (!AscParseCanId(line, 4, out uint canId))
                return null;

            // 通道号：token2（Vector 为 1-based 数字；解析不了时按单通道 1 处理，与经典格式保持一致）
            if (!AscParseByte(line, 2, out byte channel))
                channel = 1;

            // ID 之后可能还有符号名：向后找 <brs> <esi> <dlc> <len> 四连，靠 DLC↔长度一致性排除误判
            for (int i = 5; i + 3 < count; i++)
            {
                if (!AscTokIsBit(line, i) || !AscTokIsBit(line, i + 1)) continue;
                if (!AscParseHexByte(line, i + 2, out byte dlc) || dlc > 15) continue;

                int expected = BLFAPI.GetFdDataLength(dlc);
                if (!AscParseDataLength(line, i + 3, expected, out int dataLength)) continue;
                if (dataLength < 0 || dataLength > 64) continue;

                int dataStart = i + 4;
                if (dataStart + dataLength > count) continue;

                byte[] datas = new byte[dataLength];
                bool ok = true;
                for (int k = 0; k < dataLength; k++)
                {
                    if (!AscParseHexByte(line, dataStart + k, out datas[k]))
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

        /// <summary>解析 ASC 的 CAN ID（十六进制）；部分工具给扩展帧加 "x"/"X" 后缀，这里一并容忍（快路径的回退实现）</summary>
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
            // 类型 100 是"变长 CAN FD 对象"：对象长度 = 52 + 数据字节数（4 字节对齐），
            // 数据最大可到 64 字节。原实现固定按结构体 72 字节读、载荷截到 16 字节，
            // 遇到变长 FD 文件（包括本程序转换生成的文件）会丢数据，这里按对象自身长度读。
            int objSize = (int)headerBase.mObjectSize;
            if (objSize < SizeOfCANFDMsg) objSize = SizeOfCANFDMsg;   // 至少覆盖结构体，字段偏移才有效
            if (objSize > MaxBlfStructSize) objSize = MaxBlfStructSize;

            IntPtr pBuf = GetBlfBuffer();
            Marshal.StructureToPtr(headerBase, pBuf, false);
            BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)objSize);

            double timestampUs = ReadTimestampFromBuffer(pBuf);
            canId = (uint)Marshal.ReadInt32(pBuf, OffsetOfCANFDMsg_mID);
            channel = (byte)Marshal.ReadInt16(pBuf, OffsetOfCANMsg_mChannel);
            uint validBytes = Marshal.ReadByte(pBuf, OffsetOfCANFDMsg_mValidDataBytes);
            // 双重钳位：既不超过 64 字节，也不超过对象实际携带的字节数
            int maxByObject = (int)headerBase.mObjectSize - OffsetOfCANFDMsg_mData;
            if (maxByObject < 0) maxByObject = 0;
            dataLen = Math.Min(Math.Min(64u, validBytes), (uint)maxByObject);
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
