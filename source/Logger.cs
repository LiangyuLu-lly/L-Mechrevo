using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MechrevoLite;
using MechrevoLite.Helpers;

public static class Logger
{
	public enum LogLevel
	{
		Off,
		ErrorOnly,
		All
	}

	public enum LogSeverity
	{
		Info,
		Error
	}

	public const string LogDirectoryOverrideVariable = "LMECHREVO_LOG_DIR";

	public const string LevelOverrideVariable = "LMECHREVO_LOG_LEVEL";

	private static readonly string[] TestHostProcessNames;

	public static readonly string appPath;

	public static readonly string logFile;

	public static readonly string crashFile;

	internal const string TestLogDirectoryName = "test-logs";

	internal const long MaxLogBytes = 10485760L;

	// 2026-10-12 logfix: 每代上限 MaxLogBytes，超限就整代改名成 log.N.txt 往下顺延，最多留
	// RetainedLogGenerations 代（log.1/2/3.txt），最老的一代直接删除 —— 日志总量有界、且永远能继续写。
	internal const int RetainedLogGenerations = 3;

	// 2026-10-12 logfix2（用户原话「改log」）：内存环形缓冲 64 KB 在真机上只够 ~80 秒
	// （0.291.6 耀世诊断包：日志\内存日志缓冲.txt 224 行、覆盖 80 s）—— 事情一发生就已经滚出去了。
	// 提到 4 MiB：按实测速率（~0.44 MB/h、~292 B/行）约 9 小时；Write 里是无条件 Ring.Append，
	// 所以内存/CPU 成本与日志级别无关，磁盘占用 0（只在导出诊断包/崩溃时才落盘）。
	internal const int CrashRingBufferBytes = 4194304;

	// 2026-10-12 logfix2（「改log」）：崩溃时最多往 crash.txt 追加 1 MiB（尾部、按整行对齐）。
	// crash.txt 不轮转，缓冲涨到 4 MiB 之后不能让一次崩溃就灌 4 MiB —— 崩溃循环会把磁盘撑爆。
	internal const int CrashRingFlushBytes = 1048576;

	private static readonly UTF8Encoding Utf8NoBom;

	private static readonly string[] ErrorMarkers;

	private static readonly ConcurrentQueue<string> Queue;

	private static readonly AutoResetEvent Pending;

	private static readonly ConcurrentDictionary<string, long> ThrottleTicks;

	private static readonly ConcurrentDictionary<string, string> LastMessages;

	private static readonly CrashRingBuffer Ring;

	internal static readonly CrashRingBuffer Errors;

	private static readonly Thread WriterThread;

	private static int _closed;

	private static int _errorCount;

	private static int _crashFlushed;

	public static int ErrorCount => Volatile.Read(ref _errorCount);

	// 2026-10-05 uifix: 本进程有没有落过崩溃现场（FlushCrashBuffer）。
	// CpuCoAutoApplyGuard 用它决定"正常退出标记"到底能不能写 —— 已经崩过一次就宁可不写。
	internal static bool CrashFlushed => Volatile.Read(ref _crashFlushed) != 0;

	internal static event Action? ErrorRecorded;

	internal static LogLevel CurrentLevel => ResolveLevel(Environment.GetEnvironmentVariable("LMECHREVO_LOG_LEVEL"), TryGetConfiguredLevel());

	internal static string ResolveAppPath()
	{
		return ResolveAppPath(Environment.GetEnvironmentVariable("LMECHREVO_LOG_DIR"), IsTestHost(), AppContext.BaseDirectory, Path.Combine(Environment.GetFolderPath(ProcessHelper.IsRunningAsSystem() ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.ApplicationData), "MechrevoLite"));
	}

	internal static string ResolveAppPath(string? overridden, bool isTestHost, string baseDirectory, string defaultDirectory)
	{
		if (!string.IsNullOrWhiteSpace(overridden))
		{
			try
			{
				return Path.GetFullPath(overridden);
			}
			catch
			{
			}
		}
		if (isTestHost)
		{
			try
			{
				return Path.Combine(baseDirectory, "test-logs");
			}
			catch
			{
			}
		}
		return defaultDirectory;
	}

