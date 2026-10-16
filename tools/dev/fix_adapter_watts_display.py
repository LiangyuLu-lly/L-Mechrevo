# -*- coding: utf-8 -*-
# 适配器瓦数显示：编码 0x08(230W) 在部分机型实为 240W，统一显示 230/240W
def edit(path, old, new, tag):
    t = open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# 1) PowerInputDecoder 加 DisplayAdapterWatts
p1 = r'D:\openLM\source\MechrevoLite.Hardware\PowerInputDecoder.cs'
old1 = '''			return watts > 0;
		}
	}
}'''
new1 = '''			return watts > 0;
		}

		public static string DisplayAdapterWatts(int watts)
		{
			if (watts == 230)
			{
				return "230/240";
			}
			return watts.ToString();
		}
	}
}'''
edit(p1, old1, new1, 'display-helper')

# 2) SettingsForm 显示层
p2 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
old2 = '''				list.Add(adapterWatts.HasValue ? string.Format(Strings.PowerTipAdapter, adapterWatts.GetValueOrDefault()) : string.Format(Strings.PowerTipAdapterUnknown, power.BiosInfo3Raw & 0x78));'''
new2 = '''				list.Add(adapterWatts.HasValue ? string.Format(Strings.PowerTipAdapter, PowerInputDecoder.DisplayAdapterWatts(adapterWatts.GetValueOrDefault())) : string.Format(Strings.PowerTipAdapterUnknown, power.BiosInfo3Raw & 0x78));'''
edit(p2, old2, new2, 'settings-tip')

# 3) 诊断包 [供电] 段
p3 = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'
old3 = '''			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (adapterWatts.Value + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));'''
new3 = '''			int? adapterWatts = powerInputSample.AdapterWatts;
			string wattsText = (adapterWatts.HasValue ? (PowerInputDecoder.DisplayAdapterWatts(adapterWatts.Value) + "W") : ("未解码（编码 0x" + (powerInputSample.BiosInfo3Raw & 0x78).ToString("X2") + "）"));'''
edit(p3, old3, new3, 'diag-watts')

print('ALL OK')
