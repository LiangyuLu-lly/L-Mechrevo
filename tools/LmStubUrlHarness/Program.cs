// ================================================================================================
// LmStubUrlHarness —— 启动壳「打开下载」代码路径的无窗 harness（r23 新增；r24 修断言）
// ================================================================================================
// 它把【真正打进安装包的那个壳】（L-Mechrevo-Launcher.exe）里的
//   LmLauncherStub.UrlOpener.TryOpen(url)
// 走一遍，并用 LmLauncherStub.TestHooks.TryRun 替身接住所有"拉进程"动作：
//   ⛔ 不显示任何窗口、⛔ 不真的开浏览器、⛔ 不弹 UAC、⛔ 不碰注册表/剪贴板。
//
// ⚠️⚠️ r24 修的是【错误断言】：r23 的 harness 断言第 1 档用的是
//     Environment.SpecialFolder.System（= C:\Windows\System32\explorer.exe）—— 等于把 bug 写进了测试。
//     这个文件在 64 位 Windows 上根本不存在，所以壳每次都在那一档抛「找不到文件」。
//   现在断言：
//     A 档位顺序 = ① shell 关联 -> ② 显式默认浏览器 -> ③ explorer.exe（「能用的那档」必须在最前）
//     B 第2档（默认浏览器）必须是【磁盘上真实存在的 exe】
//     C 第3档（explorer）必须是【磁盘上真实存在的 explorer.exe】，且【不得】是 ...\System32\explorer.exe
//     D 每一档的参数形态 = 带引号的直链；全失败 => TryOpen 返回 false（界面给可见提示）
//     E 每一档各自 try/catch：某一档抛异常不会带走后面几档
// 用法： LmStubUrlHarness.exe <壳 exe 的路径> [日志路径]
// ================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace LmStubUrlHarness
{
    internal static class Program
    {
        private static int failures;
        private static readonly List<string> lines = new List<string>();

        private static void Say(string s)
        {
            lines.Add(s);
            Console.WriteLine(s);
        }

        private static void Check(string name, bool cond)
        {
            if (!cond)
                failures++;
            Say((cond ? "  PASS " : "  FAIL ") + name);
        }

        private static int Main(string[] argv)
        {
            string stubPath = argv.Length > 0
                ? argv[0]
                : @"D:\openLM\installer-src\files\L-Mechrevo-Launcher.exe";
            string logPath = argv.Length > 1
                ? argv[1]
                : Path.Combine(Path.GetTempPath(), "L-Mechrevo-Launcher.log");

            Say("=== LmLauncherStub 打开网址 harness (r24) ===");
            Say("壳 = " + stubPath + "  (" + new FileInfo(stubPath).Length + " B)");

            // ⚠️ 反射加载（不是编译期引用）：本 harness 只针对【已构建的那个 exe】做断言，
            //   壳一改、harness 重新一跑就能发现断链（编译期引用会被旧程序集蒙过去）。
            Assembly asm = Assembly.LoadFrom(stubPath);
            Type opener = asm.GetType("LmLauncherStub.UrlOpener", true);
            Type hooks = asm.GetType("LmLauncherStub.TestHooks", true);
            MethodInfo tryOpen = opener.GetMethod("TryOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo resolveExplorer = opener.GetMethod("ResolveExplorerExe", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo resolveBrowser = opener.GetMethod("ResolveDefaultBrowserExe", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            PropertyInfo tryRunProp = hooks.GetProperty("TryRun", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo reset = hooks.GetMethod("Reset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (tryOpen == null || tryRunProp == null || reset == null || resolveExplorer == null || resolveBrowser == null)
            {
                Say("FATAL: 壳里找不到 UrlOpener.TryOpen / ResolveExplorerExe / ResolveDefaultBrowserExe / TestHooks.*");
                return 9;
            }

            // 直链从壳自己的常量里读（⛔ 不在 harness 里抄一份，避免两处漂移）。
            Type prog = asm.GetType("LmLauncherStub.Program", true);
            FieldInfo urlField = prog.GetField("RuntimeUrl", BindingFlags.NonPublic | BindingFlags.Static);
            string url = (string)urlField.GetValue(null);
            Say("壳里的直链 = " + url);

            Type funcType = tryRunProp.PropertyType;              // Func<string,string,bool>

            // 替身：记录 (fileName, arguments)，按脚本返回。
            var calls = new List<string[]>();
            bool firstOk = true;         // ① shell 关联
            bool secondOk = false;       // ② 默认浏览器
            bool thirdOk = false;        // ③ explorer
            int ordinal = 0;
            bool throwOnFirst = false;

            Func<string, string, bool> fakeLambda = (fileName, arguments) =>
            {
                calls.Add(new[] { fileName, arguments });
                ordinal++;
                if (ordinal == 1)
                {
                    if (throwOnFirst)
                        throw new InvalidOperationException("harness: 故意让第1档抛异常");
                    return firstOk;
                }
                if (ordinal == 2)
                    return secondOk;
                return thirdOk;
            };
            Delegate fake = fakeLambda;

            tryRunProp.SetValue(null, fake, null);

            // ---- 先问壳自己：它到底解析出了什么（这是 r24 新加的解析函数）----
            Say("");
            Say("=== 壳自己的路径解析（真实文件系统，只 File.Exists，不启动任何东西）===");
            string explorer = (string)resolveExplorer.Invoke(null, null);
            string browser = (string)resolveBrowser.Invoke(null, null);
            Say("  ResolveExplorerExe()      = " + (explorer ?? "<null>"));
            Say("  ResolveDefaultBrowserExe()= " + (browser ?? "<null>"));
            Check("R1 explorer 解析结果非空（磁盘上确实有 explorer.exe）", !string.IsNullOrEmpty(explorer));
            if (!string.IsNullOrEmpty(explorer))
            {
                Check("R2 explorer 解析结果【真实存在】", File.Exists(explorer));
                Check("R3 explorer 不得是 ...\\System32\\explorer.exe（r23 的错路径）",
                    !explorer.ToLowerInvariant().EndsWith(@"\system32\explorer.exe"));
                Check("R4 explorer 来自 {win}（C:\\Windows\\explorer.exe）",
                    explorer.ToLowerInvariant() == Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe").ToLowerInvariant());
            }
            if (!string.IsNullOrEmpty(browser))
                Check("R5 默认浏览器解析结果【真实存在】", File.Exists(browser));

            // 这台机器上预期会被调用的档位序列（默认浏览器可能没注册 => 少一档）
            var expectSeq = new List<string>();
            expectSeq.Add(null);
            if (!string.IsNullOrEmpty(browser))
                expectSeq.Add(browser);
            expectSeq.Add(explorer);

            try
            {
                long logBefore = LogLength(logPath);

                // ---------------- CASE A：第1档（shell 关联）成功 ----------------
                Say("");
                Say("=== CASE A 第1档 shell 关联成功 => 必须立刻停（⛔ 不再试后面的档）===");
                calls.Clear(); ordinal = 0; firstOk = true; secondOk = false; thirdOk = false; throwOnFirst = false;
                bool r = (bool)tryOpen.Invoke(null, new object[] { url });
                Check("A1 返回 true", r);
                Check("A2 只调用 1 次", calls.Count == 1);
                if (calls.Count >= 1)
                {
                    Say("  调用[1] file=[" + (calls[0][0] ?? "<null>") + "] args=[" + calls[0][1] + "]");
                    Check("A3 第1档 = shell 关联（fileName == null，把 URL 交给 shell）", calls[0][0] == null);
                    Check("A4 第1档参数 = 裸直链", calls[0][1] == url);
                }
                string logA = LogTail(logPath, logBefore);
                Check("A5 日志记录第1档成功", logA.Contains("第1档 shell 关联"));

                // ---------------- CASE B：第1档失败 -> 第2档（默认浏览器）----------------
                Say("");
                Say("=== CASE B 第1档失败 -> 第2档 显式默认浏览器 ===");
                calls.Clear(); ordinal = 0; firstOk = false; secondOk = true; thirdOk = false; throwOnFirst = false;
                logBefore = LogLength(logPath);
                r = (bool)tryOpen.Invoke(null, new object[] { url });
                Check("B1 返回 true", r);
                Check("B2 调用序列 == 预期档位序列（顺序断言）", SameSeq(calls, expectSeq));
                if (calls.Count >= 2)
                {
                    Say("  调用[2] file=[" + (calls[1][0] ?? "<null>") + "] args=[" + calls[1][1] + "]");
                    Check("B3 第2档 fexists（拿到的 exe 必须真实存在）",
                        calls[1][0] != null && File.Exists(calls[1][0]));
                    Check("B4 第2档参数 = 带引号的直链（防截断）", calls[1][1] == "\"" + url + "\"");
                }
                string logB = LogTail(logPath, logBefore);
                Check("B5 日志记录第1档失败 + 第2档成功",
                    logB.Contains("第1档 shell 关联失败") && logB.Contains("第2档 默认浏览器已接受网址"));

                // ---------------- CASE C：前两档失败 -> 第3档 explorer（★ r24 的重点断言）----------------
                Say("");
                Say("=== CASE C 前两档失败 -> 第3档 explorer.exe（路径必须真实存在）===");
                calls.Clear(); ordinal = 0; firstOk = false; secondOk = false; thirdOk = true; throwOnFirst = false;
                logBefore = LogLength(logPath);
                r = (bool)tryOpen.Invoke(null, new object[] { url });
                Check("C1 返回 true", r);
                Check("C2 调用序列 == 预期档位序列（顺序断言）", SameSeq(calls, expectSeq));
                Check("C2b 走到最后一档（调用次数 == 预期档数）", calls.Count == expectSeq.Count);
                if (calls.Count >= 1)
                {
                    string[] last = calls[calls.Count - 1];
                    Say("  最后一档 file=[" + (last[0] ?? "<null>") + "] args=[" + last[1] + "]");
                    Check("C3 最后一档以 explorer.exe 结尾", last[0] != null && last[0].ToLowerInvariant().EndsWith("explorer.exe"));
                    Check("C4 ★ explorer 路径【在磁盘上真实存在】", last[0] != null && File.Exists(last[0]));
                    Check("C5 ★ 不得是 ...\\System32\\explorer.exe（r23 的错路径）",
                        last[0] != null && !last[0].ToLowerInvariant().EndsWith(@"\system32\explorer.exe"));
                    Check("C6 explorer 参数 = 带引号的直链", last[1] == "\"" + url + "\"");
                }
                string logC = LogTail(logPath, logBefore);
                Check("C7 日志里有 explorer 候选逐个 File.Exists 的判定记录",
                    logC.Contains("explorer 候选") && (logC.Contains("存在（采用）") || logC.Contains("不存在（换下一个）")));

                // ---------------- CASE D：全失败 ----------------
                Say("");
                Say("=== CASE D 所有档都失败 => 返回 false（界面给可见提示）===");
                calls.Clear(); ordinal = 0; firstOk = false; secondOk = false; thirdOk = false; throwOnFirst = false;
                logBefore = LogLength(logPath);
                r = (bool)tryOpen.Invoke(null, new object[] { url });
                Check("D1 返回 false", !r);
                Check("D2 调用序列 == 预期档位序列", SameSeq(calls, expectSeq));
                Check("D2b 三档都试过（调用次数 == 预期档数）", calls.Count == expectSeq.Count);
                string logD = LogTail(logPath, logBefore);
                Check("D3 日志记录「三档都没能打开」", logD.Contains("三档都没能打开"));

                // ---------------- CASE E：第1档抛异常 -> 继续后面的档 ----------------
                Say("");
                Say("=== CASE E 第1档抛异常 -> 被吞掉 + 继续第2档 ===");
                calls.Clear(); ordinal = 0; firstOk = false; secondOk = true; thirdOk = false; throwOnFirst = true;
                logBefore = LogLength(logPath);
                r = (bool)tryOpen.Invoke(null, new object[] { url });
                Check("E1 返回 true（异常没有冒到 UI 线程）", r);
                Check("E2 第1档抛异常后【仍然继续】第2档（序列是预期前缀）", SameSeq(calls, expectSeq));
                Check("E2b 第1档确实被调用过（异常发生在调用之后）", calls.Count >= 2);
                string logE = LogTail(logPath, logBefore);
                Check("E3 日志记录第1档异常 + 第2档成功",
                    logE.Contains("第1档 shell 关联异常") && logE.Contains("第2档 默认浏览器已接受网址"));
            }
            finally
            {
                reset.Invoke(null, null);   // 还原钩子（本进程随即便退出，仍照做以免误导）
            }

            Say("");
            Say(failures == 0 ? "RESULT: ALL-PASS" : "RESULT: HAS-FAILURE (" + failures + ")");
            Say("DONE-ALL-OK");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>调用序列必须是【预期档位序列的前缀】（某一档成功就停，所以允许提前结束）。</summary>
        private static bool SameSeq(List<string[]> calls, List<string> expect)
        {
            if (calls.Count > expect.Count)
                return false;
            for (int i = 0; i < calls.Count; i++)
            {
                string got = calls[i][0];
                string want = expect[i];
                if (want == null)
                {
                    if (got != null)
                        return false;
                }
                else if (got == null || !string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static long LogLength(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static string LogTail(string path, long from)
        {
            try
            {
                if (!File.Exists(path))
                    return string.Empty;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (from > fs.Length)
                        from = 0;
                    fs.Seek(from, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                        return sr.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
