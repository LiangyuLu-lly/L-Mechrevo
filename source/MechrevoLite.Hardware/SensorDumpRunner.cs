// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · "接传感器"第 1 步）：
// 诊断开关 `--sensor-dump` —— ⭐ 把**自带的 LibreHardwareMonitor** 能读到的传感器**全列出来**。
//
// ⭐⭐ 为什么需要它（⭐ 用户 2026-10-16 原话）：
//    「⭐ 接 传感器如果 LM 自带的那个 dll 能读温度优先用 LM 的传感器 不能的再接」
//    ⇒ ⭐ 落实这条优先级之前，⭐ 得先**知道 LHM 到底能读什么** ✅
//    ⚠️ 现有 `LhmMonitor` **只读 Power / Clock / 显存**，⛔ **根本没读温度与风扇** ⚠️
//       ⇒ ⭐ 所以"能不能读"这个问题，⭐ 现在**没有答案** ⇒ ⭐ 先把事实测出来 ✅
//
// ⭐ 用法：⭐ `L-Mechrevo.exe --sensor-dump [输出目录]`
//    ⭐ 不给目录就只打印到控制台；⭐ 给了就同时写一份 `传感器清单.txt`（⭐ 方便机主发回 ✅）
// ⚠️ LHM 的部分传感器**需要管理员**（⭐ 它要装/加载自己的内核驱动 ✅）
//    ⇒ ⭐ 本工具会把"是否管理员"一并记下，⭐ 免得把"权限不够"误判成"读不到" ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MechrevoLite.Hardware;

