using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

/// <summary>
/// 2026-10-11（用户拍板「要它，另开一轮做麦克风静音开关」）：**默认录音端点**的静音开关。
///
/// 路径（用户指定「最可靠路径：Windows CoreAudio 端点静音」）：
///   <c>MMDeviceEnumerator.GetDefaultAudioEndpoint(eCapture, eConsole)</c>
///   → <c>IMMDevice.Activate(IID_IAudioEndpointVolume)</c>
///   → <c>IAudioEndpointVolume::SetMute / GetMute</c>。
///
/// 设计要点：
/// <list type="bullet">
/// <item><b>能力判据</b>＝「本机存在**活动的默认录音端点**且它能被激活成 IAudioEndpointVolume，
/// 并且 GetMute 能读回」。三者任一失败 ⇒ <see cref="Available" /> 为 false ⇒
/// 界面侧（<c>MechrevoHw.SupportsQuickSwitch("micmute")</c>）按能力位**隐藏**该开关与磁贴，
/// ⛔ 不显示一个点不动的开关（用户原话「否则按能力位隐藏或置灰，不要显示出点不动的开关」）。
/// 无录音设备（含全部录音端点被禁用/拔出）时 <c>GetDefaultAudioEndpoint</c> 返回
/// <c>E_NOTFOUND</c>(0x80070490) ⇒ 判为不支持。</item>
/// <item><b>状态回读</b>：<see cref="IsMuted" /> 复用同一次激活，带 <see cref="ProbeIntervalMs" />
/// 的短缓存（避免界面定时器每秒枚举一次设备树）；<see cref="TrySetMute" /> **总是**现场下发
/// 并现场读回，不读缓存 ⇒ 下发后的界面状态不会滞后。</item>
/// <item><b>副作用边界</b>：只调用 <c>SetMute</c>（= 系统音量状态，与用户在「声音设置」里点静音等价），
/// ⛔ 不写注册表、⛔ 不写 EC/NVRAM、⛔ 不改电源计划、⛔ 不落任何 AppConfig 键。</item>
/// <item>线程：全部 COM 调用都在调用者线程上同步完成（界面路径即 STA 的 UI 线程）。
/// 任何异常都被吞掉并折算成「不可用/失败」——静音开关失灵绝不许影响主界面。</item>
/// </list>
/// </summary>
internal static class AudioEndpointMic
{
	/// <summary>CLSCTX_ALL（mmdeviceapi.h 的 CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER）。</summary>
	private const int ClsCtxAll = 23;

	/// <summary>EDataFlow.eCapture（mmdeviceapi.h）。</summary>
	private const int DataFlowCapture = 1;

	/// <summary>ERole.eConsole（mmdeviceapi.h）—— 与系统「声音设置」默认设备一致的那一个。</summary>
	private const int RoleConsole = 0;

	/// <summary>状态缓存时长：界面定时器最密每秒一轮，这里 3s 足够且把设备枚举开销摊薄。</summary>
	private const long ProbeIntervalMs = 3000L;

	private static readonly Guid IidAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

	private static readonly object Gate = new object();

	private static bool _probed;

	private static long _lastProbeTick;

	private static bool _available;

	private static bool _muted;

	private static string _lastNote = "尚未探测";

	/// <summary>本机当前是否有可静音的默认录音端点。界面能力位就是它。</summary>
	internal static bool Available
	{
		get
		{
			Probe(force: false);
			lock (Gate)
			{
				return _available;
			}
		}
	}

	/// <summary>默认录音端点当前是否处于静音。不可用时为 false（调用方应先看 <see cref="Available" />）。</summary>
	internal static bool IsMuted
	{
		get
		{
			Probe(force: false);
			lock (Gate)
			{
				return _muted;
			}
		}
	}

	/// <summary>最近一次探测/下发的说明（只用于日志，不含任何设备 ID/名称）。</summary>
	internal static string LastNote
	{
		get
		{
			lock (Gate)
			{
				return _lastNote;
			}
		}
	}

