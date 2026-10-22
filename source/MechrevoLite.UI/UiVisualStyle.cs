using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MechrevoLite.UI;

internal static class UiVisualStyle
{
	internal static class TypeScale
	{
		public const float Caption = 8.5f;

		public const float Body = 9f;

		public const float Subtitle = 9.5f;

		public const float Title = 12f;

		public const float Display = 15f;
	}

	internal static class Space
	{
		public const int Xs = 4;

		public const int Sm = 8;

		public const int Md = 12;

		public const int Lg = 16;

		public const int Xl = 24;
	}

	// 2026-10-09（UI 打磨②，仅夜间）：新增 BorderStrong —— 输入类控件（RTextBox / RNumericUpDown /
	// RComboDisplay）夜间 1px 圆角描边用的「强一档」边色。夜间 60/78/112；日间本轮不启用，
	// 取值与日间 Border 完全相同（201/213/227），因此日间任何取 BorderStrong 的地方与取 Border 像素一致。
	// 2026-10-09（UI 打磨⑤，仅夜间）：在 Input 之后（保持既有字段顺序不变，末尾 Track 仍是最后一个）
	// 新增「按钮面」色阶三档 —— 语义是「浮起」，与 Input 的「凹陷/可输入」相对：
	//   ButtonFace          未选中按钮面（比卡片 Surface 明显更浅）
	//   ButtonFaceHover     悬停（再浅一档）
	//   ButtonFaceSelected  选中底（最浅一档）/ 也是 ModeActivatedFill 的调色基色
	// ⚠️ Input 的语义（凹陷/可输入）不变、数值不动；Window/Surface/SurfaceRaised 三者的既有关系也不动。
	// 2026-10-09（UI 打磨⑦，仅夜间）：在 BorderStrong 之后新增 ButtonEdge —— 「未选中按钮那一圈 1px 硬边」的
	// 专用色阶。夜间 82/82/82：比页面底与卡片亮、比 ButtonFace(62) 亮 Δ20（Border 56 画在 62 上只有 Δ6，
	// 几乎看不见 —— 这正是用户说"我们的边发虚/对比不够"的一半原因）。Border / BorderStrong / Track 等
	// **既有数值一个都没动**，日间取与日间 Border 相同的 201/213/227 ⇒ 即便日间读到它也逐像素不变。
	private readonly record struct Palette(Color Window, Color Surface, Color SurfaceRaised, Color Input, Color ButtonFace, Color ButtonFaceHover, Color ButtonFaceSelected, Color Border, Color BorderStrong, Color ButtonEdge, Color Text, Color Muted, Color Accent, Color AccentText, Color AccentHover, Color AccentPressed, Color AccentBlue, Color Danger, Color Ok, Color Warn, Color Track);

	internal const string ThemeModeConfigKey = "ui_theme_mode";

	/// <summary>2026-10-22（主题自定义）：用户自定义强调色，`#RRGGBB` 或 `#AARRGGBB`；空=默认。</summary>
	internal const string AccentColorConfigKey = "ui_accent_color";

	/// <summary>2026-10-22（主题自定义）：主窗口背景色覆盖，`#RRGGBB` 或 `#AARRGGBB`；空=默认。</summary>
	internal const string BackgroundColorConfigKey = "ui_bg_color";

	/// <summary>2026-10-22（主题自定义）：主窗口背景图片路径；空=不显示。</summary>
	internal const string BackgroundImageConfigKey = "ui_bg_image";

	/// <summary>2026-10-22（主题自定义）：背景图不透明度 0~100（默认 65，越低界面越易读）。</summary>
	internal const string BackgroundOpacityConfigKey = "ui_bg_opacity";

	/// <summary>2026-10-22（主题自定义）：内容区遮罩 0~100（默认 50，越大底图越淡、文字越清楚）。</summary>
	internal const string BackgroundScrimConfigKey = "ui_bg_scrim";

	internal const string ThemeFollowSystemKey = "ui_theme_follow_system";

	internal const string BaseFontFamily = "HarmonyOS Sans SC";
private static readonly PrivateFontCollection _embeddedFonts = new();

private static readonly List<GCHandle> _fontPins = new();

private static FontFamily? _embeddedFamily;

static UiVisualStyle()
{
	LoadEmbeddedFonts();
}

private static void LoadEmbeddedFonts()
{
	try
	{
		string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
		string[] files = new string[2] { Path.Combine(dir, "HarmonyOS_Sans_SC_Regular.ttf"), Path.Combine(dir, "HarmonyOS_Sans_SC_Bold.ttf") };
		foreach (string f in files)
		{
			if (!File.Exists(f))
			{
				continue;
			}
			_embeddedFonts.AddFontFile(f);
		}
		_embeddedFamily = _embeddedFonts.Families.Cast<FontFamily>().FirstOrDefault((FontFamily f) => f.Name.Contains("HarmonyOS Sans SC"));
	}
	catch
	{
		_embeddedFamily = null;
	}
}
	// <summary> （设置窗启动慢 · 缓存）：回退字体族只创建一次（GDI+ 的 FontFamily 构造不便宜
	private static readonly FontFamily _fallbackFamily = new FontFamily("Microsoft YaHei UI");
	// <summary> （设置窗启动慢 · 缓存）：同一个 (字号/字重/单位) 复用同一个 Font 实例。
	private static readonly Dictionary<(int SizeKey, FontStyle Style, GraphicsUnit Unit), Font> _fontCache = new();

	private static readonly object _fontCacheLock = new();

	internal static FontFamily FontFamilyOrDefault()
	{
		return _embeddedFamily ?? _fallbackFamily;
	}

	// 2026-10-09（UI 打磨②b，仅夜间；用户原话「可以 虽然说好像没有看出区别」）：上一轮②的幅度太小
	// （Window/Surface 1.11、Surface/SurfaceRaised 1.16，大面积平色上不可辨）。本轮**只抬卡片、不动页面**：
	// Window 11/18/32 保持最深（页面底），Surface 19/29/48 → 29/43/66，SurfaceRaised 27/41/68 → 39/56/85，
	// Input 13/21/38 → 9/14/24（比 Window 还暗，坐实「最暗的凹陷级」），Border 44/60/90 → 60/80/114，
	// BorderStrong 60/78/112 → 86/112/158（输入类控件 2px 描边），Track 34/48/74 → 44/62/94
	// （跟住 SurfaceRaised，保持「开关滑轨/滑槽」与卡片的既有级差 1.098 不变）。
	// WCAG：Window↔Surface 1.111 → 1.316（≥1.3）、Surface↔SurfaceRaised 1.161 → 1.208（≥1.2）、
	// Input↔Surface 1.081 → 1.358；Muted 在 SurfaceRaised 上仍有 4.576（AA）、Text ≥ 9.89。
	// ⚠️ 日间 Palette 一个数值都未改（见 DayPalette，BorderStrong 仍与日间 Border 同值）。
	//
	// 2026-10-09（UI 打磨⑤，仅夜间；用户原话「它的按钮颜色是更浅 背景更深 我们是反的 而且我们的描边没有它立体」）：
	// 上一轮把按钮面放在 Input(9/14/24) 上 —— 比 Window(11/18/32) 还暗，正是「我们是反的」。
	// 本轮新增按钮面三档（在 Input 之后），只抬按钮、不动 Window/Surface/SurfaceRaised/Input：
	//   ButtonFace 44/60/86、ButtonFaceHover 54/72/100、ButtonFaceSelected 68/80/100（比底色更「中性」一点，
	//   这样 ModeActivatedFill 用强调色淡调时绿/蓝/棕各自还读得出来）。
	// WCAG（本文件 ContrastRatio 同公式）：ButtonFace/Window 1.683、ButtonFace/Surface 1.279、
	// ButtonFaceHover/ButtonFace 1.202、ButtonFaceSelected/ButtonFace 1.365、ButtonFaceSelected/Surface 1.746、
	// Text(228,236,247) 在 ButtonFace 上 9.58、在 ButtonFaceSelected 上 7.02（≥4.5 AA）。
	// 2026-10-09（UI 打磨⑥，仅夜间；用户原话「深色模式改成和它一样的黑色背景 我们这个偏蓝色了」）：
	// 上一轮的中性层（Window 11/18/32 … Track 44/62/94）**每一档的 B 都明显高于 R**（蓝偏 21~72），
	// 整屏因此发蓝。本轮把「中性层」整体去饱和成**纯中性灰（R=G=B，B−R 全部归零）**，只保层次、不保蓝：
	//   Window 11/18/32 → 14/14/14（页面底）、Surface 29/43/66 → 40/40/40（卡片）、
	//   SurfaceRaised 39/56/85 → 54/54/54、Input 9/14/24 → 10/10/10（仍是最暗的凹陷级）、
	//   ButtonFace 44/60/86 → 62/62/62、ButtonFaceHover 54/72/100 → 72/72/72、
	//   ButtonFaceSelected 68/80/100 → 86/86/86、Border 60/80/114 → 72/72/72、
	//   BorderStrong 86/112/158 → 96/96/96、Track 44/62/94 → 58/58/58、
	//   Text 228/236/247 → 236/238/242（近白，只留 6 的 B−R 以免整体发灰）、
	//   Muted 143/163/191 → 160/164/172（原来的蓝灰次要文字也收掉大部分蓝偏）。
	// ⚠️ 语义色**一个数值都没动**：Accent 77/163/255、AccentText 8/18/31、AccentHover 111/180/255、
	// AccentPressed 58/140/230、AccentBlue 116/168/232、Danger 240/88/78、Ok 53/196/107、Warn 232/179/57
	// 全部原值（蓝/红/绿/黄/棕的色相与鲜艳度不受中性层影响）；ModePalette 五个模式色也未动。
	// 层次关系（WCAG，本文件 ContrastRatio 同公式；相邻级差均不低于本轮去蓝之前）：
	// Window↔Surface 1.309（改前 1.316）、Surface↔SurfaceRaised 1.220（1.208）、Input↔Surface 1.343（1.358）、
	// ButtonFace/Surface 1.378（1.279）、ButtonFaceHover/ButtonFace 1.169（1.201）、
	// ButtonFaceSelected/ButtonFace 1.457（1.365）、ButtonFace/Window 1.805（1.683）；
	// Text 在 Window 上 16.62、在 Surface 上 12.69、在 SurfaceRaised 上 10.40、
	// 在 ButtonFace 上 9.21、在 ButtonFaceSelected 上 6.32（全部 ≥4.5 AA）；
	// Muted 在 Surface 上 5.90、在 SurfaceRaised 上 4.83、在 Track 上 4.55、在 Window 上 7.72（AA）。
	// 2026-10-09（紧急修复 + 背景深度回调，仅夜间；用户原话「文字消失了 按钮不能点击了 背景颜色太深了
	// 你直接和它一样深度就行」）：⑥ 把中性层压到近黑（Window 14/14/14、Input 10/10/10）——用户反馈"太深了"，
	// 要求**直接和 G-Helper 一样深**。已取 G-Helper 上游原值核对（seerge/g-helper app/UI/RForm.cs InitColors）：
	//   formBack 28/28/28（页面底）、buttonSecond 36、buttonMain 46（按钮面）、borderMain 55、foreMain 240。
	// 本轮据此把**中性层抬到与 G-Helper 同一深度**，并刻意只抬"底/卡片/输入/边/滑轨"这一层，
	// **按钮面那三档 62/72/86 一档都不动** —— 因为⑤ 验证过的"按钮比背景更浅（浮起）"正是靠它们承载，
	// 抬底的同时若也抬按钮，ButtonFace/Window 的级差会从 1.805 掉到 1.33 以下，"浮起"就没了（B 与 C 的平衡点）。
	//   Window 14/14/14 → **28/28/28**（= G-Helper formBack 同值，页面底最暗）
	//   Surface 40/40/40 → 40/40/40（卡片，不变）
	//   SurfaceRaised 54/54/54 → 54/54/54（不变）
	//   Input 10/10/10 → **20/20/20**（仍比页面底与卡片都暗 = 凹陷；不再压近黑）
	//   Border 72/72/72 → **56/56/56**（≈ G-Helper borderMain 55；随底下移，保持"能看到的那条边"）
	//   BorderStrong 96/96/96 → **74/74/74**（输入类描边，仍比 Border 强一档）
	//   Track 58/58/58 → **46/46/46**（= G-Helper buttonMain，滑轨/滑槽）
	//   ButtonFace/Hover/Selected 62/72/86 **不动**（浮起感、选中更浅的级差 1.169/1.457 原样保留）
	//   Text 236/238/242 → **240/240/240**（= G-Helper foreMain；B−R 归零，中性层彻底去蓝）
	//   Muted 160/164/172 → **158/158/158**（B−R 归零）
	// ⚠️ 语义色**一个数值都没动**：Accent 77/163/255、AccentText 8/18/31、AccentHover 111/180/255、
	// AccentPressed 58/140/230、AccentBlue 116/168/232、Danger 240/88/78、Ok 53/196/107、Warn 232/179/57
	// 全部原值；ModePalette 五个模式色也未动（静音绿/平衡蓝/静音狂暴浅红/高能狂暴大红/自定义浅蓝）。
	// 层次关系（WCAG，本文件 ContrastRatio 同公式；页面底最暗 → 卡片 → 按钮面更浅 → 选中更浅；Input 比卡片暗）：
	// Window↔Surface 1.156、Surface↔SurfaceRaised 1.220、Input↔Surface 1.25、
	// ButtonFace/Surface 1.378、ButtonFaceHover/ButtonFace 1.169、ButtonFaceSelected/ButtonFace 1.457、
	// ButtonFace/Window 1.593、SurfaceRaised/Window 1.41、Track/Window 1.255；
	// Text 在 Window 上 14.95、Surface 12.94、SurfaceRaised 10.60、Input 16.17、ButtonFace 9.39、
	// ButtonFaceSelected 6.44（全部 ≥4.5 AA）；Muted 在 Window 6.36、Surface 5.50、SurfaceRaised 4.51、
	// Track 5.07（AA）。
	private static readonly Palette NightPalette = new Palette(Color.FromArgb(28, 28, 28), Color.FromArgb(40, 40, 40), Color.FromArgb(54, 54, 54), Color.FromArgb(20, 20, 20), Color.FromArgb(62, 62, 62), Color.FromArgb(72, 72, 72), Color.FromArgb(86, 86, 86), Color.FromArgb(56, 56, 56), Color.FromArgb(98, 98, 98), Color.FromArgb(82, 82, 82), Color.FromArgb(240, 240, 240), Color.FromArgb(158, 158, 158), Color.FromArgb(77, 163, 255), Color.FromArgb(8, 18, 31), Color.FromArgb(111, 180, 255), Color.FromArgb(58, 140, 230), Color.FromArgb(116, 168, 232), Color.FromArgb(240, 88, 78), Color.FromArgb(53, 196, 107), Color.FromArgb(232, 179, 57), Color.FromArgb(46, 46, 46));

