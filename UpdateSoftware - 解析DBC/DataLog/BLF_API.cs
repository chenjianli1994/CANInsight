using Peak.Can.Basic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vector.Tools;

namespace PCAN_Client.DataLog
{
    internal class BLF_API
    {
        static void test(string[] args)
        {
            try
            {
                string blfFilePath = "";
                using (var reader = new Vector.Tools.BinlogReader(blfFilePath))
                {
                    while (reader.Read())
                    {
                        var record = reader.CurrentRecord;
                        // 处理记录，比如输出记录的类型和时间戳
                        Console.WriteLine($"Record Type: {record.GetType().Name}, Timestamp: {record.Timestamp}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while reading the .blf file: {ex.Message}");
            }
        }
    }
}
