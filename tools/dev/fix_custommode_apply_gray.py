# -*- coding: utf-8 -*-
import io

def load(p):
    return open(p, encoding='utf-8').read()

def save(p, t):
    open(p, 'w', encoding='utf-8', newline='').write(t)

def rep(t, old, new, tag):
    n = t.count(old)
    if n != 1:
        print(f'[{tag}] 匹配数异常: {n}')
        raise SystemExit(1)
    return t.replace(old, new)

# ============ 1. PerfModeApplyPlanner.cs：FirmwareSlot 参数步骤不再依赖 caps 能力位 ============
p1 = r'D:\openLM\source\MechrevoLite.Mode\PerfModeApplyPlanner.cs'
t1 = load(p1)
# FirmwareSlot 分支：开关区块整体替换（避免与 BuiltIn 分支同文本冲突）
old_gate = '''		if (perfModeRoute == PerfModeRoute.FirmwareSlot && (object)slotPlan != null && slotPlan.NeedsParameterWrite)
		{
			if (caps.SupportsTcc && settings.TccOn.HasValue)
			{
				list.Add(Field("CpuTccOffsetSwitch", settings.TccOn.Value ? 1 : 0));
			}
			if (caps.SupportsDynamicBoost && settings.GpuDynamicBoostOn.HasValue)
			{
				list.Add(Field("GpuDynamicBoostSwitch", settings.GpuDynamicBoostOn.Value ? 1 : 0));
			}
			if (caps.SupportsGpuOverclock && settings.GpuOverclockOn.HasValue)
			{
				list.Add(Field("OverClockingSwitch", settings.GpuOverclockOn.Value ? 1 : 0));
			}
			if (caps.SupportsFanSwitchSpeed && settings.FanSwitchSpeedOn.HasValue)
			{
				list.Add(Field("FanSwitchSpeedEnabled", settings.FanSwitchSpeedOn.Value ? 1 : 0));
			}'''
new_gate = '''		if (perfModeRoute == PerfModeRoute.FirmwareSlot && (object)slotPlan != null && slotPlan.NeedsParameterWrite)
		{
			if (settings.TccOn.HasValue)
			{
				list.Add(Field("CpuTccOffsetSwitch", settings.TccOn.Value ? 1 : 0));
			}
			if (settings.GpuDynamicBoostOn.HasValue)
			{
				list.Add(Field("GpuDynamicBoostSwitch", settings.GpuDynamicBoostOn.Value ? 1 : 0));
			}
			if (settings.GpuOverclockOn.HasValue)
			{
				list.Add(Field("OverClockingSwitch", settings.GpuOverclockOn.Value ? 1 : 0));
			}
			if (settings.FanSwitchSpeedOn.HasValue)
			{
				list.Add(Field("FanSwitchSpeedEnabled", settings.FanSwitchSpeedOn.Value ? 1 : 0));
			}'''
t1 = rep(t1, old_gate, new_gate, 'planner-gate')

for tag, old, new in [
    ('pl1',    '\t\t\tif (caps.SupportsPl1 && settings.Pl1.HasValue)', '\t\t\tif (settings.Pl1.HasValue)'),
    ('pl2',    '\t\t\tif (caps.SupportsPl2 && settings.Pl2.HasValue)', '\t\t\tif (settings.Pl2.HasValue)'),
    ('pl4',    '\t\t\tif (caps.SupportsPl4 && settings.Pl4.HasValue)', '\t\t\tif (settings.Pl4.HasValue)'),
    ('tccT',   '\t\t\t\tlist.Add(Field("PL4", settings.Pl4.Value, PerfApplyVerify.CpuPackagePower));\n\t\t\t}\n\t\t\tif (caps.SupportsTcc && settings.TccTarget.HasValue)', '\t\t\t\tlist.Add(Field("PL4", settings.Pl4.Value, PerfApplyVerify.CpuPackagePower));\n\t\t\t}\n\t\t\tif (settings.TccTarget.HasValue)'),
    ('tgp',    '\t\t\tif (caps.SupportsTgp && settings.GpuTgp.HasValue)', '\t\t\tif (settings.GpuTgp.HasValue)'),
    ('db',     '\t\t\tif (caps.SupportsDynamicBoost && settings.GpuDynamicBoost.HasValue)', '\t\t\tif (settings.GpuDynamicBoost.HasValue)'),
    ('ocCore', '\t\t\tif (caps.SupportsGpuOverclock && settings.GpuCoreOffset.HasValue)', '\t\t\tif (settings.GpuCoreOffset.HasValue)'),
    ('ocMem',  '\t\t\tif (caps.SupportsGpuOverclock && settings.GpuMemoryOffset.HasValue)', '\t\t\tif (settings.GpuMemoryOffset.HasValue)'),
    ('fanSwMs','\t\t\t\tlist.Add(Field("GpuMemoryClockOffsetOC", settings.GpuMemoryOffset.Value, PerfApplyVerify.GpuClockOffset));\n\t\t\t}\n\t\t\tif (caps.SupportsFanSwitchSpeed && settings.FanSwitchSpeedMs.HasValue)', '\t\t\t\tlist.Add(Field("GpuMemoryClockOffsetOC", settings.GpuMemoryOffset.Value, PerfApplyVerify.GpuClockOffset));\n\t\t\t}\n\t\t\tif (settings.FanSwitchSpeedMs.HasValue)'),
]:
    t1 = rep(t1, old, new, 'planner-' + tag)

