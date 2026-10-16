// 2026-10-16（0.291.9 beta22.9 · 宝龙达模具适配 · 第 2 步）：
// 宝龙达模具后端 —— ⭐ 本轮**接上真实 WMI 协议层**（<see cref="BaolongdaWmi" />），
// 取代第 1 步那个空骨架。
//
// 目标机型：**机械革命 蛟龙16Pro（2022 / 2023 / 2024 年款）** 与其它宝龙达（Baolongda）模具机型。
// 它**不走同方的 GCU 服务**（GCU = MQTT 127.0.0.1:13688，那是同方/AiStone 那套）。
//
// ⭐⭐ 当前状态（⭐ 重要，⭐ 请先读完再动）：
//   · ⭐ `BaolongdaWmi.TryCreate()` 是**真实探测**（⭐ 打开 `root\WMI:MICommonInterface` 实例 +
//     只读试读 CPU 温度 + 摸风扇个数/模式档数）⇒ ⭐ **在本树支持的任何机型上都能安全跑** ✅
//   · ⭐ 但**接管（成为 ActiveBackend）默认仍然是【关】** —— ⚠️ 因为：
//       ⭐ ① 还没有真机验证过（⭐ 手上没有蛟龙16Pro）；
//       ⭐ ② 一旦接管，⭐ 性能档/风扇等会由我们下发 ⇒ ⭐ 没验证就接管是不负责任的 ⚠️。
//   · ⭐ **默认行为**：⭐ 探到接口 ⇒ **只记一行完整诊断日志**（⭐ 类 GUID · 实例路径 · 风扇个数 · 模式档数
//     · CPU 温度），⭐ 然后**照常继续按原候选表往下走** ⇒ ⭐ **对现有任何机型零影响** ✅
//   · ⭐ **要接管**：⭐ 设环境变量 ⭐ `LMECHREVO_BAOLONGDA_BACKEND=1` ⇒ ⭐ 真机调试用 ✅
//     （⭐ 真机验证通过后，⭐ 把"默认不接管"这一层去掉即可 ✅）
//
// 📌 下一步（⭐ 待做，⭐ 见 docs\notes\宝龙达适配-蛟龙16Pro-方案与进度.md）：
//   ⭐ ① ⭐ 拿到蛟龙16Pro 的抓包/日志 ⇒ ⭐ 确认实例名、类 GUID、功能号可用性 ✅
//   ⭐ ② ⭐ 实现 `MechrevoHw.ApplyBaolongdaBackendAsync`（⭐ 仿 `ApplyLegacyBackendAsync`）——
//      设 `_activeBackend`、填能力集、起刷新定时器、`NotifyConnectionReady` ✅
//   ⭐ ③ ⭐ 按能力位逐个接通：性能档 → 风扇 → CPU 温度 → 键盘灯/LOGO → 功耗墙 ✅
//   ⭐ ④ ⭐ 真机验证通过后去掉实验开关 ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 宝龙达模具后端（⭐ 已接真实 WMI 协议层，⭐ 但**接管默认关闭** —— 见文件头说明）。
/// <para>生命周期与 <see cref="LegacyMifsWmi" /> 对齐：探测返回非 null = 后端可用。</para>
/// <para>⛔ 全程只走 WMI（⭐ 官方控制台也是），⛔ 不碰 EC、⛔ 不装内核驱动、⛔ 不写注册表。</para>
/// </summary>
internal sealed class BaolongdaBackend : IDisposable
{
	/// <summary>
	/// 实验开关（环境变量）：⭐ 置 <c>1</c> 才**允许接管**（成为 ActiveBackend）。
	/// <para>⚠️ 不设（默认）时：⭐ 仍然会**只读探测并把诊断写进日志**，⛔ 但不接管 ⇒ 对现有机型零影响 ✅</para>
	/// </summary>
	internal const string EnableEnvVar = "LMECHREVO_BAOLONGDA_BACKEND";

	private readonly BaolongdaWmi _wmi;
	private bool _disposed;

	/// <summary>本后端已接上真实协议层（⭐ 不再是空骨架）。</summary>
	internal static bool IsImplemented => true;

	/// <summary>底层的 WMI 协议对象（⭐ 供将来的 <c>ApplyBaolongdaBackendAsync</c> 使用）。</summary>
	internal BaolongdaWmi Wmi => _wmi;

	private BaolongdaBackend(BaolongdaWmi wmi)
	{
		_wmi = wmi;
	}

