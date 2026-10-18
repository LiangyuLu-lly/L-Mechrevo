// ⭐⭐ 2026-10-17（摄像头「系统隐私开关」· 不依赖 GCU）：
//   宝龙达没有 GCU ⇒ 原来的 WEBCAM_ON/OFF 走 MQTT 必然失败 ⇒ 开关恒"点了没反应"。
//   摄像头**不是无线电**（不能走 Windows.Devices.Radios）⇒ 用 Windows 的**摄像头隐私授权**：
//     HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
//   其中 `Value`（打包应用）与 `NonPackaged\Value`（桌面应用）取值 `Allow` / `Deny`。
//
//   ⚠️⚠️ 语义必须如实（UI 文案要写清）：这是「系统隐私开关」，等价于
//     设置 → 隐私和安全性 → 摄像头 →「让应用使用摄像头 / 让桌面应用使用摄像头」。
//     · 立即生效（不需要重启）
//     · 但**不是** EC/固件级断电：个别绕过系统相机栈的旧程序**可能**不受影响
//     · 需要管理员（LM 的 manifest 是 requireAdministrator ✅）
//   ⚠️ 非宝龙达（GCU 机型）不启用：分派处带 `UsesBaolongda` 判据 ⇒ GCU 路径逐位不变 ✅

using System;
using Microsoft.Win32;

namespace MechrevoLite.Helpers;

/// <summary>⭐ 摄像头隐私开关（注册表 ConsentStore）—— 只做"读状态 / 写状态 / 回读确认"。</summary>
internal static class CameraPrivacy
{
	private const string KeyRoot =
		@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

	private const string ValueName = "Value";

	private const string Allow = "Allow";

	private const string Deny = "Deny";

	/// <summary>⭐ 本机是否支持（⭐ 键存在且可写）。⚠️ 读不到就当作不支持 ⇒ 界面不显示该开关 ✅。</summary>
	internal static bool Available
	{
		get
		{
			try
			{
				using RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyRoot, writable: true);
				return key != null;
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>
	/// ⭐ 读"当前是否禁止使用摄像头"。
	/// <para>⭐ 判据：`NonPackaged`（桌面应用）优先 —— LM 的开关面向的是"这台机器上的程序"，
	/// 而桌面程序看的就是这一项；读不到再退到父键（打包应用用）。</para>
	/// </summary>
	internal static bool TryGetDenied(out bool denied, out string note)
	{
		denied = false;
		note = string.Empty;
		try
		{
			using RegistryKey root = Registry.LocalMachine.OpenSubKey(KeyRoot, writable: false);
			if (root == null)
			{
				note = "读不到摄像头隐私键";
				return false;
			}
			string nonPackaged = ReadValue(root, "NonPackaged");
			string packaged = ReadValue(root, string.Empty);
			string effective = (nonPackaged.Length > 0) ? nonPackaged : packaged;
			if (effective.Length == 0)
			{
				note = "两项都读不到（当作未知）";
				return false;
			}
			denied = string.Equals(effective, Deny, StringComparison.OrdinalIgnoreCase);
			note = $"桌面应用={(nonPackaged.Length > 0 ? nonPackaged : "读不到")} · 打包应用={(packaged.Length > 0 ? packaged : "读不到")}";
			return true;
		}
		catch (Exception ex)
		{
			note = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 写"是否禁止使用摄像头"（⭐ 两项都写：打包应用 + 桌面应用），然后**回读确认** ✅。
	/// <para>⚠️ 回读只认 `NonPackaged`（桌面应用那一项）—— 它是本开关的实际作用面。</para>
	/// </summary>
	internal static bool TrySetDenied(bool deny, out string note)
	{
		note = string.Empty;
		string want = deny ? Deny : Allow;
		try
		{
			using (RegistryKey root = Registry.LocalMachine.OpenSubKey(KeyRoot, writable: true))
			{
				if (root == null)
				{
					note = "没有写权限或键不存在";
					return false;
				}
				root.SetValue(ValueName, want, RegistryValueKind.String);
				// ⭐ 桌面应用那一项单独一个子键（⭐ 不存在就建）
				using (RegistryKey nonPackaged = root.CreateSubKey("NonPackaged", writable: true))
				{
					if (nonPackaged == null)
					{
						note = "建不了 NonPackaged 子键";
						return false;
					}
					nonPackaged.SetValue(ValueName, want, RegistryValueKind.String);
				}
			}
			// ⭐ 回读确认（⛔ 不"写完就报成功"）
			if (!TryGetDenied(out bool nowDenied, out string readNote))
			{
				note = "写后回读失败：" + readNote;
				return false;
			}
			if (nowDenied != deny)
			{
				note = $"写后回读不一致（期望 {(deny ? Deny : Allow)}，实际 {(nowDenied ? Deny : Allow)}）";
				return false;
			}
			note = $"已写 {want} 并回读确认（{readNote}）";
			return true;
		}
		catch (Exception ex)
		{
			note = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	private static string ReadValue(RegistryKey root, string subKey)
	{
		try
		{
			if (subKey.Length == 0)
			{
				return root.GetValue(ValueName) as string ?? string.Empty;
			}
			using RegistryKey child = root.OpenSubKey(subKey, writable: false);
			return child?.GetValue(ValueName) as string ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
