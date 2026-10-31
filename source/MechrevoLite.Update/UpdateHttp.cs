using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Update;

internal static class UpdateHttp
{
	internal static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10L);

	internal static readonly HttpClient Check = Create(UpdateChecker.HttpTimeout);

	internal static readonly HttpClient Download = Create(Timeout.InfiniteTimeSpan);

	internal static bool IsRedirect(HttpStatusCode status)
	{
		if (status >= HttpStatusCode.MultipleChoices)
		{
			return status < HttpStatusCode.BadRequest;
		}
		return false;
	}

	internal static string RedirectRefusedReason(HttpStatusCode status)
	{
		return $"HTTP {(int)status}：拒绝跟随重定向";
	}

	/// <summary>下载 GitHub Release 资产时跟随 302，但只允许跳到允许列表内 HTTPS 主机。</summary>
	internal static async Task<HttpResponseMessage> DownloadFollowingRedirectsAsync(Uri uri, CancellationToken ct)
	{
		const int maxRedirects = 5;
		Uri current = uri;
		for (int i = 0; i <= maxRedirects; i++)
		{
			using HttpRequestMessage request = new(HttpMethod.Get, current);
			HttpResponseMessage response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			if (!IsRedirect(response.StatusCode))
			{
				return response;
			}
			Uri? next = null;
			try
			{
				string? location = response.Headers.Location?.ToString();
				if (!string.IsNullOrWhiteSpace(location))
				{
					next = new Uri(current, location);
				}
			}
			catch
			{
			}
			response.Dispose();
			if (next == null)
			{
				throw new HttpRequestException("重定向响应缺少有效的 Location");
			}
			if (!UpdatePolicy.IsAcceptableDownloadUri(next) || !UpdatePolicy.IsAllowedDownloadHost(next))
			{
				throw new HttpRequestException("重定向目标不在允许列表内：" + next.Host);
			}
			current = next;
		}
		throw new HttpRequestException("重定向次数过多");
	}

	private static HttpClient Create(TimeSpan timeout)
	{
		HttpClient httpClient = new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		});
		httpClient.Timeout = timeout;
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("L-Mechrevo/" + Program.ReleaseLabel);
		return httpClient;
	}
}
