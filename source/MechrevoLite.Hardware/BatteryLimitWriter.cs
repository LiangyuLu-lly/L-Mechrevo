// 2026-10-18（宝龙达 · 充电阈值）：⭐⭐ **限充启用/关闭**（ACPI EC 白名单写）⭐⭐
//
// ⭐ 协议事实来源：JiaoLong16Pro-Battery-threshold（BM5235 / 2023 款实测；⛔ 未复制代码）：
//    · 常驻值：0x83A1 = 停充高 % · 0x83A2 = 起充低 % · 0x83C0 = 模式（0x00 正常 / 0x48 限充生效）
//    · mailbox：0x8153 = 高参数 · 0x8154 = 低参数 · 0x8151 = 命令（0x0D 启用/0x0F 关闭）
//                · 0x8150 = 触发（写 0x10）· 0x8152 = 结果（0x55 成功）
//
// ⭐ 启用顺序（严格）：身份 OK → 0x8150==0 → AC 在线 → 0<low<high<=100（SOC≥high 允许，参考 initial_discharge_allowed=true）
//    → 写 0x8153=high → 0x8154=low（回读）→ 0x8151=0x0D → 0x8150=0x10
//    → 轮询 0x8150 回 0（≤3 s）→ 校验 0x8152==0x55 && 0x83C0==0x48（+ 0x83A1/0x83A2）
// ⭐ 关闭顺序：0x8151=0x0F → 0x8150=0x10 → 轮询 → 校验 0x8152==0x55 && 0x83C0==0x00
// ⛔ 失败**不重试、不做清理写**；⛔ 只写 4 个白名单地址（见 EcPortIo.TryWriteBatteryByte）✅

using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 宝龙达充电阈值启用/关闭（严格时序 · 失败不重试）✅。</summary>
internal static class BatteryLimitWriter
{
	internal const int HighRegister = 0x83A1;

	internal const int LowRegister = 0x83A2;

	internal const int ModeRegister = 0x83C0;

	internal const int ResultRegister = 0x8152;

	internal const int ModeActive = 0x48;

	internal const int TimeoutMs = 3000;

	/// <summary>⭐ 只读当前常驻状态：高/低/模式 ✅。</summary>
	internal static bool TryReadState(out int high, out int low, out int mode, out string detail)
	{
		high = -1;
		low = -1;
		mode = -1;
		bool h = ReadEc(HighRegister, out high);
		bool l = ReadEc(LowRegister, out low);
		bool m = ReadEc(ModeRegister, out mode);
		detail = "0x83A1=" + Fmt(high) + " · 0x83A2=" + Fmt(low) + " · 0x83C0=" + Fmt(mode);
		return h && l && m;
	}

