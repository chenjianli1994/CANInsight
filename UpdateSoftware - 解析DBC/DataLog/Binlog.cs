using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PCAN_Client.DataLog
{
    internal class Binlog
    {
        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLCreateFile")]
        public static extern IntPtr BLCreateFile(IntPtr inPath, ulong outPath);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLSetApplication")]
        public static extern bool BLSetApplication(IntPtr hFile, byte appID, byte appMajor, byte appMinor, byte appBuild);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLSetMeasurementStartTime")]
        public static extern bool BLSetMeasurementStartTime(IntPtr hFile, SYSTEMTIME lpStartTime);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLSetWriteOptions")]
        public static extern bool BLSetWriteOptions(IntPtr hFile, UInt32 dwCompression, UInt32 dwFlags);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLWriteObject")]
        public static extern bool BLWriteObject(IntPtr hFile, ref VBLObjectHeaderBase pBase);//VBLObjectHeaderBase

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLCloseHandle")]
        public static extern bool BLCloseHandle(IntPtr hFile);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLGetFileStatisticsEx")]
        public static extern bool BLGetFileStatisticsEx(IntPtr hFile, IntPtr pStatistics);//VBLFileStatisticsEx


        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLPeekObject")]
        public static extern bool BLPeekObject(IntPtr hFile, ref VBLObjectHeaderBase pBase);


        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLReadObjectSecure")]
        public static extern bool BLReadObjectSecure(IntPtr hFile, IntPtr pBase, ulong expectedSize);// VBLObjectHeaderBase

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLFreeObject")]
        public static extern bool BLFreeObject(IntPtr hFile, ref VBLObjectHeaderBase pBase);

        [DllImport(@"binlog.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BLSkipObject")]
        public static extern bool BLSkipObject(IntPtr hFile, ref VBLObjectHeaderBase pBase);
    }
}
