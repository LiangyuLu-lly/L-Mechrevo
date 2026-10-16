# -*- coding: utf-8 -*-
with open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8') as f:
    f.write('''【2026-10-03 版本号显示 0.290.0 beta21.1】
- AssemblyInfo.AssemblyInformationalVersion 改为 "0.290.0-beta21.1"（AssemblyVersion/FileVersion 保持 0.290.0）
- Program.NormalizeReleaseVersion 追加 Split('-')[0]，ReleaseVersion 保持纯数字 "0.290.0"（更新检查、诊断包文件名、遥测 ver 不受影响）；ReleaseLabel 解析为 "beta21.1"
- SettingsForm 底栏 labelVersion 显示改为"版本 + 标签"拼接：有 beta 标签时显示 "0.290.0 beta21.1"，无标签时仅显示版本
- 安装程序 .NET 依赖：已确认 installer-src/install_script.iss 内置检查（共享 fx 注册表键检测 → 缺失弹窗 RuntimeRequired → 是=自动下载 aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe 安装 / 否=打开官方下载页），无需新增
- 编译 0 错误；提权替换完成

''')
print('log ok')
