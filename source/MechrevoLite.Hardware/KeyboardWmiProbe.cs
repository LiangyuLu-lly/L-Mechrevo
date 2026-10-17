using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 2026-10-19（机主：「直接调 LM 接口」）：⭐ 键盘灯 WMI 探针 —— **用主程序自己的代码通道**，
/// 与 <see cref="BaolongdaWmi" /> 共用同一套 Invoke/帧格式 ⇒ ⛔ 不再受 PowerShell 封送问题影响 ✅
/// <para>⭐ 只读 + 单次写（16=2 / 17=红 / 18=1），每步回读，结果写日志与桌面文件 ✅</para>
/// </summary>
internal static class KeyboardWmiProbe
{
	internal static string Run()
	{
		StringBuilder sb = new StringBuilder();
		using BaolongdaWmi wmi = BaolongdaWmi.TryCreate();
		if (wmi == null)
		{
			sb.AppendLine("❌ 没有 MICommonInterface 实例（本机不是宝龙达？）");
			return Flush(sb);
		}
		sb.AppendLine("实例: " + wmi.InstancePath);
		sb.AppendLine("（只读）10=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardStatus)
			+ " 16=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardMode)
			+ " 17=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardColor)
			+ " 18=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness));
		// 写：模式=2（固定色）→ 红 → 亮度 1
		wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 2);
		Thread.Sleep(500);
		sb.AppendLine("写 16=2 ⇒ 回读 16=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
		wmi.TryWriteColor(255, 0, 0);
		Thread.Sleep(500);
		sb.AppendLine("写 17=红 ⇒ 回读 17=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardColor));
		wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardBrightness, 1);
		Thread.Sleep(400);
		sb.AppendLine("写 18=1 ⇒ 回读 18=" + Read(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness));
		sb.AppendLine("👀 请目视：键盘灯变红了吗？亮度变了吗？");
		return Flush(sb);
	}

	private static string Read(BaolongdaWmi wmi, BaolongdaWmi.Function f)
	{
		return wmi.TryReadByte(f, out byte v) ? v.ToString() : "失败";
	}

	private static string Flush(StringBuilder sb)
	{
		string text = sb.ToString();
		try
		{
			string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"lm-kbd-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
			File.WriteAllText(desktop, text, Encoding.UTF8);
			return text + "\n📄 已另存: " + desktop;
		}
		catch (Exception ex)
		{
			return text + "\n⚠️ 另存失败: " + ex.Message;
		}
	}
}
