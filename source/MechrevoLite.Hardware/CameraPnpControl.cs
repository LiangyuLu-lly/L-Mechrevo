// ⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：
//   ⚠️ 宝龙达**没有摄像头功能号、也没有 GCU** ⇒ 原来的 `WEBCAM_ON/OFF`（厂商 MQTT 通道）必然无效；
//      之前的"摄像头隐私授权"只是系统隐私开关，治标不治本。机主批准后改为：
//   ⭐ 直接经 Windows SetupAPI **禁用/启用摄像头设备**（通用、不依赖厂商通道）✅
//
//   ⭐ 做法：
//     · 枚举：SetupDiGetClassDevs(Camera 类 {ca3e7ab9-…} → Image 类 {6bdd1fc6-…}) + SetupDiEnumDeviceInfo；
//     · 识别：Camera 类设备默认认；Image 类**必须**名字含 camera/webcam/摄像头关键字
//             （Image 类里混着扫描仪，⛔ 不能一起禁）；虚拟摄像头 / Windows Hello 红外相机**不碰**；
//     · 禁用/启用：SetupDiSetClassInstallParams(DIF_PROPERTYCHANGE, DICS_FLAG_GLOBAL)
//                   + SetupDiChangeState(DICS_DISABLE / DICS_ENABLE)；
//     · 状态回读：CM_Get_DevNode_Status（problem == 22 = CM_PROB_DISABLED）
//                 ＋ SPDRP_CONFIGFLAGS（CONFIGFLAG_DISABLED = 0x1）—— 双判据，任一成立即判已禁用。
//
//   ⚠️ 需要管理员：LM 的 manifest 是 requireAdministrator ✅；非提权时 DICS_* 会返回
//      ERROR_ACCESS_DENIED(5) ⇒ 日志**如实**写「需要管理员」，⛔ 不假装成功。
//   ⛔ 本文件只做摄像头设备的枚举 / 开关 / 状态回读；⛔ 不改任何厂商下发协议
//      （GCU 机型的 WEBCAM_ON/OFF 那条原路逐位不变；无摄像头设备时才退回旧的隐私键兜底）。

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 摄像头设备（Windows PnP）控制器：枚举 / 禁用 / 启用 / 状态回读。
/// <para>⭐ 全程 Win32 P/Invoke（<c>setupapi.dll</c> + <c>cfgmgr32.dll</c>），⛔ 不引第三方包。</para>
/// <para>⚠️ 所有公开入口都自带 try/catch：顶层只记日志 + 返回 <c>false</c>/<c>detail</c>，⛔ 绝不把异常抛给 UI。</para>
/// </summary>
internal static class CameraPnpControl
{
	// ──────────────────────────────────────────────────────────────────────
	// ⭐ 设备识别：类 GUID + 关键字
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>Camera（摄像头）设备安装类 GUID。</summary>
	private static readonly Guid CameraClassGuid = new Guid("ca3e7ab9-b4c3-4ae6-8251-579ef933890f");

	/// <summary>Image（成像设备）类 GUID —— 不少机器的摄像头挂在这个类下（与扫描仪同类 ⚠️）。</summary>
	private static readonly Guid ImageClassGuid = new Guid("6bdd1fc6-810f-11d0-bec7-08002be2092f");

	private static readonly Guid[] DeviceClassGuids = new Guid[2] { CameraClassGuid, ImageClassGuid };

	private static readonly string[] DeviceClassNames = new string[2] { "Camera", "Image" };

	/// <summary>⛔ 扫描/打印类关键字：Image 类里混着扫描仪，⛔ 绝不能一起禁用。</summary>
	private static readonly string[] ScannerPrinterKeywords = new string[10]
	{
		"scanner", "scan ", "扫描", "扫描仪", "printer", "打印机", "fax", "传真", "adf", "imaging"
	};

