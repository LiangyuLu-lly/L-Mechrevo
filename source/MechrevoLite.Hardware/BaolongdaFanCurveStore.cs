// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · "全部接入"第 4 步 · 风扇曲线 · ⭐ 只做存储）：
// ⭐⭐ 宝龙达**软件闭环风扇曲线**的本地持久化 ✅
//
// ⭐⭐ 为什么需要它（⭐ 用户 2026-10-16 原话：「四 宝龙达是风扇曲线是吗 能不能接我们的曲线里面」）：
//    ⭐ 我们的风扇曲线（`FanCurveForm` / `PerfModeStore.CpuFanDuty`）⭐ 是"⭐ **上传表给 GCU 固件**"的 ✅
//       ⇒ ⭐ 固件拿到表后**自己按温度调**，⭐ 我们退出后它照样在跑 ✅
//    ⚠️ 而宝龙达协议（⭐ 功能号 20/21）⭐ **只有**「⭐ 手动开关 ＋ ⭐ 固定转速」，⭐ **没有曲线表** ⚠️
//       （⭐ 三份开源参考实现里也都没有 ✅）
//    ⇒ ⭐ ⭐ 所以宝龙达只能做 ⭐ **软件闭环**：⭐ **我们的程序读温度 → ⭐ 查曲线 → ⭐ 写功能号 21** ✅
//
// ⭐⭐ 为什么**不**复用 `PerfModeStore`（⭐ 重要的设计取舍，⭐ 如实说明）：
//    ⭐ ① ⭐ `PerfModeStore` 在 **`MechrevoLite.Mode`** 项目里，⭐ 而本执行器在 **`MechrevoLite.Hardware`** ⚠️
//       ⇒ ⭐ 依赖方向是 `MechrevoLite` → ⭐ `Hardware` ⇒ ⭐ **Hardware 引用不到它** ❌
//    ⭐ ② ⭐ `PerfModeService.RecordFanCurves` ⭐ 有硬门槛：
//       ⭐ `mode.Route != PerfModeRoute.FirmwareSlot` ⭐ 时**直接跳过不落盘** ⚠️
//       ⭐ 那是 GCU"固件槽档"的概念 ⇒ ⭐ 宝龙达**未必成立** ⚠️
//    ⭐ ③ ⭐ 语义也不同：⭐ 那是"⭐ **固件槽的曲线表**"、⭐ 这是"⭐ **我们软件执行器的曲线**" ✅
//    ⇒ ⭐ ⭐ 所以新增一个**同风格**的独立存储（⭐ 照 `LightingSettingsStore` 的写法 ✅），
//       ⭐ 路径与 `PerfModeStore` 同源（⭐ 都在 `%APPDATA%\MechrevoLite\` ✅）⇒ ⭐ 安装/卸载一起带走 ✅
//
// ⚠️ 本轮**只做存储**（⭐ 用户要求「⭐ 先只做存储 + 门面层的曲线执行器骨架 ✅」）
//    ⇒ ⭐ ⛔ **不开** `FanSettings` 能力位（⭐ 因为⚠️ 风扇两通道归属未确认 ⇒ ⭐ 会违反铁律 ✅）
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达**软件闭环风扇曲线**的设置快照 ✅
/// <para>⭐ <b>曲线形状与既有风扇曲线对齐</b>：⭐ 每条通道 ⭐ **16 个点** ✅
/// —— ⭐ 每点 = ⭐ (温度 ℃, ⭐ 占空比 %) ✅，⭐ 与 `MechrevoHw.CpuCurveUpT` / `CpuCurveDuty`
/// 同形状 ⇒ ⭐ 将来要复用 `FanCurveForm` 的编辑界面时**不用转换** ✅。</para>
/// <para>⚠️ 占空比是 ⭐ **百分比 0~100**（⭐ 我们的界面口径 ✅）；
/// ⭐ 转成宝龙达的 ⭐ `x100RPM` 由执行器按"⭐ 该通道最大转速"换算 ⚠️（⭐ 见执行器注释 ✅）。</para>
/// </summary>
internal sealed class BaolongdaFanCurveSettings
{
	/// <summary>⭐ 点数（⭐ 与既有曲线一致 = 16 ✅）。</summary>
	internal const int PointCount = 16;

	/// <summary>⭐ 软件闭环是否启用 ⚠️（⭐ 默认 **false** —— ⭐ 通道归属未确认前不许启用 ✅）。</summary>
	internal bool Enabled { get; init; }

	/// <summary>⭐ 这份曲线是在哪个档位下保存的（⭐ 便于将来按档位分别记忆 ✅）；空 = 不区分档位 ✅。</summary>
	internal string ModeId { get; init; } = string.Empty;

