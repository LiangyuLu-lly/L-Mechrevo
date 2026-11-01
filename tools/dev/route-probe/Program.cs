// task-41：机型分流只读探针
// 用法：route-probe.exe [MechrevoLite.Core.dll 路径]
//   · 不传路径时用仓库默认 Release 输出（相对本 exe 的 6 级上级目录）。
//   · 反射加载 Core 程序集（Core 的机型事实/路由类型是 internal，但探针不引用 Core 源码，避免把调试桩带进生产）。
//   · 只读：MachineIdentity.Collect 只读注册表/WMI；BackendRouter.DecideAndLog 是纯路由 + 一行日志；
//     MechrevoBackendOrder.Probe 与启动时同一条只读探测（不连 MQTT、不发硬件写命令）；
//     RunSelfTest / DiagnosticBackendRouteInfo.Build 是纯函数矩阵/只读诊断。
//   · 退出码：0 = 自测 FAIL=0；1 = 自测失败；2 = 加载/反射失败。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace RouteProbe;

internal static class Program
{
    private const string CoreRelativePath =
        "source/bin/MechrevoLite.Core/Release/net10.0-windows10.0.19041.0/MechrevoLite.Core.dll";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string corePath = args.Length > 0 ? Path.GetFullPath(args[0]) : DefaultCorePath();
        Console.WriteLine("=== 机型分流只读探针（task-41）===");
        Console.WriteLine("Core = " + corePath);
        Console.WriteLine("身份 = " + (IsElevated() ? "管理员（本探针不需要）" : "普通用户（非管理员）") + " · 只读");
        if (!File.Exists(corePath))
        {
            Console.WriteLine("[ERR] 找不到 Core 程序集；先构建 source/MechrevoLite.Core.csproj -c Release");
            return 2;
        }
        string? coreDir = Path.GetDirectoryName(corePath);
        // 让 Core 的依赖（MQTTnet / LibreHardwareMonitorLib / System.Management …）从 Core 输出目录解析，
        // 而不是从本探针目录解析（探针不复制任何生产依赖）。
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (coreDir is null || name.Name is null)
            {
                return null;
            }
            string candidate = Path.Combine(coreDir, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        long logLengthBefore = SafeLogLength();
        Assembly? core = null;
        try
        {
            core = AssemblyLoadContext.Default.LoadFromAssemblyPath(corePath);
            Type identityType = RequireType(core, "MechrevoLite.Hardware.MachineIdentity");
            Type routerType = RequireType(core, "MechrevoLite.Hardware.BackendRouter");
            Type orderType = RequireType(core, "MechrevoLite.Hardware.MechrevoBackendOrder");

            object identity = InvokeStatic(identityType, "Collect");
            Console.WriteLine();
            Console.WriteLine("--- 机型事实（MachineIdentity.Collect，只读）---");
            foreach (string name in new[]
                     {
                         "Board", "BoardSource", "BaolongdaWhitelisted", "GpuGeneration", "GcuServiceInstalled",
                         "Model", "ModelSource", "MifsClass", "MifsClassPresent", "MifsLegacyCompatible",
                         "ProjectId", "ProjectIdSource", "GcuVariant", "GcuVariantFamily", "WujieApu", "WujieApuSource"
                     })
            {
                Console.WriteLine("  " + name.PadRight(22) + " = " + Format(GetProperty(identity, name)));
            }
            Console.WriteLine("  " + "Summary".PadRight(22) + " = " + Format(GetProperty(identity, "Summary")));

            Console.WriteLine();
            Console.WriteLine("--- 路由 + 后端探测（与启动同一条只读路径；不连 MQTT、不写硬件）---");
            object probed = InvokeStatic(orderType, "Probe");
            // ValueTuple 的命名元素（Kind/Created）只存在于编译期，反射要读 Item1/Item2 字段。
            object? actualValue = probed.GetType().GetField("Item1", BindingFlags.Instance | BindingFlags.Public)?.GetValue(probed);
            string actual = Format(actualValue);
            Console.WriteLine("  ActualBackend    = " + actual);
            object? decision = GetStaticProperty(routerType, "LastDecision");
            if (decision is null)
            {
                Console.WriteLine("  [WARN] LastDecision 为空（路由未运行）");
            }
            else
            {
                Console.WriteLine("  DeclaredBackend  = " + Format(GetProperty(decision, "Backend")));
                Console.WriteLine("  RuleIndex        = " + Format(GetProperty(decision, "RuleIndex")));
                Console.WriteLine("  Rule             = " + Format(GetProperty(decision, "Rule")));
                Console.WriteLine("  DeclarationPassed= " + Format(GetProperty(decision, "DeclarationPassed")));
                Console.WriteLine("  声明 vs 实际     = " + (Format(GetProperty(decision, "Backend")) == actual ? "一致 ✅" : "⚠️ 不一致"));
            }

            // [gate] 机型分类 是 ConnectAsync 里的私有日志辅助方法；这里用同一方法生成与真实启动一致的日志行
            // （记录：探针调用的是生产代码里的同一方法，不是另写一套格式）。
            Console.WriteLine();
            Console.WriteLine("--- [gate] 机型分类（调用生产日志辅助方法，只写日志）---");
            try
            {
                Type hwType = RequireType(core, "MechrevoLite.Hardware.MechrevoHw");
                Type kindType = RequireType(core, "MechrevoLite.Hardware.MechrevoBackendKind");
                object kindValue = Enum.Parse(kindType, actual);
                MethodInfo? gate = hwType.GetMethod("LogMachineRouteGate", BindingFlags.Static | BindingFlags.NonPublic);
                if (gate is null)
                {
                    Console.WriteLine("  [WARN] 找不到 MechrevoHw.LogMachineRouteGate（日志辅助已改名？）");
                }
                else
                {
                    gate.Invoke(null, new[] { kindValue });
                    Console.WriteLine("  已写入 [gate] 机型分类 日志（declared/actual/一致 一行）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [WARN] gate 日志辅助调用失败（不影响其它结果）：" + ex.GetType().Name + " " + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine("--- 诊断包节选（DiagnosticBackendRouteInfo.Build，只读）---");
            Type diagType = RequireType(core, "MechrevoLite.Diagnostics.DiagnosticBackendRouteInfo");
            string diag = (string)InvokeStatic(diagType, "Build")!;
            foreach (string line in SplitLines(diag))
            {
                Console.WriteLine("  " + line);
            }

            Console.WriteLine();
            Console.WriteLine("--- 规则矩阵自测（BackendRouter.RunSelfTest，纯函数）---");
            string selfTest = (string)InvokeStatic(routerType, "RunSelfTest")!;
            foreach (string line in SplitLines(selfTest))
            {
                Console.WriteLine("  " + line);
            }
            bool ok = selfTest.Contains("FAIL=0", StringComparison.Ordinal);
            Console.WriteLine();
            Console.WriteLine(ok ? "[RESULT] 规则矩阵自测全部通过 ✅（只读，无硬件动作）"
                                 : "[RESULT] ⚠️ 规则矩阵自测存在失败项，见上方 ✗");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERR] " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
        finally
        {
            if (core is not null)
            {
                // Logger 是异步写盘（后台线程 + 队列）：不 Close 会丢最后几行日志（探针第一次就踩到了）。
                TryCloseLogger(core);
                PrintNewLogLines(logLengthBefore);
            }
        }
    }

    private static string DefaultCorePath()
    {
        // exe 在 tools/dev/route-probe/bin/Release/net10.0-windows/ ⇒ 上 6 级 = 仓库根。
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            dir = dir.Parent;
        }
        string root = dir?.FullName ?? Environment.CurrentDirectory;
        return Path.Combine(root, CoreRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string LogPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MechrevoLite", "log.txt");
    }

    private static long SafeLogLength()
    {
        try
        {
            return new FileInfo(LogPath()).Exists ? new FileInfo(LogPath()).Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static void PrintNewLogLines(long fromLength)
    {
        try
        {
            string path = LogPath();
            if (!File.Exists(path))
            {
                return;
            }
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= fromLength)
            {
                Console.WriteLine();
                Console.WriteLine("（日志文件没有新增内容；若日志级别=仅错误，Info 行不会落盘）");
                return;
            }
            stream.Seek(fromLength, SeekOrigin.Begin);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string text = reader.ReadToEnd();
            string[] lines = SplitLines(text)
                .Where(line => line.Contains("[强化机型分流]", StringComparison.Ordinal)
                            || line.Contains("[gate] 机型分类", StringComparison.Ordinal))
                .ToArray();
            Console.WriteLine();
            Console.WriteLine("--- 本次写入 " + Path.GetFileName(path) + " 的机型分流行（" + LogPath() + "）---");
            if (lines.Length == 0)
            {
                Console.WriteLine("  (无；日志级别可能是仅错误——可用 LMECHREVO_LOG_LEVEL=all 复跑)");
            }
            foreach (string line in lines)
            {
                Console.WriteLine("  " + line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  [WARN] 读日志失败：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void TryCloseLogger(Assembly core)
    {
        try
        {
            Type? logger = core.GetType("Logger");
            logger?.GetMethod("Close", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
        }
        catch
        {
            // 探针退出前 flush 日志，失败不影响结果。
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static Type RequireType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName, throwOnError: true)!;
    }

    private static object InvokeStatic(Type type, string methodName)
    {
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(type.FullName + "." + methodName);
        }
        return method.Invoke(null, null)!;
    }

    private static object? GetProperty(object target, string name)
    {
        return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
    }

    private static object? GetStaticProperty(Type type, string name)
    {
        return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => "(null)",
            bool b => b ? "True" : "False",
            string s => s.Length == 0 ? "(空)" : s,
            _ => value.ToString() ?? "(null)"
        };
    }
}
