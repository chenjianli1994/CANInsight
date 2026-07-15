using PCAN_Client.CAN_API;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using vxlapi_NET; // Import the XL Driver Library

namespace PCAN_Client.Canoe_API
{
    internal class CanOe_API
    {
        private MultiMessageCANScheduler multiMessageCANScheduler = new MultiMessageCANScheduler();

        public XLDriver xlDriver = new XLDriver();
        public XLClass.xl_event xlEvent = new XLClass.xl_event();
        public int portHandle = 2; // Correctly initialize the port handle as an integer
        public ulong appChannelMask = 0;
        public List<ulong> ChannelMaskList = new List<ulong>();
        public Boolean aliveFlag = false;
        public Boolean CanFDFlag = false;
        public class conoeData
        {
            public byte[] data;
            public uint len;
            public uint ID;
            public ulong time;
        }
        public XLClass.xl_driver_config FindAllChannel(ulong channelNum, Boolean CanFDFlag)
        {
            ulong permissionMask = 0;
            this.CanFDFlag = CanFDFlag;

            XLDefine.XL_Status status;

            ChannelMaskList = new List<ulong>();

            CANOE_Close();

            status = xlDriver.XL_OpenDriver();
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to open driver: " + status);
            }

            // Get the driver configuration
            XLClass.xl_driver_config driverConfig = new XLClass.xl_driver_config();
            status = xlDriver.XL_GetDriverConfig(ref driverConfig);
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to get driver config: " + status);
            }

            for (int i = 0; i < driverConfig.channelCount; i++)
            {
                if ((driverConfig.channel[i].channelBusCapabilities & XLDefine.XL_BusCapabilities.XL_BUS_ACTIVE_CAP_CAN) != 0)
                {
                    appChannelMask = 1UL << driverConfig.channel[i].channelIndex;
                    permissionMask = appChannelMask;
                    if (channelNum < driverConfig.channel[i].channelIndex)
                    {
                        break;
                    }
                    else
                    {
                        ChannelMaskList.Add(driverConfig.channel[i].channelIndex);
                    }
                }
            }
            return driverConfig;
        }
        public void CANOE_Close()
        {
            try
            {
                multiMessageCANScheduler.Stop();
                aliveFlag = false;
                XLDefine.XL_Status status = xlDriver.XL_ClosePort(portHandle);
                status = xlDriver.XL_DeactivateChannel(portHandle, appChannelMask);
            }
            catch (Exception e)
            {
                //MessageBox.Show(e.ToString());
            }
        }
        public Boolean CANOE_Open(ulong channelIndex, Boolean CanFDFlag)
        {
            ulong permissionMask = 0;
            uint baud = 500000;

            this.CanFDFlag = CanFDFlag;
            XLDefine.XL_Status status;

            status = xlDriver.XL_OpenDriver();
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to open driver: " + status);
                return false;
            }

            // Get the driver configuration
            XLClass.xl_driver_config driverConfig = new XLClass.xl_driver_config();
            status = xlDriver.XL_GetDriverConfig(ref driverConfig);
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to get driver config: " + status);
                return false;
            }

            appChannelMask = channelIndex;
            permissionMask = channelIndex;

            // Open the port
            if (CanFDFlag)
            {
                status = xlDriver.XL_OpenPort(ref portHandle, "CANExample", appChannelMask, ref permissionMask, 4096, XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION_V4, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN);
            }
            else
            {
                status = xlDriver.XL_OpenPort(ref portHandle, "CANExample", appChannelMask, ref permissionMask, 4096, XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION_V3, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN);
            }
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to open port: " + status);
                return false;
            }

            // Set the CAN bitrate
            if (CanFDFlag)
            {
                XLClass.XLcanFdConf CanFDConf = new XLClass.XLcanFdConf();
                CanFDConf.arbitrationBitRate = 500000; /* 500k波特率 */
                CanFDConf.tseg1Abr = 25;
                CanFDConf.tseg2Abr = 6;
                CanFDConf.sjwAbr = 1;
                CanFDConf.dataBitRate = 2000000; // 2M波特率
                CanFDConf.tseg1Dbr = 31;
                CanFDConf.tseg2Dbr = 8;
                CanFDConf.sjwDbr = 1;
                status = xlDriver.XL_CanFdSetConfiguration(portHandle, appChannelMask, CanFDConf);
            }
            else
            {
                status = xlDriver.XL_CanSetChannelBitrate(portHandle, appChannelMask, baud);
            }
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to set CAN bitrate: " + status);
                return false;
            }

            // Activate the channel
            status = xlDriver.XL_ActivateChannel(portHandle, appChannelMask, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN, XLDefine.XL_AC_Flags.XL_ACTIVATE_NONE);
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to activate channel: " + status);
                return false;
            }

            aliveFlag = true;
            //CanoeCanTransmit();
            //ReceiveCANFDMessage();
