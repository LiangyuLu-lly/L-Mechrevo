// 2026-10-16（0.291.9 beta22.9 · 宝龙达模具适配 · 第 2 步：WMI 协议层实现）：
// 目标机型：**机械革命 蛟龙16Pro（2022 / 2023 / 2024 年款）** 以及其它宝龙达（Baolongda）模具机型。
//
// ⭐ 协议来源（⭐ 三份独立实现交叉验证，⭐ 详见 docs\notes\宝龙达接口-从开源实现读出的事实.md）：
//   ① GaoXanSheng/JiaolongControl（⭐ **蛟龙16Pro 本体**）· ② Aveare/BellatorFanControl（官方控制台逆向）
//   ③ KanzakiK/DOUZHANZHE-Control。
//   三份在**核心协议上完全一致**：
//     · 命名空间/类   : root\WMI : MICommonInterface
//     · 实例名        : ⭐ ACPI\PNP0C14\MIFS_0（⭐ 宝龙达三份实现都用**实例名直指**）
//     · 方法          : MiInterface（入参 InData byte[32] ⇒ 出参 OutData byte[]）
//     · InData[1]     : 250 = Get · 251 = Set
//     · InData[3]     : 功能号（见下 Function 枚举）
//     · InData[4..]   : 参数
//     · OutData[4..]  : 返回值
//   ⚠️ 与本树既有的 `LegacyMifsWmi`（老同方机型）**协议同族、功能号 8 语义一致** ⇒
//      模式映射可**直接复用**（⭐ 已逐跳核对，⭐ 见笔记 4.1：静音→MIFS 2 / 平衡→MIFS 0 / 狂暴→MIFS 1 ✅）。
//
// ⚠️ 平台差异（⭐ 必须自适应，⛔ 不要写死）：
//   · 风扇数量：⭐ 蛟龙16Pro = **2 个**（OutData[4..5]=GPU扇 · [6..7]=CPU扇）
//               ⭐ 联想 N176 = **3 个**（[4..5]=大扇1 · [6..7]=大扇2 · [10..11]=小扇）
//   · 模式档数：⭐ 蛟龙 = **3 档**（0/1/2）· ⭐ 联想 = **4 档**（0/1/2/3）
//   · 功能覆盖：⭐ 蛟龙**独有** CPUPower(23) · 键盘灯(16/17/18) · LOGO灯(15)
//   ⇒ 本类**先用只读试读来判断**（返回长度/有效性），⛔ 不猜、⛔ 不写死。
//
// ⚠️ 安全边界：
//   · 本类**只做 WMI**（⭐ 官方控制台也只用 WMI —— Bellator README 原话：
//     「原厂控制台通过 WMI 调用厂商 ACPI 接口，**不是直接写 EC 端口**」）⇒ ⛔ **不碰 EC**、⛔ 不装驱动 ✅
//   · `TryCreate()` **全程只读**（⭐ 只发 Get）⇒ 可在任何机器上安全探测 ✅
//   · 写入方法一律**显式命名**（TrySet*），⭐ 调用方按能力位决定要不要调 ✅
//
// 许可/署名：本文件为**独立实现**（协议号与字段布局来自公开开源实现的事实性记录），
// 见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 宝龙达模具的 WMI 协议层（<c>root\WMI : MICommonInterface</c> 的 <c>MiInterface</c> 方法）。
/// <para>⭐ 与 <see cref="LegacyMifsWmi" /> 同族，但**能力更全**：除性能档外还有风扇、温度、功耗墙、灯效。</para>
/// <para>⛔ 不碰 EC、⛔ 不装内核驱动、⛔ 不写注册表（⭐ 全程只走 WMI ✅）。</para>
/// </summary>
internal sealed class BaolongdaWmi : IDisposable
{
	// ── WMI 常量（⭐ 三份开源实现逐字一致）────────────────────────────────
	private const string WmiNamespace = "root\\WMI";
	private const string WmiClass = "MICommonInterface";
	private const string WmiMethodName = "MiInterface";

	/// <summary>
	/// ⭐ 实例名候选表（⭐ 按顺序试；⭐ 都失败再枚举 ← 兜底 ✅）。
	/// <para>⚠️ 2026-10-16 **真机实测修正**（蛟龙16Pro · MRID6-23）：⭐ 设备树里那个 WMI-ACPI 设备叫
	/// ⭐ <c>ACPI\PNP0C14\MIFS</c>（⭐ **没有 `_0` 后缀** ⚠️），⭐ 而三份开源实现里写的是
	/// <c>ACPI\PNP0C14\MIFS_0</c> ⇒ ⭐ **实例名确实因机型/映射而异** ⚠️
	/// ⇒ ⭐ 所以**两种都试**，⛔ 不写死一个 ✅（⭐ 反正最后还有枚举兜底 ✅）</para>
	/// </summary>
	internal static readonly string[] PreferredInstanceNames = new string[2]
	{
		@"ACPI\PNP0C14\MIFS_0", // 三份开源实现用的（联想 N176 等）
		@"ACPI\PNP0C14\MIFS",   // ⭐ 真机实测到的（机械革命蛟龙16Pro · MRID6-23）
	};

	/// <summary>⚠️ 仅用于兼容旧引用（⭐ 取第一个候选 ✅）。</summary>
	internal const string PreferredInstanceName = @"ACPI\PNP0C14\MIFS_0";

	/// <summary>请求/响应缓冲固定 32 字节（⭐ 三份实现一致）。</summary>
	internal const int BufferLength = 32;

	private const byte MethodGet = 250;
	private const byte MethodSet = 251;

	/// <summary>⭐ 功能号（⭐ 蛟龙16Pro 的 <c>SysEnums.MethodName</c> 逐项照抄，⭐ 与另两份实现交叉核对过）。</summary>
	internal enum Function : byte
	{
		/// <summary>⭐ 性能/散热模式（0=平衡 · 1=增强 · 2=静音 · 3=疯狂/游戏，⭐ 档数按机型）。</summary>
		SystemPerMode = 8,

		/// <summary>显卡模式（0=混合 · 1=独显）。</summary>
		GpuMode = 9,

		/// <summary>键盘灯状态（只读）。</summary>
		RgbKeyboardStatus = 10,

		/// <summary>Fn 锁（0/1）。</summary>
		FnLock = 11,

		/// <summary>触摸板锁（0/1）。</summary>
		TPLock = 12,

		/// <summary>⭐ 风扇转速（⭐ 返回 2 或 3 个值，⭐ 见 <see cref="TryReadFanSpeeds" />）。</summary>
		CpuGpuFanSpeed = 13,

		/// <summary>⚠️ 未使用（⭐ 蛟龙那份标了 NotUse）。</summary>
		GpuFanSpeedNotUse = 14,

		/// <summary>⭐ LOGO 灯（0=关 · 1=开）。</summary>
		AmbientLight = 15,

		/// <summary>键盘灯模式（0=关 · 2=RGB 固定色）。</summary>
		RgbKeyboardMode = 16,

		/// <summary>⭐ 键盘灯颜色（⭐ 写 3 字节 R,G,B；⭐ 读 OutData[4],[5],[6]）。</summary>
		RgbKeyboardColor = 17,

		/// <summary>键盘灯亮度（0~3 级）。</summary>
		RgbKeyboardBrightness = 18,

		/// <summary>交流/电池类型（只读）。</summary>
		SystemAcType = 19,

