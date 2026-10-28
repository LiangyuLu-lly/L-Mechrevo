using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MechrevoLite;
using MechrevoLite.Hardware;
using MechrevoLite.UI;

// 2026-10-11（真机 175% DPI 取证）：本机 DPI 下把**真实 SettingsForm** 建出来，直接量
//   * 窗体的 MinimumSize / ClientSize / Width / DeviceDpi
//   * 标题行左组与读数块的真实几何
// 目的：宿主固定 96 DPI，无法复现"高 DPI 下 MinimumSize 语义/缩放"这一类问题 —— 这里用真机 DPI 量。
// ⛔ 只读：UiAuditMode=true，不连 GCU、不读 SMU、不碰驱动、不写配置。
internal static class WidthProbe
{
	private static readonly Assembly Asm = typeof(MechrevoHw).Assembly;

	[STAThread]
	private static void Main()
	{
		string dir = Path.Combine(Path.GetTempPath(), "widthprobe");
		Directory.CreateDirectory(dir);
		string cfg = Path.Combine(dir, "cfg.json");
		if (!File.Exists(cfg))
		{
			File.WriteAllText(cfg, "{\r\n  \"performance_mode\": 0,\r\n  \"igpu_only_asked\": 1,\r\n  \"igpu_only_ui\": \"show\",\r\n  \"igpu_attempt_pending\": -1,\r\n  \"dgpu_generation\": \"Gen40\",\r\n  \"dgpu_generation_source\": \"MarketingName\",\r\n  \"dgpu_has_dgpu\": \"1\"\r\n}\r\n", new System.Text.UTF8Encoding(false));
		}
		Environment.SetEnvironmentVariable("LMECHREVO_CONFIG_FILE", cfg);
		Environment.SetEnvironmentVariable("LMECHREVO_LOG_DIR", dir);
		Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
		Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		SetStatic("MechrevoLite.Program", "UiAuditMode", true);

		using Graphics g = Graphics.FromHwnd(IntPtr.Zero);
		Console.WriteLine($"host dpi (Graphics.FromHwnd) = {g.DpiX}");

		MechrevoDeviceCapabilities caps = new MechrevoDeviceCapabilities
		{
			ProfileAvailable = true,
			TurboMode = true,
			TurboSubMode = true,
			CpuPerformanceTuning = true,
			FanSettings = true,
			LiquidCooling = true,
			DisplayRefresh = true,
			ColorCalibration = true,
			LcdOverdrive = true,
			DgpuDirect = true,
			IgpuOnly = true,
			Keyboard = true,
			Lightbar = true,
			LogoLight = true
		};
		SetStatic("MechrevoLite.Program", "UiAuditUseReportedCapabilities", true);
		MechrevoHw hw = MakeHw(caps);
		SetStatic("MechrevoLite.Program", "hw", hw);

		SettingsForm form = new SettingsForm();
		form.ShowInTaskbar = false;
		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(20, 20);
		form.Opacity = 0.01;
		form.Show();
		Application.DoEvents();
		Console.WriteLine($"after Show: DeviceDpi={form.DeviceDpi} Width={form.Width} Height={form.Height} Client={form.ClientSize} MinimumSize={form.MinimumSize}");
		Console.WriteLine($"  CurrentAutoScaleDimensions={form.CurrentAutoScaleDimensions} AutoScaleDimensions={form.AutoScaleDimensions} AutoScaleMode={form.AutoScaleMode}");
		Console.WriteLine($"  required logical client = {Invoke(form, "RequiredDashboardLogicalWidthForAudit")}");
		Console.WriteLine($"  detail = {Invoke(form, "DescribeRequiredDashboardWidthForAudit")}");
		Invoke(form, "RefreshDashboardMinimumWidth");
		Application.DoEvents();
		Console.WriteLine($"after RefreshDashboardMinimumWidth: Width={form.Width} Client={form.ClientSize} MinimumSize={form.MinimumSize}");
		Invoke(form, "ApplyResponsiveBounds", Screen.FromControl(form).WorkingArea);
		Application.DoEvents();
		Console.WriteLine($"after ApplyResponsiveBounds: Width={form.Width} Client={form.ClientSize} MinimumSize={form.MinimumSize}");
		DumpRows(form);
		Environment.Exit(0);
	}

	private static void DumpRows(Form form)
	{
		foreach (string name in new string[] { "perfHeaderRow", "perfModeRow", "gpuHeaderRow", "gpuModeRow", "panelCPUTitle", "panelPerformance" })
		{
			Control c = Find(form, name);
			if (c == null)
			{
				Console.WriteLine($"  {name} = MISSING");
				continue;
			}
			Console.WriteLine($"  {name} bounds={c.Bounds} client={c.ClientSize} parent={c.Parent?.Name}");
			if (name.EndsWith("HeaderRow"))
			{
				foreach (Control child in c.Controls)
				{
					if (child is TableLayoutPanel left && left.Name == name + "Left")
					{
						int neededRight = 0;
						foreach (Control gc in left.Controls)
						{
							if (gc.Visible)
							{
								neededRight = Math.Max(neededRight, gc.Bounds.Right + gc.Margin.Right);
							}
						}
						Console.WriteLine($"    左组 {left.Bounds} 内容右缘={neededRight} ({(neededRight <= left.ClientSize.Width + 1 ? "PASS" : "FAIL 被裁")})");
					}
					else if (child is Label label)
					{
						Size need = TextRenderer.MeasureText(label.Text ?? "", label.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
						Console.WriteLine($"    读数 '{label.Text}' {label.Bounds} AutoSize={label.AutoSize} AutoEllipsis={label.AutoEllipsis} 文本宽={need.Width} 完整可见={(need.Width <= label.ClientSize.Width ? "PASS" : "FAIL")}");
					}
				}
			}
		}
	}

	private static MechrevoHw MakeHw(MechrevoDeviceCapabilities caps)
	{
		foreach (ConstructorInfo ctor in typeof(MechrevoHw).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			ParameterInfo[] ps = ctor.GetParameters();
			if (ps.Length == 2 && ps[1].ParameterType == typeof(MechrevoDeviceCapabilities))
			{
				return (MechrevoHw)ctor.Invoke(new object[2] { null, caps });
			}
		}
		return null;
	}

	private static Control Find(Control root, string name)
	{
		if (root == null)
		{
			return null;
		}
		if (root.Name == name)
		{
			return root;
		}
		foreach (Control c in root.Controls)
		{
			Control r = Find(c, name);
			if (r != null)
			{
				return r;
			}
		}
		return null;
	}

	private static void SetStatic(string typeName, string member, object value)
	{
		Type t = Asm.GetType(typeName);
		PropertyInfo pi = t?.GetProperty(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (pi != null)
		{
			pi.SetValue(null, value);
			return;
		}
		t?.GetField(member, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, value);
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (m.Name == name && m.GetParameters().Length == args.Length)
			{
				return m.Invoke(target, args);
			}
		}
		return "METHOD-NOT-FOUND";
	}
}
