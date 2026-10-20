using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;

namespace MechrevoLite;

/// <summary>
/// 从外置 GCU 包导入并安装 GCU（用于精简安装包 / 手动删过 GCU 的机器）。
/// <para>只对 GCU 机型开放：非 GCU 后端（宝龙达 / 老接口）点状态条不弹导入。</para>
/// <para>识别方式（不只看文件名）：</para>
/// <list type="bullet">
/// <item>标准名：1020/30/40/50/WJXY.7z；</item>
/// <item>改名字的 .7z/.zip：解开看内容，按 GCUService.exe 版本/体积判变体后重打包；</item>
/// <item>已解压的树（含 AiStoneService\MyControlCenter\GCUService.exe）：直接重打包成变体 7z。</item>
/// </list>
/// </summary>
internal static class GcuPackageImport
{
	private const string SevenZipResource = "L-Mechrevo.7za.exe";

	private static readonly string[] VariantArchives = { "1020.7z", "30.7z", "40.7z", "50.7z", "WJXY.7z" };

	/// <summary>当前是否应该提供「导入 GCU 包」入口（GCU 后端但未连接/未安装）。</summary>
	internal static bool ShouldOffer()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || hw.ActiveBackend != MechrevoBackendKind.Gcu)
		{
			return false;
		}
		return !hw.IsConnected || !MechrevoHw.IsGcuServiceInstalled;
	}

	/// <summary>弹引导 → 选包 → 解出变体 payload → 调用安装脚本。</summary>
	internal static async Task OfferAsync(IWin32Window owner)
	{
		string baseDir = AppContext.BaseDirectory;
		string archive = Path.Combine(baseDir, "GCU.7z");
		string folder = Path.Combine(baseDir, "GCU");
		string source = null;
		if (File.Exists(archive))
		{
			source = archive;
		}
		else if (Directory.Exists(folder))
		{
			source = folder;
		}
		if (source == null)
		{
			using FolderBrowserDialog folderDialog = new FolderBrowserDialog
			{
				Description = Strings.GcuImportTitle + "：选择 GCU 包（.7z/.zip）或其所在文件夹",
				ShowNewFolderButton = false
			};
			if (folderDialog.ShowDialog(owner) != DialogResult.OK)
			{
				return;
			}
			source = folderDialog.SelectedPath;
		}
		if (MessageBox.Show(owner, Strings.GcuImportAsk + Environment.NewLine + Environment.NewLine + source,
			Strings.GcuImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
		string staging = Path.Combine(Path.GetTempPath(), "lm-gcu-import-" + Guid.NewGuid().ToString("N"));
		string toolDir = Path.Combine(Path.GetTempPath(), "lm-gcu-tool-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(staging, "payload"));
			Directory.CreateDirectory(toolDir);
			string sevenZip = Path.Combine(toolDir, "7za.exe");
			ExtractSevenZip(sevenZip);
			int found = Directory.Exists(source)
				? StageFromFolder(source, sevenZip, staging)
				: StageFromFile(source, sevenZip, staging);
			if (found == 0)
			{
				MessageBox.Show(owner, Strings.GcuImportNoPackage, Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			File.Copy(sevenZip, Path.Combine(staging, "7za.exe"), overwrite: true);
			string script = Path.Combine(baseDir, "GCU", "Install-Gcu.ps1");
			if (!File.Exists(script))
			{
				MessageBox.Show(owner, "找不到 GCU 安装脚本（{app}\\GCU\\Install-Gcu.ps1）。" + Environment.NewLine +
					"请使用完整安装包安装一次，或把 GCU\\Install-Gcu.ps1 放回应用目录。",
					Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			int exitCode = await Task.Run(() => RunInstallScript(script, staging, Path.Combine(baseDir, "GCU")));
			Logger.WriteError($"[gcu-import] source={source} variants={found} exit={exitCode}");
			if (exitCode == 0)
			{
				MessageBox.Show(owner, Strings.GcuImportDone, Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			else
			{
				MessageBox.Show(owner, string.Format(Strings.GcuImportFailed, exitCode), Strings.GcuImportTitle,
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 导入失败：" + ex);
			MessageBox.Show(owner, string.Format(Strings.GcuImportFailed, ex.Message), Strings.GcuImportTitle,
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			TryDeleteDirectory(staging);
			TryDeleteDirectory(toolDir);
		}
	}

	private static void ExtractSevenZip(string targetPath)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SevenZipResource)
			?? throw new InvalidOperationException("缺少内置 7za.exe 资源");
		using FileStream file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
		stream.CopyTo(file);
	}

	/// <summary>文件夹：先按标准名找；没有则找里面的任意 .7z/.zip；再没有则按“已解压的树”重打包。</summary>
	private static int StageFromFolder(string sourceDir, string sevenZip, string staging)
	{
		int found = StageKnownNames(new[] { sourceDir, Path.Combine(sourceDir, "payload"), Path.Combine(sourceDir, "GCU") }, staging);
		if (found > 0)
		{
			return found;
		}
		foreach (string file in Directory.EnumerateFiles(sourceDir))
		{
			string ext = Path.GetExtension(file).ToLowerInvariant();
			if (ext == ".7z" || ext == ".zip")
			{
				found += StageFromFile(file, sevenZip, staging);
			}
		}
		if (found > 0)
		{
			return found;
		}
		string tree = FindServiceTree(sourceDir);
		if (tree != null)
		{
			found += RepackTree(sevenZip, tree, staging);
		}
		return found;
	}

	/// <summary>单个文件：标准名直接复制；其它压缩包解开看内容 → 判变体 → 用真实 7z 重打包。</summary>
	private static int StageFromFile(string file, string sevenZip, string staging)
	{
		string name = Path.GetFileName(file);
		if (VariantArchives.Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			File.Copy(file, Path.Combine(staging, "payload", name), overwrite: true);
			return 1;
		}
		string extract = Path.Combine(staging, "_extract_" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(extract);
			if (RunProcess(sevenZip, $"x -y -o\"{extract}\" \"{file}\"", null) != 0)
			{
				Logger.WriteError($"[gcu-import] 解包失败（不是有效压缩包？）：{file}");
				return 0;
			}
			string tree = FindServiceTree(extract);
			if (tree == null)
			{
				Logger.WriteError($"[gcu-import] 压缩包内没找到 GCU 服务目录：{file}");
				return 0;
			}
			return RepackTree(sevenZip, tree, staging);
		}
		finally
		{
			TryDeleteDirectory(extract);
		}
	}

	private static int StageKnownNames(string[] directories, string staging)
	{
		int found = 0;
		foreach (string directory in directories)
		{
			if (!Directory.Exists(directory))
			{
				continue;
			}
			foreach (string name in VariantArchives)
			{
				string path = Path.Combine(directory, name);
				string dest = Path.Combine(staging, "payload", name);
				if (File.Exists(path) && !File.Exists(dest))
				{
					File.Copy(path, dest, overwrite: false);
					found++;
				}
			}
		}
		return found;
	}

	/// <summary>在目录树里找 GCU 服务目录（含 MyControlCenter\GCUService.exe），返回“可打包的父目录”。</summary>
	private static string FindServiceTree(string root)
	{
		try
		{
			foreach (string exe in Directory.EnumerateFiles(root, "GCUService.exe", SearchOption.AllDirectories))
			{
				DirectoryInfo controlCenter = Directory.GetParent(exe);
				if (controlCenter == null ||
					!string.Equals(controlCenter.Name, "MyControlCenter", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				DirectoryInfo serviceDir = controlCenter.Parent;
				if (serviceDir == null)
				{
					continue;
				}
				if (string.Equals(serviceDir.Name, "AiStoneService", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(serviceDir.Name, "UniwillService", StringComparison.OrdinalIgnoreCase))
				{
					return serviceDir.FullName;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 扫描 GCU 服务目录失败：" + ex.Message);
		}
		return null;
	}

	/// <summary>按 GCUService.exe 的特征判变体，然后把服务目录重打包成 &lt;变体&gt;.7z。</summary>
	private static int RepackTree(string sevenZip, string serviceDir, string staging)
	{
		string exe = Path.Combine(serviceDir, "MyControlCenter", "GCUService.exe");
		if (!File.Exists(exe))
		{
			return 0;
		}
		string variant = DetectVariant(exe);
		if (variant == null)
		{
			Logger.WriteError("[gcu-import] 无法从 GCUService.exe 判断变体：" + exe);
			return 0;
		}
		// Install-Gcu.ps1 认的归档布局 = 根下直接是 AiStoneService\（或旧 UniwillService\）；
		// 7za a 时把工作目录设为父目录、只压服务目录，即可得到该布局。
		string parent = Directory.GetParent(serviceDir)?.FullName;
		if (parent == null)
		{
			return 0;
		}
		string archiveName = variant + ".7z";
		string dest = Path.Combine(staging, "payload", archiveName);
		int exitCode = RunProcess(sevenZip, $"a -t7z -mx=5 \"{dest}\" \"{serviceDir}\"", parent);
		if (exitCode != 0)
		{
			Logger.WriteError($"[gcu-import] 重打包失败 exit={exitCode}：{serviceDir}");
			return 0;
		}
		Logger.WriteError($"[gcu-import] 内容识别：{Path.GetFileName(serviceDir)} ⇒ {archiveName}");
		return 1;
	}

	/// <summary>按版本/体积判变体（与安装器/应用内的 GCU 变体判定同口径）。</summary>
	private static string DetectVariant(string exePath)
	{
		try
		{
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);
			string version = (versionInfo.FileVersion ?? string.Empty).Trim();
			long size = new FileInfo(exePath).Length;
			if (version.StartsWith("1.2.", StringComparison.Ordinal))
			{
				return "50";
			}
			if (size > 5L * 1024 * 1024)
			{
				return "40";
			}
			if (version.StartsWith("1.0.2.70", StringComparison.Ordinal))
			{
				return "30";
			}
			return "1020";
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 读取 GCUService.exe 版本失败：" + ex.Message);
			return null;
		}
	}

	private static int RunInstallScript(string script, string staging, string targetDir)
	{
		string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -StagingRoot \"{staging}\" -TargetDir \"{targetDir}\"";
		return RunProcess(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), arguments, null);
	}

	private static int RunProcess(string fileName, string arguments, string workingDirectory)
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory ?? string.Empty,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			});
			if (process == null)
			{
				return -1;
			}
			if (!process.WaitForExit(300000))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				return -2;
			}
			return process.ExitCode;
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 进程启动失败：" + fileName + " · " + ex.Message);
			return -3;
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
