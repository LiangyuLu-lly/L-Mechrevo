# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 26:20] 🎯 台式机实测结论：Vermeer 的 CO 可读可写（每核写成立）—— 关键里程碑
================================================================================
用户提供的报告：D:\\Downloads\\SMU-CO-corewrite-20261005-230824.txt（15,344 字节）
机器：AMD Ryzen 7 5700X（Vermeer / family Matisse / SMU 0x00384C00 / 8 物理核）

【守卫】ACCEPT (matched via cpuid=Vermeer, driver=Vermeer)  ✅（两侧都报 Vermeer，上一轮担心的
        "driver 报 Matisse" 未出现；但"任一来源即放行"的修正仍是对的、且更稳）

【★ 关键结论 1：CO 回读成立（读路径确认）】
read path: PSMU 0x7C per core (GetDldoPsmMargin, 上游继承自 Zen2，标 // Not sure)
  8 核全 status=OK(0x01)，返回 0xFFFFFFF1 -> CO -15（32 位有符号）
  -> 曾被上游标注 "// Not sure" 的 0x7C **在这台机器上确实可用** ✅
  -> 解码位宽：读回为 **32 位有符号**（0xFFFFFFFB = -5）✅

【★ 关键结论 2：每核写成立】
写入：核 0 = -5 CO，候选 mp1-35-m16（MP1 0x35，margin 16 位，arg0 = 0x0000FFFB）
  -> write status OK(0x01)
写后逐核回读：
  core0: -15 -> **-5**     ← 只有它变了 ✅
  core1..core7: -15 -> -15 ← 都没变 ✅
  changed cores: 1/8 ; cores reading exactly -5: 1/8
**verdict: CORE-WRITE-OK** ✅
  -> 每核写的命令 ID（MP1 0x35）、arg0 编码（core<<20 | margin）、margin 位宽（16 位）三项全部确定 ✅

【★ 关键结论 3：恢复已核实】
per-core 写回 0 + SetCoAll(0) + DisableOc() -> 恢复后 8 核全部读回 0 CO ✅

【⚠️ 两个必须记住的副作用/发现】
1. **基线不是 0**：写前 8 核全部是 **-15 CO** —— 说明该机原本就有运行时 CO 偏移
   （可能来自 BIOS / 其他工具 / PBO 设置）。工具的"恢复到 0"把**这个 -15 也清掉了** ⚠️
   · 报告在写入**之前**就用 [WARN] 明确提示了这一点，并记录了基线值 ✅（设计正确）
   · CO 只在本次开机有效 -> **重启即由固件/BIOS 重新施加**，用户的 -15 会回来 ✅
   · ⚠️ **对产品的设计输入**：将来把每核写做进产品 UI 时，"恢复"不应一律写 0，
     而应**恢复到用户进入前的基线**（或至少明确告知会清除既有偏移）—— 否则会误清用户已有设置 ⚠️
2. **arg0 编码自检无法区分**：写前用 core<<20 与裸核号读同一核，两者返回**相同值**
   （因为当时是全核统一的 -15）。真正区分靠的是"写下核 0 后只有 core0 变化"这一步 ✅
   -> 结论：**读**对 arg0 不敏感（或该机为全核语义），但**写**确认为 core<<20 族 ✅

【下一步（按既定分支）】
- CORE-WRITE-OK 已达成 -> 可把以下内容接进**产品**（需用户批准）：
  ① 产品读表加 `case CpuCodeName.Vermeer: return 124u;`（0x7C）
  ② 产品支持每核写：MP1 0x35 + 16 位 margin + arg0=(ccd<<28)|(core<<20)|margin
  ③ UI：把"全核值"与"每核值"分开（8 个每核输入框/表格）
  ④ ⚠️ 恢复语义改为"恢复基线"而非"一律写 0"（见上）
- 仍未批准/未做：Matisse-only（真 Zen2 桌面）、Zen4 桌面、APU 的每核写
- 本次仍未打包、未打 NoGCU、未改产品源码
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