	// 2026-10-09（0.291 beta22，日间同构升级；用户原话「浅色的是不是还没升级」+
	// 《UI样式规范》§9 第 3 题选 A＝日间与夜间同构）：日间**从「几乎没有层次」升级为与夜间同构的一版**。
	// 改前日间的四个硬伤（规范 §2.4 实测）：Input = Surface = 纯白（1.000 ⇒ 没有"凹陷"）、
	// ButtonFace/Hover/Selected 三档全等 233/239/246（1.000 ⇒ 没有悬停/选中底）、
	// Window↔Surface 只 1.094（低于 §3.2 下限 1.15）、BorderStrong = Border = ButtonEdge（输入族"强一档"的边不存在）。
	// 本轮**只改中性层与语义边的数值**（语义色 Accent/Danger/Ok/Warn/AccentBlue 与五个模式色一个都没动）：
	//   Window          242,245,249 → 233,238,244（页面底：拉开与白卡片的级差 1.094 → 1.167）
	//   Surface         255,255,255 **不动**（卡片＝纯白，日间的最亮层，身份不变）
	//   SurfaceRaised   233,239,246 **不动**（1.158，已达标）
	//   Input           255,255,255 → **224,232,241**（比白卡片明显更暗＝凹陷语义；Input↔Surface 1.000 → 1.237、
	//                   Input 比 Window 再暗 1.060，与夜间「Input 比 Window 暗 1.081」同向）
	//   ButtonFace      233,239,246 **不动**（与卡片 1.158；浅底上不可能比纯白卡片更亮，故日间的"浮起"＝
	//                   比卡片**深一档的浅灰面** + 一圈硬边，这是浅色主题唯一可行的"浮起"表达）
	//   ButtonFaceHover 233,239,246 → **218,227,238**（改前 1.000 ⇒ 悬停完全无反馈；现在 1.120）
	//   ButtonFaceSelected 233,239,246 → **209,219,233**（改前 1.000；现在对 ButtonFace 1.208）
	//   Border          201,213,227 **不动**（对 Surface 1.488）
	//
	// 2026-10-09（0.291 beta22 补丁；用户原话「浅色模式按钮快淡到看不见了」「除了性能模式都不要浅填充」）：
	// ①**日间未选中按钮面抬不动就压深一档**（"淡到看不见"的根因是它跟白卡片只差 1.158、跟页面底几乎同色）：
	//     ButtonFace        233,239,246 → **217,224,234**（对 Surface **1.158 → 1.329**、对 Window 1.008 → 1.139）
	//     ButtonFaceHover   218,227,238 → **201,210,222**（对 ButtonFace 1.120 → **1.149**，悬停仍明显）
	//     ButtonFaceSelected 209,219,233 → **186,197,211**（对 ButtonFace 1.208 → **1.315**，下拉选中行仍另起一档）
	//     ButtonEdge        176,191,208 → **168,183,201**（未选中按钮那圈 1px 硬边；对 ButtonFace **1.537**，
	//                       而 Border 201,213,227 对新 ButtonFace 只有 1.12 ⇒ RButton 日间也改用它画未选中边）
	//    ⚠️ Surface(纯白) / SurfaceRaised / Border / BorderStrong / Input / Track / Text / Muted 与全部语义色
	//    **一个都没动**；夜间 Palette **一个字节都没动**（夜间 ButtonFace 62 对 Surface 40 已是 1.378，够用）。
	// ②"浅填充只留给性能模式"（用户口径「除了性能模式都不要浅填充」）：**非性能模式组**（显卡行/刷新率/
	//    电池三档/核显省电/MiniLED·FHD·HDR）选中时底＝**与未选中同级的中性面**（夜 ButtonFace 62、日
	//    ButtonFace 217,224,234，即"不加任何浅填充"），选中完全由那圈 2 设备像素描边表达；
	//    **性能模式五行**才给模式色淡填充（夜 12% 深底混色、日朝白 60% 基色混 15%，见 ModeActivatedFill），
	//    判据是代码标志 SelectionUsesModeFill（只由 SetPerfModeAccent 置位），不做任何文字匹配。
	//   BorderStrong    201,213,227 → **158,175,196**（输入族描边，对 Input 1.810；恢复"强一档"语义）
	//   ButtonEdge      201,213,227 → **176,191,208**（未选中按钮那圈 1px 硬边，对 ButtonFace 1.619，
	//                   与夜间 ButtonEdge/ButtonFace 1.369 同向）
	//   Muted           90,107,128 → **78,95,117**（修掉 §3.3 的日间唯一 AA 不达标：Muted 在 Track 上 4.09 ❌
	//                   → 4.895 ✅；其余底也全部 ≥4.5，最低是 ButtonFaceSelected 上 4.668）
	//   Track 215,224,235、Text 22,32,46 **不动**。
	// 层次关系（WCAG，与本文件 ContrastRatio 同公式；日间方向＝越深越"沉"）：
	// Surface↔Window 1.167、Surface↔SurfaceRaised 1.158、Surface↔Input 1.237、Input↔Window 1.060、
	// Surface↔ButtonFace 1.158、ButtonFace↔Hover 1.120、ButtonFace↔Selected 1.208、
	// ButtonEdge↔ButtonFace 1.619、BorderStrong↔Input 1.810、Track↔Window 1.143；
	// Text 14.06~16.40、Muted 4.67~6.53（全部 ≥4.5 AA）。
	private static readonly Palette DayPalette = new Palette(Color.FromArgb(233, 238, 244), Color.FromArgb(255, 255, 255), Color.FromArgb(233, 239, 246), Color.FromArgb(224, 232, 241), Color.FromArgb(217, 224, 234), Color.FromArgb(201, 210, 222), Color.FromArgb(186, 197, 211), Color.FromArgb(201, 213, 227), Color.FromArgb(112, 130, 155), Color.FromArgb(168, 183, 201), Color.FromArgb(22, 32, 46), Color.FromArgb(78, 95, 117), Color.FromArgb(31, 111, 224), Color.White, Color.FromArgb(26, 95, 196), Color.FromArgb(20, 80, 159), Color.FromArgb(0, 102, 204), Color.FromArgb(190, 30, 45), Color.FromArgb(22, 120, 63), Color.FromArgb(139, 95, 15), Color.FromArgb(215, 224, 235));

	/// <summary>2026-10-08：一个性能模式的「高亮底色 + 该底色上的文字色」。</summary>
	internal readonly record struct ModeAccent(Color Background, Color Foreground);

	private readonly record struct ModePalette(ModeAccent Silent, ModeAccent Balanced, ModeAccent SilentTurbo, ModeAccent Turbo, ModeAccent Custom);

	// 2026-10-08（用户要求：性能模式高亮按模式上色）：静音=绿 / 平衡=蓝（沿用既有强调色，视觉不变）/
	// 静音狂暴=浅红 / 高能狂暴（视觉 1 的非静音子模式）=大红 / 自定义=浅蓝。
	// 只用于「当前实际模式」的高亮；「待生效（重启后生效）」仍走 UiVisualStyle.Warn 棕色（见 SettingsForm
	// 的 GPU 模式行）与本次改动无关。文字色按底色明度取深/浅：夜间的浅红、浅蓝用深字（浅色配深字），
	// 日间的绿、大红、蓝用白字；各组合 WCAG 对比度 ≥ 4.7（本项目 ContrastRatio() 同公式）。
	private static readonly ModePalette NightModePalette = new ModePalette(new ModeAccent(Color.FromArgb(52, 168, 106), Color.FromArgb(7, 22, 15)), new ModeAccent(NightPalette.Accent, NightPalette.AccentText), new ModeAccent(Color.FromArgb(230, 128, 120), Color.FromArgb(40, 10, 8)), new ModeAccent(Color.FromArgb(206, 44, 40), Color.FromArgb(255, 255, 255)), new ModeAccent(Color.FromArgb(122, 200, 236), Color.FromArgb(8, 24, 36)));

	private static readonly ModePalette DayModePalette = new ModePalette(new ModeAccent(Color.FromArgb(24, 132, 76), Color.FromArgb(255, 255, 255)), new ModeAccent(DayPalette.Accent, DayPalette.AccentText), new ModeAccent(Color.FromArgb(226, 116, 106), Color.FromArgb(56, 12, 10)), new ModeAccent(Color.FromArgb(192, 38, 38), Color.FromArgb(255, 255, 255)), new ModeAccent(Color.FromArgb(118, 192, 232), Color.FromArgb(12, 34, 52)));

	/// <summary>2026-10-08：当前主题（日/夜）下五个性能模式的高亮配色。</summary>
	internal static ModeAccent ModeSilentAccent => CurrentModePalette.Silent;

	internal static ModeAccent ModeBalancedAccent => CurrentModePalette.Balanced;

	internal static ModeAccent ModeSilentTurboAccent => CurrentModePalette.SilentTurbo;

	internal static ModeAccent ModeTurboAccent => CurrentModePalette.Turbo;

	internal static ModeAccent ModeCustomAccent => CurrentModePalette.Custom;

	private static ModePalette CurrentModePalette
	{
		get
		{
			if (!IsNightMode)
			{
				return DayModePalette;
			}
			return NightModePalette;
		}
	}

	private static bool? _auditNightMode;

	private static readonly ConditionalWeakTable<Control, object> _footerGlyphs = new ConditionalWeakTable<Control, object>();
	// 2026-10-12 r6：窗体标题栏图标（左上角）用哪枚字形 —— 与底栏/磁贴按钮同一套 Kind。
	private static readonly ConditionalWeakTable<Form, object> _windowGlyphs = new ConditionalWeakTable<Form, object>();

	private static readonly ConditionalWeakTable<Control, object> _footerGlyphColors = new ConditionalWeakTable<Control, object>();

	private static readonly ConditionalWeakTable<Control, object> _themedControls = new ConditionalWeakTable<Control, object>();

	private static readonly ConditionalWeakTable<Control, object> _themedNumeric = new ConditionalWeakTable<Control, object>();

	private static Palette Current
	{
		get
		{
			Palette palette = (IsNightMode ? NightPalette : DayPalette);
			Color customAccent = ParseCustomColor(AppConfig.GetString(AccentColorConfigKey));
			if (customAccent != Color.Empty)
			{
				Color accent = customAccent;
				palette = palette with
				{
					Accent = accent,
					AccentHover = Mix(accent, Color.White, IsNightMode ? 0.18f : 0.12f),
					AccentPressed = Mix(accent, Color.Black, IsNightMode ? 0.12f : 0.18f),
					AccentText = ContrastTextFor(accent)
				};
			}
			Color customBackground = ParseCustomColor(AppConfig.GetString(BackgroundColorConfigKey));
			if (customBackground != Color.Empty && string.IsNullOrWhiteSpace(AppConfig.GetString(BackgroundImageConfigKey)))
			{
				palette = palette with
				{
					Window = customBackground
				};
			}
			return palette;
		}
	}

	public static Color Window => Current.Window;

	public static Color Surface => Current.Surface;

	public static Color SurfaceRaised => Current.SurfaceRaised;

	public static Color Input => Current.Input;

	/// <summary>2026-10-09（UI 打磨⑤，仅夜间）：未选中按钮面 —— 比所在卡片更浅（浮起）。日间 = SurfaceRaised。</summary>
	internal static Color ButtonFace => Current.ButtonFace;

	/// <summary>2026-10-09（UI 打磨⑤，仅夜间）：悬停按钮面 —— 比 <see cref="ButtonFace" /> 再浅一档。</summary>
	internal static Color ButtonFaceHover => Current.ButtonFaceHover;

	/// <summary>2026-10-09（UI 打磨⑦，仅夜间）：选中按钮底 —— 最浅一档（也是 <see cref="ModeActivatedFill" /> 的基色）。</summary>
	internal static Color ButtonFaceSelected => Current.ButtonFaceSelected;

	/// <summary>
	/// 2026-10-09（0.291 beta22，下拉框自制样式统一；用户原话「下拉框好像没改过来 还是旧版的」）：
	/// 下拉框**箭头格**的三档底色的唯一来源。
	///
	/// 改前（⑦）箭头格是「与输入区不同底」的独立一格：夜间 <c>ButtonFace 62</c> 画在 <c>Input 20</c> 上
	/// （Δ42、WCAG 1.378），日间更是各画各的 —— 这正是"看着像老式 Windows 下拉按钮"的来源。
	/// 本轮取消"箭头格比输入区亮一档"：<b>箭头格与输入区同底</b>，只靠一条极淡的竖分隔线区分；
	/// 悬停/展开时才轻微变一档（夜间＝提亮，日间＝略深，各自朝"更有反馈"的方向），见下：
	///   <see cref="ComboCellHover" />  ：夜间 Mix(Input→白, 0.12)＝48（对 Input 1.36）；日间 Mix(Input→BorderStrong, 0.30)（1.18）
	///   <see cref="ComboCellActive" /> ：夜间 ButtonFace 62（展开中＝浮起为按钮面）；日间 Mix(Input→BorderStrong, 0.45)（1.29）
	///   <see cref="ComboSeparator" />  ：夜间 Mix(Input→BorderStrong, 0.42)＝43（1.29，远淡于改前的 BorderStrong 74＝2.08）；
	///                                    日间 Mix(Input→BorderStrong, 0.28)（1.15）
	/// 全部只用于 RComboBox 的自绘，不参与任何布局/命中区域计算。
	/// </summary>
	internal static Color ComboCellFill => Input;

	/// <summary>2026-10-09（0.291 beta22）：下拉框箭头格悬停底（夜间提亮、日间略深，均为"轻微"一档）。
	/// 2026-10-09（0.291 beta22 补丁，用户原话「箭头左边还有奇怪深色矩形也删除」）：日间幅度 0.30→**0.14**、
	/// 夜间 0.12→**0.08** —— 鼠标停在箭头格上时那一格的变化必须"几乎看不出来"，否则在放大/截图上就是一
	/// 块突兀的深色方块。静止态本来就与输入区**完全同底**（见 <see cref="ComboCellFill" />）。</summary>
	internal static Color ComboCellHover => IsNightMode ? Mix(Input, Color.White, 0.08f) : Mix(Input, BorderStrong, 0.14f);

	/// <summary>2026-10-09（0.291 beta22）：下拉框箭头格在"展开中/按下"时的底 —— 比悬停再明确一档。
	/// 2026-10-09（0.291 beta22 补丁）：日间 0.45→**0.24**（同上：只作反馈、不做色块）。</summary>
	internal static Color ComboCellActive => IsNightMode ? ButtonFace : Mix(Input, BorderStrong, 0.24f);

	/// <summary>2026-10-09（0.291 beta22）：输入区与箭头格之间那条"极淡"竖分隔线 —— 用 Input 与 BorderStrong 的中间色。</summary>
	internal static Color ComboSeparator => Mix(Input, BorderStrong, IsNightMode ? 0.42f : 0.28f);

	/// <summary>
	/// 2026-10-09（UI 打磨⑤，仅夜间）：未选中按钮面的「1px 顶部高光」色 = ButtonFace 朝白提亮 16%。
	/// 只由 RButton.DrawTopHighlight 在夜间、未选中、有按钮面底色时画一条线（不填任何矩形）。
	/// </summary>
	internal static Color ButtonFaceHighlight => Mix(ButtonFace, Color.White, 0.16f);

