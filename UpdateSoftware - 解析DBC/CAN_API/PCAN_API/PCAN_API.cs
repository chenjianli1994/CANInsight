using PCAN_Client.CAN_API;
using PCAN_Client.UTIL;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;


namespace PCAN_Client.PCAN_API
{
    internal class PCAN_API
    {
        private MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();

        internal static int PCAN_ChannelNum = 0;
        ushort PCAN_DeviceChannel = 255;
        /// <summary>多通道连接：逻辑通道号 → PCAN句柄（通道配置驱动的批量连接；为空时走PCAN_DeviceChannel单连接兼容路径）</summary>
        private readonly Dictionary<byte, ushort> _connectedChannels = new Dictionary<byte, ushort>();
        TPCANBaudrate ConnectBaud = TPCANBaudrate.PCAN_BAUD_500K;
        public byte PCAN_ReceiveThreadAlive = 0;
        Task PCAN_ReceiveThread = null;
        Boolean CanFDFlag = false;
        string bitrateFD = "f_clock_mhz=60, nom_brp=12, nom_tseg1=7, nom_tseg2=2, nom_sjw=1, data_brp=3, data_tseg1=7, data_tseg2=2, data_sjw=1"; /* 500k + 2M */

        Action action = null;

        //[DllImport("winmm")]
        //static extern void timeBeginPeriod(int t);
        //[DllImport("winmm")]
        //static extern void timeEndPeriod(int t);

        ushort[] PCAN_DeviceChannelBuf = new ushort[16]
        {
            PCANBasic.PCAN_USBBUS1,
            PCANBasic.PCAN_USBBUS2,
            PCANBasic.PCAN_USBBUS3,
            PCANBasic.PCAN_USBBUS4,
            PCANBasic.PCAN_USBBUS5,
            PCANBasic.PCAN_USBBUS6,
            PCANBasic.PCAN_USBBUS7,
            PCANBasic.PCAN_USBBUS8,
            PCANBasic.PCAN_USBBUS9,
            PCANBasic.PCAN_USBBUS10,
            PCANBasic.PCAN_USBBUS11,
            PCANBasic.PCAN_USBBUS12,
            PCANBasic.PCAN_USBBUS13,
            PCANBasic.PCAN_USBBUS14,
            PCANBasic.PCAN_USBBUS15,
            PCANBasic.PCAN_USBBUS16,
        };

        public static string PcanChannelNumRefresh()
        {
            try
            {
                PCAN_ChannelNum = 0;
                // 查询所有即插即用设备// 查询USB设备信息
                string query = "SELECT * FROM Win32_PnPEntity WHERE Description LIKE '%PCAN%'";
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                PCAN_ChannelNum = searcher.Get().Count;
                Console.WriteLine($"PCAN通道数量: {PCAN_ChannelNum} ");
                //ManagementObjectCollection managementBaseObjects = searcher.Get();
                //foreach (ManagementObject device in managementBaseObjects)
                //{
                //    // 检查设备是否是USB设备
                //    if (device["Caption"] != null && device["Caption"].ToString().Contains("PCAN"))
                //    {
                //        PCAN_ChannelNum++;
                //    }
                //}
            }
            catch { }
            return "OK";
        }

        public void SetPcanChannel(int channel)
        {
            PCAN_DeviceChannel = PCAN_DeviceChannelBuf[channel];
        }

        /// <summary>当前连接的物理通道（PCAN_USBBUSn）对应的逻辑通道号（经通道配置的硬件通道映射）</summary>
        private byte GetCurrentLogicChannel()
        {
            int idx = Array.IndexOf(PCAN_DeviceChannelBuf, PCAN_DeviceChannel);
            byte hw = (byte)(idx >= 0 ? idx + 1 : 1);
            return BaseParamter.GetBlfChannelByHw(hw);
        }

        public void PCAN_ChannelUninitialize()
        {
            multiMessageCANScheduler.Stop();
            Main.main.pCAN_API.PCAN_ReceiveThreadAlive = 0;
            foreach (var kv in _connectedChannels)
            {
                PCANBasic.Uninitialize(kv.Value);
            }
            _connectedChannels.Clear();
            PCANBasic.Uninitialize(PCAN_DeviceChannel);
            PCAN_DeviceChannel = 255;
        }

