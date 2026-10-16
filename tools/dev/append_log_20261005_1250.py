# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:50] 查清「首次启动提示」机制：onboarding_version（重装不再提示的原因）
================================================================================
用户观察：「LM貌似首次启动会有提示 不过我这么多次反复安装都没有再提示过了」

【机制（代码实证）】
```
SettingsForm.cs:4433  internal void ShowFirstRunGuideIfNeeded()
:4435   if (Program.UiAuditMode || IsDisposed || AppConfig.Get("onboarding_version", 0) >= 1)
            return;                                        // 看过即跳过
:4439   using FirstRunGuideForm firstRunGuideForm = new FirstRunGuideForm();   // 3 步引导
:4440   firstRunGuideForm.ShowDialog(this);
:4441   AppConfig.Set("onboarding_version", 1);            // 打标记
```
- 提示本体 = `FirstRunGuideForm`（Strings.FirstRun / FirstRunStep1..3 等）
- **门控键 = `onboarding_version`**（>= 1 即跳过），存在 `%APPDATA%\\MechrevoLite\\config.json`
- 另有独立标记 **`usage_notice_version`**（匿名使用统计/日志提示）
- `Program.UiAuditMode` 为真时**直接跳过**引导（截图/审计模式不弹）
- 调用点：`Program.cs:676`、`Program.cs:1849`

【为什么反复安装都不再提示（安装器实证）】
三个 iss 的 `[UninstallDelete]` 只删：
  `{commonappdata}\\BT_DFU_Tool`、`{commonappdata}\\L-Mechrevo\\logs`
-> **`%APPDATA%\\MechrevoLite`（用户 config.json 等）从不被安装/卸载触及** ✅
-> 所以 `onboarding_version` 永远是 1 -> **再也不会提示** ✅

【本机实测值】
  onboarding_version = 1     usage_notice_version = 1     start_count = 703

【实用：一键复位（想再看一次引导 / 做"真·干净安装"测试）】
```powershell
$cfg = "$env:APPDATA\\MechrevoLite\\config.json"
(Get-Content $cfg -Raw -Encoding UTF8) -replace '"onboarding_version":\\s*\\d+', '"onboarding_version": 0' | Set-Content $cfg -Encoding UTF8
# 然后重启 L-Mechrevo；想看匿名统计提示同理把 usage_notice_version 改 0
```
- 做**完全干净**的首次安装测试时，除卸载外还需手动清 `%APPDATA%\\MechrevoLite\\`
  （或至少复位上述两个键），否则永远测不到首次引导
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
