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
using static PCAN_Client.UDS.DataShow;

namespace PCAN_Client.UDS
{
    public partial class DataShow : Form
    {
        int SelectComponentNodesIndex = 0;

        bool LPFindFlag = false;
        bool HPFindFlag = false;
        int LPMsgIndex = 0;
        int LPSignalIndex = 0;
        int HPMsgIndex = 0;
        int HPSignalIndex = 0;


        public List<Component> ComponentNodes = new List<Component>()
        {
            new Component( systemSignalStatus,                       "系统状态"),
            new Component( BatSignalStatus,                          "电池(Bat)"),
            new Component( compSignalStatus,                         "压缩机(COMP)"),
            new Component( ptcSignalStatus,                          "加热器(PTC)"),
            new Component( hvacSignalStatus,                         "空调状态(HVAC)"),
            new Component( exvSignalStatus,                          "电子膨胀阀(EXV)"),
            new Component( pumpSignalStatus,                         "电子水泵(PUMP)"),
            new Component( fanSignalStatus,                          "电子风扇(FAN)"),
            new Component( ValVeSignalStatus,                        "多通阀(Valve)"),
            new Component( agsSignalStatus,                          "主动格栅(AGS)"),
            new Component( aqsPM2_5SignalStatus,                     "空气质量检测(AQS_PM2_5)"),
            new Component( HeatrefrigerantChargeSignalStatus,        "热泵模式充注量试验数据" ),
            new Component( SignalAcrefrigerantChargeSignalStatus,    "单制冷模式充注量试验数据" ),
            new Component( DoubleAcrefrigerantChargeSignalStatus,    "混合制冷模式充注量试验数据" ),
        };
        /* 系统状态 */
        public static List<SignalStatus> systemSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_CoolSysWorkMode",            "液侧系统工作模式",     false),
            new SignalStatus(0, 0, "_RefSysWorkMode",             "制冷剂侧系统工作模式", false),
            new SignalStatus(0, 0, "Test_output_Debug_ACWorkmode","前空调工作模式",       false),
            new SignalStatus(0, 0, "Test_out_Debug_e_u_MixWTCWo", "混合工作模式状态",     false),
            new SignalStatus(0, 0, "Test_output_Debug_IniInCarTe","车内初始温度",         false),
            new SignalStatus(0, 0, "Test_output_Debug_InCarCorrC","车内温度修正温差",     false),
            new SignalStatus(0, 0, "Test_e_u_ACworkmode_DehSta",  "温度是否稳定",         false),
            new SignalStatus(0, 0, "Test_output_Debug_Modify_Lsu","左阳光校正值",         false),
            new SignalStatus(0, 0, "Test_output_Debug_Modify_Rsu","右阳光校正值",         false),
            new SignalStatus(0, 0, "Test_output_Debug_Modify_inc","车内温度校正值",       false),
            new SignalStatus(0, 0, "Test_output_Debug_OHXDerfost","结冰状态",             false),
            
            /* 前空调 */
            new SignalStatus(0, 0, "前空调 ",                     "*******前空调******",  false),
            new SignalStatus(0, 0, "_ModeSts",                    "前空调开关",           false),
            new SignalStatus(0, 0, "_WorkModeStsFb",              "前空调工作状态",       false),
            new SignalStatus(0, 0, "_AutoACHeatSts",              "前空调自动冷暖风状态", false),
            new SignalStatus(0, 0, "Test_output_Debug_DATTarget", "前出风温度目标值",     false),
            new SignalStatus(0, 0, "Test_e_sw_AirT_PV",           "前出风温度实际值",     false),
            new SignalStatus(0, 0, "Test_out_Deb_Q_lost_total_Fr","前空调总能量",         false),
            new SignalStatus(0, 0, "Test_out_Debug_Q_lost_ss_Fr", "前空调稳态负荷",       false),
            new SignalStatus(0, 0, "Test_out_Debug_Q_lost_tran_F","前空调瞬态负荷",       false),
            new SignalStatus(0, 0, "Test_output_Debug_IdataPwr",  "前空积分能量",         false),
            new SignalStatus(0, 0, "Test_output_Debug_Blo_Massfl","前空调质量风量",       false),

            /* 后空调 */
            new SignalStatus(0, 0, "后空调 ",                     "*******后空调******",  false),
            new SignalStatus(0, 0, "_RearEnDisplay",              "后空调开关",           false),
            new SignalStatus(0, 0, "_RaccAutoACHeatSts",          "后空调自动冷暖风状态", false),
            new SignalStatus(0, 0, "Test_e_u_DATTargetRear",      "后空调风温目标值",     false),
            new SignalStatus(0, 0, "Test_e_u_Q_total_Rear",       "后空调总负荷",         false),
            new SignalStatus(0, 0, "Test_e_u_Q_ss_Rear",          "后空调稳态负荷",       false),
            new SignalStatus(0, 0, "Test_e_u_Q_tran_Rear",        "后空调瞬态负荷",       false),
            new SignalStatus(0, 0, "Test_e_w_Q_AmbPwr_Rear",      "后空调环境负荷能量",   false),
            new SignalStatus(0, 0, "Test_e_w_Q_IdataPwr_Rear",    "后空积分能量",         false),
            new SignalStatus(0, 0, "Test_e_u_Blo_Massflow_Rear",  "后空调质量风量",       false),
            
            /* 部件实际状态 */
            new SignalStatus(0, 0, "部件状态 ",                   "*****部件状态****",    false),
            new SignalStatus(0, 0, "CCM_Speed",                   "压缩机实际转速",       false),
            new SignalStatus(0, 0, "_FanSpdResp",                 "电子风扇反馈转速",     false),
            new SignalStatus(0, 0, "PTC_Power",                   "PTC 反馈功率",         false),
            new SignalStatus(0, 0, "_RAPTC_MsrdPwrFrt",           "后PTC功率反馈值",      false),
            new SignalStatus(0, 0, "_EXVPos_Ac",                  "AcEXV反馈位置",        false),
            new SignalStatus(0, 0, "_EXVPos_Bat",                 "BatEXV反馈位置",       false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_H_PV",          "EXCV控制实际值",       false),
            new SignalStatus(0, 0, "_AcPumpSpdResp",              "采暖水泵反馈值",       false),
            new SignalStatus(0, 0, "_BatPumpSpdResp",             "电池水泵反馈值",       false),
            new SignalStatus(0, 0, "MotPumpSpdResp",              "后电机水泵反馈值",     false),
            new SignalStatus(0, 0, "_CFCV_Pos",                   "采暖三通阀位置反馈",   false),
            new SignalStatus(0, 0, "_DSPBDCPosFB",                "DSP三通阀TMS反馈值",   false),
            new SignalStatus(0, 0, "_C5WV_Pos",                   "五通阀反馈位置" ,      false),
            new SignalStatus(0, 0, "_AGS_precent",                "AGS反馈位置",          false),
            new SignalStatus(0, 0, "_PM25_Value_Level",           "PM2.5空气质量等级",    false),
            new SignalStatus(0, 0, "_AQSLv",                      "AQS空气质量等级",      false),
            
            /* 传感器状态 */
            new SignalStatus(0, 0, "传感器状态 ",                 "*****传感器状态****",  false),
            new SignalStatus(0, 0, "_EvaporatorTemp",             "前蒸发器温度",         false),
            new SignalStatus(0, 0, "_EvapOutTemp",                "前蒸发器出口温度",     false),
            new SignalStatus(0, 0, "RACC_RearEvapTemp",           "后蒸发器温度",         false),
            new SignalStatus(0, 0, "_LPPresure",                  "低压(kPa)",            false),
            new SignalStatus(0, 0, "_HPPresure",                  "高压(kPa)",            false),
            new SignalStatus(0, 0, "PTC_OutTemp",                 "PTC出口温度",          false),
            new SignalStatus(0, 0, "_InsideTemp",                 "前车内温度",           false),
            new SignalStatus(0, 0, "_FLFaceTemp",                 "主驾吹面温度",         false),
            new SignalStatus(0, 0, "_FRFaceTemp",                 "副驾吹面温度",         false),
            new SignalStatus(0, 0, "_FLFootTemp",                 "主驾吹脚温度",         false),
            new SignalStatus(0, 0, "_FRFootTemp",                 "副驾吹脚温度",         false),
            new SignalStatus(0, 0, "RACC_RearIncarTemp",          "后车内温度",           false),
            new SignalStatus(0, 0, "RACC_RearFaceTemp",           "后吹面温度",           false),
            new SignalStatus(0, 0, "RACC_RearFootTemp",           "后吹脚温度",           false),
            new SignalStatus(0, 0, "RACC_RearEvapTemp",           "后蒸发温度",           false),
        };

