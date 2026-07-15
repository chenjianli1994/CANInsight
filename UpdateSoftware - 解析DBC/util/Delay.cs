using System;
using System.Diagnostics;

namespace PCAN_Client.UTIL
{
    public class Delay
    {
        static Stopwatch stopwatch = new Stopwatch();

        public static void delay_ms(long ms)
        {
            delay_tick(ms * 10000);
            return;
        }

        public static void delay_us(long us)
        {
            delay_tick(us * 10);
            return;
        }

        public static void delay_tick(long tick)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long end = sw.ElapsedTicks + tick;
            while (sw.ElapsedTicks < end)
            {
                /* enpty */
            }
            sw.Stop();
        }

        internal static void delay_ms(object time)
        {
            throw new NotImplementedException();
        }

        internal static void start()
        {
            stopwatch.Restart();
            //stopwatch.Start();
        }

        internal static void stop() 
        {
            stopwatch.Stop();
            Console.WriteLine($"函数运行时间: {stopwatch.ElapsedMilliseconds} 毫秒");
        }
    }
}
