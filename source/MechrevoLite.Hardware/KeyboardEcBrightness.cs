// ⭐⭐ 2026-10-19（30 系宝龙达 · 键盘背光亮度 · EC 写 0x1807）：
// 机主已用 WMI18 ↔ EC 时间关联 24/24 命中确认：0x1807 就是键盘背光亮度寄存器，
// 值 = PWM 占空比：档位 0/1/2/3 ⇒ 0x00 / 0x66 / 0xB3 / 0xFF（0% / 40% / 70% / 100%）。
//
// 本类的硬约束（能力位铁律 + 写白名单）：
//   · **只写** 0x1807；**只允许** 0x00/0x66/0xB3/0xFF 四个值；⛔ 其它地址/值一律拒绝；
//   · 不重试：写一次 → 回读必须等于目标；失败就如实返回 false；
//   · 写后同时读 WMI 功能号 18 作为**交叉验证**（日志打印两者）；
//   · ACPI（0x93）写不进 ⇒ 再试 LPC（EcLpcIo 已把 0x1807 加入写白名单）。
//
// ⚠️ 关灯（档位 0）在 30 系仍走调用方既有 `18=0` 路径（本类仍支持 0，但调用方不使用）；
// ⚠️ 40 系/其它机型不进入本类（调用方只在 Gen30 分支调用）。

using System;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 30 系键盘背光亮度（EC 0x1807 · PWM 4 档）白名单写 + 回读 + WMI18 交叉验证 ✅。
/// <para>⚠️ 只写 0x1807 的 0x00/0x66/0xB3/0xFF；⛔ 不碰其它 EC 地址、⛔ 不重试 ✅。</para>
/// </summary>
internal static class KeyboardEcBrightness
{
	/// <summary>⭐ EC 亮度寄存器地址（机主关联确认）✅。</summary>
	internal const int RegisterAddress = 0x1807;

	/// <summary>⭐ 档位 0/1/2/3 ⇒ PWM 值 0x00/0x66/0xB3/0xFF ✅。</summary>
	private static readonly byte[] LevelValues = new byte[4] { 0x00, 0x66, 0xB3, 0xFF };

	/// <summary>⭐ 档位对应的 PWM 值（越界返回 -1）✅。</summary>
	internal static int ValueForLevel(int level)
	{
		return (level >= 0 && level <= 3) ? LevelValues[level] : -1;
	}