        /// <summary>
        /// 多通道批量连接：按通道配置（BusChannels）中各通道绑定的硬件通道批量Initialize，
        /// 任一成功即启动接收；返回成功连接的通道数
        /// </summary>
        public int ConnectMulti(bool canFDFlag)
        {
            this.CanFDFlag = canFDFlag;
            _connectedChannels.Clear();
            PCAN_ReceiveThreadAlive = 0;

            foreach (var ch in BaseParamter.BusChannels)
            {
                byte hw = ch.EffectiveHwChannel;
                if (hw < 1 || hw > 16) continue;
                ushort handle = PCAN_DeviceChannelBuf[hw - 1];

                PCANBasic.Uninitialize(handle);
                TPCANStatus result = canFDFlag
                    ? PCANBasic.InitializeFD(handle, bitrateFD)
                    : PCANBasic.Initialize(handle, ConnectBaud, (TPCANType)0, 0, 0);
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    _connectedChannels[ch.BlfChannelId] = handle;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[PCAN] 通道{ch.Name}(USB_{hw})连接失败: {result}");
                }
            }

            if (_connectedChannels.Count > 0)
            {
                PCAN_ReceiveThreadAlive = 1;
                multiMessageCANScheduler.Start();
            }
            return _connectedChannels.Count;
        }

        /// <summary>按逻辑通道号取已连接句柄；未连接该通道时回退当前单连接句柄</summary>
        private ushort GetHandleForChannel(byte logicChannel)
        {
            if (_connectedChannels.TryGetValue(logicChannel, out ushort handle)) return handle;
            return PCAN_DeviceChannel;
        }

