using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCAN_Client.util
{
    /// <summary>
    /// 局域网自更新：启动时从公司文件中转站的固定共享文件夹读取 version.txt 比较版本，
    /// 有新版本时经用户确认后复制新 exe，再通过批处理完成替换并重启程序。
    ///
    /// 共享文件夹内放置的文件：
    ///   version.txt —— 第一行为版本号文本（与 BaseParamter.softVersion 同格式，如 V3.01.08 -- 2026-07-20），
    ///                  第二行（可选）为 exe 文件名，缺省则按本机 exe 名查找。
    ///   CANInsight.exe —— 新版程序本体。
    ///   更新说明.txt —— （可选）本次更新的内容说明，会在更新确认弹窗中展示给用户。
    /// </summary>

    internal enum UpdateDialogResult
    {
        Later,
        Update,
        Ignore
    }

    internal static partial class SelfUpdater
    {
        /* 中转站共享文件夹路径（UNC 或映射盘）来自本机私有文件 util/SelfUpdater.Local.cs：
           公司内网地址不写进源码仓（该文件已加进 .gitignore，只在本机参与编译）。
           源码仓里没有这个文件时 UpdateDir 为空串，自动更新静默跳过，"检查更新"会提示未配置。 */
        private static readonly string UpdateDir = ResolveUpdateDir();

        /// <summary>取本机私有文件里的中转站地址；无私有实现（源码仓编译）时返回空串</summary>
        private static string ResolveUpdateDir()
        {
            string dir = null;
            FillLocalUpdateDir(ref dir);
            return dir ?? "";
        }

        /// <summary>由本机私有文件实现的中转站地址填充（partial：源码仓不带实现，调用点编译期被移除）</summary>
        static partial void FillLocalUpdateDir(ref string dir);

        /* 暂存目录/替换批处理/替换日志统一按进程号命名：同一台机器多开实例同时更新时不会互相覆盖 */
        private static readonly string UpdateTempName = "CANInsight_update_" + Process.GetCurrentProcess().Id;

        /// <summary>
        /// 启动自愈:检查本机安装目录缺失的运行时散落文件(exe旁config/DLL),从共享目录补齐。
        /// 历史版本自更新只替换 exe 不更新 DLL,导致部分用户更新后因缺散落 DLL 打不开;
        /// 新版 exe 启动时先补齐再运行,未更新/已打不开的用户都能自动恢复。
        /// </summary>
        public static void EnsureRuntimeFiles()
        {
            try
            {
                /* 未配置中转站地址(源码仓编译、无本机私有文件)时直接跳过:
                   否则 Path.Combine("", "files.txt") 会退化成相对路径,误读当前目录里的同名文件 */
                if (UpdateDir.Length == 0) return;
                if (!QuickProbe(UpdateDir, 1500)) return;

                var names = new List<string>();
                string manifestFile = Path.Combine(UpdateDir, "files.txt");
                if (File.Exists(manifestFile))
                {
                    foreach (var line in File.ReadAllLines(manifestFile))
                    {
                        string name = line.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        names.Add(name);
                    }
                }
                else
                {
                    // 服务器无清单(旧发布):回退为固定运行时文件集合
                    names.AddRange(new[]
                    {
                        "CANInsight.exe.config",
                        "PCANBasic.NET.dll", "Newtonsoft.Json.dll",
                        "vxlapi_NET.dll", "binlog.dll"
                    });
                }

                string installDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(installDir)) return;

                foreach (var name in names)
                {
                    try
                    {
                        if (string.Equals(name, Path.GetFileName(Application.ExecutablePath),
                                StringComparison.OrdinalIgnoreCase)) continue; // exe 本体跳过
                        string local = Path.Combine(installDir, name);
                        if (File.Exists(local)) continue;
                        string remote = Path.Combine(UpdateDir, name);
                        if (!File.Exists(remote)) continue;
                        File.Copy(remote, local, true);
                        System.Diagnostics.Debug.WriteLine("[SelfUpdater] 自愈补齐缺失文件: " + name);
                    }
                    catch
                    {
                        // 单个文件补齐失败不影响启动,其余文件继续
                    }
                }
            }
            catch
            {
                /* 共享不可达等异常静默跳过,不影响启动 */
            }
        }

        /// <summary>程序启动时调用，后台静默检查；自动检查会尊重用户设置的忽略版本。</summary>
        public static void CheckOnStartup()
        {
            Task.Run(() =>
            {
                try
                {
                    Check(false, null);
                }
                catch
                {
                    /* 共享不可达等异常静默跳过 */
                }
            });
        }

        /// <summary>由界面主动触发版本检查；手动检查不受忽略版本设置影响。</summary>
        internal static void CheckNow(Form owner)
        {
            Task.Run(() =>
            {
                try
                {
                    Check(true, owner);
                }
                catch (Exception ex)
                {
                    InvokeOnOwner(owner, () => MessageBox.Show(owner,
                        "检查更新失败：" + ex.Message, "检查更新",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
            });
        }

        private static void Check(bool manual, Form owner)
        {
            UpdateInfo update;
            bool isLatest;
            string error;
            if (!TryReadUpdateInfo(out update, out isLatest, out error))
            {
                if (manual)
                {
                    InvokeOnOwner(owner, () => MessageBox.Show(owner,
                        "检查更新失败：" + error, "检查更新",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
                return;
            }

            if (isLatest)
            {
                if (manual)
                {
                    InvokeOnOwner(owner, () => MessageBox.Show(owner,
                        "当前已是最新版本。", "检查更新",
                        MessageBoxButtons.OK, MessageBoxIcon.Information));
                }
                return;
            }

            if (!manual && IsIgnoredVersion(update.RemoteVersionText))
            {
                return;
            }

            Action showPrompt = () => HandleUpdatePrompt(update, manual);
            if (manual)
            {
                InvokeOnOwner(owner, showPrompt);
            }
            else
            {
                showPrompt();
            }
        }

        private static bool TryReadUpdateInfo(out UpdateInfo update, out bool isLatest, out string error)
        {
            update = null;
            isLatest = false;
            error = "无法访问更新服务器。";
            try
            {
                if (UpdateDir.Length == 0)
                {
                    error = "本机未配置中转站地址（源码仓不含公司内网地址，需本机私有文件 util/SelfUpdater.Local.cs）。";
                    return false;
                }

                /* 先快速探测服务器可达性，避免不可达时 SMB 长时间挂起拖慢进程 */
                if (!QuickProbe(UpdateDir, 1500))
                {
                    return false;
                }

                string versionFile = Path.Combine(UpdateDir, "version.txt");
                if (!File.Exists(versionFile))
                {
                    error = "更新服务器上不存在 version.txt。";
                    return false;
                }

                var lines = File.ReadAllLines(versionFile);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
                {
                    error = "更新服务器上的版本信息无效。";
                    return false;
                }

                string remoteVersionText = lines[0].Trim();
                string exeName = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])
                    ? lines[1].Trim()
                    : Path.GetFileName(Application.ExecutablePath);
                if (CompareVersion(remoteVersionText, BaseParamter.softVersion) <= 0)
                {
                    isLatest = true;
                    return true;
                }

                string remoteExe = Path.Combine(UpdateDir, exeName);
                if (!File.Exists(remoteExe))
                {
                    error = "更新服务器上不存在新版程序文件：" + exeName;
                    return false;
                }

                // 整包更新清单(files.txt):exe + config + 运行时DLL。
                // 共享目录缺清单时回退为仅更新 exe(兼容旧服务器)。
                var files = new List<string>();
                string manifestFile = Path.Combine(UpdateDir, "files.txt");
                if (File.Exists(manifestFile))
                {
                    foreach (var line in File.ReadAllLines(manifestFile))
                    {
                        string name = line.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        files.Add(name);
                    }
                    foreach (var name in files)
                    {
                        if (!File.Exists(Path.Combine(UpdateDir, name)))
                        {
                            error = "更新服务器缺少文件：" + name;
                            return false;
                        }
                    }
                }
                else
                {
                    files.Add(exeName);
                }

                update = new UpdateInfo
                {
                    RemoteVersionText = remoteVersionText,
                    ExeName = exeName,
                    Files = files,
                    Notes = ReadNotes(Path.Combine(UpdateDir, "更新说明.txt"))
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void HandleUpdatePrompt(UpdateInfo update, bool manual)
        {
            UpdateDialogResult result = UpdateDialog.Show(update.RemoteVersionText, update.Notes, !manual);
            if (result == UpdateDialogResult.Ignore)
            {
                IgnoreVersion(update.RemoteVersionText);
                return;
            }
            if (result != UpdateDialogResult.Update)
            {
                return;
            }

            // 整包下载到暂存目录(清单内的 exe/config/DLL 全部下载,避免缺散落 DLL 导致新版本打不开)
            CleanStaleUpdateTempFiles();
            string stageDir = Path.Combine(Path.GetTempPath(), UpdateTempName);
            try
            {
                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
                Directory.CreateDirectory(stageDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新包下载失败：" + ex.Message, "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dlError;
            if (!DownloadFilesWithProgress(update, stageDir, out dlError))
            {
                MessageBox.Show("更新包下载失败：" + (dlError ?? "未知错误"), "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 升级包就绪:提示升级成功,用户确认后由批处理完成替换并自动重启软件
            MessageBox.Show("升级成功！\r\n点击“确定”后软件将自动重启，完成升级。", "检查更新",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            StartReplaceAndExit(stageDir, Application.ExecutablePath);
        }

        /// <summary>
        /// 带进度条下载整包到暂存目录:模态进度窗显示实时进度(按文件字节累计),
        /// 下载完成自动关闭。返回true=全部成功;失败时out error携带原因。
        /// </summary>
        private static bool DownloadFilesWithProgress(UpdateInfo update, string stageDir, out string error)
        {
            error = null;
            var srcPaths = new List<string>();
            var fileSizes = new List<long>();
            long totalBytes = 0;
            try
            {
                foreach (var name in update.Files)
                {
                    string src = Path.Combine(UpdateDir, name);
                    long len = new FileInfo(src).Length;
                    totalBytes += len;
                    srcPaths.Add(src);
                    fileSizes.Add(len);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            var dialog = new UpdateProgressDialog();
            // 强制先创建进度窗句柄:源端在本地/已缓存时下载可能瞬间完成,首个 SetProgress 的 BeginInvoke
            // 若早于句柄创建会抛异常,对话框就没人关闭,更新流程永久卡在"正在准备下载"(ControlBox=false 关不掉)
            var progressHandle = dialog.Handle;
            bool ok = false;
            string dlError = null; // lambda内捕获局部变量,避免out参数限制
            var task = Task.Run(() =>
            {
                try
                {
                    long done = 0;
                    for (int i = 0; i < update.Files.Count; i++)
                    {
                        File.Copy(srcPaths[i], Path.Combine(stageDir, update.Files[i]), true);
                        done += fileSizes[i];
                        string copiedName = update.Files[i];
                        long d = done;
                        dialog.BeginInvoke(new Action(() => dialog.SetProgress(copiedName, d, totalBytes)));
                    }
                    ok = true;
                }
                catch (Exception ex)
                {
                    dlError = ex.Message;
                }
                finally
                {
                    try { dialog.BeginInvoke(new Action(() => dialog.Close())); } catch { }
                }
            });
            dialog.ShowDialog(); // 模态泵消息直到下载完成关闭
            task.Wait();
            error = dlError;
            return ok;
        }

        private static bool IsIgnoredVersion(string remoteVersionText)
        {
            string ignored = Properties.Settings.Default.IgnoredUpdateVersion;
            if (string.IsNullOrWhiteSpace(ignored)
                || !Regex.IsMatch(remoteVersionText ?? "", @"\d+\.\d+\.\d+")
                || !Regex.IsMatch(ignored, @"\d+\.\d+\.\d+"))
            {
                return false;
            }
            return CompareVersion(remoteVersionText, ignored) == 0;
        }

        private static void IgnoreVersion(string remoteVersionText)
        {
            try
            {
                Properties.Settings.Default.IgnoredUpdateVersion = remoteVersionText;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SelfUpdater] 保存忽略版本失败：" + ex.Message);
            }
        }

        private static void InvokeOnOwner(Form owner, Action action)
        {
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
            {
                action();
                return;
            }
            try
            {
                owner.BeginInvoke(action);
            }
            catch
            {
                action();
            }
        }
        private sealed class UpdateInfo
        {
            public string RemoteVersionText;
            public string ExeName;
            public List<string> Files;   // 整包更新文件清单(相对文件名)
            public string Notes;
        }


        /// <summary>快速探测 UNC 主机的 445 端口可达性；非 UNC 路径（本地/映射盘）直接返回 true</summary>
        private static bool QuickProbe(string uncPath, int timeoutMs)
        {
            try
            {
                var m = Regex.Match(uncPath, @"^\\\\([^\\]+)");
                if (!m.Success)
                {
                    return true;
                }
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var ar = client.BeginConnect(m.Groups[1].Value, 445, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs) && client.Connected;
                    client.Close();
                    return ok;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读取更新说明，文件不存在或读取失败返回空字符串；自动识别 UTF-8/GBK 编码</summary>
        private static string ReadNotes(string notesFile)
        {
            try
            {
                if (!File.Exists(notesFile))
                {
                    return "";
                }
                byte[] bytes = File.ReadAllBytes(notesFile);
                /* 有 BOM 直接按 BOM 识别 */
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                }
                /* 无 BOM：先按 UTF-8 严格解码，失败则按 GBK */
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>从版本文本中提取 x.y.z 数字版本进行比较，返回 &gt;0 表示 remote 更新</summary>
        private static int CompareVersion(string remote, string local)
        {
            var regex = new Regex(@"(\d+)\.(\d+)\.(\d+)");
            var mRemote = regex.Match(remote);
            var mLocal = regex.Match(local);
            if (!mRemote.Success || !mLocal.Success)
            {
                return 0;
            }
            var vRemote = new Version(int.Parse(mRemote.Groups[1].Value),
                                      int.Parse(mRemote.Groups[2].Value),
                                      int.Parse(mRemote.Groups[3].Value));
            var vLocal = new Version(int.Parse(mLocal.Groups[1].Value),
                                     int.Parse(mLocal.Groups[2].Value),
                                     int.Parse(mLocal.Groups[3].Value));
            return vRemote.CompareTo(vLocal);
        }

        /// <summary>
        /// 清理历史更新的临时残留（上次更新中断留下的暂存目录/批处理/日志）。
        /// 只清 1 小时前的：并发实例正在使用的文件绝不动。
        /// </summary>
        private static void CleanStaleUpdateTempFiles()
        {
            try
            {
                string temp = Path.GetTempPath();
                DateTime cutoff = DateTime.Now.AddHours(-1);
                foreach (var dir in Directory.GetDirectories(temp, "CANInsight_update*"))
                {
                    try
                    {
                        if (string.Equals(Path.GetFileName(dir), UpdateTempName, StringComparison.OrdinalIgnoreCase)
                            || Directory.GetLastWriteTime(dir) > cutoff)
                        {
                            continue;
                        }
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                        /* 被占用等异常跳过,不影响本次更新 */
                    }
                }
                foreach (var pattern in new[] { "CANInsight_update*.bat", "CANInsight_update*.log" })
                {
                    foreach (var file in Directory.GetFiles(temp, pattern))
                    {
                        try
                        {
                            if (string.Equals(Path.GetFileNameWithoutExtension(file), UpdateTempName, StringComparison.OrdinalIgnoreCase)
                                || File.GetLastWriteTime(file) > cutoff)
                            {
                                continue;
                            }
                            File.Delete(file);
                        }
                        catch
                        {
                            /* 被占用等异常跳过,不影响本次更新 */
                        }
                    }
                }
            }
            catch
            {
                /* 清理只是卫生工作 */
            }
        }

        /// <summary>
        /// 生成替换批处理：等待本进程退出后将暂存目录全部文件(exe/config/DLL)覆盖到安装目录，
        /// 重启程序，随后清理暂存目录并删除自身；失败分支同样重启程序，由程序自身弹提示。
        /// </summary>
        private static void StartReplaceAndExit(string stageDir, string oldExe)
        {
            string temp = Path.GetTempPath();
            string batPath = Path.Combine(temp, UpdateTempName + ".bat");
            string logPath = Path.Combine(temp, UpdateTempName + ".log");
            File.WriteAllText(batPath,
                BuildReplaceScript(stageDir, Path.GetDirectoryName(oldExe), Path.GetFileName(oldExe), logPath),
                Encoding.Default);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + batPath + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                /* 起不了替换批处理就别退出:留着界面,用户至少还能继续用手动方式更新 */
                MessageBox.Show("无法启动升级程序：" + ex.Message + "\r\n请手动从中转站复制新版本。", "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Environment.Exit(0);
        }

        /// <summary>
        /// 构造替换批处理文本。判断成败以“程序本体是否已换成新版”为准：
        /// copy 整包时个别散落文件被占用（杀软扫描、残留进程、另一实例等）不应把已经换好 exe 的更新误报成失败，
        /// 也绝不能因为误报失败而不重启程序（用户会以为软件自己关了）。
        /// </summary>
        internal static string BuildReplaceScript(string stageDir, string installDir, string exeName, string logPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("set \"src=" + stageDir + "\"");
            sb.AppendLine("set \"dst=" + installDir + "\"");
            sb.AppendLine("set \"exe=" + exeName + "\"");
            sb.AppendLine("set \"log=" + logPath + "\"");
            sb.AppendLine("set /a n=0");
            sb.AppendLine(":retry");
            // 整包复制：exe 被占用时 copy 失败，等待本进程退出后重试；其余 config/DLL 同步就位。
            // 输出进日志：真出问题时能看出是哪个文件、什么原因没落地。
            sb.AppendLine("copy /y \"%src%\\*\" \"%dst%\" >\"%log%\" 2>&1");
            sb.AppendLine("if not errorlevel 1 goto ok");
            sb.AppendLine("set /a n+=1");
            sb.AppendLine("if %n% geq 30 goto verify");
            sb.AppendLine("ping 127.0.0.1 -n 2 >nul");
            sb.AppendLine("goto retry");
            sb.AppendLine(":verify");
            // 整包始终有文件没落地：以程序本体逐字节比对裁决（散落 DLL 缺了由程序启动自愈补齐）
            sb.AppendLine("fc /b \"%src%\\%exe%\" \"%dst%\\%exe%\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine(":ok");
            sb.AppendLine("start \"\" \"%dst%\\%exe%\" /updated");
            sb.AppendLine("rd /s /q \"%src%\"");
            sb.AppendLine("del \"%log%\"");
            sb.AppendLine("goto cleanup");
            sb.AppendLine(":fail");
            // 失败也要把程序拉起来（否则用户看到的是软件自己关了），失败提示留在 %log% 里供排查
            sb.AppendLine("start \"\" \"%dst%\\%exe%\" /updatefailed");
            sb.AppendLine("rd /s /q \"%src%\"");
            sb.AppendLine(":cleanup");
            // 自删除只能放最后一行:(goto) 让 cmd 先释放批处理文件再删,否则 del 之后的语句都不会再执行
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            return sb.ToString();
        }
    }

    /// <summary>更新确认对话框：自动检查支持忽略版本，手动检查只提供更新或不更新。</summary>
    internal class UpdateDialog : Form
    {
        private UpdateDialog(string remoteVersionText, string notes, bool allowIgnore)
        {
            this.Text = "检查更新";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.ClientSize = new Size(560, 320);
            this.Font = new Font("微软雅黑", 9F);

            var labelTitle = new Label
            {
                Text = "发现新版本，是否立即更新？",
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 12)
            };
            var labelVersion = new Label
            {
                Text = "当前版本：" + BaseParamter.softVersion + "\r\n最新版本：" + remoteVersionText,
                AutoSize = true,
                Location = new Point(15, 42)
            };
            var textNotes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Text = string.IsNullOrWhiteSpace(notes) ? "（本次更新未提供说明）" : notes,
                Location = new Point(15, 82),
                Size = new Size(530, 185)
            };
            var buttonUpdate = new Button
            {
                Text = "立即更新",
                DialogResult = DialogResult.OK,
                Size = new Size(100, 30),
                Location = allowIgnore ? new Point(235, 278) : new Point(345, 278)
            };
            var buttonIgnore = new Button
            {
                Text = "忽略此版本",
                DialogResult = DialogResult.Ignore,
                Size = new Size(100, 30),
                Location = new Point(345, 278),
                Visible = allowIgnore
            };
            var buttonLater = new Button
            {
                Text = allowIgnore ? "稍后再说" : "不更新",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 30),
                Location = new Point(455, 278)
            };

            this.Controls.Add(labelTitle);
            this.Controls.Add(labelVersion);
            this.Controls.Add(textNotes);
            this.Controls.Add(buttonUpdate);
            this.Controls.Add(buttonIgnore);
            this.Controls.Add(buttonLater);
            this.AcceptButton = buttonUpdate;
            this.CancelButton = buttonLater;
        }

        /// <summary>弹出更新确认框，返回用户选择。</summary>
        public static UpdateDialogResult Show(string remoteVersionText, string notes, bool allowIgnore)
        {
            using (var dialog = new UpdateDialog(remoteVersionText, notes, allowIgnore))
            {
                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK) return UpdateDialogResult.Update;
                if (result == DialogResult.Ignore) return UpdateDialogResult.Ignore;
                return UpdateDialogResult.Later;
            }
        }
    }

    /// <summary>在线升级下载进度对话框:模态显示下载进度,下载中不可关闭,完成后由调用方关闭并提示重启。</summary>
    internal class UpdateProgressDialog : Form
    {
        private ProgressBar _progressBar;
        private Label _label;

        public UpdateProgressDialog()
        {
            this.Text = "在线升级";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ControlBox = false; // 下载中不可中断
            this.TopMost = true;
            this.ClientSize = new Size(420, 110);
            this.Font = new Font("微软雅黑", 9F);

            _label = new Label
            {
                Text = "正在准备下载...",
                Location = new Point(15, 12),
                Size = new Size(390, 22)
            };
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Location = new Point(15, 44),
                Size = new Size(390, 18)
            };
            this.Controls.Add(_label);
            this.Controls.Add(_progressBar);
        }

        /// <summary>更新下载进度(按已下载字节/总字节计算百分比),必须在对话框所在线程调用</summary>
        public void SetProgress(string fileName, long doneBytes, long totalBytes)
        {
            int pct = totalBytes > 0 ? (int)(doneBytes * 100 / totalBytes) : 0;
            _progressBar.Value = Math.Min(100, Math.Max(0, pct));
            _label.Text = string.Format("正在下载: {0}  ({1:P0})", fileName,
                totalBytes > 0 ? (double)doneBytes / totalBytes : 0.0);
        }
    }
}
