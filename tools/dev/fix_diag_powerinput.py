# -*- coding: utf-8 -*-
# 诊断包系统信息加 [供电] 段：导出时现场采样 0x7CC/0x49F + 解码瓦数
def edit(path, old, new, tag):
    t = open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'

# 1) Build() 里追加调用
old1 = '''		AppendCapabilities(stringBuilder);
		return stringBuilder.ToString();'''
new1 = '''		AppendCapabilities(stringBuilder);
		AppendPowerInput(stringBuilder);
		return stringBuilder.ToString();'''
edit(p, old1, new1, 'build-call')

# 2) 追加 AppendPowerInput 方法（放在 AppendCapabilities 方法之后、YesNo 之前）
old2 = '''	private static string YesNo(bool value)'''
new2 = '''	private static void AppendPowerInput(StringBuilder sb)
	{
		sb.AppendLine("[供电]");
		MechrevoLite.HardwareControl.RefreshPowerInput(true);
		PowerInputSample? powerInputSample = MechrevoLite.HardwareControl.PowerInput;
		if (powerInputSample == null)
		{
			sb.AppendLine("供电采样未就绪。");
			sb.AppendLine();
			return;
		}
		if (!powerInputSample.DecodeEnabled)
		{
			sb.AppendLine("供电采样未启用（机型不支持 EC 供电解码）。");
			sb.AppendLine();
			return;
		}
		string text = powerInputSample.Kind switch
		{
			PowerInputKind.Battery => "电池供电",
			PowerInputKind.Barrel => "圆口供电",
			PowerInputKind.TypeC => "Type-C 供电",
			PowerInputKind.BarrelAndTypeC => "圆口 + Type-C",
			PowerInputKind.ExternalUnknown => "外部供电（方式未知）",
			_ => "未知"
		};
		sb.AppendLine("供电方式: " + text);
		if (powerInputSample.ComplexStatusRaw >= 0)
		{
			sb.AppendLine("复杂状态 0x7CC: 0x" + powerInputSample.ComplexStatusRaw.ToString("X2"));
		}
		else
		{
			sb.AppendLine("复杂状态 0x7CC: 读取失败");
		}
		if (powerInputSample.BiosInfo3Raw >= 0)
		{
			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (adapterWatts.Value + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));
			sb.AppendLine("适配器瓦数 0x49F: 0x" + powerInputSample.BiosInfo3Raw.ToString("X2") + " → " + wattsText);
		}
		else
		{
			sb.AppendLine("适配器瓦数 0x49F: 读取失败");
		}
		sb.AppendLine();
	}

	private static string YesNo(bool value)'''
edit(p, old2, new2, 'append-power-input')

print('ALL OK')
