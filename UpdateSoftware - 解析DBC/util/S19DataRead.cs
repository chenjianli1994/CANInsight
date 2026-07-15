using System;
using System.Collections.Generic;
using System.IO;

namespace PCAN_Client.util
{
    internal class S19DataRead
    {
        /******************************************************************************
         * 该部分为S19文件和srec文件解析规则 
         * S0，位于文件的第一行，和其他行不同，地址部分没有使用，用0000置位，整行表示记录的开始
         * S1表示地址长度为两字节（4字符）的记录，包含类型、长度、地址、数据和校验和五个部分
         * S2表示地址长度为三字节（6字符）的记录，包含类型、长度、地址、数据和校验和五个部分
         * S3表示地址长度为四字节（8字符）的记录，包含类型、长度、地址、数据和校验和五个部分
         * S5表示文件中含有S1、S2、S3记录的个数，其后不接数据，包含S5的记录并不是每个文件必须的
         * S7表示地址长度为四字节（8字符）的记录，包含类型、长度、地址和校验和四个部分，此行表示程序的结束
         * S8表示地址长度为三字节（6字符）的记录，包含类型、长度、地址和校验和四个部分，此行表示程序的结束
         * S9表示地址长度为两字节（4字符）的记录，包含类型、长度、地址和校验和四个部分，此行表示程序的结束
         * 只有S1、S2、S3需要写入Flash中
         * ***************************************************************************/

        public static Boolean MngS19SrecToBin(StreamReader HexReader, ref UInt32 startAddr, ref List<Byte> BinData)
        {
            Boolean resutl = true;
            try
            {
                uint sumLen = 0;//记录的是字符的长度
                int lineNum = 0;
                string szLine = ""; //数据处理临时字符串
                string sTemp = "";
                int nowLineAddressLen = 0;
                int nowLineRecordLen = 0;
                uint nowLineAddr;

                while (true)
                {
                    //读取一行数据
                    szLine = HexReader.ReadLine();
                    //读完所有行导出最后一个块
                    if (szLine == null)
                    {
                        //Log("总函数：" + lineNum.ToString() + "，总长度：" + sumLen.ToString());
                        sumLen = 0;
                        break;
                    }
                    lineNum++;

                    //判断第1字符是否是S
                    if (szLine.Substring(0, 1) != "S")
                    {
                        HexReader.Close();
                        throw new Exception("S19文件第1个字符不是S");
                    }

                    //获取类型
                    sTemp = szLine.Substring(0, 2);
                    //为S0或S5 S7-S9则略过
                    if (sTemp == "S0" || sTemp == "S5" || sTemp == "S7" || sTemp == "S8" | sTemp == "S9")
                    {
                        continue;
                    }

                    //获取当前行内数据的地址长度
                    nowLineRecordLen = Convert.ToInt32(szLine.Substring(2, 2), 16);
                    //Console.WriteLine("line=" + lineNum.ToString() + ",len=" + nowLineRecordLen.ToString());
                    if (nowLineRecordLen % 2 != 0)
                    {
                        //throw new Exception("S19文件第"+lineNum.ToString()+ "行数据长度不对");
                    }

                    //获取当前行内数据的地址
                    nowLineAddressLen = ParseAddressLen(sTemp);
                    nowLineAddr = Convert.ToUInt32(szLine.Substring(4, nowLineAddressLen), 16);
                    //首行地址当作文件起始地址
                    if (sumLen == 0)
                    {
                        startAddr = nowLineAddr;
                    }

                    int dataLen = nowLineRecordLen * 2 - nowLineAddressLen - 2;
                    sTemp = szLine.Substring(4 + nowLineAddressLen, dataLen);
                    for (int i = 0; i < dataLen; i += 2)
                    {
                        string valStr = sTemp.Substring(i, 2);
                        BinData.Add(Convert.ToByte(valStr, 16));
                        sumLen += 1;
                    }
                }
                HexReader.Close();
                return resutl;
            }
            catch (Exception ex)
            {
                resutl = false;
            }
            return resutl;
        }

        //返回地址长度
        private static int ParseAddressLen(string SecondFlag)
        {
            int AddressLen = 0;
            switch (SecondFlag)
            {
                case "S0":
                    AddressLen = 2 * 2;
                    break;
                case "S1":
                    AddressLen = 2 * 2;
                    break;
                case "S2":
                    AddressLen = 3 * 2;
                    break;
                case "S3":
                    AddressLen = 4 * 2;
                    break;
                case "S7":
                    AddressLen = 4 * 2;
                    break;
                case "S8":
                    AddressLen = 3 * 2;
                    break;
                case "S9":
                    AddressLen = 2 * 2;
                    break;
            }
            return AddressLen;
        }


        /* 获取hex文件 */
        public static Boolean MngHexToBin(StreamReader HexReader, ref UInt32 startAddr, ref List<Byte> BinData)
        {
            Boolean resutl = true;
            var hexFileData = ReadHexFile(HexReader);
            if(hexFileData.Data.Length > 500 && hexFileData.Data.Length < 524288)
            {
                startAddr = (uint)hexFileData.StartAddress;
                BinData.Clear();
                foreach (var tmp in hexFileData.Data)
                {
                    BinData.Add(tmp);
                }
            }
            else
            {
                resutl = false;
            }
            return resutl;

        }

