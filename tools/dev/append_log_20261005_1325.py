# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 13:25] iss 改造：卸载全删用户数据 / 升级保留（欢迎界面在"卸载后全新安装"时重现）
================================================================================
用户要求：「卸载全删 升级保留」—— 即卸载后全新安装会再次出现欢迎界面（FirstRunGuideForm），
          而覆盖升级不出现（onboarding_version 保留）。

【关键陷阱与设计】
Inno 升级时会**自动调用旧版卸载程序** -> 不能把 {userappdata}\\MechrevoLite 放进 [UninstallDelete]
（否则升级也会被清空，与需求相反）。采用「确定性判定 + 双保险」：
  ① 卸载时 `CurUninstallStepChanged(usPostUninstall)`：
       `if not UninstallSilent() then DelTree(ExpandConstant('{userappdata}\\MechrevoLite'), True, True, True);`
       用户手动卸载 = 非静默 -> 全删 ✅；升级触发的静默卸载 -> 保留 ✅
       （Inno 的 UninstallSilent 语义已查证文档：静默模式返回 True）
  ② 全新安装时（[Run] 首条 + `Check: IsFreshInstallFlag`）：
       用 `FileExists(ExpandConstant('{app}\\L-Mechrevo.exe'))` 判定 ——
       InitializeSetup 在任何文件复制之前运行，所以"旧版 exe 还在"就等价于"正在升级"。
       不依赖 AppId，两个 iss 通用；作为 ① 的安全网（即使 ① 判定失误也能纠正）
  ③ `GFreshInstall` 在 **InitializeSetup** 里求值并存入全局，[Run] 的 Check 只读它 ——
       避免在 [Run] 阶段读取时卸载注册项已被写入导致的误判

【改动（tools\\patch_iss_uninstall_userdata.py + tools\\patch_iss_main_userdata.py）】
两个文件都已改：installer-src\\install_script.iss、installer-src\\install_script_nogcu.iss
  · [Run] 段首新增一条 runhidden 的 PowerShell：
      Remove-Item -LiteralPath (Join-Path $env:APPDATA 'MechrevoLite') -Recurse -Force
      （Check: IsFreshInstallFlag —— 升级时不执行）
  · [Code] 新增 `var GFreshInstall: Boolean;`、`IsFreshInstallFlag()`、
      `CurUninstallStepChanged()`（含 UninstallSilent + DelTree）
  · 两个 InitializeSetup 的 begin 之后插入 `GFreshInstall := not FileExists(...)`
    ⚠️ 主 iss 的 InitializeSetup 带 `var OfficialUninst: String;`
       -> 第一版脚本的正则 `function...;\\nbegin\\n` 匹配失败（未写盘，原子设计生效 ✅）
       -> 定向补丁 tools\\patch_iss_main_userdata.py 用完整锚点重打成功 ✅

【验证】
- 两个 iss 均已含 6 处标志改动（[Run] 条目 + GFreshInstall 声明/赋值 + IsFreshInstallFlag
  + CurUninstallStepChanged + UninstallSilent + DelTree）✅
- **复核：[UninstallDelete] 段内均无 userappdata / MechrevoLite** ✅
  -> 升级不会被清空 ✅（这是本需求的关键约束）
- 那条 PowerShell 表达式已安全验证（只打印路径）：
  `Join-Path $env:APPDATA 'MechrevoLite'` -> `C:\\Users\\<用户>\\AppData\\Roaming\\MechrevoLite` ✅
  与 LM 实际使用的目录一致 ✅
- 备份：backup\\install_script.iss_20261005_pre_userdata.iss、
        backup\\install_script_nogcu.iss_20261005_pre_userdata.iss
- ⚠️ **未编译 iss、未打包**（遵守用户"不要打包"）—— Inno 的 Pascal 语法错误只会在编译时暴露，
  下次打包时请留意编译输出；如报语法错，改动位置见上表（均在 [Code] 与 [Run] 段）

【行为预期】
| 场景 | 用户数据 %APPDATA%\\MechrevoLite | 欢迎界面 |
|---|---|---|
| 覆盖升级（旧版在） | 保留 ✅ | 不出现 ✅ |
| 卸载（手动） | **整目录删除** ✅ | — |
| 卸载后全新安装 | 残留被清 ✅ | **重新出现** ✅ |
| 首次安装（全新机器） | 无（无害）✅ | 出现 ✅ |
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
