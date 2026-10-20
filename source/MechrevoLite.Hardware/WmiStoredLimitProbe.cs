using System;
using System.Management;

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
				return 2;
			}
			uint? probe = Read(instance, 0x0740);
			if (!probe.HasValue)
			{
				Print("mailbox 读 0x0740 失败 ⇒ B 路不可用");
				return 2;
			}
			Print("mailbox[0x0740] = " + Hex(probe.Value) + "（项目号低字节 = 0x" + (probe.Value & 0xFF).ToString("X2") + "）");
			if (probe.Value == ErrorSentinel)
			{
				Print("0xFEFEFEFE = mailbox 超时/未实现 ⇒ B 路不可用（不写任何东西）");
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
			return 0;
		}
		catch (Exception ex)
		{
			Print("探测失败：" + ex.GetType().Name + " " + ex.Message);
			return 1;
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
}
