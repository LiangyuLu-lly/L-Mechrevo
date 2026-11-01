# -*- coding: utf-8 -*-
"""修复：更新日志当前 = head(历史混编码) + gbk(upd)，把 gbk(upd) 换回 utf-8(upd)"""
p = r'D:\openLM\更新日志.txt'
data = open(p, 'rb').read()

upd = '''【2026-10-03】〇、热切换执行层删除
- 与核显省电功能重复的免重启热切换（50 系点集显即时切换）执行逻辑已整体删除
- 混合→集显统一走"重启后应用"（50 系热切换机型）/ 即时（40 系三模，分流不变）
- 显卡切换高亮/布局体系（GpuRowLayout=HotSwap、路由回读高亮、重启确认闭环）不受影响
- 核显省电（混合模式静默独显，IGPU_ONLY_CONNECT_RB_* 独立路径）不受影响
- 代码层面：删除约 6 个方法/分支 + 2 个参数签名 + 2 个无引用文件，热切换确认/回滚死代码全清

'''
gbk_bytes = upd.encode('gbk')
idx = data.find(gbk_bytes)
assert idx > 0, 'gbk 追加起点未找到'
head = data[:idx]
# 把 gbk 追加替换为 utf-8 追加
out = head + upd.encode('utf-8')
open(p, 'wb').write(out)

# 验证：utf-8 读取应能找到条目（head 混编码用 replace 容错）
t = open(p, encoding='utf-8', errors='replace').read()
print('utf-8 replace 读回含条目:', '热切换执行层删除' in t)
print('文件大小:', len(out))
