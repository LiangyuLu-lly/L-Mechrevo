# L-Mechrevo.exe 启动 CLR 内部错误（2026-10-25 存档）

## 现象

用户机器双击 `L-Mechrevo.exe` 直接打不开。

事件查看器：

```text
来源: .NET Runtime
事件 ID: 1023
Application: L-Mechrevo.exe
CoreCLR Version: 10.0.1126.37416
.NET Version: 10.0.11
Description: The process was terminated due to an internal error in the .NET Runtime
Exit code: 0x80131506
```

`0x80131506` = `COR_E_EXECUTIONENGINE`，CLR 内部错误，不是普通托管异常。

## 当前判断

- 不是 WPF 宽屏/迷你窗口的正常业务异常。
- `L-Mechrevo.exe` 是旧 WinForms 主程序；`L-Mechrevo.WpfP0.exe` 是 WPF P1。
- 用户机器运行时为 10.0.11；开发机构建/测试运行时为 10.0.12。
- 可能是运行时版本差异、旧 WinForms 启动路径、原生互操作（PawnIO/inpoutx64/EC/WMI/HID）或栈溢出。
- 事件 1023 没有托管堆栈；需要事件 1000（Application Error）的出错模块或 dump 才能定位。

## 待办

1. 确认 `L-Mechrevo.WpfP0.exe` 是否能正常打开。
2. 升级 .NET 10 Desktop Runtime 到 10.0.12 或更新。
3. 确认同目录 exe/dll/runtimeconfig 同版本，不混用。
4. 抓 `Application Error` 事件 ID 1000 的出错模块。
5. 开启 LocalDumps 后复现，分析 dump。
6. 临时入口改用 `L-Mechrevo.WpfP0.exe`。
