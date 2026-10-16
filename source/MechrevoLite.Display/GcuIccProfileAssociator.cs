using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MechrevoLite.Display;

/// <summary>
/// 40 系 GCU「生成 ICC 但不关联」的绕行：在「屏幕校色 = 开」成功下发**之后**，
/// 把 GCU 刚生成的那个 ICC 关联到当前内屏，并读回验证（失败自动回退）。
///
/// <para>紧急关闭键：config 键 <c>calib_assoc_gcu_icc</c>，<b>默认启用</b>（缺失即启用）。
/// 置 0 即可在不重编译的情况下整段停用 ⇒ 那时不拍快照、不读 ICC、不写注册表、无日志。</para>
///
/// <para><b>⭐ 2026-10-10【用户范围约束】「这个只对 40 系的 GCU 生效 因为 50 系的有回读」：</b>
/// 本功能是"GCU 生成 ICC 但不关联"的<b>绕行</b>，只对<b>没有回读能力</b>的机器（今天的 40 / 40B / 30 系）有意义；
/// 有回读的机器（Gen50 + GCU 已上报档位）GCU 自己就会把 ICC 关联好 ⇒ 本功能<b>整体跳过</b>：
/// ⛔ 不拍快照 · ⛔ 不枚举 spool · ⛔ 不轮询 · ⛔ 不写注册表，只落一行 <c>[icc-assoc]</c> 日志（见 <see cref="CaptureAsync"/>）。
/// 判据与校色判定链<b>同一套输入</b>（<c>Program.hw.DgpuGeneration</c> + <c>Program.hw.ColorCalibrationModeSeen</c>
/// ⇒ <c>ColorCalibrationSwitchPolicy.SupportsReadback</c>），⛔ 不新加代际探测。</para>
///
/// <para><b>识别判据（2026-10-10 按机主口径简化：只盯 spool，⛔ 不再有 desc / 面板前缀 / 暂存目录 / 多级兜底）</b></para>
/// <list type="number">
/// <item>「开」<b>之前</b>只给 <c>C:\Windows\System32\spool\drivers\color</c> 拍快照（文件名 → 大小 + mtime UTC）。</item>
/// <item>命令成功返回后<b>轮询最多 <see cref="PollTimeoutMs"/> ms</b>（6000ms，250ms 一次；⭐ 满足判据立刻退出，
/// 实测命中在 <c>polls=4</c>≈0.75s；⭐ 整段走 <see cref="ApplyAfterCalibrationOnDetached"/> 后台即发即忘，⛔ 不阻塞界面）。</item>
/// <item><b>主判据</b> = 快照里没有的<b>新文件名</b>，且 <c>体积 &gt; 0</c>（GCU 会同时产出 0 字节废档）
///   且连续两次轮询体积一致（确认已写完）。命中即取它。</item>
/// <item><b>兜底判据</b>（仅在轮询窗口内**始终没有新文件名**时才用）= 快照里<b>已有文件</b>的 <c>mtime</c> 变了
///   且 <c>体积 &gt; 0</c>（防 GCU"改写同名档"）。走这条会**明确记日志**说明用的是兜底分支。</item>
/// <item>多个候选 ⇒ 取 <c>mtime</c> 最新者，并把**全部**候选写进日志。</item>
/// <item>两者都没有 ⇒ 记日志放弃（⛔ 不拿旧档硬关联）。</item>
/// </list>
///
/// <para><b>写入方式</b>（机主实测"自定义校色"这条能把 ICC 关联上）：直接调 <see cref="IccHelper.ApplyIcc"/> ——
/// HKCU <c>…\ICM\RegisteredProfiles</c> 注册项（值名=文件名 → spool 全路径）+ 标记子键 +
/// <c>%LOCALAPPDATA%\Microsoft\Windows\Color\</c> 副本 + <c>…\ProfileAssociations\Display\DISPLAY</c> 的
/// <c>ICMProfile</c>(REG_SZ) + <c>…\ProfileAssociations\Display\{4d36e96e-…}\0000..0008</c> 的
/// <c>ICMProfile</c>(<b>REG_MULTI_SZ</b>，值=文件名)。本机实测：该槽一写上，<b>新进程</b>的
/// <c>gdi32!GetICMProfile(\\.\DISPLAY1)</c> 立刻改指新档。</para>
///
/// <para><b>⚠️ 上一版为什么"没有用"（2026-10-10 受控实测，见下），本版据此修正：</b>
/// <c>gdi32!GetICMProfile</c> 的结果被 <b>GDI/mscms 按进程缓存</b>：进程内**第一次**调用之后，
/// 无论注册表怎么改，本进程再读都返回同一个旧档（实测连续两次、广播 <c>WM_SETTINGCHANGE</c> 后依旧）。
/// 上一版在<b>拍快照</b>时就读了一次 <c>GetICMProfile</c>（<see cref="Snapshot"/> 里记录"写前生效档"），
/// 于是写后的读回必然是旧档 ⇒ 判"写入成功但读回不是挑中的档（未生效）" ⇒ 触发回退，
/// 把刚写好的 HKCU <c>ICMProfile</c> / 注册项 / 用户目录副本又删掉（现场即"槽里 ICMProfile 为空"）。
/// 本版因此：<b>① 前置快照阶段绝不调用 <c>GetICMProfile</c></b>（只读注册表 + 只拍 spool 快照）；
/// <b>② 生效判定以注册表槽读回为准</b>，<c>GetICMProfile</c> 照旧读并写进日志（进程内首次读就是真值；
/// 若它没变指而注册表槽已写对，日志里明确写"进程级缓存"并保留关联、不回退）。</para>
///
/// <para>⚠️ 不删除、不改写 GCU 生成的 ICC；不碰 GCU 侧行为；不碰校色判定链；不改 <see cref="IccHelper"/> 自身。</para>
/// </summary>
internal static class GcuIccProfileAssociator
{
	/// <summary>
	/// 紧急关闭键：默认启用，置 0 关闭。缺失 / 非 0 ⇒ 启用（<see cref="AppConfig.Get"/> 缺省值给 1）。
	/// </summary>
	internal const string ConfigKey = "calib_assoc_gcu_icc";

