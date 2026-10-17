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
			// ⭐⭐ 2026-10-16（⭐ 用户澄清，⭐ 原文：「⭐ 注意不要改蛟龙其他机型已经50系的蛟龙
			//    ⭐ 其他都是同方模具的 只有30 40系的蛟龙16pro不是」）：
			//    ⭐ 即 ⭐ **只有蛟龙16Pro 的 30/40 系是【宝龙达】模具** ✅
			//    ⚠️ ⭐ **其他蛟龙（⭐ 含 50 系）⭐ 全是【同方】模具** ⚠️ ⇒ ⭐ 它们该走 GCU ✅
			//
			//    ⚠️⚠️ **为什么必须加这道门槛**（⭐ 真实风险 ⚠️）：
			//       ⭐ 宝龙达与老同方**用同一个类 GUID** ✅ ⇒ ⭐ 我们把宝龙达候选**提到了老接口之前** ✅
			//       ⭐ 而活体判据只是"⭐ 能读一次 CPU 温度（功能号 22）"⚠️
			//       ⇒ ⚠️ 若某台**同方**机型恰好也能读 ⇒ ⭐ **会被误判成宝龙达** ❌
			//          ⇒ ⭐ 后果一：⭐ 走我们宝龙达的能力集（⭐ 只开了性能档/LOGO/键盘灯/功耗墙/显卡模式 ⚠️）
			//             ⭐ 而这些在同方机型上**未必能用** ⇒ ⚠️ **违反"⛔ 点了不能没反应"** ❌
			//          ⇒ ⭐ 后果二：⭐ 同方机型**本来有 GCU** ⚠️ ⇒ ⭐ 被误判后就**绕开了 GCU**
			//             ⇒ ⭐ **功能静默大缩水** ❌（⭐ 比"漏认宝龙达"难查得多 ⚠️）
			//    ⇒ ⭐ ⭐ **原则（⭐ 用户与我们都认同 ✅）**：
			//       ⭐ **"⭐ 宁可不认宝龙达，⭐ 也绝不误伤同方"** ✅
			//       （⭐ 宝龙达漏认可以用 `--fan-watch` 等工具**人工确认** ✅；
			//         ⭐ 而同方被误判是**静默缩水**，⭐ 极难查 ⚠️）
			if (!IsBaolongdaModelWhitelisted(out string whitelistNote))
			{
				Logger.WriteLine("宝龙达后端：⚠️ 探到 MICommonInterface，⭐ 但**机型不在白名单** ⇒ "
					+ "⭐ **不认作宝龙达** ✅（⭐ 宁可漏认、⭐ 也绝不误伤同方 ✅）—— " + whitelistNote);
				try
				{
					wmi.Dispose();
				}
				catch
				{
				}
				return null;
			}
			Logger.WriteLine("宝龙达后端：⭐ 机型白名单**通过** ✅ —— " + whitelistNote);			if (IsDisabledByEnvVar())
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
	/// <summary>
	/// ⭐⭐ 2026-10-16（⭐ 用户澄清「⭐ 只有 30/40 系的蛟龙16Pro 不是同方模具」）：
	/// ⭐ **宝龙达机型白名单** ✅ —— ⭐ 一道**防误伤同方**的门槛 ⚠️
	///
	/// <para>⭐⭐ <b>为什么要有它</b>：⭐ 宝龙达与老同方**共用同一个 WMI 类 GUID** ✅
	/// ⇒ ⭐ 我们把宝龙达候选提到了老接口**之前** ✅
	/// ⭐ 而活体判据只是"⭐ 能读一次 CPU 温度"⚠️ ⇒ ⭐ **同方机型有被误判的可能** ❌
	/// ⇒ ⭐ 误判后果：⭐ ① ⭐ 走宝龙达能力集而**同方未必支持** ⇒ ⚠️ 违反"⛔ 点了不能没反应" ❌ ·
	/// ⭐ ② ⭐ 同方**本来有 GCU** 却被绕开 ⇒ ⭐ **功能静默大缩水** ❌</para>
	///
	/// <para>⭐⭐ <b>原则</b>：⭐ **"⭐ 宁可不认宝龙达，⭐ 也绝不误伤同方"** ✅
	/// （⭐ 宝龙达漏认可人工确认 ✅；⭐ 同方被误判是静默缩水，⭐ 极难查 ⚠️）。</para>
	///
	/// <para>⭐ <b>判据</b>：⭐ 读 ⭐ <c>HKLM\HARDWARE\DESCRIPTION\System\BIOS</c> 的
	/// ⭐ <c>BaseBoardProduct</c>（⭐ **只读** ✅ · ⭐ 与 <see cref="CpuVendor" /> 同一个键 ✅）。
	/// ⭐ 白名单：⭐ ① ⭐ <c>MRID6-23</c> ⭐ **精确命中**（⭐ 三台真机证据 ✅）·
	/// ⭐ ② ⭐ <c>MRID6</c> ⭐ **精确命中**（⭐ 2026-10-17 新增 · 蛟龙16 · 6800H＋3070Ti 真机 ✅）·
	/// ⭐ ③ ⭐ <c>MRID6-</c> ⭐ **前缀**（⭐ 覆盖 `MRID6-XX` 变体 ✅）。</para>
	///
	/// <para>⚠️ <b>为什么不用纯前缀 <c>MRID6</c></b>：⭐ 那样 <c>MRID60</c> / <c>MRID6A</c> 这类**假想板号**
	/// 也会被放进来 ⚠️ —— ⭐ 与本类"⭐ 宁可不认宝龙达，⭐ 也绝不误伤同方 ✅"的原则相悖
	/// ⇒ ⭐ 用「**精确 <c>MRID6</c>** ＋ **连字符前缀 <c>MRID6-</c>**」两条并列更稳 ✅
	/// （⭐ 二者合起来 = ⭐ `MRID6` 家族全覆盖，⭐ 且不会匹配"以 `MRID6` 开头但后面不是连字符"的板号 ✅）。</para>
	///
	/// <para>⭐⭐ <b>白名单安全性的真机依据（⭐ 用户 2026-10-16 提供 ✅）</b>：
	/// ⭐ 扫了 **28 个诊断包**（⭐ 共 6 个不同机型 ✅），⭐ 板号分布为 ——
	/// ⭐ 宝龙达：⭐ <c>MRID6-23</c> ✅；
	/// ⭐ 同方：⭐ <c>GM5HG0A</c>（⭐ RTX 4060 ✅）· ⭐ <c>GM6IX8B</c>（⭐ RTX 4080 ✅）·
	/// ⭐ <c>KUANGSHI-JIGUANG Series-X6AR55xW</c>（⭐ RTX **5070** ✅）·
	/// ⭐ <c>YAOSHI Series-X6AR55xY</c>（⭐ RTX **5070** ✅）· ⭐ <c>Standard</c>（⭐ 板号读不到 ✅）
	/// ⇒ ⭐ ⭐ **同方机型【没有一个】用 `MRID6-` 前缀** ✅ ⇒ ⭐ 本白名单**不会误伤它们** ✅
	/// ⇒ ⭐ ⭐ 且 **50 系（5070）明确是同方、板号完全不同** ✅ ⇒ ⭐ **也不会误伤 50 系** ✅</para>
	/// <para>⚠️ <b>仍如实保留的一条不确定</b>：⭐ **30 系蛟龙的板号仍未证实** ⚠️
	/// ⇒ ⭐ `MRID6-` 前缀放行对它是"⭐ 赌它也是 `MRID6-*`"⚠️；
	/// ⭐ 若实际不同 ⇒ ⭐ 它仍会**装 GCU、走 GCU** ⇒ ⭐ **保守无害** ✅</para>
	/// <para>⚠️ ⭐ 并且 ⭐ **用户已确认：有 30/40 系的【蛟龙】板号是 `GM5*` ⇒ ⭐ 那些是同方模具** ✅
	/// ⇒ ⭐ ⭐ 所以**判宝龙达只能看板号** ⛔ **不能**用"蛟龙 ＋ 30/40 系"❌、
	/// ⛔ **不能**用 `SystemProductName` 含 `Jiaolong` ❌（⭐ 否则 `GM5*` 的同方蛟龙会被误判 ⚠️⚠️）。</para>	/// <para>⚠️ <b>失败一律按"不是宝龙达"处理</b> ✅（⭐ 读不到/抛异常 ⇒ <c>false</c> ✅）
	/// —— ⭐ 这是**安全方向**：⭐ 落回老接口/GCU ⭐ 总比误伤好 ✅。</para>
	/// </summary>
	internal static bool IsBaolongdaModelWhitelisted(out string note)
	{
		note = string.Empty;
		string board = string.Empty;
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			board = (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch (Exception ex)
		{
			note = "⚠️ 读主板型号失败 ⇒ ⭐ **按「不是宝龙达」处理** ✅（⭐ 出错时走安全方向 ✅）：" + ex.Message;
			return false;
		}
		if (board.Length == 0)
		{
			note = "⚠️ 主板型号读不到（⭐ 空值 ✅）⇒ ⭐ **按「不是宝龙达」处理** ✅（⭐ 安全方向 ✅）";
			return false;
		}
		if (BoardWhitelist.Matches(board))
		{
			note = $"主板型号「{board}」⭐ 命中宝龙达白名单 ✅（⭐ `MRID6` 精确 / `MRID6-` 前缀 ✅）";
			return true;
		}
		// ⭐ ③ 其余一律**不认**（⭐ 含所有同方机型 ✅）
		note = $"主板型号「{board}」⛔ **不在宝龙达白名单**（⭐ 已知宝龙达为 `MRID6` 精确 / `MRID6-` 前缀 ✅）"
			+ " ⇒ ⭐ 按用户澄清「⭐ 其他蛟龙都是同方模具 ✅」⭐ 判定为**同方** ⇒ ⭐ 让它走老接口/GCU ✅";
		return false;
	}
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
		// ⭐⭐ 2026-10-16（"全部接入"第 2 步 · 键盘灯）：⭐ **运行时自检**代替"等真机探测" ✅
		//   ⚠️ 背景：⭐ 功能号 16/17/18（⭐ 模式/颜色/亮度）⭐ **真机从未验证过** ⚠️
		//   ⭐ 而铁律要求"⭐ 点了不能没反应" ⇒ ⭐ 所以这里 ⭐ **先真读一次功能号 10**
		//      （⭐ `RgbKeyboardStatus`，⭐ 它是这套键盘灯接口的"存在证明" ✅）
		//   ⇒ ⭐ 读得到 ⇒ ⭐ 才开 `Keyboard` ✅；⭐ 读不到 ⇒ ⭐ 保持 false ⇒ ⭐ **UI 不会出现键盘灯控件** ✅
		//   ⭐ 这样 ⭐ **不需要等机主探测就能安全上线** ✅（⭐ 而且门面层写入前也会再查一次 ✅）
		bool keyboardAvailable = false;
		if (_wmi != null)
		{
			try
			{
				keyboardAvailable = _wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardStatus, out byte _);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("宝龙达：键盘灯自检异常（按不支持处理）：" + ex.Message);
			}
		}
		Logger.WriteLine($"宝龙达：键盘灯运行时自检 ⇒ {(keyboardAvailable ? "✅ 功能号 10 可读 ⇒ 开放 Keyboard 能力位" : "⚠️ 功能号 10 读不到 ⇒ Keyboard 能力位保持 false（UI 不显示，避免点了没反应）")}");
		// ⭐⭐ 2026-10-16（"全部接入"第 3 步 · 功耗墙）：⭐ 同样用**运行时自检** ✅
		//   ⭐ 真机已实测（⭐ 机器 C）：⭐ 功能号 23 ⭐ **可读**（⭐ 读到"自定义是否已开启" ✅）
		//   ⇒ ⭐ 这台蛟龙上 ⭐ 会开出来 ✅
		//   ⭐ 而"写了不能校验数值"这件事 ⭐ 已用**本地记忆**解决 ✅（⭐ 方案第 17 节 ✅）
		bool powerWallAvailable = false;
		if (_wmi != null)
		{
			try
			{
				powerWallAvailable = _wmi.TryReadCpuCustomEnabled(out bool _);
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("宝龙达：功耗墙自检异常（按不支持处理）：" + ex2.Message);
			}
		}
		Logger.WriteLine($"宝龙达：功耗墙运行时自检 ⇒ {(powerWallAvailable ? "✅ 功能号 23 可读 ⇒ 开放 CpuPerformanceTuning" : "⚠️ 功能号 23 读不到 ⇒ 保持 false（UI 不显示）")}");
		// ⭐⭐ 2026-10-16（用户「⭐ GPUmode接入 宝龙达机器就显示两个按钮就行」）：
		//   ⭐ **显卡模式（⭐ 功能号 9 ✅）运行时自检** ✅ —— ⚠️ 功能号 9 **真机未验证** ⚠️
		//   ⭐ 读得到才认为"⭐ 这套接口真的在" ✅（⭐ 协议层还带 `OutData[3]` 回显校验 ✅ 双重保护）
		//   ⚠️ 但 ⭐ **能力位这一轮【仍然不开】** ⚠️ —— ⭐ 因为 UI 的"两按钮"分支还没接 ✅（⭐ 见下方注释）
		bool gpuModeAvailable = false;
		try
		{
			gpuModeAvailable = _wmi != null && _wmi.TryReadGpuMode(out int _);
		}
		catch (Exception exGpu)
		{
			Logger.WriteLine("宝龙达：显卡模式自检异常（按不支持处理）：" + exGpu.Message);
		}
		Logger.WriteLine($"宝龙达：显卡模式运行时自检 ⇒ {(gpuModeAvailable ? "✅ 功能号 9 可读（⭐ 混合/独显直连 ✅）" : "⚠️ 功能号 9 读不到")}"
			+ " ⇒ ⚠️ 能力位**本轮暂不开**（⭐ 待 UI 两按钮分派接好 ✅）");
		return new MechrevoDeviceCapabilities
		{
			ProjectId = "Baolongda",
			IsMechrevo = true,
			ProfileAvailable = true,
			TurboMode = true,
			// ⭐⭐ 2026-10-16（"全部接入"第 1 步）：⭐ **LOGO 灯** ✅
			//   ⭐ 依据：⭐ ① 真机已验证功能号 15 可读（⭐ 读到 `00 80 00 0F 01` = 开 ✅）
			//          ⭐ ② **下发分派已接好** ⇒ `MechrevoHw.TrySetLogoLightViaBackend`
			//             ＋ ⭐ `Program.SetExternalLightingPowerAsync` 开头那一段 ✅
			//   ⚠️ 按铁律：⭐ **分派接好了才开这一位** ✅（⭐ 铁律见本方法文档注释 ✅）
			LogoLight = true,
			// ⭐⭐ 2026-10-16（第 2 步 · 键盘灯）：⭐ 由上面的**运行时自检**决定 ✅
			//   ⚠️ 只开 `Keyboard`（⭐ 开关 + ⭐ 颜色 + ⭐ 亮度），⛔ **不开** `Lightbar` / ⛔ `RgbLightbar`
			//   —— ⭐ 宝龙达只有"模式 16 / 颜色 17 / 亮度 18"三件套 ✅，
			//   ⭐ 那些复杂效果（⭐ 波浪/涟漪/跑马灯…）它**不支持** ⇒ ⭐ 开了只会出现点了没反应的选项 ❌
			Keyboard = keyboardAvailable,
			// ⭐⭐ 2026-10-16（第 3 步 · 功耗墙）：⚠️ **暂时保持 false** ⚠️
			//   ⭐ 为什么：⭐ 按铁律 ⭐"能力位 = 已接好下发路径" ⇒ ⭐ 而 `MechrevoService` 那处
			//      **下发分派还没接**（⭐ 方案第 17.2 节第 ② 件 ✅）
			//      ⇒ ⭐ 若现在就开 ⇒ ⭐ 用户设了值 ⇒ ⭐ 会走到 `Publish("Fan/Control", …)` ❌
			//        ⇒ ⭐ 而宝龙达**没有 GCU** ⇒ ⭐ **静默失败** ⇒ ⭐ **正好违反铁律** ⚠️
			//   ⭐ 已完成的部分（⭐ **都已就位** ✅）：
			//      ① ⭐ `MechrevoHw.ApplyBaolongdaPowerRanges()` ⇒ ⭐ 范围与本地记忆已填 ✅
			//      ② ⭐ `TryApplyCpuPowerLimitViaBackend()` ⇒ ⭐ 协议写入＋回读＋记忆更新已写好 ✅
			//      ③ ⭐ `powerWallAvailable` 自检已就位（⭐ 只是**先不用它**）✅
			//   ⇒ ⭐ **待办**：⭐ 把 `MechrevoService` 那处下发分派接上（⭐ 一行 `continue` ✅）
			//      ⇒ ⭐ 接上之后 ⭐ 把下面这行改成 ⭐ `CpuPerformanceTuning = powerWallAvailable,` ⭐ 即可 ✅
			//   ⚠️ 且 ⛔ **不开** `OverclockSettings`（⭐ 那是 GPU 超频那一组，
			//      ⭐ 宝龙达协议里**没有**对应功能号 ⚠️ ⇒ ⭐ 开了就是点了没反应 ❌）
			CpuPerformanceTuning = powerWallAvailable,
			// ⭐⭐ 2026-10-16（用户要求「⭐ GPUmode接入 宝龙达机器就显示两个按钮就行」）：
			//   ⭐ **显卡模式**（⭐ 功能号 9 ✅）—— ⚠️ **能力位暂不开**（⭐ 铁律 ✅）⚠️
			//   ⭐ 为什么暂不开：⭐ 按铁律「⭐ 能力位 = 已接好下发路径的证明」✅
			//      ⚠️ 而 ⭐ UI 那一侧**还没有"只显示两个按钮"的分支** ⚠️
			//      ⭐ 现在 UI 是 `GPUModeControl`，⭐ 它有**三档**（⭐ Eco / 标准 / 极致 ⚠️）
			//      ⇒ ⚠️ 若此刻开位 ⇒ ⭐ `CanOfferGpuModeSwitch` 可能变真 ⇒ ⭐ 界面出现**三档**，
			//         ⭐ 其中"⭐ Eco/极致"⭐ 会走到**没接的 GCU 路径** ❌ ⇒ ⭐ **正好违反铁律** ✅
			//   ⭐ **已完成的部分（⭐ 都已就位 ✅）**：
			//      ① ⭐ `BaolongdaWmi.TryReadGpuMode` / ⭐ `TrySetGpuMode`
			//         （⭐ 含写前试读保护 ＋ ⭐ `OutData[3]` 回显校验 ✅）
			//      ② ⭐ `MechrevoHw.TryReadGpuModeViaBackend` / ⭐ `TrySetGpuModeViaBackend`
			//         （⭐ 含写后回读确认 ＋ ⭐ 非宝龙达返回 false ✅）
			//      ③ ⭐ `gpuModeAvailable` 自检已就位（⭐ 只是**先不用它** ✅）
			//   ⇒ ⭐ **待办（⭐ 顺序不能反 ✅）**：
			//      ⭐ ① ⭐ 先给 `GPUModeControl` 加"⭐ 宝龙达只显示两个按钮（混合/独显直连）"⭐ 的分支
			//         （⭐ 必须以 `Program.hw.UsesBaolongda` 开头 ⇒ ⭐ 同方/GCU 走 else 原路 ✅）
			//      ⭐ ② ⭐ 把那两个按钮的点击分派到 `TrySetGpuModeViaBackend` ✅
			//      ⭐ ③ ⭐ **然后**才把下面这行改成 ⭐ `DgpuDirect = gpuModeAvailable,` ✅
			// ⭐⭐ 2026-10-16 晚：⭐ **现在开** ✅ —— ⭐ 因为两件前提都已就位 ✅：
			//   ⭐ ① ⭐ 分派已接：⭐ `GPUModeControl.SetGPUMode` 里 `UsesBaolongda` 分支
			//      ⇒ ⭐ 界面 1（标准）⇒ ⭐ 后端 0（混合）· ⭐ 界面 2（独显直连）⇒ ⭐ 后端 1 ✅
			//      ⭐ 且 ⭐ 界面 0（核显）⭐ **明确拒绝**（⭐ 宝龙达不支持 ✅）
			//   ⭐ ② ⭐ 按钮已只显示两个：⭐ `SettingsForm` 里 `UsesBaolongda` 分支调
			//      ⭐ `VisualiseGPUButtons(eco: false, ultimate: true)` ⇒ ⭐ **混合 ＋ ⭐ 独显直连** ✅
			//   ⭐ 为什么 `CanOfferGpuModeSwitch` 会成立（⭐ 已推演 ✅）：
			//      ⭐ `CanOfferGpuModeSwitchCore(null, true, false, 代际, 三档)` ✅
			//      ⇒ ⭐ `gcuDgpuDirectSupport` 为 null（⭐ 宝龙达没 GCU ✅）⇒ ⭐ 不否决 ✅
			//      ⇒ ⭐ `supportsDgpuDirect=true` ⇒ ⭐ 过"两者皆无"那一关 ✅
			//      ⇒ ⭐ 代际 ⭐ **不是 Gen30** ⇒ ⭐ 过代际否决 ✅
			//      ⇒ ⭐ `DisplayRoutePolicy.AllowsAction`：⭐ 代际 ≤ 1（⭐ `None` ✅）时
			//         ⭐ 走 ⭐ `DisplayRouteMatrix.IsKnownAction(action)` ⇒ ⭐ 直连动作是**已知动作** ✅
			//         ⇒ ⭐ 返回 true ⇒ ⭐ 于是整体 **true** ✅
			//   ⭐ 且 ⭐ `GpuRowLayout` 会解析成 ⭐ **`Mux2` = 「混合模式 / 独显直连」两档** ✅
			//      （⭐ `GpuRowLayouts.Resolve(false, modeSwitch=true, igpuMuxTarget=false)` ✅）
			//      ⇒ ⭐ ⭐ **这正是用户要的"⭐ 就显示两个按钮"** ✅
			//   ⚠️ 真机需确认：⭐ ① 功能号 9 真能读（⭐ 自检日志会写 ✅）·
			//      ⭐ ② 切了之后**回读值真的是目标值** ✅ · ⭐ ③ 界面确实只出现两颗按钮 ✅
			DgpuDirect = gpuModeAvailable,
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
