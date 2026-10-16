// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · ⭐ 临时诊断段）：
// ⭐⭐ `DiagnosticBaolongdaInfo` —— ⭐ **把宝龙达的扫描放进诊断包** ✅
//
// ⭐ 用户 2026-10-16 原话：「⭐ 把宝龙达的扫临时写入诊断里面」✅
//
// ⭐⭐ 它做什么（⭐ 全部**只读** ✅ · ⛔ 一个 Set 都不发 ✅）：
//    ⭐ ① ⭐ **机型白名单取证**：⭐ 打印 `BaseBoardProduct` ＋ ⭐ 白名单判定结果 ✅
//       ⇒ ⭐ 机主跑一次诊断就能确认"⭐ 我这台到底认没认成宝龙达、⭐ 为什么" ✅
//    ⭐ ② ⭐ **功能号 8~23 逐个试读** ⇒ ⭐ 每个都打印「⭐ 可读（值 N）/ ⚠️ 读不到」✅
//       ⭐ 且 ⭐ 走**带回显校验**的读 ⇒ ⭐ 回显不符也如实标出 ✅
//    ⭐ ③ ⭐ **结论清单**：⭐ 哪些功能号通（⭐ 复用 `BaolongdaStateSnapshot.DescribeSupportList()` ✅）
//    ⭐ ④ ⭐ **`\\.\ACPIDriver` 只读探测**（⭐ 判断限充那条 EC 通道在不在 ✅）
//    ⭐ ⑤ ⭐ **HID 事件如实说明**（⭐ 我们**没实现** ✅ —— ⭐ ⛔ 不假装 ✅）
//
// ⚠️⚠️ **隐私**：⭐ 本段 ⛔ **不含**序列号 / ⭐ 用户名 / ⭐ 机器名 / ⭐ MAC ✅
//    ⭐ 只含 ⭐ 主板型号（⭐ 它已在诊断包里用于机型判定 ✅）
//
// ⚠️ 标记「⭐ 临时」：⭐ 用户说"临时写入"✅ ⇒ ⭐ 定位清楚后可按需撤掉 ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>⭐ 宝龙达临时诊断段（⭐ 见文件头注释 ✅）。</summary>
internal static class DiagnosticBaolongdaInfo
{
	/// <summary>⭐ 生成诊断文本（⭐ 只读 ✅ · ⭐ 全程 try/catch ⇒ ⭐ 出错不影响诊断包其余部分 ✅）。</summary>
	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("════════════════════════════════════════════════════════");
		sb.AppendLine("⭐ 宝龙达（蛟龙16Pro 等）诊断段（⭐ 临时 · ⭐ 全程只读 ⛔ 不下发任何命令）");
		sb.AppendLine("════════════════════════════════════════════════════════");
		try
		{
			AppendWhitelist(sb);
			AppendDriverProbe(sb);
			AppendFunctionProbe(sb);
			AppendHidNote(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ 宝龙达诊断段异常（已吞掉，不影响诊断包其余部分）：" + ex.Message);
		}
		return sb.ToString();
	}

