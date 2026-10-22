using System;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界风扇曲线）：无界（WUJIE14XA）EC 风扇占空比直写后端 —— 走 PawnIO ACPI EC 0x92/0x93 通道，
/// 与宝龙达写风扇目标同一条路（不需要 inpoutx64）。
///
/// <para>依据：无界 EC 的占空比目标为主扇 <c>0x1804</c>、副扇 <c>0x1809</c>（0..200 = 0..100%）；
/// 无界转速 <c>0xC834/0xC835</c> 已用同一条 ACPI EC 0x92 通道读取（见 <see cref="MechrevoHw.TryReadWujieEcFanSpeeds" />）。
/// 温度 <c>0x043E</c>，转速 <c>0x0464/0x0465</c>、<c>0x046C/0x046D</c>（大端 16 位）。</para>
///
/// <para>⚠️ 本类当前提供：只读状态（诊断用）+ **单次**写占空比（带 0..200 白名单与回读校验）。
/// 25Hz 持续写入的曲线控制器不在本类（下一步接入）；任何写失败都会返回 false，由调用方交还 EC。</para>
/// </summary>
internal static class WujieFanEc
{
	internal const int RegisterMode = 0x200D;

	internal const int RegisterDutyFan1 = 0x1804;

	internal const int RegisterDutyFan2 = 0x1809;

	internal const int RegisterCpuTemp = 0x043E;

	internal const int RegisterFan1RpmHigh = 0x0464;

	internal const int RegisterFan2RpmHigh = 0x046C;

	/// <summary>副扇可用下限（%）：低于该值副扇可能被判堵转闭锁。</summary>
	internal const int FanFloorPercent = 20;

