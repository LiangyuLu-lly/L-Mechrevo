# -*- coding: utf-8 -*-
r"""撤回「快捷开关点击」诊断日志（机主复测：Fn 键**本来就能用**，此前的"无效果"是误判）。

只回滚 `SettingsForm` 里那段加进去的日志，恢复成改动前的处理器（行为与 646db16 之前完全一致）。
⚠️ 键盘灯「只写变化项」的修复**保留**（那是另一处、且真机症状成立）。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\source\MechrevoLite\SettingsForm.cs"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t

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
if t.count(NEW) != 1:
    raise SystemExit("FAIL: 回滚锚点不唯一（%d 处）" % t.count(NEW))
t = t.replace(NEW, OLD)

assert "\r\n" not in t
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] SettingsForm.cs -> 已撤回快捷开关诊断日志（恢复原处理器）")
