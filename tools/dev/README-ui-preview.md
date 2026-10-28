# UI 预览工具（离线出图，不用真机）

改 UI 前先出图，减少“发版后才发现错位”的来回。

## 1. 电池/限充面板（宝龙达专属面板）

```powershell
# 一次编译
dotnet build tools\dev\battery-preview\battery-preview.csproj -c Release

# 出图（默认 380 宽，输出 PNG）
tools\dev\battery-preview\bin\Release\net10.0-windows\battery-preview.exe D:\openLM\_probe\battery-panel.png
```

- 实例化真实 `BatteryLimitPanel` → `DrawToBitmap`；
- 本机（非宝龙达）也能渲染，状态行显示“状态读不到”，但**布局/按钮尺寸/重叠**都是真实值。

## 2. 设置窗截图（内置 `--ui-audit`）

```powershell
# 需要管理员（exe 清单是 requireAdministrator）
L-Mechrevo.exe --ui-audit <输出目录>
```

- 会离屏渲染主界面/设置等视图并写 PNG + 报告；
- 适合检查卡片重叠、主题按钮尺寸、PawnIO 行等。

## 说明

- 悬浮窗（`MechrevoLite.Overlay.HardwareOverlay`）继承自 `OSDNativeForm`（原生分层窗口），不能用 `DrawToBitmap` 离屏渲染 ⇒ 暂不做离线预览，需真机目视。
