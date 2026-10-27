using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MechrevoLite;

/// <summary>
/// 2026-10-09（exportperm）：设置的一键导出 / 一键导入（zip）。
///
/// 背景：用户设置只落在两处，此前除了单代 config.json.bak 没有任何导出/导入：
///   - %APPDATA%\MechrevoLite\：config.json（+ .bak）、rgb.cfg、lightbar.cfg、keyboard.cfg、lhm_sensors.txt；
///   - %ProgramData%\MechrevoLite\：config.json（+ .bak，机器级回退副本，AppConfig 日志里
///     "Config loaded from C:\ProgramData\MechrevoLite\config.json" 指的就是这份）。
///
/// 本类只处理**用户设置**：日志（log.txt / crash.txt）与诊断产物一律不进备份包。
/// 导入是"按文件整体覆盖 + 覆盖前先备份"，不做逐键合并；键的作用域语义
/// （ModelScopedConfig / AppConfig 的机型作用域键）完全不在这里解释，交给下次启动的 LM 去读。
/// </summary>
internal static class SettingsBackup
{
	/// <summary>备份包里的恢复说明（导入时不会被还原到磁盘）。</summary>
	internal const string ReadmeEntryName = "README-restore.txt";

	/// <summary>备份包里的两个根目录名：对应 %APPDATA%\MechrevoLite 与 %ProgramData%\MechrevoLite。</summary>
	internal const string RoamingEntryFolder = "Roaming";

	internal const string ProgramDataEntryFolder = "ProgramData";

	/// <summary>导入前自动备份现有设置时用的目录名前缀（最终形如 userdata-backup-20261009-200000）。</summary>
	internal const string BackupFolderPrefix = "userdata-backup-";

	/// <summary>%APPDATA%\MechrevoLite 里参与备份的设置文件（缺失即跳过，并在 README 与结果里如实列出）。</summary>
	private static readonly string[] RoamingFiles = new string[9] { "config.json", "config.json.bak", "rgb.cfg", "lightbar.cfg", "logolight.cfg", "keyboard.cfg", "lighting.user-configured", "fan_curve_bd.cfg", "lhm_sensors.txt" };

	/// <summary>%APPDATA%\MechrevoLite 里按命名规则参与备份的文件（如宝龙达每个自定义档的风扇曲线）。</summary>
	private static readonly string[] RoamingPatterns = new string[1] { "baolongda_fancurve_*.cfg" };

	/// <summary>%APPDATA%\MechrevoLite 下参与备份的子目录（如自定义 ICC）。</summary>
	private static readonly string[] RoamingSubdirs = new string[1] { "customICC" };

	private static readonly string[] IccExtensions = new string[2] { ".icc", ".icm" };

