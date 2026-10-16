# -*- coding: utf-8 -*-
# 1) OnGpuDeviceStatus 加日志（区分 GCU 没推消息 vs 推了但无 currentHZList）
# 2) 诊断包 [液冷 / GCU] 段写入 GCU 服务版本（GCUService.exe 文件版本）
import io

def edit(path, old, new, tag, enc='utf-8'):
    t = io.open(path, encoding=enc).read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding=enc, newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# ---------- 1) OnGpuDeviceStatus 日志 ----------
p1 = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
old1 = '''	private void OnGpuDeviceStatus(JObject o)
	{
		GpuDeviceStatusSeen = true;
		if (o["currentHZList"] is JArray source)
		{
			_hzList = (from value in (from h in source
					select int.TryParse(h?.ToString(), out var result) ? result : 0 into value
					where value > 0
					select value).Distinct()
				orderby value descending
				select value).ToArray();
		}
		CurrentHz = Int(o, "currentHZ");
		GpuSaveMode = Int(o, "currentSaveingMode");
		bool? flag = OptionalBool(o, "DC_HZ");
		if (flag.HasValue)
		{
			DcHz = flag.Value;
			DcHzSeen = true;
		}
	}'''
new1 = '''	private void OnGpuDeviceStatus(JObject o)
	{
		GpuDeviceStatusSeen = true;
		if (o["currentHZList"] is JArray source)
		{
			_hzList = (from value in (from h in source
					select int.TryParse(h?.ToString(), out var result) ? result : 0 into value
					where value > 0
					select value).Distinct()
				orderby value descending
				select value).ToArray();
		}
		CurrentHz = Int(o, "currentHZ");
		GpuSaveMode = Int(o, "currentSaveingMode");
		bool? flag = OptionalBool(o, "DC_HZ");
		if (flag.HasValue)
		{
			DcHz = flag.Value;
			DcHzSeen = true;
		}
		Logger.WriteLine($"GPUDevice/Status parsed: hasHZList={o["currentHZList"] != null} hzList=[{string.Join(",", _hzList)}] currentHz={CurrentHz} gpuSaveMode={GpuSaveMode} dcHzSeen={DcHzSeen} dcHz={(DcHzSeen ? DcHz.ToString() : "?")}");
	}'''
edit(p1, old1, new1, 'gpu-status-log')

# ---------- 2) 诊断包 GCU 版本 ----------
p2 = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'
old2 = '''	private static void AppendLiquidCooling(StringBuilder sb)
	{
		sb.AppendLine("[液冷 / GCU]");
		MechrevoHw hw = Program.hw;
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
		handler.AppendLiteral("GCU 已连接: ");
		handler.AppendFormatted(YesNo(hw?.IsConnected ?? false));
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("固件版本: ");
		handler.AppendFormatted(Or(hw?.LcFwVersion));
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("已发现设备数: ");
		handler.AppendFormatted(hw?.LcDeviceMacs.Count ?? 0);
		stringBuilder4.AppendLine(ref handler);
		sb.AppendLine();
	}'''
new2 = '''	private static void AppendLiquidCooling(StringBuilder sb)
	{
		sb.AppendLine("[液冷 / GCU]");
		MechrevoHw hw = Program.hw;
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
		handler.AppendLiteral("GCU 已连接: ");
		handler.AppendFormatted(YesNo(hw?.IsConnected ?? false));
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("GCU 版本: ");
		handler.AppendFormatted(ReadGcuVersion());
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
		handler.AppendLiteral("固件版本: ");
		handler.AppendFormatted(Or(hw?.LcFwVersion));
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("已发现设备数: ");
		handler.AppendFormatted(hw?.LcDeviceMacs.Count ?? 0);
		stringBuilder5.AppendLine(ref handler);
		sb.AppendLine();
	}

	/// <summary>从与 LM 同目录的 GCU 服务读取版本（优先 GCUService.exe，其次 GCUBridge.exe）。</summary>
	private static string ReadGcuVersion()
	{
		try
		{
			string[] candidates = new string[2]
			{
				Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "MyControlCenter", "GCUService.exe"),
				Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "GCUBridge.exe")
			};
			foreach (string path in candidates)
			{
				if (File.Exists(path))
				{
					System.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
					string text = versionInfo.ProductVersion ?? versionInfo.FileVersion;
					if (!string.IsNullOrWhiteSpace(text))
					{
						return text + " (" + Path.GetFileName(path) + ")";
					}
					return "(无版本信息) " + Path.GetFileName(path);
				}
			}
			return "未知（未找到 GCU 服务文件）";
		}
		catch (Exception ex)
		{
			return "读取失败: " + ex.Message;
		}
	}'''
edit(p2, old2, new2, 'gcu-version', enc='utf-8')
print('ALL OK')
