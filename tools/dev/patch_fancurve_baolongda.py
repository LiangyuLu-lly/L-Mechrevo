# -*- coding: utf-8 -*-
# 给 FanCurveForm 加宝龙达分支（修正版：正确缩进 3/4 tab + 2 tab，CRLF 归一后写回）
import io, sys

P = r'D:\openLM\source\MechrevoLite\FanCurveForm.cs'
raw = io.open(P, encoding='utf-8', newline='').read()
had_crlf = '\r\n' in raw
t = raw.replace('\r\n', '\n')          # 统一成 LF 做替换
fails = []

# ── ① 读取：OnCurveUpdated（MechrevoHw hw = 3 tab，内部 4 tab）──────────
old1 = (
"\t\t\tMechrevoHw hw = Program.hw;\n"
"\t\t\tif (hw != null)\n"
"\t\t\t{\n"
"\t\t\t\t_cpuPanel.Load(hw.CpuCurveUpT, hw.CpuCurveDuty);\n"
"\t\t\t\t_gpuPanel.Load(hw.GpuCurveUpT, hw.GpuCurveDuty);\n"
"\t\t\t\t_status.Text = Strings.FanTablePrefix + (string.IsNullOrEmpty(hw.TableName) ? Strings.Unknown : hw.TableName);\n"
"\t\t\t}\n"
)
new1 = (
"\t\t\tMechrevoHw hw = Program.hw;\n"
"\t\t\tif (hw != null)\n"
"\t\t\t{\n"
"\t\t\t\t// ** 2026-10-17（风扇曲线接入）：** 宝龙达**没有固件曲线表** \u26a0\ufe0f\n"
"\t\t\t\t//   => 它走的是**软件闭环**（读温度 -> 查曲线 -> 写功能号 21），数据存 `BaolongdaFanCurveStore`\n"
"\t\t\t\t//   => 本窗复用同一套绘制控件，只是把 **RPM 换算成占空比 %** 显示\n"
"\t\t\t\t//   \u26a0\ufe0f 通道映射（owner 已确认）：`FanType 0`（大扇）= **GPU** <-> `_gpuPanel`\n"
"\t\t\t\t//      · `FanType 1`（小扇）= **CPU** <-> `_cpuPanel`\n"
"\t\t\t\tif (hw.UsesBaolongda)\n"
"\t\t\t\t{\n"
"\t\t\t\t\tBaolongdaFanCurveSettings s = hw.LoadBaolongdaFanCurve();\n"
"\t\t\t\t\t_cpuPanel.Load(TempsToBytes(s.Channel1Temperatures), RpmsToPercent(s.Channel1Rpms, s.MinRpm, s.MaxRpm));\n"
"\t\t\t\t\t_gpuPanel.Load(TempsToBytes(s.Channel0Temperatures), RpmsToPercent(s.Channel0Rpms, s.MinRpm, s.MaxRpm));\n"
"\t\t\t\t\t_status.Text = \"风扇曲线：软件闭环（宝龙达）\"\n"
"\t\t\t\t\t\t+ (s.Enabled ? \" · 已启用\" : \" · 未启用\")\n"
"\t\t\t\t\t\t+ $\" · 范围 {s.MinRpm}~{s.MaxRpm} RPM\";\n"
"\t\t\t\t\treturn;\n"
"\t\t\t\t}\n"
"\t\t\t\t_cpuPanel.Load(hw.CpuCurveUpT, hw.CpuCurveDuty);\n"
"\t\t\t\t_gpuPanel.Load(hw.GpuCurveUpT, hw.GpuCurveDuty);\n"
"\t\t\t\t_status.Text = Strings.FanTablePrefix + (string.IsNullOrEmpty(hw.TableName) ? Strings.Unknown : hw.TableName);\n"
"\t\t\t}\n"
)
if t.count(old1) == 1:
    t = t.replace(old1, new1)
else:
    fails.append(('读取分支', t.count(old1)))

