# -*- coding: utf-8 -*-
# 合并 18.6 的 Reconcile（切换后回读对账补写）到 PerfModeService
p = r'D:\openLM\source\MechrevoLite.Mode\PerfModeService.cs'
t = open(p, encoding='utf-8').read()

def rep(s, old, new, tag):
    n = s.count(old)
    print('[' + tag + '] 匹配数:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag)
    return s.replace(old, new)

# 1. ActivateLockedAsync 开头保存期望配置快照（切换前）
old1 = '''		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null)
		{
			Logger.WriteLine("PerfModes: activate(" + modeId + ") ignored, mode does not exist");
			return null;
		}
		MechrevoHw hw = _hw;'''
new1 = '''		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null)
		{
			Logger.WriteLine("PerfModes: activate(" + modeId + ") ignored, mode does not exist");
			return null;
		}
		PerfModeSettings expectedSettings = mode.Settings;
		MechrevoHw hw = _hw;'''
t = rep(t, old1, new1, 'expected-snapshot')

# 2. 切换成功块：自定义模式启动对账补写
old2 = '''			StartGuard(mode.Id);
		}
		RaiseApplied(outcome);
		return outcome;
	}

	public async Task<bool> PushSlotNameAsync(string modeId)'''
new2 = '''			StartGuard(mode.Id);
			if (mode.IsCustom)
			{
				_ = ReconcileCustomAsync(mode, expectedSettings);
			}
		}
		RaiseApplied(outcome);
		return outcome;
	}

	private async Task ReconcileCustomAsync(PerfModeDefinition mode, PerfModeSettings expected)
	{
		try
		{
			await Task.Delay(1000).ConfigureAwait(continueOnCapturedContext: false);
			MechrevoHw hw = _hw;
			if (hw == null || !hw.IsConnected || hw.OperatingMode != 3 || !string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal))
			{
				return;
			}
			PerfLiveSnapshot live = SnapshotLive(includeFanCurves: false);
			Dictionary<string, string> diffs = new Dictionary<string, string>();
			void Check(string wireKey, int? expectedValue, int? actual)
			{
				if (expectedValue.HasValue && actual.HasValue && actual.GetValueOrDefault() != expectedValue.GetValueOrDefault())
				{
					diffs[wireKey] = expectedValue.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
				}
			}
			void CheckSwitch(string wireKey, bool? expectedValue, bool? actual)
			{
				if (expectedValue.HasValue && actual.HasValue && actual.GetValueOrDefault() != expectedValue.GetValueOrDefault())
				{
					diffs[wireKey] = (expectedValue.GetValueOrDefault() ? 1 : 0).ToString(CultureInfo.InvariantCulture);
				}
			}
			CheckSwitch("CpuTccOffsetSwitch", expected.TccOn, live.TccOn);
			CheckSwitch("GpuDynamicBoostSwitch", expected.GpuDynamicBoostOn, live.GpuDynamicBoostOn);
			CheckSwitch("OverClockingSwitch", expected.GpuOverclockOn, live.GpuOverclockOn);
			CheckSwitch("FanSwitchSpeedEnabled", expected.FanSwitchSpeedOn, live.FanSwitchSpeedOn);
			Check("PL1", expected.Pl1, live.Pl1);
			Check("PL2", expected.Pl2, live.Pl2);
			Check("PL4", expected.Pl4, live.Pl4);
			Check("CpuTccOffset", expected.TccTarget, live.TccTarget);
			Check("GpuConfigurableTGPTarget", expected.GpuTgp, live.GpuTgp);
			Check("GpuDynamicBoost", expected.GpuDynamicBoost, live.GpuDynamicBoost);
			Check("GpuCoreClockOffsetOC", expected.GpuCoreOffset, live.GpuCoreOffset);
			Check("GpuMemoryClockOffsetOC", expected.GpuMemoryOffset, live.GpuMemoryOffset);
			Check("FanSwitchSpeed", expected.FanSwitchSpeedMs, live.FanSwitchSpeedMs);
			if (diffs.Count == 0)
			{
				return;
			}
			Logger.WriteLine("PerfModes: reconcile 重应用: " + string.Join(", ", diffs.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
			foreach (KeyValuePair<string, string> kv in diffs)
			{
				if (!string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal) || _hw == null || !_hw.IsConnected || _hw.OperatingMode != 3)
				{
					return;
				}
				try
				{
					await _backend.WriteDetailFieldAsync(kv.Key, kv.Value, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
					await Task.Delay(PerfModeApplyPlanner.FieldDelayMs).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("PerfModes: reconcile field " + kv.Key + " failed: " + ex.Message);
				}
			}
			PerfModeStore.SaveSettings(mode.Id, expected);
			Invalidate();
			Logger.WriteLine("PerfModes: reconcile 完成, 已同步本地配置");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes: reconcile failed: " + ex.Message);
		}
	}

	public async Task<bool> PushSlotNameAsync(string modeId)'''
t = rep(t, old2, new2, 'reconcile-insert')

open(p, 'w', encoding='utf-8', newline='').write(t)
print('ALL OK')
