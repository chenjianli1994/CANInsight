using PCAN_Client.CAN_Data.blf;
using PCAN_Client.CAN_API;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PCAN_Client.CAN_Data
{
    /// <summary>
    /// BLF/ASC 录制器：文件创建、写队列、连续写线程、文件分割。
    /// 自 GUI 的 Log/Logging 收归 Core；宿主通过事件接收新文件通知。
    ///
    /// 线程模型（重要）：binlog.dll 的句柄不是线程安全的——同一句柄上的 BLWriteObject /
    /// BLCloseHandle / BLCreateFileW 被两个线程同时调用会破坏堆，表现为"录制中一切正常、
    /// 点停止录制时偶发闪退"（已用探针复现：0xC0000374 堆损坏）。因此所有触碰句柄的操作
    /// 一律在 BLFAPI.SyncRoot 内完成（手动"保存BLF"导出共用同一把锁）；入队（ConcurrentQueue）
    /// 不加锁，不阻塞接收线程。
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
            /// <summary>该帧来自 CAN FD 总线：按 FD 对象落盘（即使有效字节 ≤8），与 CANoe 一致</summary>
            public bool IsFd;
        }

        // 文件状态（RecordingState 同步供 GUI 代理显示）
        public static IntPtr NowBlfHandle = IntPtr.Zero;
        public static string NowBlfPath = "";

        /// <summary>最近一次开始录制失败的原因（供 GUI 提示；成功时为空）</summary>
        public static string LastStartError = "";

        // 写队列（ConcurrentQueue = 无锁，不阻塞 CAN 接收线程）
        private static ConcurrentQueue<CanMessageData> _canMessageQueue = new ConcurrentQueue<CanMessageData>();

        // 连续写线程
        private static Thread _writerThread;
        private static volatile bool _writerRunning;
        private static string _writeDir = "";
        private static long _maxFileBytes = 128L * 1024 * 1024;

        /// <summary>BLF 句柄互斥锁（与手动导出共用）：写线程、线程池刷新、开始/停止/分割串行访问 binlog.dll</summary>
        private static object BlfLock { get { return BLFAPI.SyncRoot; } }

        /// <summary>BLF 新文件打开事件（GUI 订阅更新显示路径）</summary>
        public static event Action<string> BlfFileOpened;

        /// <summary>添加 CAN 消息到写入队列（拷贝数据，避免外部缓冲区复用）</summary>
        /// <param name="isFd">该帧是否来自 CAN FD 总线（决定落盘对象类型）</param>
        public static void AddCanMessageToWrite(uint canId, byte[] data, ulong timestamp,
            byte channel = 1, byte flags = 0, bool isFd = false)
        {
            byte[] dataCopy = new byte[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            _canMessageQueue.Enqueue(new CanMessageData
            {
                CanId = canId,
                Data = dataCopy,
                Timestamp = timestamp,
                Channel = channel,
                Flags = flags,
                IsFd = isFd
            });
        }

        /// <summary>开始 BLF 录制：创建文件 + 启动连续写线程。返回当前文件路径（失败返回空串，原因见 LastStartError）</summary>
        /// <param name="maxBytes">分段阈值：&lt;0=不分割，0=默认 128 MB，&gt;0=指定字节数</param>
        public static string StartBlf(string directory, long maxBytes = 0)
        {
            StopBlf();
            LastStartError = "";
            lock (BlfLock)
            {
                _writeDir = string.IsNullOrEmpty(directory) ? AppDomain.CurrentDomain.BaseDirectory : directory;
                _maxFileBytes = maxBytes < 0 ? 0 : (maxBytes > 0 ? maxBytes : 128L * 1024 * 1024);
                DiscardQueuedMessages(); /* 上一轮停止后残留的帧不得混进新文件 */
                CreateBlfFile();
                if (NowBlfHandle == IntPtr.Zero) return "";
                _writerRunning = true;
                _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "BLFWriter" };
                _writerThread.Start();
                return NowBlfPath;
            }
        }

        /// <summary>停止 BLF 录制：停写线程 → 刷队列 → 关句柄（全程互斥，避免关闭句柄时写线程仍在写）</summary>
        public static void StopBlf()
        {
            Thread writer;
            lock (BlfLock)
            {
                _writerRunning = false;
                writer = _writerThread;
                _writerThread = null;
            }
            // 先等写线程退出，且必须在不持锁的情况下等：写线程本身要拿锁才能收尾
            if (writer != null && writer.IsAlive && writer != Thread.CurrentThread)
            {
                try { writer.Join(5000); } catch { }
            }
            lock (BlfLock)
            {
                try { DrainCanWriteQueueCore(); } catch { }
                if (NowBlfHandle != IntPtr.Zero)
                {
                    try { BLFAPI.BLCloseHandle(NowBlfHandle); } catch { }
                    NowBlfHandle = IntPtr.Zero;
                }
                // 路径必须一起清空：否则停止后迟到的刷新会走"无句柄"分支，把裸帧追加进 .blf 造成文件损坏
                NowBlfPath = "";
                RecordingState.NowBlfFileAddrStr = "";
                DiscardQueuedMessages();
            }
        }

        /// <summary>创建（或分割时新建）BLF 文件。调用方必须已持有 BlfLock。
        /// 先建新文件再切句柄：分割失败时继续写旧文件，不会把正在进行的录制弄断。</summary>
        private static void CreateBlfFile()
        {
            string newPath = BuildUniqueFilePath(_writeDir);
            IntPtr blfHandle = BLFAPI.BLCreateFileW(newPath, GENERIC.GENERIC_WRITE);
            // 目录不存在时 binlog 仍会返回非零句柄，但文件其实没建出来——必须再确认文件真的存在，
            // 否则界面显示"录制中"却一个字都没写（现场表现为"点了开始录制没反应、也找不到文件"）
            if (blfHandle == IntPtr.Zero || !File.Exists(newPath))
            {
                if (blfHandle != IntPtr.Zero)
                {
                    try { BLFAPI.BLCloseHandle(blfHandle); } catch { }
                }
                LastStartError = "创建BLF文件失败：" + newPath + "（目录不存在或不可写）";
                Debug.WriteLine("[Recorder] " + LastStartError);
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

            // 切换文件：先把旧队列写进旧文件，再关旧句柄（文件分割时创建新文件）
            try { DrainCanWriteQueueCore(); } catch { }
            if (NowBlfHandle != IntPtr.Zero)
            {
                try { BLFAPI.BLCloseHandle(NowBlfHandle); } catch { }
                NowBlfHandle = IntPtr.Zero;
            }
            NowBlfHandle = blfHandle;
            NowBlfPath = newPath;
            LastStartError = "";
            RecordingState.NowBlfFileAddrStr = NowBlfPath; // 同步 GUI 代理显示
            try { BlfFileOpened?.Invoke(NowBlfPath); } catch { }
            Debug.WriteLine($"[Recorder] BLF文件创建成功: {NowBlfPath}, handle={NowBlfHandle}");
        }

        /// <summary>生成不重名的文件路径：文件名只到秒，同一秒内停止再开始会撞名并覆盖上一份录制</summary>
        private static string BuildUniqueFilePath(string dir)
        {
            string baseName = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string path = baseName + ".blf";
            int suffix = 1;
            while (File.Exists(path))
            {
                path = baseName + "_" + suffix + ".blf";
                suffix++;
            }
            return path;
        }

        private static void WriterLoop()
        {
            while (_writerRunning)
            {
                try
                {
                    FlushCanWriteQueue();
                    // 文件分割检查
                    if (_maxFileBytes > 0)
                        SplitIfTooLarge();
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Recorder] 写入线程错误: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
            // 退出前把队列里剩余的帧写完（停止流程随后才关句柄）
            try { DrainCanWriteQueue(); } catch { }
        }

        /// <summary>超过分割阈值就新建文件。所有句柄操作都在锁内完成，避免与停止/刷新并发</summary>
        private static void SplitIfTooLarge()
        {
            lock (BlfLock)
            {
                if (NowBlfHandle == IntPtr.Zero || string.IsNullOrEmpty(NowBlfPath)) return;
                bool tooLarge = false;
                try
                {
                    var fi = new FileInfo(NowBlfPath);
                    tooLarge = fi.Exists && fi.Length > _maxFileBytes;
                }
                catch { return; }
                if (tooLarge) CreateBlfFile();
            }
        }

        /// <summary>立即处理队列中的消息（无 Sleep，不阻塞 CAN 接收线程）</summary>
        public static void FlushCanWriteQueue()
        {
            lock (BlfLock)
            {
                FlushCanWriteQueueCore();
            }
        }

        /// <summary>一次刷新最多处理的帧数（限批是为了不长时间占着句柄锁，队列深时由 DrainCanWriteQueue 循环）</summary>
        private const int FlushBatchSize = 500;

        /// <summary>刷新实现本体。调用方必须已持有 BlfLock（写线程、停止流程、分割共用）。返回本次出队的帧数。</summary>
        private static int FlushCanWriteQueueCore()
        {
            if (_canMessageQueue.IsEmpty) return 0;
            if (NowBlfHandle == IntPtr.Zero)
            {
                // 未持有句柄（未开始/已停止）：丢弃残留帧，避免队列无限增长，也绝不写进 .blf
                DiscardQueuedMessages();
                return 0;
            }

            int dequeued = 0;
            while (dequeued < FlushBatchSize && _canMessageQueue.TryDequeue(out CanMessageData msg))
            {
                dequeued++;
                // CAN FD 帧（或长度 >8 的帧）必须按 FD 对象落盘，否则 >8 字节的数据会被截断
                if (msg.IsFd || msg.Data.Length > 8)
                    WriteCanFdObject(NowBlfHandle, msg);
                else
                    WriteClassicCanObject(NowBlfHandle, msg);
            }
            return dequeued;
        }

        /// <summary>
        /// 把队列彻底排空（停止录制 / 文件分割前调用）。
        /// 一次刷新最多 FlushBatchSize 条，队列深时（瞬时突发、磁盘卡顿后追帧）只刷一批就把剩下的丢掉，
        /// 表现为"停止录制后文件里少了最后一段"。这里循环到队列为空。
        /// </summary>
        private static void DrainCanWriteQueueCore()
        {
            const int MAX_ROUNDS = 2000; // 兜底：2000 × 500 = 100 万帧
            for (int i = 0; i < MAX_ROUNDS; i++)
            {
                if (FlushCanWriteQueueCore() < FlushBatchSize) return;
            }
        }

        /// <summary>排空队列（自取锁版本，供不持有锁的调用方使用）</summary>
        private static void DrainCanWriteQueue()
        {
            lock (BlfLock) { DrainCanWriteQueueCore(); }
        }

        /// <summary>经典 CAN 对象：32 字节对象头 + 通道/标志/DLC/ID + 8 字节数据 = 48 字节（录制与格式转换共用）</summary>
        internal static void WriteClassicCanObject(IntPtr handle, CanMessageData msg)
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
                canMessage.mID = MakeBlfId(msg.CanId);
                canMessage.mData = new byte[8];
                Array.Copy(msg.Data, canMessage.mData, Math.Min(msg.Data.Length, 8));
                Marshal.StructureToPtr(canMessage, msgPtr, false);
                BLFAPI.BLWriteObject(handle, msgPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>
        /// CAN FD 对象（最多 64 字节数据）。实时录制与格式转换共用，写出**变长对象**（类型 100，
        /// 52 + 载荷字节数，4 字节对齐）：8 字节帧 60 字节，相比固定 136 字节的 FD64 对象，
        /// 记录文件体积约减半。原实现固定写 FD64（类型 101，136 字节）。
        /// </summary>
        internal static void WriteCanFdObject(IntPtr handle, CanMessageData msg)
        {
            WriteCanFdObjectVariable(handle, msg);
        }
        /// <summary>
        /// CAN FD 对象（**变长**，类型 100）：对象长度 = 52 + 数据字节数（4 字节对齐，60~116 字节），
        /// 相比固定 136 字节的 FD64 对象能显著减小文件（8 字节帧 60 vs 136，32 字节帧 84 vs 136）。
        /// 布局来自 binlog.h：mChannel@32(UInt16)、mFlags@34、mDLC@35、mID@36、mArbBitCount@44、
        /// mCANFDFlags@45、mValidDataBytes@46、mData@52；读取方见 LogFileLoader.ReadCANFDMessage16。
        /// 实时录制（Recorder.WriteCanFdObject）与格式转换（BlfStreamWriter）共用此实现。
        /// </summary>
        internal static void WriteCanFdObjectVariable(IntPtr handle, CanMessageData msg)
        {
            int len = Math.Min(msg.Data == null ? 0 : msg.Data.Length, 64);
            int padded = ((len + 3) / 4) * 4;
            if (padded < 8) padded = 8;                 // 至少 8 字节，避免异常小的对象
            int objSize = 52 + padded;                  // 60 / 64 / 68 ... 116
            int structSize = Marshal.SizeOf<VBLCANFDMessage>();   // 72（其 mData 仅 16 字节，超出部分手动写指针）
            int bufSize = Math.Max(structSize, objSize + 8);
            IntPtr msgPtr = Marshal.AllocHGlobal(bufSize);
            try
            {
                // 先清零，保证对象尾部（可能超出结构体的部分）为确定的 0
                for (int i = 0; i < bufSize; i++) Marshal.WriteByte(msgPtr, i, 0);

                var canFd = new VBLCANFDMessage();
                canFd.mHeader.mBase.mSignature = BLFAPI.BL_OBJ_SIGNATURE;
                canFd.mHeader.mBase.mHeaderSize = (UInt16)Marshal.SizeOf(typeof(VBLObjectHeader));
                canFd.mHeader.mBase.mHeaderVersion = 1;
                canFd.mHeader.mBase.mObjectSize = (UInt16)objSize;
                canFd.mHeader.mBase.mObjectType = (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE;
                canFd.mHeader.mObjectFlags = (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS;
                canFd.mHeader.mObjectTimeStamp = (ulong)(msg.Timestamp * 1000);
                canFd.mChannel = msg.Channel;
                canFd.mFlags = 0;                       // 0 = Rx
                canFd.mDLC = BLFAPI.GetFdDlcCode(len);
                canFd.mArbBitCount = 0;
                canFd.mCANFDFlags = 0;                  // 无 BRS/ESI 标记（与 FD64 写法给出的信息量一致）
                canFd.mValidDataBytes = (byte)len;
                canFd.mReserved1 = 0;
                canFd.mReserved2 = 0;
                canFd.mFrameLength = 0;
                canFd.mID = MakeBlfId(msg.CanId);
                var head16 = new byte[16];
                if (len > 0) Array.Copy(msg.Data, head16, Math.Min(len, 16));
                canFd.mData = head16;

                Marshal.StructureToPtr(canFd, msgPtr, false);
                // 结构体 mData 只有 16 字节，17~64 字节直接写到对象体（mData@52）
                for (int i = 16; i < len; i++) Marshal.WriteByte(msgPtr, 52 + i, msg.Data[i]);

                BLFAPI.BLWriteObject(handle, msgPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>BLF 的 mID：bit31 标记扩展帧（29 位 ID），与 CANoe 的写法一致；读取方按 0x1FFFFFFF 掩码取真实 ID</summary>
        private static uint MakeBlfId(uint canId)
        {
            return canId > 0x7FF ? (0x80000000u | (canId & 0x1FFFFFFFu)) : canId;
        }

        /// <summary>丢弃队列中尚未落盘的帧（开始/停止时清场）</summary>
        private static void DiscardQueuedMessages()
        {
            while (_canMessageQueue.TryDequeue(out _)) { }
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