        /// <summary>已连接的（逻辑通道号,句柄）枚举，供接收轮询</summary>
        internal IEnumerable<KeyValuePair<byte, ushort>> ConnectedChannels => _connectedChannels;
        public List<string> GetPCAN_ChannelRefresh()
        {
            TPCANStatus result;
            int ChannelNum;
            List<string> PCAN_Channel = new List<string>();

            Delay.start();
            if(0 == PCAN_ChannelNum)
            {
                PcanChannelNumRefresh();
            }
            else
            {
                /* empty */
            }
            ChannelNum = PCAN_ChannelNum;

            for (int i = 0; i < ChannelNum; i++)
            {
                if(PCAN_DeviceChannelBuf[i] == PCAN_DeviceChannel)
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(已连接)");
                    continue;
                }
                result = PCANBasic.Initialize(PCAN_DeviceChannelBuf[i], ConnectBaud, (TPCANType)0, 0, 0);
                if (TPCANStatus.PCAN_ERROR_HWINUSE == result)
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(已占用)");
                    continue;
                }
                else if (TPCANStatus.PCAN_ERROR_ILLHW == result)
                {
                    break;
                }
                else
                {
                    /* empty */
                }
                result = PCANBasic.GetStatus(PCAN_DeviceChannelBuf[i]);
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(空闲)");
                    PCANBasic.Uninitialize(PCAN_DeviceChannelBuf[i]);
                }
                else if (TPCANStatus.PCAN_ERROR_INITIALIZE == result)
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(已占用)");
                }
                else
                {
                    PCAN_Channel.Add("USB_" + (i + 1) + "(空闲)");
                }
            }
            Delay.stop();
            return PCAN_Channel;
        }

        public Boolean Connect(Boolean CanFDFlag)
        {
            TPCANStatus result;

            this.CanFDFlag = CanFDFlag;

            PCANBasic.Uninitialize(PCAN_DeviceChannel);
            PCAN_ReceiveThreadAlive = 0;
            Thread.Sleep(50);
            result = PCANBasic.GetStatus(PCAN_DeviceChannel);

            if (TPCANStatus.PCAN_ERROR_OK != result)
            {
                if (CanFDFlag)
                {
                    result = PCANBasic.InitializeFD(PCAN_DeviceChannel, bitrateFD);
                }
                else
                {
                    result = PCANBasic.Initialize(PCAN_DeviceChannel, ConnectBaud, (TPCANType)0, 0, 0);
                }
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    PCAN_ReceiveThreadAlive = 1;
                    // 启动调度器
                    multiMessageCANScheduler.Start();
#if false
                    if (null == action)
                    {
                        action = PCAN_ReadData;
                    }
                    else
                    {
                        /* empty */
                    }
                    action.BeginInvoke(null, null); //打开接收线程
#endif
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                PCAN_ReceiveThreadAlive = 0;
                Thread.Sleep(50);
                PCAN_ReceiveThread?.Dispose();
                PCAN_ReceiveThread = null;

                PCANBasic.Uninitialize(PCAN_DeviceChannel);
                if (TPCANStatus.PCAN_ERROR_OK != result)
                {
                    MessageBox.Show("连接错误");
                    return false;
                }
                return false;
            }
        }

        private static int GetReceiveDataDlc(int len)
        {
            int actualDataLength;
            switch (len)
            {
                case 9:
                    actualDataLength = 12;
                    break;
                case 10:
                    actualDataLength = 16;
                    break;
                case 11:
                    actualDataLength = 20;
                    break;
                case 12:
                    actualDataLength = 24;
                    break;
                case 13:
                    actualDataLength = 32;
                    break;
                case 14:
                    actualDataLength = 48;
                    break;
                case 15:
                    actualDataLength = 64;
                    break;
                default:
                    actualDataLength = len;
                    break;
            }
            return actualDataLength;
        }
        // 实现数据长度到DLC的转换（兼容低版本C#）
        private byte GetSendDataDlc(byte dataLength)
        {
            // 确保数据长度在有效范围内
            if (dataLength <= 8) return dataLength;
            if (dataLength <= 12) return 9;
            if (dataLength <= 16) return 10;
            if (dataLength <= 20) return 11;
            if (dataLength <= 24) return 12;
            if (dataLength <= 32) return 13;
            if (dataLength <= 48) return 14;
            if (dataLength <= 64) return 15;

            // 数据长度超过64字节，默认使用最大DLC值
            return 15;
        }
        /// <summary>发送数据。channel为逻辑通道号：多通道连接时路由到对应句柄，未连接该通道时回退当前单连接句柄</summary>
        internal TPCANStatus PCAN_SendData(TPCANMsg tPCANMsg, byte channel = 1)
        {
            ushort handle = GetHandleForChannel(channel);
            if (CanFDFlag)
            {
                TPCANMsgFD tPCANMsgFD = new TPCANMsgFD();
                tPCANMsgFD.DATA = new byte[64];
                Array.Copy(tPCANMsg.DATA, 0, tPCANMsgFD.DATA, 0, tPCANMsg.LEN);
                tPCANMsgFD.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_FD;
                tPCANMsgFD.DLC = GetSendDataDlc(tPCANMsg.LEN);
                tPCANMsgFD.ID = tPCANMsg.ID;
                return PCANBasic.WriteFD(handle, ref tPCANMsgFD);
            }
            else
            {
                return PCANBasic.Write(handle, ref tPCANMsg);
            }
        }

        private void PCAN_ReadData()
        {
            TPCANTimestamp timesamp = new TPCANTimestamp();
            TPCANMsgFD msgFD = new TPCANMsgFD();
            TPCANMsg msg = new TPCANMsg();
            long time_us;
            ulong TimestampBuffer = 0;
            TPCANStatus result;

            while (0 != PCAN_ReceiveThreadAlive)
            {
                if (CanFDFlag)
                {
                    result = PCANBasic.ReadFD(PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                }
                else
                {
                    result = PCANBasic.Read(PCAN_DeviceChannel, out msg, out timesamp);
                }
                if (TPCANStatus.PCAN_ERROR_OK == result)
                {
                    if (CanFDFlag)
                    {
                        time_us = (long)TimestampBuffer;
                        lock (CAN_API.CAN_API._receiveCanDataLock)
                        {
                            CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE,(ulong)time_us, GetCurrentLogicChannel());
                        }
                    }
                    else
                    {
                        time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                        lock (CAN_API.CAN_API._receiveCanDataLock)
                        {
                            CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA,msg.MSGTYPE, (ulong)time_us, GetCurrentLogicChannel());
                        }
                    }
                }
                else
                {
                    Thread.Sleep(20);
                }
            }
        }

        public class MultiMessageCANScheduler
        {
            bool callbackFlag;
            TPCANTimestamp timesamp = new TPCANTimestamp();
            TPCANMsgFD msgFD = new TPCANMsgFD();
            TPCANMsg msg = new TPCANMsg();
            long time_us;
            ulong TimestampBuffer = 0;
            TPCANStatus result;

            public void Start()
            {
                callbackFlag = false;
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "PCAN_Receive", 1);
            }

            public void Stop()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("PCAN_Receive");
            }

            private void SchedulerCallback()
            {
                try
                {
                    if (callbackFlag)
                    {
                        return;
                    }
                    callbackFlag = true;
                    //if (0 == Main.main.pCAN_API.PCAN_ReceiveThreadAlive || true == Main.main.canoe_API.aliveFlag)
                    //{
                    //    if (true == Main.main.canoe_API.aliveFlag)
                    //    {
                    //        //Main.main.PCAN_Connect();
                    //    }
                    //    else
                    //    {
                    //        //Stop();
                    //    }
                    //    Main.main.pCAN_API.PCAN_ReceiveThreadAlive = 0;
                    //}

                    var api = Main.main.pCAN_API;
                    if (api._connectedChannels.Count > 0)
                    {
                        // 多通道模式：轮询所有已连接句柄，按各自逻辑通道号上报
                        while (0 != api.PCAN_ReceiveThreadAlive)
                        {
                            bool anyData = false;
                            foreach (var kv in api._connectedChannels)
                            {
                                if (api.CanFDFlag)
                                {
                                    result = PCANBasic.ReadFD(kv.Value, out msgFD, out TimestampBuffer);
                                }
                                else
                                {
                                    result = PCANBasic.Read(kv.Value, out msg, out timesamp);
                                }
                                if (TPCANStatus.PCAN_ERROR_OK != result) continue;
                                anyData = true;
                                if (api.CanFDFlag)
                                {
                                    time_us = (long)TimestampBuffer;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE, (ulong)time_us, kv.Key);
                                    }
                                }
                                else
                                {
                                    time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA, msg.MSGTYPE, (ulong)time_us, kv.Key);
                                    }
                                }
                            }
                            if (!anyData) break;
                        }
                    }
                    else
                    {
                        // 单通道兼容路径
                        while (0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                        {
                            if (Main.main.pCAN_API.CanFDFlag)
                            {
                                result = PCANBasic.ReadFD(Main.main.pCAN_API.PCAN_DeviceChannel, out msgFD, out TimestampBuffer);
                            }
                            else
                            {
                                result = PCANBasic.Read(Main.main.pCAN_API.PCAN_DeviceChannel, out msg, out timesamp);
                            }
                            if (TPCANStatus.PCAN_ERROR_OK == result)
                            {
                                if (Main.main.pCAN_API.CanFDFlag)
                                {
                                    time_us = (long)TimestampBuffer;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msgFD.ID, (ushort)GetReceiveDataDlc(msgFD.DLC), msgFD.DATA, msgFD.MSGTYPE, (ulong)time_us, Main.main.pCAN_API.GetCurrentLogicChannel());
                                    }
                                }
                                else
                                {
                                    time_us = timesamp.micros + timesamp.millis * 1000 + timesamp.millis_overflow * 0x100000000 * 1000;
                                    lock (CAN_API.CAN_API._receiveCanDataLock)
                                    {
                                        CAN_API.CAN_API.CanReceive(msg.ID, msg.LEN, msg.DATA, msg.MSGTYPE,(ulong)time_us, Main.main.pCAN_API.GetCurrentLogicChannel());
                                    }
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    callbackFlag = false;
                }
                catch
                {
                    callbackFlag = false;
                }
            }
        }
    }
}