	internal static bool IsTestHost()
	{
		try
		{
			if (IsTestHostProcessName(Process.GetCurrentProcess().ProcessName))
			{
				return true;
			}
			return AppDomain.CurrentDomain.GetAssemblies().Any((Assembly assembly) => IsTestHostAssemblyName(assembly.GetName().Name));
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsTestHostProcessName(string? processName)
	{
		if (string.IsNullOrWhiteSpace(processName))
		{
			return false;
		}
		string[] testHostProcessNames = TestHostProcessNames;
		foreach (string value in testHostProcessNames)
		{
			if (processName.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool IsTestHostAssemblyName(string? assemblyName)
	{
		if (string.IsNullOrWhiteSpace(assemblyName))
		{
			return false;
		}
		if (!assemblyName.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase) && !assemblyName.StartsWith("Microsoft.TestPlatform.", StringComparison.OrdinalIgnoreCase))
		{
			return assemblyName.Equals("testhost", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	static Logger()
	{
		TestHostProcessNames = new string[3] { "testhost", "vstest.console", "dotnet-test" };
		appPath = ResolveAppPath();
		logFile = Path.Combine(appPath, "log.txt");
		crashFile = Path.Combine(appPath, "crash.txt");
		Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		ErrorMarkers = new string[17]
		{
			"fail", "error", "exception", "unhandled", "unobserved", "cannot", "unable", "denied", "refused", "timeout",
			"失败", "错误", "异常", "无法", "拒绝", "超时", "损坏"
		};
		Queue = new ConcurrentQueue<string>();
		Pending = new AutoResetEvent(initialState: false);
		ThrottleTicks = new ConcurrentDictionary<string, long>();
		LastMessages = new ConcurrentDictionary<string, string>();
		Ring = new CrashRingBuffer(CrashRingBufferBytes);
		Errors = new CrashRingBuffer(16384);
		WriterThread = new Thread(WriterLoop)
		{
			IsBackground = true,
			Name = "L-Mechrevo Logger",
			Priority = ThreadPriority.BelowNormal
		};
		WriterThread.Start();
	}

	public static void WriteLine(string logMessage)
	{
		Write(logMessage, ClassifySeverity(logMessage));
	}

	public static void WriteError(string logMessage)
	{
		Write(logMessage, LogSeverity.Error);
	}

	public static void WriteInfo(string logMessage)
	{
		Write(logMessage, LogSeverity.Info);
	}

	public static void WriteLineThrottled(string key, string logMessage, int intervalMs = 5000)
	{
		long tickCount = Environment.TickCount64;
		long orAdd = ThrottleTicks.GetOrAdd(key, -4611686018427387904L);
		if (tickCount - orAdd >= intervalMs && ThrottleTicks.TryUpdate(key, tickCount, orAdd))
		{
			WriteLine(logMessage);
		}
	}

	public static void WriteLineIfChanged(string key, string logMessage)
	{
		if (TryMarkChanged(key, logMessage))
		{
			WriteLine(logMessage);
		}
	}

	internal static bool TryMarkChanged(string key, string logMessage)
	{
		if (LastMessages.TryGetValue(key, out string value) && value == logMessage)
		{
			return false;
		}
		LastMessages[key] = logMessage;
		return true;
	}

	public static void ResetChangeTracking(string key)
	{
		LastMessages.TryRemove(key, out string _);
	}

	private static void Write(string logMessage, LogSeverity severity)
	{
		string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {logMessage}";
		if (Volatile.Read(in _closed) == 0)
		{
			Ring.Append(text);
			if (severity == LogSeverity.Error)
			{
				Errors.Append(text);
				Interlocked.Increment(ref _errorCount);
				try
				{
					ErrorRecorded?.Invoke();
				}
				catch (Exception)
				{
				}
			}
			if (ShouldWrite(CurrentLevel, severity))
			{
				Queue.Enqueue(text);
				Pending.Set();
			}
		}
	}

	private static string? TryGetConfiguredLevel()
	{
		try
		{
			return AppConfig.GetString("log_level");
		}
		catch
		{
			return null;
		}
	}

	internal static LogLevel ResolveLevel(string? envOverride, string? configValue)
	{
		return ParseLevel(string.IsNullOrWhiteSpace(envOverride) ? configValue : envOverride);
	}

	internal static LogLevel ParseLevel(string? raw)
	{
		switch ((raw ?? "").Trim().ToLowerInvariant())
		{
		case "all":
		case "verbose":
			return LogLevel.All;
		case "error":
		case "error_only":
		case "erroronly":
			return LogLevel.ErrorOnly;
		default:
			return LogLevel.Off;
		}
	}

	internal static bool ShouldWrite(LogLevel level, LogSeverity severity)
	{
		return level switch
		{
			LogLevel.All => true, 
			LogLevel.ErrorOnly => severity == LogSeverity.Error, 
			_ => false, 
		};
	}

	internal static LogSeverity ClassifySeverity(string? message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return LogSeverity.Info;
		}
		string[] errorMarkers = ErrorMarkers;
		foreach (string value in errorMarkers)
		{
			if (message.Contains(value, StringComparison.OrdinalIgnoreCase))
			{
				return LogSeverity.Error;
			}
		}
		return LogSeverity.Info;
	}

	private static void WriterLoop()
	{
		while (Volatile.Read(in _closed) == 0 || !Queue.IsEmpty)
		{
			Pending.WaitOne(500);
			if (Queue.IsEmpty)
			{
				continue;
			}
			try
			{
				// 2026-10-12 logfix: 每个批次写完就关掉句柄（不再长期持有），
				// 且追加流绝不 SetLength（追加模式下截断必然抛 IOException）——
				// 超限只做"整代改名"，失败则退化截断，绝不静默停止记录。
				// ⚠️ 顺序要紧：必须先把 log.txt 的句柄关掉、再改名、最后重新打开写"已轮转"标记；
				// 否则改名后我们手里的句柄指向的是 log.1.txt，标记会写进上一代。
				string text;
				using (StreamWriter streamWriter = OpenLogWriter())
				{
					bool flag = DrainQueue(streamWriter);
					streamWriter.Flush();
					text = (flag ? RotateLogIfOversized() : string.Empty);
				}
				if (text.Length > 0 || GetLogSize() > MaxLogBytes)
				{
					using StreamWriter streamWriter2 = OpenLogWriter();
					streamWriter2.Write(RotationMarker(text));
					streamWriter2.Flush();
				}
			}
			catch (Exception ex)
			{
				// 单个批次失败绝不允许打死 WriterThread（打死就变成"之后一行都不写"）。
				// 失败原因写进 crash.txt —— 没有静默；下一批继续尝试，日志会自己恢复。
				MarkWriteFailure($"batch write failed: {ex.GetType().Name}: {ex.Message} | {ex.StackTrace}");
				try
				{
					Thread.Sleep(1000);
				}
				catch
				{
				}
			}
		}
		if (!Queue.IsEmpty)
		{
			try
			{
				using StreamWriter streamWriter = OpenLogWriter();
				if (DrainQueue(streamWriter))
				{
					streamWriter.Flush();
				}
			}
			catch (Exception ex2)
			{
				MarkWriteFailure($"final flush failed: {ex2.GetType().Name}: {ex2.Message}");
			}
		}
	}

	private static StreamWriter OpenLogWriter()
	{
		Directory.CreateDirectory(appPath);
		// FileShare.ReadWrite | Delete：别的进程要改名/轮转我们这个 log.txt 时不会被句柄挡住。
		FileStream fileStream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536);
		StreamWriter item = new StreamWriter(fileStream, Utf8NoBom, 65536)
		{
			AutoFlush = false
		};
		// 2026-10-12 logfix: 不能信 FileStream.Length —— 追加模式下 truncate 之后它可能停在旧的
		// 追加起点（旧值 10 MiB+），于是每次 flush 都误判"超限"并反复截断。只认磁盘上的真实长度。
		return item;
	}

	private static bool DrainQueue(StreamWriter writer)
	{
		bool result = false;
		string result2;
		while (Queue.TryDequeue(out result2))
		{
			writer.WriteLine(result2);
			result = true;
		}
		return result;
	}

	/// <summary>
	/// 2026-10-12 logfix: 超限 → 整代改名轮转（log.txt → log.1.txt → log.2.txt …，只留
	/// <see cref="RetainedLogGenerations" /> 代）。返回被轮转出去的那一代文件名（如 "log.1.txt"），
	/// 没超限时返回空串；改名成功后由调用方写一条"已轮转"标记。
	/// 失败（文件被占用 / 无权限）返回空串，并在内部退化截断，绝不静默停止记录。
	/// </summary>
	internal static string RotateLogIfOversized(string path, long maxBytes, int generations)
	{
		if (GetLogSizeFor(path) <= maxBytes)
		{
			return string.Empty;
		}
		string fileName = Path.GetFileNameWithoutExtension(path) + ".1" + Path.GetExtension(path);
		try
		{
			ApplyRotation(path, generations);
			return fileName;
		}
		catch (Exception ex)
		{
			MarkWriteFailure($"rotate {Path.GetFileName(path)} failed, falling back to truncate: {ex.GetType().Name} {ex.Message}");
			try
			{
				TruncateIfOversized(path, maxBytes);
			}
			catch (Exception ex2)
			{
				MarkWriteFailure($"truncate {Path.GetFileName(path)} failed, appending anyway (log keeps growing): {ex2.GetType().Name} {ex2.Message}");
			}
			return string.Empty;
		}
	}

	private static string RotateLogIfOversized()
	{
		return RotateLogIfOversized(logFile, MaxLogBytes, RetainedLogGenerations);
	}

	private static void ApplyRotation(string path, int generations)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(directoryName))
		{
			directoryName = ".";
		}
		string fileName = Path.GetFileName(path);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		int num = Math.Max(1, generations);
		// 先删最老的一代，再逐代向后改名（改名顺序避免自己覆盖自己）。
		string path2 = Path.Combine(directoryName, $"{fileNameWithoutExtension}.{num}{Path.GetExtension(fileName)}");
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
		for (int num2 = num - 1; num2 >= 1; num2--)
		{
			string text = Path.Combine(directoryName, $"{fileNameWithoutExtension}.{num2}{Path.GetExtension(fileName)}");
			if (File.Exists(text))
			{
				MoveVerified(text, Path.Combine(directoryName, $"{fileNameWithoutExtension}.{num2 + 1}{Path.GetExtension(fileName)}"));
			}
		}
		MoveVerified(path, Path.Combine(directoryName, $"{fileNameWithoutExtension}.1{Path.GetExtension(fileName)}"));
	}

	/// <summary>
	/// 2026-10-12 logfix: <c>File.Move(..., overwrite: true)</c> 在"目标已存在"时是先删目标再改名；
	/// 内部那一步失败会被它吞掉，最后只留一个语义模糊的 IOException。这里改名后复核一次目标是否
	/// 真的存在，不存在就重新抛一个带 errno 的异常（不外发，只进 crash.txt）。
	/// </summary>
	private static void MoveVerified(string source, string destination)
	{
		File.Move(source, destination, overwrite: true);
		if (!File.Exists(destination))
		{
			throw new IOException($"move {Path.GetFileName(source)} -> {Path.GetFileName(destination)} reported success but the destination is missing (exists={File.Exists(destination)})");
		}
	}

	/// <summary>
	/// 轮转失败时的退化路径：就地截断，只保留尾巴（保留量取 min(maxBytes, 1 MiB)，且对齐到行首）。
	/// ⛔ 不能用"追加"句柄做这件事 —— FileMode.Append 下 SetLength / 向后 Seek 必抛 IOException
	/// 「无法截断以追加模式打开的文件中的原有数据」，这正是本 bug 的根因。
	/// 这里用普通 ReadWrite 句柄：把尾巴写到文件开头覆盖，再 SetLength 把后面截掉。
	/// </summary>
	internal static void TruncateIfOversized(string path, long maxBytes)
	{
		int targetBytes = (int)Math.Min(maxBytes, 1048576L);
		long length;
		byte[] buffer;
		int kept;
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
		{
			length = fileStream.Length;
			long tailStart = Math.Max(0L, length - targetBytes);
			int want = (int)Math.Min(targetBytes, length);
			buffer = new byte[want];
			int read = 0;
			fileStream.Seek(tailStart, SeekOrigin.Begin);
			while (read < want)
			{
				int num = fileStream.Read(buffer, read, want - read);
				if (num <= 0)
				{
					break;
				}
				read += num;
			}
			int skip = 0;
			if (tailStart > 0 && read > 0)
			{
				// 尾巴从半个行中间开始：对齐到下一行的行首，别留半行。
				int firstNewline = Array.IndexOf(buffer, (byte)10, 0, read);
				skip = ((firstNewline >= 0) ? (firstNewline + 1) : read);
			}
			kept = read - skip;
			if (skip > 0 && kept > 0)
			{
				Buffer.BlockCopy(buffer, skip, buffer, 0, kept);
			}
		}
		if (kept <= 0 || kept >= length)
		{
			return;
		}
		using FileStream fileStream2 = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
		fileStream2.Position = 0L;
		fileStream2.Write(buffer, 0, kept);
		fileStream2.Flush();
		fileStream2.SetLength(kept);
	}

	private static string RotationMarker(string rotatedName)
	{
		return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: === log rotated: {Path.GetFileName(logFile)} 超过 {MaxLogBytes / 1048576} MiB，"
			+ $"上一代已改名 {rotatedName}（保留 {RetainedLogGenerations} 代）===";
	}

	/// <summary>log.txt 在磁盘上的真实长度（跨进程，取不到就返回 0 ⇒ 视为未超限，只会多写不会停写）。</summary>
	private static long GetLogSize()
	{
		return GetLogSizeFor(logFile);
	}

	private static long GetLogSizeFor(string path)
	{
		try
		{
			return Math.Max(0L, new FileInfo(path).Length);
		}
		catch
		{
			return 0L;
		}
	}

	/// <summary>
	/// 2026-10-12 logfix: 日志自己的故障必须留痕（⛔ 不允许静默）。写进 crash.txt —— 那条路径
	/// 一次只 AppendAllText 几十字节、文件不设上限也没有"超限轮转"，不会自我递归。
	/// </summary>
	private static void MarkWriteFailure(string note)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(crashFile);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.AppendAllText(crashFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: === log write problem: {note} ===" + Environment.NewLine, Utf8NoBom);
		}
		catch
		{
		}
	}

	internal static void FlushCrashBuffer(string reason)
	{
		Interlocked.Exchange(ref _crashFlushed, 1);
		try
		{
			string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: === crash: {reason} (log_level={CurrentLevel}) ===";
			string directoryName = Path.GetDirectoryName(crashFile);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.AppendAllText(crashFile, text + Environment.NewLine, Utf8NoBom);
			Ring.FlushTo(crashFile, CrashRingFlushBytes);
		}
		catch
		{
		}
	}

	internal static string SnapshotRingBuffer()
	{
		return Ring.Snapshot();
	}

	internal static string SnapshotErrorBuffer()
	{
		return Errors.Snapshot();
	}

	public static void Cleanup()
	{
	}

	public static void Close()
	{
		if (Interlocked.Exchange(ref _closed, 1) == 0)
		{
			Pending.Set();
			try
			{
				WriterThread.Join(2000);
			}
			catch
			{
			}
			Pending.Dispose();
		}
	}
}
