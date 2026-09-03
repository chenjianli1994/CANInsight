using PCAN_Client.CAN_API;
using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using vxlapi_NET; // Import the XL Driver Library

namespace PCAN_Client.Canoe_API
{
    public class CanOe_API
    {
        private MultiMessageCANScheduler _recvScheduler = new MultiMessageCANScheduler(null);
        private const string RecvActionName = "CANoe_Receive";

        public XLDriver xlDriver = new XLDriver();
        public XLClass.xl_event xlEvent = new XLClass.xl_event();
        public int portHandle = 2; // Correctly initialize the port handle as an integer
        public ulong appChannelMask = 0;
        /// <summary>当前实际打开（已激活）的通道mask，仅CANOE_Open成功时写入、CANOE_Close清零；
        /// 识别"已连接"判定专用——不能用appChannelMask（FindAllChannel枚举时会把它污染为最后一路CAN通道）</summary>
        public ulong OpenedChannelMask = 0;
        public List<ulong> ChannelMaskList = new List<ulong>();
        public Boolean aliveFlag = false;
        public Boolean CanFDFlag = false;
        // 发送链路死亡检测：连续发送失败计数（成功清零），通知主界面断开后置位防重复；CANOE_Open重连成功时复位
        private int _consecutiveTxFailures = 0;
        private bool _txLinkDeadNotified = false;
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

            // 仅在未连接时关闭释放：已连接状态下枚举配置与活跃连接可共存，
            // 无条件Close会把正在收发的工作连接掐断（运行期识别刷新导致CANoe收几秒就停的根因）
            if (!aliveFlag) CANOE_Close();

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
            OpenedChannelMask = 0; // 实际连接通道集合清零（识别"已连接"判定用）
            try
            {
                StopReceiveScheduler();
                aliveFlag = false;
                XLDefine.XL_Status status = xlDriver.XL_ClosePort(portHandle);
                status = xlDriver.XL_DeactivateChannel(portHandle, appChannelMask);
            }
            catch
            {
                // 关闭异常不阻断释放流程
            }
        }
        /// <summary>打开CANoe port并激活mask内通道。CanFDFlag为mask内无通道配置行的兜底模式：
        /// 有行配置的通道按各自 CAN/CANFD 模式与波特率档位逐通道设置位定时（混合模式支持）</summary>
        public Boolean CANOE_Open(ulong channelIndex, Boolean CanFDFlag)
        {
            ulong permissionMask = 0;

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

            // Open the port（统一V4接口：支持CAN FD，同时完全兼容经典CAN收发）
            status = xlDriver.XL_OpenPort(ref portHandle, "CANExample", appChannelMask, ref permissionMask, 4096, XLDefine.XL_InterfaceVersion.XL_INTERFACE_VERSION_V4, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN);
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to open port: " + status);
                return false;
            }

            // 逐通道配置位定时：有行配置用行配置（模式+波特率档），无行配置用入参兜底模式+默认档
            for (int b = 0; b < 64; b++)
            {
                ulong bit = 1UL << b;
                if ((channelIndex & bit) == 0) continue;
                byte hw = (byte)(b + 1);
                int idx = BaseParamter.GetChannelIndexByHw(BaseParamter.HwTypeCanoe, hw);
                if (idx >= 0 ? BaseParamter.GetChannelCanFd(idx) : CanFDFlag)
                {
                    var fd = idx >= 0 ? BaseParamter.GetChannelFdPreset(idx) : BaudrateConfig.GetFdPreset("");
                    XLClass.XLcanFdConf CanFDConf = new XLClass.XLcanFdConf();
                    CanFDConf.arbitrationBitRate = fd.ArbBaud; /* 仲裁段波特率 */
                    CanFDConf.tseg1Abr = 25;
                    CanFDConf.tseg2Abr = 6;
                    CanFDConf.sjwAbr = 1;
                    CanFDConf.dataBitRate = fd.DataBaud; /* 数据段波特率 */
                    CanFDConf.tseg1Dbr = 31;
                    CanFDConf.tseg2Dbr = 8;
                    CanFDConf.sjwDbr = 1;
                    status = xlDriver.XL_CanFdSetConfiguration(portHandle, bit, CanFDConf);
                }
                else
                {
                    var c = idx >= 0 ? BaseParamter.GetChannelClassicPreset(idx) : BaudrateConfig.GetClassicPreset("");
                    status = xlDriver.XL_CanSetChannelBitrate(portHandle, bit, c.CanoeBaud);
                }
                if (status != XLDefine.XL_Status.XL_SUCCESS)
                {
                    Console.WriteLine("Failed to set CAN bitrate: " + status);
                    return false;
                }
            }

