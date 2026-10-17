# -*- coding: utf-8 -*-
import io, sys
P = r'D:\openLM\source\MechrevoLite\FanCurveForm.cs'
raw = io.open(P, encoding='utf-8', newline='').read()
had_crlf = '\r\n' in raw
t = raw.replace('\r\n', '\n')

a = "\t\t\t\t\tChannel0Temperatures = TempsFromBytes(_gpuPanel.Temps),\n"
b = "\t\t\t\t\tChannel0Temperatures = prev.Channel0Temperatures,\n"
c = "\t\t\t\t\tChannel1Temperatures = TempsFromBytes(_cpuPanel.Temps),\n"
d = "\t\t\t\t\tChannel1Temperatures = prev.Channel1Temperatures,\n"
n1 = t.count(a); n2 = t.count(c)
if n1 != 1 or n2 != 1:
    print('ABORT', n1, n2); sys.exit(1)
t = t.replace(a, b).replace(c, d)

# 同时删掉不再使用的 TempsFromBytes（避免 CS0169/未使用警告？其实静态方法不会警告，但保持整洁）
old_fn = (
"\t/// <summary>温度数组 byte[] -> int[]。</summary>\n"
"\tprivate static int[] TempsFromBytes(byte[] temps)\n"
"\t{\n"
"\t\tint[] r = new int[temps.Length];\n"
"\t\tfor (int i = 0; i < temps.Length; i++)\n"
"\t\t{\n"
"\t\t\tr[i] = temps[i];\n"
"\t\t}\n"
"\t\treturn r;\n"
"\t}\n"
"\n"
)
if t.count(old_fn) == 1:
    t = t.replace(old_fn, "")

out = t.replace('\n', '\r\n') if had_crlf else t
io.open(P, 'w', encoding='utf-8', newline='').write(out)
print('OK 温度点改用 prev 存档值，已移除 TempsFromBytes')