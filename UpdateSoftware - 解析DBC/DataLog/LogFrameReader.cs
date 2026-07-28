using PCAN_Client.CAN_Data.blf;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PCAN_Client.DataLog
{
    internal static class LogFrameReader
    {
        internal readonly struct CanFrame
        {
            public readonly ulong TimeUs;
            public readonly uint CanId;
            public readonly ushort DataLen;
            public readonly byte[] Data;
            public readonly byte Channel;

            public CanFrame(ulong timeUs, uint canId, ushort dataLen, byte[] data, byte channel = 1)
            {
                TimeUs = timeUs;
                CanId = canId;
                DataLen = dataLen;
                Data = data;
                Channel = channel;
            }
        }

        internal static void ReadFrames(string filePath, Action<CanFrame> onFrame, CancellationToken cancellationToken, Action<int> onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (onFrame == null) throw new ArgumentNullException(nameof(onFrame));

            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext == ".bin")
            {
                ReadBinFrames(filePath, onFrame, cancellationToken, onProgress);
                return;
            }
            if (ext == ".asc")
            {
                ReadAscFrames(filePath, onFrame, cancellationToken, onProgress);
                return;
            }
            if (ext == ".blf")
            {
                ReadBlfFrames(filePath, onFrame, cancellationToken, onProgress);
                return;
            }

            throw new NotSupportedException($"不支持的文件类型: {ext}");
        }

        private static void ReadBinFrames(string filePath, Action<CanFrame> onFrame, CancellationToken cancellationToken, Action<int> onProgress)
        {
            const int bufferSize = 512 * 1024;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                if (fs.Length < 8) return;
                reader.ReadBytes(8);

                ulong nowTimeUs = 0;
                int lastProgress = -1;

                while (fs.Position < fs.Length && !cancellationToken.IsCancellationRequested)
                {
                    int lengthByte = reader.Read();
                    if (lengthByte < 0) break;
                    if (lengthByte < 6) break;

                    int dataLength = lengthByte - 6;

                    if (fs.Position + 2 + 3 + dataLength > fs.Length) break;

                    byte canIdHigh = reader.ReadByte();
                    byte canIdLow = reader.ReadByte();
                    // ID高字节的高5位为通道号（旧文件无通道信息读出0→归一化为1），低3位+低字节为CAN ID
                    uint canId = (uint)(((canIdHigh & 0x07) << 8) | canIdLow);
                    byte channel = (byte)(canIdHigh >> 3);
                    if (channel == 0) channel = 1;

                    byte t0 = reader.ReadByte();
                    byte t1 = reader.ReadByte();
                    byte t2 = reader.ReadByte();
                    uint deltaUs = (uint)((t0 << 16) | (t1 << 8) | t2);
                    nowTimeUs += deltaUs;

                    byte[] data = reader.ReadBytes(dataLength);
                    onFrame(new CanFrame(nowTimeUs, canId, (ushort)dataLength, data, channel));

                    if (onProgress != null)
                    {
                        int progress = (int)((fs.Position * 100L) / fs.Length);
                        if (progress != lastProgress)
                        {
                            lastProgress = progress;
                            onProgress(progress);
                        }
                    }
                }
            }
        }

        private static void ReadAscFrames(string filePath, Action<CanFrame> onFrame, CancellationToken cancellationToken, Action<int> onProgress)
        {
            const int bufferSize = 512 * 1024;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true, bufferSize))
            {
                bool headerParsed = false;
                bool isCanFdFormat = false;
                int lastProgress = -1;

                string line;
                while ((line = reader.ReadLine()) != null && !cancellationToken.IsCancellationRequested)
                {
                    if (onProgress != null)
                    {
                        int progress = (int)((fs.Position * 100L) / fs.Length);
                        if (progress != lastProgress)
                        {
                            lastProgress = progress;
                            onProgress(progress);
                        }
                    }

                    if (!headerParsed)
                    {
                        if (line.StartsWith("date", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("base", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("no internal", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }
                        headerParsed = true;
                        if (line.Contains("CANFD"))
                        {
                            isCanFdFormat = true;
                        }
                    }

                    if (line.StartsWith("End TriggerBlock", StringComparison.OrdinalIgnoreCase)) continue;

                    if (isCanFdFormat)
                    {
                        if (TryParseCanFdLine(line, out CanFrame frame))
                        {
                            onFrame(frame);
                        }
                    }
                    else
                    {
                        if (TryParseStandardCanLine(line, out CanFrame frame))
                        {
                            onFrame(frame);
                        }
                    }
                }
            }
        }

        private static bool TryParseStandardCanLine(string line, out CanFrame frame)
        {
            frame = default;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double timestampSeconds))
            {
                return false;
            }

            if (!uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint canId))
            {
                return false;
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
            if (dataLengthIndex < 0 || dataLengthIndex >= parts.Length) return false;

            if (!int.TryParse(parts[dataLengthIndex], out int dataLength) || dataLength < 0 || dataLength > 64) return false;
            if (dataLengthIndex + dataLength >= parts.Length) return false;

            byte[] datas = new byte[dataLength];
            for (int i = 0; i < dataLength; i++)
            {
                if (!byte.TryParse(parts[dataLengthIndex + 1 + i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out datas[i]))
                {
                    return false;
                }
            }

            ulong timeUs = (ulong)(timestampSeconds * 1000000.0);
            frame = new CanFrame(timeUs, canId, (ushort)dataLength, datas);
            return true;
        }

        private static bool TryParseCanFdLine(string line, out CanFrame frame)
        {
            frame = default;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) return false;
            if (parts[1] != "CANFD" && parts[1] != "CAND") return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double timestampSeconds))
            {
                return false;
            }

            if (!uint.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint canId))
            {
                return false;
            }

            int dataLengthIndex = 8;
            if (!int.TryParse(parts[dataLengthIndex], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int dataLength) || dataLength < 0 || dataLength > 64)
            {
                return false;
            }

            int dataStartIndex = dataLengthIndex + 1;
            if (dataStartIndex + dataLength > parts.Length) return false;

            byte[] datas = new byte[dataLength];
            for (int i = 0; i < dataLength; i++)
            {
                if (!byte.TryParse(parts[dataStartIndex + i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out datas[i]))
                {
                    return false;
                }
            }

            ulong timeUs = (ulong)(timestampSeconds * 1000000.0);
            frame = new CanFrame(timeUs, canId, (ushort)dataLength, datas);
            return true;
        }

        private static void ReadBlfFrames(string filePath, Action<CanFrame> onFrame, CancellationToken cancellationToken, Action<int> onProgress)
        {
            IntPtr fileHandle = BLFAPI.BLCreateFileW(filePath, GENERIC.GENERIC_READ);
            if (fileHandle == IntPtr.Zero) throw new InvalidOperationException("打开BLF失败");

            VBLFileStatistics stats;
            stats.mStatisticsSize = 28;
            int statRet = BLFAPI.BLGetFileStatistics(fileHandle, out stats);
            long fileSize = statRet == 1 ? (long)stats.mUncompressedFileSize : 0;

            int retval = 1;
            double startTimeMicroseconds = -1;
            long currentPosition = 0;
            int lastProgress = -1;

            uint canid = 0;
            uint dataLen = 0;
            byte[] datas = new byte[64];

            try
            {
                while (retval == 1 && !cancellationToken.IsCancellationRequested)
                {
                    retval = BLFAPI.BLPeekObject(fileHandle, out VBLObjectHeaderBase headerBase);
                    if (retval != 1) break;

                    double timestampMicroseconds = -1;

                    switch (headerBase.mObjectType)
                    {
                        case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE:
                            timestampMicroseconds = ProcessCANMessage(fileHandle, headerBase, ref canid, ref dataLen, ref datas);
                            break;
                        case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE2:
                            timestampMicroseconds = ProcessCANMessage2(fileHandle, headerBase, ref canid, ref dataLen, ref datas);
                            break;
                        case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE:
                            timestampMicroseconds = ProcessCANFDMessage(fileHandle, headerBase, ref canid, ref dataLen, ref datas);
                            break;
                        case (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64:
                            timestampMicroseconds = ProcessCANFDMessage64(fileHandle, headerBase, ref canid, ref dataLen, ref datas);
                            break;
                        default:
                            SkipObject(fileHandle, headerBase);
                            break;
                    }

                    currentPosition += headerBase.mObjectSize;
                    if (onProgress != null && fileSize > 0)
                    {
                        int progress = (int)((currentPosition * 100L) / fileSize);
                        if (progress != lastProgress)
                        {
                            lastProgress = progress;
                            onProgress(progress);
                        }
                    }

                    if (timestampMicroseconds < 0) continue;

                    if (startTimeMicroseconds < 0)
                    {
                        startTimeMicroseconds = timestampMicroseconds;
                    }

                    double relativeMicroseconds = timestampMicroseconds - startTimeMicroseconds;
                    if (relativeMicroseconds < 0) relativeMicroseconds = 0;
                    ulong timeUs = (ulong)relativeMicroseconds;

                    byte[] payload = new byte[dataLen];
                    Array.Copy(datas, payload, (int)dataLen);
                    onFrame(new CanFrame(timeUs, canid, (ushort)dataLen, payload));
                }
            }
            finally
            {
                BLFAPI.BLCloseHandle(fileHandle);
            }
        }

        private static double ProcessCANMessage(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas)
        {
            int structSize = Marshal.SizeOf<VBLCANMessage>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANMessage msg = new VBLCANMessage();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANMessage retMsg = Marshal.PtrToStructure<VBLCANMessage>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                dataLen = (uint)Math.Min(retMsg.mData.Length, 8);
                if (datas.Length < dataLen) Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private static double ProcessCANMessage2(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas)
        {
            int structSize = Marshal.SizeOf<VBLCANMessage2>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANMessage2 msg = new VBLCANMessage2();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANMessage2 retMsg = Marshal.PtrToStructure<VBLCANMessage2>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                dataLen = (uint)Math.Min((int)retMsg.mDLC, 8);
                if (datas.Length < dataLen) Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private static double ProcessCANFDMessage(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas)
        {
            int structSize = Marshal.SizeOf<VBLCANFDMessage>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANFDMessage msg = new VBLCANFDMessage();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANFDMessage retMsg = Marshal.PtrToStructure<VBLCANFDMessage>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                dataLen = retMsg.mValidDataBytes;
                if (dataLen > 16) dataLen = 16;
                if (datas.Length < dataLen) Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private static double ProcessCANFDMessage64(IntPtr fileHandle, VBLObjectHeaderBase headerBase, ref uint canid, ref uint dataLen, ref byte[] datas)
        {
            int structSize = Marshal.SizeOf<VBLCANFDMessage64>();
            IntPtr pMsg = Marshal.AllocHGlobal(structSize);
            try
            {
                VBLCANFDMessage64 msg = new VBLCANFDMessage64();
                msg.mHeader.mBase = headerBase;
                Marshal.StructureToPtr(msg, pMsg, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                VBLCANFDMessage64 retMsg = Marshal.PtrToStructure<VBLCANFDMessage64>(pMsg);

                double timestampMicroseconds = ConvertTimestampToMicroseconds(retMsg.mHeader);

                canid = retMsg.mID;
                dataLen = retMsg.mValidDataBytes;
                if (dataLen > 64) dataLen = 64;
                if (datas.Length < dataLen) Array.Resize(ref datas, (int)dataLen);
                Array.Copy(retMsg.mData, 0, datas, 0, (int)dataLen);

                return timestampMicroseconds;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsg);
            }
        }

        private static void SkipObject(IntPtr fileHandle, VBLObjectHeaderBase headerBase)
        {
            IntPtr pBuffer = Marshal.AllocHGlobal((int)headerBase.mObjectSize);
            try
            {
                Marshal.StructureToPtr(headerBase, pBuffer, false);
                BLFAPI.BLReadObjectSecure(fileHandle, pBuffer, (UIntPtr)headerBase.mObjectSize);
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }

        private static double ConvertTimestampToMicroseconds(VBLObjectHeader header)
        {
            if ((header.mObjectFlags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS) != 0)
            {
                return header.mObjectTimeStamp / 1000.0;
            }
            if ((header.mObjectFlags & (uint)ObjectFlag.BL_OBJ_FLAG_TIME_TEN_MICS) != 0)
            {
                return header.mObjectTimeStamp * 10.0;
            }
            return header.mObjectTimeStamp / 1000.0;
        }
    }
}
