# -*- coding: utf-8 -*-
r"""
把 gcu_send_mode.py 的「on」改成 LM 实测在用的命令
================================================================================
实测（LM 日志 2026-10-05 11:04:21）：
  MechrevoService.SwitchGpuMode(0) -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU
  SwitchGpuMode confirmed=True: expected=0 actual=0 action=DGPU_DIRECT_CONNECT_TOGGLE_IGPU
-> LM 切核显发的是 **DGPU_DIRECT_CONNECT_TOGGLE_IGPU**，
   不是 50 系风格的 IGPU_ONLY_CONNECT_RB_ON。
   后者在本机确实改了 NVRAM（路由变了）但 GCU 的 IGpuOnlyConnectionSwitch_Status 不反映它。
改法：on   -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU（与 LM 一致）
      on50 -> IGPU_ONLY_CONNECT_RB_ON（保留旧命令备用）
"""
import io
import sys

F = r'D:\openLM\tools\gcu_send_mode.py'

OLD = 'PAYLOADS = {\n    "on":      {"Action": "IGPU_ONLY_CONNECT_RB_ON", "SetToWMIEC": "OK"},\n'
NEW = ('PAYLOADS = {\n'
       '    # 2026-10-05：与 LM 实际发送的一致（实测 LM 日志 SwitchGpuMode(0) -> TOGGLE_IGPU）\n'
       '    "on":      {"Action": "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"},\n'
       '    # 50 系风格命令，本机也能改 NVRAM 但 GCU 状态字段不反映它\n'
       '    "on50":    {"Action": "IGPU_ONLY_CONNECT_RB_ON", "SetToWMIEC": "OK"},\n').encode('utf-8')


def main():
    data = io.open(F, 'rb').read()
    if b'"on50"' in data:
        print('已经改过了。')
        return 0
    ob = OLD.encode('utf-8')
    if data.count(ob) != 1:
        alt = ob.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            io.open(F, 'wb').write(data.replace(alt, NEW.replace(b'\r\n', b'\n'), 1))
            print('  写入 ' + F)
            return 0
        print('=== 锚点命中 %d 次，未写盘 ===' % data.count(ob))
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(ob, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
