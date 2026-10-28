# -*- coding: utf-8 -*-
r"""
改 GCU 选择逻辑（用户要求）
================================================================================
目标选择表：
    50 系 / 未知(无法识别)  -> 50 系 GCU
    40 系                   -> 40 系 GCU
    30 系                   -> 30 系 GCU
    20-10 系（含 16 系）    -> 30 系 GCU（暂时）

现状（Select-GcuPayload.ps1）：
    50 -> 50 ; 30 -> 30 ; 其它(40/unknown) -> 40      <- 40 与 unknown 混在一起
    且无法区分"10/16/20 系"和"真未知"，两者都是 'unknown'

改法：
  1. `$deviceClasses` 增加 'legacy'（Pascal/Turing 高位：0x1B00/1C00/1D00/1E00/1F00/2000/2100）
  2. 名称匹配增加 legacy 规则（GTX / RTX 16-20 / MX）
  3. 代际遍历数组加入 'legacy'
  4. `$key` 改为显式五分支：50->50, 40->40, 30->30, legacy->30, 其余(unknown)->50
  5. Reason 文案 + 文档注释 + SelfTest 用例同步更新
行尾：Select-GcuPayload.ps1 需实测（脚本按两套行尾自动适配）
"""
import io
import os
import sys

F = r'D:\openLM\installer-src\files\GCU\Select-GcuPayload.ps1'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# ---- 1) deviceClasses 增加 legacy ----
op(b"        '30' = @(0x2200, 0x2400, 0x2500)                    # Ampere (GA10x)\n",
   b"        '30' = @(0x2200, 0x2400, 0x2500)                    # Ampere (GA10x)\n"
   b"        # Pre-30-series NVIDIA (Pascal / Turing): 10 / 16 / 20 series.\n"
   b"        # These temporarily get the 30-series patch (see the selection table).\n"
   b"        'legacy' = @(0x1B00, 0x1C00, 0x1D00, 0x1E00, 0x1F00, 0x2000, 0x2100)\n",
   'deviceClasses 增加 legacy')

# ---- 2) 名称匹配增加 legacy 规则 ----
op(b"        if ($generation -ne 'unknown') { break }\n"
   b"    }\n"
   b"\n"
   b"    if ($generation -eq 'unknown') {\n"
   b"        foreach ($id in @($Ids)) {\n",
   b"        if ($generation -eq 'unknown' -and $name -match '(?i)\\b(GTX|RTX\\s*(16|20)\\d0|MX\\d{3})\\b') {\n"
   b"            $generation = 'legacy'\n"
   b"            $evidence.Add(\"gpu-name '$name' is a pre-30-series NVIDIA part (GTX / RTX 16-20 / MX)\")\n"
   b"        }\n"
   b"        if ($generation -ne 'unknown') { break }\n"
   b"    }\n"
   b"\n"
   b"    if ($generation -eq 'unknown') {\n"
   b"        foreach ($id in @($Ids)) {\n",
   '名称匹配增加 legacy')

# ---- 3) 代际遍历数组加入 legacy ----
op(b"            foreach ($generationKey in @('50', '40', '30')) {\n"
   b"                if ($deviceClasses[$generationKey] -contains $high) {\n",
   b"            foreach ($generationKey in @('50', '40', '30', 'legacy')) {\n"
   b"                if ($deviceClasses[$generationKey] -contains $high) {\n",
   'device-id 遍历加入 legacy')