	/// <summary>⭐ CPU 通道的温度点（⭐ 16 个 ✅）。</summary>
	internal int[] CpuTemperatures { get; init; } = DefaultTemperatures();

	/// <summary>⭐ CPU 通道的占空比（⭐ 16 个 · ⭐ 0~100 ✅）。</summary>
	internal int[] CpuDuties { get; init; } = DefaultDuties();

	/// <summary>⭐ GPU 通道的温度点（⭐ 16 个 ✅）。</summary>
	internal int[] GpuTemperatures { get; init; } = DefaultTemperatures();

	/// <summary>⭐ GPU 通道的占空比（⭐ 16 个 · ⭐ 0~100 ✅）。</summary>
	internal int[] GpuDuties { get; init; } = DefaultDuties();

	/// <summary>⭐ 默认温度点（⭐ 40~95℃ 均匀铺开 ✅ —— ⭐ 与既有曲线的常见形状一致 ✅）。</summary>
	internal static int[] DefaultTemperatures()
	{
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			// ⭐ 40℃ 起、⭐ 每点 +3~4℃ ⇒ ⭐ 最后一点 ≈ 95℃ ✅
			array[i] = 40 + (int)Math.Round(i * (95.0 - 40.0) / (PointCount - 1));
		}
		return array;
	}

	/// <summary>
	/// ⭐ 默认占空比（⭐ **保守**：⭐ 低温段低、⭐ 高温段高，⭐ 且**不贴满** ⚠️）
	/// <para>⚠️ 刻意**不从 0 起**：⭐ 风扇长期停转对笔记本散热不利 ⚠️ ⇒ ⭐ 最低给 20% ✅</para>
	/// </summary>
	internal static int[] DefaultDuties()
	{
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			int t = i * 100 / (PointCount - 1);
			array[i] = 20 + t * 60 / 100; // ⭐ 20% → 80% ✅
		}
		return array;
	}

	/// <summary>⭐ 深拷贝（⭐ 数组类型是可变引用 ⇒ ⭐ 传出去前必须拷贝，⛔ 免得外部改到内部状态 ✅）。</summary>
	internal BaolongdaFanCurveSettings Clone()
	{
		return new BaolongdaFanCurveSettings
		{
			Enabled = Enabled,
			ModeId = ModeId,
			CpuTemperatures = (int[])CpuTemperatures.Clone(),
			CpuDuties = (int[])CpuDuties.Clone(),
			GpuTemperatures = (int[])GpuTemperatures.Clone(),
			GpuDuties = (int[])GpuDuties.Clone()
		};
	}
}

/// <summary>
/// ⭐⭐ 宝龙达软件风扇曲线的存储（⭐ 照 `LightingSettingsStore` 的风格：⭐ 纯文件、⭐ 可覆盖目录 ✅）
/// <para>⭐ 文件：⭐ <c>%APPDATA%\MechrevoLite\fan_curve_bd.cfg</c> ✅
/// （⭐ 与 `keyboard.cfg` / `lightbar.cfg` / `logolight.cfg` 同目录 ⇒ ⭐ 安装/卸载一起带走 ✅）</para>
/// <para>⚠️ 全程 `try/catch` 吞异常：⭐ 存储坏掉**绝不能**影响启动或运行 ✅（⭐ 与既有 Store 同款约定 ✅）。</para>
/// </summary>
internal static class BaolongdaFanCurveStore
{
	/// <summary>⭐ 目录覆盖变量（⭐ 与 `LightingSettingsStore` 用同一个 —— ⭐ 便于测试与迁移 ✅）。</summary>
	internal const string ConfigDirectoryOverrideVariable = "LMECHREVO_LIGHT_CFG_DIR";

	private const string FileName = "fan_curve_bd.cfg";

