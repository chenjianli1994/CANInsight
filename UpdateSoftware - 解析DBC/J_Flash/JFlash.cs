using PCAN_Client.CAN_Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

using U32 = System.UInt32;  // 确保与 C 的 U32（32位无符号整型）一致

namespace PCAN_Client.J_Flash
{
    public partial class JFlash : Form
    {
        public enum JLinkInterface : uint
        {
            JTAG = 0,
            SWD = 1,
            FINE = 2,
            ICSP = 3
        }

        private ComboBox cmbJLinkSN;
        private TextBox txtDevice;
        private TextBox txtAddress;
        private Label lblFile;
        private Button btnSelectFile;
        private Button btnFlash;
        private Label lblStatus;
        public JFlash()
        {
            InitializeComponent();
        }

        private void JFlash_Load(object sender, EventArgs e)
        {
            cmbJLinkSN = new ComboBox { Width = 120, Location = new Point(10, 10) };
            txtDevice = new TextBox { Text = "S32K144", Location = new Point(150, 10) };
            txtAddress = new TextBox { Text = "0x00000000", Location = new Point(290, 10) };
            btnSelectFile = new Button { Text = "选择文件", Location = new Point(10, 50) };
            lblFile = new Label { AutoSize = true, Location = new Point(100, 55) };
            btnFlash = new Button { Text = "开始烧录", Size = new Size(80, 30), Location = new Point(400, 50) };
            lblStatus = new Label { AutoSize = true, Location = new Point(10, 90) };

            btnSelectFile.Click += btnSelectFile_Click;
            btnFlash.Click += btnFlash_Click;

            Controls.AddRange(new Control[] { cmbJLinkSN, txtDevice, txtAddress,
            btnSelectFile, lblFile, btnFlash, lblStatus });

            firmwarePath = @"C:\Users\yuanzhiwen\Desktop\SH_LP_S32K144_IAR20_CAN_CSEc_EraseKey.hex";
            lblFile.Text = Path.GetFileName(firmwarePath);
        }

        // JLinkARM.dll函数声明
        [DllImport("JLinkARM.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int JLINKARM_Open();

        [DllImport("JLinkARM.dll",
                CallingConvention = CallingConvention.Cdecl,
                CharSet = CharSet.Ansi)]
        public static extern int JLINK_Connect(string device, uint speed);

        [DllImport("JLinkARM.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int JLINK_EraseChip();

        [DllImport("JLinkARM.dll")]
        public static extern int JLINK_DownloadFile(string filePath, uint address);

        [DllImport("JLinkARM.dll")]
        public static extern void JLINKARM_Close();


        [StructLayout(LayoutKind.Sequential)]
        public struct JLinkInterfaceParam
        {
            public U32 InterfaceType;  // 使用严格对齐的类型
        }

        [DllImport("JLinkARM.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int JLINK_TIF_Select(ref JLinkInterfaceParam param);
        //// 定义准确的参数和返回类型
        //[DllImport("JLinkARM.dll",
        //    CallingConvention = CallingConvention.Cdecl,
        //    EntryPoint = "JLINK_TIF_Select",  // 显式指定函数入口点
        //    ExactSpelling = false)]           // 允许名称模糊匹配
        //public static extern int JLINK_TIF_Select(uint interfaceType);

        // 状态变量
        private string firmwarePath;
        private bool isFlashing = false;

        // 刷新J-Link设备列表
        //private void RefreshJLinkList()
        //{
        //    cmbJLinkSN.Items.Clear();
        //    // 此处需实现获取J-Link序列号列表的逻辑（可能需要调用其他DLL函数）
        //    cmbJLinkSN.Items.Add("模拟设备1");
        //    cmbJLinkSN.SelectedIndex = 0;
        //}

        // 选择固件文件
        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "固件文件|*.hex;*.bin|所有文件|*.*";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    firmwarePath = dialog.FileName;
                    lblFile.Text = Path.GetFileName(firmwarePath);
                }
            }
        }

        // 开始烧录
        private void btnFlash_Click(object sender, EventArgs e)
        {
            if (isFlashing) return;

            var thread = new Thread(() =>
            {
                isFlashing = true;
                UpdateUI(() => btnFlash.Enabled = false);

                try
                {
                    // 连接设备
                    int result = JLINKARM_Open();
                    if (result != 0) throw new Exception("连接失败");
                    Console.WriteLine($"打开结果: {result}");

                    var param = new JLinkInterfaceParam { InterfaceType = 1 };  // 1=SWD
                    int setInterfaceResult = JLINK_TIF_Select(ref param);
                    if (setInterfaceResult != 0)
                        throw new Exception($"接口设置失败: {setInterfaceResult}");

                    result = JLINK_Connect("S32K144", 4000);
                    Console.WriteLine($"连接结果: {result}");

                    result = JLINK_EraseChip();
                    Console.WriteLine($"擦除结果: {result}");
                    // 烧录文件
                    uint address = Convert.ToUInt32(txtAddress.Text, 16);
                    result = JLINK_DownloadFile(firmwarePath, address);
                    if (result < 0) throw new Exception($"烧录错误: {result}");

                    UpdateStatus("烧录成功", Color.Green);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"错误: {ex.Message}", Color.Red);
                }
                finally
                {
                    JLINKARM_Close();
                    isFlashing = false;
                    UpdateUI(() => btnFlash.Enabled = true);
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        // 线程安全更新UI
        private void UpdateUI(Action action)
        {
            if (InvokeRequired)
                Invoke(action);
            else
                action();
        }

        private void UpdateStatus(string message, Color color)
        {
            UpdateUI(() =>
            {
                lblStatus.Text = message;
                lblStatus.ForeColor = color;
            });
        }
    }
}