# ---- 4) $key 五分支 + Reason ----
op(b"    # 50-series -> 50 trio; 30-series -> 30 patch (30-series EC does not match the\n"
   b"    # 40-series trio, which is known buggy on 30-series machines); 40/unknown -> 40 trio.\n"
   b"    $key = if ($detected.Generation -eq '50') { '50' }\n"
   b"           elseif ($detected.Generation -eq '30') { '30' }\n"
   b"           else { '40' }\n"
   b"    $entry = $script:PayloadEntries[$key]\n"
   b"    $reason = switch ($detected.Generation) {\n"
   b"        '50' { '50-series detected; using 50-series GCU service trio' }\n"
   b"        '30' { '30-series detected; using 30-series GCU service patch (native EC)' }\n"
   b"        default { ('non-50/30-series ({0}); using 40-series GCU service trio (mixed-GCU)' -f $detected.Generation) }\n"
   b"    }\n",
   b"    # Selection table (user-specified 2026-10-05):\n"
   b"    #   50-series              -> 50 trio\n"
   b"    #   40-series              -> 40 trio\n"
   b"    #   30-series              -> 30 patch\n"
   b"    #   10/16/20-series        -> 30 patch  (temporary; no dedicated pre-30 payload yet)\n"
   b"    #   unresolved / no dGPU   -> 50 trio   (safety floor)\n"
   b"    $key = if ($detected.Generation -eq '50') { '50' }\n"
   b"           elseif ($detected.Generation -eq '40') { '40' }\n"
   b"           elseif ($detected.Generation -eq '30') { '30' }\n"
   b"           elseif ($detected.Generation -eq 'legacy') { '30' }\n"
   b"           else { '50' }\n"
   b"    $entry = $script:PayloadEntries[$key]\n"
   b"    $reason = switch ($detected.Generation) {\n"
   b"        '50' { '50-series detected; using 50-series GCU service trio' }\n"
   b"        '40' { '40-series detected; using 40-series GCU service trio (mixed-GCU)' }\n"
   b"        '30' { '30-series detected; using 30-series GCU service patch (native EC)' }\n"
   b"        'legacy' { 'pre-30-series NVIDIA (10/16/20) detected; using 30-series GCU service patch (temporary)' }\n"
   b"        default { ('generation unresolved ({0}); using 50-series GCU service trio (safety floor)' -f $detected.Generation) }\n"
   b"    }\n",
   '$key 五分支 + Reason')

# ---- 5) fallback 注释 ----
op(b"    # adapter of unknown generation (e.g. RTX 20/16 or odd naming) keeps the 40 trio.\n",
   b"    # adapter whose generation cannot be resolved stays 'unknown' -> 50-series trio.\n",
   'fallback 注释更正')

# ---- 6) SelfTest 用例 ----
op(b"    @{ Name = 'GTX 1650 unknown-gen -> 40 trio';    Args = @{ DeviceId = @('PCI\\VEN_10DE&DEV_1F91'); GpuName = @('NVIDIA GeForce GTX 1650') }; Expect = '40' }\n",
   b"    @{ Name = 'GTX 1650 (16-series) -> 30 patch';   Args = @{ DeviceId = @('PCI\\VEN_10DE&DEV_1F91'); GpuName = @('NVIDIA GeForce GTX 1650') }; Expect = '30' },\n"
   b"    @{ Name = 'RTX 2060 (20-series) -> 30 patch';   Args = @{ DeviceId = @('PCI\\VEN_10DE&DEV_1F15'); GpuName = @('NVIDIA GeForce RTX 2060') }; Expect = '30' },\n"
   b"    @{ Name = 'unresolved NVIDIA id -> 50 trio';    Args = @{ DeviceId = @('PCI\\VEN_10DE&DEV_9999') }; Expect = '50' }\n",
   'SelfTest 用例')

# ---- 7) 头部文档 ----
op(b"      payload\\40  - the 40-series service trio (mixed-GCU fix; overlaid for 40/unknown).\n",
   b"      payload\\40  - the 40-series service trio (mixed-GCU fix; overlaid for 40-series).\n",
   '头部文档 payload\\40 说明')
op(b"    Selection: 50-series GPU -> payload\\50 trio; 30-series GPU -> payload\\30 patch;\n"
   b"    anything else (40 / unknown) -> payload\\40 trio. The base tree payload\\GCU is always\n"
   b"    installed first, then the chosen trio is copied over it.\n",
   b"    Selection (user-specified): 50-series or unresolved -> payload\\50 trio;\n"
   b"    40-series -> payload\\40 trio; 30-series -> payload\\30 patch;\n"
   b"    10/16/20-series (pre-30 nvidia) -> payload\\30 patch for now.\n"
   b"    The base tree payload\\GCU is always installed first, then the chosen trio is copied over it.\n",
   '头部文档 Selection')


def main():
    data = io.open(F, 'rb').read()
    if b"'legacy'" in data:
        print('已经应用过了，无需重复。')
        return 0
    resolved = []
    errors = []
    for old, new, expect, label in OPS:
        if data.count(old) == expect:
            resolved.append((old, new, expect, label))
            continue
        alt_old = old.replace(b'\r\n', b'\n') if b'\r\n' in old else old.replace(b'\n', b'\r\n')
        alt_new = new.replace(b'\r\n', b'\n') if b'\r\n' in new else new.replace(b'\n', b'\r\n')
        if data.count(alt_old) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((alt_old, alt_new, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(alt_old), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in resolved:
        data = data.replace(old, new, expect)
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
