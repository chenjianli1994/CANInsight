using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCAN_Client.util
{
    internal class FrameCrcCal
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TsCOM_h_FrameCRC_ID_DataType
        {
            public uint FrameID; // 使用uint存储16进制值
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] CRCData;

            public TsCOM_h_FrameCRC_ID_DataType(uint id, byte[] data)
            {
                if (data.Length != 16)
                    throw new ArgumentException("CRC数据必须为16字节");

                FrameID = id;
                CRCData = data;
            }
        }

        public static readonly TsCOM_h_FrameCRC_ID_DataType[] CaCOM_a_FrameCRC_ID_DataCfg =
        {),
),
),
),
),
),
),
),
),
),
),
),
),
)
        };

        // CRC计算
        
        public static void messageCrcCal(uint messageID, int messageLen, ref byte[] canData, int aliveCount)
        {
            byte crc8;
            int messageIndex = 0;
            int LeCOM_u_FrameLen = 0;
            byte[] LaCOM_u_DataBuff = new byte[8];

            for (int i=0; i< CaCOM_a_FrameCRC_ID_DataCfg.Length; i++)
            {
                if (CaCOM_a_FrameCRC_ID_DataCfg[i].FrameID == messageID)
                {
                    LeCOM_u_FrameLen = messageLen;
                    messageIndex = i;
                    if (LeCOM_u_FrameLen >= 1)
                    {
                        Array.Copy(canData, 1, LaCOM_u_DataBuff, 0, LeCOM_u_FrameLen - 1);
                        LaCOM_u_DataBuff[0] &= 0xF0;
                        LaCOM_u_DataBuff[0] |= (byte)aliveCount;
                        LaCOM_u_DataBuff[LeCOM_u_FrameLen-1] = CaCOM_a_FrameCRC_ID_DataCfg[messageIndex].CRCData[aliveCount];
                        crc8 = E2E_P02_Protect(LaCOM_u_DataBuff, LeCOM_u_FrameLen);
                        
                        canData[0] = crc8;
                        canData[1] &= 0xF0;
                        canData[1] |= (byte)aliveCount;
                    }
                    else
                    {
                        /* empty */
                    }
                }
            }
        }

        private static byte E2E_P02_Protect(byte[] data, int Messagelength)
        {
            byte i, j, crc8;
            byte Init = 0xFF;  //初始值
            byte Polynomial = 0x2F; //多项式
            byte XOR = 0xFF; //结果异或值 XOR

            crc8 = Init;

            for (i = 0; i < Messagelength; i++)
            {
                crc8 ^= data[i]; //校验数据包含报文 uint81~uint8N + DATA ID
                for (j = 0; j < 8; j++)
                {
                    if (0 != (crc8 & 0x80))
                    {
                        crc8 = (byte)(((byte)(crc8 << 1)) ^ Polynomial);
                    }
                    else
                    {
                        crc8 = ((byte)(crc8 << 1));
                    }
                }
            }
            XOR = (byte)(crc8 ^ XOR);

            return (XOR);

        }
    }
}
