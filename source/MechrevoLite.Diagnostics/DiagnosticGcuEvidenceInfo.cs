using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-09（诊断补采）：把「装完不工作」定性最缺的四块证据一次性落进诊断包。
/// 背景（极光Pro23「GCU 连不上」那次，三样关键证据包里都没有，只能单独找机主要）：
///   ① GCU 装后自检结果 —— 安装脚本现在写 %ProgramData%\L-Mechrevo\gcu-install-status.json；
///   ② 安装日志（本类只补底座日志尾部，见下）；
///   ③ GCU 关键文件的大小/哈希/版本 —— 判断「补全的 40A 载荷（payload\40 覆盖层，2026-10-09 精简为 5 文件）到底装上没有」
///      （实测本机曾长期是旧的 3 文件混搭，而包里当时根本没有可比对的字段）。
/// 本类只做三件事，全部 **只读**：
///   [GCU 装后自检] —— 读 gcu-install-status.json（不存在就明说"未执行过装后自检或旧版本安装"；存在则给出解析字段 + 原文全文）；
///   [GCU 关键文件] —— 逐个列大小 + SHA256 前 16 位 + FileVersion，并给「40A 是否落地」的结论式提示；
///   [GCU 侧日志（ControlCenter / MyControlCenter）] —— 2026-10-09 第二批补采后：先列清单（含 installLog），
///     再取尾部（每文件 ≤64 KB、小节合计 ≤512 KB、每文件 ≤400 行），并对 LM_Manager/LM_Init/RGBKB/solution/
///     UNKNOWN/Keyboard/Subscribe/SUBACK/Mqtt/Error/Exception 做**关键行抽取**（命中就贴原文行），全程打码。
///     ⚠️ gcu-install-*.log 已移交 DiagnosticGcuInventoryInfo，不在本节重复采集。
/// ⚠️ 不新增任何 MQTT 订阅/GET、不连 13688、不启停服务、**不写任何文件**（不写 GCU 目录、不写 ProgramData）；
///    每项独立 try/catch，失败只写"该项读取失败（原因）"，单点失败绝不影响诊断包导出。
/// </summary>
internal static class DiagnosticGcuEvidenceInfo
{
	// 2026-10-09（第二批补采）：原来这里的「每文件 ≤200 行 / ≤32 KB」三个上限已搬到
	// DiagnosticGcuInventoryInfo（与安装日志共用同一份尾部渲染实现，口径只有一份）：
	// 现在是「每文件 ≤64 KB、[GCU 侧日志] 小节合计 ≤512 KB、每文件 ≤400 行」，并新增关键行抽取。

	/// <summary>GCU 安装脚本登记目录（gcu-install-status.json 所在目录）。</summary>
	private const string ProgramDataDir = @"C:\ProgramData\L-Mechrevo";

	private const string GcuRegistryKey = "SOFTWARE\\L-Mechrevo";

	/// <summary>旧 base（底座）GCUBridge.exe 大小（精确值）—— 只用来在结论里点名「这是旧底座」。</summary>
	private const long ExpectedBridgeOldBase = 682208L;

	/// <summary>
	/// 2026-10-09（75:00）：按变体选择的落地基线（原「40A 尺寸表硬编码 ⇒ 30 系机上必然误报」的修法）。
	/// <para>
	/// 基线来源 = 仓库里**真正的载荷树** `installer-src\files\GCU\payload\&lt;变体&gt;`（只读，逐文件 relpath+字节数），
	/// 由 `diagnostics\gcu-baseline-20261009\gen-gcu-payload-baseline.ps1` 生成到内嵌资源 <c>LMechrevo.gcu-payload-baseline.txt</c>；
	/// 只取 `AiStoneService\` 子树（安装脚本 robocopy 的就是这一层，根目录的 .bat/.txt 不进目标），排除 readme/notice/license。
	/// 装机侧的实际落盘集合 = robocopy(base `payload\GCU\AiStoneService`) 之后再 robocopy(所选 overlay) ——
	/// overlay 覆盖同名 base（见 Install-Gcu.ps1 的 Merge-GcuPayloadManifest 注释），所以本类按**同名 overlay 胜出**合并后再比对。
	/// </para>
	/// <para>
	/// ⚠️ 变体没有基线（例如没登记过 GcuVariant、或将来新增了基线未收录的变体）时**明确写「该变体无落地基线，跳过判定」**，
	/// 绝不再拿 40A 的表去比 ⇒ 不再误报。
	/// ⚠️ 40A = 40 系覆盖层（payload\40，5 文件；2026-10-09 精简后重生成基线）、40B = payload\40B（10 文件：桥 + ACPIDriverDll + GPUInfoDLL 等）、
	///    30 = payload\30（163 文件）、50 = payload\50（4 文件）；这些数字全部由基线文件本身给出，不在代码里写死。
	/// </para>
	/// </summary>
	private static IReadOnlyDictionary<string, Dictionary<string, long>>? _baselineByVariant;

	private static string? _baselineLoadError;

	private const string BaselineResourceName = "LMechrevo.gcu-payload-baseline.txt";

	/// <summary>基线里「AiStoneService\」前缀（载荷树里一律带这一层）。</summary>
	private const string BaselineRootPrefix = "AiStoneService\\";

