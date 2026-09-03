using PCAN_Client.CAN_Data.blf;
using PCAN_Client.CAN_API;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCAN_Client.CAN_Data
{
    /// <summary>
    /// BLF/ASC 录制器：文件创建、写队列、连续写线程、文件分割。
    /// 自 GUI 的 Log/Logging 收归 Core，GUI 与 Service 共用；宿主通过事件接收新文件通知。
    /// </summary>
    public static class Recorder
    {
        public struct CanMessageData
        {
            public uint CanId;
            public byte[] Data;
            public ulong Timestamp;
            public byte Channel;
            public byte Flags;
        }

        // 文件状态（RecordingState 同步供 GUI 代理显示）
        public static IntPtr NowBlfHandle = IntPtr.Zero;
        public static string NowBlfPath = "";

        // 写队列（ConcurrentQueue = 无锁，不阻塞 CAN 接收线程）
        private static ulong lastTimeUs = 0;
        private static Stopwatch stopwatch = new Stopwatch();
        private static ConcurrentQueue<CanMessageData> _canMessageQueue = new ConcurrentQueue<CanMessageData>();

        // 连续写线程
        private static Thread _writerThread;
        private static volatile bool _writerRunning;
        private static string _writeDir = "";
        private static long _maxFileBytes = 128L * 1024 * 1024;

        /// <summary>BLF 新文件打开事件（GUI 订阅更新显示路径）</summary>
        public static event Action<string> BlfFileOpened;

        public static void SetTimeUs(ulong us)
        {
            lastTimeUs = 0;
            stopwatch.Restart();
            _canMessageQueue = new ConcurrentQueue<CanMessageData>();
        }

        /// <summary>添加 CAN 消息到写入队列（拷贝数据，避免外部缓冲区复用）</summary>
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

        /// <summary>开始 BLF 录制：创建文件 + 启动连续写线程。返回当前文件路径（失败返回空串）</summary>
        public static string StartBlf(string directory, long maxBytes = 0)
        {
            StopBlf();
            _writeDir = string.IsNullOrEmpty(directory) ? AppDomain.CurrentDomain.BaseDirectory : directory;
            _maxFileBytes = maxBytes > 0 ? maxBytes : 128L * 1024 * 1024;
            SetTimeUs(0);
            CreateBlfFile();
            if (NowBlfHandle == IntPtr.Zero) return "";
            _writerRunning = true;
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "BLFWriter" };
            _writerThread.Start();
            return NowBlfPath;
        }

        /// <summary>停止 BLF 录制：刷队列、关句柄、停线程</summary>
        public static void StopBlf()
        {
            _writerRunning = false;
            if (_writerThread != null && _writerThread.IsAlive)
            {
                try { _writerThread.Join(2000); } catch { }
                _writerThread = null;
            }
            if (NowBlfHandle != IntPtr.Zero)
            {
                try { FlushCanWriteQueue(); } catch { }
                try { BLFAPI.BLCloseHandle(NowBlfHandle); } catch { }
                NowBlfHandle = IntPtr.Zero;
            }
        }

        private static void CreateBlfFile()
        {
            // 关闭旧句柄（文件分割时创建新文件）
            if (NowBlfHandle != IntPtr.Zero)
            {
                try { FlushCanWriteQueue(); } catch { }
                try { BLFAPI.BLCloseHandle(NowBlfHandle); } catch { }
                NowBlfHandle = IntPtr.Zero;
            }
            NowBlfPath = Path.Combine(_writeDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".blf");
            IntPtr blfHandle = BLFAPI.BLCreateFileW(NowBlfPath, GENERIC.GENERIC_WRITE);
            if (blfHandle == IntPtr.Zero)
            {
                Debug.WriteLine($"[Recorder] 创建BLF文件失败: {NowBlfPath}");
                NowBlfPath = "";
                return;
            }
            BLFAPI.BLSetApplication(blfHandle, BLAppID.BL_APPID_CANALYZER, 3, 0, 1);
            SYSTEMTIME systemTime = new SYSTEMTIME();
            DateTime now = DateTime.Now;
            systemTime.wYear = (UInt16)now.Year;
            systemTime.wMonth = (UInt16)now.Month;
            systemTime.wDay = (UInt16)now.Day;
            systemTime.wHour = (UInt16)now.Hour;
            systemTime.wMinute = (UInt16)now.Minute;
            systemTime.wSecond = (UInt16)now.Second;
            IntPtr timePtr = Marshal.AllocHGlobal(Marshal.SizeOf<SYSTEMTIME>());
            try
            {
                Marshal.StructureToPtr(systemTime, timePtr, false);
                BLFAPI.BLSetMeasurementStartTime(blfHandle, timePtr);
            }
            finally { Marshal.FreeHGlobal(timePtr); }
            BLFAPI.BLSetWriteOptions(blfHandle, 0, 0); // 减少内部缓存，确保及时写入
            NowBlfHandle = blfHandle;
            RecordingState.NowBlfFileAddrStr = NowBlfPath; // 同步 GUI 代理显示
            try { BlfFileOpened?.Invoke(NowBlfPath); } catch { }
            Debug.WriteLine($"[Recorder] BLF文件创建成功: {NowBlfPath}, handle={NowBlfHandle}");
        }

        private static void WriterLoop()
        {
            while (_writerRunning)
            {
                try
                {
                    FlushCanWriteQueue();
                    // 文件分割检查
                    if (NowBlfHandle != IntPtr.Zero && _maxFileBytes > 0)
                    {
                        try
                        {
                            var fi = new FileInfo(NowBlfPath);
                            if (fi.Exists && fi.Length > _maxFileBytes)
                                CreateBlfFile();
                        }
                        catch { }
                    }
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Recorder] 写入线程错误: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
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
                Debug.WriteLine($"直接文件写入失败: {ex.Message}");
            }
        }

        /// <summary>立即处理队列中的消息（无 Sleep，不阻塞 CAN 接收线程）</summary>
        public static void FlushCanWriteQueue()
        {
            if (_canMessageQueue.IsEmpty) return;
            if (NowBlfHandle == IntPtr.Zero && string.IsNullOrEmpty(NowBlfPath))
                return;

            int dequeued = 0;
            const int MAX_BATCH = 500;
            while (dequeued < MAX_BATCH && _canMessageQueue.TryDequeue(out CanMessageData msg))
            {
                dequeued++;
                if (NowBlfHandle != IntPtr.Zero)
                {
                    int structSize = Marshal.SizeOf<VBLCANMessage>();
                    IntPtr msgPtr = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        var canMessage = new VBLCANMessage();
                        canMessage.mHeader.mBase.mSignature = BLFAPI.BL_OBJ_SIGNATURE;
                        canMessage.mHeader.mBase.mHeaderSize = (UInt16)Marshal.SizeOf(typeof(VBLObjectHeader));
                        canMessage.mHeader.mBase.mHeaderVersion = 1;
                        canMessage.mHeader.mObjectTimeStamp = (ulong)(msg.Timestamp * 1000);
                        canMessage.mHeader.mBase.mObjectSize = (UInt16)structSize;
                        canMessage.mHeader.mBase.mObjectType = (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE;
                        canMessage.mHeader.mObjectFlags = (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS;
                        canMessage.mChannel = msg.Channel;
                        canMessage.mFlags = msg.Flags;
                        canMessage.mDLC = (byte)Math.Min(msg.Data.Length, 8);
                        canMessage.mID = msg.CanId;
                        canMessage.mData = new byte[8];
                        Array.Copy(msg.Data, canMessage.mData, Math.Min(msg.Data.Length, 8));
                        Marshal.StructureToPtr(canMessage, msgPtr, false);
                        BLFAPI.BLWriteObject(NowBlfHandle, msgPtr);
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
                    // ID高字节的高5位携带通道号（ID≤0x7FF只用低3位）：旧文件无通道信息时读出0→归一化为通道1，向后兼容
                    binWriter.Add((byte)(((uint)msg.Channel << 3) | ((msg.CanId >> 8) & 0x07)));
                    binWriter.Add((byte)(msg.CanId & 0xFF));
                    binWriter.Add((byte)(delta >> 16));
                    binWriter.Add((byte)(delta >> 8));
                    binWriter.Add((byte)(delta & 0xFF));
                    for (int i = 0; i < copyLen; i++)
                        binWriter.Add(data8[i]);
                    WriteBufferToFileDirect(ref binWriter, NowBlfPath);
                }
            }
        }

        /// <summary>ASC 文本追加写（供 ASC 录制与 CSV 之外的文本输出）</summary>
        public static void saveLog(string str, string path)
        {
            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Append))
                using (StreamWriter sw = new StreamWriter(fileStream))
                {
                    sw.Write(str);
                }
            }
            catch
            {
            }
        }
    }
}
