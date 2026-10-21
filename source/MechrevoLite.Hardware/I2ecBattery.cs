using System;
using System.Diagnostics;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 无界（8000 系）电池 EC I2EC 通道（0x681 地址高 / 0x682 地址低 / 0x683 数据）。
/// <para>只经 PawnIO LpcIO 模块的原始端口读写（端口是否放行由模块白名单决定，探测不到就如实失败）。</para>
/// <para>用途：内置「电池充电电压修复」——覆盖 EC 老化降额（17600mV → 16600mV）。</para>
/// <para>⚠️ 写操作只用于显式测试/修复流程：写前记录原值，结束后恢复；不伪造水冷/门控等其它寄存器。</para>
/// </summary>
internal static class I2ecBattery
{
	private const ushort PortAddrHigh = 0x681;

	private const ushort PortAddrLow = 0x682;

	private const ushort PortData = 0x683;

	internal const int ExpectedBaseVoltageMv = 17600;

	internal const int ChargeCurrentMa = 2816;

	// 已验证的 EC 地址（与参考实现同口径）
	internal const int AddrDesiredVoltage = 0x0522;

	internal const int AddrQueuedVoltage = 0x0836;

	internal const int AddrDesiredCurrent = 0x0520;

	internal const int AddrQueuedCurrent = 0x0834;

	internal const int AddrQueuedInputCurrent = 0x083A;

	internal const int AddrStopGate = 0x0574;

	private static bool Select(int address, out string note)
	{
		note = string.Empty;
		if (!EcLpcIo.TryRawPortWrite(PortAddrHigh, (byte)(address >> 8), out string noteHi))
		{
			note = "0x681 写失败：" + noteHi;
			return false;
		}
		if (!EcLpcIo.TryRawPortWrite(PortAddrLow, (byte)(address & 0xFF), out string noteLo))
		{
			note = "0x682 写失败：" + noteLo;
			return false;
		}
		return true;
	}

	internal static bool TryReadByte(int address, out int value, out string note)
	{
		value = -1;
		if (!Select(address, out note))
		{
			return false;
		}
		if (!EcLpcIo.TryRawPortRead(PortData, out byte data, out note))
		{
			note = "0x683 读失败：" + note;
			return false;
		}
		value = data;
		return true;
	}

	internal static bool TryWriteByte(int address, byte value, out string note)
	{
		if (!Select(address, out note))
		{
			return false;
		}
		if (!EcLpcIo.TryRawPortWrite(PortData, value, out note))
		{
			note = "0x683 写失败：" + note;
			return false;
		}
		return true;
	}

	internal static bool TryReadLe16(int address, out int value, out string note)
	{
		value = -1;
		if (!TryReadByte(address, out int lo, out note) || !TryReadByte(address + 1, out int hi, out note))
		{
			return false;
		}
		value = lo | (hi << 8);
		return true;
	}

	internal static bool TryWriteLe16(int address, int value, out string note)
	{
		if (!TryWriteByte(address, (byte)(value & 0xFF), out note))
		{
			return false;
		}
		return TryWriteByte(address + 1, (byte)((value >> 8) & 0xFF), out note);
	}

	/// <summary>只读探测：通道是否可读写、EC 身份、4S/基础电压、目标/排队电压、停充门等。</summary>
	internal static string Probe()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("=== 电池 EC I2EC 通道探针（只读） ===");
		sb.AppendLine("主机：" + Environment.MachineName + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sb.AppendLine("端口：0x681/0x682/0x683（PawnIO LpcIO 原始端口读写）");
		try
		{
			if (!TryReadByte(0x200D, out int mode, out string modeNote))
			{
				sb.AppendLine("0x200D 读不到：" + modeNote);
			}
			else
			{
				sb.AppendLine(string.Format("0x200D=0x{0:X2}（I2EC 读写模式位 bit0/1={1}）", mode, ((mode & 0x03) == 0x03)));
			}
			if (TryReadByte(0x0454, out int id, out string idNote))
			{
				sb.AppendLine(string.Format("0x0454=0x{0:X2}（忽略 bit1 后=0x{1:X2}，身份期望 0x8C）", id, id & 0xFD));
			}
			else
			{
				sb.AppendLine("0x0454 读不到：" + idNote);
			}
			AppendLe(sb, "基础目标 base_voltage", 0x030E);
			AppendLe(sb, "实际 desired_voltage", AddrDesiredVoltage);
			AppendLe(sb, "排队 queued_voltage", AddrQueuedVoltage);
			AppendLe(sb, "desired_current", AddrDesiredCurrent);
			AppendLe(sb, "queued_current", AddrQueuedCurrent);
			AppendLe(sb, "queued_input_current", AddrQueuedInputCurrent);
			if (TryReadByte(AddrStopGate, out int gate, out string gateNote))
			{
				sb.AppendLine("stop_gate 0x0574 = 0x" + gate.ToString("X2") + "（1=停充门生效）");
			}
			else
			{
				sb.AppendLine("stop_gate 读不到：" + gateNote);
			}
			AppendLe(sb, "RSOC(%)", 0x04AB);
			AppendLe(sb, "pack_voltage", 0x0438);
		}
		catch (Exception ex)
		{
			sb.AppendLine("探针异常：" + ex.GetType().Name + " · " + ex.Message);
		}
		return sb.ToString();
	}