        /* 电池 */
        public static List<SignalStatus> BatSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_CoolSysWorkMode",           "液侧系统工作模式",        false),
            new SignalStatus(0, 0, "_RefSysWorkMode",            "制冷剂侧系统工作模式",    false),
            new SignalStatus(0, 0, "ThermalManageRequest",       "BMS热管理请求",           false),
            new SignalStatus(0, 0, "InletDestinTemp",            "电池目标入口水温",        false),
            new SignalStatus(0, 0, "BattIntakeTemp",             "电池实际入口水温",        false),
            new SignalStatus(0, 0, "BattIOutletTemp",            "电池实际出口水温",        false),
            new SignalStatus(0, 0, "BattAvgTemp",                "电池平均温度",            false),
            new SignalStatus(0, 0, "BattMinTemp",                "电池最低温度",            false),
            new SignalStatus(0, 0, "BattMaxTemp",                "电池最高温度",            false),
            new SignalStatus(0, 0, "_BatPumpSpdResp",            "电池水泵反馈值",          false),
            new SignalStatus(0, 0, "MotPumpSpdResp",             "后电机水泵反馈值",        false),
            new SignalStatus(0, 0, "PTC_Power",                  "PTC 反馈功率",            false),
            new SignalStatus(0, 0, "CCM_Speed",                  "压缩机实际转速",          false),
            new SignalStatus(0, 0, "_FanSpdResp",                "电子风扇反馈转速",        false),
            new SignalStatus(0, 0, "_EXVPos_Bat",                "BatEXV反馈位置",          false),
            new SignalStatus(0, 0, "_CFCV_Pos",                  "采暖三通阀位置反馈",      false),
            new SignalStatus(0, 0, "五通阀 ",                    "***** ValVe5 (50:Normal 950:低温散热 1850:电机保温 2750:余热利用)****",   false),
            new SignalStatus(0, 0, "_C5WV_Pos",                  "五通阀反馈位置" ,         false),
            new SignalStatus(0, 0, "_AGS_precent",               "AGS反馈位置",             false),
            new SignalStatus(0, 0, "GW_BMS_RHighVolSts",         "整车高压状态",            false),
            new SignalStatus(0, 0, "GW_BMS_BattChargerSts",      "整车充电状态",            false),
            new SignalStatus(0, 0, "_ModeSts",                   "前空调开关",              false),
            new SignalStatus(0, 0, "_RearEnDisplay",             "后空调开关",              false),
        };

