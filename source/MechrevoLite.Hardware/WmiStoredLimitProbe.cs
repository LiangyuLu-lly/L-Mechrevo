using System;
using System.IO;
using System.Management;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// 只读探测：厂商 WMI EC mailbox（AcpiTest_MULong / GetSetULong）是否可用，
/// 以及充电上限相关寄存器（0x0740 握手、0x087F 存储上限、0x07B9 实时上限、0x07C3 门控、
/// 0x0742 生效标志、0x0770 产品 ID 首字节）的当前值。
/// <para>只读：不写任何寄存器、不改配置、不碰门控字节。</para>
/// </summary>
internal static class WmiStoredLimitProbe
{
	private const ulong ReadFlag = 0x0100UL << 32;

	private const uint ErrorSentinel = 0xFEFEFEFE;

	private static readonly StringBuilder Report = new StringBuilder();

	internal static int Run()
	{
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM AcpiTest_MULong");
			ManagementObject instance = null;
			using (ManagementObjectCollection results = searcher.Get())
			{
				foreach (ManagementObject item in results)
				{
					instance = item;
					break;
				}
			}
			if (instance == null)
			{
				Print("AcpiTest_MULong 类不存在 ⇒ 本机没有这条 WMI mailbox 通路（B 路不可用）");
				WriteReportFile();
				return 2;
			}
			Print("AcpiTest_MULong 类存在 ✅");
			bool hasMethod = false;
			try
			{
				using ManagementClass meta = new ManagementClass(new ManagementPath("root\\WMI:AcpiTest_MULong"));
				foreach (MethodData method in meta.Methods)
				{
					Print("  method: " + method.Name);
					if (string.Equals(method.Name, "GetSetULong", StringComparison.OrdinalIgnoreCase))
					{
						hasMethod = true;
					}
				}
			}
			catch
			{
				// 元数据枚举失败不影响后续按名字调用
				hasMethod = true;
			}
			if (!hasMethod)
			{
				Print("GetSetULong 方法不存在 ⇒ B 路不可用");
				WriteReportFile();
				return 2;
			}
			uint? probe = Read(instance, 0x0740);
			if (!probe.HasValue)
			{
				Print("mailbox 读 0x0740 失败 ⇒ B 路不可用");
				WriteReportFile();
				return 2;
			}
			Print("mailbox[0x0740] = " + Hex(probe.Value) + "（项目号低字节 = 0x" + (probe.Value & 0xFF).ToString("X2") + "）");
			if (probe.Value == ErrorSentinel)
			{
				Print("0xFEFEFEFE = mailbox 超时/未实现 ⇒ B 路不可用（不写任何东西）");
				WriteReportFile();
				return 2;
			}
			int[] addresses = { 0x087F, 0x07B9, 0x07C3, 0x0742, 0x0770 };
			string[] labels = { "存储上限", "实时充电上限", "平台门控", "生效标志(bit2)", "ROMID[0]" };
			for (int i = 0; i < addresses.Length; i++)
			{
				uint? value = Read(instance, (ushort)addresses[i]);
				if (!value.HasValue)
				{
					Print("mailbox[0x" + addresses[i].ToString("X4") + "]（" + labels[i] + "）读失败");
					continue;
				}
				string note = "";
				if (addresses[i] == 0x087F)
				{
					note = " · 存储上限低7位=" + (value.Value & 0x7F);
				}
				else if (addresses[i] == 0x07B9)
				{
					note = " · 实时上限低7位=" + (value.Value & 0x7F) + "（bit7=" + ((value.Value & 0x80) != 0 ? "reached/已启用" : "0") + "）";
				}
				else if (addresses[i] == 0x07C3)
				{
					note = " · 门控=" + value.Value + "（4/5 为开门值）";
				}
				else if (addresses[i] == 0x0742)
				{
					note = " · bit2=" + (((value.Value & 4) != 0) ? "1（门控开）" : "0");
				}
				Print("mailbox[0x" + addresses[i].ToString("X4") + "]（" + labels[i] + "） = " + Hex(value.Value) + note);
			}
			Print("只读探测完成：本机 WMI mailbox 可用；未写任何寄存器。");
			WriteReportFile();
			return 0;
		}
		catch (Exception ex)
		{
			Print("探测失败：" + ex.GetType().Name + " " + ex.Message);
			WriteReportFile();
			return 1;
		}
	}

	/// <summary>
	/// B 路写入（正式路径）：写 0x087F 存储上限并回读校验；成功返回 true。
	/// <para>percent ≥ 100 时写 0（= 取消存储上限/恢复默认充电）；40..99 写该值。</para>
	/// <para>只写 0x087F 一个地址，不碰门控/ROMID；失败不重试。</para>
	/// </summary>
	internal static bool TryWriteStoredLimit(int percent, out string detail)
	{
		detail = string.Empty;
		try
		{
			if (percent < 40 || percent > 100)
			{
				detail = "拒绝：percent 必须在 40..100（收到 " + percent + "）";
				return false;
			}
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM AcpiTest_MULong");
			ManagementObject instance = null;
			using (ManagementObjectCollection results = searcher.Get())
			{
				foreach (ManagementObject item in results)
				{
					instance = item;
					break;
				}
			}
			if (instance == null)
			{
				detail = "AcpiTest_MULong 类不存在";
				return false;
			}
			uint? probe = Read(instance, 0x0740);
			if (!probe.HasValue || probe.Value == ErrorSentinel)
			{
				detail = "mailbox 握手失败";
				return false;
			}
			int value = (percent >= 100) ? 0 : percent;
			Write(instance, 0x087F, (byte)value);
			uint? verify = Read(instance, 0x087F);
			int stored = verify.HasValue ? (int)(verify.Value & 0x7F) : -1;
			detail = "写 0x087F=" + value + " · 回读=" + stored;
			if (stored != value)
			{
				detail += " ⇒ 不一致";
				return false;
			}
			Logger.WriteError("[charge-limit] B 路写入存储上限成功（" + detail + "，项目号 0x" + (probe.Value & 0xFF).ToString("X2") + "）");
			return true;
		}
		catch (Exception ex)
		{
			detail = "异常：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>写入测试：写 0x087F（40..100）并回读；--restore 时按旧值还原。</summary>
	internal static int RunWrite(int value, bool restore)
	{
		Report.Clear();
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM AcpiTest_MULong");
			ManagementObject instance = null;
			using (ManagementObjectCollection results = searcher.Get())
			{
				foreach (ManagementObject item in results)
				{
					instance = item;
					break;
				}
			}
			if (instance == null)
			{
				Print("AcpiTest_MULong 类不存在 ⇒ B 路不可用");
				WriteReportFile();
				return 2;
			}
			uint? probe = Read(instance, 0x0740);
			if (!probe.HasValue || probe.Value == ErrorSentinel)
			{
				Print("mailbox 握手失败/超时 ⇒ 不写任何东西");
				WriteReportFile();
				return 2;
			}
			uint? oldValue = Read(instance, 0x087F);
			int oldLimit = oldValue.HasValue ? (int)(oldValue.Value & 0x7F) : -1;
			Print("mailbox[0x0740] = " + Hex(probe.Value) + "（项目号 0x" + (probe.Value & 0xFF).ToString("X2") + "）");
			Print("写前 0x087F = " + (oldValue.HasValue ? Hex(oldValue.Value) : "读不到") + "（低7位=" + oldLimit + "）");
			if (restore)
			{
				Print("restore 模式：把 0x087F 写回 0（= 取消存储上限/恢复默认充电）");
				value = 0;
			}
			if (!restore && (value < 40 || value > 100))
			{
				Print("拒绝写入：值必须在 40..100（收到 " + value + "）");
				WriteReportFile();
				return 1;
			}
			uint? written = Write(instance, 0x087F, (byte)value);
			Print("写入 0x087F=" + value + " ⇒ 返回 " + (written.HasValue ? Hex(written.Value) : "调用失败"));
			uint? verify = Read(instance, 0x087F);
			int verifyLimit = verify.HasValue ? (int)(verify.Value & 0x7F) : -1;
			Print("回读 0x087F = " + (verify.HasValue ? Hex(verify.Value) : "读不到") + "（低7位=" + verifyLimit + "）");
			bool ok = verifyLimit == value;
			Print(ok ? "✅ 写入并回读一致：B 路写 0x087F 有效（是否跨重启持久需重启后再跑 --charge-limit-wmi-probe 确认）"
				: "❌ 回读不一致：固件忽略 mailbox 写或不持久 ⇒ B 路不可靠，应退回 A 路");
			WriteReportFile();
			return ok ? 0 : 1;
		}
		catch (Exception ex)
		{
			Print("写入测试失败：" + ex.GetType().Name + " " + ex.Message);
			WriteReportFile();
			return 1;
		}
	}

	private static uint? Write(ManagementObject instance, ushort address, byte value)
	{
		try
		{
			ulong data = ((ulong)value << 16) | address;
			using ManagementBaseObject inParams = instance.GetMethodParameters("GetSetULong");
			inParams["Data"] = data;
			using ManagementBaseObject outParams = instance.InvokeMethod("GetSetULong", inParams, null);
			if (outParams == null || outParams["Return"] == null)
			{
				return null;
			}
			return Convert.ToUInt32(outParams["Return"]);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[charge-limit] WMI mailbox 写 0x" + address.ToString("X4") + " 失败：" + ex.Message);
			return null;
		}
	}

	private static uint? Read(ManagementObject instance, ushort address)
	{
		try
		{
			ulong data = ReadFlag | address;
			using ManagementBaseObject inParams = instance.GetMethodParameters("GetSetULong");
			inParams["Data"] = data;
			using ManagementBaseObject outParams = instance.InvokeMethod("GetSetULong", inParams, null);
			if (outParams == null || outParams["Return"] == null)
			{
				return null;
			}
			return Convert.ToUInt32(outParams["Return"]);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[charge-limit] WMI mailbox 读 0x" + address.ToString("X4") + " 失败：" + ex.Message);
			return null;
		}
	}

	private static string Hex(uint value)
	{
		return "0x" + value.ToString("X8");
	}

	private static void Print(string text)
	{
		Report.AppendLine(text);
		try
		{
			Console.WriteLine(text);
		}
		catch
		{
		}
		try
		{
			Logger.WriteError("[charge-limit-wmi] " + text);
		}
		catch
		{
		}
	}

	/// <summary>把探测结果写到桌面文件（WinForms 程序无控制台，用户需要可见结果）。</summary>
	internal static string WriteReportFile()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"lm-charge-limit-wmi-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
			File.WriteAllText(path, "lm-charge-limit-wmi-probe " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n\r\n" + Report, Encoding.UTF8);
			Logger.WriteError("[charge-limit-wmi] 报告文件：" + path);
			return path;
		}
		catch (Exception ex)
		{
			Logger.WriteError("[charge-limit-wmi] 写报告文件失败：" + ex.Message);
			return string.Empty;
		}
	}
}
