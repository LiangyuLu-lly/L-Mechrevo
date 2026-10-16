# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
t = open(p, encoding='utf-8').read()

pairs = [
    ('		Logger.WriteLineThrottled("cpu-telemetry", $"CPU telemetry: temp={CpuTemp} freq={CpuFrequency} usage={CpuUsage}", 5000);\n', ''),
    ('		Logger.WriteLineThrottled("gpu-telemetry", $"GPU telemetry: temp={GpuTemp} freq={GpuCoreFreq} usage={GpuUsage} vram={VramUsedMb}", 5000);\n', ''),
    ('		Logger.WriteLineThrottled("fan-telemetry", $"Fan telemetry: cpu={CpuFanDuty}/{CpuFanRpm} gpu={GpuFanDuty}/{GpuFanRpm}", 5000);\n', ''),
]

for old, new in pairs:
    n = t.count(old)
    print('match:', n, repr(old[:60]))
    if n != 1:
        print('匹配数异常，中止')
        raise SystemExit(1)
    t = t.replace(old, new)

# 校验无残留
for k in ['"cpu-telemetry"', '"gpu-telemetry"', '"fan-telemetry"', 'CPU telemetry:', 'GPU telemetry:', 'Fan telemetry:']:
    print('残留', k, ':', t.count(k))
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK')
