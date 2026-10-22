using System;
using System.Collections.Generic;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（机主：AIDA64 充电电流一会有一会没有）：诊断包内做 100ms 级只读采样，
/// 抓 5 秒内的 pack 电压 / 电池电流 / desired / gate 波形，判断是显示采样问题还是真实充放交替。
/// 只读，不写任何寄存器。
/// </summary>
internal static class BatteryChargeWaveformProbe
{
	internal static string Build(int durationMs = 5000, int intervalMs = 100)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[无界电池充电波形（100ms 只读采样）]");
		if (!I2ecInpOut.IsAvailable)
		{
			sb.AppendLine("内置 I2EC 不可用：" + I2ecInpOut.InitDetail);
			return sb.ToString();
		}
		try
		{
			int samples = Math.Max(2, Math.Min(200, durationMs / Math.Max(20, intervalMs)));
			List<int> currents = new List<int>(samples);
			List<int> packs = new List<int>(samples);
			int signChanges = 0;
			int zeroCount = 0;
			int lastSign = 0;
			int minCurrent = int.MaxValue;
			int maxCurrent = int.MinValue;
			long sumCurrent = 0L;
			int desiredMv = -1;
			int queuedMv = -1;
			int gate = -1;
			sb.AppendLine("采样：间隔 " + intervalMs + "ms × " + samples + " 点（约 " + (samples * intervalMs / 1000.0).ToString("0.0") + "s）");
			sb.AppendLine("序号,时间偏移ms,desiredMv,queuedMv,packMv,currentMa,gate");
			for (int i = 0; i < samples; i++)
			{
				int d = ReadLe16(AddrDesiredVoltage);
				int q = ReadLe16(AddrQueuedVoltage);
				int pack = ReadLe16(0x0438);
				int current = ReadLe16(0x050A);
				int g = ReadByte(0x0574);
				if ((current & 0x8000) != 0)
				{
					current -= 0x10000;
				}
				if (d >= 0)
				{
					desiredMv = d;
				}
				if (q >= 0)
				{
					queuedMv = q;
				}
				if (g >= 0)
				{
					gate = g;
				}
				if (current >= 0)
				{
					currents.Add(current);
					packs.Add(pack);
					minCurrent = Math.Min(minCurrent, current);
					maxCurrent = Math.Max(maxCurrent, current);
					sumCurrent += current;
					int sign = Math.Sign(current);
					if (sign == 0)
					{
						zeroCount++;
					}
					if (lastSign != 0 && sign != 0 && sign != lastSign)
					{
						signChanges++;
					}
					if (sign != 0)
					{
						lastSign = sign;
					}
				}
				sb.AppendLine(i + "," + (i * intervalMs) + "," + d + "," + q + "," + pack + "," + current + "," + g);
				System.Threading.Thread.Sleep(intervalMs);
			}
			sb.AppendLine();
			sb.AppendLine("摘要：desired=" + desiredMv + "mV · queued=" + queuedMv + "mV · gate=" + gate);
			if (currents.Count > 0)
			{
				sb.AppendLine("电流：min=" + minCurrent + "mA · max=" + maxCurrent + "mA · avg=" + (sumCurrent / currents.Count)
					+ "mA · 0值=" + zeroCount + " · 正负切换=" + signChanges + " 次");
				int minPack = int.MaxValue;
				int maxPack = int.MinValue;
				foreach (int pack in packs)
				{
					if (pack > 0)
					{
						minPack = Math.Min(minPack, pack);
						maxPack = Math.Max(maxPack, pack);
					}
				}
				sb.AppendLine("pack：min=" + (minPack == int.MaxValue ? -1 : minPack) + "mV · max=" + (maxPack == int.MinValue ? -1 : maxPack) + "mV");
			}
			sb.AppendLine("判读：持续为正=在充电；频繁正负切换/0值=EC 在脉冲或保护；desired 掉回 16600=被回写。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("采样异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}
		return sb.ToString();
	}

	private const int AddrDesiredVoltage = 0x0522;

	private const int AddrQueuedVoltage = 0x0836;

	private static int ReadByte(int address)
	{
		return I2ecInpOut.TryReadEcByte(address, out int value, out _) ? value : -1;
	}

	private static int ReadLe16(int address)
	{
		int low = ReadByte(address);
		int high = ReadByte(address + 1);
		return (low < 0 || high < 0) ? -1 : ((high << 8) | low);
	}
}
