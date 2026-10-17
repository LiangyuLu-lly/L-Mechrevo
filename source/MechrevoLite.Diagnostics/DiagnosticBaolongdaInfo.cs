// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · ⭐ 临时诊断段）：
// ⭐⭐ `DiagnosticBaolongdaInfo` —— ⭐ **把宝龙达的扫描放进诊断包** ✅
//
// ⭐ 用户 2026-10-16 原话：「⭐ 把宝龙达的扫临时写入诊断里面」✅
//
// ⭐⭐ 它做什么（⭐ 全部**只读** ✅ · ⛔ 一个 Set 都不发 ✅）：
//    ⭐ ① ⭐ **机型白名单取证**：⭐ 打印 `BaseBoardProduct` ＋ ⭐ 白名单判定结果 ✅
//       ⇒ ⭐ 机主跑一次诊断就能确认"⭐ 我这台到底认没认成宝龙达、⭐ 为什么" ✅
//    ⭐ ② ⭐ **功能号 8~23 逐个试读** ⇒ ⭐ 每个都打印「⭐ 可读（值 N）/ ⚠️ 读不到」✅
//       ⭐ 且 ⭐ 走**带回显校验**的读 ⇒ ⭐ 回显不符也如实标出 ✅
//    ⭐ ③ ⭐ **结论清单**：⭐ 哪些功能号通（⭐ 复用 `BaolongdaStateSnapshot.DescribeSupportList()` ✅）
//    ⭐ ④ ⭐ **`\\.\ACPIDriver` 只读探测**（⭐ 判断限充那条 EC 通道在不在 ✅）
//    ⭐ ⑤ ⭐ **HID 事件如实说明**（⭐ 我们**没实现** ✅ —— ⭐ ⛔ 不假装 ✅）
//
// ⭐ 2026-10-16 追加（⭐ 用户批准：「⭐ 先把诊断功能加入EC版本的读取 ⭐ 限制充电暂缓」✅）：
//    ⭐ ②b ⭐ **EC 版本只读试读**（⭐ XRAM `0x8410~0x8413` ✅）—— ⭐ 限充调研确认「蛟龙上能做限充」，
//       但**只核对过 EC `00.28.00.00` 这一个版本** ⚠️，而 XRAM 地址会随固件重构变化 ⇒ 做限充前先核对版本 ✅
//       ⚠️ `0x8410` 属 **XRAM**，与既有 H2RAM `0x700–0x7FF` 是**两套地址空间** ⇒ 本节**实测它读不读得到** ✅
//       ⭐ 且**用一条对照读**（H2RAM 已知可读的门控 `0x07C3`）把「通道不通」与「地址不可达」分开 ⚠️
//    ⭐ ②c ⭐ **`AcpiTest_MULong` 探索结论**（⭐ 架构表里「Legacy Tongfang Mulong」那条免驱 WMI ✅）——
//       ⚠️ 本机实测（2026-10-16 实跑）：**类存在、实例数 = 10** ⚠️ ——
//          ⛔ 而 `Get-CimInstance` 口径查不到任何实例 ⇒ ⭐ **那个口径是假否定、不能用** ⚠️（本段改用 `ManagementObjectSearcher` ✅）
//       ⇒ ⭐ 但**仍不可用**：⭐ 它的方法签名（GetULong/SetULong/FireULong/GetSetULong/GetButton）
//          **没有「地址＋值」结构** ⇒ ⭐ 那是 ACPI **通用测试钩子**、⛔ 不是 EC 读写接口 ✅
//       ⚠️ 其中 `FireULong(Hack)` 是**有副作用**的钩子 ⇒ ⛔ 绝不能调用 ✅
//    ⚠️ 以上两项**全程只读**：⛔ 不写任何寄存器、⛔ 不改限充行为（⭐ 限充当前【暂缓】✅）
//
// ⚠️⚠️ **隐私**：⭐ 本段 ⛔ **不含**序列号 / ⭐ 用户名 / ⭐ 机器名 / ⭐ MAC ✅
//    ⭐ 只含 ⭐ 主板型号（⭐ 它已在诊断包里用于机型判定 ✅）
//
// ⚠️ 标记「⭐ 临时」：⭐ 用户说"临时写入"✅ ⇒ ⭐ 定位清楚后可按需撤掉 ✅
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Management;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>⭐ 宝龙达临时诊断段（⭐ 见文件头注释 ✅）。</summary>
internal static class DiagnosticBaolongdaInfo
{
	/// <summary>⭐ 生成诊断文本（⭐ 只读 ✅ · ⭐ 全程 try/catch ⇒ ⭐ 出错不影响诊断包其余部分 ✅）。</summary>
	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("════════════════════════════════════════════════════════");
		sb.AppendLine("⭐ 宝龙达（蛟龙16Pro 等）诊断段（⭐ 临时 · ⭐ 全程只读 ⛔ 不下发任何命令）");
		sb.AppendLine("════════════════════════════════════════════════════════");
		try
		{
			AppendWhitelist(sb);
			AppendWmiClassProbe(sb);
			AppendDriverProbe(sb);
			AppendEcVersionProbe(sb);
			AppendEcPortIoProbe(sb);
			AppendEcStandardSpaceProbe(sb);
			AppendEcLpcIoProbe(sb);
			AppendFunctionProbe(sb);
			AppendHidNote(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ 宝龙达诊断段异常（已吞掉，不影响诊断包其余部分）：" + ex.Message);
		}
		return sb.ToString();
	}

