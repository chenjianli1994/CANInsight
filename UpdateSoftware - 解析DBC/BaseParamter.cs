using PCAN_Client.CAN_Data;
using System;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;

namespace PCAN_Client
{
    internal class BaseParamter
    {
        public enum SelectProjectTypeEnum
        {
            LingPao = 0,
            BeiQi_Old = 1,
            BeiQi_New = 2,
        };

        public static Boolean AutoUpdateFlag = true;

        public readonly static byte[] RepairShopCode = new byte[]  /* 外部工具序列号 */
        {
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 01
        };

        public static int SelectProjectType = 0; /* 0:客户B平台 1：客户V50 2:客户AS */
        public readonly static UInt16 UDS_FunTX_ID = 0x7DF;
        public static UInt16 UDS_TX_ID = 0x7B0;
        public static UInt16 UDS_RX_ID = 0x7B8;
        public static UInt16 FlashDriverSelectIndex = 0;

        public static Boolean Pre_b_OnlyUpdateFlag = false;
        public static string DBCFilepath = "";
        public static DbcHelper dbcHelper = new DbcHelper();
        public static readonly byte[] BeiQiKey = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        public static readonly int KeyLength = 64;
        public static readonly byte KeyFillValue = 0x00;
        public static readonly byte[] BaseKeyBuf_uds = new byte[16] { 0xB7, 0xF9, 0xCE, 0x9C, 0xC9, 0x78, 0x17, 0xB6, 0x21, 0xE6, 0x9C, 0x98, 0x32, 0x8C, 0xAB, 0x3E };
        public static readonly byte[] BaseKeyBuf_program = new byte[16] { 0xD3, 0x97, 0xA1, 0xF0, 0x96, 0xB2, 0x4C, 0xA6, 0x8D, 0x4E, 0x8F, 0x8E, 0xCC, 0x51, 0xCB, 0x79 };
        public static int DataDlc = 8;
        public static readonly string softVersion = "V3.01.11 -- 2026-07-27";
        public static bool LP_24Selected = false;
        public static void MngBaseParamterInit()
        {
            SelectProjectType = Properties.Settings.Default.ProjectSelectIndex;
            UDS_TX_ID = (ushort)Properties.Settings.Default.UDS_TX_ID;
            UDS_RX_ID = (ushort)Properties.Settings.Default.UDS_RX_ID;
            FlashDriverSelectIndex = (ushort)Properties.Settings.Default.FlashDriverSelectIndex;
        }

        public static bool IsSelectProjectType(SelectProjectTypeEnum selectProjectType)
        { 
            return SelectProjectType == (int)selectProjectType;
        }

        private static int GetSendDataDlc(int len)
        {
            if (len <= 0)
            {
                return 0;
            }
            else if (len <= 1)
            {
                return 1;
            }
            else if (len <= 2)
            {
                return 2;
            }
            else if (len <= 3)
            {
                return 3;
            }
            else if (len <= 4)
            {
                return 4;
            }
            else if (len <= 5)
            {
                return 5;
            }
            else if (len <= 6)
            {
                return 6;
            }
            else if (len <= 7)
            {
                return 7;
            }
            else if (len <= 8)
            {
                return 8;
            }
            else if (len <= 12)
            {
                return 12;
            }
            else if (len <= 16)
            {
                return 16;
            }
            else if (len <= 20)
            {
                return 20;
            }
            else if (len <= 24)
            {
                return 24;
            }
            else if (len <= 32)
            {
                return 32;
            }
            else if (len <= 48)
            {
                return 48;
            }
            else
            {
                return 64;
            }
        }

        public static int GetCanSetDlc()
        {
            return GetSendDataDlc(DataDlc);
        }
        public static void SetCanSetDlc(int len)
        {
            DataDlc = GetSendDataDlc(len);
        }
    }
}
