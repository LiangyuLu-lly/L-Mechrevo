// ⭐⭐ 2026-10-19（机主：亮度调节延迟 / 外部变化不同步）：
//   **亮度诊断状态** —— 只记录"最近一次只读读 / 最近一次写"的元数据，供日志与诊断包使用。
//
// ⭐ 纪律：
//   · ⛔ 不参与任何控制流（只写时间戳/数值，读方只展示）；
//   · ⛔ 不写硬件、不碰注册表/EC；数据全部来自调用方已经发生的读/写。
//   · 线程安全：读/写可能来自 UI 线程与后台写线程 ⇒ 用锁保护。
using System;

namespace MechrevoLite.Display;

/// <summary>⭐ 亮度最近一次读/写的诊断快照（只读/只写元数据，⛔ 不参与控制）。</summary>
internal static class BrightnessDiagnosticsState
{
	private static readonly object Gate = new object();

	private static DateTime _lastReadUtc = DateTime.MinValue;

	private static string _lastReadSource = string.Empty;

	private static bool _lastReadOk;

	private static string _lastReadReason = string.Empty;

	private static DateTime _lastWriteUtc = DateTime.MinValue;

	private static int _lastWriteRequested = -1;

	private static int _lastWriteApplied = -1;

	private static bool _lastWriteOk;

	internal static DateTime LastReadUtc
	{
		get
		{
			lock (Gate)
			{
				return _lastReadUtc;
			}
		}
	}

	internal static string LastReadSource
	{
		get
		{
			lock (Gate)
			{
				return _lastReadSource;
			}
		}
	}

	internal static bool LastReadOk
	{
		get
		{
			lock (Gate)
			{
				return _lastReadOk;
			}
		}
	}

	internal static string LastReadReason
	{
		get
		{
			lock (Gate)
			{
				return _lastReadReason;
			}
		}
	}

	internal static DateTime LastWriteUtc
	{
		get
		{
			lock (Gate)
			{
				return _lastWriteUtc;
			}
		}
	}

	internal static int LastWriteRequested
	{
		get
		{
			lock (Gate)
			{
				return _lastWriteRequested;
			}
		}
	}

	internal static int LastWriteApplied
	{
		get
		{
			lock (Gate)
			{
				return _lastWriteApplied;
			}
		}
	}

	internal static bool LastWriteOk
	{
		get
		{
			lock (Gate)
			{
				return _lastWriteOk;
			}
		}
	}

	/// <summary>⭐ 记录一次"只读读亮度"的结果（成功/失败都记，供诊断包看是否支持/最近读时间）。</summary>
	internal static void NoteRead(bool ok, string source, string reason)
	{
		lock (Gate)
		{
			_lastReadUtc = DateTime.UtcNow;
			_lastReadOk = ok;
			_lastReadSource = source ?? string.Empty;
			_lastReadReason = reason ?? string.Empty;
		}
	}

	/// <summary>⭐ 记录一次"写亮度"的结果（请求值/回读值/写调用是否成功）。</summary>
	internal static void NoteWrite(bool ok, int requested, int applied)
	{
		lock (Gate)
		{
			_lastWriteUtc = DateTime.UtcNow;
			_lastWriteOk = ok;
			_lastWriteRequested = requested;
			_lastWriteApplied = applied;
		}
	}
}
