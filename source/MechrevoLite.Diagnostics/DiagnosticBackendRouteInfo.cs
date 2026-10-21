using System;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 诊断包用：[强化机型分流（只读）] 一节。
/// <para>与运行日志 <c>[强化机型分流]</c> / <c>[gate] 机型分类</c> 共用同一份 <see cref="MachineIdentity" />，
/// 保证诊断包的机型事实与运行判定一致（第 4 步：日志/诊断对齐）。</para>
/// <para>只读：不探测后端、不发 MQTT、不写任何东西。</para>
/// </summary>
internal static class DiagnosticBackendRouteInfo
{
	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[强化机型分流（只读）]");
		try
		{
			MachineIdentity identity = MachineIdentity.Collect();
			BackendRouteDecision decision = BackendRouter.Decide(identity);
			sb.AppendLine("  机型事实：" + identity.Summary);
			sb.AppendLine("  路由结果：backend=" + decision.Backend
				+ " · rule=#" + decision.RuleIndex + " " + decision.Rule
				+ " · 能力声明=" + (decision.DeclarationPassed ? "pass" : "fail"));
			sb.AppendLine("  后端能力声明：");
			sb.AppendLine(BackendRouter.DescribeDeclarations());
			sb.AppendLine("  说明：路由只读机型事实、首条命中；不做“探测失败换后端”。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("  (采集失败（已吞掉）：" + ex.GetType().Name + " · " + ex.Message + ")");
		}
		return sb.ToString();
	}
}