	/// <summary>
	/// 探测入口（签名与 <see cref="LegacyMifsWmi.TryCreate" /> 一致：**返回非 null 即成功**）。
	/// <para>⭐ ① 只读探测（<see cref="BaolongdaWmi.TryCreate" />）—— ⭐ 探不到直接返回 null（⭐ 零影响）✅</para>
	/// <para>⭐ ② 探到了 ⇒ ⭐ 打一行**完整诊断**（⭐ 这就是我们最缺的真机证据 ✅）</para>
	/// <para>⭐ ③ ⭐ 只有设了 <see cref="EnableEnvVar" />=<c>1</c> 才**接管**；⭐ 否则放掉并继续原候选表 ✅</para>
	/// <para>⛔ 任何异常都吞掉返回 null（探测循环还会再兜一层）。</para>
	/// </summary>
	internal static BaolongdaBackend TryCreate()
	{
		BaolongdaWmi wmi = null;
		try
		{
			wmi = BaolongdaWmi.TryCreate();
			if (wmi == null)
			{
				// ⭐ 没探到 ⇒ 静默返回（⭐ 非宝龙达机型每次启动都走到这里，⛔ 不能刷日志）
				return null;
			}

			// ── ⭐ 探到了：⭐ 先把诊断写全（⭐ 这是本轮最有价值的产出 ✅）──
			string guidNote;
			if (string.Equals(wmi.ClassGuid, LegacyMifsWmi.InterfaceGuid, StringComparison.OrdinalIgnoreCase))
			{
				guidNote = "⭐ 与老同方那份**相同** ⇒ 说明 LegacyMifsWmi 在本机**本来也能探到**";
			}
			else if (wmi.ClassGuid.Length == 0)
			{
				guidNote = "⚠️ 该类**没有 GUID 限定符** ⇒ ⭐ 这正是 LegacyMifsWmi（硬校验 GUID）**探不到**的原因";
			}
			else
			{
				guidNote = "⚠️ 与老同方那份**不同** ⇒ ⭐ 这正是 LegacyMifsWmi（硬校验 GUID）**探不到**的原因";
			}
			Logger.WriteLine($"宝龙达后端：⭐ 探测到接口 —— 实例=\"{wmi.InstancePath}\" · 类GUID={(wmi.ClassGuid.Length == 0 ? "(无)" : wmi.ClassGuid)} "
				+ $"· 风扇={wmi.FanCount} 个 · 模式={wmi.ModeCount} 档 ⇒ {guidNote}");

			// ── ⭐ 接管仍需显式开关（⭐ 真机验证前保持保守）──
			if (!IsTakeoverEnabled())
			{
				Logger.WriteLine($"宝龙达后端：⭐ 本版**默认不接管**（尚未真机验证）⇒ 继续按原候选表探测；"
					+ $"⭐ 要在本机试用请设环境变量 {EnableEnvVar}=1 后重启（⭐ 设了就会由我们接管性能档/风扇等）");
				wmi.Dispose();
				return null;
			}

			Logger.WriteLine($"宝龙达后端：⭐ 已由环境变量 {EnableEnvVar}=1 启用接管（⭐ 真机调试模式）");
			return new BaolongdaBackend(wmi);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达后端探测失败（已忽略）：" + ex.Message);
			wmi?.Dispose();
			return null;
		}
	}

	/// <summary>是否允许接管（⭐ 只在环境变量显式为 <c>1</c> 时 ✅；⭐ 只读环境变量，⛔ 不写配置/注册表）。</summary>
	private static bool IsTakeoverEnabled()
	{
		try
		{
			return string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar)?.Trim(), "1", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 本后端的能力集。
	/// <para>⚠️ 与 <c>ApplyLegacyBackendAsync</c> 同口径：⭐ 凡"要靠上报才知道"的能力一律 false（⛔ 不假装可写）。</para>
	/// <para>⭐ 本轮**只开放最小集**（性能档）—— ⭐ 风扇/灯效/功耗墙等**等真机验证过再逐个放开**（⭐ 见文件头"下一步"）✅</para>
	/// </summary>
	internal MechrevoDeviceCapabilities BuildCapabilities()
	{
		return new MechrevoDeviceCapabilities
		{
			ProjectId = "Baolongda",
			IsMechrevo = true,
			ProfileAvailable = true,
			TurboMode = true,
			AmdPlatform = PawnIO.CpuInfo.IsAMD
		};
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_wmi?.Dispose();
	}
}