# 风扇曲线：去掉 caps.SupportsFanCurve 外层（CPU）
old_cpu = '''\t\t\tif (caps.SupportsFanCurve)
			{
				int[] cpuFanDuty = settings.CpuFanDuty;
				if (cpuFanDuty != null && cpuFanDuty.Length == 16)
				{
					list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: true, settings.CpuFanDuty, PerfApplyVerify.FanDuty));
				}
			}'''
new_cpu = '''\t\t\tint[] cpuFanDuty = settings.CpuFanDuty;
			if (cpuFanDuty != null && cpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: true, settings.CpuFanDuty, PerfApplyVerify.FanDuty));
			}'''
t1 = rep(t1, old_cpu, new_cpu, 'planner-fanCpu')

# 风扇曲线：GPU
old_gpu = '''\t\t\tif (caps.SupportsFanCurve)
			{
				int[] cpuFanDuty = settings.GpuFanDuty;
				if (cpuFanDuty != null && cpuFanDuty.Length == 16)
				{
					list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: false, settings.GpuFanDuty, PerfApplyVerify.FanDuty));
				}
			}'''
new_gpu = '''\t\t\tint[] gpuFanDuty = settings.GpuFanDuty;
			if (gpuFanDuty != null && gpuFanDuty.Length == 16)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.WriteFanCurve, null, null, 0, IsCpuCurve: false, settings.GpuFanDuty, PerfApplyVerify.FanDuty));
			}'''
t1 = rep(t1, old_gpu, new_gpu, 'planner-fanGpu')

# 尾部：FanBoost / RefreshRate 去 caps 门槛
t1 = rep(t1,
    '\t\tif (caps.SupportsFanBoost && settings.FanBoost.HasValue)',
    '\t\tif (settings.FanBoost.HasValue)', 'planner-fanBoost')
old_rr = '''\t\tif (caps.SupportsRefreshRate)
		{
			int? refreshHz = settings.RefreshHz;
			if (refreshHz.HasValue && refreshHz.GetValueOrDefault() > 0)
			{
				list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyRefreshRate, null, null, settings.RefreshHz.Value, IsCpuCurve: false, null, PerfApplyVerify.RefreshRate));
			}
		}'''
new_rr = '''\t\tint? refreshHz = settings.RefreshHz;
		if (refreshHz.HasValue && refreshHz.GetValueOrDefault() > 0)
		{
			list.Add(new PerfApplyStep(PerfApplyStepKind.ApplyRefreshRate, null, null, settings.RefreshHz.Value, IsCpuCurve: false, null, PerfApplyVerify.RefreshRate));
		}'''
t1 = rep(t1, old_rr, new_rr, 'planner-refresh')
save(p1, t1)
print('[1] PerfModeApplyPlanner.cs OK')

# ============ 2. PerfModeService.cs：NotifyExternalSwitch ============
p2 = r'D:\openLM\source\MechrevoLite.Mode\PerfModeService.cs'
t2 = load(p2)
old_ns = '''	public string DisplayedModeId => PendingModeId ?? ActiveModeId;
'''
new_ns = '''	public string DisplayedModeId => PendingModeId ?? ActiveModeId;

	public void NotifyExternalSwitch(string modeId)
	{
		if (string.Equals(ActiveModeId, modeId, StringComparison.Ordinal))
		{
			return;
		}
		PerfModeStore.ActiveModeId = modeId;
		RaiseActiveChanged();
	}
'''
t2 = rep(t2, old_ns, new_ns, 'service-notify')
save(p2, t2)
print('[2] PerfModeService.cs OK')

# ============ 3. SettingsForm.cs：主窗切换成功后通知 PerfModeService ============
p3 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t3 = load(p3)
old_sw = '''			if (request == Volatile.Read(in _performanceRequest))
			{
				int displayMode = flag ? mode : await mechrevoService.RefreshCurrentModeAsync(mechrevoService.CurrentMode);
				bool actuallySucceeded = flag || displayMode == mode;
				ShowMode(MechrevoService.ToVisualMode(displayMode));
				SetContextMenu();
				ModeControl.NotifySwitchOutcome(actuallySucceeded, cancelled: false);
			}'''
