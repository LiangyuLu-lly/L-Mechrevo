using System;
using System.Collections.Generic;
using System.Management;
using System.Text;
using MechrevoLite.Hardware;
using Probe;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-14（用户原话「要在 LM 里加一条【只读日志】打出机型码字段」）：
/// 目的 = 机主跑一次 LM 就能把「平台码 ↔ 真实机型」的原始字段收回来，好建立映射表
/// （⭐ 现在**没有**这张表，见 docs\notes\GCU载荷对比-星耀14-星耀14UX7-星耀15.md 第五节/第七节）。
/// <para>
/// ⛔ **严格只读**：只查 WMI（Win32_BaseBoard / Win32_ComputerSystem / Win32_BIOS /
/// Win32_ComputerSystemProduct）+ 复用既有的 EC 机型码读取通道（<see cref="ModelRegistry.Read" />，
/// 内部只做 <c>\\.\ACPIDriver</c> 的 H2RAM 读）。⛔ 不写注册表 / EC / ACPI / NVRAM，⛔ 不改任何状态。
/// </para>
/// <para>
/// ⭐ **复用**：BIOS 项目码走的就是 LM 现成那一套 —— <see cref="MechrevoService.EcReadTransportFactory" />
/// （与 <c>RuntimeModelSupport.Current()</c> / <c>PlDefaults.Read</c> 同一个工厂）→
/// <see cref="ModelRegistry.Read" /> → <see cref="ModelIdentity.BiosProjectId" />。
/// ⛔ 这里**不另造**第二套 EC/BIOS 项目码读取实现。
/// </para>
/// <para>
/// ⚠️ **两个出口**（用户要求「最稳的是两条都做」）：
/// ① 诊断包 <c>系统信息.txt</c> 的 <see cref="SectionTitle" /> 一节 —— **默认日志级别下唯一可靠**的出口
///    （默认级别是「仅错误」，Info 行不落盘）；
/// ② 启动时一行 <see cref="LogStartupState" /> —— 级别为「全部」时直接可见。
/// </para>
/// <para>
/// ⚠️ WMI 失败一律 <c>try/catch</c> 并记 "unavailable"，⛔ 不抛异常、⛔ 不让启动路径挂掉。
/// </para>
/// </summary>
internal static class MachineCodeProbe
{
	internal const string SectionTitle = "[机型码字段（只读）]";

	private static bool _startupLogged;

	/// <summary>WMI 取不到时的占位（与「字段确实是空串」区分开）。</summary>
	internal const string Unavailable = "unavailable";

	/// <summary>
	/// 一次性把全部机型码字段读成「标签 → 原始值」的有序表（**原始值，未打码**）。
	/// ⭐ 诊断包一节与启动日志一行**共用这一份采集**（口径只有一份，绝不各读一套）。
	/// ⛔ 任何单项失败都只记 <see cref="Unavailable" />，绝不抛。
	/// </summary>
	internal static List<KeyValuePair<string, string>> ReadFields()
	{
		Dictionary<string, string> computerSystem = QueryWmi(
			"SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber FROM Win32_ComputerSystem",
			"Manufacturer", "Model", "SystemFamily", "SystemSKUNumber");
		Dictionary<string, string> baseBoard = QueryWmi(
			"SELECT Manufacturer, Product, Version FROM Win32_BaseBoard",
			"Manufacturer", "Product", "Version");
		Dictionary<string, string> bios = QueryWmi(
			"SELECT SMBIOSBIOSVersion, Version FROM Win32_BIOS",
			"SMBIOSBIOSVersion", "Version");
		Dictionary<string, string> product = QueryWmi(
			"SELECT Name, Version FROM Win32_ComputerSystemProduct",
			"Name", "Version");

		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>(16)
		{
			Pair("Win32_BaseBoard.Product", Field(baseBoard, "Product")),
			Pair("Win32_BaseBoard.Manufacturer", Field(baseBoard, "Manufacturer")),
			Pair("Win32_BaseBoard.Version", Field(baseBoard, "Version")),
			Pair("Win32_ComputerSystem.Model", Field(computerSystem, "Model")),
			Pair("Win32_ComputerSystem.Manufacturer", Field(computerSystem, "Manufacturer")),
			Pair("Win32_BIOS.SMBIOSBIOSVersion", Field(bios, "SMBIOSBIOSVersion")),
			Pair("Win32_BIOS.Version", Field(bios, "Version")),
			Pair("Win32_ComputerSystemProduct.Name", Field(product, "Name")),
			Pair("Win32_ComputerSystemProduct.Version", Field(product, "Version")),
			Pair("Win32_ComputerSystem.SystemFamily", Field(computerSystem, "SystemFamily")),
			Pair("Win32_ComputerSystem.SystemSKUNumber", Field(computerSystem, "SystemSKUNumber"))
		};

		AppendEcIdentity(list);
		AppendExistingProjectCodeSources(list);
		return list;
	}

