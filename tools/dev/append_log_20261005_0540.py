# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 05:40] 答疑：为什么装在 C:\\Program Files\\L-Mechrevo 还提示"目录允许非管理员创建或删除文件"
- 用户问：「怎么我装在这个位置了都提示」
- 判定代码：`Startup.ScheduleCharge()` -> `ExecutableTrust.IsCurrentImageInProtectedLocation()`
  -> `IsProtectedLocation()` -> `TryReadDirectoryRules()`（读**镜像所在目录**的 ACL，
  includeExplicit+includeInherited）-> `GrantsImageReplacementToNonAdministrators()`
  规则：Allow + 非 InheritOnly + 含(Write|DeleteSubdirectoriesAndFiles|Delete|ChangePermissions|
  TakeOwnership) + 身份不在 TrustedSids{Admins, SYSTEM, LocalService, NetworkService, TrustedInstaller}
  -> 命中则报"目录 X 允许非管理员创建或删除文件"
- **实测本机 ACL（现在）**：
  ```
  所有者 BUILTIN\\Administrators ；AreAccessRulesProtected=True（继承已阻断）；规则数=3
  SYSTEM         FullControl                    Allow  ← S-1-5-18 trusted ✅
  Administrators FullControl                    Allow  ← S-1-5-32-544 trusted ✅
  Users          ReadAndExecute, Synchronize    Allow  ← 只读+执行，不含写/删 ✅
  ```
  -> 复现判定（tools\\repro_trust_check.ps1）结果 **SAFE** ✅ -> **现在不会再提示** ✅
- **关键佐证**：这 3 条规则**恰好等于** `TryHardenImageDirectory()` 写入的三项
  （S-1-5-18 FullControl / S-1-5-32-544 FullControl / S-1-5-32-545 ReadAndExecute +
   ContainerInherit|ObjectInherit + 断开继承）
  -> **说明用户当时已经点了「是」**，LM 把目录权限收紧并复核通过 ✅
  -> 那个提示是**修复之前**的状态，属真实提示（当时目录 ACL 里确实有非管理员可写项，
     很可能因为目录不是由安装器创建，而是手动复制/解压过去的）
- 自查方法：`Get-Acl 'C:\\Program Files\\L-Mechrevo'` -> Users 必须只有 ReadAndExecute
- ⚠️ **交接文档待更新**：文档写"UAC 已关闭"，实测 `EnableLUA = 1`（UAC 是开的），
  且当前用户 xiaohan 不在管理员组（`IsInRole(Administrator)=False`）
  -> 与"UAC 关闭所以提权静默"的旧结论不符，需更正
- ⚠️ **另一个老坑复现**：自写 .ps1 含中文 -> Windows PowerShell 按 ANSI 读 -> 语法错误。
  已按交接文档改为**纯 ASCII** 脚本（tools\\repro_trust_check.ps1）
- 未打包（本次为答疑 + 核验，无代码改动）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
