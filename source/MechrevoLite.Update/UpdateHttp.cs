using System;
using System.Net;
using System.Net.Http;
using System.Threading;

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
