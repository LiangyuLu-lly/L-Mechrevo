// ⭐⭐ 2026-10-17（WiFi / 蓝牙「本地开关」· 不依赖 GCU）：
//   ⭐ 依据（40 系 GCU ，只取**做法**）：`GCUService\GCService5\DeviceSwitchItem.cs` 与 `BLE.cs`
//     都 `using Windows.Devices.Radios;` ⇒ 厂商自己就是用 **Windows Radio API** 切这两个开关，
//     ⛔ 不是"禁用设备"那种重手段。
//
//   ⭐ 本机只读探针（tools\dev\radio-probe）已确认：在 `net10.0-windows10.0.19041.0` ·
//     未打包 · 管理员 下 `Radio.RequestAccessAsync()` ⇒ **Allowed**，能枚举到 WiFi / 蓝牙两个 Radio ✅
//
//   ⚠️ 边界（决定 UI 怎么写）：
//     · 访问被拒（组策略等）⇒ `Available=false` ⇒ 开关**不显示**（⭐ 能点的必须有反应 ✅）
//     · `RadioState.Disabled`（机身物理开关 / 飞行模式 / 被禁用）⇒ 软件**打不开**，如实返回原因 ✅
//     · 写后**回读**再确认（⛔ 不"调用成功就报成功"）
//
//   ⚠️ 非宝龙达（GCU 机型）**不启用**本类：`MechrevoService.SwitchQuick` 里只对
//     `_hw.UsesBaolongda` 分派 ⇒ GCU 那条 MQTT 路径**逐位不变** ✅

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace MechrevoLite.Helpers;

/// <summary>⭐ 本地 Radio 开关的两种目标（⭐ 与 <see cref="RadioKind" /> 一一对应）。</summary>
internal enum RadioSwitchKind
{
	Wifi,
	Bluetooth
}

/// <summary>
/// ⭐⭐ 2026-10-17：WiFi / 蓝牙的**本地**开关（Windows.Devices.Radios）。
/// <para>⭐ 用途：宝龙达没有 GCU ⇒ 这两个开关原本靠 GCU 上报/下发 ⇒ 恒不可用。
/// 本类给出"读状态 + 切状态 + 回读确认"的最小实现 ✅。</para>
/// <para>⚠️ 后台 3 s 轮询一次，缓存快照 ⇒ UI 侧（<c>SupportsQuickSwitch</c> / 状态同步）**同步读缓存**，
/// ⛔ 不阻塞 UI 线程、⛔ 不在 UI 线程上等待 WinRT ✅。</para>
/// </summary>
internal static class RadioSwitch
{
	private sealed class Entry
	{
		internal bool Available;
		internal bool On;
		internal string StateText = "未知";
		internal string Note = string.Empty;
	}

	private static readonly object Gate = new object();

	private static readonly Dictionary<RadioSwitchKind, Entry> Cache = new Dictionary<RadioSwitchKind, Entry>
	{
		[RadioSwitchKind.Wifi] = new Entry(),
		[RadioSwitchKind.Bluetooth] = new Entry()
	};

	private static int _started;

	// 缓存 Radio 对象：600ms 轮询时不再每次 FindAll/FromId（那两次 WinRT 枚举才是主要开销），
	// 只读缓存的 Radio.State；15s 或读失败时才重新枚举。
	private static readonly Dictionary<RadioSwitchKind, Radio> CachedRadios = new();
	private static long _lastRadioEnumerationTick;

	private static RadioAccessStatus? _access;

	/// <summary>⭐ 可用性变化回调（⭐ 由 MechrevoHw 挂上 ⇒ 让界面重新评估这两个开关是否显示）。</summary>
	internal static Action AvailabilityChanged;

	/// <summary>系统/硬件 Radio 状态变化（key=WiFi/蓝牙，on=当前状态）。</summary>
	internal static event Action<RadioSwitchKind, bool>? StateChanged;

