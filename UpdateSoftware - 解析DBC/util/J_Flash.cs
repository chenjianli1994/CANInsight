using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;
using System.Windows.Forms;
using System.Threading;

namespace PCAN_Client.util
{
    internal class J_Flash
    {
        internal static string J_Flash_Install_Path = "";
        public void ProgramDevice(string firmwarePath, string prjPath)
        {
            var jflashPath = "";
            //var arguments = $"-device={prjPath} -file=\"{firmwarePath}\" -program";
            //var arguments = $"-openprj\"{prjPath}\" -open\"{firmwarePath}\" -auto -exit";
            //var arguments = $"-openprj\"{prjPath}\" -open\"{firmwarePath}\" -auto";
            var arguments = "";

            if (!J_Flash_Install_Path.Contains("JFlash.exe"))
            {
                jflashPath = FindJFlashInstallPath();
                if (jflashPath != null && jflashPath.Contains("JFlash.exe"))
                {
                    J_Flash_Install_Path = jflashPath;
                    Console.WriteLine($"找到 J-Flash: {jflashPath}");
                    // 调用刷写逻辑...
                }
                else
                {
                    MessageBox.Show("未找到 J-Flash 安装路径！");
                }
            }
            else
            {
                jflashPath = J_Flash_Install_Path;
            }
            try
            {
                //arguments = $"-openprj\"{prjPath}\" -open\"{firmwarePath}\" \" -RSetType \"Core\",0x00000000 -auto";
                arguments = $"-openprj \"{prjPath}\" " +
                       $"-open \"{firmwarePath}\" " +  // 移除错误地址参数
                       "-auto ";
                       //"-RSetType \"Core Reset\" ";            // 复位参数必须放在-auto前;
                // 构建参数列表（自动处理空格）
                //string args =
                //$"-openprj=\"{prjPath}\" " +  // 关键：用等号连接
                //$"-device=S32K144 " +        // 设备型号必须精确
                //"-if=SWD " +
                //"-speed=4000 " +
                //"-jflashparam=\"-AllowSecurity\" " +
                //$"-open=\"{firmwarePath}\" " +
                //"-auto";

                var startInfo = new ProcessStartInfo
                {
                    FileName = jflashPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                // 启动进程
                using (Process proc = Process.Start(startInfo))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();

                    proc.WaitForExit(300000); // 5分钟超时

                    // 记录详细日志
                    File.WriteAllText("jflash.log", $"Exit Code: {proc.ExitCode}\nOutput:\n{output}\nError:\n{error}");

                    return;
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText("jflash_error.log", ex.ToString());
                return;
            }
            //try
            //{
            //    // 构建命令行参数
            //    string args = $"-device {prjPath} -if SWD -speed 4000 -open \"{firmwarePath}\" -auto -exit";
            //    Console.WriteLine($"args: {args}");
            //    //if (!string.IsNullOrEmpty(jlinkSerial))
            //    //    args += $" -serial {jlinkSerial}";

            //    ProcessStartInfo startInfo = new ProcessStartInfo
            //    {
            //        FileName = jflashPath,
            //        Arguments = args,
            //        UseShellExecute = false,
            //        RedirectStandardOutput = true,
            //        RedirectStandardError = true,
            //        CreateNoWindow = true,
            //        Verb = "runas" // 管理员权限
            //    };

            //    using (Process process = Process.Start(startInfo))
            //    {
            //        string output = process.StandardOutput.ReadToEnd();
            //        string error = process.StandardError.ReadToEnd();
            //        process.WaitForExit(60000); // 超时60秒

            //        // 记录日志
            //        File.WriteAllText("jflashlite.log", $"Output:\n{output}\nError:\n{error}");

            //        // 判断成功条件
            //        return ;
            //    }
            //}
            //catch (Exception ex)
            //{
            //    File.WriteAllText("jflashlite_error.log", ex.ToString());
            //    return;
            //}
            //var process = new Process
            //{
            //    StartInfo = new ProcessStartInfo
            //    {
            //        FileName = jflashPath,
            //        Arguments = arguments,
            //        UseShellExecute = false,
            //        RedirectStandardOutput = true,
            //        CreateNoWindow = true
            //    }
            //};
            //Console.WriteLine("FileName:" + jflashPath + "Arguments:" + arguments);
            //process.Start();
            //string output = process.StandardOutput.ReadToEnd();
            //process.WaitForExit();

            //if (output.Contains("Programming successful"))
            //{
            //    Console.WriteLine("烧录成功");
            //}
            //else
            //{
            //    Console.WriteLine($"烧录失败：{output}");
            //}
        }

        // 安全格式化参数（处理空格）
        private static string FormatArguments(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(a =>
                a.Contains(" ") ? $"\"{a}\"" : a));
        }
        //public bool FlashFirmwareSilently(string deviceName, string interfaceType, int speed, string firmwarePath)
        //{
        //    var jflashPath = "";

