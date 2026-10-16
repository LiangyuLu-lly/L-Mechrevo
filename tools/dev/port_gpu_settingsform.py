# -*- coding: utf-8 -*-
"""批3：SettingsForm.cs 显卡高亮体系移植"""
import re

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
s = open(p, encoding='utf-8', errors='replace').read()
orig_len = len(s)

def replace_once(old, new, tag):
    global s
    assert old in s, 'NOT FOUND: ' + tag
    assert s.count(old) == 1, 'NOT UNIQUE: ' + tag + ' count=' + str(s.count(old))
    s = s.replace(old, new, 1)
    print('OK:', tag)

# ============ 1. 字段 ============
old = '\tprivate string _lastPerformanceLayout = "";\n\n\tprivate string _lastGpuLayout = "";\n'
new = '\tprivate string _lastPerformanceLayout = "";\n\n\tprivate string _lastGpuLayout = "";\n\n\tprivate GpuRowLayout _gpuRowLayout;\n\n\tprivate bool _gpuSwitchInProgress;\n\n\tprivate int _lastGpuModeHint = int.MinValue;\n'
replace_once(old, new, '字段 _gpuRowLayout/_gpuSwitchInProgress/_lastGpuModeHint')

# ============ 2. HasPendingGpuRestart 委托 Verifier ============
old = '''	private static bool HasPendingGpuRestart()
	{
		if (!AppConfig.Is("gpu_restart_pending"))
		{
			return false;
		}
		if (long.TryParse(AppConfig.GetString("gpu_restart_tick"), out var result) && Environment.TickCount64 + 10000 < result)
		{
			AppConfig.Set("gpu_restart_pending", 0);
			AppConfig.Remove("gpu_restart_tick");
			return false;
		}
		return true;
	}'''
new = '''	private static bool HasPendingGpuRestart()
	{
		return GpuRestartVerifier.HasPendingThisBoot();
	}'''
replace_once(old, new, 'HasPendingGpuRestart → Verifier')

# ============ 3. VisualiseGPUMode 整体替换（beta21 版，保留群友版尾部 VisualiseIgpuSaving）============
old = '''	public void VisualiseGPUMode(int GPUMode = -1)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				VisualiseGPUMode(GPUMode);
			});
			return;
		}
		if (toolTip.GetToolTip(pictureGPU) != (GPUModeControl.gpuError ?? ""))
		{
			pictureGPU.BackgroundImage = ((GPUModeControl.gpuError == null) ? Resources.icons8_video_card_32 : SystemIcons.Warning.ToBitmap());
			pictureGPU.Cursor = ((GPUModeControl.gpuError == null) ? Cursors.Default : Cursors.Hand);
			toolTip.SetToolTip(pictureGPU, GPUModeControl.gpuError);
		}
		ButtonEnabled(buttonOptimized, enabled: true);
		ButtonEnabled(buttonEco, enabled: true);
		ButtonEnabled(buttonStandard, enabled: true);
		ButtonEnabled(buttonUltimate, enabled: true);
		if (GPUMode == -1)
		{
			GPUMode = AppConfig.Get("gpu_mode");
			if (GPUMode == 3)
			{
				GPUMode = Program.service?.CurrentGpuMode ?? 1;
			}
		}
		buttonEco.Activated = false;
		buttonStandard.Activated = false;
		buttonUltimate.Activated = false;
		switch (GPUMode)
		{
		case 0:
			buttonEco.Activated = true;
			labelGPU.Text = Strings.GPUMode;
			panelGPU.AccessibleName = Strings.GPUMode + " - " + Strings.EcoMode;
			break;
		case 2:
			buttonUltimate.Activated = true;
			labelGPU.Text = Strings.GPUMode;
			panelGPU.AccessibleName = Strings.GPUMode + " - " + Strings.UltimateMode;
			break;
		default:
			buttonStandard.Activated = true;
			labelGPU.Text = Strings.GPUMode;
			panelGPU.AccessibleName = Strings.GPUMode + " - " + Strings.StandardMode;
			break;
		}
		VisualiseIcon();
		labelTipGPU.Text = "";
		labelTipGPU.Visible = false;
		if (isGpuSection)
		{
			if (menuEco != null)
			{
				menuEco.Checked = buttonEco.Activated;
			}
			if (menuStandard != null)
			{
				menuStandard.Checked = buttonStandard.Activated;
			}
			if (menuUltimate != null)
			{
				menuUltimate.Checked = buttonUltimate.Activated;
			}
		}
		if (Top < 0)
		{
			labelTipGPU.Visible = false;
			labelTipScreen.Visible = false;
			Top = 5;
		}
		VisualiseIgpuSaving();
	}'''
