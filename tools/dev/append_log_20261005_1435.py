# -*- coding: utf-8 -*-
"""2026-10-05 追加：收紧「核显模式显示设置」单问对话框版面（追加，不覆盖）。"""
import io

ENTRY = """
[2026-10-05 14:35] 收紧「核显模式显示设置」单问对话框版面
================================================================================
用户：「单问题对话框太大、太占屏，而且标题重复」-> 只改 igpuOnlyStepOnly 单问模式版面，
完整引导（4 步）观感一字不动；不部署、不打包。

【改了哪些位置（全部在 source\\MechrevoLite\\FirstRunGuideForm.cs + 文案资源）】
1. 去掉重复的大标题 —— 单问模式标题区原来是「15pt 粗体 H1『核显模式显示设置』+ 副标题」两行，
   而窗口标题已经是「核显模式显示设置」，纯重复。现改为只留一行 8.5pt 小字辅助说明；
   完整引导仍是大标题「开始使用 L-Mechrevo」+ 引言（原样）。标题区 Padding：单问 (16,10,16,2)
   / 完整 (24,16,24,8)（后者与原值一致）。
2. 副标题缩短 —— IgpuOnlyGuideIntro（该键只有单问模式用，完整引导用的是 FirstRunIntro）：
   「只需回答一个问题，回答后还可以在设置里随时修改。」->「回答后仍可在设置里随时修改。」
   EN: "One quick question — you can change the answer any time in Settings."
     -> "You can change the answer any time in Settings."
3. 正文缩短 —— 新增 FirstRunStep4BodyShort（单问模式专用）：
   「在 BIOS 或官方控制台里找「集显模式 / 核显模式 / iGPU Only」这一档。」
   必需关键词「BIOS 或官方控制台」「集显模式 / 核显模式 / iGPU Only」都在；100% DPI 下自动折 2 行，
   125%~200% 下 1 行。完整引导继续用原文 FirstRunStep4Body（一字未改）。
   两个 resx（UTF-8 带 BOM、LF、逐条空白排版原样）+ Strings.cs 同步新增该键，
   校验 710/710/710 一致；脚本 tools\\add_igpu_compact_strings.py。
4. 尺寸策略 —— ClientSize 580×290 -> 460×230（96 基线逻辑像素）。
   缩放沿用本文件既有风格：先写 96 基线值，末尾由 ResponsiveLayout.ScaleFrom96 统一按 DPI 缩放；
   **没有**在布局里改用 D()/LogicalToDevice 逐个换算 —— 本文件是「raw 96 + 末尾 ScaleFrom96」，
   混用会二次缩放（D() 风格是 RgbForm/SettingsDialog 那种 AutoScaleMode.None 窗体用的）。
   在 ScaleFrom96 之后调用新增的 ApplyCompactIgpuOnlyLayout() 收口：
     - 客户区宽 = max(当前宽, LogicalToDevice(460))，保证 460 逻辑宽；
     - 实测「标题区 + 步骤块 + 底部按钮行」的实际高度，若比客户区高就按内容增高
       （宁可高一点也不裁正文 / 不冒滚动条）；
     - MinimumSize = 内容刚好放得下的尺寸（+ 非客户区），MaximumSize = 紧凑尺寸 ×1.5（兜底上限）。
   未用 Form.AutoSize / AutoSizeMode.GrowAndShrink：本窗体中排是 Percent 100 + AutoScroll 面板，
   AutoSize 会把中排按内容塌缩，尺寸反而不可控；故采用「明确客户区 + 内容兜底 + Min/Max 夹住」。
5. 间距收紧（仅单问模式）：底部按钮行行高 58 -> 46、Padding (16,8,16,8) -> (12,6,12,6)；
   步骤块 Padding (4,8,4,8) -> (4,4,4,4)、下边距 8 -> 4、序号圆标 28 -> 24、正文上边距 4 -> 2；
   三选一行高 30 -> 26、上边距 8 -> 6。完整引导全部走原来的值。
6. 三选一「有/没有/不确定」与底部「稍后 / 确定」保持原样可用：同一 CreateIgpuOnlyChoiceRow、
   同一 RButton + Activated 单选、默认「不确定」、AcceptButton/CancelButton 不变。

【硬约束遵守】
- 落盘逻辑与键名一字未动：PersistIgpuOnlyChoice() 仍是 有->igpu_only_ui=show、没有->hide、
  不确定/未选/X/Esc->show，且一律 igpu_only_asked=1；UiAuditMode 下不写。
- SettingsForm 未改（ShowIgpuOnlyGuideIfNeeded 仍原样调 FirstRunGuideForm(igpuOnlyStepOnly:true)）。
- 未动三档显卡模式 / 核显省电 / 性能模式 / ResolveGpuModeStatus / GpuRestartVerifier；installer-src 未动。
- 完整引导（igpuOnlyStepOnly:false）所有取值仍是原始字面量（三元分支全部走 else 侧），观感不变。

【编译】
- `& D:\\openLM\\dotnet-sdk\\dotnet.exe build D:\\openLM\\source\\L-Mechrevo.csproj -c Release`
  -> **0 错误**（警告 1186 -> 1189，新增 3 条全是 CS8632「可空注解」提示，与本文件既有 RButton? 同源）
- 未部署 / 未打包：未 publish 到安装目录、未跑 替换LM.ps1、未编译 iss。

【尺寸与目视核验（临时工具，非产品代码）】
- 主程序有单实例互斥（Global\\L-Mechrevo-SingleInstance）且用户 LM 正在运行（PID 26848），
  --ui-audit 会被互斥挡住，故改用临时工具 D:\\openLM\\tmp_compact_shot\\（引用编译产物，
  反射复用 UiAuditRunner 的 AuditForm/CapturePages 判据，另加自写的裁切/滚动条/越界检查），
  渲染单问模式与完整模式各 6 个视口，截图在 D:\\openLM\\tmp_compact_shot\\shots\\。
- 单问模式结果：客户区在 100%/125%/150%/200% DPI 下**恒为 460×230 逻辑像素**
  （100% -> 窗口 476×269；200% -> 窗口 936×499），无越出工作区、无滚动条、无兄弟重叠、
  自写「换行文本高度 vs Label 高度」检查全通过；200% 下正文一行放得下，100% 下折 2 行且高度够。
  AuditForm 对单问模式只报了 1 条 text-clipping，原因是该判据把中文按**单行**量宽后与 Label 宽度比；
  完整引导在同一判据下报 4 条同类 —— 属「AutoSize=false 的换行中文 Label」固有误报，非真实裁切。
- 完整引导在本工具里仍是 580×500 / 4 步 + 第 4 步，内容超高时同样由 AutoScroll 出滚动条
  （与改动前一致，属既有表现）。

【未确定项】
1. 未用 Form.AutoSize（原因见上，改为内容兜底 + Min/Max）；若坚持纯 AutoSize 需另调中排布局。
2. 未做「改动前 vs 改动后」同机像素级 A/B：D:\\openLM 不是 git 仓库，取不到改动前的源文件；
   「完整引导不变」是按逐行核对（所有取值都在 else 分支、与原字面量一致）+ 本工具渲染结论得出。
3. 运行时 DPI 切换（把对话框拖到另一块不同缩放的显示器）时 Minimum/MaximumSize 可能仍是旧值；
   因是 FixedDialog 不可缩放，最坏只是兜底值偏保守、不改变布局比例。未实测该场景。
4. 100% DPI 下正文折 2 行（第 2 行只剩「档。」）；若要一行可再删字或把客户区宽给到 480。
5. 仍建议用户在本机实际弹一次做目视验收（弹窗条件：igpu_only_asked != 1 且判据不可靠）。

【未部署未打包，待验收】
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
