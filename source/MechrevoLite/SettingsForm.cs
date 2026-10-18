using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using MechrevoLite.Battery;
using MechrevoLite.Diagnostics;
using MechrevoLite.Display;
using MechrevoLite.Gpu;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using MechrevoLite.Update;

namespace MechrevoLite;

public class SettingsForm : RForm
{
	private sealed class DashboardWheelFilter : IMessageFilter
	{
		/// <summary>2026-10-09：取鼠标落点**真实的最深窗口**（见 <see cref="PreFilterMessage" /> 里的说明）。</summary>
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern nint WindowFromPoint(Point point);

		private readonly SettingsForm _form;

		public DashboardWheelFilter(SettingsForm form)
		{
			_form = form;
		}

		public bool PreFilterMessage(ref Message m)
		{
			if (m.Msg != 522)
			{
				return false;
			}
			if (_form.IsDisposed || !_form.IsHandleCreated)
			{
				return false;
			}
			BufferedPanel dashboardScroll = _form._dashboardScroll;
			RScrollBar dashboardScrollBar = _form._dashboardScrollBar;
			if (dashboardScroll == null || dashboardScrollBar == null || !dashboardScrollBar.Visible || !dashboardScroll.IsHandleCreated)
			{
				return false;
			}
			int x = (short)((long)m.LParam & 0xFFFFL);
			int y = (short)(((long)m.LParam >> 16) & 0xFFFF);
			Point point = new Point(x, y);
			if (!dashboardScroll.RectangleToScreen(dashboardScroll.ClientRectangle).Contains(point))
			{
				return false;
			}
			// 2026-10-09（用户原话「如果打开下拉列表 鼠标滚轮上下滑动 列表会错位」）：⭐
			// **展开中的下拉列表**是系统原生的**桌面级弹窗**（窗口类 ComboLBox，父窗口是桌面）——它既不在
			// 本面板的子控件树里、下面那个 `GetChildAtPoint` 也**命中不到它**，于是鼠标一移到刚弹出的列表上，
			// 这里的判定就落空 ⇒ 滚轮被吞掉去滚页面：页面带着下拉框本体从弹出列表底下滑走、列表钉在原地不动
			// ⇒ 实测（175%）滚一格列表就相对下拉框错位一个 Step（70 设备像素），且列表自身一格都没滚。
			// 修法：落在展开列表上的滚轮**一律放过**，由系统按原生语义交给那个列表（列表项没超出可见行时
			// 系统本来也不滚，符合直觉）。⚠️ 只影响"展开列表上"的滚轮；页面背景上的滚轮照旧。
			if (RComboBox.AnyDropDownListCovers(dashboardScroll, point))
			{
				return false;
			}
			// 2026-10-12（用户原话「我发现自定义窗口在主窗口上面时候 滑动滚轮，动的是主窗口上下划」）：⭐
			// **本过滤器是 `Application.AddMessageFilter` 的进程级过滤器**（挂载点 SettingsForm.cs:4957），
			// 它只看"鼠标落点是否在主窗滚动区矩形内"——可**另一个顶层窗口**（自定义磁贴窗 QuickTilePickerForm、
			// 或任何压在设置窗上的窗口）停在那块矩形上时，那个矩形照样命中 ⇒ 滚轮被这里吞掉去滚主窗口，
			// 而真正被鼠标指着的窗口一格都没滚。⭐ 这正是"自定义窗口在主窗口上面，滚轮滚的是主窗口"。
			// 修法：先认**落点真实的顶层窗口** —— 不是本窗（也不是本窗子孙，`FindForm()` 沿父链取根）
			// 就 ⛔ 绝不认领，交回给系统按原生语义递给那个窗口（展开的下拉列表走上面那条，语义不变）。
			// ⚠️ 落点取 `WindowFromPoint`（下面那段判下拉框用的同一个 API，此处只是提前取用）。
			Control hitUnder = Control.FromHandle(WindowFromPoint(point));
			Control hitForm = ((hitUnder == null) ? null : hitUnder.FindForm());
			if (hitForm != null && hitForm != dashboardScroll.FindForm())
			{
				return false;
			}
			// 2026-10-09（用户原话「依旧没跟着走」）：⭐ **下面那个"落在下拉框/滑杆上就放过"的判定原来是死代码**。
			// 依据（探针实测）：`Control.GetChildAtPoint` 只回**直接子控件** —— 鼠标明明压在深层嵌套的
			// 下拉框上，`_dashboardScroll.GetChildAtPoint(...)` 回的是 `_dashboardStack` 那个 **Panel**
			//（同一落点 `WindowFromPoint` 回的就是那颗 `RComboBox`）⇒ 沿父链走到 `_dashboardScroll` 都
			// 命中不到 `ComboBox` ⇒ **滚轮在下拉框本体上也被吞掉去滚页面**（用户截图那一幕：
			// 页面滚走、列表留在原地）。这里改用**真实的窗口落点**（`WindowFromPoint` → `Control.FromHandle`）
			// 作起点，父链走法不变 ⇒ 深层嵌套的下拉框/滑杆终于能被认出来。
			// ⚠️ 只改"起点怎么取"：放过条件、滚动量、返回 true 的位置一律未动。
			nint hwndUnder = WindowFromPoint(point);
			Control control = (hwndUnder == IntPtr.Zero) ? null : Control.FromHandle(hwndUnder);
			while (control != null && control != dashboardScroll)
			{
				if (control is ComboBox || control is RSlider)
				{
					return false;
				}
				control = control.Parent;
			}
			int num = (short)(((long)m.WParam >> 16) & 0xFFFF);
			if (num == 0)
			{
				return false;
			}
			dashboardScrollBar.Value -= Math.Sign(num) * dashboardScrollBar.Step;
			return true;
		}
	}

	internal static readonly Size CompactDashboardLogicalClientSize = new Size(420, 529);

	/// <summary>
	/// 2026-10-12（用户答复③「不要变宽：缩小磁贴让它一行放下」⇒ **撤销抬高窗口最小宽度**）：
	/// 窗口最小宽回到**原来的水平** = 420 逻辑像素 —— 与 2026-10-09 归档里的
	/// `MinimumSize = new Size(Scale(420), Scale(300))` 同值、逐像素相同。
	/// ⚠️ **保留**上一轮修好的**单位口径**：`Form.MinimumSize` 在**运行期赋值**时按**设备像素**生效
	/// ⇒ 下发时乘当前 DPI 缩放（见 `RequiredDashboardWindowWidth`），⛔ 不再把它当逻辑像素用。
	/// ⚠️ 代价（已向用户报告）：窗口缩到 420 逻辑宽时，标题行右端读数与磁贴都会比自然宽窄；
	/// 磁贴那一侧靠**缩小磁贴**（行高/图标/字号/间距，见 `QuickTileLogicalHeight` 与列宽自适配）
	/// 保证「5 个动作 + ＋」仍然一行放得下、且**任何一格文字都不出现省略号**。
	/// </summary>
	internal static readonly Size MinimumDashboardLogicalClientSize = new Size(420, 529);

	/// <summary>
	/// 2026-10-11（用户拍板「抬高窗口最小宽度」，2026-10-12 已按用户答复③**撤销**）：
	/// **最宽的可能读数**样本（逻辑像素测宽用）。
	/// ⚠️ 现在只喂给 <see cref="ComputeRequiredDashboardLogicalWidth" />（**取证日志用**），
	/// ⛔ 不再决定窗口最小宽（那个回到固定 420 逻辑像素了）。
	/// 3 位功耗 + 4 位转速 + 3 位占空比 = "123W 4567rpm 100%"，是本页读数字段的自然上限
	/// （功耗整数 W、转速整数 rpm、占空比整数 %）。窗口最小宽度按它预留 ⇒ 真机怎么跳都不裁字。
	/// </summary>
	internal const string TelemetryWidestSample = "123W 4567rpm 100%";

	/// <summary>
	/// 2026-10-16（遥测排版方案 A）：**拆列之后每列各自的预留样本**。
	/// ⚠️ 拆列前只有一片（功耗 空格 转速 空格 占空比 拼在一起）、共用一份样本
	///（<see cref="TelemetryReserveSampleFor" /> 当时的 "1234rpm 100%"）⇒ 片宽只跟这一份样本走，
	/// 于是两行的字段个数不同时（宝龙达 CPU 行没有功耗、GPU 行有 "14W"），同一片里的文本
	/// 宽窄不一、而片内文本是居中的 ⇒ **两行的转速数字左右错开**（机主原话「感觉不太统一」）。
	/// 拆成三列后每列各有样本 ⇒ 同名列在两行里宽度一致 ⇒ 数字列逐列对齐。
	/// </summary>
	internal const string TelemetryPowerSample = "123W";

	internal const string TelemetryRpmSample = "1234rpm";

	internal const string TelemetryDutySample = "100%";

	internal const int CompactOfficialConsoleLogicalHeight = 56;

	internal const int LiquidCoolingLogicalHeight = 112;

	private static readonly (string Title, string[] Keys)[] QuickSwitchGroups = new (string, string[])[4]
	{
		(Strings.QuickGroupInput, new string[7] { "touchpad", "touchpadtoggle", "wifi", "bt", "webcam", "micmute", "numpad" }),
		(Strings.QuickGroupKeyboard, new string[5] { "winkey", "fnkey", "copilot", "osd", "capslock" }),
		// ⭐ 2026-10-19（机主要求）：`apuboost`（APU 防缩缸）归到「电源与系统」组，紧挨 `highperf` ✅
		(Strings.QuickGroupPower, new string[13]
		{
			"usb", "highperf", "apuboost", "fanboost", "acrecovery", "cpuadvperf", "gamewhitelist", "taskbarautohide", "transparency", "darktheme", "deepsleep",
			"monitoroff",
			"winupdate"
		}),
		(Strings.QuickGroupSleep, new string[6] { "sleepnetoff", "sleepaway", "sleepsysreq", "sleeprtcwake", "sleepdeep", "sleephibernate" })
	};

	private readonly Dictionary<string, (Control Header, Control Grid)> _quickGroupContainers = new Dictionary<string, (Control, Control)>();

	/// <summary>
	/// 2026-10-11（快捷磁贴行）：**「更多开关」的 (键, 文案) 表**从 <c>BuildQuickSwitchRows</c> 的局部数组
	/// 提到类级（那种写法是本文件反编译后的既有形状，本轮只做"提级"、条目与顺序一字未改）——
	/// 理由：磁贴行必须与「更多开关」**用同一份文案**（否则同一个开关在两处叫不同名字，
	/// 用户答复②要求的"同源"就不成立了）；两处例外（磁贴短名）见 <see cref="QuickTileText" />。
	/// </summary>
	private static readonly (string, string)[] QuickSwitchLabels = new (string, string)[31]
	{
		("touchpad", Strings.QuickTouchpad),
		("wifi", "WiFi"),
		("bt", Strings.QuickBluetooth),
		("webcam", Strings.QuickCamera),
		// 2026-10-11（麦克风静音开关 · 用户答复①「要它」）：**新建的开关**，分组 = 「输入设备」
		//（与「摄像头」同一组、紧挨着它）；键名 micmute，勾选 = **已静音**（SetMute(true)）。
		// 无录音设备时 hw.SupportsQuickSwitch("micmute") 判假 ⇒ 本项连同它的磁贴一起按能力位隐藏。
		("micmute", Strings.QuickMicMute),
		("winkey", Strings.QuickWinkey),
		("fnkey", Strings.QuickFnkey),
		("numpad", Strings.QuickNumpad),
		// ⭐ 2026-10-17（新增开关）：大小写锁定。它与 `numpad` 一样是**键盘自身的锁定键**，
		// 不在厂商 WMI/EC 通道里 ⇒ 走 Windows 模拟按键（`MechrevoLite.Helpers.NativeLockKey`）。
		// ⚠️ 文案用**字面量**而不是 `Strings.*`：`Strings*.resx` 是**混合行尾**
		//    （CRLF=14 / 裸 LF=5343），用编辑器改它会把整篇行尾改掉 ⇒ 本表既有的
		//    `("wifi", "WiFi")` 就是字面量先例 ✅
		("capslock", "大小写锁定"),
		("osd", Strings.QuickOsd),
		("usb", Strings.QuickUsb),
		("copilot", Strings.QuickCopilot),
		("acrecovery", Strings.QuickAcRecovery),
		("highperf", Strings.QuickHighPerf),
		// ⭐ 2026-10-19（机主要求）：APU 防缩缸（处理器性能提升模式 → 高效、积极且有保障）
		("apuboost", Strings.ApuAntiDegrade),
		("fanboost", Strings.FanBoostStrong),
		("deepsleep", Strings.QuickDeepSleep),
		("touchpadtoggle", Strings.QuickTouchpadToggle),
		("gamewhitelist", Strings.QuickGameWhitelist),
		("cpuadvperf", Strings.QuickCpuAdvPerf),
		("taskbarautohide", Strings.QuickTaskbarAutohide),
		("transparency", Strings.QuickTransparency),
		("darktheme", Strings.QuickDarkTheme),
		("monitoroff", Strings.QuickMonitorOff),
		("sleepnetoff", Strings.QuickSleepNetOff),
		("sleepaway", Strings.QuickSleepAway),
		("sleepsysreq", Strings.QuickSleepSysReq),
		("sleeprtcwake", Strings.QuickSleepRtcWake),
		("sleepdeep", Strings.QuickSleepDeep),
		("sleephibernate", Strings.QuickSleepHibernate),
		("winupdate", Strings.QuickWindowsUpdate)
	};

	private static bool _quickSleepAc = true;

	private RButton? _quickSleepAcBtn;

	private RButton? _quickSleepDcBtn;

	private readonly ToolTip _quickSleepToolTip = new ToolTip();

	internal static readonly (float Label, float Slider, float Value) BrightnessColumnPercentages = (Label: 26f, Slider: 60f, Value: 14f);

	internal static readonly (float Title, float Day, float Night) ThemeColumnPercentages = (Title: 60f, Day: 20f, Night: 20f);

	private ContextMenuStrip contextMenuStrip = new CustomContextMenu();

	private ToolStripMenuItem menuEco;

	private ToolStripMenuItem menuStandard;

	private ToolStripMenuItem menuUltimate;

	private DonateControl donateControl;

	public GPUModeControl gpuControl;

	private AutoUpdateControl updateControl;

	private Label? _batteryLimitValue;
	private RButton[]? _batteryModeButtons;
	// 2026-10-08 17:00：本窗是否走「50 系滑块行」（真＝滑块上屏、无开关无三档；假＝新布局）。
	private bool _gen50SliderRow;
	// 2026-10-08（充电上限开关）：开关控件 + 抑制重入标志（程序化设置 Checked 时不触发写 EC）。
	// 2026-10-10（用户要求 · ⭐ 原话「无极限制充电开的时候上面三档位应该置灰(包括EC现充开关)」）：
	// checkChargeLimit（EC 限充开关）与 _batteryModeButtons（三档）的**启用态** =
	//   本字段（EC 通道可用性基准，由 ApplyChargeLimitSliderGating 记录）
	//   **且** 无极限充开关（checkChargeSlider / _chargeSliderEnabled）为**关**。
	// 两者分开存，是为了让「开关打开 ⇒ 上面置灰」与「EC 不可用 ⇒ 整块置灰」互不覆盖
	// （后者在构造之后还会由 RefreshDeviceCapabilities 再跑一次）。
	private bool _chargeLimitStageBaseEnabled = true;
	// 2026-10-09（无极限充滑块 · 实验性功能）：用户原话「滑块限制充电改成默认隐藏 在设置里面增加一个开关
	// 『启用无极限充功能(实验性)』打开才显示 注意不要和其他一样是三个选项 开关两个按钮就行」。
	// 滑块（无级上限 40–100%）默认不上屏，只由电池卡片里那颗**两态开关** checkChargeSlider 控制
	// （⛔ 不是三选一/分段控件）；关的时候该行 RowStyle 高度收到 0 ⇒ 不留占位空行。
	// 显隐状态存 charge_limit_slider（缺省 0 = 关 ⇒ 默认隐藏），滑块自己的取值仍存在 charge_limit。
	private const string ChargeSliderConfigKey = "charge_limit_slider";
	private bool _chargeSliderEnabled;
	// 2026-10-10（限充代系门控 · 用户要求「为 40 系及以后的机型开放，30 系不显示这个开关」；
	// 18:40 修正：NoDgpu 不排除 —— 无界 15X 等无独显办公本也可走 EC 限充门控）：
	// 无极限充（滑块＋实验开关）只排除 Gen30 —— 30 系不建开关行、不建滑块行（RowCount=3），
	// 且 _chargeSliderEnabled 强制为 false。判定见 ChargeSliderSupported()。
	private bool _chargeSliderSupported;
	// 2026-10-10（限充安全警告 · 用户要求「警告加在开关下面，打开后显示，用红色字」）：
	// 无极限充开关正下方的红色警告行（UiVisualStyle.Danger，仓库既有语义色，不自造颜色），
	// 随开关一起显隐（ApplyChargeSliderVisibility 统一收放，关时行高 0 不留占位）。
	private Label _chargeSliderWarning;
	private int _chargeSliderWarningRowIndex = -1;
	private int _chargeSliderWarningRowHeight = 0;
	// 下面三个引用字段**故意不写可空注解**（`?`）：本文件没有 #nullable 上下文，写注解会各多一条
	// CS8632，而本仓库的构建基线（0 错误 / 1435 警告）是硬上限 —— 语义与加注解等价（都在用之前判 != null）。
	private RCheckBox checkChargeSlider;
	private TableLayoutPanel _chargeSliderTable;
	private Label _chargeSliderLabel;
	private int _chargeSliderRowIndex = -1;
	private int _chargeSliderRowHeight;
	// 2026-10-16（EC 直写逃生口 · 补回被删掉的 UI 出口）：用户要求「保留『关闭 EC 直写』的出口」——
	// 原来唯一写 `ec_charge_limit` 的「EC 三档限充(实验性)」开关已随 b93baef 删除 ⇒ 该键只剩代码在读、
	// 界面上**没有任何入口**，用户失去了「EC 直写行为异常时把它关掉」的手段。
	// ⚠️ 语义（与本键既有读法逐项对齐，见 EcChargeLimit.IsAvailableOnThisMachine / IsFirmwareLimitSupported）：
	//   勾选 ⇒ 写 "0" ＝ **显式关闭 EC 直写**（TrySet 直接拒绝、通道层面判不可用）；
	//   取消 ⇒ **删键** ＝ 回到自动判定。⛔ **刻意不写 "1"** —— "1" 是「强制放行」覆盖，会绕过通道探测，
	//   用户只是想恢复默认时写 "1" 等于偷偷开后门。
	// ⚠️ 它只影响「直写 EC」这一条路：GCU 三档（BatteryProtection/Control）不受影响。
	// ⚠️ 这一行**永不参与** ApplyChargeLimitRowVisibility 的收放，否则用户勾上就等于把自己的撤销入口藏掉。
	// ⚠️ 与上面几个字段同样**故意不写可空注解**（本文件无 #nullable，写了会各多一条 CS8632）。
	private RCheckBox _checkEcDirectWriteOff;
	private int _ecDirectWriteOffRowIndex = -1;
	private int _ecDirectWriteOffRowHeight;
	private const string EcDirectWriteOffConfigKey = "ec_charge_limit";
	// ⚠️ 面向界面的两条新文案**放在代码里**（与 PerfModeRunner.WriteRejectedDetail / StepErrorDetail 同一先例）：
	//   `Strings*.resx` 是**混合行尾**（CRLF=14 ＋ 裸 LF=5343），`edit` 会把整个文件改写成 LF ⇒ diff 爆成全文；
	//   且新增一条要同改 Strings.resx ＋ Strings.zh-CN.resx ＋ Strings.cs 三处。故沿用既有先例。
	private const string EcDirectWriteOffSwitchText = "关闭 EC 直写充电上限（写入异常时用）";
	private const string EcDirectWriteOffSwitchTip = "只关闭「直写 EC」这一条限充通路：\n· 勾上后本机不再向 EC 写充电上限（滑块与档位的 EC 直写一律被拒）；\n· GCU 官方三档（BatteryProtection）不受影响，仍可按；\n· 取消勾选即恢复自动判定（本机能不能直写由程序自己探）。\n⚠️ 仅在本机 EC 写入表现异常（写入无效、反复回退）时兜底用。";
	// 2026-10-15（隐藏规则 A/B · 用户原话「对不支持的机型直接隐藏掉按钮就行 滑块和EC三档的开关都隐藏
	// 10系-30系用这个可能损坏硬件必须强制隐藏EC三档和滑块 只保留GCU路(注意不删三个按钮)」）：
	// 下面这几个引用 + 行高记录，供 ApplyChargeLimitRowVisibility 把「EC 直写方式专属」的几行收起来
	// （开关行 / 副标题行 / 无极限充开关行 / 警告行 / 滑块行）——⛔ 三档按钮行**永不**收放（EC/GCU 共用）。
	// ⚠️ 与上面几个字段同样**故意不写可空注解**（本文件无 #nullable，写了会各多一条 CS8632）。
	private TableLayoutPanel _chargeLimitHeadRow;
	private Label _chargeLimitDescLabel;
	// 2026-10-15（限充真效果判据上屏 · 修法④）：0x0742 bit2 判定结果的只读展示标签（原「实验」白胶囊那一格）。
	private Label _chargeLimitEffectLabel;
	private int _chargeLimitHeadRowHeight;
	private int _chargeLimitDescRowHeight;
	private int _chargeSliderToggleRowHeight;
	private bool _chargeLimitRowsHidden;
	// 2026-10-15：老机型（规则 B）**不建**滑块行 ⇒ _chargeSliderTable 恒为 null，但开关行/副标题行仍在宿主表里
	// 需要被收起来 ⇒ 单独记下宿主表（有滑块行时两者指向同一实例）。
	private TableLayoutPanel _chargeLimitHostTable;
	// ⭐⭐ 2026-10-17（宝龙达机型 · 真机反馈「一电池限充还在」）：
	//   宝龙达模具**没有任何限充通路** —— EC 直写物理上做不到（该模具没有 UWACPIDriver，
	//   `\\.\ACPIDriver` 打不开，真机诊断包已证）；GCU 官方三档也走不通（宝龙达机型根本没有 GCU）。
	//   ⇒ 三档按钮（长效/平衡/工作站）与整张电池卡片都不该出现。
	// ⚠️ 为什么不在 `EcChargeLimit.IsAvailableOnThisMachine()` 里补判：那里虽然已有宝龙达分支，
	//   但 `ec_charge_limit=1` 的「强制放行」是**在它之前**早退的 ⇒ 用户若残留该键（本机真机就是
	//   这种情况：面板被点亮、三档可点但点了只弹「GCU 未连接」），UI 照样会被点亮。
	//   而 `EcChargeLimit.cs` 是**零改动红线**（用户明令）⇒ 判据只能放在界面侧的唯一汇聚点。
	// ⚠️ 记下第 2 行（三档按钮行）的原始行高：隐藏时一并收 0，否则卡片顶部会留下一块空白带。
	private int _chargeLimitTierRowHeight;
	private bool _chargeLimitTierHidden;

	// ⭐⭐ 2026-10-18（机主截图）：⭐ 宝龙达下隐藏旧「关闭 EC 直写充电上限（写入异常时用）」行 ✅
	private bool _ecDirectWriteOffHidden;

	private RButton? buttonSilentTurbo;

	private RButton? buttonCustomMode;

	private CustomModeForm? customModeForm;

	// 2026-10-09（回归修复：「我切不到自定义档位了」）：主界面【自定义】按钮点击时是否**显式切档**。
	// 背景：主界面这颗按钮此前是靠"开窗顺带 SelectModeAsync"这条**隐式**路径才真正切到自定义档的；
	// 崩溃针对性改动 (A) 把那条隐式路径去掉后，按钮就只剩"开窗"了 ⇒ 用户点【自定义】时性能模式仍停在
	// 原档（截图里高亮停在【狂暴】）。修法＝把两件事**解耦**：
	//   ① 开窗（含 BindMode 只读绑定）= 由**代码结构**保证只读：buttonCustomMode.Click 的显示/绑定
	//      分支里没有任何切档/下发调用（(A) 原来的 `OpenCustomEditorAppliesMode` 开关已删除 —— 留一个
	//      能一键翻回"开窗即全量下发"的开关本身就是隐患，那正是已确认会杀死耀世进程的路径；要恢复它
	//      必须显式加回调用，而不是改一个 bool）；
	//   ② 按钮点击 = **显式切档**（本开关），走编辑器既有的 SelectModeAsync（＝按该档已保存的值
	//      **强制全量下发一次**，见 CustomModeForm.ActivateModeAsync 的 forceParameters: true）。
	// ⚠️ 一键回退（仅崩溃排查用）：改成 false ⇒ 恢复"点按钮只开窗、不切档"。切档动作与开窗动作
	//     互不依赖：关掉本开关不会让"打开编辑器"重新变成下发路径。
	private const bool CustomModeButtonSwitchesMode = true;

	private TableLayoutPanel? _dashboard;

	private BufferedPanel? _dashboardScroll;

	private BufferedTableLayoutPanel? _dashboardStack;

	private RScrollBar? _dashboardScrollBar;

	private RCollapseGroup? _liquidGroup;

	private RCollapseGroup? _quickGroup;

	private RCollapseGroup? _lightGroup;

	private BufferedPanel? _telemetryPanel;

	private BufferedPanel? _panelGcuStatus;

	private Label? _labelGcuStatus;

	private Label? _telemetryCpuName;

	private Label? _telemetryCpuTemp;

	// ⚠️ 2026-10-16（方案 A）：下面 6 个新字段**刻意不写 `Label?` 注解** —— 本文件不在 `#nullable`
	// 上下文里（见 BuildPerfModeRow 里关于 `object?` 的同一处说明），每写一个引用类型注解就多一条
	// CS8632 警告，而本轮要求「警告基线一个不增」。语义与相邻的 `Label?` 字段完全相同（都允许为 null）。
	private Label _telemetryCpuPower;

	private Label _telemetryCpuRpm;

	private Label _telemetryCpuDuty;

	private Label? _telemetrySeparator;

	private Label? _telemetryGpuName;

	private Label? _telemetryGpuTemp;

	private Label _telemetryGpuPower;

	private Label _telemetryGpuRpm;

	private Label _telemetryGpuDuty;

	private bool _telemetryLayingOut;

	/// <summary>2026-10-11（抬高窗口最小宽度；2026-10-12 撤销该抬高后本字段仍用于下限的幂等兜底）：
	/// 一次布局里有多次可见性下发，延后合并成一次重算。</summary>
	private bool _minimumWidthRefreshPending;

	private TableLayoutPanel? _hzSegTable;

	private TableLayoutPanel? _screenRowsLayout;

	// 2026-10-16：由 3 行扩到 5 行 —— 屏幕区布局（bufferedTableLayoutPanel，见 :2262~2266 的 RowStyles）
	// 实际有 5 行：0=占位 26、1=刷新率 34、2=亮度 28、3=**校色行 calibPanel 34**、4=**色彩增强行 displayEffectRow 34**。
	// 而本数组原先只有 3 个元素 ⇒ SetScreenRowControls 对 row 3/4 因 `row >= Length` 直接 return
	// ⇒ 那两行**从来没有被任何判据管过**：校色行留下"有标题没按钮"的空壳（三个控件跟 flag11 隐藏、
	// 而标题 calibCaption 恒可见），色彩增强行整行恒显示（displayEffectRow.Visible 全仓从未被赋值）。
	// ⚠️ 只**追加**后两项，前三项的索引与数值一字未动 ⇒ row 0/1/2 的既有行为逐位不变。
	private static readonly int[] ScreenRowHeights = new int[5] { 26, 34, 28, 34, 34 };

	private readonly int[] _screenRowVisibleHeights = new int[5];

	private readonly List<RButton> _hzButtons = new List<RButton>();

	private string _lastHzSignature = "";

	// 2026-10-15（hz-win32）：刷新率那一行的 **Win32 档位缓存**。
	// 背景：档位列表原本唯一来源是 Program.hw.HzList <- GCU 上报的 currentHZList，停掉 GCUBridge 后
	// 列表为空 ⇒ 一颗按钮都不建 ⇒ SetScreenRowControls(1,false,_hzSegTable) ⇒ 「刷新率那一行」（180/60）整行消失。
	// ⭐⭐ 2026-10-15 晚（用户拍板「把刷新率这条整个都不走GCU了」）：**Win32 现在是【主】数据源**，
	// 走 ScreenNative.GetAvailableRefreshRates(内屏)（纯 EnumDisplaySettingsEx 枚举，~140 个 mode ⇒ 去重）；
	// GCU 的 HzList 反而退成"只在这条读不到时"的兜底。
	// SyncHzButtons 在启动/每 2s 的显示刷新 tick/DisplaySettingsChanged 等路径上都会被调，故加 60s TTL 进程内缓存
	//（与 ScreenGateProbe.CacheTtlMs 同值同口径），避免每次重画都枚举一遍显示模式。
	private IReadOnlyList<int>? _hzFallbackRates;

	private long _hzFallbackRatesAt;

	// ⭐ 2026-10-16（宝龙达 · 「屏幕刷新率没显示」）：Win32 档位枚举为**空**这件事的**状态翻转哨兵**
	//   （0 = 上一次非空/未记录，1 = 上一次为空且已记过一次）。
	//   ⚠️ 为什么需要：这个枚举在启动 / 每 2s 显示 tick / DisplaySettingsChanged 上都会被调 ⇒
	//   直接 `WriteError` 会**刷屏**；按状态翻转只在「空 ⇒ 非空」或「非空 ⇒ 空」时各记一行。
	//   ⚠️ 为什么用 `WriteError`：`Logger.WriteLine` 的级别按**消息内容**推断 ⇒ 默认「仅错误」
	//   级别下这类信息行不落盘（真机上"日志里没有"会被误读成"没发生"）—— 见 Logger.ClassifySeverity。
	private int _hzEmptyLogged;

	/// <summary>⭐ 2026-10-15（用户拍板「根因治掉吧」）：**是否已向 GCU 补送过"固件侧自动切换 = 关"**。
	/// 老版本会把"自动开着"告诉固件（<c>SwitchAutoRefreshRate(1)</c>），而那在**固件侧是持久的** ——
	/// 本轮改成"恒发 false"后，只有用户再拨一次开关才会送出去；这里在 GCU 可用后**主动补送一次 0**，
	/// 确保真凶（固件的 <c>GPU_DC_HZ</c>：息屏/显示恢复时把用户手选的档位顶回）处于关闭状态。</summary>
	private bool _gcuAutoOffPushed;

	/// <summary>⭐ 2026-10-15：性能模式行上一次 `ApplySegmentRow` 时**父容器是否可见**。
	/// 用于修「首次安装不显示『狂暴』」：行在父容器不可见（宽度 0）时排过一次分段布局后，父容器可见时因
	/// 布局签名未变而跳过重排 ⇒ 几何是按 0 宽算的 ⇒ 按钮被挤没。见 <c>ReflowPerformanceButtons</c>。</summary>
	private bool _perfRowSegmentsAppliedVisible;

	// ⚠️ 只缓存**非空**结果：空列表（那一刻内屏没枚举好/内屏关闭）不缓存 ⇒ 下次调用重测，
	// 与 ScreenGateProbe 的「只缓存成功值」一致 —— 否则启动早期的假阴性会被记住一分钟，
	// 反而把本来能显示的刷新率行继续判成没数据源；TTL 过期后重新枚举，外接屏/内屏切换能在一分钟内自愈。
	private static readonly long HzFallbackRatesTtlMs = 60000L;

	/// <summary>
	/// 2026-10-15：Win32 侧可用刷新率档位 —— **刷新率行的主数据源**（名字里的 "Fallback" 是历史遗留：
	/// 它最初只是 GCU 列表为空时的降级源，现在反过来，GCU 只在它读不到时兜底）。
	/// 纯读，只枚举显示模式：不写 config、不写注册表、不下发任何显示设置。
	/// 失败一律吞掉并按「读不到」（空列表）返回 ⇒ 调用方回退到 GCU 的列表。
	/// </summary>
	private IReadOnlyList<int> Win32FallbackRates()
	{
		long now = Environment.TickCount64;
		IReadOnlyList<int>? cached = Volatile.Read(in _hzFallbackRates);
		if (cached != null && now - Volatile.Read(in _hzFallbackRatesAt) < HzFallbackRatesTtlMs)
		{
			return cached;
		}
		IReadOnlyList<int> readOnlyList;
		try
		{
			// ⭐⭐ 2026-10-16（宝龙达 · 「屏幕刷新率没显示」）：
			// 真机诊断包实证 —— 蛟龙16Pro（MRID6-23）接着 HDMI 外接屏时**内屏识别为 None**
			// （活动显示路径里没有内屏）⇒ 原来的 `laptopScreen == null ⇒ 返回空列表`
			//         ⇒ `_hzButtons.Count == 0` ⇒ **「刷新率」整行消失**（用户报的现象）。
			//   而同一台机上 `GetRefreshRate` 因已有「退化为当前显示设备」的兜底 ⇒ 能读到 144Hz
			//         ⇒ 症状正是「亮度有、刷新率没有」。
			// ⚠️ 只在**宝龙达**机型上开启这条兜底 ⇒ GCU / 老接口机型**逐位不变**。
			// ⚠️ 它只影响**界面显示与即时切换**；落盘/档位校正那条路仍走
			//    `MechrevoHw` 里 `confidence == Embedded` 之后的调用（默认参数 = 原行为）。
			bool allowCurrentDeviceFallback = Program.hw?.UsesBaolongda == true;
			readOnlyList = MechrevoLite.Display.ScreenNative.GetAvailableRefreshRates(MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false), allowCurrentDeviceFallback);
			// ⭐ 诊断（2026-10-16）：**空列表**是「刷新率整行消失」的直接原因，而它在默认
			//   「仅错误」级别下过去看不到（`Logger.WriteLine` 的级别按消息内容推断）
			//   ⇒ 这里用 `WriteError` 保证**默认级别也落盘**；⚠️ 按状态翻转只记一次，避免刷屏。
			if (readOnlyList.Count == 0)
			{
				if (Interlocked.Exchange(ref _hzEmptyLogged, 1) == 0)
				{
					// ⚠️ 诊断值先在**洞外**算好再插值：洞内写具名实参（`log: false`）会被当成
					//    格式串的 `:` 截断，洞内写字符串字面量也易踩引号坑 —— 两者都避开。
					string hzScreen = MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false) ?? "(null)";
					bool hzAuto = AppConfig.Is("screen_auto");
					int hzMax = ScreenGateProbe.Win32MaxRefreshRate();
					Logger.WriteError("screen-hz: Win32 刷新率档位枚举为**空** ⇒ 刷新率整行会消失"
						+ " · 内屏识别=" + hzScreen
						+ " · 宝龙达兜底=" + (allowCurrentDeviceFallback ? "on" : "off")
						+ " · screen_auto=" + (hzAuto ? "1" : "0")
						+ " · Win32Max=" + hzMax);
				}
			}
			else if (Interlocked.Exchange(ref _hzEmptyLogged, 0) == 1)
			{
				Logger.WriteError("screen-hz: Win32 刷新率档位**已恢复**（" + readOnlyList.Count + " 档）⇒ 刷新率行应正常显示");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("hz-win32-fallback-fail", "hz source: Win32 刷新率档位枚举失败（按读不到处理）：" + ex.Message);
			return Array.Empty<int>();
		}
		if (readOnlyList.Count == 0)
		{
			return readOnlyList;
		}
		// ⭐ 2026-10-15（加固①，用户实测触发）：他在**独显直连**下用 NV 控制面板手动设了 165Hz ——
		// 那种"驱动另加的时序"**不在面板 EDID 的枚举结果里** ⇒ 列表里没有它，高亮一颗都匹配不上（全灭）。
		// 这里把**当前实测档**并进列表（±2Hz 视同已有）。
		// ⚠️ 刻意在这里（**缓存填充时**）做，而不是每次调用都并：VRR/G-Sync 下当前值会抖，
		//    每次并会让列表签名每 tick 变化 ⇒ 整行反复重建。放在 60s 缓存填充点，列表 60s 内保持稳定。
		try
		{
			int currentNow = MechrevoLite.Display.ScreenNative.GetRefreshRate(MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false));
			if (currentNow > 0 && !readOnlyList.Any((int hz) => Math.Abs(hz - currentNow) <= 2))
			{
				Logger.WriteLineIfChanged("hz-current-merged", $"hz source: 把驱动另设的当前档 {currentNow}Hz 并入档位列表（不在枚举结果里，如 NVCP 自定义时序）");
				readOnlyList = new List<int>(readOnlyList) { currentNow };
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLineIfChanged("hz-win32-fallback-fail", "hz source: 并入当前档失败（忽略，沿用枚举结果）：" + ex2.Message);
		}
		Volatile.Write(ref _hzFallbackRates, readOnlyList);
		Volatile.Write(ref _hzFallbackRatesAt, now);
		return readOnlyList;
	}

	private RCheckBox? _kbPowerSw;

	private RCheckBox? _lbPowerSw;

	private RCheckBox? _logoPowerSw;

	private RCheckBox? _hingePowerSw;

	private ComboBox? _hingeEffectCombo;

	private RCheckBox? _syncPowerSw;

	private ComboBox? _syncEffectCombo;

	private ComboBox? _kbEffectCombo;

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：宝龙达「Logo 灯」开关跑到最右 —— 第 3 次修复）：
	/// 宝龙达**非键盘灯行**（灯带 / Logo / 铰链 / 同步）的「记录」（行 + 开关 + 日志名）。
	/// ⚠️ 该表在**刷新期**由 <see cref="ApplyBaolongdaLightRowLayout" /> 每次更新
	/// （建行期 hw 可能还没就绪 ⇒ 不能只在建行期记录）✅。
	/// </summary>
	private readonly List<(TableLayoutPanel Row, RCheckBox Switch, string Label)> _baolongdaCollapsedLightRows = new List<(TableLayoutPanel, RCheckBox, string)>();

	/// <summary>防止 <see cref="ApplyBaolongdaLightRowLayout" /> 在 Layout 事件里递归重入 / 反复重排 ✅。</summary>
	private bool _applyingCollapsedLightRowLayout;

	private ComboBox? _lbEffectCombo;

	private ComboBox? _logoEffectCombo;

	private Label? _lblKeyboardControllerStatus;

	private bool _syncingEffectCombos;

	/// <summary>
	/// ⭐⭐ 2026-10-19（运行时自校准）：键盘灯能力探测是否已在**本界面**排过队（0/1）。
	/// <para>⚠️ 只防重复排队；真正的"只跑一次"由 <c>MechrevoHw.EnsureKeyboardLightingCalibration()</c>
	/// 的缓存保证 ✅。</para>
	/// </summary>
	private int _kbdCalibrationQueued;

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 键盘灯效表）：
	/// ⭐ 宝龙达键盘灯的**可写模式表 ＝ 三项** ✅（⭐ 下拉、⭐ 索引→Id 映射、
	/// ⭐ 点进 LightForm 那扇窗、⭐ 以及下发时的 Id→模式映射 ⭐ **四处共用** ✅）。
	///
	/// <para>⭐⭐ <b>为什么没有「自定义颜色（Manual）」</b>：
	/// ⭐ 原厂枚举里模式 <c>3</c> 只定义、从不写入 ⚠️；⭐ 40 系真机（MRID6-23）实测
	/// **写 3 后回读仍为 2** ⇒ ⭐ 本平台固件**不接受模式 3** ❌
	/// ⇒ ⭐ 按能力位铁律**不放进界面**（⛔ 不假装支持 ✅）。
	/// ⭐ 单色统一走 <c>Single</c>（模式 <c>2</c>）＋ 功能号 <c>17</c> 的真实 RGB ✅。</para>
	///
	/// <para>⭐ 三项对应 <c>MechrevoHw.TryMapBaolongdaKeyboardMode</c> 的 0／2／1 ✅；
	/// ⭐ 关灯**不走本表**：⭐ 关灯 ＝ ⭐ <b>写亮度（功能号 18）＝ 0</b> ✅
	/// （⭐ 见 <c>MechrevoHw.TrySetKeyboardLightingViaBackend</c> ✅）。</para>
	/// </summary>
	// ⭐⭐ 2026-10-19（机主：「键盘下拉里面的关和左边开关功能重复」）：⭐ 下拉里**去掉「关」** ✅
	//   ⭐ 开/关由左边那颗开关负责（写亮度 0），下拉只选"开着的时候是什么效果" ✅
	//   ⚠️ 顺序保持 Single(2) → Rainbow(1)，与 `MechrevoHw.TryMapBaolongdaKeyboardMode` 一致 ✅
	// ⭐⭐ 2026-10-19 第二轮（机主：「先不禁用，参照 7.3 的实现」）：
	//   ⭐ 恢复「彩虹」，并按第三方 7.3 的做法（**同一套 WMI 16/17/18**）补写入细节：
	//     · 7.3 设亮度是「先 4 → 再 0 → 再目标值」的**复位序列**（我们的"只写变化项"可能因此被固件忽略）
	//     · 7.3 设彩虹 = `RGBKeyboardMode = Mode_RGBAutoCyclic(1)`，固定色 = 2 + 颜色(17)
	// ⭐⭐ 2026-10-19 第三轮（机主批准「软件彩虹」）：
	//   ⭐ 真机确认本机固件「模式 16=1」无效 ⇒ 彩虹改为**软件刷色**（16=2 + 每 50ms 写 17）✅
	//   ⇒ 界面**始终保留**这两项（⛔ 不再按"彩虹可用性"隐藏）✅
	private static readonly (string Effect, string Name)[] BaolongdaLightEffects = new (string, string)[2]
	{
		("Single", Strings.EffectSingle),
		("Rainbow", Strings.EffectRainbow)
	};

	/// <summary>
	/// ⭐ 宝龙达时把任意「声明的效果表」收敛成**本机真能做到的项** ✅；
	/// ⭐ 其他机型（⭐ GCU ／ 老同方 ／ 非宝龙达 ✅）**原样返回** ⇒ ⭐ 逐位不变 ✅。
	/// </summary>
	private static (string Effect, string Name)[] EffectsForCurrentBackend((string Effect, string Name)[] declared)
	{
		return (Program.hw?.UsesBaolongda == true) ? BaolongdaLightEffects : declared;
	}

	private RCheckBox? _lightOffOnBatteryChk;

	private RComboBox? _lightSleepCombo;

	/// <summary>
	/// ⭐ 2026-10-19（「自动关闭时间」复用头部下拉）：当前填充的平台（-1=未填 / 0=其它机型厂商逻辑 / 1=宝龙达软件实现）。
	/// <para>⚠️ 只在**平台切换**时重填选项，⛔ 不每次刷新都清空重建 ✅。</para>
	/// </summary>
	private int _lightAutoOffComboPlatform = -1;

	private bool _syncingLightHeader;

	private Action? _fitQuickCards;

	private int _kbCmdGen;

	private Task? _kbPowerBypassInFlight;

	private SettingsDialog? _settingsDialog;

	/// <summary>2026-10-09（settingswarm）：设置对话框外壳**预热**的状态位。
	/// <para>用户原话「设置界面拉起要2秒说是 不能立刻拉出来吗」——本机实测（临时探针，验收后已删）：
	/// 齿轮「设置」首次打开 = 5347ms（其中 ctor 504ms、`ResponsiveLayout.PerformLayoutTree` 1450ms、
	/// 首次 `Show()` 3385ms），第二次打开 = 1657ms（其中 `PerformLayoutTree` 1368ms）。</para>
	/// <para><c>_settingsShellPreWarmArmed</c>：已经排过一次队（防重复排队）；<c>_settingsShellReady</c>：
	/// 外壳已建好并**排过整棵树**（点击时只剩 Show + 刷新数据，ShowAdjacentTo 里那次重排会被签名跳过）。</para></summary>
	private bool _settingsShellPreWarmArmed;

	private bool _settingsShellReady;

	/// <summary>预热重试计数（有别的模态窗开着时先不建，避免把别人的模态框卡住）。</summary>
	private int _settingsShellPreWarmTries;

	private RCheckBox? _autoHzChk;

	private RCheckBox? _overdriveChk;

	private bool _overdriveAvailable;

	private ComboBox? _calibCombo;
	private RButton? _calibSettingsBtn;
	private RCheckBox? _calibSwitch;

	private Button? _dayModeButton;

	private Button? _nightModeButton;

	private Panel? _themeModePanel;

	private RCheckBox? _themeFollowBox;

	private Control[] _dashboardSections = Array.Empty<Control>();

	private readonly HashSet<Control> _enabledDashboardSections = new HashSet<Control>();

	private BufferedPanel? _unsupportedModelBanner;

	private Label? _unsupportedModelLabel;

	private RTextBox? _modelOverrideBox;

	private Label? _modelOverrideStatus;

	private static readonly string[] WriteEntryControlNames = new string[7] { "sliderBattery", "buttonEco", "buttonStandard", "buttonUltimate", "buttonTurbo", "buttonSilentTurbo", "buttonCustomMode" };

	private Panel? _officialConsolePanel;

	// 2026-10-05「核显模式显示策略」③：设置对话框里的「核显模式显示」项。
	// 2026-10-09（igpugate）：两态开关 → **三选**（自动（按硬件）/ 强制显示 / 强制隐藏），
	// 键仍是 igpu_only_ui（值域 auto/show/hide，见 Gpu\IgpuOnlyUiMode.cs）。
	private Panel? _igpuOnlyUiPanel;

	private RButton _igpuOnlyUiAuto = null!;

	private RButton _igpuOnlyUiShow = null!;

	private RButton _igpuOnlyUiHide = null!;

	private bool _syncingIgpuOnlyUi;

	// ⚠️ 2026-10-09（lightsplit，用户原话「额外灯光项应该细分」）：原来**一个**三态行只管「灯带 + Logo」两行，
	// 表达不了"隐灯带、留 Logo"（16Pro 2024 正是这个需求）⇒ 现在拆成**两个独立常显行**：
	//   * 「灯带行显示」=> 键 light_strip_ui（值域 auto/show/hide，见 LightStripUiMode）；
	//   * 「Logo 灯行显示」=> 键 light_logo_ui（见 LightLogoUiMode）。
	// 各键只影响**自己那一行**的可见性与灯光区通道数；旧键 light_extra_ui 已退化为一次性迁移的数据源
	// （见 LightExtraRows.MigrateLegacyOnce）。
	private Panel? _lightStripUiPanel;

	private Panel? _lightLogoUiPanel;

	private RButton _lightStripUiAuto = null!;

	private RButton _lightStripUiShow = null!;

	private RButton _lightStripUiHide = null!;

	private RButton _lightLogoUiAuto = null!;

	private RButton _lightLogoUiShow = null!;

	private RButton _lightLogoUiHide = null!;

	private Label? _officialConsoleStatus;

	private RButton? _officialConsoleButton;

	// ⭐⭐ 2026-10-18（机主：设置页加 PawnIO 安装入口）✅
	private Label _pawnioStatus;

	private RButton _pawnioButton;

	private Label _pawnioWarning;

	private bool _pawnioBusy;
	private readonly System.Windows.Forms.Timer _officialConsoleStatusTimer = new System.Windows.Forms.Timer
	{
		Interval = 3000
	};

	private bool _arrangingDashboard;

	private string _lastStackLayout = "";

	private string _lastWindowHeightSignature = "";

	private readonly MechrevoDeviceCapabilities _deviceCapabilities = MechrevoDeviceCapabilities.Load();

	private string _lastCapabilityLayout = "";

	private int _refreshCapsQueued;

	private CheckBox[] _quickSwitches = Array.Empty<CheckBox>();

	private CheckBox? _usbChargerBox;

	private CheckBox? _fanBoostBox;

	private CheckBox? _gpuOcBox;

	private string _lastPerformanceLayout = "";

	/// <summary>
	/// 2026-10-12（用户要求"几个按钮大小完全一致"）：按行记下"等宽行"的按钮，行宽变化时
	/// 重算最后一颗的右边距（把 TableLayoutPanel 兜给最后一列的取整余量挪出按钮宽度）。
	/// </summary>
	private readonly Dictionary<TableLayoutPanel, RButton[]> _segmentRows = new Dictionary<TableLayoutPanel, RButton[]>();

	/// <summary>已挂过 <c>ClientSizeChanged</c> 的行（⛔ 避免重复挂）。</summary>
	private readonly HashSet<TableLayoutPanel> _segmentRowHooked = new HashSet<TableLayoutPanel>();

	/// <summary>等宽行的重入保护（改 Margin 会让父表再排一次版）。</summary>
	private bool _equalizingSegmentRows;

	private string _lastGpuLayout = "";

	private GpuRowLayout _gpuRowLayout;

	private bool _gpuSwitchInProgress;

	// 2026-10-05: monotonic id of the latest GPU-mode UI request. A pending restart
	// prompt whose id is stale (the user picked another mode meanwhile) is discarded,
	// and only one prompt may be on screen at a time.
	private static int _gpuSwitchRequestSeq;

	private static bool _gpuRestartPromptOpen;

	private int _lastGpuModeHint = int.MinValue;

	// 2026-10-13（修「切完显卡模式，这一行 UI 依旧不刷新」）：VisualiseGPUMode 画完按钮后，把
	// **这一次真正画上去的**高亮档位与「重启以应用」状态记下来；周期刷新按同一套判据重算，
	// 不一致就重画一次 —— 否则标签每 2 秒重算、按钮高亮却只在交互时画一次，两者会互相打架。
	private int _lastVisualisedGpuMode = int.MinValue;

	private bool _lastVisualisedGpuPending;

	private DateTime _lastQuickSwitchUi = DateTime.MinValue;

	// ⭐⭐ 2026-10-18（机主 UI 体验 · 强冷乐观更新）：⏱ 宽限期内**保持用户点的状态**，
	//   ⛔ 不让定时器/周期刷新按 `FanBoostActive` 提前拨回；只有**最终确认失败**才回滚 ✅
	//   ⚠️ 用 static：`ReadQuickSwitchState` 是静态方法（磁贴/更多开关共用同一读回入口）✅
	private static DateTime _fanBoostOptimisticUntil = DateTime.MinValue;

	private static bool _fanBoostOptimisticValue;

	/// <summary>
	/// ⭐ 2026-10-17：<c>capslock</c> / <c>numpad</c> 直读系统锁定态时，**每个键首次必打一行**日志，
	/// 之后只在值发生变化时打。用途：证明"这条读路径到底有没有跑过"（否则它静默生效，
	/// 日志里完全看不出来，无法与"根本没跑"区分）。
	/// </summary>
	private readonly HashSet<string> _lockKeyLoggedOnce = new HashSet<string>(StringComparer.Ordinal);
	private bool _syncingSwitches;

	// 2026-10-11（修回归「修复以后直接点不动了」）：「息屏（不睡眠）」点击后，开关的勾选态至少显示这么久再回落。
	// RCheckBox 只在 OnCheckedChanged 里 Invalidate()（重绘排队），若在同一条消息里就把 Checked 改回 false，
	// 那一下永远来不及重绘 ⇒ 用户看到的就是"点了完全没反应"。260ms 足够让用户看到开关动了一下。
	private const int MonitorOffSwitchVisibleMs = 260;

	// 2026-10-10（修用户反馈「打开或者关闭会卡一会」）：winupdate 的动作链改到后台线程执行后，
	// 用 _winupdateBusy 表示「动作进行中」——定时器 UpdateQuickSwitches 跳过 winupdate 键，
	// 且回调本身忽略进行中的再次拨动（重入安全，绝不让两个动作并发改系统）。
	private bool _winupdateBusy;

	private bool _winupdateBusyTarget;

	private int _deepSleepPendingValue = -1;

	private bool _syncingDisplay;

	// ⭐⭐ 2026-10-19（机主：「设置窗口里根本没有这些功能，为什么要加载这么多？」）：
	//   设置窗**构建分段计时**（只打一次；默认级别可见）—— 先量再改 ✅
	//   · `_uiBuildPerfMs` / `_uiBuildLightMs` 由 BuildDashboardLayout 内部累加（性能组 / 灯光组）；
	//   · `_uiBuildLogOnce` 保证同一进程只落一行（多实例/重入不刷屏）。
	private long _uiBuildPerfMs;

	private long _uiBuildLightMs;

	private static int _uiBuildLogOnce;

	private long _uiBuildLayoutTicks;

	private long _uiBuildLayoutCount;

	private bool _uiBuildProfiling;

	// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
	//   构造期这两个控件是**局部变量**（定时器靠闭包访问）⇒ 现在提到字段，供「只读同步」复用。
	//   · `_brightnessSlider` / `_brightnessValueLabel`：滑块与百分比标签；
	//   · `_lastBrightnessSystemSyncTick`：低频只读轮询节流（5 s，且只在设置页可见时跑）；
	//   · `_brightnessUnsupportedShown`：读不到系统亮度 ⇒ 滑块置灰 + 标注（只改 UI，⛔ 不写硬件）；
	//   · `_brightnessRowVisible`：亮度行当前是否被既有能力闸门放行（恢复显隐时保持闸门语义）。
	private const int BrightnessSystemPollMs = 5000;

	private RSlider _brightnessSlider;

	private Label _brightnessValueLabel;

	private long _lastBrightnessSystemSyncTick;

	private bool _brightnessUnsupportedShown;

	private bool _brightnessRowVisible;

	private RCheckBox _displayEffectSwitch = null!;

	private ComboBox _displayEffectCombo = null!;

	private RButton _displayEffectEditBtn = null!;

	private DateTime _lastHzUi = DateTime.MinValue;

	private bool _syncingLc;

	private bool _lcConnecting;

	private bool _lcAutoTried;

	private bool _lcStatusTicking;

	private bool _lcWasConnected;

	private bool _lcManualDisconnect;

	private int _lcReconnectAttempts;

	private DateTime _lcLastReconnectAttempt = DateTime.MinValue;

	private int _lcGcuConnectAttempts;

	private DateTime _lcLastGcuConnectAttempt = DateTime.MinValue;

	private DateTime _lcLastGcuStatusRequest = DateTime.MinValue;

	private int _lcGcuRestoreGeneration = -1;

	private int _lcGcuRestoreAttempts;

	private DateTime _lcGcuLastRestoreAttempt = DateTime.MinValue;

	private SystemBluetoothConnectionObservation _lcSystemBluetoothObservation;

	private int _performanceRequest;

	private readonly BrightnessCommitQueue _brightnessCommitQueue;

	// ⭐⭐ 2026-10-19（机主：亮度调节有延迟 / 外部改亮度 LM 不同步）：
	//   · `_brightnessMergedSubmits`：上一轮实际写之前合并了多少次拖动/输入（写日志用）；
	//   · `_brightnessLastThrottleFlushTick`：拖动期的**节流写**时间戳（~120ms 合并一次）；
	//   · `_brightnessReadInFlight`：只读回读的"在飞"标记（回读放到后台线程，⛔ 不阻塞 UI）。
	private int _brightnessMergedSubmits;

	private long _brightnessLastThrottleFlushTick;

	private int _brightnessReadInFlight;

	private readonly System.Windows.Forms.Timer _liquidCoolingStatusTimer = new System.Windows.Forms.Timer
	{
		Interval = 3000
	};

	private readonly System.Windows.Forms.Timer _displayStatusTimer = new System.Windows.Forms.Timer
	{
		Interval = 3000
	};



	private readonly System.Windows.Forms.Timer _quickSwitchStatusTimer = new System.Windows.Forms.Timer
	{
		Interval = 3000
	};

	private LiquidCoolingLightMenu? _liquidCoolingLightMenu;

	private DateTime _lastBrightnessUi = DateTime.MinValue;

	private DateTime _lastCalibUi = DateTime.MinValue;
	private DateTime _calibSyncHoldUntil = DateTime.MinValue;
	private int _calibSetInFlight;
	private int? _lastMqttCalibLevel;

	private int _runtimeResourcesDisposed;

	private int _officialStatusRefreshRunning;

	private int _startupStatusLoading;

	private bool _syncingStartup;

	private int _lastVisualMode = int.MinValue;

	private bool _lastVisualSilentTurbo;

	private RgbForm? rgbForm;

	private LightForm? lightbarForm;

	private LightForm? logoLightForm;

	private readonly System.Timers.Timer _sensorTimer;

	private readonly System.Windows.Forms.Timer batteryTimer = new System.Windows.Forms.Timer
	{
		Interval = 200
	};

	public Updates? updatesForm;

	private static long lastRefresh;

	private static long lastBatteryRefresh;

	private static long lastLostFocus;

	private bool isGpuSection = true;

	private bool isMuxGpu = true;

	private bool batteryMouseOver;

	private bool batteryFullMouseOver;

	private bool activateCheck;

	private static readonly string[] DashboardAuditPageNames = new string[1] { "Main" };

	private bool _applyingResponsiveBounds;

	private int _dashboardWindowDpi;

	private bool _hasPresentedWindow;

	private Rectangle _lastWorkingArea;

	private FormWindowState _lastWindowState;

	private bool isDark = RForm.CheckSystemDarkModeStatus();

	private bool _lIconSet;

	internal static readonly string BatteryLimitUnknownText = Strings.Unreadable;

	private IContainer components;

	private Panel panelBattery;

	// ⭐⭐ 2026-10-18（机主：宝龙达电池页被隐藏）：⭐ 宝龙达充电阈值面板可用性（进程内缓存；
	//    只有 `UsesBaolongda` 才会去读 EC，⛔ 非宝龙达不触发任何 EC 访问）✅
	private bool _baolongdaChargePanelProbed;

	private bool _baolongdaChargePanelAvailable;

	// ⭐⭐ 2026-10-18（机主：行索引撞车）：⭐ 宝龙达面板行**单独标记**（-1 = 未追加）✅
	private int _bdChargePanelRowIndex = -1;

	private int _bdChargePanelRowHeight;

	private Panel panelFooter;

	private RButton buttonQuit;

	private RCheckBox checkStartup;

	private Panel panelPerformance;

	private TableLayoutPanel? perfConfigRow;

	private RComboBox[]? perfConfigCombos;

	private RButton? perfConfigEditButton;

	/// <summary>
	/// 2026-10-11（UI 重排第三轮 · 回退）：性能模式**标题行**里那组快捷开关的容器 ——
	/// 成员＝「风扇强冷」（<c>_fanBoostBox</c>）、「显卡超频」（<c>_gpuOcBox</c>）、「配置编辑」（<c>perfConfigEditButton</c>）。
	/// 只负责"紧跟「性能模式」标题文字、左对齐"这一件事，⛔ 不改三者的任何显隐判据（由各自原调用点下发）。
	/// </summary>
	private FlowLayoutPanel? _perfShortcuts;

	/// <summary>
	/// 2026-10-12（用户原话「我发现它是底边和标题对齐的 能不能改成中线的标题对齐」）：
	/// <see cref="CenterPerfShortcutsVertically" /> 的重入保护（该方法会写子控件的 <c>Margin</c>，
	/// 而 Margin 变化会让 FlowLayoutPanel 再排一次版 ⇒ 没有它就会自我递归）。
	/// </summary>
	private bool _perfShortcutCentering;

	/// <summary>
	/// 2026-10-12（本轮 · 修「标题行快捷开关容器永不显示」）：「显卡超频」/「配置编辑」各自
	/// <b>意图可见性</b>的最后一次下发值（由原调用点在下发 <c>Visible</c> 的同一处记下）。
	/// ⚠️ 为什么必须有它：<see cref="Control.Visible" /> 的 getter 返回**有效**可见性（父链任一不可见即 false），
	/// 构造期窗体还没 Show、或容器自己被收起时子控件的有效值恒 false ⇒ 求和恒 0 ⇒ 容器自锁。
	/// ⛔ 只读记录，⛔ 不参与任何硬件/下发判据。
	/// </summary>
	private bool _perfShortcutGpuOcIntent;

	/// <summary>同上：<c>perfConfigEditButton</c>（「配置编辑」）的意图可见性。</summary>
	private bool _perfShortcutConfigEditIntent;

	/// <summary>2026-10-11：性能模式**标题行**（[图标][标题][快捷开关组][弹性余量][CPU 读数]）的容器。</summary>
	private TableLayoutPanel? _perfHeaderRow;

	/// <summary>2026-10-11（真机「叠在一起了」修复）：性能卡片按钮行（Dock=Top，贴在标题行下方）。</summary>
	private TableLayoutPanel? _perfModeRow;

	/// <summary>2026-10-11（真机「叠在一起了」修复）：显卡卡片按钮行（同上）。</summary>
	private TableLayoutPanel? _gpuModeRow;

	/// <summary>2026-10-11：显卡模式**标题行**（[图标][标题][状态文字][弹性余量][GPU 读数]）的容器。</summary>
	private TableLayoutPanel? _gpuHeaderRow;

	private int _perfHeightCompact;

	private int _perfHeightWithConfig;

	private int _perfRowExtraHeight;

	// ==========================================================================================
	// 2026-10-11（本轮任务 · 快捷磁贴行）：用户原话「参考这个 在性能模式下面加一个快捷按钮…」
	//   「关闭屏幕那个按钮的位置改成加号按钮」
	//   「准确描述位置:性能模式按钮的下面一行（30系要在配置下拉的下面一行）」
	// + 用户对四个阻塞点的答复① 同源（点磁贴 = 勾选/取消同一个开关）、② 抬高窗口最小宽度
	//   —— ⚠️ 2026-10-12 用户改口径为「③ 不要变宽：缩小磁贴让它一行放下」⇒ 最小宽已回到 420，
	//   磁贴改为**收小 + 按实测文字宽分列 + 字号自适配**（见 QuickTileLogicalHeight / ComputeQuickTileCellWidths）。
	//
	// 设计（与「更多开关」**同源**，⛔ 不做第二套状态）：
	//   * 磁贴**只**按 key 引用「更多开关」里那颗 RCheckBox（见 <see cref="FindQuickSwitch" />）；
	//     高亮态**只**由 <see cref="ReadQuickSwitchState" /> 读回，与「更多开关」读的是同一个函数；
	//     点击 = 让那颗 CheckBox 自己走它既有的下发路径（<see cref="ToggleQuickTileAsync" />），
	//     ⛔ 磁贴自己从不直接调 service ⇒ 两侧不可能出现两套状态。
	//   * 磁贴集合持久化为 AppConfig 单键 <c>perf_quick_tiles</c>（全局、逗号分隔 key 列表、缺省 = 默认集）；
	//     **渲染时按能力位过滤**（为什么不在存的时候过滤：AppConfig 是单份 config.json，
	//     而"哪些开关存在"由机型/运行期能力位决定 —— 换机型后旧键必须能被重新过滤而不是被抹掉）。
	//   * 最后一格固定是「＋」（打开自定义面板）。
	// ==========================================================================================

	/// <summary>快捷磁贴行的持久化键（全局，逗号分隔的开关 key 列表；缺省 = <see cref="DefaultQuickTileKeys" />）。</summary>
	private const string QuickTilesConfigKey = "perf_quick_tiles";

	/// <summary>
	/// 2026-10-12（用户原话「快捷开关最少 0 个（全部不选就只显示自定义）」）：**显式空集**的哨兵值。
	/// <para>为什么需要哨兵：本键的语义是"**缺省**（键不存在/空串）= 默认集"，而"用户明确全不选"是另一个状态 ——
	/// 若沿用"空串/空列表 ⇒ 默认集"，用户的 0 选会被下一次读取还原成默认集（用户原话正是"全部不选就只显示自定义"）。
	/// 故取一个**不可能与任何开关 key 相撞**的字面量（开关 key 全是 <c>[a-z]+</c> 标识符）来区分三种状态：
	/// <list type="bullet">
	/// <item><c>键不存在</c>（<see cref="AppConfig.GetString" /> 返回 null）⇒ <see cref="DefaultQuickTileKeys" />（首次使用/老配置）；</item>
	/// <item><c>"-"</c> ⇒ **显式空集** ⇒ 磁贴行只剩末位那颗「自定义」（<see cref="LoadQuickTileKeys" /> 直接返回空表）；</item>
	/// <item><c>"a,b,c"</c> ⇒ 这些 key，**且顺序就是磁贴顺序**（用户原话「还有希望可以自行调整顺序」）。</item>
	/// </list>
	/// ⛔ 不新增第二个布尔键：单键三态已能表达全部语义，且老配置（无此键）语义不变。</para>
	/// </summary>
	internal const string QuickTilesEmptySentinel = "-";

	/// <summary>该原始串是否表示"用户明确一个都不选"（见 <see cref="QuickTilesEmptySentinel" />）。⛔ 只读判断，不写配置。
	/// ⚠️ 形参**故意不写** <c>string?</c>：本文件没有 <c>#nullable</c> 上下文，写了会多一条 CS8632（见 ApplyBatteryLimitModeAsync 同款注释）。</summary>
	internal static bool IsExplicitEmptyQuickTiles(string raw)
	{
		return raw != null && raw.Trim() == QuickTilesEmptySentinel;
	}

	/// <summary>
	/// 用户指定的默认磁贴集（原话「默认磁贴 = 一键强冷 · 摄像头 · 麦克风 · Win键锁 · 息屏 · ＋」）：
	/// 前 5 个是动作格的 key，第 6 格由代码固定补成「＋」。
	/// <list type="bullet">
	/// <item><c>fanboost</c> = 一键强冷（该开关本体已按用户要求移回「更多开关」组）；</item>
	/// <item><c>webcam</c> = 摄像头；</item>
	/// <item><c>micmute</c> = 麦克风（开关文案「麦克风静音」，磁贴文案「麦克风」）；</item>
	/// <item><c>winkey</c> = Win键锁；</item>
	/// <item><c>monitoroff</c> = 息屏 —— 用户答复①「息屏和其他开关里面的息屏**同逻辑**」。
	/// <para>**认定依据**（2026-10-12 逐条核对「更多开关」的 (键, 文案) 表 <see cref="QuickSwitchLabels" />）：
	/// <c>monitoroff → Strings.QuickMonitorOff = 「息屏（不睡眠）」</c>（zh-CN resx 3497-3500）；
	/// <c>deepsleep → Strings.QuickDeepSleep = 「深度睡眠」</c>（zh-CN resx 3441-3444）。
	/// 字面叫「息屏」的**只有 monitoroff 一项** ⇒ 磁贴与它同键、同一条下发路径
	/// （<c>ApplyQuickMonitorOffAsync</c>，就是「更多开关」里那颗 CheckBox 点下去走的同一个方法）。</para>
	/// <para>⚠️ 上一轮默认用的是 <c>deepsleep</c>，有**两处**不对：①它的开关文案是「深度睡眠」不叫「息屏」；
	/// ②它的能力位是 GCU 上报的 <c>DeepSleepSeen</c>，而本机没上报过 ⇒ `QuickTileSupported("deepsleep")`
	/// 判假、磁贴被能力位过滤掉 —— 这正是用户截图里那一行只剩
	/// 「风扇强冷 · 摄像头 · 麦克风 · Win键锁 · ＋」（4 个动作）的原因，不是键没定。</para>
	/// <c>deepsleep</c> 仍留在「更多开关」里，可从「＋」面板选成磁贴（那时磁贴文案 = 开关文案「深度睡眠」）。
	/// </item>
	/// </list>
	/// </summary>
	private static readonly string[] DefaultQuickTileKeys = new string[5] { "fanboost", "webcam", "micmute", "winkey", "monitoroff" };

	/// <summary>动作格上限（第 6 格固定是「＋」）—— 直接来自用户答复“窄窗放不下 5 动作 + ＋”。</summary>
	private const int QuickTileActionSlots = 5;

	/// <summary>
	/// 磁贴行的容器（<c>Dock=Top</c>，列数 = 动作格数 + 1（末位「＋」）；
	/// 列宽按**实测文字宽**加权分配，见 <see cref="DistributeQuickTileColumns" />）。
	/// </summary>
	private TableLayoutPanel _quickTileRow;

	/// <summary>当前在屏的磁贴按钮（顺序 = <see cref="_quickTileKeys" />；末尾多一个「＋」）。</summary>
	private readonly List<RButton> _quickTileButtons = new List<RButton>();

	/// <summary>当前在屏的磁贴 key（已按能力位过滤、已裁到 <see cref="QuickTileActionSlots" />）。</summary>
	private readonly List<string> _quickTileKeys = new List<string>();

	private readonly ToolTip _quickTileToolTip = new ToolTip();

	/// <summary>
	/// 2026-10-12（用户原话「给按钮加个标题 叫快捷开关 类似性能模式之类的」）：磁贴行的**标题行**
	/// （<c>Dock=Top</c>，与「性能模式 / 显卡模式」**同一套** <see cref="BuildTitleRow" /> + 既有字形图标）。
	/// 位置：磁贴行正上方、配置下拉行（30 系）之下 —— 见 BuildPerfModeRow 里的倒序停靠说明。
	/// </summary>
	private BufferedTableLayoutPanel _quickTileHeaderRow = null!;

	/// <summary>「快捷开关」标题文字（字号/颜色在 BuildDashboardLayout 里与 labelPerf/labelGPU 并列下发）。</summary>
	private Label _quickTileHeaderLabel = null!;

	/// <summary>
	/// 磁贴行的**自然**宽（设备像素，实测）：Σ 每格「文字实测宽 + D(<see cref="QuickTileTextPadLogical" />)」
	/// + 格数 × gap。<see cref="DistributeQuickTileColumns" /> 用它当**权重**把行宽按比例分给各格
	/// （⛔ 不再按"最宽一格 × 格数"等宽切 —— 那样「自定义快捷按钮」那一格必然放不下）。
	/// ⚠️ 只用于列宽权重与取证日志；⛔ 不参与窗口最小宽（用户答复③已撤销"抬高最小宽度"）。
	/// </summary>
	private int _quickTileNaturalRowWidth;

	/// <summary>
	/// 每一格**所需内容宽**（设备像素，⛔ 不含该格 Margin 的 gap；口径见
	/// <see cref="ComputeQuickTileCellWidths" />）。长度 = 动作格数 + 1（末位是「＋」格）。
	/// </summary>
	private int[] _quickTileCellDesired = Array.Empty<int>();

	/// <summary>磁贴文字字号（运行期按行宽下限自适配，见 <see cref="ChooseQuickTileFontSize" />；行内共用一份）。</summary>
	private float _quickTileFontSize;

	/// <summary>磁贴行共用的文字字体（换字号时把旧的那份显式 Dispose，见 <see cref="RebuildQuickTileButtons" />）。</summary>
	private Font _quickTileFont = null!;

	/// <summary>磁贴自然高（逻辑像素）：图标 + 一行文字 + 上下留白。用户原话「按钮巨大很难看」⇒ 54 → 44。</summary>
	private const int QuickTileLogicalHeight = 44;

	/// <summary>磁贴之间的逻辑间距（与 <c>UiVisualStyle.ApplyIndependentRow</c> 的 D(6) 同口径；本轮 6 → 4）。</summary>
	private const int QuickTileLogicalGap = 4;

	/// <summary>磁贴图标边长（逻辑像素）：20 → 16（与行高一并收小，见 <see cref="QuickTileLogicalHeight" />）。</summary>
	private const int QuickTileLogicalIcon = 16;

	/// <summary>
	/// 磁贴文字左右安全余量（逻辑像素）：<c>UiAuditRunner.GetTextClipping</c> 对 ButtonBase 的判据是
	/// 「文本宽 ≤ 客户区宽 − 8」⇒ 列宽按「实测文本宽 + D(8)」给，**绘制与审计两条口径同时成立**。
	/// </summary>
	private const int QuickTileTextPadLogical = 8;

	/// <summary>
	/// 候选字号（**从大到小**）：行宽放不下时逐档下探（⛔ 绝不出省略号）。上限 8 比上一版的 8.5 已收小
	/// —— 用户原话「按钮巨大很难看…图标尺寸、文字字号、内边距一并收小」。
	/// </summary>
	private static readonly float[] QuickTileFontSizes = new float[3] { 8f, 7.5f, 7f };

	/// <summary>
	/// 在途的快捷开关（键 = <c>Tag</c>，正在下发）。2026-10-12（用户原话「我发现第三个按钮有概率出现
	/// 点不动的情况 只有开关第二个按钮才能再次可点击」）新增：**磁贴的可点性必须由"本键是否在途"决定，
	/// ⛔ 不许再跟着那颗 CheckBox 的 <c>Enabled</c> 走** —— CheckBox 的 <c>Enabled=false</c> 只是
	/// "这颗开关此刻在忙"的反馈（见 <c>ApplyQuickSwitchAsync</c> / 深睡 / 个人化开关三处），
	/// 而 <see cref="RefreshQuickTiles" /> 恰恰是在那个忙碌窗口里被调用的（<see cref="ToggleQuickTileAsync" />
	/// 先拨 Checked 再刷新）⇒ 旧写法会把"刚点过的那颗磁贴"锁成 <c>Enabled=false</c>，
	/// 且动作结束后没有任何一处重算它，只能等下一次别的开关刷新或 12s 后的定时器。
	/// </summary>
	private readonly HashSet<string> _quickSwitchInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	// 2026-10-05: guards RefreshPerfConfigComboLabels() from re-sending the mode
	private bool _refreshingConfigLabels;

	private TableLayoutPanel tablePerf;

	private RButton buttonTurbo;

	private RButton buttonBalanced;

	private RButton buttonSilent;

	private Panel panelGPU;

	private TableLayoutPanel tableGPU;

	private RButton buttonUltimate;

	private RButton buttonStandard;

	private RButton buttonEco;

	private RButton _igpuSavingOff = null!;

	private RButton _igpuSavingOn = null!;

	private RButton _igpuSavingAuto = null!;

	private Label _igpuSavingLabel = null!;

	private BufferedTableLayoutPanel _igpuSavingRow = null!;

	private bool _syncingIgpuSaving;

	/// <summary>
	/// 2026-10-13（用户批准「连同强制一起做」）：本次「核显省电」点击里，用户是否已经**明确同意**
	/// 结束占用独显的程序（⭐ 安全门 (d) 的"主动的第二次动作"）。⚠️ 只在**当前这一次**
	/// <see cref="SwitchIgpuSavingAsync" /> 调用内有效：进方法置 false、finally 置回 false，
	/// ⛔ 绝不跨点击复用（否则一次同意会变成长期默认同意）。
	/// </summary>
	private bool _igpuSavingForceConfirmedThisAttempt;

	/// <summary>
	/// 2026-10-13：本次点击是否已经就「独显正在独家驱动显示」向用户做过**明确、充分告知**
	/// （⚠️ 安全门 (b) 的 override 只认这个标记）。与 <see cref="_igpuSavingForceConfirmedThisAttempt" />
	/// 同生命周期，⛔ 不落盘、⛔ 不跨点击。
	/// </summary>
	private bool _igpuSavingDisplayOverrideThisAttempt;

	private Panel panelScreen;

	private TableLayoutPanel tableScreen;

	private RButton buttonScreenAuto;

	private RButton button60Hz;

	private Panel panelKeyboard;

	private TableLayoutPanel tableLayoutKeyboard;

	private RComboBox comboKeyboard;

	private RButton button120Hz;


	private Label labelTipGPU;

	private Label labelTipScreen;

	private RButton buttonMiniled;

	private RColorButton buttonKeyboardColor;

	private RSlider sliderBattery;

	private Panel panelGPUTitle;

	private PictureBox pictureGPU;

	private ToolTip toolTip;

	private Label labelGPU;

	private Label labelGPUFan;

	private Panel panelCPUTitle;

	private PictureBox picturePerf;

	private Label labelPerf;

	private Panel panelScreenTitle;

	private Label labelMidFan;

	private PictureBox pictureScreen;

	private Label labelSreen;

	private Panel panelKeyboardTitle;

	private PictureBox pictureKeyboard;

	private Label labelKeyboard;

	private Panel panelBatteryTitle;

	private Label labelBattery;

	private PictureBox pictureBattery;

	private Label labelBatteryTitle;

	private Panel panelStartup;

	private RButton buttonStopGPU;

	private TableLayoutPanel tableButtons;

	private Panel panelPeripherals;

	private TableLayoutPanel tableLayoutPeripherals;

	private RButton buttonPeripheral2;

	private RButton buttonPeripheral3;

	private RButton buttonPeripheral1;

	private RButton buttonKeyboard;

	private RButton buttonUpdates;

	private Label labelCharge;

	private RButton buttonFnLock;

	private RButton buttonBatteryFull;

	private TableLayoutPanel tableAMD;

	private RButton buttonOverlay;

	private RButton buttonFHD;

	private Label labelBacklight;

	private BufferedPanel panelVersion;

	private Label labelVersion;

	private RBadgeButton buttonDonate;

	private RButton buttonEnergySaver;

	private RButton buttonAmdOled;

	private RButton buttonHDRControl;

	internal static readonly string KeyboardControllerUnsupportedText = Strings.KeyboardControllerUnsupported;

	internal static readonly (string Label, int Mins, int IdleSeconds)[] LightingCloseTimerOptions = new (string, int, int)[9]
	{
		(Strings.Off, 0, 0),
		(Strings.IdleOff10Sec, 0, 10),
		(Strings.IdleOff10Min, 10, 600),
		(Strings.IdleOff15Min, 15, 900),
		(Strings.IdleOff20Min, 20, 1200),
		(Strings.IdleOff30Min, 30, 1800),
		(Strings.IdleOff45Min, 45, 2700),
		(Strings.IdleOff1Hour, 60, 3600),
		(Strings.IdleOff2Hour, 120, 7200)
	};

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主批准「灯光自动关闭时间」· 宝龙达软件实现）：
	/// 选项 = 关（默认）/ 10 秒 / 5 分钟 / 10 分钟 / 30 分钟；**配置统一按秒存**（`light_autooff_seconds`）✅。
	/// <para>⚠️ 只有宝龙达显示本设置行；其它机型逐位不变 ✅。</para>
	/// </summary>
	internal static readonly (string Label, int Seconds)[] KeyboardAutoOffOptions = new (string, int)[5]
	{
		(Strings.Off, 0),
		(Strings.IdleOff10Sec, 10),
		(Strings.IdleOff5Min, 300),
		(Strings.IdleOff10Min, 600),
		(Strings.IdleOff30Min, 1800)
	};

	internal bool IsReadOnlyDegraded { get; private set; }

	private string? _lastBatteryTip;

	internal readonly record struct BatteryDisplayInput(PowerInputSample? Power, bool AcOnline, BatteryStatusReading? Status, decimal? RateWatts, int Percent, int Limit, bool Abnormal, int CycleCount, string? CapacityText);

	// Cached bold font for the tray context menu (per-mode highlight). Reusing the same
	// font instance instead of creating one per rebuild avoids GDI font-handle leaks.
	private Font? _trayBoldFont;
	private Font TrayBoldFont()
	{
		Font font = contextMenuStrip.Font;
		if (_trayBoldFont == null || _trayBoldFont.Size != font.Size || _trayBoldFont.Name != font.Name)
		{
			_trayBoldFont?.Dispose();
			_trayBoldFont = new Font(font, FontStyle.Bold);
		}
		return _trayBoldFont;
	}

	internal string GcuStatusTooltip
	{
		get
		{
			if (_labelGcuStatus != null)
			{
				return toolTip.GetToolTip(_labelGcuStatus);
			}
			return "";
		}
	}

	internal string ChargeLimitSliderReason => toolTip.GetToolTip(sliderBattery);

	internal bool MiniledOffered { get; private set; }

	internal int DashboardPageCount => 1;

	private static IEnumerable<Control> AllDescendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control item in AllDescendants(child))
			{
				yield return item;
			}
		}
	}

	private BufferedTableLayoutPanel BuildHeadRow(Control iconBox, Label nameLabel, Control? statusControl, Control? statusControl2 = null, Control? statusControl3 = null, Control? extraControl = null)
	{
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = false,
			ColumnCount = ((iconBox == null) ? 2 : 3) + ((statusControl2 != null) ? 1 : 0) + ((statusControl3 != null) ? 1 : 0) + ((extraControl != null) ? 1 : 0),
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		int num = 0;
		if (iconBox != null)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(26)));
			iconBox.Dock = DockStyle.Fill;
			iconBox.Margin = Padding.Empty;
			bufferedTableLayoutPanel.Controls.Add(iconBox, num++, 0);
		}
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		nameLabel.Dock = DockStyle.Fill;
		nameLabel.AutoSize = false;
		nameLabel.Margin = Padding.Empty;
		nameLabel.TextAlign = ContentAlignment.MiddleLeft;
		bufferedTableLayoutPanel.Controls.Add(nameLabel, num++, 0);
		if (statusControl3 != null)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			statusControl3.Margin = new Padding(0, 0, D(4), 0);
			statusControl3.Dock = (statusControl3 is CheckBox) ? DockStyle.None : DockStyle.Fill;
			statusControl3.Anchor = (statusControl3 is CheckBox) ? AnchorStyles.Left : AnchorStyles.None;
			if (statusControl3 is CheckBox checkBox3)
			{
				checkBox3.BackColor = bufferedTableLayoutPanel.BackColor;
			}
			bufferedTableLayoutPanel.Controls.Add(statusControl3, num++, 0);
		}
		// 2026-10-04：额外按钮列 —— 视觉上落在 statusControl3 与 statusControl2 之间
		// （即「显卡OC」与「风扇强冷」之间；30 系显卡OC 恒不显示）
		if (extraControl != null)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			extraControl.Margin = new Padding(0, 0, D(4), 0);
			extraControl.Dock = DockStyle.None;
			extraControl.Anchor = AnchorStyles.None;
			bufferedTableLayoutPanel.Controls.Add(extraControl, num++, 0);
		}
		if (statusControl2 != null)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			statusControl2.Margin = new Padding(0, 0, D(4), 0);
			statusControl2.Dock = (statusControl2 is CheckBox) ? DockStyle.None : DockStyle.Fill;
			statusControl2.Anchor = (statusControl2 is CheckBox) ? AnchorStyles.Left : AnchorStyles.None;
			if (statusControl2 is CheckBox checkBox)
			{
				checkBox.BackColor = bufferedTableLayoutPanel.BackColor;
			}
			bufferedTableLayoutPanel.Controls.Add(statusControl2, num++, 0);
		}
		if (statusControl != null)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			statusControl.Dock = DockStyle.Fill;
			statusControl.Margin = new Padding(D(6), 0, 0, 0);
			if (statusControl is Label label)
			{
				label.TextAlign = ContentAlignment.MiddleRight;
			}
			else if (statusControl is CheckBox checkBox)
			{
				checkBox.TextAlign = ContentAlignment.MiddleRight;
			}
			bufferedTableLayoutPanel.Controls.Add(statusControl, num++, 0);
		}
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		return bufferedTableLayoutPanel;
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
	}

	private PictureBox BuildHeadIcon(UiGlyph.Kind kind)
	{
		PictureBox pictureBox = new PictureBox
		{
			Name = "headIcon_" + kind,
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = UiVisualStyle.Window,
			
		};
		UiVisualStyle.ApplyGlyph(pictureBox, kind);
		return pictureBox;
	}

	private void ApplyRowSkins()
	{
		Control[] dashboardSections = _dashboardSections;
		foreach (Control obj in dashboardSections)
		{
			UiVisualStyle.ApplyRow(obj);
			foreach (Control item in AllDescendants(obj))
			{
				if (item is Panel)
				{
					if (item is BufferedPanel bufferedPanel && !(bufferedPanel is RCollapseGroup))
					{
						bufferedPanel.CardStyle = false;
					}
					UiVisualStyle.ApplyRow(item);
				}
			}
		}
		if (_dashboardScroll != null)
		{
			UiVisualStyle.ApplyRow(_dashboardScroll);
		}
		if (_dashboardScrollBar != null)
		{
			_dashboardScrollBar.BackColor = UiVisualStyle.Window;
		}
		UiVisualStyle.ApplyRow(panelFooter);
	}

	internal void ApplyThemeMode(bool night)
	{
		if (Program.UiAuditMode)
		{
			UiVisualStyle.SetAuditNightMode(night);
		}
		else
		{
			UiVisualStyle.SetNightMode(night);
		}
		InitTheme();
		InitContextMenuTheme();
		if (_dashboard != null)
		{
			_dashboard.BackColor = UiVisualStyle.Window;
		}
		if (_dashboardStack != null)
		{
			_dashboardStack.BackColor = UiVisualStyle.Window;
		}
		if (_liquidGroup != null)
		{
			_liquidGroup.BackColor = UiVisualStyle.Window;
		}
		if (_quickGroup != null)
		{
			_quickGroup.BackColor = UiVisualStyle.Window;
		}
		UiVisualStyle.ApplyWindow(this);
		// ⭐⭐ 2026-10-18（机主：主题适配）：⭐ 电池页充电阈值面板自绘/自取色，ApplyWindow 不会替它刷色
		//   ⇒ 主题切换后显式重刷（浅/深色都走 UiVisualStyle 同一套语义）✅
		foreach (Control themedControl in Controls.Find(BatteryLimitPanel.PanelControlName, searchAllChildren: true))
		{
			if (themedControl is BatteryLimitPanel batteryLimitPanel)
			{
				batteryLimitPanel.ApplyTheme();
			}
		}
		(string, UiGlyph.Kind)[] array = new (string, UiGlyph.Kind)[2]
		{
			("labelThemeModeTitle", UiGlyph.Kind.Contrast),
			("labelOfficialConsoleTitle", UiGlyph.Kind.Console)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (key, kind) = array[i];
			if (Controls.Find(key, searchAllChildren: true).FirstOrDefault() is Label label)
			{
				UiVisualStyle.ApplyGlyph(label, kind);
			}
		}
		UiVisualStyle.ApplyGlyph(labelVersion, UiGlyph.Kind.Info);
		picturePerf.Image = UiGlyph.Render(UiGlyph.Kind.Gauge, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		picturePerf.Visible = true;
		pictureGPU.Image = UiGlyph.Render(UiGlyph.Kind.VideoCard, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		pictureGPU.Visible = true;
		pictureBattery.Image = UiGlyph.Render(UiGlyph.Kind.Battery, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		pictureBattery.Visible = true;
		pictureScreen.Visible = false;
		ApplyRowSkins();
		RefreshThemeButtons();
		// 2026-10-08：模式高亮色是日/夜两套写死的 Color，换主题后必须重新下发一次。
		ApplyPerfModeAccents();
		RefreshBatteryModeButtons();
		Form[] ownedForms = OwnedForms;
		foreach (Form form in ownedForms)
		{
			if (form is RForm rForm && !form.IsDisposed)
			{
				rForm.InitTheme();
				UiVisualStyle.ApplyWindow(rForm);
				UiVisualStyle.RetintChrome(rForm);
			}
		}
		UpdateDashboardWindowHeight();
		Invalidate(invalidateChildren: true);
		Update();
	}

	/// <summary>
	/// 2026-10-08（用户要求：性能模式高亮按模式上色）：把五个模式按钮的激活高亮色换成本模式专属色。
	/// 静音=绿 / 平衡=蓝 / 静音狂暴=浅红 / 高能狂暴（狂暴）=大红 / 自定义=浅蓝。
	/// 只影响「当前实际模式」那个按钮（谁 Activated 谁显色，见 VisualiseMode）；
	/// 未激活态、悬停态一律未动；「待生效（重启后生效）」的棕色语义只在 GPU 模式行
	/// （VisualiseGPUMode 的 ActivatedColor = Warn）使用，本次未被触碰。
	/// 日/夜两套色取自 UiVisualStyle（见其 NightModePalette / DayModePalette），
	/// 故主题切换后必须重新下发一次（ApplyThemeMode 已调用，否则会留着旧主题的色）。
	/// </summary>
	private void ApplyPerfModeAccents()
	{
		SetPerfModeAccent(buttonSilent, UiVisualStyle.ModeSilentAccent);
		SetPerfModeAccent(buttonBalanced, UiVisualStyle.ModeBalancedAccent);
		SetPerfModeAccent(buttonSilentTurbo, UiVisualStyle.ModeSilentTurboAccent);
		SetPerfModeAccent(buttonTurbo, UiVisualStyle.ModeTurboAccent);
		SetPerfModeAccent(buttonCustomMode, UiVisualStyle.ModeCustomAccent);
	}

	private static void SetPerfModeAccent(RButton button, UiVisualStyle.ModeAccent accent)
	{
		if (button == null)
		{
			return;
		}
		button.ActivatedColor = accent.Background;
		button.ActivatedTextColor = accent.Foreground;
		// 2026-10-09（UI 打磨②b，仅夜间）：这五个「性能模式」按钮的选中态改为「彩色描边(2px) + 深底」——
		// 模式色（静音绿/平衡蓝/静音狂暴浅红/高能狂暴大红/自定义浅蓝）改由描边承载，故上面的
		// ActivatedColor 仍是该模式色。日间 RButton.OnPaint 忽略本开关（IsNightMode 门控），仍是实心填充。
		// 只开在这五个按钮上：GPU 模式行「待生效＝棕」（VisualiseGPUMode 的 ActivatedColor = Warn）没开，
		// 语义与画法都不变。
		button.ActivatedOutline = true;
		// 2026-10-09（UI 反馈①，仅夜间）：**这一个标志就是"是不是性能模式行"的可靠判据**（不做任何文字匹配）——
		// true ⇒ 选中态用本按钮的模式色（静音绿/平衡蓝/静音狂暴浅红/高能狂暴大红/自定义浅蓝）画描边；
		// 其余所有行（显卡模式行非待生效/刷新率/电池档位/核显省电/MiniLED/FHD/HDR）都是 false ⇒ 强调蓝高亮
		//（2026-10-09 UI 反馈② 起；此前是中性灰 Muted 158）。
		// 另有一处置 true：VisualiseGPUMode 在"待生效＝重启后生效"时下发 Warn（棕）—— 那是另一种语义，保持棕色。
		button.SelectionUsesAssignedAccent = true;
		// 2026-10-09（0.291 beta22 补丁；用户原话「除了性能模式都不要浅填充」）：**浅填充只给这五个按钮**。
		// 这个标志只在这里置 true（显卡行「待生效＝棕」用的是 SelectionUsesAssignedAccent，不含本标志）
		// ⇒ 非性能模式组（显卡行/刷新率/电池三档/核显省电/MiniLED·FHD·HDR）的选中态只有那圈描边、
		// 底＝与未选中同级的中性面，没有任何浅填充；性能模式五行才拿"模式色淡调浅填充"。
		// ⚠️ 判据是代码标志，不做任何文字/名称匹配；只改取色，不改画法/几何/事件。
		button.SelectionUsesModeFill = true;
	}

	private void RefreshThemeButtons()
	{
		bool isNightMode = UiVisualStyle.IsNightMode;

		SetThemeButtonState(_dayModeButton, !isNightMode);
		SetThemeButtonState(_nightModeButton, isNightMode);
		if (_themeFollowBox != null)
		{
			_themeFollowBox.Checked = UiVisualStyle.FollowSystemTheme;
		}
	}

	private static void SetThemeButtonState(Button? button, bool selected)
	{
		if (button != null)
		{
			// 2026-10-09（0.291 beta22 补丁；用户已拍板「界面主题的浅色/深色两颗 + 快捷睡眠 AC/DC …
			// 现在是普通 Button，两主题都是实心块 … 改」）：这两处由"实心强调色块"改成 **描边式** ——
			// 与 RButton 的「非性能模式组」**同一套规则**：底＝中性按钮面（两主题都一样，**不加浅填充**）、
			// 文字＝Text、选中只由那圈 2 设备像素描边表达（日间浅档 / 夜间全强度，见
			// UiVisualStyle.AccentSelectionOutline），未选中那圈取 ButtonEdge（日间也看得见）。
			// 圆角仍由 SettingsForm.ApplyRoundedRegion(scale(8)) 的 Control.Region 负责（未动）。
			// ⚠️ 只改这三个外观属性与日志文案，**点击行为/选项语义/AC-DC 读写一概未动**。
			Color outline = (selected ? UiVisualStyle.AccentSelectionOutline : UiVisualStyle.ButtonEdge);
			button.BackColor = UiVisualStyle.ButtonFace;
			button.ForeColor = UiVisualStyle.Text;
			button.FlatAppearance.BorderColor = outline;
			button.FlatAppearance.BorderSize = (selected ? 2 : 1);
			button.AccessibleDescription = (selected ? "Selected" : null);
			// 2026-10-09（0.291 beta22 补丁④；用户原话「插电离电高亮没了，然后默认显示插电时 给插电时高亮吧
			// （这个是老问题，以前就是两个都不高亮但是默认显示的是插电的情况)」）：**RButton 根本不看
			// `FlatAppearance`** —— 它自己整块自绘：可见的那圈边来自 `RButton.BorderColor`、选中态来自
			// `Activated + ActivatedOutline`。上一版这里只改 `FlatAppearance.BorderColor/BorderSize`，
			// 于是"快捷睡眠 AC/DC"这两颗 **RButton** 看起来**一模一样**（都只是中性面）＝ 用户报的
			// "插电离电高亮没了"。本段补上 RButton 自己的开关，走"非性能模式组"同一套规则：
			// **只描边 + 中性底、不加任何浅填充**（SelectionUsesModeFill=false），文字用 Text。
			if (button is RButton rButton)
			{
				rButton.Activated = selected;
				rButton.ActivatedOutline = true;
				rButton.SelectionUsesAssignedAccent = false;
				rButton.SelectionUsesModeFill = false;
				rButton.BorderColor = (selected ? Color.Transparent : UiVisualStyle.ButtonEdge);
				rButton.Invalidate();
			}
			// 2026-10-09（UI 打磨⑤）：日志的键由 raised= 改为 face= —— 这里下发的已经不是 SurfaceRaised 了，
			// 而是新的按钮面 ButtonFace（日间两者同值，夜间 44/60/86），日志如实反映实际生效的颜色。
			Logger.WriteLine($"SetThemeButtonState {button.Name} selected={selected} outline={outline} face={UiVisualStyle.ButtonFace} rbutton={(button is RButton)}");
		}
	}

	/// <summary>
	/// 2026-10-10（修用户反馈「禁用Windows更新开关打不开」）：快捷开关动作的返回值从 bool 换成
	/// 带明细的结果 —— 只有 winupdate 需要区分「必需项失败」与「可选项跳过」，其余开关沿用 bool 语义。
	/// </summary>
	private sealed class SwitchOutcome
	{
		public bool Ok { get; init; }

		/// <summary>失败时的用户可见明细（成功项数 + 失败项 + 跳过项）。</summary>
		public string Note { get; init; } = string.Empty;

		public static SwitchOutcome FromBool(bool ok)
		{
			return new SwitchOutcome { Ok = ok };
		}

		public static SwitchOutcome From(WindowsUpdateGuard.Result result)
		{
			return new SwitchOutcome { Ok = result.Ok, Note = result.UserDetail() };
		}
	}

	private static SwitchOutcome ApplyWindowsPersonalizationSwitch(string key, bool requested)
	{
		return key switch
		{
			"taskbarautohide" => SwitchOutcome.FromBool(ShellPersonalization.SetTaskbarAutoHide(requested)), 
			"transparency" => SwitchOutcome.FromBool(ShellPersonalization.SetTransparencyEnabled(requested)), 
			"darktheme" => SwitchOutcome.FromBool(ShellPersonalization.SetDarkTheme(requested)),
			// 2026-10-10：禁用 Windows 更新改为逐项执行 + 结果对象；失败明细回传给 UI 显性提示（不再静默拨回）。
			"winupdate" => SwitchOutcome.From(WindowsUpdateGuard.SetDisabled(requested)), 
			_ => SwitchOutcome.FromBool(false), 
		};
	}

	internal static bool? ReadWindowsPersonalizationSwitch(string key)
	{
		return key switch
		{
			"taskbarautohide" => ShellPersonalization.IsTaskbarAutoHide(), 
			"transparency" => ShellPersonalization.IsTransparencyEnabled(), 
			"darktheme" => ShellPersonalization.IsDarkTheme(),
			"winupdate" => WindowsUpdateGuard.IsDisabled(), 
			_ => null, 
		};
	}

	private void OpenLightForm(string topic, string title, (string Effect, string Name)[]? effects = null)
	{
		LightForm lightForm = (topic.Contains("Logo") ? (logoLightForm ?? (logoLightForm = new LightForm(topic, title, effects))) : (lightbarForm ?? (lightbarForm = new LightForm(topic, title, effects))));
		if (lightForm.IsDisposed)
		{
			lightForm = (topic.Contains("Logo") ? (logoLightForm = new LightForm(topic, title, effects)) : (lightbarForm = new LightForm(topic, title, effects)));
		}
		if (lightForm.Visible)
		{
			lightForm.Close();
			return;
		}
		AddOwnedForm(lightForm);
		ResponsiveLayout.ShowAdjacentTo(lightForm, this);
	}

	private void OpenRgbForm()
	{
		// ⭐⭐ 2026-10-19（运行时自校准）：机主打开**键盘灯设置窗口** ⇒ 排队一次亮度上限探测 ✅
		//   （后台线程、结果缓存；供 RgbForm 的亮度滑块范围使用）
		EnsureBaolongdaKeyboardCalibrationQueued();
		if (rgbForm == null || rgbForm.IsDisposed)
		{
			rgbForm = new RgbForm(Program.rgb);
			AddOwnedForm(rgbForm);
		}
		if (rgbForm.Visible)
		{
			rgbForm.Close();
		}
		else
		{
			ResponsiveLayout.ShowAdjacentTo(rgbForm, this);
		}
	}

	private void BuildQuickSwitchPanel()
	{
		if (Controls.ContainsKey("panelQuickSwitch"))
		{
			return;
		}
		BufferedPanel panel = new BufferedPanel
		{
			Name = "panelQuickSwitch",
			Dock = DockStyle.Top,
			CardStyle = true,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(16, 12, 16, 12)
		};
		TableLayoutPanel root = new TableLayoutPanel
		{
			Name = "quickSwitchRoot",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			GrowStyle = TableLayoutPanelGrowStyle.AddRows,
			ColumnCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = panel.BackColor
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.Controls.Add(root);
		Dictionary<string, FlowLayoutPanel> groupGrids = new Dictionary<string, FlowLayoutPanel>();
		_quickGroupContainers.Clear();
		int num = 0;
		(string, string[])[] quickSwitchGroups = QuickSwitchGroups;
		for (int i = 0; i < quickSwitchGroups.Length; i++)
		{
			(string, string[]) tuple = quickSwitchGroups[i];
			string item = tuple.Item1;
			string[] item2 = tuple.Item2;
			Label label = new Label
			{
				Name = "labelQuickGroup_" + item2[0],
				Text = item,
				Font = UiVisualStyle.Font(8.5f),
				ForeColor = UiVisualStyle.Muted,
				AutoSize = true,
				Margin = new Padding(0, (num != 1) ? 12 : 0, 0, 4)
			};
			Control header = label;
			if (item2[0] == "sleepnetoff")
			{
				TableLayoutPanel titleRow = new TableLayoutPanel
				{
					Name = "quickGroupTitle_sleep",
					ColumnCount = 3,
					RowCount = 1,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					Margin = Padding.Empty,
					Padding = Padding.Empty,
					BackColor = panel.BackColor
				};
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRow.Controls.Add(label, 0, 0);
				RButton btnAc = new RButton
				{
					Name = "btnSleepAc",
					Text = Strings.SleepAc,
					Size = new Size(56, 22),
					MinimumSize = new Size(56, 22),
					MaximumSize = new Size(56, 22),
					Padding = Padding.Empty,
					Margin = new Padding(6, 9, 2, 0),
					BackColor = UiVisualStyle.ButtonFace,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Anchor = AnchorStyles.None,
					Cursor = Cursors.Hand
				};
				RButton btnDc = new RButton
				{
					Name = "btnSleepDc",
					Text = Strings.SleepDc,
					Size = new Size(56, 22),
					MinimumSize = new Size(56, 22),
					MaximumSize = new Size(56, 22),
					Padding = Padding.Empty,
					Margin = new Padding(2, 9, 0, 0),
					BackColor = UiVisualStyle.ButtonFace,
					ForeColor = UiVisualStyle.Text,
					BorderColor = UiVisualStyle.Border,
					Anchor = AnchorStyles.None,
					Cursor = Cursors.Hand
				};
				titleRow.Controls.Add(btnAc, 1, 0);
				titleRow.Controls.Add(btnDc, 2, 0);
				btnAc.Click += (object? _, EventArgs _) => SetQuickSleepAc(ac: true);
				btnDc.Click += (object? _, EventArgs _) => SetQuickSleepAc(ac: false);
				// 2026-10-09（0.291 beta22 补丁④；用户原话「插电离电高亮没了 … 默认显示插电时 给插电时高亮吧」）：
				// 改前这里只写死 `SetThemeButtonState(btnAc, selected: true)`，**DC 那一颗从来没有被初始化过**
				//（这正是"两个都不高亮"的另一半原因），而且不看 `_quickSleepAc` 的**真实值**
				//（该字段是 static、跨窗口存活：上次点了"离电时"再打开设置窗，高亮还停在 AC ⇒ 与实际不符）。
				// 现在两颗都按真实值点灯：`_quickSleepAc == true` ⇒ 「插电时」高亮（用户要的默认）。
				// ⚠️ 点击行为/读写语义（SetQuickSleepAc / PowerSleepGuard）一字未改。
				SetThemeButtonState(btnAc, _quickSleepAc);
				SetThemeButtonState(btnDc, !_quickSleepAc);
				_quickSleepAcBtn = btnAc;
				_quickSleepDcBtn = btnDc;
				header = titleRow;
				root.Controls.Add(titleRow, 0, num++);
			}
			else
			{
				root.Controls.Add(label, 0, num++);
			}
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Name = "quickGroup_" + item2[0],
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = true,
				AutoScroll = false,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = panel.BackColor
			};
			root.Controls.Add(flowLayoutPanel, 0, num++);
			_quickGroupContainers[item] = (header, flowLayoutPanel);
			string[] array = item2;
			foreach (string key in array)
			{
				groupGrids[key] = flowLayoutPanel;
			}
		}
		(string, string)[] array2 = QuickSwitchLabels;
		_usbChargerBox = null;
		_fanBoostBox = null;
		List<CheckBox> list = new List<CheckBox>();
		(string, string)[] array3 = array2;
		for (int i = 0; i < array3.Length; i++)
		{
			(string, string) tuple2 = array3[i];
			string key2 = tuple2.Item1;
			string item3 = string.Equals(key2, "webcam", StringComparison.OrdinalIgnoreCase)
				? QuickCameraLabel(tuple2.Item2)
				: tuple2.Item2;
			RCheckBox cb = new RCheckBox
			{
				Name = "quick_" + key2,
				Text = item3,
				Tag = key2,
				ForeColor = UiVisualStyle.Text,
				Cursor = Cursors.Hand,
				AutoSize = true,
				Margin = new Padding(0, 4, 8, 4)
			};
			bool? flag = ReadWindowsPersonalizationSwitch(key2);
			if (flag.HasValue)
			{
				cb.Checked = flag.Value;
			}
			if (key2 == "usb")
			{
				_usbChargerBox = cb;
				cb.CheckedChanged += async (object? _, EventArgs _) =>
				{
					if (!_syncingSwitches)
					{
						_lastQuickSwitchUi = DateTime.Now;
						if (Program.service != null)
						{
							MechrevoHw hw = Program.hw;
							if (hw != null && hw.IsConnected)
							{
								await ApplyQuickSwitchAsync(cb, Program.service.SwitchUsbCharger);
							}
						}
					}
				};
			}
			else if (key2 == "fanboost")
			{
				_fanBoostBox = cb;
				// ⭐ 2026-10-19（强冷核对 ②）：**建控件时**就按真实状态给勾选态（宝龙达没有 GCU ⇒
				//   状态在 `FanBoostActive`）。⚠️ 必须在 `CheckedChanged +=` **之前**赋值，
				//   否则会被当成一次用户点击触发下发（⛔ 启动时不该写风扇）✅
				MechrevoHw hwFanBoostInit = Program.hw;
				cb.Checked = ((hwFanBoostInit != null && hwFanBoostInit.UsesBaolongda)
					? MechrevoHw.FanBoostActive
					: (hwFanBoostInit?.FanBoost ?? false));
				cb.CheckedChanged += async (object? _, EventArgs _) =>
				{
					if (!_syncingSwitches)
					{
						_lastQuickSwitchUi = DateTime.Now;
						// ⭐⭐ 2026-10-18（机主 UI 体验 · 乐观更新）：⭐ 点击后**立刻**保持选中态（不等硬件确认），
						//   ⏱ 12 s 宽限内不被定时器拨回；⭐ 硬件仍严格校验，最终失败才回滚 ✅
						_fanBoostOptimisticUntil = DateTime.Now.AddSeconds(12);
						_fanBoostOptimisticValue = cb.Checked;
						Logger.WriteLine("[fan] 强冷：UI 先乐观置" + (cb.Checked ? "开" : "关") + "（等待确认）");
						if (Program.service != null)
						{
							MechrevoHw hw = Program.hw;
							if (hw != null && hw.IsConnected)
							{
								// ⭐⭐ 2026-10-19（机主：「风扇强冷不可点击」）：⭐ 这里原来**直接**调
								//   `SwitchFanBoost`（GCU/MQTT 专用）⇒ 宝龙达上必然失败，表现为"点了没反应" ❌
								//   ⭐ 改走统一入口 `SwitchQuick("fanboost", on)`：宝龙达分支会走本机通道
								//      （暂停曲线闭环 + 功能号 21 满转 + 13 回读）✅
								//   ⚠️ GCU 机型：`SwitchQuick` 内部对 fanboost 的判据与原来等价（同样发 MQTT）✅
								await ApplyQuickSwitchAsync(cb, (bool on) => Program.service.SwitchQuick("fanboost", on));
							}
						}
					}
				};
			}
			else if (key2 == "deepsleep")
			{
				int num2 = (_deepSleepPendingValue = AppConfig.Get("deepsleep_pending"));
				cb.Checked = ((num2 >= 0) ? (num2 == 1) : (ReadQuickSwitchState(key2) == true));
				// 2026-10-11（磁贴行 · 同源）：本键的动作体抽成 ApplyDeepSleepSwitchAsync ——
				// 这里（「更多开关」的 CheckBox.Click）与磁贴点击**调用同一个方法**，
				// ⛔ 不是两份实现（见 ToggleQuickTileAsync）。
				string deepSleepKey = key2;
				cb.Click += async (object sender, EventArgs e) =>
				{
					await ApplyDeepSleepSwitchAsync(cb, deepSleepKey);
				};
			}
			else if (key2 == "monitoroff")
			{
				// 2026-10-11（重构）：息屏改为「虚拟显示器」方案（参考 NullSignal：挂载 usbmmidd_v2 虚拟屏 →
				// displayswitch /external 强切 → 物理屏熄灭、系统不睡眠 → 移动鼠标恢复）。
				// 为什么换：S0 现代待机上 SC_MONITORPOWER(2) 会被系统当成「进入新型待机」请求（事件 506），
				// 旧方案无论背光 0% 还是关屏指令都无法做到"真息屏不睡眠"。
				// 一次性语义不变：点完由 finally 取消勾选；勾选态至少显示 MonitorOffSwitchVisibleMs 再回落。
				// 启动失败（驱动组件缺失等）由 VirtualDisplayBlank.Start() 同步返回原因 → 弹窗告知。
				// 2026-10-11（磁贴行 · 同源）：动作体同样抽成 ApplyQuickMonitorOffAsync，磁贴点击复用同一份。
				cb.Click += async (object sender, EventArgs e) =>
				{
					await ApplyQuickMonitorOffAsync(cb);
				};
			}
			else
			{
				bool flag2;
				switch (key2)
				{
				case "taskbarautohide":
				case "transparency":
				case "darktheme":
				case "winupdate":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				if (flag2)
				{
					string windowsKey = key2;
					// 2026-10-10（修用户反馈「打开或者关闭会卡一会」）：整条动作链（net stop / sc config /
					// schtasks / 注册表写）原先在 UI 线程上同步跑，拨动时界面会冻住一会儿。
					// 现在 winupdate 一项改成 await Task.Run(...) 在后台线程执行（这些进程调用与注册表写
					// 在后台线程上安全）；结果 await 回来后自然落在 UI 线程，Checked / _lastQuickSwitchUi /
					// Toast 一律在 UI 线程上设置。async void 是事件回调，整段包在 try/catch 里，异常不逃逸。
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (_syncingSwitches)
						{
							return;
						}
						bool flag4 = cb.Checked;
						bool isWinUpdate = windowsKey == "winupdate";
						if (isWinUpdate && _winupdateBusy)
						{
							// 重入策略：动作进行中再次拨动 → 忽略这一次（丢弃），把开关回落到在途目标；
							// 绝不让两个动作并发改系统。_syncingSwitches 挡住这次写入触发的递归。
							Logger.WriteLine($"Quick switch winupdate ignored: busy (requested={flag4})");
							_syncingSwitches = true;
							try
							{
								cb.Checked = _winupdateBusyTarget;
							}
							finally
							{
								_syncingSwitches = false;
							}
							return;
						}
						if (cb.Focused)
						{
							Control parent = cb.Parent;
							if (parent != null && parent.CanFocus)
							{
								parent.Focus();
							}
						}
						if (isWinUpdate)
						{
							_winupdateBusy = true;
							_winupdateBusyTarget = flag4;
							// 2026-10-10：winupdate 分支此前不打这个标记，定时器会在动作途中用 IsDisabled 覆盖界面。
							_lastQuickSwitchUi = DateTime.Now;
						}
						cb.Enabled = false;
						// 2026-10-12（修「第三个磁贴点不动」）：与本处 finally 里的解除**成对**（个人化开关也能当磁贴）。
						SetQuickSwitchInFlight(cb, inFlight: true);
						// 忙碌反馈（不引入新控件、不改布局）：winupdate 等待期间指针变等待光标 + 本开关自身禁用，
						// 表单其余部分仍可拖动/点击；动作结束（含失败）在 finally 里整体恢复。
						if (isWinUpdate)
						{
							UseWaitCursor = true;
						}
						try
						{
							SwitchOutcome switchOutcome;
							try
							{
								if (isWinUpdate)
								{
									switchOutcome = await Task.Run(delegate
									{
										return ApplyWindowsPersonalizationSwitch(windowsKey, flag4);
									});
								}
								else
								{
									switchOutcome = ApplyWindowsPersonalizationSwitch(windowsKey, flag4);
								}
							}
							catch (Exception ex)
							{
								// 后台动作抛出的任何异常都当成一次失败，走下面的显性报错，不逃出 async void。
								Logger.WriteLine($"Quick switch {windowsKey} -> {flag4} threw: " + ex.Message);
								switchOutcome = new SwitchOutcome { Ok = false, Note = ex.Message };
							}
							if (!switchOutcome.Ok)
							{
								// 2026-10-10（修用户反馈「禁用Windows更新开关打不开」）：原来这里
								// cb.Checked = !flag4 静默拨回，用户只看到「打不开」而不知道原因。
								// 现改为显性报错（列出成功/失败项），并先记 _lastQuickSwitchUi，
								// 免得 UpdateQuickSwitches 立刻把界面覆盖回去。
								_lastQuickSwitchUi = DateTime.Now;
								string switchDetail = ((switchOutcome.Note.Length > 0) ? switchOutcome.Note : Strings.QuickSwitchFailed);
								Logger.WriteLine($"Quick switch {windowsKey} -> {flag4} failed: {switchDetail}");
								ToastForm.ShowFailure(Strings.QuickWindowsUpdate + "：" + switchDetail);
								// 显性报错之后再按实际状态回落，且用 _syncingSwitches 挡住这一次写入触发的递归。
								_syncingSwitches = true;
								try
								{
									cb.Checked = ReadWindowsPersonalizationSwitch(windowsKey) ?? (!flag4);
								}
								finally
								{
									_syncingSwitches = false;
								}
							}
						}
						catch (Exception ex)
						{
							// 兜底：任何未预料的异常都不许逃出这个 async void 回调。
							Logger.WriteLine($"Quick switch {windowsKey} -> {flag4} unhandled: " + ex.Message);
						}
						finally
						{
							// 兜底恢复：窗口若在等待期间被关闭（控件已释放），这里同样不许把异常抛出去。
							try
							{
								if (isWinUpdate)
								{
									_winupdateBusy = false;
									// 动作结束后再给 12s 宽限，免得定时器立刻把刚写好的状态覆盖掉。
									_lastQuickSwitchUi = DateTime.Now;
								}
								if (!IsDisposed && !Disposing)
								{
									if (isWinUpdate)
									{
										UseWaitCursor = false;
									}
									if (!cb.IsDisposed)
									{
										cb.Enabled = true;
									}
								}
								SetQuickSwitchInFlight(cb, inFlight: false);
							}
							catch (Exception ex)
							{
								Logger.WriteLine($"Quick switch {windowsKey} -> {flag4} restore failed: " + ex.Message);
							}
						}
					};
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}
				if (key2.StartsWith("sleep"))
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							bool requested = cb.Checked;
							_lastQuickSwitchUi = DateTime.Now;
							// 2026-10-09（修「快捷开关设置失败」）：睡眠省电不再直接写 PowerSchemes 注册表（被系统拒绝），
							// 改走官方电源 API（PowerWriteAC/DCValueIndex，由 Power 服务代执行）+ 回读校验；
							// 失败给专用提示（设置名 + 插电/离电 + 原因），不再笼统提示「快捷开关设置失败」。
							if (!PowerSleepGuard.TrySetEnabled(key2, requested, _quickSleepAc, out string sleepError))
							{
								ToastForm.ShowFailure((sleepError.Length > 0) ? sleepError : Strings.QuickSwitchFailed);
								RevertCheck(cb, PowerSleepGuard.ReadEnabled(key2, _quickSleepAc) ?? (!requested));
							}
						}
					};
					string sleepTip = SleepTipFor(key2);
					if (sleepTip.Length > 0)
					{
						_quickSleepToolTip.SetToolTip(cb, sleepTip);
					}
					list.Add(cb);
					GroupOf(key2).Controls.Add(cb);
					continue;
				}
				string text = key2;
				if (key2 == "micmute")
				{
					// 2026-10-11（用户答复①「要它，另开一轮做麦克风静音开关」）：走**专用**下发路径
					// MechrevoService.SwitchMicMute（Windows CoreAudio 端点静音），⛔ 不经过
					// SwitchQuick 的 EC/MQTT 通道。
					// ⚠️ **不要求 hw.IsConnected**：麦克风静音是 Windows 侧功能（默认录音端点），
					//    与 GCU/EC 连接无关；要求连接会让"控制台没连上"时这个开关点不动。
					//     能力位（本机有没有可静音的默认录音端点）由 hw.SupportsQuickSwitch("micmute")
					//     在 RefreshDeviceCapabilities 里单独下发可见性，缺录音设备时**整条隐藏**。
					_quickSleepToolTip.SetToolTip(cb, Strings.QuickMicMuteTip);
					// ⚠️ 有意**不写可空注解**（`object? _`）：本文件没有 #nullable 上下文，写注解只会多一条
					//    CS8632，而本仓库的警告预算是硬上限（1448）；语义完全等价（这两个参数都不使用）。
					cb.CheckedChanged += async (object sender, EventArgs e) =>
					{
						if (!_syncingSwitches)
						{
							_lastQuickSwitchUi = DateTime.Now;
							MechrevoService service = Program.service;
							if (service != null)
							{
								await ApplyQuickSwitchAsync(cb, service.SwitchMicMute);
							}
						}
					};
				}
				else if ((text == "gamewhitelist" || text == "cpuadvperf") ? true : false)
				{
					if (key2 == "cpuadvperf")
					{
						_quickSleepToolTip.SetToolTip(cb, Strings.QuickCpuAdvPerfTip);
					}
					bool whitelist = key2 == "gamewhitelist";
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							_lastQuickSwitchUi = DateTime.Now;
							MechrevoService service = Program.service;
							if (service != null)
							{
								MechrevoHw hw = Program.hw;
								if (hw != null && hw.IsConnected)
								{
									await ApplyQuickSwitchAsync(cb, whitelist ? new Func<bool, Task<bool>>(service.SwitchGameWhitelist) : new Func<bool, Task<bool>>(service.SwitchCpuAdvancedPerformance));
								}
							}
						}
					};
				}
				else
				{
					cb.CheckedChanged += async (object? _, EventArgs _) =>
					{
						if (!_syncingSwitches)
						{
							_lastQuickSwitchUi = DateTime.Now;
							if (Program.service != null)
							{
								MechrevoHw hw = Program.hw;
								if (hw != null && hw.IsConnected)
								{
									await ApplyQuickSwitchAsync(cb, (bool requested) => Program.service.SwitchQuick(key2, requested));
								}
							}
						}
					};
				}
			}
			// 2026-10-11（用户拍板「「风扇强冷」开关移回「更多开关」里面」）：这一层 `key2 != "fanboost"`
			// 门控取消 —— 该开关此前建好后**从不**进任何快捷分组面板（它被贴到性能模式标题行右侧），
			// 本轮它按用户要求回到「更多开关」的「电源与系统」组；性能模式标题行不再持有它。
			GroupOf(key2).Controls.Add(cb);
			list.Add(cb);
		}
		_quickSwitches = list.ToArray();
		Logger.WriteLine($"[QS] groups={QuickSwitchGroups.Length} switches={_quickSwitches.Length} panelH={panel.Height} rootH={root.Height} rootPref={root.PreferredSize.Height}");
		bool fittingQuickGroups = false;
		_fitQuickCards = FitQuickGroups;
		panel.ClientSizeChanged += (object? _, EventArgs _) =>
		{
			FitQuickGroups();
		};
		foreach (FlowLayoutPanel item5 in groupGrids.Values.Distinct())
		{
			item5.Layout += (object? _, LayoutEventArgs _) =>
			{
				FitQuickGroups();
			};
		}
		FitQuickGroups();
		RCheckBox autoHzChk = new RCheckBox
		{
			Name = "checkAutoRefreshRate",
			Text = Strings.AutoRefresh,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			Checked = (Program.hw?.DcHz ?? false)
		};
		autoHzChk.CheckedChanged += (object? _, EventArgs _) =>
		{
			if (!_syncingDisplay)
			{
				bool requested = autoHzChk.Checked;
				// ⭐⭐ 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**，不再依赖 GCU）：
				// 「自动刷新率」的**唯一实现 = Win32** —— 写我们自己的 `screen_auto` 配置键，并**立刻**按
				// 当前电源状态应用一次（插电 ⇒ 最大档 / 电池 ⇒ MIN_RATE）。
				// ⚠️ 触发链（2026-10-15 用户拍板改成 ①「只认插拔 + 开机首次」）：
				//   **只在【真·插拔电源】与【开机首次】应用**（门控在 Program.SetAutoModes 尾部的 `if (init || flag)`）；
				//   ⛔ **唤醒 / 会话解锁 / 切换显卡模式都不再应用** —— 否则 auto 开着时，这些非用户动作会把
				//   用户手选的中间档（如 120Hz）顶回"插电=最大 / 电池=MIN_RATE"。
				ScreenControl.SetAutoRefresh(requested ? 1 : 0);
				if (requested)
				{
					ScreenControl.AutoScreen();
				}
				// ⭐⭐ 2026-10-15（用户拍板「根因治掉吧」）：**永不把"自动开"告诉 GCU** —— 改成**恒发 false**
				// 之前是 `SwitchAutoRefreshRate(requested)`：把"自动开着"告诉固件后，**固件自己的 `GPU_DC_HZ`**
				// 就按**它自己的时机**执行 —— 真机实证它在【息屏/显示恢复】时把用户手选的 100Hz 顶回 180Hz
				//（同一时刻我们的门控是关着的：`AutoSetting … changed=False` ⇒ 不是我们改的）。
				// 而我们控制不了固件的触发时机 ⇒ 干脆让它**保持关闭**；自动切换全部由我们的 Win32 实现负责。
				// best-effort、不 await、不参与成功判定（`SwitchAutoRefreshRate` 内部对 `!DcHzSeen` 会自行返回）。
				MechrevoHw hwAuto = Program.hw;
				bool autoMirrored = hwAuto != null && hwAuto.IsGcuConnected && Program.service != null;
				if (autoMirrored)
				{
					_ = Program.service.SwitchAutoRefreshRate(false);
				}
				Logger.WriteLineIfChanged("auto-hz-toggle", $"自动刷新率: requested={requested} → Win32 screen_auto={(requested ? 1 : 0)}（已按当前电源态应用）· 固件侧恒送关闭={autoMirrored}");
			}
		};
		_autoHzChk = autoHzChk;
		_hzSegTable = new BufferedTableLayoutPanel
		{
			Name = "tableHzButtons",
			Dock = DockStyle.Fill,
			ColumnCount = 0,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		BufferedPanel bufferedPanel = new BufferedPanel
		{
			Name = "panelBrightness",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Width = 414,
			BackColor = UiVisualStyle.Window
		};
		bufferedPanel.Padding = new Padding(ResponsiveLayout.LogicalToDevice(this, 16), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 12), 0);
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 3,
			RowCount = 5,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = bufferedPanel.BackColor
		};
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, BrightnessColumnPercentages.Label));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, BrightnessColumnPercentages.Slider));
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, BrightnessColumnPercentages.Value));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		_screenRowsLayout = bufferedTableLayoutPanel;
		bufferedPanel.Controls.Add(bufferedTableLayoutPanel);
		Label control = new Label
		{
			Name = "labelScreenBrightness",
			Text = Strings.Brightness,
			Font = UiStyleBody(),
			ForeColor = UiStyleXXX(),
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			BackColor = bufferedPanel.BackColor
		};
		RSlider brightSlider = new RSlider
		{
			Name = "sliderScreenBrightness",
			// 2026-10-12（用户报 bug ① 原话「发现亮度滑块最小是10 而不是0」）：量程放开到 0。
			// 写入走的是 **Windows WMI**（WmiMonitorBrightnessMethods.WmiSetBrightness，uint8 0–100，
			// 见 Display\ScreenBrightness.cs），0 是契约内的合法值；产品自己的亮度热键路径
			// ScreenBrightness.Adjust 本来就夹在 Math.Max(0, …) ⇒ 这里写死 10 才是**不一致**的那一处。
			Minimum = 0,
			Maximum = 100,
			Dock = DockStyle.Fill,
			
		};
		Label brightPercent = new Label
		{
			Name = "labelScreenBrightnessValue",
			Text = "100%",
			ForeColor = UiVisualStyle.Muted,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			Font = UiStyleCaptionFont(),
			MinimumSize = new Size(44, 0),
			Margin = Padding.Empty,
			BackColor = bufferedPanel.BackColor
		};
		// ⭐⭐ 2026-10-19（机主：启动时亮度滑块没读系统真值）：提到字段，供「只读同步」复用 ✅
		_brightnessSlider = brightSlider;
		_brightnessValueLabel = brightPercent;
		brightSlider.ValueChanged += (object? _, EventArgs _) =>
		{
			int value = brightSlider.Value;
			brightPercent.Text = value + "%";
			if (!_syncingDisplay)
			{
				_lastBrightnessUi = DateTime.UtcNow;
				if (Program.hw != null && !Program.UiAuditMode)
				{
					RequestBrightnessWrite(value);
				}
			}
		};
		brightSlider.KeyUp += (object? _, KeyEventArgs _) =>
		{
			// ⭐⭐ 2026-10-19（机主：亮度调节有延迟）：松手/按键结束 ⇒ **补一次最终值**（立即写，不等防抖）
			_brightnessLastThrottleFlushTick = 0;
			_brightnessCommitQueue.Flush();
		};
		brightSlider.MouseUp += (object _, MouseEventArgs _) =>
		{
			// ⭐ 鼠标拖动松手同上（⛔ 不改滑块自身行为，只是把最终值立即补写一次）✅
			_brightnessLastThrottleFlushTick = 0;
			_brightnessCommitQueue.Flush();
		};
		brightSlider.MouseCaptureChanged += (object _, EventArgs _) =>
		{
			// ⭐ 在滑块外松手（捕获丢失）也算"松手"：补最终值 ✅
			if (!brightSlider.Capture)
			{
				_brightnessLastThrottleFlushTick = 0;
				_brightnessCommitQueue.Flush();
			}
		};
		Label label2 = new Label
		{
			Name = "labelScreenRow",
			Text = Strings.Screen,
			Dock = DockStyle.Fill,
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiVisualStyle.Text,
			BackColor = bufferedPanel.BackColor,
			
		};
		UiVisualStyle.ApplyTitle(label2);
		RComboBox calibCombo = new MechrevoLite.UI.RComboBox
		{
			Cursor = Cursors.Hand,
			DropDownStyle = ComboBoxStyle.DropDownList,
			DisplayMember = "Key",
			FlatStyle = FlatStyle.Flat,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleBody(),
			Anchor = (AnchorStyles.Left | AnchorStyles.Right),
			Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 8), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 8), ResponsiveLayout.LogicalToDevice(this, 2))
		};
		RebuildCalibrationItems(calibCombo);
		RCheckBox calibSwitch = new RCheckBox
		{
			AutoSize = true,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Margin = Padding.Empty,
			Anchor = AnchorStyles.Left
		};
		int initialCalibMode = ResolveColorCalibrationMode();
		// 2026-10-09（默认值修正）：calib_enabled 键缺失 = 新装 / 全新配置 ⇒ 开关默认「关」。
		// 旧写法（键缺失时用 initialCalibMode > 0）把"厂商控制台本来就校准过"（本机注册表
		// HKLM\SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures\ColorCalibration=1，
		// 经 MechrevoService.GetColorCalibrationMode 读成 1）当成了 LM 自己的默认值，于是
		// 全新配置一打开就显示「屏幕校色 = 开」。用户一旦拨动开关，calib_enabled 即落盘，
		// 此后一律以保存值为准 —— 老用户已保存的值（含 0）不受本改动影响。
		calibSwitch.Checked = AppConfig.Exists("calib_enabled") && AppConfig.Is("calib_enabled");
		calibCombo.Enabled = calibSwitch.Checked;
		calibCombo.SelectedIndex = CalibComboIndexFor(initialCalibMode, calibCombo);
		calibSwitch.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingDisplay || Program.UiAuditMode)
			{
				return;
			}
			MechrevoService? svc = Program.service;
			MechrevoHw hw = Program.hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：本段是 GCU 校色 ICC 绕行（下发全是 MQTT）⇒ 真正需要 GCU。
			// 2026-10-16【更正】上面这句原来紧贴在 `if (hw.IsGcuConnected)` 之前、读起来像在说
			// "整个屏幕校色都需要 GCU"，容易把整段判死 —— 实际它只覆盖**这一颗开关**（以及下面
			// 走 SetColorCalibration 的档位切换）。校色分两半：
			//   * 「自定义 ICC」档（下拉选 999）—— 纯本地：IccHelper.ApplyIcc →
			//     WcsAssociateColorProfileWithDevice（Windows 标准 API / mscms.dll），**不需要 GCU**；
			//   * 预设档位（默认/sRGB/P3/AdobeRGB）与这颗开关 —— 走 GCU 的 SetColorCalibration / MQTT。
			// 所以宝龙达（非 GCU）下的处理是：这颗开关隐藏（本段走不到 ⇒ 点了不会有反应，
			// 留着就违反"能点的就必须有反应"），而「设置」按钮与下拉的「自定义」档**保留**
			// （判据见 flag11 / flag11GcuOnly / CustomIccTierAvailable）。
			if (svc != null && hw != null && hw.IsGcuConnected)
			{
				bool requested = calibSwitch.Checked;
				calibSwitch.Enabled = false;
				// 2026-10-10【默认启用】40 系 GCU「生成 ICC 但不关联」绕行：
				// 紧急关闭键 = config 键 calib_assoc_gcu_icc（**默认启用**；置 0 即整段停用 ⇒
				// 之后不拍快照、不读 ICC、不写注册表）。必须在清理/下发**之前**抓前置快照 ——
				// 后面的 ClearAllIcc 会清空关联槽，晚抓就抓不到"原来挂着的是哪个档"了。
				// ⭐ 2026-10-10【范围约束】tag 只用于日志：有回读能力的机器（Gen50+已上报档位）在此直接得 null ⇒ 整体跳过。
				GcuIccProfileAssociator.Snapshot calibIccBefore = await GcuIccProfileAssociator.CaptureAsync("开关");
				if (!requested && ((AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on")))
				{
					// 总开关关闭：清理已应用的自定义 ICC
					try
					{
						await Task.Run(() => IccHelper.ClearAllIcc());

					}
					catch (Exception ex)
					{
						Logger.WriteLine("关闭校色清理 ICC 异常: " + ex.Message);
					}
				}
				int value = requested ? (calibCombo.SelectedItem is KeyValuePair<string, int> kv2 ? kv2.Value : 1) : 0;
				bool flag4;
				bool showToast = false;
				try
				{
					Interlocked.Increment(ref _calibSetInFlight);
					if (requested && value == 999)
					{
						// 恢复自定义 ICC：不经 GCU，直接经 Windows 颜色管理应用
						string? iccName = AppConfig.GetString("calib_custom_icc");
						string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
						if (!string.IsNullOrEmpty(iccPath) && File.Exists(iccPath))
						{
							try
							{
								flag4 = await Task.Run(() => IccHelper.ApplyIcc(iccPath));
							}
							catch (Exception ex)
							{
								Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
								flag4 = false;
							}
						}
						else
						{
							// 自定义 ICC 文件缺失（如重装后被清理）：回退默认档，避免开关无法打开
							Logger.WriteLine("自定义 ICC 文件缺失，打开校色回退默认档: " + (iccName ?? "(空)"));
							value = 1;
							_syncingDisplay = true;
							calibCombo.SelectedIndex = CalibComboIndexFor(1, calibCombo);
							_syncingDisplay = false;
							flag4 = await svc.SetColorCalibration(1);
						}
					}
					else
					{
						// 开：档位取下拉（并做"不超过 GCU 上报档位数"的截断）；
						// 关：必须发 0（不校准）。此前无条件用下拉档位覆盖，等于把"关"也发成当前档位，
						// 二分设备（40 系 + sRGB 屏，下拉只有「默认」= 1）上表现为"点了没反应"（走 AlreadyApplied 静默返回）。
						value = (requested ? CalibrationModeForCombo(calibCombo) : 0);
						flag4 = await svc.SetColorCalibration(value);
					}
				}
				finally
				{
					Interlocked.Decrement(ref _calibSetInFlight);
					_calibSyncHoldUntil = DateTime.UtcNow.AddSeconds(8.0);
					calibSwitch.Enabled = true;
				}
				if (flag4)
				{
					// 2026-10-10【默认启用】「开」成功后：用前后比对认出 GCU 刚生成的 ICC，
					// 关联到内屏（只写当前设备那一个槽）+ GetICMProfile 读回验证 + 失败回退。
					// 紧急关闭键 calib_assoc_gcu_icc=0 时 CaptureAsync 返回 null ⇒ 此段不执行。
					if (requested && value != 999 && calibIccBefore != null)
					{
						// ⭐ 2026-10-10：这里只改"怎么等待"——不再 await，改即发即忘：
						// 事件处理立刻返回（界面不卡），关联在后台跑，结果/异常由
						// GcuIccProfileAssociator 自己落 [icc-assoc] 日志（含"关联结果(开关)"）。
						GcuIccProfileAssociator.ApplyAfterCalibrationOnDetached(calibIccBefore, "开关");
					}
					_lastCalibUi = DateTime.UtcNow;
					// 档位与开关分离：calib_mode 恒存用户档位（关闭时不覆盖，重启后下拉回显）；calib_enabled 存开关
					if (requested)
					{
						AppConfig.Set("calib_mode", value);
						AppConfig.Set("calib_enabled", 1);
					}
					else
					{
						AppConfig.Set("calib_enabled", 0);
					}
				}
				else
				{
					showToast = true;
					_syncingDisplay = true;
					calibSwitch.Checked = !requested;
					_syncingDisplay = false;
				}
				calibCombo.Enabled = calibSwitch.Checked;
				if (showToast)
				{
					// 失败不再静默：原来只写日志，用户只看到开关/档位自己跳回去
					Logger.WriteLine("校色开关应用失败: requested=" + requested + " mode=" + value);
					ToastForm.ShowFailure(Strings.ColorCalibrationFailed);
				}
			}
			else
			{
				_syncingDisplay = true;
				calibSwitch.Checked = !calibSwitch.Checked;
				_syncingDisplay = false;
			}
		};
		calibCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			bool showToast = false;
			if (_syncingDisplay || Program.UiAuditMode)
			{
				return;
			}
			if (calibCombo.SelectedItem is KeyValuePair<string, int> keyValuePair && Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					// 2026-10-10【默认启用】同「屏幕校色」开关处：切换档位也算"开"，
					// 前置快照仍须在清理/下发之前抓（紧急关闭键 =0 ⇒ 返回 null ⇒ 后面整段不执行）。
					// ⭐ 2026-10-10【范围约束】tag 只用于日志：有回读能力的机器（Gen50+已上报档位）在此直接得 null ⇒ 整体跳过。
					GcuIccProfileAssociator.Snapshot calibIccBefore = await GcuIccProfileAssociator.CaptureAsync("切档");
					int value = keyValuePair.Value;
					int acceptedIndex = calibCombo.SelectedIndex;
					calibCombo.Enabled = false;
					bool flag4;
					try
					{
					Interlocked.Increment(ref _calibSetInFlight);
					if (value == 999)
					{
						// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏
						string? iccName = AppConfig.GetString("calib_custom_icc");
						string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
						if (!string.IsNullOrEmpty(iccPath) && File.Exists(iccPath))
						{
							try
							{
								flag4 = await Task.Run(() => IccHelper.ApplyIcc(iccPath));
							}
							catch (Exception ex)
							{
								Logger.WriteLine("自定义 ICC 应用异常: " + ex.Message);
								flag4 = false;
							}
						}
						else
						{
							// 自定义 ICC 文件缺失（如重装后被清理）：回退默认档并弹回下拉
							Logger.WriteLine("自定义 ICC 文件缺失，切换校色回退默认档: " + (iccName ?? "(空)"));
							value = 1;
							flag4 = await Program.service.SetColorCalibration(1);
							if (flag4)
							{
								acceptedIndex = CalibComboIndexFor(1, calibCombo);
							}
						}
					}
					else
					{
						// 切离自定义档：若自定义 ICC 已应用则清理，避免颜色管理页残留
						if ((AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on"))
						{
							try
							{
								await Task.Run(() => IccHelper.ClearAllIcc());
		
							}
							catch (Exception ex)
							{
								Logger.WriteLine("切离自定义档清理 ICC 异常: " + ex.Message);
							}
						}
						flag4 = await Program.service.SetColorCalibration(CalibrationModeForCombo(calibCombo));
					}
					}
					finally
					{
						Interlocked.Decrement(ref _calibSetInFlight);
						_calibSyncHoldUntil = DateTime.UtcNow.AddSeconds(8.0);
						calibCombo.Enabled = true;
					}
					if (flag4)
					{
						// 2026-10-10【默认启用】切档成功后同样做一次前后比对 + 关联 + 读回验证。
						if (value != 999 && calibIccBefore != null)
						{
							// ⭐ 2026-10-10：同开关处，只改"怎么等待"——即发即忘，切档下拉立刻可用；
							// 关联结果记在 [icc-assoc] 的"关联结果(切档)"。
							GcuIccProfileAssociator.ApplyAfterCalibrationOnDetached(calibIccBefore, "切档");
						}
						_lastCalibUi = DateTime.UtcNow;
						// 记忆用户选中的档位（按 GCU 上报档位数收敛，不再做 4 折 2），
						// 使重启回显、开机应用与用户选择一致
						AppConfig.Set("calib_mode", CalibrationModeForCombo(calibCombo));
						AppConfig.Set("calib_enabled", 1);
					}
					else
					{
						showToast = true;
						_syncingDisplay = true;
						calibCombo.SelectedIndex = acceptedIndex;
						_syncingDisplay = false;
					}
				}
			}
			if (showToast)
			{
				Logger.WriteLine("校色档位应用失败: sent=" + CalibrationModeForCombo(calibCombo) + ", 已回退到原档位");
				ToastForm.ShowFailure(Strings.ColorCalibrationFailed);
			}
		};
		_calibSwitch = calibSwitch;
		_calibCombo = calibCombo;
		TableLayoutPanel calibPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 4,
			RowCount = 1,
			Height = ResponsiveLayout.LogicalToDevice(this, 34),
			Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 4), 0, 0),
			BackColor = UiVisualStyle.Window,
			Name = "rowColorCalibration"
		};
		// 2026-10-09（UI 反馈①，裁切①-a）：末列（「设置」小胶囊）原来只有 12%，中文两字 8pt 在最窄的场景下
		// 会贴着按钮内沿/被 EndEllipsis 削掉右边 ⇒ 12% → **18%**（从下拉那列 56% → **50%** 里挪 6%）。
		// 行高（D(34)）、行数、控件顺序、Margin 一律未动；下拉的可见宽度仍 ≈ 50%×行宽（比 12% 的胶囊宽得多）。
		// 2026-10-09（UI 反馈②，用户原话「屏幕色彩的按钮可以收窄一点」）：18% 又太宽 ⇒ 折中收成 **15%**，
		// 把让出的 3% 还回下拉列（50% → **53%**）。仍带余量：本行宽 ≈368 逻辑像素时末列 ≈55 逻辑像素、
		// 减左右各 D(2) 边距后按钮可用宽 ≈51 逻辑像素，而「设置」8pt 两字 TextRenderer 实测 ≈22 逻辑像素
		// ⇒ 余量 ≈29 逻辑像素（≈2.3 个字宽），不会触发 EndEllipsis（数值见本轮报告）。
		calibPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
		calibPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10f));
		calibPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53f));
		calibPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15f));
		calibPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label calibCaption = new Label
		{
			Text = Strings.ColorCalibration,
			ForeColor = UiVisualStyle.Text,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			Font = UiStyleBody()
		};
		calibPanel.Controls.Add(calibCaption, 0, 0);
		calibPanel.Controls.Add(calibSwitch, 1, 0);
		calibPanel.Controls.Add(calibCombo, 2, 0);
		RButton calibSettingsBtn = new RButton
		{
			Tag = "mini-chip",
			Text = Strings.Settings,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			BorderColor = UiVisualStyle.Border,
			BorderRadius = 12,
			FlatStyle = FlatStyle.Flat,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Cursor = Cursors.Hand,
			Font = UiVisualStyle.Font(8f),
			Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 3), 0, ResponsiveLayout.LogicalToDevice(this, 3))
		};
		calibSettingsBtn.FlatAppearance.BorderSize = 0;
		calibSettingsBtn.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
		calibSettingsBtn.Click += (object? _, EventArgs _) =>
		{
			using CalibrationStartupForm settingsForm = new CalibrationStartupForm();
			settingsForm.ShowDialog(this);
			// 自定义开关状态可能在弹窗内变化：关闭弹窗后重建校色下拉（含/不含“自定义”项）
			RebuildCalibrationItems(_calibCombo);
		};
		_calibSettingsBtn = calibSettingsBtn;
		calibPanel.Controls.Add(calibSettingsBtn, 3, 0);
		BufferedTableLayoutPanel control2 = BuildHeadRow(BuildHeadIcon(UiGlyph.Kind.Display), label2, autoHzChk);
		bufferedTableLayoutPanel.SetColumnSpan(control2, 3);
		bufferedTableLayoutPanel.Controls.Add(control2, 0, 0);
		_hzSegTable.Dock = DockStyle.Fill;
		_hzSegTable.Margin = Padding.Empty;
		bufferedTableLayoutPanel.SetColumnSpan(_hzSegTable, 3);
		bufferedTableLayoutPanel.Controls.Add(_hzSegTable, 0, 1);
		bufferedTableLayoutPanel.Controls.Add(control, 0, 2);
		bufferedTableLayoutPanel.Controls.Add(brightSlider, 1, 2);
		bufferedTableLayoutPanel.Controls.Add(brightPercent, 2, 2);
		bufferedTableLayoutPanel.SetColumnSpan(calibPanel, 3);
		bufferedTableLayoutPanel.Controls.Add(calibPanel, 0, 3);
		TableLayoutPanel displayEffectRow = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 4,
			RowCount = 1,
			Height = ResponsiveLayout.LogicalToDevice(this, 34),
			Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 4), 0, 0),
			BackColor = UiVisualStyle.Window,
			Name = "rowDisplayEffect"
		};
		// 2026-10-09（UI 反馈①，裁切①-b）：与上面「屏幕校色」行同因同修 —— 末列「编辑」小胶囊 12% → 18%
		//（下拉列 56% → 50%）；行高 D(34)、行数、控件顺序、Margin 均未动。
		// 2026-10-09（UI 反馈②）：与「设置」同步收成末列 **15%**、下拉列 **53%**（同因同数值，见上一行的算式）。
		displayEffectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
		displayEffectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10f));
		displayEffectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53f));
		displayEffectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15f));
		displayEffectRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label effectCaption = new Label
		{
			Text = Strings.DisplayEffect,
			AutoSize = false,
			Dock = DockStyle.Fill,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleBody(),
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty
		};
		_displayEffectSwitch = new RCheckBox
		{
			AutoSize = true,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Margin = Padding.Empty,
			Anchor = AnchorStyles.Left
		};
		_displayEffectSwitch.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingDisplay || Program.UiAuditMode)
			{
				return;
			}
			MechrevoService? svc = Program.service;
			// 2026-10-15（C-1 第 2 步分类 ①）：下发 SetDisplayEffectSwitch（GCU 上报的显示特效）⇒ 真正需要 GCU。
			if (svc != null && Program.hw != null && Program.hw.IsGcuConnected)
			{
				bool requested = _displayEffectSwitch.Checked;
				_displayEffectSwitch.Enabled = false;
				if (!(await svc.SetDisplayEffectSwitch(requested)))
				{
					_syncingDisplay = true;
					_displayEffectSwitch.Checked = !requested;
					_syncingDisplay = false;
				}
				else if (requested)
				{
					string mode = GetDisplayEffectMode();
					MechrevoHw hw = Program.hw;
					if (hw != null && hw.DisplayEffectProfiles.TryGetValue(mode, out DisplayEffectProfile? profile))
					{
						await svc.ApplyDisplayEffectProfile(profile);
					}
					_ = svc.RequestDisplayEffectStatus();
				}
				AppConfig.Set("display_effect", requested ? 1 : 0);
				_displayEffectSwitch.Enabled = true;
			}
			else
			{
				bool requested = _displayEffectSwitch.Checked;
				_syncingDisplay = true;
				_displayEffectSwitch.Checked = !requested;
				_syncingDisplay = false;
			}
			bool effectOn = _displayEffectSwitch.Checked;
			_displayEffectCombo.Enabled = effectOn;
			_displayEffectEditBtn.Enabled = effectOn;
		};
		_displayEffectCombo = new MechrevoLite.UI.RComboBox
		{
			Cursor = Cursors.Hand,
			DropDownStyle = ComboBoxStyle.DropDownList,
			DisplayMember = "Key",
			FlatStyle = FlatStyle.Flat,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleBody(),
			Anchor = (AnchorStyles.Left | AnchorStyles.Right),
			Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 8), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 8), ResponsiveLayout.LogicalToDevice(this, 2))
		};
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectVibrantMode, "VibrantMode"));
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectInternetMode, "InternetMode"));
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectVideoMode, "VideoMode"));
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectLowBlueMode, "LowBlueMode"));
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectCinemaMode, "CinemaMode"));
		_displayEffectCombo.Items.Add(new KeyValuePair<string, string>(Strings.DisplayEffectPhotoMode, "PhotoMode"));
		_displayEffectCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingDisplay || Program.UiAuditMode)
			{
				return;
			}
			if (_displayEffectCombo.SelectedItem is KeyValuePair<string, string> kv && Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected && hw.DisplayEffectProfiles.TryGetValue(kv.Value, out DisplayEffectProfile? profile))
				{
					await Program.service.ApplyDisplayEffectProfile(profile);
					AppConfig.Set("display_effect_mode", kv.Value);
					_ = Program.service.RequestDisplayEffectStatus();
				}
				else if (hw != null && hw.IsConnected)
				{
					_ = Program.service.RequestDisplayEffectStatus();
				}
			}
		};
		RButton effectEditBtn = new RButton
		{
			Tag = "mini-chip",
			Text = Strings.Edit,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			BorderColor = UiVisualStyle.Border,
			BorderRadius = 12,
			FlatStyle = FlatStyle.Flat,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Cursor = Cursors.Hand,
			Font = UiVisualStyle.Font(8f),
			Margin = new Padding(ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 3), 0, ResponsiveLayout.LogicalToDevice(this, 3))
		};
		effectEditBtn.FlatAppearance.BorderSize = 0;
		effectEditBtn.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
		_displayEffectEditBtn = effectEditBtn;
		effectEditBtn.Click += (object? _, EventArgs _) =>
		{
			using DisplayEffectEditorForm editor = new DisplayEffectEditorForm(GetDisplayEffectMode());
			editor.ShowDialog(this);
			SyncDisplayEffectRow();
			MechrevoService? svc = Program.service;
			if (svc != null)
			{
				_ = svc.RequestDisplayEffectStatus();
			}
		};
		displayEffectRow.Controls.Add(effectCaption, 0, 0);
		displayEffectRow.Controls.Add(_displayEffectSwitch, 1, 0);
		displayEffectRow.Controls.Add(_displayEffectCombo, 2, 0);
		displayEffectRow.Controls.Add(effectEditBtn, 3, 0);
		bufferedTableLayoutPanel.SetColumnSpan(displayEffectRow, 3);
		bufferedTableLayoutPanel.Controls.Add(displayEffectRow, 0, 4);
		MechrevoHw hw0 = Program.hw;
		bool? gcuEffectOn0 = hw0?.DisplayEffectOn;
		// 2026-10-09（默认值修正）：display_effect 键缺失 = 新装 / 全新配置 ⇒ 默认「关」。
		// 旧写法 (AppConfig.Get("display_effect") != 0) 是错的：AppConfig.Get 的缺省返回值是 **-1**
		// （AppConfig.cs:397 `Get(string name, int empty = -1)`），键不存在时 -1 != 0 成立，
		// 于是开关默认显示为「开」。GCU 真的上报过 DisplaySwitch 时仍以硬件回读为准（gcuEffectOn0）。
		bool effectOn0 = gcuEffectOn0 ?? (AppConfig.Exists("display_effect") && AppConfig.Is("display_effect"));
		if (_displayEffectSwitch.Checked != effectOn0)
		{
			_syncingDisplay = true;
			_displayEffectSwitch.Checked = effectOn0;
			_syncingDisplay = false;
		}
		_displayEffectCombo.Enabled = effectOn0;
		_displayEffectEditBtn.Enabled = effectOn0;
		// 2026-10-09（默认值修正）：display_effect_mode 缺失（新装 / 全新配置）时不再预置「多彩」——
		// 老写法 GetString("display_effect_mode", "VibrantMode") 会让关着的开关旁边永远显示「多彩」，
		// 用户读成"多彩模式默认是开的"。现在只有用户真的选过档位才回显；没选过就保持未选中（空白）。
		string? memEffectMode = AppConfig.GetString("display_effect_mode");
		if (!string.IsNullOrEmpty(memEffectMode))
		{
			foreach (object itemObj in _displayEffectCombo.Items)
			{
				if (itemObj is KeyValuePair<string, string> kvMem && kvMem.Value == memEffectMode)
				{
					int idxMem = _displayEffectCombo.Items.IndexOf(itemObj);
					if (_displayEffectCombo.SelectedIndex != idxMem)
					{
						_syncingDisplay = true;
						_displayEffectCombo.SelectedIndex = idxMem;
						_syncingDisplay = false;
					}
					break;
				}
			}
		}
		MechrevoService? svc = Program.service;
		if (svc != null)
		{
			_ = svc.RequestDisplayEffectStatus();
		}
		RCheckBox overdriveChk = new RCheckBox
		{
			Name = "checkLcdOverdrive",
			Text = Strings.Overdrive,
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Margin = new Padding(0, 8, 14, 0),
			Visible = false
		};
		overdriveChk.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingDisplay && Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					bool requested = overdriveChk.Checked;
					overdriveChk.Enabled = false;
					if (!(await Program.service.SwitchLcdOverdrive(requested)))
					{
						_syncingDisplay = true;
						overdriveChk.Checked = !requested;
						_syncingDisplay = false;
					}
					overdriveChk.Enabled = true;
				}
			}
		};
		_overdriveChk = overdriveChk;
		BufferedPanel lcPanel = new BufferedPanel
		{
			Name = "panelLc",
			CardStyle = true,
			Dock = DockStyle.Top,
			Width = 414,
			Height = 96,
			BackColor = UiVisualStyle.Surface,
			Padding = new Padding(12, 4, 12, 4)
		};
		Label label3 = new Label
		{
			Text = Strings.LiquidCoolingSystem,
			Font = UiVisualStyle.Font(9f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			AutoSize = true,
			Location = new Point(20, 16)
		};
		UiVisualStyle.ApplyGlyph(label3, UiGlyph.Kind.Droplet);
		RButton rButton = new RButton();
		rButton.Tag = "mini-chip";
		rButton.Text = Strings.NotConnected;
		rButton.ForeColor = UiVisualStyle.Muted;
		rButton.BackColor = UiVisualStyle.Surface;
		rButton.BorderColor = UiVisualStyle.Border;
		rButton.BorderRadius = 12;
		rButton.FlatStyle = FlatStyle.Flat;
		rButton.FlatAppearance.BorderSize = 0;
		rButton.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
		rButton.AutoSize = false;
		rButton.Size = new Size(240, 28);
		rButton.Location = new Point(136, 17);
		rButton.TextAlign = ContentAlignment.MiddleLeft;
		rButton.Cursor = Cursors.Hand;
		rButton.Font = UiVisualStyle.Font(8.5f);
		rButton.AutoEllipsis = true;
		RButton lcStatus = rButton;
		lcStatus.TextChanged += (object? _, EventArgs _) =>
		{
			toolTip.SetToolTip(lcStatus, lcStatus.Text);
			if (_liquidGroup != null)
			{
				_liquidGroup.Summary = LcSummaryText(lcStatus.Text);
			}
		};
		lcStatus.Click += async (object? _, EventArgs _) =>
		{
			if (!_lcConnecting)
			{
				LiquidCoolingControlRoute liquidCoolingControlRoute = GetLiquidCoolingRoute();
				if (liquidCoolingControlRoute == LiquidCoolingControlRoute.DirectBle)
				{
					WaterCoolerBle ble = Program.ble;
					if (ble != null && ble.IsConnected)
					{
						if (MessageBox.Show(Strings.DisconnectBleWaterCooler, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
						{
							_lcManualDisconnect = true;
							_lcReconnectAttempts = 3;
							Program.ble.Disconnect();
						}
						return;
					}
				}
				if (liquidCoolingControlRoute == LiquidCoolingControlRoute.Gcu)
				{
					lcStatus.Text = Strings.LcRefreshing;
					lcStatus.ForeColor = UiVisualStyle.Warn;
					await RequestGcuLiquidCoolingStatusAsync(force: true);
				}
				else
				{
					_lcManualDisconnect = false;
					_lcReconnectAttempts = 0;
					await AutoConnectLc(silent: false);
				}
			}
		};
		lcPanel.Controls.Add(label3);
		lcPanel.Controls.Add(lcStatus);
		RComboBox lcPumpCombo = new MechrevoLite.UI.RComboBox
		{
			Name = "comboLcPump",
			Cursor = Cursors.Hand,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 100,
			Location = new Point(20, 55),
			DisplayMember = "Key",
			Enabled = false
		};
		lcPumpCombo.Items.Add(new KeyValuePair<string, int>(Strings.Auto, -1));
		for (int num3 = 0; num3 < LiquidCoolingDisplayPolicy.PumpGearLabels.Length; num3++)
		{
			lcPumpCombo.Items.Add(new KeyValuePair<string, int>(LiquidCoolingDisplayPolicy.GearLabel(LiquidCoolingDisplayPolicy.PumpGearLabels[num3], num3, 2), num3));
		}
		lcPumpCombo.SelectedIndex = FindProfileIndex(lcPumpCombo, Program.ble?.SavedPumpProfile ?? (-2));
		int acceptedPumpSelection = lcPumpCombo.SelectedIndex;
		lcPumpCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingLc || !(lcPumpCombo.SelectedItem is KeyValuePair<string, int> { Value: not -2 } kv))
			{
				return;
			}
			LiquidCoolingControlRoute route = GetLiquidCoolingRoute();
			lcPumpCombo.Enabled = false;
			LiquidCoolingControlRoute liquidCoolingControlRoute = route;
			bool flag4;
			if (liquidCoolingControlRoute != LiquidCoolingControlRoute.Gcu)
			{
				if (liquidCoolingControlRoute == LiquidCoolingControlRoute.DirectBle)
				{
					WaterCoolerBle ble = Program.ble;
					if (ble != null && ble.IsConnected)
					{
						flag4 = await ble.ApplyPumpProfileAsync(kv.Value, CurrentCoolingTemperature());
						goto IL_0245;
					}
				}
			}
			else
			{
				if (Program.service != null && LiquidCoolingAutoPolicy.ShouldTransmitVendorAuto(kv.Value))
				{
					flag4 = await Program.service.SwitchLcFanAuto();
					goto IL_0245;
				}
				if (Program.service != null)
				{
					int value = kv.Value;
					if (value >= 0 && value <= 2)
					{
						flag4 = await Program.service.SwitchLcPump(kv.Value);
						goto IL_0245;
					}
				}
			}
			flag4 = false;
			goto IL_0245;
			IL_0245:
			bool ok = flag4;
			if (ok && route == LiquidCoolingControlRoute.Gcu)
			{
				AppConfig.Set("lc_pump_profile", kv.Value);
				await RequestGcuLiquidCoolingStatusAsync(force: true);
			}
			_syncingLc = true;
			try
			{
				if (ok)
				{
					acceptedPumpSelection = lcPumpCombo.SelectedIndex;
					lcStatus.Text = ((route == LiquidCoolingControlRoute.Gcu) ? (Strings.LcPumpConfirmed + kv.Key) : (Strings.LcPumpSent + kv.Key));
				}
				else
				{
					lcPumpCombo.SelectedIndex = acceptedPumpSelection;
					RButton rButton3 = lcStatus;
					flag4 = route == LiquidCoolingControlRoute.Gcu;
					if (flag4)
					{
						int value = kv.Value;
						bool flag5 = ((value == -1 || value == 3) ? true : false);
						flag4 = flag5;
					}
					rButton3.Text = (flag4 ? Strings.LcPumpFallback : Strings.LcPumpWriteFailed);
				}
			}
			finally
			{
				_syncingLc = false;
				ComboBox comboBox = lcPumpCombo;
				liquidCoolingControlRoute = GetLiquidCoolingRoute();
				flag4 = (uint)(liquidCoolingControlRoute - 2) <= 1u;
				comboBox.Enabled = flag4;
			}
		};
		lcPanel.Controls.Add(lcPumpCombo);
		RComboBox lcFanCombo = new MechrevoLite.UI.RComboBox
		{
			Cursor = Cursors.Hand,
			Name = "comboLcFan",
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 100,
			Location = new Point(130, 55),
			DisplayMember = "Key",
			Enabled = false
		};
		lcFanCombo.Items.Add(new KeyValuePair<string, int>(Strings.Auto, -1));
		for (int num4 = 0; num4 < LiquidCoolingDisplayPolicy.FanGearLabels.Length; num4++)
		{
			lcFanCombo.Items.Add(new KeyValuePair<string, int>(LiquidCoolingDisplayPolicy.GearLabel(LiquidCoolingDisplayPolicy.FanGearLabels[num4], num4, 3), num4));
		}
		lcFanCombo.SelectedIndex = FindProfileIndex(lcFanCombo, Program.ble?.SavedFanProfile ?? (-2));
		int acceptedFanSelection = lcFanCombo.SelectedIndex;
		lcFanCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingLc || !(lcFanCombo.SelectedItem is KeyValuePair<string, int> { Value: not -2 } kv))
			{
				return;
			}
			LiquidCoolingControlRoute route = GetLiquidCoolingRoute();
			lcFanCombo.Enabled = false;
			LiquidCoolingControlRoute liquidCoolingControlRoute = route;
			bool flag4;
			if (liquidCoolingControlRoute != LiquidCoolingControlRoute.Gcu)
			{
				if (liquidCoolingControlRoute == LiquidCoolingControlRoute.DirectBle)
				{
					WaterCoolerBle ble = Program.ble;
					if (ble != null && ble.IsConnected)
					{
						flag4 = await ble.ApplyFanProfileAsync(kv.Value, CurrentCoolingTemperature());
						goto IL_0245;
					}
				}
			}
			else
			{
				if (Program.service != null && LiquidCoolingAutoPolicy.ShouldTransmitVendorAuto(kv.Value))
				{
					flag4 = await Program.service.SwitchLcFanAuto();
					goto IL_0245;
				}
				if (Program.service != null)
				{
					int value = kv.Value;
					if (value >= 0 && value <= 3)
					{
						flag4 = await Program.service.SwitchLcFan(kv.Value);
						goto IL_0245;
					}
				}
			}
			flag4 = false;
			goto IL_0245;
			IL_0245:
			bool ok = flag4;
			if (ok && route == LiquidCoolingControlRoute.Gcu)
			{
				AppConfig.Set("lc_fan_profile", kv.Value);
				await RequestGcuLiquidCoolingStatusAsync(force: true);
			}
			_syncingLc = true;
			try
			{
				if (ok)
				{
					acceptedFanSelection = lcFanCombo.SelectedIndex;
					lcStatus.Text = ((route == LiquidCoolingControlRoute.Gcu) ? (Strings.LcFanConfirmed + kv.Key) : (Strings.LcFanSent + kv.Key));
				}
				else
				{
					lcFanCombo.SelectedIndex = acceptedFanSelection;
					RButton rButton3 = lcStatus;
					flag4 = route == LiquidCoolingControlRoute.Gcu;
					if (flag4)
					{
						int value = kv.Value;
						bool flag5 = ((value == -1 || value == 4) ? true : false);
						flag4 = flag5;
					}
					rButton3.Text = (flag4 ? Strings.LcFanFallback : Strings.LcFanWriteFailed);
				}
			}
			finally
			{
				_syncingLc = false;
				ComboBox comboBox = lcFanCombo;
				liquidCoolingControlRoute = GetLiquidCoolingRoute();
				flag4 = (uint)(liquidCoolingControlRoute - 2) <= 1u;
				comboBox.Enabled = flag4;
			}
		};
		lcPanel.Controls.Add(lcFanCombo);
		RButton rButton2 = new RButton();
		rButton2.Name = "buttonLcLight";
		rButton2.Tag = "mini-chip";
		rButton2.Text = Strings.WaterCoolingLighting;
		rButton2.TextAlign = ContentAlignment.MiddleCenter;
		rButton2.FlatStyle = FlatStyle.Flat;
		rButton2.ForeColor = UiVisualStyle.Text;
		rButton2.BackColor = UiVisualStyle.Surface;
		rButton2.BorderColor = UiVisualStyle.Border;
		rButton2.BorderRadius = 12;
		rButton2.FlatAppearance.BorderColor = UiVisualStyle.Border;
		rButton2.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
		rButton2.AutoSize = false;
		rButton2.AutoEllipsis = true;
		rButton2.Font = UiStyleCaptionFont();
		rButton2.Cursor = Cursors.Hand;
		rButton2.Enabled = true;
		RButton lcLightBtn = rButton2;
		_liquidCoolingLightMenu = new LiquidCoolingLightMenu
		{
			Name = "liquidCoolingLightMenu"
		};
		LiquidCoolingLightMenu lcLightMenu = _liquidCoolingLightMenu;
		lcLightMenu.AddHeader(Strings.LcHeadEffectHeader);
		AddLightItem(Strings.LcCyanStatic, "cyan_static");
		AddLightItem(Strings.LcCyanBreath, "cyan_breath");
		AddLightItem(Strings.LcColorful, "colorful");
		AddLightItem(Strings.LcColorfulBreath, "colorful_breath");
		lcLightMenu.AddHeader(Strings.LcCustomColorHeader);
		AddCustomLightItem(Strings.LcCustomStatic, "custom_static");
		AddCustomLightItem(Strings.LcCustomBreath, "custom_breath");
		lcLightMenu.AddHeader(Strings.LcFanLedHeader);
		AddLightItem(Strings.LcFanRotate, "fan_rotate", requiresFanLed: true);
		AddLightItem(Strings.LcFanRainbow, "fan_rainbow", requiresFanLed: true);
		lcLightMenu.AddSeparator();
		AddLightItem(Strings.LcAllOff, "off");
		lcLightMenu.Opening += (object? _, CancelEventArgs _) =>
		{
			bool enabled = SupportsLiquidCoolingFanLighting(GetLiquidCoolingRoute());
			foreach (ToolStripMenuItem fanLedItem in lcLightMenu.FanLedItems)
			{
				fanLedItem.Enabled = enabled;
			}
		};
		lcLightBtn.Click += async (object? _, EventArgs _) =>
		{
			LiquidCoolingControlRoute liquidCoolingControlRoute = GetLiquidCoolingRoute();
			if ((uint)liquidCoolingControlRoute <= 1u)
			{
				await ProbeSystemBluetoothConnectionAsync(force: true);
			}
			liquidCoolingControlRoute = GetLiquidCoolingRoute();
			if ((uint)liquidCoolingControlRoute <= 1u)
			{
				string text2 = DescribeGcuLiquidCoolingState();
				string text3 = (_lcSystemBluetoothObservation.IsConnected ? Strings.LcSetLightWaitGcu : (string.IsNullOrWhiteSpace(text2) ? Strings.LcSetLightFirstConnect : string.Format(Strings.LcSetLightWaitDone, text2)));
				MessageBox.Show(text3, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			else
			{
				lcLightMenu.Show(lcLightBtn, new Point(0, lcLightBtn.Height));
			}
		};
		lcPanel.Controls.Add(lcLightBtn);
		lcPanel.Controls.Clear();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = lcPanel.BackColor
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 84)));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		label3.Visible = false;
		lcPumpCombo.Dock = DockStyle.Fill;
		lcPumpCombo.Margin = new Padding(0, 3, 0, 3);
		lcFanCombo.Dock = DockStyle.Fill;
		lcFanCombo.Margin = new Padding(0, 3, 0, 3);
		tableLayoutPanel.Controls.Add(MakeRowName(Strings.PumpSpeed), 0, 0);
		tableLayoutPanel.Controls.Add(lcPumpCombo, 1, 0);
		tableLayoutPanel.SetColumnSpan(lcPumpCombo, 2);
		tableLayoutPanel.Controls.Add(MakeRowName(Strings.Fan), 0, 1);
		tableLayoutPanel.Controls.Add(lcFanCombo, 1, 1);
		tableLayoutPanel.SetColumnSpan(lcFanCombo, 2);
		lcLightBtn.Dock = DockStyle.Fill;
		lcLightBtn.Margin = new Padding(0, 4, 0, 4);
		tableLayoutPanel.Controls.Add(MakeRowName(Strings.Lighting), 0, 2);
		tableLayoutPanel.Controls.Add(lcLightBtn, 1, 2);
		lcStatus.Font = UiStyleCaptionFont();
		lcStatus.AutoEllipsis = true;
		lcStatus.Dock = DockStyle.Fill;
		lcStatus.Location = Point.Empty;
		lcStatus.Margin = new Padding(0, 4, 0, 4);
		tableLayoutPanel.Controls.Add(lcStatus, 2, 2);
		lcPanel.Controls.Add(tableLayoutPanel);
		_liquidCoolingStatusTimer.Tick += async (object? _, EventArgs _) =>
		{
			if (_lcStatusTicking)
			{
				return;
			}
			_lcStatusTicking = true;
			try
			{
				await RequestGcuLiquidCoolingStatusAsync();
				await ProbeSystemBluetoothConnectionAsync();
				_syncingLc = true;
				try
				{
					LiquidCoolingControlRoute liquidCoolingControlRoute = GetLiquidCoolingRoute();
					bool num5 = liquidCoolingControlRoute == LiquidCoolingControlRoute.DirectBle && (Program.ble?.IsConnected ?? false);
					bool flag4 = liquidCoolingControlRoute == LiquidCoolingControlRoute.Gcu && Program.hw != null;
					bool flag5 = num5 | flag4;
					ComboBox comboBox = lcPumpCombo;
					bool enabled = (lcFanCombo.Enabled = flag5);
					comboBox.Enabled = enabled;
					if (num5 && !_lcConnecting)
					{
						int num6 = FindProfileIndex(lcPumpCombo, Program.ble.SavedPumpProfile);
						int num7 = FindProfileIndex(lcFanCombo, Program.ble.SavedFanProfile);
						if (lcPumpCombo.SelectedIndex != num6)
						{
							lcPumpCombo.SelectedIndex = num6;
						}
						if (lcFanCombo.SelectedIndex != num7)
						{
							lcFanCombo.SelectedIndex = num7;
						}
						acceptedPumpSelection = num6;
						acceptedFanSelection = num7;
					}
					else if (flag4 && !_lcConnecting)
					{
						int num8 = FindProfileIndex(lcPumpCombo, LiquidCoolingDisplayPolicy.PumpProfile(AppConfig.Get("lc_pump_profile", -2), Program.hw.LcPumpControl));
						int num9 = FindProfileIndex(lcFanCombo, LiquidCoolingDisplayPolicy.FanProfile(AppConfig.Get("lc_fan_profile", -2), Program.hw.LcFanControl));
						if (lcPumpCombo.SelectedIndex != num8)
						{
							lcPumpCombo.SelectedIndex = num8;
						}
						if (lcFanCombo.SelectedIndex != num9)
						{
							lcFanCombo.SelectedIndex = num9;
						}
						acceptedPumpSelection = num8;
						acceptedFanSelection = num9;
					}
					if (num5)
					{
						bool flag7 = !Program.ble.IsFlowMeterReady;
						bool isFlowFaultConfirmed = Program.ble.IsFlowFaultConfirmed;
						RButton rButton3 = lcStatus;
						string text2 = (isFlowFaultConfirmed ? Strings.LcFlowFault : (flag7 ? Strings.LcConfirmingFlow : Strings.LcDirectConnected));
						rButton3.Text = text2;
						toolTip.SetToolTip(lcStatus, (isFlowFaultConfirmed ? Strings.LcFlowFault : (flag7 ? Strings.LcConfirmingFlow : Strings.LcDirectConnected)) + " · " + Program.ble.ConnectedName);
						lcStatus.ForeColor = (isFlowFaultConfirmed ? UiVisualStyle.Danger : (flag7 ? UiVisualStyle.Warn : UiVisualStyle.Ok));
					}
					else if (flag4)
					{
						string text3 = (string.IsNullOrWhiteSpace(Program.hw.LcCurrentMac) ? Program.hw.LcFwVersion : Program.hw.LcCurrentMac);
						bool lcMeterFaultConfirmed = Program.hw.LcMeterFaultConfirmed;
						lcStatus.Text = (lcMeterFaultConfirmed ? Strings.LcMeterFault : Strings.LcGcuConnected);
						toolTip.SetToolTip(lcStatus, (lcMeterFaultConfirmed ? Strings.LcGcuReportsFault : Strings.LcGcuConnected) + (string.IsNullOrWhiteSpace(text3) ? "" : (" · " + text3)));
						lcStatus.ForeColor = (lcMeterFaultConfirmed ? UiVisualStyle.Danger : UiVisualStyle.Ok);
					}
					else if (liquidCoolingControlRoute == LiquidCoolingControlRoute.BluetoothObserved)
					{
						string name = _lcSystemBluetoothObservation.Name;
						lcStatus.Text = Strings.LcBleConnected;
						toolTip.SetToolTip(lcStatus, Strings.LcBleWaitingGcu + (string.IsNullOrWhiteSpace(name) ? "" : (" · " + name)));
						lcStatus.ForeColor = UiVisualStyle.Warn;
					}
					else if (!string.IsNullOrWhiteSpace(DescribeGcuLiquidCoolingState()))
					{
						lcStatus.Text = DescribeGcuLiquidCoolingState();
						toolTip.SetToolTip(lcStatus, DescribeGcuLiquidCoolingDetail());
						lcStatus.ForeColor = ((Program.hw?.LcReportedConnected ?? false) ? UiVisualStyle.Warn : UiVisualStyle.Muted);
					}
					else
					{
						lcStatus.Text = Strings.LcNotConnectedClick;
						lcStatus.ForeColor = UiVisualStyle.Muted;
					}
				}
				finally
				{
					_syncingLc = false;
				}
				if (GetLiquidCoolingRoute() == LiquidCoolingControlRoute.Gcu)
				{
					await RestoreSavedGcuLightingAsync();
				}
				LiquidCoolingControlRoute liquidCoolingControlRoute2 = GetLiquidCoolingRoute();
				if (liquidCoolingControlRoute2 == LiquidCoolingControlRoute.Gcu)
				{
					_lcGcuConnectAttempts = 0;
				}
				else if (!_lcConnecting && LiquidCoolingConnectionPolicy.ShouldRetryGcuConnection(Program.hw?.IsConnected ?? false, Program.service != null, liquidCoolingControlRoute2, Program.hw?.LcActionSupportReported ?? false, Program.hw?.LcActionSupported ?? false, _lcGcuConnectAttempts, (long)(DateTime.UtcNow - _lcLastGcuConnectAttempt).TotalMilliseconds))
				{
					_lcGcuConnectAttempts++;
					_lcLastGcuConnectAttempt = DateTime.UtcNow;
					await AutoConnectLc(silent: true);
					liquidCoolingControlRoute2 = GetLiquidCoolingRoute();
				}
				if (Program.ble?.IsConnected ?? false)
				{
					_lcWasConnected = true;
					_lcReconnectAttempts = 0;
					await Program.ble.UpdateAutomaticCoolingAsync(CurrentCoolingTemperature());
				}
				else
				{
					switch (liquidCoolingControlRoute2)
					{
					case LiquidCoolingControlRoute.Gcu:
						await UpdateGcuAutomaticCoolingAsync();
						break;
					case LiquidCoolingControlRoute.None:
						if (!_lcManualDisconnect && !_lcConnecting)
						{
							WaterCoolerBle ble = Program.ble;
							if (ble != null && ble.HasSavedDevice && _lcReconnectAttempts < 3 && (DateTime.UtcNow - _lcLastReconnectAttempt).TotalSeconds >= 8.0)
							{
								if (_lcWasConnected)
								{
									_lcReconnectAttempts = 0;
								}
								_lcWasConnected = false;
								_lcReconnectAttempts++;
								_lcLastReconnectAttempt = DateTime.UtcNow;
								await AutoConnectLc(silent: true);
							}
						}
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Liquid cooling status update failed: " + ex.Message);
			}
			finally
			{
				_lcStatusTicking = false;
			}
		};
		if (!Program.UiAuditMode)
		{
			_liquidCoolingStatusTimer.Start();
		}
		Shown += async (object? _, EventArgs _) =>
		{
			if (!Program.UiAuditMode && !_lcAutoTried)
			{
				_lcAutoTried = true;
				await Task.Delay(2000);
				await AutoConnectLc(silent: true);
			}
		};
		Control[] array4 = new Control[3] { panel, bufferedPanel, lcPanel };
		for (int i = 0; i < array4.Length; i++)
		{
			ResponsiveLayout.ScaleFrom96(array4[i], this);
		}
		Controls.Add(bufferedPanel);
		Controls.Add(lcPanel);
		Controls.Add(panel);
		Controls.SetChildIndex(panel, 5);
		Controls.SetChildIndex(bufferedPanel, 2);
		_displayStatusTimer.Tick += (object? _, EventArgs _) =>
		{
			_syncingDisplay = true;
			try
			{
				MechrevoHw hw = Program.hw;
				// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
				//   ⭐ 旧实现这里只读 **GCU 上报**的 `hw.ScreenBrightness` —— 无 GCU 机型（宝龙达）恒为 -1
				//      ⇒ 周期回读**从来不跑**，滑块一直停在构造默认值 ❌
				//   ⭐ 现在统一走 `SyncBrightnessFromSystemReadOnly`：WMI `WmiMonitorBrightness`（首选）
				//      → dxva2 `GetMonitorBrightness`（备选）→ GCU 字段（旧路径兼容）；
				//      ⭐ 只更新滑块/数值，⛔ 不写硬件；低频（5 s）且只在设置页可见时跑 ✅
				SyncBrightnessFromSystemReadOnly(force: false);
				// ⚠️⚠️ 2026-10-15（用户反馈「刷新率的回读有问题 走 Windows API 的，我点了 60 切过去还是显示 180
				// 或者说状态刷新不及时」）：`SyncHzButtons()` 原来被包在**上面那个「亮度可读」条件里** ——
				// 而无 GCU 时 `hw.ScreenBrightness` 恒为 -1（那是 **GCU 上报**字段）⇒ 周期回读**根本不跑**，
				// Win32 切完档后高亮就永远停在旧值（正是"状态刷新不及时"）。
				// 现把它提到条件**外面**，每个 tick 都跑一次：内部按 `_lastHzSignature` 短路（列表没变时几乎零开销），
				// 且 Win32 档位枚举自带 60s 缓存 ⇒ 不会给定时器加压。
				SyncHzButtons();
				if (_autoHzChk != null)
				{
					// ⭐⭐ 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**）：
					// 可用性 = **Win32 侧至少有两档可切**（内屏实测最大档 > MIN_RATE）—— 与 GCU 无关，
					// 老接口 / 宝龙达 / 无 GCU 的机器都能用；**真值 = 我们自己的 `screen_auto` 配置**。
					// ⚠️ 不再拿 GCU 的 `DcHz` 当可用性/真值判据 ⇒ 也就不会再出现"回读有问题"（那种机器上
					// 该字段根本不上报，勾选框会停在用户点出来的样子）。
					bool autoHzAvailable = ScreenGateProbe.Win32MaxRefreshRate() > ScreenControl.MIN_RATE;
					if (_autoHzChk.Enabled != autoHzAvailable)
					{
						_autoHzChk.Enabled = autoHzAvailable;
					}
					bool autoHzOn = AppConfig.Is("screen_auto");
					if (_autoHzChk.Checked != autoHzOn)
					{
						_autoHzChk.Checked = autoHzOn;
					}
					// ⭐ 2026-10-15（用户拍板「根因治掉吧」）：**一次性把固件侧的自动切换关掉**。
					// 老版本会把"自动开着"告诉固件（`SwitchAutoRefreshRate(1)`），那在**固件侧是持久的** ⇒
					// 本轮改成"恒发 false"后，只有用户再拨一次开关才会送出去。这里在 GCU 可用后主动补送一次 0，
					// 确保真凶（固件的 GPU_DC_HZ：**息屏/显示恢复时**把用户手选的 100Hz 顶回 180Hz）处于关闭状态。
					if (!_gcuAutoOffPushed)
					{
						MechrevoHw hwGcuOff = Program.hw;
						if (hwGcuOff != null && hwGcuOff.IsGcuConnected && Program.service != null)
						{
							_gcuAutoOffPushed = true;
							_ = Program.service.SwitchAutoRefreshRate(false);
							Logger.WriteLine("[hz-gcu-autofix] 已向 GCU 补送 SwitchAutoRefreshRate(false)：确保固件的 GPU_DC_HZ 关闭（它会在息屏/唤醒时顶回刷新率）");
						}
					}
				}
				if (_calibCombo != null)
		{
			int? mqttCalibNow = Program.hw?.MqttColorCalibrationLevel;
			if (mqttCalibNow != _lastMqttCalibLevel)
			{
				_lastMqttCalibLevel = mqttCalibNow;
				RebuildCalibrationItems(_calibCombo);
			}
		}
		if (_calibCombo != null && (DateTime.UtcNow - _lastCalibUi).TotalSeconds >= 3.0)
				{
					// 回读同步按能力区分（用户口径：40 系 GCU 不支持回读、50 系支持回读开关）：
					// 只有 50 系且 GCU 上报过档位字段才做回读，40 系一律跳过 —— 否则注册表/GCU 旧值
					// 会在定时刷新时把用户刚选的档位或开关拉回去（"选完一小会儿又跳回 sRGB"）。
					// 50 系：SetColorCalibration 要等回执，最长 4s 才返回；这期间 _lastCalibUi 还没更新，
					// 若此时回读覆盖下拉，用户会看到"刚选完就跳回 sRGB"。故用"操作进行中"闸门 + 操作后 8s 保持窗。
					bool calibOpInFlight = _calibSetInFlight > 0;
					bool calibHold = DateTime.UtcNow < _calibSyncHoldUntil;
					// 2026-10-09（默认值修正）：回读覆盖开关只对「用户已经表过态」的配置生效
					// （calib_enabled 键存在）。新装 / 全新配置下开关默认关（见构造处），
					// 若这里仍按 GCU 上报档位 > 0 回读，几秒后就会把默认关又翻成开。
					// 用户一旦拨动开关该键即落盘，此后回读行为与改动前逐字一致。
					bool calibPreferenceSaved = AppConfig.Exists("calib_enabled");
					if (CalibrationReadbackSupported() && calibPreferenceSaved && !calibOpInFlight && !calibHold)
					{
					int num5 = CalibComboIndexFor(ResolveColorCalibrationMode());
					if (_calibCombo.SelectedIndex != num5)
					{
						_calibCombo.SelectedIndex = num5;
					}
					if (_calibSwitch != null)
					{
						// 2026-10-13（「校色开关关了又自己打开」根因 · 最小修法）：开关态只认 LM 自己落盘的
						// calib_enabled（与构造处 :2131 同一口径），**不再用「档位 > 0」推**。多档设备（本机
						// GCU 上报 SupportColorCalibration=4）档位恒 ≥1（1=默认档，关校色不会把档位写回 0，
						// 见诊断包证据 SetColorCalibration(off) expectedMode=0 actualMode=1 switch=False），
						// ⇒ 旧写法在 8s 保持窗结束后必然把开关翻回「开」。档位下拉的回读同步照旧（上一行）。
						bool calibOn = AppConfig.Is("calib_enabled");
						_syncingDisplay = true;
						_calibSwitch.Checked = calibOn;
						_calibCombo.Enabled = calibOn;
						_syncingDisplay = false;
					}
					}
				}
				hw = Program.hw;
				if (hw != null && hw.LcdOverdriveSeen)
				{
					if (!overdriveChk.Visible)
					{
						overdriveChk.Visible = true;
					}
					if (overdriveChk.Checked != Program.hw.LcdOverdrive)
					{
						overdriveChk.Checked = Program.hw.LcdOverdrive;
					}
				}
			}
			finally
			{
				_syncingDisplay = false;
			}
		};
		if (!Program.UiAuditMode)
		{
			_displayStatusTimer.Start();
		}
		// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
		//   ⭐ 首次打开设置页 ⇒ **只读同步一次**系统亮度（WMI 优先 / dxva2 备选），
		//      ⛔ 不写硬件（不调 `ScreenBrightness.Set`）、⛔ 不动 `_lastBrightnessUi`（那是用户拖动的时间戳）✅
		Shown += (object? _, EventArgs _) =>
		{
			if (!Program.UiAuditMode)
			{
				SyncBrightnessFromSystemReadOnly(force: true);
			}
		};
		void AddCustomLightItem(string text2, string profile)
		{
			lcLightMenu.AddItem(text2, async () =>
			{
				using RColorPicker picker = new RColorPicker(CurrentLiquidCoolingLightColor())
				{
					Text = text2
				};
				if (picker.ShowDialog(this) == DialogResult.OK)
				{
					await ApplyLiquidCoolingLightAsync(text2, profile, requiresFanLed: false, picker.Color);
				}
			});
		}
		void AddLightItem(string text2, string profile, bool requiresFanLed = false)
		{
			ToolStripMenuItem item4 = null;
			item4 = lcLightMenu.AddItem(text2, async () =>
			{
				item4.Enabled = false;
				try
				{
					await ApplyLiquidCoolingLightAsync(text2, profile, requiresFanLed);
				}
				finally
				{
					item4.Enabled = true;
				}
			}, requiresFanLed);
		}
		async Task ApplyLiquidCoolingLightAsync(string text5, string profile, bool requiresFanLed, Color? color = null)
		{
			LiquidCoolingControlRoute route = GetLiquidCoolingRoute();
			if ((uint)route <= 1u)
			{
				string text2 = DescribeGcuLiquidCoolingState();
				string? text3;
				if (route == LiquidCoolingControlRoute.BluetoothObserved)
				{
					text3 = Strings.LcBleDetectedNotControllable;
				}
				else
				{
					text3 = (string.IsNullOrWhiteSpace(text2) ? Strings.LcSystemNotConnected : string.Format(Strings.LcNotControllableYet, text2));
				}
				MessageBox.Show(text3, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			if (requiresFanLed && !SupportsLiquidCoolingFanLighting(route))
			{
				MessageBox.Show(Strings.LcEffectNeedsFanLed, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			Color color2 = color ?? CurrentLiquidCoolingLightColor();
			LiquidCoolingControlRoute liquidCoolingControlRoute = route;
			bool flag4;
			if (liquidCoolingControlRoute != LiquidCoolingControlRoute.Gcu)
			{
				if (liquidCoolingControlRoute == LiquidCoolingControlRoute.DirectBle)
				{
					WaterCoolerBle ble = Program.ble;
					if (ble != null && ble.IsConnected)
					{
						flag4 = await ble.ApplyLightProfileAsync(profile, color2);
						goto IL_01e9;
					}
				}
			}
			else if (Program.service != null)
			{
				flag4 = await Program.service.LcApplyLightProfile(profile, color2, persist: true, requireReadback: true);
				goto IL_01e9;
			}
			flag4 = false;
			goto IL_01e9;
			IL_01e9:
			bool ok = flag4;
			if (ok && route == LiquidCoolingControlRoute.Gcu)
			{
				await RequestGcuLiquidCoolingStatusAsync(force: true);
			}
			RButton rButton3 = lcStatus;
			string text4;
			if (ok)
			{
				if (route == LiquidCoolingControlRoute.Gcu && (Program.hw?.LcLightingStatusSeen ?? false))
				{
					text4 = Strings.LcEffectConfirmed + text5;
				}
				else
				{
					text4 = ((route == LiquidCoolingControlRoute.Gcu) ? (Strings.LcEffectSent + text5) : (Strings.LcEffectApplied + text5));
				}
			}
			else
			{
				text4 = Strings.LcEffectWriteFailed;
			}
			rButton3.Text = text4;
		}
		async Task<bool> ApplyQuickSwitchAsync(CheckBox box, Func<bool, Task<bool>> command)
		{
			bool requested = box.Checked;
			bool hadFocus = box.Focused;
			// ⭐ 2026-10-16（0.291.8.1 · 查「翼龙15Pro 快捷开关/更多开关点了亮了又熄灭」）：
			// 本方法原来**一行日志都没有** ⇒ 诊断包里看不出"到底写没写进去、回读是什么"。
			// 现在把「键 / 期望值 / 下发结果 / 立刻回读值」四件套一次记全 ⇒ 下次诊断包可直接定性：
			//   · 下发 return false        ⇒ 路径/能力位问题（根本没发出去）
			//   · 下发 true 但回读仍旧值   ⇒ ⭐ 硬件没回显（12 秒后被 UpdateQuickSwitches 拉回去 = 用户看到的"熄灭"）
			//   · 下发 true 且回读新值     ⇒ 正常
			string diagKey = box.Tag as string;
			Logger.WriteLine($"[QS-apply] 键={diagKey} 期望={requested} 起始回读={ReadQuickSwitchState(diagKey)?.ToString() ?? "null"} 下发前的 hw 值={(Program.hw != null && diagKey != null && Program.hw.QuickSwitches.TryGetValue(diagKey, out var beforeVal) ? beforeVal.ToString() : "n/a")}");
			if (hadFocus)
			{
				Control parent = box.Parent;
				if (parent != null && parent.CanFocus)
				{
					parent.Focus();
				}
			}
			box.Enabled = false;
			// 2026-10-12（修「第三个磁贴点不动」）：忙碌窗口**成对**打标记；解除在 finally ⇒ 无论成功/失败/
			// 异常/被丢弃，磁贴都会立刻恢复可点（旧写法只在 box.Enabled=true 处收尾，⛔ 没有任何一处重算磁贴）。
			SetQuickSwitchInFlight(box, inFlight: true);
			bool confirmed = false;
			try
			{
				try
				{
					confirmed = await command(requested);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Quick switch failed: " + ex.Message);
					ToastForm.ShowFailure(Strings.QuickSwitchFailed);
				}
				if (!confirmed)
				{
					// ⭐⭐ 2026-10-18（机主 UI 体验）：⭐ 强冷确认窗口结束仍未达 ⇒ **这时才**回滚 UI
					//   （写失败/明确回读不符）；⛔ 轮询期间不提前回滚 ✅
					if (string.Equals(diagKey, "fanboost", StringComparison.Ordinal))
					{
						_fanBoostOptimisticUntil = DateTime.MinValue;
						Logger.WriteLine("[fan] 强冷：确认失败 ⇒ UI 回滚");
					}
					bool flag4 = ReadQuickSwitchState((string)box.Tag) ?? (!requested);
					_syncingSwitches = true;
					try
					{
						box.Checked = flag4;
					}
					finally
					{
						_syncingSwitches = false;
					}
				}
			}
			finally
			{
				box.Enabled = true;
				SetQuickSwitchInFlight(box, inFlight: false);
			}
			// ⭐ 2026-10-16（0.291.8.1）：收尾再记一次 —— 下发结果 + **下发后立刻回读**。
			// 这一行是判定"亮了又熄灭"根因的关键：若这里 confirmed=True 而回读仍是旧值，
			// 说明硬件/GCU 没有回显我们写下去的状态 ⇒ 12 秒宽限一过，UpdateQuickSwitches 就会把它拨回去。
			Logger.WriteLine($"[QS-apply] 键={diagKey} 下发结果={confirmed} 收尾回读={ReadQuickSwitchState(diagKey)?.ToString() ?? "null"} hw 值={(Program.hw != null && diagKey != null && Program.hw.QuickSwitches.TryGetValue(diagKey, out var afterVal) ? afterVal.ToString() : "n/a")}" + (confirmed ? "" : " ⚠️ 下发被拒 ⇒ 已按真实状态回滚界面"));
			if (hadFocus && !box.Focused && box.CanFocus)
			{
				box.Focus();
			}
			return confirmed;
		}
		async Task AutoConnectLc(bool silent)
		{
			if (_lcConnecting)
			{
				return;
			}
			_lcConnecting = true;
			if (!silent)
			{
				lcStatus.Text = Strings.LcConnectingBox;
				lcStatus.ForeColor = UiVisualStyle.Warn;
			}
			try
			{
				await ProbeSystemBluetoothConnectionAsync(force: true);
				if (GetLiquidCoolingRoute() == LiquidCoolingControlRoute.Gcu)
				{
					await RestoreSavedGcuLightingAsync();
					lcStatus.Text = Strings.LcGcuConnected;
					lcStatus.ForeColor = UiVisualStyle.Ok;
				}
				else
				{
					MechrevoHw hw = Program.hw;
					// 2026-10-15（C-1 第 2 步分类 ①）：flag4 = 「GCU 可控」（决定液冷走 GCU 还是 BLE）⇒ 真正需要 GCU。
					bool flag4 = hw != null && hw.IsGcuConnected && Program.service != null;
					if (flag4)
					{
						flag4 = await TryConnectGcuLiquidCoolingAsync(TimeSpan.FromSeconds(20L));
					}
					if (flag4)
					{
						await RestoreSavedGcuLightingAsync();
						lcStatus.Text = Strings.LcGcuConnected;
						lcStatus.ForeColor = UiVisualStyle.Ok;
					}
					else if (LiquidCoolingConnectionPolicy.ShouldSkipDirectFallbackBecauseGcuHoldsRadio(Program.hw?.LcConnectString ?? ""))
					{
						if (!silent)
						{
							lcStatus.Text = Strings.LcGcuConnecting;
							lcStatus.ForeColor = UiVisualStyle.Warn;
						}
					}
					else
					{
						bool gcuReportedLiquidCooling = Program.hw?.LcStatusSeen ?? false;
						bool systemAlreadyConnected = _lcSystemBluetoothObservation.IsConnected;
						flag4 = LiquidCoolingConnectionPolicy.ShouldUseAutomaticDirectFallback(Program.ble?.IsConnected ?? false);
						if (flag4)
						{
							flag4 = await TryDirectLiquidCoolingAsync();
						}
						if (!flag4 && !silent)
						{
							lcStatus.Text = ((gcuReportedLiquidCooling | systemAlreadyConnected) ? Strings.LcConnectFailedBusy : Strings.LcNotFoundRetry);
							lcStatus.ForeColor = UiVisualStyle.Warn;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("AutoConnectLc fail: " + ex.Message);
				if (!silent)
				{
					lcStatus.Text = Strings.LcConnectFailedRetry;
					lcStatus.ForeColor = UiVisualStyle.Danger;
				}
			}
			finally
			{
				_lcConnecting = false;
			}
		}
		static int CurrentCoolingTemperature()
		{
			MechrevoHw hw = Program.hw;
			if (hw == null)
			{
				return 0;
			}
			return LiquidCoolingAutoPolicy.ResolveCoolingTemperature(hw.CpuTemp, hw.GpuTemp, hw.IsTemperatureFresh(LiquidCoolingAutoPolicy.TemperatureFreshness, -1L));
		}
		Color CurrentLiquidCoolingLightColor()
		{
			if (GetLiquidCoolingRoute() == LiquidCoolingControlRoute.Gcu)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.LcLedRed >= 0 && hw.LcLedGreen >= 0 && hw.LcLedBlue >= 0)
				{
					return Color.FromArgb(Math.Clamp(hw.LcLedRed, 0, 255), Math.Clamp(hw.LcLedGreen, 0, 255), Math.Clamp(hw.LcLedBlue, 0, 255));
				}
			}
			return Program.ble?.SavedLightColor ?? Color.FromArgb(AppConfig.Get("lc_light_color", Color.FromArgb(0, 255, 255).ToArgb()));
		}
		int DHead(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		static string DescribeGcuLiquidCoolingDetail()
		{
			MechrevoHw hw = Program.hw;
			if (hw == null || !hw.LcStatusSeen)
			{
				return "";
			}
			string text2 = DescribeGcuLiquidCoolingState();
			string text3 = (string.IsNullOrWhiteSpace(hw.LcCurrentMac) ? hw.LcFwVersion : hw.LcCurrentMac);
			if (!string.IsNullOrWhiteSpace(text3))
			{
				return text2 + " · " + text3;
			}
			return text2;
		}
		static string DescribeGcuLiquidCoolingState()
		{
			MechrevoHw hw = Program.hw;
			if (hw == null || !hw.LcStatusSeen)
			{
				return "";
			}
			string text2 = hw.LcConnectString switch
			{
				"DeviceNotReady" => Strings.LcDeviceNotReady, 
				"DeviceIsReady" => Strings.LcDeviceIsReady, 
				"Scanning" => Strings.LcScanning, 
				"Connecting" => Strings.LcConnecting, 
				"IsConnectable" => Strings.LcWaitConnectable, 
				"Connected" => Strings.Connected, 
				"Disconnected" => Strings.Disconnected, 
				_ => string.IsNullOrWhiteSpace(hw.LcConnectString) ? Strings.LcWaitingStatus : hw.LcConnectString, 
			};
			if (hw.LcConnected && hw.LcActionSupportReported && !hw.LcActionSupported)
			{
				text2 += Strings.ReadOnlySuffix;
			}
			return Strings.LcGcuWaterCoolingPrefix + text2;
		}
		static int FindProfileIndex(ComboBox combo, int profile)
		{
			for (int k = 0; k < combo.Items.Count; k++)
			{
				if (combo.Items[k] is KeyValuePair<string, int> keyValuePair && keyValuePair.Value == profile)
				{
					return k;
				}
			}
			return 0;
		}
		void FitQuickGroups()
		{
			if (fittingQuickGroups || panel.IsDisposed)
			{
				return;
			}
			fittingQuickGroups = true;
			try
			{
				int width = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal);
				foreach (FlowLayoutPanel item6 in groupGrids.Values.Distinct())
				{
					item6.Width = width;
					item6.Height = item6.GetPreferredSize(new Size(width, 0)).Height;
				}
				root.PerformLayout();
				panel.Height = root.Bottom + panel.Padding.Bottom;
			}
			finally
			{
				fittingQuickGroups = false;
			}
		}
		LiquidCoolingControlRoute GetLiquidCoolingRoute()
		{
			MechrevoHw hw = Program.hw;
			bool gcuControllable = hw != null && hw.LcGcuControllable && hw.IsLcStatusFresh(TimeSpan.FromSeconds(15L), -1L);
			return LiquidCoolingConnectionPolicy.ResolveRoute(Program.ble?.IsConnected ?? false, gcuControllable, _lcSystemBluetoothObservation.IsConnected);
		}
		FlowLayoutPanel GroupOf(string text2)
		{
			if (!groupGrids.TryGetValue(text2, out FlowLayoutPanel value))
			{
				throw new InvalidOperationException("快捷开关 \"" + text2 + "\" 没有归组（见 groupMembership）");
			}
			return value;
		}
		Label MakeRowName(string name)
		{
			return new Label
			{
				Text = name,
				AutoSize = true,
				Dock = DockStyle.Fill,
				ForeColor = UiVisualStyle.Muted,
				BackColor = lcPanel.BackColor,
				Font = UiStyleCaptionFont(),
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty
			};
		}
		async Task ProbeSystemBluetoothConnectionAsync(bool force = false)
		{
			if (Program.ble == null)
			{
				_lcSystemBluetoothObservation = default;
			}
			else
			{
				SystemBluetoothConnectionObservation lcSystemBluetoothObservation = await Program.ble.ProbeSystemConnectionAsync(Program.hw?.LcDeviceMacs, force);
				_lcSystemBluetoothObservation = lcSystemBluetoothObservation;
			}
		}
		async Task<bool> RequestGcuLiquidCoolingStatusAsync(bool force = false)
		{
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					if (!force && DateTime.UtcNow - _lcLastGcuStatusRequest < TimeSpan.FromSeconds(6L))
					{
						return true;
					}
					_lcLastGcuStatusRequest = DateTime.UtcNow;
					return await Program.service.RefreshLiquidCoolingStatus();
				}
			}
			return false;
		}
		async Task RestoreSavedGcuLightingAsync()
		{
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					string profile = AppConfig.GetString("lc_light_profile", "") ?? "";
					if (LiquidCoolingConnectionPolicy.ShouldRestoreSavedGcuLighting(Program.ble?.IsConnected ?? false, hw.LcGcuControllable, hw.IsLcStatusFresh(TimeSpan.FromSeconds(15L), -1L), profile))
					{
						int generation = hw.ConnectionGeneration;
						if (_lcGcuRestoreGeneration != generation)
						{
							_lcGcuRestoreGeneration = generation;
							_lcGcuRestoreAttempts = 0;
							_lcGcuLastRestoreAttempt = DateTime.MinValue;
						}
						if (_lcGcuRestoreAttempts < 2 && !(DateTime.UtcNow - _lcGcuLastRestoreAttempt < TimeSpan.FromSeconds(10L)))
						{
							string text2 = profile;
							bool flag4 = ((text2 == "fan_rotate" || text2 == "fan_rainbow") ? true : false);
							if (flag4 && !hw.LcFanLightingSupported)
							{
								_lcGcuRestoreAttempts = 2;
								Logger.WriteLine("GCU 液冷灯效恢复跳过：当前设备不支持风扇灯效，profile=" + profile);
							}
							else
							{
								_lcGcuRestoreAttempts++;
								_lcGcuLastRestoreAttempt = DateTime.UtcNow;
								Color color = Color.FromArgb(AppConfig.Get("lc_light_color", Color.FromArgb(0, 255, 255).ToArgb()));
								bool flag5 = await Program.service.LcApplyLightProfile(profile, color, persist: false, requireReadback: true);
								if (flag5)
								{
									_lcGcuRestoreAttempts = 2;
								}
								Logger.WriteLine($"GCU 液冷灯效恢复：generation={generation}, profile={profile}, sent={flag5}, attempt={_lcGcuRestoreAttempts}");
							}
						}
					}
				}
			}
		}
		static bool SupportsLiquidCoolingFanLighting(LiquidCoolingControlRoute route)		{
			switch (route)
			{
			case LiquidCoolingControlRoute.DirectBle:
			{
				WaterCoolerBle ble = Program.ble;
				return ble != null && ble.IsConnected && ble.SupportsFanLed;
			}
			case LiquidCoolingControlRoute.Gcu:
				return Program.hw?.LcFanLightingSupported ?? false;
			default:
				return false;
			}
		}
		async Task<bool> TryConnectGcuLiquidCoolingAsync(TimeSpan timeout)
		{
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected)
				{
					long statusBeforeRequest = hw.LcStatusVersion;
					await RequestGcuLiquidCoolingStatusAsync(force: true);
					if (hw.LcGcuControllable && hw.LcStatusVersion > statusBeforeRequest)
					{
						return true;
					}
					if (hw.LcActionSupportReported && !hw.LcActionSupported)
					{
						return false;
					}
					if (!(await Program.service.LcConnect()))
					{
						return false;
					}
					await RequestGcuLiquidCoolingStatusAsync(force: true);
					return await hw.WaitForStateAsync(() => hw.LcGcuControllable && hw.LcStatusVersion > statusBeforeRequest, timeout);
				}
			}
			return false;
		}
		async Task<bool> TryDirectLiquidCoolingAsync()
		{
			bool flag4 = Program.ble != null;
			if (flag4)
			{
				flag4 = await Program.ble.AutoConnectAsync();
			}
			if (!flag4)
			{
				return false;
			}
			await Program.ble.WaitForStartupStabilizationAsync(TimeSpan.FromSeconds(8L));
			await Program.ble.RestoreSavedSettingsAsync(CurrentCoolingTemperature());
			_lcWasConnected = true;
			_lcReconnectAttempts = 0;
			lcStatus.Text = Strings.LcDirectConnected;
			toolTip.SetToolTip(lcStatus, Strings.LcDirectConnected + " " + Program.ble.ConnectedName);
			lcStatus.ForeColor = UiVisualStyle.Ok;
			return true;
		}
		static async Task UpdateGcuAutomaticCoolingAsync()
		{
			if (Program.service != null)
			{
				MechrevoHw hw = Program.hw;
				if (hw != null && hw.IsConnected && LiquidCoolingAutoPolicy.DecideGcuRefresh(AppConfig.Get("lc_pump_profile", -2) == -1, AppConfig.Get("lc_fan_profile", -2) == -1, hw.LcFanControl) == LiquidCoolingAutoPolicy.RefreshAction.EnableVendorAuto)
				{
					await Program.service.SwitchLcFanAuto();
				}
			}
		}
	}

	private void SetQuickSleepAc(bool ac)
	{
		if (_quickSleepAc == ac)
		{
			return;
		}
		_quickSleepAc = ac;
		SetThemeButtonState(_quickSleepAcBtn, ac);
		SetThemeButtonState(_quickSleepDcBtn, !ac);
		if (_quickSwitches != null)
		{
			_syncingSwitches = true;
			try
			{
				CheckBox[] quickSwitches = _quickSwitches;
				for (int num = 0; num < quickSwitches.Length; num++)
				{
					string? key = quickSwitches[num].Tag as string;
					if (key != null && key.StartsWith("sleep"))
					{
						bool? sleepState = PowerSleepGuard.ReadEnabled(key, ac);
						if (sleepState.HasValue)
						{
							quickSwitches[num].Checked = sleepState.Value;
						}
					}
				}
			}
			finally
			{
				_syncingSwitches = false;
			}
		}
	}

	private static string SleepTipFor(string key)
	{
		return key switch
		{
			"sleepnetoff" => Strings.QuickSleepNetOffTip,
			"sleepaway" => Strings.QuickSleepAwayTip,
			"sleepsysreq" => Strings.QuickSleepSysReqTip,
			"sleeprtcwake" => Strings.QuickSleepRtcWakeTip,
			"sleepdeep" => Strings.QuickSleepDeepTip,
			"sleephibernate" => Strings.QuickSleepHibernateTip,
			_ => ""
		};
	}

	private void UpdateQuickSwitches()
	{
		if (_quickSwitches.Length == 0 || (DateTime.Now - _lastQuickSwitchUi).TotalSeconds < 12.0)
		{
			return;
		}
		_syncingSwitches = true;
		try
		{
			CheckBox[] quickSwitches = _quickSwitches;
			foreach (CheckBox checkBox in quickSwitches)
			{
				string text = (string)checkBox.Tag;
				// 2026-10-10（修用户反馈「打开或者关闭会卡一会」）：winupdate 动作进行中不得用
				// IsDisabled 覆盖界面 —— 该键跳过本轮刷新，等动作结束（_winupdateBusy=false +
				// _lastQuickSwitchUi 宽限）后再由本定时器按真实状态收敛。
				if (text == "winupdate" && _winupdateBusy)
				{
					continue;
				}
				// ⭐⭐ 2026-10-18（机主 UI 体验）：⭐ 强冷确认窗口内保持**乐观置**的勾选态，
				//   ⛔ 不按 `FanBoostActive` 提前拨回；只有最终确认失败才在 ApplyQuickSwitchAsync 里显式回滚 ✅
				if (text == "fanboost" && DateTime.Now < _fanBoostOptimisticUntil)
				{
					checkBox.Checked = _fanBoostOptimisticValue;
					continue;
				}
				bool? flag = ReadWindowsPersonalizationSwitch(text);
				if (flag.HasValue)
				{
					checkBox.Checked = flag.Value;
				}
				else if (text.StartsWith("sleep"))
				{
					bool? sleepState = PowerSleepGuard.ReadEnabled(text, _quickSleepAc);
					if (sleepState.HasValue)
					{
						checkBox.Checked = sleepState.Value;
					}
				}
				else if (text == "micmute")
				{
					// 2026-10-11（麦克风静音开关）：它的状态**不在** hw.QuickSwitches 里（那是 EC/GCU 通道），
					// 而在 Windows CoreAudio 侧 ⇒ 与「更多开关」共用同一个读回入口 ReadQuickSwitchState
					//（磁贴行的刷新也读它 ⇒ 两侧同一来源，⛔ 不会各读一套）。
					bool? micState = ReadQuickSwitchState(text);
					if (micState.HasValue)
					{
						checkBox.Checked = micState.Value;
					}
				}
				else if (text == "capslock" || (text == "numpad" && (Program.hw?.UsesBaolongda ?? false)))
				{
					// ⭐ 2026-10-17（大小写 / 数字小键盘）：这两个是**键盘自身的锁定键**，状态由
					// Windows 的键盘状态机持有 ⇒ **直读、不缓存**，与写入侧（`NativeLockKey.TrySet`）
					// 同源 ✅（同一组 user32 API，⛔ 不会各读一套）。
					// ⚠️ `numpad` **只在宝龙达**走这里；老 GCU 机型仍读 `hw.QuickSwitches`
					//    （那是 GCU 上报的 `NumPad` 字段）⇒ ⛔ 不能覆盖，逐位不变。
					bool lockedNow = string.Equals(text, "capslock", StringComparison.Ordinal)
						? MechrevoLite.Helpers.NativeLockKey.IsCapsLockOn()
						: MechrevoLite.Helpers.NativeLockKey.IsNumLockOn();
					// ⚠️ **每个键首次必打**一行（`_lockKeyLoggedOnce`），之后只在值要被改掉时才打
					//     —— 否则这条读路径静默生效，日志里与"根本没跑"无法区分 ✅
					bool firstLockSync = _lockKeyLoggedOnce.Add(text);
					if (firstLockSync || checkBox.Checked != lockedNow)
					{
						Logger.WriteLine($"[QS-lock] 直读系统锁定键：键={text} 界面值={checkBox.Checked} -> 实际={MechrevoLite.Helpers.NativeLockKey.Describe(lockedNow)}"
							+ (firstLockSync ? "（首次读取 · 证明本读路径已生效）" : ""));
					}
					checkBox.Checked = lockedNow;
				}
				else
				{
					if (Program.hw == null || !Program.hw.QuickSwitches.TryGetValue(text, out var value))
					{
						continue;
					}
					if (text == "deepsleep" && _deepSleepPendingValue >= 0)
					{
						if (value == (_deepSleepPendingValue == 1))
						{
							SetDeepSleepPending(-1);
						}
						else
						{
							checkBox.Checked = _deepSleepPendingValue == 1;
						}
					}
					else
					{
						// ⭐ 2026-10-16（0.291.8.1）：只在**界面值要被改掉**时记一行 ——
						// 这就是用户看到的"亮了又熄灭"的那一拍（宽限期过后按 hw 真值回拨）。
						// ⛔ 值相同不记，避免每 12 秒刷屏。
						if (checkBox.Checked != value)
						{
							Logger.WriteLine($"[QS-refresh] 按 hw 真值回拨：键={text} 界面值={checkBox.Checked} -> hw 值={value}（12 秒宽限已过；若刚点过这个键，说明硬件/GCU 没回显我们写下去的状态）");
						}
						checkBox.Checked = value;
					}
				}
			}
			if (_usbChargerBox != null && Program.hw != null)
			{
				_usbChargerBox.Checked = Program.hw.UsbCharger;
			}
			if (_fanBoostBox != null && Program.hw != null)
			{
				// ⭐ 2026-10-19（强冷核对 ① 可见 + ② 状态真实）：宝龙达的强冷状态在
				//   `MechrevoHw.FanBoostActive`（本机通道：功能号 21 满转 + 13 回读）；
				//   `hw.FanBoost`（GCU 上报字段）在宝龙达上**恒 false** ⇒ 原来 12 秒宽限一过
				//   定时器就把开关拨回去（用户看到的"点了又弹回"）✅
				//   ⚠️ 非宝龙达逐位不变（仍读 hw.FanBoost）✅
				// ⭐⭐ 2026-10-18（机主 UI 体验）：⭐ 强冷确认窗口（最多 10 s）内保持**乐观置**的勾选态，
				//   ⛔ 不被周期刷新拨回；最终失败路径会显式回滚并清乐观窗口 ✅
				if (DateTime.Now < _fanBoostOptimisticUntil)
				{
					_fanBoostBox.Checked = _fanBoostOptimisticValue;
				}
				else
				{
					_fanBoostBox.Checked = Program.hw.UsesBaolongda ? MechrevoHw.FanBoostActive : Program.hw.FanBoost;
				}
			}
		}
		finally
		{
			_syncingSwitches = false;
		}
		RefreshQuickGroupSummary();
		// 2026-10-11（磁贴行 · 同源）：同一次定时刷新里把磁贴高亮一起收敛 —— 与「更多开关」读的是
		// 同一批 ReadQuickSwitchState 结果（上面那些 checkBox.Checked 的最终值），⛔ 不另读一套状态。
		RefreshQuickTiles();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（宝龙达 Logo 灯「UI 与硬件状态不同步」）：⭐ **立刻**按真实状态刷新一次开关界面 ✅
	/// <para>⚠️ 根因（机主诊断包 0.291.9.11 实证）：启动「灯效恢复（单次下发）」用 WMI 功能号 15
	/// 把 LOGO 灯点亮了，但界面那颗 LOGO 开关（<c>_logoPowerSw</c>，属于「灯光」折叠组、
	/// **不在** `_quickSwitches` 里）只有在 <see cref="RefreshDeviceCapabilities" /> 的能力布局**发生变化**
	/// 时才会经 <see cref="SyncLightRows" /> 刷新 ⚠️ ⇒ 启动恢复之后**没有任何路径**把它拉回真值
	/// ⇒ 机主看到「硬件实际是开、界面显示关」❌。</para>
	/// <para>⭐ 本方法供"状态刚刚被程序自己改掉"的路径（启动灯效恢复 ／ 宝龙达 WMI 电源下发）调用：
	/// ⭐ 灯光行**立刻**收敛（<see cref="SyncLightRows" />）；⭐「更多开关」仍走
	/// <see cref="UpdateQuickSwitches" /> 自己的 12 秒宽限（⛔ 不覆盖用户刚点的值 ✅）。</para>
	/// </summary>
	internal void RefreshQuickSwitchStatesNow()
	{
		if (IsDisposed || Disposing)
		{
			return;
		}
		try
		{
			// ⭐ 灯光行（键盘 ／ 灯带 ／ LOGO ／ 铰链 ／ 同步）—— 原来没有周期/事件消费 ⚠️
			SyncLightRows();
			// ⭐ 「更多开关」—— 自带 12 秒宽限（用户刚点过就跳过）⇒ 宽限期逻辑对刚下发的键仍然生效 ✅
			UpdateQuickSwitches();
			RefreshQuickGroupSummary();
			RefreshQuickTiles();
		}
		catch (Exception ex)
		{
			// 恢复路径是**非 UI 线程**调过来的，这里吞掉异常，⛔ 不让一次刷新失败影响灯效恢复本身 ✅
			Logger.WriteLine("[logo] 立即刷新开关界面失败（已吞掉，不影响别处）：" + ex.Message);
		}
	}

	/// <summary>
	/// 把一颗快捷开关的勾选态**程序化**拨回去，且不触发它自己的下发回调。
	/// 2026-10-11：从构造函数里的局部函数**提级**为实例方法（判据/语义一字未改）——
	/// 因为「深度睡眠」的动作体被抽成了 <see cref="ApplyDeepSleepSwitchAsync" />（磁贴与 CheckBox 共用），
	/// 那个方法在构造函数作用域之外，必须能调到同一个实现。
	/// </summary>
	private void RevertCheck(CheckBox box, bool value)
	{
		_syncingSwitches = true;
		try
		{
			box.Checked = value;
		}
		finally
		{
			_syncingSwitches = false;
		}
	}

	/// <summary>
	/// 「深度睡眠」快捷开关的动作体（2026-10-11 从 <c>cb.Click</c> 内联体**逐行**抽出，语义一字未改）：
	/// 现在它同时是「更多开关」里那颗 CheckBox 的 Click 处理器**和**磁贴（<c>deepsleep</c> = 用户口径的「息屏」）
	/// 点击的唯一实现 ⇒ 「点磁贴 = 勾选/取消同一个开关」在代码结构上成立。
	/// ⚠️ <paramref name="cb" /> 的 <c>Checked</c> 必须已经是"用户想要的目标值"（CheckBox.Click 先拨再抛；
	/// 磁贴侧由 <see cref="ToggleQuickTileAsync" /> 先赋值再调本方法）。
	/// </summary>
	private async Task ApplyDeepSleepSwitchAsync(CheckBox cb, string key)
	{
		if (_syncingSwitches)
		{
			return;
		}
		bool requested = cb.Checked;
		if (!NativeMethods.HasFreshUserInput())
		{
			Logger.WriteLine("Deep sleep switch ignored: no fresh user input.");
			RevertCheck(cb, (_deepSleepPendingValue >= 0) ? (_deepSleepPendingValue == 1) : (ReadQuickSwitchState(key) == true));
			return;
		}
		_lastQuickSwitchUi = DateTime.Now;
		MechrevoService service = Program.service;
		if (service == null)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			return;
		}
		bool hadFocus = cb.Focused;
		if (hadFocus)
		{
			Control parent = cb.Parent;
			if (parent != null && parent.CanFocus)
			{
				parent.Focus();
			}
		}
		cb.Enabled = false;
		// 2026-10-12（修「第三个磁贴点不动」）：忙碌窗口成对打标记，解除在 finally。
		SetQuickSwitchInFlight(cb, inFlight: true);
		bool confirmed = false;
		try
		{
			confirmed = await service.SwitchDeepSleep(requested);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Deep sleep switch failed: " + ex.Message);
		}
		finally
		{
			cb.Enabled = true;
			SetQuickSwitchInFlight(cb, inFlight: false);
		}
		if (hadFocus && !cb.Focused && cb.CanFocus)
		{
			cb.Focus();
		}
		SetDeepSleepPending(requested ? 1 : 0);
		RevertCheck(cb, requested);
		Logger.WriteLine($"SwitchDeepSleep({requested}) confirmed={confirmed}; 重启后生效");
		if (!Program.UiAuditMode)
		{
			MessageBox.Show(Strings.DeepSleepResult(requested), "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	/// <summary>
	/// 「关闭屏幕（不睡眠）」快捷开关的动作体（2026-10-11 从 <c>cb.Click</c> 内联体**逐行**抽出）。
	/// 一次性语义：进入前要求 <c>cb.Checked == true</c>，收尾由 finally 取消勾选。
	/// 磁贴侧（只有用户从「＋」面板把它选成磁贴时才会走到）先 <c>Checked = true</c> 再调本方法。
	/// </summary>
	private async Task ApplyQuickMonitorOffAsync(CheckBox cb)
	{
		if (!cb.Checked)
		{
			return;
		}
		if (_syncingSwitches)
		{
			// 动作进行中又被点了一下：CheckBox.OnClick 是**先拨 Checked 再抛 Click**，所以这一次点击已经
			// 把勾选态拨成了 true —— 这里只把它拨回去，不重入动作（否则会重复下发，或卡在"已勾选"）。
			// ⚠️ 有意**不置灰**（Enabled 保持 true）：本类修的就是"点了没反应"，灰色控件外观更像"点不动"；
			//    重入由本分支 + 顶部 _syncingSwitches 判定挡住，够用。
			cb.Checked = false;
			return;
		}
		try
		{
			_syncingSwitches = true;
			// ⚠️ 故意不写可空注解（`?`）：本文件没有 #nullable 上下文，写注解只会多一条 CS8632，
			//    而本仓库的警告预算是硬上限；语义等价（只判 != null）。
			string screenBlankNotice = null;
			try
			{
				// 让"已勾选"这一帧先真正画出来（否则用户看不到任何反应）。
				await Task.Delay(MonitorOffSwitchVisibleMs);
				// 同步检查（驱动组件存在性/重入）→ 返回 null 表示已启动；非 null 为失败原因。
				string startError = VirtualDisplayBlank.Start();
				if (startError != null)
				{
					Logger.WriteLine("SCREEN_BLANK_FAILED: " + startError);
					screenBlankNotice = Strings.ScreenBlankFailed + startError;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Screen blank failed: " + ex.Message);
				screenBlankNotice = Strings.ScreenBlankFailed + ex.Message;
			}
			finally
			{
				try
				{
					cb.Checked = false;
				}
				finally
				{
					_syncingSwitches = false;
				}
			}
			if (screenBlankNotice != null && !Program.UiAuditMode)
			{
				MessageBox.Show(screenBlankNotice, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
		}
		catch (Exception ex2)
		{
			// async void 里漏出去的异常会直接杀掉进程 ⇒ 这里兜住（上面的 finally 已经把状态复位）。
			Logger.WriteLine("Screen blank click handler failed: " + ex2.Message);
		}
	}

	private void RefreshQuickGroupSummary()
	{
		if (_quickGroup != null && _quickSwitches.Length != 0)
		{
			int value = _quickSwitches.Count((CheckBox cb) => cb.Checked);
			_quickGroup.Summary = string.Format(Strings.QuickSwitchesOn, value, _quickSwitches.Length);
		}
	}

	// ==========================================================================================
	// 2026-10-11（本轮任务 · 快捷磁贴行）
	//   用户原话：「参考这个 在性能模式下面加一个快捷按钮…」「关闭屏幕那个按钮的位置改成加号按钮」
	//             「准确描述位置:性能模式按钮的下面一行（30系要在配置下拉的下面一行）」
	//   用户对四个阻塞点的答复②：「同源：点磁贴 = 勾选/取消同一个开关」
	//                            「磁贴高亮 <-> 该开关为开；从任一侧改，另一侧同步刷新」
	//
	// ⚠️ 同源怎么保证（**代码结构**层面，不是靠"再读一遍同样的值"）：
	//   ① 磁贴**只有一个身份** —— 它所引用的「更多开关」里那颗 CheckBox（Tag = key，见 FindQuickSwitch）；
	//   ② 磁贴的**读**：高亮态直接取那颗 CheckBox 的 Checked；取不到时才退回 ReadQuickSwitchState(key)
	//      （与 UpdateQuickSwitches 刷 CheckBox 用的是**同一个函数**）；
	//   ③ 磁贴的**写**：不直接调 service，而是让那颗 CheckBox 自己走它既有的下发路径
	//      （CheckedChanged 或抽出来的 ApplyDeepSleepSwitchAsync / ApplyQuickMonitorOffAsync）；
	//   ④ 每一轮定时刷新 UpdateQuickSwitches 结束时统一调一次 RefreshQuickTiles ⇒ 无论从哪一侧改，
	//      另一侧在同一帧收敛。
	// ==========================================================================================

	/// <summary>按 key 取「更多开关」里那颗开关（磁贴的唯一身份来源）。</summary>
	private CheckBox FindQuickSwitch(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}
		CheckBox[] switches = _quickSwitches;
		for (int i = 0; i < switches.Length; i++)
		{
			if (string.Equals(switches[i].Tag as string, key, StringComparison.OrdinalIgnoreCase))
			{
				return switches[i];
			}
		}
		return null;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：
	/// ⭐ 宝龙达下 `webcam` 现在是**真正禁用/启用摄像头设备**（SetupAPI PnP，见 `CameraPnpControl`）
	/// ⇒ 文案恢复成「摄像头」✅；
	/// ⚠️ 只有本机枚举不到可控制摄像头设备、退回旧隐私键兜底时，才必须如实写「**摄像头隐私**」
	/// （两者语义不同：一个是设备级禁用，一个只是系统隐私授权 ⚠️）。
	/// <para>⚠️ 非宝龙达仍用 <see cref="Strings.QuickCamera" />（GCU 的 `WEBCAM_ON/OFF` 是厂商级）⇒ 逐位不变 ✅</para>
	/// <para>⚠️ 用字面量而不是改 `Strings*.resx`：那个文件是**混合行尾**（CRLF 14 / 裸 LF 5343），
	/// 用编辑器改会整篇改行尾；本表里 `("wifi", "WiFi")` 就是既有先例 ✅</para>
	/// </summary>
	private static string QuickCameraLabel(string fallback)
	{
		if (!(Program.hw?.UsesBaolongda ?? false))
		{
			return fallback;
		}
		// ⭐ 有可控制的摄像头设备 ⇒ 走 PnP 设备级禁用/启用 ⇒ 文案「摄像头」；
		//   ⛔ 没有（例如只有 Windows Hello 红外相机被我们排除）⇒ 退回隐私键 ⇒ 文案「摄像头隐私」
		return CameraPnpControl.HasCamera ? fallback : Strings.QuickCameraPrivacy;
	}

	/// <summary>「更多开关」里该 key 的文案（提级后的 <see cref="QuickSwitchLabels" />，与那一组完全同源）。</summary>
	private static string QuickSwitchText(string key)
	{
		(string, string)[] labels = QuickSwitchLabels;
		for (int i = 0; i < labels.Length; i++)
		{
			if (string.Equals(labels[i].Item1, key, StringComparison.OrdinalIgnoreCase))
			{
				return string.Equals(labels[i].Item1, "webcam", StringComparison.OrdinalIgnoreCase)
					? QuickCameraLabel(labels[i].Item2)
					: labels[i].Item2;
			}
		}
		return key;
	}

	/// <summary>
	/// 磁贴文案：默认＝「更多开关」里同一个开关的文案（⛔ 不另造一套名字）；**用户点名**的例外只有一处：
	/// <list type="bullet">
	/// <item><c>micmute</c> ⇒ 「麦克风」—— 用户给的默认磁贴名就是这个（开关本身叫「麦克风静音」）；</item>
	/// <item><c>monitoroff</c> ⇒ 「息屏」—— 用户给的默认磁贴名；开关本身叫「息屏（不睡眠）」，
	/// 那 7 个字在 44px 高的窄磁贴里放不下，故磁贴取短名（**键与下发路径与那颗开关完全相同**）。</item>
	/// </list>
	/// ⚠️ 2026-10-12 更正：上一版把 <c>deepsleep</c> 显示成「息屏」是**错的** —— 它的开关文案是
	/// 「深度睡眠」，两个不同的开关被磁贴叫成同一个名字；现在 <c>deepsleep</c> 一律回落到它自己的
	/// 开关文案（「深度睡眠」）。
	/// </summary>
	private static string QuickTileText(string key)
	{
		if (string.Equals(key, "micmute", StringComparison.OrdinalIgnoreCase))
		{
			return Strings.QuickTileMicrophone;
		}
		if (string.Equals(key, "monitoroff", StringComparison.OrdinalIgnoreCase))
		{
			return Strings.QuickTileScreenOff;
		}
		// ⭐ 2026-10-17（摄像头 · 隐私开关）：磁贴只有 44px 高 ⇒ 用短名「摄像头」；
		//   ⚠️ 开关行仍写全「摄像头隐私」（语义不同，见 QuickCameraLabel 的注释）✅
		if (string.Equals(key, "webcam", StringComparison.OrdinalIgnoreCase) && (Program.hw?.UsesBaolongda ?? false))
		{
			return Strings.QuickCamera; // ⚠️ 磁贴短名复用既有本地化文案（⛔ 不硬编码）
		}
		return QuickSwitchText(key);
	}

	/// <summary>磁贴图标（图标在上、文字在下；只认形状，不看文字）。静音时麦克风图标带一道斜杠。</summary>
	private static UiGlyph.Kind QuickTileGlyph(string key, bool on)
	{
		switch (key)
		{
		case "fanboost":
			return UiGlyph.Kind.Fan;
		case "webcam":
			return UiGlyph.Kind.Camera;
		case "micmute":
			return on ? UiGlyph.Kind.MicrophoneMute : UiGlyph.Kind.Microphone;
		case "deepsleep":
			return UiGlyph.Kind.Moon;
		case "monitoroff":
			return UiGlyph.Kind.Display;
		case "winkey":
		case "fnkey":
		case "copilot":
		case "numpad":
		case "capslock":
			return UiGlyph.Kind.Keyboard;
		case "osd":
		case "taskbarautohide":
			return UiGlyph.Kind.Console;
		case "darktheme":
		case "transparency":
			return UiGlyph.Kind.Contrast;
		case "usb":
		case "acrecovery":
			return UiGlyph.Kind.Battery;
		case "highperf":
		case "cpuadvperf":
			return UiGlyph.Kind.Gauge;
		case "gamewhitelist":
			return UiGlyph.Kind.Package;
		case "winupdate":
			return UiGlyph.Kind.Refresh;
		// 2026-10-12（用户原话「还有快捷开关的WiFi和蓝牙能有自己的图标吗」）：这两颗此前**没有自己的 case**，
		// 落到下面的 default 复用通用「开关」字形（Toggles）；现在各给一枚自己的。
		case "wifi":
			return UiGlyph.Kind.Wifi;
		case "bt":
			return UiGlyph.Kind.Bluetooth;
		// 2026-10-12（用户原话「触摸板能不能有专属图标」）：touchpad 此前**没有自己的 case**，
		// 落到 default 复用通用「开关」字形（Toggles）；现在给它一枚专属触摸板字形。
		// ⚠️ `touchpadtoggle`（触摸板开关）本轮**未改**，仍走 default = Toggles。
		case "touchpad":
			return UiGlyph.Kind.Touchpad;
		// 2026-10-12（用户原话「触摸板切换键是干啥用的 也用触摸板图标吧」）：`touchpadtoggle` 与 `touchpad`
		// 共用同一枚触摸板字形（用户明确要求）；⛔ 行为/布局/排序/持久化一律未动，只加这一个字形映射。
		case "touchpadtoggle":
			return UiGlyph.Kind.Touchpad;
		default:
			return UiGlyph.Kind.Toggles;
		}
	}

	/// <summary>
	/// 磁贴 key 在当前机型上是否存在 —— 与「更多开关」**同一把尺子**（同一条判据表达式，
	/// 见 RefreshDeviceCapabilities 里算 <c>flag2</c> 的那一行）：审计模式 / hw 上报 / 固定能力项。
	/// 用户答复①：「若某机型不支持 ⇒ 按能力位隐藏或置灰，不要显示出点不动的开关」。
	/// ⚠️ 本方法是从 RefreshDeviceCapabilities 的局部函数 <c>StaticQuick</c> **提级**而来，判据一字未改。
	/// </summary>
	private bool StaticQuick(string key)
	{
		// ⭐ 2026-10-19（APU 防缩缸）：纯 Windows 电源策略，任何机型都可用 ⇒ 恒显示 ✅
		if (string.Equals(key, "apuboost", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		MechrevoDeviceCapabilities caps = (Program.hw?.Capabilities) ?? _deviceCapabilities;
		switch (key)
		{
		case "capslock":
			// ⭐ 2026-10-17（新增开关）：大小写锁定是**纯系统级**能力（Windows 键盘状态机），
			// 不依赖任何后端 ⇒ 所有机型都可用，恒 true。
			return true;
		case "numpad":
			// ⚠️ 老 GCU 机型：仍按能力位 `caps.Numpad`（与改动前逐字一致）。
			// ⭐ 宝龙达：没有 GCU ⇒ `caps.Numpad` 恒假 ⇒ 补一支（走模拟按键）。
			// ⚠️ 非宝龙达逐位不变。
			return caps.Numpad || (Program.hw?.UsesBaolongda ?? false);
		case "acrecovery":
			return caps.AcRecovery;
		case "fanboost":
			return caps.FanBoost;
		case "highperf":
			// ⭐⭐ 2026-10-17（「高性能电源」彻底解耦 GCU）：⭐ 它是**纯配置开关** ——
			//   ⭐ 打开 = 切档时由 LM 用 **Windows API** 设「电源模式」（powrprof overlay），
			//      ⛔ 不需要厂商能力位、也不发 MQTT ⇒ ⭐ 所有机型都该显示 ✅
			//   ⚠️ 原来只靠 `HighPerformanceSeen`（GCU 上报）⇒ 没上报的机器直接隐藏（机主截图就是这种）
			return true;
		case "monitoroff":
		case "taskbarautohide":
		case "transparency":
		case "darktheme":
		case "sleepnetoff":
		case "sleepaway":
		case "sleepsysreq":
		case "sleeprtcwake":
		case "sleepdeep":
		case "sleephibernate":
		case "winupdate":
			return true;
		default:
			return false;
		}
	}

	/// <summary>磁贴 key 是否在当前机型上可用（审计模式一律可用）。</summary>
	private bool QuickTileSupported(string key)
	{
		if (Program.UiAuditMode && !Program.UiAuditUseReportedCapabilities)
		{
			return true;
		}
		MechrevoHw hw = Program.hw;
		return ((hw != null && hw.SupportsQuickSwitch(key)) || StaticQuick(key)) ? true : false;
	}

	/// <summary>
	/// 读 <c>perf_quick_tiles</c>（逗号分隔的 key 列表；**键不存在** ⇒ 默认集 <see cref="DefaultQuickTileKeys" />，
	/// 值 = 哨兵「-」⇒ 显式空集）。顺序 = 配置里的顺序 = 磁贴顺序。
	/// ⚠️ 这里**只做去重与去空白**，⛔ 不做能力位过滤 —— 过滤在 <see cref="ApplyQuickTiles" /> 的渲染路径上，
	/// 这样"换机型后旧键仍在配置里"，换回去还能恢复（AppConfig 是单份 config.json，能力位是运行期判据）。
	/// </summary>
	private static List<string> LoadQuickTileKeys()
	{
		List<string> list = new List<string>();
		string raw = AppConfig.GetString(QuickTilesConfigKey);
		// 2026-10-12（0 个磁贴）：**显式空集**必须与"键缺省"分开 —— 否则"全不选"会被还原成默认集。
		if (IsExplicitEmptyQuickTiles(raw))
		{
			return list;
		}
		if (!string.IsNullOrWhiteSpace(raw))
		{
			string[] pieces = raw.Split(',');
			for (int i = 0; i < pieces.Length; i++)
			{
				string key = pieces[i].Trim();
				if (key.Length > 0 && !list.Contains(key, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(key);
				}
			}
		}
		if (list.Count == 0)
		{
			list.AddRange(DefaultQuickTileKeys);
		}
		return list;
	}

	private static void SaveQuickTileKeys(List<string> keys)
	{
		if (keys == null || keys.Count == 0)
		{
			// 2026-10-12（0 个磁贴）：写哨兵而不是空串/删键 —— 空串会被 LoadQuickTileKeys 当成"缺省 ⇒ 默认集"。
			AppConfig.Set(QuickTilesConfigKey, QuickTilesEmptySentinel);
			Logger.WriteLine($"[tiles] perf_quick_tiles = {QuickTilesEmptySentinel}（显式空集：磁贴行只留「自定义」）");
			return;
		}
		AppConfig.Set(QuickTilesConfigKey, string.Join(",", keys));
		Logger.WriteLine($"[tiles] perf_quick_tiles = {string.Join(",", keys)}");
	}

	/// <summary>
	/// 磁贴文字在给定字体下的实测宽（设备像素）。⛔ 不用 <c>Graphics.MeasureString</c>：RButton 自绘走
	/// <c>TextRenderer</c>（GDI），两者在高 DPI 下会差 1~3px —— 按 GDI 量才与真正画出来的宽同口径。
	/// </summary>
	private static int QuickTileTextDeviceWidth(string text, Font font)
	{
		return TextRenderer.MeasureText(text ?? string.Empty, font).Width;
	}

	/// <summary>
	/// 每一格**所需内容宽**（设备像素，⛔ 不含该格 Margin 的 gap）：动作格 = 该格文案实测宽
	/// + D(<see cref="QuickTileTextPadLogical" />)；末位「＋」格 = 「自定义快捷按钮」实测宽 + 同样的余量
	/// （它是本行最宽的一格，必须按自己的文字单独算，⛔ 不能沿用动作格的平均宽）。
	/// RButton 画文字时带 <c>EndEllipsis</c> ⇒「把宽度给够」是**唯一**不出现省略号的做法。
	/// </summary>
	private int[] ComputeQuickTileCellWidths(List<string> keys, Font font)
	{
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileTextPadLogical);
		int[] array = new int[keys.Count + 1];
		for (int i = 0; i < keys.Count; i++)
		{
			array[i] = QuickTileTextDeviceWidth(QuickTileText(keys[i]), font) + num;
		}
		array[keys.Count] = QuickTileTextDeviceWidth(Strings.QuickTileAdd, font) + num;
		return array;
	}

	/// <summary>整行宽需求（设备像素）= Σ 每格内容宽 + 格数 × gap。</summary>
	private static int QuickTileRowWidthFor(int[] cells, int gap)
	{
		int num = 0;
		for (int i = 0; i < cells.Length; i++)
		{
			num += cells[i];
		}
		return num + gap * cells.Length;
	}

	/// <summary>
	/// 磁贴行**可用宽的下限**（设备像素）：窗口能缩到的最小宽（<see cref="MinimumDashboardLogicalClientSize" />，
	/// 2026-10-12 起 = 420 逻辑像素）− 非客户区 − dashboard 左右外边距 D(12) − 性能卡片左右内边距
	/// − 一条竖直滚动条（卡片内容高于窗口时 dashboard 会出滚动条；不留这一条就会在"刚出滚动条"那一帧
	/// 把最后一格挤成省略号）。
	/// ⚠️ 字号就是按这个**下限**选的（只会偏保守）⇒ 列宽只会比需求宽、不会更窄。
	/// </summary>
	private int QuickTileRowMinDeviceWidth()
	{
		int num = ResponsiveLayout.LogicalToDevice(this, MinimumDashboardLogicalClientSize.Width);
		// 非客户区（边框）本来就是设备像素；句柄还没建时 Width−ClientSize 为 0 ⇒ 取 D(16) 这个实测下限兜底。
		num -= Math.Max(Math.Max(0, Width - ClientSize.Width), ResponsiveLayout.LogicalToDevice(this, 16));
		num -= ResponsiveLayout.LogicalToDevice(this, 12);
		if (panelPerformance != null)
		{
			num -= panelPerformance.Padding.Horizontal;
		}
		num -= SystemInformation.VerticalScrollBarWidth;
		return Math.Max(1, num);
	}

	/// <summary>
	/// 选字号：取 <see cref="QuickTileFontSizes" /> 里**最大的**、使「当前动作格 + ＋」整行需求
	/// ≤ 可用宽下限（<see cref="QuickTileRowMinDeviceWidth" />）的那一档；都放不下就退回最小档
	/// （那时 <see cref="DistributeQuickTileColumns" /> 会按行宽等比压缩并留日志 —— 实机不该出现）。
	/// </summary>
	private float ChooseQuickTileFontSize(List<string> keys, int availableDevice)
	{
		float result = QuickTileFontSizes[QuickTileFontSizes.Length - 1];
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalGap);
		for (int i = 0; i < QuickTileFontSizes.Length; i++)
		{
			using (Font font = UiVisualStyle.Font(QuickTileFontSizes[i]))
			{
				if (QuickTileRowWidthFor(ComputeQuickTileCellWidths(keys, font), num) <= availableDevice)
				{
					result = QuickTileFontSizes[i];
					break;
				}
			}
		}
		return result;
	}

	/// <summary>
	/// 按当前格数 + 行宽下限选字号，并算出每格所需内容宽与整行自然宽（设备像素）。
	/// ⚠️ 字号**只在这里选一次**：行宽下限是保守值（含滚动条），窗口拉宽时字号不跟着变，
	/// 避免拖动窗口时字号跳动/重排闪烁。
	/// </summary>
	private void PrepareQuickTileFontAndCells()
	{
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalGap);
		_quickTileFontSize = ChooseQuickTileFontSize(_quickTileKeys, QuickTileRowMinDeviceWidth());
		using (Font font = UiVisualStyle.Font(_quickTileFontSize))
		{
			_quickTileCellDesired = ComputeQuickTileCellWidths(_quickTileKeys, font);
		}
		_quickTileNaturalRowWidth = QuickTileRowWidthFor(_quickTileCellDesired, num);
	}

	/// <summary>
	/// 建磁贴行（构造期一次）：<c>TableLayoutPanel</c>，列数 = 动作格数 + 1（末位固定「＋」），
	/// 行高 D(<see cref="QuickTileLogicalHeight" />)。⛔ 不做 WrapContents（用户答复②：⛔ 不换行）。
	/// </summary>
	private void BuildQuickTileRow()
	{
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		_quickTileRow = new TableLayoutPanel
		{
			Name = "perfQuickTiles",
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = QuickTileActionSlots + 1,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Height = D(QuickTileLogicalHeight),
			BackColor = UiVisualStyle.Window
		};
		_quickTileRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		// 行宽变化时重切列宽（绝对列，见 RebuildQuickTileButtons 里的说明）。
		_quickTileRow.ClientSizeChanged += (object sender, EventArgs e) =>
		{
			DistributeQuickTileColumns();
		};
		_quickTileKeys.Clear();
		_quickTileKeys.AddRange(FilterQuickTileKeys(LoadQuickTileKeys()));
		PrepareQuickTileFontAndCells();
		RebuildQuickTileButtons();
	}

	/// <summary>
	/// 2026-10-12（用户原话「给按钮加个标题 叫快捷开关 类似性能模式之类的」）：磁贴行的标题行。
	/// 与「性能模式 / 显卡模式」**同一套**：<see cref="BuildTitleRow" />（图标列 D(22) + AutoSize 标题）
	/// + <see cref="BuildHeadIcon" />（既有字形，⛔ 不引入位图资源）。图标取 <c>UiGlyph.Kind.Toggles</c>
	/// —— 字形表里语义就是"开关"的那一枚（其余候选 Gear=设置 / Console=控制台 / Chevrons=折叠 都不贴切）。
	/// </summary>
	private void BuildQuickTileHeaderRow()
	{
		_quickTileHeaderLabel = new Label
		{
			Name = "labelQuickTiles",
			Text = Strings.QuickTileRowTitle,
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_quickTileHeaderRow = BuildTitleRow("tileHeaderRow", BuildHeadIcon(UiGlyph.Kind.Toggles), _quickTileHeaderLabel, null);
	}

	/// <summary>
	/// 磁贴来源过滤（与「更多开关」**同一把尺子**）：来源限定 = 那里真实存在的那颗开关
	///（「睡眠省电」组不在磁贴来源里，见 <see cref="OpenQuickTilePicker" />），再按能力位过滤，
	/// 最后裁到 <see cref="QuickTileActionSlots" /> 格。
	/// 调用点：构造期（<see cref="BuildQuickTileRow" />）与 <see cref="ApplyQuickTiles" /> —— 两处共用同一段，
	/// ⛔ 不再各写一份（上一轮是两份逐字相同的循环）。
	/// </summary>
	private List<string> FilterQuickTileKeys(List<string> desired)
	{
		List<string> list = new List<string>();
		for (int i = 0; i < desired.Count && list.Count < QuickTileActionSlots; i++)
		{
			string text = desired[i];
			if (FindQuickSwitch(text) != null && QuickTileSupported(text))
			{
				list.Add(text);
			}
		}
		return list;
	}

	/// <summary>
	/// 按当前配置 + 当前能力位**重算**磁贴集合并重建那一行。变更不成立时只做一次状态刷新（便宜路径）。
	/// 调用点：构造期（BuildQuickTileRow）、每轮 RefreshDeviceCapabilities（经 ApplyPerfConfigRowVisibility）、
	/// 「＋」面板确定之后。
	/// </summary>
	private void ApplyQuickTiles()
	{
		if (_quickTileRow == null)
		{
			return;
		}
		List<string> list = FilterQuickTileKeys(LoadQuickTileKeys());
		bool flag = list.Count == _quickTileKeys.Count;
		if (flag)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (!string.Equals(list[i], _quickTileKeys[i], StringComparison.OrdinalIgnoreCase))
				{
					flag = false;
					break;
				}
			}
		}
		if (flag)
		{
			RefreshQuickTiles();
			return;
		}
		_quickTileKeys.Clear();
		_quickTileKeys.AddRange(list);
		PrepareQuickTileFontAndCells();
		RebuildQuickTileButtons();
	}

	/// <summary>按 <see cref="_quickTileKeys" /> 重建这一行的所有按钮（末尾固定补「＋」），并重算行高。</summary>
	private void RebuildQuickTileButtons()
	{
		TableLayoutPanel tableLayoutPanel = _quickTileRow;
		if (tableLayoutPanel == null)
		{
			return;
		}
		tableLayoutPanel.SuspendLayout();
		try
		{
			foreach (RButton item in _quickTileButtons)
			{
				tableLayoutPanel.Controls.Remove(item);
				item.Dispose();
			}
			_quickTileButtons.Clear();
			// 行内共用一份字体：旧磁贴已 Dispose，此刻换字体最安全（控件不持有 Font 的所有权，
			// 但共用实例必须等所有引用者都没了再 Dispose）。
			Font font = _quickTileFont;
			_quickTileFont = UiVisualStyle.Font(_quickTileFontSize);
			font?.Dispose();
			while (tableLayoutPanel.ColumnStyles.Count > 0)
			{
				tableLayoutPanel.ColumnStyles.RemoveAt(tableLayoutPanel.ColumnStyles.Count - 1);
			}
			int num = _quickTileKeys.Count + 1;
			tableLayoutPanel.ColumnCount = num;
			tableLayoutPanel.RowCount = 1;
			for (int i = 0; i < num; i++)
			{
				tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1f));
			}
			for (int j = 0; j < _quickTileKeys.Count; j++)
			{
				RButton value = CreateQuickTileButton(_quickTileKeys[j]);
				tableLayoutPanel.Controls.Add(value, j, 0);
				_quickTileButtons.Add(value);
			}
			RButton value2 = CreateQuickTileAddButton();
			tableLayoutPanel.Controls.Add(value2, num - 1, 0);
			_quickTileButtons.Add(value2);
			DistributeQuickTileColumns();
		}
		finally
		{
			tableLayoutPanel.ResumeLayout(performLayout: true);
		}
		RefreshQuickTiles();
		RefreshPerfPanelHeights();
	}

	/// <summary>
	/// 一颗动作磁贴。2026-10-12（用户原话「按钮巨大很难看」）：
	/// ⛔ 不再设 <c>MinimumSize</c> —— 列宽由 <see cref="DistributeQuickTileColumns" /> 按**实测文字**分配
	/// （绝对列），再压一个下限就会把最后一格顶出父客户区（`[parent-overflow]`）；宽度够不够由字号
	/// 自适配保证（见 <see cref="ChooseQuickTileFontSize" />），口径是"给够宽度"而不是"截断文字"。
	/// </summary>
	private RButton CreateQuickTileButton(string key)
	{
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalGap);
		RButton rButton = new RButton
		{
			Name = "tile_" + key,
			Tag = key,
			Text = QuickTileText(key),
			Dock = DockStyle.Fill,
			AutoSize = false,
			Margin = new Padding(num / 2, 0, num / 2, 0),
			Padding = Padding.Empty,
			BorderRadius = 8,
			BackColor = UiVisualStyle.ButtonFace,
			Borderless = false,
			BorderColor = UiVisualStyle.Border,
			ForeColor = UiVisualStyle.Muted,
			ActivatedOutline = true,
			Cursor = Cursors.Hand,
			Font = _quickTileFont
		};
		string tileKey = key;
		rButton.Click += async (object sender, EventArgs e) =>
		{
			await ToggleQuickTileAsync(tileKey);
		};
		_quickTileToolTip.SetToolTip(rButton, QuickTileText(key));
		return rButton;
	}

	/// <summary>
	/// 最后一格 = 「＋」（用户原话「关闭屏幕那个按钮的位置改成加号按钮」）。
	/// 2026-10-12（用户原话「+号文字介绍是自定义快捷按钮」）：**图标仍是 ＋**，文字标签 = 它的 tooltip
	/// = <see cref="Strings.QuickTileAdd" />（「自定义快捷按钮」）。⚠️ 这一格是本行**最宽**的一格，
	/// <see cref="ComputeQuickTileCellWidths" /> 按它自己的文字实测宽给列宽 ⇒ ⛔ 不会被裁成"…"。
	/// </summary>
	private RButton CreateQuickTileAddButton()
	{
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalGap);
		int num2 = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalIcon);
		RButton rButton = new RButton
		{
			Name = "tile_add",
			Tag = "",
			Text = Strings.QuickTileAdd,
			Image = UiGlyph.Render(UiGlyph.Kind.Plus, num2, UiVisualStyle.Accent),
			Dock = DockStyle.Fill,
			AutoSize = false,
			Margin = new Padding(num / 2, 0, num / 2, 0),
			Padding = Padding.Empty,
			BorderRadius = 8,
			BackColor = UiVisualStyle.ButtonFace,
			BorderColor = UiVisualStyle.Border,
			ForeColor = UiVisualStyle.Text,
			Cursor = Cursors.Hand,
			Font = _quickTileFont
		};
		rButton.Click += (object sender, EventArgs e) =>
		{
			OpenQuickTilePicker();
		};
		// tooltip 用「＋」面板自己的标题（「自定义快捷磁贴」）—— 磁贴上的文字只有 3 个字「自定义」，
		// 悬浮说明点下去会发生什么（用户原话「自定义快捷按钮改成自定义吧 这看着太宽了」）。
		_quickTileToolTip.SetToolTip(rButton, Strings.QuickTilePickerTitle);
		return rButton;
	}

	/// <summary>
	/// 把磁贴的**高亮/暗淡**与能力位（Enabled）收敛到「更多开关」的当前状态。
	/// ⚠️ 读的是那颗 CheckBox 的 <c>Checked</c>（读不到才退回 <see cref="ReadQuickSwitchState" />）
	/// ⇒ 与「更多开关」是**同一个值**，不可能出现两处状态不一致。
	/// </summary>
	private void RefreshQuickTiles()
	{
		if (_quickTileButtons.Count == 0)
		{
			return;
		}
		for (int i = 0; i < _quickTileKeys.Count && i < _quickTileButtons.Count; i++)
		{
			string key = _quickTileKeys[i];
			RButton rButton = _quickTileButtons[i];
			CheckBox checkBox = FindQuickSwitch(key);
			bool switchOn = ((checkBox != null) ? checkBox.Checked : (ReadQuickSwitchState(key) ?? false));
			ApplyQuickTileVisual(rButton, key, switchOn);
			// 2026-10-12：⛔ 不再用 checkBox.Enabled（那是"忙碌反馈"的瞬时值，见 _quickSwitchInFlight 的说明）
			rButton.Enabled = QuickTileClickable(key, checkBox);
		}
	}

	/// <summary>
	/// 磁贴此刻**是否可点** —— 只由"它自己的能力与状态"决定：
	/// <list type="number">
	/// <item>整机只读降级（不支持的机型）⇒ 不可点（与「更多开关」同一口径，见 <see cref="IsReadOnlyDegraded" />）；</item>
	/// <item>本键正在下发（<see cref="_quickSwitchInFlight" />）⇒ 不可点（就是那颗开关的"忙碌反馈"）；</item>
	/// <item>引用的 CheckBox 失效/被替换（<paramref name="owner" /> == null）⇒ 按能力位兜底，
	/// ⛔ 不静默失效、更不会永远点不动。</item>
	/// </list>
	/// ⚠️ <see cref="RefreshQuickTiles" /> 与 <see cref="ToggleQuickTileAsync" /> **共用**本判据
	/// ⇒ "看起来能点但点了没反应"在结构上不可能出现。
	/// </summary>
	private bool QuickTileClickable(string key, CheckBox owner)
	{
		if (IsReadOnlyDegraded)
		{
			return false;
		}
		if (owner == null || owner.IsDisposed)
		{
			return QuickTileSupported(key);
		}
		return !_quickSwitchInFlight.Contains(key);
	}

	/// <summary>
	/// 打/解除"本键在途"标记。<b>解除一侧必定重算磁贴可点性</b> —— 这就是"动作结束磁贴自动恢复可点"
	/// 的唯一收口；⛔ 不依赖任何别的开关被拨动，⛔ 不依赖 12s 定时器。
	/// 调用口径（2026-10-12）：个人化开关 / <c>ApplyQuickSwitchAsync</c> / 深睡三处**成对**调用，
	/// 且"解除"放在 <c>finally</c> 里 ⇒ 成功/失败/异常都恢复。
	/// </summary>
	private void SetQuickSwitchInFlight(CheckBox box, bool inFlight)
	{
		string key = box.Tag as string;
		if (!string.IsNullOrEmpty(key))
		{
			if (inFlight)
			{
				_quickSwitchInFlight.Add(key);
			}
			else
			{
				_quickSwitchInFlight.Remove(key);
			}
		}
		if (!inFlight && !IsDisposed && !Disposing)
		{
			RefreshQuickTiles();
		}
	}

	/// <summary>
	/// 磁贴**高亮极性**：默认 = 开关为开；**唯一例外** <c>micmute</c> ——
	/// 用户答复②「反过来：高亮 = 未静音（能收音）」，而那颗开关的勾选语义是"已静音"（<c>SetMute(true)</c>），
	/// 故这一键取反。
	/// <para>做法取 **(a)**：开关本体保持「麦克风静音」（勾选 = 已静音）**一字不动**，只给磁贴这一键反转
	/// 高亮极性。理由：①开关本体是上一轮用户点名要的、已确认的修复（文案/提示/下发路径/能力位判据
	/// 都不动最稳）；②磁贴文案本来就是短名「麦克风」，读作"麦克风（亮 = 在收音）"自洽；
	/// ③改名的做法 (b) 会同时改掉开关文案、tooltip 与"勾选 = 静音"的心智模型，动的东西更多、
	/// 且与「更多开关」里其它开关"勾选 = 该功能开"的直觉相反。
	///</para>
	/// ⚠️ 只反转**高亮**：图标仍按开关状态画（静音 = 带斜杠的麦克风），点击仍勾/去勾同一个开关。
	/// </summary>
	private static bool QuickTileHighlightOn(string key, bool switchOn)
	{
		if (string.Equals(key, "micmute", StringComparison.OrdinalIgnoreCase))
		{
			return !switchOn;
		}
		return switchOn;
	}

	private void ApplyQuickTileVisual(RButton tile, string key, bool switchOn)
	{
		int num = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalIcon);
		Image image = tile.Image;
		bool on = QuickTileHighlightOn(key, switchOn);
		// 高亮 = 开（亮色图标 + 强调色描边 + 浅底，与 RButton 既有选中态同一套画法）；
		// 暗淡 = 关（Muted 图标/文字 + 普通边框）。⛔ 不另造一套配色。
		tile.Image = UiGlyph.Render(QuickTileGlyph(key, switchOn), num, on ? UiVisualStyle.Text : UiVisualStyle.Muted);
		if (image != null)
		{
			image.Dispose();
		}
		tile.Activated = on;
		tile.ActivatedOutline = true;
		tile.ForeColor = on ? UiVisualStyle.Text : UiVisualStyle.Muted;
		tile.BorderColor = on ? UiVisualStyle.AccentSelectionOutline : UiVisualStyle.Border;
	}

	/// <summary>
	/// 点磁贴 = 勾选/取消**同一个开关**（用户答复②）。实现上**不另写一套下发代码**：
	/// 除 deepsleep / monitoroff 这两个把动作挂在 <c>CheckBox.Click</c> 上的键之外，
	/// 一律只改那颗 CheckBox 的 <c>Checked</c>，由它既有的 CheckedChanged 处理器去下发。
	/// </summary>
	private async Task ToggleQuickTileAsync(string key)
	{
		CheckBox box = FindQuickSwitch(key);
		// 2026-10-12：判据与 RefreshQuickTiles 的 rButton.Enabled **同一把尺子**（见 QuickTileClickable）——
		// ⛔ 不再用 box.Enabled（否则"那颗 CheckBox 正忙"会被误当成"这颗磁贴不可用"，用户看到的就是点不动）。
		if (box == null || !QuickTileClickable(key, box) || _syncingSwitches)
		{
			return;
		}
		bool target = !box.Checked;
		if (key == "deepsleep")
		{
			// deepsleep 的动作体挂在 Click（不是 CheckedChanged）⇒ 先赋值再调同一个抽出来的方法。
			box.Checked = target;
			await ApplyDeepSleepSwitchAsync(box, key);
			RefreshQuickTiles();
			return;
		}
		if (key == "monitoroff")
		{
			// monitoroff 是**一次性**动作（进入要求 Checked=true，收尾自己拨回 false）。
			box.Checked = true;
			await ApplyQuickMonitorOffAsync(box);
			RefreshQuickTiles();
			return;
		}
		box.Checked = target;
		RefreshQuickTiles();
	}

	/// <summary>
	/// 「＋」自定义面板：可选来源 = 「更多开关」的**全部键去掉「睡眠省电」组**（用户原话
	/// 「可选磁贴来源 = 「更多开关」除「睡眠省电」组以外的部分」），按 <see cref="QuickSwitchGroups" />
	/// 的固定顺序排序。
	/// <para>⭐⭐ 2026-10-18（机主：本机不支持的功能项**直接隐藏**，⛔ 不再灰显）：能力位**唯一来源**仍是
	/// <see cref="QuickTileSupported" />（= <c>hw.SupportsQuickSwitch</c> ∥ <see cref="StaticQuick" />；
	/// 宝龙达 <c>fanboost</c> 的 <c>FanCurveVerified</c> 闸门在 <c>SupportsQuickSwitch</c> 内）⇒
	/// 不支持项**不传入面板**（面板只给 supported 项建行；全部不支持时面板显示一行说明，⛔ 不灰显）✅</para>
	/// <para>⭐⭐ 确定后按**可用集合**落盘：已保存选择里若含不支持项（换机型/能力位变化的残留）⇒
	/// **静默剔除**（⛔ 不弹窗、⛔ 不报错），写 <c>perf_quick_tiles</c> 并重建磁贴行；取消不动任何键 ✅</para>
	/// </summary>
	private void OpenQuickTilePicker()
	{
		List<string> keys = new List<string>();
		List<string> texts = new List<string>();
		List<bool> supported = new List<bool>();
		(string, string[])[] groups = QuickSwitchGroups;
		for (int g = 0; g < groups.Length; g++)
		{
			string[] groupKeys = groups[g].Item2;
			if (groupKeys.Length == 0 || groupKeys[0].StartsWith("sleep", StringComparison.Ordinal))
			{
				continue;
			}
			for (int i = 0; i < groupKeys.Length; i++)
			{
				string key = groupKeys[i];
				if (keys.Contains(key, StringComparer.OrdinalIgnoreCase))
				{
					continue;
				}
				keys.Add(key);
				texts.Add(QuickTileText(key));
				supported.Add(QuickTileSupported(key));
			}
		}
		using (QuickTilePickerForm picker = new QuickTilePickerForm(keys, texts, supported, LoadQuickTileKeys(), QuickTileActionSlots))
		{
			if (picker.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			// ⭐⭐ 2026-10-18（机主需求 3）：保存时**按可用集合落盘** —— 已保存选择里若含不支持项
			//   （换机型 / 能力位变化后的残留），这里**静默剔除**（⛔ 不弹窗、⛔ 不报错）✅
			List<string> chosen = new List<string>();
			for (int i = 0; i < picker.SelectedKeys.Count; i++)
			{
				string selectedKey = picker.SelectedKeys[i];
				if (keys.Contains(selectedKey, StringComparer.OrdinalIgnoreCase) && QuickTileSupported(selectedKey))
				{
					chosen.Add(selectedKey);
				}
			}
			SaveQuickTileKeys(chosen);
		}
		ApplyQuickTiles();
	}

	/// <summary>
	/// 2026-10-11（磁贴行）建立、2026-10-12（用户答复③「缩小磁贴让它一行放下」）改为**按测算权重切**：
	/// 把磁贴行的可用宽按 <see cref="_quickTileCellDesired" />（每格实测文字宽 + 余量）等比分成各列，
	/// 用「累计取整」（第 i 列右界 = round(usable × cum_i / Σcum)）⇒ 各列之和**恒等于**行客户区宽，
	/// ⛔ 不会像 Percent 列那样在高 DPI 下越出父客户区（实测 3/14/20px）。
	/// 每列宽 = 该格内容宽 + gap（该格的左右 Margin）。
	/// ⚠️ 行宽 > 需求时多出来的部分也按权重摊掉（宽窗下磁贴跟着变宽，比例不变、文字一定不裁）。
	/// </summary>
	private void DistributeQuickTileColumns()
	{
		TableLayoutPanel tableLayoutPanel = _quickTileRow;
		if (tableLayoutPanel == null || tableLayoutPanel.IsDisposed || tableLayoutPanel.ColumnStyles.Count == 0)
		{
			return;
		}
		int num = tableLayoutPanel.ColumnStyles.Count;
		int num2 = ResponsiveLayout.LogicalToDevice(this, QuickTileLogicalGap);
		int num3 = Math.Max(num, tableLayoutPanel.ClientSize.Width - num2 * num);
		int[] array = _quickTileCellDesired;
		if (array.Length != num)
		{
			return;
		}
		long num4 = 0L;
		for (int i = 0; i < num; i++)
		{
			num4 += Math.Max(1, array[i]);
		}
		if (num4 <= 0)
		{
			return;
		}
		if (num3 < num4)
		{
			// 兜底：连最小字号都放不下（窗口小于最小宽 / 滚动条比 SystemInformation 报的更宽）。
			// ⛔ 不静默：留一条日志，且下面仍按比例压缩（宁可窄，也不越出父客户区）。
			Logger.WriteLineIfChanged("tiles-narrow", $"[tiles] 磁贴行按行宽压缩：需求 {num4 + num2 * num} 设备 px > 可用 {num3 + num2 * num} 设备 px（文字会走 EndEllipsis）");
		}
		long num5 = 0L;
		for (int j = 0; j < num; j++)
		{
			long num6 = num5;
			num5 += Math.Max(1, array[j]);
			int num7 = (int)Math.Round((double)num3 * ((double)num6 / (double)num4));
			int num8 = (int)Math.Round((double)num3 * ((double)num5 / (double)num4));
			tableLayoutPanel.ColumnStyles[j] = new ColumnStyle(SizeType.Absolute, Math.Max(1, num8 - num7 + num2));
		}
	}

	/// <summary>
	/// 磁贴行的**自然行宽**（逻辑像素；取证口径）：自然内容宽 + 卡片左右内边距 + dashboard 左右外边距，
	/// 再乘与标题行一致的 8% 舒适余量。⛔ 全部是实测（文字用 TextRenderer 量、内边距取控件真值），
	/// ⚠️ **不再**参与窗口最小宽（用户答复③撤销"抬高最小宽度"）—— 只写进取证日志。
	/// </summary>
	private int RequiredQuickTileRowLogicalWidth()
	{
		if (_quickTileNaturalRowWidth <= 0)
		{
			return 0;
		}
		int num = _quickTileNaturalRowWidth;
		if (panelPerformance != null)
		{
			num += panelPerformance.Padding.Horizontal;
		}
		num += ResponsiveLayout.LogicalToDevice(this, 12);
		float num2 = Math.Max(1f, EffectiveLayoutScale);
		int num3 = (int)Math.Ceiling((double)num / (double)num2);
		return (int)Math.Ceiling((double)num3 * 1.08);
	}

	private void SetDeepSleepPending(int value)
	{
		_deepSleepPendingValue = value;
		if (value < 0)
		{
			AppConfig.Remove("deepsleep_pending");
		}
		else
		{
			AppConfig.Set("deepsleep_pending", value);
		}
	}

	internal static bool? ReadQuickSwitchState(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}
		bool? result = ReadWindowsPersonalizationSwitch(key);
		if (result.HasValue)
		{
			return result;
		}
		if (key.StartsWith("sleep"))
		{
			return PowerSleepGuard.ReadEnabled(key, _quickSleepAc);
		}
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return null;
		}
		// 2026-10-11（麦克风静音开关）：状态在 Windows CoreAudio 侧，与 EC/GCU 无关 ——
		// **放在 hw == null 之后**是因为能力判据仍走 hw.SupportsQuickSwitch（同一把尺子，
		// 保证"界面显示这个开关"与"能读回状态"永远一致）；但读取本身不需要 hw 的连接状态。
		if (key == "micmute")
		{
			return hw.SupportsQuickSwitch("micmute") ? new bool?(MechrevoLite.Helpers.AudioEndpointMic.IsMuted) : ((bool?)null);
		}
		if (!(key == "usb"))
		{
			if (key == "fanboost")
			{
				// ⭐ 2026-10-19（强冷核对 ②）：宝龙达没有 GCU ⇒ `hw.SupportsFanBoost` 恒 false、
				//   `hw.FanBoost` 也不会被本机通道更新 ⇒ 读**静态** `FanBoostActive`（与写入侧同源）✅
				//   ⚠️ 非宝龙达逐位不变 ✅
				// ⭐⭐ 2026-10-18（机主 UI 体验）：⭐ 确认窗口内返回**乐观置**的值 ⇒ 磁贴与更多开关
				//   立刻高亮且 12 s 内不被回读拨回；最终失败会清窗口并回滚 ✅
				if (DateTime.Now < _fanBoostOptimisticUntil)
				{
					return new bool?(_fanBoostOptimisticValue);
				}
				if (hw.SupportsFanBoost)
				{
					return new bool?(hw.FanBoost);
				}
				return hw.UsesBaolongda ? new bool?(MechrevoHw.FanBoostActive) : ((bool?)null);
			}
			// ⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：
			//   ⭐ 宝龙达下 `webcam` 的**权威状态是设备状态**（`CameraPnpControl`：SetupAPI 枚举 +
			//      `CM_Get_DevNode_Status` / ConfigFlags）✅ —— ⛔ 不再拿"隐私授权"或 3 s 周期镜像冒充真值 ⚠️；
			//   ⚠️ 读不到（本机没有可控制摄像头设备）⇒ **回落到下面的通用分支**（⛔ 不编造状态）；
			//   ⛔ 非宝龙达不进本分支 ⇒ GCU / 老同方行为逐位不变 ✅
			if (key == "webcam" && hw.UsesBaolongda && CameraPnpControl.TryGetEnabled(out bool camEnabled, out string _))
			{
				return new bool?(camEnabled);
			}
			// ⭐⭐ 2026-10-18（宝龙达 Logo 灯「UI 与硬件状态不同步」）：`logolight` 的**权威状态是硬件
			//  功能号 15 的实时回读** ✅ —— ⛔ 不再只信 3 s 周期的 `QuickSwitches` 镜像 ⚠️
			//  （镜像已由 `MechrevoHw.SyncLogoLightQuickSwitch` 在每次写后按真值回灌；这里再直读一次，
			//   让"读状态"这条路径任何时刻都拿到硬件真值 ✅）。
			//  ⚠️ 读不到 ⇒ **回落到下面的通用分支**（⛔ 读失败不编造状态 ✅）；
			//  ⛔ 非宝龙达不进本分支 ⇒ GCU / 老同方行为逐位不变 ✅
			if (key == "logolight" && hw.UsesBaolongda && hw.TryReadLogoLightViaBackend(out bool logoOn))
			{
				return new bool?(logoOn);
			}
			bool value;
			return hw.QuickSwitches.TryGetValue(key, out value) ? new bool?(value) : ((bool?)null);
		}
		return hw.UsbChargerSeen ? new bool?(hw.UsbCharger) : ((bool?)null);
	}

	public SettingsForm()
	{
		// ⭐⭐ 2026-10-19（机主：设置窗加载太多）：构建分段计时（先量再改；只打一次）✅
		Stopwatch uiBuildTotal = Stopwatch.StartNew();
		_uiBuildProfiling = true;
		long uiBuildInitMs = 0L;
		long uiBuildDisplayMs = 0L;
		long uiBuildOtherMs = 0L;
		Stopwatch uiBuildPhase = Stopwatch.StartNew();
		BackColor = UiVisualStyle.Window;
		InitializeComponent();
		uiBuildInitMs = uiBuildPhase.ElapsedMilliseconds;
		uiBuildPhase.Restart();
		// ⭐⭐ 2026-10-19（机主：亮度调节有延迟）：写入改成**节流合并**（拖动期 ~120ms 一次 + 松手补最终值）；
		//   写后回读在后台线程执行 ⇒ ⛔ 不阻塞 UI。日志：`[brightness] 写（节流合并 n 次）请求=… ⇒ 实际=…`
		_brightnessCommitQueue = new BrightnessCommitQueue(WriteBrightnessValueAsync, TimeSpan.FromMilliseconds(120L), (Exception ex) =>
		{
			Logger.WriteLine("Screen brightness update failed: " + ex.Message);
		});
		labelVersion.Text = (Program.ReleaseLabel == Program.ReleaseVersion) ? Program.ReleaseLabel : Program.ReleaseVersion + " " + Program.ReleaseLabel;
		InitTheme(setDPI: true);
		Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
		// 2026-10-09（多显示器刷新率修复）：外接屏接入/断开、切换显示配置时都要重算刷新率行。
		Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
		if (!Program.UiAuditMode)
		{
			Application.AddMessageFilter(new DashboardWheelFilter(this));
		}
		gpuControl = new GPUModeControl(this);
		updateControl = new AutoUpdateControl(this);
		buttonSilent.Text = Strings.Silent;
		buttonBalanced.Text = Strings.Balanced;
		buttonTurbo.Text = Strings.Turbo;
		buttonEco.Text = Strings.EcoMode;
		buttonUltimate.Text = Strings.UltimateMode;
		buttonStandard.Text = Strings.StandardMode;
		buttonStopGPU.Text = Strings.StopGPUApps;
		buttonScreenAuto.Text = Strings.AutoMode;
		buttonMiniled.Text = Strings.Multizone;
		buttonKeyboardColor.Text = Strings.Color;
		buttonKeyboard.Text = Strings.Extra;
		labelPerf.Text = Strings.PerformanceMode;
		labelGPU.Text = Strings.GPUMode;
		labelSreen.Text = Strings.LaptopScreen;
		UpdateKeyboardLabel();
		labelBatteryTitle.Text = Strings.BatteryChargeLimit;
		checkStartup.Text = Strings.RunOnStartup;
		buttonQuit.Text = Strings.Quit;
		buttonUpdates.Text = Strings.Updates;
		buttonDonate.Text = Strings.Donate;
		sliderBattery.AccessibleName = Strings.BatteryChargeLimit;
		buttonQuit.AccessibleName = Strings.Quit;
		buttonUpdates.AccessibleName = Strings.CheckUpdates;
		panelPerformance.AccessibleName = Strings.PerformanceMode;
		buttonSilent.AccessibleName = Strings.Silent;
		buttonBalanced.AccessibleName = Strings.Balanced;
		buttonTurbo.AccessibleName = Strings.Turbo;
		panelGPU.AccessibleName = Strings.GPUMode;
		buttonEco.AccessibleName = Strings.EcoMode;
		buttonStandard.AccessibleName = Strings.StandardMode;
		buttonUltimate.AccessibleName = Strings.UltimateMode;
		panelScreen.AccessibleName = Strings.LaptopScreen;
		buttonScreenAuto.AccessibleName = Strings.AutoMode;
		panelKeyboard.AccessibleName = Strings.LaptopKeyboard;
		buttonKeyboard.AccessibleName = Strings.ExtraSettings;
		buttonKeyboardColor.AccessibleName = Strings.LaptopKeyboard + " " + Strings.Color;
		comboKeyboard.AccessibleName = Strings.LaptopBacklight;
		FormClosing += SettingsForm_FormClosing;
		Deactivate += SettingsForm_LostFocus;
		Activated += SettingsForm_Focused;
		contextMenuStrip.Opening += (object? _, CancelEventArgs _) =>
		{
			SetContextMenu();
		};
		buttonSilent.BorderColor = RForm.colorEco;
		buttonBalanced.BorderColor = RForm.colorStandard;
		buttonTurbo.BorderColor = RForm.colorTurbo;
		buttonEco.BorderColor = RForm.colorEco;
		buttonStandard.BorderColor = RForm.colorStandard;
		buttonUltimate.BorderColor = RForm.colorTurbo;
		button60Hz.BorderColor = RForm.colorGray;
		button120Hz.BorderColor = RForm.colorGray;
		buttonScreenAuto.BorderColor = RForm.colorGray;
		buttonMiniled.BorderColor = RForm.colorTurbo;
		buttonEnergySaver.BackColor = RForm.colorEco;
		buttonEnergySaver.ForeColor = SystemColors.ControlLightLight;
		buttonEnergySaver.Click += ButtonEnergySaver_Click;
		buttonAmdOled.BackColor = RForm.colorTurbo;
		buttonAmdOled.ForeColor = SystemColors.ControlLightLight;
		buttonAmdOled.Click += ButtonAmdOled_Click;
		buttonSilent.Click += ButtonSilent_Click;
		buttonBalanced.Click += ButtonBalanced_Click;
		buttonTurbo.Click += ButtonTurbo_Click;
		buttonSilent.Image = null;
		buttonSilent.TextAlign = ContentAlignment.MiddleCenter;
		buttonBalanced.Image = null;
		buttonBalanced.TextAlign = ContentAlignment.MiddleCenter;
		buttonTurbo.Image = null;
		buttonTurbo.TextAlign = ContentAlignment.MiddleCenter;
		buttonTurbo.Text = Strings.TurboMode;
		buttonTurbo.AccessibleName = Strings.TurboMode;
		tablePerf.Controls.Remove(buttonTurbo);
		tablePerf.Controls.Add(buttonTurbo, 3, 0);
		buttonSilentTurbo = new RButton
		{
			Text = Strings.SilentTurbo,
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			ForeColor = UiVisualStyle.Text,
			// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace（日间与 SurfaceRaised 同值，像素不变）。
			BackColor = UiVisualStyle.ButtonFace
		};
		buttonSilentTurbo.FlatAppearance.BorderColor = UiVisualStyle.Border;
		buttonSilentTurbo.BorderColor = RForm.colorTurbo;
		buttonSilentTurbo.Click += async (object? _, EventArgs _) =>
		{
			await SwitchPerformanceModeAsync(1, true);
		};
		tablePerf.Controls.Add(buttonSilentTurbo, 2, 0);
		buttonCustomMode = new RButton
		{
			Text = Strings.CustomMode,
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			ForeColor = UiVisualStyle.Text,
			// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace（日间与 SurfaceRaised 同值，像素不变）。
			BackColor = UiVisualStyle.ButtonFace
		};
		buttonCustomMode.FlatAppearance.BorderColor = UiVisualStyle.Border;
		buttonCustomMode.BorderColor = RForm.colorCustom;
		buttonCustomMode.Click += async (object? _, EventArgs _) =>
		{
			// 2026-10-10（用户要求 · ⭐ 原话「还有增加一个右键自定义模式按钮 会打开自定义窗口但是不切换模式」）：
			// 「开窗」这一段搬到 OpenCustomModeEditor()，左键（本 lambda）与右键（下面的 MouseUp）共用**同一条**
			// 路径，⛔ 不另写一份开窗逻辑；档位 id 的解析同样搬进 ResolveCustomModeIdForEditor()（语义一字未改）。
			if (!OpenCustomModeEditor())
			{
				return;
			}
			string customId = ResolveCustomModeIdForEditor();
			// ① 开窗（只读）已由上面的 OpenCustomModeEditor() 完成 —— 窗口的显示/绑定本身不写任何硬件，
			// 那一步里没有任何切档/下发调用，是**代码结构**上的只读（原 `OpenCustomEditorAppliesMode` 开关
			// 已删除，见字段区注释）。
			// 2026-10-09（回归修复）：这颗按钮不再兼任"再点一次收起编辑器"的开关 —— 它是**性能模式行**的
			// 第 5 颗按钮，语义与另外四颗一致："切到这个模式，并把它显示出来"。原来"可见就 Hide"会让用户
			// 卡在"编辑器开着、模式却还是狂暴"的状态（用户原话「我切不到自定义档位了」）。
			// 收起编辑器请用编辑器自己的关闭按钮（CustomModeForm.FormClosing → Hide()）。
			if (!CustomModeButtonSwitchesMode)
			{
				// 一键回退（仅崩溃排查用）：只读绑定，不切档、不写硬件。
				customModeForm.BindMode(customId);
				return;
			}
			// ② ⭐ 显式切档（与"开窗"解耦）：SelectModeAsync = BindMode（只读绑定到该档）
			//    + ActivateModeAsync（PerfModeService.ActivateAsync(id, "editor", forceParameters: true)）
			//    + 状态刷新。语义＝"按该档**已保存**的值强制全量下发一次"：即使档位占用的固件槽签名
			//    一致（slot hit），也会走 PerfModeService 的 `forced rewrite` 分支把这批字段重写一遍
			//    （含两条风扇曲线），用户不会再有"切了却没生效"的观感。
			//    失败（如 GCU 未连接）⇒ 退回只读绑定，绝不留下"看着切了、其实没切"的高亮。
			if (await customModeForm.SelectModeAsync(customId))
			{
				ShowMode(MechrevoService.ToVisualMode(3));
				SetContextMenu();
			}
			else
			{
				customModeForm.BindMode(PerfModeService.Instance?.DisplayedModeId ?? PerfModeStore.ActiveModeId);
			}
		};
		// 2026-10-10（用户要求 · ⭐ 原话「还有增加一个右键自定义模式按钮 会打开自定义窗口但是不切换模式」）：
		// 右键 = **只开窗**（复用 OpenCustomModeEditor()，与左键同一条路径），再做一次**只读** BindMode
		// （就是左键"不切档"那一支的同一句，不写硬件）。
		// ⛔ 这里**没有** SelectModeAsync / ActivateModeAsync / ShowMode / SetContextMenu ⇒ 右键绝不切档、
		// 不改高亮、不写硬件。
		// 右键为什么不会把左键的切档逻辑带跑：RButton 直承 System.Windows.Forms.Button，ButtonBase 只在
		// **左键**抬起时 OnClick（WinForms 既有行为），本控件与 RButton 都没有把右键转发成 Click，
		// 本按钮上也没有任何 MouseUp/MouseDown/ContextMenuStrip（见下方第 4 点自查）。
		// ⚠️ 隐式类型的 lambda（⛔ 不写 `object? sender`）：本文件没有 #nullable 上下文，显式可空注解
		// 会多一条 CS8632，而构建基线（0 错误 / 1435 警告）是硬上限（同 BuildChargeSliderToggleRow 的写法）。
		buttonCustomMode.MouseUp += (_, e) =>
		{
			if (e.Button != MouseButtons.Right)
			{
				return;
			}
			if (!OpenCustomModeEditor())
			{
				return;
			}
			// 只读绑定到「自定义编辑器该显示的那一档」（与左键同一解析函数），不切档、不写硬件。
			customModeForm.BindMode(ResolveCustomModeIdForEditor());
		};
		// 2026-10-10（用户要求 · ⭐ 原话「可以加个悬浮提示 "右键打开自定义面板"」）：性能模式行第 5 颗
		// 「自定义」按钮加**悬浮提示**，沿用本窗口既有的 tooltip 机制（this.toolTip，＝
		// System.Windows.Forms.ToolTip，InitializeComponent 里用 this.components 建好；
		// ⚠️ 本窗口**没有**"是否显示提示"这类全局开关（已全仓查过：AppConfig 里没有任何 tooltip/tip 开关），
		// 因此无需判断，照其它 SetToolTip 一样直接下发；文案走 Strings（三份资源），⛔ 不硬编码。
		// ⛔ 只加提示：不碰左键切档逻辑，不碰右键开窗逻辑（OpenCustomModeEditor /
		// ResolveCustomModeIdForEditor 一字未动），也不覆盖任何既有提示（本按钮此前没有提示）。
		toolTip.SetToolTip(buttonCustomMode, Strings.CustomModeRightClickTip);
		tablePerf.Controls.Add(buttonCustomMode, 4, 0);
		ApplySegmentRow(new List<Control> { buttonSilent, buttonBalanced, buttonSilentTurbo, buttonTurbo, buttonCustomMode });
		// 2026-10-08：建好按钮就按当前主题下发模式专属高亮色（换主题时由 ApplyThemeMode 再刷）。
		ApplyPerfModeAccents();
		ApplySegmentRow(new List<Control> { buttonEco, buttonStandard, buttonUltimate });
		// 2026-10-09（UI 打磨③，仅夜间）：显卡模式行（核显模式/混合模式/独显直连）的选中态也改「彩边 + 深底」。
		// 语义色照旧由 VisualiseGPUMode 下发：非待生效时 ActivatedColor 为空 ⇒ 取通用强调色 Accent（蓝）；
		// 「待生效＝重启后生效」时 ActivatedColor=Warn（棕）⇒ 变成「棕边 + 棕调深底」，棕的语义一字未改，
		// 只是由实心块换成描边。2026-10-09（UI 打磨④）：不再有任何内缩值 —— 深底铺满整颗、那圈 2px 彩边
		// 就是按钮自己的边框（选中时 UiVisualStyle.Border 那圈被抑制），与性能模式行完全同款。
		UiVisualStyle.UseOutlineSelection(buttonEco, buttonStandard, buttonUltimate);
		// 2026-10-09（UI 反馈①，仅夜间）：屏幕行右侧的 MiniLED / FHD / HDR 三颗独立按钮**此前从未开过**
		// ActivatedOutline（见《UI样式规范.md》§6.4 第 4 条：夜间选中态只把文字换成 AccentText 8/18/31，
		// 近黑字压在 ButtonFace 62 上对比仅 1.76，几乎看不见；FHD 连边都没有）⇒ 与其它行统一：
		// 选中态 = 中性灰高亮（2 设备像素 Muted 158 描边 + ButtonFace 62 深底）。
		// ⚠️ 这三颗没有下发 ActivatedColor、也不置 SelectionUsesAssignedAccent ⇒ 一律走中性灰（不借模式色）。
		UiVisualStyle.UseOutlineSelection(buttonMiniled, buttonFHD, buttonHDRControl);
		// 2026-10-09（0.291 beta22，用户追加口径「边框高亮 …依旧没有」）：屏幕行左侧那三颗**经典刷新率按钮**
		// （自动 / 60 / 120，设计器里就是三颗独立按钮：列 0~2、BorderColor=Transparent、BorderRadius=5）
		// 此前也没开 ActivatedOutline ⇒ 夜间是"近黑字压在 62 灰上"（1.76、几乎看不见）、
		// 日间更是"白字压近白底"。与 MiniLED/FHD/HDR 及动态刷新率行（_hzButtons）统一：
		// 选中态 = 中性底 + 强调蓝描边（夜/日同一套几何，日间不再有实心蓝块）。
		UiVisualStyle.UseOutlineSelection(button60Hz, button120Hz, buttonScreenAuto);
		buttonEco.Click += ButtonEco_Click;
		buttonStandard.Click += ButtonStandard_Click;
		buttonUltimate.Click += ButtonUltimate_Click;
		buttonStopGPU.Click += ButtonStopGPU_Click;
		pictureGPU.Click += PictureGPU_Click;
		VisibleChanged += SettingsForm_VisibleChanged;
		button60Hz.Click += Button60Hz_Click;
		button120Hz.Click += Button120Hz_Click;
		buttonScreenAuto.Click += ButtonScreenAuto_Click;
		buttonMiniled.Click += ButtonMiniled_Click;
		buttonFHD.Click += ButtonFHD_Click;
		buttonHDRControl.Click += ButtonHDRControl_Click;
		buttonQuit.Click += ButtonQuit_Click;
		buttonUpdates.Text = Strings.CheckUpdates;
		buttonUpdates.Click += (object? _, EventArgs _) =>
		{
			ShowUpdateDialog();
		};
		buttonKeyboardColor.Click += ButtonKeyboardColor_Click;
		buttonKeyboardColor.Swatch2Click += ButtonKeyboardColor2_Click;
		labelGPUFan.Click += LabelCPUFan_Click;
		checkStartup.Checked = false;
		checkStartup.Enabled = Program.UiAuditMode;
		checkStartup.Click += CheckStartup_Click;
		Shown += (object? _, EventArgs _) =>
		{
			if (!Program.UiAuditMode)
			{
				RefreshStartupStatusAsync();
			}
		};
		labelVersion.Click += LabelVersion_Click;
		labelVersion.ForeColor = UiVisualStyle.Muted;
		buttonEco.MouseMove += ButtonEco_MouseHover;
		buttonEco.MouseLeave += ButtonGPU_MouseLeave;
		buttonStandard.MouseMove += ButtonStandard_MouseHover;
		buttonStandard.MouseLeave += ButtonGPU_MouseLeave;
		buttonUltimate.MouseMove += ButtonUltimate_MouseHover;
		buttonUltimate.MouseLeave += ButtonGPU_MouseLeave;
		tableGPU.MouseLeave += ButtonGPU_MouseLeave;
		buttonScreenAuto.MouseMove += ButtonScreenAuto_MouseHover;
		buttonScreenAuto.MouseLeave += ButtonScreen_MouseLeave;
		button60Hz.MouseMove += Button60Hz_MouseHover;
		button60Hz.MouseLeave += ButtonScreen_MouseLeave;
		button120Hz.MouseMove += Button120Hz_MouseHover;
		button120Hz.MouseLeave += ButtonScreen_MouseLeave;
		buttonFHD.MouseMove += ButtonFHD_MouseHover;
		buttonFHD.MouseLeave += ButtonScreen_MouseLeave;
		panelBatteryTitle.Controls.Clear();
		labelBattery.ForeColor = UiVisualStyle.Muted;
		labelBattery.Font = UiStyleCaptionFont();
		panelBatteryTitle.Controls.Add(BuildHeadRow(pictureBattery, labelBatteryTitle, labelBattery));
		// 2026-10-08 17:00（修正 · 用户要求「不要影响 50 系默认显示的滑块」）：滑块是否上屏**按代际区分**，
		// 回到返工前的规则 ——
		//   50 系：滑块默认显示（返工前观感），上限完全由滑块决定；这一支不建「EC 限充／实验」开关、
		//          也不建三档按钮，所以滑块不可能被新控件挤占；
		//   非 50 系（含本机 40 系）：滑块不上屏（Visible=false、不加入任何容器），用「EC 限充」开关
		//          （+「实验」角标、独占一行，下面再跟一行副文案）+ 三档按钮的新布局。
		// 2026-10-09（无极限充滑块 · 实验性功能，用户原话「滑块限制充电改成默认隐藏 在设置里面增加一个开关
		// 『启用无极限充功能(实验性)』打开才显示」）：**可见性改由开关决定、默认隐藏** —— 代际只决定
		// 「滑块行建在哪套布局里」，不再决定上不上屏。两个分支都把滑块行建出来（默认行高 0 ⇒ 不上屏），
		// 由 ApplyChargeSliderVisibility() 统一收放：开＝上屏、关＝收掉占位。
		// 2026-10-10（用户要求 · ⭐ 原话「调整代码 三档限充不为50系隐藏 现在是共存状态了」）：**50 系不再走
		// 「只有滑块」的旧布局** —— 三档（长效/平衡/工作站）与 EC 限充开关和其它代际一致地建出来，与
		// 「启用无极限充功能(实验性)」两态开关 / 滑块**共存**：开关关（默认）＝三档与 EC 开关可用、滑块不上屏；
		// 开关开＝滑块上屏，三档与 EC 开关置灰（既有规则，见 ApplyChargeLimitStageEnablement）。
		// ⛔ 这是「50 系隐藏三档」的**唯一**判据来源：现在恒 false ⇒ 下面两个 _gen50SliderRow 分支恒走 else
		// （＝40 系/NoDgpu 那条布局），其它代际行为逐字不变；IsRtx50SeriesGpu() 保留但不再参与布局决策。
		_gen50SliderRow = false;
		// 2026-10-10（限充代系门控）：无极限充只排除 Gen30（30 系不显示开关；40/50/NoDgpu 办公本均开放）⇒
		// _chargeSliderEnabled 在 30 系被强制为 false，滑块与开关行都不建。
		_chargeSliderSupported = ChargeSliderSupported();
		_chargeSliderEnabled = _chargeSliderSupported && AppConfig.Is(ChargeSliderConfigKey);
		sliderBattery.Visible = _chargeSliderEnabled;
		sliderBattery.Dock = DockStyle.Fill;
		sliderBattery.Margin = Padding.Empty;
		_batteryLimitValue = new Label
		{
			Name = "labelBatteryLimitValue",
			Text = "100%",
			ForeColor = UiVisualStyle.Muted,
			BackColor = panelBattery.BackColor,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			Margin = Padding.Empty,
			// 2026-10-10（用户要求 · ⭐ 原话「这个是硬编码吗 怎么不是我们的字体」）：原先这里写死
			// new Font("Consolas", …) —— 本标签既显示「100%」这类数字，也在**非 50 系且上限=100**时显示
			// Strings.ChargeLimitOff（「充电上限已关闭」）这句中文；Consolas 没有汉字字形，GDI 会回落到
			// 另一个字体 ⇒ 与同一张卡片里其它标签（labelBattery / limitLabel「限充」/ checkChargeSlider
			// 都用 UiStyleCaptionFont()）明显不是同一套字。现改用本仓库统一的字体入口
			// UiVisualStyle.Font(8.5f)（＝ UiStyleCaptionFont()，即自带字体 HarmonyOS Sans SC），
			// ⛔ 不自造字体/字号；取值语义、右对齐、显隐逻辑一字未动。
			Font = UiStyleCaptionFont()
		};
		// 50 系走单行滑块行（返工前的 16%/70%/14% 三列、行高 32、行样式 100%）；
		// 非 50 系走三行（开关+角标行 26 + 副文案行 26 + 三档行 30），滑块行的「限充」标签与数值标签都不上屏
		// （开关自带「EC 限充／实验」文案，当前上限由档位按钮高亮 + 电池标题行表达）。
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Name = _gen50SliderRow ? "tableBatterySlider" : "tableBatteryLimit",
			ColumnCount = 3,
			RowCount = _gen50SliderRow ? 3 : 6,
			Location = new Point(panelBattery.Padding.Left, panelBatteryTitle.Bottom + ResponsiveLayout.LogicalToDevice(this, 2)),
			Size = new Size(Math.Max(1, panelBattery.ClientSize.Width - panelBattery.Padding.Horizontal), ResponsiveLayout.LogicalToDevice(this, _gen50SliderRow ? 32 : 82)),
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			BackColor = panelBattery.BackColor
		};
		// 2026-10-10（三档不再对 50 系隐藏）：本分支自此不可达（_gen50SliderRow 恒 false），保留仅为可读性对照。
		if (_gen50SliderRow)
		{
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16f));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14f));
			// 2026-10-09（无极限充滑块）：第 0 行＝滑块行（默认隐藏 ⇒ 高度算 0）、第 1 行＝实验性开关。
			// 高度必须用 Absolute（Percent 收不到 0）；两行高度在 ApplyChargeSliderVisibility() 里统一重算。
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 32)));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 26)));
		}
		else
		{
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
			// 行样式在 BuildChargeLimitControls 里按「开关+角标行 / 副文案行 / 三档行 / 无极限充开关行 /
			// 滑块行」重设（此处只是占位；前四行以后者为准，滑块行高度由 ApplyChargeSliderVisibility() 收放）。
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 26)));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 26)));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 30)));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 26)));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, ResponsiveLayout.LogicalToDevice(this, 32)));
		}
		panelBattery.Controls.Add(tableLayoutPanel);
		panelBattery.MinimumSize = new Size(0, tableLayoutPanel.Bottom + panelBattery.Padding.Bottom);
		sliderBattery.MouseUp += SliderBattery_MouseUp;
		sliderBattery.KeyUp += SliderBattery_KeyUp;
		sliderBattery.ValueChanged += SliderBattery_ValueChanged;
		batteryTimer.Tick += (object? _, EventArgs _) =>
		{
			batteryTimer.Stop();
			// 2026-10-10（三方式互斥 · 用户原话「EC限充和无极限制应该实现方式差不多,这俩也不要冲突了」）：
			// 防抖窗口内「无极限充」被关掉（或从未生效）⇒ **取消这次滑块写入** —— 否则隐藏滑块的值会在
			// 窗口结束后写进 EC，把三档（或 GCU 侧）刚确立的值覆写掉。
			if (!_chargeSliderEnabled)
			{
				Logger.WriteLine("限充方式互斥：无极限充（实验性）未生效 ⇒ 取消本次滑块 EC 写入（不覆写档位/GCU 的值）");
				return;
			}
			// 2026-10-08 17:00：滑块行由「无极限充」开关收放（2026-10-10 起 50 系与其它代际同构）；
			// 写入与返工前一致 —— EcChargeLimit 直写 EC（0x7B9 上限；0x7D0 自 2026-10-10 起只读不写）；
			// 效果观察器在 CommitChargeLimit 里 Arm。
			BatteryControl.ApplyChargeLimitFromUserGesture(sliderBattery.Value);
		};

		// 2026-10-08（用户要求 · 充电上限 UI 返工；17:00 修正适用代际）：**只有非 50 系**走下面这套新布局 ——
		// ① 三档（长效/平衡/工作站）保持原样：仍在同一行、同一顺序、同一外观，不再被开关挤占；
		// ② 新增的「EC 限充」开关 +「实验」角标单独占一行（第 1 行），副文案占第 2 行，三档在第 3 行；
		// ③ 上限由三档决定（长效 100 / 平衡 80 / 工作站 60），滑块不上屏、不参与；
		// ④ 写入仍只有一条通道：EC 直写（EcChargeLimit），三档不再发 GCU/MQTT 写指令。
		// 50 系改走 BuildChargeLimitSliderRow：只有滑块行，不建开关、不建三档（＝返工前观感）。
		// 2026-10-10（三档不再对 50 系隐藏）：本分支自此不可达（_gen50SliderRow 恒 false）—— 50 系改走
		// BuildChargeLimitControls（三档 + EC 限充开关 + 无极限充开关 + 滑块行共存），与 40 系逐字同构。
		if (_gen50SliderRow)
		{
			// 2026-10-09（无极限充滑块）：50 系＝第 0 行滑块行（「限充」标签 / 滑块 / 数值）、第 1 行＝
			// 「启用无极限充功能(实验性)」两态开关；开关关（默认）时第 0 行高度收 0 ⇒ 滑块不上屏、不留空行。
			_chargeSliderTable = tableLayoutPanel;
			_chargeSliderRowIndex = 0;
			_chargeSliderRowHeight = ResponsiveLayout.LogicalToDevice(this, 32);
			BuildChargeLimitSliderRow(tableLayoutPanel, _batteryLimitValue!, 0);
			BuildChargeSliderToggleRow(tableLayoutPanel, 1);
			// 2026-10-10（限充安全警告）：开关正下方（第 2 行）红色警告行 —— 打开时显示、关闭时隐藏。
			Label chargeSliderWarning = BuildChargeSliderWarningLabel();
			tableLayoutPanel.Controls.Add(chargeSliderWarning, 0, 2);
			tableLayoutPanel.SetColumnSpan(chargeSliderWarning, 3);
			_chargeSliderWarning = chargeSliderWarning;
			_chargeSliderWarningRowIndex = 2;
			_chargeSliderWarningRowHeight = ResponsiveLayout.LogicalToDevice(this, 34);
			ApplyChargeSliderVisibility();
		}
		else
		{
			BuildChargeLimitControls(tableLayoutPanel);
			// ⭐⭐ 2026-10-18（机主定：宝龙达同款机型 · 充电阈值放电池页）：⭐ 起充/停充 + 启用开关 ✅
			BuildBaolongdaChargeLimitSection(tableLayoutPanel);
		}
		MechrevoHw? hwForBattery = Program.hw;
		if (hwForBattery != null)
		{
			hwForBattery.BatteryProtectionChanged += OnBatteryProtectionChanged;
		}
		Activated += async (object? _, EventArgs _) =>
		{
			try
			{
				RefreshThemeButtons();
				MechrevoHw? hwActivated = Program.hw;
				if (hwActivated != null)
				{
					await hwActivated.RequestBatteryProtectionAsync();
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Battery protection read failed: " + ex.Message);
			}
		};
		ChargeLimitMonitor.VerdictChanged += OnChargeLimitVerdictChanged;
		_sensorTimer = new System.Timers.Timer(AppConfig.Get("sensor_timer", 1000));
		_sensorTimer.Elapsed += OnTimedEvent;
		// 传感器轮询常驻：不随主窗显示/隐藏启停（对齐 beta21「最小化时除传感器轮询外暂停显示回读」），
		// 避免每次打开主窗都重新进入读取周期
		if (!Program.UiAuditMode)
		{
			_sensorTimer.Start();
		}
		InitQuickSwitchRefresh();
		labelCharge.MouseEnter += PanelBattery_MouseEnter;
		labelCharge.MouseLeave += PanelBattery_MouseLeave;
		labelBattery.Click += LabelBattery_Click;
		buttonBatteryFull.MouseEnter += ButtonBatteryFull_MouseEnter;
		buttonBatteryFull.MouseLeave += ButtonBatteryFull_MouseLeave;
		buttonBatteryFull.Click += ButtonBatteryFull_Click;
		buttonOverlay.Click += ButtonOverlay_Click;
		buttonOverlay.BorderColor = RForm.colorStandard;
		string text = (string.IsNullOrWhiteSpace(_deviceCapabilities.Model) ? _deviceCapabilities.SystemFamily : _deviceCapabilities.Model);
		Text = "L-Mechrevo " + (ProcessHelper.IsUserAdministrator() ? "—" : "-") + " " + text;
		buttonFnLock.Visible = false;
		labelCharge.Click += LabelCharge_Click;
		donateControl = new DonateControl(this, buttonDonate);
		donateControl.Init();
		labelBacklight.ForeColor = RForm.colorStandard;
		labelBacklight.Click += LabelBacklight_Click;
		// ⭐ 布局批处理（性能优化）：整段构建只触发一次最终布局（⛔ 不在每个 Add 后级联重排）
		// ⚠️ 通用路径（⛔ 不按平台分支）：宝龙达 / GCU / 同方都走这同一段 ⇒ 布局优化对所有机型生效 ✅
		SuspendLayout();
		uiBuildPhase.Restart();
		BuildOfficialConsolePanel();
		uiBuildDisplayMs += uiBuildPhase.ElapsedMilliseconds;
		// ⭐⭐ 2026-10-19（机主：判据为假连控件都不建）：核显模式三选行 / 灯光额外两行延后到
		//   `EnsureSettingsDialogBuilt()`（此时 Program.hw 已就绪 ⇒ 判据可判、且不构建不存在的行）✅
		uiBuildPhase.Restart();
		BuildDashboardLayout();
		uiBuildOtherMs += uiBuildPhase.ElapsedMilliseconds;
		ResumeLayout(false);
		PerformLayout();
		// 2026-10-09（UI 打磨③，用户原话「这种长条的能不能拆成和Ghelper一样独立的呢」）：
		// BuildDashboardLayout 里的 MakeCompactModeButton 会把性能/显卡两行按钮的 Margin 清成 Empty
		// （那是"拼成一条长条"时代的写法），会把上面下发过的拆条 Margin 抹掉；而运行时那两次
		// ReflowPerformanceButtons/VisualiseGPUButtons 只在布局签名变化时才重新下发，故必须在这里、
		// 在 BuildDashboardLayout 之后再按同样的顺序补一次（此后每次 reflow 都会自己带上新 Margin）。
		ApplySegmentRow(new List<Control> { buttonSilent, buttonBalanced, buttonSilentTurbo, buttonTurbo, buttonCustomMode });
		ApplySegmentRow(new List<Control> { buttonEco, buttonStandard, buttonUltimate });
		ConfigureResponsiveWindow();
		// 2026-10-09（lightspeed，P2）：ctor 末尾这一次与 Program.cs 里紧随其后的那一次
		// （`settingsForm.RefreshDeviceCapabilities();`，在本窗体构造之后、hw 建好之后）是**同一帧内的两次
		// 全量刷新**：本机实测第一次 44.4ms、第二次 55.0ms，两次都把整套可见性算一遍、并各做一次
		// SyncHzButtons/ArrangeDashboard/PerformLayout（第二次的产物与第一次不同，因为此刻 hw 才建好）。
		// 而这两次里，**第一次的输入恒为空能力位**（Program.hw 此刻还是 null —— 它在本窗体构造之后
		// 才 `hw = new MechrevoHw()`；caps 退回 _deviceCapabilities 缓存），其结果（每个 flag、每处
		// Visible、_lastCapabilityLayout）在第二次刷新里一律被覆盖 ⇒ 直接跳过第一次（省 44.4ms），
		// 界面不会少任何东西（第二次发生在本窗体第一次显示之前，见 Program.cs 的 SettingsToggle）。
		// ⚠️ 只在"硬件对象尚未创建"这一种情形下跳过：Program.UiAuditMode（UI 审计/回归必须拿到完整布局）
		// 与 Program.hw != null（软重载、能力位变化回调、任何已有硬件的重入）都照旧全量刷新。
		if (Program.UiAuditMode || Program.hw != null)
		{
			uiBuildPhase.Restart();
			RefreshDeviceCapabilities();
			uiBuildDisplayMs += uiBuildPhase.ElapsedMilliseconds;
		}
		// ⭐⭐ 2026-10-19（机主：「设置窗口里根本没有这些功能，为什么要加载这么多？」）：
		//   ⭐ 分段耗时日志（**只打一次**、默认级别可见）—— 先拿证据再改，⛔ 不猜 ✅
		//   · 基础 = 总计 − 灯光组 − 性能 − 显示/核显；外设电量在构建阶段**不执行**（后台刷新）。
		uiBuildTotal.Stop();
		_uiBuildProfiling = false;
		long uiBuildLayoutMs = (long)(_uiBuildLayoutTicks * 1000.0 / Stopwatch.Frequency);
		long uiBuildTotalMs = uiBuildTotal.ElapsedMilliseconds;
		long uiBuildLightMs = _uiBuildLightMs;
		long uiBuildPerfMs = _uiBuildPerfMs;
		long uiBuildBaseMs = Math.Max(0L, uiBuildTotalMs - uiBuildLightMs - uiBuildPerfMs - uiBuildDisplayMs);
		// ⚠️ 通用路径（⛔ 不分平台）：分段计时对所有机型一致，便于 GCU/同方/宝龙达横向对比 ✅
		if (Interlocked.Exchange(ref _uiBuildLogOnce, 1) == 0)
		{
			int uiBuildControlCount = CountAllControls(this);
			Logger.WriteError($"[ui-build] 分段耗时：基础={uiBuildBaseMs} ms · 灯光组={uiBuildLightMs} ms · 性能={uiBuildPerfMs} ms"
				+ $" · 外设电量=0 ms（构建阶段不执行，已改后台） · 显示/核显={uiBuildDisplayMs} ms · 总计={uiBuildTotalMs} ms"
				+ $"（InitializeComponent={uiBuildInitMs} ms · 其它构建={uiBuildOtherMs} ms · 来源=设置窗构造）");
			Logger.WriteError($"[ui-build] 组=灯光 总计={uiBuildLightMs} ms · 组=性能 总计={uiBuildPerfMs} ms · 组=显示/核显 总计={uiBuildDisplayMs} ms（创建/添加/布局细分见合计行）");
			Logger.WriteError($"[ui-build] 合计：创建+添加≈{uiBuildTotalMs - uiBuildLayoutMs} ms · 布局={uiBuildLayoutMs} ms（次数={_uiBuildLayoutCount}，含嵌套） · 总计={uiBuildTotalMs} ms · 控件数={uiBuildControlCount}");
		}
	}

	private void BuildOfficialConsolePanel()
	{
		_officialConsolePanel = new BufferedPanel
		{
			Name = "panelOfficialConsole",
			CardStyle = true,
			Height = D(112),
			Padding = new Padding(D(12), D(8), D(12), D(8)),
			BackColor = UiVisualStyle.Surface
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = _officialConsolePanel.BackColor
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(110)));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(140)));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, D(56)));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, D(56)));
		Label label = new Label
		{
			Name = "labelOfficialConsoleTitle",
			Text = Strings.OfficialConsole,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			
		};
		UiVisualStyle.ApplyGlyph(label, UiGlyph.Kind.Console);
		_officialConsoleStatus = new Label
		{
			Text = Strings.Detecting,
				Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			AutoEllipsis = true,
			
		};
		_officialConsoleButton = new RButton
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(D(8), 0, 0, 0),
			// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace（日间与 SurfaceRaised 同值，像素不变）。
			BackColor = UiVisualStyle.ButtonFace,
			ForeColor = UiVisualStyle.Text,
			Cursor = Cursors.Hand
		};
		_officialConsoleButton.Click += OfficialConsoleButton_Click;
		tableLayoutPanel.Controls.Add(label, 0, 0);
		tableLayoutPanel.Controls.Add(_officialConsoleStatus, 1, 0);
		tableLayoutPanel.Controls.Add(_officialConsoleButton, 2, 0);
		// ⭐⭐ 2026-10-18（机主：PawnIO 安装入口）：⭐ 官方控制台下一行（宝龙达未安装时前面加醒目提示）✅
		_pawnioWarning = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiVisualStyle.Danger,
			AutoEllipsis = true,
			Visible = false
		};
		tableLayoutPanel.Controls.Add(_pawnioWarning, 0, 1);
		tableLayoutPanel.SetColumnSpan(_pawnioWarning, 3);
		Label pawnioTitle = new Label
		{
			Name = "labelPawnIODriverTitle",
			Text = Strings.PawnIODriver,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_pawnioStatus = new Label
		{
			Text = Strings.Detecting,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			AutoEllipsis = true
		};
		_pawnioButton = new RButton
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(D(8), 0, 0, 0),
			BackColor = UiVisualStyle.ButtonFace,
			ForeColor = UiVisualStyle.Text,
			Cursor = Cursors.Hand,
			Text = Strings.PawnIODownloadInstall
		};
		_pawnioButton.Click += PawnIOButton_Click;
		tableLayoutPanel.Controls.Add(pawnioTitle, 0, 2);
		tableLayoutPanel.Controls.Add(_pawnioStatus, 1, 2);
		tableLayoutPanel.Controls.Add(_pawnioButton, 2, 2);
		_officialConsolePanel.Controls.Add(tableLayoutPanel);
		UiVisualStyle.ApplyTitle(label);
		UiVisualStyle.ApplyMuted(_officialConsoleStatus);
		if (!Program.UiAuditMode)
		{
			RefreshOfficialConsolePanel();
		}
		_officialConsoleStatusTimer.Tick += (object? _, EventArgs _) =>
		{
			RefreshOfficialConsolePanel();
		};
		Shown += (object? _, EventArgs _) =>
		{
			RefreshOfficialConsolePanel();
			RefreshPawnIOPanel();
			if (!Program.UiAuditMode)
			{
				_officialConsoleStatusTimer.Start();
			}
		};
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
	}

	/// <summary>
	/// 2026-10-05「核显模式显示策略」③：设置对话框里的「核显模式显示」项。
	/// 2026-10-09（igpugate）：由两态开关改成**三选** —— 自动（按硬件）/ 强制显示 / 强制隐藏。
	///   * 自动 = 由硬件能力位判据决定（MechrevoHw.IgpuOnlyHardwareVerdict，含 40 系）；
	///   * 强制显示 = ⭐ 用户要的**后路**：判断出错（该显示却隐藏）时在这里手动开回来；
	///   * 强制隐藏 = 老 hide 语义。
	/// 样式与同文件 _igpuSavingRow（核显省电 OFF/ON/AUTO）同构：分段按钮 + Activated 高亮。
	/// ⚠️ 本项的**可见性不再跟着 igpu_only_ui 判据走**（见 IgpuOnlyUiRowAvailable）——
	/// 判据判成"隐藏"时这一项必须还在，否则用户无法把它改回来。
	/// </summary>
	/// <summary>
	/// 2026-10-09（ui-compact，用户原话「这个按钮也太大了」）：两套三态选择行 —— 设置里的「核显模式显示」
	/// （igpu_only_ui）与两行按行灯光项（light_strip_ui / light_logo_ui）—— **共用**的工厂：
	/// 尺寸 / 字号 / 间距只有一个出处，
	/// 避免下次两处又长得不一样。
	///
	/// 2026-10-09（ui-align，用户原话「排布不合适」）：**排布改成与整页其它行同构**（照「官方控制台」那张卡）：
	///   * 列：`ColumnCount=2`，第 0 列 `Absolute D(110)` —— 与「官方控制台」的标签列**同宽**（标签从此同列）、
	///     第 1 列 `Percent 100` 吃满剩余宽度；
	///   * 行 1：左＝标题（`Dock=Fill`、`TextAlign=MiddleLeft`、高 D(28)），右＝控件组
	///     （`FlowLayoutPanel`，`Dock=Right` + `AutoSize` ⇒ **右对齐**、贴着卡片右边，与整页"右侧控件"的节奏一致；
	///     组内三颗按钮仍 AutoSize 宽、统一 D(6) 间距 ⇒ 右对齐也不会拉伸/截断）；
	///   * 行 2：muted 子说明（`ColumnSpan=2`、`Anchor=Left`）—— 与标签**同列起始**、紧贴该行下方，
	///     照抄「日志级别」/「隐私与反馈」那两处的子说明写法，不再自成一块。
	/// 尺寸口径（逻辑像素，按 DPI 乘算）：高 D(28)（= 行内 mini-chip 那档）、字号 8.5pt（比正文 9pt 略小）、
	/// 左右内边距各 D(10)、相邻间距 D(6)、圆角 12（同 mini-chip）。宽度由文案量出来 ⇒ 不留空白、也不截断。
	/// </summary>
	private ThreeStateRow BuildThreeStateRow(string panelName, string titleName, string titleText, UiGlyph.Kind glyph, string tip, string hint, string autoName, string showName, string hideName, Action onAuto, Action onShow, Action onHide, Func<int, int> D)
	{
		Panel panel = new BufferedPanel
		{
			Name = panelName,
			CardStyle = true,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = new Padding(0, 0, 0, D(6)),
			Padding = new Padding(D(12), D(6), D(12), D(6)),
			BackColor = UiVisualStyle.Surface
		};
		BufferedTableLayoutPanel table = new BufferedTableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 2,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = panel.BackColor
		};
		// 与「官方控制台」同一套列宽：标签列 D(110)，控件列吃满剩余（见上面注释）。
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(110)));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label title = new Label
		{
			Name = titleName,
			Text = titleText,
			AutoSize = false,
			Height = D(28),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 0, D(8), 0),
			BackColor = panel.BackColor
		};
		UiVisualStyle.ApplyTitle(title);
		UiVisualStyle.ApplyGlyph(title, glyph);
		// 控件组：Dock=Right + AutoSize ⇒ 整组右对齐、宽度由三颗按钮的文案决定（与「系统」那一行右侧开关组同一写法）。
		FlowLayoutPanel choices = new FlowLayoutPanel
		{
			Name = panelName + "Choices",
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Dock = DockStyle.Right,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = panel.BackColor
		};
		RButton button = CreateCompactChoiceButton(autoName, Strings.IgpuOnlyModeAuto, D, new Padding(0, 0, D(6), 0));
		RButton button2 = CreateCompactChoiceButton(showName, Strings.IgpuOnlyModeShow, D, new Padding(0, 0, D(6), 0));
		RButton button3 = CreateCompactChoiceButton(hideName, Strings.IgpuOnlyModeHide, D, Padding.Empty);
		button.Click += (object? _, EventArgs _) => onAuto();
		button2.Click += (object? _, EventArgs _) => onShow();
		button3.Click += (object? _, EventArgs _) => onHide();
		toolTip.SetToolTip(title, tip);
		toolTip.SetToolTip(button, tip);
		toolTip.SetToolTip(button2, tip + "\r\n" + hint);
		toolTip.SetToolTip(button3, tip);
		choices.Controls.Add(button);
		choices.Controls.Add(button2);
		choices.Controls.Add(button3);
		table.Controls.Add(title, 0, 0);
		table.Controls.Add(choices, 1, 0);
		// 行 2：muted 子说明（与标签同列起始、横跨两列、紧贴该行下方）。
		Label hintLabel = new Label
		{
			Name = titleName + "Hint",
			Text = hint,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiVisualStyle.Muted,
			Font = UiVisualStyle.Font(8.5f),
			Margin = new Padding(0, D(2), 0, 0),
			BackColor = panel.BackColor
		};
		toolTip.SetToolTip(hintLabel, hint);
		table.Controls.Add(hintLabel, 0, 1);
		table.SetColumnSpan(hintLabel, 2);
		panel.Controls.Add(table);
		return new ThreeStateRow(panel, button, button2, button3, hintLabel);
	}

	/// <summary>2026-10-09（ui-compact）：一套三态选择行的控件引用（两处共用同一个工厂的产物）。</summary>
	private sealed class ThreeStateRow
	{
		internal ThreeStateRow(Panel panel, RButton auto, RButton show, RButton hide, Label hint)
		{
			Panel = panel;
			Auto = auto;
			Show = show;
			Hide = hide;
			Hint = hint;
		}

		internal Panel Panel { get; }

		internal RButton Auto { get; }

		internal RButton Show { get; }

		internal RButton Hide { get; }

		internal Label Hint { get; }
	}

	/// <summary>
	/// 2026-10-09（ui-compact）：三态行里的一颗**紧凑**分段按钮。
	/// **只有高度被钉住**（宽用 int.MaxValue 表示不限），宽度交给 AutoSize 按文案量
	/// （`自动（按硬件）` 完整显示，任何 DPI 下都不截断）；
	/// 2026-10-09（lightspeed，用户原话「不要做成三个颜色」）：⚠️ 原来按语义下发
	/// 蓝(自动)/绿(强制显示)/红(强制隐藏) 三色描边（BorderColor + ActivatedColor），
	/// 于是**三颗按钮永远各带一个颜色**，选中只是把同一颜色的 1px 边加成 2px ⇒ 连"选没选中"都看不出来。
	/// 现在整页统一（与刷新率行 / MiniLED·FHD·HDR / 性能·显卡行同一画法）：
	///   * 未选中 ⇒ 中性（不下发 BorderColor ⇒ RButton 走 ButtonEdge 那圈中性硬边，底色 ButtonFace）；
	///   * 选中   ⇒ `ActivatedOutline` + 不下发 ActivatedColor ⇒ RButton.DrawActivatedOutline 取
	///     `UiVisualStyle.AccentSelectionOutline`（整页的强调色 2 设备像素描边 + 中性底 + 粗体文字）。
	/// hover/pressed 反馈不变（仍由 RButton 自身的 MouseOver/MouseDown 面与 HoverFace 提供）。
	/// ⚠️ 尺寸 / 字号 / 间距 / 圆角 / 排布一个字节都没动：只删了两行配色下发。
	/// 对齐参照 = 行内 mini-chip：RButton + 圆角 12 + AutoSize 宽。
	/// </summary>
	private RButton CreateCompactChoiceButton(string name, string text, Func<int, int> D, Padding margin)
	{
		return new RButton
		{
			Name = name,
			Text = text,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Height = D(28),
			MinimumSize = new Size(0, D(28)),
			// ⚠️ 宽度上限给 int.MaxValue（不能用 0：那会被 Control 的尺寸约束当成"宽上限=0"）。
			MaximumSize = new Size(int.MaxValue, D(28)),
			TextAlign = ContentAlignment.MiddleCenter,
			Cursor = Cursors.Hand,
			Font = UiVisualStyle.Font(8.5f),
			Margin = margin,
			Padding = new Padding(D(10), 0, D(10), 0),
			BorderRadius = 12,
			BackColor = UiVisualStyle.ButtonFace,
			// 未选中＝中性；选中＝整页强调色描边（不下发 ActivatedColor/BorderColor，见方法注释）。
			ActivatedOutline = true
		};
	}

	/// <summary>
	/// 2026-10-05「核显模式显示策略」③：设置对话框里的「核显模式显示」三选行。
	/// 2026-10-09（igpugate）：两态开关改成**三选** —— 自动（按硬件）/ 强制显示 / 强制隐藏；
	/// 2026-10-09（ui-compact）：与「额外灯光项」共用 <see cref="BuildThreeStateRow"/>（同一尺寸/字号/间距）。
	///   * 自动 = 由硬件能力位判据决定（MechrevoHw.IgpuOnlyHardwareVerdict，含 40 系）；
	///   * 强制显示 = ⭐ 用户要的**后路**：判断出错（该显示却隐藏）时在这里手动开回来；
	///   * 强制隐藏 = 老 hide 语义。
	/// ⚠️ 本项的**可见性不再跟着 igpu_only_ui 判据走**（见 IgpuOnlyUiRowAvailable）——
	/// 判据判成"隐藏"时这一项必须还在，否则用户无法把它改回来。
	/// </summary>
	private void BuildIgpuOnlyUiPanel()
	{
		ThreeStateRow row = BuildThreeStateRow("panelIgpuOnlyUi", "labelIgpuOnlyUiTitle", Strings.IgpuOnlyShowToggle, UiGlyph.Kind.VideoCard, Strings.IgpuOnlyModeTip, Strings.IgpuOnlyModeForceShowHint, "buttonIgpuOnlyAuto", "buttonIgpuOnlyForceShow", "buttonIgpuOnlyForceHide", () => SetIgpuOnlyUiMode(IgpuOnlyUiMode.Auto), () => SetIgpuOnlyUiMode(IgpuOnlyUiMode.Show), () => SetIgpuOnlyUiMode(IgpuOnlyUiMode.Hide), D);
		_igpuOnlyUiPanel = row.Panel;
		_igpuOnlyUiAuto = row.Auto;
		_igpuOnlyUiShow = row.Show;
		_igpuOnlyUiHide = row.Hide;
		SyncIgpuOnlyUiChoice();
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
	}

	/// <summary>
	/// 2026-10-09（lightsplit，用户原话「额外灯光项应该细分」）：设置对话框里的**两行**按行三选行——
	/// 「灯带行显示」（键 light_strip_ui）与「Logo 灯行显示」（键 light_logo_ui，见 LightExtraUiMode.cs）
	/// —— 与「核显模式显示」**同一个工厂**（BuildThreeStateRow / CreateCompactChoiceButton），
	/// 尺寸 / 字号 / 间距逐项一致，不新造视觉。
	///   * 自动 = **该行**的硬件判据（灯带行 BaseRowPhantom / Logo 行 LogoRowSupported；证据不足一律留）；
	///   * 强制显示 = 该行原样保留（判据判错时的后路）；强制隐藏 = 只收掉**该行**。
	/// ⚠️ 拆成两行才让"隐灯带、留 Logo"（16Pro 2024）与"两行都藏"都能直接表达，不必再靠旧键改义硬凑。
	/// ⚠️ 只影响灯光组里「灯带行 / Logo 行」的可见性与通道数，不改灯效下发 / 主题 / 去重。
	/// ⚠️ 两行的可见性见 <see cref="LightExtraRowAvailable"/> —— **恒为可见**（承载"判据判错时的后路"）。
	/// </summary>
	private void BuildLightRowUiPanels()
	{
		// ⚠️ 两行都走**既有**的 BuildThreeStateRow / CreateCompactChoiceButton（与「核显模式显示」同构），
		// 不新造视觉；只是每行接自己的键（light_strip_ui / light_logo_ui），互不影响。
		ThreeStateRow strip = BuildThreeStateRow("panelLightStripUi", "labelLightStripUiTitle", Strings.LightStripUiTitle, UiGlyph.Kind.Toggles, Strings.LightStripUiTip, Strings.LightStripUiHint, "buttonLightStripAuto", "buttonLightStripForceShow", "buttonLightStripForceHide", () => SetLightRowUiMode(isStripRow: true, LightStripUiMode.Auto), () => SetLightRowUiMode(isStripRow: true, LightStripUiMode.Show), () => SetLightRowUiMode(isStripRow: true, LightStripUiMode.Hide), D);
		ThreeStateRow logo = BuildThreeStateRow("panelLightLogoUi", "labelLightLogoUiTitle", Strings.LightLogoUiTitle, UiGlyph.Kind.Droplet, Strings.LightLogoUiTip, Strings.LightLogoUiHint, "buttonLightLogoAuto", "buttonLightLogoForceShow", "buttonLightLogoForceHide", () => SetLightRowUiMode(isStripRow: false, LightLogoUiMode.Auto), () => SetLightRowUiMode(isStripRow: false, LightLogoUiMode.Show), () => SetLightRowUiMode(isStripRow: false, LightLogoUiMode.Hide), D);
		_lightStripUiPanel = strip.Panel;
		_lightStripUiAuto = strip.Auto;
		_lightStripUiShow = strip.Show;
		_lightStripUiHide = strip.Hide;
		_lightLogoUiPanel = logo.Panel;
		_lightLogoUiAuto = logo.Auto;
		_lightLogoUiShow = logo.Show;
		_lightLogoUiHide = logo.Hide;
		SyncLightRowUiChoices();
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
	}

	/// <summary>
	/// 2026-10-09（lightsplit）：把**两行**三颗按钮的高亮分别同步到**用户的选择**
	/// （light_strip_ui / light_logo_ui，各经 LightRowUiValue 解读 auto/show/hide）—— 显示的是选择本身，
	/// 不是"当前实际收没收这一行"那个派生结果。无禁用态：auto 判成收行时，用户仍必须能点「强制显示」。
	/// </summary>
	private void SyncLightRowUiChoices()
	{
		if (_lightStripUiPanel == null || _lightLogoUiPanel == null)
		{
			return;
		}
		_lightStripUiAuto.Activated = string.Equals(LightStripUiMode.Current(), LightStripUiMode.Auto, StringComparison.OrdinalIgnoreCase);
		_lightStripUiShow.Activated = string.Equals(LightStripUiMode.Current(), LightStripUiMode.Show, StringComparison.OrdinalIgnoreCase);
		_lightStripUiHide.Activated = string.Equals(LightStripUiMode.Current(), LightStripUiMode.Hide, StringComparison.OrdinalIgnoreCase);
		_lightLogoUiAuto.Activated = string.Equals(LightLogoUiMode.Current(), LightLogoUiMode.Auto, StringComparison.OrdinalIgnoreCase);
		_lightLogoUiShow.Activated = string.Equals(LightLogoUiMode.Current(), LightLogoUiMode.Show, StringComparison.OrdinalIgnoreCase);
		_lightLogoUiHide.Activated = string.Equals(LightLogoUiMode.Current(), LightLogoUiMode.Hide, StringComparison.OrdinalIgnoreCase);
		_lightStripUiAuto.Enabled = true;
		_lightStripUiShow.Enabled = true;
		_lightStripUiHide.Enabled = true;
		_lightLogoUiAuto.Enabled = true;
		_lightLogoUiShow.Enabled = true;
		_lightLogoUiHide.Enabled = true;
	}

	/// <summary>
	/// 2026-10-09（lightsplit）：按行三选行的一次落盘 + 刷新（只写该行自己那把键）。
	/// RefreshDeviceCapabilities 会把 flag5/flag6 带进布局签名，所以选择一变就会真实重排灯光组与通道数；
	/// 灯效下发一律不经过这里。
	/// </summary>
	private void SetLightRowUiMode(bool isStripRow, string mode)
	{
		if (_lightStripUiPanel == null || _lightLogoUiPanel == null)
		{
			return;
		}
		if (isStripRow)
		{
			LightStripUiMode.Set(mode);
			Logger.WriteInfo($"[lightmerge] 灯光额外汇（灯带行）：用户选择 light_strip_ui={LightStripUiMode.Current()}（只影响灯带行可见性与通道数）");
		}
		else
		{
			LightLogoUiMode.Set(mode);
			Logger.WriteInfo($"[lightmerge] 灯光额外汇（Logo 行）：用户选择 light_logo_ui={LightLogoUiMode.Current()}（只影响 Logo 行可见性与通道数）");
		}
		SyncLightRowUiChoices();
		RefreshDeviceCapabilities();
	}

	/// <summary>
	/// 2026-10-09（igpugate）：把三颗按钮的高亮同步到**用户的选择**（config 的 igpu_only_ui，
	/// 经 IgpuOnlyUiMode 解读：auto/show/hide）。它显示的是用户的选择，**不是**「当前实际会不会
	/// 显示核显模式」那个派生结果 —— 否则 auto 判成隐藏的机型上会看不出自己选的是什么。
	/// </summary>
	private void SyncIgpuOnlyUiChoice()
	{
		if (_igpuOnlyUiAuto == null)
		{
			return;
		}
		_syncingIgpuOnlyUi = true;
		try
		{
			string mode = IgpuOnlyUiMode.Current();
			_igpuOnlyUiAuto.Activated = string.Equals(mode, IgpuOnlyUiMode.Auto, StringComparison.OrdinalIgnoreCase);
			_igpuOnlyUiShow.Activated = string.Equals(mode, IgpuOnlyUiMode.Show, StringComparison.OrdinalIgnoreCase);
			_igpuOnlyUiHide.Activated = string.Equals(mode, IgpuOnlyUiMode.Hide, StringComparison.OrdinalIgnoreCase);
			// 可见即代表可用（IgpuOnlyUiRowAvailable 已把 30 系/无独显挡在外面）。
			bool available = IgpuOnlyUiRowAvailable();
			_igpuOnlyUiAuto.Enabled = available;
			_igpuOnlyUiShow.Enabled = available;
			_igpuOnlyUiHide.Enabled = available;
		}
		finally
		{
			_syncingIgpuOnlyUi = false;
		}
	}

	/// <summary>
	/// 2026-10-09（igpugate）：这一行/这一项什么时候**显示**。
	/// 关键点（用户要求「后路必须真的可达」）：它**不再**由「当前会不会显示核显模式」或
	/// 「是不是 40 系」决定 —— 它现在承载的是"手动覆盖硬件判据"，所以只要这台机器**有可能**用到
	/// 核显模式，这一项就得在（哪怕 auto 已经判定为隐藏）：
	///   * 30 系：CanOfferIgpuOnly 里 L1 硬屏蔽，选什么都改不了结果 ⇒ 不显示（避免"点了没用"）；
	///   * NoDgpu（枚举成功但没有 NV 适配器）：本工程不为其造显卡面板/托盘项 ⇒ 不显示；
	///   * 其余（40 / 50 / 未知世代）：**一律显示**（50 系这一项现在也真的会改变结果，
	///     因为三态判断排在 Gen50 的硬件分支之前）。
	/// 判定用 GpuGenerationProvider（持久化在 config 的 dgpu_generation），与 MechrevoHw.DgpuGeneration 同源，
	/// 因此硬件还没连上时也能判。
	/// </summary>
	private static bool IgpuOnlyUiRowAvailable()
	{
		// ⭐⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式）：
		//   「宝龙达没有核显模式 · 设置里面核显模式显示也只给同方开放」
		//   ⇒ 宝龙达机型上这一行**彻底不出现**，连「手动强制显示」这个后路都不给。
		//   ⚠️ 为什么必须显式排除、不能只靠下面的显卡代际判据：
		//     下面的判据只排除 Gen30 / NoDgpu，而 `dgpu_generation` 在**未探测出来**时是
		//     Unknown ⇒ Unknown 不等于 Gen30 ⇒ 仍会判"可用"⇒ 在宝龙达上照样显示。
		//     所以这里按**后端**（是不是宝龙达）先判，与显卡代际无关。
		//   ⚠️ 判据优先级高于配置：即使配置文件里是 igpu_only_ui=show（手动强制显示），
		//     宝龙达下也**不显示**——这正是用户要的"连后路都不给"。
		//   ⚠️ 能力层依据：宝龙达 WMI 功能号 9 `GpuMode` 只有 Hybrid=0 / Discrete=1，
		//     **没有 iGPU-only 这一档**。
		//   ⚠️ 非宝龙达逐位不变：本方法对其余机型的返回值与改动前完全一致。
		if (Program.hw?.UsesBaolongda ?? false)
		{
			return false;
		}
		DgpuGenerationKind generation = GpuGenerationProvider.Current().Generation;
		return generation != DgpuGenerationKind.Gen30 && generation != DgpuGenerationKind.NoDgpu;
	}

	/// <summary>
	/// 2026-10-09（lightfix2 / lightsplit）：设置里两行按行灯光项（灯带行 / Logo 灯行）**永远显示**。
	/// 改因：这两行承载的是「手动覆盖灯光行判据」，**不能由被判定的那套判据决定它自己可不可达** ——
	/// 旧口径（"本机可能有灯带/Logo 行，或用户已表过态，才显示"）在**最需要它的机器上正好不显示**：
	/// 耀世 16Pro 2024 画像判否、两行又被判据收掉 ⇒ 整块不出现，用户没法把 hide 改回 auto。
	/// 与「核显模式显示」那行（<see cref="IgpuOnlyUiRowAvailable"/>，30 系点了确实没用才不显示）不同：
	/// 这两行点一下总能改写 light_strip_ui / light_logo_ui，不存在"点了没用"的机型。
	/// 只影响这两种行的 Visible（±2 行高度，宽度与「核显模式显示」同尺寸）；不读能力位、不发任何指令、
	/// 不写任何硬件设置。
	/// </summary>
	private bool LightExtraRowAvailable()
	{
		return true;
	}

	/// <summary>2026-10-09（igpugate）：三选行的一次落盘 + 刷新（原 ApplyIgpuOnlyUiSwitch 的等价物）。</summary>
	private void SetIgpuOnlyUiMode(string mode)
	{
		if (_syncingIgpuOnlyUi || _igpuOnlyUiPanel == null)
		{
			return;
		}
		// 只写这一个键 + 迁移标记；走 IgpuOnlyUiMode.Set 才不会把新写的 show 当成老值解读。
		IgpuOnlyUiMode.Set(mode);
		// 手动改这一项 = 用户已就这个问题表过态，别再让升级引导覆盖它
		AppConfig.Set("igpu_only_asked", 1);
		// 2026-10-06 D-1：这一项同样是**明确表态**（强制显示=有 / 强制隐藏=没有）；
		// 选「自动」= 把决定权交回硬件/兜底逻辑，按"未表态"记录（不额外断言）。
		if (string.Equals(mode, IgpuOnlyUiMode.Show, StringComparison.OrdinalIgnoreCase))
		{
			IgpuOnlyAttempt.RememberAnswer(show: true);
		}
		else if (string.Equals(mode, IgpuOnlyUiMode.Hide, StringComparison.OrdinalIgnoreCase))
		{
			IgpuOnlyAttempt.RememberAnswer(show: false);
		}
		else
		{
			IgpuOnlyAttempt.RememberUnknownAnswer();
		}
		AppConfig.Flush();
		Logger.WriteLine($"iGPU-only UI mode -> igpu_only_ui={mode} (asked=1)");
		// 重新同步不会把选择弹回去（这里显示的是用户的选择，不是派生结果）；两个刷新调用：
		// RefreshDeviceCapabilities() 会按 CanOfferIgpuOnly 重建显卡区与核显模式按钮，
		// SetContextMenu() 会重建托盘菜单里的核显模式项 —— 两者都不回调本方法，不会递归。
		SyncIgpuOnlyUiChoice();
		RefreshDeviceCapabilities();
		SetContextMenu();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主）：⭐ 设置页 PawnIO 行 —— 状态/按钮/宝龙达提醒刷新（幂等）✅
	/// </summary>
	private void RefreshPawnIOPanel()
	{
		if (_pawnioStatus == null || _pawnioButton == null || _pawnioWarning == null)
		{
			return;
		}
		if (_pawnioBusy)
		{
			return;
		}
		PawnIOInstaller.Status status = PawnIOInstaller.GetStatus();
		bool baolongda = Program.hw?.UsesBaolongda == true;
		_pawnioStatus.Text = status.Installed
			? (status.Version.Length > 0
				? string.Format(Strings.PawnIODriverInstalledFormat, status.Version)
				: Strings.PawnIODriverInstalled)
			: Strings.PawnIODriverNotInstalled;
		_pawnioButton.Text = status.Installed ? Strings.PawnIOReinstall : Strings.PawnIODownloadInstall;
		_pawnioButton.Enabled = !Program.UiAuditMode;
		bool showWarning = baolongda && !status.Installed;
		_pawnioWarning.Text = showWarning ? Strings.PawnIOBaolongdaDependency : "";
		_pawnioWarning.Visible = showWarning;
		// 警告行高度：显示时 24 逻辑像素，否则 0（不占空行）
		if (_officialConsolePanel?.Controls.Count > 0 && _officialConsolePanel.Controls[0] is TableLayoutPanel table
			&& table.RowStyles.Count > 1)
		{
			int warningHeight = showWarning ? ResponsiveLayout.LogicalToDevice(this, 24) : 0;
			table.RowStyles[1] = new RowStyle(SizeType.Absolute, warningHeight);
			_officialConsolePanel.Height = ResponsiveLayout.LogicalToDevice(this, 112 + (showWarning ? 24 : 0));
		}
		Logger.WriteLineIfChanged("pawnio-settings-status",
			"[pawnio] 设置页：状态=" + (status.Installed ? "已安装" + (status.Version.Length > 0 ? " " + status.Version : "") : "未安装")
			+ " · 宝龙达=" + baolongda + " · 按钮=" + _pawnioButton.Text);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主）：⭐ 复用现有 `Install-PawnIO.ps1`（与 CpuTuningForm 同一入口），
	/// 管理员启动；运行中按钮禁用并显示“正在下载并安装…”；结束刷新状态 ✅
	/// </summary>
	private async void PawnIOButton_Click(object sender, EventArgs e)
	{
		if (_pawnioBusy || _pawnioButton == null || Program.UiAuditMode)
		{
			return;
		}
		_pawnioBusy = true;
		_pawnioButton.Enabled = false;
		string previousText = _pawnioButton.Text;
		_pawnioButton.Text = Strings.PawnIOInstalling;
		if (_pawnioStatus != null)
		{
			_pawnioStatus.Text = Strings.PawnIOInstalling;
		}
		PawnIOInstaller.Status before = PawnIOInstaller.GetStatus();
		try
		{
			int exitCode = await PawnIOInstaller.RunInstallerAsync();
			PawnIOInstaller.Status after = PawnIOInstaller.GetStatus();
			Logger.WriteLine("[pawnio] 设置页：状态=" + (before.Installed ? "已安装" : "未安装")
				+ " · 点击安装 ⇒ 结果=" + (exitCode == 0 ? "成功" : (exitCode == 3010 ? "需要重启" : "失败"))
				+ "（退出码=" + exitCode + "）· 安装后=" + (after.Installed ? "已安装" : "未安装"));
			if (exitCode == 3010)
			{
				if (_pawnioStatus != null)
				{
					_pawnioStatus.Text = Strings.PawnIODriverRestart;
				}
			}
			else if (exitCode != 0)
			{
				if (_pawnioStatus != null)
				{
					_pawnioStatus.Text = Strings.PawnIOInstallFailed + "（" + exitCode + "）";
				}
				ToastForm.ShowFailure(Strings.PawnIOInstallFailed);
			}
		}
		catch (Exception ex)
		{
			if (_pawnioStatus != null)
			{
				_pawnioStatus.Text = Strings.PawnIOInstallFailed;
			}
			ToastForm.ShowFailure(Strings.PawnIOInstallFailed);
			Logger.WriteLine("[pawnio] 设置页安装异常：" + ex.GetType().Name + " " + ex.Message);
		}
		finally
		{
			_pawnioBusy = false;
			if (_pawnioButton != null)
			{
				_pawnioButton.Enabled = true;
				_pawnioButton.Text = previousText;
			}
			RefreshPawnIOPanel();
		}
	}
	private async void OfficialConsoleButton_Click(object? sender, EventArgs e)
	{
		if (_officialConsoleButton == null || Program.UiAuditMode)
		{
			return;
		}
		_officialConsoleButton.Enabled = false;
		OfficialConsoleIsolation.IsolationStatus isolationStatus;
		try
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：变量名与用途都是 GCU（官方控制台隔离判据）⇒ 真正需要 GCU。
			bool gcuConnected = Program.hw?.IsGcuConnected ?? false;
			isolationStatus = await Task.Run(() => OfficialConsoleIsolation.GetStatus(gcuConnected));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Official console status check failed: " + ex.Message);
			_officialConsoleButton.Enabled = true;
			return;
		}
		bool isolate = !isolationStatus.Isolated;
		if (MessageBox.Show(isolate ? Strings.IsolatePrompt : Strings.RestorePrompt, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			ApplyOfficialConsoleStatus(isolationStatus);
			return;
		}
		_officialConsoleStatus.Text = (isolate ? Strings.Isolating : Strings.Restoring);
		if (!isolate)
		{
			OfficialConsoleIsolation.StopGuard();
		}
		OfficialConsoleIsolation.OperationResult operationResult = await OfficialConsoleIsolation.SetIsolationAsync(isolate);
		if (operationResult.Success)
		{
			if (isolate)
			{
				OfficialConsoleIsolation.StartGuardIfNeeded();
			}
			else
			{
				OfficialConsoleIsolation.LaunchOfficialUi();
			}
		}
		else if (!isolate)
		{
			OfficialConsoleIsolation.StartGuardIfNeeded();
		}
		RefreshOfficialConsolePanel();
		_officialConsoleButton.Enabled = true;
		MessageBox.Show(operationResult.Message, "L-Mechrevo", MessageBoxButtons.OK, operationResult.Success ? MessageBoxIcon.Asterisk : MessageBoxIcon.Exclamation);
	}

	private void RefreshOfficialConsolePanel()
	{
		if (!Program.UiAuditMode && _officialConsoleStatus != null && _officialConsoleButton != null && Interlocked.Exchange(ref _officialStatusRefreshRunning, 1) == 0)
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：同上，GCU 官方控制台隔离 ⇒ 真正需要 GCU。
			bool gcuConnected = Program.hw?.IsGcuConnected ?? false;
			RefreshOfficialConsoleStatusAsync(gcuConnected);
		}
	}

	private async Task RefreshOfficialConsoleStatusAsync(bool gcuConnected)
	{
		try
		{
			OfficialConsoleIsolation.IsolationStatus status = await Task.Run(() => OfficialConsoleIsolation.GetStatus(gcuConnected)).ConfigureAwait(continueOnCapturedContext: false);
			Volatile.Write(ref _officialStatusRefreshRunning, 0);
			if (IsDisposed || !IsHandleCreated)
			{
				return;
			}
			try
			{
				BeginInvoke(() =>
				{
					ApplyOfficialConsoleStatus(status);
				});
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Official console status UI update failed: " + ex.Message);
			}
		}
		catch (Exception ex2)
		{
			Volatile.Write(ref _officialStatusRefreshRunning, 0);
			Logger.WriteLine("Official console background status failed: " + ex2.GetBaseException().Message);
		}
	}

	private void ApplyOfficialConsoleStatus(OfficialConsoleIsolation.IsolationStatus status)
	{
		if (_officialConsoleStatus != null && _officialConsoleButton != null && !IsDisposed)
		{
			_officialConsoleStatus.Text = status.Detail;
			Label? officialConsoleStatus = _officialConsoleStatus;
			Color foreColor;
			if (status.Isolated && !status.OfficialUiRunning && status.GcuRunning)
			{
				foreColor = UiVisualStyle.Accent;
			}
			else
			{
				foreColor = ((status.OfficialUiRunning && status.Isolated) ? UiVisualStyle.Danger : UiVisualStyle.Muted);
			}
			officialConsoleStatus.ForeColor = foreColor;
			_officialConsoleButton.Text = (status.Isolated ? Strings.RestoreOfficialConsole : Strings.IsolateOfficialConsole);
			_officialConsoleButton.BorderColor = (status.Isolated ? UiVisualStyle.Accent : UiVisualStyle.Border);
			_officialConsoleButton.Activated = status.Isolated;
			_officialConsoleButton.Enabled = status.OfficialUiInstalled || status.Isolated;
		}
	}

	private void BuildDashboardLayout()
	{
		Control control = Controls.Find("panelLc", searchAllChildren: true).FirstOrDefault();
		Control control2 = Controls.Find("panelQuickSwitch", searchAllChildren: true).FirstOrDefault();
		Control control3 = Controls.Find("panelBrightness", searchAllChildren: true).FirstOrDefault();
		if (control == null || control2 == null || control3 == null)
		{
			return;
		}
		BackColor = UiVisualStyle.Window;
		tablePerf.AutoSize = false;
		tablePerf.ColumnCount = 5;
		tablePerf.RowCount = 1;
		tablePerf.ColumnStyles.Clear();
		for (int i = 0; i < 5; i++)
		{
			tablePerf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
		}
		tablePerf.RowStyles.Clear();
		tablePerf.RowStyles.Add(new RowStyle(SizeType.Absolute, D(34)));
		tablePerf.Height = D(34);
		foreach (Button item2 in tablePerf.Controls.OfType<Button>())
		{
			MakeCompactModeButton(item2);
		}
		picturePerf.Image = UiGlyph.Render(UiGlyph.Kind.Gauge, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		picturePerf.SizeMode = PictureBoxSizeMode.Zoom;
		picturePerf.Visible = true;
		_gpuOcBox = new RCheckBox
		{
			Name = "gpu_oc",
			Text = Strings.GpuOverclock,
			ForeColor = UiVisualStyle.Text,
			Cursor = Cursors.Hand,
			AutoSize = true,
			Margin = new Padding(0, 4, 8, 4)
		};
		bool ocSupported = MechrevoService.IsGcu50;
		_gpuOcBox.Checked = ocSupported && AppConfig.Is("gpu_oc");
		MechrevoService.GpuOverclockEnabled = ocSupported && _gpuOcBox.Checked;
		_gpuOcBox.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingSwitches)
			{
				bool on = _gpuOcBox.Checked;
				AppConfig.Set("gpu_oc", on ? 1 : 0);
				MechrevoService.GpuOverclockEnabled = on;
				MechrevoHw hw = Program.hw;
				// 2026-10-15（C-1 第 2 步分类 ①）：ApplyGpuOverclock 走 GCU ⇒ 真正需要 GCU。
				if (Program.service != null && hw != null && hw.IsGcuConnected)
				{
					await Program.service.ApplyGpuOverclock(on);
				}
			}
		};
		MechrevoHw? hw0 = Program.hw;
		// 2026-10-12（本轮 · 缺陷修复）：判据一字未改 —— 只是把结果**同时记进意图字段**，
		// 供 SyncPerfShortcutsVisibility / BuildTitleRow 在构造期（有效 Visible 恒 false）使用。
		_perfShortcutGpuOcIntent = Program.service != null && Program.service.CurrentMode == 1 && hw0 != null && MechrevoService.IsGcu50;
		_gpuOcBox.Visible = _perfShortcutGpuOcIntent;
		// 2026-10-04：30 系「配置编辑」按钮 —— 先建好，再作为 extraControl 传入 BuildHeadRow，
		// 落在「显卡OC」与「风扇强冷」之间（仅 30 系可见）
		perfConfigEditButton = new RButton
		{
			Tag = "mini-chip",
			BorderRadius = 12,
			Text = Strings.ModeConfigShort + Strings.Edit,
			AutoSize = false,
			// 2026-10-04：必须给明确高度 —— 只设 Width 时用 RButton 默认高度，8f 文字会被压裁
			// 标题行高 D(26)，按钮取 D(24) 留上下各 1px
			Size = new Size(D(72), D(24)),
			FlatStyle = FlatStyle.Flat,
			// 2026-10-04：字号比相邻标签(8.5)再小一点；底色用 Track（比 SurfaceRaised 深）
			Font = UiVisualStyle.Font(8f),
			// 2026-10-04：用户要求底色更深 —— Muted 实心底 + AccentText 前景
			// （日间 90,107,128 底/白字；夜间 143,163,191 底/深字，两边对比度都够）
			ForeColor = UiVisualStyle.Text,
			Visible = false
		};
		perfConfigEditButton.FlatAppearance.BorderSize = 0;
		perfConfigEditButton.Click += async (object? _, EventArgs _) => await OpenPerfConfigEditorAsync();
		// 2026-10-11（UI 重排 · 用户原话「性能模式旁边的指示性能的文字删除」）：性能模式标题行右侧那颗
		// 「当前模式指示文字」（原 labelCPUFan，显示"平衡/狂暴"之类的模式名）已**整体删除** ——
		// 字段/设计器实例化/初始化赋值全部移除，本行只留「图标 + 性能模式」。
		// ⚠️ 2026-10-11 第三轮（用户原话「回退 布局错乱 …」「更正风扇强冷/显卡超频/配置编辑放在标题行左对齐」）：
		//    这里的临时表头容器 `value` 会被 BuildTelemetryRow → BuildPerfModeRow 整体丢弃并重建为
		//    「标题行（图标+标题+三个快捷开关左对齐+CPU 读数右对齐）/ 按钮行（只有模式按钮）」两行；
		//    下面这行只作为重建前的占位（picturePerf / labelPerf 由新标题行复用）。
		BufferedTableLayoutPanel value = BuildHeadRow(picturePerf, labelPerf, null);
		panelCPUTitle.AutoSize = false;
		panelCPUTitle.Height = D(26);
		panelCPUTitle.Controls.Clear();
		panelCPUTitle.Controls.Add(value);
		panelPerformance.AutoSize = false;
		panelPerformance.Padding = new Padding(D(12), D(2), D(12), 0);
		panelPerformance.Height = D(66);
		_perfHeightCompact = D(66);
		// 2026-10-08（回归修复）：配置行高由 D(28) 改为 D(34)（见 BuildPerfConfigRow），
		// 顶部吸附布局下这里必须同步给够高度，否则配置行底部会被面板裁掉。
		// 2026-10-08（下拉改矮）：行高常量收到 D(26)，这里同步 +D(26)。
		_perfHeightWithConfig = D(66) + D(26);
		// 2026-10-04：30 系「模式 × 配置」—— 性能按钮下方横排三个配置下拉
		// ⭐ 分段计时：性能组（配置行）
		Stopwatch uiPerfPhase = Stopwatch.StartNew();
		BuildPerfConfigRow(D);
		_uiBuildPerfMs += uiPerfPhase.ElapsedMilliseconds;
		tableGPU.AutoSize = false;
		tableGPU.RowStyles.Clear();
		tableGPU.RowCount = 1;
		tableGPU.RowStyles.Add(new RowStyle(SizeType.Absolute, D(34)));
		tableGPU.Height = D(34);
		foreach (Button item3 in tableGPU.Controls.OfType<Button>())
		{
			MakeCompactModeButton(item3);
		}
		buttonEco.Text = Strings.IgpuShort;
		buttonStandard.Text = Strings.HybridShort;
		buttonUltimate.Text = Strings.DirectShort;
		toolTip.SetToolTip(buttonEco, Strings.IgpuModeTooltip);
		toolTip.SetToolTip(buttonStandard, Strings.HybridModeTooltip);
		toolTip.SetToolTip(buttonUltimate, Strings.DirectModeTooltip);
		MechrevoHw igpuHw0 = Program.hw;
		// 2026-10-08（QA 开关）：判据整体搬进 IgpuSavingRowVisible（原表达式一字未改），
		// 只为在末尾多挂一条**默认关闭**的 QA 旁路 —— 打开 LMECHREVO_UI_QA=1 时才把这一行显示出来看样式。
		bool canIgpuSaving = IgpuSavingRowVisible(igpuHw0);
		if (_igpuSavingOff == null)
		{
			_igpuSavingLabel = new Label
			{
				Text = Strings.IgpuSaving,
				AutoSize = false,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = UiVisualStyle.Muted,
				Font = UiStyleCaptionFont(),
				Margin = Padding.Empty
			};
			_igpuSavingOff = new RButton
			{
				Text = Strings.IgpuSavingOff,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiStyleBody(),
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace。此前这三颗没有显式底色，只能继承
				// 父容器（＝页面底 Window），面与底同色 ⇒ 未选中时几乎看不出是按钮；现在与其它按钮一致地"浮起"。
				// 日间 ButtonFace = 233/239/246，与 ControlHelper.AdjustControls 一贯下发的 RForm.buttonMain 相同。
				BackColor = UiVisualStyle.ButtonFace,
				BorderColor = RForm.colorEco,
				// 2026-10-09（UI 打磨③，仅夜间）：这行「核显省电 OFF/ON/AUTO」也是横向选项组（已是独立按钮，
				// 但选中态原先只靠文字色变化，几乎看不出来）。统一改成 ②b 的「彩边 + 深底」，
				// 强调色沿用本按钮既有的语义边色（OFF=绿/ON=蓝/AUTO=红，与 BorderColor 同一取值）。
				// 2026-10-09（UI 打磨④）：不再下发内缩值 —— 选中态一律深底铺满整颗 + 那圈彩边就是按钮自己的
				// 边框（选中时 BorderColor 那圈被抑制），故全行只有一层边。
				ActivatedColor = RForm.colorEco,
				ActivatedOutline = true
			};
			_igpuSavingOn = new RButton
			{
				Text = Strings.IgpuSavingOn,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiStyleBody(),
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace（理由同行首 OFF 那颗）。
				BackColor = UiVisualStyle.ButtonFace,
				BorderColor = RForm.colorStandard,
				ActivatedColor = RForm.colorStandard,
				ActivatedOutline = true
			};
			_igpuSavingAuto = new RButton
			{
				Text = Strings.IgpuSavingAuto,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				Cursor = Cursors.Hand,
				Font = UiStyleBody(),
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				// 2026-10-09（UI 打磨⑤，仅夜间）：按钮面 → ButtonFace（理由同行首 OFF 那颗）。
				BackColor = UiVisualStyle.ButtonFace,
				BorderColor = RForm.colorTurbo,
				ActivatedColor = RForm.colorTurbo,
				ActivatedOutline = true
			};
			_igpuSavingRow = new BufferedTableLayoutPanel
			{
				Dock = DockStyle.Bottom,
				Height = D(30),
				ColumnCount = 4,
				Margin = Padding.Empty,
				Padding = Padding.Empty
			};
			_igpuSavingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			_igpuSavingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			_igpuSavingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			_igpuSavingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
			_igpuSavingRow.Controls.Add(_igpuSavingLabel, 0, 0);
			_igpuSavingRow.Controls.Add(_igpuSavingOff, 1, 0);
			_igpuSavingRow.Controls.Add(_igpuSavingOn, 2, 0);
			_igpuSavingRow.Controls.Add(_igpuSavingAuto, 3, 0);
			_igpuSavingOff.Click += (object? _, EventArgs _) => { _ = SwitchIgpuSavingAsync("OFF"); };
			_igpuSavingOn.Click += (object? _, EventArgs _) => { _ = SwitchIgpuSavingAsync("ON"); };
			_igpuSavingAuto.Click += (object? _, EventArgs _) => { _ = SwitchIgpuSavingAsync("AUTO"); };
		}
		if (!panelGPU.Controls.Contains(_igpuSavingRow))
		{
			panelGPU.Controls.Add(_igpuSavingRow);
		}
		_igpuSavingRow.Visible = canIgpuSaving;
		VisualiseIgpuSaving();
		panelGPUTitle.AutoSize = false;
		panelGPUTitle.Height = D(26);
		pictureGPU.Image = UiGlyph.Render(UiGlyph.Kind.VideoCard, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		pictureGPU.SizeMode = PictureBoxSizeMode.Zoom;
		pictureGPU.Visible = true;
		panelGPUTitle.Controls.Clear();
		labelGPUFan.ForeColor = UiVisualStyle.Muted;
		labelGPUFan.Font = UiVisualStyle.Font(8.5f);
		panelGPUTitle.Controls.Add(BuildHeadRow(pictureGPU, labelGPU, labelGPUFan));
		labelTipGPU.Visible = false;
		panelGPU.AutoSize = false;
		panelGPU.Padding = new Padding(D(12), D(2), D(12), 0);
		panelGPU.Height = D(canIgpuSaving ? 100 : 66);
		// ⚠️ 这里先按旧口径给个底高，真正的卡片高度由 BuildGpuModeRow 末尾的 Panel_GPU_ApplyHeight()
		// 按「上内边距 + 标题面板上外边距 + 标题行 + 按钮行 + 核显省电行（可见时）」重算
		// （2026-10-11 第三轮：标题行 + 按钮行两行都装在 panelGPUTitle 里，卡片 = 2 + 4 + D(26) + D(34)）。
		panelBatteryTitle.AutoSize = false;
		panelBatteryTitle.Height = D(26);
		pictureBattery.Image = UiGlyph.Render(UiGlyph.Kind.Battery, ResponsiveLayout.LogicalToDevice(this, 16), UiVisualStyle.Muted);
		pictureBattery.SizeMode = PictureBoxSizeMode.Zoom;
		pictureBattery.Visible = true;
		labelBatteryTitle.Text = Strings.Battery;
		panelBattery.Padding = new Padding(D(12), D(2), D(12), D(2));
		panelBattery.MinimumSize = Size.Empty;
		panelBattery.Height = D(56);
		// ⭐⭐ 2026-10-18（机主：宝龙达电池卡片仍为空）：⭐ 本方法在电池表构建**之后**运行，
		//   上面两行会清掉 RecalculateBatteryTableHeight 设置的 MinimumSize/高度 ⇒ 宝龙达充电阈值面板被裁掉。
		//   ⇒ 这里重新按统一算法恢复（仅宝龙达且面板可用；非宝龙达保持原样，⛔ 不触发 EC）✅
		if (Program.hw?.UsesBaolongda == true && _bdChargePanelRowIndex >= 0
			&& BatteryLimitPanel.IsSupportedBoard(Program.hw))
		{
			RecalculateBatteryTableHeight();
			if (panelBattery.Height < panelBattery.MinimumSize.Height)
			{
				panelBattery.Height = panelBattery.MinimumSize.Height;
			}
		}
		control.Height = D(112);
		control3.Height = D(94);
		panelVersion.Height = D(40);
		panelFooter.AutoSize = false;
		panelFooter.Padding = new Padding(D(6), D(3), D(6), D(3));
		panelFooter.Height = D(44);
		tableButtons.AutoSize = false;
		tableButtons.Dock = DockStyle.Fill;
		tableButtons.Margin = Padding.Empty;
		foreach (Button item4 in tableButtons.Controls.OfType<Button>())
		{
			item4.Dock = DockStyle.Fill;
			item4.Margin = new Padding(D(3));
			item4.Font = UiVisualStyle.Font(8.5f);
		}
		Control[] array = new Control[4] { panelScreen, panelKeyboard, panelPeripherals, panelStartup };
		for (int j = 0; j < array.Length; j++)
		{
			array[j].Visible = false;
		}
		if (_officialConsolePanel == null)
		{
			return;
		}
		_officialConsolePanel.Height = D(48);
		BuildThemeModePanel(D);
		uiPerfPhase.Restart();
		BuildTelemetryRow();
		_uiBuildPerfMs += uiPerfPhase.ElapsedMilliseconds;
		control.Dock = DockStyle.Top;
		_liquidGroup = new RCollapseGroup(Strings.LiquidCoolingShort, UiGlyph.Kind.Droplet, "group_lc_open", defaultExpanded: false, showDivider: true);
		_liquidGroup.SetContent(control);
		_liquidGroup.Toggled += (object? _, EventArgs _) =>
		{
			UpdateDashboardWindowHeight();
			UpdateDashboardScroll();
		};
		control2.Dock = DockStyle.Top;
		_quickGroup = new RCollapseGroup(Strings.MoreSwitches, UiGlyph.Kind.Chevrons, "group_quick_open", defaultExpanded: false);
		_quickGroup.SetContent(control2);
		_quickGroup.Toggled += (object? _, EventArgs _) =>
		{
			UpdateDashboardWindowHeight();
			if (_quickGroup.Expanded)
			{
				_fitQuickCards?.Invoke();
			}
			UpdateDashboardScroll();
			TableLayoutPanel? qsRoot = control2.Controls["quickSwitchRoot"] as TableLayoutPanel;
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];
			Logger.WriteLine($"[QS] toggled exp={_quickGroup.Expanded} groupH={_quickGroup.Height} contentH={control2.Height} contentPref={control2.PreferredSize.Height} rootH={qsRoot?.Height} rootPref={qsRoot?.PreferredSize.Height} rootRows={qsRoot?.RowCount} sleepH={sleepPanel?.Height} sleepPref={sleepPanel?.PreferredSize.Height} sleepCount={sleepPanel?.Controls.Count}");
			if (qsRoot != null)
			{
				Logger.WriteLine("[QS] rowheights=" + string.Join("/", qsRoot.GetRowHeights()));
			}
			if (sleepPanel is FlowLayoutPanel sleepFlp && sleepFlp.Controls.Count > 0 && sleepFlp.Controls[0] is RCheckBox firstSleep)
			{
				Logger.WriteLine($"[QS] firstSleep text=[{firstSleep.Text}] size={firstSleep.Size} pref={firstSleep.GetPreferredSize(Size.Empty)} vis={firstSleep.Visible}");
			}
		};
		_lightGroup = new RCollapseGroup(Strings.Lighting, UiGlyph.Kind.Gear, "group_light_open");
		_lightGroup.Toggled += (object? _, EventArgs _) =>
		{
			UpdateDashboardWindowHeight();
			UpdateDashboardScroll();
			// ⭐ 2026-10-19（机主：Logo 开关跑到最右）：展开/折叠灯光组后立即按平台重排（幂等；body.Layout 也会兜一次）✅
			ApplyBaolongdaLightRowLayout(Program.hw);
		};
		// ⭐ 分段计时：灯光组（键盘/灯带/Logo/铰链/同步行）
		Stopwatch uiLightPhase = Stopwatch.StartNew();
		BuildLightRowsPanel();
		_uiBuildLightMs += uiLightPhase.ElapsedMilliseconds;
		BuildGcuStatusStrip(D);
		_unsupportedModelBanner = new BufferedPanel
		{
			Name = "panelUnsupportedModelNotice",
			Dock = DockStyle.Top,
			Height = D(64),
			CardStyle = true,
			Padding = new Padding(D(16), D(6), D(16), D(6)),
			Visible = false
		};
		_unsupportedModelLabel = new Label
		{
			Name = "labelUnsupportedModelNotice",
			Dock = DockStyle.Fill,
			ForeColor = UiVisualStyle.Text,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			Font = UiVisualStyle.Font(8.5f)
		};
		_unsupportedModelBanner.Controls.Add(BuildUnsupportedModelRow(D));
		_dashboardSections = new Control[9] { _panelGcuStatus, _unsupportedModelBanner, panelPerformance, panelGPU, control3, panelBattery, _liquidGroup, _lightGroup, _quickGroup };
		_enabledDashboardSections.Clear();
		array = _dashboardSections;
		foreach (Control item in array)
		{
			_enabledDashboardSections.Add(item);
		}
		_dashboardScroll = new BufferedPanel
		{
			Name = "dashboardPageHost",
			Dock = DockStyle.Fill,
			AutoScroll = false,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		_dashboardStack = new BufferedTableLayoutPanel
		{
			Name = "dashboardStack",
			Dock = DockStyle.None,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			GrowStyle = TableLayoutPanelGrowStyle.AddRows,
			ColumnCount = 1,
			Margin = Padding.Empty,
			// 2026-10-11（用户原话「可否缩小一些 界面两边边距」）：仪表盘栈的左右内边距 16 → 8 逻辑像素。
			// 这一层是"窗口边到卡片边"那圈留白的主要来源（另两层是窗体自身 Padding 6 与卡片自身 Padding）。
			// 改前每侧 6(窗体) + 16(本层) + 16或12(卡片) = 38/34；改后 6 + 8 + 12 = 26 逻辑像素。
			// ⚠️ 不缩到 0：卡片是圆角卡片，贴边会显得挤、且滚动条紧贴内容；8 逻辑像素与卡片内 12 一起
			// 仍保持"卡片浮在底色上"的观感（与卡片之间的垂直间距 8/10 同量级）。
			Padding = new Padding(D(8), D(10), D(8), D(12)),
			BackColor = UiVisualStyle.Window
		};
		_dashboardStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		_dashboardScroll.Controls.Add(_dashboardStack);
		_dashboardScrollBar = new RScrollBar
		{
			Name = "dashboardScrollBar",
			Dock = DockStyle.Right,
			Width = D(8),
			BackColor = UiVisualStyle.Window
		};
		_dashboardScroll.Controls.Add(_dashboardScrollBar);
		_dashboardScrollBar.BringToFront();
		_dashboardScrollBar.ValueChanged += (object? _, EventArgs _) =>
		{
			UpdateDashboardScroll(rangeChanged: false);
		};
		_dashboardStack.SizeChanged += (object? _, EventArgs _) =>
		{
			UpdateDashboardScroll();
		};
		_dashboardScroll.SizeChanged += (object? _, EventArgs _) =>
		{
			UpdateDashboardScroll();
		};
		RefreshThemeButtons();
		_dashboard = new BufferedTableLayoutPanel
		{
			Name = "dashboardLayout",
			Dock = DockStyle.Fill,
			AutoSize = false,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = UiVisualStyle.Window,
			Padding = Padding.Empty,
			
		};
		_dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		_dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, D(51)));
		AttachDashboardChrome();
		array = _dashboardSections;
		foreach (Control obj in array)
		{
			obj.Dock = DockStyle.Top;
			obj.Margin = new Padding(0, 0, 0, D(6));
		}
		panelPerformance.Margin = new Padding(0, 0, 0, D(2));
			_liquidGroup.Margin = new Padding(0, D(4), 0, D(6));
		ApplyRowSkins();
		RefreshThemeButtons();
		array = new Control[3] { _themeModePanel, _officialConsolePanel, panelVersion };
		for (int j = 0; j < array.Length; j++)
		{
			array[j].Visible = false;
		}
		BuildFooterV2(D);
		UiVisualStyle.ApplyTitle(labelPerf);
		UiVisualStyle.ApplyTitle(labelGPU);
		UiVisualStyle.ApplyTitle(labelBatteryTitle);
		// 2026-10-12（「快捷开关」标题行）：与上面三条**同一处、同一口径**下发标题样式（9.5pt 粗体 + 主题文字色）。
		if (_quickTileHeaderLabel != null)
		{
			UiVisualStyle.ApplyTitle(_quickTileHeaderLabel);
		}
		UiVisualStyle.ApplyWindow(this);
		Controls.Add(_dashboard);
		_dashboard.BringToFront();
		UiVisualStyle.ApplyWindow(this);
		ArrangeDashboard();
		RefreshThemeButtons();
		int D(int value2)
		{
			return ResponsiveLayout.LogicalToDevice(this, value2);
		}
		static void MakeCompactModeButton(Button button)
		{
			button.Image = null;
			button.BackgroundImage = null;
			button.TextImageRelation = TextImageRelation.Overlay;
			button.TextAlign = ContentAlignment.MiddleCenter;
			button.ImageAlign = ContentAlignment.MiddleCenter;
			button.Font = UiStyleBody();
			button.Margin = Padding.Empty;
			button.Padding = Padding.Empty;
		}
	}

	private void BuildGcuStatusStrip(Func<int, int> scale)
	{
		_panelGcuStatus = new BufferedPanel
		{
			Name = "panelGcuStatus",
			CardStyle = true,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Padding = new Padding(scale(12), scale(7), scale(12), scale(7)),
			BackColor = UiVisualStyle.Surface,
			Margin = new Padding(0, 0, 0, scale(6)),
			AccessibleRole = AccessibleRole.StatusBar
		};
		_labelGcuStatus = new Label
		{
			Name = "labelGcuStatus",
			AutoSize = true,
			Text = Strings.GcuStatusUnknown,
			ForeColor = UiVisualStyle.Muted,
			BackColor = _panelGcuStatus.BackColor,
			Font = UiVisualStyle.Font(9f, FontStyle.Bold),
			Cursor = Cursors.Hand,
			
		};
		_labelGcuStatus.Click += (object? _, EventArgs _) =>
		{
			RefreshGcuStatus();
		};
		_panelGcuStatus.Controls.Add(_labelGcuStatus);
		UpdateGcuStatus();
	}

	internal void UpdateGcuStatus()
	{
		if (_labelGcuStatus != null && _panelGcuStatus != null && !IsDisposed)
		{
			MechrevoHw hw = Program.hw;
			// 2026-10-09（批次3 #7）：重连循环在「传输还在、只是没订上」时会**让位**（IsReconnecting 会归 0），
			// 那一档仍应显示成琥珀色「连接中」（与改动前一致）⇒ 把 IsSessionAwaitingSubscribe 一并算进去。
			// ⚠️ 只影响这一处状态档位，⛔ 不改任何连接/订阅/退避行为。
			// 2026-10-15（非 GCU 后端）：顶部状态显示「已就绪 / 未就绪」 —— 用户 2026-10-15 原话：
			// 「对 就是对非GCU方法的 标题改成已就绪 GCU不影响 如果不支持为GCU未连接(GCU机型)/未就绪」
			// 事实：这类后端（老接口 root\WMI:MICommonInterface，以及将来给宝龙达准备的那套 ACPI）
			// **完全不经过 GCU/MQTT** ⇒ 对它们显示「GCU 未连接」是**误导**。
			// ⚠️ 判据用 **ActiveBackend**（而不是原来的 UsesLegacyMifs）⇒ 以后加宝龙达后端时这里
			//    **一行都不用改**（后端枚举与探测表都在 MechrevoBackend.cs 里）。
			// ⚠️ 「能力是否支持」用**缓存字段** IsReadOnlyDegraded（由 RefreshDeviceCapabilities →
			//    ApplyUnsupportedModelNotice 写入，即 !ShouldDegradeToReadOnly）—— 便宜；
			//    ⛔ 这里**不调** RuntimeModelSupport.Current()（那会开 ACPI 设备，不该在状态刷新里跑）。
			// ⚠️ 只改这一处状态文案/颜色/提示；⛔ 不动面板布局、不动任何连接/订阅行为；
			//    **GCU 机型行为一字不变**（继续走下面既有的 GCU 状态机）。
			bool nonGcuBackend = hw != null && hw.ActiveBackend != MechrevoBackendKind.None && hw.ActiveBackend != MechrevoBackendKind.Gcu;
			if (nonGcuBackend)
			{
				bool backendReady = !IsReadOnlyDegraded;
				string backendText = (backendReady ? Strings.LegacyMifsConnected : Strings.LegacyMifsNotReady);
				Color backendColor = (backendReady ? UiVisualStyle.Ok : UiVisualStyle.Warn);
				if (_labelGcuStatus.Text != backendText)
				{
					_labelGcuStatus.Text = backendText;
				}
				if (_labelGcuStatus.ForeColor != backendColor)
				{
					_labelGcuStatus.ForeColor = backendColor;
				}
				toolTip.SetToolTip(_labelGcuStatus, Strings.LegacyMifsTip);
				return;
			}
			bool gcuBusyOrReconnecting = (hw?.IsReconnecting ?? false) || (hw?.IsSessionAwaitingSubscribe ?? false);
			var (color, text, caption) = GcuConnectionStatus.Describe(GcuConnectionStatus.Resolve(hw != null, hw?.IsConnected ?? false, gcuBusyOrReconnecting, hw != null && hw.ConnectionGeneration > 0, hw?.ReconnectAgeMs ?? 0));
			if (_labelGcuStatus.Text != text)
			{
				_labelGcuStatus.Text = text;
			}
			if (_labelGcuStatus.ForeColor != color)
			{
				_labelGcuStatus.ForeColor = color;
			}
			toolTip.SetToolTip(_labelGcuStatus, caption);
		}
	}

	internal void RefreshGcuStatus()
	{
		if (IsDisposed || !IsHandleCreated)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(() =>
			{
				UpdateGcuStatus();
			});
		}
		else
		{
			UpdateGcuStatus();
		}
	}

	private void BuildThemeModePanel(Func<int, int> scale)
	{
		if (_themeModePanel == null)
		{
			_themeModePanel = new BufferedPanel
			{
				Name = "panelThemeMode",
				CardStyle = true,
				Height = scale(54),
				Padding = new Padding(scale(12), scale(8), scale(12), scale(8)),
				BackColor = UiVisualStyle.Surface
			};
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				BackColor = _themeModePanel.BackColor
			};
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, ThemeColumnPercentages.Title));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, ThemeColumnPercentages.Day));
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, ThemeColumnPercentages.Night));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, scale(56)));
			Label label = new Label
			{
				Name = "labelThemeModeTitle",
				Text = Strings.UiAppearance,
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty,
				ForeColor = UiVisualStyle.Text,
				Font = UiVisualStyle.Font(9f, FontStyle.Bold)
			};
			UiVisualStyle.ApplyGlyph(label, UiGlyph.Kind.Contrast);
			_dayModeButton = CreateThemeButton(Strings.LightTheme, night: false);
			_nightModeButton = CreateThemeButton(Strings.DarkTheme, night: true);
			tableLayoutPanel.Controls.Add(label, 0, 0);
			tableLayoutPanel.Controls.Add(_dayModeButton, 1, 0);
			tableLayoutPanel.Controls.Add(_nightModeButton, 2, 0);
				_themeFollowBox = new RCheckBox
				{
					Name = "checkFollowSystem",
					Text = Strings.FollowSystem,
					Tag = "theme_follow",
					AutoSize = true,
					Dock = DockStyle.Fill,
					BackColor = UiVisualStyle.Surface,
					ForeColor = UiVisualStyle.Text,
					Cursor = Cursors.Hand,
					Checked = UiVisualStyle.FollowSystemTheme,
					Margin = new Padding(scale(16), 0, 0, 0)
				};
				_themeFollowBox.CheckedChanged += (object? _, EventArgs _) =>
				{
					UiVisualStyle.SetFollowSystemTheme(_themeFollowBox.Checked);
					ApplyThemeMode(UiVisualStyle.WindowsDarkTheme());
					RefreshThemeButtons();
				};
				// 跟随系统开关已移至设置对话框的界面标题行
				BeginInvoke((Action)(() =>
				{
					if (!IsDisposed && _themeModePanel != null)
					{
						_themeModePanel.Invalidate(true);
					}
				}));
			_themeModePanel.Controls.Add(tableLayoutPanel);
		}
		RefreshThemeButtons();
		Button CreateThemeButton(string text, bool night)
		{
			Button button = new Button();
			button.Name = (night ? "buttonNightMode" : "buttonDayMode");
			button.Text = text;
			button.Dock = DockStyle.Fill;
			button.Margin = new Padding(scale(4), 0, scale(4), 0);
			button.FlatStyle = FlatStyle.Flat;
			button.Tag = "theme_mode";
			button.UseVisualStyleBackColor = false;
			button.Cursor = Cursors.Hand;
			button.Click += (object? _, EventArgs _) =>
			{
				UiVisualStyle.SetFollowSystemTheme(false);
				ApplyThemeMode(night);
			};
			ApplyRoundedRegion(button, scale(8));
			button.Resize += (object? _, EventArgs _) => ApplyRoundedRegion(button, scale(8));
			return button;
		}
	}

	internal string DashboardPageAuditName(int index)
	{
		return DashboardAuditPageNames[0];
	}

	internal void SelectDashboardPageForAudit(int index)
	{
		SelectDashboardPage(index, persist: false);
	}

	/// <summary>
	/// 2026-10-09（igpuask-once）：核显模式这一问的「显示前占用」闸门 —— 进程内静态位 + 落盘键双重保护。
	/// 为什么必须**在显示之前**写 <c>igpu_only_asked=1</c>：
	///   * Program.cs 的两个入口都是"先排队、后显示"：启动期 Application.Idle（Program.cs:778-780）
	///     与首次从托盘唤起时的两次 BeginInvoke（Program.cs:2125-2127）；
	///   * BeginInvoke 排的两个委托落在同一个消息泵里，而第一个委托的 <c>ShowDialog</c> 是**嵌套消息循环**
	///     —— 它会继续处理第二个已排队的委托。此时第一个对话框还没关闭，
	///     <c>FirstRunGuideForm.FormClosed</c> 里的落盘（FirstRunGuideForm.cs:387-416）尚未执行，
	///     于是 <c>igpu_only_asked</c> 仍是 0，第二个入口就又把同一个问题弹了一遍（"同时弹了两个"）。
	/// 旧实现把标记写成"对话框返回后才落盘"，任何"显示期间的重入"都拦不住；现在改成先占用再显示。
	/// 语义未变：占用只写 igpu_only_asked（= 已经问过），回答（igpu_only_ui / igpu_only_answer）仍由
	/// FirstRunGuideForm 关闭时写；机型作用域键（ModelScopedConfig）与三态取值一字未动。
	/// </summary>
	private static int _igpuGuideClaimed;

	private static bool TryClaimIgpuOnlyGuide()
	{
		if (AppConfig.Get("igpu_only_asked", 0) == 1)
		{
			// 用户（或本进程更早的一次询问）已经表过态
			return false;
		}
		if (Interlocked.CompareExchange(ref _igpuGuideClaimed, 1, 0) != 0)
		{
			// 本进程内已经有另一次调用占用了这一问（同一轮消息泵里的并发/重入）
			return false;
		}
		// ⭐ 显示前先落盘（不只是内存）：即使后续异常/进程被杀，也不会再问第二次
		AppConfig.Set("igpu_only_asked", 1);
		AppConfig.Flush();
		Logger.WriteLine("iGPU-only UI guide claimed before showing: igpu_only_asked=1 written pre-display (内存位+落盘双重保护)");
		return true;
	}

	internal void ShowFirstRunGuideIfNeeded()
	{
		if (Program.UiAuditMode || IsDisposed || AppConfig.Get("onboarding_version", 0) >= 1)
		{
			return;
		}
		// ⭐⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式）：
		//   「首次提示的核显模式只给同方开放」⇒ 宝龙达下第 4 步那一问**不问**。
		//   ⚠️ 这里**必须跳过 TryClaimIgpuOnlyGuide()**，不能只是"不显示"：
		//     它会在显示前把 `igpu_only_asked=1` 落盘。若宝龙达下也写，用户以后
		//     换到同方机器就**永远不再问**这一问 ⇒ 那样是隐性数据损坏。
		//   ⚠️ `igpu_only_ui` 也不写：宝龙达没有这一档，写 show/hide 都会误导设置页判据
		//     （见 IgpuOnlyUiRowAvailable 的宝龙达排除）。
		//   ⚠️ 完整引导本身照常弹（第 1/2/3/5 步对宝龙达一样有用），只是第 4 步不建
		//     —— 见 FirstRunGuideForm 里的 skipIgpuOnlyStep。
		//   ⚠️ 非宝龙达逐位不变：多出的只是一个恒 false 的条件。
		if (!(Program.hw?.UsesBaolongda ?? false))
		{
			// 2026-10-09（igpuask-once）：首次引导的第 4 步就是「核显模式」那一问 —— 显示前先把这一问占用掉
			// （内存位 + 落盘），否则启动期/托盘唤起那一对 BeginInvoke 里的升级引导会在本窗体的嵌套消息循环里
			// 再把同一个问题弹一次。返回 false（用户已表过态）不影响完整引导本身照常弹出。
			TryClaimIgpuOnlyGuide();
		}
		using FirstRunGuideForm firstRunGuideForm = new FirstRunGuideForm();
		DialogResult dialogResult = firstRunGuideForm.ShowDialog(this);
		AppConfig.Set("onboarding_version", 1);
		if (dialogResult == DialogResult.OK)
		{
			SelectDashboardPage(2, persist: true);
		}
	}

	/// <summary>
	/// 2026-10-05「核显模式显示策略」④⑤：升级用户也需要问一次「有没有集显模式这一档」。
	/// 老用户 onboarding_version 已是 1，看不到首次引导，所以这里单独弹一次只含该问题的向导。
	/// 门控键 igpu_only_asked；2026-10-09（igpuask-once）改为**显示前**由 TryClaimIgpuOnlyGuide 占用并落盘
	/// （原先由 FirstRunGuideForm 在 FormClosed 落盘 —— 那是"显示后才置位"，拦不住显示期间的重入）。
	/// §3④：只在判据不可靠的机型上问 —— 50 系有可靠能力位（直接显示，不问），
	/// 30 系恒隐藏（问了也没意义）。这两种情况不置 asked，判据变化后仍会问一次。
	/// </summary>
	internal void ShowIgpuOnlyGuideIfNeeded()
	{
		if (Program.UiAuditMode || IsDisposed)
		{
			return;
		}
		if (AppConfig.Get("igpu_only_asked", 0) != 1)
		{
			MechrevoHw? hw = Program.hw;
			// ⭐⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式）：
			//   「首次提示的核显模式只给同方开放」⇒ 宝龙达下这一问**完全不弹、也不占用**。
			//   ⚠️ 必须挡在 TryClaimIgpuOnlyGuide() 之前：否则会写 `igpu_only_asked=1`，
			//     用户以后换同方机器就永远不再问（隐性数据损坏）。
			//   ⚠️ `hw == null`（后端还没就绪）时按**非宝龙达**处理（保守：维持原行为），
			//     所以这里用 `hw?.UsesBaolongda ?? false` 而不是先判 hw 非空。
			//   ⚠️ 非宝龙达逐位不变：多出的只是一个恒 false 的条件。
			bool baolongdaMachine = hw?.UsesBaolongda ?? false;
			// 2026-10-06（igpuscope）：「只在判据不可靠的机型上问」用的也是同一个「可靠能力位」判据 ——
			// 40 系（哪怕它上报了 40 系的 iGPUModeOnlySupport）同样要问一次：40 系的决定权已经交回用户开关。
			// 2026-10-09（igpuask-once）：TryClaimIgpuOnlyGuide() 必须在**创建窗体之前**调用 ——
			// 它把 igpu_only_asked=1 先落盘（+ 进程内静态位），这是"绝不重复弹"的唯一保证点。
			if (!baolongdaMachine && (hw == null || (hw.DgpuGeneration != DgpuGenerationKind.Gen30 && !hw.HasReliableIgpuOnlyCapability)) && TryClaimIgpuOnlyGuide())
			{
				using FirstRunGuideForm firstRunGuideForm = new FirstRunGuideForm(igpuOnlyStepOnly: true);
				firstRunGuideForm.ShowDialog(this);
			}
		}
		// 无论本次是否弹过（含刚走完首次引导、第 4 步已作答的新用户），都把三选行与 GPU 行/托盘刷新一次
		SyncIgpuOnlyUiChoice();
		RefreshDeviceCapabilities();
		SetContextMenu();
	}

	private void SelectDashboardPage(int index, bool persist)
	{
	}

	private static bool? _rtx50Cache;

	private static bool? _hasIgpuCache;

	private static bool HasIntegratedGpu()
	{
		if (_hasIgpuCache.HasValue)
		{
			return _hasIgpuCache.Value;
		}
		bool result = false;
		try
		{
				using System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
			foreach (System.Management.ManagementObject managementObject in searcher.Get())
			{
				string? name = managementObject["Name"]?.ToString();
				if (name != null && (name.Contains("Intel(R)", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)))
				{
					result = true;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("IGPU detection failed: " + ex.Message);
		}
		_hasIgpuCache = result;
		return result;
	}

	private static bool IsRtx50SeriesGpu()
	{
		if (_rtx50Cache.HasValue)
		{
			return _rtx50Cache.Value;
		}
		bool result = false;
		try
		{
								// 优先使用 LM 持久化的 dGPU 代际信号（启动时探测并落盘；
				// 混合/核显模式下独显未被 Win32_VideoController 枚举时依然可靠）
				string? generation = AppConfig.GetString("dgpu_generation", null);
				if (string.Equals(generation, "Gen50", StringComparison.OrdinalIgnoreCase))
				{
					_rtx50Cache = true;
					return true;
				}
using System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
			foreach (System.Management.ManagementObject managementObject in searcher.Get())
			{
				string? name = managementObject["Name"]?.ToString();
				if (name != null && (name.Contains("RTX 50", StringComparison.OrdinalIgnoreCase) || name.Contains("RTX50", StringComparison.OrdinalIgnoreCase)))
				{
					result = true;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RTX50 detection failed: " + ex.Message);
		}
		_rtx50Cache = result;
		return result;
	}

	/// <summary>
	/// <summary>
	/// 2026-10-10（限充代系门控 · 用户要求「为 40 系及以后的机型开放，30 系不显示这个开关」；
	/// 18:40 修正 NoDgpu 办公本放行；18:55 修正「排除 Gen30 及更早」）：
	/// 排除 Gen30 与 Unknown —— Unknown = 有独显但营销名/设备 ID 都识别不出代际（RTX 20 / GTX 16 等
	/// 更早落点，DgpuGenerationProbe 只认 RTX 30/40/50 与三组 DeviceId），即「30 系及更早」全部不显示；
	/// NoDpug（无独显办公本，如无界 15X）与 Gen40 / Gen50 一律开放。判定走 GpuGenerationProvider
	/// （持久化在 config 的 dgpu_generation，硬件没连上也能判）。
	/// </summary>
	private static bool ChargeSliderSupported()
	{
		// 2026-10-15（隐藏规则 B · 用户原话「10系-30系用这个可能损坏硬件必须强制隐藏EC三档和滑块」）：
		// 老机型上**连行都不建**（比建好再收起来更彻底：不存在任何被程序化路径重新点亮的机会）——
		// 判据走 GcuVariant（'1020' = 10/16/20 系、'30' = 30 系），与 EC 侧 IsLegacyGenerationBlocked 同源。
		// ⚠️ 与既有显卡代际判据是**或**关系：任一判「不支持」即不建（两者互不削弱）。
		// ⚠️ 非老机型上本方法返回值与改动前逐字一致（GcuVariant=40/50/WJXY/缺失 ⇒ 该合取项恒真）。
		if (EcChargeLimit.IsLegacyGenerationBlocked())
		{
			Logger.WriteLine("限充代系门控：本机属 10/16/20/30 系 ⇒ 不建「无极限充（实验性）」开关行与滑块行（规则 B：只保留 GCU 路）");
			return false;
		}
		DgpuGenerationKind generation = GpuGenerationProvider.Current().Generation;
		return generation != DgpuGenerationKind.Gen30 && generation != DgpuGenerationKind.Unknown;
	}

	private async Task<bool> ApplyBatteryLimitModeAsync(int mode, int limitPercent)
	{
		// 2026-10-10（传输路由/三方式互斥 · 用户澄清原话「而且我讲清楚 EC限制充电也是那三档按钮,只是打开以后
		// 不走GCU发报,直接用EC走三档,不知道你有没有理解」）：**三档按钮是共用的一组"值来源"**，
		// 「EC 限充」开关只决定**传输方式** ——
		//   · 开关 ON（EC 传输）⇒ 同一档位交给 **EC 直写**（EcChargeLimit 0x7B9 + 写后回读）落地，⛔ 不发 GCU 报文；
		//   · 开关 OFF（GCU 传输）⇒ 同一档位交给 **GCU 官方路径**（BatteryProtection/Control：
		//     0=长效 / 1=平衡 / 2=工作站，与档位下标一一对应），⛔ **不写 EC**（对称规则）。
		// ⚠️ 唯一的拒绝条件是「无极限充（实验性）」滑块生效（它与档位互斥，用户既有偏好：滑块打开时三档置灰）——
		// 闸门判据是**滑块开关** _chargeSliderEnabled，⛔ 不是「EC 限充」开关（EC 限充 ON 时按档位照常走 EC 直写）。
		if (_chargeSliderEnabled)
		{
			Logger.WriteLine($"限充方式互斥：无极限充（实验性）生效 ⇒ 三档点击被拒绝（档位 {limitPercent}% 不落地，滑块为唯一权威）");
			RefreshBatteryModeButtons();
			await Task.CompletedTask;
			return false;
		}
		BatteryControl.SetSelectedModePercent(limitPercent);
		// 2026-10-15（隐藏规则 B · 三档按钮的**路**）：10/16/20/30 系上 EC 直写整体禁用 ⇒ 即使记忆里
		// charge_limit_lock 还是 1（开关在那些机型上已被隐藏、值可能残留），三档也**必须**改走 GCU 官方路径，
		// 否则点下去会走 EC 分支、被 TrySet 的兜底拒绝并弹「未确认」——用户看到的是「点了没反应」。
		// ⚠️ 只改**路由判据**：GCU 分支与 EC 分支的代码一字未动（用户特意强调「注意不删三个按钮」）。
		bool legacyBlocked = EcChargeLimit.IsLegacyGenerationBlocked();
		if (legacyBlocked)
		{
			Logger.WriteLine($"限充传输=GCU（代际门控）：本机属 10/16/20/30 系 ⇒ 三档 {limitPercent}% 强制走 GCU 官方路径（⛔ EC 直写已按代际禁用，可能损坏硬件）");
		}
		if (!BatteryControl.LockEnabled() || legacyBlocked)
		{
			// —— GCU 传输（官方路径）：档位交给 GCU，⛔ 我们一个字都不写 EC ——
			if (!GcuBatteryProtectionAvailable())
			{
				Logger.WriteLine($"限充传输=GCU：GCU 电池保护不可读/未连接 ⇒ 档位 {limitPercent}% 只记进记忆（⛔ 不写 EC、不发报文）");
				// 2026-10-12（用户原话「可以补」）：⛔ 这里不再静默成功 —— 一条报文都没发出去，就必须如实回报
				// **失败**（改前 return true，点击方看到的是"成功"）；并与下方 EC 分支的 ChargeLimitUnavailable
				// 对齐给出用户可见反馈。复用仓库**既有的 GCU 专用键**（两份 resx 早有、此前无人引用）：
				// zh「GCU 未连接，限充模式设置失败」/ en「GCU not connected. Charge limit mode setting failed」。
				ToastForm.ShowFailure(Strings.ChargeLimitGcuDisconnected);
				RefreshBatteryModeButtons();
				await Task.CompletedTask;
				return false;
			}
			bool gcuConfirmed = await PublishTierToGcuAsync(mode, limitPercent, "三档点击（EC 限充关闭 ⇒ 官方 GCU 路径）");
			if (!gcuConfirmed)
			{
				// 用户可见反馈（「GCU 没确认这个值」）已由 PublishTierToGcuAsync 内部弹出 ⇒ 这里只记日志，⛔ 不重复弹。
				// ⛔ 本文件没有 #nullable 上下文：这里故意不写 `MechrevoHw?`（会多一条 CS8632 警告）。
				MechrevoHw hwGcu = Program.hw;
				Logger.WriteLine($"限充传输=GCU：档位 {limitPercent}% 未被 GCU 确认（BatteryProtection={(hwGcu != null ? hwGcu.BatteryProtection : -1)}）；⛔ 不回退写 EC");
			}
			RefreshBatteryModeButtons();
			return gcuConfirmed;
		}
		// —— EC 传输（EC 限充 ON）：直写 EC；⛔ 绝不下发 GCU 档位报文（两个源互相覆盖的根源就在这） ——
		// 2026-10-15（隐藏规则 B）：判据改用 IsEcDirectWriteAllowed()（含代际门控）—— 老机型在这里也**拒绝**，
		// 与 BatteryControl 侧同一把闸（上一步已把它们路由去 GCU 分支，这里是防御性的第二道）。
		if (!BatteryControl.CaptureChargeLimitGesture() || !BatteryControl.ApplyChargeLimitFromUserGesture(limitPercent))
		{
			ToastForm.ShowFailure(Strings.ChargeLimitUnconfirmed);
			RefreshBatteryModeButtons();
			return false;
		}
		await Task.CompletedTask;
		RefreshBatteryModeButtons();
		return true;
	}

	/// <summary>
	/// 2026-10-10（传输路由）：GCU 官方限电方式是否可用 —— 以「<c>System/BatteryProtection</c> 的
	/// HealthProtectionStatus 有上报（≥0）」为能力探测（与 FunctionVerifier 的「电池保护三档」同源）。
	/// ⛔ 只读：不发报文、不写 config / EC、不碰任何控件。
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：宝龙达电池页被隐藏）：⭐ 宝龙达充电阈值面板是否可用（缓存一次）✅。
	/// <para>⚠️ 只有 `UsesBaolongda` 才调用 `BatteryLimitPanel.IsAvailable`（会读 EC 状态）；
	/// ⛔ 非宝龙达直接 false，**不触发任何 EC 访问**（本机 GM5HG0A 逐位不变）✅</para>
	/// </summary>
	private bool BaolongdaChargePanelAvailable(MechrevoHw hardware)
	{
		if (hardware == null || !hardware.UsesBaolongda)
		{
			return false;
		}
		if (!_baolongdaChargePanelProbed)
		{
			// 2026-10-18（机主：电池区域空白）：EC 读不到（常见原因：PawnIO 未安装）时 IsAvailable 为假 ⇒
		//   整块面板不构建、卡片空白。改为按「板号支持」判断 ⇒ 卡片仍显示，面板内部给出「状态读不到 / 需要 PawnIO」。
		_baolongdaChargePanelAvailable = BatteryLimitPanel.IsSupportedBoard(hardware);
			_baolongdaChargePanelProbed = true;
		}
		return _baolongdaChargePanelAvailable;
	}

	private static bool GcuBatteryProtectionAvailable()
	{
		MechrevoHw hw = Program.hw;
		return hw != null && hw.IsConnected && hw.BatteryProtection >= 0;
	}

	/// <summary>三档百分比 → GCU 官方动作序号（长效 100 ⇒ 0 PERFORMANCEDMODE / 平衡 80 ⇒ 1 BALANCEDMODE /
	/// 工作站 60 ⇒ 2 HEALTHYMODE）；非档位值一律按平衡档（与 BatteryControl.NormalizeMode 的默认 80 同源）。</summary>
	private static int ModeIndexForPercent(int percent)
	{
		return percent switch
		{
			100 => 0, 
			60 => 2, 
			_ => 1, 
		};
	}

	/// <summary>
	/// 2026-10-10（传输路由）：把三档选中项交给 **GCU 官方路径**（<c>BatteryProtection/Control</c>）。
	/// ⛔ 只在 GCU 传输生效（EC 限充关闭）时调用；EC 传输期间**绝不下发**任何档位报文。
	/// 失败只记日志（⛔ 不回退去写 EC —— 那正是"两个源同时写"）。
	/// </summary>
	private static async Task<bool> PublishTierToGcuAsync(int mode, int limitPercent, string reason)
	{
		if (!GcuBatteryProtectionAvailable())
		{
			// 2026-10-12（用户原话「可以补」）：通道不可用 ⇒ 如实失败 + 用户可见反馈（⛔ 不再只写日志）。
			ToastForm.ShowFailure(Strings.ChargeLimitGcuDisconnected);
			return false;
		}
		MechrevoHw hw = Program.hw;
		// 只读 GCU **最近一次上报**的档位（BatteryProtection 是 auto-property，⛔ 不发 GET、⛔ 不写 EC）：
		// 用于把下面日志里的「已回读确认」与「已是该值（SetBatteryProtection 内部短路，⛔ 未下发任何报文）」区分开。
		bool alreadySet = hw.BatteryProtection == mode;
		bool confirmed = await hw.SetBatteryProtection(mode);
		Logger.WriteLine($"限充传输=GCU（{reason}）：档位 {limitPercent}% → BatteryProtection={mode}{(confirmed ? (alreadySet ? " 已是该值（GCU 上报已等于目标，⛔ 未重复下发）" : " 已回读确认") : " 未确认")}");
		if (!confirmed)
		{
			// ⭐ 与 EC 分支的 ChargeLimitUnconfirmed（「限充模式切换未确认，请重试」）**文案区分**：这一条
			// 明确是 **GCU 没确认这个值**（EC 侧一个字节都没动，见 MechrevoHw.SetBatteryProtection 的严格相等回读）。
			ToastForm.ShowFailure(Strings.ChargeLimitGcuUnconfirmed);
		}
		return confirmed;
	}

	/// <summary>
	/// 2026-10-10（三方式互斥 · 用户原话「使用EC方式的时候GCU的限电方式应该不能同时生效,防止覆盖」）：
	/// 进入 **EC 直写方式**（EC 限充 ON 或 无极限充滑块 ON）时，请求 GCU 侧切到**长效模式**
	/// （BatteryProtection=0 ＝官方"不限制"档）⇒ GCU 的限电方式不再同时生效；失败只记日志
	/// （其后由 EC 侧的重放/重确立保护我们的值）。⛔ 不改 EC、不启停 GCU 服务、不新增定时器；
	/// 调用方**不 await**（内部最多等一次回读确认 ≈≤2.6s，不阻塞 UI）。
	/// </summary>
	private static async Task EnsureGcuStandsDownAsync(string reason)
	{
		try
		{
			MechrevoHw hw = Program.hw;
			if (hw == null || !hw.IsConnected || hw.BatteryProtection < 0)
			{
				Logger.WriteLine($"限充方式互斥（{reason}）：GCU 电池保护不可读/未连接 ⇒ 无法让 GCU 让位（EC 侧重放仍需保护我们的值）");
				return;
			}
			if (hw.BatteryProtection == 0)
			{
				Logger.WriteLine($"限充方式互斥（{reason}）：GCU 侧已是长效模式（不限制）⇒ 无需下发");
				return;
			}
			bool confirmed = await hw.SetBatteryProtection(0);
			Logger.WriteLine($"限充方式互斥（{reason}）：已请求 GCU 切到长效模式（BatteryProtection=0 不限制）{(confirmed ? "并回读确认" : "但未确认 ⇒ GCU 可能仍在限电")}");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("限充方式互斥：让 GCU 让位失败（不影响 EC 写入）：" + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-08（充电上限 UI 返工）：三档高亮 = BatteryControl.SelectedModePercent()。
	/// 它仍然是「EC 0x7B9 回读优先」（EC 上真有限充时档位就是那个值，不优先信 GCU）；
	/// 只有 EC 上没有任何限充（0/100）时才回落到记忆的档位 —— 这样开关关闭时
	/// 也记得用户选的是哪一档（开关关闭只切档位、不写 EC）。
	/// 2026-10-10（传输路由）：高亮判据**保持不变**（EC 0x7B9 回读优先 ⇒ 反映"物理上真正生效的上限"）。
	/// ⚠️ 曾试过「GCU 方式生效时改以 BatteryProtection 为准」，评估后放弃：GCU 上报 ≠ 物理上限
	/// （EC 上可能有上一条方式留下的值，例如滑块的 73%），改了就可能在 GCU 档位上显示一个并不生效的高亮。
	/// 传输方式本身由「EC 限充」开关的勾选状态表达，⛔ 不需要另一个高亮来源。
	/// </summary>
	internal void RefreshBatteryModeButtons()
	{
		if (_batteryModeButtons == null)
		{
			return;
		}
		int num = BatteryControl.SelectedModePercent();
		int[] array = new int[3] { 100, 80, 60 };
		int currentMode = -1;
		// ⭐⭐ 2026-10-16（参考 beta18 / 0.289.1.0 旧代码，修「GCU 三档点不动」）：
		// 旧代码（存档 archive/checkpoints/存档_20260924/source_modified/SettingsForm.cs:3747）的高亮判据是
		// **优先读 GCU 上报的档位**：
		//     if (hw != null && hw.BatteryProtection >= 0 && hw.BatteryProtection <= 2)
		//         currentMode = hw.BatteryProtection;
		//     else { int limit = AppConfig.Get("charge_limit", 100);
		//            currentMode = (limit >= 95) ? 0 : ((limit >= 80) ? 1 : 2); }
		// ⚠️ 而「三种传输方式」那次改造把它换成了**只读 EC 侧** BatteryControl.SelectedModePercent() ——
		//    于是 GCU 传输（EC 限充关 ⇒ 三档交给 GCU 官方 BatteryProtection/Control）时，
		//    高亮依据的是 **EC 0x7B9 的残留值**，与 GCU 真实档位无关
		//    ⇒ 点完高亮不挪窝 ⇒ 用户的主观结论必然是「点不动」（真机日志恰好印证：请求已下发且已被 GCU 回读确认）。
		// 修法（**只补 GCU 这一层取数，⛔ 不改传输闸门 / EC 写入 / 滑块互斥 / Enabled 任何一处**）：
		//   · EC 直写方式**未生效** ⇒ **优先用 GCU 上报的档位**（0=长效 / 1=平衡 / 2=工作站，与数组下标一一对应）；
		//   · EC 直写方式**生效** ⇒ 跳过本块，**下面原逻辑一字不变**（那时 GCU 值本就不同步，不能拿来当准）。
		if (!ChargeLimitEcPathActive())
		{
			MechrevoHw hwTier = Program.hw;
			if (hwTier != null && hwTier.BatteryProtection >= 0 && hwTier.BatteryProtection <= 2)
			{
				currentMode = hwTier.BatteryProtection;
			}
			// ⚠️ 诊断日志必须用 **WriteError**（Logger.WriteLine 的级别由**消息内容**判定，默认「仅错误」
			// 级别下不保证落盘 ⇒ 机主按默认配置看不到）；并用**状态翻转**去重，⛔ 不刷屏、⛔ 不让 ErrorCount 疯涨。
			string tierHighlightNote = $"[charge-limit] 三档高亮=按 GCU 上报档位 BatteryProtection={currentMode}"
				+ $"（EC 直写未生效 ⇒ 高亮以 GCU 真值为准，与 beta18 旧行为一致）；EC 侧值={num}（仅供参考，⛔ 不再决定高亮）";
			if (Logger.TryMarkChanged("charge-limit-tier-gcu-highlight", tierHighlightNote))
			{
				Logger.WriteError(tierHighlightNote);
			}
		}
		if (currentMode < 0 && EcChargeLimit.IsSupportedLimit(num))
		{
			for (int i = 0; i < 3; i++)
			{
				if (array[i] == num)
				{
					currentMode = i;
					break;
				}
			}
		}
		// 2026-10-12（用户报 bug ③ 原话「三档按钮依旧不可点击 在两个开关都关闭的时候 应该仍然可以点击而且走的GCU路径」）：
		// 上面那条判据在**一个具体状态**下会让三颗**一个都不高亮**：EC 0x7B9 回读到一个 40–99 的**非档位**值
		// （例如实验滑块留下的 73%），此时 `currentMode` 恒为 -1 —— 于是三档虽然**可点**（Enabled=True，
		// 见 ApplyChargeLimitStageEnablement）且点击**真的下发**（见 ApplyBatteryLimitModeAsync），
		// 但点下去**屏幕上没有任何变化**（高亮不会挪窝）⇒ 用户的主观结论必然是"点不动"。
		// 修法（**只补最后一格高亮**，⛔ 不动判据本身）：既然"两个开关都关"＝EC 直写方式未生效
		// ⇒ 三档此时只是**交给 GCU 的档位值来源**（`ApplyBatteryLimitModeAsync` 的 GCU 分支一条 EC 都不写），
		// 那么当 EC 回读给不出任何档位时，就用**我们最后告诉 GCU 的那个档位**（记忆位 `BatteryControl.StoredModePercent()`）
		// 当高亮 —— 它一定落在 {100,80,60} 之一，点击后立刻跟着走（SetSelectedModePercent 在点击路径里已写）。
		// ⚠️ 严格限定：⛔ 只在 `currentMode < 0`（本来一个都不亮）**且** EC 直写方式未生效时兜底 ⇒
		// EC 回读是正常档位值时**行为与改前逐像素相同**；⛔ 不改 EC 写入/传输闸门/滑块互斥/Enabled 任何一处。
		if (currentMode < 0 && !ChargeLimitEcPathActive())
		{
			int num2 = BatteryControl.StoredModePercent();
			for (int j = 0; j < 3; j++)
			{
				if (array[j] == num2)
				{
					currentMode = j;
					break;
				}
			}
			Logger.WriteLineIfChanged("charge-limit-tier-highlight", $"[charge-limit] 三档高亮：EC 回读值 {num} 不是任何档位（40–99 的非档位残留）⇒ 按 GCU 传输用记忆档位 {num2} 兜底高亮（⛔ 未改任何写入/闸门）");
		}
		for (int i = 0; i < 3; i++)
		{
			RButton modeButton = _batteryModeButtons[i];
			bool selected = i == currentMode;
			// 2026-10-09（UI 打磨③ 仅夜间；0.291 beta22 起**日间同构**，用户追加口径「边框高亮 …依旧没有」）：
			// 三档是"限充上限"档位，没有模式专属色 ⇒ 选中态走"中性底 + 强调蓝描边"（ActivatedColor 仍空、
			// SelectionUsesAssignedAccent 仍 false ⇒ RButton 落蓝档）。
			// ⚠️ 改前日间走的是另一条路：`Activated = night && selected` + `BackColor = Accent` +
			// `ForeColor = Surface` ⇒ 选中那颗是**实心饱和蓝块 + 白字**（用户截图里那个"老式实心蓝块"）。
			// 现在两个主题都走同一套：Activated = selected、底 = ButtonFace（中性）、文字 = Text，
			// 选中只由那圈 2 设备像素强调蓝描边表达 ⇒ 夜间像素与改前完全一致（夜间本来就是这一支）。
			modeButton.Activated = selected;
			modeButton.ActivatedColor = Color.Empty;
			modeButton.ActivatedOutline = true;
			modeButton.BackColor = UiVisualStyle.ButtonFace;
			modeButton.ForeColor = UiVisualStyle.Text;
			modeButton.FlatAppearance.BorderColor = UiVisualStyle.Border;
			modeButton.FlatAppearance.BorderSize = 1;
		}
	}

	private void OnBatteryProtectionChanged()
	{
		if (IsDisposed || _batteryModeButtons == null)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action(OnBatteryProtectionChanged));
			return;
		}
		// 2026-10-12（bug ②）：GCU 报回电池保护正是"GCU 通路可用性"变化的时刻 ⇒ 顺带重算三档启用态
		//（⛔ 只改 Enabled，不动任何写入/记忆/下发）。
		ApplyChargeLimitStageEnablement();
		RefreshBatteryModeButtons();
	}


	/// <summary>
	/// 2026-10-08 17:00（修正）：50 系滑块行 —— 与返工前逐项一致：单行、16%/70%/14% 三列
	/// （「限充」标签 / 滑块 / 数值），行高 32。50 系不建「锁定充电上限」开关、不建三档按钮，
	/// 因此滑块不会被任何新控件挤占；上限完全由滑块决定，写入通道与返工前相同：
	/// EcChargeLimit 直写 EC（0x7B9 上限 / 0x7D0 复充下限），写后回读确认。
	/// 2026-10-09（无极限充滑块）：新增 row 参数（50 系＝第 0 行；非 50 系＝滑块行子面板的第 0 行），
	/// 三件套的 Visible 改由 ApplyChargeSliderVisibility() 统一收放（默认关 ⇒ 不上屏）。
	/// 2026-10-10（三档不再对 50 系隐藏）：50 系不再走这一支的"只有滑块"布局，本方法现由
	/// BuildChargeLimitControls 统一调用（滑块行＝第 5 行子面板的第 0 行）；行内列宽那里 50 系已不适用。
	/// </summary>
	private void BuildChargeLimitSliderRow(TableLayoutPanel host, Label valueLabel, int row)
	{
		Label limitLabel = new Label
		{
			Name = "labelBatteryLimit",
			Text = Strings.ChargeLimitShort,
			ForeColor = UiVisualStyle.Text,
			BackColor = panelBattery.BackColor,
			AutoSize = true,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			Font = UiStyleCaptionFont()
		};
		limitLabel.Visible = _chargeSliderEnabled;
		_chargeSliderLabel = limitLabel;
		host.Controls.Add(limitLabel, 0, row);
		host.Controls.Add(sliderBattery, 1, row);
		if (valueLabel != null)
		{
			valueLabel.Visible = _chargeSliderEnabled;
			host.Controls.Add(valueLabel, 2, row);
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主定 · 充电阈值产品接线）：⭐ 在电池页追加「起充/停充 + 启用开关」区域 ✅。
	/// <para>⭐ 能力判据：`UsesBaolongda` + 板号以 `MRID6` 开头 + EC 可读 ⇒ 才构建；
	/// ⛔ 非宝龙达 / 其它板号 / EC 读不到 ⇒ **不构建**（非宝龙达逐位不变）✅</para>
	/// </summary>
	private void BuildBaolongdaChargeLimitSection(TableLayoutPanel host)
	{
		try
		{
			MechrevoHw hardware = Program.hw;
			// 2026-10-18（机主：电池区域空白）：⛔ 不再以「EC 可读」作为构建前置 ——
			//   PawnIO 没装时 EC 读不到，但面板必须建出来并说明「状态读不到 / 需要 PawnIO」，否则卡片全空。
			if (!BatteryLimitPanel.IsSupportedBoard(hardware))
			{
				return;
			}
			BatteryLimitPanel panel = new BatteryLimitPanel();
			host.RowCount += 1;
			// ⭐⭐ 2026-10-18（机主：电池行内容为空 / 行索引撞车）：⭐ 宿主表是**固定高度**的 TableLayoutPanel，
			//   只加控件不会自动长高 ⇒ 新行会被裁掉看不见。这里按面板首选高度补一行 **Absolute** 行高，
			//   ⭐ 并**单独记住该行索引/行高**（所有重算/隐藏路径都跳过它）✅
			int panelWidth = Math.Max(320, host.Width);
			// 2026-10-18（机主：「底下的『未限制充电』被裁掉一点点」）：
			//   PreferredSize 是**内容高**，不含面板内边距/字体行高取整 ⇒ 顶部/底部各差几像素就会被裁。
			//   ⇒ 统一留 8 逻辑像素余量（足够覆盖 96/125/150/175% 缩放的取整误差）。
			int panelHeight = Math.Max(100, panel.GetPreferredSize(new Size(panelWidth, 0)).Height + ResponsiveLayout.LogicalToDevice(this, 8));
			// 2026-10-18（机主：电池区域仍然空白 · 诊断包实测 `rowIndex=7 · rows=5`）：
			//   ⚠️ 根因 = 增量行之前 `RowStyles.Count` 可能**小于** `RowCount`（有行只加了 RowCount 没加样式）
			//   ⇒ 只 `RowStyles.Add()` 会让"新行"落在 index=5，而控件加到 index=7 ⇒ **那一行没有样式 ⇒ 高度 0 ⇒ 面板看不见**。
			//   ⇒ ⭐ 先把 RowStyles 补齐到 RowCount，再**按最后一行显式赋值样式**，索引与控件位置逐一对齐 ✅
			while (host.RowStyles.Count < host.RowCount)
			{
				host.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
			}
			int panelRow = host.RowCount - 1;
			host.RowStyles[panelRow] = new RowStyle(SizeType.Absolute, panelHeight);
			_bdChargePanelRowIndex = panelRow;
			_bdChargePanelRowHeight = panelHeight;
			host.Controls.Add(panel, 0, _bdChargePanelRowIndex);
			host.SetColumnSpan(panel, 3);
			RecalculateBatteryTableHeight();
			host.PerformLayout();
			Logger.WriteLine("[battery] 充电阈值面板：rowIndex=" + _bdChargePanelRowIndex + " · rows=" + host.RowStyles.Count
				+ " · preferredHeight=" + panelHeight + " · host.Width=" + host.Width + " · host.Height=" + host.Height
				+ " · panel.Height=" + panel.Height + " · panel.Visible=" + panel.Visible);
			// ⭐⭐ 2026-10-18（机主：打开电池页也核对一次）：⭐ 后台跑、幂等；⛔ 不阻塞 UI ✅
			_ = Task.Run(delegate
			{
				try
				{
					BatteryLimitRestore.RunOnce("打开电池页");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("[battery] 打开电池页核对失败（已吞掉，不重试）：" + ex.Message);
				}
			});
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery] 构建充电阈值区域失败（已忽略，不影响电池页其它控件）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	private void BuildChargeLimitControls(TableLayoutPanel host)
	{
		// 2026-10-08（用户要求 · 充电上限 UI 返工；17:00 修正适用代际）：**所有开放限充的代际**（40/50/NoDgpu）
		// 走这一套布局（2026-10-10 起 50 系不再例外，见构造里 _gen50SliderRow = false）——
		// ① 滑块不上屏（见构造里 sliderBattery.Visible = _gen50SliderRow；50 系只走滑块行），
		//    上限由三档决定：长效 100 / 平衡 80 / 工作站 60；
		// ② 第 1 行＝开关 +「实验」角标（容器 chargeLimitHead，两个控件在行内垂直居中）；
		//    第 2 行＝副文案（单行，labelChargeLimitDesc）；第 3 行＝三档按钮，保持原来的顺序、
		//    外观与行高 30，开关与副文案都另占行，不会把三档挤走或裁字。
		// ③ 写入仍只有一条通道：EcChargeLimit 直写 EC（0x7B9 上限 / 0x7D0 复充下限），写后回读确认。
		// 2026-10-08 19:30 定稿：副文案「通过直写 EC 限电（不走 GCU）」是单行 —— TextRenderer 实测
		// 8.5pt 下中文(Microsoft YaHei UI) 181×17、英文(Segoe UI) 276×15 逻辑像素，卡片可用宽约 352，
		// 单行放得下；行高 26 = 单行 17 + 9 余量，不会裁字（**该行本轮未动，仍 26**）。
		// 行高：开关行 26 / 副文案行 26 / 档位行 **36**（总高 88，与上面 tableBatteryLimit 的占位高度一致）。
		// 2026-10-09（UI 反馈②，用户原话「三档限充按钮可以加高一点(注意不要裁切)」）：档位行 D(30) → **D(36)**。
		// 175% DPI 下的完整验算（四量一起算，确保 `MinimumSize ≤ 单元格可用高`）：
		//   档位行 = D(36) = 63 设备像素；modePanel 还带上下各 D(2) 边距 ⇒ 面板高 = 63 − 2×3.5 = 56；
		//   modePanel 内每颗按钮带上下各 D(3) 边距 ⇒ 单元格可用高 = 56 − 2×5.25 = 45.5 → 45 设备像素；
		//   MinimumSize 高取 **D(23) = 40.25 → 40** ⇒ 40 ≤ 45（余量 5 设备像素，≈2.5 逻辑像素）；
		//   实际渲染高 = 单元格可用高 = 45 设备像素（旧的 D(20) 档位行时只有 34）。
		//   ⇒ 加高约 +11 设备像素（+32%），且**不会**出现"最小高 > 可用高"那种把底部 1px 边框挤出父容器被裁的情况。
		//   100% DPI：行 36、面板 32、可用 26、最小 23、渲染 26（原 20）。
		// 2026-10-15（隐藏规则 A/B）：记下宿主表，供 ApplyChargeLimitRowVisibility 收放开关行/副标题行
		// （老机型上 _chargeSliderTable 为 null，但这两行仍要能收起来）。
		_chargeLimitHostTable = host;
		// 2026-10-16（空档删除 · 用户原话「空挡删除」）：删掉「EC 三档限充(实验性)」开关之后，
		// 第 0 行（原「EC 开关 +「实验」角标」行）与第 1 行（原副文案「通过直写 EC 限电（不走 GCU）」行）
		// **已经没有任何控件落在上面**（`host.Controls.Add(..., 0, 0/1)` 的那两处已随开关一起删除），
		// 但行高仍是 D(26) + D(26) ⇒ 卡片顶部留下一块 **52 逻辑像素的空白**（用户截图里的「空档」）。
		// ⇒ 两行行高归零：**行号保持不变**（⛔ 不动第 2 行三档、第 3 行无极限充开关、第 4 行警告、第 5 行滑块），
		//    因此 `_chargeSliderRowIndex`/`_chargeSliderWarningRowIndex`/`ApplyChargeLimitRowVisibility`
		//    里的既有下标**一个都不用改**；表高由下方 `host.Height = num + num2 + …` 与
		//    `ApplyChargeSliderVisibility` 末尾的逐行求和自动跟上。
		// ⚠️ 已核对：`_chargeLimitHeadRowHeight`/`_chargeLimitDescRowHeight` 两个字段**从未被赋值**
		//    （只有定义 + `ApplyChargeLimitRowVisibility` 里的 `> 0` 读取）⇒ 那两个 `if` 恒假、
		//    ⛔ 不会把这两行再恢复成 26。两行的控件字段（`_chargeLimitHeadRow`/`_chargeLimitDescLabel`/
		//    `_chargeLimitEffectLabel`）都仍存在判空保护，归零后它们只是不再占位，不影响任何逻辑。
		int num = 0;
		int num2 = 0;
		int num3 = ResponsiveLayout.LogicalToDevice(this, 36);
		// ⭐ 2026-10-17（宝龙达 · 修「电池限充还在」）：记下第 2 行（三档按钮行）的原始行高，
		// 供 ApplyChargeLimitTierVisibility() 在宝龙达机型上收 0（不留空白带）。
		_chargeLimitTierRowHeight = num3;
		// 2026-10-09（无极限充滑块）：新增第 3 行＝「启用无极限充功能(实验性)」两态开关（26 逻辑像素）、
		// 第 4 行＝「限充」滑块行（32，默认关 ⇒ 由 ApplyChargeSliderVisibility() 收到 0，不留占位）。
		// 2026-10-10（限充代系门控）：仅 30 系不建「无极限充」开关行与滑块行（其余世代都建）—— 30 系 RowCount=3、
		// 不建开关与滑块，num4/num5 记 0 不占行高。
		int num4 = _chargeSliderSupported ? ResponsiveLayout.LogicalToDevice(this, 26) : 0;
		// 2026-10-15（隐藏规则 A/B）：第 3 行（无极限充开关行）的行高也记下来，隐藏时一并收 0。
		_chargeSliderToggleRowHeight = num4;
		// 2026-10-10（限充安全警告）：开关正下方第 4 行＝红色警告行（34），滑块行顺延到第 5 行。
		int num6 = _chargeSliderSupported ? ResponsiveLayout.LogicalToDevice(this, 34) : 0;
		int num5 = _chargeSliderSupported ? ResponsiveLayout.LogicalToDevice(this, 32) : 0;
		// 2026-10-16（EC 直写逃生口）：新增第 6 行＝「关闭 EC 直写充电上限（写入异常时用）」。
		// ⚠️ 行数固定 7（0/1 空行 · 2 三档 · 3 无极限充开关 · 4 警告 · 5 滑块 · 6 逃生口）：
		//    代际门控的 30 系分支 num4/num6/num5 本来就是 0 ⇒ 高度与改前逐像素一致，
		//    但**行号不再随分支漂移** ⇒ 逃生口恒为第 6 行，不会被既有下标逻辑误伤。
		int num7 = ResponsiveLayout.LogicalToDevice(this, 26);
		_ecDirectWriteOffRowHeight = num7;
		_ecDirectWriteOffRowIndex = 6;
		host.RowCount = 7;
		host.RowStyles.Clear();
		host.RowStyles.Add(new RowStyle(SizeType.Absolute, num));
		host.RowStyles.Add(new RowStyle(SizeType.Absolute, num2));
		host.RowStyles.Add(new RowStyle(SizeType.Absolute, num3));
		// 合并（2026-10-18）：保留 main 的「滑块支持才建滑块行」闸门，同时保留 baolongda 的
		// 第 6 行（EC 直写逃生口）与统一高度重算。
		if (_chargeSliderSupported)
		{
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, num4));
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, num6));
			host.RowStyles.Add(new RowStyle(SizeType.Absolute, num5));
		}
		host.RowStyles.Add(new RowStyle(SizeType.Absolute, num7));
		// ⭐ 统一高度计算：所有 RowStyles 之和（含后续追加的宝龙达充电阈值行）✅
		RecalculateBatteryTableHeight();
		// 2026-10-15（限充真效果判据上屏 · 用户批准的修法④；同时按用户要求删掉原来那颗白色「实验」小胶囊）：
		// 把上游口径的 0x0742 bit2 判定（ChargeLimitEffectEvidence.Latest：生效 / 未生效 / 无法判定）从
		// 「只进日志」搬到限充区**用户看得到**的位置 —— 复用原角标所在的那一格（第 1 行容器的第 1 列）。
		// ⚠️ 纯只读展示：不写 EC、不改 config、不触发任何写入；由 RefreshChargeLimitEffectLabel 按 2s 快照刷新。
		// ⚠️ 外观刻意用**普通说明文字**（Muted 小字）而不是 mini-chip 胶囊 ⇒ 不会被误认成没删掉的旧角标。
		string[] modeLabels = new string[3] { Strings.ChargeModeLongevity, Strings.ChargeModeBalanced, Strings.ChargeModeWorkstation };
		int[] modeLimits = new int[3] { 100, 80, 60 };
		string modeTipFormat = Strings.ChargeModeTip;
		TableLayoutPanel modePanel = new TableLayoutPanel
		{
			ColumnCount = 3,
			RowCount = 1,
			Dock = DockStyle.Fill,
			AutoSize = false,
			Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 2), ResponsiveLayout.LogicalToDevice(this, 2)),
			Padding = Padding.Empty
		};
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
		_batteryModeButtons = new RButton[3];
		for (int mi = 0; mi < 3; mi++)
		{
			int mode = mi;
			RButton modeButton = new RButton
			{
				Text = modeLabels[mode],
				Tag = "theme_mode",
				FlatStyle = FlatStyle.Flat,
				ForeColor = UiVisualStyle.Text,
				// 2026-10-09（UI 打磨⑤，仅夜间）：电池档位行的按钮面 → ButtonFace（日间同值，像素不变）。
				BackColor = UiVisualStyle.ButtonFace,
				Dock = DockStyle.Fill,
				// 2026-10-09（UI 反馈①，裁切②）：最小尺寸由 D(80)×D(20) 收到 D(64)×D(16)。
				// 旧值在 175% DPI 下 D(20)=35 设备像素 ≥ 当时单元格可用的 34 设备像素 ⇒ Dock=Fill 也压不下去
				//（MinimumSize 优先），控件被强制比单元格高 ⇒ 底部那一行（正是 1px 边框所在行）被父容器裁掉。
				// 2026-10-09（UI 反馈②，用户要「三档限充按钮加高一点(注意不要裁切)」）：档位行由 D(30) 抬到 D(36)
				//（见 BuildChargeLimitControls 顶部的四量验算），最小高随之回到 **D(23)**：175% DPI 下
				// 40 设备像素 ≤ 单元格可用 45 设备像素（余量 5）⇒ 加高 +11 设备像素（34→45）且不可能裁切。
				MinimumSize = new Size(ResponsiveLayout.LogicalToDevice(this, 64), ResponsiveLayout.LogicalToDevice(this, 23)),
				Margin = new Padding(0, ResponsiveLayout.LogicalToDevice(this, 3), ResponsiveLayout.LogicalToDevice(this, 6), ResponsiveLayout.LogicalToDevice(this, 3)),
				BorderRadius = 10,
				Cursor = Cursors.Hand
			};
			modeButton.AccessibleName = modeLabels[mode] + " " + modeLimits[mode] + "%";
			// 档位只是选上限；文案统一不提系统电量显示（2026-10-08 19:30 定稿）。
			toolTip.SetToolTip(modeButton, string.Format(modeTipFormat, modeLabels[mode], modeLimits[mode]));
			// 2026-10-12（用户原话「可以补」）：⛔ 不再丢弃点击处的返回结果 —— 失败时按结果把三档高亮 /
			// EC 限充开关拉回**真实状态**（改前 `=> await …` 直接把 bool 丢掉，用户看不到任何反馈）。
			// 失败**文案**由 ApplyBatteryLimitModeAsync（通道不可用 / 未确认 / EC 侧各自）内部给出，
			// 这里⛔ 不重复弹，避免一次点击两条 Toast。
			modeButton.Click += async (object? _, EventArgs _) =>
			{
				if (!await ApplyBatteryLimitModeAsync(mode, modeLimits[mode]))
				{
					RefreshBatteryModeButtons();
				}
			};
			_batteryModeButtons[mode] = modeButton;
			modePanel.Controls.Add(modeButton, mi, 0);
		}
		host.Controls.Add(modePanel, 0, 2);
		host.SetColumnSpan(modePanel, 3);
		// 2026-10-09（无极限充滑块 · 实验性功能，用户原话「滑块限制充电改成默认隐藏 在设置里面增加一个开关
		// 『启用无极限充功能(实验性)』打开才显示 注意不要和其他一样是三个选项 开关两个按钮就行」）：
		// 第 3 行＝**两态开关**（RCheckBox 开关外观，⛔ 不是三选一/分段控件），第 4 行＝「限充」滑块行。
		// 滑块行套一层 16 %/70 %/14 % 的子面板 —— 外层三列是 33.33 % 均分，直接用会把滑块挤短；
		// 子面板的比例与 50 系那一支逐项一致，滑块观感不变。
		// 2026-10-10（限充代系门控）：仅 30 系不建「无极限充」开关（第 3 行）与滑块行（第 4 行）；
		// 其余世代（40 / 50 / NoDgpu 办公本）都建 —— 行样式与行高已在上面按 _chargeSliderSupported 收放。
		if (_chargeSliderSupported)
		{
			BuildChargeSliderToggleRow(host, 3);
			// 2026-10-10（限充安全警告 · 用户要求「警告加在开关下面，打开后显示，用红色字」）：
			// 开关正下方（第 4 行）红色警告行 —— 打开时显示、关闭时隐藏（行高 0），滑块行顺延到第 5 行。
			Label chargeSliderWarning = BuildChargeSliderWarningLabel();
			host.Controls.Add(chargeSliderWarning, 0, 4);
			host.SetColumnSpan(chargeSliderWarning, 3);
			_chargeSliderWarning = chargeSliderWarning;
			_chargeSliderWarningRowIndex = 4;
			_chargeSliderWarningRowHeight = num6;
			TableLayoutPanel chargeSliderRow = new TableLayoutPanel
			{
				Name = "tableChargeSliderRow",
				ColumnCount = 3,
				RowCount = 1,
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				BackColor = panelBattery.BackColor
			};
			// 2026-10-10（用户要求 · ⭐ 原话「这个是硬编码吗 怎么不是我们的字体」，同一张截图里这串文字还被
			// 折成「充电上 / 限已关」两行）：**只改非 50 系这条滑块行**的列宽 —— 数值列原先照搬 50 系的 14 %，
			// 但 50 系这一格只显示「100%」，非 50 系这一格在「无极限充」打开且上限=100 时显示
			// Strings.ChargeLimitOff（「充电上限已关闭」，7 个汉字）。按卡片可用宽 ≈352 逻辑像素算：
			// 14 % ≈ 49 px，7 个 8.5pt 汉字约需 80 px ⇒ 必然折行（截图里正是 4+3 字，与 49 px 吻合）。
			// 现改 16 %/56 %/28 %：数值列 ≈ 98 px（中英文均单行放得下），滑块列 70 %→56 %（≈ 197 px，仍够用）；
			// 第 0 列（「限充」/英文 "Charge limit"）不动。⛔ 只动列宽，不动行高、控件、滑块取值/上下限/事件。
			chargeSliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16f));
			chargeSliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
			chargeSliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
			chargeSliderRow.RowStyles.Add(new RowStyle(SizeType.Absolute, num5));
			_chargeSliderTable = host;
			_chargeSliderRowIndex = 5;
			_chargeSliderRowHeight = num5;
			BuildChargeLimitSliderRow(chargeSliderRow, _batteryLimitValue!, 0);
			host.Controls.Add(chargeSliderRow, 0, 5);
			host.SetColumnSpan(chargeSliderRow, 3);
		}
		// 2026-10-16（EC 直写逃生口）：在既有两条布局（含/不含无极限充滑块行）都建完之后挂第 6 行。
		BuildEcDirectWriteOffRow(host, _ecDirectWriteOffRowIndex);
		ApplyChargeSliderVisibility();
		RecalculateBatteryTableHeight();
		RefreshBatteryModeButtons();
		// 2026-10-15（限充真效果判据上屏 · 修法④）：构造时先按当前快照填一次（通常为空 ⇒ 隐藏，零回归）。
		RefreshChargeLimitEffectLabel();
	}

	/// <summary>
	/// 2026-10-09（无极限充滑块 · 实验性功能，用户原话「滑块限制充电改成默认隐藏 在设置里面增加一个开关
	/// 『启用无极限充功能(实验性)』打开才显示 注意不要和其他一样是三个选项 开关两个按钮就行」）：
	/// 电池卡片里的那颗**两态开关**（RCheckBox 的开关外观 = 开/关两个状态；⛔ 不是三选一/分段控件/下拉）。
	/// 打开＝显示「限充」滑块（无级上限 40–100%）；关闭（默认）＝滑块不上屏、并收掉它占的行高。
	/// 位置：50 系＝滑块行下面的第 1 行；非 50 系＝三档行下面的第 3 行（不挤占三档按钮）。
	/// </summary>
	/// <summary>
	/// 2026-10-16（EC 直写逃生口 · 补回被删掉的 UI 出口）：电池卡片最下面一行的**两态开关**——
	/// 勾上即写 <c>ec_charge_limit="0"</c>（显式关闭 EC 直写）、取消即**删键**（回到自动判定）。
	/// <para>⛔ 只写 config ＋ 触发一次既有能力刷新：不写 EC、不发 MQTT、不碰任何寄存器。</para>
	/// <para>⛔ 外观与 <see cref="BuildChargeSliderToggleRow"/> 同款（RCheckBox 开关外观、⛔ 非三选一）。</para>
	/// <para>⚠️ 本行**永不参与** <see cref="ApplyChargeLimitRowVisibility"/> 的收放：那个方法收的是
	/// 「EC 直写方式专属」的行，而本行正是**关闭 EC 直写**的入口 —— 一旦跟着被收掉，用户勾上就再也撤销不了。</para>
	/// </summary>
	private void BuildEcDirectWriteOffRow(TableLayoutPanel host, int row)
	{
		_checkEcDirectWriteOff = new RCheckBox
		{
			Name = "checkEcDirectWriteOff",
			Text = EcDirectWriteOffSwitchText,
			AutoSize = true,
			Appearance = Appearance.Normal,
			Anchor = AnchorStyles.Left,
			SwitchOnRight = false,
			TabStop = false,
			Cursor = Cursors.Hand,
			ForeColor = UiVisualStyle.Text,
			BackColor = panelBattery.BackColor,
			Font = UiStyleCaptionFont(),
			Margin = Padding.Empty
		};
		// 顺序要紧：先把 Checked 对齐存储值，再挂事件 —— 反了会把「启动时同步」当成一次用户点击去写配置。
		_checkEcDirectWriteOff.Checked = IsEcDirectWriteOffConfigured();
		_checkEcDirectWriteOff.AccessibleName = EcDirectWriteOffSwitchText;
		toolTip.SetToolTip(_checkEcDirectWriteOff, EcDirectWriteOffSwitchTip);
		_checkEcDirectWriteOff.CheckedChanged += (_, _) => OnEcDirectWriteOffToggled();
		host.Controls.Add(_checkEcDirectWriteOff, 0, row);
		host.SetColumnSpan(_checkEcDirectWriteOff, 3);
	}

	/// <summary>
	/// 勾上 ⇒ <c>ec_charge_limit="0"</c>（显式关闭 EC 直写）；取消 ⇒ **删键**（回自动判定）。
	/// <para>⚠️ 刻意<b>不写 "1"</b>：那是「强制放行」覆盖，会绕过通道探测与固件判据（见
	/// <c>EcChargeLimit.IsAvailableOnThisMachine</c> / <c>IsFirmwareLimitSupported</c>）；
	/// 用户只是想恢复默认时写 "1" 等于偷偷开后门。</para>
	/// <para>改完立刻 <see cref="RefreshDeviceCapabilities"/>：面板可见性、行收放、三档启用态全都挂在
	/// <see cref="ApplyChargeLimitSliderGating"/> 这一条既有汇聚点上（构造后它还会再跑一次）⇒ 不另造刷新路径。</para>
	/// </summary>
	private void OnEcDirectWriteOffToggled()
	{
		if (IsDisposed || _checkEcDirectWriteOff == null)
		{
			return;
		}
		bool off = _checkEcDirectWriteOff.Checked;
		if (off)
		{
			AppConfig.Set(EcDirectWriteOffConfigKey, "0");
		}
		else
		{
			AppConfig.Remove(EcDirectWriteOffConfigKey);
		}
		Logger.WriteError($"[charge-limit] EC 直写逃生口：用户{(off ? "关闭" : "恢复")} EC 直写（{EcDirectWriteOffConfigKey}={(off ? "写 0" : "已删键，回自动判定")}）⇒ 立刻重刷能力位（⛔ 未写 EC、⛔ 未发 MQTT）");
		RefreshDeviceCapabilities();
	}

	/// <summary>是否已由用户显式关闭 EC 直写（<c>ec_charge_limit="0"</c>）。只读配置，⛔ 不碰硬件。</summary>
	private static bool IsEcDirectWriteOffConfigured()
	{
		return AppConfig.GetString(EcDirectWriteOffConfigKey) == "0";
	}

	private void BuildChargeSliderToggleRow(TableLayoutPanel host, int row)
	{
		checkChargeSlider = new RCheckBox
		{
			Name = "checkChargeSlider",
			Text = Strings.ChargeLimitSliderSwitch,
			AutoSize = true,
			Appearance = Appearance.Normal,
			Anchor = AnchorStyles.Left,
			SwitchOnRight = false,
			TabStop = false,
			Cursor = Cursors.Hand,
			ForeColor = UiVisualStyle.Text,
			BackColor = panelBattery.BackColor,
			Font = UiStyleCaptionFont(),
			Margin = Padding.Empty
		};
		// 顺序要紧：先把 Checked 对齐存储值，再挂事件 —— 反了会把"启动时同步"当成一次用户点击去写配置。
		checkChargeSlider.Checked = _chargeSliderEnabled;
		checkChargeSlider.AccessibleName = Strings.ChargeLimitSliderSwitch;
		toolTip.SetToolTip(checkChargeSlider, Strings.ChargeLimitSliderSwitchTip);
		// 用隐式类型的 lambda（⛔ 不写 `object? sender`）：本文件没有 #nullable 上下文，显式可空注解会多一条
		// CS8632 警告，而构建基线（0 错误 / 1435 警告）是硬上限。
		checkChargeSlider.CheckedChanged += (_, _) => OnChargeSliderToggleChanged();
		host.Controls.Add(checkChargeSlider, 0, row);
		host.SetColumnSpan(checkChargeSlider, 3);
	}

	/// <summary>
	/// 2026-10-10（限充安全警告 · 用户要求「警告加在开关下面，打开后显示，用红色字」）：
	/// 开关正下方的红色警告 Label（UiVisualStyle.Danger，仓库既有语义色，不自造颜色）。
	/// 50 系与非 50 系两条布局共用同一工厂方法，保证外观/字体/颜色完全一致。
	/// </summary>
	private Label BuildChargeSliderWarningLabel()
	{
		return new Label
		{
			Name = "chargeSliderWarning",
			Text = Strings.ChargeLimitSliderWarning,
			AutoSize = false,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiVisualStyle.Danger,
			BackColor = panelBattery.BackColor,
			Font = UiStyleCaptionFont(),
			Margin = Padding.Empty,
			// 初始显隐由 ApplyChargeSliderVisibility() 统一收放（启动时按存储值、切换时按新状态）。
			Visible = _chargeSliderEnabled
		};
	}

	/// <summary>
	/// 2026-10-09（无极限充滑块 · 实验性功能）：开关**只控制显示与否** —— 落盘 <c>charge_limit_slider</c>
	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：关闭无极限充时要恢复满充）：⭐ 写回 100% —— 先写、后收行，避免“行没了但限值还留着” ✅。
	/// <para>通道判据：EC 直写可用（`IsEcDirectWriteAllowed()` 且未 `ec_charge_limit=0`）⇒ 走 EC 直写；
	/// 否则走 GCU 官方路径（BatteryProtection=0 = 长效 100）✅</para>
	/// <para>写后回读确认；失败**不重试**、诚实记日志并提示用户手动点「长效」档 ✅</para>
	/// </summary>
	private async Task WriteBackFullChargeAfterSliderOffAsync()
	{
		const string logPrefix = "[charge-limit] 关闭无极限充 ⇒";
		try
		{
			bool ecAllowed = !IsEcDirectWriteOffConfigured() && EcChargeLimit.IsEcDirectWriteAllowed();
			if (ecAllowed)
			{
				int current = EcChargeLimit.ReadPercent();
				if (current == 100)
				{
					Logger.WriteLine(logPrefix + " 已是 100%，无需写回");
					return;
				}
				(bool ok, int applied, int readback) = await Task.Run(delegate
				{
					bool written = EcChargeLimit.TrySet(100, out int appliedPercent, out _);
					int rb = EcChargeLimit.ReadPercent();
					return (written, appliedPercent, rb);
				});
				string rbText = readback < 0 ? "读不到" : readback + "%";
				Logger.WriteLine(logPrefix + " 自动写回 100%（通道=EC直写 · 写入=" + ok + " · 回读=" + rbText + "）");
				if (readback == 100)
				{
					AppConfig.Set("charge_limit", 100);
					AppConfig.Flush();
					BatteryControl.chargeFull = true;
					VisualiseBattery(100);
					return;
				}
				Logger.WriteError(logPrefix + " ⛔ 写回 100% 失败（通道=EC直写 · 原因=写入=" + ok
					+ " 回读=" + rbText + "）⇒ 不重试，请手动点「长效」档");
				ToastForm.ShowFailure(Strings.ChargeSliderOffResetFailed);
				return;
			}

			MechrevoHw hw = Program.hw;
			if (hw == null || !hw.IsConnected)
			{
				Logger.WriteError(logPrefix + " ⛔ 写回 100% 失败（通道=GCU · 原因=GCU 未连接）⇒ 不重试，请手动点「长效」档");
				ToastForm.ShowFailure(Strings.ChargeSliderOffResetFailed);
				return;
			}
			if (hw.BatteryProtection == 0)
			{
				Logger.WriteLine(logPrefix + " 已是 100%，无需写回（GCU BatteryProtection=0）");
				return;
			}
			bool confirmed = await hw.SetBatteryProtection(0);
			int gcuReadback = hw.BatteryProtection;
			Logger.WriteLine(logPrefix + " 自动写回 100%（通道=GCU · 写入=" + confirmed + " · 回读=BatteryProtection=" + gcuReadback + "）");
			if (!confirmed || gcuReadback != 0)
			{
				Logger.WriteError(logPrefix + " ⛔ 写回 100% 失败（通道=GCU · 原因=写入=" + confirmed
					+ " 回读=" + gcuReadback + "）⇒ 不重试，请手动点「长效」档");
				ToastForm.ShowFailure(Strings.ChargeSliderOffResetFailed);
				return;
			}
			AppConfig.Set("charge_limit", 100);
			AppConfig.Flush();
			BatteryControl.chargeFull = true;
			VisualiseBattery(100);
		}
		catch (Exception ex)
		{
			Logger.WriteError(logPrefix + " ⛔ 写回 100% 失败（通道判定/写入异常 · 原因="
				+ ex.GetType().Name + " " + ex.Message + "）⇒ 不重试，请手动点「长效」档");
			ToastForm.ShowFailure(Strings.ChargeSliderOffResetFailed);
		}
	}

	/// <summary>
	/// （缺省 0＝关），再按新状态收放滑块行。⛔ 不读写滑块取值、不发 EC/GCU 指令、不动滑块的
	/// Value / Minimum / Maximum / ValueChanged / MouseUp / KeyUp 与写入链路：
	/// 用户设的上限一直在 config 的 <c>charge_limit</c> 里，隐藏↔显示来回切不会丢。
	/// </summary>
	private async void OnChargeSliderToggleChanged()
	{
		if (checkChargeSlider == null)
		{
			return;
		}
		bool flag = checkChargeSlider.Checked;
		if (flag == _chargeSliderEnabled)
		{
			return;
		}
		_chargeSliderEnabled = flag;
		AppConfig.Set(ChargeSliderConfigKey, flag ? 1 : 0);
		// 2026-10-10（三方式互斥 · 用户原话「EC限充和无极限制应该实现方式差不多,这俩也不要冲突了,
		// 然后使用EC方式的时候GCU的限电方式应该不能同时生效,防止覆盖」）：两个实验开关**互斥且规则对称** ——
		// 「无极限充」（本开关 = B）打开 ⇒ 「EC 限充」（checkChargeLimit = A）**自动关闭**：
		//   · SetChargeLimitSwitchQuiet(false)：只改 UI 勾选（抑制重入）⇒ ⛔ 不触发 CheckChargeLimit_CheckedChanged，
		//     因此**不写 EC**（EC 上我们刚写/即将写的值不会被这次切换动到）；
		//   · SetLockEnabled(false)：清掉 charge_limit_lock 记忆 ⇒ ⛔ 不留"被置灰但仍然锁定"的状态
		//     （那种状态下 A 的档位记忆会在 GCU 上报后经重放覆盖 B 的滑块值）。
		// 反向（B 生效时点 A）由 CheckChargeLimit_CheckedChanged 的写入硬门拒绝并把勾选还原 ⇒
		// 同一时刻最多一种 EC 方式生效，双向对称、不存在来回互写。
		if (flag && BatteryControl.LockEnabled())
		{
			BatteryControl.SetLockEnabled(false);
			Logger.WriteLine("限充方式互斥：打开无极限充（实验性）⇒ EC 限充开关已自动关闭（charge_limit_lock=0；⛔ 未写 EC）");
		}
		if (flag)
		{
			// 滑块 ＝ EC 直写传输 ⇒ 同样要请 GCU 侧让位（长效＝不限制），否则"两种限电方式同时生效"。
			_ = EnsureGcuStandsDownAsync("打开无极限充（实验性）⇒ 切到 EC 直写传输");
		}
		// 与三档的关系（本代理定稿）：**互斥** —— 本开关（滑块）生效时三档与 EC 限充开关置灰（
		// ApplyChargeLimitStageEnablement）+ 三档点击被 ApplyBatteryLimitModeAsync 的硬门拒绝；
		// 关掉本开关即回到"三档×传输方式"那套（EC 限充 ON ⇒ EC 直写档位；OFF ⇒ GCU 官方档位）。
		// 理由：滑块与档位写的是**同一个** EC 寄存器 0x7B9，同时可用必然互相覆盖；用户既有偏好也是
		// 「无极限制充电开的时候上面三档位应该置灰(包括EC现充开关)」，故取互斥而非"最后操作者优先"。
		// 2026-10-10（限充安全警告 · 用户要求「警告加在开关下面，打开后显示，用红色字」）：
		// 从关到开不再弹窗 —— 警告改为开关正下方的红色常驻行（ApplyChargeSliderVisibility 控制显隐），
		// 打开时一直可见，比一闪而过的弹窗更符合「警告」语义。
		// ⭐⭐ 2026-10-18（机主：关闭无极限充 ⇒ 自动写回 100%）：⭐ **先写回，再收行/更新三档高亮** ✅
		if (!flag)
		{
			await WriteBackFullChargeAfterSliderOffAsync();
		}
		AppConfig.Flush();
		ApplyChargeSliderVisibility();
		panelBattery.PerformLayout();
		Logger.WriteLine($"无极限充（实验性）滑块显隐：{(flag ? "显示" : "隐藏")}（{ChargeSliderConfigKey}={(flag ? 1 : 0)}）");
		if (_dashboardScroll != null)
		{
			UpdateDashboardScroll();
		}
	}

	/// <summary>
	/// 2026-10-09（无极限充滑块 · 实验性功能）：滑块行（「限充」标签 / 滑块 / 数值标签）显隐的唯一入口。
	/// <para>关（默认）：三件套 Visible=false + 该行 RowStyle 高度收到 0 ⇒ **不留占位空行**
	/// （表格自身高度与 panelBattery 的 AutoSize 一起收回去）；开：恢复 Visible + 行高回到 32 逻辑像素。</para>
	/// <para>⛔ 只碰可见性与行高，滑块自身的取值/上下限/事件/保存一字未动。</para>
	/// </summary>
	private void ApplyChargeSliderVisibility()
	{
		// 2026-10-15（隐藏规则 A/B）：本方法在切换无极限充开关时也会被调用，若不考虑 _chargeLimitRowsHidden，
		// 就会把刚被规则收起的那几行重新点亮 ⇒ 在这里把两条规则**合并**进同一个判据（显隐仍只有这一个出口）。
		bool flag = _chargeSliderEnabled && !_chargeLimitRowsHidden;
		// 2026-10-10（用户要求 · ⭐ 原话「无极限制充电开的时候上面三档位应该置灰(包括EC现充开关)」）：
		// 本方法是这份状态的**唯一入口**（启动时构造里按 charge_limit_slider 调一次、切换时
		// OnChargeSliderToggleChanged 再调一次）⇒ 启用态也挂在这里，重启后按存储值同样生效。
		ApplyChargeLimitStageEnablement();
		if (_chargeSliderLabel != null)
		{
			_chargeSliderLabel.Visible = flag;
		}
		// 2026-10-10（限充安全警告 · 用户要求「警告加在开关下面，打开后显示，用红色字」）：
		// 红色警告行跟随开关显隐；行高收放与滑块行一致（关时 0），避免留空占位。
		if (_chargeSliderWarning != null)
		{
			_chargeSliderWarning.Visible = flag;
		}
		if (_batteryLimitValue != null)
		{
			_batteryLimitValue.Visible = flag;
		}
		sliderBattery.Visible = flag;
		if (_chargeSliderTable == null || _chargeSliderRowIndex < 0 || _chargeSliderRowIndex >= _chargeSliderTable.RowStyles.Count)
		{
			return;
		}
		_chargeSliderTable.RowStyles[_chargeSliderRowIndex] = new RowStyle(SizeType.Absolute, flag ? _chargeSliderRowHeight : 0);
		if (_chargeSliderWarningRowIndex >= 0 && _chargeSliderWarningRowIndex < _chargeSliderTable.RowStyles.Count)
		{
			_chargeSliderTable.RowStyles[_chargeSliderWarningRowIndex] = new RowStyle(SizeType.Absolute, flag ? _chargeSliderWarningRowHeight : 0);
		}
		RecalculateBatteryTableHeight();
	}

	/// <summary>
	/// 2026-10-10（三方式互斥 · 用户原话「EC限充和无极限制应该实现方式差不多,这俩也不要冲突了,然后使用EC方式
	/// 的时候GCU的限电方式应该不能同时生效,防止覆盖」）：本类**唯一**的「EC 直写方式是否生效」判据 ——
	/// 两个实验开关任一为开（<c>无极限充</c>滑块 = charge_limit_slider / <c>EC 限充</c> = charge_limit_lock）
	/// ⇒ EC 直写方式生效（EC 0x7B9 的值由我们负责；GCU 官方限电方式此时**不得同时生效**，见协调件）。
	/// 两者都关 ⇒ 我们不主动写 EC（挂起恢复/合盖等自动路径一律让位，避免覆写 GCU 侧的值）。
	/// <para>⛔ 只读开关状态/记忆位：不写 config、不写 EC、不发 MQTT、不碰任何控件。</para>
	/// </summary>
	private bool ChargeLimitEcPathActive()
	{
		return _chargeSliderEnabled || BatteryControl.LockEnabled();
	}

	/// <summary>
	/// 2026-10-15（隐藏规则 A/B · 用户原话「对不支持的机型直接隐藏掉按钮就行 滑块和EC三档的开关都隐藏
	/// 10系-30系用这个可能损坏硬件必须强制隐藏EC三档和滑块 只保留GCU路(注意不删三个按钮)」）：
	/// 「EC 直写方式专属」那几行的**显隐唯一入口** —— 规则 A（机型不支持 / EC 通道不可用）或规则 B（10/16/20/30 系）
	/// 任一成立 ⇒ 把「EC 三档限充」开关行（含副标题）+ 无极限充开关行 + 警告行 + 滑块行**全部收起**（行高 0、
	/// 不留占位），只保留 GCU 路。
	/// <para>⛔ **不删任何控件、不删任何代码路径**（用户特意强调「注意不删三个按钮」）：三档按钮行**永不**参与
	/// 本方法 —— 它们是 EC/GCU **共用**的档位值来源（见 ApplyBatteryLimitModeAsync：开关关时走 GCU 官方
	/// BatteryProtection/Control，一条 EC 都不写）⇒ 规则 A/B 下必须继续可用，只是改走 GCU 路。</para>
	/// <para>⚠️ 天然关闭 / 零回归：两规则都不成立（＝当前所有正常机型）时 <paramref name="hidden"/> 为 false，
	/// 本方法把每一行都恢复成**构造时的原始行高**、所有 Visible 恢复为「由既有开关逻辑决定」的那个值 ——
	/// 即与改动前逐像素一致。</para>
	/// <para>⛔ 只改 Visible 与 RowStyle 高度：不碰任何控件的 Checked/Enabled/Click/事件，不写 EC/config/注册表。</para>
	/// </summary>
	private void ApplyChargeLimitRowVisibility(bool hidden)
	{
		// ⚠️ 幂等短路只在「布局真的建好了」时才成立（用 _chargeLimitHostTable 而不是 _chargeLimitHeadRow 判）：
		// 若本方法在 BuildChargeLimitControls 之前被调用过一次，用行控件判会把那次当成已生效 ⇒ 布局建好后
		// 的第二次调用被短路、隐藏状态落不下去。
		if (_chargeLimitRowsHidden == hidden && _chargeLimitHostTable != null)
		{
			return;
		}
		_chargeLimitRowsHidden = hidden;
		if (_chargeLimitHeadRow != null)
		{
			_chargeLimitHeadRow.Visible = !hidden;
		}
		if (_chargeLimitDescLabel != null)
		{
			_chargeLimitDescLabel.Visible = !hidden;
		}
		if (_chargeLimitEffectLabel != null)
		{
			_chargeLimitEffectLabel.Visible = !hidden && _chargeLimitEffectLabel.Text.Length > 0;
		}
		// 无极限充开关行 + 警告行：隐藏规则生效时一并收起（它们同样只服务 EC 直写方式）。
		if (checkChargeSlider != null)
		{
			checkChargeSlider.Visible = !hidden;
		}
		// 有滑块行时用 _chargeSliderTable（= host），老机型上回落到宿主表 —— 两者都指向同一个 TableLayoutPanel。
		TableLayoutPanel table = _chargeSliderTable ?? _chargeLimitHostTable;
		if (table == null || table.RowStyles.Count == 0)
		{
			return;
		}
		if (_chargeLimitHeadRowHeight > 0)
		{
			table.RowStyles[0] = new RowStyle(SizeType.Absolute, hidden ? 0 : _chargeLimitHeadRowHeight);
		}
		if (_chargeLimitDescRowHeight > 0 && table.RowStyles.Count > 1)
		{
			table.RowStyles[1] = new RowStyle(SizeType.Absolute, hidden ? 0 : _chargeLimitDescRowHeight);
		}
		if (_chargeSliderToggleRowHeight > 0 && table.RowStyles.Count > 3)
		{
			table.RowStyles[3] = new RowStyle(SizeType.Absolute, hidden ? 0 : _chargeSliderToggleRowHeight);
		}
		// ⚠️ 警告行与滑块行的显隐**还受「无极限充」开关约束**（它们只在开关打开时上屏）：恢复时不能无脑还原成
		// 原始行高，否则开关处于关闭态时会被这里点亮（那是回归）。⇒ 恢复高度 = 开关态 ? 原始行高 : 0。
		int sliderRowHeight = (_chargeSliderEnabled ? _chargeSliderRowHeight : 0);
		int warningRowHeight = (_chargeSliderEnabled ? _chargeSliderWarningRowHeight : 0);
		if (_chargeSliderWarningRowIndex >= 0 && _chargeSliderWarningRowIndex < table.RowStyles.Count)
		{
			table.RowStyles[_chargeSliderWarningRowIndex] = new RowStyle(SizeType.Absolute, hidden ? 0 : warningRowHeight);
		}
		if (_chargeSliderRowIndex >= 0 && _chargeSliderRowIndex < table.RowStyles.Count)
		{
			table.RowStyles[_chargeSliderRowIndex] = new RowStyle(SizeType.Absolute, hidden ? 0 : sliderRowHeight);
		}
		// ⭐ 统一高度计算（含所有 RowStyles，含宝龙达追加班龙达行）✅
		RecalculateBatteryTableHeight();
		Logger.WriteLine($"限充行显隐：{(hidden ? "隐藏" : "显示")} EC 直写方式专属各行（开关行/副标题/无极限充开关/警告/滑块）—— 规则 A（机型不支持/EC 通道不可用）或规则 B（10/16/20/30 系，可能损坏硬件）⇒ 只保留 GCU 路；⛔ 三档按钮行不受影响、未删任何控件");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：宝龙达电池卡片内容仍为空）：⭐ **电池卡片高度的单一计算来源** ✅。
	/// <para>高度 = **所有 RowStyles 高度之和**（含宝龙达追加的充电阈值行），据此设置表高与
	/// <c>panelBattery.MinimumSize</c>；⛔ 各处不再各自硬编码 6 行 ✅</para>
	/// <para>⚠️ 所有可见性/门控路径（gating / tier / ecDirectWriteOff / rowVisibility / 追加班龙达行）
	/// 改完行高后都必须调用本方法 ✅</para>
	/// </summary>
	private void RecalculateBatteryTableHeight()
	{
		RecalculateTableHeight(_chargeLimitHostTable);
		if (_chargeSliderTable != null && !ReferenceEquals(_chargeSliderTable, _chargeLimitHostTable))
		{
			RecalculateTableHeight(_chargeSliderTable);
		}
	}

	private void RecalculateTableHeight(TableLayoutPanel table)
	{
		if (table == null || table.RowStyles.Count == 0)
		{
			return;
		}
		// ⭐⭐ 2026-10-18（机主：行索引撞车）：⭐ 宝龙达面板行**单独标记、单独保护** ——
		//   其它行（旧三档/EC 直写/滑块）收 0 时⛔ 不得连带把这一行收掉；若被误清零则按记录还原 ✅
		int panelRow = ((object)table == (object)_chargeLimitHostTable) ? _bdChargePanelRowIndex : -1;
		if (panelRow >= 0 && panelRow < table.RowStyles.Count)
		{
			if (table.RowStyles[panelRow].Height <= 0f && _bdChargePanelRowHeight > 0)
			{
				table.RowStyles[panelRow] = new RowStyle(SizeType.Absolute, _bdChargePanelRowHeight);
			}
			Logger.WriteLineIfChanged("battery-panel-row",
				"[battery] 面板行：index=" + panelRow + " · 行高=" + table.RowStyles[panelRow].Height
				+ " · 期望>0 ⇒ ok=" + (table.RowStyles[panelRow].Height > 0f));
		}
		float total = 0f;
		foreach (RowStyle rowStyle in table.RowStyles)
		{
			total += Math.Max(0f, rowStyle.Height);
		}
		int height = (int)Math.Round(total);
		table.Height = height;
		if (panelBattery == null)
		{
			return;
		}
		int minimum = table.Bottom + panelBattery.Padding.Bottom;
		panelBattery.MinimumSize = new Size(0, minimum);
		if (panelBattery.Height < minimum)
		{
			panelBattery.Height = minimum;
		}
		Logger.WriteLineIfChanged("battery-card-height",
			"[battery] 卡片高度：rows=" + table.RowStyles.Count + " · 合计=" + height
			+ "px · panel.MinimumSize=" + panelBattery.MinimumSize
			+ " · panel.Height=" + panelBattery.Height
			+ " · table.Bottom=" + table.Bottom);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17（宝龙达机型 · 真机反馈「一电池限充还在」）：三档按钮行（长效/平衡/工作站）的
	/// **可见性唯一出口**。宝龙达模具**没有任何限充通路**（EC 直写无 UWACPIDriver；GCU 三档无 GCU）
	/// ⇒ <paramref name="hidden" /> 为 true 时把三颗按钮与它们所在的 modePanel 一起收起，第 2 行行高收 0。
	/// <para>⛔ <b>不删控件、不删代码路径</b>（用户明令「注意不删三个按钮」）—— 只是 Visible/行高，
	/// 与 <see cref="ApplyChargeLimitRowVisibility" /> 的做法同源。</para>
	/// <para>⚠️ 与 <see cref="ApplyChargeLimitStageEnablement" /> 的分工：那个只管 <c>Enabled</c>，
	/// 本方法只管 <c>Visible</c> + 行高；两者互不覆盖（隐藏时 Enabled 是什么都无所谓）。</para>
	/// <para>⚠️ 幂等短路：同一状态重复调用直接返回（本方法由 ApplyChargeLimitSliderGating 调用，
	/// 而后者在构造后还会被 RefreshDeviceCapabilities 多次调用）——避免反复重算表高。</para>
	/// <para>⚠️ 非宝龙达机型 <paramref name="hidden" /> 恒 false ⇒ 每颗按钮 Visible=true、
	/// 行高恢复成 <see cref="_chargeLimitTierRowHeight" /> ⇒ 与改动前逐像素一致。</para>
	/// </summary>
	private void ApplyChargeLimitTierVisibility(bool hidden)
	{
		if (_chargeLimitTierRowHeight <= 0)
		{
			// 构造期还没跑过 BuildChargeLimitControls（行高未登记）⇒ 不碰，等它建好后再由下一次刷新落地。
			return;
		}
		if (_chargeLimitTierHidden == hidden)
		{
			return;
		}
		_chargeLimitTierHidden = hidden;
		if (_batteryModeButtons != null)
		{
			RButton[] batteryModeButtons = _batteryModeButtons;
			for (int i = 0; i < batteryModeButtons.Length; i++)
			{
				batteryModeButtons[i].Visible = !hidden;
			}
			// 三档按钮的宿主 modePanel 一并收起：只藏按钮而留着面板，行高 36 仍会占位（空白带）。
			Control modePanel = batteryModeButtons[0].Parent;
			if (modePanel != null)
			{
				modePanel.Visible = !hidden;
			}
		}
		TableLayoutPanel table = _chargeLimitHostTable;
		if (table != null && table.RowStyles.Count > 2)
		{
			table.RowStyles[2] = new RowStyle(SizeType.Absolute, hidden ? 0 : _chargeLimitTierRowHeight);
			float num = 0f;
			foreach (RowStyle rowStyle in table.RowStyles)
			{
				num += rowStyle.Height;
			}
			RecalculateBatteryTableHeight();
		}
		Logger.WriteLine($"限充三档显隐：{(hidden ? "隐藏" : "显示")} 三档按钮行（长效/平衡/工作站）—— "
			+ (hidden ? "本机是宝龙达模具，没有任何限充通路（EC 直写无 UWACPIDriver、GCU 三档无 GCU）" : "非宝龙达机型，恢复原行高")
			+ "；⛔ 未删任何控件、未删任何代码路径（只改 Visible 与行高）");
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主截图）：⭐ 宝龙达下隐藏旧的「关闭 EC 直写充电上限（写入异常时用）」行
	/// （判据基于旧的 `\\.\ACPIDriver` 通路，宝龙达不适用）✅。
	/// <para>⛔ 只改 Visible 与行高，**不删控件**；非宝龙达 `hidden=false` ⇒ 恢复原行高，逐位不变 ✅</para>
	/// </summary>
	private void ApplyEcDirectWriteOffVisibility(bool hidden)
	{
		if (_ecDirectWriteOffRowHeight <= 0)
		{
			// 构造期还没跑过 BuildChargeLimitControls（行高未登记）⇒ 不碰，等它建好后再由下一次刷新落地。
			return;
		}
		if (_ecDirectWriteOffHidden == hidden)
		{
			return;
		}
		_ecDirectWriteOffHidden = hidden;
		if (_checkEcDirectWriteOff != null)
		{
			_checkEcDirectWriteOff.Visible = !hidden;
		}
		TableLayoutPanel table = _chargeLimitHostTable;
		if (table != null && _ecDirectWriteOffRowIndex >= 0 && table.RowStyles.Count > _ecDirectWriteOffRowIndex)
		{
			table.RowStyles[_ecDirectWriteOffRowIndex] = new RowStyle(SizeType.Absolute,
				hidden ? 0 : _ecDirectWriteOffRowHeight);
			RecalculateBatteryTableHeight();
		}
		if (hidden)
		{
			Logger.WriteLineIfChanged("battery-baolongda-legacy-rows-hidden",
				"[battery] 宝龙达 ⇒ 隐藏旧 EC 直写/三档行（仅保留新充电阈值面板）");
		}
	}

	/// <summary>
	/// 2026-10-15（限充真效果判据上屏 · 用户批准的修法④）：把 <see cref="ChargeLimitEffectEvidence.Latest"/> 的
	/// 上游口径判定（0x0742 bit2：生效 / 未生效 / 无法判定）显示在限充区。
	/// <para>⛔ 纯只读：只读静态证据对象 + 改一个 Label 的 Text/Visible，不写 EC、不改 config、不发起任何探测。</para>
	/// <para>本次会话还没判定过（CheckedAt == MinValue）⇒ 清空文本并隐藏，界面与改动前一致（零回归）。</para>
	/// </summary>
	private void RefreshChargeLimitEffectLabel()
	{
		if (_chargeLimitEffectLabel == null || IsDisposed)
		{
			return;
		}
		ChargeLimitEffectRecord latest = ChargeLimitEffectEvidence.Latest;
		string text = "";
		if (latest.CheckedAt != DateTime.MinValue)
		{
			text = latest.Effect switch
			{
				ChargeLimitEffect.Verified => Strings.ChargeLimitEffectOk,
				ChargeLimitEffect.Ineffective => Strings.ChargeLimitEffectBad,
				_ => Strings.ChargeLimitEffectUnknown,
			};
		}
		if (_chargeLimitEffectLabel.Text != text)
		{
			_chargeLimitEffectLabel.Text = text;
		}
		bool visible = !_chargeLimitRowsHidden && text.Length > 0;
		if (_chargeLimitEffectLabel.Visible != visible)
		{
			_chargeLimitEffectLabel.Visible = visible;
		}
	}

	/// <summary>
	/// 2026-10-10（三方式互斥 · 防覆写）：挂起恢复/合盖这类**自动**路径原先无条件调用
	/// <see cref="MechrevoLite.Battery.BatteryControl.AutoBattery" />，而它会按 config 的 <c>charge_limit</c>
	/// 直写 EC 0x7B9（甚至在"不限制"时写 0）—— 在用户并没有选择 EC 直写方式（两个实验开关都关、由 GCU 官方
	/// 限电方式生效）时，这会把 GCU 侧刚确立的值覆写掉。⇒ 只有 <see cref="ChargeLimitEcPathActive" /> 为真
	/// 才走原路径；否则**完全不写 EC**（只记一条日志；UI 由 10s 的状态定时器与窗口 Activated 刷新兜底）。
	/// ⛔ 不改 AutoBattery 自身（属 limit 侧文件）、不碰 GCU/MQTT、不新增定时器。
	/// </summary>
	private void AutoBatteryIfEcPathActive(string reason)
	{
		if (ChargeLimitEcPathActive())
		{
			BatteryControl.AutoBattery();
			return;
		}
		Logger.WriteLine($"限充方式互斥：{reason} —— 当前不是 EC 直写方式（EC 限充与无极限充都关）⇒ 跳过自动写 EC（不覆写 GCU 侧的值）");
	}

	/// <summary>
	/// 2026-10-10（用户要求 · ⭐ 原话「无极限制充电开的时候上面三档位应该置灰(包括EC现充开关)」）：
	/// 「无极限充」开关**上面**那两组控件 —— 三档按钮（_batteryModeButtons[3]：长效/平衡/工作站）与
	/// EC 限充开关（checkChargeLimit，含其「实验」角标所属行）—— 的**启用态唯一出口**。
	/// <para>开＝无极限充打开 ⇒ 两组 Enabled=false（置灰）；关 ⇒ 各自按下述判据恢复可用：
	/// EC 限充开关回到 _chargeLimitStageBaseEnabled（＝ EC直写通道是否可用，由 ApplyChargeLimitSliderGating 记录）；
	/// **三档**（2026-10-12 用户报 bug ② 后拆分）= EC 通道可用 ‖ EC 限充开关是开的 ‖ GCU 官方通路可用，
	/// 只有三条都不通才置灰（详见方法体内 2026-10-12 注释）。</para>
	/// <para>⛔ 只改 Enabled：不碰这些控件的 Checked / Click / CheckedChanged / MouseDown，不碰 EC 写入链路，
	/// 也⛔不动开关**下面**的「限充」滑块（sliderBattery 的启用态仍只由 gating 管）。</para>
	/// <para>置灰是真灰、且用的是仓库既有灰：RButton.OnPaint 与 RCheckBox.OnPaint 在 !Enabled 时
	/// 一律用 UiVisualStyle.Muted 画文字（RButton.cs:687 / RCheckBox.cs:205,208）—— 与
	/// ApplyChargeLimitSliderGating 既有做法同源，⛔ 未新造任何颜色、未新增样式。</para>
	/// <para>2026-10-10（三档不再对 50 系隐藏）：50 系改走与 40 系同构的布局 ⇒ 三档与 EC 限充开关在 50 系上
	/// 同样存在、同样参与本规则（不再是"两个引用均为 null 的安全空操作"）；仅 Gen30 / 未知代际仍不建二者。</para>
	/// </summary>
	private void ApplyChargeLimitStageEnablement()
	{
		// 2026-10-12（用户报 bug ② 原话「在EC限充关闭的情况下 三档限充无法点击 修复一下」）：
		// **三档与 EC 限充开关的启用态从此分开判** —— 改前两者共用同一个 `flag`，而
		// `flag` 里的 `_chargeLimitStageBaseEnabled` 是**EC 直写通道**的可用性（
		// `EcChargeLimit.IsAvailableOnThisMachine() && !只读降级`）⇒ 一旦这条通道判为不可用（机型判据降级、
		// 或 ec_charge_limit=0），三档就被一起置灰 —— 但按传输模型三档只是**档位值来源**，
		// EC 限充关闭时它该走 GCU 官方报文（BatteryProtection/Control，见 ApplyBatteryLimitModeAsync），
		// 与 EC 通道是否可用无关。
		//   · checkChargeLimit（EC 限充开关）：判据**一字未改** —— 它写 EC 寄存器，仍要求 EC 通道可用。
		//   · 三档：任一条通路可用即可点 = EC 通道可用 ‖ EC 限充开关本身是开的（开关态记忆）‖ GCU 通路可用。
		// ⛔ 互斥规则原样保留（无极限充实验滑块生效 ⇒ 两者都灰）；⛔ 写入侧的闸门（ApplyBatteryLimitModeAsync）一字未动。
		bool ecSwitchEnabled = _chargeLimitStageBaseEnabled && !_chargeSliderEnabled;
		bool ecSwitchOn = BatteryControl.LockEnabled();
		bool gcuPathAvailable = GcuBatteryProtectionAvailable();
		bool tierEnabled = !_chargeSliderEnabled && (_chargeLimitStageBaseEnabled || ecSwitchOn || gcuPathAvailable);
		if (_batteryModeButtons != null)
		{
			Logger.WriteLineIfChanged("charge-limit-tier-enable", $"[charge-limit] 三档 Enabled={tierEnabled}（EC 通道基准={_chargeLimitStageBaseEnabled}、EC 限充开关={ecSwitchOn}、GCU 电池保护可用={gcuPathAvailable}、无极限充滑块={_chargeSliderEnabled}）");
			RButton[] batteryModeButtons = _batteryModeButtons;
			for (int i = 0; i < batteryModeButtons.Length; i++)
			{
				batteryModeButtons[i].Enabled = tierEnabled;
			}
		}
	}

	private void OnChargeLimitVerdictChanged(ChargeLimitVerdict verdict)
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action<ChargeLimitVerdict>(OnChargeLimitVerdictChanged), verdict);
			return;
		}
		if (verdict == ChargeLimitVerdict.Ineffective)
		{
			ToastForm.ShowFailure(Strings.ChargeLimitIneffective);
			// 2026-10-08：三档与开关已常驻，回退后只需按 EC 回读重新同步 UI。
			RefreshBatteryModeButtons();
			return;
		}
		if (verdict == ChargeLimitVerdict.Verified)
		{
			RefreshBatteryModeButtons();
			Program.toast?.RunToast(Strings.ChargeLimitVerified);
		}
	}

	public void RefreshDeviceCapabilities()
	{
		if (IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			if (Interlocked.Exchange(ref _refreshCapsQueued, 1) == 0)
			{
				BeginInvoke(() =>
				{
					Interlocked.Exchange(ref _refreshCapsQueued, 0);
					RefreshDeviceCapabilities();
				});
			}
			return;
		}
		MechrevoHw hw = Program.hw;
		MechrevoDeviceCapabilities caps = hw?.Capabilities ?? _deviceCapabilities;
		bool audit = Program.UiAuditMode && !Program.UiAuditUseReportedCapabilities;
		SupportDecision decision = RuntimeModelSupport.Current();
		bool flag = RuntimeModelSupport.ShouldDegradeToReadOnly(decision, hw != null && hw.IsReconnecting && hw.ConnectionGeneration == 0);
		List<bool> list = new List<bool>(_quickSwitches.Length);
		Dictionary<string, bool> visibleByKey = new Dictionary<string, bool>(_quickSwitches.Length);
		CheckBox[] quickSwitches = _quickSwitches;
		for (int num = 0; num < quickSwitches.Length; num++)
		{
			string key = (quickSwitches[num].Tag as string) ?? "";
			bool flag2 = audit || (hw != null && hw.SupportsQuickSwitch(key)) || StaticQuick(key);
			visibleByKey[key] = flag2;
			list.Add(flag2);
		}
		bool flag3 = audit || list.Any((bool result) => result);
		bool flag4 = Show(caps.Keyboard || (hw?.SupportsKeyboard ?? false));
		bool flag5 = Show(caps.Lightbar || caps.RgbLightbar || (hw?.SupportsLightbar ?? false));
		bool flag6 = Show(caps.LogoLight || (hw?.SupportsLogoLight ?? false));
		bool flag6b = Show(hw?.SupportsHingeLight ?? false);
		bool flag6c = Show(hw?.SupportsSyncLight ?? false);
		// 2026-10-09（lightmerge / lightfix2 / lightsplit）：灯光区「额外汇（灯带 / Logo 灯）」**逐行**收不收 ——
		// 只影响这两行的可见性与灯光区通道数（flag5/flag6 后面同时喂给 rowLightbar/rowLogo 的 Visible
		// 与本方法的布局签名；2026-10-11 起「通道数」那一份只用于可见性/行显隐，卡片标题行的摘要已下线 ⇒
		// 不再有 _lightChannel* 这类汇总字段），不碰任何下发（Program 那边的灯效去重 / 补 ID / 外部灯条通道一律不动）。
		// ⚠️ lightsplit 起每行有**自己**的三态键：light_strip_ui 管灯带行、light_logo_ui 管 Logo 行；
		// 判据仍是 LightExtraRows.BaseRowPhantom / LogoRowSupported（"证据不足就保持现状"，故默认 auto 零变化）。
		string lightStripUiMode = LightStripUiMode.Current();
		string lightLogoUiMode = LightLogoUiMode.Current();
		(bool lightExtraLightbar, bool lightExtraLogo) = LightExtraRows.Resolve(hw, flag5, flag6, lightStripUiMode, lightLogoUiMode, out string lightExtraReason);
		if (lightExtraLightbar != flag5 || lightExtraLogo != flag6)
		{
			Logger.WriteLineIfChanged("light-extra-ui", $"[lightmerge] 灯光额外汇：灯带行 {flag5}->{lightExtraLightbar}、Logo 行 {flag6}->{lightExtraLogo}（{lightExtraReason}；light_strip_ui={lightStripUiMode} light_logo_ui={lightLogoUiMode}）");
		}
		else if (lightExtraReason.StartsWith("base:auto", StringComparison.Ordinal))
		{
			// 只在"两行判据都让它们保持现状"时留一条（不会刷屏：WriteLineIfChanged 去重）。
			Logger.WriteLineIfChanged("light-extra-ui-keep", $"[lightmerge] 灯光额外汇保持现状（{lightExtraReason}；light_strip_ui={lightStripUiMode} light_logo_ui={lightLogoUiMode}）");
		}
		flag5 = lightExtraLightbar;
		flag6 = lightExtraLogo;
		// 2026-10-15（screen-gate）：「屏幕」区主体（panelBrightness，内含刷新率行 + 亮度行 + 校色 +
		// 色彩增强共 5 行）原来**只认 GCU 上报的能力位**，于是停掉 GCUBridge 后整块消失、只剩「屏幕」
		// 标题与「自动刷新率」开关。但这两项的数据源本来就是 Win32/WMI（与 GCU 无关）：
		//   * 刷新率 —— ScreenNative.MeasureMaxRefreshRate（EnumDisplaySettingsEx，只测量、**不落盘**）；
		//   * 亮度  —— ScreenBrightness.TryGet（root\wmi:WmiMonitorBrightness）。
		// 于是 flag7/flag10 各补一条"Win32/WMI 实测能读到"的判据（OR，只放宽不收紧）；两条实测都带缓存
		// （见 ScreenGateProbe），不会让每次 RefreshDeviceCapabilities 都去枚举显示模式 / 查 WMI。
		// ⚠️ 只放宽"屏幕区可见性"这一件事：下面的「自动刷新率」开关仍由它自己的 GCU 门控制，见 §④。
		bool flag7 = Show(caps.DisplayRefresh || (hw?.SupportsDisplayRefresh ?? false) || ScreenGateProbe.Win32MaxRefreshRate() > 0);
		bool flag8 = flag4 | flag5 | flag6 | flag6b | flag6c;
		bool flag9 = audit | flag8;
		bool flag10 = Show((hw?.ScreenBrightnessSeen ?? false) || ScreenGateProbe.WmiBrightnessReadable());
		// 2026-10-16：校色行原先只有下面这一条判据（只认 GCU 上报的能力位）⇒ 宝龙达（非 GCU）下
		// 恒为假 ⇒ 整行三个控件（开关/下拉/设置）一起隐藏，只留下恒可见的标题 calibCaption = 空壳。
		// 但这一行里**只有开关**真正需要 GCU：它的 Click 整段包在 `if (hw.IsGcuConnected)` 里
		// （见 :2375），点开关本质是 SetColorCalibration ⇒ 纯 MQTT。另外两项都有**非 GCU 的可用路径**：
		//   * 下拉的「自定义」档 —— :2516 的自定义 ICC 分支直接走 IccHelper.ApplyIcc
		//     → WcsAssociateColorProfileWithDevice（Windows 标准 API / mscms.dll），与 GCU 无关；
		//   * 「设置」按钮 —— 打开 CalibrationStartupForm，里面全是本地配置（ICC 导入、开机自动应用）。
		// 于是照 flag7/flag10 的既有模式（OR 补一条"实测可用"的判据，**只放宽不收紧**）拆成两条：
		//   flag11GcuOnly —— 严格版，仍只认 GCU 上报；**只给开关用**（开关没有非 GCU 路径，
		//                    放宽它就会做出"能点但没反应"的控件，违反能力位铁律）。
		//   flag11        —— 放宽版，额外认"宝龙达上本地 ICC 路径可用"；给下拉、设置按钮、行可见性用。
		// ⚠️ 非宝龙达机型上 `UsesBaolongda` 恒为假 ⇒ 两条取值与改动前的 flag11 逐位相同。
		bool flag11GcuOnly = Show(caps.ColorCalibration || (hw?.SupportsColorCalibration ?? false));
		bool flag11 = Show(caps.ColorCalibration || (hw?.SupportsColorCalibration ?? false) || (hw?.UsesBaolongda ?? false));
		bool flag12 = Show(caps.LcdOverdrive || (hw?.SupportsLcdOverdrive ?? false));
		bool flag13 = flag10 | flag11 | flag12;
		bool flag14 = Show(AppConfig.Is("force_liquid_cooling_menu") || caps.LiquidCooling || (hw?.SupportsLiquidCooling ?? false));
		// 2026-10-18（机主：干净安装后「狂暴」不显示）：宝龙达后端 Capabilities.TurboMode=true，
		//   但首次刷新时 hw 可能还是 null / 用的还是机型表（无 TurboModeSupport 位）⇒ 按钮被藏。
		//   ⇒ 宝龙达机型**强制显示狂暴**（WMI 功能号 8 支持 3 档，已真机验证）。
		// 2026-10-18（机主：GCU 同方机型「首次启动狂暴不显示、重启一次 LM 才出现」）：
		//   ⭐ 根因 = 首次启动时 GCU 还没连上 ⇒ caps.TurboMode 为假 ⇒ 按钮被藏；
		//      等 GCU 连上后那次刷新本应补出来，但首帧已按"隐藏"排过版 ⇒ 用户看到的就是"没有狂暴"。
		//   ⭐ 修法 = **乐观显示**：GCU 服务已装且当前**还没连上**时，先按支持处理（等连上后以真实能力位为准，
		//      若机型确实只有两档，连上后会自动收起）。⛔ 老接口/宝龙达不走这条（宝龙达另有强制显示）。
		bool gcuExpectedOptimistic = !(hw?.IsConnected ?? false)
			&& MechrevoHw.IsGcuServiceInstalled
			&& !(hw?.UsesBaolongda ?? false);
		bool flag15 = Show(caps.TurboMode || (hw?.UsesBaolongda ?? false) || gcuExpectedOptimistic);
		// 2026-10-08（QA 开关，用户原话「你能不能给我开放静音狂暴按钮 … 我检查一下UI」）：
		// 「静音狂暴」按钮的可见性 = 原判据（能力位 SilentTurboAvailability == Supported，本机 Gen40
		// 的 TurboSubMode=false ⇒ 原判据为假）**或** 默认关闭的 QA 开关 UiQaShowAll.Enabled。
		// 不设环境变量/config 时后半段恒为 false，flag16 与改动前逐位一致（默认态零变化）。
		// ⚠️ 只影响这一颗的 Visible 与随之而来的等宽重排：不动任何取值/命令 —— 它的 Click 照旧走
		// SwitchPerformanceModeAsync(1, true)，由原有能力位自行判定；也随之不动 buttonTurbo 的文案
		// （「狂暴 / 高能狂暴」仍严格由 caps.SilentTurboAvailability 决定，见下方 buttonTurbo.Text）。
		bool flag16 = Show(caps.SilentTurboAvailability == FeatureAvailability.Supported) || UiQaShowAll.Enabled;
		bool flag17 = Show(caps.CpuPerformanceTuning || caps.FanSettings || (hw?.HasAnyCustomRange ?? false));
		int num2;
		int num3;
		if (!audit)
		{
			if (hw == null)
			{
				num2 = 0;
			}
			else
			{
				num2 = (hw.CanOfferGpuModeSwitch ? 1 : 0);
				if (num2 != 0)
				{
					goto IL_0313;
				}
			}
			num3 = 0;
			goto IL_0328;
		}
		num2 = 1;
		goto IL_0313;
		IL_0313:
		// 2026-10-06（gcu 门）：GCU 明确上报"不支持独显直连"时**整行隐藏**（与托盘一致，见 GpuRowHiddenByGcuReports）。
		// 只在 GCU 有明确否定值时生效；未连接 / 老 GCU / 字段缺失（未表态）时本条件恒为 false，行为与改动前一致。
		num3 = ((!(hw?.GpuRowHiddenByGcuReports ?? false) && Show(hw?.CanOfferIgpuOnly ?? false)) ? 1 : 0);
		goto IL_0328;
		IL_0328:
		bool flag18 = (byte)num3 != 0;
		bool flag19 = num2 != 0 && Show(hw?.SupportsDgpuDirect ?? caps.DgpuDirect);
		bool flag21 = audit | flag18 | flag19;
		bool flag22 = Show(hw?.SupportsLocalDimming ?? caps.LocalDimming);
		bool flag23 = flag13 | flag7; // 「屏幕」区主体（panelBrightness）的**唯一**可见性判据，见下方 §③
		KeyboardRgb rgb = Program.rgb;
		bool flag24 = rgb != null && rgb.ControllerAvailability == FeatureAvailability.Unsupported;
		// 2026-10-09（igpugate）：「核显模式显示」三选行的可见性只由世代决定（30 系 / 无独显不显示，
		// 其余显示 —— 判据判成隐藏时它也必须还在，这是用户要的后路）。面板在 SettingsForm 构造时
		// 就建好、之后才被交给设置对话框（同一个实例），这里同步一次可见性；数据行是 AutoSize，
		// TableLayoutPanel 会把不可见的行高度收到 0，所以不会留下空白。
		bool igpuRow = IgpuOnlyUiRowAvailable();
		if (_igpuOnlyUiPanel != null)
		{
			_igpuOnlyUiPanel.Visible = igpuRow;
		}
		string text = string.Join('|', new object[10] { true, true, true, true, flag21, flag23, true, flag14, flag9, flag3 }) + "|" + string.Join('|', list) + $"|{flag4}|{flag5}|{flag6}|{flag6b}|{flag6c}|{flag7}|{flag15}|{flag16}|{flag17}|{flag18}|{flag19}|{flag22}|{flag}|{flag24}|igpuRow={igpuRow}";
		// ⭐⭐ 2026-10-19（机主：宝龙达 Logo 开关跑到最右）：能力刷新也是布局修正的触发点之一
		//   ⚠️ 必须放在下面 `text == _lastCapabilityLayout` 早退**之前**（否则能力没变化就永远不重排）；
		//      方法内部幂等 + 仅宝龙达生效 + 行未布局时自动跳过 ⇒ 其它机型逐位不变 ✅
		ApplyBaolongdaLightRowLayout(hw);
		if (text == _lastCapabilityLayout)
		{
			ApplyChargeLimitSliderGating(flag);
			return;
		}
		_lastCapabilityLayout = text;
		int num4 = 0;
		quickSwitches = _quickSwitches;
		for (int num = 0; num < quickSwitches.Length; num++)
		{
			quickSwitches[num].Visible = list[num4++];
		}
		(string, string[])[] quickSwitchGroups = QuickSwitchGroups;
		for (int num = 0; num < quickSwitchGroups.Length; num++)
		{
			var (key2, source) = quickSwitchGroups[num];
			if (_quickGroupContainers.TryGetValue(key2, out (Control, Control) value))
			{
				bool visible = source.Any((string key3) => visibleByKey.TryGetValue(key3, out var value2) & value2);
				value.Item1.Visible = visible;
				value.Item2.Visible = visible;
			}
		}
		if (_quickGroup != null)
		{
			RefreshQuickGroupSummary();
		}
		if (_lightGroup != null)
		{
			foreach (Control item in AllDescendants(_lightGroup))
			{
				if (item.Name == "rowKeyboard")
				{
					item.Visible = flag4;
				}
				if (item.Name == "rowLightbar")
				{
					item.Visible = flag5;
				}
				if (item.Name == "rowLogo")
				{
					item.Visible = flag6;
				}				if (item.Name == "rowHinge")
				{
					item.Visible = flag6b;
				}
				if (item.Name == "rowSync")
				{
					item.Visible = flag6c;
				}
			}
			EnableSection(_lightGroup, flag9);
			SyncLightRows();
		}
		if (_lblKeyboardControllerStatus != null)
		{
			// ⭐ 2026-10-17（宝龙达 MRID6）：键盘灯走厂商 WMI（功能号 16/17/18）⇒ 有真实可用的软件通道，
			//   ⛔ 不再显示「控制器不支持软件灯效控制」❌；⭐ 其它机型保持原样（判据逐位不变）✅
			_lblKeyboardControllerStatus.Visible = flag4 && (audit | flag24)
				&& !(Program.hw?.UsesBaolongda ?? false);
		}
		_overdriveAvailable = audit | flag12;
		if (_overdriveChk != null)
		{
			_overdriveChk.Visible = _overdriveAvailable;
		}
		_settingsDialog?.SetDisplayGroupAvailable(_overdriveAvailable);
		// 2026-10-09（lightmerge / lightfix2）：「额外灯光项」那一行的**可见性永远为真**（见
		// LightExtraRowAvailable）—— 它是"判据判错时的后路"，不许再被同一套判据关掉；
		// 之所以仍交给 SetLightExtraRowAvailable 动态下发，是为了能力/选择晚到时也能补上。
		_settingsDialog?.SetLightExtraRowAvailable(LightExtraRowAvailable());
		if (_calibCombo != null)
		{
			// 二分设备（40 系 + sRGB 屏）档位仍由开关表达，但下拉框保留占位：
			// 用户口径——隐藏后整行右侧空一大块很怪，哪怕只有一项也要显示（是否可点见 CalibrationComboInteractive）
			_calibCombo.Visible = audit | flag11;
		}
		if (_calibSwitch != null)
		{
			// 2026-10-16：开关用**严格版** flag11GcuOnly —— 它的 Click 整段包在
			// `if (svc != null && hw != null && hw.IsGcuConnected)` 里（见 :2375），
			// 宝龙达下点了不会有任何下发（开关会拨动但状态与硬件不一致）⇒ 必须隐藏。
			// 能力位铁律：能点的就必须有反应。
			_calibSwitch.Visible = audit | flag11GcuOnly;
		}
		if (_calibSettingsBtn != null)
		{
			_calibSettingsBtn.Visible = audit | flag11;
		}
		// 2026-10-16：校色行（row 3）与色彩增强行（row 4）改由**行级可见性**统一管理 ——
		// 整行含标题同进同退，且 SetScreenRowControls 会把行高一起清零并扣减父容器高度 ⇒ 不留空档。
		// 此前这两行没有任何判据管过（原因见 ScreenRowHeights 的注释：数组只有 3 个元素 ⇒ row 3/4 越界 return）。
		// ⚠️ 只在宝龙达下启用：非宝龙达保持改动前的原样（那两行恒显示），逐位不变。
		if (hw?.UsesBaolongda ?? false)
		{
			// 校色行：整行跟放宽版 flag11。宝龙达下它恒为真 ⇒ 整行显示、空壳（标题在而按钮不在）随之消失；
			// 将来若判据变假，则整行连标题一起消失，不会再出现"有标题没按钮"。
			SetScreenRowVisible(3, flag11, new string[1] { "rowColorCalibration" });
			// 色彩增强行：整行隐藏（含标题 effectCaption）。
			// 判据 DisplayEffectOn.HasValue 等价于"GCU 上报过 Display/Status" —— 它全仓只有一个赋值点，
			// 在 MechrevoHw.OnDisplayStatus（MechrevoHw.cs:5590，MQTT payload 处理）⇒ 宝龙达下恒为 null。
			// 为什么整行隐藏而不是保留：ApplyDisplayEffectProfile / SetDisplayEffectSwitch 全是往
			// `Display/Control` 发 MQTT、profile 数据也来自 GCU 上报，**没有任何本地路径** ⇒ 宝龙达上确实
			// 做不了；而它此前"整行显示、开关点下去会回弹"（:2720 判 IsGcuConnected 后走 else 把开关拨回去）
			// 恰好违反"能点的就必须有反应"。
			SetScreenRowVisible(4, hw?.DisplayEffectOn.HasValue ?? false, new string[1] { "rowDisplayEffect" });
		}
		SetScreenRowVisible(2, flag10, new string[3] { "labelScreenBrightness", "sliderScreenBrightness", "labelScreenBrightnessValue" });
		// ⭐⭐ 2026-10-19（机主：读不到系统亮度时滑块置灰/隐藏并标注）：
		//   ⭐ 记录**既有能力闸门**对亮度行的放行结果；随后按"是否读得到系统亮度"补一次 UI 状态：
		//      · 读得到 ⇒ 滑块恢复可用、数值 = 真实百分比；
		//      · 读不到 ⇒ 滑块置灰，数值位显示「本机不支持软件调亮度」（⛔ 不显示假值）；
		//      ⛔ 这里只改 UI，不写硬件、也不改闸门判据（闸门说隐藏就仍然隐藏）✅
		_brightnessRowVisible = flag10;
		ApplyBrightnessVisualState();
		if (_hzSegTable != null)
		{
			SetScreenRowControls(1, _hzButtons.Count > 0, _hzSegTable);
		}
		// 2026-10-15（screen-gate）③：这里原来写的是 flag13（= 亮度∪校色∪过驱，**少了 flag7**），
		// 与下面 :8116 那处 EnableSection(panelBrightness, flag23) 用的 flag23（= flag13|flag7）**不一致**；
		// 而 EnableSection 在 RefreshDeviceCapabilities 末尾无条件执行 ⇒ 本行等于被覆盖成无效代码。
		// 统一成 flag23（两者取并集的那个，更宽），理由：它才是「屏幕区主体」的语义判据
		// （刷新率行或亮度行任一有数据源就该显示）；且与 EnableSection 一致后，本行不再是"看着像
		// 闸门、实际不起作用"的误导代码。⚠️ 不收紧：flag23 ⊇ flag13，本行改前只会"关得更多"。
		SetVisible("panelBrightness", flag23);
		if (buttonSilentTurbo != null)
		{
			buttonSilentTurbo.Visible = flag16;
		}
		buttonTurbo.Visible = flag15;
		buttonTurbo.Text = (caps.SilentTurboAvailability == FeatureAvailability.Supported) ? Strings.TurboHighEnergy : Strings.Turbo;
		buttonTurbo.AccessibleName = buttonTurbo.Text;
		// 2026-10-04：30 系「模式 × 配置」机型隐藏「自定义」按钮
		// （官方 30 系控制台无自定义模式，且该机型不上报 CustomProfileIndex，无法回读确认）
		// 2026-10-09（实验开关，用户原话「c」）：**只在这里**多挂一条默认关闭的实验旁路
		// Force30Ui.Enabled —— 打开后「自定义」按钮也按 30 系样式隐藏。它只影响下面这个
		// Visible 与 ReflowPerformanceButtons 的排布；⚠️ 自定义模式本身的可用性/取值/命令
		// 一概不变（ResolveModeProfileIndex 里的 custom 分支与 SupportsModeProfileConfig 一字未动）。
		// ⭐⭐ 2026-10-17（宝龙达 · 自定义档本地化）：⭐ 宝龙达没有固件自定义槽，
		//   但它的参数（PL1/PL2/温度墙 → WMI 23 · GPU 超频 → NVAPI · 风扇曲线 → 20/21 软件闭环）
		//   **都能本地下发** ⇒ 入口不该再依赖 flag17（那三个能力位它一个都不上报）✅
		//   ⚠️ 30 系「模式 × 配置」机型仍按 `SupportsModeProfileConfig` 隐藏（判据未变）✅
		bool bdCustomEntry = Program.hw?.UsesBaolongda ?? false;
		bool flagCustomMode = (flag17 || bdCustomEntry) && !((Program.hw?.SupportsModeProfileConfig ?? false) || Force30Ui.Enabled);
		if (buttonCustomMode != null)
		{
			buttonCustomMode.Visible = flagCustomMode;
		}
		ReflowPerformanceButtons(flag16, flag15, flagCustomMode);
		ApplyPerfConfigRowVisibility();
		_gpuRowLayout = (hw?.GpuRowLayout ?? GpuRowLayout.Hidden);
		panelGPU.Visible = flag21;
		// ⭐⭐ 2026-10-16（宝龙达适配 · 用户原话「⭐ GPUmode接入 宝龙达机器就显示两个按钮就行」）：
		//   ⭐ 宝龙达协议 ⭐ **只有两个**显卡模式（⭐ 功能号 9 ✅）：
		//      ⭐ 0 = 混合模式（⭐ 对应按钮「⭐ 标准」✅）· ⭐ 1 = 独显直连（⭐ 对应按钮「⭐ 独显直连」✅）
		//   ⇒ ⭐ 所以 ⭐ **隐藏「核显/Eco」那一颗** ⭐ ⇒ ⭐ 正好剩两个 ✅
		//   ⭐ 实现依据：⭐ `VisualiseGPUButtons(eco, ultimate)` 的三行是
		//      ⭐ `buttonEco.Visible = eco` · ⭐ `buttonStandard.Visible = eco | ultimate` ·
		//      ⭐ `buttonUltimate.Visible = ultimate` ✅
		//      ⇒ ⭐ 传 ⭐ (eco: false, ultimate: true) ⭐ **刚好就是"⭐ 混合 ＋ ⭐ 独显直连"⭐ 两颗** ✅
		//   ⚠️ 分支**以 `UsesBaolongda` 开头** ⇒ ⭐ 同方 / GCU 机型走 else ⭐ **逐字原样**（⭐ 零影响 ✅）
		if (hw != null && hw.UsesBaolongda)
		{
			VisualiseGPUButtons(eco: false, ultimate: true);
			Logger.WriteLineIfChanged("baolongda-gpu-buttons",
				"宝龙达显卡模式：⭐ 只显示两个按钮（⭐ 混合 ＋ ⭐ 独显直连 ✅）⇒ ⭐ 已隐藏「核显/Eco」那一颗"
				+ "（⭐ 因为宝龙达协议只有功能号 9 的 0/1 两个取值 ✅）");
		}
		else
		{
			VisualiseGPUButtons(flag18, flag19);
		}
		MiniledOffered = flag22;
		buttonMiniled.Visible = flag22;
		// 2026-10-13（miniledgcu）：MiniLED 取证行 —— 改前这一整条判据零日志，诊断包里无从自证
		// "为什么这颗不显示"。口径与 flag22 同源（Show(hw?.SupportsLocalDimming ?? caps.LocalDimming)），
		// 由 MechrevoHw.SupportsLocalDimming 在解析时写进 LocalDimmingEvidence
		// （Source= 区分实时上报 / 注册表回退 / 两者皆无）。WriteLineIfChanged ⇒ 只在取证串变化时落盘、
		// 不会刷屏；写日志失败也不影响任何可见性（纯读，不下发、不写注册表）。
		Logger.WriteLineIfChanged("miniled-offer", hw?.LocalDimmingEvidence ?? $"MiniledOffered={flag22} source=none flag22={flag22} gcuReported=False gcu=unset registry={caps.LocalDimming} seen=False (hw=null)");
		_enabledDashboardSections.Clear();
		EnableSection(panelPerformance, enabled: true);
		EnableSection(_panelGcuStatus, enabled: true);
		EnableSection(panelGPU, flag21);
			EnableSection(_liquidGroup, flag14);
		EnableSection(_quickGroup, flag3);
		EnableSection(_lightGroup, flag9);
		EnableSection(panelBattery, enabled: true);
		EnableSection(Controls.Find("panelBrightness", searchAllChildren: true).FirstOrDefault(), flag23);
		// 2026-10-15（screen-gate）⑤：屏幕区闸门的诊断日志 —— 改前这一整条判据零日志，用户报
		// "停 GCUBridge 后屏幕区整块消失"时只能靠 VisualiseScreen 那条日志反推。这里把四个 flag 与
		// 两条**实测**判据的原始值一并落盘（WriteLineIfChanged ⇒ 只在组合变化时写，不刷屏；
		// 纯读，不改任何可见性、不下发、不写 config）。
		EnableSection(panelFooter, enabled: true);
		Logger.WriteLineIfChanged("screen-gate", $"screen-gate: flag7={flag7} flag10={flag10} flag11={flag11} flag12={flag12} flag13={flag13} flag23={flag23} (panelBrightness 主体可见={flag23}) win32Max={ScreenGateProbe.Win32MaxRefreshRate()} wmiBright={ScreenGateProbe.WmiBrightnessReadable()} gcuDisplayRefresh={caps.DisplayRefresh} gcuHzList={hw?.SupportsDisplayRefresh ?? false} gcuBrightSeen={hw?.ScreenBrightnessSeen ?? false}");
		ApplyUnsupportedModelNotice(flag, decision);
		if (_unsupportedModelBanner != null && !_enabledDashboardSections.Contains(_unsupportedModelBanner))
		{
			_enabledDashboardSections.Add(_unsupportedModelBanner);
		}
		ApplyChargeLimitSliderGating(flag);
		SyncHzButtons();
		ArrangeDashboard();
		PerformLayout();
		UpdateTelemetryText();
		void EnableSection(Control? section, bool enabled)
		{
			if (section != null)
			{
				section.Visible = enabled;
				if (enabled)
				{
					_enabledDashboardSections.Add(section);
				}
			}
		}
		void SetScreenRowVisible(int row, bool visible2, params string[] controlNames)
		{
			if (_screenRowsLayout != null)
			{
				Control[] controls = controlNames.Select((string name) => Controls.Find(name, searchAllChildren: true).FirstOrDefault()).OfType<Control>().ToArray();
				SetScreenRowControls(row, visible2, controls);
			}
		}
		void SetVisible(string name, bool visible2)
		{
			Control control = Controls.Find(name, searchAllChildren: true).FirstOrDefault();
			if (control != null)
			{
				control.Visible = visible2;
			}
		}
		bool Show(bool supported)
		{
			return audit | supported;
		}
	}

	private void ApplyUnsupportedModelNotice(bool unsupported, SupportDecision decision)
	{
		bool flag = (IsReadOnlyDegraded = unsupported);
		if ((_unsupportedModelLabel != null) & flag)
		{
			_unsupportedModelLabel.Text = ((decision.Reason == SupportReason.Unparsable) ? Strings.ModelUnparsable : string.Format(Strings.ModelUnsupported, decision.ProjectId));
		}
		if (_unsupportedModelBanner != null)
		{
			_unsupportedModelBanner.Visible = flag;
		}
		string[] writeEntryControlNames = WriteEntryControlNames;
		foreach (string key in writeEntryControlNames)
		{
			Control control = Controls.Find(key, searchAllChildren: true).FirstOrDefault();
			if (control != null)
			{
				control.Enabled = !flag;
			}
		}
		CheckBox[] quickSwitches = _quickSwitches;
		for (int i = 0; i < quickSwitches.Length; i++)
		{
			quickSwitches[i].Enabled = !flag;
		}
		// 2026-10-11（磁贴行 · 同源）：磁贴的启用态跟它引用的那颗「更多开关」走 ——
		// 不支持的机型上整块界面本来就是只读的（见上面 WriteEntryControlNames 那一圈），
		// 磁贴也不能例外地"看起来能点"。
		foreach (RButton tile in _quickTileButtons)
		{
			CheckBox owner = FindQuickSwitch(tile.Tag as string);
			tile.Enabled = ((owner == null) || owner.Enabled);
		}
		if (_modelOverrideBox != null)
		{
			_modelOverrideBox.Enabled = true;
		}
	}

	// ============================================================================================
	// 2026-10-09（warmupreload）：首次装 GCU「预热期漏报 ⇒ 被判不支持并永久隐藏」的一次性软重载。
	// 触发链路的全部上游都在 GcuWarmupReload.cs（由 Program 的 hw.ConnectionReady 调用）+ Program.cs:604；
	// 这里只放"重载之后要做的几件 UI 事"所需的**只读/还原**小工具（下面 4 个方法只在软重载里被调用）：
	//   * 抓/还原滚动位置 —— 软重载会真实重排仪表盘（panelBattery 一行可能刚从隐藏变可见），
	//     若不还原，RScrollBar 会按新范围重算并把内容顶回顶部（用户看到的"跳回顶部"）；
	//   * 抓/还原键盘焦点 —— 让 DPI/布局重算后焦点不丢（尽量不打扰）。
	// ⚠️ 这 4 个方法不写 config、不碰硬件、不改任何闸门判定；唯一写 config 的是 GcuWarmupReload 里的
	// 一次性标记键。全部 try/catch，失败只是"没还原"，绝不影响刷新本身。
	// ============================================================================================

	/// <summary>软重载前抓滚动位置（只读）。返回 scrollBar.Value，并输出旧范围上限。</summary>
	internal int CaptureDashboardScroll(out int maxBefore)
	{
		maxBefore = 0;
		try
		{
			if (_dashboardScrollBar != null)
			{
				maxBefore = _dashboardScrollBar.MaxValue;
				return _dashboardScrollBar.Value;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("预热重载：抓取滚动位置失败（忽略）：" + ex.Message);
		}
		return -1;
	}

	/// <summary>
	/// 软重载后还原滚动位置：按**当前内容范围**夹取旧偏移再写回（新范围允许时位置逐像素保持；
	/// 万一重排后范围变小，就落在新的最大偏移上，不会跳回顶部）。
	/// </summary>
	internal void RestoreDashboardScroll(int value, int maxBefore)
	{
		try
		{
			if (value >= 0 && _dashboardScrollBar != null)
			{
				_dashboardScrollBar.Value = Math.Min(value, _dashboardScrollBar.MaxValue);
				UpdateDashboardScroll();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("预热重载：还原滚动位置失败（忽略）：" + ex.Message);
		}
	}

	/// <summary>软重载前抓键盘焦点（只读）。没焦点就返回 null。</summary>
	internal IWin32Window? CaptureKeyboardFocus()
	{
		try
		{
			Control? focused = FindDeepestFocusedControl(this);
			if (focused != null && focused is not Form && !focused.IsDisposed && focused.CanSelect)
			{
				return focused;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("预热重载：抓取焦点失败（忽略）：" + ex.Message);
		}
		return null;
	}

	/// <summary>软重载后还原键盘焦点（控件已释放/不可选时静默放弃）。</summary>
	internal void RestoreKeyboardFocus(IWin32Window? target)
	{
		try
		{
			if (target is Control control && !control.IsDisposed && control.CanSelect && control.IsHandleCreated)
			{
				control.Focus();
				if (control is ComboBox combo)
				{
					combo.SelectAll();
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("预热重载：还原焦点失败（忽略）：" + ex.Message);
		}
	}

	private static Control? FindDeepestFocusedControl(Control root)
	{
		Control? focused = root.Focused ? root : null;
		foreach (Control child in root.Controls)
		{
			Control? focused2 = FindDeepestFocusedControl(child);
			if (focused2 != null)
			{
				focused = focused2;
			}
		}
		return focused;
	}

	/// <summary>软重载日志用（只读）：电池区当前是否上屏。</summary>
	internal bool IsBatteryPanelVisible()
	{
		return panelBattery?.Visible ?? false;
	}

	/// <summary>软重载日志用（只读）：「机型不支持」横幅当前是否上屏（＝ApplyUnsupportedModelNotice 的 flag）。</summary>
	internal bool IsUnsupportedModelBannerVisible()
	{
		return _unsupportedModelBanner?.Visible ?? false;
	}

	private void ApplyChargeLimitSliderGating(bool unsupportedModel)
	{
		bool flag = EcChargeLimit.IsAvailableOnThisMachine() && !unsupportedModel;
		sliderBattery.Enabled = flag;
		// ⭐⭐ 2026-10-17（宝龙达机型 · 真机反馈「一电池限充还在」）：
		//   宝龙达模具**没有任何限充通路** —— EC 直写物理上做不到（该模具没有 UWACPIDriver，
		//   `\\.\ACPIDriver` 打不开，真机诊断包已证；`EcChargeLimit.IsAvailableOnThisMachine()`
		//   里本来就有这条宝龙达分支）；GCU 官方三档也走不通（宝龙达机型根本没有 GCU，无 MQTT 通路）。
		//   ⇒ 三档按钮与整张电池卡片都不该出现（用户真机截图：三档 + 逃生口挂在设置里，点了只弹
		//   「GCU 未连接，限充模式设置失败」⇒ 违反铁律「能点的就必须有反应」）。
		// ⚠️ 为什么**不能**只依赖 `IsAvailableOnThisMachine()` 的宝龙达分支：`ec_charge_limit=1`
		//   的「强制放行」是在它**之前**早退的 ⇒ 用户若残留该键（该机主真机就是这种情况），
		//   flag 会变 true ⇒ 三档可点 + 整个面板上屏 ❌。而 `EcChargeLimit.cs` 是**零改动红线**（用户明令）
		//   ⇒ 判据只能放在界面侧这个「可见性唯一汇聚点」。
		// ⚠️ 非宝龙达机型 `baolongdaNoChargeLimit` 恒 false ⇒ 下面两处判断与改动前**逐位一致**。
		// ⭐⭐ 2026-10-18（机主：宝龙达电池页被隐藏）：⭐ 若宝龙达充电阈值面板可用 ⇒ 电池页**必须可见**
		//   （旧判据 `baolongdaNoChargeLimit = UsesBaolongda` 会把整张卡片隐藏 ❌）；其它机型逐位不变 ✅
		bool baolongdaRaw = Program.hw?.UsesBaolongda == true;
		bool baolongdaPanelAvailable = BaolongdaChargePanelAvailable(Program.hw);
		bool baolongdaNoChargeLimit = baolongdaRaw && !baolongdaPanelAvailable;
		// 2026-10-16（EC 直写逃生口）：`ec_charge_limit=0` 时 `IsAvailableOnThisMachine()` 恒 false ⇒ flag=false
		// ⇒ 整张电池卡片被隐藏 —— 而「关闭 EC 直写」的那颗开关**就在这张卡片里**（第 6 行）⇒ 用户勾上之后
		// 连撤销入口一起消失，等于把自己锁在门外。⇒ 这一支**保卡片可见**：此时 EC 各行已由
		// ApplyChargeLimitRowVisibility 收起、滑块也被 sliderBattery.Enabled=false 置灰，界面上只剩
		// 三档按钮 ＋ 逃生口本身 —— 正是用户要的「只保留 GCU 路」。
		// ⚠️ 三档在 `=0` 下仍可点：ApplyChargeLimitStageEnablement 的判据含「GCU 通路可用」那一支
		//    （2026-10-12 用户报 bug ② 的修法），故三档不因 EC 通道关闭而置灰。
		// ⚠️ 只对「用户显式写 0」这一支放宽，且**机型本身不支持时不放宽**（unsupportedModel 仍整卡隐藏）：
		//    通道打不开 / 只读降级 等自动判据**逐位不变**。
		bool explicitEcOff = IsEcDirectWriteOffConfigured();
		if (explicitEcOff)
		{
			// 本支是「用户显式关掉 EC 直写」这一种状态，只在状态翻转时记一次（⛔ 不刷屏）；
			// 用 WriteError ⇒ 默认「仅错误」级别也能看到，便于机主/用户自证"卡片没被自己关掉"。
			string msg = $"[charge-limit] EC 直写已被用户显式关闭（{EcDirectWriteOffConfigKey}=0）⇒ 电池卡片**保持可见**（撤销入口就在卡片最下面一行），EC 直写各行已收起、滑块置灰；三档是否可点仍按既有判据（GCU 通路可用即可点）";
			if (Logger.TryMarkChanged("charge-limit-ec-off-panel-kept", msg))
			{
				Logger.WriteError(msg);
			}
		}
		if (panelBattery != null)
		{
			// ⭐ 2026-10-17：宝龙达且充电阈值面板**不可用** ⇒ 整张电池卡片隐藏（UiAuditMode 仍放行）；
			// ⭐⭐ 2026-10-18（机主实测）：⭐ 若 `BatteryLimitPanel.IsAvailable` 为真 ⇒ **强制可见**
			//    （电池基础信息 + 新充电阈值区；旧三档/EC 直写行仍由下面 ApplyChargeLimitTierVisibility 收起）✅
			bool visible = Program.UiAuditMode || baolongdaPanelAvailable
				|| (!baolongdaNoChargeLimit && (flag || (explicitEcOff && !unsupportedModel)));
			panelBattery.Visible = visible;
			Logger.WriteLineIfChanged("battery-panel-visible",
				"[battery] 电池页可见性：baolongda=" + baolongdaRaw
				+ " · panelAvailable=" + baolongdaPanelAvailable + " ⇒ visible=" + visible
				+ (baolongdaPanelAvailable ? "（旧判据会隐藏）" : ""));
		}
		// ⭐ 2026-10-17：宝龙达下三档按钮行也收起（⛔ 不删控件、不删代码路径，只改 Visible 与行高）。
		// ⚠️ 2026-10-18：这里仍按 `baolongdaRaw` 收起旧三档（宝龙达没有 GCU 通路）；只放开电池阈值区 ✅
		ApplyChargeLimitTierVisibility(baolongdaRaw);
		// ⭐⭐ 2026-10-18（机主截图）：⭐ 旧「关闭 EC 直写充电上限（写入异常时用）」行也一起收 ✅
		ApplyEcDirectWriteOffVisibility(baolongdaRaw);
		// ⭐⭐ 2026-10-18（机主：面板行缺失）：⭐ 宝龙达且板号命中白名单 ⇒ **确保面板行存在**
		//   （构造期若本机 EC 还没就绪也不能漏建；这里补建一次并单独标记行索引）✅
		if (baolongdaRaw && _bdChargePanelRowIndex < 0 && _chargeLimitHostTable != null
			&& BatteryLimitPanel.IsSupportedBoard(Program.hw))
		{
			BuildBaolongdaChargeLimitSection(_chargeLimitHostTable);
		}
		// 2026-10-08 17:00：非 50 系滑块不上屏，这两句不会被人看到（50 系滑块的行内说明与开关共用同一条措辞）。
		toolTip.SetToolTip(sliderBattery, flag ? Strings.ChargeLimitWindowsNote : ChargeLimitDisabledReason(RuntimeModelSupport.Current()));
		// 2026-10-08：开关与三档快捷值跟随同一条 EC 通道的可用性，不能只灰滑块。
		// 2026-10-10（用户要求）：这里**只记录基准值**再交给 ApplyChargeLimitStageEnablement() 落地 ——
		// 本方法在构造之后还会由 RefreshDeviceCapabilities 再跑一次，若直接 Enabled=flag 会把
		// 「无极限充打开 ⇒ 上面置灰」重新点亮。基准值与开关态分开存，两条规则互不覆盖。
		_chargeLimitStageBaseEnabled = flag;
		// 2026-10-15（隐藏规则 A/B）：本方法是「机型是否不支持」与「EC 通道是否可用」的**既有唯一汇聚点**
		// （构造之后 RefreshDeviceCapabilities 还会再跑一次）⇒ 两条隐藏规则也挂在这里，天然跟随所有刷新路径。
		// 规则 A = flag 为假（机型不支持 / 只读降级 / ec_charge_limit=0 / 通道打不开）；
		// 规则 B = 10/16/20/30 系（用户原话「用这个可能损坏硬件」）。
		// ⚠️ 两条规则只收「EC 直写方式专属」的那几行；panelBattery 与三档按钮的可见/启用仍由 flag 决定
		// （代际门控**故意不进** flag）⇒ 老机型保留完整电池卡片 + 三档（GCU 路）。
		ApplyChargeLimitRowVisibility(!flag || EcChargeLimit.IsLegacyGenerationBlocked());
		ApplyChargeLimitStageEnablement();
		// ⭐⭐ 2026-10-18（机主：面板行被裁）：⭐ 所有行高调整结束后**统一重算一次**（单一来源）✅
		RecalculateBatteryTableHeight();
	}

	internal static string ChargeLimitDisabledReason(SupportDecision support)
	{
		if (AppConfig.GetString("ec_charge_limit") == "0")
		{
			return Strings.ChargeLimitOff;
		}
		return support.Reason switch
		{
			SupportReason.Unparsable => Strings.ChargeLimitUnparsable, 
			SupportReason.NotInSet => ModelRegistry.IsKnownProjectName(support.ProjectId) ? Strings.ChargeLimitUnsupportedModel : Strings.ChargeLimitUnparsable, 
			_ => Strings.ChargeLimitUnavailable, 
		};
	}

	private Control BuildUnsupportedModelRow(Func<int, int> D)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Name = "tableUnsupportedModelNotice",
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Percent, 100f)
			},
			RowStyles = 
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize)
			},
			Controls = { 
			{
				(Control)_unsupportedModelLabel,
				0,
				0
			} }
		};
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Name = "rowModelOverride",
			Dock = DockStyle.Fill,
			ColumnCount = 5,
			RowCount = 1,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(70)));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(64)));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(64)));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(120)));
		Label control = new Label
		{
			Text = Strings.ManualModel,
			Dock = DockStyle.Fill,
			ForeColor = UiVisualStyle.Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			Font = UiVisualStyle.Font(8.5f)
		};
		_modelOverrideBox = new RTextBox
		{
			Name = "textModelOverride",
			Dock = DockStyle.Fill,
			AutoSize = false,
			Height = D(24),
			Font = UiVisualStyle.Font(8.5f),
			Text = (ModelOverrideStateMachine.ReadConfiguredManualModel() ?? "")
		};
		_modelOverrideStatus = new Label
		{
			Name = "labelModelOverrideStatus",
			Dock = DockStyle.Fill,
			ForeColor = UiVisualStyle.Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			Font = UiVisualStyle.Font(8.5f)
		};
		RButton rButton = new RButton
		{
			Name = "buttonModelOverrideApply",
			Text = Strings.Apply,
			Dock = DockStyle.Fill,
			AutoSize = true
		};
		RButton rButton2 = new RButton
		{
			Name = "buttonModelOverrideAuto",
			Text = Strings.Auto,
			Dock = DockStyle.Fill,
			AutoSize = true
		};
		rButton.Click += (object? _, EventArgs _) =>
		{
			bool flag = ModelOverrideStateMachine.TrySetManual(_modelOverrideBox?.Text, out SupportDecision decision);
			if (flag)
			{
				ModelOverrideStateMachine.SetMode(ModelOverrideMode.ManualPinned);
			}
			_modelOverrideStatus.Text = (flag ? Strings.ModelOverridePinned : string.Format(Strings.ModelOverrideInvalid, decision.Reason));
			_modelOverrideStatus.ForeColor = (flag ? UiVisualStyle.Text : UiVisualStyle.Danger);
		};
		rButton2.Click += (object? _, EventArgs _) =>
		{
			ModelOverrideStateMachine.SetMode(ModelOverrideMode.Auto);
			_modelOverrideStatus.Text = Strings.ModelOverrideAuto;
			_modelOverrideStatus.ForeColor = UiVisualStyle.Muted;
		};
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(_modelOverrideBox, 1, 0);
		tableLayoutPanel2.Controls.Add(rButton, 2, 0);
		tableLayoutPanel2.Controls.Add(rButton2, 3, 0);
		tableLayoutPanel2.Controls.Add(_modelOverrideStatus, 4, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
		return tableLayoutPanel;
	}

	private void SetScreenRowControls(int row, bool visible, params Control[] controls)
	{
		if (_screenRowsLayout == null || row < 0 || row >= ScreenRowHeights.Length)
		{
			return;
		}
		for (int i = 0; i < controls.Length; i++)
		{
			controls[i].Visible = visible;
		}
		RowStyle rowStyle = _screenRowsLayout.RowStyles[row];
		Control parent = _screenRowsLayout.Parent;
		if (parent == null)
		{
			return;
		}
		if (!visible)
		{
			if (!(rowStyle.Height <= 0f))
			{
				int num = _screenRowsLayout.GetRowHeights()[row];
				_screenRowVisibleHeights[row] = ((num > 0) ? num : ScreenRowHeights[row]);
				rowStyle.Height = 0f;
				parent.Height -= _screenRowVisibleHeights[row];
			}
		}
		else if (!(rowStyle.Height > 0f))
		{
			int num2 = ((_screenRowVisibleHeights[row] > 0) ? _screenRowVisibleHeights[row] : ScreenRowHeights[row]);
			rowStyle.Height = num2;
			parent.Height += num2;
		}
	}

	private static bool ReflowSingleRow(TableLayoutPanel table, List<Control> buttons, ref string lastLayout)
	{
		string text = buttons.Count + ":" + string.Join('|', buttons.Select((Control button) => button.Name));
		if (text == lastLayout)
		{
			return false;
		}
		lastLayout = text;
		table.SuspendLayout();
		table.Controls.Clear();
		table.ColumnStyles.Clear();
		table.ColumnCount = Math.Max(1, buttons.Count);
		for (int num = 0; num < buttons.Count; num++)
		{
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (float)Math.Max(1, buttons.Count)));
			table.Controls.Add(buttons[num], num, 0);
		}
		table.ResumeLayout(performLayout: true);
		return true;
	}

	/// <summary>
	/// 构建「模式 × 配置」行：性能按钮下方横排三个下拉，分别绑定办公/游戏/狂飙的固件配置号。
	/// 三个模式号（2=办公 / 0=游戏 / 1=狂飙）与 ModeProfileConfig 的键、SwitchPerformanceModeAsync 参数同号。
	/// D 由调用方传入（DPI 缩放是 BuildDashboardLayout 的局部函数，项目惯例用 Func&lt;int,int&gt; 传递）。
	/// 仅 30 系显示，见 ApplyPerfConfigRowVisibility / MechrevoHw.SupportsModeProfileConfig。
	/// </summary>
	/// <summary>
	/// 「配置编辑」：复用自定义模式编辑界面。
	/// ⚠️ 2026-10-04 第一步只把按钮与界面接起来，**数据源尚未绑定到 30 系固件配置**
	///    （当前仍是 LM 自定义档），因此暂不要在表单内保存 —— 接上固件配置后移除本提示。
	/// </summary>
	/// <summary>
	/// 2026-10-10（用户要求 · ⭐ 原话「还有增加一个右键自定义模式按钮 会打开自定义窗口但是不切换模式」）：
	/// 「自定义」按钮**开窗这一段**的唯一实现 —— 左键（Click，切档前先开窗）与右键（MouseUp 右键，只开窗）
	/// 共用这一条路径，⛔ 不另写一份开窗逻辑（控件创建 / AddOwnedForm / 可见性 / Opacity+Update 置位 /
	/// PlaceAdjacent 全部与返工前逐字一致）。本方法**只开窗**：不切档、不写硬件、⛔ 不碰高亮与托盘菜单。
	/// </summary>
	/// <returns>可继续使用的编辑器；UI 审计模式或已释放（开不了）时返回 false，调用方应直接 return。</returns>
	private bool OpenCustomModeEditor()
	{
		if (customModeForm == null || customModeForm.IsDisposed)
		{
			customModeForm = new CustomModeForm();
			AddOwnedForm(customModeForm);
		}
		if (!customModeForm.Visible)
		{
			customModeForm.Opacity = 0.01;
			ResponsiveLayout.PlaceAdjacent(customModeForm, this);
			try
			{
				customModeForm.Show();
				customModeForm.Update();
			}
			finally
			{
				if (!customModeForm.IsDisposed)
				{
					customModeForm.Opacity = 1.0;
				}
			}
		}
		return !Program.UiAuditMode && !customModeForm.IsDisposed;
	}

	/// <summary>
	/// 2026-10-10：「自定义」编辑器要绑定的档位 id（左键与右键共用同一条解析）。
	/// ⚠️ 2026-10-09 既有修正原样搬入、语义一字未改：LastCustomId 可能指向**已经被删除**的档位 ——
	/// 那样 SelectModeAsync 会直接失败（FindMode 返回 null），点了【自定义】等于没点（正是用户抱怨的现象）。
	/// 这里退一步：目标不存在或不是自定义档时，改用"当前第一个存在的自定义档"。
	/// </summary>
	private static string ResolveCustomModeIdForEditor()
	{
		string customId = PerfModeStore.LastCustomId ?? PerfModeDefinition.CustomId(1);
		PerfModeService? perfForSwitch = PerfModeService.Instance;
		if (perfForSwitch != null && perfForSwitch.Find(customId)?.IsCustom != true)
		{
			customId = perfForSwitch.Modes.FirstOrDefault((PerfModeDefinition m) => m.IsCustom)?.Id ?? customId;
		}
		return customId;
	}

	private async Task OpenPerfConfigEditorAsync()
	{
		int visualMode = Program.service?.CurrentMode ?? 0;
		int profileIndex = ModeProfileConfig.Get(visualMode);
		Logger.WriteLine($"OpenPerfConfigEditor: visualMode={visualMode} profile={profileIndex} count={ModeProfileConfig.Count}");
		if (customModeForm == null || customModeForm.IsDisposed)
		{
			customModeForm = new CustomModeForm();
			AddOwnedForm(customModeForm);
		}
		if (customModeForm.Visible)
		{
			customModeForm.Hide();
			// 2026-10-05: the editor may have renamed a profile -- re-label the combos.
			RefreshPerfConfigComboLabels();
			return;
		}
		// 2026-10-04: bind the editor to (current mode, selected firmware profile).
		customModeForm.BindFirmwareProfile(visualMode, ModeProfileConfig.Get(visualMode));
		customModeForm.Opacity = 0.01;
		ResponsiveLayout.PlaceAdjacent(customModeForm, this);
		try
		{
			customModeForm.Show();
			customModeForm.Update();
		}
		finally
		{
			if (!customModeForm.IsDisposed)
			{
				customModeForm.Opacity = 1.0;
			}
		}
	}

	/// <summary>2026-10-05: "\u914d\u7f6e N" plus the profile's CustomizeName when it has one.</summary>
	private static string PerfConfigComboText(PerfModeKind kind, int profileIndex)
	{
		// 2026-10-05: when a custom name exists, show ONLY the name (user request).
		// Names are LM-local (the GCU has none for preset profiles).
		int num = ((kind == PerfModeKind.Silent) ? 2 : ((kind == PerfModeKind.Turbo) ? 1 : 0));
		string text2 = ModeProfileConfig.GetName(num, profileIndex);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		// 2026-10-05: 下拉框宽 D(72)，英文 "Config 1" 会被裁成 "Conf..."，故用短标签。
		// 2026-10-10（用户原话「暴露一个问题 配置号被裁切了」→「但是这样子就不是一排的了」→
		// ⭐「这没必要吧 不能字体小一点就能塞进去了吗」）：这里**保持全称「配置 N」**（en "Cfg N"），
		// 一律不再缩写 —— 腾宽度改由「该行字号小一档（9pt→8pt）+ 该下拉自己的箭头格/内边距收窄
		// （26→24 / 8→5 逻辑像素，逐控件覆盖，见 RComboBox.ArrowCellLogicalPx / TextPadLogical）」
		// 完成。逐档实测（探针，100/125/150/175/200%）：
		//   全称「配置 1」所需文本宽 = 43/52/56/67/77 设备；该行 8pt 下模式名标签 = 29/37/42/50/57；
		//   箭头格+2×内边距 = 34/42/51/60/68；每格可用 = 120/148/176/203/230
		//   ⇒ 合计 118/137/157/194/218，**五档全部放得下**（余量 2/11/19/9/12 设备像素）。
		// 自定义名优先的分支一字未改。
		return Strings.ModeConfigTiny + " " + (profileIndex + 1);
	}

	/// <summary>2026-10-05: re-label the three profile combos (e.g. after a rename).</summary>
	private void RefreshPerfConfigComboLabels()
	{
		if (perfConfigCombos == null)
		{
			return;
		}
		int[] array = new int[3] { 2, 0, 1 };
		_refreshingConfigLabels = true;
		try
		{
			for (int i = 0; i < perfConfigCombos.Length && i < 3; i++)
			{
				RComboBox rComboBox = perfConfigCombos[i];
				if (rComboBox == null)
				{
					continue;
				}
				int num = Math.Max(1, ModeProfileConfig.Count);
				PerfModeKind perfModeKind = ((array[i] == 2) ? PerfModeKind.Silent : ((array[i] == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));
				int selectedIndex = rComboBox.SelectedIndex;
				rComboBox.BeginUpdate();
				rComboBox.Items.Clear();
				for (int j = 1; j <= num; j++)
				{
					rComboBox.Items.Add(PerfConfigComboText(perfModeKind, j - 1));
				}
				rComboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, num - 1);
				rComboBox.EndUpdate();
				// 2026-10-10（修「配...」）：项文本（自定义名）换过 ⇒ 让所在格重排一次，
				// 触发下面的 Layout 钩子按新文本重算宽度（不重排则宽度还是旧文本的量）。
				rComboBox.Parent?.PerformLayout();
			}
		}
		finally
		{
			_refreshingConfigLabels = false;
		}
	}

	/// <summary>
	/// 2026-10-10（用户原话「暴露一个问题 配置号被裁切了」）：算配置号**不裁字**所需的下拉宽，
	/// 并把它夹进本格实际可用宽。量法 = <see cref="RComboBox" /> **自己**的闭合态文本矩形
	/// （DrawClosedStateText / PaintSplit 同源）：可用文本宽 = 客户端宽 − 箭头格 − 2×内边距，
	/// 其中两个量直接取该控件自己的覆盖值（<see cref="RComboBox.ArrowCellLogicalPx" /> /
	/// <see cref="RComboBox.TextPadLogical" />，两者**唯一真源在 RComboBox**，这里不另抄一份）；
	///   ⇒ 所需控件宽 = 箭头格 + 2×内边距 + **最长的项文本宽**
	///      （TextRenderer.MeasureText，与既有 UiAuditRunner.GetTextClipping 同一口径）+ D(4) 余量。
	/// 夹取：不低于改前的 D(72)（短文本不倒退），不高于本格可用宽（窗口过窄时宁可让
	/// 文本按 WinForms 常规省略，也绝不把箭头格裁掉）。
	/// ⚠️ 只写 Width —— 高度/字号/颜色/项/事件/下发逻辑一字未动；值不变不写，避免 Layout 里自触发重排。
	/// </summary>
	private static void FitPerfConfigComboWidth(Control cell, RComboBox combo, Func<int, int> D)
	{
		int chrome = D(combo.ArrowCellLogicalPx) + D(combo.TextPadLogical) * 2;
		int textWidth = 0;
		foreach (object item in combo.Items)
		{
			textWidth = Math.Max(textWidth, TextRenderer.MeasureText(combo.GetItemText(item), combo.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width);
		}
		int wanted = Math.Max(D(72), chrome + textWidth + D(4));
		// 本格可用宽 = 格宽 − 本下拉左右边距 − **它左边已占位的兄弟控件**（模式名标签 + 其边距），
		// 否则"标签 + 下拉"加起来会超出格宽、下拉被格裁掉（右边缘连箭头一起消失）。
		int used = 0;
		foreach (Control sibling in cell.Controls)
		{
			if (sibling == combo)
			{
				break;
			}
			if (sibling.Visible)
			{
				used += sibling.Width + sibling.Margin.Horizontal;
			}
		}
		int available = cell.ClientSize.Width - combo.Margin.Horizontal - used;
		// 格还没被布局（宽 0/极小）⇒ 先不动，等它的 Layout 事件再算。
		if (available <= 0)
		{
			return;
		}
		int width = Math.Min(wanted, available);
		if (width > 0 && combo.Width != width)
		{
			combo.Width = width;
		}
	}

	private void BuildPerfConfigRow(Func<int, int> D)
	{
		// 2026-10-08（回归修复：配置下拉尺寸）：「配置 N」这一行原来按 D(26) 建，
		// 比行内控件的自然高度还矮——RComboBox 的自然高是 D(30)（DropDownList + ItemHeight，
		// 见 RComboBox.CalibrateItemHeight），标签的行高上限也被撑到 57px，
		// 于是下拉被画到行矩形之外、叠到上一行「静音/平衡/狂暴」模式按钮上，
		// 标签被拉伸，视觉上就是「下拉变大把标签压住/挤歪」。
		// 行高改为 D(34)：与上方模式按钮行等高（tablePerf = D(34)），正好容下 D(30) 下拉 + D(2) 上边距，
		// 宽度与列宽比例一个字未改（列仍 3 等分 33.33%，下拉仍 D(72)）。
		// 2026-10-08（用户要求：下拉「高度」再矮一点；宽度不要动）：
		// 下拉走 RComboBox.NativeHeight=true —— ItemHeight 只按字体度量取最小（不再用 44*scale/192 那条
		// 「自然高 30」的公式），控件高 = 内容高 + 边框补偿，实测 D(24) 能完整放下「配置 1」这类文字（约 15px 高）
		// 且不裁字；行高随之由 D(34) 收到 D(26)（= D(24) 下拉 + 上下各 1px），
		// 仍是本行私有常量，与上方 tablePerf（D(34)）各走各的，不溢出、不压模式按钮。
		const int perfConfigRowHeight = 26;
		_perfRowExtraHeight = D(perfConfigRowHeight);
		// 2026-10-10（用户原话「这没必要吧 不能字体小一点就能塞进去了吗」）：⭐ 本行**小一档字号**。
		// 页面正文/其它行是 9pt，本行取 8pt —— 与同一面板里既有的两颗构件**同字号**：
		// 「配置编辑」按钮（UiVisualStyle.Font(8f)，BuildHeadRow 上方同排）与表格里的说明文字，
		// 故"小一档但不突兀"是有据可依的（不是为本行临时造的字号）。
		// ⚠️ 只设在本行的 TableLayoutPanel 上（标签与三颗下拉**继承**，不逐个写死），
		//    其余行/面板字号一字未动；DPI 感知由 Font(pt) 自身承担（点号随系统 DPI 放大，不是写死像素）。
		// 2026-10-04：改为 3 等分 TableLayoutPanel —— FlowLayoutPanel 会按内容撑宽，
		// 最小窗口宽度下不换行而是横向溢出被裁（「狂暴」下拉被切）
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 3,
			RowCount = 1,
			AutoSize = false,
			Height = D(perfConfigRowHeight),
			Margin = Padding.Empty,
			Font = UiVisualStyle.Font(8f),
			BackColor = UiVisualStyle.Surface
		};
		for (int c = 0; c < 3; c++)
		{
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		}
		perfConfigRow = tableLayoutPanel;
		int[] array = new int[3] { 2, 0, 1 };
		string[] array2 = new string[3] { Strings.Silent, Strings.Balanced, Strings.Turbo };
		perfConfigCombos = new RComboBox[3];
		for (int i = 0; i < 3; i++)
		{
			int modeValue = array[i];
			Label label = new Label
			{
				Text = array2[i],
				AutoSize = true,
				ForeColor = UiVisualStyle.Muted,
				// 2026-10-08：上边距不再写死 D(5)，建好后按行高重新算（见下面 label.Margin），
				// 使标签在 D(26) 行高里仍与下拉垂直居中对齐。
				Margin = new Padding(D(6), 0, D(2), 0)
			};
			RComboBox rComboBox = new RComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				// 2026-10-08：NativeHeight=true 后 ItemHeight 不再被撑到 D(30)，Height 才真正生效；
				// 下面的 Height = D(24) 即为用户要的「矮一点」。
				NativeHeight = true,
				// 2026-10-10（修「配置 N」裁字）：宽度不再是常量 D(72) —— 由
				// FitPerfConfigComboWidth 按**本下拉自己的项文本**实测后夹进本格可用宽（下面的
				// Fit + Layout 钩子），这里给的是初始值。
				Width = D(72),
				Height = D(24),
				// 2026-10-10（修「配置 N」裁字）：⭐ 只给这三颗窄下拉收窄**闭合态**的箭头格与文本内边距
				// （26→24 / 8→5 逻辑像素，默认值仍是 26/8，其它下拉逐像素不变）——
				// 这是"字体小一档之后仍差的那几像素"的补足（逐 DPI 实测：五档余量 11/11/14/11/12 设备像素）。
				// ⚠️ 不动展开列表的行内边距、不动高度、不动 chevron 画法。
				ArrowCellLogicalPx = 24,
				TextPadLogical = 5,
				Margin = new Padding(0, D(1), D(8), 0)
			};
			int num = Math.Max(1, ModeProfileConfig.Count);
			// 2026-10-05: show the profile's user-facing name (CustomizeName) when set.
			PerfModeKind perfModeKind = ((modeValue == 2) ? PerfModeKind.Silent : ((modeValue == 1) ? PerfModeKind.Turbo : PerfModeKind.Balanced));
			for (int j = 1; j <= num; j++)
			{
				rComboBox.Items.Add(PerfConfigComboText(perfModeKind, j - 1));
			}
			rComboBox.SelectedIndex = Math.Clamp(ModeProfileConfig.Get(modeValue), 0, num - 1);
			rComboBox.SelectedIndexChanged += async (object? _, EventArgs _) =>
			{
				int selectedIndex = rComboBox.SelectedIndex;
				if (selectedIndex >= 0 && !_refreshingConfigLabels)
				{
					ModeProfileConfig.Set(modeValue, selectedIndex);
					if (Program.service != null && Program.service.CurrentMode == modeValue)
					{
						// 2026-10-05: SwitchPerformanceModeAsync() dedupes a switch to the
						// already-active mode, so the newly chosen ProfileIndex was never
						// published -- the firmware stayed on the OLD profile, later writes
						// landed on the wrong config and were refused ("params reverted").
						// SetMode() always publishes mode + ProfileIndex. The mode itself
						// does not change, so this cannot cause an unwanted mode switch.
						MechrevoHw hwConfigRow = Program.hw;
						if (hwConfigRow != null && hwConfigRow.IsConnected && hwConfigRow.SupportsModeProfileConfig)
						{
							await hwConfigRow.SetMode(modeValue);
						}
						// 该模式正是当前模式：立即按新配置号重新下发
						// （模式 1=狂飙 需带 silentTurbo，0 与 2 传 null —— 沿用模式按钮的约定）
						bool? silentTurbo = ((modeValue == 1) ? (bool?)MechrevoService.IsSilentTurboActive : ((bool?)null));
						await SwitchPerformanceModeAsync(modeValue, silentTurbo);
					}
					// 2026-10-05: if the firmware-profile editor is open on this mode, re-bind it
					// so its config number and parameter values follow the dropdown.
					// 2026-10-05: MUST also honour _refreshingConfigLabels. Re-labelling the combos
					// sets their SelectedIndex, which fires this handler once per combo; without
					// this guard the last combo rebound the editor to a different mode
					// ("balanced/silent shown swapped").
					// 2026-10-05: only re-bind when the changed combo belongs to the ACTIVE mode.
					// Rebinding for a non-active mode made the editor jump to that mode even though
					// the machine stayed where it was (reported by the user).
					if (!_refreshingConfigLabels && Program.service != null && Program.service.CurrentMode == modeValue && customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)
					{
						customModeForm.BindFirmwareProfile(modeValue, selectedIndex);
					}
				}
			};
			perfConfigCombos[i] = rComboBox;
			// 每模式一个等宽单元格（模式名标签 + 配置号下拉，**单排并排**）
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = false,
				// 2026-10-08：高度钉死在行高上 —— Dock=Fill 时单元格会把子面板撑到
				// PreferredSize（远高于行高），标签随之被拉长、下拉落到行外。钉死后
				// 标签回到自身 D(5)+16+D(2) 的正常高度，下拉正好落在行内。
				Height = D(perfConfigRowHeight),
				Margin = Padding.Empty,
				BackColor = UiVisualStyle.Surface
			};
			flowLayoutPanel.Controls.Add(label);
			flowLayoutPanel.Controls.Add(rComboBox);
			// 2026-10-08：行高收到 D(26) 后，标签上边距不能再写死 D(5)（那会把标签压到行底）。
			// 按「行高 - 标签自然高」的一半取整定位，下拉同理（行高 - 下拉高）/2，
			// 两者都随 DPI 与各自实际高度自适应 —— 同行居中对齐、不裁字、不溢出本行。
			int availableRowHeight = D(perfConfigRowHeight);
			// 2026-10-10（修「配...」）：⭐ 左右边距**收紧**换宽度 —— 单排要同时装下
			// 模式名标签与下拉，逐档实测（100/125/150/175/200%）把 D(6)/D(2)/D(8) 收到
			// D(2)/D(1)/D(2) 才能让三格都有余量（各档余量 4/14/14/13/10 设备像素，见
			// PerfConfigComboText 的实测表）；上边距仍按行高居中，行高/垂直位置一字未改。
			label.Margin = new Padding(D(2), Math.Max(0, (availableRowHeight - label.PreferredHeight) / 2), D(1), 0);
			rComboBox.Margin = new Padding(0, Math.Max(0, (availableRowHeight - rComboBox.Height) / 2), D(2), 0);
			// 2026-10-10（修「配...」）：宽度按本下拉**自己的项文本**实测（见 FitPerfConfigComboWidth），
			// 格宽只有布局时才知道 ⇒ 首次算一次 + 每次 Layout 重算（值不变不写）。
			FitPerfConfigComboWidth(flowLayoutPanel, rComboBox, D);
			flowLayoutPanel.Layout += (object? _, LayoutEventArgs _) => FitPerfConfigComboWidth(flowLayoutPanel, rComboBox, D);
			perfConfigRow.Controls.Add(flowLayoutPanel, i, 0);
		}
		panelPerformance.Controls.Add(perfConfigRow);
		// Dock=Top 按 Controls 索引倒序停靠 —— 索引 0 才落在最下方（tablePerf 之后）
		panelPerformance.Controls.SetChildIndex(perfConfigRow, 0);
		perfConfigRow.Visible = false;
	}

	/// <summary>
	/// 按机型显示/隐藏「模式 × 配置」行，并相应调整性能面板高度
	/// （紧凑布局原本固定 D(66)，加一行需 +D(26)，见 BuildPerfConfigRow 的行高）。高度在建行时已算好存字段。
	/// </summary>
	/// <summary>
	/// 2026-10-11（本轮 · 标题行快捷开关组的显隐/宽度收敛）：把 <c>_perfShortcuts</c> 的
	/// <b>可见性</b>与<b>宽度</b>按"当前有几个可见子控件"重新收敛。
	///
	/// 为什么需要它（本轮去掉「风扇强冷」后暴露出来的两条既有脆弱点）：
	/// <list type="number">
	/// <item><see cref="BuildTitleRow" /> 只在构造期按当时的子控件可见性写一次宽度。构造期
	/// <c>_gpuOcBox</c>/<c>perfConfigEditButton</c> 都还是默认可见 ⇒ 之后能力位把它们关掉时，
	/// 那个宽度就**陈旧**了（实测 200 逻辑 px；125%/150%/200% 下放大成 250/300/400 ⇒
	/// <c>[left-fit] 左组…被裁</c> + <c>[parent-overflow]</c> + <c>[empty-band]</c>）。
	/// 反过来，构造期全不可见而之后又冒出一个可见开关时，宽度会停在 0 ⇒ 那颗开关被裁
	///（30 系「配置编辑」在 150%/200% 下的 <c>[parent-overflow] …/perfShortcuts/RButton</c>）。</item>
	/// <item>一个可见子控件都没有时，把容器整条 <c>Visible=false</c>：既符合"没有开关就不占位"，
	/// 也让取证宿主的 <c>[zero-size]</c>/<c>[empty-band]</c> 两条空容器判据不再命中。</item>
	/// </list>
	/// ⛔ 不动任何开关自己的显隐判据（那三条判据仍由各自原调用点下发），本方法只读它们的 Visible。
	/// </summary>
	private void SyncPerfShortcutsVisibility()
	{
		FlowLayoutPanel flow = _perfShortcuts;
		if (flow == null || flow.IsDisposed)
		{
			return;
		}
		int width = 0;
		foreach (Control shortcut in flow.Controls)
		{
			// 2026-10-12（本轮 · 缺陷修复）：⛔ 不能直接拿 shortcut.Visible 求和 —— Control.Visible 的
			// getter 返回的是**有效**可见性（父链上任何一个不可见 ⇒ false）。构造期窗体还没 Show、
			// 以及"容器自己刚被收起"这两种情况下，两颗开关的有效值恒为 false ⇒ 求和恒 0 ⇒ 容器恒收起，
			// 而子控件又因父容器隐藏永远不可能变成有效可见 ⇒ **自锁**（取证实测容器恒 0×H / vis=False）。
			// 改口径：容器**自己可见**时用有效 Visible（可信，"两颗都真被关掉"立刻生效、不留空隙）；
			// 容器不可见（构造期 / 刚收起）时用各调用点下发的**意图**值（见 ShortcutIntendedVisible）。
			if (flow.Visible ? shortcut.Visible : ShortcutIntendedVisible(shortcut))
			{
				width += shortcut.Width + shortcut.Margin.Horizontal;
			}
		}
		if (width > 0)
		{
			flow.Visible = true;
			flow.Width = Math.Max(width + ResponsiveLayout.LogicalToDevice(this, 2), flow.PreferredSize.Width);
		}
		else
		{
			flow.Width = 0;
			flow.Visible = false;
		}
	}

	/// <summary>
	/// 2026-10-12（本轮 · 缺陷修复）：一颗快捷开关**按意图**是否显示。
	/// ⚠️ 存在的理由：<see cref="Control.Visible" /> 是**有效**可见性（父链不可见即 false），
	/// 构造期（窗体未 Show）/ 容器自己被收起时读它恒 false ⇒ 用有效值求和必然把容器永久收起（见
	/// <see cref="SyncPerfShortcutsVisibility" />）。这里改读各调用点在下发 <c>Visible</c> 时**同时记下**的意图，
	/// 两者永不分叉（⛔ 判据本身一字未动，仍由原调用点下发）。
	/// </summary>
	private bool ShortcutIntendedVisible(Control shortcut)
	{
		if (shortcut == _gpuOcBox)
		{
			return _perfShortcutGpuOcIntent;
		}
		if (shortcut == perfConfigEditButton)
		{
			return _perfShortcutConfigEditIntent;
		}
		// 未登记意图的控件（以后若再加成员）：退回有效可见性，行为与本轮之前完全一致。
		return shortcut.Visible;
	}

	private void ApplyPerfConfigRowVisibility()
	{
		// 2026-10-09（实验开关，用户原话「c」）：**只在这里**多挂一条默认关闭的实验旁路
		// Force30Ui.Enabled —— 打开后本界面按 30 系样式渲染这一行，供用户肉眼比对。
		// ⚠️ 硬件判据 SupportsModeProfileConfig 一字未动（它同时是 ResolveModeProfileIndex
		//    决定是否下发 ProfileIndex 的判据，污染它＝行为改变）。开关变量是局部量，
		//    只喂给下面的 Visible / 高度，不进任何下发路径。
		bool flag = (Program.hw?.SupportsModeProfileConfig ?? false) || Force30Ui.Enabled;
		// 2026-10-09（实验开关验收，完整 30 系性能模式）：把「硬件判据」与两把实验开关的来源分别写清，
		// 尤其 Force30Perf（LMECHREVO_FORCE_30PERF）——它会让 SupportsModeProfileConfig 判真（＝行为改变）
		// 并把 ProfileIndex 真的下发。只写日志，不改任何状态。关闭时该字段恒 false，判定口径不变。
		bool force30Perf = MechrevoLite.Hardware.Force30Perf.Enabled;
		// 2026-10-09（实验开关验收）：只在**可见性真的发生变化**时打一行，把两个来源分别写清，
		// 供验收时从日志确认「带开关 / 不带开关」这一行确实不同；只写日志，不改任何状态。
		Logger.WriteLineIfChanged("perf-config-row-visible", $"[exp30ui] 模式×配置行 visible={flag} | hw.SupportsModeProfileConfig={Program.hw?.SupportsModeProfileConfig ?? false} | 实验开关 Force30Ui={Force30Ui.Enabled} | 实验开关 Force30Perf={force30Perf}{(force30Perf ? " (LMECHREVO_FORCE_30PERF=1 ⇒ 判据判真、切模式会真的下发 ProfileIndex)" : "")} | 高度={(flag ? _perfHeightWithConfig : _perfHeightCompact)}");
		if (perfConfigRow != null)
		{
			perfConfigRow.Visible = flag;
		}
		if (perfConfigEditButton != null)
		{
			// 2026-10-12（本轮 · 缺陷修复）：判据 flag 一字未改 —— 只是**同时记下意图**（同上）。
			_perfShortcutConfigEditIntent = flag;
			perfConfigEditButton.Visible = flag;
		}
		// 2026-10-11（本轮）：「配置编辑」的显隐变了 ⇒ 标题行那颗容器的宽度/可见性跟着重算
		//（否则会停在构造期的陈旧宽度，或反过来停在 0 而把刚显示出来的按钮裁掉）。
		SyncPerfShortcutsVisibility();
		if (panelPerformance != null && _perfHeightCompact > 0)
		{
			// 行高固定 D(26)（3 等分表格），用确定的预存高度
			panelPerformance.AutoSize = false;
			panelPerformance.Height = (flag ? _perfHeightWithConfig : _perfHeightCompact);
		}
		// 2026-10-11（用户拍板「抬高窗口最小宽度」；2026-10-12 撤销该抬高后此处只是幂等兜底）：
		// 「配置编辑」是标题行里的第三个开关，
		// 它的显隐直接改变标题行内容宽 ⇒ 这里按新的可见组合重算窗口下限（只增大、不缩小）。
		// 2026-10-11（本轮 · 磁贴行）：磁贴集合的**能力位过滤**也挂在这里 —— 它必须排在
		// RefreshDeviceCapabilities 的"能力位刚下发完"之后；换机型/换外设（插拔录音设备）后
		// 不支持的磁贴会在这里被剃掉，⛔ 不会留下一个点不动的磁贴。
		ApplyQuickTiles();
		RefreshDashboardMinimumWidth();
	}

	private void ReflowPerformanceButtons(bool silentTurbo, bool turbo, bool custom)
	{
		List<Control> list = new List<Control> { buttonSilent, buttonBalanced };
		if (silentTurbo && buttonSilentTurbo != null)
		{
			list.Add(buttonSilentTurbo);
		}
		if (turbo)
		{
			list.Add(buttonTurbo);
		}
		if (custom && buttonCustomMode != null)
		{
			list.Add(buttonCustomMode);
		}
		bool flagReflowed = ReflowSingleRow(tablePerf, list, ref _lastPerformanceLayout);
		// ⭐⭐ 2026-10-15（修「首次安装不显示『狂暴』」，用户实测）：
		// 真机诊断（[perf-row] 行）显示 —— 第一轮刷新的调用发生在**父容器（perfModeRow）还不可见**时
		// （parentVisible=False、宽度 0），而那一轮 `重排=True` ⇒ `ApplySegmentRow` **已经按 0 宽算过**
		// 等宽与末位右边距（`EqualizeSegmentRowWidths`）；等父容器可见后那一轮 `重排=False`（布局签名没变）
		// ⇒ **跳过了 ApplySegmentRow** ⇒ 于是"按钮 Visible=True 但几何是按 0 宽算的" ⇒ **实际被挤没** ❌
		// （用户看到的就是"狂暴模式一开始安装不显示"。）
		// 修法：**行第一次变为可见时强制再排一次分段布局**（幂等、只多做一次），保证几何是按真实宽度算的。
		bool flagRowVisibleNow = tablePerf.Parent?.Visible ?? false;
		// ⚠️ 先把"本次是否应用"算进变量再应用 —— 否则日志里再求值一次时状态已被上面那行更新过，
		// 会把"确实应用了"误报成 应用=False（本轮踩过）。
		bool flagApplied = flagReflowed || (flagRowVisibleNow && !_perfRowSegmentsAppliedVisible);
		if (flagApplied)
		{
			ApplySegmentRow(list);
		}
		_perfRowSegmentsAppliedVisible = flagRowVisibleNow;
		// ⭐ 2026-10-15（用户反馈「首次安装时『狂暴』不显示」）：诊断行 —— 看清"这次重排到底把哪几颗算进可见集、
		// 有没有真的重排、父容器可见性如何"。用 WriteLineIfChanged（同一内容只记一次）⇒ 稳态下不刷日志。
		Logger.WriteLineIfChanged("perf-row-reflow", $"[perf-row] 可见集={string.Join("/", list.ConvertAll((Control c) => c.Name))} 重排={flagReflowed} 应用={flagApplied} turbo={turbo} silentTurbo={silentTurbo} custom={custom} turboVisible={buttonTurbo.Visible} parent={tablePerf.Parent?.Name ?? "(null)"} parentVisible={flagRowVisibleNow}");
	}

	private void ApplySegmentRow(List<Control> buttons)
	{
		// 2026-10-09（UI 打磨③，用户原话「这种长条的能不能拆成和Ghelper一样独立的呢」）：原来是
		// `button.Margin = Padding.Empty` 把整行拼成一条连续的长条，现改为 ApplyIndependentRow ——
		// 每颗按钮各自四角圆角（Single），相邻之间留 D(6) 缝隙；行高（绝对 D(34)）与按钮高度都不变。
		RButton[] array = buttons.OfType<RButton>().ToArray();
		UiVisualStyle.ApplyIndependentRow(array);
		// 2026-10-12（用户原话「检查一下性能模式四个按钮大小是不是一样的」）：统一内边距只解决
		// "首末两颗贴齐两端而偏宽"，TableLayoutPanel 还会把取整余量补给最后一列 ⇒ 最后一颗再宽几像素。
		// 这里把那点余量挪到它的右边距上，并挂一次行宽变化钩子（窗口缩放后仍然等宽）。
		if (array.Length != 0 && array[0].Parent is TableLayoutPanel tableLayoutPanel)
		{
			_segmentRows[tableLayoutPanel] = array;
			EqualizeSegmentRowWidths(tableLayoutPanel, array);
			if (_segmentRowHooked.Add(tableLayoutPanel))
			{
				tableLayoutPanel.ClientSizeChanged += delegate
				{
					RefreshSegmentRowWidths();
				};
			}
		}
	}

	/// <summary>
	/// 2026-10-12（用户要求"几个按钮大小完全一致"）：等宽列 + 统一内边距下，TableLayoutPanel 会把
	/// 取整余量（0..n−1 设备像素）补给**最后一列** ⇒ 最后一颗按钮比同排其它按钮宽那几像素。
	/// 这里把余量从**按钮宽度**里挪到它的**右边距**上 ⇒ 同排每颗按钮逐一等宽（行的右端由那段边距占位）。
	/// ⛔ 只改 Margin：不碰列数/行高/文本/点击语义/选中态绘制，也⛔ 不动其它任何行。
	/// </summary>
	private void EqualizeSegmentRowWidths(TableLayoutPanel table, RButton[] buttons)
	{
		if (table == null || buttons == null || buttons.Length == 0)
		{
			return;
		}
		int[] columnWidths = table.GetColumnWidths();
		if (columnWidths.Length != buttons.Length)
		{
			return;
		}
		int num = Math.Max(2, ResponsiveLayout.LogicalToDevice(this, UiVisualStyle.IndependentGapLogical));
		int num2 = Math.Max(0, columnWidths[buttons.Length - 1] - columnWidths[0]);
		for (int i = 0; i < buttons.Length; i++)
		{
			int right = ((i == buttons.Length - 1) ? (num - num / 2 + num2) : (num - num / 2));
			buttons[i].Margin = new Padding(num / 2, 0, right, 0);
		}
	}

	/// <summary>行宽变化后统一重算（幂等；带重入保护）。</summary>
	private void RefreshSegmentRowWidths()
	{
		if (_equalizingSegmentRows)
		{
			return;
		}
		_equalizingSegmentRows = true;
		try
		{
			foreach (KeyValuePair<TableLayoutPanel, RButton[]> segmentRow in _segmentRows)
			{
				EqualizeSegmentRowWidths(segmentRow.Key, segmentRow.Value);
			}
		}
		finally
		{
			_equalizingSegmentRows = false;
		}
	}

	internal void AttachDashboardChrome()
	{
		if (_dashboard != null && _dashboardScroll != null && panelFooter != null)
		{
			AttachGridChildOnce(_dashboard, _dashboardScroll, 0, 0);
			AttachGridChildOnce(_dashboard, panelFooter, 0, 1);
		}
	}

	private static void AttachGridChildOnce(TableLayoutPanel host, Control child, int column, int row)
	{
		Control[] array = (from Control c in host.Controls
			where c != child && c.Name == child.Name
			select c).ToArray();
		foreach (Control control in array)
		{
			host.Controls.Remove(control);
			control.Dispose();
		}
		if (child.Parent == host)
		{
			host.SetCellPosition(child, new TableLayoutPanelCellPosition(column, row));
		}
		else
		{
			host.Controls.Add(child, column, row);
		}
	}

	private void ArrangeDashboard(bool force = false)
	{
		if (_dashboardStack == null || _arrangingDashboard || IsDisposed)
		{
			return;
		}
		_arrangingDashboard = true;
		try
		{
			string text = string.Join('|', from section in _dashboardSections
				where _enabledDashboardSections.Contains(section)
				select section.Name);
			if (!force && text == _lastStackLayout)
			{
				return;
			}
			_lastStackLayout = text;
			_dashboardStack.SuspendLayout();
			_dashboardStack.Controls.Clear();
			_dashboardStack.RowStyles.Clear();
			int rowCount = 0;
			Control[] dashboardSections = _dashboardSections;
			foreach (Control control in dashboardSections)
			{
				if (_enabledDashboardSections.Contains(control))
				{
					_dashboardStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
					_dashboardStack.Controls.Add(control, 0, rowCount++);
				}
			}
			_dashboardStack.RowCount = rowCount;
			_dashboardStack.ResumeLayout(performLayout: false);
		}
		finally
		{
			_arrangingDashboard = false;
		}
		UpdateDashboardWindowHeight();
		UpdateDashboardScroll();
	}

	private void UpdateDashboardWindowHeight()
	{
		if (_dashboardStack == null || _dashboard == null || panelFooter == null || IsDisposed || WindowState != FormWindowState.Normal)
		{
			return;
		}
		float scale = EffectiveLayoutScale;
		Rectangle rectangle = (_lastWorkingArea.IsEmpty ? Screen.FromControl(this).WorkingArea : _lastWorkingArea);
		int num = Height - ClientSize.Height;
		int val = Math.Max(Scale(300), rectangle.Height - num);
		int num2 = Math.Min(Scale(CompactDashboardLogicalClientSize.Height), val);
		string text = $"{rectangle.Height}|{num2}|{ClientSize.Width}";
		if (!(text == _lastWindowHeightSignature))
		{
			_lastWindowHeightSignature = text;
			ClientSize = new Size(ClientSize.Width, num2);
			if (_hasPresentedWindow && !_lastWorkingArea.IsEmpty)
			{
				ResponsiveLayout.PlaceBottomRight(this, _lastWorkingArea);
			}
			UpdateDashboardScroll();
		}
		int Scale(int logical)
		{
			return (int)Math.Round((float)logical * scale);
		}
	}

	private void ApplyDashboardWindowMetrics(bool force = false)
	{
		float scale = EffectiveLayoutScale;
		int num = (int)Math.Round(scale * 96f);
		if (force || _dashboardWindowDpi != num)
		{
			_dashboardWindowDpi = num;
			// 2026-10-12（用户答复③「撤销抬高窗口最小宽度」）：最小宽回到**固定的 420 逻辑像素**
			//（= 2026-10-09 归档里的 `MinimumSize = new Size(Scale(420), Scale(300))`，⛔ 不再按标题行实测下发）。
			// ⚠️ 保留上一轮的**单位口径**修正：`Form.MinimumSize` 运行期赋值按**设备像素**生效，
			//    所以 RequiredDashboardWindowWidth() 内部乘了当前 DPI 缩放（见它的说明）。
			MinimumSize = new Size(RequiredDashboardWindowWidth(), Scale(300));
			_lastWindowHeightSignature = "";
			if (WindowState == FormWindowState.Normal)
			{
				ClientSize = new Size(Scale(CompactDashboardLogicalClientSize.Width), Scale(CompactDashboardLogicalClientSize.Height));
			}
		}
		int Scale(int logical)
		{
			return (int)Math.Round((float)logical * scale);
		}
	}

	/// <summary>
	/// 2026-10-12（用户答复③「撤销抬高窗口最小宽度（让窗口能像以前一样窄）」）：
	/// **窗口**最小宽（**设备像素**）= <see cref="MinimumDashboardLogicalClientSize" />.Width 逻辑像素 ×
	/// 当前 DPI 缩放 —— 与 2026-10-09 归档里的 `MinimumSize = new Size(Scale(420), Scale(300))` **同值**。
	/// ⚠️ 保留上一轮修好的**单位口径**：<c>Form.MinimumSize</c> 在运行期赋值时按**设备像素**生效
	/// ⇒ 这里必须乘 <see cref="EffectiveLayoutScale" />（⛔ 不再当成逻辑像素下发；那是上一轮的 bug）。
	/// ⚠️ ⛔ 不再把"标题行内容宽 / 磁贴行自然宽"算进下限（那正是本轮要撤销的"抬高"）。
	/// 保留的只有一条兜底：小屏可用宽夹取（`min-width-clamped`，避免下限超过屏幕）。
	/// </summary>
	private int RequiredDashboardWindowWidth()
	{
		float num = Math.Max(1f, EffectiveLayoutScale);
		int num2 = (int)Math.Round((float)MinimumDashboardLogicalClientSize.Width * num);
		if (IsHandleCreated && !IsDisposed)
		{
			int num3 = AvailableScreenDeviceWidth();
			if (num2 > num3)
			{
				Logger.WriteLineIfChanged("min-width-clamped", $"[tiles] 最小窗口宽按屏幕可用宽夹取：需要 {num2} 设备 px → 取上限 {num3} 设备 px");
				num2 = num3;
			}
		}
		return num2;
	}

	/// <summary>本窗所在屏幕的**可用宽**（设备像素）—— 用于"最小宽不得超过屏幕"的取舍夹取。</summary>
	private int AvailableScreenDeviceWidth()
	{
		try
		{
			Rectangle area = (_lastWorkingArea.IsEmpty ? Screen.FromControl(this).WorkingArea : _lastWorkingArea);
			if (area.Width <= 0)
			{
				return int.MaxValue;
			}
			return Math.Max(320, area.Width - 16);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("AvailableScreenDeviceWidth failed: " + ex.Message);
			return int.MaxValue;
		}
	}

	/// <summary>
	/// 2026-10-11：--ui-audit / 取证宿主用的只读入口（见 <see cref="RequiredDashboardLogicalWidth" />）。
	/// </summary>
	internal int RequiredDashboardLogicalWidthForAudit()
	{
		return RequiredDashboardLogicalWidth();
	}

	/// <summary>2026-10-11：--ui-audit / 取证宿主的只读明细（用来核对"最小宽度"的算式）。</summary>
	internal string DescribeRequiredDashboardWidthForAudit()
	{
		int icon = ResponsiveLayout.LogicalToDevice(this, 22);
		int title = labelPerf?.PreferredWidth ?? -1;
		int switches = 0;
		foreach (Control shortcut in _perfShortcuts.Controls)
		{
			switches += shortcut.Visible ? (shortcut.Width + shortcut.Margin.Horizontal) : 0;
		}
		int readings = 0;
		Font monoFont = _telemetryCpuRpm?.Font ?? _telemetryCpuName?.Font;
		if (monoFont != null)
		{
			// 2026-10-16（方案 A）：拆列后是三列各自的样本，⛔ 不再是拼在一起的那一份。
			readings = TelemetryTextWidth(TelemetryPowerSample, monoFont, ContentAlignment.MiddleRight) + TelemetryTextWidth(TelemetryRpmSample, monoFont, ContentAlignment.MiddleRight) + TelemetryTextWidth(TelemetryDutySample, monoFont, ContentAlignment.MiddleRight);
		}
		readings += TelemetryTextWidth(_telemetryCpuName?.Text ?? "CPU", _telemetryCpuName?.Font ?? Font, ContentAlignment.MiddleLeft);
		readings += TelemetryTextWidth(_telemetryCpuTemp?.Text ?? "—", _telemetryCpuTemp?.Font ?? Font, ContentAlignment.MiddleRight);
		return $"icon={icon} title={title} switches={switches}(fan={_fanBoostBox?.Visible}/{_fanBoostBox?.Width},oc={_gpuOcBox?.Visible}/{_gpuOcBox?.Width},cfg={perfConfigEditButton?.Visible}/{perfConfigEditButton?.Width}) readings={readings} pad={panelPerformance?.Padding.Horizontal} computedHeader={(int)Math.Ceiling((double)(icon + title + switches + readings + (panelPerformance?.Padding.Horizontal ?? 0)) * 1.08)} tiles={DescribeQuickTileRowForAudit()} tileRow={RequiredQuickTileRowLogicalWidth()} computed={ComputeRequiredDashboardLogicalWidth()} chosen={RequiredDashboardLogicalWidth()} minWin={MinimumSize.Width}";
	}

	/// <summary>
	/// 2026-10-11（磁贴行取证）：磁贴行的实测明细 —— 格数 / 字号 / 自然行宽 / 可用宽下限 / 行宽 / 行高，
	/// 以及**逐格**「文字实测宽 / 该格可用宽」的对照（超了才打 <c>!CLIP</c>）。
	/// ⚠️ 逐格那一份是本轮"标签会不会被裁"的**唯一**判据：`available` 已扣掉 D(8)，
	/// 与 <c>UiAuditRunner</c> 对 ButtonBase 的 ±8 口径相同。
	/// </summary>
	internal string DescribeQuickTileRowForAudit()
	{
		int num = _quickTileKeys.Count + 1;
		string text = "";
		for (int i = 0; i < _quickTileButtons.Count; i++)
		{
			RButton rButton = _quickTileButtons[i];
			string text2 = rButton.Text ?? "";
			int num2 = TextRenderer.MeasureText(text2, rButton.Font).Width;
			int num3 = Math.Max(1, rButton.ClientSize.Width - ResponsiveLayout.LogicalToDevice(this, QuickTileTextPadLogical));
			text = text + text2 + ":" + num2 + "/" + num3 + ((num2 > num3) ? "!CLIP " : " ");
		}
		return $"keys={string.Join("+", _quickTileKeys)}(+add) cells={num} font={_quickTileFontSize:F1} naturalRow={_quickTileNaturalRowWidth} minRowAvail={QuickTileRowMinDeviceWidth()} row={_quickTileRow?.ClientSize.Width ?? 0} height={_quickTileRow?.Height ?? 0} needLogical={RequiredQuickTileRowLogicalWidth()} tiles=[{text.Trim()}]";
	}

	/// <summary>
	/// 2026-10-12（用户答复③「不要变宽：缩小磁贴让它一行放下」）：窗口最小宽 = **原来的水平**
	/// （<see cref="MinimumDashboardLogicalClientSize" />.Width，420 逻辑像素）。
	/// ⚠️ 标题行内容宽（<see cref="ComputeRequiredDashboardLogicalWidth" />）与磁贴行自然宽
	/// （<see cref="RequiredQuickTileRowLogicalWidth" />）仍然逐轮**实测**，但**只写进取证日志**，
	/// ⛔ 不再抬窗口下限 —— 用户明确要求窗口能像以前一样窄；窗口窄了以后读数/磁贴的富余量由
	/// 「缩小磁贴」（本轮）与"窗口可拉宽"（用户自己控制）承担。
	/// </summary>
	private int RequiredDashboardLogicalWidth()
	{
		return MinimumDashboardLogicalClientSize.Width;
	}

	/// <summary>
	/// 标题行内容宽（逻辑像素）的**实测**算式，见 <see cref="RequiredDashboardLogicalWidth" />。
	/// ⚠️ 2026-10-12 起**只用于取证日志**（⛔ 不再抬窗口下限，见 <see cref="RequiredDashboardLogicalWidth" />）；
	/// 算式本身一字未改，好让日志能继续给出"窄窗下差多少"的实测值。
	/// </summary>
	private int ComputeRequiredDashboardLogicalWidth()
	{
		if (labelPerf == null || _perfShortcuts == null)
		{
			return MinimumDashboardLogicalClientSize.Width;
		}
		int num = ResponsiveLayout.LogicalToDevice(this, 22);
		int num2 = labelPerf.PreferredWidth;
		int num3 = 0;
		foreach (Control control in _perfShortcuts.Controls)
		{
			if (control.Visible)
			{
				num3 += control.Width + control.Margin.Horizontal;
			}
		}
		int num4 = 0;
		Font font = _telemetryCpuRpm?.Font ?? _telemetryCpuName?.Font;
		if (font != null)
		{
			num4 = TelemetryTextWidth(TelemetryPowerSample, font, ContentAlignment.MiddleRight) + TelemetryTextWidth(TelemetryRpmSample, font, ContentAlignment.MiddleRight) + TelemetryTextWidth(TelemetryDutySample, font, ContentAlignment.MiddleRight);
		}
		num4 += TelemetryTextWidth(_telemetryCpuName?.Text ?? "CPU", _telemetryCpuName?.Font ?? Font, ContentAlignment.MiddleLeft);
		num4 += TelemetryTextWidth(_telemetryCpuTemp?.Text ?? "—", _telemetryCpuTemp?.Font ?? Font, ContentAlignment.MiddleRight);
		int horizontal = panelPerformance.Padding.Horizontal;
		int num5 = num + num2 + num3 + num4 + horizontal;
		return (int)Math.Ceiling((double)num5 * 1.08);
	}

	/// <summary>
	/// 2026-10-12（用户答复③）：最小宽改回**固定的 420 逻辑像素**之后，本方法不再"抬高"任何东西 ——
	/// 它只在**构造末尾/能力位下发之后**再确认一次下限已经下发（幂等；`RequiredDashboardWindowWidth()`
	/// 与 <see cref="ApplyDashboardWindowMetrics" /> 同源 ⇒ 恒有 `num &lt;= MinimumSize.Width`，直接返回）。
	/// ⚠️ 保留它的理由：`RefreshDeviceCapabilities` / 主题切换等路径都会走到这里，留着这一处
	/// 就保证了"无论谁把 MinimumSize 改小，最终都会被兜回下限"，且仍然只**变大**不缩小（不闪）。
	/// </summary>
	private void RefreshDashboardMinimumWidth()
	{
		if (IsDisposed)
		{
			return;
		}
		if (_applyingResponsiveBounds || _arrangingDashboard)
		{
			// ⚠️ 本方法会在 PerformLayout / 可见性下发 / ApplyResponsiveBounds 的中途被调到（那些路径正在
			//    改尺寸），当场改 MinimumSize 会重入布局 ⇒ 延后到消息循环再算一次（幂等，只增大）。
			if (!_minimumWidthRefreshPending)
			{
				_minimumWidthRefreshPending = true;
				BeginInvoke(delegate
				{
					_minimumWidthRefreshPending = false;
					RefreshDashboardMinimumWidth();
				});
			}
			return;
		}
		int num = RequiredDashboardWindowWidth();
		if (num <= MinimumSize.Width)
		{
			return;
		}
		MinimumSize = new Size(num, MinimumSize.Height);
		Logger.WriteLine($"dashboard minimum width raised to {num}px (floor {MinimumDashboardLogicalClientSize.Width} logical px)");
	}

	private void ConfigureResponsiveWindow()
	{
		AutoSize = false;
		AutoSizeMode = AutoSizeMode.GrowOnly;
		AutoScroll = false;
		AutoScrollMargin = Size.Empty;
		ApplyDashboardWindowMetrics(force: true);
		ApplyResponsiveBounds();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (_dashboard != null && !IsDisposed && WindowState != _lastWindowState)
		{
			_lastWindowState = WindowState;
			_dashboard.PerformLayout();
			UpdateDashboardScroll();
			Invalidate(invalidateChildren: true);
		}
	}

	public override void ApplyResponsiveBounds(Rectangle? workingArea = null)
	{
		if (_applyingResponsiveBounds || IsDisposed)
		{
			return;
		}
		_applyingResponsiveBounds = true;
		try
		{
			_lastWorkingArea = workingArea ?? (IsHandleCreated ? Screen.FromControl(this).WorkingArea : Rectangle.Empty);
			ApplyDashboardWindowMetrics();
			ArrangeDashboard();
			PerformLayout();
			base.ApplyResponsiveBounds(workingArea);
			ArrangeDashboard();
			PerformLayout();
			if (WindowState == FormWindowState.Normal)
			{
				ResponsiveLayout.PlaceBottomRight(this, workingArea);
			}
			UpdateDashboardWindowHeight();
			NormalizeScreenCardMetrics();
		}
		finally
		{
			_applyingResponsiveBounds = false;
		}
	}

	private void NormalizeScreenCardMetrics()
	{
		Control control = Controls.Find("panelBrightness", searchAllChildren: true).FirstOrDefault();
		if (control != null)
		{
			control.Padding = new Padding(D(12), D(2), D(12), 0);
		}
		Control control2 = Controls.Find("headIcon_Display", searchAllChildren: true).FirstOrDefault();
		if (control2 != null && control2.Parent is TableLayoutPanel tableLayoutPanel && tableLayoutPanel.ColumnStyles.Count > 0)
		{
			tableLayoutPanel.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, D(26));
		}
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
	}

	private void ButtonAmdOled_Click(object? sender, EventArgs e)
	{
		AmdDisplay.RunAdrenaline();
		activateCheck = true;
	}

	private void LabelBattery_Click(object? sender, EventArgs e)
	{
		HardwareControl.chargeWatt = !HardwareControl.chargeWatt;
		RefreshSensors(force: true);
	}

	private void ButtonEnergySaver_Click(object? sender, EventArgs e)
	{
		KeyboardHook.KeyKeyPress(Keys.LWin, Keys.A);
	}

	private void LabelBacklight_Click(object? sender, EventArgs e)
	{
		if (AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled())
		{
			DynamicLightingHelper.OpenSettings();
		}
	}

	private void ButtonFHD_Click(object? sender, EventArgs e)
	{
		ScreenControl.ToogleFHD();
	}

	private void ButtonHDRControl_Click(object? sender, EventArgs e)
	{
		ScreenControl.ToogleHDRControl();
	}

	private void SliderBattery_ValueChanged(object? sender, EventArgs e)
	{
		VisualiseBatteryTitle(sliderBattery.Value);
	}

	private void SliderBattery_KeyUp(object? sender, KeyEventArgs e)
	{
		BatteryControl.CaptureChargeLimitGesture();
		batteryTimer.Stop();
		batteryTimer.Start();
	}

	private void SliderBattery_MouseUp(object? sender, MouseEventArgs e)
	{
		BatteryControl.CaptureChargeLimitGesture();
		batteryTimer.Stop();
		batteryTimer.Start();
	}

	private void LabelCharge_Click(object? sender, EventArgs e)
	{
		BatteryControl.BatteryReport();
	}

	public void VisualiseAmdOled(bool status = false)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				VisualiseAmdOled(status);
			});
		}
		else
		{
			buttonAmdOled.Visible = status;
		}
	}

	private void ButtonOverlay_Click(object? sender, EventArgs e)
	{
		ToggleOverlay();
	}

	private void SettingsForm_Focused(object? sender, EventArgs e)
	{
		if (activateCheck)
		{
			buttonAmdOled.Visible = AmdDisplay.IsOledPowerOptimization();
			activateCheck = false;
		}
	}

	private void SettingsForm_LostFocus(object? sender, EventArgs e)
	{
		lastLostFocus = DateTimeOffset.Now.ToUnixTimeMilliseconds();
	}

	private void ButtonBatteryFull_Click(object? sender, EventArgs e)
	{
		BatteryControl.ToggleBatteryLimitFull();
	}

	private void ButtonBatteryFull_MouseLeave(object? sender, EventArgs e)
	{
		batteryFullMouseOver = false;
		RefreshSensors(force: true);
	}

	private void ButtonBatteryFull_MouseEnter(object? sender, EventArgs e)
	{
		batteryFullMouseOver = true;
		labelCharge.Text = Strings.BatteryLimitFull;
	}

	private void PanelBattery_MouseEnter(object? sender, EventArgs e)
	{
		batteryMouseOver = true;
		ShowBatteryWear();
	}

	private void PanelBattery_MouseLeave(object? sender, EventArgs e)
	{
		batteryMouseOver = false;
		RefreshSensors(force: true);
	}

	private void ShowBatteryWear()
	{
		if (lastBatteryRefresh == 0L || Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastBatteryRefresh) > 900000)
		{
			lastBatteryRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
		}
		if (HardwareControl.batteryHealth != -1m)
		{
			labelCharge.Text = Strings.BatteryHealth + ": " + Math.Round(HardwareControl.batteryHealth, 1) + "%";
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
	/// ⭐ **只读**同步系统亮度到滑块/数值标签 —— ⛔ 不写硬件（绝不调 <c>ScreenBrightness.Set</c>）✅。
	/// <para>⭐ 触发时机：
	/// ① 启动 / 首次打开设置页（`Shown`）；② 页面重新打开（`VisibleChanged` → Visible）；
	/// ③ 显示器变化（`OnDisplaySettingsChanged`）；④ 设置页可见时每 <see cref="BrightnessSystemPollMs" /> 毫秒一次低频校验。</para>
	/// <para>⭐ 读取来源：WMI <c>root\wmi:WmiMonitorBrightness.CurrentBrightness</c>（首选）
	/// → Win32 <c>dxva2.GetMonitorBrightness</c>（备选）→ GCU 上报字段（旧路径兼容）。</para>
	/// <para>⚠️ 用户正在拖滑块 / 刚拖过 3 秒内 / 有写入在飞 ⇒ **跳过**本次回读（⛔ 不覆盖用户刚设的值）；
	/// 读不到 ⇒ 滑块置灰 + 数值位标注「本机不支持软件调亮度」（⛔ 不显示假值）✅。</para>
	/// <para>⚠️ 日志去重：同一值/同一失败原因只落一行（`Logger.TryMarkChanged`），⛔ 不刷屏 ✅。</para>
	/// </summary>
	/// <param name="force">⭐ true = 打开/显示变化等事件触发（忽略 5 s 节流，但仍避开用户拖动窗口）。</param>
	private void SyncBrightnessFromSystemReadOnly(bool force)
	{
		if (Program.UiAuditMode || IsDisposed || _brightnessSlider == null || _brightnessValueLabel == null)
		{
			return;
		}
		// ⭐⭐ 2026-10-19（机主：外部改亮度 LM 不同步 / 调节防回环）：
		//   ⭐ 用户正在拖 / 刚写入 **2 秒内** / 还有写入在飞 ⇒ **跳过本次同步**（以用户输入为准）；
		//      ⛔ 不写硬件、⛔ 不覆盖用户刚设的值 ✅
		if (_brightnessCommitQueue.HasPendingOrRunning || (DateTime.UtcNow - _lastBrightnessUi).TotalSeconds < 2.0)
		{
			return;
		}
		long now = Environment.TickCount64;
		bool firstSync = _lastBrightnessSystemSyncTick == 0;
		if (!force)
		{
			// ⭐ 复用**既有** `_displayStatusTimer`（3 s）的 tick，只在本页可见时执行；
			//   ⭐ 读 WMI 有开销 ⇒ 节流到 ~5 s 一次（⛔ 不新增任何定时器）✅
			if (!Visible)
			{
				return;
			}
			if (now - _lastBrightnessSystemSyncTick < BrightnessSystemPollMs)
			{
				return;
			}
		}
		_lastBrightnessSystemSyncTick = now;
		if (Interlocked.CompareExchange(ref _brightnessReadInFlight, 1, 0) != 0)
		{
			return;
		}
		// ⭐⭐ 2026-10-19（机主：回读校验不要阻塞 UI）：读 WMI 放**后台线程**，结果再回 UI 线程；
		//   ⛔ 不用任何新定时器 —— 触发源仍是既有 tick / 打开页 / 显示器变化 ✅
		Task.Run(() =>
		{
			bool ok;
			int value;
			string source;
			string reason;
			try
			{
				ok = TryReadSystemBrightness(out value, out source, out reason);
			}
			catch (Exception ex)
			{
				ok = false;
				value = -1;
				source = string.Empty;
				reason = "读取线程异常：" + ex.GetType().Name + " " + ex.Message;
			}
			bool posted = false;
			try
			{
				if (!IsDisposed && IsHandleCreated)
				{
					BeginInvoke((Action)(() =>
					{
						try
						{
							ApplyBrightnessReadResult(ok, value, source, reason, firstSync);
						}
						finally
						{
							Interlocked.Exchange(ref _brightnessReadInFlight, 0);
						}
					}));
					posted = true;
				}
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("[brightness] 回读结果回主线程失败（已吞掉，不阻塞 UI）：" + ex2.Message);
			}
			finally
			{
				if (!posted)
				{
					Interlocked.Exchange(ref _brightnessReadInFlight, 0);
				}
			}
		});
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：外部改亮度 LM 不同步）：⭐ 把**后台只读回读**的结果应用到 UI（主线程）✅。
	/// <para>⚠️ 应用前再查一次"用户正在拖 / 刚写入 2 秒内" ⇒ 有就丢弃本次结果（⛔ 不与用户输入打架）。</para>
	/// </summary>
	private void ApplyBrightnessReadResult(bool ok, int value, string source, string reason, bool firstSync)
	{
		if (IsDisposed || _brightnessSlider == null || _brightnessValueLabel == null)
		{
			return;
		}
		if (_brightnessCommitQueue.HasPendingOrRunning || (DateTime.UtcNow - _lastBrightnessUi).TotalSeconds < 2.0)
		{
			return;
		}
		if (!ok)
		{
			_brightnessUnsupportedShown = true;
			ApplyBrightnessVisualState();
			BrightnessDiagnosticsState.NoteRead(ok: false, source, reason);
			string failMsg = "[brightness] ⚠️ 读不到系统亮度（原因=" + reason + "）";
			if (Logger.TryMarkChanged("brightness-read-fail", failMsg))
			{
				Logger.WriteError(failMsg);
			}
			return;
		}
		BrightnessDiagnosticsState.NoteRead(ok: true, source, string.Empty);
		bool wasUnsupported = _brightnessUnsupportedShown || !_brightnessSlider.Enabled;
		int previousUi = _brightnessSlider.Value;
		_brightnessUnsupportedShown = false;
		ApplyBrightnessVisualState();
		bool changed = false;
		bool prevSyncing = _syncingDisplay;
		_syncingDisplay = true;
		try
		{
			if (_brightnessSlider.Value != value)
			{
				_brightnessSlider.Value = value;
				changed = true;
			}
			string text = value + "%";
			if (_brightnessValueLabel.Text != text)
			{
				_brightnessValueLabel.Text = text;
				changed = true;
			}
		}
		finally
		{
			_syncingDisplay = prevSyncing;
		}
		if (!(changed || wasUnsupported || firstSync))
		{
			return;
		}
		string okMsg;
		if (!firstSync && !wasUnsupported && changed && previousUi != value)
		{
			// ⭐ 机主要求的"外部变化"日志（挂既有刷新 tick 的低频校验发现的不一致）
			okMsg = "[brightness] 外部变化同步 ⇒ 系统=" + value + "% → UI " + value + "%（未写入，来源=既有刷新 tick）";
		}
		else
		{
			okMsg = "[brightness] 只读同步 ⇒ 系统值=" + value + "%（来源=" + source + "）· UI 已更新（未写硬件）";
		}
		if (Logger.TryMarkChanged("brightness-read-sync", okMsg))
		{
			Logger.WriteError(okMsg);
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：亮度调节有延迟）：⭐ **拖动期节流写**（~120ms 合并一次；首个输入立即写）。
	/// <para>⚠️ 仍只在用户亲手拖动/点按时调用（`ValueChanged` 且 `_syncingDisplay == false`）；
	/// 回读同步改 UI 时 `_syncingDisplay = true` ⇒ ⛔ 不会走到这里、⛔ 不会写硬件 ✅。</para>
	/// </summary>
	private void RequestBrightnessWrite(int value)
	{
		Interlocked.Increment(ref _brightnessMergedSubmits);
		// ⭐ 先 Submit（记录最新值），再按节流窗口决定是否立即 Flush（= 真写）。
		_brightnessCommitQueue.Submit(value);
		long now = Environment.TickCount64;
		if (_brightnessLastThrottleFlushTick == 0 || now - _brightnessLastThrottleFlushTick >= 120)
		{
			_brightnessLastThrottleFlushTick = now;
			_brightnessCommitQueue.Flush();
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：亮度调节有延迟 / 回读校验不要阻塞 UI）：
	/// ⭐ 实际写亮度（后台线程）—— 写后**异步回读**并落一行机主要求的日志 ✅。
	/// <para>日志：`[brightness] 写（节流合并 n 次）请求=… ⇒ 实际=…`（默认级别可见）✅</para>
	/// </summary>
	private Task WriteBrightnessValueAsync(int requested, CancellationToken token)
	{
		return Task.Run(() =>
		{
			token.ThrowIfCancellationRequested();
			int merged = Interlocked.Exchange(ref _brightnessMergedSubmits, 0);
			if (merged <= 0)
			{
				merged = 1;
			}
			bool ok = ScreenBrightness.TrySet(requested);
			int actual = -1;
			try
			{
				// ⭐ 写后回读（后台线程，⛔ 不阻塞 UI）；WMI 生效可能有延迟 ⇒ 对不上就再等 60ms 读一次
				if (ScreenBrightness.TryGet(out int readNow))
				{
					actual = readNow;
				}
				else
				{
					Thread.Sleep(60);
					if (ScreenBrightness.TryGet(out int retry))
					{
						actual = retry;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[brightness] 写后回读异常（已吞掉，不阻塞 UI）：" + ex.Message);
			}
			BrightnessDiagnosticsState.NoteWrite(ok, requested, actual);
			string actualText = (actual >= 0) ? (actual + "%") : "读不到";
			Logger.WriteError("[brightness] 写（节流合并 " + merged + " 次）请求=" + requested + "% ⇒ 实际=" + actualText
				+ " · 写成功=" + ok + "（回读在后台线程，⛔ 不阻塞 UI）");
		}, token);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
	/// ⭐ **读取系统亮度**（只读）—— 返回 false 时 <paramref name="reason" /> 给出可读原因 ✅。
	/// <para>顺序：① WMI <c>WmiMonitorBrightness</c>（首选）→ ② dxva2 <c>GetMonitorBrightness</c>（备选）
	/// → ③ GCU 上报字段（仅 ≥0 的旧路径兼容；无 GCU 机型恒 -1 ⇒ 不会走到）✅</para>
	/// </summary>
	private static bool TryReadSystemBrightness(out int percent, out string source, out string reason)
	{
		percent = -1;
		source = string.Empty;
		reason = string.Empty;
		// ① WMI WmiMonitorBrightness（笔记本内屏通用）
		try
		{
			if (ScreenBrightness.TryGet(out int wmi) && wmi >= 0 && wmi <= 100)
			{
				percent = wmi;
				source = "WMI WmiMonitorBrightness";
				return true;
			}
			reason = "WMI WmiMonitorBrightness 无有效实例";
		}
		catch (Exception ex)
		{
			reason = "WMI 异常：" + ex.GetType().Name + " " + ex.Message;
		}
		// ② Win32 dxva2 GetMonitorBrightness（只读备选）
		if (MonitorBrightness.TryGet(out int dxPercent, out string dxReason))
		{
			percent = dxPercent;
			source = "Win32 dxva2 GetMonitorBrightness";
			return true;
		}
		reason = (reason.Length == 0) ? dxReason : (reason + "；" + dxReason);
		// ③ 旧路径兼容：GCU 上报字段（无 GCU 机型恒 -1）
		MechrevoHw hw = Program.hw;
		if (hw != null && hw.ScreenBrightness >= 0 && hw.ScreenBrightness <= 100)
		{
			percent = hw.ScreenBrightness;
			source = "GCU 上报 ScreenBrightness";
			return true;
		}
		return false;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：读不到系统亮度时滑块置灰/隐藏并标注）：
	/// ⭐ 按 <see cref="_brightnessUnsupportedShown" /> 切换亮度行 UI —— ⛔ 只改控件显示，不写硬件、不改闸门判据 ✅。
	/// <para>⚠️ 标注「本机不支持软件调亮度」比数值列宽 ⇒ 只在**闸门放行（行可见）**时隐藏滑块、
	/// 让标注跨「滑块 + 数值」两列；闸门说隐藏时保持原样 ✅。</para>
	/// </summary>
	private void ApplyBrightnessVisualState()
	{
		if (_brightnessSlider == null || _brightnessValueLabel == null)
		{
			return;
		}
		if (_brightnessUnsupportedShown)
		{
			_brightnessSlider.Enabled = false;
			if (_brightnessValueLabel.Text != Strings.BrightnessUnsupported)
			{
				_brightnessValueLabel.Text = Strings.BrightnessUnsupported;
			}
			_brightnessValueLabel.TextAlign = ContentAlignment.MiddleLeft;
			if (_brightnessRowVisible && _screenRowsLayout != null)
			{
				_brightnessSlider.Visible = false;
				// ⚠️ 顺序：先移到第 1 列，再设跨 2 列 —— 反过来的话 (2,2)+span2 会越界（3 列表格）。
				_screenRowsLayout.SetCellPosition(_brightnessValueLabel, new TableLayoutPanelCellPosition(1, 2));
				_screenRowsLayout.SetColumnSpan(_brightnessValueLabel, 2);
			}
			return;
		}
		// 可读：恢复默认布局（滑块 (1,2) · 数值 (2,2)）
		if (_screenRowsLayout != null)
		{
			_screenRowsLayout.SetColumnSpan(_brightnessValueLabel, 1);
			_screenRowsLayout.SetCellPosition(_brightnessValueLabel, new TableLayoutPanelCellPosition(2, 2));
		}
		_brightnessValueLabel.TextAlign = ContentAlignment.MiddleRight;
		_brightnessSlider.Enabled = true;
		if (_brightnessRowVisible)
		{
			_brightnessSlider.Visible = true;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：界面刷新计时器关闭 UI 后应停止）：⭐ 可见性启停日志（去重，低调）✅。
	/// <para>⚠️ 只记 `_displayStatusTimer` / `_quickSwitchStatusTimer` 两个**界面刷新** tick；
	/// ⛔ 不涉及常驻传感器泵、风扇闭环、灯光自动关闭、强冷轮询等后台定时器（它们不受可见性影响）✅</para>
	/// </summary>
	private static void LogUiRefreshTimerLifecycle(bool visible)
	{
		string msg = "[ui] 刷新计时器随可见性启停 ⇒ Visible=" + (visible ? "true" : "false");
		if (Logger.TryMarkChanged("ui-refresh-timer-lifecycle", msg))
		{
			Logger.WriteError(msg);
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主批准：设置窗布局性能优化）：构建期**布局耗时/次数**统计（含嵌套；仅首次构建开启）✅。
	/// </summary>
	protected override void OnLayout(LayoutEventArgs levent)
	{
		if (!_uiBuildProfiling)
		{
			base.OnLayout(levent);
			return;
		}
		long layoutStart = Stopwatch.GetTimestamp();
		try
		{
			base.OnLayout(levent);
		}
		finally
		{
			_uiBuildLayoutTicks += Stopwatch.GetTimestamp() - layoutStart;
			_uiBuildLayoutCount++;
		}
	}

	/// <summary>⭐ 统计整棵控件树数量（仅构建日志用；⛔ 不改布局）✅。</summary>
	private static int CountAllControls(Control root)
	{
		int count = root.Controls.Count;
		foreach (Control child in root.Controls)
		{
			count += CountAllControls(child);
		}
		return count;
	}
	private void SettingsForm_VisibleChanged(object? sender, EventArgs e)
	{
		if (!Program.UiAuditMode)
		{
			// _sensorTimer 常驻，不随可见性启停（见构造函数注释）
			if (!IsDisposed)
			{
				_displayStatusTimer.Enabled = Visible;
				// ⭐⭐ 2026-10-19（机主：界面刷新在关闭 UI 后应停止）：
				//   ⭐ `_quickSwitchStatusTimer` 改成与 `_displayStatusTimer` **同款**随可见性启停；
				//      ⛔ 不影响常驻传感器泵 / 风扇闭环 / 灯光自动关闭 / 强冷轮询等后台独立定时器 ✅
				_quickSwitchStatusTimer.Enabled = Visible;
				LogUiRefreshTimerLifecycle(Visible);
			}
			if (Visible)
			{
				// ⭐⭐ 2026-10-19（机主：启动时屏幕亮度滑块没有读取一次系统真实状态）：
				//   ⭐ **重新打开设置页**（ShowAll → Visible=true）⇒ 立刻只读同步一次系统亮度；
				//      ⛔ 不写硬件；值没变时只更新 UI 不刷日志（去重在同步方法内）✅
				SyncBrightnessFromSystemReadOnly(force: true);
				// ⭐⭐ 2026-10-19（机主：宝龙达 Logo 开关跑到最右 —— 第 3 次修复）：
				//   ⭐ 窗口显示后**立即排队**一次灯光行重排（BeginInvoke 落在本次布局完成之后 ⇒ 键盘行已有真实像素列宽）；
				//      覆盖"建行时 hw 还没就绪 / 面板刚建好还没布局"的时序；幂等、仅宝龙达生效 ✅
				if (IsHandleCreated)
				{
					BeginInvoke(() =>
					{
						if (!IsDisposed)
						{
							ApplyBaolongdaLightRowLayout(Program.hw);
						}
					});
				}
				if (_themeModePanel != null)
				{
					UiVisualStyle.ApplyWindow(this);
					RefreshThemeButtons();
					RefreshBatteryModeButtons();
				}
				// ⭐⭐ 2026-10-19（机主：外设电量不是打开设置窗的功能）：
				//   ⭐ 原来窗口一显示就 `Task.Run(RefreshPeripheralsBattery)`（HID/外设电量查询）；
				//      现在改为**显示后延迟后台**做：⛔ 不阻塞打开、⛔ 不在构建路径 ✅
				_ = Task.Run(async () =>
				{
					try
					{
						await Task.Delay(2500).ConfigureAwait(continueOnCapturedContext: false);
						if (!IsDisposed && Visible)
						{
							RefreshPeripheralsBattery();
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine("Peripherals battery refresh failed: " + ex.Message);
					}
				});
				updateControl.CheckForUpdates();
			}
		}
	}

	private void RefreshPeripheralsBattery()
	{
		PeripheralsProvider.RefreshBatteryForAllDevices(force: true);
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 536)
		{
			try
			{
				HandlePowerBroadcast(ref m);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Power broadcast handling failed: " + ex.Message);
			}
		}
		if (m.Msg == Program.WM_TASKBARCREATED)
		{
			// 2026-10-09（批次3 #10）：托盘改懒加载 ⇒ 资源管理器重启时"需要就建、已建就重新 Visible"归 Program 管。
			Program.OnTaskbarCreated();
		}
		try
		{
			base.WndProc(ref m);
		}
		catch (Exception)
		{
		}
	}

	private void HandlePowerBroadcast(ref Message m)
	{
		if (m.WParam == 4)
		{
			Logger.WriteLine("System Suspend");
			// 2026-10-11（修「唤醒后屏幕反复被关闭」）：睡眠前清理息屏状态（不发 SC_MONITORPOWER、还原临时 sleep guard）。
			VirtualDisplayBlank.AbortOnSuspend();
			m.Result = 1;
		}
		if (m.WParam == 18)
		{
			Logger.WriteLine("System Resume");
			BatteryControl.AutoBattery();
			m.Result = 1;
		}
		if (m.WParam != 32787)
		{
			return;
		}
		NativeMethods.POWERBROADCAST_SETTING pOWERBROADCAST_SETTING = (NativeMethods.POWERBROADCAST_SETTING)m.GetLParam(typeof(NativeMethods.POWERBROADCAST_SETTING));
		if (pOWERBROADCAST_SETTING.PowerSetting == NativeMethods.PowerSettingGuid.LIDSWITCH_STATE_CHANGE)
		{
			switch (pOWERBROADCAST_SETTING.Data)
			{
			case 0:
				Logger.WriteLine("Lid Closed");
				BatteryControl.AutoBattery();
				InputDispatcher.lidClose = true;
				Aura.ApplyBrightness(0, "Lid");
				break;
			case 1:
				Logger.WriteLine("Lid Open");
				InputDispatcher.InitFNLock();
				InputDispatcher.lidClose = false;
				Aura.ApplyBrightness(InputDispatcher.GetBacklight(), "Lid");
				break;
			}
		}
		else if (pOWERBROADCAST_SETTING.PowerSetting == NativeMethods.PowerSettingGuid.EnergySaverStatus)
		{
			Logger.WriteLine("Battery Saver: " + pOWERBROADCAST_SETTING.Data);
			buttonEnergySaver.Visible = pOWERBROADCAST_SETTING.Data != 0;
		}
		else
		{
			// 2026-10-11（重构）：息屏已改虚拟显示器方案（VirtualDisplayBlank），不再依赖系统回报确认；
			// 旧的 SC_MONITORPOWER 确认机制已随 ScreenBlankController 一并删除。
			if (pOWERBROADCAST_SETTING.PowerSetting == NativeMethods.PowerSettingGuid.ConsoleDisplayState)
			{
				// 息屏已改虚拟显示器方案，不再需要系统回报确认显示器开关状态。
			}
			switch (pOWERBROADCAST_SETTING.Data)
			{
			case 0:
				Logger.WriteLine("Monitor Power Off");
				Aura.SleepBrightness();
				Program.hardwareOverlay?.SuspendForDisplayOff();
				break;
			case 1:
				Logger.WriteLine("Monitor Power On");
				if (!Program.SetAutoModes(powerChanged: false, init: false, wakeup: true))
				{
					BatteryControl.AutoBattery();
				}
				Program.hardwareOverlay?.ResumeForDisplayOn();
				break;
			case 2:
				Logger.WriteLine("Monitor Dimmed");
				break;
			}
		}
		m.Result = 1;
	}

	public void SetContextMenu()
	{
		int num = Program.hw?.OperatingMode ?? (-1);
		bool isSilentTurboActive = MechrevoService.IsSilentTurboActive;
		foreach (ToolStripItem item2 in contextMenuStrip.Items.Cast<ToolStripItem>().ToList())
		{
			if (item2 is ToolStripMenuItem toolStripMenuItem)
			{
				toolStripMenuItem.Dispose();
			}
		}
		contextMenuStrip.Items.Clear();
		contextMenuStrip.ShowCheckMargin = true;
		contextMenuStrip.ImageScalingSize = new Size(16, 16);
		contextMenuStrip.ShowImageMargin = false;
		Padding padding = new Padding(5, 5, 5, 5);
		menuEco = (menuStandard = (menuUltimate = null));
		MechrevoHw trayHw = Program.hw;
		MechrevoDeviceCapabilities mechrevoDeviceCapabilities = trayHw?.Capabilities ?? _deviceCapabilities;
		bool trayAudit = Program.UiAuditMode;
		AddDisabledTitle(Strings.PerformanceMode);
		// 2026-10-09（30 系「模式 × 配置」失败可见化）：托盘右键同样是**用户主动切模式**的入口，
		// 标一次「用户主动」，SwitchMode 里那次 ProfileIndex 回读判定才会把结论交给 UI 弹提示。
		Program.service?.MarkUserModeSwitch();
		AddAsyncAction(Strings.SilentMode, num == 0, async () =>
		{
			MechrevoService.ModeSwitchResult switchResult = (Program.service == null) ? MechrevoService.ModeSwitchResult.Failed : await Program.service.SwitchMode(2);
			if (switchResult == MechrevoService.ModeSwitchResult.Ok)
			{
				// 2026-10-06：托盘切模式原先只走 MechrevoService.SwitchMode()，不通知 PerfModeService，
				// 于是 ActiveModeId 滞后（自定义模式窗口的下拉要等下一次点主界面按钮才纠正）。补上同步。
				NotifyPerfModeState(2, null);
			}
			else if (switchResult == MechrevoService.ModeSwitchResult.Failed)
			{
				int refreshedMode = (Program.service == null) ? num : await Program.service.RefreshCurrentModeAsync(num);
				if (refreshedMode != num)
				{
					ToastForm.ShowFailure(Strings.PerfSwitchFailed);
				}
				ShowMode(MechrevoService.ToVisualMode(refreshedMode));
				SetContextMenu();
			}
		});
		AddAsyncAction(Strings.BalancedMode, num == 1, async () =>
		{
			MechrevoService.ModeSwitchResult switchResult = (Program.service == null) ? MechrevoService.ModeSwitchResult.Failed : await Program.service.SwitchMode(0);
			if (switchResult == MechrevoService.ModeSwitchResult.Ok)
			{
				// 2026-10-06：同上，托盘切平衡模式也要同步 PerfModeService（视觉 0 = 平衡）。
				NotifyPerfModeState(0, null);
			}
			else if (switchResult == MechrevoService.ModeSwitchResult.Failed)
			{
				int refreshedMode = (Program.service == null) ? num : await Program.service.RefreshCurrentModeAsync(num);
				if (refreshedMode != num)
				{
					ToastForm.ShowFailure(Strings.PerfSwitchFailed);
				}
				ShowMode(MechrevoService.ToVisualMode(refreshedMode));
				SetContextMenu();
			}
		});
if (TrayShow(mechrevoDeviceCapabilities.SilentTurboAvailability == FeatureAvailability.Supported))
		{
			AddAsyncAction(Strings.SilentTurbo, (num == 2) & isSilentTurboActive, async () =>
			{
				MechrevoService.ModeSwitchResult switchResult = (Program.service == null) ? MechrevoService.ModeSwitchResult.Failed : await Program.service.SwitchMode(1);
				if (switchResult == MechrevoService.ModeSwitchResult.Ok)
				{
					await Program.service.SwitchTurboSubMode(silent: true);
					// 2026-10-06：静音狂暴 = 视觉 1 + silentTurbo 子模式，同样要同步模式状态。
					NotifyPerfModeState(1, true);
					ShowMode(1);
					SetContextMenu();
				}
				else if (switchResult == MechrevoService.ModeSwitchResult.Failed)
				{
					int refreshedMode = (Program.service == null) ? num : await Program.service.RefreshCurrentModeAsync(num);
					if (refreshedMode != num)
					{
						ToastForm.ShowFailure(Strings.PerfSwitchFailed);
					}
					ShowMode(MechrevoService.ToVisualMode(refreshedMode));
					SetContextMenu();
				}
			});
}
		if (TrayShow(mechrevoDeviceCapabilities.TurboMode || (Program.hw?.UsesBaolongda ?? false)
			|| ((Program.hw != null) && !Program.hw.IsConnected && MechrevoHw.IsGcuServiceInstalled && !Program.hw.UsesBaolongda)))
		{
			AddAsyncAction(mechrevoDeviceCapabilities.SilentTurboAvailability == FeatureAvailability.Supported ? Strings.TurboHighEnergy : Strings.Turbo, num == 2 && !isSilentTurboActive, async () =>
			{
				MechrevoService.ModeSwitchResult switchResult = (Program.service == null) ? MechrevoService.ModeSwitchResult.Failed : await Program.service.SwitchMode(1);
				if (switchResult == MechrevoService.ModeSwitchResult.Ok)
				{
					if (trayHw?.Capabilities.TurboSubMode ?? false)
					{
						await Program.service.SwitchTurboSubMode(silent: false);
					}
					// 2026-10-06：高能狂暴 = 视觉 1 + 非静音子模式，同样要同步模式状态。
					NotifyPerfModeState(1, false);
					ShowMode(1);
					SetContextMenu();
				}
				else if (switchResult == MechrevoService.ModeSwitchResult.Failed)
				{
					int refreshedMode = (Program.service == null) ? num : await Program.service.RefreshCurrentModeAsync(num);
					if (refreshedMode != num)
					{
						ToastForm.ShowFailure(Strings.PerfSwitchFailed);
					}
					ShowMode(MechrevoService.ToVisualMode(refreshedMode));
					SetContextMenu();
				}
			});
}
		if (TrayShow(mechrevoDeviceCapabilities.FanBoost || (trayHw?.SupportsFanBoost ?? false)))
		{
			AddAsyncAction(Strings.FanBoostStrong, trayHw?.FanBoost ?? false, async () =>
			{
				bool enable = !(trayHw?.FanBoost ?? false);
				if (Program.service != null)
				{
					await Program.service.SwitchFanBoost(enable);
				}
			});
		}
		if (TrayShow(mechrevoDeviceCapabilities.CpuPerformanceTuning || mechrevoDeviceCapabilities.FanSettings || (trayHw?.HasAnyCustomRange ?? false)))
		{
			// 2026-10-15（用户反馈 · 原话「托盘菜单的自定义模式有跟进最新的自定义窗口吗,它好像数量和自定义模式的
			// 实际配置数量对不上 自定义名称也没显示」）：这一段原先**写死 4 项**（`for (num3 = 0; num3 < 4; num3++)`
			// ＋ 只用 `Strings.CustomProfileN` 拼「自定义 N」）⇒ 既不跟随自定义窗口里**真实存在**的自定义模式、
			// 不显示用户起的名字，勾选还按**固件槽位**判。
			// 现改为**枚举真实的自定义模式**（`PerfModeService.Modes` 里 `IsCustom` 的那些，1~12 个，
			// 见 PerfModeCollection.MaxCustomModes）：
			//   · 标签 = 用户起的名字（没起名才退回「自定义 N」，N = id 里的序号）；
			//   · 勾选 = `DisplayedModeId` 命中该档（比按固件槽位判准）；
			//   · 点击 = **与自定义窗口 / 主界面【自定义】按钮同一条路**：`PerfModeService.ActivateAsync(id, "tray")`
			//     —— 按该档**已保存的值**全量下发（含风扇曲线 / GPU 设置 / 电源计划），而不是只切固件槽位。
			//     ⚠️ 电源计划不必再单独调 `WinPowerPlan.ApplyProfile`：新引擎已接管
			//        （PerfModeService 里设了 `WinPowerPlan.ApplyProfileOverride`，且 ApplyPowerPlan 是规划器的一步）。
			//     ⚠️ 判定只认「硬失败」`AnyFailed`：回读通道不可用时引擎给的是"已下发、暂时确认不了"（Sent/Stale），
			//        那不是失败，不该弹红条（与 PerfLazyVerifier 的既定口径一致）。
			// ⚠️ 兜底：服务没起来 / 一个自定义模式都枚举不到时，**逐字保留**原来的写死 4 项（行为与改动前一致）。
			int num2 = trayHw?.CustomProfileIndex ?? (-1);
			PerfModeService trayPerf = PerfModeService.Instance;
			List<PerfModeDefinition> trayCustoms = ((trayPerf == null) ? null : trayPerf.Modes.Where((PerfModeDefinition m) => m.IsCustom).ToList());
			if (trayCustoms != null && trayCustoms.Count > 0)
			{
				string TrayCustomLabel(PerfModeDefinition mode)
				{
					if (!string.IsNullOrWhiteSpace(mode.UserName))
					{
						return mode.UserName;
					}
					int ordinal;
					if (PerfModeCollection.TryParseCustomOrdinal(mode.Id, out ordinal))
					{
						return string.Format(Strings.CustomProfileN, ordinal);
					}
					return mode.Id;
				}
				PerfModeDefinition trayActiveCustom = trayCustoms.FirstOrDefault((PerfModeDefinition m) => string.Equals(m.Id, trayPerf.DisplayedModeId, StringComparison.Ordinal));
				bool trayCustomActive = trayActiveCustom != null;
				ToolStripMenuItem trayCustomRoot = new ToolStripMenuItem(trayCustomActive ? string.Format(Strings.CustomModeCurrent, TrayCustomLabel(trayActiveCustom)) : Strings.CustomMode)
				{
					Margin = padding,
					Checked = trayCustomActive,
					ForeColor = (trayCustomActive ? UiVisualStyle.Ok : contextMenuStrip.ForeColor),
					Font = (trayCustomActive ? TrayBoldFont() : contextMenuStrip.Font)
				};
				foreach (PerfModeDefinition trayCustom in trayCustoms)
				{
					PerfModeDefinition trayTarget = trayCustom;
					ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem(TrayCustomLabel(trayTarget))
					{
						Margin = padding,
						Checked = trayCustomActive && string.Equals(trayTarget.Id, trayActiveCustom.Id, StringComparison.Ordinal)
					};
					toolStripMenuItem3.Click += async (object sender, EventArgs e) =>
					{
						PerfModeService perfForTray = PerfModeService.Instance;
						PerfApplyOutcome perfOutcome = ((perfForTray == null) ? null : await perfForTray.ActivateAsync(trayTarget.Id, "tray"));
						if (perfOutcome == null || perfOutcome.AnyFailed)
						{
							ToastForm.ShowFailure(Strings.PerfSwitchFailed);
						}
						else
						{
							ShowMode(MechrevoService.ToVisualMode(3));
						}
						SetContextMenu();
					};
					trayCustomRoot.DropDownItems.Add(toolStripMenuItem3);
				}
				contextMenuStrip.Items.Add(trayCustomRoot);
			}
			else
			{
				bool flag = num == 3 && num2 >= 0 && num2 <= 3;
				ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(flag ? string.Format(Strings.CustomModeCurrent, num2 + 1) : Strings.CustomMode)
				{
					Margin = padding,
					Checked = flag,
					ForeColor = (flag ? UiVisualStyle.Ok : contextMenuStrip.ForeColor),
					Font = (flag ? TrayBoldFont() : contextMenuStrip.Font)
				};
				for (int num3 = 0; num3 < 4; num3++)
				{
					int idx = num3;
					ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem(string.Format(Strings.CustomProfileN, num3 + 1))
					{
						Margin = padding,
						Checked = (num == 3 && num2 == idx)
					};
					toolStripMenuItem3.Click += async (object? _, EventArgs _) =>
					{
						bool flag4 = Program.service == null;
						if (!flag4)
						{
							flag4 = !(await Program.service.SwitchCustomProfile(idx));
						}
						if (flag4)
						{
							ToastForm.ShowFailure(Strings.PerfSwitchFailed);
						}
						else
						{
							if (!WinPowerPlan.ApplyProfile(idx))
							{
								ToastForm.ShowFailure(Strings.CustomProfilePowerPlanUnconfirmed);
							}
							// 2026-10-06：托盘切自定义档位同样要刷新模式状态（按固件槽位归属解析模式 id）。
							NotifyTrayCustomProfile(idx);
							ShowMode(MechrevoService.ToVisualMode(3));
							SetContextMenu();
						}
					};
					toolStripMenuItem2.DropDownItems.Add(toolStripMenuItem3);
				}
				contextMenuStrip.Items.Add(toolStripMenuItem2);
			}
		}
		contextMenuStrip.Items.Add("-");
		int num4;
		int num5;
		if (!trayAudit)
		{
			MechrevoHw mechrevoHw = trayHw;
			if (mechrevoHw == null)
			{
				num4 = 0;
			}
			else
			{
				num4 = (mechrevoHw.CanOfferGpuModeSwitch ? 1 : 0);
				if (num4 != 0)
				{
					goto IL_0401;
				}
			}
			num5 = 0;
			goto IL_041c;
		}
		num4 = 1;
		goto IL_0401;
		IL_0401:
		Logger.WriteLine($"TrayGPU: IgpuOnly={trayHw?.CanOfferIgpuOnly} SupIgpu={trayHw?.SupportsIgpuOnly} SupDgpu={trayHw?.SupportsDgpuDirect} Switch={trayHw?.CanOfferGpuModeSwitch} IgpuStatus={trayHw?.IgpuOnlyStatusSupport} GcuHide={trayHw?.GpuRowHiddenByGcuReports} GcuRaw={trayHw?.GcuDgpuDirectSupportRaw ?? "-"}");
		// 2026-10-06（gcu 门）：托盘与设置窗必须一致 —— GCU 明确说不支持独显直连时，整段「显卡模式」都不出现
		// （设置窗那侧走 HideGPUModes，见 GPUModeControl；这里连 flag2/num5 一起收掉，不留"只有核显模式"的残行）。
		num5 = ((!(trayHw?.GpuRowHiddenByGcuReports ?? false) && TrayShow((trayHw?.CanOfferIgpuOnly ?? false))) ? 1 : 0);
		goto IL_041c;
		IL_041c:
		bool flag2 = (byte)num5 != 0;
		bool flag3 = num4 != 0 && TrayShow(trayHw?.SupportsDgpuDirect ?? mechrevoDeviceCapabilities.DgpuDirect);
		if (flag2 | flag3)
		{
			ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem(Strings.GPUMode);
			toolStripMenuItem4.Margin = padding;
			toolStripMenuItem4.Enabled = false;
			contextMenuStrip.Items.Add(toolStripMenuItem4);
			if (flag2)
			{
				menuEco = new ToolStripMenuItem(Strings.IgpuMode);
				menuEco.Click += ButtonEco_Click;
				menuEco.Margin = padding;
				menuEco.Checked = buttonEco.Activated;
				contextMenuStrip.Items.Add(menuEco);
			}
			menuStandard = new ToolStripMenuItem(Strings.HybridMode);
			menuStandard.Click += ButtonStandard_Click;
			menuStandard.Margin = padding;
			menuStandard.Checked = buttonStandard.Activated;
			contextMenuStrip.Items.Add(menuStandard);
			if (flag3)
			{
				menuUltimate = new ToolStripMenuItem(Strings.DirectMode);
				menuUltimate.Click += ButtonUltimate_Click;
				menuUltimate.Margin = padding;
				menuUltimate.Checked = buttonUltimate.Activated;
				contextMenuStrip.Items.Add(menuUltimate);
			}
			contextMenuStrip.Items.Add("-");
		}
		if (!trayAudit && !mechrevoDeviceCapabilities.Keyboard)
		{
			MechrevoHw mechrevoHw2 = trayHw;
			if (mechrevoHw2 == null || !mechrevoHw2.SupportsKeyboard)
			{
				goto IL_060c;
			}
		}
		AddAction(Strings.KeyboardLighting, checked_: false, () =>
		{
			OpenRgbForm();
		});
		goto IL_060c;
		IL_060c:
		AddAction(Strings.OverlayMonitor, AppConfig.IsOverlay(), () =>
		{
			ToggleOverlay();
		});
		AddAction(Strings.OpenMainUi, checked_: false, () =>
		{
			Program.SettingsToggle(checkForFocus: false, trayClick: true);
		});
		AddAsyncAction(Strings.ExportDiagnostics, checked_: false, () => DiagnosticPackCommand.RunAsync(this, null));
		contextMenuStrip.Items.Add("-");
		ToolStripMenuItem toolStripMenuItem5 = new ToolStripMenuItem(Strings.Quit);
		toolStripMenuItem5.Click += ButtonQuit_Click;
		toolStripMenuItem5.Margin = padding;
		contextMenuStrip.Items.Add(toolStripMenuItem5);
		contextMenuStrip.Renderer = new CustomMenuRenderer();
		InitContextMenuTheme();
		// 2026-10-09（批次3 #10）：托盘懒加载 ⇒ 这里只挂到**已存在**的托盘上（⛔ 不因装菜单而创建托盘）；
		// 托盘真正创建时 Program.EnsureTrayIcon() 会再调一次本方法，菜单项一个不少。
		if (Program.ExistingTrayIcon != null)
		{
			Program.ExistingTrayIcon.ContextMenuStrip = contextMenuStrip;
		}
		void AddAction(string text, bool checked_, Action onClick)
		{
			ToolStripMenuItem toolStripMenuItem6 = new ToolStripMenuItem(text)
			{
				Margin = padding,
				Checked = checked_
			};
			toolStripMenuItem6.Click += (object? _, EventArgs _) =>
			{
				onClick();
			};
			contextMenuStrip.Items.Add(toolStripMenuItem6);
		}
		void AddAsyncAction(string text, bool checked_, Func<Task> onClick)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text)
			{
				Margin = padding,
				Checked = checked_
			};
			item.Click += async (object? _, EventArgs _) =>
			{
				item.Enabled = false;
				try
				{
					await onClick();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Tray action failed: " + ex.Message);
					ToastForm.ShowFailure(Strings.TrayActionFailed);
				}
				finally
				{
					if (!item.IsDisposed)
					{
						item.Enabled = true;
					}
				}
			};
			contextMenuStrip.Items.Add(item);
		}
		void AddDisabledTitle(string text)
		{
			ToolStripMenuItem value = new ToolStripMenuItem(text)
			{
				Margin = padding,
				Enabled = false
			};
			contextMenuStrip.Items.Add(value);
		}
		bool TrayShow(bool supported)
		{
			return trayAudit | supported;
		}
	}

	public void InitContextMenuTheme()
	{
		_liquidCoolingLightMenu?.ApplyTheme();
		if (contextMenuStrip != null)
		{
			contextMenuStrip.BackColor = BackColor;
			contextMenuStrip.ForeColor = ForeColor;
			// Tray menu inherits the UI font family (HarmonyOS Sans SC, falls back to
			// Microsoft YaHei UI) instead of the system default.
			contextMenuStrip.Font = UiVisualStyle.Font(contextMenuStrip.Font.Size, contextMenuStrip.Font.Style);
		}
		donateControl?.ApplyTheme();
	}

	private void LabelVersion_Click(object? sender, EventArgs e)
	{
		// 2026-10-10 licence: 版本号改成「关于 / 开源许可」入口（GPLv3 §0/§5d 的 Appropriate Legal
		// Notices 必须在界面里可达）。原来的 QA「测试反馈」弹窗保留在 Ctrl+点击 上，功能不丢。
		if ((ModifierKeys & Keys.Control) == Keys.Control)
		{
			ShowTestFeedback();
			return;
		}
		using AboutLicensesForm aboutLicensesForm = new AboutLicensesForm();
		AddOwnedForm(aboutLicensesForm);
		aboutLicensesForm.ShowDialog(this);
	}

	internal static void ShowTestFeedback()
	{
		MessageBox.Show(Strings.TestFeedback, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ShowUpdateDialog()
	{
		try
		{
			using UpdateForm updateForm = new UpdateForm(autoCheck: false);
			AddOwnedForm(updateForm);
			updateForm.ShowDialog(this);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新窗口打开失败：" + ex.Message);
		}
	}

	internal void MarkUpdateAvailable(UpdateInfo info)
	{
		if (IsDisposed)
		{
			return;
		}
		try
		{
			buttonUpdates.Text = Strings.CheckUpdatesDot;
			buttonUpdates.ForeColor = UiVisualStyle.Accent;
			toolTip.SetToolTip(buttonUpdates, string.Format(Strings.UpdateAvailableTooltip, info.LatestVersion));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新角标设置失败：" + ex.Message);
		}
	}

	private static void OnTimedEvent(object? source, ElapsedEventArgs? e)
	{
		ChargeLimitMonitor.Tick(DateTime.Now);
		Program.settingsForm.RefreshSensors();
	}

	private void ButtonFHD_MouseHover(object? sender, EventArgs e)
	{
		labelTipScreen.Text = "Switch to " + ((buttonFHD.Text == "FHD") ? "UHD" : "FHD") + " Mode";
	}

	private void Button120Hz_MouseHover(object? sender, EventArgs e)
	{
		labelTipScreen.Text = Strings.MaxRefreshTooltip;
	}

	private void Button60Hz_MouseHover(object? sender, EventArgs e)
	{
		labelTipScreen.Text = Strings.MinRefreshTooltip.Replace("60", ScreenControl.MIN_RATE.ToString());
	}

	private void ButtonScreen_MouseLeave(object? sender, EventArgs e)
	{
		labelTipScreen.Text = "";
	}

	private void ButtonScreenAuto_MouseHover(object? sender, EventArgs e)
	{
		labelTipScreen.Text = Strings.AutoRefreshTooltip.Replace("60", ScreenControl.MIN_RATE.ToString());
	}

	private void ButtonUltimate_MouseHover(object? sender, EventArgs e)
	{
	}

	private void ButtonStandard_MouseHover(object? sender, EventArgs e)
	{
	}

	private void ButtonEco_MouseHover(object? sender, EventArgs e)
	{
	}

	private void ButtonGPU_MouseLeave(object? sender, EventArgs e)
	{
	}

	private void ButtonScreenAuto_Click(object? sender, EventArgs e)
	{
		ScreenControl.SetAutoRefresh(1);
		ScreenControl.AutoScreen();
	}

	private async Task RefreshStartupStatusAsync()
	{
		if (Interlocked.Exchange(ref _startupStatusLoading, 1) != 0)
		{
			return;
		}
		try
		{
			bool? flag = await Task.Run(Startup.ReadScheduledState);
			if (!flag.HasValue)
			{
				throw new InvalidOperationException("Can't read autostart state.");
			}
			bool value = flag.Value;
			if (!IsDisposed)
			{
				_syncingStartup = true;
				try
				{
					checkStartup.Checked = value;
				}
				finally
				{
					_syncingStartup = false;
				}
				if (value && !AppConfig.Exists("startup_enabled"))
				{
					AppConfig.Set("startup_enabled", 1);
				}
				checkStartup.Enabled = true;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Startup status refresh failed: " + ex.Message);
			if (!IsDisposed)
			{
				checkStartup.Enabled = true;
			}
		}
		finally
		{
			Volatile.Write(ref _startupStatusLoading, 0);
		}
	}

	private async void CheckStartup_Click(object? sender, EventArgs e)
	{
		if (!(sender is CheckBox chk) || _syncingStartup || Program.UiAuditMode)
		{
			return;
		}
		bool requested = chk.Checked;
		if (!NativeMethods.HasFreshUserInput())
		{
			Logger.WriteLine("Autostart (footer) ignored: no fresh user input.");
			await RefreshStartupStatusAsync();
			return;
		}
		chk.Enabled = false;
		try
		{
			_ = 1;
			try
			{
				bool flag = await Task.Run(() => Startup.WriteScheduledState(requested));
				if (flag)
				{
					AppConfig.Set("startup_enabled", requested ? 1 : 0);
				}
				if (!flag && !IsDisposed)
				{
					string text = (requested ? Strings.Create : Strings.Delete);
					MessageBox.Show(string.Format(Strings.StartupTaskFailed, text), "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Startup schedule change failed: " + ex.Message);
			}
		}
		finally
		{
			await RefreshStartupStatusAsync();
			if (!IsDisposed)
			{
				chk.Enabled = true;
			}
		}
	}

	private void LabelCPUFan_Click(object? sender, EventArgs e)
	{
		RefreshSensors(force: true);
	}

	private void ButtonKeyboardColor2_Click(object? sender, EventArgs e)
	{
		SetColorPicker("aura_color2", Aura.Color2);
	}

	private void SetColorPicker(string colorField, Color initial)
	{
		if (Aura.IsMechrevoConnected)
		{
			return;
		}
		using RColorPicker rColorPicker = new RColorPicker(initial, colorField == "aura_color" && Aura.HasRandomColor());
		rColorPicker.ColorChanged += (Color c) =>
		{
			AppConfig.Set(colorField, c.ToArgb());
			SetAura();
		};
		rColorPicker.ShowDialog(this);
	}

	private void ButtonKeyboardColor_Click(object? sender, EventArgs e)
	{
		SetColorPicker("aura_color", Aura.Color1);
	}

	public void InitAura()
	{
		comboKeyboard.Visible = false;
		buttonKeyboardColor.Visible = false;
		labelBacklight.Visible = false;
	}

	public void SetAura()
	{
		if (!Aura.IsMechrevoConnected)
		{
			Task.Run(() =>
			{
				Aura.ApplyAura();
				VisualiseAura();
			});
		}
	}

	private void _VisualiseAura()
	{
		buttonKeyboardColor.SwatchColor = Aura.Color1;
		buttonKeyboardColor.SwatchColor2 = (Aura.HasSecondColor() ? new Color?(Aura.Color2) : ((Color?)null));
		if (AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled() && !AppConfig.IsDynamicLightingOnly())
		{
			labelBacklight.Cursor = Cursors.Hand;
			labelBacklight.Text = Strings.DisableDynamicLighting;
		}
		else if (Aura.Mode == AuraMode.Ambient)
		{
			labelBacklight.Cursor = Cursors.Default;
			labelBacklight.Text = Strings.AmbientModeResources;
		}
		else
		{
			labelBacklight.Cursor = Cursors.Default;
			labelBacklight.Text = "";
		}
	}

	public void VisualiseAura()
	{
		if (InvokeRequired)
		{
			Invoke(_VisualiseAura);
		}
		else
		{
			_VisualiseAura();
		}
	}

	private void Button120Hz_Click(object? sender, EventArgs e)
	{
		ScreenControl.SetAutoRefresh(0);
		ScreenControl.SetScreen(1000);
	}

	private void Button60Hz_Click(object? sender, EventArgs e)
	{
		ScreenControl.SetAutoRefresh(0);
		ScreenControl.SetScreen(ScreenControl.MIN_RATE);
	}

	private async void ButtonMiniled_Click(object? sender, EventArgs e)
	{
		if (Program.service != null && Program.hw != null)
		{
			await Program.service.SwitchLocalDimming(!Program.hw.LocalDimming);
		}
	}

	public void VisualiseScreen(bool screenEnabled, bool screenAuto, int frequency, int maxFrequency, int overdrive, bool overdriveSetting, int miniled1, int miniled2, bool hdr, bool acm, int fhd, int hdrControl)
	{
		Logger.WriteLine($"VisualiseScreen <- enabled={screenEnabled} freq={frequency} maxFreq={maxFrequency} minRate={ScreenControl.MIN_RATE}");
		ButtonEnabled(button60Hz, screenEnabled);
		ButtonEnabled(button120Hz, screenEnabled);
		ButtonEnabled(buttonScreenAuto, screenEnabled);
		ButtonEnabled(buttonMiniled, screenEnabled);
		labelSreen.Text = (screenEnabled ? (Strings.LaptopScreen + ": " + frequency + "Hz" + ((overdrive == 1) ? (" + " + Strings.Overdrive) : "")) : (Strings.LaptopScreen + ": " + Strings.TurnedOff));
		panelScreen.AccessibleName = labelSreen.Text;
		button60Hz.Activated = false;
		button120Hz.Activated = false;
		buttonScreenAuto.Activated = false;
		if (screenAuto)
		{
			buttonScreenAuto.Activated = true;
		}
		else if (frequency == ScreenControl.MIN_RATE)
		{
			button60Hz.Activated = true;
		}
		else if (frequency > ScreenControl.MIN_RATE)
		{
			button120Hz.Activated = true;
		}
		button60Hz.Text = ScreenControl.MIN_RATE + "Hz";
		if (maxFrequency > ScreenControl.MIN_RATE)
		{
			button120Hz.Text = maxFrequency + "Hz" + (overdriveSetting ? " + OD" : "");
			tableScreen.Visible = ScreenPanelVisibility.RefreshTableVisible(maxFrequency, ScreenControl.MIN_RATE);
		}
		else if (maxFrequency > 0)
		{
			tableScreen.Visible = ScreenPanelVisibility.RefreshTableVisible(maxFrequency, ScreenControl.MIN_RATE);
		}
		else
		{
			// 2026-10-12 screen-diag: 原来没有 else —— maxFrequency<=0（读不到最大刷新率）时
			// tableScreen.Visible 会**保持上一次的值**，一旦被置 false 就再也恢复不了（真机现象：
			// 停掉 GCUBridge 后刷新率 + 亮度整块消失，只剩自动刷新率/校色/色彩增强）。
			// 这里显式写回"读不到就不显示"，把可见性变成每次都由入参决定的纯函数，**不再残留状态**。
			Logger.WriteLineIfChanged("screen-refresh-table-hidden", $"VisualiseScreen: maxFreq={maxFrequency} minRate={ScreenControl.MIN_RATE} -> 刷新率表隐藏（读不到最大刷新率）");
			tableScreen.Visible = ScreenPanelVisibility.RefreshTableVisible(maxFrequency, ScreenControl.MIN_RATE);
		}
		if (fhd >= 0)
		{
			buttonFHD.Visible = true;
			buttonFHD.Text = ((fhd > 0) ? "FHD" : "UHD");
		}
		bool num = hdr && hdrControl >= 0;
		MechrevoHw? hw = Program.hw;
		if (hw != null && hw.SupportsLocalDimming)
		{
			buttonMiniled.Enabled = screenEnabled;
			buttonMiniled.Activated = Program.hw.LocalDimming | hdr;
			buttonMiniled.Text = (Program.hw.LocalDimming ? Strings.Multizone : Strings.OneZone);
		}
		if (num)
		{
			buttonHDRControl.Visible = true;
			buttonHDRControl.Activated = hdrControl > 0;
			buttonHDRControl.BorderColor = RForm.colorTurbo;
		}
		else
		{
			buttonHDRControl.Visible = false;
		}
	}

	private void ButtonQuit_Click(object? sender, EventArgs e)
	{
		AsusLampArray.Release();
		Close();
		// 2026-10-09（批次3 #10）：退出收起托盘（Program 侧只动**已存在**的托盘，不会为隐藏而创建）。
		Program.HideTrayIconForExit();
		Application.Exit();
	}

	public void HideAll()
	{
		Hide();
		if (updatesForm != null && updatesForm.Text != "")
		{
			updatesForm.Close();
		}
		MemoryHelper.TrimAfter();
	}

	public void ShowAll(Rectangle? workingArea = null)
	{
		if (!Visible)
		{
			bool flag = !_hasPresentedWindow;
			if (flag)
			{
				Opacity = 0.01;
			}
			WindowState = FormWindowState.Normal;
			if (workingArea.HasValue)
			{
				ApplyResponsiveBounds(workingArea);
			}
			PerformLayout();
			Show();
			if (flag)
			{
				Update();
				Opacity = 1.0;
				_hasPresentedWindow = true;
			}
		}
		Activate();
	}

	internal void StopRuntimeTimers()
	{
		if (Volatile.Read(in _runtimeResourcesDisposed) == 0)
		{
			VirtualDisplayBlank.RestoreImmediate();
			StopRuntimeTimersCore();
			_brightnessCommitQueue.Dispose();
		}
	}

	private void StopRuntimeTimersCore()
	{
		_liquidCoolingStatusTimer.Stop();
		_displayStatusTimer.Stop();
		_quickSwitchStatusTimer.Stop();
		_officialConsoleStatusTimer.Stop();
		batteryTimer.Stop();
		_sensorTimer.Stop();
	}

	private void DisposeRuntimeResources()
	{
		if (Interlocked.Exchange(ref _runtimeResourcesDisposed, 1) == 0)
		{
			VirtualDisplayBlank.RestoreImmediate();
			StopRuntimeTimersCore();
			_brightnessCommitQueue.Dispose();
			_liquidCoolingStatusTimer.Dispose();
			_displayStatusTimer.Dispose();
			_quickSwitchStatusTimer.Dispose();
			_officialConsoleStatusTimer.Dispose();
			batteryTimer.Dispose();
			_sensorTimer.Dispose();
			_liquidCoolingLightMenu?.Dispose();
			contextMenuStrip.Dispose();
		}
	}

	public bool HasAnyFocus(bool lostFocusCheck = false)
	{
		if ((updatesForm == null || !updatesForm.ContainsFocus) && !ContainsFocus)
		{
			if (lostFocusCheck)
			{
				return Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastLostFocus) < 300;
			}
			return false;
		}
		return true;
	}

	private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
	{
		if (e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			HideAll();
		}
	}

	private async void ButtonUltimate_Click(object? sender, EventArgs e)
	{
		await SwitchGpuModeFromUi(2, Strings.DirectMode);
	}

	private async void ButtonStandard_Click(object? sender, EventArgs e)
	{
		await SwitchGpuModeFromUi(1, Strings.HybridShort);
	}

	private async void ButtonEco_Click(object? sender, EventArgs e)
	{
		await SwitchGpuModeFromUi(0, Strings.IgpuShort);
	}

	private async Task SwitchGpuModeFromUi(int targetMode, string modeName)
	{
		int requestSeq = ++_gpuSwitchRequestSeq;
		if (Program.service != null)
		{
			MechrevoHw hw = Program.hw;
			if (hw != null && hw.IsConnected)
			{
				if (Program.hw != null && !Program.hw.CanSwitchGpuMode(targetMode))
				{
					Logger.WriteLine($"GPU mode UI request rejected as unsupported: {targetMode}");
					RefreshDeviceCapabilities();
					MessageBox.Show(Strings.GpuModeUnsupported, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
				}
				GpuModeStatusReadback gpuModeStatusReadback = await RefreshGpuModeUiStateAsync();
				if (gpuModeStatusReadback == GpuModeStatusReadback.Unavailable)
				{
					MessageBox.Show(Strings.GpuModeUnreadable, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
				}
				// ⭐⭐ 2026-10-16（宝龙达 · 修「显卡模式切换不行」第二步）：
				//   ⚠️ 上面那道「读不到当前模式」我已经修了 ✅（`RefreshGpuModeStatus` 加了宝龙达分派 ✅）
				//      ⭐ 但 ⭐ **真正"写下去"的那一步也还没接给宝龙达** ❌：
				//      ⭐ 下面 `GpuSwitchPolicy.Resolve` 之后 ⭐ 要么走
				//         `Program.service.SwitchGpuMode(...)`（⭐ GCU MQTT ❌）、
				//         ⭐ 要么只 `ShowGpuRestartPromptAsync`（⭐ 从不真的写 ❌）
				//      ⇒ ⭐ ⭐ **所以必须在这里插一条宝龙达自己的分支** ✅
				//   ⭐ 分支**以 `UsesBaolongda` 开头** ⇒ ⭐ 非宝龙达**逐位不变** ✅（⭐ 下面一个字未改 ✅）
				MechrevoHw bdHw = Program.hw;
				if (bdHw != null && bdHw.UsesBaolongda)
				{
					await SwitchGpuModeBaolongdaAsync(targetMode, modeName, requestSeq);
					return;
				}
				GpuSwitchPlan gpuSwitchPlan = GpuSwitchPolicy.Resolve(Program.service.CurrentGpuMode, targetMode, gpuModeStatusReadback == GpuModeStatusReadback.Fresh);
				Logger.WriteLine($"GpuSwitch UI: target={targetMode} route={gpuSwitchPlan.Route} current={Program.service.CurrentGpuMode} gen={Program.hw.DgpuGeneration} allowsRestart={DisplayRouteMatrix.AllowsRestart(Program.hw.DgpuGeneration)} pendingRestart={HasPendingGpuRestart()} seq={requestSeq}");
				if (gpuSwitchPlan.Route == GpuSwitchRoute.NoChange)
				{
					if (HasPendingGpuRestart())
					{
						await ShowGpuRestartPromptAsync(targetMode, modeName, requestSeq);
					}
					return;
				}
				switch (gpuSwitchPlan.Route)
				{
				case GpuSwitchRoute.Restart:
					if (DisplayRouteMatrix.AllowsRestart(Program.hw.DgpuGeneration))
					{
						await ShowGpuRestartPromptAsync(targetMode, modeName, requestSeq);
						break;
					}
					await Program.service.SwitchGpuMode(targetMode);
					await RefreshGpuModeUiStateAsync();
					break;
				}
				return;
			}
		}
		MessageBox.Show(Strings.GcuServiceNotReady, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达 · 修「显卡模式切换不行」）：⭐ **宝龙达自己的显卡模式切换** ✅
	///
	/// <para>⭐ 流程：⭐ ① 校验目标值（⭐ 宝龙达**只有两个**：⭐ <c>1 = 混合</c> · ⭐ <c>2 = 独显直连</c> ✅）
	/// ⇒ ⭐ ② 写 WMI 功能号 9（⭐ `TrySetGpuModeViaBackend`，⭐ 含**写后回读确认** ✅）
	/// ⇒ ⭐ ③ 刷新界面 ⇒ ⭐ ④ ⭐ **提示「重启以应用」** ✅。</para>
	///
	/// <para>⭐⭐ <b>为什么⛔不能走原来那条 GCU 的路</b>：⭐ 原来那条会
	/// <c>Publish("Setting/Control", …)</c> ⭐ 发「重启路由」（<c>BuildGpuRestartRoute</c> ✅）
	/// ⚠️ ⇒ ⭐ 宝龙达**没有 GCU** ⇒ ⭐ 路由为空 ⇒ ⭐ <c>GpuRestartRequestOutcome.Unsupported</c> ❌
	/// ⇒ ⭐ 用户只会看到「点了没反应」❌。⭐ 宝龙达的「应用」就是**它自己的固件在下次开机时读功能号 9** ✅
	/// ⇒ ⭐ ⭐ **所以只需写 9 ＋ ⭐ 提示重启** ✅。</para>
	///
	/// <para>⚠️ <c>targetMode</c> 是**界面口径**（⭐ <c>0 = 核显</c> · ⭐ <c>1 = 混合</c> · ⭐ <c>2 = 独显直连</c> ✅）；
	/// ⭐ 宝龙达**不支持核显档** ⇒ ⭐ <c>0</c> ⭐ **明确拒绝并如实报错** ✅（⛔ 不静默 ✅）。
	/// ⭐ 且 UI 侧已隐藏那颗按钮 ⇒ ⭐ 正常路径上点不到 ✅。</para>
	/// </summary>
	private async Task SwitchGpuModeBaolongdaAsync(int targetMode, string modeName, int requestSeq)
	{
		MechrevoHw hw = Program.hw;
		if (hw == null)
		{
			return;
		}
		if (targetMode != 1 && targetMode != 2)
		{
			Logger.WriteLine($"宝龙达 显卡模式切换：⭐ 拒绝界面口径 {targetMode}（⭐ 宝龙达不支持核显档，⭐ 只接受 1=混合 / 2=独显直连 ✅）");
			MessageBox.Show(Strings.GpuModeUnsupported, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		// ⭐⭐ 2026-10-19（机主：切走再切回同一模式仍显示黄色高亮）：
		//   ⭐ 判据 = **选择 == 当前生效模式**（取源与原逻辑一致：`ResolveHighlightedGpuMode(-1)`
		//      = 能力位/厂商回显；宝龙达没有 GCU 时按当前实际路由判定）⇒ **蓝色（无待应用）** ✅
		//   ⭐ 切走再切回（等于原模式）⇒ 清掉 pending ⇒ 恢复蓝色，⛔ 不再误显示黄色待重启 ✅
		//   ⚠️ 仅宝龙达分支（GCU/同方路径逐位不变）✅
		int currentEffective = ResolveHighlightedGpuMode(-1);
		if (currentEffective == targetMode)
		{
			ClearGpuRestartPending();
			VisualiseGPUMode();
			LogGpuModeHighlight(targetMode, currentEffective, pending: false);
			return;
		}
		int backendMode = ((targetMode == 2) ? BaolongdaWmi.GpuModeDiscrete : BaolongdaWmi.GpuModeHybrid);
		bool ok = hw.TrySetGpuModeViaBackend(backendMode, out bool confirmed);
		Logger.WriteLine($"宝龙达 显卡模式切换：⭐ 界面={targetMode}（{modeName}）⇒ ⭐ 后端功能号 9={backendMode}"
			+ $" ⇒ ⭐ 写入={(ok ? "成功" : "失败")} · ⭐ 回读确认={confirmed}"
			+ "（⭐ 界面口径：⭐ 1=混合 · ⭐ 2=独显直连 ✅）");
		if (!ok)
		{
			if (requestSeq == _gpuSwitchRequestSeq)
			{
				MessageBox.Show(Strings.GpuModeUnsupported, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			return;
		}
		await RefreshGpuModeUiStateAsync();
		if (requestSeq != _gpuSwitchRequestSeq || IsDisposed)
		{
			return;
		}
		// ⭐⭐ 2026-10-16（机主真机反馈 · 修「弹窗 GCU 未能发送重启切换请求」）：
		//   ⚠️⚠️ **原先这里在 AllowsRestart 时会调 `ShowGpuRestartPromptAsync`** ❌ ——
		//      ⭐ 而那个方法是 **GCU 专用**的 ⚠️：⭐ 它弹「重启提示」⇒ ⭐ 用户选「是/否」之后
		//      ⭐ 会走 ⭐ `Program.service.SwitchGpuMode(...)` ⭐ / ⭐ `RequestGpuModeRestartOutcomeAsync(...)`
		//      ⭐ ⭐ **两条都是 GCU MQTT** ❌
		//   ⇒ ⭐ ⭐ **宝龙达没有 GCU** ⇒ ⭐ 必然失败 ⇒ ⭐ 弹「⭐ **GCU 未能发送重启切换请求，
		//      显卡模式未被标记为已切换。**」❌ —— ⭐ ⭐ **而功能号 9 其实【已经写成功了】** ✅
		//      ⇒ ⭐ 用户看到的弹窗与事实**相反** ⚠️（⭐ 真机日志：⭐ 写入=成功 · ⭐ 回读确认=True ✅）
		//   ⇒ ⭐ ⭐ **所以宝龙达【一律不走那条 GCU 提示】** ✅ —— ⭐ 改为一律「记待重启 ＋ 日志」✅
		//   ⭐ 为什么这样也**不算「点了没反应」** ✅：
		//      ⭐ `MarkGpuRestartPending` ⇒ ⭐ ⭐ **界面以橙色高亮待切换状态** ✅
		//      ⭐（⭐ 见 `:11104` 注释：⭐「⭐ 标记待重启，⭐ 界面以橙色高亮待切换状态」✅）
		//      ⭐ ＋ ⭐ 上面那行日志（⭐ 默认级别可见性由级别决定，⭐ 诊断包里一定看得到 ✅）
		//   ⚠️ 待办：⭐ 若日后想要更明显的提示 ⇒ ⭐ 可在 `Strings` 加一条文案 ＋ ⭐ 用
		//      ⭐ `ToastForm.ShowNotice(...)`（⭐ 本次刻意不动 resx，⭐ 减少改动面 ✅）
		//   ⭐ 语义：⭐ 宝龙达的「应用」＝ ⭐ **它自己的固件下次开机读功能号 9** ✅
		//      ⇒ ⭐ ⛔ 不需要 GCU 的重启路由 ✅
		MarkGpuRestartPending(targetMode);
		LogGpuModeHighlight(targetMode, currentEffective, pending: true);
		Logger.WriteLine("宝龙达 显卡模式切换：⭐ 已写入功能号 9 ⇒ ⭐ 记为「待重启生效」"
			+ "（⭐ 宝龙达不走 GCU 重启路由 ⇒ ⭐ 请用户在方便时手动重启 ✅）");
		// ⭐⭐ 2026-10-16（0.291.9.3 · 机主真机反馈「⭐ 显卡模式切换后没有重启提示，⭐ 我也不知道切换成功没」⚠️）：
		//   ⚠️⚠️ 上一次（⭐ `740951c`）把 GCU 那条**错误的**弹窗去掉了 ✅ ——
		//      ⭐ 但**只剩「⭐ 界面橙色高亮待切换状态」**（⭐ `MarkGpuRestartPending` ✅）⚠️
		//      ⇒ ⭐ ⭐ **机主说看不到** ❌ ⇒ ⭐ 所以那次等于"⭐ 点了没反应"⚠️
		//   ⇒ ⭐ ⭐ **现在补一条明确的用户可见提示** ✅ —— ⭐ 用既有的 `ToastForm.ShowNotice`
		//      （⭐ 它内部 `RunToast(..., essential: true)` ✅ ⇒ ⭐ **即使「OSD 提示」快捷开关关掉也照样弹** ✅
		//       —— ⭐ 因为这是"**功能结果告知**"、⭐ 不是装饰性 OSD ✅ 与 `ToastForm` 的既有设计一致 ✅）
		//   ⭐ 文案走**行内**（⛔ 不动 `Strings*.resx`：⭐ 它是**混合行尾** ⚠️、⭐ 加串要同改 3 个文件；
		//      ⭐ 且本文件/本项目**已有行内文案先例** ✅ —— ⭐ 例如 `CustomModeForm.LocalApplyText`）
		//   ⭐ ⭐ **必须区分成功与未确认** ✅（⭐ 诚实性铁律 ⚠️）：
		//      ⭐ `confirmed` 为真 ⇒ ⭐ 回读已确认功能号 9 写对了 ✅ ⇒ ⭐ 才敢说「已切到」
		//      ⚠️ 未确认 ⇒ ⭐ **不谎报"已切换"** ⚠️ ⇒ ⭐ 换一句"未确认"的说法 ✅
		//   ⭐ 语义：⭐ 宝龙达的「应用」＝ ⭐ **它自己的固件下次开机读功能号 9** ✅ ⇒ ⭐ 重启才生效 ✅
		ToastForm.ShowNotice(confirmed
			? $"已切到{modeName}，重启后生效"
			: $"已请求切到{modeName}，但回读未确认，重启后请再确认");
	}

	private async Task<GpuModeStatusReadback> RefreshGpuModeUiStateAsync()
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
	}

	private static void MarkGpuRestartPending(int targetMode = -1)
	{
		// 2026-10-13：刚下发切换 —— 清掉 pending-route 的 5 秒缓存，别让后面的刷新拿
		// 「下发之前」的旧读数去判定「待生效已满足」（那会让高亮与「重启以应用」标签打架）。
		UefiDisplayModeDiagnostics.InvalidatePendingRouteCache();
		AppConfig.Set("gpu_restart_pending", 1);
		AppConfig.Set("gpu_restart_tick", Environment.TickCount64.ToString(CultureInfo.InvariantCulture));
		if (targetMode >= 0)
		{
			AppConfig.Set("gpu_restart_target", targetMode);
		}
		// 2026-10-06 D-1「试过就记住」：在不确认的机型上（按钮本来就是猜出来的）点「核显模式」
		// 等于用户同意"试一次"—— 记一笔，等这次重启后的开机回读给结论（切得过去吗）。
		// 其它机型（30 系硬屏蔽 / 50 系可靠判据 / 用户已明确表态）不记：那本来就不是猜的。
		if (targetMode == 0 && (Program.hw?.IgpuOnlyUiIsGuess ?? false))
		{
			IgpuOnlyAttempt.MarkAttempt();
		}
	}

	private static bool HasPendingGpuRestart()
	{
		return GpuRestartVerifier.HasPendingThisBoot();
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：切走再切回同一模式仍显示黄色高亮）：⭐ GPU 模式高亮的**判据日志**（去重）✅。
	/// <para>判据：`选中 == 当前生效` ⇒ **蓝色**（无待应用）；不等 ⇒ 黄色（待应用/待重启）。</para>
	/// </summary>
	private static void LogGpuModeHighlight(int selectedMode, int currentMode, bool pending)
	{
		string selectedName = ((selectedMode >= 0) ? GpuRestartVerifier.ModeName(selectedMode) : "未知");
		string currentName = ((currentMode >= 0) ? GpuRestartVerifier.ModeName(currentMode) : "未知");
		string message = "[igpu] GPU 模式高亮：选择=" + selectedName + " · 当前=" + currentName
			+ " ⇒ " + (pending ? "黄色（待应用）" : "蓝色（无待应用）");
		if (Logger.TryMarkChanged("gpu-mode-highlight", message))
		{
			Logger.WriteError(message);
		}
	}

	private static void ClearGpuRestartPending()
	{
		AppConfig.Set("gpu_restart_pending", 0);
		AppConfig.Remove("gpu_restart_tick");
	}

	private async Task<bool> ShowGpuRestartPromptAsync(int targetMode, string modeName, int requestSeq)
	{
		// 2026-10-05: no supersede/dedupe guard here on purpose. This dialog is shown
		// synchronously and is owner-modal, so no other request can arrive while it is
		// open (the guard that used to be here was dead code). The 15 s fallback
		// watchdog in WatchGcuRestartFallbackAsync is where supersede/dedupe is needed.
		Logger.WriteLine($"GpuRestartPrompt: seq={requestSeq} latest={_gpuSwitchRequestSeq}");
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
			try
			{
				if (await Program.service.SwitchGpuMode(targetMode))
				{
					AppConfig.Set("gpu_mode", targetMode);
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
		if (Program.service == null)
		{
			await RefreshGpuModeUiStateAsync();
			return false;
		}
		int previousMode = AppConfig.Get("gpu_mode");
		bool hadPendingRestart = HasPendingGpuRestart();
		string previousRestartTick = AppConfig.GetString("gpu_restart_tick") ?? "";
		AppConfig.Set("gpu_mode", targetMode);
		MarkGpuRestartPending(targetMode);
		AppConfig.Flush();
		GpuRestartRequestOutcome outcome = await Program.service.RequestGpuModeRestartOutcomeAsync(targetMode);
		if (outcome != GpuRestartRequestOutcome.Requested)
		{
			AppConfig.Set("gpu_mode", previousMode);
			if (hadPendingRestart)
			{
				AppConfig.Set("gpu_restart_pending", 1);
				AppConfig.Set("gpu_restart_tick", previousRestartTick);
			}
			else
			{
				ClearGpuRestartPending();
			}
			AppConfig.Flush();
			await RefreshGpuModeUiStateAsync();
			MessageBox.Show((outcome == GpuRestartRequestOutcome.Unsupported) ? (string.Format(Strings.GpuRestartUnsupported, modeName)) : Strings.GpuRestartRequestFailed, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return false;
		}
		VisualiseGPUMode(targetMode);
		SetContextMenu();
		WatchGcuRestartFallbackAsync(requestSeq);
		return true;
	}

	private async Task WatchGcuRestartFallbackAsync(int requestSeq)
	{
		await Task.Delay(15000);
		if (IsDisposed || !IsHandleCreated)
		{
			return;
		}
		// 2026-10-05: one watchdog per switch request used to mean one dialog each --
		// two quick requests produced two stacked prompts. A newer request supersedes
		// this watchdog (user decision: discard the stale prompt).
		if (requestSeq != _gpuSwitchRequestSeq)
		{
			Logger.WriteLine($"GCU restart fallback: superseded (seq={requestSeq} latest={_gpuSwitchRequestSeq}) -> discarded");
			return;
		}
		try
		{
			BeginInvoke(() =>
			{
				if (_gpuRestartPromptOpen)
				{
					Logger.WriteLine("GpuRestartManualPrompt: another prompt is already open -> discarded");
					return;
				}
				_gpuRestartPromptOpen = true;
				try
				{
					if (MessageBox.Show(this, Strings.GpuRestartManualPrompt, "L-Mechrevo", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
					{
						SystemRestart.CaptureUserConfirmation();
						Logger.WriteLine("GCU restart did not happen; falling back to Windows shutdown /r.");
						SystemRestart.RequestRestart("GCU restart fallback");
					}
				}
				finally
				{
					_gpuRestartPromptOpen = false;
				}
			});
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private void ButtonStopGPU_Click(object? sender, EventArgs e)
	{
		gpuControl.KillGPUApps();
	}

	public void RefreshSensors(bool force = false)
	{
		int num = 2000;
		if (!force && Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastRefresh) < num)
		{
			return;
		}
		lastRefresh = DateTimeOffset.Now.ToUnixTimeMilliseconds();
		string text = "";
		string gpuTemp = "";
		string text2 = "";
		string gpuFan = "";
		string midFan = "";
		string battery = "";
		string charge = "";
		string batteryTip = "";
		bool batteryAbnormal = false;
		HardwareControl.ReadSensors();
		MechrevoHw? hwSensors = Program.hw;
		TimeSpan fanInfoMaximumAge = MechrevoHw.FanInfoMaximumAge;
		if (HardwareControl.cpuTemp > 0 && hwSensors != null && hwSensors.IsSensorInfoFresh(cpu: true, fanInfoMaximumAge, -1L))
		{
			text = ": " + TempHelper.FormatTemp(HardwareControl.cpuTemp);
		}
		if (HardwareControl.batteryCapacity > 0)
		{
			charge = Strings.BatteryCharge + ": " + HardwareControl.batteryCharge;
		}
		BatteryDisplayInput batteryInput = CurrentBatteryDisplayInput();
		battery = BatteryHeadlineText(in batteryInput);
		batteryTip = BatteryDetailsTooltip(in batteryInput);
		batteryAbnormal = batteryInput.Abnormal;
		if (HardwareControl.gpuTemp > 0 && hwSensors != null)
		{
			gpuTemp = (hwSensors.IsSensorInfoFresh(cpu: false, fanInfoMaximumAge, -1L) ? ": " + TempHelper.FormatTemp(HardwareControl.gpuTemp) : ": —");
		}
		bool fanInfoFresh = hwSensors?.IsFanInfoFresh(fanInfoMaximumAge, -1L) ?? false;
		// ⭐⭐ 2026-10-16（0.291.9.3 · 机主反馈"⭐ 托盘显示 `风扇: 0% (1790 rpm)`、⭐ 而主界面只显示 `1790rpm`"⚠️）：
		//   ⚠️⚠️ 根因：⭐ 主界面那行遥测（⭐ `TelemetryParts` ✅）已在 `94dcba6` 修成
		//      ⭐ **宝龙达下把 duty 当"未知"（`null`）** ✅ —— 但**这里（⭐ 托盘/悬浮窗 ✅）
		//      ⭐ 走的是【另一套拼装】** ❌ ⇒ ⭐ 它仍拿 `HardwareControl.cpuFan/gpuFan`（⭐ = `hw.*FanDuty` ⚠️）
		//      ⇒ ⭐ 宝龙达**没有 GCU 推送** ⇒ 那两个 duty **恒为 0** ⚠️
		//      ⇒ ⭐ 于是显示 ❌ **误导性的 `0%`**（⭐ 而同一行 RPM 明明有值 ✅）
		//   ⇒ ⭐ 修法：⭐ 与主界面**同一口径** ✅ —— 宝龙达下把 duty 当"未知" ⇒
		//      ⭐ 不满足下面那支 ⇒ ⭐ 落到"⭐ 只有 RPM"那一支 ⇒ ⭐ 显示 `风扇: 1790 rpm` ✅
		//      ⇒ ⭐ 托盘与主界面**就一致了** ✅（⭐ 机主反馈的"没有同时"✅）
		//   ⚠️ 非宝龙达（⭐ GCU/同方 ✅）⇒ `dutyUnknown` 为假 ⇒ ⭐ **判据一字不变** ✅
		//      ⇒ ⭐ **行为逐位不变** ✅（⭐ 含"⭐ duty=0 时照旧显示 `0%`"⚠️）
		bool dutyUnknown = hwSensors != null && hwSensors.UsesBaolongda;
		int? cpuFan = dutyUnknown ? (int?)null : HardwareControl.cpuFan;
		if (fanInfoFresh && cpuFan.HasValue && cpuFan.GetValueOrDefault() >= 0)
		{
			string[] array = new string[6]
			{
				Strings.FanSpeed,
				": ",
				null,
				null,
				null,
				null
			};
			cpuFan = HardwareControl.cpuFan;
			array[2] = cpuFan.ToString();
			if (HardwareControl.cpuFanRPM >= 0)
			{
				array[3] = "% (";
				array[4] = HardwareControl.cpuFanRPM.ToString();
				array[5] = " rpm)";
			}
			else
			{
				// 30-series GCU does not report RPM; omit the suffix instead of "(-1 rpm)".
				array[3] = "%";
			}
			text2 = string.Concat(array);
		}
		// ⭐⭐ 2026-10-16（0.291.9 · 修"宝龙达机型界面看不到风扇转速"）：
		//   ⚠️ 根因之二（⭐ 也是主因）：⭐ 上面那支的**门槛是"占空比%"**（`HardwareControl.cpuFan` ✅），
		//      而 ⭐ RPM ⭐ **只是括号里的后缀**（⭐ `% (1965 rpm)` ⚠️）。
		//      ⚠️ 而占空比只由 ⭐ **GCU/MQTT 载荷**（`CpuFanDuty` ✅）填 ⇒ ⭐ 宝龙达**没有 GCU 推送**
		//      ⇒ ⭐ `cpuFan` 恒为 null/负 ⇒ ⭐ ⭐ **整行风扇读数被省略，RPM 也跟着看不到** ❌
		//   ⭐ 修法：⭐ 补一支"⭐ **只有 RPM**"的显示（⭐ 直接显示 `1965 rpm`，⛔ **不编造百分比** ✅）。
		//   ⚠️ 安全性：⭐ 这支是 `else if` ⇒ ⭐ **只有在上面那支不成立时才会走** ✅
		//      ⇒ ⭐ GCU 机型（⭐ 一定有占空比 ✅）**永远走上面那支** ⇒ ⭐ **行为逐位不变** ✅
		//   ⚠️ 用 `> 0` 而不是 `>= 0`：⭐ `cpuFanRPM` 是普通 static int，⭐ 默认 0 ⚠️
		//      ⇒ ⭐ 用 `>= 0` 会把"⭐ 从没读到过"（⭐ 0 ⚠️）当成有效值 ⇒ ⭐ 显示 "0 rpm" ❌
		else if (fanInfoFresh && HardwareControl.cpuFanRPM > 0)
		{
			text2 = Strings.FanSpeed + ": " + HardwareControl.cpuFanRPM + " rpm";
		}
		cpuFan = dutyUnknown ? (int?)null : HardwareControl.gpuFan;
		if (fanInfoFresh && cpuFan.HasValue && cpuFan.GetValueOrDefault() >= 0)
		{
			string[] array2 = new string[6]
			{
				Strings.FanSpeed,
				": ",
				null,
				null,
				null,
				null
			};
			cpuFan = HardwareControl.gpuFan;
			array2[2] = cpuFan.ToString();
			if (HardwareControl.gpuFanRPM >= 0)
			{
				array2[3] = "% (";
				array2[4] = HardwareControl.gpuFanRPM.ToString();
				array2[5] = " rpm)";
			}
			else
			{
				// 30-series GCU does not report RPM; omit the suffix instead of "(-1 rpm)".
				array2[3] = "%";
			}
			gpuFan = string.Concat(array2);
		}
		// ⭐⭐ 2026-10-16（0.291.9 · 同上）：⭐ GPU 风扇同样补一支"⭐ 只有 RPM"的显示 ✅
		//   ⚠️ 同样用 `else if` ⇒ ⭐ 只在没有占空比时才走 ⇒ ⭐ GCU 机型逐位不变 ✅
		else if (fanInfoFresh && HardwareControl.gpuFanRPM > 0)
		{
			gpuFan = Strings.FanSpeed + ": " + HardwareControl.gpuFanRPM + " rpm";
		}
		cpuFan = HardwareControl.midFan;
		if (cpuFan.HasValue)
		{
			string fanSpeed = Strings.FanSpeed;
			cpuFan = HardwareControl.midFan;
			midFan = fanSpeed + ": " + cpuFan;
		}
		string trayTip = "CPU" + text + " " + text2;
		if (gpuTemp.Length > 0)
		{
			trayTip = trayTip + "\nGPU" + gpuTemp + " " + gpuFan;
		}
		if (battery.Length > 0)
		{
			trayTip = trayTip + "\n" + battery;
		}
		if (IsHandleCreated && !IsDisposed)
		{
			if (InvokeRequired)
			{
				BeginInvoke(ApplySnapshot);
			}
			else
			{
				ApplySnapshot();
			}
		}
		void ApplySnapshot()
		{
			UpdatePerfRowStatus();
			if (GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
			{
				GpuRouteMonitor.Snapshot();
			}
			// 2026-10-13（修「显卡模式这一行 UI 不刷新」）：标签与按钮高亮必须同源同刻。
			// 标签每 2 秒重算一次，高亮过去只在交互时画一次 —— 固件稍后才把待生效路由落盘时，
			// 就会「标签已显示重启以应用、高亮还停在旧档」。这里按同一套判据重算，不一致才重画。
			if (GpuRowLayouts.UsesRouteReadback(_gpuRowLayout))
			{
				int wantedGpuMode = ResolveHighlightedGpuMode(-1);
				bool gpuPendingNow = HasPendingGpuRestart();
				if (gpuPendingNow)
				{
					int pendingTarget = GpuRestartVerifier.PendingTarget;
					if (pendingTarget >= 0)
					{
						wantedGpuMode = pendingTarget;
					}
				}
				if (wantedGpuMode != _lastVisualisedGpuMode || gpuPendingNow != _lastVisualisedGpuPending)
				{
					VisualiseGPUMode();
				}
			}
			string text3 = GpuRowStatusText();
			if (labelGPUFan.Text != text3)
			{
				labelGPUFan.Text = text3;
			}
			UpdateTelemetryText();
			UpdateGcuStatus();
			// 2026-10-15（限充真效果判据上屏 · 修法④）：借这条既有 2s 快照刷新判定标签（⛔ 不新增定时器、
			// ⛔ 不发起任何探测/写入 —— 只读 ChargeLimitEffectEvidence.Latest 这个内存快照）。
			RefreshChargeLimitEffectLabel();
			int? gpuFan2 = HardwareControl.gpuFan;
			if (gpuFan2.HasValue && AppConfig.NoGpu())
			{
				string text4 = "GPU" + gpuTemp + " " + gpuFan;
				if (labelMidFan.Text != text4)
				{
					labelMidFan.Text = text4;
				}
			}
			gpuFan2 = HardwareControl.midFan;
			if (gpuFan2.HasValue)
			{
				string text5 = "Mid " + midFan;
				if (labelMidFan.Text != text5)
				{
					labelMidFan.Text = text5;
				}
			}
			if (labelBattery.Text != battery)
			{
				labelBattery.Text = ((battery.Length > 0) ? battery : BatteryHealthText(Program.hw));
			}
			Color batteryColor = (batteryAbnormal ? UiVisualStyle.Danger : UiVisualStyle.Muted);
			if (labelBattery.ForeColor != batteryColor)
			{
				labelBattery.ForeColor = batteryColor;
			}
			if (!string.Equals(_lastBatteryTip, batteryTip, StringComparison.Ordinal))
			{
				_lastBatteryTip = batteryTip;
				toolTip.SetToolTip(labelBattery, (batteryTip.Length > 0) ? batteryTip : null);
			}
			if (!batteryMouseOver && !batteryFullMouseOver && labelCharge.Text != charge)
			{
				labelCharge.Text = charge;
			}
			// 2026-10-09（批次3 #10）：托盘提示文本走 Program.SetTrayTip —— 托盘还没建就只记下（建好时补上），
			// ⛔ 绝不为了让 tooltip 生效而把托盘提前建出来（那会把懒加载的收益又还回去）。
			Program.SetTrayTip(trayTip);
		}
	}

	public void ToggleOverlay(bool fromHotkey = false)
	{
		bool flag = !AppConfig.IsOverlay();
		AppConfig.Set("overlay", flag ? 1 : 0);
		Logger.WriteLine("Overlay " + (flag ? "On" : "Off") + (AppConfig.IsOverlayGameOnly() ? " (game only)" : ""));
		if (flag)
		{
			Program.hardwareOverlay?.StartOverlay();
		}
		else
		{
			Program.hardwareOverlay?.StopOverlay();
		}
		buttonOverlay.Activated = flag;
		UpdateFooterOverlayVisual(flag);
		if (fromHotkey && AppConfig.IsOverlayGameOnly())
		{
			Program.toast.RunToast(Strings.Overlay + " " + (flag ? Strings.On : Strings.Off));
		}
		SetContextMenu();
	}

	public void ToggleOverlayGameOnly()
	{
		AppConfig.Set("overlay_game_only", (!AppConfig.IsOverlayGameOnly()) ? 1 : 0);
		if (AppConfig.IsOverlay())
		{
			Program.hardwareOverlay?.StopOverlay();
			Program.hardwareOverlay?.StartOverlay();
		}
		SetContextMenu();
	}

	public void ShowMode(int mode)
	{
		// 2026-10-05: keep an open firmware-profile editor in sync with the active mode.
		// ShowMode() runs on every perf-UI refresh, so this covers mode switches.
		if (customModeForm != null && !customModeForm.IsDisposed && customModeForm.Visible && customModeForm.IsFirmwareMode)
		{
			int num = Program.service?.CurrentMode ?? mode;
			int num2 = ModeProfileConfig.Get(num);
			if (customModeForm.FirmwareVisualMode != num || customModeForm.FirmwareProfileIndex != num2)
			{
				customModeForm.BindFirmwareProfile(num, num2);
			}
		}
		if (InvokeRequired)
		{
			BeginInvoke(() =>
			{
				VisualiseMode(mode);
			});
		}
		else
		{
			VisualiseMode(mode);
		}
	}

	protected void VisualiseMode(int mode)
	{
		bool flag = mode == 1 && MechrevoService.IsSilentTurboActive;
		if (!ShouldRefreshVisualMode(_lastVisualMode, _lastVisualSilentTurbo, mode, flag))
		{
			return;
		}
		_lastVisualMode = mode;
		_lastVisualSilentTurbo = flag;
		Logger.WriteLine($"VisualiseMode({mode})");
		if (_gpuOcBox != null)
		{
			MechrevoHw? hw0 = Program.hw;
			_perfShortcutGpuOcIntent = mode == 1 && hw0 != null && MechrevoService.IsGcu50;
			_gpuOcBox.Visible = _perfShortcutGpuOcIntent;
			// 2026-10-11（本轮）：「显卡超频」的显隐变了 ⇒ 同步标题行那颗容器的宽度/可见性。
			SyncPerfShortcutsVisibility();
			// 2026-10-11（用户拍板「抬高窗口最小宽度」；2026-10-12 撤销该抬高后此处只是幂等兜底）：
			// 「显卡超频」也是标题行里的开关，显隐变化就重算下限（下限现在恒为 420 逻辑像素）。
			RefreshDashboardMinimumWidth();
		}
		buttonSilent.Activated = false;
		buttonBalanced.Activated = false;
		buttonTurbo.Activated = false;
		if (buttonSilentTurbo != null)
		{
			buttonSilentTurbo.Activated = false;
		}
		if (buttonCustomMode != null)
		{
			buttonCustomMode.Activated = false;
		}
		if (IsCustomVisualMode(mode))
		{
			if (buttonCustomMode != null)
			{
				buttonCustomMode.Activated = true;
			}
			UpdatePerfRowStatus();
			return;
		}
		switch (mode)
		{
		case 2:
			buttonSilent.Activated = true;
			break;
		case 1:
			if ((buttonSilentTurbo != null) & flag)
			{
				buttonSilentTurbo.Activated = true;
			}
			else
			{
				buttonTurbo.Activated = true;
			}
			break;
		case 0:
			buttonBalanced.Activated = true;
			break;
		}
		UpdatePerfRowStatus();
	}

	private void UpdatePerfRowStatus()
	{
		// 2026-10-11（UI 重排 · 用户原话「性能模式旁边的指示性能的文字删除」）：
		// 原 labelCPUFan 显示「当前模式名（如 平衡）」的指示逻辑与那颗标签本身已整体移除
		// （字段/设计器实例化/初始化赋值/本方法里的赋值全部删掉）。当前模式只由五颗模式按钮的
		// 高亮表达，本方法保留调用点，不再写任何文字、也不碰任何控件。
	}

	internal static bool IsCustomVisualMode(int mode)
	{
		return mode == 4;
	}

	internal static bool ShouldRefreshVisualMode(int currentMode, bool currentSilentTurbo, int nextMode, bool nextSilentTurbo)
	{
		if (currentMode == nextMode)
		{
			if (nextMode == 1)
			{
				return currentSilentTurbo != nextSilentTurbo;
			}
			return false;
		}
		return true;
	}

	public void SetModeLabel(string modeText)
	{
		if (InvokeRequired)
		{
			BeginInvoke(() =>
			{
				labelPerf.Text = Strings.PerformanceMode;
				panelPerformance.AccessibleName = modeText;
			});
		}
		else
		{
			labelPerf.Text = Strings.PerformanceMode;
			panelPerformance.AccessibleName = modeText;
		}
	}

	public void VisualiseGPUButtons(bool eco = true, bool ultimate = true)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				VisualiseGPUButtons(eco, ultimate);
			});
			return;
		}
		isMuxGpu = ultimate;
		isGpuSection = eco | ultimate;
		buttonEco.Visible = eco;
		buttonStandard.Visible = eco | ultimate;
		buttonUltimate.Visible = ultimate;
		if (menuEco != null)
		{
			menuEco.Visible = eco;
		}
		if (menuStandard != null)
		{
			menuStandard.Visible = eco | ultimate;
		}
		if (menuUltimate != null)
		{
			menuUltimate.Visible = ultimate;
		}
		buttonStopGPU.Visible = false;
		List<Control> list = new List<Control>();
		if (eco)
		{
			list.Add(buttonEco);
		}
		if (eco | ultimate)
		{
			list.Add(buttonStandard);
		}
		if (ultimate)
		{
			list.Add(buttonUltimate);
		}
		if (ReflowSingleRow(tableGPU, list, ref _lastGpuLayout))
		{
			ApplySegmentRow(list);
		}
	}

	public void HideGPUModes(bool gpuExists)
	{
		isGpuSection = false;
		buttonEco.Visible = false;
		buttonStandard.Visible = false;
		buttonUltimate.Visible = false;
		buttonStopGPU.Visible = false;
		tableGPU.ColumnCount = 0;
		SetContextMenu();
		panelGPU.Visible = gpuExists;
	}

	public void LockGPUModes(string text = null)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				LockGPUModes(text);
			});
			return;
		}
		if (text == null)
		{
			text = Strings.GPUMode + ": " + Strings.GPUChanging + " ...";
		}
		ButtonEnabled(buttonEco, enabled: false);
		ButtonEnabled(buttonStandard, enabled: false);
		ButtonEnabled(buttonUltimate, enabled: false);
		labelGPU.Text = Strings.GPUMode;
		labelTipGPU.Text = Strings.GPUChanging + " ...";
		labelTipGPU.Visible = true;
	}

	public void VisualiseGPUMode(int GPUMode = -1)
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
		bool flag2 = !IsReadOnlyDegraded;
		bool restartPending = HasPendingGpuRestart();
		if (restartPending)
		{
			int pendingTarget = GpuRestartVerifier.PendingTarget;
			if (pendingTarget >= 0)
			{
				// ⭐⭐ 2026-10-19（机主：切走再切回同一模式仍显示黄色高亮）：
				//   ⭐ 若"待生效目标" == **当前生效模式**（取源同上，⛔ 不受 hint 影响）⇒ 等于没变化 ⇒
				//      清掉 pending、**恢复蓝色**（⛔ 不再误显示黄色待重启）✅
				//   ⚠️ 只对宝龙达生效（GCU/同方待重启语义逐位不变）✅
				int currentEffectiveMode = ResolveHighlightedGpuMode(-1);
				if ((Program.hw?.UsesBaolongda ?? false) && pendingTarget == currentEffectiveMode)
				{
					ClearGpuRestartPending();
					restartPending = false;
					LogGpuModeHighlight(pendingTarget, currentEffectiveMode, pending: false);
				}
				else
				{
					num = pendingTarget;
				}
			}
		}
		ButtonEnabled(buttonEco, flag2);
		ButtonEnabled(buttonStandard, flag2);
		ButtonEnabled(buttonUltimate, flag2);
		toolTip.SetToolTip(buttonEco, (Program.hw?.IgpuOnlyUiIsGuess ?? false) ? Strings.GpuRouteIgpuTip + "\r\n" + Strings.IgpuOnlyUnverifiedTip : Strings.GpuRouteIgpuTip);
		Color pendingColor = (restartPending ? UiVisualStyle.Warn : Color.Empty);
		buttonEco.ActivatedColor = pendingColor;
		buttonStandard.ActivatedColor = pendingColor;
		buttonUltimate.ActivatedColor = pendingColor;
		// 2026-10-09（UI 反馈①，仅夜间）：**「待生效＝棕」保持棕色不变**（用户明确要求"若认为应一律灰，
		// 不要自行决定，保留现状并提问"）—— 只有 restartPending 时这行才用下发色画描边（棕），
		// 非待生效时 ActivatedColor 为空且本标志为 false ⇒ 选中态走强调蓝高亮（2026-10-09 UI 反馈② 起；
		// 反馈① 时那档是中性灰 Muted 158，本轮只换边色，底仍 ButtonFace 62 不浅填充）。
		buttonEco.SelectionUsesAssignedAccent = restartPending;
		buttonStandard.SelectionUsesAssignedAccent = restartPending;
		buttonUltimate.SelectionUsesAssignedAccent = restartPending;
		buttonEco.Activated = num == 0;
		buttonStandard.Activated = num == 1;
		buttonUltimate.Activated = num == 2;
		// 2026-10-13：记下这一次真正画上去的状态（周期刷新据此决定要不要重画，见 ApplySnapshot）
		_lastVisualisedGpuMode = num;
		_lastVisualisedGpuPending = restartPending;
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
				menuEco.Enabled = true;
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
		// 2026-10-06 D-1：判据不可靠的机型（40 系）上，这个「核显模式」按钮是按代理规则猜出来的
		// —— 加一句提示，让用户知道"可以试一次，试过之后会记住结果"。
		// 用户一旦明确表态（有/没有、拨开关）或真试过一次，IgpuOnlyUiIsGuess 变 false，这句自动消失。
		if (Program.hw?.IgpuOnlyUiIsGuess ?? false)
		{
			return Strings.IgpuOnlyUnverifiedHint;
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
	}

	/// <summary>
	/// 「核显省电」行三颗按钮的统一入口。2026-10-13（用户批准「连同强制一起做」）流程：
	/// <list type="number">
	/// <item>⛔ 早退条件一字未改（未连接 / 不支持热切换 ⇒ 什么都不发）；⭐ 另加一条：本行**只在混合模式下显示**
	/// （<see cref="IgpuSavingRowVisible" />），隐藏时按钮根本点不到，但机型/GCU 状态可能刚好翻转，
	/// 所以这里再判一次"只有混合模式才允许下发"（⭐ 已经是核显模式再"省电"没意义，独显直连下更切不过去）；</item>
	/// <item>普通路：走 <see cref="MechrevoService.SetIGpuOnlySaveMode" />（⭐ 已有真实确认判据）；</item>
	/// <item>失败 ⇒ ⭐ **提示**（旧实现把失败静默吞掉）+ 重新问一次（<see cref="MechrevoService.RecheckIgpuOnlySaveModeAsync" />）
	/// 让高亮反映真实状态；拿不到新包时按 <c>DgpuRecheckFailed</c> 提示、并让本次作废；</item>
	/// <item>失败且读到有程序占用独显 ⇒ 进入强制路（列名单 → 用户第二次动作确认 → 提权结束 → 重试）；</item>
	/// <item>仍然没成 ⇒ ⭐ **回滚**（把核显省电恢复成下发前那个"已知"取值，见
	/// <see cref="IgpuOnlySaveModeDecision.ShouldRollback" />）。</item>
	/// </list>
	/// </summary>
	public async Task SwitchIgpuSavingAsync(string mode)
	{
		if (_syncingIgpuSaving)
		{
			return;
		}
		MechrevoService svc = Program.service;
		MechrevoHw hw = Program.hw;
		if (svc is null || hw is null || !hw.IsConnected || !hw.SupportsGpuHotSwap)
		{
			return;
		}
		_syncingIgpuSaving = true;
		// ⚠️ 两个"本次点击有效"的标记在这里归零，finally 再归零一次：⛔ 绝不让上一次的同意跨到这一次。
		_igpuSavingForceConfirmedThisAttempt = false;
		_igpuSavingDisplayOverrideThisAttempt = false;
		try
		{
			int previousMode = hw.IgpuOnlySaveMode;
			bool switchOk = await svc.SetIGpuOnlySaveMode(mode);
			bool forcedAttempt = false;
			if (!switchOk)
			{
				// ⭐ 优先复用既有设施：NVML + nvidia-smi 合并的占用名单，再过保护/白名单过滤。
				IReadOnlyList<DgpuApplication> occupants = TryReadDgpuOccupants(out bool snapshotAvailable);
				IReadOnlyList<DgpuApplication> closable = snapshotAvailable
					? DgpuApplicationSafety.FilterClosable(occupants, Environment.ProcessId, Process.GetCurrentProcess().SessionId)
					: (IReadOnlyList<DgpuApplication>)Array.Empty<DgpuApplication>();
				DisplayRouteSnapshot display = await WmiDisplayRouteProbe.ProbeAsync();
				GpuScenarioVerdict verdict = GpuScenarioGuard.Evaluate(display, snapshotAvailable, closable.Count > 0, _igpuSavingForceConfirmedThisAttempt, _igpuSavingDisplayOverrideThisAttempt);
				Logger.WriteLine($"SwitchIgpuSaving({mode}) notConfirmed={!switchOk}: route={WmiDisplayRouteProbe.Describe(display)} occupants={closable.Count} snapshot={snapshotAvailable} verdictRefusal={verdict.RefusalReason} needsForceConfirm={verdict.NeedsForceConfirmation}");
				if (verdict.Refused)
				{
					PromptIgpuSavingRefused(verdict, display, closable);
				}
				else if (verdict.NeedsForceConfirmation)
				{
					// ⭐ 安全门 (d)：一定**先列名单**、再由用户主动做第二次动作，⛔ 绝不静默结束任何进程。
					_igpuSavingForceConfirmedThisAttempt = PromptIgpuSavingForceConfirm(closable, display, verdict);
					if (_igpuSavingForceConfirmedThisAttempt)
					{
						// 重新判一次：这次用"已确认"的标记，等价于 GpuScenarioGuard.Evaluate 的第 ④ 分支。
						GpuScenarioVerdict confirmedVerdict = GpuScenarioGuard.Evaluate(display, snapshotAvailable, occupied: true, forceConfirmed: true, _igpuSavingDisplayOverrideThisAttempt);
						if (!confirmedVerdict.Refused && confirmedVerdict.ForceAllowed)
						{
							forcedAttempt = await TryForceIgpuSavingAsync(svc, mode, closable);
							switchOk = forcedAttempt;
						}
					}
				}
			}
			if (switchOk)
			{
				await RefreshIgpuSavingUiAsync();
				VisualiseIgpuSaving();
			}
			else
			{
				// ⭐ 失败提示（旧实现静默吞掉；⛔ 同一件事只弹一条 toast）+ ⭐ 「重新问一次」把高亮对齐真实状态。
				await RefreshIgpuSavingUiAsync();
				bool fresh = await svc.RecheckIgpuOnlySaveModeAsync();
				if (!fresh)
				{
					Logger.WriteLine("SwitchIgpuSaving: the recheck after the attempt returned no fresh packet");
					ToastForm.ShowFailure(Strings.DgpuRecheckFailed);
				}
				else
				{
					VisualiseIgpuSaving();
					ToastForm.ShowFailure(Strings.DgpuStuckCancelled);
				}
				// ⭐ 阶段 3（v）回滚：对齐只读参考的做法（Invoke-DgpuSilence.ps1:1003 的自动 enable 回滚 /
				// Invoke-GpuHotSwitch.ps1:354-364 的自动回滚+复核）—— 未确认就把核显省电恢复成下发前的已知取值。
				if (IgpuOnlySaveModeDecision.ShouldRollback(commandSent: true, confirmed: false, previousMode))
				{
					string previousName = IgpuOnlySaveModeDecision.ActionSuffix(previousMode) ?? "";
					Logger.WriteLine($"SwitchIgpuSaving rollback: restoring igpu-only save mode to {previousName}({previousMode}) after forcedAttempt={forcedAttempt}");
					bool restored = await svc.SetIGpuOnlySaveMode(previousName);
					await svc.RecheckIgpuOnlySaveModeAsync();
					await RefreshIgpuSavingUiAsync();
					VisualiseIgpuSaving();
					if (!restored)
					{
						Logger.WriteLine("SwitchIgpuSaving rollback was not confirmed by the hardware");
						ToastForm.ShowFailure(Strings.DgpuStuckCancelled);
					}
				}
			}
		}
		finally
		{
			_igpuSavingForceConfirmedThisAttempt = false;
			_igpuSavingDisplayOverrideThisAttempt = false;
			_syncingIgpuSaving = false;
		}
	}

	/// <summary>
	/// ⭐ 阶段 2 第 3 条：占用名单来源 —— **优先复用已有设施**
	/// <see cref="NvidiaGpuControl.TryGetActiveDgpuApplications" />（NVML <c>GetActiveApplications</c>
	/// + <c>nvidia-smi --query-compute-apps</c> 合并）。
	/// <para>⚠️ 已知覆盖缺口（⭐ 我的决定，见报告）：D3D/游戏类占用**只有** Windows GPU 性能计数器
	/// （<c>Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory/GPUEngine</c>）能看到
	/// （只读参考 <c>Clear-DgpuOccupants.ps1:77-116</c> 的 <c>Get-GpuMemoryRows</c>/<c>Get-GpuEngineRows</c>）。
	/// 那部分要 ~200 行 C# 解析 <c>pid_&lt;n&gt;_luid_0x…</c> 并按"dedicated 最大的 LUID = 独显"挑 LUID，
	/// ⛔ 本轮**不做**：本机没有可复现的 D3D 占用场景、离线无法验证，写了就是"看起来能用的死代码"。
	/// 因此本函数的<b>覆盖口径</b>只到 NVML/nvidia-smi，⚠️ 漏检时表现为"NVML 说没人占用、
	/// 但切换仍被 GCU 拒"，此时走的是下面 <c>DgpuStillInUsePrompt</c> 的"仍在使用"分支。</para>
	/// <para><paramref name="available" /> 为假 = 快照不可信（NVML 报错 / 有进程读不到启动时间），
	/// ⇒ 上层走 <c>DgpuSnapshotUnavailable</c>（失败关闭，⛔ 不猜）。</para>
	/// </summary>
	private static IReadOnlyList<DgpuApplication> TryReadDgpuOccupants(out bool available)
	{
		try
		{
			bool ok = NvidiaGpuControl.TryGetActiveDgpuApplications(out IReadOnlyList<DgpuApplication> applications);
			available = ok;
			return applications ?? Array.Empty<DgpuApplication>();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("dGPU occupant snapshot failed: " + ex.Message);
			available = false;
			return Array.Empty<DgpuApplication>();
		}
	}

	/// <summary>
	/// 失败后先把界面与读数对齐：<see cref="RefreshGpuModeUiStateAsync" /> 会重读 GCU 状态并跑
	/// <see cref="VisualiseGPUMode" />（其中就含 <see cref="VisualiseIgpuSaving" />）。
	/// </summary>
	private async Task RefreshIgpuSavingUiAsync()
	{
		try
		{
			await RefreshGpuModeUiStateAsync();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RefreshIgpuSavingUi failed: " + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 安全门 (b)(d)：**列名单 + 用户主动的第二次动作**。⛔ 返回 false 时上层什么都不做。
	/// <para>文案一律复用**已存在**的 resx 串（⛔ 不自己造新串）：
	/// 列名单与警告用 <see cref="Strings.DgpuStillInUsePrompt" />，需要提权时用
	/// <see cref="Strings.DgpuElevationPrompt" />，用户没确认就取消时用 <see cref="Strings.DgpuStuckCancelled" />。
	/// ⚠️ 「独显独家驱动显示」的**黑屏风险说明**确实没有现成文案 ⇒ 这是本次唯一新增的一条
	/// （<see cref="Strings.DgpuRiskDgpuDrivesDisplay" />，zh-CN + en + <c>Strings.cs</c> 三处同步；
	/// override 的"第二次确认"夹在它后面，见 <see cref="PromptIgpuSavingForceConfirm" />）。</para>
	/// </summary>
	private bool PromptIgpuSavingForceConfirm(IReadOnlyList<DgpuApplication> closable, DisplayRouteSnapshot display, GpuScenarioVerdict verdict)
	{
		try
		{
			if (closable.Count == 0)
			{
				// 理论到不了这里（verdict.NeedsForceConfirmation 要求 Occupied），但保持"绝不静默"。
				MessageBox.Show(Strings.DgpuSnapshotUnavailable, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return false;
			}
			string names = string.Join("\r\n", closable.Select((DgpuApplication a) => a.ProcessName));
			// ⚠️ 只读参考 Invoke-GpuHotSwitch.ps1:305-312 在「独显独家驱动显示」时是**硬拒绝**
			// （只有 -Force 才放行）。本函数就是那个显式的 override 关口：风险说明**放在名单之前**，
			// 让用户先看到黑屏/需重启的风险，再看要结束哪些程序。
			string body = verdict.DgpuOwnsOnlyActiveDisplay
				? Strings.DgpuRiskDgpuDrivesDisplay + "\r\n\r\n" + string.Format(Strings.DgpuStillInUsePrompt, names)
				: string.Format(Strings.DgpuStillInUsePrompt, names);
			if (MessageBox.Show(body, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				MessageBox.Show(Strings.DgpuStuckCancelled, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return false;
			}
			if (verdict.DgpuOwnsOnlyActiveDisplay)
			{
				// ⭐ override 必须"明确、充分告知"：这里再问一次，把"可能黑屏/需重启"单独说一遍。
				// ⚠️ 用户点"是"才算 override；点"否"= 放弃本次强制，什么都不做。
				if (MessageBox.Show(Strings.IgpuOnlyForceSecondConfirm, "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
				{
					MessageBox.Show(Strings.DgpuStuckCancelled, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
					return false;
				}
				_igpuSavingDisplayOverrideThisAttempt = true;
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Igpu saving force prompt failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>⭐ 安全门 (b)：拒绝时**必须说明原因**（文案复用现成串，⛔ 不静默）。</summary>
	private void PromptIgpuSavingRefused(GpuScenarioVerdict verdict, DisplayRouteSnapshot display, IReadOnlyList<DgpuApplication> closable)
	{
		try
		{
			string reason;
			if (verdict.RefusalReason == "dgpu-owns-only-active-display")
			{
				reason = Strings.DgpuRiskDgpuDrivesDisplay;
			}
			else if (verdict.RefusalReason == "occupancy-snapshot-unavailable")
			{
				reason = Strings.DgpuSnapshotUnavailable;
			}
			else
			{
				reason = Strings.DgpuRecheckFailed;
			}
			Logger.WriteLine($"Igpu saving refused: reason={verdict.RefusalReason} route={WmiDisplayRouteProbe.Describe(display)} occupants={closable.Count}");
			MessageBox.Show(reason, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Igpu saving refusal prompt failed: " + ex.Message);
		}
	}

	/// <summary>
	/// ⭐ 阶段 3 (iii)/(iv)：提权结束（<see cref="ElevatedProcessKiller.KillWithElevationAsync" />，
	/// 已存在、此前零调用；helper 入口见 <c>Program.cs:352</c> 的 <c>--kill-process</c>）
	/// ⇒ 再走一次普通路。
	/// <para>⚠️ 结束名单由 <see cref="DgpuForceKillPlanBuilder.TryPlan" /> 二次过滤（安全门 (a)：
	/// 保护名单 + 本进程树 + **%SystemRoot% 之下的一律不结束**；路径读不到也拒绝结束）。
	/// ⛔ 本方法本身不结束任何进程 —— 只把过滤后的名单交给已存在的提权 helper。</para>
	/// </summary>
	private async Task<bool> TryForceIgpuSavingAsync(MechrevoService svc, string mode, IReadOnlyList<DgpuApplication> closable)
	{
		GpuForceKillPlan plan = DgpuForceKillPlanBuilder.TryPlan(closable, Environment.ProcessId, Process.GetCurrentProcess().SessionId, _igpuSavingForceConfirmedThisAttempt, ReadProcessImagePath);
		Logger.WriteLine($"Igpu saving force: candidates={closable.Count} safe={plan.SafeToKill.Count} skipped={plan.Skipped.Count} refused={plan.RefusedReason ?? "-"}");
		if (plan.Refused || plan.SafeToKill.Count == 0)
		{
			if (plan.SafeToKill.Count == 0 && !plan.Refused)
			{
				// 名单里全是被保护的进程 ⇒ 不能结束任何一个，⭐ 说清楚而不是硬来。
				ToastForm.ShowFailure(Strings.DgpuStuckCancelled);
				Logger.WriteLine("Igpu saving force: every candidate is protected; nothing to end");
			}
			return false;
		}
		string names = string.Join("\r\n", plan.SafeToKill.Select((DgpuApplication a) => a.ProcessName));
		if (MessageBox.Show(string.Format(Strings.DgpuElevationPrompt, names), "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
		{
			return false;
		}
		(bool clean, string message) = await ElevatedProcessKiller.KillWithElevationAsync(plan.SafeToKill);
		Logger.WriteLine($"Igpu saving force: clean={clean} message={message}");
		if (!clean)
		{
			return false;
		}
		// ⭐ (iv) 再走阶段 1 的普通路。
		bool switched = await svc.SetIGpuOnlySaveMode(mode);
		Logger.WriteLine($"Igpu saving force retry: mode={mode} switched={switched}");
		await svc.RecheckIgpuOnlySaveModeAsync();
		return switched;
	}

	/// <summary>读进程映像路径（失败返回 <c>null</c> ⇒ 安全门按"不结束"处理）。⛔ 只读。</summary>
	private static string ReadProcessImagePath(DgpuApplication application)
	{
		try
		{
			using Process process = Process.GetProcessById(application.ProcessId);
			return process.MainModule?.FileName;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("dgpu-image-path", $"Could not read the image path of an occupant: {ex.Message}", 60000);
			return null;
		}
	}

	/// <summary>
	/// 「核显省电」行（混合模式下免重启切核显）**是否显示**。位置＝显卡模式按钮的下一行，
	/// 由 <c>_igpuSavingRow</c>（Dock=Bottom）挂在 panelGPU 上，行高与 Gen50 上完全一致（panelGPU 100 / 66）。
	/// <para>判据（与 2026-10-09「核显省电行不显示」修复时一字未改）：</para>
	/// <list type="number">
	/// <item><see cref="MechrevoHw.SupportsGpuHotSwap"/>——静态能力位为真，或（仅 50 系）GCU 实时裁定为真；</item>
	/// <item>⭐ <b>且当前处于混合模式</b>（<c>hw.GpuMode == 1</c>）—— 用户 2026-10-13 原话
	/// 「核显省电模式 是不是只在混合模式显示，其他模式不要显示」：已经是核显模式再"省电"没意义，
	/// 独显直连下根本切不过去（阶段 2 的安全门也会拒）；</item>
	/// <item>且没有待重启的显卡切换（<see cref="HasPendingGpuRestart"/>）。</item>
	/// </list>
	/// <para>2026-10-08（QA 开关）：末尾多挂一条**默认关闭**的旁路 <see cref="UiQaShowAll.Enabled"/>，
	/// 打开后仅把这一行**显示出来**给人看样式。⚠️ 它不改变任何既有定论 —— 2026-10-03
	/// 「40 系三模不显示核显省电」说的就是上面第 ① 条（本机 Gen40 的 <c>IgpuStatus=False</c>），
	/// 那条判据一字未动；开关只作用于这一行的 Visible 与 panelGPU 的高度。</para>
	/// <para>⚠️ 取值/命令一律不受影响：三颗按钮真正下发前的守卫仍在
	/// <see cref="SwitchIgpuSavingAsync"/>（<c>!hw.SupportsGpuHotSwap</c> 早退）；本开关打开后
	/// <see cref="MechrevoHw.SupportsGpuHotSwap"/> / <c>IgpuOnlySaveMode</c> 的返回值也完全不变。</para>
	/// <para>hw 为 null（没有设备对象）时不显示，与改动前一致。</para>
	/// </summary>
	private static bool IgpuSavingRowVisible(MechrevoHw hw)
	{
		if (hw == null)
		{
			return false;
		}
		if (hw.SupportsGpuHotSwap && hw.GpuMode == 1 && !HasPendingGpuRestart())
		{
			return true;
		}
		// QA 旁路：默认关闭（两项都不设时为 false ⇒ 与改动前逐项一致）。
		return UiQaShowAll.Enabled;
	}

	public void VisualiseIgpuSaving()
	{
		if (InvokeRequired)
		{
			Invoke(() => VisualiseIgpuSaving());
			return;
		}
		if (_igpuSavingOff == null)
		{
			return;
		}
		MechrevoHw? hw = Program.hw;
		// 2026-10-08（QA 开关）：与构造时的 canIgpuSaving 共用同一判据（原表达式一字未改），
		// 避免"构造处显示 / 刷新处又藏起来"两套逻辑走岔。
		bool showIgpuSaving = IgpuSavingRowVisible(hw);
		if (_igpuSavingRow != null)
		{
			_igpuSavingRow.Visible = showIgpuSaving;
		}
		if (panelGPU != null)
		{
			// 高度不再写死 D(100/66)：走 Panel_GPU_ApplyHeight（标题行块 D(60) + 模式行 D(34) + 可选核显省电行）
			Panel_GPU_ApplyHeight();
		}
		int mode = hw?.IgpuOnlySaveMode ?? 1;
		_igpuSavingOff.Activated = mode == 1;
		_igpuSavingOn.Activated = mode == 0;
		_igpuSavingAuto.Activated = mode == 3;
	}

	public void VisualiseIcon(bool themeChange = false)
	{
		// 2026-10-09（批次3 #10）：只对**已存在**的托盘换图标（⛔ 不为换图标创建托盘）。
		// 托盘是懒加载的 ⇒ 若它还没建，这里直接返回、**不消耗** _lIconSet：
		// 托盘创建时 EnsureTrayIcon() 本来就会从同一个 favicon.ico 建图标，效果与改动前一致。
		if (Program.ExistingTrayIcon == null || _lIconSet)
		{
			return;
		}
		_lIconSet = true;
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
			if (File.Exists(text))
			{
				Icon? icon = Program.ExistingTrayIcon.Icon;
				Program.ExistingTrayIcon.Icon = new Icon(text);
				icon?.Dispose();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("VisualiseIcon fail: " + ex.Message);
		}
	}

	private void PictureGPU_Click(object? sender, EventArgs e)
	{
		if (GPUModeControl.gpuError != null)
		{
			Process.Start(new ProcessStartInfo("devmgmt.msc")
			{
				UseShellExecute = true
			});
		}
	}

	private async void ButtonSilent_Click(object? sender, EventArgs e)
	{
		await SwitchPerformanceModeAsync(2);
	}

	private async void ButtonBalanced_Click(object? sender, EventArgs e)
	{
		await SwitchPerformanceModeAsync(0);
	}

	private async void ButtonTurbo_Click(object? sender, EventArgs e)
	{
		await SwitchPerformanceModeAsync(1, false);
	}

	/// <summary>2026-10-06：把「视觉模式号 + 静音狂暴子模式」映射成 PerfModeKind 并刷新模式状态。</summary>
	/// <remarks>
	/// 主界面模式按钮（SwitchPerformanceModeAsync）与托盘右键菜单（SetContextMenu）共用这一处映射，
	/// 因此两个入口最终都会走 PerfModeService.NotifyExternalSwitch() 同步 ActiveModeId / DisplayedModeId
	/// —— 托盘切模式不再滞后到下一次点主界面按钮才被纠正。
	/// 自定义模式（视觉 3）不在这里通知：它由 PerfModeService 按固件槽位归属解析（见 NotifyTrayCustomProfile）。
	/// </remarks>
	private static void NotifyPerfModeState(int displayMode, bool? silentTurbo)
	{
		PerfModeKind kind = displayMode switch
		{
			1 => (silentTurbo == true ? PerfModeKind.SilentTurbo : PerfModeKind.Turbo),
			2 => PerfModeKind.Silent,
			_ => PerfModeKind.Balanced
		};
		PerfModeService.Instance?.NotifyExternalSwitch(PerfModeDefinition.BuiltInId(kind));
	}

	/// <summary>2026-10-06：托盘「自定义档位」切换后按固件槽位归属刷新模式状态。</summary>
	/// <remarks>自定义模式 id 无法由视觉模式号推出，只能用已存在的 SlotOf() 反查归属；槽位无归属时不通知。</remarks>
	private static void NotifyTrayCustomProfile(int slotIndex)
	{
		PerfModeService perf = PerfModeService.Instance;
		if (perf == null)
		{
			return;
		}
		foreach (PerfModeDefinition mode in perf.Modes)
		{
			if (perf.SlotOf(mode.Id) == slotIndex)
			{
				perf.NotifyExternalSwitch(mode.Id);
				return;
			}
		}
	}

	private async Task SwitchPerformanceModeAsync(int mode, bool? silentTurbo = null)
	{
		MechrevoService mechrevoService = Program.service;
		if (mechrevoService == null)
		{
			return;
		}
		// 2026-10-09（30 系「模式 × 配置」失败可见化）：本方法是**用户主动切模式**的唯一入口
		// （主界面 4 个模式按钮 + 静音狂暴按钮 + 配置号下拉），先标一次「用户主动」，
		// 这样 SwitchMode 里那次 ProfileIndex 回读判定才会把结论交给 UI 弹提示。
		// ⚠️ 只影响「要不要弹这一次提示」，不改任何下发/取值/档位逻辑；
		//    AppConfig 启动重放等非用户发起模式切换的路径不会走到这里。
		mechrevoService.MarkUserModeSwitch();
		if (mode == 1)
		{
			MechrevoHw? hw = Program.hw;
			if (hw == null || !hw.Capabilities.TurboMode)
			{
				Logger.WriteLine("Turbo mode UI request rejected as unsupported by this device profile.");
				RefreshDeviceCapabilities();
				return;
			}
		}
		int request = Interlocked.Increment(ref _performanceRequest);
		ShowMode(MechrevoService.ToVisualMode(mode));
		if (mode == 1 && buttonSilentTurbo != null)
		{
			buttonSilentTurbo.Activated = silentTurbo == true;
			buttonTurbo.Activated = silentTurbo != true;
		}
		SetContextMenu();
		// ⭐⭐ 2026-10-19（「高性能电源」· **与切档同时下发**）：⭐ 机主口径「代码顺序 —— 直接跟着 GCU 发命令同时，不要等回读」
		//   ⭐ 因此在 `SwitchMode`（GCU/MQTT）**之前**就把 Windows 电源模式发出去，两边"同时"到 ✅
		//   ⚠️ 不阻塞：内部只调一次 powrprof，不等回读、不重试、不 sleep ✅
		//   ⚠️ 只在开关打开时生效（`WindowsPowerOverlayEnabled` 内部判）；失败只记日志、不影响切档 ✅
		try
		{
			MechrevoLite.Mode.PerfModeService.Instance?.ApplyWindowsPowerOverlayForUiMode(mode, silentTurbo == true);
		}
		catch (Exception exPow)
		{
			Logger.WriteLine("[power] UI 切档同步电源模式失败（已吞掉）：" + exPow.Message);
		}
		bool flag = await mechrevoService.SwitchMode(mode) == MechrevoService.ModeSwitchResult.Ok;
		if (request == Volatile.Read(in _performanceRequest))
		{
			MechrevoHw? hw2 = Program.hw;
			bool flag2 = hw2 != null && hw2.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported;
			if (flag && silentTurbo == true && !flag2)
			{
				flag = false;
			}
			else if ((flag && silentTurbo.HasValue) & flag2)
			{
				flag = await mechrevoService.SwitchTurboSubMode(silentTurbo.Value);
			}
			if (request == Volatile.Read(in _performanceRequest))
			{
				int displayMode = flag ? mode : await mechrevoService.RefreshCurrentModeAsync(mechrevoService.CurrentMode);
				bool actuallySucceeded = flag || displayMode == mode;
				ShowMode(MechrevoService.ToVisualMode(displayMode));
				SetContextMenu();
				ModeControl.NotifySwitchOutcome(actuallySucceeded, cancelled: false);
				// 2026-10-09（30 系「模式 × 配置」失败可见化）：模式切成功了，但这次下发的
				// ProfileIndex 若未被设备回读确认（LastProfileIndexReadback=Mismatch），**必须可见地告诉
				// 用户**，不能静默（否则用户以为配置号生效了）。⚠️ 只补一条提示与日志，不改任何档位逻辑；
				// 非「模式 × 配置」机型恒为 NotEvaluated ⇒ 这条分支永不进入，行为逐项不变。
				if (mechrevoService.LastProfileIndexReadback == MechrevoService.ProfileIndexReadback.Mismatch)
				{
					// 2026-10-09（实验开关验收）：带上来源标记 —— 开关（LMECHREVO_FORCE_30PERF）打开时
					// 为 " source=force30perf"，关闭时为空串 ⇒ 默认状态下这行日志与改动前逐字一致。
					// 只影响这一行日志文本，不改任何提示条件/档位逻辑。
					Logger.WriteLine($"[perf-profile] 提示用户：模式 {mode} 已切换，但配置号未被设备确认{MechrevoLite.Hardware.Force30Perf.LogSourceSuffix}");
					ToastForm.ShowFailure(Strings.PerfProfileIndexUnconfirmed);
				}
				if (displayMode >= 0 && displayMode <= 2)
				{
					// 2026-10-06：displayMode 是**视觉模式号**（0=平衡/游戏 1=狂暴/狂飙 2=静音/办公）。
					// 映射逻辑抽到 NotifyPerfModeState()，与托盘右键菜单（SetContextMenu）共用同一处
					// 编号约定，保证主界面与托盘两个切模式入口都会刷新 PerfModeStore.ActiveModeId。
					NotifyPerfModeState(displayMode, silentTurbo);
				}
			}
		}
	}

	public void ButtonEnabled(RButton but, bool enabled)
	{
		but.Enabled = enabled;
		but.BackColor = (but.Enabled ? Color.FromArgb(255, but.BackColor) : Color.FromArgb(100, but.BackColor));
	}

	private void InitQuickSwitchRefresh()
	{
		BuildQuickSwitchPanel();
		VisualiseBatteryTitle(BatteryControl.ResolveDisplayLimitPercent());
		buttonBatteryFull.Visible = false;
		Control control = Controls["panelQuickSwitch"];
		if (control != null)
		{
			Controls.SetChildIndex(panelPerformance, 9);
			Controls.SetChildIndex(Controls["panelLc"], 8);
			Controls.SetChildIndex(panelGPU, 7);
			Controls.SetChildIndex(control, 6);
			Controls.SetChildIndex(panelBattery, 5);
			Controls.SetChildIndex(Controls["panelBrightness"], 3);
		}
		panelStartup.Controls.Remove(checkStartup);
		labelVersion.Dock = DockStyle.Left;
		labelVersion.AutoSize = false;
		labelVersion.Size = new Size(140, labelVersion.Height);
		labelVersion.Font = UiVisualStyle.Font(8.5f);
		labelVersion.AutoEllipsis = true;
		// 2026-10-03：开机自启动开关移至设置对话框"系统"标题右侧，不再占用版本栏
		panelStartup.Visible = false;
		_quickSwitchStatusTimer.Tick += (object? _, EventArgs _) =>
		{
			UpdateQuickSwitches();
			SyncDisplayEffectRow();
			MechrevoHw hw = Program.hw;
			// ⭐⭐ 2026-10-18（宝龙达 Logo 灯「UI 与硬件状态不同步」根因②：界面没有周期消费）：
			//   `_quickSwitches` 里**没有** logolight —— LOGO 开关在「灯光」折叠组（`_logoPowerSw`），
			//   它的状态只由 `SyncLightRows()` 从 `hw.QuickSwitches["logolight"]` 读；而 `SyncLightRows`
			//   原来**只有** `RefreshDeviceCapabilities` 的能力布局发生变化时才会被调用 ⚠️
			//   ⇒ 启动「灯效恢复」点亮 LOGO 后，界面**没有任何路径**再把它拉回真值 ⇒ 一直显示"关"❌
			//   ⇒ ⭐ 这里按与「更多开关」**同一把尺子**（12 秒宽限）周期收敛 ✅：
			//     用户刚点过（宽限内）就不抢，宽限一过才按真值回拨（与 UpdateQuickSwitches 语义一致 ✅）。
			//   ⚠️ **只在宝龙达启用**：GCU / 同方的灯光行继续保持"仅能力刷新时同步"的既有行为
			//      （⛔ 不在本轮顺手改其它机型）✅
			// ⭐⭐ 2026-10-19（机主：宝龙达专属轮询不能在 GCU/同方机器上空转）：
			//   ⭐ 这一行是**周期任务门控**的唯一入口：`UsesBaolongda` 为假 ⇒ 连 `SyncLightRows()` 都不调用
			//      （⛔ 不读宝龙达 WMI、⛔ 不刷宝龙达日志）✅
			if ((hw?.UsesBaolongda ?? false) && (DateTime.Now - _lastQuickSwitchUi).TotalSeconds >= 12.0)
			{
				SyncLightRows();
			}
			if (hw != null && hw.IsConnected)
			{
				// ⚠️ 电池标题是**通用 UI**（GCU 机器也显示），不属于宝龙达专属轮询 ⇒ 保持原样 ✅
				VisualiseBatteryTitle(BatteryControl.ResolveDisplayLimitPercent());
			}
		};
		if (!Program.UiAuditMode)
		{
			// ⭐⭐ 2026-10-19（机主：界面刷新在关闭 UI 后应停止）：
			//   ⭐ 原来这里无条件 Start() ⇒ 隐藏到托盘后仍在跑（含 SyncLightRows/电池标题）
			//   ⭐ 改为**随可见性启停**：可见才启动，隐藏由 `SettingsForm_VisibleChanged` 统一 Stop ✅
			if (Visible)
			{
				_quickSwitchStatusTimer.Start();
			}
		}
	}

	public void VisualiseBatteryTitle(int limit)
	{
		labelBatteryTitle.Text = Strings.Battery;
		if (_batteryLimitValue != null)
		{
			if (!EcChargeLimit.IsSupportedLimit(limit))
			{
				_batteryLimitValue.Text = Strings.Unreadable;
			}
			else if (limit >= 100)
			{
				// 2026-10-08 17:00：50 系滑块行的数值标签按返工前显示数值本身（滑块在 100% 位＝不限制，
				// 与滑块位置一致，且 14% 宽的数值列放不下「充电上限已关闭」这句 7 字文案）；
				// 非 50 系的滑块数值标签不上屏，沿用上一轮「100% 不说成"上限 100%"」的如实措辞。
				_batteryLimitValue.Text = _gen50SliderRow ? limit + "%" : Strings.ChargeLimitOff;
			}
			else
			{
				_batteryLimitValue.Text = limit + "%";
			}
		}
	}

	internal static string BatteryHealthSuffix(MechrevoHw? hw)
	{
		if (hw == null)
		{
			return "";
		}
		List<string> list = new List<string>(3);
		if (hw.BatteryCycleCount >= 0)
		{
			list.Add(string.Format(Strings.BatteryCycles, hw.BatteryCycleCount));
		}
		if (HasMeaningfulCapacity(hw.BatteryCapacityText))
		{
			list.Add(hw.BatteryCapacityText);
		}
		if (hw.BatteryAbnormal)
		{
			list.Add(Strings.BatteryAbnormal);
		}
		if (list.Count != 0)
		{
			return "（" + string.Join(" · ", list) + "）";
		}
		return "";
	}

	internal static bool HasMeaningfulCapacity(string? capacityText)
	{
		if (string.IsNullOrWhiteSpace(capacityText))
		{
			return false;
		}
		string text = new string(capacityText.TakeWhile((char c) => char.IsDigit(c) || char.IsWhiteSpace(c)).Where(char.IsDigit).ToArray());
		if (text.Length > 0 && long.TryParse(text, out var result))
		{
			return result > 0;
		}
		return false;
	}

	internal static string BatteryHealthText(MechrevoHw? hw)
	{
		if (hw == null)
		{
			return "";
		}
		List<string> list = new List<string>(2);
		if (hw.BatteryCycleCount >= 0)
		{
			list.Add(string.Format(Strings.BatteryCycles, hw.BatteryCycleCount));
		}
		if (HasMeaningfulCapacity(hw.BatteryCapacityText))
		{
			list.Add(hw.BatteryCapacityText);
		}
		return string.Join(" · ", list);
	}

	private static BatteryDisplayInput CurrentBatteryDisplayInput()
	{
		PowerStatus powerStatus = SystemInformation.PowerStatus;
		float batteryLifePercent = powerStatus.BatteryLifePercent;
		int percent;
		if (batteryLifePercent >= 0f && batteryLifePercent <= 1f)
		{
			percent = (int)Math.Round(batteryLifePercent * 100f);
		}
		else
		{
			percent = ((HardwareControl.batteryCharge > 0) ? HardwareControl.batteryCharge : (-1));
		}
		MechrevoHw hw = Program.hw;
		return new BatteryDisplayInput(HardwareControl.PowerInput, powerStatus.PowerLineStatus == PowerLineStatus.Online, HardwareControl.BatteryStatus, HardwareControl.batteryRate, percent, BatteryControl.ResolveDisplayLimitPercent(), hw?.BatteryAbnormal ?? false, hw?.BatteryCycleCount ?? (-1), hw?.BatteryCapacityText);
	}

	private static string Watts(decimal watts)
	{
		return Math.Round(Math.Abs(watts), 1).ToString("0.0", CultureInfo.InvariantCulture);
	}

	internal static string PowerSourceHeadline(PowerInputKind kind, int? adapterWatts)
	{
		switch (kind)
		{
		case PowerInputKind.Battery:
			return Strings.PowerSourceBattery;
		case PowerInputKind.Barrel:
		case PowerInputKind.BarrelAndTypeC:
		{
			string result;
			if (adapterWatts.HasValue)
			{
				result = string.Format(Strings.PowerSourceBarrelWatts, PowerInputDecoder.DisplayAdapterWatts(adapterWatts.GetValueOrDefault()));
			}
			else
			{
				result = Strings.PowerSourceBarrel;
			}
			return result;
		}
		case PowerInputKind.TypeC:
			return Strings.PowerSourceTypeC;
		default:
			return Strings.PowerSourceExternal;
		}
	}

	// ⭐⭐ 2026-10-18（机主：限充生效时“未充电”误导）：⭐ 宝龙达限充停在阈值上的状态缓存（5s）✅
	private static long _bdHeldAtLimitTick;

	private static bool _bdHeldAtLimitCache;

	private static int _bdHeldAtLimitHigh = -1;

	// ⭐⭐ 2026-10-18（机主定稿）：⭐ 标题行状态摘要缓存（5s，避免电池刷新频繁读 EC）✅
	private static long _bdTitleLimitTick;

	private static int _bdTitleLimitHigh = -1;

	private static int _bdTitleLimitLow = -1;

	private static int _bdTitleLimitMode = -1;

	/// <summary>
	/// ⭐⭐ 2026-10-18：⭐ 判断“宝龙达限充生效且 SOC ≥ 停充阈值”✅。
	/// <para>判据：`0x83C0 == 0x48` + `0x83A1`（停充阈值）+ 当前 SOC；⛔ 读不到 EC 状态 ⇒ false
	/// （回退到普通“未在充电”文案，⛔ 不显示“限充中”）✅</para>
	/// <para>⚠️ 只有 `UsesBaolongda` 才会读 EC；结果缓存 5 s，避免电池刷新时频繁读 EC ✅</para>
	/// </summary>
	private static bool TryBaolongdaHeldAtLimit(int percent, out int high)
	{
		high = -1;
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.UsesBaolongda)
		{
			return false;
		}
		long now = Environment.TickCount64;
		if (now - _bdHeldAtLimitTick < 5000)
		{
			high = _bdHeldAtLimitHigh;
			return _bdHeldAtLimitCache;
		}
		if (!BatteryLimitWriter.TryReadState(out int currentHigh, out _, out int mode, out _))
		{
			_bdHeldAtLimitTick = now;
			_bdHeldAtLimitHigh = -1;
			_bdHeldAtLimitCache = false;
			return false;
		}
		bool held = mode == BatteryLimitWriter.ModeActive && percent >= 0 && currentHigh >= 1 && percent >= currentHigh;
		_bdHeldAtLimitTick = now;
		_bdHeldAtLimitHigh = currentHigh;
		_bdHeldAtLimitCache = held;
		high = currentHigh;
		return held;
	}

	internal static string BatteryStateText(in BatteryDisplayInput input)
	{
		if (input.RateWatts > (decimal?)0m)
		{
			return string.Format(Strings.BatteryStateCharging, Watts(input.RateWatts.Value));
		}
		if (input.RateWatts < (decimal?)0m)
		{
			return string.Format(Strings.BatteryStateDischarging, Watts(input.RateWatts.Value));
		}
		BatteryStatusReading status = input.Status;
		if (status == null)
		{
			return "";
		}
		if (status.IsCharging)
		{
			return Strings.BatteryStateChargingNoRate;
		}
		if (status.IsDischarging || !input.AcOnline)
		{
			return Strings.BatteryStateDischargingNoRate;
		}
		// ⭐⭐ 2026-10-18（机主：限充生效时“未充电”误导）：⭐ AC + 未充电 + 限充生效且 SOC≥停充阈值
		//   ⇒ 「已到停充阈值（限充中）」；⛔ EC 读不到 ⇒ 回退到普通“未在充电”✅
		if (input.AcOnline && input.Percent >= 0 && TryBaolongdaHeldAtLimit(input.Percent, out _))
		{
			return Strings.BatteryStateHeldAtBaolongdaLimit;
		}
		if (input.Percent >= 0)
		{
			// 2026-10-08（限充开关）：设了上限时，电量到上限附近一律说"已到充电上限"，
			// 不再说"已充满" —— 上限是 EC 侧的，充到上限就停，说"已充满"会和上限自相矛盾。
			int limit = input.Limit;
			if (limit >= 40 && limit < 100 && input.Percent >= limit - 5)
			{
				return Strings.BatteryStateHeldAtLimit;
			}
		}
		if (input.Percent >= 99)
		{
			return Strings.BatteryStateFull;
		}
		return Strings.BatteryStateIdle;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主定稿）：⭐ 标题行右侧的唯一状态摘要 —— 读 `0x83C0/0x83A1/0x83A2`（5s 缓存）；
	/// ⛔ 读不到 ⇒ 显示「未限制充电」，⛔ 不显示“限充中” ✅
	/// </summary>
	private static bool TryReadBaolongdaLimitForTitle(out int high, out int low, out int mode)
	{
		high = -1;
		low = -1;
		mode = -1;
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.UsesBaolongda)
		{
			return false;
		}
		long now = Environment.TickCount64;
		if (now - _bdTitleLimitTick < 5000)
		{
			high = _bdTitleLimitHigh;
			low = _bdTitleLimitLow;
			mode = _bdTitleLimitMode;
			return mode >= 0;
		}
		bool ok = BatteryLimitWriter.TryReadState(out high, out low, out mode, out _);
		_bdTitleLimitTick = now;
		_bdTitleLimitHigh = ok ? high : -1;
		_bdTitleLimitLow = ok ? low : -1;
		_bdTitleLimitMode = ok ? mode : -1;
		return ok;
	}

	internal static string BatteryHeadlineText(in BatteryDisplayInput input)
	{
		// ⭐⭐ 2026-10-18（机主定稿布局）：⭐ 宝龙达电池页的**状态摘要唯一位置 = 标题行右侧**：
		//   「限制充电中（停充 X% / 起充 Y%）」/「未限制充电」；⛔ 不再显示“外接电源·未充电”那串 ✅
		if (Program.hw?.UsesBaolongda == true && BatteryLimitPanel.IsSupportedBoard(Program.hw))
		{
			if (TryReadBaolongdaLimitForTitle(out int titleHigh, out int titleLow, out int titleMode))
			{
				return titleMode == BatteryLimitWriter.ModeActive
					? string.Format(Strings.BatteryLimitTitleActiveFormat, titleHigh, titleLow)
					: Strings.BatteryLimitTitleInactive;
			}
			return Strings.BatteryLimitTitleInactive;
		}
		PowerInputKind kind = PowerInputDecoder.Reconcile(input.Power, input.AcOnline);
		List<string> list = new List<string>(4) { PowerSourceHeadline(kind, input.Power?.AdapterWatts) };
		string text = BatteryStateText(in input);
		if (text.Length > 0)
		{
			list.Add(text);
		}
		if (input.Abnormal)
		{
			list.Add(Strings.BatteryAbnormalTag);
		}
		// 2026-10-08（限充开关）：限充在上限处停充，LM 电池区直接列出「上限 X% · 当前 xx%」，
		// 让人看到自己设了多少（2026-10-08 19:30 定稿：不再提系统读数）。
		if (input.Limit >= 40 && input.Limit < 100 && input.Percent >= 0)
		{
			list.Add(string.Format(Strings.ChargeLimitStateLine, input.Limit, input.Percent));
		}
		return string.Join(" · ", list);
	}

	internal static string BatteryDetailsTooltip(in BatteryDisplayInput input)
	{
		PowerInputKind kind = PowerInputDecoder.Reconcile(input.Power, input.AcOnline);
		PowerInputSample power = input.Power;
		List<string> list = new List<string>(6);
		string text;
		switch (kind)
		{
		case PowerInputKind.Barrel:
			text = Strings.PowerSourceBarrel;
			break;
		case PowerInputKind.TypeC:
			text = Strings.PowerSourceTypeC;
			break;
		case PowerInputKind.BarrelAndTypeC:
			text = Strings.PowerSourceBarrelAndTypeC;
			break;
		case PowerInputKind.Battery:
			text = null;
			break;
		default:
			text = Strings.PowerSourceExternal;
			break;
		}
		if (text != null)
		{
			if (power != null && power.Kind == kind && power.EcReadable)
			{
				list.Add(string.Format(Strings.PowerTipSource, text, power.ComplexStatusRaw));
			}
			else if (power != null && power.Kind == kind && power.DecodeEnabled)
			{
				list.Add(Strings.PowerTipSourceUnknown);
			}
			else
			{
				list.Add(string.Format(Strings.PowerTipSourcePlain, text));
			}
			if (kind == PowerInputKind.BarrelAndTypeC)
			{
				list.Add(Strings.PowerTipDualUnverified);
			}
			bool isBarrel = kind == PowerInputKind.Barrel || kind == PowerInputKind.BarrelAndTypeC;
			if (isBarrel && power != null && power.BiosInfo3Raw >= 0)
			{
				int? adapterWatts = power.AdapterWatts;
				list.Add(adapterWatts.HasValue ? string.Format(Strings.PowerTipAdapter, PowerInputDecoder.DisplayAdapterWatts(adapterWatts.GetValueOrDefault())) : string.Format(Strings.PowerTipAdapterUnknown, power.BiosInfo3Raw & 0x78));
			}
		}
		List<string> batteryLines = new List<string>(2);
		if (input.Percent >= 0)
		{
			batteryLines.Add(string.Format(Strings.BatteryLevelLine, input.Percent));
		}
		BatteryStatusReading status = input.Status;
		if (status != null)
		{
			uint voltageMillivolts = status.VoltageMillivolts;
			if (voltageMillivolts != 0 && voltageMillivolts < uint.MaxValue)
			{
				batteryLines.Add(string.Format(Strings.BatteryVoltageLine, ((decimal)status.VoltageMillivolts / 1000m).ToString("0.00", CultureInfo.InvariantCulture)));
			}
		}
		if (batteryLines.Count > 0)
		{
			list.Add(string.Join(" · ", batteryLines));
		}
		if (input.CycleCount >= 0)
		{
			list.Add(string.Format(Strings.BatteryCycleCountLine, input.CycleCount));
		}
		if (HasMeaningfulCapacity(input.CapacityText))
		{
			list.Add(string.Format(Strings.BatteryCapacityLine, input.CapacityText.Trim()));
		}
		// 2026-10-08（限充开关 19:30 定稿）：只说明"上限是多少 / 现在多少"，不再提系统显示。
		if (input.Limit >= 40 && input.Limit < 100)
		{
			list.Add(string.Format(Strings.ChargeLimitStateLine, input.Limit, (input.Percent >= 0) ? input.Percent : 0));
			if (input.Percent >= input.Limit)
			{
				list.Add(Strings.ChargeLimitWindowsNote);
			}
		}
		if (input.Abnormal)
		{
			list.Add(Strings.BatteryAbnormalTag);
		}
		return string.Join("\n", list);
	}

	internal static string BatteryRateText(decimal? rate)
	{
		if (rate < (decimal?)0m)
		{
			return Strings.Discharging + ": " + Math.Round(-rate.Value, 1) + "W";
		}
		if (rate > (decimal?)0m)
		{
			return Strings.Charging + ": " + Math.Round(rate.Value, 1) + "W";
		}
		return "";
	}

	public void VisualiseBattery(int limit)
	{
		if (InvokeRequired)
		{
			Invoke(() =>
			{
				VisualiseBattery(limit);
			});
			return;
		}
		if (!EcChargeLimit.IsSupportedLimit(limit))
		{
			VisualiseBatteryUnknown();
			return;
		}
		VisualiseBatteryTitle(limit);
		sliderBattery.Value = Math.Clamp(limit, sliderBattery.Minimum, sliderBattery.Maximum);
		sliderBattery.AccessibleName = Strings.BatteryChargeLimit + ": " + limit + "%";
		VisualiseBatteryFull();
	}

	public void VisualiseBatteryUnknown()
	{
		if (InvokeRequired)
		{
			Invoke(VisualiseBatteryUnknown);
			return;
		}
		if (_batteryLimitValue != null)
		{
			_batteryLimitValue.Text = Strings.Unreadable;
		}
		sliderBattery.AccessibleName = Strings.BatteryChargeLimit + ": " + Strings.Unreadable;
		VisualiseBatteryFull();
	}

	public void VisualiseBatteryFull()
	{
		if (InvokeRequired)
		{
			Invoke(VisualiseBatteryFull);
			return;
		}
		// 2026-10-08（充电上限 UI 返工）：每次写入被确认/回退后都按 EC 回读重同步开关与档位高亮，
		// 避免"开关开着但寄存器是关的"或高亮停在旧档位。滑块已不参与显示。
		RefreshBatteryModeButtons();
		if (BatteryControl.chargeFull)
		{
			buttonBatteryFull.BackColor = RForm.colorStandard;
			buttonBatteryFull.ForeColor = SystemColors.ControlLightLight;
			buttonBatteryFull.AccessibleName = Strings.BatteryChargeLimit + "100% on";
		}
		else
		{
			buttonBatteryFull.BackColor = RForm.buttonSecond;
			buttonBatteryFull.ForeColor = SystemColors.ControlDark;
			buttonBatteryFull.AccessibleName = Strings.BatteryChargeLimit + "100% off";
		}
	}

	public void UpdateKeyboardLabel()
	{
		labelKeyboard.Text = Strings.LaptopKeyboard;
	}

	public void VisualiseFnLock()
	{
		buttonFnLock.BackColor = RForm.buttonSecond;
		buttonFnLock.ForeColor = SystemColors.ControlDark;
		buttonFnLock.AccessibleName = "Fn-Lock off";
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			DisposeRuntimeResources();
			Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			components?.Dispose();
		}
		base.Dispose(disposing);
	}

	// 2026-10-09（多显示器刷新率修复）：外接屏接入/断开或显示配置变化（含 MUX/分辨率/刷新率变化）时，
	// GCU 不一定会重推 GPUDevice/Status —— 刷新率行会一直停在"上一块屏"的档位与当前值上。
	// 这里在显示配置变化后立刻用系统侧实测重新校正档位/当前值，再重画刷新率这一行。
	// 只"重读状态"，不下发任何显示/刷新率指令；读不到内屏时 hw 侧不改动，保持既有护栏。
	private void OnDisplaySettingsChanged(object? sender, EventArgs e)
	{
		try
		{
			BeginInvoke((Action)(() =>
			{
				if (IsDisposed)
				{
					return;
				}
				Program.hw?.RefreshRefreshRateFromActiveDisplay();
				// 2026-10-09（"双屏同开一次后刷新率永久不正确"根因修复 · 纠正侧）：
				// 显示配置变化（外接屏接入/断开、MUX/分辨率切换）后，顺手核对 config 里已落盘的
				// screen_max / max_frequency / frequency 是否被别的屏污染；只在能确定 eDP 内屏时才纠正。
				MechrevoLite.Display.ScreenControl.ReconcileScreenMetrics("DisplaySettingsChanged");
				SyncHzButtons();
				// ⭐⭐ 2026-10-19（机主：显示器变化后亮度也要刷新）：
				//   ⭐ 显示配置变化（内屏/外接屏接入、MUX/分辨率切换）后**只读**重读一次系统亮度；
				//      ⛔ 不写硬件、不影响刷新率那条逻辑 ✅
				SyncBrightnessFromSystemReadOnly(force: true);
			}));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("DisplaySettingsChanged refresh rate row failed: " + ex.Message);
		}
	}

	private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
	{
		if (!UiVisualStyle.FollowSystemTheme)
		{
			return;
		}
		if (e.Category == Microsoft.Win32.UserPreferenceCategory.General || e.Category == Microsoft.Win32.UserPreferenceCategory.Color)
		{
			BeginInvoke((Action)(() =>
			{
				if (!IsDisposed)
				{
					ApplyThemeMode(UiVisualStyle.WindowsDarkTheme());
				}
			}));
		}
	}

	private static void ApplyRoundedRegion(Control control, int radius)
	{
		int w = control.ClientSize.Width;
		int h = control.ClientSize.Height;
		if (w <= 0 || h <= 0)
		{
			return;
		}
		using System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
		int r = Math.Min(radius, Math.Min(w, h));
		if (r <= 0)
		{
			control.Region = new Region(new Rectangle(0, 0, w, h));
			return;
		}
		path.AddArc(0, 0, r, r, 180, 90);
		path.AddArc(w - r, 0, r, r, 270, 90);
		path.AddArc(w - r, h - r, r, r, 0, 90);
		path.AddArc(0, h - r, r, r, 90, 90);
		path.CloseFigure();
		control.Region = new Region(path);
	}
	private void InitializeComponent()
	{
		this.components = new System.ComponentModel.Container();
		this.toolTip = new System.Windows.Forms.ToolTip(this.components);
		this.panelBattery = new MechrevoLite.UI.BufferedPanel();
		this.buttonBatteryFull = new MechrevoLite.UI.RButton();
		this.sliderBattery = new MechrevoLite.UI.RSlider();
		this.panelBatteryTitle = new System.Windows.Forms.Panel();
		this.labelBattery = new System.Windows.Forms.Label();
		this.pictureBattery = new System.Windows.Forms.PictureBox();
		this.labelBatteryTitle = new System.Windows.Forms.Label();
		this.panelFooter = new System.Windows.Forms.Panel();
		this.tableButtons = new System.Windows.Forms.TableLayoutPanel();
		this.buttonDonate = new MechrevoLite.UI.RBadgeButton();
		this.buttonQuit = new MechrevoLite.UI.RButton();
		this.buttonUpdates = new MechrevoLite.UI.RButton();
		this.checkStartup = new MechrevoLite.UI.RCheckBox();
		this.panelPerformance = new MechrevoLite.UI.BufferedPanel();
		this.tablePerf = new MechrevoLite.UI.BufferedTableLayoutPanel();
		this.buttonSilent = new MechrevoLite.UI.RButton();
		this.buttonBalanced = new MechrevoLite.UI.RButton();
		this.buttonTurbo = new MechrevoLite.UI.RButton();
		this.panelCPUTitle = new MechrevoLite.UI.BufferedPanel();
		this.picturePerf = new System.Windows.Forms.PictureBox();
		this.labelPerf = new System.Windows.Forms.Label();
		this.panelGPU = new MechrevoLite.UI.BufferedPanel();
		this.labelTipGPU = new System.Windows.Forms.Label();
		this.tableAMD = new System.Windows.Forms.TableLayoutPanel();
		this.buttonOverlay = new MechrevoLite.UI.RButton();
		this.tableGPU = new MechrevoLite.UI.BufferedTableLayoutPanel();
		this.buttonStopGPU = new MechrevoLite.UI.RButton();
		this.buttonEco = new MechrevoLite.UI.RButton();
		this.buttonStandard = new MechrevoLite.UI.RButton();
		this.buttonUltimate = new MechrevoLite.UI.RButton();
		this.panelGPUTitle = new System.Windows.Forms.Panel();
		this.pictureGPU = new System.Windows.Forms.PictureBox();
		this.labelGPU = new System.Windows.Forms.Label();
		this.labelGPUFan = new System.Windows.Forms.Label();
		this.panelScreen = new MechrevoLite.UI.BufferedPanel();
		this.labelTipScreen = new System.Windows.Forms.Label();
		this.tableScreen = new MechrevoLite.UI.BufferedTableLayoutPanel();
		this.buttonScreenAuto = new MechrevoLite.UI.RButton();
		this.button60Hz = new MechrevoLite.UI.RButton();
		this.button120Hz = new MechrevoLite.UI.RButton();
		this.buttonMiniled = new MechrevoLite.UI.RButton();
		this.buttonFHD = new MechrevoLite.UI.RButton();
		this.panelScreenTitle = new System.Windows.Forms.Panel();
		this.labelMidFan = new System.Windows.Forms.Label();
		this.pictureScreen = new System.Windows.Forms.PictureBox();
		this.labelSreen = new System.Windows.Forms.Label();
		this.panelKeyboard = new MechrevoLite.UI.BufferedPanel();
		this.labelBacklight = new System.Windows.Forms.Label();
		this.tableLayoutKeyboard = new System.Windows.Forms.TableLayoutPanel();
		this.buttonKeyboard = new MechrevoLite.UI.RButton();
		this.buttonKeyboardColor = new MechrevoLite.UI.RColorButton();
		this.comboKeyboard = new MechrevoLite.UI.RComboBox();
		this.panelKeyboardTitle = new System.Windows.Forms.Panel();
		this.buttonFnLock = new MechrevoLite.UI.RButton();
		this.pictureKeyboard = new System.Windows.Forms.PictureBox();
		this.labelKeyboard = new System.Windows.Forms.Label();
		this.panelStartup = new MechrevoLite.UI.BufferedPanel();
		this.labelCharge = new System.Windows.Forms.Label();
		this.panelPeripherals = new MechrevoLite.UI.BufferedPanel();
		this.tableLayoutPeripherals = new System.Windows.Forms.TableLayoutPanel();
		this.buttonPeripheral3 = new MechrevoLite.UI.RButton();
		this.buttonPeripheral2 = new MechrevoLite.UI.RButton();
		this.buttonPeripheral1 = new MechrevoLite.UI.RButton();
		this.panelVersion = new MechrevoLite.UI.BufferedPanel();
		this.buttonEnergySaver = new MechrevoLite.UI.RButton();
		this.buttonAmdOled = new MechrevoLite.UI.RButton();
		this.labelVersion = new System.Windows.Forms.Label();
		this.buttonHDRControl = new MechrevoLite.UI.RButton();
		this.panelBattery.SuspendLayout();
		this.panelBatteryTitle.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBattery).BeginInit();
		this.panelFooter.SuspendLayout();
		this.tableButtons.SuspendLayout();
		this.panelPerformance.SuspendLayout();
		this.tablePerf.SuspendLayout();
		this.panelCPUTitle.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.picturePerf).BeginInit();
		this.panelGPU.SuspendLayout();
		this.tableAMD.SuspendLayout();
		this.tableGPU.SuspendLayout();
		this.panelGPUTitle.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureGPU).BeginInit();
		this.panelScreen.SuspendLayout();
		this.tableScreen.SuspendLayout();
		this.panelScreenTitle.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureScreen).BeginInit();
		this.panelKeyboard.SuspendLayout();
		this.tableLayoutKeyboard.SuspendLayout();
		this.panelKeyboardTitle.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureKeyboard).BeginInit();
		this.panelStartup.SuspendLayout();
		this.panelPeripherals.SuspendLayout();
		this.tableLayoutPeripherals.SuspendLayout();
		this.panelVersion.SuspendLayout();
		base.SuspendLayout();
		this.panelBattery.AutoSize = true;
		this.panelBattery.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelBattery.Controls.Add(this.buttonBatteryFull);
		this.panelBattery.Controls.Add(this.sliderBattery);
		this.panelBattery.Controls.Add(this.panelBatteryTitle);
		this.panelBattery.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelBattery.Location = new System.Drawing.Point(11, 1683);
		this.panelBattery.Margin = new System.Windows.Forms.Padding(0);
		this.panelBattery.Name = "panelBattery";
		this.panelBattery.Padding = new System.Windows.Forms.Padding(20, 15, 20, 0);
		this.panelBattery.Size = new System.Drawing.Size(827, 104);
		this.panelBattery.TabIndex = 8;
		this.buttonBatteryFull.Activated = false;
		this.buttonBatteryFull.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.buttonBatteryFull.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonBatteryFull.BorderColor = System.Drawing.Color.Transparent;
		this.buttonBatteryFull.BorderRadius = 2;
		this.buttonBatteryFull.FlatAppearance.BorderSize = 0;
		this.buttonBatteryFull.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonBatteryFull.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f, System.Drawing.FontStyle.Bold);
		this.buttonBatteryFull.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.buttonBatteryFull.Location = new System.Drawing.Point(728, 62);
		this.buttonBatteryFull.Borderless = true;
		this.buttonBatteryFull.Margin = new System.Windows.Forms.Padding(0);
		this.buttonBatteryFull.Name = "buttonBatteryFull";
		this.buttonBatteryFull.Secondary = true;
		this.buttonBatteryFull.Size = new System.Drawing.Size(73, 36);
		this.buttonBatteryFull.TabIndex = 41;
		this.buttonBatteryFull.Text = "100%";
		this.buttonBatteryFull.UseVisualStyleBackColor = false;
		this.sliderBattery.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
		this.sliderBattery.Location = new System.Drawing.Point(20, 60);
		this.sliderBattery.Margin = new System.Windows.Forms.Padding(4);
		this.sliderBattery.Maximum = 100;
		// 2026-10-10（限充代系门控 · 用户要求「调整滑块范围为 50%-100%」）：下限 40 → 50。
		this.sliderBattery.Minimum = 50;
		this.sliderBattery.Name = "sliderBattery";
		this.sliderBattery.Size = new System.Drawing.Size(707, 40);
		this.sliderBattery.TabIndex = 20;
		this.sliderBattery.Text = "sliderBattery";
		this.sliderBattery.Value = 100;
		this.panelBatteryTitle.Controls.Add(this.labelBattery);
		this.panelBatteryTitle.Controls.Add(this.pictureBattery);
		this.panelBatteryTitle.Controls.Add(this.labelBatteryTitle);
		this.panelBatteryTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelBatteryTitle.Location = new System.Drawing.Point(20, 15);
		this.panelBatteryTitle.Margin = new System.Windows.Forms.Padding(4);
		this.panelBatteryTitle.Name = "panelBatteryTitle";
		this.panelBatteryTitle.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
		this.panelBatteryTitle.Size = new System.Drawing.Size(787, 44);
		this.panelBatteryTitle.TabIndex = 40;
		this.labelBattery.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.labelBattery.Location = new System.Drawing.Point(455, 0);
		this.labelBattery.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelBattery.Name = "labelBattery";
		this.labelBattery.Size = new System.Drawing.Size(324, 36);
		this.labelBattery.TabIndex = 39;
		this.labelBattery.Text = "                ";
		this.labelBattery.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.pictureBattery.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureBattery.Location = new System.Drawing.Point(8, 3);
		this.pictureBattery.Margin = new System.Windows.Forms.Padding(4);
		this.pictureBattery.Name = "pictureBattery";
		this.pictureBattery.Size = new System.Drawing.Size(32, 32);
		this.pictureBattery.TabIndex = 38;
		this.pictureBattery.TabStop = false;
		this.labelBatteryTitle.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Bold);
		this.labelBatteryTitle.Location = new System.Drawing.Point(43, 0);
		this.labelBatteryTitle.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelBatteryTitle.Name = "labelBatteryTitle";
		this.labelBatteryTitle.Size = new System.Drawing.Size(467, 32);
		this.labelBatteryTitle.TabIndex = 37;
		this.labelBatteryTitle.Text = "Battery Charge Limit";
		this.panelFooter.AutoSize = true;
		this.panelFooter.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelFooter.Controls.Add(this.tableButtons);
		this.panelFooter.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelFooter.Location = new System.Drawing.Point(11, 1887);
		this.panelFooter.Margin = new System.Windows.Forms.Padding(0);
		this.panelFooter.Name = "panelFooter";
		this.panelFooter.Padding = new System.Windows.Forms.Padding(20, 10, 20, 20);
		this.panelFooter.Size = new System.Drawing.Size(827, 88);
		this.panelFooter.TabIndex = 11;
		this.tableButtons.AutoSize = true;
		this.tableButtons.ColumnCount = 3;
		this.tableButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333332f));
		this.tableButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333332f));
		this.tableButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333332f));
		this.tableButtons.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 27f));
		this.tableButtons.Controls.Add(this.buttonDonate, 0, 0);
		this.tableButtons.Controls.Add(this.buttonQuit, 2, 0);
		this.tableButtons.Controls.Add(this.buttonUpdates, 1, 0);
		this.tableButtons.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableButtons.Location = new System.Drawing.Point(20, 10);
		this.tableButtons.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableButtons.Name = "tableButtons";
		this.tableButtons.RowCount = 1;
		this.tableButtons.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableButtons.Size = new System.Drawing.Size(787, 58);
		this.tableButtons.TabIndex = 25;
		this.buttonDonate.Activated = false;
		this.buttonDonate.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonDonate.Badge = 0;
		this.buttonDonate.BorderColor = System.Drawing.Color.Transparent;
		this.buttonDonate.BorderRadius = 2;
		this.buttonDonate.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonDonate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		UiVisualStyle.ApplyFooterGlyph(this.buttonDonate, UiGlyph.Kind.Heart, 32);
		this.buttonDonate.ImageAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.buttonDonate.Location = new System.Drawing.Point(4, 5);
		this.buttonDonate.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		this.buttonDonate.Name = "buttonDonate";
		this.buttonDonate.Secondary = true;
		this.buttonDonate.Size = new System.Drawing.Size(254, 48);
		this.buttonDonate.TabIndex = 3;
		this.buttonDonate.Text = "&Donate";
		this.buttonDonate.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonDonate.UseVisualStyleBackColor = false;
		this.buttonQuit.Activated = false;
		this.buttonQuit.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonQuit.BorderColor = System.Drawing.Color.Transparent;
		this.buttonQuit.BorderRadius = 2;
		this.buttonQuit.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonQuit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		UiVisualStyle.ApplyFooterGlyph(this.buttonQuit, UiGlyph.Kind.Close, 32);
		this.buttonQuit.Location = new System.Drawing.Point(528, 5);
		this.buttonQuit.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		this.buttonQuit.Name = "buttonQuit";
		this.buttonQuit.Secondary = true;
		this.buttonQuit.Size = new System.Drawing.Size(255, 48);
		this.buttonQuit.TabIndex = 2;
		this.buttonQuit.Text = "&Quit";
		this.buttonQuit.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.buttonQuit.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonQuit.UseVisualStyleBackColor = false;
		this.buttonUpdates.Activated = false;
		this.buttonUpdates.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonUpdates.BorderColor = System.Drawing.Color.Transparent;
		this.buttonUpdates.BorderRadius = 2;
		this.buttonUpdates.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonUpdates.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		UiVisualStyle.ApplyFooterGlyph(this.buttonUpdates, UiGlyph.Kind.Update, 32);
		this.buttonUpdates.ImageAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.buttonUpdates.Location = new System.Drawing.Point(266, 5);
		this.buttonUpdates.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
		this.buttonUpdates.Name = "buttonUpdates";
		this.buttonUpdates.Secondary = true;
		this.buttonUpdates.Size = new System.Drawing.Size(254, 48);
		this.buttonUpdates.TabIndex = 1;
		this.buttonUpdates.Text = "&Updates";
		this.buttonUpdates.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonUpdates.UseVisualStyleBackColor = false;
		this.checkStartup.AutoSize = true;
		this.checkStartup.Dock = System.Windows.Forms.DockStyle.Left;
		this.checkStartup.Location = new System.Drawing.Point(20, 0);
		this.checkStartup.Margin = new System.Windows.Forms.Padding(11, 5, 11, 5);
		this.checkStartup.Name = "checkStartup";
		this.checkStartup.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
		this.checkStartup.Size = new System.Drawing.Size(216, 50);
		this.checkStartup.TabIndex = 21;
		this.checkStartup.Text = MechrevoLite.Properties.Strings.RunOnStartup;
		this.checkStartup.UseVisualStyleBackColor = true;
		this.panelPerformance.AccessibleRole = System.Windows.Forms.AccessibleRole.Grouping;
		this.panelPerformance.AutoSize = true;
		this.panelPerformance.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelPerformance.Controls.Add(this.tablePerf);
		this.panelPerformance.Controls.Add(this.panelCPUTitle);
		this.panelPerformance.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelPerformance.Location = new System.Drawing.Point(11, 11);
		this.panelPerformance.Margin = new System.Windows.Forms.Padding(0);
		this.panelPerformance.Name = "panelPerformance";
		this.panelPerformance.Padding = new System.Windows.Forms.Padding(20);
		this.panelPerformance.Size = new System.Drawing.Size(827, 208);
		this.panelPerformance.TabIndex = 0;
		this.panelPerformance.TabStop = true;
		this.tablePerf.AutoSize = true;
		this.tablePerf.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tablePerf.ColumnCount = 5;
		for (int i = 0; i < 5; i++)
		{
			this.tablePerf.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20f));
		}
		this.tablePerf.Controls.Add(this.buttonSilent, 0, 0);
		this.tablePerf.Controls.Add(this.buttonBalanced, 1, 0);
		this.tablePerf.Controls.Add(this.buttonTurbo, 2, 0);
		this.tablePerf.Dock = System.Windows.Forms.DockStyle.Top;
		this.tablePerf.Location = new System.Drawing.Point(20, 60);
		this.tablePerf.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tablePerf.Name = "tablePerf";
		this.tablePerf.RowCount = 1;
		this.tablePerf.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 128f));
		this.tablePerf.Size = new System.Drawing.Size(787, 128);
		this.tablePerf.TabIndex = 29;
		this.buttonSilent.Activated = false;
		this.buttonSilent.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonSilent.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
		this.buttonSilent.BorderColor = System.Drawing.Color.Transparent;
		this.buttonSilent.BorderRadius = 5;
		this.buttonSilent.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonSilent.FlatAppearance.BorderSize = 0;
		this.buttonSilent.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSilent.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonSilent.Image = MechrevoLite.Properties.Resources.icons8_bicycle_48__1_;
		this.buttonSilent.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonSilent.Location = new System.Drawing.Point(4, 4);
		this.buttonSilent.Margin = new System.Windows.Forms.Padding(4);
		this.buttonSilent.Name = "buttonSilent";
		this.buttonSilent.Secondary = false;
		this.buttonSilent.Size = new System.Drawing.Size(188, 120);
		this.buttonSilent.TabIndex = 1;
		this.buttonSilent.Text = Strings.Office;
		this.buttonSilent.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonSilent.UseVisualStyleBackColor = false;
		this.buttonBalanced.Activated = false;
		this.buttonBalanced.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonBalanced.BorderColor = System.Drawing.Color.Transparent;
		this.buttonBalanced.BorderRadius = 5;
		this.buttonBalanced.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonBalanced.FlatAppearance.BorderSize = 0;
		this.buttonBalanced.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonBalanced.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonBalanced.Image = MechrevoLite.Properties.Resources.icons8_fiat_500_48;
		this.buttonBalanced.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonBalanced.Location = new System.Drawing.Point(200, 4);
		this.buttonBalanced.Margin = new System.Windows.Forms.Padding(4);
		this.buttonBalanced.Name = "buttonBalanced";
		this.buttonBalanced.Secondary = false;
		this.buttonBalanced.Size = new System.Drawing.Size(188, 120);
		this.buttonBalanced.TabIndex = 1;
		this.buttonBalanced.Text = Strings.Game;
		this.buttonBalanced.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonBalanced.UseVisualStyleBackColor = false;
		this.buttonTurbo.Activated = false;
		this.buttonTurbo.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonTurbo.BorderColor = System.Drawing.Color.Transparent;
		this.buttonTurbo.BorderRadius = 5;
		this.buttonTurbo.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonTurbo.FlatAppearance.BorderSize = 0;
		this.buttonTurbo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonTurbo.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonTurbo.Image = MechrevoLite.Properties.Resources.icons8_rocket_48;
		this.buttonTurbo.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonTurbo.Location = new System.Drawing.Point(396, 4);
		this.buttonTurbo.Margin = new System.Windows.Forms.Padding(4);
		this.buttonTurbo.Name = "buttonTurbo";
		this.buttonTurbo.Secondary = false;
		this.buttonTurbo.Size = new System.Drawing.Size(188, 120);
		this.buttonTurbo.TabIndex = 2;
		this.buttonTurbo.Text = Strings.Extreme;
		this.buttonTurbo.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonTurbo.UseVisualStyleBackColor = false;
		this.panelCPUTitle.Controls.Add(this.picturePerf);
		this.panelCPUTitle.Controls.Add(this.labelPerf);
		this.panelCPUTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelCPUTitle.Location = new System.Drawing.Point(20, 20);
		this.panelCPUTitle.Margin = new System.Windows.Forms.Padding(4);
		this.panelCPUTitle.Name = "panelCPUTitle";
		this.panelCPUTitle.Size = new System.Drawing.Size(787, 40);
		this.panelCPUTitle.TabIndex = 30;
		this.picturePerf.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.picturePerf.InitialImage = null;
		this.picturePerf.Location = new System.Drawing.Point(8, 0);
		this.picturePerf.Margin = new System.Windows.Forms.Padding(4);
		this.picturePerf.Name = "picturePerf";
		this.picturePerf.Size = new System.Drawing.Size(32, 32);
		this.picturePerf.TabIndex = 35;
		this.picturePerf.TabStop = false;
		this.labelPerf.AccessibleRole = System.Windows.Forms.AccessibleRole.Caret;
		this.labelPerf.AutoSize = true;
		this.labelPerf.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Bold);
		this.labelPerf.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
		this.labelPerf.Location = new System.Drawing.Point(40, 0);
		this.labelPerf.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelPerf.Name = "labelPerf";
		this.labelPerf.Size = new System.Drawing.Size(234, 32);
		this.labelPerf.TabIndex = 0;
		this.labelPerf.Text = "Performance Mode";
		this.panelGPU.AccessibleRole = System.Windows.Forms.AccessibleRole.Grouping;
		this.panelGPU.AutoSize = true;
		this.panelGPU.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelGPU.Controls.Add(this.labelTipGPU);
		this.panelGPU.Controls.Add(this.tableAMD);
		this.panelGPU.Controls.Add(this.tableGPU);
		this.panelGPU.Controls.Add(this.panelGPUTitle);
		this.panelGPU.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelGPU.Location = new System.Drawing.Point(11, 219);
		this.panelGPU.Margin = new System.Windows.Forms.Padding(0);
		this.panelGPU.Name = "panelGPU";
		this.panelGPU.Padding = new System.Windows.Forms.Padding(20, 20, 20, 0);
		this.panelGPU.Size = new System.Drawing.Size(827, 432);
		this.panelGPU.TabIndex = 1;
		this.panelGPU.TabStop = true;
		this.labelTipGPU.Dock = System.Windows.Forms.DockStyle.Top;
		this.labelTipGPU.ForeColor = System.Drawing.SystemColors.GrayText;
		this.labelTipGPU.Location = new System.Drawing.Point(20, 396);
		this.labelTipGPU.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelTipGPU.Name = "labelTipGPU";
		this.labelTipGPU.Size = new System.Drawing.Size(787, 36);
		this.labelTipGPU.TabIndex = 20;
		this.tableAMD.AutoSize = true;
		this.tableAMD.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableAMD.ColumnCount = 3;
		this.tableAMD.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableAMD.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableAMD.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableAMD.Controls.Add(this.buttonOverlay, 0, 0);
		this.tableAMD.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableAMD.Location = new System.Drawing.Point(20, 316);
		this.tableAMD.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableAMD.Name = "tableAMD";
		this.tableAMD.RowCount = 1;
		this.tableAMD.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80f));
		this.tableAMD.Size = new System.Drawing.Size(787, 80);
		this.tableAMD.TabIndex = 24;
		this.tableAMD.Visible = false;
		this.buttonOverlay.Activated = false;
		this.buttonOverlay.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonOverlay.BorderColor = System.Drawing.Color.Transparent;
		this.buttonOverlay.BorderRadius = 5;
		this.buttonOverlay.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonOverlay.FlatAppearance.BorderSize = 0;
		this.buttonOverlay.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonOverlay.ForeColor = System.Drawing.SystemColors.ControlText;
		UiVisualStyle.ApplyFooterGlyph(this.buttonOverlay, UiGlyph.Kind.Overlay, 32);
		this.buttonOverlay.ImageAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.buttonOverlay.Location = new System.Drawing.Point(266, 4);
		this.buttonOverlay.Margin = new System.Windows.Forms.Padding(4);
		this.buttonOverlay.Name = "buttonOverlay";
		this.buttonOverlay.Secondary = false;
		this.buttonOverlay.Size = new System.Drawing.Size(254, 72);
		this.buttonOverlay.TabIndex = 12;
		this.buttonOverlay.Text = "Overlay";
		this.buttonOverlay.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonOverlay.UseVisualStyleBackColor = false;
		this.tableGPU.AutoSize = true;
		this.tableGPU.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableGPU.ColumnCount = 4;
		this.tableGPU.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableGPU.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableGPU.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableGPU.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableGPU.Controls.Add(this.buttonStopGPU, 0, 0);
		this.tableGPU.Controls.Add(this.buttonEco, 0, 0);
		this.tableGPU.Controls.Add(this.buttonStandard, 1, 0);
		this.tableGPU.Controls.Add(this.buttonUltimate, 2, 0);
		this.tableGPU.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableGPU.Location = new System.Drawing.Point(20, 60);
		this.tableGPU.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableGPU.Name = "tableGPU";
		this.tableGPU.RowCount = 1;
		this.tableGPU.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 128f));
		this.tableGPU.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 128f));
		this.tableGPU.Size = new System.Drawing.Size(787, 256);
		this.tableGPU.TabIndex = 16;
		this.buttonStopGPU.Activated = false;
		this.buttonStopGPU.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonStopGPU.BorderColor = System.Drawing.Color.Transparent;
		this.buttonStopGPU.BorderRadius = 5;
		this.buttonStopGPU.CausesValidation = false;
		this.buttonStopGPU.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonStopGPU.FlatAppearance.BorderSize = 0;
		this.buttonStopGPU.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonStopGPU.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonStopGPU.Image = MechrevoLite.Properties.Resources.icons8_leaf_48;
		this.buttonStopGPU.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonStopGPU.Location = new System.Drawing.Point(200, 4);
		this.buttonStopGPU.Margin = new System.Windows.Forms.Padding(4);
		this.buttonStopGPU.Name = "buttonStopGPU";
		this.buttonStopGPU.Secondary = false;
		this.buttonStopGPU.Size = new System.Drawing.Size(188, 120);
		this.buttonStopGPU.TabIndex = 4;
		this.buttonStopGPU.Text = "Stop GPU applications";
		this.buttonStopGPU.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonStopGPU.UseVisualStyleBackColor = false;
		this.buttonStopGPU.Visible = false;
		this.buttonEco.Activated = false;
		this.buttonEco.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonEco.BorderColor = System.Drawing.Color.Transparent;
		this.buttonEco.BorderRadius = 5;
		this.buttonEco.CausesValidation = false;
		this.buttonEco.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonEco.FlatAppearance.BorderSize = 0;
		this.buttonEco.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonEco.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonEco.Image = MechrevoLite.Properties.Resources.icons8_leaf_48;
		this.buttonEco.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonEco.Location = new System.Drawing.Point(4, 4);
		this.buttonEco.Margin = new System.Windows.Forms.Padding(4);
		this.buttonEco.Name = "buttonEco";
		this.buttonEco.Secondary = false;
		this.buttonEco.Size = new System.Drawing.Size(188, 120);
		this.buttonEco.TabIndex = 4;
		this.buttonEco.Text = MechrevoLite.Properties.Strings.EcoMode;
		this.buttonEco.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonEco.UseVisualStyleBackColor = false;
		this.buttonStandard.Activated = false;
		this.buttonStandard.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonStandard.BorderColor = System.Drawing.Color.Transparent;
		this.buttonStandard.BorderRadius = 5;
		this.buttonStandard.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonStandard.FlatAppearance.BorderSize = 0;
		this.buttonStandard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonStandard.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonStandard.Image = MechrevoLite.Properties.Resources.icons8_spa_flower_48;
		this.buttonStandard.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonStandard.Location = new System.Drawing.Point(396, 4);
		this.buttonStandard.Margin = new System.Windows.Forms.Padding(4);
		this.buttonStandard.Name = "buttonStandard";
		this.buttonStandard.Secondary = false;
		this.buttonStandard.Size = new System.Drawing.Size(188, 120);
		this.buttonStandard.TabIndex = 5;
		this.buttonStandard.Text = MechrevoLite.Properties.Strings.StandardMode;
		this.buttonStandard.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonStandard.UseVisualStyleBackColor = false;
		this.buttonUltimate.Activated = false;
		this.buttonUltimate.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonUltimate.BorderColor = System.Drawing.Color.Transparent;
		this.buttonUltimate.BorderRadius = 5;
		this.buttonUltimate.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonUltimate.FlatAppearance.BorderSize = 0;
		this.buttonUltimate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonUltimate.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonUltimate.Image = MechrevoLite.Properties.Resources.icons8_game_controller_48;
		this.buttonUltimate.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.buttonUltimate.Location = new System.Drawing.Point(592, 4);
		this.buttonUltimate.Margin = new System.Windows.Forms.Padding(4);
		this.buttonUltimate.Name = "buttonUltimate";
		this.buttonUltimate.Secondary = false;
		this.buttonUltimate.Size = new System.Drawing.Size(191, 120);
		this.buttonUltimate.TabIndex = 6;
		this.buttonUltimate.Text = MechrevoLite.Properties.Strings.UltimateMode;
		this.buttonUltimate.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
		this.buttonUltimate.UseVisualStyleBackColor = false;
		this.panelGPUTitle.Controls.Add(this.pictureGPU);
		this.panelGPUTitle.Controls.Add(this.labelGPU);
		this.panelGPUTitle.Controls.Add(this.labelGPUFan);
		this.panelGPUTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelGPUTitle.Location = new System.Drawing.Point(20, 20);
		this.panelGPUTitle.Margin = new System.Windows.Forms.Padding(4);
		this.panelGPUTitle.Name = "panelGPUTitle";
		this.panelGPUTitle.Size = new System.Drawing.Size(787, 40);
		this.panelGPUTitle.TabIndex = 21;
		this.pictureGPU.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureGPU.Location = new System.Drawing.Point(8, 0);
		this.pictureGPU.Margin = new System.Windows.Forms.Padding(4);
		this.pictureGPU.Name = "pictureGPU";
		this.pictureGPU.Size = new System.Drawing.Size(32, 32);
		this.pictureGPU.TabIndex = 22;
		this.pictureGPU.TabStop = false;
		this.labelGPU.AutoSize = true;
		this.labelGPU.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Bold);
		this.labelGPU.Location = new System.Drawing.Point(40, 0);
		this.labelGPU.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelGPU.Name = "labelGPU";
		this.labelGPU.Size = new System.Drawing.Size(136, 32);
		this.labelGPU.TabIndex = 21;
		this.labelGPU.Text = "GPU Mode";
		this.labelGPUFan.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.labelGPUFan.Location = new System.Drawing.Point(387, 0);
		this.labelGPUFan.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelGPUFan.Name = "labelGPUFan";
		this.labelGPUFan.Size = new System.Drawing.Size(400, 35);
		this.labelGPUFan.TabIndex = 20;
		this.labelGPUFan.Text = "         ";
		this.labelGPUFan.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.panelScreen.AccessibleRole = System.Windows.Forms.AccessibleRole.Grouping;
		this.panelScreen.AutoSize = true;
		this.panelScreen.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelScreen.Controls.Add(this.labelTipScreen);
		this.panelScreen.Controls.Add(this.tableScreen);
		this.panelScreen.Controls.Add(this.panelScreenTitle);
		this.panelScreen.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelScreen.Location = new System.Drawing.Point(11, 651);
		this.panelScreen.Margin = new System.Windows.Forms.Padding(0);
		this.panelScreen.Name = "panelScreen";
		this.panelScreen.Padding = new System.Windows.Forms.Padding(20, 11, 20, 0);
		this.panelScreen.Size = new System.Drawing.Size(827, 187);
		this.panelScreen.TabIndex = 2;
		this.panelScreen.TabStop = true;
		this.labelTipScreen.Dock = System.Windows.Forms.DockStyle.Top;
		this.labelTipScreen.ForeColor = System.Drawing.SystemColors.GrayText;
		this.labelTipScreen.Location = new System.Drawing.Point(20, 151);
		this.labelTipScreen.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelTipScreen.Name = "labelTipScreen";
		this.labelTipScreen.Size = new System.Drawing.Size(787, 36);
		this.labelTipScreen.TabIndex = 24;
		this.tableScreen.AutoSize = true;
		this.tableScreen.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableScreen.ColumnCount = 4;
		this.tableScreen.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableScreen.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableScreen.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableScreen.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableScreen.Controls.Add(this.buttonScreenAuto, 0, 0);
		this.tableScreen.Controls.Add(this.button60Hz, 1, 0);
		this.tableScreen.Controls.Add(this.button120Hz, 2, 0);
		this.tableScreen.Controls.Add(this.buttonMiniled, 3, 0);
		this.tableScreen.Controls.Add(this.buttonFHD, 3, 0);
		this.tableScreen.Controls.Add(this.buttonHDRControl, 3, 0);
		this.tableScreen.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableScreen.Location = new System.Drawing.Point(20, 51);
		this.tableScreen.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableScreen.Name = "tableScreen";
		this.tableScreen.RowCount = 1;
		this.tableScreen.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80f));
		this.tableScreen.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20f));
		this.tableScreen.Size = new System.Drawing.Size(787, 100);
		this.tableScreen.TabIndex = 23;
		this.buttonScreenAuto.Activated = false;
		this.buttonScreenAuto.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonScreenAuto.BorderColor = System.Drawing.Color.Transparent;
		this.buttonScreenAuto.BorderRadius = 5;
		this.buttonScreenAuto.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonScreenAuto.FlatAppearance.BorderSize = 0;
		this.buttonScreenAuto.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonScreenAuto.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonScreenAuto.Location = new System.Drawing.Point(4, 4);
		this.buttonScreenAuto.Margin = new System.Windows.Forms.Padding(4);
		this.buttonScreenAuto.Name = "buttonScreenAuto";
		this.buttonScreenAuto.Secondary = false;
		this.buttonScreenAuto.Size = new System.Drawing.Size(188, 72);
		this.buttonScreenAuto.TabIndex = 9;
		this.buttonScreenAuto.Text = MechrevoLite.Properties.Strings.AutoMode;
		this.buttonScreenAuto.UseVisualStyleBackColor = false;
		this.button60Hz.Activated = false;
		this.button60Hz.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.button60Hz.BorderColor = System.Drawing.Color.Transparent;
		this.button60Hz.BorderRadius = 5;
		this.button60Hz.CausesValidation = false;
		this.button60Hz.Dock = System.Windows.Forms.DockStyle.Fill;
		this.button60Hz.FlatAppearance.BorderSize = 0;
		this.button60Hz.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.button60Hz.ForeColor = System.Drawing.SystemColors.ControlText;
		this.button60Hz.Location = new System.Drawing.Point(200, 4);
		this.button60Hz.Margin = new System.Windows.Forms.Padding(4);
		this.button60Hz.Name = "button60Hz";
		this.button60Hz.Secondary = false;
		this.button60Hz.Size = new System.Drawing.Size(188, 72);
		this.button60Hz.TabIndex = 10;
		this.button60Hz.Text = "60Hz";
		this.button60Hz.UseVisualStyleBackColor = false;
		this.button120Hz.Activated = false;
		this.button120Hz.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.button120Hz.BorderColor = System.Drawing.Color.Transparent;
		this.button120Hz.BorderRadius = 5;
		this.button120Hz.Dock = System.Windows.Forms.DockStyle.Fill;
		this.button120Hz.FlatAppearance.BorderSize = 0;
		this.button120Hz.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.button120Hz.ForeColor = System.Drawing.SystemColors.ControlText;
		this.button120Hz.Location = new System.Drawing.Point(396, 4);
		this.button120Hz.Margin = new System.Windows.Forms.Padding(4);
		this.button120Hz.Name = "button120Hz";
		this.button120Hz.Secondary = false;
		this.button120Hz.Size = new System.Drawing.Size(188, 72);
		this.button120Hz.TabIndex = 11;
		this.button120Hz.Text = "120Hz + OD";
		this.button120Hz.UseVisualStyleBackColor = false;
		this.buttonMiniled.Activated = false;
		this.buttonMiniled.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonMiniled.BorderColor = System.Drawing.Color.Transparent;
		this.buttonMiniled.BorderRadius = 5;
		this.buttonMiniled.CausesValidation = false;
		this.buttonMiniled.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonMiniled.FlatAppearance.BorderSize = 0;
		this.buttonMiniled.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMiniled.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonMiniled.Location = new System.Drawing.Point(592, 4);
		this.buttonMiniled.Margin = new System.Windows.Forms.Padding(4);
		this.buttonMiniled.Name = "buttonMiniled";
		this.buttonMiniled.Secondary = false;
		this.buttonMiniled.Size = new System.Drawing.Size(191, 72);
		this.buttonMiniled.TabIndex = 12;
		this.buttonMiniled.Text = MechrevoLite.Properties.Strings.Multizone;
		this.buttonMiniled.UseVisualStyleBackColor = false;
		this.buttonFHD.Activated = false;
		this.buttonFHD.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonFHD.BorderColor = System.Drawing.Color.Transparent;
		this.buttonFHD.BorderRadius = 5;
		this.buttonFHD.CausesValidation = false;
		this.buttonFHD.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonFHD.FlatAppearance.BorderSize = 0;
		this.buttonFHD.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonFHD.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonFHD.Location = new System.Drawing.Point(4, 84);
		this.buttonFHD.Margin = new System.Windows.Forms.Padding(4);
		this.buttonFHD.Name = "buttonFHD";
		this.buttonFHD.Secondary = false;
		this.buttonFHD.Size = new System.Drawing.Size(188, 12);
		this.buttonFHD.TabIndex = 13;
		this.buttonFHD.Text = "FHD";
		this.buttonFHD.UseVisualStyleBackColor = false;
		this.buttonFHD.Visible = false;
		this.panelScreenTitle.Controls.Add(this.labelMidFan);
		this.panelScreenTitle.Controls.Add(this.pictureScreen);
		this.panelScreenTitle.Controls.Add(this.labelSreen);
		this.panelScreenTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelScreenTitle.Location = new System.Drawing.Point(20, 11);
		this.panelScreenTitle.Margin = new System.Windows.Forms.Padding(4);
		this.panelScreenTitle.Name = "panelScreenTitle";
		this.panelScreenTitle.Size = new System.Drawing.Size(787, 40);
		this.panelScreenTitle.TabIndex = 25;
		this.labelMidFan.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
		this.labelMidFan.Location = new System.Drawing.Point(500, 0);
		this.labelMidFan.Margin = new System.Windows.Forms.Padding(8, 0, 8, 0);
		this.labelMidFan.Name = "labelMidFan";
		this.labelMidFan.Size = new System.Drawing.Size(285, 35);
		this.labelMidFan.TabIndex = 28;
		this.labelMidFan.Text = "         ";
		this.labelMidFan.TextAlign = System.Drawing.ContentAlignment.TopRight;
		this.pictureScreen.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureScreen.Location = new System.Drawing.Point(8, 3);
		this.pictureScreen.Margin = new System.Windows.Forms.Padding(4);
		this.pictureScreen.Name = "pictureScreen";
		this.pictureScreen.Size = new System.Drawing.Size(32, 32);
		this.pictureScreen.TabIndex = 27;
		this.pictureScreen.TabStop = false;
		this.labelSreen.AutoSize = true;
		this.labelSreen.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Bold);
		this.labelSreen.Location = new System.Drawing.Point(40, 0);
		this.labelSreen.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelSreen.Name = "labelSreen";
		this.labelSreen.Size = new System.Drawing.Size(176, 32);
		this.labelSreen.TabIndex = 26;
		this.labelSreen.Text = "Laptop Screen";
		this.panelKeyboard.AccessibleRole = System.Windows.Forms.AccessibleRole.Grouping;
		this.panelKeyboard.AutoSize = true;
		this.panelKeyboard.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelKeyboard.Controls.Add(this.labelBacklight);
		this.panelKeyboard.Controls.Add(this.tableLayoutKeyboard);
		this.panelKeyboard.Controls.Add(this.panelKeyboardTitle);
		this.panelKeyboard.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelKeyboard.Location = new System.Drawing.Point(11, 1394);
		this.panelKeyboard.Margin = new System.Windows.Forms.Padding(0);
		this.panelKeyboard.Name = "panelKeyboard";
		this.panelKeyboard.Padding = new System.Windows.Forms.Padding(20, 20, 20, 0);
		this.panelKeyboard.Size = new System.Drawing.Size(827, 146);
		this.panelKeyboard.TabIndex = 6;
		this.panelKeyboard.TabStop = true;
		this.labelBacklight.Cursor = System.Windows.Forms.Cursors.Hand;
		this.labelBacklight.Dock = System.Windows.Forms.DockStyle.Top;
		this.labelBacklight.Font = MechrevoLite.UI.UiVisualStyle.Font(9f);
		this.labelBacklight.ForeColor = System.Drawing.SystemColors.GrayText;
		this.labelBacklight.Location = new System.Drawing.Point(20, 112);
		this.labelBacklight.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelBacklight.Name = "labelBacklight";
		this.labelBacklight.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelBacklight.Size = new System.Drawing.Size(787, 34);
		this.labelBacklight.TabIndex = 43;
		this.tableLayoutKeyboard.AutoSize = true;
		this.tableLayoutKeyboard.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableLayoutKeyboard.ColumnCount = 3;
		this.tableLayoutKeyboard.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableLayoutKeyboard.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableLayoutKeyboard.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33f));
		this.tableLayoutKeyboard.Controls.Add(this.buttonKeyboard, 0, 0);
		this.tableLayoutKeyboard.Controls.Add(this.buttonKeyboardColor, 0, 0);
		this.tableLayoutKeyboard.Controls.Add(this.comboKeyboard, 0, 0);
		this.tableLayoutKeyboard.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableLayoutKeyboard.Location = new System.Drawing.Point(20, 60);
		this.tableLayoutKeyboard.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableLayoutKeyboard.Name = "tableLayoutKeyboard";
		this.tableLayoutKeyboard.RowCount = 1;
		this.tableLayoutKeyboard.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutKeyboard.Size = new System.Drawing.Size(787, 52);
		this.tableLayoutKeyboard.TabIndex = 39;
		this.buttonKeyboard.Activated = false;
		this.buttonKeyboard.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.buttonKeyboard.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonKeyboard.BorderColor = System.Drawing.Color.Transparent;
		this.buttonKeyboard.BorderRadius = 2;
		this.buttonKeyboard.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonKeyboard.FlatAppearance.BorderSize = 0;
		this.buttonKeyboard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonKeyboard.Image = MechrevoLite.Properties.Resources.icons8_settings_32;
		this.buttonKeyboard.ImageAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.buttonKeyboard.Location = new System.Drawing.Point(528, 4);
		this.buttonKeyboard.Margin = new System.Windows.Forms.Padding(4);
		this.buttonKeyboard.Name = "buttonKeyboard";
		this.buttonKeyboard.Secondary = true;
		this.buttonKeyboard.Size = new System.Drawing.Size(255, 48);
		this.buttonKeyboard.TabIndex = 37;
		this.buttonKeyboard.Text = "&Extra";
		this.buttonKeyboard.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonKeyboard.UseVisualStyleBackColor = false;
		this.buttonKeyboardColor.Activated = false;
		this.buttonKeyboardColor.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.buttonKeyboardColor.BackColor = System.Drawing.SystemColors.ButtonHighlight;
		this.buttonKeyboardColor.BorderColor = System.Drawing.Color.Transparent;
		this.buttonKeyboardColor.BorderRadius = 2;
		this.buttonKeyboardColor.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonKeyboardColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonKeyboardColor.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonKeyboardColor.Location = new System.Drawing.Point(0, 0);
		this.buttonKeyboardColor.Margin = new System.Windows.Forms.Padding(4);
		this.buttonKeyboardColor.Name = "buttonKeyboardColor";
		this.buttonKeyboardColor.Secondary = false;
		this.buttonKeyboardColor.Size = new System.Drawing.Size(254, 48);
		this.buttonKeyboardColor.TabIndex = 14;
		this.buttonKeyboardColor.Text = MechrevoLite.Properties.Strings.Color;
		this.buttonKeyboardColor.UseVisualStyleBackColor = false;
		this.comboKeyboard.BorderColor = MechrevoLite.UI.UiVisualStyle.Border;
		this.comboKeyboard.ButtonColor = MechrevoLite.UI.UiVisualStyle.SurfaceRaised;
		this.comboKeyboard.Dock = System.Windows.Forms.DockStyle.Top;
		this.comboKeyboard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.comboKeyboard.Font = MechrevoLite.UI.UiVisualStyle.Font(9f);
		this.comboKeyboard.FormattingEnabled = true;
		this.comboKeyboard.Items.AddRange("Static", "Breathe", "Rainbow", "Strobe");
		this.comboKeyboard.Location = new System.Drawing.Point(7, 7);
		this.comboKeyboard.Margin = new System.Windows.Forms.Padding(7, 7, 7, 4);
		this.comboKeyboard.Name = "comboKeyboard";
		this.comboKeyboard.Size = new System.Drawing.Size(248, 40);
		this.comboKeyboard.TabIndex = 13;
		this.panelKeyboardTitle.Controls.Add(this.buttonFnLock);
		this.panelKeyboardTitle.Controls.Add(this.pictureKeyboard);
		this.panelKeyboardTitle.Controls.Add(this.labelKeyboard);
		this.panelKeyboardTitle.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelKeyboardTitle.Location = new System.Drawing.Point(20, 20);
		this.panelKeyboardTitle.Margin = new System.Windows.Forms.Padding(0);
		this.panelKeyboardTitle.Name = "panelKeyboardTitle";
		this.panelKeyboardTitle.Padding = new System.Windows.Forms.Padding(0, 0, 5, 0);
		this.panelKeyboardTitle.Size = new System.Drawing.Size(787, 40);
		this.panelKeyboardTitle.TabIndex = 40;
		this.buttonFnLock.Activated = false;
		this.buttonFnLock.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonFnLock.BorderColor = System.Drawing.Color.Transparent;
		this.buttonFnLock.BorderRadius = 2;
		this.buttonFnLock.Borderless = true;
		this.buttonFnLock.Dock = System.Windows.Forms.DockStyle.Right;
		this.buttonFnLock.FlatAppearance.BorderSize = 0;
		this.buttonFnLock.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonFnLock.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f, System.Drawing.FontStyle.Bold);
		this.buttonFnLock.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.buttonFnLock.Location = new System.Drawing.Point(675, 0);
		this.buttonFnLock.Margin = new System.Windows.Forms.Padding(0);
		this.buttonFnLock.Name = "buttonFnLock";
		this.buttonFnLock.Secondary = true;
		this.buttonFnLock.Size = new System.Drawing.Size(107, 40);
		this.buttonFnLock.TabIndex = 4;
		this.buttonFnLock.Text = "FN-Lock";
		this.buttonFnLock.UseVisualStyleBackColor = false;
		this.pictureKeyboard.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureKeyboard.Location = new System.Drawing.Point(8, 0);
		this.pictureKeyboard.Margin = new System.Windows.Forms.Padding(4);
		this.pictureKeyboard.Name = "pictureKeyboard";
		this.pictureKeyboard.Size = new System.Drawing.Size(32, 32);
		this.pictureKeyboard.TabIndex = 35;
		this.pictureKeyboard.TabStop = false;
		this.labelKeyboard.AutoSize = true;
		this.labelKeyboard.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Bold);
		this.labelKeyboard.Location = new System.Drawing.Point(43, 0);
		this.labelKeyboard.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
		this.labelKeyboard.Name = "labelKeyboard";
		this.labelKeyboard.Size = new System.Drawing.Size(210, 32);
		this.labelKeyboard.TabIndex = 34;
		this.labelKeyboard.Text = "Laptop Keyboard";
		this.panelStartup.Controls.Add(this.labelCharge);
		this.panelStartup.Controls.Add(this.checkStartup);
		this.panelStartup.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelStartup.Location = new System.Drawing.Point(11, 1787);
		this.panelStartup.Margin = new System.Windows.Forms.Padding(0);
		this.panelStartup.Name = "panelStartup";
		this.panelStartup.Padding = new System.Windows.Forms.Padding(20, 0, 20, 0);
		this.panelStartup.Size = new System.Drawing.Size(827, 50);
		this.panelStartup.TabIndex = 9;
		this.labelCharge.Cursor = System.Windows.Forms.Cursors.Hand;
		this.labelCharge.Dock = System.Windows.Forms.DockStyle.Right;
		this.labelCharge.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.labelCharge.Location = new System.Drawing.Point(442, 0);
		this.labelCharge.Margin = new System.Windows.Forms.Padding(0);
		this.labelCharge.Name = "labelCharge";
		this.labelCharge.Size = new System.Drawing.Size(365, 50);
		this.labelCharge.TabIndex = 40;
		this.labelCharge.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.panelPeripherals.AutoSize = true;
		this.panelPeripherals.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelPeripherals.Controls.Add(this.tableLayoutPeripherals);
		this.panelPeripherals.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelPeripherals.Location = new System.Drawing.Point(11, 1540);
		this.panelPeripherals.Margin = new System.Windows.Forms.Padding(0);
		this.panelPeripherals.Name = "panelPeripherals";
		this.panelPeripherals.Padding = new System.Windows.Forms.Padding(20, 5, 20, 10);
		this.panelPeripherals.Size = new System.Drawing.Size(827, 143);
		this.panelPeripherals.TabIndex = 7;
		this.panelPeripherals.Visible = false;
		this.tableLayoutPeripherals.AutoSize = false;
		this.tableLayoutPeripherals.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.tableLayoutPeripherals.ColumnCount = 3;
		this.tableLayoutPeripherals.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableLayoutPeripherals.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableLayoutPeripherals.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableLayoutPeripherals.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25f));
		this.tableLayoutPeripherals.Controls.Add(this.buttonPeripheral3, 0, 0);
		this.tableLayoutPeripherals.Controls.Add(this.buttonPeripheral2, 0, 0);
		this.tableLayoutPeripherals.Controls.Add(this.buttonPeripheral1, 0, 0);
		this.tableLayoutPeripherals.Dock = System.Windows.Forms.DockStyle.Top;
		this.tableLayoutPeripherals.Location = new System.Drawing.Point(20, 5);
		this.tableLayoutPeripherals.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		this.tableLayoutPeripherals.Name = "tableLayoutPeripherals";
		this.tableLayoutPeripherals.RowCount = 1;
		this.tableLayoutPeripherals.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		this.tableLayoutPeripherals.Size = new System.Drawing.Size(787, 128);
		this.tableLayoutPeripherals.TabIndex = 43;
		this.buttonPeripheral3.Activated = false;
		this.buttonPeripheral3.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonPeripheral3.BorderColor = System.Drawing.Color.Transparent;
		this.buttonPeripheral3.BorderRadius = 5;
		this.buttonPeripheral3.CausesValidation = false;
		this.buttonPeripheral3.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonPeripheral3.FlatAppearance.BorderSize = 0;
		this.buttonPeripheral3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonPeripheral3.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f);
		this.buttonPeripheral3.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonPeripheral3.Image = MechrevoLite.Properties.Resources.icons8_maus_48;
		this.buttonPeripheral3.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonPeripheral3.Location = new System.Drawing.Point(528, 4);
		this.buttonPeripheral3.Margin = new System.Windows.Forms.Padding(4);
		this.buttonPeripheral3.Name = "buttonPeripheral3";
		this.buttonPeripheral3.Secondary = true;
		this.buttonPeripheral3.Size = new System.Drawing.Size(255, 120);
		this.buttonPeripheral3.TabIndex = 2;
		this.buttonPeripheral3.Text = "Mouse 3";
		this.buttonPeripheral3.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonPeripheral3.UseVisualStyleBackColor = false;
		this.buttonPeripheral2.Activated = false;
		this.buttonPeripheral2.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonPeripheral2.BorderColor = System.Drawing.Color.Transparent;
		this.buttonPeripheral2.BorderRadius = 5;
		this.buttonPeripheral2.CausesValidation = false;
		this.buttonPeripheral2.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonPeripheral2.FlatAppearance.BorderSize = 0;
		this.buttonPeripheral2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonPeripheral2.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f);
		this.buttonPeripheral2.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonPeripheral2.Image = MechrevoLite.Properties.Resources.icons8_maus_48;
		this.buttonPeripheral2.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonPeripheral2.Location = new System.Drawing.Point(266, 4);
		this.buttonPeripheral2.Margin = new System.Windows.Forms.Padding(4);
		this.buttonPeripheral2.Name = "buttonPeripheral2";
		this.buttonPeripheral2.Secondary = true;
		this.buttonPeripheral2.Size = new System.Drawing.Size(254, 120);
		this.buttonPeripheral2.TabIndex = 1;
		this.buttonPeripheral2.Text = "Mouse 2";
		this.buttonPeripheral2.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonPeripheral2.UseVisualStyleBackColor = false;
		this.buttonPeripheral1.Activated = false;
		this.buttonPeripheral1.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonPeripheral1.BorderColor = System.Drawing.Color.Transparent;
		this.buttonPeripheral1.BorderRadius = 5;
		this.buttonPeripheral1.CausesValidation = false;
		this.buttonPeripheral1.Dock = System.Windows.Forms.DockStyle.Top;
		this.buttonPeripheral1.FlatAppearance.BorderSize = 0;
		this.buttonPeripheral1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonPeripheral1.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f);
		this.buttonPeripheral1.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonPeripheral1.Image = MechrevoLite.Properties.Resources.icons8_maus_48;
		this.buttonPeripheral1.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonPeripheral1.Location = new System.Drawing.Point(4, 4);
		this.buttonPeripheral1.Margin = new System.Windows.Forms.Padding(4);
		this.buttonPeripheral1.Name = "buttonPeripheral1";
		this.buttonPeripheral1.Secondary = true;
		this.buttonPeripheral1.Size = new System.Drawing.Size(254, 120);
		this.buttonPeripheral1.TabIndex = 0;
		this.buttonPeripheral1.Text = "Mouse 1";
		this.buttonPeripheral1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
		this.buttonPeripheral1.UseVisualStyleBackColor = false;
		this.panelVersion.AutoSize = true;
		this.panelVersion.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		this.panelVersion.Controls.Add(this.buttonEnergySaver);
		this.panelVersion.Controls.Add(this.buttonAmdOled);
		this.panelVersion.Controls.Add(this.labelVersion);
		this.panelVersion.Dock = System.Windows.Forms.DockStyle.Top;
		this.panelVersion.Location = new System.Drawing.Point(11, 1837);
		this.panelVersion.MinimumSize = new System.Drawing.Size(0, 50);
		this.panelVersion.CardStyle = true;
		this.panelVersion.Name = "panelVersion";
		this.panelVersion.Padding = new System.Windows.Forms.Padding(20, 5, 24, 5);
		this.panelVersion.Size = new System.Drawing.Size(827, 50);
		this.panelVersion.TabIndex = 10;
		this.buttonEnergySaver.Activated = false;
		this.buttonEnergySaver.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonEnergySaver.BorderColor = System.Drawing.Color.Transparent;
		this.buttonEnergySaver.BorderRadius = 2;
		this.buttonEnergySaver.Dock = System.Windows.Forms.DockStyle.Right;
		this.buttonEnergySaver.FlatAppearance.BorderSize = 0;
		this.buttonEnergySaver.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonEnergySaver.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f, System.Drawing.FontStyle.Bold);
		this.buttonEnergySaver.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.buttonEnergySaver.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonEnergySaver.Location = new System.Drawing.Point(640, 5);
		this.buttonEnergySaver.Margin = new System.Windows.Forms.Padding(0);
		this.buttonEnergySaver.Name = "buttonEnergySaver";
		this.buttonEnergySaver.Secondary = true;
		this.buttonEnergySaver.Size = new System.Drawing.Size(163, 40);
		this.buttonEnergySaver.TabIndex = 39;
		this.buttonEnergySaver.Text = "Energy Saver";
		this.buttonEnergySaver.UseVisualStyleBackColor = false;
		this.buttonEnergySaver.Visible = false;
		this.buttonAmdOled.Activated = false;
		this.buttonAmdOled.BackColor = System.Drawing.SystemColors.ControlLight;
		this.buttonAmdOled.BorderColor = System.Drawing.Color.Transparent;
		this.buttonAmdOled.BorderRadius = 2;
		this.buttonAmdOled.Dock = System.Windows.Forms.DockStyle.Right;
		this.buttonAmdOled.FlatAppearance.BorderSize = 0;
		this.buttonAmdOled.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonAmdOled.Font = MechrevoLite.UI.UiVisualStyle.Font(8.5f, System.Drawing.FontStyle.Bold);
		this.buttonAmdOled.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.buttonAmdOled.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonAmdOled.Location = new System.Drawing.Point(640, 5);
		this.buttonAmdOled.Margin = new System.Windows.Forms.Padding(0);
		this.buttonAmdOled.Name = "buttonAmdOled";
		this.buttonAmdOled.Secondary = true;
		this.buttonAmdOled.Size = new System.Drawing.Size(180, 40);
		this.buttonAmdOled.TabIndex = 39;
		this.buttonAmdOled.Text = "AMD OledSaver";
		this.buttonAmdOled.UseVisualStyleBackColor = false;
		this.buttonAmdOled.Visible = false;
		this.labelVersion.Cursor = System.Windows.Forms.Cursors.Hand;
		this.labelVersion.Dock = System.Windows.Forms.DockStyle.Left;
		this.labelVersion.Font = MechrevoLite.UI.UiVisualStyle.Font(9f, System.Drawing.FontStyle.Underline);
		this.labelVersion.ForeColor = System.Drawing.SystemColors.ControlDark;
		this.labelVersion.Location = new System.Drawing.Point(20, 5);
		this.labelVersion.Margin = new System.Windows.Forms.Padding(0);
		this.labelVersion.Name = "labelVersion";
		this.labelVersion.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
		this.labelVersion.Size = new System.Drawing.Size(399, 40);
		this.labelVersion.TabIndex = 38;
		this.labelVersion.Text = "";
		this.labelVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
		this.buttonHDRControl.Activated = false;
		this.buttonHDRControl.BackColor = System.Drawing.SystemColors.ControlLightLight;
		this.buttonHDRControl.BorderColor = System.Drawing.Color.Transparent;
		this.buttonHDRControl.BorderRadius = 5;
		this.buttonHDRControl.CausesValidation = false;
		this.buttonHDRControl.Dock = System.Windows.Forms.DockStyle.Fill;
		this.buttonHDRControl.FlatAppearance.BorderSize = 0;
		this.buttonHDRControl.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonHDRControl.ForeColor = System.Drawing.SystemColors.ControlText;
		this.buttonHDRControl.Location = new System.Drawing.Point(200, 84);
		this.buttonHDRControl.Margin = new System.Windows.Forms.Padding(4);
		this.buttonHDRControl.Name = "buttonHDRControl";
		this.buttonHDRControl.Secondary = false;
		this.buttonHDRControl.Size = new System.Drawing.Size(188, 12);
		this.buttonHDRControl.TabIndex = 14;
		this.buttonHDRControl.Text = "HDR Color Control";
		this.buttonHDRControl.UseVisualStyleBackColor = false;
		this.buttonHDRControl.Visible = false;
		base.AutoScaleDimensions = new System.Drawing.SizeF(192f, 192f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
		this.AutoSize = true;
		base.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		base.ClientSize = new System.Drawing.Size(849, 2075);
		base.Controls.Add(this.panelFooter);
		base.Controls.Add(this.panelVersion);
		base.Controls.Add(this.panelStartup);
		base.Controls.Add(this.panelBattery);
		base.Controls.Add(this.panelGPU);
		base.Controls.Add(this.panelPerformance);
		base.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
		base.MaximizeBox = false;
		base.MdiChildrenMinimizedAnchorBottom = false;
		base.MinimizeBox = false;
		this.MinimumSize = new System.Drawing.Size(821, 71);
		base.Name = "SettingsForm";
		base.Padding = new System.Windows.Forms.Padding(11);
		base.ShowIcon = false;
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "L-Mechrevo";
		this.panelBattery.ResumeLayout(false);
		this.panelBatteryTitle.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)this.pictureBattery).EndInit();
		this.panelFooter.ResumeLayout(false);
		this.panelFooter.PerformLayout();
		this.tableButtons.ResumeLayout(false);
		this.panelPerformance.ResumeLayout(false);
		this.panelPerformance.PerformLayout();
		this.tablePerf.ResumeLayout(false);
		this.panelCPUTitle.ResumeLayout(false);
		this.panelCPUTitle.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.picturePerf).EndInit();
		this.panelGPU.ResumeLayout(false);
		this.panelGPU.PerformLayout();
		this.tableAMD.ResumeLayout(false);
		this.tableGPU.ResumeLayout(false);
		this.panelGPUTitle.ResumeLayout(false);
		this.panelGPUTitle.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureGPU).EndInit();
		this.panelScreen.ResumeLayout(false);
		this.panelScreen.PerformLayout();
		this.tableScreen.ResumeLayout(false);
		this.panelScreenTitle.ResumeLayout(false);
		this.panelScreenTitle.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureScreen).EndInit();
		this.panelKeyboard.ResumeLayout(false);
		this.panelKeyboard.PerformLayout();
		this.tableLayoutKeyboard.ResumeLayout(false);
		this.tableLayoutKeyboard.PerformLayout();
		this.panelKeyboardTitle.ResumeLayout(false);
		this.panelKeyboardTitle.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureKeyboard).EndInit();
		this.panelStartup.ResumeLayout(false);
		this.panelStartup.PerformLayout();
		this.panelPeripherals.ResumeLayout(false);
		this.panelPeripherals.PerformLayout();
		this.tableLayoutPeripherals.ResumeLayout(false);
		this.panelVersion.ResumeLayout(false);
		base.ResumeLayout(false);
		base.PerformLayout();
	}

	/// <summary>是否见过有效风扇转速（30 系 GCU 恒为 -1，见 <see cref="TelemetryReserveSampleFor" />）。</summary>
	private bool _telemetryRpmSeen;

	/// <summary>2026-10-16（方案 A）：功耗列是否见过有效值 —— 没见过就不为它预留宽度
	///（否则在根本没有功耗读数的机器上白留一列；见 <see cref="TelemetryReserveSampleFor" />）。</summary>
	private bool _telemetryPowerSeen;

	/// <summary>2026-10-16（方案 A）：占空比列是否见过有效值 —— 同上（宝龙达下 duty 恒 null ⇒ 永不预留）。</summary>
	private bool _telemetryDutySeen;

	/// <summary>
	/// 读数片的**预留样本**（防跳动用的典型值，恒不小于当前实际文本的形状）。
	/// 2026-10-16（遥测排版方案 A）起**按片**给（原来只有一片、只有一份样本）：
	/// **名字片**（CPU/GPU）两片共用「更宽的那个」⇒ 两行的名字列等宽（否则后面所有列整体错开 1px）；
	/// **温度片**不预留（与原实现一致：宽度只跟当前文本走，两行正常情况下都是 "NN°C"、本身等宽）；
	/// **功耗 / 转速 / 占空比**三列各自预留（"123W" / "1234rpm" / "100%"），各自**见过有效值**才预留，
	/// 且**同一个 seen 旗标对两行同时生效** ⇒ CPU 行与 GPU 行的同名列宽度必然相等 ⇒ 逐列对齐。
	/// ⚠️ 用 seen 旗标而不是无条件预留：宝龙达下 duty 恒 null、有的机器没有功耗读数，
	/// 无条件预留会在那些机器上白留两列（历史教训：读数一变宽就把左组挤到裁字）。
	/// ⚠️ 这套"按机型能力升级预留"的逻辑（30 系 GCU 不报转速 ⇒ 只按占空比预留，避免留一段空档）
	/// 与原实现同源，只是从"写进 MinimumSize"改成"存成样本"来表达 —— 因为
	/// <see cref="Control.MinimumSize" /> 是**棘轮**（只能调大）：原实现把预留写进 MinimumSize，
	/// 见到转速后它被抬到含 rpm 的宽、之后再也降不下来（30 系机型的空档因此永久存在）。
	/// </summary>
	private string? TelemetryReserveSampleFor(Label piece)
	{
		// 2026-10-16（方案 A）：**名字片也要预留**，而且两片共用同一份 —— 取「CPU / GPU 里更宽的那个」。
		// ⚠️ 为什么必须：`GPU` 比 `CPU` 宽 1px（本机 96 DPI 实测 52 vs 51）。名字列不等宽 ⇒ 它后面
		// **所有**列都整体错开 1px（拆列后本机日志实测确实如此）⇒ 「两行严格对齐」就差这一口气。
		// 取两者更宽的那个 ⇒ 无论字体里谁更宽，两行的名字列都等于同一宽度。
		if (piece == _telemetryCpuName || piece == _telemetryGpuName)
		{
			if (TelemetryTextWidth("CPU", piece.Font, ContentAlignment.MiddleCenter) >= TelemetryTextWidth("GPU", piece.Font, ContentAlignment.MiddleCenter))
			{
				return "CPU";
			}
			return "GPU";
		}
		if (piece == _telemetryCpuPower || piece == _telemetryGpuPower)
		{
			return _telemetryPowerSeen ? TelemetryPowerSample : null;
		}
		if (piece == _telemetryCpuRpm || piece == _telemetryGpuRpm)
		{
			return _telemetryRpmSeen ? TelemetryRpmSample : null;
		}
		if (piece == _telemetryCpuDuty || piece == _telemetryGpuDuty)
		{
			return _telemetryDutySeen ? TelemetryDutySample : null;
		}
		return null;
	}

	/// <summary>
	/// 2026-10-11（真机「叠在一起了」修复）：某片读数**希望占的宽度** ——
	/// = max(当前文本实测宽, 该片的预留样本宽) + 两侧各一份 <see cref="TelemetryGapLogical" />。
	/// 预留的作用是"读数小幅变化时不横向跳动"；两者取大 ⇒ 宽度永远不小于真实文本宽、不会折行。
	///
	/// 2026-10-11（用户原话「CPU GPU读数的间隔不统一 优化一下」＋「主要是温度那个」）：
	/// ⭐ **片与片之间的"间隔"就在这一处统一**。改前各片宽度 = 各自的实测文本宽（无内边距口径），
	/// 而绘制是**右对齐**（<see cref="AppendTelemetryPieces" /> 下发的 <c>MiddleRight</c>）⇒
	/// 两片文本之间真实的空档 = 相邻两片的"实测宽 − 实际画出宽"之和：名字片（`CPU`，只有 3 个字母、
	/// 末尾无字形外伸）几乎吃满自己那一片 ⇒ 它右边与温度片之间只剩 1~2 设备像素（用户看到的
	/// "`CPU 60°C` 贴在一起"）；而温度片/余项片末尾是数字，实测宽比画出宽多 1~2px ⇒
	/// 后面的空档反而更大 —— 这就是"间隔不统一"的根因（不是列样式、也不是外边距，
	/// 见 <see cref="TelemetryPieceWidth" /> 的口径说明）。
	/// 修法：**每片都在文本两侧各留一份固定 <see cref="TelemetryGapLogical" />**，并且片内文本
	/// **居中**（<see cref="AppendTelemetryPieces" />）⇒ 任何相邻两片文本之间的空档恒等于
	/// <c>2 × TelemetryGapLogical</c>（与文本内容、与"实测宽 vs 画出宽"的差都无关）。
	/// </summary>
	private int TelemetryPieceDesiredWidth(Label piece)
	{
		// ⚠️ 2026-10-16（方案 A）：按**该片自己的对齐方式**量宽 —— 右对齐片的末位字形贴右缘，
		// 安全余量比居中/左对齐片多 1px（见 TelemetryTextWidth）；⛔ 仍不能写死 MiddleCenter。
		int num = TelemetryPieceWidth(piece.Text, piece.Font, piece.TextAlign);
		string text = TelemetryReserveSampleFor(piece);
		if (text != null)
		{
			num = Math.Max(num, TelemetryPieceWidth(text, piece.Font, piece.TextAlign));
		}
		// ⭐ 2026-10-16（用户原话「传感器读数间距太大了 …小一点但别贴在一起」）：**空片且无预留 ⇒ 宽度归零**。
		// 上面那条算式对空文本仍给出 <c>TelemetryTextWidth("") + 2 × GapLogical</c>（实测 = 2 + 2 × GapLogical），
		// 那是一段"什么都不显示、却照样占位"的死宽度：宝龙达下没有功耗读数（`_telemetryPowerSeen` 为假）时，
		// 它正好夹在「温度」与「转速」两片之间 ⇒ 白白多出一个空档。真机日志 `[70°C|w=44][|w=12][2705rpm|w=76]`
		// 就是这个形状（那 12px 里有 10px 是死宽度）。归零后该列宽 = 0，两片直接按 <c>2 × GapLogical</c> 相邻。
		// ⚠️ 只在「文本为空 **且** 没有预留样本」时归零 —— 只要 `seen` 为真（任一行真的要显示该字段），
		// 仍按预留样本给宽；那是"两行逐列对齐 + 防跳动"的既有语义，⛔ 不碰。
		// ⚠️ 不破坏 `w == want` 那条回归断言：<see cref="TelemetryReserveSampleFor" /> 对同一字段的 CPU/GPU
		// 两片返回同一个值（`seen` 是全局的），所以"文本为空"必定两行同时成立 ⇒ 列宽与控件宽同时为 0。
		if (string.IsNullOrEmpty(piece.Text) && text == null)
		{
			return 0;
		}
		return num;
	}

	/// <summary>
	/// 2026-10-11（用户原话「CPU GPU读数的间隔不统一 优化一下」＋「主要是温度那个」）：**读数片的统一间距**。
	/// 每一片在文本左右各留一份本值（逻辑像素）⇒ 相邻两片文本之间的空档恒为 <c>2 × 本值</c>；
	/// 行最右那片文本离卡片右沿也是 <c>本值 + TextSafetyRightLogical</c>（见 <see cref="TelemetryPieceWidth" />）。
	/// 取 3（2026-10-16 用户原话「传感器读数间距太大了 修一下吧 小一点但别贴在一起」⇒ 由 5 收窄）：
	/// <b>下限约束</b>：相邻两片读数文本之间**恒为 <c>2 × 本值</c> = 6 逻辑像素**（8.5pt 下约合两个空格，
	/// 肉眼可辨"没贴在一起"）⇒ ⛔ 不许再调小到 2 及以下；也⛔ 不许把某一侧留白改成比本值更小 ——
	/// 那会破坏"相邻空档与文本内容无关"这条不变量（见 <see cref="TelemetryPieceWidth" /> 与
	/// <see cref="AppendTelemetryPieces" /> 的两条口径说明）。
	/// ⚠️ 收窄**只动间距**，⛔ 不动任何预留样本 —— 缩小预留会重新引入"读数一变宽就把左组挤到裁字"
	/// （历史记录见 <see cref="TelemetryReserveSampleFor" /> 与 :14012-14031）。
	/// </summary>
	private const int TelemetryGapLogical = 3;

	/// <summary>
	/// 2026-10-11（用户原话「CPU GPU读数的间隔不统一 优化一下」）：**读数片宽度**的唯一算式。
	/// = 文本实测宽（<see cref="TelemetryTextWidth" />，与 <c>RTelemetryLabel.OnPaint</c> 的 NoPadding 口径一致）
	/// + <see cref="TelemetryGapLogical" />（左）+ <c>max(TelemetryGapLogical, TextSafetyRightLogical)</c>（右）。
	/// ⚠️ 右侧取 max 是"只加不减"：既保住原来给右对齐末位字形的那份安全余量，又保证两侧的**片内留白相等**
	/// （文本居中 ⇒ 相邻两片文本之间的空档仍是 2 × GapLogical）。
	/// </summary>
	private static int TelemetryPieceWidth(string? text, Font font, ContentAlignment align)
	{
		int pad = Math.Max(TelemetryGapLogical, TextSafetyRightLogical);
		return TelemetryTextWidth(text, font, align) + TelemetryGapLogical + pad;
	}

	/// <summary>
	/// 2026-10-11（真机「叠在一起了」修复）：读数的**绘制口径**文本宽。
	/// Label 默认绘制（<see cref="TextFormatFlags.Default" />，带内边距）比"无内边距"实测宽 **+7px**
	/// （本机 96 DPI 实测："12W 2539rpm 35%" → NoPadding 105 / Default 112；该组数字是当时读数片还在用
	/// Consolas 8.5pt 时量的 —— 2026-10-11 起读数片字体已换成 UiStyleCaptionFont()＝HarmonyOS Sans SC，见
	/// <see cref="MakeTelemetryPiece" />；口径差 +7px 这一结论与字体无关，故算式不变、只是实测宽随字体变）——
	/// 旧代码按 <c>NoPadding + 4</c> 给列宽，于是 Label 在 109px 里画 112px 的字 ⇒ 末段 "35%" 被折到
	/// 第二行（真机截图正是这个形状）。
	/// 本修复把两侧口径统一成**无内边距**：读数片自己把 <see cref="Control.Padding" /> 清空、
	/// 由 <see cref="RTelemetryLabel" /> 自绘单行（文本矩形 = 整个客户区），所以这里按 NoPadding 实测。
	/// ⚠️ 安全余量按对齐方向给：右对齐的读数片最后一位字形就贴在客户区右缘（实测 "58°C" 末尾会被
	/// 切掉半个字形），需要 <see cref="TextSafetyRightLogical" />；左对齐片贴左缘，1px 足够。
	/// ⚠️ 2026-10-11（片间距统一）：片宽不再直接用本值，而是走 <see cref="TelemetryPieceWidth" />
	/// （本值 + 两侧统一留白）—— 本方法只剩"文本本身多宽"，⛔ 不再承担间距职责。
	/// </summary>
	private static int TelemetryTextWidth(string? text, Font font, ContentAlignment align)
	{
		// 按实现实测（96 DPI，当时读数片用 Consolas 8.5pt；现在读数片字体是 HarmonyOS Sans SC，
		// 见 MakeTelemetryPiece）：同一段文字，NoPadding 口径比 Label 默认口径
		//（TextFormatFlags.Default，带内边距）窄 7px —— 例如 "12W 2539rpm 35%"：105 vs 112。
		// 读数片现在自己清空 Padding 并由 RTelemetryLabel 自绘单行（文本矩形 = 客户区），
		// 所以宽度按 NoPadding 口径给才是"画出来的那个宽"；右对齐片的末位字形贴右缘，余量多给 1px。
		Size size = TextRenderer.MeasureText(text ?? "", font, new Size(4000, 100), TextFormatFlags.NoPadding);
		return size.Width + ((IsRightAligned(align) ? TextSafetyRightLogical : TextSafetyLogical));
	}

	private static bool IsRightAligned(ContentAlignment align)
	{
		return align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight;
	}

	/// <summary>左对齐读数片的安全余量（逻辑像素）。见 <see cref="TelemetryTextWidth" />。</summary>
	private const int TextSafetyLogical = 1;

	/// <summary>右对齐读数片的安全余量（逻辑像素）：末位字形贴右缘，留够半个字形宽（1px 实测会切掉半边）。</summary>
	private const int TextSafetyRightLogical = 2;

	/// <summary>
	/// 2026-10-16（遥测排版方案 A）：**十片**读数（CPU/GPU 各五片：名字/温度/功耗/转速/占空比）。
	/// ⚠️ 拆列前是六片（每行三片，第三片把功耗/转速/占空比用空格拼在一起）—— 见
	/// <see cref="TelemetryReserveSampleFor" /> 与 <see cref="TelemetryPieceAlign" /> 的说明。
	/// 顺序 = 行内从左到右，且 CPU 行在前、GPU 行在后（与 BuildTitleRow 的两次调用一致）。
	/// </summary>
	private Label[] TelemetryPieceArray()
	{
		return new Label[10]
		{
			_telemetryCpuName, _telemetryCpuTemp, _telemetryCpuPower, _telemetryCpuRpm, _telemetryCpuDuty,
			_telemetryGpuName, _telemetryGpuTemp, _telemetryGpuPower, _telemetryGpuRpm, _telemetryGpuDuty
		};
	}

	/// <summary>2026-10-16（方案 A）：某一行（CPU 或 GPU）的五片读数，按行内顺序。</summary>
	private Label[] TelemetryRowPieces(bool isCpu)
	{
		if (isCpu)
		{
			return new Label[5] { _telemetryCpuName, _telemetryCpuTemp, _telemetryCpuPower, _telemetryCpuRpm, _telemetryCpuDuty };
		}
		return new Label[5] { _telemetryGpuName, _telemetryGpuTemp, _telemetryGpuPower, _telemetryGpuRpm, _telemetryGpuDuty };
	}

	/// <summary>
	/// 2026-10-16（遥测排版方案 A）：**片内文本的对齐方式**。
	/// 名字/温度片沿用 <c>MiddleCenter</c>（2026-10-11「间隔统一」那一轮的口径：片内留白左右相等 ⇒
	/// 相邻两片文本之间的空档恒为 <c>2 × TelemetryGapLogical</c>，⛔ 不动它）。
	/// 功耗/转速两片用 <c>MiddleRight</c>：数字列惯用右对齐，位数变化时**右缘恒对齐**
	///（例如 <c>999rpm</c> 与 <c>1790rpm</c> 居中对齐会左右各差半个字宽）。
	/// <para>
	/// ⭐ 2026-10-16（方案 X · 用户原话「传感器读数间距太大了 …小一点但别贴在一起」）：**占空比片改 <c>MiddleLeft</c>**。
	/// <b>理由</b>：它是**行内最后一列**（追加顺序见 <see cref="TelemetryPieceArray" /> /
	/// <see cref="BuildTelemetryRow" />，占空比之后不再有任何片）⇒ 它的预留余量应当留在**行外侧**，
	/// ⛔ 不该变成与转速列之间的空档。右对齐时余量全落在左边：占空比按 <c>"100%"</c> 预留、
	/// 而实际显示 <c>49%</c> 这种两位数时，差出来的一个字宽就夹在 <c>2296rpm</c> 与 <c>32%</c> 之间
	///（真机反馈点名的正是这一处；本机实测该空档 ≈18px，而其余相邻空档只有 <c>2 × GapLogical</c> = 6px）。
	/// 左对齐后**所有相邻空档恒为 6px**，正好恢复本类注释里那条不变量；而且片宽、预留样本、
	/// 取证口径（<see cref="TelemetryPowerSample" /> 等）一字未动 ⇒ 零抖动，也不会重演
	///「位数变化列宽会跳」那类历史回归。代价仅是占空比两位数的行右端多出约一个字的空白（在行外侧）。
	/// </para>
	/// ⚠️ <b>前提是「占空比恒为行内最后一列」</b>：若将来在它右边再加一片（新字段/新列），
	/// 这个左对齐就会把余量推进**新的空档**里 ⇒ 那时必须把它改回 <c>MiddleRight</c>（或另想余量去处）。
	/// </summary>
	private ContentAlignment TelemetryPieceAlign(Label piece)
	{
		if (piece == _telemetryCpuDuty || piece == _telemetryGpuDuty)
		{
			return ContentAlignment.MiddleLeft;
		}
		if (piece == _telemetryCpuPower || piece == _telemetryGpuPower || piece == _telemetryCpuRpm || piece == _telemetryGpuRpm)
		{
			return ContentAlignment.MiddleRight;
		}
		return ContentAlignment.MiddleCenter;
	}

	private void BuildTelemetryRow()
	{
		// 2026-10-11（UI 重排第二轮 · 用户原话「性能模式旁边的指示性能的文字删除，把 CPU 的读数放在性能模式
		// 最右边 显卡的读数放显卡的最右边 然后风扇强冷的开关（也包括隐藏的显卡超频 编辑配置之类）
		// 贴到性能模式的右边排列 上个AI改的不对」）：
		// ⛔ 上一轮把读数塞进「标题行」（图标 + 性能模式那一行）的右端，与用户要的「模式**行**最右」不是同一行
		//    （标题行在按钮行上方），观感上就是"读数悬在性能模式旁边、下面还有一行空标题"。
		// ✅ 本轮按原话重建：读数与快捷开关组全部落到**模式按钮那一行**（含性能模式五颗按钮 / 显卡模式三颗按钮），
		//    该行 = [模式按钮(表格)] [风扇强冷+显卡OC+配置编辑 成组] [CPU/GPU 读数 → 行最右]。
		// 标题行恢复成「图标 + 性能模式 / 显卡模式」，不再挂任何读数或开关。
		// 2026-10-11（用户原话「这个温度颜色是不是和其他的不一样 统一一下」→「全部改成白色」→
		// 「其他读数的颜色向着温度字的颜色对齐」＋「浅色模式的话也是可读性要高一点(其他向着温度对齐
		// 还有这个读数的字体是鸿蒙字体吗 不是的话改」）：**六片读数一律取同一档前景色**。
		// ⛔ 不再有 `bright` 例外：温度片原来那一档亮色（= UiVisualStyle.Text，即主题的**主文字色**）
		// 现在就是**所有片**的颜色 —— 即「温度片是基准色、其它片向它对齐」。
		// 该色**随主题解析**（夜 240,240,240 / 日 22,32,46，见 UiVisualStyle 的 NightPalette/DayPalette），
		// 所以浅色主题下同样是"该主题里对比度最高的主文字色"，⛔ 不是硬编码白（白字在浅底上不可读）。
		// 主题切换时由 UiVisualStyle.ApplyWindow → ApplyTree → ResolveLabelForeground 按"近灰 ⇒ Text"
		// 重新解析（旧主题的主文字色是近灰色，chroma < 48），⛔ 不需要在 ApplyThemeMode 里另加一条。
		_telemetryCpuName = MakeTelemetryPiece("CPU");
		_telemetryCpuTemp = MakeTelemetryPiece("—");
		_telemetryCpuPower = MakeTelemetryPiece("");
		_telemetryCpuRpm = MakeTelemetryPiece("");
		_telemetryCpuDuty = MakeTelemetryPiece("");
		_telemetryGpuName = MakeTelemetryPiece("GPU");
		_telemetryGpuTemp = MakeTelemetryPiece("—");
		_telemetryGpuPower = MakeTelemetryPiece("");
		_telemetryGpuRpm = MakeTelemetryPiece("");
		_telemetryGpuDuty = MakeTelemetryPiece("");
		// 2026-10-16（方案 A）：对齐方式在建片时先落一次（宽度计算要用它），
		// 之后 AppendTelemetryPieces 会用同一个 TelemetryPieceAlign 再下发一次（幂等）。
		foreach (Label alignPiece in TelemetryPieceArray())
		{
			alignPiece.TextAlign = TelemetryPieceAlign(alignPiece);
		}
		// 2026-10-11（真机「配置编辑右缘被裁」修复 · 空间回收）：读数片自己**清掉 Label 的内边距**
		// （默认 3px 左右 + 3px 顶部）。理由：读数片是"固定宽 + 右对齐 + 自绘单行"的紧凑数字片，
		// 内边距没有任何视觉作用，却让每片白吃 6px 横向空间 —— 真机 420 逻辑窗宽下，标题行放
		// 「图标 + 标题 + 风扇强冷/显卡超频/配置编辑 + CPU/GPU 读数」本来就紧，这 36px（6 片 × 6px）
		// 正好是「配置编辑」右缘被裁的那一段。清掉之后：
		//   * 控件宽的实测口径（<see cref="TelemetryTextWidth" />）与绘制口径完全一致（都是无内边距）⇒
		//     既不会折行也不会为了"防折行"多留 7px；
		//   * 列改 AutoSize 后列宽 = 控件 PreferredWidth = 文本宽，不再把左组挤到没余量。
		// ⚠️ 预留宽**不写进 MinimumSize**（MinimumSize 是棘轮、只增不减，会把 30 系机型本可省下的
		// 空档永久留在行里）：预留改由 TelemetryPieceDesiredWidth 每帧"取大"表达。
		foreach (Label piece in TelemetryPieceArray())
		{
			piece.Padding = Padding.Empty;
			piece.AutoSize = false;
			piece.Height = piece.PreferredHeight;
			piece.Width = TelemetryPieceDesiredWidth(piece);
			piece.MinimumSize = Size.Empty;
		}
		LayoutTelemetryPieces();
		foreach (Label piece in TelemetryPieceArray())
		{
			// 读数可点击强制刷新传感器（沿袭原 labelCPUFan/labelGPUFan 的点击语义）
			piece.Click += LabelCPUFan_Click;
			int lastW = int.MinValue;
			int lastH = int.MinValue;
			piece.SizeChanged += (object? _, EventArgs _) =>
			{
				if (!_telemetryLayingOut)
				{
					int width = piece.Width;
					int height = piece.Height;
					if (width != lastW || height != lastH)
					{
						lastW = width;
						lastH = height;
						LayoutTelemetryPieces();
					}
				}
			};
		}
		BuildPerfModeRow();
		BuildGpuModeRow();
		UpdateTelemetryText();
	}

	/// <summary>
	/// 把性能卡片重建为**标题行在上、按钮行在下**两行（2026-10-11 第三轮 · 回退上一轮的错乱布局）：
	///   标题行 = [图标 | 「性能模式」 | 风扇强冷·显卡超频·配置编辑（左对齐、紧跟标题） … 弹性余量 … CPU 读数（右对齐）]
	///   按钮行 = [静音/平衡/狂暴/自定义]（**本行只有按钮**，占满整行宽度）
	/// 五颗模式按钮仍留在既有的 <c>tablePerf</c> 里（<see cref="ReflowPerformanceButtons" /> 的等宽重排、
	/// 显隐判据、点击语义**一字未改**）；
	/// ⛔ 不改任何开关的显隐条件（风扇强冷恒显示、显卡超频 <c>IsGcu50 &amp;&amp; 模式==1</c>、
	/// 配置编辑 <c>SupportsModeProfileConfig || Force30Ui</c>，全部由原调用点各自下发）。
	/// </summary>
	private void BuildPerfModeRow()
	{
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		_perfShortcuts = new FlowLayoutPanel
		{
			Name = "perfShortcuts",
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		// 2026-10-11（用户拍板「「风扇强冷」开关移回「更多开关」里面」）：这里**不再**把 _fanBoostBox
		// 塞进性能模式标题行 —— 它已回到「更多开关」的「电源与系统」组（见 BuildQuickSwitchRows 里
		// 取消 `key2 != "fanboost"` 门控的那一处）。标题行现在只留「显卡超频 + 配置编辑」两颗
		//（两者按能力位互斥，用户原话「50GCU 的功能不会与配置编辑同时出现 => 可共用同一位置」）。
		if (_gpuOcBox != null)
		{
			_perfShortcuts.Controls.Add(_gpuOcBox);
		}
		if (perfConfigEditButton != null)
		{
			_perfShortcuts.Controls.Add(perfConfigEditButton);
		}
		// 2026-10-12（用户原话「我发现它是底边和标题对齐的 能不能改成中线的标题对齐」）：
		// 快捷开关要的是**垂直中线**与「性能模式」标题对齐（改前是"贴着容器顶边"，观感＝偏上/底边对齐）。
		// 具体做法、为什么不能用 Anchor、为什么必须"布局之后再算"全部写在 CenterPerfShortcutsVertically 的注释里。
		// ⚠️ 这里先按"当前高度"摆一次（构造期字体还不是最终字体，值可能与最终差 1px），
		//    真正的口径由 SizeChanged 在字体/DPI 落定后自动校正 ⇒ 两个事件都要挂。
		// ⚠️ 匿名方法**不写参数表**：本文件不在 `#nullable` 上下文里，写 `(object? _, EventArgs _)`
		//    会多出 CS8632 警告（本轮基线 1445 个警告必须一个不增）。
		_perfShortcuts.SizeChanged += delegate
		{
			CenterPerfShortcutsVertically();
		};
		foreach (Control shortcut in _perfShortcuts.Controls.Cast<Control>())
		{
			// 快捷开关自带的上下 Margin（D(4)）会让"控件高 + 上下留白"超过标题行高 D(26) 而被裁；
			// 行内的垂直留白改由 CenterPerfShortcutsVertically 承担（左右 Margin 保留，横向间距不变）。
			shortcut.SizeChanged += delegate
			{
				CenterPerfShortcutsVertically();
			};
		}
		CenterPerfShortcutsVertically();
		_perfShortcuts.Anchor = AnchorStyles.Left;
		// 2026-10-11（真机布局修复）：左右外边距由 D(4)/D(2) 收到 0 —— 真机 420 逻辑窗宽下，标题行
		// 要同时装「图标 + 性能模式 + 风扇强冷/显卡超频/配置编辑 + CPU 读数」，实测左组内容比可用宽
		// 多出几像素（表现就是开关组右缘被裁）。开关组自身已经紧贴标题文字/读数列，去掉外边距后
		// 靠 TLP 的 3px 间隔视觉上仍分得开，省下的宽度正好补上缺口。
		_perfShortcuts.Margin = Padding.Empty;
		// 2026-10-11（本轮）：建好之后立刻按"当前有几个可见子控件"收敛一次宽度/可见性
		//（构造期 oc/cfg 都还是默认可见，之后能力位会把它们关掉，见 SyncPerfShortcutsVisibility）。
		SyncPerfShortcutsVisibility();
		// ① 标题行：[图标 | 「性能模式」 | 三个快捷开关（左对齐紧跟标题）… 弹性余量 … CPU 读数（右对齐）]
		_perfHeaderRow = BuildTitleRow("perfHeaderRow", picturePerf, labelPerf, new Control[1] { _perfShortcuts }, TelemetryRowPieces(isCpu: true));
		// ② 按钮行：**只有**模式按钮（本轮用户原话：「按钮行是不被挤占的」），按钮按百分比列占满整行宽度
		BufferedTableLayoutPanel perfModeRow = new BufferedTableLayoutPanel
		{
			Name = "perfModeRow",
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = 1,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Height = D(34),
			BackColor = UiVisualStyle.Window
		};
		_perfModeRow = perfModeRow;
		perfModeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tablePerf.Dock = DockStyle.Fill;
		tablePerf.Margin = Padding.Empty;
		perfModeRow.Controls.Add(tablePerf, 0, 0);
		// ③ 快捷磁贴行（2026-10-11 用户拍板）：用户原话「参考这个 在性能模式下面加一个快捷按钮…」
		//    「关闭屏幕那个按钮的位置改成加号按钮」
		//    「准确描述位置:性能模式按钮的下面一行（30系要在配置下拉的下面一行）」。
		BuildQuickTileRow();
		// 2026-10-12（用户原话「给按钮加个标题 叫快捷开关」）：磁贴行**正上方**那一行标题。
		BuildQuickTileHeaderRow();
		// ⚠️ 分层顺序（2026-10-11 第四轮 · 用户原话「三零系…变成 按钮行 配置行 磁贴行(从上到下)」）：
		//    本文件 BuildPerfConfigRow 写明的既有口径是「Dock=Top 按 Controls 索引**倒序**停靠」⇒ 索引大者贴顶。
		//    故把这些行按"从下往上"的顺序 Add：
		//      索引 0 = 磁贴行（**最下**，30 系也在配置行之下 —— 用户原话"这个加在下面"）；
		//      索引 1 = 「快捷开关」标题行（2026-10-12 用户原话"给按钮加个标题 叫快捷开关"）；
		//      索引 2 = 配置下拉行（30 系可见）⇒ 30 系从上到下 = 按钮行 -> 配置行 -> 标题行 -> 磁贴行；
		//      索引 3 = 按钮行（非 30 系时它就在标题行正上方）；
		//      索引 4 = 标题/性能行（最上，最后 Add ⇒ 贴顶）。
		//    ⛔ 磁贴行**不换行**（WrapContents 不适用）：列宽由 RebuildQuickTileButtons/DistributeQuickTileColumns
		//       按**实测文字**分配（见 ComputeQuickTileCellWidths），⛔ 窗口最小宽**不再**为它抬高
		//       （用户答复③「不要变宽：缩小磁贴让它一行放下」）。
		panelCPUTitle.SuspendLayout();
		panelPerformance.SuspendLayout();
		// 顺带丢掉设计器里那层旧表头容器（picturePerf/labelPerf 此刻已被 BuildTitleRow 移到新标题行上，
		// 旧容器已经空了 ⇒ 直接 Remove + Dispose，不留孤儿控件）
		foreach (Control oldHead in panelCPUTitle.Controls.Cast<Control>().ToArray())
		{
			panelCPUTitle.Controls.Remove(oldHead);
			oldHead.Dispose();
		}
		// 性能行（= 图标 + 「性能模式」 + 快捷开关 + CPU 读数）独占 panelCPUTitle，按钮行/配置行/磁贴行
		// 依次移到卡片面板上
		panelCPUTitle.Controls.Add(_perfHeaderRow);
		panelCPUTitle.AutoSize = false;
		panelCPUTitle.Height = _perfHeaderRow.Height;
		panelCPUTitle.ResumeLayout(performLayout: true);
		panelPerformance.Controls.Clear();
		if (_quickTileRow != null)
		{
			panelPerformance.Controls.Add(_quickTileRow);        // 索引 0 ⇒ 最下（磁贴行）
		}
		if (_quickTileHeaderRow != null)
		{
			panelPerformance.Controls.Add(_quickTileHeaderRow);  // 索引 1 ⇒ 磁贴行之上（「快捷开关」标题行）
		}
		if (perfConfigRow != null)
		{
			panelPerformance.Controls.Add(perfConfigRow);         // 索引 2 ⇒ 标题行之上（30 系可见）
		}
		panelPerformance.Controls.Add(perfModeRow);               // 索引 3 ⇒ 按钮行
		panelPerformance.Controls.Add(panelCPUTitle);             // 索引 4 ⇒ 贴顶（性能行）
		// 高度 = 上内边距 + 性能行 + 按钮行 + 配置行（可见时）+ 「快捷开关」标题行 + 磁贴行；每项都取自控件真实高度
		RefreshPerfPanelHeights();
		// ⚠️ 必须恢复布局：本方法开头的 SuspendLayout 若没有这一句，卡片会永远停在"暂停布局"状态 ——
		//    卡片自身被父容器拉伸到正确宽度，而**它的子行**（标题行/按钮行/配置行/标题/磁贴行）
		//    停在 Suspend 前那次的陈旧宽（取证实测 200 设备 px，磁贴全被 EndEllipsis 截断、
		//    整张卡片内容挤在左半边）。2026-10-12 本轮曾因重写这一段而漏掉它、已修回。
		panelPerformance.ResumeLayout(performLayout: true);
	}

	/// <summary>
	/// 2026-10-12（用户原话「我发现它是底边和标题对齐的 能不能改成中线的标题对齐」）：
	/// 让 <c>perfShortcuts</c> 里的快捷开关（「GPU 超频」/「配置编辑」）**垂直中线**与「性能模式」标题对齐。
	///
	/// <para><b>为什么改前是"底边/贴顶"</b>：标题 <c>labelPerf</c> 与容器 <c>perfShortcuts</c> 都是
	/// <c>Anchor=Left</c>，TableLayoutPanel 对"不含 Top/Bottom 的 Anchor"是**垂直居中**摆放（两侧各留一半），
	/// 所以容器本身是居中的；但 <b>FlowLayoutPanel 排子控件只用 <c>Margin</c></b>（<c>Anchor</c>/<c>Dock</c>
	/// 对 FLP 的子控件排布**不生效**）且一律贴容器顶边 ⇒ 容器居中、内容却整体偏上。
	/// 100% DPI 实测：容器 26 高、<c>gpu_oc</c> 16 高 ⇒ 开关中线 55、标题中线 60（Δ=5px，底边差 6px）。</para>
	///
	/// <para><b>改法</b>（两步，都在本位方法内）：① 容器高收敛到"最高那颗子控件"（⇒ TLP 把容器整块居中，
	/// 且容器高 == 内容高，不会裁到子控件）；② 每颗子控件把"容器高 − 自身高"的**一半**补到 <c>Margin.Top</c>
	/// （另一半给 Bottom 保持对称）⇒ 子控件中线 == 容器中线 == 行中线 == 标题中线。</para>
	///
	/// <para><b>⚠️ 为什么必须"布局之后再算"</b>：<c>RCheckBox</c> 是 AutoSize，其高度 =
	/// <c>max(滑轨 16, 文本实测高)</c>（见 <c>RCheckBox.MeasurePreferredSize</c>），而正文用的是**主题字体**
	/// （<c>HarmonyOS Sans SC 9pt</c>，由 <c>UiVisualStyle.ApplyWindow/ApplyThemeMode</c> 在 Show 之后下发）
	/// ⇒ 构造期按当时字体算出来是 17、最终是 16（实测差 1px）。所以本方法挂到容器/子控件的
	/// <c>SizeChanged</c> 上，**每次尺寸落定（含主题字体、DPI、显隐引起的重排）都重算一次**。</para>
	///
	/// <para><b>⛔ 不做什么</b>：⛔ 不动任何显隐判据、⛔ 不动左右 Margin（横向间距与列宽算式一字未改）、
	/// ⛔ 不改标题行自身高度（行高仍由标题行的 D(26) 决定 ⇒ 本行高度不变）。幂等：值没变就不赋值
	/// （避免 Margin↔Layout 互相触发）；再入由 <see cref="_perfShortcutCentering" /> 拦住。</para>
	/// </summary>
	private void CenterPerfShortcutsVertically()
	{
		FlowLayoutPanel flow = _perfShortcuts;
		if (flow == null || flow.IsDisposed || _perfShortcutCentering)
		{
			return;
		}
		int maxHeight = 0;
		foreach (Control shortcut in flow.Controls.Cast<Control>())
		{
			maxHeight = Math.Max(maxHeight, shortcut.Height);
		}
		if (maxHeight <= 0)
		{
			return;
		}
		// ⚠️ 容器高只在**行高之内**收敛：比行高还高就会把子控件顶出行外（行高未知时退回"最高子控件"）。
		int rowHeight = ((_perfHeaderRow != null && _perfHeaderRow.Height > 0) ? _perfHeaderRow.Height : maxHeight);
		int container = Math.Min(maxHeight, rowHeight);
		_perfShortcutCentering = true;
		try
		{
			if (flow.Height != container)
			{
				// ⛔ 只改高度；宽度/可见性由 BuildTitleRow / SyncPerfShortcutsVisibility 负责。
				flow.Height = container;
			}
			foreach (Control shortcut in flow.Controls.Cast<Control>())
			{
				int slack = Math.Max(0, container - shortcut.Height);
				// 上取整、下取整 ⇒ 两者之和恒等于 slack（子控件连同留白正好等于容器高，不会被裁）。
				Padding want = new Padding(shortcut.Margin.Left, (slack + 1) / 2, shortcut.Margin.Right, slack / 2);
				if (shortcut.Margin != want)
				{
					shortcut.Margin = want;
				}
			}
		}
		finally
		{
			_perfShortcutCentering = false;
		}
	}

	/// <summary>
	/// 性能卡片的高度 = 内边距 + 「标题行 + 模式按钮行」两行真实高度之和（两者都是 Dock=Top，按内容定高）。
	/// ⚠️ 不写死 D(66)：本轮把读数与快捷开关并进按钮行后，行高来源必须跟着实际控件走，否则会被裁。
	/// </summary>
	private int PerfPanelHeight()
	{
		int num = panelPerformance.Padding.Vertical;
		if (panelCPUTitle != null)
		{
			num += panelCPUTitle.Height;
		}
		if (perfConfigRow != null && perfConfigRow.Visible)
		{
			num += perfConfigRow.Height;
		}
		return num;
	}

	/// <summary>
	/// 把显卡卡片重建为**标题行在上、按钮行在下**两行（与性能卡片同一形状）：
	///   标题行 = [图标 | 「显卡模式」 | 状态文字（左对齐紧跟标题） … 弹性余量 … GPU 读数（右对齐）]
	///   按钮行 = [核显/混合/独显直连]（**本行只有按钮**，占满整行宽度）
	/// 三颗按钮仍留在既有的 <c>tableGPU</c> 里，显隐判据（<c>VisualiseGPUButtons</c> / <c>HideGPUModes</c>）一字未改。
	/// ⚠️ 行序同性能卡片：标题行必须**最后** Add（Dock=Top 按 Controls 索引倒序停靠 ⇒ 索引大者贴顶）。
	/// </summary>
	private void BuildGpuModeRow()
	{
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		// 拆掉设计器那层旧表头容器（图标 + 标题 + 状态文字），三颗控件复用（先解挂再丢容器，不 Dispose 控件）
		panelGPUTitle.SuspendLayout();
		foreach (Control oldHead in panelGPUTitle.Controls.Cast<Control>().ToArray())
		{
			foreach (Control inner in oldHead.Controls.Cast<Control>().ToArray())
			{
				oldHead.Controls.Remove(inner);
			}
			panelGPUTitle.Controls.Remove(oldHead);
			oldHead.Dispose();
		}
		// ① 标题行：[图标 | 「显卡模式」 | 状态文字 … 弹性余量 … GPU 读数（右对齐）]
		_gpuHeaderRow = BuildTitleRow("gpuHeaderRow", pictureGPU, labelGPU, new Control[1] { labelGPUFan }, TelemetryRowPieces(isCpu: false));
		// ② 按钮行：**只有**三颗模式按钮，占满整行宽度
		BufferedTableLayoutPanel gpuModeRow = new BufferedTableLayoutPanel
		{
			Name = "gpuModeRow",
			Dock = DockStyle.Top,
			AutoSize = false,
			ColumnCount = 1,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Height = D(34),
			BackColor = UiVisualStyle.Window
		};
		_gpuModeRow = gpuModeRow;
		gpuModeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableGPU.Dock = DockStyle.Fill;
		tableGPU.Margin = Padding.Empty;
		gpuModeRow.Controls.Add(tableGPU, 0, 0);
		panelGPUTitle.AutoSize = false;
		panelGPUTitle.Controls.Add(gpuModeRow);     // 索引 0 ⇒ 在下（按钮行）
		panelGPUTitle.Controls.Add(_gpuHeaderRow);  // 索引 1 ⇒ 贴顶（标题行）
		// 高度 = 两行的显式高度之和（⛔ 不再 MinimumSize/MaximumSize 锁死，也不再引用设计器里的旧高度）
		panelGPUTitle.Height = _gpuHeaderRow.Height + gpuModeRow.Height;
		panelGPUTitle.ResumeLayout(performLayout: true);
		Panel_GPU_ApplyHeight();
	}

	/// <summary>
	/// 显卡卡片高度 = 上内边距 + 标题面板上外边距 + 标题行块（标题行 + 按钮行，两者都在 <c>panelGPUTitle</c> 内）
	/// + 核显省电行（可见时）。
	/// ⚠️ panelGPUTitle.Height **本身就含两行** ⇒ 这里⛔不能再加一次 <c>_gpuModeRow.Height</c>
	/// （上一轮那样加等于白留 D(34) 空带）。
	/// </summary>
	private void Panel_GPU_ApplyHeight()
	{
		int num = panelGPU.Padding.Vertical + panelGPUTitle.Margin.Top + panelGPUTitle.Height;
		if (_igpuSavingRow != null && _igpuSavingRow.Visible)
		{
			num += _igpuSavingRow.Height;
		}
		panelGPU.AutoSize = false;
		panelGPU.Height = num;
	}

	/// <summary>
	/// 把一组读数标签依次追加到 <paramref name="head" /> 的第 <paramref name="column" /> 列起（每颗一列）。
	/// 读数片一律**固定宽**：AutoSize=True 时标签宽度恒等于文本宽度、读数会随文字长度"呼吸"、行右端对不齐；
	/// 且（2026-10-11 真机修复）AutoSize 下的宽度是"无内边距"口径、比实际绘制窄 7px，
	/// 会让读数在标签内折行。宽度由 <see cref="SyncTelemetryReserveWidth" /> 每帧按真实渲染口径同步。
	/// ⚠️ 列一律用 **AutoSize**（⛔ 不用 Absolute）：Absolute 列会先吃满自己声明的宽度、把左组那个
	/// Percent 列挤到没有余量，导致「风扇强冷/配置编辑」右缘被裁。
	/// ⚠️ 2026-10-11（用户原话「CPU GPU读数的间隔不统一 优化一下」＋「主要是温度那个」）：文本对齐由
	/// <c>MiddleRight</c> 改成 <c>MiddleCenter</c> —— 片宽 = 文本宽 + 两侧各一份
	/// <see cref="TelemetryGapLogical" />（见 <see cref="TelemetryPieceWidth" />），**居中**才能让
	/// "片内留白左右相等"，从而任何相邻两片文本之间的空档恒为 <c>2 × TelemetryGapLogical</c>；
	/// 若仍右对齐，空档还会被"实测宽 − 实际画出宽"那 0~2px 的差带偏（这正是改前名字片与温度片
	/// 之间只剩 1~2px、而后面几片更松的根因）。CPU / GPU 两行的片序与列样式完全相同 ⇒ 逐列对齐不变。
	/// </summary>
	private void AppendTelemetryPieces(TableLayoutPanel head, int column, params Label[] pieces)
	{
		int num = column;
		foreach (Label piece in pieces)
		{
			piece.AutoSize = false;
			piece.Dock = DockStyle.Fill;
			// 2026-10-16（方案 A）：对齐按片下发（名字/温度居中；功耗/转速/占空比右对齐）——
			// 见 TelemetryPieceAlign。原来这里把**所有**片一律设成 MiddleCenter，
			// 连 BuildTelemetryRow 里给旧 rest 片设的 MiddleRight 也被它覆盖掉了。
			piece.TextAlign = TelemetryPieceAlign(piece);
			piece.Width = TelemetryPieceDesiredWidth(piece);
			head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			head.Controls.Add(piece, num, 0);
			num++;
		}
	}

	private Label MakeTelemetryPiece(string text)
	{
		// 2026-10-11（真机「叠在一起了」修复）：改用 RTelemetryLabel（**永远单行**绘制，见该类注释）。
		// AutoSize 交给 BuildTelemetryRow 统一下发（全部 false + 按真实渲染口径实测固定宽）。
		// 2026-10-11（用户原话「全部改成白色」/「其他读数的颜色向着温度字的颜色对齐」＋「浅色模式…可读性
		// 要高一点」）：**六片同色** = 主题主文字色 UiVisualStyle.Text（夜 #F0F0F0 / 日 #16202E）。
		// 改前温度片是这一档、其余四片是 UiVisualStyle.Muted（次要灰）⇒ 用户看到「47°C 比 CPU/rpm/% 亮」。
		// 2026-10-11（用户原话「这个读数的字体是鸿蒙字体吗 不是的话改」）：字体由硬编码的
		// `new Font("Consolas", …)` 换成**本仓库统一的字体入口** UiStyleCaptionFont()
		//（= UiVisualStyle.Font(8.5f)，族名 HarmonyOS Sans SC：优先用随包内嵌的
		//  MechrevoLite.UI\Fonts\HarmonyOS_Sans_SC_*.ttf，缺文件时回落 Microsoft YaHei UI）。
		// ⚠️ 字号仍 8.5pt（与改动前同档）；⛔ 只改读数片这一处字体，⛔ 未动全应用字体。
		return new RTelemetryLabel
		{
			Text = text,
			AutoSize = true,
			// 2026-10-11（用户原话「不许省略号/截断：必须显示完整数值」）：**关掉 AutoEllipsis**。
			// 宽度按绘制口径实测（见 TelemetryTextWidth）+ 自绘单行且与量宽同口径（见 RTelemetryLabel）
			// ⇒ 正常情况下永远画得下；万一真排不下，宁可**横向裁掉**也不再显示 "…" 这种"半截读数"。
			AutoEllipsis = false,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleCaptionFont(),
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = Padding.Empty,
			Location = Point.Empty
		};
	}

	private static float UiStyleCaption()
	{
		return 8.5f;
	}

	internal void UpdateTelemetryText()
	{
		if (_telemetryCpuName != null && _telemetryGpuName != null && !IsDisposed)
		{
			MechrevoHw? hw = Program.hw;
			(string name, string temp, string power, string rpm, string duty) tuple = TelemetryParts(hw, isCpu: true);
			(string name, string temp, string power, string rpm, string duty) tuple2 = TelemetryParts(hw, isCpu: false);
			// 2026-10-16（方案 A）：十片**逐字段**下发 —— 拆列后每个字段各自一片，
			// ⛔ 不再把功耗/转速/占空比拼成一个字符串（那正是两行数字错开的根因）。
			if ((0u | (SetTelemetryPiece(_telemetryCpuTemp, tuple.temp) ? 1u : 0u) | (SetTelemetryPiece(_telemetryCpuPower, tuple.power) ? 1u : 0u) | (SetTelemetryPiece(_telemetryCpuRpm, tuple.rpm) ? 1u : 0u) | (SetTelemetryDutyPiece(_telemetryCpuDuty, tuple.duty) ? 1u : 0u) | (SetTelemetryPiece(_telemetryGpuTemp, tuple2.temp) ? 1u : 0u) | (SetTelemetryPiece(_telemetryGpuPower, tuple2.power) ? 1u : 0u) | (SetTelemetryPiece(_telemetryGpuRpm, tuple2.rpm) ? 1u : 0u) | (SetTelemetryDutyPiece(_telemetryGpuDuty, tuple2.duty) ? 1u : 0u)) != 0)
			{
				LayoutTelemetryPieces();
			}
		}
	}

	private static bool SetTelemetryPiece(Label? piece, string text)
	{
		if (piece == null || piece.Text == text)
		{
			return false;
		}
		piece.Text = text;
		return true;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系反馈）：⭐ **占空比片**专用 setter —— ⭐ &gt;100% 时用警示色提示
	/// （⭐ 30 系原厂允许到 140%；⭐ 只改**显示颜色**，⛔ 不改任何下发值 ✅）。
	/// </summary>
	private static bool SetTelemetryDutyPiece(Label piece, string text)
	{
		if (piece == null)
		{
			return false;
		}
		bool changed = piece.Text != text;
		if (changed)
		{
			piece.Text = text;
		}
		Color wanted = (ParseTrailingPercent(text) > 100) ? UiVisualStyle.Warn : UiVisualStyle.Text;
		if (piece.ForeColor.ToArgb() != wanted.ToArgb())
		{
			piece.ForeColor = wanted;
			changed = true;
		}
		return changed;
	}

	/// <summary>⭐ 解析 <c>"140%"</c> 这类文本里的百分比（⭐ 不是百分比格式 ⇒ -1）✅。</summary>
	private static int ParseTrailingPercent(string text)
	{
		if (string.IsNullOrEmpty(text) || text[text.Length - 1] != '%')
		{
			return -1;
		}
		int end = text.Length - 1;
		int start = end;
		while (start > 0 && char.IsDigit(text[start - 1]))
		{
			start--;
		}
		return (start < end && int.TryParse(text.AsSpan(start, end - start), out int value)) ? value : -1;
	}

	private void LayoutTelemetryPieces()
	{
		if (_telemetryCpuRpm == null || _telemetryGpuRpm == null)
		{
			return;
		}
		// 2026-10-11（真机「叠在一起了」修复）：每帧按**当前文本**的**真实渲染口径**实测并同步
		// 六片读数的控件宽（+ 所在列宽）；见到有效转速时把预留宽升级成含 rpm 的保证值（防跳动）。
		bool flag = SyncTelemetryReserveWidth();
		MechrevoHw? hw = Program.hw;
		if (!_telemetryRpmSeen && hw != null && (hw.CpuFanRpm >= 0 || hw.GpuFanRpm >= 0))
		{
			_telemetryRpmSeen = true;
			flag |= SyncTelemetryReserveWidth();
		}
		// 2026-10-16（方案 A）：功耗列/占空比列的预留闸 —— 判据是「这个字段**真的被显示出来了**」
		//（复用 TelemetryParts 那一套同一个格式化入口 ⇒ ⛔ 不会出现「预留了但永远不显示」的空列）。
		if (hw != null)
		{
			// ⭐ 2026-10-16（用户原话「传感器读数间距太大了 …小一点但别贴在一起」＋守住上面那句自述语义）：
			// 判据改用 **TelemetryParts 的返回值** —— 也就是**展示层同一个函数**的输出，⛔ 不再另取原始字段。
			// ⚠️ 改前为什么是错的：duty 那两个判据读的是 `hw.CpuFanDuty` / `hw.GpuFanDuty`（**只有 GCU 载荷会填**），
			// 而展示层在宝龙达下把 duty 判成"未知"（没有可靠分母 ⇒ 不打百分比，见 TelemetryParts 内那段注释）
			// ⇒ 两边**取值不同源** ⇒ 宝龙达上 `_telemetryDutySeen` 被那个恒为 0 的原始字段置真，
			// 于是"预留了但永远不显示"：真机日志 `[2678rpm|w=76][|w=51]` 的行尾白白空出 51px。
			// 这正是本段原来以为不可能发生的那种情况；改用 TelemetryParts 后由构造保证同源。
			(string name, string temp, string power, string rpm, string duty) cpuParts = TelemetryParts(hw, isCpu: true);
			(string name, string temp, string power, string rpm, string duty) gpuParts = TelemetryParts(hw, isCpu: false);
			if (!_telemetryPowerSeen && (cpuParts.power.Length > 0 || gpuParts.power.Length > 0))
			{
				_telemetryPowerSeen = true;
				flag |= SyncTelemetryReserveWidth();
			}
			if (!_telemetryDutySeen && (cpuParts.duty.Length > 0 || gpuParts.duty.Length > 0))
			{
				_telemetryDutySeen = true;
				flag |= SyncTelemetryReserveWidth();
			}
		}
		if (flag)
		{
			// 读数片的控件宽 / 列宽变了必须让标题行重排一次（按钮行不受影响）
			_perfHeaderRow?.PerformLayout();
			_gpuHeaderRow?.PerformLayout();
		}
		// 2026-10-11（真机「叠在一起了」修复）：列宽变了就重算标题行高度 —— 读数若真排不下（折行）
		// 就把行高给够，⛔ 不让它越出行外压到按钮行上（宿主的 row-overlap 断言就是查这个）。
		FitTitleRow(_perfHeaderRow, panelCPUTitle, _perfModeRow);
		RefreshPerfPanelHeights();
		FitTitleRow(_gpuHeaderRow, panelGPUTitle, _gpuModeRow);
		RefreshGpuPanelHeight();
		// 2026-10-11（真机「读数仍被省略号截断」取证）：把六片读数的**实际宽度 / 期望宽度 / 字体**
		// 与行宽写成一行日志（只在内容变化时落盘）。真机是 175% DPI + DPI 不感知进程，宿主固定 96 DPI
		// 复现不了；有这一行就能直接判断"是宽度没算对"还是"算对了但绘制时字更宽"。
		LogTelemetryWidths();
	}

	/// <summary>
	/// 2026-10-11：读数宽度取证日志（<see cref="Logger.WriteLineIfChanged" /> ⇒ 只在内容变化时落盘）。
	/// 报告：每片的 文本 / 实际控件宽 / 期望宽（按当前字体实测）/ 字体名+字号 / AutoSize / AutoEllipsis，
	/// 以及性能行与显卡行的行宽、左组宽。用来定位"真机读数被省略号截断"到底出在哪一步。
	/// </summary>
	private void LogTelemetryWidths()
	{
		System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
		foreach (Label piece in TelemetryPieceArray())
		{
			if (piece != null)
			{
				stringBuilder.Append($" [{piece.Text}|w={piece.Width}|want={TelemetryPieceDesiredWidth(piece)}|{piece.Font.Name} {piece.Font.SizeInPoints}pt|as={piece.AutoSize}|ell={piece.AutoEllipsis}]");
			}
		}
		int num = 0;
		if (_perfHeaderRow != null)
		{
			foreach (Control child in _perfHeaderRow.Controls)
			{
				if (child is TableLayoutPanel)
				{
					num = child.Width;
				}
			}
			stringBuilder.Append($" perfRow={_perfHeaderRow.ClientSize.Width} perfLeft={num}");
		}
		stringBuilder.Append($" gpuRow={((_gpuHeaderRow != null) ? _gpuHeaderRow.ClientSize.Width : 0)}");
		Logger.WriteLineIfChanged("telemetry-width", "[telemetry-width]" + stringBuilder.ToString());
	}

	/// <summary>
	/// 组装**标题行**：[左组 = 图标 + 标题文字 + 中段控件（左对齐、紧跟标题）+ 弹性余量][读数片 …（行最右、右对齐）]。
	/// ⚠️ 读数片各占一个 **AutoSize** 列（控件自身 AutoSize=false、宽度就是实测文本宽）⇒ 列宽 = 文本宽；
	/// 左组独占 Percent 列，拿余量。⛔ 不要改回 Absolute：绝对值列会先吃满自己声明的宽度，把左组挤到
	/// 连「风扇强冷 / 显卡超频 / 配置编辑」都放不下的程度（2026-10-11 真机「配置编辑右缘被裁」就是它）。
	/// 六片读数的控件宽/列宽由 <see cref="SyncTelemetryReserveWidth" /> 每帧按**真实渲染口径**同步。
	/// </summary>
	private BufferedTableLayoutPanel BuildTitleRow(string name, Control iconBox, Label nameLabel, Control[] middle, params Label[] readingPieces)
	{
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		// 左组：图标 + 标题 + 中段控件 + 弹性余量（余量把中段控件留在标题右侧，而不是把它们顶到行右端）
		BufferedTableLayoutPanel left = new BufferedTableLayoutPanel
		{
			Name = name + "Left",
			Dock = DockStyle.Fill,
			AutoSize = false,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		// 2026-10-11（真机布局修复）：图标列由 D(26) 收到 D(22) —— 图标 32x32 + SizeMode=Zoom，
		// 22x26 的格子里会等比缩到 22x22（原 26x26），观感几乎不变；省下的宽度用来补左组在
		// 420 逻辑窗宽（= 应用真实默认/最小 client 宽）下"图标 + 标题 + 风扇强冷/显卡超频/配置编辑"
		// 与右端读数之间的那几像素缺口（见 _perfShortcuts / TelemetryTextWidth 的注释）。
		left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(22)));
		if (iconBox != null)
		{
			iconBox.Dock = DockStyle.Fill;
			iconBox.Margin = Padding.Empty;
			left.Controls.Add(iconBox, 0, 0);
		}
		// 标题文字用 AutoSize 列（口径与 BuildHeadRow 的表头一致：图标 26 之后紧跟文字，中段控件才贴得上）
		nameLabel.AutoSize = true;
		nameLabel.Dock = DockStyle.None;
		nameLabel.Anchor = AnchorStyles.Left;
		nameLabel.TextAlign = ContentAlignment.MiddleLeft;
		nameLabel.Margin = Padding.Empty;
		left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		left.Controls.Add(nameLabel, 1, 0);
		int num = 2;
		if (middle != null)
		{
			foreach (Control control in middle)
			{
				if (control != null)
				{
					// 中段控件一律"按自身自然宽度、左对齐"排：
					// ⛔ 不能沿用上一版 BuildHeadRow 留下的 Dock=Fill + 旧宽度 —— 那会把陈旧宽度当成
					// 列宽（实测 200% DPI 下 labelGPUFan 撑到 400px 越出左组，parent-overflow）。
					control.Dock = DockStyle.None;
					control.Anchor = AnchorStyles.Left;
					if (control is Label middleLabel)
					{
						middleLabel.AutoSize = true;
					}
					left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
					left.Controls.Add(control, num++, 0);
					// 2026-10-11（真机布局修复）：中段控件（快捷开关组）的宽度按**子控件自然宽之和**
					// 精确算出来。⛔ 不能依赖 FlowLayoutPanel 自己的 AutoSize / PreferredSize：
					// 实测（9pt 中文 + RCheckBox 自绘）它会报 102px 而子控件实际需要 ≥105px，
					// 于是最右那颗「风扇强冷」在面板里被裁掉右缘（用户截图里那半截字）。
					if (control is FlowLayoutPanel flow)
					{
						control.AutoSize = false;
						int num2 = 0;
						foreach (Control shortcut in flow.Controls)
						{
							// 2026-10-12（本轮 · 同一缺陷的第二处）：构造期窗体未 Show ⇒ 子控件的**有效**
							// Visible 恒 false（见 SyncPerfShortcutsVisibility 的注释）。这里若按有效值求和，
							// num2 恒 0 ⇒ 下面 else 分支把宽度写成 0 ⇒ 即使 SyncPerfShortcutsVisibility
							// 刚把容器设成可见，宽度也会被这一处清零（"容器显示了但宽 0"）。
							// 同一口径：按**意图**求和（⛔ 算式与 else 分支的语义一字未改）。
							if (ShortcutIntendedVisible(shortcut))
							{
								num2 += shortcut.Width + shortcut.Margin.Horizontal;
							}
						}
						if (num2 > 0)
						{
							// 子控件自然宽之和 + D(2) 余量：RCheckBox 自绘的指示器/文字间距在
							// 不同 DPI 下会多出 1~2px，只按 Width 求和会刚好差一点（实测 94 vs 102）。
							control.Width = Math.Max(num2 + D(2), flow.PreferredSize.Width);
						}
						else
						{
							// 2026-10-11（本轮 · 去掉「风扇强冷」后的回归修复）：**一个可见子控件都没有**时
							// 也必须把宽度收敛到 0。原实现只在 num2 > 0 时写宽度 ⇒ 这种组合会把这个
							// FlowLayoutPanel 的**陈旧初始宽度**当成列宽留在标题行里（取证实测
							// `[empty-band] perfShortcuts Container {Width=200} has 2 children, none visible`；
							// 125%/150%/200% DPI 下被放大成 250/300/400 ⇒ `[left-fit] 左组…被裁` +
							// `[parent-overflow] perfShortcuts`）。语义上"没有可见开关"就该占 0 宽，
							// 与 num2>0 分支的"按子控件自然宽算"是同一口径。
							// ⚠️ 本轮之前这个组合不会出现（「风扇强冷」恒在组里），所以是随本轮改动一起修的。
							control.Width = 0;
						}
					}
				}
			}
		}
		left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		left.ColumnCount = left.ColumnStyles.Count;
		left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		// 行容器：高 D(26)（＝本文件既有标题行高，也是这三个开关原先所在行的高度）
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Name = name,
			Dock = DockStyle.Top,
			AutoSize = false,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Height = D(26),
			BackColor = UiVisualStyle.Window
		};
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		bufferedTableLayoutPanel.Controls.Add(left, 0, 0);
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		if (readingPieces != null && readingPieces.Length != 0)
		{
			// 每片各占一列，列一律 AutoSize；片内文本居中（见 AppendTelemetryPieces 的三条口径说明）。
			AppendTelemetryPieces(bufferedTableLayoutPanel, 1, readingPieces);
		}
		bufferedTableLayoutPanel.ColumnCount = bufferedTableLayoutPanel.ColumnStyles.Count;
		return bufferedTableLayoutPanel;
	}

	/// <summary>
	/// 让每一片读数按 <c>Max(本帧文本实测宽, 预留样本宽)</c>（都是"绘制口径 + 两侧统一留白"，见
	/// <see cref="TelemetryPieceDesiredWidth" />）同步控件宽，并把所在列**归一到 AutoSize**
	/// （列宽跟着控件 PreferredSize 走；漏同步就会裁字 / 把左组挤没余量 —— 见方法内的两段注释）。
	/// ⚠️ 2026-10-11 起片内文本是 <c>MiddleCenter</c>、列一律 AutoSize，⛔ 不再是"右对齐 + Absolute 列"。
	/// 返回是否真的改过（调用方据此决定要不要再跑一次布局）。
	/// </summary>
	private bool SyncTelemetryReserveWidth()
	{
		bool changed = false;
		foreach (Label piece in TelemetryPieceArray())
		{
			if (piece == null)
			{
				continue;
			}
			// 2026-10-11（真机「叠在一起了」修复）：宽度 = **绘制口径**实测宽（+方向相关的安全余量），
			// 与预留样本取大 —— 见 TelemetryPieceDesiredWidth / TelemetryTextWidth。
			int num = TelemetryPieceDesiredWidth(piece);
			if (piece.Width != num)
			{
				piece.Width = num;
				changed = true;
			}
			if (piece.Parent is TableLayoutPanel table)
			{
				int column = table.GetCellPosition(piece).Column;
				// 2026-10-11（真机「配置编辑右缘被裁」修复）：读数片的列从 Absolute 改成 **AutoSize**。
				// Absolute 列在任何情况下都**先拿满**自己声明的宽度，剩下的才给左组那个 Percent 列 ⇒
				// 读数片宽度一变大（旧代码折行、新代码按渲染口径给宽），左组就被同等地挤掉同样多 ——
				// 本机 420 逻辑窗宽实测：左组内容要 195px（26 图标 + 61 标题 + 108 快捷开关组），
				// Absolute 口径下左组只剩 ~167px ⇒ 快捷开关组右缘（"配置编辑"）被裁。
				// AutoSize 列只吃控件自身的 PreferredSize；而读数片的 AutoSize 已被关掉、宽度就是我们
				// 实测出来的文本宽 ⇒ 列宽 = 文本宽（不再吃满剩余空间），左组能拿回余量。
				if (column >= 0 && column < table.ColumnStyles.Count && table.ColumnStyles[column].SizeType != SizeType.AutoSize)
				{
					table.ColumnStyles[column] = new ColumnStyle(SizeType.AutoSize);
					changed = true;
				}
			}
		}
		return changed;
	}

	/// <summary>
	/// 2026-10-11（真机「叠在一起了」修复 · 标题行高度）：标题行必须**容纳它的全部内容** ——
	/// 返回 = <c>max(标题行自身高, 行内每个可见读数片"按自身宽度真实排版所需的高度")</c>。
	/// 正常情况下读数片是单行（高 = 字体高 14 &lt; 26），结果恒等于标题行自身高 D(26)，布局不变；
	/// 只有当某个读数片真的排不下（字体替换 / 异常长的读数）时行高才会**增大**，让"读数越出标题行、
	/// 压到下面的按钮行上"这种坏态不可能出现（用户要求的"两行永不重叠"）。
	/// </summary>
	private static int TitleRowFitHeight(TableLayoutPanel header)
	{
		int num = header.Height;
		foreach (Control child in header.Controls)
		{
			if (child is Label label && label.Visible)
			{
				// 与 Label 实际绘制同口径（TextFormatFlags.Default，含内边距）量高：
				// 宽度不够时它会折行，折行后的高度就是必须给够的行高。
				Size size = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(Math.Max(1, label.ClientSize.Width), int.MaxValue), TextFormatFlags.Default | TextFormatFlags.NoPrefix);
				if (size.Height > num)
				{
					num = size.Height;
				}
			}
		}
		return num;
	}

	/// <summary>
	/// 2026-10-11（真机「叠在一起了」修复）：标题行的高度按内容重算，并让宿主的行高跟着走
	/// （标题行 + 按钮行都是 Dock=Top，宿主高度 = 两行高度之和）—— 保证"标题行与按钮行矩形不相交"。
	/// 返回标题行最终高度。
	/// </summary>
	private int FitTitleRow(TableLayoutPanel header, Control host, Control modeRow)
	{
		if (header == null || host == null)
		{
			return 0;
		}
		int num = TitleRowFitHeight(header);
		if (num > header.Height)
		{
			header.Height = num;
		}
		// 2026-10-11（用户原话「把按钮放在性能行下面一行」）：按钮行已从宿主里搬走（性能卡片的按钮行
		// 现在挂在 panelPerformance 上，在配置下拉行之下），宿主高度不再等于"标题行 + 按钮行" ——
		// 这里只在显卡卡片那一类宿主上沿用两行之和，其余交给各自的 Refresh*PanelHeight。
		if (host == panelGPUTitle)
		{
			int num2 = ((modeRow != null) ? modeRow.Height : 0);
			if (host.Height != header.Height + num2)
			{
				host.Height = header.Height + num2;
			}
			return header.Height;
		}
		if (host == panelCPUTitle && host.Height != header.Height)
		{
			host.Height = header.Height;
		}
		return header.Height;
	}

	/// <summary>
	/// 2026-10-11（用户原话「把按钮放在性能行下面一行吧(30系则是配置下拉列表下面一行)」+ 第四轮
	/// 「三零系…按钮行 配置行 磁贴行(从上到下)」+ 2026-10-12「给按钮加个标题 叫快捷开关」）：
	/// 性能卡片高度 = 上内边距 + **性能行**（panelCPUTitle，含读数/快捷开关）+ **按钮行** +
	/// 配置下拉行（30 系可见）+ **「快捷开关」标题行** + **磁贴行**（最下）。
	/// 各行都是 Dock=Top，按索引倒序停靠（索引 0 = 磁贴行在最下、索引 4 = 性能行贴顶）⇒ 顺序恒定。
	/// 每项都取控件自身真实高度 ⇒ 与内容严格相等，⛔ 不写死 D(66)/D(96)。
	/// </summary>
	private void RefreshPerfPanelHeights()
	{
		if (panelCPUTitle == null || _perfModeRow == null)
		{
			return;
		}
		int num = panelPerformance.Padding.Vertical + panelCPUTitle.Margin.Top + _perfHeaderRow.Height + _perfModeRow.Height + QuickTileHeaderRowDeviceHeight() + QuickTileRowDeviceHeight();
		_perfHeightCompact = num;
		_perfHeightWithConfig = num + ((perfConfigRow != null) ? perfConfigRow.Height : 0);
		if (_perfHeightCompact > 0)
		{
			panelPerformance.AutoSize = false;
			panelPerformance.Height = ((perfConfigRow != null && perfConfigRow.Visible) ? _perfHeightWithConfig : _perfHeightCompact);
		}
	}

	/// <summary>
	/// 2026-10-12（「快捷开关」标题行）占用的高度（设备像素；未建时 0）。
	/// ⚠️ 与磁贴行同一个理由**不判 <c>Visible</c>**：本方法会在标题行还没挂到卡片上（父链未上屏）时被调到，
	/// 那时 WinForms 的"有效可见性"为假会把行高算成 0 ⇒ 卡片比内容矮一整行（parent-overflow）。
	/// </summary>
	private int QuickTileHeaderRowDeviceHeight()
	{
		return (_quickTileHeaderRow != null) ? _quickTileHeaderRow.Height : 0;
	}

	/// <summary>
	/// 磁贴行占用的高度（设备像素；未建时 0）。
	/// ⚠️ **不判 <c>Visible</c>**：WinForms 的 <c>Control.Visible</c> 是"**有效**可见性"（任一级父容器不可见
	/// 就返回 false），而本方法会在 <c>BuildPerfModeRow</c> 里、磁贴行还没挂到卡片上（父链未上屏）时被调到
	/// —— 那时它会返回 false 而把行高算成 0，卡片高度就会比内容矮整整一行（取证日志里表现为
	/// <c>[parent-overflow] perfQuickTiles ... outside parent client`）。本行按设计**恒显示**，
	/// 所以直接取自身高度。
	/// </summary>
	private int QuickTileRowDeviceHeight()
	{
		return (_quickTileRow != null) ? _quickTileRow.Height : 0;
	}

	/// <summary>2026-10-11（真机「叠在一起了」修复）：显卡卡片的标题行 + 按钮行按内容定高（同性能卡片口径）。</summary>
	private void RefreshGpuPanelHeight()
	{
		if (panelGPUTitle == null || _gpuHeaderRow == null || _gpuModeRow == null)
		{
			return;
		}
		panelGPUTitle.Height = _gpuHeaderRow.Height + _gpuModeRow.Height;
		Panel_GPU_ApplyHeight();
	}

	private static (string name, string temp, string power, string rpm, string duty) TelemetryParts(MechrevoHw? hw, bool isCpu)
	{
		if (hw == null)
		{
			return (name: isCpu ? "CPU" : "GPU", temp: "—", power: "", rpm: "", duty: "");
		}
		int temp = (isCpu ? hw.CpuTemp : hw.GpuTemp);
		int rpm = (isCpu ? hw.CpuFanRpm : hw.GpuFanRpm);
		// ⭐⭐ 2026-10-17（接手 · 按原厂口径修正转速百分比）：⭐ 宝龙达的百分比 = ⭐ RPM ÷ 6000 ✅
		//   依据：原厂 40 系控制台 v0.3.15 的 `float num = rpm / 60f`（⭐ 完整代码与真机对照见
		//   `MechrevoHw.BaolongdaFanRpmAtFullPercent` 的注释）。
		//   ⚠️ 旧实现先读功能号 21 当分母 —— 原厂把那个读数当**手动转速设定值**、⛔ 不是最大转速
		//      ⇒ 真机读到 50 ⇒ 分母 5000 ⇒ 1996 RPM 显示 40%，而官方是 33% ❌ 已弃用。
		//   ⭐ 真机对照（MRID6-23 / 4070）：官方 1965 RPM ⇒ 33%（1965 ÷ 6000 = 32.75 ✅）。
		//   ⚠️ 对 GCU 机型：`UsesBaolongda` 为假 ⇒ 走 else ⇒ 判据一字未改（含 duty=0 照旧显示 0%）✅
		int? duty;
		if (hw.UsesBaolongda)
		{
			int rpmMaxForPercent = hw.FanRpmMaxForPercent;
			int rpmNow = isCpu ? hw.CpuFanRpm : hw.GpuFanRpm;
			// ⭐⭐ 2026-10-18（机主新决定 · 30 系风扇基准）：
			//   ⭐ 30 系 100% = **4900 RPM**（机主实测：3500 压不住；原厂上限约 140% ≈ 4900 ⇒ 以其为 100% 基准）✅
			//   ⭐ 40 系 100% = 6000 RPM ✅
			//   ⚠️ 旧实现 `Math.Min(100, …)` ⇒ 超过 100% 的部分永远显示 100%，比官方少一截 ❌
			//   ⭐ 现在**不夹 100**，只留 140% 的显示安全上限（⛔ 只改显示，不改任何下发值）✅
			if (rpmMaxForPercent > 0 && rpmNow > 0)
			{
				int rawPercent = (int)Math.Round(rpmNow * 100.0 / rpmMaxForPercent);
				int displayPercent = Math.Min(MechrevoHw.BaolongdaFanPercentDisplayMax, rawPercent);
				duty = displayPercent;
				// ⭐ 日志自证：⭐ >100% 时记一行（⭐ 每个整数百分比只记一次，⛔ 不刷屏）✅
				LogBaolongdaFanPercent(isCpu, rpmNow, displayPercent);
			}
			else
			{
				duty = null;
			}
		}
		else
		{
			duty = isCpu ? hw.CpuFanDuty : hw.GpuFanDuty;
		}
		float? power = (isCpu ? HardwareControl.cpuPower : HardwareControl.gpuPower);
		// ⭐⭐ 2026-10-17（真机诊断包 0.291.9.9 定因 · 修「CPU/GPU 功率显示值与实际不符」）：
		//   ⚠️⚠️ 根因：⭐ `cpuPower`/`gpuPower` 是静态字段、**这里原来没有任何新鲜度判据** ❌
		//      ⇒ ⭐ 本地采样链一停（⭐ 以前只有自定义模式页/悬浮窗在采样 ⚠️），它们就**冻结在最后一次的值上**、
		//      ⭐ **一直当实时值显示** ❌ —— 真机日志证据：20:36:29 起 CPU 恒 29 W、GPU 恒 10 W，连续 4 分钟
		//      **一位都没动**，而同一行的 CPU 温度（60→62℃）与风扇（2241→2757 rpm）一直在变 ⚠️。
		//   ⭐ 修法：⭐ 只有「⭐ 刚刚真的被刷新过」⭐ 的功耗才显示 ⇒ 超时就不显示（⛔ 不拿冻结值冒充实时值 ✅）。
		//   ⭐ 现在 `HardwareControl` 有**与界面无关**的常驻采样泵（2.5 s）⇒ 正常情况永远新鲜 ✅；
		//      ⭐ 只有采样链真的停摆（降级/退出/系统挂起）时才回落到"不显示" ✅。
		//   ⚠️ 判据只碰"新鲜度"，⛔ **不改任何 W 的换算**（LHM 已是 W；NVML 的 mW 在 `NvmlHelper` 里已 /1000 ✅）；
		//      ⭐ GCU 机型的温度/风扇仍走厂商值，⛔ 与本判据无关 ✅。
		if (power.HasValue && !HardwareControl.LocalPowerIsFresh(HardwareControl.LocalPowerFreshAge))
		{
			power = null;
		}
		// 2026-10-16（方案 A）：三个字段**分开**返回（各占一片）；⛔ 不再在 TelemetryRest 里拼接。
		bool fanFresh = hw.IsFanInfoFresh(MechrevoHw.FanInfoMaximumAge, -1L);
		bool zeroIsMeaningful = hw.FanZeroIsMeaningful;
		return (name: isCpu ? "CPU" : "GPU", temp: TelemetryTemp(temp, hw.IsSensorInfoFresh(isCpu, MechrevoHw.FanInfoMaximumAge, -1L)), power: TelemetryPower(power), rpm: TelemetryRpm(rpm, fanFresh, zeroIsMeaningful), duty: TelemetryDuty(duty, fanFresh, zeroIsMeaningful));
	}

	/// <summary>⭐ 上次已记录过的宝龙达风扇百分比（⭐ 避免遥测每秒刷同一行；⭐ 只记 &gt;100% 的档）✅</summary>
	private static int _baolongdaCpuPercentLogged = -1;

	private static int _baolongdaGpuPercentLogged = -1;

	/// <summary>
	/// ⭐⭐ 2026-10-18（日志自证）：⭐ 宝龙达风扇百分比 **&gt;100%** 时记一行
	/// （⭐ 30 系原厂允许 140%；⭐ 每个整数百分比只记一次，⛔ 不刷屏）✅
	/// </summary>
	private static void LogBaolongdaFanPercent(bool isCpu, int rpmNow, int percent)
	{
		if (percent <= 100)
		{
			return;
		}
		int last = isCpu ? _baolongdaCpuPercentLogged : _baolongdaGpuPercentLogged;
		if (last == percent)
		{
			return;
		}
		if (isCpu)
		{
			_baolongdaCpuPercentLogged = percent;
		}
		else
		{
			_baolongdaGpuPercentLogged = percent;
		}
		Logger.WriteLine($"[fan] 百分比：rpm={rpmNow} ⇒ {percent}%（未夹 100）");
	}

	internal static string TelemetryTemp(int temp, bool fresh)
	{
		if (!fresh || temp <= 0)
		{
			return "—";
		}
		return $"{temp}°C";
	}

	/// <summary>
	/// ⭐ 主界面那行遥测的"⭐ 后半段"（⭐ 功耗 / ⭐ 转速 / ⭐ 百分比 ✅）。
	/// <para>⚠️ 2026-10-16（0.291.9.1）：⭐ <paramref name="duty" /> 由 <c>int</c> 改成
	/// <b><c>int?</c></b> ✅ —— ⭐ <c>null</c> = ⭐ <b>这个后端根本给不出占空比</b> ⚠️
	/// ⇒ ⭐ 那就**不显示百分比** ✅（⭐ 宝龙达 ✅）。⭐ 有值时的判据一字未改 ✅。</para>
	/// </summary>
	/// <summary>
	/// 2026-10-16（遥测排版方案 A）：**功耗段**的格式化 —— 与拆列前的判据逐字相同
	///（<c>power.HasValue &amp;&amp; power &gt; 0</c> ⇒ 整数 W）。
	/// </summary>
	internal static string TelemetryPower(float? power)
	{
		if (!power.HasValue || power.GetValueOrDefault() <= 0f)
		{
			return "";
		}
		return $"{(int)Math.Round(power.Value)}W";
	}

	/// <summary>2026-10-16（方案 A）：**转速段**的格式化（判据与拆列前逐字相同）。</summary>
	internal static string TelemetryRpm(int rpm, bool fanFresh, bool zeroIsMeaningful)
	{
		if (!fanFresh)
		{
			return "";
		}
		int num = ((!zeroIsMeaningful) ? 1 : 0);
		if (rpm < num)
		{
			return "";
		}
		return $"{rpm}rpm";
	}

	/// <summary>
	/// 2026-10-16（方案 A）：**占空比段**的格式化。
	/// ⚠️ <paramref name="duty" /> 是 <c>int?</c>（0.291.9.1 起）：<c>null</c> = 这个后端根本给不出占空比
	/// ⇒ **不打百分比**（宝龙达）；有值时的判据与改动前逐字相同。
	/// </summary>
	internal static string TelemetryDuty(int? duty, bool fanFresh, bool zeroIsMeaningful)
	{
		if (!fanFresh || !duty.HasValue)
		{
			return "";
		}
		int num = ((!zeroIsMeaningful) ? 1 : 0);
		if (duty.GetValueOrDefault() < num)
		{
			return "";
		}
		return $"{duty.GetValueOrDefault()}%";
	}

	/// <summary>
	/// 2026-10-16（方案 A）：拆列前的「后半段拼串」（功耗 空格 转速 空格 占空比）。
	/// ⚠️ 主界面**已不再用它**（十片各自渲染）；保留它只为兼容既有调用面 / 取证口径，
	/// 输出与拆列前**逐字相同**（三段都由上面三个同一入口产出，空段不参与拼接）。
	/// </summary>
	internal static string TelemetryRest(float? power, int rpm, int? duty, bool fanFresh, bool zeroIsMeaningful)
	{
		List<string> list = new List<string>(3);
		string text = TelemetryPower(power);
		if (text.Length > 0)
		{
			list.Add(text);
		}
		string text2 = TelemetryRpm(rpm, fanFresh, zeroIsMeaningful);
		if (text2.Length > 0)
		{
			list.Add(text2);
		}
		string text3 = TelemetryDuty(duty, fanFresh, zeroIsMeaningful);
		if (text3.Length > 0)
		{
			list.Add(text3);
		}
		return string.Join(" ", list);
	}

	private void SyncHzButtons()
	{
		if (_hzSegTable == null || _hzSegTable.IsDisposed)
		{
			return;
		}
		// ⭐⭐ 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**，不再依赖 GCU）：
		// **数据源改成 Win32 优先** —— 档位列表 = 内屏实测可用档位（EnumDisplaySettingsEx 枚举，60s 缓存），
		// 当前档 = 同一套 API 实测；GCU 的 `HzList` / `CurrentHz` **只在 Win32 读不到时兜底**。
		// 理由：① Win32 是"面板真给的"（GCU 曾报出面板物理不可能的最高档，见 ScreenNative 里的历史注释）；
		//       ② 它**与平台无关** ⇒ GCU 机型 / 老接口 / 将来宝龙达共用同一条实现。
		// ⚠️ EnumDisplaySettingsEx 会把同一分辨率下的重复 mode 都列出来 ⇒ 必须按数值**去重 + 降序**：
		//    去重是为了签名稳定（下面 _lastHzSignature 直接 join 这个列表）也避免建出重复按钮；
		//    降序 = 高在左（用户 2026-10-15 明确要求「之前高的在左边」）。
		IReadOnlyList<int> gcuList = Program.hw?.HzList ?? new List<int>();
		IReadOnlyList<int> win32Rates = Win32FallbackRates();
		IReadOnlyList<int> readOnlyList = ((win32Rates.Count > 0) ? win32Rates.Distinct().OrderByDescending((int hz) => hz).ToArray() : gcuList);
		bool usedWin32List = win32Rates.Count > 0;
		// 当前档：同样 Win32 优先（内屏认不出时 GetRefreshRate 会退化为"当前显示设备"，见该方法注释）；
		// 只有它读不到（<=0）才退回 GCU 的 currentHZ。
		int num = MechrevoLite.Display.ScreenNative.GetRefreshRate(MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false));
		bool usedWin32Current = num > 0;
		if (!usedWin32Current)
		{
			num = Program.hw?.CurrentHz ?? 0;
		}
		Logger.WriteLineIfChanged("hz-win32-fallback", $"hz source: gcu=[{string.Join(",", gcuList)}] win32=[{string.Join(",", win32Rates)}] current={num} used={(usedWin32List ? "win32" : "gcu")} currentSource={(usedWin32Current ? "win32" : "gcu")} btnCount={readOnlyList.Count}");
		// 2026-10-09（lightspeed，P1）：签名里**剔除 CurrentHz** —— 只有刷新率**列表**变了才重建按钮
		// （重建 = Controls.Clear + 每颗 new RButton + ApplyIndependentRow/UseOutlineSelection + 布局，
		// 本机实测 40~67ms/次，而开机路径上它至少被调 2 次）；纯 CurrentHz 变化（同一份列表里换了一档）
		// 只走下面的 else 分支更新 Activated —— 与改前"整行重建一遍再按新 CurrentHz 置 Activated"结果等价，
		// 因为重建分支里唯一的 CurrentHz 消费点就是 `button.Activated = item == num`（列表本身与它无关），
		// 而 SetScreenRowControls 只看按钮个数（个数只由列表决定 ⇒ 不重建也不会漏掉那次可见性下发）。
		string text = string.Join('|', readOnlyList);
		if (text != _lastHzSignature)
		{
			_lastHzSignature = text;
			_hzSegTable.SuspendLayout();
			_hzSegTable.Controls.Clear();
			_hzButtons.Clear();
			_hzSegTable.ColumnStyles.Clear();
			_hzSegTable.ColumnCount = Math.Max(1, readOnlyList.Count);
			foreach (int item in readOnlyList)
			{
				int value = item;
				RButton button = new RButton
				{
					Text = item.ToString(),
					Dock = DockStyle.Fill,
					Margin = Padding.Empty,
					Font = UiVisualStyle.Font(9f),
					Cursor = Cursors.Hand,
					AccessibleName = item + "Hz"
				};
				button.Click += async (object? _, EventArgs _) =>
				{
					if (!_syncingDisplay && !button.Activated)
					{
						_lastHzUi = DateTime.Now;
						// ⭐⭐ 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**，不再依赖 GCU）：
						// **唯一动作 = Win32**（ScreenControl.SetScreen ⇒ ScreenNative.SetRefreshRate ⇒
						// ChangeDisplaySettingsEx）—— 与平台无关 ⇒ GCU 机型 / 老接口 / 将来宝龙达共用同一条实现。
						// ⚠️⚠️ 2026-10-15（用户拍板：**不关自动，只提示**）：
						// 手选档位**不去动「自动刷新率」开关**（⛔ 不写配置、⛔ 不关）。
						// 但若此刻自动是**开着**的，就弹一条**说明性**提示，让用户知道"插拔电源时会按电源切档"——
						// 因为自动的触发已改成 ①（只认"真·插拔电源 + 开机首次"，见 Program.SetAutoModes 尾部
						// `if (init || flag)`）⇒ 解锁/唤醒/切显卡模式都**不会**来顶它，你手选的档会保持到下次真插拔。
						// ⚠️ 2026-10-15（用户拍板：**不关自动，只出一条 OSD**；措辞「已切换 XXHz 自动刷新率开启时，
						// 此设置可能会被覆盖」+「没必要搞这么复杂就一个OSD而已」）：
						// 手选档位**不去动「自动刷新率」开关**（⛔ 不写配置、⛔ 不关），只在自动开着时提示一句。
						// ⚠️ 刻意**不**做那些"聪明"判定（与目标档冲突才弹 / 面板最高档是否>60 / 每个档位只弹一次）——
						// 用户明确要求保持简单：文案里的「**可能**」已经覆盖了"有时不会真被覆盖"这层意思。
						if (AppConfig.Is("screen_auto"))
						{
							Logger.WriteLineIfChanged("hz-manual-auto-on", $"手选 {value}Hz，且「自动刷新率」开着：该设置可能会被覆盖（插拔电源时按电源切档）");
							ToastForm.ShowNotice(string.Format(Strings.HzManualWhileAutoOn, value));
						}
						bool flag8 = true;
						try
						{
							ScreenControl.SetScreen(value);
						}
						catch (Exception ex)
						{
							flag8 = false;
							Logger.WriteLine("Hz switch failed: " + ex.Message);
						}
						// ② 回读（Win32）—— `ChangeDisplaySettingsEx` 之后**立刻**回读常常仍读到旧值
						//（模式切换是异步落地的，真机现象「我点了 60 切过去还是显示 180」）⇒ **短重试**：
						// 最多 6 次 / 每次 150ms，直到与目标一致（±2Hz 视为同一档，规避 59.94 取整），
						// 最多约 750ms；`await` 不阻塞 UI 线程。
						// ⚠️ 不用 GetAvailableRefreshRates 回读：那个列表来自 60s 缓存，切换后仍是旧快照。
						int num8 = -1;
						if (flag8)
						{
							for (int attempt = 0; attempt < 6; attempt++)
							{
								if (attempt > 0)
								{
									await Task.Delay(150);
								}
								num8 = MechrevoLite.Display.ScreenNative.GetRefreshRate(MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false));
								if (num8 > 0 && Math.Abs(num8 - value) <= 2)
								{
									break;
								}
							}
						}
						// ⭐ 2026-10-15（加固②，用户实测触发：NVCP 手动设的 165Hz 不在档位表里 ⇒ 回读 165 与任何档位都对不上，
						// 若按回读值置高亮会"两颗都不亮"）：**对不上目标时高亮"用户点的那一档"**，而不是按一个对不上的回读值。
						int num9 = ((num8 > 0 && Math.Abs(num8 - value) <= 2) ? num8 : value);
						if (flag8)
						{
							foreach (RButton hzButton4 in _hzButtons)
							{
								hzButton4.Activated = Math.Abs(int.Parse(hzButton4.Text) - num9) <= 2;
							}
						}
						// ③ **单向镜像给 GCU**（用户 2026-10-15 拍板：「那防止GCU覆盖那留着GCU吧」）：
						//    GCU 可用时顺手把同一档位也发给固件，免得固件按它自己的记忆把刷新率顶回去。
						//    ⚠️ **纯 best-effort：不 await、不参与成功判定** —— GCU 不在 / 下发失败都完全不影响
						//    上面的 Win32 结果（这正是"不走 GCU 也能用"的保证）。
						MechrevoHw hwForMirror = Program.hw;
						bool mirrored = hwForMirror != null && hwForMirror.IsGcuConnected && Program.service != null;
						if (mirrored)
						{
							_ = Program.service.SwitchRefreshRate(value);
						}
						// 若重试后仍对不上（例如驱动拒绝该模式），周期性的 SyncHzButtons 会继续按真值收敛。
						Logger.WriteLineIfChanged("hz-switch", $"hz switch: value={value} applied={flag8} readback={num8} used={num9} mirroredGcu={mirrored}");
					}
				};
				button.Activated = item == num;
				_hzButtons.Add(button);
				_hzSegTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (float)Math.Max(1, readOnlyList.Count)));
				_hzSegTable.Controls.Add(button, _hzButtons.Count - 1, 0);
			}
			// 2026-10-09（UI 打磨③，用户原话「这种长条的能不能拆成和Ghelper一样独立的呢」）：
			// 刷新率行（180/60…）也拆成一颗颗独立按钮（相邻 D(6) 缝隙），选中态改 ②b 的「彩边 + 深底」。
			// 强调色＝通用强调色 Accent（刷新率不是"性能模式"，没有模式专属色，不该借用某个模式色）。
			// 2026-10-09（UI 打磨④）：不再下发内缩值 —— 选中态一律深底铺满整颗 + 那圈 2px 彩边就是按钮
			// 自己的边框（同层不再画 UiVisualStyle.Border 那圈），故本行与性能/显卡行画法完全一致。
			UiVisualStyle.ApplyIndependentRow(_hzButtons.ToArray());
			UiVisualStyle.UseOutlineSelection(_hzButtons.ToArray());
			_hzSegTable.ResumeLayout(performLayout: true);
			SetScreenRowControls(1, _hzButtons.Count > 0, _hzSegTable);
		}
		else
		{
			// 2026-10-15（刷新率整条走 Windows API）：高亮回读**同样 Win32 优先**，读不到才退回 GCU 值。
			//（原实现先读 GCU 的 `CurrentHz`、只为 0 时才降级 ⇒ 非 GCU 机型上等价于"永远走兜底"。）
			int num10 = MechrevoLite.Display.ScreenNative.GetRefreshRate(MechrevoLite.Display.ScreenNative.FindLaptopScreen(log: false));
			if (num10 <= 0)
			{
				num10 = Program.hw?.CurrentHz ?? 0;
			}
			if (num10 <= 0)
			{
				return;
			}
			// ⛔ 2026-10-15（用户拍板：**去掉"唤醒被改"那条提示**）：
			// 「唤醒被改的提示就不要了 我怕把正常的切换行为（本来离电插电切换之类）也弹提示」。
			// 之前那版在这里做"外部改档探测"（档变了且距我们上次下发 >4s ⇒ 弹提示）；已删除。
			// 真凶（GCU 固件的 GPU_DC_HZ 在息屏/显示恢复时顶档）改为**从根上治**：见
			// ① `_gcuAutoOffPushed`（GCU 可用后补送 `SwitchAutoRefreshRate(false)`）
			// ② 自动开关 handler（**恒发 false**，永不把"自动开"告诉固件）。
			// ⭐ 2026-10-15（加固③，VRR/G-Sync）：**没有任何档位能匹配当前值**时**保留上一次高亮**，
			// 而不是把每颗都置 false（那会"两颗都不亮"）。当前值可能落在档位之间
			//（例如驱动另设的 165Hz、或 G-Sync 下瞬时值抖动）。
			if (!_hzButtons.Any((RButton hzButtonAny) => Math.Abs(int.Parse(hzButtonAny.Text) - num10) <= 2))
			{
				return;
			}
			foreach (RButton hzButton5 in _hzButtons)
			{
				hzButton5.Activated = Math.Abs(int.Parse(hzButton5.Text) - num10) <= 2;
			}
		}
	}

	private void BuildLightRowsPanel()
	{
		BufferedTableLayoutPanel body = new BufferedTableLayoutPanel
		{
			Name = "lightRows",
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = new Padding(D(10), 0, D(4), D(4)),
			BackColor = UiVisualStyle.Window
		};
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel = MakeRow("rowKeyboard");
		FillRow(tableLayoutPanel, Strings.Keyboard, out var sw, out var effectCombo, () =>
		{
			OpenRgbForm();
		});
		_kbPowerSw = sw;
		_kbEffectCombo = effectCombo;
		FillKeyboardEffectCombo(effectCombo);
		// ⭐⭐ 2026-10-19（运行时自校准）：机主**打开键盘灯效果下拉**（= 打开键盘灯设置）时排队一次亮度上限探测 ✅
		//   ⚠️ 后台线程 + 结果缓存；结果供 RgbForm 的亮度滑块范围使用（彩虹已改软件刷色，与校准无关）✅
		effectCombo.DropDown += delegate
		{
			EnsureBaolongdaKeyboardCalibrationQueued();
		};
		effectCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingEffectCombos && !Program.UiAuditMode)
			{
				int selectedIndex = effectCombo.SelectedIndex;
				if (selectedIndex >= 0 && Program.rgb != null)
				{
					// ⭐⭐ 2026-10-16（宝龙达 · 效果表收敛）：⭐ 宝龙达下这里与 FillKeyboardEffectCombo
					//   **必须用同一张表** ✅ ⇒ ⭐ 否则下拉显示两项、索引却映射到呼吸/波浪
					//   ⇒ ⭐ 选「单色」实际发的是别的效果 ❌（⭐ 非宝龙达判据不变 ⇒ 逐位不变 ✅）
					if (ShouldUseGcuKeyboardFallback(Program.rgb) || Program.hw?.UsesBaolongda == true)
					{
						(string, string)[] array2 = (Program.hw?.UsesBaolongda == true)
							? BaolongdaLightEffects
							: KeyboardFirmwareEffects.Visible(CurrentKeyboardType());
						if (selectedIndex < array2.Length)
						{
							string id = array2[selectedIndex].Item1;
							// ⭐⭐ 2026-10-19（机主：「彩虹=软件刷色，颜色无意义、亮度必须保留」）：
							//   ⭐ 本控件是**旧 Aura 遗留**（启动时 InitAura 已隐藏），这里保留原有"禁用"防御；
							//      用户真正能看到的颜色区在 `RgbForm`（编辑窗）——那里在彩虹模式下**整体隐藏**，
							//      亮度滑块/数值/标签**始终保留可用**（见 RgbForm.RefreshKeyboardLightEffectUi）✅
							//   ⚠️ 仅宝龙达（GCU 的彩虹档沿用原逻辑）✅
							if (Program.hw?.UsesBaolongda == true && buttonKeyboardColor != null && !buttonKeyboardColor.IsDisposed)
							{
								bool rainbow = string.Equals(id, "Rainbow", StringComparison.OrdinalIgnoreCase);
								if (buttonKeyboardColor.Enabled == rainbow)
								{
									buttonKeyboardColor.Enabled = !rainbow;
									Logger.WriteLine($"[light] 宝龙达键盘灯效={id} ⇒ 颜色按钮 {(rainbow ? "禁用（彩虹模式下颜色无意义，亮度不受影响）" : "恢复可用")}");
								}
							}
							LightChannelSettings settings = LightingSettingsStore.Load("Keyboard/Ctrl", array2[0].Item1);
							if (!sw.Checked)
							{
								LightingSettingsStore.Save("Keyboard/Ctrl", settings with
								{
									Effect = id
								});
							}
							else if (Program.service != null)
							{
								MechrevoHw hw = Program.hw;
								if (hw != null && hw.IsConnected)
								{
									// ⭐ 2026-10-17（真机「颜色改不了 / 下发恒白」）：
									//   宝龙达 WMI 只有功能号 17 的 3 字节 RGB ⇒ 预设色板也必须换算成真实 ARGB
									//   （原来只传 ColorArgb ⇒ 选了预设色仍是默认白 ❌）；非宝龙达逐位不变 ✅
									Color? singleColor = ((Program.hw?.UsesBaolongda == true)
										? new Color?(Color.FromArgb(settings.EffectiveColorArgb()))
										: ((id == "Single") ? new Color?(Color.FromArgb(settings.ColorArgb)) : ((Color?)null)));
									sw.Enabled = false;
									bool flag = await Program.service.SetKeyboardEffect(id, settings.Light, settings.Speed, "None", singleColor, save: true);
									sw.Enabled = true;
									if (flag)
									{
										LightingSettingsStore.Save("Keyboard/Ctrl", settings with
										{
											Effect = id
										});
									}
								}
							}
						}
					}
					else
					{
						(int, string)[] array3 = RgbForm.VisibleHidEffects(CurrentKeyboardType());
						if (selectedIndex < array3.Length)
						{
							(int Mode, string Name) hid = array3[selectedIndex];
							if (!sw.Checked)
							{
								Program.rgb.KbHidMode = hid.Mode;
								Program.rgb.QueueSaveConfig();
							}
							else
							{
								Program.rgb.KbPowerOn = true;
								Program.rgb.KbHidMode = hid.Mode;
								Program.rgb.QueueSaveConfig();
								if (Program.rgb.IsConnected && !ShouldUseGcuKeyboardFallback(Program.rgb))
								{
									Task.Run(() =>
									{
										Program.rgb.StartMode(hid.Mode);
									});
								}
								else if (Program.service != null)
								{
									MechrevoHw hw = Program.hw;
									if (hw != null && hw.IsConnected)
									{
										sw.Enabled = false;
										bool flag2 = await Program.service.SetKeyboardPower(on: true);
										sw.Enabled = true;
										if (flag2)
										{
											Task.Run(() => Program.service.SetKeyboardBrightnessPreservingEffect(KeyboardRgb.MapSoftwareBrightnessToHardwareLevel(Program.rgb.Brightness)));
										}
									}
								}
							}
						}
					}
					// ⭐⭐ 2026-10-19（机主：彩虹=软件刷色 ⇒ 颜色无意义、亮度保留）：
					//   ⭐ 效果下拉切换后**立刻**刷新键盘灯编辑窗（彩虹⇒折叠颜色区，单色⇒恢复；亮度始终保留）✅
					//   ⚠️ 只刷新 UI，⛔ 不写硬件；窗口没开/已释放时是空操作；⚠️ 仅宝龙达 ⇒ 其它机型逐位不变 ✅
					if (Program.hw?.UsesBaolongda == true)
					{
						rgbForm?.RefreshKeyboardLightEffectUi();
					}
				}
			}
		};
		sw.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingSwitches)
			{
				_lastQuickSwitchUi = DateTime.Now;
				bool requested = sw.Checked;
				int gen = ++_kbCmdGen;
				bool gcuFallback = ShouldUseGcuKeyboardFallback(Program.rgb);
				Task hidStop = null;
				if (Program.rgb != null)
				{
					Program.rgb.KbPowerOn = requested;
					if (!requested)
					{
						KeyboardRgb rgb = Program.rgb;
						int stopGen = gen;
						hidStop = Task.Run(() =>
						{
							StopHidEffectForCurrentGeneration(stopGen, () => _kbCmdGen, rgb.StopCurrentEffect);
						});
					}
					Program.rgb.QueueSaveConfig();
				}
				if (requested && !gcuFallback && !Program.UiAuditMode)
				{
					KeyboardRgb rgbToConnect = Program.rgb;
					if (rgbToConnect != null && !rgbToConnect.IsConnected)
					{
						if (rgbToConnect.ControllerAvailability == FeatureAvailability.Unknown)
						{
							await rgbToConnect.EnsureHidReadyAsync();
						}
						if (rgbToConnect.ControllerAvailability != FeatureAvailability.Unknown)
						{
							RefreshDeviceCapabilities();
						}
						if (!rgbToConnect.IsConnected && rgbToConnect.ControllerAvailability != FeatureAvailability.Unsupported)
						{
							await Task.Run(() => rgbToConnect.Connect());
						}
					}
				}
				if (requested && !gcuFallback)
				{
					KeyboardRgb rgb2 = Program.rgb;
					if (rgb2 != null && rgb2.IsConnected)
					{
						KeyboardRgb rgb3 = Program.rgb;
						MechrevoService service = Program.service;
						MechrevoHw hw = Program.hw;
						Task bypass = ((service != null && hw != null && hw.IsConnected && !hw.KeyboardPower) ? service.SetKeyboardPower(on: true) : null);
						_kbPowerBypassInFlight = bypass;
						Task.Run(async () =>
						{
							await RunHidWhileFirmwarePowerBypassCompletesAsync(bypass, () =>
							{
								if (gen == _kbCmdGen)
								{
									rgb3.ReInitCustomMode();
									rgb3.StartMode(rgb3.KbHidMode);
								}
							});
							if (_kbPowerBypassInFlight == bypass)
							{
								_kbPowerBypassInFlight = null;
							}
						});
						// 2026-10-10：本条分支提前返回（HID 直通）—— 状态已经落定，同样把那一次重绘补上
						// （见 RepaintKeyboardLightRow 的说明）。
						RepaintKeyboardLightRow();
						return;
					}
				}
				if (Program.service != null)
				{
					MechrevoHw hw2 = Program.hw;
					if (hw2 != null && hw2.IsConnected)
					{
						MechrevoService service2 = Program.service;
						sw.Enabled = false;
						bool flag;
						if (!requested)
						{
							Task kbPowerBypassInFlight = _kbPowerBypassInFlight;
							if (kbPowerBypassInFlight != null)
							{
								await Task.WhenAny(kbPowerBypassInFlight, Task.Delay(250));
							}
							flag = await service2.SetKeyboardPower(on: false);
						}
						else
						{
							flag = await service2.SetKeyboardPower(on: true);
							if (flag)
							{
								Task.Run(() =>
								{
									MechrevoService mechrevoService = service2;
									KeyboardRgb rgb5 = Program.rgb;
									return mechrevoService.SetKeyboardBrightnessPreservingEffect(KeyboardRgb.MapSoftwareBrightnessToHardwareLevel((rgb5 != null) ? rgb5.Brightness : 100));
								});
							}
						}
						sw.Enabled = true;
						if (!flag)
						{
							_syncingSwitches = true;
							sw.Checked = !requested;
							_syncingSwitches = false;
							if (Program.rgb != null)
							{
								Program.rgb.KbPowerOn = !requested;
								if (!requested)
								{
									KeyboardRgb rgb4 = Program.rgb;
									if (rgb4.IsConnected && !gcuFallback)
									{
										Task.Run(async () =>
										{
											hidStop?.Wait(600);
											if (Program.service != null)
											{
												MechrevoHw hw3 = Program.hw;
												if (hw3 != null && hw3.IsConnected && !Program.hw.KeyboardPower)
												{
													await Program.service.SetKeyboardPower(on: true);
												}
											}
											rgb4.ReInitCustomMode();
											rgb4.StartMode(rgb4.KbHidMode);
										});
									}
								}
							}
						}
					}
				}
			}
			// 2026-10-10：本条分支（走服务 / GCU 下发）的**所有**动作到此为止 —— 状态已落定，
			// 在这里补上那一次重绘（见 RepaintKeyboardLightRow 的说明）。
			RepaintKeyboardLightRow();
		};
		body.Controls.Add(tableLayoutPanel, 0, 0);
		BuildLightChannelRow("rowLightbar", Strings.Lightbar, "HidLightbar/Ctrl", Strings.LightbarEffect, LightForm.LightbarEffects, 1, (RCheckBox lbPowerSw, ComboBox lbEffectCombo) =>
		{
			_lbPowerSw = lbPowerSw;
			_lbEffectCombo = lbEffectCombo;
		});
		BuildLightChannelRow("rowLogo", Strings.Logo, "HidLightbar_Logo/Ctrl", Strings.LogoEffect, LightForm.LogoEffects, 2, (RCheckBox logoPowerSw, ComboBox logoEffectCombo) =>
		{
			_logoPowerSw = logoPowerSw;
			_logoEffectCombo = logoEffectCombo;
			// ⭐ 2026-10-19：显隐/收列结论改在**刷新期** `ApplyBaolongdaLightRowLayout` 做
			//   （建行时 hw 往往还没就绪 ⇒ 原来这里判断恒 false，正是前两次修复失效的原因）✅
		});		BuildLightChannelRow("rowHinge", Strings.LightChannelHinge, "HidLightbar_Hinge/Ctrl", Strings.LightHingeTitle, LightForm.HingeEffects, 3, (RCheckBox hingePowerSw, ComboBox hingeEffectCombo) =>
		{
			_hingePowerSw = hingePowerSw;
			_hingeEffectCombo = hingeEffectCombo;
		});
		BuildLightChannelRow("rowSync", Strings.LightChannelSync, "HidLightbar_Sync/Ctrl", Strings.LightSyncTitle, LightForm.SyncEffects, 4, (RCheckBox syncPowerSw, ComboBox syncEffectCombo) =>
		{
			_syncPowerSw = syncPowerSw;
			_syncEffectCombo = syncEffectCombo;
		});
		_lightGroup?.SetContent(body);
		RCheckBox offOnBatteryChk = new RCheckBox
		{
			Name = "checkLightingOffOnBattery",
			Text = Strings.AutoOffOnBattery,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			AutoSize = true,
			Checked = AppConfig.Is("lighting_off_on_battery"),
			Margin = new Padding(D(2), 0, 0, 0),
			Anchor = AnchorStyles.Left
		};
		offOnBatteryChk.CheckedChanged += async (object? _, EventArgs _) =>
		{
			if (!_syncingLightHeader)
			{
				AppConfig.Set("lighting_off_on_battery", offOnBatteryChk.Checked ? 1 : 0);
				await Program.ReconcileLightingPowerAsync();
			}
		};
		_lightOffOnBatteryChk = offOnBatteryChk;
		// 2026-10-10（用户原话「灯光自动关闭时间下拉框改窄 并在左边加入"自动关闭时间"字样」）：
		// 在时间下拉**紧邻左侧**加一颗字段标签（原来那句「离电自动关闭全部灯效」是左边那颗开关自己的文字，
		// 保留不动、未替换、未移走 ⇒ 本行仍是"开关文字 + [自动关闭时间] + 下拉"三件套）。
		// 字体/颜色/背景一律沿用同区块既有标签那一套（UiStyleBody() + UiVisualStyle.Text/Window），
		// ⛔ 未新造字体/字号/颜色；AutoSize ⇒ 该标签在任何 DPI / 窗口宽度下都不会被父格裁切（也不会顶到下拉）。
		Label autoOffTimeLabel = new Label
		{
			Name = "labelLightingAutoOffTime",
			Text = Strings.LightingAutoOffTime,
			AutoSize = true,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleBody(),
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(D(6), 0, 0, 0),
			Anchor = AnchorStyles.Left
		};
		RComboBox sleepCombo = new RComboBox
		{
			Name = "comboLightingSleepTimer",
			DropDownStyle = ComboBoxStyle.DropDownList,
			DropDownWidth = D(150),
			// 2026-10-11（用户原话「1通道 0开启删掉 把自动关闭时间放这个位置 然后下拉列表可以再缩窄一点点」）：
			// 本颗下拉**离开**了原来那一行、搬进**卡片标题行右端** —— 位置改由下面那颗 `headerRight` 面板
			// 在 `Layout` 里按"右对齐"摆（见该面板的注释），所以这里**不再**用 Anchor 表达"贴右沿"。
			// ⚠️ 右侧 D(2) 内边距保留原样：箭头格（26 逻辑像素）右边那一列（含右下圆角与 1px 描边）
			// 原本就是靠它才有余量，与本轮"只挪位置 + 缩窄"无关，不动。
			Margin = new Padding(0, D(3), D(2), D(3)),
			Anchor = AnchorStyles.Right
		};
		// 2026-10-09（0.291 beta22 补丁⑥；用户原话「顺便把这个10分钟的高度改小一点」）：
		// 这一颗在 `D(32)`（175% → 56 设备像素）的行里被 `TableLayoutPanel` **纵向拉伸**到 48 设备像素，
		// 而同族其它下拉都是 40（175% 实测：`[diag] comboLightingSleepTimer … 217x48` vs 屏幕校色/色彩增强
		// `275x40`）。修法：**只钉这一颗控件自己的高度上限与高度**（= 族标准 `ComboHeightLogical` 逻辑像素
		// ⇒ 175% 下 40 设备像素）；**行高 D(32)、列宽 D(132)、Margin、Anchor 一律不动**，
		// `TableLayoutPanel` 会自然让它在单元格里居中。
		int __familyComboH = (int)Math.Round(UiVisualStyle.ComboHeightLogical * (UiDpi.Paint(this) / 96f));
		sleepCombo.MaximumSize = new Size(0, __familyComboH);
		sleepCombo.Height = __familyComboH;
		// 2026-10-09（0.291 beta22 补丁⑥，根因修复）：**字体与同族对齐**（9pt = `UiStyleBody()`，
		// 屏幕校色 / 色彩增强 / 刷新率等下拉都是这一档）。改前这颗**没有显式字体**、继承了更大的父面板字体
		// ⇒ `ComboBox.PreferredHeight`（由字体度量决定、且 `ComboBox` 会用它强制自己的高度）比同族大 ⇒ 高 48
		// 而兄弟是 40。实测：只钉控件 Height/MaximumSize、以及只改行高 D(32)→D(29)，**都压不住**（仍是 217x48）；
		// 把字体对齐后高度才会落到族标准。
		sleepCombo.Font = UiStyleBody();
		// ⭐⭐ 2026-10-19（机主截 bug：不允许两个"自动关闭时间"入口）：
		//   ⭐ **复用头部这颗已有下拉**（⛔ 不再新增独立行）；平台分支在 SelectedIndexChanged 里按 hw 判定：
		//     · 宝龙达 ⇒ 软件实现（light_autooff_seconds：关/10秒/5分钟/10分钟/30分钟）✅
		//     · 其它机型 ⇒ 原厂商逻辑（lighting_idle_seconds）**逐位不变** ✅
		//   ⚠️ 构建时 hw 可能还没就绪 ⇒ 先按厂商选项填充；平台确定后 FillLightingAutoOffCombo() 切换 ✅
		sleepCombo.SelectedIndexChanged += async (object? _, EventArgs _) =>
		{
			if (_syncingLightHeader)
			{
				return;
			}
			int selectedIndex = sleepCombo.SelectedIndex;
			if (selectedIndex < 0)
			{
				return;
			}
			// ── ⭐ 宝龙达：用我们的软件实现（配置统一按秒）──
			if (Program.hw?.UsesBaolongda == true)
			{
				if (selectedIndex >= KeyboardAutoOffOptions.Length)
				{
					return;
				}
				MechrevoHw.SetKeyboardAutoOffSeconds(KeyboardAutoOffOptions[selectedIndex].Seconds);
				Program.hw.ApplyKeyboardAutoOffSetting();
				return;
			}
			// ── ⭐ 其它机型：厂商逻辑逐位不变 ──
			if (selectedIndex < LightingCloseTimerOptions.Length)
			{
				(string, int, int) tuple = LightingCloseTimerOptions[selectedIndex];
				AppConfig.Set("lighting_idle_seconds", tuple.Item3);
				if (Program.rgb != null)
				{
					Program.rgb.CloseTimerMinutes = tuple.Item2;
					Program.rgb.SaveConfig();
				}
				await Program.ReconcileLightingPowerAsync();
			}
		};
		_lightSleepCombo = sleepCombo;
		FillLightingAutoOffCombo();
		// 2026-10-09（0.291 beta22 补丁⑥；用户原话「顺便把这个10分钟的高度改小一点」）：
		// 这一行原来 `D(32)`（175% → 56 设备像素），而 `TableLayoutPanel` 会把 `Anchor=Left|Right` 的子控件
		// **纵向拉伸填满单元格** ⇒ 里面的时间下拉被拉到 48 设备像素，比同族的 40 高一档
		//（实测 `[diag] comboLightingSleepTimer … 217x48` vs 屏幕校色/色彩增强 `275x40`）。
		// 试过"只钉控件自己的 Height/MaximumSize"**压不住**（`ComboBox.SetBoundsCore`/布局都会把它拉回去），
		// 所以这里改的是**行高本身**：`D(32)` → `D(29)`（56 → 50 设备像素）⇒ 减去下拉上下 Margin
		// 2×D(3)＝10 设备像素后正好落在族标准 **40**。左列那颗「离电时关闭」开关是 AutoSize + 左对齐，仍有余量。
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 1,
			Height = ResponsiveLayout.LogicalToDevice(this, 29),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Window,
			Name = "rowLightPrefs"
		};
		// 2026-10-11（用户原话「1通道 0开启删掉 把自动关闭时间放这个位置 然后下拉列表可以再缩窄一点点」）：
		// ⭐ 本行只剩**一件**内容 —— 左列那颗「离电自动关闭全部灯效」开关（`Percent 100` 左对齐，AutoSize）。
		// 原第 2/3 列（「自动关闭时间」标签 + 时间下拉）**整对搬到卡片标题行右端**（见下面 `headerRight`），
		// 所以这里把 ColumnCount 3 → 2 并删掉那两条列样式 —— ⛔ 不是"留空列"，而是**行宽不再为它们预留**
		// （留空列会让开关右边空出一大块，正是用户不要的"空洞"）。
		// ⚠️ 行高仍是 D(29)、开关自身的 Margin/Anchor/文字/事件一字未动（本行现在只有它，垂直留白由它自己居中）。
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(offOnBatteryChk, 0, 0);
		body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		body.RowCount = body.RowStyles.Count;
		Control[] array = body.Controls.OfType<Control>().ToArray();
		foreach (Control control in array)
		{
			body.SetCellPosition(control, new TableLayoutPanelCellPosition(0, body.GetCellPosition(control).Row + 1));
		}
		body.Controls.Add(tableLayoutPanel2, 0, 0);
		// 2026-10-11（用户原话「1通道 0开启删掉 把自动关闭时间放这个位置 然后下拉列表可以再缩窄一点点」
		// ＋「然后下拉列表可以再缩窄一点点我指的是 自动关闭时间的下拉框」）：
		// ⭐ 把「自动关闭时间」标签 + 时间下拉**整对**放进**卡片标题行右端**（＝原「1 通道 · 0 开启」摘要所在的那一格）。
		//   · 摘要那一格由 `RCollapseGroup.SetHeaderRight` 让出来（摘要 Label 被移除、Summary setter 变空操作），
		//     本面板 `Dock=Fill` 占满该格；标题行高、列样式、点击折叠语义一律未动。
		//   · 面板自己按"右对齐"排两件内容（Layout 处理器）；下拉**只缩这一颗**（见 FitAutoOffComboWidth），
		//     ⛔ 不碰 UiVisualStyle / RComboBox 的族规则，也⛔ 不影响任何其它下拉。
		//   · ⚠️ 这一格是标题行的一部分，而标题行整体带"点击折叠"处理器 ⇒ 面板自己也要吃掉落在**内容上**的
		//     点击（不能把 `Cursor` 判据交给 `Dock=Fill` 的 Summary 占位标签那种老做法 —— 点击落进标签会冒泡到
		//     标题行，直接开关卡片）。见下面 headerRight.Click。
		Panel headerRight = new Panel
		{
			Name = "lightHeaderRight",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		headerRight.Controls.Add(autoOffTimeLabel);
		headerRight.Controls.Add(sleepCombo);
		// 标签与下拉之间的水平间距（逻辑像素）；右端到标题行右沿的间距（逻辑像素）。
		int headerGap = D(6);
		int headerRightPad = D(2);
		autoOffTimeLabel.AutoSize = true;
		// ⚠️ 上限是**这一颗原先所在那一格的宽**（`D(104)`，见上面 tableLayoutPanel2 的历史注释：
		//    175% 下 182 设备像素、可见宽 96 逻辑像素），⛔ **不是** `DropDownWidth`（那是展开列表的宽）。
		//    用 D(104) 作上限 ⇒ 本次改动**只会缩、绝不会比改前宽**，与用户"I don't want it wider"一致。
		sleepCombo.Width = FitAutoOffComboWidth(sleepCombo, D(104));
		headerRight.Layout += (object? _, LayoutEventArgs _) =>
		{
			int labelWidth = autoOffTimeLabel.PreferredWidth;
			int comboWidth = Math.Max(1, sleepCombo.Width);
			int comboLeft = Math.Max(0, headerRight.ClientSize.Width - headerRightPad - comboWidth);
			int labelLeft = Math.Max(0, comboLeft - headerGap - labelWidth);
			autoOffTimeLabel.Location = new Point(labelLeft, Math.Max(0, (headerRight.ClientSize.Height - autoOffTimeLabel.PreferredHeight) / 2));
			sleepCombo.Location = new Point(comboLeft, Math.Max(0, (headerRight.ClientSize.Height - sleepCombo.Height) / 2));
		};
		headerRight.Click += (object? _, EventArgs _) =>
		{
			// 落在"自动关闭时间"这两件内容上的点击**不当成折叠标题的点击**（与标题行其它文字的语义一致：
			// 只有空白处才折叠）。坐标用本面板自己的坐标系判 —— ⛔ 不依赖任何全局状态。
			Point cursor = headerRight.PointToClient(Cursor.Position);
			if (autoOffTimeLabel.Bounds.Contains(cursor) || sleepCombo.Bounds.Contains(cursor))
			{
				return;
			}
			_lightGroup?.Toggle();
		};
		_lightGroup?.SetHeaderRight(headerRight);
		_lblKeyboardControllerStatus = new Label
		{
			Name = "labelKeyboardControllerStatus",
			Text = Strings.KeyboardControllerUnsupported,
			AutoSize = false,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Visible = false,
			ForeColor = UiVisualStyle.Muted,
			BackColor = UiVisualStyle.Window,
			Font = UiStyleCaptionFont(),
			Margin = new Padding(D(2), 0, D(4), D(2))
		};
		_lblKeyboardControllerStatus.Resize += (object? _, EventArgs _) =>
		{
			ReflowKeyboardStatusHeight();
		};
		_lblKeyboardControllerStatus.FontChanged += (object? _, EventArgs _) =>
		{
			ReflowKeyboardStatusHeight();
		};
		Control control2 = body.Controls.OfType<Control>().First((Control c) => c.Name == "rowKeyboard");
		int row = body.GetCellPosition(control2).Row;
		body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		body.RowCount = body.RowStyles.Count;
		array = body.Controls.OfType<Control>().ToArray();
		foreach (Control control3 in array)
		{
			int row2 = body.GetCellPosition(control3).Row;
			if (row2 > row)
			{
				body.SetCellPosition(control3, new TableLayoutPanelCellPosition(0, row2 + 1));
			}
		}
		body.Controls.Add(_lblKeyboardControllerStatus, 0, row + 1);
		void BuildLightChannelRow(string rowName, string label, string topic, string title, (string Effect, string Name)[] effects, int gridRow, Action<RCheckBox, ComboBox> storeControls)
		{
			// ⭐⭐ 2026-10-16（宝龙达 · 效果表收敛）：⭐ 宝龙达固件只支持 关／RGB 固定色 ✅
			//   ⇒ ⭐ 这里把"声明的效果表"收敛一次，⭐ **下面下拉、⭐ 索引→Id 映射、⭐ 以及点进
			//      LightForm 那扇窗的列表**都用这张收敛后的表 ✅ —— ⭐ 三处口径必然一致 ✅。
			//   ⭐ 非宝龙达 ⇒ **原样返回** ⇒ ⭐ 逐位不变 ✅。
			effects = EffectsForCurrentBackend(effects);
			// ⭐⭐ 2026-10-17（真机反馈「氛围灯有颜色设置但无效」）：⭐ 宝龙达的 LOGO 灯**只有【开／关】** ⚠️
			//   ⭐ 依据：协议里它只有一个字节功能号 15（AmbientLight，0/1）—— ⛔ 没有颜色、⛔ 没有效果、
			//      ⛔ 没有亮度/帧率/角度/速度（BaolongdaWmi.TrySetLogoLight(on) / TryReadLogoLight(out on) 可证 ✅）。
			//   ⚠️ 而 EffectsForCurrentBackend 对**所有**灯光行都返回同一张 3 项表
			//      ⇒ ⭐ LOGO 行会露出这 3 项 ⇒ ⭐ 点了不会有效果（违反能力位铁律）❌
			//   ⇒ ⭐ 宝龙达下 **LOGO 行不显示效果下拉**（只保留开/关开关）✅；非宝龙达 ⭐ 逐位不变 ✅。
			// ⭐⭐ 2026-10-19（机主：Logo 开关跑到最右 —— 第 3 次修复）：
			//   ⚠️ 前两次失效的根因：宝龙达非键盘灯行（灯带/Logo/铰链/同步）的「隐藏效果列 + 隐藏编辑按钮 +
			//      开关对齐」原来在**建行那一刻**按 `Program.hw?.UsesBaolongda` 做一次；而启动懒加载 +
			//      后端探测异步 ⇒ 建行时它往往还是 false ⇒ 建行分支不成立（也没记录行）❌
			//   ⭐ 现在统一挪到**每次灯光同步**的 `ApplyBaolongdaLightRowLayout`（幂等；非宝龙达门控直接 return）✅
			//   行本身**保留**（LightExtraRowAvailable() 恒 true 是刻意的"判据判错时的后路"，
			//   用户还能改 light_strip_ui / light_logo_ui —— 这条设计不动 ✅）
			TableLayoutPanel tableLayoutPanel3 = MakeRow(rowName);
			FillRow(tableLayoutPanel3, label, out var sw2, out var effectCombo2, () =>
			{
				OpenLightForm(topic, title, effects);
			});
			storeControls(sw2, effectCombo2);
			(string, string)[] array2 = effects;
			for (int num3 = 0; num3 < array2.Length; num3++)
			{
				string item2 = array2[num3].Item2;
				effectCombo2.Items.Add(new KeyValuePair<string, string>(item2, item2));
			}
			string saved = LightingSettingsStore.Load(topic, effects[0].Effect).Effect;
			int num4 = Array.FindIndex(effects, ((string Effect, string Name) e) => e.Effect == saved);
			effectCombo2.SelectedIndex = ((num4 >= 0) ? num4 : 0);
			effectCombo2.SelectedIndexChanged += async (object? _, EventArgs _) =>
			{
				if (!_syncingEffectCombos && !Program.UiAuditMode)
				{
					int selectedIndex = effectCombo2.SelectedIndex;
					if (selectedIndex >= 0 && selectedIndex < effects.Length)
					{
						string effectId = effects[selectedIndex].Effect;
						LightChannelSettings settings = LightingSettingsStore.Load(topic, effects[0].Effect);
						if (!sw2.Checked)
						{
							LightingSettingsStore.Save(topic, settings with
							{
								Effect = effectId
							});
						}
						else if (Program.service != null)
						{
							MechrevoHw hw = Program.hw;
							if (hw != null && hw.IsConnected)
							{
								sw2.Enabled = false;
								// 2026-10-16（0.291.8.1 · 修「logo 灯/灯带效果错乱」）：⭐ 单色系灯效都带已保存的颜色
								//（原来只有 Single 带 ⇒ 呼吸发硬编码七彩 ⇒ 硬件取首段红、混合直接关灯 ❌）。
								bool flag = await Program.service.SetLightEffect(topic, effectId, settings.Light, settings.Speed, "None", KeyboardFirmwareEffects.IsSingleColorEffect(effectId) ? new Color?(Color.FromArgb(settings.ColorArgb)) : ((Color?)null));
								sw2.Enabled = true;
								if (flag)
								{
									LightingSettingsStore.Save(topic, settings with
									{
										Effect = effectId
									});
								}
							}
						}
					}
				}
			};
			sw2.CheckedChanged += async (object? _, EventArgs _) =>
			{
				if (!_syncingSwitches)
				{
					_lastQuickSwitchUi = DateTime.Now;
					if (Program.service != null)
					{
						MechrevoHw hw = Program.hw;
						if (hw != null && hw.IsConnected)
						{
							bool requested = sw2.Checked;
							sw2.Enabled = false;
							bool flag = await Program.service.SetLightPower(topic, requested);
							sw2.Enabled = true;
							if (flag)
							{
								// ⭐⭐⭐ 2026-10-18（机主：每次启动仍默认打开 Logo 灯）：
								//   ⭐ **只有这里（用户亲手点灯光开关）才允许置 powerexplicit=1** ✅
								//   ⛔ 任何自动路径（启动恢复/亮度/效果/开关幂等回写）都不许调 SavePower
								LightingSettingsStore.SavePower(topic, requested, "用户点击「灯光」开关");
								// ⭐⭐ 2026-10-19（机主实测「Logo 灯开→显示关」）：⭐ 这里原来会再调一次
								//   `ApplyLightChannelEffectAsync` ⇒ 它内部**又**按旧快照的 `PowerOn` 发一次
								//   `PublishLightPower(..., false)`（诊断包日志：on=True 与 on=False 只隔 0.55 s）❌
								//   ⭐ LOGO 通道只有「开 / 关」（功能号 15），**没有效果字段可应用**
								//   （日志原文：「界面给的效果 Single／亮度 4 没有对应字段 ⇒ 已忽略」）
								//   ⇒ ⭐ 非键盘通道**不再走效果应用**，杜绝第二次下发 ✅
								//   ⚠️ 键盘通道保持原样（它确实需要写效果/颜色）✅
								bool isKeyboardChannel = string.Equals(topic, "Keyboard/Ctrl", StringComparison.OrdinalIgnoreCase);
								if (requested && isKeyboardChannel)
								{
									LightChannelSettings settings = LightingSettingsStore.Load(topic, effects[0].Effect);
									Program.ApplyLightChannelEffectAsync(topic, settings, 120, () => LightingSettingsStore.Load(topic, effects[0].Effect).PowerOn);
								}
							}
							else
							{
								_syncingSwitches = true;
								sw2.Checked = !requested;
								_syncingSwitches = false;
							}
						}
					}
				}
			};
			body.Controls.Add(tableLayoutPanel3, 0, gridRow);
		}
		int D(int value)
		{
			return ResponsiveLayout.LogicalToDevice(this, value);
		}
		void FillRow(TableLayoutPanel tableLayoutPanel3, string name, out RCheckBox powerSwitch, out ComboBox reference, Action click)
		{
			Label control4 = new Label
			{
				Text = name,
				AutoSize = false,
				Dock = DockStyle.Fill,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window,
				Font = UiStyleBody(),
				TextAlign = ContentAlignment.MiddleLeft,
				Margin = Padding.Empty
			};
			powerSwitch = new RCheckBox
			{
				AutoSize = true,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window,
				Margin = Padding.Empty,
				Anchor = AnchorStyles.Left
			};
			reference = new MechrevoLite.UI.RComboBox
			{
				Cursor = Cursors.Hand,
				DropDownStyle = ComboBoxStyle.DropDownList,
				DisplayMember = "Key",
				FlatStyle = FlatStyle.Flat,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window,
				Font = UiStyleBody(),
				Anchor = (AnchorStyles.Left | AnchorStyles.Right),
				Margin = new Padding(D(8), D(3), D(8), D(3))
			};
			// 2026-10-04: use RButton + the "mini-chip" theme so this Edit button matches
			// the rounded chips used by the screen/lighting rows (plain Button has no BorderRadius).
			RButton button = new RButton
			{
				Tag = "mini-chip",
				BorderRadius = 12,
				Text = Strings.Edit,
				FlatStyle = FlatStyle.Flat,
				Cursor = Cursors.Hand,
				ForeColor = UiVisualStyle.Text,
				BackColor = UiVisualStyle.Window
			};
			button.AutoSize = false;
			button.Dock = DockStyle.Fill;
			// 2026-10-04: match the screen/lighting chips exactly -- Font(8f) like the others,
			// and vertical inset 2 (this row is 32 tall vs calibPanel 34) so the button
			// ends up 28px tall, same as the calibration "settings" chip.
			button.Font = UiVisualStyle.Font(8f);
			button.Margin = new Padding(D(2), D(2), 0, D(2));
			// 2026-10-04: FlatStyle.Flat defaults BorderSize to 1, and this row group is
			// rebuilt after the theme pass, so the "mini-chip" theme never reaches this
			// button. Replicate that theme explicitly so it matches the calibration /
			// display-effect chips exactly.
			button.BackColor = UiVisualStyle.Surface;
			button.BorderColor = UiVisualStyle.Border;
			button.FlatAppearance.BorderColor = UiVisualStyle.Border;
			button.FlatAppearance.BorderSize = 0;
			button.FlatAppearance.MouseOverBackColor = UiVisualStyle.SurfaceRaised;
			button.FlatAppearance.MouseDownBackColor = UiVisualStyle.SurfaceRaised;
			button.TextAlign = ContentAlignment.MiddleCenter;
			Button button2 = button;
			button2.Click += (object? _, EventArgs _) =>
			{
				click();
			};
			tableLayoutPanel3.Controls.Add(control4, 0, 0);
			tableLayoutPanel3.Controls.Add(powerSwitch, 1, 0);
			tableLayoutPanel3.Controls.Add(reference, 2, 0);
			tableLayoutPanel3.Controls.Add(button2, 3, 0);
		}
		TableLayoutPanel MakeRow(string rowName)
		{
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 4,
				RowCount = 1,
				Height = ResponsiveLayout.LogicalToDevice(this, 32),
				Margin = Padding.Empty,
				BackColor = UiVisualStyle.Window,
				Name = rowName
			};
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f));
			tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			body.Controls.Add(tableLayoutPanel3, 0, body.RowCount - 1);
			return tableLayoutPanel3;
		}
		void ReflowKeyboardStatusHeight()
		{
			Label lblKeyboardControllerStatus = _lblKeyboardControllerStatus;
			int width = Math.Max(1, lblKeyboardControllerStatus.ClientSize.Width - 8);
			int height = TextRenderer.MeasureText(lblKeyboardControllerStatus.Text, lblKeyboardControllerStatus.Font, new Size(width, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
			lblKeyboardControllerStatus.Height = height + 4;
		}
		// ⭐⭐ 2026-10-19（机主：Logo 开关跑到最右 —— 第 3 次修复）：
		//   布局修正的**补充触发点**（主触发 = 每次 `SyncLightRows`；这里保证窗口缩放 / DPI 变化 /
		//   「灯光」组展开后立即重排；⚠️ 非宝龙达 ⇒ 方法内 `UsesBaolongda` 门控直接 return，其它机型逐位不变 ✅）
		body.Layout += (object _, LayoutEventArgs _) => ApplyBaolongdaLightRowLayout(Program.hw);
		body.SizeChanged += (object _, EventArgs _) => ApplyBaolongdaLightRowLayout(Program.hw);
		ApplyBaolongdaLightRowLayout(Program.hw);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：宝龙达「Logo 灯」开关跑到最右 —— 第 3 次修复，先做代码级 UI 审查）：
	/// ⭐ **宝龙达非键盘灯行（灯带 / Logo / 铰链 / 同步）的「按平台收列 + 开关对齐」唯一入口** ✅
	/// <para>⭐ 每次 <see cref="SyncLightRows" /> / <see cref="RefreshDeviceCapabilities" /> / 窗口缩放 /
	/// 「灯光」组展开都会调用（**幂等**，可重复进）：
	/// ① 隐藏效果下拉（col2）与「编辑」按钮（col3）；② 开关钉在 col1；
	/// ③ 列宽 = 键盘行**当前实际像素列宽**（col0/col1），col2/col3 = 0 ⇒ 开关 x 与键盘行逐像素相同。</para>
	/// <para>⚠️ 为什么必须在刷新期做而不是建行期：启动懒加载 + 后端探测异步 ⇒ 建行时
	/// <c>Program.hw?.UsesBaolongda</c> 往往还是 false（建行分支不成立）；运行期老实现又把列宽写回
	/// 80%/20%/0/0 ⇒ 开关被推到最右（机主截图）。本方法每次同步都重新判定平台并重排，不依赖建行时刻 ✅</para>
	/// <para>⛔ 非宝龙达：入口 `UsesBaolongda` 门控直接 return ⇒ GCU/同方布局逐位不变 ✅</para>
	/// </summary>
	private void ApplyBaolongdaLightRowLayout(MechrevoHw hw)
	{
		if (hw == null || !hw.UsesBaolongda || IsDisposed || _applyingCollapsedLightRowLayout)
		{
			return;
		}
		TableLayoutPanel keyboardRow = _kbPowerSw?.Parent as TableLayoutPanel;
		if (keyboardRow == null)
		{
			Control[] foundKeyboard = Controls.Find("rowKeyboard", true);
			if (foundKeyboard.Length > 0 && foundKeyboard[0] is TableLayoutPanel found)
			{
				keyboardRow = found;
			}
		}
		// ⭐ 对齐基准 = 键盘行**当前实际像素列宽**（唯一来源；键盘行本身仍是 22/10/56/12 百分比）
		int col0Width = -1;
		int col1Width = -1;
		if (keyboardRow != null && keyboardRow.ClientSize.Width > 0)
		{
			keyboardRow.PerformLayout();
			int[] keyboardColumns = keyboardRow.GetColumnWidths();
			if (keyboardColumns.Length >= 2 && keyboardColumns[0] > 0)
			{
				col0Width = keyboardColumns[0];
				col1Width = keyboardColumns[1];
			}
		}
		if (col0Width <= 0 || keyboardRow == null)
		{
			// ⭐ 可诊断性：明确记录"刷新路径已跑到、但在等键盘行宽"（灯光组折叠/窗口未显示时会出现）
			Logger.WriteLineIfChanged("light-ui-audit-wait",
				"[light][UI审查] 宝龙达：灯光行布局修正等待键盘行布局（键盘行=" + ((keyboardRow == null) ? "未找到" : (keyboardRow.ClientSize.Width.ToString() + "px")) + "）⇒ 显示/展开灯光组后自动重试");
		}
		(string RowName, string Label, RCheckBox Switch, ComboBox Combo)[] rows = new (string, string, RCheckBox, ComboBox)[4]
		{
			("rowLightbar", "灯带", _lbPowerSw, _lbEffectCombo),
			("rowLogo", "Logo", _logoPowerSw, _logoEffectCombo),
			("rowHinge", "铰链", _hingePowerSw, _hingeEffectCombo),
			("rowSync", "同步", _syncPowerSw, _syncEffectCombo)
		};
		_applyingCollapsedLightRowLayout = true;
		try
		{
			for (int i = 0; i < rows.Length; i++)
			{
				RCheckBox powerSwitch = rows[i].Switch;
				if (powerSwitch == null || powerSwitch.IsDisposed)
				{
					continue;
				}
				TableLayoutPanel row = powerSwitch.Parent as TableLayoutPanel;
				if (row == null)
				{
					Control[] foundRows = Controls.Find(rows[i].RowName, true);
					if (foundRows.Length > 0 && foundRows[0] is TableLayoutPanel foundRow)
					{
						row = foundRow;
					}
				}
				if (row == null || row.IsDisposed)
				{
					continue;
				}
				bool changed = false;
				// ① 隐藏效果下拉（col2）与「编辑」按钮（col3）—— 每次同步确保（⛔ 只改 UI，不改开关逻辑）
				ComboBox combo = rows[i].Combo;
				if (combo != null && !combo.IsDisposed && (combo.Visible || combo.Enabled))
				{
					combo.Visible = false;
					combo.Enabled = false;
					changed = true;
				}
				foreach (Control child in row.Controls)
				{
					if (row.GetColumn(child) == 3 && child.Visible)
					{
						child.Visible = false;
						changed = true;
					}
				}
				// ② 开关钉在 col1（标签右侧）
				int previousColumn = row.GetColumn(powerSwitch);
				if (previousColumn != 1)
				{
					row.SetColumn(powerSwitch, 1);
					Logger.WriteLine("[light] 宝龙达：" + rows[i].Label + " 行开关从 col" + previousColumn + " 移回 col1（标签右侧）");
					changed = true;
				}
				// ③ 列宽 = 键盘行**实际像素列宽**（col0/col1），col2/col3 = 0
				if (col0Width > 0 && col1Width >= 0)
				{
					if (ApplyBaolongdaLightRowWidths(row, col0Width, col1Width, force: false))
					{
						changed = true;
					}
				}
				if (changed)
				{
					row.PerformLayout();
				}
				// 记录/更新（机主/后续排查用；同标签覆盖）
				bool recorded = false;
				for (int j = 0; j < _baolongdaCollapsedLightRows.Count; j++)
				{
					if (string.Equals(_baolongdaCollapsedLightRows[j].Label, rows[i].Label, StringComparison.Ordinal))
					{
						_baolongdaCollapsedLightRows[j] = (row, powerSwitch, rows[i].Label);
						recorded = true;
						break;
					}
				}
				if (!recorded)
				{
					_baolongdaCollapsedLightRows.Add((row, powerSwitch, rows[i].Label));
				}
				// ④ ⭐ UI 审查：读**真实运行值**；不一致 ⇒ 标 FAIL 并继续尝试修正（最多 3 次）✅
				AuditBaolongdaLightRowLayout(row, powerSwitch, rows[i].Label, keyboardRow, col0Width, col1Width);
			}
			// ⭐ 需求 4：顺带核对「更多开关」分区 —— 那边是 FlowLayoutPanel 分组（无隐藏列）⇒ 不受同一问题影响
			Logger.WriteLineIfChanged("light-ui-audit-quick",
				"[light][UI审查] 「更多开关」分区：FlowLayoutPanel 分组布局（无 TableLayoutPanel 隐藏列）⇒ 不受灯光行同一问题影响");
		}
		finally
		{
			_applyingCollapsedLightRowLayout = false;
		}
	}

	/// <summary>
	/// ⭐ 把**键盘行的实际像素列宽**复制给非键盘灯行；<paramref name="force" /> = 即使数值相同也重设
	/// （FAIL 重试时强制触发布局）✅。返回是否真的改动了列宽样式。
	/// </summary>
	private static bool ApplyBaolongdaLightRowWidths(TableLayoutPanel row, int col0Width, int col1Width, bool force)
	{
		if (row.ColumnStyles.Count < 4)
		{
			return false;
		}
		bool same = row.ColumnStyles[0].SizeType == SizeType.Absolute && Math.Abs(row.ColumnStyles[0].Width - col0Width) <= 0.5f
			&& row.ColumnStyles[1].SizeType == SizeType.Absolute && Math.Abs(row.ColumnStyles[1].Width - col1Width) <= 0.5f
			&& row.ColumnStyles[2].SizeType == SizeType.Absolute && row.ColumnStyles[2].Width <= 0.5f
			&& row.ColumnStyles[3].SizeType == SizeType.Absolute && row.ColumnStyles[3].Width <= 0.5f;
		if (same && !force)
		{
			return false;
		}
		row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, col0Width);
		row.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, col1Width);
		row.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 0f);
		row.ColumnStyles[3] = new ColumnStyle(SizeType.Absolute, 0f);
		return true;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主：必须做代码级 UI 审查，别再"算了应该行"）：⭐ **读真实运行值**并打日志；
	/// 若 <c>switchX != kbSwitchX</c> ⇒ 直接标 <c>FAIL</c>（默认日志级别可见）并**继续尝试修正**（最多 3 次）✅
	/// <para>日志：<c>[light][UI审查] 行=Logo · col=1 · switchX=111 · kbSwitchX=111 · kb列宽=[111,50,283,56] · rowW=506 · kbRowW=506 · 列宽=[111,50,0,345] ⇒ 对齐=True（x差=0px）</c></para>
	/// </summary>
	private void AuditBaolongdaLightRowLayout(TableLayoutPanel row, RCheckBox powerSwitch, string rowLabel,
		TableLayoutPanel keyboardRow, int col0Width, int col1Width)
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			int rowWidth = row.ClientSize.Width;
			int keyboardSwitchX = _kbPowerSw?.Location.X ?? int.MinValue;
			int switchColumn = row.GetColumn(powerSwitch);
			int switchX = powerSwitch.Location.X;
			if (rowWidth <= 0 || col0Width <= 0 || keyboardSwitchX == int.MinValue)
			{
				// 行还没真正布局（灯光组折叠 / 窗口未显示）⇒ ⛔ 不谎报 FAIL，等下一次刷新再审查
				return;
			}
			int diff = switchX - keyboardSwitchX;
			bool aligned = diff == 0 && switchColumn == 1;
			int[] rowColumns = row.GetColumnWidths();
			string columnsText = (rowColumns.Length > 0) ? ("[" + string.Join(",", rowColumns) + "]") : "?";
			int keyboardRowWidth = keyboardRow?.ClientSize.Width ?? -1;
			int[] keyboardColumns = (keyboardRow != null) ? keyboardRow.GetColumnWidths() : Array.Empty<int>();
			string keyboardColumnsText = (keyboardColumns.Length > 0) ? ("[" + string.Join(",", keyboardColumns) + "]") : "?";
			string message = $"[light][UI审查] 行={rowLabel} · col={switchColumn} · switchX={switchX}"
				+ $" · kbSwitchX={keyboardSwitchX} · kb列宽={keyboardColumnsText} · rowW={rowWidth} · kbRowW={keyboardRowWidth} · 列宽={columnsText}"
				+ $" ⇒ 对齐={(aligned ? "True" : "FAIL")}（x差={diff}px · 第 {attempt} 次）";
			if (aligned)
			{
				Logger.WriteLineIfChanged("light-ui-audit-" + rowLabel, message);
				return;
			}
			// ❌ 不一致 ⇒ ⛔ 不只打日志：列宽/列位没到位就强制重设后再读一次；
			//    ⚠️ 若列宽已经等于键盘行像素宽仍偏 ⇒ 属于测量/边距问题，记一次 FAIL 后结束本轮（⛔ 不做无意义重排死循环）
			bool widthsMatch = rowColumns.Length >= 2 && rowColumns[0] == col0Width && rowColumns[1] == col1Width;
			bool columnFixed = switchColumn == 1;
			if (columnFixed && widthsMatch)
			{
				if (Logger.TryMarkChanged("light-ui-audit-fail-" + rowLabel, message))
				{
					Logger.WriteError(message);
				}
				return;
			}
			if (switchColumn != 1)
			{
				row.SetColumn(powerSwitch, 1);
			}
			if (col0Width > 0)
			{
				ApplyBaolongdaLightRowWidths(row, col0Width, col1Width, force: true);
			}
			keyboardRow?.PerformLayout();
			row.PerformLayout();
			if (Logger.TryMarkChanged("light-ui-audit-fail-" + rowLabel, message))
			{
				Logger.WriteError(message);
			}
		}
	}

	private void SyncDisplayEffectRow()
	{
		MechrevoHw? hw = Program.hw;
		if (hw == null || _displayEffectSwitch == null || _displayEffectCombo == null)
		{
			return;
		}
		_syncingDisplay = true;
		try
		{
			if (hw.DisplayEffectOn.HasValue && _displayEffectSwitch.Checked != hw.DisplayEffectOn.Value)
			{
				_displayEffectSwitch.Checked = hw.DisplayEffectOn.Value;
			}
			_displayEffectCombo.Enabled = _displayEffectSwitch.Checked;
			_displayEffectEditBtn.Enabled = _displayEffectSwitch.Checked;
			if (hw.DisplayEffectMode.Length > 0)
			{
				foreach (KeyValuePair<string, string> item in _displayEffectCombo.Items)
				{
					if (item.Value == hw.DisplayEffectMode)
					{
						int idx = _displayEffectCombo.Items.IndexOf(item);
						if (_displayEffectCombo.SelectedIndex != idx)
						{
							_displayEffectCombo.SelectedIndex = idx;
						}
						break;
					}
				}
			}
		}
		finally
		{
			_syncingDisplay = false;
		}
	}

	private string GetDisplayEffectMode()
	{
		if (_displayEffectCombo != null && _displayEffectCombo.SelectedItem is KeyValuePair<string, string> kv && kv.Value.Length > 0)
		{
			return kv.Value;
		}
		MechrevoHw? hw = Program.hw;
		if (hw != null && hw.DisplayEffectMode.Length > 0)
		{
			return hw.DisplayEffectMode;
		}
		// 2026-10-09（默认值修正）：不再凭空返回 "VibrantMode"。旧写法在"用户从未选过档位、
		// GCU 也没上报当前档位"时会返回多彩，于是打开色彩增强开关的瞬间就静默应用了「多彩」
		// —— 这正是「关着还显示多彩 / 一开就是多彩」的同一处 bug。返回空串表示"没有明确档位"，
		// 调用方（SettingsForm 开关、DisplayEffectEditorForm）都已按空档位跳过应用，只开开关。
		return "";
	}

	private static Font UiStyleCaptionFont()
	{
		return UiVisualStyle.Font(UiStyleCaption());
	}

	private static Font UiStyleBody()
	{
		return UiVisualStyle.Font(9f);
	}

	internal static async Task RunHidWhileFirmwarePowerBypassCompletesAsync(Task? firmwarePowerBypass, Action startHid)
	{
		startHid();
		if (firmwarePowerBypass != null)
		{
			try
			{
				await firmwarePowerBypass;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("RGB 固件电源旁路失败，仍重申自定义帧模式: " + ex.Message);
			}
			Logger.WriteLine("RGB 固件电源落地，重申自定义帧模式");
			startHid();
		}
	}

	internal static void StopHidEffectForCurrentGeneration(int stopGeneration, Func<int> currentGeneration, Action stopHid)
	{
		if (stopGeneration == currentGeneration())
		{
			stopHid();
		}
	}

	private static bool ShouldUseGcuKeyboardFallback(KeyboardRgb? rgb)
	{
		if (rgb != null)
		{
			// 2026-10-09（② 的配套）：通道归属判据与灯效表判据解耦 —— 与 RgbForm.IsGcuKeyboardFallback 一致，
			// 仍按 KeyboardType∈{1,2} 硬判走 GCU/MQTT（4 分区机型不被切到 HID），避免回归。
			if (KeyboardFirmwareEffects.IsGcuZoneKeyboardPath(CurrentKeyboardType()))
			{
				return true;
			}
			return KeyboardLightPathPolicy.ShouldUseGcuKeyboardFallback(rgb.ControllerAvailability, rgb.IsConnected, Program.service != null && (Program.hw?.IsConnected ?? false), hidBrightnessTookEffect: true);
		}
		return false;
	}

	private static int CurrentKeyboardType()
	{
		return Program.hw?.Capabilities.KeyboardType ?? 0;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主要求「运行时自校准」）：打开/刷新键盘灯设置时**排队一次**亮度上限探测 ✅。
	/// <para>⭐ 探测本体（写 WMI 18 + 回读 + try/finally 恢复）在
	/// <c>MechrevoHw.EnsureKeyboardLightingCalibration()</c> 里**只跑一次并缓存**；
	/// 本方法只负责"后台线程触发"（结果供 <c>RgbForm</c> 的亮度滑块范围使用）✅。</para>
	/// <para>⚠️ 必须后台线程：探测含多次 WMI 往返（约 &lt; 1s），⛔ 不阻塞设置界面 ✅。</para>
	/// <para>⚠️ 彩虹**不参与校准**：它已改为软件刷色 ⇒ 界面始终显示「单色 + 彩虹」✅。</para>
	/// </summary>
	private void EnsureBaolongdaKeyboardCalibrationQueued()
	{
		if (Program.hw?.UsesBaolongda != true)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw == null || hw.KeyboardLightingCaps != null)
		{
			return;
		}
		if (Interlocked.Exchange(ref _kbdCalibrationQueued, 1) != 0)
		{
			return;
		}
		Task.Run(delegate
		{
			try
			{
				hw.EnsureKeyboardLightingCalibration();
			}
			catch (Exception ex)
			{
				Logger.WriteError("[kbd-cal] 键盘灯亮度上限探测（设置界面触发）异常（已吞掉）：" + ex.Message);
			}
		});
	}

	private void FillKeyboardEffectCombo(ComboBox combo)
	{
		bool flag = ShouldUseGcuKeyboardFallback(Program.rgb);
		int keyboardType = CurrentKeyboardType();
		(string, string)[] array = KeyboardFirmwareEffects.Visible(keyboardType);
		// ⭐⭐ 2026-10-16（宝龙达 · 效果表收敛）：⭐ 宝龙达固件**只支持 关／单色／自动循环** ✅
		//   ⇒ ⭐ 强制走「固件效果表」这一路（⭐ 形状是 (Id, Label) ✅），⭐ 且只留那三项 ✅。
		//   ⚠️ 必须**同时**改下面键盘行的 SelectedIndexChanged（⭐ 它用同一张表按索引取 Id ⚠️）——
		//      否则下拉显示三项、索引却映射到呼吸/波浪 ⇒ ⭐ 选「单色」实际发的是别的效果 ❌。
		if (Program.hw?.UsesBaolongda == true)
		{
			flag = true;
			// ⭐⭐ 2026-10-19（软件彩虹）：彩虹始终可用（软件刷色）⇒ 固定用「单色 + 彩虹」两项 ✅
			array = BaolongdaLightEffects;
		}
		(int, string)[] array2 = RgbForm.VisibleHidEffects(keyboardType);
		string[] array3 = (flag ? array.Select(((string Id, string Label) e) => e.Label).ToArray() : array2.Select(((int Mode, string Name) e) => e.Name).ToArray());
		int num;
		if (flag)
		{
			LightChannelSettings settings = LightingSettingsStore.Load("Keyboard/Ctrl", array[0].Item1);
			num = Array.FindIndex(array, ((string Id, string Label) e) => e.Id == settings.Effect);
		}
		else
		{
			int mode = Program.rgb?.KbHidMode ?? (-1);
			num = ((mode >= 0) ? Array.FindIndex(array2, ((int Mode, string Name) e) => e.Mode == mode) : (-1));
		}
		if (num < 0 && Program.hw?.UsesBaolongda == true)
		{
			// ⭐ 旧配置里可能还存着已撤掉的 Manual（模式 3 —— 本机固件写不进）
			//   ⇒ ⭐ 显示为「单色」（模式 2 ＋ 用户颜色），⛔ 不假装仍支持 ✅
			num = Array.FindIndex(array, ((string Id, string Label) e) => e.Id == "Single");
		}
		if (num < 0)
		{
			num = 0;
		}
		bool flag2 = combo.Items.Count == array3.Length;
		if (flag2)
		{
			for (int num2 = 0; num2 < array3.Length; num2++)
			{
				if (combo.GetItemText(combo.Items[num2]) != array3[num2])
				{
					flag2 = false;
					break;
				}
			}
		}
		if (flag2 && combo.SelectedIndex == num)
		{
			return;
		}
		_syncingEffectCombos = true;
		if (!flag2)
		{
			combo.Items.Clear();
			string[] array4 = array3;
			foreach (string key in array4)
			{
				combo.Items.Add(new KeyValuePair<string, int>(key, 0));
			}
		}
		if (combo.Items.Count > 0)
		{
			combo.SelectedIndex = Math.Clamp(num, 0, combo.Items.Count - 1);
		}
		// 2026-10-10（用户原话「拨动（开关）之后会变成错的 我指针移上去就刷新成对的了」）：
		// ⭐ **这一颗下拉框的状态刚被改过**（`Items.Clear()` + 重新 `Add` + 重设 `SelectedIndex`），
		// 而 `RComboBox` 的闭合态是整块自绘（`PaintFlat`）—— `Items.Clear()` 那一下只让**原生**控件
		// 失效了它自己的"选择字段"那一小块，我们的整块自绘不保证被重跑 ⇒ 屏幕上就停在被局部擦过/铺过的样子
		// （用户看到的"多出来的一道线/残留"），直到下一次**完整**重绘（悬停 / 焦点 / 尺寸变化）才修好。
		// 就在**状态真正变化**的这一处，失效**正确的目标控件**（本方法只在这颗下拉框真的被改过时才走到这里：
		// 上面 `if (flag2 && combo.SelectedIndex == num) return;` 已经把"没变"全部挡掉了）。
		// ⛔ 只 `Invalidate()` 这一颗（排队一次重绘，不动整窗、不重下发主题、不改任何属性/几何/Items）。
		if (combo.IsHandleCreated && !combo.IsDisposed)
		{
			combo.Invalidate();
		}
		_syncingEffectCombos = false;
	}

	/// <summary>
	/// 2026-10-10（用户原话「拨动（键盘灯开关）之后会变成错的 我指针移上去就刷新成对的了」＋
	/// 「这个细线不是焦点本身产生的」）：⭐ **「键盘」那一行的失效点** —— 开关状态落定（或落定后又被还原）之后，
	/// 按**当前**状态把这一行与其中的下拉框立刻重画一次。
	///
	/// 为什么需要的正是"重画一次"：`RComboBox` 的闭合态外观由 <c>PaintFlat</c> 一次画完（圆角填充 +
	/// 圆角描边 + chevron + 文字），而它的**全部**绘制输入只有 `BackColor` / `BorderColor` / `ArrowColor` /
	/// `Enabled` / `_arrowHot` / `DroppedDown` / 自身几何（见 `RComboBox.PaintSplit`）——
	/// 这些**没有一个**会被"键盘灯开关"改变，所以拨开关之后屏幕上多出来/残留的那道线**只能是"没人重画"**：
	/// 任何一次重绘（悬停 / 焦点 / 尺寸变化）都会立刻把它修好 —— 这正是用户实测到的签名。
	/// 而 2026-10-10 上一轮把 `StyleComboBox` 里那句"每次外观下发都重下发 `SetWindowTheme` 并补一次
	/// `Invalidate()`"删掉之后（见 <c>UiVisualStyle.ApplyDarkComboTheme</c>：主题名没变时**稳态零开销**），
	/// 外观下发链（`ApplyTree` → `StyleComboBox`）不再顺带给这颗下拉一次重绘 ⇒ 那次"本该有的重绘"永久缺席。
	/// ⛔ 本方法**只**失效「键盘」这一行（`_kbPowerSw` / `_kbEffectCombo` 所在的行面板）与那颗下拉框本体：
	/// 不碰整窗、不碰其它行、不重下发主题、不改任何颜色/几何/Items/事件。
	/// </summary>
	private void RepaintKeyboardLightRow()
	{
		if (IsDisposed)
		{
			return;
		}
		Control row = _kbEffectCombo?.Parent ?? _kbPowerSw?.Parent;
		if (row != null && !row.IsDisposed && row.IsHandleCreated)
		{
			// 行面板：**连子控件一起**失效（`Invalidate(true)`）—— 行内紧贴下拉框那一圈像素属于行自己的客户区，
			// 只失效下拉框本体盖不住它。
			row.Invalidate(invalidateChildren: true);
			row.Update();
		}
		if (_kbEffectCombo != null && !_kbEffectCombo.IsDisposed && _kbEffectCombo.IsHandleCreated)
		{
			// 下拉框本体：`Invalidate()` 排队一次重绘、`Update()` 立刻把它做掉
			// （用户口径：拨完开关**不移动鼠标、不切焦点**，外观当场就该是对的）。
			_kbEffectCombo.Invalidate();
			_kbEffectCombo.Update();
		}
	}

	/// <summary>
	/// 2026-10-11（用户原话「1通道 0开启删掉 把自动关闭时间放这个位置 然后下拉列表可以再缩窄一点点」
	/// ＋「我指的是 自动关闭时间的下拉框」）：**只给「自动关闭时间」这一颗**下拉按内容定一个更窄的宽度。
	///
	/// 口径（＝上一轮"量宽与画宽不一致"那次的教训：先对齐口径、再定尺寸）：
	///   · 闭合态文本的**绘制**走 <c>RComboBox.ClosedStateTextRectangle()</c>：
	///     文本区宽 = 客户区宽 − 箭头格(<c>ArrowCellLogicalPx</c>＝26 逻辑像素) − 左内边距(<c>TextPadLogical</c>＝8) × 2，
	///     文本由 <c>TextRenderer.DrawText(..., EndEllipsis)</c> 画在该矩形里；
	///   · 所以"装不装得下"的判据就是同一个减法：<c>TextRenderer.MeasureText</c>（**默认**＝带内边距口径，
	///     与 <c>DrawText</c> 默认一致；⛔ 这里**不能**用 NoPadding —— 那会少算约 7px、又打省略号）；
	///   · 最终宽 = <c>min( max( 收窄值, 硬底线 ), maxWidth )</c>；
	///     <c>maxWidth</c>＝本颗原先所在那一格的宽（<c>D(104)</c>）⇒ 本改动**只会缩、绝不会比改前宽**。
	///   · ⛔ 高度完全不参与：仍由族常量 <c>UiVisualStyle.ComboHeightLogical = 23</c> 与
	///     <c>RComboBox.SetBoundsCore</c>/<c>ApplyFamilyHeight</c> 钉住（96 DPI 实测改前/改后都是高 24）。
	/// ⚠️ 只改**本颗控件自己的 Width**：<c>UiVisualStyle</c> / <c>RComboBox</c> 的族规则、以及其它任何下拉框
	/// （含同一张卡片里「键盘」行的单色下拉、「灯带 / Logo / 铰链 / 同步」那几行）一概未动。
	///
	/// ⚠️ 实测（96 DPI / Microsoft YaHei UI 9pt / 旧宽 104 设备像素 ⇒ 文本区 = 104 − 26 − 16 = **62**）：
	///   zh-CN 各选项实测：<c>关 20 / 10 分钟 50 / 45 分钟 50 / 1 小时 43 / 2 小时 43</c>；
	///   ⚠️ **最长那一项 <c>10 秒（软件）</c> = 86 &gt; 62** ⇒ 它在**改前就已经**放不下（本来就会被
	///   <c>EndEllipsis</c> 截断），⛔ 不是本次改动引入的。因此"硬底线"取"**改前本来就放得下的**最长选项
	///   （50）＋箭头格＋两侧内边距"＝92：收窄值 = ⌊92×0.9⌋ = 82，取 max(82, 92) = 92 ⇒ 可见宽
	///   96 → 84 逻辑像素（**-12.5%**）；此时文本区 = 92 − 26 − 16 = 50 ⇒
	///   <c>关 / 10 分钟 / 45 分钟 / 1 小时 / 2 小时</c> 全部仍**完整显示**（50 ≤ 50，另有取整余量）。
	/// </summary>
	private static int FitAutoOffComboWidth(RComboBox combo, int maxWidth)
	{
		Font font = combo.Font ?? UiStyleBody();
		float scale = UiDpi.Paint(combo) / 96f;
		int textPad = (int)Math.Round((float)combo.TextPadLogical * scale);
		int arrowCell = (int)Math.Round((float)combo.ArrowCellLogicalPx * scale);
		// 改前**实际能画出来的文本区**（＝旧宽度下"本来就放得下"的那些选项的宽）—— 用它当硬底线，
		// 保证"缩窄后仍能完整显示"的选项集合不比改前少。
		int oldTextArea = Math.Max(1, maxWidth - arrowCell - textPad * 2);
		int longest = 0;
		foreach ((string Label, int Mins, int IdleSeconds) option in LightingCloseTimerOptions)
		{
			int optionWidth = TextRenderer.MeasureText(option.Label ?? string.Empty, font).Width;
			if (optionWidth <= oldTextArea)
			{
				longest = Math.Max(longest, optionWidth);
			}
		}
		// 硬底线：完整显示"改前本来放得下的最长选项"所需的控件宽。
		int floor = longest + arrowCell + textPad * 2;
		// "再缩一点点"＝在硬底线之上再收 10%（向下取整 ⇒ 只会更窄、不会反而变宽）。
		int narrowed = (int)Math.Floor(floor * 9.0 / 10.0);
		int width = Math.Max(narrowed, floor);
		return Math.Max(1, Math.Min(width, maxWidth));
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（机主截 bug：自动关闭时间做成两个入口）：
	/// **唯一入口** = 灯光区头部已有的「自动关闭时间」下拉 ✅。
	/// <para>⭐ 平台分支：宝龙达 ⇒ 软件实现选项（关/10 秒/5 分钟/10 分钟/30 分钟，`light_autooff_seconds`）；
	/// 其它机型 ⇒ 原厂商选项（`lighting_idle_seconds`）**逐位不变** ✅。</para>
	/// <para>⚠️ 只在平台切换时重填（⛔ 不每次刷新清空重建）；hw 未就绪时先按厂商表填充 ✅。</para>
	/// </summary>
	private void FillLightingAutoOffCombo()
	{
		RComboBox combo = _lightSleepCombo;
		if (combo == null || combo.IsDisposed)
		{
			return;
		}
		int platform = (Program.hw?.UsesBaolongda == true) ? 1 : 0;
		if (_lightAutoOffComboPlatform == platform && combo.Items.Count > 0)
		{
			return;
		}
		_lightAutoOffComboPlatform = platform;
		_syncingLightHeader = true;
		try
		{
			combo.Items.Clear();
			if (platform == 1)
			{
				// ⭐ 宝龙达：我们的软件实现（统一按秒存）
				foreach ((string Label, int Seconds) option in KeyboardAutoOffOptions)
				{
					combo.Items.Add(option.Label);
				}
				int seconds = MechrevoHw.GetKeyboardAutoOffSeconds();
				int index = Array.FindIndex(KeyboardAutoOffOptions,
					((string Label, int Seconds) o) => o.Seconds == seconds);
				combo.SelectedIndex = Math.Max(0, index);
			}
			else
			{
				// ⭐ 其它机型：原厂商逻辑（逐位不变）
				foreach ((string Label, int Mins, int IdleSeconds) option in LightingCloseTimerOptions)
				{
					combo.Items.Add(option.Label);
				}
				int configuredIdleSeconds = AppConfig.Get("lighting_idle_seconds",
					Math.Max(0, Program.rgb?.CloseTimerMinutes ?? 0) * 60);
				int index = Array.FindIndex(LightingCloseTimerOptions,
					((string Label, int Mins, int IdleSeconds) o) => o.IdleSeconds == configuredIdleSeconds);
				combo.SelectedIndex = (index >= 0) ? index : 0;
			}
		}
		finally
		{
			_syncingLightHeader = false;
		}
	}

	private void SyncLightRows()
	{
		MechrevoHw hw = Program.hw;
		// ⭐⭐ 2026-10-19（机主：宝龙达「Logo 灯」开关依旧跑到最右 —— 这次先做代码级 UI 审查）：
		//   ⚠️ 前两次失效根因（与「Logo 特效下拉每次同步隐藏」同一个坑）：
		//     1) 建行时 `Program.hw?.UsesBaolongda` 往往还是 false（启动懒加载 + 后端探测异步）
		//        ⇒ 建行时的"收列 + 记录行"没发生；
		//     2) 运行期老实现（下面这段）只在"编辑按钮可见"那一次写列宽，而且写的是 **80%/20%/0/0**
		//        ⇒ 开关被推到最右 ❌（正是机主截图）
		//   ⭐ 现在把「按平台收列 + 开关对齐 + [UI审查]」整体挪到
		//      `ApplyBaolongdaLightRowLayout(hw)`：每次灯光同步都跑（RefreshDeviceCapabilities /
		//      3 s 灯光轮询 / 「灯光」组展开 都会进这里），不再依赖建行时刻 ✅
		ApplyBaolongdaLightRowLayout(hw);
		// ⭐⭐ 2026-10-19（机主批准「灯光自动关闭时间」）：每次刷新灯光行时确保计时器与配置一致
		//   （默认 0 = 关闭 ⇒ 不创建任何计时器、无额外检查开销；幂等）✅
		Program.hw?.ApplyKeyboardAutoOffSetting();
		// ⭐ 「自动关闭时间」**唯一入口**：按平台填充/回填同一下拉（宝龙达软件实现 / 其它厂商逻辑）✅
		FillLightingAutoOffCombo();
		if (Program.hw?.UsesBaolongda == true && _lightSleepCombo != null && !_lightSleepCombo.IsDisposed)
		{
			int autoOffSeconds = MechrevoHw.GetKeyboardAutoOffSeconds();
			int autoOffIndex = Array.FindIndex(KeyboardAutoOffOptions,
				((string Label, int Seconds) o) => o.Seconds == autoOffSeconds);
			if (autoOffIndex >= 0 && _lightSleepCombo.SelectedIndex != autoOffIndex)
			{
				_syncingLightHeader = true;
				try
				{
					_lightSleepCombo.SelectedIndex = autoOffIndex;
				}
				finally
				{
					_syncingLightHeader = false;
				}
			}
		}
		bool isLightingTemporarilySuspended = Program.IsLightingTemporarilySuspended;
		if (_kbPowerSw != null && !IsDisposed)
		{
			// 2026-10-12（键盘灯「有时候控制不了」根因①：门控背离）：就绪提示与开关显示统一用
			// KeyboardLightingDecision（= 硬件报 On 且用户没明确关过 ⇒ 视为开），UI 不再与硬件长期背离。
			// ⚠️ 2026-10-12（用户拍板「键盘灯光也修复」）：判据统一走 KeyboardLightingDecision ⇒
			// 「用户明确关过」这条标记**只在硬件也报 Off 时才算数**（硬件报 On 时它是陈旧的，
			// 由 Program.SyncKbPowerFromHardwareIfDiverged 清掉），故此处开关不再被陈旧标记永久压成"关"。
			// ⭐⭐ 2026-10-18（机主：启动灯光策略「只读」）：⭐ 宝龙达下键盘行开关显示**硬件真实回读值**
			//   （启动只读同步写进 hw.KeyboardPower 的那份）—— ⛔ 不再让"缓存想开"覆盖真值
			//   （否则硬件是灭、开关却显示开 ⇒ UI 与真值不符）✅
			//   ⚠️ GCU/同方保持原判据（KeyboardLightingDecision）⇒ **逐位不变** ✅
			bool keyboardSwitchOn = (hw != null && hw.UsesBaolongda)
				? (hw.IsConnected && hw.KeyboardPower)
				: KeyboardLightingDecision.ShouldWrite(hw != null && hw.IsConnected, hw != null && hw.KeyboardPower);
			bool flag = LightingState.IsLightSwitchOn(keyboardSwitchOn, isLightingTemporarilySuspended);
			if (_kbPowerSw.Checked != flag)
			{
				_syncingSwitches = true;
				_kbPowerSw.Checked = flag;
				_syncingSwitches = false;
			}
			if (_kbEffectCombo != null)
			{
				FillKeyboardEffectCombo(_kbEffectCombo);
			}
		}
		if (_lbPowerSw != null && hw != null && hw.QuickSwitches.TryGetValue("lightbar", out var value))
		{
			bool flag2 = LightingState.IsLightSwitchOn(value, isLightingTemporarilySuspended);
			if (_lbPowerSw.Checked != flag2)
			{
				_syncingSwitches = true;
				_lbPowerSw.Checked = flag2;
				_syncingSwitches = false;
			}
		}
		if (_logoPowerSw != null && hw != null && hw.QuickSwitches.TryGetValue("logolight", out var value2))
		{
			bool flag3 = LightingState.IsLightSwitchOn(value2, isLightingTemporarilySuspended);
			if (_logoPowerSw.Checked != flag3)
			{
				_syncingSwitches = true;
				_logoPowerSw.Checked = flag3;
				_syncingSwitches = false;
			}
		}		if (_hingePowerSw != null && hw != null && hw.QuickSwitches.TryGetValue("hingelight", out var value3))
		{
			bool flag5 = LightingState.IsLightSwitchOn(value3, isLightingTemporarilySuspended);
			if (_hingePowerSw.Checked != flag5)
			{
				_syncingSwitches = true;
				_hingePowerSw.Checked = flag5;
				_syncingSwitches = false;
			}
		}
		if (_syncPowerSw != null && hw != null && hw.QuickSwitches.TryGetValue("synclight", out var value4))
		{
			bool flag6x = LightingState.IsLightSwitchOn(value4, isLightingTemporarilySuspended);
			if (_syncPowerSw.Checked != flag6x)
			{
				_syncingSwitches = true;
				_syncPowerSw.Checked = flag6x;
				_syncingSwitches = false;
			}
		}
		if (_lightOffOnBatteryChk != null && !IsDisposed)
		{
			bool flag4 = AppConfig.Is("lighting_off_on_battery");
			if (_lightOffOnBatteryChk.Checked != flag4)
			{
				_syncingLightHeader = true;
				_lightOffOnBatteryChk.Checked = flag4;
				_syncingLightHeader = false;
			}
			// ⭐ 2026-10-19：「自动关闭时间」下拉在**其它机型**仍回填原厂商配置；
			//   ⚠️ 宝龙达走上面的软件实现分支（light_autooff_seconds）⇒ 这里不碰 ✅
			if (Program.hw?.UsesBaolongda != true)
			{
				int idleSeconds = AppConfig.Get("lighting_idle_seconds", Math.Max(0, Program.rgb?.CloseTimerMinutes ?? 0) * 60);
				int num = Array.FindIndex(LightingCloseTimerOptions, ((string Label, int Mins, int IdleSeconds) o) => o.IdleSeconds == idleSeconds);
				if (num >= 0 && _lightSleepCombo != null && _lightSleepCombo.SelectedIndex != num)
				{
					_syncingLightHeader = true;
					_lightSleepCombo.SelectedIndex = num;
					_syncingLightHeader = false;
				}
			}
		}
	}

	internal static string LcSummaryText(string full)
	{
		if (full.StartsWith(Strings.LcGcuConnected) || full.StartsWith(Strings.LcDirectConnected))
		{
			List<string> list = new List<string>(3) { Strings.Connected };
			AddLcGearPart(list, Strings.Pump, "lc_pump_profile", LiquidCoolingDisplayPolicy.PumpGearLabels, Program.ble?.SavedPumpProfile, 2);
			AddLcGearPart(list, Strings.Fan, "lc_fan_profile", LiquidCoolingDisplayPolicy.FanGearLabels, Program.ble?.SavedFanProfile, 3);
			return string.Join(" · ", list);
		}
		if (full.StartsWith(Strings.LcBleConnected))
		{
			return Strings.BleAwaitingTakeover;
		}
		if (full.StartsWith(Strings.LcGcuReportsFault))
		{
			return Strings.FlowFault;
		}
		if (full.StartsWith(Strings.NotConnected))
		{
			return Strings.NotConnected;
		}
		return full;
	}

	private static void AddLcGearPart(List<string> parts, string channelPrefix, string configKey, string[] gearLabels, int? bleProfile, int topProfile)
	{
		int profile = bleProfile ?? WaterCoolerBle.NormalizeProfile(AppConfig.Get(configKey, -2), topProfile);
		string text = LiquidCoolingDisplayPolicy.GearSummaryLabel(channelPrefix, gearLabels, profile, topProfile);
		if (text != null)
		{
			parts.Add(text);
		}
	}

	internal static int ResolveColorCalibrationMode()
	{
		if (AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999)
		{
			// 自定义 ICC 档位（不走 GCU）：始终以 LM 记忆为准，不受 GCU 回读影响
			return 999;
		}
		MechrevoHw? hw = Program.hw;
		if (hw == null || !CalibrationReadbackSupported())
		{
			// 无回读能力的机型（40 系 GCU 不支持回读）优先用 LM 自己的记忆：
			// 设置校色时写入 calib_mode，保证在 LM 内关闭后重启仍保持关闭；
			// 从未用 LM 设置过才读官方控制台注册表。
			if (AppConfig.Exists("calib_mode"))
			{
				return Math.Clamp(AppConfig.Get("calib_mode"), 0, 4);
			}
			return MechrevoService.GetColorCalibrationMode();
		}
		return Program.hw.ColorCalibrationMode;
	}

	/// <summary>
	/// 校色档位数的上报口径（唯一来源）：GCU `Customize/Info` 的 `SupportColorCalibration`（整数 = 档位数）
	/// -> MechrevoHw.MqttColorCalibrationLevel（MechrevoHw.cs:2307 OnCustomizeInfo），
	/// 其次注册表 ItemSupport -> MechrevoDeviceCapabilities.ColorCalibrationLevel（MechrevoDeviceCapabilities.cs:270/300），
	/// 都没有才退回屏幕色域（sRGB 屏 1 档）。实测：本机 40 系+ sRGB 屏上报 1；50 系 P3 屏上报 4。
	/// </summary>
	internal static int CalibrationLevel()
	{
		MechrevoHw? hw = Program.hw;
		return ColorCalibrationSwitchPolicy.ResolveLevel(hw?.MqttColorCalibrationLevel, hw?.Capabilities?.ColorCalibrationLevel ?? 0, DisplayColorGamut.IsSrgbDisplay);
	}

	/// <summary>
	/// 二分设备（用户口径：40 系 + sRGB 屏，只有校准 / 不校准）：档位用开关表达，不显示档位下拉。
	/// 非二分设备：50 系 P3 屏（上报 4 档 -> 默认/sRGB/P3/AdobeRGB 下拉）、广色域屏（上报 2 档 -> 默认/sRGB 下拉）。
	/// </summary>
	internal static bool CalibrationBinaryOnly()
	{
		return CalibrationLevel() <= 1;
	}

	/// <summary>
	/// 「自定义 ICC」这一档是否可用（**唯一来源**：下拉的填充与可选性共用这一个判据）。
	/// <para>该档走的是**纯本地路径**：SettingsForm 的自定义 ICC 分支（:2516）→
	/// <c>IccHelper.ApplyIcc</c> → <c>WcsAssociateColorProfileWithDevice</c>
	/// （Windows 标准 API / mscms.dll），与 GCU 无关 ⇒ 宝龙达（非 GCU）后端上它同样可用，
	/// 不该因为"GCU 没上报档位数"就被藏掉。</para>
	/// <para>⚠️ "必须有本地 ICC 文件"是硬要求、不是保守：选这一档时若文件缺失，
	/// :2533 会回退成 <c>value = 1</c> 并调 <c>SetColorCalibration(1)</c>（那是 GCU 路径）
	/// ⇒ 宝龙达上必然失败。只有文件真的在，这一档才是纯本地、才该出现且可选。</para>
	/// <para>⚠️ 第一个分支（<c>calib_custom_on</c>）保持既有语义不变；后两个分支只会在
	/// "本地已有自定义 ICC 文件"时成立，而那正是改动前用户打开同一开关后能走到的同一档
	/// ⇒ 非宝龙达机型不受影响。</para>
	/// </summary>
	internal static bool CustomIccTierAvailable()
	{
		if (AppConfig.Is("calib_custom_on"))
		{
			return true;
		}
		string name = AppConfig.GetString("calib_custom_icc");
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		try
		{
			return File.Exists(Path.Combine(IccHelper.CustomIccDir, name));
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// 档位回读同步能力（用户口径：40 系 GCU 不支持回读、50 系支持回读开关）：
	/// 只有 50 系且 GCU 真的上报过档位字段才允许回读覆盖 UI；40 系一律不回读。
	/// </summary>
	internal static bool CalibrationReadbackSupported()
	{
		MechrevoHw? hw = Program.hw;
		return hw != null && ColorCalibrationSwitchPolicy.SupportsReadback(hw.DgpuGeneration, hw.ColorCalibrationModeSeen);
	}

	internal static int CalibComboIndexFor(int mode, ComboBox? combo = null)
	{
		if (mode == 999)
		{
			// 自定义 ICC 档位：返回下拉中 value==999 的项索引；找不到回退 0
			if (combo != null)
			{
				for (int i = 0; i < combo.Items.Count; i++)
				{
					if (combo.Items[i] is KeyValuePair<string, int> kv2 && kv2.Value == 999)
					{
						return i;
					}
				}
			}
			return 0;
		}
		return MapCalibrationMode(mode, CalibrationLevel()) - 1;
	}

	/// <summary>
	/// 界面档位 -> GCU 档位：只做「不超过 GCU 上报档位数」的截断，不再做 4 档折 2 档
	/// （2026-10-07 返工：上一轮据"50 系 GCUService.exe 的 UTF-16 字符串里没有 _SRGB/_ADOBERGB"
	/// 把 4 档折成 2 档，依据不可靠也不成立 —— 同一 exe 的 UTF-8 命令名表里
	/// `COLOR_CALIBRATION_ON_DEFAULT / _SRGB / _P3 / _ADOBERGB` 四档齐全，见 MechrevoService.ColorCalibrationAction）。
	/// 二分设备（40 系 + sRGB 屏，上报 1 档）截断为 1，动作词由 MechrevoService 落成裸 COLOR_CALIBRATION_ON。
	/// </summary>
	internal static int MapCalibrationMode(int mode, int max)
	{
		return Math.Clamp(mode, 1, Math.Max(1, Math.Min(max, ColorCalibrationSwitchPolicy.MaxLevel)));
	}

	/// <summary>下拉/开关索引 -> GCU 档位（按 GCU 上报档位数截断，不折叠）。</summary>
	internal static int CalibrationModeForCombo(ComboBox combo)
	{
		if (combo?.SelectedItem is KeyValuePair<string, int> kv2 && kv2.Value != 999)
		{
			return MapCalibrationMode(kv2.Value, CalibrationLevel());
		}
		return 1;
	}

	/// <summary>按 GCU 实时上报档位数重建校色配置下拉（构建后 GCU 上报变化时也调用）。</summary>
	private void RebuildCalibrationItems(ComboBox calibCombo)
	{
		if (calibCombo == null)
		{
			return;
		}
		int calibLevel = CalibrationLevel();
		int currentMode = ResolveColorCalibrationMode();
		_syncingDisplay = true;
		try
		{
			calibCombo.Items.Clear();
			calibCombo.Items.Add(new KeyValuePair<string, int>(Strings.Default, 1));
			if (calibLevel >= 2)
			{
				calibCombo.Items.Add(new KeyValuePair<string, int>("sRGB", 2));
			}
			if (calibLevel >= 3)
			{
				calibCombo.Items.Add(new KeyValuePair<string, int>("P3", 3));
			}
			if (calibLevel >= 4)
			{
				calibCombo.Items.Add(new KeyValuePair<string, int>("AdobeRGB", 4));
			}
			if (CustomIccTierAvailable())
			{
				// 「自定义」项（经 Windows ICC，不走 GCU）。
				// 2026-10-16：判据由 `calib_custom_on` 改为 CustomIccTierAvailable()，多认一条
				// "本地已有自定义 ICC 文件" —— 宝龙达（非 GCU）下 GCU 不上报档位数（CalibrationLevel() <= 1），
				// 若不认文件，用户即使在「设置」里导入了 ICC，下拉也永远只有「默认」一项。
				calibCombo.Items.Add(new KeyValuePair<string, int>(Strings.CustomCalibration, 999));
			}
			calibCombo.SelectedIndex = CalibComboIndexFor(currentMode, calibCombo);
			// 下拉始终可见（含二分设备只列出「默认」一项）：整行布局靠它占位，隐藏会让右侧空一大块。
			// 只有一项且没开「自定义校色」时不可选（Enabled=false）——点了也没有别的档可选，
			// 避免做出一个“看着能选其实无效”的控件；开着时（多出 ICC 档）照常可选。
			// 注意：40 系二分语义不变——校准/不校准仍由开关表达、仍不做回读。
			calibCombo.Visible = true;
			// 2026-10-16：宝龙达下「屏幕校色」开关已被隐藏（它没有非 GCU 路径，见 flag11GcuOnly 处）
			// ⇒ 不能拿它的 Checked 当门，否则下拉会因为"那颗隐藏的开关没勾"而永久灰掉、
			// 用户根本选不了「自定义」。开关**可见**时与改动前逐位相同（仍取 Checked）；
			// 开关不可见时视为不设门。构建期 _calibSwitch 尚为 null ⇒ 也走"不设门"，与改动前的
			// `?? true` 结果一致。
			bool enabledBySwitch = (_calibSwitch?.Visible ?? false) ? (_calibSwitch?.Checked ?? true) : true;
			calibCombo.Enabled = CalibrationComboInteractive(enabledBySwitch);
		}
		finally
		{
			_syncingDisplay = false;
			// 2026-10-09（0.291 beta22 补丁⑭ **兜底**；用户原话「我想可以补一个兜底就是刷新后立刻重绘一次把它这个盖掉」）：
			// 本方法在**启动后的校色能力/状态刷新**里被调用（`Items.Clear()` → 重新 Add → 重设 `SelectedIndex`
			// → 改 `Enabled`/`Visible`），这些都会给下拉带来重绘与背景擦除消息。**根治**那条已在
			// `UiVisualStyle.HandleOpaqueChrome`（`RComboBox` 分支不再真的擦背景 ⇒ 不存在"擦了没重画"的窗口）；
			// 这里再按用户要求**补一次主动重绘**：刷新一结束立刻重画一次，把任何可能残留的中间态盖掉。
			// ⚠️ 这是兜底、不是根治；只调 `Invalidate`（不碰 Items / 事件 / 尺寸 / 外观属性）。
			calibCombo?.Invalidate();
		}
	}

	/// <summary>
	/// 档位下拉是否「可选」：二分设备（40 系 + sRGB 屏）只列「默认」一项时不可选（Enabled=false），
	/// 但仍可见占位；非二分设备（50 系 4 档 / 广色域屏 2 档）以及开了「自定义校色」（多出 ICC 档）时可选。
	/// enabledBySwitch：开关态（总开关关时档位无意义，沿用既有「开关关 -> 档位灰」的表现）。
	/// </summary>
	private static bool CalibrationComboInteractive(bool enabledBySwitch = true)
	{
		// 2026-10-16：第二项由 `calib_custom_on` 改为 CustomIccTierAvailable() —— 与
		// RebuildCalibrationItems 的**填充判据同源**，避免出现"项加进来了却说不可选"（或反过来）的口径打架。
		return (!CalibrationBinaryOnly() || CustomIccTierAvailable()) && enabledBySwitch;
	}
	
	/// <summary>校色配置下拉的当前索引（关闭由开关独立控制，下拉不再含“关闭”项）。</summary>
	internal static int ResolveCalibrationComboIndex()
	{
		return CalibComboIndexFor(ResolveColorCalibrationMode());
	}

	private void UpdateDashboardScroll(bool rangeChanged = true)
	{
		if (_dashboardScroll != null && _dashboardStack != null && _dashboardScrollBar != null)
		{
			if (rangeChanged)
			{
				int contentHeight = Math.Max(_dashboardStack.Height, DeepestContentBottom(_dashboardStack) + _dashboardStack.Padding.Bottom);
				_dashboardScrollBar.SetRange(contentHeight, _dashboardScroll.ClientSize.Height);
			}
			_dashboardStack.Location = new Point(_dashboardStack.Left, -_dashboardScrollBar.Value);
		}
	}

	private static int DeepestContentBottom(Control root)
	{
		int num = 0;
		foreach (Control control in root.Controls)
		{
			if (control.Visible)
			{
				num = Math.Max(num, control.Bottom);
				int num2 = DeepestContentBottom(control);
				if (num2 > 0)
				{
					num = Math.Max(num, control.Top + num2);
				}
			}
		}
		return num;
	}

	/// <summary>
	/// 2026-10-09（settingswarm）：把设置对话框的**外壳**（控件树 + OnLoad/OnShown + 首帧）提前付掉、
	/// 而且**不显示**，于是用户点「设置」时只剩 <c>Show()</c> + 数据刷新。
	/// <para>排在哪：主窗口首帧显示之后的 <c>Application.Idle</c> 里调用（见 Program.cs deferredInitialization），
	/// 而且自己再 <c>BeginInvoke</c> 一次 —— 优先级低于首帧，**冷启动到主窗口的数字一点不碰**。</para>
	/// <para>⚠️ 为什么必须"隐形 Show 一次"而不是只建树：本机实测（临时探针，验收后已删）——设置对话框
	/// 首次打开 5347ms 里，<c>OnLoad</c> 的 <c>PerformLayoutTree</c> 1370ms + <c>ShrinkToContent</c>
	/// 1444ms（内部又排一次树）**只在窗口真的 Show 过之后才能算干净**；只在隐藏状态下排树（预热里直接调
	/// <c>PerformLayoutTree</c>）实测白花 ~1.8 秒且下一次 Show 仍要重付一次（隐藏时子控件句柄没建，
	/// 布局结果不可复用）。所以这里用 <c>Opacity=0.01</c>（与主窗防闪烁同一手法，肉眼不可见）+
	/// <c>ShowWithoutActivation</c>（SW_SHOWNA，不抢焦点）Show 一次，把 OnLoad/OnShown/首帧全部付掉，
	/// 然后立刻 <c>Hide()</c> + 把 Opacity 恢复 1.0。几何与真实打开**完全同路**（同一个 PlaceAdjacent →
	/// 同一个 ShrinkToContent/ConstrainToWorkingArea）。</para>
	/// <para>退化：任何一步抛异常都只记一行日志，<c>_settingsDialog</c> 保持"没建好/没排过"的状态，
	/// 点击时仍走原来的按需构建（<see cref="EnsureSettingsDialogBuilt"/> 是幂等的）。</para>
	/// <para>代价：一次 ctor + 一次隐形 Show 的 UI 线程占用（本机实测 ctor 约 0.5 秒、隐形 Show 里
	/// PerformLayoutTree 1370ms + ShrinkToContent 1444ms，合计约 3.4 秒），账记在**首帧之后**；
	/// 只在"没有别的模态窗开着"时动手，否则 250ms 后再看一次（最多约 2.5 分钟）。额外常驻 = 一个隐藏的
	/// <see cref="SettingsDialog"/> 窗口（句柄 1 个、子控件句柄按需创建）+ 它持有的是**主窗自己的活控件**
	/// （外观/官方控制台/额外灯光两行/自启开关/跟随系统），不复制数据、不额外占内存。关闭只是 <c>Hide()</c>
	/// （既有行为），实例**复用**同一个。</para>
	/// </summary>
	internal void ScheduleSettingsShellPreWarm()
	{
		if (_settingsShellReady || _settingsShellPreWarmArmed || IsDisposed)
		{
			return;
		}
		_settingsShellPreWarmArmed = true;
		BeginInvoke((Action)PreWarmSettingsShell);
	}

	private void PreWarmSettingsShell()
	{
		_settingsShellPreWarmArmed = false;
		if (_settingsShellReady || IsDisposed)
		{
			return;
		}
		// 让路规则（只为"别卡住别人"）：有别的模态窗开着（首次引导 / 核显询问 / 更新提示）就让路，
		// 250ms 后再看一次，最多 600 次（约 2.5 分钟）后放弃预热（此后点击走按需构建＝改前行为）。
		// ⚠️ 刻意**不**按"用户正在输入"让路：那样会让"刚启动就点设置"这条最常见的路径永远等不到预热，
		// 反而退化成改前的 5 秒多；宁可把这一次约 3.4 秒的 UI 线程占用放在首帧之后立刻付掉
		// （实测：预热进行中来的点击不会另起一次构建，只是排队到预热结束 —— 最坏 ≈ 预热剩余 + 0.28 秒，
		// 仍**低于**改前的 5.2 秒，见报告）。
		if (_settingsDialog is { Visible: true } || HasForeignModalForm())
		{
			RetrySettingsShellPreWarm();
			return;
		}
		try
		{
			// 2026-10-15（D，启动阶段耗时日志）：用户反馈「LM 完全退出后重新打开要卡一会」，
			// 定位到卡的就是本方法（隐形 Show 整个设置窗）。这里把三个阶段各自计时打出来，
			// 以后这类"卡一会"一眼可定位到是 ctor / 布局 / 首帧哪一段（日志键 [startup-phase]）。
			long tCtor0 = Environment.TickCount64;
			EnsureSettingsDialogBuilt();
			long tCtor1 = Environment.TickCount64;
			SettingsDialog? dialog = _settingsDialog;
			if (dialog == null)
			{
				return;
			}
			dialog.BeginPreWarmShow();
			try
			{
				// 与真实打开同一条几何路径（先摆到主窗旁边，Show 里再做 ShrinkToContent/约束）
				ResponsiveLayout.PlaceAdjacent(dialog, this);
				dialog.Show();
				long tShow = Environment.TickCount64;
				// 首帧也在这里画掉（下一次 Show 就只剩贴上去）
				dialog.Update();
				long tUpdate = Environment.TickCount64;
				dialog.MarkLayoutPrimed();
				Logger.WriteLine($"[startup-phase] settingswarm: ctor={tCtor1 - tCtor0}ms show(含PerformLayoutTree+ShrinkToContent)={tShow - tCtor1}ms update(首帧)={tUpdate - tShow}ms total={tUpdate - tCtor0}ms");
			}
			finally
			{
				dialog.EndPreWarmShow();
			}
			_settingsShellReady = true;
			Logger.WriteLine("[settingswarm] 设置外壳已预热（隐形 Show 一次后藏回，点击时只做 Show + 刷新数据）");
		}
		catch (Exception ex)
		{
			// 预热失败绝不影响"点开时按需构建"这条退路（EnsureSettingsDialogBuilt 下次会重试）
			Logger.WriteLine("[settingswarm] 设置外壳预热失败，改回按需构建: " + ex.Message);
		}
	}

	private void RetrySettingsShellPreWarm()
	{
		if (++_settingsShellPreWarmTries > 600 || IsDisposed)
		{
			return;
		}
		System.Windows.Forms.Timer retry = new System.Windows.Forms.Timer
		{
			Interval = 250
		};
		retry.Tick += delegate
		{
			retry.Stop();
			retry.Dispose();
			ScheduleSettingsShellPreWarm();
		};
		retry.Start();
	}

	private bool HasForeignModalForm()
	{
		foreach (Form form in Application.OpenForms)
		{
			if (form != this && form is { Visible: true, Modal: true } && !form.IsDisposed)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>按需构建设置对话框外壳（幂等：已建好/没销毁就直接返回）。</summary>
	private void EnsureSettingsDialogBuilt()
	{
		if ((_settingsDialog == null || _settingsDialog.IsDisposed) && _themeModePanel != null && _officialConsolePanel != null)
		{
			// 2026-10-09（igpugate）：30 系与无独显机型不显示「核显模式显示」三选行（其余世代显示，
			// 见 IgpuOnlyUiRowAvailable）；直接不把这一行交给对话框，TableLayoutPanel 就不会为它建行。
			// 2026-10-09（lightmerge / lightfix2 / lightsplit）：「额外灯光项」那一块**总是**交给对话框
			// （lightfix2 起可见性恒为 true；lightsplit 起已细分为**两行** —— 灯带行 / Logo 灯行），
			// 见 LightExtraRowAvailable —— 它们是判据判错时的后路，不许由判据自己关掉；
			// 可见性仍交给 SetLightExtraRowAvailable 动态下发，这样能力晚到也不会像构造期一次性判定那样
			// 把后门永久关掉。
			// 2026-10-09（settingswarm）：本段从 OpenSettingsDialog 内联搬到本方法（内容一字未改），
			// 好让预热与点击走**同一段**构建代码 —— 预热没做完/失败时，点击自己会再走一遍。
			// ⭐⭐ 2026-10-19（机主：「设置窗口里根本没有这些功能，为什么要加载这么多？」）：
			//   ⭐ **判据为假 ⇒ 连控件都不创建**（⛔ 不再"先 new 再 Visible=false"）：
			//      · 核显模式三选行：只有 `IgpuOnlyUiRowAvailable()` 为真才构建（宝龙达/30 系/无独显 ⇒ 不建）；
			//      · 灯光额外两行（灯带行/Logo 行显示）：承载"判据判错时的后路"，语义不变（LightExtraRowAvailable 恒 true）⇒ 构建；
			//      · 构建时机 = 打开设置对话框时（此时 Program.hw 已就绪），⛔ 不在主窗构造函数里白建 ✅
			bool igpuRow = IgpuOnlyUiRowAvailable();
			if (igpuRow && _igpuOnlyUiPanel == null)
			{
				BuildIgpuOnlyUiPanel();
			}
			if (_lightStripUiPanel == null || _lightLogoUiPanel == null)
			{
				BuildLightRowUiPanels();
			}
			_settingsDialog = new SettingsDialog(_themeModePanel, _officialConsolePanel, _overdriveChk, _overdriveAvailable, _themeFollowBox, checkStartup, igpuRow ? _igpuOnlyUiPanel : null, _lightStripUiPanel, _lightLogoUiPanel);
			_settingsDialog.SetLightExtraRowAvailable(LightExtraRowAvailable());
			AddOwnedForm(_settingsDialog);
		}
	}

	private void OpenSettingsDialog()
	{
		EnsureSettingsDialogBuilt();
		if (_settingsDialog != null)
		{
			// 打开设置前先按当前选择同步「核显模式显示」三选行（首次引导/升级弹窗可能刚改过它）
			SyncIgpuOnlyUiChoice();
			// ⭐⭐ 2026-10-17（用户要求 · 宝龙达没有核显模式 · 用户原话
			//   「设置里面核显模式显示也只给同方开放」）：
			//   这里**再同步一次可见性**，堵住两条会让这一行"漏出来"的路：
			//   ① `SettingsDialog` 构造时把传入面板硬设成 `Visible = true`（SettingsDialog.cs:276），
			//      而 `SyncIgpuOnlyUiChoice()` 只改三颗按钮的 Enabled、**不改 Visible**；
			//   ② `EnsureSettingsDialogBuilt()` 是幂等的：若外壳是在 `Program.hw` 就绪**之前**
			//      被预热的，`IgpuOnlyUiRowAvailable()` 当时可能返回 true ⇒ 行被建进对话框，
			//      之后判据变 false 也不会重建 ⇒ 行留在那儿。
			//   ⇒ 每次打开前按**当前**判据落一次 Visible，与 RefreshDeviceCapabilities 里的
			//     同一处同步（见其 `_igpuOnlyUiPanel.Visible = igpuRow;`）口径一致。
			//   ⚠️ 非宝龙达逐位不变：这一行的可见性本来就该等于该判据，重复落一次同值无副作用。
			if (_igpuOnlyUiPanel != null)
			{
				_igpuOnlyUiPanel.Visible = IgpuOnlyUiRowAvailable();
			}
			if (_settingsDialog.Visible)
			{
				_settingsDialog.Close();
			}
			else
			{
				_settingsDialog.ShowAdjacentTo(this);
				// 用户已经真实打开过一次 ⇒ OnLoad/首帧/布局都付过了，预热不必再排队（也顺手停掉重试计时器）
				_settingsShellReady = true;
			}
		}
	}

	private void BuildFooterV2(Func<int, int> scale)
	{
		panelFooter.AutoSize = false;
		panelFooter.Padding = new Padding(scale(12), scale(6), scale(10), scale(4));
		panelFooter.Height = scale(51);
		panelFooter.Controls.Clear();
		Panel value = new Panel
		{
			Dock = DockStyle.Top,
			Height = 1,
			BackColor = UiVisualStyle.Border,
			
		};
		labelVersion.AutoSize = true;
		labelVersion.Dock = DockStyle.Fill;
		labelVersion.Font = UiVisualStyle.Font(8.5f);
		labelVersion.ForeColor = UiVisualStyle.Muted;
		BufferedTableLayoutPanel bufferedTableLayoutPanel = new BufferedTableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 7,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = new Padding(scale(4), 2, 0, 1),
			BackColor = panelFooter.BackColor
		};
		bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
		for (int i = 0; i < 6; i++)
		{
			bufferedTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f));
		}
		bufferedTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		bufferedTableLayoutPanel.Controls.Add(labelVersion, 0, 0);
		ResetLegacyFooterButton(buttonQuit, Strings.Quit, UiGlyph.Kind.Close, scale);
		ResetLegacyFooterButton(buttonDonate, Strings.Donate, UiGlyph.Kind.Heart, scale);
		ResetLegacyFooterButton(buttonUpdates, Strings.Update, UiGlyph.Kind.Update, scale);
		ResetLegacyFooterButton(buttonOverlay, Strings.Overlay, UiGlyph.Kind.Overlay, scale);
		RButton rButton = new RButton
		{
			Text = Strings.Settings,
			Cursor = Cursors.Hand,
			AutoSize = false,
			Size = new Size(FooterButtonWidth(Text, scale), scale(36)),
			Anchor = AnchorStyles.None,
			BackColor = panelFooter.BackColor
		};
		UiVisualStyle.StyleFooterGhostButton(rButton);
		UiVisualStyle.ApplyFooterGlyph(rButton, UiGlyph.Kind.Gear, scale(16));
		rButton.Click += (object? _, EventArgs _) =>
		{
			OpenSettingsDialog();
		};
		RButton diagnosticButton = new RButton
		{
			Text = Strings.Diagnostics,
			Cursor = Cursors.Hand,
			AutoSize = false,
			Size = new Size(FooterButtonWidth(Text, scale), scale(36)),
			Anchor = AnchorStyles.None,
			BackColor = panelFooter.BackColor,
			AccessibleName = Strings.ExportDiagnostics
		};
		UiVisualStyle.StyleFooterGhostButton(diagnosticButton);
		UiVisualStyle.ApplyFooterGlyph(diagnosticButton, UiGlyph.Kind.Package, scale(16));
		diagnosticButton.Click += async (object? _, EventArgs _) =>
		{
			await DiagnosticPackCommand.RunAsync(this, diagnosticButton);
		};
		bufferedTableLayoutPanel.Controls.Add(buttonOverlay, 1, 0);
		bufferedTableLayoutPanel.Controls.Add(rButton, 2, 0);
		bufferedTableLayoutPanel.Controls.Add(buttonUpdates, 3, 0);
		bufferedTableLayoutPanel.Controls.Add(buttonDonate, 4, 0);
		bufferedTableLayoutPanel.Controls.Add(diagnosticButton, 5, 0);
		bufferedTableLayoutPanel.Controls.Add(buttonQuit, 6, 0);
		panelFooter.Controls.Add(bufferedTableLayoutPanel);
		panelFooter.Controls.Add(value);
	}

	private int FooterButtonWidth(string label, Func<int, int> scale)
	{
		int num = scale(46);
		bool flag = true;
		foreach (char c in label)
		{
			if (c > '\u007F')
			{
				flag = false;
				break;
			}
		}
		if (flag)
		{
			int num2 = TextRenderer.MeasureText(label, UiVisualStyle.Font(7f)).Width;
			return Math.Max(num, scale(num2) + scale(16));
		}
		return num;
	}
	private void ResetLegacyFooterButton(Button button, string label, UiGlyph.Kind icon, Func<int, int> scale)
	{
		button.Text = label;
		button.Dock = DockStyle.None;
		button.Anchor = AnchorStyles.None;
		button.AutoSize = false;
		button.Size = new Size(FooterButtonWidth(label, scale), scale(36));
		button.BackColor = panelFooter.BackColor;
		UiVisualStyle.StyleFooterGhostButton(button);
		if (icon == UiGlyph.Kind.Overlay)
		{
			UpdateFooterOverlayVisual(AppConfig.IsOverlay());
		}
		else
		{
			UiVisualStyle.ApplyFooterGlyph(button, icon, scale(16));
		}
	}

	internal void UpdateFooterOverlayVisual(bool active)
	{
		buttonOverlay.ForeColor = (active ? UiVisualStyle.Accent : UiStyleXXX());
		UiVisualStyle.ApplyFooterGlyph(buttonOverlay, UiGlyph.Kind.Overlay, ResponsiveLayout.LogicalToDevice(this, 16), () => (!AppConfig.IsOverlay()) ? UiVisualStyle.Muted : UiVisualStyle.Accent);
	}

	private static Color UiStyleXXX()
	{
		return UiVisualStyle.Text;
	}
}