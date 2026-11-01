using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using MechrevoLite.Gpu;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>
/// 机型事实集（只读、带来源、可审计）。
/// <para>第一层「机型识别」只产出事实，不做路由；路由见 <see cref="BackendRouter" />。</para>
/// <para>每个事实失败都退化成 Unknown/空，不抛、不猜、不影响后端探测本身。</para>
/// <para>⭐ 2026-10-27（task-41 机型分流审计）：按设计文档
/// <c>docs/notes/后端匹配-声明式架构-设计-20261020.md</c> §1 补齐事实：
/// <c>model</c> · <c>mifsClass</c> · <c>projectId</c> · <c>gcuVariant</c> · <c>wujieApu</c>。
/// 全部**只读**（注册表 / WMI 类查询 / 已缓存代际），失败=Unknown，⛔ 不写注册表、不碰 EC/NVRAM。</para>
/// <para>⚠️ 事实与后端无关：同一台机器在任何后端下 <see cref="Build" /> 的结果一致；
/// ⛔ 不读 <c>MechrevoDeviceCapabilities</c> 快照（UI 审计会注入假能力位，会污染机型事实）。</para>
/// </summary>
internal sealed record MachineIdentity(
	string Board,
	string BoardSource,
	bool BaolongdaWhitelisted,
	string GpuGeneration,
	bool GcuServiceInstalled,
	string Model,
	string ModelSource,
	string MifsClass,
	bool MifsClassPresent,
	bool MifsLegacyCompatible,
	string ProjectId,
	string ProjectIdSource,
	string GcuVariant,
	string GcuVariantFamily,
	bool WujieApu,
	string WujieApuSource,
	string Summary)
{
	/// <summary>UMI 机器：型号名包含 Umi。</summary>
	public bool IsUmi => (Model ?? "").Contains("Umi", StringComparison.OrdinalIgnoreCase);

	/// <summary>Intel NUC X15：型号含 NUC/X15，或板号 LAPKC*。</summary>
	public bool IsIntelNuc => (Model ?? "").Contains("NUC", StringComparison.OrdinalIgnoreCase)
		|| (Model ?? "").Contains("X15", StringComparison.OrdinalIgnoreCase)
		|| (Board ?? "").StartsWith("LAPKC", StringComparison.OrdinalIgnoreCase)
		// 暂时 Umi/NUC 共用同一套模具与 ACPI 硬件层；先按 NUC 控制台适配。
		|| IsUmi;


	/// <summary>WMI 类 <c>root\WMI:MICommonInterface</c>（老同方 / 宝龙达共用入口）。</summary>
	private const string MifsWmiClass = "root\\WMI:MICommonInterface";

	/// <summary>MICommonInterface 类的探测上限：WMI 卡死时最多拖 2 s，超过按 Unknown 处理。</summary>
	private const int MifsProbeTimeoutMs = 2000;

	/// <summary>项目码（BIOS_PROJECT_ID）可能的注册表位置：ItemSupport 优先、GpuConfig 兜底。</summary>
	private static readonly string[] ProjectRegistryPaths =
	{
		"SOFTWARE\\OEM\\GamingCenter2\\ItemSupport",
		"SOFTWARE\\OEM\\GamingCenter\\ItemSupport",
		"SOFTWARE\\OEM\\ControlCenter\\ItemSupport",
		"SOFTWARE\\OEM\\GamingCenter2\\MySetting\\GpuConfig",
		"SOFTWARE\\OEM\\GamingCenter\\MySetting\\GpuConfig",
		"SOFTWARE\\OEM\\ControlCenter\\MySetting\\GpuConfig"
	};

	/// <summary>项目码可能的注册表值名（与 MechrevoDeviceCapabilities 读取的别名表同口径）。</summary>
	private static readonly string[] ProjectValueNames =
	{
		"BIOS_PROJECT_ID",
		"BIOSProjectID",
		"BIOSProjectId",
		"ProjectID",
		"ProjectId"
	};

	/// <summary>安装器登记的 GCU 变体（<c>HKLM\SOFTWARE\L-Mechrevo\GcuVariant</c>）。</summary>
	private const string GcuRegistryKey = "SOFTWARE\\L-Mechrevo";

	/// <summary>
	/// ⭐ 采集本机机型事实。**只读**；每个事实独立 try/catch，失败退化为 Unknown/空。
	/// <para>⚠️ 每次调用都重新采集（不做进程级缓存）：调用点只有「路由一次」与「诊断包」，
	/// 这样诊断包能如实反映采集当时的事实；运行期判定与日志共用同一份实例（见
	/// <see cref="BackendRouter.LastDecision" />）。</para>
	/// </summary>
	internal static MachineIdentity Collect()
	{
		string board = string.Empty;
		string boardSource = "unknown";
		bool whitelisted = false;
		string model = string.Empty;
		string modelSource = "unknown";
		try
		{
			BoardWhitelist.BoardProductEvidence evidence = BoardWhitelist.GetBoardProductEvidence();
			board = evidence.Product ?? string.Empty;
			boardSource = string.IsNullOrEmpty(evidence.Source) ? "unknown" : evidence.Source;
			whitelisted = BoardWhitelist.Matches(board);
			// ⭐ 设计里的 model：Win32_ComputerSystem.Model / SystemProductName。
			//   BoardWhitelist 已经为板号白名单读过这两项（缓存），直接复用、⛔ 不重复 WMI 查询。
			model = evidence.WmiModel ?? string.Empty;
			modelSource = "wmi.ComputerSystem.Model";
			if (model.Length == 0)
			{
				model = evidence.RegSystemProduct ?? string.Empty;
				modelSource = "reg.SystemProductName";
			}
			if (model.Length == 0)
			{
				modelSource = "unknown";
			}
		}
		catch
		{
		}

		string generation = "Unknown";
		try
		{
			generation = GpuGenerationProvider.Current().Generation.ToString();
		}
		catch
		{
		}

		bool gcuInstalled = false;
		try
		{
			gcuInstalled = MechrevoHw.IsGcuServiceInstalled;
		}
		catch
		{
		}

		string mifsClass;
		bool mifsPresent;
		bool mifsLegacyCompatible;
		try
		{
			mifsClass = ProbeMifsClass(out mifsPresent, out mifsLegacyCompatible);
		}
		catch
		{
			mifsClass = "Unknown(collect exception)";
			mifsPresent = false;
			mifsLegacyCompatible = false;
		}

		string projectId;
		string projectIdSource;
		try
		{
			projectId = ReadProjectId(out projectIdSource);
		}
		catch
		{
			projectId = string.Empty;
			projectIdSource = "unknown";
		}

		string gcuVariant = string.Empty;
		string gcuVariantFamily = string.Empty;
		try
		{
			gcuVariant = ReadGcuVariant();
			if (gcuVariant.Length > 0)
			{
				gcuVariantFamily = MechrevoLite.Hardware.GcuVariantFamily.Resolve(gcuVariant);
			}
		}
		catch
		{
		}

		bool wujieApu = false;
		string wujieApuSource = "unknown";
		try
		{
			wujieApu = ProbeWujieApu(model, board, out wujieApuSource);
		}
		catch
		{
		}

		return new MachineIdentity(board, boardSource, whitelisted, generation, gcuInstalled,
			model, modelSource, mifsClass, mifsPresent, mifsLegacyCompatible,
			projectId, projectIdSource, gcuVariant, gcuVariantFamily,
			wujieApu, wujieApuSource, string.Empty)
		{
			Summary = BuildSummary(board, boardSource, whitelisted, generation, gcuInstalled,
				model, modelSource, mifsClass, projectId, projectIdSource, gcuVariant, gcuVariantFamily,
				wujieApu, wujieApuSource)
		};
	}

	/// <summary>
	/// 仅供离线自测/探针构造**合成事实**（不触碰真实硬件）。
	/// <para>参数默认值 = "什么都没识别到"；路由规则矩阵（<see cref="BackendRouter.RunSelfTest" />）用它。</para>
	/// </summary>
	internal static MachineIdentity ForTest(
		string board = "",
		bool whitelisted = false,
		string generation = "Unknown",
		bool gcuInstalled = false,
		bool mifsClass = false,
		string model = "",
		string projectId = "",
		string gcuVariant = "",
		bool wujieApu = false)
	{
		string boardText = board ?? string.Empty;
		string generationText = string.IsNullOrEmpty(generation) ? "Unknown" : generation;
		string modelText = model ?? string.Empty;
		string projectIdText = projectId ?? string.Empty;
		string variantText = gcuVariant ?? string.Empty;
		string variantFamily = variantText.Length == 0 ? string.Empty : MechrevoLite.Hardware.GcuVariantFamily.Resolve(variantText);
		string mifsText = mifsClass ? "present(guid=legacy)" : "absent";
		string boardSource = boardText.Length == 0 ? "unknown" : "test";
		string modelSource = modelText.Length == 0 ? "unknown" : "test";
		return new MachineIdentity(boardText, boardSource, whitelisted, generationText, gcuInstalled,
			modelText, modelSource, mifsText, mifsClass, mifsClass,
			projectIdText, projectIdText.Length == 0 ? "unknown" : "test",
			variantText, variantFamily,
			wujieApu, "test", string.Empty)
		{
			Summary = BuildSummary(boardText, boardSource, whitelisted, generationText, gcuInstalled,
				modelText, modelSource, mifsText, projectIdText,
				projectIdText.Length == 0 ? "unknown" : "test", variantText, variantFamily,
				wujieApu, "test")
		};
	}

	private static string BuildSummary(
		string board, string boardSource, bool whitelisted, string generation, bool gcuInstalled,
		string model, string modelSource, string mifsClass, string projectId, string projectIdSource,
		string gcuVariant, string gcuVariantFamily, bool wujieApu, string wujieApuSource)
	{
		return "board=" + (board.Length == 0 ? "?" : board) + "(" + boardSource + ")"
			+ " · whitelist=" + whitelisted
			+ " · gen=" + generation
			+ " · gcuInstalled=" + gcuInstalled
			+ " · model=" + (model.Length == 0 ? "?" : model) + "(" + modelSource + ")"
			+ " · mifs=" + mifsClass
			+ " · projectId=" + (projectId.Length == 0 ? "?" : projectId) + "(" + projectIdSource + ")"
			+ " · gcuVariant=" + (gcuVariant.Length == 0 ? "?" : gcuVariant)
			+ (gcuVariantFamily.Length == 0 ? "" : "(family=" + gcuVariantFamily + ")")
			+ " · wujieApu=" + wujieApu + "(" + wujieApuSource + ")";
	}

	private readonly record struct MifsProbeResult(bool Present, bool LegacyCompatible, string Text);

	/// <summary>
	/// 只读探测 WMI 类 <c>root\WMI:MICommonInterface</c> 是否存在，并记录其 GUID 是否等于老同方那份
	/// （<see cref="LegacyMifsWmi.InterfaceGuid" />，<c>LegacyMifsWmi.TryCreate</c> 会硬校验它）。
	/// <para>⚠️ 有 2 s 上限：WMI 挂死时退化为 <c>Unknown(超时)</c>，⛔ 不拖住启动路由。</para>
	/// </summary>
	private static string ProbeMifsClass(out bool present, out bool legacyCompatible)
	{
		present = false;
		legacyCompatible = false;
		try
		{
			Task<MifsProbeResult> task = Task.Run(ProbeMifsClassCore);
			if (!task.Wait(MifsProbeTimeoutMs))
			{
				return "Unknown(探测超时 " + MifsProbeTimeoutMs + "ms)";
			}
			MifsProbeResult result = task.Result;
			present = result.Present;
			legacyCompatible = result.LegacyCompatible;
			return result.Text;
		}
		catch (AggregateException ex)
		{
			Exception inner = ex.InnerException ?? ex;
			return "Unknown(" + inner.GetType().Name + ")";
		}
		catch (Exception ex2)
		{
			return "Unknown(" + ex2.GetType().Name + ")";
		}
	}

	private static MifsProbeResult ProbeMifsClassCore()
	{
		try
		{
			using ManagementClass managementClass = new ManagementClass(MifsWmiClass);
			// 类不存在 ⇒ ManagementException(NotFound/InvalidClass)；这里不读实例、不发 WMI 方法调用。
			managementClass.Get();
			string guid = (managementClass.Qualifiers["Guid"]?.Value?.ToString() ?? string.Empty)
				.Trim().Trim('{', '}');
			bool legacyCompatible = string.Equals(guid, LegacyMifsWmi.InterfaceGuid, StringComparison.OrdinalIgnoreCase);
			string text = guid.Length == 0
				? "present(guid=无)"
				: "present(guid=" + guid + (legacyCompatible ? "=legacy" : "") + ")";
			return new MifsProbeResult(true, legacyCompatible, text);
		}
		catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
		{
			return new MifsProbeResult(false, false, "absent");
		}
		catch (Exception ex2)
		{
			return new MifsProbeResult(false, false, "Unknown(" + ex2.GetType().Name + ")");
		}
	}

	/// <summary>只读读项目码：ItemSupport 优先、GpuConfig 兜底；读不到返回空 + source=unknown。</summary>
	private static string ReadProjectId(out string source)
	{
		for (int i = 0; i < ProjectRegistryPaths.Length; i++)
		{
			try
			{
				using RegistryKey key = Registry.LocalMachine.OpenSubKey(ProjectRegistryPaths[i], writable: false);
				if (key == null)
				{
					continue;
				}
				for (int j = 0; j < ProjectValueNames.Length; j++)
				{
					string text = (key.GetValue(ProjectValueNames[j]) as string ?? string.Empty).Trim();
					if (text.Length > 0)
					{
						source = "registry." + ProjectRegistryPaths[i] + "." + ProjectValueNames[j];
						return text;
					}
				}
			}
			catch
			{
			}
		}
		source = "unknown";
		return string.Empty;
	}

	/// <summary>只读读安装器登记的 GCU 变体（原值，不归一）。</summary>
	private static string ReadGcuVariant()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(GcuRegistryKey, writable: false);
			return (key?.GetValue("GcuVariant") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	/// <summary>
	/// 只读判定「无界 APU」：型号/板号关键字（无界/WUJIE/WJ）+ AMD CPU + 无 NVIDIA 独显。
	/// <para>⚠️ 与 <c>WujieSmuGate.Evaluate()</c> 的 <c>ApuOk</c> 同口径，但**不触发 SMU 可用性探测**
	/// （那需要 PawnIO / SMU 往返，属后端能力，不属于机型识别）；family/SKU 字段不参与判定
	/// —— 事实来源里如实注明这一点。</para>
	/// </summary>
	private static bool ProbeWujieApu(string model, string board, out string source)
	{
		source = "unknown";
		try
		{
			bool isWujie = WujieSmuGate.IsWujieMachine(model, string.Empty, string.Empty, board, out string hitField, out string hitValue);
			bool isAmd = CpuVendor.IsAmd;
			bool hasNvidia;
			try
			{
				hasNvidia = GpuAdapterReader.ReadFromRegistry(out bool _).Any((GpuAdapter adapter) => adapter.IsNvidia);
			}
			catch
			{
				// 读不到适配器 ⇒ ⛔ 不能证明"没有 NVIDIA 独显" ⇒ 按有处理（与 WujieSmuGate 同方向）。
				hasNvidia = true;
			}
			bool apu = isWujie && isAmd && !hasNvidia;
			source = "关键字" + (hitField.Length == 0 ? "(未命中)" : "(" + hitField + "=" + hitValue + ")")
				+ "+CPU厂商(AMD=" + isAmd + ")+无NVIDIA独显=" + (!hasNvidia)
				+ "（不含 family/SKU 与 SMU 可用性）";
			return apu;
		}
		catch
		{
			source = "Unknown";
			return false;
		}
	}
}
