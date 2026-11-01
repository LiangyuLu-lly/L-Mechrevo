// 2026-10-15（C-1 第 2 步「后端化」）：把「本机走哪个后端」从写死的「有 GCU / 没 GCU」抽成**有序候选表**。
// 用途：MechrevoHw 启动时按 MechrevoBackendOrder.Candidates 顺序**逐个探测**，第一个可用的即为 ActiveBackend。
// ⭐ 以后新增后端（例如宝龙达模具那套另行准备的 ACPI 驱动）**只需加一项**：
//     ① MechrevoBackendKind 加枚举值；② 写一个「返回非 null 即成功」的探测委托；③ 加进 Candidates 数组。
//     ⛔ MechrevoHw 里不用改任何 else/if —— 探测循环是数据驱动的（见 MechrevoHw.ConnectAsync）。
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Text;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>硬件后端 —— 谁在替我们读写这台机器的性能档/风扇。</summary>
internal enum MechrevoBackendKind
{
	/// <summary>还没探测过，或一个后端都没探到（此时退回 GCU 通路，行为与改动前一致）。</summary>
	None = 0,

	/// <summary>GCU 服务（MQTT 127.0.0.1:13688，走 Fan/Control 等话题）。新机型默认走这条。</summary>
	Gcu = 1,

	/// <summary>老机型 WMI 接口 <c>root\WMI:MICommonInterface</c>（内置性能档，完全不经过 GCU/MQTT）。</summary>
	LegacyMifs = 2,

	/// <summary>
	/// 宝龙达（Baolongda）模具的机械革命机型 —— ⭐ 主要目标 = **蛟龙16 的 2022 / 2023 / 2024 年款**。
	/// <para>它**不走同方的 GCU 服务**（用户 2026-10-15：「ACPI 到时候会另外准备一套给宝龙达」）。</para>
	/// <para>⭐ 2026-10-16（0.291.9 beta22.9）：本轮**开始适配**，先把骨架放好 ——
	/// 见 <see cref="BaolongdaBackend" />（⚠️ 现在只有骨架、不接真实协议；默认**探不中**，⛔ 不影响现有机型）。</para>
	/// </summary>
	Baolongda = 3,
}

/// <summary>后端探测表里的一项。探测失败一律返回 <c>null</c>（⛔ 异常由探测循环吞掉，不许让启动崩）。</summary>
internal readonly struct MechrevoBackendProbe
{
	internal MechrevoBackendProbe(MechrevoBackendKind kind, Func<object> tryCreate)
	{
		Kind = kind;
		TryCreate = tryCreate;
	}

	/// <summary>探到了就是哪一个后端。</summary>
	internal MechrevoBackendKind Kind { get; }

	/// <summary>
	/// 建后端对象：**返回非 null 即成功**（对象本身自证可用 —— 例如 <c>LegacyMifsWmi.TryCreate()</c>
	/// 内部已经做过 2 s 超时的实例枚举并试读了一次档位）；返回 null 或抛异常都算「这个后端不可用」，
	/// 由调用方继续试下一个。
	/// </summary>
	internal Func<object> TryCreate { get; }
}

