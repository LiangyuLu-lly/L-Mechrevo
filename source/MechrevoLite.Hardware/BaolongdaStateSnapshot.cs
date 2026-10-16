// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · ⭐ "回读"需求主体）：
// ⭐⭐ 宝龙达**状态快照** —— 一次把能读的功能号全读一遍 ✅
//
// ⭐⭐ 为什么需要（⭐ 用户 2026-10-16 原话：「⭐ 回读功能没有我们需要做的
//    ⭐ 要不然宝龙达切了读不到 HID 事件表」）：
//    ⚠️ 缺口：⭐ 宝龙达的几个开关（⭐ Fn 锁 11 · ⭐ 触摸板锁 12 · ⭐ 键盘灯状态 10 ·
//       ⭐ 风扇手动 20 · ⭐ LOGO 15 ⚠️）⭐ **只有我们主动去读（Get）才知道状态** ⚠️
//       ⇒ ⭐ 用户/硬件自己改了状态（⭐ 按 Fn+Q 切档、⭐ 按触摸板/飞行模式键 ✅）
//          ⭐ **我们不会知道** ❌ ⇒ ⭐ 界面显示的状态会**过时/不对** ❌
//    ⭐ 而 HID 事件那条路 ⭐ **已查明走不通** ⚠️（⭐ `HidDeviceWin` ⛔ 没有 `ReadFile` ✅
//       —— ⭐ 详见方案文档第 25.5 节 ✅）
//    ⇒ ⭐ ⭐ 所以保底方案 = ⭐ **周期性 WMI Get 回读** ✅（⭐ 挂在宝龙达已有的 3 秒定时器上 ✅）
//
// ⭐⭐ 设计原则（⭐ 重要 ✅）：
//    ⭐ ① ⭐ **只读**：⭐ 只发 Get(=250) ✅，⛔ 一个 Set 都不发 ✅
//    ⭐ ② ⭐ **回显校验**：⭐ 全部走 `TryReadByteChecked`（⭐ 校验 `OutData[3]` 回显功能号 ✅）
//       ⇒ ⭐ 防止"⭐ 读 A 却拿到 B 的值"⭐ 这种静默错配 ❌
//    ⭐ ③ ⭐ **读不到就是"未知"**：⭐ 用 `int?`（⭐ `null` = 读不到 ✅），
//       ⛔ **绝不拿 0 冒充**（⭐ 0 在好几个功能号里都是**合法值** ⚠️ —— ⭐ 例如风扇转速 ✅）
//    ⭐ ④ ⭐ **非宝龙达不动作**：⭐ 调用方（`MechrevoHw`）先判 `_baolongda != null` ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达**状态快照**（⭐ 一次回读的结果 ✅）。
/// <para>⚠️ 所有字段都是 <c>int?</c> ⇒ ⭐ <c>null</c> 表示**本机读不到**（⛔ 不是 0 ✅）。</para>
/// <para>⭐ 用途：⭐ ① 周期同步进 `MechrevoHw.QuickSwitches` ✅ ·
/// ⭐ ② 诊断包（⭐ 让机主一次看清"⭐ 这台机哪些功能号是通的"✅）✅。</para>
/// </summary>
internal sealed class BaolongdaStateSnapshot
{
	/// <summary>⭐ 采集时刻（⭐ 用于判断新鲜度 ✅）。</summary>
	internal DateTime CapturedAt { get; private set; } = DateTime.MinValue;

	/// <summary>⭐ 功能号 8（⭐ 系统性能模式 ✅：⭐ 0/1/2 ✅）。</summary>
	internal int? SystemPerMode { get; private set; }

	/// <summary>⭐ 功能号 9（⭐ 显卡模式 ✅：⭐ 0 = 混合 · ⭐ 1 = 独显直连 ✅）。</summary>
	internal int? GpuMode { get; private set; }

	/// <summary>⭐ 功能号 10（⭐ 键盘灯状态 ✅：⭐ 0 = 关 · ⭐ 否则开 ⚠️）。</summary>
	internal int? KeyboardStatus { get; private set; }

	/// <summary>⭐ 功能号 11（⭐ Fn 锁 ⚠️ 未真机验证 ✅）。</summary>
	internal int? FnLock { get; private set; }

	/// <summary>⭐ 功能号 12（⭐ 触摸板锁 ⚠️ 未真机验证 ✅）。</summary>
	internal int? TouchpadLock { get; private set; }

	/// <summary>⭐ 功能号 13（⭐ 第一个风扇通道转速 RPM ⚠️ 归属待确认 ✅）。</summary>
	internal int? FanFirstRpm { get; private set; }

