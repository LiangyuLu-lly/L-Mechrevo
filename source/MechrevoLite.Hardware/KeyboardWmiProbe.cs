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
			// ⭐⭐ 2026-10-19（机主实测 30 系）：原顺序 mode→color→brightness 下，18 回读恒 0（灯不亮）
			//   ⇒ 改成原厂顺序：**先亮度 → 再模式 → 再颜色** ✅
			bool w0 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardBrightness, 3);
			Thread.Sleep(400);
			L("写 18=3 (ok=" + w0 + ") => 回读 18=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness));
			bool w1 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 2);
			Thread.Sleep(500);
			L("写 16=2 (ok=" + w1 + ") => 回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
			bool w2 = wmi.TryWriteColor(255, 0, 0);
			Thread.Sleep(500);
			L("写 17=Red (ok=" + w2 + ") => 回读 17=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardColor));
			// ⭐⭐ 2026-10-18（机主：彩虹无效）⇒ 追加「彩虹专段」：
			L("");
			L("── 彩虹专段（模式 1 = AutoCyclic · 只写 16，不写颜色）──");
			bool r1 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 1);
			L("写 16=1 (ok=" + r1 + ") => 立刻回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
			Thread.Sleep(800);
			L("等 800ms => 回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode) + "（保持 1 说明固件接受彩虹）");
			bool rb = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardBrightness, 3);
			Thread.Sleep(400);
			L("写 18=3 (ok=" + rb + ") => 回读 18=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness)
				+ " · 回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode) + "（变成 2 说明亮度写把彩虹顶掉）");
			bool r2 = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 1);
			Thread.Sleep(500);
			L("再次写 16=1 (ok=" + r2 + ") => 回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
			// ⭐⭐ 2026-10-18（机主：7.3 的彩虹有效、我们的探针无效）⇒「纯净彩虹测试」：
			//   只写 16=1，之后 3 秒内**不碰任何 17/18**（7.3 也只写这一个字节），每秒回读一次
			L("");
			L("── 纯净彩虹测试（只写 16=1，不碰 17/18，每秒回读）──");
			for (int k = 0; k < 3; k++)
			{
				bool okPure = wmi.TryWriteByte(BaolongdaWmi.Function.RgbKeyboardMode, 1);
				Thread.Sleep(1000);
				L("第 " + (k + 1) + " 次写 16=1 (ok=" + okPure + ") ⇒ 1s 后回读 16=" + R(wmi, BaolongdaWmi.Function.RgbKeyboardMode));
			}
			L("⭐ 请目视：这 3 秒里键盘有没有变彩虹？（⭐ 若有，记下是哪一次写的之后变的）");
			L("⭐ 请目视：键盘出现彩虹渐变/呼吸了吗？（把最终 16 值 + 目视结果一起发回）");
			L("请目视：键盘灯变红了吗？亮度变了吗？");
		}
		catch (Exception ex)
		{
			L("探针异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return path;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（30 系「亮度只有 FN 能调」只读诊断）：
	/// `--kbd-fn-probe [秒]` —— 周期**只读**采样 16/17/18 的完整 OutData，raw 变化才记一行 ✅。
	/// <para>⚠️ 全程只发 Get；⛔ 不写任何字节。机主在此期间按 Fn 调亮/调暗，看哪个字节跟着变 ✅。</para>
	/// </summary>
	internal static string RunFnProbe(int seconds)
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-kbd-fn-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-kbd-fn-probe 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
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
			L("⭐ **只读**探针：接下来 " + seconds + " 秒内，请**按 Fn 调亮/调暗各 2~3 次**（不要动 LM 界面）。");
			L("⭐ 每 200ms 采样一次 16/17/18 的完整 OutData，只有 raw 变化才记一行。");
			DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
			string last = "";
			int changes = 0;
			while (DateTime.UtcNow < deadline)
			{
				string line = "16=" + Raw(wmi, BaolongdaWmi.Function.RgbKeyboardMode)
					+ " · 17=" + Raw(wmi, BaolongdaWmi.Function.RgbKeyboardColor)
					+ " · 18=" + Raw(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness);
				if (line != last)
				{
					last = line;
					changes++;
					L("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + line);
				}
				Thread.Sleep(200);
			}
			L("采样结束：共记录 " + changes + " 次变化（含初始值）。");
			L("⭐ 判读：哪一行在你按 Fn 时变了？若变化出现在 18 raw 的**某个字节** ⇒ 那个字节才是亮度真实偏移；");
			L("   若只有 16/17 变或完全没有字节变 ⇒ 说明 FN 走的是另一条命令/EC，不是这条 WMI Get 能看到的。");
		}
		catch (Exception ex)
		{
			L("fn-probe 异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return path;
	}

	/// <summary>⭐ 只读：读一个功能号的完整 OutData 摘要（下标 2..8，含长度）✅。</summary>
	private static string Raw(BaolongdaWmi wmi, BaolongdaWmi.Function function)
	{
		if (!wmi.TryReadRawOutData(function, out byte[] data) || data == null)
		{
			return "fail";
		}
		int end = Math.Min(data.Length - 1, 8);
		StringBuilder text = new StringBuilder();
		text.Append("raw[2..").Append(end).Append("]=");
		for (int i = 2; i <= end; i++)
		{
			if (i > 2)
			{
				text.Append('-');
			}
			text.Append(data[i].ToString("X2"));
		}
		text.Append("(len=").Append(data.Length).Append(')');
		return text.ToString();
	}

	/// <summary>⭐ 旧探针用：只取单字节 <c>OutData[4]</c> ✅。</summary>
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
