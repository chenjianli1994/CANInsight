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

    internal static class SelfUpdater
    {
        /* 中转站固定共享文件夹路径（网络盘如 Z:\CANInsight 也可直接填） */
        private const string UpdateDir = @"\\update-server\company-share\dept\group\其他资料\project\transfer\developer\CANInsight";

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
            string stageDir = Path.Combine(Path.GetTempPath(), "CANInsight_update");
            try
            {
                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
                Directory.CreateDirectory(stageDir);
                foreach (var name in update.Files)
                {
                    File.Copy(Path.Combine(UpdateDir, name), Path.Combine(stageDir, name), true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新包下载失败：" + ex.Message, "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartReplaceAndExit(stageDir, Application.ExecutablePath);
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
        /// 生成替换批处理：等待本进程退出后将暂存目录全部文件(exe/config/DLL)覆盖到安装目录，
        /// 重启程序，随后清理暂存目录并删除自身。
        /// </summary>
        private static void StartReplaceAndExit(string stageDir, string oldExe)
        {
            string exeName = Path.GetFileName(oldExe);
            string installDir = Path.GetDirectoryName(oldExe);
            string batPath = Path.Combine(Path.GetTempPath(), "CANInsight_update.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("set \"src=" + stageDir + "\"");
            sb.AppendLine("set \"dst=" + installDir + "\"");
            sb.AppendLine("set \"exe=" + exeName + "\"");
            sb.AppendLine("set /a n=0");
            sb.AppendLine(":retry");
            // 整包复制：exe 被占用时 copy 失败，等待本进程退出后重试；其余 config/DLL 同步就位
            sb.AppendLine("copy /y \"%src%\\*\" \"%dst%\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  set /a n+=1");
            sb.AppendLine("  if %n% geq 60 goto fail");
            sb.AppendLine("  ping 127.0.0.1 -n 2 >nul");
            sb.AppendLine("  goto retry");
            sb.AppendLine(")");
            sb.AppendLine("start \"\" \"%dst%\\%exe%\" /updated");
            sb.AppendLine("rd /s /q \"%src%\"");
            sb.AppendLine("del \"%~f0\"");
            sb.AppendLine("exit");
            sb.AppendLine(":fail");
            sb.AppendLine("msg * \"CANInsight 更新失败：无法替换程序文件，请手动从共享文件夹复制新版本。\"");
            sb.AppendLine("rd /s /q \"%src%\"");
            sb.AppendLine("del \"%~f0\"");
            File.WriteAllText(batPath, sb.ToString(), Encoding.Default);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + batPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Environment.Exit(0);
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
}