	// ── ① 机型白名单取证（⭐ 让机主一次看清"认没认成宝龙达、为什么" ✅）──
	private static void AppendWhitelist(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ① 机型白名单（⭐ 判宝龙达**只看板号** ⛔ 不看是不是蛟龙、⛔ 不看 30/40 系）──");
		string board = ReadBaseBoardProduct();
		sb.AppendLine("主板型号 BaseBoardProduct = " + (board.Length == 0 ? "（读不到）" : board));
		bool hit = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string note);
		sb.AppendLine("白名单判定 = " + (hit ? "✅ 命中 ⇒ 本机可认作宝龙达" : "⛔ 未命中 ⇒ 本机按同方/GCU 处理"));
		sb.AppendLine("判定说明 = " + note);
		sb.AppendLine("⭐ 判据依据：⭐ 宝龙达为 `MRID6-23`（⭐ 真机证实）；⭐ 同方机型实测板号为 "
			+ "GM5HG0A / GM6IX8B / KUANGSHI-* / YAOSHI-* / Standard ⇒ ⭐ 均**不是** `MRID6-` 前缀 ✅");
		sb.AppendLine("⚠️ 已知不确定：⭐ 30 系蛟龙板号**未证实** ⚠️ ⇒ ⭐ 若它不是 `MRID6-*`，"
			+ "本机会按同方走 GCU（⭐ 保守无害 ✅）");
	}

	private static string ReadBaseBoardProduct()
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	// ── ② `\\.\ACPIDriver` 只读探测（⭐ 限充那条 EC 通道 ✅）──
	private static void AppendDriverProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ② `\\.\\ACPIDriver`（⭐ UWACPIDriver）只读探测 ──");
		sb.AppendLine("⭐ 用途：⭐ 限充（EC 通道）与机型码读取都靠它 ✅");
		// ⚠️ 这里只查**驱动文件在不在** ✅（⭐ 真正的通道探测由 `EcChargeLimit` 负责 ✅ ——
		//    ⭐ 本段刻意**不**去打开设备，⛔ 免得在诊断路径里引入副作用 ⚠️）
		string[] candidates = new string[3]
		{
			"\\\\?\\GLOBALROOT\\Device\\ACPIDriver",
			"C:\\Windows\\System32\\drivers\\UWACPIDriver.sys",
			"C:\\Program Files\\L-Mechrevo\\UWACPIDriver\\UWACPIDriver.sys"
		};
		foreach (string path in candidates)
		{
			bool exists = false;
			try
			{
				exists = File.Exists(path);
			}
			catch
			{
			}
			sb.AppendLine("  " + (exists ? "✅ 在" : "⚠️ 不在") + " ： " + path);
		}
		sb.AppendLine("⭐ 说明：⭐ 设备对象那条路径用 `File.Exists` 查不到是**正常**的 ✅"
			+ "（⭐ 它要走 `CreateFile` 打开 ✅）⇒ ⭐ 限充到底可不可用，"
			+ "⭐ 以设置页「限充」那一项**能不能点**、⭐ 以及运行日志里 `EcChargeLimit` 的探测行为为准 ✅");
	}

	// ── ③ 功能号 8~23 逐个试读 ──
	private static void AppendFunctionProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ③ 功能号 8~23 逐个试读（⭐ 只发 Get=250 ✅ · ⭐ 带 `OutData[3]` 回显校验 ✅）──");
		BaolongdaWmi wmi = ResolveWmi();
		if (wmi == null)
		{
			sb.AppendLine("⚠️ 当前**没有可用的宝龙达接口**（⭐ 本机不是宝龙达，⭐ 或后端没接上 ✅）");
			sb.AppendLine("   ⇒ ⭐ 本节无数据 ✅（⭐ 这是**正常**结果，⛔ 不是错误 ✅）");
			return;
		}
		sb.AppendLine("接口实例 = " + wmi.InstancePath);
		sb.AppendLine("风扇数 = " + wmi.FanCount + " · 模式档数 = " + wmi.ModeCount);
		sb.AppendLine();
		AppendOne(sb, wmi, BaolongdaWmi.Function.SystemPerMode, "8", "系统性能模式");
		AppendGpuMode(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardStatus, "10", "键盘灯状态");
		AppendOne(sb, wmi, BaolongdaWmi.Function.FnLock, "11", "Fn 锁");
		AppendOne(sb, wmi, BaolongdaWmi.Function.TPLock, "12", "触摸板锁");
		AppendFans(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.AmbientLight, "15", "LOGO 灯");
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardMode, "16", "键盘灯模式");
		AppendColor(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardBrightness, "18", "键盘灯亮度");
		AppendOne(sb, wmi, BaolongdaWmi.Function.SystemAcType, "19", "交流类型");
		AppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeedSwitch, "20", "风扇手动开关");
		AppendOne(sb, wmi, BaolongdaWmi.Function.CpuThermometer, "22", "CPU 温度");
		AppendOne(sb, wmi, BaolongdaWmi.Function.CpuPower, "23", "功耗墙自定义开关");
		// ⭐ 结论清单（⭐ 复用快照的那份 —— ⛔ 不另写一套判据 ✅）
		sb.AppendLine();
		sb.AppendLine("── ③b 结论清单（⭐ 哪些功能号通 ✅）──");
		try
		{
			BaolongdaStateSnapshot snap = BaolongdaStateSnapshot.Capture(wmi);
			foreach (string line in snap.DescribeSupportList())
			{
				sb.AppendLine("  " + line);
			}
			sb.AppendLine("  ── 摘要 ──");
			sb.AppendLine("  " + snap.Describe());
		}
		catch (Exception ex)
		{
			sb.AppendLine("  ⚠️ 快照采集失败：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 取一个可用的 `BaolongdaWmi`：⭐ 优先用**后端已经打开的那个** ✅（⛔ 不新建连接）。
	/// <para>⚠️ 拿不到就返回 <c>null</c> ⇒ ⭐ 本节如实写"没有接口" ✅（⛔ 不假装 ✅）。</para>
	/// </summary>
	private static BaolongdaWmi ResolveWmi()
	{
		try
		{
			return Program.hw?.BaolongdaWmi;
		}
		catch
		{
			return null;
		}
	}

	private static void AppendOne(StringBuilder sb, BaolongdaWmi wmi, BaolongdaWmi.Function function, string number, string name)
	{
		string verdict;
		try
		{
			if (wmi.TryReadByteChecked(function, out byte value))
			{
				verdict = "✅ 可读，值 = " + value;
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或回显不符 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + number.PadLeft(2) + " · " + name.PadRight(18) + " · " + verdict);
	}

	private static void AppendGpuMode(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadGpuMode(out int mode))
			{
				verdict = "✅ 可读，值 = " + mode + "（" + (mode == BaolongdaWmi.GpuModeHybrid ? "混合" : "独显直连") + "）";
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或值不是 0/1 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "9".PadLeft(2) + " · " + "显卡模式".PadRight(18) + " · " + verdict);
	}

	private static void AppendColor(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadBytesChecked(BaolongdaWmi.Function.RgbKeyboardColor, 3, out byte[] rgb))
			{
				verdict = "✅ 可读，R=" + rgb[0] + " G=" + rgb[1] + " B=" + rgb[2];
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或回显不符 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "17".PadLeft(2) + " · " + "键盘灯颜色(RGB)".PadRight(18) + " · " + verdict);
	}

	private static void AppendFans(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				verdict = "✅ 可读，第一通道 = " + speeds.First + " RPM · 第二通道 = " + speeds.Second + " RPM"
					+ "（⚠️ 哪个是 CPU/GPU **归属待 `--fan-watch` 确认** ⚠️）";
			}
			else
			{
				verdict = "⚠️ 读不到";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "13".PadLeft(2) + " · " + "风扇转速".PadRight(18) + " · " + verdict);
	}

	// ── ⑤ HID 事件如实说明（⭐ 我们没有实现 ✅ ⛔ 不假装 ✅）──
	private static void AppendHidNote(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ④ HID 事件（⭐ 如实说明：⭐ **未实现** ⚠️）──");
		sb.AppendLine("⭐ 参考实现（JiaolongControl）里有一张 `EventName` HID 事件表（⭐ 4~33），"
			+ "⭐ 用于**设备主动上报**状态变化（⭐ Fn 锁 / ⭐ 触摸板 / ⭐ 键盘灯亮度 / ⭐ 性能档 … ✅）");
		sb.AppendLine("⚠️ 但我们**收不到**这类事件 ⚠️ —— ⭐ 原因已查实：");
		sb.AppendLine("   我们的 HID 封装 `HidDeviceWin` **只有** `HidD_SetFeature` / `WriteFile` / `CreateFile`，"
			+ "⛔ **没有 `ReadFile`** ⇒ ⭐ **没有读输入报告的能力** ❌");
		sb.AppendLine("   且 ⭐ 现有 HID 用途**只有键盘灯下发**（⭐ 纯输出通道 ✅）");
		sb.AppendLine("⇒ ⭐ 所以本版**用周期性 WMI Get 回读**做保底 ✅（⭐ 已挂宝龙达的 3 秒定时器 ✅）");
		sb.AppendLine("⭐ 要做 HID 事件需要：⭐ ① 给 `HidDeviceWin` 加 `ReadFile`（⭐ 或走 RawInput ⚠️）· "
			+ "⭐ ② ⚠️ **在真机上确定「哪个设备 / 哪个 collection 会发事件」** ⚠️（⭐ 这一条只能真机才能定 ✅）");
		sb.AppendLine("⚠️ 在真机数据到手之前，⭐ 本项**保持「暂不支持」的如实状态** ✅（⛔ 不写「已支持」 ✅）");
	}
}
