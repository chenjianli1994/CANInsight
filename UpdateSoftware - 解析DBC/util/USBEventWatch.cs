using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PCAN_Client.util
{
    internal class USBEventWatch
    {
        public static Func<string> FunctionCallBack;      /* 回调函数 */
        public static void StartWMIWatcher(Func<string> func)
        {
            try
            {
                // 查询 PCAN 设备的插入事件
                var insertQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");

                var insertWatcher = new ManagementEventWatcher(insertQuery);
                insertWatcher.EventArrived += (sender, e) =>
                    HandleDeviceEvent(e, "插入");
                insertWatcher.Start();

                // 查询 PCAN 设备的移除事件
                var removeQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");

                var removeWatcher = new ManagementEventWatcher(removeQuery);
                removeWatcher.EventArrived += (sender, e) =>
                    HandleDeviceEvent(e, "拔出");
                removeWatcher.Start();
                FunctionCallBack = func;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WMI 监听初始化失败: {ex.Message}");
            }
        }

        static void HandleDeviceEvent(EventArrivedEventArgs e, string eventType)
        {
            try
            {
                var instance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                var deviceId = instance["DeviceID"].ToString();
                var description = instance["Description"]?.ToString();

                // 检查是否为 PCAN 设备
                if (description != null && description.Contains("PCAN"))
                {
                    PCAN_API.PCAN_API.PcanChannelNumRefresh();
                    FunctionCallBack();
                    if (!Main.updateingFlag)
                    {
                        FunctionCallBack();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理事件时出错: {ex.Message}");
            }
        }
    }
}
