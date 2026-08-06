using Peak.Can.Basic.BackwardCompatibility;

namespace PCAN_Client
{
    /// <summary>波特率档位配置：档位名作为通道配置持久化值；默认档与旧版本硬编码一致</summary>
    internal static class BaudrateConfig
    {
        internal const string DefaultClassicName = "500K";
        internal const string DefaultFdName = "500K+2M";

        internal sealed class ClassicPreset
        {
            internal readonly string Name;            // 如 "500K"
            internal readonly TPCANBaudrate PcanBaud; // PCAN 枚举
            internal readonly uint CanoeBaud;         // CANoe 波特率 Hz
            internal ClassicPreset(string name, TPCANBaudrate pcan, uint canoe)
            { Name = name; PcanBaud = pcan; CanoeBaud = canoe; }
        }
        internal sealed class FdPreset
        {
            internal readonly string Name;            // 如 "500K+2M"
            internal readonly uint ArbBaud;           // 仲裁段 Hz
            internal readonly uint DataBaud;          // 数据段 Hz
            internal FdPreset(string name, uint arb, uint data)
            { Name = name; ArbBaud = arb; DataBaud = data; }
        }

        internal static readonly ClassicPreset[] ClassicPresets = new[]
        {
            new ClassicPreset("125K", TPCANBaudrate.PCAN_BAUD_125K, 125000),
            new ClassicPreset("250K", TPCANBaudrate.PCAN_BAUD_250K, 250000),
            new ClassicPreset("500K", TPCANBaudrate.PCAN_BAUD_500K, 500000),
            new ClassicPreset("1M",   TPCANBaudrate.PCAN_BAUD_1M,   1000000),
        };
        internal static readonly FdPreset[] FdPresets = new[]
        {
            new FdPreset("500K+2M", 500000, 2000000), // 默认，与旧硬编码一致
            new FdPreset("250K+1M", 250000, 1000000),
            new FdPreset("500K+1M", 500000, 1000000),
            new FdPreset("1M+2M",   1000000, 2000000),
        };

        /// <summary>经典档查询：空/未知名 → 默认 500K（与旧行为一致）</summary>
        internal static ClassicPreset GetClassicPreset(string name)
        {
            foreach (var p in ClassicPresets) if (p.Name == name) return p;
            return ClassicPresets[2];
        }
        /// <summary>FD 档查询：空/未知名 → 默认 500K+2M（与旧行为一致）</summary>
        internal static FdPreset GetFdPreset(string name)
        {
            foreach (var p in FdPresets) if (p.Name == name) return p;
            return FdPresets[0];
        }

        /// <summary>PCAN FD 位定时字符串：60MHz 时钟、采样点 80%（tseg1=7, tseg2=2）。
        /// brp = 60e6/(10*baud)，不可整除时回退默认档。500K+2M 输出必须与旧字面量逐字符一致</summary>
        internal static string BuildPcanFdBitrateString(uint arbBaud, uint dataBaud)
        {
            long arbBrp = 60_000_000L / (10L * arbBaud);
            long dataBrp = 60_000_000L / (10L * dataBaud);
            if (arbBrp <= 0 || 60_000_000L != arbBrp * 10L * arbBaud ||
                dataBrp <= 0 || 60_000_000L != dataBrp * 10L * dataBaud)
            {
                return "f_clock_mhz=60, nom_brp=12, nom_tseg1=7, nom_tseg2=2, nom_sjw=1, data_brp=3, data_tseg1=7, data_tseg2=2, data_sjw=1";
            }
            return string.Format("f_clock_mhz=60, nom_brp={0}, nom_tseg1=7, nom_tseg2=2, nom_sjw=1, data_brp={1}, data_tseg1=7, data_tseg2=2, data_sjw=1", arbBrp, dataBrp);
        }
    }
}
