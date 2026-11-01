using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PawnIO;

// ==============================================================================================
// 2026-10-10 intel（⑦B）：Intel OC 邮箱（MSR 0x150）通路。
//
// 来源：上游（作者 0.290.6）`PawnIO\IntelOcMailbox.cs`（5,973 B / 244 行）**逐字移植**（含量程与位布局），
//      协议来源 kitsunyan/intel-undervolt（GPL-3.0）—— 署名见 THIRD-PARTY-NOTICES.md §1.3。
//      模块二进制 = LMechrevo.IntelMSR.bin（PawnIO.Modules，LGPL-2.1-or-later），
//      见 source\L-Mechrevo.csproj 的 <EmbeddedResource> 与 THIRD-PARTY-NOTICES.md §1.2 B4。
//
// ⚠️⚠️ 安全边界（本仓库明确纪律）：本文件**只在本机为 Intel 且用户显式点「应用」时**才会被调用
//      （RuntimeCpuTuning.Probe 的 Intel 分支）。本仓库的验证机是 AMD 8745H ⇒ 这条路径在验证机
//      上**永远不会执行**，**未经过任何真机 Intel 写入验证**。切勿在没有 Intel 真机的情况下
//      用手写工具或单元测试去调 Apply/WriteVerified —— 那是真的写 MSR。
//
// 关键语义（A-31 / A-32，作者在 0.290.6 的收口）：
//   * Command()    —— 写 MSR 0x150：bit63 置位表示"有请求"，(domain<<40) | (command<<32) | value；
//                     写完再读同一条 MSR，取 bit32..39 当作响应码，非 0 = 固件拒绝。
//                     command 17 = 写入，16 = 读回，1 = 读能力。
//   * ReadIdle()   —— 轮询 bit63（busy）直到清零，200 ms 超时（超时抛 TimeoutException，绝不硬等）。
//   * WriteVerified() —— **本次合并的核心加固**：
//       1) desired == previous  -> 一个字都不写（"旧值没变就不发恢复写入"）；
//       2) 写后必须读回 == desired，否则抛异常；
//       3) 失败后**再读一次**：仍 == previous  => 抛带 Data["PreviousValueUnchanged"]=true 的异常
//          （**不伪报"已恢复"**，这正是 275HX 固件拒绝降压时要能区分的那一件事）；
//       4) 不是 previous => 尝试写回 previous 并二次校验，失败则带 "Restore failed: "。
//       Command(17,…) 被邮箱拒绝时给异常打 Data["MailboxWriteRejected"]=true，
//       由 RuntimeCpuTuning.Apply 转成"本会话禁用该选项"（_denied）。
//   * EncodeOffset/DecodeOffset —— mv × 1.024 四舍五入（AwayFromZero）取整，存 11 位补码；
//                     范围 -150..0 mV，越界直接抛（绝不静默截断）。
//   * Locked()     —— 命名互斥体 "Global\Access_Intel_OC_Mailbox"（1.5 s 超时，
//                     AbandonedMutexException 当成功），与其它访问该邮箱的程序互斥。
// ==============================================================================================
internal sealed class IntelOcMailbox : IDisposable
{
	// bit63 = "邮箱忙"（见 ReadIdle 的轮询）。
	private const ulong Busy = 9223372036854775808uL;

	// ⚠️ 委托签名里**不写** `ulong[]?`：本工程未开 <Nullable>（CS8632）。
	//    PawnIOWrapper.Execute(string, ulong[]?, ulong[]?) 运行时本来就会收到 null
	//    （写 MSR 时 output=null、读 MSR 时 input=null），语义一字未变。
	private readonly Func<string, ulong[], ulong[], bool> _execute;

	private readonly Mutex _mutex = new Mutex(initiallyOwned: false, "Global\\Access_Intel_OC_Mailbox");

	private readonly Dictionary<int, uint> _baseline = new Dictionary<int, uint>();

	internal IntelOcMailbox(Func<string, ulong[], ulong[], bool> execute)
	{
		_execute = execute;
	}

	internal IntelOcDomain Probe(int domain)
	{
		return Locked(() =>
		{
			ValidateDomain(domain);
			uint capabilities = Command(1, domain, 0u);
			uint value = Command(16, domain, 0u);
			_baseline.TryAdd(domain, value);
			return new IntelOcDomain(domain, value, capabilities);
		});
	}

	internal IntelOcDomain Apply(int domain, decimal? offsetMv = null, int? ratio = null)
	{
		return Locked(() =>
		{
			IntelOcDomain intelOcDomain = Probe(domain);
			uint num = intelOcDomain.Value;
			if (offsetMv.HasValue)
			{
				if (!intelOcDomain.VoltageSupported)
				{
					throw new InvalidOperationException("Voltage offset unsupported or locked.");
				}
				num = (num & 0x1FFFFF) | EncodeOffset(offsetMv.Value);
			}
			if (ratio.HasValue)
			{
				if (!intelOcDomain.RatioSupported || ratio.Value < 8 || ratio.Value > intelOcDomain.MaximumRatio)
				{
					throw new ArgumentOutOfRangeException("ratio", "Ratio exceeds the reported OC capability.");
				}
				num = (num & 0xFFFFFF00u) | (uint)ratio.Value;
			}
			WriteVerified(domain, num, intelOcDomain.Value);
			return intelOcDomain with
			{
				Value = num
			};
		});
	}

