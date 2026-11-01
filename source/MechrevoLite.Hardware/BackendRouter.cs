using System;
using System.Collections.Generic;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 路由结果：命中的规则序号/说明 + 选中的后端 + 当时的机型事实。
/// <para>每次路由都会落一行可解释日志：<c>[强化机型分流] backend=… | machine=… | rule=#n … | decl=…</c></para>
/// </summary>
internal sealed record BackendRouteDecision(int RuleIndex, string Rule, MechrevoBackendKind Backend, MachineIdentity Identity, bool DeclarationPassed);

/// <summary>
/// 后端能力声明（第 3 步）：每个后端声明「需要哪些事实 + 支持哪些机型类别」。
/// <para>路由统一判断：选中的后端若声明不支持当前机型，则落回兜底规则并记日志（不静默换后端）。</para>
/// </summary>
internal sealed record BackendDeclaration(
	MechrevoBackendKind Kind,
	string DisplayName,
	string[] RequiresFacts,
	Func<MachineIdentity, bool> Supports,
	string Note);

/// <summary>
/// 第二层「路由」：有序声明式规则表，首条命中即选后端；未命中落到兜底规则。
/// <para>规则只读 <see cref="MachineIdentity" />，不做后端探测、不“试一个失败再换一个”。</para>
/// <para>新增机型/后端 = 加一条规则、一个事实或一个能力声明，不改控制流。</para>
/// <para>⭐ 2026-10-27（task-41 机型分流审计）：按设计文档
/// <c>docs/notes/后端匹配-声明式架构-设计-20261020.md</c> 补齐
/// <b>LegacyMifs 规则</b>（原表只有 Baolongda / Gcu，无 GCU 的老接口机型会被声明成 GCU），
/// 并把 <c>gen</c> 规则收紧为「代际 + 已装 GCU」（代际本身不能证明 GCU 通路存在）。</para>
/// </summary>
internal static class BackendRouter
{
	internal sealed record Rule(int Index, string Description, Func<MachineIdentity, bool> When, MechrevoBackendKind Backend);

	/// <summary>规则表（顺序 = 优先级；首条命中即选中）。</summary>
	private static readonly Rule[] Rules =
	{
		// ① 板号白名单是**硬机型证据**：即使装了 GCU 服务也先交给宝龙达后端
		//    （否则会被 GCU 抢先，表现为「GCU 未连接」；见设计文档 §2 蛟龙事故）。
		new Rule(1, "board in Baolongda whitelist -> Baolongda", identity => identity.BaolongdaWhitelisted, MechrevoBackendKind.Baolongda),
		// ② 设计文档 §1 规则 2：代际只能"佐证"，必须**已装 GCU 服务**才声明 GCU
		//    （原实现 gen 30/40/50 单独即声明 GCU ⇒ 无 GCU 的老接口机型被误声明）。
		new Rule(2, "gpu gen 30/40/50 && GCU service installed -> Gcu", identity => identity.GcuServiceInstalled && IsGcuGeneration(identity.GpuGeneration), MechrevoBackendKind.Gcu),
		// ③ 设计文档 §1 规则 3：装了 GCU 服务（代际未知也走这一条）
		new Rule(3, "GCU service installed -> Gcu", identity => identity.GcuServiceInstalled, MechrevoBackendKind.Gcu),
		// ④ 设计文档 §1 规则 4：MICommonInterface 存在（且 GUID 与老同方一致）+ 未装 GCU ⇒ 老接口后端
		//    ⚠️ 必须排在"装了 GCU"之后：MICommonInterface 是同方通用类，新机型也可能带（设计文档 §2）。
		new Rule(4, "MICommonInterface(legacy GUID) && no GCU service -> LegacyMifs",
			identity => identity.MifsLegacyCompatible && !identity.GcuServiceInstalled, MechrevoBackendKind.LegacyMifs)
	};

