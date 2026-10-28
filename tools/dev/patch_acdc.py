# -*- coding: utf-8 -*-
"""睡眠省电：AC/DC 分档切换 + ToolTip 悬停解释"""
import io

SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
PG = r'D:\openLM\source\MechrevoLite.Helpers\PowerSleepGuard.cs'

with io.open(SF, 'r', encoding='utf-8') as f:
    s = f.read()
with io.open(PG, 'r', encoding='utf-8') as f:
    p = f.read()

def rep(src, old, new, label, cnt=1):
    n = src.count(old)
    assert n == cnt, 'expect %d match for [%s], got %d' % (cnt, label, n)
    return src.replace(old, new)

# ---------- PowerSleepGuard.cs ----------
# ReadEnabled 分档
p = rep(p, '''    /// <summary>回读开关状态：AC 与 DC 都等于开启值才算开。</summary>
    public static bool? ReadEnabled(string key)
    {
        int idx = IndexOf(key);
        if (idx < 0)
        {
            return null;
        }
        string? scheme = GetActiveScheme();
        if (scheme == null)
        {
            return null;
        }
        (string sub, string setting, int onValue, _) = Options[idx];
        string path = SettingPath(scheme, sub, setting);
        try
        {
            using RegistryKey? rk = Registry.LocalMachine.OpenSubKey(path);
            object? ac = rk?.GetValue("ACSettingIndex");
            object? dc = rk?.GetValue("DCSettingIndex");
            if (ac is int a && dc is int d)
            {
                return a == onValue && d == onValue;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("PowerSleepGuard: read enabled failed at " + path + ": " + ex.Message);
        }
        return null;
    }''',
'''    /// <summary>回读开关状态（指定插电/离电档）。</summary>
    public static bool? ReadEnabled(string key, bool ac)
    {
        int idx = IndexOf(key);
        if (idx < 0)
        {
            return null;
        }
        string? scheme = GetActiveScheme();
        if (scheme == null)
        {
            return null;
        }
        (string sub, string setting, int onValue, _) = Options[idx];
        string path = SettingPath(scheme, sub, setting);
        try
        {
            using RegistryKey? rk = Registry.LocalMachine.OpenSubKey(path);
            object? v = ac ? rk?.GetValue("ACSettingIndex") : rk?.GetValue("DCSettingIndex");
            if (v is int i)
            {
                return i == onValue;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("PowerSleepGuard: read enabled failed at " + path + ": " + ex.Message);
        }
        return null;
    }''', 'PG ReadEnabled ac')

# SetEnabled 分档
p = rep(p, '''    /// <summary>写入开关状态（AC/DC 同时设），成功返回 true。</summary>
    public static bool SetEnabled(string key, bool enabled)
    {
        int idx = IndexOf(key);
        if (idx < 0)
        {
            return false;
        }
        string? scheme = GetActiveScheme();
        if (scheme == null)
        {
            return false;
        }
        (string sub, string setting, _, int offValue) = Options[idx];
        int value = enabled ? Options[idx].OnValue : offValue;
        string path = SettingPath(scheme, sub, setting);
        try
        {
            using RegistryKey rk = Registry.LocalMachine.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree);
            rk.SetValue("ACSettingIndex", value, RegistryValueKind.DWord);
            rk.SetValue("DCSettingIndex", value, RegistryValueKind.DWord);
            rk.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("PowerSleepGuard: write failed at " + path + ": " + ex.Message);
            return false;
        }
    }''',
'''    /// <summary>写入开关状态（指定插电/离电档），成功返回 true。</summary>
    public static bool SetEnabled(string key, bool enabled, bool ac)
    {
        int idx = IndexOf(key);
        if (idx < 0)
        {
            return false;
        }
        string? scheme = GetActiveScheme();
        if (scheme == null)
        {
            return false;
        }
        (string sub, string setting, _, int offValue) = Options[idx];
        int value = enabled ? Options[idx].OnValue : offValue;
        string path = SettingPath(scheme, sub, setting);
        try
        {
            using RegistryKey rk = Registry.LocalMachine.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree);
            rk.SetValue(ac ? "ACSettingIndex" : "DCSettingIndex", value, RegistryValueKind.DWord);
            rk.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("PowerSleepGuard: write failed at " + path + ": " + ex.Message);
            return false;
        }
    }''', 'PG SetEnabled ac')

# ---------- SettingsForm.cs ----------
# 1) 字段
s = rep(s, '''	private readonly Dictionary<string, (Control Header, Control Grid)> _quickGroupContainers = new Dictionary<string, (Control, Control)>();''',
'''	private readonly Dictionary<string, (Control Header, Control Grid)> _quickGroupContainers = new Dictionary<string, (Control, Control)>();

	private bool _quickSleepAc = true;

	private RButton? _quickSleepAcBtn;

	private RButton? _quickSleepDcBtn;

	private readonly ToolTip _quickSleepToolTip = new ToolTip();''', 'fields')

