// ================================================================================================
// L-Mechrevo 启动壳（LmLauncherStub）—— 2026-10-13 新增
// ================================================================================================
// 目的（用户原话）：「只需要在 LM 启动的时候如果没有 .net10 再提示就行」。
//
// 背景：真正的 L-Mechrevo.exe 是 .NET 10 的 apphost（source\app.manifest = requireAdministrator）。
//   目标机缺 .NET 10 桌面运行时时，apphost 在托管代码跑起来【之前】就失败，弹出焊死在 exe 里的
//   【英文】对话框（改不了），程序自己没有任何机会提示。所以这个壳必须：
//     ① 不依赖 .NET 10（本壳跑在 .NET Framework 4.8.1 上，Win10 2004+ / Win11 系统自带）；
//     ② 缺运行时 -> 中文说明 + 【可框选复制】的完整下载 URL，并尽力打开浏览器，然后退出；
//     ③ 有运行时 -> 原样透传命令行参数，启动同目录的 L-Mechrevo.exe。
//
// 本文件不参与 source\L-Mechrevo.csproj 构建；主程序逻辑一字未动。
// ⚠️ 文件编码：UTF-8【带 BOM】，csc 才稳定地把中文字面量当 UTF-8（见 打包须知 / 构建命令）。
// ================================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LmLauncherStub
{
    internal static class Program
    {
        /// <summary>真正的程序（与本壳同目录）。</summary>
        private const string TargetExeName = "L-Mechrevo.exe";

        /// <summary>
        /// 唯一对外地址：x64 Windows Desktop Runtime 10 官方【直链】（与 install_script.iss 的
        /// Net10DownloadDirectUrl 逐字一致）=> 浏览器打开即【直接下载】安装包。
        /// ⚠️ 2026-10-13：用户已在自己浏览器里实测该短链能正常触发下载；本机只读 HEAD 亦为
        /// 200 + application/octet-stream + 60,032,984 B（302 终址 builds.dotnet.microsoft.com/.../10.0.12）。
        /// ⛔ 界面上【只放这一条】：先前同时显示"直链 + 下载页"两条挤在窄框里，用户反馈「太紧有裁切」。
        /// </summary>
        private const string RuntimeUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

        /// <summary>与 install_script.iss 的 NetSharedFxKey 一致：官方安装器写的 sharedfx 键（版本号是【值名】）。</summary>
        private const string SharedFxKey =
            @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App";

        /// <summary>与 install_script.iss 的 NetVersionValueNames 一致的候选值名（本壳另有子键枚举兜底，所以这张表只是补充）。</summary>
        private static readonly string[] VersionValueNames =
        {
            "10.0.11", "10.0.10", "10.0.9", "10.0.8", "10.0.7", "10.0.6", "10.0.5"
        };

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string target = Path.Combine(appDir, TargetExeName);

            // 同一套判据：本地/注册表里有没有 10.x 的 WindowsDesktop 共享框架。
            if (!IsNetDesktop10Installed())
            {
                ShowMissingRuntimeDialog(target);
                return 2;
            }

            return LaunchTarget(target);
        }

        // ------------------------------------------------------------------------------------------
        // 一、.NET 10 桌面运行时检测（判据与 install_script.iss:693 IsNetDesktop10Installed 一致）
        // ------------------------------------------------------------------------------------------

        /// <summary>共享框架根目录候选：{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App 等。</summary>
        private static IEnumerable<string> SharedFrameworkRoots()
        {
            var candidates = new List<string>();
            string pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");

            foreach (string root in new[] { pf64, pf32 })
            {
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, @"dotnet\shared\Microsoft.WindowsDesktop.App"));
            }
            if (!string.IsNullOrEmpty(fromEnv))
                candidates.Add(Path.Combine(fromEnv, @"shared\Microsoft.WindowsDesktop.App"));

            return candidates;
        }

        /// <summary>目录下是否有以 "10." 开头的子目录（与 iss 的 NetTenSubDirExists 同义）。</summary>
        private static bool HasTenSubDir(string baseDir)
        {
            try
            {
                if (!Directory.Exists(baseDir))
                    return false;
                foreach (string dir in Directory.GetDirectories(baseDir))
                {
                    string name = Path.GetFileName(dir);
                    if (IsTenOrNewerVersion(name))
                        return true;
                }
            }
            catch (Exception)
            {
                // 权限/IO 异常一律当作「这一次没探到」，继续看下一个判据；⛔ 不因为探测失败就报缺运行时。
            }
            return false;
        }

        /// <summary>
        /// 名字里的【大版本号】是否 >= 10（与 install_script.iss 的 NetIsTenOrNewerVersion 同义）。
        /// ⚠️ 2026-10-13（用户口径「如果用户装了能支持运行的其他版本也不提示」）：
        ///   程序 runtimeconfig 要求 Microsoft.WindowsDesktop.App >= 10.0.0 且未写 rollForward
        ///   （.NET 缺省 = Minor，允许向上扩到更高主版本）=> 10.x / 11.x / 12.x 都算「能跑」；
        ///   ⛔ 6.x / 7.x / 8.x / 9.x 仍【不算】满足（net10.0 程序装 8.x 起不来）。
        /// </summary>
        private static bool IsTenOrNewerVersion(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            int i = 0;
            while (i < name.Length && (name[i] < '0' || name[i] > '9'))
                i++;
            int start = i;
            while (i < name.Length && name[i] >= '0' && name[i] <= '9')
                i++;
            if (i <= start)
                return false;
            int major;
            if (!int.TryParse(name.Substring(start, i - start), out major))
                return false;
            return major >= 10;
        }

        /// <summary>
        /// 注册表 sharedfx 键：先枚举子键名，再探候选值名（值本身就是版本号，如 10.0.11）。
        /// ⚠️ 绝不因为「键存在」就判真 —— 装过 6/8 的机器同样有这个键。
        /// </summary>
        private static bool HasTenInSharedFxKey()
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = hklm.OpenSubKey(SharedFxKey))
                {
                    if (key == null)
                        return false;

                    foreach (string sub in key.GetSubKeyNames())
                    {
                        if (IsTenOrNewerVersion(sub))
                            return true;
                    }
                    foreach (string valueName in VersionValueNames)
                    {
                        object v = key.GetValue(valueName);
                        string s = v as string;
                        if (s != null && IsTenOrNewerVersion(s))
                            return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>命中顺序同 iss：① 10.x 共享框架目录 ② sharedfx 键的 10.x 子键/值名。</summary>
        private static bool IsNetDesktop10Installed()
        {
            foreach (string root in SharedFrameworkRoots())
            {
                if (HasTenSubDir(root))
                    return true;
            }
            return HasTenInSharedFxKey();
        }

        // ------------------------------------------------------------------------------------------
        // 二、缺运行时：中文提示（唯一一条【可直接下载】的官方直链；够宽的只读多行文本框；不裁切）
        // ------------------------------------------------------------------------------------------
        // ⚠️ 2026-10-13（用户反馈「启动壳 UI 太紧有裁切」）：旧版把【直链 + 下载页】两条网址塞进
        //    两个 470px 的【单行】文本框，长直链被横向截断、说明文字也被固定 Size 裁掉。
        //    现在：① 只显示一条直链；② 只读【多行】文本框，宽度按文本实测宽度给足（整条一行放得下）；
        //    ③ 所有 Label 都 AutoSize（MaximumSize 限宽 => 自动换行，绝不用固定 Size 硬裁）；
        //    ④ 窗体尺寸由内容实测算出（ClientSize 按需给足）；⑤ 按钮 打开下载 / 复制地址 / 关闭。
        // ⚠️ 构造逻辑放在 BuildMissingRuntimeDialog() 里 => 无窗 harness 可以
        //    CreateControl + PerformLayout + DrawToBitmap 实测有没有裁切（⛔ 不需要 UAC、⛔ 不显示窗口）。

        private static readonly Font UiFont = new Font("Microsoft YaHei UI", 9f);
        private static readonly Font UiBoldFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        private static readonly Font UrlFont = new Font("Consolas", 10f);

        /// <summary>按文本 + 字体 + 最大宽度实测尺寸（WordBreak 换行等价于 Label 的 AutoSize 行为）。</summary>
        private static Size Measure(string text, Font font, int maxWidth)
        {
            return TextRenderer.MeasureText(text, font, new Size(maxWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }

        /// <summary>内部布局参数（harness 也读这些常数来判断"给够了没有"）。</summary>
        internal const int ContentMaxWidth = 780;
        internal const int PadPad = 20;

        /// <summary>
        /// 构造「缺运行时」对话框（不显示）。out 参数回传网址文本框，便于调用方聚焦/全选。
        /// </summary>
        internal static Form BuildMissingRuntimeDialog(bool opened, out TextBox urlBox)
        {
            // ---- 网址文本框：宽度按【整条直链一行放得下】实测给足（⛔ 不横向裁切）----
            Size urlTextSize = TextRenderer.MeasureText(RuntimeUrl, UrlFont);
            int boxWidth = urlTextSize.Width + 24;            // 左右内边距
            int boxHeight = urlTextSize.Height + 10;          // 上下内边距（单行显示）

            const int Pad = 20;
            int contentWidth = Math.Max(boxWidth, ContentMaxWidth);

            // ⚠️ 2026-10-13（第二轮修正）：⛔ 不再用手工 Location + 上一控件 .Bottom 定位
            //   （那种写法在 AutoSize 生效前读到的是未测量的高度 => 会重叠，实测过）。
            //   改成 FlowLayoutPanel(TopDown) + 全部 AutoSize => 由布局引擎算高，绝不重叠/裁切。
            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(Pad),
                Margin = new Padding(0)
            };

            var title = new Label
            {
                Text = "无法启动 L-Mechrevo：缺少 .NET 桌面运行时 10（x64）",
                Font = UiBoldFont,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, 8)
            };

            var body = new Label
            {
                Text = "本程序需要 64 位 .NET 桌面运行时 10（Microsoft Windows Desktop Runtime 10）。\r\n"
                     + "当前系统里没有检测到，所以程序还没有启动。请先安装它，装好后重新打开 L-Mechrevo。",
                Font = UiFont,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, 10)
            };

            var hint = new Label
            {
                Text = "安装方法：点「打开下载」将【直接下载】官方安装包（x64）；"
                     + "也可以点「复制地址」，把下面这条直链粘到浏览器里打开。",
                Font = UiFont,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, 10)
            };

            var urlLabel = new Label
            {
                Text = "官方直链（可直接下载；下面文本框里的地址只读，但可以整行框选复制）：",
                Font = UiFont,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, 6)
            };

            var box = new TextBox
            {
                Text = RuntimeUrl,
                ReadOnly = true,
                Multiline = true,          // 多行 + 不换行 => 整条一行，宽度已按实测给足
                WordWrap = false,
                ScrollBars = ScrollBars.None,
                TabStop = true,
                Font = UrlFont,
                BackColor = SystemColors.Window,
                Size = new Size(boxWidth, boxHeight),
                MinimumSize = new Size(0, boxHeight),
                Margin = new Padding(0, 0, 0, 14)
            };
            urlBox = box;

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 12)
            };

            var openButton = new Button
            {
                Text = "打开下载",
                Font = UiFont,
                Size = new Size(120, 34),
                Margin = new Padding(0, 0, 12, 0)
            };
            openButton.Click += delegate { TryOpenBrowser(); };

            var copyButton = new Button
            {
                Text = "复制地址",
                Font = UiFont,
                Size = new Size(120, 34),
                Margin = new Padding(0, 0, 12, 0)
            };
            copyButton.Click += delegate
            {
                try
                {
                    Clipboard.SetText(RuntimeUrl);
                    copyButton.Text = "已复制";
                }
                catch (Exception)
                {
                    // 剪贴板被别的进程占用等：让用户自己框选复制，⛔ 不弹二次错误框。
                    box.Focus();
                    box.SelectAll();
                }
            };

            var closeButton = new Button
            {
                Text = "关闭",
                Font = UiFont,
                DialogResult = DialogResult.Cancel,
                Size = new Size(120, 34),
                Margin = new Padding(0)
            };

            buttons.Controls.Add(openButton);
            buttons.Controls.Add(copyButton);
            buttons.Controls.Add(closeButton);

            // ⚠️ 2026-10-13（本批修复）：浏览器【确实没起来】时（opened == false）必须给可见提示，
            //   而不是只说一句"没成功"：明确告诉用户"点「复制地址」就会复制，粘到浏览器即可"。
            string statusText = opened
                ? "（已尝试以你的身份打开浏览器；没弹出就直接点「复制地址」，再用浏览器打开。）"
                : "（自动打开浏览器没成功：网址已可一键复制 —— 点「复制地址」，再粘到浏览器地址栏打开即可下载。）";
            var status = new Label
            {
                Text = statusText,
                Font = UiFont,
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, 10)
            };

            var foot = new Label
            {
                Text = "⚠️ 装好运行时之前启动 L-Mechrevo，只会看到程序入口自带的英文 .NET 报错窗口"
                     + "（那行字焊在 exe 里，改不了）；所以请先按上面的直链把运行时装好。"
                     + "\r\n排查用日志（若「打开下载」没反应，可把这个文件发给作者）："
                     + "%TEMP%\\L-Mechrevo-Launcher.log",
                Font = UiFont,
                ForeColor = Color.FromArgb(160, 60, 0),
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0)
            };

            root.Controls.Add(title);
            root.Controls.Add(body);
            root.Controls.Add(hint);
            root.Controls.Add(urlLabel);
            root.Controls.Add(box);
            root.Controls.Add(buttons);
            root.Controls.Add(status);
            root.Controls.Add(foot);

            var form = new Form
            {
                Text = "L-Mechrevo —— 缺少 .NET 10 桌面运行时",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = true,
                AutoScaleMode = AutoScaleMode.None,
                Font = UiFont,
                // ⛔ 不写死像素：窗体客户区 = root 面板【实测的首选尺寸】（PreferredSize）
                //   => 内容需要多少就给多少，绝不出现"窗体比内容小几像素、最后一行被切"。
                AutoSize = false,
                Padding = new Padding(0)
            };
            form.Controls.Add(root);
            // ⚠️ 必须在加完子控件之后再问 PreferredSize（否则拿到的是未测量的默认值）。
            Size content = root.PreferredSize;
            // +8/  +16：给最底下一行留一点余量（实测渲染时字体度量比布局测量略高），确保不出现贴边裁切。
            form.ClientSize = new Size(content.Width + 8, content.Height + 16);
            form.AcceptButton = closeButton;
            form.CancelButton = closeButton;
            return form;
        }

        private static void ShowMissingRuntimeDialog(string targetExePath)
        {
            bool opened = TryOpenBrowser();
            TextBox urlBox;
            using (Form form = BuildMissingRuntimeDialog(opened, out urlBox))
            {
                form.ShowDialog();
            }
        }

        /// <summary>尽力用默认浏览器打开【官方直链】（浏览器随即开始下载；失败不抛，返回 false 由界面给出可见提示）。</summary>
        private static bool TryOpenBrowser()
        {
            // ⚠️ 2026-10-13（本批修复）：原来这里只有一句
            //   Process.Start(new ProcessStartInfo(RuntimeUrl) { UseShellExecute = true });
            //   而本壳的清单是 requireAdministrator（tools\LmLauncherStub\app.manifest:11）：
            //   在【提权进程】里让 shell 打开 URL，常常【不抛异常也不报错，就是什么都不发生】
            //   （默认浏览器与 shell 之间的关联走的是【非提权】的 shell 用户上下文；提权上下文里
            //    要么没有关联、要么被 UIPI 挡下）=> 用户看到的就是「点了没反应」。
            //   => 现在交给 UrlOpener（首选 explorer.exe = 由 shell 用户代理降权打开），且失败有可见提示。
            //   ⚠️ 顺带把「提权句柄泄漏」也去掉了：旧写法拿到 Process 对象后从不解引用。
            return UrlOpener.TryOpen(RuntimeUrl);
        }

        // ------------------------------------------------------------------------------------------
        // 三、有运行时：透传原始命令行参数，以【同一权限】启动真正的 L-Mechrevo.exe
        // ------------------------------------------------------------------------------------------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetCommandLineW();

        /// <summary>
        /// 取「本壳自己的 argv[0] 之后」的原始命令尾串 —— 直接来自系统原始命令行，
        /// 引号/空格原样保留（用 Environment.GetCommandLineArgs 再拼回去会丢掉原始引号形态）。
        /// </summary>
        private static string GetRawCommandTail()
        {
            string raw;
            try
            {
                raw = Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
            if (raw.Length == 0)
                return string.Empty;

            int i = 0;
            while (i < raw.Length && (raw[i] == ' ' || raw[i] == '\t'))
                i++;
            if (i >= raw.Length)
                return string.Empty;

            if (raw[i] == '"')
            {
                i++;
                while (i < raw.Length && raw[i] != '"')
                    i++;
                if (i < raw.Length)
                    i++; // 跳过结尾引号
            }
            else
            {
                while (i < raw.Length && raw[i] != ' ' && raw[i] != '\t')
                    i++;
            }

            while (i < raw.Length && (raw[i] == ' ' || raw[i] == '\t'))
                i++;

            return i < raw.Length ? raw.Substring(i) : string.Empty;
        }

        private static int LaunchTarget(string targetExePath)
        {
            if (!File.Exists(targetExePath))
            {
                MessageBox.Show(
                    "找不到程序主文件：" + Environment.NewLine + targetExePath + Environment.NewLine + Environment.NewLine
                    + "安装可能不完整，请重新运行 L-Mechrevo 安装程序。",
                    "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            try
            {
                var psi = new ProcessStartInfo(targetExePath)
                {
                    // 原样透传参数；没有参数时为空串（等价于不传）。
                    Arguments = GetRawCommandTail(),
                    WorkingDirectory = Path.GetDirectoryName(targetExePath),
                    // UseShellExecute 让子进程以本壳【同一权限】启动（本壳 requireAdministrator =>
                    // 子进程同权限，不会再弹一次 UAC）。
                    UseShellExecute = true
                };
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "启动 L-Mechrevo 失败：" + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine
                    + targetExePath,
                    "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 4;
            }
        }
    }

    // ================================================================================================
    // 四、以【用户身份】打开网址（r23 新增；r24 修「explorer 路径写成 System32」+ 加显式默认浏览器档）
    // ================================================================================================
    /// <summary>
    /// 打开 URL 的档位链。⚠️ r24 起本壳的清单是 asInvoker（tools\LmLauncherStub\app.manifest）：
    /// 本壳只做「查 .NET 运行时 + 启动主程序」，真正的 L-Mechrevo.exe 自带 requireAdministrator
    /// 清单会自己提权 => 壳不必提权。于是第 1 档（shell 关联）就能以【用户身份】打开浏览器，
    /// 提权/降权那一整摊麻烦随之消失（用户报的「点了没反应」根因之一就是提权进程里让 shell 开 URL）。
    /// 档位（⛔ 不抛异常、⛔ 不弹 Windows 系统错误框、⛔ 不写注册表；每档各自 try/catch + 写日志）：
    ///   ① shell 关联 Process.Start(url, UseShellExecute=true)（首选，asInvoker 下的正路）
    ///   ② 显式默认浏览器：HKCU ...\UrlAssociations\http\UserChoice 的 ProgId
    ///      -> HKCU\Software\Classes\&lt;ProgId&gt;\shell\open\command（缺则 HKCR\...）里解析出的 exe
    ///   ③ explorer.exe "&lt;url&gt;"：⚠️ 路径必须【解析 + File.Exists 校验】——
    ///      r23 用的是 Environment.SpecialFolder.System = C:\Windows\System32\explorer.exe，
    ///      而该文件在 64 位 Windows 上【根本不存在】（真身 = C:\Windows\explorer.exe）
    ///      => r23 第 1 档每次都抛「找不到文件」，还把 Windows 的系统错误框弹到用户脸上（实测截图）。
    ///      现在三候选取第一个真实存在的；一个都没有 => ⛔ 跳过该档，只写日志。
    /// ⚠️ 可测性：真正拉起进程的动作收敛到 <see cref="TestHooks.TryRun"/>（默认 <see cref="RunDetached"/>），
    ///   harness 换掉它就能断言「到底用哪个 exe、传了什么参数」，⛔ 不会真的开浏览器。
    /// </summary>
    internal static class UrlOpener
    {
        /// <summary>尝试顺序（先正路、后兜底）：返回 null 表示都没成，调用方给可见提示。</summary>
        internal static bool TryOpen(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Log("URL 为空 -> 不打开（保留提示框里的「复制地址」）");
                return false;
            }

            // ⚠️ 防参数注入 / 防引号截断：URL 里正常不会有引号，真出现就剔掉。
            string safe = url.Replace("\"", string.Empty);

            // ---- ① shell 关联（当前用户的默认浏览器；asInvoker 下这就是正路）----
            try
            {
                if (TryRun(null, safe))
                {
                    Log("第1档 shell 关联（UseShellExecute）已接受网址：" + safe);
                    return true;
                }
                Log("第1档 shell 关联失败 -> 转第2档 显式默认浏览器");
            }
            catch (Exception ex)
            {
                Log("第1档 shell 关联异常（" + ex.GetType().Name + " / " + ex.Message + "）-> 转第2档");
            }

            // ---- ② 显式默认浏览器（注册表拿 exe，存在才用）----
            try
            {
                string browser = ResolveDefaultBrowserExe();
                if (browser == null)
                {
                    Log("第2档 跳过：注册表里读不到可用的默认浏览器 exe");
                }
                else if (TryRun(browser, "\"" + safe + "\""))
                {
                    Log("第2档 默认浏览器已接受网址：" + browser + " \"" + safe + "\"");
                    return true;
                }
                else
                {
                    Log("第2档 默认浏览器启动返回 false：" + browser);
                }
            }
            catch (Exception ex)
            {
                Log("第2档 默认浏览器异常（" + ex.GetType().Name + " / " + ex.Message + "）");
            }

            // ---- ③ explorer.exe "<url>"（★ 路径先解析 + File.Exists 校验，缺了就跳过）----
            try
            {
                string explorer = ResolveExplorerExe();
                if (explorer == null)
                {
                    Log("第3档 跳过：explorer.exe 候选路径都不存在（⛔ 不调用，免得弹系统错误框）");
                }
                else if (TryRun(explorer, "\"" + safe + "\""))
                {
                    Log("第3档 explorer.exe 已接受网址：" + explorer + " \"" + safe + "\"");
                    return true;
                }
                else
                {
                    Log("第3档 explorer.exe 启动返回 false：" + explorer);
                }
            }
            catch (Exception ex)
            {
                Log("第3档 explorer.exe 异常（" + ex.GetType().Name + " / " + ex.Message + "）");
            }

            Log("三档都没能打开 -> 由界面给出【可见提示】（网址可复制，手动粘贴即可）");
            return false;
        }

        /// <summary>日志文件全路径（提示框里也显示，方便用户按这个路径去取日志）。</summary>
        internal static string LogPath()
        {
            return Path.Combine(Path.GetTempPath(), "L-Mechrevo-Launcher.log");
        }

        /// <summary>
        /// explorer.exe 的真实位置：三候选取第一个 File.Exists 的；都不存在 => null（该档跳过）。
        /// ★ r24 修正：真身是 %windir%\explorer.exe；System32 下【没有】explorer.exe。
        /// </summary>
        internal static string ResolveExplorerExe()
        {
            var candidates = new List<string>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win))
            {
                candidates.Add(Path.Combine(win, "explorer.exe"));
                candidates.Add(Path.Combine(Path.Combine(win, "SysWOW64"), "explorer.exe"));
            }
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(sys))
                candidates.Add(Path.Combine(sys, "explorer.exe"));

            foreach (string c in candidates)
            {
                bool exists;
                try { exists = File.Exists(c); }
                catch (Exception) { exists = false; }
                Log("explorer 候选 " + c + " => " + (exists ? "存在（采用）" : "不存在（换下一个）"));
                if (exists)
                    return c;
            }
            return null;
        }

        /// <summary>默认浏览器 exe：HKCU UserChoice 的 ProgId -> open 命令里的 exe；解析不到/不存在 => null。</summary>
        internal static string ResolveDefaultBrowserExe()
        {
            string progId = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                {
                    if (k != null)
                        progId = k.GetValue("ProgId") as string;
                }
            }
            catch (Exception)
            {
            }

            string command = null;
            if (!string.IsNullOrEmpty(progId))
            {
                command = ReadOpenCommand(Registry.CurrentUser, @"Software\Classes\" + progId)
                       ?? ReadOpenCommand(Registry.ClassesRoot, progId);
            }
            if (string.IsNullOrEmpty(command))
                command = ReadOpenCommand(Registry.CurrentUser, @"Software\Classes\http")
                       ?? ReadOpenCommand(Registry.ClassesRoot, "http");
            Log("默认浏览器 ProgId=" + (progId ?? "<null>") + " command=" + (command ?? "<null>"));

            string exe = ExtractExecutable(command);
            if (string.IsNullOrEmpty(exe))
            {
                Log("默认浏览器 open 命令解析不出 exe => 跳过该档");
                return null;
            }
            bool ok;
            try { ok = File.Exists(exe); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                Log("默认浏览器 exe 不存在：" + exe + " => 跳过该档");
                return null;
            }
            return exe;
        }

        /// <summary>读 &lt;subPath&gt;\shell\open\command 的默认值；⛔ 不 Dispose 传进来的基键。</summary>
        private static string ReadOpenCommand(RegistryKey baseKey, string subPath)
        {
            try
            {
                using (RegistryKey k = baseKey.OpenSubKey(subPath + @"\shell\open\command"))
                {
                    return k == null ? null : (k.GetValue(null) as string);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>命令行里取 exe：带引号取引号内；不带引号取到第一个空格。</summary>
        internal static string ExtractExecutable(string command)
        {
            if (string.IsNullOrEmpty(command))
                return null;
            string s = command.Trim();
            if (s.Length == 0)
                return null;
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                return end > 1 ? s.Substring(1, end - 1) : null;
            }
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        /// <summary>把 URL 写进本壳自己的日志（%TEMP%\L-Mechrevo-Launcher.log）；⛔ 失败静默。</summary>
        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    LogPath(),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [open-url] " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>经 TestHooks 走一次"拉进程"（默认实现用 Process.Start(UseShellExecute=true)）。</summary>
        private static bool TryRun(string fileName, string arguments)
        {
            return TestHooks.TryRun(fileName, arguments);
        }

        /// <summary>默认实现：UseShellExecute=true 拉起（fileName == null 时把 arguments 当 URL 直接交给 shell）。</summary>
        internal static bool RunDetached(string fileName, string arguments)
        {
            var psi = fileName == null
                ? new ProcessStartInfo(arguments)
                : new ProcessStartInfo(fileName) { Arguments = arguments };
            // UseShellExecute：explorer 也好、URL 也好，都交给 shell 打开；⛔ 不使用管道（免得挂住子进程）。
            psi.UseShellExecute = true;
            using (Process p = Process.Start(psi))
            {
                // ⚠️ explorer 打开 URL 时【它是转交后立刻退出】的，p 可能为 null 也可能立刻结束：
                //   => 只要能 Start 成功就判"已交给 shell"，⛔ 不在这里等退出。
                return true;
            }
        }
    }

    /// <summary>给无窗 harness 用的替身钩子（⛔ 生产路径不设它 => 一律走真实的 RunDetached）。</summary>
    internal static class TestHooks
    {
        private static readonly Func<string, string, bool> Default = RunDefault;

        private static Func<string, string, bool> current = Default;

        /// <summary>非 null 时【取代】真正的拉进程动作；harness 用它记录并断言参数。</summary>
        internal static Func<string, string, bool> TryRun
        {
            get { return current; }
            set { current = value ?? Default; }
        }

        /// <summary>把钩子还原成真实实现（harness 收尾用）。</summary>
        internal static void Reset()
        {
            current = Default;
        }

        private static bool RunDefault(string fileName, string arguments)
        {
            return UrlOpener.RunDetached(fileName, arguments);
        }
    }
}
