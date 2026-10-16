# -*- coding: utf-8 -*-
p = r'D:\openLM\修改日志.txt'
entry = '''
【2026-10-03 托盘/悬浮 GPU 行消失修复（GCU 停报 System/GpuInfo 适配）】
- 问题：关闭主窗后托盘悬停提示的 GPU 行消失（主窗开着时也会间歇消失）；用户原话"主要是关闭主窗后 GPU 的信息就消失了"
- 排查：在 MechrevoHw 的 OnSystemCpuInfo/OnSystemGpuInfo/OnSystemFanInfo 加独立节流日志（cpu/gpu/fan-telemetry，避免共享 key 吞记录），实测约 2 分钟——CPU telemetry 每 ~6s 持续到达、Fan telemetry 每 ~6s 持续到达、GPU telemetry 在 GPU 空闲/下线待机时长时间停报（14:23:53→14:24:40 停了 47s 无一条，活跃时 usage=99 每 4-6s 一条）
- 结论：根因是 GCU 行为——GPU 空闲/下线待机时不推 System/GpuInfo（用户推测"混合模式 GPU 自动下线待机"正确）；与主窗关闭无关（Hide 仅 HideAll，不触发 System_OFF、不暂停 MQTT 订阅，System_ON/OFF 只在连接/退出时发送）
- 差异定位：主窗遥测行（UpdateTelemetryText→TelemetryParts→TelemetryTemp）早已是"过期显示—"不消失；只有托盘提示（RefreshSensors 6455/6477/6528）在 gpuTemp 新鲜度过期（IsSensorInfoFresh cpu:false 6s）后整行隐藏 GPU——两处行为不一致
- 修复：RefreshSensors GPU 温度赋值去掉新鲜度硬条件——只要 HardwareControl.gpuTemp>0（有过最后值）即保留 GPU 行，新鲜显示温度、过期显示"—"，与主窗 UpdateTelemetryText 对齐；风扇部分依赖 FanInfo 持续上报，仍显示真实值
- 诊断插桩（三处 telemetry 节流日志）验证通过后已移除，干净版本已部署
- 编译 0 错误；提权替换完成（DLL 14:30:39，LM pid 25884）
'''
with open(p, encoding='utf-8', newline='') as f:
    t = f.read()
t = t.rstrip('\n') + '\n' + entry
with open(p, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print('appended, tail check:')
print(t[-420:])