	/// <summary>⛔ 虚拟摄像头（OBS / NVIDIA Broadcast / 各类 VCam）不碰 —— 禁掉会破坏用户的直播软件。</summary>
	private static readonly string[] VirtualCameraKeywords = new string[9]
	{
		"virtual", "虚拟", "obs", "snap camera", "manycam", "nvidia broadcast", "e2esoft", "vcam", "droidcam"
	};

	/// <summary>⛔ Windows Hello 红外相机不碰 —— 它是人脸登录设备，不应当作"摄像头开关"的副作用。</summary>
	private static readonly string[] IrHelloKeywords = new string[6]
	{
		"infrared", "红外", "ir camera", "ir webcam", "windows hello", "hello face"
	};

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ Win32 常量（setupapi.h / cfgmgr32.h）
	// ──────────────────────────────────────────────────────────────────────

	private const int ErrorAccessDenied = 5;

	private const int CrSuccess = 0;

	private const uint DigcfPresent = 0x00000002;

	private const uint SpdrpDeviceDesc = 0x00000000;

	private const uint SpdrpClass = 0x00000007;

	private const uint SpdrpConfigFlags = 0x0000000A;

	private const uint SpdrpFriendlyName = 0x0000000C;

	/// <summary><c>CONFIGFLAG_DISABLED</c>：设备被"禁用"时 ConfigFlags 里置的这一位。</summary>
	private const uint ConfigFlagDisabled = 0x00000001;

	private const uint DifPropertyChange = 0x00000012;

	private const uint DicsEnable = 0x00000001;

	private const uint DicsDisable = 0x00000002;

	private const uint DicsFlagGlobal = 0x00000001;

	private const uint DicsFlagConfigSpecific = 0x00000002;

	/// <summary><c>CM_PROB_DISABLED</c>：设备管理器里"已禁用"的设备 problem code = 22。</summary>
	private const uint CmProbDisabled = 22;

	private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

	/// <summary>⭐ 枚举缓存 TTL（3 s 周期状态回读时不必每次重新枚举；写后强制刷缓存 ✅）。</summary>
	private const int CacheTtlMs = 2000;

	private static readonly object Gate = new object();

	private static List<CameraDeviceInfo> _cache;

	private static long _cacheAtMs;

	private static string _cacheDetail = string.Empty;

	/// <summary>⭐ 一台被识别为"摄像头"的 PnP 设备（只读快照）。</summary>
	internal readonly record struct CameraDeviceInfo(string Name, string ClassName, bool Disabled, uint ProblemCode);

	/// <summary>
	/// ⭐ 本机是否存在可控制的摄像头设备（Camera/Image 类，排除扫描仪、虚拟摄像头、Windows Hello 红外相机）。
	/// <para>⚠️ 只读；枚举失败/没有摄像头 ⇒ <c>false</c>（调用方据此决定是否退回隐私键兜底）。</para>
	/// </summary>
	internal static bool HasCamera
	{
		get
		{
			// ⭐⭐ 2026-10-19（机主：后端初始化不要挂在设置窗打开路径上）：首次调用才探测（并缓存），之后只读缓存 ✅
			return (Volatile.Read(ref _hasCameraProbed) == 1) ? _hasCameraCached : ProbeHasCamera();
		}
	}

	private static int _hasCameraProbed;

	private static bool _hasCameraCached;

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：后端初始化不要挂在设置窗打开路径上）：
	/// ⭐ **只读缓存**（未探测过 = false，⛔ **不做 SetupAPI 枚举**）—— 给"打开设置/构建行/算能力位"用 ✅
	/// </summary>
	internal static bool HasCameraCached
	{
		get
		{
			return Volatile.Read(ref _hasCameraProbed) == 1 && _hasCameraCached;
		}
	}

	/// <summary>
	/// ⭐ 明确探测一次摄像头设备并写缓存（启动后空闲后台预热 / 真正需要判断时调用；⛔ 不在设置窗构建路径）✅
	/// </summary>
	internal static bool ProbeHasCamera()
	{
		try
		{
			bool has = TryReadDevices(forceRefresh: true, out IReadOnlyList<CameraDeviceInfo> devices, out string _) && devices.Count > 0;
			_hasCameraCached = has;
			return has;
		}
		catch (Exception)
		{
			_hasCameraCached = false;
			return false;
		}
		finally
		{
			Volatile.Write(ref _hasCameraProbed, 1);
		}
	}