	/// <summary>⭐ 读一次设置（⭐ 读不到/坏掉 ⇒ ⭐ 返回**默认**（⭐ `Enabled=false` ✅），⭐ 不外抛 ✅）。</summary>
	internal static BaolongdaFanCurveSettings Load()
	{
		try
		{
			string path = GetPath();
			if (!File.Exists(path))
			{
				return new BaolongdaFanCurveSettings();
			}
			Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in File.ReadAllLines(path))
			{
				int eq = line.IndexOf('=');
				if (eq > 0)
				{
					map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
				}
			}
			return new BaolongdaFanCurveSettings
			{
				// ⚠️ 默认 false：⭐ 文件里没写 `enabled=1` 就是**不启用** ✅（⭐ 安全默认 ✅）
				Enabled = map.TryGetValue("enabled", out string e) && e == "1",
				ModeId = (map.TryGetValue("modeid", out string m) ? m : string.Empty),
				CpuTemperatures = ParseCurve(map, "cputemp", BaolongdaFanCurveSettings.DefaultTemperatures()),
				CpuDuties = ParseCurve(map, "cpuduty", BaolongdaFanCurveSettings.DefaultDuties()),
				GpuTemperatures = ParseCurve(map, "gputemp", BaolongdaFanCurveSettings.DefaultTemperatures()),
				GpuDuties = ParseCurve(map, "gpuduty", BaolongdaFanCurveSettings.DefaultDuties())
			};
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 读取失败（按默认值处理）：" + ex.Message);
			return new BaolongdaFanCurveSettings();
		}
	}

	/// <summary>⭐ 写一次设置（⭐ 失败只记日志 ✅）。</summary>
	internal static bool Save(BaolongdaFanCurveSettings settings)
	{
		if (settings == null)
		{
			return false;
		}
		try
		{
			string path = GetPath();
			string directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("# 宝龙达软件风扇曲线（⭐ 由 L-Mechrevo 自动生成，⭐ 手改请谨慎 ✅）");
			sb.AppendLine("# ⚠️ 数值是**我们软件记的**（⭐ 机器读不回来）—— 见 docs\\notes 里第 4 步说明 ✅");
			sb.Append("enabled=").Append(settings.Enabled ? "1" : "0").AppendLine();
			sb.Append("modeid=").AppendLine(settings.ModeId ?? string.Empty);
			sb.Append("cputemp=").AppendLine(Join(settings.CpuTemperatures));
			sb.Append("cpuduty=").AppendLine(Join(settings.CpuDuties));
			sb.Append("gputemp=").AppendLine(Join(settings.GpuTemperatures));
			sb.Append("gpuduty=").AppendLine(Join(settings.GpuDuties));
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Logger.WriteLine($"宝龙达风扇曲线：⭐ 已保存（⭐ enabled={settings.Enabled} · ⭐ CPU={Join(settings.CpuDuties)} · ⭐ GPU={Join(settings.GpuDuties)}）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 保存失败：" + ex.Message);
			return false;
		}
	}

	/// <summary>⭐ 删掉设置（⭐ 恢复"从未配置"状态 ✅）。</summary>
	internal static void Clear()
	{
		try
		{
			string path = GetPath();
			if (File.Exists(path))
			{
				File.Delete(path);
				Logger.WriteLine("宝龙达风扇曲线：⭐ 已清除本地配置 ✅");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 清除失败：" + ex.Message);
		}
	}

	/// <summary>⭐ 设置文件是否存在（⭐ 诊断用 ✅）。</summary>
	internal static bool Exists()
	{
		try
		{
			return File.Exists(GetPath());
		}
		catch
		{
			return false;
		}
	}

	private static string Join(int[] values)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}
		string[] parts = new string[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
		}
		return string.Join(",", parts);
	}

	/// <summary>
	/// ⭐ 解析一条 16 点曲线。
	/// <para>⚠️ **容错**：⭐ 长度不对/格式坏 ⇒ ⭐ **整条退回默认**（⛔ 不部分采用 —— ⭐ 半个曲线会让风扇乱转 ✅）。</para>
	/// </summary>
	private static int[] ParseCurve(Dictionary<string, string> map, string key, int[] fallback)
	{
		if (!map.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}
		string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != BaolongdaFanCurveSettings.PointCount)
		{
			Logger.WriteLine($"宝龙达风扇曲线：⭐ {key} 点数={parts.Length}（⭐ 应为 {BaolongdaFanCurveSettings.PointCount}）⇒ 该条退回默认 ⚠️");
			return fallback;
		}
		int[] array = new int[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out array[i]))
			{
				Logger.WriteLine($"宝龙达风扇曲线：⭐ {key} 第 {i} 项解析失败 ⇒ 该条退回默认 ⚠️");
				return fallback;
			}
		}
		return array;
	}

	private static string GetPath()
	{
		return Path.Combine(GetConfigDirectory(), FileName);
	}

	/// <summary>⭐ 配置目录（⭐ 与 `LightingSettingsStore` 完全同源 ✅ —— ⭐ 同一个覆盖变量 ✅）。</summary>
	private static string GetConfigDirectory()
	{
		try
		{
			string environmentVariable = Environment.GetEnvironmentVariable(ConfigDirectoryOverrideVariable);
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return environmentVariable;
			}
		}
		catch
		{
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite");
	}
}
