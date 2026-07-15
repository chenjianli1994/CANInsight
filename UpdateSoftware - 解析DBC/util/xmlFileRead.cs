using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCAN_Client.util
{
    internal class XmlFileRead
    {
        public static Boolean XmlToBytes(string XmlPath, ref int len, ref List<Byte> xmlData)
        {
            Boolean resultBool = true;
            try
            {
                using (StreamReader sr = new StreamReader(XmlPath, Encoding.GetEncoding("gb2312")))
                {
                    string str = "";
                    string[] result;
                    string temp;
                    while ((temp = sr.ReadLine()) != null)
                    {
                        str += temp + " ";
                    }
                    if (str.Contains("ISOFT") && str.Contains("Data"))
                    {
                        result = str.Split(new string[] { "<VALUE>", "</VALUE>" }, StringSplitOptions.RemoveEmptyEntries);
                        string valStr = "";
                        try
                        {
                            int.TryParse(result[1], out len);
                            for (int i = 0; i < len; i++)
                            {
                                valStr = result[3].Substring(1 + 3 * i, 2);
                                xmlData.Add(Convert.ToByte(valStr, 16));
                            }
                        }
                        catch
                        {
                            len = 0;
                            xmlData = null;
                        }
                    }
                    else
                    {
                        len = 0;
                        xmlData = null;
                    }
                }
            }
            catch { resultBool = false; }

            return resultBool;
        }
    }
}
