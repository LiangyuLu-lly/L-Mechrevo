using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-09（诊断补采·第二批）：在第一轮（<see cref="DiagnosticGcuEvidenceInfo"/>，47:00 批）已补的
/// 「装后自检 / 关键文件大小哈希 / 就绪键 / 底座日志尾部」之外，再补四样此前**包里完全没有**的东西：
///   (a) [GCU 用户档案清单（UserPofiles）] —— 只列文件名 / 大小 / 修改时间（⚠️ **绝不读取或复制档案内容**：
///       那是 GCU 运行期用户档案，属机主个人数据），并对照「应含 MainOption + Mode1..3_Profile1..5（共 16 个）」
///       逐个报缺失、把不在期望清单里的项也列出来（机主机器上这 16 项齐不齐只有他机器上能确认）；
///   (b) [GCU 侧日志]（在 DiagnosticGcuEvidenceInfo 的既有小节里就地扩写，不新开重复小节）——
///       C:\ProgramData\ControlCenter\*.log（含 installLog）与 C:\MyControlCenter.log 的清单 + 尾部
///       （每文件 ≤64 KB、小节合计 ≤512 KB）+ 关键行抽取；
///   (c) [GCU 安装日志与装后自检状态] —— %ProgramData%\L-Mechrevo\ 与 %ProgramData%\L-Mechrevo\logs\
///       两处的 gcu-install-*.log 清单 + 尾部（每文件 ≤64 KB、小节合计 ≤256 KB）；gcu-install-status.json
///       的**原文全文**在既有「[GCU 装后自检]」节（同一份，此处不重复贴）；两处都没有安装日志时明确写「不存在」
///       —— 这正是「旧安装器」的证据（旧安装器根本不写安装日志）；
///   (d) [GCU 底座目录清单] —— 底座根（注册表 GcuServiceExe 推导，兼容两种布局）下的目录树（≤3 层、≤400 项、
///       单目录 ≤40 项）+ 关键文件名 / 大小 / 时间戳，以及安装根目录的 1 层清单（看清 payload\ 有没有）。
///
/// ⚠️ 全程只读：不写任何文件（不写 GCU 目录、不写 ProgramData、不写注册表）、不改 GCU 配置、不连 13688、
///    不新增任何 MQTT 订阅/GET、不启停服务；每项独立 try/catch，失败只写「该项读取失败（原因）」，
///    单点失败绝不影响诊断包导出。
/// </summary>
internal static class DiagnosticGcuInventoryInfo
{
	/// <summary>单个日志文件尾部读取上限（64 KiB）。</summary>
	internal const int TailBytesPerFile = 32768;

	/// <summary>[GCU 侧日志] 小节所有文件尾部合计上限（512 KiB）。</summary>
	internal const int ControlCenterBudgetBytes = 196608;

	/// <summary>[GCU 安装日志] 小节所有文件尾部合计上限（256 KiB；安装日志本身很小）。</summary>
	internal const int InstallLogBudgetBytes = 131072;

	/// <summary>关键行抽取窗口 = 文件末尾 min(文件大小, 512 KiB)。</summary>
	private const int KeyLineScanBytes = 524288;

	/// <summary>单个日志文件最多贴出的内容行数（与 64 KiB 字节上限取先到者）。</summary>
	private const int MaxLogLinesPerFile = 400;

	/// <summary>单个日志文件最多贴出的关键行数。</summary>
	private const int MaxKeyLinesPerFile = 40;

	/// <summary>每行最多字符数（防单行超长把包撑大）。</summary>
	private const int MaxLogLineChars = 400;

	private const int MaxKeyLineChars = 300;

	/// <summary>目录树最大层数（大目录只列前几层）。</summary>
	private const int MaxTreeDepth = 2;

	/// <summary>目录树最多列出的条目数（目录 + 文件）。</summary>
	private const int MaxTreeEntries = 200;

	/// <summary>单个目录最多列出的子项数。</summary>
	private const int MaxDirChildrenListed = 20;

	private const string ProgramDataDir = @"C:\ProgramData\L-Mechrevo";

	private const string ControlCenterDir = @"C:\ProgramData\ControlCenter";

	private const string MyControlCenterLogPath = @"C:\MyControlCenter.log";

	/// <summary>关键行命中规则（⚠️ 命中就贴原文行，不只给计数；不区分大小写）。</summary>
	private const string KeyRuleText = "LM_Manager / LM_Init / RGBKB / solution / UNKNOWN / Keyboard / Subscribe / SUBACK / Mqtt / Error / Exception";

