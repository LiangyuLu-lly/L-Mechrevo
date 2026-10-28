// 2026-10-17（WiFi/蓝牙本地开关 · 只读探针）：
// ⭐ 目的：确认在我们的目标框架（net10.0-windows10.0.19041.0 · 未打包桌面程序 · 管理员）
//    下，`Windows.Devices.Radios` 真的可用（⭐ 依据：40 系 GCU 反编译里就是这套 ——
//    `DeviceSwitchItem.cs` / `BLE.cs` 都 `using Windows.Devices.Radios;`）。
// ⛔ 本探针**只读**：枚举 Radio、打印 Kind/State、请求访问权限，**绝不切换任何状态**。
//
// 跑法：dotnet run --project tools\dev\radio-probe

using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

internal static class Program
{
	private static async Task<int> Main()
	{
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		Console.WriteLine("=== Windows.Devices.Radios 只读探针 ===");

		RadioAccessStatus access;
		try
		{
			access = await Radio.RequestAccessAsync();
			Console.WriteLine($"RequestAccessAsync ⇒ {access}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"RequestAccessAsync ❌ 抛异常：{ex.GetType().Name} {ex.Message}");
			return 2;
		}

		try
		{
			DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(Radio.GetDeviceSelector());
			Console.WriteLine($"Radio 设备数 = {devices.Count}");
			foreach (DeviceInformation device in devices)
			{
				Radio radio;
				try
				{
					radio = await Radio.FromIdAsync(device.Id);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"  {device.Name}: FromIdAsync ❌ {ex.GetType().Name} {ex.Message}");
					continue;
				}
				Console.WriteLine($"  Kind={radio.Kind} · State={radio.State} · Name={radio.Name}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"FindAllAsync ❌ 抛异常：{ex.GetType().Name} {ex.Message}");
			return 3;
		}

		Console.WriteLine("（只读探针结束；未切换任何状态）");
		return 0;
	}
}
