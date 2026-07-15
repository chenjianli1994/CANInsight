using System;
using System.Collections.Generic;

namespace PCAN_Client.util
{
    internal class CRC32
    {
        private static readonly uint[] Crc32Table = new uint[256];
        static CRC32()
        {
            uint i, j;
            uint crc;
            for (i = 0; i < 256; i++)
            {
                crc = i;
                for (j = 0; j < 8; j++)
                {
                    if ((crc & 1) > 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
                Crc32Table[i] = crc;
            }
        }
        public static byte[] ComputeUpdate(ref List<Byte> BinData)
        {
            byte[] bytes = new byte[BinData.Count];

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = BinData[i];
            }

            byte[] result = Compute(bytes, 0, bytes.Length);
            if ((BaseParamter.SelectProjectTypeEnum)BaseParamter.SelectProjectType == BaseParamter.SelectProjectTypeEnum.BeiQi_Old) /* VQ */
            {
                for (int i = 0; i < 4; i++)
                {
                    result[i] = (byte)(result[i] ^ 0xFF);
                }
            }
            else
            {
                /* empty */
            }
            return result;
        }

        public static byte[] Compute(ref List<Byte> BinData)
        {
            byte[] bytes = new byte[BinData.Count];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = BinData[i];
            }
            return Compute(bytes, 0, bytes.Length);
        }
        public static byte[] Compute(byte[] data)
        {
            return Compute(data, 0, data.Length);
        }
        public static byte[] Compute(byte[] data, int offset, int count)
        {

            uint crc = ComputeUint(data, offset, count);
            byte[] bytes = new byte[4];
            bytes[0] = (byte)((crc >> 24) & 0xff);
            bytes[1] = (byte)((crc >> 16) & 0xff);
            bytes[2] = (byte)((crc >> 8) & 0xff);
            bytes[3] = (byte)(crc & 0xff);
            return bytes;
        }

        public static uint ComputeUint(byte[] data, int offset, int count)
        {

            uint crc = 0xffffffff;

            for (var i = offset; i < offset + count; i++)
            {
                crc = (crc >> 8) ^ Crc32Table[(crc ^ data[i]) & 0xff];
            }
            return crc ^ 0xffffffff;
        }
    }
}
