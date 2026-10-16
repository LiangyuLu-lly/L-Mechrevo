// 2026-10-16（0.291.9 beta22.9 · 宝龙达模具适配 · 第 1 步：后端骨架）：
// 背景：宝龙达（Baolongda）模具的机械革命机型 —— ⭐ **主要目标 = 蛟龙16Pro 的 2022 / 2023 / 2024 年款**
//   （机主 2026-10-16 明确：「2022/2023/2024 机械革命蛟龙16Pro 准确来说」）。
//   这一支**不走同方的 GCU 服务**（GCU 走 MQTT 127.0.0.1:13688，那是同方/AiStone 那套）。
//   机主 2026-10-15 说明「ACPI 到时候会另外准备一套给宝龙达」⇒ 本后端是**为那套接口预留的落点**。
//
// ⭐⭐ 本文件在 0.291.9 第 1 步**只搭骨架，不接任何真实协议**：
//   · 枚举值 `MechrevoBackendKind.Baolongda` 早就预留好了（见 MechrevoBackend.cs:30），本轮启用它；
//   · 这里提供 `BaolongdaBackend`（后端对象，携带能力集）与 `BaolongdaBackend.TryCreate()`（探测入口）；
//   · ⚠️ **探测默认"不成功"**（除非显式打开实验开关）⇒ 对 GCU / 老接口机型**零影响**：
//     探不到 ⇒ 候选表继续往下走，行为与改动前逐字一致。
//
// 🔬 为什么默认不启用：现在**手上还没有那台机器、也没有那套 ACPI 接口的确切说明**
//   ⇒ 盲写探测代码会在宝龙达机型上"探中但用不了"，反而把原本能用的路径挤掉 ❌。
//   ⇒ 所以本轮先把**结构**放好，等拿到机器/接口资料（或机主提供抓包）再往里填。
//
// 📌 下一步（要做的事，按顺序）：
//   ① 拿到宝龙达机型的那套 ACPI/WMI 接口说明（或机主提供的抓包/诊断包）；
//   ② 在 `TryCreate()` 里实现真实探测（仿 LegacyMifsWmi.TryCreate：WMI 类 + GUID 校验 + 2 s 超时）；
//   ③ 实现档位读/写（`ReadMode` / `WriteMode`）与能力集补全；
//   ④ 接线：加进 `MechrevoBackendOrder.Candidates`，并在 `MechrevoHw.ConnectAsync` 里
//      加一个分支调 `ApplyBaolongdaBackendAsync`（仿 `ApplyLegacyBackendAsync`）；
//   ⑤ 真机验证后**去掉实验开关**，改为正常探测。
//
// 参考实现（机主提供，⚠️ 本会话访问不了 github.com，需先下载到本地再读）：
//   https://github.com/KanzakiK/DOUZHANZHE-Control （斗战胜佛 Control）
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 宝龙达模具后端（⚠️ **骨架，尚未接真实协议** —— 见文件头说明）。
/// <para>生命周期与 <see cref="LegacyMifsWmi" /> 对齐：探测返回非 null = 后端可用；
/// 对象本身负责后续的档位读写与刷新。</para>
/// <para>⛔ 本类型**不写注册表/EC/ACPI/NVRAM**（除将来明确实现的那套接口）；现在的实现一行都不写。</para>
/// </summary>
internal sealed class BaolongdaBackend : IDisposable
{
	/// <summary>
	/// ⭐ 实验开关（环境变量）：置 <c>1</c> 才允许"探中"本后端。
	/// <para>⚠️ 这是**给将来真机调试用的门**：现在没有机器也没协议说明，默认必须探不中，
	/// 免得在宝龙达机型上把可用的路径挤掉。等真机验证通过后，把这里去掉、改成正常探测。</para>
	/// </summary>
	internal const string EnableEnvVar = "LMECHREVO_BAOLONGDA_BACKEND";

	private bool _disposed;

	/// <summary>本后端是否为"手上有真机、接口已确认"的状态（现在恒为 false）。</summary>
	internal static bool IsImplemented => false;

	private BaolongdaBackend()
	{
	}

	/// <summary>
	/// 探测入口（签名与 <see cref="LegacyMifsWmi.TryCreate" /> 一致：**返回非 null 即成功**）。
	/// <para>⚠️ 当前实现：⭐ **只在显式设了 <see cref="EnableEnvVar" />=1 时才继续**，
	/// 且骨架阶段**仍然返回 null**（只记一行日志）—— 只用于把"后端被选中"这条链路打通、便于将来接协议。</para>
	/// <para>⛔ 任何异常都在这里吞掉并返回 null（探测循环也会再兜一层）。</para>
	/// </summary>
	internal static BaolongdaBackend TryCreate()
	{
		try
		{
			if (!IsExperimentalProbeEnabled())
			{
				// ⭐ 默认路径：不参与探测 ⇒ 老接口 / GCU 机型行为逐字不变。
				return null;
			}
			if (!IsImplemented)
			{
				// ⭐ 骨架阶段：即使开了开关，也只记一行日志说明"还没实现协议"，然后**仍然不接管**，
				//    避免半成品后端把可用路径挤掉。
				Logger.WriteLineIfChanged(
					"baolongda-backend-scaffold",
					"宝龙达后端：实验开关已打开，但本版只有骨架（IsImplemented=false）⇒ 不接管，继续按原候选表探测");
				return null;
			}
			// ⭐ 将来在这里放**真实探测**（仿 LegacyMifsWmi.TryCreate）：
			//   ① 校验那套 ACPI/WMI 接口存不存在（类名 + GUID）；
			//   ② 试读一次档位（2 s 超时）；
			//   ③ 成功 ⇒ new BaolongdaBackend(...) 返回；失败 ⇒ null。
			return null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达后端探测失败（已忽略）：" + ex.Message);
			return null;
		}
	}

	/// <summary>实验开关是否打开（⭐ 只读环境变量，⛔ 不写配置、不写注册表）。</summary>
	private static bool IsExperimentalProbeEnabled()
	{
		try
		{
			return string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar)?.Trim(), "1", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>本后端的能力集（⚠️ 骨架阶段返回"最小集"，等真机确认后再补）。</summary>
	internal MechrevoDeviceCapabilities BuildCapabilities()
	{
		// ⚠️ 与 ApplyLegacyBackendAsync 同口径：凡"要靠上报才知道"的能力一律 false（⛔ 不假装可写）。
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
		_disposed = true;
	}
}
