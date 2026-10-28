# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 02:30] 追问：底座 payload\\GCU(50系) 与本机官方 30 系 GCU 的重复文件数
- 对比脚本：tools\\diff_gcu_base_vs_official30.py（SHA256 逐字节校验）
- 结果：
  ```
  文件数              : 底座(A)=261   官方(B)=279
  同名文件            : 122
    ├ 逐字节【完全相同】: 20     （重复体积 11.49 MB）
    └ 同名但【内容不同】: 102    （11.50 MB）
  只在底座 A 独有      : 139    （25.03 MB）
  只在官方 B 独有      : 157    （22.36 MB）
  ```
- **逐字节相同的 20 个里 10.5 MB 是跨机型通用组件**：
  EntityFramework.dll 4.98 / EntityFramework.xml 3.51 / System.Management.Automation.dll 1.54 /
  IntelOverclockingSDK.dll 0.70 / EntityFramework.SqlServer.dll 0.59（均"完全相同"）
- **"同名内容不同"的 102 个里 93 个是机型风扇表**（UserFanTables\\PH4A*/PH6A*），
  差异仅 **1-2 字节**（例 PH4ARxx\\M1T1.json A=3114/B=3116）
- **两条结论（其中一条推翻我先前的猜想）**：
  1. ❌ **不能靠"复用官方已装文件"瘦身** —— 底座与官方 30 系只有 **20 个**文件逐字节相同
     （底座是 50 系版本，绝大多数二进制不同）
  2. ✅ **风扇表不是体积问题** —— 底座 `UserFanTables` **167 个文件但仅 0.49 MB**
     （官方 0.24 MB）；25 个机型前缀 × 6 个（`(根)` 另有 23 个）-> 只影响文件数/整洁度
- 真正的省体积点仍是附录 B.3 三条：①三件套二选一 **~17 MB** ②删 xml **3.66 MB**
  ③按机型拆 50 系特有的大文件（oled_care.scr 12.35 / rtknrai.exe 2.48 /
  DisplayInfo.dll 2.43 / SQLite.Interop.dll 1.91）
- 已写入 30系GCU与LM自带GCU文件树差异.md 附录 B.6
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
