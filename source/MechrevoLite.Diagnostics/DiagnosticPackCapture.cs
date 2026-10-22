using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticPackCapture
{
	internal static DiagnosticPackInputs Capture()
	{
		List<DiagnosticPackFile> configFiles = (from file in AppConfig.ExportableConfigFiles()
			select new DiagnosticPackFile(file.SourcePath, "配置/" + file.FileName, file.Description)).ToList();
		// 2026-10-12 logfix2（用户原话「改log」）：包要能**自证**「这次生效的是哪一档、为什么是这一档」——
		// 环境变量 LMECHREVO_LOG_LEVEL 的优先级高于配置 log_level，那种情况下机主在设置里怎么点都不会生效，
		// 所以来源必须一起写进包（说明.txt 与 系统信息.txt 的打包清单都写）。
		string text = Environment.GetEnvironmentVariable(Logger.LevelOverrideVariable);
		string logLevelSource = (string.IsNullOrWhiteSpace(text)
			? "配置 log_level（可在 设置 → 日志级别 修改）"
			: "环境变量 " + Logger.LevelOverrideVariable + "=" + text + "（优先级高于配置 log_level）");
		return new DiagnosticPackInputs(Program.ReleaseVersion, Program.ReleaseLabel, DiagnosticSystemInfo.Build(), BuildLogFiles(), configFiles, Logger.SnapshotRingBuffer(), Logger.CurrentLevel.ToString(), logLevelSource);
	}

	internal static List<DiagnosticPackFile> BuildLogFiles()
	{
		// 2026-10-12 logfix: log.txt 不再"超限就截断/停写"，而是超过 10 MiB 时整代改名成
		// log.1.txt / log.2.txt / log.3.txt（只留 3 代）。轮转出去的那几代也一起进诊断包 ——
		// 它们正是"出事前"那段日志，丢了就白轮转了。
		List<DiagnosticPackFile> list = new List<DiagnosticPackFile>
		{
			new DiagnosticPackFile(Logger.logFile, "日志/" + Path.GetFileName(Logger.logFile), "应用运行日志（默认级别 all；级别 off 时不存在。超过 10 MB 时整代改名为 log.1.txt，只留 3 代。打包时按同一套规则打码）"),
			new DiagnosticPackFile(Logger.crashFile, "日志/" + Path.GetFileName(Logger.crashFile), "崩溃现场日志（进程异常退出时落盘的内存环形缓冲快照；打包时按同一套规则打码）")
		};
		// 2026-10-22（机主：诊断包体积爆大）：只带最近 1 代（log.1.txt）—— 够定位上一次崩溃/唤醒；
		// 更老的 log.2/log.3 不再进包（本机仍保留磁盘文件，需要时单独取）。
		string previous = Path.Combine(Logger.appPath, Path.GetFileNameWithoutExtension(Logger.logFile) + ".1" + Path.GetExtension(Logger.logFile));
		if (File.Exists(previous))
		{
			list.Add(new DiagnosticPackFile(previous, "日志/" + Path.GetFileName(previous), "上一次运行的日志（只保留最近 1 代进包；更老的 log.2/3 不进包）"));
		}
		return list;
	}
}