	/// <summary>
	/// ⭐ 尝试把 30 系键盘亮度设为指定档位（EC 0x1807）✅。
	/// <para>流程：先读 → 已是目标则直接成功；否则 ACPI 0x93 写 → 回读；ACPI 不行再试 LPC → 回读；
	/// ⛔ 失败不重试；每次写后读 WMI18 交叉验证并打日志 ✅。</para>
	/// </summary>
	/// <param name="level">档位 0..3 ✅。</param>
	/// <param name="wmi">宝龙达 WMI（可为 null；仅用于交叉验证）✅。</param>
	/// <param name="readback">最终回读的 0x1807 值（失败为 -1 或最后一次回读）✅。</param>
	/// <param name="wmi18">写后读到的 WMI 功能号 18（读不到为 -1）✅。</param>
	internal static bool TrySetLevel(int level, BaolongdaWmi wmi, out int readback, out int wmi18, string source = "未标注")
	{
		readback = -1;
		wmi18 = -1;
		// ⭐⭐ 2026-10-19（门控铁律）：**必须同时是宝龙达 + Gen30**（同方 30 系也 Gen30，⛔ 不碰 EC 0x1807）✅
		MechrevoHw hw = Program.hw;
		if (hw == null || hw.DgpuGeneration != DgpuGenerationKind.Gen30 || !hw.UsesBaolongda)
		{
			string skipMessage = $"[kbd] 30 系 EC 键盘：跳过（非宝龙达 · gen={(hw == null ? "hw=null" : hw.DgpuGeneration.ToString())}）";
			if (Logger.TryMarkChanged("kbd30-ec-brightness-skip", skipMessage))
			{
				Logger.WriteError(skipMessage);
			}
			return false;
		}
		wmi18 = ReadWmi18(wmi);
		int targetValue = ValueForLevel(level);
		if (targetValue < 0)
		{
			LogVisible("kbd-ec-invalid-level", $"[kbd-ec] ⛔ 写亮度失败：档位={level} 越界（只允许 0..3）⇒ 不写");
			return false;
		}
		byte target = (byte)targetValue;

		// ⭐ 先读：已是目标 ⇒ 不写 ✅
		int current = -1;
		bool currentOk = EcPortIo.TryReadByte(RegisterAddress, out current);
		if (currentOk && current == target)
		{
			readback = current;
			LogVisible("kbd-ec-already-" + level,
				$"[kbd-ec] 写亮度：档位={level} 已是目标 0x1807=0x{target:X2}"
				+ $"（回读=0x{current:X2} ✅ · WMI18={FormatWmi18(wmi18)}）");
			return true;
		}

		// ⭐ ① ACPI 0x93 写 + 回读 ✅
		string via = "ACPI";
		TraceEcWrite(level, target, source, via);
		bool wrote = EcPortIo.TryWriteByte(RegisterAddress, target);
		int rb = -1;
		bool rbOk = EcPortIo.TryReadByte(RegisterAddress, out rb);
		bool ok = wrote && rbOk && rb == target;

		// ⭐ ② ACPI 不行 ⇒ LPC 写 + 回读（⛔ 只再试一次，不重试）✅
		if (!ok)
		{
			via = "LPC";
			TraceEcWrite(level, target, source, via);
			bool lpcWrote = EcLpcIo.TryWriteByte(RegisterAddress, target, out int lpcBack);
			bool lpcRead = EcLpcIo.TryReadByte(RegisterAddress, out int lpcCheck);
			if (lpcWrote && lpcRead && lpcCheck == target)
			{
				ok = true;
				rb = lpcCheck;
			}
			else
			{
				rb = lpcRead ? lpcCheck : rb;
			}
		}

		readback = rb;
		// ⭐ 写后读 WMI18 交叉验证（即使失败也打印当前实际）✅
		wmi18 = ReadWmi18(wmi);
		if (ok)
		{
			LogVisible("kbd-ec-ok-" + level + "-" + via,
				$"[kbd-ec] 写亮度：档位={level} ⇒ 0x1807=0x{target:X2}"
				+ $"（回读=0x{readback:X2} ✅ · WMI18={FormatWmi18(wmi18)} · 通道={via}）");
			return true;
		}
		LogVisible("kbd-ec-fail-" + level + "-" + via,
			$"[kbd-ec] ⛔ 写亮度失败：档位={level} 目标=0x{target:X2}"
			+ $" 回读={(readback >= 0 ? "0x" + readback.ToString("X2") : "读不到")}"
			+ $"（WMI18={FormatWmi18(wmi18)} · 通道={via}）⇒ 不重试");
		return false;
	}

	/// <summary>⭐ 只读 WMI 功能号 18（交叉验证；读不到返回 -1）✅。</summary>
	private static int ReadWmi18(BaolongdaWmi wmi)
	{
		if (wmi == null)
		{
			return -1;
		}
		try
		{
			return wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte value) ? value : -1;
		}
		catch
		{
			return -1;
		}
	}

	/// <summary>⭐ WMI18 显示文本 ✅。</summary>
	private static string FormatWmi18(int wmi18)
	{
		return (wmi18 >= 0) ? wmi18.ToString() : "读不到";
	}

	/// <summary>⭐ 2026-10-19（追踪「谁在写 0x1807」）：每次真实 EC 写前打来源 + 前 3 帧调用链（⛔ 不打整栈）✅。</summary>
	private static void TraceEcWrite(int level, byte target, string source, string channel)
	{
		Logger.WriteError($"[kbd-trace] 写 0x1807=0x{target:X2} ← 来源={source} · 档位={level}"
			+ $" · 通道={channel} · 调用链={DescribeEcCallChain(3)}");
	}

	/// <summary>⭐ 截前 N 帧调用链（类型.方法 ← …），⛔ 不打完整栈 ✅。</summary>
	private static string DescribeEcCallChain(int maxFrames)
	{
		try
		{
			System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(2, fNeedFileInfo: false);
			string text = "";
			int count = Math.Min(Math.Max(0, maxFrames), stackTrace.FrameCount);
			for (int i = 0; i < count; i++)
			{
				var method = stackTrace.GetFrame(i)?.GetMethod();
				if (method == null)
				{
					continue;
				}
				if (text.Length > 0)
				{
					text += " ← ";
				}
				text += method.DeclaringType?.Name + "." + method.Name;
			}
			return (text.Length > 0) ? text : "n/a";
		}
		catch
		{
			return "n/a";
		}
	}

	/// <summary>⭐ “默认可见 + 去重”日志（错误级别 ⇒ 默认日志可见；同 key+同内容只记一次）✅。</summary>
	private static void LogVisible(string key, string message)
	{
		if (Logger.TryMarkChanged(key, message))
		{
			Logger.WriteError(message);
		}
	}
}
