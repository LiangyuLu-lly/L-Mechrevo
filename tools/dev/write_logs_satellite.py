# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 补充返工：zh-CN 卫星资源程序集遗漏替换】
- 现象：用户发现中文资源"没替换"——resx 改动已编译进 11:27:14 的产物，但 LM 仍显示旧文案
- 根因：zh-CN 语言资源编译为**独立卫星程序集** zh-CN\\L-Mechrevo.resources.dll（53248B），不在主 DLL 内；此前所有提权替换脚本只复制主 DLL，安装目录的 zh-CN\\L-Mechrevo.resources.dll 一直停留在 09:50 旧版（含"集显"文案）
- 修复：elevated_kill_copy.ps1 加入卫星资源复制（New-Item zh-CN 目录 + Copy-Item resources.dll），已重新提权替换
- 验证：安装目录 zh-CN\\L-Mechrevo.resources.dll = 53248B @11:27:14（与编译产物一致）
- **教训（后续所有替换必须带）**：主 DLL + zh-CN\\L-Mechrevo.resources.dll 两个文件一起复制，否则中文资源不生效
- 本补充仅记录返工过程，不改功能；更新日志无需追加（术语文案变更此前已按规则只记修改日志）

'''

append(r'D:\openLM\修改日志.txt', mod)
print('log ok')
