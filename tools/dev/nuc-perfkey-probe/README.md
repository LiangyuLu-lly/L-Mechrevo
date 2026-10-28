# NUC / UMI 性能键事件探针

## 用途

只读探测性能键按下时：

- ACPI EC 地址 `1870` 等是否变化
- 是否有 WMI 事件

不写任何 EC、不切模式、不改键盘灯。

## 运行

管理员 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\Capture-Ec.ps1 -Seconds 120
```

运行后 120 秒内按性能键 3 次，每次间隔约 3 秒。

输出：

```text
perfkey-capture-YYYYMMDD-HHmmss.txt
```

## 注意

- 需要 `\\.\ACPIDriver` 存在
- 如果没有任何 EC 变化，下一步需要加 WMI 事件订阅探针
- 该目录只用于探测，不参与正式构建