	// ── ① 机型白名单取证（⭐ 让机主一次看清"认没认成宝龙达、为什么" ✅）──
	private static void AppendWhitelist(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ① 机型白名单（⭐ 判宝龙达**只看板号** ⛔ 不看是不是蛟龙、⛔ 不看 30/40 系）──");
		string board = ReadBaseBoardProduct();
		sb.AppendLine("主板型号 BaseBoardProduct = " + (board.Length == 0 ? "（读不到）" : board));
		bool hit = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string note);
		sb.AppendLine("白名单判定 = " + (hit ? "✅ 命中 ⇒ 本机可认作宝龙达" : "⛔ 未命中 ⇒ 本机按同方/GCU 处理"));
		sb.AppendLine("判定说明 = " + note);
		sb.AppendLine("⭐ 判据依据：⭐ 宝龙达板号为 `MRID6`（⭐ 裸板号 · 2026-10-17 真机：蛟龙16 · 6800H＋3070Ti）、"
			+ "`MRID6-23`（⭐ 40 系 · 蛟龙16Pro · 真机证实）、`MRID6-*`（⭐ 后续变体）；"
			+ "⛔ 同方机型实测板号为 GM5HG0A / GM6IX8B / KUANGSHI-* / YAOSHI-* / Standard ⇒ ⭐ 均**不命中** ✅");
		sb.AppendLine("⚠️ 历史踩坑（⭐ 2026-10-17）：⭐ 旧判据只认「精确 `MRID6-23`」＋「前缀 `MRID6-`」"
			+ "⇒ ⭐ **裸 `MRID6` 两条都不命中** ❌ ⇒ ⭐ 整台机退回 legacy 后端 ⇒ ⭐ 一堆功能「点了没反应」⚠️"
			+ "（⭐ 现已修复：⭐ 裸 `MRID6` 精确命中 ✅）");
		sb.AppendLine("⚠️ 仍待逐台证实的：⭐ `MRID6-*` 里**除 `MRID6` / `MRID6-23` 之外**的变体"
			+ "（⭐ 这两个已真机证实 ✅）⇒ ⭐ 若某个变体其实是同方模具，⭐ 届时把它加进排除表 ✅");
	}

	private static string ReadBaseBoardProduct()
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	// ── ①b `root\WMI:MICommonInterface` 类/实例存在性（⭐ 识别链的**第一步** ⚠️）──
	//    ⚠️ 2026-10-16 群里反馈：「⭐ 一卸载官方控制台这个软件就识别不了了」⚠️
	//    ⇒ ⭐ 这条反馈原来**无法证伪**：⭐「类不存在」那条分支是【完全静默】的 ❌ ·
	//      ⭐ 而诊断段也**从不打印**这个类 ⇒ ⭐ 我们手上没有任何证据 ⚠️
	//    ⇒ ⭐ 本段 ＋ ⭐ 运行时那条 `Logger.WriteError` 一起补上这个盲点 ✅
	private static void AppendWmiClassProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ①b `root\\WMI:MICommonInterface`（⭐ 宝龙达识别链的**第一步** ⚠️）──");
		sb.AppendLine("⭐ 用途：⭐ 这个类**在不在**，直接决定我们能否认出宝龙达 ✅");
		sb.AppendLine("⚠️ 已知成因：⭐ 群里反馈 **卸载官方控制台（JiaoLong Control）后可能消失** ⚠️"
			+ "（⭐ 新增本段就是为了证实/证伪它 ✅）");
		try
		{
			using ManagementClass mc = new ManagementClass("root\\WMI:MICommonInterface");
			try
			{
				mc.Get();
			}
			catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
			{
				sb.AppendLine("类存在性 = ⛔ **不存在** ❌ ⇒ ⭐ ⭐ **这就是认不出宝龙达的原因** ✅");
				sb.AppendLine("   ⇒ ⭐ 运行时也会有一条【默认级别可见】的日志（⭐ 仅板号为 `MRID6-*` 候选时打 ✅）");
				// ⚠️ ⭐ **这一支最需要旁证** ⚠️ ⇒ ⭐ 打完旁证再返回 ✅（⛔ 不能直接 return ❌）
				AppendSideEvidence(sb);
				return;
			}
			sb.AppendLine("类存在性 = ✅ 存在 ⇒ ⭐ 第一步通过 ✅");
			try
			{
				object guid = mc.Qualifiers["Guid"]?.Value;
				sb.AppendLine("类 GUID = " + (guid?.ToString() ?? "（⚠️ 无 GUID 限定符）"));
			}
			catch
			{
				sb.AppendLine("类 GUID = （读取失败）");
			}
			try
			{
				int count = 0;
				using ManagementObjectCollection instances = mc.GetInstances();
				foreach (ManagementBaseObject inst in instances)
				{
					string relative = (inst as ManagementObject)?.Path?.RelativePath;
					sb.AppendLine("  实例[" + count + "] = " + (string.IsNullOrEmpty(relative) ? "（无路径）" : relative));
					count++;
				}
				sb.AppendLine("实例个数 = " + count
					+ (count == 0 ? " ⚠️ ⇒ 类在但**没有任何实例**（⭐ 识别同样会失败 ⚠️）" : " ✅"));
			}
			catch (Exception ex)
			{
				sb.AppendLine("实例枚举失败 = " + ex.Message);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("类探测异常 = " + ex.Message);
		}
		// ⭐ 旁证段（⭐ **两种情形都要打** ⚠️ —— ⭐ 尤其"类不存在"⭐ 那一支最需要它 ✅）
		AppendSideEvidence(sb);
	}

	/// <summary>
	/// ⭐ 旁证：⭐ 官方控制台（⭐ JiaoLong Control ✅）/ ⭐ 它的驱动 / ⭐ `PNP0C14` 设备 / ⭐ `WmiAcpi.sys`
	/// 的存在性 ✅ —— ⭐ 全部只读 ⚠️。
	/// <para>⚠️⚠️ **必须在"类不存在"时也打印** ✅ —— ⭐ 那正是最需要旁证的场景 ⚠️
	/// （⭐ 群里"卸载官方控制台后识别不了"⭐ 的反馈就落在那一支 ✅）。</para>
	/// <para>⭐ 真机实测（⭐ 同方/GCU 机型 ✅）：⭐ **有 `PNP0C14` 设备但【没有】`MICommonInterface` 类** ✅
	/// ⇒ ⭐ 所以「设备在 ≠ 类在」⚠️ ⇒ ⭐ 两者必须**分别打印**才能分辨断在哪一步 ✅。</para>
	/// </summary>
	private static void AppendSideEvidence(StringBuilder sb)
	{
		// ⭐ 旁证：⭐ 官方控制台/它的驱动在不在（⭐ 用于和"卸载后失效"的反馈对照 ✅ · ⭐ 只查存在性 ⚠️）
		try
		{
			string[] paths = new string[3]
			{
				"C:\\Program Files\\JiaoLongControl",
				"C:\\Program Files\\JiaoLongControl\\JiaoLongDriver64.sys",
				"C:\\Windows\\System32\\drivers\\WmiAcpi.sys"
			};
			for (int k = 0; k < paths.Length; k++)
			{
				string what = (k == 0)
					? "官方控制台目录"
					: ((k == 1) ? "官方控制台驱动" : "WmiAcpi.sys（⭐ ACPI MOF ⇒ WMI 类 的提供者 ✅）");
				bool exists = (k == 0) ? Directory.Exists(paths[k]) : File.Exists(paths[k]);
				sb.AppendLine("旁证·" + what + " = " + (exists ? "✅ 在" : "⛔ 不在"));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("旁证探测异常 = " + ex.Message);
		}
		// ⭐ 旁证·`PNP0C14` 设备（⭐ **关键判据** ⚠️ · ⭐ 真机实测：⭐ 同方机型【有】`PNP0C14` 但【没有】该类 ✅）
		try
		{
			int deviceCount = 0;
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
				"root\\CIMV2", "SELECT DeviceID,Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\\\PNP0C14%'");
			using ManagementObjectCollection rows = searcher.Get();
			foreach (ManagementBaseObject row in rows)
			{
				sb.AppendLine("旁证·PNP0C14 设备[" + deviceCount + "] = " + row["DeviceID"]
					+ " · 状态=" + row["Status"]);
				deviceCount++;
			}
			sb.AppendLine("旁证·PNP0C14 设备个数 = " + deviceCount
				+ (deviceCount == 0
					? " ⚠️ ⇒ 连 ACPI-WMI 映射设备都没有（⭐ 少见 ⚠️）"
					: " ✅（⭐ 注意：⭐ 有设备**不代表**有 `MICommonInterface` 类 ⚠️ —— ⭐ 类由**该机 BIOS 的 ACPI MOF** 定义 ✅）"));
		}
		catch (Exception ex)
		{
			sb.AppendLine("旁证·PNP0C14 设备查询失败 = " + ex.Message);
		}
	}

	// ── ② `\\.\ACPIDriver` 只读探测（⭐ 限充那条 EC 通道 ✅）──
	private static void AppendDriverProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ② `\\.\\ACPIDriver`（⭐ UWACPIDriver）只读探测 ──");
		sb.AppendLine("⭐ 用途：⭐ 限充（EC 通道）与机型码读取都靠它 ✅");
		// ⚠️ 这里只查**驱动文件在不在** ✅（⭐ 真正的通道探测由 `EcChargeLimit` 负责 ✅ ——
		//    ⭐ 本段刻意**不**去打开设备，⛔ 免得在诊断路径里引入副作用 ⚠️）
		string[] candidates = new string[3]
		{
			"\\\\?\\GLOBALROOT\\Device\\ACPIDriver",
			"C:\\Windows\\System32\\drivers\\UWACPIDriver.sys",
			"C:\\Program Files\\L-Mechrevo\\UWACPIDriver\\UWACPIDriver.sys"
		};
		foreach (string path in candidates)
		{
			bool exists = false;
			try
			{
				exists = File.Exists(path);
			}
			catch
			{
			}
			sb.AppendLine("  " + (exists ? "✅ 在" : "⚠️ 不在") + " ： " + path);
		}
		sb.AppendLine("⭐ 说明：⭐ 设备对象那条路径用 `File.Exists` 查不到是**正常**的 ✅"
			+ "（⭐ 它要走 `CreateFile` 打开 ✅）⇒ ⭐ 限充到底可不可用，"
			+ "⭐ 以设置页「限充」那一项**能不能点**、⭐ 以及运行日志里 `EcChargeLimit` 的探测行为为准 ✅");
	}

	// ── ②b EC 版本（XRAM 0x8410~0x8413）只读试读 ＋ ②c `AcpiTest_MULong` 探索结论 ──
	// 2026-10-16（用户批准：「先把诊断功能加入EC版本的读取 限制充电暂缓」）：
	//   限充可行性调研已确认「蛟龙上能做限充」，但**只核对过 EC 00.28.00.00 这一个版本** ⚠️，
	//   而那个项目的原理文档指出 XRAM 地址会随固件重构变化
	//   ⇒ ⭐ 做任何限充相关工作前，必须先知道机主的 EC 版本对不对得上 ✅。
	// ⚠️⚠️ 地址空间：0x8410 属 **XRAM**，而我们既有的 `\\.\ACPIDriver` 通道按文档只覆盖
	//   **H2RAM 0x700–0x7FF**（见 `EcChargeLimit.ProbeRegisters` 的 channelNote）
	//   ⇒ ⭐ 本节就是**实测这个地址到底读不读得到**（⛔ 全程只读 · ⛔ 不写任何寄存器）✅。
	// ⚠️ 而且**必须能把两种失败分开**（⭐ 否则以后查不出卡在哪 ⚠️）：
	//   · 对照读（H2RAM 已知可读的 0x07C3）也失败 ⇒ 设备/IOCTL 这条链本身不通（与地址无关）
	//   · 对照读成功、而 0x8410 全失败 ⇒ 地址不在当前通道可达范围（⭐ 这正是本节最想知道的 ✅）
	private static void AppendEcVersionProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ②b EC 版本（XRAM 0x8410~0x8413）只读试读 ──");
		sb.AppendLine("⭐ 用途：限充可行性调研确认「蛟龙上能做限充」，但**只核对过 EC 00.28.00.00 这一个版本** ⚠️");
		sb.AppendLine("   ⇒ 那个项目的原理文档指出 XRAM 地址会随固件重构变化 ⇒ 做限充前必须先核对版本 ✅");
		sb.AppendLine("⚠️ 地址空间说明：0x8410 属 **XRAM**，而我们的 `\\.\\ACPIDriver` 通道按文档只覆盖 **H2RAM 0x700–0x7FF** ⚠️");
		sb.AppendLine("   ⇒ 本节**实测该地址到底读不读得到**（⛔ 只读 · ⛔ 不写任何寄存器）✅");
		sb.AppendLine();
		try
		{
			// ① 对照读：H2RAM 里一个已知可读的寄存器（门控 0x07C3）—— 用来把两种失败区分开 ✅
			bool controlOk = EcChargeLimit.TryReadByteAt(EcChargeLimit.GateRegister, out int controlValue);
			sb.AppendLine("对照读（H2RAM 已知可读 · 0x" + EcChargeLimit.GateRegister.ToString("X3") + "）= "
				+ (controlOk ? ("✅ 可读，值 = " + ChargeLimitEcProbe.FormatRaw(controlValue)) : "⛔ 读不到"));
			sb.AppendLine("   （⭐ 对照读的作用：把「通道不通」与「地址不可达」分开 —— ⛔ 否则两种失败长得一样 ⚠️）");

			// ② 逐个试读 XRAM 0x8410~0x8413
			int[] bytes = new int[4];
			int okCount = 0;
			for (int i = 0; i < bytes.Length; i++)
			{
				int address = EcChargeLimit.XramEcVersionRegister + i;
				bool ok = EcChargeLimit.TryReadByteAt(address, out int v);
				bytes[i] = (ok ? v : (-1));
				if (ok)
				{
					okCount++;
				}
				sb.AppendLine("试读 XRAM 0x" + address.ToString("X4") + " = "
					+ (ok ? ("✅ 可读，原始字节 = " + ChargeLimitEcProbe.FormatRaw(v)) : "⛔ 读不到"));
			}
			sb.AppendLine();

			// ③ 结论（⭐ 五态分明 ✅ —— ⚠️ 多出的那一态正是**实跑实测**发现的「全 0xFF 填充值」✅）
			// ⚠️⚠️【本节实测得到的关键判据】全 0xFF **不等于**读到了真实版本 ⚠️：
			//   本机（同方翼龙15Pro · 2026-10-16 实跑）实测：对照读 0x07C3 得到真实值 0x07 ✅，
			//   而 0x8410~0x8413 **四个字节全部返回 0xFF** —— 与既有的 0x0770 / 0x087F 的 0xFF 是**同一个签名**
			//   （`EcChargeLimit` 里那段注释早就写过：0xFF 更可能是「该地址未映射时的填充值」）。
			//   ⇒ ⭐ **IOCTL 返回成功 ≠ 地址可达** —— 本通道对窗口外的地址**不报错**、而是回填充值 ⚠️
			//   ⇒ ⭐ 所以**只有「不是全 0xFF」才算真正读到了版本** ✅
			//      （⛔ 否则会把 `FF.FF.FF.FF` 当成版本号报出去 —— 那是**编造读数** ⚠️）
			int ffCount = 0;
			for (int i = 0; i < bytes.Length; i++)
			{
				if (bytes[i] == 255)
				{
					ffCount++;
				}
			}
			bool allFf = (okCount == bytes.Length && ffCount == bytes.Length);
			if (okCount == bytes.Length && !allFf)
			{
				StringBuilder ver = new StringBuilder();
				for (int i = 0; i < bytes.Length; i++)
				{
					if (i > 0)
					{
						ver.Append('.');
					}
					ver.Append(bytes[i].ToString("X2"));
				}
				sb.AppendLine("结论 = ✅ **EC 版本可读** ⇒ EC 版本 = " + ver);
				if (ffCount > 0)
				{
					sb.AppendLine("   ⚠️ **但其中有 " + ffCount + " 个字节是 0xFF** ⇒ 疑似填充值混入，请以**原始字节**为准、⛔ 别直接当版本号用 ⚠️");
				}
				sb.AppendLine("   ⚠️ 解析口径：按 4 字节【十六进制】逐字节拼（如 00.28.00.00）—— ⛔ 不做 BCD/十进制猜测 ✅");
			}
			else if (allFf)
			{
				sb.AppendLine("结论 = ⚠️ **读到了、但四个字节全是 0xFF ⇒ 疑似「未映射填充值」，⛔ 不能当成 EC 版本** ⚠️");
				sb.AppendLine("   ⇒ ⭐ 关键结论：**IOCTL 返回成功并不代表地址可达** —— 本通道对窗口外的地址");
				sb.AppendLine("      不报错、而是回 0xFF ✅（与既有 0x0770 / 0x087F 的 0xFF 是同一个签名 ⚠️）");
				sb.AppendLine("   ⇒ ⚠️ 两种可能、**本通道分不开**：");
				sb.AppendLine("      ① 该地址在本机**未映射**（⚠️ 本机是同方翼龙15Pro，EC 未必是蛟龙的 IT5570）；");
				sb.AppendLine("      ② 该地址真实值就是 0xFF（⚠️ 四个字节全 FF 不像版本号）。");
				sb.AppendLine("   ⇒ ⭐ 因此本通道**无法回答「EC 版本是多少」**；要拿到真值需要：");
				sb.AppendLine("      ① ⭐ 在**蛟龙（宝龙达）真机**上跑本节（那才是该地址的目标机型 ✅）；");
				sb.AppendLine("      ② ⭐ 或改用**端口 I/O** 通道（0x62/0x66 · 需 PawnIO 自建模块）⚠️");
				sb.AppendLine("         ⛔ 不要为此引入 WinRing0 / inpoutx64（合规不允许）⚠️");
			}
			else if (okCount > 0)
			{
				sb.AppendLine("结论 = ⚠️ **部分可读（" + okCount + "/4 字节）** ⇒ 版本号无法可靠解析，请以上面的原始字节为准 ⚠️");
			}
			else if (controlOk)
			{
				sb.AppendLine("结论 = ⛔ **地址不可达** —— 对照读成功而 0x8410 全失败 ⚠️");
				sb.AppendLine("   ⇒ ⭐ 说明现有 `\\.\\ACPIDriver`（H2RAM 0x700–0x7FF）通道**到不了 XRAM 0x8410** ✅");
				sb.AppendLine("   ⇒ ⭐ 要读它需要【另一条通路】：EC 端口 I/O（0x62/0x66 · 需 PawnIO 自建模块）");
				sb.AppendLine("      ⛔ 不是我们现在这条 —— 也 ⛔ 不要为此引入 WinRing0/inpoutx64（合规不允许）⚠️");
			}
			else
			{
				sb.AppendLine("结论 = ⛔ **通道不可用** —— `\\.\\ACPIDriver` 打不开、或 IOCTL 读全线失败 ⚠️");
				sb.AppendLine("   ⇒ ⭐ 这与地址无关：先解决驱动（`UWACPIDriver.sys` 未装/未加载，或权限不足）✅");
				sb.AppendLine("   ⇒ ⚠️ 蛟龙（宝龙达）真机**预期就是这一条**（`CreateFile` 返回 `Win32 error 2`）✅");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("EC 版本试读异常 = " + ex.Message);
		}

		// ④ `AcpiTest_MULong`（⭐ 用户架构表里「Legacy Tongfang Mulong」那条免驱 WMI）—— ⭐ 如实给探索结论 ⛔ 不假装能用 ✅
		//    ⚠️ 本机（同方翼龙15Pro）2026-10-16 **实跑**：**类存在、实例数 = 10** ⚠️
		//       ⛔ 而 `Get-CimInstance` 口径查不到任何实例 ⇒ ⭐ **那是假否定、不能用那个口径** ⚠️
		//       （本段因此改用 `ManagementObjectSearcher` ✅ —— ⭐ 与 PNP0C14 那节同款 ✅）
		//    ⇒ ⭐ 但**仍不可用**：⭐ 它的方法签名（GetULong / SetULong / FireULong / GetSetULong / GetButton）
		//      **没有「地址 ＋ 值」的结构** ⇒ ⭐ 那是 ACPI **通用测试钩子**、⛔ 不是 EC 读写接口 ✅
		//      `FireULong(Hack)` 更是一个**有副作用**的钩子 ⇒ ⛔ 绝不能调用 ⚠️。
		//    ⚠️ 本探测**只枚举实例数**（元数据）· ⛔ 不调用任何方法 ✅
		sb.AppendLine();
		sb.AppendLine("── ②c `AcpiTest_MULong`（⭐ 架构表里「Legacy Tongfang Mulong」那条免驱 WMI）──");
		sb.AppendLine("⭐ 待答问题：⭐ 它能不能用来读写 EC 寄存器（⭐ 若能 ⇒ ⭐ 是一条比端口 I/O 干净的第三条路 ✅）");
		try
		{
			int instances = 0;
			using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM AcpiTest_MULong"))
			{
				using ManagementObjectCollection rows = searcher.Get();
				foreach (ManagementBaseObject row in rows)
				{
					instances++;
				}
			}
			sb.AppendLine("实例数 = " + instances);
			if (instances == 0)
			{
				sb.AppendLine("结论 = ⛔ **不可用**：类定义在、但**实例数为 0** ⚠️");
				sb.AppendLine("   ⇒ ⭐ WMI 方法调用**必须有实例** ⇒ ⭐ 无法调用它 ⇒ ⭐ **不构成可用的 EC 通路** ✅");
			}
			else
			{
				sb.AppendLine("结论 = ⚠️ **有实例**，但**仍不建议**用它读 EC ⚠️：");
				sb.AppendLine("   ⇒ 它的方法签名（GetULong/SetULong/FireULong/GetSetULong/GetButton）**没有「地址＋值」结构** ⇒");
				sb.AppendLine("     ⭐ 那是 ACPI **通用测试钩子**、⛔ 不是 EC 读写接口 ✅");
				sb.AppendLine("   ⇒ ⚠️ 其中 `FireULong(Hack)` 是**有副作用**的钩子 ⇒ ⛔ 绝不能调用 ⚠️");
			}
			sb.AppendLine("⭐ 如实说明：⭐ 本项**只枚举实例数**（元数据），⛔ **没有调用任何方法** ✅（⭐ 避免副作用）");
		}
		catch (Exception ex)
		{
			sb.AppendLine("AcpiTest_MULong 实例查询失败 = " + ex.Message);
			sb.AppendLine("   （⭐ 类不存在也会走到这里 ⇒ ⭐ 那同样说明本机没有这条通路 ✅）");
		}
	}

	// ── ②d EC 端口 I/O（PawnIO LpcACPIEC 模块）只读试读 ──
	// 2026-10-16（用户批准：「先把诊断功能加入EC版本的读取」）：
	//   ⭐ ②b 已**实测证明**既有 `\\.\ACPIDriver`（H2RAM 0x700–0x7FF）**到不了 XRAM** ⚠️
	//     （对照读 0x07C3 得真实值 0x07，而 0x8410~0x8413 四字节全回 0xFF = 「未映射填充值」签名）
	//   ⇒ 本节走**第二条通路**：PawnIO `LpcACPIEC` 模块提供的 **ACPI EC 端口 I/O**
	//     （端口 0x62 数据 / 0x66 状态·命令；命令 0x92 读 / 0x93 写）——
	//     ⭐ 这正是开源限充项目与厂商控制台碰 XRAM 用的那类通道 ✅。
	//   ⚠️ 合规：PawnIO **模块**是 LGPL-2.1-or-later（**可分发**：已内嵌 + NOTICE + LICENSES\LGPL-2.1.txt）；
	//     ⛔ 既不引入 WinRing0（签名 2007–2014 已过期 ＋ 在微软易受攻击驱动列表里）
	//     ⛔ 也不引入 inpoutx64（许可只自由用于个人/非商业）✅。
	//   ⛔ **全程只读**：只发 0x92 读命令 · ⛔ 绝不发 0x93 写 · ⛔ 不改任何寄存器 ✅（限充仍【暂缓】）。
	//   ⭐ 与 ②b 的**关键差别**：端口通道**会超时**（等 IBF/OBF 超过 250ms 即失败），
	//     ⇒ ⭐ 它能**明确区分**「地址不可达」与「通道根本不通」；
	//       而 ②b 那条 H2RAM 通道对窗口外地址**静默回填充值**、两种失败长得一模一样 ⚠️。
	//
	// ⭐⭐ 2026-10-16 修正（⭐ **真机实测暴露的两个设计缺陷** · 诊断包 …-233138）：
	//   实测原文：0x8410 ✅ 0xAE · 0x8411 ✅ 0x13 · 0x8412 ✅ 0x00 · 0x8413 ⛔（等 OBF 超时）
	//             ⇒ ⭐ 通道**立刻全局中毒** ⇒ ⭐ SOC / 模式两条交叉校验**全部丢失** ⚠️
	//   ⚠️ 注意：前三个字节**值各不相同、且非全 0xFF** ⇒ ⭐ 端口通道机制**确实在工作** ✅
	//      （对照 ②b：那条 H2RAM 通读回来四个全 0xFF = 未映射填充值 ⚠️）
	//
	//   缺陷①（**读取顺序**）：交叉校验（SOC / 模式）才是**最有价值**的判据 ——
	//     ⭐ 版本号可能是任意数据，而 **SOC 必须 0~100、模式必须 0/2/0x48**；
	//     这两个对不上就说明**地址映射根本没对**，版本号再「合理」也不能当真 ⚠️
	//     ⇒ ⭐ **改为先读交叉校验、再读版本号** ✅（这样即使后面某个字节超时，最关键证据也已到手 ✅）
	//   缺陷②（**中毒条件**）：⭐ **读是安全操作**，不该因一个地址超时就让整节失效 ⚠️
	//     ⇒ ⭐ 读软失败**只标记该地址失败、继续读后面的**；连续读失败达
	//       <see cref="EcPortIo.MaxConsecutiveReadFailures"/> 次才升级为全局中毒 ✅
	//     ⇒ ⚠️ 写路径 / 白名单违规 / 异常 ⇒ **仍立即中毒**（保守，护栏不动）✅
	private static void AppendEcPortIoProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ②d EC 端口 I/O（PawnIO LpcACPIEC 模块 · 只读）──");
		sb.AppendLine("⭐ 与 ②b 的关系：②b 已实测证明既有 `\\.\\ACPIDriver`（H2RAM 0x700–0x7FF）**到不了 XRAM** ⚠️");
		sb.AppendLine("   ⇒ 本节走**第二条通路**：PawnIO `LpcACPIEC` 模块的 **ACPI EC 端口 I/O**");
		sb.AppendLine("     （0x62 数据 · 0x66 状态/命令 · 命令 0x92 读）—— ⭐ 开源限充项目与厂商控制台用的就是这类通道 ✅");
		sb.AppendLine("⚠️ 合规：PawnIO 模块 LGPL-2.1-or-later（**可分发** · 已内嵌 ＋ NOTICE ＋ LICENSES\\LGPL-2.1.txt）");
		sb.AppendLine("   ⛔ 不引入 WinRing0（签名已过期 ＋ 微软易受攻击驱动列表）· ⛔ 不引入 inpoutx64（非商业许可）✅");
		sb.AppendLine("   ⭐ 模块本身**只放行端口 0x62/0x66**，超出即拒绝 ✅");
		sb.AppendLine("⛔ 全程只读：只发 0x92 读命令 · ⛔ 绝不发 0x93 写 · ⛔ 不改任何寄存器 ✅");
		sb.AppendLine();
		sb.AppendLine("⭐⭐ **读取顺序（2026-10-16 修正）**：⭐ **先读交叉校验（SOC / 模式）、再读版本号** ⚠️");
		sb.AppendLine("   理由：⭐ 版本号可能是任意数据，而 **SOC 必须落在 0~100、模式必须是 0/2/0x48** ——");
		sb.AppendLine("   这两个才是「**是不是真的在跟一颗 IT5570 对话**」的判据 ⇒ ⭐ 最有价值、必须最先拿到 ✅");
		sb.AppendLine("   ⚠️ 原顺序把版本号放前面：实测第 4 个版本字节超时后，两条交叉校验**全部丢失** —— 已修正 ✅");
		sb.AppendLine("⭐⭐ **中毒条件（2026-10-16 修正）**：⭐ 读的软失败**只标记该地址、继续读后面的** ✅");
		sb.AppendLine("   （⭐ 连续读失败达 " + EcPortIo.MaxConsecutiveReadFailures
			+ " 次才升级为全局中毒 ⇒ ⭐ 未达上限说明只是**个别地址**不响应，⛔ 不是通道坏了 ✅）");
		sb.AppendLine("   ⚠️ 写路径 / 白名单违规 / 异常 ⇒ **仍立即中毒**（保守，护栏不动）✅");
		sb.AppendLine("⭐⭐ **第 4 字节「间歇超时」的排查结论（2026-10-16 A/B 实测）**：");
		sb.AppendLine("   ⚠️ 曾怀疑「背靠背连发导致命令被丢弃」（ACPI 规范确有事务间隔要求）⇒ ⭐ 于是做了 A/B：");
		sb.AppendLine("      · 加 5ms 间隔：4 字节全读到 ✅（1 次）");
		sb.AppendLine("      · 不加间隔：4 字节全读到 ✅（2 次）· 第 4 字节超时 ⛔（1 次）");
		sb.AppendLine("   ⇒ ⭐ **加不加都会间歇性超时** ⇒ ⭐ 延迟**不是**成因、也**不是**有效缓解");
		sb.AppendLine("      ⇒ ⛔ **未采用**该缓解（当前事务间隔 = " + EcPortIo.InterTransactionDelayMs + "ms ⇒ 即**不做额外等待**）");
		sb.AppendLine("   ⭐ 决定性旁证：**超时那一刻 `0x66` 的状态字节 = `0x00`（OBF=0 且 IBF=0）**");
		sb.AppendLine("      ⇒ ⭐ **EC 已取走我们写入的字节（IBF 已清），只是没给出数据字节（OBF 从未置位）**");
		sb.AppendLine("      ⇒ ⭐ 这是「**EC 未在 " + EcPortIo.TimeoutMs + "ms 内应答**」的签名，⛔ 不是「命令被丢弃」的签名 ⚠️");
		sb.AppendLine("   ⇒ ⭐ 故**不改**超时逻辑；⭐ 真正的保障是上面的**分级中毒**：");
		sb.AppendLine("      即使某地址超时，⭐ 交叉校验也已先拿到、且**不会作废整节** ✅");
		sb.AppendLine("   ⚠️ 若在蛟龙真机上仍复现 ⇒ ⭐ 候选方向是**放长超时**（" + EcPortIo.TimeoutMs + "→500ms）或该地址未实现；");
		sb.AppendLine("      ⚠️ 两者都要**真机再加证据**，⛔ 不在这里猜 ⚠️");
		sb.AppendLine();
		try
		{
			// ⭐ 局部函数：把「读到 / 读不到」写成一行，供下面两处复用 ✅
			static string DescribeRead(bool ok, int value)
			{
				return ok ? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(value)) : "⛔ 读不到";
			}

			sb.AppendLine("PawnIO 驱动 = " + EcPortIo.DescribePawnInstalled());
			sb.AppendLine();

			// ── ① 交叉校验读（**最先**，见上面的顺序说明）──
			sb.AppendLine("① **交叉校验**（⭐ 先读：它才是「地址映射对不对」的判据 ✅）");
			bool socOk = EcPortIo.TryReadByte(0x847C, out int soc);
			sb.AppendLine("   0x847C（SOC，合理区间 0~100）= " + DescribeRead(socOk, soc)
				+ (socOk
					? ((soc >= 0 && soc <= 100) ? " ⇒ ✅ **在合理区间**" : " ⇒ ⚠️ **超出合理区间**（地址映射可疑）")
					: ("　（原因：" + EcPortIo.LastReadFailure + "）")));
			bool modeOk = EcPortIo.TryReadByte(0x83C0, out int mode);
			sb.AppendLine("   0x83C0（模式，合理值 0/2/0x48）= " + DescribeRead(modeOk, mode)
				+ (modeOk
					? ((mode == 0 || mode == 2 || mode == 0x48) ? " ⇒ ✅ **命中合理值**" : " ⇒ ⚠️ **未命中合理值**（地址映射可疑）")
					: ("　（原因：" + EcPortIo.LastReadFailure + "）")));
			bool mappingConfirmed = socOk && soc >= 0 && soc <= 100
				&& modeOk && (mode == 0 || mode == 2 || mode == 0x48);
			sb.AppendLine("   ⇒ **地址映射判据** = " + (mappingConfirmed
				? "✅ **已确认**（SOC 与模式双双落在合理值内 ⇒ ⭐ 确实在与 EC 对话 ✅）"
				: "⚠️ **未确认** ⇒ ⭐ 下面即使读到「看似合理」的版本号，也**不能当真** ⚠️"));
			sb.AppendLine();

			// ── ② 再读 EC 版本 4 字节（XRAM 0x8410~0x8413）──
			sb.AppendLine("② **EC 版本**（XRAM 0x8410~0x8413）");
			int[] version = new int[4];
			int okCount = 0;
			int ffCount = 0;
			for (int i = 0; i < version.Length; i++)
			{
				int addr = EcChargeLimit.XramEcVersionRegister + i;
				bool ok = EcPortIo.TryReadByte(addr, out int v);
				version[i] = (ok ? v : (-1));
				if (ok)
				{
					okCount++;
					if (v == 0xFF)
					{
						ffCount++;
					}
				}
				sb.AppendLine("   0x" + addr.ToString("X4") + " = " + DescribeRead(ok, v)
					+ (ok ? "" : ("　（原因：" + EcPortIo.LastReadFailure + "）")));
			}
			sb.AppendLine();

			sb.AppendLine("通道 = " + EcPortIo.DescribeChannel());
			int lastStatus = EcPortIo.LastStatusSeen;
			if (lastStatus >= 0)
			{
				sb.AppendLine("   最近一次 0x66 状态字节 = " + ChargeLimitEcProbe.FormatRaw(lastStatus)
					+ "（bit0=OBF「EC 已给出可读字节」· bit1=IBF「EC 未取走我们写入的字节」）");
				sb.AppendLine("   ⭐ 用途：把「EC 根本不响应」与「EC 卡住不取走」分开 ✅");
			}
			sb.AppendLine();

			// ── ③ 结论 —— ⭐ **先看地址映射是否确认、再看版本号** ⚠️
			//    判据是「**值本身合理**」，⛔ 不是「调用没报错」（⭐ 这正是 ②b 得到的教训 ✅）
			bool allFf = okCount == 4 && ffCount == 4;
			if (!mappingConfirmed)
			{
				sb.AppendLine("结论 = ⚠️ **地址映射未确认** ⇒ ⭐ 本节的版本号**不足以判定 EC 版本** ⚠️");
				sb.AppendLine("   ⇒ 判据是**值本身合理**（SOC 0~100 · 模式 0/2/0x48），⛔ 不是「调用没报错」");
				sb.AppendLine("      —— ⭐ 这正是 ②b 那条「全 0xFF 填充值」教训的延伸 ✅");
				if (okCount > 0)
				{
					sb.AppendLine("   ⇒ ⚠️ 但版本字节读到了 " + okCount + "/4（值见上）⇒ ⭐ **端口通道机制是工作的** ✅；");
					sb.AppendLine("      只是**该机的 XRAM 布局与本节的假设不一致** ⚠️");
					sb.AppendLine("      （⭐ 本机是同方翼龙15Pro · EC 未必是蛟龙的 IT5570 ⇒ ⭐ 对本机而言这可能是**正常**结果 ✅）");
				}
				sb.AppendLine("   ⇒ ⭐ **请以蛟龙（宝龙达）真机的结果为准** ✅");
			}
			else if (okCount == 4 && !allFf)
			{
				StringBuilder ver = new StringBuilder();
				for (int i = 0; i < version.Length; i++)
				{
					if (i > 0)
					{
						ver.Append('.');
					}
					ver.Append(version[i].ToString("X2"));
				}
				sb.AppendLine("结论 = ✅ **地址映射已确认 ＋ EC 版本可读（端口通道）** ⇒ EC 版本 = " + ver);
				if (ffCount > 0)
				{
					sb.AppendLine("   ⚠️ 但其中有 " + ffCount + " 个字节是 0xFF ⇒ ⭐ 请以**原始字节**为准 ⚠️");
				}
				sb.AppendLine("   ⇒ ⭐ 这正是 ②b 那条 H2RAM 通道**读不到**的东西 ⇒ ⭐ **端口通道是有效通路** ✅");
				sb.AppendLine("   ⚠️ 版本号口径提醒：⭐ 以**真机读数**为准 —— 那个开源项目实测记 `00.21`、");
				sb.AppendLine("      README 却写 `00.28`、脚本支持列表里连 `00.21` 都没有 ⇒ ⛔ 别只认某一个版本 ⚠️");
			}
			else if (allFf)
			{
				sb.AppendLine("结论 = ⚠️ **地址映射已确认、但四个版本字节全是 0xFF** ⚠️");
				sb.AppendLine("   ⇒ ⭐ 说明**端口读写机制与地址映射都是工作的**（SOC/模式已落在合理值内）✅");
				sb.AppendLine("   ⇒ 但 0x8410~0x8413 在该机**读不到真实版本**（该固件未实现该处、或值本就是 FF）");
				sb.AppendLine("   ⇒ ⭐ 请以**蛟龙（宝龙达）真机**的结果为准 ✅");
			}
			else if (okCount > 0)
			{
				sb.AppendLine("结论 = ⚠️ **部分可读（" + okCount + "/4 字节）** ⇒ 版本号无法可靠解析，请以上面原始字节为准 ⚠️");
				sb.AppendLine("   ⭐ 未读到的字节见上面的「原因：」—— ⭐ 它**不影响**已读到的字节、也**不影响**交叉校验 ✅");
				sb.AppendLine("      （⭐ 这正是本次修正的目的：⛔ 不再让一个地址超时就作废整节 ⚠️）");
			}
			else
			{
				sb.AppendLine("结论 = ⛔ **端口通道不通** —— 4 个字节一个都没读到 ⚠️");
				sb.AppendLine("   ⇒ ⭐ 与 ②b 的失败**不同**：端口通道有超时与中毒机制，");
				sb.AppendLine("     ⭐ 具体卡在哪看上面「通道 =」那一行（驱动未装 / 模块未装载 / 已中毒）✅");
				sb.AppendLine("   ⇒ ⭐ 若「PawnIO 驱动 = ⛔ 未安装」⇒ ⭐ 需用户**显式点击**安装（见 Install-PawnIO.ps1）");
				sb.AppendLine("     ⛔ 我们**不分发** PawnIO 驱动本体（GPL-2.0），只分发 LGPL 的**模块** ✅");
			}
			sb.AppendLine("⭐ 如实说明：本节**只读**（只发 0x92）· ⛔ 未发 0x93 写 · ⛔ 未改任何寄存器 ✅");
		}
		catch (Exception ex)
		{
			sb.AppendLine("EC 端口 I/O 试读异常 = " + ex.Message);
		}
	}

	// ── ②e EC ACPI 标准空间（0x00~0xFF · 命令 0x80 · 单字节地址）只读试读 ──
	// 2026-10-16（键盘背光通道调研 · 用户要求「本轮只读」）：
	//   ⭐ 出发点：用户报「键盘灯开了无法关闭 亮度不能调节」，并指出「关灯就是和亮度 0 一样」⚠️
	//   ⭐ 调研结论：⭐ EC 的地址空间【不止一套】，而此前只实现了厂商 XRAM mailbox ⚠️
	//     · 0x92/0x93 → XRAM mailbox，用【双字节】地址（0x8150 限充 / 0x8410 EC版本 / 0x847C SOC）← ②d 那节
	//     · 0x80/0x81 → ACPI 标准空间，用【单字节】地址（0x9A 键盘背光 / 0x20 Fn锁 / 0xE4 散热模式）← 本节
	//   ⭐ 依据（斗战者 · 同为宝龙达代工，其读实现即标准 ACPI EC 协议）：
	//     `DriverBridge.ReadEc`：`0x66←0x80`（RD_EC）→ `0x62←(byte)reg` → 读 `0x62`（⭐ 单字节 ✅）
	//     其 EC 物理窗口 `EC_BASE=0xFE800400` / `EC_SIZE=0xFF`（= 256）⇒ 映射的正是这 256 字节标准空间 ✅
	//   ⇒ ⭐ 这就解释了**此前用 0x92 读键盘背光为何怎么都不对**：⭐ **两者地址空间不同** ✅
	//   ⛔ **全程只读**：只发 0x80 · ⛔ 绝不发 0x81（写）· ⛔ 不改任何寄存器 ✅
	private static void AppendEcStandardSpaceProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ②e EC **ACPI 标准空间**（0x00~0xFF · 命令 0x80 · 单字节）只读试读 ──");
		sb.AppendLine("⭐ 为什么要单独一节：EC 的地址空间【不止一套】⚠️");
		sb.AppendLine("   · 0x92/0x93 → 厂商 **XRAM mailbox**，用【双字节】地址（0x8150 / 0x8410 / 0x847C）← ②d 那节");
		sb.AppendLine("   · 0x80/0x81 → **ACPI 标准空间**，用【单字节】地址（0x9A 键盘背光 / 0x20 Fn锁 / 0xE4 散热）← 本节");
		sb.AppendLine("⭐⭐ 依据（斗战者 · 同为宝龙达代工）：其读实现就是标准 ACPI EC 协议");
		sb.AppendLine("   `0x66←0x80(RD_EC)` → `0x62←(byte)reg` → 读 `0x62`（⭐ 单字节地址 ✅）");
		sb.AppendLine("   其 EC 物理窗口 `EC_BASE=0xFE800400` / `EC_SIZE=0xFF`（=256）⇒ 映射的正是这 256 字节标准空间 ✅");
		sb.AppendLine("   ⇒ ⭐ 这解释了此前用 0x92 读键盘背光**为何怎么都不对**：⭐ **地址空间不同** ✅");
		sb.AppendLine("⛔ 全程只读：只发 0x80 · ⛔ 绝不发 0x81（写）· ⛔ 不改任何寄存器 ✅");
		sb.AppendLine();
		sb.AppendLine("⭐⭐ **对照实验（用来判定两套地址空间的关系）**：");
		sb.AppendLine("   本机 H2RAM 已实测 `0x740`(ProjectID)=**25**、`0x741`=**33**、`0x766`(键盘灯判据)=**0x97**、`0x767`=**0**");
		sb.AppendLine("   ⇒ ⭐ 若本节 `0x40`/`0x41`/`0x66`/`0x67` 读出**同样的值**，即说明");
		sb.AppendLine("      **H2RAM 窗口 ≡ 标准空间 ＋ 0x700** ✅；⛔ 若不同，则两套确为**互相独立**的地址空间 ⚠️");
		sb.AppendLine();
		try
		{
			sb.AppendLine("PawnIO 驱动 = " + EcPortIo.DescribePawnInstalled());
			sb.AppendLine();

			// ⭐ 探测表：前 9 个是斗战者的偏移语义（同为宝龙达代工）；后 4 个是上面那组对照 ✅
			(int Reg, string Label)[] probes =
			{
				(0x9A, "键盘背光等级（斗战者 OFF_KBNL · 合理值 0~3 · ⭐ 0 = 关灯）"),
				(0x20, "Fn 锁（斗战者 OFF_FNHK · bit3）"),
				(0xE4, "散热模式（斗战者 OFF_ITSM · 合理值 0~3）"),
				(0xE0, "GPU 温度（斗战者 OFF_GPUT）"),
				(0xE1, "CPU 温度（斗战者 OFF_CPUT）"),
				(0x96, "GPU 风扇转速 高字节（斗战者 OFF_F3HI）"),
				(0x97, "GPU 风扇转速 低字节（斗战者 OFF_F3LO）"),
				(0x9B, "CPU 风扇转速 高字节（斗战者 OFF_F1HI）"),
				(0x9C, "CPU 风扇转速 低字节（斗战者 OFF_F1LO）"),
				(0x40, "⭐对照：H2RAM 0x740 读到 25（ProjectID）"),
				(0x41, "⭐对照：H2RAM 0x741 读到 33"),
				(0x66, "⭐对照：H2RAM 0x766 读到 0x97（键盘灯判据）"),
				(0x67, "⭐对照：H2RAM 0x767 读到 0"),
			};

			int okCount = 0;
			foreach ((int reg, string label) in probes)
			{
				bool ok = EcPortIo.TryReadByteAcpi(reg, out int v);
				if (ok)
				{
					okCount++;
				}
				sb.AppendLine("   0x" + reg.ToString("X2") + " " + label + " = "
					+ (ok ? ("✅ " + ChargeLimitEcProbe.FormatRaw(v)) : "⛔ 读不到")
					+ (ok ? "" : ("　（原因：" + EcPortIo.LastReadFailure + "）")));
			}
			sb.AppendLine();
			sb.AppendLine("通道 = " + EcPortIo.DescribeChannel());
			sb.AppendLine();

			if (okCount == 0)
			{
				sb.AppendLine("结论 = ⛔ **标准空间一个字节都没读到** ⚠️");
				sb.AppendLine("   ⇒ ⭐ 具体卡在哪看上面「通道 =」那一行（驱动未装 / 模块未装载 / 已中毒）✅");
			}
			else
			{
				sb.AppendLine("结论 = ✅ 读到了 " + okCount + "/" + probes.Length
					+ " 个字节 ⇒ ⭐ **ACPI 标准空间通道（命令 0x80 · 单字节地址）可用** ✅");
				sb.AppendLine("   ⭐ 这**才是**键盘背光 `0x9A` 所在的那套地址空间 ⚠️（②d 那套 XRAM **够不到**它）");
				sb.AppendLine("   ⚠️ 但本机（同方翼龙15Pro）EC **未必**是蛟龙的 IT5570");
				sb.AppendLine("      ⇒ ⭐ `0x9A` 在**本机**是否等于「键盘背光等级」**尚不能断言** ⚠️");
				sb.AppendLine("      ⇒ ⭐ 请以**蛟龙（宝龙达）真机**读数为准；真机上还应做「改亮度前后各读一次」的对比 ✅");
			}
			sb.AppendLine("⭐ 如实说明：本节**只读**（只发 0x80）· ⛔ 未发 0x81 写 · ⛔ 未改任何寄存器 ✅");
		}
		catch (Exception ex)
		{
			sb.AppendLine("EC 标准空间试读异常 = " + ex.Message);
		}
	}

	// ── ③ 功能号 8~23 逐个试读 ──
	// ── ②f EC **LPC/SuperIO 寄存器窗口**（PawnIO LpcIO · slot 1 = 0x4E/0x4F）只读试读 ──
	//
	//   ⭐ 为什么再加一条：EC 的访问方式已确认【不止一套】，四条各自的**地址空间不同** ⚠️
	//     · ① 0x62/0x66 ＋ 命令 0x92/0x93 → 厂商 XRAM mailbox（0x8150 限充 / 0x8410 EC版本 / 0x847C SOC）← ②d
	//     · ② 0x62/0x66 ＋ 命令 0x80      → ACPI 标准空间（0x9A 键盘背光 / 0x20 Fn锁 / 0xE4 散热模式）← ②e
	//     · ③ 0x4E/0x4F SuperIO 索引/数据窗口 → 蛟龙控制台口径（0xC411 EC版本 / 0xC834~0xC83D 风扇）← 本节
	//     · ④ 0x2E/0x2F ＋ Write_EC_Init 解锁前置 → 厂商控制台 7.3
	//   ⭐ 关键疑点：两个上游**都自称在读「EC 版本」**，但一个说 0xC411、一个说 0x8410~0x8413 ⚠️
	//     这不可能是笔误 ⇒ ⭐ 若本节能读到合理的值，就印证「两套地址空间」的判断 ✅
	//   ⚠️ 诚实边界：本节的「只读」**包含对 SuperIO 窗口寄存器的写** ——
	//     读一个字节必须先向子寄存器 0x11/0x10 写「地址高/低字节」（那是**寻址**），再读子寄存器 0x12；
	//     ⛔ 本通道**没有任何写 EC 数据的路径**（不写 0x12、不发 0x93/0x81）✅
	//   ⚠️ 本机（同方翼龙15Pro）EC **不是 IT5570** ⇒ 读到的值**不能当蛟龙的真值**；
	//     本节能验证的是「**这条通道 ＋ 这套时序是否工作**」✅
	//   ⛔ 全程只读：不写 EC 数据 · 不改任何寄存器 ✅
	private static void AppendEcLpcIoProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ②f EC **LPC/SuperIO 寄存器窗口**（slot 1 = 0x4E/0x4F · 只读）──");
		sb.AppendLine("⭐ 与 ②d/②e 的关系：**第三条独立通道**，地址空间与它们都不同 ⚠️");
		sb.AppendLine("   端口对 0x4E/0x4F（SuperIO 索引/数据）＋ 子寄存器 0x11 地址高 · 0x10 地址低 · 0x12 数据");
		sb.AppendLine("   ⭐ 口径来源：JiaolongControl（蛟龙开源控制台，MIT）的 ECMemoryTable ——");
		sb.AppendLine("      EC_ADDR_PORT = 0x4E / EC_DATA_PORT = 0x4F ⇒ ⭐ 正是上游 LpcIO.p 所说的 slot 1 ✅");
		sb.AppendLine("   ⭐ 与厂商控制台 7.3 的关系：厂商走 **slot 0（0x2E/0x2F）**、**同一套子寄存器语义** ✅");
		sb.AppendLine("     ⇒ ⭐ 两条只是**端口基址不同**，协议同一套（已逐字对照确认）✅");
		sb.AppendLine("⚠️ 合规：PawnIO `LpcIO` 模块 LGPL-2.1-or-later（**可分发** · 已内嵌 ＋ NOTICE ＋ LICENSES\\LGPL-2.1.txt）");
		sb.AppendLine("   ⛔ 不引入 JiaoLongDriver64（蛟龙控制台自带的内核驱动）· ⛔ 不引入 WinRing0 / inpoutx64 ✅");
		sb.AppendLine("⛔ 全程只读：⛔ 不写 EC 数据（不写子寄存器 0x12）· ⛔ 不发 0x93/0x81 · ⛔ 不改任何寄存器 ✅");
		sb.AppendLine("⚠️ 诚实边界：本节为**寻址**而写子寄存器 0x11/0x10（窗口寄存器，**不是** EC 数据）✅");
		sb.AppendLine();
		try
		{
			sb.AppendLine("PawnIO 驱动 = " + EcLpcIo.DescribePawnInstalled());
			sb.AppendLine();
			sb.AppendLine("⓪ **解锁前置探测（只读）** —— ⭐ 上游在访问 XRAM 前**必须先解锁** ⚠️");
			sb.AppendLine("   依据（两处独立一致 ✅）：");
			sb.AppendLine("     · JiaolongControl `EC_init()`：读 0x2000 芯片 ID；若 == 0x55 ⇒ 再读 0x1060、置 bit7、**写回**");
			sb.AppendLine("     · 厂商控制台 7.3 `Write_EC_Init()`：读 0x2000/0x2001；若 == 0x55 ⇒ 0x1060 置 bit7");
			sb.AppendLine("   ⛔ 本节**只读**：⛔ **不执行解锁写**（0x1060 的写是写操作，须先拍板）✅");
			bool id1Ok = EcLpcIo.TryReadByte(0x2000, out int chipId1);
			sb.AppendLine("   0x2000（芯片 ID）= " + (id1Ok
				? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(chipId1) + (chipId1 == 0x55
					? " ⇒ ⭐ **命中 0x55** ⇒ 该 EC 需要解锁才能读 XRAM ⚠️"
					: " ⇒ ⚠️ 非 0x55 ⇒ 本机 EC 不是上游那一款（或未进入配置态）"))
				: ("⛔ 读不到（原因：" + EcLpcIo.LastReadFailure + "）")));
			bool id2Ok = EcLpcIo.TryReadByte(0x2001, out int chipId2);
			sb.AppendLine("   0x2001（芯片 ID 2）= " + (id2Ok ? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(chipId2)) : "⛔ 读不到"));
			bool lockOk = EcLpcIo.TryReadByte(0x1060, out int lockReg);
			sb.AppendLine("   0x1060（解锁寄存器现值）= " + (lockOk
				? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(lockReg) + (((lockReg & 0x80) != 0)
					? " ⇒ ⚠️ bit7 **字面**置位 —— ⚠️ 但 0xFF 与「未映射填充值」**无法区分** ⇒ ⭐ 只有 0x2000 == 0x55 时该结论才可信"
					: " ⇒ ⚠️ bit7 **未置位**（未解锁）"))
				: "⛔ 读不到"));
			sb.AppendLine("   ⇒ ⭐ **若 0x2000 == 0x55 且 bit7 未置位 ⇒ 下面读到的 0xFF 可能是「未解锁」所致** ⚠️");
			sb.AppendLine("      ⇒ ⚠️ 那就**不能**据 0xFF 断定该地址不存在 ⚠️ —— 解锁需写 0x1060，⛔ 本轮不做（只读）✅");
			sb.AppendLine();			sb.AppendLine("① **EC 版本（蛟龙控制台口径 · 单字节）** 0xC411");
			bool verOk = EcLpcIo.TryReadByte(0xC411, out int ver);
			sb.AppendLine("   0xC411 = " + (verOk
				? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(ver))
				: "⛔ 读不到"));
			if (!verOk)
			{
				sb.AppendLine("      （原因：" + EcLpcIo.LastReadFailure + "）");
			}
			sb.AppendLine("   ⚠️ 判据提醒：**读到 ≠ 合理**（②b 的教训：IOCTL 成功也可能回填充值）");
			sb.AppendLine("      ⇒ ⭐ 本机 EC 不是 IT5570 ⇒ 本行的值**不能当蛟龙的真值**，只能证明「通道 ＋ 时序」是否工作 ⚠️");
			sb.AppendLine();
			sb.AppendLine("② **风扇转速（蛟龙控制台口径 · 单字节）** 0xC834 / 0xC835");
			bool fan1Ok = EcLpcIo.TryReadByte(0xC834, out int fan1);
			sb.AppendLine("   0xC834（上游 Fan1_RPM）= " + (fan1Ok
				? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(fan1))
				: "⛔ 读不到"));
			bool fan2Ok = EcLpcIo.TryReadByte(0xC835, out int fan2);
			sb.AppendLine("   0xC835（上游 Fan2_RPM）= " + (fan2Ok
				? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(fan2))
				: "⛔ 读不到"));
			sb.AppendLine("   ⚠️ 语义未定：上游把这两个标为 RPM，但**单字节**装不下真实转速（几千 rpm）");
			sb.AppendLine("      ⇒ ⭐ 可能是「等级 / RPM÷100 / 高字节」之一 —— ⛔ 未真机确认前**不下结论**，只报原始字节 ⚠️");
			sb.AppendLine();
			sb.AppendLine("③ **与 ②d（XRAM 口径）的并列对照** —— ⭐ 本节的意义就在这一行 ⚠️");
			sb.AppendLine("   · ②d 的 EC 版本取 0x8410~0x8413（**四字节**，限充项目口径）");
			sb.AppendLine("   · 本节的 EC 版本取 0xC411（**单字节**，蛟龙控制台口径）");
			sb.AppendLine("   ⭐ 若两者**都能读到合理值** ⇒ ⭐ 说明它们确属**两套地址空间**（同一台 EC 上并存）✅");
			sb.AppendLine("   ⚠️ 若本节读到的是 0xFF / 0x00 这类**恒定填充值** ⇒ ⭐ 说明该地址在本机根本不存在");
			sb.AppendLine("      ⇒ ⚠️ 那就**不能**据此否定蛟龙上的存在性（⭐ 本机 EC 型号不同）⚠️");
			sb.AppendLine();
			sb.AppendLine("④ ⭐ **slot 0（0x2E/0x2F）vs slot 1（0x4E/0x4F）并列对照** —— ⭐ 本节的**判定核心** ⚠️");
			sb.AppendLine("   ⭐ 为什么要对照：两个槽位是**同一套 SuperIO 协议**的两个端口基址、指向**同一片 XRAM**");
			sb.AppendLine("      （上游 LpcIO.p：\"Slot 0 is at 0x2e/0x2f, slot 1 at 0x4e/0x4f\"）");
			sb.AppendLine("      ⇒ ⚠️ 单读一个槽位时，「**槽位不对**」与「**本机读不到**」长得**一模一样** ⚠️");
			sb.AppendLine("         · 若 **slot 0 能读到 0x55（芯片 ID）** ⇒ ⭐ 证明**通道与协议都对、只是槽位因模具而异** ✅");
			sb.AppendLine("         · 若 **两个槽位都读不到 / 全 0xFF** ⇒ ⭐ 只能说**本机读不到** ⚠️");
			sb.AppendLine("           ⛔ 不得据此说「不存在」—— 本探针**分不开**这三种可能：");
			sb.AppendLine("              ① 本机没有该 SuperIO 设备 · ② 未进入 SuperIO 配置态 · ③ 该地址本就不存在 ⚠️");
			sb.AppendLine("   ⚠️ 已知：厂商控制台 7.3 走 **slot 0**、蛟龙开源控制台走 **slot 1**；");
			sb.AppendLine("      而两者都自称在读同一个 0x2000 / 0x1060 / 0xC411 ⇒ ⭐ 更说明是**同一片空间的两个入口** ✅");
			sb.AppendLine();
			sb.AppendLine("   地址      含义                          slot 0 (0x2E/0x2F)   slot 1 (0x4E/0x4F)");
			sb.AppendLine("   ────────  ────────────────────────────  ───────────────────  ───────────────────");
			AppendSlotCompareRow(sb, 8192, "芯片 ID 1（期望 0x55）");
			AppendSlotCompareRow(sb, 8193, "芯片 ID 2");
			AppendSlotCompareRow(sb, 4192, "解锁寄存器（bit7=已解锁）");
			AppendSlotCompareRow(sb, 50193, "EC 版本（控制台口径）");
			AppendSlotCompareRow(sb, 51252, "风扇 1（上游 Fan1_RPM）");
			AppendSlotCompareRow(sb, 51253, "风扇 2（上游 Fan2_RPM）");
			sb.AppendLine();
			sb.AppendLine("   ⚠️ 判据提醒：**读到 ≠ 合理**（②b 的教训：IOCTL 成功也可能回填充值）");
			sb.AppendLine("      ⇒ ⭐ 本表**只报原始字节**，不下语义结论 ⚠️");
			sb.AppendLine("   ⚠️ 且本机 EC **不是 IT5570** ⇒ 本机读数**不能当蛟龙的真值**，只能验「通道 ＋ 时序 ＋ 槽位」✅");
			sb.AppendLine();
			sb.AppendLine("⑤ **通道状态** = " + EcLpcIo.DescribeChannel());
			sb.AppendLine("⛔ 本节只读：只读子寄存器 0x12 · 只写窗口寄存器 0x11/0x10（寻址）· ⛔ 未写任何 EC 数据 ✅");
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ ②f 诊断异常（已吞掉，不影响本节其余部分）：" + ex.GetType().Name + " · " + ex.Message);
		}
	}

	/// <summary>
	/// ②f 辅助：同一地址在 slot 0 / slot 1 各读一次，输出一行对照。
	///
	/// <para>⚠️ <see cref="EcLpcIo.LastReadFailure"/> 是**全局**的 ⇒ 若要取原因文本必须**读完立刻取**，
	/// 否则会被下一次读覆盖。本方法只报成功/失败与字节值，故不需要它。</para>
	///
	/// <para>⛔ 只读：不写任何 EC 数据（只向窗口寄存器写地址高/低字节，那是**寻址**）。</para>
	/// </summary>
	private static void AppendSlotCompareRow(StringBuilder sb, int address, string what)
	{
		string cell0 = ReadOneSlotForCompare(EcLpcIo.SlotZero, address);
		string cell1 = ReadOneSlotForCompare(EcLpcIo.SlotOne, address);
		sb.AppendLine("   0x" + address.ToString("X4") + "    " + PadTo(what, 28) + "  "
			+ PadTo(cell0, 19) + "  " + cell1);
	}

	/// <summary>②f 辅助：在指定槽位读一个字节 ⇒ 可直接打印的单元格文本（⛔ 只读）。</summary>
	private static string ReadOneSlotForCompare(ulong slot, int address)
	{
		try
		{
			if (EcLpcIo.TryReadByteOnSlot(slot, address, out int v))
			{
				return "✅ 0x" + v.ToString("X2");
			}
			return "⛔ 读不到";
		}
		catch (Exception ex)
		{
			return "⛔ 异常(" + ex.GetType().Name + ")";
		}
	}

	/// <summary>
	/// ②f 辅助：按**显示宽度**补齐（全角与符号占 2 列，否则对照表会歪）。
	/// ⚠️ 只影响诊断文本的对齐，**不影响任何判据**。
	/// </summary>
	private static string PadTo(string text, int width)
	{
		string s = ((text == null) ? string.Empty : text);
		int num = 0;
		foreach (char c in s)
		{
			bool flag = (c >= '\u1100' && c <= '\u115f') || (c >= '\u2600' && c <= '\u27bf')
				|| (c >= '\u2b00' && c <= '\u2bff') || (c >= '\u2e80' && c <= '\ua4cf')
				|| (c >= '\uac00' && c <= '\ud7a3') || (c >= '\uf900' && c <= '\ufaff')
				|| (c >= '\ufe30' && c <= '\ufe6f') || (c >= '\uff00' && c <= '\uff60')
				|| (c >= '\uffe0' && c <= '\uffe6');
			num += (flag ? 2 : 1);
		}
		return ((num >= width) ? s : (s + new string(' ', width - num)));
	}

	private static void AppendFunctionProbe(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ③ 功能号 8~23 逐个试读（⭐ 只发 Get=250 ✅ · ⭐ 带 `OutData[3]` 回显校验 ✅）──");
		BaolongdaWmi wmi = ResolveWmi();
		if (wmi == null)
		{
			sb.AppendLine("⚠️ 当前**没有可用的宝龙达接口**（⭐ 本机不是宝龙达，⭐ 或后端没接上 ✅）");
			sb.AppendLine("   ⇒ ⭐ 本节无数据 ✅（⭐ 这是**正常**结果，⛔ 不是错误 ✅）");
			return;
		}
		sb.AppendLine("接口实例 = " + wmi.InstancePath);
		sb.AppendLine("风扇数 = " + wmi.FanCount + " · 模式档数 = " + wmi.ModeCount);
		// ⭐⭐ 2026-10-17（30 系宝龙达致命 bug 之后补）：⭐ 把**探测依据**与**CPU 温度能力**如实打出来 ✅
		//   ⚠️ 为什么重要：⭐ 机主那台 30 系的症状是"整机退回 legacy"，
		//   ⭐ 而根因是「功能号 22 读不到」被当成了**整机是否接管**的判据 ⚠️
		//   ⇒ ⭐ 以后拿到诊断包，⭐ 一眼就能看出「白名单命中 ⇒ 接管」还是「活体证据不成立 ⇒ 不接管」✅
		sb.AppendLine("探测活体证据 = " + (string.IsNullOrEmpty(wmi.LivenessEvidence)
			? "⚠️ 无（⭐ 板号白名单命中时仍会接管 ✅；⭐ 板号未命中则不接管 ⛔）"
			: wmi.LivenessEvidence));
		sb.AppendLine("CPU 温度（功能号 22）= " + (wmi.CpuTempAvailable
			? "✅ 可读（" + wmi.CpuTempCelsius + "℃）"
			: "⚠️ **不可用**（⭐ 30 系宝龙达的已知现象；⭐ 温度改由 LM 自带 LHM 兜底，⛔ 不会显示成 0℃）"));
		sb.AppendLine();
		AppendOne(sb, wmi, BaolongdaWmi.Function.SystemPerMode, "8", "系统性能模式");
		AppendGpuMode(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardStatus, "10", "键盘灯状态");
		AppendOne(sb, wmi, BaolongdaWmi.Function.FnLock, "11", "Fn 锁");
		AppendOne(sb, wmi, BaolongdaWmi.Function.TPLock, "12", "触摸板锁");
		AppendFans(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.AmbientLight, "15", "LOGO 灯");
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardMode, "16", "键盘灯模式");
		AppendColor(sb, wmi);
		AppendOne(sb, wmi, BaolongdaWmi.Function.RgbKeyboardBrightness, "18", "键盘灯亮度");
		AppendOne(sb, wmi, BaolongdaWmi.Function.SystemAcType, "19", "交流类型");
		AppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeedSwitch, "20", "风扇手动开关");
		// ⭐⭐ 2026-10-17（接手）：⭐ 功能号 21 的**读语义已由原厂包确认** ——
		//   ⛔ **不是"最大转速"**，⭐ 而是**手动转速设定值**：原厂 `MaxFanSpeed.Text = value`、
		//   再由它反算滑块位置（`Set_MaxFanSpeedSlider`）⇒ 读值 = 上次写下去的 `x100 RPM` ✅。
		//   ⇒ ⭐ 我们**不再**拿它当百分比分母（真机读到 50 ⇒ 曾被当成 5000 RPM，导致 40% vs 官方 33% ❌）。
		//   下面仍把原始字节打出来，⭐ 供核对"手动设定值到底是多少" ✅。
		AppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeed, "21", "风扇手动转速设定值（⚠️ 读=上次设的转速，不是最大转速）");
		try
		{
			if (wmi.TryReadBytesChecked(BaolongdaWmi.Function.MaxFanSpeed, 4, out byte[] mfsRaw)
				&& mfsRaw != null && mfsRaw.Length >= 4)
			{
				sb.AppendLine($"     · 功能号 21 原始 4 字节 = {mfsRaw[0]},{mfsRaw[1]},{mfsRaw[2]},{mfsRaw[3]}"
					+ $" ⇒ 单字节={mfsRaw[0]}（= 手动设定 {mfsRaw[0] * 100} RPM）"
					+ $" · 小端16位={(mfsRaw[1] << 8) + mfsRaw[0]} · 大端16位={(mfsRaw[0] << 8) + mfsRaw[1]}"
					+ " · ⭐ 原厂口径：⛔ 不是最大转速 ⇒ 已经不拿它当百分比分母 ✅");
			}
			else
			{
				sb.AppendLine("     · 功能号 21 只读到 1 字节或读不到"
					+ " ⇒ ⚠️ 这只影响\"手动设定值\"的显示；⭐ 百分比分母是常量 6000、与本项无关 ✅");
			}
		}
		catch (Exception ex21)
		{
			sb.AppendLine("     · 功能号 21 原始字节试读异常：" + ex21.Message);
		}
		AppendOne(sb, wmi, BaolongdaWmi.Function.CpuThermometer, "22", "CPU 温度");
		AppendOne(sb, wmi, BaolongdaWmi.Function.CpuPower, "23", "功耗墙自定义开关");
		// ⭐ 结论清单（⭐ 复用快照的那份 —— ⛔ 不另写一套判据 ✅）
		sb.AppendLine();
		sb.AppendLine("── ③b 结论清单（⭐ 哪些功能号通 ✅）──");
		try
		{
			BaolongdaStateSnapshot snap = BaolongdaStateSnapshot.Capture(wmi);
			foreach (string line in snap.DescribeSupportList())
			{
				sb.AppendLine("  " + line);
			}
			sb.AppendLine("  ── 摘要 ──");
			sb.AppendLine("  " + snap.Describe());
		}
		catch (Exception ex)
		{
			sb.AppendLine("  ⚠️ 快照采集失败：" + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 取一个可用的 `BaolongdaWmi`：⭐ 优先用**后端已经打开的那个** ✅（⛔ 不新建连接）。
	/// <para>⚠️ 拿不到就返回 <c>null</c> ⇒ ⭐ 本节如实写"没有接口" ✅（⛔ 不假装 ✅）。</para>
	/// </summary>
	private static BaolongdaWmi ResolveWmi()
	{
		try
		{
			return Program.hw?.BaolongdaWmi;
		}
		catch
		{
			return null;
		}
	}

	private static void AppendOne(StringBuilder sb, BaolongdaWmi wmi, BaolongdaWmi.Function function, string number, string name)
	{
		string verdict;
		try
		{
			if (wmi.TryReadByteChecked(function, out byte value))
			{
				verdict = "✅ 可读，值 = " + value;
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或回显不符 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + number.PadLeft(2) + " · " + name.PadRight(18) + " · " + verdict);
	}

	private static void AppendGpuMode(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadGpuMode(out int mode))
			{
				verdict = "✅ 可读，值 = " + mode + "（" + (mode == BaolongdaWmi.GpuModeHybrid ? "混合" : "独显直连") + "）";
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或值不是 0/1 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "9".PadLeft(2) + " · " + "显卡模式".PadRight(18) + " · " + verdict);
	}

	private static void AppendColor(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadBytesChecked(BaolongdaWmi.Function.RgbKeyboardColor, 3, out byte[] rgb))
			{
				verdict = "✅ 可读，R=" + rgb[0] + " G=" + rgb[1] + " B=" + rgb[2];
			}
			else
			{
				verdict = "⚠️ 读不到（⭐ 或回显不符 ⇒ 已作废 ✅）";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "17".PadLeft(2) + " · " + "键盘灯颜色(RGB)".PadRight(18) + " · " + verdict);
	}

	private static void AppendFans(StringBuilder sb, BaolongdaWmi wmi)
	{
		string verdict;
		try
		{
			if (wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds))
			{
				verdict = "✅ 可读，第一通道 = " + speeds.First + " RPM · 第二通道 = " + speeds.Second + " RPM"
					+ "（⚠️ 哪个是 CPU/GPU **归属待 `--fan-watch` 确认** ⚠️）";
			}
			else
			{
				verdict = "⚠️ 读不到";
			}
		}
		catch (Exception ex)
		{
			verdict = "⚠️ 异常：" + ex.Message;
		}
		sb.AppendLine("  功能号 " + "13".PadLeft(2) + " · " + "风扇转速".PadRight(18) + " · " + verdict);
	}

	// ── ⑤ HID 事件如实说明（⭐ 我们没有实现 ✅ ⛔ 不假装 ✅）──
	private static void AppendHidNote(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("── ④ HID 事件（⭐ 如实说明：⭐ **未实现** ⚠️）──");
		sb.AppendLine("⭐ 参考实现（JiaolongControl）里有一张 `EventName` HID 事件表（⭐ 4~33），"
			+ "⭐ 用于**设备主动上报**状态变化（⭐ Fn 锁 / ⭐ 触摸板 / ⭐ 键盘灯亮度 / ⭐ 性能档 … ✅）");
		sb.AppendLine("⚠️ 但我们**收不到**这类事件 ⚠️ —— ⭐ 原因已查实：");
		sb.AppendLine("   我们的 HID 封装 `HidDeviceWin` **只有** `HidD_SetFeature` / `WriteFile` / `CreateFile`，"
			+ "⛔ **没有 `ReadFile`** ⇒ ⭐ **没有读输入报告的能力** ❌");
		sb.AppendLine("   且 ⭐ 现有 HID 用途**只有键盘灯下发**（⭐ 纯输出通道 ✅）");
		sb.AppendLine("⇒ ⭐ 所以本版**用周期性 WMI Get 回读**做保底 ✅（⭐ 已挂宝龙达的 3 秒定时器 ✅）");
		sb.AppendLine("⭐ 要做 HID 事件需要：⭐ ① 给 `HidDeviceWin` 加 `ReadFile`（⭐ 或走 RawInput ⚠️）· "
			+ "⭐ ② ⚠️ **在真机上确定「哪个设备 / 哪个 collection 会发事件」** ⚠️（⭐ 这一条只能真机才能定 ✅）");
		sb.AppendLine("⚠️ 在真机数据到手之前，⭐ 本项**保持「暂不支持」的如实状态** ✅（⛔ 不写「已支持」 ✅）");
	}
}
