# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()

def rep(old, new, cnt=1, label=''):
    global t
    assert t.count(old) == cnt, f'anchor mismatch ({t.count(old)} != {cnt}): {label or old[:70]!r}'
    t = t.replace(old, new, cnt)

# 1) VisualiseGPUMode: 待重启时高亮 pendingTarget + Warn 色
rep('''		int num = ResolveHighlightedGpuMode(GPUMode);
		bool flag2 = !IsReadOnlyDegraded;
		ButtonEnabled(buttonOptimized, flag2);
		ButtonEnabled(buttonEco, flag2);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, Strings.GpuRouteIgpuTip);
		buttonEco.Activated = num == 0;
		buttonStandard.Activated = num == 1;
		buttonUltimate.Activated = num == 2;''',
'''		int num = ResolveHighlightedGpuMode(GPUMode);
		bool flag2 = !IsReadOnlyDegraded;
		bool restartPending = HasPendingGpuRestart();
		if (restartPending)
		{
			int pendingTarget = GpuRestartVerifier.PendingTarget;
			if (pendingTarget >= 0)
			{
				num = pendingTarget;
			}
		}
		ButtonEnabled(buttonOptimized, flag2);
		ButtonEnabled(buttonEco, flag2);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, Strings.GpuRouteIgpuTip);
		Color pendingColor = (restartPending ? UiVisualStyle.Warn : Color.Empty);
		buttonEco.ActivatedColor = pendingColor;
		buttonStandard.ActivatedColor = pendingColor;
		buttonUltimate.ActivatedColor = pendingColor;
		buttonEco.Activated = num == 0;
		buttonStandard.Activated = num == 1;
		buttonUltimate.Activated = num == 2;''', 1, 'VisualiseGPUMode')

# 2) ShowGpuRestartPromptAsync: 三选项（是=重启 / 否=发命令不重启+待重启高亮 / 取消=不动）
rep('''	private async Task<bool> ShowGpuRestartPromptAsync(int targetMode, string modeName)
	{
		if (MessageBox.Show(string.Format(Strings.GpuRestartPrompt, modeName), "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
		{
			await RefreshGpuModeUiStateAsync();
			return false;
		}
		if (Program.service == null)''',
'''	private async Task<bool> ShowGpuRestartPromptAsync(int targetMode, string modeName)
	{
		DialogResult dialogResult = MessageBox.Show(string.Format(Strings.GpuRestartPrompt, modeName), "L-Mechrevo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
		if (dialogResult == DialogResult.No)
		{
			// 否：先发送切换指令不重启，标记待重启，界面以橙色高亮待切换状态
			if (Program.service == null)
			{
				await RefreshGpuModeUiStateAsync();
				return false;
			}
			int previousMode2 = AppConfig.Get("gpu_mode");
			bool previousAuto2 = AppConfig.Is("gpu_auto");
			try
			{
				if (await Program.service.SwitchGpuMode(targetMode))
				{
					AppConfig.Set("gpu_mode", targetMode);
					AppConfig.Set("gpu_auto", 0);
					MarkGpuRestartPending(targetMode);
					AppConfig.Flush();
					VisualiseGPUMode(targetMode);
					SetContextMenu();
					return true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("GPU switch (no restart) failed: " + ex.Message);
			}
			AppConfig.Set("gpu_mode", previousMode2);
			AppConfig.Set("gpu_auto", previousAuto2 ? 1 : 0);
			AppConfig.Flush();
			await RefreshGpuModeUiStateAsync();
			ToastForm.ShowFailure(Strings.GpuRestartRequestFailed);
			return false;
		}
		if (dialogResult != DialogResult.Yes)
		{
			// 取消：不切换也不重启
			await RefreshGpuModeUiStateAsync();
			return false;
		}
		if (Program.service == null)''', 1, 'ShowGpuRestartPromptAsync')

open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsForm.cs: gpu prompt 3-option + pending highlight patched')
