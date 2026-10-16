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

			// ── ⭐⭐ 2026-10-16（第 3 步 · 接入 UI）：⭐ **不再默认挡住** ✅ ──
			//   ⭐ 之前必须设 `LMECHREVO_BAOLONGDA_BACKEND=1` 才接管（⭐ 那时协议还没真机验证 ⚠️）。
			//   ⭐⭐ 现在协议**已经真机验证**（⭐ 2026-10-16 · 机器 C：⭐ 6 个功能号全部可读、
			//      ⭐ 档位语义与我们完全一致 ✅）⇒ ⭐ **探到就接管** ✅
			//   ⚠️ 但保留一个**反向开关**（⭐ 排障用 ✅）：⭐ 显式设成 ⭐ `0` ⇒ ⭐ 跳过本后端 ✅
			if (IsDisabledByEnvVar())
			{
				Logger.WriteLine($"宝龙达后端：⭐ 已被环境变量 {EnableEnvVar}=0 显式禁用 ⇒ 跳过（⭐ 排障用）");
				wmi.Dispose();
				return null;
			}
			Logger.WriteLine("宝龙达后端：⭐ 协议已真机验证（2026-10-16）⇒ ⭐ 接管本会话（性能档；其余功能等接线）");
			return new BaolongdaBackend(wmi);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达后端探测失败（已忽略）：" + ex.Message);
			wmi?.Dispose();
			return null;
		}
	}

	/// <summary>
	/// ⭐ 是否被**显式禁用**（⭐ 只在环境变量恰为 <c>0</c> 时 ✅）。
	/// <para>⚠️ 语义在 2026-10-16 第 3 步**反转**了：⭐ 以前是"设 <c>1</c> 才启用"（⭐ 协议未验证时保守 ⚠️），
	/// 现在是 ⭐ **"探到就启用、设 <c>0</c> 才禁用"**（⭐ 协议已真机验证 ✅）—— ⭐ 保留它是为了**排障** ✅
	/// （⭐ 万一某台机上接管后不对劲，⭐ 用户/我们能让它退回老接口或 GCU 路径 ✅）。</para>
	/// </summary>
	private static bool IsDisabledByEnvVar()
	{
		try
		{
			return string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar)?.Trim(), "0", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 本后端的能力集 —— ⭐⭐ **本类的能力位是【唯一来源】** ✅
	/// （⭐ <c>MechrevoHw.ApplyBaolongdaBackendAsync</c> 直接用它赋值，⛔ 不再另写一份 ✅）。
	///
	/// <para>⭐⭐ <b>铁律（⭐ 2026-10-16 定稿 · ⭐ 见 docs\notes\宝龙达-全部接入实施方案-20261016.md）</b>：
	/// ⭐ <b>能力位 = "已经接好下发路径"的证明</b>，⛔ **不是"硬件支持"的声明** ⚠️。
	/// ⭐ 因为 UI 只认能力位 ⇒ ⭐ 一旦开出去，⭐ 用户点下去就会走到**已有的 GCU 下发代码**上 ⚠️，
	/// ⭐ 而宝龙达**没有 GCU**（⭐ 没有 MQTT 13688 ✅）⇒ ⭐ 必然失败/静默无效 ❌。
	/// ⇒ ⭐ ⭐ **每开一个能力位，⭐ 必须先把它的下发路径分派到宝龙达 WMI** ✅。</para>
	///
	/// <para>⭐⭐ <b>这条铁律同时保证了"不影响同方"</b> ✅：
	/// ⭐ 宝龙达的能力位**只在本方法里产出**、⭐ 且只在 <c>ApplyBaolongdaBackendAsync</c> 里被用；
	/// ⭐ 老同方走 <c>ApplyLegacyBackendAsync</c>（⭐ 未被改动 ✅）、
	/// ⭐ GCU 机型走 <c>RefreshDeviceCapabilities</c>（⭐ 未被改动 ✅）
	/// ⇒ ⭐ 三者**互不干扰** ✅。</para>
	///
	/// <para>⭐ <b>当前已开放</b>（⭐ 已完成并真机验证的）：
	/// <list type="bullet">
	/// <item>⭐ <c>ProfileAvailable</c> / ⭐ <c>TurboMode</c> —— ⭐ 性能档（功能号 8，⭐ 真机验证 ✅）</item>
	/// </list></para>
	///
	/// <para>⚠️ <b>尚未开放</b>（⭐ 待接下发分派，⭐ 顺序见方案文档第 4 节）：
	/// <list type="bullet">
	/// <item>⚠️ <c>LogoLight</c> —— 功能号 15；⭐ 协议已真机验证（读到 1 = 开 ✅），⭐ **只差下发分派** ✅</item>
	/// <item>⚠️ <c>Keyboard</c> —— 功能号 16/17/18；⚠️ **这三个功能号真机还没验证过** ⚠️（⭐ 方案 2.3）</item>
	/// <item>⚠️ <c>CpuPerformanceTuning</c> —— 功能号 23；⚠️ **只能写、读不回来** ⚠️（⭐ 方案 2.2，⭐ 建议先做"开关+预设"）</item>
	/// <item>⚠️ <c>FanSettings</c> —— 功能号 20/21；⚠️⚠️ **风险最高**：⭐ 会真正改散热行为 ⚠️，
	/// ⭐ 且**风扇通道归属还没确认**（⭐ 猜的 `[4..5]`=GPU · ⭐ `[6..7]`=CPU ⚠️）
	/// ⇒ ⭐ **必须最后做** ✅（⭐ 方案 2.4）</item>
	/// <item>⛔ <c>Lightbar</c> / ⛔ <c>RgbLightbar</c> —— ⭐ 宝龙达功能号 15 **只有开关、⛔ 没有颜色/效果**
	/// ⇒ ⭐ **永远不要开**（⭐ 开了 UI 会显示颜色/效果选项 ⇒ ⭐ 点下去没反应 ❌）✅</item>
	/// </list></para>
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