	/// <summary>⭐ 功能号 13（⭐ 第二个风扇通道转速 RPM ⚠️ 归属待确认 ✅）。</summary>
	internal int? FanSecondRpm { get; private set; }

	/// <summary>⭐ 功能号 15（⭐ LOGO 灯 ✅：⭐ 1 = 亮 · ⭐ 0 = 灭 ✅）。</summary>
	internal int? LogoLight { get; private set; }

	/// <summary>⭐ 功能号 16（⭐ 键盘灯模式 ⚠️ 未真机验证 ✅）。</summary>
	internal int? KeyboardMode { get; private set; }

	/// <summary>⭐ 功能号 17（⭐ 键盘灯颜色 R,G,B ⚠️ 未真机验证 ✅）；⭐ 读不到为 <c>null</c> ✅。</summary>
	internal byte[] KeyboardColorRgb { get; private set; }

	/// <summary>⭐ 功能号 18（⭐ 键盘灯亮度 0~3 ⚠️ 未真机验证 ✅）。</summary>
	internal int? KeyboardBrightness { get; private set; }

	/// <summary>⭐ 功能号 19（⭐ 交流类型 ⚠️ 未真机验证 ✅：⭐ 一般 1 = 接电源 · ⭐ 0 = 电池 ⚠️）。</summary>
	internal int? SystemAcType { get; private set; }

	/// <summary>⭐ 功能号 20（⭐ 风扇手动开关 ✅：⭐ 1 = 手动接管中 ✅）。</summary>
	internal int? FanManualSwitch { get; private set; }

	/// <summary>⭐ 功能号 22（⭐ CPU 温度 ℃ ✅）。</summary>
	internal int? CpuTemperature { get; private set; }

	/// <summary>⭐ 功能号 23（⭐ 功耗墙自定义是否已开启 ⚠️ —— ⭐ 数值读不回来 ✅）。</summary>
	internal int? CpuCustomEnabled { get; private set; }

