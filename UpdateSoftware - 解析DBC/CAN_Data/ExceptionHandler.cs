using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PCAN_Client.CAN_Data
{
    public static class ExceptionHandler
    {

        public static int Report(string str)
        {
            Exception en = new Exception(str);
            throw (en);
        }

        public static void Handle(Exception en)
        {
            MessageBox.Show(en.Message);
        }

    }
}
