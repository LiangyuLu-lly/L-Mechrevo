using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界风扇表会错乱，怎么知道 EC 的温度取值）：
/// 只读采样器 —— 不写任何 EC 寄存器，按秒记录 EC 自己控制风扇时的
/// 温度 / 占空比 / 转速，用来反推 EC 内部温度→占空比策略曲线。
///
/// <para>温度寄存器：CPU 0x043E、GPU 0x044F（APU 可能为 0）、电池 0x04A2（0.1K）；
/// 占空比 0x1804/0x1809，转速 0x0464/0x046C（大端）。</para>
/// </summary>
internal static class WujieFanMapProbe
{
	private const int RegisterCpuTemp = 0x043E;

	private const int RegisterGpuTemp = 0x044F;

	private const int RegisterBatteryTemp = 0x04A2;

	private const int RegisterDutyFan1 = 0x1804;

	private const int RegisterDutyFan2 = 0x1809;

	private const int RegisterFan1Rpm = 0x0464;

	private const int RegisterFan2Rpm = 0x046C;

	private const int RegisterMode = 0x200D;

	/// <summary>采样指定秒数，写 CSV；返回路径与摘要。</summary>
	internal static string Run(int seconds, string outputPath)
	{
		string model = MechrevoDeviceCapabilities.Current.Model ?? "";
		bool wujie = WujieFanEc.IsWujiePlatform()
			|| model.IndexOf("WUJIE", StringComparison.OrdinalIgnoreCase) >= 0
			|| model.IndexOf("无界", StringComparison.OrdinalIgnoreCase) >= 0;
		if (!wujie)
		{
			return "非无界机型：不采样（未写任何寄存器 · model=" + model + "）";
		}
		if (seconds <= 0)
		{
			seconds = 60;
		}
		string path = string.IsNullOrWhiteSpace(outputPath)
			? Path.Combine(Path.GetTempPath(), "wujie-fan-map-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv")
			: Path.GetFullPath(outputPath);
		StringBuilder log = new StringBuilder();
		log.AppendLine("[wujie-fan-map] 只读采样（不写 EC）");
		log.AppendLine("输出：" + path);
		log.AppendLine("提示：先把「风扇曲线」关掉（交还 EC 自动），分别在待机/单烤/双烤下各采样一次，便于对照。");
		try
		{
			using StreamWriter writer = new StreamWriter(path, append: false, Encoding.UTF8);
			writer.WriteLine("time,tempCpuC,tempGpuC,tempBatteryC,duty1Raw,duty2Raw,rpm1,rpm2,mode0x200D");
			int count = 0;
			for (int i = 0; i < seconds; i++)
			{
				int tempCpu = Read(RegisterCpuTemp);
				int tempGpu = Read(RegisterGpuTemp);
				int tempBatRaw = Read(RegisterBatteryTemp);
				int duty1 = Read(RegisterDutyFan1);
				int duty2 = Read(RegisterDutyFan2);
				int rpm1 = ReadWord(RegisterFan1Rpm);
				int rpm2 = ReadWord(RegisterFan2Rpm);
				int mode = Read(RegisterMode);
				double tempBatC = (tempBatRaw > 0 && tempBatRaw < 65535) ? (tempBatRaw / 10.0 - 273.15) : -1;
				writer.WriteLine($"{i},{tempCpu},{tempGpu},{tempBatC:F1},{duty1},{duty2},{rpm1},{rpm2},{mode}");
				writer.Flush();
				count++;
				if (i == 0 || (i + 1) % 10 == 0)
				{
					Console.WriteLine($"[{i + 1}/{seconds}] CPU={tempCpu}℃ GPU={tempGpu}℃ duty={duty1}/{duty2} rpm={rpm1}/{rpm2}");
				}
				Thread.Sleep(1000);
			}
			log.AppendLine($"完成：{count} 个样本");
			log.AppendLine("判读：把 tempCpuC 与 duty1Raw 画出来，就能看到 EC 自己的温度→占空比策略（含回滞/档位跳变）。");
		}
		catch (Exception ex)
		{
			log.AppendLine("采样异常：" + ex.GetType().Name + " " + ex.Message);
		}
		return log.ToString().TrimEnd();
	}

	private static int Read(int address)
	{
		if (I2ecInpOut.IsAvailable && I2ecInpOut.TryReadEcByte(address, out int value, out _))
		{
			return value;
		}
		return EcPortIo.TryReadByte(address, out int acpiValue) ? acpiValue : -1;
	}

	private static int ReadWord(int address)
	{
		int high = Read(address);
		int low = Read(address + 1);
		return (high < 0 || low < 0) ? -1 : ((high & 0xFF) << 8) | (low & 0xFF);
	}
}