	/// <summary>
	/// 轮询上限（毫秒）。⭐ 2026-10-10 由 25000 收紧到 6000（机主口径「太久了 GCU 基本上能立刻响应的」）。
	/// 依据：本机 log.txt 实测主判据命中在 <c>轮询结束 polls=4</c>（≈0.75s，见 14:25:53 那条），
	/// 6s 是该实测的约 6~8 倍；且轮询**满足判据即立刻退出**、整段已改为后台即发即忘 ⇒
	/// 这个上限只在"GCU 本次根本没生成新档"时才会走满，且⛔不再影响界面响应，
	/// 故不再需要为"落盘→元数据曾观察到约 11s"（那可能是元数据/槽写入而非 spool 文件本身）留 25s 余量。
	/// </summary>
	private const int PollTimeoutMs = 6000;

	private const int PollIntervalMs = 250;

	/// <summary>⭐ 2026-10-10「关联不阻塞调用方」的防重入闸门（见 <see cref="ApplyAfterCalibrationOnDetached"/>）。</summary>
	private static readonly object AssocGate = new object();

	/// <summary>是否已有一次后台关联在执行（同一时刻只允许一次）。</summary>
	private static bool _assocRunning;

	/// <summary>执行中被新请求覆盖的排队项（只保留最新一次快照）。</summary>
	private static PendingAssociation _assocPending;

	/// <summary>排队中的一次关联请求：前置快照 + 来源标签（仅日志用）。</summary>
	private sealed class PendingAssociation
	{
		internal Snapshot Before;

		internal string Tag = "";
	}

	/// <summary>唯一被监视的目录（机主口径：GCU 生成的 ICC 会落这里）。</summary>
	private const string SpoolColorDir = @"C:\Windows\System32\spool\drivers\color";

	private const string IcmRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
	private const string DisplayClassGuid = "{4d36e96e-e325-11ce-bfc1-08002be10318}";
	private const string AssociationsDisplay = IcmRoot + @"\ProfileAssociations\Display";
	/// <summary>显示器 Class 关联键（<c>0000..0008</c> 九槽挂在它下面，与 <see cref="IccHelper"/> 完全一致）。</summary>
	private const string AssociationsClassKey = AssociationsDisplay + @"\" + DisplayClassGuid;
	/// <summary>传统 DISPLAY 关联槽（页面/注册项自洽用，与 <see cref="IccHelper"/> 一致）。</summary>
	private const string AssociationsDisplayKey = AssociationsDisplay + @"\DISPLAY";
	private const string RegisteredProfilesKey = IcmRoot + @"\RegisteredProfiles";
	/// <summary>关联槽个数：0000..0008（与 <see cref="IccHelper.ApplyIcc"/> 一致，⛔ 不缩减）。</summary>
	private const int AssociationSlots = 9;

