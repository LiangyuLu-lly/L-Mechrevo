# -*- coding: utf-8 -*-
"""修复更新日志编码：原文(GBK) + 追加文本(GBK)，保持文件原编码"""
p = r'D:\openLM\更新日志.txt'
data = open(p, 'rb').read()

# 我的追加文本（与 write_logs_hotswap.py 中 upd 完全一致）
upd = '''【2026-10-03】〇、热切换执行层删除
- 与核显省电功能重复的免重启热切换（50 系点集显即时切换）执行逻辑已整体删除
- 混合→集显统一走"重启后应用"（50 系热切换机型）/ 即时（40 系三模，分流不变）
- 显卡切换高亮/布局体系（GpuRowLayout=HotSwap、路由回读高亮、重启确认闭环）不受影响
- 核显省电（混合模式静默独显，IGPU_ONLY_CONNECT_RB_* 独立路径）不受影响
- 代码层面：删除约 6 个方法/分支 + 2 个参数签名 + 2 个无引用文件，热切换确认/回滚死代码全清

'''
marker = '【2026-10-03】〇、热切换执行层删除'.encode('utf-8')
idx = data.find(marker)
assert idx > 0, '追加起点未找到'
head = data[:idx]

# 原文解码尝试
for enc in ('gbk', 'utf-8'):
    try:
        head.decode(enc)
        print('原文编码:', enc)
        break
    except UnicodeDecodeError:
        continue

# 统一用 gbk 写回（原文字节原样保留 + 追加转 gbk）
out = head + upd.encode('gbk')
open(p, 'wb').write(out)

# 验证
t = open(p, encoding='gbk').read()
print('gbk 读回 OK:', '热切换执行层删除' in t)
