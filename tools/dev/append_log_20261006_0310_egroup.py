# -*- coding: utf-8 -*-
# 2026-10-06 E 组界面细节排查修复 + DPI/小屏矩阵验证 + 打包 —— 追加到 修改日志.txt（utf-8 追加，不覆盖）
import io, os

LOG = r'D:\openLM\修改日志.txt'
TITLE = '[2026-10-06 03:10] E 组界面细节排查修复 + DPI/小屏矩阵验证 + 打包'

BODY = """E 组（界面细节 / 文字裁切）本次按"跨缩放渲染 + 场景矩阵"验证：新建只读取证工具 tools\\egroupshot\\
（复用 archive\\tools-20261005 d1shot 的"fixture 能力位 + 真实窗体 + DrawToBitmap"套路，判据直接反射复用产品
MechrevoLite.UI.UiAuditRunner.AuditForm/GetTextClipping/CheckSiblingOverlaps/CheckEmptyBands/CheckFooterOcclusion，
与产品 --ui-audit 完全同口径；另加"标签折行量高 / 每核 NumericUpDown 文本宽 / 每核网格整块宽 / D-1 提示行 /
页脚版本号几何链"五类自有观察量），12 个夹具（Settings×3、SettingsDialog、CustomMode、CpuTune×5=AMD-8核/
AMD-无每核/AMD-无回读命令/Intel/无驱动、FanCurve、FirstRunGuide）× 8 个视口（DPI 100/125/150/200% ×
1920×1080 与 1366×768）= 96 组合，全程 fixture hw + 临时配置文件 + 临时日志目录（LMECHREVO_CONFIG_FILE /
LMECHREVO_LOG_DIR），不连 GCU、不读 SMU、不碰 PawnIO 驱动、不点应用/重置，两次 after 复跑结果逐行一致。
修前 143 行观察量、修后 142 行，**唯一差异**是 FirstRunGuide 在 1366×768@200% 的 window-overflow 被修掉
（窗口 1176×821 → 1176×624，可用区 1366×672），零新增问题（无回归）：根因是窗体 MinimumSize(540×430 经 DPI
放大 = 1064×821) 本身大于可用工作区，把 ResponsiveLayout.ConstrainToWorkingArea 的收敛顶住，窗口必然超出屏幕、
底部「稍后/前往系统页」落在屏幕外；被动 ResponsiveLayout.ConstrainToWorkingArea（唯一改动文件）新增一段：
当窗口下限在工作区里根本不可达时，只在这种情况下把 MinimumSize 放松到刚好装得下，其余情况一个字节不动
（100%/125%/150% 及 1920×1080@200% 行为完全不变）。复核 A-25（DHead(52)→DHead(64)）：**结论仍为不适用**——
全文件 grep `DHead(` 只有 L2924 的函数定义、无任何调用，校色行(rowColorCalibration)是 TableLayoutPanel 百分比列
(22/10/56/12) + AutoSize=false/Dock=Fill 标签，与作者固定 64 逻辑像素是两套实现，强行插入会破坏百分比布局，维持跳过。
另有两项"观察到但判定不改"（依据不足/判据误报，均写明理由）：① 页脚版本号 labelVersion 在 200% 视口显示为
"0.290.7 b…"（截图取证），但原因是审计用 form.Scale+ScaleFonts 模拟 DPI 时把 panelFooter/内层表 padding 二次放大
（模拟下可用高 54px < 需 58px；真实 200% 会话里该行可得 ~78px > 需 58px），属仿真口径产物，且明显修法（把版本列
从 22% 加宽）会把 6 个固定 46 逻辑像素宽的页脚按钮挤到互相重叠 → 按硬约束"依据不足保留原状"不改；② FirstRunGuide
32 条 text-clipping 是判据对"AutoSize=false 中文换行标签"的已知误报（截图里正文完整可见），CpuTune-NoDriver 的
cpuTunePerCoreInfo 空标签 width=0 无用户影响，均不改。核心结论：设置窗口各行/下拉框/复选框、CPU 调节窗口、
每核 8 输入框（4 列，网格宽 555→1127 ≤ 宿主 583→1183，8 框齐全）、显卡模式区、按钮文字在 8 个视口全部 PASS，
我们新加的 D-1 提示行「本机可能不支持，可试一次」8 个视口全 PASS（152/200px → 289/400px），仅 1366×768+200%
的首次引导窗口尺寸被修。验收：编译 0 错误；部署 C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll 2,080,768 B /
SHA256 B02F8C2BDF9D8138F87DBD21A6198FC30CBEB0FCEBBC9D0BB645A50DF8B9C5CC（与源构建逐字节一致）/
FileVersion 0.290.7，zh-CN 卫星同步，LM PID=27784 存活，重启后 35 行日志无异常（"Performance mode editor
refresh failed" 自 10-05 20:40 起既有，与本次无关）。打包：publish（须知里的显式参数写法，未用 rsp）→
py -3 tools\\merge_and_clean.py --apply（更新 L-Mechrevo.dll 1901→2032KB + zh-CN 卫星，另 12 个冗余文件移入
backup\\files_clean_20261006_011418）→ ISCC 主包 **0 Warning / 0 Error** → installer-src\\Output\\
LM-0.290.7-beta21.7-setup.exe 52,147,419 B (49.73MB) / 2026-10-06 01:14:41 / FileVersion 0.290.7 /
SHA256 3E043391C23A3A212F1EBFBDF03E595BC945BFB1BD1C1461398DE0E4DA88EF55；**版本号本次未升**（仍 0.290.7 /
beta21.7，覆盖旧包，建议下次升 beta21.8 见报告）；Output 里只有主包、无 NoGCU 产物；GCU 分流脚本
Select-GcuPayload.ps1 22,337 B / D462DBF1BCFD20F96091211208B46528C5AB08919C07EC68B3907A1BEB7E2F06 与
payload 30/40/50/GCU 打包前后哈希逐一致（innounp 读包内计数 114/3/3/247 亦与基线一致）；未安装、未运行该安装包，
C:\\Program Files\\L-Mechrevo\\ 除 替换LM.ps1 未动任何东西；取证工具留在 tools\\egroupshot\\，证据在
diagnostics\\egroup-20261006\\（before/after/after2 的 txt+csv+96 张截图+crops+包清单），未在仓库根留任何临时文件。
"""


def main():
    assert os.path.exists(LOG), LOG
    with io.open(LOG, 'r', encoding='utf-8', errors='replace') as f:
        before = f.read()
    block = '\n' + ('-' * 80) + '\n' + TITLE + '\n' + ('-' * 80) + '\n' + BODY
    with io.open(LOG, 'a', encoding='utf-8', newline='\r\n') as f:
        f.write(block)
    with io.open(LOG, 'r', encoding='utf-8', errors='replace') as f:
        after = f.read()
    print('appended  :', after.startswith(before), '| grew by', len(after) - len(before), 'chars')
    print('title at  :', after.find(TITLE))
    print('last line :', after.rstrip().splitlines()[-1][:80])


main()
