using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Display;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;

namespace MechrevoLite.Gpu;

public class GPUModeControl
{
	private SettingsForm settings;

	public static int gpuMode;

	public static bool? gpuExists;

	private static bool nvRestartPending;

	private static int _manualGpuSwitchInFlight;

	public static string? gpuError;

	public GPUModeControl(SettingsForm settingsForm)
	{
		settings = settingsForm;
	}

	public void InitGPUMode()
	{
		if (Program.hw != null && (Program.hw.Capabilities.ProfileAvailable || Program.hw.SettingStatusSeen))
		{
			bool canOfferGpuModeSwitch = Program.hw.CanOfferGpuModeSwitch;
			bool flag = canOfferGpuModeSwitch && Program.hw.CanOfferIgpuOnly;
			bool flag2 = canOfferGpuModeSwitch && Program.hw.SupportsDgpuDirect;
			settings.ApplyGpuRowLayout(Program.hw.GpuRowLayout);
			// 2026-10-04：eco（核显模式按钮）只能用 flag（CanOfferIgpuOnly）；
			// 原来写成 flag | flag2，导致"支持独显直连但不支持核显模式"的机型
			// （如极光Pro GM6PQ0X）也显示核显模式按钮。
			settings.VisualiseGPUButtons(flag, flag2);
			settings.RefreshDeviceCapabilities();
			if (!flag && !flag2)
			{
				settings.HideGPUModes(gpuExists: false);
				return;
			}
			int num = AppConfig.Get("gpu_mode");
			int num2 = Program.hw.GpuMode;
			int num3 = ((num2 >= 0 && num2 <= 3) ? Program.hw.GpuMode : num);
			gpuMode = num3;
			settings.VisualiseGPUMode(gpuMode);
			return;
		}
		if (AppConfig.NoGpu())
		{
			settings.HideGPUModes(gpuExists: false);
			return;
		}
		int num4 = Program.acpi.DeviceGet(0u);
		int num5 = Program.acpi.DeviceGet(589846u);
		Logger.WriteLine("Eco flag : " + num4);
		Logger.WriteLine("Mux flag : " + num5);
		if (num4 == 1)
		{
			IGpuControl? gpuControl = HardwareControl.GpuControl;
			if (gpuControl != null && gpuControl.IsValid)
			{
				Logger.WriteLine("Eco half-state");
			}
		}
		// 2026-10-04：同上一处 —— eco 只看 num4(Eco flag)>=0，不能用 || num5(Mux flag)
		settings.VisualiseGPUButtons(num4 >= 0, num5 >= 0);
		if (num5 == 0)
		{
			gpuMode = 2;
		}
		else
		{
			if (num4 == 1)
			{
				gpuMode = 0;
			}
			else
			{
				gpuMode = 1;
			}
			if (num4 < 0 && num5 < 0)
			{
				bool? flag3 = gpuExists;
				if (!flag3.HasValue)
				{
					gpuExists = Program.acpi.GetFan(AsusFan.GPU) >= 0;
				}
				settings.HideGPUModes(gpuExists.Value);
			}
		}
		AppConfig.Set("gpu_mode", gpuMode);
		settings.VisualiseGPUMode(gpuMode);
		Aura.CustomRGB.ApplyGPUColor(gpuMode);
		Task.Run((Action)CheckGpuError);
	}

