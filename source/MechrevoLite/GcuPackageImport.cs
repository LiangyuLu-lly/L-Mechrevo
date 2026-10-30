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
/// <item>标准名：1020/30/40/50/WJ.7z；</item>
/// <item>改名字的 .7z/.zip：解开看内容，按 GCUService.exe 版本/体积判变体后重打包；</item>
/// <item>已解压的树（含 AiStoneService\MyControlCenter\GCUService.exe）：直接重打包成变体 7z。</item>
/// </list>
/// </summary>
internal static class GcuPackageImport
{

	private static readonly string[] VariantArchives = { "1020.7z", "30.7z", "40.7z", "50.7z", "WJ.7z" };

	/// <summary>当前是否应该提供「导入 GCU 包」入口（GCU 后端但未连接/未安装）。</summary>
	internal static bool ShouldOffer()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return true;
		}
		// 明确不是 GCU 的机型不给导入入口：宝龙达（本机不装 GCU）/ 老接口（不经过 GCU）
		if (hw.ActiveBackend == MechrevoBackendKind.Baolongda || hw.ActiveBackend == MechrevoBackendKind.LegacyMifs)
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
		else if (Directory.Exists(folder) && HasUsablePayload(folder))
		{
			source = folder;
		}
		// 自动检测到包：确认；选「否」= 改手动选择
		if (source != null)
		{
			string askAuto = NormalizeNewlines(Strings.GcuImportAsk) + Environment.NewLine + Environment.NewLine + source;
			if (MessageBox.Show(owner, askAuto, Strings.GcuImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				goto confirmedWithoutGoto;
			}
			source = null;
		}
		if (source == null)
		{
			DialogResult choice = MessageBox.Show(owner,
				"请选择 GCU 包的形式：" + Environment.NewLine + Environment.NewLine +
				"「是」= 选择压缩包文件（.7z / .zip，支持改过名字的）" + Environment.NewLine +
				"「否」= 选择 GCU 文件夹（已解压目录，或把包放在名为 GCU 的文件夹里）" + Environment.NewLine +
				"「取消」= 放弃导入",
				Strings.GcuImportTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
			if (choice == DialogResult.Cancel)
			{
				return;
			}
			if (choice == DialogResult.Yes)
			{
				using OpenFileDialog fileDialog = new OpenFileDialog
				{
					Title = "选择 GCU 压缩包",
					Filter = "GCU 包 (*.7z;*.zip)|*.7z;*.zip|所有文件 (*.*)|*.*",
					CheckFileExists = true
				};
				if (fileDialog.ShowDialog(owner) != DialogResult.OK)
				{
					return;
				}
				source = fileDialog.FileName;
			}
			else
			{
				MessageBox.Show(owner, "请把 GCU 包（.7z/.zip）或已解压的 GCU 目录放进一个名为「GCU」的文件夹，" +
					Environment.NewLine + "然后在下一步选择该文件夹。", Strings.GcuImportTitle,
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				using FolderBrowserDialog folderDialog = new FolderBrowserDialog
				{
					Description = "选择 GCU 文件夹",
					ShowNewFolderButton = false,
					SelectedPath = folder
				};
				if (folderDialog.ShowDialog(owner) != DialogResult.OK)
				{
					return;
				}
				string selected = folderDialog.SelectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string nested = Path.Combine(selected, "GCU");
				if (string.Equals(Path.GetFileName(selected), "GCU", StringComparison.OrdinalIgnoreCase))
				{
					source = selected;
				}
				else if (Directory.Exists(nested))
				{
					source = nested;
				}
				else
				{
					MessageBox.Show(owner, "请把 GCU 包或已解压的 GCU 目录放进一个名为「GCU」的文件夹后再选它。" +
						Environment.NewLine + Environment.NewLine +
						"（直接选下载目录这类文件夹不会被扫描，避免误识别无关压缩包）",
						Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
			}
		}
		if (MessageBox.Show(owner, NormalizeNewlines(Strings.GcuImportAsk) + Environment.NewLine + Environment.NewLine + source,
			Strings.GcuImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			return;
		}
	confirmedWithoutGoto:
		if (source == null)
		{
			return;
		}
		string staging = Path.Combine(Path.GetTempPath(), "lm-gcu-import-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(staging, "payload"));
			string sevenZip = ResolveSevenZip();
			int found = Directory.Exists(source)
				? StageFromFolder(source, sevenZip, staging)
				: StageFromFile(source, sevenZip, staging);
			if (found == 0)
			{
				MessageBox.Show(owner, Strings.GcuImportNoPackage, Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			try
			{
				string staged = string.Join(", ", Directory.EnumerateFiles(Path.Combine(staging, "payload")).Select(f => Path.GetFileName(f)));
				Logger.WriteError($"[gcu-import] source={source} found={found} payload=[{staged}]");
			}
			catch
			{
			}
			File.Copy(sevenZip, Path.Combine(staging, "7za.exe"), overwrite: true);
			string[] stagedVariants = Directory.EnumerateFiles(Path.Combine(staging, "payload"))
				.Select(f => Path.GetFileNameWithoutExtension(f))
				.Where(v => VariantArchives.Contains(v + ".7z", StringComparer.OrdinalIgnoreCase))
				.ToArray();
			string installVariant = ((stagedVariants.Length == 1) ? stagedVariants[0] : "Auto");
			if (stagedVariants.Length == 1)
			{
				// 单变体：仍做一次“本机需要 vs 实际提供”的校验提示（不阻止，用户确认后按所选变体安装）
				string expectedSingle = ExpectedVariant();
				// 40 系优先 40；只有缺 40 时才允许用 50（不再弹“变体不匹配”）
				if (expectedSingle == "40" && string.Equals(installVariant, "50", StringComparison.OrdinalIgnoreCase))
				{
					Logger.WriteError("[gcu-import] 40 系未提供 40 包 ⇒ 退回 50 包（DB 由 GpuDbControl 补）");
				}
				else if (expectedSingle != null && !string.Equals(expectedSingle, installVariant, StringComparison.OrdinalIgnoreCase))
				{
					if (MessageBox.Show(owner, "本机需要 " + expectedSingle + " 系 GCU，实际提供：" + installVariant + " 系 GCU"
						+ Environment.NewLine + Environment.NewLine + "仍要安装手动选择的 " + installVariant + " 系 GCU 吗？",
						Strings.GcuImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					{
						return;
					}
					Logger.WriteError("[gcu-import] 用户确认：按手动选择的变体安装 " + installVariant + "（本机需要 " + expectedSingle + "）");
				}
				Logger.WriteError("[gcu-import] 单变体包 ⇒ 直接安装变体 " + installVariant + "（不按硬件 Auto 选择）");
			}
			else
			{
				string expectedVariant = ExpectedVariant();
				if (expectedVariant != null && !File.Exists(Path.Combine(staging, "payload", expectedVariant + ".7z")))
				{
					string stagedNames = string.Join("、", stagedVariants.Select(v => v + " 系 GCU"));
					if (MessageBox.Show(owner, "本机需要 " + expectedVariant + " 系 GCU，实际提供：" + stagedNames + Environment.NewLine + Environment.NewLine +
						"仍然继续尝试安装吗？", Strings.GcuImportTitle,
						MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					{
						return;
					}
				}
			}
			string script = Path.Combine(baseDir, "GCU", "Install-Gcu.ps1");
			if (!File.Exists(script))
			{
				MessageBox.Show(owner, "找不到 GCU 安装脚本（{app}\\GCU\\Install-Gcu.ps1）。" + Environment.NewLine +
					"请使用完整安装包安装一次，或把 GCU\\Install-Gcu.ps1 放回应用目录。",
					Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			int exitCode = await Task.Run(() => RunInstallScript(script, staging, Path.Combine(baseDir, "GCU"), installVariant));
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
		}
	}



	/// <summary>解压目录里任意层级的标准变体 7z（只认固定文件名，避免误收无关包）。</summary>
	private static int StageNestedArchives(string root, string staging)
	{
		int found = 0;
		foreach (string name in VariantArchives)
		{
			foreach (string path in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
			{
				string dest = Path.Combine(staging, "payload", name);
				if (!File.Exists(dest))
				{
					File.Copy(path, dest, overwrite: false);
					found++;
				}
			}
		}
		return found;
	}

	/// <summary>当前机器需要的 GCU 变体（拿不到就返回 null，不猜）。</summary>
	private static string ExpectedVariant()
	{
		MechrevoHw hw = Program.hw;
		if (hw != null)
		{
			string fromHw = hw.DgpuGeneration switch
			{
				MechrevoLite.Gpu.DgpuGenerationKind.Gen50 => "50",
				// 40 系改用 50 包（DB 由 GpuDbControl 补）
				MechrevoLite.Gpu.DgpuGenerationKind.Gen40 => "40",
				MechrevoLite.Gpu.DgpuGenerationKind.Gen30 => "30",
				_ => null
			};
			if (fromHw != null)
			{
				return fromHw;
			}
		}
		// GCU 未连接时 hw 拿不到代际 ⇒ 用安装器的变体选择脚本现算（纯只读，读注册表/PCI id）
		try
		{
			string baseDir = AppContext.BaseDirectory;
			string selector = Path.Combine(baseDir, "GCU", "Select-GcuPayload.ps1");
			if (File.Exists(selector))
			{
				string outFile = Path.Combine(Path.GetTempPath(), "lm-gcu-variant-" + Guid.NewGuid().ToString("N") + ".txt");
				try
				{
					string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
					int code = RunProcess(ps, $"-NoProfile -ExecutionPolicy Bypass -File \"{selector}\" -Variant Auto -OutputFile \"{outFile}\"", null);
					if (code == 0 && File.Exists(outFile))
					{
						string variant = File.ReadAllText(outFile).Trim().ToUpperInvariant();
						if (variant.Length != 0)
						{
							Logger.WriteError($"[gcu-import] 本机所需变体（选择脚本判定）={variant}");
							return variant;
						}
					}
				}
				finally
				{
					try
					{
						File.Delete(outFile);
					}
					catch
					{
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 变体选择脚本失败：" + ex.Message);
		}
		return null;
	}

	/// <summary>安装脚本失败时的可读原因：读本次日志尾部的 FATAL 行。</summary>
	private static string ReadInstallLogTail()
	{
		try
		{
			string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo", "logs");
			FileInfo latest = null;
			foreach (string file in Directory.EnumerateFiles(dir, "gcu-install-*.log"))
			{
				FileInfo info = new FileInfo(file);
				if (latest == null || info.LastWriteTimeUtc > latest.LastWriteTimeUtc)
				{
					latest = info;
				}
			}
			if (latest == null)
			{
				return string.Empty;
			}
			string[] lines = File.ReadAllLines(latest.FullName);
			for (int i = lines.Length - 1; i >= Math.Max(0, lines.Length - 40); i--)
			{
				if (lines[i].Contains("FATAL", StringComparison.Ordinal))
				{
					return lines[i].Trim();
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 读取安装日志失败：" + ex.Message);
		}
		return string.Empty;
	}

	/// <summary>用本机已存在的 GCU 文件重新打包并调用安装脚本（重新注册服务/ACL/启动）。</summary>
	private static async Task RepairFromInstalledAsync(IWin32Window owner, string baseDir, string serviceDir)
	{
		string staging = Path.Combine(Path.GetTempPath(), "lm-gcu-repair-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(staging, "payload"));
			string sevenZip = ResolveSevenZip();
			if (RepackTree(sevenZip, serviceDir, staging) == 0)
			{
				MessageBox.Show(owner, "用现有 GCU 文件重打包失败，无法重新注册。", Strings.GcuImportTitle,
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			string variant = Directory.EnumerateFiles(Path.Combine(staging, "payload"))
				.Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? "Auto";
			File.Copy(sevenZip, Path.Combine(staging, "7za.exe"), overwrite: true);
			string script = Path.Combine(baseDir, "GCU", "Install-Gcu.ps1");
			if (!File.Exists(script))
			{
				MessageBox.Show(owner, "找不到 GCU 安装脚本（{app}\\GCU\\Install-Gcu.ps1）。", Strings.GcuImportTitle,
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			int exitCode = await Task.Run(() => RunInstallScript(script, staging, Path.Combine(baseDir, "GCU"), variant));
			Logger.WriteError($"[gcu-import] 用现有文件重新注册：variant={variant} exit={exitCode}");
			if (exitCode == 0)
			{
				MessageBox.Show(owner, Strings.GcuImportDone, Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			else
			{
				string reason = ReadInstallLogTail();
				MessageBox.Show(owner, string.Format(Strings.GcuImportFailed, exitCode)
					+ (string.IsNullOrEmpty(reason) ? string.Empty : Environment.NewLine + Environment.NewLine + reason),
					Strings.GcuImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("[gcu-import] 用现有文件重新注册失败：" + ex);
			MessageBox.Show(owner, string.Format(Strings.GcuImportFailed, ex.Message), Strings.GcuImportTitle,
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			TryDeleteDirectory(staging);
		}
	}

	/// <summary>取应用目录里的 7za.exe（随安装器装机，不再内嵌进 DLL）。</summary>
	private static string ResolveSevenZip()
	{
		string baseDir = AppContext.BaseDirectory;
		string[] candidates =
		{
			Path.Combine(baseDir, "GCU", "7za.exe"),
			Path.Combine(baseDir, "7za.exe")
		};
		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		throw new FileNotFoundException("缺少 7za.exe（应随安装器一起装到 GCU 目录）。请重新运行安装器，或把 7za.exe 放到应用目录。");
	}

	/// <summary>目录里是否真的含 GCU 包/解压树（自动检测路径用；没有就走手动选择）。</summary>
	private static bool HasUsablePayload(string directory)
	{
		foreach (string candidate in new[] { directory, Path.Combine(directory, "payload"), Path.Combine(directory, "GCU") })
		{
			foreach (string name in VariantArchives)
			{
				if (File.Exists(Path.Combine(candidate, name)))
				{
					return true;
				}
			}
		}
		foreach (string file in Directory.EnumerateFiles(directory))
		{
			string ext = Path.GetExtension(file).ToLowerInvariant();
			if (ext == ".7z" || ext == ".zip")
			{
				return true;
			}
		}
		return FindServiceTree(directory) != null;
	}

	/// <summary>resx 里可能写成字面量 \n（旧资源），这里统一换成真实换行。</summary>
	private static string NormalizeNewlines(string text)
	{
		return text?.Replace("\\n", Environment.NewLine) ?? string.Empty;
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

	/// <summary>按 GCUService.exe 的特征判变体，然后按归档规范重打包成 &lt;变体&gt;.7z。</summary>
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
		// Install-Gcu.ps1 按变体表认归档布局：
		//   1020 -> 20\UniwillService   30 -> 30\UniwillService
		//   40   -> 40\AiStoneService   50 -> 50\AiStoneService   WJ -> WJXY\AiStoneService
		(string innerRoot, string serviceLeaf) = variant switch
		{
			"1020" => ("20", "UniwillService"),
			"30" => ("30", "UniwillService"),
			"WJ" => ("WJXY", "AiStoneService"),
			_ => (variant, "AiStoneService")
		};
		string packageRoot = Path.Combine(staging, "_pkg_" + Guid.NewGuid().ToString("N"));
		string destination = Path.Combine(packageRoot, innerRoot, serviceLeaf);
		try
		{
			string innerDir = Path.Combine(packageRoot, innerRoot);
			Directory.CreateDirectory(innerDir);
			CopyDirectory(serviceDir, destination);
			string dest = Path.Combine(staging, "payload", variant + ".7z");
			int exitCode = RunProcess(sevenZip, $"a -t7z -mx=5 \"{dest}\" \".\\{innerRoot}\"", packageRoot);
			if (exitCode != 0)
			{
				Logger.WriteError($"[gcu-import] 重打包失败 exit={exitCode}：{serviceDir}");
				return 0;
			}
			Logger.WriteError($"[gcu-import] 内容识别：{Path.GetFileName(serviceDir)} ⇒ {variant}.7z（{innerRoot}\\{serviceLeaf}）");
			return 1;
		}
		finally
		{
			TryDeleteDirectory(packageRoot);
		}
	}

	private static void CopyDirectory(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(source))
		{
			File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
		}
		foreach (string directory in Directory.EnumerateDirectories(source))
		{
			CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
		}
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

	private static int RunInstallScript(string script, string staging, string targetDir, string variant)
	{
		string variantArg = (string.IsNullOrEmpty(variant) || string.Equals(variant, "Auto", StringComparison.OrdinalIgnoreCase)) ? string.Empty : " -Variant " + variant;
		string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -StagingRoot \"{staging}\" -TargetDir \"{targetDir}\"{variantArg}";
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