        //    if (!J_Flash_Install_Path.Contains("JFlash.exe"))
        //    {
        //        jflashPath = FindJFlashInstallPath();
        //        if (jflashPath != null && jflashPath.Contains("JFlash.exe"))
        //        {
        //            J_Flash_Install_Path = jflashPath;
        //            Console.WriteLine($"找到 J-Flash: {jflashPath}");
        //            // 调用刷写逻辑...
        //        }
        //        else
        //        {
        //            MessageBox.Show("未找到 J-Flash 安装路径！");
        //        }
        //    }
        //    else
        //    {
        //        jflashPath = J_Flash_Install_Path;
        //    }

        //    string tempScriptPath = Path.GetTempFileName();
        //    try
        //    {
        //        // 生成J-Link脚本内容
        //        string scriptContent = $@"
        //                                device {deviceName}
        //                                speed {speed}
        //                                interface {interfaceType}
        //                                connect
        //                                erase
        //                                loadfile ""{firmwarePath}""
        //                                r
        //                                exit
        //                                ";
        //        File.WriteAllText(tempScriptPath, scriptContent);

        //        // 配置进程启动参数
        //        ProcessStartInfo startInfo = new ProcessStartInfo
        //        {
        //            FileName = jflashPath, // 确保JLinkExe在系统路径中，或使用完整路径
        //            Arguments = $"-CommanderScript \"{tempScriptPath}\"",
        //            UseShellExecute = false,
        //            CreateNoWindow = false,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true
        //        };

        //        // 启动进程
        //        using (Process process = new Process { StartInfo = startInfo })
        //        {
        //            StringBuilder output = new StringBuilder();
        //            process.OutputDataReceived += (sender, e) => output.AppendLine(e.Data);

        //            process.Start();
        //            process.BeginOutputReadLine();
        //            process.WaitForExit(30000); // 设置超时30秒

        //            // 检查执行结果
        //            if (process.HasExited)
        //            {
        //                if (process.ExitCode != 0)
        //                {
        //                    Console.WriteLine($"刷写失败，错误代码：{process.ExitCode}");
        //                    return false;
        //                }
        //                // 检查输出中是否包含成功关键词
        //                if (output.ToString().Contains("Programming"))
        //                {
        //                    Console.WriteLine("固件刷写成功！");
        //                    return true;
        //                }
        //            }
        //            else
        //            {
        //                Console.WriteLine("操作超时，未完成刷写。");
        //                process.Kill();
        //            }
        //            return false;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"发生异常：{ex.Message}");
        //        return false;
        //    }
        //    finally
        //    {
        //        // 清理临时脚本文件
        //        if (File.Exists(tempScriptPath))
        //            File.Delete(tempScriptPath);
        //    }
        //}

        // 自动获取 J-Flash 安装路径
        public static string FindJFlashInstallPath()
        {
            // 方法1: 从注册表获取
            //string path = GetRegistryInstallPath();
            //if (!string.IsNullOrEmpty(path)) return path;

            // 方法2: 探测默认路径
            //path = ProbeDefaultPaths();
            //if (!string.IsNullOrEmpty(path)) return path;

            // 方法3: 用户手动选择
            return ManualSelectJFlashExe();
        }

        // 注册表查询
        private static string GetRegistryInstallPath()
        {
            try
            {
                // 64位系统兼容性处理
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry32)) // 32位程序访问方式
                {
                    // SEGGER 标准注册表路径
                    using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\SEGGER\J-Link"))
                    {
                        if (key != null)
                        {
                            string installPath = key.GetValue("InstallPath") as string;
                            if (!string.IsNullOrEmpty(installPath))
                            {
                                string fullPath = Path.Combine(installPath, "JFlash.exe");
                                if (File.Exists(fullPath)) return fullPath;
                            }
                        }
                    }

                    // 检查"添加/删除程序"中的条目
                    using (RegistryKey uninstallKey = baseKey.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            using (RegistryKey subKey = uninstallKey.OpenSubKey(subKeyName))
                            {
                                string displayName = subKey.GetValue("DisplayName") as string;
                                if (displayName != null && displayName.Contains("J-Flash"))
                                {
                                    string installLocation = subKey.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrEmpty(installLocation))
                                    {
                                        string fullPath = Path.Combine(installLocation, "JFlash.exe");
                                        if (File.Exists(fullPath)) return fullPath;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"注册表访问失败: {ex.Message}");
            }
            return null;
        }

        // 探测默认安装路径
        private static string ProbeDefaultPaths()
        {
            string[] defaultPaths = new[]
            {
            @"C:\Program Files\SEGGER\JFlash\JFlash.exe",
            @"C:\Program Files (x86)\SEGGER\JFlash\JFlash.exe"
        };

            foreach (string path in defaultPaths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // 用户手动选择
        private static string ManualSelectJFlashExe()
        {
            string selectedPath = null;
            Thread thread = new Thread(() =>
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Title = "请手动选择 JFlash.exe",
                    Filter = "JFlash Executable|JFlash.exe",
                    InitialDirectory = @"C:\Program Files\SEGGER\JLink_V796h"
                };

                // 设置线程为 STA
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    selectedPath = dialog.FileName;
                }
            });

            // 配置线程为 STA 模式
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(); // 等待线程完成

            return selectedPath;
        }
    }
}
