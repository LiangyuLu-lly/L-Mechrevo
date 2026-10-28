# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 16:40] 合并三个决策拍板 + 查清 UV 入口与白名单
================================================================================
用户决策（回应子代理提出的三件事）：
  ① WinPowerPlan —— **跟作者一样**（删掉 WellKnownPlans 合并）
     用户理由：「笔记本要卓越性能没有用（非常费电）」✅
  ② CpuInfo.IsSupportedUV() —— **按作者来（CPUID 判据）**
     用户附带疑问：「我还没见过这玩意入口在哪里以及判断白名单是什么」-> 本次已查清（见下）
  ③ 遥测服务端回执（A-4）—— **选 C**：做，但加退路
     （收不到回执时把 HTTP 200 当确认，做成可配置开关）

【查清：UV 白名单是什么】
`D:\\openLM\\source\\PawnIO\\CpuInfo.cs:52`
```csharp
public static bool IsSupportedUV()
{
    if (!Name.Contains("RYZEN AI MAX") && !Name.Contains("Ryzen AI 9") && !Name.Contains("Ryzen 9")
        && !Name.Contains("4900H") && !Name.Contains("4800H"))
    {
        return Name.Contains("4600H");
    }
    return true;
}
public static bool IsSupportedUViGPU() { return Name.Contains("6900H"); }
```
-> 白名单（CPU 型号字符串）实际是：
   **RYZEN AI MAX / Ryzen AI 9 / Ryzen 9 / 4900H / 4800H / 4600H**
   iGPU 降压另有一档：仅 **6900H**
-> **本机是 AMD Ryzen 7 8745H** -> 不在表里 -> `IsSupportedUV()` = **false**
   => **本机看不到 UV 入口是正常的** ✅（不是 bug）
-> `Name` 来自注册表 `HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0\\ProcessorNameString`

【查清：UV 的界面入口在哪（为什么用户"从没见过"）】
- 全工程搜 UV 相关资源键 / `CustomModeForm` 里的 UV 控件：**都没有**（resx 无 UV 键、CustomModeForm 无 UV 引用）
- `ModeControl.cs:545-628` 只在**应用**某个模式时读/写 `_cpuUV` / `_igpuUV` 并校验白名单；
  `AppConfig.IsApplyUV()` = `IsMode("auto_uv")`（配置键驱动，非界面控件）
- 本机 config.json 里也没有任何 UV 字段
=> **结论：我们这条线目前根本没有 UV 的界面入口** ✅
   作者的 **`CpuTuningForm`（清单 A-28，我们"完全没有"）**才是新增的 CPU 调校界面，
   很可能就是用户听说/期待的那个入口 —— 这解释了「我还没见过这玩意入口在哪里」✅
   合并 A-28/A-29 之后才会有入口；且入口是否出现仍受白名单（作者版改为 CPUID）约束
   —— 本机 8745H 在**现行**白名单下仍是不支持，合并后是否会开放取决于 CPUID 判据的结果

【下一步】
按 9 步合并计划逐步执行（每步编译+部署+用户验收）：
  ①PawnIO/SMU 加固 ②Logger 错误环+事件 ③重连退避/BLE/日志分级 ④配置落盘+文案
  ⑤模式切换可取消 ⑥界面裁切 ⑦A AMD CPU 调校 ⑦B Intel+驱动安装 ⑧遥测管线 ⑨旧机型后端
其中：
  - ①/②/③/④/⑤/⑥/⑧/⑨ 与本机无冲突点，按计划执行
  - ⑦ 涉及 CPU 调校与 UV/OC 入口 -> 按用户决定改用作者的 CPUID 判据（决策②）
  - ⑧ 遥测管线里的 A-4 按决策③做（带回执退路）
  - WinPowerPlan 按决策①采用作者做法（删除合并）—— 但注意我们自定义模式用电源计划 GUID
    （config 里有 custom0_plan = 381b4222-...），删合并前需确认自定义模式仍能找到目标计划 ⚠️
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