#if false
            CanoeCanReceive();
#else
            // 启动调度器
            multiMessageCANScheduler.Start();
#endif
            return true;
        }

        private XLDefine.XL_CANFD_DLC GetSendDataDlc(int len)
        {
            if (len <= 0)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_0_BYTES;
            }
            else if (len <= 1)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_1_BYTES;
            }
            else if (len <= 2)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_2_BYTES;
            }
            else if (len <= 3)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_3_BYTES;
            }
            else if (len <= 4)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_4_BYTES;
            }
            else if (len <= 5)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_5_BYTES;
            }
            else if (len <= 6)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_6_BYTES;
            }
            else if (len <= 7)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_7_BYTES;
            }
            else if (len <= 8)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_8_BYTES;
            }
            else if (len <= 12)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_12_BYTES;
            }
            else if (len <= 16)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_16_BYTES;
            }
            else if (len <= 20)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_20_BYTES;
            }
            else if (len <= 24)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_24_BYTES;
            }
            else if (len <= 32)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_32_BYTES;
            }
            else if (len <= 48)
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_48_BYTES;
            }
            else
            {
                return XLDefine.XL_CANFD_DLC.DLC_CANFD_64_BYTES;
            }
        }

        public Boolean CanoeCanTransmit(uint ID, ushort len, byte[] data)
        {
            XLDefine.XL_Status status;

            if (CanFDFlag)
            {
                uint msgCnt = 1;
                XLClass.XLcanTxEvent xLcanTxEvent = new XLClass.XLcanTxEvent
                {
                    tag = XLDefine.XL_CANFD_TX_EventTags.XL_CAN_EV_TAG_TX_MSG,
                    channelIndex = (byte)appChannelMask,
                    tagData = new XLClass.XL_CAN_TX_MSG
                    {
                        canId = ID,
                        msgFlags = (XLDefine.XL_CANFD_TX_MessageFlags)((int)(XLDefine.XL_CANFD_TX_MessageFlags.XL_CAN_TXMSG_FLAG_EDL | XLDefine.XL_CANFD_TX_MessageFlags.XL_CAN_TXMSG_FLAG_BRS) | (ID > 0x7FF ? 0x0001 : 0)),
                        dlc = GetSendDataDlc(len),
                        data = new byte[len],
                    }
                };
                Array.Copy(data, 0, xLcanTxEvent.tagData.data, 0, len);
                status = xlDriver.XL_CanTransmitEx(portHandle, appChannelMask, ref msgCnt, xLcanTxEvent);
            }
            else
            {
                XLClass.xl_event txEvent = new XLClass.xl_event
                {
                    tag = XLDefine.XL_EventTags.XL_TRANSMIT_MSG,
                    tagData = new XLClass.xl_tag_data
                    {
                        can_Msg = new XLClass.xl_can_msg
                        {
                            id = ID,
                            flags = (XLDefine.XL_MessageFlags)((ID > 0x7FF) ? 0x04 : 0), // XL_CAN_MSG_FLAG_EXT
                            dlc = len,
                            data = new byte[len]
                        }
                    }
                };
                Array.Copy(data, 0, txEvent.tagData.can_Msg.data, 0, len);
                status = xlDriver.XL_CanTransmit(portHandle, appChannelMask, txEvent);
            }
            //XLClass.xl_event txEvent = new XLClass.xl_event
            //{
            //    tag = XLDefine.XL_EventTags.XL_TRANSMIT_MSG,
            //    tagData = new XLClass.xl_tag_data
            //    {
            //        can_Msg = new XLClass.xl_can_msg
            //        {
            //            id = ID,
            //            dlc = len,
            //            data = new byte[len]
            //        }
            //    }
            //};
            //Array.Copy(data, 0, txEvent.tagData.can_Msg.data, 0, len);
            //status = xlDriver.XL_CanTransmit(portHandle, appChannelMask, txEvent);
            if (status == XLDefine.XL_Status.XL_SUCCESS)
            {
                return true;
            }
            else
            {
                Console.WriteLine(status);
                return false;
            }
        }

        private static uint GetActualDataLength(XLDefine.XL_CANFD_DLC dlcEnum)
        {
            switch (dlcEnum)
            {
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_0_BYTES: return 0;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_1_BYTES: return 1;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_2_BYTES: return 2;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_3_BYTES: return 3;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_4_BYTES: return 4;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_5_BYTES: return 5;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_6_BYTES: return 6;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_7_BYTES: return 7;
                case XLDefine.XL_CANFD_DLC.DLC_CAN_CANFD_8_BYTES: return 8;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_12_BYTES: return 12;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_16_BYTES: return 16;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_20_BYTES: return 20;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_24_BYTES: return 24;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_32_BYTES: return 32;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_48_BYTES: return 48;
                case XLDefine.XL_CANFD_DLC.DLC_CANFD_64_BYTES: return 64;
                default: throw new ArgumentException("Invalid DLC enum value");
            }
        }
        public void CanoeCanReceive()
        {
            XLDefine.XL_Status status;
            XLClass.XLcanRxEvent xLcanRxEvent = new XLClass.XLcanRxEvent();
            try
            {
                //CanoeDataDeal();
                Task task = new Task(() =>
                {
                    while (aliveFlag)
                    {
                        if (CanFDFlag)
                        {
                            status = xlDriver.XL_CanReceive(portHandle, ref xLcanRxEvent);
                            if (status == XLDefine.XL_Status.XL_SUCCESS)
                            {
                                if (0x1F != xLcanRxEvent.tagData.canRxOkMsg.canId)
                                {
                                    if ((xLcanRxEvent.tag == XLDefine.XL_CANFD_RX_EventTags.XL_CAN_EV_TAG_RX_OK) && (0 != xLcanRxEvent.tagData.canRxOkMsg.data.Length) && (0 != xLcanRxEvent.tagData.canRxOkMsg.canId))
                                    {
                                        conoeData datas = new conoeData();
                                        datas.data = new byte[GetActualDataLength(xLcanRxEvent.tagData.canRxOkMsg.dlc)];
                                        Array.Copy(xLcanRxEvent.tagData.canRxOkMsg.data, 0, datas.data, 0, datas.data.Length);
                                        datas.ID = xLcanRxEvent.tagData.canRxOkMsg.canId;
                                        datas.len = GetActualDataLength(xLcanRxEvent.tagData.canRxOkMsg.dlc);
                                        //datas.len = (8 <= datas.len) ? 8 : datas.len;
                                        datas.time = xLcanRxEvent.timeStamp / 1000;
                                        TPCANMessageType msgType = ((int)xLcanRxEvent.tagData.canRxOkMsg.msgFlags & 0x01) != 0
                                            ? TPCANMessageType.PCAN_MESSAGE_EXTENDED
                                            : TPCANMessageType.PCAN_MESSAGE_STANDARD;
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time);
                                        }
                                    }
                                    else
                                    {
                                        /* empty */
                                    }
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else
                            {
                                if (Main.updateingFlag)
                                {
                                    //timeBeginPeriod(1);
                                    Thread.Sleep(1);
                                    //timeEndPeriod(1);
                                }
                                else
                                {
                                    //timeBeginPeriod(5);
                                    Thread.Sleep(5);
                                    //timeEndPeriod(5);
                                }
                            }
                        }
                        else
                        {
                            status = xlDriver.XL_Receive(portHandle, ref xlEvent);
                            if (status == XLDefine.XL_Status.XL_SUCCESS)
                            {
                                if (0x1F != xlEvent.tagData.can_Msg.id)
                                {
                                    if ((xlEvent.tag == XLDefine.XL_EventTags.XL_RECEIVE_MSG) && (0 != xlEvent.tagData.can_Msg.data.Length) && (0 != xlEvent.tagData.can_Msg.id))
                                    {
                                        conoeData datas = new conoeData();
                                        datas.data = xlEvent.tagData.can_Msg.data;
                                        datas.ID = xlEvent.tagData.can_Msg.id;
                                        datas.len = (uint)xlEvent.tagData.can_Msg.data.Length;
                                        datas.time = xlEvent.timeStamp / 1000;
                                        TPCANMessageType msgType = ((int)xlEvent.tagData.can_Msg.flags & 0x04) != 0
                                            ? TPCANMessageType.PCAN_MESSAGE_EXTENDED
                                            : TPCANMessageType.PCAN_MESSAGE_STANDARD;
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time);
                                        }
                                    }
                                    else
                                    {
                                        /* empty */
                                    }
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else
                            {
                                if (Main.updateingFlag)
                                {
                                    //timeBeginPeriod(1);
                                    Thread.Sleep(1);
                                    //timeEndPeriod(1);
                                }
                                else
                                {
                                    //timeBeginPeriod(5);
                                    Thread.Sleep(5);
                                    //timeEndPeriod(5);
                                }
                            }
                        }
                    }
                });
                task.Start();
            }
            catch
            {
                MessageBox.Show("接收错误");
            }
        }

        public class MultiMessageCANScheduler
        {
            bool callbackFlag;
            XLDefine.XL_Status status;
            XLClass.XLcanRxEvent xLcanRxEvent = new XLClass.XLcanRxEvent();

            public void Start()
            {
                callbackFlag = false;
                Main.main.multiMessageCANScheduler.AddAction(SchedulerCallback, "CANoe_Receive", 1);
            }

            public void Stop()
            {
                Main.main.multiMessageCANScheduler.RemoveAction("CANoe_Receive");
                callbackFlag = false;
            }

            private void SchedulerCallback()
            {
                try
                {
                    //if (false == Main.main.canoe_API.aliveFlag ||
                    //    0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                    //{
                    //    if(0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                    //    {
                    //        //Main.main.CANoeConnect();
                    //    }
                    //    else
                    //    {
                    //        Stop();
                    //    }
                    //    return;
                    //}
                    if (callbackFlag)
                    {
                        return;
                    }
                    callbackFlag = true;
                    int count = 100;
                    while (count > 0 && Main.main.canoe_API.aliveFlag)
                    {
                        count--;
                        //if (false == Main.main.canoe_API.aliveFlag ||
                        //    0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                        //{
                        //    if (0 != Main.main.pCAN_API.PCAN_ReceiveThreadAlive)
                        //    {
                        //        //Main.main.CANoeConnect();
                        //    }
                        //    else
                        //    {
                        //        Stop();
                        //    }
                        //    return;
                        //}
                        if (Main.main.canoe_API.CanFDFlag)
                        {
                            status = Main.main.canoe_API.xlDriver.XL_CanReceive(Main.main.canoe_API.portHandle, ref xLcanRxEvent);
                            if (status == XLDefine.XL_Status.XL_SUCCESS)
                            {
                                if (0x1F != xLcanRxEvent.tagData.canRxOkMsg.canId)
                                {
                                    if ((xLcanRxEvent.tag == XLDefine.XL_CANFD_RX_EventTags.XL_CAN_EV_TAG_RX_OK) && (0 != xLcanRxEvent.tagData.canRxOkMsg.data.Length) && (0 != xLcanRxEvent.tagData.canRxOkMsg.canId))
                                    {
                                        conoeData datas = new conoeData();
                                        datas.data = new byte[GetActualDataLength(xLcanRxEvent.tagData.canRxOkMsg.dlc)];
                                        Array.Copy(xLcanRxEvent.tagData.canRxOkMsg.data, 0, datas.data, 0, datas.data.Length);
                                        datas.ID = xLcanRxEvent.tagData.canRxOkMsg.canId;
                                        datas.len = GetActualDataLength(xLcanRxEvent.tagData.canRxOkMsg.dlc);
                                        //datas.len = (8 <= datas.len) ? 8 : datas.len;
                                        datas.time = xLcanRxEvent.timeStamp / 1000;
                                        // Vector XL API: 扩展帧的 IDE 位编码在 id 的 bit 31
                                        TPCANMessageType msgType = ((datas.ID & 0x80000000) != 0 || datas.ID > 0x7FF)
                                            ? TPCANMessageType.PCAN_MESSAGE_EXTENDED
                                            : TPCANMessageType.PCAN_MESSAGE_STANDARD;
                                        if (msgType == TPCANMessageType.PCAN_MESSAGE_STANDARD && datas.ID > 0x7FF)
                                            msgType = TPCANMessageType.PCAN_MESSAGE_EXTENDED;
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time);
                                        }
                                    }
                                    else
                                    {
                                        /* empty */
                                    }
                                }
                                else
                                {
                                    /* empty */
                                }
                            }
                            else
                            {
                                /* empty */
                            }
                        }
                        else
                        {
                            status = Main.main.canoe_API.xlDriver.XL_Receive(Main.main.canoe_API.portHandle, ref Main.main.canoe_API.xlEvent);
                            if (status == XLDefine.XL_Status.XL_SUCCESS)
                            {
                                if (0x1F != Main.main.canoe_API.xlEvent.tagData.can_Msg.id)
                                {
                                    if ((Main.main.canoe_API.xlEvent.tag == XLDefine.XL_EventTags.XL_RECEIVE_MSG) && (0 != Main.main.canoe_API.xlEvent.tagData.can_Msg.data.Length) && (0 != Main.main.canoe_API.xlEvent.tagData.can_Msg.id))
                                    {
                                        conoeData datas = new conoeData();
                                        datas.data = Main.main.canoe_API.xlEvent.tagData.can_Msg.data;
                                        datas.ID = Main.main.canoe_API.xlEvent.tagData.can_Msg.id;
                                        datas.len = (uint)Main.main.canoe_API.xlEvent.tagData.can_Msg.data.Length;
                                        datas.time = Main.main.canoe_API.xlEvent.timeStamp / 1000;
                                        // Vector XL API: 扩展帧的 IDE 位编码在 id 的 bit 31
                                        TPCANMessageType msgType = ((datas.ID & 0x80000000) != 0 || datas.ID > 0x7FF)
                                            ? TPCANMessageType.PCAN_MESSAGE_EXTENDED
                                            : TPCANMessageType.PCAN_MESSAGE_STANDARD;
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time);
                                        }
                                    }
                                    else
                                    {
                                        /* empty */
                                    }
                                }
                                else
                                {
                                    /* empty */
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





