using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal static class UiDpi
{
	public const int Baseline = 96;

	internal static int AuditDpi { get; set; }

	internal static int Layout(Control control)
	{
		return Math.Max(96, control.DeviceDpi);
	}

	internal static float LayoutScale(Control control)
	{
		return Math.Max(1f, (float)Layout(control) / 96f);
	}

	internal static float Paint(Graphics graphics)
	{
		if (AuditDpi <= 0)
		{
			return Math.Max(96f, graphics.DpiX);
		}
		return AuditDpi;
	}

	internal static float Paint(Control control)
	{
		return (AuditDpi > 0) ? AuditDpi : Math.Max(96, control.DeviceDpi);
	}
}
