using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCAN_Client.DataLog
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLAppTrigger
    {
        public VBLObjectHeader mHeader;                     /* object header */
        public UInt64 mPreTriggerTime;             /* pre-trigger time */
        public UInt64 mPostTriggerTime;            /* post-trigger time */
        public UInt16 mChannel;                    /* channel of event which triggered (if any) */
        public UInt16 mFlags;                      /* trigger type (see above) */
        public UInt32 mAppSecific2;                /* app specific member 2 */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct VBLCANMessage
    {
        public VBLObjectHeader mHeader;                     /* object header */
        public UInt16 mChannel;                    /* application channel */
        public byte mFlags;                      /* CAN dir & rtr */
        public byte mDLC;                        /* CAN dlc */
        public UInt32 mID;                         /* CAN ID */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] mData;                    /* CAN data */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLCANFDMessage
    {
        public VBLObjectHeader mHeader;                     /* object header */
        public UInt16 mChannel;                    /* application channel */
        public byte mFlags;                      /* CAN dir & rtr */
        public byte mDLC;                        /* CAN dlc */
        public UInt32 mID;                         /* CAN ID */
        public UInt32 mFrameLength;                /* message length in ns - without 3 inter frame space bits and by Rx-message also without 1 End-Of-Frame bit */
        public byte mArbBitCount;                /* bit count of arbitration phase */
        public byte mCANFDFlags;                 /* CAN FD flags */
        public byte mValidDataBytes;             /* Valid payload length of mData */
        public byte mReserved1;                  /* reserved */
        public UInt32 mReserved2;                  /* reserved */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 84)]
        public byte[] mData;                   /* CAN FD data */
    }
    ;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLEnvironmentVariable
    {
        public VBLObjectHeader mHeader;                     /* object header - NOTE! set the object size to*/
        /* */
        /* mHeader.mObjectSize = sizeof( VBLEnvironmentVariable) + mNameLength + mDataLength; */
        /* */
        public uint mNameLength;                 /* length of variable name in bytes */
        public uint mDataLength;                 /* length of variable data in bytes */
        public IntPtr mName;                       /* variable name in MBCS */
        public IntPtr mData;                       /* variable data */
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLAppText
    {
        public VBLObjectHeader mHeader;                     /* object header - NOTE! set the object size to*/
        /* */
        /* mHeader.mObjectSize = sizeof( VBLAppText) + mTextLength; */
        /* */
        public UInt32 mSource;                     /* source of text */
        public UInt32 mReserved;                   /* reserved */
        public UInt32 mTextLength;                 /* text length in bytes */
        public IntPtr mText;                       /* text in MBCS */
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLEthernetFrame
    {
        public VBLObjectHeader mHeader;                     /* object header - NOTE! set the object size to*/
        /* mHeader.mObjectSize = sizeof( VBLEthernetFrame) + mPayLoadLength; */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] mSourceAddress;
        public UInt16 mChannel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] mDestinationAddress;
        public UInt16 mDir;                        /* Direction flag: 0=Rx, 1=Tx, 2=TxRq */
        public UInt16 mType;
        public UInt16 mTPID;
        public UInt16 mTCI;
        public UInt16 mPayLoadLength;              /* Number of valid mPayLoad bytes */
        public IntPtr mPayLoad;                    /* Max 1582 (1600 packet length - 18 header) data bytes per frame  */
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLObjectHeader
    {
        public VBLObjectHeaderBase mBase;                   /* base header object */
        public UInt32 mObjectFlags;            /* object flags */
        public UInt16 mClientIndex;            /* client index of send node */
        public UInt16 mObjectVersion;          /* object specific version */
        public UInt64 mObjectTimeStamp;        /* object timestamp */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct VBLObjectHeaderBase
    {
        public UInt32 mSignature;                        /* signature (BL_OBJ_SIGNATURE) */
        public UInt16 mHeaderSize;                       /* sizeof object header */
        public UInt16 mHeaderVersion;                    /* header version (1) */
        public UInt32 mObjectSize;                       /* object size */
        public UInt32 mObjectType;                       /* object type (BL_OBJ_TYPE_XXX) */
    }


    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VBLFileStatisticsEx
    {
        public UInt32 mStatisticsSize;                   /* sizeof (VBLFileStatisticsEx) */
        public byte mApplicationID;                    /* application ID */
        public byte mApplicationMajor;                 /* application major number */
        public byte mApplicationMinor;                 /* application minor number */
        public byte mApplicationBuild;                 /* application build number */
        public UInt64 mFileSize;                         /* file size in bytes */
        public UInt64 mUncompressedFileSize;             /* uncompressed file size in bytes */
        public UInt32 mObjectCount;                      /* number of objects */
        public UInt32 mObjectsRead;                      /* number of objects read */
        public SYSTEMTIME mMeasurementStartTime;             /* measurement start time */
        public SYSTEMTIME mLastObjectTime;                   /* last object time */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
        public UInt32[] mReserved;                     /* reserved */
    }
    internal class BLFStruct
    {
    }
}
