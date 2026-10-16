// 2026-10-15（C-1 第 2 步「后端化」）：把「本机走哪个后端」从写死的「有 GCU / 没 GCU」抽成**有序候选表**。
// 用途：MechrevoHw 启动时按 MechrevoBackendOrder.Candidates 顺序**逐个探测**，第一个可用的即为 ActiveBackend。
// ⭐ 以后新增后端（例如宝龙达模具那套另行准备的 ACPI 驱动）**只需加一项**：
//     ① MechrevoBackendKind 加枚举值；② 写一个「返回非 null 即成功」的探测委托；③ 加进 Candidates 数组。
//     ⛔ MechrevoHw 里不用改任何 else/if —— 探测循环是数据驱动的（见 MechrevoHw.ConnectAsync）。
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
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
		new MechrevoBackendProbe(MechrevoBackendKind.Gcu, () => MechrevoHw.IsGcuServiceInstalled ? MechrevoHw.GcuBackendMarker : null),
		new MechrevoBackendProbe(MechrevoBackendKind.LegacyMifs, () => LegacyMifsWmi.TryCreate()),
		// ⭐ 2026-10-16（0.291.9 · 宝龙达适配第 1 步）：放在 **GCU 无条件兜底之前** ——
		//   ⚠️ 它现在**恒返回 null**（骨架阶段 + 默认不启用）⇒ 对现有全部机型都是空跑，
		//   行为与改动前逐字一致 ✅；等将来实现真实探测后自然在这里生效。
		//   ⭐ 位置理由：宝龙达机型**没有 GCU 服务**、也**没有同方老接口** ⇒ 天然落在这一格。
		new MechrevoBackendProbe(MechrevoBackendKind.Baolongda, () => BaolongdaBackend.TryCreate()),
		new MechrevoBackendProbe(MechrevoBackendKind.Gcu, () => MechrevoHw.GcuBackendMarker),
	};

	/// <summary>
	/// 按 <see cref="Candidates" /> 顺序探测，返回第一个可用的后端（<c>Kind=None / Created=null</c> = 都没探到）。
	/// ⚠️ 每个候选的异常都被吞掉（只写一行日志），⛔ 绝不外抛 —— ConnectAsync 里探后端不许把启动搞崩。
	/// ⚠️ 本方法是**同步阻塞**的（调用方已经把它包在 <c>Task.Run</c> 里），⛔ 别在 UI 线程上直接调。
	/// </summary>
	internal static (MechrevoBackendKind Kind, object Created) Probe()
	{
		MechrevoBackendProbe[] candidates = Candidates;
		for (int i = 0; i < candidates.Length; i++)
		{
			MechrevoBackendProbe candidate = candidates[i];
			try
			{
				object created = candidate.TryCreate();
				if (created != null)
				{
					return (Kind: candidate.Kind, Created: created);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine($"Backend probe failed ({candidate.Kind}): " + ex.Message);
			}
		}
		return (Kind: MechrevoBackendKind.None, Created: null);
	}
}
