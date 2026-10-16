using System;
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
