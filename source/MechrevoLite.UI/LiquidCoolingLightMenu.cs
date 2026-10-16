using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MechrevoLite.UI;

internal sealed class LiquidCoolingLightMenu : CustomContextMenu
{
	public const string HeaderTag = "lc-header";

	private readonly List<ToolStripMenuItem> _fanLedItems = new List<ToolStripMenuItem>();

	public IReadOnlyList<ToolStripMenuItem> FanLedItems => _fanLedItems;

	public LiquidCoolingLightMenu()
	{
		ShowImageMargin = false;
		Renderer = new LiquidCoolingMenuRenderer();
		ApplyTheme();
	}

	public void AddHeader(string text)
	{
		Items.Add(new ToolStripMenuItem(text)
		{
			Tag = "lc-header",
			Enabled = false,
			AutoSize = true,
			Padding = new Padding(0, 4, 0, 4)
		});
	}

	public ToolStripMenuItem AddItem(string text, Action onClick, bool requiresFanLed = false)
	{
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(text)
		{
			AutoSize = true,
			Padding = new Padding(0, 7, 0, 7)
		};
		toolStripMenuItem.Click += (object? _, EventArgs _) =>
		{
			onClick();
		};
		Items.Add(toolStripMenuItem);
		if (requiresFanLed)
		{
			_fanLedItems.Add(toolStripMenuItem);
		}
		return toolStripMenuItem;
	}

	public void AddSeparator()
	{
		Items.Add(new ToolStripSeparator());
	}

	public void ApplyTheme()
	{
		BackColor = UiVisualStyle.Input;
		ForeColor = UiVisualStyle.Text;
		Invalidate();
	}
}
