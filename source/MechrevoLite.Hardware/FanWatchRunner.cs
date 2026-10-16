// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · "风扇"前置步骤）：
// 诊断开关 `--fan-watch` —— ⭐ **只读**地盯住宝龙达功能号 13 的两个风扇通道，
// 同时打印 CPU/GPU 温度 ⇒ ⭐ **用来确认"哪个通道是 CPU、哪个是 GPU"** ✅
//
// ⭐⭐ 为什么必须先做这一步（⭐ 用户 2026-10-16：「⭐ 做」）：
//    ⚠️ 宝龙达功能号 13 返回**两个** 16 位转速值：⭐ `OutData[4..5]` 与 ⭐ `[6..7]` ⚠️
//    ⚠️ 参考实现（蛟龙16Pro `JiaolongControl`）说：⭐ 第一个 = **GPU 扇** · 第二个 = **CPU 扇** ⚠️
//    ⚠️ 但 ⭐ 真机两组读数**太接近**（⭐ 3054 / 3098 ✅）⭐ **分不出来** ❌
//    ⇒ ⭐ ⚠️ 通道搞反的后果：⭐ **CPU 风扇会按 GPU 温度转** ⚠️ ⇒ ⭐ 散热逻辑完全错 ❌
//    ⇒ ⭐ ⭐ 所以：⭐ **先在负载下观察相关性**，⭐ 确认了再动"手动控速" ✅
//
// ⭐ 用法：⭐ `L-Mechrevo.exe --fan-watch [秒数] [输出目录]`
//    ⭐ 默认 60 秒；⭐ 给了目录就写 `风扇通道观察.txt` ✅
// ⭐⭐ 怎么用（⭐ 关键）：
//    ⭐ ① ⭐ 先跑这个工具（⭐ 它会每秒打一行 ✅）
//    ⭐ ② ⭐ **只让 CPU 热**（⭐ 例如跑 CPU 烤机/编译/视频转码 ✅）⇒ ⭐ 看**哪个通道先涨** ✅
//    ⭐ ③ ⭐ **只让 GPU 热**（⭐ 例如跑 3D 游戏/GPU 烤机 ✅）⇒ ⭐ 看**另一个通道**涨 ✅
//    ⭐ ④ ⭐ 把日志发回 ⇒ ⭐ 归属就定了 ✅
// ⚠️ 全程**只读**（⭐ 只用 Get=250 ✅），⛔ 一个 Set 都不发、⛔ 不改风扇/不改任何设置 ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace MechrevoLite.Hardware;

/// <summary>⭐ `--fan-watch` 的实现：⭐ 只读观察宝龙达两个风扇通道 ＋ ⭐ 同步 CPU/GPU 温度。</summary>
internal static class FanWatchRunner
{
	/// <summary>
	/// ⭐ 跑一次观察。⭐ <paramref name="seconds" /> 观察时长（⭐ 默认 60 ✅）；
	/// ⭐ <paramref name="outputDirectory" /> 非空则写 `风扇通道观察.txt` ✅。
	/// </summary>
	internal static int Run(int seconds, string outputDirectory)
	{
		if (seconds <= 0)
		{
			seconds = 60;
		}
		if (seconds > 1800)
		{
			seconds = 1800; // ⭐ 上限 30 分钟，⛔ 免得跑飞 ✅
		}
		List<string> lines = new List<string>();
		void W(string s)
		{
			lines.Add(s);
			Console.WriteLine(s);
		}

		W("=== 宝龙达风扇通道观察（⭐ 只读）===");
		W("开始时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   时长: " + seconds + " 秒");
		W("");
		W("⭐⭐ 怎么判读（⭐ 请照做）：");
		W("   ① 先让它空转几秒（记下基线）");
		W("   ② **只让 CPU 热**（烤机/编译/转码）⇒ 看**哪个通道先涨**");
		W("   ③ **只让 GPU 热**（3D 游戏/GPU 烤机）⇒ 看**另一个通道**涨");
		W("   ④ 把这份日志发回 ⇒ 通道归属就确定了");
		W("⚠️ 全程只读：⛔ 不发任何写命令、⛔ 不改风扇与任何设置");
		W("");

		BaolongdaWmi wmi = null;
		try
		{
			wmi = BaolongdaWmi.TryCreate();
		}
		catch (Exception ex)
		{
			W("❌ 打开宝龙达接口失败：" + ex.Message);
		}
		if (wmi == null)
		{
			W("⚠️ 这台机**没有**可用的宝龙达接口（⭐ `root\\WMI:MICommonInterface` 探不到）");
			W("   ⇒ ⭐ 本工具只在宝龙达机型（⭐ 蛟龙16Pro 等）上有意义 ✅");
			WriteFile(outputDirectory, lines);
			return 1;
		}
		W("✅ 已连上宝龙达接口：⭐ " + wmi.InstancePath + "（⭐ 风扇=" + wmi.FanCount + " 个）");
		W("");

		// ⭐ LHM：⭐ 只用来给 CPU/GPU 温度做对照（⭐ 读不到也不影响本工具 ✅）
		Computer computer = null;
		try
		{
			computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = false };
			computer.Open();
		}
		catch (Exception ex2)
		{
			W("⚠️ LHM 打开失败（⭐ 温度列会空着，⭐ 不影响风扇读数）：" + ex2.Message);
		}