		/// <summary>⭐ 风扇手动上限 开关（写 <c>[4]=FanType</c> · <c>[5]=0/1</c>）。</summary>
		MaxFanSpeedSwitch = 20,

		/// <summary>⭐ 风扇手动上限 转速（写 <c>[4]=FanType</c> · <c>[5]=x100 RPM</c>）。</summary>
		MaxFanSpeed = 21,

		/// <summary>⭐ CPU 温度（读 <c>OutData[4]</c> 摄氏度）。</summary>
		CpuThermometer = 22,

		/// <summary>⭐ CPU 自定义功耗（⭐ 见 <see cref="CpuPowerSub" />）。</summary>
		CpuPower = 23,
	}

	/// <summary>⭐ <see cref="Function.CpuPower" />（23）的**子命令**（⭐ 蛟龙16Pro 的 <c>CPUPower</c> 枚举）。</summary>
	internal enum CpuPowerSub : byte
	{
		/// <summary>关闭自定义（写单字节）。</summary>
		CloseState = 0,

		/// <summary>开启自定义（写单字节）。</summary>
		OpenState = 1,

		/// <summary>⭐ PL1 长时功耗（写两字节 <c>[2, 瓦数]</c>）。</summary>
		LongPower = 2,

		/// <summary>⭐ PL2 短时功耗（写两字节 <c>[3, 瓦数]</c>）。</summary>
		ShortPower = 3,

		/// <summary>⭐ CPU 温度墙（写两字节 <c>[4, 摄氏]</c>）。</summary>
		CpuTempWallState = 4,
	}

	private readonly object _gate = new object();
	private readonly string _instancePath;
	private bool _disposed;

	/// <summary>本对象拿到的实例路径（⭐ 诊断用：⭐ 确认到底是不是 `MIFS_0`）。</summary>
	internal string InstancePath => _instancePath;

	/// <summary>类的 GUID 限定符（⭐ 诊断用；⭐ 探不到就是空串）。⭐ 这是判断"我们该不该放宽 GUID 判据"的关键证据。</summary>
	internal string ClassGuid { get; private set; }

	/// <summary>⭐ 实测风扇个数（2 或 3；0 = 还没测出来）。</summary>
	internal int FanCount { get; private set; }

	/// <summary>⭐ 实测模式档数（3 或 4；0 = 还没测出来）。</summary>
	internal int ModeCount { get; private set; }

	/// <summary>
	/// ⭐⭐ 2026-10-17：**功能号 22（CPU 温度）在探测期是否读得通**。
	/// <para>⚠️ 语义**如实**：<c>false</c> = 这个功能号在本机**读不到**（⭐ 30 系宝龙达的已知现象 ⚠️），
	/// ⛔ **不是**"本机没有 CPU"✅ —— 温度仍由 LM 自带 LHM 兜底显示 ✅。</para>
	/// <para>⭐ 用途：⭐ 诊断包/日志如实输出，⛔ 不作任何"整机是否可用"的判据
	/// （⭐ 这正是 2026-10-17 那轮致命 bug 的反面：⛔ 不能拿单项可读性决定整机接管 ✅）。</para>
	/// </summary>
	internal bool CpuTempAvailable { get; private set; }

	/// <summary>⭐ 探测期读到的 CPU 温度（℃）；<see cref="CpuTempAvailable" /> 为 <c>false</c> 时无意义。</summary>
	internal int CpuTempCelsius { get; private set; }

	/// <summary>
	/// ⭐ 探测时**成立的那一条活体证据**（⭐ 便于日志/诊断一眼看出"凭什么接管"）。
	/// <para>⭐ 空串 = 板号白名单命中但**所有**只读功能号都没读通（⭐ 仍接管，但记录如实 ✅）。</para>
	/// </summary>
	internal string LivenessEvidence { get; private set; } = string.Empty;

	private BaolongdaWmi(string instancePath)
	{
		_instancePath = instancePath;
		ClassGuid = string.Empty;
	}

