using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 路由结果：命中的规则序号/说明 + 选中的后端 + 当时的机型事实。
/// <para>每次路由都会落一行可解释日志：<c>[router] backend=… | machine=… | rule=#n … | decl=…</c></para>
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
/// </summary>
internal static class BackendRouter
{
	internal sealed record Rule(int Index, string Description, Func<MachineIdentity, bool> When, MechrevoBackendKind Backend);

	// 规则表（顺序 = 优先级；首条命中即选中）
	private static readonly Rule[] Rules =
	{
		new Rule(1, "board in Baolongda whitelist", identity => identity.BaolongdaWhitelisted, MechrevoBackendKind.Baolongda),
		new Rule(2, "GCU service installed", identity => identity.GcuServiceInstalled, MechrevoBackendKind.Gcu),
		new Rule(3, "GPU gen 30/40/50 -> GCU", identity => string.Equals(identity.GpuGeneration, "Gen30", StringComparison.Ordinal)
			|| string.Equals(identity.GpuGeneration, "Gen40", StringComparison.Ordinal)
			|| string.Equals(identity.GpuGeneration, "Gen50", StringComparison.Ordinal), MechrevoBackendKind.Gcu),
	};

	// 后端能力声明（第 3 步）：事实需求 + 支持的机型类别
	private static readonly BackendDeclaration[] Declarations =
	{
		new BackendDeclaration(MechrevoBackendKind.Baolongda, "BaolongDa",
			new[] { "board(product)", "boardWhitelist" },
			identity => identity.BaolongdaWhitelisted,
			"仅限板号白名单机型"),
		new BackendDeclaration(MechrevoBackendKind.Gcu, "GCU",
			new[] { "gcuServiceInstalled", "gpuGeneration" },
			identity => !identity.BaolongdaWhitelisted,
			"同方机型；宝龙达白名单机型不走 GCU"),
	};

	private const MechrevoBackendKind Fallback = MechrevoBackendKind.Gcu;

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
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for (int i = 0; i < Declarations.Length; i++)
		{
			BackendDeclaration declaration = Declarations[i];
			sb.AppendLine("  - " + declaration.DisplayName + "：需要[" + string.Join(", ", declaration.RequiresFacts) + "] · " + declaration.Note);
		}
		return sb.ToString().TrimEnd();
	}

	internal static BackendRouteDecision Decide(MachineIdentity identity)
	{
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
			Logger.WriteLine("[router] 规则 #" + rule.Index + " 命中 " + rule.Backend + "，但该后端声明不支持当前机型 ⇒ 落兜底规则");
			break;
		}
		return new BackendRouteDecision(Rules.Length + 1, "fallback -> GCU", Fallback, identity,
			DeclarationPassed: DeclarationFor(Fallback)?.Supports(identity) ?? true);
	}

	internal static BackendRouteDecision DecideAndLog()
	{
		BackendRouteDecision decision = Decide(MachineIdentity.Collect());
		BackendDeclaration declaration = DeclarationFor(decision.Backend);
		Logger.WriteLine("[router] backend=" + decision.Backend
			+ " | machine=" + decision.Identity.Summary
			+ " | rule=#" + decision.RuleIndex + " " + decision.Rule
			+ " | decl=" + (declaration == null ? "(无)" : declaration.DisplayName + (decision.DeclarationPassed ? "=pass" : "=fail")));
		return decision;
	}
}
