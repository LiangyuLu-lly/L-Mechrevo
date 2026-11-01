# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 键盘灯效单分区判断修正（用户洞察：40 系高端也有单键 RGB，GCU 版本不能当单分区判断）】
- 现象：SetLightEffect（Keyboard/Ctrl）中 3161 行 if (isGcu40Keyboard || isSingleZoneKeyboard)——40 系 GCU（注册表 GcuVariant=40）一律强制单分区 payload，40 系高端单键 RGB 机型（KeyboardType 非 1/2）会被误判为单分区 → 灯效指令错误/不生效
- 依据（用户）：单分区/单键 RGB 是**硬件能力**（KeyboardType 上报），GCU 版本只决定**指令协议格式**
- 判断来源确认：
  - Capabilities.KeyboardType = GCU 上报 KeyboardType/RGBKeyboardType/KeyboardLightType（MechrevoDeviceCapabilities.cs:266,315）——硬件键盘类型
  - IsSingleZoneRgb = KeyboardType 1/2 → 单分区（KeyboardFirmwareEffects.cs:39-46）
  - IsGcu40Keyboard = 注册表 HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant=="40"（MechrevoService.cs:2897-2913）——纯 GCU 版本
- 修改（MechrevoService.cs:3161）：if (isGcu40Keyboard || isSingleZoneKeyboard) → if (isSingleZoneKeyboard)——单分区 payload 仅按硬件能力判断
- isGcu40Keyboard 分支（3136-3160：powerstatus=1、nv_save=null、direction=null、MonochromeIndex=33、ColorBlocks 7 段转换）保留——这是 40 系 GCU 指令协议格式，仅作协议分流
- 分流结果（修改后）：
  - 40 系单键 RGB（KeyboardType≠1/2）：40 系协议字段 + per-key payload ✓
  - 40 系单分区（KeyboardType=1/2）：40 系协议字段 + 单分区 payload ✓
  - 50 系单键 RGB：无 40 系字段 + per-key payload ✓
  - 50 系单分区：单分区 payload ✓
- 注意（遗留边界）：50 系单键 RGB 装 40 系 GCU 时 KeyboardType 若被报为 1/2 仍会走单分区——属 GCU 上报不准，不在本次范围
- UI 层（RgbForm/SettingsForm 的 IsSingleZoneRgb 使用）本就是按 KeyboardType 判断，无需改
- 编译 0 错误（1134 既有警告）；提权替换完成（主 DLL + zh-CN 资源均 @11:47:42，LM 已重启）

'''

upd = '''【2026-10-03】〇、键盘灯效单分区判断修正
- 单分区/单键 RGB 判断改为仅按硬件能力（GCU 上报 KeyboardType，1/2 为单分区），不再以 GCU 版本强制单分区
- GCU 版本（40/50）仅作指令协议格式分流；修复 40 系高端单键 RGB 机型灯效被误判为单分区的问题

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