new = '''	public void VisualiseGPUMode(int GPUMode = -1)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				VisualiseGPUMode(GPUMode);
			});
			return;
		}
		if (toolTip.GetToolTip(pictureGPU) != (GPUModeControl.gpuError ?? ""))
		{
			pictureGPU.BackgroundImage = ((GPUModeControl.gpuError == null) ? Resources.icons8_video_card_32 : SystemIcons.Warning.ToBitmap());
			pictureGPU.Cursor = ((GPUModeControl.gpuError == null) ? Cursors.Default : Cursors.Hand);
			toolTip.SetToolTip(pictureGPU, GPUModeControl.gpuError);
		}
		int num = ResolveHighlightedGpuMode(GPUMode);
		bool flag = _gpuRowLayout == GpuRowLayout.HotSwap && num == 2;
		bool flag2 = !IsReadOnlyDegraded;
		ButtonEnabled(buttonOptimized, flag2);
		ButtonEnabled(buttonEco, flag2 && !flag);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, flag ? Strings.GpuIgpuNeedsStandard : Strings.GpuRouteIgpuTip);
		buttonEco.Activated = num == 0;
		buttonStandard.Activated = num == 1;
		buttonUltimate.Activated = num == 2;
		labelGPU.Text = Strings.GPUMode;
		string text;
		if (num >= 0)
		{
			if (_gpuRowLayout != GpuRowLayout.NvPreference)
			{
				text = GpuRestartVerifier.ModeName(num);
			}
			else
			{
				text = ((num == 2) ? Strings.GpuPrefHighPerf : Strings.GpuPrefAuto);
			}
		}
		else
		{
			text = ((_gpuRowLayout == GpuRowLayout.NvPreference) ? Strings.GpuPrefUnknown : Strings.GpuRouteUnknownTag);
		}
		panelGPU.AccessibleName = Strings.GPUMode + " - " + text;
		if (num < 0 && GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
		{
			toolTip.SetToolTip(panelGPU, Strings.GpuRouteUnknown);
		}
		else
		{
			toolTip.SetToolTip(panelGPU, null);
		}
		VisualiseIcon();
		labelTipGPU.Text = "";
		labelTipGPU.Visible = false;
		string text2 = GpuRowStatusText();
		if (labelGPUFan.Text != text2)
		{
			labelGPUFan.Text = text2;
		}
		if (isGpuSection)
		{
			if (menuEco != null)
			{
				menuEco.Checked = buttonEco.Activated;
				menuEco.Enabled = !flag;
			}
			if (menuStandard != null)
			{
				menuStandard.Checked = buttonStandard.Activated;
			}
			if (menuUltimate != null)
			{
				menuUltimate.Checked = buttonUltimate.Activated;
			}
		}
		if (Top < 0)
		{
			labelTipGPU.Visible = false;
			labelTipScreen.Visible = false;
			Top = 5;
		}
		VisualiseIgpuSaving();
	}

	private int ResolveHighlightedGpuMode(int hint)
	{
		switch (_gpuRowLayout)
		{
		case GpuRowLayout.NvPreference:
			return NvPreferredGpuMonitor.Last switch
			{
				NvPreferredGpu.AutoSelect => 1,
				NvPreferredGpu.HighPerformance => 2,
				_ => -1,
			};
		case GpuRowLayout.Mux2:
		case GpuRowLayout.Mux3:
		case GpuRowLayout.HotSwap:
			if (hint >= 0 && hint != _lastGpuModeHint)
			{
				_lastGpuModeHint = hint;
				GpuRouteMonitor.RefreshAsync();
			}
			return GpuRouteInference.ToGpuMode(GpuRouteMonitor.Snapshot().Route);
		default:
		{
			if (hint >= 0)
			{
				return hint;
			}
			int num2 = AppConfig.Get("gpu_mode");
			if (num2 != 3)
			{
				return num2;
			}
			return Program.service?.CurrentGpuMode ?? 1;
		}
		}
	}

	private string GpuRowStatusText()
	{
		if (_gpuSwitchInProgress)
		{
			return Strings.GPUChanging;
		}
		if (!GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
		{
			return "";
		}
		if (HasPendingGpuRestart())
		{
			return Strings.GpuRestartPendingTag;
		}
		if (GpuRouteMonitor.Last.Route == GpuRoute.Unknown)
		{
			return Strings.GpuRouteUnknownTag;
		}
		return "";
	}

	internal void ApplyGpuRowLayout(GpuRowLayout layout)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				ApplyGpuRowLayout(layout);
			});
			return;
		}
		bool flag3 = _gpuRowLayout != layout;
		_gpuRowLayout = layout;
		bool flag4 = layout == GpuRowLayout.NvPreference;
		buttonStandard.Text = (flag4 ? Strings.GpuPrefAuto : Strings.GpuRouteStandard);
		buttonUltimate.Text = (flag4 ? Strings.GpuPrefHighPerf : Strings.GpuRouteDirect);
		toolTip.SetToolTip(buttonStandard, flag4 ? Strings.GpuPrefTip : Strings.GpuRouteStandardTip);
		toolTip.SetToolTip(buttonUltimate, flag4 ? Strings.GpuPrefTip : Strings.GpuRouteDirectTip);
		if (flag3)
		{
			Logger.WriteLine("GPU row layout: " + layout);
			if (flag4)
			{
				NvPreferredGpuMonitor.RefreshInBackground();
			}
			VisualiseGPUMode();
		}
	}'''
