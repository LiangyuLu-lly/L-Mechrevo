# -*- coding: utf-8 -*-
import os, datetime

def info(path):
    if os.path.exists(path):
        st = os.stat(path)
        return f'{st.st_size}B {datetime.datetime.fromtimestamp(st.st_mtime)}'
    return 'MISSING'

# 编译产物目录
build_dir = r'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0'
print('== 编译产物 ==')
print('主 DLL:', info(os.path.join(build_dir, 'L-Mechrevo.dll')))
for sub in ('zh-CN', 'en', 'en-US'):
    d = os.path.join(build_dir, sub)
    if os.path.isdir(d):
        for f in os.listdir(d):
            print(f'  {sub}\\{f}:', info(os.path.join(d, f)))

# 安装目录
inst = r'C:\Program Files\L-Mechrevo'
print('== 安装目录 ==')
print('主 DLL:', info(os.path.join(inst, 'L-Mechrevo.dll')))
for sub in ('zh-CN', 'en', 'en-US'):
    d = os.path.join(inst, sub)
    if os.path.isdir(d):
        for f in os.listdir(d):
            print(f'  {sub}\\{f}:', info(os.path.join(d, f)))
    else:
        print(f'  {sub}: 目录不存在')
