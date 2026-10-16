using System;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

namespace MechrevoLite;

public class AsusACPI
{
	public const uint ScreenOverdrive = 327705u;

	public const uint ScreenMiniled1 = 327710u;

	public const uint ScreenMiniled2 = 327726u;

	public const uint ScreenFHD = 327708u;

	public const uint ScreenHDRControl = 327793u;

	public const uint ScreenOptimalBrightness = 327722u;

	public const int PerformanceBalanced = 0;

	public const int PerformanceTurbo = 1;

	public const int PerformanceSilent = 2;

	public const int PerformanceManual = 4;

	public const int GPUModeEco = 0;

	public const int GPUModeStandard = 1;

	public const int GPUModeUltimate = 2;

	public const int GPUEco = 0;

	public const uint GPUMux = 589846u;

	public const int MinTotal = 5;

	public const int MinCPU = 5;

	public const int MaxCPU = 210;

	public const int MaxTotal = 210;

	public const int NotSupported = -1;

	private static MechrevoDeviceCapabilities Capabilities => Program.hw?.Capabilities ?? MechrevoDeviceCapabilities.Current;

	public static bool IsInvalidCurve(byte[] curve)
	{
		if (curve == null || curve.Length != 16)
		{
			return true;
		}
		for (int i = 1; i < 8; i++)
		{
			if (curve[i] < 30 && curve[i] != 0)
			{
				return true;
			}
		}
		return false;
	}

	public bool IsConnected()
	{
		return Program.hw?.IsConnected ?? false;
	}

	public int DeviceGet(uint code, int len = 4)
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			return -1;
		}
		return code switch
		{
			0u => Program.hw.GpuEcoFlag, 
			589846u => Program.hw.GpuMuxFlag, 
			_ => -1, 
		};
	}

	public async Task SetPerformanceMode(int mode, string logName = "Mode")
	{
		if (Program.hw == null)
		{
			return;
		}
		try
		{
			if (Program.service != null)
			{
				await Program.service.SwitchMode(mode);
			}
			else
			{
				await Program.hw.SetMode(mode);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetMode fail: " + ex.Message);
		}
	}

	public int GetFan(AsusFan device)
	{
		MechrevoHw hw = Program.hw;
		if (hw != null && hw.IsConnected)
		{
			if (device != AsusFan.CPU)
			{
				return Program.hw.GpuFanRpm;
			}
			return Program.hw.CpuFanRpm;
		}
		return -1;
	}

	public bool IsAllAmdPPT()
	{
		return Capabilities.AmdPlatform;
	}

	public bool IsNVidiaGPU()
	{
		return Capabilities.NvidiaGpu;
	}

	public bool IsMidFanSupported()
	{
		int? midFan = HardwareControl.midFan;
		return midFan.HasValue;
	}

	public bool IsXGConnected()
	{
		return false;
	}

	public bool IsOverdriveSupported()
	{
		return Capabilities.LcdOverdrive;
	}

	public bool IsSupported(uint code)
	{
		switch (code)
		{
		case 0u:
		case 589846u:
			return Program.hw?.IsConnected ?? false;
		case 327705u:
			return Capabilities.LcdOverdrive;
		case 327710u:
		case 327726u:
			return Capabilities.LocalDimming;
		default:
			return false;
		}
	}

	public int SetGPUEco(int eco)
	{
		return 0;
	}
}
