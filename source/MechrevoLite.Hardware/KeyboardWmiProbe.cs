using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 键盘灯 WMI 探针（用主程序自己的 BaolongdaWmi 通道）。
/// 2026-10-19 修正：先建桌面文件写表头、再逐行 Append ⇒ 即使中途异常也留得下内容。
/// </summary>
internal static class KeyboardWmiProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-kbd-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-kbd-probe 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[kbd-probe] " + s); } catch { }
		}
		try
		{
			using BaolongdaWmi wmi = BaolongdaWmi.TryCreate();
			if (wmi == null)
			{
				L("没有 MICommonInterface 实例（本机不是宝龙达？）");
				return path;
			}
			L("实例: " + wmi.InstancePath);
			L("(只读) 10=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardStatus)
				+ " 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode)
				+ " 17=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardColor)
				+ " 18=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness));
			bool w1 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 2);
			Thread.Sleep(500);
			L("写 16=2 (ok=" + w1 + ") => 回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
			bool w2 = wmi.TryWriteColor(255, 0, 0);
			Thread.Sleep(500);
			L("写 17=Red (ok=" + w2 + ") => 回读 17=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardColor));
			bool w3 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardBrightness, 1);
			Thread.Sleep(400);
			L("写 18=1 (ok=" + w3 + ") => 回读 18=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness));
			L("请目视：键盘灯变红了吗？亮度变了吗？");
		}
		catch (Exception ex)
		{
			L("探针异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return path;
	}

	private static string R(BaolongdaWmi wmi, BaolongdaWmi.Function f)
	{
		try
		{
			return wmi.TryReadByte(f, out byte v) ? v.ToString() : "fail";
		}
		catch (Exception ex)
		{
			return "ex:" + ex.GetType().Name;
		}
	}
}
