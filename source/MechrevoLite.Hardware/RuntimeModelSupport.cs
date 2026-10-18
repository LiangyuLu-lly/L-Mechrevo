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
			// ⭐⭐ 2026-10-16（宝龙达适配）：⭐ 判据放宽为 `UsesBuiltInModes` ⇒ ⭐ 宝龙达机型**也判支持** ✅
			//   （⭐ 两者都"不经过 GCU、也不依赖 ACPI 机型码" ✅ —— ⭐ 真机实测宝龙达机型拿不到 GCU 那套 ✅）
			MechrevoHw legacyHw = Program.hw;
			if (legacyHw != null && legacyHw.UsesLegacyMifs)
			{
				return SupportDecision.Supported("MICommonInterface");
			}
			if (legacyHw != null && legacyHw.UsesBaolongda)
			{
				// ⭐ 宝龙达走这里 —— ⭐ 与老接口**同样的判据**（⭐ 探到就判支持，⭐ 不看 ACPIDriver 能不能开 ✅）
				return SupportDecision.Supported("Baolongda");
			}
			// ⭐⭐⭐ 2026-10-16（宝龙达 · 修「更多开关整块灰掉」⚠️ —— 真机症状：机主原话「开关都点不了」）：
			//
			//   ⚠️ **根因链**（已逐行核实 ✅）：
			//     ⭐ ① `SettingsForm.ApplyUnsupportedModelNotice`（约 :8410）里有一圈
			//        `quickSwitches[i].Enabled = !flag;` ⇒ ⭐ **一刀切**把**全部**快捷开关设成
			//        `!IsReadOnlyDegraded` ⚠️ ⇒ ⭐ 只要整机被判"只读降级" ⇒ ⭐ **整块灰掉、一个都点不了** ❌
			//     ⭐ ② 而 `IsReadOnlyDegraded` 来自 `ShouldDegradeToReadOnly(decision, …)` = `!decision.IsSupported`
			//     ⭐ ③ 而上面的 `UsesBaolongda` 那一支 ⭐ **依赖 `Program.hw` 的后端探测结果** ⚠️
			//        ⇒ ⭐ 而后端探测是**异步**的（WMI 探测要几十毫秒起步 ✅）
			//        ⇒ ⭐ ⭐ **首次 UI 刷新时 `UsesBaolongda` 还是 `false`** ⚠️
			//     ⭐ ④ ⇒ 于是掉到下面 `AcpiDriverReadTransport.TryOpen` ⚠️
			//     ⭐ ⑤ ⇒ ⭐ ⭐ **蛟龙上没有 `\\.\ACPIDriver`**（真机实锤：
			//        `CreateFile("\\.\ACPIDriver") failed: Win32 error 2 系统找不到指定的文件` ❌）
			//     ⭐ ⑥ ⇒ `IsServiceServed()` 假、`IsGcuServiceInstalled` 假 ⇒ ⭐ **`Unparsable()`** ❌
			//     ⭐ ⑦ ⇒ ⭐ `IsReadOnlyDegraded = true` ⇒ ⭐ ⭐ **29 个开关一起变灰** ❌❌
			//     ⚠️ 而且 ⭐ **没有自愈** ⚠️：那条"预热期软重载"（`GcuWarmupReload`）⭐ **是 GCU 专有**的 ✅
			//        ⇒ ⭐ 宝龙达上**永远不会触发** ⇒ ⭐ **灰掉的状态一直留着** ❌
			//
			//   ⇒ ⭐ ⭐ **修法：⭐ 补一道【静态机型判据】**（⭐ 只看板号，⭐ **不依赖后端探测是否完成** ✅）
			//      ⭐ 判据与后端白名单**同一套**（⭐ `BaolongdaBackend.IsBaolongdaModelWhitelisted` ✅）
			//      ⇒ ⭐ ⭐ **两处结论永远一致** ✅（⭐ 不会出现"后端认了、机型判定不认"的矛盾 ✅）
			//      ⭐ 且 ⭐ **只读注册表**、⭐ 失败/空值一律按"不是宝龙达"处理 ⇒ ⭐ **安全方向** ✅
			//   ⚠️ 为什么放在 `UsesBaolongda` **之后**：⭐ 后端已经认下来时走上面那支更"有据"（能记到实例名 ✅）；
			//      ⭐ 这一支是**兜底**：⭐ 覆盖"后端还没探完 / 探测失败但机型确实是宝龙达"的窗口 ✅
			//   ⚠️ 对同方/GCU 机型：⭐ 板号是 `GM5HG0A` / `GM6IX8B` / `KUANGSHI-*` / `YAOSHI-*` 等
			//      ⇒ ⭐ **白名单判假** ⇒ ⭐ ⭐ **完全走原路，行为逐位不变** ✅
			if (IsBaolongdaModelByBoard())
			{
				Logger.WriteLine("模型支持判定：⭐ 主板型号命中宝龙达白名单 ⇒ ⭐ 判**受支持** ✅"
					+ "（⭐ 静态判据，⭐ 不依赖后端探测是否完成 ⚠️ —— ⭐ 这正是「更多开关整块灰掉」的修复 ✅）");
				return SupportDecision.Supported("Baolongda");
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

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 修「更多开关整块灰掉」⚠️ 真机症状：机主原话「开关都点不了」）：
	/// ⭐ **只看板号的静态机型判据** —— ⭐ 与后端白名单**同一套**（<see cref="BaolongdaBackend.IsBaolongdaModelWhitelisted" /> ✅）。
	/// <para>⭐⭐ <b>为什么必须有它</b>：<see cref="Current" /> 里那一支 <c>UsesBaolongda</c> 依赖
	/// <c>Program.hw</c> 的**异步**后端探测结果 ⚠️ ⇒ ⭐ 首次 UI 刷新时它还是 <c>false</c> ⇒
	/// 掉进 <c>\\.\ACPIDriver</c> 分支 ⇒ ⭐ 蛟龙**没有**该驱动（真机实锤：<c>Win32 error 2</c> ✅）
	/// ⇒ 判 <see cref="SupportReason.Unparsable" /> ⇒ ⭐ 而 <c>SettingsForm.ApplyUnsupportedModelNotice</c>
	/// 会把**全部**快捷开关 <c>Enabled = !IsReadOnlyDegraded</c> ⇒ ⭐ ⭐ **整块灰掉、一个都点不了** ❌。</para>
	/// <para>⭐ 判据**只读注册表**（<c>BaseBoardProduct</c> ✅）；⭐ 读不到/异常/空值一律按
	/// **「不是宝龙达」** 处理 ⇒ ⭐ **安全方向** ✅（⭐ 宁可不认宝龙达，⭐ 也绝不误伤同方 ✅）。</para>
	/// <para>⚠️ 缓存：⭐ 板号在运行期不会变 ⇒ ⭐ 只判一次 ✅（<see cref="Current" /> 每次界面刷新都会被调用 ⚠️）。
	/// ⭐ 缓存写入用普通字段 —— ⭐ 竞态最多导致**多读一次注册表**，⭐ 结果恒定，⭐ 无害 ✅。</para>
	/// </summary>
	private static bool IsBaolongdaModelByBoard()
	{
		if (_baolongdaBoardHit.HasValue)
		{
			return _baolongdaBoardHit.Value;
		}
		bool hit = false;
		try
		{
			hit = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string note);
			if (!hit)
			{
				Logger.WriteLine("模型支持判定：⭐ 静态板号判据**未命中** ⇒ ⭐ 按非宝龙达处理 ✅（⭐ 安全方向 ✅）：" + note);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("模型支持判定：⭐ 静态板号判据异常 ⇒ ⭐ 按非宝龙达处理 ✅（⭐ 安全方向 ✅）：" + ex.Message);
			hit = false;
		}
		_baolongdaBoardHit = hit;
		return hit;
	}

	/// <summary>⭐ 静态板号判据的缓存（<c>null</c> = 还没判过 ✅）。</summary>
	private static bool? _baolongdaBoardHit;

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
			// ⭐⭐ 2026-10-16（宝龙达适配）：⭐ 「有厂商后端在服务这台机器」也算 ——
			//   ⭐ 老同方（老接口）与 ⭐ 宝龙达都是**不经 GCU** 的后端 ⇒ ⭐ 它们活着就说明这台机被服务着 ✅
			//   ⚠️ 老同方机型上 `UsesBaolongda` 恒 false ⇒ ⭐ 与改动前一致 ✅。
			if (hw != null && hw.UsesBuiltInModes)
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
