# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 14:20] ✅ 实现「核显模式显示策略」①~⑥（⑦仍不做）+ 子代理报的一个重要发现
================================================================================
用户：「第七点加分项存档 其他可以去做了」-> 实现 ①~⑥，⑦（读官方控制台配置）保持不做。

【逐文件改动（子代理执行，主代理复查+部署）】
1. ModelScopedConfig.cs —— ExactMachineScopedKeys: string[3]{charge_limit,gpu_mode,ec_charge_limit}
   -> string[5]{..., igpu_only_ui, igpu_only_asked}
2. MechrevoHw.cs CanOfferIgpuOnly（约 :419-461）按优先级重写：
     Gen30 -> false（新增显式硬屏蔽，防被 show 越过）
     SupportsIgpuOnly(50系) -> IsGpuActionAllowedByGeneration("DGPU_DIRECT_CONNECT_TOGGLE_IGPU")（原样）
     igpu_only_ui==hide -> false ／ ==show -> true
     否则(ask) -> CanOfferGpuModeSwitch && SupportsDgpuDirect（与今天一致）
3. SettingsForm.cs —— 新增 BuildIgpuOnlyUiPanel()（样式对齐 BuildOfficialConsolePanel）/ 
   SyncIgpuOnlyUiSwitch() / ApplyIgpuOnlyUiSwitch()（写键 -> 防递归 Sync -> RefreshDeviceCapabilities
   -> SetContextMenu）；SettingsDialog.cs 加可选参数并在「官方控制台」行后 AddRow
4. FirstRunGuideForm.cs —— 加第 4 步（marker '?'，CreateStep 增可选 extra 控件参数），
   三选一「有/没有/不确定」（ApplySegmentGroup + RButton.Activated，默认「不确定」）；
   FormClosed 统一落盘：有->show、没有->hide、不确定/未选/X/Esc->show（fail-safe），
   一律 igpu_only_asked=1；UiAuditMode 下不写
5. SettingsForm.ShowIgpuOnlyGuideIfNeeded()（新，:4569-4588；ShowFirstRunGuideIfNeeded 未改）
   —— UiAuditMode 跳过；igpu_only_asked != 1 且判据不可靠才弹；复用 FirstRunGuideForm(igpuOnlyStepOnly:true)
   触发点 Program.cs:678 / :1853
6. 字符串 —— 两个 resx（UTF-8 带 BOM，行尾/排版原样）+ Strings.cs 同步新增 9 键：
   FirstRunStep4Title/Body/Yes/No/Unknown、IgpuOnlyGuideTitle/Intro、IgpuOnlyShowToggle/Tip
   校验 709/709/709 一致；插入脚本 tools\\add_igpu_ui_strings.py
新增键与默认值： igpu_only_ui = ask|show|hide（默认 ask）；igpu_only_asked = 0|1（默认 0）

【编译与部署】
- 编译 0 错误（1178 -> 1186 警告，新增 8 条全是 CS8632 可空注解，与工程既有同类）；
  另做 -t:Rebuild 全量复核 0 错误
- 部署：产物 1934848 字节 @14:05:41；替换完成 14:05:41；LM PID 26848 Responding=True ✅
- 启动后**无任何异常**日志 ✅；两个新键尚未写入（首次弹窗回答后才写）✅
- 未部署/未打包 iss（遵守要求）

【⚠️ 子代理报的重要发现（主代理认为需要用户决定）】
**「机器作用域」机制当前是休眠的**：
  `ModelScopedConfig.MigrateToModelScope / Plan / IsMachineScopedKey`
  以及 `AppConfig.GetModel()` 在 source/ 里**找不到任何调用点**。
  -> 仅把键加进 `ExactMachineScopedKeys` **不会让读取真正按机器隔离** ⚠️
  -> 受影响的不只是本次两个新键，**连既有的 charge_limit / gpu_mode / ec_charge_limit 也一样** ⚠️
  -> 对本功能的实际影响：开关是"按安装"生效，**不是按机型**；单机用户完全够用 ✅，
     但把 Windows 配置目录在**不同机型间复制**时会串（A 机设 hide，B 机也 hide）⚠️
  方案：(a) 接受现状（够用）／(b) 另开任务恢复机器作用域迁移调用点（较大改动）

【其它未确定项（子代理原文摘要）】
1. `show` 分支加了一条底线 `DgpuGeneration != NoDgpu` —— 严格 `show -> true` 会在"枚举成功但无 NV 适配器"
   的机型上凭空造出显卡面板/托盘项；有独显机器不受影响。（要严格照方案就把 :450 改回 return true）
3. §3④ 按"已连接并上报 SupportsIgpuOnly"识别 50 系；启动早期 GCU 未连接时 50 系可能被问一次
   （回答会被可靠分支覆盖，无行为影响）。Gen30 用注册表/设备 ID 判据可稳定跳过。
4. 开关放在设置对话框（未放主界面 GPU 面板，避免碰三档/核显省电区域）；手动动开关会置 asked=1；
   show/hide 比较用 OrdinalIgnoreCase 且不 Trim。
5. 未做目视验收；第 4 步用分段 RButton 高亮，视觉不合意可换 RCheckBox 组（落盘逻辑不变）。

【待用户目视验收】
- 重启 LM 后是否弹出新引导（本机 igpu_only_asked 未写 + 判据不可靠 -> 应弹）
- 设置对话框里「显示核显模式」开关是否存在、切换是否即时生效
- 引导第 4 步的三选一与落盘（有->show / 没有->hide / 不确定->show）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
