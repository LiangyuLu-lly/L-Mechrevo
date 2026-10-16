# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
t = open(p, encoding='utf-8').read()

# 独立节流 key：CPU / GPU / Fan 分别记录，互不吞并
old1 = 'Logger.WriteLineThrottled("gpu-telemetry", $"CPU telemetry: temp={CpuTemp} freq={CpuFrequency} usage={CpuUsage}", 5000);'
new1 = 'Logger.WriteLineThrottled("cpu-telemetry", $"CPU telemetry: temp={CpuTemp} freq={CpuFrequency} usage={CpuUsage}", 5000);'
old2 = 'Logger.WriteLineThrottled("gpu-telemetry", $"GPU telemetry: temp={GpuTemp} freq={GpuCoreFreq} usage={GpuUsage} vram={VramUsedMb}", 5000);'
old3 = 'Logger.WriteLineThrottled("gpu-telemetry", $"Fan telemetry: cpu={CpuFanDuty}/{CpuFanRpm} gpu={GpuFanDuty}/{GpuFanRpm}", 5000);'
new3 = 'Logger.WriteLineThrottled("fan-telemetry", $"Fan telemetry: cpu={CpuFanDuty}/{CpuFanRpm} gpu={GpuFanDuty}/{GpuFanRpm}", 5000);'

print('cpu-old:', t.count(old1), 'gpu-old:', t.count(old2), 'fan-old:', t.count(old3))
if t.count(old1) != 1 or t.count(old2) != 1 or t.count(old3) != 1:
    print('匹配数异常，中止')
    raise SystemExit(1)
t = t.replace(old1, new1).replace(old2, old2).replace(old3, new3)
open(p, 'w', encoding='utf-8', newline='').write(t)
t2 = open(p, encoding='utf-8').read()
print('独立 key 校验: cpu-telemetry=', t2.count('"cpu-telemetry"'), 'gpu-telemetry=', t2.count('"gpu-telemetry"'), 'fan-telemetry=', t2.count('"fan-telemetry"'))
print('OK')
