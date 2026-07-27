using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PCAN_Client
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Byte[] assemblyData = new Byte[1];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            //原创来自 www.luofenming.com
            //初始化时添加下面代码 
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {//注意WindowsFormsApplication1 这个是主程序的命名空间
                string resourceName = "PCAN_Client.Lib." + new AssemblyName(resolveArgs.Name).Name + ".dll";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // 调试输出找不到的资源
                        Console.WriteLine($"Embedded resource not found: {resourceName}");
                        return null;
                    }

                    // 3. 安全读取流
                    assemblyData = new Byte[stream.Length];
                    stream.Read(assemblyData, 0, assemblyData.Length);
                    return Assembly.Load(assemblyData);
                }
            };
            //ExtractEmbeddedDLL();
            HideRelatedFiles();
            /* 首次打开或版本更新后首次打开：弹窗提示（/updated 为更新批处理重启时携带的参数） */
            bool justUpdated = false;
            foreach (var arg in args)
            {
                if (arg.Equals("/updated", StringComparison.OrdinalIgnoreCase))
                {
                    justUpdated = true;
                    break;
                }
            }
            if (justUpdated || !Properties.Settings.Default.NoticeShownVersion.Equals(BaseParamter.softVersion))
            {
                string notice = "1、使用过程中发现问题请内部沟通工具联系developer进行反馈\r\n" +
                                "2、如需要更新软件需要连接上公司内网，确认可以访问\\\\update-server地址后重新打开此软件";
                if (justUpdated)
                {
                    MessageBox.Show("更新完成！当前版本：" + BaseParamter.softVersion + "\r\n\r\n" + notice,
                        "更新成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(notice, "温馨提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                Properties.Settings.Default.NoticeShownVersion = BaseParamter.softVersion;
                Properties.Settings.Default.Save();
            }
            util.SelfUpdater.CheckOnStartup(); /* 启动时后台检查局域网共享文件夹中的新版本 */
            if (BaseParamter.Pre_b_OnlyUpdateFlag)
            {
                Application.Run(new Update.update());
            }
            else
            {
                // 主窗口以最小化+不显示任务栏按钮的方式启动:窗口全程不可见,
                // 避免"先显示Main→Load中打开绘图窗口→再Hide"造成的启动闪窗。
                // Load事件照常触发(绘图窗口正常打开);"报文列表"按钮唤出时恢复Normal即可。
                var mainForm = new Main();
                mainForm.WindowState = FormWindowState.Minimized;
                mainForm.ShowInTaskbar = false;
                Application.Run(mainForm);
            }
        }
        public static void ExtractEmbeddedDLL()
        {
            string dllName = "JLinkARM.dll";
            string tempPath = Path.Combine(Path.GetTempPath(), dllName);

            string resourceName = "PCAN_Client.Lib." + dllName;
            // 从资源中读取 DLL
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(dllName))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(tempPath, buffer);
            }

            // 设置 DLL 加载路径
            Environment.SetEnvironmentVariable("PATH",
                $"{Environment.GetEnvironmentVariable("PATH")};{Path.GetTempPath()}");
        }

        /// <summary>
        /// 自动隐藏与当前可执行文件相关的配置文件(executable.config)和PDB文件(executable.pdb)
        /// </summary>
        public static void HideRelatedFiles()
        {
            try
            {
                string exePath = Application.ExecutablePath; // 获取当前可执行文件完整路径
                string exeName = Path.GetFileNameWithoutExtension(exePath);

                if (string.IsNullOrEmpty(exeName))
                {
                    // 如果无法获取可执行文件名，使用默认命名模式
                    exeName = "CANInsight";
                    Console.WriteLine($"使用默认文件名: {exeName}");
                }

                // 构建文件路径
                string configPath = Path.Combine(Path.GetDirectoryName(exePath), $"{exeName}.exe.config");
                string pdbPath = Path.Combine(Path.GetDirectoryName(exePath), $"{exeName}.pdb");

                Console.WriteLine($"当前可执行文件: {Path.GetFileName(exePath)}");
                Console.WriteLine($"配置文件路径: {configPath}");
                Console.WriteLine($"PDB文件路径: {pdbPath}");

                // 设置隐藏属性
                SetFileAttribute(configPath, FileAttributes.Hidden);
                SetFileAttribute(pdbPath, FileAttributes.Hidden);
            }
            catch (Exception ex)
            {
                // 在实际应用中，应使用日志系统记录错误
                Console.WriteLine($"隐藏相关文件时出错: {ex.Message}");
                Debug.WriteLine($"隐藏文件错误: {ex}");
            }
        }

        /// <summary>
        /// 设置文件属性（如果文件存在）
        /// </summary>
        private static void SetFileAttribute(string filePath, FileAttributes attribute)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"文件不存在: {filePath}");
                return;
            }

            try
            {
                FileAttributes currentAttributes = File.GetAttributes(filePath);

                // 检查属性是否已设置
                if ((currentAttributes & attribute) == attribute)
                {
                    Console.WriteLine($"文件已具有 {attribute} 属性: {filePath}");
                    return;
                }

                // 添加属性
                File.SetAttributes(filePath, currentAttributes | attribute);
                Console.WriteLine($"成功设置 {attribute} 属性: {Path.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"没有权限设置属性: {filePath}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"文件访问错误: {ioEx.Message}");
            }
        }

        /// <summary>
        /// 显示所有相关文件（取消隐藏属性）
        /// </summary>
        public static void ShowRelatedFiles()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string exeName = Path.GetFileNameWithoutExtension(exePath);

                if (string.IsNullOrEmpty(exeName))
                {
                    exeName = "CANInsight";
                }

                string configPath = Path.Combine(Path.GetDirectoryName(exePath), $"{exeName}.exe.config");
                string pdbPath = Path.Combine(Path.GetDirectoryName(exePath), $"{exeName}.pdb");

                // 取消隐藏属性
                RemoveFileAttribute(configPath, FileAttributes.Hidden);
                RemoveFileAttribute(pdbPath, FileAttributes.Hidden);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"显示相关文件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除文件属性（如果文件存在）
        /// </summary>
        private static void RemoveFileAttribute(string filePath, FileAttributes attribute)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"文件不存在: {filePath}");
                return;
            }

            try
            {
                FileAttributes currentAttributes = File.GetAttributes(filePath);

                // 检查属性是否已设置
                if ((currentAttributes & attribute) != attribute)
                {
                    Console.WriteLine($"文件不具有 {attribute} 属性: {filePath}");
                    return;
                }

                // 移除属性
                File.SetAttributes(filePath, currentAttributes & ~attribute);
                Console.WriteLine($"成功移除 {attribute} 属性: {Path.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"没有权限修改属性: {filePath}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"文件访问错误: {ioEx.Message}");
            }
        }
    }
}
