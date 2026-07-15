using System;

namespace PCAN_Client.CAN_Data
{
    internal class CAN_Data
    {
        internal struct TslCanData
        {
            internal float LeftVolt;
            internal float RightVolt;
            internal float ModeVolt;
            internal float DefrostVolt;
        };

        internal static TslCanData SsCANDATA_h_TslData;
        internal static Boolean ReveiveFlag = false;

        internal static void GetTslCanData(out TslCanData canData)
        {
            canData = SsCANDATA_h_TslData;
        }

        internal static void DataDeal0x3A0(ref byte[] buf)
        {
            SsCANDATA_h_TslData.LeftVolt = SignalValue(16, 8, ref buf) * 0.02f;
            SsCANDATA_h_TslData.RightVolt = SignalValue(8, 8, ref buf) * 0.02f;
            SsCANDATA_h_TslData.ModeVolt = SignalValue(32, 8, ref buf) * 0.02f;
            SsCANDATA_h_TslData.DefrostVolt = SignalValue(24, 8, ref buf) * 0.02f;
            Main.main.VoltRefresh(SsCANDATA_h_TslData);
            ReveiveFlag = true;
        }

        internal static int SignalValue(int start, int length, ref byte[] data)
        {
            int data_merge = 0, data_value = 0;

            int lsbbit = start & 7;//获取LSB所在bit位

            int lsbbyte = start >> 3;//获取低字节所在位置

            int msbbyte = lsbbyte - ((lsbbit + length - 1) >> 3);//获取高字节所在位置

            //合并高低字节数据  
            for (int index = msbbyte; index < (lsbbyte + 1); index++)
            {
                data_merge += data[index] << ((lsbbyte - index) << 3);
            }
            data_value = data_merge >> lsbbit;//去尾
            data_value = data_value & ((1 << length) - 1);//按位与（只保留有效数据）
            return data_value;
        }
    }
}
