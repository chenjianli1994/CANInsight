using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PCAN_Client
{
    /// <summary>
    /// WinMM 多媒体定时器（1ms 高精度；自 Main.cs 迁出，供 CAN 接收轮询调度使用）
    /// </summary>
    public class MultimediaTimer : IDisposable
    {
        // 定时器回调委托
        private delegate void TimeProc(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

        // Win32 API 导入
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeSetEvent(
            uint uDelay,
            uint uResolution,
            TimeProc lpTimeProc,
            UIntPtr dwUser,
            uint fuEvent);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeKillEvent(uint uTimerID);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeEndPeriod(uint uPeriod);

        // 常量定义
        private const uint TIME_PERIODIC = 0x0001;
        private const uint TIME_ONESHOT = 0x0000;
        private const uint TIME_KILL_SYNC = 0x0100;

        private uint _timerId;
        private readonly TimeProc _timeProc;
        private readonly Action _callback;
        private bool _disposed = false;
        private bool _isRunning = false;

        public MultimediaTimer(Action callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _timeProc = new TimeProc(TimerCallback);
        }

        /// <summary>
        /// 启动定时器
        /// </summary>
        /// <param name="intervalMs">定时间隔（毫秒）</param>
        /// <param name="oneShot">是否只执行一次</param>
        public void Start(uint intervalMs, bool oneShot = false)
        {
            if (_isRunning) return;

            // 设置系统定时器精度（可选，但可以提高精度）
            timeBeginPeriod(1);

            uint mode = oneShot ? TIME_ONESHOT : TIME_PERIODIC;

            _timerId = timeSetEvent(
                intervalMs,        // 延迟时间（毫秒）
                0,                // 分辨率（0表示最高精度）
                _timeProc,        // 回调函数
                UIntPtr.Zero,     // 用户数据
                mode);           // 模式：周期性或单次

            if (_timerId == 0)
            {
                throw new Exception("无法创建多媒体定时器");
            }

            _isRunning = true;
        }

        /// <summary>
        /// 停止定时器
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            if (_timerId != 0)
            {
                timeKillEvent(_timerId);
                _timerId = 0;
            }

            // 恢复系统定时器精度
            timeEndPeriod(1);
            _isRunning = false;
        }

        private void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        {
            try
            {
                _callback?.Invoke();
            }
            catch (Exception ex)
            {
                // 记录异常，避免异常传播到非托管代码
                System.Diagnostics.Debug.WriteLine($"定时器回调异常: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                }

                Stop();
                _disposed = true;
            }
        }

        ~MultimediaTimer()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 1ms 周期 Action 调度器（自 Main.cs 迁出；PCAN/CANoe 接收轮询与脚本 tick 共用）
    /// </summary>
    public class MultiMessageCANScheduler : IDisposable
    {
        private readonly uint timeInterval = 1;
        private readonly MultimediaTimer _timer;
        private readonly object _lockObject = new object(); // 添加锁对象
        private List<Tuple<Action, string, uint>> ActionItems = new List<Tuple<Action, string, uint>>();
        private Dictionary<string, uint> keyValuePairs = new Dictionary<string, uint>();

        public MultiMessageCANScheduler(Action<List<CAN_Data.Message>> batchSendAction, uint timerResolutionMs = 1)
        {
            _timer = new MultimediaTimer(SchedulerCallback);
        }

        public void AddAction(Action action, string Name, uint timeMs)
        {
            lock (_lockObject) // 添加锁
            {
                try
                {
                    // 查询
                    var item = ActionItems.FirstOrDefault(x => x.Item2 == Name);
                    if (item != null)
                    {
                        ActionItems.RemoveAll(x => x.Item2 == Name);
                        keyValuePairs.Remove(Name);
                    }
                    // 添加元素
                    ActionItems.Add(Tuple.Create(action, Name, timeMs));
                    keyValuePairs.Add(Name, 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"添加Action失败: {ex.Message}");
                }
            }
        }

        public void RemoveAction(string Name)
        {
            lock (_lockObject) // 添加锁
            {
                try
                {
                    // 查询
                    var item = ActionItems.FirstOrDefault(x => x.Item2 == Name);
                    if (item != null)
                    {
                        ActionItems.RemoveAll(x => x.Item2 == Name);
                        keyValuePairs.Remove(Name);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"移除Action失败: {ex.Message}");
                }
            }
        }

        public void ClearAllActions()
        {
            lock (_lockObject) // 添加锁
            {
                ActionItems.Clear();
                keyValuePairs.Clear();
            }
        }

        public int GetActionCount()
        {
            lock (_lockObject) // 添加锁
            {
                return ActionItems.Count;
            }
        }

        public bool ContainsAction(string Name)
        {
            lock (_lockObject) // 添加锁
            {
                return ActionItems.Any(x => x.Item2 == Name);
            }
        }

        public void Start()
        {
            _timer.Start(timeInterval);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void SchedulerCallback()
        {
            List<Tuple<Action, string>> actionsToExecute = new List<Tuple<Action, string>>();

            // 第一步：收集需要执行的Action（在锁内）
            lock (_lockObject)
            {
                foreach (var item in ActionItems)
                {
                    try
                    {
                        keyValuePairs[item.Item2] += timeInterval;
                        if (keyValuePairs[item.Item2] >= item.Item3)
                        {
                            actionsToExecute.Add(Tuple.Create(item.Item1, item.Item2));
                            keyValuePairs[item.Item2] = 0; // 重置计时器
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"更新计时器时出错 {item.Item2}: {ex.Message}");
                    }
                }
            }

            // 第二步：执行Action（在锁外，避免阻塞其他线程）
            foreach (var actionTuple in actionsToExecute)
            {
                try
                {
                    actionTuple.Item1?.Invoke();
                    //Console.WriteLine($"成功执行: {actionTuple.Item2}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"执行 {actionTuple.Item2} 时出错: {ex.Message}");
                }
            }
        }

        // 获取当前所有Action的状态（用于调试）
        public Dictionary<string, uint> GetCurrentTimers()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, uint>(keyValuePairs);
            }
        }

        // 手动重置某个Action的计时器
        public void ResetTimer(string Name)
        {
            lock (_lockObject)
            {
                if (keyValuePairs.ContainsKey(Name))
                {
                    keyValuePairs[Name] = 0;
                }
            }
        }

        private void FlushSendBuffer()
        {
            // 如果需要清理缓冲区，可以在这里实现
        }

        public void Dispose()
        {
            Stop();
            ClearAllActions();
            _timer?.Dispose();
        }
    }
}
