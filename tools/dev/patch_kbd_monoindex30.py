# -*- coding: utf-8 -*-
"""
补丁：30 系/通用单分区键盘灯效不生效修复
=====================================================================
根因（2026-10-04 抓包 + 实测确认，见 diagnostics\\30系键盘灯GCU协议_抓包分析.md）：
  SetLightEffect("Keyboard/Ctrl") 的 payload 构造里，"MonochromeIndex" 与
  "ColorBuffer 每项补 ID" 这两件事只写在 isGcu40Keyboard 分支内。
  非 40 系 GCU（30 系）走 single-zone 通用分支 → 这两个字段全缺 →
  整条 SetEffectALL 不被 GCU 采用 → 灯效/颜色不生效。
  （电源走 SetPower、亮度走 SetLightingLevel，是独立函数，故一直正常。）

修法（最小改动）：在 40 系分支之后、single-zone 通用分支之前，插一段
  if (isSingleZoneKeyboard && !isGcu40Keyboard) { 补 MonochromeIndex="33" + 重建带 ID 的 ColorBuffer }
  - 不动 40 系分支（保持既有行为逐字节不变）
  - 不动 powerstatus / nv_save / direction（实测 30 系 GCU 接受 LM 现有取值）
  - 单色类效果：7 段同色 —— 与实测通过的测试 B 完全一致
  - Manual：保留原 6 段色，只补 ID
  - Rainbow/Breathing（7 段七彩）：保留各自颜色，只补 ID

依据：tools\\gcu_kbd_send.py 测试 B（MonochromeIndex=33 + 7 段纯蓝 + ID）
      → GCU 上报 MonochromeIndex 2→33 且键盘变蓝；
      测试 E（nv_save=SAVE / direction=None + ID）→ GCU 上报 33→7，确认字段被接受。
"""
import io
import os
import sys

SRC = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'

ANCHOR = '\t\t\t\tif (isSingleZoneKeyboard)\n'

NEW_BLOCK = '''\t\t\t\t// 2026-10-04：MonochromeIndex("33"=颜色走 ColorBuffer 自定义色) 与 ColorBuffer 每项的 ID
\t\t\t\t// 是单分区 GCU 的公共要求，原先只写在 40 系分支内 → 30 系/其他单分区 GCU 掉进
\t\t\t\t// 通用路径时这两个字段全缺，整条 SetEffectALL 不被采用（灯效/颜色无效；电源走
\t\t\t\t// SetPower、亮度走 SetLightingLevel，属独立函数，故一直正常）。
\t\t\t\t// 已用 tools\\gcu_kbd_send.py 测试 B/E 实测验证（MonochromeIndex 状态 2→33→7）。
\t\t\t\tif (isSingleZoneKeyboard && !isGcu40Keyboard)
\t\t\t\t{
\t\t\t\t\tpayload["MonochromeIndex"] = "33";
\t\t\t\t\tbool isManualEffect = string.Equals(effect, "Manual", StringComparison.Ordinal);
\t\t\t\t\tint szBlocks = (isManualEffect ? 6 : 7);
\t\t\t\t\tvalue["ColorBlocks"] = szBlocks;
\t\t\t\t\tif (value["ColorBuffer"] is object[] cbOldSz)
\t\t\t\t\t{
\t\t\t\t\t\tint r0 = 255, g0 = 0, b0 = 0;
\t\t\t\t\t\tif (cbOldSz.Length > 0 && cbOldSz[0] is Dictionary<string, object> cd0)
\t\t\t\t\t\t{
\t\t\t\t\t\t\tif (cd0.ContainsKey("R")) r0 = Convert.ToInt32(cd0["R"]);
\t\t\t\t\t\t\tif (cd0.ContainsKey("G")) g0 = Convert.ToInt32(cd0["G"]);
\t\t\t\t\t\t\tif (cd0.ContainsKey("B")) b0 = Convert.ToInt32(cd0["B"]);
\t\t\t\t\t\t}
\t\t\t\t\t\tobject[] cb7Sz = new object[szBlocks];
\t\t\t\t\t\tfor (int ci = 0; ci < szBlocks; ci++)
\t\t\t\t\t\t{
\t\t\t\t\t\t\tint r = r0, g = g0, b = b0;
\t\t\t\t\t\t\tif (ci < cbOldSz.Length && cbOldSz[ci] is Dictionary<string, object> cdSz)
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tif (cdSz.ContainsKey("R")) r = Convert.ToInt32(cdSz["R"]);
\t\t\t\t\t\t\t\tif (cdSz.ContainsKey("G")) g = Convert.ToInt32(cdSz["G"]);
\t\t\t\t\t\t\t\tif (cdSz.ContainsKey("B")) b = Convert.ToInt32(cdSz["B"]);
\t\t\t\t\t\t\t}
\t\t\t\t\t\t\tcb7Sz[ci] = new Dictionary<string, object> { ["ID"] = ci, ["R"] = r, ["G"] = g, ["B"] = b };
\t\t\t\t\t\t}
\t\t\t\t\t\tvalue["ColorBuffer"] = cb7Sz;
\t\t\t\t\t}
\t\t\t\t\tLogger.WriteLine("single-zone payload(30/generic): " + System.Text.Json.JsonSerializer.Serialize(payload));
\t\t\t\t}
'''


def main():
    if not os.path.isfile(SRC):
        print('FAIL: 源文件不存在 ' + SRC)
        return 1

    raw = io.open(SRC, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')

    if 'single-zone payload(30/generic)' in text:
        print('SKIP: 补丁已应用过（找到标记字符串）')
        return 0

    n = text.count(ANCHOR)
    if n != 1:
        print('FAIL: 锚点 if (isSingleZoneKeyboard) 命中 %d 次，期望 1 次，未修改' % n)
        return 2

    text = text.replace(ANCHOR, NEW_BLOCK + ANCHOR, 1)

    out = text.encode('utf-8')
    if bom:
        out = b'\xef\xbb\xbf' + out
    io.open(SRC, 'wb').write(out)

    print('OK: 已插入单分区通用分支的 MonochromeIndex + ColorBuffer ID 补丁')
    print('    文件: %s (%d -> %d 字节)' % (SRC, len(raw), len(out)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
