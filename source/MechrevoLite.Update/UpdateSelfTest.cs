using System;

namespace MechrevoLite.Update;

internal static class UpdateSelfTest
{
	internal static int Run(string[] args)
	{
		string text = ((args.Length != 0) ? args[0] : null);
		if (!string.IsNullOrWhiteSpace(text))
		{
			UpdateChecker.BaseUrlOverride = text;
		}
		Logger.WriteLine($"===== 更新自测开始：基址={UpdateChecker.BaseUrl} 本机版本={Program.ReleaseVersion} 自动检测开关={UpdateChecker.AutoCheckEnabled}");
		UpdateInfo result = UpdateChecker.CheckAsync(force: true).GetAwaiter().GetResult();
		if ((object)result == null)
		{
			Logger.WriteLine("更新自测：决定=拒绝（检测失败：网络或响应不合法）");
			Console.WriteLine("check failed");
			return 2;
		}
		Console.WriteLine($"current={result.CurrentVersion} latest={result.LatestVersion} available={result.UpdateAvailable}");
		Logger.WriteLine($"更新自测：本机={result.CurrentVersion} 服务端={result.LatestVersion ?? "(无)"} 服务端上报 current_version={result.ServerCurrentVersion ?? "(无)"} 有更新={result.UpdateAvailable}");
		if (!result.UpdateAvailable)
		{
			Logger.WriteLine("更新自测：决定=无更新（已是最新，或服务端未提供比本机更新的版本）");
			return 0;
		}
		if (string.IsNullOrWhiteSpace(result.DownloadUrl))
		{
			Logger.WriteLine("更新自测：决定=不自动安装（服务端没有 download_url，引导下载页）");
			return 3;
		}
		if (!UpdatePolicy.TryAcceptOffer(result, out string reason))
		{
			Logger.WriteLine("更新自测：决定=拒绝下载（" + reason + "）");
			Console.WriteLine("refused: " + reason);
			return 2;
		}
		Logger.WriteLine("更新自测：决定=接受，开始下载并强校验");
		DownloadResult result2 = UpdateInstaller.DownloadAsync(result).GetAwaiter().GetResult();
		string path = result2.Path;
		if (path == null)
		{
			Logger.WriteLine("更新自测：决定=拒绝（下载失败：" + (result2.Reason ?? "原因未知") + "）");
			Console.WriteLine("download failed: " + (result2.Reason ?? "unknown"));
			return 2;
		}
		PackageVerification packageVerification = UpdateInstaller.Verify(path, result);
		Logger.WriteLine("更新自测：校验 " + (packageVerification.Ok ? "通过" : "失败") + " —— " + packageVerification.Reason);
		if (!packageVerification.Ok)
		{
			UpdateInstaller.DiscardPackage(path);
			Logger.WriteLine("更新自测：决定=拒绝（校验失败，临时包已删除）");
			return 2;
		}
		if (UpdateInstaller.LooksLikeInstallerPackage(result) || UpdateInstaller.IsPeExecutable(path))
		{
			Logger.WriteLine("更新自测：决定=接受（安装包已校验；--updatetest 不拉起安装器）");
			return 0;
		}
		string text2 = UpdateInstaller.ExtractPackage(path, result);
		if (text2 == null)
		{
			Logger.WriteLine("更新自测：决定=拒绝（解压失败）");
			return 2;
		}
		string currentExePath = UpdateInstaller.CurrentExePath;
		if (!UpdateInstaller.StartUpdater(text2, currentExePath))
		{
			Logger.WriteLine("更新自测：决定=拒绝（更新器启动失败）");
			return 2;
		}
		Logger.WriteLine($"更新自测：决定=接受并安装（{text2} -> {currentExePath}），本进程退出");
		Logger.Close();
		return 0;
	}
}
