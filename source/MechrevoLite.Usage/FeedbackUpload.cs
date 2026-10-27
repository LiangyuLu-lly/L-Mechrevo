using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Diagnostics;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;
using MechrevoLite.Properties;
using MechrevoLite.Update;

namespace MechrevoLite.Usage;

internal static class FeedbackUpload
{
	internal const int MinMessageChars = 4;

	internal const int MaxMessageChars = 4000;

	internal const int MaxContactChars = 120;

	internal const int MaxSysInfoBytes = 24576;

	internal const int MaxLogBytes = 98304;

	internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30L);

	internal static Func<string, string, CancellationToken, Task<string>>? HttpPostOverride { get; set; }

	internal static string BuildUrl(string baseUrl)
	{
		return UpdateChecker.NormalizeBaseUrl(baseUrl) + "/api/feedback.php";
	}

	internal static bool IsSendable(string? message)
	{
		return (message?.Trim().Length ?? 0) >= 4;
	}

	internal static string BuildBody(string id, string version, string model, string project, string gpu, string message, string? contact, bool attach, string? systemInfo, string? log)
	{
		string text = message.Trim();
		if (text.Length > 4000)
		{
			text = text.Substring(0, 4000);
		}
		string text2 = (contact ?? "").Trim();
		if (text2.Length > 120)
		{
			text2 = text2.Substring(0, 120);
		}
		string value = (attach ? HeadUtf8(UsageTelemetry.RedactLog(systemInfo ?? ""), 24576) : "");
		string value2 = (attach ? UsageTelemetry.TailUtf8(UsageTelemetry.RedactLog(log ?? ""), 98304) : "");
		return JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["id"] = id,
			["ver"] = version,
			["model"] = UsageSnapshot.Clip(model),
			["project"] = UsageSnapshot.Clip(project),
			["gpu"] = UsageSnapshot.Clip(gpu),
			["message"] = text,
			["contact"] = text2,
			["sysinfo"] = value,
			["log"] = value2
		}, UsageTelemetry.WireJson);
	}

	internal static string HeadUtf8(string text, int maxBytes)
	{
		if (string.IsNullOrEmpty(text) || maxBytes <= 0)
		{
			return "";
		}
		if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
		{
			return text;
		}
		int num = 0;
		int i;
		int num2;
		for (i = 0; i < text.Length; i += num2)
		{
			num2 = ((i + 1 >= text.Length || !char.IsHighSurrogate(text[i]) || !char.IsLowSurrogate(text[i + 1])) ? 1 : 2);
			int byteCount = Encoding.UTF8.GetByteCount(text.AsSpan(i, num2));
			if (num + byteCount > maxBytes)
			{
				break;
			}
			num += byteCount;
		}
		return text.Substring(0, i);
	}

	internal static FeedbackResult ParseResponse(int statusCode, string? body)
	{
		string text = null;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind == JsonValueKind.Object)
			{
				if (rootElement.TryGetProperty("ok", out var value) && value.ValueKind == JsonValueKind.True && rootElement.TryGetProperty("ticket", out var value2) && value2.ValueKind == JsonValueKind.String)
				{
					return new FeedbackResult(Ok: true, value2.GetString(), null);
				}
				if (rootElement.TryGetProperty("error", out var value3) && value3.ValueKind == JsonValueKind.String)
				{
					text = value3.GetString();
				}
			}
		}
		catch (JsonException)
		{
		}
		return new FeedbackResult(Ok: false, null, statusCode switch
		{
			429 => Strings.FeedbackRateLimited, 
			413 => Strings.FeedbackTooLarge, 
			_ => $"HTTP {statusCode}" + (string.IsNullOrWhiteSpace(text) ? "" : (" · " + text)), 
		});
	}

	internal static async Task<FeedbackResult> SendAsync(string message, string? contact, bool attach, CancellationToken token)
	{
		if (!IsSendable(message))
		{
			return new FeedbackResult(Ok: false, null, Strings.FeedbackTooShort);
		}
		string model = "";
		string project = "";
		string gpu = "";
		try
		{
			model = MechrevoDeviceCapabilities.Current.Model ?? "";
		}
		catch
		{
		}
		try
		{
			project = RuntimeModelSupport.Current().ProjectId ?? "";
		}
		catch
		{
		}
		try
		{
			gpu = UsageSnapshot.GpuGenToken(GpuGenerationProvider.Current().Generation);
		}
		catch
		{
		}
		string systemInfo = null;
		string log = null;
		if (attach)
		{
			systemInfo = await Task.Run(() =>
			{
				try
				{
					return DiagnosticSystemInfo.Build();
				}
				catch (Exception ex4)
				{
					return "系统信息采集失败：" + ex4.Message;
				}
			}, token).ConfigureAwait(continueOnCapturedContext: false);
			log = Logger.SnapshotRingBuffer();
		}
		string body = BuildBody(UsageTelemetry.InstallId(), Program.ReleaseVersion ?? "", model, project, gpu, message, contact, attach, systemInfo, log);
		string text = BuildUrl(UsageTelemetry.BaseUrl);
		try
		{
			if (HttpPostOverride != null)
			{
				return ParseResponse(200, await HttpPostOverride(text, body, token).ConfigureAwait(continueOnCapturedContext: false));
			}
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
			timeout.CancelAfter(SendTimeout);
			using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await UpdateHttp.Download.PostAsync(text, content, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			string body2 = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			FeedbackResult feedbackResult = ParseResponse((int)response.StatusCode, body2);
			Logger.WriteLine(feedbackResult.Ok ? $"Feedback sent: ticket {feedbackResult.Ticket} ({Encoding.UTF8.GetByteCount(body)} bytes)" : ("Feedback not accepted: " + feedbackResult.Error));
			return feedbackResult;
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine("Feedback upload timed out.");
			return new FeedbackResult(Ok: false, null, Strings.FeedbackTimedOut);
		}
		catch (Exception ex3)
		{
			Logger.WriteLine("Feedback upload did not reach the server: " + ex3.Message);
			return new FeedbackResult(Ok: false, null, ex3.Message);
		}
	}
}
