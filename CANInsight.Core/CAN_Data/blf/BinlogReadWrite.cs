using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCAN_Client.CAN_Data.blf
{
    public class BinlogReadWrite
    {
        /// <summary>读 BLF：与录制写线程、手动导出共用同一把 binlog 锁，避免并发调用破坏堆</summary>
        public static List<MessageBase> readBLF(string path)
        {
            lock (BLFAPI.SyncRoot) { return readBLFCore(path); }
        }

        //throw Exception
        private static List<MessageBase> readBLFCore(string path)
        {

            if (!File.Exists(path))
            {

                throw new FileNotFoundException();
            }

            List<MessageBase> readMessageList = new List<MessageBase>();
            IntPtr fileHandle = BLFAPI.BLCreateFileW(path, GENERIC.GENERIC_READ);
            if (fileHandle == IntPtr.Zero)
            {
                throw new Exception(String.Format("Read BLF file error.File Path:{0}", path));
            }
            VBLFileStatistics myFileStatistics;
            myFileStatistics.mStatisticsSize = 28;
            int retval = BLFAPI.BLGetFileStatistics(fileHandle, out myFileStatistics);

            VBLObjectHeaderBase headerBase;


            while (retval == 1)
            {
                retval = BLFAPI.BLPeekObject(fileHandle, out headerBase);
                if (retval == 1)
                {
                    if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE)
                    {
                        int structSize = Marshal.SizeOf<VBLCANMessage>();
                        IntPtr pMsg = Marshal.AllocHGlobal(structSize);
                        VBLCANMessage msg = new VBLCANMessage();
                        msg.mHeader.mBase = headerBase;
                        Marshal.StructureToPtr(msg, pMsg, false);
                        retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                        if (retval == 1)
                        {
                            VBLCANMessage retMsg = Marshal.PtrToStructure<VBLCANMessage>(pMsg);
                            // 时间戳/ID 必须取回读到的对象：原实现取的是入参种子结构（mObjectTimeStamp 恒为 0）
                            readMessageList.Add(new CANMessage(retMsg.mChannel, retMsg.mID & 0x1FFFFFFF, retMsg.mData,
                                retMsg.mHeader.mObjectTimeStamp / 1000000000.0));
                        }

                        Marshal.FreeHGlobal(pMsg);

                    }
                    else if (headerBase.mObjectType == (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64)
                    {
                        int structSize = Marshal.SizeOf<VBLCANFDMessage64>();
                        IntPtr pMsg = Marshal.AllocHGlobal(structSize);
                        VBLCANFDMessage64 msg = new VBLCANFDMessage64();
                        msg.mHeader.mBase = headerBase;
                        Marshal.StructureToPtr(msg, pMsg, false);
                        retval = BLFAPI.BLReadObjectSecure(fileHandle, pMsg, (UIntPtr)structSize);
                        if (retval == 1)
                        {
                            VBLCANFDMessage64 retMsg = Marshal.PtrToStructure<VBLCANFDMessage64>(pMsg);
                            // 原实现读了 FD 对象却直接丢弃：录制里全是 CAN FD 帧时读回来是空列表
                            int validBytes = Math.Min((int)retMsg.mValidDataBytes, 64);
                            byte[] data = new byte[validBytes];
                            if (validBytes > 0 && retMsg.mData != null)
                            {
                                Array.Copy(retMsg.mData, data, Math.Min(validBytes, retMsg.mData.Length));
                            }
                            // bit31 是扩展帧标记，真实 ID 在低 29 位
                            readMessageList.Add(new CANFDMessage(retMsg.mChannel, retMsg.mID & 0x1FFFFFFF, 0, data,
                                retMsg.mHeader.mObjectTimeStamp / 1000000000.0));
                        }
                        Marshal.FreeHGlobal(pMsg);
                    }
                    else
                    {
                        // 未知对象（应用文本/水印，以及闪退后残留的半截对象）必须消费掉：
                        // 不消费 BLPeekObject 会原地返回同一个位置，变成死循环；读不动就收尾
                        if (!SkipObject(fileHandle, headerBase)) break;
                    }
                }
            }
            BLFAPI.BLCloseHandle(fileHandle);
            return readMessageList;
        }

        /// <summary>跳过不关心的对象。返回 false 表示该对象读不动（文件被截断/损坏），调用方应停止读取。</summary>
        private static bool SkipObject(IntPtr fileHandle, VBLObjectHeaderBase headerBase)
        {
            int size = (int)headerBase.mObjectSize;
            if (size <= 0 || size > 16 * 1024 * 1024) return false;
            IntPtr pBuf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(headerBase, pBuf, false);
                return BLFAPI.BLReadObjectSecure(fileHandle, pBuf, (UIntPtr)size) == 1;
            }
            finally
            {
                Marshal.FreeHGlobal(pBuf);
            }
        }

        /// <summary>写 BLF：与录制写线程共用同一把 binlog 锁（录制过程中导出手动 BLF 不会破坏堆）</summary>
        public static bool writeBLF(string path, List<MessageBase> messageList)
        {
            lock (BLFAPI.SyncRoot) { return writeBLFCore(path, messageList); }
        }

        private static bool writeBLFCore(string path, List<MessageBase> messageList)
        {

            IntPtr fileHandle = BLFAPI.BLCreateFileW(path, GENERIC.GENERIC_WRITE);

            int retval = BLFAPI.BLSetApplication(fileHandle, BLAppID.BL_APPID_CANALYZER, 3, 0, 1);
            int timeSize = Marshal.SizeOf<SYSTEMTIME>();
            IntPtr timePtr = Marshal.AllocHGlobal(timeSize);
            SYSTEMTIME systemTime = new SYSTEMTIME();
            DateTime now = DateTime.Now;
            systemTime.wYear = (UInt16)now.Year;
            systemTime.wMonth = (UInt16)now.Month;
            systemTime.wDay = (UInt16)now.Day;
            systemTime.wHour = (UInt16)now.Hour;
            systemTime.wMinute = (UInt16)now.Minute;
            systemTime.wSecond = (UInt16)now.Second;
            Marshal.StructureToPtr(systemTime, timePtr, false);
            retval = BLFAPI.BLSetMeasurementStartTime(fileHandle, timePtr);
            Marshal.FreeHGlobal(timePtr);

            foreach (MessageBase msg in messageList)
            {
                CANType type = msg.canType;
                switch (type)
                {
                    case CANType.CAN:
                        CANMessage canMsg = (CANMessage)msg;
                        int msgSize = Marshal.SizeOf<VBLCANMessage>();
                        IntPtr msgPtr = Marshal.AllocHGlobal(msgSize);
                        VBLCANMessage canMessage = new VBLCANMessage();
                        canMessage.mHeader.mBase.mSignature = BLFAPI.BL_OBJ_SIGNATURE;
                        canMessage.mHeader.mBase.mHeaderSize = (UInt16)Marshal.SizeOf(typeof(VBLObjectHeader));
                        canMessage.mHeader.mBase.mHeaderVersion = 1;
                        canMessage.mHeader.mObjectTimeStamp = (ulong)(1000000000L * canMsg.timeStamp);
                        canMessage.mHeader.mBase.mObjectSize = (UInt16)Marshal.SizeOf(typeof(VBLCANMessage));
                        canMessage.mHeader.mBase.mObjectType = (uint)BLFObjectType.BL_OBJ_TYPE_CAN_MESSAGE;
                        canMessage.mHeader.mObjectFlags = (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS;
                        canMessage.mChannel = (UInt16)(canMsg.channel + 1);
                        canMessage.mFlags = 0;
                        canMessage.mDLC = canMsg.DLC;
                        // bit31 标记扩展帧（与 CANoe 一致；读取方按 0x1FFFFFFF 掩码取真实 ID）
                        canMessage.mID = canMsg.ID > 0x7FF ? (0x80000000 | canMsg.ID) : canMsg.ID;
                        canMessage.mData = canMsg.data;
                        Marshal.StructureToPtr(canMessage, msgPtr, false);
                        retval = BLFAPI.BLWriteObject(fileHandle, msgPtr);
                        Marshal.FreeHGlobal(msgPtr);
                        if (retval != 1)
                        {
                            //throw new Exception("Save message fail! The details of the message that failed to save are as follows:" + canMsg.ToString());
                        }

                        break;
                    case CANType.CANFD:
                        CANFDMessage canFDMsg = (CANFDMessage)msg;

                        int msgFDSize = Marshal.SizeOf<VBLCANFDMessage64>();
                        IntPtr msgFDPtr = Marshal.AllocHGlobal(msgFDSize);
                        VBLCANFDMessage64 canFDMessage = new VBLCANFDMessage64();
                        canFDMessage.mHeader.mBase.mSignature = BLFAPI.BL_OBJ_SIGNATURE;
                        canFDMessage.mHeader.mBase.mHeaderSize = (UInt16)Marshal.SizeOf(typeof(VBLObjectHeader));
                        canFDMessage.mHeader.mBase.mHeaderVersion = 1;
                        canFDMessage.mHeader.mObjectTimeStamp = (ulong)(1000000000L * canFDMsg.timeStamp);
                        // 对象长度按 CANoe 实际写出的 136 字节，而不是结构体的 144（多出的 8 字节是可选扩展帧数据）
                        canFDMessage.mHeader.mBase.mObjectSize = (UInt16)BLFAPI.CanFd64ObjectSize;
                        canFDMessage.mHeader.mBase.mObjectType = (uint)BLFObjectType.BL_OBJ_TYPE_CAN_FD_MESSAGE_64;
                        canFDMessage.mHeader.mObjectFlags = (uint)ObjectFlag.BL_OBJ_FLAG_TIME_ONE_NANS;
                        canFDMessage.mChannel = (byte)(canFDMsg.channel + 1);
                        canFDMessage.mFlags = 0x3000;
                        if (canFDMsg.messageFlag == MessageFlag.MSG_EXT)
                        {
                            canFDMessage.mID = 0x80000000 | canFDMsg.ID;
                        }
                        else
                        {
                            canFDMessage.mID = canFDMsg.ID;
                        }

                        int fdDataLen = Math.Min(canFDMsg.data.Length, 64);
                        canFDMessage.mValidDataBytes = (byte)fdDataLen;
                        byte[] data = new byte[64];

                        Array.Copy(canFDMsg.data, 0, data, 0, fdDataLen);
                        canFDMessage.mData = data;
                        canFDMessage.mDLC = BLFAPI.GetFdDlcCode(fdDataLen);
                        Marshal.StructureToPtr(canFDMessage, msgFDPtr, false);

                        retval = BLFAPI.BLWriteObject(fileHandle, msgFDPtr);
                        Marshal.FreeHGlobal(msgFDPtr);
                        if (retval != 1)
                        {
                            //throw new Exception("Save message fail! The details of the message that failed to save are as follows:" + canFDMsg.ToString());
                        }
                        break;
                    default:
                        break;
                }
            }
            BLFAPI.BLCloseHandle(fileHandle);
            return true;
        }
    }
}