# -*- coding: utf-8 -*-
with open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8') as f:
    f.write('''【2026-10-03 核显省电按钮待重启隐藏】
- 问题：混合模式下点切换"否"（发命令不重启）进入待重启后，核显省电按钮仍显示（判断只看 CanOfferIgpuOnly && GpuMode==1，未排除待重启）
- 修复：VisualiseIgpuSaving 运行时判断与 BuildDashboardLayout 构建判断均追加 !HasPendingGpuRestart()；VisualiseGPUMode 函数体内已含 VisualiseIgpuSaving() 调用，pending 标记后自动隐藏核显省电行
- 编译 0 错误；提权替换完成

''')
print('log ok')