	// 后端能力声明（第 3 步）：事实需求 + 支持的机型类别
	private static readonly BackendDeclaration[] Declarations =
	{
		new BackendDeclaration(MechrevoBackendKind.Baolongda, "BaolongDa",
			new[] { "board(product)", "boardWhitelist" },
			identity => identity.BaolongdaWhitelisted,
			"仅限板号白名单机型（MRID6 / MRID6-*）；无 GCU，走宝龙达 WMI"),
		new BackendDeclaration(MechrevoBackendKind.Gcu, "GCU",
			new[] { "gcuServiceInstalled", "gpuGeneration" },
			identity => !identity.BaolongdaWhitelisted,
			"同方机型；宝龙达白名单机型不走 GCU。未识别任何机型时也落到这里（未连接则如实提示）"),
		new BackendDeclaration(MechrevoBackendKind.LegacyMifs, "LegacyMifs",
			new[] { "mifsClass" },
			identity => !identity.BaolongdaWhitelisted && identity.MifsLegacyCompatible && !identity.GcuServiceInstalled,
			"老同方 MICommonInterface（GUID 与 LegacyMifsWmi 一致）且未装 GCU 服务；内置性能档、不走 MQTT")
	};

	/// <summary>兜底后端（设计文档 §1 规则 5）：未识别机型也落到 GCU，未连接则如实提示。</summary>
	private const MechrevoBackendKind Fallback = MechrevoBackendKind.Gcu;

	// ⭐ 运行期最近一次路由结果（供 [gate] 机型分类日志 / 诊断包 / ActiveRoute 对比复用同一份事实）。
	private static BackendRouteDecision? _lastDecision;

	/// <summary>
	/// ⭐ 本进程最近一次路由结果（未路由过 = null）。
	/// <para>日志、诊断包、<see cref="MechrevoHw.RouteDeclarationMatchesActual" /> 都读它，
	/// 保证「运行判定」与「诊断重算」用的是同一份 <see cref="MachineIdentity" /> 实例（设计文档 §3.4）。</para>
	/// </summary>
	internal static BackendRouteDecision? LastDecision => System.Threading.Volatile.Read(ref _lastDecision);

	internal static BackendDeclaration DeclarationFor(MechrevoBackendKind kind)
	{
		for (int i = 0; i < Declarations.Length; i++)
		{
			if (Declarations[i].Kind == kind)
			{
				return Declarations[i];
			}
		}
		return null;
	}