	internal void Restore(int domain)
	{
		Locked(() =>
		{
			ValidateDomain(domain);
			if (!_baseline.TryGetValue(domain, out var value))
			{
				throw new InvalidOperationException("No captured baseline.");
			}
			uint previous = Command(16, domain, 0u);
			WriteVerified(domain, value, previous);
			return true;
		});
	}

	private void WriteVerified(int domain, uint desired, uint previous)
	{
		if (desired == previous)
		{
			return;
		}
		try
		{
			Command(17, domain, desired);
			if (Command(16, domain, 0u) != desired)
			{
				throw new InvalidOperationException("CPU readback differs; firmware protection may have rejected the change.");
			}
		}
		catch (Exception ex)
		{
			bool flag = false;
			try
			{
				flag = Command(16, domain, 0u) == previous;
			}
			catch (Exception ex2)
			{
				ex.Data["RecoveryReadbackFailure"] = ex2.Message;
			}
			if (flag)
			{
				throw Failure(ex, "Previous value unchanged.", unchanged: true);
			}
			try
			{
				Command(17, domain, previous);
				if (Command(16, domain, 0u) != previous)
				{
					throw new InvalidOperationException("Baseline readback differs.");
				}
			}
			catch (Exception ex3)
			{
				throw Failure(ex, "Restore failed: " + ex3.Message);
			}
			throw Failure(ex, "Previous value restored.");
		}
	}

	private static InvalidOperationException Failure(Exception original, string detail, bool unchanged = false)
	{
		if (original.Data["RecoveryReadbackFailure"] is string text)
		{
			detail = detail + " Recovery readback failed: " + text;
		}
		InvalidOperationException ex = new InvalidOperationException(original.Message + " " + detail, original);
		if (original.Data.Contains("MailboxWriteRejected"))
		{
			ex.Data["MailboxWriteRejected"] = true;
		}
		if (unchanged)
		{
			ex.Data["PreviousValueUnchanged"] = true;
		}
		return ex;
	}

	private uint Command(byte command, int domain, uint value)
	{
		ReadIdle();
		ulong num = (ulong)(long.MinValue | ((long)domain << 40)) | ((ulong)command << 32) | value;
		if (!_execute("ioctl_write_msr", new ulong[2] { 336uL, num }, null))
		{
			throw new IOException("OC Mailbox write denied or driver unavailable.");
		}
		ulong num2 = ReadIdle();
		byte b = (byte)(num2 >> 32);
		if (b != 0)
		{
			InvalidOperationException ex = new InvalidOperationException($"OC Mailbox rejected command 0x{command:X2}: 0x{b:X2}.");
			if (command == 17)
			{
				ex.Data["MailboxWriteRejected"] = true;
			}
			throw ex;
		}
		return (uint)num2;
	}

	private ulong ReadIdle()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ulong[] array = new ulong[1];
		do
		{
			if (!_execute("ioctl_read_msr", new ulong[1] { 336uL }, array))
			{
				throw new IOException("OC Mailbox read denied or driver unavailable.");
			}
			if ((array[0] & 0x8000000000000000uL) == 0L)
			{
				return array[0];
			}
			Thread.Sleep(1);
		}
		while (stopwatch.ElapsedMilliseconds < 200);
		throw new TimeoutException("OC Mailbox remained busy for 200 ms.");
	}

	private T Locked<T>(Func<T> action)
	{
		bool flag;
		try
		{
			flag = _mutex.WaitOne(1500);
		}
		catch (AbandonedMutexException)
		{
			flag = true;
		}
		if (!flag)
		{
			throw new TimeoutException("Another CPU tuning request holds the mailbox.");
		}
		try
		{
			return action();
		}
		finally
		{
			_mutex.ReleaseMutex();
		}
	}

	private static void ValidateDomain(int domain)
	{
		if ((domain != 0 && domain != 2) || 1 == 0)
		{
			throw new ArgumentOutOfRangeException("domain");
		}
	}

	internal static uint EncodeOffset(decimal mv)
	{
		if ((mv < -150m || mv > 0m) ? true : false)
		{
			throw new ArgumentOutOfRangeException("mv");
		}
		return (uint)(((int)decimal.Round(mv * 1.024m, 0, MidpointRounding.AwayFromZero) & 0x7FF) << 21);
	}

	internal static decimal DecodeOffset(uint value)
	{
		int num = (int)((value >> 21) & 0x7FF);
		if ((num & 0x400) != 0)
		{
			num -= 2048;
		}
		return (decimal)num / 1.024m;
	}

	public void Dispose()
	{
		_mutex.Dispose();
	}
}