	private static void AppendLe(StringBuilder sb, string label, int address)
	{
		if (TryReadLe16(address, out int value, out string note))
		{
			sb.AppendLine(string.Format("{0}（0x{1:X4}）= {2}", label, address, value));
		}
		else
		{
			sb.AppendLine(string.Format("{0}（0x{1:X4}）读不到：{2}", label, address, note));
		}
	}

	/// <summary>
	/// 写入测试：保持 17600mV/2816mA + 解除停充门若干秒，结束后恢复原值（与参考实现同序列）。
	/// <para>⚠️ 只在显式调用时执行；失败不重试。</para>
	/// </summary>
	internal static bool TryHoldFull(int seconds, out string detail)
	{
		StringBuilder log = new StringBuilder();
		detail = string.Empty;
		try
		{
			if (!TryReadLe16(0x030E, out int baseVoltage, out string baseNote))
			{
				detail = "基础电压读不到：" + baseNote;
				return false;
			}
			if (!TryReadByte(0x0491, out int cellsRaw, out _))
			{
				cellsRaw = -1;
			}
			int cells = (cellsRaw > 0) ? ((cellsRaw >> 4) & 0x0F) : -1;
			log.AppendLine("基础电压=" + baseVoltage + "mV · cells=" + ((cells > 0) ? cells.ToString() : "?"));
			if (baseVoltage != ExpectedBaseVoltageMv)
			{
				detail = "基础电压 " + baseVoltage + "mV ≠ 已验证的 " + ExpectedBaseVoltageMv + "mV ⇒ 拒绝写入";
				return false;
			}
			if (cells > 0 && cells != 4)
			{
				detail = "EC 报告 " + cells + "S ≠ 4S ⇒ 拒绝写入";
				return false;
			}
			int origDesiredV = 0;
			int origQueuedV = 0;
			int origDesiredI = 0;
			int origQueuedI = 0;
			int origGate = 0;
			bool okRead = TryReadLe16(AddrDesiredVoltage, out origDesiredV, out _)
				&& TryReadLe16(AddrQueuedVoltage, out origQueuedV, out _)
				&& TryReadLe16(AddrDesiredCurrent, out origDesiredI, out _)
				&& TryReadLe16(AddrQueuedCurrent, out origQueuedI, out _)
				&& TryReadByte(AddrStopGate, out origGate, out _);
			if (!okRead)
			{
				detail = "原值读取不完整 ⇒ 拒绝写入（避免无法恢复）";
				return false;
			}
			log.AppendLine("原值：desiredV=" + origDesiredV + " queuedV=" + origQueuedV
				+ " desiredI=" + origDesiredI + " queuedI=" + origQueuedI + " gate=" + origGate);
			Stopwatch watch = Stopwatch.StartNew();
			int ticks = 0;
			bool writeOk = true;
			try
			{
				while (watch.Elapsed.TotalSeconds < seconds)
				{
					writeOk &= TryWriteLe16(AddrDesiredVoltage, ExpectedBaseVoltageMv, out string w1);
					writeOk &= TryWriteLe16(AddrQueuedVoltage, ExpectedBaseVoltageMv, out string w2);
					writeOk &= TryWriteLe16(AddrDesiredCurrent, ChargeCurrentMa, out string w3);
					writeOk &= TryWriteLe16(AddrQueuedCurrent, ChargeCurrentMa, out string w4);
					writeOk &= TryWriteByte(AddrStopGate, 0, out string w5);
					ticks++;
					if (!writeOk)
					{
						log.AppendLine("写入失败，最后一次：" + w1 + " / " + w2 + " / " + w3 + " / " + w4 + " / " + w5);
						break;
					}
					System.Threading.Thread.Sleep(20);
				}
			}
			finally
			{
				TryWriteLe16(AddrDesiredVoltage, origDesiredV, out _);
				TryWriteLe16(AddrQueuedVoltage, origQueuedV, out _);
				TryWriteLe16(AddrDesiredCurrent, origDesiredI, out _);
				TryWriteLe16(AddrQueuedCurrent, origQueuedI, out _);
				TryWriteByte(AddrStopGate, (byte)origGate, out _);
				log.AppendLine("已恢复原值（desiredV=" + origDesiredV + " / queuedV=" + origQueuedV
					+ " / gate=" + origGate + "）");
			}
			log.AppendLine("写入轮次=" + ticks + " · 全部写入成功=" + writeOk);
			if (TryReadLe16(AddrDesiredVoltage, out int afterV, out _))
			{
				log.AppendLine("恢复后 desired_voltage=" + afterV + "mV");
			}
			detail = log.ToString().TrimEnd();
			return writeOk && ticks > 0;
		}
		catch (Exception ex)
		{
			detail = "异常：" + ex.GetType().Name + " · " + ex.Message + "\n" + log;
			return false;
		}
	}
}
