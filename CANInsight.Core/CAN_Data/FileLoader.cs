using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PCAN_Client.CAN_Data
{
    public static class FileLoader
    {
        #region 方法
        /// <summary>
        /// 加载文本文件到字符串
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="stringOut">输出的字符串</param>
        /// <returns></returns>
        public static int Load(string path, ref string stringOut)
        {
            FileStream fs = null;
            StreamReader sr = null;

            try
            {
                // 使用 GB2312 编码打开文件
                using (fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (sr = new StreamReader(fs, Encoding.GetEncoding("GB2312")))
                {
                    stringOut = sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                sr?.Close();
                fs?.Close();
                Console.WriteLine($"发生错误：{ex.Message}");
            }
            return 0;
        }
        #endregion
    }
}