		W("  时间      | ⭐通道0(大/GPU?) | ⭐通道1(小/CPU?) | ⭐CPU温度 | ⭐GPU温度 | ⭐LHM-CPU | ⭐LHM-GPU | ⭐手动态");
		W("  ----------|-----------------|-----------------|----------|----------|----------|----------|--------");
		try
		{
			DateTime end = DateTime.Now.AddSeconds(seconds);
			int tick = 0;
			while (DateTime.Now < end)
			{
				tick++;
				int ch0 = -1;
				int ch1 = -1;
				bool fanOk = false;
				try
				{
					fanOk = wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds);
					if (fanOk)
					{
						ch0 = speeds.First;
						ch1 = speeds.Second;
					}
				}
				catch (Exception ex3)
				{
					W("  ⚠️ 读风扇失败：" + ex3.Message);
				}
				int cpuTemp = -1;
				bool manual = false;
				try
				{
					if (wmi.TryReadCpuTemperature(out int c))
					{
						cpuTemp = c;
					}
					if (wmi.TryReadMaxFanSpeedSwitch(out bool on))
					{
						manual = on;
					}
				}
				catch
				{
				}
				// ⭐ LHM 温度（⭐ 对照用 ✅）
				double lhmCpu = double.NaN;
				double lhmGpu = double.NaN;
				if (computer != null)
				{
					try
					{
						foreach (IHardware hardware in computer.Hardware)
						{
							hardware.Update();
							foreach (ISensor sensor in hardware.Sensors)
							{
								if (!sensor.Value.HasValue || sensor.SensorType != SensorType.Temperature)
								{
									continue;
								}
								if (hardware.HardwareType == HardwareType.Cpu
									&& double.IsNaN(lhmCpu) && LhmMonitor.IsCpuTemperatureSensorName(sensor.Name))
								{
									lhmCpu = sensor.Value.Value;
								}
								else if (LhmMonitor.IsGpuTemperatureSensorName(sensor.Name)
									&& LhmMonitor.IsGpuHardwareType(hardware.HardwareType)
									&& double.IsNaN(lhmGpu))
								{
									lhmGpu = sensor.Value.Value;
								}
							}
						}
					}
					catch
					{
					}
				}
				W(string.Format("  {0:HH:mm:ss}  | {1,15} | {2,15} | {3,8} | {4,8} | {5,8} | {6,8} | {7,6}",
					DateTime.Now,
					fanOk ? (ch0 + " RPM") : "读不到",
					fanOk ? (ch1 + " RPM") : "读不到",
					cpuTemp >= 0 ? (cpuTemp + "℃") : "—",
					"—",
					double.IsNaN(lhmCpu) ? "—" : lhmCpu.ToString("F0") + "℃",
					double.IsNaN(lhmGpu) ? "—" : lhmGpu.ToString("F0") + "℃",
					manual ? "开" : "关"));
				Thread.Sleep(1000);
				_ = tick;
			}
		}
		finally
		{
			try
			{
				computer?.Close();
			}
			catch
			{
			}
			try
			{
				wmi.Dispose();
			}
			catch
			{
			}
		}
		W("");
		W("=== 判读提示 ===");
		W("⭐ 只让 **CPU** 热时**先涨**的那个通道 ⭐ = **CPU 扇** ✅");
		W("⭐ 只让 **GPU** 热时**先涨**的那个通道 ⭐ = **GPU 扇** ✅");
		W("⚠️ 若两个通道**始终同涨同落** ⇒ ⭐ 说明这台的 EC 不做分工（⭐ 那就按参考实现即可 ✅）");
		W("⭐ 请把这份日志发回 ⇒ ⭐ 我们据此定下通道映射，⛔ 不靠猜 ✅");
		WriteFile(outputDirectory, lines);
		return 0;
	}

	private static void WriteFile(string outputDirectory, List<string> lines)
	{
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(outputDirectory);
			string path = Path.Combine(outputDirectory, "风扇通道观察.txt");
			File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Console.WriteLine();
			Console.WriteLine("⭐ 已写出：" + path);
		}
		catch (Exception ex)
		{
			Console.WriteLine("⚠️ 写文件失败：" + ex.Message);
		}
	}
}