        internal class HexFileData
        {
            public long StartAddress { get; set; }
            public byte[] Data { get; set; }
            public long Length => Data?.LongLength ?? 0;
            public long? EntryAddress { get; set; } // 可选的程序入口地址
        }

        public static HexFileData ReadHexFile(StreamReader HexReader)
        {
            uint currentSegment = 0;      // 当前段地址（用于类型02记录）
            uint currentUpperAddress = 0; // 当前高16位地址（用于类型04记录）
            long minAddress = long.MaxValue;
            long maxAddress = long.MinValue;
            long? entryAddress = null;    // 程序入口地址
            var dataRecords = new List<DataRecord>();

            while (true)
            {
                string line = HexReader.ReadLine();
                if (line == null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                    continue;

                byte[] recordBytes = ParseHexLine(line.Substring(1));
                ValidateChecksum(recordBytes);

                int byteCount = recordBytes[0];
                ushort address = (ushort)((recordBytes[1] << 8) | recordBytes[2]);
                byte recordType = recordBytes[3];
                byte[] data = new byte[byteCount];
                Array.Copy(recordBytes, 4, data, 0, byteCount);

                switch (recordType)
                {
                    case 0x00: // 数据记录
                        long fullAddress = CalculateFullAddress(currentSegment, currentUpperAddress, address);
                        dataRecords.Add(new DataRecord(fullAddress, data));
                        UpdateAddressRange(ref minAddress, ref maxAddress, fullAddress, byteCount);
                        break;

                    case 0x01: // 文件结束记录
                               // 可在此处添加处理结束标志的逻辑
                        break;

                    case 0x02: // 扩展段地址记录
                        if (byteCount != 2) throw new InvalidDataException("无效的段地址记录");
                        currentSegment = (uint)((data[0] << 8) | data[1]);
                        break;

                    case 0x04: // 扩展线性地址记录
                        if (byteCount != 2) throw new InvalidDataException("无效的线性地址记录");
                        currentUpperAddress = (uint)((data[0] << 8) | data[1]);
                        break;

                    case 0x03: // 起始段地址记录 (CS:IP)
                    case 0x05: // 起始线性地址记录
                        entryAddress = ParseEntryAddress(recordType, data, byteCount);
                        break;

                    default:
                        // 跳过未知记录类型
                        break;
                }
            }

            if (minAddress == long.MaxValue)
                throw new InvalidDataException("HEX文件中未找到有效的数据记录");

            byte[] mergedData = MergeDataRecords(dataRecords, minAddress, maxAddress);

            return new HexFileData
            {
                StartAddress = minAddress,
                Data = mergedData,
                EntryAddress = entryAddress
            };
        }

        private static byte[] ParseHexLine(string hexString)
        {
            if (hexString.Length % 2 != 0)
                throw new FormatException("无效的HEX行长度");

            byte[] bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static void ValidateChecksum(byte[] recordBytes)
        {
            byte checksum = 0;
            for (int i = 0; i < recordBytes.Length; i++)
            {
                checksum += recordBytes[i];
            }
            if (checksum != 0) // 所有字节之和（包括校验和）应为0模256
                throw new InvalidDataException($"校验和错误: 计算值={checksum:X2}，应为00");
        }

        private static long CalculateFullAddress(uint segment, uint upperAddress, ushort offset)
        {
            return ((long)upperAddress << 16) + ((long)segment << 4) + offset;
        }

        private static void UpdateAddressRange(ref long min, ref long max, long address, int byteCount)
        {
            min = Math.Min(min, address);
            max = Math.Max(max, address + byteCount - 1);
        }

        private static long? ParseEntryAddress(byte recordType, byte[] data, int byteCount)
        {
            if (recordType == 0x03 && byteCount == 4) // 起始段地址记录
            {
                uint cs = (uint)((data[0] << 8) | data[1]);
                uint ip = (uint)((data[2] << 8) | data[3]);
                return (cs << 4) + ip; // 转换为线性地址
            }

            if (recordType == 0x05 && byteCount == 4) // 起始线性地址记录
            {
                return (long)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
            }

            return null;
        }

        private static byte[] MergeDataRecords(List<DataRecord> records, long minAddress, long maxAddress)
        {
            long dataLength = maxAddress - minAddress + 1;
            if (dataLength > int.MaxValue)
                throw new InvalidOperationException($"数据量过大({dataLength}字节)，超过byte[]最大长度");

            byte[] result = new byte[dataLength];
            ArrayExtensions.Fill(result, (byte)0xFF); // 用0xFF填充未编程区域

            foreach (var record in records)
            {
                long offset = record.Address - minAddress;
                if (offset + record.Data.Length > result.Length)
                {
                    throw new InvalidOperationException($"数据超出范围: 地址=0x{record.Address:X8}, 偏移={offset}, 数据长度={record.Data.Length}");
                }
                Array.Copy(record.Data, 0, result, offset, record.Data.Length);
            }

            return result;
        }

        private class DataRecord
        {
            public long Address { get; }
            public byte[] Data { get; }

            public DataRecord(long address, byte[] data)
            {
                Address = address;
                Data = data;
            }
        }
    }

    // 修复: 创建独立的静态类来存放扩展方法
    public static class ArrayExtensions
    {
        /// <summary>
        /// 用指定值填充整个数组
        /// </summary>
        public static void Fill<T>(this T[] array, T value)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }
    }
}
