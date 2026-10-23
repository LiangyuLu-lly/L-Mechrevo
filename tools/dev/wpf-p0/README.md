# WPF P0 原型

这是 WinForms → WPF 迁移评估的 **P0 原型**，只验证视觉/交互地基，不接入硬件层。

## 运行

```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release
& 'D:\openLM\tools\dev\wpf-p0\bin\Release\net10.0-windows\L-Mechrevo.WpfP0.exe'
```

## P0 验证点

- 背景图 `Image Stretch=UniformToFill` 铺满窗口；
- 半透明卡片 / 控件叠在底图上；
- `ScrollViewer` 内容随滚动平移，不重绘子控件；
- 深色/浅色主题与强调色通过 `ResourceDictionary` 即时切换；
- DPI / 字体 / 圆角 / 阴影观感；
- 与现有 WinForms 逻辑库完全隔离，失败可直接删除本目录。

## 当前范围

只有主界面一页的静态布局：性能模式、快捷开关、显卡/显示、电池、风扇曲线占位、外观设置。
下一阶段 P1 才接 `MechrevoLite.Hardware` / `Mode` 等逻辑层与真实数据。

## 截图

`preview.png`（2026-10-23 运行截图，1180×800）。