replace_once(old, new, 'VisualiseGPUMode 替换 + 新方法')

# ============ 4. RefreshGpuModeUiStateAsync 替换 ============
old = '''	private async Task<GpuModeStatusReadback> RefreshGpuModeUiStateAsync()
	{
		if (Program.service == null)
		{
			return GpuModeStatusReadback.Unavailable;
		}
		GpuModeStatusReadback gpuModeStatusReadback = await Program.service.RefreshGpuModeStatus();
		if (gpuModeStatusReadback == GpuModeStatusReadback.Unavailable || IsDisposed)
		{
			return GpuModeStatusReadback.Unavailable;
		}
		VisualiseGPUMode(Program.service.CurrentGpuMode);
		SetContextMenu();
		return gpuModeStatusReadback;
	}'''
new = '''	private async Task<GpuModeStatusReadback> RefreshGpuModeUiStateAsync()
	{
		if (Program.service == null)
		{
			return GpuModeStatusReadback.Unavailable;
		}
		GpuModeStatusReadback readback = await Program.service.RefreshGpuModeStatus();
		if (GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
		{
			await GpuRouteMonitor.RefreshAsync();
		}
		else if (_gpuRowLayout == GpuRowLayout.NvPreference)
		{
			NvPreferredGpuMonitor.Store(await Task.Run((Func<NvPreferredGpu>)NvPreferredGpuReader.ReadFromDriver));
		}
		if (IsDisposed)
		{
			return GpuModeStatusReadback.Unavailable;
		}
		VisualiseGPUMode(Program.service.CurrentGpuMode);
		SetContextMenu();
		return readback;
	}'''
replace_once(old, new, 'RefreshGpuModeUiStateAsync 替换')

# ============ 5. ApplySnapshot：GpuRowStatusText ============
old = '''		void ApplySnapshot()
		{
			UpdatePerfRowStatus();
			string text3 = (HasPendingGpuRestart() ? Strings.RestartToApply : "");
			if (labelGPUFan.Text != text3)
			{
				labelGPUFan.Text = text3;
			}'''
new = '''		void ApplySnapshot()
		{
			UpdatePerfRowStatus();
			if (GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
			{
				GpuRouteMonitor.Snapshot();
			}
			string text3 = GpuRowStatusText();
			if (labelGPUFan.Text != text3)
			{
				labelGPUFan.Text = text3;
			}'''
replace_once(old, new, 'ApplySnapshot → GpuRowStatusText')

# ============ 6. RefreshDeviceCapabilities：设置 _gpuRowLayout ============
old = '''		panelGPU.Visible = flag21;
		VisualiseGPUButtons(flag18 | flag19, flag19, flag18);'''
new = '''		_gpuRowLayout = (hw?.GpuRowLayout ?? GpuRowLayout.Hidden);
		panelGPU.Visible = flag21;
		VisualiseGPUButtons(flag18 | flag19, flag19, flag18);'''
replace_once(old, new, 'RefreshDeviceCapabilities 设置 _gpuRowLayout')

# ============ 7. MarkGpuRestartPending 写 target（Verifier 兼容）============
old = '''	private static void MarkGpuRestartPending()
	{
		AppConfig.Set("gpu_restart_pending", 1);
		AppConfig.Set("gpu_restart_tick", Environment.TickCount64.ToString(CultureInfo.InvariantCulture));
	}'''
new = '''	private static void MarkGpuRestartPending(int targetMode = -1)
	{
		AppConfig.Set("gpu_restart_pending", 1);
		AppConfig.Set("gpu_restart_tick", Environment.TickCount64.ToString(CultureInfo.InvariantCulture));
		if (targetMode >= 0)
		{
			AppConfig.Set("gpu_restart_target", targetMode);
		}
	}'''
replace_once(old, new, 'MarkGpuRestartPending 加 target')

# ============ 8. ShowGpuRestartPromptAsync：MarkPending(targetMode) ============
old = '''		AppConfig.Set("gpu_mode", targetMode);
		AppConfig.Set("gpu_auto", 0);
		MarkGpuRestartPending();
		AppConfig.Flush();'''
new = '''		AppConfig.Set("gpu_mode", targetMode);
		AppConfig.Set("gpu_auto", 0);
		MarkGpuRestartPending(targetMode);
		AppConfig.Flush();'''
replace_once(old, new, 'ShowGpuRestartPromptAsync MarkPending(targetMode)')

open(p, 'w', encoding='utf-8', newline='').write(s)
print('DONE. len', orig_len, '->', len(s))