	/// <summary>
	/// ⭐ 读摄像头开关状态：<paramref name="enabled" /> = 「至少有一台摄像头处于启用状态」。
	/// <para>⚠️ 状态源是**设备状态**（<c>CM_Get_DevNode_Status</c> / <c>ConfigFlags</c>），⛔ 不是厂商回显。</para>
	/// <para>⭐ 返回 <c>false</c> = 本机没有可控制的摄像头设备（或枚举失败）⇒ 调用方退回隐私键兜底。</para>
	/// </summary>
	internal static bool TryGetEnabled(out bool enabled, out string detail)
	{
		enabled = false;
		detail = string.Empty;
		try
		{
			if (!TryReadDevices(forceRefresh: false, out IReadOnlyList<CameraDeviceInfo> devices, out string readDetail))
			{
				detail = readDetail;
				return false;
			}
			if (devices.Count == 0)
			{
				detail = "未找到可控制的摄像头设备";
				return false;
			}
			enabled = AnyEnabled(devices);
			detail = Describe(devices);
			return true;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 禁用 / 启用**所有**识别到的摄像头设备，并**写后回读确认**。
	/// <para>⭐ 幂等：已经处于目标状态的设备**一个字节都不写** ✅。</para>
	/// <para>⭐ 返回 <c>true</c> = 写后回读确认已到目标状态（界面据此保留勾选）；<c>false</c> = 未确认
	/// （界面按真实状态回滚）—— ⛔ 不"写完就报成功"。</para>
	/// <para>⚠️ 需要管理员；<c>ERROR_ACCESS_DENIED</c> 会写进 <paramref name="detail" /> 与日志。</para>
	/// </summary>
	internal static bool TrySetEnabled(bool enable, out bool confirmed, out string detail)
	{
		confirmed = false;
		detail = string.Empty;
		string target = enable ? "启用" : "禁用";
		try
		{
			if (!EnumerateRaw(applyChange: true, enableTarget: enable, out List<CameraDeviceInfo> devices,
				out int changed, out List<string> applyErrors, out bool accessDenied, out string scanDetail))
			{
				detail = "枚举失败：" + scanDetail;
				Logger.WriteLine("[camera] ⚠️ 失败：PnP " + target + " 未执行 · " + detail);
				return false;
			}
			if (devices.Count == 0)
			{
				detail = "未找到可控制的摄像头设备";
				Logger.WriteLine("[camera] ⚠️ 失败：PnP " + target + " 未执行 · " + detail);
				return false;
			}
			// ⭐ 写后回读（最多 3 次、150 ms 间隔 —— 禁用/启用是 PnP 操作，驱动卸载/启动可能略滞后 ⚠️）
			IReadOnlyList<CameraDeviceInfo> after = devices;
			string readDetail = Describe(devices);
			bool readbackOk = false;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				readbackOk = TryReadDevices(forceRefresh: true, out after, out readDetail);
				confirmed = readbackOk && MatchesTarget(enable, after);
				if (confirmed)
				{
					break;
				}
				Thread.Sleep(150);
			}
			string failText = (applyErrors.Count > 0) ? " · 写入失败=" + string.Join("；", applyErrors) : string.Empty;
			string adminText = accessDenied ? " · 需要管理员（ERROR_ACCESS_DENIED=5）" : string.Empty;
			string readText = readbackOk ? string.Empty : " · 回读失败：" + readDetail;
			detail = "changed=" + changed + " 台 · 设备=" + Describe(after) + failText + adminText + readText;
			Logger.WriteLine("[camera] PnP " + target + " ⇒ ok=" + confirmed + " · " + detail);
			if (!confirmed)
			{
				Logger.WriteLine("[camera] ⚠️ 失败：PnP " + target + " 写后回读不一致（期望"
					+ (enable ? "至少一台启用" : "全部禁用") + "）⇒ 界面将按真实状态回滚");
			}
			return confirmed;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			Logger.WriteLine("[camera] ⚠️ 失败：PnP " + target + " 异常 · " + detail);
			return false;
		}
	}

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ 枚举（带缓存）
	// ──────────────────────────────────────────────────────────────────────

	private static bool TryReadDevices(bool forceRefresh, out IReadOnlyList<CameraDeviceInfo> devices, out string detail)
	{
		lock (Gate)
		{
			long now = Environment.TickCount64;
			if (!forceRefresh && _cache != null && now - _cacheAtMs <= CacheTtlMs)
			{
				devices = _cache;
				detail = _cacheDetail;
				return true;
			}
			bool ok = EnumerateRaw(applyChange: false, enableTarget: false, out List<CameraDeviceInfo> fresh,
				out int _, out List<string> _, out bool _, out string freshDetail);
			devices = fresh;
			detail = freshDetail;
			if (ok)
			{
				_cache = fresh;
				_cacheAtMs = now;
				_cacheDetail = freshDetail;
			}
			else
			{
				// ⛔ 枚举失败不沿用旧快照（宁可让调用方退回兜底，也不拿过期状态冒充真值）
				_cache = null;
				_cacheDetail = freshDetail;
			}
			return ok;
		}
	}

	/// <summary>
	/// ⭐ 枚举核心：按类 GUID 逐个打开 device information set，识别摄像头设备。
	/// <para><paramref name="applyChange" /> = <c>true</c> 时顺便对"状态与目标不一致"的设备下发
	/// <c>DICS_DISABLE</c>/<c>DICS_ENABLE</c>（幂等：已到位的设备跳过）。</para>
	/// </summary>
	private static bool EnumerateRaw(bool applyChange, bool enableTarget, out List<CameraDeviceInfo> devices,
		out int changed, out List<string> applyErrors, out bool accessDenied, out string detail)
	{
		devices = new List<CameraDeviceInfo>();
		changed = 0;
		applyErrors = new List<string>();
		accessDenied = false;
		detail = string.Empty;
		List<string> skipped = new List<string>();
		List<string> setErrors = new List<string>();
		int setsOpened = 0;
		for (int classIndex = 0; classIndex < DeviceClassGuids.Length; classIndex++)
		{
			Guid classGuid = DeviceClassGuids[classIndex];
			string enumClassName = DeviceClassNames[classIndex];
			IntPtr deviceInfoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent);
			if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == InvalidHandleValue)
			{
				setErrors.Add(enumClassName + " 类枚举失败（err=" + Marshal.GetLastWin32Error() + "）");
				continue;
			}
			setsOpened++;
			try
			{
				for (uint index = 0; ; index++)
				{
					SpDevinfoData deviceInfo = default(SpDevinfoData);
					deviceInfo.cbSize = (uint)Marshal.SizeOf<SpDevinfoData>();
					// ⭐ 枚举到 ERROR_NO_MORE_ITEMS 就是正常结束（不当作错误）
					if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
					{
						break;
					}
					string friendlyName = ReadStringProperty(deviceInfoSet, ref deviceInfo, SpdrpFriendlyName);
					string deviceDesc = ReadStringProperty(deviceInfoSet, ref deviceInfo, SpdrpDeviceDesc);
					string deviceClass = ReadStringProperty(deviceInfoSet, ref deviceInfo, SpdrpClass);
					string name = (friendlyName.Length > 0) ? friendlyName : deviceDesc;
					if (!IsCameraDevice(enumClassName, deviceClass, name, deviceDesc, skipped))
					{
						continue;
					}
					bool disabled = IsDeviceDisabled(deviceInfoSet, ref deviceInfo, out uint problemCode);
					devices.Add(new CameraDeviceInfo(name, enumClassName, disabled, problemCode));
					if (!applyChange)
					{
						continue;
					}
					// ⭐ 幂等：已经是目标状态 ⇒ 一个字节都不写 ✅（目标"开"=设备未禁用；目标"关"=设备已禁用）
					bool needChange = disabled != !enableTarget;
					if (!needChange)
					{
						continue;
					}
					if (SetOneDeviceState(deviceInfoSet, ref deviceInfo, enableTarget, out string setError, out bool denied))
					{
						changed++;
					}
					else
					{
						applyErrors.Add(name + "：" + setError);
						accessDenied |= denied;
					}
				}
			}
			catch (Exception ex)
			{
				setErrors.Add(enumClassName + " 类枚举异常（" + ex.GetType().Name + ":" + ex.Message + "）");
			}
			finally
			{
				if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != InvalidHandleValue)
				{
					SetupDiDestroyDeviceInfoList(deviceInfoSet);
				}
			}
		}
		if (setsOpened == 0)
		{
			detail = string.Join("；", setErrors);
			return false;
		}
		string stateText = (devices.Count > 0) ? Describe(devices) : "未找到摄像头设备";
		string skipText = (skipped.Count > 0) ? " · 已跳过：" + string.Join("；", skipped) : string.Empty;
		string errorText = (setErrors.Count > 0) ? " · " + string.Join("；", setErrors) : string.Empty;
		detail = stateText + skipText + errorText;
		return true;
	}

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ 设备识别 / 状态判定 / 写入
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// ⭐ 识别一台设备是不是"这一轮要管的摄像头"。
	/// <para>⭐ 规则：Camera 类默认认（很多 UVC 相机名字是 "USB Video Device"，不含 camera 关键字）；
	/// Image 类**必须**名字含 camera/webcam/摄像头（⛔ 不能误伤扫描仪）；桌面/虚拟相机、
	/// Windows Hello 红外相机**不碰** ✅。</para>
	/// </summary>
	private static bool IsCameraDevice(string enumClassName, string deviceClass, string name, string desc, List<string> skipped)
	{
		string effectiveClass = (deviceClass.Length > 0) ? deviceClass : enumClassName;
		string text = ((name.Length > 0) ? name : desc).Trim();
		if (text.Length == 0)
		{
			return false;
		}
		string lower = text.ToLowerInvariant();
		bool nameHasCameraKeyword = lower.Contains("camera") || lower.Contains("webcam")
			|| text.Contains("摄像头") || text.Contains("摄像");
		bool isCameraClass = string.Equals(effectiveClass, "Camera", StringComparison.OrdinalIgnoreCase);
		if (!isCameraClass && !nameHasCameraKeyword)
		{
			return false; // ⭐ Image 类里的扫描仪/打印机等：名字没摄像头关键字 ⇒ 不是我们要管的
		}
		string exclusion = ExclusionReason(lower, text);
		if (exclusion != null)
		{
			skipped.Add(text + "（" + exclusion + "）");
			return false;
		}
		return true;
	}

	/// <summary>⭐ 返回"为什么跳过"（<c>null</c> = 不跳过）。</summary>
	private static string ExclusionReason(string lower, string original)
	{
		for (int i = 0; i < ScannerPrinterKeywords.Length; i++)
		{
			string keyword = ScannerPrinterKeywords[i];
			if (lower.Contains(keyword) || original.Contains(keyword))
			{
				return "扫描/打印设备";
			}
		}
		for (int i = 0; i < VirtualCameraKeywords.Length; i++)
		{
			if (lower.Contains(VirtualCameraKeywords[i]))
			{
				return "虚拟摄像头";
			}
		}
		for (int i = 0; i < IrHelloKeywords.Length; i++)
		{
			string keyword = IrHelloKeywords[i];
			if (lower.Contains(keyword) || original.Contains(keyword))
			{
				return "Windows Hello/红外相机（不碰，免得影响人脸登录）";
			}
		}
		return null;
	}

	/// <summary>
	/// ⭐ 读一台设备是否"已禁用"：<c>CM_Get_DevNode_Status</c>（problem=22）
	/// ＋ <c>SPDRP_CONFIGFLAGS</c>（CONFIGFLAG_DISABLED）双判据。
	/// </summary>
	private static bool IsDeviceDisabled(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, out uint problemCode)
	{
		problemCode = 0;
		bool disabled = false;
		try
		{
			if (CM_Get_DevNode_Status(out uint _, out uint problem, deviceInfo.DevInst, 0u) == CrSuccess)
			{
				problemCode = problem;
				disabled = problem == CmProbDisabled;
			}
		}
		catch
		{
			// ⛔ CM 读不到不当作"未禁用"；下面 ConfigFlags 还能再兜一层
		}
		if (TryReadUIntProperty(deviceInfoSet, ref deviceInfo, SpdrpConfigFlags, out uint configFlags)
			&& (configFlags & ConfigFlagDisabled) != 0)
		{
			disabled = true;
		}
		return disabled;
	}

	/// <summary>
	/// ⭐ 对一台设备下发 <c>DICS_DISABLE</c> / <c>DICS_ENABLE</c>（<c>DICS_FLAG_GLOBAL</c>），并复核一次。
	/// <para>⚠️ 已知坑：个别设备类"启用"需要先 <c>DICS_FLAG_CONFIGSPECIFIC</c> 再 <c>DICS_FLAG_GLOBAL</c>
	/// 两段式调用才生效 ⇒ 这里只补一次（⛔ 不无限重试，失败就如实报告）✅。</para>
	/// </summary>
	private static bool SetOneDeviceState(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, bool enable,
		out string error, out bool accessDenied)
	{
		error = string.Empty;
		accessDenied = false;
		uint primaryChange = enable ? DicsEnable : DicsDisable;
		if (!ApplyPropertyChange(deviceInfoSet, ref deviceInfo, primaryChange, DicsFlagGlobal, out int firstError))
		{
			accessDenied = firstError == ErrorAccessDenied;
			error = "SetupAPI 调用失败 err=" + firstError + (accessDenied ? "（需要管理员/提权）" : string.Empty);
			return false;
		}
		if (enable == !IsDeviceDisabled(deviceInfoSet, ref deviceInfo, out uint _))
		{
			return true; // ⭐ 已经到目标状态 ✅
		}
		// ⭐ 补一次（启用：CONFIGSPECIFIC → 再 GLOBAL；禁用：再 GLOBAL 一次）
		uint retryScope = enable ? DicsFlagConfigSpecific : DicsFlagGlobal;
		if (!ApplyPropertyChange(deviceInfoSet, ref deviceInfo, primaryChange, retryScope, out int retryError))
		{
			accessDenied = retryError == ErrorAccessDenied;
			error = "SetupAPI 补调失败 err=" + retryError + (accessDenied ? "（需要管理员/提权）" : string.Empty);
			return false;
		}
		if (enable
			&& !ApplyPropertyChange(deviceInfoSet, ref deviceInfo, DicsEnable, DicsFlagGlobal, out int globalError))
		{
			accessDenied = globalError == ErrorAccessDenied;
			error = "SetupAPI 两段式启用失败 err=" + globalError + (accessDenied ? "（需要管理员/提权）" : string.Empty);
			return false;
		}
		return true;
	}

	/// <summary>⭐ <c>SetupDiSetClassInstallParams</c> + <c>SetupDiChangeState</c>（<c>DIF_PROPERTYCHANGE</c>）。</summary>
	private static bool ApplyPropertyChange(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo,
		uint stateChange, uint scope, out int win32Error)
	{
		win32Error = 0;
		SpPropChangeParams parameters = default(SpPropChangeParams);
		parameters.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf<SpClassInstallHeader>();
		parameters.ClassInstallHeader.InstallFunction = DifPropertyChange;
		parameters.StateChange = stateChange;
		parameters.Scope = scope;
		parameters.HwProfile = 0u;
		if (!SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfo, ref parameters,
			(uint)Marshal.SizeOf<SpPropChangeParams>()))
		{
			win32Error = Marshal.GetLastWin32Error();
			return false;
		}
		if (!SetupDiChangeState(deviceInfoSet, ref deviceInfo))
		{
			win32Error = Marshal.GetLastWin32Error();
			return false;
		}
		return true;
	}

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ 设备属性读取
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>⭐ 读字符串型注册表属性（FriendlyName / DeviceDesc / Class）；读不到返回空串。</summary>
	private static string ReadStringProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property)
	{
		try
		{
			SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfo, property, out uint _,
				null, 0u, out uint requiredSize);
			if (requiredSize == 0u || requiredSize > 65536u)
			{
				return string.Empty;
			}
			byte[] buffer = new byte[requiredSize];
			if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfo, property, out uint _,
				buffer, (uint)buffer.Length, out uint size))
			{
				return string.Empty;
			}
			if (size < 2u)
			{
				return string.Empty;
			}
			string value = Encoding.Unicode.GetString(buffer, 0, (int)(size - (size % 2u)));
			int nullIndex = value.IndexOf('\0');
			return (nullIndex >= 0) ? value.Substring(0, nullIndex) : value;
		}
		catch
		{
			return string.Empty;
		}
	}

	/// <summary>⭐ 读 DWORD 型注册表属性（这里只用 ConfigFlags）；读不到返回 <c>false</c>。</summary>
	private static bool TryReadUIntProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property, out uint value)
	{
		value = 0u;
		try
		{
			byte[] buffer = new byte[8];
			if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfo, property, out uint _,
				buffer, (uint)buffer.Length, out uint size)
				|| size < 4u)
			{
				return false;
			}
			value = BitConverter.ToUInt32(buffer, 0);
			return true;
		}
		catch
		{
			return false;
		}
	}

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ 状态摘要
	// ──────────────────────────────────────────────────────────────────────

	private static bool AnyEnabled(IReadOnlyList<CameraDeviceInfo> devices)
	{
		for (int i = 0; i < devices.Count; i++)
		{
			if (!devices[i].Disabled)
			{
				return true;
			}
		}
		return false;
	}

	private static bool MatchesTarget(bool enable, IReadOnlyList<CameraDeviceInfo> devices)
	{
		if (devices.Count == 0)
		{
			return false;
		}
		return enable ? AnyEnabled(devices) : !AnyEnabled(devices);
	}

	private static string Describe(IReadOnlyList<CameraDeviceInfo> devices)
	{
		if (devices.Count == 0)
		{
			return "（无摄像头设备）";
		}
		string[] parts = new string[devices.Count];
		for (int i = 0; i < devices.Count; i++)
		{
			CameraDeviceInfo device = devices[i];
			string problem = (device.ProblemCode != 0u) ? (" · problem=" + device.ProblemCode) : string.Empty;
			parts[i] = device.Name + "（status=" + (device.Disabled ? "Disabled" : "Enabled") + problem + "）";
		}
		return string.Join(" / ", parts);
	}

	// ──────────────────────────────────────────────────────────────────────
	// ⭐ Win32 结构 / P/Invoke
	// ──────────────────────────────────────────────────────────────────────

	[StructLayout(LayoutKind.Sequential)]
	private struct SpDevinfoData
	{
		public uint cbSize;
		public Guid ClassGuid;
		public uint DevInst;
		public IntPtr Reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SpClassInstallHeader
	{
		public uint cbSize;
		public uint InstallFunction;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SpPropChangeParams
	{
		public SpClassInstallHeader ClassInstallHeader;
		public uint StateChange;
		public uint Scope;
		public uint HwProfile;
	}

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
	private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

	[DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiEnumDeviceInfo")]
	private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

	[DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
	private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
		uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

	[DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiSetClassInstallParamsW")]
	private static extern bool SetupDiSetClassInstallParams(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
		ref SpPropChangeParams classInstallParams, uint classInstallParamsSize);

	[DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiChangeState")]
	private static extern bool SetupDiChangeState(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData);

	[DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiDestroyDeviceInfoList")]
	private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

	[DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
	private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);
}
