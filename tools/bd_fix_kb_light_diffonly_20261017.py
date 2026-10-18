# -*- coding: utf-8 -*-
r"""修 2：键盘灯**只写变化项**（依据 7.3 与 JiaolongControl 的做法）。

参考事实（只取做法，不抄代码）：
  · 7.3：改颜色 = 写模式 2 → 等 500 ms → 写颜色（**不重写亮度**）；改亮度只写功能号 18
  · JiaolongControl(MIT)：颜色/模式/亮度各自独立下发（SetColor 只写 17，不碰 16/18）
我们原来每次都把 16/17/18 全写一遍 ⇒ 改颜色时顺带重写亮度，固件可能重新应用配置 ⇒ 颜色被顶掉。
真机现象（.8 诊断包）：亮度可调、颜色/效果无反应，而回读 16=2 / 17=255,255,255 / 18=3 全"成功"。

同时把亮度上限从写死的 3 改成 `BaolongdaProfile.BrightnessMax(代数)`（40/50 系 = 4）。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t


def must(c, m):
    if not c:
        raise SystemExit("FAIL: " + m)


OLD = """			// ⭐ ② 先模式（⭐ 功能号 16）—— ⭐ 由调用方按效果 Id 映射（⭐ 单一来源 ✅）
			bool modeOk = wmi.TrySetKeyboardMode(mode);
			// ⭐⭐ 2026-10-17（依据：第三方 JiaoLong7.3 + 官方 40 系 `SetSP_KeyboardColor`）：
			//   ⭐ 两家 40 系实现都是「写模式 2 → **await Task.Delay(500)** → 写颜色」⚠️
			//   ⭐ 而官方 **30 系**是「模式 2 → 立即写颜色」（无延迟）✅
			//   ⇒ ⭐ 40/50 系**必须**在模式与颜色之间等 500ms，否则颜色会被固件丢掉 ❌
			//      （真机现象：彩虹/轮播显示成单一颜色、单色有效 —— 正是颜色没进去）
			//   ⚠️ 30 系不加延迟（与官方 30 系一致）；⛔ 只在写入线程上等，不阻塞 UI ✅
			DgpuGenerationKind kbGen = DgpuGeneration;
			if (kbGen == DgpuGenerationKind.Gen40 || kbGen == DgpuGenerationKind.Gen50)
			{
				Thread.Sleep(500);
			}
			// ⭐ ③ 再颜色（⭐ 功能号 17，⭐ 三字节 R,G,B ✅）
			int r = (colorArgb >> 16) & 0xFF;
			int g = (colorArgb >> 8) & 0xFF;
			int b = colorArgb & 0xFF;
			bool colorOk = wmi.TrySetKeyboardColor((byte)r, (byte)g, (byte)b);
			// ⭐ ④ 最后亮度（⭐ 功能号 18，⭐ 0~3 ✅）
			byte level = (byte)Math.Clamp(brightness0to3, 0, 3);
			bool brightOk = wmi.TrySetKeyboardBrightness(level);
"""

NEW = """			// ⭐⭐ 2026-10-17（按 7.3 与 JiaolongControl 的做法重写"写什么"）：
			//   ⭐ 两家参考都**只写变化的那一项**：
			//     · 7.3：改颜色 = 模式 2 → 等 500 ms → 颜色（**不重写亮度**）；改亮度只写 18 ✅
			//     · JiaolongControl(MIT)：颜色/模式/亮度各自独立下发（SetColor 只写 17）✅
			//   ⚠️ 我们原来**每次都把 16/17/18 全写一遍** ⇒ 改颜色时顺带重写亮度，
			//     固件可能据此重新应用配置 ⇒ 颜色被顶掉
			//     （真机现象：亮度可调、颜色/效果无反应，而 16/17/18 回读全"成功" —— 完全对得上）
			// ⭐ ② 模式（功能号 16）—— **只在变化时写**
			bool modeKnown = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardMode, out byte curMode);
			bool needMode = !modeKnown || curMode != mode;
			bool modeOk = !needMode || wmi.TrySetKeyboardMode(mode);
			//   ⭐ 只在**真的写了模式**之后才等 500 ms（40/50 系官方与 7.3 都是这个时序）✅
			//   ⚠️ 30 系不加延迟（与官方 30 系一致）；⛔ 只在写入线程上等，不阻塞 UI ✅
			DgpuGenerationKind kbGen = DgpuGeneration;
			if (needMode && (kbGen == DgpuGenerationKind.Gen40 || kbGen == DgpuGenerationKind.Gen50))
			{
				Thread.Sleep(500);
			}
			// ⭐ ③ 颜色（功能号 17，三字节 R,G,B）—— 颜色**不可靠回读** ⇒ 每次都写 ✅
			int r = (colorArgb >> 16) & 0xFF;
			int g = (colorArgb >> 8) & 0xFF;
			int b = colorArgb & 0xFF;
			bool colorOk = wmi.TrySetKeyboardColor((byte)r, (byte)g, (byte)b);
			// ⭐ ④ 亮度（功能号 18）—— **只在变化时写**（改颜色时不再重写亮度 ✅）
			//   ⚠️ 上限按代数：Gen40/50 = 4（原厂 Level_4）· 其余 = 3 —— 与 `BaolongdaProfile` 同一事实 ✅
			byte level = (byte)Math.Clamp(brightness0to3, 0, BaolongdaProfile.BrightnessMax(kbGen));
			bool brightKnown = wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte curBright);
			bool needBright = !brightKnown || curBright != level;
			bool brightOk = !needBright || wmi.TrySetKeyboardBrightness(level);
"""
must(t.count(OLD) == 1, "键盘灯写入块锚点不唯一")
t = t.replace(OLD, NEW)

# 日志里把"本次写了哪几项"打出来（下次诊断包直接看）
OLD_LOG = """			Logger.WriteError($"宝龙达 键盘灯：⭐ 下发 rgb=#{r:X2}{g:X2}{b:X2} · 模式={mode} · 亮度={level}"
				+ $" ⇒ ⭐ ① 写成功={colorOk && modeOk && brightOk}（色={colorOk} 模式={modeOk} 亮度={brightOk}）\""""
NEW_LOG = """			Logger.WriteError($"宝龙达 键盘灯：⭐ 下发 rgb=#{r:X2}{g:X2}{b:X2} · 模式={mode} · 亮度={level}"
				+ $" · ⭐ 本次**只写变化项**（模式={needMode} · 颜色=是 · 亮度={needBright}）"
				+ $" ⇒ ⭐ ① 写成功={colorOk && modeOk && brightOk}（色={colorOk} 模式={modeOk} 亮度={brightOk}）\""""
must(t.count(OLD_LOG) == 1, "日志锚点不唯一")
t = t.replace(OLD_LOG, NEW_LOG)

assert "\r\n" not in t
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] MechrevoHw.cs -> 键盘灯只写变化项 + 亮度上限按代数")