	private static readonly Regex KeyLinePattern = new Regex(
		"LM_Manager|LM_Init|RGBKB|solution|UNKNOWN|Keyboard|Subscribe|SUBACK|Mqtt|Error|Exception",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>「应含 16 个」的期望档案名（MainOption.json + Mode1..3 各 Profile1..5）。</summary>
	private static readonly string[] ExpectedUserPofiles = BuildExpectedUserPofiles();

	private static readonly UTF8Encoding Utf8Loose = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		AppendInstallLogs(sb);
		AppendUserPofiles(sb);
		AppendBaseTree(sb);
		return sb.ToString();
	}

	private static string[] BuildExpectedUserPofiles()
	{
		List<string> list = new List<string> { "MainOption.json" };
		for (int mode = 1; mode <= 3; mode++)
		{
			for (int profile = 1; profile <= 5; profile++)
			{
				list.Add($"Mode{mode}_Profile{profile}.json");
			}
		}
		return list.ToArray();
	}

	// ================================================================ (c) 安装日志 + 装后自检状态

	private static void AppendInstallLogs(StringBuilder sb)
	{
		sb.AppendLine("[GCU 安装日志与装后自检状态]");
		sb.AppendLine("（⚠️ 只读；gcu-install-status.json 的**原文全文**在上文「[GCU 装后自检]」一节（同一份，此处不重复贴）。"
			+ $"这里补 gcu-install-*.log 的清单 + 尾部：每文件 ≤ {TailBytesPerFile / 1024} KB，小节合计 ≤ {InstallLogBudgetBytes / 1024} KB；"
			+ "超出会写明截断/丢弃了多少。）");
		try
		{
			string status = Path.Combine(ProgramDataDir, "gcu-install-status.json");
			if (File.Exists(status))
			{
				FileInfo fileInfo = new FileInfo(status);
				sb.AppendLine($"  状态文件: 存在（{DiagnosticGcuEvidenceInfo.Mask(status)}；{fileInfo.Length} 字节；mtime={fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}）⇒ 解析字段与全文见「[GCU 装后自检]」节");
			}
			else
			{
				sb.AppendLine("  状态文件: 不存在（未跑过装后自检，或旧版本安装器 —— 旧安装器不写这个文件）");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  状态文件: 该项读取失败（" + ex.Message + "）");
		}
		sb.AppendLine("  安装日志（gcu-install-*.log；两个位置都找）：");
		try
		{
			int budget = InstallLogBudgetBytes;
			int num = AppendDirectoryLogs(sb, ProgramDataDir, "gcu-install-*.log", recursive: false, ref budget);
			int num2 = AppendDirectoryLogs(sb, Path.Combine(ProgramDataDir, "logs"), "gcu-install-*.log", recursive: false, ref budget);
			int num3 = InstallLogBudgetBytes - budget;
			if (num + num2 == 0)
			{
				sb.AppendLine("  ⇒ gcu-install-*.log：**不存在**（%ProgramData%\\L-Mechrevo\\ 与 %ProgramData%\\L-Mechrevo\\logs\\ 两处都没有）"
					+ " —— 这正是「旧安装器」的证据：旧版本安装时不写任何安装日志。");
			}
			else
			{
				sb.AppendLine($"  ⇒ 小计 {num} + {num2} = {num + num2} 个安装日志；尾部合计 {num3} 字节 / 上限 {InstallLogBudgetBytes} 字节。");
			}
		}
		catch (Exception ex2)
		{
			sb.AppendLine("  该项读取失败（" + ex2.Message + "）");
		}
		sb.AppendLine();
	}

	// ================================================================ (a) UserPofiles 清单

	private static void AppendUserPofiles(StringBuilder sb)
	{
		sb.AppendLine("[GCU 用户档案清单（UserPofiles）]");
		sb.AppendLine("（⚠️ 只列文件名 / 大小 / 修改时间，**绝不读取或复制档案内容** —— 这些是 GCU 运行期用户档案，属机主个人数据；"
			+ "⚠️ 机主机器上这 16 项齐不齐，只有他机器上能确认。）");
		try
		{
			string reason;
			string root;
			string dir = ResolveUserPofilesDir(out root, out reason);
			if (string.IsNullOrWhiteSpace(dir))
			{
				sb.AppendLine("  底座根: 取不到（" + Or(reason) + "）⇒ 用户档案目录也取不到");
				sb.AppendLine();
				return;
			}
			sb.AppendLine("  底座根: " + DiagnosticGcuEvidenceInfo.Mask(root) + "（" + Or(reason) + "）");
			sb.AppendLine("  档案目录: " + DiagnosticGcuEvidenceInfo.Mask(dir));
			if (!Directory.Exists(dir))
			{
				sb.AppendLine("  状态: 目录不存在（该机器上 GCU 没有释放 UserPofiles，或布局与预期不同）");
				sb.AppendLine("  期望应含: MainOption + Mode1..3_Profile1..5（共 16 个：" + string.Join("、", ExpectedUserPofiles) + "）");
				sb.AppendLine();
				return;
			}
			FileInfo[] files = new DirectoryInfo(dir).GetFiles();
			Array.Sort(files, (FileInfo a, FileInfo b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			long num = 0L;
			foreach (FileInfo fileInfo in files)
			{
				num += fileInfo.Length;
			}
			sb.AppendLine($"  状态: 存在，共 {files.Length} 个文件，合计 {num} 字节（⚠️ 只列清单，档案内容不进诊断包）");
			sb.AppendLine("  期望应含: MainOption + Mode1..3_Profile1..5（共 16 个：MainOption.json + Mode1/2/3 各 Profile1..5.json）");
			sb.AppendLine("  实际清单（按名称排序）：");
			foreach (FileInfo fileInfo2 in files)
			{
				sb.AppendLine($"    - {fileInfo2.Name}  {fileInfo2.Length} 字节  mtime={fileInfo2.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
			}
			List<string> list = new List<string>();
			foreach (string expected in ExpectedUserPofiles)
			{
				bool flag = false;
				foreach (FileInfo fileInfo3 in files)
				{
					if (string.Equals(fileInfo3.Name, expected, StringComparison.OrdinalIgnoreCase))
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					list.Add(expected);
				}
			}
			List<string> list2 = new List<string>();
			foreach (FileInfo fileInfo4 in files)
			{
				bool flag2 = false;
				foreach (string item in ExpectedUserPofiles)
				{
					if (string.Equals(fileInfo4.Name, item, StringComparison.OrdinalIgnoreCase))
					{
						flag2 = true;
						break;
					}
				}
				if (!flag2)
				{
					list2.Add(fileInfo4.Name);
				}
			}
			if (list.Count == 0)
			{
				sb.AppendLine("  缺失项: 无（期望的 16 项全在）");
			}
			else
			{
				sb.AppendLine($"  ⚠️ 缺失 {list.Count} 个（应含 16 个，实缺这些）: " + string.Join("、", list));
			}
			if (list2.Count > 0)
			{
				sb.AppendLine($"  额外项 {list2.Count} 个（不在 16 项期望清单里，可能是 Mode4 等其它档位/机型档）: " + string.Join("、", list2));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
		sb.AppendLine();
	}

	/// <summary>
	/// 取 GCU 的 UserPofiles 档案目录（= MyControlCenter\UserPofiles），同时回传底座根与推导原因。
	/// 复用 <see cref="DiagnosticGcuEvidenceInfo.ResolveGcuRoot"/> 的注册表推导，兼容两种布局：
	///   A) &lt;root&gt;\AiStoneService\MyControlCenter\UserPofiles\
	///   B) &lt;root&gt;\MyControlCenter\UserPofiles\
	/// （⚠️ 实测踩过：一开始把 MyControlCenter 本身当成了 UserPofiles，导致 30 个 DLL 被当成「档案」、
	///   16 项全报缺失 —— 路径必须拼到 UserPofiles 这一层。）
	/// </summary>
	internal static string ResolveUserPofilesDir(out string root, out string reason)
	{
		root = DiagnosticGcuEvidenceInfo.ResolveGcuRoot(out reason);
		if (string.IsNullOrWhiteSpace(root))
		{
			return null;
		}
		string text = Path.Combine(root, "MyControlCenter");
		if (!Directory.Exists(text))
		{
			string text2 = Path.Combine(root, "AiStoneService\\MyControlCenter");
			if (Directory.Exists(text2))
			{
				text = text2;
			}
		}
		return Path.Combine(text, "UserPofiles");
	}

	// ================================================================ (d) 底座目录清单

	private static void AppendBaseTree(StringBuilder sb)
	{
		sb.AppendLine("[GCU 底座目录清单]");
		sb.AppendLine($"（底座根由注册表 GcuServiceExe 推导，兼容两种布局；目录树 ≤ {MaxTreeDepth} 层、≤ {MaxTreeEntries} 项、单目录 ≤ {MaxDirChildrenListed} 项，"
			+ "超出写明截断；⚠️ 只列名字 / 大小 / 时间戳，不对全树做哈希；⚠️ 大目录（如 UserFanTables）只列前几层 + 前几十项。）");
		try
		{
			string reason;
			string root = DiagnosticGcuEvidenceInfo.ResolveGcuRoot(out reason);
			if (string.IsNullOrWhiteSpace(root))
			{
				sb.AppendLine("  底座根: 取不到（" + Or(reason) + "）");
				sb.AppendLine();
				return;
			}
			sb.AppendLine("  底座根: " + DiagnosticGcuEvidenceInfo.Mask(root) + "（" + Or(reason) + "）");
			int budget = MaxTreeEntries;
			int num = 0;
			int num2 = 0;
			List<string> list = new List<string>();
			sb.AppendLine($"  目录树（起点 = 底座根，缩进即层级）：");
			Walk(sb, root, 1, MaxTreeDepth, ref budget, ref num, ref num2, list);
			sb.AppendLine($"  小计: {num} 个目录 / {num2} 个文件（条目上限 {MaxTreeEntries}）"
				+ ((list.Count == 0) ? "；无截断" : "；⚠️ 截断说明：" + string.Join("；", list)));
			try
			{
				string directoryName = Path.GetDirectoryName(root.TrimEnd('\\'));
				if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
				{
					int budget2 = 60;
					int num3 = 0;
					int num4 = 0;
					List<string> list2 = new List<string>();
					sb.AppendLine("  安装根（底座根的上级，⚠️ 只列 1 层，用来看 payload\\ 与安装脚本在不在）: " + DiagnosticGcuEvidenceInfo.Mask(directoryName));
					Walk(sb, directoryName, 1, 1, ref budget2, ref num3, ref num4, list2);
					sb.AppendLine($"  安装根小计: {num3} 个目录 / {num4} 个文件" + ((list2.Count == 0) ? "；无截断" : "；⚠️ 截断说明：" + string.Join("；", list2)));
				}
			}
			catch (Exception ex)
			{
				sb.AppendLine("  安装根: 该项读取失败（" + ex.Message + "）");
			}
		}
		catch (Exception ex2)
		{
			sb.AppendLine("  该项读取失败（" + ex2.Message + "）");
		}
		sb.AppendLine();
	}

	/// <summary>
	/// 递归列目录树：目录在前、文件在后，各按名称排序；深度、总条目数、单目录条目数三重上限，超出记入 truncated。
	/// 每个目录独立 try/catch（一个目录读不了不影响其它）。
	/// </summary>
	private static void Walk(StringBuilder sb, string dir, int level, int maxDepth, ref int budget, ref int dirs, ref int files, List<string> truncated)
	{
		string indent = new string(' ', 2 + level * 2);
		DirectoryInfo[] array;
		FileInfo[] array2;
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(dir);
			array = directoryInfo.GetDirectories();
			array2 = directoryInfo.GetFiles();
		}
		catch (Exception ex)
		{
			sb.AppendLine(indent + "该项读取失败（" + ex.Message + "）");
			return;
		}
		Array.Sort(array, (DirectoryInfo a, DirectoryInfo b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		Array.Sort(array2, (FileInfo a, FileInfo b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		int num = 0;
		int num2 = 0;
		DirectoryInfo[] array3 = array;
		foreach (DirectoryInfo directoryInfo2 in array3)
		{
			if (budget <= 0)
			{
				AddNote(truncated, $"「{Path.GetFileName(dir)}」下还有 {array.Length - num} 个子目录未列出（条目上限 {MaxTreeEntries}）");
				return;
			}
			if (num2 >= MaxDirChildrenListed)
			{
				AddNote(truncated, $"「{Path.GetFileName(dir)}」下还有 {array.Length - num2} 个子目录未列出（单目录上限 {MaxDirChildrenListed}）");
				break;
			}
			budget--;
			num2++;
			num++;
			dirs++;
			sb.AppendLine(indent + directoryInfo2.Name + "\\  （目录）");
			if (level < maxDepth)
			{
				Walk(sb, directoryInfo2.FullName, level + 1, maxDepth, ref budget, ref dirs, ref files, truncated);
			}
		}
		int num3 = 0;
		FileInfo[] array4 = array2;
		foreach (FileInfo fileInfo in array4)
		{
			if (budget <= 0)
			{
				AddNote(truncated, $"「{Path.GetFileName(dir)}」下还有 {array2.Length - num3} 个文件未列出（条目上限 {MaxTreeEntries}）");
				return;
			}
			if (num2 >= MaxDirChildrenListed)
			{
				AddNote(truncated, $"「{Path.GetFileName(dir)}」下还有 {array2.Length - num3} 个文件未列出（单目录上限 {MaxDirChildrenListed}）");
				break;
			}
			budget--;
			num2++;
			num3++;
			files++;
			sb.AppendLine($"{indent}{fileInfo.Name}  {fileInfo.Length} 字节  mtime={fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
		}
	}

	private static void AddNote(List<string> notes, string note)
	{
		if (notes.Count < 8 && !notes.Contains(note))
		{
			notes.Add(note);
		}
	}

	// ================================================================ 共用：目录日志清单 + 尾部 + 关键行

	/// <summary>
	/// 目录日志清单 + 尾部（[GCU 侧日志] 与 [GCU 安装日志] 共用同一实现，保证口径只有一份）。
	/// 返回匹配到的文件数；目录不存在 / 没有匹配文件时**明确写「不存在」**，绝不静默略过。
	/// 每个文件独立 try/catch，尾部字节从 <paramref name="budget"/> 里扣减，用尽后逐个写明「已跳过」。
	/// </summary>
	internal static int AppendDirectoryLogs(StringBuilder sb, string directory, string pattern, bool recursive, ref int budget)
	{
		int count = 0;
		sb.AppendLine("    · 目录: " + DiagnosticGcuEvidenceInfo.Mask(directory) + (recursive ? "（含子目录）" : "") + " / " + pattern);
		try
		{
			if (!Directory.Exists(directory))
			{
				sb.AppendLine("      不存在（机主/本机没有这个目录，已按缺失路径处理）");
				return 0;
			}
			DirectoryInfo directoryInfo = new DirectoryInfo(directory);
			List<FileInfo> list = new List<FileInfo>(directoryInfo.GetFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
			if (list.Count == 0)
			{
				sb.AppendLine("      该目录存在，但没有匹配文件 —— 不存在");
				return 0;
			}
			list.Sort((FileInfo a, FileInfo b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
			count = list.Count;
			sb.AppendLine($"      清单: 共 {list.Count} 个（按修改时间新→旧）");
			foreach (FileInfo item in list)
			{
				string text = Path.GetRelativePath(directory, item.FullName);
				sb.AppendLine($"        - {DiagnosticGcuEvidenceInfo.Mask(text)}  {item.Length} 字节  mtime={item.LastWriteTime:yyyy-MM-dd HH:mm:ss}  mtimeUtc={item.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}");
			}
			foreach (FileInfo item2 in list)
			{
				AppendOneLogTail(sb, item2, ref budget);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("      该项读取失败（" + ex.Message + "）");
		}
		return count;
	}

	/// <summary>单个具名日志（例如 C:\MyControlCenter.log）的存在性 + 尾部 + 关键行。</summary>
	internal static void AppendSingleLog(StringBuilder sb, string path, ref int budget)
	{
		sb.AppendLine("    · 文件: " + DiagnosticGcuEvidenceInfo.Mask(path));
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!fileInfo.Exists)
			{
				sb.AppendLine("      不存在（机主/本机没有这个文件，已按缺失路径处理）");
				return;
			}
			sb.AppendLine($"      存在: {fileInfo.Length} 字节  mtime={fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
			AppendOneLogTail(sb, fileInfo, ref budget);
		}
		catch (Exception ex)
		{
			sb.AppendLine("      该项读取失败（" + ex.Message + "）");
		}
	}

	/// <summary>
	/// 单个文件：尾部（≤64 KiB 且不超过剩余预算，另有 400 行上限）、截断说明、内容行（已打码）、关键行抽取。
	/// </summary>
	private static void AppendOneLogTail(StringBuilder sb, FileInfo info, ref int budget)
	{
		try
		{
			long length = info.Length;
			int num = Math.Min(TailBytesPerFile, Math.Max(budget, 0));
			if (num <= 0)
			{
				sb.AppendLine($"      尾部: **已跳过**（本小节尾部总上限已用尽；该文件 {length} 字节，未读取）");
				return;
			}
			byte[] array = ReadTailBytes(info.FullName, num);
			budget -= array.Length;
			bool flag = length > array.Length;
			sb.AppendLine("      ── 尾部：" + DiagnosticGcuEvidenceInfo.Mask(info.FullName));
			if (flag)
			{
				sb.AppendLine($"        大小={length} 字节  mtime={info.LastWriteTime:yyyy-MM-dd HH:mm:ss} ⇒ 已按字节截断：只取末尾 {array.Length} 字节，丢弃更早的 {length - array.Length} 字节");
			}
			else
			{
				sb.AppendLine($"        大小={length} 字节  mtime={info.LastWriteTime:yyyy-MM-dd HH:mm:ss} ⇒ 全文取出（未截断）");
			}
			List<string> list = SplitLines(DecodeText(array));
			while (list.Count > 0 && list[0].Length == 0 && flag)
			{
				list.RemoveAt(0);
			}
			while (list.Count > 0 && list[list.Count - 1].Length == 0)
			{
				list.RemoveAt(list.Count - 1);
			}
			int num2 = Math.Min(list.Count, MaxLogLinesPerFile);
			int num3 = list.Count - num2;
			sb.AppendLine($"        内容: 尾部共 {list.Count} 行，贴出 {num2} 行" + ((num3 > 0) ? $"（已按行截断：丢弃更早的 {num3} 行）" : "（未按行截断）")
				+ $"；每行 ≤ {MaxLogLineChars} 字符，路径/用户名/机器名/序列号/MAC 已打码");
			for (int i = 0; i < num2; i++)
			{
				sb.AppendLine("        │ " + Truncate(DiagnosticGcuEvidenceInfo.Mask(list[num3 + i]), MaxLogLineChars));
			}
			AppendKeyLines(sb, info.FullName, length);
		}
		catch (Exception ex)
		{
			sb.AppendLine("      该项读取失败（" + ex.Message + "）");
		}
	}

	/// <summary>
	/// 关键行抽取：在「文件末尾 min(文件大小, 512 KiB)」窗口内按 <see cref="KeyRuleText"/> 找行，
	/// ⚠️ 命中就贴**原文行**（已打码），不只给计数；单文件最多贴 40 行并写明总数。
	/// </summary>
	private static void AppendKeyLines(StringBuilder sb, string path, long fileLength)
	{
		sb.AppendLine("        关键行抽取（规则: " + KeyRuleText + "；窗口=文件末尾 min(文件大小, " + KeyLineScanBytes / 1024 + " KB)，不区分大小写）:");
		try
		{
			int num = (int)Math.Min(fileLength, KeyLineScanBytes);
			if (num <= 0)
			{
				sb.AppendLine("          未命中任何关键行（文件为空）。");
				return;
			}
			string text = DecodeText(ReadTailBytes(path, num));
			List<string> list = new List<string>();
			int num2 = 0;
			foreach (string item in SplitLines(text))
			{
				string text2 = item.Trim();
				if (text2.Length != 0 && KeyLinePattern.IsMatch(text2))
				{
					num2++;
					if (list.Count < MaxKeyLinesPerFile)
					{
						list.Add(text2);
					}
				}
			}
			if (num2 == 0)
			{
				sb.AppendLine("          未命中任何关键行。");
				return;
			}
			sb.AppendLine($"          命中 {num2} 行" + ((num2 > list.Count) ? $"（只贴前 {list.Count} 行）" : "") + "：");
			foreach (string item2 in list)
			{
				sb.AppendLine("          ★ " + Truncate(DiagnosticGcuEvidenceInfo.Mask(item2), MaxKeyLineChars));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("          该项读取失败（" + ex.Message + "）");
		}
	}

	private static List<string> SplitLines(string text)
	{
		List<string> list = new List<string>();
		foreach (string item in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			list.Add(item);
		}
		return list;
	}

	/// <summary>读文件末尾 maxBytes 字节（共享读写，不锁文件）。</summary>
	private static byte[] ReadTailBytes(string path, int maxBytes)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		long num = Math.Min(fileStream.Length, maxBytes);
		byte[] array = new byte[num];
		fileStream.Seek(fileStream.Length - num, SeekOrigin.Begin);
		int num2 = 0;
		while (num2 < array.Length)
		{
			int num3 = fileStream.Read(array, num2, array.Length - num2);
			if (num3 <= 0)
			{
				break;
			}
			num2 += num3;
		}
		if (num2 == array.Length)
		{
			return array;
		}
		byte[] array2 = new byte[num2];
		Array.Copy(array, array2, num2);
		return array2;
	}

	/// <summary>无 BOM 感知的宽松 UTF-8 解码（GBK 字节退化成 U+FFFD，不影响判定）。</summary>
	private static string DecodeText(byte[] bytes)
	{
		int num = 0;
		if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
		{
			num = 3;
		}
		return Utf8Loose.GetString(bytes, num, bytes.Length - num);
	}

	private static string Truncate(string text, int max)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + $"…(已截断，原长 {text.Length} 字符)";
	}

	private static string Or(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return "(空)";
	}
}

/// <summary>
/// 2026-10-09（诊断补采·第二批，75:00 扩写）：诊断包的**隐私打码零命中自检**，口径与 <c>tools\GCU抓包.ps1</c>
/// 的「打码自检」一致 —— 真实用户名（%USERNAME%）、真实主机名（%COMPUTERNAME%）、C:\Users 下的用户目录名、
/// 系统序列号（Win32_ComputerSystemProduct.IdentifyingNumber）这些「裸值」，在产物里必须**零命中**。
/// <para>
/// 75:00 两处修正（真机实测报「未通过（6 处疑似漏打码）」的根因与修法）：
///   ① 受检集与打码用的裸值是**同一份** <see cref="DiagnosticGcuEvidenceInfo.SensitiveValues"/> 快照 ——
///      以前自检自己重建一份、Mask 却不知道有这些值，于是「自检报漏、Mask 也没盖」；
///      现在 <c>Mask</c> 会按这份快照逐个兜底（主机名 / 用户名 / 系统序列号 / 显示器 EDID 序列号）。
///   ② 新增**显示器 EDID 序列号**（真机 ICC 文件名 `&lt;PnP&gt;_&lt;序列号&gt;.icm` 里那一处）与
///      **文件名内序列号**规则的自检行。
/// </para>
/// ⚠️ 机型名（SystemProductName）按既有口径**不算**泄露值（要保留可判断性），故不参与自检。
/// ⚠️ 命中时只写「第几行 + 打码后的片段」，绝不把原值再抄一遍进包；只在本机生成时执行，不联网、不上传。
/// ⚠️ 本自检的覆盖范围是「系统信息.txt」全文（新采集的 GCU 侧日志/清单都在这份文本里）。
/// </summary>
internal static class DiagnosticPrivacySelfCheck
{
	internal static string Run(string text)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[隐私打码自检]");
		sb.AppendLine("（口径与 tools\\GCU抓包.ps1 的「打码自检」一致：真实用户名 / 真实主机名 / C:\\Users 下的用户目录名 /");
		sb.AppendLine("  系统序列号 / 显示器 EDID 序列号 —— 这些「裸值」在本包 系统信息.txt 里必须零命中；");
		sb.AppendLine("  ⚠️ 另有「文件名内序列号」形态（ICC .icm/.icc）与「面板号豁免」两条规则自检行。机型名按既有口径不算泄露值，不参与自检。只在本机生成，不联网。）");
		sb.AppendLine("  ⚠️ 面板号（ICC 文件名里的 BOE0B40 / BOE0CBE 这类：面板厂 3 字母 + 型号 4 位十六进制，恰好 7 位）按【面板信息】处理，");
		sb.AppendLine("     不打码、不计入「序列号裸值」命中；与面板无关的长串（真序列号）照旧打码并照旧会被抓出。");
		try
		{
			try
			{
				string userName = Environment.UserName;
				if (!string.IsNullOrWhiteSpace(userName))
				{
					DiagnosticGcuEvidenceInfo.SensitiveValues.Register(userName, "UserName（真实用户名 %USERNAME%）");
				}
			}
			catch
			{
			}
			try
			{
				string machineName = Environment.MachineName;
				if (!string.IsNullOrWhiteSpace(machineName))
				{
					DiagnosticGcuEvidenceInfo.SensitiveValues.Register(machineName, "HostName（真实主机名 %COMPUTERNAME%）");
				}
			}
			catch
			{
			}
			try
			{
				string text2 = Environment.GetFolderPath(Environment.SpecialFolder.System);
				string path = Path.Combine(Path.GetPathRoot(text2) ?? "C:\\", "Users");
				if (Directory.Exists(path))
				{
					foreach (string item in Directory.GetDirectories(path))
					{
						string fileName = Path.GetFileName(item);
						if (!string.IsNullOrWhiteSpace(fileName) && !IsSystemAccountFolder(fileName))
						{
							DiagnosticGcuEvidenceInfo.SensitiveValues.Register(fileName, "UserName（C:\\Users 下的用户目录名）");
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct");
				foreach (ManagementBaseObject item2 in managementObjectSearcher.Get())
				{
					string text3 = item2["IdentifyingNumber"]?.ToString()?.Trim();
					if (!string.IsNullOrWhiteSpace(text3) && text3.Length >= 6)
					{
						DiagnosticGcuEvidenceInfo.SensitiveValues.Register(text3, "序列号（Win32_ComputerSystemProduct.IdentifyingNumber）");
					}
				}
			}
			catch
			{
			}
			// 2026-10-09（75:00）：受检集**就是** Mask 用来兜底的那个快照（SensitiveValues）——
			// 显示器 EDID 序列号由 DiagnosticDisplayInfo 在采集时登记进来（真机 ICC 文件名里那一处漏的就是它）。
			// 2026-10-09（批次3 #3）：**面板号**（`BOE0B40` 这类，见 PanelIdentifiers）已在登记处被挡掉
			// ⇒ 它不再出现在受检集里；这里再加一道同口径的护栏，保证「自检报什么、Mask 盖什么」依然同源。
			List<KeyValuePair<string, string>> list = DiagnosticGcuEvidenceInfo.SensitiveValues.Snapshot();
			List<string> list2 = new List<string>();
			string[] array = text.Replace("\r\n", "\n").Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				string text4 = array[i];
				foreach (KeyValuePair<string, string> item3 in list)
				{
					if (DiagnosticGcuEvidenceInfo.PanelIdentifiers.IsPanelNumber(item3.Key))
					{
						continue;
					}
					if (DiagnosticGcuEvidenceInfo.SensitiveValues.IsUsableToken(item3.Key) && Regex.IsMatch(text4, "(?i)(?<![0-9A-Za-z])" + Regex.Escape(item3.Key) + "(?![0-9A-Za-z])", RegexOptions.CultureInvariant))
					{
						// ⚠️ 先 Mask 再截断（绝不能把没打码的片段截进包里）。
						list2.Add($"    第 {i + 1} 行 ← " + item3.Value + " ⇒ " + Truncate(DiagnosticGcuEvidenceInfo.Mask(text4.Trim()), 160));
						break;
					}
				}
			}
			sb.AppendLine($"  受检标识: {list.Count} 个（" + ((list.Count == 0) ? "一个都取不到，无法自检）" : string.Join("、", list.ConvertAll((KeyValuePair<string, string> p) => p.Value).ToArray()) + "）"));
			if (list2.Count == 0)
			{
				sb.AppendLine("  结果: 通过 —— 真实用户名 / 主机名 / 用户目录名 / 系统序列号 / 显示器 EDID 序列号在本包 系统信息.txt 里【零命中】。");
			}
			else
			{
				sb.AppendLine($"  ⚠️ 结果: 未通过（{list2.Count} 处疑似漏打码）—— 请先把下面这些行贴给开发者，暂勿直接外发本包：");
				foreach (string item4 in list2)
				{
					sb.AppendLine(item4);
				}
			}
			// 2026-10-09（75:00）：文件名里夹序列号（ICC `.icm`/`.icc`）的形态也顺带自检一次 ——
			// 它属于「打码规则是否生效」而不是「裸值是否零命中」，命中说明有文件名没被盖住。
			int num2 = 0;
			foreach (string item5 in array)
			{
				if (!string.Equals(DiagnosticGcuEvidenceInfo.MaskSerialsInFilenames(item5), item5, StringComparison.Ordinal))
				{
					num2++;
				}
			}
			sb.AppendLine($"  文件名序列号自检: 全文 {array.Length} 行里有 {num2} 行含「ICC 文件名（.icm/.icc）内序列号」形态（这些行已由规则打成 &lt;文件名内序列号已打码&gt;，不再外泄）。");
			// 2026-10-09（批次3 #3「打码放宽」）：面板号豁免的**可核对**计数 —— 机主看到 ICC 文件名时能对上账：
			// `<面板号>_<后缀>.icm` 里的面板号（BOE=面板厂 + 型号）**不算**序列号裸值、也**不打码**，
			// 因此它既不会出现在上面的「疑似漏打码」里，也不会被写成 &lt;文件名内序列号已打码&gt;。
			int num3 = 0;
			foreach (string item6 in array)
			{
				if (DiagnosticGcuEvidenceInfo.PanelIdentifiers.ContainsIccPanelIdentity(item6))
				{
					num3++;
				}
			}
			sb.AppendLine($"  面板号豁免: 全文 {num3} 行含「ICC 文件名里的面板号」形态（例如 BOE0B40 / BOE0CBE：面板厂 3 字母 + 型号 4 位十六进制，恰好 7 位）—— 按【面板信息】处理：不打码、不计入上面的「序列号裸值」命中，也不计入「文件名内序列号」。⚠️ 与面板无关的长串（主机/EDID 序列号那种）仍照旧打码并仍会被上面的自检抓出。");
			int num = 0;
			foreach (char c in text)
			{
				if (c == '\u0001')
				{
					num++;
				}
			}
			sb.AppendLine((num == 0) ? "  打码哨兵: 已全部还原（正文没有残留 U+0001）" : $"  ⚠️ 打码哨兵未还原：残留 {num} 处 U+0001，说明打码过程有 bug，请告知开发者。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.Message + "）");
		}
		return sb.ToString();
	}

	private static bool IsSystemAccountFolder(string name)
	{
		return name.Equals("Public", StringComparison.OrdinalIgnoreCase) || name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Equals("Default User", StringComparison.OrdinalIgnoreCase) || name.Equals("All Users", StringComparison.OrdinalIgnoreCase) || name.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) || name.Equals("AppData", StringComparison.OrdinalIgnoreCase) || name.Equals("WsiAccount", StringComparison.OrdinalIgnoreCase) || name.Equals("defaultuser0", StringComparison.OrdinalIgnoreCase);
	}

	private static string Truncate(string text, int max)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + $"…(已截断，原长 {text.Length} 字符)";
	}
}
