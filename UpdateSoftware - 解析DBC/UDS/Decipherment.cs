using Peak.Can.Basic.BackwardCompatibility;
using System;
using System.Diagnostics;

namespace PCAN_Client
{
    internal class Decipherment
    {
        public enum Ser27_LockStatus
        {
            Ser27_isLock = 0, /* 未解锁状态 */
            Ser27_isReadBootOrApp = 1, /* 读取在Boot Or App */
            Ser27_isReadConverMode = 2, /* 读取会话模式 */
            Ser27_isExtendedConversation = 3, /* 跳转进拓展会话 */
            Ser27_isSeedReq = 4, /* 请求种子 */
            Ser27_isDeciphermentCal = 5, /* 种子计算结果返回 */
            Ser27_isUnlock = 6, /* 解锁成功 */
        };

        public enum BootOrAppStatus
        {
            BootOrAppStatus_isApp = 0,
            BootOrAppStatus_isBoot = 1,
        };
        public enum ConverMode
        {
            ConverMode_isDefaultSession = 1,
            ConverMode_isProgrammingSession = 2,
            ConverMode_isExtendedSession = 3,
        };
        public static readonly byte UDS_Tx_FillNum = 0xAA;
        public static Ser27_LockStatus ser27_LockStatus = Ser27_LockStatus.Ser27_isLock;
        public static BootOrAppStatus BootOrAppSta = BootOrAppStatus.BootOrAppStatus_isBoot;
        public static ConverMode converMode = ConverMode.ConverMode_isDefaultSession;

        public static void Decipherment_Fill0xFF(ref byte[] buf, int index, int length, byte fillNum)
        {
            for (int i = 0; i < length; i++)
            {
                buf[index + i] = fillNum;
            }
        }

        public static void StartDecipherment()
        {
            TPCANMsg tPCANMsg;
            Stopwatch timeSwNew = new Stopwatch();
            byte[] cmd = new byte[8] { 0x03, 0x22, 0xF1, 0xEF, 0xAA, 0xAA, 0xAA, 0xAA };

            ser27_LockStatus = Ser27_LockStatus.Ser27_isReadBootOrApp;
            /* 读取当前在Boot还是App */
            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            Main.uDS.UDS_TxData(tPCANMsg);
        }