/// <summary>
/// 后端**探测顺序**表 —— ⭐ 新后端唯一需要改动的地方（加一项即可）。
/// 当前只有 2 个后端：**先探老机型 MICommonInterface，探不到再落到 GCU**。
/// 次序理由：① 老接口「在不在」是**确定性**判断（WMI 类 + GUID 校验 + 2 s 超时），不会误判；
/// ② 老机型上根本没有 GCU 服务，先探 GCU 会白等一次连接超时；③ 反过来在新机型上探老接口
/// 只是一次「类不存在」的快速失败（<c>ManagementStatus.NotFound / InvalidClass</c> 被直接吞掉，连日志都不打）。
/// </summary>
internal static class MechrevoBackendOrder
{
	/// <summary>
	/// 后端探测顺序。⭐⭐ 2026-10-15 晚（用户反馈「**有 50 系机主说 GCU 连不上**」后改的）：
	/// <para>⚠️ 原顺序是「老接口 → GCU」，而 GCU 那一项用的是**无条件非 null 的哨兵**
	/// （<c>MechrevoHw.GcuBackendMarker</c>）⇒ 只要老接口探到就**永远轮不到 GCU**。而
	/// <c>root\WMI:MICommonInterface</c> 是**同方通用接口**，**新机型（含 50 系）也可能带** ⇒
	/// 那台机器会被判成老机型 ⇒ <b>根本不去连 GCU</b> ⇒ 机主看到「GCU 连不上」❌（今天 C-1 引入的新路径）。</para>
	/// <para>现改为：**「装了 GCU 服务」⇒ 优先 GCU**；没装才试老接口；两者都没有仍落到 GCU（与改动前一致，
	/// 表现为「GCU 未连接」而不是"选错后端"）。判据 <see cref="MechrevoHw.IsGcuServiceInstalled" /> 只看
	/// 服务注册表键 / GCU 是否留下过 ItemSupport 快照 —— 都是**只读**、不启服务、不发包。</para>
	/// </summary>
	internal static readonly MechrevoBackendProbe[] Candidates = new MechrevoBackendProbe[4]
	{
		// 板号命中宝龙达白名单 ⇒ 即使装了 GCU 服务也先交给宝龙达后端（否则会被 GCU 抢先，表现为「GCU 未连接」）
		new MechrevoBackendProbe(MechrevoBackendKind.Gcu, () => (MechrevoHw.IsGcuServiceInstalled && !BaolongdaBackend.IsBaolongdaModelWhitelisted(out _)) ? MechrevoHw.GcuBackendMarker : null),
		// ⭐⭐ 2026-10-16（0.291.9 · 宝龙达适配第 3 步）：⭐ **提到老接口【之前】** ✅
		//   ⭐ 理由（⭐ 真机实测得出 ⚠️）：⭐ 宝龙达机型的 `MICommonInterface` **类 GUID 与老同方那份完全一致**
		//     （⭐ `b60bfb48-3e5b-49e4-a0e9-8cffe1b3434b` ✅）⇒ ⭐ 老接口的 GUID 校验**也会通过** ⚠️
		//     ⇒ ⭐ 若把宝龙达放在后面，⭐ 它**永远轮不到**，⭐ 新接的风扇/温度/灯效就都用不上 ✅
		//   ⚠️ 不会误伤同方（⭐ **真正的保证在 `BaolongdaBackend.TryCreate` 里那道【机型白名单】硬闸门** ✅）：
		//      ⭐ 本候选一旦探到 `MICommonInterface`，⭐ `BaolongdaBackend.TryCreate()` **会先查板号白名单**
		//      （⭐ `MRID6` 精确 / `MRID6-` 前缀 ✅）⇒ ⭐ 不在白名单 ⇒ ⭐ **立即弃掉并返回 null** ✅
		//      ⇒ ⭐ 自动落到下一格 `LegacyMifs` / `GCU` ⇒ ⭐ **行为与改动前一致** ✅
		//   ⚠️⚠️ 2026-10-17（30 系宝龙达整机失效修复后**必须同步更正本段**）：
		//      旧说法「`BaolongdaWmi.TryCreate` 要求真读一次 **CPU 温度（功能号 22）** 才算活体」
		//      ⭐ **已不再是事实** ⚠️ —— 该判据被证明过严：**30 系宝龙达（`MRID6`·6800H+3070Ti）
		//      实测读不到功能号 22**（官方 30 系控制台也不用它）⇒ 一个可选功能读不到却让**整机退回 legacy** ❌。
		//      现在是 ⭐ **探测与能力位解耦** ✅：⭐ 板号白名单命中 ⇒ 必接管（功能号 22 只记日志）；
		//      ⭐ 板号未命中 ⇒ 多判据任一成功（功能号 22/8/11/12/13，⭐ 见 `BaolongdaWmi.ProbeLiveness` ✅）。
		//      ⇒ ⭐ 「不误伤同方」**不靠活体判据**，⭐ 靠**白名单** ✅（⭐ 这是本次更正的核心 ✅）。
		//   ⚠️ 也不能放在 GCU **之前**：⭐ `IsGcuServiceInstalled` 为真说明这台机该走 GCU（⭐ 见上面那段注释 ⚠️）。
		new MechrevoBackendProbe(MechrevoBackendKind.Baolongda, () => BaolongdaBackend.TryCreate()),
		new MechrevoBackendProbe(MechrevoBackendKind.LegacyMifs, () => LegacyMifsWmi.TryCreate()),
		new MechrevoBackendProbe(MechrevoBackendKind.Gcu, () => MechrevoHw.GcuBackendMarker),
	};