	/// <summary>
	/// ⭐ 启用限充。<para>⚠️ 2026-10-18（机主按参考实现纠正）：⭐ SOC ≥ high **默认允许**启用
	/// （等价参考实现 `initial_discharge_allowed=true`，可能触发一次短暂放电把电量降到阈值）——
	/// 只写一行日志，⛔ 不再拦截；其它安全前置（AC/参数/身份/触发空闲）保持不变 ✅</para>
	/// </summary>
	internal static bool TryEnable(int low, int high, out string detail)
	{
		StringBuilder log = new StringBuilder();
		try
		{
			if (!(low > 0 && low < high && high <= 100))
			{
				detail = "参数非法：要求 0 < low < high <= 100（收到 low=" + low + " high=" + high + "）";
				return false;
			}
			log.AppendLine("参数检查：low=" + low + "% · high=" + high + "% ✅");

			if (!EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string idNote))
			{
				detail = log + "⛔ EC 身份未通过：" + idNote;
				return false;
			}
			log.AppendLine("EC 身份：" + idNote);

			if (SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online)
			{
				detail = log + "⛔ AC 未在线 ⇒ 不启用（参考实现要求 AC 在线）";
				return false;
			}
			log.AppendLine("AC = Online ✅");

			if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int trigger) || trigger != 0)
			{
				detail = log + "⛔ 0x8150 触发=" + Fmt(trigger) + "（应为 0x00）⇒ 有未完成事务，不写";
				return false;
			}
			log.AppendLine("0x8150 触发 = 0x00 ✅");

			TryReadState(out int curHigh, out int curLow, out int curMode, out string stateDetail);
			log.AppendLine("当前状态：" + stateDetail);
			if (curMode == ModeActive && curHigh == high && curLow == low)
			{
				detail = log + "⭐ 已是目标配置（限充已生效）⇒ 本次无需写";
				return true;
			}
			if (curMode != 0 && curMode != ModeActive)
			{
				detail = log + "⛔ 当前模式 " + Fmt(curMode) + " 非 0x00/0x48 ⇒ 不写（请先显式 --disable）";
				return false;
			}
			if (curMode == ModeActive)
			{
				log.AppendLine("当前限充生效但阈值不同 ⇒ 先关闭（0x8151=0x0F → 触发 → 校验 mode=0）");
				if (!TryDisableCore(log))
				{
					detail = log.ToString();
					return false;
				}
			}

			int batteryPercent = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100.0);
			log.AppendLine("电池 SOC = " + batteryPercent + "%");
			if (batteryPercent >= high)
			{
				// ⭐ 机主要求：只记一行日志，⛔ 不弹 UI、⛔ 不拦截 ✅
				string note = "[battery] 启用时 SOC(" + batteryPercent + "%) ≥ 停充阈值(" + high
					+ "%) ⇒ 按参考实现允许（initial_discharge_allowed=true），可能触发一次短暂放电";
				log.AppendLine(note);
				Logger.WriteLine(note);
			}

			log.AppendLine("① 写参数：0x8153=high(" + high + ") → 0x8154=low(" + low + ")");
			if (!WriteVerified(EcPortIo.BatteryHighParamRegister, high, "0x8153=high", log) ||
				!WriteVerified(EcPortIo.BatteryLowParamRegister, low, "0x8154=low", log))
			{
				detail = log.ToString();
				return false;
			}
			bool rbHighOk = ReadEc(EcPortIo.BatteryHighParamRegister, out int rbHigh);
			bool rbLowOk = ReadEc(EcPortIo.BatteryLowParamRegister, out int rbLow);
			if (!rbHighOk || !rbLowOk || rbHigh != high || rbLow != low)
			{
				detail = log + "⛔ 参数回读不一致（0x8153=" + Fmt(rbHigh) + " · 0x8154=" + Fmt(rbLow) + "）⇒ 不发启用触发";
				return false;
			}
			log.AppendLine("参数回读：0x8153=" + Fmt(rbHigh) + " · 0x8154=" + Fmt(rbLow) + " ✅");

			log.AppendLine("② 写命令 0x8151=0x0D → 检查 0x8150==0 → 触发 0x8150=0x10");
			if (!WriteVerified(EcPortIo.BatteryCommandRegister, 0x0D, "0x8151=0x0D(启用)", log))
			{
				detail = log.ToString();
				return false;
			}
			if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int triggerBefore) || triggerBefore != 0)
			{
				detail = log + "⛔ 触发前 0x8150=" + Fmt(triggerBefore) + " ≠ 0 ⇒ 不触发";
				return false;
			}
			if (!WriteTriggerPulse(log))
			{
				detail = log.ToString();
				return false;
			}

			log.AppendLine("③ 轮询 0x8150 回 0（≤" + TimeoutMs + " ms；⭐ 触发寄存器自清零，不校验回读==0x10）");
			if (!WaitTriggerDone(log))
			{
				detail = log.ToString();
				return false;
			}
			log.AppendLine("④ 结果态校验（成功判据）：0x83C0==0x48 且 0x83A1==high 且 0x83A2==low 且 0x8152==0x55");
			bool resultOk = ReadEc(ResultRegister, out int result) && result == 0x55;
			bool modeOk = ReadEc(ModeRegister, out int modeAfter) && modeAfter == ModeActive;
			bool highAfterOk = ReadEc(HighRegister, out int highAfter);
			bool lowAfterOk = ReadEc(LowRegister, out int lowAfter);
			bool thrOk = highAfterOk && lowAfterOk && highAfter == high && lowAfter == low;
			log.AppendLine("0x8152=" + Fmt(result) + " · 0x83C0=" + Fmt(modeAfter)
				+ " · 0x83A1=" + Fmt(highAfter) + " · 0x83A2=" + Fmt(lowAfter));
			detail = log.ToString();
			if (!(resultOk && modeOk && thrOk))
			{
				detail += "⛔ 启用结果校验未通过（result/mode/thresholds）⇒ 如实报失败，不重试、不做清理写";
				return false;
			}
			detail += "✅ 限充已启用（mode=0x48 · 高=" + high + "% · 低=" + low + "%）";
			return true;
		}
		catch (Exception ex)
		{
			detail = log + "异常：" + ex.GetType().Name + " " + ex.Message + "（不重试）";
			return false;
		}
	}

	/// <summary>⭐ 关闭限充（恢复默认充电）；失败不重试 ✅。</summary>
	internal static bool TryDisable(out string detail)
	{
		StringBuilder log = new StringBuilder();
		try
		{
			if (!EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string idNote))
			{
				detail = "⛔ EC 身份未通过：" + idNote;
				return false;
			}
			log.AppendLine("EC 身份：" + idNote);
			if (!ReadEc(ModeRegister, out int modeBefore))
			{
				detail = log + "⛔ 读 0x83C0 失败";
				return false;
			}
			log.AppendLine("当前 0x83C0=" + Fmt(modeBefore));
			if (modeBefore == 0)
			{
				detail = log + "⭐ 当前已是正常模式（0x00）⇒ 无需关闭";
				return true;
			}
			if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int trigger) || trigger != 0)
			{
				detail = log + "⛔ 0x8150=" + Fmt(trigger) + " ≠ 0 ⇒ 有未完成事务，不写";
				return false;
			}
			if (!TryDisableCore(log))
			{
				detail = log.ToString();
				return false;
			}
			detail = log.ToString();
			return true;
		}
		catch (Exception ex)
		{
			detail = log + "异常：" + ex.GetType().Name + " " + ex.Message + "（不重试）";
			return false;
		}
	}

	private static bool TryDisableCore(StringBuilder log)
	{
		if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int trigger) || trigger != 0)
		{
			log.AppendLine("⛔ 0x8150=" + Fmt(trigger) + " ≠ 0 ⇒ 不写关闭命令");
			return false;
		}
		if (!WriteVerified(EcPortIo.BatteryCommandRegister, 0x0F, "0x8151=0x0F(关闭)", log))
		{
			return false;
		}
		if (!ReadEc(EcPortIo.BatteryTriggerRegister, out int triggerBefore) || triggerBefore != 0)
		{
			log.AppendLine("⛔ 触发前 0x8150=" + Fmt(triggerBefore) + " ≠ 0 ⇒ 不触发");
			return false;
		}
		if (!WriteTriggerPulse(log))
		{
			return false;
		}
		if (!WaitTriggerDone(log))
		{
			return false;
		}
		bool resultOk = ReadEc(ResultRegister, out int result) && result == 0x55;
		bool modeOk = ReadEc(ModeRegister, out int modeAfter) && modeAfter == 0;
		log.AppendLine("0x8152=" + Fmt(result) + " · 0x83C0=" + Fmt(modeAfter));
		if (!(resultOk && modeOk))
		{
			log.AppendLine("⛔ 关闭结果校验未通过 ⇒ 如实报失败，不重试、不做清理写");
			return false;
		}
		log.AppendLine("✅ 已恢复默认充电（mode=0x00）");
		return true;
	}

	private static bool WaitTriggerDone(StringBuilder log)
	{
		long start = Environment.TickCount64;
		int last = -1;
		while (Environment.TickCount64 - start <= TimeoutMs)
		{
			if (!ReadEc(EcPortIo.BatteryTriggerRegister, out last))
			{
				log.AppendLine("⚠️ 轮询中读 0x8150 失败");
			}
			else if (last == 0)
			{
				log.AppendLine("0x8150 已回 0（等待 " + (Environment.TickCount64 - start) + " ms）✅");
				return true;
			}
			Thread.Sleep(20);
		}
		log.AppendLine("⛔ 等待 0x8150 回 0 超时（最后=" + Fmt(last) + "，≤" + TimeoutMs + " ms）⇒ 不重试");
		return false;
	}

	private static bool WriteTriggerPulse(StringBuilder log)
	{
		bool ok = EcPortIo.TryWriteBatteryTriggerPulse(out int readBack);
		log.AppendLine("写 0x8150=0x10(触发) ⇒ "
			+ (ok ? "已发送（自清零寄存器，不校验回读）" : "写入调用失败")
			+ " · 立即回读=" + Fmt(readBack));
		return ok;
	}

	private static bool WriteVerified(int address, int value, string label, StringBuilder log)
	{
		bool ok = EcPortIo.TryWriteBatteryByte(address, (byte)value, out int readBack);
		log.AppendLine("写 " + label + " ⇒ ok=" + ok + " · 回读=" + Fmt(readBack));
		return ok && readBack == value;
	}

	private static bool ReadEc(int address, out int value)
	{
		if (EcPortIo.TryReadByteStable(address, out value, out _))
		{
			return true;
		}
		return EcLpcIo.TryReadByteStable(address, out value, out _);
	}

	private static string Fmt(int value)
	{
		return value < 0 ? "读不到" : "0x" + value.ToString("X2");
	}
}
