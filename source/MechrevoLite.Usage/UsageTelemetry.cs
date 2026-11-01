using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Update;
using PawnIO;

namespace MechrevoLite.Usage;

internal static class UsageTelemetry
{
	internal const string EnabledKey = "usage_telemetry";

	internal const string InstallIdKey = "usage_install_id";

	internal const string BaseUrlKey = "usage_base_url";

	internal const string DefaultBaseUrl = "https://stats.l-mechrevo.cn";

	internal const int DefaultEnabled = 1;

	internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5L);

	internal const int MaxLogBytes = 65536;

	internal const int MaxCrashBytes = 16384;

	// 2026-10-06 step8 (upstream 0.290.6 A-3): 30 分钟 -> 15 秒。错误即时入队，成功上传后的下一次最早间隔。
	internal static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(15L);

	internal const string CrashUploadedKey = "usage_crash_uploaded";

	internal const string NoticeVersionKey = "usage_notice_version";

	internal const int CurrentNoticeVersion = 1;

	// 2026-10-06 step8 方案C: 服务器没有回执时的退路开关（默认开 = 把 HTTP 200 视为已确认）。
	// 置 0 则退回"严格回执"：没有 batch/bytes/sha256 一律不当成确认，本地记录保留并退避重试。
	internal const string AckFallbackKey = "telemetry_ack_fallback";

	internal const string OutboxMaxBatchesKey = "telemetry_outbox_max_batches";

	internal static readonly JsonSerializerOptions WireJson = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly object Gate = new object();

	private static CancellationTokenSource? _cts;

	private static Task? _loop;

	private static Task? _logLoop;

	private static readonly SemaphoreSlim LogSignal = new SemaphoreSlim(0, 1);

	private static readonly SemaphoreSlim UploadSignal = new SemaphoreSlim(0, 1);

	private static readonly SemaphoreSlim LogUploadGate = new SemaphoreSlim(1, 1);

	private static readonly object LogCaptureGate = new object();

	private static readonly TelemetryLogOutbox Outbox = new TelemetryLogOutbox(Path.Combine(Logger.appPath, "telemetry-outbox"));

	private static int _errorsQueued;

	private static long _crashQueuedStamp;

	private static int _forceLog;

	private static int _stopped;

	private static TaskCompletionSource<bool> _noticeGate = NewNoticeGate();

	private static int _startSent;

	private static readonly Regex UsersPath = new Regex("[A-Za-z]:\\\\Users\\\\[^\\\\\\r\\n\"'<>|]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex MacAddress = new Regex("\\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\\b", RegexOptions.Compiled);

	internal static TimeSpan FirstDelay { get; set; } = TimeSpan.FromSeconds(20L);

	internal static TimeSpan LogDebounce { get; set; } = TimeSpan.FromSeconds(3L);

	internal static Func<HttpClient>? LogClientOverride { get; set; }

	internal static Func<string, string, CancellationToken, Task>? HttpPostOverride { get; set; }

	/// <summary>2026-10-22（机主：隐私与反馈功能暂时隐藏和关闭）：置 true 时彻底停用匿名统计上传。
	/// 保留本地崩溃/日志文件写入，但不联网。</summary>
	internal const bool TemporarilyDisabled = true;

	internal static bool Enabled => !TemporarilyDisabled && AppConfig.Get("usage_telemetry", 1) != 0;

	internal static bool NoticeShown => AppConfig.Get("usage_notice_version", 0) >= 1;

	// 方案C 退路：无回执时 HTTP 200 是否算已确认（默认开）。
	internal static bool AckFallbackEnabled => AppConfig.Get("telemetry_ack_fallback", 1) != 0;

	internal static string BaseUrl => UpdateChecker.NormalizeBaseUrl(AppConfig.GetString("usage_base_url") ?? "https://stats.l-mechrevo.cn");

	internal static string OutboxDirectory => Outbox.Directory;

	internal static int OutboxPendingCount => Outbox.PendingCount();

	private static TaskCompletionSource<bool> NewNoticeGate()
	{
		return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	internal static void ResetNoticeGateForTests()
	{
		Interlocked.Exchange(ref _noticeGate, NewNoticeGate());
	}

	internal static void MarkNoticeShown()
	{
		AppConfig.Set("usage_notice_version", 1);
		Volatile.Read(in _noticeGate).TrySetResult(result: true);
	}

	internal static void SetEnabled(bool enabled)
	{
		AppConfig.Set("usage_telemetry", enabled ? 1 : 0);
		Logger.WriteLine("Usage telemetry " + (enabled ? "enabled" : "disabled") + " by the user.");
		if (enabled)
		{
			Start();
			return;
		}
		CancelLoop();
		lock (LogCaptureGate)
		{
			try
			{
				// 用户关闭上报 -> 本地待传记录一并清掉，不留隐私残留。
				Outbox.Clear();
				_crashQueuedStamp = 0L;
				_errorsQueued = Logger.ErrorCount;
			}
			catch (Exception ex)
			{
				Logger.WriteInfo("usage log backlog cleanup failed: " + ex.Message);
			}
		}
	}

	private static void CancelLoop()
	{
		CancellationTokenSource cts;
		lock (Gate)
		{
			cts = _cts;
			_cts = null;
			_loop = null;
			_logLoop = null;
			Logger.ErrorRecorded -= SignalLog;
		}
		try
		{
			cts?.Cancel();
		}
		catch
		{
		}
		try
		{
			cts?.Dispose();
		}
		catch
		{
		}
	}

	internal static string InstallId()
	{
		string text = AppConfig.GetString("usage_install_id");
		if (!string.IsNullOrWhiteSpace(text) && text.Length >= 8)
		{
			return text.Trim();
		}
		string text2 = Guid.NewGuid().ToString("N");
		AppConfig.Set("usage_install_id", text2);
		return text2;
	}

	internal static string BuildUrl(string baseUrl)
	{
		return UpdateChecker.NormalizeBaseUrl(baseUrl) + "/api/heartbeat.php";
	}

	internal static string BuildLogUrl(string baseUrl)
	{
		return UpdateChecker.NormalizeBaseUrl(baseUrl) + "/api/log.php";
	}

	internal static string RedactLog(string text)
	{
		return RedactLog(text, SafeFolder(Environment.SpecialFolder.UserProfile), SafeName(() => Environment.MachineName), SafeName(() => Environment.UserName));
	}

	internal static string RedactLog(string text, string? profile, string? machineName, string? userName)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		if (!string.IsNullOrEmpty(profile))
		{
			text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
		}
		text = UsersPath.Replace(text, "%USERPROFILE%");
		text = ReplaceName(text, machineName, "%COMPUTERNAME%");
		text = ReplaceName(text, userName, "%USERNAME%");
		return MacAddress.Replace(text, "xx:xx:xx:xx:xx:xx");
	}

	private static string ReplaceName(string text, string? name, string placeholder)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
		{
			return text;
		}
		return Regex.Replace(text, "(?<![A-Za-z0-9])" + Regex.Escape(name.Trim()) + "(?![A-Za-z0-9])", placeholder, RegexOptions.IgnoreCase);
	}

	private static string? SafeFolder(Environment.SpecialFolder folder)
	{
		try
		{
			return Environment.GetFolderPath(folder);
		}
		catch
		{
			return null;
		}
	}

	private static string? SafeName(Func<string> read)
	{
		try
		{
			return read();
		}
		catch
		{
			return null;
		}
	}

	internal static string TailUtf8(string text, int maxBytes)
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
		int num2 = text.Length;
		while (num2 > 0)
		{
			int num3 = ((num2 < 2 || !char.IsLowSurrogate(text[num2 - 1]) || !char.IsHighSurrogate(text[num2 - 2])) ? 1 : 2);
			int byteCount = Encoding.UTF8.GetByteCount(text.AsSpan(num2 - num3, num3));
			if (num + byteCount > maxBytes)
			{
				break;
			}
			num += byteCount;
			num2 -= num3;
		}
		return text.Substring(num2);
	}

	internal static string BuildLogPayload(string? crashTail, string ringBuffer)
	{
		string text = (string.IsNullOrWhiteSpace(crashTail) ? "" : ("--- crash ---\n" + TailUtf8(RedactLog(crashTail.Trim()), 16384)));
		if (string.IsNullOrWhiteSpace(ringBuffer))
		{
			return text;
		}
		int num = Encoding.UTF8.GetByteCount(text) + ((text.Length > 0) ? 1 : 0) + Encoding.UTF8.GetByteCount("--- recent ---\n");
		string text2 = "--- recent ---\n" + TailUtf8(RedactLog(ringBuffer.Trim()), Math.Max(0, 65536 - num));
		if (text.Length <= 0)
		{
			return text2;
		}
		return text + "\n" + text2;
	}

	private static string? ReadNewCrashTail(out long stamp)
	{
		stamp = 0L;
		try
		{
			string crashFile = Logger.crashFile;
			if (!File.Exists(crashFile))
			{
				return null;
			}
			FileInfo fileInfo = new FileInfo(crashFile);
			stamp = fileInfo.LastWriteTimeUtc.Ticks;
			long num = (long.TryParse(AppConfig.GetString("usage_crash_uploaded"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0);
			if (fileInfo.Length <= 0 || stamp <= num)
			{
				stamp = 0L;
				return null;
			}
			int num2 = (int)Math.Min(fileInfo.Length, 32768L);
			using FileStream fileStream = new FileStream(crashFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fileInfo.Length > num2)
			{
				fileStream.Seek(-num2, SeekOrigin.End);
			}
			using StreamReader streamReader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			string text = streamReader.ReadToEnd().Trim();
			return (text.Length > 0) ? text : null;
		}
		catch
		{
			stamp = 0L;
			return null;
		}
	}

	internal static UsageSnapshot Capture(string eventName)
	{
		MechrevoDeviceCapabilities mechrevoDeviceCapabilities = SafeCaps();
		MechrevoHw mechrevoHw = null;
		try
		{
			mechrevoHw = Program.hw;
		}
		catch
		{
		}
		string project = "";
		try
		{
			project = RuntimeModelSupport.Current().ProjectId ?? "";
		}
		catch
		{
		}
		string gpuName = "";
		DgpuGenerationKind kind = DgpuGenerationKind.Unknown;
		try
		{
			DgpuIdentity dgpuIdentity = GpuGenerationProvider.Current();
			kind = dgpuIdentity.Generation;
			gpuName = dgpuIdentity.MarketingName ?? "";
		}
		catch
		{
		}
		string cpu = "";
		try
		{
			cpu = CpuInfo.Name ?? "";
		}
		catch
		{
		}
		bool gcu = false;
		try
		{
			gcu = mechrevoHw?.IsConnected ?? false;
		}
		catch
		{
		}
		bool elevated = false;
		try
		{
			elevated = ProcessHelper.IsUserAdministrator();
		}
		catch
		{
		}
		string os = "";
		try
		{
			Version version = Environment.OSVersion.Version;
			os = $"{version.Major}.{version.Minor}.{version.Build}";
		}
		catch
		{
		}
		return UsageSnapshot.Sanitize(new UsageSnapshot(InstallId(), Program.ReleaseVersion, eventName, mechrevoDeviceCapabilities.Model ?? "", project, mechrevoDeviceCapabilities.BiosVersion ?? "", UsageSnapshot.GpuGenToken(kind), gpuName, cpu, gcu, mechrevoDeviceCapabilities.Keyboard, mechrevoDeviceCapabilities.Lightbar || mechrevoDeviceCapabilities.RgbLightbar, elevated, "", 0, os, "", Logger.ErrorCount));
	}

	internal static string Serialize(UsageSnapshot snapshot)
	{
		return JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["id"] = snapshot.Id,
			["ver"] = snapshot.Ver,
			["event"] = snapshot.Event,
			["model"] = snapshot.Model,
			["project"] = snapshot.Project,
			["bios"] = snapshot.Bios,
			["gpu"] = snapshot.Gpu,
			["gpu_name"] = snapshot.GpuName,
			["cpu"] = snapshot.Cpu,
			["gcu"] = snapshot.Gcu,
			["keyboard"] = snapshot.Keyboard,
			["lightbar"] = snapshot.Lightbar,
			["elevated"] = snapshot.Elevated,
			["tier"] = snapshot.Tier,
			["port"] = snapshot.Port,
			["os"] = snapshot.Os,
			["upd"] = snapshot.Update,
			["errors"] = snapshot.Errors
		}, WireJson);
	}

	internal static void Start()
	{
		lock (Gate)
		{
			if (Program.UiAuditMode || !Enabled)
			{
				return;
			}
			if (!NoticeShown)
			{
				// 群友版不引入首次说明弹窗，静默视为已确认，保证心跳正常运行。
				MarkNoticeShown();
			}
			else
			{
				Volatile.Read(in _noticeGate).TrySetResult(result: true);
			}
			Task loop = _loop;
			if (loop == null || loop.IsCompleted)
			{
				Volatile.Write(ref _stopped, 0);
				_cts = new CancellationTokenSource();
				CancellationToken token = _cts.Token;
				_loop = Task.Run(() => RunAsync(token), token);
				// 2026-10-06 step8 (A-1): 错误级日志即时唤醒采集循环，不再等 15 s 轮询。
				Logger.ErrorRecorded -= SignalLog;
				Logger.ErrorRecorded += SignalLog;
				Interlocked.Exchange(ref _forceLog, 1);
				_logLoop = Task.Run(() => Task.WhenAll(CaptureLogsAsync(token), RunLogsAsync(token)), token);
			}
		}
	}

	internal static void Stop()
	{
		if (Interlocked.Exchange(ref _stopped, 1) != 0)
		{
			return;
		}
		CancellationTokenSource cts;
		lock (Gate)
		{
			cts = _cts;
			_ = _loop;
			_cts = null;
			_loop = null;
			_logLoop = null;
			Logger.ErrorRecorded -= SignalLog;
		}
		try
		{
			cts?.Cancel();
		}
		catch
		{
		}
		try
		{
			if (Enabled && NoticeShown && !Program.UiAuditMode)
			{
				using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500L));
				PersistLog(force: false);
				List<Task> list = new List<Task> { DrainLogsAsync(cancellationTokenSource.Token) };
				if (Volatile.Read(in _startSent) != 0)
				{
					list.Add(Post(Capture("stop"), cancellationTokenSource.Token));
				}
				Task.WhenAll(list).Wait(TimeSpan.FromMilliseconds(2500L));
			}
		}
		catch (Exception ex)
		{
			Logger.WriteInfo("usage stop heartbeat failed: " + ex.Message);
		}
		try
		{
			cts?.Dispose();
		}
		catch
		{
		}
	}

	private static async Task RunAsync(CancellationToken token)
	{
		_ = 4;
		try
		{
			await Task.Delay(FirstDelay, token).ConfigureAwait(continueOnCapturedContext: false);
			if (!NoticeShown)
			{
				await Volatile.Read(in _noticeGate).Task.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (Enabled)
			{
				Interlocked.Exchange(ref _startSent, 1);
				await Post(Capture("start"), token).ConfigureAwait(continueOnCapturedContext: false);
				while (!token.IsCancellationRequested)
				{
					await Task.Delay(Interval, token).ConfigureAwait(continueOnCapturedContext: false);
					await Post(Capture("beat"), token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Logger.WriteInfo("usage heartbeat loop failed: " + ex2.Message);
		}
	}

	private static async Task Post(UsageSnapshot snapshot, CancellationToken token)
	{
		if (snapshot.Id.Length < 8)
		{
			return;
		}
		string text = BuildUrl(BaseUrl);
		string text2 = Serialize(snapshot);
		try
		{
			if (HttpPostOverride != null)
			{
				await HttpPostOverride(text, text2, token).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			using StringContent content = new StringContent(text2, Encoding.UTF8, "application/json");
			using HttpResponseMessage httpResponseMessage = await UpdateHttp.Check.PostAsync(text, content, token).ConfigureAwait(continueOnCapturedContext: false);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				Logger.WriteInfo($"usage heartbeat HTTP {(int)httpResponseMessage.StatusCode}");
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Logger.WriteInfo("usage heartbeat failed: " + ex2.Message);
		}
	}

	private static void SignalLog()
	{
		Signal(LogSignal);
	}

	private static void Signal(SemaphoreSlim signal)
	{
		if (signal.CurrentCount == 0)
		{
			try
			{
				signal.Release();
			}
			catch (SemaphoreFullException)
			{
			}
		}
	}

	// 2026-10-06 step8 (A-6): 崩溃现场同步进待传队列（只落盘，不联网）。
	internal static void PersistCrashForUpload()
	{
		try
		{
			PersistLog(force: true);
			Signal(UploadSignal);
		}
		catch (Exception ex)
		{
			Logger.WriteInfo("usage crash persistence failed: " + ex.Message);
		}
	}

	// 终止性异常：只保证证据已经落在本地待传队列里，不做 1.5 s 网络排空（崩溃路径不干重活）。
	internal static void FlushFatalLog()
	{
		try
		{
			PersistLog(force: false);
			Logger.WriteInfo("usage fatal log remains pending in the local outbox");
		}
		catch (Exception ex)
		{
			Logger.WriteInfo("usage fatal log stays local: " + ex.Message);
		}
	}

	internal static void PersistLog(bool force)
	{
		lock (LogCaptureGate)
		{
			if (!Enabled || !NoticeShown || Program.UiAuditMode)
			{
				return;
			}
			try
			{
				int errorCount = Logger.ErrorCount;
				string text = ReadNewCrashTail(out var stamp);
				if (force || errorCount > _errorsQueued || stamp > _crashQueuedStamp)
				{
					// 脱敏：错误环与普通环都过 RedactLog（BuildLogPayload 内部做）。
					string ringBuffer = "--- errors ---\n" + Logger.SnapshotErrorBuffer() + "\n--- recent ---\n" + TailUtf8(Logger.SnapshotRingBuffer(), 32768);
					bool flag = text != null && stamp > _crashQueuedStamp;
					string text2 = BuildLogPayload(flag ? text : null, ringBuffer);
					// 超大载荷二分缩尾：保证单批序列化后 <= 128 KiB。
					while (Encoding.UTF8.GetByteCount(text2) > 0 && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(text2, WireJson)) > 131072)
					{
						text2 = TailUtf8(text2, Encoding.UTF8.GetByteCount(text2) / 2);
					}
					Outbox.Enqueue(new TelemetryLogBatch(InstallId(), Program.ReleaseVersion ?? "", Guid.NewGuid().ToString("N"), text2, flag ? stamp : 0, errorCount));
					_errorsQueued = errorCount;
					if (flag)
					{
						_crashQueuedStamp = stamp;
					}
					Logger.ResetChangeTracking("usage-log-outbox-write");
				}
			}
			catch (Exception ex)
			{
				// 磁盘写入失败必须可观测；用 IfChanged 抑制"每次重试都刷一条"，避免自我放大。
				Logger.WriteLineIfChanged("usage-log-outbox-write", "usage log backlog write failed: " + ex.Message);
			}
		}
	}

	internal static TimeSpan LogRetryDelay(int failures)
	{
		return TimeSpan.FromSeconds(Math.Min(300.0, 15.0 * Math.Pow(2.0, Math.Clamp(failures - 1, 0, 5))));
	}

	private static async Task CaptureLogsAsync(CancellationToken token)
	{
		_ = 2;
		try
		{
			if (!NoticeShown)
			{
				await Volatile.Read(in _noticeGate).Task.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			}
			while (!token.IsCancellationRequested && Enabled)
			{
				PersistLog(Interlocked.Exchange(ref _forceLog, 0) != 0);
				Signal(UploadSignal);
				await LogSignal.WaitAsync(Interval, token).ConfigureAwait(continueOnCapturedContext: false);
				await Task.Delay(LogDebounce, token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			Logger.WriteInfo("usage log capture loop failed: " + ex2.Message);
		}
	}

	private static async Task RunLogsAsync(CancellationToken token)
	{
		_ = 2;
		try
		{
			if (!NoticeShown)
			{
				await Volatile.Read(in _noticeGate).Task.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			}
			int failures = 0;
			long nextUpload = 0L;
			while (!token.IsCancellationRequested && Enabled)
			{
				long num = nextUpload - Environment.TickCount64;
				TimeSpan timeout;
				if (num > 0)
				{
					timeout = TimeSpan.FromMilliseconds(num);
				}
				else
				{
					// 失败 -> 指数退避（15/30/60…<=300 s）；成功 -> 15 s 后再看有没有新批次。
					bool flag = await DrainLogsAsync(token).ConfigureAwait(continueOnCapturedContext: false);
					failures = ((!flag) ? (failures + 1) : 0);
					TimeSpan timeSpan = (flag ? ErrorLogInterval : LogRetryDelay(failures));
					nextUpload = Environment.TickCount64 + (long)timeSpan.TotalMilliseconds;
					timeout = (flag ? Interval : timeSpan);
				}
				await UploadSignal.WaitAsync(timeout, token).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			Logger.WriteInfo("usage log loop failed: " + ex2.Message);
		}
	}

	// 2026-10-06 step8 方案C 的三态判定。
	internal enum LogAckOutcome
	{
		// 回执齐全且 batch/bytes/sha256 全对 -> 可以删本地待传记录。
		Acknowledged,
		// 服务器给了回执但对不上（含回执字段不全）-> 绝不删，失败退避重试。
		Mismatch,
		// 服务器根本没给回执（老服务端 / 非 JSON 响应）-> 交给 telemetry_ack_fallback 决定。
		Missing
	}

	internal static LogAckOutcome EvaluateLogReceipt(string? reply, TelemetryLogBatch batch)
	{
		if (string.IsNullOrWhiteSpace(reply))
		{
			return LogAckOutcome.Missing;
		}
		JsonDocument jsonDocument;
		try
		{
			jsonDocument = JsonDocument.Parse(reply);
		}
		catch (JsonException)
		{
			return LogAckOutcome.Missing;
		}
		using (jsonDocument)
		{
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				return LogAckOutcome.Missing;
			}
			bool flag = rootElement.TryGetProperty("ok", out var value);
			bool flag2 = rootElement.TryGetProperty("batch", out var value2);
			bool flag3 = rootElement.TryGetProperty("bytes", out var value3);
			bool flag4 = rootElement.TryGetProperty("sha256", out var value4);
			if (!flag && !flag2 && !flag3 && !flag4)
			{
				return LogAckOutcome.Missing;
			}
			if (!flag || !flag2 || !flag3 || !flag4)
			{
				return LogAckOutcome.Mismatch;
			}
			string text = ((value2.ValueKind == JsonValueKind.String) ? value2.GetString() : null);
			string text2 = ((value4.ValueKind == JsonValueKind.String) ? value4.GetString() : null);
			int? num = ((value3.ValueKind == JsonValueKind.Number && value3.TryGetInt32(out var value5)) ? new int?(value5) : ((int?)null));
			if (text == null || text2 == null || !num.HasValue)
			{
				return LogAckOutcome.Mismatch;
			}
			string value6 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(batch.Log)));
			if (value.ValueKind == JsonValueKind.True && text == batch.Batch && num.Value == Encoding.UTF8.GetByteCount(batch.Log) && string.Equals(text2, value6, StringComparison.OrdinalIgnoreCase))
			{
				return LogAckOutcome.Acknowledged;
			}
			return LogAckOutcome.Mismatch;
		}
	}

	internal static async Task<bool> SendLogAsync(TelemetryLogBatch batch, string url, HttpClient client, CancellationToken token)
	{
		string content = JsonSerializer.Serialize(new
		{
			id = batch.Id,
			ver = batch.Ver,
			batch = batch.Batch,
			log = batch.Log
		}, WireJson);
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
		deadline.CancelAfter(TimeSpan.FromSeconds(15L));
		using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await client.PostAsync(url, content2, deadline.Token).ConfigureAwait(continueOnCapturedContext: false);
		string reply = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			Logger.WriteInfo($"usage log not acknowledged: HTTP {(int)response.StatusCode}");
			return false;
		}
		switch (EvaluateLogReceipt(reply, batch))
		{
		case LogAckOutcome.Acknowledged:
			Logger.ResetChangeTracking("usage-log-receipt-mismatch");
			return true;
		case LogAckOutcome.Mismatch:
			// 方案C：回执存在但对不上 -> 不删本地记录，走失败退避重试。
			Logger.WriteLineIfChanged("usage-log-receipt-mismatch", "usage log receipt mismatch; the pending snapshot is kept");
			return false;
		default:
			if (AckFallbackEnabled)
			{
				// 方案C 退路：服务器不给回执时把 HTTP 200 视为已确认（默认开）。
				Logger.WriteInfo("usage log accepted without a receipt (telemetry_ack_fallback)");
				return true;
			}
			Logger.WriteInfo("usage log not acknowledged: the server returned no receipt (telemetry_ack_fallback=0)");
			return false;
		}
	}

	internal static async Task<bool> DrainLogsAsync(CancellationToken token)
	{
		await LogUploadGate.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			while (Enabled && NoticeShown && !Program.UiAuditMode)
			{
				token.ThrowIfCancellationRequested();
				(string Path, TelemetryLogBatch Batch)? entry = Outbox.Peek();
				if (!entry.HasValue)
				{
					return true;
				}
				HttpClient client = ((LogClientOverride != null) ? LogClientOverride() : UpdateHttp.Download);
				if (!(await SendLogAsync(entry.Value.Batch, BuildLogUrl(BaseUrl), client, token).ConfigureAwait(continueOnCapturedContext: false)))
				{
					return false;
				}
				Outbox.Acknowledge(entry.Value.Path);
				if (entry.Value.Batch.CrashStamp > 0)
				{
					AppConfig.Set("usage_crash_uploaded", entry.Value.Batch.CrashStamp.ToString(CultureInfo.InvariantCulture));
				}
			}
			return true;
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Logger.WriteInfo("usage log upload deferred: " + ex2.Message);
			return false;
		}
		finally
		{
			LogUploadGate.Release();
		}
	}

	private static MechrevoDeviceCapabilities SafeCaps()
	{
		try
		{
			return MechrevoDeviceCapabilities.Current;
		}
		catch
		{
			return new MechrevoDeviceCapabilities();
		}
	}
}
