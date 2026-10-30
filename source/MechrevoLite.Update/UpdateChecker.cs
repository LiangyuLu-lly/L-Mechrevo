using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MechrevoLite.Update;

internal static class UpdateChecker
{
	internal const string DefaultBaseUrl = "https://stats.l-mechrevo.cn";

	internal const string BetaChannel = "beta";

	internal static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(6L);

	internal static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(6);

	internal const long MaxPackageBytes = 419430400L;

	private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

	private static UpdateInfo? cached;

	internal static Func<string, Task<string>>? HttpGetOverride { get; set; }

	internal static string? BaseUrlOverride { get; set; }

	internal static string BaseUrl => NormalizeBaseUrl(BaseUrlOverride ?? AppConfig.GetString("update_base_url") ?? "https://stats.l-mechrevo.cn");

	internal static bool AutoCheckEnabled => AppConfig.Get("check_updates", 1) != 0;

	internal static UpdateInfo? Cached => cached;

	internal static bool ShouldAutoCheck(DateTime nowUtc, DateTime lastCheckUtc)
	{
		return nowUtc - lastCheckUtc >= AutoCheckInterval;
	}

	internal static bool ShouldAutoCheckNow(DateTime nowUtc)
	{
		return ShouldAutoCheck(nowUtc, DateTimeOffset.FromUnixTimeSeconds(AppConfig.Get("update_last_check", 0)).UtcDateTime);
	}

	internal static string BuildCheckUrl(string baseUrl, string version, string channel = "beta")
	{
		string value = NormalizeBaseUrl(baseUrl);
		return $"{value}/api/update_check.php?version={Uri.EscapeDataString(version)}&channel={Uri.EscapeDataString(channel)}";
	}

	internal const string GitHubOwner = "xmsjx";
	internal const string GitHubRepo = "L-Mechrevo-Next";
	internal const string GitHubReleasesApi = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=30";

