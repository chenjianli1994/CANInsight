using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PCAN_Client.Update
{
    public partial class Custom_Add : Form
    {
        public Custom_Add()
        {
            InitializeComponent();
        }

        public enum OpenType
        {
            Add,
            Change,
        };
        private OpenType openType;
        Func<string, int> func;

        private void InitLoad()
        {
            comboBoxName.Items.Clear();
            comboBoxName.Items.Add("10_DiagnosticSessionControl");
            comboBoxName.Items.Add("11_ECUReset");
            comboBoxName.Items.Add("14_ClearDiagnosticInformation");
            comboBoxName.Items.Add("19_ReadDTCInformation");
            comboBoxName.Items.Add("22_ReadDataByIdentifier");
            comboBoxName.Items.Add("23_ReadMemoryByAddress");
            comboBoxName.Items.Add("24_ReadScalingDataByIdentifier");
            comboBoxName.Items.Add("27_SecurityAccessSeed");
            comboBoxName.Items.Add("27_SecurityAccessKey");
            comboBoxName.Items.Add("28_CommunicationControl");
            comboBoxName.Items.Add("2A_ReadDataByPeriodicIdentifier");
            comboBoxName.Items.Add("2C_DynamicallyDefineDataIdentifier");
            comboBoxName.Items.Add("2E_WriteDataByIdentifier");
            comboBoxName.Items.Add("2F_InputOutputControlByIdentifier");
            comboBoxName.Items.Add("31_RoutineControlCrc");
            comboBoxName.Items.Add("31_RoutineControl");
            comboBoxName.Items.Add("31_RoutineControlRSA");
            comboBoxName.Items.Add("34_RequestDownload");
            comboBoxName.Items.Add("36_TransferData");
            comboBoxName.Items.Add("37_TransferExit");
            comboBoxName.Items.Add("35_RequestUpload");
            comboBoxName.Items.Add("3D_WriteMemoryByAddress");
            comboBoxName.Items.Add("83_AccessTimingParameter");
            comboBoxName.Items.Add("84_SecuredDataTransmission");
            comboBoxName.Items.Add("86_ResponseOnEvent");
            comboBoxName.Items.Add("87_LinkControl");
            comboBoxName.Items.Add("85_ControlDTCSetting");
            comboBoxName.SelectedIndex = 0;

            comboBox_Physical.Items.Clear();
            comboBox_Physical.Items.Add("True");
            comboBox_Physical.Items.Add("False");
            comboBox_Physical.SelectedIndex = 0;

            comboBox_Inhibittion.Items.Clear();
            comboBox_Inhibittion.Items.Add("True");
            comboBox_Inhibittion.Items.Add("False");
            comboBox_Inhibittion.SelectedIndex = 0;

            comboBox_Enable.Items.Clear();
            comboBox_Enable.Items.Add("True");
            comboBox_Enable.Items.Add("False");
            comboBox_Enable.SelectedIndex = 0;
        }
        private void Custom_Add_Load(object sender, EventArgs e)
        {

        }

        public void ChangeValue(Func<string, int> MethodName, List<string> strings)
        {
            InitLoad();
            for (int i = 0; i < comboBoxName.Items.Count; i++)
            {
                if (comboBoxName.Items[i].Equals(strings[0]))
                {
                    comboBoxName.SelectedIndex = i; break;
                }
            }
            for (int i = 0; i < comboBox_Physical.Items.Count; i++)
            {
                if (comboBox_Physical.Items[i].Equals(strings[1]))
                {
                    comboBox_Physical.SelectedIndex = i; break;
                }
            }
            for (int i = 0; i < comboBox_Inhibittion.Items.Count; i++)
            {
                if (comboBox_Inhibittion.Items[i].Equals(strings[2]))
                {
                    comboBox_Inhibittion.SelectedIndex = i; break;
                }
            }
            textBoxRequest.Text = strings[3];
            textBoxRespose.Text = strings[4];
            textBoxDelay.Text = strings[5];
            for (int i = 0; i < comboBox_Enable.Items.Count; i++)
            {
                if (comboBox_Enable.Items[i].Equals(strings[6]))
                {
                    comboBox_Enable.SelectedIndex = i; break;
                }
            }
            openType = OpenType.Change;
            this.func = MethodName;
            this.ShowDialog();
        }
        public void GetAddValue(Func<string, int> MethodName)
        {
            InitLoad();
            openType = OpenType.Add;
            this.func = MethodName;
            this.ShowDialog();
        }

        private void button1_Click(object sender, EventArgs e) /* OK */
        {
            string str = "";

            str += comboBoxName.Text + "###";
            str += comboBox_Physical.Text + "###";
            str += comboBox_Inhibittion.Text + "###";
            str += textBoxRequest.Text + "###";
            str += textBoxRespose.Text + "###";
            str += textBoxDelay.Text + "###";
            str += comboBox_Enable.Text;
            func(str);
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
