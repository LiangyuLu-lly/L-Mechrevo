# -*- coding: utf-8 -*-
r"""WiFi / 蓝牙 本地开关（Windows.Devices.Radios）接线。

四处：
  ① `MechrevoHw.SupportsQuickSwitch`：宝龙达下 wifi/bt 改看 Radio 是否可用（找不到 ⇒ 仍隐藏）
  ② `MechrevoService.SwitchQuick`：宝龙达下 wifi/bt 分派给 `RadioSwitch.SetAsync`（写后回读）
  ③ `MechrevoHw.RefreshBaolongdaSwitchStates`：每 3 s 把 Radio 真实状态同步进 QuickSwitches
  ④ `MechrevoHw.ApplyBaolongdaBackendAsync`：启动 `RadioSwitch` 的后台轮询
⛔ GCU 机型：四处都带 `UsesBaolongda` 判据 ⇒ 行为逐位不变。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def load(p):
    with open(p, "rb") as f:
        raw = f.read()
    assert not raw.startswith(b"\xef\xbb\xbf"), "有 BOM: " + p
    t = raw.decode("utf-8")
    assert "\r\n" not in t, "不是纯 LF: " + p
    return t


def save(p, t):
    assert "\r\n" not in t
    with open(p, "wb") as f:
        f.write(t.encode("utf-8"))


def sub(t, old, new, what):
    n = t.count(old)
    if n != 1:
        raise SystemExit("FAIL: %s 锚点 %d 处" % (what, n))
    return t.replace(old, new)


# ── MechrevoHw.cs ─────────────────────────────────────────────────
P1 = r"D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs"
hw = load(P1)

hw = sub(hw,
         '			"wifi" => WifiSeen, ',
         '			"wifi" => WifiSeen || (UsesBaolongda && RadioSwitch.Available(RadioSwitchKind.Wifi)), ',
         "SupportsQuickSwitch(wifi)")

hw = sub(hw,
         '			"bt" => BluetoothSeen, ',
         '			"bt" => BluetoothSeen || (UsesBaolongda && RadioSwitch.Available(RadioSwitchKind.Bluetooth)), ',
         "SupportsQuickSwitch(bt)")

hw = sub(hw,
         '			SyncBaolongdaQuickSwitch("fnkey", snap.FnLock);',
         '''			SyncBaolongdaQuickSwitch("fnkey", snap.FnLock);
			// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地 Radio）：⭐ 宝龙达没有 GCU ⇒ 这两个状态由
			//   Windows 的 Radio 提供（⭐ 依据：40 系 GCU 反编译里也是 `Windows.Devices.Radios` ✅）。
			//   ⚠️ 只同步**读到**的；读不到就不动界面（⭐ 保持"未知"比谎报好 ✅）
			if (RadioSwitch.Available(RadioSwitchKind.Wifi) && RadioSwitch.TryGet(RadioSwitchKind.Wifi, out bool wifiOn, out string wifiNote))
			{
				if (!QuickSwitches.TryGetValue("wifi", out bool wifiCur) || wifiCur != wifiOn)
				{
					QuickSwitches["wifi"] = wifiOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ wifi ⇒ {(wifiOn ? "开" : "关")}（⭐ 来源：Windows Radio · {wifiNote} ✅）");
				}
			}
			if (RadioSwitch.Available(RadioSwitchKind.Bluetooth) && RadioSwitch.TryGet(RadioSwitchKind.Bluetooth, out bool btOn, out string btNote))
			{
				if (!QuickSwitches.TryGetValue("bt", out bool btCur) || btCur != btOn)
				{
					QuickSwitches["bt"] = btOn;
					Logger.WriteLine($"宝龙达状态回读：⭐ bt ⇒ {(btOn ? "开" : "关")}（⭐ 来源：Windows Radio · {btNote} ✅）");
				}
			}''',
         "RefreshBaolongdaSwitchStates 同步 wifi/bt")

hw = sub(hw,
         "		Capabilities = backend.BuildCapabilities();",
         """		Capabilities = backend.BuildCapabilities();
		// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地开关）：⭐ 启动 Windows Radio 的后台轮询（3 s 一次）
		//   ⚠️ 只在宝龙达后端启动 ⇒ GCU 机型**不会**跑这个轮询（行为逐位不变 ✅）
		RadioSwitch.Start();""",
         "ApplyBaolongdaBackendAsync 启动 Radio 轮询")

save(P1, hw)
print("[OK] MechrevoHw.cs")

# ── MechrevoService.cs ────────────────────────────────────────────
P2 = r"D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs"
sv = load(P2)

OLD = """			// ⭐⭐ 2026-10-16（宝龙达 · OSD 提示）：⭐ 走**本地实现**（⭐ 它连 WMI 都不用 ✅）"""
NEW = """			// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地开关）：⭐ 宝龙达没有 GCU ⇒ 走 Windows Radio ✅
			//   ⭐ 依据（40 系 GCU 反编译，只取做法）：`DeviceSwitchItem.cs` 用 `Windows.Devices.Radios` 切这两个 ✅
			//   ⚠️ 仅 `UsesBaolongda` 分派 ⇒ GCU 机型的 `WIFI_ON/OFF`、`BT_ON/OFF` 那条 MQTT 路径**逐位不变** ✅
			if (_hw.UsesBaolongda
				&& (string.Equals(key, "wifi", StringComparison.Ordinal) || string.Equals(key, "bt", StringComparison.Ordinal)))
			{
				RadioSwitchKind radioKind = string.Equals(key, "wifi", StringComparison.Ordinal)
					? RadioSwitchKind.Wifi
					: RadioSwitchKind.Bluetooth;
				if (RadioSwitch.Available(radioKind))
				{
					(bool radioOk, string radioNote) = await RadioSwitch.SetAsync(radioKind, on).ConfigureAwait(continueOnCapturedContext: false);
					Logger.WriteLine($"SwitchQuick({key},{on}) ⭐ 走本地 Radio（Windows.Devices.Radios）⇒ 确认={radioOk}（{radioNote}）");
					return radioOk;
				}
				Logger.WriteLine($"SwitchQuick({key},{on})：⚠️ 本地 Radio 不可用（未探测到或无访问权限）⇒ 不下发");
				return false;
			}
			// ⭐⭐ 2026-10-16（宝龙达 · OSD 提示）：⭐ 走**本地实现**（⭐ 它连 WMI 都不用 ✅）"""
sv = sub(sv, OLD, NEW, "SwitchQuick 分派 wifi/bt")
save(P2, sv)
print("[OK] MechrevoService.cs")
print("DONE")
