using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace MechrevoLite.Update;

internal static class UpdateInstaller
{
	internal const string ExpectedExePrefix = "L-Mechrevo";

	internal static string TempRoot => Path.Combine(Path.GetTempPath(), "L-Mechrevo-update");

	internal static string CurrentExePath => Environment.ProcessPath ?? Application.ExecutablePath;

	internal static string PackageFileName(UpdateInfo info)
	{
		string fileName = info.FileName;
		string text = ((fileName != null && fileName.Length > 0) ? Path.GetFileName(info.FileName) : null);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "L-Mechrevo-" + (info.LatestVersion ?? "update") + ".exe";
		}
		string text2 = string.Join('_', text.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		if (text2.Length <= 0)
		{
			return "L-Mechrevo-update.exe";
		}
		return text2;
	}

	internal static bool LooksLikeInstallerPackage(UpdateInfo info)
	{
		string fileName = info.FileName;
		string text = ((fileName != null && fileName.Length > 0) ? Path.GetFileName(info.FileName) : null);
		if (!string.IsNullOrWhiteSpace(text) && text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out Uri result) && result.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	internal static bool IsPeExecutable(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			return fileStream.ReadByte() == 77 && fileStream.ReadByte() == 90;
		}
		catch
		{
			return false;
		}
	}

	internal static async Task<DownloadResult> DownloadAsync(UpdateInfo info, IProgress<int>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(info.DownloadUrl))
		{
			return new DownloadResult(null, "服务端没有提供下载地址");
		}
		if (!UpdatePolicy.TryAcceptDownloadUrl(info.DownloadUrl, out Uri uri, out string reason))
		{
			Logger.WriteLine("更新包下载被拒：" + reason);
			return new DownloadResult(null, reason);
		}
		if (!UpdatePolicy.IsValidSha256(info.Sha256))
		{
			Logger.WriteLine("更新包下载被拒：服务端未提供有效的 SHA-256 校验值");
			return new DownloadResult(null, "服务端未提供有效的 SHA-256 校验值，拒绝下载");
		}
		if (!UpdatePolicy.IsAcceptablePackageSize(info.Size))
		{
			Logger.WriteLine("更新包下载被拒：服务端未提供有效的体积声明");
			return new DownloadResult(null, "服务端未提供有效的体积声明，拒绝下载");
		}
		long expectedSize = info.Size.Value;
		string text = Path.Combine(TempRoot, info.LatestVersion ?? "unknown", "attempt-" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(text, PackageFileName(info));
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(UpdateHttp.DownloadTimeout);
		try
		{
			Directory.CreateDirectory(text);
			CleanStaleTempArtifacts(text);
			using HttpResponseMessage response = await UpdateHttp.Download.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (UpdateHttp.IsRedirect(response.StatusCode))
			{
				string text2 = UpdateHttp.RedirectRefusedReason(response.StatusCode);
				Logger.WriteLine("更新包下载被拒：" + text2);
				return new DownloadResult(null, text2);
			}
			response.EnsureSuccessStatusCode();
			long? contentLength = response.Content.Headers.ContentLength;
			if (contentLength.HasValue)
			{
				long valueOrDefault = contentLength.GetValueOrDefault();
				if (valueOrDefault != expectedSize)
				{
					Logger.WriteLine($"更新包 Content-Length 与 JSON size 不符（{valueOrDefault} ≠ {expectedSize}）");
					return new DownloadResult(null, $"文件大小与服务器声明不符（{valueOrDefault} ≠ {expectedSize}）");
				}
				if (!UpdatePolicy.IsAcceptablePackageSize(valueOrDefault))
				{
					Logger.WriteLine($"更新包过大（声明 {valueOrDefault} 字节），已放弃");
					return new DownloadResult(null, $"更新包超过体积上限（声明 {valueOrDefault} 字节）");
				}
			}
			DownloadResult result;
			await using (Stream source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false))
			{
				long total = 0L;
				bool tooLarge = false;
				await using (FileStream target = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					byte[] buffer = new byte[81920];
					int num;
					while ((num = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(continueOnCapturedContext: false)) > 0)
					{
						total += num;
						if (total > expectedSize || total > 419430400)
						{
							tooLarge = true;
							break;
						}
						await target.WriteAsync(buffer.AsMemory(0, num), timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
						if (contentLength.HasValue)
						{
							long valueOrDefault2 = contentLength.GetValueOrDefault();
							if (valueOrDefault2 > 0)
							{
								progress?.Report((int)Math.Clamp(total * 100 / valueOrDefault2, 0L, 100L));
							}
						}
					}
				}
				if (tooLarge || total != expectedSize)
				{
					Logger.WriteLine($"更新包体积与 JSON 声明不符（{total} ≠ {expectedSize}），已中止下载");
					DeleteFileLogged(filePath);
					result = new DownloadResult(null, $"文件大小与服务器声明不符（{total} ≠ {expectedSize}）");
				}
				else
				{
					progress?.Report(100);
					Logger.WriteLine($"更新包已下载：{filePath}（{total} 字节）");
					result = new DownloadResult(filePath, null);
				}
			}
			return result;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine("更新包下载已取消（超时或调用方取消）");
			DeleteFileLogged(filePath);
			return new DownloadResult(null, "下载已取消或超时");
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("更新包下载失败：" + ex2.GetType().Name + " " + ex2.Message);
			DeleteFileLogged(filePath);
			return new DownloadResult(null, "下载失败：" + ex2.Message);
		}
	}

