using System;

namespace PCAN_Client.CAN_Data
{
    public static class ExceptionHandler
    {

        public static int Report(string str)
        {
            Exception en = new Exception(str);
            throw (en);
        }
    }
}