	/// <summary>
	/// 读内嵌基线并解析成 变体 -&gt; (相对路径 -&gt; 字节数)。
	/// 变体键：'GCU'（base）、'30'、'40'、'40B'、'50'；相对路径已去掉「AiStoneService\」前缀，与 <see cref="ExpectedKeyFileSizes"/> 同一口径。
	/// </summary>
	private static IReadOnlyDictionary<string, Dictionary<string, long>> BaselineByVariant(out string? error)
	{
		error = null;
		if (_baselineByVariant != null || _baselineLoadError != null)
		{
			error = _baselineLoadError;
			return _baselineByVariant ?? new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
		}
		Dictionary<string, Dictionary<string, long>> result = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using Stream stream = typeof(DiagnosticGcuEvidenceInfo).Assembly.GetManifestResourceStream(BaselineResourceName);
			if (stream == null)
			{
				_baselineLoadError = "内嵌资源 " + BaselineResourceName + " 缺失（打包时漏了 EmbeddedResource）";
				return result;
			}
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			string current = "";
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				string text = line.Trim();
				if (text.Length == 0 || text.StartsWith('#'))
				{
					continue;
				}
				if (text.StartsWith("VAR=", StringComparison.Ordinal))
				{
					int num = text.IndexOf(' ');
					current = ((num > 4) ? text.Substring(4, num - 4) : text.Substring(4)).Trim();
					result[current] = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
					continue;
				}
				if (current.Length == 0)
				{
					continue;
				}
				string[] array = text.Split('\t');
				if (array.Length >= 2 && long.TryParse(array[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
				{
					string text2 = array[1].Trim();
					if (text2.StartsWith(BaselineRootPrefix, StringComparison.OrdinalIgnoreCase))
					{
						text2 = text2.Substring(BaselineRootPrefix.Length);
					}
					result[current][text2] = size;
				}
			}
		}
		catch (Exception ex)
		{
			_baselineLoadError = "内嵌基线解析失败：" + ex.Message;
		}
		_baselineByVariant = result;
		return result;
	}

	/// <summary>
	/// 按变体解析「这台机器应该有哪些文件、各多大」：base(`payload\GCU`) 打底，所选 overlay 同名覆盖。
	/// 返回 null = 该变体没有基线（调用方必须写「无落地基线，跳过判定」，不许回落 40A）。
	/// <para>⛔ <b>故意不并 40 / 40B 家族</b>（2026-10-09「40B≡40」那批）：40 与 40B 是**不同的载荷字节集**
	/// （payload\40 = 5 文件、payload\40B = 10 文件），行为判断等价（键盘灯走 40 系协议，见
	/// <c>MechrevoLite.Hardware.GcuVariantFamily</c>）**不等于**载荷身份等价 —— 这里必须按注册表原值精确取表：
	/// <c>GcuVariant=40B</c> 用 40B 的基线，绝不回落到 40 的表，反之亦然。</para>
	/// </summary>
	private static Dictionary<string, long>? BaselineForVariant(string variant, out string reason)
	{
		reason = "";
		IReadOnlyDictionary<string, Dictionary<string, long>> readOnlyDictionary = BaselineByVariant(out string error);
		if (!string.IsNullOrWhiteSpace(error))
		{
			reason = error;
			return null;
		}
		string text = (variant ?? "").Trim();
		if (text.Length == 0)
		{
			reason = "注册表没有 GcuVariant（旧版安装器未登记）";
			return null;
		}
		if (!readOnlyDictionary.TryGetValue("GCU", out Dictionary<string, long> value))
		{
			reason = "内嵌基线缺 base（payload\\GCU）一档";
			return null;
		}
		Dictionary<string, long> dictionary = new Dictionary<string, long>(value, StringComparer.OrdinalIgnoreCase);
		bool flag = false;
		foreach (string key in readOnlyDictionary.Keys)
		{
			if (key.Equals("GCU", StringComparison.OrdinalIgnoreCase) || !key.Equals(text, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			foreach (KeyValuePair<string, long> item in readOnlyDictionary[key])
			{
				dictionary[item.Key] = item.Value;
			}
			flag = true;
		}
		reason = (flag ? ("GcuVariant=" + text + " 的载荷基线") : ("GcuVariant=" + Mask(text) + " 没有对应载荷分档（腰线只叠在 base 上）"));
		return dictionary;
	}

	/// <summary>无 BOM 感知的 UTF-8 读取（日志可能是 GBK，认不出的字节退化成 U+FFFD，仅取尾部不影响判定）。</summary>
	private static readonly UTF8Encoding Utf8Loose = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

	private static readonly Regex GuidLike = new Regex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}", RegexOptions.CultureInvariant);
	private static readonly Regex LongHexLike = new Regex("\\b[0-9A-Fa-f]{16,}\\b", RegexOptions.CultureInvariant);
	private static readonly Regex MacLike = new Regex("\\b[0-9A-Fa-f]{2}([-:][0-9A-Fa-f]{2}){5}\\b", RegexOptions.CultureInvariant);
	private static readonly Regex UsersPath = new Regex("(?i)([A-Za-z]:\\\\Users\\\\)([^\\\\\\s\"']+)", RegexOptions.CultureInvariant);

	/// <summary>
	/// 2026-10-09（75:00）新修：**文件名里夹的序列号**。
	/// 真机实测形态：ICC 文件名 `&lt;PnP 设备 id&gt;_&lt;显示器 EDID 序列号&gt;.icm`（例如 `…_M24M4B0000010.icm`），
	/// 我们的自检恰好报了这一处「疑似漏打码」（因为裸序列号是从文件名里长出来的，不在已有规则覆盖内）。
	/// 规则只动**扩展名紧邻的那一段**（`_`/`-`/`.` 之后到 `.icm`/`.icc` 之前，含字母且不含路径分隔符），
	/// 纯数字的 PnP 段（例如 `DIS_1234` 里的 `1234`）因为「必须含字母」不会被吞；
	/// 路径主干与机型名一律不碰 —— 宁可少打，也不能把证据路径打烂。
	/// 2026-10-09（批次3 #3「打码放宽」）：扩展名紧邻那一段若是**面板号**（`BOE0B40` 这类，判据见
	/// <see cref="PanelIdentifiers"/>）则**不打码** —— 机主确认 `BOE0B40_*.icm` 里的 `BOE0B40` 是面板型号，
	/// 不是序列号（详见 <see cref="MaskSerialsInFilenames"/> 的替换委托）。
	/// </summary>
	private static readonly Regex SerialTailInFilename = new Regex(
		"(?<=[_.-])(?=[0-9A-Za-z-]*[A-Za-z])[0-9A-Za-z][0-9A-Za-z-]{5,}(?=\\.(?:icm|icc)\\b)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


	/// <summary>
	/// 「机器名\用户名」形态（安装日志的 ACL 行就会写这个）：整体打码，别把机主名带出包。
	/// ⚠️ 这条规则极难写对（实测踩过两种误伤：把 "Files\L-Mechrevo"、"ProgramData\L-Mechrevo" 当域名吞掉）。
	/// 现做法：先把已知的系统路径分量用哨兵 &#92;u0001 包起来 → 再按否定字符类排除盘符/路径形态 → 最后还原哨兵。
	/// 2026-10-09（第二批补采实测再修两处误伤）：
	///   ① 否定字符类里追加反斜杠 —— 否则**系统路径里的普通两级目录**会被当域名吞掉，例如
	///      `…\ControlCenter\installLog\install.log`、`…\GCU\AiStoneService`、`…\MyControlCenter\UserPofiles`
	///      （这些正是本次要列出的证据路径）；
	///   ② 否定字符类由 `A-Za-z0-9` 改成 `\p{L}\p{N}` —— 原来只排除 ASCII，中文机器名会被从**中间**咬断：
	///      实测 `…\OEM\机械革命控制中心\AiStoneService\…` 被打成 `…\OEM\机<机器名\用户名已打码>\…`
	///      （前缀多出一个「机」，路径证据再也读不通）。
	/// 真机日志里的 ACL 形态 `机器名\用户名` 前面是空格或冒号，两项修改都不影响它（实测仍打码）。
	/// 2026-10-09（75:00 再修）：机主实测**这一形态仍漏**（真实主机名没被打掉）。原因 = 机名判定太窄：
	/// 原判据「含字母且不含 '-'」会把 `DESKTOP-A1B2C3`（带连字符）、`WINDOWS-8K3L9` 这类**标准 Windows 机名**
	/// 直接放过。现判据：① 名字在**本机已知标识表**里（见 SensitiveValues）就一定要打；
	/// ② 否则退回原形状判据；用户名一侧仍要求「不含 '-'、不含数字」，宁少勿滥伤路径。
	/// </summary>
	private static readonly Regex MachineUser = new Regex(
		"(?<![\\p{L}\\p{N}_:$\\\\\\u0001])([\\p{L}\\p{N}_$.-]{2,})\\\\([\\p{L}\\p{N}_.$-]{2,})",
		RegexOptions.CultureInvariant);

	/// <summary>要保护的已知系统路径分量（不是机器名\用户名），用哨兵包住后再做域名识别。</summary>
	private static readonly Regex ProtectedPathToken = new Regex(
		"(?i)(Program Files|ProgramData|ControlCenter|Windows|System32|SysWOW64|Users|AppData|MechrevoLite|L-Mechrevo|Mechrevo|Microso?ft|NVIDIA|Intel|AMD|PowerShell|PowerShell_ISE|SOFTWARE|HARDWARE|SYSTEM|CurrentControlSet|OEM|GamingCenter)",
		RegexOptions.CultureInvariant);

	private const char MaskSentinel = '\u0001';
	private static readonly Regex SerialLike = new Regex("(?i)(Serial(Number)?|SN|序列号)(\\s*[:=]\\s*|\"\\s*:\\s*\")([A-Za-z0-9]{6,})", RegexOptions.CultureInvariant);
	private static readonly Regex UserNameLine = new Regex("(?i)(user(name)?|登录用户|用户名)(\\s*[:=]\\s*)([^\\s,;]+)", RegexOptions.CultureInvariant);

	public static string Build()
	{
		StringBuilder sb = new StringBuilder();
		AppendInstallStatus(sb);
		AppendKeyFiles(sb);
		AppendItemSupportReadiness(sb);
		AppendBaseLogTail(sb);
		AppendKeyboardManagerFiles(sb);
		return sb.ToString();
	}

	// ================================================================ ① 装后自检结果

	private static void AppendInstallStatus(StringBuilder sb)
	{
		sb.AppendLine("[GCU 装后自检]");
		sb.AppendLine("（只读 %ProgramData%\\L-Mechrevo\\gcu-install-status.json —— 由 GCU 安装脚本装后自检写入）");
		try
		{
			string path = Path.Combine(ProgramDataDir, "gcu-install-status.json");
			if (!File.Exists(path))
			{
				sb.AppendLine("  状态文件: 不存在（未执行过装后自检或旧版本安装）");
				sb.AppendLine("  期望路径: " + Mask(path));
				sb.AppendLine();
				return;
			}
			FileInfo info = new FileInfo(path);
			sb.AppendLine($"  状态文件: 存在（{Mask(path)}；{info.Length} 字节；mtime={info.LastWriteTime:yyyy-MM-dd HH:mm:ss}）");
			string text;
			try
			{
				text = ReadWholeText(path, 65536);
			}
			catch (Exception ex)
			{
				sb.AppendLine("  内容: 该项读取失败（" + ex.Message + "）");
				sb.AppendLine();
				return;
			}
			string status;
			string reason;
			string updated;
			List<string> checks;
			if (TryParseStatusJson(text, out status, out reason, out updated, out checks))
			{
				sb.AppendLine("  Status    = " + Or(status));
				sb.AppendLine("  Reason    = " + Or(reason));
				sb.AppendLine("  UpdatedUtc= " + Or(updated));
				sb.AppendLine($"  Checks    = 共 {checks.Count} 项" + ((checks.Count == 0) ? "（数组为空）" : "："));
				foreach (string check in checks)
				{
					sb.AppendLine("    - " + check);
				}
			}
			else
			{
				sb.AppendLine("  内容: JSON 解析失败（下面给出原文前 1200 字符，供人工判读）");
				sb.AppendLine("  ---8<---");
				foreach (string line in text.Substring(0, Math.Min(text.Length, 1200)).Split('\n'))
				{
					sb.AppendLine("  " + Mask(line.TrimEnd('\r')));
				}
				sb.AppendLine("  --->8---");
			}
			// 2026-10-09（第二批补采）：用户要求「存在就全文」—— 该文件很小（实测 1.5 KB），
			// 解析器万一漏字段也能人工判读；⚠️ 只在这里贴一次，DiagnosticGcuInventoryInfo 那节只做指引、不重复。
			sb.AppendLine($"  原文全文（{text.Length} 字符，已打码）：");
			sb.AppendLine("  ---8<--- 全文开始");
			int lineNo = 0;
			foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
			{
				lineNo++;
				sb.AppendLine("  " + lineNo.ToString("D3") + "| " + Mask(rawLine));
			}
			sb.AppendLine("  --->8--- 全文结束");
		}
		catch (Exception ex2)
		{
			sb.AppendLine("  该项读取失败（" + ex2.Message + "）");
		}
		sb.AppendLine();
	}

	/// <summary>
	/// 极简 JSON 读取：只认安装脚本实际写出的有序结构
	/// {Status,Reason,UpdatedUtc,Checks:[{Name,Ok,Detail}]}；不引第三方库、不猜键名。
	/// </summary>
	private static bool TryParseStatusJson(string text, out string status, out string reason, out string updated, out List<string> checks)
	{
		status = "";
		reason = "";
		updated = "";
		checks = new List<string>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		status = JsonString(text, "Status");
		reason = JsonString(text, "Reason");
		updated = JsonString(text, "UpdatedUtc");
		foreach (Match match in Regex.Matches(text, "\\{[^{}]*\\}", RegexOptions.CultureInvariant))
		{
			string body = match.Value;
			string name = JsonString(body, "Name");
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}
			string ok = JsonRaw(body, "Ok");
			string detail = JsonString(body, "Detail");
			checks.Add($"Name={name}  Ok={Or(ok)}  Detail=\"{Truncate(Mask(detail), 220)}\"");
		}
		return !string.IsNullOrWhiteSpace(status) || checks.Count > 0;
	}

	private static string JsonString(string text, string key)
	{
		Match match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return "";
		}
		return Regex.Unescape(match.Groups[1].Value);
	}

	private static string JsonRaw(string text, string key)
	{
		Match match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*([^,}\\r\\n]+)", RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value.Trim().Trim('"');
	}