            // Activate the channel
            status = xlDriver.XL_ActivateChannel(portHandle, appChannelMask, XLDefine.XL_BusTypes.XL_BUS_TYPE_CAN, XLDefine.XL_AC_Flags.XL_ACTIVATE_NONE);
            if (status != XLDefine.XL_Status.XL_SUCCESS)
            {
                Console.WriteLine("Failed to activate channel: " + status);
                return false;
            }

            aliveFlag = true;
            OpenedChannelMask = appChannelMask; // 记录实际打开的通道集合（此时appChannelMask=入参mask，未被枚举污染）
            // 重连成功：重新武装发送链路死亡检测
            _consecutiveTxFailures = 0;
            _txLinkDeadNotified = false;
            //CanoeCanTransmit();
            //ReceiveCANFDMessage();
#if false
            CanoeCanReceive();
#else
            // 启动调度器
            StartReceiveScheduler();
#endif
            return true;
        }

        /// <summary>增量连接单个硬件通道：port未开时完整打开；已开时合并mask重开port
        /// （XL限制：port的channelMask在OpenPort时固定，Activate mask外通道会被拒绝，只能重开）
        /// 通道模式/波特率按该行配置生效，无行配置时按经典CAN默认处理</summary>
        public bool ActivateChannel(byte hw)
        {
            if (hw < 1 || hw > 64) return false;
            ulong bit = 1UL << (hw - 1);
            if (!aliveFlag)
            {
                return CANOE_Open(bit, false); // port未开：完整打开（OpenedChannelMask在Open内赋值）
            }
            if ((OpenedChannelMask & bit) != 0) return true; // 该通道已在连接中
            // 合并新通道到现有连接集合，重开port（Deactivate全部→Close→OpenPort(合并mask)→SetBitrate→Activate全部，
            // 由CANOE_Open完整流程完成；接收短暂中断后自动恢复）
            ulong newMask = OpenedChannelMask | bit;
            CANOE_Close();
            return CANOE_Open(newMask, false);
        }

        /// <summary>增量断开单个硬件通道；全部断开后关闭port（aliveFlag=false、接收回调移除）</summary>
        public bool DeactivateChannel(byte hw)
        {
            if (hw < 1 || hw > 64 || !aliveFlag) return false;
            ulong bit = 1UL << (hw - 1);
            try { xlDriver.XL_DeactivateChannel(portHandle, bit); } catch { }
            appChannelMask &= ~bit;
            OpenedChannelMask &= ~bit;
            if (OpenedChannelMask == 0) CANOE_Close();
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

        /// <summary>发送数据。channel为逻辑通道号：按通道配置的硬件通道绑定计算发送mask；无匹配时回退appChannelMask（单通道兼容）</summary>
        public Boolean CanoeCanTransmit(uint ID, ushort len, byte[] data, byte channel = 1)
        {
            XLDefine.XL_Status status;

            // 逻辑通道号 → 硬件通道 → 发送mask（越界或未配置时hw=channel）
            byte hw = channel;
            int chIdx = BaseParamter.GetChannelIndex(channel);
            if (chIdx >= 0)
                hw = BaseParamter.GetEffectiveHwChannel(chIdx);
            ulong txMask = (hw >= 1 && hw <= 64) ? (1UL << (hw - 1)) : appChannelMask;

            // 按通道模式决定帧格式：自动降级——≤8字节按经典CAN格式发送（不带EDL/BRS，兼容纯经典CAN节点）；>8字节FD通道必须FD格式
            bool fd = BaseParamter.GetChannelCanFdByLogic(channel);
            int txFlags = (ID > 0x7FF ? 0x0001 : 0); /* 0x0001=扩展帧 */
            if (fd && len > 8)
            {
                txFlags |= (int)(XLDefine.XL_CANFD_TX_MessageFlags.XL_CAN_TXMSG_FLAG_EDL | XLDefine.XL_CANFD_TX_MessageFlags.XL_CAN_TXMSG_FLAG_BRS);
            }
            uint msgCnt = 1;
            XLClass.XLcanTxEvent xLcanTxEvent = new XLClass.XLcanTxEvent
            {
                tag = XLDefine.XL_CANFD_TX_EventTags.XL_CAN_EV_TAG_TX_MSG,
                channelIndex = (byte)(hw - 1),
                tagData = new XLClass.XL_CAN_TX_MSG
                {
                    canId = ID,
                    msgFlags = (XLDefine.XL_CANFD_TX_MessageFlags)txFlags,
                    dlc = GetSendDataDlc(len),
                    data = new byte[len],
                }
            };
            Array.Copy(data, 0, xLcanTxEvent.tagData.data, 0, len);
            status = xlDriver.XL_CanTransmitEx(portHandle, txMask, ref msgCnt, xLcanTxEvent);
            if (status == XLDefine.XL_Status.XL_SUCCESS)
            {
                _consecutiveTxFailures = 0;
                return true;
            }
            else
            {
                Console.WriteLine(status);
                // 连续发送失败（通常为硬件被拔出/驱动异常）→ 通知主界面断开并提示。总线无ACK不会导致XL_CanTransmit失败，不会误触发
                if (aliveFlag && !_txLinkDeadNotified && ++_consecutiveTxFailures >= 10)
                {
                    _txLinkDeadNotified = true;
                    CAN_API.CAN_API.RaiseCanoeTxLinkDead();
                }
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
                                        // XL事件channelIndex为0-based，映射为1-based硬件通道号后再转逻辑通道号（混合硬件时限定CANoe类型反查消除同号歧义）
                                        byte logicCh = BaseParamter.GetLogicChannelByHw(BaseParamter.HwTypeCanoe, (byte)(xLcanRxEvent.channelIndex + 1));
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time, logicCh);
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
                                Thread.Sleep(5);
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
                                        // XL事件chanIndex为0-based，映射为1-based硬件通道号后再转逻辑通道号（混合硬件时限定CANoe类型反查消除同号歧义）
                                        byte logicCh = BaseParamter.GetLogicChannelByHw(BaseParamter.HwTypeCanoe, (byte)(xlEvent.chanIndex + 1));
                                        lock (CAN_API.CAN_API._receiveCanDataLock)
                                        {
                                            CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time, logicCh);
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
                                Thread.Sleep(5);
                            }
                        }
                    }
                });
                task.Start();
            }
            catch
            {
                Debug.WriteLine("[CANoe] 接收错误");
            }
        }

        /// <summary>启动接收轮询（自建 1ms 调度器，注册本实例回调）</summary>
        private void StartReceiveScheduler()
        {
            _recvScheduler.AddAction(SchedulerCallback, RecvActionName, 1);
            _recvScheduler.Start();
        }

        /// <summary>停止接收轮询</summary>
        private void StopReceiveScheduler()
        {
            _recvScheduler.RemoveAction(RecvActionName);
            _recvScheduler.Stop();
        }

        private bool callbackFlag;
        private XLDefine.XL_Status status;
        private XLClass.XLcanRxEvent xLcanRxEvent = new XLClass.XLcanRxEvent();

        private void SchedulerCallback()
        {
            try
            {
                if (callbackFlag) return;
                callbackFlag = true;
                int count = 100;
                while (count > 0 && aliveFlag)
                {
                    count--;
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
                                // Vector XL API: 扩展帧的 IDE 位编码在 id 的 bit 31
                                TPCANMessageType msgType = ((datas.ID & 0x80000000) != 0 || datas.ID > 0x7FF)
                                    ? TPCANMessageType.PCAN_MESSAGE_EXTENDED
                                    : TPCANMessageType.PCAN_MESSAGE_STANDARD;
                                if (msgType == TPCANMessageType.PCAN_MESSAGE_STANDARD && datas.ID > 0x7FF)
                                    msgType = TPCANMessageType.PCAN_MESSAGE_EXTENDED;
                                // XL事件channelIndex为0-based，映射为1-based硬件通道号后再转逻辑通道号（混合硬件时限定CANoe类型反查消除同号歧义）
                                byte logicCh = BaseParamter.GetLogicChannelByHw(BaseParamter.HwTypeCanoe, (byte)(xLcanRxEvent.channelIndex + 1));
                                lock (CAN_API.CAN_API._receiveCanDataLock)
                                {
                                    CAN_API.CAN_API.CanReceive(datas.ID, (ushort)datas.len, datas.data, msgType, datas.time, logicCh);
                                }
                            }
                        }
                    }
                    else
                    {
                        break; // 无事件（队列空）：立即退出本轮轮询，避免空闲时每ms 100次P/Invoke忙等
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