	/// <summary>用户颜色目录（<see cref="IccHelper.ApplyIcc"/> 会往这里放副本，回退时要能撤掉）。</summary>
	private static readonly string UserColorDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		@"Microsoft\Windows\Color");

	[DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateDC(string driver, string device, string port, IntPtr devMode);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern bool DeleteDC(IntPtr hdc);

	[DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetICMProfile(IntPtr hdc, ref uint size, StringBuilder name);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE info, uint flags);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAY_DEVICE
	{
		public int cb;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceString;

		public uint StateFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceID;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceKey;
	}

	/// <summary>单个文件在快照里的样子（只用于"变没变"的比对）。</summary>
	internal sealed class FileStamp
	{
		internal long Length;

		internal long Ticks;
	}

	/// <summary>一次"前后比对"的前置状态（⛔ 不含 <c>GetICMProfile</c>，见类注释）。</summary>
	internal sealed class Snapshot
	{
		/// <summary>spool 目录的文件名 → (大小, mtime)。</summary>
		internal Dictionary<string, FileStamp> Spool = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);

		/// <summary>目标显示器关联槽注册表路径（回退用）。</summary>
		internal string RegistryPath = "";

		/// <summary>写前该槽的 <c>ICMProfile</c> 原值（<b>只读注册表</b>，仅日志/取证）。</summary>
		internal string SlotProfileBefore = null;

		internal bool Ready;

		internal string Reason = "";

		internal string Describe()
		{
			return "ICMProfile=[" + (SlotProfileBefore ?? "(无)") + "] 槽=" + RegistryPath + " 快照 spool="
				+ Spool.Count.ToString(CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// 写关联<b>之前</b>的现场备份：覆盖 <see cref="IccHelper.ApplyIcc"/> 会碰的全部位置
	/// （九槽 <c>ICMProfile</c> + <c>DISPLAY</c> 槽 <c>ICMProfile</c> + <c>RegisteredProfiles</c> 注册项/标记子键
	/// + 用户颜色目录副本），失败时逐个还原。
	/// </summary>
	internal sealed class AssociationBackup
	{
		internal Dictionary<string, string[]> Slots = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		internal string DisplayProfile;

		internal bool DisplayProfileExisted;

		internal string RegisteredValue;

		internal bool RegisteredValueExisted;

		internal bool RegisteredSubKeyExisted;

		internal bool UserColorFileExisted;
	}

	/// <summary>本次动作的结果（只用于日志与调用方判断）。</summary>
	internal sealed class Outcome
	{
		internal bool Applied;

		internal bool Skipped;

		internal string Chosen = "(无)";

		internal string Candidates = "(无)";

		internal string Before = "(无)";

		internal string After = "(无)";

		internal string Reason = "";

		internal bool RolledBack;
	}

	/// <summary>功能是否启用（默认启用）。</summary>
	private static bool Enabled()
	{
		return AppConfig.Get(ConfigKey, 1) != 0;
	}

	/// <summary>spool 目录快照：文件名 → (大小, mtime UTC)。⛔ 只认 .icm/.icc。</summary>
	private static Dictionary<string, FileStamp> SnapSpool()
	{
		Dictionary<string, FileStamp> result = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!Directory.Exists(SpoolColorDir))
			{
				return result;
			}
			foreach (string file in Directory.EnumerateFiles(SpoolColorDir))
			{
				string ext = Path.GetExtension(file);
				if (!ext.Equals(".icm", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".icc", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				try
				{
					FileInfo info = new FileInfo(file);
					FileStamp stamp = new FileStamp();
					stamp.Length = info.Length;
					stamp.Ticks = info.LastWriteTimeUtc.Ticks;
					result[Path.GetFileName(file)] = stamp;
				}
				catch
				{
					// 单个文件读不到就跳过，不影响整体比对
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[icc-assoc] spool 快照未完成 dir=" + SpoolColorDir + " : " + ex.Message);
		}
		return result;
	}

	/// <summary>
	/// 关联目标设备名（GDI）+ 它的 <c>ICMProfile</c> 关联槽注册表路径。
	/// 依据（本机实测）：<c>EnumDisplayDevices</c> 给出 <c>MONITOR\BOE0CBE\{4d36e96e-…}\0001</c>，
	/// 该末段 <c>0001</c> 就是 Class 下的实例序号，也正是
	/// <c>HKCU\…\ICM\ProfileAssociations\Display\{4d36e96e-…}\0001</c> 这个槽 —— 与
	/// <c>GetICMProfile(\\.\DISPLAY1)</c> 读回的档完全对应。**只写这一个槽**，不碰外屏/虚拟屏。
	/// </summary>
	private static bool TryResolveTarget(out string gdiDevice, out string slotRegistryPath, out string monitorDeviceId, out string reason)
	{
		gdiDevice = "";
		slotRegistryPath = "";
		monitorDeviceId = "";
		reason = "";
		string gdi = ScreenNative.FindLaptopScreen(out ScreenNative.InternalScreenConfidence confidence, out string technology);
		if (string.IsNullOrEmpty(gdi))
		{
			reason = "内屏未识别（FindLaptopScreen 返回空）";
			return false;
		}
		DISPLAY_DEVICE monitor = default(DISPLAY_DEVICE);
		monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
		if (!EnumDisplayDevices(gdi, 0u, ref monitor, 0u))
		{
			reason = "EnumDisplayDevices(" + gdi + ",0) 失败 err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
			return false;
		}
		monitorDeviceId = monitor.DeviceID ?? "";
		string key = monitor.DeviceKey ?? "";
		// DeviceKey 形如 \Registry\Machine\System\CurrentControlSet\Control\Class\{…}\0001
		int slash = key.LastIndexOf('\\');
		if (slash < 0 || slash + 1 >= key.Length)
		{
			reason = "监视器 DeviceKey 末段取不到（槽号未知）: " + key;
			return false;
		}
		string slot = key.Substring(slash + 1);
		if (slot.Length == 0)
		{
			reason = "监视器 DeviceKey 末段为空: " + key;
			return false;
		}
		gdiDevice = gdi;
		slotRegistryPath = AssociationsDisplay + "\\" + DisplayClassGuid + "\\" + slot;
		reason = "校色=" + technology + " 可信度=" + confidence + " 监视器=" + monitorDeviceId + " 槽=" + slot;
		return true;
	}

	/// <summary>
	/// 读 <c>GetICMProfile</c>：当前该设备真正在用的档（文件名）。
	/// ⚠️ <b>GDI/mscms 按进程缓存</b>：本进程第一次调用之后结果就固定了（实测）。因此调用点必须保证
	/// 这是"关联写入之后"的第一次读（见类注释），否则读回的是被缓存的旧档。
	/// </summary>
	private static string ReadBackIcmProfile(string gdiDevice)
	{
		IntPtr hdc = CreateDC(null, gdiDevice, null, IntPtr.Zero);
		if (hdc == IntPtr.Zero)
		{
			return "(CreateDC 未成功 err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ")";
		}
		try
		{
			uint needed = 0u;
			// 先问所需长度（文档：pszFilename 为 NULL 且成功时把所需大小写回）
			GetICMProfile(hdc, ref needed, null);
			if (needed == 0u)
			{
				return "(读回无返回 err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ")";
			}
			StringBuilder buffer = new StringBuilder((int)needed);
			uint size = needed;
			if (!GetICMProfile(hdc, ref size, buffer))
			{
				return "(读回未被接受 err=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ")";
			}
			return Path.GetFileName(buffer.ToString());
		}
		catch (Exception ex)
		{
			return "(读回异常: " + ex.Message + ")";
		}
		finally
		{
			DeleteDC(hdc);
		}
	}

	private static string FirstOf(object value)
	{
		string[] array = value as string[];
		if (array != null)
		{
			return (array.Length == 0) ? null : array[0];
		}
		return value as string;
	}

	/// <summary>把注册表值（REG_MULTI_SZ / REG_SZ）统一成 <c>string[]</c>；不存在返回 null。</summary>
	private static string[] AsArray(object value)
	{
		string[] array = value as string[];
		if (array != null)
		{
			return array;
		}
		string single = value as string;
		return (single == null) ? null : new string[1] { single };
	}

	/// <summary>ICC 结构自检（offset 36 处是 <c>acsp</c>）。<b>只写日志，⛔ 不参与候选判定</b>。</summary>
	private static bool IsValidIccHeader(string path)
	{
		try
		{
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if (stream.Length < 128L)
				{
					return false;
				}
				byte[] head = new byte[40];
				if (stream.Read(head, 0, 40) < 40)
				{
					return false;
				}
				return head[36] == 97 && head[37] == 99 && head[38] == 115 && head[39] == 112;
			}
		}
		catch
		{
			return false;
		}
	}

	/// <summary>写关联<b>之前</b>的现场备份（只读注册表 + 只探文件是否存在，⛔ 不调 <c>GetICMProfile</c>）。</summary>
	private static AssociationBackup CaptureAssociationBackup(string fileName)
	{
		AssociationBackup backup = new AssociationBackup();
		try
		{
			using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AssociationsClassKey))
			{
				for (int i = 0; i < AssociationSlots; i++)
				{
					string slotName = "000" + i.ToString(CultureInfo.InvariantCulture);
					string[] values = null;
					if (key != null)
					{
						using (RegistryKey slot = key.OpenSubKey(slotName))
						{
							if (slot != null)
							{
								values = AsArray(slot.GetValue("ICMProfile"));
							}
						}
					}
					backup.Slots[slotName] = values;
				}
			}
			using (RegistryKey display = Registry.CurrentUser.OpenSubKey(AssociationsDisplayKey))
			{
				if (display != null)
				{
					object value = display.GetValue("ICMProfile");
					backup.DisplayProfileExisted = value != null;
					backup.DisplayProfile = FirstOf(value);
				}
			}
			using (RegistryKey registered = Registry.CurrentUser.OpenSubKey(RegisteredProfilesKey))
			{
				if (registered != null)
				{
					object value = registered.GetValue(fileName);
					backup.RegisteredValueExisted = value != null;
					backup.RegisteredValue = value as string;
					backup.RegisteredSubKeyExisted = registered.OpenSubKey(fileName) != null;
				}
			}
			backup.UserColorFileExisted = File.Exists(Path.Combine(UserColorDir, fileName));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[icc-assoc] 写前备份不完整（继续，回退可能不全）: " + ex.Message);
		}
		return backup;
	}

	/// <summary>
	/// 只读注册表地取关联槽当前值（⛔ <b>绝不</b>在这里调 <c>GetICMProfile</c>：那会把旧档灌进本进程的
	/// GDI/mscms 缓存，写后就再也读不到新档 —— 正是上一版"没有用"的原因）。
	/// </summary>
	private static string ReadSlotRegistry(string slotPath)
	{
		try
		{
			using (RegistryKey key = Registry.CurrentUser.OpenSubKey(slotPath))
			{
				if (key != null)
				{
					// 槽里写的是 REG_MULTI_SZ（IccHelper 的写法），旧版也见过 REG_SZ ⇒ 两种都认
					return FirstOf(key.GetValue("ICMProfile"));
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[icc-assoc] 读关联槽未成功: " + ex.Message);
		}
		return null;
	}

	/// <summary>
	/// ⭐ 2026-10-10【用户范围约束】该机是否<b>有回读能力</b> ⇒ 本功能<b>整体跳过</b>。
	/// 判据（与校色判定链<b>同一套输入</b>，⛔ 不新加代际探测）：<c>Program.hw.DgpuGeneration</c>
	/// + <c>Program.hw.ColorCalibrationModeSeen</c> ⇒ <c>ColorCalibrationSwitchPolicy.SupportsReadback(...)</c>
	/// （现语义 <c>ModeSeen &amp;&amp; 代际==Gen50</c>，与 <c>SettingsForm.CalibrationReadbackSupported()</c> 逐字同源）。
	///
	/// <para>⭐ 另加一条兜底：代际本身就是 <c>Gen50</c> 时<b>无条件</b>跳过 —— 覆盖"50 系机器上 GCU 还没上报档位字段
	/// （<c>ColorCalibrationModeSeen</c> 仍为 false ⇒ <c>SupportsReadback</c> 会返回 false）"这个时间窗；
	/// 否则 50 系会被误判成"无回读"而跑绕行。机主口径是"50 系有回读 ⇒ 必须整体跳过"，故按代际兜底。</para>
	///
	/// <para>⚠️ 代际未知（<c>Unknown</c> / <c>NoDgpu</c>，含指纹变化后探测不到独显的场合）时<b>不</b>跳过：
	/// 那时判据说"无回读"，与改动前行为一致（⛔ 不因为"读不到"把 40/30 系的绕行一起停掉）。</para>
	/// </summary>
	/// <param name="reason">判定依据（只用于日志）。</param>
	private static bool ReadbackBlocksFeature(out string reason)
	{
		var hw = Program.hw;
		var generation = MechrevoLite.Gpu.DgpuGenerationKind.Unknown;
		bool modeSeen = false;
		if (hw != null)
		{
			generation = hw.DgpuGeneration;
			modeSeen = hw.ColorCalibrationModeSeen;
		}
		if (MechrevoLite.Hardware.ColorCalibrationSwitchPolicy.SupportsReadback(generation, modeSeen))
		{
			reason = "有回读（代际=" + generation.ToString() + " 档位已上报=" + modeSeen + " SupportsReadback=true）";
			return true;
		}
		if (generation == MechrevoLite.Gpu.DgpuGenerationKind.Gen50)
		{
			reason = "50 系（有回读族；本次 GCU 尚未上报档位字段 代际=Gen50 档位已上报=False SupportsReadback=false，按机主口径仍整体跳过）";
			return true;
		}
		reason = "无回读（代际=" + generation.ToString() + " 档位已上报=" + modeSeen + " SupportsReadback=false）";
		return false;
	}

	/// <summary>
	/// 「开」命令下发**之前**在后台线程拍快照：<b>只</b>拍 spool 目录 + 目标槽注册表原值。
	/// 关闭键为 0 时立即返回 null ⇒ 不拍快照、不写注册表，行为与改动前一致。
	/// <para>⭐ 2026-10-10 起再加一道<b>范围门控</b>：有回读能力的机器（或就是 50 系）直接返回 null —— 这一步在
	/// <c>Task.Run</c> / <c>SnapSpool</c> / 读注册表<b>之前</b>，故跳过时⛔ 不拍快照、⛔ 不枚举 spool 目录、⛔ 不写注册表；
	/// 调用方拿到 null ⇒ 连 <see cref="ApplyAfterCalibrationOnDetached"/> 都不会被调（⛔ 不轮询、⛔ 不写关联）。</para>
	/// </summary>
	/// <param name="tag">来源标签（"开关"/"切档"/"开机"），只用于日志：跳过与执行两条日志带同一个 tag，便于核对。</param>
	internal static Task<Snapshot> CaptureAsync(string tag = "入口")
	{
		if (!Enabled())
		{
			return Task.FromResult<Snapshot>(null);
		}
		string readbackReason;
		if (ReadbackBlocksFeature(out readbackReason))
		{
			Logger.WriteLine("[icc-assoc] tag=" + tag + " skip=有回读 —— 因该机有回读能力（" + readbackReason
				+ "），本次跳过 ICC 自动关联（本次不拍快照/不枚举 spool/不轮询/不写注册表）");
			return Task.FromResult<Snapshot>(null);
		}
		return Task.Run(delegate
		{
			Snapshot snapshot = new Snapshot();
			string gdi;
			string slotPath;
			string monitorId;
			string targetReason;
			if (!TryResolveTarget(out gdi, out slotPath, out monitorId, out targetReason))
			{
				snapshot.Ready = false;
				snapshot.Reason = targetReason;
				Logger.WriteLine("[icc-assoc] 前置快照未建立 —— " + targetReason);
				return snapshot;
			}
			snapshot.RegistryPath = slotPath;
			snapshot.SlotProfileBefore = ReadSlotRegistry(slotPath);
			snapshot.Spool = SnapSpool();
			snapshot.Ready = true;
			snapshot.Reason = targetReason;
			Logger.WriteLine("[icc-assoc] 前置快照 taken=" + snapshot.Spool.Count.ToString(CultureInfo.InvariantCulture)
				+ " dir=" + SpoolColorDir + " 槽=" + slotPath + " 槽内ICMProfile=" + (snapshot.SlotProfileBefore ?? "(无)"));
			return snapshot;
		});
	}

	/// <summary>
	/// ⭐ 2026-10-10「关联过程不阻塞调用方」：<see cref="ApplyAfterCalibrationOnAsync"/> 的<b>即发即忘</b>包装。
	/// 调用方（UI 事件处理）调用后<b>立刻返回</b>；轮询、写注册表、读回验证全在后台线程完成。
	/// 内部自行 <c>try/catch</c> 并落 <c>[icc-assoc]</c> 日志（⛔ 失败绝不被吞掉、也绝不抛回 UI）。
	/// <para><b>防重入</b>：同一时刻只允许一次关联在跑（<see cref="_assocRunning"/>）；此期间再来的请求
	/// <b>既不并发、也不丢弃</b> —— 只把<b>最新一次</b>快照放进 <see cref="_assocPending"/>（新的覆盖旧的），
	/// 当前这次跑完立刻接着跑 ⇒ 不会"同一动作被重复触发两遍关联"，也不会丢掉后一次动作该做的关联。</para>
	/// </summary>
	internal static void ApplyAfterCalibrationOnDetached(Snapshot before, string tag)
	{
		try
		{
			if (before == null || !Enabled())
			{
				return;
			}
			// ⭐ 2026-10-10【范围门控】兜底：正常路径下有回读的机器在 CaptureAsync 就已经拿到 null（连这里都进不来），
			// 这里再判一次是防"将来有调用方拿旧快照直接进来" ⇒ 有回读时照样只落一行日志、⛔ 不轮询、⛔ 不写注册表。
			string readbackReasonSkip;
			if (ReadbackBlocksFeature(out readbackReasonSkip))
			{
				Logger.WriteLine("[icc-assoc] tag=" + tag + " skip=有回读（调度前兜底） —— 因该机有回读能力（"
					+ readbackReasonSkip + "），本次跳过 ICC 自动关联");
				return;
			}
			lock (AssocGate)
			{
				PendingAssociation request = new PendingAssociation();
				request.Before = before;
				request.Tag = tag;
				_assocPending = request;
				if (_assocRunning)
				{
					Logger.WriteLine("[icc-assoc] 已有一次关联在执行 ⇒ 本次(" + tag + ")排队（只留最新一次：不并发、不重复触发）");
					return;
				}
				_assocRunning = true;
			}
			Logger.WriteLine("[icc-assoc] 关联转入后台（即发即忘，调用方不等待）tag=" + tag);
			Task.Run((Func<Task>)DrainAssociations);
		}
		catch (Exception ex)
		{
			// ⛔ 调度失败也不能抛回 UI（UI 事件处理是 async void）
			lock (AssocGate)
			{
				_assocRunning = false;
				_assocPending = null;
			}
			Logger.WriteLine("[icc-assoc] 后台关联未能启动(" + tag + "): " + ex.Message);
		}
	}

	/// <summary>后台串行消费排队中的关联请求，直到队列空。整段 ⛔ 不向外抛异常。</summary>
	private static async Task DrainAssociations()
	{
		while (true)
		{
			PendingAssociation current;
			lock (AssocGate)
			{
				current = _assocPending;
				_assocPending = null;
				if (current == null)
				{
					_assocRunning = false;
					return;
				}
			}
			try
			{
				Outcome outcome = await ApplyAfterCalibrationOnAsync(current.Before).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteLine("[icc-assoc] 关联结果(" + current.Tag + ") applied=" + outcome.Applied + " 跳过=" + outcome.Skipped
					+ " 挑中=" + outcome.Chosen + " 回退=" + outcome.RolledBack + " 原因=" + outcome.Reason);
			}
			catch (Exception ex)
			{
				// ⛔ 失败绝不能被吞掉：连异常类型与堆栈一起落日志（后台线程里没人接得住）
				Logger.WriteLine("[icc-assoc] 关联异常(" + current.Tag + ") " + ex.GetType().Name + ": " + ex.Message + " | " + ex.StackTrace);
			}
		}
	}

	/// <summary>
	/// 校色「开」成功之后调用：轮询比对 spool 前后快照，认出 GCU 刚生成的 ICC，关联到内屏并读回验证。
	/// </summary>
	internal static async Task<Outcome> ApplyAfterCalibrationOnAsync(Snapshot before)
	{
		Outcome outcome = new Outcome();
		if (!Enabled())
		{
			return outcome;
		}
		if (before == null || !before.Ready || string.IsNullOrEmpty(before.RegistryPath))
		{
			outcome.Reason = "前置快照不可用（无法回退，放弃）";
			Logger.WriteLine("[icc-assoc] 放弃 —— " + outcome.Reason);
			return outcome;
		}
		outcome.Before = before.Describe();
		List<string> newFiles = new List<string>();
		Dictionary<string, FileStamp> changed = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
		// 新档要"连续两次轮询体积一致"才算写完，故记上一轮看到的体积
		Dictionary<string, long> lastSize = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		int polls = 0;
		long deadline = Environment.TickCount64 + PollTimeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			polls++;
			newFiles.Clear();
			changed.Clear();
			Dictionary<string, FileStamp> now = SnapSpool();
			foreach (KeyValuePair<string, FileStamp> pair in now)
			{
				if (pair.Value.Length <= 0L)
				{
					continue;
				}
				FileStamp old;
				if (!before.Spool.TryGetValue(pair.Key, out old))
				{
					long seen;
					if (lastSize.TryGetValue(pair.Key, out seen) && seen == pair.Value.Length)
					{
						newFiles.Add(pair.Key);
					}
					else
					{
						lastSize[pair.Key] = pair.Value.Length;
					}
					continue;
				}
				if (old.Ticks != pair.Value.Ticks)
				{
					// 兜底分支的素材：同名档被改写（mtime 变了）
					changed[pair.Key] = pair.Value;
				}
			}
			if (newFiles.Count > 0)
			{
				break;
			}
			await Task.Delay(PollIntervalMs).ConfigureAwait(continueOnCapturedContext: false);
		}
		// ⭐ 判定：新文件名优先；窗口内一个都没有 ⇒ 才退到"已有档 mtime 变了"
		bool usedFallback = false;
		List<string> candidates;
		if (newFiles.Count > 0)
		{
			candidates = new List<string>(newFiles);
		}
		else if (changed.Count > 0)
		{
			candidates = new List<string>(changed.Keys);
			usedFallback = true;
		}
		else
		{
			candidates = new List<string>();
		}
		StringBuilder audit = new StringBuilder();
		foreach (string name in candidates)
		{
			audit.Append("[" + name + " " + DescribeFile(name) + "] ");
		}
		outcome.Candidates = (audit.Length == 0) ? "(无)" : audit.ToString().Trim();
		if (candidates.Count == 0)
		{
			outcome.Reason = "轮询 " + polls.ToString(CultureInfo.InvariantCulture) + " 次（上限 "
				+ PollTimeoutMs.ToString(CultureInfo.InvariantCulture) + "ms）内 spool 既无新增文件名、也无已有档 mtime 变化 ⇒ 本次不关联";
			Logger.WriteLine("[icc-assoc] 轮询结束 polls=" + polls.ToString(CultureInfo.InvariantCulture)
				+ " new=[] changed=[] picked=(无) reason=" + outcome.Reason);
			Logger.WriteLine("[icc-assoc] 放弃 —— " + outcome.Reason);
			return outcome;
		}
		Logger.WriteLine("[icc-assoc] 轮询结束 polls=" + polls.ToString(CultureInfo.InvariantCulture)
			+ " new=[" + string.Join(",", newFiles.ToArray()) + "] changed=[" + string.Join(",", new List<string>(changed.Keys).ToArray())
			+ "] picked=" + candidates.Count.ToString(CultureInfo.InvariantCulture) + " 个候选 branch="
			+ (usedFallback ? "兜底:已有档 mtime 变化" : "主:新文件名") + " 候选=" + outcome.Candidates);
		if (usedFallback)
		{
			Logger.WriteLine("[icc-assoc] 走兜底分支：本次没有新增文件名，改用已有档 mtime 变化者（防 GCU 改写同名档）");
		}
		// 多个候选 ⇒ 取 mtime 最新者
		string chosen = null;
		DateTime chosenTime = DateTime.MinValue;
		foreach (string name in candidates)
		{
			try
			{
				DateTime mtime = new FileInfo(Path.Combine(SpoolColorDir, name)).LastWriteTime;
				if (chosen == null || mtime > chosenTime)
				{
					chosen = name;
					chosenTime = mtime;
				}
			}
			catch
			{
			}
		}
		if (chosen == null)
		{
			outcome.Reason = "候选取最新者未成立";
			Logger.WriteLine("[icc-assoc] 放弃 —— " + outcome.Reason);
			return outcome;
		}
		outcome.Chosen = chosen;
		string spoolPath = Path.Combine(SpoolColorDir, chosen);
		// 关联前解析目标设备（只走 EnumDisplayDevices，⛔ 不碰 GetICMProfile）
		string gdiNow;
		string slotNow;
		string monNow;
		string reasonNow;
		if (!TryResolveTarget(out gdiNow, out slotNow, out monNow, out reasonNow))
		{
			outcome.Reason = "后置解析目标未成立: " + reasonNow;
			Logger.WriteLine("[icc-assoc] 放弃 —— " + outcome.Reason);
			return outcome;
		}
		AssociationBackup backup = CaptureAssociationBackup(chosen);
		string writeError;
		bool rc = WriteAssociation(chosen, out writeError);
		if (!rc)
		{
			Logger.WriteLine("[icc-assoc] ApplyIcc 未成功: " + writeError);
		}
		string slotAfter = rc ? ReadSlotRegistry(slotNow) : null;
		// ⚠️ 本进程第一次（且应该只有这一次）读 GetICMProfile：写入之前绝不读，否则读回是缓存旧档
		string readback = rc ? ReadBackIcmProfile(gdiNow) : "(未写入，未读)";
		bool slotOk = string.Equals(slotAfter, chosen, StringComparison.OrdinalIgnoreCase);
		bool readbackOk = string.Equals(readback, chosen, StringComparison.OrdinalIgnoreCase);
		bool ok = rc && slotOk;
		outcome.After = "槽ICMProfile=[" + (slotAfter ?? "(无)") + "] GetICMProfile=" + readback;
		Logger.WriteLine("[icc-assoc] ApplyIcc path=" + spoolPath + " rc=" + rc + " 槽读回=" + (slotAfter ?? "(无)")
			+ " GetICMProfile=" + readback + " slotOk=" + slotOk + " readbackOk=" + readbackOk + " ok=" + ok);
		if (!ok)
		{
			outcome.Reason = "写入未生效（rc=" + rc + " 槽读回=" + (slotAfter ?? "(无)") + "）";
			string rollbackNote;
			RestoreAssociation(backup, chosen, out rollbackNote);
			outcome.RolledBack = true;
			outcome.After = "槽ICMProfile=[" + (ReadSlotRegistry(slotNow) ?? "(无)") + "]";
			Logger.WriteLine("[icc-assoc] 已回退（" + rollbackNote + "）reason=" + outcome.Reason);
			return outcome;
		}
		if (!readbackOk)
		{
			// 不判失败、不回退：注册表槽已确认写对，GetICMProfile 只是本进程的缓存值（实测结论）
			Logger.WriteLine("[icc-assoc] GetICMProfile 未指向新档（本进程 GDI/mscms 进程级缓存：进程内首次读之后固定；"
				+ "注册表槽已确认写入 ⇒ 保留关联，新开进程/颜色管理页会显示新档）");
		}
		outcome.Applied = true;
		outcome.Reason = readbackOk ? "已关联，槽读回与 GetICMProfile 都指向新档" : "已关联，槽读回指向新档（GetICMProfile 为本进程缓存值）";
		Logger.WriteLine("[icc-assoc] 成功 file=" + chosen + " " + outcome.After + " 全部候选=" + outcome.Candidates);
		return outcome;
	}

	/// <summary>候选文件的一行日志（大小 + mtime + ICC 结构，⚠️ 结构只用于记录）。</summary>
	private static string DescribeFile(string name)
	{
		try
		{
			FileInfo info = new FileInfo(Path.Combine(SpoolColorDir, name));
			return info.Length.ToString(CultureInfo.InvariantCulture) + "B "
				+ info.LastWriteTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
				+ " ICC结构=" + (IsValidIccHeader(info.FullName) ? "yes" : "no");
		}
		catch (Exception ex)
		{
			return "读属性未成功: " + ex.Message;
		}
	}

	/// <summary>
	/// 采用<b>机主实测能把 ICC 关联上</b>的那条路：直接调 <see cref="IccHelper.ApplyIcc"/>（自定义校色同款），
	/// 即九槽 <c>0000..0008</c> 的 <c>ICMProfile</c>(REG_MULTI_SZ) + <c>DISPLAY</c> 槽 + <c>RegisteredProfiles</c>
	/// 注册项/标记子键 + 用户颜色目录副本。⛔ 不缩减成"只写一个槽"。
	/// </summary>
	private static bool WriteAssociation(string fileName, out string error)
	{
		error = "";
		string path = Path.Combine(SpoolColorDir, fileName);
		if (!File.Exists(path))
		{
			error = "spool 里没有该档: " + path;
			return false;
		}
		if (!IccHelper.ApplyIcc(path))
		{
			error = "IccHelper.ApplyIcc 返回 false（详见其自身日志）";
			return false;
		}
		return true;
	}

	/// <summary>按备份还原 <see cref="IccHelper.ApplyIcc"/> 碰过的全部位置（只碰这些）。</summary>
	private static bool RestoreAssociation(AssociationBackup backup, string fileName, out string note)
	{
		note = "";
		if (backup == null)
		{
			note = "无备份，未回退";
			return false;
		}
		try
		{
			using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AssociationsClassKey))
			{
				for (int i = 0; i < AssociationSlots; i++)
				{
					string slotName = "000" + i.ToString(CultureInfo.InvariantCulture);
					string[] values = backup.Slots.ContainsKey(slotName) ? backup.Slots[slotName] : null;
					using (RegistryKey slot = key?.CreateSubKey(slotName))
					{
						if (slot == null)
						{
							continue;
						}
						if (values == null || values.Length == 0)
						{
							slot.DeleteValue("ICMProfile", throwOnMissingValue: false);
						}
						else
						{
							slot.SetValue("ICMProfile", values, RegistryValueKind.MultiString);
						}
					}
				}
			}
			using (RegistryKey display = Registry.CurrentUser.CreateSubKey(AssociationsDisplayKey))
			{
				if (display != null)
				{
					if (!backup.DisplayProfileExisted || string.IsNullOrEmpty(backup.DisplayProfile))
					{
						display.DeleteValue("ICMProfile", throwOnMissingValue: false);
					}
					else
					{
						display.SetValue("ICMProfile", backup.DisplayProfile, RegistryValueKind.String);
					}
				}
			}
			using (RegistryKey registered = Registry.CurrentUser.OpenSubKey(RegisteredProfilesKey, writable: true))
			{
				if (registered != null)
				{
					if (!backup.RegisteredValueExisted)
					{
						registered.DeleteValue(fileName, throwOnMissingValue: false);
					}
					else
					{
						registered.SetValue(fileName, backup.RegisteredValue ?? "", RegistryValueKind.String);
					}
				}
			}
			if (!backup.RegisteredSubKeyExisted)
			{
				Registry.CurrentUser.DeleteSubKey(RegisteredProfilesKey + "\\" + fileName, throwOnMissingSubKey: false);
			}
			if (!backup.UserColorFileExisted)
			{
				try
				{
					string userPath = Path.Combine(UserColorDir, fileName);
					if (File.Exists(userPath))
					{
						File.Delete(userPath);
					}
				}
				catch
				{
					// 用户颜色目录副本删不掉不阻断回退
				}
			}
			note = "九槽 + DISPLAY + RegisteredProfiles + 用户颜色目录副本";
			return true;
		}
		catch (Exception ex)
		{
			note = "回退异常: " + ex.Message;
			return false;
		}
	}

	/// <summary>诊断用：把"识别依据"整体捞一遍（只读，⛔ 不调 <c>GetICMProfile</c>）。</summary>
	internal static string DescribeIdentification()
	{
		List<string> lines = new List<string>();
		string gdi;
		string slotPath;
		string monitorId;
		string targetReason;
		if (!TryResolveTarget(out gdi, out slotPath, out monitorId, out targetReason))
		{
			lines.Add("目标解析未成立: " + targetReason);
		}
		else
		{
			lines.Add("设备=" + gdi + " 监视器=" + monitorId + " 槽=" + slotPath);
			lines.Add("槽内ICMProfile=" + (ReadSlotRegistry(slotPath) ?? "(无)"));
		}
		lines.Add("启用=" + (Enabled() ? "是" : "否(" + ConfigKey + "=0)"));
		lines.Add("监视目录=" + SpoolColorDir + " 存在=" + Directory.Exists(SpoolColorDir) + " 文件数="
			+ SnapSpool().Count.ToString(CultureInfo.InvariantCulture));
		StringBuilder sb = new StringBuilder();
		foreach (string line in lines)
		{
			sb.AppendLine(line);
		}
		return sb.ToString();
	}
}