	// ================================================================ ② GCU 关键文件（大小/哈希/版本 + 40A 落地判定）

	private static void AppendKeyFiles(StringBuilder sb)
	{
		sb.AppendLine("[GCU 关键文件（大小 + SHA256 前 16 位 + FileVersion）]");
		string root = null;
		string rootReason;
		try
		{
			root = ResolveGcuRoot(out rootReason);
		}
		catch (Exception ex)
		{
			rootReason = "该项读取失败（" + ex.Message + "）";
		}
		if (string.IsNullOrWhiteSpace(root))
		{
			sb.AppendLine("  底座目录: 取不到（" + Or(rootReason) + "）");
			sb.AppendLine();
			return;
		}
		sb.AppendLine("  底座目录: " + Mask(root) + "（" + Or(rootReason) + "）");
		sb.AppendLine("  注册表登记: GcuVariant / GcuServiceDir / GcuServiceExe 见「配置与注册表」一节第 ③ 小节（此处不重复）。");
		// ⚠️ 两种实际布局都要认（本机实测是 B）：
		//   A) <root>\AiStoneService\GCUBridge.exe + <root>\AiStoneService\MyControlCenter\…
		//   B) <root>\GCUBridge.exe          + <root>\MyControlCenter\…（注册表 GcuServiceExe 指到的就是 AiStoneService 这一层）
		bool nested = File.Exists(Path.Combine(root, "AiStoneService\\GCUBridge.exe")) || !File.Exists(Path.Combine(root, "GCUBridge.exe"));
		string prefix = (nested ? "AiStoneService\\" : "");
		sb.AppendLine("  布局判定: " + (nested ? "A（<root>\\AiStoneService\\…）" : "B（GCUBridge.exe 与 MyControlCenter 直接位于 <root>）"));
		// 2026-10-09（75:00）：基线按变体选（原来无论什么机型都拿 40A 的表去比 ⇒ 30/50 系机上必然报「疑似未装」）。
		ReadRegistryStrings(out string _, out string variant, out string variantError);
		if (!string.IsNullOrWhiteSpace(variantError))
		{
			sb.AppendLine("  变体登记: GcuVariant 读取失败（" + variantError + "）");
		}
		Dictionary<string, long> baseline = BaselineForVariant(variant, out string baselineReason);
		string[] leaves;
		if (baseline != null)
		{
			sb.AppendLine($"  落地基线: {baselineReason}（{baseline.Count} 个相对路径；与实际落盘集合比对，内容不逐字节比对）");
			leaves = baseline.Keys.ToArray();
			Array.Sort(leaves, StringComparer.OrdinalIgnoreCase);
		}
		else
		{
			// ⚠️ 没有基线就**只列现状、不下结论**：绝不再拿 40A 的尺寸表硬套（这就是原来的误报来源）。
			sb.AppendLine("  落地基线: 该变体无落地基线，跳过判定（" + (string.IsNullOrWhiteSpace(variant) ? "注册表没有 GcuVariant" : "GcuVariant=" + Mask(variant)) + "）");
			if (!string.IsNullOrWhiteSpace(baselineReason))
			{
				sb.AppendLine("    基线说明: " + baselineReason);
			}
			leaves = KeyLeavesForDisplay(root, prefix);
			sb.AppendLine($"  （下面只列 {leaves.Length} 个常见键文件的存在性/大小/版本，供人工判断，不做「是否落地」结论。）");
		}
		int missing = 0;
		int matched = 0;
		int sizeMismatch = 0;
		int mutable = 0;
		int total = 0;
		foreach (string leaf in leaves)
		{
			string relative = prefix + leaf;
			string full = Path.Combine(root, relative);
			bool flag = baseline != null && IsRuntimeMutable(leaf);
			if (baseline != null && !flag)
			{
				total++;
			}
			sb.AppendLine("  " + ((nested || leaf.IndexOf('\\') < 0) ? relative : leaf));
			try
			{
				if (!File.Exists(full))
				{
					sb.AppendLine("    缺失");
					missing++;
					continue;
				}
				FileInfo info = new FileInfo(full);
				long expectedSize;
				if (baseline != null && baseline.TryGetValue(leaf, out expectedSize))
				{
					if (flag)
					{
						// 运行期会被 GCU 改写的文件（用户档案 / 风扇表 / 液冷标识）：只报现值，**不参与判定**，
						// 否则「装完正常用几天」的机器必然报一堆「大小不符」= 新的误报。
						mutable++;
						sb.AppendLine($"    （运行期可变，不计入落地判定）现值 {info.Length} 字节 / 出厂 {expectedSize} 字节");
					}
					else if (info.Length == expectedSize)
					{
						matched++;
					}
					else
					{
						sizeMismatch++;
						sb.AppendLine($"    ⚠️ 大小与原版基线不一致：实际 {info.Length} 字节 / 基线 {expectedSize} 字节");
					}
				}
				string version = "(无版本信息)";
				try
				{
					System.Diagnostics.FileVersionInfo vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(full);
					version = Or(vi.FileVersion ?? vi.ProductVersion);
				}
				catch (Exception ex)
				{
					version = "该项读取失败（" + ex.Message + "）";
				}
				string hash = "(哈希计算失败)";
				try
				{
					hash = ShortSha256(full);
				}
				catch (Exception ex2)
				{
					hash = "(哈希计算失败：" + ex2.Message + ")";
				}
				sb.AppendLine($"    大小={info.Length} 字节  SHA256前16位={hash}  FileVersion={version}");
			}
			catch (Exception ex3)
			{
				sb.AppendLine("    该项读取失败（" + ex3.Message + "）");
			}
		}
		AppendLandedVerdict(sb, root, variant, baseline, matched, missing, sizeMismatch, leaves.Length);
		sb.AppendLine();
	}