	/// <summary>从 GitHub Releases 读取最新应用包（排除 GCU 服务包等非应用发布）。</summary>
	internal static async Task<UpdateInfo?> CheckGitHubAsync(bool force = false, string? versionOverride = null)
	{
		await Gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!force && cached != null)
			{
				return cached;
			}
			if (!force && !ShouldAutoCheckNow(DateTime.UtcNow))
			{
				return cached;
			}
			string version = versionOverride ?? Program.ReleaseVersion;
			string json;
			try
			{
				using HttpRequestMessage request = new(HttpMethod.Get, GitHubReleasesApi);
				request.Headers.UserAgent.ParseAdd("L-Mechrevo/" + Program.ReleaseLabel);
				request.Headers.Accept.ParseAdd("application/vnd.github+json");
				using HttpResponseMessage response = await UpdateHttp.Check.SendAsync(request).ConfigureAwait(continueOnCapturedContext: false);
				response.EnsureSuccessStatusCode();
				json = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("GitHub 更新检测失败（静默跳过）：" + ex.GetType().Name + " " + ex.Message);
				return null;
			}
			UpdateInfo? info = ParseGitHubReleases(json, version);
			if (info == null)
			{
				Logger.WriteLine("GitHub 更新检测失败：没有可用的应用 Release");
				return null;
			}
			cached = info;
			AppConfig.Set("update_last_check", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			Logger.WriteLine($"GitHub 更新检测：本机={info.CurrentVersion} 最新={info.LatestVersion ?? "(无)"} 有更新={info.UpdateAvailable} 资产={info.FileName ?? "(无)"}");
			return info;
		}
		finally
		{
			Gate.Release();
		}
	}

	/// <summary>版本号里的 -betaN.M 是发布标签，不是补丁号；比较主版本时先剥掉。</summary>
	private static string BaseVersion(string? version)
	{
		if (string.IsNullOrWhiteSpace(version)) return "";
		string text = version.Trim().TrimStart('v', 'V');
		int idx = text.IndexOf("-beta", StringComparison.OrdinalIgnoreCase);
		return idx > 0 ? text[..idx] : text;
	}

	private static bool IsNewerGitHub(string? candidate, string? current)
	{
		int baseCmp = UpdateVersion.Compare(BaseVersion(candidate), BaseVersion(current));
		if (baseCmp != 0) return baseCmp > 0;
		return UpdateVersion.Compare(candidate, current) > 0;
	}

	private static JObject? FindExternalGcuAsset(JArray releases)
	{
		foreach (JToken token in releases)
		{
			if (token is not JObject release) continue;
			if (release.Value<bool?>("draft") == true) continue;
			JArray? assets = release["assets"] as JArray;
			if (assets == null) continue;
			foreach (JToken assetToken in assets)
			{
				if (assetToken is not JObject asset) continue;
				string? name = Clean(asset.Value<string>("name"));
				if (name == null) continue;
				if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
					&& name.Contains("gcu", StringComparison.OrdinalIgnoreCase))
				{
					return asset;
				}
			}
		}
		return null;
	}

	private static UpdateInfo? ParseGitHubReleases(string? json, string currentVersion)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		JArray releases;
		try { releases = JArray.Parse(json); }
		catch { return null; }
		JObject? best = null;
		string bestVersion = "";
		foreach (JToken token in releases)
		{
			if (token is not JObject release) continue;
			if (release.Value<bool?>("draft") == true) continue;
			string? tag = Clean(release.Value<string>("tag_name"));
			if (string.IsNullOrWhiteSpace(tag)) continue;
			string normalized = tag!.TrimStart('v', 'V');
			if (!UpdatePolicy.IsSaneVersion(normalized)) continue;
			JArray? assets = release["assets"] as JArray;
			// 更新包发布不一定附带 setup（只有 DLL 更新时也应可检测）；只要资产里
			// 有可用的 update.zip/7z 或 setup.exe 就纳入候选。
			bool hasUsableAsset = assets?.OfType<JObject>().Any(a =>
			{
				string? name = Clean(a.Value<string>("name"));
				if (name == null) return false;
				bool isUpdate = name.Contains("update", StringComparison.OrdinalIgnoreCase)
					&& (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
						|| name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
				bool isSetup = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
					&& name.Contains("setup", StringComparison.OrdinalIgnoreCase);
				return isUpdate || isSetup;
			}) == true;
			if (!hasUsableAsset) continue;
			if (best == null || IsNewerGitHub(normalized, bestVersion))
			{
				best = release;
				bestVersion = normalized;
			}
		}
		if (best == null) return null;
		string? chosenTag = Clean(best.Value<string>("tag_name"));
		string latest = chosenTag?.TrimStart('v', 'V') ?? bestVersion;
		JArray? allAssets = best["assets"] as JArray;
		JObject? updateAsset = allAssets?.OfType<JObject>().FirstOrDefault(a =>
		{
			string? name = Clean(a.Value<string>("name"));
			return name != null
				&& name.Contains("update", StringComparison.OrdinalIgnoreCase)
				&& (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
					|| name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
		});
		JObject? setupAsset = allAssets?.OfType<JObject>().FirstOrDefault(a =>
		{
			string? name = Clean(a.Value<string>("name"));
			return name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
				&& name.Contains("setup", StringComparison.OrdinalIgnoreCase);
		});
		// 优先更新包；没有更新包才回退安装包。
		JObject? chosenAsset = updateAsset ?? setupAsset;
		string? downloadUrl = Clean(chosenAsset?.Value<string>("browser_download_url"));
		string? digest = Clean(chosenAsset?.Value<string>("digest"));
		string? sha256 = null;
		if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
		{
			sha256 = digest.Substring(7);
		}
		long? size = chosenAsset?.Value<long?>("size");
		string? chosenName = Clean(chosenAsset?.Value<string>("name"));
		// NoGCU setup 发布时，GCU 厂商组件单独挂在 GitHub 的 gcu-service-unmodified
		// Release（GCU.7z）；只在本次资产是 setup.exe 时把它带下去，由安装器解压安装。
		string? externalGcuName = null;
		string? externalGcuUrl = null;
		long? externalGcuSize = null;
		string? externalGcuSha256 = null;
		if (chosenName != null && chosenName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			JObject? gcuAsset = FindExternalGcuAsset(releases);
			if (gcuAsset != null)
			{
				externalGcuName = Clean(gcuAsset.Value<string>("name"));
				externalGcuUrl = Clean(gcuAsset.Value<string>("browser_download_url"));
				externalGcuSize = gcuAsset.Value<long?>("size");
				string? gcuDigest = Clean(gcuAsset.Value<string>("digest"));
				if (gcuDigest != null && gcuDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
				{
					externalGcuSha256 = gcuDigest.Substring(7);
				}
			}
		}
		string? page = Clean(best.Value<string>("html_url"));
		string? notes = Clean(best.Value<string>("body"));
		string? date = Clean(best.Value<string>("published_at"));
		bool available = IsNewerGitHub(latest, currentVersion);
		return new UpdateInfo(currentVersion, latest, "github", false, available, date, notes,
			chosenName, size, sha256, downloadUrl, page, null,
			externalGcuName, externalGcuUrl, externalGcuSize, externalGcuSha256);
	}

	internal static async Task<UpdateInfo?> CheckAsync(bool force = false, string? versionOverride = null)
	{
		await Gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!force && (object)cached != null)
			{
				return cached;
			}
			if (!force && !ShouldAutoCheckNow(DateTime.UtcNow))
			{
				return cached;
			}
			string version = versionOverride ?? Program.ReleaseVersion;
			string text = BuildCheckUrl(BaseUrl, version);
			string json;
			try
			{
				string text2 = ((HttpGetOverride == null) ? (await GetCheckJsonAsync(text).ConfigureAwait(continueOnCapturedContext: false)) : (await HttpGetOverride(text).ConfigureAwait(continueOnCapturedContext: false)));
				json = text2;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("更新检测失败（静默跳过）：" + ex.GetType().Name + " " + ex.Message);
				return null;
			}
			UpdateInfo updateInfo = ParseResponse(json);
			if ((object)updateInfo == null)
			{
				Logger.WriteLine("更新检测失败（静默跳过）：响应无法解析");
				return null;
			}
			updateInfo = updateInfo with
			{
				CurrentVersion = version
			};
			if (updateInfo.UpdateAvailable && !UpdateVersion.IsNewer(updateInfo.LatestVersion, updateInfo.CurrentVersion))
			{
				Logger.WriteLine($"更新检测：服务端报有更新，但 {updateInfo.LatestVersion} 并不比本机 {updateInfo.CurrentVersion} 新，按无更新处理");
				updateInfo = updateInfo with
				{
					UpdateAvailable = false
				};
			}
			cached = updateInfo;
			AppConfig.Set("update_last_check", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			Logger.WriteLine($"更新检测：本机={updateInfo.CurrentVersion} 服务端={updateInfo.LatestVersion ?? "(无)"} (服务端上报 current_version={updateInfo.ServerCurrentVersion ?? "(无)"}) 有更新={updateInfo.UpdateAvailable} 通道={updateInfo.Channel}{(updateInfo.ChannelFallback ? "(回退)" : "")}");
			return updateInfo;
		}
		finally
		{
			Gate.Release();
		}
	}

	private static async Task<string> GetCheckJsonAsync(string url)
	{
		using HttpResponseMessage response = await UpdateHttp.Check.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: false);
		if (UpdateHttp.IsRedirect(response.StatusCode))
		{
			throw new HttpRequestException(UpdateHttp.RedirectRefusedReason(response.StatusCode));
		}
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
	}

	internal static string NormalizeBaseUrl(string? raw)
	{
		string text = (raw ?? "").Trim().TrimEnd('/');
		if (text.Length == 0)
		{
			return "https://stats.l-mechrevo.cn";
		}
		if (Uri.TryCreate(text, UriKind.Absolute, out Uri result) && (result.Scheme == Uri.UriSchemeHttps || (result.Scheme == Uri.UriSchemeHttp && result.IsLoopback)))
		{
			return text;
		}
		Logger.WriteLine("更新基址不合法（" + ((text.Length == 0) ? "(空)" : text) + "），已回退到默认基址 https://stats.l-mechrevo.cn");
		return "https://stats.l-mechrevo.cn";
	}

	internal static UpdateInfo? ParseResponse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		JObject jObject;
		try
		{
			jObject = JObject.Parse(json);
		}
		catch
		{
			return null;
		}
		JToken? jToken = jObject["ok"];
		if (jToken != null && jToken.Type == JTokenType.Boolean && !jObject.Value<bool>("ok"))
		{
			return null;
		}
		if (!(jObject["data"] is JObject jObject2))
		{
			return null;
		}
		string text = Clean(jObject2.Value<string>("current_version"));
		string text2 = Clean(jObject2.Value<string>("latest_version"));
		if (text2 != null && !UpdatePolicy.IsSaneVersion(text2))
		{
			Logger.WriteLine("更新检测：服务端 latest_version 不合法（" + text2 + "），拒绝该响应");
			return null;
		}
		string text3 = Clean(jObject2.Value<string>("download_url"));
		if (text3 != null && (!Uri.TryCreate(text3, UriKind.Absolute, out Uri result) || !UpdatePolicy.IsAcceptableDownloadUri(result)))
		{
			Logger.WriteLine("更新检测：download_url 不是 HTTPS 地址（" + text3 + "），拒绝该响应");
			return null;
		}
		bool updateAvailable = jObject2.Value<bool?>("update_available") == true;
		if (text2 == null)
		{
			updateAvailable = false;
		}
		long? size = jObject2.Value<long?>("size");
		if (size.HasValue)
		{
			long valueOrDefault = size.GetValueOrDefault();
			if (!UpdatePolicy.IsAcceptablePackageSize(valueOrDefault))
			{
				Logger.WriteLine($"更新检测：size 不在允许范围（{valueOrDefault}），拒绝该响应");
				return null;
			}
		}
		return new UpdateInfo(text ?? "", text2, Clean(jObject2.Value<string>("channel")) ?? "", jObject2.Value<bool?>("channel_fallback") == true, updateAvailable, Clean(jObject2.Value<string>("release_date")), Clean(jObject2.Value<string>("notes")), Clean(jObject2.Value<string>("filename")), size, Clean(jObject2.Value<string>("sha256")), text3, Clean(jObject2.Value<string>("download_page")), text);
	}

	private static string? Clean(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
