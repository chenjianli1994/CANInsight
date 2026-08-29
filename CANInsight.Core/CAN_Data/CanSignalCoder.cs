using System;
using System.Collections.Generic;
using System.Linq;

namespace PCAN_Client.CAN_Data
{
    /// <summary>
    /// 信号级编解码：按信号名+物理值打包 / 帧字节解码为信号物理值。
    /// 复用 CanMessageBuilder/CanSignalParser 的位打包与因子偏移换算，语义与 GUI 一致。
    /// </summary>
    public static class SignalCoder
    {
        /// <summary>按信号名+物理值打包成帧字节。未传的信号位保持 0；复用信号按 mux 选择信号值筛选。</summary>
        public static byte[] Pack(DbcHelper dbc, uint id, IDictionary<string, double> values, int dataLen = 8)
        {
            if (dbc == null) throw new ArgumentException("通道未挂载 DBC");
            Message msg = dbc.GetMessageById(id);
            if (msg == null) throw new ArgumentException("DBC 中不存在报文 ID 0x" + id.ToString("X"));
            if (dataLen <= 0) dataLen = 8;

            byte[] data = new byte[dataLen];
            int muxValue = -1;

            // 复用选择信号（若有且用户传了值）
            Signal muxSig = msg.signals.FirstOrDefault(s => s.multiplexerIndicator == -1);
            if (muxSig != null && TryGetValue(values, muxSig.signalName, out double mv))
            {
                long mraw = RawFromPhysical(mv, muxSig);
                CanMessageBuilder.EncodeSingleSignal(data, muxSig, mraw);
                muxValue = (int)mraw;
            }

            foreach (Signal sig in msg.signals)
            {
                if (sig.multiplexerIndicator == -1) continue;
                if (sig.multiplexerIndicator != -2 && sig.multiplexerIndicator != muxValue) continue;
                if (!TryGetValue(values, sig.signalName, out double pv)) continue;
                CanMessageBuilder.EncodeSingleSignal(data, sig, RawFromPhysical(pv, sig));
            }
            return data;
        }

        /// <summary>物理值→原始值（四舍五入避免因子非整除丢精度）</summary>
        public static long RawFromPhysical(double physical, Signal sig)
        {
            return (long)Math.Round((physical - sig.offset) / sig.factor);
        }

        /// <summary>帧字节解码为信号物理值字典（含因子/偏移/符号扩展/复用筛选）</summary>
        public static Dictionary<string, double> Decode(DbcHelper dbc, uint id, byte[] data)
        {
            if (dbc == null) throw new ArgumentException("通道未挂载 DBC");
            Message msg = dbc.GetMessageById(id);
            if (msg == null) throw new ArgumentException("DBC 中不存在报文 ID 0x" + id.ToString("X"));
            return CanSignalParser.ParseSignals(data, msg.signals);
        }

        private static bool TryGetValue(IDictionary<string, double> values, string name, out double v)
        {
            foreach (var kv in values)
                if (string.Equals(kv.Key.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    v = kv.Value;
                    return true;
                }
            v = 0;
            return false;
        }
    }

    /// <summary>E2E 保护（P02 风格）：CRC-8 SAE J1850 + 滚动计数器。信号名由调用方指定，位域取自 DBC。</summary>
    public static class E2ECrc
    {
        /// <summary>CRC-8 SAE J1850：多项式 0x1D，初值 0xFF，无反转（AUTOSAR E2E P02 常用）</summary>
        public static byte Crc8SaeJ1850(byte[] data)
        {
            byte crc = 0xFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x1D : crc << 1);
            }
            return crc;
        }

        /// <summary>把计数器写入 counterSig 位域、CRC 位清零后对整帧算 CRC-8 并写入 crcSig 位域。返回计算出的 CRC。</summary>
        public static byte Apply(byte[] data, Signal counterSig, Signal crcSig, int counter)
        {
            long mask = (counterSig.signalSize >= 63) ? long.MaxValue : (1L << (int)counterSig.signalSize) - 1;
            CanMessageBuilder.EncodeSingleSignal(data, counterSig, counter & mask);
            CanMessageBuilder.EncodeSingleSignal(data, crcSig, 0);
            byte crc = Crc8SaeJ1850(data);
            CanMessageBuilder.EncodeSingleSignal(data, crcSig, crc);
            return crc;
        }

        /// <summary>校验：返回(收到的CRC, 重算CRC, 计数器值)。CRC位按0参与重算，不修改入参。</summary>
        public static (byte crc, byte computed, int counter) Verify(byte[] data, Signal counterSig, Signal crcSig)
        {
            int counter = (int)CanSignalParser.ExtractRawValue(data, counterSig);
            byte crc = (byte)CanSignalParser.ExtractRawValue(data, crcSig);
            byte[] tmp = (byte[])data.Clone();
            CanMessageBuilder.EncodeSingleSignal(tmp, crcSig, 0);
            byte computed = Crc8SaeJ1850(tmp);
            return (crc, computed, counter);
        }

        /// <summary>按信号名找 Signal；未找到返回 null。</summary>
        public static Signal FindSignal(DbcHelper dbc, uint id, string signalName)
        {
            Message msg = dbc.GetMessageById(id);
            if (msg == null || string.IsNullOrEmpty(signalName)) return null;
            return msg.signals.FirstOrDefault(s => string.Equals(s.signalName.Trim(), signalName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
