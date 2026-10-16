# -*- coding: utf-8 -*-
import re
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()

def rep(old, new, cnt=1, label=''):
    global t
    assert t.count(old) == cnt, f'anchor mismatch ({t.count(old)} != {cnt}): {label or old[:60]!r}'
    t = t.replace(old, new, cnt)

# 1) QuickSwitchGroups 电源组 12→11 项，删 "startup"
rep('''		(Strings.QuickGroupPower, new string[12]
		{
			"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
			"monitoroff", "startup"
		}),''',
'''		(Strings.QuickGroupPower, new string[11]
		{
			"usb", "highperf", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
			"monitoroff"
		}),''', 1, 'QuickSwitchGroups')

# 2) array2 28→27 项，删 ("startup", Strings.QuickStartup),
rep('''			("monitoroff", Strings.QuickMonitorOff),
			("startup", Strings.QuickStartup),
			("sleepnetoff", Strings.QuickSleepNetOff),''',
'''			("monitoroff", Strings.QuickMonitorOff),
			("sleepnetoff", Strings.QuickSleepNetOff),''', 1, 'array2')
t = t.replace('(string, string)[] array2 = new (string, string)[28]',
              '(string, string)[] array2 = new (string, string)[27]', 1)

# 3) BuildQuickRow startup 分支（1327-1378）整块删除
rep('''				if (key2 == "startup")
				{
					bool? flag3 = Startup.ReadScheduledState();
					cb.Checked = flag3 ?? Startup.IsAutostartPreferred();
					cb.Click += (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							bool flag4 = cb.Checked;
							if (NativeMethods.HasFreshUserInput())
							{
								if (cb.Focused)
								{
									Control parent = cb.Parent;
									if (parent != null && parent.CanFocus)
									{
										parent.Focus();
									}
								}
								cb.Enabled = false;
								try
								{
									if (Startup.ApplyScheduledState(flag4))
									{
										AppConfig.Set("startup_enabled", flag4 ? 1 : 0);
									}
									else
									{
										RevertCheck(cb, Startup.ReadScheduledState() ?? (!flag4));
									}
									return;
								}
								catch (Exception ex)
								{
									Logger.WriteLine("Autostart switch failed: " + ex.Message);
									ToastForm.ShowFailure(Strings.StartupFailed);
									RevertCheck(cb, Startup.ReadScheduledState() ?? (!flag4));
									return;
								}
								finally
								{
									cb.Enabled = true;
								}
							}
							Logger.WriteLine("Autostart switch ignored: no fresh user input.");
							RevertCheck(cb, Startup.ReadScheduledState() ?? (!flag4));
						}
					};
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}
''', '', 1, 'BuildQuickRow startup')

# 4) RefreshQuickSwitch startup 分支
rep('''				else if (text == "startup")
				{
					if (_quickGroup == null || _quickGroup.Expanded)
					{
						bool? flag2 = Startup.ReadScheduledState();
						if (flag2.HasValue)
						{
							checkBox.Checked = flag2.Value;
						}
					}
				}
''', '', 1, 'RefreshQuickSwitch startup')

# 5) GetQuickSwitchState startup 分支
rep('''		if (key == "startup")
		{
			return Startup.ReadScheduledState();
		}
''', '', 1, 'GetQuickSwitchState startup')

# 6) capability switch case "startup"
rep('''			case "monitoroff":
			case "taskbarautohide":
			case "transparency":
			case "darktheme":
			case "startup":
			case "sleepnetoff":''',
'''			case "monitoroff":
			case "taskbarautohide":
			case "transparency":
			case "darktheme":
			case "sleepnetoff":''', 1, 'case startup')

open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsForm.cs startup quick-item removed (6 spots)')
