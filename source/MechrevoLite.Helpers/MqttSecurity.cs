using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

public static class MqttSecurity
{
	public sealed record OperationResult(bool Success, string Message);

	public const string FirewallRuleName = "L-Mechrevo - Block remote GCU MQTT";

	public const int DefaultPort = 13688;

	private const int DirectionInbound = 1;

	private const int ActionBlock = 0;

	private const int ProtocolTcp = 6;

	private const int ProfileAll = int.MaxValue;

	public static bool HasInboundBlockRule(int port = 13688)
	{
		object obj = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
			if ((object)typeFromProgID == null)
			{
				return false;
			}
			obj = Activator.CreateInstance(typeFromProgID);
			if (obj == null)
			{
				return false;
			}
			dynamic val = obj;
			int num = val.CurrentProfileTypes;
			foreach (dynamic item in val.Rules)
			{
				try
				{
					if (item.Enabled && item.Direction == 1 && item.Action == 0 && item.Protocol == 6 && string.Equals((string)item.Name, "L-Mechrevo - Block remote GCU MQTT", StringComparison.Ordinal) && (((int)item.Profiles & num) != 0 || (int)item.Profiles == int.MaxValue) && PortListContains((string)item.LocalPorts, port))
					{
						return true;
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Firewall rule inspection failed: " + ex.Message);
				}
				finally
				{
					if (Marshal.IsComObject(item))
					{
						Marshal.FinalReleaseComObject(item);
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Firewall policy inspection failed: " + ex2.Message);
		}
		finally
		{
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
		return false;
	}

	public static OperationResult TryCreateInboundBlockRule(int port = 13688)
	{
		if (!ProcessHelper.IsUserAdministrator())
		{
			return new OperationResult(Success: false, "创建防火墙规则需要管理员权限。");
		}
		if (HasInboundBlockRule(port))
		{
			return new OperationResult(Success: true, $"入站阻断规则已存在，远程访问 {port} 端口已被拦截。");
		}
		object obj = null;
		object obj2 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
			Type typeFromProgID2 = Type.GetTypeFromProgID("HNetCfg.FWRule");
			if ((object)typeFromProgID == null || (object)typeFromProgID2 == null)
			{
				return new OperationResult(Success: false, "本机没有可用的 Windows 防火墙 COM 接口。");
			}
			obj = Activator.CreateInstance(typeFromProgID);
			obj2 = Activator.CreateInstance(typeFromProgID2);
			if (obj == null || obj2 == null)
			{
				return new OperationResult(Success: false, "无法创建防火墙 COM 对象。");
			}
			dynamic val = obj2;
			val.Name = "L-Mechrevo - Block remote GCU MQTT";
			val.Description = "阻止从其他主机访问机械革命 GCUBridge 的 MQTT 端口。本机连接不受影响。";
			val.Protocol = 6;
			val.LocalPorts = port.ToString();
			val.Direction = 1;
			val.Action = 0;
			val.Profiles = int.MaxValue;
			val.Enabled = true;
			dynamic val2 = obj;
			val2.Rules.Add(val);
			return HasInboundBlockRule(port) ? new OperationResult(Success: true, $"已创建入站阻断规则，{port} 端口不再接受来自其他主机的连接。") : new OperationResult(Success: false, "规则已提交但回读校验失败，请检查组策略是否限制了防火墙规则。");
		}
		catch (Exception ex)
		{
			return new OperationResult(Success: false, "创建防火墙规则失败：" + ex.Message);
		}
		finally
		{
			if (obj2 != null && Marshal.IsComObject(obj2))
			{
				Marshal.FinalReleaseComObject(obj2);
			}
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}

	public static OperationResult TryRemoveInboundBlockRule()
	{
		if (!ProcessHelper.IsUserAdministrator())
		{
			return new OperationResult(Success: false, "删除防火墙规则需要管理员权限。");
		}
		object obj = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
			if ((object)typeFromProgID == null)
			{
				return new OperationResult(Success: false, "本机没有可用的 Windows 防火墙 COM 接口。");
			}
			obj = Activator.CreateInstance(typeFromProgID);
			if (obj == null)
			{
				return new OperationResult(Success: false, "无法创建防火墙 COM 对象。");
			}
			dynamic val = obj;
			val.Rules.Remove("L-Mechrevo - Block remote GCU MQTT");
			return HasInboundBlockRule() ? new OperationResult(Success: false, "删除后规则仍然存在。") : new OperationResult(Success: true, "已删除入站阻断规则。");
		}
		catch (Exception ex)
		{
			return new OperationResult(Success: false, "删除防火墙规则失败：" + ex.Message);
		}
		finally
		{
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}

	internal static string MissingRuleWarning(int port = 13688)
	{
		return $"SECURITY: 厂商 GCU 的 MQTT 端口 {port} 监听在所有网络接口且凭据固定，当前没有 L-Mechrevo 的入站阻断规则，同网络的其他主机可以控制本机硬件。以管理员身份运行 \"L-Mechrevo.exe --secure-mqtt\" 可创建阻断规则（不影响本机连接）。";
	}

	internal static bool PortListContains(string? ports, int expected)
	{
		if (string.IsNullOrWhiteSpace(ports))
		{
			return false;
		}
		string[] array = ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			if (int.TryParse(text, out var result) && result == expected)
			{
				return true;
			}
			string[] array2 = text.Split('-', 2, StringSplitOptions.TrimEntries);
			if (array2.Length == 2 && int.TryParse(array2[0], out var result2) && int.TryParse(array2[1], out var result3) && expected >= result2 && expected <= result3)
			{
				return true;
			}
		}
		return false;
	}
}
