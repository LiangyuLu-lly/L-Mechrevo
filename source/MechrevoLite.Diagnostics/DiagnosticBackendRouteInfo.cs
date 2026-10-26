using System;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 诊断包用：[强化机型分流（只读）] 一节。
/// <para>与运行日志 <c>[强化机型分流]</c> / <c>[gate] 机型分类</c> 共用同一份 <see cref="MachineIdentity" />，
/// 保证诊断包的机型事实与运行判定一致（第 4 步：日志/诊断对齐）。</para>
/// <para>⭐ 2026-10-27（task-41）：补齐「声明 vs 实际」与规则矩阵自测：
/// 运行期事实取 <see cref="BackendRouter.LastDecision" /> 里的同一实例；诊断会现场重采一次并对比，
/// 重算一次路由确认规则表是纯函数；再与实际探测结果（<see cref="MechrevoBackendOrder.LastActualKind" />）对照。</para>
/// <para>只读：不探测后端（实际后端只是读 Probe 已写下的结果）、不发 MQTT、不写任何东西。</para>
/// </summary>
internal static class DiagnosticBackendRouteInfo
{
	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[强化机型分流（只读）]");
		try
		{
			BackendRouteDecision runtimeDecision = BackendRouter.LastDecision;
			MachineIdentity runtimeIdentity = runtimeDecision?.Identity;
			MachineIdentity freshIdentity = MachineIdentity.Collect();

			sb.AppendLine("  运行期机型事实：" + (runtimeIdentity == null ? "(路由未运行，见下方重采)" : runtimeIdentity.Summary));
			sb.AppendLine("  诊断重采事实：" + freshIdentity.Summary);
			if (runtimeIdentity != null && !string.Equals(runtimeIdentity.Summary, freshIdentity.Summary, StringComparison.Ordinal))
			{
				sb.AppendLine("  ⚠️ 运行期事实与诊断重采**不一致**（机型事实在运行期间发生变化，或采集失败方向不同）：");
				sb.AppendLine("       运行期：" + runtimeIdentity.Summary);
				sb.AppendLine("       重  采：" + freshIdentity.Summary);
			}

			if (runtimeDecision == null)
			{
				BackendRouteDecision freshDecision = BackendRouter.Decide(freshIdentity);
				sb.AppendLine("  路由声明：（本次运行未执行路由，以下为诊断实时计算）backend=" + freshDecision.Backend
					+ " · rule=#" + freshDecision.RuleIndex + " " + freshDecision.Rule
					+ " · 能力声明=" + (freshDecision.DeclarationPassed ? "pass" : "fail"));
			}
			else
			{
				sb.AppendLine("  路由声明（运行期）：backend=" + runtimeDecision.Backend
					+ " · rule=#" + runtimeDecision.RuleIndex + " " + runtimeDecision.Rule
					+ " · 能力声明=" + (runtimeDecision.DeclarationPassed ? "pass" : "fail"));
				// 纯函数自校验：同一份事实重新路由必须得到同一结果（不一致=规则表有副作用/状态，属 bug）。
				BackendRouteDecision recomputed = BackendRouter.Decide(runtimeIdentity ?? freshIdentity);
				bool same = recomputed.Backend == runtimeDecision.Backend && recomputed.RuleIndex == runtimeDecision.RuleIndex
					&& recomputed.DeclarationPassed == runtimeDecision.DeclarationPassed;
				sb.AppendLine("  路由重算（同一事实）：backend=" + recomputed.Backend + " · rule=#" + recomputed.RuleIndex
					+ " ⇒ " + (same ? "与运行期一致 ✅（规则表纯函数）" : "⚠️ 与运行期不一致（规则表可能有副作用，请排查）"));
			}

			MechrevoBackendKind actual = MechrevoBackendOrder.LastActualKind;
			bool probed = runtimeDecision != null;
			string actualText = probed ? actual.ToString() : "(未探测)";
			sb.AppendLine("  实际后端（探测结果）：" + actualText);
			if (probed)
			{
				BackendRoute decisionRoute = runtimeDecision == null ? BackendRoute.None : BackendRouter.ToRoute(runtimeDecision.Backend);
				BackendRoute actualRoute = BackendRouter.ToRoute(actual);
				sb.AppendLine("  声明 vs 实际：" + (decisionRoute == actualRoute
					? "一致 ✅"
					: "⚠️ **不一致**（声明 " + decisionRoute + " · 实际 " + actualRoute + "）；已按实际后端运行，不静默换后端"));
			}

			sb.AppendLine("  后端能力声明：");
			sb.AppendLine(BackendRouter.DescribeDeclarations());
			sb.AppendLine("  说明：路由只读机型事实、首条命中；不做“探测失败换后端”。");
			sb.AppendLine("  机型分流自测（纯函数矩阵，只读）：");
			sb.AppendLine(Indent(BackendRouter.RunSelfTest(), "    "));
		}
		catch (Exception ex)
		{
			sb.AppendLine("  (采集失败（已吞掉）：" + ex.GetType().Name + " · " + ex.Message + ")");
		}
		return sb.ToString();
	}

	private static string Indent(string text, string prefix)
	{
		if (string.IsNullOrEmpty(text))
		{
			return prefix + "(空)";
		}
		string[] lines = text.Replace("\r\n", "\n").Split('\n');
		return prefix + string.Join("\n" + prefix, lines);
	}
}