	/// <summary>⭐ 启动后台轮询（⭐ 幂等；只在宝龙达后端启动时调一次）。</summary>
	internal static void Start()
	{
		if (Interlocked.Exchange(ref _started, 1) != 0)
		{
			return;
		}
		// 首扫稍等启动，之后 600ms 一次。
		// 这个节拍和 OpenRevo 的 slow track 一致（50ms 主循环 × 12 tick）：
		// WinRT Radio 读取约 0.5ms，不会造成 CPU 负担；3s 一次会让系统开关看起来“没反应”。
		_ = Task.Run(async delegate
		{
			await Task.Delay(200).ConfigureAwait(continueOnCapturedContext: false);
			while (true)
			{
				await PollOnceAsync().ConfigureAwait(continueOnCapturedContext: false);
				await Task.Delay(600).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	/// <summary>⭐ 是否可用（⭐ 已探测到该 Radio 且访问已获准）。</summary>
	internal static bool Available(RadioSwitchKind kind)
	{
		lock (Gate)
		{
			return Cache.TryGetValue(kind, out Entry entry) && entry.Available;
		}
	}

	/// <summary>⭐ 读缓存的状态（⭐ on = 当前是否开启；note 给出 RadioState 原文，便于日志/诊断）。</summary>
	internal static bool TryGet(RadioSwitchKind kind, out bool on, out string note)
	{
		lock (Gate)
		{
			if (Cache.TryGetValue(kind, out Entry entry) && entry.Available)
			{
				on = entry.On;
				note = entry.StateText;
				return true;
			}
		}
		on = false;
		note = "不可用（未探测到或无访问权限）";
		return false;
	}

	/// <summary>
	/// ⭐⭐ 切换一个 Radio（⭐ 写后回读确认）。
	/// <para>返回 <c>(ok, note)</c>：<c>ok</c> = **回读与目标一致**；
	/// <c>note</c> 是给人看的原因（失败也要说明白：权限 / 硬件开关 / 回读不一致）。</para>
	/// </summary>
	internal static async Task<(bool ok, string note)> SetAsync(RadioSwitchKind kind, bool on)
	{
		try
		{
			RadioAccessStatus access = await EnsureAccessAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (access != RadioAccessStatus.Allowed)
			{
				return (false, $"无 Radio 访问权限（{access}）");
			}
			DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(Radio.GetDeviceSelector())
				.AsTask().ConfigureAwait(continueOnCapturedContext: false);
			foreach (DeviceInformation device in devices)
			{
				Radio radio = await Radio.FromIdAsync(device.Id).AsTask().ConfigureAwait(continueOnCapturedContext: false);
				if (radio == null || MapKind(radio.Kind) != kind)
				{
					continue;
				}
				// ⭐ 硬件开关 / 飞行模式 / 被禁用：⭐ 软件打不开 ⇒ 如实说明，⛔ 不谎报成功
				//   ⚠️ 注意：本投影的 `RadioState` 只有 Unknown / On / Off / Disabled 三个可用值
				//      （没有 HardwareOff 这个成员）⇒ 用"既不是 On 也不是 Off"来判定不可切换 ✅
				if (on && radio.State != RadioState.Off && radio.State != RadioState.On)
				{
					UpdateCache(kind, radio.State);
					return (false, $"当前状态为 {radio.State}（硬件开关 / 飞行模式 / 被禁用）⇒ 软件打不开");
				}
				RadioAccessStatus setResult = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off)
					.AsTask().ConfigureAwait(continueOnCapturedContext: false);
				if (setResult != RadioAccessStatus.Allowed)
				{
					UpdateCache(kind, radio.State);
					return (false, $"SetState 被拒绝（{setResult}）");
				}
				await Task.Delay(400).ConfigureAwait(continueOnCapturedContext: false);
				Radio after = await Radio.FromIdAsync(device.Id).AsTask().ConfigureAwait(continueOnCapturedContext: false);
				RadioState state = after?.State ?? RadioState.Unknown;
				UpdateCache(kind, state);
				bool ok = on ? state == RadioState.On : state == RadioState.Off;
				return (ok, ok
					? $"已{(on ? "打开" : "关闭")}（回读 {state} ✅）"
					: $"写后回读不一致（{state}）⚠️");
			}
			return (false, "未找到对应的 Radio 设备");
		}
		catch (Exception ex)
		{
			return (false, ex.GetType().Name + " " + ex.Message);
		}
	}

	private static async Task PollOnceAsync()
	{
		try
		{
			RadioAccessStatus access = await EnsureAccessAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (access != RadioAccessStatus.Allowed)
			{
				SetUnavailableAll($"无 Radio 访问权限（{access}）");
				return;
			}
			bool wasWifi = Available(RadioSwitchKind.Wifi);
			bool wasBt = Available(RadioSwitchKind.Bluetooth);
			long now = Environment.TickCount64;
			if (CachedRadios.Count == 0 || now - _lastRadioEnumerationTick > 15000)
			{
				CachedRadios.Clear();
				DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(Radio.GetDeviceSelector())
					.AsTask().ConfigureAwait(continueOnCapturedContext: false);
				foreach (DeviceInformation device in devices)
				{
					Radio radio = await Radio.FromIdAsync(device.Id).AsTask().ConfigureAwait(continueOnCapturedContext: false);
					if (radio == null)
					{
						continue;
					}
					RadioSwitchKind? kind = MapKind(radio.Kind);
					if (kind.HasValue)
					{
						CachedRadios[kind.Value] = radio;
					}
				}
				_lastRadioEnumerationTick = now;
			}
			bool sawWifi = false;
			bool sawBt = false;
			foreach (KeyValuePair<RadioSwitchKind, Radio> pair in CachedRadios)
			{
				try
				{
					UpdateCache(pair.Key, pair.Value.State);
					if (pair.Key == RadioSwitchKind.Wifi) sawWifi = true;
					else sawBt = true;
				}
				catch
				{
					CachedRadios.Remove(pair.Key);
					break;
				}
			}
			if (!sawWifi)
			{
				SetUnavailable(RadioSwitchKind.Wifi, "本机没有 WiFi 无线电（或已禁用）");
			}
			if (!sawBt)
			{
				SetUnavailable(RadioSwitchKind.Bluetooth, "本机没有蓝牙无线电（或已禁用）");
			}
			if (wasWifi != Available(RadioSwitchKind.Wifi) || wasBt != Available(RadioSwitchKind.Bluetooth))
			{
				try
				{
					AvailabilityChanged?.Invoke();
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			CachedRadios.Clear();
			SetUnavailableAll(ex.GetType().Name + " " + ex.Message);
		}
	}

	private static async Task<RadioAccessStatus> EnsureAccessAsync()
	{
		RadioAccessStatus? cached = _access;
		if (cached.HasValue)
		{
			return cached.Value;
		}
		try
		{
			RadioAccessStatus status = await Radio.RequestAccessAsync().AsTask().ConfigureAwait(continueOnCapturedContext: false);
			_access = status;
			return status;
		}
		catch
		{
			_access = RadioAccessStatus.DeniedBySystem;
			return RadioAccessStatus.DeniedBySystem;
		}
	}

	private static RadioSwitchKind? MapKind(RadioKind kind)
	{
		return kind switch
		{
			RadioKind.WiFi => RadioSwitchKind.Wifi,
			RadioKind.Bluetooth => RadioSwitchKind.Bluetooth,
			_ => null
		};
	}

	private static void UpdateCache(RadioSwitchKind kind, RadioState state)
	{
		bool changed;
		bool on;
		lock (Gate)
		{
			Entry entry = Cache[kind];
			bool wasAvailable = entry.Available;
			bool wasOn = entry.On;
			entry.Available = true;
			entry.On = state == RadioState.On;
			entry.StateText = state.ToString();
			entry.Note = string.Empty;
			on = entry.On;
			changed = !wasAvailable || wasOn != on;
		}
		if (changed)
		{
			try
			{
				StateChanged?.Invoke(kind, on);
			}
			catch
			{
			}
		}
	}

	private static void SetUnavailable(RadioSwitchKind kind, string note)
	{
		lock (Gate)
		{
			Entry entry = Cache[kind];
			entry.Available = false;
			entry.On = false;
			entry.StateText = "不可用";
			entry.Note = note;
		}
	}

	private static void SetUnavailableAll(string note)
	{
		SetUnavailable(RadioSwitchKind.Wifi, note);
		SetUnavailable(RadioSwitchKind.Bluetooth, note);
	}
}
