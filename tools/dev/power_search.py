import json, sys

d = json.load(open(r'D:\openLM\tools\power_settings_map.json', encoding='utf-8'))
lines = []

sleep = d.get('238c9fa8-0aad-41ed-83f4-97be242c8f20', {})
lines.append('== SLEEP GROUP (238c9fa8) settings ==')
for g, s in sleep.get('settings', {}).items():
    lines.append('%s | %s' % (g, s.get('friendly', '')))

lines.append('')
lines.append('== search: 系统所需/System Required ==')
for g, info in d.items():
    for s, si in info.get('settings', {}).items():
        fn = si.get('friendly', '')
        if '系统所需' in fn or 'System Required' in fn:
            lines.append('%s | %s | %s' % (g, s, fn))

lines.append('')
lines.append('== search: 休眠/hibernate/深度/Deep ==')
for g, info in d.items():
    for s, si in info.get('settings', {}).items():
        fn = si.get('friendly', '')
        low = fn.lower()
        if '休眠' in fn or 'hibernate' in low or '深度' in fn or 'deep' in low:
            lines.append('%s | %s | %s' % (g, s, fn))

lines.append('')
lines.append('== search: 网络/Networking ==')
for g, info in d.items():
    for s, si in info.get('settings', {}).items():
        fn = si.get('friendly', '')
        low = fn.lower()
        if '网络' in fn or 'networking' in low or 'network' in low:
            lines.append('%s | %s | %s' % (g, s, fn))

with open(r'D:\openLM\tools\power_search_out.txt', 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines))
print('written')
