# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
t = open(p, encoding='utf-8').read()

# OnSystemCpuInfo 加节流日志
old1 = '''	private void OnSystemCpuInfo(JObject o)
	{
		CpuTemp = Int(o, "CpuTemperature");
		CpuUsage = Int(o, "CpuUsage");
		CpuFrequency = Int(o, "CpuFrequency");
		Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
	}'''
new1 = '''	private void OnSystemCpuInfo(JObject o)
	{
		CpuTemp = Int(o, "CpuTemperature");
		CpuUsage = Int(o, "CpuUsage");
		CpuFrequency = Int(o, "CpuFrequency");
		Interlocked.Exchange(ref _cpuInfoReceivedAt, Environment.TickCount64);
		Logger.WriteLineThrottled("gpu-telemetry", $"CPU telemetry: temp={CpuTemp} freq={CpuFrequency} usage={CpuUsage}", 5000);
	}'''

# OnSystemGpuInfo 加节流日志
old2 = '''	private void OnSystemGpuInfo(JObject o)
	{
		GpuTemp = Int(o, "GpuTemperature");
		GpuUsage = Int(o, "GpuUsage");
		GpuCoreFreq = Int(o, "GpuCoreFreq");
		VramUsedMb = Int(o, "GpuMem");
		Interlocked.Exchange(ref _gpuInfoReceivedAt, Environment.TickCount64);
	}'''
new2 = '''	private void OnSystemGpuInfo(JObject o)
	{
		GpuTemp = Int(o, "GpuTemperature");
		GpuUsage = Int(o, "GpuUsage");
		GpuCoreFreq = Int(o, "GpuCoreFreq");
		VramUsedMb = Int(o, "GpuMem");
		Interlocked.Exchange(ref _gpuInfoReceivedAt, Environment.TickCount64);
		Logger.WriteLineThrottled("gpu-telemetry", $"GPU telemetry: temp={GpuTemp} freq={GpuCoreFreq} usage={GpuUsage} vram={VramUsedMb}", 5000);
	}'''

# OnSystemFanInfo 加节流日志
old3 = '''	private void OnSystemFanInfo(JObject o)
	{
		Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
		CpuFanDuty = Int(o, "CpuFanDuty");
		GpuFanDuty = Int(o, "GpuFanDuty");
		CpuFanRpm = Int(o, "CpuFanRpm");
		GpuFanRpm = Int(o, "GpuFanRpm");
	}'''
new3 = '''	private void OnSystemFanInfo(JObject o)
	{
		Interlocked.Exchange(ref _fanInfoReceivedAt, Environment.TickCount64);
		CpuFanDuty = Int(o, "CpuFanDuty");
		GpuFanDuty = Int(o, "GpuFanDuty");
		CpuFanRpm = Int(o, "CpuFanRpm");
		GpuFanRpm = Int(o, "GpuFanRpm");
		Logger.WriteLineThrottled("gpu-telemetry", $"Fan telemetry: cpu={CpuFanDuty}/{CpuFanRpm} gpu={GpuFanDuty}/{GpuFanRpm}", 5000);
	}'''

print('old1:', t.count(old1), 'old2:', t.count(old2), 'old3:', t.count(old3))
if t.count(old1) != 1 or t.count(old2) != 1 or t.count(old3) != 1:
    print('匹配数异常，中止')
    raise SystemExit(1)
t = t.replace(old1, new1).replace(old2, new2).replace(old3, new3)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK')
