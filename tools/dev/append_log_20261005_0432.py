# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 04:32] 修英文模式配置下拉框被裁（"Conf..."）
- 用户报：「英文模式下config 1被裁 有没有缩写」
- **根因**：`SettingsForm.cs` BuildPerfConfigRow 里下拉框固定 `Width = D(72)`，
  而 `PerfConfigComboText` 产出 `Strings.ModeConfigShort + " " + N`；
  en 资源 `ModeConfigShort = "Config"` -> `Config 1` 连箭头约 78px，**72 装不下** -> `Conf...` ✅
  中文 `配置 1` 宽度小，本来就够 ✅
- **为何不能直接改 ModeConfigShort**：它用在 4 处 ——
  `SettingsForm:5214` 下拉框（窄，需短）、`SettingsForm:3927` 编辑按钮（`Config Edit`）、
  `CustomModeForm:1579` 编辑器标题、`CustomModeForm:1587` 编辑器提示；
  直接改成 `Cfg` 会让按钮变 `Cfg Edit` 很怪 ✅
- **做法**（tools\\patch_config_combo_abbrev.py，4 项锚点，含行尾自动适配）：
  新增**窄用短标签** `ModeConfigTiny`：
    en resx     = `Cfg`
    zh-CN resx  = `配置`（中文此宽度足够，与 Short 同值）
    Strings.cs  新增访问器
    `PerfConfigComboText` 改用它（**仅下拉框**）
  -> 按钮/标题仍是 `Config`（`Config Edit` 不变）✅
- 编译 0 错误；提权替换完成（L-Mechrevo.dll 1935360B @02:28:08；LM PID 20004，
  `Responding=True`）
- **部署后核验**：主 DLL 内含 `ModeConfigTiny`（ascii 命中）与 `Cfg`（出现 2 次）；
  中文卫星 `zh-CN\\L-Mechrevo.resources.dll` 已同步更新 @02:28:08 ✅
  （主 DLL 大小三版都是 1935360 = 512 字节对齐所致，**不能靠大小判断是否更新** ⚠️
   要看 LastWriteTime + 内容核验 ✅）
- 备份：backup\\Strings_20261005_pre_tiny.resx / Strings.zh-CN_20261005_pre_tiny.resx /
        Strings.cs_20261005_pre_tiny.cs
- 待实测：英文界面下三个配置下拉框应显示 `Cfg 1`（不再 `Conf...`）；中文仍 `配置 1`
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
