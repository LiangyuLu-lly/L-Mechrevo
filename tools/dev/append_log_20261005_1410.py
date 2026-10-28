# -*- coding: utf-8 -*-
"""2026-10-05 14:10 在 D:\\openLM\\修改日志.txt 末尾追加「核显模式显示策略」①~⑥ 的实现记录。
文件为 UTF-8 无 BOM + LF，追加时不覆盖任何既有内容。"""
import io
import sys

LOG = r"D:\openLM\修改日志.txt"

TITLE = "[2026-10-05 14:10] 实现「核显模式显示策略」①~⑥（⑦读官方控制台配置仍不做）"
BAR = "=" * 80

BODY = """
方案存档：D:\\openLM\\核显模式显示策略-方案存档.md（§3 ①~⑦ 细化 / §6 待办清单）
一句话：40 系有的支持纯核显模式、有的不支持且无可靠判据 -> 改为「首次询问用户 + 设置里可随时
        开关」，替换当前的「代理规则」（代理规则仅保留为 ask 默认态）。

【新增配置键（机器作用域）】
| 键 | 取值 | 默认 | 含义 |
|---|---|---|---|
| igpu_only_ui | ask / show / hide | ask | ask=保持今天的代理规则（先显示、等用户回答）|
| igpu_only_asked | 0 / 1 | 0 | 是否已问过（升级用户单次引导的门控）|
- 两键均已加入 ModelScopedConfig.ExactMachineScopedKeys（string[3] -> string[5]）

【逐文件改动清单】
1) source\\MechrevoLite\\ModelScopedConfig.cs（①）
   ExactMachineScopedKeys: {charge_limit, gpu_mode, ec_charge_limit}
   -> {charge_limit, gpu_mode, ec_charge_limit, igpu_only_ui, igpu_only_asked}（+2 行注释说明）
2) source\\MechrevoLite.Hardware\\MechrevoHw.cs（② CanOfferIgpuOnly，现 :419-461）
   优先级：Gen30 -> false（**新增显式硬屏蔽**，防止被 show 越过；原屏蔽只在 CanOfferGpuModeSwitch 内）
           SupportsIgpuOnly（50 系）-> IsGpuActionAllowedByGeneration("DGPU_DIRECT_CONNECT_TOGGLE_IGPU")（原样保留）
           igpu_only_ui == hide -> false
           igpu_only_ui == show -> true（唯一附加条件 DgpuGeneration != NoDgpu，见「不确定项」a）
           其它（ask）-> CanOfferGpuModeSwitch && SupportsDgpuDirect（**与今天完全一致**）
3) source\\MechrevoLite\\SettingsForm.cs（③⑤）
   - 新增字段 _igpuOnlyUiPanel / _igpuOnlyUiSwitch / _syncingIgpuOnlyUi
   - 新增 BuildIgpuOnlyUiPanel()：样式对齐 BuildOfficialConsolePanel（BufferedPanel(CardStyle)
     + BufferedTableLayoutPanel + 左侧 Label(Strings.IgpuOnlyShowToggle，带 VideoCard 图标)
     + 右侧 RCheckBox 开关 + tooltip）
   - 新增 SyncIgpuOnlyUiSwitch()：开关显示「当前实际是否会显示核显模式按钮」
     （Program.hw?.CanOfferIgpuOnly；hw 为空时退化为 config != hide）
   - 新增 ApplyIgpuOnlyUiSwitch()：切换即写 igpu_only_ui=show/hide + igpu_only_asked=1 + Flush
     -> SyncIgpuOnlyUiSwitch()（30 系/50 系会回到实际值）-> RefreshDeviceCapabilities()
     -> SetContextMenu()（刷新 GPU 行 + 托盘）；_syncingIgpuOnlyUi 防递归
   - 构造函数在 BuildOfficialConsolePanel() 后调用 BuildIgpuOnlyUiPanel()
   - OpenSettingsDialog()：把 _igpuOnlyUiPanel 作为新增末位实参传给 SettingsDialog，显示前先 Sync
   - 新增 ShowIgpuOnlyGuideIfNeeded()（**独立于 ShowFirstRunGuideIfNeeded，后者未改**）：
     Program.UiAuditMode 直接返回；igpu_only_asked != 1 且判据不可靠时弹一次（50 系/Gen30 不弹），
     之后无条件 Sync + RefreshDeviceCapabilities + SetContextMenu
4) source\\MechrevoLite\\SettingsDialog.cs（③）
   - 构造函数新增末位可选参数 Control? igpuOnlyPanel = null
   - 在「官方控制台」行之后 AddRow 该行（Dock=Top、Visible=true）
   - （未改 UiAuditRunner 里那一处「合成 SettingsDialog」的构造，故截图审计不含本行）
5) source\\MechrevoLite\\FirstRunGuideForm.cs（④）
   - 新增 FirstRunGuideForm(bool igpuOnlyStepOnly)：true=只显示「核显模式」这一步，
     复用同一界面与同一落盘逻辑（给升级用户）
   - 首次引导加第 4 步（marker "?"；CreateStep 增加可选 extra 控件参数）：
     FirstRunStep4Title/Body + 三选一「有 / 没有 / 不确定」（UiVisualStyle.ApplySegmentGroup
     + RButton.Activated 分段单选，默认选中「不确定」）
   - FormClosed 统一落盘（点 X / Esc / 按钮都会走到）：
       有->show、没有->hide、不确定或未选->show（fail-safe），并一律 igpu_only_asked=1；
       Program.UiAuditMode 下不写配置（UiAuditRunner 会 new 本窗体）
   - 完整引导窗口 ClientSize 580x390 -> 580x500（步骤区本就 AutoScroll）
   - 顺带清掉文件头重复的 using（原 4 组重复 -> CS0105 消失）
6) source\\MechrevoLite\\Program.cs（⑤）
   :676 与 :1851 两处 ShowFirstRunGuideIfNeeded() 之后各追加 settingsForm.ShowIgpuOnlyGuideIfNeeded()
   （前者同一 if 内、后者同一 BeginInvoke 队列，顺序保证新用户走完首次引导后不会再被问一次）
7) 文案（⑥）
   - source\\MechrevoLite.Properties.Strings.resx（英文；UTF-8 **带 BOM** + LF，位置样式原样保持）
   - source\\MechrevoLite.Properties.Strings.zh-CN.resx（中文；同上）
   - source\\MechrevoLite.Properties\\Strings.cs（访问器，按 name 字母序插入）
   新增 9 键，三文件已校验同步（709 / 709 / 709，无重复、无缺漏）：
     FirstRunStep4Title、FirstRunStep4Body、FirstRunStep4Yes、FirstRunStep4No、FirstRunStep4Unknown、
     IgpuOnlyGuideTitle、IgpuOnlyGuideIntro、IgpuOnlyShowToggle、IgpuOnlyShowToggleTip
   插入脚本：tools\\add_igpu_ui_strings.py（可复查/复用；重复执行会报"already present"）

【问法（§3 ①：具体可查，不用术语）】
  正文：请在 BIOS 或官方控制台里找一下：有没有「集显模式 / 核显模式 / iGPU Only」这一档？
        选「有」会显示「核显模式」按钮，选「没有」会隐藏它；选「不确定」保持显示，
        之后可在设置里随时修改。

【编译结果】
& 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
-> **0 个错误** / 1186 个警告（改动前基线 1178）
   新增 8 条全部是 CS8632（"#nullable" 上下文外使用 ? 注解）——与全工程既有 1178 条同类，
   分别来自 FirstRunGuideForm.cs ×6、MechrevoHw.cs ×1、SettingsDialog.cs ×1
产物：source\\bin\\Release\\net10.0-windows10.0.19041.0\\L-Mechrevo.dll（1934848 字节 @14:03:08）
      + zh-CN\\L-Mechrevo.resources.dll；两个新键名已确认存在于主程序集与 zh-CN 附属程序集
完整编译输出存档：tools\\build_igpu_ui.txt

【未做 / 遵守的硬约束】
- ⑦（读官方控制台配置作佐证）**未实现**，文档 §3⑦ 记录未删
- 未碰：OperatingMode==3 等性能模式、核显省电链（_igpuSaving* / SwitchIgpuSavingAsync /
  SetIGpuOnlySaveMode / IgpuOnlySaveMode）、三档显卡模式（0/1/2）既有切换行为、
  ResolveGpuModeStatus、GpuRestartVerifier.PendingAlreadySatisfied、installer-src 下任何 iss
- **未部署、未打包**（未替换安装目录、未跑 替换LM.ps1、未编译/打包 iss）

【不确定项（列出，未硬猜）】
a) show 分支我加了一条底线：DgpuGeneration != NoDgpu（枚举成功但机器上没有 NVIDIA 适配器时仍隐藏）。
   理由：若严格 "show -> true"，在无独显机型上会凭空出现「显卡」面板与托盘 GPU 子菜单（今天不会）；
   有独显的机器不受影响。若你要严格照方案，把 MechrevoHw.cs :450 改回 return true; 即可。
b) 取值比较用 OrdinalIgnoreCase 且**不做 Trim**：手改成 "SHOW" 仍算 show；
   手改成 " show "（带空格）会落回 ask 分支。是否需要 Trim/归一化由你定。
c) §3④「只在判据不可靠时问」我按「已连接并上报 SupportsIgpuOnly」识别 50 系。
   启动早期 GCU 未连接时 SupportsIgpuOnly=false -> 50 系机器可能仍被问一次
   （回答会被 50 系可靠分支覆盖，无行为影响，仅多一次打扰）。
   Gen30 走注册表/设备 ID 判据（GpuGenerationProvider），不依赖 GCU，能稳定跳过。
d) 引导挂在 Program 的两处 ShowFirstRunGuideIfNeeded 旁；若用户长期 startMinimized 且从不还原窗口，
   只有「首次还原」那次机会。
e) **重要观察**：在 source\\ 里没找到 ModelScopedConfig.MigrateToModelScope / Plan / IsMachineScopedKey
   的调用点（AppConfig.GetModel() 同样无调用点），即「机器作用域」的迁移/读取路径在当前源码里
   没有调用者。按你的要求①已把两个新键加进数组，但**仅加数组目前不会让读取真正按机器隔离**，
   需要恢复迁移/作用域读取的调用点才会生效 —— 这点我没有动，等你确认（这是本次最大的未决项）。
f) 开关位置：放在设置对话框「官方控制台」行下方（SettingsDialog），没有放进主界面 GPU 面板 ——
   因为①要求是"设置对话框里新增一行"，②GPU 面板属三档显卡模式/核显省电既有区域（硬约束 2/3）。
g) 手动动设置开关会顺带把 igpu_only_asked 置 1（视为用户已表态，避免升级引导覆盖他的选择）。
   若你认为手动开关不该算「已问过」，删掉 ApplyIgpuOnlyUiSwitch() 里的那行 Set 即可。
h) 未做目视验收（本机未运行程序、未截图）；step-4 单选组用的是分段 RButton 高亮
   （Activated），若视觉上不合意可换 RCheckBox 组，落盘逻辑不受影响。
"""


def main():
    raw = open(LOG, "rb").read()
    text = raw.decode("utf-8")
    if TITLE in text:
        print("ERROR: entry already present")
        return 1
    if not text.endswith("\n"):
        text += "\n"
    text += "\n" + TITLE + "\n" + BAR + "\n" + BODY.lstrip("\n")
    if not text.endswith("\n"):
        text += "\n"
    with open(LOG, "wb") as fh:
        fh.write(text.encode("utf-8"))
    print("appended: %d bytes total" % len(text.encode("utf-8")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
