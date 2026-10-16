# -*- coding: utf-8 -*-
# SettingsForm 与诊断包：230W 显示 230/240W
def edit(path, old, new, tag):
    t = open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# SettingsForm 显示层
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
old2 = '''				list.Add(adapterWatts.HasValue ? string.Format(Strings.PowerTipAdapter, adapterWatts.GetValueOrDefault()) : string.Format(Strings.PowerTipAdapterUnknown, power.BiosInfo3Raw & 0x78));'''
new2 = '''				list.Add(adapterWatts.HasValue ? string.Format(Strings.PowerTipAdapter, PowerInputDecoder.DisplayAdapterWatts(adapterWatts.GetValueOrDefault())) : string.Format(Strings.PowerTipAdapterUnknown, power.BiosInfo3Raw & 0x78));'''
edit(p2, old2, new2, 'settings-tip')

# 诊断包 [供电] 段
p3 = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'
old3 = '''			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (adapterWatts.Value + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));'''
new3 = '''			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (PowerInputDecoder.DisplayAdapterWatts(adapterWatts.Value) + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));'''
edit(p3, old3, new3, 'diag-watts')

print('ALL OK')