	public async Task SetGPUMode(int GPUMode)
	{
		int num;
		switch (Program.hw?.GpuMode)
		{
		default:
			num = AppConfig.Get("gpu_mode");
			break;
		case 0:
		case 1:
		case 2:
		case 3:
			num = Program.hw.GpuMode;
			break;
		}
		int num2 = num;
		if (num2 == GPUMode)
		{
			settings.VisualiseGPUMode();
			return;
		}
		MechrevoHw hw = Program.hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：显卡模式切换走 GCU（GPUDevice/Control）⇒ 真正需要 GCU。
		if (hw != null && hw.IsGcuConnected)
		{
			try
			{
				int modeBeforeSwitch = Program.hw.GpuMode;
				// 2026-10-03：热切换执行已删除（与核显省电功能重叠），切集显统一走重启后应用
				Interlocked.Exchange(ref _manualGpuSwitchInFlight, 1);
				bool flag2;
				try
				{
					flag2 = ((Program.service == null) ? (await Program.hw.SetGpuMode(GPUMode)) : (await Program.service.SwitchGpuMode(GPUMode, autoRestart: false)));
				}
				finally
				{
					Interlocked.Exchange(ref _manualGpuSwitchInFlight, 0);
				}
				if (flag2)
				{
					AppConfig.Set("gpu_mode", GPUMode);
					settings.VisualiseGPUMode(GPUMode);
					return;
				}
					settings.VisualiseGPUMode(Program.hw.GpuMode);
				Logger.WriteLine($"Legacy SetGPUMode not confirmed: expected={GPUMode} actual={Program.hw.GpuMode}");
				try
				{
					Program.toast?.RunToast(Strings.GpuSwitchFailedRestored);
					return;
				}
				catch (Exception ex)
				{
					Logger.WriteLine("GPU switch failure toast failed: " + ex.Message);
					return;
				}
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Legacy SetGPUMode failed: " + ex2.Message);
				if (Program.hw != null)
				{
					settings.VisualiseGPUMode(Program.hw.GpuMode);
				}
				try
				{
					Program.toast?.RunToast(Strings.GpuSwitchFailedRestored);
					return;
				}
				catch (Exception ex3)
				{
					Logger.WriteLine("GPU switch failure toast failed: " + ex3.Message);
					return;
				}
			}
		}
		bool flag3 = false;
		bool flag4 = false;
		if (num2 == 2)
		{
			if (MessageBox.Show(Strings.AlertUltimateOff, Strings.AlertUltimateTitle, MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				SystemRestart.CaptureUserConfirmation();
				flag3 = true;
				flag4 = true;
			}
		}
		else
		{
			switch (GPUMode)
			{
			case 2:
				if (Program.acpi.DeviceGet(589846u) < 0)
				{
					Logger.WriteLine("Mux not supported");
					settings.VisualiseGPUMode();
					return;
				}
				if (MessageBox.Show(Strings.AlertUltimateOn, Strings.AlertUltimateTitle, MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					SystemRestart.CaptureUserConfirmation();
					Program.acpi.SetGPUEco(0);
					await Task.Delay(500);
					int num3 = Program.acpi.DeviceGet(0u);
					Logger.WriteLine("Eco flag : " + num3);
					if (num3 == 1)
					{
						settings.VisualiseGPUMode();
						return;
					}
					flag3 = true;
					flag4 = true;
				}
				break;
			case 0:
				settings.VisualiseGPUMode(GPUMode);
				SetGPUEco(1);
				flag4 = true;
				break;
			case 1:
				settings.VisualiseGPUMode(GPUMode);
				SetGPUEco(0);
				flag4 = true;
				break;
			}
		}
		if (flag4)
		{
			AppConfig.Set("gpu_mode", GPUMode);
		}
		if (flag3)
		{
			settings.VisualiseGPUMode();
			SystemRestart.RequestRestart("legacy GPU mode switch", "/r /t 1");
		}
	}

	public void SetGPUEco(int eco)
	{
		settings.LockGPUModes();
		Task.Run(async () =>
		{
			Program.modeControl.WaitForApply();
			if (eco == 1)
			{
				HardwareControl.KillGPUApps();
				HardwareControl.DisposeGpuControl();
				if (AppConfig.IsNVPlatform())
				{
					NvidiaGpuControl.StopNVService();
				}
			}
			Logger.WriteLine($"Running eco command {eco}");
			try
			{
				Program.acpi.SetGPUEco(eco);
				await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("refresh_delay", 500)));
				await settings.InvokeAsync(() =>
				{
					InitGPUMode();
					// ⭐ 2026-10-15（用户拍板「触发方式改成 ①：只认插拔 + 开机首次」）：**切换显卡模式不再应用自动刷新率**。
					// 这里原来是 `ScreenControl.AutoScreen()` ⇒ auto 开着时，切一次显卡模式就把用户手选的中间档
					// （如 120Hz）顶回 插电=最大档 / 电池=MIN_RATE。按 ① 去掉（auto 关闭时它本来就只是空转）。
					// ⚠️ 若将来发现切显卡模式后刷新率会掉到非预期档，可单独把这一条加回来。
				});
				if (eco == 0)
				{
					if (AppConfig.IsNVPlatform() || nvRestartPending)
					{
						await settings.InvokeAsync(() =>
						{
							settings.LockGPUModes(Strings.RestartingNVServices);
						});
						await Task.Delay(TimeSpan.FromMilliseconds(AppConfig.Get("nv_delay", 5000)));
						if (AppConfig.IsNVPlatform())
						{
							NvidiaGpuControl.RestartNVService();
						}
						else
						{
							NvidiaGpuControl.RestartNvContainer();
						}
						nvRestartPending = false;
						await settings.InvokeAsync(InitGPUMode);
						await Task.Delay(TimeSpan.FromMilliseconds(1000L));
					}
					for (int i = 0; i < 3; i++)
					{
						HardwareControl.RecreateGpuControl();
						if (HardwareControl.GpuControl != null)
						{
							break;
						}
						await Task.Delay(TimeSpan.FromSeconds(2L));
					}
					Program.modeControl.SetGPUClocks();
				}
				if (AppConfig.IsModeReapplyRequired())
				{
					await Task.Delay(TimeSpan.FromMilliseconds(3000L));
					Program.modeControl.AutoPerformance();
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Error setting GPU Eco: " + ex.Message);
			}
		});
	}

	public static bool IsPlugged()
	{
		return Program.currentSource == Program.PowerSource.Barrel;
	}

	public void KillGPUApps()
	{
		if (HardwareControl.GpuControl != null)
		{
			HardwareControl.GpuControl.KillGPUApps();
		}
	}

	public void CaptureNvBootState()
	{
		nvRestartPending = Program.acpi.IsNVidiaGPU() && Program.acpi.DeviceGet(0u) == 1;
	}

	public void StandardModeFix()
	{
		if (AppConfig.IsStandardModeFix() && Program.acpi.DeviceGet(589846u) != 0)
		{
			Logger.WriteLine("Forcing Standard Mode on shutdown");
			Program.acpi.SetGPUEco(0);
		}
	}

	private void CheckGpuError()
	{
		string text = DeviceHelper.GetGpuError();
		if (gpuError != text)
		{
			gpuError = text;
			if (text != null)
			{
				Logger.WriteLine(text);
			}
			settings.VisualiseGPUMode();
		}
	}
}
