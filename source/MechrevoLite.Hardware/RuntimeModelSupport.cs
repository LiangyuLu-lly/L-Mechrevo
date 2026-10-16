using System;
using Probe;

namespace MechrevoLite.Hardware;

public static class RuntimeModelSupport
{
	public static SupportDecision Current()
	{
		string text = ModelOverrideStateMachine.EnvironmentOverride();
		if (text != null)
		{
			return ModelOverrideStateMachine.ValidateManual(text);
		}
		if (Program.UiAuditMode)
		{
			return SupportDecision.Supported("audit");
		}
		try
		{
			// ⚠️⚠️ 2026-10-15（用户原话「老接口判定提前 做」）：**老机型后端必须【最先】判**。
			// 这一支原来被放在「打不开 \\.\ACPIDriver」的分支里 ⇒ 万一某台老接口机器**能**打开该设备
			// （那需要机器里有同方的 ACPI\INOU0000），它就会掉进下面的正常机型判定：而老接口机器
			// **没有 GCU、注册表也没有 ItemSupport** ⇒ 被判**不支持** ⇒ 顶部显示「未就绪」+ 只读降级
			// （一堆控件被灰），可它实际上是受支持的。
			// 老接口（root\WMI:MICommonInterface，以及将来给宝龙达准备的那套）**不经过 GCU、也不依赖
			// ACPI 机型码** ⇒ 只要探到它就一律判支持，与 ACPIDriver 能不能打开无关。
			// 本机（GCU 机型）`UsesLegacyMifs` 恒 false ⇒ 这一支进不来，行为与改动前逐位一致。
			MechrevoHw legacyHw = Program.hw;
			if (legacyHw != null && legacyHw.UsesLegacyMifs)
			{
				return SupportDecision.Supported("MICommonInterface");
			}
			if (!AcpiDriverReadTransport.TryOpen(out AcpiDriverReadTransport transport, out string error) || transport == null)
			{
				Logger.WriteLine("Model identity unavailable: " + error);
				// ⭐⭐ 2026-10-16（0.291.8.1 · 修「首次安装的刷新效果不明显」的根因之一）：GCU 没连上时
				// **不再直接判 Unparsable**。首次安装的真实时序是：应用先起来 → GCU 服务刚注册/刚启动
				// （真机日志里 ServiceReady=0）→ MQTT 还没连上 ⇒ IsServiceServed()=false ⇒ 整机只读、
				// 一大批控件被藏，用户看着就像"界面没刷新出来"。
				// 判据补一层：只要**这台机器装了 GCU 服务**（或留过 ItemSupport 快照）⇒ 说明它本来就是
				// GCU 机型，先按"受支持"处理；等 GCU 连上后能力位与界面自然会补齐。
				// ⚠️ 老接口机型仍走上面那一支（UsesLegacyMifs ⇒ MICommonInterface），行为不变。
				if (IsServiceServed())
				{
					return SupportDecision.Supported("GCU");
				}
				if (MechrevoHw.IsGcuServiceInstalled)
				{
					return SupportDecision.Supported("GCU-installed (not connected yet)");
				}
				return SupportDecision.Unparsable();
			}
			using (transport)
			{
				SupportDecision supportDecision = ModelSupport.Determine(transport, IsServiceServed());
				ModelOverrideDecision modelOverrideDecision = ModelOverrideStateMachine.EvaluateConfigured(supportDecision);
				return modelOverrideDecision.ManualApplied ? ModelOverrideStateMachine.ValidateManual(modelOverrideDecision.EffectiveModel) : supportDecision;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Model support determination failed: " + ex.Message);
			return SupportDecision.Unparsable();
		}
	}

	public static bool IsPositivelyUnsupported(SupportDecision decision)
	{
		return decision.Reason == SupportReason.NotInSet;
	}

	public static bool ShouldDegradeToReadOnly(SupportDecision decision, bool gcuFirstConnectInProgress)
	{
		if (!gcuFirstConnectInProgress)
		{
			return !decision.IsSupported;
		}
		return false;
	}

	internal static bool IsServiceServed()
	{
		try
		{
			MechrevoHw hw = Program.hw;
			// 2026-10-15（C-1 第 2 步分类 ① + A-22）：这里问的是「**GCU 服务**在不在服务这台机器」
			// ⇒ 必须看 GCU 连接而不是 IsConnected，否则老接口机型会被误判成 "GCU 已服务"。
			// ⚠️ 老接口机型另有单独一支（按 UsesLegacyMifs 返回 MICommonInterface），见下方分支。
			if (hw != null && hw.IsGcuConnected)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Service-served probe (MQTT) failed: " + ex.Message);
		}
		try
		{
			return MechrevoDeviceCapabilities.HasItemSupportContent();
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Service-served probe (ItemSupport) failed: " + ex2.Message);
			return false;
		}
	}
}