new_sw = '''			if (request == Volatile.Read(in _performanceRequest))
			{
				int displayMode = flag ? mode : await mechrevoService.RefreshCurrentModeAsync(mechrevoService.CurrentMode);
				bool actuallySucceeded = flag || displayMode == mode;
				ShowMode(MechrevoService.ToVisualMode(displayMode));
				SetContextMenu();
				ModeControl.NotifySwitchOutcome(actuallySucceeded, cancelled: false);
				if (displayMode >= 0 && displayMode <= 2)
				{
					PerfModeKind kind = displayMode switch
					{
						0 => PerfModeKind.Silent,
						1 => (silentTurbo == true ? PerfModeKind.SilentTurbo : PerfModeKind.Turbo),
						2 => PerfModeKind.Balanced,
						_ => PerfModeKind.Balanced
					};
					PerfModeService.Instance?.NotifyExternalSwitch(PerfModeDefinition.BuiltInId(kind));
				}
			}'''
t3 = rep(t3, old_sw, new_sw, 'settings-notify')
save(p3, t3)
print('[3] SettingsForm.cs OK')

# ============ 4. CustomModeForm.cs：BindMode 去强制跳转 + 置灰 ============
p4 = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
t4 = load(p4)

# 4a. BindMode 删除 IsGen40Plus 强制跳转块
old_bind_head = '''	internal void BindMode(string id)
	{
		if (IsGen40Plus(Program.hw))
		{
			PerfModeDefinition current = FindMode(id);
			if (current != null && current.Kind != PerfModeKind.Custom)
			{
				PerfModeDefinition? firstCustom = ModesSnapshot().FirstOrDefault((PerfModeDefinition m) => m.Kind == PerfModeKind.Custom);
				if (firstCustom != null)
				{
					id = firstCustom.Id;
				}
			}
		}
		PerfModeDefinition mode = FindMode(id) ?? ModesSnapshot().FirstOrDefault();'''
new_bind_head = '''	internal void BindMode(string id)
	{
		PerfModeDefinition mode = FindMode(id) ?? ModesSnapshot().FirstOrDefault();'''
t4 = rep(t4, old_bind_head, new_bind_head, 'form-bindHead')

# 4b. BindMode 末尾（ResizeToContent 前）加 SetOptionsEnabled
old_bind_tail = '''		if (IsHandleCreated)
		{
			ResizeToContent();
		}
	}

	private void UpdateRouteHint(PerfModeDefinition mode)'''
new_bind_tail = '''		SetOptionsEnabled();
		if (IsHandleCreated)
		{
			ResizeToContent();
		}
	}

	private void SetOptionsEnabled()
	{
		MechrevoHw? hw = Program.hw;
		PerfModeDefinition? mode = FindMode(_modeId);
		bool locked = hw != null && IsGen40Plus(hw) && (mode == null || !mode.IsCustom);
		foreach (Control control in OptionControls())
		{
			if (control != null)
			{
				control.Enabled = !locked;
			}
		}
	}

	private Control[] OptionControls()
	{
		return new Control[]
		{
			_pl1, _pl2, _pl4, _tcc, _tgp, _db, _coreOc, _memOc, _fanSwitchSpeed,
			_pl1Val, _pl2Val, _pl4Val, _tccVal, _tgpVal, _dbVal, _coreOcVal, _memOcVal, _fanSwitchSpeedVal,
			_dbChk, _tccChk, _ocChk, _fanSwitchSpeedChk,
			_powerModeCombo, _planCombo, _boostCombo, _fanBoostCombo, _refreshCombo,
			_fanCurveButton, _gpuOcAdminRow, _fanBoostRow, _refreshRow
		};
	}

	private void UpdateRouteHint(PerfModeDefinition mode)'''
t4 = rep(t4, old_bind_tail, new_bind_tail, 'form-bindTail')

# 4c. OnCustomChanged finally 后调用 SetOptionsEnabled（刷新后重新置灰）
old_oc = '''			finally
			{
				_syncing = false;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Performance mode editor refresh failed: " + ex.Message);
		}
	}
}'''
new_oc = '''			finally
			{
				_syncing = false;
			}
		}
		SetOptionsEnabled();
	}
	catch (Exception ex)
	{
		Logger.WriteLine("Performance mode editor refresh failed: " + ex.Message);
	}
}'''
t4 = rep(t4, old_oc, new_oc, 'form-oc')
save(p4, t4)
print('[4] CustomModeForm.cs OK')
print('全部修改完成')
