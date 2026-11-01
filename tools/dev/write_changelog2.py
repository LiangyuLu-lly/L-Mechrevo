# -*- coding: utf-8 -*-
# 追加 10/04 上午校色档位/开关分离 + 清理优化记录到修改日志
import io

log = r'D:\openLM\修改日志.txt'
entry = '''

==== 2026-10-04 屏幕校色自定义 ICC 追加修复（版本待定：0.290.3 beta21.3） ====

1. 校色档位与开关状态分离（关键修复）：
   - 根因：关闭总开关 SetColorCalibration(0) 成功后把 calib_mode 覆盖为 0，导致重启后
     下拉回显"默认"，档位记忆丢失（40 系 GCU 无回读，完全依赖 LM 自身记忆）。
   - 修复：calib_mode 恒存"用户档位选择"（999=自定义 / 1-4=官方档），关闭总开关只写
     新增 calib_enabled（0/1）开关状态，不再覆盖 calib_mode；
     下拉切档隐含置 calib_enabled=1；开关初始化优先读 calib_enabled，无键时回退 calib_mode>0。
   - 效果：自定义档关闭总开关 → 重启后开关保持关闭、下拉仍回显"自定义"；再打开即恢复应用。

2. 重新打开自定义档走 ApplyIcc（不再经 GCU）：
   - 根因：恢复 saved=999 时走了 SetColorCalibration(999)（GCU 无效模式）→ 失败 → 开关回弹无法打开。
   - 修复：开关打开且档位==999 时直接走 IccHelper.ApplyIcc（不经 GCU），与下拉 999 分支一致。

3. ClearAllIcc 去遍历 + 不误伤外屏：
   - 文件清理不再遍历 customICC 目录：只清理 AppConfig calib_custom_icc 记录的当前应用文件
     （删 RegisteredProfiles 值/子键 + 用户颜色目录文件），性能与语义更干净。
   - 关联槽不再 DeleteSubKeyTree（会误伤外屏/其他显示器）：改为按当前应用文件名精确匹配
     删除对应槽的 ICMProfile（遍历仅 8 个槽的轻量检查）。

4. 关闭校色不连带关闭自定义开关：清理 ICC 时不再写 calib_custom_on=0，开关独立记忆。
'''

t = io.open(log, encoding='utf-8').read()
if '2026-10-04 屏幕校色自定义 ICC 追加修复' not in t:
    t = t.rstrip('\n') + '\n' + entry
    io.open(log, 'w', encoding='utf-8', newline='').write(t)
    print('APPENDED')
else:
    print('ALREADY PRESENT')

t2 = io.open(log, encoding='utf-8').read()
print('size now:', len(t2.encode('utf-8')))
print('tail has 不误伤外屏:', '不误伤外屏' in t2)