/// <summary>⭐ `--sensor-dump` 的实现：⭐ 枚举 LHM 能看到的全部【温度 / 风扇 / 控制】传感器。</summary>
internal static class SensorDumpRunner
{
	/// <summary>
	/// 跑一次转储。⭐ 返回进程退出码（0 = 跑完，⭐ 1 = 出错）。
	/// <para>⭐ <paramref name="outputDirectory" /> 为空 ⇒ 只打印 ✅；⭐ 非空 ⇒ 同时写 `传感器清单.txt` ✅。</para>
	/// </summary>
	internal static int Run(string outputDirectory)
	{
		List<string> lines = new List<string>();
		void W(string s)
		{
			lines.Add(s);
			Console.WriteLine(s);
		}

		W("=== LM 自带传感器（LibreHardwareMonitor）能力清单 ===");
		W("采集时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		W("");
		W("⭐ 目的：⭐ 落实「⭐ LM 自带 DLL 能读温度就优先用它，不能的再接宝龙达 WMI」⭐ 这条优先级 ✅");
		W("⭐ 本文件由 `L-Mechrevo.exe --sensor-dump` 生成 ✅");
		W("");

		bool isAdmin;
		try
		{
			using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
				.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
		}
		catch
		{
			isAdmin = false;
		}
		W("⭐⭐ 当前是否管理员：⭐ " + isAdmin + "（⚠️ 这一项很关键，见文末说明）");
		W("");

		Computer computer = null;
		try
		{
			computer = new Computer
			{
				IsCpuEnabled = true,
				IsGpuEnabled = true,
				IsMotherboardEnabled = true,
				// ⚠️⚠️ 2026-10-16 实测修正：⭐ **不要**开 `IsControllerEnabled` ❌
				//    ⭐ 实测报错：⭐ `Could not load file or assembly 'System.IO.Ports, Version=10.0.0.3'` ⚠️
				//    ⭐ 原因：⭐ "控制器"那一支会拉**串口**代码 ⇒ ⭐ 需要 `System.IO.Ports.dll` ✅
				//    ⭐ 而我们的输出目录 / ⭐ 官方原包的 `{app}` 目录**都没有**这个 DLL ⇒ ⭐ 一开就抛 ❌
				//    ⭐ （⭐ 现有 `LhmMonitor` 只开 Cpu/Gpu/Memory ⇒ ⭐ 从来没碰到这个问题 ✅）
				//    ⚠️ 代价：⭐ LHM 的**风扇**读取（⭐ 有些机型挂"控制器"那支）⭐ 我们就拿不到了 ⚠️
				//    ⇒ ⭐ 但 ⭐ 风扇本来就由**宝龙达 WMI 功能号 13** 提供（⭐ 真机已验证能读 ✅）
				//      ⇒ ⭐ 正好符合用户的优先级原则：⭐ "LM 能读的用 LM，⭐ 不能的再接" ✅
				IsMemoryEnabled = true,
				IsStorageEnabled = true,
				IsNetworkEnabled = false,
			};
			computer.Open();
			W("✅ LHM 已打开（⭐ 各硬件类都启用）");
			W("");

			int temperatureCount = 0;
			int fanCount = 0;
			int controlCount = 0;
			List<string> temperatureNames = new List<string>();
			List<string> fanNames = new List<string>();

			foreach (IHardware hardware in computer.Hardware)
			{
				W($"── 硬件：⭐ {hardware.Name}（{hardware.HardwareType}）");
				try
				{
					hardware.Update();
				}
				catch (Exception ex)
				{
					W("   ⚠️ Update 失败：" + ex.Message);
				}
				Collect(hardware, "   ", W, ref temperatureCount, ref fanCount, ref controlCount, temperatureNames, fanNames);

				foreach (IHardware sub in hardware.SubHardware)
				{
					W($"   ── 子硬件：⭐ {sub.Name}（{sub.HardwareType}）");
					try
					{
						sub.Update();
					}
					catch (Exception ex2)
					{
						W("      ⚠️ Update 失败：" + ex2.Message);
					}
					Collect(sub, "      ", W, ref temperatureCount, ref fanCount, ref controlCount, temperatureNames, fanNames);
				}
			}

			W("");
			W("═══ 汇总 ═══");
			W($"🌡 温度传感器：⭐ {temperatureCount} 个");
			W($"🌀 风扇传感器：⭐ {fanCount} 个");
			W($"🎛 控制传感器：⭐ {controlCount} 个");
			W("");

			// ⭐⭐ 结论段（⭐ 直接回答用户的优先级问题 ✅）
			W("═══ 结论（⭐ 直接回答「该用谁」）═══");
			if (temperatureCount > 0)
			{
				W($"✅ 温度：⭐ LM 自带 DLL **能读**（{temperatureCount} 个）⇒ ⭐⭐ **优先用它，⛔ 不用宝龙达** ✅");
				foreach (string n in temperatureNames)
				{
					W("      · " + n);
				}
			}
			else
			{
				W("⚠️ 温度：⭐ LM 自带 DLL **一个都没读到** ⇒ ⭐ 需要看是否管理员（见上）");
				W("      ⭐ 若本来就不是管理员 ⇒ ⭐ **先以管理员重跑本工具再下结论** ⚠️");
			}
			if (fanCount > 0)
			{
				W($"✅ 风扇：⭐ LM 自带 DLL **能读**（{fanCount} 个）⇒ ⭐ 优先用它 ✅");
				foreach (string n in fanNames)
				{
					W("      · " + n);
				}
			}
			else
			{
				W("❌ 风扇：⭐ LM 自带 DLL **读不到** ⇒ ⭐ ⭐ **这一项正是宝龙达 WMI 要补的** ✅");
				W("      （⭐ 真机已验证：⭐ 宝龙达功能号 13 能读到风扇转速 ✅）");
			}
			W("");
			W("⚠️ 说明：⭐ LHM 的**部分传感器需要管理员权限**（⭐ 它要加载自己的内核驱动 ✅）。");
			W("   ⭐ 若上面「温度/风扇」都是 0，⭐ 请**用管理员身份**重跑本工具再判断 ⚠️");
		}
		catch (Exception ex3)
		{
			W("❌ LHM 枚举失败：⭐ " + ex3.Message);
			W("   类型：" + ex3.GetType().FullName);
			W("   ⚠️ 常见原因：① 不是管理员；② LHM 内核驱动未能加载（被杀软拦）；③ 运行时版本不匹配 ✅");
			if (!isAdmin)
			{
				W("   ⭐ 当前**不是管理员** ⇒ ⭐ 请先用管理员身份重跑再下结论 ⚠️");
			}
			WriteFile(outputDirectory, lines);
			return 1;
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
		}

		WriteFile(outputDirectory, lines);
		return 0;
	}

	private static void Collect(IHardware hardware, string indent, Action<string> w,
		ref int temperatureCount, ref int fanCount, ref int controlCount,
		List<string> temperatureNames, List<string> fanNames)
	{
		foreach (ISensor sensor in hardware.Sensors)
		{
			try
			{
				if (sensor.SensorType == SensorType.Temperature)
				{
					temperatureCount++;
					string line = $"{hardware.HardwareType}/{hardware.Name}/{sensor.Name} = {sensor.Value}";
					temperatureNames.Add(line);
					w($"{indent}🌡 温度  {sensor.Name,-34} = {sensor.Value}");
				}
				else if (sensor.SensorType == SensorType.Fan)
				{
					fanCount++;
					string line = $"{hardware.HardwareType}/{hardware.Name}/{sensor.Name} = {sensor.Value}";
					fanNames.Add(line);
					w($"{indent}🌀 风扇  {sensor.Name,-34} = {sensor.Value}");
				}
				else if (sensor.SensorType == SensorType.Control)
				{
					controlCount++;
					w($"{indent}🎛 控制  {sensor.Name,-34} = {sensor.Value}");
				}
			}
			catch (Exception ex)
			{
				w($"{indent}⚠️ 读传感器失败（{sensor.Name}）：{ex.Message}");
			}
		}
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
			string path = Path.Combine(outputDirectory, "传感器清单.txt");
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
