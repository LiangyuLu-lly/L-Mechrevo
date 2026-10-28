# -*- coding: utf-8 -*-
"""追加 更新日志.txt（面向用户）与 修改日志.txt（内部）"""
import io

CHANGELOG = r'D:\openLM\installer-src\files\更新日志.txt'
MYLOG = r'D:\openLM\修改日志.txt'

ENTRY = """
────────────────────────────────────────────
十五、30 系 GCU 适配与多项修复（2026-10-05，0.290.3 beta21.3）
────────────────────────────────────────────
一、性能模式 × 配置（30 系 GCU）
1. 性能模式行下方新增三个配置下拉框（静音 / 平衡 / 狂暴 各一个），
   可选「配置 1」～「配置 5」，与模式一起写入固件并回读校验；
   切换配置时即使当前模式未变，也会重新发布模式 + 配置索引。
2. 配置可自定义命名（名称保存在 LM 本地配置里，GCU 无此字段）：
   命名后下拉框直接显示该名称，未命名时显示「配置 N」。
3. 模式编辑器可打开风扇曲线并写入对应固件配置。
4. 英文界面下配置下拉框显示「Cfg N」（原「Config N」因宽度不足被截成「Conf...」）。

二、显卡模式
1. 移除 AUTO 显卡模式按钮（AUTO 即混合模式，官方控制台亦无此按钮）。
2. 新增「核显省电模式」行（混合模式下静默独显）：仅支持的机型显示，
   且仅在混合模式下出现；切换到集显 / 独显直连时自动隐藏。
3. 修复切换显卡模式时弹出两个重启提示的问题：
   重启提示改为单实例「看门狗」机制并去重；弹窗出现前若已有更新的切换请求，
   则废弃该次提示（不再出现两个相同弹窗）。

三、悬浮窗与主界面
1. 修复 30 系 GCU 不提供风扇转速时的显示异常：
   - 主界面 CPU / GPU 温度与占空比之间不再留出一大段空档；
   - 托盘浮窗不再显示「-1 rpm」（无转速时只显示百分比）。
2. 转速可用时仍按原样式显示「1234rpm 100%」，不受影响。

四、GCU 分发包
1. GCU 变体选择逻辑调整：
   50 系 / 无法识别 → 50 系 GCU；40 系 → 40 系 GCU；30 系 → 30 系 GCU；
   10 / 16 / 20 系 → 暂时使用 30 系 GCU。
   （安装时仍可用 -Variant 30/40/50 强制指定，便于测试）
2. GCU 载荷体积精简：删除与基础包重复的文件、IntelliSense 文档（.xml）、
   调试符号（.pdb）、以及其它机型的风扇表等无用文件。
3. 版本号 18.3 内测版 → 0.290.3 beta21.3。
"""

MYENTRY = """
[2026-10-05 05:00] 版本号改 0.290.3 beta21.3 + 打包目录清理合并 + 打包
- 用户三件事：①版本号改为 0.290.3 beta21.3 并写更新日志/修改日志
  ②检查 LM 打包文件夹的冗余文件 ③合并本次更新并打包
- **①版本号**：
  `source\\Properties\\AssemblyInfo.cs`：
    AssemblyFileVersion "18.3" -> "0.290.3"
    AssemblyInformationalVersion "18.3-内测版" -> "0.290.3-beta21.3"
    AssemblyVersion "18.3" -> "0.290.3"
  显示链路（已核对）：`Program.NormalizeReleaseVersion` = split('+')[0].split('-')[0] -> "0.290.3"；
  `NormalizeReleaseLabel` -> "beta21.3"（不以 "beta." 开头，原样返回）；
  `SettingsForm:3355` 拼成 **"0.290.3 beta21.3"** ✅ 正是要求的样子
- **②打包目录冗余检查**：写 tools\\check_iss_files.py
  （第一版有两个错：大小写敏感 -> 误报 files\\Fonts 缺失；对两个 .iss 各算一遍
   -> NoGCU 只打包主程序，GCU 目录被误判为冗余。已修正为「两个 .iss 都没引用」）
  结果：`files\\` 共 434 个文件；**失效引用 30 项 -> 清理后 0 项**；
  **真冗余 30 项 / 1.31 MB**
- **⚠️ 打包本会失败**：`.iss` 逐条列出文件，而 GCU payload 被精简过（删了
  oled_care.scr / ICCProfile 17 项 / License 4 项 / GCUUtil.exe / ACPIDriverDll.dll /
  GPUPowerSavingSettings.json / disable+enableWebcam.ps1 等）-> `.iss` 仍引用
  -> 写 tools\\fix_iss.py（默认 dry-run）：**删除 28 条失效 Source 行** + 更新 6 个版本行
  （AppVerName / AppVersion / OutputBaseFilename / VersionInfoVersion /
    VersionInfoProductVersion / UninstallDisplayName）
  产物名 `LM-0.290.3-beta21.3-setup`（文件名用连字符，与既有 LM-0.290.0-beta21.1-setup 一致）
  已写入两个 .iss（各留 .bak-<时间戳>）
- **冗余清理**（tools\\merge_and_clean.py，移到 backup\\files_clean_20261005_023308）：
  12 个：`GCU\\Install-Gcu.ps1.bak`、payload\\30 里的
  `GCUBridge.InstallState`/`InstallLog`/`InstallUtil.InstallLog`/`install.bat`/`uninstall.bat`、
  `MyControlCenter\\NAudio.dll`/`SharpDX.dll`/`SharpDX.RawInput.dll`（T3 遗留）、
  `GamingMonitor\\Config\\configs.json`、`AppSettings\\{AppProfileBindingPowerSaving,GamingMonitor}.json`；
  顺带删空目录（含 `payload\\GCU\\...\\ICCProfile`）
  **保留**：`L-Mechrevo.pdb`（排查崩溃）、`UserPofiles\\*.json`（配置模板）、
  `payload\\40\\README.txt`（维护说明）
- **③合并 + 打包**：合并 `rebuild\\lm_fix_build` -> `installer-src\\files`
  （策略：只覆盖 files 里已存在的同名文件，避免新增未被 .iss 引用的文件）
  ⚠️ **第一次合并后发现版本号不在 DLL 里** —— 因为版本补丁是在上次 publish 之后才改的；
  重新 build+publish（02:33:15）再合并，核验 `0.290.3` 与 `beta21.3` 均在 DLL 内 ✅
  -> 教训：**合并前必须核验产物内容**（主 DLL 大小恒为 1935360 = 512 字节对齐，
     看大小完全无法判断是否更新）
- 备份：backup\\AssemblyInfo_20261005_pre_02903.cs、install_script*.iss.bak-<ts>、
        files_clean_20261005_023308\\
- 未打包（下一步 ISCC）
"""

with io.open(CHANGELOG, 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
with io.open(MYLOG, 'a', encoding='utf-8', newline='') as f:
    f.write(MYENTRY)
print('changelog appended:', len(ENTRY.encode('utf-8')), 'bytes')
print('mylog appended:', len(MYENTRY.encode('utf-8')), 'bytes')
