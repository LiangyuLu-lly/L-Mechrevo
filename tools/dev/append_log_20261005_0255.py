# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 02:55] 30 系 payload 精简分析（用户把完整官方 279 个复制进来了）
- 用户：「我现在把完整的30系GCU复制到 D:\\openLM\\installer-src\\files\\GCU\\payload\\30\\AiStoneService 了，
  你看看有没有什么可以删的」
- 实测：新 30 补丁 = **279 个 / 44.17 MB**，与官方已装 **279 个全部逐字节相同** ✅
  （即用户复制的是完整官方 30 系 GCU）
- 工具：**tools\\trim_30_payload.py**（默认 dry-run；`--apply` 才执行且**移到备份**；
  `--tiers=T1,T2` 选层）—— 分析脚本 tools\\analyze_30_payload_deletable.py
- **三层可删清单**：
  | 层 | 判据 | 个数 | 可省 | 风险 |
  |---|---|---|---|---|
  | T1 | **与底座逐字节相同**（底座总是先铺，已提供） | 20 | **11.49 MB** | ✅ 零 |
  | T2 | `.xml`(IntelliSense 文档) + `.pdb`(调试符号)，运行时永不加载 | 7 | **6.32 MB** | ✅ 零 |
  | T3 | GamingMonitor/ICCProfile/DisplayProfile/AppSettings/KeyboardManager/SharpDX/NAudio | 34 | 7.37 MB | ❌ **不可删** |
  （T1 与 T2 **无重叠**）
- ⚠️ **T3 验证结论：不能删** —— 扫 `GCUService.exe`(1728KB) 字符串实测：
  `GamingMonitor`(ascii+unicode) / `SharpDX`(ascii，3 次) / `NAudio`(ascii，3 次) /
  `ICCProfile` / `DisplayProfile` / `KeyboardManager` / `AppSettings` / `SQLite`×3 均有引用
  -> 删了可能在某代码路径抛 FileNotFoundException ✅ 放弃
  （`GCUBridge.exe` 76KB 与 `GCUServicePlugin.dll` 57KB 干净）
- **结论**：安全精简 = **T1+T2 = 27 个 / 17.81 MB** -> `payload\\30` **44.17 MB → 26.36 MB** ✅
- **重要对照**：精简后 26.36 MB **仍大于原 117 文件版（18.73 MB）** ——
  因为原版**还额外缺了** GamingMonitor(6.61 MB) 等 T3 内容 ⚠️
  -> 即**原 117 文件版属"激进精简"（含 T3）、风险未评估**；
    建议改用 **T1+T2 精简版（279-27=252 个）**，兼顾体积与安全 ✅
- 已写入 30系GCU与LM自带GCU文件树差异.md **附录 C**
- ⏭ 待用户决定是否执行 `--tiers=T1,T2 --apply`
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