# ── ② 保存：FlushPendingAsync（MechrevoHw hw = 2 tab）──────────────────
old2 = (
"\t\tMechrevoHw hw = Program.hw;\n"
"\t\t// 2026-10-15（C-1 第 2 步分类 ①）：本函数是「保存风扇曲线到 GCU」⇒ 真正需要 GCU。\n"
"\t\tif (hw == null || !hw.IsGcuConnected)\n"
"\t\t{\n"
"\t\t\t_status.Text = Strings.NotConnectedNotSaved;\n"
"\t\t\treturn;\n"
"\t\t}\n"
)
new2 = (
"\t\tMechrevoHw hw = Program.hw;\n"
"\t\t// ** 2026-10-17（风扇曲线接入）：** 宝龙达走**软件闭环**，不经过 GCU/MQTT \u26a0\ufe0f\n"
"\t\t//   => 把占空比 % 换算回 RPM 后存进 `BaolongdaFanCurveStore`，并**立即应用一次**\n"
"\t\t//   \u26a0\ufe0f 非宝龙达 => 下面的 GCU 逻辑**逐位不变**\n"
"\t\tif (hw != null && hw.UsesBaolongda)\n"
"\t\t{\n"
"\t\t\tBaolongdaFanCurveSettings prev = hw.LoadBaolongdaFanCurve();\n"
"\t\t\t_cpuPending = (_gpuPending = false);\n"
"\t\t\ttry\n"
"\t\t\t{\n"
"\t\t\t\tint[] cpuPercent = _cpuPanel.Duties.ToArray();\n"
"\t\t\t\tint[] gpuPercent = _gpuPanel.Duties.ToArray();\n"
"\t\t\t\tBaolongdaFanCurveSettings next = new BaolongdaFanCurveSettings\n"
"\t\t\t\t{\n"
"\t\t\t\t\tEnabled = true,\n"
"\t\t\t\t\tHysteresisC = prev.HysteresisC,\n"
"\t\t\t\t\tIntervalMs = prev.IntervalMs,\n"
"\t\t\t\t\tMinRpm = prev.MinRpm,\n"
"\t\t\t\t\tMaxRpm = prev.MaxRpm,\n"
"\t\t\t\t\tChannel0Temperatures = TempsFromBytes(_gpuPanel.Temps),\n"
"\t\t\t\t\tChannel0Rpms = PercentToRpms(gpuPercent, prev.MinRpm, prev.MaxRpm),\n"
"\t\t\t\t\tChannel1Temperatures = TempsFromBytes(_cpuPanel.Temps),\n"
"\t\t\t\t\tChannel1Rpms = PercentToRpms(cpuPercent, prev.MinRpm, prev.MaxRpm),\n"
"\t\t\t\t};\n"
"\t\t\t\tbool ok = hw.SaveBaolongdaFanCurve(next);\n"
"\t\t\t\t_status.Text = ok ? (\"风扇曲线已保存并启用 · \" + DateTime.Now.ToString(\"HH:mm:ss\")) : \"风扇曲线保存失败\";\n"
"\t\t\t\tRaiseFanCurveSaved(confirmed: ok);\n"
"\t\t\t}\n"
"\t\t\tcatch (Exception exb)\n"
"\t\t\t{\n"
"\t\t\t\tLogger.WriteLine(\"宝龙达风扇曲线保存失败: \" + exb.Message);\n"
"\t\t\t\t_status.Text = \"风扇曲线保存失败\";\n"
"\t\t\t\tRaiseFanCurveSaved(confirmed: false);\n"
"\t\t\t}\n"
"\t\t\treturn;\n"
"\t\t}\n"
"\t\t// 2026-10-15（C-1 第 2 步分类 ①）：本函数是「保存风扇曲线到 GCU」⇒ 真正需要 GCU。\n"
"\t\tif (hw == null || !hw.IsGcuConnected)\n"
"\t\t{\n"
"\t\t\t_status.Text = Strings.NotConnectedNotSaved;\n"
"\t\t\treturn;\n"
"\t\t}\n"
)
if t.count(old2) == 1:
    t = t.replace(old2, new2)