	/// <summary>
	/// 2026-10-09（UI 打磨⑤，仅夜间）：该颜色是否属于本轮新增的「按钮面」家族（含悬停/选中）。
	/// 供 RButton 判断「这颗按钮是不是真的有浮起的面」—— 继承父色（＝页面底）的按钮不会被画上一条孤立亮线。
	/// </summary>
	internal static bool IsButtonFace(Color color)
	{
		return SameRgb(color, ButtonFace) || SameRgb(color, ButtonFaceHover) || SameRgb(color, ButtonFaceSelected);
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑤ 仅夜间；0.291 beta22 起**日间同构**）：选中态那圈 2 设备像素描边的颜色。
	/// <list type="bullet">
	/// <item>**夜间**（0.291 beta22 补丁改）：**该强调色原值（全强度）**，不再朝白提亮 12%。
	/// 用户口径「我觉得反而深色应该是这种深度」⇒ 夜间的"分量感"由**更实、更饱和的边**承载，
	/// 而不是靠提亮。⚠️ 刻意**不**改成像日间那样朝黑压深：夜间底是深灰（ButtonFace 62/62/62），
	/// 边一压深就贴住底（Mix(accent,黑,0.30) 对 62 底只有 1.18 ⇒ 反而看不见）。
	/// 全强度 Accent 77/163/255 对 62 底 **4.07**、对未选中那圈 ButtonEdge 82 是 2.98、色相不变。</item>
	/// <item>**日间**（0.291 beta22 补丁改）：该强调色朝**白**提亮 <see cref="DayOutlineLighten" /> ＝"浅档"，
	/// 并对**浅色模式色**（静音狂暴浅红 226,116,106、自定义浅蓝 118,192,232）按
	/// <see cref="DayOutlineMaxLuminance" /> 设亮度上限 ⇒ 边在浅填充上仍有 ≥3:1（WCAG 1.4.11 非文字下限）。
	/// 用户口径「颜色太深了 原先的颜色就还可以」"整体调浅"就落在这一行（改前是朝黑 30%）。</item>
	/// </list>
	/// ⚠️ 只被 <c>RButton.DrawActivatedOutline</c> 使用（性能模式五行与显卡行「待生效＝棕」）。
	/// </summary>
	internal static Color SelectionOutlineColor(Color accent)
	{
		if (accent.A == 0)
		{
			return accent;
		}
		return IsNightMode ? accent : DaySelectionOutline(accent);
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：**日间**选中态描边的"浅档"取色 —— 唯一入口（非性能模式组与
	/// 性能模式五行共用同一套规则，只是传入的强调色不同：前者传 <see cref="Accent" />、后者传各自模式色），
	/// 于是"日间所有按钮组的边是同一档浅"这条口径由代码结构保证，不会出现某一行忘记改的情况。
	///
	/// 规则两步：
	/// ① 朝白提亮 <see cref="DayOutlineLighten" />（0.15）＝"轻盈"档；
	/// ② 若提亮后的**相对亮度**仍高于 <see cref="DayOutlineMaxLuminance" />（0.24），按 0.02 步长朝黑混
	///    到该上限为止 —— 只压明度、**色相与饱和度关系不变**（浅红→红、浅蓝→蓝、棕 R&gt;G&gt;B）。
	///    为什么必须有这一步：日间的选中底是**浅**填充，而「静音狂暴浅红」「自定义浅蓝」本身就接近浅底，
	///    单纯提亮会让边糊进填充里（对自身填充只有 1.3~2.0）⇒ 用户会看到"只有浅底、没有边"。
	/// 结果（候选 A，96 DPI 实测值见报告）：非性能蓝边 60,122,211（对中性底 217,224,234 = **3.21**）；
	/// 静音 54,138,95；平衡=蓝边；静音狂暴 175,104,97；高能狂暴 201,71,71；自定义 86,125,146；
	/// 待生效棕 156,119,51 —— 边/填充 3.15~3.65，**全部 ≥3:1**（WCAG 1.4.11 非文字下限）。
	/// </summary>
	private static Color DaySelectionOutline(Color accent)
	{
		if (accent.A == 0)
		{
			return accent;
		}
		Color lifted = Mix(accent, Color.White, DayOutlineLighten);
		if (RelativeLuminance(lifted) <= DayOutlineMaxLuminance)
		{
			return lifted;
		}
		for (float amount = 0.02f; amount <= 0.9f; amount += 0.02f)
		{
			Color candidate = Mix(lifted, Color.Black, amount);
			if (RelativeLuminance(candidate) <= DayOutlineMaxLuminance)
			{
				return candidate;
			}
		}
		return Mix(lifted, Color.Black, 0.9f);
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁④；用户原话「B中间档位吧」）：**日间**选中态描边的"提亮档"＝
	/// **0.22（B 中间档，本轮落地）**。三档实测（非性能组，对日间中性底 217,224,234）：
	/// A＝0.15/上限 0.20 ⇒ 边 60,122,211（**3.21**，达标但用户嫌深）、
	/// **B 中＝0.22/上限 0.26 ⇒ 边 78,136,222（≈2.7，本轮落地）**、
	/// B 浅＝0.28/上限 0.30 ⇒ 边 92,148,228（2.32）。
	/// 三档只差这两个常量，随时可切；文字对比三档都远高于 AA（≥11）。
	/// </summary>
	private const float DayOutlineLighten = 0.22f;

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁④）：日间选中态描边的亮度上限＝ **0.26**（配合 B 中间档；
	/// 两档必须同向改，否则上限会把提亮的部分又压回去）。实测最差一档（静音绿 / 平衡蓝）仍 ≈2.6~2.7，
	/// 低于 WCAG 1.4.11 的 3:1 非文字下限 —— 这是用户连续两次要"更浅"的直接代价，**如实报告**；
	/// 文字对比不受影响（≥11，AA）。
	/// </summary>
	private const double DayOutlineMaxLuminance = 0.26;

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：**日间**选中底 ＝ <see cref="ButtonFace" /> 先朝白提亮
	/// <see cref="DayFillWhite" />（0.60）再混入该模式色 <see cref="DayModeFillAccent" />（0.15）。
	/// 为什么基色要朝**白**走：日间未选中按钮面本身已是浅灰（233,239,246），选中若往"更深"走就会发沉
	/// （用户口径「颜色太深了」），往"更亮"走才读得出"浮起/活动"——白卡浮在灰底上，这是浅色主题唯一
	/// 可行的表达；同时底越浅、那圈浅边越容易保住 ≥3:1。
	/// </summary>
	private const float DayFillWhite = 0.72f;

	/// <summary>2026-10-09（0.291 beta22 补丁）：日间选中底里混入的模式色比例（保留色相，克制不刺眼）。
	/// 2026-10-09（0.291 beta22 补丁②）：0.15→**0.12**（配合用户"高亮颜色还可以再浅一点"，填充再提亮一档）。</summary>
	private const float DayModeFillAccent = 0.12f;

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：WCAG 相对亮度 —— 与 <see cref="ContrastRatio" /> 内的局部函数
	/// <c>Luminance</c>/<c>Linear</c> **同公式**（逐行相同，只是抽到文件级供
	/// <see cref="DaySelectionOutline" /> 的"亮度设限"使用）；<see cref="ContrastRatio" /> 本身一字未改。
	/// </summary>
	private static double RelativeLuminance(Color color)
	{
		return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
	}

	private static double LinearChannel(byte value)
	{
		double num = (double)(int)value / 255.0;
		if (!(num <= 0.04045))
		{
			return Math.Pow((num + 0.055) / 1.055, 2.4);
		}
		return num / 12.92;
	}

	private static Color Mix(Color from, Color to, float amount)
	{
		return Color.FromArgb(from.A, (int)Math.Round((float)(int)from.R + (float)(to.R - from.R) * amount), (int)Math.Round((float)(int)from.G + (float)(to.G - from.G) * amount), (int)Math.Round((float)(int)from.B + (float)(to.B - from.B) * amount));
	}

	public static Color Border => Current.Border;

	/// <summary>2026-10-09（UI 打磨②，仅夜间）：比 Border 强一档的边色，用于输入类控件的 1px 圆角描边。</summary>
	internal static Color BorderStrong => Current.BorderStrong;

	/// <summary>
	/// 2026-10-09（UI 打磨⑦，仅夜间）：未选中按钮那一圈 1px **硬边**的专用色（夜 82/82/82）。
	/// 只在夜间被 <c>RButton</c> 读取；日间取日间 Border 同值（201/213/227），故日间读到也逐像素不变。
	/// </summary>
	internal static Color ButtonEdge => Current.ButtonEdge;

	/// <summary>
	/// 2026-10-09（UI 打磨⑦，仅夜间）：统一「小圆角矩形」的目标半径 —— **4 逻辑像素**（96 DPI 下 4 设备像素）。
	/// 为什么写 8：仓库既有约定是「设备半径 = BorderRadius × Dpi / 192」（RButton.OnPaint 的 num3），
	/// 96 DPI 下正好减半，故 8 单位 = 4px；这也与 SettingsForm.CreateThemeButton 的
	/// <c>ApplyRoundedRegion(button, scale(8))</c> 逐像素一致（那里传的是**直径** 8 ⇒ 圆角半径同样是 4px）。
	/// 于是夜间「按钮 / 输入框 / 数字框 / 下拉框」四种圆角半径归一为 4px，观感接近 G-Helper 的方正按键。
	/// </summary>
	internal const int SmallCornerUnit = 8;

	/// <summary>2026-10-09（UI 打磨⑦，仅夜间）：<see cref="CornerRadiusDevicePx" /> 用的逻辑像素半径。</summary>
	internal const int SmallCornerLogical = 4;

	/// <summary>
	/// 2026-10-09（UI 打磨⑦ 仅夜间；0.291 beta22 起**日间同构**）：把 <c>RButton.BorderRadius</c>
	/// （单位 = 缩放前的值，设备半径 = 值 × Dpi/192）统一收紧到小圆角。<b>只收不放</b> ——
	/// 比 8 更小的配置值（2/4/5：页脚幽灵按钮、对话框按钮、实验角标）一律保持原值，
	/// 否则会把本来更小的胶囊改圆，也会在小控件上撞到「半径 &gt; 行高一半」的异常路径。
	/// ⚠️ 0.291 beta22：日间不再"原样返回" —— 用户本轮要求日间与夜间同构（《UI样式规范》§9 第 3 题选 A），
	/// 而 §4.1 的按钮半径已定为 R4＝4 逻辑像素（代码 8），故日间同样 min(配置值, 8)：
	/// 日间原先写 12/16/10 的按钮从 6/8/5px 收到 4px，与夜间一致（只收不放 ⇒ 不会有按钮变圆）。
	/// </summary>
	internal static int EffectiveCornerRadius(int configuredUnit)
	{
		if (configuredUnit <= 0)
		{
			return configuredUnit;
		}
		return Math.Min(configuredUnit, SmallCornerUnit);
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑦ 仅夜间；0.291 beta22 起**日间同构**）：输入类控件
	/// （RTextBox / RNumericUpDown / RComboBox / RComboDisplay）的圆角半径，单位 = **设备像素**
	/// （已按 DPI 缩放并取整到整数 ⇒ 角落不会落在半像素上）。
	/// 夜间与日间统一为 <see cref="SmallCornerLogical" />＝4 逻辑像素（96 DPI 下 4px、150% 下 6px），
	/// 与按钮族/下拉框同一个圆角 —— 改前日间是既有的 <c>Dpi/192×12</c>（96 DPI 下 6px），
	/// 与日间 RButton 的 6px 一致但与夜间 4px 不一致，属 §4.1「同一界面两种半径」的来源之一。
	/// </summary>
	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁②；用户原话「下拉按钮还是方形的 … 我不是说改下拉菜单,是改按钮」）：
	/// **下拉族专用**圆角半径（独立常量，不动输入族/按钮族的 <see cref="SmallCornerLogical" />＝4）。
	/// 2026-10-09（补丁④；用户原话「下拉框圆角不明显可以加大有点」）：**6 → 8 逻辑像素**。
	/// 100%→8、125%→10、150%→12、175%→**14**、200%→16 设备像素；按**控件自己的 DPI** 取整。
	/// 下拉高 40 设备像素时 14/40 ＝ **35%**，仍小于"胶囊"的 50% 上限（`FlatRoundedPath` 内部还会把半径
	/// 夹到 `min(宽,高)/2`，任何 DPI 下都不可能变成胶囊）。
	/// </summary>
	internal const int ComboCornerLogical = 8;

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑥；用户原话「顺便把这个10分钟的高度改小一点」）：
	/// **下拉族的统一高度**＝**23 逻辑像素**（175% DPI → 40 设备像素）。
	/// 依据：175% 下实测同族下拉都是 `size=…x40`（屏幕校色 / 色彩增强 / 刷新率），只有灯效「10 分钟」那颗
	/// `comboLightingSleepTimer` 因为**继承了更大的父面板字体**而自然高到 48。`RComboBox.CalibrateItemHeight`
	/// 会把 `Dock=None` 的下拉高度钉到这个值 ⇒ 全族一致（`Dock=Fill/Top` 的由父容器决定高度，不碰）。
	/// </summary>
	internal const int ComboHeightLogical = 23;

	/// <summary>2026-10-09（0.291 beta22 补丁②）：下拉族圆角（设备像素，按控件 DPI 取整）。</summary>
	internal static int ComboCornerDevicePx(Control control)
	{
		return (int)Math.Round(ComboCornerLogical * UiDpi.Paint(control) / 96f);
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁⑤；用户原话「我发现是圆角边框太细而且有锯齿导致的」）：
	/// **下拉族描边线宽** —— 由"恒定 1 设备像素"改为与按钮族选中态**同一档 2 逻辑像素**
	/// （整数设备像素：100%→2、125%→3、150%→3、175%→4、200%→4）。
	/// 为什么必须加粗：1px 细边在 175% 屏上只有 1 物理像素，圆角处再被二值裁剪啃掉一半 AA 羽化 ⇒
	/// 观感就是"方按钮"（用户自己定位到的根因①）。上限刻意保持 2 逻辑像素（"克制"、不是 3px+），
	/// 与 <see cref="ModeOutlineWidth" /> 的按钮族选中边同值 ⇒ 家族一致。
	/// 按**控件自己的 DPI** 算（不取绘制 DC 的 DpiX），与圆角/箭头格同源。
	/// 2026-10-09（P0.5）：宽度换算已收敛到唯一工厂
	/// <see cref="OutlinePen(Control, Color, float, float, bool)" />，本属性只给出逻辑线宽
	/// （<see cref="ModeOutlineLogical" />＝2）；返回值与改动前逐 DPI 相同（见工厂的对照表）。
	/// </summary>
	internal static float ComboOutlineWidth(Control control)
	{
		return Math.Max(2f, (float)Math.Round(ModeOutlineLogical * UiDpi.Paint(control) / 96f, MidpointRounding.AwayFromZero));
	}

	internal static int CornerRadiusDevicePx(Graphics graphics)
	{
		return (int)Math.Round(SmallCornerLogical * UiDpi.Paint(graphics) / 96f);
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：与 <see cref="CornerRadiusDevicePx(Graphics)" /> 同值，但**按控件
	/// 自己的 DPI**（<c>Control.DeviceDpi</c>）算，而不是按绘制 DC 的 <c>Graphics.DpiX</c>。
	/// 为什么需要：`Graphics.FromHdc(窗口 DC)` 上的 <c>DpiX</c> 是**设备能力里的 LOGPIXELSX**，
	/// 在 PerMonitorV2 / "系统 DPI ≠ 显示器 DPI" 的机器上可能回 96 —— 于是 4 逻辑像素被算成
	/// 4 **设备**像素（175% 屏上只等于约 2.3 逻辑像素）⇒ 圆角看上去就是**直角**，这正是用户
	/// 「我不要这种直角样式的改成克制圆角」第二次反馈的机制之一。控件自己的 <c>DeviceDpi</c> 是可靠的。
	/// </summary>
	internal static int CornerRadiusDevicePx(Control control)
	{
		return (int)Math.Round(SmallCornerLogical * UiDpi.Paint(control) / 96f);
	}

	public static Color Text => Current.Text;

	public static Color Muted => Current.Muted;

	public static Color Accent => Current.Accent;

	internal static Color AccentText => Current.AccentText;

	private static Color AccentHover => Current.AccentHover;

	private static Color AccentPressed => Current.AccentPressed;

	public static Color AccentBlue => Current.AccentBlue;

	public static Color Danger => Current.Danger;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主：状态行成功色）：⭐ 成功色**浅/深分别取色**（⛔ 不在面板里写死）：
	/// 浅色主题用更深的绿（白底对比度足够），深色主题用更亮的绿（深底可读）✅
	/// </summary>
	public static Color Success => IsNightMode
		? Color.FromArgb(96, 214, 132)
		: Color.FromArgb(18, 118, 64);

	public static Color Ok => Current.Ok;

	public static Color Warn => Current.Warn;

	public static Color Track => Current.Track;

	/// <summary>
	/// 2026-10-09（UI 打磨①：消除"整块异色矩形"）：取某个控件**所在位置实际被画出来的底色**。
	///
	/// 自绘控件若在 OnPaint 里把整个 ClientRectangle 填成"自己的底色"，而这个颜色与父容器
	/// 真正画出来的底色差几个数值，控件四周就会露出一圈"假边"（观感是淡淡的框/异色块）。
	/// 本方法沿父链向上取**第一个实色**容器的 BackColor —— 因为 WinForms 里子控件的绘制
	/// 总是在父容器画完自己的客户区之后叠上去的，"第一个实色父容器的 BackColor"就是该位置
	/// 实际被画出来的底色；填这个颜色，矩形的每条边都与周围逐分量相等，因此不可见。
	///
	/// 与 RCheckBox.SurfaceColor() 的旧实现逐字等价（那里已按同一思路修好），此处抽出共用，
	/// 避免滑块/数字框再各写一份。
	/// </summary>
	internal static Color ContextBackColor(Control? control)
	{
		// 2026-10-22（主题背景图）：父容器已经是「透明」（背景图从下面透出来）时，返回透明，
		// 让自定义控件（开关/滑块/数字框…）不再往自己的矩形里填一块实心底色（否则会成为黑方块）。
		// ⚠️ 只看**直接父容器**：父容器是实色时仍按原逻辑取它，保持既有观感。
		if (control?.Parent != null && control.Parent.BackColor.A == 0)
		{
			return Color.Transparent;
		}
		for (Control? parent = control?.Parent; parent != null; parent = parent.Parent)
		{
			Color parentColor = parent.BackColor;
			if (parentColor.A != 0 && parentColor != Color.Empty)
			{
				return parentColor;
			}
		}
		return Window;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨②/②b，仅夜间）：输入类控件（RTextBox / RNumericUpDown / RComboDisplay）的
	/// 圆角描边。圆角半径自⑦起统一走 <see cref="CornerRadiusDevicePx" />（夜间 4 逻辑像素＝"小圆角矩形"；
	/// 日间仍是既有的 Dpi/192×12 ⇒ 96 DPI 下 6px），AntiAlias + GraphicsPath。
	/// ②b 起线宽由 InputOutlineWidth 给出：夜间 2px（DPI 缩放）且 PenAlignment.Inset —— 2px 全部落在客户区内，
	/// 不会被裁掉一半，且整数像素对齐（不跨半像素 ⇒ 不会出现"两列各 50%"的灰边）；
	/// 日间仍是 1px、Center 对齐（与 ② 的日间像素逐点相同）。
	/// 只画线，不填底、不设 Region、不改 Bounds —— 尺寸/布局/命中区域与未加描边时逐像素相同。
	/// </summary>
	internal static void DrawInputOutline(Graphics graphics, Rectangle bounds, Color color, Control? control = null)
	{
		if (bounds.Width <= 4 || bounds.Height <= 4)
		{
			return;
		}
		// 2026-10-09（P0.5）：圆角与线宽都改成"按控件自己的 DPI"（传 null 时才退回绘制 DC 的 DPI）。
		int radius = ((control == null) ? Math.Min(CornerRadiusDevicePx(graphics), Math.Min(bounds.Width, bounds.Height) / 2) : Math.Min(CornerRadiusDevicePx(control), Math.Min(bounds.Width, bounds.Height) / 2));
		System.Drawing.Drawing2D.SmoothingMode smoothingMode = graphics.SmoothingMode;
		graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
		using System.Drawing.Drawing2D.GraphicsPath path = RoundedPath(bounds, radius);
		// 2026-10-09（P0.5）：线宽改走共用工厂 OutlinePen（整数设备像素 + Inset）。
		using Pen pen = ((control == null) ? OutlinePen(graphics, color, InputOutlineLogical) : OutlinePen(control, color, InputOutlineLogical));
		graphics.DrawPath(pen, path);
		graphics.SmoothingMode = smoothingMode;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨②b，仅夜间）：输入类控件的描边线宽（DPI 缩放）。夜间 2px（100% DPI）、
	/// 日间恒为 1px —— 日间与上一轮逐像素相同。
	/// 2026-10-09（P0.5）：**已收敛到唯一工厂** <see cref="OutlinePen(Control, Color, float, float, bool)" />；
	/// 本属性只负责给出"输入族当前的逻辑线宽"（夜间 2、日间 1），宽度换算/取整不再各写一遍。
	/// </summary>
	internal static float InputOutlineLogical => IsNightMode ? 2f : 1f;

	/// <summary>2026-10-09（P0.5）：输入族描边线宽的兼容入口（旧签名保留，供既有调用点读取数值）。</summary>
	internal static float InputOutlineWidth(Graphics graphics)
	{
		return Math.Max(1f, (float)Math.Round(InputOutlineLogical * UiDpi.Paint(graphics) / 96f, MidpointRounding.AwayFromZero));
	}

	/// <summary>
	/// 2026-10-09（UI 反馈②，仅夜间；用户原话「我觉得其他的可以改成蓝色高亮但是不填充浅色」）：
	/// **非性能模式**的选中态底色 —— 仍取 <see cref="ButtonFace" />（62/62/62），即"与未选中同级"。
	/// ⚠️ 本轮只换描边色（见 <see cref="NeutralSelectionOutline" /> 的注释），**底色一个字都没动**：
	/// 用户明确"不填充浅色"，那条浅填充禁令继续生效（浅色来自旧的 ButtonFaceSelected 86，已弃用）。
	/// </summary>
	/// <remarks>⚠️ 本属性现在只被"底"这一项使用；上一轮它同时也是"中性灰分组"的成员之一，那种叫法已废弃。</remarks>
	/// <remarks>⚠️ 2026-10-09（0.291 beta22 补丁）：**两个主题都只用"中性面"、不加任何浅填充**
	/// —— 用户口径「除了性能模式都不要浅填充」。夜＝ ButtonFace 62/62/62、日＝ ButtonFace 217,224,234
	/// （都等于该主题下**未选中**的按钮面 ⇒ 选中与未选中的差别**只**落在那圈 2 设备像素描边上；
	/// 浅填充只留给性能模式五行，见 <see cref="ModeActivatedFill" /> + <c>RButton.SelectionUsesModeFill</c>）。</remarks>
	internal static Color NeutralActivatedFill => ButtonFace;

	/// <summary>
	/// 2026-10-09（UI 反馈②，仅夜间；用户原话「其他的可以改成蓝色高亮但是不填充浅色」）：
	/// **非性能模式**（显卡行非待生效／刷新率／电池三档／核显省电／MiniLED/FHD/HDR）选中态那圈
	/// 2 设备像素描边的颜色 = 既有强调蓝 <see cref="Accent" />（夜间 77/163/255）。
	/// <para>WCAG（本文件 ContrastRatio 同公式）：蓝边 vs 选中底 ButtonFace(62/62/62) = **4.07**、
	/// vs 未选中那圈 ButtonEdge(82/82/82) = 2.98；而 62↔82 只有 1.369 ⇒ 选中与未选中的差别
	/// 完全落在这圈蓝上，且底仍是深灰（**没有任何浅填充**）。对比上一轮的中性灰 Muted 158（3.99）
	/// 反而略高，且 &gt; 3:1（WCAG 1.4.11 非文字对比下限）。</para>
	/// <para>⚠️ 为什么不复用 <see cref="NeutralSelectionOutline" />：那个是上一轮的中性灰
	/// <see cref="Muted" />（158/158/158，对比 3.99）—— 代码保留（可一键回退），但**本轮起不再被
	/// 任何选中态使用**。性能模式五行（各自模式色）与显卡行「待生效＝棕」（Warn）**不受影响**，
	/// 仍走 <see cref="SelectionOutlineColor" /> / 各自语义色。</para>
	/// 日间<b>读</b>本属性（0.291 beta22 补丁起）：日间＝ <see cref="DaySelectionOutline" />(强调蓝) ＝
	/// 提亮 15% 的浅蓝 65,133,229（对日间选中底 209,219,233 的对比 2.63、对极浅底 3.1+）——
	/// 用户口径「颜色太深了」就落在这里（改前日间这一档是 <see cref="Muted" /> 158 或加深 30% 的深蓝）。
	/// </summary>
	internal static Color AccentSelectionOutline => (IsNightMode ? Accent : DaySelectionOutline(Accent));

	/// <summary>
	/// 2026-10-09（UI 反馈①，仅夜间）：**已被 <see cref="AccentSelectionOutline" /> 取代**的
	/// 中性灰描边（<see cref="Muted" /> 158/158/158；对选中底 ButtonFace 的 WCAG 对比 3.99）。
	/// 反馈②起选中态改用强调蓝，本属性**不再有任何调用点**，保留仅为「按对比度/观感回退」留一个旋钮
	/// （回退＝把 <c>RButton.DrawActivatedOutline</c> 里 else 分支的 <c>AccentSelectionOutline</c>
	/// 换回本属性即可，其余几何一字不用动）。日间不读本属性。
	/// </summary>
	internal static Color NeutralSelectionOutline => Muted;

	/// <summary>
	/// 2026-10-09（UI 反馈①-B，仅夜间）：圆角裁剪区（<c>Control.Region</c>）向外让出的**设备像素**数 = 2。
	/// 二值 Region（SetWindowRgn）会把抗锯齿画出来的那条约 1~1.5 设备像素的羽化边整条切掉 ⇒ 圆角处留下
	/// 阶梯（"锯齿"）。外扩 2 设备像素让羽化边完整落在裁剪区内；外扩出来的那一圈由 RButton 先铺成
	/// **父容器在该位置真正画出来的底色**（<see cref="ContextBackColor" />）⇒ 多出来的环不可见。
	/// 2026-10-09（P1）：**日间也读本常量** —— 原来日间传 <c>Dpi/96</c>（1 设备像素），于是同一种按钮
	/// 日间圆角的 AA 羽化仍被啃掉一条、看着比夜间"方"；两主题统一到 2 之后圆角平滑度一致，
	/// 多让出的那一圈同样由父容器真色铺满（`RButton.OnPaint` 现在两主题都铺）。
	/// </summary>
	internal const float RegionOutsetDevicePx = 2f;

	/// <summary>
	/// 2026-10-09（UI 反馈①，仅夜间）：键盘灯光「预设颜色」色板**选中**那一格的浅色填充 ——
	/// 与该色块自身的颜色协调（朝白 42%），无论色块是红/绿/黑都能一眼看出被选中。
	/// 只由 RgbForm 的预设色板在夜间、且该格正是当前选中色时使用（日间调用方不取本值 ⇒ 日间像素不变）。
	/// ⚠️ 只用于色板这一处：按钮族的选中态一律"无浅色填充"（见 <see cref="NeutralActivatedFill" />）。
	/// </summary>
	internal static Color SwatchSelectionFill(Color swatch)
	{
		return Mix(swatch, Color.White, 0.42f);
	}

	/// <summary>
	/// 选中态的**底**（与那圈 2 设备像素描边配对的填充）。
	/// <list type="bullet">
	/// <item>**夜间**（不变）：基色 <see cref="ButtonFace" />（62/62/62，与未选中同级）混入该模式色 **12%**
	/// —— 用户原话「填充浅色很怪」，故夜间**只描边、不做浅填充**，模式色主要由那圈描边承载
	/// （混色后相对亮度对比仅 1.18~1.31 ⇒ 仍是"深底"）。「待生效＝棕」同理（Warn 混色后 R/G 仍高于 B）。</item>
	/// <item>**日间**（0.291 beta22 新增，用户追加口径「性能模式浅色填充依旧没有」；0.291 beta22 补丁再调浅）：
	/// **只给性能模式五行**（用户口径「除了性能模式都不要浅填充」——显卡行「待生效＝棕」等非性能组走
	/// <see cref="NeutralActivatedFill" /> 的中性面，不加填充）。基色＝ <see cref="ButtonFace" /> 朝白提亮
	/// <see cref="DayFillWhite" />（＝240,243,247），再混入该模式色 <see cref="DayModeFillAccent" />（15%）
	/// ⇒ 一眼可辨但很轻的**淡彩填充**（静音 `208,226,221`、平衡 `209,223,244`、静音狂暴 `238,224,226`、
	/// 高能狂暴 `233,212,216`、自定义 `222,235,245`）。文字 <see cref="Text" /> 在其上 11.6~13.5（AA ✅）；
	/// 与那圈浅边的对比 3.15~3.65（全部 ≥3:1）。⚠️ 只调"提亮量 + 混入比例"，不引入任何新的显式色值。</item>
	/// </list>
	/// </summary>
	internal static Color ModeActivatedFill(Color accent)
	{
		if (!IsNightMode)
		{
			Color dayBase = Mix(ButtonFace, Color.White, DayFillWhite);
			return Mix(dayBase, accent, DayModeFillAccent);
		}
		Color baseColor = ButtonFace;
		const float amount = 0.12f;
		return Color.FromArgb(
			(int)Math.Round(baseColor.R + (accent.R - baseColor.R) * amount),
			(int)Math.Round(baseColor.G + (accent.G - baseColor.G) * amount),
			(int)Math.Round(baseColor.B + (accent.B - baseColor.B) * amount));
	}

	/// <summary>
	/// 2026-10-09（UI 打磨②b，仅夜间）：性能模式按钮选中态描边的线宽（DPI 缩放），100% DPI 下 2px。
	/// 2026-10-09（UI 反馈①-A，用户原话「它的高亮粗且明显 而且没有锯齿」）：**线宽取整到整数设备像素**
	/// —— 旧式 <c>2 × Dpi/96</c> 在 175% 下是 3.5px，半像素线宽配 <see cref="System.Drawing.Drawing2D.PenAlignment.Inset" />
	/// 会让描边的**内沿**落在两条像素的交界上（内沿发糊/看着毛），且粗细每台机器不同。
	/// 现在：100%→2、125%→3、150%→3、175%→4、200%→4 设备像素，一律整数 ⇒ 内外沿都落在整像素列/行上。
	/// 2026-10-09（P0.5）：**全仓唯一的"按钮族选中边"线宽来源** —— 下拉描边（<see cref="ComboOutlineWidth" />）
	/// 与按钮选中描边（<c>RButton.DrawActivatedOutline</c>）都读本值 ⇒ 两族同宽这件事由代码结构保证，
	/// 不再各写一份 <c>2f</c>。宽度换算本身收敛到
	/// <see cref="OutlinePen(Control, Color, float, float, bool)" />。
	/// </summary>
	internal const float ModeOutlineLogical = 2f;

	/// <summary>2026-10-09（P0.5）：按钮族选中边线宽（兼容入口，按绘制 DC 的 DPI；界面控件请走 Control 版）。</summary>
	internal static float ModeOutlineWidth(Graphics graphics)
	{
		return Math.Max(2f, (float)Math.Round(ModeOutlineLogical * UiDpi.Paint(graphics) / 96f, MidpointRounding.AwayFromZero));
	}

	/// <summary>2026-10-09（P0.5）：按钮族选中边线宽（按控件自己的 DPI，与下拉族同源）。</summary>
	internal static float ModeOutlineWidth(Control control)
	{
		return Math.Max(2f, (float)Math.Round(ModeOutlineLogical * UiDpi.Paint(control) / 96f, MidpointRounding.AwayFromZero));
	}

	private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle rect, int radius)
	{
		System.Drawing.Drawing2D.GraphicsPath graphicsPath = new System.Drawing.Drawing2D.GraphicsPath();
		if (radius <= 0)
		{
			graphicsPath.AddRectangle(rect);
			return graphicsPath;
		}
		int num = radius * 2;
		graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑥，用户原话「学习一下它的描边 我们这个锯齿感很厉害」）：把「圆角形状的窗口裁剪区」
	/// 做**抗锯齿友好**处理。
	///
	/// 成因（这就是锯齿感的来源）：WinForms 的 <c>Control.Region = new Region(path)</c> 交给
	/// <c>SetWindowRgn</c>，那条 region 是**二值掩码** —— 抗锯齿绘制出来的边缘像素
	/// （透明/半透明的那一条，约 1 个设备像素宽）会被干干净净地切掉，剩下的是 0/1 的阶梯，
	/// 观感就是"锯齿"。本方法把同一条圆角路径**向外偏移 <paramref name="outsetDevicePx" /> 个设备像素**
	/// 再裁：让那条半透明羽化边完整落进裁剪区内，阶梯就消失了。
	///
	/// ⚠️ 为什么必须保留 Region（而不是"改成不裁 Region、自己填父色"）：子窗口不会自动被父窗口裁剪，
	/// 取消 Region 后控件的圆角那几颗像素会露出自己的方角底色 —— 就是之前折腾很久的"整块异色矩形"假边。
	/// 偏移只向外、且只有 1 个设备像素，控件不会被裁掉任何可见内容（向外的部分本来就不属于它）。
	///
	/// ⚠️ 2026-10-09 紧急修复：<b>Region 必须是「原路径填充 ∪ widen 环形」</b>，**不能**只用 widen 的结果。
	/// widen 只给出"描边那一圈"，中间是洞 —— 详见方法体内的实测数据与说明。
	///
	/// <paramref name="outsetDevicePx" /> 应传「设备像素」（<c>UiDpi.Paint/96</c> 缩放后的 1），
	/// 这样 100% / 150% / 200% DPI 下都只让出 1 个物理像素。
	/// </summary>
	internal static void SetRoundedRegion(Control control, System.Drawing.Drawing2D.GraphicsPath path, float outsetDevicePx)
	{
		if (control == null || path == null)
		{
			return;
		}
		// ⚠️ 2026-10-09 紧急修复（用户原话「文字消失了 按钮不能点击了」）：**绝不能只用 widen 后的路径建 Region**。
		// GraphicsPath.Widen(pen) 返回的是「沿原路径描边的那一圈」，即一个**环形带（annulus）**：它有外轮廓，
		// 也有由原路径构成的内轮廓 ⇒ 用 new Region(widen 结果) 得到的裁剪区中间是**一个洞**。
		// SetWindowRgn(环形) 的后果与截图完全一致：控件只剩一圈边框是实心的，中间被整块裁掉 ⇒
		// 文字画在洞里看不见、鼠标点击落在洞外被穿透（连同命中测试一起失效）。
		// 已在 GDI+ 层实测确认（96 DPI、半径 20 的 160×40 圆角矩形、outset=1）：
		//   widenOnly.IsVisible(中心 80,20) = False    ← 洞
		//   fillOnly .IsVisible(中心 80,20) = True
		//   union    .IsVisible(中心 80,20) = True
		// 正确做法：Region = 原路径（填充实体）∪ widen(pen)（向外扩张的那 1 个设备像素）。
		// 并集同时保住两个目标 —— ① 覆盖控件**整个内部**（文字/命中区域恢复原状）；
		// ② 外沿仍比原路径外扩 outsetDevicePx（抗锯齿羽化边不再被二值掩码切掉，⑥ 的锯齿修复保留）。
		System.Drawing.Drawing2D.GraphicsPath? outline = path.Clone() as System.Drawing.Drawing2D.GraphicsPath;
		Region region = new Region(path);
		if (outsetDevicePx > 0.01f && outline != null)
		{
			using (Pen pen = new Pen(Color.Black, outsetDevicePx * 2f))
			{
				pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Center;
				pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
				// ⚠️ .NET 的 GraphicsPath.Widen 是**原地加宽**（返回 void），加宽后的路径就是 outline 本身。
				outline.Widen(pen);
			}
			using Region outlineRegion = new Region(outline);
			region.Union(outlineRegion);
		}
		outline?.Dispose();
		control.Region = region;
	}

	internal static bool IsNightMode => _auditNightMode ?? (FollowSystemTheme ? WindowsDarkTheme() : ResolveNightMode(AppConfig.GetString("ui_theme_mode")));

	internal static Font Font(float size, FontStyle style = FontStyle.Regular)
	{
		return Font(size, style, GraphicsUnit.Point);
	}
	// （字体缓存配套）：需要**用完即弃**的字体时用它 —— 返回全新实例、可安全 Dispose。
	internal static Font TempFont(float size, FontStyle style = FontStyle.Regular)
	{
		return new Font(FontFamilyOrDefault(), size, style);
	}

	internal static Font Font(float size, FontStyle style, GraphicsUnit unit)
	{
		// （设置窗启动慢 · 字体缓存）：同 (字号,字重,单位) 只构造一次 GDI+ Font。
		int sizeKey = (int)Math.Round(size * 10f);
		var key = (sizeKey, style, unit);
		lock (_fontCacheLock)
		{
			if (_fontCache.TryGetValue(key, out Font cached))
			{
				return cached;
			}
			Font created = new Font(FontFamilyOrDefault(), sizeKey / 10f, style, unit);
			_fontCache[key] = created;
			return created;
		}
	}

	internal static bool ResolveNightMode(string? savedMode)
	{
		return !string.Equals(savedMode, "day", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool FollowSystemTheme => AppConfig.Get("ui_theme_follow_system", 0) == 1;

	internal static void SetFollowSystemTheme(bool on)
	{
		if (AppConfig.Get("ui_theme_follow_system", 0) != (on ? 1 : 0))
		{
			AppConfig.Set("ui_theme_follow_system", on ? 1 : 0);
			AppConfig.Flush();
		}
	}

	internal static bool WindowsDarkTheme()
	{
		try
		{
			using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (key?.GetValue("AppsUseLightTheme") is int value)
			{
				return value == 0;
			}
		}
		catch
		{
		}
		return false;
	}

	internal static void SetNightMode(bool night)
	{
		string text = (night ? "night" : "day");
		if (!string.Equals(AppConfig.GetString("ui_theme_mode"), text, StringComparison.OrdinalIgnoreCase))
		{
			AppConfig.Set("ui_theme_mode", text);
			AppConfig.Flush();
		}
	}

	internal static void SetAuditNightMode(bool? night)
	{
		_auditNightMode = night;
	}

	internal static (Color Background, Color Foreground) GetAccentButtonColors(bool night)
	{
		Palette palette = (night ? NightPalette : DayPalette);
		return (Background: palette.Accent, Foreground: palette.AccentText);
	}

	internal static (Color Background, Color Foreground) GetAccentButtonHoverColors(bool night)
	{
		Palette palette = (night ? NightPalette : DayPalette);
		return (Background: palette.AccentHover, Foreground: palette.AccentText);
	}

	/// <summary>2026-10-22（主题自定义）：解析 config 里的颜色字符串（#RRGGBB / #AARRGGBB）；空/非法返回 null。</summary>
	internal static Color ParseCustomColor(string raw)
	{
		Color color;
		return TryParseCustomColor(raw, out color) ? color : Color.Empty;
	}

	/// <summary>2026-10-22：解析成功返回 true（颜色写入 <paramref name="color" />）；空/非法返回 false。</summary>
	internal static bool TryParseCustomColor(string raw, out Color color)
	{
		color = Color.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}
		try
		{
			string text = raw.Trim();
			if (text.StartsWith("#", StringComparison.Ordinal))
			{
				text = text.Substring(1);
			}
			if (text.Length == 6)
			{
				color = Color.FromArgb(255, Convert.ToInt32(text.Substring(0, 2), 16), Convert.ToInt32(text.Substring(2, 2), 16), Convert.ToInt32(text.Substring(4, 2), 16));
				return true;
			}
			if (text.Length == 8)
			{
				color = Color.FromArgb(Convert.ToInt32(text.Substring(0, 2), 16), Convert.ToInt32(text.Substring(2, 2), 16), Convert.ToInt32(text.Substring(4, 2), 16), Convert.ToInt32(text.Substring(6, 2), 16));
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>2026-10-22：把颜色写成 config 字符串（#RRGGBB）；<see cref="Color.Empty" /> 表示清除。</summary>
	internal static void SaveCustomColor(string key, Color color)
	{
		AppConfig.Set(key, (color == Color.Empty) ? "" : ("#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2")));
	}

	private static Color ContrastTextFor(Color background)
	{
		return (RelativeLuminance(background) > 0.5) ? Color.FromArgb(8, 18, 31) : Color.White;
	}

	private static readonly object BackgroundImageGate = new object();
	private static readonly ConditionalWeakTable<Form, List<Control>> TransparentRoots = new ConditionalWeakTable<Form, List<Control>>();
	private static string _loadedBackgroundImagePath = "";
	private static Image _loadedBackgroundImage;
	private static int _loadedBackgroundOpacity = -1;
	private static int _loadedBackgroundScrim = -1;
	private static Color _loadedBackgroundBaseColor = Color.Empty;

	/// <summary>2026-10-22（主题自定义）：把「强调色 + 背景色/背景图」应用到窗体。
	/// <para>背景图只在主窗体上生效：把根容器（底色=Window 的面板）设成透明，让图片从卡片间隙露出来。</para></summary>
	internal static void ApplyFormBackground(Form form)
	{
		if (form == null || form.IsDisposed)
		{
			return;
		}
		try
		{
			Color currentWindow = Window;
			Image image = LoadBackgroundImage();
			form.BackColor = currentWindow;
			form.BackgroundImageLayout = ImageLayout.None;
			form.BackgroundImage = image;
			List<Control> transparent = new List<Control>();
			// 2026-10-22：先还原上一次设成透明的容器（主题色/背景色变化后要重新套）。
			if (TransparentRoots.TryGetValue(form, out List<Control> previous))
			{
				foreach (Control old in previous)
				{
					if (old != null && !old.IsDisposed && old.BackColor == Color.Transparent)
					{
						old.BackColor = Window;
					}
				}
			}
			if (image != null)
			{
				// 递归处理：把「页面底」色的容器都设透明（主页仪表盘/栈/分组），卡片(Surface)保持不透明。
				ApplyTransparentPageBackgrounds(form, transparent);
			}
			TransparentRoots.Remove(form);
			TransparentRoots.Add(form, transparent);
			// 2026-10-22：透明容器/文字控件重设后强制整窗重绘，避免残影（旧帧没被擦掉）。
			foreach (Control control in transparent)
			{
				if (control != null && !control.IsDisposed)
				{
					control.Invalidate();
				}
			}
			form.Invalidate(invalidateChildren: true);
			Logger.WriteLine("[theme] 背景应用：color=" + ToHex(Window) + " · image=" + (image != null ? "有" : "无") + " · 透明根=" + transparent.Count);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[theme] 背景应用失败（已忽略）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	private static Image LoadBackgroundImage()
	{
		string path = (AppConfig.GetString(BackgroundImageConfigKey) ?? "").Trim();
		int opacity = Math.Clamp(AppConfig.Get(BackgroundOpacityConfigKey, 65), 0, 100);
		int scrim = Math.Clamp(AppConfig.Get(BackgroundScrimConfigKey, 50), 0, 100);
		Color baseColor = Window;
		lock (BackgroundImageGate)
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				_loadedBackgroundImagePath = "";
				_loadedBackgroundImage = null;
				return null;
			}
			if (string.Equals(path, _loadedBackgroundImagePath, StringComparison.OrdinalIgnoreCase)
				&& opacity == _loadedBackgroundOpacity
				&& scrim == _loadedBackgroundScrim
				&& baseColor == _loadedBackgroundBaseColor)
			{
				return _loadedBackgroundImage;
			}
			try
			{
				using Image source = Image.FromFile(path);
				// 2026-10-22（机主：加背景图后按钮可能需要半透明）：WinForms 对子控件透明度支持有限，
				// 这里改成把图片按不透明度**预先混到窗口底色上**，卡片/按钮保持原色也仍然清晰。
				Bitmap blended = new Bitmap(Math.Max(1, source.Width), Math.Max(1, source.Height));
				using (Graphics graphics = Graphics.FromImage(blended))
				{
					graphics.Clear(baseColor);
					float effectiveAlpha = (opacity / 100f) * (1f - scrim / 100f);
					if (effectiveAlpha > 0.001f)
					{
						using System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes();
						System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix
						{
							Matrix33 = effectiveAlpha
						};
						attributes.SetColorMatrix(matrix);
						graphics.DrawImage(source, new Rectangle(0, 0, blended.Width, blended.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
					}
				}
				_loadedBackgroundImage?.Dispose();
				_loadedBackgroundImage = blended;
				_loadedBackgroundImagePath = path;
				_loadedBackgroundOpacity = opacity;
				_loadedBackgroundScrim = scrim;
				_loadedBackgroundBaseColor = baseColor;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[theme] 背景图加载失败：" + ex.Message);
				_loadedBackgroundImagePath = "";
				_loadedBackgroundImage = null;
			}
			return _loadedBackgroundImage;
		}
	}

	/// <summary>2026-10-22（主题背景图）：窗体自己画背景图（cover 铺满整窗，上下/左右不留空）。
	/// <returns>true 表示已画（调用方不要再走默认背景绘制）。</returns></summary>
	internal static bool PaintFormBackground(Graphics graphics, Form form)
	{
		Image image = form?.BackgroundImage;
		if (graphics == null || form == null || image == null)
		{
			return false;
		}
		Size client = form.ClientSize;
		if (client.Width <= 0 || client.Height <= 0 || image.Width <= 0 || image.Height <= 0)
		{
			return false;
		}
		float scale = Math.Max((float)client.Width / image.Width, (float)client.Height / image.Height);
		float width = image.Width * scale;
		float height = image.Height * scale;
		float left = (client.Width - width) / 2f;
		float top = (client.Height - height) / 2f;
		graphics.DrawImage(image, new RectangleF(left, top, width, height));
		return true;
	}

	/// <summary>2026-10-22：控件是否位于底栏（底栏不参与背景自定义）。</summary>
	internal static bool IsInsideFooter(Control control)
	{
		for (Control parent = control; parent != null; parent = parent.Parent)
		{
			if (string.Equals(parent.Name, "panelFooter", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>2026-10-22（主题背景图）：把窗体背景图在控件位置上的那一块画进控件，
	/// 让按钮/开关的半透明填充真正“透出底图”（否则 WinForms 控件会先擦成实心底，alpha 只在实心底上混）。
	/// <para>只处理 BackgroundImageLayout=Zoom（与 ApplyFormBackground 一致）。</para></summary>
	internal static void DrawFormBackgroundSlice(Graphics graphics, Control control)
	{
		Form form = control?.FindForm();
		Image image = form?.BackgroundImage;
		if (graphics == null || form == null || image == null || control.Width <= 0 || control.Height <= 0)
		{
			return;
		}
		Rectangle client = form.ClientRectangle;
		// 2026-10-22（机主：图片上下边要填满）：按 cover 缩放（取较大比例、超出部分裁掉）。
		float scale = Math.Max((float)client.Width / image.Width, (float)client.Height / image.Height);
		if (scale <= 0f)
		{
			return;
		}
		float width = image.Width * scale;
		float height = image.Height * scale;
		float left = client.X + (client.Width - width) / 2f;
		float top = client.Y + (client.Height - height) / 2f;
		Point controlOnForm = form.PointToClient(control.PointToScreen(Point.Empty));
		System.Drawing.Drawing2D.GraphicsState state = graphics.Save();
		try
		{
			graphics.SetClip(new Rectangle(0, 0, control.Width, control.Height));
			graphics.DrawImage(image, new RectangleF(left - controlOnForm.X, top - controlOnForm.Y, width, height));
		}
		finally
		{
			graphics.Restore(state);
		}
	}

	/// <summary>2026-10-22（主题背景图）：按透明度返回控件填充色；未启用背景图时原样返回。</summary>
	internal static Color TranslucentFor(Control control, Color color, int alpha)
	{
		if (alpha >= 255 || color.A == 0 || !FormHasBackgroundImage(control))
		{
			return color;
		}
		return Color.FromArgb(Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);
	}

	/// <summary>2026-10-22（主题背景图）：当前控件所在窗体是否正在显示背景图。</summary>
	internal static bool FormHasBackgroundImage(Control control)
	{
		Form form = control?.FindForm();
		return form != null && form.BackgroundImage != null;
	}

	/// <summary>2026-10-22：递归把「页面底」色的容器设成透明，让窗体背景图从卡片间隙/页面空白处露出来。
	/// <para>⚠️ 只处理 Panel/TableLayoutPanel/FlowLayoutPanel 且底色=Window 的容器；Surface 卡片不透明、整块保留。</para></summary>
	private static void ApplyTransparentPageBackgrounds(Control parent, List<Control> transparent)
	{
		foreach (Control child in parent.Controls)
		{
			if (child == null || child.IsDisposed)
			{
				continue;
			}
			// 2026-10-22（机主：底栏不参与背景自定义）：底栏整块跳过，保持原样。
			if (string.Equals(child.Name, "panelFooter", StringComparison.Ordinal))
			{
				continue;
			}
			bool container = child is Panel || child is TableLayoutPanel || child is FlowLayoutPanel;
			Color back = child.BackColor;
			if (!container)
			{
				// 2026-10-22：标签/复选框这类「文字控件」若还是窗口底色，会在背景图上变成一个个黑色方块；
				// 把它们也设成透明（不支持透明的控件 try/catch 跳过）。
				bool textControl = child is Label || child is CheckBox;
				if (textControl && (back == Window || back == NightPalette.Window || back == DayPalette.Window))
				{
					try
					{
						child.BackColor = Color.Transparent;
						transparent.Add(child);
					}
					catch
					{
					}
				}
				continue;
			}
			if (back == Window || back == NightPalette.Window || back == DayPalette.Window)
			{
				child.BackColor = Color.Transparent;
				transparent.Add(child);
				ApplyTransparentPageBackgrounds(child, transparent);
			}
			else if (back == Color.Transparent)
			{
				ApplyTransparentPageBackgrounds(child, transparent);
			}
		}
	}

	private static string ToHex(Color color)
	{
		return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}

	internal static void RetintChrome(Control root)
	{
		root.BackColor = MapChrome(root.BackColor);
		foreach (Control control in root.Controls)
		{
			RetintChrome(control);
		}
	}

	private static Color MapChrome(Color color)
	{
		Palette palette = (IsNightMode ? DayPalette : NightPalette);
		Palette current = Current;
		if (SameRgb(color, palette.Window))
		{
			return current.Window;
		}
		if (SameRgb(color, palette.Surface))
		{
			return current.Surface;
		}
		if (SameRgb(color, palette.SurfaceRaised))
		{
			return current.SurfaceRaised;
		}
		if (SameRgb(color, palette.Input))
		{
			return current.Input;
		}
		if (SameRgb(color, palette.Border))
		{
			return current.Border;
		}
		if (SameRgb(color, palette.Track))
		{
			return current.Track;
		}
		return color;
	}

	internal static double ContrastRatio(Color first, Color second)
	{
		double val = Luminance(first);
		double val2 = Luminance(second);
		return (Math.Max(val, val2) + 0.05) / (Math.Min(val, val2) + 0.05);
		static double Linear(byte value)
		{
			double num = (double)(int)value / 255.0;
			if (!(num <= 0.04045))
			{
				return Math.Pow((num + 0.055) / 1.055, 2.4);
			}
			return num / 12.92;
		}
		static double Luminance(Color color)
		{
			return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
		}
	}

	public static void ApplyWindow(Form form)
	{
		form.BackColor = Window;
		form.ForeColor = Text;
		form.Font = Font(9f);
		ApplyTree(form);
	}

	public static void ApplySection(Control section)
	{
		SetContainerColor(section, Surface);
		ApplyTree(section);
	}

	public static void ApplyRow(Control row)
	{
		SetContainerColor(row, Window);
		ApplyTree(row);
	}

	public static void ApplyPrimaryButton(Button button)
	{
		button.BackColor = Accent;
		button.ForeColor = AccentText;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		button.FlatAppearance.MouseOverBackColor = AccentHover;
		button.FlatAppearance.MouseDownBackColor = AccentPressed;
		button.Font = Font(9f, FontStyle.Bold);
	}

	public static void ApplySecondaryButton(ButtonBase button)
	{
		if (!(button is RColorButton) && !object.Equals(button.Tag, "color-swatch") && !object.Equals(button.Tag, "theme_mode"))
		{
			// 2026-10-09（UI 打磨⑤）：二次按钮的底色由 SurfaceRaised 换成 ButtonFace。
			// 日间两者同值（233/239/246），日间像素不变；夜间 ButtonFace 是「比卡片更浅」的按钮面。
			button.BackColor = ButtonFace;
			button.ForeColor = Text;
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderColor = Border;
			button.FlatAppearance.BorderSize = 1;
		}
	}

	public static void ApplySegmentGroup(params RButton[] buttons)
	{
		for (int i = 0; i < buttons.Length; i++)
		{
			buttons[i].BorderRadius = 16;
			buttons[i].SegmentPosition = ((buttons.Length == 1) ? RSegmentPosition.Single : ((i == 0) ? RSegmentPosition.First : ((i == buttons.Length - 1) ? RSegmentPosition.Last : RSegmentPosition.Middle)));
		}
	}

	/// <summary>
	/// 2026-10-09（UI 打磨③，用户原话「然后这种长条的能不能拆成和Ghelper一样独立的呢」）：
	/// 把一行「分段控件」拆成一颗颗**独立按钮**（G-Helper 风）——与 <see cref="ApplySegmentGroup" /> 的区别只有两点：
	/// ① 每个按钮 SegmentPosition=Single（四角全圆角，各自成一张小卡片，不再有"共用的左右端头 + 中缝分隔线"）；
	/// ② 相邻按钮之间留 <see cref="IndependentGapLogical" />（6）逻辑像素的可见缝隙（DPI 缩放，两侧各分一半；
	///    行首/行尾不加额外缩进，故整行的左右边界与拆分前逐像素一致）。
	/// 行高不变：只下发**水平** Margin，垂直 Margin 恒为 0，按钮仍 Dock=Fill 在**绝对行高**的单元格里
	/// （性能/显卡行 = D(34)），故每颗按钮的高度与拆分前逐像素相同。
	/// 没有"底槽"要处理：拆分前那条"长条"就是各分段按钮自己用 UiVisualStyle.Input 铺出来的连续面，
	/// 容器（TableLayoutPanel）不画任何底板；拆分后既不需要去掉底槽，也不会留下旧的分隔线
	/// （分隔线是 DrawSegmentBorder 的 First/Middle/Last 分支画的，SegmentPosition=Single 走 default 分支，
	/// 只画自己那一圈圆角边框）。
	/// <para>2026-10-12：内边距**统一**为左右各 gap/2（⛔ 不再让首/末两颗贴齐行两端）——
	/// 理由见方法体内注释（用户要求"几个按钮大小完全一致"）。</para>
	/// ⚠️ 这是**布局**改动、不是配色改动：日间也会变成一颗颗独立按钮，但日间色板一个数值都没改。
	/// </summary>
	internal const int IndependentGapLogical = 6;

	public static void ApplyIndependentRow(params RButton[] buttons)
	{
		if (buttons == null || buttons.Length == 0)
		{
			return;
		}
		for (int i = 0; i < buttons.Length; i++)
		{
			RButton rButton = buttons[i];
			if (rButton == null)
			{
				continue;
			}
			int gap = Math.Max(2, ResponsiveLayout.LogicalToDevice(rButton, IndependentGapLogical));
			rButton.BorderRadius = 10;
			rButton.SegmentPosition = RSegmentPosition.Single;
			// 2026-10-12（用户原话「检查一下性能模式四个按钮大小是不是一样的」）：**统一内边距** ——
			// 原来首/末两颗把**外侧**间距去掉（左/右 margin = 0）以贴齐行两端，代价是它们各比中间那颗宽 gap/2：
			// 实测（取证宿主 [geo]，1920x1080 100%）= 静音 89 / 平衡 86 / 静音狂暴 86 / 高能狂暴 89；
			// 200% = 166/160/160/166 ⇒ 肉眼可辨的"两头大一圈"。改为每颗左右各 gap/2（总间距恒为 gap）后，
			// 等宽列下四颗按钮**逐一等宽**、相邻间距均匀（行两端各让出 gap/2，与卡片内边距观感一致）。
			rButton.Margin = new Padding(gap / 2, 0, gap - gap / 2, 0);
		}
	}

	/// <summary>
	/// 2026-10-09（UI 打磨③，仅夜间）：把一组「横向选项」按钮的选中态统一切成 ②b 的「彩边 + 深底」画法
	/// （RButton.ActivatedOutline）。只改画法，不改任何语义色：调用方事先/事后照旧下发 ActivatedColor
	/// （性能模式行 = 模式色、GPU 行 = 待生效棕/默认强调色、刷新率行 = 强调色、电池档位行 = 强调色）。
	/// 日间由 RButton.OnPaint 的 IsNightMode 门控忽略本开关，仍是实心填充，像素不变。
	/// 2026-10-09（UI 打磨④）：本方法只管开关；选中态的几何（深底铺满整颗客户区 + 2px 彩边画在按钮
	/// 自身圆角路径上、并抑制按钮原有那圈边框）由 RButton.DrawActivatedOutline 统一决定，调用方
	/// 不再下发任何按行不同的内缩值。
	/// </summary>
	internal static void UseOutlineSelection(params RButton[] buttons)
	{
		if (buttons == null)
		{
			return;
		}
		foreach (RButton rButton in buttons)
		{
			if (rButton != null)
			{
				rButton.ActivatedOutline = true;
			}
		}
	}

	public static void ApplyTitle(Label label, float size = 9.5f)
	{
		label.ForeColor = Text;
		label.Font = Font(size, FontStyle.Bold);
	}

	public static void ApplyMuted(Label label)
	{
		label.ForeColor = Muted;
	}

	internal static void ApplyGlyph(PictureBox box, UiGlyph.Kind kind)
	{
		box.Tag = kind;
		RenderGlyph(box, kind);
		Logger.WriteLine($"[diag] glyph {kind} -> {box.Name}#{box.GetHashCode()} form={box.FindForm()?.GetHashCode()} tagAfter={box.Tag}");
	}

	internal static void ApplyGlyph(Label label, UiGlyph.Kind kind)
	{
		label.Tag = kind;
		RenderGlyph(label, kind);
	}

	internal static void ApplyFooterGlyph(ButtonBase button, UiGlyph.Kind kind, int side)
	{
		ApplyFooterGlyph(button, kind, side, null);
	}

	internal static void ApplyFooterGlyph(ButtonBase button, UiGlyph.Kind kind, int side, Func<Color>? color)
	{
		_footerGlyphs.AddOrUpdate(button, kind);
		_footerGlyphColors.AddOrUpdate(button, color ?? ((Func<Color>)(() => Muted)));
		Image? image = button.Image;
		button.Image = UiGlyph.Render(kind, side, color?.Invoke() ?? Muted);
		image?.Dispose();
	}

	private static void RenderFooterGlyph(ButtonBase button, UiGlyph.Kind kind)
	{
		Image image = button.Image;
		int side = ((image != null && image.Width > 8) ? image.Width : 16);
		_footerGlyphColors.TryGetValue(button, out object value);
		ApplyFooterGlyph(button, kind, side, value as Func<Color>);
	}

	/// <summary>
	/// 2026-10-12 r6（用户原话「还有检查更新 设置等界面左上角图标 能不能同步按钮的图标」）：
	/// 把窗体**标题栏左上角那枚图标**设成与对应按钮**同一枚** <see cref="UiGlyph.Kind" />（单一来源，
	/// ⛔ 不再各画一套、⛔ 不引入位图素材）。颜色固定取**强调色**（<see cref="Accent" />）：
	/// 深浅两套标题栏上都看得见，也不会因为窗体只建一次而在换主题后失效。
	/// ⚠️ 只改 <c>Form.Icon</c>，⛔ 不碰布局/尺寸/文本/行为。
	/// </summary>
	internal static void ApplyWindowGlyph(Form form, UiGlyph.Kind kind)
	{
		_windowGlyphs.AddOrUpdate(form, kind);
		RenderWindowGlyph(form, kind);
	}

	/// <summary>按当前 DPI 画 16 逻辑像素的图标，并挂到标题栏。⚠️ <c>Form.Icon</c> 不负责释放旧图标。</summary>
	private static void RenderWindowGlyph(Form form, UiGlyph.Kind kind)
	{
		int side = Math.Max(16, ResponsiveLayout.LogicalToDevice(form, 16));
		using Bitmap bitmap = UiGlyph.Render(kind, side, Accent);
		form.Icon = CreateAlphaIcon(bitmap);
	}

	/// <summary>
	/// 把 32bpp 位图写成**带 alpha 的 .ico**。
	/// ⚠️ 2026-10-12 r6b 实测：<c>Bitmap.GetHicon()</c> → <c>Icon.FromHandle</c> → <c>Icon.Save</c> → <c>new Icon</c>
	/// 这条链**会丢 alpha**（透明角落变成不透明黑）⇒ 标题栏上图标是一块黑方块。所以这里手写 ICO 容器：
	/// ICONDIR(6) + ICONDIRENTRY(16) + BITMAPINFOHEADER(40) + XOR(自下而上 BGRA) + AND 掩码。
	/// </summary>
	private static Icon CreateAlphaIcon(Bitmap bitmap)
	{
		int side = bitmap.Width;
		int maskStride = (side + 31) / 32 * 4;
		int imageBytes = 40 + side * side * 4 + maskStride * side;
		using MemoryStream stream = new MemoryStream(22 + imageBytes);
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write((ushort)0);
		writer.Write((ushort)1);
		writer.Write((ushort)1);
		writer.Write((byte)((side >= 256) ? 0 : side));
		writer.Write((byte)((side >= 256) ? 0 : side));
		writer.Write((byte)0);
		writer.Write((byte)0);
		writer.Write((ushort)1);
		writer.Write((ushort)32);
		writer.Write(imageBytes);
		writer.Write(22);
		writer.Write(40);
		writer.Write(side);
		writer.Write(side * 2);
		writer.Write((ushort)1);
		writer.Write((ushort)32);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		for (int y = side - 1; y >= 0; y--)
		{
			for (int x = 0; x < side; x++)
			{
				Color pixel = bitmap.GetPixel(x, y);
				writer.Write(pixel.B);
				writer.Write(pixel.G);
				writer.Write(pixel.R);
				writer.Write(pixel.A);
			}
		}
		for (int y = side - 1; y >= 0; y--)
		{
			for (int b = 0; b < maskStride; b++)
			{
				byte bits = 0;
				for (int i = 0; i < 8; i++)
				{
					int x = b * 8 + i;
					if (x < side && bitmap.GetPixel(x, y).A == 0)
					{
						bits |= (byte)(128 >> i);
					}
				}
				writer.Write(bits);
			}
		}
		writer.Flush();
		stream.Position = 0L;
		return new Icon(stream);
	}

	internal static void StyleFooterGhostButton(ButtonBase button)
	{
		button.FlatStyle = FlatStyle.Flat;
		button.Tag = "footer-ghost";
		button.Margin = Padding.Empty;
		button.Padding = Padding.Empty;
		button.ImageAlign = ContentAlignment.TopCenter;
		button.TextAlign = ContentAlignment.BottomCenter;
		button.TextImageRelation = TextImageRelation.ImageAboveText;
		button.ForeColor = Text;
		button.Font = Font(7f);
		if (button is RButton rButton)
		{
			rButton.Borderless = true;
			rButton.BorderRadius = 2;
		}
		button.FlatAppearance.BorderColor = button.BackColor;
		button.FlatAppearance.BorderSize = 0;
	}

	private static void RenderGlyph(PictureBox box, UiGlyph.Kind kind)
	{
		int size;
		if (box.Height > 8)
		{
			size = box.Height;
		}
		else
		{
			size = ((box.Width > 8) ? box.Width : 16);
		}
		Image? image = box.Image;
		box.Image = UiGlyph.Render(kind, size, Muted);
		image?.Dispose();
		if (box.Parent != null)
		{
			box.BackColor = box.Parent.BackColor;
		}
	}

	private static void RenderGlyph(Label label, UiGlyph.Kind kind)
	{
		if (label.Image != null)
		{
			Image? image = label.Image;
			label.Image = null;
			image?.Dispose();
		}
		if (label.Padding.Left != 0)
		{
			label.Padding = Padding.Empty;
		}
	}

	private static void ApplyTree(Control root)
	{
		// r6：换主题/换 DPI 时重画窗体标题栏图标（登记过才画；没登记过的窗体一律不碰）。
		if (root is Form windowForm && _windowGlyphs.TryGetValue(windowForm, out object windowValue) && windowValue is UiGlyph.Kind windowKind)
		{
			RenderWindowGlyph(windowForm, windowKind);
		}
		foreach (Control control4 in root.Controls)
		{
			Control control2 = control4;
			if (!(control2 is RColorButton))
			{
				if (!(control2 is PictureBox pictureBox))
				{
					// 2026-10-09（CPU 调节页「下载 PawnIO 驱动」夜间太暗）：LinkLabel **派生自 Label**，
					// 所以这一条必须排在下面那条 `is Label` 之前，否则永远轮不到它（链接会一直沿用
					// 系统的 0,0,255 —— 在夜间页面底 Window(28,28,28) 上对比度只有 1.98，确实读不出来）。
					if (control2 is LinkLabel linkLabel)
					{
						StyleLinkLabel(linkLabel);
					}
					else if (!(control2 is Label label))
					{
						if (!(control2 is CheckBox checkBox))
						{
							if (!(control2 is RadioButton radioButton))
							{
								if (!(control2 is ButtonBase buttonBase))
								{
									if (!(control2 is ComboBox comboBox))
									{
										if (!(control2 is TextBoxBase textBoxBase))
										{
											if (!(control2 is NumericUpDown numericUpDown))
											{
												if (control2 is TrackBar trackBar)
												{
													trackBar.BackColor = trackBar.Parent?.BackColor ?? Surface;
												}
											}
											else
											{
												numericUpDown.BorderStyle = BorderStyle.None;
												numericUpDown.BackColor = Input;
												numericUpDown.ForeColor = Text;
												foreach (Control control5 in numericUpDown.Controls)
												{
													control5.BackColor = Input;
													control5.ForeColor = Text;
												}
												ApplyExplorerTheme(numericUpDown);
											}
										}
										else
										{
											textBoxBase.BackColor = Input;
											textBoxBase.ForeColor = Text;
											// 2026-10-09（UI 打磨②，仅夜间）：RTextBox 夜间改用自绘 1px 圆角描边
											// （UiVisualStyle.BorderStrong，见 RTextBox.WndProc），故把原生 FixedSingle
											// 方角边关掉，避免「系统方框 + 圆角描边」双线；日间保持 FixedSingle 原样。
											// ⚠️ 只影响这一个控件的边框绘制方式：Bounds/尺寸/布局/命中区域不变。
											textBoxBase.BorderStyle = ((textBoxBase is RTextBox && IsNightMode) ? BorderStyle.None : BorderStyle.FixedSingle);
										}
									}
									else
									{
										StyleComboBox(comboBox);
									}
								}
								else if (object.Equals(buttonBase.Tag, "solid-chip"))
								{
									// 2026-10-04: dark solid chip (deeper than the background, for emphasis
									// buttons such as the 30-series "config edit" chip). Muted bg + AccentText fg
									// keeps contrast in both day and night palettes.
									buttonBase.BackColor = Muted;
									buttonBase.ForeColor = AccentText;
									buttonBase.FlatAppearance.BorderSize = 0;
									buttonBase.FlatAppearance.MouseOverBackColor = Muted;
									buttonBase.FlatAppearance.MouseDownBackColor = Muted;
									if (buttonBase is RButton rButtonSolid)
									{
										rButtonSolid.BorderColor = Muted;
									}
								}
								else if (object.Equals(buttonBase.Tag, "spin-ghost"))
								{
									// 2026-10-09（灰框方案 C）：数值框右侧的 −/+ 步进按钮「去盒子感」——
									// 与所在卡片同底（Surface）、**不画任何边框**（Borderless 关掉 RButton 自己那圈
									// BorderColor/ButtonEdge），只留字形。夜/日两套主题同一口径（都取 Surface），
									// 故不存在"夜间是浅胶囊、日间是白块"的差别；悬停给一档轻反馈（SurfaceRaised），
									// 底色仍是卡片色 ⇒ 看上去不是"一个盒子被点亮"。
									// ⚠️ 只影响打了这个 tag 的按钮（本仓只有 CustomModeForm 的 −/+ 两颗），
									// mini-chip 那套（行内小胶囊 Edit/液冷 chip）一个字节都没动。
									buttonBase.BackColor = Surface;
									// 字形固定用 Muted（夜 158 灰 / 日 78,95,117，都是各自主题的次要文字色）——
									// 刻意不用 ResolveLabelForeground：主题重下发时 ControlHelper.Adjust 会先把
									// ForeColor 改成 Text，那样"日间是 Muted、切一次主题变 Text"会漂移。
									buttonBase.ForeColor = Muted;
									buttonBase.FlatAppearance.BorderSize = 0;
									buttonBase.FlatAppearance.MouseOverBackColor = SurfaceRaised;
									buttonBase.FlatAppearance.MouseDownBackColor = SurfaceRaised;
									if (buttonBase is RButton rButtonSpin)
									{
										rButtonSpin.Borderless = true;
										rButtonSpin.BorderColor = Color.Transparent;
									}
								}
								else if (object.Equals(buttonBase.Tag, "mini-chip"))
								{
									// 2026-10-09（UI 打磨⑤，仅夜间）：mini-chip（行长右侧的小「编辑/设置」按钮、
									// 液冷状态 chip）原先底色 = Surface（与所在卡片同一个色 ⇒ 只剩一圈边、面是"平"的）。
									// 夜间改用 ButtonFace（比卡片浅）；**日间仍取 Surface**，故日间逐像素不变。
									buttonBase.BackColor = (IsNightMode ? ButtonFace : Surface);
									buttonBase.ForeColor = ResolveLabelForeground(buttonBase.ForeColor);
									buttonBase.FlatAppearance.BorderSize = 0;
									buttonBase.FlatAppearance.MouseOverBackColor = (IsNightMode ? ButtonFaceHover : SurfaceRaised);
									buttonBase.FlatAppearance.MouseDownBackColor = (IsNightMode ? ButtonFaceHover : SurfaceRaised);
									if (buttonBase is RButton rButton)
									{
										rButton.BorderColor = Border;
									}
								}
								else
								{
									ButtonBase buttonBase2 = buttonBase;
									if (object.Equals(buttonBase2.Tag, "footer-ghost"))
									{
										buttonBase2.BackColor = buttonBase2.Parent?.BackColor ?? Surface;
										buttonBase2.ForeColor = ResolveLabelForeground(buttonBase2.ForeColor);
										buttonBase2.FlatAppearance.BorderColor = buttonBase2.BackColor;
										buttonBase2.FlatAppearance.BorderSize = 0;
										buttonBase2.FlatAppearance.MouseOverBackColor = SurfaceRaised;
										buttonBase2.FlatAppearance.MouseDownBackColor = SurfaceRaised;
										if (_footerGlyphs.TryGetValue(buttonBase2, out object value) && value is UiGlyph.Kind kind)
										{
											RenderFooterGlyph(buttonBase2, kind);
										}
									}
									else
									{
										ApplySecondaryButton(buttonBase);
									}
								}
							}
							else
							{
								radioButton.ForeColor = Text;
								radioButton.BackColor = radioButton.Parent?.BackColor ?? Surface;
							}
						}
						else
						{
							checkBox.ForeColor = Text;
							checkBox.BackColor = checkBox.Parent?.BackColor ?? Surface;
						}
					}
					else if (label.Tag is UiGlyph.Kind kind2)
					{
						RenderGlyph(label, kind2);
						label.ForeColor = ResolveLabelForeground(label.ForeColor);
						label.BackColor = label.Parent?.BackColor ?? Surface;
					}
					else
					{
						label.ForeColor = ResolveLabelForeground(label.ForeColor);
						label.BackColor = label.Parent?.BackColor ?? Surface;
					}
				}
				else if (pictureBox.Tag is UiGlyph.Kind kind3)
				{
					RenderGlyph(pictureBox, kind3);
					pictureBox.BackColor = pictureBox.Parent?.BackColor ?? Surface;
				}
			}
			ApplyTree(control4);
		}
	}

	private static Color ResolveLabelForeground(Color color)
	{
		if (IsMuted(color) || IsPaletteMuted(color))
		{
			return Muted;
		}
		if (!IsSemanticForeground(color))
		{
			return Text;
		}
		return color;
	}

	/// <summary>
	/// 2026-10-09（用户反馈：CPU 调节页那条「下载 PawnIO 驱动」在夜间太暗、几乎看不清）：
	/// LinkLabel 的四档链接色**默认来自系统**（LinkColor 0,0,255 / ActiveLinkColor Red /
	/// VisitedLinkColor 128,0,128 / DisabledLinkColor 133,133,133 —— 本机实测取值）。
	/// 在夜间页面底 Window(28,28,28) 上，0,0,255 的对比度只有 **1.98**（WCAG AA 需 ≥4.5）；
	/// 本方法改用主题语义色：Accent 6.49、AccentHover 7.83、AccentBlue 6.91、Muted 6.36
	/// （对 Window；对 Surface 分别是 5.62 / 6.77 / 5.97 / 5.50，均 ≥4.5 AA）。
	/// 日间：**显式还原系统默认四色**（逐像素与改动前相同，且夜→日切换时不会残留夜间色）；
	/// 底色/前景仍与 Label 分支同一口径（<see cref="ResolveLabelForeground" /> + 父容器底色）。
	/// ⚠️ 只改这四档颜色：下划线、LinkBehavior、LinkArea、AutoSize、点击事件一律未动。
	/// </summary>
	private static void StyleLinkLabel(LinkLabel linkLabel)
	{
		if (IsNightMode)
		{
			linkLabel.LinkColor = Accent;
			linkLabel.ActiveLinkColor = AccentHover;
			linkLabel.VisitedLinkColor = AccentBlue;
			linkLabel.DisabledLinkColor = Muted;
		}
		else
		{
			linkLabel.LinkColor = Color.FromArgb(0, 0, 255);
			linkLabel.ActiveLinkColor = Color.Red;
			linkLabel.VisitedLinkColor = Color.FromArgb(128, 0, 128);
			linkLabel.DisabledLinkColor = Color.FromArgb(133, 133, 133);
		}
		linkLabel.ForeColor = ResolveLabelForeground(linkLabel.ForeColor);
		linkLabel.BackColor = linkLabel.Parent?.BackColor ?? Surface;
	}

	private static bool IsPaletteMuted(Color color)
	{
		if (!SameRgb(color, NightPalette.Muted))
		{
			return SameRgb(color, DayPalette.Muted);
		}
		return true;
	}

	private static bool IsSemanticForeground(Color color)
	{
		if (color.A == 0)
		{
			return false;
		}
		return Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) >= 48;
	}

	/// <summary>2026-10-09（UI 打磨⑤）：由 private 改为 internal —— RButton 需要按 RGB 判断「这颗按钮的底色是不是按钮面」。</summary>
	internal static bool SameRgb(Color first, Color second)
	{
		if (first.R == second.R && first.G == second.G)
		{
			return first.B == second.B;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑥，用户原话「学习一下它的下拉框样式我的明显糙不少」）：**唯一**的下拉框主题入口。
	///
	/// 背景（这条是本次的关键）：LM 里原来有**两条互不相通**的下拉框上色路径 ——
	/// ① <c>ApplyTree</c>（UiVisualStyle，改 BackColor/BorderColor/ArrowColor，只有 RComboBox 会自绘）；
	/// ② <c>ControlHelper.Adjust</c>（RForm.InitTheme 时跑，把 RComboBox 的底/边/箭头改写成
	///    RForm.buttonMain / borderMain / foreMain 这套**旧的**调色板值）。两者谁后跑谁生效，
	///    于是同一个界面里出现「有的下拉深色、有的白底」。
	/// 现在 ② 对 RComboBox 已停用（见 ControlHelper.Adjust），全部收敛到本方法；
	/// 并且 <see cref="RComboBox" /> 自己也会在首次绘制时调一次（<see cref="StyleComboBoxIfStale" />），
	/// 所以**主题下发之后才动态创建**的下拉框（灯效组里的「单色」等）也不会漏掉。
	///
	/// ⚠️ 只改外观属性，不碰 Items / SelectedIndex / 事件 / DropDownWidth / 尺寸。
	/// </summary>
	internal static void StyleComboBox(ComboBox combo)
	{
		if (combo == null)
		{
			return;
		}
		combo.FlatStyle = FlatStyle.Flat;
		combo.BackColor = Input;
		combo.ForeColor = Text;
		if (combo is RComboBox rComboBox)
		{
			// 2026-10-09（UI 打磨⑥）：扁平深色 —— 边 = BorderStrong、按钮格 = Input（与输入区同色，
			// 不再有"按钮格 + 白方块"），箭头 = Muted。RComboBox 的 WndProc 会用这三个值整块自绘。
			rComboBox.BorderColor = BorderStrong;
			rComboBox.ButtonColor = Input;
			rComboBox.ArrowColor = Muted;
			// 2026-10-09（UI 打磨⑥）：**展开后的列表**也统一 —— 给 RComboBox 的窗口下发 DarkMode_CFD
			// （日间 CFD），让系统那条下拉列表窗口走深色主题；再配合 RComboBox 的 WM_CTLCOLORLISTBOX
			// （深色刷子）与 OnDrawItem（整行铺满），列表就不会出现白底。
			// ⚠️ 只是 SetWindowTheme，不动 Items/索引/事件/下拉宽度。
			// 2026-10-10（用户原话「开关一次 键盘灯 校色等又会导致 下拉框绘制出问题」——截图里下拉框**左缘**
			// 残留着一道错位的边框痕迹）：这里原来紧跟一句 `_themedControls.Remove(combo);
			// _themedControls.Add(combo, FlatComboMarker);` —— **它把本方法上一行刚写进缓存的真实主题名
			// （"DarkMode_CFD"/"CFD"）覆盖成了哨兵串 "flat-combo"**，于是 <see cref="ApplyDarkComboTheme" />
			// 开头那句"主题名没变就直接 return"从此**永远不成立**，<see cref="StyleComboBox" /> 每被调一次
			// （`RForm.OnLoad`/`OnShown` 各一次、`StyleAllCombos`、以及任何外观属性 setter 触发的重绘路径）
			// 就会**真的再调一次 `SetWindowTheme`**，而它每次都会让**系统按经典/主题样式把控件自己重画一遍**
			// （这正是"方角 + 错位边框"的来源），再补一次 `Invalidate()` —— 于是本控件**每一轮都被系统重画一次
			// 覆在我们的整块自绘之上**。稳态下本该零开销（本方法自己的注释就是这么写的），实测却次次命中。
			// ⛔ 本轮只**删掉那两行覆盖**，让缓存在 <see cref="ApplyDarkComboTheme" /> 里保持"真实主题名"这一个
			// 含义；⛔ 不动 FlatStyle/BackColor/BorderColor/ButtonColor/ArrowColor，⛔ 不动 Items/事件/尺寸。
			ApplyDarkComboTheme(combo);
			return;
		}
		else
		{
			if (combo.DrawMode != DrawMode.OwnerDrawFixed)
			{
				combo.DrawMode = DrawMode.OwnerDrawFixed;
				combo.DrawItem += ComboBoxDrawItem;
			}
			ApplyDarkComboTheme(combo);
		}
	}

	/// <summary>
	/// 2026-10-09（UI 打磨⑥）：**只做一次**的自我补色 —— 主题下发（<see cref="ApplyWindow" /> /
	/// <see cref="ApplySection" /> / <see cref="ApplyRow" />）之后才动态创建的下拉框（灯效组里的「单色」等）
	/// 在被创建时不会经过 <see cref="ApplyTree" />，于是从来没被上过色（这正是截图里"白色底 + 黑三角"的那一处）。
	/// RComboBox 在首绘时调本方法补一次；之后换主题由 <see cref="ApplyThemeMode" /> 那条路径（ApplyTree）重新下发。
	/// 用 <c>_themedControls</c> 表里存的**真实主题名**（"DarkMode_CFD"/"CFD"）标记「已按该主题上过色」，
	/// 主题没变时稳态零开销（2026-10-10 修：该标记曾被哨兵串覆盖，导致次次重下发，见 <see cref="StyleComboBox" />）。
	/// </summary>
	/// <returns>true ＝ 本帧刚刚补过色（调用方 <c>RComboBox.PaintFlat</c> 会因此放弃本帧绘制，见那里的说明）。</returns>
	internal static bool StyleComboBoxIfStale(ComboBox combo)
	{
		// 2026-10-10：这里的"已上过色"标记**就是** <see cref="ApplyDarkComboTheme" /> 存进 _themedControls
		// 的真实主题名（"DarkMode_CFD"/"CFD"）。原来的哨兵串 `FlatComboMarker` 已被
		// <see cref="StyleComboBox" /> 里那两行覆盖代码带崩（见那里的 2026-10-10 注释），本轮一并删除。
		string theme = ComboThemeFor(IsNightMode);
		if (_themedControls.TryGetValue(combo, out object value) && value is string text && text == theme)
		{
			return false;
		}
		StyleComboBox(combo);
		return true;
	}

	/// <summary>
	/// 2026-10-09（0.291 beta22 补丁）：**所有窗体的统一下拉配置入口** —— 递归遍历 <paramref name="root" />，
	/// 对每一颗 <see cref="ComboBox" /> 调一次唯一入口 <see cref="StyleComboBox" />。
	///
	/// 为什么必须有这个方法（＝用户报的"下拉依旧是旧版直角"的根因）：RComboBox 的外观虽然由自己整块自绘，
	/// 但"是不是我们的样式"取决于 <see cref="StyleComboBox" /> 有没有被调用过（FlatStyle / 底 / 边 / 箭头 /
	/// 展开列表主题）。此前它**只**在主设置窗的 <c>UiVisualStyle.ApplyTree</c> 里被调用，于是两类下拉漏网：
	///   ① 构建时机**晚于** <c>ApplyWindow</c> 的控件（灯效那几行、性能配置行等运行时动态创建的下拉）；
	///   ② **从不走** <c>ApplyWindow</c> 的次级窗体（「设置」对话框 SettingsDialog 的「语言」「日志级别」、
	///      键盘灯效 RgbForm 里后建的几颗、屏幕校色 CalibrationStartupForm 等）。
	/// 它们只能等首绘时的自愈（<see cref="StyleComboBoxIfStale" />），而那次自愈发生在 WM_PAINT **里面**：
	/// 改 <c>FlatStyle</c> 会让 <see cref="ComboBox" /> **重建 HWND** ⇒ 本帧 <c>BeginPaint</c> 拿到的裁剪区
	/// 随即失效 ⇒ 我们画的圆角整块被裁掉，屏幕上只剩系统原生绘制，也就是用户看到的
	/// **"方角 + 浅色渐变箭头格"的老式下拉**。
	/// 现在：① <see cref="RComboBox" /> 构造函数里就把 FlatStyle 定成 Flat（句柄尚未创建 ⇒ 不会重建）；
	/// ② 每个 <c>RForm</c> 在 OnLoad / OnShown 各跑一次本方法（此时子控件都已建好）⇒ 稳态下自愈永远不动手。
	/// ⚠️ 只改外观属性，**不碰** Items / SelectedIndex / 事件 / DropDownWidth / 尺寸 / 位置 / 命中区域。
	/// </summary>
	internal static void StyleAllCombos(Control root)
	{
		if (root == null)
		{
			return;
		}
		if (root is ComboBox combo)
		{
			StyleComboBox(combo);
		}
		foreach (Control child in root.Controls)
		{
			StyleAllCombos(child);
		}
	}

	private static void ApplyDarkComboTheme(ComboBox combo)
	{
		string text = ComboThemeFor(IsNightMode);
		if (_themedControls.TryGetValue(combo, out object value) && value is string text2 && text2 == text)
		{
			return;
		}
		_themedControls.AddOrUpdate(combo, text);
		if (combo.IsHandleCreated)
		{
			Apply();
			return;
		}
		combo.HandleCreated += (object? _, EventArgs _) =>
		{
			Apply();
		};
		void Apply()
		{
			try
			{
				SetWindowTheme(combo.Handle, ComboThemeFor(IsNightMode), null);
				// 2026-10-09（0.291 beta22 补丁⑥ 收口）：**改完窗口主题立刻按我们的自绘重绘一次**。
				// `SetWindowTheme` 会让系统用经典/主题样式重画一遍控件（用户看到的"首帧 2px 粗线，
				// 紧接着被刷成 1px 细线"就发生在这个时刻）⇒ 这里强制 `Invalidate`，让紧随其后的一次
				// `WM_PAINT` 走我们的 `RComboBox.PaintFlat`（2 逻辑像素圆角描边），把系统那一圈盖掉。
				combo.Invalidate();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("ComboBox theme failed: " + ex.Message);
			}
		}
	}

	internal static string ComboThemeFor(bool night)
	{
		if (!night)
		{
			return "CFD";
		}
		return "DarkMode_CFD";
	}

	internal static string? AppliedComboTheme(ComboBox combo)
	{
		if (!_themedControls.TryGetValue(combo, out object value) || !(value is string result))
		{
			return null;
		}
		return result;
	}

	private static void ComboBoxDrawItem(object? sender, DrawItemEventArgs e)
	{
		if (sender is ComboBox comboBox && e.Index >= 0)
		{
			bool flag = (e.State & DrawItemState.Selected) != 0;
			Color color = (flag ? Border : Input);
			Color foreColor;
			if (flag)
			{
				foreColor = Text;
			}
			else
			{
				foreColor = (comboBox.Enabled ? Text : Muted);
			}
			using (SolidBrush brush = new SolidBrush(color))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
			Rectangle bounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 4), e.Bounds.Height);
			TextRenderer.DrawText(e.Graphics, comboBox.GetItemText(comboBox.Items[e.Index]), comboBox.Font, bounds, foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
	}

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int SetWindowTheme(nint hWnd, string pszSubAppName, string? pszSubIdList);

	private static void ApplyExplorerTheme(Control control)
	{
		if (_themedNumeric.TryGetValue(control, out object _))
		{
			return;
		}
		_themedNumeric.Add(control, new object());
		if (control.IsHandleCreated)
		{
			Apply();
			return;
		}
		control.HandleCreated += (object? _, EventArgs _) =>
		{
			Apply();
		};
		void Apply()
		{
			string pszSubAppName = (IsNightMode ? "DarkMode_Explorer" : "Explorer");
			try
			{
				SetWindowTheme(control.Handle, pszSubAppName, null);
				foreach (Control control2 in control.Controls)
				{
					SetWindowTheme(control2.Handle, pszSubAppName, null);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("SetWindowTheme failed: " + ex.Message);
			}
		}
	}

	private static void SetContainerColor(Control root, Color color)
	{
		root.BackColor = color;
		foreach (Control control in root.Controls)
		{
			if (control is Panel)
			{
				SetContainerColor(control, color);
			}
		}
	}

	private static bool IsMuted(Color color)
	{
		int num = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
		int num2 = (color.R + color.G + color.B) / 3;
		if (num < 24)
		{
			if (num2 >= 90)
			{
				return num2 <= 205;
			}
			return false;
		}
		return false;
	}

	/// <summary><c>WM_ERASEBKGND</c>（0x14）。</summary>
	internal const int WmEraseBkgnd = 20;

	/// <summary><c>WM_NCPAINT</c>（0x85）。</summary>
	internal const int WmNcPaint = 133;

	/// <summary>
	/// 2026-10-09（P0「UI 自绘层地基」）：<b>自绘控件不许系统再画一遍</b>的唯一入口 ——
	/// 供所有自绘控件在自己的 <c>WndProc</c> 开头调用：
	/// <code>if (UiVisualStyle.HandleOpaqueChrome(this, ref m)) return;</code>
	///
	/// <para><b>它只做两件事，都是"别再画一层"，一个像素的颜色/几何都不改：</b></para>
	/// <list type="number">
	/// <item><b>接管 <c>WM_ERASEBKGND</c></b>：用该控件**所在位置真实的容器底色**
	/// （<see cref="ContextBackColor" />：沿父链取第一个实色容器的 <c>BackColor</c>）整块填充客户区并返回 1。
	/// 为什么不能用系统那一次擦除：<c>Control.WndProc</c> 的默认处理是用**控件自己的 <c>BackColor</c> 刷子**
	/// 去擦 —— 而自绘控件的 <c>BackColor</c> 是"控件自己的底色"、不是"它所在位置真正被画出来的底色"，
	/// 两者差几个数值时，被失效的那条带（常常只有几像素高，例如滚动/布局平移让出的一行）就会被擦成
	/// 一条**比周围略亮/略暗的淡线**，直到下一次完整重绘才消失 —— 这就是历史「开关上方偶发淡线」
	/// （仓库根目录 `UI-淡线问题-存档搁置.md`）与「整块异色矩形/白块」这一类问题的**同一个共因**。
	/// 现在那条带被擦成**父容器真色**，与周围逐分量相等 ⇒ 结构上不可见。
	/// ⚠️ 只填色、不改任何 Bounds/Margin/Region/命中区域/功能逻辑。</item>
	/// <item><b>吞掉 <c>WM_NCPAINT</c></b>：本项目所有自绘控件的非客户区**实测为 0 像素**
	/// （无 <c>BorderStyle</c>、<c>FlatStyle.Flat</c> + <c>BorderSize=0</c>），本来就没有属于它的像素要画；
	/// 而系统在某些时序（窗口激活、主题切换、按下/抬起状态变化）会沿**经典（非主题）**样式重描一圈方角 1px 框，
	/// 正好盖在我们的圆角描边上 ⇒ 观感就是"边先粗后细/突然变成方角"。吞掉它并触发一次客户区重绘
	/// （<c>WM_NCPAINT</c> 只触发 <c>WM_PAINT</c>，不会递归回到本方法）。</item>
	/// </list>
	///
	/// <para>⚠️ 刻意**不**在这里做任何其它事（不设 <c>ControlStyles.Opaque</c>、不动
	/// <c>PixelOffsetMode</c>、不改颜色/几何）—— P0 的目标只有一个：让系统那一层彻底不出现。</para>
	/// </summary>
	/// <returns>true ＝ 本消息已由我们处理完，调用方必须立刻 <c>return</c>（<b>不要</b>再调 <c>base.WndProc</c>）。</returns>
	internal static bool HandleOpaqueChrome(Control control, ref Message m)
	{
		if (control == null)
		{
			return false;
		}
		if (m.Msg == WmNcPaint)
		{
			// 非客户区：不画系统那圈方框；让客户区（我们的自绘）重绘一次即可。
			control.Invalidate();
			return true;
		}
		if (m.Msg == WmEraseBkgnd)
		{
			// 2026-10-09（0.291 beta22 补丁⑭；用户更正后的现象「首次是粗 刷新是细的」＋
			// 原话「我想可以补一个兜底就是刷新后立刻重绘一次把它这个盖掉」）：
			// ⭐ **`RComboBox` 这一支只声明"已处理"，绝不真的擦**。
			// 机制：`RComboBox.PaintFlat` 每一帧都会把**整个客户区**铺满（父容器真色 → 圆角填充 → 2 逻辑像素描边
			// → 箭头 → 文字），因此它**根本不需要任何背景擦除**；而"擦了却不马上重画"正是"首绘粗、刷新后细"的
			// 窗口期来源：启动后的校色状态刷新（`Items.Clear/Add` + 重设 `SelectedIndex`）会先送来一次
			// `WM_ERASEBKGND`，这里原先用 `ContextBackColor` **把整块客户区填平** ⇒ 把刚画好的圆角填充与
			// 2 逻辑像素描边整片擦掉（只剩最外 1 圈 AA 像素＝"细线"）；若紧随的 `WM_PAINT` 被裁小/延后，
			// 屏幕就停在"细"的样子。
			// 现在：不画任何像素，只返回 1 ⇒ **不存在"擦了没重画"的窗口**，刷新前后都由 `PaintFlat` 决定外观。
			// ⚠️ 只对 `RComboBox` 这样（它的自绘保证铺满整块）；其它控件（RButton/RCheckBox/…）仍按原样用
			// 容器底色擦除——它们的自绘只覆盖圆角路径，角部仍依赖这次擦除，贸然改会影响已定稿的观感。
			if (control is RComboBox)
			{
				m.Result = (IntPtr)1;
				return true;
			}
			// 用「该位置真实的容器底色」擦，而不是控件自己的 BackColor / 系统窗口刷子（白）。
			if (m.WParam != IntPtr.Zero)
			{
				using Graphics graphics = Graphics.FromHdc(m.WParam);
				using SolidBrush brush = new SolidBrush(ContextBackColor(control));
				graphics.FillRectangle(brush, control.ClientRectangle);
			}
			m.Result = (IntPtr)1;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 2026-10-09（P0.5「统一 DPI 与笔宽」）：<b>所有 1 逻辑像素级描边的唯一工厂</b>。
	/// 解决两件在旧代码里各写一遍的事：
	/// <list type="number">
	/// <item><b>DPI 来源统一</b>：宽度一律由**控件自己的 DPI**（<c>Control.DeviceDpi</c>，经
	/// <see cref="UiDpi.Paint(Control)" />）算，而**不再**用绘制 DC 的 <c>Graphics.DpiX</c> ——
	/// 后者在 PerMonitorV2 /「系统 DPI ≠ 显示器 DPI」时可能回 96，会把 4 逻辑像素的圆角算成 4 设备像素
	/// （175% 屏上只等于约 2.3 逻辑像素，看着就是直角），也会让同族控件的边宽各不相同。</item>
	/// <item><b>整数设备像素</b>：<c>round(逻辑宽 × DPI / 96)</c>（<see cref="MidpointRounding.AwayFromZero" />）
	/// ⇒ 100%→N、125%→round(1.25N)、150%→round(1.5N)、175%→round(1.75N)、200%→2N，
	/// 内外沿都落在整像素列/行上，不会出现"两列各 50%"的发虚灰边。</item>
	/// </list>
	///
	/// <para><b>已验证的同族一致性（本轮 6 个调用点，全部走本函数）</b>：
	/// 按钮族选中边与下拉框描边都传 2 逻辑像素 ⇒ 100%→2、125%→3、150%→3、175%→4、200%→4 设备像素
	/// （与改动前 <c>ModeOutlineWidth</c> / <c>ComboOutlineWidth</c> 的数值逐个相同）；
	/// 按钮未选中硬边与分段边、下拉 chevron 与输入族 (RTextBox / RNumericUpDown / RComboDisplay) 都传
	/// 1 逻辑像素 ⇒ 100%→1、125%→1、150%→2、175%→2、200%→2。</para>
	/// </summary>
	/// <param name="control">用于取 DPI 的控件（其 <c>DeviceDpi</c>）。</param>
	/// <param name="color">边色（语义色原样使用，本函数不做任何颜色替换）。</param>
	/// <param name="logicalWidth">**逻辑**像素线宽（1 = 普通 1px 边，2 = 按钮/下拉的选中边）。</param>
	/// <param name="minDevicePx">设备像素下限（普通边传 1、按钮族选中边传 2，与改动前一致）。</param>
	/// <param name="inset">是否用 <c>PenAlignment.Inset</c>（整条线落在控件内、直线段为实心硬边）。</param>
	internal static Pen OutlinePen(Control control, Color color, float logicalWidth, float minDevicePx = 1f, bool inset = true)
	{
		float dpi = UiDpi.Paint(control);
		float deviceWidth = Math.Max(minDevicePx, (float)Math.Round(logicalWidth * dpi / 96f, MidpointRounding.AwayFromZero));
		Pen pen = new Pen(color, deviceWidth);
		if (inset && deviceWidth > 1f)
		{
			pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
		}
		return pen;
	}

	/// <summary>
	/// 2026-10-09（P0.5）：<see cref="OutlinePen(Control, Color, float, float, bool)" /> 的
	/// <see cref="Graphics" /> 版 —— 只给**没有控件实例**的绘制路径用（外接覆盖层、审计离屏画布）。
	/// ⚠️ 宽度取自 <c>Graphics.DpiX</c>（经 <see cref="UiDpi.Paint(Graphics)" />，审计时优先用
	/// <c>UiDpi.AuditDpi</c>）；**界面控件一律用 Control 版**（见前者的说明）。
	/// </summary>
	internal static Pen OutlinePen(Graphics graphics, Color color, float logicalWidth, float minDevicePx = 1f, bool inset = true)
	{
		float dpi = UiDpi.Paint(graphics);
		float deviceWidth = Math.Max(minDevicePx, (float)Math.Round(logicalWidth * dpi / 96f, MidpointRounding.AwayFromZero));
		Pen pen = new Pen(color, deviceWidth);
		if (inset && deviceWidth > 1f)
		{
			pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
		}
		return pen;
	}

	/// <summary>
	/// 2026-10-09（exportperm）：设置窗里用的**紧凑次要按钮**（动作按钮，不是分段选择）。
	/// 尺寸规范照抄设置窗既有的紧凑按钮（SettingsForm.CreateCompactChoiceButton）：
	/// 高度钉死在 D(28)、圆角 12、左右内边距 D(10)、字号 8.5，宽度交给 AutoSize 按文案量。
	/// 描边取 Border 而不是强调色：它是普通动作按钮，没有"选中"语义。
	/// </summary>
	internal static RButton CreateCompactButton(string name, string text, Func<int, int> D, Padding margin)
	{
		RButton button = new RButton
		{
			Name = name,
			Text = text,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Height = D(28),
			MinimumSize = new Size(0, D(28)),
			// 宽度上限必须给 int.MaxValue：给 0 会被 Control 的尺寸约束当成"宽上限=0"。
			MaximumSize = new Size(int.MaxValue, D(28)),
			TextAlign = ContentAlignment.MiddleCenter,
			Cursor = Cursors.Hand,
			Font = Font(8.5f),
			Margin = margin,
			Padding = new Padding(D(10), 0, D(10), 0),
			BorderRadius = 12,
			BackColor = ButtonFace,
			BorderColor = Border,
			ForeColor = Text,
			Secondary = true
		};
		ApplySecondaryButton(button);
		button.BorderColor = Border;
		return button;
	}
}
