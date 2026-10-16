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
using System.Management;
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
			AppendWmiClassProbe(sb);
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

	// ── ①b `root\WMI:MICommonInterface` 类/实例存在性（⭐ 识别链的**第一步** ⚠️）──
	//    ⚠️ 2026-10-16 群里反馈：「⭐ 一卸载官方控制台这个软件就识别不了了」⚠️
	//    ⇒ ⭐ 这条反馈原来**无法证伪**：⭐「类不存在」那条分支是【完全静默】的 ❌ ·
	//      ⭐ 而诊断段也**从不打印**这个类 ⇒ ⭐ 我们手上没有任何证据 ⚠️
	//    ⇒ ⭐ 本段 ＋ ⭐ 运行时那条 `Logger.WriteError` 一起补上这个盲点 ✅
	private static void AppendWmiClassProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ①b `root\\WMI:MICommonInterface`（⭐ 宝龙达识别链的**第一步** ⚠️）──");
		sb.AppendLine("⭐ 用途：⭐ 这个类**在不在**，直接决定我们能否认出宝龙达 ✅");
		sb.AppendLine("⚠️ 已知成因：⭐ 群里反馈 **卸载官方控制台（JiaoLong Control）后可能消失** ⚠️"
			+ "（⭐ 新增本段就是为了证实/证伪它 ✅）");
		try
		{
			using ManagementClass mc = new ManagementClass("root\\WMI:MICommonInterface");
			try
			{
				mc.Get();
			}
			catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
			{
				sb.AppendLine("类存在性 = ⛔ **不存在** ❌ ⇒ ⭐ ⭐ **这就是认不出宝龙达的原因** ✅");
				sb.AppendLine("   ⇒ ⭐ 运行时也会有一条【默认级别可见】的日志（⭐ 仅板号为 `MRID6-*` 候选时打 ✅）");
				// ⚠️ ⭐ **这一支最需要旁证** ⚠️ ⇒ ⭐ 打完旁证再返回 ✅（⛔ 不能直接 return ❌）
				AppendSideEvidence(sb);
				return;
			}
			sb.AppendLine("类存在性 = ✅ 存在 ⇒ ⭐ 第一步通过 ✅");
			try
			{
				object guid = mc.Qualifiers["Guid"]?.Value;
				sb.AppendLine("类 GUID = " + (guid?.ToString() ?? "（⚠️ 无 GUID 限定符）"));
			}
			catch
			{
				sb.AppendLine("类 GUID = （读取失败）");
			}
			try
			{
				int count = 0;
				using ManagementObjectCollection instances = mc.GetInstances();
				foreach (ManagementBaseObject inst in instances)
				{
					string relative = (inst as ManagementObject)?.Path?.RelativePath;
					sb.AppendLine("  实例[" + count + "] = " + (string.IsNullOrEmpty(relative) ? "（无路径）" : relative));
					count++;
				}
				sb.AppendLine("实例个数 = " + count
					+ (count == 0 ? " ⚠️ ⇒ 类在但**没有任何实例**（⭐ 识别同样会失败 ⚠️）" : " ✅"));
			}
			catch (Exception ex)
			{
				sb.AppendLine("实例枚举失败 = " + ex.Message);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("类探测异常 = " + ex.Message);
		}
		// ⭐ 旁证段（⭐ **两种情形都要打** ⚠️ —— ⭐ 尤其"类不存在"⭐ 那一支最需要它 ✅）
		AppendSideEvidence(sb);
	}

	/// <summary>
	/// ⭐ 旁证：⭐ 官方控制台（⭐ JiaoLong Control ✅）/ ⭐ 它的驱动 / ⭐ `PNP0C14` 设备 / ⭐ `WmiAcpi.sys`
	/// 的存在性 ✅ —— ⭐ 全部只读 ⚠️。
	/// <para>⚠️⚠️ **必须在"类不存在"时也打印** ✅ —— ⭐ 那正是最需要旁证的场景 ⚠️
	/// （⭐ 群里"卸载官方控制台后识别不了"⭐ 的反馈就落在那一支 ✅）。</para>
	/// <para>⭐ 真机实测（⭐ 同方/GCU 机型 ✅）：⭐ **有 `PNP0C14` 设备但【没有】`MICommonInterface` 类** ✅
	/// ⇒ ⭐ 所以「设备在 ≠ 类在」⚠️ ⇒ ⭐ 两者必须**分别打印**才能分辨断在哪一步 ✅。</para>
	/// </summary>
	private static void AppendSideEvidence(StringBuilder sb)
	{
		// ⭐ 旁证：⭐ 官方控制台/它的驱动在不在（⭐ 用于和"卸载后失效"的反馈对照 ✅ · ⭐ 只查存在性 ⚠️）
		try
		{
			string[] paths = new string[3]
			{
				"C:\\Program Files\\JiaoLongControl",
				"C:\\Program Files\\JiaoLongControl\\JiaoLongDriver64.sys",
				"C:\\Windows\\System32\\drivers\\WmiAcpi.sys"
			};
			for (int k = 0; k < paths.Length; k++)
			{
				string what = (k == 0)
					? "官方控制台目录"
					: ((k == 1) ? "官方控制台驱动" : "WmiAcpi.sys（⭐ ACPI MOF ⇒ WMI 类 的提供者 ✅）");
				bool exists = (k == 0) ? Directory.Exists(paths[k]) : File.Exists(paths[k]);
				sb.AppendLine("旁证·" + what + " = " + (exists ? "✅ 在" : "⛔ 不在"));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("旁证探测异常 = " + ex.Message);
		}
		// ⭐ 旁证·`PNP0C14` 设备（⭐ **关键判据** ⚠️ · ⭐ 真机实测：⭐ 同方机型【有】`PNP0C14` 但【没有】该类 ✅）
		try
		{
			int deviceCount = 0;
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
				"root\\CIMV2", "SELECT DeviceID,Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\\\PNP0C14%'");
			using ManagementObjectCollection rows = searcher.Get();
			foreach (ManagementBaseObject row in rows)
			{
				sb.AppendLine("旁证·PNP0C14 设备[" + deviceCount + "] = " + row["DeviceID"]
					+ " · 状态=" + row["Status"]);
				deviceCount++;
			}
			sb.AppendLine("旁证·PNP0C14 设备个数 = " + deviceCount
				+ (deviceCount == 0
					? " ⚠️ ⇒ 连 ACPI-WMI 映射设备都没有（⭐ 少见 ⚠️）"
					: " ✅（⭐ 注意：⭐ 有设备**不代表**有 `MICommonInterface` 类 ⚠️ —— ⭐ 类由**该机 BIOS 的 ACPI MOF** 定义 ✅）"));
		}
		catch (Exception ex)
		{
			sb.AppendLine("旁证·PNP0C14 设备查询失败 = " + ex.Message);
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