        /* 压缩机 */
        public static List<SignalStatus> compSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "COMP_SV",                    "系统压缩机控制目标",      false),
            new SignalStatus(0, 0, "COMP_PV",                    "系统压缩机控制实际值",    false),
            new SignalStatus(0, 0, "AC_Command",                 "压缩机开启指令",          false),
            new SignalStatus(0, 0, "CompressorSpeedCtl",         "压缩机控制转速",          false),
            new SignalStatus(0, 0, "CCM_Speed",                  "压缩机实际转速",          false),
            new SignalStatus(0, 0, "Test_output_Debug_CompSta",  "系统压缩机状态",          false),
            new SignalStatus(0, 0, "CCM_Power",                  "压缩机功率",              false),
            new SignalStatus(0, 0, "CCM_BaseState",              "压缩机反馈状态",          false),
            new SignalStatus(0, 0, "_CompOutTemp",               "压缩机出口温度",          false),
            new SignalStatus(0, 0, "_HPTemp",                    "压缩机高压温度",          false),
            new SignalStatus(0, 0, "_EvaporatorTemp",            "前蒸发器温度",            false),
            new SignalStatus(0, 0, "_EvapOutTemp",               "前蒸发器出口温度",        false),
            new SignalStatus(0, 0, "RACC_RearEvapTemp",          "后蒸发器温度",            false),
            new SignalStatus(0, 0, "Test_e_sw_LPLowPermit",      "低压下限保护",            false),
            new SignalStatus(0, 0, "_LPPresure",                 "低压(kPa)",               false),
            new SignalStatus(0, 0, "Test_e_sw_LPHighPermit",     "低压上限保护",            false),
            new SignalStatus(0, 0, "Test_TxMsg6_HPLowPermit",    "高压下限保护",            false),
            new SignalStatus(0, 0, "_HPPresure",                 "高压(kPa)",               false),
            new SignalStatus(0, 0, "Test_TxMsg6_HPHighPermit",   "高压上限保护",            false),
            new SignalStatus(0, 0, "_EcompOffCode",              "压缩机关闭码",            false),
            new SignalStatus(0, 0, "CCM_CANErr",                 "压缩机故障状态",          false),
            new SignalStatus(0, 0, "CCM_Volt",                   "压缩机电压",              false),
            new SignalStatus(0, 0, "CCM_Curr",                   "压缩机电流",              false),
            new SignalStatus(0, 0, "GW_BMS_RHighVolSts",         "整车高压状态",            false),
            new SignalStatus(0, 0, "GW_BMS_BattChargerSts",      "整车充电状态",            false),
        };
        /* PTC */
        public static List<SignalStatus> ptcSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "Test_e_sw_WTC_H_SV",         "系统PTC控制目标",      false),
            new SignalStatus(0, 0, "Test_e_sw_WTC_H_PV",         "系统PTC控制实际值",    false),
            new SignalStatus(0, 0, "_PTCHeatingReq",             "PTC_AC加热开启请求",   false),
            new SignalStatus(0, 0, "_PTCPowerSet",               "PTC 控制功率",         false),
            new SignalStatus(0, 0, "_PTCOutTempSet",             "PTC 设定温度",         false),
            new SignalStatus(0, 0, "PTC_Power",                  "PTC 反馈功率",         false),
            new SignalStatus(0, 0, "PTC_BscSts",                 "PTC反馈状态",          false),
            new SignalStatus(0, 0, "PTC_OutTemp",                "PTC出口温度",          false),
            new SignalStatus(0, 0, "_PTCOffCode",                "PTC关闭码",            false),
            new SignalStatus(0, 0, "PTC_HighVoltage",            "PTC电压",              false),
            new SignalStatus(0, 0, "PTC_Fault",                  "PTC故障状态",          false),
            new SignalStatus(0, 0, "_AcPumpSpdResp",             "采暖水泵实际转速",     false),
            new SignalStatus(0, 0, "GW_BMS_RHighVolSts",         "整车高压状态",         false),
            new SignalStatus(0, 0, "GW_BMS_BattChargerSts",      "整车充电状态",         false),
            
            /* 后PTC */
            new SignalStatus(0, 0, "后PTC ",                     "*******后PTC******",   false),
            new SignalStatus(0, 0, "_RAPTC_ModOpEnblCmd",        "后APTC开启使能",       false),
            new SignalStatus(0, 0, "_RAPTC_PwrCmnd",             "后PTC功率请求值",      false),
            new SignalStatus(0, 0, "_RAPTC_MsrdPwrFrt",          "后PTC功率反馈值",      false),
            new SignalStatus(0, 0, "_RAPTC_MaxAllowPwr",         "后PTC最大功率允许值",  false),
            new SignalStatus(0, 0, "_RAPTC_ModOprtgStsFrt",      "后APTC工作状态",       false),
            new SignalStatus(0, 0, "_RAPTC_MsrdLvFrt",           "后APTC电压值",         false),
            new SignalStatus(0, 0, "_RAPTC_MsrdCurrFrt",         "后APTC电流值",         false),

        };

        /* 空调状态 */
        public static List<SignalStatus> hvacSignalStatus = new List<SignalStatus>()
        {
            /* 前空调 */
            new SignalStatus(0, 0, "_ModeSts",                    "前空调开关",           false),
            new SignalStatus(0, 0, "_AutoACHeatSts",              "前空调自动冷暖风状态", false),
            new SignalStatus(0, 0, "_WorkModeStsF",               "前空调工作状态",       false),
            new SignalStatus(0, 0, "FLTempSet",                   "主驾设定温度",         false),
            new SignalStatus(0, 0, "FRTempSet",                   "副驾设定温度",         false),
            new SignalStatus(0, 0, "_FrontDefrostSts",            "前除霜状态",           false),
            new SignalStatus(0, 0, "_RearDefrostSts",             "后除霜状态",           false),
            new SignalStatus(0, 0, "_CycleStatus",                "内外循环状态",         false),
            new SignalStatus(0, 0, "_ModelDisplay",               "前空调出风模式",       false),
            new SignalStatus(0, 0, "_FLFanGearDisplay",           "前空调风量档位",       false),
            new SignalStatus(0, 0, "_BlowerTrgVol",               "前空调风量电压控制值", false),
            new SignalStatus(0, 0, "_BlowerVolFb",                "前空调风量电压反馈值", false),
            new SignalStatus(0, 0, "_CoolSysWorkMode",            "液侧系统工作模式",     false),
            new SignalStatus(0, 0, "_RefSysWorkMode",             "制冷剂侧系统工作模式", false),
            new SignalStatus(0, 0, "_FLFaceTemp",                 "主驾吹面温度",         false),
            new SignalStatus(0, 0, "_FRFaceTemp",                 "副驾吹面温度",         false),
            new SignalStatus(0, 0, "_FLFootTemp",                 "主驾吹脚温度",         false),
            new SignalStatus(0, 0, "_FRFootTemp",                 "副驾吹脚温度",         false),
            new SignalStatus(0, 0, "_EvaporatorTemp",             "前蒸发温度",           false),
            new SignalStatus(0, 0, "_OutsideTemp_Corrected",      "环境温度校正值",       false),
            new SignalStatus(0, 0, "_AM_VolFb_Left",              "左混合风门反馈电压",   false),
            new SignalStatus(0, 0, "_AM_VolFb_Right",             "右混合风门反馈电压",   false),
            new SignalStatus(0, 0, "_AirIntakeMotVolFb",          "内外循环电机反馈电压", false),
            new SignalStatus(0, 0, "_AirDisMotVolFb",             "模式电机反馈电压",     false),

            /* 后空调 */
            new SignalStatus(0, 0, "后空调 ",                     "*******后空调******",  false),
            new SignalStatus(0, 0, "_RearEnDisplay",              "后空调开关",           false),
            new SignalStatus(0, 0, "_RaccAutoACHeatSts",          "后空调自动冷暖风状态", false),
            new SignalStatus(0, 0, "_RearTempDisplay",            "后空调设定温度",       false),
            new SignalStatus(0, 0, "_RearModelDisplay",           "后空调出风模式",       false),
            new SignalStatus(0, 0, "_RearFanGearDisplay",         "后空调风量档位",       false),
            new SignalStatus(0, 0, "RACC_Blower_VolTargetFb",     "后空调风量电压",       false),
            new SignalStatus(0, 0, "RACC_RearMixMotorFB",         "后混合风门反馈电压",   false),
            new SignalStatus(0, 0, "RACC_RearIncarTemp",          "后车内温度",           false),
            new SignalStatus(0, 0, "RACC_RearFaceTemp",           "后吹面温度",           false),
            new SignalStatus(0, 0, "RACC_RearFootTemp",           "后吹脚温度",           false),
            new SignalStatus(0, 0, "RACC_RearEvapTemp",           "后蒸发温度",           false),
            new SignalStatus(0, 0, "RACC_RearSO_TXVStFB",         "SOTXV反馈状态",        false),
        };
        
        /* EXV */
        public static List<SignalStatus> exvSignalStatus = new List<SignalStatus>()
        {
            /* AcEXV */
            new SignalStatus(0, 0, "Test_e_sw_EXV_AC_SV",        "AcEXV控制目标值",      false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_AC_PV",        "AcEXV控制实际值",      false),
            new SignalStatus(0, 0, "_EXVPos_Ac_Req",          "AcEXV请求位置",        false),
            new SignalStatus(0, 0, "_EXVPos_Ac",              "AcEXV反馈位置",        false),
            new SignalStatus(0, 0, "_EXVPos_Ac_ErrAll",       "AcEXV故障状态",        false),
            new SignalStatus(0, 0, "_EXVErr_Ac_Sts",          "AcEXV故障值",          false),
            new SignalStatus(0, 0, "_EXV_St_Initial_Ac",      "AcEXV初始化状态",      false),
            
            /* BatEXV */
            new SignalStatus(0, 0, "电池EXV ",                   "*******Bat EXV******", false),
            new SignalStatus(0, 0, "_EXVPos_Bat_Req",            "BatEXV请求位置",       false),
            new SignalStatus(0, 0, "_EXVPos_Bat",                "BatEXV反馈位置",       false),
            new SignalStatus(0, 0, "_EXVPos_Bat_ErrAll",         "BatEXV故障状态",       false),
            new SignalStatus(0, 0, "_EXVErr_Bat_Sts",            "BatEXV故障值",         false),
            new SignalStatus(0, 0, "_EXV_St_Initial_Bat",        "BatEXV初始化状态",     false),

            /* EXCV */
            new SignalStatus(0, 0, "采暖EXV ",                   "******* EXCV *******", false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_H_SV",         "EXCV控制目标值",       false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_H_PV",         "EXCV控制实际值",       false),
            new SignalStatus(0, 0, "_EXVPos_EXCV_Req",           "EXCV请求位置",         false),
            new SignalStatus(0, 0, "_EXVPos_EXCV",               "EXCV反馈位置",         false),
            new SignalStatus(0, 0, "_EXVPos_EXCV_ErrAll",        "EXCV故障状态",         false),
            new SignalStatus(0, 0, "_EXVErr_EXCV_Sts",           "EXCV故障值",           false),
            new SignalStatus(0, 0, "_EXV_St_Initial_Excv",       "EXCV初始化状态",       false),
        };

        /* Pump */
        public static List<SignalStatus> pumpSignalStatus = new List<SignalStatus>()
        {
            /* 采暖Pump */
            new SignalStatus(0, 0, "_PumpPosition",           "采暖水泵请求值",       false),
            new SignalStatus(0, 0, "_AcPumpSpdResp",          "采暖水泵反馈值",       false),
            new SignalStatus(0, 0, "_AcPumpErrSts",           "采暖水泵故障状态",     false),
            new SignalStatus(0, 0, "_AcPumpVoltResp",         "采暖水泵电压",         false),
            new SignalStatus(0, 0, "_AcPumpCurrResp",         "采暖水泵电流",         false),
            new SignalStatus(0, 0, "_AcPumpTempResp",         "采暖水泵温度",         false),
            
            /* 电池Pump */
            new SignalStatus(0, 0, "电池PUMP ",                  "******Bat PUMP*****",  false),
            new SignalStatus(0, 0, "_BMSPumpCtrl",               "电池水泵请求值",       false),
            new SignalStatus(0, 0, "_BatPumpSpdResp",            "电池水泵反馈值",       false),
            new SignalStatus(0, 0, "_BatPumpErrSts",             "电池水泵故障状态",     false),
            new SignalStatus(0, 0, "BatPump_Supplied_Voltage",   "电池水泵电压",         false),
            new SignalStatus(0, 0, "BatPump_Operation_Current",  "电池水泵电流",         false),
            new SignalStatus(0, 0, "_BatPump_Operation_Temp",    "电池水泵温度",         false),

            /* 后电机Pump */
            new SignalStatus(0, 0, "后电机PUMP ",                "*****RMotor PUMP*****",false),
            new SignalStatus(0, 0, "MotPumpSpdReq",              "后电机水泵请求值",     false),
            new SignalStatus(0, 0, "MotPumpSpdResp",             "后电机水泵反馈值",     false),
            new SignalStatus(0, 0, "MotPumpErrSts",              "后电机水泵故障状态",   false),
            new SignalStatus(0, 0, "MotPump_Supplied_Volta",     "后电机水泵电压(26/27)",false),
            new SignalStatus(0, 0, "MotPump_Operation_Curr",     "后电机水泵电流(26/27)",false),
            new SignalStatus(0, 0, "TMS_RearMotPump_Voltage",    "后电机水泵电压(24)",   false),
            new SignalStatus(0, 0, "TMS_RearMotPump_Current",    "后电机水泵电流(24)",   false),
            new SignalStatus(0, 0, "MotPump_Operation_Temp",     "后电机水泵温度",       false),
        };

        /* 电子风扇(FAN) */
        public static List<SignalStatus> fanSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_FanWorSts",                 "电子风扇控制",         false),
            new SignalStatus(0, 0, "_FanSpdResp",                "电子风扇反馈转速",     false),
            new SignalStatus(0, 0, "_FanAvgVolResp",             "电子风扇反馈电压",     false),
            new SignalStatus(0, 0, "_FanAvgCurrentResp",         "电子风扇反馈电流",     false),
            new SignalStatus(0, 0, "_FanAvgTempResp",            "电子风扇反馈温度",     false),
            new SignalStatus(0, 0, "_FanAvgTempResp",            "电子风扇反馈温度",     false),
            new SignalStatus(0, 0, "_FanErrSts",                 "电子风扇反馈状态",     false),
        };

        /* 多通阀(Valve) */
        public static List<SignalStatus> ValVeSignalStatus = new List<SignalStatus>()
        {
            /* 采暖三通阀 */
            new SignalStatus(0, 0, "_CFCV_Pos_Req",              "采暖三通阀位置请求",   false),
            new SignalStatus(0, 0, "_CFCV_Pos",                  "采暖三通阀位置反馈",   false),
            new SignalStatus(0, 0, "_CFCV_ErrSts",               "采暖三通阀故障值",     false),
            
            /* DSP三通阀 */
            new SignalStatus(0, 0, "电池PUMP ",                  "******Bat PUMP*****", false),
            new SignalStatus(0, 0, "GW_ADDCSOC_PipeFlowMaxReq",  "DSP智驾最大需求流量", false),
            new SignalStatus(0, 0, "GW_ADDCSOC_OpeningValueReq", "DSP智驾最大需求开度", false),
            new SignalStatus(0, 0, "GW_ICHS_PipeFlowMaxReq",     "DSP座舱最大需求流量", false),
            new SignalStatus(0, 0, "GW_ICHS_OpeningValueReq",    "DSP座舱最大需求开度", false),
            new SignalStatus(0, 0, "_DSPCoolingValve_PosReq",    "DSP三通阀TMS请求值",  false),
            new SignalStatus(0, 0, "_DSPBDCPosFB",               "DSP三通阀TMS反馈值",  false),
            new SignalStatus(0, 0, "_DSPCoolingValveFaultSts",   "DSP三通阀故障状态",   false),
            
            /* 五通阀 */
            new SignalStatus(0, 0, "五通阀 ",                    "***** ValVe5 (50:Normal 950:低温散热 1850:电机保温 2750:余热利用)****",   false),
            new SignalStatus(0, 0, "_C5WV_targetpos",            "五通阀目标角度",      false),
            new SignalStatus(0, 0, "Test_C5WVPos",               "五通阀测试帧强控状态",false),
            new SignalStatus(0, 0, "_C5WV_Pos",                  "五通阀反馈位置" ,     false),
            new SignalStatus(0, 0, "_C5WV_Sta",                  "五通阀故障状态" ,     false),
            new SignalStatus(0, 0, "_C5WV_ErrSts",               "五通阀故障值" ,       false),
            new SignalStatus(0, 0, "_C5WV_PCBA",                 "五通阀PCBA温度" ,     false),
        };

        /* 主动格栅(AGS) */
        public static List<SignalStatus> agsSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_AGSPercentRq",              "AGS请求位置",          false),
            new SignalStatus(0, 0, "_AGS_precent",               "AGS反馈位置",          false),
            new SignalStatus(0, 0, "_AGSErrSts",                 "AGS故障值",            false),
        };

        /* 空气质量检测(AQS_PM2_5) */
        public static List<SignalStatus> aqsPM2_5SignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_PM25_Value_Level",          "PM2.5空气质量等级",    false),
            new SignalStatus(0, 0, "_PM25_Fault",                "PM2.5故障状态",        false),

            new SignalStatus(0, 0, "空气质量检测AQS ",           "******AGS*****",       false),
            new SignalStatus(0, 0, "_AQSLv",                     "AQS空气质量等级",      false),
            new SignalStatus(0, 0, "_AQSErr",                    "AQS故障状态",          false),
        };

        /* 热泵模式充注量试验数据 */
        public static List<SignalStatus> HeatrefrigerantChargeSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_EXVPos_EXCV",               "EXCV反馈位置",                false),
            new SignalStatus(0, 0, "CCM_Speed",                  "压缩机实际转速",              false),
            new SignalStatus(0, 0, "_OHXOutTemp",                "OHX出口温度",                 false),
            new SignalStatus(0, 0, "_CompOutTemp",               "压缩机出口温度",              false),
            new SignalStatus(0, 0, "_HPPresure",                 "高压(kPa)",                   false),
            new SignalStatus(0, 0, "_LPPresure",                 "低压(kPa)",                   false),
            new SignalStatus(0, 0, "压缩机吸气口温度 ",          "**压缩机吸气口温度**",        false),
            new SignalStatus(0, 0, "_HPTemp",                    "高压压力传感器温度",          false),
            new SignalStatus(0, 0, "高压饱和温度",               "高压饱和温度",                false),
            new SignalStatus(0, 0, "低压饱和温度",               "低压饱和温度",                false),
            new SignalStatus(0, 0, "-------------",              "**------------**",            false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_H_SV",         "EXCV控制目标值",              false),
            new SignalStatus(0, 0, "Test_e_sw_EXV_H_PV",         "EXCV控制实际值",              false),
            new SignalStatus(0, 0, "_FLFootTemp",                "主驾吹脚温度",                false),
            new SignalStatus(0, 0, "_FRFootTemp",                "副驾吹脚温度",                false),
        };

        /* 单制冷模式充注量试验数据 */
        public static List<SignalStatus> SignalAcrefrigerantChargeSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_EXVPos_Ac",                 "AcEXV反馈位置",               false),
            new SignalStatus(0, 0, "CCM_Speed",                  "压缩机实际转速",              false),
            new SignalStatus(0, 0, "_OHXOutTemp",                "OHX出口温度",                 false),
            new SignalStatus(0, 0, "_EvapOutTemp",               "前蒸发器出口温度",            false),
            new SignalStatus(0, 0, "-------------",              "** EVAP Out 热电偶 **",       false),
            new SignalStatus(0, 0, "_CompOutTemp",               "压缩机出口温度",              false),
            new SignalStatus(0, 0, "_HPPresure",                 "高压(kPa)",                   false),
            new SignalStatus(0, 0, "_LPPresure",                 "低压(kPa)",                   false),
            new SignalStatus(0, 0, "------------- ",             "**压缩机吸气口温度 热电偶**", false),
            new SignalStatus(0, 0, "高压饱和温度",               "高压饱和温度",                false),
            new SignalStatus(0, 0, "低压饱和温度",               "低压饱和温度",                false),
            new SignalStatus(0, 0, "-------------",              "蒸发器出口过热度",            false),
            new SignalStatus(0, 0, "-------------",              "压缩机吸气过热度",            false),
            new SignalStatus(0, 0, "-------------",              "压缩机排气过热度",            false),
            new SignalStatus(0, 0, "-------------",              "OHX出口过冷度",               false),
            new SignalStatus(0, 0, "_FLFaceTemp",                "主驾吹面温度",               false),
            new SignalStatus(0, 0, "_FRFaceTemp",                "副驾吹面温度",               false),
            //new SignalStatus(0, 0, "Test_e_sw_EXV_AC_SV",        "AcEXV控制目标值",             false),
            //new SignalStatus(0, 0, "Test_e_sw_EXV_AC_PV",        "AcEXV控制实际值",             false),
        };

        /* 混合制冷模式充注量试验数据 */
        public static List<SignalStatus> DoubleAcrefrigerantChargeSignalStatus = new List<SignalStatus>()
        {
            new SignalStatus(0, 0, "_EXVPos_Ac",                 "AcEXV反馈位置",               false),
            new SignalStatus(0, 0, "CCM_Speed",                  "压缩机实际转速",              false),
            new SignalStatus(0, 0, "_OHXOutTemp",                "OHX出口温度",                 false),
            new SignalStatus(0, 0, "_EvapOutTemp",               "前蒸发器出口温度",            false),
            new SignalStatus(0, 0, "-------------",              "** EVAP Out 热电偶 **",       false),
            new SignalStatus(0, 0, "_CompOutTemp",               "压缩机出口温度",              false),
            new SignalStatus(0, 0, "_HPPresure",                 "高压(kPa)",                   false),
            new SignalStatus(0, 0, "_LPPresure",                 "低压(kPa)",                   false),
            new SignalStatus(0, 0, "------------- ",             "**压缩机吸气口温度 热电偶**", false),
            new SignalStatus(0, 0, "高压饱和温度",               "高压饱和温度",                false),
            new SignalStatus(0, 0, "低压饱和温度",               "低压饱和温度",                false),
            new SignalStatus(0, 0, "-------------",              "蒸发器出口过热度",            false),
            new SignalStatus(0, 0, "-------------",              "压缩机吸气过热度",            false),
            new SignalStatus(0, 0, "-------------",              "压缩机排气过热度",            false),
            new SignalStatus(0, 0, "-------------",              "OHX出口过冷度",               false),
            new SignalStatus(0, 0, "_FLFaceTemp",                 "主驾吹面温度",               false),
            new SignalStatus(0, 0, "_FRFaceTemp",                 "副驾吹面温度",               false),
            new SignalStatus(0, 0, "_RefrigerantTemp",           "Chiller出口温度",             false),
        };

        public class Component
        {
            public Component(List<SignalStatus> signalStatus, string ComponentNodeNa)
            {
                signalStatuss = signalStatus;
                ComponentNodeName = ComponentNodeNa;
            }
            public List<SignalStatus> signalStatuss = new List<SignalStatus>();
            public string ComponentNodeName = "";
        }
        public class SignalStatus
        {
            public SignalStatus(int msgIndex, int sigIndex, string siglName, string DisName, bool flag)
            {
                messgeIndex = msgIndex;
                signalIndex = sigIndex;
                signalName = siglName;
                DisplayName = DisName;
                Flag = flag;
            }
            public int messgeIndex = 0;/* message 下标 */
            public int signalIndex = 0;/* signal 下标 */
            public string signalName = "";
            public string DisplayName = "";
            public bool Flag = false;
        }

        public DataShow()
        {
            InitializeComponent();
        }

        internal void DataShowInit()
        {
            UpdateComponentTreeview();
            FindSelectIndex();

            radioButton_R134a.Checked = true;
            BaseParamter.dbcHelper.GetMessageIndexAndSignalIndexBySigName("TMS_HPPresure", ref HPMsgIndex, ref HPSignalIndex, ref HPFindFlag);
            BaseParamter.dbcHelper.GetMessageIndexAndSignalIndexBySigName("TMS_LPPresure", ref LPMsgIndex, ref LPSignalIndex, ref LPFindFlag);

            SelectComponentNodesIndex = 0;
            UpdateComponentNodeListview(SelectComponentNodesIndex);
        }
        internal void FindSelectIndex()
        {
            foreach (Component component in ComponentNodes) 
            {
                foreach (SignalStatus signalStatus in component.signalStatuss)
                {
                    signalStatus.messgeIndex = 0;
                    signalStatus.signalIndex = 0;
                    signalStatus.Flag = false;
                    BaseParamter.dbcHelper.GetMessageIndexAndSignalIndexBySigName(signalStatus.signalName,ref signalStatus.messgeIndex, ref signalStatus.signalIndex, ref signalStatus.Flag);
                }
            }
        }

        internal void UpdateComponentTreeview()
        {
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add("Component");

            listView1.View = View.Details;
            listView1.Columns.Add("Name");
            listView1.Columns.Add("当前状态");
            listView1.Columns.Add("信号名");
            listView1.Columns[0].Width = 200;
            listView1.Columns[1].Width = 200;
            listView1.Columns[2].Width = 350;


            //ComponentNodes.Clear();
            foreach (var component in ComponentNodes)
            {
                treeView1.Nodes[0].Nodes.Add(component.ComponentNodeName);
            }

            treeView1.ExpandAll();
        }
        private void DataShow_Load(object sender, EventArgs e)
        {
            DataShowInit();
            timer1.Start();
        }

        private void UpdateComponentNodeListview(int index)
        {
            int i;
            int messageIndex = 0;
            int signalIndex = 0;
            bool Flag = false;
            if (ComponentNodes.Count <= index)
            {
                return;
            }
            ListViewItem item;
            listView1.BeginUpdate();
            listView1.Items.Clear();

            for (i = 0; i < ComponentNodes[index].signalStatuss.Count; i++)
            {
                messageIndex = ComponentNodes[index].signalStatuss[i].messgeIndex;
                signalIndex = ComponentNodes[index].signalStatuss[i].signalIndex;
                Flag = ComponentNodes[index].signalStatuss[i].Flag;
                item = new ListViewItem();

                item.Text = ComponentNodes[index].signalStatuss[i].DisplayName;

                item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].signalDisplayStr);
                item.SubItems.Add(BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].Comment);

                listView1.Items.Add(item);
            }
            listView1.EndUpdate();
        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if ((e.Node.Parent != null) && (e.Node.Parent.Text == "Component"))
            {
                try
                {
                    SelectComponentNodesIndex = e.Node.Index;
                    UpdateComponentNodeListview(SelectComponentNodesIndex);
                }
                catch (Exception en)
                {

                }
            }
        }

        private void UpdateeComponentNodeListviewTimer(int index)
        {
            int i;
            int messageIndex = 0;
            int signalIndex = 0;
            bool Flag = false;
            if (ComponentNodes.Count <= index)
            {
                return;
            }
            for (i = 0; i < ComponentNodes[index].signalStatuss.Count; i++)
            {
                messageIndex = ComponentNodes[index].signalStatuss[i].messgeIndex;
                signalIndex = ComponentNodes[index].signalStatuss[i].signalIndex;
                Flag = ComponentNodes[index].signalStatuss[i].Flag;

                if (Flag)
                {
                    if (!listView1.Items[i].SubItems[1].Text.Equals(BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].signalDisplayStr))
                    {
                        listView1.Items[i].SubItems[1].Text = BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].signalDisplayStr;
                    }
                    else
                    {
                        /* empty */
                    }
                    if (!listView1.Items[i].SubItems[2].Text.Equals("(0x" + BaseParamter.dbcHelper.dbcFile.messages[messageIndex].messgeId.ToString("X2") + ") " + BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].signalName))
                    {
                        listView1.Items[i].SubItems[2].Text = "(0x" + BaseParamter.dbcHelper.dbcFile.messages[messageIndex].messgeId.ToString("X2") + ") " + BaseParamter.dbcHelper.dbcFile.messages[messageIndex].signals[signalIndex].signalName;
                    }
                    else
                    {
                        /* empty */
                    }
                }
                else
                {
                    if(ComponentNodes[index].signalStatuss[i].signalName.Equals("高压饱和温度"))
                    {
                        if(!listView1.Items[i].SubItems[1].Text.Equals(CalculateSaturationTemperature(BaseParamter.dbcHelper.dbcFile.messages[HPMsgIndex].signals[HPSignalIndex].result)))
                        {
                            listView1.Items[i].SubItems[1].Text = CalculateSaturationTemperature(BaseParamter.dbcHelper.dbcFile.messages[HPMsgIndex].signals[HPSignalIndex].result);
                        }
                        if (!listView1.Items[i].SubItems[2].Text.Equals("高压饱和温度"))
                        {
                            listView1.Items[i].SubItems[2].Text = "高压饱和温度";
                        }
                    }
                    else if(ComponentNodes[index].signalStatuss[i].signalName.Equals("低压饱和温度"))
                    {
                        if (!listView1.Items[i].SubItems[1].Text.Equals(CalculateSaturationTemperature(BaseParamter.dbcHelper.dbcFile.messages[LPMsgIndex].signals[LPSignalIndex].result)))
                        {
                            listView1.Items[i].SubItems[1].Text = CalculateSaturationTemperature(BaseParamter.dbcHelper.dbcFile.messages[LPMsgIndex].signals[LPSignalIndex].result);
                        }

                        if(!listView1.Items[i].SubItems[2].Text.Equals("低压饱和温度"))
                        {
                            listView1.Items[i].SubItems[2].Text = "低压饱和温度";
                        }
                    }
                    else if (!listView1.Items[i].SubItems[1].Text.Equals("未找到该信号"))
                    {
                        listView1.Items[i].SubItems[1].Text = "未找到该信号";
                        listView1.Items[i].SubItems[2].Text = "未找到该信号";
                    }
                    else
                    {
                        /* empty */
                    }
                }
            }
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            UpdateeComponentNodeListviewTimer(SelectComponentNodesIndex);
        }

        /// <summary>
        /// 使用多项式拟合计算饱和温度（更高精度）
        /// 适用压力范围：100-3000 kPa
        /// </summary>
        public string CalculateSaturationTemperature(double pressureKPa)
        {
            if(radioButton_R134a.Checked)
            {
                return R1234yfSaturationCalculator.CalculateSaturationTemperatureLogInterpolation_R134a(pressureKPa).ToString("F1") + "℃";
            }
            else
            {
                return R1234yfSaturationCalculator.CalculateSaturationTemperatureLogInterpolation_1234yf(pressureKPa).ToString("F1") + "℃";
            }
        }
        public class R1234yfSaturationCalculator
        {
            /// <summary>
            /// 温度-压力映射结构体
            /// </summary>
            public struct TemperaturePressurePoint
            {
                public int TemperatureTenthC;  // 温度 (0.1°C)
                public int PressureKPa;        // 压力 (kPa)

                public TemperaturePressurePoint(int tempTenthC, int pressureKPa)
                {
                    TemperatureTenthC = tempTenthC;
                    PressureKPa = pressureKPa;
                }

                public double TemperatureC => TemperatureTenthC / 10.0;
            }

            // 1234yf饱和温度-压力对照表 (基于您提供的数据)
            private static readonly List<TemperaturePressurePoint> SaturationTable_1234yf = new List<TemperaturePressurePoint>
    {
        new TemperaturePressurePoint(869, 2893), new TemperaturePressurePoint(860, 2842), new TemperaturePressurePoint(850, 2785),
        new TemperaturePressurePoint(840, 2730), new TemperaturePressurePoint(830, 2675), new TemperaturePressurePoint(820, 2622),
        new TemperaturePressurePoint(810, 2569), new TemperaturePressurePoint(800, 2517), new TemperaturePressurePoint(790, 2466),
        new TemperaturePressurePoint(780, 2416), new TemperaturePressurePoint(770, 2366), new TemperaturePressurePoint(760, 2318),
        new TemperaturePressurePoint(750, 2270), new TemperaturePressurePoint(740, 2223), new TemperaturePressurePoint(730, 2177),
        new TemperaturePressurePoint(720, 2131), new TemperaturePressurePoint(710, 2086), new TemperaturePressurePoint(700, 2042),
        new TemperaturePressurePoint(690, 1999), new TemperaturePressurePoint(680, 1956), new TemperaturePressurePoint(670, 1915),
        new TemperaturePressurePoint(660, 1873), new TemperaturePressurePoint(650, 1833), new TemperaturePressurePoint(640, 1793),
        new TemperaturePressurePoint(630, 1754), new TemperaturePressurePoint(620, 1715), new TemperaturePressurePoint(610, 1677),
        new TemperaturePressurePoint(600, 1640), new TemperaturePressurePoint(590, 1603), new TemperaturePressurePoint(580, 1567),
        new TemperaturePressurePoint(570, 1532), new TemperaturePressurePoint(560, 1497), new TemperaturePressurePoint(550, 1463),
        new TemperaturePressurePoint(540, 1429), new TemperaturePressurePoint(530, 1396), new TemperaturePressurePoint(520, 1364),
        new TemperaturePressurePoint(510, 1332), new TemperaturePressurePoint(500, 1301), new TemperaturePressurePoint(490, 1270),
        new TemperaturePressurePoint(480, 1240), new TemperaturePressurePoint(470, 1210), new TemperaturePressurePoint(460, 1181),
        new TemperaturePressurePoint(450, 1152), new TemperaturePressurePoint(440, 1124), new TemperaturePressurePoint(430, 1097),
        new TemperaturePressurePoint(420, 1070), new TemperaturePressurePoint(410, 1043), new TemperaturePressurePoint(400, 1017),
        new TemperaturePressurePoint(390, 992),  new TemperaturePressurePoint(380, 966),  new TemperaturePressurePoint(370, 942),
        new TemperaturePressurePoint(360, 918),  new TemperaturePressurePoint(350, 894),  new TemperaturePressurePoint(340, 871),
        new TemperaturePressurePoint(330, 848),  new TemperaturePressurePoint(320, 826),  new TemperaturePressurePoint(310, 804),
        new TemperaturePressurePoint(300, 782),  new TemperaturePressurePoint(290, 761),  new TemperaturePressurePoint(280, 741),
        new TemperaturePressurePoint(270, 721),  new TemperaturePressurePoint(260, 701),  new TemperaturePressurePoint(250, 682),
        new TemperaturePressurePoint(240, 663),  new TemperaturePressurePoint(230, 644),  new TemperaturePressurePoint(220, 626),
        new TemperaturePressurePoint(210, 608),  new TemperaturePressurePoint(200, 591),  new TemperaturePressurePoint(190, 574),
        new TemperaturePressurePoint(180, 557),  new TemperaturePressurePoint(170, 541),  new TemperaturePressurePoint(160, 525),
        new TemperaturePressurePoint(150, 510),  new TemperaturePressurePoint(140, 494),  new TemperaturePressurePoint(130, 479),
        new TemperaturePressurePoint(120, 465),  new TemperaturePressurePoint(110, 451),  new TemperaturePressurePoint(100, 437),
        new TemperaturePressurePoint(90, 423),   new TemperaturePressurePoint(80, 410),   new TemperaturePressurePoint(70, 397),
        new TemperaturePressurePoint(60, 385),   new TemperaturePressurePoint(50, 372),   new TemperaturePressurePoint(40, 360),
        new TemperaturePressurePoint(30, 349),   new TemperaturePressurePoint(20, 337),   new TemperaturePressurePoint(10, 326),
        new TemperaturePressurePoint(0, 316),    new TemperaturePressurePoint(-10, 305),  new TemperaturePressurePoint(-20, 295),
        new TemperaturePressurePoint(-30, 285),  new TemperaturePressurePoint(-40, 275),  new TemperaturePressurePoint(-50, 266),
        new TemperaturePressurePoint(-60, 256),  new TemperaturePressurePoint(-70, 247),  new TemperaturePressurePoint(-80, 239),
        new TemperaturePressurePoint(-90, 230),  new TemperaturePressurePoint(-100, 222), new TemperaturePressurePoint(-110, 214),
        new TemperaturePressurePoint(-120, 206), new TemperaturePressurePoint(-130, 198), new TemperaturePressurePoint(-140, 191),
        new TemperaturePressurePoint(-150, 184), new TemperaturePressurePoint(-160, 177), new TemperaturePressurePoint(-170, 170),
        new TemperaturePressurePoint(-180, 163), new TemperaturePressurePoint(-190, 157), new TemperaturePressurePoint(-200, 151),
        new TemperaturePressurePoint(-210, 145), new TemperaturePressurePoint(-220, 139), new TemperaturePressurePoint(-230, 133),
        new TemperaturePressurePoint(-240, 128), new TemperaturePressurePoint(-250, 123), new TemperaturePressurePoint(-260, 118),
        new TemperaturePressurePoint(-270, 113), new TemperaturePressurePoint(-280, 108), new TemperaturePressurePoint(-290, 104),
        new TemperaturePressurePoint(-295, 102)
    };
            // 饱和温度-压力对照表 (基于您提供的新数据)
            private static readonly List<TemperaturePressurePoint> SaturationTable_R134a = new List<TemperaturePressurePoint>
    {
        new TemperaturePressurePoint(1011, 4067), new TemperaturePressurePoint(1010, 4057), new TemperaturePressurePoint(1000, 3974),
        new TemperaturePressurePoint(990, 3894),  new TemperaturePressurePoint(980, 3816),  new TemperaturePressurePoint(970, 3739),
        new TemperaturePressurePoint(960, 3664),  new TemperaturePressurePoint(950, 3591),  new TemperaturePressurePoint(940, 3519),
        new TemperaturePressurePoint(930, 3448),  new TemperaturePressurePoint(920, 3379),  new TemperaturePressurePoint(910, 3311),
        new TemperaturePressurePoint(900, 3244),  new TemperaturePressurePoint(890, 3178),  new TemperaturePressurePoint(880, 3113),
        new TemperaturePressurePoint(870, 3049),  new TemperaturePressurePoint(860, 2987),  new TemperaturePressurePoint(850, 2925),
        new TemperaturePressurePoint(840, 2865),  new TemperaturePressurePoint(830, 2805),  new TemperaturePressurePoint(820, 2747),
        new TemperaturePressurePoint(810, 2689),  new TemperaturePressurePoint(800, 2632),  new TemperaturePressurePoint(790, 2577),
        new TemperaturePressurePoint(780, 2522),  new TemperaturePressurePoint(770, 2468),  new TemperaturePressurePoint(760, 2415),
        new TemperaturePressurePoint(750, 2363),  new TemperaturePressurePoint(740, 2312),  new TemperaturePressurePoint(730, 2262),
        new TemperaturePressurePoint(720, 2213),  new TemperaturePressurePoint(710, 2164),  new TemperaturePressurePoint(700, 2116),
        new TemperaturePressurePoint(690, 2069),  new TemperaturePressurePoint(680, 2023),  new TemperaturePressurePoint(670, 1978),
        new TemperaturePressurePoint(660, 1933),  new TemperaturePressurePoint(650, 1889),  new TemperaturePressurePoint(640, 1846),
        new TemperaturePressurePoint(630, 1804),  new TemperaturePressurePoint(620, 1762),  new TemperaturePressurePoint(610, 1722),
        new TemperaturePressurePoint(600, 1681),  new TemperaturePressurePoint(590, 1642),  new TemperaturePressurePoint(580, 1603),
        new TemperaturePressurePoint(570, 1565),  new TemperaturePressurePoint(560, 1528),  new TemperaturePressurePoint(550, 1491),
        new TemperaturePressurePoint(540, 1455),  new TemperaturePressurePoint(530, 1420),  new TemperaturePressurePoint(520, 1385),
        new TemperaturePressurePoint(510, 1351),  new TemperaturePressurePoint(500, 1318),  new TemperaturePressurePoint(490, 1285),
        new TemperaturePressurePoint(480, 1253),  new TemperaturePressurePoint(470, 1221),  new TemperaturePressurePoint(460, 1190),
        new TemperaturePressurePoint(450, 1160),  new TemperaturePressurePoint(440, 1130),  new TemperaturePressurePoint(430, 1101),
        new TemperaturePressurePoint(420, 1072),  new TemperaturePressurePoint(410, 1044),  new TemperaturePressurePoint(400, 1016),
        new TemperaturePressurePoint(390, 989),   new TemperaturePressurePoint(380, 963),   new TemperaturePressurePoint(370, 937),
        new TemperaturePressurePoint(360, 912),   new TemperaturePressurePoint(350, 887),   new TemperaturePressurePoint(340, 863),
        new TemperaturePressurePoint(330, 839),   new TemperaturePressurePoint(320, 815),   new TemperaturePressurePoint(310, 792),
        new TemperaturePressurePoint(300, 770),   new TemperaturePressurePoint(290, 748),   new TemperaturePressurePoint(280, 727),
        new TemperaturePressurePoint(270, 706),   new TemperaturePressurePoint(260, 685),   new TemperaturePressurePoint(250, 665),
        new TemperaturePressurePoint(240, 646),   new TemperaturePressurePoint(230, 627),   new TemperaturePressurePoint(220, 608),
        new TemperaturePressurePoint(210, 590),   new TemperaturePressurePoint(200, 572),   new TemperaturePressurePoint(190, 554),
        new TemperaturePressurePoint(180, 537),   new TemperaturePressurePoint(170, 520),   new TemperaturePressurePoint(160, 504),
        new TemperaturePressurePoint(150, 488),   new TemperaturePressurePoint(140, 473),   new TemperaturePressurePoint(130, 458),
        new TemperaturePressurePoint(120, 443),   new TemperaturePressurePoint(110, 429),   new TemperaturePressurePoint(100, 415),
        new TemperaturePressurePoint(90, 401),    new TemperaturePressurePoint(80, 388),    new TemperaturePressurePoint(70, 375),
        new TemperaturePressurePoint(60, 362),    new TemperaturePressurePoint(50, 350),    new TemperaturePressurePoint(40, 338),
        new TemperaturePressurePoint(30, 326),    new TemperaturePressurePoint(20, 315),    new TemperaturePressurePoint(10, 304),
        new TemperaturePressurePoint(0, 293),     new TemperaturePressurePoint(-10, 282),   new TemperaturePressurePoint(-20, 272),
        new TemperaturePressurePoint(-30, 262),   new TemperaturePressurePoint(-40, 253),   new TemperaturePressurePoint(-50, 243),
        new TemperaturePressurePoint(-60, 234),   new TemperaturePressurePoint(-70, 226),   new TemperaturePressurePoint(-80, 217),
        new TemperaturePressurePoint(-90, 209),   new TemperaturePressurePoint(-100, 201),  new TemperaturePressurePoint(-110, 193),
        new TemperaturePressurePoint(-120, 185),  new TemperaturePressurePoint(-130, 178),  new TemperaturePressurePoint(-140, 171),
        new TemperaturePressurePoint(-150, 164),  new TemperaturePressurePoint(-160, 158),  new TemperaturePressurePoint(-170, 151),
        new TemperaturePressurePoint(-180, 145),  new TemperaturePressurePoint(-190, 139),  new TemperaturePressurePoint(-200, 133),
        new TemperaturePressurePoint(-210, 127),  new TemperaturePressurePoint(-220, 122),  new TemperaturePressurePoint(-230, 117),
        new TemperaturePressurePoint(-240, 112),  new TemperaturePressurePoint(-250, 107),  new TemperaturePressurePoint(-260, 102),
        new TemperaturePressurePoint(-270, 97),   new TemperaturePressurePoint(-280, 93),   new TemperaturePressurePoint(-290, 89),
        new TemperaturePressurePoint(-300, 85),   new TemperaturePressurePoint(-310, 81),   new TemperaturePressurePoint(-320, 77),
        new TemperaturePressurePoint(-330, 73),   new TemperaturePressurePoint(-340, 70),   new TemperaturePressurePoint(-350, 67),
        new TemperaturePressurePoint(-360, 63),   new TemperaturePressurePoint(-370, 60),   new TemperaturePressurePoint(-380, 57),
        new TemperaturePressurePoint(-390, 54),   new TemperaturePressurePoint(-400, 52)
    };

            /// <summary>
            /// 通过压力计算1234yf的饱和温度
            /// </summary>
            /// <param name="pressureKPa">压力值 (kPa)</param>
            /// <returns>饱和温度 (°C)</returns>
            public static double CalculateSaturationTemperature_R134a(double pressureKPa)
            {
                // 按压力排序表格（确保有序）
                var sortedTable = SaturationTable_R134a.OrderBy(p => p.PressureKPa).ToList();

                // 边界检查
                if (pressureKPa <= sortedTable[0].PressureKPa)
                    return sortedTable[0].TemperatureC;

                if (pressureKPa >= sortedTable[sortedTable.Count - 1].PressureKPa)
                    return sortedTable[sortedTable.Count - 1].TemperatureC;

                // 查找相邻的压力点
                TemperaturePressurePoint lowerPoint = sortedTable[0];
                TemperaturePressurePoint upperPoint = sortedTable[sortedTable.Count - 1];

                for (int i = 0; i < sortedTable.Count - 1; i++)
                {
                    if (pressureKPa >= sortedTable[i].PressureKPa && pressureKPa <= sortedTable[i + 1].PressureKPa)
                    {
                        lowerPoint = sortedTable[i];
                        upperPoint = sortedTable[i + 1];
                        break;
                    }
                }

                // 线性插值计算温度
                double pressureFraction = (pressureKPa - lowerPoint.PressureKPa) /
                                         (upperPoint.PressureKPa - lowerPoint.PressureKPa);

                double temperature = lowerPoint.TemperatureC +
                                   pressureFraction * (upperPoint.TemperatureC - lowerPoint.TemperatureC);

                return temperature;
            }

            /// <summary>
            /// 通过温度计算1234yf的饱和压力
            /// </summary>
            /// <param name="temperatureC">温度 (°C)</param>
            /// <returns>饱和压力 (kPa)</returns>
            public static double CalculateSaturationPressure_R134a(double temperatureC)
            {
                double temperatureTenthC = temperatureC * 10;

                // 按温度排序表格
                var sortedTable = SaturationTable_R134a.OrderBy(p => p.TemperatureTenthC).ToList();

                // 边界检查
                if (temperatureTenthC <= sortedTable[0].TemperatureTenthC)
                    return sortedTable[0].PressureKPa;

                if (temperatureTenthC >= sortedTable[sortedTable.Count - 1].TemperatureTenthC)
                    return sortedTable[sortedTable.Count - 1].PressureKPa;

                // 查找相邻的温度点
                TemperaturePressurePoint lowerPoint = sortedTable[0];
                TemperaturePressurePoint upperPoint = sortedTable[sortedTable.Count - 1];

                for (int i = 0; i < sortedTable.Count - 1; i++)
                {
                    if (temperatureTenthC >= sortedTable[i].TemperatureTenthC &&
                        temperatureTenthC <= sortedTable[i + 1].TemperatureTenthC)
                    {
                        lowerPoint = sortedTable[i];
                        upperPoint = sortedTable[i + 1];
                        break;
                    }
                }

                // 线性插值计算压力
                double tempFraction = (temperatureTenthC - lowerPoint.TemperatureTenthC) /
                                     (upperPoint.TemperatureTenthC - lowerPoint.TemperatureTenthC);

                double pressure = lowerPoint.PressureKPa +
                                tempFraction * (upperPoint.PressureKPa - lowerPoint.PressureKPa);

                return pressure;
            }

            /// <summary>
            /// 使用对数插值计算饱和温度（更高精度）
            /// </summary>
            public static double CalculateSaturationTemperatureLogInterpolation_R134a(double pressureKPa)
            {
                var sortedTable = SaturationTable_R134a.OrderBy(p => p.PressureKPa).ToList();

                if (pressureKPa <= sortedTable[0].PressureKPa)
                    return sortedTable[0].TemperatureC;

                if (pressureKPa >= sortedTable[sortedTable.Count - 1].PressureKPa)
                    return sortedTable[sortedTable.Count - 1].TemperatureC;

                // 查找相邻点
                TemperaturePressurePoint lowerPoint = sortedTable[0];
                TemperaturePressurePoint upperPoint = sortedTable[sortedTable.Count - 1];

                for (int i = 0; i < sortedTable.Count - 1; i++)
                {
                    if (pressureKPa >= sortedTable[i].PressureKPa && pressureKPa <= sortedTable[i + 1].PressureKPa)
                    {
                        lowerPoint = sortedTable[i];
                        upperPoint = sortedTable[i + 1];
                        break;
                    }
                }

                // 对数线性插值（更符合物性规律）
                double logP1 = Math.Log(lowerPoint.PressureKPa);
                double logP2 = Math.Log(upperPoint.PressureKPa);
                double logP = Math.Log(pressureKPa);

                double fraction = (logP - logP1) / (logP2 - logP1);
                double temperature = lowerPoint.TemperatureC + fraction * (upperPoint.TemperatureC - lowerPoint.TemperatureC);

                return temperature;
            }
            public static double CalculateSaturationTemperatureLogInterpolation_1234yf(double pressureKPa)
            {
                var sortedTable = SaturationTable_1234yf.OrderBy(p => p.PressureKPa).ToList();

                if (pressureKPa <= sortedTable[0].PressureKPa)
                    return sortedTable[0].TemperatureC;

                if (pressureKPa >= sortedTable[sortedTable.Count - 1].PressureKPa)
                    return sortedTable[sortedTable.Count - 1].TemperatureC;

                // 查找相邻点
                TemperaturePressurePoint lowerPoint = sortedTable[0];
                TemperaturePressurePoint upperPoint = sortedTable[sortedTable.Count - 1];

                for (int i = 0; i < sortedTable.Count - 1; i++)
                {
                    if (pressureKPa >= sortedTable[i].PressureKPa && pressureKPa <= sortedTable[i + 1].PressureKPa)
                    {
                        lowerPoint = sortedTable[i];
                        upperPoint = sortedTable[i + 1];
                        break;
                    }
                }

                // 对数线性插值（更符合物性规律）
                double logP1 = Math.Log(lowerPoint.PressureKPa);
                double logP2 = Math.Log(upperPoint.PressureKPa);
                double logP = Math.Log(pressureKPa);

                double fraction = (logP - logP1) / (logP2 - logP1);
                double temperature = lowerPoint.TemperatureC + fraction * (upperPoint.TemperatureC - lowerPoint.TemperatureC);

                return temperature;
            }
        }
    }
}