        public static void DeciphermentFun(TPCANMsg msg)
        {
            int index;
            UInt32 seed;
            UInt32 key1;
            UInt32 y;
            UInt32 z;
            UInt32 sum;
            uint n;
            UInt32[] key2 = new UInt32[4] { 0x4fe87269, 0x6bc361d8, 0x9b127d51, 0x5ba41903 }; /* 128 bits */
            TPCANMsg tPCANMsg;
            byte[] cmd = new byte[8] { 0x06, 0x27, 0x02, 0x00, 0x00, 0x00, 0x00, 0xAA };

            tPCANMsg.DATA = cmd;
            tPCANMsg.LEN = (byte)cmd.Length;
            tPCANMsg.ID = BaseParamter.UDS_TX_ID;
            tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            switch (ser27_LockStatus)
            {
                case Ser27_LockStatus.Ser27_isLock:
                    /* empty */
                    break;

                case Ser27_LockStatus.Ser27_isReadBootOrApp:
                    if (0x04 == msg.DATA[0] && 0x62 == msg.DATA[1] && 0xF1 == msg.DATA[2] && 0xEF == msg.DATA[3])
                    {
                        BootOrAppSta = (BootOrAppStatus)msg.DATA[4];
                        index = 0;
                        tPCANMsg.DATA[index++] = 0x03;
                        tPCANMsg.DATA[index++] = 0x22;
                        tPCANMsg.DATA[index++] = 0xF1;
                        tPCANMsg.DATA[index++] = 0x86;
                        Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                        Main.uDS.UDS_TxData(tPCANMsg);
                        ser27_LockStatus = Ser27_LockStatus.Ser27_isReadConverMode;
                    }
                    else
                    {
                        /* empty */
                    }
                    break;
                case Ser27_LockStatus.Ser27_isReadConverMode:
                    if (0x04 == msg.DATA[0] && 0x62 == msg.DATA[1] && 0xF1 == msg.DATA[2] && 0x86 == msg.DATA[3])
                    {
                        converMode = (ConverMode)msg.DATA[4];
                        if (converMode != ConverMode.ConverMode_isDefaultSession) /* 不在默认会话，则直接请求种子 */
                        {
                            index = 0;
                            tPCANMsg.DATA[index++] = 0x02;
                            tPCANMsg.DATA[index++] = 0x27;
                            if (BootOrAppSta == BootOrAppStatus.BootOrAppStatus_isApp)
                            {
                                tPCANMsg.DATA[index++] = 0x01;
                            }
                            else
                            {
                                tPCANMsg.DATA[index++] = 0x11;
                            }
                            Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                            Main.uDS.UDS_TxData(tPCANMsg);
                            ser27_LockStatus = Ser27_LockStatus.Ser27_isSeedReq;
                        }
                        else /* 跳转到拓展会话 */
                        {
                            index = 0;
                            tPCANMsg.DATA[index++] = 0x02;
                            tPCANMsg.DATA[index++] = 0x10;
                            tPCANMsg.DATA[index++] = 0x03;
                            Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                            Main.uDS.UDS_TxData(tPCANMsg);
                            ser27_LockStatus = Ser27_LockStatus.Ser27_isExtendedConversation;
                        }
                    }
                    else
                    {
                        /* empty */
                    }
                    break;
                case Ser27_LockStatus.Ser27_isExtendedConversation:
                    if (0x06 == msg.DATA[0] && 0x50 == msg.DATA[1] && 0x03 == msg.DATA[2]) /* 请求种子 */
                    {
                        index = 0;
                        tPCANMsg.DATA[index++] = 0x02;
                        tPCANMsg.DATA[index++] = 0x27;
                        if (BootOrAppSta == BootOrAppStatus.BootOrAppStatus_isApp)
                        {
                            tPCANMsg.DATA[index++] = 0x01;
                        }
                        else
                        {
                            tPCANMsg.DATA[index++] = 0x11;
                        }
                        Decipherment_Fill0xFF(ref tPCANMsg.DATA, index, tPCANMsg.DATA.Length - index, UDS_Tx_FillNum);
                        Main.uDS.UDS_TxData(tPCANMsg);
                        ser27_LockStatus = Ser27_LockStatus.Ser27_isSeedReq;
                    }
                    else
                    {
                        /* empty */
                    }
                    break;

                case Ser27_LockStatus.Ser27_isSeedReq:
                    if (0x67 == msg.DATA[1] && 0x01 == msg.DATA[2])
                    {
                        seed = (UInt32)(msg.DATA[6] + (msg.DATA[5] << 8) + (msg.DATA[4] << 16) + (msg.DATA[3] << 24));
                        if (0 == seed) /* 已解锁状态 */
                        {
                            ser27_LockStatus = Ser27_LockStatus.Ser27_isUnlock;
                            return;
                        }
                        else
                        {
                            /* empty */
                        }
                        key1 = ((((seed >> 4) ^ seed) << 3) ^ seed);

                        cmd[3] = (byte)((key1 & 0xFF000000) >> 24);
                        cmd[4] = (byte)((key1 & 0x00FF0000) >> 16);
                        cmd[5] = (byte)((key1 & 0x0000FF00) >> 8);
                        cmd[6] = (byte)(key1 & 0x000000FF);

                        tPCANMsg.DATA = cmd;
                        tPCANMsg.LEN = (byte)cmd.Length;
                        tPCANMsg.ID = BaseParamter.UDS_TX_ID;
                        tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                        Main.uDS.UDS_TxData(tPCANMsg);
                        ser27_LockStatus = Ser27_LockStatus.Ser27_isDeciphermentCal;
                    }
                    else
                    {
                        /* empty */
                    }

                    if (0x67 == msg.DATA[1] && 0x11 == msg.DATA[2])
                    {
                        seed = BitConverter.ToUInt32(msg.DATA, 3);
                        if (0 == seed) /* 已解锁状态 */
                        {
                            ser27_LockStatus = Ser27_LockStatus.Ser27_isUnlock;
                            return;
                        }
                        else
                        {
                            /* empty */
                        }
                        key1 = 0;
                        z = 0;
                        sum = 0; /* y = LOW_PART, z = HIGH_PART */
                        n = 64; /* number of iterations */
                        y = seed;
                        while (n > 0)
                        { /* encrypt */
                            y += (((z << 4) ^ (z >> 5)) + z) ^ (sum + key2[sum & 3]);
                            sum += 0x8f750a1d;
                            z += (((y << 4) ^ (y >> 5)) + y) ^ (sum + key2[(sum >> 11) & 3]);
                            n--;
                        }
                        cmd[3] = (byte)(z & 0x000000FF);
                        cmd[4] = (byte)((z & 0x0000FF00) >> 8);
                        cmd[5] = (byte)((z & 0x00FF0000) >> 16);
                        cmd[6] = (byte)((z & 0xFF000000) >> 24);

                        cmd[2] = 0x12;
                        tPCANMsg.DATA = cmd;
                        tPCANMsg.LEN = (byte)cmd.Length;
                        tPCANMsg.ID = BaseParamter.UDS_TX_ID;
                        tPCANMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                        Main.uDS.UDS_TxData(tPCANMsg);
                        ser27_LockStatus = Ser27_LockStatus.Ser27_isDeciphermentCal;
                    }
                    break;

                case Ser27_LockStatus.Ser27_isDeciphermentCal:
                    if (0x02 == msg.DATA[0] && 0x67 == msg.DATA[1])
                    {
                        if (0x02 == msg.DATA[2] || 0x12 == msg.DATA[2])
                        {
                            ser27_LockStatus = Ser27_LockStatus.Ser27_isUnlock;
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
                    break;

                case Ser27_LockStatus.Ser27_isUnlock:
                    /* empty */
                    break;

                default:
                    /* empty */
                    break;
            }
        }
    }
}
