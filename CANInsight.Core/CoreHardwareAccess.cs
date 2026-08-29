using System;

namespace PCAN_Client
{
    /// <summary>
    /// 同一进程内对 PEAK CAN/LIN 驱动访问的串行锁（枚举/初始化/释放互斥）。
    /// GUI 的 LIN_API.PeakHardwareAccess.SyncRoot 委托到本锁，保证 CAN 与 LIN 共享互斥。
    /// </summary>
    public static class CoreHardwareAccess
    {
        public static readonly object SyncRoot = new object();
    }
}