	/// <summary>
	/// 这些相对路径在 GCU 装完之后的**运行期会被改写**（不是安装问题），必须从「载荷是否落地」的大小比对里排除，
	/// 否则会误报「大小不符」。真机实测（本机 GcuVariant=40）：`UserFanTables\M3T1.json` 3017→3006、
	/// `UserPofiles\Mode1_Profile1.json` 1551→1634、`LiquidHWOC\LiquidHWOC.json` 151→155（液冷设备标识），
	/// 共 14 处「大小不符」全部落在这三类里，二进制一个都没错。
	/// </summary>
	private static bool IsRuntimeMutable(string relPath)
	{
		return relPath.StartsWith("MyControlCenter\\UserPofiles\\", StringComparison.OrdinalIgnoreCase)
			|| relPath.StartsWith("MyControlCenter\\UserFanTables\\", StringComparison.OrdinalIgnoreCase)
			|| relPath.Equals("MyControlCenter\\LiquidHWOC\\LiquidHWOC.json", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 没有基线时给人工看的「常见键文件」清单：内嵌 base 载荷表里，这台机器**实际存在**的那些（最多 20 个）；
	/// 一个都没有就回落到最基础的 5 个桥/服务文件（全部会显示「缺失」，不会下结论）。
	/// </summary>
	private static string[] KeyLeavesForDisplay(string root, string prefix)
	{
		List<string> list = new List<string>();
		if (BaselineByVariant(out string _).TryGetValue("GCU", out Dictionary<string, long> value))
		{
			string[] array = value.Keys.ToArray();
			Array.Sort(array, StringComparer.OrdinalIgnoreCase);
			foreach (string text in array)
			{
				if (list.Count >= 20)
				{
					break;
				}
				if (File.Exists(Path.Combine(root, prefix + text)))
				{
					list.Add(text);
				}
			}
		}
		if (list.Count == 0)
		{
			list.Add("GCUBridge.exe");
			list.Add("MyControlCenter\\GCUService.exe");
			list.Add("MyControlCenter\\GPUInfoDLL.dll");
			list.Add("MyControlCenter\\NVControlSetting.dll");
			list.Add("MyControlCenter\\AppSettings\\GamingMonitor.json");
		}
		return list.ToArray();
	}

	/// <summary>把「载荷是否落地」压成一句结论式文案（2026-10-09 75:00：改为按变体基线）。</summary>
	private static void AppendLandedVerdict(StringBuilder sb, string root, string variant, Dictionary<string, long>? baseline, int matched, int missing, int sizeMismatch, int total)
	{
		try
		{
			string bridge = Path.Combine(root, "AiStoneService\\GCUBridge.exe");
			if (!File.Exists(bridge))
			{
				bridge = Path.Combine(root, "GCUBridge.exe");
			}
			string gamingMonitor = Path.Combine(root, "AiStoneService\\MyControlCenter\\AppSettings\\GamingMonitor.json");
			if (!File.Exists(gamingMonitor))
			{
				gamingMonitor = Path.Combine(root, "MyControlCenter\\AppSettings\\GamingMonitor.json");
			}
			long bridgeSize = File.Exists(bridge) ? new FileInfo(bridge).Length : -1L;
			long jsonSize = File.Exists(gamingMonitor) ? new FileInfo(gamingMonitor).Length : -1L;
			string text = (string.IsNullOrWhiteSpace(variant) ? "(未登记)" : Mask(variant));
			if (baseline == null)
			{
				sb.AppendLine($"  [载荷落地] GcuVariant={text} ⇒ 该变体无落地基线，跳过判定（不比对、不报「疑似未装」）。");
				sb.AppendLine((bridgeSize < 0) ? "  [载荷落地] GCUBridge.exe 缺失" : $"  [载荷落地] GCUBridge.exe 实际 {bridgeSize} 字节（仅现状，无基线可比）");
				sb.AppendLine((jsonSize < 0) ? "  [载荷落地] GamingMonitor.json 缺失" : $"  [载荷落地] GamingMonitor.json 实际 {jsonSize} 字节（仅现状，无基线可比）");
				AppendRegistryExpectation(sb);
				return;
			}
			sb.AppendLine($"  [载荷落地] GcuVariant={text} 按本变体基线逐个大小比对：{matched}/{total} 与原版一致，缺失 {missing}，大小不符 {sizeMismatch}"
				+ ((matched == total) ? $" ⇒ {total} 个文件大小全对（内容未逐字节比对）" : ""));
			string verdict;
			if (bridgeSize < 0)
			{
				verdict = "[载荷落地] GCUBridge 缺失 ⇒ 无法判定";
			}
			else
			{
				long value;
				if (baseline.TryGetValue("GCUBridge.exe", out value) && bridgeSize == value)
				{
					verdict = $"[载荷落地] 疑似已装（GCUBridge 与本变体基线一致，{bridgeSize} 字节）";
				}
				else if (bridgeSize == ExpectedBridgeOldBase)
				{
					verdict = (baseline.TryGetValue("GCUBridge.exe", out value)
						? $"[载荷落地] 疑似未装（GCUBridge 为旧 base 版 {ExpectedBridgeOldBase} 字节，本变体基线为 {value} 字节）"
						: $"[载荷落地] 疑似未装（GCUBridge 为旧 base 版 {ExpectedBridgeOldBase} 字节，本变体基线里没有 GCUBridge.exe）");
				}
				else
				{
					verdict = (baseline.TryGetValue("GCUBridge.exe", out value)
						? $"[载荷落地] 疑似未装（GCUBridge 为 {bridgeSize} 字节，本变体基线为 {value} 字节）"
						: $"[载荷落地] 疑似未装（GCUBridge 为 {bridgeSize} 字节，本变体基线里没有 GCUBridge.exe）");
				}
			}
			sb.AppendLine("  " + verdict);
			if (baseline.TryGetValue("MyControlCenter\\AppSettings\\GamingMonitor.json", out long value2))
			{
				sb.AppendLine("  [载荷落地] GamingMonitor.json " + JsonSizeText(jsonSize, value2) + $"（本变体基线为 {value2} 字节）");
			}
			else
			{
				sb.AppendLine((jsonSize < 0) ? "  [载荷落地] GamingMonitor.json 缺失（本变体基线里没有这个文件）" : $"  [载荷落地] GamingMonitor.json 实际 {jsonSize} 字节（本变体基线里没有这个文件）");
			}
			AppendRegistryExpectation(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("  [载荷落地] 该项读取失败（" + ex.Message + "）");
		}
	}

	/// <summary>注册表里安装器登记的期望值（GcuVariant / GcuPayloadSha256）——与落地判定分开，两处共用。</summary>
	private static void AppendRegistryExpectation(StringBuilder sb)
	{
		ReadRegistryStrings(out string registryHash, out string registryVariant, out string registryError);
		if (!string.IsNullOrWhiteSpace(registryError))
		{
			sb.AppendLine("  注册表期望值: 该项读取失败（" + registryError + "）");
			return;
		}
		if (string.IsNullOrWhiteSpace(registryHash))
		{
			sb.AppendLine("  注册表期望值: HKLM\\SOFTWARE\\L-Mechrevo\\GcuPayloadSha256 不存在（旧版安装器未登记）");
			return;
		}
		string shortHash = (registryHash.Length > 16) ? (registryHash.Substring(0, 16) + "…") : registryHash;
		sb.AppendLine($"  注册表期望值: GcuPayloadSha256={shortHash} GcuVariant={Or(registryVariant)}");
		sb.AppendLine("    ⇒ 注意：该值是安装器登记的**整份载荷清单哈希（全部文件）**，不是单个 GCUBridge.exe 的哈希，"
			+ "所以它与下面单个文件的 SHA256 前 16 位**天然不会相等**；逐个文件的比对见 tools\\诊断-GCU连不上.ps1 第 ⑤ 节。");
	}


	private static string JsonSizeText(long size, long expected)
	{
		if (size < 0)
		{
			return "缺失";
		}
		if (size == expected)
		{
			return $"={size} 字节（与基线一致）";
		}
		return $"={size} 字节（与基线 {expected} 字节不一致）";
	}

	private static void ReadRegistryStrings(out string payloadSha256, out string variant, out string error)
	{
		payloadSha256 = "";
		variant = "";
		error = null;
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(GcuRegistryKey);
			if (key == null)
			{
				error = "键 HKLM\\" + GcuRegistryKey + " 不存在";
				return;
			}
			payloadSha256 = (key.GetValue("GcuPayloadSha256") as string)?.Trim() ?? "";
			variant = (key.GetValue("GcuVariant") as string)?.Trim() ?? "";
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
	}

	/// <summary>
	/// 推导 GCU 底座根目录（即「AiStoneService / MyControlCenter / …」的父目录）。
	/// 注册表 GcuServiceExe 指向 …\AiStoneService\GCUBridge.exe，GcuServiceDir 可能是绝对路径、
	/// 也可能只是子目录名（本机实测就是 "AiStoneService"）；两者都只是**线索**，
	/// 真正的判据是「该候选下 AiStoneService\GCUBridge.exe 或 MyControlCenter\GCUService.exe 存在」，
	/// 逐个候选实测后再采用，避免像早期版本那样把父目录猜错、整节全报"缺失"。
	/// ⚠️ internal：DiagnosticGcuInventoryInfo（UserPofiles 清单 / 底座目录树）复用同一份推导，绝不各写一套。
	/// </summary>
	internal static string ResolveGcuRoot(out string reason)
	{
		reason = "";
		string serviceExe = "";
		string serviceDir = "";
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(GcuRegistryKey);
			if (key != null)
			{
				serviceExe = (key.GetValue("GcuServiceExe") as string)?.Trim().Trim('"') ?? "";
				serviceDir = (key.GetValue("GcuServiceDir") as string)?.Trim().Trim('"') ?? "";
			}
		}
		catch (Exception ex)
		{
			reason = "注册表读取失败（" + ex.Message + "），";
		}
		List<string> roots = new List<string>();
		AddRoot(roots, AppContext.BaseDirectory);
		string serviceExeDir = null;
		if (serviceExe.Length > 0 && Path.IsPathRooted(serviceExe))
		{
			try
			{
				serviceExeDir = Path.GetDirectoryName(serviceExe);
			}
			catch (Exception ex2)
			{
				reason += "GcuServiceExe 推导失败（" + ex2.Message + "），";
			}
		}
		else if (serviceExe.Length > 0)
		{
			reason += "GcuServiceExe 不是绝对路径（" + Mask(serviceExe) + "），";
		}
		else
		{
			reason += "注册表无 GcuServiceExe，";
		}
		if (!string.IsNullOrWhiteSpace(serviceExeDir))
		{
			// 先试父目录：GcuServiceExe 指向 …\AiStoneService\GCUBridge.exe 时，底座根目录是 AiStoneService 的父目录。
			AddRoot(roots, ParentDir(serviceExeDir));
			AddRoot(roots, serviceExeDir.TrimEnd('\\'));
		}
		if (serviceDir.Length > 0)
		{
			if (Path.IsPathRooted(serviceDir))
			{
				AddRoot(roots, serviceDir.TrimEnd('\\'));
				AddRoot(roots, ParentDir(serviceDir));
			}
			else
			{
				string leaf = serviceDir.Trim('\\');
				AddRoot(roots, Path.Combine(@"C:\Program Files\L-Mechrevo\GCU", leaf));
				AddRoot(roots, @"C:\Program Files\L-Mechrevo\GCU");
				reason += $"GcuServiceDir=\"{Mask(serviceDir)}\" 只是子目录名（非绝对路径），";
			}
		}
		else
		{
			reason += "注册表无 GcuServiceDir，";
		}
		foreach (string root in roots)
		{
			string probe = Path.Combine(root, "MyControlCenter\\GCUService.exe");
			if (File.Exists(probe))
			{
				reason += "且该目录下有 MyControlCenter\\GCUService.exe ⇒ 采用";
				return root;
			}
		}
		foreach (string root2 in roots)
		{
			if (File.Exists(Path.Combine(root2, "GCUBridge.exe")))
			{
				reason += "且该目录下有 GCUBridge.exe（无 MyControlCenter 子目录：40A 覆盖层疑似未落地）⇒ 采用";
				return root2;
			}
		}
		if (roots.Count > 0)
		{
			reason += "但候选目录下都没有 MyControlCenter\\GCUService.exe / GCUBridge.exe ⇒ 按首个候选列出（下面各项会显示「缺失」）";
			return roots[0];
		}
		reason += "没有任何候选目录 ⇒ 底座目录取不到";
		return null;
	}

	/// <summary>
	/// 2026-10-09（75:00）：已知的「敏感裸值」登记处 —— 打码与打码自检**共用同一份**，
	/// 保证「自检查什么，Mask 就盖什么」，不会再出现「自检报漏打码、而 Mask 根本不知道有这个值」。
	/// <para>
	/// 现状：① 真实主机名 / 用户名（<see cref="DiagnosticGcuInventoryInfo"/> 的隐私自检登记）；
	/// ② 显示器 EDID 序列号（<see cref="DiagnosticDisplayInfo"/> 采集到的 WmiMonitorID SerialNumberID ——
	/// 真机实测它就出现在 ICC 文件名 `&lt;PnP&gt;_&lt;序列号&gt;.icm` 里，而 2026-10-09 的真机包里自检报了这一处漏打码）；
	/// ③ 系统序列号。
	/// </para>
	/// <para>⚠️ 只在本机导出诊断包时按当前机器的值登记，不落盘、不联网、不上传。</para>
	/// </summary>
	internal static class SensitiveValues
	{
		private static readonly object Lock = new object();

		private static readonly List<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>();

		/// <summary>登记一个裸值。<paramref name="label"/> 是给自检报告看的说明（例如「真实主机名（%COMPUTERNAME%）」）。</summary>
		internal static void Register(string? value, string label)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return;
			}
			string text = value.Trim();
			if (text.Length < 2)
			{
				return;
			}
			// 2026-10-09（批次3 #3「打码放宽」）：**面板号**（`BOE0B40` 这类 —— 面板厂 3 字母 + 型号 3~5 位十六进制）
			// 是【面板信息】而不是标识：不登记 ⇒ 既不打码、也不进「序列号裸值」自检。
			// 机主确认真机上 ICC 文件名 `BOE0B40_*.icm` 里那一段就是面板型号（BOE=面板厂、0B40=面板型号），
			// 旧口径把它当 EDID 序列号登记 ⇒ 自检误报「疑似漏打码」。判据见 <see cref="PanelIdentifiers"/>。
			if (PanelIdentifiers.IsPanelNumber(text))
			{
				return;
			}
			lock (Lock)
			{
				foreach (KeyValuePair<string, string> item in Items)
				{
					if (string.Equals(item.Key, text, StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
				}
				Items.Add(new KeyValuePair<string, string>(text, label));
			}
		}

		internal static List<KeyValuePair<string, string>> Snapshot()
		{
			lock (Lock)
			{
				return new List<KeyValuePair<string, string>>(Items);
			}
		}

		/// <summary>
		/// 这个裸值适不适合当成「标识」拿去全文兜底打码（<see cref="MaskKnownValues"/>）与自检：
		/// **必须同时含字母和数字**，或**大小写混排**（≥2 个大写 + 有字母）。
		/// 阈值刻意保守 —— 真机实测踩过的坑：`xiaohan` 这种纯小写用户名会把 `Corporation` 里的 `orporation`
		/// 当成命中（`x` 在 `N` 之后），一路把 `NVIDIA Corporation` 打成 `NVIDIA &lt;用户名已打码&gt;`，
		/// 既毁证据又没真打掉什么。主机名（`DESKTOP-A1B2C3` / `LAPTOP-1A2B`）、设备序列号、EDID 序列号
		/// 这类**真标识**都同时满足「含数字」或「大小写混排」，一个都不会漏。
		/// </summary>
		internal static bool IsUsableToken(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}
			string text = value.Trim();
			if (text.Length < 4 || text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0)
			{
				return false;
			}
			bool flag = false;
			bool flag2 = false;
			int num = 0;
			bool flag3 = false;
			foreach (char c in text)
			{
				if (char.IsDigit(c))
				{
					flag3 = true;
				}
				else if (char.IsLetter(c))
				{
					if (char.IsUpper(c))
					{
						num++;
					}
					else
					{
						flag2 = true;
					}
				}
			}
			flag = text.Any(char.IsLetter);
			return (flag && flag3) || (flag && flag2 && num >= 2);
		}
	}

	/// <summary>
	/// 2026-10-09（批次3 #3「打码放宽」）：**面板号**识别 —— 面板号**不是序列号**。
	/// <para>
	/// 背景（机主已确认）：诊断包里的隐私自检把屏幕 ICC 文件名误报成「序列号裸值」，例如 `BOE0B40_*.icm`；
	/// 其中 `BOE` = 面板厂、`0B40` = 面板型号，**是【面板信息】不是序列号**。旧口径把它当 EDID 序列号登记、
	/// 打码、自检 ⇒ 误报「疑似漏打码」，让这个自检不可信。
	/// </para>
	/// <para>
	/// ⭐ 判据（**形状 + 面板厂白名单**，白名单就是用户确认的那 10 个厂，不猜、不乱加）：
	/// <c>^&lt;3 字母面板厂&gt;&lt;4 位十六进制型号&gt;$</c>，总长**恰好 7**，例如
	/// <c>BOE0B40</c> / <c>BOE0CBE</c> / <c>AUO1234</c> / <c>LGD05E0</c>。
	/// 面板厂白名单：BOE / AUO / CSO / LGD / SDC / INX / IVO / TMX / EDP / SHP。
	/// ⚠️ 「厂 3 字母 + 产品 4 位」这个形状不是我编的：本仓库 <c>MechrevoLite.Hardware\DisplayColorGamut.cs</c>
	/// 的 <c>EnumerateDisplayPrefixes</c> 注释与判据（<c>name.Length &gt;= 7</c> + 不含 <c>&amp;</c>）就是
	/// Windows 的 <c>DISPLAY\&lt;厂3&gt;&lt;型号4&gt;</c> 设备实例命名，ICC 文件名用的正是同一个 PnP id。
	/// </para>
	/// <para>
	/// ⚠️ **保守**：只有**同时**满足「厂在白名单内」且「型号段恰好 4 位十六进制」才算面板号。于是
	/// 任何长度 ≠7 或型号段含非十六进制字符的串（典型主机序列号 / EDID 序列号，例如 `B025AA78094A`、
	/// `M24M4B0000010`、`BOE0B400`）天然落不进这一类 —— 它们仍然照旧被打码、也仍然会被自检抓出
	/// （放宽没有削掉能力）。拿不准的一律按旧口径处理。
	/// </para>
	/// </summary>
	internal static class PanelIdentifiers
	{
		/// <summary>面板号总长：3 字母厂 + 4 位十六进制型号（Windows `DISPLAY\&lt;厂&gt;&lt;型号&gt;` 的同一个 PnP id）。</summary>
		private const int PanelNumberLength = 7;

		/// <summary>面板厂白名单（用户确认的清单，逐字）。判据只认这 10 个厂，不扩充、不猜。</summary>
		private static readonly string[] KnownVendors = new string[10] { "BOE", "AUO", "CSO", "LGD", "SDC", "INX", "IVO", "TMX", "EDP", "SHP" };

		/// <summary>行内面板号 token（恰好 7 位：3 字母厂 + 4 位十六进制；前后不得紧邻字母/数字 ⇒ 不会切长串）。</summary>
		private static readonly Regex PanelNumberToken = new Regex(
			"(?<![0-9A-Za-z])(?:BOE|AUO|CSO|LGD|SDC|INX|IVO|TMX|EDP|SHP)[0-9A-Fa-f]{4}(?![0-9A-Za-z])",
			RegexOptions.CultureInvariant);

		/// <summary>「ICC 文件名里的面板号」：`&lt;面板号&gt;[_&lt;后缀&gt;].icm/.icc`（只用于自检报告计数，绝不打码）。</summary>
		private static readonly Regex IccPanelIdentityInLine = new Regex(
			"(?<![0-9A-Za-z])(?:BOE|AUO|CSO|LGD|SDC|INX|IVO|TMX|EDP|SHP)[0-9A-Fa-f]{4}(?=[_.-]?[0-9A-Za-z_.-]{0,64}\\.(?:icm|icc)(?![0-9A-Za-z]))",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		/// <summary>该 3 字母是不是白名单里的面板厂。</summary>
		internal static bool IsPanelVendor(string token)
		{
			if (string.IsNullOrEmpty(token) || token.Length != 3)
			{
				return false;
			}
			foreach (string vendor in KnownVendors)
			{
				if (string.Equals(vendor, token, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>整段是不是一个面板号（厂在白名单 + 型号恰好 4 位十六进制 + 总长恰好 7）。</summary>
		internal static bool IsPanelNumber(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}
			string text = value.Trim();
			if (text.Length != PanelNumberLength || !IsPanelVendor(text.Substring(0, 3)))
			{
				return false;
			}
			for (int i = 3; i < text.Length; i++)
			{
				char c = text[i];
				bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if (!isHex)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>该行出现几个面板号（只计数，供自检报告写「面板号豁免 N 处」）。</summary>
		internal static int CountPanelNumbers(string line)
		{
			return string.IsNullOrEmpty(line) ? 0 : PanelNumberToken.Matches(line).Count;
		}

		/// <summary>该行是否含「ICC 文件名里的面板号」形态（`&lt;面板号&gt;…​.icm/.icc`）。</summary>
		internal static bool ContainsIccPanelIdentity(string line)
		{
			return !string.IsNullOrEmpty(line) && IccPanelIdentityInLine.IsMatch(line);
		}
	}

	private static void AddRoot(List<string> roots, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string item = path.TrimEnd('\\');
		foreach (string root in roots)
		{
			if (string.Equals(root, item, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		roots.Add(item);
	}

	private static string ParentDir(string path)
	{
		try
		{
			return Path.GetDirectoryName(path.TrimEnd('\\'));
		}
		catch
		{
			return null;
		}
	}

	private static string ShortSha256(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using SHA256 sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(stream);
		return Convert.ToHexString(hash, 0, 8);
	}

	// ================================================================ ③ ItemSupport 就绪键

	private static void AppendItemSupportReadiness(StringBuilder sb)
	{
		sb.AppendLine("[GCU 就绪键（ItemSupport）]");
		sb.AppendLine("（只读；⚠️ ServiceLoadingReady / PerformanceReady 在故障机上可能整个值不存在 —— 这正是重要线索）");
		string[] paths = new string[3]
		{
			"SOFTWARE\\OEM\\GamingCenter2\\ItemSupport",
			"SOFTWARE\\OEM\\GamingCenter\\ItemSupport",
			"SOFTWARE\\OEM\\ControlCenter\\ItemSupport"
		};
		string[] names = new string[3] { "ServiceReady", "ServiceLoadingReady", "PerformanceReady" };
		try
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			foreach (string path in paths)
			{
				try
				{
					using RegistryKey key = baseKey.OpenSubKey(path);
					if (key == null)
					{
						sb.AppendLine($"  HKLM\\{path}：键不存在");
						continue;
					}
					List<string> parts = new List<string>();
					foreach (string name in names)
					{
						try
						{
							object value = key.GetValue(name, null);
							if (value == null)
							{
								parts.Add(name + "=不存在");
							}
							else
							{
								parts.Add(name + "=" + FormatRegValue(value));
							}
						}
						catch (Exception ex)
						{
							parts.Add(name + "=该项读取失败（" + ex.Message + "）");
						}
					}
					sb.AppendLine($"  HKLM\\{path}：" + string.Join("  ", parts));
				}
				catch (Exception ex2)
				{
					sb.AppendLine($"  HKLM\\{path}：该项读取失败（{ex2.Message}）");
				}
			}
		}
		catch (Exception ex3)
		{
			sb.AppendLine("  该项读取失败（" + ex3.Message + "）");
		}
		sb.AppendLine();
	}

	private static string FormatRegValue(object value)
	{
		if (value is int num)
		{
			return $"0x{num:X} ({num})";
		}
		if (value is byte[] bytes)
		{
			return "0x" + Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 16));
		}
		return value.ToString() ?? "(空)";
	}

	// ================================================================ ④ GCU 侧日志（ControlCenter / MyControlCenter）

	/// <summary>
	/// 2026-10-09（第二批补采）：本节由原来的「只取 2 个最新文件、每文件 ≤200 行/≤32 KB 的尾部」
	/// 扩写为「**先列清单**（目录/文件是否存在都写明，不存在就明确标注）+ 每个文件尾部 ≤64 KB
	/// （本小节合计 ≤512 KB，超出写明截断/丢弃了多少）+ **关键行抽取**（命中就贴原文行）」。
	/// ⚠️ gcu-install-*.log 的清单+尾部已移交 DiagnosticGcuInventoryInfo 的「[GCU 安装日志与装后自检状态]」小节，
	///    以免同一批文件被两节重复采集。
	/// ⚠️ 尾部读取与渲染复用 DiagnosticGcuInventoryInfo 的同一份实现（口径只有一份）。
	/// </summary>
	private static void AppendBaseLogTail(StringBuilder sb)
	{
		sb.AppendLine("[GCU 侧日志（ControlCenter / MyControlCenter）]");
		sb.AppendLine("（⚠️ 原厂/GCU 侧日志，不是我们自己的 log.txt；先列清单再取尾部：每文件 ≤ "
			+ DiagnosticGcuInventoryInfo.TailBytesPerFile / 1024 + " KB，本小节所有文件尾部合计 ≤ "
			+ DiagnosticGcuInventoryInfo.ControlCenterBudgetBytes / 1024 + " KB，超出会写明截断/丢弃了多少；"
			+ "路径/用户名/机器名/序列号/MAC 已打码。⚠️ 关键行（LM_Manager/LM_Init/RGBKB/solution/UNKNOWN/Keyboard/"
			+ "Subscribe/SUBACK/Mqtt/Error/Exception）会额外单独贴原文行 —— 30 系 solution=UNKNOWN、桥不回 SUBACK 这类问题看它。）");
		int budget = DiagnosticGcuInventoryInfo.ControlCenterBudgetBytes;
		int num = 0;
		try
		{
			num += DiagnosticGcuInventoryInfo.AppendDirectoryLogs(sb, @"C:\ProgramData\ControlCenter", "*.log", recursive: false, ref budget);
		}
		catch (Exception ex)
		{
			sb.AppendLine("    · 目录: C:\\ProgramData\\ControlCenter —— 该项读取失败（" + ex.Message + "）");
		}
		try
		{
			num += DiagnosticGcuInventoryInfo.AppendDirectoryLogs(sb, @"C:\ProgramData\ControlCenter\installLog", "*.log", recursive: false, ref budget);
		}
		catch (Exception ex2)
		{
			sb.AppendLine("    · 目录: C:\\ProgramData\\ControlCenter\\installLog —— 该项读取失败（" + ex2.Message + "）");
		}
		try
		{
			DiagnosticGcuInventoryInfo.AppendSingleLog(sb, @"C:\MyControlCenter.log", ref budget);
		}
		catch (Exception ex3)
		{
			sb.AppendLine("    · 文件: C:\\MyControlCenter.log —— 该项读取失败（" + ex3.Message + "）");
		}
		// 2026-10-09（75:00）：上面 3 条是**优先命中**（存在就照上面那样读内容 + 抽关键行）。
		// 30 系实测：底座目录树 40 个目录全量枚举里**没有任何 log 目录**，而 ProgramData\ControlCenter 只有一个
		// 200 字节的 Application_Startup 日志 ⇒ 真日志既不在这 3 条路径、也不在安装根下，属于**采集缺口**。
		// 所以下面再按「可发现」的方式扫一遍：底座根 + 安装根 + %ProgramData%，递归找近 24 小时的 *.log。
		// ⚠️ 第一版**只列路径 / 大小 / mtime，不读内容**（避免成本失控），并写明各项上限。
		AppendLogPathDiscovery(sb, ref budget);
		if (num == 0)
		{
			sb.AppendLine("  ⇒ 小结: C:\\ProgramData\\ControlCenter\\*.log（含 installLog）与 C:\\MyControlCenter.log **全部不存在**"
				+ "（机主机器上若有官方控制台/GCU，这两处通常会有日志；本机没有就是「本机无此目录/文件」；"
				+ "⚠️ 这只是「3 条优先路径」的结论，真日志在哪见下面「[GCU 日志路径发现]」。—— 30 系实测就是这 3 条全不中、日志另在他处）。");
		}
		else
		{
			sb.AppendLine($"  ⇒ 小结: 命中 {num} 个 ControlCenter 侧 *.log 文件；尾部合计 {DiagnosticGcuInventoryInfo.ControlCenterBudgetBytes - budget} 字节 / 上限 {DiagnosticGcuInventoryInfo.ControlCenterBudgetBytes} 字节。");
		}
		sb.AppendLine();
	}

	// ================================================================ ④-b GCU 日志路径发现（2026-10-09 75:00 新增）

	/// <summary>路径发现：最大递归深度（根算第 0 层）。</summary>
	private const int DiscoverMaxDepth = 6;

	/// <summary>路径发现：最多访问的目录数（防超大目录树）。</summary>
	private const int DiscoverMaxDirs = 4000;

	/// <summary>路径发现：最多收集的 *.log 文件数（超出只报数）。</summary>
	private const int DiscoverMaxFiles = 300;

	/// <summary>路径发现：清单里最多列出多少个（其余只报数，避免把诊断包撑大）。</summary>
	private const int DiscoverListCap = 40;

	/// <summary>路径发现：清单里最多列出多少个「近 24 小时」命中（优先列这些）。</summary>
	private const int DiscoverRecentCap = 20;

	/// <summary>路径发现：单个日志文件路径+大小+mtime 的字符上限（防超长行）。</summary>
	private const int DiscoverMaxLineChars = 300;

	/// <summary>
	/// 2026-10-09（75:00）：「GCU 侧日志」原来只试 3 条硬编码路径 —— 30 系实测这 3 条全不中（ProgramData\ControlCenter
	/// 只有一个 200 字节的启动日志；底座目录树 40 个目录里没有任何 log 目录），真日志到底在哪**包看不到**，这是采集缺口。
	/// 本节按「可发现」的方式补：在 **底座根 + 安装根（底座根的上级）+ GCU 服务目录 + %ProgramData%** 下递归找 *.log，
	/// 列出「相对路径 / 大小 / mtime」，并写明「命中 N 个、近 24 小时 M 个、取尾部 K 个」。
	/// <para>
	/// ⚠️ 第一版**只列路径与大小/mtime，绝不读内容**（内容采集仍是上面 3 条优先路径的活，口径不变）。
	/// ⚠️ 边界：深度 ≤ <see cref="DiscoverMaxDepth"/>、目录 ≤ <see cref="DiscoverMaxDirs"/>、文件 ≤ <see cref="DiscoverMaxFiles"/>、
	///    清单 ≤ <see cref="DiscoverListCap"/> 条（其中近 24h 的优先、≤ <see cref="DiscoverRecentCap"/> 条）；
	///    因超限跳过的目录/文件都明确写在输出里（「因超限而跳过」），绝不静默。
	/// </para>
	/// </summary>
	private static void AppendLogPathDiscovery(StringBuilder sb, ref int budget)
	{
		sb.AppendLine("  [GCU 日志路径发现]（⚠️ 只列路径/大小/mtime，**不读内容**；内容由上面 3 条优先路径负责）");
		try
		{
			List<string> list = new List<string>();
			int num = Directory.Exists(@"C:\ProgramData") ? 4 : 3;
			sb.AppendLine($"    搜索范围: 底座根 + 安装根（底座根的上级）+ GCU 服务目录 + %ProgramData%\\（最多 {num} 个根）");
			sb.AppendLine($"    上限: 深度 ≤ {DiscoverMaxDepth} 层 · 目录 ≤ {DiscoverMaxDirs} 个 · *.log 文件 ≤ {DiscoverMaxFiles} 个 · 清单 ≤ {DiscoverListCap} 条；不跟随目录联接（junction/symlink）");
			string text = null;
			string reason;
			try
			{
				text = ResolveGcuRoot(out reason);
			}
			catch (Exception ex)
			{
				reason = "该项读取失败（" + ex.Message + "）";
			}
			if (!string.IsNullOrWhiteSpace(text))
			{
				AddRoot(list, text);
				try
				{
					AddRoot(list, Path.GetDirectoryName(text.TrimEnd('\\')));
				}
				catch
				{
				}
			}
			else
			{
				sb.AppendLine("    ⚠️ 底座根取不到（" + Or(reason) + "）⇒ 只扫 %ProgramData%\\ 一处；安装根也没法定。");
			}
			try
			{
				using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(GcuRegistryKey);
				string text2 = (registryKey?.GetValue("GcuServiceDir") as string)?.Trim().Trim('"') ?? "";
				if (text2.Length > 0 && Path.IsPathRooted(text2))
				{
					AddRoot(list, text2.TrimEnd('\\'));
				}
			}
			catch
			{
			}
			AddRoot(list, @"C:\ProgramData");
			int num2 = 0;
			int num3 = 0;
			long num4 = 0L;
			int num5 = 0;
			List<string> notes = new List<string>();
			List<KeyValuePair<DateTime, string>> list2 = new List<KeyValuePair<DateTime, string>>();
			DateTime dateTime = DateTime.Now.AddHours(-24.0);
			foreach (string item in list)
			{
				if (!Directory.Exists(item))
				{
					sb.AppendLine("    · 根: " + Mask(item) + " —— 不存在");
					continue;
				}
				sb.AppendLine("    · 根: " + Mask(item) + "（含子目录）");
				int num6 = 0;
				DiscoverLogs(item, 0, list2, notes, ref num2, ref num3, ref num4, ref num6, dateTime);
				num5 += num6;
			}
			list2.Sort((KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) => b.Key.CompareTo(a.Key));
			sb.AppendLine($"    命中: 共 {list2.Count} 个 *.log（近 24 小时 {num5} 个，取尾部 {Math.Min(list2.Count, DiscoverListCap)} 个按 mtime 新→旧列出）；体积合计 {num4 / 1024} KB");
			if (list2.Count == 0)
			{
				sb.AppendLine("      （一个都没有 —— 那就是「这几种根下确实没有 *.log」，不是「本机无日志能力」。）");
			}
			else
			{
				int num7 = 0;
				foreach (KeyValuePair<DateTime, string> item2 in list2.Take(DiscoverListCap))
				{
					num7++;
					bool flag = item2.Key >= dateTime;
					sb.AppendLine($"      {num7}. {(flag ? "[近24h] " : "")}{Truncate(Mask(item2.Value), DiscoverMaxLineChars)}  mtime={item2.Key:yyyy-MM-dd HH:mm:ss}");
					if (budget > 0)
					{
						budget = Math.Max(0, budget - Math.Min(budget, item2.Value.Length + 80));
					}
				}
				if (list2.Count > DiscoverListCap)
				{
					sb.AppendLine($"      …另有 {list2.Count - DiscoverListCap} 个未列出（清单上限 {DiscoverListCap} 条）。");
				}
			}
			sb.AppendLine($"    规模: 访问目录 {num3} 个（上限 {DiscoverMaxDirs}）· 收集 *.log {num2} 个（上限 {DiscoverMaxFiles}）");
			if (notes.Count == 0)
			{
				sb.AppendLine("    超限跳过: 无（没有因上限而跳过的目录/文件）");
			}
			else
			{
				sb.AppendLine("    ⚠️ 因超限而跳过（未列出，避免成本失控）：");
				foreach (string item3 in notes)
				{
					sb.AppendLine("      - " + item3);
				}
			}
		}
		catch (Exception ex2)
		{
			sb.AppendLine("    该项读取失败（" + ex2.Message + "）");
		}
	}

	// ================================================================ ⑤ KeyboardManager 两文件（2026-10-09 75:00 新增）

	/// <summary>KeyboardManager 两文件：单个文件最多贴多少字符的原文（它们很小，全文贴足够）。</summary>
	private const int KeyManagerFileMaxChars = 4096;

	/// <summary>
	/// 2026-10-09（75:00）补采两个高价值小文件（灯效解的强候选）：
	///   · <c>…\MyControlCenter\AppSettings\KeyboardManager.json</c>（真机 32 B；⭐ mtime 晚于安装时间 ⇒ 被 GCU **运行期改写**）
	///   · <c>…\MyControlCenter\KeyboardManager\settings.json</c>（真机 145 B）
	/// <para>
	/// ⚠️ **全文贴**（有 <see cref="KeyManagerFileMaxChars"/> 上限，超出写明截断），并按实际布局兼容两种情况：
	/// 底座根下直接是 <c>MyControlCenter\…</c>（布局 B）或先有 <c>AiStoneService\</c>（布局 A）——两条路径都试。
	/// ⚠️ 不存在就**明确写「不存在」**，并附 mtime（存在时）用于判断是否被运行期改写；只读，不写任何文件。
	/// </para>
	/// </summary>
	private static void AppendKeyboardManagerFiles(StringBuilder sb)
	{
		sb.AppendLine("[GCU 键盘灯效文件（KeyboardManager）]");
		sb.AppendLine("（⚠️ 只读 + 打码后**贴全文**：这两个文件都很小，是「30 系灯效解」的强候选；"
			+ "文件名/路径已打码，内容里的序列号/长十六进制/用户名也过同一套打码。⚠️ 不存在就写明「不存在」。）");
		string text = null;
		try
		{
			text = ResolveGcuRoot(out string _);
		}
		catch (Exception ex)
		{
			sb.AppendLine("  底座根: 取不到（" + ex.Message + "）⇒ 两个文件都无法定位");
			sb.AppendLine();
			return;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			sb.AppendLine("  底座根: 取不到 ⇒ 两个文件都无法定位");
			sb.AppendLine();
			return;
		}
		string[] array = new string[2] { "MyControlCenter\\AppSettings\\KeyboardManager.json", "MyControlCenter\\KeyboardManager\\settings.json" };
		foreach (string text2 in array)
		{
			string text3 = null;
			string[] array2 = new string[2]
			{
				Path.Combine(text, "AiStoneService\\" + text2),
				Path.Combine(text, text2)
			};
			foreach (string text4 in array2)
			{
				try
				{
					if (File.Exists(text4))
					{
						text3 = text4;
						break;
					}
				}
				catch (Exception ex2)
				{
					sb.AppendLine("  " + text2 + " —— 该项读取失败（" + ex2.Message + "）");
				}
			}
			sb.AppendLine("  " + text2);
			if (text3 == null)
			{
				sb.AppendLine("    不存在（两个候选路径都没有：AiStoneService\\ 下与底座根下）—— 该机型可能没有这个文件");
				continue;
			}
			try
			{
				FileInfo fileInfo = new FileInfo(text3);
				sb.AppendLine($"    实际路径: {Mask(text3)}");
				sb.AppendLine($"    大小={fileInfo.Length} 字节  mtime={fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}（安装后被 GCU 运行期改写的话，mtime 会晚于安装时间）");
				string text5 = ReadWholeText(text3, KeyManagerFileMaxChars * 4);
				sb.AppendLine("    内容（全文，已打码）：");
				foreach (string item in Mask(text5).Replace("\r\n", "\n").Split('\n'))
				{
					sb.AppendLine("      " + Truncate(item, 400));
				}
				if (fileInfo.Length > KeyManagerFileMaxChars * 4)
				{
					sb.AppendLine($"    ⚠️ 只贴了前 {KeyManagerFileMaxChars * 4} 字节（原文件 {fileInfo.Length} 字节）—— 这两个文件正常只有几十~几百字节，出现这种情况请把大小报给开发者。");
				}
			}
			catch (Exception ex3)
			{
				sb.AppendLine("    该项读取失败（" + ex3.Message + "）");
			}
		}
		sb.AppendLine();
	}

	/// <summary>截断说明去重收集（与 <see cref="DiagnosticGcuInventoryInfo"/> 同一口径：最多 8 条、不重复）。</summary>
	private static void AddNote(List<string> notes, string note)
	{
		if (notes.Count < 8 && !notes.Contains(note))
		{
			notes.Add(note);
		}
	}

	/// <summary>递归收集 *.log（只 stat，不读内容）。超出上限的目录/文件写进 notes，绝不静默。</summary>
	private static void DiscoverLogs(string dir, int depth, List<KeyValuePair<DateTime, string>> files, List<string> notes, ref int found, ref int dirs, ref long bytes, ref int recent, DateTime since)
	{
		if (depth > DiscoverMaxDepth || dirs >= DiscoverMaxDirs || found >= DiscoverMaxFiles)
		{
			AddNote(notes, $"「{Mask(dir)}」未展开（深度 > {DiscoverMaxDepth} 或目录/文件数已达上限）");
			return;
		}
		DirectoryInfo directoryInfo;
		try
		{
			directoryInfo = new DirectoryInfo(dir);
			directoryInfo.GetFiles("*.log");
		}
		catch (Exception ex)
		{
			AddNote(notes, $"「{Mask(dir)}」读取失败（{ex.Message}）");
			return;
		}
		dirs++;
		try
		{
			foreach (FileInfo item in directoryInfo.GetFiles("*.log"))
			{
				if (found >= DiscoverMaxFiles)
				{
					AddNote(notes, $"*.log 文件数已达上限 {DiscoverMaxFiles}，其余不再收集（「{Mask(dir)}」及其后续目录都受影响）");
					break;
				}
				found++;
				bytes += item.Length;
				if (item.LastWriteTime >= since)
				{
					recent++;
				}
				files.Add(new KeyValuePair<DateTime, string>(item.LastWriteTime, item.FullName));
			}
		}
		catch (Exception ex2)
		{
			AddNote(notes, $"「{Mask(dir)}」枚举文件失败（{ex2.Message}）");
		}
		if (depth >= DiscoverMaxDepth)
		{
			return;
		}
		DirectoryInfo[] array;
		try
		{
			array = directoryInfo.GetDirectories();
		}
		catch (Exception ex3)
		{
			AddNote(notes, $"「{Mask(dir)}」枚举子目录失败（{ex3.Message}）");
			return;
		}
		foreach (DirectoryInfo directoryInfo2 in array)
		{
			try
			{
				if ((directoryInfo2.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					AddNote(notes, $"「{Mask(directoryInfo2.FullName)}」是 junction/symlink，未跟随");
					continue;
				}
			}
			catch
			{
			}
			DiscoverLogs(directoryInfo2.FullName, depth + 1, files, notes, ref found, ref dirs, ref bytes, ref recent, since);
		}
	}

	private static string DecodeText(byte[] bytes)
	{
		int offset = 0;
		if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
		{
			offset = 3;
		}
		return Utf8Loose.GetString(bytes, offset, bytes.Length - offset);
	}

	private static string ReadWholeText(string path, int maxBytes)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		int num = (int)Math.Min(stream.Length, maxBytes);
		byte[] array = new byte[num];
		int num2 = 0;
		while (num2 < num)
		{
			int num3 = stream.Read(array, num2, num - num2);
			if (num3 <= 0)
			{
				break;
			}
			num2 += num3;
		}
		return DecodeText(array);
	}

	// ================================================================ 公共小工具

	/// <summary>
	/// 打码：用户名、序列号/SN、MAC、长十六进制串（哈希/设备 ID）等一律替换，避免随包外泄。
	/// ⚠️ internal：DiagnosticGcuInventoryInfo 复用**同一份**实现（打码口径只有一份，绝不各写一套）。
	/// </summary>
	internal static string Mask(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		try
		{
			string result = ProtectedPathToken.Replace(text, MaskSentinel + "$1" + MaskSentinel);
			result = MaskMachineUsers(result);
			result = result.Replace(MaskSentinel.ToString(), "");
			result = MaskUserProfilePaths(result);
			// 2026-10-09（75:00）：先把「文件名里夹的序列号」打掉，再做下面的通用规则 —— 顺序保证
			// 序列号不会先被 LongHexLike/GuidLike 改成别的形态而漏掉 .icm/.icc 的上下文。
			result = MaskSerialsInFilenames(result);
			// 2026-10-09（75:00）：再按**本机已知的敏感裸值**（主机名 / 用户名 / 显示器 EDID 序列号 / 系统序列号）兜一遍，
			// 规则同「打码自检」用的那份（SensitiveValues）—— 自检报什么，这里就盖什么。
			result = MaskKnownValues(result);
			// 2026-10-09（批次3 #3「打码放宽」）：`Serial=BOE0B40` 这种「标签 + 面板号」也要放行 ——
			// 面板号是面板信息（上面 SerialTailInFilename / MaskKnownValues 已同口径放行），
			// 这里若仍按「Serial 后面跟 ≥6 位字母数字」打码，就会把面板信息盖掉、与自检口径自相矛盾。
			// 判据仍只有一条：<see cref="PanelIdentifiers.IsPanelNumber"/>（恰好 7 位：厂 3 字母 + 型号 4 位十六进制）。
			result = SerialLike.Replace(result, MaskSerialLikeMatch);
			result = UserNameLine.Replace(result, "$1$3<用户名已打码>");
			result = MacLike.Replace(result, "<MAC已打码>");
			result = LongHexLike.Replace(result, "<长十六进制已打码>");
			result = GuidLike.Replace(result, "<GUID已打码>");
			return result;
		}
		catch
		{
			return text;
		}
	}

	/// <summary>
	/// 文件名里夹的序列号（ICC `.icm`/`.icc`）—— 只替换扩展名紧邻的那一段。
	/// 2026-10-09（批次3 #3「打码放宽」）：那一段若本身是**面板号**（`BOE0B40` / `BOE0CBE` 这类，见
	/// <see cref="PanelIdentifiers"/>）就**原样保留**（它是面板信息、不是序列号；
	/// 例如 `X_BOE0CBE.icm` 不再被误打成 `X_&lt;文件名内序列号已打码&gt;.icm`）；
	/// 其余任何 ≥6 位含字母的长串（真序列号，例如 `_B025AA78094A.icm`）**照旧打码**。
	/// 返回替换处数，供日志/人工核对（DiagnosticGcuInventoryInfo 复用同一份实现）。
	/// </summary>
	internal static string MaskSerialsInFilenames(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		return SerialTailInFilename.Replace(text, (Match match) => PanelIdentifiers.IsPanelNumber(match.Value) ? match.Value : "<文件名内序列号已打码>");
	}

	/// <summary>
	/// `Serial=`/`SN:`/`序列号:` 后面那一格的打码（原样保留标签与分隔符，只换值）。
	/// 2026-10-09（批次3 #3）：值是**面板号**时**原样保留**（面板信息，见 <see cref="PanelIdentifiers"/>）；
	/// 其余一律照旧替换成 <c>&lt;序列号已打码&gt;</c>（真序列号的能力一点没少）。
	/// </summary>
	private static string MaskSerialLikeMatch(Match match)
	{
		if (PanelIdentifiers.IsPanelNumber(match.Groups[4].Value))
		{
			return match.Value;
		}
		return match.Groups[1].Value + match.Groups[3].Value + "<序列号已打码>";
	}

	/// <summary>
	/// 按本机已知敏感裸值逐个打码（主机名 / 用户名 / 用户目录名 / 显示器 EDID 序列号 / 系统序列号）。
	/// 判定与「打码自检」完全同一份 <see cref="SensitiveValues"/> 快照。
	/// </summary>
	internal static string MaskKnownValues(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		foreach (KeyValuePair<string, string> item in SensitiveValues.Snapshot())
		{
			// 2026-10-09（批次3 #3「打码放宽」）：面板号是【面板信息】，即便被别处登记过也一律不打码。
			if (PanelIdentifiers.IsPanelNumber(item.Key))
			{
				continue;
			}
			if (!SensitiveValues.IsUsableToken(item.Key) || text.IndexOf(item.Key, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			try
			{
				text = Regex.Replace(text, "(?i)(?<![0-9A-Za-z])" + Regex.Escape(item.Key) + "(?![0-9A-Za-z])", MarkerFor(item.Value), RegexOptions.CultureInvariant);
			}
			catch
			{
			}
		}
		return text;
	}

	/// <summary>裸值的说明 → 打码标记（HostName/UserName 的判定与自检同一份）。</summary>
	private static string MarkerFor(string label)
	{
		if (label.Contains("HostName", StringComparison.Ordinal) || label.Contains("主机名"))
		{
			return "<主机名已打码>";
		}
		if (label.Contains("UserName", StringComparison.Ordinal) || label.Contains("用户名"))
		{
			return "<用户名已打码>";
		}
		if (label.Contains("序列号"))
		{
			return "<序列号已打码>";
		}
		return "<已打码>";
	}

	/// <summary>
	/// 把「机器名\用户名」逐个候选判定后再打码：机器名以「含字母且不含 '-'」为判据
	/// （这样 `gcu-install-20261008-195110\foo` 这类日志文件名不会被误吞，实测踩过），
	/// 用户名则以「不含 '-' 且不含数字」为判据（用户名带连字符时留原文，宁少勿滥伤）。
	/// </summary>
	private static string MaskMachineUsers(string text)
	{
		StringBuilder sb = new StringBuilder();
		int last = 0;
		foreach (Match match in MachineUser.Matches(text))
		{
			string machine = match.Groups[1].Value;
			string user = match.Groups[2].Value;
			// 2026-10-09（75:00）：机名若在**本机已知标识表**里（含数字 / 大小写混排的标准机名，例如
			// DESKTOP-A1B2C3）就一定要打 —— 原形状判据「含字母且不含 '-'」会把这类标准 Windows 机名整条放过，
			// 这正是机主实测「真实主机名没被打掉」的那一处。其余仍走原判据（`gcu-install-2026…\foo` 不被误吞）。
			bool machineLooksLikeName = SensitiveValues.IsUsableToken(machine) || (machine.Any(char.IsLetter) && !machine.Contains('-'));
			bool userLooksLikeName = !user.Contains('-') && !user.Any(char.IsDigit);
			if (!machineLooksLikeName || !userLooksLikeName)
			{
				continue;
			}
			sb.Append(text, last, match.Index - last);
			sb.Append("<机器名\\用户名已打码>");
			last = match.Index + match.Length;
		}
		if (last == 0)
		{
			return text;
		}
		sb.Append(text, last, text.Length - last);
		return sb.ToString();
	}

	/// <summary>
	/// `C:\Users\&lt;名&gt;\…` 里的用户名打码；同样对「名」做形状判定（不含 '-'、不含数字），
	/// 免得把 `…\Users\gcu-install-2026…` 这类路径当用户名误吞。
	/// </summary>
	private static string MaskUserProfilePaths(string text)
	{
		StringBuilder sb = new StringBuilder();
		int last = 0;
		foreach (Match match in UsersPath.Matches(text))
		{
			string user = match.Groups[2].Value;
			if (user.Contains('-') || user.Any(char.IsDigit))
			{
				continue;
			}
			sb.Append(text, last, match.Index - last);
			sb.Append(match.Groups[1].Value).Append("<用户>");
			last = match.Index + match.Length;
		}
		if (last == 0)
		{
			return text;
		}
		sb.Append(text, last, text.Length - last);
		return sb.ToString();
	}

	private static string Truncate(string text, int max)	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + $"…(已截断，原长 {text.Length} 字符)";
	}

	private static string Or(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return "(空)";
	}
}
