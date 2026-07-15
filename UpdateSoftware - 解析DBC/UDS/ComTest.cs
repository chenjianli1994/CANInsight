using CSScriptLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;

namespace PCAN_Client.UDS
{
    public partial class ComTest : Form
    {        
        // 修改AppConfig类，包含完整的DBC数据
        [Serializable]
        public class AppConfigComTest
        {
            public Dictionary<string, int> RxSelectList { get; set; }
        }

        private Dictionary<string, ComboBox> Rx0List = new Dictionary<string, ComboBox>();
        private Dictionary<string, int> Rx0SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx1List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx1TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx1SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx2List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx2TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx2SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx3List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx3TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx3SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx4List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx4TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx4SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx5List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx5TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx5SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx6List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx6TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx6SelectList = new Dictionary<string, int>();

        private Dictionary<string, ComboBox> Rx7List = new Dictionary<string, ComboBox>();
        private Dictionary<string, TextBox> Rx7TextBoxList = new Dictionary<string, TextBox>();
        private Dictionary<string, int> Rx7SelectList = new Dictionary<string, int>();

        private int HMI_MinTemp = 18;
        private int HMI_MaxTemp = 32; 
        
        private bool isInitializing = false; // 添加初始化标志
        private bool isSetting = false;

        public ComTest()
        {
            InitializeComponent();
        }