	/// <summary>
	/// ⭐ 把 LM **现成**的两处项目码来源也一并打出（⛔ 都是**复用**，没有新造读取实现）：
	/// ① <see cref="MechrevoDeviceCapabilities.ProjectId" /> —— 注册表
	///    <c>HKLM\SOFTWARE\OEM\GamingCenter*\ItemSupport</c> 的 <c>BIOS_PROJECT_ID</c> 一族
	///    （由 GCU 写入；这正是用户提到的「仓库里已有类似 -BiosProjectId 的读取方式」在 LM 里的落点）；
	/// ② <see cref="MechrevoHw.BiosProjectId" /> —— GCU 运行期 MQTT <c>Customize/Info</c> 上报的 ProjectID。
	/// ⚠️ 两处都只读内存态/注册表，⛔ 不写任何地方。
	/// </summary>
	private static void AppendExistingProjectCodeSources(List<KeyValuePair<string, string>> list)
	{
		// ① 注册表侧（MechrevoDeviceCapabilities.Load 的既有实现；内部每处都自带 try/catch）
		try
		{
			MechrevoDeviceCapabilities capabilities = MechrevoDeviceCapabilities.Current;
			list.Add(Pair("注册表项目码（ItemSupport BIOS_PROJECT_ID）", capabilities.ProjectId));
		}
		catch (Exception ex)
		{
			list.Add(Pair("注册表项目码（ItemSupport BIOS_PROJECT_ID）", "读取失败（" + ex.GetType().Name + "）"));
		}
		// ② GCU 运行期上报侧（Program.hw 内存态，⛔ 不新增任何 MQTT 订阅/GET）
		try
		{
			MechrevoHw hw = Program.hw;
			if (hw == null)
			{
				list.Add(Pair("GCU 上报项目码（Customize/Info ProjectID）", "尚未收到（Program.hw 还没创建）"));
			}
			else
			{
				string reported = hw.BiosProjectId;
				list.Add(Pair("GCU 上报项目码（Customize/Info ProjectID）", string.IsNullOrWhiteSpace(reported) ? "尚未收到" : reported));
			}
		}
		catch (Exception ex2)
		{
			list.Add(Pair("GCU 上报项目码（Customize/Info ProjectID）", "读取失败（" + ex2.GetType().Name + "）"));
		}
	}