	/// <summary>
	/// ⭐⭐ 采一次快照（⭐ **全只读** ✅）。
	/// <para>⚠️ 每个功能号**独立 try/catch** ⇒ ⭐ 任何一个失败都**不影响**其余的 ✅。</para>
	/// </summary>
	internal static BaolongdaStateSnapshot Capture(BaolongdaWmi wmi)
	{
		BaolongdaStateSnapshot snap = new BaolongdaStateSnapshot
		{
			CapturedAt = DateTime.Now
		};
		if (wmi == null)
		{
			return snap;
		}
		// ⭐ 单字节功能号：⭐ 逐个读、⭐ 各自吞异常 ✅
		snap.SystemPerMode = ReadByteSafe(wmi, BaolongdaWmi.Function.SystemPerMode);
		snap.KeyboardStatus = ReadByteSafe(wmi, BaolongdaWmi.Function.RgbKeyboardStatus);
		snap.FnLock = ReadByteSafe(wmi, BaolongdaWmi.Function.FnLock);
		snap.TouchpadLock = ReadByteSafe(wmi, BaolongdaWmi.Function.TPLock);
		snap.LogoLight = ReadByteSafe(wmi, BaolongdaWmi.Function.AmbientLight);
		snap.KeyboardMode = ReadByteSafe(wmi, BaolongdaWmi.Function.RgbKeyboardMode);
		snap.KeyboardBrightness = ReadByteSafe(wmi, BaolongdaWmi.Function.RgbKeyboardBrightness);
		snap.SystemAcType = ReadByteSafe(wmi, BaolongdaWmi.Function.SystemAcType);
		snap.FanManualSwitch = ReadByteSafe(wmi, BaolongdaWmi.Function.MaxFanSpeedSwitch);
		snap.CpuTemperature = ReadByteSafe(wmi, BaolongdaWmi.Function.CpuThermometer);
		snap.CpuCustomEnabled = ReadByteSafe(wmi, BaolongdaWmi.Function.CpuPower);
		// ⭐ 显卡模式：⭐ 走协议层的专用读（⭐ 它带"⭐ 未知取值当失败"⭐ 的判据 ✅）
		try
		{
			if (wmi.TryReadGpuMode(out int gpuMode))
			{
				snap.GpuMode = gpuMode;
			}
		}
		catch
		{
		}
		// ⭐ 键盘灯颜色（⭐ 功能号 17 = R,G,B 三字节 ✅）
		try
		{
			if (wmi.TryReadBytesChecked(BaolongdaWmi.Function.RgbKeyboardColor, 3, out byte[] rgb))
			{
				snap.KeyboardColorRgb = rgb;
			}
		}
		catch
		{
		}
		// ⭐ 风扇转速（⭐ 功能号 13 = 两个 16 位 ✅）
		try
		{
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				snap.FanFirstRpm = speeds.First;
				snap.FanSecondRpm = speeds.Second;
			}
		}
		catch
		{
		}
		return snap;
	}

	private static int? ReadByteSafe(BaolongdaWmi wmi, BaolongdaWmi.Function function)
	{
		try
		{
			// ⭐ 用**带回显校验**的那一个 ✅（⭐ 不符即作废 ✅）
			if (wmi.TryReadByteChecked(function, out byte value))
			{
				return value;
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>
	/// ⭐ 一行摘要（⭐ 供日志/诊断 ✅）。⭐ 读不到的显示 ⭐ `—` ✅（⛔ 不显示 0 冒充 ✅）。
	/// </summary>
	internal string Describe()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("宝龙达状态：");
		sb.Append("性能档=").Append(Show(SystemPerMode)).Append(' ');
		sb.Append("显卡=").Append(DescribeGpuMode(GpuMode)).Append(' ');
		sb.Append("键盘灯=").Append(Show(KeyboardStatus)).Append(' ');
		sb.Append("Fn锁=").Append(Show(FnLock)).Append(' ');
		sb.Append("触摸板=").Append(Show(TouchpadLock)).Append(' ');
		sb.Append("LOGO=").Append(Show(LogoLight)).Append(' ');
		sb.Append("键盘模式=").Append(Show(KeyboardMode)).Append(' ');
		sb.Append("键盘色=").Append(DescribeColor()).Append(' ');
		sb.Append("键盘亮度=").Append(Show(KeyboardBrightness)).Append(' ');
		sb.Append("交流=").Append(Show(SystemAcType)).Append(' ');
		sb.Append("风扇手动=").Append(Show(FanManualSwitch)).Append(' ');
		sb.Append("CPU=").Append(Show(CpuTemperature)).Append(' ');
		sb.Append("功耗墙自定义=").Append(Show(CpuCustomEnabled)).Append(' ');
		sb.Append("风扇RPM=").Append(Show(FanFirstRpm)).Append('/').Append(Show(FanSecondRpm));
		return sb.ToString();
	}

	/// <summary>⭐ "哪些功能号是通的"清单（⭐ 供诊断包 ⇒ ⭐ 让机主一眼看出本机支持情况 ✅）。</summary>
	internal List<string> DescribeSupportList()
	{
		List<string> list = new List<string>();
		Add(list, 8, "系统性能模式", SystemPerMode);
		Add(list, 9, "显卡模式", GpuMode);
		Add(list, 10, "键盘灯状态", KeyboardStatus);
		Add(list, 11, "Fn 锁", FnLock);
		Add(list, 12, "触摸板锁", TouchpadLock);
		Add(list, 13, "风扇转速（第一通道）", FanFirstRpm);
		Add(list, 13, "风扇转速（第二通道）", FanSecondRpm);
		Add(list, 15, "LOGO 灯", LogoLight);
		Add(list, 16, "键盘灯模式", KeyboardMode);
		Add(list, 17, "键盘灯颜色", KeyboardColorRgb == null ? ((int?)null) : 1);
		Add(list, 18, "键盘灯亮度", KeyboardBrightness);
		Add(list, 19, "交流类型", SystemAcType);
		Add(list, 20, "风扇手动开关", FanManualSwitch);
		Add(list, 22, "CPU 温度", CpuTemperature);
		Add(list, 23, "功耗墙自定义开关", CpuCustomEnabled);
		return list;
	}

	private static void Add(List<string> list, int function, string name, int? value)
	{
		list.Add(string.Format(CultureInfo.InvariantCulture, "功能号 {0,2} · {1,-20} · {2}",
			function, name, (value.HasValue ? ("✅ 可读（值 " + value.Value + "）") : "⚠️ 读不到")));
	}

	private static string Show(int? value)
	{
		return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";
	}

	private static string DescribeGpuMode(int? value)
	{
		if (!value.HasValue)
		{
			return "—";
		}
		if (value.Value == BaolongdaWmi.GpuModeHybrid)
		{
			return "混合(0)";
		}
		if (value.Value == BaolongdaWmi.GpuModeDiscrete)
		{
			return "独显直连(1)";
		}
		return value.Value.ToString(CultureInfo.InvariantCulture);
	}

	private static string DescribeColor()
	{
		return "见诊断明细";
	}
}
