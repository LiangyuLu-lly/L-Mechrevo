# -*- coding: utf-8 -*-
r"""修 1：GPU 温度**不再跟着 LHM 闸门一起死**。

真机证据（诊断包 `0.291.9.8-20261017-180311`）：
    18:02:13  NVML 取到 41℃ ⇒ 已写回门面层 ✅
    18:02:15  42℃
    18:02:28  41℃
    18:03:12  [telemetry] GPU 温度格 = 「—」   ← 44 s 后
⇒ 根因：`ApplySensorPriority` 在 `lhm-update` / `lhm-read` 两道闸门**之后**，
  闸门一失败就 return ⇒ 门面层不再被刷新 ⇒ `KeepAlive`(30 s) 到期 ⇒ 界面「—」。
⇒ 修法：把"无厂商值时的 NVML 温度读取"提到**两道 LHM 闸门之前**（仍排在 `_localSamplingQuarantined`
  之后，⛔ 不破坏原生层防护），独立一步、失败只跳过本步。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\source\MechrevoLite\HardwareControl.cs"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t


def must(c, m):
    if not c:
        raise SystemExit("FAIL: " + m)


# ── ① 在降级隔离闸门之后、LHM 两道闸门之前插入独立一步 ──────────────
OLD = """		// (c) 降级中：一次原生调用都不发。
		if (_localSamplingQuarantined)
		{
			return;
		}
		if (!TryStep(lhmMonitor.Update, "lhm-update"))
"""
NEW = """		// (c) 降级中：一次原生调用都不发。
		if (_localSamplingQuarantined)
		{
			return;
		}
		// ⭐⭐ 2026-10-17（真机诊断包 `0.291.9.8-180311` 定因 · 修「4070 显卡温度又变回 —」）：
		//   证据：18:02:13~28 由 NVML 取到 41/42℃ 并写回门面层 ✅；之后 LHM 两道闸门失败 ⇒
		//     `ApplySensorPriority` 不再执行 ⇒ 门面层不再刷新 ⇒ `KeepAlive`(30 s) 到期 ⇒ 44 s 后界面又「—」❌
		//   ⇒ ⭐ 把「无厂商值时的 NVML 温度」提前到**两道 LHM 闸门之前** ✅
		//     ⚠️ 仍排在 `_localSamplingQuarantined` **之后** ⇒ ⛔ 不破坏"降级期间一次原生调用都不发"的保护 ✅
		//   ⚠️ 独立一步：失败只跳过本步（`countsAsHealthFailure:false` ⇒ ⛔ 不参与健康判定）✅
		TryStep(delegate
		{
			RefreshGpuTempFromNvmlWhenNoVendor();
		}, "gpu-temp-nvml-early", countsAsHealthFailure: false);
		if (!TryStep(lhmMonitor.Update, "lhm-update"))
"""
must(t.count(OLD) == 1, "闸门锚点不唯一")
t = t.replace(OLD, NEW)

# ── ② 新方法：放在 MarkGpuTempRealSourceReady 之前 ─────────────────
ANCHOR = "	/// <summary>\n	/// ⭐⭐（同上）：⭐ 标记\"⭐ **真正的本地来源**（LHM / NVML）已经就绪\" ✅ ⇒ ⭐ <c>nvidia-smi</c>"
must(t.count(ANCHOR) == 1, "MarkGpuTempRealSourceReady 锚点不唯一")

METHOD = """	/// <summary>
	/// ⭐⭐ 2026-10-17（修「4070 GPU 温度已取到却又变回—」）：⭐ **与 LHM 闸门解耦**的 GPU 温度读取 ✅
	///
	/// <para>⚠️⚠️ <b>为什么要提前</b>：原来唯一会写 GPU 温度的是 `ApplySensorPriority`，
	/// 而它排在 `lhm-update` / `lhm-read` **两道闸门之后** ⚠️ ⇒ 闸门一失败就 `return`，
	/// 门面层从此不再刷新 ⇒ 只靠 `KeepAliveGpuTempOnSampleFailure()` 续 30 s ⇒ 之后界面变「—」❌
	/// （真机诊断包 `0.291.9.8-180311` 的时间线正是这样：13~28 s 有值 → 44 s 后「—」）。</para>
	///
	/// <para>⭐ <b>为什么安全</b>：
	/// <list type="bullet">
	/// <item>只读 <see cref="NvmlHelper.GetGpuTemperature" />（⭐ 加固层自带隔离/节流 ✅）；</item>
	/// <item>调用点排在 `_localSamplingQuarantined` **之后** ⇒ ⛔ 降级期间一次原生调用都不发 ✅；</item>
	/// <item>包在 `TryStep(..., countsAsHealthFailure:false)` 里 ⇒ 失败只跳过本步、⛔ 不影响健康判定 ✅；</item>
	/// <item>⭐ **厂商有值时（GCU 机型）直接返回** ⇒ ⛔ 不覆盖厂商值，行为逐位不变 ✅。</item>
	/// </list></para>
	/// </summary>
	private static void RefreshGpuTempFromNvmlWhenNoVendor()
	{
		MechrevoHw hw;
		try
		{
			hw = Program.hw;
		}
		catch
		{
			return;
		}
		if (hw == null || NvmlHelper.Unavailable)
		{
			return;
		}
		// ⭐ 厂商给了值（GCU 机型）⇒ ⛔ 不碰；非宝龙达 + 门面值不是我们写的 ⇒ 老路 ✅
		if (hw.GpuTemp > 0 && !IsGpuTempFacadeWrittenByUs(hw) && !hw.UsesBaolongda)
		{
			return;
		}
		float? temp = NvmlHelper.GetGpuTemperature();
		if (!temp.HasValue || temp.Value <= 0f)
		{
			return; // ⚠️ 待机时 NVML 常返回 0 ⇒ 不写（⛔ 不谎报）✅
		}
		int celsius = (int)Math.Round(temp.Value);
		hw.MarkExternalGpuTemp(celsius);
		// ⭐ 记成"我们写的" ⇒ 下一轮 `ApplySensorPriority` 不会误判成厂商值 ✅
		Interlocked.Exchange(ref _gpuTempFacadeWritten, celsius);
		Interlocked.Exchange(ref _gpuTempExternalGoodTick, Environment.TickCount64);
		Interlocked.Exchange(ref _gpuTempKeepAliveState, 0);
		MarkGpuTempRealSourceReady();
	}

""" + ANCHOR

t = t.replace(ANCHOR, METHOD)
assert "\r\n" not in t
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] HardwareControl.cs -> GPU 温度提前到 LHM 闸门之前（独立一步）")