	/// <summary>
	/// 按 <see cref="Candidates" /> 顺序探测，返回第一个可用的后端（<c>Kind=None / Created=null</c> = 都没探到）。
	/// ⚠️ 每个候选的异常都被吞掉（只写一行日志），⛔ 绝不外抛 —— ConnectAsync 里探后端不许把启动搞崩。
	/// ⚠️ 本方法是**同步阻塞**的（调用方已经把它包在 <c>Task.Run</c> 里），⛔ 别在 UI 线程上直接调。
	/// <para>⭐ 2026-10-27（task-41）：新增 <see cref="MechrevoBackendKind.LegacyMifs" /> 分支 ——
	/// 路由声明为老接口时**老接口排第一**（原来只有 Baolongda/GCU 两个特例，声明 LegacyMifs 会先白跑 GCU/宝龙达探测）。
	/// 「声明」只决定**尝试顺序**；探测失败仍按后面的候选兜底（不破坏已知机型），实际后端与声明的差异由
	/// <see cref="Probe" /> 统一记 <c>[强化机型分流]</c> 日志。</para>
	/// </summary>
	internal static MechrevoBackendProbe[] CandidatesFor(MechrevoBackendKind preferred)
	{
		MechrevoBackendProbe gcuGate = Candidates[0];
		MechrevoBackendProbe baolongda = Candidates[1];
		MechrevoBackendProbe legacyMifs = Candidates[2];
		MechrevoBackendProbe gcuMarker = Candidates[3];
		if (preferred == MechrevoBackendKind.Baolongda)
		{
			// 机型已证明是宝龙达：先宝龙达（内部仍有板号白名单硬闸门），再老接口，最后 GCU 哨兵。
			return new MechrevoBackendProbe[3] { baolongda, legacyMifs, gcuMarker };
		}
		if (preferred == MechrevoBackendKind.LegacyMifs)
		{
			// 路由声明 = 老接口（MICommonInterface 存在且未装 GCU）：先老接口，再按原安全顺序兜底。
			return new MechrevoBackendProbe[3] { legacyMifs, gcuGate, gcuMarker };
		}
		// GCU 优先或兜底：保持原有安全顺序（GCU 门控 → 宝龙达 → 老接口 → GCU 哨兵）。
		return new MechrevoBackendProbe[4] { gcuGate, baolongda, legacyMifs, gcuMarker };
	}

	/// <summary>
	/// ⭐ 本进程最近一次**实际**探到的后端（未探测/全失败 = <see cref="MechrevoBackendKind.None" />）。
	/// <para>用途：<c>[gate] 机型分类</c> 日志、诊断包、<see cref="MechrevoHw.RouteDeclarationMatchesActual" />
	/// 与路由声明的对比 —— 直接回答"声明的后端 vs 实际跑的后端是否一致"（设计文档 §3.4）。</para>
	/// </summary>
	internal static MechrevoBackendKind LastActualKind => (MechrevoBackendKind)System.Threading.Volatile.Read(ref _lastActualKind);

	private static int _lastActualKind;

	internal static (MechrevoBackendKind Kind, object Created) Probe()
	{
		BackendRouteDecision decision = BackendRouter.DecideAndLog();
		MechrevoBackendProbe[] candidates = CandidatesFor(decision.Backend);
		// 候选顺序留痕：排障时能看出"为什么先探了某个后端"，也与诊断包里的声明对照。
		StringBuilder order = new StringBuilder();
		for (int i = 0; i < candidates.Length; i++)
		{
			if (i > 0)
			{
				order.Append(" -> ");
			}
			order.Append(candidates[i].Kind);
		}
		Logger.WriteLine("[强化机型分流] 候选顺序=" + order + "（declared=" + decision.Backend + " · rule=#" + decision.RuleIndex + "）");
		for (int j = 0; j < candidates.Length; j++)
		{
			MechrevoBackendProbe candidate = candidates[j];
			try
			{
				object created = candidate.TryCreate();
				if (created != null)
				{
					LogDeclaredVsActual(decision, candidate.Kind);
					return (Kind: candidate.Kind, Created: created);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine($"Backend probe failed ({candidate.Kind}): " + ex.Message);
			}
		}
		LogDeclaredVsActual(decision, MechrevoBackendKind.None);
		return (Kind: MechrevoBackendKind.None, Created: null);
	}

	/// <summary>
	/// ⭐ 声明 vs 实际：一致记一行 Info；**不一致**必须记 <c>WriteError</c>（默认日志级别下也可见），
	/// 并写明"已按实际后端运行、诊断包会记录"——⛔ 不静默换后端（设计文档 §3.4）。
	/// </summary>
	private static void LogDeclaredVsActual(BackendRouteDecision decision, MechrevoBackendKind actual)
	{
		System.Threading.Volatile.Write(ref _lastActualKind, (int)actual);
		BackendRoute declaredRoute = BackendRouter.ToRoute(decision.Backend);
		BackendRoute actualRoute = BackendRouter.ToRoute(actual);
		string message = "[强化机型分流] declared=" + decision.Backend + "(rule=#" + decision.RuleIndex + ")"
			+ " · actual=" + actual
			+ " · 一致=" + (declaredRoute == actualRoute)
			+ " · machine=" + decision.Identity.Summary;
		if (declaredRoute == actualRoute)
		{
			Logger.WriteLine(message);
			return;
		}
		Logger.WriteError("[强化机型分流] ⚠️ 路由声明与实际后端不一致 ⇒ 已按实际后端运行（不静默换后端，诊断包会记录）：" + message);
	}
}
