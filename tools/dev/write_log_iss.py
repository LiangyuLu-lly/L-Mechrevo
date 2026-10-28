# -*- coding: utf-8 -*-
with open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8') as f:
    f.write('''【2026-10-03 安装程序 .NET 检测双保险 + 版本号 beta21.1】
- 问题：IsNetDesktop10Installed 仅查注册表键（HKLM\\...\\sharedfx\\Microsoft.WindowsDesktop.App），手动部署/zip 解压的 .NET 桌面运行时（如 C:\\Program Files\\dotnet 直接放置）不写注册表 → 能运行却会被误判"缺运行库"并弹下载
- 修复：检测改为三选一通过——注册表键存在 或 {commonpf64}\\dotnet\\shared\\Microsoft.WindowsDesktop.App 目录存在 或 {commonpf}\\dotnet\\shared\\Microsoft.WindowsDesktop.App 目录存在
- 版本：install_script.iss AppVerName/AppVersion/OutputBaseFilename 均为 0.290.0-beta21.1，产出 LM-0.290.0-beta21.1-setup.exe（ISCC 编译 19.7s 成功）
- 验证：本机场景（注册表键缺失 + 目录存在）现在判定为已安装，不再误弹

''')
print('log ok')
