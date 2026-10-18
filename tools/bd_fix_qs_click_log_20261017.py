# -*- coding: utf-8 -*-
r"""修 3（诊断增强）：快捷开关点击链路加"到没到 / 被谁挡"的日志。

真机事实（.8 诊断包）：点 Fn 键开关后，日志里**一条 `SwitchQuick` 都没有** ⇒ 点击没到服务层。
但现有代码在**入口**（`cb.CheckedChanged`）**没有任何日志** ⇒ 无法判断是
`_syncingSwitches`、`hw.IsConnected`、还是服务为空把它挡了。
⇒ 本步**只加日志**（不改任何行为），下一次诊断包就能直接指出是哪一行挡的。

⚠️ 与 7.3 / JiaolongControl 对照后的结论：
  · 7.3：Fn 锁就是一个 ToggleButton ⇒ 直接 `SetValue(FnLock, ON/OFF)`（无中间层）
  · JiaolongControl：只消费 `FnState` 事件做 OSD，**不提供软件开关**（靠 Fn 键本身）
  ⇒ 我们走 WMI 功能号 11 的方向没问题（与 7.3 一致）⇒ 问题在"点击没进来"，不在协议。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\source\MechrevoLite\SettingsForm.cs"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t

OLD = """				else
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							_lastQuickSwitchUi = DateTime.Now;
							if (Program.service != null)
							{
								MechrevoHw hw = Program.hw;
								if (hw != null && hw.IsConnected)
								{
									await ApplyQuickSwitchAsync(cb, (bool requested) => Program.service.SwitchQuick(key2, requested));
								}
							}
						}
					};
				}
"""
NEW = """				else
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						// ⭐⭐ 2026-10-17（诊断增强 · 只加日志、⛔ 不改行为）：
						//   真机诊断包显示「点 Fn 键开关后没有任何 SwitchQuick 日志」⇒ 点击没到服务层。
						//   这里把"到没到 / 被谁挡"逐条记下来，下一次诊断包即可定因 ✅
						if (_syncingSwitches)
						{
							Logger.WriteLineIfChanged("qs-click-syncing-" + key2,
								$"快捷开关点击（{key2}）：⚠️ 被 _syncingSwitches 挡下（界面正在按硬件状态回写）⇒ 本次忽略");
							return;
						}
						_lastQuickSwitchUi = DateTime.Now;
						MechrevoHw hw = Program.hw;
						Logger.WriteLine($"快捷开关点击：{key2} → {(cb.Checked ? "开" : "关")}"
							+ $"（hw={(hw == null ? "空" : (hw.IsConnected ? "已连接" : "未连接"))}"
							+ $" · service={(Program.service == null ? "空" : "有")}）");
						if (Program.service == null)
						{
							Logger.WriteLineIfChanged("qs-click-nosvc-" + key2, $"快捷开关点击（{key2}）：⚠️ 服务为空 ⇒ 不下发");
							return;
						}
						if (hw != null && hw.IsConnected)
						{
							await ApplyQuickSwitchAsync(cb, (bool requested) => Program.service.SwitchQuick(key2, requested));
						}
						else
						{
							Logger.WriteLineIfChanged("qs-click-nohw-" + key2,
								$"快捷开关点击（{key2}）：⚠️ hw 空或未连接 ⇒ 不下发（界面会按硬件真值回拨）");
						}
					};
				}
"""
if t.count(OLD) != 1:
    raise SystemExit("FAIL: 点击处理锚点不唯一（%d 处）" % t.count(OLD))
t = t.replace(OLD, NEW)

assert "\r\n" not in t
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] SettingsForm.cs -> 快捷开关点击链路加日志")
