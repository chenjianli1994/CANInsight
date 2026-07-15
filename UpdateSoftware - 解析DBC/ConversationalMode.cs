using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static PCAN_Client.Decipherment;

namespace PCAN_Client
{
    internal class ConversationalMode
    {
        public enum ConverMode_Status
        {
            ConverMode_ReadConverMode         = 1, /* 读取会话模式 */
            ConverMode_JumpToDefaultSession   = 2, /* 跳转到默认会话 */
            ConverMode_JumpToExtendedSession  = 3, /* 跳转到拓展会话 */
            ConverMode_JumpToProgramSession   = 4, /* 跳转到编程会话 */
            ConverMode_OK                     = 5, /* 本次处理完毕 */
        };

        public enum ConverMode
        {
            ConverMode_isDefaultSession     = 1,
            ConverMode_isProgrammingSession = 2,
            ConverMode_isExtendedSession    = 3,
        };

        private static readonly uint UDS_TX_ID = Main.uDS.UDS_TX_ID;

        public static ConverMode         converMode        = ConverMode.ConverMode_isDefaultSession;
        public static ConverMode_Status  converMode_Status = ConverMode_Status.ConverMode_OK;
        public static ConverMode         TargetconverMode  = ConverMode.ConverMode_isDefaultSession;

        public static void StartConverMode(ConverMode targetconverMode)
        {
            TPCANMsg tPCANMsg;
            Stopwatch timeSwNew = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0x86, 0xAA, 0xAA, 0xAA, 0xAA };

            TargetconverMode   = targetconverMode;

            converMode_Status = ConverMode_Status.ConverMode_ReadConverMode;
            /* 读取当前会话模式 */
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            Main.uDS.UDS_TxData(tPCANMsg);
        }

        public static void ConverModeFun(TPCANMsg msg)
        {
            int index;
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x06, 0x27, 0x02, 0x00, 0x00, 0x00, 0x00, 0xAA };
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            switch(converMode_Status)
            {
                case ConverMode_Status.ConverMode_ReadConverMode: /* 读取当前会话模式 */
                    if (0x04 == msg.DATA[0] && 0x62 == msg.DATA[1] && 0xF1 == msg.DATA[2] && 0x86 == msg.DATA[3])
                    {
                        converMode = (ConverMode)msg.DATA[4];
                        if(converMode != TargetconverMode)
                        {
                            index = 0;
                            tPCANMsg.DATA[index++] = 0x02;
                            tPCANMsg.DATA[index++] = 0x10;
                            tPCANMsg.DATA[index++] = 0x01;
                            Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                            Main.uDS.UDS_TxData(tPCANMsg);
                            converMode_Status = ConverMode_Status.ConverMode_JumpToDefaultSession;
                        }
                        else
                        {
                            converMode_Status = ConverMode_Status.ConverMode_OK;
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                    break;

                case ConverMode_Status.ConverMode_JumpToDefaultSession:
                    if (0x50 == msg.DATA[1] && 0x01 == msg.DATA[2])
                    {
                        converMode = ConverMode.ConverMode_isDefaultSession;
                        if (TargetconverMode != converMode)
                        {
                            index = 0;
                            tPCANMsg.DATA[index++] = 0x02;
                            tPCANMsg.DATA[index++] = 0x10;
                            tPCANMsg.DATA[index++] = 0x03;
                            Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                            Main.uDS.UDS_TxData(tPCANMsg);
                            converMode_Status = ConverMode_Status.ConverMode_JumpToExtendedSession;
                        }
                        else
                        {
                            converMode_Status = ConverMode_Status.ConverMode_OK;
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                    break;

                case ConverMode_Status.ConverMode_JumpToExtendedSession:
                    if (0x50 == msg.DATA[1] && 0x03 == msg.DATA[2])
                    {
                        converMode = ConverMode.ConverMode_isExtendedSession;
                        if (TargetconverMode != converMode)
                        {
                            index = 0;
                            tPCANMsg.DATA[index++] = 0x02;
                            tPCANMsg.DATA[index++] = 0x10;
                            tPCANMsg.DATA[index++] = 0x02;
                            Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                            Main.uDS.UDS_TxData(tPCANMsg);
                            converMode_Status = ConverMode_Status.ConverMode_JumpToProgramSession;
                        }
                        else
                        {
                            converMode_Status = ConverMode_Status.ConverMode_OK;
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                    break;

                case ConverMode_Status.ConverMode_JumpToProgramSession:
                    if (0x50 == msg.DATA[1] && 0x02 == msg.DATA[2])
                    {
                        converMode = ConverMode.ConverMode_isProgrammingSession;
                        converMode_Status = ConverMode_Status.ConverMode_OK;
                    }
                    else
                    {
                        /* empty */
                    }
                    break;

                default:
                    converMode_Status = ConverMode_Status.ConverMode_ReadConverMode;
                    break;
            }
        }
    }
}