	internal static bool VerifyExternalArchive(string filePath, string? sha256, long? size, out string reason)
	{
		reason = "";
		try
		{
			if (!File.Exists(filePath))
			{
				reason = "外置 GCU 包不存在";
				return false;
			}
			long length = new FileInfo(filePath).Length;
			if (size.HasValue && length != size.Value)
			{
				reason = $"外置 GCU 包大小与服务器声明不符（{length} ≠ {size.Value}）";
				return false;
			}
			if (!UpdatePolicy.IsValidSha256(sha256))
			{
				reason = "外置 GCU 包缺少有效 SHA-256";
				return false;
			}
			string actual = ComputeSha256(filePath);
			if (!string.Equals(actual, sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				reason = "外置 GCU 包 SHA-256 不匹配";
				return false;
			}
			reason = "外置 GCU 包 SHA-256 校验通过";
			return true;
		}
		catch (Exception ex)
		{
			reason = "外置 GCU 包校验异常：" + ex.Message;
			return false;
		}
	}

	internal static PackageVerification Verify(string filePath, UpdateInfo info)
	{
		if (!File.Exists(filePath))
		{
			return new PackageVerification(Ok: false, "下载文件不存在");
		}
		if (!UpdatePolicy.IsValidSha256(info.Sha256))
		{
			return new PackageVerification(Ok: false, "服务端未提供有效的 SHA-256 校验值，拒绝安装");
		}
		long length = new FileInfo(filePath).Length;
		if (length <= 0)
		{
			return new PackageVerification(Ok: false, "下载文件为空");
		}
		if (!UpdatePolicy.MatchesDeclaredSize(length, info.Size))
		{
			if (!UpdatePolicy.IsAcceptablePackageSize(info.Size))
			{
				return new PackageVerification(Ok: false, "服务端未提供有效的文件大小声明，拒绝安装");
			}
			return new PackageVerification(Ok: false, $"文件大小与服务器声明不符（{length} ≠ {info.Size}）");
		}
		string text = ComputeSha256(filePath);
		if (!string.Equals(text, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			Logger.WriteLine("更新包 SHA-256 不匹配：期望 " + info.Sha256 + " 实际 " + text);
			return new PackageVerification(Ok: false, "更新包校验失败（SHA-256 不匹配）");
		}
		if (IsPeExecutable(filePath))
		{
			return new PackageVerification(Ok: true, "SHA-256 校验通过");
		}
		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(filePath);
			if (zipArchive.Entries.Count == 0)
			{
				return new PackageVerification(Ok: false, "更新包是空的");
			}
			ZipArchiveEntry zipArchiveEntry = FindPackageExe(zipArchive);
			if (zipArchiveEntry == null)
			{
				return new PackageVerification(Ok: false, "更新包里没有找到 L-Mechrevo*.exe");
			}
			string path = Path.Combine(Path.GetTempPath(), "lmechrevo-auth-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
			try
			{
				using (Stream stream = zipArchiveEntry.Open())
				{
					using FileStream destination = File.Create(path);
					stream.CopyTo(destination);
				}
				if (!UpdatePolicy.TryAcceptAuthenticode(path, out string reason))
				{
					return new PackageVerification(Ok: false, reason);
				}
			}
			finally
			{
				try
				{
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			return new PackageVerification(Ok: false, "更新包不是有效的 zip：" + ex.Message);
		}
		return new PackageVerification(Ok: true, "SHA-256 校验通过");
	}

	internal static void DiscardPackage(string? packagePath)
	{
		if (!string.IsNullOrWhiteSpace(packagePath))
		{
			TryDelete(packagePath);
			Logger.WriteLine("临时更新包已删除：" + packagePath);
		}
	}

	internal static string? ExtractPackage(string zipPath, UpdateInfo info)
	{
		string text = Path.Combine(TempRoot, info.LatestVersion ?? "unknown", "extracted");
		try
		{
			if (Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
			Directory.CreateDirectory(text);
			ZipFile.ExtractToDirectory(zipPath, text);
			using ZipArchive archive = ZipFile.OpenRead(zipPath);
			ZipArchiveEntry zipArchiveEntry = FindPackageExe(archive);
			if (zipArchiveEntry == null)
			{
				return null;
			}
			string fullPath = Path.GetFullPath(Path.Combine(text, zipArchiveEntry.FullName.Replace('/', Path.DirectorySeparatorChar)));
			if (!fullPath.StartsWith(Path.GetFullPath(text), StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return File.Exists(fullPath) ? fullPath : null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新包解压失败：" + ex.Message);
			return null;
		}
	}

	private static ZipArchiveEntry? FindPackageExe(ZipArchive archive)
	{
		return (from e in archive.Entries
			where e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			where e.Name.StartsWith("L-Mechrevo", StringComparison.OrdinalIgnoreCase)
			orderby e.FullName.Count((char c) => (c == '/' || c == '\\') ? true : false)
			select e).FirstOrDefault();
	}

	internal static bool CanSelfInstall(out string reason)
	{
		return CanSelfInstall(CurrentExePath, File.Exists, out reason);
	}

	internal static bool CanSelfInstall(string exePath, Func<string, bool> fileExists, out string reason)
	{
		string directoryName = Path.GetDirectoryName(exePath);
		if (string.IsNullOrEmpty(directoryName))
		{
			reason = "找不到程序目录";
			return false;
		}
		string arg = Path.Combine(directoryName, Path.GetFileNameWithoutExtension(exePath) + ".dll");
		if (fileExists(arg))
		{
			reason = "当前是框架依赖构建（程序目录里有同名 dll），自更新会破坏安装，请用下载页手动替换";
			return false;
		}
		reason = "";
		return true;
	}

	internal static bool StartInstaller(string setupPath)
	{
		try
		{
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = setupPath,
				UseShellExecute = true
			});
			if (process == null)
			{
				return false;
			}
			Logger.WriteLine($"安装包已启动（pid {process.Id}）：{setupPath}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("安装包启动失败：" + ex.Message);
			return false;
		}
	}

	internal static bool IsDllUpdatePackage(UpdateInfo info)
	{
		string name = info.FileName ?? "";
		string url = info.DownloadUrl ?? "";
		return name.Contains("update", StringComparison.OrdinalIgnoreCase)
			&& (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
			|| url.Contains("update", StringComparison.OrdinalIgnoreCase)
			&& (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
	}

	internal static bool VerifyDllPackage(string zipPath, out string reason)
	{
		reason = "";
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(zipPath);
			ZipArchiveEntry? manifestEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "manifest.json", StringComparison.OrdinalIgnoreCase));
			if (manifestEntry == null)
			{
				reason = "更新包缺少 manifest.json";
				return false;
			}
			using StreamReader reader = new(manifestEntry.Open());
			JObject manifest = JObject.Parse(reader.ReadToEnd());
			JArray? files = manifest["files"] as JArray;
			if (files == null || files.Count == 0)
			{
				reason = "更新包 manifest 没有文件列表";
				return false;
			}
			string root = Path.GetFullPath(Path.GetDirectoryName(zipPath) ?? "");
			foreach (JToken token in files)
			{
				string? rel = token.Value<string>("path");
				string? sha = token.Value<string>("sha256");
				long? size = token.Value<long?>("size");
				if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(sha) || !size.HasValue)
				{
					reason = "更新包 manifest 条目不完整";
					return false;
				}
				string normalized = rel.Replace('\\', '/');
				if (normalized.StartsWith("/") || normalized.Contains(".."))
				{
					reason = "更新包包含非法路径";
					return false;
				}
				ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
				if (entry == null || entry.Length != size.Value)
				{
					reason = "更新包文件缺失或大小不符：" + rel;
					return false;
				}
			}
			reason = "更新包 manifest 校验通过";
			return true;
		}
		catch (Exception ex)
		{
			reason = "更新包校验失败：" + ex.Message;
			return false;
		}
	}

	internal static bool PrepareDllUpdater(string zipPath, string installDir, out string reason)
	{
		reason = "";
		if (!VerifyDllPackage(zipPath, out reason)) return false;
		try
		{
			string root = Path.Combine(TempRoot, "dll-update", Guid.NewGuid().ToString("N"));
			string extractDir = Path.Combine(root, "package");
			string helperDir = Path.Combine(root, "helper");
			Directory.CreateDirectory(extractDir);
			ZipFile.ExtractToDirectory(zipPath, extractDir);
			CopyDirectory(installDir, helperDir);
			string exeName = Path.GetFileName(CurrentExePath);
			string helperExe = Path.Combine(helperDir, exeName);
			if (!File.Exists(helperExe))
			{
				reason = "更新器复制失败：找不到 " + exeName;
				return false;
			}
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = helperExe,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = helperDir,
				ArgumentList =
				{
					"--apply-dll-update", extractDir, installDir, Environment.ProcessId.ToString()
				}
			});
			if (process == null)
			{
				reason = "更新器启动失败";
				return false;
			}
			reason = "更新器已启动（pid " + process.Id + "）";
			return true;
		}
		catch (Exception ex)
		{
			reason = "更新器准备失败：" + ex.Message;
			return false;
		}
	}

	private static void CopyDirectory(string source, string target)
	{
		Directory.CreateDirectory(target);
		foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(dir.Replace(source, target));
		}
		foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
		{
			string dest = file.Replace(source, target);
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			File.Copy(file, dest, overwrite: true);
		}
	}

	internal static bool StartUpdater(string newExePath, string targetExePath)
	{
		try
		{
			string text = Path.Combine(TempRoot, "updater");
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, Path.GetFileName(CurrentExePath) ?? "L-Mechrevo.exe");
			File.Copy(CurrentExePath, text2, overwrite: true);
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = text2,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = text,
				ArgumentList = 
				{
					"--apply-update",
					newExePath,
					targetExePath,
					Environment.ProcessId.ToString()
				}
			});
			if (process == null)
			{
				return false;
			}
			Logger.WriteLine($"更新器已启动（pid {process.Id}），准备替换 {targetExePath}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新器启动失败：" + ex.Message);
			return false;
		}
	}

	internal static int RunUpdater(string[] args)
	{
		if (args.Length < 3)
		{
			return 2;
		}
		string text = args[0];
		string text2 = args[1];
		int pid = (int.TryParse(args[2], out var result) ? result : (-1));
		try
		{
			WaitForExit(pid, TimeSpan.FromSeconds(90L));
			if (!File.Exists(text))
			{
				return 2;
			}
			string text3 = text2 + ".bak";
			TryDelete(text3);
			if (File.Exists(text2))
			{
				File.Move(text2, text3);
			}
			try
			{
				File.Copy(text, text2, overwrite: true);
				if (new FileInfo(text).Length != new FileInfo(text2).Length)
				{
					throw new IOException("替换后大小不一致");
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("替换 exe 失败，尝试回滚：" + ex.Message);
				TryDelete(text2);
				if (File.Exists(text3))
				{
					File.Move(text3, text2);
				}
				Restart(text2);
				return 2;
			}
			Logger.WriteLine("更新完成：" + text2);
			Restart(text2);
			return 0;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("更新器异常：" + ex2.Message);
			return 2;
		}
	}

	private static void WaitForExit(int pid, TimeSpan timeout)
	{
		if (pid <= 0)
		{
			return;
		}
		try
		{
			using Process process = Process.GetProcessById(pid);
			if (!process.WaitForExit((int)timeout.TotalMilliseconds))
			{
				Logger.WriteLine("等待原进程退出超时，仍继续替换");
			}
		}
		catch (ArgumentException)
		{
		}
		Thread.Sleep(1200);
	}

	private static void Restart(string exePath)
	{
		try
		{
			if (File.Exists(exePath))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = exePath,
					UseShellExecute = true,
					WorkingDirectory = (Path.GetDirectoryName(exePath) ?? "")
				});
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新后重启失败：" + ex.Message);
		}
	}

	internal static string ComputeSha256(string filePath)
	{
		using FileStream source = File.OpenRead(filePath);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	internal static void CleanStaleTempArtifacts(string activeAttemptDirectory)
	{
		if (!Directory.Exists(TempRoot))
		{
			return;
		}
		string text = FullPathNoTrailingSeparator(activeAttemptDirectory);
		string directoryName = Path.GetDirectoryName(text);
		string right = Path.Combine(TempRoot, "updater");
		string[] directoriesOrEmpty = GetDirectoriesOrEmpty(TempRoot);
		foreach (string text2 in directoriesOrEmpty)
		{
			if (IsSamePath(text2, right))
			{
				continue;
			}
			if (directoryName == null || !IsSamePath(text2, directoryName))
			{
				DeleteDirectoryLogged(text2);
				continue;
			}
			string[] directoriesOrEmpty2 = GetDirectoriesOrEmpty(text2);
			foreach (string text3 in directoriesOrEmpty2)
			{
				if (!IsSamePath(text3, text))
				{
					DeleteDirectoryLogged(text3);
				}
			}
			directoriesOrEmpty2 = GetFilesOrEmpty(text2);
			for (int j = 0; j < directoriesOrEmpty2.Length; j++)
			{
				DeleteFileLogged(directoriesOrEmpty2[j]);
			}
		}
		directoriesOrEmpty = GetFilesOrEmpty(TempRoot);
		for (int i = 0; i < directoriesOrEmpty.Length; i++)
		{
			DeleteFileLogged(directoriesOrEmpty[i]);
		}
	}

	private static string FullPathNoTrailingSeparator(string path)
	{
		return Path.GetFullPath(path).TrimEnd(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		});
	}

	private static bool IsSamePath(string left, string right)
	{
		return string.Equals(FullPathNoTrailingSeparator(left), FullPathNoTrailingSeparator(right), StringComparison.OrdinalIgnoreCase);
	}

	private static string[] GetDirectoriesOrEmpty(string root)
	{
		try
		{
			return Directory.GetDirectories(root);
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"更新临时目录清理：无法枚举 {root} —— {ex.GetType().Name} {ex.Message}");
			return Array.Empty<string>();
		}
	}

	private static string[] GetFilesOrEmpty(string root)
	{
		try
		{
			return Directory.GetFiles(root);
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"更新临时目录清理：无法枚举 {root} —— {ex.GetType().Name} {ex.Message}");
			return Array.Empty<string>();
		}
	}

	private static string[] GetFilesRecursiveOrEmpty(string root)
	{
		try
		{
			return Directory.GetFiles(root, "*", SearchOption.AllDirectories);
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"更新临时目录清理：无法枚举 {root} —— {ex.GetType().Name} {ex.Message}");
			return Array.Empty<string>();
		}
	}

	private static void DeleteDirectoryLogged(string directory)
	{
		string[] filesRecursiveOrEmpty = GetFilesRecursiveOrEmpty(directory);
		for (int i = 0; i < filesRecursiveOrEmpty.Length; i++)
		{
			DeleteFileLogged(filesRecursiveOrEmpty[i]);
		}
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"更新临时目录清理：无法删除 {directory} —— {ex.GetType().Name} {ex.Message}");
		}
	}

	private static void DeleteFileLogged(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"临时文件删除失败：{path} —— {ex.GetType().Name} {ex.Message}");
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"临时文件删除失败：{path} —— {ex.GetType().Name} {ex.Message}");
		}
	}
}
