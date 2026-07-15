using System;
using System.Collections.Generic;
using System.IO;

namespace PCAN_Client.util
{
    internal class tmpFileRead
    {
        private static byte[] byteReverse(byte[] data)
        {
            byte[] bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                bytes[i] = data[data.Length - i - 1];
            }
            return bytes;
        }

        public static int tmpFileTobytes(string XmlPath, int readLen, ref List<Byte> xmlData, ref UInt32 startAddr, ref List<Byte> BinData, ref UInt32 FlashDriverStartAddr, ref List<Byte> FlashDriverBinData)
        {
            int j = 0;
            int Addr = 0;
            UInt32 flashDriverCrcValue = 0;
            UInt32 flashDriverStartAddr = 0;
            UInt32 flashDriverLength = 0;
            UInt32 APPCrcValue = 0;
            UInt32 APPStartAddr = 0;
            UInt32 APPLength = 0;

            try
            {
                FileStream fileStream = new FileStream(XmlPath, FileMode.Open);//打开文件
                BinaryReader binaryReader = new BinaryReader(fileStream);      //打开流通道
                byte[] bytes = new byte[fileStream.Length];

                binaryReader.Read(bytes, 0, (int)fileStream.Length); //读取文件中的内容并保存到字节数组中
                binaryReader.Close();                      //关闭流
                fileStream.Close();                        //关闭文件]

                for(int i=0; i<0x180; i++) /* 读取falshdriver和app的数据 */
                {
                    if (bytes[i] == 0x7D && bytes[i+1] == 0x02)
                    {
                        i += 2;

                        byte[] temp = new byte[4];
                        Array.Copy(bytes, i, temp, 0, 4);
                        flashDriverCrcValue = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;
                        Array.Copy(bytes, i, temp, 0, 4);
                        flashDriverStartAddr = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;
                        Array.Copy(bytes, i, temp, 0, 4);
                        flashDriverLength = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;

                        Array.Copy(bytes, i, temp, 0, 4);
                        APPCrcValue = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;
                        Array.Copy(bytes, i, temp, 0, 4);
                        APPStartAddr = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;
                        Array.Copy(bytes, i, temp, 0, 4);
                        APPLength = BitConverter.ToUInt32(byteReverse(temp), 0);
                        i += 4;

                        if(0x4000 > flashDriverLength)/* Flasher Driver的长度应小于0x4000 */
                        {
                            FlashDriverStartAddr = flashDriverStartAddr;
                            FlashDriverBinData = new List<byte>();
                            for (j=i; j<i+ flashDriverLength; j++) /* 复制FlashDriver内容 */
                            {
                                FlashDriverBinData.Add(bytes[j]);
                            }
                            i += (int)flashDriverLength;

                            startAddr = APPStartAddr;
                            BinData = new List<byte>();
                            for (j = i; j < i + APPLength; j++)/* 复制APP内容 */
                            {
                                BinData.Add(bytes[j]);
                            }
                            i += (int)APPLength;

                            for (j = i; j < bytes.Length; j++)
                            {
                                xmlData.Add(bytes[j]);
                            }
                            return 1;
                        }
                        else
                        {
                            for (i = (bytes.Length - readLen); i < bytes.Length; i++)
                            {
                                xmlData.Add(bytes[i]);
                            }
                        }
                    }
                }
                bytes[0] = 1;
            }
            catch
            { return -1; }

            return 0;
        }
    }
}
