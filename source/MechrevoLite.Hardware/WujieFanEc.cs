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
			bool okMode = EcPortIo.TryReadByteStable(RegisterMode, out int modeValue, out string modeNote);
			bool okDuty1 = EcPortIo.TryReadByteStable(RegisterDutyFan1, out int duty1Value, out string duty1Note);
			bool okDuty2 = EcPortIo.TryReadByteStable(RegisterDutyFan2, out int duty2Value, out string duty2Note);
			bool okTemp = EcPortIo.TryReadByte(RegisterCpuTemp, out int tempValue);
			bool okRpm1 = EcPortIo.TryReadBytes(RegisterFan1RpmHigh, 2, out int[] rpm1Bytes);
			bool okRpm2 = EcPortIo.TryReadBytes(RegisterFan2RpmHigh, 2, out int[] rpm2Bytes);
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
		}
		catch (Exception ex)
		{
			sb.AppendLine("风扇 EC 探针异常（已忽略）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}
}
