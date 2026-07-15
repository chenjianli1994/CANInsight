using System;

namespace PCAN_Client
{
    internal class ResistanceSleep
    {
        //定义API函数
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(ExecutionFlag flags);

        [Flags]
        enum ExecutionFlag : uint
        {
            System = 0x00000001,
            Display = 0x00000002,
            Continus = 0x80000000,
        }

        /// <summary>
        ///阻止系统休眠，直到线程结束恢复休眠策略
        /// </summary>
        /// <param name="includeDisplay">是否阻止关闭显示器</param>
        public static void PreventSleep(bool includeDisplay = false)
        {
            try
            {
                if (includeDisplay)
                    SetThreadExecutionState(ExecutionFlag.System | ExecutionFlag.Display | ExecutionFlag.Continus);
                else
                    SetThreadExecutionState(ExecutionFlag.System | ExecutionFlag.Continus);
            }
            catch { }
        }

        /// <summary>
        ///恢复系统休眠策略
        /// </summary>
        public static void ResotreSleep()
        {
            try { SetThreadExecutionState(ExecutionFlag.Continus); } catch { }
        }

        /// <summary>
        ///重置系统休眠计时器
        /// </summary>
        /// <param name="includeDisplay">是否阻止关闭显示器</param>
        public static void ResetSleepTimer(bool includeDisplay = false)
        {
            try
            {
                if (includeDisplay)
                    SetThreadExecutionState(ExecutionFlag.System | ExecutionFlag.Display);
                else
                    SetThreadExecutionState(ExecutionFlag.System);
            }
            catch { }
        }
    }
}