	// ══════════════════════════════════════════════════════════════════════
	//  探测（⭐ 全程只读）
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 探测入口（⭐ 签名与 <see cref="LegacyMifsWmi.TryCreate" /> 一致：**返回非 null 即成功**）。
	/// <para>⭐ 顺序：⭐ ① **实例名直指** <see cref="PreferredInstanceName" />（宝龙达三份实现的做法 ✅）
	/// ⇒ ⭐ ② 失败则**枚举** <c>MICommonInterface</c> 的实例，取第一个（⭐ 兼容实例名不同的机型 ✅）。</para>
	/// <para>⭐ 探到时会把 ⭐ **类 GUID · 实例路径 · 风扇个数 · 模式档数** ⭐ 全部打进日志 ⇒
	/// ⭐ **一次真机运行就能定案**（⭐ 这正是我们当前最缺的证据 ✅）。</para>
	/// <para>⛔ 全程只发 Get（只读）；⛔ 异常一律吞掉返回 null（探测循环还会再兜一层）。</para>
	/// </summary>
	internal static BaolongdaWmi TryCreate()
	{
		BaolongdaWmi created = null;
		try
		{
			string path = FindInstancePath();
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			created = new BaolongdaWmi(path);
			// ⭐ 拿类 GUID（⭐ 诊断关键：⭐ 若与老同方那份 `B60BFB48-…` 不同，⭐ 就解释了为什么 LegacyMifsWmi 探不到）
			created.ProbeClassGuid();
			// ⭐⭐⭐ 2026-10-17（真机致命 bug 修复 · 30 系宝龙达整机失效）：
			//
			//   背景（机主真机反馈 + 诊断包实证）：
			//     旧判据把「**功能号 22（CPU 温度）能读**」当作**唯一**活体证明。
			//     而 **30 系宝龙达机型**（主板 `MRID6` · 6800H + 3070Ti）实测**读不到功能号 22** ——
			//     与已知证据吻合：官方 **30 系**控制台的 `WMIMethodName` 只到 18，
			//     **40 系**才到 23（含 `CPUThermometer=22`）⇒ 30 系固件很可能真的没有这一项。
			//   ⇒ 后果：⭐ **一个可选功能读不到，却让整机退回 legacy** ⚠️
			//      ⇒ 所有宝龙达能力全部失效（性能模式提示失败 / FN 锁不显示 / 灯光不显示 /
			//        色彩增强未隐藏 / Win 键锁 / 小键盘锁 / 触摸板 / 校色空壳 全部跟着坏 ❌）
			//
			//   ⭐ **设计原则（写死在这里，防止后人再犯）**：
			//     **探测**（要不要接管这个后端）与**能力位**（这个后端的每项功能能不能用）是**两层** ——
			//     ⛔ 绝不能用一个功能的可读性去决定「整机是否接管」。
			//     探测只判「**这个接口是不是活的**」；每项能力由
			//     `ProbeCapabilities()`（诊断用）与 `BaolongdaBackend.BuildCapabilities()`
			//     （⭐ **逐项运行时自检** ✅）各自独立决定 —— 后者本来就是按功能号 10/23/9 分别自检的 ✅。
			//
			//   ⭐ 分级判据：
			//     ① **板号命中白名单**（⭐ 静态证据 · 与后端白名单同一套 ✅）＋ **类存在**
			//        （⭐ `FindInstancePath()` 已保证 ✅）⇒ ⭐ **必须接管**；
			//        功能号 22 读不到只记日志，⛔ 不再一票否决 ✅
			//     ② 板号**未**命中白名单 ⇒ 保持较严的活体证明：**多判据任一成功**即接管
			//        （⭐ 见 `ProbeLiveness()`：功能号 22 / 8 / 11 / 12 / 13，**每条都带语义校验** ✅）
			//   ⚠️ ⛔ 绝不变成"能打开就接管"：**类存在**这一前置条件**始终保留** ✅
			//      （⭐ 非宝龙达机型根本没有 `MICommonInterface` 这个类 ⇒ `FindInstancePath()` 早已静默返回 null ✅）
			bool boardWhitelisted = IsBaolongdaBoardCandidate();
			string liveness = created.ProbeLiveness();
			created.LivenessEvidence = liveness ?? string.Empty;
			if (!boardWhitelisted && liveness == null)
			{
				// 板号未命中白名单、且**所有**活体判据都不成立 ⇒ ⛔ 不接管（安全方向：宁可不认，绝不误认）
				Logger.WriteLine("宝龙达 WMI 探测：类存在但**所有**活体判据都不成立 ⇒ 判为不可用（⛔ 不接管）");
				created.Dispose();
				return null;
			}
			if (boardWhitelisted)
			{
				// ⭐ 板号白名单命中 ⇒ 静态证据优先 ⇒ **必须接管**（把活体证据如实记下来）
				// ⚠️ 用 `WriteError`：默认「仅错误」级别下也要落盘（⭐ 这是"凭什么接管"的关键证据 ✅）
				Logger.WriteError($"宝龙达 WMI 探测：⭐ 主板型号命中宝龙达白名单 ⇒ ⭐ **接管**（静态证据优先 ✅）"
					+ $" · ⭐ 活体证据={liveness ?? "⚠️ 无（所有只读功能号都没读通，仍按白名单接管 ⚠️）"}"
					+ $" · 类GUID={created.ClassGuid} · 实例=\"{path}\"");
				if (!created.CpuTempAvailable)
				{
					// ⭐ 如实记录：CPU 温度这一项不可用 —— 但⛔ **不影响其余能力**（这就是本次修复的核心 ✅）
					// ⚠️ 同样用 `WriteError`：这是 30 系机型的关键指纹，诊断包必须能看到 ✅
					Logger.WriteError("宝龙达 WMI：⭐ 功能号 22（CPU 温度）**读不到** ⇒ ⭐ CPU 温度能力=**不可用**"
						+ "（⭐ 由 LM 自带 LHM 兜底显示，⛔ **不会显示成 0℃** ✅）"
						+ " ⚠️ 这是 **30 系宝龙达** 的已确认现象（官方 30 系控制台也不使用功能号 22）"
						+ " ⭐ **不影响其余宝龙达能力**（⭐ 探测已与能力位解耦 ✅）");
				}
			}
			else
			{
				Logger.WriteLine($"宝龙达 WMI 探测：板号未命中白名单，但活体证据成立 ⇒ 接管（证据={liveness}）");
			}
			// ⭐ 实测风扇个数与模式档数（⭐ 只读；⭐ 用返回长度/有效性判断，⛔ 不猜）
			//   ⚠️ 这两项**只用于日志/诊断**（消费者只有 `DiagnosticBaolongdaInfo` / 启动日志 / `FanWatchRunner`）
			//      ⇒ ⭐ 探测失败只是保持 0（"没测出来"），⛔ **不影响任何功能** ✅
			created.ProbeCapabilities();
			Logger.WriteLine($"宝龙达 WMI 探测成功：⭐ 实例=\"{created._instancePath}\" · ⭐ 类GUID={created.ClassGuid} · "
				+ $"⭐ 风扇={created.FanCount} 个 · ⭐ 模式={created.ModeCount} 档 · "
				+ $"⭐ CPU温度={(created.CpuTempAvailable ? created.CpuTempCelsius + "℃" : "⚠️功能号22不可用")}"
				+ $" · ⭐ 活体证据={created.LivenessEvidence}");
			return created;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI 探测失败（已忽略）：" + ex.Message);
			created?.Dispose();
			return null;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17：**活体证明**（⭐ 全程只读）—— 判断「这个 WMI 接口是不是**活的**」。
	/// <para>⛔ **不判断任何具体能力**：⭐ 这是"探测层"，能力位是另一层（⭐ 见 <see cref="TryCreate" /> 里的说明 ✅）。</para>
	/// <para>⭐ 返回**成立的第一条**证据描述（⭐ 便于日志/诊断看出"凭什么接管"）；⭐ **全部不成立 ⇒ <c>null</c>** ✅。</para>
	/// <para>⭐⭐ 每条判据都带**语义校验**（⛔ 不是"能打开就算"、⛔ 也不是"返回长度够就算"）——
	/// 因为本项目反复踩过「**读到东西 ≠ 读到对的东西**」「**0xFF 填充值**」这类坑 ⚠️：</para>
	/// <list type="number">
	/// <item>⭐ 功能号 22（CPU 温度）：读数必须落在 <c>1..254</c>（⭐ <c>0</c> 与 <c>255</c> 都判无效 ✅）</item>
	/// <item>⭐ 功能号 8（性能模式）：原始值必须 <c>≤ 3</c>（⭐ 已知映射只覆盖 0/1/2/3 ✅；⭐ 0xFF 会被排除 ✅）</item>
	/// <item>⭐ 功能号 11 / 12（Fn 锁 / 触摸板锁）：**带 <c>OutData[3]</c> 回显校验** ✅
	///   —— 回应里必须**回显我们请求的功能号** ⇒ ⭐ 证明这个接口在按我们的协议应答 ✅（最强证据）</item>
	/// <item>⭐ 功能号 13（风扇转速）：返回长度 ≥ 8 **且** RPM 值合理（⭐ 两个通道都 <c>&lt; 20000</c>
	///   ⇒ ⭐ 排除 <c>0xFF</c> 填充出来的 <c>65535</c> ✅）</item>
	/// </list>
	/// <para>⚠️ 顺序：⭐ 先试 22（⭐ 顺便把 CPU 温度能力记下来）⭐，再 8 ⭐，再 11/12（回显校验）⭐，最后 13 ✅。</para>
	/// </summary>
	private string ProbeLiveness()
	{
		// ── ① 功能号 22：CPU 温度（⭐ 原判据，仍保留为**首选**证据）──
		if (TryReadCpuTemperature(out int celsius))
		{
			CpuTempAvailable = true;
			CpuTempCelsius = celsius;
			return $"功能号22(CPU温度)={celsius}℃";
		}
		// ── ② 功能号 8：性能模式（⭐ 只认 0..3 的原始值）──
		//   ⚠️ 这里**刻意调 `TryReadByte` 而不是 `TryReadOperatingMode`**：后者对未知原始值会打
		//      `WriteError("未知的性能模式原始值 …")` —— ⭐ 那是**运行期**该报的错（说明映射对不上），
		//      但在**探测期**打出来会误导（⭐ 探测只是问"活不活"，不是"映射对不对"）⚠️。
		if (TryReadByte(Function.SystemPerMode, out byte rawMode) && rawMode <= 3)
		{
			return $"功能号8(性能模式)=raw{rawMode}";
		}
		// ── ③ 功能号 11 / 12：Fn 锁 / 触摸板锁（⭐ 带回显校验，最可靠）──
		if (TryReadByteChecked(Function.FnLock, out byte fnLock))
		{
			return $"功能号11(Fn锁)={fnLock}（回显校验通过）";
		}
		if (TryReadByteChecked(Function.TPLock, out byte tpLock))
		{
			return $"功能号12(触摸板锁)={tpLock}（回显校验通过）";
		}
		// ── ④ 功能号 13：风扇转速（⭐ 带合理性校验，排除 0xFF 填充）──
		if (TryReadFanSpeeds(out FanSpeeds speeds))
		{
			// ⭐ 合理性：两个主通道都要在 0..20000 之间（⭐ 协议上限 6000 ⇒ 20000 足够宽松 ✅）；
			//   ⚠️ 若固件对不支持的功能号回 `0xFF` 填充 ⇒ 会解析成 65535 ⇒ ⭐ 这里排除掉 ✅
			if (speeds.First is >= 0 and < 20000 && speeds.Second is >= 0 and < 20000)
			{
				return $"功能号13(风扇)={speeds.First}/{speeds.Second} RPM";
			}
		}
		return null;
	}

	/// <summary>
	/// 找可用实例路径：⭐ 先确认**类存在**（⭐ 不存在就静默返回，⛔ 免得在非宝龙达机型上刷日志），
	/// ⭐ 再**直指** `MIFS_0`，⭐ 最后**枚举**（⭐ 三步都只读 ✅）。
	/// </summary>
	private static string FindInstancePath()
	{
		// ── ⓪ 先确认类存在（⭐ 关键：⭐ 非宝龙达机型没有这个类，⛔ 必须静默返回 ✅）──
		try
		{
			using ManagementClass probe = new ManagementClass(WmiNamespace + ":" + WmiClass);
			probe.Get();
		}
		catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
		{
			// ⭐ 这个类根本不存在 ⇒ 对**非宝龙达机型**必须静默（⭐ 现有机型每次启动都会走到这里，⛔ 一行日志都不该打 ✅）
			// ⚠️⚠️ 2026-10-16 群里反馈：⭐「⭐ 一卸载官方控制台这个软件就识别不了了」⚠️
			//    ⇒ ⭐ 若**板号是宝龙达候选**（⭐ `MRID6-*` ✅）**却探不到这个类** ⚠️，
			//      ⭐ ⭐ 那就【必须】留一条**默认级别可见**的记录 ✅ ——
			//      ⭐ 否则我们连"为什么认不出"都拿不到任何证据 ❌（⭐ 这正是本次反馈卡住的原因 ✅）
			//    ⭐ 判据用**静态板号**（⭐ 只读注册表 ✅ · ⭐ 与后端白名单**同一套** ✅）⇒ ⭐ 不误伤同方 ✅
			if (IsBaolongdaBoardCandidate())
			{
				Logger.WriteError("宝龙达 WMI 探测失败：⭐ 主板型号是宝龙达候选（`MRID6` / `MRID6-*`）但 "
					+ "`root\\WMI:MICommonInterface` **这个类不存在** ⇒ ⭐ 本机认不出宝龙达 ❌。"
					+ "⚠️ 已知成因：⭐ **卸载官方控制台（JiaoLong Control）后可能出现此现象** ⚠️；"
					+ "⭐ 请随诊断包一起反馈（⭐ 宝龙达诊断段会打印该类与 `PNP0C14` 设备的存在性 ✅）");
			}
			return null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：确认类是否存在时失败（按「没有」处理）：" + ex.Message);
			return null;
		}

		// ── ① 逐个试候选实例名（⭐ 真机实测：⭐ 蛟龙是 `MIFS`，⭐ 联想是 `MIFS_0` ⇒ ⭐ 两种都得试 ✅）──
		foreach (string candidate in PreferredInstanceNames)
		{
			try
			{
				using ManagementObject obj = new ManagementObject(WmiNamespace, WmiClass + ".InstanceName='" + candidate + "'", null);
				obj.Get();
				return obj.Path.RelativePath;
			}
			catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
			{
				// ⚠️⚠️ 2026-10-16 真机实测：⭐ **非管理员身份下会 AccessDenied**（⭐ 抓包脚本那次就是 ✅）
				// ⇒ ⭐ 这不是"接口不存在"，⭐ 而是"权限不够" ⇒ ⭐ 必须**如实记出来**，
				//    ⛔ 不能当成"没有这个后端"（⭐ 否则会误导排查方向 ⚠️）
				Logger.WriteLine($"宝龙达 WMI：⭐ 访问被拒（AccessDenied）—— 实例 \"{candidate}\" **存在但我们权限不够**"
					+ " ⇒ ⚠️ 本进程可能没有以管理员运行；⭐ 这也说明这台机器**很可能就是宝龙达机型** ✅");
				return null;
			}
			catch (Exception ex)
			{
				// 类在、但这个实例名不在 ⇒ 试下一个候选
				Logger.WriteLine($"宝龙达 WMI：实例名候选 \"{candidate}\" 未命中（试下一个）：{ex.Message}");
			}
		}
		// ── ② 枚举（⭐ 兜底：⭐ 兼容实例名完全不同的机型 ✅）──
		try
		{
			using ManagementClass mc = new ManagementClass(WmiNamespace + ":" + WmiClass);
			mc.Get();
			using ManagementObjectCollection instances = mc.GetInstances();
			foreach (ManagementBaseObject inst in instances)
			{
				// ⚠️ `Path` 在 `ManagementObject` 上，`ManagementBaseObject` 没有 ⇒ 要转一下
				string relative = (inst as ManagementObject)?.Path?.RelativePath;
				if (!string.IsNullOrEmpty(relative))
				{
					Logger.WriteLine($"宝龙达 WMI：枚举到实例 \"{relative}\"（⭐ 请把这一行发回，⭐ 我们要确认实例名规律）");
					return relative;
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("宝龙达 WMI：枚举实例失败：" + ex2.Message);
		}
		return null;
	}

	/// <summary>
	/// ⭐ 静态板号判据：⭐ 本机**是不是宝龙达候选** ✅（⭐ 只读注册表 ✅ · ⭐ 与
	/// <see cref="BaolongdaBackend.IsBaolongdaModelWhitelisted" /> **同一套** ✅ ⇒ ⭐ 单一事实来源 ✅）。
	/// <para>⚠️ 用途**仅限**：⭐ 决定"探不到 WMI 类"时**要不要留一条可见记录** ✅ ——
	/// ⛔ **不参与任何能力/后端判定** ⚠️（⭐ 能力位仍由 <c>BaolongdaBackend.BuildCapabilities</c> 独占 ✅）。</para>
	/// <para>⭐ 非宝龙达机型 ⇒ 恒 <c>false</c> ⇒ ⭐ **保持原来的静默** ✅（⛔ 不刷日志 ✅）。</para>
	/// </summary>
	private static bool IsBaolongdaBoardCandidate()
	{
		try
		{
			return BaolongdaBackend.IsBaolongdaModelWhitelisted(out _);
		}
		catch
		{
			// ⭐ 读注册表失败/异常 ⇒ 按"不是候选"处理 ⇒ ⭐ 安全方向（⭐ 与后端白名单一致 ✅）
			return false;
		}
	}

	/// <summary>读类的 GUID 限定符（⭐ 只读；⭐ 失败就留空串）。</summary>
	private void ProbeClassGuid()
	{
		try
		{
			using ManagementClass mc = new ManagementClass(WmiNamespace + ":" + WmiClass);
			mc.Get();
			object guid = mc.Qualifiers["Guid"]?.Value;
			ClassGuid = guid?.ToString()?.Trim('{', '}') ?? string.Empty;
			if (ClassGuid.Length == 0)
			{
				Logger.WriteLine("宝龙达 WMI：⚠️ 该类**没有** GUID 限定符（⭐ 这正是 LegacyMifsWmi 可能探不到的原因之一）");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：读类 GUID 失败（不影响使用）：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 实测能力（⭐ 只读）：风扇个数靠**功能号 13 的返回长度**判断；模式档数靠**试读值域**判断。
	/// <para>⛔ 只发 Get；⛔ 任何失败都当作"没测出来"（0），⛔ 不影响对象可用性。</para>
	/// </summary>
	private void ProbeCapabilities()
	{
		// ── 风扇个数 ──
		try
		{
			byte[] outData = Invoke(MethodGet, Function.CpuGpuFanSpeed);
			if (outData != null)
			{
				// ⭐ 蛟龙16Pro：2 个（用 [4..7]）
				// ⭐ 联想 N176：3 个（还用 [10..11]）
				// ⚠️ 判据：⭐ 返回长度 ≥ 12 **且** [10..11] 有非零值 ⇒ 认为有第 3 个（小扇）
				bool hasThird = outData.Length >= 12 && (outData[10] != 0 || outData[11] != 0);
				FanCount = hasThird ? 3 : 2;
				Logger.WriteLine($"宝龙达 WMI：风扇探测 ⇒ OutData 长度={outData.Length} · [10..11]={(outData.Length >= 12 ? $"0x{outData[10]:X2}{outData[11]:X2}" : "n/a")} ⇒ 判为 {FanCount} 个");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：风扇个数探测失败（保持 0）：" + ex.Message);
		}

		// ── 模式档数 ──
		try
		{
			byte[] outData = Invoke(MethodGet, Function.SystemPerMode);
			if (outData != null && outData.Length > 4)
			{
				byte raw = outData[4];
				// ⭐ 蛟龙16Pro 只有 0/1/2；⭐ 联想有 0~3。
				// ⚠️ 读不出"支持几档"（⭐ 只能反映**当前**在哪一档），故：
				//   ⭐ 若当前值 == 3 ⇒ **该机支持 4 档** ✅（⭐ 这是唯一能确定的证据）
				//   ⭐ 否则按 3 档处理（⭐ 保守，⛔ 不主动写 3 —— 见笔记 4.1 的注意事项 ✅）
				ModeCount = (raw == 3) ? 4 : 3;
				Logger.WriteLine($"宝龙达 WMI：模式探测 ⇒ 当前 raw={raw} ⇒ 判为最多 {ModeCount} 档（⭐ raw==3 才算 4 档）");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：模式档数探测失败（保持 0）：" + ex.Message);
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	//  底层收发
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 发一次 <c>MiInterface</c> 并取回 <c>OutData</c>。
	/// <para>⭐ <paramref name="payload" /> 非空时会拷到 <c>InData[4..]</c>（⭐ 长度受 <see cref="BufferLength" /> 限制）。</para>
	/// <para>⛔ 由调用方负责加锁（见各公开方法的 <c>lock (_gate)</c>）。</para>
	/// </summary>
	private byte[] Invoke(byte methodType, Function function, byte[] payload = null)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			byte[] inData = new byte[BufferLength];
			inData[1] = methodType;
			inData[3] = (byte)function;
			if (payload != null && payload.Length > 0)
			{
				int count = Math.Min(payload.Length, BufferLength - 4);
				Array.Copy(payload, 0, inData, 4, count);
			}
			using ManagementObject obj = new ManagementObject(WmiNamespace, _instancePath, null);
			ManagementBaseObject inParams = obj.GetMethodParameters(WmiMethodName);
			inParams["InData"] = inData;
			using ManagementBaseObject outParams = obj.InvokeMethod(WmiMethodName, inParams, null);
			return outParams?["OutData"] as byte[];
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	//  通用读 / 写
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>读一个功能号的单字节值（<c>OutData[4]</c>）。</summary>
	internal bool TryReadByte(Function function, out byte value)
	{
		value = 0;
		try
		{
			byte[] outData = Invoke(MethodGet, function);
			if (outData == null || outData.Length <= 4)
			{
				return false;
			}
			value = outData[4];
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"宝龙达 WMI：读功能号 {(byte)function} 失败：{ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（"回读"需求）：⭐ **带回显校验**的单字节读 ✅
	/// <para>⭐⭐ 依据（⭐ 真机实测出来的技巧，⭐ 见决策笔记）：⭐ 宝龙达的回应头是
	/// ⭐ <c>00 80 00 &lt;功能号&gt;</c> ⇒ ⭐ **`OutData[3]` 会回显功能号** ✅
	/// ⇒ ⭐ 所以可以拿它确认"⭐ 这次回应**是不是我要的那一条**" ⚠️
	/// —— ⭐ 防止"⭐ 读 A 却拿到 B 的值"⭐ 这种静默错配 ❌。</para>
	/// <para>⚠️ **回显不符 ⇒ 返回 false**（⭐ ⛔ 不把可疑值当有效值 ✅），⭐ 并记一行日志 ✅。</para>
	/// <para>⭐ 用途：⭐ **状态回读/周期同步**（⭐ Fn 锁 · ⭐ 触摸板锁 · ⭐ 键盘灯状态 · ⭐ LOGO 灯 ·
	/// ⭐ 风扇手动开关 · ⭐ 功耗墙开关 · ⭐ 显卡模式 · ⭐ 交流类型 ✅）。</para>
	/// </summary>
	internal bool TryReadByteChecked(Function function, out byte value)
	{
		value = 0;
		try
		{
			byte[] outData = Invoke(MethodGet, function);
			if (outData == null || outData.Length <= 4)
			{
				return false;
			}
			// ⭐ 回显校验：⭐ 期望 OutData[3] == 功能号 ✅
			if (outData[3] != (byte)function)
			{
				Logger.WriteLine($"宝龙达 WMI：⚠️ 回显不符 —— 读功能号 {(byte)function}，"
					+ $"但回应里是 {(int)outData[3]}（⭐ OutData[3]）⇒ ⭐ **本次读数作废**（⛔ 不当有效值 ✅）");
				return false;
			}
			value = outData[4];
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("baolongda-readchecked", $"宝龙达 WMI：回读功能号 {(byte)function} 失败：{ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（"回读"需求）：⭐ **多字节**带回显校验读 ✅ —— ⭐ 用于 ⭐ 功能号 17（⭐ 键盘灯颜色 R,G,B ✅）
	/// <para>⭐ <paramref name="count" /> 是要取的字节数（⭐ 从 `OutData[4]` 起 ✅）。</para>
	/// <para>⚠️ 同样做 ⭐ `OutData[3]` **回显校验** ✅ —— ⭐ 不符就作废（⛔ 不返回可疑数据 ✅）。</para>
	/// </summary>
	internal bool TryReadBytesChecked(Function function, int count, out byte[] values)
	{
		values = null;
		if (count <= 0 || count > 8)
		{
			return false;
		}
		try
		{
			byte[] outData = Invoke(MethodGet, function);
			if (outData == null || outData.Length < 4 + count)
			{
				return false;
			}
			if (outData[3] != (byte)function)
			{
				Logger.WriteLine($"宝龙达 WMI：⚠️ 回显不符 —— 读功能号 {(byte)function}，"
					+ $"但回应里是 {(int)outData[3]}（⭐ OutData[3]）⇒ ⭐ **本次读数作废**（⛔ 不当有效值 ✅）");
				return false;
			}
			byte[] array = new byte[count];
			Array.Copy(outData, 4, array, 0, count);
			values = array;
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("baolongda-readbyteschecked", $"宝龙达 WMI：回读功能号 {(byte)function}（{count} 字节）失败：{ex.Message}");
			return false;
		}
	}
	/// <summary>写一个功能号的单字节值（<c>InData[4]</c>）。</summary>
	internal bool TryWriteByte(Function function, byte value)
	{		try
		{
			Invoke(MethodSet, function, new byte[1] { value });
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"宝龙达 WMI：写功能号 {(byte)function} = {value} 失败：{ex.Message}");
			return false;
		}
	}

	/// <summary>写两个字节（<c>InData[4]=第一、[5]=第二</c>）—— ⭐ 用于风扇（FanType + 值）与 CPUPower（子命令 + 值）。</summary>
	internal bool TryWriteTwo(Function function, byte first, byte second)
	{
		try
		{
			Invoke(MethodSet, function, new byte[2] { first, second });
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"宝龙达 WMI：写功能号 {(byte)function} = ({first}, {second}) 失败：{ex.Message}");
			return false;
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ① 性能模式（⭐ 与 LegacyMifsWmi 同一套映射 —— 见笔记 4.1）
	//     ⭐⭐ 2026-10-16 真机【实测证实】：⭐ 平衡→raw 0 · ⭐ 增强→raw 1 · ⭐ 静音→raw 2 ✅
	//        且静音档风扇明显更低（2743 RPM vs 3054）⇒ ⭐ 语义没搞反，⭐ 物理上也自洽 ✅
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>最近一次读/写是否成功（⭐ 与 <c>LegacyMifsWmi.Available</c> 同款语义 ✅）。</summary>
	internal bool Available { get; private set; }

	private long _version;

	/// <summary>
	/// 读一次档位并打包成 <see cref="LegacyMifsSnapshot" />。
	/// <para>⭐ 刻意**复用老接口的快照类型**（⭐ 协议同族 ✅）⇒ ⭐ <c>MechrevoHw</c> 的刷新/切档逻辑
	/// 可以**一套代码管两种后端**，⛔ 不必复制一份 ✅</para>
	/// <para>⚠️ 失败时置 <see cref="Available" />=<c>false</c> 并**原样抛出**（⭐ 与 <c>LegacyMifsWmi.ReadMode</c> 同款约定 ✅）。</para>
	/// </summary>
	internal LegacyMifsSnapshot ReadMode()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			try
			{
				// ⚠️ 这里**不能**调 TryReadOperatingMode（⭐ 它内部也要 lock _gate ⇒ ⭐ C# 的 lock 可重入，
				//    所以其实**可以**调；⭐ 但为清晰起见仍走它 ✅ —— lock 可重入这点已确认 ✅）
				if (!TryReadOperatingMode(out int operatingMode))
				{
					throw new IOException("宝龙达 MIFS 读档位失败（详见上一行日志）。");
				}
				Available = true;
				return new LegacyMifsSnapshot(operatingMode, ++_version);
			}
			catch
			{
				Available = false;
				throw;
			}
		}
	}

	/// <summary>
	/// 写档位 ＋ 内部最多 5 次回读确认（⭐ 150 ms 间隔）—— ⭐ 与 <c>LegacyMifsWmi.SwitchModeAsync</c> 同款 ✅
	/// <para>⭐ <paramref name="operatingMode" /> 取内部口径 <c>0..2</c>。</para>
	/// <para>⚠️ 真机实测：⭐ 该机型只有 3 档 ⇒ ⭐ 写 3 会被 <see cref="TrySetOperatingMode" /> 拒绝 ✅。</para>
	/// </summary>
	internal async Task<bool> SwitchModeAsync(int operatingMode, CancellationToken ct)
	{
		await Task.Run(delegate
		{
			ct.ThrowIfCancellationRequested();
			if (!TrySetOperatingMode(operatingMode))
			{
				throw new IOException("宝龙达 MIFS 写档位失败（详见上一行日志）。");
			}
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		int lastSeen = -1;
		for (int attempt = 0; attempt < 5; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			lastSeen = (await Task.Run((Func<LegacyMifsSnapshot>)ReadMode, ct).ConfigureAwait(continueOnCapturedContext: false)).OperatingMode;
			if (lastSeen == operatingMode)
			{
				return true;
			}
			await Task.Delay(150, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		// ⭐ 2026-10-17：⭐ 与 `LegacyMifsWmi.SwitchModeAsync` **同一口径** ——
		//   如实记下「写了什么 / 回读是什么」，这是判「固件不认」还是「映射不同」的唯一证据 ✅
		Logger.WriteError($"宝龙达 WMI：写档位内部值 {operatingMode} ⇒ 5 次回读仍是 {lastSeen}（每次间隔 150ms）"
			+ " ⇒ ⭐ 如实判为【未确认】（⚠️ 不是「写失败」：⛔ 本判据只证明回读不一致 ⚠️）");
		return false;
	}

	/// <summary>
	/// 读性能模式，⭐ 返回值与 <see cref="MechrevoHw.OperatingMode" /> 同一口径（⭐ `ConsoleOperatingMode`）：
	/// <c>0 = Office（办公/静音）· 1 = Gaming（游戏/平衡）· 2 = Turbo（狂暴）</c>。
	/// <para>⭐ 原始值 → 我们的口径（⭐ 与 <see cref="LegacyMifsWmi.ReadOperatingMode" /> **逐字一致**，⛔ 不另造）：
	/// 原始 <c>2 → 0</c> · <c>0 → 1</c> · <c>1 或 3 → 2</c>。</para>
	/// </summary>
	internal bool TryReadOperatingMode(out int operatingMode)
	{
		operatingMode = -1;
		try
		{
			byte[] outData = Invoke(MethodGet, Function.SystemPerMode);
			if (outData == null || outData.Length <= 4)
			{
				return false;
			}
			switch (outData[4])
			{
			case 0:
				operatingMode = 1;
				return true;
			case 1:
			case 3:
				operatingMode = 2;
				return true;
			case 2:
				operatingMode = 0;
				return true;
			default:
				// ⭐ 2026-10-17：⭐ 未知原始值必须**默认级别可见**（`WriteLine` 的级别由内容判定，
				//   不保证在「仅错误」下落盘 ⚠️）⇒ ⭐ 用 `WriteError`（⭐ 与 LegacyMifsWmi 同一口径 ✅）
				Logger.WriteError($"宝龙达 WMI：**未知的性能模式原始值 {outData[4]}**"
					+ $"（⭐ 响应长度={outData.Length} · 取值下标=4 ⇒ ⭐ 已知映射只覆盖原始 0/1/2/3"
					+ " ⇒ ⭐ 该机型映射可能与 40 系不同 ⚠️）请把这一行发回");
				return false;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：读性能模式失败：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 写性能模式（⭐ 输入口径同 <see cref="TryReadOperatingMode" />）。
	/// <para>⚠️ 原始值只有 0/1/2 三档（⭐ 蛟龙16Pro）；⭐ 联想那种 4 档机型多一个 <c>3</c>
	/// —— ⭐ 我们**不主动写 3**（⭐ 保守：⭐ 写错档可能让机器进不期望的状态 ⚠️）。</para>
	/// </summary>
	internal bool TrySetOperatingMode(int operatingMode)
	{
		byte raw;
		switch (operatingMode)
		{
		case 0:
			raw = 2; // Office/静音 → MIFS 2 = Quiet
			break;
		case 1:
			raw = 0; // Gaming/平衡 → MIFS 0 = Balance
			break;
		case 2:
			raw = 1; // Turbo/狂暴 → MIFS 1 = Performance
			break;
		default:
			Logger.WriteLine($"宝龙达 WMI：拒绝写不支持的口径值 {operatingMode}（⭐ 只接受 0/1/2）");
			return false;
		}
		return TryWriteByte(Function.SystemPerMode, raw);
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ② 风扇
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>风扇转速快照（⭐ 单位 RPM；-1 = 该通道不存在/读不到）。</summary>
	internal readonly struct FanSpeeds
	{
		internal FanSpeeds(int first, int second, int third)
		{
			First = first;
			Second = second;
			Third = third;
		}

		/// <summary>⭐ 蛟龙16Pro：**GPU 扇**；⭐ 联想 3 扇机型：**大扇 1**。</summary>
		internal int First { get; }

		/// <summary>⭐ 蛟龙16Pro：**CPU 扇**；⭐ 联想 3 扇机型：**大扇 2**。</summary>
		internal int Second { get; }

		/// <summary>⭐ 仅 3 扇机型有（**小扇**）；⭐ 2 扇机型为 -1。</summary>
		internal int Third { get; }
	}

	/// <summary>
	/// 读风扇转速（功能号 13）。
	/// <para>⭐ 布局（⭐ 三份实现一致）：<c>OutData[4..5]</c> 第 1 个 · <c>[6..7]</c> 第 2 个 · <c>[10..11]</c> 第 3 个（⭐ 仅 3 扇机型）。</para>
	/// <para>⭐ 16 位**小端**（<c>(hi &lt;&lt; 8) | lo</c>，⭐ 其中 hi=高地址字节）。</para>
	/// </summary>
	internal bool TryReadFanSpeeds(out FanSpeeds speeds)
	{
		speeds = new FanSpeeds(-1, -1, -1);
		try
		{
			byte[] outData = Invoke(MethodGet, Function.CpuGpuFanSpeed);
			if (outData == null || outData.Length < 8)
			{
				return false;
			}
			int first = (outData[5] << 8) + outData[4];
			int second = (outData[7] << 8) + outData[6];
			int third = (outData.Length >= 12) ? ((outData[11] << 8) + outData[10]) : -1;
			speeds = new FanSpeeds(first, second, third);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：读风扇转速失败：" + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// ⭐ 风扇手动上限 开/关（功能号 20）。
	/// <para>⚠️⚠️ **重要**：⭐ 参考实现明确注明「写风扇前**先关掉** <c>MaxFanSpeedSwitch</c>，
	/// 是为**避免与官方控制台冲突**」⇒ ⭐ 我们接管前**也应先关它**（⭐ 见 <see cref="PrepareFanManualControl" />）✅</para>
	/// </summary>
	internal bool TrySetMaxFanSpeedSwitch(bool on)
	{
		return TryWriteByte(Function.MaxFanSpeedSwitch, on ? (byte)1 : (byte)0);
	}

	/// <summary>读 风扇手动上限 开关状态。</summary>
	internal bool TryReadMaxFanSpeedSwitch(out bool on)
	{
		on = false;
		if (!TryReadByte(Function.MaxFanSpeedSwitch, out byte raw))
		{
			return false;
		}
		on = raw == 1;
		return true;
	}

	/// <summary>
	/// ⭐ 设风扇手动上限转速（功能号 21）。
	/// <para>⭐ <paramref name="fanType" />：<c>0 = 大扇（蛟龙=GPU / 联想=大扇1）</c> · <c>1 = 小扇</c>。</para>
	/// <para>⭐ <paramref name="speedUnits100Rpm" /> 单位是 ⭐ **x100 RPM**（⭐ 例：35 ⇒ 3500 RPM ✅）。</para>
	/// </summary>
	internal bool TrySetMaxFanSpeed(byte fanType, byte speedUnits100Rpm)
	{
		return TryWriteTwo(Function.MaxFanSpeed, fanType, speedUnits100Rpm);
	}

	/// <summary>
	/// ⭐ 进入"我们来管风扇"之前的**准备动作**：⭐ 若官方控制台的手动开关是开的，⭐ 先关掉它。
	/// <para>⭐ 依据：参考实现原话「此处为兼容性设置，**为避免官方控制台冲突设计**」✅</para>
	/// <para>⛔ 只读 + 一次可能的写；⭐ 失败不影响调用方继续 ✅</para>
	/// </summary>
	internal bool PrepareFanManualControl()
	{
		if (!TryReadMaxFanSpeedSwitch(out bool on))
		{
			// 读不到 ⇒ 保守：仍然尝试下发一次"关"
			Logger.WriteLine("宝龙达 WMI：读不到风扇手动开关状态 ⇒ 仍先下发一次「关」（⭐ 避免与官方控制台冲突）");
			return TrySetMaxFanSpeedSwitch(false);
		}
		if (!on)
		{
			return true; // 本来就是关的 ✅
		}
		Logger.WriteLine("宝龙达 WMI：官方控制台的手动风扇开关是**开**的 ⇒ 先关掉它（⭐ 避免两边打架）");
		return TrySetMaxFanSpeedSwitch(false);
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ③ CPU 温度
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 读 CPU 温度（功能号 22，⭐ <c>OutData[4]</c> 摄氏度）。
	/// <para>⭐ **上界校验**（⭐ 取自 Bellator 的做法）：⭐ <c>0</c> 与 <c>255</c> 都视为**无效**（⭐ 免得把无效读数当 0℃）✅</para>
	/// </summary>
	internal bool TryReadCpuTemperature(out int celsius)
	{
		celsius = -1;
		try
		{
			byte[] outData = Invoke(MethodGet, Function.CpuThermometer);
			if (outData == null || outData.Length <= 4)
			{
				return false;
			}
			byte raw = outData[4];
			if (raw == 0 || raw == 255)
			{
				return false; // ⭐ 无效读数（⭐ 参考实现同款判定）
			}
			celsius = raw;
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：读 CPU 温度失败：" + ex.Message);
			return false;
		}
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ④ CPU 功耗墙 / 温度墙（⭐ 蛟龙16Pro 独有）
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>⭐ 读"自定义功耗是否已开启"（功能号 23，<c>OutData[4] == 1</c>）。</summary>
	internal bool TryReadCpuCustomEnabled(out bool enabled)
	{
		enabled = false;
		if (!TryReadByte(Function.CpuPower, out byte raw))
		{
			return false;
		}
		enabled = raw == (byte)CpuPowerSub.OpenState;
		return true;
	}

	/// <summary>⭐ 开/关自定义功耗（功能号 23，写**单字节**）。</summary>
	internal bool TrySetCpuCustomEnabled(bool enabled)
	{
		return TryWriteByte(Function.CpuPower, enabled ? (byte)CpuPowerSub.OpenState : (byte)CpuPowerSub.CloseState);
	}

	/// <summary>⭐ 设 PL1 长时功耗（⭐ 单位瓦特）。</summary>
	internal bool TrySetCpuLongPower(byte watts)
	{
		return TryWriteTwo(Function.CpuPower, (byte)CpuPowerSub.LongPower, watts);
	}

	/// <summary>⭐ 设 PL2 短时功耗（⭐ 单位瓦特）。</summary>
	internal bool TrySetCpuShortPower(byte watts)
	{
		return TryWriteTwo(Function.CpuPower, (byte)CpuPowerSub.ShortPower, watts);
	}

	/// <summary>⭐ 设 CPU 温度墙（⭐ 单位摄氏度）。</summary>
	internal bool TrySetCpuTempWall(byte celsius)
	{
		return TryWriteTwo(Function.CpuPower, (byte)CpuPowerSub.CpuTempWallState, celsius);
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ⑤ 显卡模式（⭐ 2026-10-16 · 用户要求「⭐ GPUmode接入 宝龙达机器就显示两个按钮就行」✅）
	//     ⭐ 协议：⭐ 功能号 9 ✅ · ⭐ 读 `InData[1]=250` / ⭐ 写 `251` ⇒ ⭐ `InData[4]` = 值 ✅
	//     ⭐ 取值（⭐ 参考实现 `GpuMode` 枚举 ✅）：⭐ **0 = 混合模式（Hybrid）** ·
	//        ⭐ **1 = 独显直连（Discrete）** ⇒ ⭐ **只有这两个** ✅
	//     ⚠️ 真机**尚未验证**功能号 9 ⚠️ ⇒ ⭐ 所以调用方必须**先读一次**再写 ✅
	//        （⭐ 与键盘灯那套同一个保护思路 ✅）
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>⭐ 混合模式（⭐ 参考实现 `GpuMode.HybridMode = 0` ✅）。</summary>
	internal const int GpuModeHybrid = 0;

	/// <summary>⭐ 独显直连（⭐ 参考实现 `GpuMode.DiscreteMode = 1` ✅）。</summary>
	internal const int GpuModeDiscrete = 1;

	/// <summary>
	/// ⭐ 读显卡模式（⭐ 功能号 9 ✅）。
	/// <para>⭐ 返回值：⭐ 0 = 混合 · ⭐ 1 = 独显直连 ✅（⭐ 其它值一律当读失败 —— ⭐ 因为我们只知道这两个 ✅）。</para>
	/// <para>⭐ 用 <see cref="TryReadByteChecked" /> ⇒ ⭐ 带 **`OutData[3]` 回显校验** ✅。</para>
	/// </summary>
	internal bool TryReadGpuMode(out int mode)
	{
		mode = -1;
		if (!TryReadByteChecked(Function.GpuMode, out byte raw))
		{
			return false;
		}
		if (raw != GpuModeHybrid && raw != GpuModeDiscrete)
		{
			// ⚠️ 未知取值：⭐ 如实记下来（⭐ 说不定别的机型有第三档 ⚠️），⭐ 但⛔ 不当作有效值 ✅
			Logger.WriteLineIfChanged("baolongda-gpumode-unknown",
				$"宝龙达 显卡模式：⚠️ 读到**未知取值** {raw}（⭐ 已知只有 0=混合 / 1=独显直连 ⚠️）⇒ ⭐ 当作读不到 ✅");
			return false;
		}
		mode = raw;
		return true;
	}

	/// <summary>
	/// ⭐ 写显卡模式（⭐ 功能号 9 ✅）。
	/// <para>⚠️ <b>写前保护</b>：⭐ 先 <see cref="TryReadGpuMode" /> 试读一次 ⇒ ⭐ **读不到就拒绝盲写** ✅
	/// （⭐ 因为功能号 9 真机未验证 ⚠️ —— ⭐ 与键盘灯先读功能号 10 是同一个道理 ✅）。</para>
	/// <para>⭐ 只接受 ⭐ <see cref="GpuModeHybrid" /> / ⭐ <see cref="GpuModeDiscrete" /> ✅（⭐ 其它一律拒绝 ✅）。</para>
	/// </summary>
	internal bool TrySetGpuMode(int mode)
	{
		if (mode != GpuModeHybrid && mode != GpuModeDiscrete)
		{
			Logger.WriteLine($"宝龙达 显卡模式：⭐ 拒绝写入不支持的值 {mode}（⭐ 只接受 0=混合 / 1=独显直连 ✅）");
			return false;
		}
		// ⭐ 写前保护：⭐ 读得到才写 ✅
		if (!TryReadGpuMode(out int current))
		{
			Logger.WriteLine("宝龙达 显卡模式：⚠️ **读不到当前模式** ⇒ ⭐ 拒绝盲写（⭐ 功能号 9 真机未验证 ⚠️）"
				+ " ⇒ ⭐ 本次不动，⛔ 一个字节都不写 ✅");
			return false;
		}
		if (current == mode)
		{
			Logger.WriteLine($"宝龙达 显卡模式：⭐ 已经是目标值 {mode} ⇒ ⛔ 无需写入 ✅");
			return true;
		}
		return TryWriteByte(Function.GpuMode, (byte)mode);
	}

	// ══════════════════════════════════════════════════════════════════════
	//  ⑥ 灯效（⭐ 蛟龙16Pro 独有）
	// ══════════════════════════════════════════════════════════════════════

	/// <summary>⭐ LOGO 灯开/关（功能号 15）。</summary>
	internal bool TrySetLogoLight(bool on)
	{
		return TryWriteByte(Function.AmbientLight, on ? (byte)1 : (byte)0);
	}

	/// <summary>⭐ 读 LOGO 灯状态。</summary>
	internal bool TryReadLogoLight(out bool on)
	{
		on = false;
		if (!TryReadByte(Function.AmbientLight, out byte raw))
		{
			return false;
		}
		on = raw == 1;
		return true;
	}

	/// <summary>⭐ 键盘灯颜色（功能号 17，⭐ 写 3 字节 R,G,B）。</summary>
	internal bool TrySetKeyboardColor(byte r, byte g, byte b)
	{
		try
		{
			Invoke(MethodSet, Function.RgbKeyboardColor, new byte[3] { r, g, b });
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达 WMI：写键盘灯颜色失败：" + ex.Message);
			return false;
		}
	}

	/// <summary>⭐ 键盘灯亮度（功能号 18，⭐ 0~3 级）。</summary>
	internal bool TrySetKeyboardBrightness(byte level0to4)
	{
		// ⭐⭐ 2026-10-17（依据：第三方 JiaoLong7.3 的 `WMIRGBKeyboardBrightnessLevel` = Level_0..Level_4）：
		//   ⭐ 亮度值域是 **0~4（5 档）**，不是 0~3 ⚠️
		//   ⇒ 原来 `> 3 ? 3 : v` 会把 40 系的第 4 档**静默夹成 3** ⇒ 看起来"亮度调不上去" ❌
		//   ⚠️ 上限判据交给调用方（`BaolongdaBrightnessFromUi` 按 GPU 代数决定 30 系 3 / 40 系 4）✅
		//      ⭐ 这里只做最后的 0~4 安全夹，⛔ 不再按 3 夹 ✅
		return TryWriteByte(Function.RgbKeyboardBrightness, level0to4 > 4 ? (byte)4 : level0to4);
	}

	/// <summary>⭐ 键盘灯模式（功能号 16）—— ⭐ <c>0 = 关</c> · <c>2 = RGB 固定色</c>（⭐ 参考实现只用到这两个）。</summary>
	internal bool TrySetKeyboardMode(byte mode)
	{
		return TryWriteByte(Function.RgbKeyboardMode, mode);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
		}
	}
}
