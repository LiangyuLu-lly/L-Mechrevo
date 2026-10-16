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
		sb.AppendLine("⭐ 判据依据：⭐ 宝龙达为 `MRID6-23`（⭐ 真机证实）；⭐ 同方机型实测板号为 "
			+ "GM5HG0A / GM6IX8B / KUANGSHI-* / YAOSHI-* / Standard ⇒ ⭐ 均**不是** `MRID6-` 前缀 ✅");
		sb.AppendLine("⚠️ 已知不确定：⭐ 30 系蛟龙板号**未证实** ⚠️ ⇒ ⭐ 若它不是 `MRID6-*`，"
			+ "本机会按同方走 GCU（⭐ 保守无害 ✅）");
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
	//   ⭐ 与 ②b 的**关键差别**：端口通道**会超时**（等 IBF/OBF 超过 250ms 即失败并置「中毒」），
	//     ⇒ ⭐ 它能**明确区分**「地址不可达」与「通道根本不通」；
	//       而 ②b 那条 H2RAM 通道对窗口外地址**静默回填充值**、两种失败长得一模一样 ⚠️。
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
		try
		{
			sb.AppendLine("PawnIO 驱动 = " + EcPortIo.DescribePawnInstalled());

			// ① 读 EC 版本 4 字节（XRAM 0x8410~0x8413）
			int[] version = new int[4];
			EcPortIo.TryReadBytes(EcChargeLimit.XramEcVersionRegister, 4, out version);
			for (int i = 0; i < version.Length; i++)
			{
				int addr = EcChargeLimit.XramEcVersionRegister + i;
				sb.AppendLine("试读 XRAM 0x" + addr.ToString("X4") + " = "
					+ ((version[i] >= 0) ? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(version[i])) : "⛔ 读不到"));
			}

			// ② 交叉校验读（⭐ 作用：判断「是不是真的在跟一颗 IT5570 对话」，而不只是「调用没报错」⚠️）
			bool socOk = EcPortIo.TryReadByte(0x847C, out int soc);
			sb.AppendLine("交叉校验 · XRAM 0x847C（SOC，合理区间 0~100）= "
				+ (socOk ? ("✅ 读得 " + soc + ((soc < 0 || soc > 100) ? " ⚠️ 超出合理区间" : " ✅ 在合理区间")) : "⛔ 读不到"));
			bool modeOk = EcPortIo.TryReadByte(0x83C0, out int mode);
			sb.AppendLine("交叉校验 · XRAM 0x83C0（模式，合理值 0/2/0x48）= "
				+ (modeOk ? ("✅ 读得 " + ChargeLimitEcProbe.FormatRaw(mode)) : "⛔ 读不到"));

			sb.AppendLine("通道 = " + EcPortIo.DescribeChannel());
			sb.AppendLine();

			// ③ 结论 —— ⚠️ 判据是「值本身合理」，⛔ 不是「调用没报错」（⭐ 这正是 ②b 得到的教训 ✅）
			int okCount = 0;
			int ffCount = 0;
			foreach (int v in version)
			{
				if (v >= 0)
				{
					okCount++;
					if (v == 0xFF)
					{
						ffCount++;
					}
				}
			}
			bool allFf = okCount == 4 && ffCount == 4;
			if (okCount == 4 && !allFf)
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
				sb.AppendLine("结论 = ✅ **EC 版本可读（端口通道）** ⇒ EC 版本 = " + ver);
				if (ffCount > 0)
				{
					sb.AppendLine("   ⚠️ 但其中有 " + ffCount + " 个字节是 0xFF ⇒ 疑似填充值混入，⭐ 请以**原始字节**为准 ⚠️");
				}
				sb.AppendLine("   ⇒ ⭐ 这正是 ②b 那条 H2RAM 通道**读不到**的东西 ⇒ ⭐ **端口通道是有效通路** ✅");
			}
			else if (allFf)
			{
				sb.AppendLine("结论 = ⚠️ **端口通道通了、但四个字节全是 0xFF** ⚠️");
				sb.AppendLine("   ⇒ ⭐ 说明**端口读写机制本身是工作的**（没有超时、没中毒）✅");
				sb.AppendLine("   ⇒ 但该地址**读不到真实版本**，两种可能：");
				sb.AppendLine("      ① 本机 EC **不是 IT5570**（⚠️ 本机是同方翼龙15Pro，蛟龙才是 IT5570）");
				sb.AppendLine("         ⇒ ⭐ 该 XRAM 地址在本机**未映射**（对本机而言这是**正常**结果、⛔ 不是故障）");
				sb.AppendLine("      ② 该地址真实值就是 0xFF（⚠️ 四个字节全 FF 不像版本号）");
				sb.AppendLine("   ⇒ ⭐ 请以**蛟龙（宝龙达）真机**的结果为准 ✅");
				if (socOk && soc >= 0 && soc <= 100)
				{
					sb.AppendLine("   ⚠️ 但交叉校验读到了**合理**的 SOC（" + soc + "）⇒ ⭐ 该机地址映射可能**部分可用**，");
					sb.AppendLine("      建议把本节输出整段发回 ⚠️");
				}
			}
			else if (okCount > 0)
			{
				sb.AppendLine("结论 = ⚠️ **部分可读（" + okCount + "/4 字节）** ⇒ 版本号无法可靠解析，请以上面原始字节为准 ⚠️");
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

	// ── ③ 功能号 8~23 逐个试读 ──
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
		// ⭐⭐ 2026-10-16（宝龙达 · 转速百分比）：⭐ **补试读功能号 21**（`MaxFanSpeed`）✅ ——
		//   ⚠️ 核查发现本节**从未试读过它**（只读了 8/13/20/22/23）⇒ ⭐ "功能号 21 能不能读到
		//   **本机最大转速**"此前**无人知道** ⚠️。⭐ 它是风扇百分比**分母**的第一候选 ✅。
		//   ⚠️ 写是双字节 `[FanType, RPM÷100]`（⭐ 见 `TrySetMaxFanSpeed` ✅）⇒ ⭐ **读的语义未验证**
		//   ⇒ ⭐ 所以下面把**原始字节**也打出来，让诊断包自行判断 ⛔（⭐ 不猜 ✅）。
		AppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeed, "21", "风扇最大转速");
		try
		{
			if (wmi.TryReadBytesChecked(BaolongdaWmi.Function.MaxFanSpeed, 4, out byte[] mfsRaw)
				&& mfsRaw != null && mfsRaw.Length >= 4)
			{
				sb.AppendLine($"     · 功能号 21 原始 4 字节 = {mfsRaw[0]},{mfsRaw[1]},{mfsRaw[2]},{mfsRaw[3]}"
					+ $" ⇒ 单字节={mfsRaw[0]} · 小端16位={(mfsRaw[1] << 8) + mfsRaw[0]}"
					+ $" · 大端16位={(mfsRaw[0] << 8) + mfsRaw[1]}"
					+ " · ⚠️ 也可能是 RPM÷100 口径 ⚠️ ⇒ **语义待判定**（⭐ 不猜 ✅）");
			}
			else
			{
				sb.AppendLine("     · 功能号 21 只读到 1 字节或读不到"
					+ " ⇒ ⚠️ 不能据此推 max（⭐ 若读不到就只能退化为**参考分母** ✅）");
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
