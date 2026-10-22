using System;
using System.Collections.Generic;
using System.IO;
using MechrevoLite.Mode;
using Newtonsoft.Json.Linq;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（机主：无界风扇表能不能读官方表/对应温度）：
/// 读取 GCU 载荷自带的官方风扇表 JSON（<c>UserFanTables\DefaultFanTable_*.json</c>），
/// 解析出温度→占空比曲线供风扇曲线窗口显示/初始化。
///
/// <para>⚠️ 这是 GCU 软件层表，不保证等于 EC 固件内部实际执行的表；只做显示/参考，不自动下发。</para>
/// </summary>
internal static class WujieFanTableFile
{
	internal const int MaxPoints = 10;

	internal const int PanelPoints = 16;

	/// <summary>2026-10-22（机主：温度不能是奇怪的个位数温度）：官方表里 UpT=0 这类点不再显示/参与曲线。</summary>
	internal const int MinimumTemperatureC = 10;

	/// <summary>按档位读取官方表；失败时 detail 说明原因（文件缺失/格式异常），不改任何硬件状态。</summary>
	internal static bool TryLoad(string modeId, out byte[] cpuTemps, out byte[] cpuDuties, out byte[] gpuTemps, out byte[] gpuDuties, out string sourcePath, out string detail)
	{
		cpuTemps = new byte[PanelPoints];
		cpuDuties = new byte[PanelPoints];
		gpuTemps = new byte[PanelPoints];
		gpuDuties = new byte[PanelPoints];
		sourcePath = "";
		detail = "";
		for (int i = 0; i < PanelPoints; i++)
		{
			cpuTemps[i] = byte.MaxValue;
			cpuDuties[i] = 0;
			gpuTemps[i] = byte.MaxValue;
			gpuDuties[i] = 0;
		}
		string tableName = TableNameFor(modeId);
		string root = ResolveDataRoot();
		if (string.IsNullOrEmpty(root))
		{
			detail = "找不到 GCU 的 MyControlCenter 目录（UserFanTables 不在预期位置）";
			return false;
		}
		string path = Path.Combine(root, "UserFanTables", tableName + ".json");
		sourcePath = path;
		if (!File.Exists(path))
		{
			detail = "官方风扇表不存在：" + path;
			return false;
		}
		try
		{
			JObject rootObject = JObject.Parse(File.ReadAllText(path));
			if (!TryReadChannel(rootObject, "CPU", out byte[] cpuT, out byte[] cpuD, out string cpuDetail))
			{
				detail = "CPU 曲线无效：" + cpuDetail;
				return false;
			}
			if (!TryReadChannel(rootObject, "GPU", out byte[] gpuT, out byte[] gpuD, out string gpuDetail) || gpuT.Length == 0)
			{
				// 无 GPU 曲线时用 CPU 曲线（无界是 APU 双扇，官方表可能只给 CPU）
				gpuT = (byte[])cpuT.Clone();
				gpuD = (byte[])cpuD.Clone();
			}
			Array.Copy(cpuT, cpuTemps, Math.Min(cpuT.Length, PanelPoints));
			Array.Copy(cpuD, cpuDuties, Math.Min(cpuD.Length, PanelPoints));
			Array.Copy(gpuT, gpuTemps, Math.Min(gpuT.Length, PanelPoints));
			Array.Copy(gpuD, gpuDuties, Math.Min(gpuD.Length, PanelPoints));
			detail = "已读取 " + tableName + "（" + cpuT.Length + " 点）";
			return true;
		}
		catch (Exception ex)
		{
			detail = "官方风扇表解析失败：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	private static string TableNameFor(string modeId)
	{
		string text = (modeId ?? "").Trim().ToLowerInvariant();
		if (text == "balanced" || text == "gaming")
		{
			return "DefaultFanTable_Gaming";
		}
		if (text == "turbo" || text == "performance")
		{
			return "DefaultFanTable_Turbo";
		}
		return "DefaultFanTable_Office";
	}

	private static string ResolveDataRoot()
	{
		try
		{
			string root = VendorModeDefaults.DataRoot();
			if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
			{
				return root;
			}
		}
		catch
		{
		}
		string[] candidates = new string[]
		{
			Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "MyControlCenter"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "L-Mechrevo", "GCU", "AiStoneService", "MyControlCenter")
		};
		foreach (string candidate in candidates)
		{
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}
		return "";
	}

	private static bool TryReadChannel(JObject root, string channel, out byte[] temps, out byte[] duties, out string detail)
	{
		temps = Array.Empty<byte>();
		duties = Array.Empty<byte>();
		detail = "";
		if (root[channel] is not JArray array || array.Count < 2)
		{
			detail = channel + " 数组缺失或不足 2 点";
			return false;
		}
		List<byte> tempList = new List<byte>();
		List<byte> dutyList = new List<byte>();
		int previousTemp = -1;
		int skipped = 0;
		foreach (JToken item in array)
		{
			int upT = item.Value<int?>("UpT") ?? -1;
			int duty = item.Value<int?>("Duty") ?? -1;
			if (upT < 0 || upT > 254 || duty < 0 || duty > 100)
			{
				detail = "点数值越界（UpT=" + upT + " Duty=" + duty + "）";
				return false;
			}
			// 2026-10-22：官方表里 UpT=0 / 个位数温度（低温停转占位点）不显示，避免曲线出现奇怪温度点。
			if (upT < MinimumTemperatureC)
			{
				skipped++;
				continue;
			}
			if (upT < previousTemp)
			{
				detail = "温度点不是递增（UpT=" + upT + " 前一个=" + previousTemp + "）";
				return false;
			}
			previousTemp = upT;
			tempList.Add((byte)upT);
			dutyList.Add((byte)duty);
		}
		if (tempList.Count < 2)
		{
			detail = channel + " 过滤低温点后不足 2 点";
			return false;
		}
		if (skipped > 0)
		{
			detail = "（已跳过 " + skipped + " 个 <" + MinimumTemperatureC + "℃ 的点）";
		}
		temps = tempList.ToArray();
		duties = dutyList.ToArray();
		return true;
	}
}