        private void SetCmdToSignal(string CAN_IDStr, string signalName, int value, ref Dictionary<string, int> RxSelectList)
        {
            RxSelectList[signalName] = value;
            try
            {
                uint CAN_ID = uint.Parse(CAN_IDStr, System.Globalization.NumberStyles.HexNumber);
                CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
                if (null != message)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == signalName);
                    if (null != signal)
                    {
                        signal.cmdValue = value;
                        message.updateFlag = true;
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            /* RX0 */
            var config = new AppConfigComTest
            {
                RxSelectList = Rx0SelectList
            };
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx0_Save = json;

            /* RX1 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx1SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx1_Save = json;

            /* RX2 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx2SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx2_Save = json;

            /* RX3 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx3SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx3_Save = json;

            /* RX4 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx4SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx4_Save = json;

            /* RX5 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx5SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx5_Save = json;

            /* RX6 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx6SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx6_Save = json;

            /* RX7 */
            config = new AppConfigComTest
            {
                RxSelectList = Rx7SelectList
            };
            json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Properties.Settings.Default.TestRx7_Save = json;

            Properties.Settings.Default.Save();
        }

        private void mapInit()
        {
            /* Rx0 Init */
            Rx0List.Add("Test_Function_switch", comboBox_Test_Function_switch);
            Rx0List.Add("Test_RecySta", comboBox_Test_RecySta);
            Rx0List.Add("Test_HVAC_Sta", comboBox_Test_HVAC_Sta);
            Rx0List.Add("Test_HVAC_Sta_Rear", comboBox_Test_HVAC_Sta_Rear);
            Rx0List.Add("Test_BlowSta", comboBox_Test_BlowSta);
            Rx0List.Add("Test_BlowValue", comboBox_Test_BlowValue);
            Rx0List.Add("Test_BlowStaR", comboBox_Test_BlowStaR);
            Rx0List.Add("Test_BlowValueR", comboBox_Test_BlowValueR);
            Rx0List.Add("Test_ModeSta", comboBox_Test_ModeSta);
            Rx0List.Add("Test_ModeValue", comboBox_Test_ModeValue);
            Rx0List.Add("Test_ModeStaR", comboBox_Test_ModeStaR);
            Rx0List.Add("Test_ModeValueR", comboBox_Test_ModeValueR);
            Rx0List.Add("Test_DrTemSt", comboBox_Test_DrTemSt);
            Rx0List.Add("Test_PaTemSt", comboBox_Test_PaTemSt);
            Rx0List.Add("Test_RearTempSt", comboBox_Test_RearTempSt);
            if (Properties.Settings.Default.TestRx0_Save.Equals("Default"))
            {
                foreach (var item in Rx0List)
                {
                    Rx0SelectList.Add(item.Key, 0);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx0_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx0SelectList = config.RxSelectList;
            }

            /* Rx1 Init */
            Rx1List.Add("Test_PID_switch", comboBox_Test_PID_switch);
            Rx1List.Add("Test_Comp_PID_switch", comboBox_Test_Comp_PID_switch);
            Rx1List.Add("Test_EXV_PID_switch", comboBox_Test_EXV_PID_switch);
            Rx1List.Add("Test_ACCDV_PID_switch", comboBox_Test_ACCDV_PID_switch);
            Rx1List.Add("Test_Flap_PID_switch", comboBox_Test_Flap_PID_switch);
            Rx1List.Add("Test_HVH_PID_switch", comboBox_Test_HVH_PID_switch);

            Rx1TextBoxList.Add("Test_Comp_P", textBox_Test_Comp_P);
            Rx1TextBoxList.Add("Test_Comp_I", textBox_Test_Comp_I);
            Rx1TextBoxList.Add("Test_Comp_SV", textBox_Test_Comp_SV);
            Rx1TextBoxList.Add("Test_EXV_P", textBox_Test_EXV_P);
            Rx1TextBoxList.Add("Test_EXV_I", textBox_Test_EXV_I);
            Rx1TextBoxList.Add("Test_EXV_SV", textBox_Test_EXV_SV);
            if (Properties.Settings.Default.TestRx1_Save.Equals("Default"))
            {
                foreach (var item in Rx1List)
                {
                    Rx1SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx1TextBoxList)
                {
                    Rx1SelectList.Add(item.Key, 0);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx1_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx1SelectList = config.RxSelectList;
                Rx1SelectList["Test_Comp_P"] = 0;
                Rx1SelectList["Test_Comp_I"] = 0;
                Rx1SelectList["Test_Comp_SV"] = 0;
                Rx1SelectList["Test_EXV_P"] = 0;
                Rx1SelectList["Test_EXV_I"] = 0;
                Rx1SelectList["Test_EXV_SV"] = 0;
            }

            /* Rx2 Init */
            Rx2TextBoxList.Add("Test_ACCDV_P", textBox_Test_ACCDV_P);
            Rx2TextBoxList.Add("Test_ACCDV_I", textBox_Test_ACCDV_I);
            Rx2TextBoxList.Add("Test_ACCDV_SV", textBox_Test_ACCDV_SV);
            Rx2TextBoxList.Add("Test_Flap_P", textBox_Test_Flap_P);
            Rx2TextBoxList.Add("Test_Flap_I", textBox_Test_Flap_I);
            Rx2TextBoxList.Add("Test_Flap_SV", textBox_Test_Flap_SV);
            Rx2TextBoxList.Add("Test_HVH_P", textBox_Test_HVH_P);
            Rx2TextBoxList.Add("Test_HVH_I", textBox_Test_HVH_I);
            Rx2TextBoxList.Add("Test_HVH_SV", textBox_Test_HVH_SV);
            if (Properties.Settings.Default.TestRx2_Save.Equals("Default"))
            {
                foreach (var item in Rx2List)
                {
                    Rx2SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx2TextBoxList)
                {
                    Rx2SelectList.Add(item.Key, 0);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx2_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx2SelectList = config.RxSelectList;
                Rx2SelectList["Test_ACCDV_P"] = 0;
                Rx2SelectList["Test_ACCDV_I"] = 0;
                Rx2SelectList["Test_ACCDV_SV"] = 0;
                Rx2SelectList["Test_Flap_P"] = 0;
                Rx2SelectList["Test_Flap_I"] = 0;
                Rx2SelectList["Test_Flap_SV"] = 0;
                Rx2SelectList["Test_HVH_P"] = 0;
                Rx2SelectList["Test_HVH_I"] = 0;
                Rx2SelectList["Test_HVH_SV"] = 0;
            }

            /* Rx3 Init */
            Rx3List.Add("Test_Switch", comboBox_Test_Switch);
            Rx3List.Add("Test_APTC_en", comboBox_Test_APTC_en);
            Rx3List.Add("Test_SOV1Pos", comboBox_Test_SOV1Pos);
            Rx3List.Add("Test_SOV2VPos", comboBox_Test_SOV2VPos);
            Rx3List.Add("Test_SOV3VPos", comboBox_Test_SOV3VPos);
            Rx3List.Add("Test_SO_TXVVPos", comboBox_Test_SO_TXVVPos);

            Rx3TextBoxList.Add("Test_APTC_pwr", textBox_Test_APTC_pwr);
            Rx3TextBoxList.Add("Test_RMCUPumpSpdRatioReq", textBox_Test_RMCUPumpSpdRatioReq);
            Rx3TextBoxList.Add("Test_RACCTempFlap", textBox_Test_RACCTempFlap);
            if (Properties.Settings.Default.TestRx3_Save.Equals("Default"))
            {
                foreach (var item in Rx3List)
                {
                    Rx3SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx3TextBoxList)
                {
                    Rx3SelectList.Add(item.Key, -1);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx3_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx3SelectList = config.RxSelectList;
                Rx3SelectList["Test_APTC_pwr"] = -1;
                Rx3SelectList["Test_RMCUPumpSpdRatioReq"] = -1;
                Rx3SelectList["Test_RACCTempFlap"] = -1;
            }

            /* Rx4 Init */
            Rx4TextBoxList.Add("Test_InCarSensorCorrA", textBox_Test_InCarSensorCorrA);
            Rx4TextBoxList.Add("Test_InCarSensorCorrB", textBox_Test_InCarSensorCorrB);
            Rx4TextBoxList.Add("Test_InCarSensorCorrC", textBox_Test_InCarSensorCorrC);
            Rx4TextBoxList.Add("Test_QAmbPwrCoefA", textBox_Test_QAmbPwrCoefA);
            Rx4TextBoxList.Add("Test_QAmbPwrCoefB", textBox_Test_QAmbPwrCoefB);
            Rx4TextBoxList.Add("Test_Qlost_integral", textBox_Test_Qlost_integral);
            Rx4TextBoxList.Add("Test_Qlosttran_Afa", textBox_Test_Qlosttran_Afa);
            Rx4TextBoxList.Add("Test_Qlosttran_Const", textBox_Test_Qlosttran_Const);
            if (Properties.Settings.Default.TestRx4_Save.Equals("Default"))
            {
                foreach (var item in Rx4List)
                {
                    Rx4SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx4TextBoxList)
                {
                    Rx4SelectList.Add(item.Key, 0);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx4_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx4SelectList = config.RxSelectList;
                Rx4SelectList["Test_InCarSensorCorrA"] = 0;
                Rx4SelectList["Test_InCarSensorCorrB"] = 0;
                Rx4SelectList["Test_InCarSensorCorrC"] = 0;
                Rx4SelectList["Test_QAmbPwrCoefA"] = 0;
                Rx4SelectList["Test_QAmbPwrCoefB"] = 0;
                Rx4SelectList["Test_Qlost_integral"] = 0;
                Rx4SelectList["Test_Qlosttran_Afa"] = 0;
                Rx4SelectList["Test_Qlosttran_Const"] = 0;
            }

            /* Rx5 Init */
            Rx5TextBoxList.Add("Test_AfaFECONTEST", textBox_Test_AfaFECONTEST);
            Rx5TextBoxList.Add("Test_QACModeCoolToRH", textBox_Test_QACModeCoolToRH);
            Rx5TextBoxList.Add("Test_QACModeRHTOHEAT", textBox_Test_QACModeRHTOHEAT);
            Rx5TextBoxList.Add("Test_QlossssAirCorrA", textBox_Test_QlossssAirCorrA);
            Rx5TextBoxList.Add("Test_QlossssAirCorrB", textBox_Test_QlossssAirCorrB);
            Rx5TextBoxList.Add("Test_QlosstranAirCorrA", textBox_Test_QlosstranAirCorrA);
            Rx5TextBoxList.Add("Test_QlosstranAirCorrB", textBox_Test_QlosstranAirCorrB);
            Rx5TextBoxList.Add("Test_QsunCoef", textBox_Test_QsunCoef);
            if (Properties.Settings.Default.TestRx5_Save.Equals("Default"))
            {
                foreach (var item in Rx5List)
                {
                    Rx5SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx5TextBoxList)
                {
                    Rx5SelectList.Add(item.Key, 0);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx5_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx5SelectList = config.RxSelectList;
                Rx5SelectList["Test_AfaFECONTEST"] = 0;
                Rx5SelectList["Test_QACModeCoolToRH"] = 0;
                Rx5SelectList["Test_QACModeRHTOHEAT"] = 0;
                Rx5SelectList["Test_QlossssAirCorrA"] = 0;
                Rx5SelectList["Test_QlossssAirCorrB"] = 0;
                Rx5SelectList["Test_QlosstranAirCorrA"] = 0;
                Rx5SelectList["Test_QlosstranAirCorrB"] = 0;
                Rx5SelectList["Test_QsunCoef"] = 0;
            }

            /* Rx6 Init */
            Rx6List.Add("Test_CompEn", comboBox_Test_CompEn);
            Rx6List.Add("Test_HVH_en", comboBox_Test_HVH_en);
            Rx6List.Add("Test_C5WVPos", comboBox_Test_C5WVPos);

            Rx6TextBoxList.Add("Test_CompSpd", textBox_Test_CompSpd);
            Rx6TextBoxList.Add("Test_HVH_pwr", textBox_Test_HVH_pwr);
            Rx6TextBoxList.Add("Test_DrTMCTempFlap", textBox_Test_DrTMCTempFlap);
            Rx6TextBoxList.Add("Test_PaTMCTempFlap", textBox_Test_PaTMCTempFlap);
            Rx6TextBoxList.Add("Test_SpdFanPwm", textBox_Test_SpdFanPwm);
            Rx6TextBoxList.Add("Test_AC_CDVPos", textBox_Test_AC_CDVPos);
            if (Properties.Settings.Default.TestRx6_Save.Equals("Default"))
            {
                foreach (var item in Rx6List)
                {
                    Rx6SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx6TextBoxList)
                {
                    Rx6SelectList.Add(item.Key, -1);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx6_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx6SelectList = config.RxSelectList;
                Rx6SelectList["Test_CompSpd"] = -1;
                Rx6SelectList["Test_HVH_pwr"] = -1;
                Rx6SelectList["Test_DrTMCTempFlap"] = -1;
                Rx6SelectList["Test_PaTMCTempFlap"] = -1;
                Rx6SelectList["Test_SpdFanPwm"] = -1;
                Rx6SelectList["Test_AC_CDVPos"] = -1;
            }

            /* Rx7 Init */
            Rx7List.Add("Test_AGS_Pos", comboBox_Test_AGS_Pos);

            Rx7TextBoxList.Add("Test_AcEXVPos", textBox_Test_AcEXVPos);
            Rx7TextBoxList.Add("Test_BatEXVPos", textBox_Test_BatEXVPos);
            Rx7TextBoxList.Add("Test_EXV_HPos", textBox_Test_EXV_HPos);
            Rx7TextBoxList.Add("Test_AcPumpSpdRatioReq", textBox_Test_AcPumpSpdRatioReq);
            Rx7TextBoxList.Add("Test_BatPumpSpdRatioReq", textBox_Test_BatPumpSpdRatioReq);
            Rx7TextBoxList.Add("Test_RecFlap_Pos", textBox_Test_RecFlap_Pos);
            Rx7TextBoxList.Add("Test_FMCUPumpSpdRatioReq", textBox_Test_FMCUPumpSpdRatioReq);
            if (Properties.Settings.Default.TestRx7_Save.Equals("Default"))
            {
                foreach (var item in Rx7List)
                {
                    Rx7SelectList.Add(item.Key, 0);
                }
                foreach (var item in Rx7TextBoxList)
                {
                    Rx7SelectList.Add(item.Key, -1);
                }
            }
            else
            {
                AppConfigComTest config = JsonConvert.DeserializeObject<AppConfigComTest>(Properties.Settings.Default.TestRx7_Save,
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    TypeNameHandling = TypeNameHandling.Auto
                });
                Rx7SelectList = config.RxSelectList;
                Rx7SelectList["Test_AcEXVPos"] = -1;
                Rx7SelectList["Test_BatEXVPos"] = -1;
                Rx7SelectList["Test_EXV_HPos"] = -1;
                Rx7SelectList["Test_AcPumpSpdRatioReq"] = -1;
                Rx7SelectList["Test_BatPumpSpdRatioReq"] = -1;
                Rx7SelectList["Test_RecFlap_Pos"] = -1;
                Rx7SelectList["Test_FMCUPumpSpdRatioReq"] = -1;
            }

            Rx0SelectList["Test_Function_switch"] = 0;
            Rx6SelectList["Test_Switch"] = 0;
        }


        private void ComTest_Load(object sender, EventArgs e)
        {
            isInitializing = true; // 开始初始化
            mapInit();
            Rx0Init();
            Rx1Init();
            Rx2Init();
            Rx3Init(); 
            Rx4Init();
            Rx5Init();
            Rx6Init();
            Rx7Init(); 
            isInitializing = false; // 初始化结束
        }
        private void ComTest_FormClosed(object sender, FormClosedEventArgs e)
        {
            /* 关闭时自动关闭强控 */
            SetCmdToSignal(textBox_RX0ID.Text, "Test_Function_switch", 0, ref Rx0SelectList);
            SetCmdToSignal(textBox_RX3ID.Text, "Test_Switch", 0, ref Rx3SelectList);
            SaveConfig();
        }
        /* RX0 */
        private void Rx0Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX0ID.Text, System.Globalization.NumberStyles.HexNumber);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage0");
            var settings = Properties.Settings.Default;
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX0ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx0List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx0SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
                comboBox_Test_DrTemSt.Items.Clear();
                comboBox_Test_PaTemSt.Items.Clear();
                comboBox_Test_RearTempSt.Items.Clear();
                for (int temp = HMI_MinTemp; temp <= HMI_MaxTemp; temp++)
                {
                    comboBox_Test_DrTemSt.Items.Add(temp);
                    comboBox_Test_PaTemSt.Items.Add(temp);
                    comboBox_Test_RearTempSt.Items.Add(temp);
                }

                Rx0SelectList.TryGetValue("Test_DrTemSt", out selectedIndex);
                if(0 <= selectedIndex - HMI_MinTemp)
                {
                    comboBox_Test_DrTemSt.SelectedIndex = selectedIndex - HMI_MinTemp;
                }
                else
                {
                    comboBox_Test_DrTemSt.SelectedIndex = 0;
                }

                Rx0SelectList.TryGetValue("Test_PaTemSt", out selectedIndex);
                if (0 <= selectedIndex - HMI_MinTemp)
                {
                    comboBox_Test_PaTemSt.SelectedIndex = selectedIndex - HMI_MinTemp;
                }
                else
                {
                    comboBox_Test_PaTemSt.SelectedIndex = 0;
                }

                Rx0SelectList.TryGetValue("Test_RearTempSt", out selectedIndex);
                if (0 <= selectedIndex - HMI_MinTemp)
                {
                    comboBox_Test_RearTempSt.SelectedIndex = selectedIndex - HMI_MinTemp;
                }
                else
                {
                    comboBox_Test_RearTempSt.SelectedIndex = 0;
                }
            }
            else
            {
                textBox_RX0ID.Text = "未找到测试帧";
            }
        }
        private void textBox_RX0ID_Click(object sender, EventArgs e)
        {
            Rx0Init();
        }

        private void comboBox_Test_Function_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_Function_switch", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_RecySta_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_RecySta", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_HVAC_Sta_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_HVAC_Sta", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_HVAC_Sta_Rear_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_HVAC_Sta_Rear", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_BlowSta_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_BlowSta", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_BlowValue_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_BlowValue", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_BlowStaR_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_BlowStaR", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_BlowValueR_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_BlowValueR", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_ModeSta_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_ModeSta", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_ModeValue_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_ModeValue", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_ModeStaR_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_ModeStaR", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_ModeValueR_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_ModeValueR", comboBox.SelectedIndex, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_DrTemSt_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_DrTemSt", comboBox.SelectedIndex + HMI_MinTemp, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_PaTemSt_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_PaTemSt", comboBox.SelectedIndex + HMI_MinTemp, ref Rx0SelectList);
            }
        }

        private void comboBox_Test_RearTempSt_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX0ID.Text, "Test_RearTempSt", comboBox.SelectedIndex + HMI_MinTemp, ref Rx0SelectList);
            }
        }

        /* RX1 */
        private void Rx1Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX1ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage1");
            var settings = Properties.Settings.Default;
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX1ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx1List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx1SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx1SelectList.TryGetValue("Test_Comp_P", out tempValue);
                textBox_Test_Comp_P.Text = tempValue.ToString();

                Rx1SelectList.TryGetValue("Test_Comp_I", out tempValue);
                textBox_Test_Comp_I.Text = tempValue.ToString();

                Rx1SelectList.TryGetValue("Test_Comp_SV", out tempValue);
                textBox_Test_Comp_SV.Text = tempValue.ToString();

                Rx1SelectList.TryGetValue("Test_EXV_P", out tempValue);
                textBox_Test_EXV_P.Text = tempValue.ToString();

                Rx1SelectList.TryGetValue("Test_EXV_I", out tempValue);
                textBox_Test_EXV_I.Text = tempValue.ToString();

                Rx1SelectList.TryGetValue("Test_EXV_SV", out tempValue);
                textBox_Test_EXV_SV.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX1ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX1ID_Click(object sender, EventArgs e)
        {
            Rx1Init();
        }
        private void comboBox_Test_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        private void comboBox_Test_Comp_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        private void textBox_Test_Comp_P_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_P", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Comp_P_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_P", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_Comp_I_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_I", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Comp_I_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_I", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Comp_SV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_SV", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Comp_SV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_Comp_SV", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_EXV_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        private void textBox_Test_EXV_P_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_P", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_EXV_P_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_P", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_EXV_I_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_I", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_EXV_I_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_I", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_EXV_SV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_SV", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }

        private void textBox_Test_EXV_SV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX1ID.Text, "Test_EXV_SV", value, ref Rx1SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_ACCDV_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_ACCDV_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        private void comboBox_Test_Flap_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_Flap_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        private void comboBox_Test_HVH_PID_switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX1ID.Text, "Test_HVH_PID_switch", comboBox.SelectedIndex, ref Rx1SelectList);
            }
        }

        /* RX2 */
        private void Rx2Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX2ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage2");
            var settings = Properties.Settings.Default;
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX2ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx2List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx2SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx2SelectList.TryGetValue("Test_ACCDV_P", out tempValue);
                textBox_Test_ACCDV_P.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_ACCDV_I", out tempValue);
                textBox_Test_ACCDV_I.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_ACCDV_SV", out tempValue);
                textBox_Test_ACCDV_SV.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_Flap_P", out tempValue);
                textBox_Test_Flap_P.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_Flap_I", out tempValue);
                textBox_Test_Flap_I.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_Flap_SV", out tempValue);
                textBox_Test_Flap_SV.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_HVH_P", out tempValue);
                textBox_Test_HVH_P.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_HVH_I", out tempValue);
                textBox_Test_HVH_I.Text = tempValue.ToString();

                Rx2SelectList.TryGetValue("Test_HVH_SV", out tempValue);
                textBox_Test_HVH_SV.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX2ID.Text = "未找到测试帧";
            }
        }
        private void textBox_RX2ID_Click(object sender, EventArgs e)
        {
            Rx2Init();
        }
        private void textBox_Test_ACCDV_P_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_ACCDV_P_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_ACCDV_I_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_ACCDV_I_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_ACCDV_SV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_ACCDV_SV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_ACCDV_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Flap_P_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Flap_P_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Flap_I_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Flap_I_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_Flap_SV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Flap_SV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_Flap_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_HVH_P_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }

        private void textBox_Test_HVH_P_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_P", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }

        }

        private void textBox_Test_HVH_I_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_HVH_I_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_I", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_HVH_SV_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_HVH_SV_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX2ID.Text, "Test_HVH_SV", value, ref Rx2SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        /* RX3 */
        private void Rx3Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX3ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage3");
            var settings = Properties.Settings.Default;
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX3ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx3List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx3SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx3SelectList.TryGetValue("Test_APTC_pwr", out tempValue);
                textBox_Test_APTC_pwr.Text = tempValue.ToString();

                Rx3SelectList.TryGetValue("Test_RMCUPumpSpdRatioReq", out tempValue);
                textBox_Test_RMCUPumpSpdRatioReq.Text = tempValue.ToString();

                Rx3SelectList.TryGetValue("Test_RACCTempFlap", out tempValue);
                textBox_Test_RACCTempFlap.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX3ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX3ID_Click(object sender, EventArgs e)
        {
            Rx3Init();
        }

        private void comboBox_Test_Switch_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_Switch", comboBox.SelectedIndex, ref Rx3SelectList);
            }
        }

        private void comboBox_Test_APTC_en_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_APTC_en", comboBox.SelectedIndex, ref Rx3SelectList);
                if (0 == comboBox.SelectedIndex)
                {
                    textBox_Test_APTC_pwr.Text = "0";
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_APTC_pwr", 0, ref Rx3SelectList);
                }
            }
        }

        private void textBox_Test_APTC_pwr_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_APTC_pwr", value, ref Rx3SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_APTC_pwr_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_APTC_pwr", value, ref Rx3SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_SOV1Pos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_SOV1Pos", comboBox.SelectedIndex, ref Rx3SelectList);
            }
        }

        private void comboBox_Test_SOV2VPos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_SOV2VPos", comboBox.SelectedIndex, ref Rx3SelectList);
            }
        }

        private void comboBox_Test_SOV3VPos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_SOV3VPos", comboBox.SelectedIndex, ref Rx3SelectList);
            }
        }

        private void comboBox_Test_SO_TXVVPos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX3ID.Text, "Test_SO_TXVVPos", comboBox.SelectedIndex, ref Rx3SelectList);
            }
        }


        private void textBox_Test_RMCUPumpSpdRatioReq_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_RMCUPumpSpdRatioReq", value, ref Rx3SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_RMCUPumpSpdRatioReq_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_RMCUPumpSpdRatioReq", value, ref Rx3SelectList); 
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_RACCTempFlap_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_RACCTempFlap", value, ref Rx3SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_RACCTempFlap_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX3ID.Text, "Test_RACCTempFlap", value, ref Rx3SelectList); 
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void Rx4Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX4ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage4");
            var settings = Properties.Settings.Default;
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX4ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx4List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx4SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx4SelectList.TryGetValue("Test_InCarSensorCorrA", out tempValue);
                textBox_Test_InCarSensorCorrA.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_InCarSensorCorrB", out tempValue);
                textBox_Test_InCarSensorCorrB.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_InCarSensorCorrC", out tempValue);
                textBox_Test_InCarSensorCorrC.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_QAmbPwrCoefA", out tempValue);
                textBox_Test_QAmbPwrCoefA.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_QAmbPwrCoefB", out tempValue);
                textBox_Test_QAmbPwrCoefB.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_Qlost_integral", out tempValue);
                textBox_Test_Qlost_integral.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_Qlosttran_Afa", out tempValue);
                textBox_Test_Qlosttran_Afa.Text = tempValue.ToString();

                Rx4SelectList.TryGetValue("Test_Qlosttran_Const", out tempValue);
                textBox_Test_Qlosttran_Const.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX4ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX4ID_Click(object sender, EventArgs e)
        {
            Rx4Init();
        }
        private void textBox_Test_InCarSensorCorrA_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrA", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_InCarSensorCorrA_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrA", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_InCarSensorCorrB_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrB", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_InCarSensorCorrB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrB", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_InCarSensorCorrC_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrC", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_InCarSensorCorrC_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_InCarSensorCorrC", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QAmbPwrCoefA_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_QAmbPwrCoefA", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QAmbPwrCoefA_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_QAmbPwrCoefA", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QAmbPwrCoefB_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_QAmbPwrCoefB", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QAmbPwrCoefB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_QAmbPwrCoefB", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Qlost_integral_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlost_integral", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Qlost_integral_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlost_integral", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Qlosttran_Afa_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlosttran_Afa", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Qlosttran_Afa_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlosttran_Afa", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_Qlosttran_Const_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlosttran_Const", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_Qlosttran_Const_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX4ID.Text, "Test_Qlosttran_Const", value, ref Rx4SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }


        /* RX5 */
        private void Rx5Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX5ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage5");
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX5ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx5List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx5SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx5SelectList.TryGetValue("Test_AfaFECONTEST", out tempValue);
                textBox_Test_AfaFECONTEST.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QACModeCoolToRH", out tempValue);
                textBox_Test_QACModeCoolToRH.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QACModeRHTOHEAT", out tempValue);
                textBox_Test_QACModeRHTOHEAT.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QlossssAirCorrA", out tempValue);
                textBox_Test_QlossssAirCorrA.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QlossssAirCorrB", out tempValue);
                textBox_Test_QlossssAirCorrB.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QlosstranAirCorrA", out tempValue);
                textBox_Test_QlosstranAirCorrA.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QlosstranAirCorrB", out tempValue);
                textBox_Test_QlosstranAirCorrB.Text = tempValue.ToString();

                Rx5SelectList.TryGetValue("Test_QsunCoef", out tempValue);
                textBox_Test_QsunCoef.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX5ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX5ID_Click(object sender, EventArgs e)
        {
            Rx5Init();
        }
        private void textBox_Test_AfaFECONTEST_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_AfaFECONTEST", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_AfaFECONTEST_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_AfaFECONTEST", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QACModeCoolToRH_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QACModeCoolToRH", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QACModeCoolToRH_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QACModeCoolToRH", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QACModeRHTOHEAT_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QACModeRHTOHEAT", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QACModeRHTOHEAT_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QACModeRHTOHEAT", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QlossssAirCorrA_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlossssAirCorrA", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QlossssAirCorrA_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlossssAirCorrA", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }

        }

        private void textBox_Test_QlossssAirCorrB_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlossssAirCorrB", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QlossssAirCorrB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlossssAirCorrB", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QlosstranAirCorrA_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlosstranAirCorrA", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QlosstranAirCorrA_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlosstranAirCorrA", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QlosstranAirCorrB_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlosstranAirCorrB", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QlosstranAirCorrB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QlosstranAirCorrB", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_QsunCoef_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QsunCoef", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_QsunCoef_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX5ID.Text, "Test_QsunCoef", value, ref Rx5SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        /* RX6 */
        private void Rx6Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX6ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage6");
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX6ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx6List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx6SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx6SelectList.TryGetValue("Test_CompSpd", out tempValue);
                textBox_Test_CompSpd.Text = tempValue.ToString();

                Rx6SelectList.TryGetValue("Test_HVH_pwr", out tempValue);
                textBox_Test_HVH_pwr.Text = tempValue.ToString();

                Rx6SelectList.TryGetValue("Test_DrTMCTempFlap", out tempValue);
                textBox_Test_DrTMCTempFlap.Text = tempValue.ToString();

                Rx6SelectList.TryGetValue("Test_PaTMCTempFlap", out tempValue);
                textBox_Test_PaTMCTempFlap.Text = tempValue.ToString();

                Rx6SelectList.TryGetValue("Test_SpdFanPwm", out tempValue);
                textBox_Test_SpdFanPwm.Text = tempValue.ToString();

                Rx6SelectList.TryGetValue("Test_AC_CDVPos", out tempValue);
                textBox_Test_AC_CDVPos.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX6ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX6ID_Click(object sender, EventArgs e)
        {
            Rx6Init();
        }

        private void comboBox_Test_CompEn_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX6ID.Text, "Test_CompEn", comboBox.SelectedIndex, ref Rx6SelectList);
                if(0 == comboBox.SelectedIndex)
                {
                    isSetting = true;
                    textBox_Test_CompSpd.Text = "-1";
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_CompSpd", -1, ref Rx6SelectList);
                    isSetting = false;
                }
            }
        }

        private void textBox_Test_CompSpd_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing|| isSetting)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_CompSpd", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_CompSpd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_CompSpd", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_HVH_en_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX6ID.Text, "Test_HVH_en", comboBox.SelectedIndex, ref Rx6SelectList);
                if(0 == comboBox.SelectedIndex)
                {
                    isSetting = true;
                    textBox_Test_HVH_pwr.Text = "-1";
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_HVH_pwr", -1, ref Rx6SelectList);
                    isSetting = false;
                }
            }
        }
        private void textBox_Test_HVH_pwr_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing|| isSetting)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_HVH_pwr", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_HVH_pwr_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_HVH_pwr", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_DrTMCTempFlap_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_DrTMCTempFlap", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_DrTMCTempFlap_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_DrTMCTempFlap", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_PaTMCTempFlap_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_PaTMCTempFlap", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_PaTMCTempFlap_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_PaTMCTempFlap", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_SpdFanPwm_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_SpdFanPwm", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_SpdFanPwm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_SpdFanPwm", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_AC_CDVPos_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_AC_CDVPos", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_AC_CDVPos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX6ID.Text, "Test_AC_CDVPos", value, ref Rx6SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_C5WVPos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX6ID.Text, "Test_C5WVPos", comboBox.SelectedIndex, ref Rx6SelectList);
            }
        }


        /* RX7 */
        private void Rx7Init()
        {
            //uint CAN_ID = uint.Parse(textBox_RX7ID.Text, System.Globalization.NumberStyles.HexNumber);
            //CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageById(CAN_ID);
            CAN_Data.Message message = BaseParamter.dbcHelper.GetMessageByMsgName("Test_RxMessage7");
            List<string> enumDefinitions = new List<string>();
            int selectedIndex = 0;

            if (null != message)
            {
                textBox_RX7ID.Text = message.messgeId.ToString("X2");
                foreach (var map in Rx7List)
                {
                    Signal signal = message.signals.FirstOrDefault(m => m.signalName == map.Key);
                    if (null != signal && null != signal.enumDefinitions)
                    {
                        enumDefinitions.Clear();
                        foreach (var kv in signal.enumDefinitions)
                        {
                            enumDefinitions.Add(kv.Value);
                        }
                        enumDefinitions.Reverse();

                        map.Value.Items.Clear();
                        foreach (var tmp in enumDefinitions)
                        {
                            map.Value.Items.Add(tmp);
                        }

                        if (0 < map.Value.Items.Count)
                        {
                            selectedIndex = 0;
                            Rx7SelectList.TryGetValue(map.Key, out selectedIndex);
                            map.Value.SelectedIndex = selectedIndex;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                int tempValue = 0;
                Rx7SelectList.TryGetValue("Test_AcEXVPos", out tempValue);
                textBox_Test_AcEXVPos.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_BatEXVPos", out tempValue);
                textBox_Test_BatEXVPos.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_EXV_HPos", out tempValue);
                textBox_Test_EXV_HPos.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_AcPumpSpdRatioReq", out tempValue);
                textBox_Test_AcPumpSpdRatioReq.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_BatPumpSpdRatioReq", out tempValue);
                textBox_Test_BatPumpSpdRatioReq.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_RecFlap_Pos", out tempValue);
                textBox_Test_RecFlap_Pos.Text = tempValue.ToString();

                Rx7SelectList.TryGetValue("Test_FMCUPumpSpdRatioReq", out tempValue);
                textBox_Test_FMCUPumpSpdRatioReq.Text = tempValue.ToString();
            }
            else
            {
                textBox_RX7ID.Text = "未找到测试帧";
            }
        }

        private void textBox_RX7ID_Click(object sender, EventArgs e)
        {
            Rx7Init();
        }
        private void textBox_Test_AcEXVPos_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_AcEXVPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_AcEXVPos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_AcEXVPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_BatEXVPos_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_BatEXVPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_BatEXVPos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_BatEXVPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_EXV_HPos_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_EXV_HPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_EXV_HPos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_EXV_HPos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_AcPumpSpdRatioReq_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_AcPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_AcPumpSpdRatioReq_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_AcPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_BatPumpSpdRatioReq_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_BatPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_BatPumpSpdRatioReq_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_BatPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }
        private void textBox_Test_RecFlap_Pos_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_RecFlap_Pos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_RecFlap_Pos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_RecFlap_Pos", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void textBox_Test_FMCUPumpSpdRatioReq_TextChanged(object sender, EventArgs e)
        {
            if (isInitializing)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_FMCUPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    textBox.Focus();
                }
            }
            else
            {
                if (sender is TextBox textBox)
                {
                    textBox.BackColor = Color.Yellow;
                }
            }
        }
        private void textBox_Test_FMCUPumpSpdRatioReq_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (sender is TextBox textBox && !textBox.Text.Equals(""))
                {
                    int value;
                    if (!int.TryParse(textBox.Text, out value))
                    {
                        value = 0;
                    }
                    SetCmdToSignal(textBox_RX7ID.Text, "Test_FMCUPumpSpdRatioReq", value, ref Rx7SelectList);
                    textBox.BackColor = Color.White;
                    // 可选：让文本框失去焦点
                    textBox.Focus();
                }
            }
        }

        private void comboBox_Test_AGS_Pos_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem != null)
            {
                SetCmdToSignal(textBox_RX7ID.Text, "Test_AGS_Pos", comboBox.SelectedIndex, ref Rx7SelectList);
            }
        }


    }
}
