using System;
using System.IO;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite;

/// <summary>
/// 内置「电池充电电压修复」探针：
/// <list type="bullet">
/// <item><c>--battery-voltage-probe</c>：只读探测 I2EC 通道与关键电池寄存器，结果写桌面报告。</item>
/// <item><c>--battery-voltage-test [--seconds N]</c>：写入测试（17600mV/2816mA + 解停充门），结束后恢复原值。</item>
/// </list>
/// </summary>
internal static class BatteryVoltageProbe
{
	internal static int RunReadOnly()
	{
		string report = I2ecBattery.Probe();
		string path = WriteReport("lm-battery-voltage-probe", report);
		report += "\n报告文件：" + path;
		Console.WriteLine(report);
		Logger.WriteLine("[battery-fix] I2EC 只读探针完成，报告=" + path);
		Logger.Close();
		return 0;
	}

	internal static int RunWriteTest(int seconds)
	{
		bool ok = I2ecBattery.TryHoldFull(seconds, out string detail);
		string report = "=== 电池 EC 写入测试（17600mV/2816mA，结束后恢复原值）===" + Environment.NewLine
			+ "结果：" + (ok ? "成功" : "失败") + Environment.NewLine
			+ "时长：" + seconds + "s" + Environment.NewLine
			+ detail + Environment.NewLine;
		string path = WriteReport("lm-battery-voltage-test", report);
		report += Environment.NewLine + "报告文件：" + path;
		Console.WriteLine(report);
		Logger.WriteLine("[battery-fix] I2EC 写入测试 " + (ok ? "成功" : "失败") + "，报告=" + path);
		Logger.Close();
		return ok ? 0 : 2;
	}

	private static string WriteReport(string prefix, string text)
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
			File.WriteAllText(path, text, Encoding.UTF8);
			return path;
		}
		catch
		{
			return "(写桌面失败)";
		}
	}
}
