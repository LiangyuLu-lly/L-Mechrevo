# -*- coding: utf-8 -*-
# 追加充电上限分流恢复记录到修改日志
import io

log = r'D:\openLM\修改日志.txt'
entry = '''

==== 2026-10-04 充电上限恢复机型分流（50 系不再直接三档） ====

- 反馈：50 系机主（耀世 ARL / RTX 5070 Ti）打开界面直接是三档模式，而非"限充失败回三档"。
- 根因：此前为"无极调节（滑块）在部分机器未生效"将 UI 改为 if(true) 无条件三档，覆盖了 50 系。
- 修复（SettingsForm.cs）：
  1. 恢复分流：if (!IsRtx50SeriesGpu()) → 40 系三档；50 系走滑块（无极）路径。
  2. 50 系滑块写入成功后 ChargeLimitMonitor.Arm(limit) 启动观察（每 10s 判定）。
  3. 观察判定 Ineffective（不受控）时：Toast 提示 + 动态把滑块 UI 切换为三档模式
     （新增 BuildChargeLimitTieredMode(host) 方法，供构建与回退复用；移除滑块、隐藏百分比值、建三档按钮）。
  4. VerdictChanged 订阅移出三档分支，50 系滑块路径也监听判定。
- 效果：50 系先显示滑块并观察实际充电行为，仅不受控时回退三档（长效 100%/平衡 80%/工作站 60%）。
- 诊断包佐证：该机 config.json 中 dgpu_generation=Gen50、ec_charge_limit_verdict=ineffective（已恢复满充 100%）。
'''

t = io.open(log, encoding='utf-8').read()
if '恢复机型分流' not in t:
    t = t.rstrip('\n') + '\n' + entry
    io.open(log, 'w', encoding='utf-8', newline='').write(t)
    print('APPENDED')
else:
    print('ALREADY PRESENT')

t2 = io.open(log, encoding='utf-8').read()
print('size now:', len(t2.encode('utf-8')))
