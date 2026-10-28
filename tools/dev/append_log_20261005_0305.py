# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 03:05] 用户反馈「MyControlCenter 实测不能删」-> 验证 T1 是否仍成立（结论：成立）
- 用户原话：「MyControlCenter实测不能删」
- **需要区分两种删除**（这是关键）：
  | 操作 | 会不会坏 | 原因 |
  |---|---|---|
  | 删**整个** MyControlCenter\\ | **会坏** ✅ 用户说得对 | GCUService.exe / GCUServicePlugin.dll /
    EntityFramework* 等**只在这里**，根目录没有 |
  | 删 **T1 那 20 个**（与底座逐字节相同的） | **不会坏** | **底座也有一份且逐字节相同** |
- **依据：读 `Install-Gcu.ps1` :930-962 实测**：
  ```
  [3/8] copying base payload\\GCU + {0} trio
    先备份 UserPofiles / UserFanTables（跨升级保留用户数据）
    Copy-Tree $baseServiceSource -> $serviceTarget    # 底座先铺
    Copy-Tree $trioServiceSource -> $serviceTarget    # 机型补丁后覆盖
    Copy-Tree $driverSource      -> $driverTarget
    恢复 UserPofiles / UserFanTables
  ```
  * `Copy-Tree`（:66）= `Copy-Item -LiteralPath (Join-Path $Source '*') -Destination $Dest -Recurse -Force`
    -> **合并覆盖，不是替换整个目录** ✅
  * **没有任何 `Remove-Item` 清空目标目录** ✅
    （:479 删的是 allow-list 里的**官方控制台目录**；:960 删的是临时备份目录）
  => **补丁里缺失的文件 = 保留底座版本** ⇒ **T1 成立** ✅
  => 附带确认：`UserPofiles`/`UserFanTables` 被备份+恢复，**升级不丢用户数据** ✅
- 已写入 30系GCU与LM自带GCU文件树差异.md **附录 C.3**（安装顺序验证）
- ⏭ 仍待用户决定是否执行 `py -3 tools\\trim_30_payload.py --tiers=T1,T2 --apply`（44.17 -> 26.36 MB）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
