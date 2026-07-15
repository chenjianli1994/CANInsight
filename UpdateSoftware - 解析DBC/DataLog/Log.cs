using PCAN_Client.CAN_API;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCAN_Client.DataLog
{
    internal class Log
    {
        const ulong GENERIC_READ = 0x80000000L;
        const ulong GENERIC_WRITE = 0x40000000L;
        const ulong GENERIC_EXECUT = 0x20000000L;
        const ulong GENERIC_ALL = 0x10000000L;
        const uint BL_OBJ_FLAG_TIME_TEN_MICS = 0x00000001;
        const uint BL_OBJ_FLAG_TIME_ONE_NANS = 0x00000002;
        const uint BL_OBJ_SIGNATURE = 0x4A424F4C;
        const uint BL_OBJ_TYPE_UNKNOWN = 0;
        const uint BL_OBJ_TYPE_CAN_MESSAGE = 1;
        const uint BL_OBJ_TYPE_CAN_MESSAGE2 = 2;
        const uint BL_OBJ_TYPE_CAN_FD_MESSAGE = 100;
        const uint BL_OBJ_TYPE_CAN_FD_MESSAGE_64 = 101;
        const uint BL_OBJ_TYPE_CAN_ERROR_EXT = 5;
        const uint BL_OBJ_TYPE_ETHERNET_FRAME_EX = 120;
        const uint BL_OBJ_TYPE_ETHERNET_FRAME_FORWARDED = 121;
        const uint BL_OBJ_TYPE_ETHERNET_ERROR_EX = 122;
        const uint BL_OBJ_TYPE_ETHERNET_ERROR_FORWARDED = 123;

        string[] Mon = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec" };
        string[] Week = { "Mon", "Tue", "Wed", "Thur", "Fri", "Sat", "Sun" };

        private static ulong lastTimeUs = 0;
        private static Stopwatch stopwatch = new Stopwatch();
        // CAN数据队列（ConcurrentQueue = 无锁，不阻塞CAN接收线程）
        private static ConcurrentQueue<CanMessageData> _canMessageQueue = new ConcurrentQueue<CanMessageData>();

        public struct CanMessageData
        {
            public uint CanId;
            public byte[] Data;
            public ulong Timestamp;
            public byte Channel;
            public byte Flags;
        }

        public static void SetTimeUs(ulong us)
        {
            lastTimeUs = 0;
            stopwatch.Restart();
            _canMessageQueue = new ConcurrentQueue<CanMessageData>();
        }

        /// <summary>
        /// 添加CAN消息到写入队列（拷贝数据，避免外部缓冲区复用）
        /// </summary>
        public static void AddCanMessageToWrite(uint canId, byte[] data, ulong timestamp, byte channel = 1, byte flags = 0)
        {
            if (0 == lastTimeUs)
                lastTimeUs = timestamp;

            byte[] dataCopy = new byte[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            _canMessageQueue.Enqueue(new CanMessageData
            {
                CanId = canId,
                Data = dataCopy,
                Timestamp = timestamp,
                Channel = channel,
                Flags = flags
            });
        }

        private static void WriteBufferToFileDirect(ref List<byte> buffer, string filePath)
        {
            if (buffer == null || buffer.Count == 0 || string.IsNullOrEmpty(filePath))
                return;

            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    fileStream.Seek(0, SeekOrigin.End);
                    fileStream.Write(buffer.ToArray(), 0, buffer.Count);
                    fileStream.Flush(true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"直接文件写入失败: {ex.Message}");
            }
        }

        /// <summary>立即处理队列中的消息（无Sleep，不阻塞CAN接收线程）</summary>
        public static void FlushCanWriteQueue()
        {
            if (_canMessageQueue.IsEmpty) return;
            if (Logging.NowBLFFileHandle == IntPtr.Zero && string.IsNullOrEmpty(Logging.NowBLFFileAddr_str))
                return;

            int dequeued = 0;
            const int MAX_BATCH = 500;
            while (dequeued < MAX_BATCH && _canMessageQueue.TryDequeue(out CanMessageData msg))
            {
                dequeued++;
                if (Logging.NowBLFFileHandle != IntPtr.Zero)
                {
                    int structSize = Marshal.SizeOf<CAN_Data.blf.VBLCANMessage>();
                    IntPtr msgPtr = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        var canMessage = new CAN_Data.blf.VBLCANMessage();
                        canMessage.mHeader.mBase.mSignature = CAN_Data.blf.BLFAPI.BL_OBJ_SIGNATURE;
                        canMessage.mHeader.mBase.mHeaderSize = (UInt16)Marshal.SizeOf(typeof(CAN_Data.blf.VBLObjectHeader));
                        canMessage.mHeader.mBase.mHeaderVersion = 1;
                        canMessage.mHeader.mObjectTimeStamp = (ulong)(msg.Timestamp * 1000);
                        canMessage.mHeader.mBase.mObjectSize = (UInt16)structSize;
                        canMessage.mHeader.mBase.mObjectType = (uint)CAN_Data.blf.BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE;
                        canMessage.mHeader.mObjectFlags = (uint)CAN_Data.blf.ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS;
                        canMessage.mChannel = msg.Channel;
                        canMessage.mFlags = msg.Flags;
                        canMessage.mDLC = (byte)Math.Min(msg.Data.Length, 8);
                        canMessage.mID = msg.CanId;
                        canMessage.mData = new byte[8];
                        Array.Copy(msg.Data, canMessage.mData, Math.Min(msg.Data.Length, 8));
                        Marshal.StructureToPtr(canMessage, msgPtr, false);
                        CAN_Data.blf.BLFAPI.BLWriteObject(Logging.NowBLFFileHandle, msgPtr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(msgPtr);
                    }
                }
                else
                {
                    byte[] data8 = new byte[8];
                    int copyLen = Math.Min(msg.Data.Length, 8);
                    Array.Copy(msg.Data, data8, copyLen);
                    uint delta = (uint)(msg.Timestamp - lastTimeUs);
                    lastTimeUs = (uint)msg.Timestamp;
                    var binWriter = new List<byte>();
                    binWriter.Add((byte)(copyLen + 6));
                    binWriter.Add((byte)(msg.CanId >> 8));
                    binWriter.Add((byte)(msg.CanId & 0xFF));
                    binWriter.Add((byte)(delta >> 16));
                    binWriter.Add((byte)(delta >> 8));
                    binWriter.Add((byte)(delta & 0xFF));
                    for (int i = 0; i < copyLen; i++)
                        binWriter.Add(data8[i]);
                    WriteBufferToFileDirect(ref binWriter, Logging.NowBLFFileAddr_str);
                }
            }
        }

        public static void ContinuousWriteWorker()
        {
            try
            {
                FlushCanWriteQueue();
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入线程错误: {ex.Message}");
                Thread.Sleep(100);
            }
        }

        public static void saveLog(string str, string path)
        {
            try
            {
                StreamWriter sw;
                FileStream fileStream = new FileStream(path, FileMode.Append);
                sw = new StreamWriter(fileStream);
                sw.Write(str);
                sw.Close();
                fileStream?.Close();
            }
            catch
            {
            }
        }
    }
}
