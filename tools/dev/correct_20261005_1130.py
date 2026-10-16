# -*- coding: utf-8 -*-
"""更正：把"我手动发核显指令成功"的错误结论作废（用户澄清是 BIOS 手动切的）"""
import io

LOG = r'D:\openLM\修改日志.txt'
DOC = r'D:\openLM\核显模式判据-存档与结论.md'

LOGENTRY = """
[2026-10-05 11:30] ⚠️ 更正：作废"我手动发核显指令成功"的结论（用户澄清是 BIOS 切的）
================================================================================
用户原话：「⚠️ 改了 NVRAM（确实切了 ✅结论错误 是我进BIOS手动切的」
--------------------------------------------------------------------------------
【作废的结论】
- 2026-10-05 07:20 条里我写：
  「连发 5 次的 IGPU_ONLY_CONNECT_RB_ON 确实被接受并写入了 NVRAM ✅」
  「只是写入时刻在我 02:58:14 那次读取之后」
  -> ❌ **全部作废**。NVRAM @0x62 变 2 **是用户在 BIOS 里手动切核显模式造成的** ✅
- 2026-10-05 09:00 存档文档里同样写着"实测：on 有效 —— NVRAM @0x62 变为 2、NVIDIA 被屏蔽" ❌ 作废
- 2026-10-05 11:10 条里"为什么我手动发看起来失败"那段的前提（"确实改了 NVRAM"）❌ 也作废

【更正后的结论（重要）】
1. `IGpuOnlyConnectionSwitch_Support = NotSupport` **说的是真话** ✅ ——
   本机这个 40 系 GCU（GCUService 1.0.2.70）**确实不支持运行时切核显模式** ✅
2. 因此 `IGpuOnlyConnectionSwitch_Status = IGPU_ONLY_CONNECT_RB_OFF` **也是真实的** ✅（不是"死字段"）
3. **我的手动指令 `IGPU_ONLY_CONNECT_RB_ON` 是失败的** ✅（与用户最初的判断一致 ✅）
4. ⚠️ **连带影响**：今天恢复的判据回退分支
   `CanOfferIgpuOnly => CanOfferGpuModeSwitch && SupportsDgpuDirect`
   **前提不再成立** —— 它会让 LM **显示一个按了可能没用的按钮** ⚠️
   （比"藏起来"更差：用户会以为能用）
5. ⚠️ 待查：**LM 自己发的 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`** 是否真能切？
   - 证据：11:04:23 `SwitchGpuMode confirmed=True ... action=DGPU_DIRECT_CONNECT_TOGGLE_IGPU` ✅
   - 但 `confirmed` 只是**状态字段回读**（`DiscreteGpuDirectConnectionSwitch_Status` 变成
     `TOGGLE_IGPU`）—— 而当时机器**已经**因 BIOS 处于核显模式 ✅
     -> 无法区分"LM 真的切了"还是"只是读到了 BIOS 已切的状态" ⚠️
   - **唯一可靠的判定法**：不动 BIOS，做一次受控实验：
       ① 读 NVRAM @0x62（应 = 1 Direct）
       ② 在 LM 里点「核显模式」
       ③ 重启
       ④ 读 NVRAM @0x62：变 2(IgpuOnly) + NVIDIA 被屏蔽 => LM 真能切 ✅
                          仍为 1(Direct)               => LM 也不能切 ❌
     （NVRAM 值可在 %APPDATA%\\MechrevoLite\\log.txt 的 `GPU route at startup` 行里看，
       或重启 LM 让它重新读一次）

【待用户决定】
- A) 做上面的受控实验（能给出确定答案）
- B) 直接**把回退分支也撤掉**（回到"只要 GCU 说不支持就藏起来"）
      —— 与用户最初"暂时回退吧 / 30系可以全部屏蔽"的态度一致 ✅，且**不会给出假按钮** ✅
- C) 保留回退分支，但把按钮做成"点了会提示需要 BIOS 设置" ⚠️（工作量更大）
- 未打包
"""

DOCFIX = """

---

# ⚠️ 重要更正（2026-10-05 11:30）

本文 §7 与 §4 里「实测：`on` 有效 —— NVRAM `@0x62` 变为 2、NVIDIA 被屏蔽」的结论 **作废** ❌

**用户澄清**：那次核显模式是**他进 BIOS 手动切的** ✅，不是我发的 `IGPU_ONLY_CONNECT_RB_ON` 生效 ✅

## 更正后的结论

1. **`IGpuOnlyConnectionSwitch_Support = NotSupport` 说的是真话** ✅
   → 本机这个 40 系 GCU（GCUService 1.0.2.70）**确实不支持运行时切核显** ✅
2. `IGpuOnlyConnectionSwitch_Status = IGPU_ONLY_CONNECT_RB_OFF` 也是**真实的** ✅（不是"死字段"）
3. **手动发 `IGPU_ONLY_CONNECT_RB_ON` 是失败的** ✅（与用户最初判断一致）
4. ⚠️ **本文 §4「当前实现」的回退分支前提不再成立**：
   `CanOfferIgpuOnly => CanOfferGpuModeSwitch && SupportsDgpuDirect`
   会显示一个**按了可能没用**的按钮 ⚠️（比藏起来更差）
5. ⚠️ **尚未确认**：LM 自己发的 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU` 是否真能切？
   `confirmed=True` 只是**状态字段回读** ✅，而当时机器**已经**因 BIOS 处于核显模式 ✅
   → 无法区分"真切了"还是"读到了 BIOS 已切的状态"

## 唯一可靠的判定法（不动 BIOS 的受控实验）

```
① 读 NVRAM @0x62         （应 = 1 = Direct）
② 在 LM 里点「核显模式」
③ 重启
④ 再读 NVRAM @0x62：
     变 2(IgpuOnly) + NVIDIA 被屏蔽  => LM 真能切 ✅
     仍为 1(Direct)                  => LM 也不能切 ❌
```

NVRAM 值看 `%APPDATA%\\MechrevoLite\\log.txt` 的 `GPU route at startup: ... NVRAM display byte @0x62=`
（或重启 LM 让它重新读一次）

## 对当前代码的影响

**待用户决定**：撤掉回退分支（回到"GCU 说不支持就藏"）／做受控实验／或保留但加提示
"""

with io.open(LOG, 'a', encoding='utf-8', newline='') as f:
    f.write(LOGENTRY)
with io.open(DOC, 'a', encoding='utf-8', newline='') as f:
    f.write(DOCFIX)
print('log :', len(LOGENTRY.encode('utf-8')), 'bytes')
print('doc :', len(DOCFIX.encode('utf-8')), 'bytes')
