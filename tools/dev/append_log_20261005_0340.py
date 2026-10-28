# -*- coding: utf-8 -*-
"""追加日志 + 在适配方案文档里加一条重要发现"""
import io

ENTRY = """
[2026-10-05 03:40] 改 GCU 选择逻辑（用户要求）+ 发现本机其实是 40 系（重要）
--------------------------------------------------------------------------------
一、选择逻辑已按要求修改（installer-src\\files\\GCU\\Select-GcuPayload.ps1）
- 目标表（用户指定）：
    50 系 / 未知(无法识别) -> 50 系 GCU
    40 系                  -> 40 系 GCU
    30 系                  -> 30 系 GCU
    20-10 系（含 16 系）   -> 30 系 GCU（暂时）
- 原逻辑：`50->50 ; 30->30 ; 其它(40/unknown)->40`，且**无法区分**"10/16/20 系"与"真未知"
- 改法（tools\\patch_gcu_selection_table.py，8 项锚点，含行尾自动适配）：
  1. `$deviceClasses` 新增 'legacy'（Pascal/Turing 高位
     0x1B00/1C00/1D00/1E00/1F00/2000/2100）
  2. 名称匹配新增 legacy 规则 `\\b(GTX|RTX\\s*(16|20)\\d0|MX\\d{3})\\b`
  3. 代际遍历数组加入 'legacy'
  4. `$key` 改为显式分支：50->50, 40->40, 30->30, legacy->30, 其余(unknown)->50
  5. Reason 文案 / 头部文档 / fallback 注释 同步更新
  6. SelfTest 用例更新
- **验证**：
  * `Select-GcuPayload.ps1 -SelfTest` -> **10 个用例 10 PASS** ✅
  * 真机探测（tools\\probe_gcu_selection.ps1）：
    `Generation=40  Variant=40  StagedDir=payload\\40`
    `Reason: 40-series detected; using 40-series GCU service trio (mixed-GCU)`
    `Evidence: gpu-name 'NVIDIA GeForce RTX 4060 Laptop GPU' matches RTX 40[5-9]x`
- 备份：backup\\Select-GcuPayload_20261005_pre_legacy.ps1

二、⚠️ 重要发现：本机实际是 **40 系**，不是 30 系
- 实测独显：`NVIDIA GeForce RTX 4060 Laptop GPU`，`PCI\\VEN_10DE&DEV_28A0`（Ada / 40 系）
  iGPU：AMD Radeon 780M
- LM 配置（%APPDATA% 与 %ProgramData% 两份一致）：
  `dgpu_generation = Gen40`，`dgpu_generation_source = MarketingName` ✅ LM 检测**正确**
- 但注册表 `HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant = 30` ⚠️（GcuInstalledUtc 2026-10-04T14:22:49Z）
  -> 即本机是**「40 系机型 + 30 系 GCU」的混搭组合**
- 这与 payload\\40\\README 描述的场景吻合：**40 系机型用错 GCU 会导致狂暴模式 TGP 写不进去
  （满载 115W，官方 40 系可达 140W）**
- **这可能就是之前那些怪象的根源**：混合模式不自动重启、重复重启提示 ——
  不一定是"30 系不支持"，而是 GCU 与机型不匹配

三、⚠️ 由此暴露一个必须用户拍板的冲突
- `Install-Gcu.ps1:807-809`：`Set-ItemProperty ... -Name 'GcuVariant' -Value $Selection.Variant`
  -> 装 40 变体会写 `GcuVariant = "40"`
- LM 的「模式 × 配置」门控（MechrevoHw.cs:3274/3281）：
  `DgpuGeneration == Gen30 || GcuVariant == "30"`
  -> 本机 `DgpuGeneration=Gen40`；若 `GcuVariant` 变 "40" => **判据不再满足 -> 功能关闭** ⚠️
  | 方案 | GCU 匹配 | 模式×配置 |
  |---|---|---|
  | 现状 GcuVariant=30 | ❌ 不匹配 | ✅ 开启 |
  | 装 payload\\40 -> GcuVariant=40 | ✅ 匹配（或可修 TGP） | ❌ 关闭 |
- **待用户决定**：A 保持 30 变体 / B 装 40 变体 / C 装 40 变体 + 放宽 LM 门控（把 "40" 也纳入判据）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)

DOC = r'D:\openLM\30系性能模式适配方案.md'
NOTE = """

---

## ⚠️ 重大前提修正（2026-10-05）：本机实际是 **40 系**

实测证据：
```
独显    : NVIDIA GeForce RTX 4060 Laptop GPU   PCI\\VEN_10DE&DEV_28A0   (Ada / 40 系)
iGPU    : AMD Radeon 780M
LM 配置 : dgpu_generation = Gen40   (source = MarketingName)   <- LM 检测正确
注册表  : HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant = 30           <- 装的是 30 系 GCU
```

**即本机是「40 系机型 + 30 系 GCU」的混搭组合**，与 `payload\\40\\README` 描述的场景一致：
> 40 系双显三模机型使用 LM 自带的 50 系 GCU 时，狂暴模式 TGP 写不进去（满载 115W，
> 官方 40 系控制台可达 140W）

**对本项目的意义**：
1. 本文档此前把本机当"30 系"来做适配，实际是**靠 GCU 变体（GcuVariant=30）把功能判据打开**的
2. 之前观察到的"混合模式不自动重启""重复重启提示"等怪象，
   **可能源于 GCU 与机型不匹配**，而非"30 系不支持"
3. ⚠️ **冲突**：`Install-Gcu.ps1` 会把 `GcuVariant` 写成所选变体；
   若改装 `payload\\40`（对本机正确），`GcuVariant` 变 `"40"` ->
   LM 判据 `DgpuGeneration==Gen30 || GcuVariant=="30"` **不再满足** -> **「模式×配置」功能关闭**
   | 方案 | GCU 与机型匹配 | 模式×配置 |
   |---|---|---|
   | 保持 `GcuVariant=30` | ❌ | ✅ |
   | 装 `payload\\40` | ✅（或可修 TGP） | ❌ |
   | 装 `payload\\40` + 放宽 LM 判据（纳入 `"40"`） | ✅ | ✅（需改代码） |

GCU 选择逻辑已按用户要求改为：
`50/未知 -> 50`、`40 -> 40`、`30 -> 30`、`10/16/20 系 -> 30（暂时）`
（SelfTest 10/10 PASS；本机真机探测 -> `payload\\40`）
"""

with io.open(DOC, 'a', encoding='utf-8', newline='') as f:
    f.write(NOTE)

print('log appended:', len(ENTRY.encode('utf-8')), 'bytes')
print('doc appended:', len(NOTE.encode('utf-8')), 'bytes')
