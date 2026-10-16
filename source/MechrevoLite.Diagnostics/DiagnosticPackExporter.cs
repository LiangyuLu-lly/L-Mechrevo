using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticPackExporter
{
	internal const string SystemInfoEntry = "系统信息.txt";

	internal const string GuideEntry = "说明.txt";

	internal const string RingBufferEntry = "日志/内存日志缓冲.txt";

	internal const string LogFolderEntry = "日志/";

	internal const string ConfigFolderEntry = "配置/";

	internal const string ConfigReadmeEntry = "配置/README-配置.txt";

	internal const string IssuesUrl = "https://github.com/LiangyuLu-lly/L-Mechrevo/issues";

	internal const long DefaultMaxLogBytes = 8388608L;

	private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

	/// <summary>2026-10-12 logfix2：日志文件的原始编码是 UTF-8 无 BOM，加进去的打码副本也保持一致。</summary>
	private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	internal static string BuildFileName(string version, DateTime timestamp)
	{
		return $"L-Mechrevo-诊断包-{version}-{timestamp:yyyyMMdd-HHmmss}.zip";
	}

	internal static DiagnosticPackResult Export(string zipPath, DiagnosticPackInputs inputs, long maxLogBytes = 8388608L)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(zipPath, "zipPath");
		ArgumentNullException.ThrowIfNull(inputs, "inputs");
		string fullPath = Path.GetFullPath(zipPath);
		string directoryName = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (File.Exists(fullPath))
		{
			File.Delete(fullPath);
		}
		List<string> entries = new List<string>();
		List<string> list = new List<string>();
		DateTime capturedAt = DateTime.Now;
		using (FileStream stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		{
			using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create);
			AddDirectory(zip, entries, "日志/");
			AddDirectory(zip, entries, "配置/");
			List<(DiagnosticPackFile, long?)> list2 = PlanLogs(inputs.LogFiles, maxLogBytes, list);
			List<(DiagnosticPackFile, long?)> list3 = PlanConfigs(inputs.ConfigFiles, list);
			list.Insert(0, "- 日志级别：" + (inputs.LogLevel ?? "未知") + "（off / error / all；本条是**本次导出时生效**的那一档）");
			list.Insert(1, "- 日志级别来源：" + (inputs.LogLevelSource ?? "未知"));
			list.Insert(2, "- 打包时间：" + capturedAt.ToString("yyyy-MM-dd HH:mm:ss") + "（本机本地时间）");
			list.Insert(3, "- 日志上限：单代 10 MiB、保留 3 代（日志目录合计 ≈ 40 MiB，超出自动整代轮转）");
			list.InsertRange(0, BuildGcuInventoryManifest());
			string text = inputs.SystemInfoText.TrimEnd() + Environment.NewLine + Environment.NewLine + "【打包清单】" + Environment.NewLine + string.Join(Environment.NewLine, list);
			// 2026-10-09（诊断补采·第二批）：隐私打码**零命中自检** —— 口径与 tools\GCU抓包.ps1 的「打码自检」一致；
			// 只在本地生成时对 系统信息.txt 全文跑一遍，结果附在文末（不联网、不上传、不改变已采集内容）。
			text = text + Environment.NewLine + Environment.NewLine + DiagnosticPrivacySelfCheck.Run(text);
			WriteText(zip, entries, "系统信息.txt", text);
			WriteText(zip, entries, "说明.txt", BuildGuide(inputs.Version, inputs.LogLevel, inputs.LogLevelSource, capturedAt));
			WriteText(zip, entries, "日志/内存日志缓冲.txt", BuildRingBufferText(inputs));
			WriteText(zip, entries, "配置/README-配置.txt", BuildConfigReadme(inputs.ConfigFiles));
			foreach (var item in list2)
			{
				CopyLogFile(zip, entries, item.Item1, item.Item2);
			}
			foreach (var item2 in list3)
			{
				CopyFile(zip, entries, item2.Item1, null);
			}
		}
		long length = new FileInfo(fullPath).Length;
		return new DiagnosticPackResult(fullPath, entries, length);
	}

	/// <summary>
	/// 2026-10-09（诊断补采·第二批）：打包清单开头的「这次多了哪几样」—— 机主一眼能看到新增了什么；
	/// ⚠️ 并写明「若某项不存在会明确标注」，免得机主以为漏采。只改文本，不改任何采集行为。
	/// </summary>
	private static List<string> BuildGcuInventoryManifest()
	{
		return new List<string>
		{
			"- 系统信息.txt：已包含 —— 本次（2026-10-09 第二批补采）新增/增强的小节：",
			"    · [GCU 关键文件] / [载荷落地]：**按注册表 GcuVariant 选本变体基线**逐个比大小（30 系 163 文件 / 40 系 5 / 40B 10 / 50 系 4），变体没有基线时明确写「该变体无落地基线，跳过判定」—— 不再拿 40A 的表去套 30 系（那是误报来源）；",
			"    · [GCU 侧日志（ControlCenter / MyControlCenter）]：3 条已知路径优先（命中就读内容 + 抽关键行），并新增 **[GCU 日志路径发现]**：底座根 + 安装根 + GCU 服务目录 + %ProgramData%\\ 递归找近 24 小时的 *.log，**只列路径/大小/mtime、不读内容**，写明「命中 N 个 / 取尾部 M 个」与「因超限而跳过」；",
			"    · [GCU 键盘灯效文件（KeyboardManager）]：AppSettings\\KeyboardManager.json 与 KeyboardManager\\settings.json **全文**（含大小与 mtime，能看出是否被 GCU 运行期改写；不存在就写明不存在）；",
			"    · [GCU 用户档案清单（UserPofiles）]：只列文件名 / 大小 / 修改时间（**不复制档案内容**，那是运行期用户档案），并列出 16 项期望清单（MainOption + Mode1..3_Profile1..5）里缺了哪几个；",
			"    · [GCU 侧日志（ControlCenter / MyControlCenter）]：目录清单 + 每个文件尾部 ≤64 KB（本小节合计 ≤512 KB）+ **关键行抽取**（LM_Manager / LM_Init / RGBKB / solution / UNKNOWN / Keyboard / Subscribe / SUBACK / Mqtt / Error / Exception，命中即贴原文行）；",
			"    · [GCU 安装日志与装后自检状态]：%ProgramData%\\L-Mechrevo\\ 与 \\logs\\ 两处 gcu-install-*.log 清单 + 尾部（≤64 KB/文件、小节合计 ≤256 KB）；gcu-install-status.json 存在则给出**原文全文**；",
			"    · [GCU 底座目录清单]：底座根（注册表 GcuServiceExe 推导，兼容两种布局）目录树 ≤3 层 + 每个文件大小 / 时间戳（不对全树哈希）+ 安装根 1 层清单；",
			"    · [隐私打码自检]：真实用户名 / 主机名 / 用户目录名 / 系统序列号 / **显示器 EDID 序列号**在 系统信息.txt 里的**零命中**自检结果（口径与 tools\\GCU抓包.ps1 一致），并附「文件名内序列号」与「面板号豁免」两条规则自检行（ICC 文件名里的面板号 BOE0B40 这类按【面板信息】处理，不打码、不计入序列号命中）；",
			"  ⚠️ 若某项不存在（例如机主机器上没有 %ProgramData%\\ControlCenter 目录、或没写过 gcu-install-*.log），对应小节会**明确标注「不存在」**，不会静默略过。"
		};
	}

	/// <summary>
	/// 2026-10-12 logfix2（用户原话「改log」）：让诊断包**自描述** —— 说明.txt 开头就写清
	/// ① 打包时间 ② 本次**生效**的日志级别与来源 ③ 文件日志的容量/代数 ④ 日志文件进包前已打码。
	/// 机主把包发出来时不用再回答「你日志级别开的什么」这类问题。
	/// </summary>
	internal static string BuildGuideHeader(string logLevel, string logLevelSource, DateTime? capturedAt)
	{
		return "L-Mechrevo 诊断包\n"
			+ "=================\n"
			+ "打包时间：" + (capturedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss") + "（本机本地时间）\n"
			+ "本次生效的日志级别：" + (logLevel ?? "未知") + "（来源：" + (logLevelSource ?? "未知") + "）\n"
			+ "文件日志容量：单代 10 MiB、保留 3 代（log.1/2/3.txt），合计 ≈ 40 MiB（超出自动整代轮转）\n"
			+ "内存日志缓冲：容量 " + FormatSize(Logger.CrashRingBufferBytes) + "（与日志级别无关，进程内常开，导出时快照）\n"
			+ "日志打码：日志\\ 下的文件（log.txt / log.N.txt / crash.txt / 内存日志缓冲.txt）都在**打包时**按与其它小节\n"
			+ "  同一套规则打码（用户名 / 主机名 / 用户目录路径 / 序列号 / MAC / GUID / 长十六进制串）；机主本机的原始日志不受影响。\n"
			+ "\n";
	}

	internal static string BuildGuide(string version, string logLevel = null, string logLevelSource = null, DateTime? capturedAt = null)
	{
		return BuildGuideHeader(logLevel, logLevelSource, capturedAt) + $"本压缩包由 L-Mechrevo 的「导出诊断包」功能生成，用于排查问题。它包含：\n  - 系统信息.txt：应用版本、Windows 版本、机型、CPU、GPU 与驱动、GCU 连通与能力位；\n      以及 2026-10-09 补采的 GCU 证据节：\n      · [GCU 装后自检]：安装脚本写的 gcu-install-status.json（解析字段 + 原文全文；没有就明说没有）；\n      · [GCU 关键文件]：按**本机 GcuVariant** 选基线的逐个文件大小 / SHA256 前 16 位 / 版本，并给「载荷是否落地」的结论（30 系 163 / 40 系 5 / 40B 10 / 50 系 4；该变体没有基线就写「跳过判定」）；\n      · [GCU 就绪键（ItemSupport）]：ServiceReady / ServiceLoadingReady / PerformanceReady；\n      · [GCU 侧日志（ControlCenter / MyControlCenter）]：日志清单 + 每文件尾部 ≤64 KB（合计 ≤512 KB）+ 关键行抽取；\n      · [GCU 日志路径发现]：底座根 / 安装根 / GCU 服务目录 / %ProgramData% 下递归找近 24h 的 *.log（只列路径 / 大小 / mtime，不读内容；写明命中数与因超限跳过项）；\n      · [GCU 键盘灯效文件（KeyboardManager）]：AppSettings\\KeyboardManager.json 与 KeyboardManager\\settings.json 全文 + 大小 / mtime；\n      · [GCU 安装日志与装后自检状态]：gcu-install-*.log 清单 + 尾部（两处都没有就明确写「不存在」）；\n      · [GCU 用户档案清单（UserPofiles）]：只列文件名 / 大小 / 修改时间（不复制档案内容），并报 16 项期望清单缺哪些；\n      · [GCU 底座目录清单]：底座根目录树 ≤3 层 + 文件名 / 大小 / 时间戳；\n      · [隐私打码自检]：真实用户名 / 主机名 / 用户目录名 / 系统序列号 / 显示器 EDID 序列号在 系统信息.txt 里零命中的自检结论；\n      ⚠️ 以上任何一项**不存在都会明确标注「不存在」**（例如没有 %ProgramData%\\ControlCenter 目录、旧安装器没写过安装日志）。\n  - 日志\\log.txt：应用运行日志（默认级别 all；级别为 OFF 时该文件不存在）。⚠️ 打包时已打码；已轮转的上一代在同一目录下（log.1/2/3.txt）；\n  - 日志\\crash.txt：进程异常退出时落盘的崩溃现场（含内存环形缓冲）。⚠️ 打包时已打码；\n  - 日志\\内存日志缓冲.txt：导出当时的内存环形缓冲快照（容量 {FormatSize(Logger.CrashRingBufferBytes)}，与日志级别无关）。⚠️ 打包时已打码；\n  - 配置\\：应用的配置文件副本（含 README-配置.txt 说明每个文件是什么）。\n\n应用版本：{version}\n\n如何提交\n--------\n请把本压缩包作为附件，提交到 GitHub Issues：\n{"https://github.com/LiangyuLu-lly/L-Mechrevo/issues"}\n或直接发给开发者。\n\n请在 Issue 里说明：\n  1. 你做了什么（操作步骤，越具体越好）；\n  2. 你期望发生什么；\n  3. 实际发生了什么（报错原文、截图）。\n\n隐私说明\n--------\n本压缩包在本地生成，不会自动上传、不会联网发送；请自行确认内容后再提交。\nGCU 侧日志与安装日志在采集时已打码（用户名 / 机器名 / 用户目录路径 / MAC / 序列号 / 长十六进制串），\n本应用自己的 日志\\ 文件（log.txt / log.N.txt / crash.txt / 内存日志缓冲.txt）在**打包时**用同一套规则打码，\n并在 系统信息.txt 末尾给出「零命中自检」结论；UserPofiles 只列清单、不复制档案内容。\n配置文件可能包含本机设备标识（例如已保存的液冷设备 MAC），介意请先检查。";
	}

	/// <summary>
	/// 2026-10-12 logfix2（用户原话「改log」）：这个快照文件现在必须**自描述** —— 写明生效级别与来源、
	/// 环形缓冲的真实容量（不再是写死的 64 KB），并说明内容已在打包时打码。
	/// </summary>
	internal static string BuildRingBufferText(DiagnosticPackInputs inputs)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("内存日志环形缓冲（导出时的快照）");
		stringBuilder.AppendLine("================================");
		stringBuilder.AppendLine("日志级别：" + (inputs.LogLevel ?? "未知") + "（来源：" + (inputs.LogLevelSource ?? "未知") + "）");
		stringBuilder.AppendLine("容量：" + FormatSize(Logger.CrashRingBufferBytes) + "（" + Logger.CrashRingBufferBytes + " 字节）—— 与日志级别无关：进程把每一行都常驻在内存里，导出诊断包时快照成这个文件，进程异常退出时追加到 日志/crash.txt。");
		stringBuilder.AppendLine("⚠️ 本文件进包前已按与 系统信息.txt 同一套规则打码（用户名 / 主机名 / 用户目录路径 / 序列号 / MAC / GUID / 长十六进制串）。");
		stringBuilder.AppendLine();
		stringBuilder.Append(MaskLogLines(inputs.CrashRingBufferText ?? "（内存缓冲为空：本次运行尚未产生任何日志行。）"));
		return stringBuilder.ToString();
	}

	/// <summary>
	/// 2026-10-12 logfix2（「改log」）：按行跑 <see cref="DiagnosticGcuEvidenceInfo.Mask" /> ——
	/// 打码规则全是行内匹配，逐行等价于全文；且内存里始终只有一行，不会把 4 MiB 缓冲翻倍成几个大字符串。
	/// </summary>
	private static string MaskLogLines(string text)
	{
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		for (int i = 0; i < array.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append('\n');
			}
			stringBuilder.Append(DiagnosticGcuEvidenceInfo.Mask(array[i]));
		}
		return stringBuilder.ToString();
	}

	internal static string BuildConfigReadme(IReadOnlyList<DiagnosticPackFile> configs)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("配置文件夹说明");
		stringBuilder.AppendLine("================");
		stringBuilder.AppendLine("本文件夹是 L-Mechrevo 配置文件的副本，用于复现问题时的设置差异。");
		stringBuilder.AppendLine("这些文件只包含设置项（模式、灯效、开关、窗口外观等），不含账号、密码或网络凭据。");
		stringBuilder.AppendLine("可能包含本机设备标识（例如已保存的液冷设备 MAC）。");
		stringBuilder.AppendLine();
		if (configs.Count == 0)
		{
			stringBuilder.AppendLine("（本次未采集到配置文件。）");
			return stringBuilder.ToString();
		}
		foreach (DiagnosticPackFile config in configs)
		{
			string value = (config.EntryName.StartsWith("配置/", StringComparison.Ordinal) ? config.EntryName.Substring("配置/".Length) : config.EntryName);
			string value2 = (string.IsNullOrWhiteSpace(config.Description) ? "配置文件" : config.Description);
			string value3 = (File.Exists(config.SourcePath) ? "已包含" : "缺失（源文件不存在）");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 3, stringBuilder2);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(value);
			handler.AppendLiteral("：");
			handler.AppendFormatted(value2);
			handler.AppendLiteral(" — ");
			handler.AppendFormatted(value3);
			stringBuilder2.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}

	private static List<(DiagnosticPackFile File, long? CopyBytes)> PlanLogs(IReadOnlyList<DiagnosticPackFile> logs, long maxLogBytes, List<string> manifest)
	{
		List<(DiagnosticPackFile, long?)> list = new List<(DiagnosticPackFile, long?)>();
		foreach (DiagnosticPackFile log in logs)
		{
			if (!File.Exists(log.SourcePath))
			{
				manifest.Add("- " + log.EntryName + "：缺失（源文件不存在）");
				continue;
			}
			long length = new FileInfo(log.SourcePath).Length;
			if (maxLogBytes > 0 && length > maxLogBytes)
			{
				manifest.Add($"- {log.EntryName}：已包含（超过 {FormatSize(maxLogBytes)} 上限，已截断至末尾 {FormatSize(maxLogBytes)}，原始 {FormatSize(length)}；已按同一套规则打码）");
				list.Add((log, maxLogBytes));
			}
			else
			{
				manifest.Add($"- {log.EntryName}：已包含（{FormatSize(length)}；已按同一套规则打码）");
				list.Add((log, null));
			}
		}
		return list;
	}

	private static List<(DiagnosticPackFile File, long? CopyBytes)> PlanConfigs(IReadOnlyList<DiagnosticPackFile> configs, List<string> manifest)
	{
		List<(DiagnosticPackFile, long?)> list = new List<(DiagnosticPackFile, long?)>();
		foreach (DiagnosticPackFile config in configs)
		{
			if (!File.Exists(config.SourcePath))
			{
				manifest.Add("- " + config.EntryName + "：缺失（源文件不存在）");
				continue;
			}
			manifest.Add($"- {config.EntryName}：已包含（{FormatSize(new FileInfo(config.SourcePath).Length)}）");
			list.Add((config, null));
		}
		return list;
	}

	private static void AddDirectory(ZipArchive zip, List<string> entries, string entryName)
	{
		zip.CreateEntry(entryName, CompressionLevel.NoCompression);
		entries.Add(entryName);
	}

	private static void WriteText(ZipArchive zip, List<string> entries, string entryName, string text)
	{
		using Stream stream = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
		using StreamWriter streamWriter = new StreamWriter(stream, Utf8Bom);
		streamWriter.Write(text);
		entries.Add(entryName);
	}

	/// <summary>
	/// 2026-10-12 logfix2（用户原话「改log」）：日志文件进包前过一遍**与其它小节同一套**打码
	/// （<see cref="DiagnosticGcuEvidenceInfo.Mask" />）。默认级别提到 all 之后包里会有大量运行日志，
	/// 实测日志里确实带着用户目录路径与用户名（例如 "Config loaded from C:\Users\&lt;名&gt;\…"），
	/// 而诊断包是要贴到公开 Issue 的。⚠️ 机主本机上的 log.txt 原文**不受影响**（只在打包这一份副本上打码）。
	/// ⚠️ 逐行读、逐行写：内存里永远只有一行，不会把 8 MiB 文件整体读进内存。
	/// </summary>
	private static void CopyLogFile(ZipArchive zip, List<string> entries, DiagnosticPackFile file, long? copyBytes)
	{
		using FileStream fileStream = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (copyBytes.HasValue && copyBytes.GetValueOrDefault() > 0 && fileStream.Length > copyBytes)
		{
			fileStream.Seek(fileStream.Length - copyBytes.Value, SeekOrigin.Begin);
			SkipToLineStart(fileStream);
		}
		using StreamReader streamReader = new StreamReader(fileStream, Utf8NoBom, detectEncodingFromByteOrderMarks: true, 65536, leaveOpen: true);
		using Stream destination = zip.CreateEntry(file.EntryName, CompressionLevel.Fastest).Open();
		using StreamWriter streamWriter = new StreamWriter(destination, Utf8NoBom, 65536);
		string text;
		while ((text = streamReader.ReadLine()) != null)
		{
			streamWriter.WriteLine(DiagnosticGcuEvidenceInfo.Mask(text));
		}
		entries.Add(file.EntryName);
	}

	/// <summary>
	/// 尾部截断会从半个行中间开始（也可能落在半个 UTF-8 字符中间）：丢弃到下一个 '\n'，
	/// 让包里的第一行是完整行。
	/// </summary>
	private static void SkipToLineStart(FileStream stream)
	{
		int num;
		while ((num = stream.ReadByte()) >= 0 && num != 10)
		{
		}
	}

	private static void CopyFile(ZipArchive zip, List<string> entries, DiagnosticPackFile file, long? copyBytes)
	{
		using FileStream fileStream = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (copyBytes.HasValue && copyBytes.GetValueOrDefault() > 0 && fileStream.Length > copyBytes)
		{
			fileStream.Seek(fileStream.Length - copyBytes.Value, SeekOrigin.Begin);
		}
		using Stream destination = zip.CreateEntry(file.EntryName, CompressionLevel.Fastest).Open();
		fileStream.CopyTo(destination);
		entries.Add(file.EntryName);
	}

	private static string FormatSize(long bytes)
	{
		if (bytes < 1048576)
		{
			if (bytes < 1024)
			{
				return $"{bytes} B";
			}
			return $"{(double)bytes / 1024.0:F0} KB";
		}
		return $"{(double)bytes / 1024.0 / 1024.0:F1} MB";
	}
}