	private static bool IsIccFile(string path)
	{
		string extension = Path.GetExtension(path);
		foreach (string candidate in IccExtensions)
		{
			if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>%ProgramData%\MechrevoLite 里参与备份的设置文件。</summary>
	private static readonly string[] ProgramDataFiles = new string[2] { "config.json", "config.json.bak" };

	private const long MaxImportEntryBytes = 4194304L;

	private const long MaxImportTotalBytes = 16777216L;

	private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

	/// <summary>%APPDATA%\MechrevoLite（用户级设置与灯效配置）。</summary>
	internal static string RoamingDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite");

	/// <summary>%ProgramData%\MechrevoLite（机器级回退配置）。</summary>
	internal static string ProgramDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MechrevoLite");

	internal static string BuildFileName(DateTime timestamp)
	{
		return "LM-设置备份-" + timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
	}

	/// <summary>默认保存/打开目录：桌面 → 下载 → 文档 → 程序目录（与诊断包导出的取向一致）。</summary>
	internal static string ResolveDefaultDirectory()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		if (Directory.Exists(folderPath))
		{
			return folderPath;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		if (Directory.Exists(text))
		{
			return text;
		}
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		if (Directory.Exists(folderPath2))
		{
			return folderPath2;
		}
		return AppContext.BaseDirectory;
	}

	/// <summary>把两处设置文件打成一个 zip（含 README-restore.txt）。失败即抛，由调用方如实报错。</summary>
	internal static SettingsBackupExportResult Export(string zipPath, DateTime timestamp)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(zipPath, "zipPath");
		string fullPath = Path.GetFullPath(zipPath);
		string directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		if (File.Exists(fullPath))
		{
			File.Delete(fullPath);
		}
		List<SettingsBackupEntry> included = new List<SettingsBackupEntry>();
		List<string> skipped = new List<string>();
		using (FileStream stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		{
			using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create);
			foreach ((string entryName, string sourcePath) in BuildExportPlan())
			{
				if (!File.Exists(sourcePath))
				{
					skipped.Add(entryName);
					continue;
				}
				using FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using Stream destination = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
				source.CopyTo(destination);
				included.Add(new SettingsBackupEntry(entryName, source.Length));
			}
			WriteReadme(zip, timestamp, included, skipped);
		}
		return new SettingsBackupExportResult(fullPath, new FileInfo(fullPath).Length, included, skipped);
	}

	/// <summary>
	/// 导入：先整体校验（不落盘），再把现有设置备份到 %APPDATA%\MechrevoLite\userdata-backup-&lt;时间戳&gt;\，
	/// 最后把包里的文件覆盖回两处。单个文件写失败不会被吞掉 —— 记进 Failed 由调用方如实展示。
	/// </summary>
	internal static SettingsBackupImportResult Import(string zipPath, DateTime timestamp)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(zipPath, "zipPath");
		string fullPath = Path.GetFullPath(zipPath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("找不到备份文件：" + fullPath, fullPath);
		}
		Dictionary<string, byte[]> payload = ReadAndValidate(fullPath, out List<string> ignored);
		if (payload.Count == 0)
		{
			throw new InvalidDataException("压缩包里没有任何 L-Mechrevo 设置文件（期望 Roaming\\config.json、Roaming\\rgb.cfg、ProgramData\\config.json 之类）。");
		}
		string backupDirectory = Path.Combine(RoamingDirectory, BackupFolderPrefix + timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
		Directory.CreateDirectory(backupDirectory);
		List<string> backedUp = BackupExisting(backupDirectory);
		List<string> restored = new List<string>();
		List<string> failed = new List<string>();
		foreach (KeyValuePair<string, byte[]> item in payload)
		{
			if (!TryResolveTargetPath(item.Key, out string targetPath))
			{
				failed.Add(item.Key + "：无法解析目标路径");
				continue;
			}
			try
			{
				string parent = Path.GetDirectoryName(targetPath);
				if (!string.IsNullOrEmpty(parent))
				{
					Directory.CreateDirectory(parent);
				}
				File.WriteAllBytes(targetPath, item.Value);
				restored.Add(item.Key);
			}
			catch (Exception ex)
			{
				failed.Add(item.Key + "：" + ex.Message);
				Logger.WriteLine("Settings backup import failed for " + item.Key + ": " + ex.Message);
			}
		}
		return new SettingsBackupImportResult(fullPath, backupDirectory, backedUp, restored, failed, ignored);
	}

	private static IEnumerable<(string EntryName, string SourcePath)> BuildExportPlan()
	{
		foreach (string file in RoamingFiles)
		{
			yield return (RoamingEntryFolder + "/" + file, Path.Combine(RoamingDirectory, file));
		}
		foreach (string file in ProgramDataFiles)
		{
			yield return (ProgramDataEntryFolder + "/" + file, Path.Combine(ProgramDataDirectory, file));
		}
		foreach (string pattern in RoamingPatterns)
		{
			if (!Directory.Exists(RoamingDirectory))
			{
				yield break;
			}
			foreach (string path in Directory.EnumerateFiles(RoamingDirectory, pattern))
			{
				yield return (RoamingEntryFolder + "/" + Path.GetFileName(path), path);
			}
		}
		foreach (string subdir in RoamingSubdirs)
		{
			string directory = Path.Combine(RoamingDirectory, subdir);
			if (!Directory.Exists(directory))
			{
				continue;
			}
			foreach (string path in Directory.EnumerateFiles(directory))
			{
				if (!IsIccFile(path))
				{
					continue;
				}
				yield return (RoamingEntryFolder + "/" + subdir + "/" + Path.GetFileName(path), path);
			}
		}
	}

	/// <summary>文件名是否匹配带 * 的简单通配（只允许一个 *）。</summary>
	private static bool MatchFilePattern(string name, string pattern)
	{
		int star = pattern.IndexOf('*');
		if (star < 0)
		{
			return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
		}
		string prefix = pattern.Substring(0, star);
		string suffix = pattern.Substring(star + 1);
		return name.Length >= prefix.Length + suffix.Length
			&& name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 校验并读出包里**认得的**设置文件。认得的条目 =（Roaming|ProgramData）/（白名单文件名），
	/// 其余条目一律忽略（不还原），带路径穿越或超限的条目直接拒绝。
	/// </summary>
	private static Dictionary<string, byte[]> ReadAndValidate(string zipPath, out List<string> ignored)
	{
		Dictionary<string, byte[]> payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		ignored = new List<string>();
		long total = 0L;
		using FileStream stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read);
		foreach (ZipArchiveEntry entry in zip.Entries)
		{
			string name = entry.FullName.Replace('\\', '/').TrimStart('/');
			if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal))
			{
				continue;
			}
			if (name.IndexOf("..", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(name))
			{
				throw new InvalidDataException("压缩包里存在非法条目名（路径穿越）：" + entry.FullName);
			}
			if (string.Equals(name, ReadmeEntryName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (!TryCanonicalize(name, out string canonicalEntryName))
			{
				ignored.Add(name);
				continue;
			}
			if (entry.Length > MaxImportEntryBytes)
			{
				throw new InvalidDataException("条目过大，拒绝导入：" + name);
			}
			total += entry.Length;
			if (total > MaxImportTotalBytes)
			{
				throw new InvalidDataException("备份包解压后超过 16 MB，拒绝导入。");
			}
			using Stream entryStream = entry.Open();
			using MemoryStream buffer = new MemoryStream();
			entryStream.CopyTo(buffer);
			payload[canonicalEntryName] = buffer.ToArray();
		}
		return payload;
	}

	private static bool IsKnownEntry(string entryName)
	{
		return TryCanonicalize(entryName, out string _);
	}

	/// <summary>
	/// 把包内条目名规范化成白名单里的标准写法（根目录与文件名都按白名单的大小写），
	/// 顺带挡住任何非 &lt;根&gt;/&lt;白名单文件名&gt; 的形状（含路径穿越）。
	/// </summary>
	private static bool TryCanonicalize(string entryName, out string canonicalEntryName)
	{
		canonicalEntryName = "";
		string normalized = entryName.Replace('\\', '/').TrimStart('/');
		string[] parts = normalized.Split('/');
		if (parts.Length == 3)
		{
			if (!string.Equals(parts[0], RoamingEntryFolder, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			bool knownSubdir = false;
			foreach (string subdir in RoamingSubdirs)
			{
				if (string.Equals(parts[1], subdir, StringComparison.OrdinalIgnoreCase))
				{
					knownSubdir = true;
					break;
				}
			}
			if (!knownSubdir)
			{
				return false;
			}
			bool knownExt = false;
			foreach (string ext in IccExtensions)
			{
				if (string.Equals(Path.GetExtension(parts[2]), ext, StringComparison.OrdinalIgnoreCase))
				{
					knownExt = true;
					break;
				}
			}
			if (!knownExt || parts[2].IndexOfAny(new char[2] { '\\', ':' }) >= 0)
			{
				return false;
			}
			canonicalEntryName = parts[0] + "/" + parts[1] + "/" + parts[2];
			return true;
		}
		if (parts.Length != 2)
		{
			return false;
		}
		string[] whitelist;
		string root;
		if (string.Equals(parts[0], RoamingEntryFolder, StringComparison.OrdinalIgnoreCase))
		{
			root = RoamingEntryFolder;
			whitelist = RoamingFiles;
		}
		else if (string.Equals(parts[0], ProgramDataEntryFolder, StringComparison.OrdinalIgnoreCase))
		{
			root = ProgramDataEntryFolder;
			whitelist = ProgramDataFiles;
		}
		else
		{
			return false;
		}
		foreach (string known in whitelist)
		{
			if (string.Equals(known, parts[1], StringComparison.OrdinalIgnoreCase))
			{
				canonicalEntryName = root + "/" + known;
				return true;
			}
		}
		if (string.Equals(root, RoamingEntryFolder, StringComparison.Ordinal)
			&& parts[1].IndexOfAny(new char[2] { '/', '\\' }) < 0)
		{
			foreach (string pattern in RoamingPatterns)
			{
				if (MatchFilePattern(parts[1], pattern))
				{
					canonicalEntryName = root + "/" + parts[1];
					return true;
				}
			}
		}
		return false;
	}

	private static bool TryResolveTargetPath(string canonicalEntryName, out string targetPath)
	{
		targetPath = "";
		string[] parts = canonicalEntryName.Split('/');
		if (!IsKnownEntry(canonicalEntryName))
		{
			return false;
		}
		if (parts.Length == 3)
		{
			targetPath = Path.Combine(RoamingDirectory, parts[1], parts[2]);
			return true;
		}
		if (parts.Length != 2)
		{
			return false;
		}
		string directory = (string.Equals(parts[0], RoamingEntryFolder, StringComparison.OrdinalIgnoreCase) ? RoamingDirectory : ProgramDataDirectory);
		targetPath = Path.Combine(directory, parts[1]);
		return true;
	}

	/// <summary>把两处**现有**设置文件复制进 userdata-backup-&lt;时间戳&gt;\（Roaming\ 与 ProgramData\ 两个子目录）。</summary>
	private static List<string> BackupExisting(string backupDirectory)
	{
		List<string> backedUp = new List<string>();
		foreach (string file in RoamingFiles)
		{
			string source = Path.Combine(RoamingDirectory, file);
			if (CopyIfExists(source, Path.Combine(backupDirectory, RoamingEntryFolder, file)))
			{
				backedUp.Add(RoamingEntryFolder + "/" + file);
			}
		}
		foreach (string file in ProgramDataFiles)
		{
			string source = Path.Combine(ProgramDataDirectory, file);
			if (CopyIfExists(source, Path.Combine(backupDirectory, ProgramDataEntryFolder, file)))
			{
				backedUp.Add(ProgramDataEntryFolder + "/" + file);
			}
		}
		if (Directory.Exists(RoamingDirectory))
		{
			foreach (string pattern in RoamingPatterns)
			{
				foreach (string path in Directory.EnumerateFiles(RoamingDirectory, pattern))
				{
					string fileName = Path.GetFileName(path);
					if (CopyIfExists(path, Path.Combine(backupDirectory, RoamingEntryFolder, fileName)))
					{
						backedUp.Add(RoamingEntryFolder + "/" + fileName);
					}
				}
			}
			foreach (string subdir in RoamingSubdirs)
			{
				string directory = Path.Combine(RoamingDirectory, subdir);
				if (!Directory.Exists(directory))
				{
					continue;
				}
				foreach (string path in Directory.EnumerateFiles(directory))
				{
					if (!IsIccFile(path))
					{
						continue;
					}
					string fileName = Path.GetFileName(path);
					if (CopyIfExists(path, Path.Combine(backupDirectory, RoamingEntryFolder, subdir, fileName)))
					{
						backedUp.Add(RoamingEntryFolder + "/" + subdir + "/" + fileName);
					}
				}
			}
		}
		return backedUp;
	}

	private static bool CopyIfExists(string source, string destination)
	{
		try
		{
			if (!File.Exists(source))
			{
				return false;
			}
			string parent = Path.GetDirectoryName(destination);
			if (!string.IsNullOrEmpty(parent))
			{
				Directory.CreateDirectory(parent);
			}
			File.Copy(source, destination, overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			// 备份单文件失败不阻断导入（调用方会在结果里看到 failed 列表为空但备份条数偏少），
			// 但必须留痕，绝不静默。
			Logger.WriteLine("Settings backup pre-import copy failed for " + source + ": " + ex.Message);
			return false;
		}
	}

	private static void WriteReadme(ZipArchive zip, DateTime timestamp, List<SettingsBackupEntry> included, List<string> skipped)
	{
		StringBuilder text = new StringBuilder();
		text.AppendLine("L-Mechrevo 设置备份（一键导出）");
		text.AppendLine("==============================");
		text.AppendLine("生成时间：" + timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		text.AppendLine("应用版本：" + Program.ReleaseVersion);
		text.AppendLine("来源：%APPDATA%\\MechrevoLite 与 %ProgramData%\\MechrevoLite");
		text.AppendLine();
		text.AppendLine("包内是什么");
		text.AppendLine("----------");
		text.AppendLine("只包含用户设置，不含日志（log.txt / crash.txt）与缓存、不含诊断产物。");
		text.AppendLine("  Roaming/<文件名>      → 恢复目标 %APPDATA%\\MechrevoLite\\<文件名>");
		text.AppendLine("  ProgramData/<文件名>  → 恢复目标 %ProgramData%\\MechrevoLite\\<文件名>");
		text.AppendLine();
		text.AppendLine("本次包含（" + included.Count.ToString(CultureInfo.InvariantCulture) + " 个文件）：");
		if (included.Count == 0)
		{
			text.AppendLine("  （无：导出时两处目录里都没有可打包的设置文件）");
		}
		foreach (SettingsBackupEntry entry in included)
		{
			text.AppendLine("  " + entry.EntryName + "（" + entry.Bytes.ToString(CultureInfo.InvariantCulture) + " 字节）");
		}
		if (skipped.Count > 0)
		{
			text.AppendLine();
			text.AppendLine("本次缺失（导出时文件不存在，未打包）：");
			foreach (string name in skipped)
			{
				text.AppendLine("  " + name);
			}
		}
		text.AppendLine();
		text.AppendLine("怎么恢复");
		text.AppendLine("--------");
		text.AppendLine("方法一（推荐）：打开 L-Mechrevo →【设置】→「设置备份」→「导入设置…」，选中本 zip。");
		text.AppendLine("  导入会先把现有设置备份到 %APPDATA%\\MechrevoLite\\" + BackupFolderPrefix + "<时间戳>\\，再覆盖写回两处；");
		text.AppendLine("  完成后请立即重启 L-Mechrevo（重启前，正在运行的实例可能把内存里的设置再写回去）。");
		text.AppendLine("方法二（手动）：把 Roaming/ 里的文件复制回 %APPDATA%\\MechrevoLite\\；");
		text.AppendLine("  把 ProgramData/ 里的文件复制回 %ProgramData%\\MechrevoLite\\（该处写入通常需要管理员权限），然后重启 L-Mechrevo。");
		text.AppendLine();
		text.AppendLine("注意");
		text.AppendLine("----");
		text.AppendLine("- 导入是按文件整体覆盖，不做逐键合并，也不会删除包里没有的文件。");
		text.AppendLine("- 本文件（" + ReadmeEntryName + "）只是说明，导入时不会被写回磁盘。");
		text.AppendLine("- 换机型后请不要直接套用旧机的机型作用域设置；导入后建议核对一遍性能/显卡/灯效各项。");
		using Stream stream = zip.CreateEntry(ReadmeEntryName, CompressionLevel.Optimal).Open();
		using StreamWriter writer = new StreamWriter(stream, Utf8Bom);
		writer.Write(text.ToString());
	}
}

/// <summary>备份包里的一个条目（zip 内相对路径 + 原始字节数）。</summary>
internal sealed record SettingsBackupEntry(string EntryName, long Bytes);

/// <summary>导出结果：zip 路径、体积、已包含条目、因源文件不存在而跳过的条目。</summary>
internal sealed record SettingsBackupExportResult(string ZipPath, long SizeBytes, IReadOnlyList<SettingsBackupEntry> Included, IReadOnlyList<string> Skipped);

/// <summary>导入结果：备份目录、成功覆盖的条目、失败的条目（含原因）、被忽略的无关条目。</summary>
internal sealed record SettingsBackupImportResult(string ZipPath, string BackupDirectory, IReadOnlyList<string> BackedUp, IReadOnlyList<string> Restored, IReadOnlyList<string> Failed, IReadOnlyList<string> Ignored);
