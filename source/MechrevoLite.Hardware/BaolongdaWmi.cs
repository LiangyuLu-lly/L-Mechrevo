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
			// ⭐ 活体证明：⭐ 真读一次 CPU 温度（⭐ 只读）
			if (!created.TryReadCpuTemperature(out int celsius))
			{
				Logger.WriteLine("宝龙达 WMI 探测：实例打开了但读不到 CPU 温度 ⇒ 判为不可用（⛔ 不接管）");
				created.Dispose();
				return null;
			}
			// ⭐ 实测风扇个数与模式档数（⭐ 只读；⭐ 用返回长度/有效性判断，⛔ 不猜）
			created.ProbeCapabilities();
			Logger.WriteLine($"宝龙达 WMI 探测成功：⭐ 实例=\"{created._instancePath}\" · ⭐ 类GUID={created.ClassGuid} · "
				+ $"⭐ 风扇={created.FanCount} 个 · ⭐ 模式={created.ModeCount} 档 · CPU温度={celsius}℃");
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
			// ⭐ 这个类根本不存在 ⇒ 静默（⭐ 现有机型每次启动都会走到这里，⛔ 一行日志都不该打 ✅）
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

	/// <summary>写一个功能号的单字节值（<c>InData[4]</c>）。</summary>
	internal bool TryWriteByte(Function function, byte value)
	{
		try
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
		for (int attempt = 0; attempt < 5; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			if ((await Task.Run((Func<LegacyMifsSnapshot>)ReadMode, ct).ConfigureAwait(continueOnCapturedContext: false)).OperatingMode == operatingMode)
			{
				return true;
			}
			await Task.Delay(150, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
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
				Logger.WriteLine($"宝龙达 WMI：未知的性能模式原始值 {outData[4]}（⭐ 请把这一行发回）");
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
	//  ⑤ 灯效（⭐ 蛟龙16Pro 独有）
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
	internal bool TrySetKeyboardBrightness(byte level0to3)
	{
		return TryWriteByte(Function.RgbKeyboardBrightness, level0to3 > 3 ? (byte)3 : level0to3);
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