else:
    fails.append(('保存分支', t.count(old2)))

# ── ③ 换算辅助方法（CurveMatches 之前，2 tab 成员）─────────────────────
old3 = "\tprivate static bool CurveMatches(int[] requested, byte[] actual, byte[] temperatures)"
new3 = (
"\t// ══════════════════════════════════════════════════════════════════════\n"
"\t// ** 2026-10-17（风扇曲线接入 · 宝龙达）：RPM 与占空比 % 的换算\n"
"\t//   \u26a0\ufe0f 为什么能这样换算：宝龙达**没有可靠的通道 max RPM**\n"
"\t//      => 用存档里的 `MinRpm` / `MaxRpm` 当分母（**我们设的**边界，不是硬件上限）\n"
"\t//   \u26a0\ufe0f 精度：16 点 x 0~100% => RPM 步进 ≈ (Max-Min)/100，可接受\n"
"\t// ══════════════════════════════════════════════════════════════════════\n"
"\n"
"\t/// <summary>温度数组 int[] -> byte[]（绘制控件口径；超界夹到 0~254）。</summary>\n"
"\tprivate static byte[] TempsToBytes(int[] temps)\n"
"\t{\n"
"\t\tbyte[] r = new byte[temps.Length];\n"
"\t\tfor (int i = 0; i < temps.Length; i++)\n"
"\t\t{\n"
"\t\t\tr[i] = (byte)Math.Clamp(temps[i], 0, 254);\n"
"\t\t}\n"
"\t\treturn r;\n"
"\t}\n"
"\n"
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
"\t/// <summary>RPM 数组 -> 占空比 %（0~100）。\u26a0\ufe0f 分母是我们设的 Min/Max，不是硬件上限。</summary>\n"
"\tprivate static byte[] RpmsToPercent(int[] rpms, int minRpm, int maxRpm)\n"
"\t{\n"
"\t\tbyte[] r = new byte[rpms.Length];\n"
"\t\tint span = Math.Max(1, maxRpm - minRpm);\n"
"\t\tfor (int i = 0; i < rpms.Length; i++)\n"
"\t\t{\n"
"\t\t\tint pct = (int)Math.Round((rpms[i] - minRpm) * 100.0 / span);\n"
"\t\t\tr[i] = (byte)Math.Clamp(pct, 0, 100);\n"
"\t\t}\n"
"\t\treturn r;\n"
"\t}\n"
"\n"
"\t/// <summary>占空比 %（0~100）-> RPM 数组。</summary>\n"
"\tprivate static int[] PercentToRpms(int[] percent, int minRpm, int maxRpm)\n"
"\t{\n"
"\t\tint[] r = new int[percent.Length];\n"
"\t\tint span = Math.Max(1, maxRpm - minRpm);\n"
"\t\tfor (int i = 0; i < percent.Length; i++)\n"
"\t\t{\n"
"\t\t\tint pct = Math.Clamp(percent[i], 0, 100);\n"
"\t\t\tr[i] = minRpm + (int)Math.Round(pct * span / 100.0);\n"
"\t\t}\n"
"\t\treturn r;\n"
"\t}\n"
"\n"
"\tprivate static bool CurveMatches(int[] requested, byte[] actual, byte[] temperatures)"
)
if t.count(old3) == 1:
    t = t.replace(old3, new3)
else:
    fails.append(('换算方法', t.count(old3)))

if fails:
    print('ABORT:', fails)
    sys.exit(1)

out = t.replace('\n', '\r\n') if had_crlf else t
io.open(P, 'w', encoding='utf-8', newline='').write(out)
print('OK 已改 FanCurveForm.cs (原行尾 CRLF=%s)' % had_crlf)