	/// <summary>
	/// ⭐ BIOS 项目码 / EC 机型码：**复用** LM 现成的读取通路（<see cref="MechrevoService.EcReadTransportFactory" />
	/// → <see cref="ModelRegistry.Read" />，即 <c>ModelIdentity.ProjectId</c> / <c>.BiosProjectId</c>）。
	/// ⛔ 不新造读取实现；⛔ 通道不可用就如实写原因，不抛。
	/// </summary>
	private static void AppendEcIdentity(List<KeyValuePair<string, string>> list)
	{
		IEcReadTransport transport = null;
		try
		{
			Func<IEcReadTransport> factory = MechrevoService.EcReadTransportFactory;
			transport = factory?.Invoke();
		}
		catch (Exception ex)
		{
			list.Add(Pair("EC 机型码通道", "不可用（打开读取通道失败：" + ex.GetType().Name + "）"));
			return;
		}
		if (transport == null)
		{
			list.Add(Pair("EC 机型码通道", "不可用（\\.\\ACPIDriver 打不开：UWACPIDriver.sys 未装/未加载，或权限不足）"));
			list.Add(Pair("EC 机型码（ProjectId）", Unavailable));
			list.Add(Pair("BIOS 项目码（BiosProjectId）", Unavailable));
			return;
		}
		try
		{
			// ⚠️ ModelRegistry.Read 内部自带 try/catch，异常时返回 ModelIdentity.Unknown，⛔ 不外抛。
			ModelIdentity identity = ModelRegistry.Read(transport);
			list.Add(Pair("EC 机型码（ProjectId）", identity.ProjectId));
			list.Add(Pair("EC 机型码原始字节", identity.RawProjectByte.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			list.Add(Pair("EC 机型码来源", identity.Source.ToString()));
			list.Add(Pair("BIOS 项目码（BiosProjectId）", identity.BiosProjectId));
			list.Add(Pair("EC 机型码通道", "OK（\\.\\ACPIDriver · H2RAM）"));
		}
		catch (Exception ex2)
		{
			list.Add(Pair("EC 机型码通道", "不可用（读取失败：" + ex2.GetType().Name + "）"));
		}
		finally
		{
			try
			{
				(transport as IDisposable)?.Dispose();
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 出口①：诊断包 <c>系统信息.txt</c> 的 <see cref="SectionTitle" /> 一节。
	/// ⚠️ 序列号相关字段**一律先 <see cref="DiagnosticGcuEvidenceInfo.Mask" /> 再落文本**
	/// （与包内其它节同一份打码实现），⛔ 绝不把未打码的值拼进包里。
	/// </summary>
	internal static string BuildSection()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine(SectionTitle);
		sb.AppendLine("（2026-10-14 新增 · 纯只读：只查 WMI + 复用既有 EC 机型码通道；⛔ 不写注册表/EC/ACPI/NVRAM，⛔ 不改任何状态）");
		sb.AppendLine("目的：把「PH4*/PH6* 平台码 ↔ 真实机型」的原始字段收回来建映射表（该表目前不存在，见 docs\\notes\\GCU载荷对比-星耀14-星耀14UX7-星耀15.md）。");
		try
		{
			foreach (KeyValuePair<string, string> item in ReadFields())
			{
				sb.AppendLine("  " + item.Key + " = " + Describe(item.Value));
			}
			sb.AppendLine("  ⚠️ 说明：BIOS 项目码 / EC 机型码走的是 LM 现成的那一套（MechrevoService.EcReadTransportFactory → ModelRegistry.Read → ModelIdentity.BiosProjectId），⛔ 没有另造读取实现；");
			sb.AppendLine("     另附 LM 既有的两处项目码来源（同样是复用，不新造）：注册表 ItemSupport 的 BIOS_PROJECT_ID（MechrevoDeviceCapabilities.ProjectId）与 GCU 运行期上报的 Customize/Info ProjectID（MechrevoHw.BiosProjectId）。");
			sb.AppendLine("     Win32_BIOS 一节**不含序列号**（SerialNumber / IdentifyingNumber 本探测从不读取，因此也不会有值进包）。");
			sb.AppendLine("     WMI 不可用时对应字段写 " + Unavailable + "（不抛异常）；EC 通道不可用时写明原因。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("  该项读取失败（" + ex.GetType().Name + "），本节的其它内容不受影响。");
		}
		sb.AppendLine();
		return sb.ToString();
	}

	/// <summary>
	/// 出口②：启动时打一行（与 UiQaShowAll / Force30Ui 等既有 <c>LogStartupState</c> 同一位置、同一风格）。
	/// ⚠️ 走 <see cref="Logger.WriteLine" />（自动分级）：一切正常时是 **Info** —— 默认级别「仅错误」下**不落盘**；
	/// 一旦某项读不到（文案里带「不可用/失败」等标记）就会自动升级成 **Error**，默认级别下**看得见**。
	/// ⭐ 默认级别下要拿到这些字段，请用诊断包（<c>系统信息.txt</c> 的 <see cref="SectionTitle" /> 一节）。
	/// </summary>
	internal static void LogStartupState()
	{
		if (_startupLogged)
		{
			return;
		}
		_startupLogged = true;
		try
		{
			StringBuilder sb = new StringBuilder();
			foreach (KeyValuePair<string, string> item in ReadFields())
			{
				if (sb.Length > 0)
				{
					sb.Append(" | ");
				}
				sb.Append(item.Key).Append('=').Append(Describe(item.Value));
			}
			Logger.WriteLine("[机型码] " + sb.ToString() + " | 只读探测（⛔ 不写注册表/EC/ACPI/NVRAM）；序列号从不读取；BIOS 项目码复用 ModelRegistry.Read");
		}
		catch (Exception ex)
		{
			// ⛔ 探测失败绝不允许影响启动：只记一行（含 error 标记 ⇒ 默认级别下也看得见）。
			Logger.WriteLine("[机型码] 探测失败（" + ex.GetType().Name + "）；诊断包的 " + SectionTitle + " 一节会写明逐项结果。");
		}
	}

	/// <summary>
	/// 单值 → 可落文本的字符串：空/缺失 → <see cref="Unavailable" />；其余先按包内**同一份**打码实现处理。
	/// </summary>
	private static string Describe(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Unavailable;
		}
		return DiagnosticGcuEvidenceInfo.Mask(value.Trim());
	}

	private static KeyValuePair<string, string> Pair(string label, string value)
	{
		return new KeyValuePair<string, string>(label, value);
	}

	/// <summary>取属性值；属性不在结果集里 / 取不到 → 空串（由 <see cref="Describe" /> 统一变成 unavailable）。</summary>
	private static string Field(IReadOnlyDictionary<string, string> values, string name)
	{
		if (values.TryGetValue(name, out string value))
		{
			return value;
		}
		return string.Empty;
	}

	/// <summary>
	/// 只读 WMI 查询：⛔ 任何失败（无权限 / WMI 仓库坏 / 类不存在）都返回空表，⛔ 不抛。
	/// ⚠️ 与包内既有做法一致（<see cref="DiagnosticSystemInfo" /> 的 QueryGpus / DiagnosticDisplayInfo 的 WmiMonitorID）。
	/// </summary>
	private static Dictionary<string, string> QueryWmi(string query, params string[] properties)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
			foreach (ManagementBaseObject item in searcher.Get())
			{
				foreach (string property in properties)
				{
					try
					{
						if (!result.ContainsKey(property))
						{
							result[property] = item[property]?.ToString()?.Trim() ?? string.Empty;
						}
					}
					catch
					{
						// 单个属性取不到：当作 unavailable，继续取其余属性。
					}
				}
				break;
			}
		}
		catch
		{
			// WMI 整体不可用：整表留空 ⇒ 每个字段都会写 unavailable。
		}
		return result;
	}
}
