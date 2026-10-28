# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 对齐 beta21：删除 GpuSwitchRoute.Direct 路由（用户拍板：40 系核显→混合同样走重启）】
- 背景：beta21 枚举为 NoChange/HotSwap/Restart（无 Direct）；群友版路由层基于 18.6 保留 Direct，实际只覆盖"集显→混合(0→1)"即时切换。用户决定对齐 21 的保守策略：0→1 也落回重启
- 修改：
  1. GpuSwitchRoute.cs：枚举删 Direct → NoChange/Restart
  2. GpuSwitchPolicy.cs：末尾 else 返回 Restart（附注释：对齐 21，0→1 同样重启后应用）
  3. SettingsForm.cs：删 case GpuSwitchRoute.Direct 分支（原 Direct 分支的 SwitchGpuMode 即时切换不再可达；0→1 落入 Restart case → AllowsRestart(40 系)=true → 弹重启确认）
- 行为变化：40 系三模从核显切回混合，由"即时切换"变为"重启后应用"（与 21 一致）
- 编译 0 错误（0 警告增量）；提权替换完成（DLL 1895936B @11:22:09，LM PID 24968）；替换脚本第二次因 DLL 被运行中进程占用 Copy 失败 → 改用 elevated_kill_copy.ps1（先杀进程再复制再启动）成功
- 至此 GpuSwitchRoute 与 beta21 完全一致（NoChange/Restart）；热切换/即时切换路径全部移除，所有显卡模式变更统一走重启后应用

'''

upd = '''【2026-10-03】〇、显卡切换路由对齐 beta21
- 删除 Direct 即时路由：集显→混合(0→1) 与其余切换统一走"重启后应用"（40 系核显切回混合同样需要重启确认）
- 显卡切换路由现与官方 beta21 一致：仅 NoChange / Restart 两种

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
