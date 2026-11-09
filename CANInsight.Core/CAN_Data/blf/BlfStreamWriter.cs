using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PCAN_Client.CAN_Data.blf
{
    /// <summary>
    /// 流式写 BLF：逐帧推给 binlog，不把整份文件读进内存（BLF/ASC 格式转换用）。
    /// 对象布局直接复用 Recorder 的写法（经典 48 字节 / CAN FD 136 字节，与 CANoe 一致），
    /// 句柄访问统一在 BLFAPI.SyncRoot 内完成——binlog 同一句柄非线程安全，录制线程可能同时在写另一个句柄。
    /// </summary>
    public sealed class BlfStreamWriter : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private readonly string _path;

        /// <summary>创建文件失败的原因（成功时为空串）</summary>
        public string LastError { get; private set; } = "";

        /// <summary>目标文件路径</summary>
        public string FilePath { get { return _path; } }

        /// <summary>已写入的帧数</summary>
        public long WrittenCount { get; private set; }

        /// <param name="path">目标 BLF 路径</param>
        /// <param name="measurementStartTime">测量起始时间（CANoe 显示的文件时间基准）</param>
        public BlfStreamWriter(string path, DateTime measurementStartTime)
        {
            _path = path;
            try
            {
                IntPtr handle = BLFAPI.BLCreateFileW(path, GENERIC.GENERIC_WRITE);
                // BLCreateFileW 对不存在的目录也会返回非零句柄却不建文件，用 File.Exists 兜底
                if (handle == IntPtr.Zero || !File.Exists(path))
                {
                    if (handle != IntPtr.Zero) BLFAPI.BLCloseHandle(handle);
                    LastError = "无法创建 BLF 文件：" + path;
                    return;
                }
                _handle = handle;

                BLFAPI.BLSetApplication(_handle, BLAppID.BL_APPID_CANALYZER, 3, 0, 1);
                var systemTime = new SYSTEMTIME
                {
                    wYear = (UInt16)measurementStartTime.Year,
                    wMonth = (UInt16)measurementStartTime.Month,
                    wDay = (UInt16)measurementStartTime.Day,
                    wHour = (UInt16)measurementStartTime.Hour,
                    wMinute = (UInt16)measurementStartTime.Minute,
                    wSecond = (UInt16)measurementStartTime.Second
                };
                IntPtr timePtr = Marshal.AllocHGlobal(Marshal.SizeOf<SYSTEMTIME>());
                try
                {
                    Marshal.StructureToPtr(systemTime, timePtr, false);
                    BLFAPI.BLSetMeasurementStartTime(_handle, timePtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(timePtr);
                }
                BLFAPI.BLSetWriteOptions(_handle, 0, 0); // 减少内部缓存，与录制写盘保持一致
            }
            catch (Exception ex)
            {
                LastError = "创建 BLF 文件失败：" + ex.Message;
                CloseHandle();
            }
        }

        /// <summary>写一帧；timestampSeconds 为相对文件起始的秒数，isFd 决定写 FD 对象还是经典对象</summary>
        public bool Write(uint canId, byte[] data, double timestampSeconds, byte channel, bool isFd)
        {
            if (_handle == IntPtr.Zero) return false;
            var msg = new Recorder.CanMessageData
            {
                CanId = canId & 0x1FFFFFFF,
                Data = data ?? new byte[0],
                // Recorder 内部按"微秒 × 1000 = 纳秒"落盘，这里保持同一约定
                Timestamp = (ulong)Math.Round(Math.Max(0.0, timestampSeconds) * 1000000.0),
                Channel = channel == 0 ? (byte)1 : channel,
                IsFd = isFd
            };
            try
            {
                lock (BLFAPI.SyncRoot)
                {
                    if (msg.IsFd || msg.Data.Length > 8) Recorder.WriteCanFdObject(_handle, msg);
                    else Recorder.WriteClassicCanObject(_handle, msg);
                }
                WrittenCount++;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            CloseHandle();
        }

        private void CloseHandle()
        {
            if (_handle == IntPtr.Zero) return;
            lock (BLFAPI.SyncRoot)
            {
                try { BLFAPI.BLCloseHandle(_handle); } catch { }
            }
            _handle = IntPtr.Zero;
        }
    }
}
