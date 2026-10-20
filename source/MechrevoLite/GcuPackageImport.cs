using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;

namespace MechrevoLite;

/// <summary>
/// 从外置 GCU 包导入并安装 GCU（用于精简安装包 / 手动删过 GCU 的机器）。
/// <para>只对 GCU 机型开放：非 GCU 后端（宝龙达 / 老接口）点状态条不弹导入。</para>
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
		else if (Directory.Exists(folder) && Directory.GetFiles(folder, "*.7z").Length > 0)
		{
			source = folder;
		}
		if (source == null)
		{
			using OpenFileDialog dialog = new OpenFileDialog
			{
				Title = Strings.GcuImportTitle,
				Filter = "GCU 包 (*.7z)|*.7z|所有文件 (*.*)|*.*",
				CheckFileExists = true
			};
			if (dialog.ShowDialog(owner) != DialogResult.OK)
			{
				return;
			}
			source = dialog.FileName;
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
				? StageFromFolder(source, staging)
				: StageFromArchive(sevenZip, source, staging);
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

	private static int StageFromFolder(string sourceDir, string staging)
	{
		int found = 0;
		string[] directories = { sourceDir, Path.Combine(sourceDir, "payload"), Path.Combine(sourceDir, "GCU") };
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

	private static int StageFromArchive(string sevenZip, string archivePath, string staging)
	{
		string extracted = Path.Combine(staging, "_extract");
		Directory.CreateDirectory(extracted);
		int exitCode = RunProcess(sevenZip, $"x -y -o\"{extracted}\" \"{archivePath}\"");
		if (exitCode != 0)
		{
			Logger.WriteError($"[gcu-import] 7za 解压失败 exit={exitCode} archive={archivePath}");
			return 0;
		}
		return StageFromFolder(extracted, staging);
	}

	private static int RunInstallScript(string script, string staging, string targetDir)
	{
		string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -StagingRoot \"{staging}\" -TargetDir \"{targetDir}\"";
		return RunProcess(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), arguments);
	}

	private static int RunProcess(string fileName, string arguments)
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
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