	/// <summary>
	/// 现场把默认录音端点设成 <paramref name="mute" /> 并**现场读回**校验。
	/// 返回 true ＝ 读回值与请求值一致。失败时 <paramref name="note" /> 给出原因（不含设备 ID）。
	/// </summary>
	internal static bool TrySetMute(bool mute, out string note)
	{
		note = "";
		object enumeratorObj = null;
		object deviceObj = null;
		object volumeObj = null;
		try
		{
			IAudioEndpointVolume volume = ActivateVolume(ref enumeratorObj, ref deviceObj, ref volumeObj, out note);
			if (volume == null)
			{
				return false;
			}
			Guid eventContext = Guid.Empty;
			int hrSet = volume.SetMute(mute, ref eventContext);
			if (hrSet != 0)
			{
				note = $"SetMute 返回 0x{hrSet:X8}";
				return false;
			}
			bool readBack;
			int hrGet = volume.GetMute(out readBack);
			if (hrGet != 0)
			{
				note = $"SetMute 后 GetMute 返回 0x{hrGet:X8}";
				return false;
			}
			lock (Gate)
			{
				_probed = true;
				_lastProbeTick = Environment.TickCount64;
				_available = true;
				_muted = readBack;
				_lastNote = $"SetMute({mute}) → 读回 {readBack}";
			}
			if (readBack != mute)
			{
				note = $"读回值与请求值不一致（请求 {mute}，读回 {readBack}）";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			note = "COM 异常：" + ex.Message;
			lock (Gate)
			{
				_lastNote = note;
			}
			return false;
		}
		finally
		{
			// ⚠️ 只释放**创建出来的那三个 RCW**（enumeratorObj/deviceObj/volumeObj）：
			//    上面那三个强类型变量都是同一个 RCW 的接口视图，重复 Release 会重复减引用计数。
			Release(volumeObj);
			Release(deviceObj);
			Release(enumeratorObj);
		}
	}

	/// <summary>
	/// 探测/刷新缓存。<paramref name="force" /> = true 时无条件重探。
	/// 无论成功失败都不抛异常：一切异常都折算成 <c>_available=false</c>。
	/// </summary>
	private static void Probe(bool force)
	{
		lock (Gate)
		{
			if (!force && _probed && Environment.TickCount64 - _lastProbeTick < ProbeIntervalMs)
			{
				return;
			}
		}
		object enumeratorObj = null;
		object deviceObj = null;
		object volumeObj = null;
		string note = "";
		bool available = false;
		bool muted = false;
		try
		{
			IAudioEndpointVolume volume = ActivateVolume(ref enumeratorObj, ref deviceObj, ref volumeObj, out note);
			if (volume != null)
			{
				int hr = volume.GetMute(out muted);
				if (hr == 0)
				{
					available = true;
					note = "默认录音端点可静音，当前 " + (muted ? "已静音" : "未静音");
				}
				else
				{
					note = $"GetMute 返回 0x{hr:X8}";
				}
			}
		}
		catch (Exception ex)
		{
			note = "COM 异常：" + ex.Message;
		}
		finally
		{
			Release(volumeObj);
			Release(deviceObj);
			Release(enumeratorObj);
		}
		lock (Gate)
		{
			_probed = true;
			_lastProbeTick = Environment.TickCount64;
			_available = available;
			_muted = muted;
			_lastNote = note;
		}
	}

	/// <summary>
	/// 激活链：枚举器 → 默认录音端点 → IAudioEndpointVolume。
	/// 失败返回 null 并给 <paramref name="note" /> 原因；成功返回 IAudioEndpointVolume，
	/// 同时把**三个新建的 RCW** 经 ref 交给调用方（由调用方 <see cref="Release" />，本方法自己不释放）。
	/// </summary>
	private static IAudioEndpointVolume ActivateVolume(ref object enumeratorObj, ref object deviceObj, ref object volumeObj, out string note)
	{
		enumeratorObj = null;
		deviceObj = null;
		volumeObj = null;
		note = "";
		enumeratorObj = new MMDeviceEnumerator();
		IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObj;
		IMMDevice defaultDevice;
		int hrDevice = enumerator.GetDefaultAudioEndpoint(DataFlowCapture, RoleConsole, out defaultDevice);
		if (hrDevice != 0 || defaultDevice == null)
		{
			// 没有活动录音设备（全被禁用/拔出/本机无麦克风）时就是这里失败（常见 E_NOTFOUND 0x80070490）。
			note = $"没有可用的默认录音端点（GetDefaultAudioEndpoint 返回 0x{hrDevice:X8}）";
			return null;
		}
		deviceObj = defaultDevice;
		Guid iid = IidAudioEndpointVolume;
		object activated;
		int hrActivate = defaultDevice.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out activated);
		if (hrActivate != 0 || activated == null)
		{
			note = $"IAudioEndpointVolume 激活失败（0x{hrActivate:X8}）";
			return null;
		}
		volumeObj = activated;
		return (IAudioEndpointVolume)activated;
	}

	private static void Release(object comObject)
	{
		if (comObject == null)
		{
			return;
		}
		try
		{
			if (Marshal.IsComObject(comObject))
			{
				Marshal.ReleaseComObject(comObject);
			}
		}
		catch (Exception)
		{
			// 释放失败只影响本进程句柄回收，不许冒泡。
		}
	}
}

/// <summary>MMDeviceEnumerator 的 CoClass（mmdeviceapi.h CLSID_MMDeviceEnumerator）。</summary>
[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator
{
}

/// <summary>IMMDeviceEnumerator —— 只声明到本功能用得到的槽位（vtable 顺序必须与 mmdeviceapi.h 一致）。</summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
	int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

	int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

	int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

	int RegisterEndpointNotificationCallback(IntPtr client);

	int UnregisterEndpointNotificationCallback(IntPtr client);
}

/// <summary>IMMDeviceCollection —— 只为占住 IMMDeviceEnumerator 的 vtable 槽位。</summary>
[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
	int GetCount(out int count);

	int Item(int index, out IMMDevice device);
}

/// <summary>IMMDevice —— 本功能只用 Activate（OpenPropertyStore 仅为占槽）。</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
	int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

	int OpenPropertyStore(int stgmAccess, out IntPtr properties);
}

/// <summary>
/// IAudioEndpointVolume（endpointvolume.h）—— 槽位**必须**与头文件完全一致（本功能只用到 SetMute/GetMute，
/// 但它们分别是第 12/13 个方法，前面的槽位一个都不能少，否则会调到别的函数上）。
/// </summary>
[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
	int RegisterControlChangeNotify(IntPtr notify);

	int UnregisterControlChangeNotify(IntPtr notify);

	int GetChannelCount(out int channelCount);

	int SetMasterVolumeLevel(float level, ref Guid eventContext);

	int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

	int GetMasterVolumeLevel(out float level);

	int GetMasterVolumeLevelScalar(out float level);

	int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);

	int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

	int GetChannelVolumeLevel(uint channel, out float level);

	int GetChannelVolumeLevelScalar(uint channel, out float level);

	int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

	int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

	int GetVolumeStepInfo(out uint step, out uint stepCount);

	int VolumeStepUp(ref Guid eventContext);

	int VolumeStepDown(ref Guid eventContext);

	int QueryHardwareSupport(out uint hardwareSupportMask);

	int GetVolumeRange(out float min, out float max, out float increment);
}