# 2) 组循环：睡眠组标题行特判（AC/DC 切换）
s = rep(s, '''			Label label = new Label
			{
				Name = "labelQuickGroup_" + item2[0],
				Text = item,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				Margin = new Padding(0, (num != 1) ? 12 : 0, 0, 4)
			};
			root.Controls.Add(label, 0, num++);''',
'''			Label label = new Label
			{
				Name = "labelQuickGroup_" + item2[0],
				Text = item,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				Margin = new Padding(0, (num != 1) ? 12 : 0, 0, 4)
			};
			Control header = label;
			if (item2[0] == "sleepnetoff")
			{
				FlowLayoutPanel titleRow = new FlowLayoutPanel
				{
					Name = "quickGroupTitle_sleep",
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = panel.BackColor
				};
				titleRow.Controls.Add(label);
				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					AutoSize = true,
					Margin = new Padding(10, 6, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					AutoSize = true,
					Margin = new Padding(4, 6, 0, 0),
					BackColor = UiVisualStyle.SurfaceRaised,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Cursor = Cursors.Hand
				};
				titleRow.Controls.Add(btnAc);
				titleRow.Controls.Add(btnDc);
				btnAc.Click += (object? _, EventArgs _) => SetQuickSleepAc(ac: true);
				btnDc.Click += (object? _, EventArgs _) => SetQuickSleepAc(ac: false);
				SetThemeButtonState(btnAc, selected: true);
				_quickSleepAcBtn = btnAc;
				_quickSleepDcBtn = btnDc;
				header = titleRow;
				root.Controls.Add(titleRow, 0, num++);
			}
			else
			{
				root.Controls.Add(label, 0, num++);
			}''', 'sleep title row')

# 3) _quickGroupContainers 用 header
s = rep(s, '''			_quickGroupContainers[item] = (label, flowLayoutPanel);''',
'''			_quickGroupContainers[item] = (header, flowLayoutPanel);''', 'containers header')

# 4) handler 传 ac + ToolTip
s = rep(s, '''				if (key2.StartsWith("sleep"))
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							bool requested = cb.Checked;
							_lastQuickSwitchUi = DateTime.Now;
							if (!PowerSleepGuard.SetEnabled(key2, requested))
							{
								ToastForm.ShowFailure(Strings.QuickSwitchFailed);
								RevertCheck(cb, PowerSleepGuard.ReadEnabled(key2) ?? (!requested));
							}
						}
					};
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}''',
'''				if (key2.StartsWith("sleep"))
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							bool requested = cb.Checked;
							_lastQuickSwitchUi = DateTime.Now;
							if (!PowerSleepGuard.SetEnabled(key2, requested, _quickSleepAc))
							{
								ToastForm.ShowFailure(Strings.QuickSwitchFailed);
								RevertCheck(cb, PowerSleepGuard.ReadEnabled(key2, _quickSleepAc) ?? (!requested));
							}
						}
					};
					string sleepTip = SleepTipFor(key2);
					if (sleepTip.Length > 0)
					{
						_quickSleepToolTip.SetToolTip(cb, sleepTip);
					}
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}''', 'handler ac + tooltip')

# 5) UpdateQuickSwitches 传 ac
s = rep(s, '''				else if (text.StartsWith("sleep"))
				{
					bool? sleepState = PowerSleepGuard.ReadEnabled(text);
					if (sleepState.HasValue)
					{
						checkBox.Checked = sleepState.Value;
					}
				}''',
'''				else if (text.StartsWith("sleep"))
				{
					bool? sleepState = PowerSleepGuard.ReadEnabled(text, _quickSleepAc);
					if (sleepState.HasValue)
					{
						checkBox.Checked = sleepState.Value;
					}
				}''', 'UpdateQuickSwitches ac')

# 6) ReadQuickSwitchState 传 ac
s = rep(s, '''		if (key.StartsWith("sleep"))
		{
			return PowerSleepGuard.ReadEnabled(key);
		}''',
'''		if (key.StartsWith("sleep"))
		{
			return PowerSleepGuard.ReadEnabled(key, _quickSleepAc);
		}''', 'ReadQuickSwitchState ac')

# 7) SetQuickSleepAc + SleepTipFor 方法（UpdateQuickSwitches 前插入）
s = rep(s, '''	private void UpdateQuickSwitches()''',
'''	private void SetQuickSleepAc(bool ac)
	{
		if (_quickSleepAc == ac)
		{
			return;
		}
		_quickSleepAc = ac;
		SetThemeButtonState(_quickSleepAcBtn, ac);
		SetThemeButtonState(_quickSleepDcBtn, !ac);
		if (_quickSwitches != null)
		{
			_syncingSwitches = true;
			try
			{
				CheckBox[] quickSwitches = _quickSwitches;
				for (int num = 0; num < quickSwitches.Length; num++)
				{
					string? key = quickSwitches[num].Tag as string;
					if (key != null && key.StartsWith("sleep"))
					{
						bool? sleepState = PowerSleepGuard.ReadEnabled(key, ac);
						if (sleepState.HasValue)
						{
							quickSwitches[num].Checked = sleepState.Value;
						}
					}
				}
			}
			finally
			{
				_syncingSwitches = false;
			}
		}
	}

	private static string SleepTipFor(string key)
	{
		return key switch
		{
			"sleepnetoff" => Strings.QuickSleepNetOffTip,
			"sleepaway" => Strings.QuickSleepAwayTip,
			"sleepsysreq" => Strings.QuickSleepSysReqTip,
			"sleeprtcwake" => Strings.QuickSleepRtcWakeTip,
			"sleepdeep" => Strings.QuickSleepDeepTip,
			"sleephibernate" => Strings.QuickSleepHibernateTip,
			_ => ""
		};
	}

	private void UpdateQuickSwitches()''', 'SetQuickSleepAc + SleepTipFor')

with io.open(SF, 'w', encoding='utf-8', newline='') as f:
    f.write(s)
with io.open(PG, 'w', encoding='utf-8', newline='') as f:
    f.write(p)
print('ALL DONE')
