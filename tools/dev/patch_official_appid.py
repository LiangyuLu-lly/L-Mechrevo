# -*- coding: utf-8 -*-
import io
p = r'D:\openLM\tools\build_cc30_gmonly.py'
s = io.open(p, encoding='utf-8-sig').read()

# 1) AppId 改为官方一致
s = s.replace("appid = '{{6C2F57CC-1BA5-4579-BF77-B0ED836F38E4}'",
              "appid = '{{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}'")

# 2) UninstallDelete 增加覆盖清理旧版 GamingMonitor（覆盖安装时官方旧文件不会被 [Files] 删掉）
s = s.replace("""[UninstallDelete]
Type: dirifempty; Name: "{app}";""",
              """[UninstallDelete]
Type: filesandordirs; Name: "{app}\\UniwillService\\MyControlCenter\\GamingMonitor";
Type: dirifempty; Name: "{app}";""")

io.open(p, 'w', encoding='utf-8-sig').write(s)
print('patched appid ->', '{{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}' in s)
print('patched uninst GM ->', 'GamingMonitor' in s)