	/// <summary>是否无界（APU 无独显）机型；诊断/UI 只在这个平台启用。</summary>
	internal static bool IsWujiePlatform()
	{
		try
		{
			return Program.hw?.WujieSmuApu == true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>只读：模式位、双扇占空比、CPU 温度、双扇转速。</summary>
	internal static bool TryReadState(out byte mode, out int duty1, out int duty2, out int tempC, out int fan1Rpm, out int fan2Rpm, out string detail)
	{
		mode = 0;
		duty1 = -1;
		duty2 = -1;
		tempC = -1;
		fan1Rpm = -1;
		fan2Rpm = -1;
		try
		{
			// 2026-10-22：控制器正在走 I2EC 时，ACPI-EC 0x92 可能被占用而读失败；
			// 诊断优先用 I2EC（与写同一通道），不可用时再回退 ACPI-EC。
			bool okMode = ReadByteAny(RegisterMode, out int modeValue, out string modeNote);
			bool okDuty1 = ReadByteAny(RegisterDutyFan1, out int duty1Value, out string duty1Note);
			bool okDuty2 = ReadByteAny(RegisterDutyFan2, out int duty2Value, out string duty2Note);
			bool okTemp = ReadByteAny(RegisterCpuTemp, out int tempValue, out _);
			int[] rpm1Bytes = new int[2];
			int[] rpm2Bytes = new int[2];
			bool okRpm1 = ReadByteAny(RegisterFan1RpmHigh, out rpm1Bytes[0], out _) && ReadByteAny(RegisterFan1RpmHigh + 1, out rpm1Bytes[1], out _);
			bool okRpm2 = ReadByteAny(RegisterFan2RpmHigh, out rpm2Bytes[0], out _) && ReadByteAny(RegisterFan2RpmHigh + 1, out rpm2Bytes[1], out _);
			mode = okMode ? (byte)modeValue : (byte)0;
			duty1 = okDuty1 && duty1Value >= 0 ? duty1Value : -1;
			duty2 = okDuty2 && duty2Value >= 0 ? duty2Value : -1;
			tempC = okTemp && tempValue >= 0 && tempValue < 128 ? tempValue : -1;
			if (okRpm1 && rpm1Bytes.Length == 2)
			{
				fan1Rpm = ((rpm1Bytes[0] & 0xFF) << 8) | (rpm1Bytes[1] & 0xFF);
			}
			if (okRpm2 && rpm2Bytes.Length == 2)
			{
				fan2Rpm = ((rpm2Bytes[0] & 0xFF) << 8) | (rpm2Bytes[1] & 0xFF);
			}
			detail = "mode=" + (okMode ? "0x" + modeValue.ToString("X2") : "读取失败(" + modeNote + ")")
				+ " · duty1=" + (okDuty1 ? duty1Value.ToString() : "读取失败(" + duty1Note + ")")
				+ " · duty2=" + (okDuty2 ? duty2Value.ToString() : "读取失败(" + duty2Note + ")");
			return okMode && okDuty1 && okDuty2;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>2026-10-22：优先 I2EC（inpoutx64）读，回退 ACPI-EC；供诊断与状态读取使用。</summary>
	private static bool ReadByteAny(int address, out int value, out string note)
	{
		if (I2ecInpOut.IsAvailable && I2ecInpOut.TryReadEcByte(address, out value, out string i2ecNote))
		{
			note = "I2EC";
			return true;
		}
		bool ok = EcPortIo.TryReadByteStable(address, out value, out note);
		return ok;
	}

	/// <summary>单次写双扇占空比（百分比，0..100；非 0 时下限 20%）：写后逐个回读确认。</summary>
	internal static bool TrySetDutyPercent(int percent, out string detail)
	{
		detail = "";
		if (!IsWujiePlatform())
		{
			detail = "不是无界机型";
			return false;
		}
		if (percent < 0)
		{
			percent = 0;
		}
		if (percent > 100)
		{
			percent = 100;
		}
		if (percent > 0 && percent < FanFloorPercent)
		{
			// 副扇低于 20% 会判堵转闭锁；宁可不给那么低。
			percent = FanFloorPercent;
		}
		if (!TryReadState(out byte mode, out _, out _, out _, out _, out _, out string readDetail))
		{
			detail = "读取状态失败：" + readDetail;
			return false;
		}
		if ((mode & 0x03) != 0x03)
		{
			detail = "I2EC 不是读写模式（0x200D=0x" + mode.ToString("X2") + "）";
			return false;
		}
		int raw = (int)Math.Round(percent * 2.0);
		if (raw < 1)
		{
			raw = 1;
		}
		bool ok1 = EcPortIo.TryWriteWujieFanDuty(RegisterDutyFan1, raw, out int readBack1);
		bool ok2 = EcPortIo.TryWriteWujieFanDuty(RegisterDutyFan2, raw, out int readBack2);
		detail = "目标=" + percent + "% (raw=" + raw + ")"
			+ " · 主扇写/回读=" + (ok1 ? readBack1.ToString() : "失败")
			+ " · 副扇写/回读=" + (ok2 ? readBack2.ToString() : "失败");
		return ok1 && ok2;
	}

	/// <summary>2026-10-22：宝龙达同款 LPC EC-RAM 通道只读探测（PawnIO LpcIO）。返回一段诊断文本。</summary>
	private static string ProbeLpcPath()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[无界风扇 · LPC EC-RAM 只读探测（宝龙达同款通道，仅读）]");
		try
		{
			sb.AppendLine("LPC EC-RAM 基准（温度 0x043E / 转速 0x0464）："
				+ DescribeLpc(0x043E) + " · " + DescribeLpc(0x0464));
			sb.AppendLine("无界占空比（0x1800/0x1801/0x1804/0x1806/0x1809）："
				+ DescribeLpc(0x1800) + " · " + DescribeLpc(0x1801) + " · " + DescribeLpc(0x1804) + " · " + DescribeLpc(0x1806) + " · " + DescribeLpc(0x1809));
			sb.AppendLine("模式/转速（0x200D / 0xC834 / 0xC835）：" + DescribeLpc(0x200D) + " · " + DescribeLpc(0xC834) + " · " + DescribeLpc(0xC835));
			bool dutyOk = EcLpcIo.TryReadByte(0x1804, out int duty1) && duty1 >= 0 && duty1 <= 200
				&& EcLpcIo.TryReadByte(0x1809, out int duty2) && duty2 >= 0 && duty2 <= 200;
			bool tempOk = EcLpcIo.TryReadByte(0x043E, out int temp) && temp > 0 && temp < 120;
			sb.AppendLine(dutyOk && tempOk
				? "结论：LPC 通道可读无界占空比/温度 ⇒ 写入也可尝试改走 PawnIO（不依赖 inpoutx64）。"
				: "结论：LPC 通道读不到（或值不合理）⇒ 写入仍需 inpoutx64（I2EC）或自研 PawnIO I2EC 模块。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("LPC 探测异常（已忽略）：" + ex.GetType().Name + " " + ex.Message);
		}
		return sb.ToString().TrimEnd();
	}

	private static string DescribeLpc(int address)
	{
		return EcLpcIo.TryReadByte(address, out int value)
			? "0x" + address.ToString("X4") + "=0x" + value.ToString("X2")
			: "0x" + address.ToString("X4") + "=读不到";
	}

	/// <summary>诊断包用：无界风扇 EC 状态（只读）。</summary>
	internal static string Describe()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[无界风扇 EC（只读探针）]");
		try
		{
			sb.AppendLine("平台识别：" + (IsWujiePlatform() ? "命中（WujieSmuApu）" : "未命中（本机不是无界 APU，跳过）"));
			if (!IsWujiePlatform())
			{
				return sb.ToString();
			}
			sb.AppendLine("通道：PawnIO ACPI-EC 0x62/0x66 · 命令 0x92/0x93 · 地址 0x1804/0x1809（宝龙达同款通道，不需要 inpoutx64）");
			// 2026-10-22：无界风扇**写**走 I2EC，需要本机 inpoutx64（DLL 放 LM 安装目录即可）；这里如实报告状态。
			bool i2ecOk = I2ecInpOut.IsAvailable;
			sb.AppendLine("I2EC 写通道（inpoutx64）：" + (i2ecOk ? "可用 · " : "不可用 · ") + I2ecInpOut.InitDetail);
			if (!TryReadState(out byte mode, out int duty1, out int duty2, out int tempC, out int fan1Rpm, out int fan2Rpm, out string detail))
			{
				sb.AppendLine("读取失败：" + detail);
				sb.AppendLine("结论：通道/地址不可用（未写入任何占空比；不据此判定不支持）");
				return sb.ToString();
			}
			sb.AppendLine($"模式 0x200D=0x{mode:X2}（bit0/1={(((mode & 0x03) == 0x03) ? "读写模式" : "非读写模式")}）");
			sb.AppendLine($"占空比：主扇={duty1}/200 · 副扇={duty2}/200 · CPU={tempC}℃ · 主扇={fan1Rpm}rpm · 副扇={fan2Rpm}rpm");
			sb.AppendLine("参考语义：占空比 0..200 → 0..100%；低于 20% 副扇可能被判堵转闭锁；写入需 25Hz 持续维持（EC 会覆写）。");
			sb.AppendLine("边界：本段只读；写曲线尚未接入 UI（下一步：25Hz 控制器 + FanCurveForm 无界分支）。");
			// 2026-10-22（机主：无界能不能走宝龙达那条 PawnIO 路子）：只读探测 LPC EC-RAM 通道是否能摸到无界的
			// 温度/转速/占空比地址。若能读到合理值，就可以把写入也挪到 PawnIO（不再依赖 inpoutx64）。
			sb.AppendLine(ProbeLpcPath());
		}
		catch (Exception ex)
		{
			sb.AppendLine("风扇 EC 探针异常（已忽略）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}
}