	internal static string DescribeDeclarations()
	{
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < Declarations.Length; i++)
		{
			BackendDeclaration declaration = Declarations[i];
			sb.AppendLine("  - " + declaration.DisplayName + "：需要[" + string.Join(", ", declaration.RequiresFacts) + "] · " + declaration.Note);
		}
		return sb.ToString().TrimEnd();
	}

	/// <summary>路由结果的一行可解释描述（日志 / 诊断共用，避免两处格式漂移）。</summary>
	internal static string DescribeDecision(BackendRouteDecision decision)
	{
		if (decision == null)
		{
			return "(未运行路由)";
		}
		BackendDeclaration declaration = DeclarationFor(decision.Backend);
		return "backend=" + decision.Backend
			+ " | machine=" + decision.Identity.Summary
			+ " | rule=#" + decision.RuleIndex + " " + decision.Rule
			+ " | decl=" + (declaration == null ? "(无)" : declaration.DisplayName + (decision.DeclarationPassed ? "=pass" : "=fail"));
	}

	internal static BackendRouteDecision Decide(MachineIdentity identity)
	{
		if (identity == null)
		{
			throw new ArgumentNullException("identity");
		}
		for (int i = 0; i < Rules.Length; i++)
		{
			Rule rule = Rules[i];
			if (!rule.When(identity))
			{
				continue;
			}
			bool declarationPassed = DeclarationFor(rule.Backend)?.Supports(identity) ?? true;
			if (declarationPassed)
			{
				return new BackendRouteDecision(rule.Index, rule.Description, rule.Backend, identity, DeclarationPassed: true);
			}
			// 声明不支持 ⇒ 不静默换后端，记下原因并落到兜底规则（不是“探测失败重试”）。
			Logger.WriteLine("[强化机型分流] 规则 #" + rule.Index + " 命中 " + rule.Backend + "，但该后端声明不支持当前机型 ⇒ 落兜底规则");
			break;
		}
		return new BackendRouteDecision(Rules.Length + 1, "fallback -> GCU（未识别机型；未连接则如实提示）", Fallback, identity,
			DeclarationPassed: DeclarationFor(Fallback)?.Supports(identity) ?? true);
	}

	/// <summary>仅 UI 探针/离线预览用：把合成机型事实落到 LastDecision，让 UI 按 NUC 渲染；不触碰真实后端。</summary>
	internal static void SetPreviewDecision(MachineIdentity identity)
	{
		System.Threading.Volatile.Write(ref _lastDecision,
			new BackendRouteDecision(0, "ui-probe -> Gcu", MechrevoBackendKind.Gcu, identity, DeclarationPassed: true));
	}

	internal static BackendRouteDecision DecideAndLog()
	{
		BackendRouteDecision decision = Decide(MachineIdentity.Collect());
		System.Threading.Volatile.Write(ref _lastDecision, decision);
		Logger.WriteLine("[强化机型分流] " + DescribeDecision(decision));
		return decision;
	}

	/// <summary>后端枚举 → 连接侧路由枚举（声明/实际的对比用；不改变 <see cref="MechrevoHw.ActiveRoute" /> 的语义）。</summary>
	internal static BackendRoute ToRoute(MechrevoBackendKind kind)
	{
		return kind switch
		{
			MechrevoBackendKind.Gcu => BackendRoute.Gcu,
			MechrevoBackendKind.Baolongda => BackendRoute.Baolongda,
			MechrevoBackendKind.LegacyMifs => BackendRoute.LegacyMifs,
			_ => BackendRoute.None
		};
	}

	private static bool IsGcuGeneration(string generation)
	{
		return string.Equals(generation, "Gen30", StringComparison.Ordinal)
			|| string.Equals(generation, "Gen40", StringComparison.Ordinal)
			|| string.Equals(generation, "Gen50", StringComparison.Ordinal);
	}

	/// <summary>
	/// ⭐ 离线自测（只读、零硬件）：用合成 <see cref="MachineIdentity" /> 跑规则矩阵，断言
	/// 每条设计规则（含新增 LegacyMifs / 代际收紧 / 兜底）都命中预期后端。
	/// <para>输出格式：首行 <c>[机型分流自测] PASS=n FAIL=m</c>，逐行 ✓/✗。诊断包与探针都会调用。</para>
	/// <para>⛔ 不读真实硬件、不写任何东西；纯函数。</para>
	/// </summary>
	internal static string RunSelfTest()
	{
		List<string> pass = new List<string>();
		List<string> fail = new List<string>();

		// ① 宝龙达白名单：GCU 服务同时存在也必须优先宝龙达（设计文档 §2 蛟龙事故）
		Check("白名单机型（同时装了 GCU）⇒ Baolongda", MechrevoBackendKind.Baolongda, 1,
			MachineIdentity.ForTest(board: "MRID6-23", whitelisted: true, generation: "Gen40", gcuInstalled: true, mifsClass: true), pass, fail);
		// ② 40 系 + 已装 GCU ⇒ Gcu（代际佐证）
		Check("Gen40 + 已装 GCU ⇒ Gcu", MechrevoBackendKind.Gcu, 2,
			MachineIdentity.ForTest(board: "GM5HG0A", generation: "Gen40", gcuInstalled: true), pass, fail);
		// ③ 代际未知但已装 GCU ⇒ Gcu（规则 3）
		Check("代际未知 + 已装 GCU ⇒ Gcu", MechrevoBackendKind.Gcu, 3,
			MachineIdentity.ForTest(board: "GM5HG0A", generation: "Unknown", gcuInstalled: true), pass, fail);
		// ④ 老接口：MICommonInterface(legacy GUID) + 未装 GCU ⇒ LegacyMifs（本轮新增规则）
		Check("MICommonInterface + 未装 GCU ⇒ LegacyMifs", MechrevoBackendKind.LegacyMifs, 4,
			MachineIdentity.ForTest(board: "GM6IX8B", generation: "Gen30", gcuInstalled: false, mifsClass: true), pass, fail);
		// ⑤ 装了 GCU 时 MICommonInterface 在也不能抢老接口（设计文档 §2：同方通用类新机型也可能带）
		Check("MICommonInterface + 已装 GCU ⇒ Gcu（老接口不得抢）", MechrevoBackendKind.Gcu, 3,
			MachineIdentity.ForTest(board: "GM5HG0A", generation: "Unknown", gcuInstalled: true, mifsClass: true), pass, fail);
		// ⑥ 只有代际、没有 GCU、没有 MIFS ⇒ 不声明 GCU 规则，落到兜底 GCU（行为=未连接如实提示）
		Check("仅代际、无 GCU/MIFS ⇒ 兜底 Gcu", MechrevoBackendKind.Gcu, 5,
			MachineIdentity.ForTest(board: "GM5HG0A", generation: "Gen40", gcuInstalled: false), pass, fail);
		// ⑦ 什么都没识别到 ⇒ 兜底 Gcu（不误判成宝龙达/老接口）
		Check("无任何事实 ⇒ 兜底 Gcu", MechrevoBackendKind.Gcu, 5,
			MachineIdentity.ForTest(), pass, fail);
		// ⑧ 白名单但 MICommonInterface 缺失：路由仍按机型证据声明 Baolongda（探测失败由候选循环兜底并记日志）
		Check("白名单 + MIFS 缺失 ⇒ 仍声明 Baolongda", MechrevoBackendKind.Baolongda, 1,
			MachineIdentity.ForTest(board: "MRID6", whitelisted: true, generation: "Gen30", gcuInstalled: false, mifsClass: false), pass, fail);
		// ⑨ 声明一致性：所有命中规则的声明都应为 pass（能力声明是第二道保险）
		CheckDeclaration(MachineIdentity.ForTest(board: "MRID6-23", whitelisted: true, mifsClass: true), pass, fail);
		CheckDeclaration(MachineIdentity.ForTest(board: "GM5HG0A", generation: "Gen40", gcuInstalled: true), pass, fail);
		CheckDeclaration(MachineIdentity.ForTest(board: "GM6IX8B", generation: "Gen30", mifsClass: true), pass, fail);

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[机型分流自测] PASS=" + pass.Count + " FAIL=" + fail.Count);
		for (int i = 0; i < pass.Count; i++)
		{
			sb.AppendLine("  ✓ " + pass[i]);
		}
		for (int j = 0; j < fail.Count; j++)
		{
			sb.AppendLine("  ✗ " + fail[j]);
		}
		return sb.ToString().TrimEnd();
	}

	private static void Check(string scenario, MechrevoBackendKind expectedBackend, int expectedRule, MachineIdentity identity, List<string> pass, List<string> fail)
	{
		BackendRouteDecision decision = Decide(identity);
		bool ok = decision.Backend == expectedBackend && decision.RuleIndex == expectedRule && decision.DeclarationPassed;
		string line = scenario + " ⇒ 实际 " + decision.Backend + "(rule=#" + decision.RuleIndex + ", decl=" + (decision.DeclarationPassed ? "pass" : "fail") + ")";
		if (ok)
		{
			pass.Add(line + " · 期望 " + expectedBackend + "(rule=#" + expectedRule + ")");
		}
		else
		{
			fail.Add(line + " · 期望 " + expectedBackend + "(rule=#" + expectedRule + ")");
		}
	}

	private static void CheckDeclaration(MachineIdentity identity, List<string> pass, List<string> fail)
	{
		BackendRouteDecision decision = Decide(identity);
		if (decision.DeclarationPassed)
		{
			pass.Add("能力声明 pass：" + decision.Backend + " rule=#" + decision.RuleIndex);
		}
		else
		{
			fail.Add("能力声明 fail：" + decision.Backend + " rule=#" + decision.RuleIndex + " machine=" + identity.Summary);
		}
	}
}
